# TASK-005：固定对战 Demo 控制器与场景接线

## 角色

你是本 Unity 项目的第一阶段 Demo 集成 Agent。

你的职责是把 TASK-004A 已经完成的真实单位目录、`local-battle-v1` 玩家对战快照、纯计算、结构化结果和 Unity 事件播放器接入一个明确场景，形成玩家和开发者都能一键运行、暂停、重放、调速、切换视角并查看结果的固定单场战斗 Demo。

本任务集中处理场景和序列化接线。不得借机实现准备、商店、手动部署、回合、玩家生命或联网。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `docs/TASK-004.md`
- `docs/TASK-004A.md`
- `docs/decisions/`（若存在）
- TASK-002～004A 的完成报告、实际 `local-battle-v1`/`unit-catalog-v1` 路径、加载入口、结果/事件类型、播放器 API、真实资源工厂、清理方式和未验证项
- TASK-002～004A 实际新增的 Battle Core、Infrastructure、Presentation、真实数据适配和测试代码
- `ProjectSettings/EditorBuildSettings.asset`
- `Assets/Scenes/SampleScene.unity`
- `Assets/Game/Debug/ButtonDebug.cs`
- `Assets/Game/Debug/MoveTest.cs`
- `Assets/Game/Debug/EventTest.cs`
- `Assets/Game/Debug/UITest.cs`
- `Assets/Game/Runtime/Deployment/UnitDeployment.cs`
- `Assets/Game/UI/UIManager.cs`
- `Assets/Game/UI/VirtualSlotPanel.cs`
- `Assets/Game/UI/ShopSlotPanel.cs`
- `Assets/Game/UI/FoldButtonHandler.cs`
- `Assets/Resources/Prefabs/DefaultUnit.prefab`
- TASK-004A 已验证或明确标记未验证的 `gopro`/`arcslma` Spine/表现资源

开始前执行 `git status --short`，检查 `SampleScene.unity`、Prefab 和项目设置是否已有用户修改。若场景存在无法安全合并的未提交改动，停止并报告，不得覆盖。确认没有其他 Unity 进程打开同一路径。

## 任务背景

当前 `SampleScene` 同时是唯一 Build Settings 场景和旧原型入口，已有：

- InitButton → `ButtonDebug.Debugbutton`；
- ButtonTest → `MoveTest.Movetest`；
- FoldButton → `ButtonDebug.ShopPanelFolder`；
- 地图、`Block(x,z)`、Main Camera、UICamera、单位栏、商店和详情 UI；
- `RemainingTime`、`PlayerHp` 等尚无正式业务驱动的显示对象。

这些旧按钮不是第一阶段战斗闭环。TASK-004A 应已经能够把 `local-battle-v1` 中双方玩家单位快照与 Player-safe `unit-catalog-v1` 连接为 `BattleInput`，使用真实 `gopro`/`arcslma` 数据完成确定性计算，并通过数据驱动资源工厂在 Unity 中回放。现在需要一个独立、可诊断的 Demo 根对象和控制器把这些已验证入口串起来。

`battle-fixture-v1`、`home-striker` 和 `away-guard` 只保留作算法测试。若 TASK-004A 没有交付真实输入、真实目录和无需 Inspector 手填的资源映射，本任务前置未满足，必须停止报告，不能在场景任务中临时硬编码映射。

优先在 `SampleScene` 中新增隔离的 `BattleDemoRoot`/等价区域，避免破坏旧原型。只有实际调查证明新场景明显更安全时，才提出新 `BattleDemo` 场景及 Build Settings 变更方案并等待项目负责人确认。

## 本任务目标

1. 在明确场景中提供一个一键加载固定 `local-battle-v1`、连接真实单位目录、计算完整战斗并开始播放的入口。
2. 建立清晰的 Demo 状态：Idle、Loading/Computing、Ready、Playing、Paused、Completed、Error 或语义等价状态。
3. 提供开始/继续、暂停、重播、演示速度和 Home/Away 观察视角控制。
4. 显示或输出：battle ID、对战/目录 schema 版本、双方玩家与单位摘要、当前演示 Tick、事件进度、状态、winner/unresolved reason 和关键错误。
5. 保证战斗计算先完整结束，再把只读结果交给播放器；控制器不逐帧推进权威 Core。
6. 重播同一结果时不重新修改输入或事件；必要时提供明确的“重新计算”调试入口，但不得与 Replay 混淆。
7. 视角切换不改变源事件和 winner；若不支持播放途中无缝切换，应在 UI 中禁用或明确通过停止/重播生效。
8. 多次点击、暂停、重播、切场景和退出 Play Mode 后无残留视图、协程、订阅或静态状态。
9. 输出结构化日志，能够定位加载、验证、计算、播放和资源映射错误。
10. 为 TASK-006 提供可自动或半自动触发的 Demo 入口及结果摘要读取方式。

## 已确认规则

- 第一阶段从固定本地测试对战数据直接进入单场战斗，不经过准备、商店或手动部署。
- 输入映射、计算、事件生成必须在演示前完成。
- 主场和客场观察者播放同一份事件与结果；客场只改变坐标和方向。
- 演示暂停、速度和渲染帧率不能改变权威 Tick、事件、最终状态或 winner。
- Replay 表示重新播放同一份封存结果；它不应偷偷重新随机计算。
- 正常结束显示唯一 Home/Away winner；同时全灭、maxTicks 或不支持情况显示明确 Unresolved reason。
- 缺少动画或表现资源不能伪造成计算失败，也不能修改胜方；必须显示/记录表现降级。
- 当前阶段只要求一名本地用户驱动 Demo，不实现多人联机。

## 不属于本任务的内容

- 不修改移动、索敌、阻挡、攻击、伤害、死亡、事件或胜负算法；
- 不改变 `local-battle-v1`、`unit-catalog-v1` 或 Battle Core 公共契约，除非发现阻塞性集成缺陷并先报告；
- 不实现准备阶段、商店购买、部署费用、手动部署、回合循环、玩家扣血或四人房间；
- 不修复与 Demo 场景无关的旧 UI/Debug 行为；
- 不修复 `UITest.targetSprite`，该 Standalone 构建债务留给 TASK-006；
- 不整体重做 UI、美术、地图、Prefab 或相机；
- 不升级 Unity、Spine、Package、渲染管线或输入系统；
- 不引入第三方 UI、状态机、事件总线或 tween 框架。

## 预计影响文件或目录

候选范围：

- `Assets/Game/Battle/Demo/`
- TASK-004 Presentation 的最小公共控制接口修正（只有实际接入需要时）
- `Assets/Game/Tests/EditMode/Battle/Demo/`
- `Assets/Game/Tests/PlayMode/Battle/Demo/`
- `Assets/Scenes/SampleScene.unity`（优先方案，集中一次修改）
- 与新脚本/场景对象对应的 `.meta`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`

默认不修改现有 Prefab、Spine 资源、Package 或 ProjectSettings。若决定新建场景或修改 Build Settings，必须先停止并提交准确方案、原因、受影响 GUID/入口和回滚方式。

## 实施要求

### A. 先调查并设计最小接线

1. 从 TASK-004/004A 完成报告确认实际播放器和真实资源工厂的创建、加载、Play/Pause/Stop/Replay、速度、视角和状态查询 API。
2. 确认 `local-battle-v1` 与 `unit-catalog-v1` 在 Editor 中可由现有 Player-safe 入口读取，且计算/真实视图创建可在不进入旧 InitButton 流程的情况下运行。
3. 记录 `SampleScene` 当前对象、组件和 UnityEvent 引用；在修改前保存文本基线和 GUID 信息。
4. 设计单一 Demo Root，所有新创建的控制器、视图父节点和 UI 放在其明确层级下，便于清理和审查。

### B. Demo 控制器

5. 控制器使用显式状态转换；重复 Start、Pause、Replay 或 View 切换不能进入非法状态或抛出未处理异常。
6. 一键入口顺序必须是：
   1. 清理上一场；
   2. 加载 `local-battle-v1` 和 `unit-catalog-v1` 文本；
   3. 连接双方单位实例与真实单位定义，转换并验证不可变输入；
   4. 完整运行 Battle Core；
   5. 保存输入/事件/结果摘要；
   6. 把封存结果交给 Presentation；
   7. 开始或进入 Ready 等待播放。
7. 任何阶段失败都进入 Error，显示阶段、结构化错误和相关 ID；不能回退为默认胜方或空事件播放。
8. 控制器不得持有可继续 Step 的权威 runner 作为播放时间源。
9. Replay 使用同一事件/结果重新初始化播放器；若提供 Recalculate，名称和日志必须明确区分，且相同真实输入摘要应一致。

### C. UI 与调试输出

10. 使用项目已有 uGUI/TextMesh Pro 能力和现有美术，不引入新 UI 框架。
11. 至少提供可操作的：Start/Continue、Pause、Replay、速度、Home/Away 视角。
12. 速度控件可使用有限预设或合理范围；实际范围属于低风险 Demo 细节，选择后记录，不影响 Core。
13. 若播放中实时视角切换会破坏状态，允许把切换限制在 Idle/Ready/Paused/Completed，并通过重新投影/重播生效；UI 必须明确，不得静默失效。
14. 状态区域至少显示：battle ID、battle/catalog schema、双方玩家/单位摘要、input digest、battle status、current presentation tick、event index/total、winner/reason、last error。
15. Console 日志使用稳定前缀，避免每渲染帧刷屏。关键阶段各记录一次开始/成功/失败摘要。

### D. 场景和生命周期

16. 优先保留旧 InitButton、ButtonTest、FoldButton 及其 UnityEvent，不删除、不改名；新的 Demo 入口与旧原型隔离。
17. 使用序列化引用而不是运行时按名称反复 `GameObject.Find`；必要的地图格查找应集中缓存。
18. OnDisable/OnDestroy/场景卸载时停止播放、清理本场对象并解除订阅。
19. 退出 Play Mode 后不写回场景运行时状态。
20. 重播至少两次，确认对象数量、订阅数量和事件消费从干净状态开始。

### E. 测试和文档

21. 对控制器状态机、错误传播和 Replay 建立可自动断言的测试；不要只依赖按钮肉眼检查。
22. 建立最小 PlayMode 流程：加载明确场景/测试对象，运行固定真实对战输入到 Completed，核对真实 unit type、winner、事件消费和清理。
23. 更新 ARCHITECTURE 描述真实 Demo 入口和数据流；更新 TEST_PLAN 记录自动/人工步骤和实际结果。
24. 最后审查场景 YAML、脚本 GUID、UnityEvent、Prefab 引用和工作区 diff。

## 兼容与迁移要求

- 保留 `SampleScene` 现有 GUID 和 Build Settings 入口。
- 保留 InitButton、ButtonTest、FoldButton 及现有旧原型脚本行为。
- 不删除或迁移 `ButtonDebug`、`MoveTest`、`EventTest`、`UITest`。
- 不把新 Demo 控制器塞入 UnitFactory、UnitDeployment 或 UIManager 静态单例。
- 采用 TASK-002～004A 实际公共接口，不复制一套真实目录/对战加载器、runner、资源工厂或事件播放器。
- 新场景对象必须使用稳定序列化引用；序列化字段改名需兼容迁移。
- 不无意修改现有 Prefab/ScriptableObject/Spine GUID 或 `.meta`。

## 验证要求

### 自动验证

至少覆盖：

- 控制器合法/非法状态转换；
- 固定 `local-battle-v1` + `unit-catalog-v1` 从加载、连接到 `BattleRunResult`；
- 结果封存后再开始播放；
- 错误输入进入 Error 且有结构化信息；
- Pause 后演示时间/事件索引停止，Continue 后恢复；
- Replay 使用相同 input/event/result digest；
- Home/Away 视角 winner 和源事件摘要一致；
- 速度变化不改变事件、结果和最终视图状态；
- 重放/销毁后对象、协程和订阅无残留；
- PlayMode 完成后 Demo 状态为 Completed，winner 与 Core 一致。

测试结果 XML 必须存在、可解析、测试数大于 0、失败为 0才能写通过。

### Unity 编译或运行验证

- 执行 Editor 编译；
- 执行相关 EditMode 和 PlayMode 测试；
- 打开目标场景并进入/退出 Play Mode；
- 完整播放至少一次 Home 和一次 Away 视角；
- 重播至少两次；
- 检查 Console 和工作区 diff；
- Standalone 构建留给 TASK-006，本任务不因已知 `UITest` 错误扩大范围。

### 人工检查

1. 打开明确 Demo 场景；
2. 记录初始状态和 Console；
3. 一键加载/计算/播放；
4. 核对 battle/catalog schema、双方玩家/真实 type ID、input digest、事件总数和 winner；
5. 暂停并确认位置/事件索引停止；
6. 继续并完成；
7. 调整速度并重播；
8. 分别以 Home/Away 视角播放；
9. 确认胜方和存活状态一致；
10. 连续重播两次，检查对象和日志无重复；
11. 退出并重新进入 Play Mode，检查无残留；
12. 审查最终 Console。

### 无法执行时

明确列出无法自动点击/观察的步骤，保留为“未验证”，提供场景对象、结构化状态和日志证据以及精确人工清单。截图只作为附加表现证据。

## 验收标准

- 明确场景中存在隔离的固定战斗 Demo 入口；
- 一键完成真实单位目录与玩家对战快照加载、连接、验证、计算、事件交付和播放；
- 可操作开始/继续、暂停、重播、速度和 Home/Away 视角；
- UI/日志显示输入摘要、当前状态/Tick、事件进度、winner/reason 和错误；
- Core 在播放前完成，演示不推进或修改权威状态；
- Replay 使用同一封存结果，重复播放无残留；
- 两视角源事件和 winner 一致；
- 失败进入明确 Error，不吞异常或伪造结果；
- 自动测试数大于 0、失败为 0，Editor 无新增编译错误；
- Play Mode 至少完成一条结构化闭环，或明确记录未验证原因；
- 场景/序列化 diff 已审查，无旧 UnityEvent、Prefab、Package、ProjectSettings 或 `.meta` 意外变化；
- ARCHITECTURE/TEST_PLAN 与实际入口一致。

## 停止并询问我的条件

- TASK-004A 未交付真实输入、真实目录或无需 Inspector 手填的资源入口，或必须重写 Core/事件/播放器才能接场景；
- `SampleScene` 有无法安全合并的用户修改；
- 需要新建场景、修改 Build Settings 或大范围调整相机/地图/Prefab；
- 需要删除/替换旧按钮或破坏现有 UnityEvent；
- 控制器必须决定未确认战斗机制或修改 winner；
- 需要引入第三方 UI、状态机、tween、Package、插件、Skill 或 MCP；
- 需要升级 Unity、Spine、渲染管线或 Input System；
- 连续三次有实质差异的尝试仍无法推进。

## 完成报告格式

1. 修改文件和场景；
2. Demo Root/控制器/UI 结构；
3. 加载→计算→播放数据流；
4. 状态机、错误和日志输出；
5. 场景/UnityEvent/序列化变更；
6. 实际运行命令和 Unity 操作；
7. Editor 编译结果；
8. EditMode/PlayMode 测试数量、结果和日志；
9. Home/Away、暂停、速度、重播人工结果；
10. 未验证项；
11. 所作假设；
12. 遗留风险；
13. 最终工作区状态；
14. TASK-006 的 Demo 入口、结果摘要和自动/人工验收步骤。
