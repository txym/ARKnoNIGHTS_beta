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
        var boundAbilityTypeIds =
            new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in unitSources.OrderBy(item => item.Key))
        {
            foreach (var eliteLevel in Enumerable.Range(0, 4))
            {
                var unit = UnitEliteVariantResolver.Resolve(
                    pair.Value,
                    eliteLevel);
                foreach (var abilityId in unit.innateAbilityIds
                             .OrderBy(item => item, StringComparer.Ordinal))
                {
                    if (boundAbilityTypeIds.TryGetValue(
                            abilityId,
                            out var boundTypeId))
                    {
                        if (boundTypeId != unit.typeId)
                            throw new InvalidOperationException(
                                "SKILL_ANIMATION_ABILITY_DUPLICATE abilityId="
                                + abilityId);
                        continue;
                    }

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
                    var presentationStateTag =
                        ability.persistentPresentationStateTag
                        ?? string.Empty;
                    if (animationKeys.Length == 0
                        && string.IsNullOrWhiteSpace(
                            presentationStateTag))
                    {
                        continue;
                    }
                    if ((animationKeys.Length > 0
                         && (animationKeys.Any(
                            string.IsNullOrWhiteSpace)
                        || animationKeys.Any(key =>
                            key.Contains("|"))))
                        || (!string.IsNullOrEmpty(
                                presentationStateTag)
                            && (string.IsNullOrWhiteSpace(
                                    presentationStateTag)
                                || presentationStateTag.Contains("|")))
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
                    var stateIdle = string.IsNullOrWhiteSpace(
                            presentationStateTag)
                        ? null
                        : unit.FindAnimation(
                            "idle." + presentationStateTag);
                    var stateMove = string.IsNullOrWhiteSpace(
                            presentationStateTag)
                        ? null
                        : unit.FindAnimation(
                            "move." + presentationStateTag);
                    var stateAttack = string.IsNullOrWhiteSpace(
                            presentationStateTag)
                        ? null
                        : unit.FindAnimation(
                            "attack." + presentationStateTag);
                    var stateDeath = string.IsNullOrWhiteSpace(
                            presentationStateTag)
                        ? null
                        : unit.FindAnimation(
                            "death." + presentationStateTag);
                    if (!string.IsNullOrWhiteSpace(
                            presentationStateTag)
                        && (stateIdle == null
                            || stateMove == null
                            || stateAttack == null
                            || stateDeath == null
                            || string.IsNullOrWhiteSpace(stateIdle.name)
                            || string.IsNullOrWhiteSpace(stateMove.name)
                            || string.IsNullOrWhiteSpace(stateAttack.name)
                            || string.IsNullOrWhiteSpace(stateDeath.name)))
                        throw new InvalidOperationException(
                            "SKILL_ANIMATION_PRESENTATION_STATE_INVALID typeId="
                            + unit.typeId
                            + " abilityId="
                            + abilityId
                            + " state="
                            + presentationStateTag);
                    boundAbilityTypeIds.Add(
                        abilityId,
                        unit.typeId);
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
                        originalAnimationTicks =
                            animations.Length == 0
                                ? 0
                                : Mathf.CeilToInt(
                                    animations.Sum(item =>
                                        item.durationSeconds)
                                    * TicksPerSecond),
                        segmentOriginalAnimationTicks =
                            animations.Select(item =>
                                    Mathf.CeilToInt(
                                        item.durationSeconds
                                        * TicksPerSecond))
                                .ToArray(),
                        presentationStateTag =
                            presentationStateTag,
                        stateIdleAnimation = stateIdle == null
                            ? string.Empty
                            : stateIdle.name,
                        stateMoveAnimation = stateMove == null
                            ? string.Empty
                            : stateMove.name,
                        stateAttackAnimation = stateAttack == null
                            ? string.Empty
                            : stateAttack.name,
                        stateDeathAnimation = stateDeath == null
                            ? string.Empty
                            : stateDeath.name
                    });
                }
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
        public string presentationStateTag;
        public string stateIdleAnimation;
        public string stateMoveAnimation;
        public string stateAttackAnimation;
        public string stateDeathAnimation;
    }

    [Serializable]
    private sealed class AbilityAnimationSource
    {
        public string schemaVersion;
        public string abilityId;
        public string activationKind;
        public string animationKey;
        public string[] animationKeys;
        public string persistentPresentationStateTag;
    }
}
