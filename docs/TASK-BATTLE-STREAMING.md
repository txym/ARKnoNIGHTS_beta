# M7：战斗确定性分块计算、增量播放与末秒校验 Agent 提示词

> 状态：待在独立 worktree 中实施。
>
> 本文件是可以直接交给实现 Agent 的任务提示词。本文“已确认规则”来自项目负责人最新确认；若当前 `docs/SPEC.md`、旧测试或旧 Demo 与本文冲突，以本文为本任务的需求输入，但不得自行扩展到本文明确排除的系统。共享事实文档最终由主 Planner 统一合并，避免多个 worktree 同时改写。

## 1. 角色与最终目标

你负责 ARKnoNIGHTS 的纯战斗计算和战斗表现数据流，不负责 LAN 协议、房间、回合经济或 AI。

在独立 worktree、`codex/` 前缀分支中，把当前“所有 Battle Core 先 `RunToCompletion()`，再一次性编译完整 Track，最后开始播放”的链路改造成：

1. Battle Core 仍以固定 `20 TPS` 确定性推进；
2. 每个完整数据块包含 `100` 个权威 Tick，即 `5` 秒；
3. 所有本地战斗都产出第一块后即可开始播放；
4. 播放期间继续按预算计算后续块，不允许在播放开始前偷偷跑完整场；
5. 终局不足 `100` Tick 的尾块立即产出；
6. 每场战斗输出双方本次承受的玩家生命伤害，并以两者大小统一判定胜、负、平；
7. 每场战斗生成只覆盖最后最多 `20` 个已完成 Tick 加最终状态的规范 SHA-256；
8. 保留足够的兼容入口，使现有一次性 Core 与表现回归测试能继续验证“分块拼接结果等于一次性结果”。
9. 提供与 M4 `BattleResolution`、M6 `BattleSeal/FinalSecondHash/PlaybackClock` 对齐的纯数据 adapter；100 Tick 块只交给本机播放机，不实现 Socket 或常规网络下发。

实现必须能被后续 LAN 层用于“所有在线客户端独立计算所有官方战斗和影子战斗”。本任务只提供纯本地、可传输的数据契约和状态接口，不实现套接字或主机协议。

## 2. 开始前必须执行

0. 使用独立 worktree 和分支 `codex/battle-streaming`；记录实际基线 SHA，不在主工作区实现，不合并回主分支。
1. 阅读仓库根目录 `AGENTS.md`。
2. 完整阅读：
   - `docs/SPEC.md`
   - `docs/ARCHITECTURE.md`
   - `docs/TEST_PLAN.md`
   - `docs/LAN-MATCH-DESIGN.md`
   - `docs/LAN-MATCH-IMPLEMENTATION-PLAN.md`
   - `docs/TASK-LAN-MATCH-FLOW.md`
   - `docs/TASK-LAN-MATCH-SESSION.md`
   - `docs/TASK-002.md`
   - `docs/TASK-003.md`
   - `docs/TASK-004A.md`
   - `docs/superpowers/specs/2026-07-25-battle-presentation-tracks-design.md`
3. 执行 `git status --short`，保护 worktree 中已有修改，不覆盖、不回滚、不格式化无关文件。
4. 检查待改符号的调用方、程序集边界和现有测试，至少覆盖：
   - `Assets/Game/Battle/Core/Simulation/BattleRunner.cs`
   - `Assets/Game/Battle/Core/Input/BattleInput.cs`
   - `Assets/Game/Battle/Core/Events/`
   - `Assets/Game/Battle/Presentation/Tracks/`
   - `Assets/Game/Battle/Presentation/Playback/BattleTrackPlaybackController.cs`
   - `Assets/Game/Battle/Demo/MultiBattlePresentationCoordinator.cs`
   - `Assets/Game/Runtime/Round/PreparationBattlePhase.cs`
   - `Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs`
   - 对应 EditMode、PlayMode 测试
5. 确认没有 Unity Editor 或 batchmode 进程正在占用这个 worktree。所有 Unity 测试串行执行。
6. 先建立相关 EditMode/PlayMode 基线。测试工具、许可证或项目占用导致无法运行时，明确记为“未验证”，不能记为通过。

## 3. 当前实现事实

开始实现时以实际代码复核以下事实，不要只相信本段：

- `BattleRunner.Step()` 已是显式确定性 Tick 驱动，构造函数会在 Tick `0` 发出初始 Spawn 事件。
- `BattleRunner.RunToCompletion()` 当前累积完整 Trace、Events 和 FinalUnits 后才返回 `BattleRunResult`。
- `BattlePresentationTrackCompiler.TryCompile()` 当前只接受已结束的完整 `BattleRunResult`，会校验唯一 `BattleEnded` 和最终实体状态。
- `PositionTrackCompressor` 需要完整移动点，不能直接假定它能在不知道未来轨迹时安全封闭跨块移动段。
- `MultiBattlePresentationCoordinator.Prepare()` 当前逐场 `RunToCompletion()` 并编译完整 Track，因此不是真正的边算边播。
- 正式本地循环的 `BattleMaxTicks` 当前仍为 `12000`，与本任务已确认的 `1800` 冲突。
- `lifeDeduct` 当前存在于真实单位目录 `UnitCatalogEntry`，尚未进入 `Battle.Core.UnitDefinition`，Core 也尚未输出玩家生命伤害。
- 当前 Core 没有完整“冲家”行为。不得在本任务中凭空设计一套新寻路或改变现有移动/索敌/阻挡规则。
- 当前 Demo 存在暂停、倍速、重播兼容功能；正式对局不使用这些功能。不要为了本任务扩大范围去删除旧 Demo API，但新正式流不得依赖暂停、倍速或重播。

## 4. 已确认的战斗规则

### 4.1 时钟与终止

- 固定 `20 TPS`，每 Tick `0.05` 秒。
- 正式对局最多 `90` 秒，即 `1800` Tick。
- 测试 fixture 仍可传入较小的正数 `MaxTicks`，以覆盖边界。
- 正常结束、双方同时全灭和达到最大 Tick，都必须生成终局结果。
- 达到最大 Tick 不是“未解决”；它仍按双方本次承受的玩家生命伤害正常判胜负或平局。

### 4.2 玩家生命伤害

对一名玩家：

```text
本次战斗承受的玩家生命伤害
= 已成功冲到该玩家一侧的敌方战斗实体 lifeDeduct 总和
+ 战斗结束时尚未冲家且仍存活的敌方战斗实体 lifeDeduct 总和
```

- 同一战斗实体至多计入一次。
- 每个实体使用自己单位类型数据中的原始 `lifeDeduct`。
- 精英等级、部署费用和结算阶段不得临时改写 `lifeDeduct`。
- 召唤物若参加战斗，按自己的类型和实体正常计数。
- 超时时，所有仍存活且未计入冲家伤害的实体直接对对方玩家结算自己的 `lifeDeduct`。
- 当前 Core 尚无冲家行为：本任务必须建立明确的已冲家累计/事件/结果契约，但不得擅自发明路线规则。当前实际战斗可以只从“终局仍存活实体”产生非零伤害；未来冲家实现必须能通过同一契约接入且不会重复计数。

### 4.3 统一胜负

设 `homeLifeDamage` 为 Home 玩家本次承受伤害，`awayLifeDamage` 为 Away 玩家本次承受伤害：

- `homeLifeDamage < awayLifeDamage`：Home 胜；
- `homeLifeDamage > awayLifeDamage`：Away 胜；
- 两者相等：平局。

该规则统一用于正常结束、双方同时全灭和超时。不能继续把“Winner 为空”同时表示平局、超时和未完成状态。

推荐新增明确的终局结果枚举，例如 `HomeWin / AwayWin / Draw`，并保留兼容 `Winner` 投影：

- HomeWin -> `BattleSide.Home`
- AwayWin -> `BattleSide.Away`
- Draw -> `null`

`IsTerminal`/`IsResolved` 必须与“有 Winner”解耦；平局也属于已解决。

回合生命扣除、连胜连败、平局额外收入、淘汰和排名由后续 Match Flow 处理。本任务只输出不可变的战斗伤害与战斗结果，不直接写回 `PlayerState`。

### 4.4 `lifeDeduct` 数据接入

- 把非负 `lifeDeduct` 作为 Battle Core 的权威单位类型输入之一，并纳入 `BattleInput.CanonicalSummary`。
- 真实目录加载必须把 `UnitCatalogEntry.LifeDeduct` 原样传给 Core。
- 保持已有构造函数源兼容；旧合成测试数据若没有显式值，应采用清楚记录的兼容默认值，不能让旧 fixture 因所有单位默认为 `0` 而意外全部变成平局。
- 新增测试必须能显式构造 `lifeDeduct = 0`、不同正值、召唤物值和相同伤害值。
- 不修改玩家层的共享牌库 `UnitId` 规则。
- 持久单位的牌库外赠送问题明确不在本任务范围。
- 战斗内召唤物继续使用 Battle Core 局部、确定性的动态实体 ID，不得占用或回写玩家持久 `UnitId`。
- 若本任务同时补齐精英多实体展开，只能从封存的持久 `UnitId` 和实体序号确定性派生 Battle Entity ID；不得创建新的持久 UnitId。精英 `0/1/2/3` 的实体数为 `1/2/3/5`。若该展开会与正在进行的单位内容分支产生冲突，先保留兼容接口和定向测试，把实际展开列为明确未完成项，不要猜测内容实现顺序。

## 5. 分块 Core 契约

### 5.1 基本要求

- 一个完整块准确对应 `100` 次权威 Tick 推进。
- Tick 边界必须在类型和字段名中表达清楚，并用测试固定，不允许只靠注释猜开闭区间。
- 当前 Runner 事件 Tick 从构造阶段的 `0` 开始、第一次 `Step()` 后为 `1`。分块适配不得改变既有 Core Tick 语义。
- 推荐用“本块推进前的 completed tick”和“本块推进后的 completed tick”表达，例如 `PreviousCompletedTick` 与 `CompletedTick`；完整首块应表示从 `0` 推进到 `100`，并明确 Tick `0` 的初始 Spawn 只出现在首块。
- 每块只携带该块新增事件的不可变副本，不得把此前全部累计 Events 重复塞进每一块。
- 事件按 `(Tick, Sequence)` 严格稳定排序。
- Core 的内部状态必须跨块连续保留；每个块不能重建 `BattleRunner`、重新加载输入或重置动态实体 ID 分配器。
- 终局在块中途发生时，立即发出尾块，不补空 Tick，不等待凑满 100。
- 对已终止 Producer 再推进必须是稳定 no-op 或结构化拒绝，不能产生第二个终局块。

### 5.2 建议的数据模型

命名可以按现有风格调整，但应有等价的只读契约：

```text
BattleSimulationChunk
  SchemaVersion
  BattleId
  SealedInputHash
  ChunkIndex
  PreviousCompletedTick
  CompletedTick
  AuthoritativeTickCount
  Events[]
  EndCheckpoint
  IsTerminal
  StopReason?          // terminal only
  Outcome?             // terminal only
  HomeLifeDamage?      // terminal only
  AwayLifeDamage?      // terminal only
  FinalSecondSha256?   // terminal only
```

`EndCheckpoint` 是用于播放重建、观察切换和后续 LAN 接入的完整不可变“表现检查点”，不是可变 `RuntimeUnitState` 的引用，也不要假装它已经是可跨版本恢复 Core 内部执行的存档。它至少需要表达：

- checkpoint Tick；
- 每个已知战斗实体的 UnitId、TypeId、PlayerId、Side；
- 是否动态生成、是否已激活、是否存活、是否已冲家；
- 位置、当前/最大生命、护盾；
- 当前目标与稳定排序的阻挡关系；
- 当前表现动作类型及足以按项目确认规则从动作开头重播的数据；
- Spawn/Activation/Death Tick；
- 终局时双方伤害、结果和停止原因。

不要暴露可变列表，不要保存 UnityEngine 对象，不要依赖引用地址、默认 `GetHashCode()`、本机时间、区域设置或字典枚举顺序。

### 5.3 Producer API

新增一个纯 C# 增量 Producer，职责是：

- 持有且只持有一个持续存在的 `BattleRunner`；
- 按调用方给定预算逐 Tick 或逐块推进；
- 只在完整 100 Tick 或提前终局时发布不可变块；
- 能报告 `ProducedThroughTick`、是否已有首块、是否终局；
- 能取得终局 `BattleRunResult`，且它与同一输入直接 `RunToCompletion()` 的结果一致；
- 不读取 `Time.deltaTime`，不使用 Unity 物理；
- 不要求后台线程。若选择线程，必须证明 Core 不接触 Unity API、并发所有权清楚且测试无竞态；默认优先单线程、预算式推进。

不得复制一份与 `BattleRunner.Step()` 分叉的战斗算法。一次性和分块模式必须复用同一权威 Tick 实现。

### 5.4 M4/M6 adapter

终局 Producer 必须能生成与 M4 对齐的纯数据结果：

```text
BattleResolution
  BattleId
  SealedInputHash
  HomeLifeDamage
  AwayLifeDamage
  Outcome
  EndTick
  TerminalReason
```

要求：

- `Outcome` 必须由双方伤害重新比较得到，不能沿用旧 nullable Winner 猜测。
- `SealedInputHash` 来自 M4 封存输入的规范 SHA-256；M7 不重建另一套不兼容摘要。
- 对同一个已终止 Producer 重复获取结果，返回相同不可变值。
- M4 负责把 Official/Shadow 结果映射到玩家、扣生命和发资源；M7 不写 MatchState。

同时提供网络无关的本地数据 DTO，以及供 M6 显式映射的末秒摘要：

```text
BattleChunkPayload
FinalSecondHashPayload
BattleRecoveryDescriptor
```

这些类型：

- 不引用 Socket、Lobby、MatchAuthority 或 Unity UI；
- 带 SchemaVersion、BattleId、SealedInputHash 和所需 chunk/checkpoint 范围；
- 能被严格序列化而不暴露可变 Core 对象；
- 不自行发送消息。

`BattleChunkPayload` 和 `BattleRecoveryDescriptor` 供本机播放、缓冲不足与重连后加速计算使用，不由房主在正常流程中下发，也不能代替客户端独立 Core 计算。`BattleRecoveryDescriptor` 至少说明本机最近可用 checkpoint Tick、已发布 chunk 范围、终局状态和当前可追赶上界。`FinalSecondHashPayload` 可由 M6 映射为上报消息。

## 6. 增量表现与缓冲

### 6.1 编译约束

当前完整 Track 编译器会使用未来事件压缩移动轨迹。增量实现不能假定未来已知。

可采用以下任一安全方案：

1. 流式块保留当前块原始位置点/事件，播放层增量采样；终局后再使用原完整压缩器生成兼容 Track；
2. 保留未封闭的移动尾段，只发布已有充分信息、不会因未来点改变的段；
3. 其他能通过“分块拼接等价”和跨边界测试证明正确的实现。

禁止在每个块上独立调用完整 `PositionTrackCompressor` 后直接拼接，因为这可能改变跨块移动插值和误差边界。

### 6.2 播放行为

- 所有本地 Battle Producer 都至少有第一块后，正式流才从 Tick `0` 开始播放。
- 同一回合的多个战斗共享一个表现 Tick；较早结束的战斗保持最终状态。
- 播放期间调用预算式计算，把每场战斗的可播放上界维持在当前表现 Tick 之后至少一个完整块；终局尾块例外。
- 不得在 `Prepare()` 中对任何一场战斗调用完整 `RunToCompletion()`。
- 调度多个战斗时采用稳定的轮转/预算方式，不能先把第一场算完再计算第二场。
- 流式协调器不能继续硬编码“恰好 4 个 observation”。它应接受后续配对层提供的任意非空战斗集合与合法观察映射；本任务不负责生成四人、三人影子或双人配对。
- 首块准备状态必须能被未来 LAN 层读取；“主机最多等在线客户端 10 秒”属于 LAN 层，本任务不实现计时或断线逻辑。
- 任一 Producer 首块失败必须返回结构化失败，供房主按已确认规则终止整局；不能用空块伪装 ready。

### 6.3 缓冲不足与中途绑定

- 若表现 Tick 即将超过已计算上界，本地表现进入明确的 `Buffering/Syncing` 状态并停在最后可用 Tick；不得虚构事件或跳过 Core 计算。
- 缓冲恢复后，从最近完整检查点重建到当前可播放 Tick，错过的动画可以不补播。
- 重建时 Core 仍必须计算所有缺失 Tick；检查点不能成为跳过确定性 Core 计算、直接信任房主结果的捷径。
- 中途绑定/重连时直接显示当前 Tick：
  - 已经死亡或已冲家的实体不创建，也不补死亡动画；
  - 存活实体显示检查点中的当前位置、生命和状态；
  - 当前动作动画允许从该动作开头重新播放；
  - 不从 Tick `0` 重播给用户。
- 新流不需要正式的暂停、倍速和重播功能。旧 Demo 的兼容 API可保留，但不得成为新流正确性的前提。

## 7. 最后 20 Tick 的 SHA-256

### 7.1 范围

每场战斗终局生成一个规范 SHA-256，当前只用于未来客户端之间比较；发现不一致后的处理策略明确暂不实现。

哈希输入只包括：

1. 哈希 schema/version；
2. `BattleId`；
3. 完整 `BattleInput.CanonicalSummary` 的 SHA-256；
4. 最后最多 `20` 个已完成权威 Tick 内的全部事件；
5. 终局所有战斗实体的完整规范状态；
6. `CompletedTicks`、停止原因、Home/Away 本次生命伤害和统一 Outcome。

“最后最多 20 个已完成 Tick”定义为：

```text
startTick = max(1, CompletedTicks - 19)
startTick <= event.Tick <= CompletedTicks
```

Tick `0` 初始 Spawn 不属于“已完成权威 Tick”的 20 Tick 事件窗口；它的影响由输入哈希、动态 Spawn 记录和终局实体状态覆盖。若短测试在 Tick `0` 就终止，必须另有显式测试和规范编码，不能产生空值歧义。

### 7.2 规范编码

- 使用 `System.Security.Cryptography.SHA256`。
- 输出固定为 64 位十六进制，统一大小写。
- 使用 UTF-8。
- 所有字符串采用长度前缀或等价无歧义编码，不能只用可出现在字段中的分隔符连接。
- 整数使用固定字节序或 invariant 十进制。
- 枚举使用明确整数值。
- 事件按 `(Tick, Sequence)` 排序。
- 实体按 `UnitId` ordinal 排序。
- Buff、阻挡 ID、能力状态等集合必须使用各自明确的稳定排序。
- 动态 Spawn 的完整实例快照必须进入对应事件规范记录。
- 不使用现有 FNV/`GetHashCode()` 代替 SHA-256。
- 不把表现帧率、动画内部时间、当前观察玩家、UI 状态或本地缓冲状态放进哈希。

## 8. 必须覆盖的测试

优先 TDD。至少新增或调整以下自动化覆盖。

### 8.1 Core 分块等价

- 相同输入重复运行，块边界、块内事件、终局结果和 SHA-256 完全一致。
- 分块事件按顺序拼接，等于一次性 `BattleRunResult.Events`，每个事件恰好一次。
- 分块终局 `BattleRunResult` 的 StableSummary、FinalUnits、UnitSnapshots、StopReason、Outcome、双方伤害等于一次性路径。
- 恰好 `100` Tick 终局：只有一个终局块。
- `99` Tick 终局：一个 99-Tick 尾块。
- `101` Tick 终局：一个 100-Tick 完整块加一个 1-Tick 尾块。
- 小于 100 Tick 的正常胜利、双方同时全灭和超时。
- 达到 `1800` Tick 时产生终局尾块/完整块，不继续到 `1801`。
- Producer 终止后重复推进不产生第二个 `BattleEnded` 或第二个终局块。
- 终局 adapter 精确生成 M4 `BattleResolution`，BattleId/InputHash/双方伤害/Outcome/EndTick/TerminalReason 一致。
- `BattleChunkPayload`、`FinalSecondHashPayload` 和 `BattleRecoveryDescriptor` 为不可变稳定数据，不引用 Runner 可变集合。

### 8.2 跨块状态

- 攻击在前一块启动、后一块结算伤害。
- 目标锁定跨块。
- 阻挡关系跨块。
- 单位连续移动跨块，边界前后采样与完整 Track 一致且保持当前误差标准。
- 动态召唤在块末附近生成、下一 Tick 激活，ID 与事件顺序稳定。
- 死亡发生在块边界前后。
- 检查点不引用 Runner 的可变集合；后续推进不能改变已发布块。

### 8.3 生命伤害与胜负

- Home 承伤较少 -> HomeWin。
- Away 承伤较少 -> AwayWin。
- 相同伤害 -> Draw，包括双方同时全灭 `0:0`。
- 超时按双方存活敌方实体的 `lifeDeduct` 比较，不再是 unresolved。
- `lifeDeduct = 0` 合法且参与相等判定。
- 不同实体分别计数；同一实体不重复计入“已冲家”和“终局存活”。
- 动态召唤物使用自己 TypeId 的 `lifeDeduct`。
- 终局结果不写回 `LocalPlayerState/PlayerState`。

如果当前没有冲家生产逻辑，用可注入的已冲家累计/测试构造器验证去重契约；不要为了通过此测试擅自增加寻路规则。

### 8.4 最后 20 Tick 哈希

- 同输入重复运行哈希一致。
- 修改最后 20 Tick 内的事件、Spawn 快照、最终实体状态、双方伤害或 Outcome，哈希变化。
- 只修改窗口外事件且保证最终状态和其他哈希字段完全相同，末秒哈希不变。
- Tick 窗口边界 `CompletedTicks - 20` 不进入，`CompletedTicks - 19` 进入。
- 短于 20 Tick 和必要的 Tick 0 边界无歧义。
- 字符串包含分隔符、不同字典插入顺序、不同当前区域设置时哈希仍一致。

### 8.5 多战斗流与播放

- 两场战斗稳定轮转计算；任何一场都不能在另一场首块产生前先跑到终局。
- 两场都有首块后才进入可播放状态。
- M4 生成的四人两场、三人 Official+Shadow 两场和两人一场 `SealedRoundPlan` 都能作为任意非空 Producer 集合运行，不在 M7 重算配对。
- 播放期间至少维持一块 ahead；预算不足时进入 Buffering，不越过已计算上界。
- 一场提前结束时固定终态，另一场继续推进。
- 在非零 Tick 切换观察对象/重新绑定，位置、HP、护盾、存活和动作类型正确，不重算 Core。
- 中途绑定不创建已死亡实体、不补死亡动画。
- 旧一次性 Track 编译和现有播放测试无无关回归。

## 9. 明确不在本任务范围

不得实现或改动：

- UDP/TCP、房间发现、LAN 消息协议、主机权威命令或重连 token；
- 客户端首块 ready 上报、主机 10 秒等待和哈希不一致后的处理；
- 四人/三人影子/双人配对算法；
- 回合生命实际扣除、淘汰、排名、连胜连败或平局额外赤金/Cost；
- 商店、共享牌库、开局持久 UnitId 分配、牌库外赠送、购买、刷新；
- 单位合成、Overflow、自动部署；
- 自动人机；
- 未在现有 Core 中定义的冲家路线、远程规则或新战斗机制；
- 单位能力内容本身；必须兼容能力目录和动态召唤，不得硬编码能力列表或假设能力实现分支先后；
- 正式 UI 的新美术、场景、Prefab、Animator、Package 或 ProjectSettings；
- 主机纠错、完整战斗回放传输或反作弊。

若实现所需的玩家可见战斗规则在本文和当前代码中仍无法确定，停止该分支的相关行为实现并向主 Planner报告；不要从《明日方舟》或其他游戏猜规则。

## 10. 文件与架构约束

- Core 层保持纯 C#、确定性且不依赖 `UnityEngine`。
- Presentation 可以依赖 Core，Core 不得反向依赖 Presentation、Demo 或 Runtime。
- 网络无关 DTO 不得引用 Socket、Lobby 或 Unity UI 类型。
- 新增 `Assets/` 下文件时保留/生成匹配 `.meta`，不得改动无关 GUID。
- 不修改场景或 Prefab。
- 不升级 Unity、Package 或其他依赖。
- 不做无关重构或全文件格式化。
- 优先新增小型明确类型，不要继续把所有职责塞进 `BattleRunner` 或 `MultiBattlePresentationCoordinator`。
- 兼容 API 若必须废弃，先提供迁移适配并说明调用方影响，不要直接删除让大量旧测试失去意义。
- 本 worktree 不改写 `docs/SPEC.md`、`docs/ARCHITECTURE.md`、`docs/TEST_PLAN.md` 的共享章节；在交付报告中列出建议文档增量，由主 Planner统一合并。

## 11. 验证

使用仓库已有脚本，Unity 路径为：

```powershell
D:\2022.3.62f1c1\Editor\Unity.exe
```

示例命令；`ProjectPath` 必须替换为该 Agent 自己的独立 worktree 绝对路径，输出放在该 worktree 的 `Temp/` 或 `Artifacts/`：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath '<独立-worktree-绝对路径>' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests' `
  -OutputDirectory 'Artifacts\BattleStreaming\Final-EditMode' `
  -TimeoutSeconds 900 `
  -NoGraphics
```

按风险至少串行执行：

1. 新增 Core/Hash/Streaming focused EditMode；
2. `BattleCoreEditModeTests`；
3. `BattlePresentationTrackEditModeTests`；
4. `MultiBattlePresentationCoordinatorEditModeTests` 或新增流式协调器测试；
5. 相关 `BattlePresentationPlaybackPlayModeTests`；
6. 全量 EditMode；
7. 全量 PlayMode。

每次检查 NUnit XML，要求测试数大于 `0`，失败、跳过、inconclusive、not-run、not-runnable 均为 `0`。扫描日志中的编译错误和未处理异常。最终检查：

- `git diff --check`
- `git status --short`
- 最终 diff 无场景、Prefab、Package、ProjectSettings 或无关资源变化
- 没有提交 `Library/`、`Temp/`、`Logs/`、`Artifacts/` 等生成物

## 12. 停止条件

遇到以下情况停止并报告证据，不要静默降级：

- 本文、当前用户已确认规则和实际数据对同一玩家可见行为存在实质冲突；
- 必须先定义新的冲家路线或其他未确认战斗机制才能继续；
- 需要修改单位能力分支拥有的高冲突文件，且无法通过兼容接口隔离；
- 需要升级 Unity/Package 或安装第三方依赖；
- 测试环境被其他 Unity 进程占用、许可证失败或无法产生有效非零测试 XML；
- 连续三次有实质差异的尝试仍无法推进。

## 13. 交付要求

在自己的分支提交小而可审查的 commit，不要合并回主分支。最终向主 Planner提供：

1. 分支名、worktree 路径和 commit SHA；
2. 完成的行为；
3. 修改/新增文件清单；
4. 公开 API 与数据契约；
   - 明确列出 M4 `BattleResolution` adapter；
   - 明确列出本机 `BattleChunk/RecoveryDescriptor` 与 M6 `FinalSecondHash` adapter；
5. 与旧一次性路径的兼容方式；
6. 每条测试命令、退出码、测试数量、XML 和日志路径；
7. 未验证项；
8. 已知风险，尤其是移动压缩跨块、动态召唤、非零 Tick 绑定和能力分支兼容；
9. 建议由主 Planner合并到 SPEC/ARCHITECTURE/TEST_PLAN 的精确文档增量；
10. 明确确认：未实现 LAN、哈希不一致处理、配对、经济、AI、合成、牌库外赠送或未确认的冲家路线。
