using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates runtime skill-animation bindings without changing the frozen
/// unit-catalog-v1 or ability-catalog-v1 transport schemas.
/// </summary>
public static class SkillAnimationCatalogGenerator
{
    private const int TicksPerSecond = 20;
    private const string UnitSourceDirectory =
        "Assets/GameData/Units/EliteVariants/Json";
    private const string AbilitySourceDirectory =
        "Assets/GameData/Abilities/Json";
    private const string OutputPath =
        "Assets/Resources/BattleData/skill-animation-catalog-v1.json";

    [MenuItem("ARKnoNIGHTS/Battle/Regenerate Skill Animation Catalog v1")]
    public static void Generate()
    {
        Generate(UnitSourceDirectory, AbilitySourceDirectory, OutputPath);
    }

    private static void Generate(
        string unitSourceDirectory,
        string abilitySourceDirectory,
        string outputPath)
    {
        var unitSources = UnitEliteVariantResolver.LoadDirectory(
            unitSourceDirectory);
        if (unitSources.Count == 0)
            throw new InvalidOperationException(
                "SKILL_ANIMATION_UNIT_SOURCE_EMPTY path="
                + unitSourceDirectory);

        var abilitySources = LoadAbilitySources(abilitySourceDirectory);
        var bindings = new List<SkillAnimationCatalogEntry>();
        var boundAbilityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in unitSources.OrderBy(item => item.Key))
        {
            var unit = UnitEliteVariantResolver.Resolve(pair.Value, 0);
            foreach (var abilityId in unit.innateAbilityIds
                         .OrderBy(item => item, StringComparer.Ordinal))
            {
                if (!abilitySources.TryGetValue(
                        abilityId,
                        out var ability))
                    throw new InvalidOperationException(
                        "SKILL_ANIMATION_ABILITY_SOURCE_MISSING typeId="
                        + unit.typeId
                        + " abilityId="
                        + abilityId);
                var animationKeys =
                    ability.animationKeys != null
                    && ability.animationKeys.Length > 0
                        ? ability.animationKeys
                        : string.IsNullOrWhiteSpace(
                            ability.animationKey)
                            ? Array.Empty<string>()
                            : new[] { ability.animationKey };
                if (animationKeys.Length == 0)
                    continue;
                if (animationKeys.Any(
                        string.IsNullOrWhiteSpace)
                    || animationKeys.Any(key =>
                        key.Contains("|"))
                    || (!string.Equals(
                            ability.activationKind,
                            "Timed",
                            StringComparison.Ordinal)
                        && !string.Equals(
                            ability.activationKind,
                            "Passive",
                            StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "SKILL_ANIMATION_KEY_MISSING abilityId="
                        + abilityId);
                if (!boundAbilityIds.Add(abilityId))
                    throw new InvalidOperationException(
                        "SKILL_ANIMATION_ABILITY_DUPLICATE abilityId="
                        + abilityId);

                var animations = animationKeys
                    .Select(unit.FindAnimation)
                    .ToArray();
                if (animations.Any(animation =>
                        animation == null
                        || string.IsNullOrWhiteSpace(
                            animation.name)
                        || animation.name.Contains("|")
                        || animation.durationSeconds <= 0f))
                    throw new InvalidOperationException(
                        "SKILL_ANIMATION_BINDING_INVALID typeId="
                        + unit.typeId
                        + " abilityId="
                        + abilityId
                        + " animationKey="
                        + string.Join("|", animationKeys));
                bindings.Add(new SkillAnimationCatalogEntry
                {
                    typeId = unit.typeId.ToString(
                        CultureInfo.InvariantCulture),
                    abilityId = abilityId,
                    animationKey = string.Join(
                        "|",
                        animationKeys),
                    animationName = string.Join(
                        "|",
                        animations.Select(item =>
                            item.name)),
                    originalAnimationTicks = Mathf.CeilToInt(
                        animations.Sum(item =>
                            item.durationSeconds)
                        * TicksPerSecond),
                    segmentOriginalAnimationTicks =
                        animations.Select(item =>
                                Mathf.CeilToInt(
                                    item.durationSeconds
                                    * TicksPerSecond))
                            .ToArray()
                });
            }
        }

        if (bindings.Count == 0)
            throw new InvalidOperationException(
                "SKILL_ANIMATION_BINDING_EMPTY");
        var document = new SkillAnimationCatalogDocument
        {
            schemaVersion = "skill-animation-catalog-v1",
            catalogId = "mainline-skill-animations",
            bindings = bindings
                .OrderBy(item => item.abilityId, StringComparer.Ordinal)
                .ToArray()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(
            outputPath,
            JsonUtility.ToJson(document, true) + "\n",
            new UTF8Encoding(false));
        AssetDatabase.ImportAsset(
            outputPath,
            ImportAssetOptions.ForceUpdate);
        Debug.Log(
            "SKILL_ANIMATION_CATALOG_GENERATED path="
            + outputPath
            + " bindings="
            + bindings.Count
            + " summary="
            + string.Join(
                "|",
                bindings.Select(item =>
                    item.typeId
                    + ":"
                    + item.abilityId
                    + ":"
                    + item.originalAnimationTicks)));
    }

    private static IReadOnlyDictionary<string, AbilityAnimationSource>
        LoadAbilitySources(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new InvalidOperationException(
                "SKILL_ANIMATION_ABILITY_SOURCE_MISSING path="
                + sourceDirectory);
        var result = new Dictionary<string, AbilityAnimationSource>(
            StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(
                         sourceDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(
                         path => Path.GetFileName(path),
                         StringComparer.Ordinal))
        {
            AbilityAnimationSource source;
            try
            {
                source = JsonUtility.FromJson<AbilityAnimationSource>(
                    File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "SKILL_ANIMATION_ABILITY_SOURCE_INVALID path="
                    + path,
                    exception);
            }
            if (source == null
                || !string.Equals(
                    source.schemaVersion,
                    "ability-source-v1",
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(source.abilityId)
                || result.ContainsKey(source.abilityId))
                throw new InvalidOperationException(
                    "SKILL_ANIMATION_ABILITY_SOURCE_INVALID path="
                    + path);
            result.Add(source.abilityId, source);
        }

        return result;
    }

    [Serializable]
    private sealed class SkillAnimationCatalogDocument
    {
        public string schemaVersion;
        public string catalogId;
        public SkillAnimationCatalogEntry[] bindings;
    }

    [Serializable]
    private sealed class SkillAnimationCatalogEntry
    {
        public string typeId;
        public string abilityId;
        public string animationKey;
        public string animationName;
        public int originalAnimationTicks;
        public int[] segmentOriginalAnimationTicks;
    }

    [Serializable]
    private sealed class AbilityAnimationSource
    {
        public string schemaVersion;
        public string abilityId;
        public string activationKind;
        public string animationKey;
        public string[] animationKeys;
    }
}
