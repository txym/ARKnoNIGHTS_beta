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
2. 旧 `UnitFactory` 仍从 `Application.dataPath/GameData/Units/Json` 读取松散 JSON；它是旧原型风险，不是新 Demo 数据链。新 Demo 使用 `Resources` 中的 `unit-catalog-v1` 与 `local-battle-v1`，已在 Player 实际加载。
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

## 29. UNIT-DATA-001 单位源数据迁移验证（2026-07-23）

- EditMode 覆盖：两个 `unit-source-v1` 源文件的新字段与旧键消失；空中文显示名不回退资源键、空技能说明合法；能力 ID 保留；目录的 `resourceKey`/中文文本/`lifeDeduct`/`rarity` 映射；`rarity` 对 `0` 与 `7` 的拒绝；待部署快照从目录投影 `rarity`；以及真实目录的速度、Tick、资源、动画和固定对战确定性回归。
- 执行时先通过 `UnitCatalogGenerator.Generate` 重新生成 `Assets/Resources/BattleData/unit-catalog-v1.json`，再运行相关 EditMode 与 PlayMode。目录、测试、编译或构建的实际结果仅在本节完成后补充；没有 XML 或构建日志的项目必须标记为未验证。
- 实际目录生成：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -executeMethod UnitCatalogGenerator.Generate -logFile G:\ARKnoNIGHTS_beta\Temp\UNIT-DATA-001\catalog-generate.log`，退出码 `0`；运行时日志包含两条未配置显示名诊断和 `TASK004A_CATALOG_GENERATED ... summary=1000:20|5503:54`，没有 C# 编译错误。第二次生成后的 SHA-256 与首次相同：`3DCB9B8CF8A346D0A4AE17301DB8E178C143194C5A50EDB8CA5DF24FCC3EA81E`。Unity 后续清理了这两份 `Temp` 生成日志。
- 实际测试：仓库 `scripts/Invoke-UnityTests.ps1` 分别运行全量 EditMode 与 PlayMode；脚本在结果 XML 写入后验证非零测试数并解析结果。EditMode 为 `66` 通过、`0` 失败、`0` 跳过；PlayMode 为 `14` 通过、`0` 失败、`0` 跳过。Unity 清理 `Temp` 时删除了这些短生命周期 XML，因此计数以脚本当场解析的结果为准；无测试失败或编译错误。
- 实际 Windows Standalone 构建：`Task006StandaloneBuild.BuildWindowsX64` 成功，日志 `Temp/UNIT-DATA-001/WindowsStandaloneBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=2`，产物输出到忽略的 `Temp/TASK-006/WindowsStandalone/`。未执行人工 GUI 验收；中文名和技能说明仍等待用户填写。

## 30. PREP-DEPLOY-001 已部署单位选中后拖动换位（2026-07-24）

- 状态层 EditMode：`Temp/PREP-DEPLOY-001/final-focused-edit/EditModeResults.xml`，筛选 `ArknoNights.Battle.Tests.LocalPlayerStateEditModeTests`，共 `12` 项、通过 `12`、失败 `0`、跳过 `0`。新增断言覆盖空格移动、己方占格原子交换、同格成功 no-op、越界/门格/不存在/非部署失败、费用不变、version/Changed 次数、CanonicalSummary 变化和重复固定序列确定性。
- 场景 PlayMode：`Temp/PREP-DEPLOY-001/green-review-fixes/PlayModeResults.xml`，筛选 `ArknoNights.Battle.Tests.StagingHudScenePlayModeTests`，共 `9` 项、通过 `9`、失败 `0`、跳过 `0`。新增断言覆盖未选中单位不能开始重定位、选中单位交换后的两个视图位置、原始 unit ID 选择保持、门格失败恢复、交互锁取消不改状态、屏幕 UI 上松手取消，以及撤退命中框不启动换位。`Temp/PREP-DEPLOY-001/phase-loop/PlayModeResults.xml` 中 `PreparationBattleLoopPlayModeTests` 为 `1/1` 通过，确认准备→战斗→准备循环回归。
- 全量 PlayMode：`Temp/PREP-DEPLOY-001/final2-full-play/PlayModeResults.xml`，共 `18` 项、通过 `18`、失败 `0`、跳过 `0`。
- 全量 EditMode：`Temp/PREP-DEPLOY-001/final2-full-edit/EditModeResults.xml`，共 `70` 项、通过 `69`、失败 `1`、跳过 `0`，因此不得记为全量通过。失败项为既有 `BattleCoreEditModeTests.RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources`：`HEAD` 中 `Assets/GameData/Units/Json/gopro.json` 的 `displayNameZhHans` 已为 `狂暴的猎狗pro`，该既有测试仍断言空字符串；两者都不在本任务 diff。本任务范围的 `LocalPlayerStateEditModeTests` 已在上述定向 XML 中全数通过。
- Editor 编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -logFile G:\ARKnoNIGHTS_beta\Temp\PREP-DEPLOY-001\final2\compile.log` 退出码 `0`；日志含 `Tundra build success`，未含 `error CS`、`Compilation failed` 或 `Scripts have compiler errors`。
- Windows Standalone：`Task006StandaloneBuild.BuildWindowsX64` 输出 `Temp/PREP-DEPLOY-001/final2/WindowsStandalone/ARKnoNIGHTS.exe`，构建摘要为 `result=Succeeded`、`errors=0`、`warnings=1`、总大小 `165411244` 字节。
- 结果保留说明：上述测试脚本均在 XML 首次完整写入、确认测试数大于零后立即解析并输出结果；随后 Unity 的 AssetDatabase 清理了这些 `Temp/PREP-DEPLOY-001` 短生命周期 XML。因此计数以本轮脚本的当场结构化解析输出为准，未将已清理文件视为持续可用证据。
- 未验证：当前无可靠的交互式 Unity Editor/Player GUI 驱动，未手工执行第一次点击、真实鼠标阈值拖动、空格移动、两单位交换、门格失败及战斗后返回准备的视觉流程；也未在生成的 Windows Player 内人工验收。自动断言不替代这些视觉/手势检查。

## 31. PREP-DEPLOY 已部署单位拖动时隐藏选择框（2026-07-24）

## 32. UI-INFO-001 验证（2026-07-24）

- Focused EditMode：42 passed / 0 failed / 0 skipped，`Temp/UnityTests/20260724-135249/EditModeResults.xml`。
- Focused PlayMode：10 passed / 0 failed / 0 skipped，`Temp/UnityTests/20260724-135337/PlayModeResults.xml`。
- Windows Player 构建、capture suite 运行与人工视觉比对尚未执行。

- TDD 红灯：`Temp/PREP-DEPLOY-001-indicator/red/PlayModeResults.xml`，筛选 `ArknoNights.Battle.Tests.StagingHudScenePlayModeTests`，共 `9` 项、通过 `6`、失败 `3`、跳过 `0`。三个失败均为新断言：开始已选中单位的重定位后 `DeployedUnitSelectionIndicator.activeSelf` 仍为 `true`，证实测试捕获的是本次需求缺口。
- 定向 PlayMode：`Temp/PREP-DEPLOY-001-indicator/green-review/PlayModeResults.xml`，同一筛选共 `9` 项、通过 `9`、失败 `0`、跳过 `0`。覆盖拖动开始隐藏选择框、成功换位与同格 no-op 后恢复、门格失败后恢复、UI 上松手与失焦取消后恢复，以及交互锁仍清除选择框。
- Editor 编译：`D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -logFile G:\ARKnoNIGHTS_beta\Temp\PREP-DEPLOY-001-indicator\compile-final.log` 退出码 `0`；日志含 `Tundra build success` 并以 `Exiting batchmode successfully now!` 结束，未发现 `error CS`、`Compilation failed` 或 `Scripts have compiler errors`。
- 未验证：无可靠的交互式 Unity Editor/Player GUI 驱动，尚未以真实鼠标拖动目视检查隐藏和恢复的逐帧表现；自动场景断言验证的是实际控制器生命周期而非人工视觉体验。
-
## LAN 房间 UI 与局域网验收（2026-07-25）

1. 定向 EditMode：运行 `LobbyProtocolEditModeTests`、`LobbyRoomStateEditModeTests`、`LanSocketIntegrationEditModeTests` 和 `LobbyAssetMapEditModeTests`，确认每个 NUnit XML 的测试数大于零且失败为零。
2. 定向 PlayMode：运行 `LanLobbyViewPlayModeTests`、`LanLobbyControllerPlayModeTests`、`AndroidMulticastLockPlayModeTests` 和 `LanLobbyCaptureSuitePlayModeTests`；最后一个实际写出五个 PNG 和包含 Canvas scale、房间、成员/ready、延迟、rect 和 Sprite 来源的 JSON 清单。
3. Windows Player：以固定 Unity `D:\2022.3.62f1c1\Editor\Unity.exe` 执行 `Task006StandaloneBuild.BuildWindowsX64`；随后从产物启动 `ARKnoNIGHTS.exe -lanLobbyCaptureSuite -lanLobbyCaptureOutput Temp/LAN-LOBBY/Captures`，等待退出码 0、五张可解码 PNG 和 manifest。只有项目忽略的 `Temp/` 或 `Artifacts/` 输出可用；不得传入 `Assets/`、项目根或外部目录。
4. 本地导出：执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyEvidence.ps1 -CaptureDirectory Temp/LAN-LOBBY/Captures -OutputDirectory Temp/LAN-LOBBY/Evidence`。脚本必须在创建输出目录前拒绝任一未映射 Sprite、非忽略输出和缺失/多重精确参考图，并生成五张实际/参考并排图；逐图检查布局、文本、顶部延迟、四人卡片、预填房间号和没有 IP/端口输入。
5. 真实局域网：在同一非隔离 Wi-Fi 上让 Windows 与 Android Player 按 `docs/LAN-LOBBY-REPORT.md` 的五步流程完成创建、发现、预填、明确加入、延迟、双端准备、开始及断连恢复。Editor loopback、截图或单机 Player 不能替代此验收；没有两台实体设备时必须标记为未验证。
