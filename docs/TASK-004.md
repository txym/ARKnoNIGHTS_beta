# TASK-004：Unity 战斗演示、事件回放与主客场只读投影

> 历史状态与后置回测：本任务已经建立只读事件回放、双视角投影和旧单位表现桥，但现有 `home-striker`/`away-guard` 合成 type ID 没有真实资源映射。新增 `docs/TASK-004A.md` 负责以真实单位目录接通 `gopro`/`arcslma`、验证 `DefaultUnit`/Spine/动画并最小修正表现桥；TASK-005 不得绕过该前置继续手填合成映射。

## 角色

你是本 Unity 项目的战斗表现适配与回放 Agent。

你的职责是把 TASK-003 已经封存的权威事件流投影为 Unity 场景对象、位置插值和 Spine 表现，并保证主场与客场观察者播放的是同一份结果。你不能通过动画、Unity 物理、渲染帧或表现对象状态修改、补算或纠正 Core 结果。

本任务建立可被后续 Demo 控制器调用的表现 API，但原则上不修改 `SampleScene`；场景按钮和最终 UI 接线属于 TASK-005。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `docs/TASK-003.md`
- `docs/decisions/`（若存在）
- TASK-003 完成报告、EditMode 结果、实际事件 DTO、`BattleRunResult`、定点尺度、Tick 阶段和 fixture 路径
- TASK-003 实际新增的 Core/Events/Result 代码
- `Assets/Game/Runtime/Initial/UnitFactory.cs`
- `Assets/Game/Runtime/Data/Unit/UnitTemplate.cs`
- `Assets/Game/Runtime/Data/Unit/UnitIdentity.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelType1.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelType2.cs`
- `Assets/Game/Runtime/Deployment/UnitDeployment.cs` 中格位和世界坐标换算相关区域
- `Assets/Resources/Prefabs/DefaultUnit.prefab`
- `Assets/Resources/Characters/` 中正式 fixture 使用的两种单位 Spine 资源
- `Assets/Scenes/SampleScene.unity` 中地图、`Block(x,z)`、相机和单位父节点的文本区域（只读调查）

开始前执行 `git status --short` 并保护既有改动。确认 TASK-003 的事件和结果契约已经通过自动测试并冻结；若事件缺少表现必需的时间、位置、unit ID 或 type ID，不要在表现层猜测，先报告契约缺口。

## 任务背景

现有 `UnitSkelBase` 和两个派生类是表现原型：

- 移动协程依赖 `Time.time` 和渲染帧，适合插值但不是权威状态；
- `ApplyHostMoveCommand` 的动画约定因 `unitskeltype` 不同而不同；
- `ApplyHostAtkCommand` 表达的是一段时间内的攻击播放，不等同于 TASK-003 的逐次 Attack/Damage 事件；
- `UnitFactory.SpawnAll` 会扫描旧源 JSON 并为每种类型生成一个对象，尚没有按 `(unitID,typeID)` 为一场战斗创建视图的公共入口；
- `UnitIdentity` 可以保存实例 ID 和类型 ID；
- `DefaultUnit` 和两种正式单位 Spine 资源可以复用，但资源和动画完整性尚未经过本阶段回放验证。

因此本任务需要建立一层明确的 Presentation Adapter，而不是把 Core 接到现有移动按钮或让 `UnitSkelBase` 反向参与计算。

## 本任务目标

1. 建立只依赖 Core 的 Unity Presentation/Adapter 边界。
2. 按 Spawn 事件的 `(unitID,typeID)` 创建或绑定唯一视图对象，并保留可查询映射。
3. 复用现有 `DefaultUnit`、Spine 数据、`UnitIdentity` 和可兼容的 `UnitSkelBase` 能力；缺少映射时输出结构化错误。
4. 把 Core 定点逻辑位置映射为 Unity 世界坐标，严格保持 `1 米 = 100 Unity 单位`。
5. 按 Tick 时间轴消费事件，使用渲染帧在相邻逻辑位置之间插值；最终位置必须由事件确定。
6. 播放移动、攻击、受击和死亡表现；表现缺失时允许可诊断降级，但不能改变 Damage、Death 或 winner。
7. 对 `attackAnimationDuration > attackInterval` 的 Attack 事件使用事件给出的原始/有效时长调节播放速度。
8. 实现主场直接投影和客场观察者 180 度只读投影；转换所有单位位置和方向，但不改变源事件、最终状态或胜方。
9. 提供后续控制器所需的加载、播放、暂停、继续、停止/清理、重放、速度和视角切换接口；本任务不制作最终按钮 UI。
10. 验证事件消费数、对象最终状态和 `BattleRunResult` 一致，并验证场景重载/重复播放不会留下协程、订阅或对象。

## 已确认规则

- 权威战斗已经在播放前计算完成；演示只读消费 `BattleRunResult` 和事件序列。
- Tick 固定为 20 TPS；正常播放时一个 Tick 对应 0.05 秒，播放速度只缩放演示时间轴。
- `1 格 = 1 米 = 100 Unity 世界坐标单位`。
- 主场观察者直接播放 Core 的主场坐标。
- 客场观察者对整份演示坐标和方向应用 180 度变换 `(x,y) → (10-x,9-y)`；不是只旋转客场阵营单位。
- 两个视角共享相同事件、最终状态、死亡单位和 winner。
- Move 表示权威逻辑位置变化；渲染插值不能写回 Core。
- Attack 在攻击开始 Tick 播放；Damage 在出伤 Tick 播放受击表现；Death 在死亡 Tick 播放死亡表现。
- 攻击在出伤前被取消时仍有 Attack 事件、没有 Damage 事件，因此攻击动画仍播放，但目标不播放该次受击。
- `attackAnimationDuration > attackInterval` 时，有效动画不长于攻击间隔；表现按事件提供的比例加速。
- 死亡动画可能不存在；缺失时允许使用停止动画、隐藏或销毁等最小降级，并记录实际策略。

## 不属于本任务的内容

- 不重新运行或修改战斗计算规则；
- 不基于 GameObject 距离、碰撞或动画回调生成 Target、Block、Damage、Death 或 winner；
- 不修正 Core 事件、不删除“看起来不合理”的事件、不在表现层重排权威 sequence；
- 不实现准备、商店、部署、回合、玩家生命或多人同步；
- 不制作最终 Demo 按钮、状态面板或场景启动流程；
- 原则上不修改 `SampleScene.unity`、Build Settings 或现有 Prefab；
- 不整体重写 UnitFactory/UnitSkel，不删除旧调试 API；
- 不升级 Spine、Unity、Package、渲染管线或 Input System；
- 不引入第三方 tween、动画、DI 或事件框架。

## 预计影响文件或目录

候选范围：

- `Assets/Game/Battle/Presentation/`
- `Assets/Game/Battle/Presentation/Views/`
- `Assets/Game/Battle/Presentation/Playback/`
- `Assets/Game/Battle/Presentation/Projection/`
- `Assets/Game/Tests/EditMode/Battle/Presentation/`
- `Assets/Game/Tests/PlayMode/Battle/Presentation/`（若建立最小 PlayMode 测试）
- `Assets/Game/Runtime/Initial/UnitFactory.cs` 的最小兼容扩展（仅在无法通过新增适配器复用资源时）
- `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs` 和派生类的向后兼容表现扩展（仅在现有公开 API 无法表达单次事件时）
- 与本任务事实直接相关的 `docs/ARCHITECTURE.md`、`docs/TEST_PLAN.md`

若需要修改现有 Prefab、Spine 资产、场景或 ScriptableObject，先说明原因、引用影响和替代方案，停止等待确认。新测试对象应优先由代码创建，避免为测试写入高冲突序列化资源。

## 实施要求

### A. 事件与视图边界

1. Presentation 程序集可以引用 Battle Core 和 UnityEngine，但 Core 不能反向引用 Presentation。
2. 以 TASK-003 的实际事件接口为唯一输入，不复制一份可漂移的战斗状态机。
3. 为每个 unit ID 维护唯一视图记录；重复 Spawn、未知 unit、未知 type ID 或资源缺失必须明确失败/记录，不能静默生成错误对象。
4. 视图记录至少区分逻辑身份、GameObject、动画适配器、当前演示位置和生命周期状态。
5. 不把 `UnitIdentity.unitID` 的旧负数原型规则带入战斗；使用事件中的独立 unit ID。

### B. 资源与现有代码兼容

6. 先调查正式 fixture 使用的 type ID 是否能通过现有两个 JSON 和 Resources 解析到 `DefaultUnit`/Spine 数据。
7. 优先新增一个按单场 Spawn 请求创建对象的适配入口，而不是调用 `SpawnAll` 后保留多余对象。
8. 若扩展 UnitFactory：保留 `SpawnAll`、静态缓存和现有按钮行为；新增 API 不能改变旧调用结果。
9. 若现有 `ApplyHostAtkCommand` 语义不符合逐次 Attack，允许通过 `UnitSkelBase.PlayAnimation` 或向后兼容的新表现方法播放单次 Attack；不得为了复用旧 API而扭曲事件时间。
10. 不要对每个 Tick 的 Move 事件盲目重启旧移动协程。可以聚合连续 Move 段或由统一 event player 插值 Transform，同时只在移动状态变化时切换动画。
11. 动画名、轨道和是否存在必须从实际 Spine 数据调查；没有受击/死亡动画时采用文档化降级，不吞掉事件。

### C. 坐标与主客场投影

12. 建立纯投影函数：输入 Core 逻辑位置和 observer view，输出演示逻辑位置/方向；源事件保持逐字段不变。
13. 客场投影绕战场中心旋转 180 度。连续定点位置也必须按相同中心变换，不能只转换整数格。
14. 世界坐标映射以 `1m=100` 为比例；场地原点、轴向和高度通过可配置适配器或现有 `Block(x,z)` 标记核对，不能让 GameObject 名称成为 Core 事实。
15. 单位朝向来自事件移动方向或攻击目标方向；客场视角再旋转 180 度。

### D. 回放时间轴

16. event player 以事件 Tick/sequence 顺序读取，演示时间可使用 Unity 时间和协程，但权威结果已封存。
17. 暂停必须停止演示时间推进；继续后从同一事件位置恢复。
18. 播放速度改变只影响演示耗时、插值和动画倍速，不改变当前事件索引、Core Tick、事件摘要或 winner。
19. Attack 动画播放倍率依据事件的原始和有效动画时长计算；不得读取当前 Spine 播放完成回调来决定 Damage 时机。现有 `maxAnimTimeScale` 等表现保护值不能导致攻击动画超过事件规定的有效时长；若资源或接口无法达到所需倍率，必须显式报告，不能静默截断。
20. Damage 事件触发受击表现；Death 事件触发死亡表现和最终不可交互/隐藏状态。
21. 回放结束时，所有视图的位置、存活/死亡状态必须与 `BattleRunResult` 对应。

### E. 生命周期与可测试性

22. 将对象创建、坐标投影、时间源/调度和动画命令分成可替换的小接口，允许 EditMode 使用假视图核对事件，不引入通用框架。
23. Stop/Reset/Dispose 或等价清理必须停止协程、解除事件、清空字典并销毁本场创建对象。
24. 重放同一结果前先完整清理或可靠复位；不能叠加 Spawn、订阅和协程。
25. 场景卸载/组件销毁时自动清理。
26. 更新 ARCHITECTURE/TEST_PLAN，只描述实际实现和真实验证。

## 兼容与迁移要求

- `UnitFactory.SpawnAll` 和 InitButton 继续可用。
- `UnitTemplate`、`UnitIdentity`、`UnitSkelBase.ApplyHostMoveCommand`、`ApplyHostAtkCommand` 的既有签名不删除、不改名。
- 若新增动画适配方法，旧派生类型继续编译并保持原型行为。
- `DefaultUnit.prefab`、Spine 资源 GUID 和 SampleScene 引用不变，除非另行获批。
- Presentation 只保存事件/结果的只读引用，不持有可写 Core runner。
- 不让 Unity 世界坐标或 Transform 进入 Core DTO。
- 不移动、删除或重新生成现有 `.meta`。

## 验证要求

### 自动验证

至少覆盖：

- Home 投影保持位置；Away observer 对整数格和连续定点位置做 180 度转换；双转换恢复；
- 投影前后源事件、事件摘要和 winner 不变；
- Spawn 建立唯一 `unitID→view` 映射，重复/未知输入明确报错；
- 事件按 `(tick,sequence)` 消费且消费数与输入一致；
- Move 插值关键时间点和结束位置正确；
- Attack、Damage、Death 触发对应视图命令；取消攻击只有 Attack、没有受击；
- 动画时长压缩比例正确；播放速度变化不改变事件/结果；
- 播放结束视图状态与 `BattleRunResult` 一致；
- Stop/Reset/Replay 后没有重复对象、协程或订阅；
- 使用假视图/假时间源的测试不需要真实 Spine 才能断言内部状态。

如建立 PlayMode 测试，结果 XML 必须可解析且测试数大于 0、失败为 0。不要用截图替代状态断言。

### Unity 编译或运行验证

- 执行 Editor 编译；
- 执行相关 EditMode 测试；
- 对程序化最小回放执行 PlayMode 测试或运行检查；
- 在可操作 Editor 时，用实际 `gopro`/`arcslma` 资源播放最小事件流，记录缺失动画和资源错误；
- 本任务不要求 Windows Standalone 全绿，已知 `UITest` 问题留给 TASK-006。

### 人工检查

- 观察移动是否连续且最终位置准确；
- 检查攻击开始、受击和死亡发生在对应事件时点；
- 检查动画加速没有改变 Damage 时点；
- 分别用 Home/Away 视角核对坐标和朝向；
- 重放并检查没有残留对象或双重动画；
- 检查 Console 没有与本任务相关的未处理异常。

### 无法执行时

将真实 Spine、视觉流畅度、GUI 操作或 PlayMode 项标记为“未验证”，同时提供假视图/结构化状态证据和精确人工清单。不得把仅编译或截图写成完整播放通过。

## 验收标准

- Presentation 单向引用 Core，Core 不引用 Unity 表现；
- 一份 TASK-003 结果可以创建唯一单位视图并完整消费事件；
- 世界比例严格为 1 米对应 100 Unity 单位；
- Home/Away 两视角使用同一源事件和 winner，Away 只转换坐标和方向；
- Move 插值不写回 Core，播放结束位置与最终状态一致；
- Attack、Damage、Death 语义分离，取消攻击不会错误播放受击；
- 动画时长大于间隔时按事件比例加速；
- 缺少动画时有明确降级和日志，不修改计算结果；
- 暂停、继续、停止、速度和重放 API 可由 TASK-005 调用；
- 重放/销毁无残留对象、协程或订阅；
- 相关自动测试数大于 0、失败为 0，Editor 无新增编译错误；
- 原则上没有场景、Prefab、Package 或现有资源 GUID 变化；
- ARCHITECTURE/TEST_PLAN 与交付一致。

## 停止并询问我的条件

- TASK-003 事件缺少无法从只读结果获得的必要位置、时间、身份或播放载荷；
- 为表现需要修改权威事件语义、伤害时机或 winner；
- 正式 fixture 的 type ID 无法映射到任何现有 Prefab/Spine 资源；
- 必须修改现有 Prefab、Spine 资产、SampleScene 或 Build Settings；
- 必须删除/改名现有 UnitFactory/UnitSkel 公共接口；
- 需要引入第三方 tween、动画、DI、Package、插件、Skill 或 MCP；
- 缺失动画的降级会明显改变玩家可见死亡/攻击语义，无法用简单隐藏/停止表达；
- 发现用户修改与表现适配范围重叠且无法安全保留；
- 连续三次有实质差异的尝试仍无法推进。

## 完成报告格式

1. 修改文件；
2. Presentation 程序集和引用；
3. 视图创建/资源映射方式；
4. 世界坐标和主客场投影；
5. 事件播放、动画和清理实现；
6. 对现有 UnitFactory/UnitSkel 的兼容修改；
7. 实际运行命令/Unity 操作；
8. Editor 编译结果；
9. EditMode/PlayMode 测试数量、结果和日志；
10. 实际 Spine 人工检查；
11. 未验证项；
12. 表现降级和所作假设；
13. 遗留风险；
14. 最终工作区状态；
15. TASK-005 所需的播放器 API、序列化引用和场景接线说明。
