# UNIT-DATA-001：单位源 JSON 规范化与目录契约迁移

## 角色

你是本 Unity 项目的单位数据契约与兼容迁移 Agent。

你的职责是把当前 `UnitJson` 和两个真实单位 JSON 从历史字段整理为一份命名、顺序、单位和用途明确的源数据格式，并让现有 `UnitFactory`、单位目录生成器、Player-safe 目录加载器、固有能力烘焙工具、玩家状态与战斗入口继续读取同一份权威数据。

本任务是一次有限的源数据迁移，不是战斗数值重做、资源重命名、完整本地化系统或 `UnitTemplate` 全量重构。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `docs/UI_TASK_TABLE.md`
- `docs/TASK-004A.md`
- `docs/UI-001.md`
- `docs/UI-005.md`
- `docs/references/ui/battle_hud/UI_SPEC.md`，重点是稀有度、单位信息面板和属性来源
- `docs/decisions/`（若存在）
- `ProjectSettings/ProjectVersion.txt`
- `Packages/manifest.json`
- `Assets/GameData/Units/UnitJson.cs`
- `Assets/GameData/Units/Json/gopro.json`
- `Assets/GameData/Units/Json/arcslma.json`
- `Assets/Game/Runtime/Initial/UnitFactory.cs`
- `Assets/Game/Runtime/Data/Unit/UnitTemplate.cs`，或实际定义 `UnitTemplate` 的文件
- `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs`
- `Assets/Game/Editor/UnitJsonAbilityBakeTool.cs`
- `Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs`
- `Assets/Game/Battle/Core/Input/BattleInput.cs`
- `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs`
- `Assets/Resources/BattleData/unit-catalog-v1.json`
- `Assets/Resources/PlayerData/local-player-state-v1.json`
- `Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs`
- `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs`
- 所有直接引用 `UnitJson`、旧字段名、`UnitCatalogEntry.UnitName`、Rarity、LifeDeduct、FixedAbility 或单位目录 DTO 的代码与测试

开始前执行 `git status --short`。当前这些 JSON、`UnitJson.cs`、目录、UI 和测试可能已有未提交修改；必须逐项阅读并增量迁移，不得覆盖用户填写的值或回滚无关差异。确认没有其他 Unity Editor 或 batchmode 进程占用同一项目。

## 任务背景

当前真实状态：

- `Assets/GameData/Units/UnitJson.cs` 同时存在 `id`、`uintName`、`ProfilePicture`、`Rarity`、`HP`、`atk`、`BlockRadius`、`FixedAbility`、`LifeDeduct`、`narrowTitle` 等不同风格字段；
- `gopro.json` 和 `arcslma.json` 是当前仅有的真实单位源 JSON，也是目录生成器的权威输入；
- `UnitCatalogGenerator` 会把米/秒转换为厘米/秒，把秒按 `20 Tick/秒` 向上取整为 Tick，并验证 Spine 动画；
- 当前生成目录把 `uintName` 写入 `unitName`，所以资源键和玩家可见名称被混为同一个字段；
- 当前目录没有中文单位名、中文技能描述或目标价值 `LifeDeduct` 的完整 Player-safe 契约；
- `UnitFactory` 仍会将源 JSON 映射到带有历史序列化字段名的 `UnitTemplate`；
- `UnitJsonAbilityBakeTool` 使用一个只读取 `id + FixedAbility` 的轻量 DTO；
- `StagingStackSnapshot` 尚未公开单位稀有度，而待部署槽和单位信息面板都需要可靠读取 `1～6` 的稀有度；
- 用户将在本任务完成后自行填写中文单位名和技能描述，Agent 不得猜测或翻译这些文本；
- 单位自带技能描述允许为空，空技能描述不是错误。

这意味着本任务需要原子地修改源格式及其全部直接消费者，但必须保持现有单位数值、资源路径、动画名、战斗 Tick 结果和现有序列化资源兼容。

## 本任务目标

1. 为单位源 JSON 定义并实现 `unit-source-v1`（或语义等价且明确版本化的）规范格式。
2. 将 `UnitJson` 字段统一为 lower camel case，并按“身份与文本 → 养成与费用 → 行为分类 → 战斗数值 → 阻挡/价值/能力 → 资源与动画”的固定顺序排列。
3. 把 `gopro.json` 和 `arcslma.json` 一次性迁移为新字段，删除旧键，不长期保留双格式源文件。
4. 新增 `displayNameZhHans` 和 `skillDescriptionZhHans`；两个字段暂时保留空字符串等待用户填写，不得生成虚构中文内容。
5. 让 `skillDescriptionZhHans` 为空成为合法状态；`displayNameZhHans` 为空时允许数据迁移和目录生成，但必须保持“未配置”语义，不能回退为 `resourceKey` 并伪装成玩家可见中文名。
6. 让单位目录明确区分资源键、中文显示名和中文技能描述，并携带 `lifeDeduct`、稀有度及现有 UI/战斗所需字段。
7. 让玩家状态或其只读待部署投影能取得目录中的稀有度，以供后续 UI 显示；UI 不得自行回读 Editor 源 JSON。
8. 更新 `UnitFactory` 和固有能力烘焙工具，使其只读取新字段；继续通过适配层写入旧 `UnitTemplate` 字段，不破坏现有序列化资产。
9. 保持现有战斗数值、单位换算、动画、目录资源路径和确定性结果不变。
10. 增加针对新源格式、目录映射、合法空技能描述、稀有度范围和旧键消失的最小自动验证。
11. 同步更新相关 SPEC、ARCHITECTURE 和 TEST_PLAN，只记录本任务实际实现和实际验证的结果。

## 已确认规则

### 建议采用的源 JSON 字段与顺序

以下是本任务的目标字段顺序。若现有 Unity `JsonUtility` 对某个等价实现造成明确限制，可以采用语义等价的数据结构，但不得恢复混合命名或同一语义多个字段：

```text
schemaVersion
typeId
resourceKey
displayNameZhHans
skillDescriptionZhHans
rarity
deploymentCost
initialEliteLevel
attackMethod
actionMethod
unitSkeletonType
maxHitPoints
attack
defense
magicResistance
moveSpeedMetresPerSecond
attackIntervalSeconds
attackAnimationDurationSeconds
attackRadiusMetres
blockRadiusMetres
canBlock
blockCapacity
tauntLevel
lifeDeduct
damageType
innateAbilityIds
skeletonDataResourceName
profilePictureResourceName
moveAnimation
attackAnimation
hitAnimation
deathAnimation
```

### 旧字段到新字段的语义映射

| 旧字段 | 新字段 |
|---|---|
| `id` | `typeId` |
| `uintName` | `resourceKey` |
| 新增 | `displayNameZhHans` |
| 新增 | `skillDescriptionZhHans` |
| `Rarity` | `rarity` |
| `cost` | `deploymentCost` |
| `initialEliteLevel` | `initialEliteLevel` |
| `attackMethod` | `attackMethod` |
| `actionMethod` | `actionMethod` |
| `unitskeltype` | `unitSkeletonType` |
| `HP` | `maxHitPoints` |
| `atk` | `attack` |
| `def` | `defense` |
| `res` | `magicResistance` |
| `moveSpeed` | `moveSpeedMetresPerSecond` |
| `attackInterval` | `attackIntervalSeconds` |
| `attackAnimationDuration` | `attackAnimationDurationSeconds` |
| `attackRadius` | `attackRadiusMetres` |
| `BlockRadius` | `blockRadiusMetres` |
| `isBlock` | `canBlock` |
| `blockCapacity` | `blockCapacity` |
| `narrowTitle` | `tauntLevel` |
| `LifeDeduct` | `lifeDeduct` |
| `damageType` | `damageType` |
| `FixedAbility` | `innateAbilityIds` |
| `skeletonData` | `skeletonDataResourceName` |
| `ProfilePicture` | `profilePictureResourceName` |
| 四个动画字段 | 对应同名 lower camel case 字段 |

### 字段语义

- `schemaVersion` 使用稳定明确的值，推荐 `unit-source-v1`。
- `typeId` 保持当前正整数语义；生成 Player-safe 目录时继续转换为不带格式变化的十进制字符串。
- `resourceKey` 是 Resources 子目录、对象命名或其他技术查找使用的稳定键，不是玩家可见名称。
- `displayNameZhHans` 是简体中文玩家可见名称。当前由用户后续填写；空值不得自动替换为 `resourceKey`。
- `skillDescriptionZhHans` 是单位自带技能的简体中文说明，允许合法为空。
- `rarity` 只允许 `1～6`；不得再把 `0` 当成有效默认等级，也不得 clamp。
- `initialEliteLevel` 只允许 `0～3`，并继续作为创建玩家单位实例时的初始值。
- `moveSpeedMetresPerSecond` 的单位是米/秒；`1 格 = 1 米 = 100 Unity 世界坐标单位`。
- `attackIntervalSeconds` 是两次开始播放攻击动画的最短间隔，单位为秒。
- `attackAnimationDurationSeconds` 是攻击动画从开始到出伤的时长，单位为秒。
- 目录生成继续使用 `20 Tick/秒`，秒到 Tick 的非整数结果向上取整。
- 当前战斗中的共享攻击/阻挡半径仍以 SPEC 已确认的 `0.25 米` 规则为准。本任务只规范源字段，不得利用 `attackRadiusMetres` 或 `blockRadiusMetres` 改变现有战斗机制。
- `lifeDeduct` 是目标价值；当前只用于数据与 UI 显示，不实现玩家生命扣除。
- `innateAbilityIds` 可以是空数组；不得因为技能描述为空而删除已有固有能力 ID。

## 不属于本任务的内容

- 不填写、翻译或推测单位中文名称和技能说明；
- 不设计完整本地化表、语言切换、字体回退或文本热更新系统；
- 不调整 `gopro`、`arcslma` 的战斗数值、费用、稀有度、精英化、资源名或动画名；
- 不改变 Tick 频率、秒到 Tick 的向上取整规则、伤害、移动、阻挡、攻击或胜负逻辑；
- 不让源 JSON 直接进入 Player 构建；Player 仍通过生成的 Resources 目录读取数据；
- 不把 `UnitTemplate` 的历史序列化字段全部重命名，也不批量重写现有 ScriptableObject、Prefab 或场景；
- 不实现技能效果、Buff 解释、目标价值结算、玩家生命扣除、商店或网络同步；
- 不实现单位信息面板布局；本任务只提供后续 UI 所需的可靠数据；
- 不重命名 Spine、头像、Prefab、贴图或其 `.meta`；
- 不升级 Unity、Package 或引入第三方 JSON/本地化依赖。

## 预计影响文件或目录

候选范围：

- `Assets/GameData/Units/UnitJson.cs`
- `Assets/GameData/Units/Json/gopro.json`
- `Assets/GameData/Units/Json/arcslma.json`
- `Assets/Game/Runtime/Initial/UnitFactory.cs`
- `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs`
- `Assets/Game/Editor/UnitJsonAbilityBakeTool.cs`
- `Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs`
- `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs`
- `Assets/Resources/BattleData/unit-catalog-v1.json`
- 与上述契约直接相关的 EditMode 测试
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`

可能需要同步修改直接引用旧目录字段的 UI、调试或测试代码，但必须先用 `rg` 列出引用并说明原因。优先不修改 `SampleScene.unity`、Prefab、ScriptableObject、图集、图片、Package 和 ProjectSettings。若发现必须修改序列化字段或批量重写资源，停止并询问。

## 实施要求

### A. 建立迁移基线

1. 列出所有旧字段的直接读取方和生成产物，至少覆盖 `UnitFactory`、目录生成器、目录加载器、能力烘焙、玩家状态和正式 HUD。
2. 在修改前记录两个源 JSON 的语义快照：类型 ID、技术资源键、稀有度、费用、精英化、六项战斗数值、时间、资源路径、动画、能力、目标价值。
3. 记录当前生成目录的 canonical summary 或等价稳定摘要，作为迁移后数值不变的对照。
4. 不把当前工作区的未提交 JSON 改动当成可丢弃的生成内容。

### B. 规范源数据类型

5. 将 `UnitJson` 改为只表达新字段；不得长期保留旧字段作为静默兼容别名。
6. 字段类型应尽量延续当前数据语义，避免无必要的浮点/字符串互转。`typeId` 必须能无损映射到现有 int `UnitTemplate.typeID` 和字符串 Battle type ID。
7. 为结构性必填字段、数值范围、资源键和枚举映射建立明确校验。错误必须包含源文件和 type ID。
8. `displayNameZhHans` 空值在本任务迁移期允许通过，但生成目录必须保留空值或显式缺失状态，并产生可定位诊断；不得写成 `gopro`/`arcslma` 来冒充显示名。
9. `skillDescriptionZhHans` 空值完全合法，不产生“技能缺失”错误。
10. `innateAbilityIds` 的空数组合法；null 应规范化为空集合或明确处理，不能导致能力烘焙异常。

### C. 一次性迁移两个真实 JSON

11. 只迁移当前两个真实源文件，保留全部现有值和资源名。
12. 按本提示词规定的字段顺序输出，统一缩进和换行；不得顺手格式化无关 JSON。
13. 删除所有已迁移的旧键。迁移后同一文件不得同时出现 `HP/maxHitPoints`、`uintName/resourceKey` 等双写。
14. `displayNameZhHans` 和 `skillDescriptionZhHans` 写为空字符串，由用户后续填写。
15. 不新增虚构单位、测试专用战斗数值或隐藏兜底。

### D. 迁移目录生成和加载契约

16. `UnitCatalogGenerator` 只读取新字段，保持：
    - 米/秒精确转换为整数厘米/秒；
    - 秒乘 20 后向上取整为 Tick；
    - Spine 资源与必要动画验证；
    - 稳定按 type ID 排序；
    - 输出不依赖 Editor 文件枚举顺序。
17. Player-safe 目录必须明确暴露：
    - `resourceKey`；
    - `displayNameZhHans`；
    - `skillDescriptionZhHans`；
    - `lifeDeduct`；
    - `rarity`；
    - 既有战斗定义、费用、精英化、头像、Skeleton 与动画字段。
18. 不得继续用一个 `UnitName` 同时代表技术资源键和玩家可见名称。若为了调用方迁移暂时保留旧属性，必须标记用途并在本任务内迁移全部仓库内调用方。
19. 可以在保持 `unit-catalog-v1` 的前提下做本地加法扩展；如果 Agent 判断必须提升目录 schema，必须先列出所有资源、loader、fixture、测试和 Player 兼容影响并停止询问，不得静默改版。
20. 目录加载器对 `rarity` 强制校验 `1～6`；非法时返回带 type ID 的结构化错误。
21. `lifeDeduct` 至少为可显示的非负配置值；不要在本任务中赋予结算行为。
22. 重新生成目录后，检查 JSON 只包含真实数据，不包含 `isSyntheticFixtureData=true`。

### E. 兼容旧运行时入口

23. `UnitFactory` 通过一个清晰适配步骤把新 `UnitJson` 映射到现有 `UnitTemplate` 历史字段；不得借本任务重命名 `UnitTemplate` 序列化字段。
24. 资源路径继续由 `resourceKey + skeletonDataResourceName` 和 `profilePictureResourceName` 构成，最终 Resources 路径与迁移前一致。
25. `UnitJsonAbilityBakeTool` 改为读取 `typeId + innateAbilityIds`；空列表不报错，已有 `SPLIT_THREE` 等 ID 不丢失。
26. `StagingStackSnapshot` 或等价只读 UI 投影增加可靠 `rarity`。它必须来自目录，不从精英化推断；非法值不显示错误图标并产生诊断。
27. 不在 UI 控件中直接解析源 JSON。

### F. 文档

28. 在 `docs/SPEC.md` 增加或修改与单位数据字段、中文文本空值、稀有度范围和目标价值来源直接相关的规则。
29. 在 `docs/ARCHITECTURE.md` 记录源 JSON → Editor 目录生成 → Player-safe 目录 → PlayerState/Battle/UI 的实际数据路径。
30. 在 `docs/TEST_PLAN.md` 记录实际新增和实际执行的验证；未运行项必须标为“未验证”。

## 兼容与迁移要求

- 保留两个源 JSON 及其 `.meta` 的 GUID；不得通过删除再创建的方式迁移。
- 保留 `DefaultUnit.prefab`、Spine、头像、UnitTemplate ScriptableObject 和现有场景引用。
- 不修改现有类型 ID `1000`、`5503`，也不修改本地玩家已有实例 ID。
- `UnitTemplate` 继续作为旧原型调用方的兼容对象；新规范字段到旧字段的映射必须显式可审查。
- `UnitFactory.GetUnitBasicValueSO`、旧 UI 或调试入口若仍需要 `UnitTemplate.uintName`，其值继续使用 `resourceKey`，不能换成中文显示名。
- `UnitCatalogEntry` 的资源键和显示名必须分离；旧 `UnitName` 调用方必须迁移或获得语义明确的兼容访问器。
- 生成目录是派生产物，源 JSON 是权威；不得手工只改生成目录而不改生成器。
- 迁移后重复运行目录生成必须产生相同内容。
- 不允许为了保留旧字段而让新旧字段同时长期存在，也不允许静默接受拼写错误后使用默认零值。

## 验证要求

### 自动验证

至少增加或调整测试以断言：

1. 两个源 JSON 能按新 schema 解析，且不再包含旧键；
2. `displayNameZhHans=""` 不会被替换为 `resourceKey`；`skillDescriptionZhHans=""` 合法；
3. `innateAbilityIds=[]` 合法，非空能力 ID 能保留；
4. `rarity=1` 和 `rarity=4` 正常，`0`、`7` 被目录校验拒绝；
5. 迁移前后的核心数值、费用、精英化、资源路径、动画和目标价值完全一致；
6. 米/秒到厘米/秒、秒到 20 Hz Tick 的转换结果不变；
7. Player-safe 目录同时包含 resource key、中文显示名字段、中文技能描述字段、lifeDeduct 和 rarity；
8. 待部署快照的 rarity 来自目录，且不由 eliteLevel 推断；
9. 目录生成运行两次结果一致；
10. 现有 BattleInput 固定真实对战仍能加载，确定性摘要若因新增非战斗元数据发生预期变化，必须解释；战斗事件与胜方不得变化。

不得把 0 个测试记为通过。若测试必须读取 Editor 源文件，应放在现有 EditMode 测试边界，不把 `System.IO(Application.dataPath)` 引入 Player 运行时。

### Unity 编译或运行验证

- 执行 Editor 编译；
- 执行目录生成器并确认两个真实单位成功；
- 运行相关 EditMode 测试；
- 若修改 Player-safe loader、Resources 目录或 Player 运行时类型，运行相关 PlayMode 测试；
- 至少加载一次 `local-player-state-v1` 和一次真实固定战斗输入；
- 检查 Console 中没有旧字段缺失导致的默认零值、资源丢失、动画缺失或 JSON 解析异常；
- 如现有安全构建脚本可用，执行 Windows Standalone 构建；无法执行则明确标记未验证。

### 人工检查

1. 打开两个源 JSON，确认字段顺序和命名一致；
2. 对照迁移前基线，确认数值和资源名未变；
3. 确认中文名与技能描述仍为空，没有 Agent 自行填写内容；
4. 确认 `gopro`/`arcslma` 仍作为技术资源键使用，而不是玩家可见中文名；
5. 在 SampleScene 自动初始化后，确认头像、Skeleton、费用和待部署顺序仍可被加载；
6. 若待部署稀有度图标尚未在本任务显示，明确记录为后续 UI-INFO-001，不得宣称视觉完成。

### 无法执行时

- 无法启动 Unity 时，静态 JSON 解析不能替代 Editor/Player 的 `JsonUtility` 验证；
- 无法运行目录生成器时，生成目录标记为未验证，不得手工编造结果；
- 无法构建 Player 时，记录具体阻塞和日志，不得沿用旧构建证明本次 schema 修改通过；
- 用户尚未填写中文内容是预期状态，不属于失败；必须在完成报告中明确列为“等待用户填写”。

## 验收标准

- `UnitJson` 只包含一套规范字段，两个源 JSON 只使用新键且顺序一致；
- 两个 JSON 均包含空的 `displayNameZhHans` 和 `skillDescriptionZhHans`；
- 空技能描述合法；空显示名不会被 resource key 冒充；
- 迁移前后的全部既有单位数值、费用、精英化、能力、资源路径和动画保持一致；
- Player-safe 目录明确区分 resource key 与玩家可见中文名，并包含技能描述、lifeDeduct 和 rarity；
- rarity 只接受 `1～6`；
- `UnitFactory`、能力烘焙和目录生成不再读取旧字段；
- 旧 `UnitTemplate` 序列化字段和现有资源 GUID 未被破坏；
- 待部署或等价只读投影能取得目录 rarity；
- 目录重复生成稳定；
- Editor 无新增编译错误；
- 相关自动测试实际执行且结果可核对，或每一未执行项被明确标为“未验证”；
- SPEC、ARCHITECTURE、TEST_PLAN 只更新与本任务直接相关的内容；
- 最终 diff 没有场景、Prefab、图片、图集、Package、ProjectSettings 或无关代码变化。

## 停止并询问我的条件

- 发现当前两个 JSON 中同一字段的现值与 SPEC、目录生成结果或测试期望实质冲突；
- 必须决定尚未确认的战斗数值、枚举含义或时间换算；
- 需要为单位填写任何中文名称或技能描述；
- 需要提升 `unit-catalog-v1` schema 且会破坏现有 fixture、Player 或测试兼容；
- 需要重命名 `UnitTemplate` 序列化字段、批量重写 ScriptableObject、场景或 Prefab；
- 需要删除/重新生成 `.meta`、更换资源 GUID 或移动 Spine/头像资源；
- 需要升级 Unity/Package、安装第三方 JSON/本地化工具或访问外部服务；
- 工作区已有修改与本任务迁移同一区域且无法安全增量合并；
- 为通过测试必须改变战斗事件、胜方或既有机制。

## 完成报告格式

最后按以下结构报告：

1. 修改文件；
2. 最终源 JSON schema、字段类型和顺序；
3. 旧字段到新字段的迁移结果；
4. 两个单位的迁移前后语义对照；
5. 目录新增字段和兼容处理；
6. UnitFactory、UnitTemplate 与能力烘焙迁移方式；
7. 实际执行的命令和 Unity 操作；
8. Editor 编译、EditMode、PlayMode、构建结果及测试数量；
9. 生成目录的稳定性与真实战斗回归结果；
10. 未验证项；
11. 等待用户填写的中文字段；
12. 所作假设；
13. 遗留风险；
14. 最终 diff 审查；
15. PREP-DEPLOY-001 和 UI-INFO-001 所需的可用接口与输入。
