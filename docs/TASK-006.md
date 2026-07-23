# TASK-006：第一阶段端到端验收与 Standalone 构建收口

## 角色

你是本 Unity 项目第一阶段的集成验证、构建卫生和最终审查 Agent。

你的职责是用真实证据验证“测试数据 → 战斗输入 → 确定性计算 → 结构化事件 → Unity 播放 → 胜方/诊断输出”的完整闭环，最小修复已复现的集成或 Standalone 构建阻塞，并形成可重复的最终验收报告。

本任务不是继续开发新机制。任何修复都必须服务于已完成闭环的编译、加载、播放、构建或验证；遇到需求缺口不得用临时规则制造通过。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `docs/TASK-001.md` 至 `docs/TASK-005.md`，包含插入的 `docs/TASK-004A.md`
- `docs/decisions/`（若存在）
- TASK-001～005（含 TASK-004A）的全部完成报告、测试 XML、Editor/PlayMode/构建日志和遗留风险
- `ProjectSettings/ProjectVersion.txt`
- `ProjectSettings/EditorBuildSettings.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`（若存在）
- TASK-002～005 实际新增/修改的 Core、合成 fixture、真实单位目录、`local-battle-v1`、Infrastructure、Presentation、Demo、测试和场景文件
- `Assets/Game/Debug/UITest.cs`
- `Assets/Game/Runtime/Deployment/UnitDeployment.cs`
- `Assets/Game/Runtime/Initial/UnitFactory.cs`
- `Assets/Scenes/SampleScene.unity` 或 TASK-005 确认的 Demo 场景

开始前执行 `git status --short`，识别所有用户改动和前置任务改动。确认没有其他 Unity 进程或项目锁。先整理一张前置验收矩阵：每项是通过、失败、未验证还是证据过期；不要直接重新运行所有命令而忽略已有有效证据。

## 任务背景

TASK-001 已确认：

- `ConvertCoordinate` 非 Editor 条件编译问题已经最小修复；
- Editor batchmode 编译曾经成功；
- Windows Standalone 构建仍被 `UITest.targetSprite` 阻塞；
- 该文件在 Editor 分支内声明变量，非 Editor 分支和后续代码使用该变量，因此一个根因产生四处诊断。

TASK-002～005 应已经交付纯 Core、合成算法回归 fixture、真实 `unit-catalog-v1`、固定 `local-battle-v1` 玩家对战快照、确定性战斗、事件、双视角真实单位表现和场景 Demo。本任务需要验证这些交付物在同一工作区和 Windows Standalone 中真正闭环。

现有 `UnitFactory` 的旧源 JSON 路径仍可能不适用于 Player，但第一阶段 Demo 必须使用 TASK-004A 生成/加载的 Player-safe `unit-catalog-v1` 和 `local-battle-v1`。源单位 JSON 是目录事实来源，Player 不得直接依赖 `Application.dataPath/GameData/...`；旧按钮的旧加载风险可以单独记录。

## 本任务目标

1. 复核所有前置任务的实际文件、接口、测试和未验证项。
2. 先取得当前 Editor 编译和全部相关 EditMode/PlayMode 测试基线。
3. 实际复现当前 Windows Standalone 首个编译/构建阻塞。
4. 若仍是 `UITest.targetSprite`，进行最小作用域修复并复测 Editor、测试和相同构建。
5. 确认 Player-safe `unit-catalog-v1` 和固定 `local-battle-v1` 在 Editor/Windows Standalone 中均能加载并连接，不依赖源码目录布局。
6. 在 Editor 和 Standalone 中运行同一真实对战输入，比较 input digest、event digest、final-state digest、winner/reason。
7. 相同真实对战输入完整计算至少 10 次，结果逐次一致。
8. 分别以 Home/Away 视角播放同一结果，并至少重播一次，确认 winner、存活状态和源事件不变。
9. 检查 Demo 正常结束、错误路径、清理、场景重载和日志。
10. 如缺少可重复的命令行构建/验收入口，使用仓库内最小 Editor 构建方法或项目脚本固化；不引入外部工具。
11. 更新 ARCHITECTURE、TEST_PLAN 和任务表状态，使其反映真实最终证据。
12. 输出第一阶段最终验收报告，明确通过、失败、未验证和后续阶段内容。

## 已确认规则

- Windows Standalone/Unity Player 是独立运行程序，不是多人联机。
- 第一阶段验收只覆盖固定本地数据驱动的单场战斗 Demo。
- 权威计算固定 20 TPS，与 Unity 物理、渲染帧率和 Spine 状态解耦。
- 同一输入必须产生同一事件、最终状态和 winner/reason。
- Home/Away 观察者共享同一权威结果，Away 只转换演示坐标和方向。
- 演示速度、暂停、重播和帧率不能改变权威摘要。
- 0 个测试、日志缺失、许可证失败、工具不可用、超时或无法操作 GUI 都不能记为通过。
- `maxTicks`、双方同时全灭或不支持机制不能伪造胜方。
- 本任务允许最小修复已复现的 `UITest.targetSprite` 条件编译作用域，但不允许重构 Debug/UI。

## 不属于本任务的内容

- 不新增移动、索敌、阻挡、攻击、伤害、技能、Buff、治疗、护盾、远程或胜负规则；
- 不实现准备、商店、部署、完整回合、玩家扣血、四人房间、局域网同步或断线重连；
- 不为了测试通过降低断言、改变合成 fixture/真实输入预期、吞异常或硬编码 winner；
- 不整体重构 Core、Presentation、Demo、UnitFactory、Debug 或 UI；
- 不整体重做场景、Prefab、美术或 Spine；
- 不升级 Unity、Package、Spine、渲染管线或 Input System；
- 不安装第三方测试、构建、截图、自动点击工具、插件、Skill 或 MCP，除非另行触发 `AGENTS.md` 审批流程并获准；
- 不发布构建、推送远程或访问外部服务。

## 预计影响文件或目录

允许的候选修改：

- `Assets/Game/Debug/UITest.cs`：仅限已复现的 `targetSprite` 条件编译作用域最小修复；
- 前置任务实际新增的 Core/Infrastructure/Presentation/Demo 文件：仅限已定位的集成缺陷；
- `Assets/Game/Editor/` 下的最小项目构建/验收入口（若当前不存在可靠命令行入口）；
- `scripts/` 下的日志、XML、摘要比较或构建辅助脚本（若确有重复验证需要）；
- 相关自动化测试；
- `docs/ARCHITECTURE.md`、`docs/TEST_PLAN.md`、`docs/PHASE1_TASK_TABLE.md`；
- 新文件对应的 `.meta`。

构建输出、Player、日志、截图、XML 临时副本和报告原始数据放在忽略/生成目录，不提交。原则上不修改 SampleScene、Prefab、Package 或 ProjectSettings；若构建入口必须修改 Build Settings，停止报告。

## 实施要求

### A. 建立最终基线

1. 汇总 TASK-001～005（含 TASK-004A）的实际测试数、失败项、未验证项和日志是否仍存在。
2. 检查项目固定 Unity 版本和 Windows 构建模块是否已存在；不得自行安装模块。
3. 串行执行 Editor 编译、EditMode、PlayMode；每一步保存独立日志、退出码和结果 XML。
4. 任何失败先定位首个有效根因；同一问题最多进行三次有实质差异的尝试。

### B. 最小修复 `UITest`

5. 实际执行 Windows Standalone 构建，确认当前错误仍来自 `targetSprite`。
6. 若确认，最小修复应保持两分支原加载方式不变：在条件编译块外声明局部 `Sprite targetSprite`，Editor/非 Editor 分支只负责赋值；不修改路径、资源、UI 逻辑或方法签名。
7. 修复后重新执行 Editor 编译、相关测试和同一 Windows Standalone 构建，确认四处诊断消失。
8. 若出现新的首个阻塞，判断是否是前置任务集成缺陷。与第一阶段无关或需要跨模块重构时停止并报告，不连续清理所有历史债务。

### C. 可重复构建和运行入口

9. 优先使用仓库已有构建方法。若没有，可新增最小、项目专用的 Editor build method：
   - 固定读取 Build Settings 已启用场景；
   - 明确 Windows x86_64 目标；
   - 输出路径由参数/环境或安全默认生成目录提供；
   - 构建失败返回可靠进程退出码；
   - 不修改持久 ProjectSettings；
   - 记录 `BuildReport` 摘要。
10. 如需要自动验收 Player，可为 Demo 增加只在明确命令行参数下启用的 autorun/auto-exit 模式：加载真实单位目录和固定玩家对战快照、计算、输出规范摘要、等待必要播放或使用结构化快速验证、写日志并退出。正常手动 Demo 行为不得改变。
11. 仓库脚本只解析日志/XML/摘要或编排现有 Unity 命令；不静默下载或执行外部代码。

### D. 端到端与确定性

12. 在 Editor 中对同一真实对战输入完整计算至少 10 次，逐项比较 input/event/final-state digest 和 winner/reason。
13. 在 Windows Standalone 中运行同一真实对战输入，输出相同格式摘要；比较 Editor 与 Player。
14. 摘要算法必须来自 Core 的稳定规范，不能用运行时默认 HashCode。
15. 运行一条完整播放：player snapshots + real unit catalog → input → calculation → events → real unit views → BattleEnded/winner。
16. Home/Away 各播放一次；确认源 event digest、winner 和存活状态一致。
17. Replay 至少一次；确认没有重复对象、订阅、协程或变化的摘要。
18. 调整表现速度或帧率时，只比较权威最终结果和视图最终状态，不以视觉流畅度替代内部断言。

### E. 数据、日志和错误路径

19. 验证 `unit-catalog-v1` 与 `local-battle-v1` 在 Player 中实际存在、连接并加载成功；路径不能依赖源码目录仍位于 `Application.dataPath/GameData/...`。
20. 明确区分旧 `UnitFactory` 源 JSON 风险与新 Demo 的 Player-safe 目录/对战输入。若旧按钮在 Player 中失败但不影响新 Demo，记录为旧原型遗留，不伪装为新闭环失败，也不顺手迁移。
21. 检查 Editor、测试、构建和 Player 日志中的编译错误、未处理异常、资源加载失败、重复订阅和空引用。
22. 错误单位目录、未知 type ID 或错误对战快照必须进入 Error/结构化失败，不崩溃、不空播放、不显示默认 winner。

### F. 最终审查和文档

23. 审查所有最终差异：程序集方向、Core 禁止依赖、事件不可变、表现清理、场景引用、条件编译和测试缺口。
24. 检查 Unity 运行没有产生意外场景、Prefab、ProjectSettings、Package 或旧 `.meta` 修改。
25. 更新 ARCHITECTURE 为最终真实架构；更新 TEST_PLAN 的实际命令、测试数和状态；更新任务表状态，但不得把未验证项标为完成。
26. 形成最终验收矩阵，对 SPEC 当前阶段每条验收原则逐项给出证据。

## 兼容与迁移要求

- 保留 `UnitDeployment.ConvertCoordinate` 已完成修复和坐标公式。
- `UITest` 只调整局部变量作用域，保留 Editor `AssetDatabase` 和 Player `Resources.Load` 两分支行为。
- 保留旧 InitButton、ButtonTest、FoldButton、UnitFactory、UnitSkel 和部署原型公共入口。
- 不改变 BattleInput/事件 schema；如确需修复，保持版本和迁移兼容并说明原因。
- 正常 Demo 与自动验收模式互不影响；自动模式必须显式启用。
- 不改变场景/Prefab/Spine GUID，不删除或重新生成现有 `.meta`。
- 不提交生成目录、构建产物或临时日志。

## 验证要求

### 编译验证

- Unity Editor 编译成功；
- Battle Core、Infrastructure、Presentation、Demo 和测试程序集无错误；
- 非 Editor/Windows Standalone 编译成功；
- 日志中没有本次改动相关的未处理异常。

### EditMode 测试

- 运行全部第一阶段相关 EditMode 测试；
- XML 可解析，测试数大于 0，失败数为 0；
- 包含坐标/输入、runner、完整战斗、事件、视角投影和控制器的相关断言；
- 10 次重复性结果一致。

### PlayMode 测试

- 运行全部第一阶段相关 PlayMode 测试；
- XML 可解析，测试数大于 0，失败数为 0；
- 覆盖最小回放、完成状态、最终视图、暂停/重放和清理；
- 无测试时必须写未验证，不能写通过。

### 构建验证

- 使用 Unity `2022.3.62f1c1` 构建 Windows x86_64；
- 记录实际命令、场景、输出、退出码、构建摘要和日志；
- 构建产物不进入版本控制；
- 启动 Player 并记录 Player 日志；
- 真实单位目录/对战快照加载连接、结果摘要和 `gopro`/`arcslma` 关键资源无错误。

### 人工游戏流程验证

1. 打开 Demo 场景并进入 Play；
2. 一键运行固定战斗；
3. 记录输入/事件/最终摘要和 winner；
4. 暂停、继续、调速、重放；
5. Home/Away 各完整播放；
6. 核对两视角 winner/存活一致；
7. 检查攻击、受击、死亡和缺失动画降级；
8. 退出/重进场景，确认无残留；
9. 在 Standalone 中重复核心流程；
10. 检查 Console/Player 日志。

### 当前仍可能无法自动验证

- 动画流畅度、视觉节奏、朝向观感和 UI 可读性；
- 无可靠 GUI 控制时的完整人工按钮序列；
- 尚未实现且不在第一阶段的多人、准备、商店和回合内容；
- 未确认的多单位阻挡竞争、同时全灭正式规则和异常属性。

以上项目必须明确写“人工通过/失败/未验证”或“后续阶段”，不能用自动摘要代替。

## 验收标准

- `UITest.targetSprite` 四处 Standalone 编译诊断经实际复现后被一个最小作用域修复消除；
- Editor 编译和 Windows Standalone 构建均有成功退出码和完整日志；
- 相关 EditMode、PlayMode 测试结果 XML 可解析、测试数大于 0、失败为 0；
- 同一真实对战输入运行 10 次得到相同 input/event/final-state digest 和 winner/reason；
- Editor 与 Windows Player 的摘要一致；
- Player 实际加载 `unit-catalog-v1` 与 `local-battle-v1`，不依赖源码 `Assets/GameData` 布局；
- Home/Away 播放同一结果，winner 和存活状态一致；
- Replay 不改变结果且无对象/协程/订阅残留；
- Demo 错误路径有结构化诊断，无未处理异常或默认 winner；
- 没有新增第一阶段范围外功能或未确认机制；
- 最终 diff 无无关重构、意外资源变化和用户修改丢失；
- ARCHITECTURE、TEST_PLAN、任务表和最终验收报告反映真实证据；
- 任何未执行项都明确标记为未验证。

若环境确实无法完成 Player 启动或人工 GUI，本任务可以交付“构建成功但运行/人工未验证”的真实报告，但不得宣称第一阶段完整验收通过。

## 停止并询问我的条件

- 需要安装 Windows 构建模块、升级 Unity/Package 或修改目标平台设置；
- `UITest` 之外出现范围无关且需要跨模块重构的构建错误；
- 修复需要改变游戏机制、合成 fixture/真实输入预期、winner 或事件顺序；
- 需要修改/重建场景、Prefab、Spine、ScriptableObject 或大批 `.meta`；
- Editor 与 Player 结果不一致且根因涉及未确认数值/同 Tick 规则；
- 需要第三方工具、二进制、插件、Skill、MCP、管理员权限或外部服务；
- 许可证、硬件或项目占用阻止可靠验证；
- 发现用户修改与集成修复重叠且无法安全保留；
- 连续三次有实质差异的尝试仍无法推进。

## 完成报告格式

1. 完成内容；
2. 修改文件；
3. 最小构建/集成修复；
4. 实际执行的所有命令和 Unity 操作；
5. Editor 编译结果和日志；
6. EditMode 测试总数/通过/失败/忽略及 XML；
7. PlayMode 测试总数/通过/失败/忽略及 XML；
8. Windows Standalone 构建目标、退出码、报告和日志；
9. Player 启动、真实单位目录/对战快照加载连接和摘要结果；
10. 10 次重复性及 Editor/Player 对比；
11. Home/Away、暂停、速度、重播和清理结果；
12. 人工检查和截图（若有）；
13. 未验证项及原因；
14. 所作假设；
15. 遗留风险和后续阶段内容；
16. 工具/脚本/Package 变化及回滚方式；
17. 最终工作区和 diff 审查；
18. 第一阶段逐项验收结论：通过、失败或未验证。
