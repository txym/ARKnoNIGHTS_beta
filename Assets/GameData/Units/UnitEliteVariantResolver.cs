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
    internal const string SchemaVersion = "unit-elite-variants-v2";

    private enum JsonValueKind
    {
        String,
        Integer,
        Number,
        Boolean
    }

    private static readonly string[] ForbiddenLegacyFields =
    {
        "unitSkeletonType",
        "hitAnimation",
        "attackAnimationDurationSeconds",
        "moveAnimation",
        "attackAnimation",
        "deathAnimation",
        "resourceFolderName",
        "initialEliteLevel",
        "animationBehavior"
    };

    private static readonly string[] RootProperties =
    {
        "schemaVersion",
        "typeId",
        "common",
        "variants"
    };

    private static readonly string[] CommonProperties =
    {
        "rarity",
        "deploymentCost",
        "attackMethod",
        "actionMethod",
        "attackRadiusMetres",
        "blockRadiusMetres",
        "canBlock",
        "blockCapacity",
        "tauntLevel",
        "damageType"
    };

    private static readonly string[] VariantProperties =
    {
        "minEliteLevel",
        "sourceVariant",
        "statsLevel",
        "displayNameZhHans",
        "skillDescriptionZhHans",
        "stats",
        "model",
        "innateAbilityIds"
    };

    private static readonly string[] CombatProperties =
    {
        "maxHitPoints",
        "attack",
        "defense",
        "magicResistance"
    };

    private static readonly string[] SharedProperties =
    {
        "moveSpeedMetresPerSecond",
        "attackIntervalSeconds",
        "lifeDeduct"
    };

    private static readonly string[] ModelProperties =
    {
        "resourceKey",
        "skeletonDataResourceName",
        "profilePictureResourceName",
        "animations"
    };

    internal static UnitEliteVariantSource LoadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_FILE_NAME_INVALID context=" + path);
        }

        var json = File.ReadAllText(path);
        var document = ParseDocument(json, path);
        return new UnitEliteVariantSource(document.typeId, path, json);
    }

    internal static IReadOnlyDictionary<int, UnitEliteVariantSource> LoadDirectory(
        string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new Dictionary<int, UnitEliteVariantSource>();
        }

        var result = new Dictionary<int, UnitEliteVariantSource>();
        foreach (var path in Directory.GetFiles(
                         directory,
                         "*.json",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(item => System.IO.Path.GetFileName(item), StringComparer.Ordinal))
        {
            var source = LoadFile(path);
            if (result.ContainsKey(source.TypeId))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_TYPEID_DUPLICATE typeId=" + source.TypeId);
            }

            result.Add(source.TypeId, source);
        }

        return result;
    }

    internal static ResolvedUnitVariant Resolve(
        string json,
        int eliteLevel,
        string context)
    {
        ValidateTargetLevel(eliteLevel, context);
        var document = ParseDocument(json, context);
        return BuildResolved(document, eliteLevel);
    }

    internal static ResolvedUnitVariant Resolve(
        UnitEliteVariantSource source,
        int eliteLevel)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return Resolve(source.Json, eliteLevel, source.Path);
    }

    internal static IReadOnlyList<string> GetDeclaredInnateAbilityIds(
        UnitEliteVariantSource source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var document = ParseDocument(source.Json, source.Path);
        return document.variants
            .Where(variant => variant.innateAbilityIds != null)
            .SelectMany(variant => variant.innateAbilityIds)
            .ToArray();
    }

    private static UnitEliteVariantsV2Document ParseDocument(
        string json,
        string context)
    {
        var rootSlice = UnitEliteVariantJsonShape.RootObject(json, context);
        var root = UnitEliteVariantJsonShape.ReadObject(json, rootSlice, context);
        UnitEliteVariantJsonShape.RequireExactProperties(
            root,
            "UNIT_ELITE_VARIANT_ROOT_SHAPE_INVALID context=" + context,
            RootProperties);
        RequireStringProperty(json, root, "schemaVersion", context);
        RequireIntegerProperty(json, root, "typeId", context);

        RejectForbiddenFields(json, rootSlice, context);

        UnitEliteVariantsV2Document document;
        try
        {
            document = JsonUtility.FromJson<UnitEliteVariantsV2Document>(json);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_JSON_INVALID context=" + context,
                exception);
        }

        if (document == null)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_JSON_INVALID context=" + context);
        }

        if (!string.Equals(document.schemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_SCHEMA_INVALID context=" + context);
        }

        if (document.typeId <= 0)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_TYPEID_INVALID context=" + context);
        }

        ValidateCommon(json, root["common"], document.common, context);
        ValidateVariants(json, root["variants"], document, context);
        ValidateEffectiveVariants(document, context);
        ValidateFileName(document, context);
        return document;
    }

    private static void ValidateCommon(
        string json,
        UnitJsonSlice slice,
        UnitCommonSource common,
        string context)
    {
        var properties = UnitEliteVariantJsonShape.ReadObject(json, slice, context);
        UnitEliteVariantJsonShape.RequireExactProperties(
            properties,
            "UNIT_ELITE_VARIANT_COMMON_SHAPE_INVALID context=" + context,
            CommonProperties);
        RequireIntegerProperty(json, properties, "rarity", context);
        RequireIntegerProperty(json, properties, "deploymentCost", context);
        RequireIntegerProperty(json, properties, "attackMethod", context);
        RequireIntegerProperty(json, properties, "actionMethod", context);
        RequireNumberProperty(json, properties, "attackRadiusMetres", context);
        RequireNumberProperty(json, properties, "blockRadiusMetres", context);
        RequireBooleanProperty(json, properties, "canBlock", context);
        RequireIntegerProperty(json, properties, "blockCapacity", context);
        RequireIntegerProperty(json, properties, "tauntLevel", context);
        RequireStringProperty(json, properties, "damageType", context);

        if (common == null
            || common.rarity < 1
            || common.rarity > 6
            || common.deploymentCost != 2
            || common.attackMethod < 0
            || common.attackMethod > 1
            || common.actionMethod < 1
            || common.actionMethod > 4
            || common.attackRadiusMetres != 0f
            || common.blockRadiusMetres != 0f
            || common.tauntLevel != 0
            || (!string.Equals(common.damageType, "Physical", StringComparison.Ordinal)
                && !string.Equals(common.damageType, "Magic", StringComparison.Ordinal)
                && !string.Equals(common.damageType, "None", StringComparison.Ordinal))
            || (common.canBlock && common.blockCapacity != 1)
            || (!common.canBlock && common.blockCapacity != 0))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_COMMON_INVALID context=" + context);
        }
    }

    private static void ValidateVariants(
        string json,
        UnitJsonSlice variantsSlice,
        UnitEliteVariantsV2Document document,
        string context)
    {
        var slices = UnitEliteVariantJsonShape.ReadArray(json, variantsSlice, context);
        if (document.variants == null
            || document.variants.Length == 0
            || document.variants.Length != slices.Count)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context);
        }

        var levels = new HashSet<int>();
        for (var index = 0; index < document.variants.Length; index++)
        {
            var variant = document.variants[index];
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
        }

        if (!levels.Contains(0))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context);
        }

        for (var index = 0; index < document.variants.Length; index++)
        {
            ValidateVariant(
                json,
                slices[index],
                document.typeId,
                document.common,
                document.variants[index],
                context);
        }

        Array.Sort(
            document.variants,
            (left, right) => left.minEliteLevel.CompareTo(right.minEliteLevel));
    }

    private static void ValidateVariant(
        string json,
        UnitJsonSlice slice,
        int typeId,
        UnitCommonSource common,
        UnitVariantSource variant,
        string context)
    {
        var properties = UnitEliteVariantJsonShape.ReadObject(json, slice, context);
        RequireAllowedAndRequired(
            properties,
            "UNIT_ELITE_VARIANT_VARIANT_SHAPE_INVALID context=" + context
            + " eliteLevel=" + variant.minEliteLevel,
            VariantProperties,
            "minEliteLevel",
            "sourceVariant",
            "statsLevel");
        RequireIntegerProperty(json, properties, "minEliteLevel", context);
        RequireStringProperty(json, properties, "sourceVariant", context);
        RequireIntegerProperty(json, properties, "statsLevel", context);
        RequireOptionalStringProperty(
            json,
            properties,
            "displayNameZhHans",
            context);
        RequireOptionalStringProperty(
            json,
            properties,
            "skillDescriptionZhHans",
            context);

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

        if (variant.minEliteLevel == 0)
        {
            RequireProperties(
                properties,
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context,
                "displayNameZhHans",
                "skillDescriptionZhHans",
                "stats",
                "model",
                "innateAbilityIds");
            if (variant.displayNameZhHans == null
                || variant.skillDescriptionZhHans == null
                || variant.innateAbilityIds == null)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context);
            }
        }

        if (properties.ContainsKey("innateAbilityIds"))
        {
            ValidateAbilityIds(
                json,
                properties["innateAbilityIds"],
                variant.innateAbilityIds,
                context,
                variant.minEliteLevel);
        }

        ValidateStats(
            json,
            properties,
            variant,
            context);
        ValidateModel(
            json,
            properties,
            typeId,
            common,
            variant,
            context);
    }

    private static void ValidateAbilityIds(
        string json,
        UnitJsonSlice slice,
        string[] abilityIds,
        string context,
        int eliteLevel)
    {
        var elements = UnitEliteVariantJsonShape.ReadArray(json, slice, context);
        foreach (var element in elements)
        {
            RequireValueKind(
                json,
                element,
                JsonValueKind.String,
                context);
        }

        if (abilityIds == null
            || abilityIds.Length != elements.Count
            || abilityIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context
                + " eliteLevel=" + eliteLevel);
        }
    }

    private static void ValidateStats(
        string json,
        IReadOnlyDictionary<string, UnitJsonSlice> variantProperties,
        UnitVariantSource variant,
        string context)
    {
        if (!variantProperties.TryGetValue("stats", out var statsSlice))
        {
            variant.stats = null;
            return;
        }

        var statsProperties =
            UnitEliteVariantJsonShape.ReadObject(json, statsSlice, context);
        RequireAllowedAndRequired(
            statsProperties,
            "UNIT_ELITE_VARIANT_STATS_SHAPE_INVALID context=" + context
            + " eliteLevel=" + variant.minEliteLevel,
            new[] { "combat", "shared" });
        if (statsProperties.Count == 0 || variant.stats == null)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_STATS_SHAPE_INVALID context=" + context
                + " eliteLevel=" + variant.minEliteLevel);
        }

        if (variant.minEliteLevel == 0)
        {
            RequireProperties(
                statsProperties,
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context,
                "combat",
                "shared");
        }

        if (statsProperties.TryGetValue("combat", out var combatSlice))
        {
            var combatProperties =
                UnitEliteVariantJsonShape.ReadObject(json, combatSlice, context);
            UnitEliteVariantJsonShape.RequireExactProperties(
                combatProperties,
                "UNIT_ELITE_VARIANT_COMBAT_SHAPE_INVALID context=" + context
                + " eliteLevel=" + variant.minEliteLevel,
                CombatProperties);
            RequireIntegerProperty(
                json,
                combatProperties,
                "maxHitPoints",
                context);
            RequireIntegerProperty(json, combatProperties, "attack", context);
            RequireIntegerProperty(json, combatProperties, "defense", context);
            RequireIntegerProperty(
                json,
                combatProperties,
                "magicResistance",
                context);
            if (variant.stats.combat == null)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_COMBAT_SHAPE_INVALID context=" + context);
            }
        }
        else
        {
            variant.stats.combat = null;
        }

        if (statsProperties.TryGetValue("shared", out var sharedSlice))
        {
            var sharedProperties =
                UnitEliteVariantJsonShape.ReadObject(json, sharedSlice, context);
            UnitEliteVariantJsonShape.RequireExactProperties(
                sharedProperties,
                "UNIT_ELITE_VARIANT_SHARED_SHAPE_INVALID context=" + context
                + " eliteLevel=" + variant.minEliteLevel,
                SharedProperties);
            RequireNumberProperty(
                json,
                sharedProperties,
                "moveSpeedMetresPerSecond",
                context);
            RequireNumberProperty(
                json,
                sharedProperties,
                "attackIntervalSeconds",
                context);
            RequireIntegerProperty(json, sharedProperties, "lifeDeduct", context);
            if (variant.stats.shared == null)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_SHARED_SHAPE_INVALID context=" + context);
            }
        }
        else
        {
            variant.stats.shared = null;
        }
    }

    private static void ValidateModel(
        string json,
        IReadOnlyDictionary<string, UnitJsonSlice> variantProperties,
        int typeId,
        UnitCommonSource common,
        UnitVariantSource variant,
        string context)
    {
        if (!variantProperties.TryGetValue("model", out var modelSlice))
        {
            variant.model = null;
            return;
        }

        var modelProperties =
            UnitEliteVariantJsonShape.ReadObject(json, modelSlice, context);
        UnitEliteVariantJsonShape.RequireExactProperties(
            modelProperties,
            "UNIT_ELITE_VARIANT_MODEL_SHAPE_INVALID context=" + context
            + " eliteLevel=" + variant.minEliteLevel,
            ModelProperties);
        RequireStringProperty(json, modelProperties, "resourceKey", context);
        RequireStringProperty(
            json,
            modelProperties,
            "skeletonDataResourceName",
            context);
        RequireStringProperty(
            json,
            modelProperties,
            "profilePictureResourceName",
            context);
        if (variant.model == null
            || string.IsNullOrWhiteSpace(variant.model.resourceKey)
            || string.IsNullOrWhiteSpace(variant.model.skeletonDataResourceName)
            || string.IsNullOrWhiteSpace(variant.model.profilePictureResourceName)
            || variant.model.animations == null)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_MODEL_SHAPE_INVALID context=" + context
                + " eliteLevel=" + variant.minEliteLevel);
        }

        var expectedFolder =
            UnitResourcePaths.BuildCharacterFolderName(typeId, variant.model.resourceKey);
        var expectedPortrait =
            UnitResourcePaths.BuildProfilePictureResourceName(typeId, variant.model.resourceKey);
        var expectedSkeleton = "enemy_" + expectedFolder + "_SkeletonData";
        if (!string.Equals(
                variant.sourceVariant,
                expectedFolder,
                StringComparison.Ordinal)
            || !string.Equals(
                variant.model.skeletonDataResourceName,
                expectedSkeleton,
                StringComparison.Ordinal)
            || !string.Equals(
                variant.model.profilePictureResourceName,
                expectedPortrait,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_MODEL_CANONICAL_NAME_INVALID context=" + context
                + " eliteLevel=" + variant.minEliteLevel);
        }

        var animationSlices =
            UnitEliteVariantJsonShape.ReadArray(json, modelProperties["animations"], context);
        if (animationSlices.Count != variant.model.animations.Length)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_ANIMATION_SHAPE_INVALID context=" + context);
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < animationSlices.Count; index++)
        {
            var animation = variant.model.animations[index];
            var animationProperties =
                UnitEliteVariantJsonShape.ReadObject(json, animationSlices[index], context);
            RequireAllowedAndRequired(
                animationProperties,
                "UNIT_ELITE_VARIANT_ANIMATION_SHAPE_INVALID context=" + context
                + " eliteLevel=" + variant.minEliteLevel,
                new[] { "key", "name", "durationSeconds" },
                "key",
                "name");
            RequireStringProperty(json, animationProperties, "key", context);
            RequireStringProperty(json, animationProperties, "name", context);
            if (animationProperties.ContainsKey("durationSeconds"))
            {
                RequireNumberProperty(
                    json,
                    animationProperties,
                    "durationSeconds",
                    context);
            }
            if (animation == null
                || string.IsNullOrWhiteSpace(animation.key)
                || string.IsNullOrWhiteSpace(animation.name))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_ANIMATION_SHAPE_INVALID context=" + context);
            }

            if (string.Equals(animation.key, "Default", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_LEGACY_FIELD_FORBIDDEN"
                    + " field=Default"
                    + " context=" + context);
            }

            if (!keys.Add(animation.key))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_ANIMATION_KEY_DUPLICATE context=" + context
                    + " key=" + animation.key);
            }

        }

        RequireAnimation(variant.model.animations, "idle", context);
        RequireAnimation(variant.model.animations, "move", context);
        RequireAnimation(variant.model.animations, "death", context);
        if (common.attackMethod == 0)
        {
            if (variant.model.animations.Any(animation =>
                    string.Equals(animation.key, "attack", StringComparison.Ordinal)
                    || animation.key.StartsWith("attack.", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_NON_ATTACKER_INVALID context=" + context);
            }
        }
        else
        {
            RequireAnimation(variant.model.animations, "attack", context);
        }

        for (var index = 0; index < animationSlices.Count; index++)
        {
            var animation = variant.model.animations[index];
            var animationProperties =
                UnitEliteVariantJsonShape.ReadObject(json, animationSlices[index], context);
            var isBaseKey = string.Equals(animation.key, "idle", StringComparison.Ordinal)
                            || string.Equals(animation.key, "move", StringComparison.Ordinal)
                            || string.Equals(animation.key, "death", StringComparison.Ordinal);
            var hasDuration = animationProperties.ContainsKey("durationSeconds");
            if ((isBaseKey && hasDuration)
                || (!isBaseKey && (!hasDuration || animation.durationSeconds <= 0f)))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_ANIMATION_DURATION_INVALID context=" + context
                    + " key=" + animation.key);
            }
        }
    }

    private static void ValidateEffectiveVariants(
        UnitEliteVariantsV2Document document,
        string context)
    {
        for (var eliteLevel = 0; eliteLevel <= 3; eliteLevel++)
        {
            var resolved = BuildResolved(document, eliteLevel);
            var expectedSourceVariant =
                UnitResourcePaths.BuildCharacterFolderName(
                    resolved.typeId,
                    resolved.resourceKey);
            if (!string.Equals(
                    resolved.sourceVariant,
                    expectedSourceVariant,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_MODEL_CANONICAL_NAME_INVALID context=" + context
                    + " eliteLevel=" + eliteLevel);
            }

            if (resolved.maxHitPoints <= 0
                || resolved.attack < 0
                || resolved.defense < 0
                || resolved.magicResistance < 0
                || resolved.magicResistance > 100)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_COMBAT_INVALID context=" + context
                    + " eliteLevel=" + eliteLevel);
            }

            if (resolved.moveSpeedMetresPerSecond < 0f
                || resolved.attackIntervalSeconds < 0f
                || resolved.lifeDeduct < 0)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_SHARED_INVALID context=" + context
                    + " eliteLevel=" + eliteLevel);
            }

            if (resolved.actionMethod == 4)
            {
                if (resolved.moveSpeedMetresPerSecond != 0f)
                {
                    throw new InvalidOperationException(
                        "UNIT_ELITE_VARIANT_SHARED_INVALID context=" + context
                        + " eliteLevel=" + eliteLevel);
                }
            }
            else if (resolved.moveSpeedMetresPerSecond <= 0f)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_SHARED_INVALID context=" + context
                    + " eliteLevel=" + eliteLevel);
            }

            if (resolved.attackMethod == 0)
            {
                if (!string.Equals(resolved.damageType, "None", StringComparison.Ordinal)
                    || resolved.attack != 0
                    || resolved.attackIntervalSeconds != 0f)
                {
                    throw new InvalidOperationException(
                        "UNIT_ELITE_VARIANT_NON_ATTACKER_INVALID context=" + context
                        + " eliteLevel=" + eliteLevel);
                }
            }
            else if ((!string.Equals(
                          resolved.damageType,
                          "Physical",
                          StringComparison.Ordinal)
                      && !string.Equals(
                          resolved.damageType,
                          "Magic",
                          StringComparison.Ordinal))
                     || resolved.attackIntervalSeconds <= 0f
                     || resolved.RequireAnimation("attack", context).durationSeconds <= 0f)
            {
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_ATTACKER_INVALID context=" + context
                    + " eliteLevel=" + eliteLevel);
            }
        }
    }

    private static void ValidateFileName(
        UnitEliteVariantsV2Document document,
        string context)
    {
        var baseVariant = document.variants[0];
        var expectedStem =
            UnitResourcePaths.BuildCharacterFolderName(
                document.typeId,
                baseVariant.model.resourceKey);
        var fileName = System.IO.Path.GetFileName(context);
        var actualStem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        if (!string.Equals(fileName, expectedStem + ".json", StringComparison.Ordinal)
            || !string.Equals(
                baseVariant.sourceVariant,
                actualStem,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_FILE_NAME_INVALID context=" + context);
        }
    }

    private static ResolvedUnitVariant BuildResolved(
        UnitEliteVariantsV2Document document,
        int eliteLevel)
    {
        var common = document.common;
        var resolved = new ResolvedUnitVariant
        {
            typeId = document.typeId,
            rarity = common.rarity,
            deploymentCost = common.deploymentCost,
            attackMethod = common.attackMethod,
            actionMethod = common.actionMethod,
            attackRadiusMetres = common.attackRadiusMetres,
            blockRadiusMetres = common.blockRadiusMetres,
            canBlock = common.canBlock,
            blockCapacity = common.blockCapacity,
            tauntLevel = common.tauntLevel,
            damageType = common.damageType
        };

        foreach (var variant in document.variants.Where(
                     item => item.minEliteLevel <= eliteLevel))
        {
            resolved.minEliteLevel = variant.minEliteLevel;
            resolved.sourceVariant = variant.sourceVariant;
            resolved.statsLevel = variant.statsLevel;
            if (variant.displayNameZhHans != null)
            {
                resolved.displayNameZhHans = variant.displayNameZhHans;
            }

            if (variant.skillDescriptionZhHans != null)
            {
                resolved.skillDescriptionZhHans = variant.skillDescriptionZhHans;
            }

            if (variant.stats != null)
            {
                if (variant.stats.combat != null)
                {
                    resolved.maxHitPoints = variant.stats.combat.maxHitPoints;
                    resolved.attack = variant.stats.combat.attack;
                    resolved.defense = variant.stats.combat.defense;
                    resolved.magicResistance = variant.stats.combat.magicResistance;
                }

                if (variant.stats.shared != null)
                {
                    resolved.moveSpeedMetresPerSecond =
                        variant.stats.shared.moveSpeedMetresPerSecond;
                    resolved.attackIntervalSeconds =
                        variant.stats.shared.attackIntervalSeconds;
                    resolved.lifeDeduct = variant.stats.shared.lifeDeduct;
                }
            }

            if (variant.model != null)
            {
                resolved.resourceKey = variant.model.resourceKey;
                resolved.skeletonDataResourceName =
                    variant.model.skeletonDataResourceName;
                resolved.profilePictureResourceName =
                    variant.model.profilePictureResourceName;
                resolved.animations = CloneAnimations(variant.model.animations);
            }

            if (variant.innateAbilityIds != null)
            {
                resolved.innateAbilityIds =
                    new List<string>(variant.innateAbilityIds);
            }
        }

        resolved.animations = CloneAnimations(resolved.animations);
        resolved.innateAbilityIds = resolved.innateAbilityIds == null
            ? new List<string>()
            : new List<string>(resolved.innateAbilityIds);
        return resolved;
    }

    private static List<UnitAnimationBinding> CloneAnimations(
        IEnumerable<UnitAnimationBinding> animations)
    {
        if (animations == null)
        {
            return new List<UnitAnimationBinding>();
        }

        return animations.Select(animation => new UnitAnimationBinding
        {
            key = animation.key,
            name = animation.name,
            durationSeconds = animation.durationSeconds
        }).ToList();
    }

    private static UnitAnimationBinding RequireAnimation(
        IEnumerable<UnitAnimationBinding> animations,
        string key,
        string context)
    {
        var animation = animations.FirstOrDefault(item =>
            string.Equals(item.key, key, StringComparison.Ordinal));
        if (animation == null)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_ANIMATION_REQUIRED_MISSING"
                + " key=" + key
                + " context=" + context);
        }

        return animation;
    }

    private static void RejectForbiddenFields(
        string json,
        UnitJsonSlice slice,
        string context)
    {
        var first = FirstNonWhitespace(json, slice);
        if (first == '{')
        {
            var properties = UnitEliteVariantJsonShape.ReadObject(json, slice, context);
            foreach (var property in properties)
            {
                if (ForbiddenLegacyFields.Contains(
                        property.Key,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        "UNIT_ELITE_VARIANT_LEGACY_FIELD_FORBIDDEN"
                        + " field=" + property.Key
                        + " context=" + context);
                }

                RejectForbiddenFields(json, property.Value, context);
            }
        }
        else if (first == '[')
        {
            foreach (var element in UnitEliteVariantJsonShape.ReadArray(
                         json,
                         slice,
                         context))
            {
                RejectForbiddenFields(json, element, context);
            }
        }
    }

    private static char FirstNonWhitespace(string json, UnitJsonSlice slice)
    {
        var end = slice.Start + slice.Length;
        for (var index = slice.Start; index < end; index++)
        {
            var character = json[index];
            if (character != ' '
                && character != '\t'
                && character != '\r'
                && character != '\n')
            {
                return character;
            }
        }

        return '\0';
    }

    private static void RequireStringProperty(
        string json,
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string propertyName,
        string context)
    {
        RequireValueKind(
            json,
            properties[propertyName],
            JsonValueKind.String,
            context);
    }

    private static void RequireOptionalStringProperty(
        string json,
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string propertyName,
        string context)
    {
        if (properties.TryGetValue(propertyName, out var slice))
        {
            RequireValueKind(json, slice, JsonValueKind.String, context);
        }
    }

    private static void RequireIntegerProperty(
        string json,
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string propertyName,
        string context)
    {
        RequireValueKind(
            json,
            properties[propertyName],
            JsonValueKind.Integer,
            context);
    }

    private static void RequireNumberProperty(
        string json,
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string propertyName,
        string context)
    {
        RequireValueKind(
            json,
            properties[propertyName],
            JsonValueKind.Number,
            context);
    }

    private static void RequireBooleanProperty(
        string json,
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string propertyName,
        string context)
    {
        RequireValueKind(
            json,
            properties[propertyName],
            JsonValueKind.Boolean,
            context);
    }

    private static void RequireValueKind(
        string json,
        UnitJsonSlice slice,
        JsonValueKind kind,
        string context)
    {
        var start = slice.Start;
        var end = slice.Start + slice.Length;
        while (start < end && IsJsonWhitespace(json[start]))
        {
            start++;
        }

        while (end > start && IsJsonWhitespace(json[end - 1]))
        {
            end--;
        }

        var isValid = false;
        switch (kind)
        {
            case JsonValueKind.String:
                isValid = end - start >= 2
                          && json[start] == '"'
                          && json[end - 1] == '"';
                break;
            case JsonValueKind.Integer:
                isValid = IsJsonNumber(json, start, end, true);
                break;
            case JsonValueKind.Number:
                isValid = IsJsonNumber(json, start, end, false);
                break;
            case JsonValueKind.Boolean:
                isValid = SliceEquals(json, start, end, "true")
                          || SliceEquals(json, start, end, "false");
                break;
        }

        if (!isValid)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_JSON_INVALID context=" + context);
        }
    }

    private static bool IsJsonNumber(
        string json,
        int start,
        int end,
        bool requireInteger)
    {
        var index = start;
        if (index < end && json[index] == '-')
        {
            index++;
        }

        if (index >= end)
        {
            return false;
        }

        if (json[index] == '0')
        {
            index++;
            if (index < end && IsAsciiDigit(json[index]))
            {
                return false;
            }
        }
        else
        {
            if (json[index] < '1' || json[index] > '9')
            {
                return false;
            }

            while (index < end && IsAsciiDigit(json[index]))
            {
                index++;
            }
        }

        if (index < end && json[index] == '.')
        {
            if (requireInteger)
            {
                return false;
            }

            index++;
            var fractionStart = index;
            while (index < end && IsAsciiDigit(json[index]))
            {
                index++;
            }

            if (index == fractionStart)
            {
                return false;
            }
        }

        if (index < end && (json[index] == 'e' || json[index] == 'E'))
        {
            if (requireInteger)
            {
                return false;
            }

            index++;
            if (index < end && (json[index] == '+' || json[index] == '-'))
            {
                index++;
            }

            var exponentStart = index;
            while (index < end && IsAsciiDigit(json[index]))
            {
                index++;
            }

            if (index == exponentStart)
            {
                return false;
            }
        }

        return index == end;
    }

    private static bool SliceEquals(
        string json,
        int start,
        int end,
        string expected)
    {
        return end - start == expected.Length
               && string.CompareOrdinal(
                   json,
                   start,
                   expected,
                   0,
                   expected.Length) == 0;
    }

    private static bool IsJsonWhitespace(char character)
    {
        return character == ' '
               || character == '\t'
               || character == '\r'
               || character == '\n';
    }

    private static bool IsAsciiDigit(char character)
    {
        return character >= '0' && character <= '9';
    }

    private static void RequireAllowedAndRequired(
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string error,
        IEnumerable<string> allowed,
        params string[] required)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        var unexpected = properties.Keys
            .Where(name => !allowedSet.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var missing = required
            .Where(name => !properties.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unexpected.Length != 0 || missing.Length != 0)
        {
            throw new InvalidOperationException(
                error
                + " missing=" + string.Join(",", missing)
                + " unexpected=" + string.Join(",", unexpected));
        }
    }

    private static void RequireProperties(
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string error,
        params string[] required)
    {
        var missing = required
            .Where(name => !properties.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                error + " missing=" + string.Join(",", missing));
        }
    }

    private static void ValidateTargetLevel(int eliteLevel, string context)
    {
        if (eliteLevel < 0 || eliteLevel > 3)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_TARGET_INVALID context=" + context
                + " eliteLevel=" + eliteLevel);
        }
    }
}
