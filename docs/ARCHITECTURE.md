# 当前项目架构

> 状态：截至 2026-07-30 的当前实现摘要。
>
> 本文只描述仓库中已经存在的结构和能力。LAN Match M1—M8 已在统一集成链中闭环；物理双机 LAN、真实网络中断恢复和多分辨率 HUD 仍待人工验收。游戏规则以 [`SPEC.md`](SPEC.md) 为准；历史演进见 [`history/ARCHITECTURE_EVOLUTION.md`](history/ARCHITECTURE_EVOLUTION.md)。

## 1. 项目基线

- Unity Editor：`2022.3.62f1c1`。
- 渲染：Built-in Render Pipeline。
- UI：uGUI、TextMesh Pro；角色表现使用仓库内的 Spine Unity 3.8 源码与资源。
- 输入：Project Settings 使用旧 Input Manager；第一方交互主要读取 `UnityEngine.Input`。
- 场景：Build Settings 只启用 `Assets/Scenes/SampleScene.unity`。
- 当前产品形态：单场景本地四玩家自走棋 Demo，以及持续整个正式对局的 LAN Match Session。正式路径已接入 M1—M5 权威状态/AI、M6 会话与重连、M7 Battle 流式计算和 M8 场景组合根/HUD。

## 2. 模块边界

| 程序集或区域 | 职责 | 关键边界 |
|---|---|---|
| `ARKnoNIGHTS.Battle.Core` | 20 TPS 确定性战斗、单位状态、索敌、移动、阻挡、攻击、能力、结果与结构化事件 | `noEngineReferences=true`；不依赖场景、物理、渲染帧率或 Unity 对象 |
| `ARKnoNIGHTS.Battle.Infrastructure` | Player-safe 目录、fixture 和本地战斗数据的加载与校验 | 引用 Core；负责 Unity Resources/JSON 到不可变输入的转换 |
| `ARKnoNIGHTS.Battle.Presentation` | 主客场投影、Track 编译、事件回放与表现接口 | 只读 Core 结果；不得反向改写战斗计算 |
| `ARKnoNIGHTS.Battle.Demo` | 计算与回放协调器、暂停、倍速、重播和观察视角 | 不拥有场景查找；场景壳在 `Assembly-CSharp` |
| `ARKnoNIGHTS.PlayerState` | 玩家单位、区域、阵型、等级、赤金、商店和本地四玩家快照 | 本地 Demo 的状态源，不是联网权威 Match 状态 |
| `ARKnoNIGHTS.Round` | 准备阶段状态机、阵型封存、四玩家配对和战斗输入生成 | 连接 PlayerState 与 Battle Core，不负责网络同步 |
| `ARKnoNIGHTS.Lobby` | UDP 房间发现、Lobby/Match TCP 协议、持续 Session、权威 actor、分权限快照、断线与重连 | 引用 `ARKnoNIGHTS.Match`；不引用 PlayerState 或 Battle Core，不计算 Battle chunk/hash |
| `ARKnoNIGHTS.Match` | 固定四席位的房主权威状态、共享实体牌库、商店经济、合成/Overflow、权威阵型、30 秒准备时钟、2/3/4 人配对、封印计划、结果校验、结算/淘汰/排名、命令幂等与分权限快照 | `noEngineReferences=true` 且零程序集引用；M4 只定义纯领域战斗输入/结果契约，不含 Battle Tick、AI 决策、LAN Session 或 Socket |
| `ARKnoNIGHTS.MatchAI` | 权限裁剪的 BotObservation、纯决策表、权威操作适配、固定 Tick 调度与掉线/退出接管生命周期 | `noEngineReferences=true`，只引用 `ARKnoNIGHTS.Match`；不读取 Unity、墙上时间、随机数、Socket、UI 或 Battle Core |
| `ARKnoNIGHTS.UI`、`ARKnoNIGHTS.Details` | 正式 HUD、商店/准备、玩家列表与单位详情投影 | 消费快照与事件，不保存第二份权威游戏状态 |
| `Assembly-CSharp` 下的 `Runtime/Initial`、`Runtime/Deployment` | `SampleScene` 自动接线、场景生命周期和部署交互 | 是各隔离程序集与序列化场景之间的集成层 |

第一方 EditMode 和 PlayMode 测试分别位于 `Assets/Game/Tests/EditMode` 与 `Assets/Game/Tests/PlayMode`，按 Battle/Lobby/Match 分程序集；Match M1–M4 纯领域测试与 M5 AI 测试分别位于独立 EditMode 程序集，LAN Session 的协议、actor、socket 与控制器测试分别位于 Lobby EditMode/PlayMode 程序集。

## 3. 主要数据流

### 3.1 单位与能力数据

```text
Assets/GameData/Units/EliteVariants/Json
Assets/GameData/Abilities/Json
        ↓ Editor 生成器与校验
Assets/Resources/BattleData/*-catalog-v1.json
        ↓ Player-safe loaders
UnitCatalog / AbilityCatalog / SkillAnimationCatalog
        ↓ 输入封存
BattleRunner
```

- v2 JSON 是人工维护源；`Assets/Resources/BattleData` 下的 v1 目录是运行时消费物。
- `UnitCatalogGenerator`、`AbilityCatalogGenerator` 和 `SkillAnimationCatalogGenerator` 负责生成与交叉校验。
- 运行时不读取项目外数据，也不从表现资源反向推断游戏规则。

### 3.2 本地准备与战斗

```text
local-match-state-v1 + 四份 PlayerState
        ↓ LocalMatchStateLoader
准备阶段（商店、购买、部署、观察、Ready）
        ↓ FourPlayerBattleRoundSealer
两场确定性 BattleInput
        ↓ BattleRunner
不可变 BattleRunResult
        ↓ MultiBattlePresentationCoordinator
共享时钟回放与玩家视角切换
        ↓
返回准备阶段并刷新商店
```

`PreparationBattleLoopController` 当前负责离线 Demo 的 `Preparation → Battle → Preparation` 场景副作用。离线战斗中的单位受伤和死亡不直接写回持久玩家单位；正式 LAN 路径由 `LanMatchRuntimeController` 把 M7 结果映射回 M4，并由 Match 层原子完成玩家扣血、淘汰与经济结算。

### 3.3 LAN Match Session

```text
UDP 发现 → 创建/加入 TCP 房间 → 成员准备 → 房主开始
                                           ↓
                         StartCommitted：冻结 1..4 席位
                                           ↓
        同一 TCP 连接 → MatchInitialized → Command/Ack → ScopedSnapshot
                                           ↓
      ConnectionLost → DisconnectedGrace → ReconnectRequest/恢复包
                                           ↓
                         MatchEnded → 有界 flush → Ended
```

`LanLobbyController` 自动创建场景级入口。开始时只停止 UDP 发现并隐藏 Lobby View，不再关闭 TCP 或解除本地 `LocalMatchState` Demo 门控。`LanRoomHost` 保留 listener 与既有 guest socket，把连接原位提升为 Match；普通 Join 从此被拒绝，listener 仅服务当前 Session 的重连。

`MatchSessionHostActor` 是 `MatchAuthority` 的唯一可变调用方。远端命令、房主本地命令、连接事件、时钟和 Battle transport 事件进入同一队列并获得全局 `HostAcceptSequence`；后台读循环只做有界帧读取、严格解码和连接元数据绑定。每个连接由一个容量为 64 的串行 writer 写 `NetworkStream`，只允许在不跨越 Ack/控制消息时原位替换尚未发送的同接收者完整快照。

Match wire 使用独立 `MatchWireEnvelope` 和 4 字节大端长度前缀：绝对上限 4 MiB，控制消息 64 KiB，完整 scoped snapshot 1 MiB，BattleSeal 为 4 MiB 减前缀。正式 M8 运行时清单使用 `lan-match-v2`，因为 BattleSeal 的每场 M7 输入 hash 和公开战斗 Buff 投影是继续对局所必需，旧客户端必须在占席前被兼容检查拒绝。协议使用严格 UTF-8、显式 DTO/方向/范围校验，不依赖运行时类型名。每次权威事务先向发送者排队 `CommandAck`，再为各在线 Human 独立投影并序列化 Public + 本人 OwnerPrivate 完整快照；客户端只原子接受更高 `StateRevision`。

安装级 `PlayerId` 为 `lan-` 加 32 位小写 hex，并通过 `ILocalProfileIdentityStore` 持久化。每个 Human 席位使用 32-byte CSPRNG base64url reconnect token；客户端原子保存单记录 credential，房主只保留 SHA-256 verifier，终局/显式退出时擦除。普通 EOF、超时和端点不可达保留 credential；自动重试为 `0/500/1000/2000/5000ms...`，兼容性不匹配停止本轮重试但保留 token。

运行时兼容清单由真实 Unit/Ability 目录加载结果生成。目录 SHA 基于 Battle Core 的规范化模拟定义，按稳定键排序，排除本地化名称和美术路径；加入、开局和重连均逐字段核对 Protocol、MatchRules、BattleCore、UnitCatalog 与 AbilityCatalog。

M6 提供 BattleSeal、FirstChunkReady、PlaybackStart、PlaybackClock、FinalSecondHash 和 ClientBattleFailure 的严格 transport/恢复顺序。M8 的正式组合根把已验证的连接丢失、恢复 Human 和显式退出 sink 绑定到 M5 `BotController`；Bot 仍只通过 `IMatchBotHost` 调用权威核心。

### 3.4 正式 LAN 运行时与本地 Battle

```text
LanLobbyController
        ↓ StartCommitted / MatchInitialized（保留原 TCP）
LanMatchRuntimeController
        ├─ Host：LanRoomHost → MatchSessionHostActor → MatchAuthority
        │                 └─ BotController（唯一房主调度）
        ├─ Guest：LanRoomClient → ScopedSnapshot（无可写 Authority）
        ├─ 每台机器：LanMatchBattleAdapter → M7 Producers
        │                              → MultiBattlePresentationCoordinator
        └─ LanMatchHudController（只读本机 scoped snapshot/本地表现）
```

`LanMatchRuntimeController` 是正式 LAN 路径的唯一场景组合根。Host 自身也只把按 Host 玩家裁剪的 `ScopedSnapshotPayload` 交给 HUD；房主本地命令与 guest 命令都进入 M6 actor 队列。Guest 从不创建可写 `MatchAuthority`。旧 `PreparationBattleLoopController` 与固定四玩家 Demo 继续用于离线测试，但 LAN handoff 会显式冻结这些入口。

`LanMatchHudController` 是正式 HUD 的 LAN 适配层，不是第二套 View。它查找并保持既有 `FormalBattleHudCanvas` 激活，把 Public/Owner scoped snapshot 投影为 `StagingHudController`、`ShopReadyHudController`、`PlayerListHudController` 和 `FormalBattleHudController` 已有的只读模型，并把旧控件的交互回调路由到 `LanMatchRuntimeController`。LAN 接管期间，离线场景协调器只暂停数据和输入驱动，不销毁或隐藏既有视觉层级；释放后恢复离线订阅、选择回调和部署门禁。禁止再创建平行的 `LanMatchHudCanvas`、停用正式画布或复制商店/玩家列表几何。

运行时目录配置从真实 Unit/Ability Resources 加载器创建，并对模拟字段生成规范 SHA-256。`LanMatchBattleAdapter` 把 M4 的公开封存阵型、公开 Buff/SourceEffect 与每场 `SealedInputHash` 转换为 M7 `BattleInput`；精英 0/1/2/3 分别派生 1/2/3/5 个局部战斗实体，持久 UnitId 只用于稳定派生 EntityId 和结果映射。M4 的规范 hash 使用小写 hex，M7 输入验证对 SHA-256 hex 大小写等价，以避免跨模块格式差异改变语义。

BattleSeal 携带本轮 M4 规范摘要和每场 M7 输入 hash。每台在线机器独立、轮转地计算全部 Official/Shadow 战斗，不接收房主 BattleChunk。全体本地首块完成后发送 FirstChunkReady；房主冻结当时在线 Human 集合，等待全部就绪或 10 秒，且绝不绕过房主本地首块，然后广播未来 1000ms 的统一播放起点。表现 Tick 只由估算的房主单调时钟按 20 TPS 推导；本地计算不足只暂停本机画面，不暂停权威时钟。

房主播放到所有本地战斗的最大 EndTick 后，把自己的 M7 `BattleResolution` 集合映射为 M4 结果并原子提交 `PlaybackCompleted + Settlement`。Guest 上报 Input/FinalSecond hash 仅用于诊断；不一致不会覆盖结算、踢人或改变对局。新 Preparation 快照会释放旧 producers/views；Ended/NoContest 释放运行时和 Session 后直接回主界面，不创建结算页或重赛房间。

重连恢复包保留当前 scoped snapshot、ClockSync、BattleSeal、PlaybackStart 与 PlaybackClock。Preparation 重连以房主 deadline 恢复只读画面并由权威清除 Ready；Battle 重连从 Tick 0 无表现加速重建到当前房主 Tick，再转入正常 one-chunk-ahead 播放。接管 generation 与重连归还均不回滚已接受状态。

### 3.5 Match M1–M5 纯领域权威状态与 AI

```text
四席位初始化 + 兼容清单 + 已验证商品目录
        ↓ MatchSessionFactory
版本化 PRNG → 共享具体 UnitId 池 → 四份六槽初始商店
        ↓
MatchCommandEnvelope → MatchAuthority → MatchEconomyTransactionDraft → MatchState
MatchBotActionId → IMatchBotHost → 同一 Economy/Formation 权威核心
        ↓ 获得具体 UnitId → 自动连锁合成 → Overflow 提升 → 单 revision
准备入口/Ready/超时 → 同 revision 封印 → SealedRoundPlan
BattleResolution → 完整结果 + 回放门控 → 同 revision Settlement
        ↓ 生命/连续/收入/淘汰/排名/自然刷新 → 下一 Preparation 或 Ended
PublicMatchSnapshot
OwnerPrivateSnapshot
HostMatchSnapshot
```

`ARKnoNIGHTS.Match` 不读取 Unity、Resources、系统时间或 Socket。M2 在 M1 的唯一 `MatchAuthority` 上增加不可变 `MatchShopCatalog`、共享具体实体池、`xoshiro256** v1` 随机状态和商店经济。M3 继续扩展同一个内部 `MatchEconomyTransactionDraft`：成功获得单位后，只扫描本次 TypeId 的合法候选区，按 `Deployed > Staging > Overflow > UnitId` 连锁合成，重映射玩家层定向 Buff，受检调整部署 Cost，永久退休被消费 UnitId，再按获取顺序逐个尝试提升 Overflow。任一步失败均丢弃草稿，不推进随机状态、ordinal、tombstone 或 revision。

`MatchStagingProjection` 是 13 槽严格堆叠与规范排序的唯一权威实现；旧 `IStagingSlotPolicy` 构造边界仅为 M2 API 兼容保留，不能覆盖状态派生结果。M4 的封存事务在同一 draft 中依次完成首回合安全购买、最终幸存单位安全部署、Overflow 永久退休、空阵安全部署、配对与战斗输入封印，不发布 `Sealing` 中间快照。

M4 由房主传入单调毫秒时钟，不读取墙上时间。`MatchFlowState` 保存准备期限、配对代次/偏移/历史、当前 `MatchSealedRoundPlan`、已接收结果、回放门控、包含刷新后 seat/pool 输出摘要的结算幂等记录、最终排名和持久外部效果 outbox。四人使用固定六轮双循环；三人每轮一场 Official 和一场 Shadow；两人主客交替。战斗适配层必须回传每场 `BattleId`、该场 `SealedInputHash`、双方非负生命伤害、终止原因和 Tick；Match 根据伤害复核 Outcome，并只按 `SettlementRecipients` 为每名存活玩家应用一个结果。旧 M1 的逐步 `TryAdvancePhase`、手工淘汰与任意字符串结束入口仅以 `internal` 保留给既有领域测试，正式调用方只能使用 M4 原子封印/结算和 `AbortMatchNoContest` 入口。

权限投影保持三层：Public 增加权威剩余时间、公开配对、生命、淘汰、名次、安全终局信息和公开战斗输入所需的持久 Buff；定向 Buff 只投影指向 Deployed/Staging 的记录，不泄露指向 Overflow 的 UnitId/payload。Public 仍不含商店、赤金、等级、Overflow、tombstone 或封印 hash/seed；OwnerPrivate 只含仍存活的本人完整状态，淘汰旁观者只获得 Public；HostMatchSnapshot 额外包含完整 `MatchFlowState`、池、封印计划、结果、结算记录与诊断。Lobby Session 消费 Match 权威命令与投影；PlayerState、Round 和 Battle Demo 仍不是联网权威状态源。

M5 新增单向依赖的 `ARKnoNIGHTS.MatchAI`。`MatchAuthority` 只通过 `IMatchBotHost` 提供本席位 Owner 数据、公开真人 Ready 和目录派生部署 Cost；该接口不暴露 `MatchState`、对手私有经济、共享池、seed、token 或随机状态。`BotDecisionMachine` 对相同观察始终产生相同的 Wait/Buy/Refresh/Upgrade 意图；`BotOperationAdapter` 用 `MatchBotActionId` 调用既有经济与阵型核心，购买成功后只对 `FinalSurvivorUnitId` 尝试一次中心向外部署。`BotController` 在准备入口执行 0ms 周期，随后补处理 1000–29000ms 固定周期，30000ms 由 M4 先封存。NativeBot 与 TakeoverBot 使用递增 controller generation；普通掉线、主动退出和恢复真人控制由 Host 内部入口驱动，状态不回滚。Public/Owner 快照仍不暴露 Bot 身份或策略运行时。M6 只需在已验证连接事件上调用 Controller 的 disconnect/quit/restore 入口，并把时钟推进放入同一房主串行队列。

## 4. 场景与启动入口

`SampleScene` 是唯一构建场景。场景中保留战斗 Demo、单位视图和旧调试对象；以下入口在加载后自动补齐运行时组件：

- `LanLobbyController`：LAN 主界面与房间会话；
- `LanMatchRuntimeController`：正式 LAN Match 生命周期、房主/客机角色、AI、Battle 与重连组合；
- `LanMatchHudController`：把 scoped snapshot 与 LAN 命令映射到既有 `FormalBattleHudCanvas` 的适配层，不拥有平行 HUD；
- `StateDrivenDeploymentController`：状态驱动部署交互；
- `PreparationBattleLoopController`：本地准备/战斗循环；
- `FormalBattleHudController`、`BattleHudSceneCoordinator`：正式 HUD、商店、玩家列表和观察协调；
- `UIManager`：全局 UI 点击路由；
- 显式命令行参数触发的截图或验收 runner。

`BattleDemoController` 仍保留固定真实数据调试能力，但在正式本地回合模式下由 `PreparationBattleLoopController` 提供封存后的输入。

## 5. 当前已实现

- 纯 C#、20 TPS、确定性的 Battle Core；
- 移动、索敌、阻挡、攻击、伤害、死亡、冲门、生命损失与大量 BONDS 能力；
- 主客场坐标投影、Track、动态 Spawn、重播、暂停和倍速；
- PlayerState、准备倒计时、部署/换位/撤退、六槽商店、购买、刷新、冻结、等级与本地观察；
- 固定四玩家本地配对、两场战斗共享时钟演示；
- LAN 房间发现、创建、加入、准备、开始和房间 UI；
- 纯 C# 房主权威 Match M1–M5 领域状态、共享牌库、确定性商店经济、自动合成、Overflow、权威阵型、准备/封印、2/3/4 人配对、战斗结果校验、结算/淘汰/排名、终局效果、分权限快照和确定性 Bot/接管调度；
- Lobby→Match 原连接提升、固定四席位 NativeBot 补位、五字段兼容清单、严格 Match wire、单 actor 命令排序、完整 scoped snapshot、安装级身份、持久 credential、自动重连、终局清理与 Battle transport 契约；
- 正式 LAN 组合根：M5 Bot 调度/接管、真实目录兼容 hash、scoped HUD、M4→M7 Battle adapter、每端全部战斗本地流式计算、首块屏障、统一房主播放时钟、hash 诊断、权威结算和重连恢复；
- EditMode/PlayMode 自动测试、Windows x86_64 构建入口和多种截图证据入口。

## 6. 当前未实现或未闭环

- 主机迁移、专用服务器与反房主作弊；
- hash 不一致的自动处置策略（当前按已确认规则只记录诊断）；
- 结算界面和重赛房间（当前产品规则明确不制作，终局直接回主界面）；
- 目标平台发布配置以及全面的分辨率、设备和人工视觉验收。

不能用本地 Demo 的四玩家 fixture 代替正式联网状态。M6 继续只拥有 transport；M7 Core/Chunk/hash 与 M8 场景组合由上层运行时消费，边界不下沉到 Lobby I/O。

## 7. 修改指南

- 改战斗规则：优先进入 `Battle.Core` 并用确定性 EditMode 测试覆盖；Unity 表现只消费事件。
- 改单位或能力：修改 v2 源和生成器，验证目录投影；不要直接把生成目录当人工事实源。
- 改本地回合：保持 `PlayerState → Round seal → BattleInput` 单向流。
- 改联网：遵守 `Lobby I/O → MatchSessionHostActor → ARKnoNIGHTS.Match → scoped snapshot → Initial 集成` 的单向依赖；后续里程碑继续扩展既有唯一权威状态，不让后台 I/O、View 或 Battle 表现直接调用 `MatchAuthority`。
- 改 UI：读取快照和事件；不要让 View 保存或推断权威规则。
- 改场景、Prefab 或序列化字段：检查 `.meta`、引用和序列化 diff，并执行相应 PlayMode/Player 验证。
