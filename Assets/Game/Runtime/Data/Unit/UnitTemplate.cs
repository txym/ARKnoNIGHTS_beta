using Bitsets;
using System;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName = "Game/Unit/Template")]
public partial class UnitTemplate : ScriptableObject
{
    [Header("必须字段")]
    public int typeID = 1000;                 // 主键
    public string uintName = "gopro";
    public string ProfilePicture = "UIImage_gopro";
    public int attackMethod = 0b01;    // 攻击方式
    public int actionMethod = 0b01;    // 行动方式
    public int unitskeltype = 1;       //动画机种类
    public int Rarity = 1;             //稀有度
    public int cost = 2;               //部署费用

    [Header("战斗数值")]
    public int HP = 3000;
    public int atk = 370;
    public int def = 0;
    public int res = 20;                //法抗
    public float moveSpeed = 1.9f;
    public int narrowTitle = 0;        // 基础嘲讽等级
    public int LifeDeduct = 1;         // 目标价值
    public float attackInterval = 1.4f;   // 攻击间隔
    public float attackRadius = 0;     // 攻击半径
    public float BlockRadius = 0.1f;      // 接敌半径
    public bool isBlock = true;           // 是否可阻挡

    [Header("能力（可配置的名称清单）")]
    public List<string> FixedAbility = new();         // 填 tag
    [SerializeField, HideInInspector] private Bitsets.TagMask innateMask; // 自动烘焙

    // 运行时查询
    public bool HasInnate(string tag, TagRegistry registry)
        => innateMask.HasTag(tag, registry);
    public bool HasInnateIndex(int idx)
        => innateMask.HasIndex(idx);

}

