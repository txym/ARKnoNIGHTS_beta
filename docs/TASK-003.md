# TASK-003：完整确定性单场战斗计算与结构化事件

> 历史状态与后置回测：本任务已经以合成 1v1 fixture 形成确定性计算与事件闭环。新增 `docs/TASK-004A.md` 将使用真实 `gopro`/`arcslma` type ID 和数值运行至少 10 次回归；合成 fixture 不删除，且只有真实数据揭示通用算法/输入缺陷时才允许最小修正本任务产物。

## 角色

你是本 Unity 项目的权威战斗计算实现 Agent。

你的职责是在 TASK-002 已建立的输入、定点坐标、运行时状态和 Tick runner 上，完成第一阶段完整的单场战斗计算闭环，并输出表现层可以只读消费的结构化事件和最终结果。

本任务把移动、索敌、阻挡、攻击、伤害、死亡、胜负和事件视为同一个持续迭代整体。不要再拆成彼此不可运行的横向半成品；应先完成一份最小 1v1 fixture 的纵向闭环，再扩展已确认边界。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `docs/TASK-002.md`
- `docs/decisions/`（若存在）
- TASK-002 完成报告、测试结果、实际程序集/类型清单、fixture schema 和 runner 阶段说明
- TASK-002 实际新增的 Battle Core、Infrastructure、fixture 和 EditMode 测试文件
- `Assets/GameData/Units/UnitJson.cs`
- `Assets/GameData/Units/Json/1000_gopro.json`
- `Assets/GameData/Units/Json/5503_arcslma.json`
- `Assets/GameData/Units/Json/5504_arcslmi.json`
- `Assets/Game/Runtime/Data/Unit/UnitTemplate.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`（只用于了解后续表现需求，不得让 Core 依赖它）

开始前必须执行 `git status --short`，保护用户修改。检查 TASK-002 是否已经真实完成：Core/测试程序集可编译、相关测试数大于 0、fixture 可加载、runner 可显式推进。若这些前置不存在，不要在本任务中重新发明另一套基础模型；先报告前置缺口。

## 任务背景

TASK-002 应已建立：

- 与 UnityEngine 隔离的 Battle Core；
- 一基阵型/战场坐标和整数定点逻辑位置；
- 双方不可变输入、单位类型定义、完整单位快照和主客场映射；
- 版本化固定 fixture；
- 20 TPS、显式 Step 的 runner 骨架；
- `maxTicks` 未解决停止状态；
- 稳定摘要和最小 EditMode 测试。

当前任务需要在这些实际接口上建立权威战斗行为。不能使用 Unity Physics、`MonoBehaviour.Update`、`Time.deltaTime`、协程、渲染帧率或 Spine 动画状态决定结果。

`docs/SPEC.md` 仍可能把已经由项目负责人确认的 Tick、重新索敌和伤害取整写成待补充。本提示词的“已确认规则”属于当前任务明确说明。开始行为实现前，先同步 SPEC 中与本任务直接相关的条目，并同步 TEST_PLAN；不得让实现 Agent面对两个相反事实源。

## 本任务目标

1. 在现有 runner 内确定并实现稳定、可审查的 Tick 阶段顺序。
2. 使用整数定点逻辑位置实现朝当前目标的直线移动，不越过目标。
3. 实现最近存活敌人索敌、同距离决胜和目标死亡后的下一 Tick 重索敌。
4. 实现 `0.25` 米半径内的对称阻挡、默认容量 1、移动停止、关系解除和死亡清理。
5. 实现首次攻击、攻击间隔、下一次允许攻击 Tick、攻击动画结束 Tick 出伤和待结算攻击队列。
6. 实现物理、法术和真实伤害、最低 5% 伤害及整数向下取整。
7. 同一 Tick 到达的有效攻击同时计算和应用伤害，再统一判定死亡。
8. 攻击者或目标在出伤 Tick 前死亡时，移除该待结算攻击；保留已经产生的 Attack 事件，不产生 Damage 事件。
9. 实现当前生命值、死亡后停止行动、目标/阻挡清理和正常单场胜方。
10. 对双方同时全灭、`maxTicks` 或其他无法产生唯一胜方的情况返回明确未解决结果，不伪造胜方。
11. 输出不可变且严格有序的 Spawn、Move、TargetChanged、BlockStarted、BlockEnded、Attack、Damage、Death 和 BattleEnded 事件。
12. 提供可重复的最终状态摘要和事件摘要；相同输入多次运行必须逐项一致。

## 已确认规则

### 逻辑帧与权威性

- Tick 即逻辑帧，固定为 `20 TPS`，每 Tick `0.05` 秒。
- 位置、目标、阻挡、攻击、HP、死亡和结果只能在 Tick 阶段内改变。
- 权威计算不得依赖 Unity 物理、渲染时间、Spine、GameObject 或实际帧率。
- 同一输入和配置必须产生同一状态、事件顺序和结果。
- 单位在权威计算开始时已经位于主客场映射后的阵型位置；从门走到阵型属于可省略的演示前奏。

### 移动与索敌

- 单位朝当前锁定目标的当前位置做确定性直线移动。
- 当前版本没有远程单位。
- 目标选择第一关键字：目标当前位置到自身当前位置的欧氏距离平方，较小者优先。
- 第二关键字：目标当前位置到“索敌单位所属阵营自己的门”的欧氏距离平方，较小者优先。
  - Home/Blue 自己的门为 `(5,1)`；
  - Away/Red 自己的门为 `(5,8)`。
- 第三关键字：目标 unit ID，使用稳定升序。
- 目标死亡后，本 Tick 不立即重选；下一 Tick 的索敌阶段再重选。
- `moveSpeed` 为米/秒。移动使用 TASK-002 的定点尺度和 20 TPS 换算，不使用浮点权威状态。

### 阻挡与攻击范围

- 阻挡关系是对称的；第一阶段全局阻挡半径为 `0.25` 米。
- 阻挡容量默认 1。正式第一阶段 fixture 必须规避多个单位同 Tick 竞争同一容量的情况。
- 单位与当前目标的距离严格小于 `0.25` 米且容量可用时建立关系；距离恰好等于 `0.25` 米时不建立阻挡。已阻挡双方停止相向移动。
- 一个单位碰到“容量已满且不是自己当前目标”的单位时，不因此停止移动。
- 阻挡判定完成后，同一 Tick 可以开始攻击。
- 攻击不要求已经建立阻挡关系，但目标必须满足同一个“距离严格小于 `0.25` 米”的范围判定并且攻击冷却完成。
- 死亡或关系不再有效时必须双向清理阻挡；多单位竞争和复杂位移解除顺序仍未确认，不得通过额外 fixture 固化。

### 攻击排程

- `attackMethod` 只表示近战/远程；当前 fixture 只使用近战。
- `attackInterval` 是两次攻击动画开始之间的最短时间。
- 每个单位维护“下一次允许开始攻击的最早 Tick”；首次进入攻击条件且没有冷却时可立即开始攻击。
- 攻击开始时生成 Attack 事件并记录攻击者、目标、开始 Tick、计划出伤 Tick和表现所需时长。
- 伤害在“调整后的攻击动画结束 Tick”进入结算。
- 若原始 `attackAnimationDuration > attackInterval`，有效攻击动画时长压缩为不长于攻击间隔；事件必须保留足够信息，使表现层能够按理论攻击间隔加速动画。
- 正式 fixture 的攻击间隔和动画时长必须是整数 Tick；本任务不决定非整数 Tick 的取整规则。
- 攻击开始后，即使目标在出伤前死亡，Attack 动画仍应能在表现层播放完；Core 移除待结算项且不产生 Damage。
- 攻击者在出伤前死亡时同样移除该待结算项。
- 同一 Tick 到达的攻击以该结算阶段开始时的存活状态判断有效性；有效攻击同时出伤，再统一死亡。因此在同一批次中将被杀死的攻击者仍能完成已经到达的有效伤害。

### 伤害与死亡

- 单位定义具有独立 DamageType：Physical、Arts、True 或语义等价枚举。
- 当前现有单位默认按物理伤害，但计算器必须支持三类伤害。
- 权威伤害使用整数，不使用浮点并向下取整：
  - 物理：`max(atk - def, floor(atk * 5 / 100))`；
  - 法术：`max(floor(atk * (100 - res) / 100), floor(atk * 5 / 100))`；
  - 真实：直接使用本次攻击指定的整数伤害。
- 正式 fixture 只使用非负攻击/防御和 `0..100` 法抗；异常属性的正式规则未确认，不得通过钳制或特殊公式固化。
- 同 Tick 所有有效 Damage 先计算，再汇总应用 HP，再统一产生 Death。
- 死亡单位从下一阶段开始不再移动、索敌、阻挡或发起攻击。

### 结果与事件

- 一方全部参战单位死亡且另一方仍有存活单位时，存活方是本场唯一胜方。
- 双方同时全灭、达到 `maxTicks` 或其他无法唯一判断时返回 Unresolved/等价状态和明确 reason。
- 事件至少包含：Spawn、Move、TargetChanged、BlockStarted、BlockEnded、Attack、Damage、Death、BattleEnded。
- 每个事件必须包含 Tick 和 Tick 内严格递增 sequence；排序不能依赖集合枚举或运行时 `GetHashCode()`。
- 主客场观察者共享同一事件流和结果；视角旋转不属于 Core。

## 不属于本任务的内容

- 不实现寻路、地形绕行、门到阵型的入场移动；
- 不实现远程单位、技能、Buff 效果、治疗、护盾、召唤、复活、嘲讽或特殊能力；
- 不实现多单位竞争阻挡容量的正式优先级；
- 不决定双方同时全灭、正式超时或平局的胜负；
- 不实现玩家生命扣除、准备、商店、部署交互、完整回合或多人同步；
- 不实现 Unity GameObject、世界坐标、动画或场景控制器；
- 不修改现有 UnitFactory、UnitSkel、部署、UI、场景、Prefab 或 Spine 资源；
- 不替换 TASK-002 的输入 schema、定点尺度或程序集，除非存在无法兼容的明确缺陷并先报告迁移影响；
- 不安装第三方数学、模拟、序列化或测试框架。

## 预计影响文件或目录

应优先限制在 TASK-002 实际建立的区域，例如：

- `Assets/Game/Battle/Core/Simulation/`
- `Assets/Game/Battle/Core/Spatial/`
- `Assets/Game/Battle/Core/Targeting/`
- `Assets/Game/Battle/Core/Blocking/`
- `Assets/Game/Battle/Core/Combat/`
- `Assets/Game/Battle/Core/Damage/`
- `Assets/Game/Battle/Core/Events/`
- `Assets/Game/Battle/Core/Result/`
- `Assets/Game/Tests/EditMode/Battle/`
- 第一阶段 fixture（只做本任务所需的受控更新）
- 与本任务事实直接相关的 `docs/SPEC.md`、`docs/ARCHITECTURE.md`、`docs/TEST_PLAN.md`

实际目录和类型必须以 TASK-002 产物为准。若需要扩大到 Unity 表现、场景、Prefab、现有 Runtime/UI/Debug，停止并报告。

## 实施要求

### A. 先建立最小纵向闭环

1. 选择一份不会触发未确认机制的 1v1 fixture：不同 unit ID、合法位置、无 Buff 效果、容量 1、时间为整数 Tick、最终能产生唯一胜方。
2. 在开始大规模编码前写出 runner 的 Tick 阶段和每阶段读写状态清单。
3. 推荐采用以下稳定阶段；如 TASK-002 已有等价阶段，优先兼容而不是改名：
   1. 清理在本 Tick 前已失效的待结算攻击；
   2. 为无有效目标的存活单位索敌；
   3. 基于阶段开始快照计算移动意图；
   4. 批量应用移动；
   5. 评估阻挡建立/解除；
   6. 评估并记录攻击开始；
   7. 收集本 Tick 到达且有效的攻击，批量计算和应用伤害；
   8. 统一判定死亡并清理目标/阻挡/待结算关系；
   9. 判断战斗终止并封存事件。
4. 若上述顺序与已经确认的规则产生冲突，停止询问；不要静默调换以让测试通过。
5. 单位集合的内部遍历必须有稳定顺序。对本应同时发生的移动和伤害使用快照/意图/批处理，不能让遍历顺序决定结果。

### B. 移动、索敌和阻挡

6. 使用整数平方距离进行索敌和门距离比较，避免开平方。
7. 实现确定性定点直线位移；低速单位的分数余量必须以稳定方式累计或由等价算法保留，长期平均速度符合 `moveSpeed`。
8. 单 Tick 位移不能越过目标；距离为零时不能除零。
9. 目标选择只考虑敌方存活参战单位。
10. 目标变化时只在实际 ID 变化时产生 TargetChanged。
11. 阻挡关系必须双向一致，不能存在 A 记录 B 而 B 未记录 A。
12. 正式 fixture 遇到未定义的多单位容量竞争时，应返回明确 unsupported/unresolved 诊断或由输入约束拒绝；不得按集合顺序偷偷选择赢家。

### C. 攻击、伤害和死亡

13. `nextAttackAllowedTick` 属于单位运行时状态，不因目标变化自动重置。
14. Attack 开始和 Damage 到达是两个不同事实；pending attack 使用不可变记录或受控状态。
15. 有效动画 Tick 为 `min(originalAnimationTicks, attackIntervalTicks)`；事件保留原始/有效时长或等价的精确播放比例。
16. 每 Tick 先收集全部到达攻击，再基于结算阶段开始时的存活状态过滤，不能一条一条扣血并立即死亡。
17. Damage 事件至少能核对来源、目标、类型、数值和 HP 变化；Death 事件只产生一次。
18. 死亡清理不得删除已经发生的 Attack 事件；被取消的 pending attack 不产生 Damage。
19. BattleEnded 必须与最终状态一致；Unresolved 不能包含伪造 winner。

### D. 事件与摘要

20. 事件 DTO 不引用运行时可变对象、Unity 类型或表现对象。
21. Spawn 事件在确定的初始 Tick/序列输出；Move 仅在位置实际变化时输出。
22. 为同 Tick 事件定义稳定类别顺序和 ID 排序。事件顺序是公共契约，必须有测试。
23. 事件集合和结果封存后只读；演示层以后不能修改。
24. 规范摘要/稳定哈希使用显式字段、固定排序和跨运行稳定算法，不使用默认对象哈希。

### E. 文档与检查点

25. 在单次任务中保留至少三个可审查检查点：
   - 1v1 移动/索敌/阻挡；
   - 1v1 攻击到唯一胜方；
   - 完整事件和重复性回归。
26. 每个检查点保持项目可编译，不提交临时空实现或恒真分支。
27. 同步 SPEC 的已确认规则、ARCHITECTURE 的实际 Core 行为和 TEST_PLAN 的真实测试清单/结果。

## 兼容与迁移要求

- 采用 TASK-002 实际输入、坐标、定点和 runner 契约；必要扩展应向后兼容 fixture schema，schema 变化必须显式增版。
- Core 不引用 `UnitTemplate`、`UnitIdentity`、`UnitSkelBase`、`UnitFactory` 或任何 Unity 类型。
- 不修改旧原型的移动协程、部署坐标或 UI。
- 不让表现需要反向进入 Core；事件只携带逻辑事实和播放所需的确定数据。
- 已存在的两个源单位 JSON 不作为权威 runtime state；fixture 转换仍是边界。
- 不移动/删除现有资源或 `.meta`；新增文件必须有对应 `.meta`。

## 验证要求

### 自动验证

至少覆盖：

1. 固定点移动：零距离、低速余量、一步不足、恰好到达、不越过目标；
2. 索敌：唯一最近、距离相同按己方门距离、仍同距按 unit ID、无敌人、目标死亡后下一 Tick 重选；
3. 阻挡：距离 `<0.25` 建立、距离 `=0.25` 不建立、对称关系、容量 1、非目标满容量单位不停车、死亡双向清理；
4. 攻击：同 Tick 阻挡后可开攻、首次攻击、冷却、下次最早 Tick、动画时长压缩、出伤 Tick；
5. 取消：攻击者提前死亡、目标提前死亡，保留 Attack 且无 Damage；
6. 伤害表：物理、法术、真实、5% 下限和整数向下取整；
7. 同 Tick 批量伤害：先全部 Damage，再统一 Death；同批次将死亡攻击者的已到达攻击仍有效；
8. 死亡后不再行动、目标/阻挡/pending 清理；
9. 一方全灭产生唯一 Home/Away winner；同时全灭和 maxTicks 返回 Unresolved；
10. 事件类型、载荷、`(tick,sequence)` 严格顺序和最终状态交叉核对；
11. 同一 fixture 完整运行至少 10 次，事件逐项、最终状态、winner/reason 和稳定摘要一致；
12. Core 仍无 UnityEngine/Physics/Time/Spine 依赖。

正式验收 fixture 不得触发尚未确认的多单位阻挡竞争、异常属性或非整数 Tick 时间。

### Unity 编译或运行验证

- 执行 Unity Editor 编译；
- 执行相关 EditMode 测试并保存 XML/日志；
- 不要求 Play Mode 或 Standalone 构建；
- 若全项目 Standalone 仍被已知 `UITest` 阻塞，记录但不修复。

### 人工检查

- 审查 Tick 阶段是否与文档一致；
- 搜索 Core 中是否出现 `UnityEngine`、`Physics`、`Time`、`MonoBehaviour`、Spine 或浮点权威状态；
- 审查集合遍历和事件排序是否稳定；
- 核对 fixture 未触发暂缓机制；
- 检查 diff 没有场景、Prefab、UI、旧 Runtime 或 Package 意外变化。

### 无法执行时

提供实际命令、退出码、日志、XML 状态、失败测试和首个有效根因。未执行、0 tests、日志不完整或结果文件缺失均写“未验证”。

## 验收标准

- 固定 1v1 fixture 能从不可变输入完整计算到唯一胜方；
- 权威状态只由显式 20 TPS Tick 推进；
- 移动、索敌、阻挡、攻击、三类伤害、HP、死亡和结果均符合已确认规则；
- 目标死亡后下一 Tick 重选；阻挡后同 Tick 可开攻；
- 攻击在有效动画结束 Tick 出伤，提前死亡会取消 pending Damage 但保留 Attack；
- 同 Tick 到达攻击先同时伤害再统一死亡；
- 事件流包含要求的全部类型，严格有序且不可变；
- 相同输入 10 次得到逐项相同事件、最终状态和结果；
- `maxTicks`、同时全灭和不支持情况不会伪造 winner；
- 相关 EditMode 测试数大于 0、失败为 0，Editor 无新增编译错误；
- Core 不依赖 UnityEngine、Physics、Time、Spine 或渲染状态；
- 没有修改表现、场景、Prefab、旧部署/UI 或多人系统；
- 文档与实际实现一致。

## 停止并询问我的条件

- TASK-002 输入、定点、fixture 或 runner 前置不完整且需要另建不兼容体系；
- 需要决定非整数 Tick 时间的取整；
- 测试或正式 fixture 无法避免多单位同 Tick 竞争阻挡容量；
- 必须决定双方同时全灭、正式超时或平局 winner；
- 现有数据要求处理异常防御/法抗、Buff、技能、远程或特殊能力；
- SPEC、当前提示词和现有测试对同一战斗行为实质冲突；
- 需要修改 Unity 表现、场景、Prefab、Package 或第三方依赖；
- 需要改变已冻结的公共输入 schema 且无法兼容迁移；
- 发现用户修改与模拟主循环或测试重叠且无法安全保留；
- 连续三次有实质差异的尝试仍无法推进。

## 完成报告格式

1. 修改文件；
2. 采用的实际 Tick 阶段顺序；
3. 移动、索敌、阻挡实现；
4. 攻击、伤害、死亡和结果实现；
5. 事件 DTO、顺序和稳定摘要；
6. fixture 及其未触发的暂缓机制；
7. 实际运行命令；
8. Editor 编译结果；
9. EditMode 测试总数、通过数、失败数及 XML/日志；
10. 10 次重复性结果；
11. 未验证项；
12. 所作技术假设；
13. 遗留风险；
14. 最终工作区状态；
15. TASK-004 必须采用的事件、结果、时间和坐标接口。
