using System;
using System.Collections;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
[CreateAssetMenu(menuName = "Unit/Template", fileName = "NewEnemyTemplate")]
public class UnitTemplate : ScriptableObject
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
    public List<string> FixedAbility = new();  // 例如 ["Burn","Freeze"]

    // --- 运行时掩码：隐藏但可序列化，由工具/加载流程写入 ---
    [SerializeField, HideInInspector]
    private ulong[] abilityMaskBits = Array.Empty<ulong>();

    /// <summary>
    /// 只读访问（避免直接改数组）。需要拷贝请自行 ToArray()。
    /// </summary>
    public IReadOnlyList<ulong> AbilityMaskBits => abilityMaskBits;

    /// <summary>
    /// 查询：某能力索引是否被置位（索引由工具侧定义）。
    /// </summary>
    public bool HasAbilityIndex(int abilityIndex)
        => BitSet64.Get(abilityMaskBits, abilityIndex);

#if UNITY_EDITOR
    /// <summary>
    /// （编辑器/构建期）整体写入掩码；工具根据 FixedAbility 计算后调用。
    /// </summary>
    internal void SetAbilityMask(ReadOnlySpan<ulong> src)
    {
        abilityMaskBits = src.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// （编辑器/构建期）按能力索引置位/清位（可选，给你的生成脚本用）
    /// </summary>
    internal void SetAbilityIndex(int abilityIndex, bool value = true)
    {
        BitSet64.Set(ref abilityMaskBits, abilityIndex, value);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}

/// <summary>
/// 多段 64 位 BitSet 的简易工具
/// </summary>
public static class BitSet64
{
    public static void Set(ref ulong[] bits, int index, bool value = true)
    {
        if (index < 0) return;
        int seg = index >> 6;       // /64
        int off = index & 63;       // %64
        EnsureCapacity(ref bits, seg + 1);
        ulong mask = 1UL << off;
        if (value) bits[seg] |= mask;
        else bits[seg] &= ~mask;
    }

    public static bool Get(ulong[] bits, int index)
    {
        if (bits == null || index < 0) return false;
        int seg = index >> 6;
        if (seg >= bits.Length) return false;
        int off = index & 63;
        return (bits[seg] & (1UL << off)) != 0;
    }

    public static void EnsureCapacity(ref ulong[] bits, int segCount)
    {
        if (bits == null) { bits = new ulong[segCount]; return; }
        if (bits.Length < segCount) Array.Resize(ref bits, segCount);
    }
}
public enum State { Idle, Move, Attack, Death, Skill, Default }
