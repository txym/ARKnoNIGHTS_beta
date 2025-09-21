using Bitsets;
using System;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName = "Game/Unit/Template")]
public partial class UnitTemplate : ScriptableObject
{
    [Header("必须字段")]
    public int id;                 // 主键
    public string uintName;
    public string attackMethod;    // 攻击方式
    public string actionMethod;    // 行动方式

    [Header("战斗数值")]
    public int maxHP;
    public int atk;
    public int taunt;              // 嘲讽等级
    public float moveSpeed;
    public float attackInterval;   // 攻击间隔
    public float attackRadius;     // 攻击半径
    public float BlockRadius;      // 接敌半径
    public bool isBlock;           // 是否可阻挡

    [Header("能力（可配置的名称清单）")]
    public List<string> FixedAbility = new();         // 填 tag
    [SerializeField, HideInInspector] private Bitsets.TagMask innateMask; // 自动烘焙

    // 运行时查询
    public bool HasInnate(string tag, TagRegistry registry)
        => innateMask.HasTag(tag, registry);
    public bool HasInnateIndex(int idx)
        => innateMask.HasIndex(idx);

}

public enum State { Idle, Move, Attack, Death, Skill, Default }
