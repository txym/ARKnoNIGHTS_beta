# TASK-001：真实运行基线与最小条件编译修复

> 状态说明：`docs/PHASE1_TASK_TABLE.md` 已将本任务记为“允许范围内已完成；项目整体非全绿”。本提示词用于保存历史任务边界，并在项目负责人明确要求时补齐尚未验证的基线证据。不得因为重新运行本提示词而重复修改已经完成的修复，或把后续任务提前并入 TASK-001。

> 后置回测说明：新增 `docs/TASK-004A.md` 负责在真实单位 JSON 和 `local-battle-v1` 接入后复测 Editor 编译与 `UnitDeployment.ConvertCoordinate`，但不重新打开本任务，也不提前处理留给 TASK-006 的 `UITest.targetSprite` Standalone 构建债务。

## 角色

你是本 Unity 项目的基线验证与最小风险修复 Agent。

你的职责是复核并补充 Unity Editor 编译、`SampleScene` 运行、Standalone 构建和现有 JSON 加载证据。你不是战斗系统实现 Agent，也不负责清理项目中所有历史编译或 UI 问题。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `ProjectSettings/ProjectVersion.txt`
- `ProjectSettings/EditorBuildSettings.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`（若存在）
- `Assets/Game/Runtime/Deployment/UnitDeployment.cs`
- `Assets/Game/Runtime/Initial/UnitFactory.cs`
- `Assets/Game/Debug/ButtonDebug.cs`
- `Assets/Game/Debug/MoveTest.cs`
- `Assets/Game/Debug/UITest.cs`
- `Assets/Game/UI/UIManager.cs`
- `Assets/Game/UI/DataUISwitch.cs`
- `Assets/GameData/Units/UnitJson.cs`
- `Assets/GameData/Units/Json/arcslma.json`
- `Assets/GameData/Units/Json/gopro.json`
- `Assets/Scenes/SampleScene.unity` 中与 InitButton、ButtonTest、FoldButton、UICamera、UnitDeployment、ButtonDebug、MoveTest 和 UITest 有关的文本区域
- 已提供的 TASK-001 首次完成报告及其中引用的日志（若日志仍存在）

开始前必须执行 `git status --short`，识别并保护用户已有修改。不得覆盖、回滚、格式化或顺手提交无关改动。

## 任务背景

首次执行 TASK-001 已报告：

- 找到 Unity `2022.3.62f1c1`；
- `UnitDeployment.ConvertCoordinate` 在非 Editor 编译中缺失的问题已复现；
- 已把不依赖 `UnityEditor` 的 `ConvertCoordinate` 移出 `#if UNITY_EDITOR`，坐标公式和签名未变，Gizmos 仍为 Editor 专用；
- 修复后 Editor batchmode 编译返回码为 0；
- Windows Standalone 构建仍失败，但 `ConvertCoordinate` 错误已经消失；
- 剩余四条诊断来自 `UITest.cs` 中同一个 `targetSprite` 条件编译作用域问题；
- 当前没有第一方自动化测试；
- 两个源 JSON 只完成静态语法解析，Editor/Standalone 运行时加载尚未验证；
- `SampleScene` 的 Play Mode 人工流程尚未验证；
- 首次报告引用的 `Temp/TASK-001` 日志可能已经不存在，不能假装重新核验过这些日志。

`UITest` 问题属于已登记的 Standalone 构建卫生债务，按当前任务表最迟在 TASK-006 处理。它与多人联机无关，也不是四个独立机制问题。

## 本任务目标

若项目负责人要求继续 TASK-001，本次只允许：

1. 核对现有 `ConvertCoordinate` 修复仍是最小差异且公式未变；
2. 重新取得可保存的 Editor 编译日志和退出码；
3. 在工具能力允许时补验 `SampleScene` 进入、退出及当前原型人工流程；
4. 补验两个单位 JSON 在 Editor 运行时的实际加载；
5. 如确有必要，重新执行一次 Windows Standalone 构建，以确认当前首个阻塞仍是 `UITest.targetSprite`；
6. 记录 Standalone 数据加载仍无法验证的原因；
7. 仅依据本次实际证据，修正 `docs/ARCHITECTURE.md` 和 `docs/TEST_PLAN.md` 中已经过期的基线状态；
8. 输出一份可供 TASK-002 使用的基线补充报告。

本任务不以“所有项目检查全绿”为完成条件，而以“证据真实、状态明确、没有越权修复”为完成条件。

## 已确认规则

- Unity 版本固定为 `2022.3.62f1c1`，不得升级。
- Build Settings 当前只启用 `Assets/Scenes/SampleScene.unity`。
- 战场规则坐标为一基 `9×8`；门格为 `(5,1)` 和 `(5,8)`。
- 当前 `UnitDeployment` 是部署交互原型，不是权威战斗计算。
- 第一阶段不实现准备、商店、完整部署业务、完整回合或多人同步。
- “Player/Standalone”指构建后的独立运行程序，不指多人玩家。
- 未实际执行、日志缺失或结果不完整的验证必须标记为“未验证”。
- 0 个测试不得表述为“测试通过”。

## 不属于本任务的内容

- 不实现 Battle Core、Tick、移动、索敌、阻挡、攻击、伤害、死亡、事件或胜方；
- 不修复 `UITest.targetSprite`；
- 不定义测试对战数据格式；
- 不迁移现有单位 JSON；
- 不重构 `UnitFactory`、`UnitDeployment`、Debug 或 UI；
- 不创建 asmdef、自动化测试程序集或构建基础设施；
- 不修改场景、Prefab、ScriptableObject、Package 或项目设置；
- 不升级 Unity、Spine、渲染管线或输入系统；
- 不安装第三方工具、Package、插件、Skill 或 MCP；
- 不为后续多人联机建立抽象。

## 预计影响文件或目录

默认不应修改业务代码。

允许的文档修改仅限：

- `docs/ARCHITECTURE.md` 中与 TASK-001 已取得真实编译/构建证据直接冲突的状态描述；
- `docs/TEST_PLAN.md` 中当前验证基线和已知风险状态。

`Assets/Game/Runtime/Deployment/UnitDeployment.cs` 已有修复应视为用户工作区中的既有修改，只审查，不重复改写。不得修改 `UITest.cs`。

日志和构建输出只能放在 `Temp/`、`Logs/`、`Build/`、`Builds/` 或其他已忽略生成目录，不得提交，也不得放入 `Assets/`。

## 实施要求

1. 检查工作区、Unity 进程和项目锁；同一路径只能有一个 Unity 实例。
2. 对照首次报告审查 `UnitDeployment.cs`：
   - `ConvertCoordinate` 在 Editor 和非 Editor 中都可见；
   - `OnDrawGizmosSelected` 仍只在 Editor 中编译；
   - 公式仍为 `floor((value + 50) / 100)`；
   - 没有无关格式化。
3. 若原日志缺失，明确写“原日志未重新核验”，并使用新目录保存本次日志。
4. 使用已安装的 Unity `2022.3.62f1c1` 执行 Editor batchmode 编译，记录完整命令、退出码、日志和关键错误检索结果。
5. 若能可靠操作 Play Mode，按人工清单补验 `SampleScene`；若不能，保持“未验证”，不要用静态场景文本替代运行证据。
6. 补验 Editor 运行时的 JSON 加载时，不改变 `UnitFactory` 路径或数据结构。
7. 如重新执行 Standalone 构建：
   - 使用当前已有 Windows 构建支持；
   - 不安装新模块；
   - 失败时定位首个有效根因；
   - 若仍是 `UITest.targetSprite`，只记录并交给 TASK-006；
   - 不为了全绿扩大修改范围。
8. 只把本次实际获得的状态同步到 ARCHITECTURE/TEST_PLAN；不得把未运行项改成通过。
9. 最后执行 `git status --short`、差异审查和空白检查，确认没有意外资源变化。

## 兼容与迁移要求

- 保留 `UnitFactory.SpawnAll`、InitButton 和现有调试入口；
- 保留 `UnitTemplate`、`UnitIdentity`、`UnitSkelBase` 的公开接口；
- 保留 `UnitDeployment.ConvertCoordinate` 的签名和公式；
- 不修改场景 UnityEvent；
- 不移动 JSON、Prefab 或 Spine 资源；
- 不迁移现有 Assembly-CSharp 脚本；
- 不生成、删除或重新创建 `.meta`。

## 验证要求

### 自动验证

至少记录：

- `git status --short`；
- Editor 编译命令、退出码和日志；
- 如执行 Standalone 构建，记录命令、目标、退出码和日志；
- 错误检索结果；
- 最终差异和空白检查。

### Unity 编译或运行验证

分别报告：

- Editor 编译：通过、失败或未验证；
- SampleScene 加载/退出：通过、失败或未验证；
- Windows Standalone 构建：通过、失败或未验证；
- JSON Editor 运行时加载：通过、失败或未验证；
- JSON Standalone 运行时加载：通过、失败或未验证。

### 人工检查

若可操作 GUI，依次检查：打开 SampleScene、进入 Play、记录 Console、点击 InitButton、确认两个单位类型和单位 UI、切换 FoldButton、拖至普通格/门格/场外、点击 ButtonTest、退出 Play、复查 Console。

### 无法执行时

写明未执行步骤、原因、已有静态证据、所需人工操作，以及为什么不能声明通过。

## 验收标准

- `ConvertCoordinate` 修复保持最小且公式不变；
- 本次实际执行的验证均有命令、退出码和日志；
- `UITest` 等范围外阻塞被准确登记而未被越权修复；
- 未验证项没有伪装为通过；
- 如更新 ARCHITECTURE/TEST_PLAN，内容与新证据一致；
- 没有业务功能、场景、Prefab、Package、项目设置或 `.meta` 新改动；
- TASK-002 能据此知道 Editor 是否可用以及当前构建债务，但不被 Standalone 失败阻塞。

## 停止并询问我的条件

- 需要安装/升级 Unity、构建模块或第三方工具；
- 项目正被另一个 Unity 进程占用；
- 许可证阻止可靠验证；
- 需要修改 `UITest` 或其他业务脚本才能继续；
- 需要迁移 JSON 或改变加载架构；
- 需要修改场景、Prefab、Package 或项目设置；
- 发现用户已有修改与本任务文档更新冲突；
- 连续三次有实质差异的尝试仍无法推进。

## 完成报告格式

1. 修改文件；
2. 审查或补验内容；
3. 实际运行的命令/Unity 操作；
4. Editor 编译结果；
5. SampleScene 结果；
6. Standalone 构建结果；
7. JSON Editor/Standalone 加载结果；
8. 自动化测试数量和状态；
9. 未验证项及原因；
10. 所作假设；
11. 遗留风险；
12. 最终工作区状态；
13. TASK-002 所需输入。
