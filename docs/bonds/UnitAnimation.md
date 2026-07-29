# 单位动画结构与处理方法

## 使用说明

- 本文依据 `bonds-unit-animation-audit-v1` 审计结果整理。
- 审计范围为 BONDS 使用的 `99` 个 TypeId、`182` 个资源变体。
- 标准动画结构为 `Attack / Default / Die / Idle / Move`，本文不重复列出完全符合标准结构的单位。
- 本文共列出 `26` 种非标准动画结构，覆盖 `52` 个 TypeId。
- “结构完全相同”是指动画名称集合完全一致；动画时长不参与合并，仍应按各资源分别读取。
- 所有 `Default`、`Default2`、`Default3`、`Default_A`、`Default_B` 及带颜色后缀的 `Default_*` 动画只保留在资源审计记录中；单位 JSON 不导入这些字段，运行时也不播放。
- 同一 TypeId 的普通与精英资源结构默认一致；`1131` 是例外，只有 `1131_sbeast_2` 属于本文列出的非标准结构。
- 每个条目下的“处理方法”由后续人工补充。

## UA-01

动画结构：

`Attack / Default / Die / Idle / Move_Begin / Move_End / Move_Loop / Run_Begin / Run_End / Run_Loop`

单位：

- `1000` 猎狗

处理方法：

Run_Loop 当 Move 用。


## UA-02

动画结构：

`Attack / Attack2 / Default / Die / Idle / Move_Begin / Move_End / Move_Loop`

单位：

- `1001` 大鲍勃

处理方法：

Move_Loop 当 Move 用。
Attack2 当 Attack 用。

## UA-03

动画结构：

`Attack / Default / Die / Idle / Move_Begin / Move_End / Move_Loop`

单位：

- `1006` 重装防御者
- `1061` 宿主重装士兵
- `1081` 游击队盾卫
- `1108` 乌萨斯裂兽
- `1170` 深池重甲卫士

处理方法：

Move_Loop 当 Move 用。

## UA-04

动画结构：

`Default / Die / Idle / Move_Begin / Move_End / Move_Loop`

单位：

- `1008` 幽灵
- `1042` 寒霜

处理方法：
注：该单位本身就不攻击。
Move_Loop 当 Move 用。

## UA-05

动画结构：

`Default / Die / Idle / Move`

单位：

- `1017` 御4
- `1026` 幽灵组长
- `1146` 铁砧
- `1355` 护障

处理方法：

该单位本身就不攻击。

## UA-06

动画结构：

`Attack / Default / Die / Idle / Run_Loop`

单位：

- `1043` 宿主士兵
- `1077` 游击队猎犬
- `1087` 冬灵猎犬
- `1165` 深池侦察犬

处理方法：

Run_Loop 当 Move 用。

## UA-07

动画结构：

`Attack / Combat / Default / Die / Idle / Move`

单位：

- `1107` 感染者纠察官

处理方法：

Combat 当 Attack 用。

## UA-08

动画结构：

`Attack / Combat / Default / Die / Idile / Move`

单位：

- `1111` 乌萨斯突击者

处理方法：

Combat 当 Attack 用。
Idile 当 Idle 用。

## UA-09

动画结构：

`Attack / Attack2 / Attack3 / Default / Default2 / Default3 / Die / Die2 / Die3 / Idle / Idle2 / Idle3 / Move / Move2 / Move3`

单位：

- `1116` 普通囚犯
- `1119` 强壮囚犯

处理方法：

根据单位能力：
第二次攻击完成之前使用3，第三次攻击完成前使用2，第三次攻击完成后使用原名的。

## UA-10

动画结构：

`Attack_grey / Attack_orange / Attack_red / Default_grey / Default_orange / Default_red / Die_grey / Die_orange / Die_red / Idle_grey / Idle_orange / Idle_red / Move_grey / Move_orange / Move_red`

单位：

- `1118` 拳手囚犯
- `1121` 重犯

处理方法：

根据单位能力：
第二次攻击完成之前使用_grey，第三次攻击完成前使用_orange，第三次攻击完成后使用_red。

## UA-11

动画结构：

`Attack / Default / Die / Idle / Move / Move1`

单位：

- `1131` 变异沙地兽（仅 `1131_sbeast_2`）

处理方法：

使用Move，不使用Move1。

## UA-12

动画结构：

`Attack / Default / Die / Idle / Move / Start`

单位：

- `1083` 游击队突袭战士
- `1137` 畸变赘生物
- `1138` 畸变恶性瘤
- `1246` 黑水源石虫
- `1333` “交通警察”
- `2035` 箱形恐鱼

处理方法：

忽略Start。

## UA-13

动画结构：

`Attack / Default / Die / Idle / Move / Skill_Begin / Skill_End / Skill_Loop`

单位：

- `1243` 残党萨克斯手
- `1322` “灰礼帽”

处理方法：

1243的能力不启用。
1322的能力，按 Skill_Begin / Skill_Loop / Skill_End 的顺序各播放一次，完整共播放1秒。能力相当于用该动画序列取代一次Attack播放。

## UA-14

动画结构：

`Attack_A / Attack_B / Default / Die / Idle / Move`

单位：

- `1254` 实验用动力装甲
- `2046` 木裂战士

处理方法：

第一次攻击使用 Attack_A，之后按 Attack_A / Attack_B 循环交替使用。

## UA-15

动画结构：

`Attack / Default / Die_1 / Die_2 / Idle / Move`

单位：

- `1281` 家族专用轿车

处理方法：

Die_2 当 Die 用。

## UA-16

动画结构：

`Attack / Default / Default_B / Die_A / Die_B / Idle_A / Idle_B / Move_A / Move_B`

单位：

- `1314` 寻路密探
- `1315` 岗哨密探
- `1316` 武装密探

处理方法：

全部使用_B.

## UA-17

动画结构：

`Attack / Default / Die / Idle / Move / Run`

单位：

- `2043` 染污躯壳

处理方法：

忽略Run。

## UA-18

动画结构：

`Attack / Die / Idle / Move / Skill / animation`

单位：

- `5503` 果冻小子

处理方法：

animation 当 Default 用。
触发能力的时候播放Skill，并停止其他动作。

## UA-19

动画结构：

`Attack_A / Attack_B / Default / Die_A / Die_B / Idle_A / Idle_B / Move_A / Move_B / Skill_Begin`

单位：

- `10001` 简饲源石虫

处理方法：

触发能力前使用_A
触发能力时播放Skill_Begin
触发能力后使用_B

## UA-20

动画结构：

`Default / Die / Idle / Move / Skill_Begin / Skill_End / Skill_Loop / Start`

单位：

- `10002` 浆果虫

处理方法：

进场后依次播放Start、Skill_Begin、Skill_Loop（循环播放），逃跑时播放Skill_End。
不妨碍正常死亡Die。

## UA-21

动画结构：

`Attack_A / Attack_B / Default / Die_A / Die_B / Idle_A / Idle_B / Move_A / Move_B / Skill_A / Skill_B`

单位：

- `10004` 浆果磐蟹

处理方法：

触发能力前使用_A
触发能力时播放 Skill_A，停止其他动作
触发能力后使用_B

## UA-22

动画结构：

`Attack / Default / Die / Idle / Move1 / Move2`

单位：

- `10005` 染污战士

处理方法：

Move1 当 Move 用。

## UA-23

动画结构：

`Attack / Default / Die / Idle / Move_1 / Move_2`

单位：

- `10006` 染秽魔像

处理方法：

触发能力前使用Move_1，触发能力后使用Move_2。

## UA-24

动画结构：

`Attack / Default / Die / Idle / Move / Skill`

单位：

- `10039` “开怀畅饮”
- `10077` 内测版自助出餐终端
- `2031` 塑路者
- `2033` 塑路者分形

处理方法：

10039、10077触发能力时播放 Skill，但不阻断攻击。
2031、2033的Skill用于带技能效果的特殊攻击。

## UA-25

动画结构：

`A_Attack / A_Default / A_Die / A_Idle / A_Move / B_Attack / B_Default / B_Die / B_Idle / B_Move / Trans`

单位：

- `10126` 异质源石虫
- `10127` 异质裂兽

处理方法：

全部使用A_

## UA-26

动画结构：

`Appear / Attack / Default / Die / Disappear / Idle / Move`

单位：

- `1502` 弑君者

处理方法：

单位 JSON 同时保存 `Appear` 与 `Disappear` 的真实动画时长，但数据层不规定两者的播放次序。闪现能力的播放行为和次序由后续动画层实现前另行确认。

## 待销毁的旧 Hit 接口

新的 Track 播放链中，Damage 只更新生命值，不产生 Hit 动作，也不播放受击动画。单位源数据和生成目录不再保存 `hitAnimation`。

以下旧接口与调用点应在兼容路径迁移完成后删除，不能永久保留为空实现：

- `IBattlePresentationView.PlayHit()`；
- `UnitSkelPresentationView.PlayHit()`；
- `UnitSkelPresentationView` 的 `hitAnimation` 序列化字段；
- `UnitSkelPresentationView.ConfigureAnimations(...)` 的 Hit 参数；
- `BattleEventPlaybackController` 在 Damage 事件中对 `PlayHit()` 的调用；
- `UnitCatalogEntry.HitAnimation`、目录 DTO 的 `hitAnimation` 以及相关构造参数；
- 所有只为满足 `PlayHit()` 接口而存在的测试替身方法。

销毁条件：所有正式和兼容播放路径都遵守“Damage 仅更新生命值”的规则，且相关 EditMode、PlayMode 回归测试已迁移。
