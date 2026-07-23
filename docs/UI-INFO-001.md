# UI-INFO-001：单位信息面板上半部与真实战斗数据投影

## 角色

你是本 Unity 项目的单位信息只读投影、正式 HUD 表现和临时对手数据接入 Agent。

你的职责是在 UNIT-DATA-001 和 PREP-DEPLOY-001 的可靠数据与选择边界上，完成 `UI_SPEC.md` 已确认的 UnitInformationPanel 上半部分，并让待部署己方、已部署己方和战斗敌方都能显示同一套真实、可追踪的数据。

为了让战斗敌方信息可用，本任务同时补齐一个独立临时敌方玩家快照、双方全局唯一 unit ID 校验、准备阶段封存时的精英化元数据和只读运行时属性投影。UI 只能读取这些数据，不能反向修改玩家状态或权威战斗结果。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/UI_TASK_TABLE.md`
- `docs/UI-001.md`
- `docs/UI-002.md`
- `docs/UI-003.md`
- `docs/UI-004.md`
- `docs/UI-005.md`
- `docs/UI-005-REPORT.md`
- `docs/UNIT-DATA-001.md` 及完成报告
- `docs/PREP-DEPLOY-001.md` 及完成报告
- `docs/references/ui/battle_hud/UI_SPEC.md`，完整阅读第 5.3、9、11、12、13 节
- `docs/references/ui/battle_hud/图2.png`
- `docs/references/ui/battle_hud/图3.png`
- `docs/references/ui/battle_hud/图4.png`
- `docs/references/ui/battle_hud/图6.png`
- `docs/decisions/`（若存在）
- `Assets/Game/Runtime/Initial/FormalBattleHudUi005.cs`
- `Assets/Game/Runtime/Initial/UI005CaptureSuite.cs`
- `Assets/Game/UI/FormalHud/StagingHudController.cs`
- `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs`
- `Assets/Game/Runtime/Deployment/StateDrivenDeploymentController.cs`
- `Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs`
- `Assets/Game/Runtime/Round/PreparationBattlePhase.cs`
- `Assets/Game/Battle/Core/Input/BattleInput.cs`
- `Assets/Game/Battle/Core/Simulation/` 下运行时单位状态与 BattleRunner
- `Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs`
- `Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs`
- `Assets/Game/Battle/Presentation/Playback/BattleEventPlaybackController.cs`
- `Assets/Game/Battle/Demo/` 下 Demo Controller/Coordinator
- `Assets/Resources/PlayerData/local-player-state-v1.json`
- `Assets/Resources/BattleData/task004a-real-1v1.json`
- `Assets/Resources/BattleData/unit-catalog-v1.json`
- `Assets/Resources/ProfilePicture/`
- `Assets/Resources/UI/Texture/unit_panal.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitInformationPanel*.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitRarity1Icon.png` ～ `UnitRarity6Icon.png` 及 `.meta`
- `Assets/Resources/UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged.png.meta`
- 相关 EditMode/PlayMode 测试、截图 manifest 和 UI-005 日志

开始前执行 `git status --short`。当前正式 HUD、图片、图集切片名、场景、玩家状态、Battle Core、Presentation 和截图脚本可能都有未提交修改；必须先审计真实状态，增量实现，不得按旧任务文档覆盖现有成果。确认没有其他 Unity Editor 或 batchmode 进程占用项目。

## 任务背景

当前真实状态：

- `FormalBattleHudUi005` 已动态创建单位信息面板外壳、头像、类型/名称、精英化文本和 HP 条，但尚未实现图6式的完整上半布局与九个属性词条；
- 当前面板仍把目录 `UnitName`（实际为资源 slug）显示为单位名；
- 当前待部署槽没有稀有度图标；
- 当前 `UnitCatalogEntry`、PlayerState 和 Battle Presentation 暴露的数据不足以同时显示中文名、技能描述、目标价值、稀有度、精英化和六项实际属性；
- `BattlePresentationViewState` 目前只有 unitId、typeId、side、位置、当前 HP、存活状态；
- `BattleInput.UnitSnapshot` 目前没有实例精英化等级；
- UI-004 仍从 `task004a-real-1v1` 中抽取固定 Away 玩家，而不是从一个完整、独立的敌方玩家状态文件封存双方；
- 用户已确认临时敌方采用独立数据文件和独立 unit ID；本地玩家实例化/加载时必须确保其 unit ID 与敌方不同；
- BattleInputFactory 已有双方 unit ID 全局去重校验，但需要在准备阶段封存入口提供更清晰、可定位的失败信息；
- 当前 Buff 只有占位数据，没有属性修改语义。本任务不能假装已经应用 Buff。

本任务应建立一个窄的“选择 → 只读详情快照 → 上半面板”边界，而不是让 UI 分别到 UnitJson、PlayerState、BattleRunner、场景对象中拼凑真相。

## 本任务目标

1. 完成 UnitInformationPanel 上半部分的图6参考布局：名称、攻击方式/伤害类型、头像、稀有度、精英化、目标价值、两列八项属性和详细 HP 条。
2. 显示顺序严格为：
    - 第一行：单位名称；
    - 第二行：`近战 伤害类型`；
    - 第三行左侧：头像及稀有度/精英化覆盖；
    - 头像下：目标价值；
    - 右侧两列四行：生命值/移动速度、攻击力/攻击间隔、防御力/法术抗性、阻挡数/部署费用。
3. 生命值显示运行时 `MaxHP`；目标价值读取单位目录 `lifeDeduct`。
4. 生命值、移动速度、攻击力、攻击间隔、防御力、法术抗性读取当前单局/战斗时点的只读实际属性；不带已实现语义的 Buff 数据不得由 UI 自行解释。
5. 准备阶段无 Buff 单位可以显示封存前的实际基础值；战斗阶段从同一场 BattleInput/运行时状态投影读取，不在 UI 中重新解析源 JSON。
6. 为待部署槽和信息面板头像同时增加 `1～6` 稀有度图标。
7. 信息面板支持待部署己方、已部署己方和战斗敌方；敌方只可查看，不能获得撤退、移动或部署权限。
8. 新增一个独立、可重复加载的临时敌方玩家状态文件，使用独立 playerId、独立 unitId 和预设 Deployed 阵型。
9. 准备→战斗封存改为由本地玩家状态与临时敌方玩家状态共同构造 BattleInput，不再依赖从固定对战 fixture 抽取 Away。
10. 本地玩家实例化/加载及战斗封存时确保双方 unit ID 全局唯一；冲突时中止战斗并输出明确错误，不静默改号。
11. 将每个玩家单位实例的 `eliteLevel` 封存进 BattleInput/表现元数据，使战斗敌方信息面板显示真实实例精英化。
12. 战斗死亡、当前 HP 和表现状态不回写本地或临时敌方玩家源数据。
13. 建立统一、只读、可测试的单位详情快照，UI 不直接跨多个系统临时查值。
14. 复用并扩展 UI-005 截图入口，对三种选择来源生成结构化 manifest 和截图，实际打开图片比对图6指定区域。
15. 使用定向测试和少量场景检查完成验证，不重建完整 UI 自动化基础设施。
16. 完成后更新 SPEC、ARCHITECTURE 和 TEST_PLAN 的真实实现与验证状态。

## 已确认规则

### 信息顺序与字段

- 第一行显示简体中文单位名称，来源是 UNIT-DATA-001 的 `displayNameZhHans`。
- `displayNameZhHans` 仍为空时显示明确未配置占位（推荐 `--` 或“名称未配置”）并输出包含 type ID 的结构化诊断；不得显示 `resourceKey` 冒充玩家可见名称。
- 第二行显示 `近战 伤害类型`。当前只有近战单位；伤害类型根据真实枚举显示 `物理`、`法术` 或 `真实`。
- 技能描述不显示在上半部分；`skillDescriptionZhHans` 留给后续“技能”页签。
- 头像来自 `Assets/Resources/ProfilePicture` 对应目录路径。
- 头像右上角显示 `UnitRarity1Icon`～`UnitRarity6Icon`；头像右下角显示当前实例精英化等级图标。
- 稀有度是类型数据，范围 `1～6`；精英化是实例数据，范围 `0～3`，两者不得互相推断。
- 头像下方目标价值读取目录 `lifeDeduct`。
- 右侧属性顺序固定为：

| 行 | 左列 | 右列 |
|---:|---|---|
| 1 | 生命值 | 移动速度 |
| 2 | 攻击力 | 攻击间隔 |
| 3 | 防御力 | 法术抗性 |
| 4 | 阻挡数 | 部署费用 |

- 生命值是运行时 `MaxHP`，不是 `CurrentHP`；CurrentHP 只显示在下方详细 HP 条中。
- 移动速度、攻击间隔和法术抗性只显示数值，不附加 `m/s`、`s` 或 `%`。
- 阻挡数来自单位当前已确认的阻挡容量/配置；部署费用和目标价值来自目录配置。

### 运行时数值来源

- UI 只读取只读详情快照，不自行计算 Buff、伤害或战斗状态。
- 当前 Buff 语义尚未实现。对于 Buff 列表为空的测试单位，基础定义就是当前实际值。
- 如果某单位带有非空 Buff，而状态层尚未提供 Buff 结算后的实际属性，对受影响的六项动态属性显示 `--` 并输出一次可定位诊断；不得静默显示未应用 Buff 的 JSON 基础值。
- 战斗阶段 CurrentHP 从权威战斗/Presentation 当前状态读取。
- 战斗阶段 MaxHP 和其他实际属性必须来自本场封存输入或权威运行时状态，而不是 UI 重新读取 Editor 源 JSON。
- 详情投影不得修改 BattleRunResult、RuntimeUnitState、PlayerState 或事件流。

### 临时敌方玩家数据

- 临时敌方使用独立的本地数据文件，推荐 `Assets/Resources/PlayerData/temporary-opponent-player-state-v1.json`。
- 它复用当前完整玩家状态 schema：playerId、deploymentCost、单位 unitId/typeId/zone/formation/eliteLevel/buffs。
- 临时敌方 playerId 与本地玩家不同。
- 临时敌方 unitId 独立、稳定并与本地玩家 unitId 不同，推荐使用明确的 opponent 前缀，但不把前缀当成唯一校验手段。
- 临时敌方至少有一个预先配置在合法本地 `9×4` 阵型中的 Deployed 单位。
- 本地玩家实例化或加载时必须确保 ID 不与临时敌方冲突；封存 BattleInput 时再次进行双方全局唯一性校验。
- 发现冲突时中止本场战斗，输出双方 playerId、冲突 unitId 和稳定错误码；不得静默重新编号、丢弃单位或只保留一方。
- 临时敌方文件是只读测试输入。战斗死亡、当前 HP、移动或胜负不回写该文件或其跨回合玩家状态。
- 当前单机 Demo 不因此扩展多人房间、网络玩家或同步协议。

### BattleInput 与精英化

- 每个 BattleInput `UnitSnapshot` 或等价实例元数据需要携带 `eliteLevel`。
- `eliteLevel` 用于表现和详情 UI，不参与当前 Battle Core 伤害、移动、阻挡或胜负计算。
- 同一份 BattleInput 在 Home/Away 视角下保持相同实例元数据；观察视角只改变坐标/敌我表现，不交换 unit ID 或精英化。
- 只有 `Deployed` 单位参与当前战斗；Staging/Shop 可保留在玩家快照中，但不生成战斗单位。

### 布局与资源

- UnitInformationPanel 位于屏幕左侧、满高，宽度参考 `720/1920`，上半区域高度参考 `567/1080`。
- 面板层级在设置按钮、顶部状态栏和待部署槽之后。
- 图6只参考右下原点 `(880,880)` 到 `(50,460)` 对应的 `830×420` 区域；必须整体等比映射，不得横纵分别拉伸。
- 头像参考尺寸 `180×180`；稀有度图标参考 `45×45`。
- 九个词条共用 `unit_panal.png / UnitInformationPanelStatEntryBackground`。
- 图标与具体 Sprite 名严格按 `UI_SPEC.md` 9.3.4。
- 详细 HP 条使用 `UnitInformationPanelHealthBarBackground`、`UnitInformationPanelHealthBarFill` 和 `UnitInformationPanelHealthValueBackground`。
- HP 条只显示生命，不显示护盾；`FillWidth = 556 × CurrentHP / MaxHP`，数值为 `CurrentHP / MaxHP`，数字区域左上角跟随填充右上端。
- HP 数值长度超过 9 个字符时字号从 24 降为 20。
- 不因参考图改变已确认字体资源和正式 HUD 层级；字体大小与间距必须通过实际截图拟合。

## 不属于本任务的内容

- 不填写中文单位名或技能说明；用户会自行填写；
- 不实现技能页签内容、技能效果、阵营、种族、Buff 语义或属性加成；
- 不实现信息面板下半部分的正式内容或页签动画；
- 不实现商店、赤金、玩家生命扣除、完整回合结算、四人房间或网络同步；
- 不实现敌方可操作交互、敌方撤退、敌方拖动或编辑敌方阵型；
- 不改变战斗伤害、Tick、移动、索敌、阻挡、攻击、死亡或胜负；
- 不把 eliteLevel 应用于战斗数值；
- 不让 UI 持有或修改 RuntimeUnitState；
- 不重写 Battle Core、Presentation、正式 HUD 或 SampleScene；
- 不重做图1～图6、美术资源、字体、Spine、世界血条或渲染管线；
- 不升级 Unity/Package，不安装第三方 UI、Tween、JSON 或截图上传服务；
- 不以运行时复制当前玩家并自动改 ID 的方式生成敌方；敌方来自独立数据文件。

## 预计影响文件或目录

候选范围：

- `Assets/Resources/PlayerData/temporary-opponent-player-state-v1.json` 及新 `.meta`
- `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs`
- `Assets/Game/Runtime/Round/PreparationBattlePhase.cs`
- `Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs`
- `Assets/Game/Battle/Core/Input/BattleInput.cs`
- `Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs`
- `Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs`
- `Assets/Game/Battle/Presentation/Playback/BattleEventPlaybackController.cs`
- `Assets/Game/Battle/Demo/` 中只读状态/Coordinator 适配
- `Assets/Game/Runtime/Initial/FormalBattleHudUi005.cs`
- `Assets/Game/UI/FormalHud/StagingHudController.cs`
- `Assets/Game/Runtime/Initial/UI005CaptureSuite.cs`
- 现有 EditMode/PlayMode 测试
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`

UNIT-DATA-001 应已完成目录字段和 rarity/lifeDeduct 数据来源。本任务不应再次改源 UnitJson schema。优先通过运行时代码生成 UI，不修改 SampleScene；若确需修改场景或 Prefab，先报告原因并严格审查序列化差异。不得修改图片像素；现有 Sprite 切片名与 GUID 必须保留。

## 实施要求

### A. 审计现有 UI-005 而不是重做

1. 列出 `FormalBattleHudUi005` 已存在的面板对象、选择来源、HP 更新、截图入口和资源加载方式。
2. 对照 `UI_SPEC.md` 列出缺失项和错误项，特别是 resource slug 冒充名称、精英化文本替代图标、缺少 rarity、缺少九词条和运行时属性来源。
3. 保留已经正确的顶部状态栏、资源区、设置按钮、选择互斥和调试面板快捷键；不得为信息面板重建整套 HUD。

### B. 建立独立临时敌方状态

4. 新建一个独立敌方玩家状态 JSON，复用现有 PlayerState schema，不复制当前玩家运行时对象。
5. 使用独立 playerId 和稳定、可读、与本地不同的 unitId；配置至少一个 Deployed 单位及合法一基 `9×4` 坐标。
6. 敌方数据包含每个单位的 eliteLevel 和 buffs；当前测试可使用空 Buff，不能省略字段后依赖默认值。
7. 为玩家状态 loader 提供可复用的“加载为只读快照/独立 PlayerState”路径。临时敌方在本任务流程中不得接受部署/撤退命令。
8. 本地玩家加载后与敌方快照执行一次 unit ID 集合冲突检查；结构化错误至少包含 `player.unitId.conflict` 或语义等价稳定 code。
9. `PreparationBattleLoopController` 不再从 `task004a-real-1v1` 抽取 fixed Away。它应加载目录、本地 PlayerState 和临时敌方 PlayerState，并在封存时映射 Home/Away。
10. 保留 `task004a-real-1v1` 作为既有战斗回归 fixture；不要删除它。
11. 对双方玩家快照封存后，继续由 `BattleInputFactory` 做最终 schema、side、formation、type ID 和全局 unit ID 校验。

### C. 扩展 BattleInput 的实例表现元数据

12. 给 `UnitSnapshot` 或等价不可变实例数据增加 eliteLevel，并更新所有构造器、fixture loader、PlayerState adapter、测试工厂和 canonical summary。
13. legacy `local-battle-v1` fixture 如果没有 eliteLevel，不能静默把未知当成类型稀有度。选择一种兼容方案：
    - 为仓库内 fixture 明确补 `eliteLevel`；或
    - loader 对旧缺失值采用有文档的 `0` 兼容，仅限 schema v1，并在测试覆盖。
   优先给当前仓库 fixture 显式补字段，避免长期隐式默认。
14. eliteLevel 进入 Presentation/详情投影，但 BattleRunner 不读取它做战斗决策。
15. BattleInput 的 Home/Away 坐标映射继续使用现有确定性逻辑；不要在 UI 中再做第二次权威变换。

### D. 建立统一只读详情快照

16. 设计一个窄的不可变详情投影，至少包含：
    - unitId、typeId、side/owner、zone；
    - displayNameZhHans；
    - attackMethod、damageType；
    - portraitResourcePath、rarity、eliteLevel；
    - currentHitPoints、maxHitPoints；
    - moveSpeed、attack、attackInterval、defense、magicResistance；
    - blockCapacity、deploymentCost、lifeDeduct；
    - 是否有未解释 Buff、字段是否可用及诊断。
17. 不要求使用上述类名；但 UI 刷新方法只能消费该投影和表现资源，不应同时查询 PlayerState、Catalog、BattleInput、Presentation 和源 JSON。
18. 准备阶段：
    - Staging/Deployed 己方从 PlayerStateSnapshot + UnitCatalog 投影；
    - CurrentHP=MaxHP；
    - eliteLevel 来自实例；
    - 空 Buff 时六项动态属性来自当前单局定义。
19. 战斗阶段：
    - CurrentHP 和存活状态来自权威 Presentation/战斗状态；
    - eliteLevel 来自本场 UnitSnapshot；
    - MaxHP 和其他属性来自本场封存定义或权威运行时状态；
    - 不重新打开源 JSON。
20. 敌方详情同样通过 unitId 查本场 Away snapshot；不要以 typeId 匹配精英化，因为同类型可有多个实例。
21. 如果未来 Buff 非空但没有计算后属性，六项动态属性标记 unavailable，UI 显示 `--`；阻挡数、费用、目标价值仍按已确认配置显示，除非其自身也被未实现 Buff 明确影响且无权威值。
22. 详情投影更新必须随选择变化、PlayerState Changed、阶段切换、Battle spawn/damage/death、Replay 和视角切换保持一致；无需每帧解析全量数据。

### E. 完成信息面板上半布局

23. 将现有 `BuildInformationPanel` 拆成职责清晰的局部构建/绑定逻辑，避免继续在一个方法中堆叠所有 RectTransform，但不得为此重写整个 HUD。
24. 按 UI_SPEC 9.3.2 的比例实现 `720×567` 参考上半部与 `830×420` 图6详情区的等比映射。
25. 名称、第二行、头像、覆盖图标、目标价值和八词条必须按确认顺序出现；不得添加参考作品中未确认的职业、等级、信赖或评分。
26. 九个属性词条使用共同背景和指定图标。图标保持宽高比，背景可按词条矩形拉伸。
27. 数值显示不附加 `%`、`s` 或单位文本。无法提供的动态值显示 `--`。
28. 中文名为空时显示未配置占位；可以在旁边保留 type ID 作为调试信息，但不得让 type ID/slug取代名称。正式视觉若不需要 type ID，应只写结构化日志。
29. 攻击方式和伤害类型从枚举映射；当前 Melee 显示 `近战`。
30. 头像右上 rarity、右下 elite；两图标不得互相覆盖，也不得占用属性区。
31. 详细 HP 条按确认公式和锚点更新。死亡时 CurrentHP=0；clamp 只影响显示，不回写。
32. 下半面板和技能/阵营/种族页签保持现有占位状态；不得在此任务填充 skillDescription。

### F. 补齐待部署稀有度

33. `StagingStackSnapshot` 应已由 UNIT-DATA-001 提供 rarity；若没有，先停止并说明前置任务未完成，不得在 UI 中通过 typeId switch 硬编码。
34. 待部署槽头像右上角显示对应 `UnitRarity1Icon`～`UnitRarity6Icon`。
35. 图标固定参考 `45×45`，锚定当前可见头像区域右上角；槽位压缩时不横向压扁，也不被头像裁剪遮掉。
36. rarity 无效时隐藏图标并输出 type ID 诊断，不显示默认 1 级。

### G. 选择、权限与生命周期

37. 保留一个共享选中 unit ID；待部署槽、准备阶段已部署单位和战斗单位选择互斥。
38. PREP-DEPLOY-001 移动/交换后，信息面板仍显示原始被选中 unit ID，不能因坐标变化跳到交换对象。
39. 敌方战斗单位可点击查看，但只改变信息面板；不显示撤退入口、不允许拖动或调用玩家状态命令。
40. Home/Away 演示视角切换不改变本地玩家身份和操作权限。
41. 选中单位离开可查看集合、战斗清理、Replay 重建或场景销毁时，清除无效选择并隐藏面板。
42. 事件订阅在 OnDisable/OnDestroy 对称释放，不因重播或回准备阶段重复订阅。

### H. 截图与视觉核对

43. 复用 UI-005 现有截图套件和输出目录，不另建一套重型框架。
44. 至少生成 1920×1080 的：
    - 待部署己方信息；
    - 已部署己方信息；
    - 战斗敌方信息；
    - 至少一个 50% CurrentHP 的详细血条状态；
    - 待部署槽 rarity 图标在自然宽和压缩宽状态。
45. manifest 至少记录选择来源、unitId、typeId、displayName 是否配置、rarity、eliteLevel、九项显示值、CurrentHP/MaxHP、关键 RectTransform 屏幕矩形和截图路径。
46. Agent 必须实际打开生成图片，并与图6指定的 `830×420` 区域及 UI_SPEC 数值比较；不能只报告文件存在。
47. 自动图片检查只辅助验证布局、遮挡、字体和资源；运行时属性来源、ID 唯一性和权限必须由断言/日志验证。

### I. 文档

48. 在 SPEC 中记录临时敌方输入、双方 unit ID 唯一、eliteLevel 表现元数据、详情字段来源和 Buff 未实现时的显示规则。
49. 在 ARCHITECTURE 中记录玩家状态 → BattleInput → Presentation/详情投影 → HUD 的真实数据流，明确 UI 只读。
50. 在 TEST_PLAN 中记录实际执行的 ID、封存、详情投影、PlayMode、截图和人工视觉验证。
51. 不把用户尚未填写的中文文本、未实现 Buff 或下半页签写成完成。

## 兼容与迁移要求

- 保留 `local-player-state-v1.json` 的本地玩家生命周期和既有 unitId，除非发现与新敌方文件冲突；若冲突，优先调整新敌方设计，不能静默改本地持久实例 ID。
- 新敌方文件使用自己的 `.meta`；不得复制现有 `.meta` GUID。
- 保留 `task004a-real-1v1.json` 作为战斗回归 fixture。
- `PlayerStateBattleInputAdapter` 可以扩展为双方快照适配，但不得把敌方变成网络玩家抽象或过度通用房间系统。
- Battle Core 的运行算法和事件结果不因 eliteLevel 改变。
- 若 `UnitSnapshot` 构造器发生变化，更新全部调用方、fixture loader、测试替身和 canonical digest；说明摘要变化是元数据变化还是战斗行为变化。
- 保留 `IBattlePresentationView` 的现有动画/血条接口；详情投影优先放在 Coordinator/状态层，不让具体 View 成为数据权威。
- 保留 `FormalBattleHudUi005` 已正确实现的 HUD、选择和截图入口。
- 保留所有已重命名 Sprite 的 GUID、切片 rect、spriteID 和 internalID。
- 不删除或重建 SampleScene、DefaultUnit、图集或图片资源。
- 战斗结束后本地与敌方 PlayerState 的 Formation、Elite、Buff 和区域不因战斗 HP/死亡改变。

## 验证要求

### 自动验证

EditMode 至少断言：

1. 本地和临时敌方玩家文件均能加载；
2. 临时敌方具有不同 playerId、至少一个 Deployed 单位、合法坐标和显式 eliteLevel；
3. 双方 unit ID 全局唯一时成功封存；
4. 人为制造双方重复 unitId 时封存失败，错误包含冲突 ID 和双方 playerId，且不静默改号；
5. eliteLevel 从 PlayerStateSnapshot 进入 BattleInput UnitSnapshot，并在 Home/Away 两方保持正确；
6. eliteLevel 不改变同一战斗的伤害、移动、事件顺序或胜方；
7. 空 Buff 单位详情的六项动态属性等于本场实际定义；
8. 非空 Buff 且无计算结果时六项动态属性标记 unavailable，不回退到源 JSON；
9. lifeDeduct、deploymentCost、blockCapacity、rarity 和 displayName 分别来自目录的正确字段；
10. displayName 为空时详情不是 resourceKey；
11. CurrentHP 变化只改变详情 CurrentHP/HP 条，不改变 MaxHP 或玩家状态；
12. 准备阶段和战斗阶段同一 unitId 的 elite/rarity/配置字段一致；
13. Battle 结束后两个 PlayerState 快照未被 HP/死亡污染；
14. 信息面板数值格式不包含 `%` 或 `s`。

PlayMode 至少覆盖：

15. 选中待部署己方显示上半面板和正确 rarity/elite；
16. 选中已部署己方显示同一面板；PREP-DEPLOY-001 移动或交换后仍绑定原 unitId；
17. Battle 中选中敌方显示敌方真实 CurrentHP、elite 和属性；
18. 敌方选择不会出现撤退入口，也不能改变 PlayerState；
19. Damage 后 HP 条和 CurrentHP 更新；
20. 战斗完成、Replay 或返回 Preparation 后选择和订阅没有残留；
21. staging rarity 图标在自然宽和压缩槽位中保持 45×45 参考比例且不被裁掉；
22. 自动截图 manifest 与画面状态一致。

不要求为每个字体像素和每个参考图坐标写单元测试。

### Unity 编译或运行验证

- 执行 Editor 编译；
- 运行相关 EditMode 测试并记录测试数量；
- 运行相关 PlayMode 测试并记录测试数量；
- 在 SampleScene 自动加载本地玩家与临时敌方；
- 完成一次 Preparation → Battle → Preparation；
- 至少查看一次 staging、deployed 和 enemy 信息；
- 在战斗中观察一次 HP 下降；
- 运行 UI 截图入口并验证 PNG 可解码、尺寸正确、manifest 完整；
- 实际打开截图做视觉判断；
- 因本任务修改 Player-safe Resources、封存入口和 UI，执行 Windows Standalone 构建及最小运行冒烟；无法执行必须标为未验证；
- 检查 Console 无重复 ID、资源缺失、空引用、重复订阅或未处理异常。

### 人工检查

1. 面板第一行、第二行、头像/覆盖、目标价值和两列八项顺序正确；
2. 排版密度、字体层级和间距接近图6指定区域，而不是把整张图6拉伸；
3. rarity 在右上、elite 在右下，均未覆盖头像关键区域或属性区；
4. 九词条背景和图标对应正确；
5. 移动速度、攻击间隔、法抗没有额外单位字符；
6. HP 数字跟随填充右端，0%、50%、100% 不越界；
7. staging 压缩后 rarity 图标不横向变形、不被裁掉；
8. 中文名未填写时占位明确，没有显示 gopro/arcslma 冒充中文名；
9. 查看敌人不会出现撤退、拖动或费用变化；
10. 战斗结束后临时敌方文件和本地玩家状态未被战斗死亡改写。

### 无法执行时

- 用户尚未填写中文名称时，使用占位完成布局验证，并明确“中文内容待用户填写”；不因此编造文本；
- 无法执行截图时，布局视觉标记为未验证，但数据/权限测试仍需独立执行；
- 截图存在但 Agent 未打开审查，不得写视觉通过；
- 无法构建 Player 时，记录命令、退出码、日志和阻塞；
- 现有 Buff 无语义不是阻塞固定空 Buff fixture；必须显示 `--` 的非空 Buff 测试仍应覆盖；
- 无法选中敌方对象时，不得通过硬编码面板内容绕过，应定位选择/Presentation 数据边界。

## 验收标准

- 存在独立临时敌方玩家状态文件，具有独立 playerId、独立 unitId、显式 eliteLevel 和预设合法部署阵型；
- 本地玩家加载与战斗封存均能发现双方 unit ID 冲突；冲突时中止且不静默改号；
- UI-004 不再从固定对战 fixture 抽取 Away，固定 fixture 仍保留用于回归；
- BattleInput 单位实例携带 eliteLevel，但战斗结果不受其影响；
- 存在统一只读单位详情投影，正式面板不直接拼接多个权威来源；
- staging、deployed、enemy 三种选择均能显示同一上半面板；
- 名称、攻击方式/伤害类型、头像、rarity、elite、目标价值、八项属性和 HP 条均按确认顺序显示；
- 九项属性数据来源符合规则；无 Buff 时显示实际值，未实现 Buff 修改时显示 `--`；
- 生命值词条显示 MaxHP，详细 HP 条显示 CurrentHP/MaxHP；
- 数值不附带 `%`、`s`；
- staging 和信息面板都显示正确 rarity 图标；
- 敌方只读，不能撤退、移动或修改 PlayerState；
- 战斗死亡和 HP 不回写任何玩家状态文件；
- UI-005 其他 HUD 功能无新增回归；
- Editor 编译、相关 EditMode/PlayMode 和 Windows 构建实际结果可核对；未执行项明确标记；
- 截图和 manifest 存在、可解析，并已由 Agent 实际视觉审查；
- SPEC、ARCHITECTURE、TEST_PLAN 已同步实际实现；
- 最终 diff 没有源图片像素、图集 GUID、DefaultUnit、Battle 算法、Package、ProjectSettings 或无关资源变化。

## 停止并询问我的条件

- UNIT-DATA-001 未提供 resourceKey/displayName/skillDescription/lifeDeduct/rarity 的可靠目录契约；
- PREP-DEPLOY-001 未提供稳定选中 unit ID 或移动后选择保持；
- 必须决定中文单位名、技能说明、Buff 数值语义或技能页签内容；
- 需要让 eliteLevel 改变战斗数值；
- 发现本地/敌方 ID 冲突且只能通过破坏性迁移现有持久玩家 ID 解决；
- 需要改变主客场坐标映射、战斗胜负或 Core 算法；
- 需要将临时敌方扩展成多人房间、网络模型或运行时克隆当前玩家；
- 需要大幅重写 BattleInput schema、Presentation 或 HUD；
- 需要删除固定 fixture、场景对象、Prefab 组件或重建 `.meta`；
- 需要修改图片像素、图集切片 GUID、渲染管线、Unity/Package；
- 需要安装外部工具、Skill、插件或上传截图，而 AGENTS.md 的权限条件未满足；
- 工作区同一区域已有无法安全增量合并的用户修改。

## 完成报告格式

最后按以下结构报告：

1. 修改文件和新增资源；
2. 临时敌方文件 schema、playerId、unitId 与部署阵型；
3. 本地实例 ID 与敌方 ID 的唯一性保证；
4. Preparation 封存双方 BattleInput 的数据流；
5. eliteLevel 在 PlayerState、BattleInput、Presentation 和 UI 中的传递；
6. 统一详情快照字段及每项权威来源；
7. 上半面板布局、资源和数值格式；
8. staging rarity 显示；
9. 敌方只读权限和生命周期清理；
10. 实际执行的命令与 Unity 操作；
11. Editor 编译、EditMode、PlayMode、构建结果及测试数量；
12. 截图、manifest、视觉判断和复拍结果；
13. 未验证项；
14. 等待用户填写的中文内容；
15. 所作假设；
16. 遗留风险；
17. 最终 diff 审查；
18. 后续技能页签或 Buff 系统所需但本任务未实现的接口。
