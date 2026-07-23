# TASK-002：Battle Core 基础、不可变输入、固定 fixture 与 Tick 骨架

> 历史状态与后置回测：本任务的纯 Core 基础、`battle-fixture-v1` 和对应自动测试已经存在。合成 fixture 继续用于算法回归；新增 `docs/TASK-004A.md` 将另行接入真实单位目录与 `local-battle-v1` 玩家快照，并只在真实数据暴露通用契约缺陷时最小修正本任务产物。不得把本提示词重新执行为另一套并行 Core。

## 角色

你是本 Unity 项目的战斗领域基础层实现 Agent。

你的职责是一次建立后续战斗计算所需的稳定基础契约：纯 C# 程序集边界、逻辑坐标、主客场映射、单位定义与实例快照、固定测试对战文件、输入验证、运行时初始状态和 20 TPS 的确定性 Tick runner 骨架。

本任务不实现移动、索敌、阻挡、攻击或伤害；但完成后，TASK-003 不应再为了表达输入或推进 Tick 而重做基础模型。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `docs/TASK-001.md`
- `docs/decisions/`（若存在）
- `ProjectSettings/ProjectVersion.txt`
- `ProjectSettings/EditorBuildSettings.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`（若存在）
- `Assets/GameData/Units/UnitJson.cs`
- `Assets/GameData/Units/Json/arcslma.json`
- `Assets/GameData/Units/Json/gopro.json`
- `Assets/Game/Runtime/Data/Unit/UnitTemplate.cs`
- `Assets/Game/Runtime/Data/Unit/UnitIdentity.cs`
- `Assets/Game/Runtime/Data/Player/PlayerUnitCollection.cs`
- `Assets/Game/Runtime/Initial/UnitFactory.cs`
- `Assets/Game/Runtime/Deployment/UnitDeployment.cs`
- `Assets/Scenes/SampleScene.unity` 中地图和 `Block(x,z)` 的文本区域
- TASK-001 完成/补充报告和实际验证日志（若存在）

开始前执行 `git status --short`，保护现有 `AGENTS.md`、`UnitDeployment.cs` 和文档修改。检查是否有 Unity 进程或项目锁；不得与另一个 Unity/batchmode 进程同时打开同一路径。

## 任务背景

当前项目没有正式战斗计算层。第一方代码都在 `Assembly-CSharp`，`UnitTemplate` 是 Unity `ScriptableObject`，`UnitFactory` 直接从 `Application.dataPath` 读取单位 JSON 并创建表现对象；这些都不能成为确定性 Core 的权威依赖。

现有 `PlayerUnitCollection` 是孤立的零基 `9×4` 数据结构，固定容量和区域模型尚未与 SPEC 对齐，不能直接作为新战斗输入。现有两个单位 JSON 也缺少当前战斗输入需要的独立伤害类型和攻击动画时长。

TASK-001 已证明 Editor 编译可用；Standalone 仍被 `UITest.targetSprite` 阻塞，但该问题不阻塞新 Core、测试程序集或 EditMode 验证。本任务不得顺手修复它。

`docs/SPEC.md` 的若干“后续补充”文字可能落后于项目负责人后续确认和 `PHASE1_TASK_TABLE.md`。本提示词“已确认规则”属于当前任务的明确说明；实现前应只同步与本任务直接相关的 SPEC、ARCHITECTURE 和 TEST_PLAN 段落，不得静默保留互相冲突的事实源。

## 本任务目标

1. 建立独立的 Battle Core 运行时 asmdef，不依赖 UnityEngine、Spine、UI、Physics、场景或 Assembly-CSharp。
2. 建立最小 EditMode 测试 asmdef，优先复用仓库已存在的 Unity Test Framework。
3. 表达不可变的本地 `9×4` 阵型坐标、完整 `9×8` 战场坐标和确定性逻辑位置。
4. 实现门格、部署合法性、主场直接映射、客场 180 度映射和战场坐标双旋转。
5. 定义第一阶段不可变战斗输入：配置、双方玩家快照、主客场关系、单位类型定义、单位实例、所在区域、本地阵型位置和 Buff 占位数据。
6. 只把处于部署区的单位映射到本场战斗初始状态；待部署区和商店记录仍保留在输入快照中但不参战。
7. 定义当前计算所需字段，包括 HP、攻击、防御、法抗、移动速度、攻击间隔、攻击动画时长、伤害类型、攻击方式和阻挡容量。
8. 定义版本化的固定测试对战 JSON，并提供从 JSON 文本到经过验证的 `BattleInput` 的加载/转换入口。
9. 选择最小、可在后续 Standalone 中包含的 fixture 载体；推荐使用 `Resources` 中的 `TextAsset` 或等价的 Player-safe 项目资源，不继续依赖 `Application.dataPath` 下的源文件布局。
10. 建立运行时单位初始状态和 20 TPS runner 骨架，支持单 Tick、运行到 `maxTicks`、稳定摘要和明确的未解决结果。
11. 使用固定输入证明相同输入重复加载和重复运行得到一致摘要。

类型和目录名称可以在调查后调整，但必须清楚区分 Core、Unity 文件加载适配和测试代码，不得让 Core 为了读取资源而依赖 UnityEngine。

## 已确认规则

### 坐标与阵营

- 战场为一基 `9×8`，`x=1..9`、`y=1..8`。
- 本地阵型为一基 `9×4`，`x=1..9`、`y=1..4`。
- 蓝方半场为 `y=1..4`，红方半场为 `y=5..8`。
- 蓝门为 `(5,1)`，红门为 `(5,8)`；两者是有效战场坐标但不可部署。
- 主场方本地阵型直接映射到蓝方；客场方使用 `(x,y) → (10-x,9-y)` 映射到红方。
- 阵型缓存不因映射而改变；旋转两次恢复原坐标。
- Home/Blue 和 Away/Red 是本场战斗计算中的阵营，不是联网抽象。

### 时间、距离与数据

- Tick 即逻辑帧，固定 `20 TPS`，一个 Tick 为 `0.05` 秒。
- `1 格 = 1 米 = 100 Unity 世界坐标单位`；Core 不使用 Unity 世界坐标。
- `moveSpeed` 语义为米/秒。
- `attackInterval` 语义为两次攻击动画开始之间的最短秒数。
- 单位定义必须具有独立 `damageType`；当前现有单位先按物理伤害。
- `attackMethod` 只表示近战/远程，不能复用为伤害类型。
- 第一阶段阻挡容量默认 1。
- 单位具有独立 unit ID；类型数据按 type ID 共享。
- 测试输入保留部署区、待部署区和商店记录；只有部署区单位参战。
- Buff 在本阶段只作为不可变输入占位保留，不实现效果或比较语义。
- `maxTicks` 只能产生明确的未解决/超限诊断，不能伪造胜方。

### 尚未固化的边界

- 秒数无法整除 `0.05` 秒时的 Tick 取整规则尚未确认。第一阶段正式 fixture 必须使用可精确表示为整数 Tick 的攻击间隔和攻击动画时长；不要借本任务决定取整规则。
- 现有 JSON 中没有真实攻击动画时长。可以使用明确标记为“测试专用”的合成 fixture 数值，但不能把它声称为现有单位的真实动画数据。
- fixture 的长期发布格式尚未决定；本任务只需要一个带 schemaVersion、可验证、Player-safe 且可替换的第一阶段格式。

## 不属于本任务的内容

- 不实现移动、索敌、阻挡关系、攻击、伤害、HP 扣减、死亡、胜方或公开战斗事件；
- 不实现 Unity 表现对象、世界坐标、动画、协程或场景控制器；
- 不修改 SampleScene、Prefab 或 Spine 资源；
- 不迁移现有 `UnitFactory`、`UnitTemplate`、`UnitIdentity`、`UnitSkelBase` 或 `PlayerUnitCollection` 到新程序集；
- 不替换现有初始化/部署原型；
- 不修复 `UITest.targetSprite`；
- 不实现 Buff、技能、治疗、护盾、远程单位、准备、商店、部署业务或多人同步；
- 不建立通用 ECS、网络序列化框架、依赖注入框架或未来联网抽象；
- 不安装第三方 JSON、数学或测试库；工具与官方 Test Framework 使用必须遵循 `AGENTS.md`。

## 预计影响文件或目录

候选范围：

- `Assets/Game/Battle/Core/`
- `Assets/Game/Battle/Core/Coordinates/`
- `Assets/Game/Battle/Core/Input/`
- `Assets/Game/Battle/Core/Simulation/`
- `Assets/Game/Battle/Infrastructure/` 或等价的 Unity fixture 适配区域
- `Assets/Game/Tests/EditMode/Battle/`
- `Assets/Resources/BattleFixtures/`、`Assets/StreamingAssets/` 或调查后选定的单一 Player-safe fixture 目录
- 与新增文件对应的 `.meta`
- 与已确认规则和验证基线直接相关的 `docs/SPEC.md`、`docs/ARCHITECTURE.md`、`docs/TEST_PLAN.md`

允许新增一个纯 Core asmdef、必要的 Unity fixture 适配 asmdef和一个 EditMode 测试 asmdef。若现有 Test Framework 无法被测试程序集引用，先按 `AGENTS.md` 检查已有包；只有确有必要时才使用已预授权的 Unity 官方 Test Framework，并记录 manifest/lock 变化，不得引入第三方依赖。

不得修改场景、Prefab、现有 ScriptableObject、现有 Spine asmdef、`UITest.cs` 或现有部署/UI 业务代码。

## 实施要求

### A. 程序集和坐标

1. 建立仅包含新增 Core 的纯 C# asmdef，禁用或避免 UnityEngine 引用。
2. 不迁移现有 Assembly-CSharp 类型；旧代码以后通过适配器调用 Core。
3. 使用含义明确的不可变值类型表示本地阵型坐标、战场格坐标和连续逻辑位置；非法输入必须返回结构化错误或显式失败，不能静默钳制。
4. 分开表达“在战场范围内”“是门格”“可部署”。
5. 实现无副作用映射和旋转；不得修改源对象。
6. 连续逻辑位置使用文档化的整数定点尺度；尺度必须能精确表示 `0.25` 米和正式 fixture 的每 Tick 位移。不得在权威状态中使用 `Vector2/Vector3/Mathf`。

### B. 输入模型与验证

7. 输入至少表达：
   - schema/fixture 版本和 battle ID；
   - `maxTicks` 等战斗配置；
   - Home/Away 玩家标识和主客场关系；
   - 单位类型定义；
   - 双方完整单位快照；
   - unit ID、type ID、区域、部署坐标、Buff 占位；
   - 当前任务目标中列出的战斗数值。
8. 输入对象构建完成后不可变；不得把可变 JSON DTO、Unity 对象或外部列表直接暴露给 Core。
9. 验证至少拒绝：未知 schema、重复 unit ID、重复 type ID、未知 type ID、非法区域、部署单位缺少坐标、非部署单位携带部署坐标、非法/门格坐标、Home/Away 玩家缺失或重复、无效数值以及不满足整数 Tick 约束的时间字段。
10. Buff 只保存原始、不可变、可比较的占位数据；不执行 Buff 行为。
11. 只有 Deployed 单位生成运行时战斗状态；Staging/Shop 保留在输入摘要中。

### C. fixture 与加载边界

12. JSON DTO 与 Core 模型分离。若使用 `JsonUtility` 或 `Resources.Load<TextAsset>`，它们只能位于 Unity 适配层。
13. fixture 必须带 `schemaVersion`，字段顺序不应影响语义；未知版本必须明确失败。
14. 至少提供：
   - 一份合法、无歧义的最小双方 fixture；
   - 一份或用内存变体覆盖非法输入测试。
15. fixture 中的攻击间隔和动画时长使用整数 Tick 可精确表达的值；合成数据必须有测试标识。
16. 不复用 `Application.dataPath/GameData/Units/Json` 作为新 Demo 的唯一运行时路径。

### D. Tick runner 骨架

17. 建立纯 C# runner，权威推进只能由显式 `Step()`/等价调用驱动，不使用 `MonoBehaviour.Update`、`Time.deltaTime` 或协程。
18. runner 至少维护当前 Tick、不可变输入引用、运行时单位初始状态、运行状态和停止原因。
19. 为 TASK-003 预留清晰的阶段边界或内部 transition sink，但本任务不公开最终事件 DTO，也不创建空壳战斗行为。
20. `RunToCompletion`/等价入口必须在 `maxTicks` 停止并返回未解决诊断；无战斗规则时不能声称任何一方获胜。
21. 稳定摘要不能依赖进程随机化的 `GetHashCode()`；使用规范化字段顺序和明确的稳定摘要/哈希方案。

### E. 文档

22. 先同步 SPEC 中与本任务直接相关且已由项目负责人确认的 20 TPS、单位字段和测试输入规则；保留真正未确认项。
23. 更新 ARCHITECTURE，准确描述新增 Core/fixture 边界，不把 TASK-003 行为写成已实现。
24. 更新 TEST_PLAN，记录新增测试程序集、实际命令和结果；未运行项仍写未验证。

## 兼容与迁移要求

- `PlayerUnitCollection` 保持原样，其零基索引不作为 Core 规则来源。
- `UnitDeployment.ConvertCoordinate` 保持现有公式，Core 不依赖它。
- `UnitFactory`、`UnitTemplate`、`UnitIdentity` 和 `UnitSkelBase` 不迁入 Core。
- 现有两个单位 JSON 保持可被旧原型加载；除非任务明确需要且字段值已获得，不要强行加入未知动画时长。
- 新输入加载失败时返回结构化错误，不吞异常，也不回退为默认战斗。
- 不改变 SampleScene 的 UnityEvent 或现有类型的程序集限定名。
- 不移动、删除或重新生成现有 `.meta`；新增资源必须有对应 `.meta`。

## 验证要求

### 自动验证

必须执行 EditMode 测试，至少覆盖：

- `9×4` 和 `9×8` 边界；
- 门格有效但不可部署；
- Home 直接映射、Away 旋转、双旋转和源值不变；
- 非法输入验证矩阵；
- 完整快照中只有 Deployed 单位进入运行时状态；
- 合法 fixture 连续加载两次得到相同规范摘要；
- 相同输入创建 runner 并运行至少 10 次，Tick trace/停止原因/摘要一致；
- `maxTicks` 返回未解决而不是胜方；
- Core 程序集无 UnityEngine 依赖。

测试结果 XML 必须存在且可解析，相关测试数必须大于 0，失败数必须为 0 才能写“通过”。

### Unity 编译或运行验证

- 执行 Unity Editor 编译；
- 执行相关 EditMode 测试；
- 不要求 Standalone 构建全绿，已知 `UITest` 错误不得被误归因于本任务；
- 本任务原则上不需要 Play Mode。

### 人工检查

- asmdef 引用方向正确；
- Core 源码没有 UnityEngine、Spine、UI、Physics、场景或文件路径依赖；
- fixture 在选定 Player-safe 资源目录中且 schemaVersion 清楚；
- 没有场景、Prefab、Package 或旧 `.meta` 意外变化；
- 文档只陈述实际已实现部分。

### 无法执行时

记录实际命令、退出码、日志、测试 XML 是否生成、阻塞原因和未验证项。不得把 0 tests、许可证错误或结果文件缺失记为通过。

## 验收标准

- 存在独立、可编译、无 UnityEngine 依赖的 Battle Core；
- 存在可编译的最小 EditMode 测试程序集；
- 坐标、门格、主客场映射和定点逻辑位置有自动断言；
- 存在版本化固定 fixture 和明确的加载/验证错误；
- `BattleInput`/等价输入在构建后不可变并包含双方完整快照；
- 只有部署单位进入运行时战斗状态；
- runner 由显式 Tick 推进，固定 20 TPS，不读取 Unity 时间或物理；
- `maxTicks` 不伪造胜方；
- 同一输入重复加载和运行得到相同稳定摘要；
- 相关 EditMode 测试数大于 0 且失败为 0；
- Editor 编译无本任务新增错误；
- 没有修改场景、Prefab、现有业务流程或 `UITest`；
- SPEC、ARCHITECTURE 和 TEST_PLAN 与实际交付保持一致。

## 停止并询问我的条件

- SPEC、任务表和当前提示词对同一外部行为存在无法通过文档同步解决的实质冲突；
- 必须决定非整数 Tick 时间的取整方式；
- 必须使用现有单位的真实攻击动画时长，但仓库和用户输入均未提供；
- fixture 必须引入尚未确认的 Buff、平局、阻挡竞争或异常属性规则；
- 最小 Core asmdef 迫使现有业务脚本迁移程序集；
- 需要修改 SampleScene、Prefab、Spine 资源或现有 UnitFactory 加载流程；
- 需要安装第三方 Package、JSON 库、数学库、插件、Skill 或 MCP；
- Test Framework 不可用且无法在 `AGENTS.md` 当前预授权范围内解决；
- 发现用户已有修改与本任务范围重叠且无法安全保留；
- 连续三次有实质差异的尝试仍无法推进。

## 完成报告格式

1. 修改文件；
2. 新增程序集和引用关系；
3. 坐标、输入、fixture 和 runner 骨架实现；
4. fixture schema 与示例摘要；
5. 实际执行命令；
6. Editor 编译结果；
7. EditMode 测试总数、通过数、失败数和 XML/日志位置；
8. 人工架构审查结果；
9. 未验证项；
10. 工具或 Package 变化及回滚方式（若有）；
11. 所作假设；
12. 遗留风险；
13. 最终工作区状态；
14. TASK-003 必须采用的实际类型、字段、runner 阶段和 fixture 路径。
