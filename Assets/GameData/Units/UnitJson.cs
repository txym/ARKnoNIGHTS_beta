[System.Serializable]
public class UnitJson
{
    // unit-source-v1. This is the sole source-data contract; UnitFactory maps it to legacy UnitTemplate fields.
    public string schemaVersion;
    public int typeId;
    public string resourceKey;
    public string displayNameZhHans;
    public string skillDescriptionZhHans;

    public int rarity;
    public int deploymentCost;
    public int initialEliteLevel;

    public int attackMethod;
    public int actionMethod;
    public int unitSkeletonType;

    public int maxHitPoints;
    public int attack;
    public int defense;
    public int magicResistance;
    public float moveSpeedMetresPerSecond;
    public float attackIntervalSeconds;
    public float attackAnimationDurationSeconds;
    public float attackRadiusMetres;
    public float blockRadiusMetres;

    public bool canBlock;
    public int blockCapacity;
    public int tauntLevel;
    public int lifeDeduct;
    public string damageType;
    public System.Collections.Generic.List<string> innateAbilityIds;

    public string skeletonDataResourceName;
    public string profilePictureResourceName;
    public string moveAnimation;
    public string attackAnimation;
    public string hitAnimation;
    public string deathAnimation;
}
