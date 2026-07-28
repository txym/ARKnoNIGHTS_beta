# 当前项目架构

## 1. 文档范围与状态

本文档记录仓库在静态审计时真实存在的结构、入口、数据流和模块边界。它不把 `docs/SPEC.md` 中尚未实现的目标设计描述为现有能力。

TASK-006 已以 Unity `2022.3.62f1c1` 完成第一阶段复核：Editor 编译、EditMode（30/30）、PlayMode（5/5）、Windows x86_64 构建和实际 Player 验收均成功。早期 TASK-001 的 Standalone 失败是保留的历史基线；`UITest.targetSprite` 的四个同根因诊断已被最小作用域修复消除。固定 Demo 的 Player-safe 目录/快照加载、真实单位视图、十次确定性、Home/Away 与 Replay 已由 Player 日志验证；人工 UI/动画观感仍未验证。

## 2. 项目基线

- Unity Editor 版本：`2022.3.62f1c1`。
- 当前使用 Built-in Render Pipeline，没有配置自定义 Scriptable Render Pipeline。
- Project Settings 当前选择旧 Input Manager；第一方交互代码使用 `UnityEngine.Input`。`UIManager` 保留了新 Input System 条件分支，但项目没有直接安装 Input System Package，当前不会走该分支。
- Build Settings 当前只启用 `Assets/Scenes/SampleScene.unity`。
- `SampleScene` 同时承担启动场景和当前战斗交互原型场景，没有独立的启动、准备或结算场景。
- 第一方运行时代码没有自有 `.asmdef`，统一进入 Unity 预定义程序集 `Assembly-CSharp`。
- 仓库中仅 Spine Runtime 和 Spine Editor 使用独立程序集定义。
- Spine Unity 以源码和资源形式放在 `Assets/Spine`，不是 UPM 依赖；其仓库内版本信息为 Spine Unity 3.8 系列。界面使用 uGUI、TextMesh Pro 和 Unity EventSystem。

## 3. 目录与模块边界

| 区域 | 当前职责 | 当前接入状态 |
|---|---|---|
| `Assets/Game/Runtime/Initial` | 从单位 JSON 构造运行时单位模板和单位对象 | 由场景调试按钮显式触发 |
| `Assets/Game/Runtime/Data/Unit` | 单位模板、实例身份、Spine 移动和攻击表现接口 | 已被单位工厂和场景原型使用 |
| `Assets/Game/Runtime/Data/Player` | 玩家单位集合、区域分桶和 `9×4` 位置索引 | 当前没有调用方，未接入场景流程 |
| `Assets/Game/Runtime/Deployment` | 从单位 UI 拖拽生成 3D 单位并吸附到地图格 | 已挂入 `SampleScene`，属于原型实现 |
| `Assets/Game/Runtime/Bitset` | 标签位集、标签注册表和单位固有能力查询 | 有数据资产和烘焙工具，未形成战斗流程 |
| `Assets/Game/UI` | 单位栏、商店面板、详情面板和点击数据路由 | 已用于 `SampleScene` 原型 |
| `Assets/Game/Debug` | 初始化、移动和 UI 调试入口 | 已由场景按钮引用；不是自动化测试 |
| `Assets/Game/Editor` | 纹理合并和单位固有能力数据烘焙 | 仅编辑器工具 |
| `Assets/GameData/Units` | 单位 JSON、JSON 数据结构和固有能力数据库 | 单位工厂直接读取其中的 JSON |
| `Assets/Resources` | 默认单位 Prefab、单位 Spine 资源和 UI Prefab | 通过 `Resources.Load` 在运行时加载 |

## 4. 场景与运行入口

### 4.1 自动入口

- `UnitFactory.ResetStatics` 使用 `SubsystemRegistration` 初始化单位类型缓存。它只重置缓存，不加载单位。
- `UIManager.InitOnLoad` 使用 `AfterSceneLoad` 安装全局点击监听。启用旧输入系统时，它会创建常驻的 `__UIManagerRunner`；启用新输入系统时则订阅 Input System 事件。
- `VirtualSlotPanel.Start`、`OnEnable` 和 `Update` 负责计算单位栏虚拟槽位与响应尺寸变化。
- `ShopSlotPanel.Start` 初始化商店槽位。
- `UnitDeployment.Update` 每帧处理单位 UI 命中、拖拽、取消和落格。

### 4.2 场景调试入口

`SampleScene` 中存在三条主要按钮链路：

1. 初始化按钮调用 `ButtonDebug.Debugbutton`；
2. 移动按钮调用 `MoveTest.Movetest`；
3. 折叠按钮调用 `ButtonDebug.ShopPanelFolder`。

场景中还存在地图模型、格位标记、单位栏、商店面板、详情面板相关对象，以及 `RemainingTime`、`PlayerHp` 等界面对象；没有发现驱动回合计时或玩家生命结算的对应业务逻辑。

## 5. 当前实际运行流程

### 5.1 单位初始化

旧单位初始化不是正式 Player 数据链，而是由初始化调试按钮触发的 legacy/debug 流程：

1. `ButtonDebug.Debugbutton` 调用 `UnitFactory.SpawnAll`；
2. `UnitFactory` 通过共享 `UnitEliteVariantResolver` 读取 `Application.dataPath/GameData/Units/EliteVariants/Json` 下的 v2 文档，当前只包含 `1000`、`5503`、`5504`；
3. 工厂在创建首个 `UnitTemplate` 或 GameObject 前完成全部 v2 源文档加载、精英 0 解析和必需 idle/move/attack 语义动画绑定校验；物理 `SkeletonDataAsset` 仍在对象创建后的第 7 步加载；
4. 工厂把已解析的 v2 事实显式适配到内存 `UnitTemplate`，并按 `typeID` 写入静态字典；
5. 工厂加载 `Resources/Prefabs/DefaultUnit` 并为每种类型实例化一个对象；
6. 工厂动态添加 `UnitIdentity` 和兼容用 `UnitSkelType2`；
7. 工厂按 JSON 字段加载 Spine `SkeletonDataAsset`；
8. `ButtonDebug` 把单位类型填入 `VirtualSlotPanel`，建立可点击、可拖拽的单位 UI。

工厂为每种类型生成的单位对象分配负数 `unitID`。这套编号只服务旧原型，尚未接入 SPEC 所描述的对战输入快照或玩家单位持久数据；正式 Player 运行时不读取该源目录。源直读适配器在正式运行数据不再需要后销毁。

### 5.2 单位详情 UI

单位 UI 通过公开的 `Payload` 暴露数据。`UIManager` 在 3D 或 2D 命中对象及其父级组件上用反射查找 `Payload`，然后调用 `DataUISwitch` 显示详情。

`DataUISwitch` 是非 `MonoBehaviour` 单例，按需加载并实例化单位详情 Prefab，通过订阅 `UIManager.OnApplyData` 更新绑定字段。该机制依赖静态事件和运行时实例，场景重载后的订阅与清理行为尚无测试覆盖。

### 5.3 拖拽部署原型

`UnitDeployment` 当前实现的是 UI 到 3D 场景的直接拖拽：

1. 每帧通过 EventSystem 射线检测带 `UnitUI` 标签的界面对象；
2. 鼠标按下后，从 `ButtonDebug` 保存的单位模板对象克隆一个 3D 单位；
3. 拖拽期间把鼠标射线投影到固定的 `y=50` 平面；
4. 鼠标释放时把世界位置换算为一基格坐标；
5. 坐标有效时查找名为 `Block(x,z)` 的场景对象并吸附；无效时销毁本次克隆。

当前有效范围是 `x=1..9`、`z=1..8`，并排除 `(5,1)` 和 `(5,8)`。这与当前 SPEC 的完整战场范围一致，但没有限制为本地 `9×4` 部署半场。

该流程没有连接 `PlayerUnitCollection`，也没有检查阶段、格位占用、单位池数量、堆叠、替换、部署费用或操作原子性。多个单位落在同一格时没有业务层冲突处理。

### 5.4 移动与攻击表现

`UnitSkelBase` 及两个派生类型提供 Spine 动画、移动指令和攻击动画接口。

- 移动通过协程按 `Time.time` 和渲染帧在起终点间插值；
- 攻击接口只负责播放或调节攻击动画；
- `MoveTest` 对最近一次拖出的单位发送固定起点、终点和持续时间的调试移动指令。

这些类目前是表现层原型，不包含权威战斗计算。它们没有实现逻辑帧、寻路、索敌、阻挡、伤害、当前生命值、死亡或胜负结算。

## 6. 数据结构现状

### 6.1 单位静态数据与运行时身份

- `UnitJson` 定义 JSON 输入字段。
- `UnitTemplate` 是运行时创建的 `ScriptableObject`，保存类型级属性；它不是仓库内逐单位持久化的资产。
- `UnitIdentity` 保存实例 `unitID` 和 `UnitTypeID`。
- `UnitFactory` 以 `typeID` 为键缓存 `UnitTemplate`。

旧原型仍没有可供其自身使用的统一对战输入结构；TASK-002 新增的 Battle Core 已独立提供不可变的双方快照，包含单位位置、Buff 占位、阵营和商店记录，但尚未接入旧原型。

### 6.2 `PlayerUnitCollection`

`PlayerUnitCollection` 是纯 C#、固定容量为 48 的结构，包含：

- `Staging`、`Deployed`、`Overflow`、`Shop` 四个区域；
- 按区域维护的稠密索引和显示顺序；
- `9×4` 的格位到单位双向索引；
- 整表加载、区域排序和严格部署位置写入等操作。

该类型当前没有调用方。其位置索引为零基内部格编号，区域定义和固定容量也尚未与当前 SPEC 的一基坐标、待部署堆叠及槽位规则对齐。因此它只能视为孤立的数据结构，不能视为现行玩家单位池实现。

### 6.3 `LocalPlayerState`（UI-001）

`ARKnoNIGHTS.PlayerState` 是独立于场景、`MonoBehaviour`、Transform 和 Unity 物理的本地玩家状态程序集。它以 Player-safe `UnitCatalog` 作为唯一运行时类型目录，维护玩家 ID、可用部署费用、独立单位 ID、type ID、区域、当前精英化、不可变 Buff 快照和可选的一基本地阵型坐标；对 UI 只公开只读快照、版本号和成功变更通知。它内部持有并按每次成功变更重建 `PlayerUnitCollection` 的固定容量/区域桶派生投影，但字符串实例记录仍是唯一权威状态，旧集合和其零基坐标 API 不向 UI 暴露。

`LocalPlayerStateLoader` 从 `Resources/PlayerData/local-player-state-v1.json` 读取版本化固定测试状态，并拒绝未知 schema、重复 unit ID、未知类型、非法精英化/区域/坐标、负费用、重复占位、超过 48 个单位和超过 13 个严格待部署槽。固定数据使用真实 `gopro`/`arcslma`，初始 Cost 为 99，包含两个可堆叠 `gopro`、一个 `arcslma` 和一个 Overflow 单位。

`PlayerState.TryDeploy`、`TryRelocateDeployed`、`TryRetreat` 和 `RemoveOverflowUnits` 是原子操作：部署只接受一基 `9×4` 空格并拒绝 `(5,1)`；已部署单位重定位对空格只移动该单位、对己方已部署占格在一次通知中交换两个 Formation、对原格返回不通知的成功 no-op；成功部署时扣除目录部署费用，撤退成功时返还全部费用，重定位不改变费用，Overflow 清理只删除 Overflow。待部署快照按 Cost、type ID 排序，并且数量为 1 时仍保留槽位数量。Buff 仍无游戏语义；当前只比较完整快照而不规范化它。

旧 `PlayerUnitCollection` 和 `UnitFactory.SpawnAll` 继续保留为未接入的 legacy/debug 原型，未改变公开零基接口语义；它们不是 UI-001 的权威状态来源。

### 6.4 标签与固有能力

`BitSet64`、`TagMask`、`TagRegistry` 和 `UnitInnateAbilityDatabase` 提供最多 64 位的标签/固有能力表示。`UnitJsonAbilityBakeTool` 可在编辑器内扫描单位 JSON 并更新数据库资产。

当前单位模板可以查询固有能力掩码，但没有战斗系统消费这些数据，也没有看到 Buff 实例、持续时间或叠加规则的运行时模型。

## 7. 当前不存在的架构能力

以下能力由 SPEC 描述或验收需要，但当前代码中没有形成可执行系统：

- 回合、准备、锁定、战斗和结算状态机；
- Unity 表现层对主客场计算结果的观察视角转换；
- 移动路径、最近敌人索敌、阻挡关系和阻挡数管理；
- 物理、法术、真实伤害计算；
- 当前生命值、死亡、胜负和单场结算；
- 真实单位 JSON/Player-safe 目录到实际 Unity/Spine 资源的类型映射（规划由 TASK-004A 补齐），以及后续由 TASK-005 接入场景的控制器；
- 从 fixture 到 Unity 表现/场景初始化双方数据的流程；
- 多玩家房间、同步、断线或重连。

## 8. 已知边界与风险

1. `UnitDeployment.Update` 在所有构建中调用 `ConvertCoordinate`。TASK-001 已将不依赖 `UnityEditor` 的方法移出 `#if UNITY_EDITOR`，保留 `OnDrawGizmosSelected` 为 Editor 专用；复核后的 Windows Standalone 编译不再报告该符号缺失。
2. `UITest.targetSprite` 的条件编译作用域已在 TASK-006 修复：变量在条件块外声明，Editor `AssetDatabase` 与 Player `Resources.Load` 分支仅各自赋值；Windows Standalone 构建已复测成功。
3. `UnitFactory` 仍从 `Application.dataPath` 下的 v2 源目录读取数据，是待销毁的旧原型风险。第一阶段新 Demo 不调用该链路，而是从 `Resources` 加载冻结的 `unit-catalog-v1` 与 `local-battle-v1`；该正式链路已在实际 Player 中验证。
4. 旧第一方业务代码仍位于 `Assembly-CSharp`，但 Battle Core、fixture 适配层和 EditMode 测试已具备独立程序集隔离；新 Core 不反向依赖旧程序集。
5. `Runtime/Deployment` 直接依赖 `Assets/Game/Debug` 中的 `ButtonDebug`、`MoveTest` 和 `EventTest`。因此 Debug 目录当前是实际运行链的一部分，不能作为可独立移除的开发辅助层。
6. 当前没有统一的 Bootstrap 或 Game Manager。初始化分散在运行时静态钩子、场景组件生命周期和调试按钮中。
7. 多处运行流程依赖静态单例和场景对象名称，包括 `ButtonDebug.Instance`、`MoveTest.Instance`、`DataUISwitch.Instance` 和 `Block(x,z)` 查找；格位对象查找失败时还缺少完整的空值处理。
8. 部署逻辑直接实例化表现对象并以对象位置作为结果，没有独立的权威部署状态。
9. 移动协程使用渲染时间，适合表现插值，不满足确定性战斗计算的状态来源要求。
10. 已有 TASK-002 的 10 项 Battle Core EditMode 自动化测试；场景、静态生命周期、Player fixture 加载和 Standalone 构建风险仍缺少回归保护。
11. 项目使用 Unity 2022.3，而仓库内 Spine Unity 资源自述为 3.8 系列；静态审计不能判断兼容性，需要通过真实导入、编译和场景运行验证。

## 9. 架构事实来源

本文件主要依据以下内容维护：

- `ProjectSettings/ProjectVersion.txt`
- `ProjectSettings/EditorBuildSettings.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `Assets/Game` 下的第一方 C# 脚本
- `Assets/GameData` 下的单位数据和数据结构
- `Assets/Scenes/SampleScene.unity`
- 第一方 Prefab、Resources、`.asmdef` 和测试文件清单

行为目标和验收口径仍以 `docs/SPEC.md` 为准；当实现与 SPEC 不一致时，本文件记录实现现状，不替代机制确认。

## 10. TASK-002 Battle Core 基础层（2026-07-17）

已新增三个隔离区域：

- `Assets/Game/Battle/Core`：`ARKnoNIGHTS.Battle.Core` 为 `noEngineReferences` 的纯 C# 程序集，定义一基坐标、主客场映射、厘米定点逻辑位置、不可变输入快照、验证、运行时初始单位状态及显式 20 TPS runner。
- `Assets/Game/Battle/Infrastructure`：唯一允许使用 `UnityEngine` 的 fixture 适配层。它以 `JsonUtility` 和 `Resources.Load<TextAsset>` 读取 `battle-fixture-v1`，转换为 Core 的不可变值，并将验证失败返回为结构化错误。
- `Assets/Game/Tests/EditMode/Battle`：最小 EditMode 测试程序集，引用 Core 和 Infrastructure；`Assets/Resources/BattleFixtures/task002-minimal-v1.json` 是 Player-safe 的合成测试 fixture，不复用 `Application.dataPath` 的旧单位 JSON 路径。

Core 不引用 `Assembly-CSharp`、Spine、UI、物理、场景、文件路径或 Unity 时间。只有 Deployed 快照生成 `RuntimeUnitState`；Staging 与 Shop 仍保留在输入中。`BattleRunner.Step()` 是唯一推进入口；TASK-003 已在既有 runner 中补齐移动、索敌、阻挡、攻击、伤害、死亡、胜方和公开事件，`maxTicks` 仍以无胜方的 `MaxTicksReached` 未解决诊断停止。

## 11. TASK-003 确定性单场战斗 Core（2026-07-18）

`BattleRunner` 现以稳定的九阶段顺序推进：清理失效待结算攻击、索敌、基于快照的移动意图并批量应用、阻挡评估、攻击开始、同 Tick 批量伤害、死亡与关系清理、终局判定和事件封存。运行时单位仅在 Core 内维护当前 HP、存活、目标、容量感知的对称阻挡列表、攻击冷却和定点移动余量；阻挡评估以入站候选的“当前目标、嘲讽、到本方门格距离、unit ID”顺序建立关系，并以目标 unit ID 解决跨目标容量争用。所有单位遍历、索敌决胜、事件和摘要均使用显式稳定排序。

`Assets/Game/Battle/Core/Events/BattleEvents.cs` 定义不含 Unity 或运行时可变对象引用的只读事件 DTO。Spawn 在 Tick 0 输出；其余事件按阶段输出并以 Tick 内 sequence 严格递增。`BattleRunResult` 以不可变快照公开事件、trace、胜方和停止原因。胜利、同时全灭和 maxTicks 均产生明确结果，不会伪造胜方；多单位阻挡容量竞争由确定性优先级消解，不再作为不支持内容停止。

移动使用厘米整数位置和余量累积；距离比较、阻挡半径和索敌只使用整数。攻击的原始和有效动画 Tick、计划出伤 Tick 均进入 Attack 事件；到达同一 Tick 的攻击先按阶段开始存活状态过滤、批量应用伤害、再统一死亡。`DamageCalculator` 支持 Physical、Magic（法术语义）和 True 三种整数伤害。

`Assets/Resources/BattleFixtures/task003-minimal-v1.json` 是不触发 Buff 或非整数 Tick 的 1v1 完整闭环 fixture。多单位阻挡竞争使用 Core EditMode 构造输入回归：移动中拦截、容量、目标优先、嘲讽、到门距离和 ID 决胜均不依赖 Unity 物理。它不改变 `battle-fixture-v1` schema；原 TASK-002 fixture 仍保留用于 maxTicks 和输入兼容回归。现有 Unity 表现、场景、Prefab、旧部署/UI 与 UnitFactory 均未接入或修改。

## 12. TASK-004 表现回放边界（EditMode 已验证）

- `ARKnoNIGHTS.Battle.Presentation` 只引用 Battle Core 与 UnityEngine。`BattleEventPlaybackController` 仅保留已完成 `BattleRunResult` 的只读引用，按输入的 `(tick, sequence)` 顺序消费事件；`BattleRunResult.FinalUnits` 公开最终的位置、HP、阵营、生死和类型快照，播放结束时逐单位核对。暂停、调速、视角和重播只影响演示时间轴，不会创建或推进 Core runner。
- 为支持未来 Buff 临时生成单位，`Spawn` 事件现公开不可变的 `unitId`、`unitTypeId`、`unitSide` 和初始定点位置。表现层以 `unitId → ViewRecord` 建立唯一映射；重复 Spawn、未知单位、缺少 Spawn 字段或工厂无映射均产生结构化诊断，不会猜测资源。
- `BattlefieldWorldProjection` 使用主场直投影与客场 180 度变换；连续定点位置按同一中心变换，`1` 个 Core 厘米单位对应 `1` 个 Unity 世界坐标单位，即 `1m = 100` Unity 单位。
- 回放在相邻 Move 事件的一个 Tick 区间内插值 Transform，并在事件 Tick 对齐权威最终位置。Attack、Damage、Death 分别驱动攻击、受击、死亡命令；攻击局部倍速使用 `originalAnimationTicks / effectiveAnimationTicks`。`UnitSkelPresentationView` 缺少动画时记录诊断，死亡动画缺失时采用隐藏这个已由事件确认死亡的对象的降级策略。
- Track 点采样的 `ShouldDisplay` 表示从该 Tick 切入时的视图创建资格：死亡单位仍保留权威终态、HP、位置和 `Death` 动作采样，但初次绑定、重新绑定或回退重建不会为其创建视图。连续播放中已经存在的视图不受创建过滤影响，仍会在跨过 Death 时接收一次死亡命令。
- 真实 Spine 视图保留本次死亡 `TrackEntry` 并监听完成回调；动画完成后以 `0.5` 秒现实时间把 Skeleton RGB 线性变为纯黑，再停用整个单位 GameObject。状态条不参与着色但随对象停用；Replay、重新绑定和清理仍由播放控制器统一 Dispose，Dispose 会先立即隐藏再在帧末销毁，避免旧视图与新视图短暂重叠。视图在死亡动画或变黑阶段通过 `IBattlePresentationView.HasPendingTerminalPresentation` 报告尚未完成，默认接口实现为 `false`，不会破坏不需要异步收尾的既有视图；若外部生命周期直接停用或销毁视图，`OnDisable`/`OnDestroy` 会退订 TrackEntry 并释放 pending，避免正式回合永久等待一个已无法更新的对象。
- 同一单位的连续 Move 事件只启动一次循环移动动画，不会每 Tick 重置 Spine Track。Attack 从其事件 Tick 持续到 `effectiveAnimationTicks` 结束，期间仍可按 Core 结果更新 Transform，但后续 Move 事件不得覆盖攻击动画；暂停将视图播放倍率置为 `0`，恢复时才还原当前演示倍率。上述演示策略不改变事件、坐标、阻挡、伤害或 winner。
- `UnitSkelPresentationView` 与 `MappedBattlePresentationViewFactory` 位于 `Assembly-CSharp`，作为旧 `DefaultUnit`、`UnitIdentity`、`UnitSkelBase`/Spine 原型对隔离 Presentation 程序集的单向桥接。后者只接受 Inspector 明确配置的 `coreTypeId → prefab / SkeletonDataAsset / legacy type ID` 绑定；未配置时返回 `resource.mapping.missing`，不会猜测绑定。没有反向把 Unity 对象、Transform 或动画状态带入 Core。
- `task003-minimal-v1` 的 `home-striker`、`away-guard` 是合成算法测试类型，仍没有也不应被猜测为 `gopro`/`arcslma` 资源映射。因此实际 Spine/Prefab 创建与视觉播放保持未验证；规划中的 TASK-004A 必须先建立真实单位目录与玩家对战快照的连接并完成真实资源回归，TASK-005 才能接场景。

## 13. TASK-004A 真实目录与本地对战适配（2026-07-18）

`Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs` 新增 Player-safe 数据边界：`UnitCatalogLoader` 以 `Resources.Load<TextAsset>` 加载 `Assets/Resources/BattleData/unit-catalog-v1.json`，验证数值、枚举、ID、Prefab/Skeleton Resources 路径并公开只读 `UnitCatalog`；`LocalBattleLoader` 再读取 `task004a-real-1v1.json`，以目录定义连接玩家实例，构造已有的不可变 `BattleInput`。`BattleInputFactory` 同时接受旧 `battle-fixture-v1` 和适配后的 `local-battle-v1`，两者的 Core 输入、事件和 runner 语义不变。

目录生成器 `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs` 只读取 `Assets/GameData/Units/EliteVariants/Json/*.json` 中的 `unit-elite-variants-v2` 文档。共享 `UnitEliteVariantResolver` 要求精英 0 完整，并按最近较低条目继承高阶条目省略的 `stats.combat`、`stats.shared`、`model` 等原子块；`sourceVariant` 是物理资源文件夹权威。当前生成目标固定为精英 0，高阶变体不会进入 Player 目录。解析结果再经稳定排序、type ID/resource key/稀有度/目标价值校验、米/秒和秒/Tick 严格换算，以及 Unity/Spine 动画与时长验证后写入 Resources 输出；`Task004aSpineProbe` 保留为可重复的动画证据探针。

当前目录数据流为：

`Assets/GameData/Units/EliteVariants/Json/*.json → UnitEliteVariantResolver(target elite 0) → UnitCatalogGenerator → frozen flat unit-catalog-v1 → existing Player loaders`

v2 是唯一人工维护的单位源，首批只完成 `1000`、`5503`、`5504`，其余单位尚未导入。模型 `animations[]` 保存稳定 key、真实 Spine 名称和必需的源时长；`Default`、Hit、Skeleton 类型、动画行为和播放倍速不进入源契约。人工维护稀有度为 `1000=1`、`5503=6`、`5504=3`。现有 `unit-catalog-v1` 与 `ability-catalog-v1` 在首批迁移中保持字节冻结，因此 Player 仍暴露迁移前目录值；正式 Player 只读这些 Resources 目录，不读取 Editor 源或项目外 staging 数据。

`UnitCatalogEntry` 明确分离 `ResourceKey`、可为空的 `DisplayNameZhHans`、可为空的 `SkillDescriptionZhHans` 与 `LifeDeduct`；`UnitCatalogLoader` 对冻结目录的 `rarity=1..6` 和非负 `lifeDeduct` 进行运行时校验。`PlayerState` 将目录 `Rarity` 原样投影至 `StagingStackSnapshot`，UI 不由精英化等级或源文件推断稀有度。v2 能合法表达不攻击且不阻挡的单位，但旧扁平目录无法表达该组合，生成器会显式拒绝投影而不是强制改写。

`UnitFactory` 与 `UnitJsonBake` 已迁移为 v2 消费者，不保留双格式源读取。legacy v1 目录投影映射的 Skeleton Type 2 与空 Hit 名称仅是旧表现接口的临时传输值，不是人工维护单位事实；源直读 `UnitFactory` 适配器在正式运行数据路径不再需要后销毁。旧 Hit/presentation 链在旧目录和播放接口退役后销毁，完整清单维护于 `docs/bonds/UnitAnimation.md`。

`MappedBattlePresentationViewFactory` 现优先从冻结目录按真实 Core type ID 自动解析 `Prefabs/DefaultUnit`、SkeletonDataAsset、legacy ID、`unitskeltype` 和动画名；既有 Inspector Binding 仍是显式覆盖。新创建的 `UnitSkelBase` 在 `Start` 前由目录注入只读表现速度/间隔，避免依赖旧 `UnitFactory.SpawnAll` 的模板缓存。`UnitSkelPresentationView` 使用目录动画；空 Hit 为兼容期无操作降级，缺 Death 仍沿用隐藏已死亡视图的策略。整个桥接仍只从结果事件流向 Unity，绝不反向写入 Core。

## 14. TASK-005 固定真实对战 Demo（2026-07-18）

- `ARKnoNIGHTS.Battle.Demo` 中的 `BattleDemoCoordinator` 是固定 Demo 的显式状态机：`Idle`、`LoadingComputing`、`Ready`、`Playing`、`Paused`、`Completed`、`Error`。它通过 `LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1")` 连接真实目录和玩家快照，在局部 `BattleRunner` 完整运行并封存 `BattleRunResult` 后才创建 `BattleEventPlaybackController`；协调器不保留可继续 Step 的 runner。
- `BattleDemoCoordinator` 暴露 input/event/result 的稳定 FNV-1a 摘要、玩家/单位摘要、演示 Tick、事件进度、winner/unresolved reason 与结构化最后错误。Replay 仅重新初始化同一封存结果；`Recalculate` 是独立的明确调试入口。Home/Away、暂停与速度只作用于 Presentation。
- `SampleScene` 保留唯一 Build Settings 入口、旧 `InitButton`、`ButtonTest` 和 `FoldButton`。新增独立顶层 `BattleDemoRoot`，其中 `BattleDemoViews` 是数据驱动工厂创建真实视图的父节点，`BattleDemoUI` 是排序层级 100 的独立 uGUI Canvas。根对象通过序列化字段关联 `BattleDemoController`、`MappedBattlePresentationViewFactory`、视图父节点和 UI；没有在运行时按场景名称查找这些对象。
- `BattleDemoUi` 在其明确根节点下创建轻量 uGUI 控件：Start/Continue、Pause、Replay、Recalculate、0.5x/1x/2x 速度循环，以及 Home/Away 观察视角。状态区域显示 schema、真实玩家与 type ID、摘要、Tick、事件消费、winner/reason 和最后错误。稳定日志前缀为 `[BattleDemo]`，只在操作和状态转换时输出。
- `UnitSkelPresentationView.SetFacing` 使用投影后的世界方向：默认朝右（世界 X 正向、60 度基础倾角），X 负向时只附加一个 Y 轴 180 度翻转，纯 Z 方向保留最近左右朝向。回放器按当前展示 Tick 的活动（或最近完成）Move 段提供移动方向；开局只会使用第一段已开始的 Move，绝不预先采用整场最后一段 Move 的方向。Attack 消费时记录攻击者和目标在该 Tick 的权威位置，并以目标方向覆盖不早于它的 Move 朝向；后续 Move 可再次接管朝向。Home/Away 仍只改变坐标投影，不改变 Core 位置、事件或结果。
- `OnDisable`/`OnDestroy` 调用协调器 `Dispose`，从而停止播放器并释放本场视图；场景和旧原型静态入口不接收 Demo 状态。`Assets/Game/Editor/Battle/BattleDemoSceneSetup.cs` 是幂等的场景接线工具：发现已有 Demo 根对象时拒绝修改，避免覆盖已有接线。

## 15. TASK-006 构建与 Player 验收入口（2026-07-18）

- `Assets/Game/Editor/Battle/Task006StandaloneBuild.cs` 提供 `Task006StandaloneBuild.BuildWindowsX64`：只读取现有 Build Settings 启用场景、构建 `StandaloneWindows64`、默认写入忽略的 `Temp/TASK-006/WindowsStandalone`、记录 `BuildReport` 摘要并以可靠退出码结束；它不改动场景、Package 或 ProjectSettings。`RunEditorAcceptance` 以相同真实 Resources 输入记录十次稳定摘要。
- `Assets/Game/Runtime/Initial/Task006PlayerAcceptance.cs` 仅在 Player 明确收到 `-task006-acceptance` 时创建临时验收器。该验收器驱动现有 `BattleDemoController` 加载真实目录/对战快照，比较十次权威计算，完成 Home、Away 和 Replay，并在帧末释放重播旧视图后记录摘要、退出。普通手动 Demo 不读取该参数时不会改变行为。
- 2026-07-18 的 Editor 与 Player 摘要均为 `0160DA1D/F247BEA4/C578716F`。固定真实快照采用 Home 3 对 Away 4，双方混用 `gopro`（`1000`）和 `arcslma`（`5503`）并打乱部署坐标；Away 胜，最终两个 Away `arcslma` 存活。摘要来自不可变 `BattleInput`、`BattleRunResult` 与事件 DTO 的稳定 FNV-1a 表示，不使用运行时 `HashCode`。

## 16. UI-002 正式待部署 HUD（2026-07-21）

- `ARKnoNIGHTS.UI` 是独立的 uGUI 表现程序集。`StagingHudController` 挂在 `SampleScene/FormalBattleHudRoot`，在 `Awake` 中只创建一份 `LocalPlayerState`，订阅其只读 `Changed` 快照，并在销毁时取消订阅；加载失败会输出 `[StagingHud][playerState.load.failed]`，保持正式 HUD 的空/错误态，不回退到 `ButtonDebug` 或 `UnitFactory`。
- `PlayerState.StagingStackSnapshot` 增加了只读 `PortraitResourcePath`。它仍由 Player-safe `UnitCatalog` 在创建权威堆叠快照时填充，UI 不查询旧集合、源 JSON 或场景对象来推断头像；既有 type ID、Cost、精英化、完整 Buff 和有序 unit ID 继续构成 UI 的只读输入。
- HUD 运行时在根对象下创建 `FormalBattleHudCanvas`（参考 `1920×1080`，排序层 `200`）、`StagingArea` 和 `DeploymentCostPanel`。单位头像资源统一为 Default Texture：`portraitResourcePath → Resources.Load<Texture2D> → UnitPortraitLoader` 创建并缓存整图 Sprite，再交给既有 uGUI `Image` 和 `RectMask2D` 裁切；加载链不会先探测 Sprite。槽位继续显示当前精英化装饰、Cost 与始终显示的 `Xn` 数量，且不创建第二份权威排序。`ToggleSelection` 只记录稳定堆叠 ID，不修改玩家状态，并在最新快照不再包含该 ID 时清除选择，供 UI-003 使用。
- `StagingHudLayout` 是无场景依赖的 UI_SPEC 计算：自然布局右对齐，压缩布局铺满；压缩选择使选中槽恢复自然宽度，其他槽按距离等差下降并在最小 `7/9` 头像宽度处钳制，最终总宽保持在可用宽内。
- `BattleDemoUiVisibilityToggle` 只控制 `BattleDemoUI` 的 Canvas、GraphicRaycaster 与 CanvasGroup，默认隐藏，默认组合键为 `Ctrl+Shift+F10`。它不禁用 `BattleDemoRoot`、`BattleDemoController` 或 Presentation 工厂。
- `UI002SampleSceneSetup.SetupSampleScene` 是幂等场景接线工具：添加/复用唯一 `FormalBattleHudRoot`，给现有 `BattleDemoUI` 增加 CanvasGroup 并序列化绑定，默认失活 `InitButton`、`ShopPanel`、`FoldButton`、`ButtonTest` 和旧 `VirtualSlotPanel` 对象；它不改地图、相机、Package 或 BattleDemoRoot。

## 17. UI-003 状态驱动部署与撤退（2026-07-21）

- `StateDrivenDeploymentController` 是 `FormalBattleHudRoot` 上唯一的准备阶段输入拥有者。正式待部署槽只向它报告稳定堆叠 ID；控制器从该快照的有序 `unitIds` 选择首个具体实例，并以 `Idle`、`Dragging`、`SelectedDeployed`、`Disabled` 状态管理预览、选择和输入锁。它不会读取 `ButtonDebug`、`UnitFactory` 或 `UnitDeployment` 原型数据。
- `PreparationGridProjection` 将 UI/世界候选投影为一基本地阵型：格心为 `(x * 100, 0, y * 100)`。拖拽期间预览自由跟随鼠标投影到的世界坐标；松手时才换算、验证并由 `PlayerState.TryDeploy` 决定是否吸附到格心。
- 已部署单位重定位使用同一控制器的来源标记拖动会话：首次世界点击只选择，只有当前 `selectedUnitId` 对应 view 的后续超过阈值手势才能开始拖动。该会话临时移动已有 view，不在松手前写入 `PlayerState`；松手时调用 `TryRelocateDeployed`，由 `PreparationUnitViewCoordinator` 的同一 Changed 快照同步移动或交换结果。失败、同格 no-op、交互锁、隐藏准备视图和生命周期取消都会恢复权威 Formation 投影；`DeployedSelectionChanged` 仍携带原始稳定 unit ID，因此 HUD 选择不会跳到被交换单位。
- 拖拽预览由 `PreparationUnitViewBuilder` 使用 Player-safe 目录和真实 `DefaultUnit` 资源创建，但不进入玩家快照、不会占格或扣费；取消/锁定时销毁。它的 `UnitIdentity` 同时持有唯一运行时整数 ID 与稳定 PlayerState 实例 ID；成功部署或撤退后，`PreparationUnitViewCoordinator` 订阅同一 `PlayerState.Changed` 快照并幂等维护 `player unit ID → PreparationUnitView` 的一对一映射；该根 `PreparationUnitViews` 与 `BattleDemoViews` 生命周期分离。
- 已部署视图与待部署槽共用一个互斥选择：选择任一待部署槽会清除场上选择，选择已部署视图或开始拖拽会清除待部署槽选择。`PreparationSelectionIndicator` 是独立世界空间 SpriteRenderer 组：命名子物体 `Overlay` 与 `ReturnToStaging` 是各自的中心锚点，内部 `Graphic` 只负责补偿图集 Sprite pivot；两者按 `1.5×` 缩放，菱形水平平行地面。组根取“单位 Transform 锚点的 `z + 50` → 摄像机”直线与 `y=200` 平面的交点；撤退图标使用局部二维坐标 `(-60,60)` 并创建与自身可见尺寸相符的命中框。命中框只发起 `TryRetreat`，成功后由快照清理视图和选择组；它不直接改 Cost、阵型或 GameObject 权威状态。
- `SetInteractionEnabled(bool)` 是 UI-004 的阶段锁入口：禁用时取消预览、清选和关闭撤退输入。`UI003SampleSceneSetup.SetupSampleScene` 幂等地只向既有 `FormalBattleHudRoot` 添加控制器，不修改地图、相机、BattleDemoRoot 或 DefaultUnit。

## 18. TASK-007 DefaultUnit 世界空间状态条（2026-07-22）

- `DefaultUnit.prefab` 根物体附加 `UnitWorldStatusBar`，并包含失活的 `WorldStatusBar` 子层级：`Background`、非渲染布局锚点 `MaxHpCapacity`、`CurrentHpFill` 与 `ShieldFill`。它们使用 MeshRenderer/MeshFilter，不使用 Canvas、uGUI、Collider 或战斗逻辑；状态条生命周期随单位 GameObject 销毁。
- `UnitWorldStatusBarLayout` 是无场景依赖的展示公式：总宽 `90`，先计算最大血量容量段，再计算其中当前 HP 的左锚填充，护盾段从右边缘向左填充。满血无盾隐藏；`maxHitPoints <= 0` 隐藏且每个组件实例最多记录一次带 unit ID 的诊断。所有 clamp 仅影响几何，不回写 Core。
- 数据流为 `UnitCatalogEntry.Definition.MaxHitPoints → MappedBattlePresentationViewFactory.ConfigureStatusBarMaximumHitPoints → UnitSkelPresentationView`。`BattleEventPlaybackController` 在 Spawn、Damage、Death 与 Home/Away 观察者切换时调用只读的 `SetStatusBarState(unitId, isEnemy, currentHitPoints, currentShield)`；当前事件流始终传入 `currentShield = 0`，没有新增护盾结算机制。
- `UnitStatusBarOverlay` 是只用于状态条的局部透明 Shader：共享材质配合 `MaterialPropertyBlock` 设置贴图、Sprite UV 与颜色，不在更新中实例化 `renderer.material`。背景使用 `slider_hp_back`，我方/敌方 HP 分别使用 `slider_hp_fill`/`slider_hp_enemy`；`ShieldFill` 复用填充 alpha 但由 `_ForceWhite` 输出白色。Shader `ZWrite Off`、`ZTest LEqual`，不会强制穿透地面；没有修改全局渲染管线或 Quality/Graphics 设置。
- 状态条以单位的局部 Y/Z 偏移定位并保持与单位贴图共面。左右朝向变化会立即重新计算局部左右锚点，`LateUpdate` 继续兜底更新位置，防止转向后直到受击才恢复正确血量/护盾方向。

## 19. UI-004 本地阶段与运行时战斗输入（2026-07-22）

- `ARKnoNIGHTS.Round` 将可测试的 `PreparationBattlePhaseMachine`、`PreparationBattleSealer` 和 `PlayerStateBattleInputAdapter` 与 Unity 场景分离。时钟只有 `Loading/Preparation/Battle/Error`，以 30 秒 unscaled 输入推进，跨过零点只产生一次转换；sealer 在 PlayerState 上按已确认顺序提交 Overflow 删除、必要的 `(5,2)` 自动部署和 BattleInput 封存。

## 20. UI-005 正式 HUD 与截图证据边界（2026-07-23）

## 21. UI-INFO-001 详情投影边界（2026-07-24）

数据单向流为 `PlayerStateSnapshot -> BattleInput -> PresentationViewState -> UnitDetailResolver -> FormalBattleHudController`。`ARKnoNIGHTS.Details` 承载不可变详情 DTO 和解析器，Core 不反向依赖该程序集。

- `FormalBattleHudController` 在既有 `FormalBattleHudRoot` 上建立正式顶部状态栏、玩家 Cost/占位资源区、待部署槽和单位信息面板；它只读取既有 `PlayerState`、`PreparationBattleLoopController`、`StateDrivenDeploymentController` 与 BattleDemo 的状态，不复制玩家状态或重新计算战斗结果。
- 信息面板选择由统一路由维护：选择待部署槽、已部署单位或战斗敌人会清除另两个来源，避免多个单位面板同时成为权威。面板血条独立于 TASK-007 的世界空间条；`HealthValue` 左上锚定在剩余血条右上，数值超过 9 个字符时缩小字号。
- 中文字体为 Noto Sans SC normal，数字字体为 Novecento Wide Normal Regular；未确认的赤金、玩家生命和页签业务保持显式占位或禁用。
- `UnitInformationCaptureRunner` 通过 Player 命令行入口产出固定状态 PNG 与布局 JSON 清单，记录 capture stage、Canvas scale、详情组件 screen rect、文本字体/字号/对齐/字符串、图标资源和参考映射。默认 `-uiCaptureSuite` 只使用真实生产状态；额外的 `-uiCaptureVisualFixture` 才会渲染空名和中等长度中文名夹具，且不会写入 `PlayerState`、单位目录或战斗输入。`scripts/ExportUiInfoEvidence.ps1` 基于清单生成面板裁切、参考并排图和调整记录，作为逐图审查的可追溯证据，不能替代人工 GUI 流程验收。
- UI-INFO-002 将 `UnitInformationPanelLayout` 作为上半部统一缩放的几何来源；`FormalBattleHudController` 构建时缓存九个图标，并为默认导入的独立 PNG 一次性创建 Sprite。
- `PreparationBattleLoopController` 是 `FormalBattleHudRoot` 的运行时幂等桥。它等待 UI-002/003 初始化，加载 Player-safe catalog 与固定 `task004a-real-1v1` 的 Away 快照，锁定输入并隐藏 `PreparationUnitViews` 后启动运行时战斗；Completed 时释放 `BattleDemoViews`、恢复准备投影/交互并重置时钟。场景重载通过 `SceneManager.sceneLoaded` 重新附加，且不会创建多个桥。
- `BattleDemoCoordinator.StartRuntimeBattle` 是固定 Resources 入口之外的加法入口：它接收已验证 `BattleInput + UnitCatalog`，仍由局部 `BattleRunner` 先计算再复用原 Playback 生命周期。正式循环模式会阻止调试 Start/Recalculate 重载固定输入，但保留暂停、速度、观察视角和同一封存结果 Replay。Core 的 HP、死亡和 winner 未回写 PlayerState。

## 22. UI-009 四玩家双战斗场景接线（2026-07-26）

- `LocalMatchState` 现在保留 fixture 中的玩家源顺序，并提供按玩家 ID 的只读查询。`PreparationBattleLoopController` 把正式 HUD 已创建的本地 `PlayerState` 作为本地覆盖项传给 `LocalMatchStateLoader`；因此没有为本地玩家创建第二份权威状态，其他三名玩家仍是 fixture 快照。
- `FourPlayerBattleRoundSealer` 在一次准备阶段转换中按源顺序封存四名玩家，并固定配对 `P1/P2 -> MatchAB`、`P3/P4 -> MatchCD`。每名玩家只执行一次 Overflow 清理和自动部署，然后以该封存快照构造两份独立的 `BattleInput`。最高费用候选若在 `typeId`、精英化等级及完整 Buff 集合上严格相同，按稳定 `unitId` 选择首项；其他并列最高候选仍返回结构化歧义错误。该规则只解决已确认的严格堆叠实例，不扩展部署规则。
- `MultiBattlePresentationCoordinator` 是 UI-009 的场景外协调层：它对每份输入仅运行一次 `BattleRunner`，保存不可变结果及其 Presentation Track，并把四名玩家稳定映射到 `(matchId, BattleObserverView)`。它只为当前观察目标绑定场景视图，以一个共享演示 Tick、暂停/倍速和重播控制两场已完成结果；切换观察目标不会重跑 Core 或写回任何 `PlayerState`。共享 Tick 到达最大 EndTick 后，`BattleTrackPlaybackController` 聚合当前视图的终局表现状态；协调器保持 Playing，直到死亡动画与变黑视图全部结束后才报告一次完成转换，正式循环此后才 Reset。这里没有硬编码终局等待时间，终局 Tick 切入未创建死亡视图时可以立即完成。
- `PreparationBattleLoopController` 在 `Preparation -> Battle` 时调用四玩家 sealer 和多战斗协调器，复用现有 `BattleDemoRoot` 的已序列化 Presentation Factory 及视图根，不创建第二套相机、Demo 根或权威 runner。`TryObserveBattlePlayer` 仅在战斗阶段接受已知玩家 ID，并把 `LocalMatchState` 的观察变化转交给协调器；`BattleHudSceneCoordinator` 将左侧玩家列表按钮接入该入口。
- `FormalBattleHudController` 与 `UnitInformationCaptureRunner` 优先读取当前多战斗 Track 的观察侧和状态，保留旧单场 `BattleDemoCoordinator` 作为兼容回退。因此状态栏敌人数、战斗单位选择和详情面板不会把旧 Demo 的 Idle 状态误当作当前战斗。
- `BattleTrackPlaybackController.ViewStates` 按当前 `PresentationTick` 采样 Track，而非采样最终 Tick；这保证了血量、死亡、动作和位置在共享时间轴上的场景投影与已封存结果一致。演示层仍没有修改战斗结果的路径。
- `SampleScene`、Prefab、Package 和项目设置没有因正式 HUD 组合而修改；既有 `FormalBattleHudRoot`、`PreparationBattleLoopController` 和 `BattleDemoRoot` 的接线足以自动进入此流程。商店、准备按钮和玩家列表由运行时组合层创建。

## 23. 正式 HUD 场景生命周期与证据入口（2026-07-26）

- `BattleHudSceneCoordinator` 由 `SceneManager.sceneLoaded` 幂等附加到既有 `FormalBattleHudRoot`。它是组合层，不保存第二份 `PlayerState`、`LocalMatchState`、`BattleRunner` 或 Track；`LocalMatchState`、`PreparationBattleLoopController`、`ShopReadyHudController`、`PlayerListObserverCoordinator` 和 `FormalBattleHudController` 仍分别拥有既有数据与职责。
- 唯一阵型命令门控为 `Preparation && !观察他人 && !本地已准备`。观察远端时，`StagingHudController.DisplayedSnapshot` 只替换只读显示快照，命令仍只使用 `Snapshot`（本地玩家）；本地 `PreparationUnitViews` 隐藏，远端已部署单位通过 `BattlefieldWorldProjection` 的 Away/180° 投影生成只读观察视图。切回本地会恢复本地快照和本地准备视图。
- 阶段进入战斗会清除部署/待部署选择、关闭商店并清除二次确认，但等级入口保持可见，玩家可在战斗阶段重新打开商店。战斗完成回到准备时，`LocalMatchState.ResetPreparationUiState()` 仅重置本地 `ready=false` 和观察目标为本地玩家，本身不改写单位、阵型、赤金、商店、生命或已封存战斗结果；商店变化来自独立的战后领域操作。购买、刷新、冻结和升级命令在准备与战斗阶段均写入持续的本地 `LocalMatchState`，但已封存的战斗输入和 Track 不受影响；阵型命令仍只允许在未准备的本地准备阶段执行。
- `LocalMatchState` 为每个 `LocalMatchPlayerData` 持有独立的六槽商店，快照为四名玩家分别投影商店；购买、冻结和付费主动刷新仍只定位 `LocalPlayerId`。`ShopOfferOrdering` 在领域层按目录稀有度、十进制数值 `typeId` 和生成顺序执行稳定排序，UI 继续只按稳定 `ShopSlotId` 显示 `snapshot.LocalPlayer`，不保存第二份排序。
- `PreparationBattleLoopController.ReturnToPreparation()` 在 `MultiBattlePresentationState.Completed` 后、清空 multi-battle observation map 前调用一次 `RefreshAllShopsAfterBattle()`；该同步操作保留各玩家冻结槽、刷新其余槽、推进临时固定商品源并只发布一次完整四玩家快照。`ResetPreparationUiState()` 仍只负责 ready 与观察目标，不负责商店刷新。
- `PlayerListHudController` 把纯列表模型投影为左侧四张可点击头像卡，显示本地、被观察、掉线、已退出、生命与返回本地状态。`PlayerListLayout` 保持 `bg_player_list` 的 `116×534` 参考尺寸，只把 `Player_<playerId>` 行缩放到 `98.6×107.1`；四行左边缘为 `x=15.7`，使用 `13px` 间距并在背景内纵向居中。运行时先创建背景，再创建各玩家行。玩家生命背景和 `Novecento wide` 数字均完全位于头像区域内，`icon_self` 左边缘与头像左边缘对齐。`LocalMatchPlayerSnapshot.HasExited` 与 `IsConnected` 分开保存：`HasExited` 将头像资源替换为 `UI/Texture/player_list/equip_replace_avatart_bg`，`PlayerListEntryPresentation.ShowsLostConnection` 对全部未连接玩家成立；`LostConnection` 子物体晚于头像创建，因此退出玩家的 `icon_lost_connect` 绘制在替换头像之上。当前 fixture 将 Player3 标记为掉线、Player4 标记为已退出，两种状态都只影响显示，不改变本地 Demo 的固定配对或战斗输入。JSON 中当前未提供的 `avatar_1..4` 会稳定回退到已有 `ProfilePicture` 资源；该回退只影响外观，不修改玩家档案或命令归属。选择任意单位时由 `FormalBattleHudController.SelectionChanged` 隐藏列表。
- `FormalBattleHudController.SetSessionHudValues` 只显示 loop-owned 本地赤金、生命和下一对手名称。当前固定测试数据因此显示赤金 `7`、生命 `400`；生命扣除、淘汰及经济规则仍未实现。
- `ShopReadyHudController` 使用全屏锚定的组合根，但等级、商店和准备按钮均按 `1920×1080` 参考矩形计算。当前商店面板和内部内容按早期布局的 `1.5×` 呈现：六张 `237×262.5` 商品卡以 `12px` 间距贴商店右边缘排列，升级卡紧邻第一张商品卡左侧并允许越过面板左边缘；面板整体左移 `10px`、上移 `40px`。升级与商品价格背景统一下移 `15px`，商品价格再左移 `1px`；冻结和刷新按钮分别额外右移 `10px` 与 `15px`，并统一相对商店面板局部基线下移 `30px`。刷新价格背景仍以刷新按钮为父物体，底边与按钮底边重合，数字相对背景上移 `3px`。`SetRefreshFreePresentation(bool)` 是保留的 UI-only 接口：启用时加载 `cost_free` 并隐藏费用数字，关闭时恢复 `cost_bg_1/2` 与数字；它不调用 `LocalMatchState`、不改变刷新按钮可用性或实际扣费。升级确认渐变占据升级卡底部 `166.5×81`。冻结/刷新图标和标签相对含阴影按钮的几何中心上移 `9px`。准备按钮固定在赤金和部署费用区上方并与两者共中垂线。刷新命令单击即执行；冻结命令通过 `LocalMatchState.TrySetOccupiedShopSlotsFrozen` 一次设置全部非空槽位并只发出一次状态通知。准备状态使用 `ready_icon`，已准备状态使用 `icon_ready`。商店商品名和冻结、刷新、准备中文标签由缓存的方正黑体简体 `FangZhengHeiTiJianTi-1` 呈现，纯数字由 `FormalNumericFont` 呈现。`FormalHudSpriteLoader` 继续负责 Sprite/Texture 混用的通用 UI 图；商品单位头像则统一走 `UnitPortraitLoader`，避免两条头像导入策略并存。
- `BattleHudSceneCoordinator` 订阅商店可见性和正式单位选择事件：打开单位信息面板会关闭商店，打开商店会清除单位选择并恢复玩家列表，避免两个左/上层信息区域同时占用界面。`BattleHudCaptureRunner` 仅在 Player 参数 `-battleHudCapture` 时运行，按真实商店、观察和阶段命令生成 `17` 张截图，并把阶段、本地/观察玩家、准备状态、商店六槽、选中比赛/观察侧、共享演示 Tick、Track 摘要、Canvas scale 和关键 RectTransform 写入 `battle-hud-manifest.json`；其中包含一张战斗阶段商店打开状态。若截图全黑或写入超时，入口明确失败并写入 `battle-hud-capture-failed.txt`。

## 24. Mainline 果冻召唤数据流与生命周期（2026-07-26）

- 权威数据流为 `Units/EliteVariants/Json(v2) + Abilities/Json → UnitEliteVariantResolver(target elite 0) + UnitCatalogGenerator/AbilityCatalogGenerator/SkillAnimationCatalogGenerator → frozen unit-catalog-v1 + ability-catalog-v1 + skill-animation-catalog-v1 → BattleInputFactory → BattleRunner`。解析器和三个生成器只读源数据并执行 schema、继承、引用、数值及稳定顺序验证；技能动画目录独立绑定单位、能力、动画 key/名称与源时长，不扩展或重写冻结的两个 v1 目录，也不会从资源或目录反向改写源 JSON。
- `LocalBattleLoader`、准备阶段封存器和 `PreparationBattleLoopController` 会把已验证的 ability definitions 与单位定义一起封存。`BattleInputFactory` 验证单位固有能力 ID 与 summon type 均可解析，遗漏目录或未知 ID 会结构化失败。
- 全局自动恢复为 `2 SP/s`，在 20 TPS 下每 10 Tick 增加一点；每个 `RuntimeAbilityState` 私有保存 SP 和施放次数。没有攻击占用时，`SUMMON_JELLY_MINIONS` 在 Tick 100/250 施放；若 SP 满时正在攻击，SP 封顶并延后到攻击动画结束后的下一 Tick，实际施放后才消费 SP，后续周期随之顺延。每次施放在施法者中心 100cm × 100cm 方形中确定性生成三个 5504，并分配递减负数 ID。
- Skill 事件携带动画 key、源动画 Tick 与有效动画 Tick。源动画 Tick 为 `ceil(durationSeconds × 20)`，所有 Skill 的有效占用固定为 `(sourceTicks + 1) / 2`，Presentation 固定以 `2×` 播放。Core 不允许 Skill 打断已开始的 Attack，并在 Skill 占用期间阻止施法者开始攻击或移动；Track 动作优先级为 `Death > Attack > Skill > Move > Idle`。
- 被动能力可携带持续或首次触发的整数生命阈值战斗修正。阈值比较以千分比交叉相乘完成，可组合攻击/防御/移速倍率、阻挡与攻速加算，也可在精确的限时 Tick 区间内把阻挡容量强制为0；状态在 Tick 开始、同批直接伤害之后及持续生命变化之后刷新，同批伤害不会因单位遍历顺序而部分使用新阈值。阻挡容量降低时按 unit ID 稳定释放超出新容量的关系。未被阻挡条件减伤在每次实际伤害结算时读取对称阻挡关系，仅修正物理/法术伤害。攻击序列修正在攻击成功开始时推进序号，并把该次快照攻击力与破防倍率封存在待结算攻击中；一次性/固定间隔增强和指定攻击前的永久状态切换均不受目标随后死亡影响。攻击计数状态可在自身转换时强制同阵营其他计数状态解放；传播后开始的攻击读取新状态，已开始攻击保持快照。范围攻击同样由封存序号决定，伤害 Tick 再以主目标当前位置扩展圆形半径或最近格十字区域；每个范围目标独立进入普通攻击伤害、闪避和反伤链。未阻挡攻击蓄力按实例激活 Tick 的固定间隔增长，作为全部攻击倍率后的最终固定加算进入 Attack 快照，并在该次待结算攻击到期时统一清零。
- 死亡后继能力在 Death 产生时封存所属玩家、阵营、位置、ability ID 与到期 Tick；延迟期间 pending successor 会维持该阵营的战斗存续，避免在后继体出现前错误判胜。到期项按 Tick、死亡单位 ID、ability ID 稳定处理，候选类型按权重哈希选择，位置偏移和动态负 ID 均可重放；Spawn 仍沿用 `ActivationTick = SpawnTick + 1`。延迟死亡范围伤害同样在 Death 封存阵营、位置、有效攻击力和效果，并维持终局待定；到期时按目标聚合同 Tick 伤害，读取目标当时的防御、法抗和承伤修正，但不触发普通攻击专属的反伤或闪避。当前 Core 没有地形图，“最近可通行格”仅在 9×8 非门格中按距离、X、Y 决胜；单位数据也没有独立空中命中标签。
- 外部数值修正按每 Tick 开始位置计算，并在移动、阻挡评估和死亡清理后刷新；范围使用厘米整数平方距离，支持全场/半径、敌我、排除来源、阻挡对手、同类型邻近计数和按 ability ID 不叠加。贡献按来源 unit ID、ability ID 排序后应用到攻击/防御/法抗/攻速/移速/回复，死亡来源同 Tick 后续阶段不再提供效果。物理/法术攻击闪避在伤害 Tick 以 battle ID、双方 unit ID、ability ID、伤害 Tick 和已封存攻击序号进行稳定千分位判定，成功时保留零伤害事件；真实伤害及非攻击伤害不进入该判定。命中后持续生命流失仅由正伤害普通攻击施加，目标按 ability ID 保存并刷新失效 Tick；生命周期阶段将其与其他每秒生命变化合并为整数余量 `HealthChanged`，不递归进入攻击反应链。沉默、异常状态与标签传播尚未进入 Core。
- Tick 在处理本 Tick 伤害、Death、阻挡解除和目标清理后判定终局；终局 Tick 不恢复 SP、不施放技能。正伤害普通攻击可按攻击序号或存活目标的受击次数触发生成；多目标攻击按原始攻击者+攻击序号去重，受击按稳定 Hit 顺序计数，生成前可检查同阵营同类型存活上限。一次性生命阈值生成会把 owner 当 Tick 位置映射到最近战场格，并在有效且非门的四个正交相邻格中心建立实例。非终局 Spawn 只创建无路径、无目标、无阻挡继承的新实例，`ActivationTick = SpawnTick + 1`，之后按普通单位规则重新索敌。
- `BattleRunResult.UnitSnapshots` 是初始和动态实例的不可变索引。`BattlePresentationTrackCompiler` 从此索引建立动态 Track；Playback 在越过 Spawn Tick 时创建目录映射视图，Replay/Dispose 清理旧视图，并以相同实例 ID 重建，绝不重新运行 Core。
