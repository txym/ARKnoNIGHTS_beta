using System;
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
    private const string OutputPath = "Assets/Resources/BattleData/ability-catalog-v1.json";

    [MenuItem("ARKnoNIGHTS/Battle/Regenerate Ability Catalog v1")]
    public static void Generate()
    {
        if (!Directory.Exists(SourceDirectory)) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_MISSING path=" + SourceDirectory);
        var sources = Directory.GetFiles(SourceDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal).Select(Convert).OrderBy(ability => ability.abilityId, StringComparer.Ordinal).ToArray();
        if (sources.Length == 0) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_EMPTY path=" + SourceDirectory);
        if (sources.Select(ability => ability.abilityId).Distinct(StringComparer.Ordinal).Count() != sources.Length) throw new InvalidOperationException("ABILITY_CATALOG_ID_DUPLICATE");

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllText(OutputPath, JsonUtility.ToJson(new AbilityCatalogDocument { schemaVersion = "ability-catalog-v1", catalogId = "mainline-abilities", abilities = sources }, true) + "\n", new UTF8Encoding(false));
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("ABILITY_CATALOG_GENERATED path=" + OutputPath + " abilities=" + sources.Length + " summary=" + string.Join("|", sources.Select(ability => ability.abilityId)));
    }

    private static AbilityCatalogEntry Convert(string sourcePath)
    {
        AbilitySource source;
        try { source = JsonUtility.FromJson<AbilitySource>(File.ReadAllText(sourcePath)); }
        catch (Exception exception) { throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_INVALID path=" + sourcePath, exception); }
        if (source == null || !string.Equals(source.schemaVersion, "ability-source-v1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(source.abilityId)) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_REQUIRED_MISSING path=" + sourcePath);
        if (!Enum.IsDefined(typeof(AbilityActivationKind), source.activationKind) || !Enum.IsDefined(typeof(SilencePolicy), source.silencePolicy) || source.skillPoints == null || !Enum.IsDefined(typeof(SkillPointGeneration), source.skillPoints.generation) || source.skillPoints.required <= 0 || source.skillPoints.initial < 0 || source.skillPoints.initial > source.skillPoints.required) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_SKILL_POINTS_INVALID path=" + sourcePath);
        var effect = source.effects == null ? null : source.effects.SingleOrDefault(item => item != null && item.kind == "Summon");
        if (effect == null || string.IsNullOrWhiteSpace(effect.summonTypeId) || effect.count <= 0 || effect.spawnArea == null || effect.spawnArea.shape != "Square" || effect.spawnArea.center != "CasterPosition" || effect.spawnArea.sideLengthMetres <= 0f) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_SUMMON_INVALID path=" + sourcePath);
        if (source.abilityId == "SUMMON_JELLY_MINIONS" && effect.inheritPathFromCaster) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_INHERIT_PATH_INVALID path=" + sourcePath);

        var centimetres = Mathf.RoundToInt(effect.spawnArea.sideLengthMetres * 100f);
        if (Mathf.Abs(effect.spawnArea.sideLengthMetres * 100f - centimetres) > 0.0001f) throw new InvalidOperationException("ABILITY_CATALOG_SOURCE_SIDE_LENGTH_NOT_EXACT path=" + sourcePath);
        return new AbilityCatalogEntry { abilityId = source.abilityId, displayNameZhHans = source.displayNameZhHans ?? string.Empty, descriptionZhHans = source.descriptionZhHans ?? string.Empty, activationKind = source.activationKind, silencePolicy = source.silencePolicy, initialSkillPoints = source.skillPoints.initial, requiredSkillPoints = source.skillPoints.required, skillPointGeneration = source.skillPoints.generation, summonTypeId = effect.summonTypeId, count = effect.count, sideLengthCentimetres = centimetres, inheritPathFromCaster = effect.inheritPathFromCaster };
    }

    [Serializable] private sealed class AbilityCatalogDocument { public string schemaVersion; public string catalogId; public AbilityCatalogEntry[] abilities; }
    [Serializable] private sealed class AbilityCatalogEntry { public string abilityId; public string displayNameZhHans; public string descriptionZhHans; public string activationKind; public string silencePolicy; public int initialSkillPoints; public int requiredSkillPoints; public string skillPointGeneration; public string summonTypeId; public int count; public int sideLengthCentimetres; public bool inheritPathFromCaster; }
    [Serializable] private sealed class AbilitySource { public string schemaVersion; public string abilityId; public string displayNameZhHans; public string descriptionZhHans; public string activationKind; public string silencePolicy; public SkillPoints skillPoints; public SummonEffect[] effects; }
    [Serializable] private sealed class SkillPoints { public int initial; public int required; public string generation; }
    [Serializable] private sealed class SummonEffect { public string kind; public string summonTypeId; public int count; public SpawnArea spawnArea; public bool inheritPathFromCaster; }
    [Serializable] private sealed class SpawnArea { public string shape; public string center; public float sideLengthMetres; }
}
