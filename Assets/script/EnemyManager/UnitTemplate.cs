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
    public float BlockRadius;      //接敌半径

    [Header("动画/状态")]
    public List<State> states;     // 状态机枚举列表
    public RuntimeAnimatorController animator; // 动画控制器
    public bool isBlock;           // 是否可阻挡

    [Header("外观")]
    public Sprite icon;            // 图标/立绘
    public GameObject prefab;      // 敌人预制体
    public UnitTemplate targetenemy;//索敌
}
public enum State { Idle, Move, Attack, Death, Skill, Default }
