# TASK-007：DefaultUnit 世界空间血量与护盾状态条

## 角色

你是本 Unity 项目的单位战斗状态表现 Agent。

你的职责是在已经完成的确定性战斗与演示闭环上，为 `DefaultUnit.prefab` 增加世界空间血量/护盾状态条，并把它接入真实单位创建、Damage 事件、Home/Away 观察视角切换、Replay 和清理流程。

本任务明确允许最小修改 `DefaultUnit.prefab`、在 Prefab 或运行时对象上挂载已有/新增脚本，以及新增必要的局部材质、Shader 或渲染子物体。不得借机实现护盾战斗机制、重写单位表现或修改全局渲染管线。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`，重点是 7.6“单位世界空间血量与护盾状态条”
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `docs/TASK-004.md`
- `docs/TASK-004A.md`
- `docs/TASK-005.md`
- `docs/TASK-006.md`
- `docs/decisions/`（若存在）
- TASK-004～006 的完成报告、测试 XML、Player 日志和未验证项
- `ProjectSettings/GraphicsSettings.asset`
- `ProjectSettings/QualitySettings.asset`
- `Assets/Resources/Prefabs/DefaultUnit.prefab`
- `Assets/Resources/UI/Texture/slider_hp_back.png` 及 `.meta`
- `Assets/Resources/UI/Texture/slider_hp_enemy.png` 及 `.meta`
- `Assets/Resources/UI/Texture/slider_hp_fill.png` 及 `.meta`
- `Assets/Game/Runtime/Initial/MappedBattlePresentationViewFactory.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`
- `Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs`
- `Assets/Game/Battle/Presentation/Playback/BattleEventPlaybackController.cs`
- `Assets/Game/Battle/Core/Events/BattleEvents.cs`
- `Assets/Game/Battle/Core/Simulation/BattleRunner.cs`
- `Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs`
- `Assets/Game/Battle/Demo/` 下的控制器与状态机代码
- `Assets/Game/Tests/EditMode/Battle/` 和 `Assets/Game/Tests/PlayMode/Battle/` 下的现有表现测试
- `Assets/Scenes/SampleScene.unity` 中 Main Camera、地图、BattleDemoRoot 和 BattleDemoViews 的文本区域

开始前执行 `git status --short`，保护所有用户修改。`DefaultUnit.prefab`、贴图、场景、表现脚本或测试若已有未提交改动，先阅读并确认可以增量合并，不得覆盖。确认没有其他 Unity Editor/batchmode 进程打开同一项目。

## 任务背景

当前真实状态：

- 第一阶段战斗计算、真实单位输入、事件回放、Home/Away 观察视角和 Demo 已完成；
- Core 已有单位血量上限、运行时当前血量、Spawn/Damage/Death 事件，但没有护盾生成、吸收或消耗机制；
- `BattleEventPlaybackController` 已跟踪单位 side 和当前 HP，观察者可以在 Home/Away 之间切换；
- `UnitSkelPresentationView` 负责真实单位位置、朝向和动画，但没有状态条接口；
- `MappedBattlePresentationViewFactory` 从 `unit-catalog-v1` 取得真实单位定义，其中包含 `MaxHitPoints`；
- `DefaultUnit.prefab` 当前根物体旋转约 60°、缩放 25，根 `MeshRenderer.sortingOrder=0`，没有血条子物体；单位左右朝向会旋转整个根 Transform；
- 三张血条图片目前是普通 Texture 资源，实际尺寸分别约为背景 `555×22`、敌方填充 `539×27`、我方填充 `555×22`；
- 项目使用 Built-in Render Pipeline，`GraphicsSettings.m_CustomRenderPipeline=0`，不是 URP/HDRP；
- 如果把血条子物体直接沿单位局部 Y 轴下移，它会因根物体旋转进入地面以下；如果直接继承单位左右转向，血量左侧和护盾右侧语义还会被镜像。

因此本任务既要接通状态数据，也要建立独立、稳定的世界空间渲染层级。Renderer sorting 对透明物体有效，但不一定能越过不透明地图的深度遮挡；必须通过实际场景验证选择局部方案，而不是先改全局渲染管线。

## 本任务目标

1. 在 `DefaultUnit.prefab` 上挂载一个单一职责的世界空间状态条组件，并建立背景、血量、护盾所需的渲染子物体或等价结构。
2. 状态条不是 Canvas、Slider、RectTransform 或屏幕空间 UI；它跟随单位移动，但不参与物理、战斗计算或输入。
3. 为状态条提供明确的只读输入：unit ID（用于诊断）、是否为当前观察者的敌人、血量上限、当前血量、当前护盾，以及必要的生命周期状态。
4. 从真实单位目录或明确的 Spawn 契约取得血量上限，不得从首次 Damage、贴图宽度或单位名称推断。
5. Spawn 时初始化状态；Damage 后即时更新当前血量；Home/Away 观察者切换时即时更新敌我颜色；Replay/销毁时正确重置和清理。
6. 当前 Core 没有护盾事件，正常战斗回放传入 `currentShield=0`；同时保留可直接设置非零护盾的表现接口并验证布局。
7. 严格实现 SPEC 的隐藏条件、左右布局与血量/护盾比例公式。
8. 状态条视觉总长度为 90 Unity 世界单位，位于单位脚下约 25 Unity 世界单位，保持面向观察者且不随单位左右转向镜像。
9. 确保状态条和单位整体在地图之上可见；优先采用局部 Transform、Renderer、材质深度或渲染队列方案，不修改全局渲染管线。
10. 使用已有三张血条贴图；敌方血量使用 enemy 贴图，我方血量使用 fill 贴图，背景使用 back 贴图，护盾显示为白色。
11. 用精简的公式测试和一个真实 Prefab PlayMode 冒烟验证完成闭环，不扩建大规模测试基础设施。
12. 实现完成后更新 ARCHITECTURE 和 TEST_PLAN，使其只记录实际实现和真实验证结果。

## 已确认规则

### 数据和敌我关系

- 状态条至少接收 `isEnemy`、`maxHitPoints`、`currentHitPoints`、`currentShield`；允许附带 unit ID、BattleSide 和 observer 用于计算、诊断和清理。
- `isEnemy` 相对于当前观察者：Home 观察者下 Away 是敌人，Away 观察者下 Home 是敌人。
- 观察视角切换只改变状态条敌我外观，不改变 Core side、事件、胜方或任何权威数据。
- 当前战斗没有护盾机制，战斗事件流中的护盾值默认为 0。本任务只实现显示输入，不实现护盾结算。

### 资源和尺寸

- 背景：`Assets/Resources/UI/Texture/slider_hp_back.png`；
- 敌方血量：`Assets/Resources/UI/Texture/slider_hp_enemy.png`；
- 我方血量：`Assets/Resources/UI/Texture/slider_hp_fill.png`；
- 护盾：纯白色，可复用合适的填充纹理并通过材质/颜色显示白色；
- 状态条总长度固定为 `90` Unity 世界单位；
- 状态条视觉中心位于单位脚下方向约 `25` Unity 世界单位；允许为防止地面遮挡增加很小的朝相机/离地偏移，但屏幕观感仍应接近脚下 25；
- 状态条必须保持水平、面向观察者，单位向左/向右时不得镜像血量与护盾。

### 显示公式

令 `W=90`、`M=maxHitPoints`、`H=clamp(currentHitPoints, 0, M)`、`S=max(currentShield, 0)`、`C=M+S`。布局必须保留“先分配血量上限宽度，再在其中缩放当前血量”的两层语义：

- `MaxHpWidth = W×M/C`，从状态条左边缘开始；
- `CurrentHpWidth = MaxHpWidth×H/M`，从 MaxHpWidth 的左边缘开始；
- `ShieldWidth = W×S/C`，从状态条右边缘向左显示；
- `S=0`：`MaxHpWidth=W`；仅 `H<M` 时显示，满血隐藏整个状态条；
- `S>0`：无论是否满血都显示；CurrentHpWidth 末端与 ShieldWidth 起点之间的 MaxHpWidth 空白部分表示已损失血量；
- `M<=0`：隐藏状态条并产生一次可诊断错误，不允许除零，也不得修正 Core 数据；
- 当前血量和护盾的 clamp 仅用于表现几何，不能反向写回权威状态。

### 生命周期和渲染

- 满血无盾时，背景、血量和护盾全部隐藏；
- 0 血量无盾时，单位仍可见期间显示空背景；单位隐藏、销毁或回放清理后，状态条随之消失；
- 不额外规定死亡动画期间血条保留时长，沿用现有单位 GameObject 生命周期；
- 状态条和单位不能被地面遮挡；当前项目使用 Built-in Render Pipeline，不应为本任务修改 GraphicsSettings、QualitySettings 或引入 URP/HDRP。

## 不属于本任务的内容

- 不实现护盾生成、吸收伤害、消耗、叠加、持续时间、破盾事件或护盾动画；
- 不实现治疗、Buff、技能、状态图标、数值文字、等级、费用、攻击条、Boss 血条或玩家血条；
- 不改变伤害公式、Damage/Death Tick、胜负、移动、索敌、阻挡或攻击行为；
- 不把状态条放入屏幕 Canvas，不使用 uGUI Slider；
- 不修改 SampleScene 的战斗布局、按钮或 Demo UI，除非仅为无法绕过的序列化引用且先报告；正常方案应通过 `DefaultUnit.prefab` 自动覆盖所有真实战斗单位；
- 不重做 `DefaultUnit`、Spine、单位动画、地图、美术或相机；
- 不修改全局渲染管线、升级 Package 或引入第三方渲染/UI/tween 依赖；
- 不为未来完整护盾系统提前扩展 Core 状态机或事件族；
- 不扩大 TASK-006 的 Standalone 验收矩阵。

## 预计影响文件或目录

候选范围：

- `Assets/Resources/Prefabs/DefaultUnit.prefab`
- `Assets/Game/Runtime/Data/Unit/` 下新增的世界空间状态条组件及必要表现适配
- `Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs`
- `Assets/Game/Runtime/Initial/MappedBattlePresentationViewFactory.cs`
- `Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs`
- `Assets/Game/Battle/Presentation/Playback/BattleEventPlaybackController.cs`
- `Assets/Game/Battle/Core/Events/BattleEvents.cs` 或 Spawn 载荷：仅当需要显式传递 MaxHitPoints，且先证明现有目录注入方案更差时
- `Assets/Game/Battle/Presentation/Rendering/` 或等价目录中的局部材质/Shader
- 三张 `slider_hp_*.png` 的 `.meta`：仅当选择 SpriteRenderer 且必须改 TextureImporter 时
- `Assets/Game/Tests/EditMode/Battle/`
- `Assets/Game/Tests/PlayMode/Battle/`
- 新文件对应的 `.meta`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`（只更新实际状态）

本任务获准修改 `DefaultUnit.prefab` 并挂载脚本。优先不修改 `SampleScene.unity`、三张源 PNG、现有 Spine SkeletonDataAsset/材质、Package 和 ProjectSettings。若必须修改共享 Spine 材质或全局 GraphicsSettings，停止并报告精确原因与更小替代方案。

## 实施要求

### A. 先建立数据契约

1. 画出当前 `UnitDefinition.MaxHitPoints → Spawn/Factory → BattleEventPlaybackController → UnitSkelPresentationView` 的实际数据路径。
2. 血量上限必须显式到达状态条。优先选择以下最小方案之一：
   - 由已加载 `UnitCatalogEntry.Definition.MaxHitPoints` 在工厂创建视图时配置状态条；或
   - 给 Spawn 事件增加稳定的 MaxHitPoints 载荷，使封存事件流自包含。
3. 不得默认“Spawn 的 HitPointsAfter 永远等于 MaxHitPoints”而省略血量上限；若采用该等价关系，必须先把它写成明确输入约束并说明未来预受伤单位的迁移影响。
4. 为表现视图增加语义清晰的状态更新入口或窄接口。避免让 `BattleEventPlaybackController` 直接查找具体 MonoBehaviour、子物体名称或 Renderer。
5. 当前护盾值在正常事件回放中传 0；组件公开一个可测试的非零护盾设置入口。不得把该入口误写成权威护盾系统。

### B. 实现纯布局计算

6. 将可见性、敌我资源选择、HP 宽度、Shield 宽度和左右锚点计算集中在无场景查找的纯逻辑中，便于少量表格测试。
7. 使用浮点数只计算表现几何；不得修改 Core 整数 HP。
8. 更新必须是幂等的：同一状态重复设置不会累积缩放、偏移或创建新材质/子物体。
9. `M<=0`、HP 越界或 Shield 为负时不抛未处理异常；按 SPEC clamp/隐藏并用稳定前缀最多记录一次相同诊断。

### C. 修改 DefaultUnit Prefab

10. 在 `DefaultUnit.prefab` 上挂载状态条组件，并建立职责清晰的 BarRoot、Background、MaxHpCapacity、CurrentHpFill、ShieldFill 或语义等价层级。MaxHpCapacity 可以是不渲染的布局锚点，但其宽度必须可检查。
11. 状态条必须使用世界空间 Renderer/Mesh，不使用 Canvas、RectTransform 或 Slider。
12. 注意根 Transform 的 25 倍缩放：Prefab 中的局部尺寸必须换算后得到约 90 的实际世界长度，而不是被放大到 2250。
13. 状态条不得随 `UnitSkelPresentationView.SetFacing` 的 Y 轴 180° 转向而镜像。可通过反向旋转、独立视觉子根、billboard/observer-facing 约束或等价最小方案实现。
14. 先把 MaxHpCapacity 以左边缘为锚点缩放到 `MaxHpWidth`，再把 CurrentHpFill 以该容量段左边缘为锚点缩放到 `CurrentHpWidth`；护盾以总条右边缘为锚点。不得跳过容量段，或用中心缩放后再凭经验补偿。
15. 避免每次状态更新调用 `renderer.material` 创建材质实例。优先使用共享材质、MaterialPropertyBlock 或预先建立的少量专用材质，并在销毁时不泄漏运行时资源。

### D. 解决地图遮挡

16. 先在 SampleScene 的真实 Main Camera、地图和一个 `DefaultUnit` 上复现脚下位置遮挡，记录是世界位置进入地面、深度测试、透明排序还是材质队列导致。
17. 优先级如下：
   1. 正确的 camera-facing/离地/朝相机小偏移，避免几何实际落入地面；
   2. 状态条内部稳定 Renderer order，保证背景 < HP < Shield；
   3. 仅作用于状态条的透明材质与渲染队列；
   4. 若不透明地图仍覆盖状态条，使用局部 `ZWrite Off` 和可证明合适的 `ZTest`/深度偏移方案。
18. Renderer sortingOrder 不能被当作一定能覆盖不透明深度的保证；必须用实际观察或截图确认。
19. 同时检查单位 Spine 本体是否被地图遮挡。只有实际复现时才做最小的单位 Renderer/材质/位置修正；不得未经验证把所有 Spine 材质全局改为 Always On Top。
20. 不修改 `GraphicsSettings.asset`、`QualitySettings.asset` 或引入 Scriptable Render Pipeline。若局部方案全部失败，停止并提交证据。

### E. 接入事件、观察者和生命周期

21. Spawn 后在第一帧正确初始化 max/current HP、Shield=0 和敌我颜色；满血无盾时不可闪现一帧血条。
22. 每个 Damage 事件消费后立即把 `HitPointsAfter` 传给状态条；演示暂停只停止事件推进，不应把已消费状态回滚。
23. `SetObserver(Home/Away)` 后遍历现存视图，按 `record.Side != observerSide` 重新计算 isEnemy 并更新贴图；不得重新创建单位或重新计算战斗。
24. Replay 必须从 Spawn 状态重新初始化；不能保留上一场剩余血量、护盾、敌我颜色或材质属性。
25. Death、缺失死亡动画的隐藏降级、StopAndClear、OnDisable 和 Dispose 后，状态条不得残留。
26. 状态条组件不使用 `GameObject.Find`、场景单例或每帧 Resources.Load。

### F. 文档与差异审查

27. 更新 ARCHITECTURE，记录实际组件、Prefab 层级、数据流和渲染方案；不要把未实现护盾机制写成事实。
28. 更新 TEST_PLAN，只记录实际运行的精简测试、人工观感和未验证项。
29. 审查 `DefaultUnit.prefab` YAML、组件 GUID、子物体层级、Renderer 材质引用、贴图 importer 差异和所有新 `.meta`。

## 兼容与迁移要求

- 保留 `DefaultUnit.prefab` GUID、现有 SkeletonAnimation、MeshRenderer、BoxCollider 和旧 `UnitFactory` 使用方式。
- 保留 `UnitSkelPresentationView` 的移动、朝向、Pause、速度、攻击、受击、死亡和 Dispose 行为。
- 如果扩展 `IBattlePresentationView`，同步更新真实视图、测试替身和所有调用方；不得为血条复制第二套播放器。
- 如果扩展 BattleEvent/Spawn，保持事件顺序、Tick/sequence 和确定性摘要稳定更新，并说明 schema/测试影响。
- 状态条不得写入 `RuntimeUnitState`、修改 BattleRunResult 或成为事件推进条件。
- Home/Away 切换只更新表现，不触发 Recalculate。
- 不删除或重新生成现有 `.meta`；修改 PNG importer 前检查是否有其他引用。
- Prefab 新增脚本必须由 Unity 生成/保留稳定 GUID，场景实例无需逐个手工挂载。

## 验证要求

本任务采用精简验证，不扩建完整测试矩阵。

### 自动验证

建立少量表格测试，至少断言：

1. 满血无盾隐藏、受伤无盾显示、满血有盾显示；
2. `M=100,H=50,S=0` 得到 MaxHpWidth 90、CurrentHpWidth 45；
3. `M=100,H=50,S=50` 得到 MaxHpWidth 60、CurrentHpWidth 30、ShieldWidth 30 且右对齐，MaxHpWidth 中剩余 30 为背景；
4. Home/Away 观察者切换时同一单位的 isEnemy/贴图选择翻转；
5. 非法 M 不除零且产生诊断。

增加一个真实 Prefab PlayMode 冒烟测试即可，覆盖：

- 实例化 `DefaultUnit` 后状态条组件与三层 Renderer 存在；
- 初始化满血无盾时隐藏；
- 设置受伤和非零护盾后可见、宽度/锚点正确；
- Damage 状态更新与 Replay/销毁无残留；
- 单位左右转向后血量仍在左、护盾仍在右。

不要求为每个像素、材质属性和所有战斗事件新增独立测试。测试 XML 必须存在、可解析、测试数大于 0、失败为 0，才能写“通过”。

### Unity 编译或运行验证

- 执行 Editor 脚本编译；
- 运行上述相关 EditMode/PlayMode 测试；
- 在 SampleScene 使用真实 `gopro`/`arcslma` 完整播放至少一场；
- 观察一次 Home 与一次 Away 视角，确认敌我颜色切换；
- 观察一次受伤后的即时缩短和满血无盾隐藏；
- 通过测试入口或临时运行时调用展示一次非零护盾；不得为此保存额外场景对象；
- 检查 Console 无材质泄漏、Missing Script、资源缺失、除零或未处理异常。

本任务不强制重跑 Windows Standalone 全套验收。若未构建，明确写“Standalone 未验证”，不得沿用 TASK-006 的旧构建结果证明本次 Prefab/Shader 修改。

### 人工检查

1. 血条总长约 90，位于脚下约 25；
2. 背景、我方、敌方三张贴图对应正确；
3. 护盾为白色且位于右侧；
4. 单位向左/向右时状态条不翻转；
5. 状态条和单位本体不被地图遮挡，也没有明显穿过不应覆盖的前景；
6. 两个单位重叠或移动时没有明显闪烁、Z-fighting 或层级互换异常；
7. 暂停、调速、Replay、Home/Away 切换后状态正确；
8. 死亡隐藏/销毁后没有残留状态条。

关键时间点截图可以作为人工证据，但不能替代数值/布局断言。

### 无法执行时

- 无法可靠观察遮挡、贴图观感或单位重叠时，写“人工表现未验证”并提供精确检查清单；
- 测试进程未正常退出但 XML 已生成时，分别记录测试结果与进程退出状态；
- 未执行 Standalone 时明确记录，不得写通过；
- 不得通过删除断言、隐藏对象或把材质改成全局 Always On Top 来制造通过。

## 验收标准

- `DefaultUnit.prefab` 已直接包含状态条组件和世界空间渲染层级；新实例无需场景逐个手工挂载；
- 状态条不是 Canvas/uGUI，且不包含 Collider 或战斗逻辑；
- 数据入口明确包含 isEnemy、MaxHP、CurrentHP、CurrentShield，并能定位 unit ID；
- 满血无盾隐藏；受伤无盾显示；有盾始终显示；
- MaxHpWidth、CurrentHpWidth 和 ShieldWidth 严格符合 SPEC 的两步公式；MaxHP/CurrentHP 左对齐，Shield 右对齐；
- 我方使用 `slider_hp_fill`，敌方使用 `slider_hp_enemy`，背景使用 `slider_hp_back`，Shield 为白色；
- 世界长度为 90，脚下视觉偏移约 25；
- Home/Away 切换立即更新敌我颜色，不修改源事件、结果或 winner；
- Damage 后状态条即时更新；Replay、Death、Dispose 后无旧状态或残留对象；
- 单位转向不会镜像状态条；状态条和单位整体不被地图遮挡；
- 没有修改全局渲染管线、Package、战斗机制或护盾结算；
- 精简自动测试数大于 0且失败为 0，Editor 无新增编译错误；
- Prefab、材质、Shader、贴图 importer 和 `.meta` 差异已审查；
- ARCHITECTURE/TEST_PLAN 与实际实现和验证结果一致。

## 停止并询问我的条件

- SPEC 中的血量/护盾宽度公式无法按现有贴图或单位结构实现，必须改变玩家可见语义；
- 必须决定护盾生成、吸收伤害、消耗、叠加或死亡时护盾处理；
- 必须修改全局 GraphicsSettings、QualitySettings、升级/引入渲染管线或第三方 Shader/UI Package；
- 必须批量修改 Spine SkeletonDataAsset、共享角色材质或所有场景单位实例；
- `DefaultUnit.prefab` 存在无法安全合并的用户修改、Missing Script 或 GUID 冲突；
- 需要修改 SampleScene/相机/地图结构才能继续，且局部 Prefab/材质方案均有失败证据；
- 为获得 MaxHP 必须破坏性重写事件 schema、BattleRunResult 或播放器；
- 需要新增未确认的死亡血条时长、数字显示或其他玩家可见规则；
- 连续三次有实质差异的尝试仍无法解决遮挡或方向问题。

## 完成报告格式

1. 修改文件与 Prefab 层级；
2. 状态条组件和数据入口；
3. MaxHP、CurrentHP、CurrentShield、isEnemy 的来源与更新时机；
4. HP/Shield 布局公式实现；
5. 使用的贴图、材质、Shader 和 Renderer 设置；
6. 地图遮挡根因和最终局部解决方案；
7. 对 `UnitSkelPresentationView`、Factory、事件/播放器的兼容修改；
8. 实际运行的命令和 Unity 操作；
9. Editor 编译结果；
10. EditMode/PlayMode 测试数量、XML 和结果；
11. Home/Away、受伤、护盾、转向、遮挡、Replay 和死亡人工结果；
12. Standalone 是否验证；
13. 未验证项；
14. 所作假设；
15. 遗留风险；
16. Prefab/材质/Shader/.meta 差异审查；
17. 最终工作区状态。
