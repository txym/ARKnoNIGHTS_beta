[System.Serializable]
public class UnitJson
{
    public int id;
    public string uintName;
    public string skeletonData;
    public string ProfilePicture;
    public int Rarity;
    public int cost;

    public int attackMethod;
    public int actionMethod;
    public int unitskeltype;

    public int HP;
    public int atk;
    public int def;
    public int res;

    public float attackInterval;
    public float attackRadius;
    public float BlockRadius;

    public float moveSpeed;
    public bool isBlock;

    public System.Collections.Generic.List<string> FixedAbility;

    public int LifeDeduct;
    public int narrowTitle;
}
