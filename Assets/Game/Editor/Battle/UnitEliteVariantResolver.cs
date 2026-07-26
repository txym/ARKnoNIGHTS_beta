using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

internal sealed class UnitEliteVariantSource
{
    internal UnitEliteVariantSource(int typeId, string path, string json)
    {
        TypeId = typeId;
        Path = path;
        Json = json;
    }

    internal int TypeId { get; }
    internal string Path { get; }
    internal string Json { get; }
}

internal static class UnitEliteVariantResolver
{
    private const string SchemaVersion = "unit-elite-variants-v1";

    internal static IReadOnlyDictionary<int, UnitEliteVariantSource> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new Dictionary<int, UnitEliteVariantSource>();
        }

        var result = new Dictionary<int, UnitEliteVariantSource>();
        foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(item => Path.GetFileName(item), StringComparer.Ordinal))
        {
            var json = File.ReadAllText(path);
            var document = ParseDocument(json, path);
            if (result.ContainsKey(document.typeId))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_TYPEID_DUPLICATE typeId=" + document.typeId);
            }

            result.Add(document.typeId, new UnitEliteVariantSource(document.typeId, path, json));
        }

        return result;
    }

    internal static UnitJson Resolve(
        string unitSourceJson,
        string variantSourceJson,
        int eliteLevel,
        string context)
    {
        if (eliteLevel < 0 || eliteLevel > 3)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_TARGET_INVALID context=" + context + " eliteLevel=" + eliteLevel);
        }

        UnitJson source;
        try
        {
            source = JsonUtility.FromJson<UnitJson>(unitSourceJson);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_BASE_INVALID context=" + context,
                exception);
        }

        if (source == null)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_BASE_INVALID context=" + context);
        }

        var document = ParseDocument(variantSourceJson, context);
        if (document.typeId != source.typeId)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_TYPEID_MISMATCH context=" + context
                + " sourceTypeId=" + source.typeId
                + " variantTypeId=" + document.typeId);
        }

        foreach (var variant in document.variants
                     .OrderBy(item => item.minEliteLevel)
                     .Where(item => item.minEliteLevel <= eliteLevel))
        {
            Apply(source, variant);
        }

        return source;
    }

    private static UnitEliteVariantsDocument ParseDocument(string json, string context)
    {
        UnitEliteVariantsDocument document;
        try
        {
            document = JsonUtility.FromJson<UnitEliteVariantsDocument>(json);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_JSON_INVALID context=" + context,
                exception);
        }

        ValidateDocument(document, context);
        return document;
    }

    private static void ValidateDocument(UnitEliteVariantsDocument document, string context)
    {
        if (document == null
            || !string.Equals(document.schemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_SCHEMA_INVALID context=" + context);
        }

        if (document.typeId <= 0
            || document.variants == null
            || document.variants.Length == 0)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context);
        }

        var ordered = document.variants
            .OrderBy(item => item == null ? int.MinValue : item.minEliteLevel)
            .ToArray();
        if (ordered[0] == null || ordered[0].minEliteLevel != 0)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context);
        }

        var levels = new HashSet<int>();
        foreach (var variant in ordered)
        {
            if (variant == null
                || variant.minEliteLevel < 0
                || variant.minEliteLevel > 3)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_LEVEL_INVALID context=" + context);
            }

            if (!levels.Add(variant.minEliteLevel))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_LEVEL_DUPLICATE context=" + context
                    + " eliteLevel=" + variant.minEliteLevel);
            }

            if (variant.statsLevel != 0)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_STATS_LEVEL_INVALID context=" + context
                    + " eliteLevel=" + variant.minEliteLevel);
            }

            if (string.IsNullOrWhiteSpace(variant.sourceVariant))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context
                    + " eliteLevel=" + variant.minEliteLevel);
            }

            ValidateStats(variant.stats, context, variant.minEliteLevel);
            ValidateModel(
                document.typeId,
                variant.sourceVariant,
                variant.model,
                context,
                variant.minEliteLevel);

            if (variant.minEliteLevel == 0
                && (variant.displayNameZhHans == null
                    || !IsCombatDeclared(variant.stats)
                    || !IsSharedDeclared(variant.stats)
                    || !IsModelDeclared(variant.model)
                    || variant.innateAbilityIds == null))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_BASE_INCOMPLETE context=" + context);
            }
        }
    }

    private static void ValidateStats(
        UnitEliteVariantStats stats,
        string context,
        int eliteLevel)
    {
        if (stats == null)
        {
            return;
        }

        if (IsCombatDeclared(stats)
            && (stats.combat.maxHitPoints <= 0
                || stats.combat.attack < 0
                || stats.combat.defense < 0
                || stats.combat.magicResistance < 0
                || stats.combat.magicResistance > 100))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_COMBAT_INVALID context=" + context
                + " eliteLevel=" + eliteLevel);
        }

        if (IsSharedDeclared(stats)
            && (stats.shared.moveSpeedMetresPerSecond <= 0f
                || stats.shared.attackIntervalSeconds <= 0f
                || stats.shared.lifeDeduct < 0))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_SHARED_INVALID context=" + context
                + " eliteLevel=" + eliteLevel);
        }
    }

    private static bool IsCombatDeclared(UnitEliteVariantStats stats)
    {
        return stats != null
            && stats.combat != null
            && (stats.combat.maxHitPoints != 0
                || stats.combat.attack != 0
                || stats.combat.defense != 0
                || stats.combat.magicResistance != 0);
    }

    private static bool IsSharedDeclared(UnitEliteVariantStats stats)
    {
        return stats != null
            && stats.shared != null
            && (stats.shared.moveSpeedMetresPerSecond != 0f
                || stats.shared.attackIntervalSeconds != 0f
                || stats.shared.lifeDeduct != 0);
    }

    private static void ValidateModel(
        int typeId,
        string sourceVariant,
        UnitEliteVariantModel model,
        string context,
        int eliteLevel)
    {
        if (!IsModelDeclared(model))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(model.resourceFolderName)
            || string.IsNullOrWhiteSpace(model.resourceKey)
            || string.IsNullOrWhiteSpace(model.skeletonDataResourceName)
            || string.IsNullOrWhiteSpace(model.profilePictureResourceName)
            || model.unitSkeletonType <= 0
            || model.attackAnimationDurationSeconds <= 0f
            || string.IsNullOrWhiteSpace(model.moveAnimation)
            || string.IsNullOrWhiteSpace(model.attackAnimation)
            || model.hitAnimation == null
            || string.IsNullOrWhiteSpace(model.deathAnimation))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_MODEL_INCOMPLETE context=" + context
                + " eliteLevel=" + eliteLevel);
        }

        var expectedFolder =
            UnitResourcePaths.BuildCharacterFolderName(typeId, model.resourceKey);
        var expectedPortrait =
            UnitResourcePaths.BuildProfilePictureResourceName(typeId, model.resourceKey);
        if (!string.Equals(
                model.resourceFolderName,
                expectedFolder,
                StringComparison.Ordinal)
            || !string.Equals(
                model.profilePictureResourceName,
                expectedPortrait,
                StringComparison.Ordinal)
            || !string.Equals(
                sourceVariant,
                model.resourceFolderName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_MODEL_CANONICAL_NAME_INVALID context=" + context
                + " eliteLevel=" + eliteLevel);
        }
    }

    private static bool IsModelDeclared(UnitEliteVariantModel model)
    {
        return model != null
            && (model.resourceFolderName != null
                || model.resourceKey != null
                || model.skeletonDataResourceName != null
                || model.profilePictureResourceName != null
                || model.unitSkeletonType != 0
                || model.attackAnimationDurationSeconds != 0f
                || model.moveAnimation != null
                || model.attackAnimation != null
                || model.hitAnimation != null
                || model.deathAnimation != null);
    }

    private static void Apply(UnitJson source, UnitEliteVariantPatch variant)
    {
        if (variant.displayNameZhHans != null)
        {
            source.displayNameZhHans = variant.displayNameZhHans;
        }

        if (IsCombatDeclared(variant.stats))
        {
            source.maxHitPoints = variant.stats.combat.maxHitPoints;
            source.attack = variant.stats.combat.attack;
            source.defense = variant.stats.combat.defense;
            source.magicResistance = variant.stats.combat.magicResistance;
        }

        if (IsSharedDeclared(variant.stats))
        {
            source.moveSpeedMetresPerSecond = variant.stats.shared.moveSpeedMetresPerSecond;
            source.attackIntervalSeconds = variant.stats.shared.attackIntervalSeconds;
            source.lifeDeduct = variant.stats.shared.lifeDeduct;
        }

        if (IsModelDeclared(variant.model))
        {
            source.resourceKey = variant.model.resourceKey;
            source.skeletonDataResourceName = variant.model.skeletonDataResourceName;
            source.profilePictureResourceName = variant.model.profilePictureResourceName;
            source.unitSkeletonType = variant.model.unitSkeletonType;
            source.attackAnimationDurationSeconds = variant.model.attackAnimationDurationSeconds;
            source.moveAnimation = variant.model.moveAnimation;
            source.attackAnimation = variant.model.attackAnimation;
            source.hitAnimation = variant.model.hitAnimation;
            source.deathAnimation = variant.model.deathAnimation;
        }

        if (variant.innateAbilityIds != null)
        {
            source.innateAbilityIds = new List<string>(variant.innateAbilityIds);
        }
    }
}

[Serializable]
internal sealed class UnitEliteVariantsDocument
{
    public string schemaVersion;
    public int typeId;
    public UnitEliteVariantPatch[] variants;
}

[Serializable]
internal sealed class UnitEliteVariantPatch
{
    public int minEliteLevel;
    public string sourceVariant;
    public int statsLevel;
    public string displayNameZhHans;
    public UnitEliteVariantStats stats;
    public UnitEliteVariantModel model;
    public string[] innateAbilityIds;
}

[Serializable]
internal sealed class UnitEliteVariantStats
{
    public UnitEliteVariantCombatStats combat;
    public UnitEliteVariantSharedStats shared;
}

[Serializable]
internal sealed class UnitEliteVariantCombatStats
{
    public int maxHitPoints;
    public int attack;
    public int defense;
    public int magicResistance;
}

[Serializable]
internal sealed class UnitEliteVariantSharedStats
{
    public float moveSpeedMetresPerSecond;
    public float attackIntervalSeconds;
    public int lifeDeduct;
}

[Serializable]
internal sealed class UnitEliteVariantModel
{
    public string resourceFolderName;
    public string resourceKey;
    public string skeletonDataResourceName;
    public string profilePictureResourceName;
    public int unitSkeletonType;
    public float attackAnimationDurationSeconds;
    public string moveAnimation;
    public string attackAnimation;
    public string hitAnimation;
    public string deathAnimation;
}
