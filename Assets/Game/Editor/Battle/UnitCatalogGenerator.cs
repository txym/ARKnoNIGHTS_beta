using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ArknoNights.Battle.Core;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Deterministically regenerates the Player-safe TASK-004A catalog from Assets/GameData/Units/Json.
/// It also verifies the configured Spine animations through Unity/Spine APIs before writing output.
/// </summary>
public static class UnitCatalogGenerator
{
    private const int TicksPerSecond = 20;
    private const string SourceDirectory = "Assets/GameData/Units/Json";
    private const string OutputPath = "Assets/Resources/BattleData/unit-catalog-v1.json";
    private const string CatalogId = "task004a-real-units";

    [MenuItem("ARKnoNIGHTS/Battle/Regenerate Unit Catalog v1")]
    public static void Generate()
    {
        if (!Directory.Exists(SourceDirectory)) throw new InvalidOperationException("TASK004A_CATALOG_SOURCE_MISSING path=" + SourceDirectory);
        var sourceFiles = Directory.GetFiles(SourceDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal).ToArray();
        if (sourceFiles.Length == 0) throw new InvalidOperationException("TASK004A_CATALOG_SOURCE_EMPTY path=" + SourceDirectory);

        var document = new UnitCatalogDocument
        {
            schemaVersion = "unit-catalog-v1",
            catalogId = CatalogId,
            units = sourceFiles.Select(Convert).OrderBy(entry => entry.typeId, StringComparer.Ordinal).ToArray()
        };
        if (document.units.Select(entry => entry.typeId).Distinct(StringComparer.Ordinal).Count() != document.units.Length)
            throw new InvalidOperationException("TASK004A_CATALOG_TYPEID_DUPLICATE");

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllText(OutputPath, JsonUtility.ToJson(document, true) + "\n", new UTF8Encoding(false));
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("TASK004A_CATALOG_GENERATED path=" + OutputPath + " units=" + document.units.Length + " summary=" + string.Join("|", document.units.Select(entry => entry.typeId + ":" + entry.attackAnimationDurationTicks)));
    }

    private static UnitCatalogEntry Convert(string sourcePath)
    {
        UnitJson source;
        try { source = JsonUtility.FromJson<UnitJson>(File.ReadAllText(sourcePath)); }
        catch (Exception exception) { throw new InvalidOperationException("TASK004A_CATALOG_SOURCE_INVALID path=" + sourcePath, exception); }
        if (source == null) throw new InvalidOperationException("TASK004A_CATALOG_SOURCE_EMPTY path=" + sourcePath);
        if (!string.Equals(source.schemaVersion, "unit-source-v1", StringComparison.Ordinal) || source.typeId <= 0 || string.IsNullOrWhiteSpace(source.resourceKey) || string.IsNullOrWhiteSpace(source.skeletonDataResourceName)) throw new InvalidOperationException("UNIT_DATA_001_SOURCE_REQUIRED_MISSING path=" + sourcePath + " typeId=" + source.typeId);
        if (source.maxHitPoints <= 0 || source.attack < 0 || source.defense < 0 || source.magicResistance < 0 || source.magicResistance > 100 || source.blockCapacity <= 0 || source.tauntLevel < 0 || source.lifeDeduct < 0 || source.deploymentCost < 0 || source.rarity < 1 || source.rarity > 6 || source.initialEliteLevel < 0 || source.initialEliteLevel > 3 || string.IsNullOrWhiteSpace(source.profilePictureResourceName)) throw new InvalidOperationException("UNIT_DATA_001_SOURCE_VALUES_INVALID path=" + sourcePath + " typeId=" + source.typeId);

        var moveSpeed = ConvertMetresPerSecondToCentimetres(source.moveSpeedMetresPerSecond, sourcePath);
        var attackIntervalTicks = ConvertSecondsToTicks(source.BaseAttackIntervalSeconds, sourcePath + ":baseAttackIntervalSeconds");
        var attackAnimationTicks = ConvertSecondsToTicks(source.attackAnimationDurationSeconds, sourcePath + ":attackAnimationDurationSeconds");
        var damageType = ParseDamageType(source.damageType, sourcePath);
        var attackMethod = ConvertAttackMethod(source.attackMethod, sourcePath);
        var expectedPortraitResourceName = UnitResourcePaths.BuildProfilePictureResourceName(source.typeId, source.resourceKey);
        if (!string.Equals(source.profilePictureResourceName, expectedPortraitResourceName, StringComparison.Ordinal))
            throw new InvalidOperationException("UNIT_RESOURCE_PROFILE_NAME_INVALID path=" + sourcePath + " expected=" + expectedPortraitResourceName + " actual=" + source.profilePictureResourceName);

        var skeletonResourcePath = UnitResourcePaths.BuildSkeletonDataResourcePath(source.typeId, source.resourceKey, source.skeletonDataResourceName);
        var portraitResourcePath = UnitResourcePaths.BuildProfilePictureResourcePath(source.typeId, source.resourceKey);

        if (string.IsNullOrEmpty(source.displayNameZhHans)) Debug.LogWarning("UNIT_DATA_001_DISPLAY_NAME_UNCONFIGURED path=" + sourcePath + " typeId=" + source.typeId);

        if (Resources.Load<Sprite>(portraitResourcePath) == null) throw new InvalidOperationException("TASK004A_CATALOG_PORTRAIT_MISSING path=" + sourcePath + " resource=" + portraitResourcePath);

        VerifyAnimation(sourcePath, skeletonResourcePath, source.attackAnimation, attackAnimationTicks, required: true);
        VerifyAnimation(sourcePath, skeletonResourcePath, source.moveAnimation, 0, required: true);
        VerifyAnimation(sourcePath, skeletonResourcePath, source.deathAnimation, 0, required: true);
        if (!string.IsNullOrEmpty(source.hitAnimation)) VerifyAnimation(sourcePath, skeletonResourcePath, source.hitAnimation, 0, required: false);

        return new UnitCatalogEntry
        {
            typeId = source.typeId.ToString(CultureInfo.InvariantCulture),
            legacyUnitTypeId = source.typeId,
            resourceKey = source.resourceKey,
            displayNameZhHans = source.displayNameZhHans ?? string.Empty,
            skillDescriptionZhHans = source.skillDescriptionZhHans ?? string.Empty,
            sourceFile = Path.GetFileName(sourcePath),
            deploymentCost = source.deploymentCost,
            portraitResourcePath = portraitResourcePath,
            rarity = source.rarity,
            initialEliteLevel = source.initialEliteLevel,
            maxHitPoints = source.maxHitPoints,
            attack = source.attack,
            defense = source.defense,
            magicResistance = source.magicResistance,
            moveSpeedCentimetresPerSecond = moveSpeed,
            attackIntervalTicks = attackIntervalTicks,
            attackAnimationDurationTicks = attackAnimationTicks,
            damageType = damageType.ToString(),
            attackMethod = attackMethod.ToString(),
            blockCapacity = source.blockCapacity,
            tauntLevel = source.tauntLevel,
            lifeDeduct = source.lifeDeduct,
            isSyntheticFixtureData = false,
            innateAbilityIds = (source.innateAbilityIds ?? new System.Collections.Generic.List<string>()).ToArray(),
            prefabResourcePath = "Prefabs/DefaultUnit",
            skeletonDataResourcePath = skeletonResourcePath,
            unitSkelType = source.unitSkeletonType,
            moveAnimation = source.moveAnimation,
            attackAnimation = source.attackAnimation,
            hitAnimation = source.hitAnimation ?? string.Empty,
            deathAnimation = source.deathAnimation
        };
    }

    private static int ConvertMetresPerSecondToCentimetres(float metresPerSecond, string context)
    {
        var centimetres = metresPerSecond * 100f;
        var integerCentimetres = Mathf.RoundToInt(centimetres);
        if (metresPerSecond <= 0f || Mathf.Abs(centimetres - integerCentimetres) > 0.0001f)
            throw new InvalidOperationException("TASK004A_CATALOG_MOVE_SPEED_NOT_EXACT context=" + context + " value=" + metresPerSecond.ToString("R", CultureInfo.InvariantCulture));
        return integerCentimetres;
    }

    // Confirmed by the project owner on 2026-07-18: non-integer seconds-to-Tick values round upward.
    private static int ConvertSecondsToTicks(float seconds, string context)
    {
        if (seconds <= 0f) throw new InvalidOperationException("TASK004A_CATALOG_DURATION_INVALID context=" + context);
        return Mathf.CeilToInt(seconds * TicksPerSecond);
    }

    private static DamageType ParseDamageType(string value, string sourcePath)
    {
        if (!Enum.TryParse(value, true, out DamageType parsed) || !Enum.IsDefined(typeof(DamageType), parsed))
            throw new InvalidOperationException("TASK004A_CATALOG_DAMAGE_TYPE_INVALID path=" + sourcePath + " value=" + value);
        return parsed;
    }

    private static AttackMethod ConvertAttackMethod(int value, string sourcePath)
    {
        // Existing source value 1 is the only present attack method, and the confirmed current rules have no ranged units.
        if (value == 1) return AttackMethod.Melee;
        throw new InvalidOperationException("TASK004A_CATALOG_ATTACK_METHOD_UNMAPPED path=" + sourcePath + " value=" + value);
    }

    private static void VerifyAnimation(string sourcePath, string resourcePath, string animationName, int expectedTicks, bool required)
    {
        if (string.IsNullOrWhiteSpace(animationName))
        {
            if (required) throw new InvalidOperationException("TASK004A_CATALOG_ANIMATION_REQUIRED_MISSING path=" + sourcePath + " resource=" + resourcePath);
            return;
        }

        var asset = Resources.Load<SkeletonDataAsset>(resourcePath);
        var animation = asset != null && asset.GetSkeletonData(true) != null ? asset.GetSkeletonData(true).FindAnimation(animationName) : null;
        if (animation == null) throw new InvalidOperationException("TASK004A_CATALOG_ANIMATION_MISSING path=" + sourcePath + " resource=" + resourcePath + " animation=" + animationName);
        if (expectedTicks > 0 && ConvertSecondsToTicks(animation.Duration, resourcePath + ":" + animationName) != expectedTicks)
            throw new InvalidOperationException("TASK004A_CATALOG_ATTACK_DURATION_MISMATCH path=" + sourcePath + " resource=" + resourcePath + " animation=" + animationName);
    }

    [Serializable] private sealed class UnitCatalogDocument { public string schemaVersion; public string catalogId; public UnitCatalogEntry[] units; }
    [Serializable] private sealed class UnitCatalogEntry { public string typeId; public int legacyUnitTypeId; public string resourceKey; public string displayNameZhHans; public string skillDescriptionZhHans; public string sourceFile; public int deploymentCost; public string portraitResourcePath; public int rarity; public int initialEliteLevel; public int maxHitPoints; public int attack; public int defense; public int magicResistance; public int moveSpeedCentimetresPerSecond; public int attackIntervalTicks; public int attackAnimationDurationTicks; public string damageType; public string attackMethod; public int blockCapacity; public int tauntLevel; public int lifeDeduct; public bool isSyntheticFixtureData; public string[] innateAbilityIds; public string prefabResourcePath; public string skeletonDataResourcePath; public int unitSkelType; public string moveAnimation; public string attackAnimation; public string hitAnimation; public string deathAnimation; }
}
