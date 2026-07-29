# 测试计划

## 1. 目的与适用范围

本文档定义当前 Unity 项目的验证层级、执行证据和后续自动化方向。测试期望以 `docs/SPEC.md` 已确认规则为准；未确认的机制不得通过测试代码被固化为事实。

TASK-006 已于 2026-07-18 以 Unity `2022.3.62f1c1` 复核第一阶段闭环。早期 TASK-001 的 Standalone 失败保留为历史基线；当前状态、实际命令、日志与未验证项以本文末尾的 TASK-006 验收记录为准。

## 2. 当前验证基线

- Unity 版本为 `2022.3.62f1c1`。
- Build Settings 当前只启用 `Assets/Scenes/SampleScene.unity`。
- `packages-lock.json` 中存在 Unity Test Framework 间接依赖，但 `manifest.json` 没有直接声明测试框架。
- Battle Core、Infrastructure、Presentation、Demo 和测试均已有第一方 `.asmdef`；Core 保持 `noEngineReferences`。
- 第一方 EditMode/PlayMode 测试程序集已覆盖固定真实输入、回放、视角和表现桥接。
- `EventTest.cs`、`MoveTest.cs` 和 `UITest.cs` 是场景调试脚本，不是自动化测试。

| 验证层 | 当前状态 |
|---|---|
| 静态文件、场景、程序集与测试清单 | 已完成只读盘点 |
| Editor 导入与脚本编译 | 已验证通过：Unity batchmode 返回码 `0`，未发现 C# 编译错误 |
| EditMode 测试 | 最新动画回归：34 项通过、0 失败、0 跳过；XML 可解析 |
| PlayMode 测试 | 最新动画回归：6 项通过、0 失败、0 跳过；XML 可解析 |
| Player 构建 | TASK-006 实际执行 Windows x86_64 构建成功，退出码 `0`；真实 Player 验收退出码 `0` |
| 人工游戏流程 | 未验证：当前没有可靠的 Unity GUI 自动化能力，未以静态场景文本替代运行证据 |

测试数为 0、测试结果文件缺失、Unity 超时、许可证错误、项目被占用或日志不完整时，结果必须记为“未验证”，不能记为“通过”。

## 3. 通用执行与记录规则

每次验证至少记录：

- 实际 Unity 版本和执行命令；
- 验证日期、代码版本或提交；
- 退出码；
- 测试总数、通过数、失败数和忽略数；
- 失败测试名称及首个有效堆栈；
- 构建目标、输出路径和构建结果；
- Editor、测试、构建和 Player 日志位置；
- 与本次改动相关的新错误、异常或资源加载失败。

同一个项目路径不得同时由多个 Unity Editor 或 batchmode 进程打开。运行前应检查工作区状态，运行后应检查 diff，防止测试或导入过程产生意外资源修改。

## 4. 编译验证

### 4.1 当前状态

TASK-006 的 Editor batchmode 编译与 Windows x86_64 构建均返回 `0`，日志未包含 `error CS`、`Scripts have compiler errors`、`Compilation failed` 或未处理异常。早期 `targetSprite` 的四个 `CS0103` 诊断已由一次局部变量作用域修复消除；Editor 与 Player 两条原加载分支未改变。

### 4.2 验证步骤

1. 使用 `2022.3.62f1c1` 打开项目或执行 batchmode 导入；
2. 等待脚本编译和资源导入完成；
3. 检查 Editor 日志，确认没有 C# 编译错误和未处理异常；
4. 额外执行一次目标 Player 的脚本编译或构建，覆盖条件编译分支；
5. 检查最终 diff，确认没有意外的场景、Prefab、Package、项目设置或 `.meta` 修改。

Editor batchmode 编译模板：

```powershell
& "<UnityEditorPath>\Unity.exe" `
  -batchmode `
  -nographics `
  -quit `
  -projectPath "G:\ARKnoNIGHTS_beta" `
  -logFile "<OutputDirectory>\editor-compile.log"
```

### 4.3 通过标准

- Unity 进程正常退出；
- 日志中没有 `error CS`、`Scripts have compiler errors`、`Compilation failed` 或未处理异常；
- Player 编译没有因 `UNITY_EDITOR` 条件差异产生错误；
- 没有与验证无关的资源变化。

## 5. EditMode 测试

### 5.1 当前状态

最新完整 EditMode 结果 XML 为 34/34 通过、0 失败、0 跳过。覆盖范围包含 Core 输入/runner/事件、真实目录与 `local-battle-v1` 连接、十次确定性、回放控制器、结构化失败诊断，以及阻挡满容量下的攻击演出和暂停动画冻结。

### 5.2 基于现有代码的候选测试

以下测试可覆盖当前已经存在的纯 C# 或低 Unity 依赖逻辑：

1. `BitSet64` 的置位、清除、包含、并集和边界行为；
2. `TagRegistry` 和 `TagMask` 的标签注册、查询及未知标签处理；
3. `PlayerUnitCollection.SetRows` 对空输入、容量上限和重复单位 ID 的处理；
4. `PlayerUnitCollection.ApplyZoneOrder` 对完整集合、重复项、缺失项和跨区域单位的拒绝；
5. `PlayerUnitCollection.SetDeployedPlacementsStrict` 对重复单位、重复格位、越界和失败原子性的处理；
6. 单位 JSON 字段到 `UnitTemplate` 的映射、重复 `typeID` 和无效 JSON 处理，但现有实现包含文件系统与 Unity 对象创建，测试夹具需要谨慎隔离。

`PlayerUnitCollection` 当前没有调用方，并且其零基位置索引、固定容量和区域模型尚未与 SPEC 对齐。相关测试只能证明这段现有代码的行为，不应把这些数值固化为玩家可见规则。

### 5.3 战斗规则测试计划

确定性战斗核心实现后，EditMode 应优先覆盖：

- 一基 `9×4` 本地阵型、`9×8` 战场、两个门格和 180 度旋转；
- 相同输入产生相同事件序列、最终状态和胜方；
- 状态只在逻辑帧变化，并且不读取 Unity Physics 或渲染帧时间作为权威结果；
- 最近敌人索敌、移动、阻挡建立与容量、攻击范围；
- 物理、法术、真实伤害以及 5% 最低伤害；
- 死亡后不再行动和单场胜方输出；
- 主客场视角共享同一战斗结果，视角转换不修改原始阵型或计算状态。

TASK-003 已确认同距索敌、目标死亡后的下一 Tick 重选、同帧批量伤害/统一死亡、伤害向下取整、超时/同时全灭返回 Unresolved，以及多单位阻挡的目标/嘲讽/到门距离/ID 决胜和双向容量。对应 EditMode 测试必须覆盖这些规则；非整数 Tick 时间仍不得作为正式 fixture 的期望。

测试程序集建立后的命令模板：

```powershell
& "<UnityEditorPath>\Unity.exe" `
  -batchmode `
  -nographics `
  -projectPath "G:\ARKnoNIGHTS_beta" `
  -runTests `
  -testPlatform editmode `
  -testResults "<OutputDirectory>\editmode-results.xml" `
  -logFile "<OutputDirectory>\editmode.log"
```

通过时必须同时满足进程成功退出、结果 XML 可解析、相关测试数大于 0、失败数为 0。

## 6. PlayMode 测试

### 6.1 当前状态

最新完整 PlayMode 结果 XML 为 6/6 通过、0 失败、0 跳过。覆盖真实 `SampleScene` Demo、真实 `gopro`/`arcslma` 视图、暂停、Home/Away、Replay 和释放。

### 6.2 基于当前原型的候选测试

- `SampleScene` 能加载和退出，且没有未处理异常；
- 触发初始化后，仓库内两个单位 JSON 能生成对应单位模板、单位对象和单位栏项目；
- 点击单位 UI 能打开详情面板并路由正确的 `Payload`；
- 折叠按钮能切换当前商店面板状态；
- 从单位 UI 拖拽到现有有效格位时生成并吸附单位；
- 拖拽到场外或两个门格时销毁本次克隆；
- 移动调试按钮能让最近记录的单位到达当前脚本定义的终点；
- 退出并重进 Play Mode 后，静态缓存、单例、事件订阅和调试列表不残留旧状态。

这些用例只描述当前原型可观察行为，不证明回合、部署费用、阻挡、伤害或结算已实现。

### 6.3 战斗演示测试计划

计算与演示分离实现后，PlayMode 应验证：

- 演示层读取预先计算的结构化战斗事件，不反向修改计算状态；
- 不同渲染帧率下播放同一事件序列时，最终单位状态和胜方一致；
- 主场直接演示基础坐标，客场只转换坐标和方向；
- 移动、攻击、受击、死亡事件触发对应表现；
- 场景重载不会保留上一场事件、协程或静态订阅。

PlayMode 命令模板：

```powershell
& "<UnityEditorPath>\Unity.exe" `
  -batchmode `
  -nographics `
  -projectPath "G:\ARKnoNIGHTS_beta" `
  -runTests `
  -testPlatform playmode `
  -testResults "<OutputDirectory>\playmode-results.xml" `
  -logFile "<OutputDirectory>\playmode.log"
```

截图或肉眼观察只能作为表现证据，不能替代结构化状态和胜方断言。

### 6.4 UI 自动截图与视觉判断计划

UI-005 应建立一个不依赖人工点击的自动截图入口，用固定本地 fixture 和选择状态构造正式 UI 画面。该能力在实际交付并运行前属于“计划”，不得写成已经可用。

自动截图闭环为：

```text
固定玩家/阶段输入
→ 正式 UI 和场景布局
→ 固定分辨率渲染
→ PNG + 状态/布局 manifest
→ Agent 打开参考图和实拍图
→ 数值关系与视觉问题报告
→ 有实质差异的调整
→ 复拍和最终结论
```

最低截图集合在 `1920×1080` 下包括：

- Preparation、10 槽未选中；
- Preparation、13 槽未选中；
- 13 槽首项、中间项、末项分别被选中；
- 已部署单位选择效果与撤退入口；
- Battle 顶部状态栏和初始敌人击败进度；
- 待部署、已部署、敌方三个来源的单位信息面板。

每张截图必须有同名或可关联的结构化 manifest，至少记录：

- 场景、分辨率、Canvas scale 和截图状态名；
- fixture/schema、阶段、选中 slot/unit；
- 槽顺序、各槽宽度和关键 RectTransform 的屏幕矩形；
- Cost、准备倒计时、敌人数等当前显示值；
- PNG 路径和捕获时间点。

捕获要求：

1. 使用正式布局和绑定代码构造画面，不允许为截图单独硬编码最终位置；
2. 捕获前刷新 Canvas，并等待到明确稳定帧；
3. 退出前确认 PNG 已写完、可解码、尺寸正确且不是全黑、全透明或单色空帧；
4. `-batchmode -nographics` 产生的画面必须先证明有效；否则改用带图形上下文的 Editor 或固定窗口 Standalone Player；
5. 产物写入 `Artifacts/UI-005/Captures/`、`Temp/UI-Captures/` 或其他忽略目录，不写入 `Assets`，不生成截图 `.meta`；
6. 保存实际命令、退出码、日志、PNG 清单和 manifest 清单。

Agent 必须实际打开本地参考图和每张实拍图，并逐状态记录：

- 结论：通过、需调整或无法判断；
- 锚点、尺寸、重叠、越界和层级；
- 头像变形、遮罩裁切和选中槽宽度过渡；
- 文字截断、阴影主体对齐和地图上可读性；
- 参考关系、实际偏差、建议调整和复拍结果。

已确认比例优先用 manifest 与 UI_SPEC 公式判断；并排图、透明叠加和差异热图只用于定位。由于参考图的背景、文字或场景内容可能不同，不以全屏逐像素相等作为唯一通过条件。可归因问题最多进行三轮有实质差异的调整，仍无法收敛时保存证据并停止。

若当前 Agent 已能读取本地 PNG，不需要额外安装。能力不足时，允许按 UI-005 和 `AGENTS.md` 的受限预授权创建或安装只进行本地截图读取/比较、不会上传数据且可回滚的 Skill。必须记录来源、版本、许可证、权限、网络、冒烟和卸载方式；需要第三方 Unity Package、MCP、账号、密钥、管理员权限、后台服务、外部上传、未知二进制或来源不明时必须停止询问。

自动截图不能验证点击区域、射线、PlayerState、Cost 原子性、阶段转换或 winner；这些仍由 EditMode、PlayMode、日志、manifest 和人工交互分别证明。只有 PNG 有效、manifest 完整、Agent 实际审查且问题有复拍/遗留结论时，自动视觉判断才能记为通过。

## 7. 构建验证

### 7.1 当前状态

TASK-006 使用已安装的 Windows Standalone 支持模块和 `Task006StandaloneBuild.BuildWindowsX64` 构建入口成功生成 Windows x86_64 Player。入口只读取当前启用的 Build Settings 场景，默认输出到 `Temp/TASK-006/WindowsStandalone`，不修改 ProjectSettings。

### 7.2 已知构建风险

1. `UITest.targetSprite` 条件编译作用域阻塞已修复并通过 Standalone 构建复测。
2. 旧 `UnitFactory` 仍从 `Application.dataPath/GameData/Units/EliteVariants/Json` 读取 v2 源文档；它是待销毁的 legacy/debug 原型风险，不是正式 Demo 数据链。正式 Demo 使用 `Resources` 中冻结的 `unit-catalog-v1` 与 `local-battle-v1`，已在 Player 实际加载。
3. 当前只启用 `SampleScene`，还没有独立启动或正式战斗场景可供构建流程选择。

### 7.3 目标平台确认后的验证步骤

1. 使用固定 Unity 版本和明确的构建目标；
2. 构建 Build Settings 中启用的场景；
3. 保存完整构建日志并确认退出码；
4. 启动 Player，检查单位 JSON、默认单位 Prefab、Spine 数据和 UI Prefab 加载；
5. 完成当前原型冒烟流程；
6. 确定性战斗实现后，以相同输入比较 Editor 与 Player 的事件序列、最终状态和胜方；
7. 检查 Player 日志没有未处理异常或资源加载失败。

当前不存在可供 `-executeMethod` 调用的项目构建方法。TASK-001 的基线使用 Unity `-buildWindows64Player` 命令行参数；正式发布构建参数仍需在目标平台确认后单独定义。

## 8. 人工游戏流程验证

### 8.1 当前原型诊断流程

该流程用于记录现有场景真实行为，不等同于 SPEC 战斗验收：

1. 使用指定 Unity 版本进入 `SampleScene`；
2. 进入 Play Mode，记录初始 Console 错误和警告；
3. 点击初始化按钮，确认两个单位类型的资源和单位栏项目是否出现；
4. 点击单位头像，确认详情面板显示对应数据；
5. 使用折叠按钮打开和关闭商店面板；
6. 分别拖拽单位到普通有效格、场外和两个门格；
7. 再把两个单位拖到同一普通格，记录当前重叠行为；该项用于暴露缺口，不应按完整部署规则判为通过；
8. 点击移动调试按钮，观察被记录单位是否到达固定终点并播放移动表现；
9. 退出 Play Mode，检查是否出现异常；
10. 检查工作区 diff，确认 Unity 没有产生意外受版本控制资源变化。

建议保存 Console 日志和关键截图，但内部状态仍应以后续断言或结构化日志为准。

### 8.2 完整战斗流程验收计划

完整战斗闭环实现后，人工验证应至少包含：

1. 从固定测试数据加载双方单位、阵型和 Buff；
2. 记录输入数据版本或哈希；
3. 完成一次战斗计算并保存事件序列、最终状态和胜方；
4. 分别以主场和客场视角演示同一结果；
5. 确认客场只发生坐标和方向转换，双方视角的胜方和存活状态一致；
6. 尝试在演示中操作单位，确认不能改变权威计算结果；
7. 改变窗口大小、渲染帧率或机器负载后重放，确认结果不变；
8. 退出并重新进入场景，确认没有上一场状态残留；
9. 检查 Editor 或 Player 日志。

## 9. 当前暂时无法自动验证的项目

### 9.1 因尚未实现而无法验证

- 回合、准备、战斗和结算状态转换；
- 测试对战数据的正式格式与加载流程；
- 阵型配对、主客场旋转及观察视角转换；
- 寻路；
- 计算事件流到演示层的回放；
- 多玩家同步、断线和重连。

### 9.2 因规则未确认而无法建立精确期望

- 逻辑帧时长和同帧行动顺序；
- 同距离索敌决胜规则；
- 目标失效后的重新索敌时机；
- 伤害取整及异常防御、法抗数值处理；
- 同时全灭、超时和平局处理；
- 待部署区容量是否最终固定为 13；
- Buff 严格相等需要比较的完整字段；
- 商店价格、刷新、出售及赤金返还比例；
- 正式目标平台和发布构建参数。

### 9.3 仍需人工判断的表现项

- 动画是否流畅、动作与事件节奏是否匹配；
- 镜头、朝向、遮挡和 UI 可读性；
- 不同分辨率和宽高比下的布局；
- Spine 资源的视觉完整性和美术质量。

人工判断不能替代战斗输入、事件序列、最终状态和胜方的结构化验证。

## 15. TASK-006 最终验收记录（2026-07-18）

- 前置矩阵：TASK-001 的 Editor 编译证据仍有效但 Standalone 失败证据已过期；TASK-002～005 的测试证据因工作区新增闭环代码而过期，均已在本任务重跑。Unity `2022.3.62f1c1` 和 Windows Standalone 模块均已安装；运行前后未发现其他 Unity 进程。
- 复现与修复：Windows x86_64 构建先以退出码 `1` 复现 `UITest.cs` 的 4 个同根因 `targetSprite`/`CS0103` 诊断。仅将 `Sprite targetSprite` 的局部声明移至条件编译块外，两分支仅赋值；随后四个诊断消失。
- 编译和测试：Editor 编译退出码 `0`；替换为 3 对 4 的真实快照后，`Artifacts/TASK-006/multibattle-editmode-retry-results.xml` 为 30/30/0/0，`Artifacts/TASK-006/multibattle-playmode-results.xml` 为 5/5/0/0。Unity Test Framework 在写入 EditMode XML 后保持驻留，已只终止本次测试启动的进程；最终 XML 和日志均保留在忽略的 `Artifacts/TASK-006`。
- 构建：`Task006StandaloneBuild.BuildWindowsX64` 读取当前唯一启用场景 `Assets/Scenes/SampleScene.unity`，最终构建 Windows x86_64 到 `Artifacts/TASK-006/WindowsStandalone/ARKnoNIGHTS.exe`；退出码 `0`，`BuildReport` 为 `Succeeded`、错误 `0`、警告 `2`、总大小 `108422052`。
- Editor/Player 摘要：Editor 和实际 Player 均得到 `inputDigest=0160DA1D`、`eventDigest=F247BEA4`、`finalStateDigest=C578716F`、`winnerOrReason=Away`、`away-1000-alpha:False,away-1000-bravo:False,away-5503-alpha:True,away-5503-bravo:True,home-1000-alpha:False,home-1000-bravo:False,home-5503-alpha:False`。真实快照为 Home 3 对 Away 4，双方混用 `gopro`/`arcslma` 并打乱部署坐标；两端均对该快照完整计算十次。Player 在显式 `-task006-acceptance` 模式下完成 Home、Away、Replay 和 7 个视图的清理，退出码 `0`。
- 命令：Editor/构建使用 `D:\\2022.3.62f1c1\\Editor\\Unity.exe -batchmode -nographics -quit -projectPath G:\\ARKnoNIGHTS_beta -executeMethod <method> -logFile <log>`；测试分别使用 `-runTests -testPlatform EditMode|PlayMode`；Player 使用 `Artifacts\\TASK-006\\WindowsStandalone\\ARKnoNIGHTS.exe -task006-acceptance -logFile Artifacts\\TASK-006\\player-acceptance.log`。
- 未验证：人工 GUI 按钮序列、动画流畅度/朝向观感/UI 可读性和跨分辨率布局；旧 `UnitFactory` 按钮在 Player 的松散源 JSON 路径；尚未实现的多人、准备、商店、完整回合及同时全灭的长期对局规则。

## 10. 当前 SPEC 验收追踪

| 验收方向 | 主要验证层 | 当前状态 |
|---|---|---|
| Unity 正常编译 | 编译、构建 | Editor 编译与 Windows Standalone 构建均通过 |
| `SampleScene` 正常进入和退出 | PlayMode、人工 | PlayMode 真实 Demo 流程通过；人工 GUI 仍未验证 |
| 从测试输入初始化双方数据 | EditMode、PlayMode、Player | 真实 `local-battle-v1`/目录连接在三层均已验证 |
| `9×4`、`9×8`、门格和旋转 | EditMode | 10 项 TASK-002 EditMode 测试中已覆盖并通过 |
| 计算不依赖物理和渲染帧率 | EditMode、PlayMode | Core 边界和显式 Tick 已由 EditMode 覆盖；PlayMode 未验证 |
| 相同输入产生相同结果 | EditMode、Editor/Player 对比 | Editor 与 Player 都得到 input/event/final-state `0160DA1D/F247BEA4/C578716F`，Away 胜；各自十次运行一致 |
| 移动、索敌、阻挡、伤害和死亡 | EditMode | TASK-003 已覆盖；2026-07-18 XML 中 17 项 EditMode 测试全部通过 |
| 输出明确单场胜方 | EditMode | TASK-003 已覆盖；2026-07-18 XML 中 17 项 EditMode 测试全部通过 |
| 主客场演示共享结果 | EditMode、PlayMode、Player、人工 | 自动验证通过；人工观感仍未验证 |
| 无未处理异常 | 所有层 | 本轮 Editor、测试、构建和 Player 验收日志未见未处理异常 |

## 11. TASK-002 Battle Core 自动化验证（2026-07-17）

- 新增 `ARKnoNIGHTS.Battle.EditModeTests`，覆盖 9×4/9×8 边界、门格、主客场映射与双旋转、厘米定点尺度、fixture 验证错误矩阵、仅 Deployed 参战、重复加载摘要、10 次重复 runner trace/摘要、`maxTicks` 未解决结果和 Core 无 UnityEngine 引用。
- 实际执行：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform EditMode -testResults G:\ARKnoNIGHTS_beta\Temp\TASK-002\EditModeResults.xml -logFile G:\ARKnoNIGHTS_beta\Temp\TASK-002\EditMode.log`。
- 结果 XML：`Temp/TASK-002/EditModeResults.xml`；总计 10，通过 10，失败 0，跳过 0。Unity 在写入结果后因在线配置请求收尾未退出，本轮已结束由该命令启动的残留进程；不影响已落盘的测试结果。
- Editor 脚本编译随测试启动实际完成（日志中 `ScriptAssemblies` 成功）；本任务未执行 PlayMode、Standalone 构建或 Player fixture 加载。已知 `UITest.targetSprite` Standalone 阻塞保持未修改。

## 12. TASK-003 Battle Core 测试清单（2026-07-18）

- `BattleCoreEditModeTests` 新增固定 1v1 完整闭环、事件类型与顺序、10 次逐项事件重复性、同距索敌 unit ID 决胜、低速余量、`< 0.25` 米阻挡边界与对称关系、移动中被非目标拦截、阻挡容量、目标/嘲讽/到门距离/ID 决胜、三类伤害/5% 下限、同 Tick 双方致死和未解决结果断言；原 TASK-002 的 fixture、输入验证和 maxTicks 回归继续保留。
- `task003-minimal-v1` 只使用 1v1、容量 1、整数 Tick、非负属性与 `0..100` 法抗；多单位阻挡竞争使用构造输入覆盖；不覆盖 Buff、远程、异常属性或非整数 Tick。
- 首次带 `-quit` 的尝试只完成脚本编译后提前退出；最终实际执行：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform EditMode -testResults G:\ARKnoNIGHTS_beta\Temp\TASK-003\validated\EditModeResults.xml -logFile G:\ARKnoNIGHTS_beta\Temp\TASK-003\validated\EditMode.log`。
- 结果：通过。XML：`Temp/TASK-003/validated/EditModeResults.xml`；总计 17，通过 17，失败 0，跳过 0。Unity 在结果生成后仍驻留，已仅终止本次测试启动的 Unity 进程（PID 48992）；日志中无 `error CS`、`Scripts have compiler errors` 或 `Compilation failed`。

## 13. TASK-004 Presentation 回放测试清单（2026-07-18）

- `BattlePresentationEditModeTests` 覆盖主场直投影、客场整数/连续定点 180 度投影、双转换恢复、`Spawn` 的 type/side/位置契约、唯一 `unitId → view` 创建、事件消费计数、Move 插值、Attack 动画压缩倍率、Damage/Death 命令、暂停、调速、重播清理，以及回放终态与 `BattleRunResult.FinalUnits` 的 HP/位置/阵营/生死逐项一致性。
- 首次执行在 Core `BattleEnded` 的 DTO 参数迁移处报 `CS7036`，未产生 XML；已补齐缺失的 `damageType` 空值参数后重跑。
- 实际执行：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform EditMode -testResults G:\ARKnoNIGHTS_beta\Temp\TASK-004\EditModeResults-rerun.xml -logFile G:\ARKnoNIGHTS_beta\Temp\TASK-004\EditMode-rerun.log`。
- 结果：通过。XML 总计 23，通过 23，失败 0，跳过 0；其中 `BattlePresentationEditModeTests` 6 项全部通过。结果文件写入后 batchmode Unity 仍驻留，已仅终止本次测试启动的 PID 19544；成功日志无 `error CS`、`Compilation failed` 或 `Scripts have compiler errors`。
- 最小 PlayMode 实际执行：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform PlayMode -testResults G:\ARKnoNIGHTS_beta\Temp\TASK-004\PlayModeResults.xml -logFile G:\ARKnoNIGHTS_beta\Temp\TASK-004\PlayMode.log`。结果：1 项通过、0 失败、0 跳过；验证跨渲染帧回放不会改变 Core 结果。XML 写入后已仅终止本次测试启动的 PID 28636；日志无编译错误标记。
- 真实 `gopro`/`arcslma` Spine 资源播放、缺失动画日志、场景卸载和实际资源映射仍未验证。`home-striker`/`away-guard` 是合成算法测试类型，不应被强行映射为真实资源；规划中的 TASK-004A 先用真实单位 JSON、`unit-catalog-v1` 和 `local-battle-v1` 完成资源连接与回归，之后 TASK-005 再接场景。

## 14. TASK-004A 真实数据回测（2026-07-18）

- 先执行 `D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod Task004aSpineProbe.Run -logFile G:\ARKnoNIGHTS_beta\Temp\task004a-spine-probe.log`，通过 Unity/Spine API 得到 `gopro: Attack=1s, Die=0.666667s, Run_Loop=0.533333s` 与 `arcslma: Attack=2.666667s, Die=1s, Move=1s`。非整数 Tick 的向上取整规则随后由项目负责人确认。
- 生成目录命令：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod UnitCatalogGenerator.Generate -logFile G:\ARKnoNIGHTS_beta\Temp\task004a-catalog-generate.log`。日志输出 `TASK004A_CATALOG_GENERATED ... summary=1000:20|5503:54`；生成器同时校验真实资源、稳定排序和源 JSON 转换。
- EditMode 命令：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform EditMode -testResults G:\ARKnoNIGHTS_beta\Temp\task004a-editmode-results.xml -logFile G:\ARKnoNIGHTS_beta\Temp\task004a-editmode.log`。结果 XML：27 项，通过 27，失败 0，跳过 0；覆盖旧 TASK-002～004 fixture 回归、真实目录、资源缺失诊断、`local-battle-v1` 连接和真实输入十次确定性运行（Away 胜，`Victory`）。
- PlayMode 命令：同一 Unity 以 `-testPlatform PlayMode` 运行；PlayMode 会清理项目 `Temp`，结果 XML 实际写到系统临时目录 `C:\Users\wzy\AppData\Local\Temp\task004a-playmode-results.xml`。结果：2 项通过、0 失败、0 跳过；其中 `RealCatalog_DefaultUnitViewsInitializeMoveAttackAndDispose` 使用真实 `DefaultUnit`、`gopro` 和 `arcslma` 的 SkeletonDataAsset，完成 Spawn、Move、Attack、回放结束及销毁清理。
- 未验证：没有修改或人工打开正式场景；未做 Standalone 构建（既有 `UITest.targetSprite` 非本任务债务）；未进行视觉质量、镜头与动画节奏人工验收。批处理测试结果写出后 UnityConnect 在线配置超时会使 Unity 退出滞后，仅结束了本次由测试命令创建的残留进程，XML 已在结束前落盘。

## 15. TASK-005 Demo 控制器与场景接线验证（2026-07-18）

- Editor 编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -logFile G:\ARKnoNIGHTS_beta\Temp\TASK-005-compile-2.log`。结果为 `Tundra build success`，无 `error CS`。
- 受控场景接线：执行 `BattleDemoSceneSetup.SetupSampleScene`，日志记录 `[BattleDemo][scene.wired]`。仅向 `SampleScene` 添加 `BattleDemoRoot`、`BattleDemoViews` 和 `BattleDemoUI`；保留旧 UnityEvent、Prefab、Build Settings 和项目设置。接线过程中 `VirtualSlotPanel` 的 `ExecuteAlways` 回调曾修改旧 Panel 尺寸，已恢复，最终 scene diff 不包含该无关变更。
- EditMode：`Temp/TASK-005-EditMode-All.xml`，总计 29，通过 29，失败 0。新增 `BattleDemoCoordinatorEditModeTests` 覆盖真实 `local-battle-v1`/`unit-catalog-v1` 加载、封存后播放、暂停/继续、Away 投影、调速、同一结果 Replay、摘要不变、重复视图释放以及缺失对战输入进入包含 `localBattle.resource.missing` 的 Error。
- PlayMode：最新执行 `D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform PlayMode -testResults C:\Users\wzy\AppData\Local\Temp\TASK-005-PlayMode-rotation.xml -logFile G:\ARKnoNIGHTS_beta\Temp\TASK-005-playmode-redgate.log`，XML 总计 4，通过 4，失败 0，跳过 0。`SampleScene_BattleDemoRootRunsTheRealCatalogToCompletion` 实际加载 `SampleScene`，通过场景控制器运行 `1000/gopro` 对 `5503/arcslma`，验证 Away 胜、暂停不消费事件、Home/Away 不改变源事件摘要和 winner、完成状态、连续两次 Replay；同时断言双方真实 `DefaultUnit` 视图在 Home/Away 下均固定为向红门倾倒的世界 X 轴 60 度旋转。既有真实 Spine 回归继续覆盖 `DefaultUnit`、Spawn、Move、Attack 和释放。
- 运行命令在测试 XML 写出后会受 UnityConnect 在线配置超时影响而滞留；每次只终止了本任务启动且 XML 已落盘的 Unity 进程。未执行 Windows Standalone 构建；该项仍留给 TASK-006。未进行人工的 UI 可读性、镜头、动画节奏和不同分辨率检查。

## 16. TASK-003 攻击动画与出伤时序回归（2026-07-18）

- 人工观察到 `arcslma` 最后一次 Attack 在低速播放时会被 `gopro` 的 Death 视觉事件抢先。检查确认 Core 未提前出伤：真实目录中的 `arcslma` Attack 为 54 Tick，Damage 固定在 `Attack.tick + 54`，Death 与该有效 Damage 同 Tick。
- 根因是 `UnitSkelPresentationView` 同时把全局播放倍率设置给 `SkeletonAnimation.timeScale`，又把它乘进单次 `TrackEntry.TimeScale`；0.5× 时动画实际以 0.25× 播放，而事件以 0.5× 推进。修正后全局倍率只应用一次，单次 entry 只接收 Attack 事件的原始/有效时长倍率。
- EditMode：`Temp/TASK-003-animation-timing/EditModeResults.xml`，总计 30，通过 30，失败 0，跳过 0；新增真实 `arcslma` 54 Tick Attack/Damage/Death 时序断言。
- PlayMode：`Temp/TASK-003-animation-timing/PlayModeResults.xml`，总计 5，通过 5，失败 0，跳过 0；新增真实 Spine 断言，验证 0.5× 时 `arcslma` Attack entry 不重复叠加播放倍率。

## 17. TASK-003 多单位双向阻挡竞争回归（2026-07-18）

- 真实单位目录重新由 `UnitCatalogGenerator.Generate` 生成；`gopro` 与 `arcslma` 源 JSON 的既有 `narrowTitle: 0` 均映射为目录和 Core 的 `tauntLevel: 0`。生成日志：`Temp/TASK-003-blocking-catalog.log`，包含 `TASK004A_CATALOG_GENERATED ... summary=1000:20|5503:54`，脚本编译成功。
- EditMode：`Temp/TASK-003-blocking-EditModeResults.xml`，总计 32，通过 32，失败 0，跳过 0。新增回归覆盖移动单位被非当前目标拦截、双方对称关系、多阻挡容量，以及候选的当前目标、嘲讽、到本方门格距离和 unit ID 决胜。
- PlayMode：`Temp/TASK-003-blocking-PlayModeResults.xml`，总计 5，通过 5，失败 0，跳过 0。运行完成后 UnityConnect 在线配置请求超时导致 batchmode 进程未自行退出；结果 XML 已先落盘，只终止了本轮测试启动的 Unity 进程。

## 18. 动画回放覆盖与暂停回归（2026-07-19）

- 三单位构造输入确认 Core 行为：当 B 已与 C 建立满容量阻挡、A 以 B 为当前目标后进入攻击半径时，A 在进入范围的 Tick 先产生 `Move` 再产生 `Attack`，但不会与 B 建立第二条阻挡关系，且后续不再继续 Move。
- Presentation 修复：连续 `Move` 事件只触发一次循环移动动画；进入攻击范围后不再为该单位生成 Move，Attack Track 不会被覆盖；Pause 将所有视图播放倍率置为 `0`，在暂停期间调速仍保持冻结，Resume 才应用新倍率。
- EditMode：`C:\Users\wzy\AppData\Local\Temp\battle-animation-regression-editmode.xml`，总计 34，通过 34，失败 0，跳过 0。日志：`Temp/battle-animation-regression-editmode-rerun.log`。
- PlayMode：`C:\Users\wzy\AppData\Local\Temp\battle-animation-regression-playmode.xml`，总计 6，通过 6，失败 0，跳过 0。真实 `DefaultUnit` 断言 Pause 将 `SkeletonAnimation.timeScale` 置为 0，暂停中调速仍为 0，Resume 才恢复新倍率。日志：`Temp/battle-animation-regression-playmode-final3.log`。

## 21. 阻挡期间的攻击目标回归（2026-07-19）

- 使用固定真实对战数据验证：`away-5503-alpha` 被 `gopro` 阻挡后，仍保留常规索敌目标用于解除阻挡后的移动，但必须向该存活 gopro 产生 Attack 事件；该规则同时适用于任一被拦截单位。

## 22. 共享攻击/阻挡范围单 Tick 穿透回归（2026-07-19）

- 使用单 Tick 移动距离大于初始间距的高速构造输入验证：单位在进入严格小于 `0.25` 米的共享攻击/阻挡范围时，必须停在非零距离的范围内位置并立刻产生 Attack；不得因任一方阻挡位耗尽而继续走到目标中心。

## 19. 目标死亡后的攻击动画锁回归（2026-07-19）

- Core 回归场景使用 A、B、C 和存活的后续目标：A 与 C 同时攻击 B，C 先在 Tick 2 击杀 B，A 的有效攻击动画原定在 Tick 5 结束。B 死亡后 A 会在下一 Tick 重新锁定存活目标，但直到 Tick 5（含）都不得移动；B 不接收 A 的 Damage，A 在 Tick 6 才可恢复移动。
- EditMode：`Temp/TASK-003-target-death-lock-EditModeResults.xml`，总计 35，通过 35，失败 0，跳过 0。首次编译发现并修正移动筛选中的局部变量名错误；随后全量 Core、目录和表现回归通过。
- PlayMode：`Temp/TASK-003-target-death-lock-PlayModeResults.xml`，总计 6，通过 6，失败 0，跳过 0。

## 20. 移动与攻击左右朝向回归（2026-07-20）

- `UnitSkelPresentationView` 默认面向世界右方；Move 的世界 X 正向保持默认，X 负向在 Y 轴翻转 180 度，纯 Z 位移保持现有左右朝向。Attack 事件以攻击者和目标在该 Tick 的权威位置更新左右朝向；攻击方向为纯 Z 时保持现有左右朝向，后续 Move 可覆盖攻击朝向。
- PlayMode 自动化测试覆盖默认、左移、右移和两种纯 Z 位移；`SampleScene` 真实视图回归只要求其保持右/左两种合法朝向，不再错误地要求所有单位固定朝向同一门格。
- EditMode 回归覆盖 Attack 目标方向及其持续到后续 Move 的规则；PlayMode 覆盖真实视图的默认、左右与纯 Z 朝向行为。

## 23. UI-001 本地玩家状态与 Player-safe 类型目录（2026-07-21）

- 目录生成兼脚本编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod UnitCatalogGenerator.Generate -logFile G:\ARKnoNIGHTS_beta\Temp\UI-001\catalog-generate-final.log`。日志包含 `Tundra build success` 和 `TASK004A_CATALOG_GENERATED ... summary=1000:20|5503:54`，无 `error CS`、`Compilation failed` 或 `Scripts have compiler errors`。
- UI-001 EditMode：`Temp/UI-001/playerstate-editmode-results-projection.xml`，总计 7，通过 7，失败 0，跳过 0。覆盖真实目录 UI 字段、资源加载、Cost=99、重复加载摘要、严格堆叠/排序/数量、加载失败矩阵、13 槽容量、一基边界/门格、部署/撤退原子性、费用与 Overflow 清理。
- Player-safe Resources 加载已由上述 EditMode 直接验证；未执行 PlayMode、Windows Standalone 构建和人工 UI 检查，本任务未修改场景或 UI。
- 历史全量 EditMode：`Temp/UI-001/editmode-results-post-projection.xml` 曾为 51 项中的 50 通过、1 失败；失败测试把合成 fixture 的初始 HP 写死为 `100`，但权威 Spawn 分别为 `home-1=1000`、`away-1=240`，且完整回放后 `home-1=980`。已改为从 Spawn/最终只读结果断言状态条输入；2026-07-21 `BattlePresentationEditModeTests` 回归 `Temp/battle-presentation-editmode-results.xml` 为 10 通过、0 失败、0 跳过。尚未据此重跑全量 EditMode，不能把历史全量结果记为通过。
- UnityConnect 在线配置请求超时会在 XML 写入后使 batchmode 进程驻留或退出异常；仅在结果 XML 已落盘后终止了本任务启动的 Unity 进程，未影响 XML 中的测试统计。

## 24. UI-002 正式待部署 HUD 验证（2026-07-21）

- 场景接线与脚本编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod UI002SampleSceneSetup.SetupSampleScene -logFile G:\ARKnoNIGHTS_beta\Temp\UI002-setup.log`。Unity 完成脚本编译和幂等场景接线，日志含 `[StagingHud][scene.wired]`；`VirtualSlotPanel` 的既有 `[ExecuteAlways]` 调试日志在场景打开/关闭时出现，但最终旧对象已失活，正式 HUD 不引用它。
- 首次携带 `-quit` 的 Test Runner 命令只完成了编译而未写 XML；按既有测试约定改用无 `-quit` 命令。布局 EditMode：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform EditMode -testFilter ArknoNights.Battle.Tests.StagingHudLayoutEditModeTests -testResults G:\ARKnoNIGHTS_beta\Temp\UI002-EditMode.xml -logFile G:\ARKnoNIGHTS_beta\Temp\UI002-EditMode.log`。XML 为 9 项通过、0 失败、0 跳过，覆盖 0/1/10/12/13 槽、1920 宽度下自然右对齐/压缩、首中末选择、最小宽度、取消选择和 PlayerState 顺序/稳定选择 ID。
- 真实场景 PlayMode：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform PlayMode -testFilter ArknoNights.Battle.Tests.StagingHudScenePlayModeTests -testResults G:\ARKnoNIGHTS_beta\Temp\UI002-PlayMode.xml -logFile G:\ARKnoNIGHTS_beta\Temp\UI002-PlayMode.log`。XML 为 1 项通过、0 失败、0 跳过；加载 `SampleScene` 后断言唯一正式 HUD、自动 PlayerState、Cost `99`、有序 `1000/5503` 槽、旧 Init/商店/折叠/调试按钮不在激活层级、BattleDemoRoot 持续启用，且调试 UI 显隐和玩家状态刷新清除选择 ID 均不销毁根控制器。
- UnityConnect 在线配置在 XML 写入后仍会导致测试启动进程滞留；每次仅终止本轮命令启动、XML 已落盘的 Unity PID。日志未见本次改动引起的 `error CS`、`Scripts have compiler errors`、未处理异常或 `[StagingHud]` 初始化失败。
- 未验证：没有可用 GUI 自动化来观察 `1920×1080` 和另一种 16:9 分辨率下的头像裁切、图层、字体、边距、压缩视觉和组合键输入；未执行 Windows Standalone 构建（UI-002 不要求）。

## 25. UI-003 状态驱动部署与撤退验证（2026-07-21）

- 场景接线与编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod UI003SampleSceneSetup.SetupSampleScene -logFile G:\ARKnoNIGHTS_beta\Temp\UI003-setup.log`。结果码 `0`，日志包含 `[UI-003][scene.wired]`；随后编译日志 `Temp/UI003-compile.log` 包含 `Tundra build success`，无 C# 编译错误。
- EditMode：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform EditMode -testFilter ArknoNights.Battle.Tests.LocalPlayerStateEditModeTests -testResults G:\ARKnoNIGHTS_beta\Temp\UI003-EditMode.xml -logFile G:\ARKnoNIGHTS_beta\Temp\UI003-EditMode.log`。XML 为 7 项通过、0 失败、0 跳过；覆盖一基边界、门格、占位、Cost、原子部署/撤退与 13 槽撤退容量失败。
- PlayMode：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -projectPath G:\ARKnoNIGHTS_beta -runTests -testPlatform PlayMode -testFilter ArknoNights.Battle.Tests.StagingHudScenePlayModeTests -testResults G:\ARKnoNIGHTS_beta\Temp\UI003-PlayMode.xml -logFile G:\ARKnoNIGHTS_beta\Temp\UI003-PlayMode.log`。XML 为 3 项通过、0 失败、0 跳过；真实目录/`DefaultUnit` 冒烟驱动一个稳定 unit ID 进入 `(5,2)`、验证 Cost `99→97` 和唯一准备视图，再撤退验证 `97→99` 与槽恢复；门格失败和交互锁确认 PlayerState 与预览清理无副作用。
- 未验证：没有执行真实鼠标拖拽、点击世界撤退按钮、选择菱形/图标位置、Sprite 排序和 TASK-007 状态条遮挡的人工 Editor 观察；未执行 Standalone 构建（UI-003 不要求）。

- 后续交互修正：待部署槽补齐 `IDragHandler`，使 Unity EventSystem 实际建立 `pointerDrag`；准备格世界 Z 偏移修正为 `-100`，预览改为松手前自由跟随。PlayMode 重跑 `Temp/UI003-visual-fix-PlayMode.xml`：3 项通过、0 失败、0 跳过，并断言单位锚点、水平选择菱形、图标大小命中框、稳定 Player unit ID 和自由预览坐标。

## 26. TASK-007 世界空间状态条验证（2026-07-22）

- 公式 EditMode：`Temp/TASK-007/LayoutEditModeResults.xml`，3 项通过、0 失败、0 跳过。覆盖满血无盾隐藏、受伤/有盾显示、`100/50/50` 的 `60/30/30` 两层宽度，以及无效最大 HP 的隐藏与零宽度。
- Prefab 与转向 PlayMode：`Temp/TASK-007/UnitWorldStatusBarPlayMode.xml`，1 项通过、0 失败、0 跳过。创建真实 `Prefabs/DefaultUnit`，验证 Renderer/层级引用、世界宽度、左右锚点、左朝向后 HP/护盾的世界左右语义，以及清理。
- 最终全量 PlayMode：`Temp/TASK-007/AllPlayModeFinal.xml`，11 项通过、0 失败、0 跳过。该次在 `_ForceWhite` 局部 Shader 修正后运行；日志未见 `error CS`、`Compilation failed`、`Scripts have compiler errors` 或未处理异常。UnityConnect 在线配置超时出现在测试结束后，结果 XML 已落盘。
- 未验证：未执行 Windows Standalone 构建；无可用 GUI 自动化或本轮人工 Editor 观察来确认地图遮挡、两单位重叠时的层级、贴图观感、用户调节后的局部偏移，以及 Home/Away、Replay、死亡期间的视觉效果。状态条材质明确使用 `ZTest LEqual`，不会通过强制置顶掩盖此项。

## 27. UI-004 准备—战斗循环验证（2026-07-22）

- 编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -logFile G:\ARKnoNIGHTS_beta\Temp\UI-004-compile-2.log` 返回码 0；日志未含 `error CS`、`Compilation failed` 或 `Scripts have compiler errors`。
- EditMode：`Temp/UnityTests/20260722-144751/EditModeResults.xml`，全量共 65 项、通过 65、失败 0、跳过 0；其中 `PreparationBattlePhaseEditModeTests` 覆盖 30 秒单次转换、Overflow 永久删除、唯一最高可支付自动部署到 `(5,2)`/Cost 扣除、高价不可支付时的下一最高可支付候选、运行时输入冻结与空 Home 的固定 Away 胜。
- PlayMode：`Temp/UnityTests/20260722-144919/PlayModeResults.xml`，全量共 12 项、通过 12、失败 0、跳过 0；其中 `PreparationBattleLoopPlayModeTests` 实际重载 SampleScene，验证自动进入 Preparation、测试时钟直接跨越 30 秒、交互锁、运行时输入、Overflow/自动部署、完整 Presentation Completed 后返回 Preparation、30 秒重置、准备视图恢复和 PlayerState 不接收战斗结果。既有固定 Resources Demo 场景回归通过显式关闭 formal 模式验证其兼容入口。

## 28. UI-005 HUD 与集成验收审计（2026-07-23）

- HUD PlayMode：`Artifacts/UI-005/ui005-cost-health-elite-final-retry.xml`，筛选 `ArknoNights.Battle.Tests.StagingHudScenePlayModeTests`，共 5 项、通过 5、失败 0、跳过 0。覆盖正式 HUD 自动加载、真实部署/撤退、交互锁、待部署/已部署/战斗信息选择互斥，以及仅显示已声明资源占位。
- 完整循环 PlayMode：`Artifacts/UI-005/ui005-phase-loop-audit.xml`，筛选 `ArknoNights.Battle.Tests.PreparationBattleLoopPlayModeTests`，共 1 项、通过 1、失败 0、跳过 0。覆盖 SampleScene 自动经历 Preparation→Battle→Preparation，且战斗结果不回写 `PlayerState`。
- Windows Standalone：`Artifacts/UI-005/ui005-build-cost-health-elite-final.log` 记录 `build.succeeded`、`errors=0`；`Artifacts/UI-005/player-capture-cost-health-elite-final.log` 记录 6 张 Player PNG 已捕获到 `Artifacts/UI-005/CapturesCostHealthEliteFinal/`。PNG 已检查关键 HUD 状态可读，且不为黑屏/空白帧。
- 未通过完成条件：该目录 `manifest.json` 只记录图片路径、分辨率、阶段、选中单位 ID、Cost、准备倒计时，缺少场景、Canvas scale、fixture/schema、选中 slot、槽顺序/宽度、关键 RectTransform、敌人数和捕获时间点；不满足 UI-005 第 39 条。因此自动截图证据不完整。
- 未验证：未在本轮 Windows Player 手工走完部署、点击/拖拽命中与完整返回准备；尚无逐图对照图 1～图 6 的持久化视觉差异报告；共享脏工作树下的最终无关差异审查尚未完成。故本节只证明部分验收，不将 UI-005 记为完成。详细审计见 `docs/UI-005-REPORT.md`。
- UnityConnect 在线请求可能在测试完成后滞留；本轮由 `scripts/Invoke-UnityTests.ps1` 在 XML 已写入且测试数大于零后管理其子进程。未执行人工 GUI 完整一轮或 Windows Player 构建，均为未验证。

## 29. UNIT-DATA-001 与单位精英变体 v2 源迁移验证（2026-07-23、2026-07-28）

- v2 EditMode 覆盖：`unit-elite-variants-v2` schema、精英 0 完整性、高阶最近低阶继承、`combat`/`shared`/`model` 原子块、`sourceVariant`、动画 key/名称/必需时长、旧源字段消失、合法不攻击/不阻挡数据，以及三个真实源的数值、资源和动画事实。
- 消费者 EditMode 覆盖：`UnitCatalogGenerator`、`AbilityCatalogGenerator`、`UnitJsonBake` 只读取 v2，未知召唤与不可表示的 v1 投影显式失败；真实目录回归同时区分 authored v2 事实与 frozen `unit-catalog-v1` 事实，不修改冻结目录期望。
- Task 6 的静态验收分别扫描生产 C#/JSON 与测试 C#/JSON。生产扫描必须为零匹配；测试扫描只允许无效 schema fixture、旧目录不存在断言和旧根 DTO 销毁断言，不能删除或混淆这些负向回归字符串。
- 精确定向 GREEN 使用 `UnitEliteVariantSourceEditModeTests;UnitSourceConsumerEditModeTests;BattleCoreEditModeTests`，输出到 `Temp/UnitEliteVariantsV2/Task6-Green`；必须核对非零测试数、零失败、零跳过或明确记录跳过、编译/异常日志和 Unity 退出状态。
- 2026-07-29 Task 6 实际结果：生产扫描零匹配；测试扫描保留 `6` 个 allowlisted 行。定向 EditMode 为 `105/105` 通过、失败 `0`、跳过 `0`，其中 v2 source `43/43`、consumer `5/5`、BattleCore `57/57`；日志未命中编译错误、编译失败、未处理异常、空引用或断言失败。结果位于 `Temp/UnitEliteVariantsV2/Task6-Green/EditModeResults.xml` 与 `EditMode.log`。结果落盘后 Unity 未在 `20` 秒 grace period 内自然退出，runner 强制停止；随后确认无 Unity 进程残留。
- 首批迁移禁止执行目录生成器，`unit-catalog-v1.json` 与 `ability-catalog-v1.json` 必须保持冻结哈希。PlayMode、目标平台构建和其余单位/独立动画层不属于 Task 6 精确定向验收，未执行时必须标记为未验证。
- 2026-07-29 Task 7 全量 EditMode：`Artifacts/UnitEliteVariantsV2/Full-EditMode/EditModeResults.xml` 为 `232 total / 227 passed / 5 failed / 0 skipped / 0 inconclusive`，wrapper 退出码 `1`，Unity 在结果落盘后正常退出；其中任务范围内 v2 source `43/43`、consumer `5/5`、BattleCore `57/57` 均通过。五项失败均来自并发 BONDS 资源导入工作：四项 `BondsUnitAnimationAuditEditModeTests` 报告 `expected=93 parsed=99 unique=99`，一项未跟踪的 `BondsUnitResourceImportEditModeTests` 断言 `Expected: 93 / But was: 99`；`docs/bonds/BONDS_SPEC.md` 已并发扩展为 `99` 个 TypeId，而对应审计代码和测试仍硬编码 `93`。这些失败不属于本次单位精英变体 v2 迁移，Task 7 未修改或重跑 BONDS 工作，但因此不能把本轮全量 EditMode 记为通过。XML 与日志 SHA-256 分别为 `037DBE5BA36EA997FD699779E712E48E37DBF8AC20B790801E84855CCA9B7CE1`、`D892B7881EB2CB4D7BD5A6C3F3B5A0F1BCCA7AB06A5D118116A59F881B6095F2`。
- 2026-07-29 Task 7 全量 PlayMode：`Artifacts/UnitEliteVariantsV2/Full-PlayMode/PlayModeResults.xml` 为 `27 total / 27 passed / 0 failed / 0 skipped / 0 inconclusive`，wrapper 退出码 `0`，Unity 正常退出；XML 与日志 SHA-256 分别为 `68B4677F0D7FCADF3637E62C1753CF71E03E337E9985C20FAC1EA40E4BD8831C`、`7213A123211732CDC85B470AB3490D3025A36E94A6AE60003247C0DBD527A582`。
- 2026-07-29 Task 7 Windows x64 StrictMode：首次完整构建的结构化日志为 `result=Succeeded`、`errors=0`、`warnings=2`，两条均为既有 `Assets/Game/Runtime/Bitset/TagRegistry.cs(11,39) warning CS0414`，但当前 PowerShell 主机异步返回 GUI `Unity.exe`，没有捕获该次 Unity OS 退出码；日志在覆盖前的 SHA-256 为 `EDC6DA6AAF98AD2105F87D3D083ACC8A75D313121286A3579D9D7A87EC973279`。随后在全机 Unity 队列为空时使用相同 Unity 参数和 `Start-Process -Wait -PassThru` 复建，捕获 Unity 退出码 `0`；增量构建日志 `Artifacts/UnitEliteVariantsV2/WindowsBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=0`，SHA-256 为 `6BFE96FE6E08E2E319C6DC84774582606AF51A3131C34F8C0A995C6B9E00565F`。产物为 `143` 个文件、总计 `282952109` 字节，主 EXE SHA-256 为 `F49CDCB9E27CF2AA5C6F63BC961B4364E93363071533661593549AEC421AD60B`。
- 2026-07-29 Task 7 冻结目录与静态审计：`unit-catalog-v1.json` SHA-256 为 `359C81D56AB89EA735FAFCD0F2A6CA243076DE7C72A9086B7E4097B6B728B0AA`，`ability-catalog-v1.json` SHA-256 为 `BA76A69BFC5AFB186863ECF28AB36EBD504F14CD503347BE09CEEC652ECB3466`。人工维护目录恰好包含 `1000_gopro.json`、`5503_arcslma.json`、`5504_arcslmi.json`，旧目录不存在；源 JSON 禁用字段扫描为零匹配。计划列出的 `16` 个迁移提交及单独的计划更正提交 `1ef5d16` 均存在于当前 HEAD 祖先链且通过 `git show --check`；共审计 `38` 个唯一提交路径，没有场景、Prefab、ScriptableObject、Package、ProjectSettings 或冻结目录文件，当前相关 Unity 资产没有缺失或孤立 `.meta`。Task 7 前后工作树脏状态数量均为 `703`（已跟踪修改 `14`、未跟踪 `689`），任务范围路径保持干净，暂存区保持为空。

### BONDS 特殊索敌、冲门与单场生命损失（2026-07-29）

- `BondsTargetingEditModeTests`：`7/7` 通过，`failed=0`、`skipped=0`。覆盖 `Untargetable`、`UntargetableByMelee` 的攻击方式差异、非攻击单位、零阻挡、无合法目标冲门、边长 `0.8m` 门区、`GateReached`、`lifeDeduct`、较少扣血方胜出，以及退出 Tick 的 Presentation Track 隐藏。
- `UnitSourceConsumerEditModeTests`：`7/7` 通过，`failed=0`、`skipped=0`。覆盖被动单位特征的能力目录隔离投影，以及 `1017/1042/1146/1355` 全部 v2 变体对 `UNTARGETABLE_BY_MELEE` 的显式引用。
- `BattleCoreEditModeTests`：第一次完整回归 `57` 项中发现 `2` 项失败；根因是目标死亡时错误删除待结算攻击并提前解除攻击动画锁。修正为仅清理失效攻击者、目标死亡或冲门只在到期 Tick 取消伤害后，最终完整回归 `57/57` 通过，`failed=0`、`skipped=0`。
- 额外静态构建：`dotnet build ARKnoNIGHTS.Battle.EditModeTests.csproj --no-restore --nologo -v:minimal` 为 `0` error；警告来自既有 Unity 程序集版本冲突与测试反序列化 DTO 未直接赋值。
- Unity 两次测试均在结构化 XML 写入后通过；中国版配置请求令 Editor 未在 10 秒收尾窗口内自行退出，脚本随后结束对应 batchmode 进程。未运行 PlayMode 与 Player 构建。
- 2026-07-29 Task 7 独立审查后修复：审查发现解析器已拒绝 `animations[].key == "Default"`，但未拒绝 `animations[].name == "Default"`。提交 `8893b1d` 先加入负向回归测试；RED 为 `44 total / 43 passed / 1 failed / 0 skipped`，唯一失败证明 `key=idle/name=Default` 会被旧实现接受。随后以相同 `StringComparison.Ordinal` 同时校验 key 与 name；GREEN 为 `44/44` 通过、失败 `0`、跳过 `0`，wrapper 退出码 `0`，日志没有编译错误或未处理异常。证据位于 `Artifacts/UnitEliteVariantsV2/DefaultBindingFix/{RED,GREEN}`。两次 Unity 都在结果落盘后超过 runner grace period 并被强制停止，最终确认无 Unity/UnityHub 残留；修复提交经独立只读复审为 `CLEAN`。
- 以下三项是 2026-07-23 v1 规范化阶段的历史证据，不是 Task 6 重跑结果，也不能替代上述 v2 冻结边界验收：
- 实际目录生成：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod UnitCatalogGenerator.Generate -logFile G:\ARKnoNIGHTS_beta\Temp\UNIT-DATA-001\catalog-generate.log`，退出码 `0`；运行时日志包含两条未配置显示名诊断和 `TASK004A_CATALOG_GENERATED ... summary=1000:20|5503:54`，没有 C# 编译错误。第二次生成后的 SHA-256 与首次相同：`3DCB9B8CF8A346D0A4AE17301DB8E178C143194C5A50EDB8CA5DF24FCC3EA81E`。Unity 后续清理了这两份 `Temp` 生成日志。
- 实际测试：仓库 `scripts/Invoke-UnityTests.ps1` 分别运行全量 EditMode 与 PlayMode；脚本在结果 XML 写入后验证非零测试数并解析结果。EditMode 为 `66` 通过、`0` 失败、`0` 跳过；PlayMode 为 `14` 通过、`0` 失败、`0` 跳过。Unity 清理 `Temp` 时删除了这些短生命周期 XML，因此计数以脚本当场解析的结果为准；无测试失败或编译错误。
- 实际 Windows Standalone 构建：`Task006StandaloneBuild.BuildWindowsX64` 成功，日志 `Temp/UNIT-DATA-001/WindowsStandaloneBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=2`，产物输出到忽略的 `Temp/TASK-006/WindowsStandalone/`。未执行人工 GUI 验收；中文名和技能说明仍等待用户填写。

## 30. PREP-DEPLOY-001 已部署单位选中后拖动换位（2026-07-24）

- 状态层 EditMode：`Temp/PREP-DEPLOY-001/final-focused-edit/EditModeResults.xml`，筛选 `ArknoNights.Battle.Tests.LocalPlayerStateEditModeTests`，共 `12` 项、通过 `12`、失败 `0`、跳过 `0`。新增断言覆盖空格移动、己方占格原子交换、同格成功 no-op、越界/门格/不存在/非部署失败、费用不变、version/Changed 次数、CanonicalSummary 变化和重复固定序列确定性。
- 场景 PlayMode：`Temp/PREP-DEPLOY-001/green-review-fixes/PlayModeResults.xml`，筛选 `ArknoNights.Battle.Tests.StagingHudScenePlayModeTests`，共 `9` 项、通过 `9`、失败 `0`、跳过 `0`。新增断言覆盖未选中单位不能开始重定位、选中单位交换后的两个视图位置、原始 unit ID 选择保持、门格失败恢复、交互锁取消不改状态、屏幕 UI 上松手取消，以及撤退命中框不启动换位。`Temp/PREP-DEPLOY-001/phase-loop/PlayModeResults.xml` 中 `PreparationBattleLoopPlayModeTests` 为 `1/1` 通过，确认准备→战斗→准备循环回归。
- 全量 PlayMode：`Temp/PREP-DEPLOY-001/final2-full-play/PlayModeResults.xml`，共 `18` 项、通过 `18`、失败 `0`、跳过 `0`。
- 全量 EditMode：`Temp/PREP-DEPLOY-001/final2-full-edit/EditModeResults.xml`，共 `70` 项、通过 `69`、失败 `1`、跳过 `0`，因此不得记为全量通过。失败项为既有 `BattleCoreEditModeTests.RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources`：当时 `HEAD` 中 `Assets/GameData/Units/Json/1000_gopro.json` 的 `displayNameZhHans` 已为 `狂暴的猎狗pro`，该既有测试仍断言空字符串；两者都不在该历史任务 diff。本任务范围的 `LocalPlayerStateEditModeTests` 已在上述定向 XML 中全数通过。
- Editor 编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -logFile G:\ARKnoNIGHTS_beta\Temp\PREP-DEPLOY-001\final2\compile.log` 退出码 `0`；日志含 `Tundra build success`，未含 `error CS`、`Compilation failed` 或 `Scripts have compiler errors`。
- Windows Standalone：`Task006StandaloneBuild.BuildWindowsX64` 输出 `Temp/PREP-DEPLOY-001/final2/WindowsStandalone/ARKnoNIGHTS.exe`，构建摘要为 `result=Succeeded`、`errors=0`、`warnings=1`、总大小 `165411244` 字节。
- 结果保留说明：上述测试脚本均在 XML 首次完整写入、确认测试数大于零后立即解析并输出结果；随后 Unity 的 AssetDatabase 清理了这些 `Temp/PREP-DEPLOY-001` 短生命周期 XML。因此计数以本轮脚本的当场结构化解析输出为准，未将已清理文件视为持续可用证据。
- 未验证：当前无可靠的交互式 Unity Editor/Player GUI 驱动，未手工执行第一次点击、真实鼠标阈值拖动、空格移动、两单位交换、门格失败及战斗后返回准备的视觉流程；也未在生成的 Windows Player 内人工验收。自动断言不替代这些视觉/手势检查。

## 31. PREP-DEPLOY 已部署单位拖动时隐藏选择框（2026-07-24）

## 32. UI-INFO-001 验证（2026-07-24）

- Focused EditMode：42 passed / 0 failed / 0 skipped，`Temp/UnityTests/20260724-135249/EditModeResults.xml`。
- Focused PlayMode：10 passed / 0 failed / 0 skipped，`Temp/UnityTests/20260724-135337/PlayModeResults.xml`。
- Windows Player 构建、capture suite 运行与人工视觉比对尚未执行。

## 33. UI-INFO-002 信息面板局部视觉验证（2026-07-25）

- Player 证据位于 `Artifacts/UI-INFO-002/00-baseline` 至 `10-final`。`scripts/ExportUiInfoEvidence.ps1` 为各阶段生成原始 PNG、面板裁切、JSON manifest、图 6 并排图和调整记录；最终证据为 `Artifacts/UI-INFO-002/10-final/captures/`。
- TDD 夹具入口先按预期失败（入口不存在），实现后单项 PlayMode 转绿。夹具仅经 `-uiCaptureVisualFixture` 显式触发，覆盖 `--`、中等长度中文名和 `18000/18000` 的 20pt HP 文本。
- 全量 EditMode：`Artifacts/UI-INFO-002/10-final/verification/EditModeResults.xml`，`77 passed / 0 failed / 0 skipped`。全量 PlayMode：`Artifacts/UI-INFO-002/10-final/verification/PlayModeResults.xml`，`19 passed / 0 failed / 0 skipped`。
- Windows Player 构建日志 `Artifacts/UI-INFO-002/10-final/verification/WindowsStandaloneBuild.log` 为 `Succeeded`、`errors=0`、`warnings=1`（未修改的 `TagRegistry.freezeAppend`，CS0414）；最终 Player 日志 `Artifacts/UI-INFO-002/10-final/verification/PlayerCapture.log` 为 `capture.completed count=8`。用户已人工确认鼠标交互通过，并已目检最终空名、中等中文名及并排图。

- TDD 红灯：`Temp/PREP-DEPLOY-001-indicator/red/PlayModeResults.xml`，筛选 `ArknoNights.Battle.Tests.StagingHudScenePlayModeTests`，共 `9` 项、通过 `6`、失败 `3`、跳过 `0`。三个失败均为新断言：开始已选中单位的重定位后 `DeployedUnitSelectionIndicator.activeSelf` 仍为 `true`，证实测试捕获的是本次需求缺口。
- 定向 PlayMode：`Temp/PREP-DEPLOY-001-indicator/green-review/PlayModeResults.xml`，同一筛选共 `9` 项、通过 `9`、失败 `0`、跳过 `0`。覆盖拖动开始隐藏选择框、成功换位与同格 no-op 后恢复、门格失败后恢复、UI 上松手与失焦取消后恢复，以及交互锁仍清除选择框。
- Editor 编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -logFile G:\ARKnoNIGHTS_beta\Temp\PREP-DEPLOY-001-indicator\compile-final.log` 退出码 `0`；日志含 `Tundra build success` 并以 `Exiting batchmode successfully now!` 结束，未发现 `error CS`、`Compilation failed` 或 `Scripts have compiler errors`。
- 未验证：无可靠的交互式 Unity Editor/Player GUI 驱动，尚未以真实鼠标拖动目视检查隐藏和恢复的逐帧表现；自动场景断言验证的是实际控制器生命周期而非人工视觉体验。

## 34. UI-009 四玩家双战斗场景集成验证（2026-07-26）

- EditMode：通过 `scripts/Invoke-UnityTests.ps1` 对本 worktree 运行全量 EditMode，结果为 `130 passed / 0 failed / 0 skipped`，结果文件为 `Temp/UI-009/full-edit-final-replay/EditModeResults.xml`。新增覆盖四玩家固定配对、每名玩家只封存一次、严格相同最高费用堆叠按 `unitId` 稳定选取、输入单位 ID 不交叉、结果不回写 PlayerState，以及两份独立结果/Track 的稳定摘要。
- Presentation 回归：`BattlePresentationEditModeTests.TrackPlayback_ViewStatesSampleTheCurrentPresentationTickInsteadOfTheFinalResult` 先以当前 Tick 和最终 Tick 的 HP 差异复现失败，修正后通过；`MultiBattlePresentationCoordinatorEditModeTests` 还先复现“重播只改 Tick、不重建视图”的失败，再在 `Temp/UI-009/green-replay-rebind/EditModeResults.xml` 通过。两项断言分别防止场景 HUD 在 Tick 0 提前显示最终死亡/HP，以及重播时画面停留在结尾。
- PlayMode：同一脚本的全量 PlayMode 结果为 `19 passed / 0 failed / 0 skipped`，文件为 `Temp/UI-009/full-play-final-replay/PlayModeResults.xml`。`PreparationBattleLoopPlayModeTests` 实际加载 `SampleScene`，覆盖 Preparation 到双 Battle、`MatchAB`/`MatchCD`、共同 Tick、P4 Away 观察、结果对象不重算、两场结束后只返回一次 Preparation，及四名玩家的持久局内状态不被战斗结果覆盖。`StagingHudScenePlayModeTests` 回归了战斗单位选择和正式 HUD 的信息互斥。
- Windows Standalone：执行 `Task006StandaloneBuild.BuildWindowsX64`，日志 `Temp/UI-009/WindowsStandaloneBuild-final-replay.log` 记录 `result=Succeeded`、`platform=StandaloneWindows64`、`errors=0`、`warnings=2`，输出目录为忽略的 `Temp/TASK-006/WindowsStandalone/`。本次没有改动场景、Prefab、Package 或项目设置。
- 未验证：没有在交互式 Editor 或 Windows Player 中人工观察 `MatchAB`/`MatchCD` Home/Away 的相机、朝向、动画速度、世界状态条和最终视觉布局；尚未由左侧玩家列表的真实按钮触发 `TryObserveBattlePlayer`，因为该图形挂接和截图 manifest 扩展属于 UI-010。自动断言与构建成功不替代上述视觉和鼠标流程检查。

## 35. 正式 HUD 场景集成与截图闭环（2026-07-26）

- 合并审查基线：`txym` 已合并 UI-009 的 four-player Track 变化后，先运行全量 EditMode，`Artifacts/UI-010/EditMode/EditModeResults.xml` 为 `130 passed / 0 failed / 0 skipped`。该入口在 XML 完整写入后等待 Unity 正常退出；本次记录为 `forced-stop-after-results; graceSeconds=20`，结果 XML 已成功解析，不能误写为 Unity 自然退出。
- `BattleHudSceneIntegrationPlayModeTests` 实际重载 `SampleScene`，覆盖自动挂载、两个 HUD 根的全屏锚定、五槽横向卡片几何与真实贴图、左侧头像卡和断线图标、断线时仍使用普通生命背景、远端观察只读快照、远端单位信息面板与玩家列表互斥、观察时阵型命令锁定、本地 ready 只锁阵型不锁商店、战斗阶段 Player4 切换到 `MatchCD`，以及战斗完成后观察目标和 ready 重置。断线生命背景修正先产生 `1 failed` 的 Red，再以 `Artifacts/BattleHudVisualAudit/green-disconnect-health/PlayModeResults.xml` 定向复测为 `1 passed / 0 failed / 0 skipped`。
- 最终全量测试：`Artifacts/BattleHudVisualAudit/final-validation-3/EditMode/EditModeResults.xml` 为 `131 passed / 0 failed / 0 skipped`，结果写入后超过 20 秒退出宽限而被脚本终止；`Artifacts/BattleHudVisualAudit/final-validation-3/PlayMode/PlayModeResults.xml` 为 `20 passed / 0 failed / 0 skipped`，Unity 正常退出。新增 EditMode 覆盖赤金不足时升级入口不得进入无效二次确认。
- 全量 PlayMode：`Artifacts/UI-010/PlayMode-rerun/PlayModeResults.xml` 为 `20 passed / 0 failed / 0 skipped`。一次初跑发现旧 UI-005 测试仍断言赤金/生命为 `--`；这与本地测试数据和 UI-010 的显示规则冲突，故将该断言更新为 `7`/`400` 并复跑通过。
- Windows Standalone：既有构建入口输出到忽略目录 `Artifacts/BattleHudVisualAudit/iteration-4/WindowsStandalone/`。构建日志 `Artifacts/BattleHudVisualAudit/iteration-4/windows-build.log` 记录 `result=Succeeded`、`errors=0`、`warnings=1`；唯一警告仍是既有 `TagRegistry.freezeAppend` 未使用。没有修改场景、Prefab、Package 或项目设置。
- 截图入口：以可见 Windows Player 运行 `-battleHudCapture -uiCaptureOutput <dir>`，连续四轮生成并逐张检查关键截图。第四轮 `Artifacts/BattleHudVisualAudit/iteration-4/captures/` 含 `17` 张 `1920×1080` PNG 和 `battle-hud-manifest.json`；Player 正常退出 `0`，日志没有捕获失败、运行时异常或空白图。已核对准备关闭、商店打开、买不起、购买确认、冻结、真实空槽、升级确认、已准备、观察/返回、远端信息面板互斥、断线、两场战斗 Home/Away 与共同 Tick 状态；后两张对照图的 manifest 均为 Tick `11.66647419333458`。
- 截图闭环修正：修前基线确认商店和玩家列表以默认 `100×100` 根节点为坐标系、商品为纵向白色调试行且 Texture 贴图加载失败；第一轮修正为全屏根、真实卡片和兼容贴图加载；第二轮根据截图把等级按钮视觉中心与设置按钮对称，并补齐远端单位信息面板截图状态；继续逐图核对发现“空槽”证据实际仍处于购买确认且不足赤金可被调试入口置为升级确认，第三轮修正命令前置可行性检查与截图顺序后，manifest 明确记录槽 `0:empty`、赤金 `1`；独立审查又发现两张“共同 Tick”截图间时钟仍在推进且断线误用扣血背景，第四轮在截图前暂停共享演示时钟，并将断线表现恢复为普通生命背景加断线图标。截图只证明可见结果，权威状态仍由测试断言和 manifest 共同核对。
- 仍需人工鼠标检查：实际点击五张商品卡、冻结/刷新/升级/准备按钮及四张玩家头像，确认点击热区与视觉一致；连续观察 Spine 动画、Home/Away 朝向和切换瞬间是否自然。自动截图使用真实运行状态但通过代码发起命令，不等价于人工鼠标流程。

## 36. 正式 HUD 视觉修正与字体比较（2026-07-26）

- 商店命令 TDD：`Artifacts/BattleHudVisualAudit/bulk-shop-red/controller/EditModeResults.xml` 与 `state/EditModeResults.xml` 各按预期出现 `2` 项失败，分别暴露冻结只影响单槽、刷新仍进入二次确认和领域层缺少批量状态入口；实现后 `bulk-shop-green/controller/EditModeResults.xml` 为 `10 passed / 0 failed / 0 skipped`，`bulk-shop-green/state/EditModeResults.xml` 为 `13 passed / 0 failed / 0 skipped`。
- 布局 TDD：`layout-red/player-list-2/EditModeResults.xml` 与 `layout-red/scene/PlayModeResults.xml` 各按预期 `1` 项失败，证明共享 `bg_player_list`、内部生命几何、商店居右、升级价格背景、字体和准备图标映射尚未接入；实现后 `layout-green/player-list-strong/EditModeResults.xml` 为 `6 passed / 0 failed / 0 skipped`，`layout-green/scene/PlayModeResults.xml` 为 `1 passed / 0 failed / 0 skipped`。
- 字体 A/B：使用同一份源代码、固定测试数据和 `1920×1080` 截图入口分别构建 `font-hanyi` 与 `font-fangzheng` Windows Player，两次构建均 `Succeeded`、`errors=0`。逐图比较 `02_shop_open`、`06_shop_frozen` 和 `08_ready_shop_still_available` 后，汉仪粗黑简在 `16～24px` 中文商品名与按钮标签上更接近参考图的粗度，且没有图标碰撞；因此最终选择仓库已有 `Assets/Resources/Fonts/HanYiCuHeiJian-1.ttf`，未下载字体、未新增依赖。
- 截图复查发现玩家生命虽已移入头像并使用数字字体，但默认 `Text` 垂直裁剪使 `400` 完全不可见。`life-overflow-red/PlayModeResults.xml` 先以 `1 failed` 固化该缺陷；放开该生命文本的横纵溢出后，`life-overflow-green/PlayModeResults.xml` 为 `1 passed / 0 failed / 0 skipped`。最终局部截图 `Artifacts/BattleHudVisualAudit/final/player-list-crop.png` 可见四个头像内部的 `400`。
- 最终截图：`Artifacts/BattleHudVisualAudit/final/captures/` 包含 `17` 张互不重名的 `1920×1080` PNG 和结构化 manifest；Player 日志记录 `[BattleHudCapture][completed] count=17` 并正常退出 `0`。本地解析实际图片和 manifest 后确认：冻结状态的五个非空槽均为 `frozen=True`，购买状态含真实空槽，全部 Battle 状态均隐藏商店，两张共同 Tick 截图均为 `11.999635696411133`，且每条记录都含等级、商店、准备、首尾玩家、信息面板和状态栏矩形。
- 最终全量测试：`Artifacts/BattleHudVisualAudit/final/EditMode/EditModeResults.xml` 为 `135 passed / 0 failed / 0 skipped`；独立审查补齐升级费用背景中心断言后，`Artifacts/BattleHudVisualAudit/final/PlayMode-review/PlayModeResults.xml` 为 `20 passed / 0 failed / 0 skipped`。两次 Unity 均在结果写入后正常退出。
- 严格 Windows x64 构建：`Artifacts/BattleHudVisualAudit/final/windows-build.log` 记录 `result=Succeeded`、`errors=0`、`warnings=1`，输出到 `Artifacts/BattleHudVisualAudit/final/WindowsStandalone/ARKnoNIGHTS.exe`。唯一警告仍为既有 `TagRegistry.freezeAppend` 未使用，与本轮 HUD 修改无关。
- 尚未自动验证：真实鼠标点击热区、连续悬停效果、按钮在不同 Windows DPI/宽高比下的视觉一致性，以及长时间观察 Spine 动画切换。自动截图命令直接调用正式命令入口，不能替代这些人工交互检查。

## 37. 商店放大与视觉中心修正（2026-07-26）

- Red 基线：`Artifacts/BattleHudVisualAudit/requested-adjustment/red-editmode/EditModeResults.xml` 为 `135 total / 133 passed / 2 failed / 0 skipped`，两项失败分别确认旧商店仍为 `1070×280`、左侧玩家列表仍保留 `24px` 边距；`requested-adjustment/red-playmode/PlayModeResults.xml` 为 `1 failed`，确认场景尚未满足新几何约束。
- 最终 EditMode：`Artifacts/BattleHudVisualAudit/requested-adjustment/final-editmode/EditModeResults.xml` 为 `135 passed / 0 failed / 0 skipped`。新增断言覆盖商店 `1.5×` 尺寸、等级按钮与商店上/右边缘连接、准备按钮中垂线、玩家列表 `8px` 左边距及 `0.85×` 行尺寸。
- 最终 PlayMode：`Artifacts/BattleHudVisualAudit/requested-adjustment/final-playmode/PlayModeResults.xml` 为 `20 passed / 0 failed / 0 skipped`。场景集成断言覆盖五张 `237×262.5` 商品卡、`12px` 间距、刷新价格背景、三类价格文字 `3px` 视觉上移、冻结/刷新内容 `9px` 视觉上移、两种等级数字颜色、准备按钮与资源面板共中垂线，以及玩家头像缩放。
- 严格 Windows x64 构建：`Artifacts/BattleHudVisualAudit/requested-adjustment/iteration-1/WindowsBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=1`，输出到同目录 `WindowsStandalone/ARKnoNIGHTS.exe`；唯一警告仍为既有未使用字段。
- 可见 Player 截图：`Artifacts/BattleHudVisualAudit/requested-adjustment/iteration-1/captures/` 包含 `17` 张互不重复的 `1920×1080` PNG 和结构化 manifest。已逐图检查商店打开、全部非空槽冻结和已准备状态；manifest 记录等级按钮 `1735,30,130,120`、商店 `260,150,1605,420`、准备按钮 `1740,620,180,60`、首名玩家 `8,164,98.6,107.1`，并确认冻结截图中五个非空槽均为 `frozen=True`。Player 日志记录 `[BattleHudCapture][completed] count=17`，未发现捕获失败或运行时异常。
- 尚未自动验证：真实鼠标点击热区、不同 Windows DPI/非 `16:9` 分辨率下的视觉观感，以及用户对本轮 `1.5×` 商店和 `0.85×` 玩家头像最终大小的主观确认。PlayMode 会在 `4:3` 测试画布验证右锚定关系，但不能替代目标 `1920×1080` 的截图判断。

## 38. 六槽商店、战斗期操作与玩家列表纠正（2026-07-26）

- Red 基线：`Artifacts/BattleHudVisualAudit/requested-adjustment-2/red-editmode/EditModeResults.xml` 为 `136 total / 10 failed / 0 skipped`，失败均来自六槽领域数据、战斗期商店命令、固定玩家列表背景及新几何断言；`red-playmode-8/PlayModeResults.xml` 为 `1 failed`，首个失败明确显示背景仍被错误缩小为 `98.6×453.9`，而期望为 `116×534`。
- 最终 EditMode：`Artifacts/BattleHudVisualAudit/requested-adjustment-2/green-editmode/EditModeResults.xml` 为 `136 passed / 0 failed / 0 skipped`。覆盖六槽加载、购买/刷新/冻结、战斗阶段购买/刷新/冻结/升级、商店左移以及玩家条目在固定背景内的尺寸和间距。
- 最终 PlayMode：第一次 Green 仅因旧的“商店右边缘与等级按钮共线”断言失败，实际正好按新规则左移 `10`；更新为本轮明确几何后，`Artifacts/BattleHudVisualAudit/requested-adjustment-2/green-playmode-2/PlayModeResults.xml` 为 `20 passed / 0 failed / 0 skipped`。场景断言覆盖六张商品卡、三类费用位置、刷新费用贴底、升级确认渐变、商店/单位面板双向互斥、战斗期等级入口与完整商店命令。
- 严格 Windows x64 构建：`Artifacts/BattleHudVisualAudit/requested-adjustment-2/iteration-1/WindowsBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=1`，输出到同目录 `WindowsStandalone/ARKnoNIGHTS.exe`。唯一警告仍为既有未使用字段。
- 可见 Player 截图：`Artifacts/BattleHudVisualAudit/requested-adjustment-2/iteration-1/captures/` 包含 `17` 张互不重复的 `1920×1080` PNG 和结构化 manifest。逐图核对确认：`bg_player_list` 保持 `116×534`；四个 `Player_*` 条目为 `98.6×107.1`、左侧 `x=16.7`，以 `13` 间距在背景内居中；商店为六槽且整体左移 `10`；商品/升级费用下移、商品费用左移、刷新费用贴底和升级确认渐变均可见；单位信息面板出现时商店关闭；`12_battle_match_ab_home.png` 明确记录并显示战斗阶段商店已打开。manifest 的所有记录均含 `6` 个商店槽，Player 日志记录 `[BattleHudCapture][completed] count=17`，未发现捕获失败、空白图或运行时异常。
- 尚未自动验证：真实鼠标连续执行战斗期购买、刷新、冻结和升级；六张商品卡及越过商店面板左边缘的升级按钮在非 `16:9`、不同 DPI 下的点击热区；不同分辨率下玩家头像条目间距的主观观感。自动测试和截图使用正式命令入口，但不能替代上述人工鼠标检查。

## 39. 商店纵向位置、按钮贴边与方正字体修正（2026-07-26）

- Red：`Artifacts/BattleHudVisualAudit/requested-adjustment-3/red-editmode/EditModeResults.xml` 为 `136 total / 2 failed / 0 skipped`，分别证明玩家行仍在 `x=16.7`、商店仍未上移 `40`。随后同一个场景 PlayMode 按断言顺序依次在 `red-playmode-buttons`、`red-playmode-refresh-cost`、`red-playmode-font` 中暴露冻结/刷新按钮旧位置、刷新费用背景仍以中心贴边，以及中文仍为汉仪字体。
- Green：`Artifacts/BattleHudVisualAudit/requested-adjustment-3/green-editmode/EditModeResults.xml` 为 `136 passed / 0 failed / 0 skipped`；`green-playmode/PlayModeResults.xml` 为 `20 passed / 0 failed / 0 skipped`。断言覆盖玩家行 `x=15.7`、商店面板上移 `40`、冻结右移 `10`、刷新右移 `15`、刷新费用背景底边贴按钮底边，以及商店/准备中文统一使用 `FangZhengHeiTiJianTi-1`。
- Windows x64：`Artifacts/BattleHudVisualAudit/requested-adjustment-3/iteration-1/WindowsBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=1`，输出到同目录 `WindowsStandalone/ARKnoNIGHTS.exe`；唯一警告仍为既有 `TagRegistry.freezeAppend` 未使用。
- 截图：同目录 `captures/` 包含 `17` 张 `1920×1080` PNG 和结构化 manifest。`02_shop_open.png` 已逐项复核方正字体、商店及按钮位置和刷新价格贴边；manifest 记录商店屏幕矩形为 `(250,110,1605,420)`、首名玩家为 `(15.7,197.3,98.6,107.1)`，并记录 `[BattleHudCapture][completed] count=17`，未发现运行时异常或空白图。
- 尚未自动验证：真实鼠标点击在新按钮位置的主观手感，以及非 `16:9`、不同 DPI 下字体清晰度和按钮阴影的视觉中心。

## 40. 冻结/刷新下移与退出玩家头像替换（2026-07-26）

- Red：`Artifacts/BattleHudVisualAudit/requested-adjustment-4/red-playmode/PlayModeResults.xml` 以 `1 failed` 证明冻结和刷新按钮仍位于旧的局部 `y=0`；`red-editmode` 与 `red-editmode-overlay` 分别证明领域快照尚无独立退出状态、退出行尚未显式抑制掉线覆盖图。固定 fixture 验收开始后，`red-fixture-editmode/EditModeResults.xml` 为 `7 total / 2 failed / 0 skipped`，两项失败分别确认 Player3 尚未作为掉线样本、Player4 尚未作为退出样本。
- Green：`Artifacts/BattleHudVisualAudit/requested-adjustment-4/final-editmode/EditModeResults.xml` 为 `137 passed / 0 failed / 0 skipped`；`final-playmode/PlayModeResults.xml` 为 `20 passed / 0 failed / 0 skipped`，两次 Unity 均在结果写入后正常退出。该轮按当时解释断言冻结/刷新按钮局部 `y=-20`、Player3 继续显示掉线覆盖图，以及 Player4 使用 `equip_replace_avatart_bg` 且不创建 `LostConnection` 子物体；其中退出玩家不显示掉线图标的解释已被第 41 节的用户纠正取代。
- Windows x64：`Artifacts/BattleHudVisualAudit/requested-adjustment-4/iteration-1/WindowsBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=2`，输出到同目录 `WindowsStandalone/ARKnoNIGHTS.exe`。日志中的唯一不同警告文本仍是既有 `TagRegistry.freezeAppend` 未使用；本轮没有新增编译诊断。
- 截图：同目录 `captures/` 包含 `17` 张 `1920×1080` PNG 和结构化 manifest，Player 日志记录 `[BattleHudCapture][completed] count=17`。已检查 `02_shop_open.png` 的冻结/刷新整体下移效果，以及 `11_player_disconnect_and_exit_list.png` 中 Player3 的掉线头像和 Player4 的退出替换头像；未发现截图失败、空白图或运行时异常。
- 尚未自动验证：联网系统真实触发“已退出”的状态迁移、退出后是否仍可观察或参与后续配对，以及真实鼠标在按钮新位置的点击手感；当前本地 Demo 仅从固定 JSON 读取该显示状态。

## 41. 退出玩家掉线图标层级与商店按钮再次下移（2026-07-26）

- Red：`Artifacts/BattleHudVisualAudit/requested-adjustment-5/red-editmode/EditModeResults.xml` 为 `1 failed`，确认旧投影错误地用 `HasExited` 抑制掉线图标；`red-playmode/PlayModeResults.xml` 为 `1 failed`，确认真实场景中的退出玩家缺少 `LostConnection`，且冻结/刷新仍位于旧的局部 `y=-20`。
- 针对性 Green：未运行全量测试。`green-editmode/EditModeResults.xml` 为 `1 passed / 0 failed / 0 skipped`；`green-playmode/PlayModeResults.xml` 为 `1 passed / 0 failed / 0 skipped`。场景断言同时核对 Player4 的替换头像、`LostConnection` 子物体晚于头像绘制，以及冻结/刷新按钮局部 `y=-30`。
- Windows x64：`Artifacts/BattleHudVisualAudit/requested-adjustment-5/iteration-1/WindowsBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=0`。
- 截图：同目录 `captures/02_shop_open.png` 和 `11_player_disconnect_and_exit_list.png` 已人工复核；冻结/刷新相对上一轮继续下移 `10`，Player4 的 `icon_lost_connect` 可见于 `equip_replace_avatart_bg` 之上。Player 日志记录 `[BattleHudCapture][completed] count=17`，没有捕获失败或运行时异常。
- 未验证：本轮按用户要求未运行全量 EditMode/PlayMode；联网系统触发退出的真实流程和真实鼠标热区仍未验证。

## 42. 刷新免费费用 UI-only 接口（2026-07-26）

- Red：`Artifacts/BattleHudVisualAudit/refresh-free-interface/red-playmode/PlayModeResults.xml` 为 `1 failed`；真实 `SampleScene` 已有刷新费用节点，但 `ShopReadyHudController` 尚无可调用的免费表现接口。
- 针对性 Green：`green-playmode/PlayModeResults.xml` 为 `1 passed / 0 failed / 0 skipped`。测试直接调用正式控制器的 `SetRefreshFreePresentation(true)`，确认背景切换为 `cost_free`、费用数字隐藏且玩家赤金不变；再调用 `false`，确认恢复 `cost_bg_1` 与费用数字。
- 未运行全量测试或新一轮截图构建。该接口默认关闭，当前截图流程与实际刷新扣费行为不变。

## 43. HUD 更新最终全量回归与提交前验收（2026-07-26）

- 全量 EditMode：`Artifacts/BattleHudVisualAudit/final-regression/EditMode/EditModeResults.xml` 为 `137 passed / 0 failed / 0 skipped`。结果 XML 完整写入后 Unity 超过 `20` 秒退出宽限，由测试脚本只终止该次测试进程；测试结果已成功解析。
- 全量 PlayMode：`Artifacts/BattleHudVisualAudit/final-regression/PlayMode/PlayModeResults.xml` 为 `20 passed / 0 failed / 0 skipped`，Unity 在结果写入后正常退出。
- 严格 Windows x64：`Artifacts/BattleHudVisualAudit/final-regression/WindowsBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=1`，输出为 `WindowsStandalone/ARKnoNIGHTS.exe`。唯一警告是既有 `TagRegistry.freezeAppend` 未使用。
- 可见 Player 截图：`Artifacts/BattleHudVisualAudit/final-regression/captures/` 包含 `17` 张 `1920×1080` PNG 和结构化 manifest；日志记录 `[BattleHudCapture][completed] count=17`。已重点复核商店、冻结/刷新位置、Player3 掉线表现及 Player4 退出替换头像与掉线图标层级，未发现捕获失败或运行时异常。
- 提交前范围检查：新退出头像贴图与 `.meta` 成对保留；`.superpowers/` 和 `docs/bonds/` 属于无关未跟踪内容，不纳入 HUD 最终提交。

## 44. Mainline 果冻召唤最终集成验证（2026-07-26）

- 数据、Core、演出与准备阶段的定向证据保存在能力分支的 `.superpowers/sdd/2026-07-26-mainline-jelly-summon/evidence/`。覆盖 5504 目录、能力目录、全局 2 SP/s 的私有技力、Tick 100/250、终局 Tick 不施放、1 格方形固定点 Spawn、下一 Tick 激活、独立索敌、动态轨道、真实 5504 工厂和 Replay。
- 功能分支最终全量验证为 EditMode `159/159`、PlayMode `22/22`，均为非零 XML、零失败、零跳过；批处理编译退出码为 `0`，日志未见 C# 编译错误或未处理异常。合并后必须重新运行相关全量测试，合并结果才是最终验收依据。
- 未验证：交互式 Unity Editor/Windows Player 中对三个 5504 一格分布、Tick 100/250 出现、重新索敌、死亡清理、Home/Away 投影、暂停/变速和 Replay 连续性的人工视觉检查；自动断言不能替代此检查。

## 45. 基础攻击间隔采用源 JSON 一半（2026-07-26）

- 规则与生成：源 `attackIntervalSeconds` 保持不变，目录生成器使用 `ceil(attackIntervalSeconds × 0.5 × 20)` 产生 Core 实际间隔。执行 `D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod UnitCatalogGenerator.Generate -logFile G:\ARKnoNIGHTS_beta\Temp\AttackInterval\CatalogGenerate-AfterTests.log`，进程退出码为 `0`；生成目录中的 `1000/5503/5504` 分别为 `14/40/15 Tick`，攻击动画时长仍为 `20/54/24 Tick`。日志包含 `TASK004A_CATALOG_GENERATED`，未命中 `error CS`、`Compilation failed`、`Scripts have compiler errors` 或 `Unhandled Exception`。
- Core RED：先只修改真实目录和出伤时序断言，运行 `BattleCoreEditModeTests` 得到 `57 total / 2 failed / 0 skipped`。两项失败分别为 gopro 期望 `14` 实际 `28`，以及 arcslma 有效动画期望 `40` 实际 `54`，证明旧目录尚未应用折半规则。
- Core GREEN：生成 `14/40/15` 目录后首次运行得到 `57 total / 1 failed / 0 skipped`。剩余失败不是 Core 计算错误，而是阻挡回归把“真实 arcslma 快照和清理契约”绑定到旧时间线中的固定 `away-5503-alpha`；新时间线中 `home-5503-alpha` 仍在 Tick `27` 建立、Tick `107` 解除阻挡。测试改为从封存输入选择实际参与阻挡的 arcslma，仍严格断言双方权威快照、敌对阵营和对应 `BlockEnded`。复跑结果为 `57 passed / 0 failed / 0 skipped`。
- UI RED/GREEN：`UnitDetailNumberFormatter.AttackInterval(15)` 的 RED 为 `1 total / 1 failed / 0 skipped`，期望 `0.75`、实际 `0.8`；格式从 `0.#` 改为 `0.##` 后为 `1 passed / 0 failed / 0 skipped`，同时验证 `14/40/15 Tick` 显示为 `0.7/2/0.75`。旧 `UnitFactory` 调试适配改为读取共享的 `BaseAttackIntervalSeconds`，不在 UI 内重复折半。
- 固定真实对战的合法结果变化：折半后对战在 Tick `528` 结束，只发生 Tick `100/250/400` 三轮果冻召唤，动态 5504 总数从旧时间线的 `42` 变为 `27`；EditMode/PlayMode 的固定输入断言据此更新。arcslma 原始攻击动画仍为 `54 Tick`，有效动画为 `40 Tick`，Spine `TrackEntry.TimeScale` 为仅包含动画压缩的 `54/40 = 1.35`；全局 `0.5×` 播放速度仍只由 `SkeletonAnimation.timeScale` 应用一次。
- 最终全量 EditMode：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe -TestPlatform EditMode -OutputDirectory Temp\AttackInterval\Full-EditMode-Final -NoGraphics`。`Temp/AttackInterval/Full-EditMode-Final/EditModeResults.xml` 为 `168 passed / 0 failed / 0 skipped`，Unity 正常退出；日志为同目录 `EditMode.log`。
- 最终全量 PlayMode：同一脚本使用 `-TestPlatform PlayMode -OutputDirectory Temp\AttackInterval\Full-PlayMode-2 -NoGraphics`。`Temp/AttackInterval/Full-PlayMode-2/PlayModeResults.xml` 为 `22 passed / 0 failed / 0 skipped`，Unity 正常退出；日志为同目录 `PlayMode.log`。PlayMode 启动会清理项目 `Temp`，因此在 PlayMode 后重新运行 EditMode，并在最后再次执行生成器；生成目录内容没有新增 diff，两份最终 XML/日志和生成日志得以同时保留。
- 最终日志未命中 C# 编译错误、编译失败或未处理异常。未执行 Windows Standalone 构建、交互式 Unity Editor/Player 人工观感检查、真实鼠标检查或不同 DPI/分辨率检查；攻击间隔文本与真实 Spine 压速已由自动化覆盖，但这些未执行项仍标记为“未验证”。

## 46. 单位死亡动画、变黑隐藏与 Track 切入（2026-07-27）

- Track TDD：先只增加死亡点采样、死亡 Tick 切入、连续跨过 Death 和回退重建断言。定向 EditMode 当场结果为 `36 total / 3 failed / 0 skipped`；三项失败分别证明旧采样的 `ShouldDisplay` 在死亡后仍为 `true`、切入死亡 Tick 仍创建死亡单位，以及回退用例的初始死亡节点仍错误创建。把死亡后点采样改为 `ShouldDisplay=false`，并只在“尚无视图记录”时应用创建过滤后，定向结果为 `36 passed / 0 failed / 0 skipped`；已有视图连续跨过 Death 仍只收到一次死亡命令。
- 真实 Spine TDD：`Temp/UnitDeathPresentation/Task2-Red/PlayModeResults.xml` 为 `9 total / 2 failed / 0 skipped`，分别确认旧视图在死亡动画结束后 RGB 不变化、`Dispose` 在帧末销毁前仍保持激活。实现保留死亡 `TrackEntry`、监听 `Complete`、用 `Time.unscaledDeltaTime` 在 `0.5` 秒内线性降低 Skeleton RGB、保持 Alpha，并在结束后停用 GameObject；`Task2-Green/PlayModeResults.xml` 为 `9 passed / 0 failed / 0 skipped`。独立审查后加强同一真实 5504 测试：在私有死亡状态仍为 Animation 时逐帧断言 RGB 不变，在 Blackening 约 `0.2` 秒处断言 RGB 已下降但未归零且 Alpha 不变，并按累计 `Time.unscaledDeltaTime` 断言在 `0.45..0.65` 秒内停用；定向类复跑为 `9 passed / 0 failed / 0 skipped`。
- 正式回合终局 TDD：独立审查发现 `MultiBattlePresentationCoordinator` 在最大 EndTick 派发最终 Death 后同帧 Completed，`PreparationBattleLoopController` 随即 Reset，导致终局死亡动画和变黑都不可见。`Temp/UnitDeathPresentation/Terminal-Red/PlayModeResults.xml` 的正式回合测试按预期为 `1 total / 1 failed / 0 skipped`，失败点是跳到终局后 Phase 已错误回到 Preparation。实现为 `IBattlePresentationView` 增加默认 `false` 的待完成终局表现契约，真实 Spine 视图在 Animation/Blackening 上报，Track 播放器聚合，协调器在最大 EndTick 保持 Playing 直到聚合值清零；定向正式回合 PlayMode、协调器 EditMode 分别为 `1 passed / 0 failed / 0 skipped`。协调器测试还覆盖待完成期间暂停不进入 Completed、完成后 Resume 才完成；缺动画或中断隐藏因视图立即进入 Hidden，不产生固定等待。
- 第一次修复后的全量 PlayMode 为 `24 total / 1 failed / 0 skipped`：既有 `BattleHudSceneIntegrationPlayModeTests` 仍固定等待两帧后断言观察目标已复位，与新增的终局表现等待语义冲突。测试改为先等待 Phase 进入 Preparation，再额外等待一帧让 `BattleHudSceneCoordinator` 清除临时观察目标；定向复跑为 `1 passed / 0 failed / 0 skipped`，没有为此修改生产逻辑或降低断言。
- 复审防御性生命周期 TDD：若播放器之外直接停用正在死亡的 GameObject，旧实现仍报告 pending，定向 PlayMode 为 `10 total / 1 failed / 0 skipped`。`OnDisable`/`OnDestroy` 现在把视图转为 Hidden 并退订 TrackEntry，避免已无法 Update 的对象永久阻塞正式回合；定向复跑为 `10 passed / 0 failed / 0 skipped`。
- 最终全量 PlayMode：`Temp/UnitDeathPresentation/Final-PlayMode-3/PlayModeResults.xml` 为 `25 passed / 0 failed / 0 skipped`，Unity 在结果写入后正常退出。
- 最终全量 EditMode：PlayMode 启动按项目既有行为清理项目 `Temp`，因此在最终 PlayMode 后运行并保留 `Temp/UnitDeathPresentation/Final-EditMode-AfterPlay-2/EditModeResults.xml`；结果为 `171 passed / 0 failed / 0 skipped`，Unity 在结果写入后正常退出。两份最终日志均未命中 `error CS`、`Compilation failed`、`Scripts have compiler errors`、未处理异常、`NullReferenceException` 或本次新增的死亡中断/缺 Skeleton 诊断。
- Windows x64 StrictMode：使用既有 `Task006StandaloneBuild.BuildWindowsX64`、唯一启用场景 `Assets/Scenes/SampleScene.unity` 构建到忽略目录 `Artifacts/UnitDeathPresentation/WindowsStandalone/ARKnoNIGHTS.exe`。Unity 进程退出码为 `0`；`Artifacts/UnitDeathPresentation/WindowsStandaloneBuild.log` 记录 `result=Succeeded`、`platform=StandaloneWindows64`、`errors=0`、`warnings=0`、总大小 `164976837` 字节。产物共 `143` 个文件，主 EXE SHA-256 为 `F49CDCB9E27CF2AA5C6F63BC961B4364E93363071533661593549AEC421AD60B`。
- Windows Player 正式流程冒烟：直接启动上述生产 Player，`Artifacts/UnitDeathPresentation/PlayerFormalLoopSmoke.log` 依次记录 `Preparation`、`ui009-round-1` 两场 Track 封存且 `state=Playing`、`battle.completed`、再次进入 `Preparation`，并记录 `playersUnchangedDuringBattle=True`；命中完整回合证据后只终止本轮启动的 PID `54584`。日志未命中未处理异常、`NullReferenceException`、阶段错误或本次死亡表现诊断。旧 `-task006-acceptance` 因正式模式明确阻止 legacy `BattleDemoController.StartOrContinue` 而退出 `1`；隐藏窗口下 `-battleHudCapture` 因首张截图不可见而退出 `1`，两者均未作为当前正式流程通过证据。
- 范围检查：没有修改 Core、Death 事件格式、目录 JSON、场景、Prefab、Shader、Package 或 ProjectSettings；`.superpowers/` 与 `docs/bonds/` 仍是用户的无关未跟踪内容。
- 人工验收：项目负责人于 2026-07-27 反馈整体人工观感“还行”，据此记录本次死亡动画、变黑与隐藏的整体视觉验收为可接受。尚未分别记录多单位同时死亡、Home/Away 切换瞬间以及不同 DPI/分辨率的专项人工验收；这些细分项仍不能由自动 RGB、激活状态和 Track 断言完全替代。

## 47. 精英变体源数据与 Texture2D 单位头像（2026-07-27）

- 目录生成：执行 `D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod UnitCatalogGenerator.Generate -logFile G:\ARKnoNIGHTS_beta\Temp\UnitEliteVariants\CatalogGenerate.log`，日志记录 `TASK004A_CATALOG_GENERATED ... units=3 summary=1000:20|5503:54|5504:24`，未命中 C# 编译错误、编译失败或未处理异常。连续两次生成的 `Assets/Resources/BattleData/unit-catalog-v1.json` SHA-256 均为 `359C81D56AB89EA735FAFCD0F2A6CA243076DE7C72A9086B7E4097B6B728B0AA`。
- 精英变体解析器：`Temp/UnitEliteVariants/Resolver-Final/EditModeResults.xml` 为 `5 passed / 0 failed / 0 skipped`，覆盖精英 0/1 基础选择、精英 2/3 专用模型与名称、缺少较高条目时只向低级继承、块级继承、显式空能力列表和非法模型块。
- 真实目录与头像聚焦回归：`Temp/UnitEliteVariants/BattleCore-Final-2/EditModeResults.xml` 为 `57 passed / 0 failed / 0 skipped`；`Temp/UnitEliteVariants/TDD-Portrait-Green/EditModeResults.xml` 为 `1 passed / 0 failed / 0 skipped`，确认精英 0 目录为猎狗 `820 HP / 190 ATK`、基础 Skeleton 路径，且 Default Texture 头像只经 `Texture2D` 加载后创建一次缓存 Sprite。
- 全量 EditMode 首次运行 `177 total / 8 failed / 0 skipped`。其中一项仍期待精三名称，另七项的 Tick 100 召唤夹具把真实 1000 的旧精三生命值当作保活条件；精零 820 HP 使战斗提前终局。测试改为期待精零名称，并只在召唤夹具中克隆一个显式高生命陪练，未修改生产战斗规则。完成前在最新提交状态复跑 `Artifacts/UnitEliteVariants/Final-EditMode/EditModeResults.xml`，结果为 `177 passed / 0 failed / 0 skipped`，Unity 正常退出。
- PlayMode：完成前在最新提交状态运行 `Artifacts/UnitEliteVariants/Final-Presentation-PlayMode/PlayModeResults.xml`，结果为 `10 passed / 0 failed / 0 skipped`；`Artifacts/UnitEliteVariants/Final-Demo-PlayMode/PlayModeResults.xml` 为 `2 passed / 0 failed / 0 skipped`，两次 Unity 均正常退出。播放层首次同样因旧精三保活夹具产生 `10 total / 1 failed / 0 skipped`，按上述隔离方式修正后通过。
- Windows x64 StrictMode：通过环境变量把输出从 Unity 会自动清理的项目 `Temp` 改到忽略目录 `Artifacts/UnitEliteVariants/WindowsStandalone/ARKnoNIGHTS.exe`。Unity 进程退出码为 `0`；`Artifacts/UnitEliteVariants/WindowsStandaloneBuild.log` 记录 `result=Succeeded`、`platform=StandaloneWindows64`、`errors=0`、`warnings=0`、总大小 `165680381` 字节。产物共 `143` 个文件，主 EXE 为 `666624` 字节，SHA-256 为 `F49CDCB9E27CF2AA5C6F63BC961B4364E93363071533661593549AEC421AD60B`。
- 尚未实现或验证：精英 2/3 的运行时目录选择、精英 1 数值系数、合成系数、局内升阶，以及新头像在所有目标分辨率/DPI 下的专项人工视觉检查。当前运行时只保证精英 0。
- 初始实现提交未创作或暂存工作区中已有的 `1000_gopro` 基础/`_2`/`_3` 模型资源、三个头像及其 `.meta`、基础 `1000_gopro.json` 变更。项目负责人随后明确要求一并提交；资源与 `.meta` 已完成配对和 GUID 唯一性检查，并在全量 EditMode 与 Windows x64 构建通过后纳入版本控制。`.superpowers/` 与 `docs/bonds/` 仍保持在本次提交之外。

## 48. 玩家商店刷新排序与整轮被动刷新（2026-07-28）

- Task 1 EditMode Red：`Artifacts/ShopRefreshSorting/Task1-Red/EditModeResults.xml` 为 `15 total / 11 passed / 4 failed / 0 skipped`，四项失败分别暴露旧实现未提供领域排序器、初始四玩家商店未彼此独立、初始加载顺序不符合夹具，以及付费主动刷新仍保留冻结商品。Green：`Artifacts/ShopRefreshSorting/Task1-Green/EditModeResults.xml` 为 `15 passed / 0 failed / 0 skipped`；日志分别为同目录 `EditMode.log`。
- Task 2 EditMode Red：`Artifacts/ShopRefreshSorting/Task2-Red/EditModeResults.xml` 为 `16 total / 15 passed / 1 failed / 0 skipped`，失败项确认领域层尚无整轮四玩家被动刷新操作。Green：`Artifacts/ShopRefreshSorting/Task2-Green/EditModeResults.xml` 为 `16 passed / 0 failed / 0 skipped`；该测试覆盖免费刷新、四个独立商店、冻结商品保留原物理槽位，以及其余槽按排序结果填入；日志分别为同目录 `EditMode.log`。
- Task 3 PlayMode Red：`Artifacts/ShopRefreshSorting/Task3-Red/PlayModeResults.xml` 为 `1 total / 0 passed / 1 failed / 0 skipped`；演出完成前商店未刷新且本地赤金未改变的前置断言已执行通过，返回 Preparation 后在四店刷新结果断言处失败。Green：`Artifacts/ShopRefreshSorting/Task3-Green/PlayModeResults.xml` 为 `1 passed / 0 failed / 0 skipped`，确认演出完成前仍不刷新、回到 Preparation 后四名玩家商店一起刷新且本地赤金保持不变；日志分别为同目录 `PlayMode.log`。
- 最终全量 EditMode：命令脚本退出码为 `0`，`Artifacts/ShopRefreshSorting/Final-EditMode/EditModeResults.xml` 为 `187 total / 187 passed / 0 failed / 0 skipped`，Unity 在写入结果后正常退出；日志为 `Artifacts/ShopRefreshSorting/Final-EditMode/EditMode.log`。
- 初次最终全量 PlayMode：命令脚本退出码为 `1`，`Artifacts/ShopRefreshSorting/Final-PlayMode/PlayModeResults.xml` 为 `25 total / 24 passed / 1 failed / 0 skipped`，Unity 在写入结果后正常退出。唯一失败为 `StagingHudScenePlayModeTests.SampleScene_FormalHudUsesExistingHudAndShowsLoopOwnedSessionValues`：当前精英 0 目录和对应 EditMode 断言使用名称“猎狗”，该旧 PlayMode 断言仍期待精英 3 名称“狂暴的猎狗pro”；定向复现 `Artifacts/ShopRefreshSorting/PlayMode-Failure-Repro/PlayModeResults.xml` 同样为 `1 failed / 0 skipped`。这两份结果保留为过时期望修正前的 Red 证据。
- Fix Round 1 经项目负责人明确批准，只把上述测试的过时名称期望改为当前权威“猎狗”，不修改生产代码或降低其他断言。聚焦 Green 的命令脚本退出码为 `0`，`Artifacts/ShopRefreshSorting/FixRound1-Focused-PlayMode/PlayModeResults.xml` 为 `1 passed / 0 failed / 0 skipped`；随后当前检出的全量 PlayMode 命令脚本退出码为 `0`，`Artifacts/ShopRefreshSorting/FixRound1-Full-PlayMode/PlayModeResults.xml` 为 `25 passed / 0 failed / 0 skipped`。两次 Unity 均在写入结果后正常退出，日志分别为对应目录的 `PlayMode.log`。
- Windows x64 StrictMode：同步构建进程退出码为 `0`；`Artifacts/ShopRefreshSorting/WindowsBuild.log` 记录 `[TASK-006][build.succeeded] result=Succeeded`、`platform=StandaloneWindows64`、`errors=0`、`warnings=0`、`totalSize=279058885`，输出 `Artifacts/ShopRefreshSorting/WindowsStandalone/ARKnoNIGHTS.exe` 存在。项目负责人在 Fix Round 1 明确批准本任务的 Windows 证据合同为“同步退出码 `0` + 上述结构化成功日志 + EXE 存在”；项目内重复摘要文件不属于本任务验收要求，因此未修改 `Task006StandaloneBuild`，也未为生成摘要单独重跑构建。
- 日志扫描：最终 EditMode、Fix Round 1 聚焦/全量 PlayMode 与 Windows 构建日志均未命中 `error CS`、`Compilation failed`、`Scripts have compiler errors`、`Unhandled Exception`、`NullReferenceException` 或 `round.observer.switch.failed`。
- 范围检查：Task 4 原提交只修改三份权威文档；Fix Round 1 只修改过时的 PlayMode 名称期望和本节测试记录。未修改生产代码、商店 UI、场景、Prefab、Package、ProjectSettings，也未纳入工作区中既有的 ShopReady、Bonds、经济文档或大量角色资源改动。
- 未验证：交互式 Editor/Windows Player 中用真实鼠标观察刷新后的卡片视觉顺序，以及未来按玩家等级概率随机生成商品的实现。

## 50. BONDS 单位资源与完整 v2 authored 数据导入（2026-07-29）

- 数据范围：`Assets/GameData/Units/EliteVariants/Json` 含 `100` 份 `unit-elite-variants-v2` 文档和 `185` 个模型变体，组成是当前 BONDS `99` 个 TypeId / `182` 个资源变体加保留的 legacy/demo `1000` 三个变体；`1021` 已排除。100 份 JSON 与 100 份 `.meta` 成对，禁用字段、`Default` key/name 绑定扫描为零匹配。
- 确定性生成器：`scripts/tests/Test-BondsUnitEliteVariantsV2Export.ps1` 连续生成两次并比较哈希，结果均为 `documents=97 / variants=180`，与保留的 `1000/5503/5504` 合并后为 `100` 份文档。测试覆盖统一 `deploymentCost=2`、四种 `actionMethod`、不攻击/不可阻挡集合、完整变体数值与资源身份、1322 反向源映射、1116 三状态动画和全部非基础动画时长。生成器同时拒绝 level 0 核心数值缺失、`null`、非数字或非有限值。
- 定向 EditMode 的首次有效 XML 为 `56 total / 54 passed / 2 failed / 0 skipped`：旧动画样本仍查找已退出 BONDS 的 `1000`；头像 PNG 原文件均为 `158×158`，但 Unity 默认 NPOT 导入把 `10001_trslim` 缩为 `128×128`。修正样本并加入统一 Default Texture + `TextureImporterNPOTScale.None` 导入规则后，`Artifacts/UnitDataImport20260729/Targeted-EditMode-Green/EditModeResults.xml` 为 `56/56` 通过；XML/日志 SHA-256 分别为 `D4C58F68540D7AB0848372961B6663B5A99A510C1732688BEBE337CE2E457C29`、`79FD4B0E240C5A68FF8DC775F556B9C1F2A5985AAB624487902E8B7B4849471C`。覆盖全部 `185` 个变体的真实 Spine 动画/时长、`182` 个 BONDS 资源、`185` 张加载后仍为 `158×158` 的头像、100 份 v2 源和隔离的三单位 legacy v1 投影。
- `UnitFactory` 定向 PlayMode：`Artifacts/UnitDataImport20260729/Targeted-PlayMode-UnitFactory/PlayModeResults.xml` 为 `2/2` 通过，确认 legacy/debug 适配器一次加载全部 `100` 个精英 0 模板与 SkeletonDataAsset，并为 `10002` 保持空攻击动画；XML/日志 SHA-256 分别为 `3821FE91DD17E548C5DB32614CE4C81FFAB411B5B4B2C5E8CBB12B7EAAF9D7B0`、`D878BA8B966D2FCFEBCF1EF62EFAA767B6F337488CE60ED124501484F2C26AD4`。
- 全量 Unity：`Artifacts/UnitDataImport20260729/Full-EditMode/EditModeResults.xml` 为 `236 passed / 0 failed / 0 skipped`，`Full-PlayMode/PlayModeResults.xml` 为 `27 passed / 0 failed / 0 skipped`；两次 Unity 均在结果落盘后正常退出。EditMode XML/日志 SHA-256 为 `708CF77827B91A64AE8352499E9CDA410956ECDAD3D733BCACD6278BA8CBB55E`、`05DC92C205D8407B8680255BD1BD6650FA8D8E1DF017B78CED5A7DF65920D1FA`，PlayMode 为 `357F132C60A76222C8FD686E87FB0C21813E41153EF656CD4183F2B42D50681F`、`3BED73556BAF9533E99EDA1F2A006460E3FF0B94A98AAE123154CBF42176785E`。
- Windows x64 StrictMode：使用 `Start-Process -Wait -PassThru` 捕获 Unity 退出码 `0`。`Artifacts/UnitDataImport20260729/WindowsBuild.log` 记录 `[TASK-006][build.succeeded] result=Succeeded`、`errors=0`、`warnings=1`、`totalSize=316427309`；日志 SHA-256 为 `D031348694BBF60BD6E55B9858A89067D4E2EB7AA5EC6458E44223615CAF9ABB`。产物共 `143` 个文件，主 EXE 为 `666624` 字节，SHA-256 为 `F49CDCB9E27CF2AA5C6F63BC961B4364E93363071533661593549AEC421AD60B`。
- 外部事实源复核：新增输出 `G:\ARKnoNIGHTS_tools\spine-fetcher-output-bonds-delta-20260729` 含 `15` 个完整目录、`105` 个文件、`0` 个 reparse point；每个 manifest 的六个文件哈希、非空伤害类型、`158×158` 头像及导入项目的四个 raw 文件哈希均一致，`report.json` 为 `15` 个 completed，`invalid-portraits.zh-Hans.txt` 为空。原输出的 `report.json` SHA-256 仍为 `96AF3B49F67B50B23C4E3A176AA59EC7844834B1228BDAC396B355D87619596D`，旧 staging 仍为 `1981` 文件、`1516624` 字节。外部下载器单元测试 `40/40` 通过。
- 冻结边界：没有执行正式目录生成命令；`unit-catalog-v1.json` SHA-256 仍为 `359C81D56AB89EA735FAFCD0F2A6CA243076DE7C72A9086B7E4097B6B728B0AA`，`ability-catalog-v1.json` 仍为 `BA76A69BFC5AFB186863ECF28AB36EBD504F14CD503347BE09CEEC652ECB3466`，正式 Player 继续只暴露原 `1000/5503/5504`。
- 未验证：未在可见 Editor/Windows Player 中逐一人工观察 185 个模型与头像；`1502` 的 `Appear`/`Disappear` 时长已保存，但闪现能力的实际播放次序仍待实现动画层前由项目负责人确认。
