using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ArknoNights.Battle.Core;
using UnityEditor;
using UnityEngine;

/// <summary>Deterministically generates the Player-safe ability catalog from authored ability sources.</summary>
public static class AbilityCatalogGenerator
{
    private const string SourceDirectory = "Assets/GameData/Abilities/Json";
    private const string UnitSourceDirectory =
        "Assets/GameData/Units/EliteVariants/Json";
    private const string OutputPath = "Assets/Resources/BattleData/ability-catalog-v1.json";

    [MenuItem("ARKnoNIGHTS/Battle/Regenerate Ability Catalog v1")]
    public static void Generate()
    {
        Generate(SourceDirectory, OutputPath);
    }

    private static void Generate(string sourceDirectory, string outputPath)
    {
        if (!Directory.Exists(sourceDirectory)) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_MISSING path=" + sourceDirectory);
        var knownUnitTypeIds = LoadKnownUnitTypeIds();
        var sources = Directory.GetFiles(sourceDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal).Select(path => Convert(path, knownUnitTypeIds)).OrderBy(ability => ability.abilityId, StringComparer.Ordinal).ToArray();
        if (sources.Length == 0) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_EMPTY path=" + sourceDirectory);
        if (sources.Select(ability => ability.abilityId).Distinct(StringComparer.Ordinal).Count() != sources.Length) throw new InvalidOperationException("ABILITY_CATALOG_ID_DUPLICATE");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, JsonUtility.ToJson(new AbilityCatalogDocument { schemaVersion = "ability-catalog-v1", catalogId = "mainline-abilities", abilities = sources }, true) + "\n", new UTF8Encoding(false));
        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("ABILITY_CATALOG_GENERATED path=" + outputPath + " abilities=" + sources.Length + " summary=" + string.Join("|", sources.Select(ability => ability.abilityId)));
    }

    private static AbilityCatalogEntry Convert(string sourcePath, ISet<string> knownUnitTypeIds)
    {
        AbilitySource source;
        try { source = JsonUtility.FromJson<AbilitySource>(File.ReadAllText(sourcePath)); }
        catch (Exception exception) { throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_INVALID path=" + sourcePath, exception); }
        if (source == null || !string.Equals(source.schemaVersion, "ability-source-v1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(source.abilityId)) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_REQUIRED_MISSING path=" + sourcePath);
        if (!Enum.IsDefined(typeof(AbilityActivationKind), source.activationKind)
            || !Enum.IsDefined(typeof(SilencePolicy), source.silencePolicy)
            || source.skillPoints == null
            || !Enum.IsDefined(typeof(SkillPointGeneration), source.skillPoints.generation))
            throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_SKILL_POINTS_INVALID path=" + sourcePath);
        var activationKind = (AbilityActivationKind)Enum.Parse(
            typeof(AbilityActivationKind),
            source.activationKind);
        if ((activationKind == AbilityActivationKind.Timed
                && (source.skillPoints.required <= 0
                    || source.skillPoints.initial < 0
                    || source.skillPoints.initial
                    > source.skillPoints.required
                    || source.skillPoints.generation
                    != SkillPointGeneration.Automatic.ToString()))
            || (activationKind == AbilityActivationKind.Passive
                && (source.skillPoints.required != 0
                    || source.skillPoints.initial != 0
                    || source.skillPoints.generation
                    != SkillPointGeneration.None.ToString())))
            throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_SKILL_POINTS_INVALID path=" + sourcePath);

        var effects = (source.effects ?? Array.Empty<AbilityEffect>())
            .Where(item => item != null)
            .ToArray();
        if (effects.Length != 1)
            throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_EFFECT_INVALID path=" + sourcePath);
        var effect = effects[0];
        var entry = new AbilityCatalogEntry
        {
            abilityId = source.abilityId,
            displayNameZhHans = source.displayNameZhHans ?? string.Empty,
            descriptionZhHans = source.descriptionZhHans ?? string.Empty,
            activationKind = source.activationKind,
            silencePolicy = source.silencePolicy,
            initialSkillPoints = source.skillPoints.initial,
            requiredSkillPoints = source.skillPoints.required,
            skillPointGeneration = source.skillPoints.generation
        };
        if (effect.kind == "UnitTrait")
        {
            if (activationKind != AbilityActivationKind.Passive
                || !Enum.TryParse(
                    effect.trait,
                    true,
                    out UnitTraitEffectKind trait)
                || !Enum.IsDefined(typeof(UnitTraitEffectKind), trait))
                throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_TRAIT_INVALID path=" + sourcePath);
            entry.unitTrait = trait.ToString();
            return entry;
        }
        if (effect.kind == "OnHitDefenseDebuff")
        {
            if (activationKind != AbilityActivationKind.Passive
                || effect.defenseReductionPerStack <= 0)
                throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_ON_HIT_DEFENSE_DEBUFF_INVALID path=" + sourcePath);
            entry.onHitDefenseReductionPerStack =
                effect.defenseReductionPerStack;
            return entry;
        }
        if (effect.kind != "Summon"
            || activationKind != AbilityActivationKind.Timed
            || string.IsNullOrWhiteSpace(effect.summonTypeId)
            || effect.count <= 0
            || effect.spawnArea == null
            || effect.spawnArea.shape != "Square"
            || effect.spawnArea.center != "CasterPosition"
            || effect.spawnArea.sideLengthMetres < 0f)
            throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_SUMMON_INVALID path=" + sourcePath);
        if (!knownUnitTypeIds.Contains(effect.summonTypeId)) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_SUMMON_TYPE_UNKNOWN path=" + sourcePath + " typeId=" + effect.summonTypeId);
        if (source.abilityId == "SUMMON_JELLY_MINIONS" && effect.inheritPathFromCaster) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_INHERIT_PATH_INVALID path=" + sourcePath);

        var centimetres = Mathf.RoundToInt(effect.spawnArea.sideLengthMetres * 100f);
        if (Mathf.Abs(effect.spawnArea.sideLengthMetres * 100f - centimetres) > 0.0001f) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_SIDE_LENGTH_NOT_EXACT path=" + sourcePath);
        entry.summonTypeId = effect.summonTypeId;
        entry.count = effect.count;
        entry.sideLengthCentimetres = centimetres;
        entry.inheritPathFromCaster = effect.inheritPathFromCaster;
        return entry;
    }

    private static ISet<string> LoadKnownUnitTypeIds()
    {
        var sources = UnitEliteVariantResolver.LoadDirectory(UnitSourceDirectory);
        if (sources.Count == 0)
            throw new InvalidOperationException(
                "ABILITY_CATALOG_UNIT_SOURCE_EMPTY path=" + UnitSourceDirectory);

        return new HashSet<string>(
            sources.Keys.Select(id =>
                id.ToString(CultureInfo.InvariantCulture)),
            StringComparer.Ordinal);
    }

    [Serializable] private sealed class AbilityCatalogDocument { public string schemaVersion; public string catalogId; public AbilityCatalogEntry[] abilities; }
    [Serializable] private sealed class AbilityCatalogEntry { public string abilityId; public string displayNameZhHans; public string descriptionZhHans; public string activationKind; public string silencePolicy; public int initialSkillPoints; public int requiredSkillPoints; public string skillPointGeneration; public string summonTypeId; public int count; public int sideLengthCentimetres; public bool inheritPathFromCaster; public string unitTrait; public int onHitDefenseReductionPerStack; }
    [Serializable] private sealed class AbilitySource { public string schemaVersion; public string abilityId; public string displayNameZhHans; public string descriptionZhHans; public string activationKind; public string silencePolicy; public SkillPoints skillPoints; public AbilityEffect[] effects; }
    [Serializable] private sealed class SkillPoints { public int initial; public int required; public string generation; }
    [Serializable] private sealed class AbilityEffect { public string kind; public string trait; public string summonTypeId; public int count; public SpawnArea spawnArea; public bool inheritPathFromCaster; public int defenseReductionPerStack; }
    [Serializable] private sealed class SpawnArea { public string shape; public string center; public float sideLengthMetres; }
}
