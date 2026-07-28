using System;
using System.Collections.Generic;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]

[Serializable]
internal sealed class UnitEliteVariantsV2Document
{
    public string schemaVersion;
    public int typeId;
    public UnitCommonSource common;
    public UnitVariantSource[] variants;
}

[Serializable]
internal sealed class UnitCommonSource
{
    public int rarity;
    public int deploymentCost;
    public int attackMethod;
    public int actionMethod;
    public float attackRadiusMetres;
    public float blockRadiusMetres;
    public bool canBlock;
    public int blockCapacity;
    public int tauntLevel;
    public string damageType;
}

[Serializable]
internal sealed class UnitVariantSource
{
    public int minEliteLevel;
    public string sourceVariant;
    public int statsLevel;
    public string displayNameZhHans;
    public string skillDescriptionZhHans;
    public UnitVariantStatsSource stats;
    public UnitModelSource model;
    public string[] innateAbilityIds;
}

[Serializable]
internal sealed class UnitVariantStatsSource
{
    public UnitCombatStatsSource combat;
    public UnitSharedStatsSource shared;
}

[Serializable]
internal sealed class UnitCombatStatsSource
{
    public int maxHitPoints;
    public int attack;
    public int defense;
    public int magicResistance;
}

[Serializable]
internal sealed class UnitSharedStatsSource
{
    public float moveSpeedMetresPerSecond;
    public float attackIntervalSeconds;
    public int lifeDeduct;
}

[Serializable]
internal sealed class UnitModelSource
{
    public string resourceKey;
    public string skeletonDataResourceName;
    public string profilePictureResourceName;
    public UnitAnimationBinding[] animations;
}

[Serializable]
public sealed class ResolvedUnitVariant
{
    public const float BaseAttackIntervalMultiplier = 0.5f;

    public int typeId;
    public int minEliteLevel;
    public string sourceVariant;
    public int statsLevel;
    public string displayNameZhHans;
    public string skillDescriptionZhHans;

    public int rarity;
    public int deploymentCost;
    public int attackMethod;
    public int actionMethod;
    public float attackRadiusMetres;
    public float blockRadiusMetres;
    public bool canBlock;
    public int blockCapacity;
    public int tauntLevel;
    public string damageType;

    public int maxHitPoints;
    public int attack;
    public int defense;
    public int magicResistance;
    public float moveSpeedMetresPerSecond;
    public float attackIntervalSeconds;
    public int lifeDeduct;

    public string resourceKey;
    public string skeletonDataResourceName;
    public string profilePictureResourceName;
    public List<UnitAnimationBinding> animations;
    public List<string> innateAbilityIds;

    public float BaseAttackIntervalSeconds =>
        attackIntervalSeconds * BaseAttackIntervalMultiplier;

    public UnitAnimationBinding FindAnimation(string key)
    {
        if (animations == null)
        {
            return null;
        }

        foreach (var animation in animations)
        {
            if (animation != null
                && string.Equals(animation.key, key, StringComparison.Ordinal))
            {
                return animation;
            }
        }

        return null;
    }

    public UnitAnimationBinding RequireAnimation(string key, string context)
    {
        var animation = FindAnimation(key);
        if (animation == null)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_ANIMATION_REQUIRED_MISSING"
                + " key=" + key
                + " context=" + context);
        }

        return animation;
    }
}

[Serializable]
public sealed class UnitAnimationBinding
{
    public string key;
    public string name;
    public float durationSeconds;
}
