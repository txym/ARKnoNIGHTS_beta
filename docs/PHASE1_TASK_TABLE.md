# 第一阶段开发任务总表

## 1. 文档用途

本文档规划“第一阶段：完整单场战斗计算 + 战斗演示 Demo”的实现顺序、任务边界、依赖关系和最小验证方式。

本文档是任务规划，不代表对应功能已经实现或验证。当前真实架构以 `docs/ARCHITECTURE.md` 为准，游戏规则以 `docs/SPEC.md` 为准，验证口径以 `docs/TEST_PLAN.md` 为准。

### 1.1 术语说明

- “Unity Player”“Standalone Player”或“Player 构建”是 Unity 对独立运行程序及其非 Editor 编译目标的称呼，不表示多人联机中的玩家。
- 游戏参与者统一称为“玩家”“主场方”或“客场方”。
- 第一阶段不实现四人房间、局域网同步、断线重连或其他多人联机功能。
- Unity 构建会编译目标平台可见的全部运行时代码。脚本即使未被战斗场景调用，只要仍属于运行时程序集且未被条件编译排除，也可能阻塞 Standalone 构建。

## 2. 当前阶段目标与边界

第一阶段只完成一条由固定本地测试数据驱动的单场战斗闭环：

```text
真实单位 JSON 源目录 + local-battle-v1 玩家对战快照
→ Player-safe unit-catalog-v1 + 不可变战斗输入与主客场映射
→ 纯 C#、20 TPS 的确定性战斗计算
→ 结构化战斗事件与结果
→ 只读的主场/客场演示投影
→ Unity 战斗演示
→ 胜方、状态和错误输出
```

其中，TASK-002/003 已有的 `battle-fixture-v1` 与 `home-striker`/`away-guard` 属于合成算法测试数据，应继续保留用于边界回归；它们不再作为真实 Spine 演示的输入。TASK-005 开始接场景之前，必须先由 TASK-004A 把现有 `Assets/GameData/Units/Json` 真实单位目录与玩家对战快照接入，并完成 TASK-001～004 的针对性回测。

当前阶段不包括：

- 商店购买；
- 准备阶段；
- 玩家手动部署的完整业务规则；
- 完整回合循环；
- 玩家生命扣除；
- 四人房间；
- 局域网同步；
- 断线重连；
- 完整技能、Buff、治疗、护盾战斗结算和远程单位；
- 美术资源整体重做。

不得为未来联网进行过度抽象，也不得为了让 Demo 看起来完整而混入上述后续系统。

## 3. 已确认的核心规则摘要

详细规则以 `docs/SPEC.md` 为准。实现 Agent 开始任务前必须重新阅读 SPEC，不得只依赖本摘要。

- 战场使用一基 `9×8` 坐标；本地阵型使用一基 `9×4` 坐标。
- 蓝门为 `(5,1)`，红门为 `(5,8)`；门格不可部署。
- 客场阵型与客场观察视角使用 180 度变换：`(x,y) → (10-x,9-y)`。
- `1 格 = 1 米 = 100 Unity 世界坐标单位`。
- Tick 即逻辑帧，固定为 `20 TPS`，每 Tick 为 `0.05` 秒。
- `moveSpeed` 的单位为米/秒；权威计算不依赖 Unity Physics、`Time.deltaTime`、渲染帧率或 Spine 状态。
- 索敌优先最近存活敌人；同距离时按“目标当前位置到索敌方所属门的欧氏距离平方”排序，再按目标 unit ID 排序。
- 目标死亡后的下一 Tick 才重新索敌。
- 阻挡为对称关系；第一版全局阻挡半径为 `0.25` 米，双方距离严格小于该值时才满足范围判定，单位阻挡容量默认 1。
- 阻挡判定完成后，同一 Tick 可以开始攻击；攻击不强制要求已建立阻挡，但目标必须满足同一个严格小于 `0.25` 米的距离判定。
- `attackInterval` 是两次攻击动画开始之间的最短间隔，单位为秒；单位维护下一次允许开始攻击的最早时间。
- 伤害在调整后的攻击动画结束 Tick 到达。同一 Tick 到达的有效攻击先同时结算伤害，再统一判定死亡。
- 攻击者或目标在出伤 Tick 前已经死亡时，移除该待结算攻击，不生成零伤害结算。
- `attackAnimationDuration > attackInterval` 时，演示层按理论攻击间隔加速动画；演示不得改变权威计算结果。
- 物理、法术、真实伤害均使用整数计算并向下取整；物理和法术伤害具有攻击力 5% 的最低伤害。
- 当前现有单位先按物理伤害处理，但模型需有独立伤害类型字段；`attackMethod` 只表示近战/远程。
- 当前 Demo 必须能判断正常的单场胜方；`maxTicks` 只产生明确的未解决诊断状态，不得伪造胜方。
- 单位使用附着于 `DefaultUnit` 的世界空间状态条：总长 `90` Unity 单位、位于脚下约 `25` Unity 单位；满血且无护盾时隐藏，受伤或有护盾时显示；血量从左侧填充，护盾为右侧白色区段，敌我颜色相对于当前观察者计算。

可由受控测试数据暂时规避的规则包括：多单位同 Tick 竞争阻挡容量、双方同时全灭、正式超时/平局规则、Buff 行为及异常属性值处理。实现不得擅自固化这些未确认机制。

## 4. TASK-001 状态与“4 个 Player 错误”的解释

TASK-001 的目标是取得真实基线，并且只处理已明确授权的 `UnitDeployment.ConvertCoordinate` 条件编译问题；它并不要求顺手修复整个项目的所有历史代码。

根据 TASK-001 完成报告，当前状态记为：**TASK-001 在允许范围内已完成；项目整体基线不是全绿，仍有已知失败项和未验证项。**

已获得的结果：

- `UnitDeployment.ConvertCoordinate` 已移出 `#if UNITY_EDITOR`，公式和签名未变；Gizmos 仍为 Editor 专用。
- Unity 2022.3.62f1c1 Editor batchmode 编译返回码为 0。
- Windows Standalone 构建已实际执行并失败，返回码为 1。
- 最初的 `ConvertCoordinate` 非 Editor 编译错误已消失。
- 当前无第一方自动化测试；不能把 0 个测试记为测试通过。
- 两个 Editor 源 JSON 已做静态语法解析；Editor 运行时和 Standalone 运行时加载仍未验证。
- SampleScene、InitButton、商店折叠、拖拽和 MoveTest 仍未完成 Play Mode 人工验证。

Standalone 构建当前报告的四处错误实际只有一个根因：`Assets/Game/Debug/UITest.cs` 把 `targetSprite` 声明在 `#if UNITY_EDITOR` 分支内；非 Editor 编译会移除该声明，但 `#else` 分支及条件块后的代码仍四次引用该变量，因此编译器在四个引用位置分别报告“当前上下文中不存在该名称”。

这与多人联机无关，也不是四个独立功能问题。`UITest.cs` 仍属于 Assembly-CSharp 的运行时代码，所以即使当前 Demo 不使用它，Standalone 构建也会编译它。

该问题不阻塞纯 C# Battle Core、Editor 编译或 EditMode 开发，因此：

- 不重新打开 TASK-001；
- 不把 `UITest` 修复设为 TASK-002 的前置条件；
- 把它登记为构建卫生债务，最迟在 TASK-006 的 Standalone 集成验收中做最小修复并复测；
- 如果它提前开始阻塞某个实际必需的非 Editor 验证，再单独取得授权处理，不借机重构 Debug/UI。

## 5. 为什么重新合并任务

旧计划把坐标、输入、fixture、Tick、移动、索敌、阻挡、攻击、伤害、死亡和事件拆成多个很小的任务。这会让同一模拟状态、Tick 阶段和 fixture 在多个会话中反复扩展，也容易为了让中间任务可编译而建立随后立即废弃的临时接口。

新计划按稳定契约分界：

1. **战斗基础契约**：坐标、输入、fixture、运行时状态和 Tick 骨架一次建立。
2. **完整计算契约**：移动到胜负及事件流作为一个持续迭代的整体实现。
3. **表现契约**：Unity 对象、动画和主客场视角只读消费同一结果。
4. **场景契约**：最后集中修改场景和控制器，减少序列化资源冲突。
5. **验收契约**：统一收口构建卫生、重复性、Player 数据加载和人工流程。

每个大任务内部仍应按“先建立可编译骨架→完成最小 1v1 纵向闭环→扩展已确认边界→统一回归”的顺序留下清晰检查点，但不再拆成独立 Codex 任务。

## 6. 合并后的任务总表

| 状态 | 任务编号 | 任务名称 | 所属里程碑 | 目标 | 前置任务 | 预计影响区域 | 最小验证方式 | 游戏机制确认 | 并行建议 | 完成后的可观察结果 |
|---|---|---|---|---|---|---|---|---|---|---|
| 历史基线完成；Standalone 遗留已由 TASK-006 收口 | TASK-001 | 真实运行基线与最小条件编译修复 | M0 | 取得 Editor、场景、Standalone 和 JSON 的真实状态；只修复已确认的 `ConvertCoordinate` 条件编译问题 | 无 | `UnitDeployment.cs`、只读日志和场景检查 | Editor batchmode；Standalone 构建；JSON/场景检查；最终 diff | 否 | 不并行 | `ConvertCoordinate` 与后续 `UITest` 构建卫生问题均不再阻塞 Player |
| 已完成并经真实数据/Player 回测 | TASK-002 | Battle Core 基础、不可变输入、固定 fixture 与 Tick 骨架 | M1 + M2 | 建立纯 C# Battle Core 程序集边界；完成坐标、主客场映射、输入验证、合成 fixture 加载、运行时状态、20 TPS runner 和 `maxTicks` 未解决结果 | TASK-001 的报告；不要求 Standalone 全绿 | Battle Core/Coordinates/Input/Infrastructure/Simulation；EditMode 测试；合成 fixture | TASK-006 EditMode、PlayMode、Editor/Player 十次摘要均通过 | 不得引入未确认 Buff、平局或阻挡竞争规则 | 不建议 | Player-safe 真实玩家快照与单位目录已验证 |
| 已完成并经真实数据/Player 回测 | TASK-003 | 完整确定性单场战斗计算与结构化事件 | M3 + M4 + M5 | 在同一 runner 内完成定点移动、索敌、对称阻挡、攻击排程、动画结束 Tick 出伤、整数伤害、HP、死亡、结果和公开事件流 | TASK-002 | Battle Core/Simulation/Combat/Events/Result；EditMode fixture 和断言 | TASK-006 的真实输入在 Editor/Player 各十次摘要一致 | 多单位阻挡竞争、同时全灭正式规则、Buff 等继续由受控输入规避 | 不建议 | 真实数值和真实 type ID 的固定 1v1 闭环已验证 |
| 已完成并经真实 Spine/Player 回测 | TASK-004 | Unity 战斗演示、事件回放与主客场只读投影 | M6 + M7（视角部分） | 已建立事件时间轴、世界坐标映射、双视角投影和旧单位表现桥 | TASK-003 | Battle Presentation；`UnitSkelPresentationView`；`MappedBattlePresentationViewFactory`；EditMode/PlayMode 测试 | TASK-006 PlayMode 与 Player 覆盖真实 `DefaultUnit`、Home/Away、Replay 和清理 | 缺少受击/死亡动画允许表现降级并记录；不得改变 Core | 不建议 | 自动化真实资源接入完成；观感仍需人工确认 |
| 已完成 | TASK-004A | 真实单位目录、临时本地对战输入与历史回测 | M1～M6 接入闸门 | 定义 `local-battle-v1` 玩家对战快照与 Player-safe `unit-catalog-v1`；由现有单位 JSON 转换真实 Core/表现数据 | TASK-004；TASK-001～004 实际产物和报告 | `UnitJson` 与两份真实 JSON；Battle Infrastructure；Player-safe BattleData；表现资源工厂/桥；EditMode/PlayMode 测试；相关文档 | TASK-006 在 Editor/Player 实际加载目录、快照和 `gopro`/`arcslma` | 攻击动画真实时长必须从资源核对；不能无损转整数 Tick 时停止询问；不新增机制 | 不并行 | TASK-005 使用的真实数据入口已验证 |
| 已完成 | TASK-005 | 固定对战 Demo 控制器与场景接线 | M7（Demo 部分） | 使用真实本地对战输入完成加载、计算、开始、暂停、重播、速度、视角、Tick、胜方/错误显示和一键场景入口 | TASK-004A | Demo/UI；`SampleScene.unity` 场景接线 | TASK-006 PlayMode、Player 自动验收和最终 diff 审查 | 不新增准备、商店、手动部署、完整回合或联网 | 不并行 | 真实单位数据驱动的固定对战可重复运行 |
| 已完成；人工表现与测试进程正常退出未验证 | TASK-006 | 第一阶段端到端验收与 Standalone 构建收口 | M8 | 验证“玩家对战快照+真实单位目录→计算→事件→播放→胜方→日志/结果”的闭环；最小处理已复现构建阻塞；验证 Player-safe 数据加载 | TASK-002～TASK-005，包含 TASK-004A | 测试、日志、构建入口/输出；最小集成修复 | Editor 编译；30 EditMode；5 PlayMode；Windows Standalone 构建/启动；Editor/Player 十次摘要；双视角与重播；最终 diff | 未确认机制仅记录 | 不并行 | 自动化闭环与 Standalone 证据完成；人工 UI/动画观感与 Unity Test Framework 收尾退出列为未验证 |
| 已实现；自动测试通过；人工表现与 Standalone 未验证 | TASK-007 | DefaultUnit 世界空间血量与护盾状态条 | M9 表现状态可视化 | 在 `DefaultUnit.prefab` 上增加非 Canvas 的世界空间状态条，接收敌我、血量上限、当前血量和当前护盾；接入 Spawn/Damage、观察者切换与清理；以局部材质/Transform 方案保持渲染，不改变权威战斗 | TASK-006 | `DefaultUnit.prefab`；单位表现桥/回放契约；血条贴图；必要的局部材质/Shader；精简 EditMode/PlayMode 测试；相关文档 | 公式 EditMode 3/3；真实 Prefab PlayMode 1/1；最终全量 PlayMode 11/11；Home/Away、受伤、护盾、遮挡仍需人工检查 | 只实现护盾显示，护盾生成/吸收/结算仍未定义；若必须改全局渲染管线则停止询问 | 不与其他 DefaultUnit/表现层任务并行 | 自动化验证状态条公式、Prefab、转向与清理；真实单位脚下最终观感待人工确认 |

## 7. 旧任务到新任务的合并关系

| 新任务 | 吸收的旧任务 | 合并理由 |
|---|---|---|
| TASK-001 | 旧 TASK-001 | 保留已完成的真实基线记录，不因后续发现另一个构建错误而重写历史结论 |
| TASK-002 | 旧 TASK-002～005 | 坐标、输入、fixture 和 runner 骨架共同构成稳定基础契约，分开会反复迁移字段、测试数据和阶段接口 |
| TASK-003 | 旧 TASK-006～012 | 移动、索敌、阻挡、攻击、伤害、死亡、胜负和事件共享权威状态与 Tick 顺序，合并后可按 1v1 纵向切片持续迭代 |
| TASK-004 | 旧 TASK-013～015 | 表现对象创建、事件播放和双视角投影共同决定生命周期、动画与只读边界 |
| TASK-004A | 新增真实数据接入闸门 | 当前合成 fixture 无法映射真实单位与 Spine；在修改场景前集中补齐单位目录、玩家快照格式和历史回归，避免 TASK-005 同时承担数据迁移、表现修复和场景接线 |
| TASK-005 | 旧 TASK-016 | 场景和 Demo 控制集中处理，降低序列化资源冲突 |
| TASK-006 | 旧 TASK-017，并收口 TASK-001 遗留 | 独立验收防止用局部测试替代端到端证据，同时避免 Debug 构建债务阻塞前期 Battle Core 开发 |
| TASK-007 | 新增表现增强任务 | 血条同时涉及 `DefaultUnit` Prefab、观察者敌我关系、事件回放状态和局部渲染顺序，集中实现可避免组件接线与表现契约分散迁移 |

## 8. 依赖关系

```text
TASK-001  基线审计（允许范围内已完成）
   ↓
TASK-002  Core 基础 + 输入 + fixture + Tick 骨架
   ↓
TASK-003  完整战斗计算 + 事件结果
   ↓
TASK-004  Unity 演示 + 回放 + 双视角
   ↓
TASK-004A  真实单位目录 + local-battle-v1 + TASK-001～004 回测
   ↓
TASK-005  Demo 控制器 + 场景
   ↓
TASK-006  端到端验收 + Standalone 收口
   ↓
TASK-007  DefaultUnit 世界空间血量/护盾状态条
```

主体按上述顺序执行。纯只读资源调查可以提前进行，但不得并行修改同一模拟主循环、事件契约、场景、Prefab 或其他序列化资源。同一项目路径不得同时由多个 Unity Editor 或 batchmode 进程打开。

## 9. 文档状态与下一步

- `docs/TASK-001.md` 至 `docs/TASK-006.md` 以及新增的 `docs/TASK-004A.md` 保存当前任务边界。TASK-001～004 是历史实现提示词，不因 TASK-004A 回测而改写为“当时已使用真实数据”。
- TASK-004A 与 TASK-005 已完成；TASK-006 已以 Player 日志复核它们的真实数据链、回放和清理，而不改写历史任务提示词的当时边界。
- `docs/ARCHITECTURE.md` 和 `docs/TEST_PLAN.md` 现记录真实 `gopro`/`arcslma`、`unit-catalog-v1` 和 `local-battle-v1` 的 Player 验证，以及仍需人工确认的表现项。
- `docs/SPEC.md` 的 `battle-fixture-v1` 继续作为合成回归格式；`local-battle-v1`/`unit-catalog-v1` 已作为第一阶段固定真实 Demo 的可用 Player-safe 输入。
- `docs/TASK-007.md` 是 TASK-006 后新增的表现任务；它获准最小修改 `DefaultUnit.prefab`、在 Prefab/运行时对象上挂载脚本，以及新增必要的局部材质或 Shader，但不得借机修改全局渲染管线或实现护盾战斗机制。

## 10. 每项任务的通用完成要求

每个实现任务完成时必须报告：

1. 修改文件；
2. 实现内容；
3. 实际运行的命令或 Unity 操作；
4. 测试、编译、场景或构建结果；
5. 未验证项及原因；
6. 所作假设；
7. 遗留风险；
8. 最终工作区状态；
9. 下一任务所需输入。

任何未实际执行的验证必须写为“未验证”，不得写成“通过”。0 个测试不得记为测试通过。构建失败必须记为“失败”，即使失败原因不属于当前业务范围。

## 11. TASK-004A 状态（2026-07-18）

TASK-004A 已完成真实数据接入闸门：`unit-catalog-v1` 与 `local-battle-v1` 已位于 `Assets/Resources/BattleData/`，并由 `UnitCatalogGenerator` 从现有真实单位 JSON 再生。真实 `gopro`/`arcslma` 的目录、Core 对战输入和 `DefaultUnit`/Spine 回放均已自动回测；旧合成 fixture 仍保留为算法回归。TASK-005 必须从 `LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1")` 取得 `BattleInput` 和 `UnitCatalog`，并使用 `MappedBattlePresentationViewFactory`，不得回退到合成 type ID 或手工 Inspector 映射。
