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
  - `ARKnoNIGHTS.MatchAI.EditModeTests`
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
- HUD：`ShopReadyHudStateEditModeTests`、`PlayerListObserverEditModeTests`；商店用例必须覆盖本地 `0..5` 槽切换到联机 `1..6` 槽后按钮仍提交自身权威 SlotIndex；
- Lobby：`LobbyProtocolEditModeTests`、`LobbyRoomStateEditModeTests`、布局和 socket 集成测试；
- Match M1–M4 领域：`ArknoNights.Match.Tests`，覆盖初始化、revision、幂等、连接状态、兼容清单、商品目录、共享池守恒、版本化 PRNG、商店经济、自动合成、Buff 重映射、部署 Cost、严格备战席栈、Overflow、准备时钟、阵型命令、封印安全规则、2/3/4 人配对、结果校验、影子映射、结算收入/连续、淘汰/排名、终局和权限裁剪；只需聚焦 M4 时可使用 `ArknoNights.Match.Tests.MatchFlowEditModeTests`；
- Match M5 AI：`ArknoNights.MatchAI.Tests`，覆盖观察权限、确定性决策表、ActionId 幂等、购买后 FinalSurvivor 单次部署、0–29000ms 固定 Tick、时间跳跃、立即封存、掉线/退出接管与恢复控制。
- LAN Match Session：`MatchSessionProtocolEditModeTests`、`MatchSessionActorEditModeTests`、`LanSocketIntegrationEditModeTests`，覆盖 22 种 wire kind、长度/UTF-8/方向/范围、固定席位、命令顺序、权限快照、token、断线/重连、Battle transport、heartbeat 和端口释放；
- LAN Match 回合整合：`LanMatchRoundIntegrationEditModeTests`，覆盖 1 Human + 3 NativeBot 的准备调度、到时封存、全部 BattleResolution 原子提交、Round 2 结算状态与 Bot 下一轮调度；

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
- `LanLobbyControllerPlayModeTests` 还覆盖真实运行时 `100` 项 Unit/`67` 项 Ability 目录兼容 hash、`94` 人商店资格及六个明确排除类型、隔离 PlayerPrefs credential 的原子记录/损坏删除、开局后本地 credential 保存、权威终局回主页，以及本地 Demo 门控在 Session 接管后保持冻结。
- `LanMatchBattleAdapterPlayModeTests` 覆盖真实目录下的 Official/Shadow 观察映射、精英 `1/2/3/5` 实体展开、重复输入 hash 稳定、房主时钟离群样本/单调性、无需 Physics Collider 的棋盘平面部署投影，以及真实 loopback TCP 上 host+guest 双运行时进入同一 Battle 播放。

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

- Lobby 覆盖发现、创建、加入、准备、开始、离开和房主解散；Match 开始后必须停止 UDP 发现但保留 listener 与既有 TCP，普通 Join 被拒绝；
- socket、消息长度、主线程派发和生命周期必须有结构化失败路径；
- Match M1–M4 focused EditMode 必须验证固定四席位、初始化拒绝路径、单调 revision、重复 CommandId、稳定摘要、连接内部事务、共享实体唯一占位、失败事务不推进随机状态、自然刷新统一返池后按槽交错抽取，以及 Public/Owner/Host 权限隔离；
- M2 还必须精确断言池副本数、具体 UnitId、概率/排序、刷新游标、槽位冻结、价格、余额、升级折扣、AcquisitionOrdinal 和结构化结果码，不能只断言非空或数量近似正确；
- M3 还必须精确断言 `0..3` 精英上限与 `1/2/4/8` 副本等价、`1/2/3/5` 战斗实体派生、确定性幸存 ID、单 revision 合成诊断、tombstone/池守恒、Buff 引用、Cost 退场、严格堆叠排序、Overflow 非阻塞提升与封存删除；
- M4 还必须精确断言 `30000ms` 时钟边界、最后 Ready 原子封印、Ready 阵型锁、首回合安全购买/部署、Overflow 删除顺序、四/三/两人黄金表、人数切换确定性、每名存活玩家恰好一个 AppliedResult、影子 owner 忽略规则、结果 hash/Outcome 复核、回放门控、六档收入/连续奖励、负生命/共享名次、结算幂等、NoContest 与外部效果 outbox；
- M5 必须精确断言 AI 观察不含对手 Gold/Shop/Cost、池/seed/token，Cost 可部署候选与最右槽排序，`G=U+P+1/+2` 边界，满 13 槽仍可升级，部署坐标 35 格顺序，ActionId 重放/冲突/generation，0/1000…29000/30000 边界，跳帧补处理，真人全 Ready 后无末次动作，以及 Preparation/Battle 掉线宽限、主动退出与重连不回滚；
- M6 必须逐字段验证 Protocol/MatchRules/BattleCore/Unit/Ability 清单；真实目录 hash 必须稳定、为 64 位 SHA-256，且不把本地化文本或美术路径作为模拟兼容输入；
- Match wire 必须验证大端前缀、严格 UTF-8、0/负数/截断/尾随/超限帧、schema/kind/direction、kind-specific DTO 和 64 KiB/1 MiB/4 MiB 分级上限；
- host 本地与远端命令必须进入同一 actor 队列并获得唯一递增 `HostAcceptSequence`；Ack 必须先于同 revision 快照，重复 CommandId 不得再次推进状态；
- 每个 scoped snapshot 只能包含 Public 与接收者本人的 OwnerPrivate；淘汰旁观者只能收到 Public，wire 不得出现 Pool、ControllerKind、token 或其他玩家 Gold/Shop/Overflow；
- 客户端只接受更高 `StateRevision` 的完整替换；同 revision、旧 revision 和乱序到达不得触发重复通知或局部 merge；
- reconnect token 必须是 32-byte CSPRNG 的 43 字符 base64url；房主只保留 verifier。EOF、timeout、端点不可达和 CompatibilityMismatch 保留 credential；ExplicitQuit、MatchEnded、SessionEnded、InvalidToken 等权威终止清除；
- socket 回环必须覆盖原连接提升、命令/快照、强制断线保留四席位、较高 generation 重连、当前 revision 恢复包、MatchEnded 清理、heartbeat 跨过漏回阈值仍连接，以及最终端口释放；
- M6 自身的 Battle 测试只验证 Session/Round/BattleSet/BattleId/input hash 路由、旧轮丢弃和重连时 `snapshot → clock → seal → playback baseline/tick` 顺序；M7 chunk/checkpoint/hash 由 Battle 程序集测试，M8 再验证两者的正式适配与生命周期。
- M8 必须同时覆盖：Bot 正式调度、M4→M7 输入/结果映射、每端独立 producer、首块屏障、统一房主 Tick、scoped HUD、直接回主界面和重连恢复；不能把“收到 Lobby Start”或仅通过本地 Demo 记为通过。
- 正式 HUD 回归必须加载真实 `SampleScene`，先等待离线 `BattleHudSceneCoordinator` 完成初始化，再启动 LAN runtime；断言复用同一个 `FormalBattleHudCanvas`、`ShopReadyHudController` 和 `PlayerListHudController`，运行期不存在 `LanMatchHudCanvas`，释放后原商店数据源与玩家列表选择回调恢复。`LanMatchBattleAdapterPlayModeTests` 覆盖场景生命周期，`ShopReadyHudStateEditModeTests` 覆盖外部投影仍使用旧控件的二次确认和回调门禁。
- 自动化 loopback host+guest 证明真实 socket 与双运行时闭环；独立 Player 进程、跨物理设备、分辨率和人工视觉仍须分别记录，未执行时不得记为通过。

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
