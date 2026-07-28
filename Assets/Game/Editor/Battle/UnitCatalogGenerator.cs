using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ArknoNights.Battle.Core;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Deterministically projects resolved elite-zero v2 sources into the frozen
/// Player-safe unit-catalog-v1 transport shape.
/// </summary>
public static class UnitCatalogGenerator
{
    private const int TicksPerSecond = 20;
    private const int LegacyMappedSkeletonType = 2;
    private const string SourceDirectory =
        "Assets/GameData/Units/EliteVariants/Json";
    private const string AbilitySourceDirectory =
        "Assets/GameData/Abilities/Json";
    private const string OutputPath =
        "Assets/Resources/BattleData/unit-catalog-v1.json";
    private const string CatalogId = "task004a-real-units";

    [MenuItem("ARKnoNIGHTS/Battle/Regenerate Unit Catalog v1")]
    public static void Generate()
    {
        Generate(SourceDirectory, OutputPath);
    }

    private static void Generate(string sourceDirectory, string outputPath)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_SOURCE_MISSING path=" + sourceDirectory);
        }

        var loaded = UnitEliteVariantResolver.LoadDirectory(sourceDirectory);
        if (loaded.Count == 0)
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_SOURCE_EMPTY path=" + sourceDirectory);
        }

        var resolved = loaded
            .OrderBy(pair => pair.Key)
            .Select(pair => new ResolvedSource(
                pair.Value.Path,
                UnitEliteVariantResolver.Resolve(pair.Value, 0)))
            .ToArray();
        var knownAbilityIds = LoadKnownAbilityIds(AbilitySourceDirectory);

        foreach (var source in resolved)
        {
            ValidateRepresentability(source.Variant, source.Path);
            ValidateAbilityReferences(
                source.Variant,
                source.Path,
                knownAbilityIds);
        }

        var units = resolved.Select(Convert).ToArray();
        if (units.Select(entry => entry.typeId)
                .Distinct(StringComparer.Ordinal)
                .Count() != units.Length)
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_TYPEID_DUPLICATE");
        }

        var document = new UnitCatalogDocument
        {
            schemaVersion = "unit-catalog-v1",
            catalogId = CatalogId,
            units = units
        };
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_OUTPUT_INVALID path=" + outputPath);
        }

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            outputPath,
            JsonUtility.ToJson(document, true) + "\n",
            new UTF8Encoding(false));

        var normalizedOutputPath = outputPath.Replace('\\', '/');
        if (normalizedOutputPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
        }

        Debug.Log(
            "TASK004A_CATALOG_GENERATED path=" + outputPath
            + " units=" + document.units.Length
            + " summary=" + string.Join(
                "|",
                document.units.Select(
                    entry => entry.typeId
                             + ":"
                             + entry.attackAnimationDurationTicks)));
    }

    private static HashSet<string> LoadKnownAbilityIds(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                "UNIT_CATALOG_ABILITY_SOURCE_MISSING path=" + directory);
        }

        var paths = Directory.GetFiles(
                directory,
                "*.json",
                SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidOperationException(
                "UNIT_CATALOG_ABILITY_SOURCE_EMPTY path=" + directory);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            AbilitySource source;
            try
            {
                source = JsonUtility.FromJson<AbilitySource>(
                    File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "UNIT_CATALOG_ABILITY_SOURCE_INVALID path=" + path,
                    exception);
            }

            if (source == null
                || !string.Equals(
                    source.schemaVersion,
                    "ability-source-v1",
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(source.abilityId))
            {
                throw new InvalidOperationException(
                    "UNIT_CATALOG_ABILITY_SOURCE_INVALID path=" + path);
            }

            if (!ids.Add(source.abilityId))
            {
                throw new InvalidOperationException(
                    "UNIT_CATALOG_ABILITY_ID_DUPLICATE abilityId="
                    + source.abilityId
                    + " path="
                    + path);
            }
        }

        return ids;
    }

    private static void ValidateAbilityReferences(
        ResolvedUnitVariant source,
        string sourcePath,
        ISet<string> knownAbilityIds)
    {
        foreach (var abilityId in source.innateAbilityIds
                     ?? new List<string>())
        {
            if (!knownAbilityIds.Contains(abilityId))
            {
                throw new InvalidOperationException(
                    "UNIT_CATALOG_SOURCE_ABILITY_UNKNOWN path=" + sourcePath
                    + " typeId=" + source.typeId
                    + " abilityId=" + abilityId);
            }
        }
    }

    private static void ValidateRepresentability(
        ResolvedUnitVariant source,
        string sourcePath)
    {
        if (source.attackMethod < 0
            || source.attackMethod > 1
            || source.actionMethod < 1
            || source.actionMethod > 4)
        {
            throw new InvalidOperationException(
                "UNIT_CATALOG_V1_SOURCE_UNREPRESENTABLE path=" + sourcePath
                + " typeId=" + source.typeId);
        }
    }

    private static UnitCatalogEntry Convert(ResolvedSource resolved)
    {
        var sourcePath = resolved.Path;
        var source = resolved.Variant;
        if (source.maxHitPoints <= 0
            || source.attack < 0
            || source.defense < 0
            || source.magicResistance < 0
            || source.magicResistance > 100
            || source.tauntLevel < 0
            || source.lifeDeduct < 0
            || source.deploymentCost < 0
            || source.rarity < 1
            || source.rarity > 6
            || string.IsNullOrWhiteSpace(source.profilePictureResourceName))
        {
            throw new InvalidOperationException(
                "UNIT_DATA_001_SOURCE_VALUES_INVALID path=" + sourcePath
                + " typeId=" + source.typeId);
        }

        var expectedFolder = UnitResourcePaths.BuildCharacterFolderName(
            source.typeId,
            source.resourceKey);
        if (!string.Equals(
                source.sourceVariant,
                expectedFolder,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "UNIT_RESOURCE_CHARACTER_FOLDER_INVALID path=" + sourcePath
                + " expected=" + expectedFolder
                + " actual=" + source.sourceVariant);
        }

        var expectedPortraitResourceName =
            UnitResourcePaths.BuildProfilePictureResourceName(
                source.typeId,
                source.resourceKey);
        if (!string.Equals(
                source.profilePictureResourceName,
                expectedPortraitResourceName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "UNIT_RESOURCE_PROFILE_NAME_INVALID path=" + sourcePath
                + " expected=" + expectedPortraitResourceName
                + " actual=" + source.profilePictureResourceName);
        }

        var move = source.RequireAnimation("move", sourcePath);
        var attack = source.attackMethod == 0
            ? null
            : source.RequireAnimation("attack", sourcePath);
        var death = source.RequireAnimation("death", sourcePath);
        var skillAnimations = source.animations
            .Where(item => item != null
                && item.key.StartsWith("skill", StringComparison.Ordinal))
            .OrderBy(item => item.key, StringComparer.Ordinal)
            .Select(item => new SkillAnimationBinding
            {
                key = item.key,
                name = item.name,
                originalAnimationTicks = ConvertSecondsToTicks(
                    item.durationSeconds,
                    sourcePath + ":" + item.key)
            })
            .ToArray();
        var moveSpeed = ConvertMetresPerSecondToCentimetres(
            source.moveSpeedMetresPerSecond,
            sourcePath);
        var attackIntervalTicks = attack == null
            ? 0
            : ConvertSecondsToTicks(
                source.BaseAttackIntervalSeconds,
                sourcePath + ":baseAttackIntervalSeconds");
        var attackAnimationTicks = attack == null
            ? 0
            : ConvertSecondsToTicks(
                attack.durationSeconds,
                sourcePath + ":attack");
        var damageType = ParseDamageType(source.damageType, sourcePath);
        var attackMethod = ConvertAttackMethod(source.attackMethod, sourcePath);
        var portraitResourcePath =
            "ProfilePicture/" + source.profilePictureResourceName;
        var skeletonResourcePath =
            "Characters/" + source.sourceVariant + "/"
            + source.skeletonDataResourceName;

        if (string.IsNullOrEmpty(source.displayNameZhHans))
        {
            Debug.LogWarning(
                "UNIT_DATA_001_DISPLAY_NAME_UNCONFIGURED path=" + sourcePath
                + " typeId=" + source.typeId);
        }

        if (Resources.Load<Texture2D>(portraitResourcePath) == null)
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_PORTRAIT_MISSING path=" + sourcePath
                + " resource=" + portraitResourcePath);
        }

        VerifyAnimations(source, sourcePath, skeletonResourcePath);

        return new UnitCatalogEntry
        {
            typeId = source.typeId.ToString(CultureInfo.InvariantCulture),
            legacyUnitTypeId = source.typeId,
            resourceKey = source.resourceKey,
            displayNameZhHans = source.displayNameZhHans ?? string.Empty,
            skillDescriptionZhHans =
                source.skillDescriptionZhHans ?? string.Empty,
            sourceFile = Path.GetFileName(sourcePath),
            deploymentCost = source.deploymentCost,
            portraitResourcePath = portraitResourcePath,
            rarity = source.rarity,
            initialEliteLevel = 0,
            maxHitPoints = source.maxHitPoints,
            attack = source.attack,
            defense = source.defense,
            magicResistance = source.magicResistance,
            moveSpeedCentimetresPerSecond = moveSpeed,
            attackIntervalTicks = attackIntervalTicks,
            attackAnimationDurationTicks = attackAnimationTicks,
            damageType = damageType.ToString(),
            attackMethod = attackMethod.ToString(),
            actionMethod = source.actionMethod,
            blockCapacity = source.blockCapacity,
            tauntLevel = source.tauntLevel,
            lifeDeduct = source.lifeDeduct,
            isSyntheticFixtureData = false,
            innateAbilityIds =
                (source.innateAbilityIds ?? new List<string>()).ToArray(),
            prefabResourcePath = "Prefabs/DefaultUnit",
            skeletonDataResourcePath = skeletonResourcePath,
            unitSkelType = LegacyMappedSkeletonType,
            moveAnimation = move.name,
            attackAnimation = attack == null ? string.Empty : attack.name,
            hitAnimation = string.Empty,
            deathAnimation = death.name,
            skillAnimations = skillAnimations
        };
    }

    private static void VerifyAnimations(
        ResolvedUnitVariant source,
        string sourcePath,
        string resourcePath)
    {
        var asset = Resources.Load<SkeletonDataAsset>(resourcePath);
        var skeletonData = asset == null ? null : asset.GetSkeletonData(true);
        if (skeletonData == null)
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_SKELETON_MISSING path=" + sourcePath
                + " resource=" + resourcePath);
        }

        foreach (var binding in source.animations)
        {
            var animation = skeletonData.FindAnimation(binding.name);
            if (animation == null)
            {
                throw new InvalidOperationException(
                    "TASK004A_CATALOG_ANIMATION_MISSING path=" + sourcePath
                    + " resource=" + resourcePath
                    + " key=" + binding.key
                    + " animation=" + binding.name);
            }

            if (!string.Equals(binding.key, "idle", StringComparison.Ordinal)
                && !string.Equals(binding.key, "move", StringComparison.Ordinal)
                && !string.Equals(binding.key, "death", StringComparison.Ordinal)
                && Mathf.Abs(animation.Duration - binding.durationSeconds)
                > 0.000001f)
            {
                throw new InvalidOperationException(
                    "TASK004A_CATALOG_ANIMATION_DURATION_MISMATCH path="
                    + sourcePath
                    + " resource="
                    + resourcePath
                    + " key="
                    + binding.key
                    + " sourceSeconds="
                    + binding.durationSeconds.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                    + " spineSeconds="
                    + animation.Duration.ToString(
                        "R",
                        CultureInfo.InvariantCulture));
            }
        }
    }

    private static int ConvertMetresPerSecondToCentimetres(
        float metresPerSecond,
        string context)
    {
        var centimetres = metresPerSecond * 100f;
        var integerCentimetres = Mathf.RoundToInt(centimetres);
        if (metresPerSecond < 0f
            || Mathf.Abs(centimetres - integerCentimetres) > 0.0001f)
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_MOVE_SPEED_NOT_EXACT context=" + context
                + " value="
                + metresPerSecond.ToString("R", CultureInfo.InvariantCulture));
        }

        return integerCentimetres;
    }

    // Confirmed by the project owner on 2026-07-18:
    // non-integer seconds-to-Tick values round upward.
    private static int ConvertSecondsToTicks(float seconds, string context)
    {
        if (seconds <= 0f)
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_DURATION_INVALID context=" + context);
        }

        return Mathf.CeilToInt(seconds * TicksPerSecond);
    }

    private static DamageType ParseDamageType(string value, string sourcePath)
    {
        if (!Enum.TryParse(value, true, out DamageType parsed)
            || !Enum.IsDefined(typeof(DamageType), parsed))
        {
            throw new InvalidOperationException(
                "TASK004A_CATALOG_DAMAGE_TYPE_INVALID path=" + sourcePath
                + " value=" + value);
        }

        return parsed;
    }

    private static AttackMethod ConvertAttackMethod(int value, string sourcePath)
    {
        if (value == 0)
        {
            return AttackMethod.None;
        }

        if (value == 1)
        {
            return AttackMethod.Melee;
        }

        throw new InvalidOperationException(
            "TASK004A_CATALOG_ATTACK_METHOD_UNMAPPED path=" + sourcePath
            + " value=" + value);
    }

    private sealed class ResolvedSource
    {
        internal ResolvedSource(string path, ResolvedUnitVariant variant)
        {
            Path = path;
            Variant = variant;
        }

        internal string Path { get; }
        internal ResolvedUnitVariant Variant { get; }
    }

    [Serializable]
    private sealed class AbilitySource
    {
        public string schemaVersion;
        public string abilityId;
    }

    [Serializable]
    private sealed class UnitCatalogDocument
    {
        public string schemaVersion;
        public string catalogId;
        public UnitCatalogEntry[] units;
    }

    [Serializable]
    private sealed class SkillAnimationBinding
    {
        public string key;
        public string name;
        public int originalAnimationTicks;
    }

    [Serializable]
    private sealed class UnitCatalogEntry
    {
        public string typeId;
        public int legacyUnitTypeId;
        public string resourceKey;
        public string displayNameZhHans;
        public string skillDescriptionZhHans;
        public string sourceFile;
        public int deploymentCost;
        public string portraitResourcePath;
        public int rarity;
        public int initialEliteLevel;
        public int maxHitPoints;
        public int attack;
        public int defense;
        public int magicResistance;
        public int moveSpeedCentimetresPerSecond;
        public int attackIntervalTicks;
        public int attackAnimationDurationTicks;
        public string damageType;
        public string attackMethod;
        public int actionMethod;
        public int blockCapacity;
        public int tauntLevel;
        public int lifeDeduct;
        public bool isSyntheticFixtureData;
        public string[] innateAbilityIds;
        public string prefabResourcePath;
        public string skeletonDataResourcePath;
        public int unitSkelType;
        public string moveAnimation;
        public string attackAnimation;
        public string hitAnimation;
        public string deathAnimation;
        public SkillAnimationBinding[] skillAnimations;
    }
}
