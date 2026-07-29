# 测试计划

> 状态：当前验证入口与完成标准。
>
> 历次任务的命令、结果、失败和证据位置已归档到 [`history/TEST_RECORDS.md`](history/TEST_RECORDS.md)。历史通过只证明当时的提交，不代表当前工作树已经重新验证。

## 1. 原则

- 测试期望以 [`SPEC.md`](SPEC.md) 的已确认规则为准。
- 优先验证可观察行为和结构化结果，不以“能编译”代替功能验收。
- 测试数为 `0`、XML 缺失、Unity 超时、许可证失败、项目被占用、跳过或日志不完整时，结果均为“未验证”。
- 同一项目路径不能同时由多个 Unity Editor 或 batchmode 进程打开。
- Unity 运行前后都要检查 `git status --short`，避免导入或测试产生意外资源修改。
- 截图只能验证表现，不能替代状态、事件、结果和错误路径断言。

## 2. 验证层级

按改动相关性依次执行：

1. 静态检查与最终 diff；
2. Editor 导入和 C# 编译；
3. 相关 EditMode 测试；
4. 相关 PlayMode 测试；
5. Windows x86_64 或目标平台构建；
6. 真实 Player 流程、结构化日志和必要截图；
7. 独立审查生命周期、事件订阅、序列化、异步与测试缺口。

纯文档改动通常只需要链接、格式、引用和 diff 检查；不应据此声称 Unity 编译或游戏流程已重新通过。

## 3. 环境基线

- Unity：`2022.3.62f1c1`。
- 唯一启用场景：`Assets/Scenes/SampleScene.unity`。
- 测试程序集：
  - `ARKnoNIGHTS.Battle.EditModeTests`
  - `ARKnoNIGHTS.Battle.PlayModeTests`
  - `ARKnoNIGHTS.Lobby.EditModeTests`
  - `ARKnoNIGHTS.Lobby.PlayModeTests`
  - `ARKnoNIGHTS.Match.EditModeTests`
- 项目测试启动器：`scripts/Invoke-UnityTests.ps1`。
- 默认结果目录：`Temp/UnityTests/<UTC 时间戳>/`，包含 NUnit XML、Unity 日志和 `summary.txt`。
- 每次运行必须使用新的结果目录。若脚本在 Unity 完成写入前读到不完整 XML，该次结果记为未验证；确认无 Unity 进程占用后，换新目录完整重跑，不覆盖或复用旧 XML。

## 4. 常用命令

以下示例从项目根目录执行，并使用仓库当前已安装位置；其他机器应替换 `UnityPath`。

### 4.1 Editor 编译

```powershell
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -logFile 'Temp/UnityTests/compile.log'
```

通过标准：

- 进程退出码为 `0`；
- 日志无 `error CS`、`Scripts have compiler errors`、`Compilation failed` 或未处理异常；
- 没有意外修改场景、Prefab、`.meta` 或生成目录。

### 4.2 EditMode

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File '.\scripts\Invoke-UnityTests.ps1' `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -NoGraphics
```

优先按受影响领域运行 `-TestFilter`：

- 战斗计算与能力：`BattleCoreEditModeTests`、`Bonds*EditModeTests`；
- 数据源和目录：`UnitSourceConsumerEditModeTests`、`UnitEliteVariantSourceEditModeTests`；
- 回放与 Track：`BattlePresentation*EditModeTests`；
- 玩家与回合：`LocalMatchStateEditModeTests`、`FourPlayerBattleRoundSealerEditModeTests`；
- HUD：`ShopReadyHudStateEditModeTests`、`PlayerListObserverEditModeTests`；
- Lobby：`LobbyProtocolEditModeTests`、`LobbyRoomStateEditModeTests`、布局和 socket 集成测试；
- Match M1–M3 领域：`ArknoNights.Match.Tests`，覆盖初始化、revision、幂等、连接状态、兼容清单、商品目录、共享池守恒、版本化 PRNG、两级加权抽取、公平刷新、冻结、购买、升级、自动合成、Buff 重映射、部署 Cost、严格备战席栈、Overflow、永久退休、规范摘要和权限裁剪。

规则或公共基础设施变化后再运行完整 EditMode。

### 4.3 PlayMode

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File '.\scripts\Invoke-UnityTests.ps1' `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -NoGraphics
```

PlayMode 重点覆盖：

- `SampleScene` 自动接线与准备/战斗循环；
- 真实目录、Prefab、Spine 和动态视图生命周期；
- HUD/详情/状态条场景集成；
- Lobby Controller、View、Capture Suite 与平台适配。

场景、Prefab、资源引用、自动 Bootstrap 或 Unity 生命周期发生变化时，不能只跑 EditMode。

### 4.4 Windows x86_64 构建

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\Temp\Build\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -nographics `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\Temp\Build\build.log'
```

通过标准：

- 命令退出码为 `0`；
- `Temp/TASK-006/windows-standalone-build-summary.txt` 或指定日志记录 `Succeeded`；
- 错误数为 `0`；
- 实际启动 Player 后，相关验收入口退出码和结构化日志符合预期。

目标平台、Package、场景或运行时程序集变化时必须执行对应构建；普通文档改动不要求构建。

## 5. 领域验收重点

### Battle Core

- 相同输入、版本和目录哈希产生相同事件、终态与胜方；
- 整数 Tick、排序和决胜规则不依赖集合遍历、Unity 物理或渲染帧率；
- 同 Tick 伤害、死亡、阻挡释放、Spawn、冲门和终局顺序有明确断言；
- 新能力同时覆盖数据投影、运行时加载、Core 语义和必要表现事件。

### Presentation

- Home/Away 只做视图投影，不修改源结果；
- Replay/Seek 不重新运行 Core；
- 动态 Spawn/Death/GateReached 的视图创建和释放不泄漏；
- 暂停、倍速和动画占用不改变权威事件。

### Player、Round 与 HUD

- 玩家快照、商店、购买、冻结、刷新、等级、部署和观察权限保持单一状态源；
- 进入战斗前封存，战斗中不回写玩家单位；
- 四玩家配对和两场演示共享时钟；
- UI 只投影快照，不保存第二份权威状态。

### Lobby 与 Match

- 当前 Lobby 覆盖发现、创建、加入、准备、开始、离开和房主解散；
- socket、消息长度、主线程派发和生命周期必须有结构化失败路径；
- Match M1–M3 focused EditMode 必须验证固定四席位、初始化拒绝路径、单调 revision、重复 CommandId、稳定摘要、连接内部事务、共享实体唯一占位、失败事务不推进随机状态、自然刷新统一返池后按槽交错抽取，以及 Public/Owner/Host 权限隔离；
- M2 还必须精确断言池副本数、具体 UnitId、概率/排序、刷新游标、槽位冻结、价格、余额、升级折扣、AcquisitionOrdinal 和结构化结果码，不能只断言非空或数量近似正确；
- M3 还必须精确断言 `0..3` 精英上限与 `1/2/4/8` 副本等价、`1/2/3/5` 战斗实体派生、确定性幸存 ID、单 revision 合成诊断、tombstone/池守恒、Buff 引用、Cost 退场、严格堆叠排序、Overflow 非阻塞提升与封存删除；
- M1–M3 没有 PlayMode、Socket、场景或 UI 接入，不能用纯领域测试推断局域网流程可玩；
- 后续 Match 里程碑必须逐步新增版本握手传输、命令排序、快照同步、重连、AI 接管、回合结算、BattleInput 和跨端摘要一致性验证；
- 不得把“收到 Lobby Start”记为正式联网对局通过。

## 6. 证据记录

每次有意义的验证至少记录：

- 日期、提交或工作树状态；
- Unity 版本和完整命令；
- 退出码；
- 测试总数、通过、失败、跳过、不确定和未运行数；
- 首个有效失败与堆栈；
- XML、日志、构建摘要和截图清单位置；
- 未执行项、人工项和剩余风险。

提交前执行 `git diff --check`、检查最终 diff，并确认没有无关修改、生成目录或用户改动丢失。
