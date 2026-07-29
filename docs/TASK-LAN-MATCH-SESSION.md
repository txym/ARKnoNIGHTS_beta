# M6：LAN 会话升级、权威同步与重连 Agent 提示词

> 状态：待在独立 worktree 中实施。
> 本文件可直接作为实现 Agent 的任务提示词。本轮改造现有 Lobby TCP 生命周期并接入 M1—M5 的 Match 契约；不实现 Battle Core 分块本身或正式 HUD。

## 1. 任务角色与目标

你负责把现有局域网房间连接从“开始时关闭 Socket、放行本地 Demo”升级为持续整个对局的 Match Session：

1. Lobby 连接无缝提升为 Match 连接；
2. 固定四席位和空席原生 AI 初始化；
3. 严格兼容清单握手；
4. 房主单一命令队列与 CommandAck；
5. Public/OwnerPrivate 权限裁剪完整快照；
6. StateRevision 客户端应用规则；
7. 高熵 reconnect token；
8. 安装级持久 PlayerId；
9. 普通断线、自动重连、应用重开恢复；
10. AI 接管/重连归还的连接事件；
11. 房主终止、MatchEnded 和凭据清理；
12. BattleSeal、首块就绪、当前播放 Tick、末秒 hash 的传输契约；5 秒 BattleChunk 由各客户端本地计算，不由房主常规下发；
13. 单连接串行写、后台 I/O 与权威 actor 隔离。

本任务不实现战斗分块算法、AI 策略、结算规则或正式战斗 UI。

## 2. 分支、基线与工作方式

- 使用独立 worktree。
- 分支名使用 `codex/lan-match-session`。
- 必须基于主 Planner 指定的 M4 commit 创建，记录 M1—M4 实际基线 SHA；M5 可在另一 worktree 并行开发，M6 只依赖 `TASK-LAN-MATCH-AI.md` 中已冻结的接管接口语义。
- 不在主工作区直接实现。
- 不创建子 Agent，除非主 Planner明确授权。
- 不合并回主分支；完成后把 commit SHA 和验证证据交给主 Planner。

开始前：

1. 阅读仓库根目录 `AGENTS.md`。
2. 完整阅读：
   - `docs/SPEC.md`
   - `docs/ARCHITECTURE.md`
   - `docs/TEST_PLAN.md`
   - `docs/LAN-MATCH-DESIGN.md`
   - `docs/LAN-MATCH-IMPLEMENTATION-PLAN.md`
   - `docs/TASK-LAN-MATCH-DOMAIN.md`
   - `docs/TASK-LAN-MATCH-ECONOMY.md`
   - `docs/TASK-LAN-MATCH-FUSION.md`
   - `docs/TASK-LAN-MATCH-FLOW.md`
   - `docs/TASK-LAN-MATCH-AI.md`
   - `docs/TASK-BATTLE-STREAMING.md`
3. 阅读 M1—M4 实际实现和测试，并读取 M5 提示词定义的连接/接管接口；若 M5 commit 已经由主 Planner合入基线，再一并检查实际实现。
4. 完整检查现有：
   - `Assets/Game/Runtime/Initial/LanLobbyController.cs`
   - `Assets/Game/Runtime/Lobby/LanRoomHost.cs`
   - `Assets/Game/Runtime/Lobby/LanRoomClient.cs`
   - `Assets/Game/Runtime/Lobby/LobbyProtocol.cs`
   - `Assets/Game/Runtime/Lobby/LobbyRoomState.cs`
   - `Assets/Game/Runtime/Lobby/LobbyModels.cs`
   - `Assets/Game/Runtime/Lobby/LanDiscoveryService.cs`
   - Lobby EditMode/PlayMode/socket integration 测试
5. 确认当前事实：
   - `CompleteGameplayTransition()` 调用 `StopServices(false)`，会关闭 TCP；
   - Match 开始前断开会从 Lobby 移除成员；
   - 当前 `PlayerId` 每次启动使用新 GUID；
   - 当前四字节大端帧最大 4096 bytes、snapshotJson 最大 2048 bytes；
   - 当前协议无法承载完整 Match 快照。
6. 执行 `git status --short` 并保护已有修改。
7. 确认没有 Unity 进程占用该 worktree。
8. 运行 M1—M4 和 Lobby 相关测试作为基线。

## 3. 生命周期

实现明确状态：

```text
Discovering
→ Lobby
→ StartCommitted
→ Match
→ Ended
```

### 3.1 Lobby

- 继续使用当前房间发现、六位房间号、成员 Ready 和开始条件。
- 允许 1—4 真人，所有当前真人 Ready 后房主可开始。
- Lobby 阶段 guest 连接断开仍删除成员并释放席位。
- Lobby 阶段 host 停止服务仍解散房间。
- 加入时执行完整兼容清单检查。

### 3.2 StartCommitted

房主接受开始后，在一个明确的 start commit 中：

1. 冻结当前 Lobby 成员顺序和身份。
2. 固定席位 `1..4`，真人保留顺序，其余由 NativeBot 填充。
3. 停止/撤销 LAN 房间发现广播。
4. Lobby listener 和所有已连接 guest TCP 必须继续存活。
5. listener 从此拒绝普通 JoinRequest，只接受属于当前 Session 的 ReconnectRequest。
6. 从实际目录/版本构造 CompatibilityManifest。
7. 创建 SessionId、MatchSeed、真人 reconnect credential。
8. 初始化 M1—M4 MatchAuthority 和 M5 BotController。
9. 向每个连接发送按本人裁剪的 MatchInitialized。
10. 隐藏 Lobby UI，把持续 session 所有权交给 Match runtime/controller。

不得再调用当前 `StopServices(false)` 来完成游戏切换，也不得只 `SetLobbyGate(false)` 继续本地 `LocalMatchState` Demo。

### 3.3 Match

- TCP listener 留用于重连。
- 现有 guest connection 继续作为原玩家连接，不要求重新加入。
- Match 断开只更新 Connection/Ready/接管计划，不删除席位或玩家状态。
- 陌生 PlayerId、普通 Join 和原生 AI 席位认领都拒绝。
- MatchAuthority 持续到竞技结束或 host abort。

### 3.4 Ended

- 尽力发送 MatchEnded 并在有界 flush 后关闭 session 连接。
- 停止 heartbeat、读写 task 和 listener。
- 房间号失效。
- 清除本机属于该 Session 的 reconnect credential。
- 回主界面，不创建重赛房间。
- 可以在 host 进程内短期保存不含 token 的 ended SessionId tombstone，使同一 listener 生命周期内的迟到重连得到 SessionEnded；不能因此保留 MatchState 或重新开放房间。

## 4. 身份和四席位

### 4.1 安装级 PlayerId

当前每次 `LoadProfile()` 调用 `Guid.NewGuid()` 必须改为：

- 首次运行生成一次高质量唯一 PlayerId。
- 持久保存在本机 Profile store。
- 后续启动和头像变更复用同一 ID。
- 格式固定、验证严格，例如 `lan-` 加 32 位小写 hex。
- 空值、非法格式或损坏时重新生成并保存。
- DisplayName 仍按当前头像规则产生；改头像不能改 PlayerId。

把持久化抽象成：

```text
ILocalProfileIdentityStore
```

Unity adapter 可使用 PlayerPrefs；纯测试使用内存 fake。不要在纯网络/领域层直接调用 PlayerPrefs。

### 4.2 NativeBot 身份

- 使用没有被真人占用的 AvatarId。
- 从合法头像中按稳定升序选择。
- DisplayName 使用正常头像派生名称。
- 身份在 StartCommitted 冻结。
- Public 不携带 Bot 标签。
- 若合法头像数量不足以做到全部不重复，开始失败并给结构化诊断；不要复用真人头像或增加 UI 标记。

### 4.3 席位

- Lobby 冻结成员依当前权威显示顺序映射到 Seat 1..N。
- AI 填 N+1..4。
- Match 中不压缩、不重排、不复用。
- Connection 绑定 PlayerId 和 SeatIndex，但 wire 命令不能用 payload PlayerId 越权。

## 5. 兼容清单

加入和重连必须逐字段严格相等：

```text
ProtocolVersion
MatchRulesVersion
BattleCoreVersion
UnitCatalogSha256
AbilityCatalogSha256
```

要求：

- 复用 M1 `MatchCompatibilityManifest` 语义。
- 从实际运行时生成目录/版本构造，不能硬编码当前 94 个商品或能力处理顺序。
- 单位/能力 SHA 为 64 位规范十六进制 SHA-256。
- 头像、美术、语言文本不进入模拟兼容 hash。
- Lobby JoinRequest 已携带清单；不匹配在占用席位前拒绝。
- StartCommitted 再验证所有已冻结真人仍有同一清单。
- ReconnectRequest 再验证。
- CompatibilityMismatch 是明确拒绝码。
- 自动重连遇到 CompatibilityMismatch 时停止本轮自动循环并显示“版本不兼容”，但保留 token，允许用户更新到兼容版本后再次恢复；不要把版本不匹配误当 InvalidToken。

若实际单位/能力目录尚未提供可靠 hash adapter，停止并报告依赖，不使用空字符串或固定假 hash。

## 6. Match wire 协议

### 6.1 帧

保留当前 TCP 四字节大端 payload 长度前缀。

定义独立的 Match 协议，不把全部字段继续塞进 `LobbyWireMessage`：

```text
MatchWireEnvelope
  ProtocolVersion
  SchemaVersion
  Kind
  SessionId
  MessageId
  Payload
```

要求：

- 严格 UTF-8，无 BOM，非法字节拒绝。
- 收到长度前缀后先检查上限，再分配 payload buffer。
- 负数、0、超上限、截断帧、尾随额外字节均拒绝。
- 未知 Kind、SchemaVersion、缺字段、重复/非法身份、非法 enum 拒绝。
- Kind-specific payload 使用明确 DTO/validator，不使用 `Dictionary<string, object>`。
- 不依赖运行时类型名反序列化。
- 数字使用受检范围。
- 字符串有长度上限。
- 每个连接出现协议错误后发送可安全 Reject（若可能）并关闭，不能继续在失步流上猜帧。

第一版上限：

```text
AbsoluteMaximumFrameBytes = 4 MiB（包含 4-byte prefix）
ControlPayloadMaximum = 64 KiB
ScopedSnapshotPayloadMaximum = 1 MiB
BattleSealPayloadMaximum = 4 MiB - prefix
```

不实现压缩。若真实合法 fixture 超过上限，先报告测量数据，不静默扩大到无界。

### 6.2 Kind

至少定义：

```text
Handshake
HandshakeAccepted
Reject
MatchInitialized
Command
CommandAck
ScopedSnapshot
SnapshotRequest
ClockSync
BattleSeal
FirstChunkReady
PlaybackStart
PlaybackClock
FinalSecondHash
ClientBattleFailure
ReconnectRequest
ReconnectAccepted
ReconnectRejected
ExplicitQuit
MatchEnded
Ping
Pong
```

M6 只实现 Battle 相关 payload 的传输、验证和路由契约；M7 实现 Chunk 内容、检查点和 hash 计算。

每种 Kind 明确允许方向：

- client→host
- host→client
- 双向

收到方向非法的消息视为协议/权限错误。

## 7. 连接、线程与 actor

### 7.1 后台 I/O

每个 connection：

- 一个读取循环；
- 一个有界发送队列；
- 一个串行写循环；
- 一个 ConnectionId 和单调 ConnectionGeneration；
- 独立 cancellation；
- 所有 SendAsync 只能入队，不能多个 task 并发写同一 NetworkStream。

### 7.2 权威队列

后台读取只做：

1. 帧读取；
2. 严格解码/验证；
3. 绑定 connection metadata；
4. 入 `MatchInboundQueue`。

MatchAuthority 只在 Unity 主线程或单一专用 actor 中处理：

- 为每个出队事件分配全局 `HostAcceptSequence`；
- host 本地命令也进入同一队列；
- connection 断开、重连、时钟和 Battle ready 事件也串行化；
- 不允许后台线程直接调用 MatchAuthority 或改 MatchState。

测试必须证明远端和 host 本地命令的最终顺序严格等于 HostAcceptSequence。

### 7.3 背压

- 发送队列必须有界。
- CommandAck、MatchEnded、Reconnect、BattleSeal 等控制消息不可被静默丢弃。
- 连续 ScopedSnapshot 可以只保留尚未发送的最新 revision，因为每份都是完整快照；不得合并不同玩家或越过必要 CommandAck。
- 若非快照消息持续塞满队列，断开该慢连接并进入普通掉线流程，不能阻塞 MatchAuthority。
- “首块计算慢”不是 socket 断线，不触发接管。

## 8. reconnect credential

### 8.1 签发

Match 初始化时为每个 Human 席位签发：

- 32 bytes CSPRNG；
- base64url 无填充 wire 表示；
- 每个 Session/Player 唯一。

要求：

- 使用平台密码学随机 API，不使用 MatchSeed、System.Random、Unity Random 或 Guid。
- raw token 只发送给对应本人并保存在本人本地 credential store。
- host 只保存 SHA-256 token digest 或等价单向 verifier。
- 比较使用固定时间。
- token 不进入 Public、Owner snapshot、普通 Host UI、CanonicalSummary、异常消息或日志。
- 日志只可记录 SessionId、Seat、结果码，不记录 raw token/digest。

### 8.2 本地持久化

```text
ReconnectCredential
  SessionId
  HostAddress
  HostPort
  PlayerId
  Token
  CompatibilityManifest
```

通过：

```text
IReconnectCredentialStore
```

持久化。

- MatchInitialized/ReconnectAccepted 后原子保存。
- ExplicitQuit、权威 MatchEnded、SessionEnded、InvalidToken 后删除。
- 单纯端点不可达、socket timeout、连接重置不删除。
- CompatibilityMismatch 不删除，但停止当前自动循环。
- 测试不得写真实用户 PlayerPrefs；使用 fake 或隔离键。

若 PlayerPrefs adapter 无法原子更新多字段，保存单一带 schema/checksum 的规范记录；损坏记录 fail-closed 删除，不猜字段。

## 9. 命令与确认

Client Command 至少映射：

```text
SessionId
PlayerId
ConnectionGeneration
CommandId
KnownStateRevision
CommandKind
TypedPayload
```

Host 验证：

- envelope SessionId 匹配；
- connection 已认证并绑定 PlayerId；
- payload PlayerId 必须与绑定身份相同；
- generation 是当前 active generation；
- CommandKind 已知且允许；
- 映射成 M1—M4 强类型命令；M5 接管/归还使用独立 Host 事件 adapter；
- 不直接反序列化成任意领域类型；
- 交由 MatchAuthority 幂等/语义验证。

CommandAck：

```text
CommandId
ResultCode
CurrentStateRevision
AcceptedStateRevision
DidChangeState
StableDetailCode
```

- 房主按处理顺序先为发送者排队 Ack，再发布该 revision 的 scoped full snapshot。
- 相同 CommandId 重发返回领域缓存的相同结果。
- 拒绝命令发送 Ack；如果结果表示客户端明显落后，可再发当前 scoped snapshot。
- Client 收到 Ack 前不提交本地预测为权威状态。
- 不实现客户端经济/牌库预测回写。

## 10. Scoped full snapshot

第一版每次成功权威事务后，为每个当前连接分别生成完整快照：

```text
ScopedSnapshot
  SessionId
  StateRevision
  PublicState
  OwnerPrivateState?
  LocalConnectionState
```

规则：

- 未淘汰 Human 收到 Public + 自己 OwnerPrivate。
- 淘汰旁观 Human 只收到 Public，不收到存活玩家或自己的私有经济。
- host 玩家 HUD 同样使用自己 scoped snapshot；不能直接绑定 HostOnly。
- NativeBot 无网络接收者。
- 不同接收者必须独立裁剪后序列化，不能先序列化 HostOnly 再依靠 UI 隐藏。
- 每次发送前有自动隐私测试/validator，确认不含 Pool、ControllerKind、token、其他玩家 Shop/Gold/Cost/Level/Overflow。
- Public 包含公开 Deployed/Staging；Owner 才包含自己的 Overflow 和商店。
- HostOnly 永不走普通 snapshot wire。

Client 应用：

- 尚无 snapshot 时接受首份合法 snapshot。
- `revision > current`：原子替换本地只读投影。
- `revision == current`：重复丢弃，不重复通知 UI。
- `revision < current`：丢弃。
- 不允许局部 merge。
- SnapshotRequest 返回当前 scoped full snapshot。

未来若加 delta，必须 BaseRevision/TargetRevision 且缺口回退 full；M6 不实现 delta。

## 11. Lobby 到 Match 的初始化消息

`MatchInitialized` 只发送给该连接，至少包含：

- SessionId
- 本人 PlayerId/SeatIndex
- HostPlayerId
- CompatibilityManifest
- Match rules/seed 中客户端需要的公开部分
- reconnect token
- 当前 ScopedSnapshot
- 当前 M4 phase clock

要求：

- 四席位/AI 外观一致。
- token 只在本人的消息中。
- guest 不因 Start 重连 TCP。
- 初始化消息写入完成失败只把该 guest 标记掉线，Match 仍按冻结席位开始；它可用 token 重连。
- host local credential 走本地 store，不通过 network loopback。
- MatchInitialized ack 可用于诊断，但不能让无响应 guest 永久阻止开始。

## 12. 心跳和断开语义

可以沿用当前 `1000 ms` Ping/Pong 和 `3` 次漏回阈值，前提是：

- Lobby：断开调用 RemovePlayer，释放席位。
- StartCommitted/Match：断开只解绑 active connection，保留 Seat/PlayerState，向 authority 入队 `ConnectionLost`。
- Preparation 断开由 M4 清 Ready。
- M5 根据 phase/round 安排接管。
- 心跳使用 connection generation，旧连接 Pong 不能恢复新连接状态。
- 延迟只作 UI/诊断，不决定 Battle 首块慢或 AI 接管。

异常 parse、发送失败、EOF 和 heartbeat expiry 统一走一次幂等 disconnect 入口。

## 13. 自动重连

### 13.1 运行中断线

Client 检测断开后：

- 保留当前只读快照。
- UI/运行状态进入 Reconnecting，停止发送普通命令。
- 使用持久 credential 连接 HostAddress/Port。
- 不要求房间号，不重新走 Lobby Join。
- 固定重试间隔：

```text
0 ms, 500 ms, 1000 ms, 2000 ms, 5000 ms, 5000 ms...
```

- 没有“连续失败 N 秒后自动放弃”的时限。
- 单纯不可达永不清 token。
- 用户可以主动返回主界面，此操作等价 ExplicitQuit/local abandon，清 token 并停止重试。
- 使用可取消异步调度，不在主线程阻塞 sleep。

### 13.2 应用重开

启动时：

- 先加载并校验 ReconnectCredential。
- 有合法记录就直接进入 Reconnecting 并按 endpoint 尝试，不显示/要求房间号。
- endpoint 暂时不可达时继续上述重试。
- CompatibilityMismatch 停止重试并提示更新，但保留 credential。
- InvalidToken/SessionEnded 清除并回主界面。

### 13.3 重连优先的可检测性边界

客机无法仅凭 TCP 不可达区分：

- 客机自身断网；
- 路由/Wi-Fi 暂时故障；
- 房主进程崩溃。

因此：

- host 崩溃在权威语义上使 Session 立即不存在；
- 但没有收到权威拒绝的客机继续显示 Reconnecting；
- 不因超时擅自清 token 或制造 MatchEnded；
- 玩家主动返回时清除；
- 不增加客户端选主、P2P 共识或主机迁移。

## 14. ReconnectRequest

必须携带：

```text
SessionId
PlayerId
RawToken
CompatibilityManifest
ClientLastAppliedRevision
```

Host 按顺序验证：

1. 协议和 schema。
2. SessionId 当前 active 或明确 ended tombstone。
3. CompatibilityManifest 严格相等。
4. PlayerId 对应开局 Human 席位。
5. 该玩家没有 ExplicitQuit。
6. token verifier 固定时间匹配。
7. Match 未 Ended。

成功：

- 为新 socket 分配更高 ConnectionGeneration。
- 原 active socket 若仍存在则关闭并失效。
- 原席位恢复 Connected。
- 调用 M5 `RestoreHumanControl`；AI 已改状态不回滚。
- Preparation 中 Ready 保持/设为 false。
- 淘汰玩家恢复为 spectator connection，不恢复命令权或 OwnerPrivate。
- token 不旋转，避免 ReconnectAccepted 丢包造成旧 token 永久失效。
- 发送 ReconnectAccepted 和第 15 节恢复包。

拒绝码至少：

```text
SessionEnded
UnknownSession
InvalidToken
UnknownPlayer
ExplicitlyQuit
CompatibilityMismatch
MalformedRequest
```

客户端清理：

- SessionEnded/InvalidToken/ExplicitlyQuit：清 credential，回主界面。
- CompatibilityMismatch：保留并停止当前循环。
- UnknownSession：
  - 若 host 明确证明当前 endpoint 已是另一个 Session，按 SessionEnded 清除；
  - 若只是无法连接，不会收到该码，继续重试。

Reject 不能回显 token，也不能区分过细到便于枚举 PlayerId/token。

## 15. 重连恢复包

ReconnectAccepted 后按顺序发送：

1. 当前 ScopedSnapshot。
2. 当前 Phase/Round 的 ClockSync。
3. 若处于 Sealing/Battle/Settlement，发送当前 SealedRoundPlan/BattleSeal。
4. 当前房主统一 Playback Tick 和未来播放基准。

战斗中：

- 从房主当前 Tick 开始显示，不从 0 重播。
- 客户端收到 BattleSeal 后使用 M7 在本机加速计算从 Tick 0 到当前房主 Tick，期间不逐 Tick 播放；完成相同 Core 计算后，用本机生成的最近检查点绑定当前表现。
- 检查点只用于本机状态/表现重建，不允许跳过 Core 或直接信任房主战斗状态。
- 当前 Tick 已死亡单位不创建、不补死亡动画。
- 当前仍存活单位按 checkpoint/current sample 创建。
- 正在进行的动作允许从动作开头重新播放。
- 不存在正式暂停、倍速或 Replay。

M6 只负责顺序和 transport；具体 checkpoint/Track 重建由 M7/M8。

## 16. Battle 同步消息边界

M6 为 M7 提供：

### Host→Client

- BattleSeal：全部 Official/Shadow input、版本、seed、canonical input hash。
- PlaybackStart：统一未来 host monotonic start。
- PlaybackClock：当前全局 presentation tick。
- MatchEnded。

### Client→Host

- FirstChunkReady：Session/Round/Battle set/input hash/ready revision。
- FinalSecondHash：每 Battle 的 canonical SHA-256。
- ClientBattleFailure：客户端本地 Core/Producer/播放器管线失败的稳定诊断码；仅用于诊断，不替代权威战果、不触发自动踢出或 AI 接管。

要求：

- FirstChunkReady 只是计算就绪，不是 heartbeat。
- 慢 client 不得被标记 disconnected 或触发 AI。
- host 本地计算也通过同一 round coordinator 报 ready，但不走 socket。
- 每台客户端的 100 Tick BattleChunk/checkpoint 由本机 M7 Producer 生成并直接交给本机播放机；正常流程不通过网络传输房主 chunk，也不能用房主 chunk 代替独立 Core 计算。
- M7 负责最多等待在线 clients 10 秒、统一开始和 mismatch 暂缓；M6 不自行结算 hash。
- 所有消息校验 Session/Round/BattleId/input hash，迟到旧轮消息丢弃并诊断。

## 17. ExplicitQuit 和 MatchEnded

### 17.1 Guest ExplicitQuit

- 必须是已认证连接消息。
- host actor 调用 M4/M5 的阶段对应退出处理。
- 标记 token 永久无效。
- 返回 QuitAccepted/最终公开 snapshot（若可）。
- client 清 credential 并回主界面。
- 断线不等于 ExplicitQuit。

### 17.2 Host ExplicitQuit

- host 本地命令进入同一 actor。
- 调用 M4 `AbortMatchNoContest`。
- 尽力广播 MatchEnded(NoContest)。
- 清全部 token verifier。
- 关闭 session。
- 不创建房主 AI。

### 17.3 Competitive MatchEnded

- 发送公开 EndReason/FinalStandings/FinalRevision。
- 不发送其他玩家私有经济。
- client 收到后原子标记 Ended、清 credential、回主界面。
- 相同 MatchEnded 重发幂等。

### 17.4 Host 进程崩溃

- host 无法广播时，服务端 Session 事实上立即消失。
- 客机按第 13.3 节持续重连，直到主动退出。
- 不声称能够从单个 TCP 断开证明 host crash。

## 18. 日志与敏感信息

禁止记录：

- raw reconnect token；
- token digest；
- 完整 ReconnectRequest；
- 其他玩家 OwnerPrivate snapshot；
- 可能包含 token 的未清洗 JSON/frame。

允许记录：

- SessionId；
- ConnectionId/generation；
- SeatIndex；
- Message Kind；
- StateRevision；
- 长度；
- 稳定错误码；
- HostAcceptSequence。

异常日志使用显式 redaction。测试捕获日志并断言 token 子串从未出现。

## 19. 错误和关闭

- 所有网络 task 的异常必须被观察，不能 silent fire-and-forget。
- `StopAsync` 幂等并等待 accept/read/write/heartbeat task 有界结束。
- cancellation 不能误触发 Match guest ExplicitQuit。
- scene unload/app quit：
  - host：NoContest 终止；
  - guest 正常 app quit 若无法发送 ExplicitQuit，下一启动仍可用 token 重连；只有用户在游戏内明确退出才清 token。
- 不吞异常后继续使用损坏 stream。
- 不在 catch-all 中删除 Match seat。

## 20. 明确不在 M6 范围

不得实现：

- Battle 100 Tick chunk 的内容生成；
- Battle Core/checkpoint/hash 算法；
- hash mismatch 纠错、踢出或终止；
- AI 策略；
- Match 经济、合成、配对或结算重写；
- 增量 snapshot/delta；
- 压缩或加密传输；
- Internet relay/NAT traversal；
- 专用服务器；
- 主机迁移；
- 反房主作弊；
- 自动重连无响应超时；
- 结算界面或重赛房间；
- 正式 HUD/场景全面接线；
- 牌库外赠送；
- Unity/Package 升级或第三方网络库。

继续使用 .NET TCP/UDP 和项目现有依赖，不安装新包。

## 21. TDD 与测试要求

先写失败测试，再实现。至少覆盖：

### 21.1 协议

- 每种 Kind 合法 round-trip。
- 四字节大端长度。
- 严格 UTF-8。
- 0、负数、截断、超上限、尾随、未知 kind/schema/direction 拒绝。
- Control/Snapshot/Battle 不同上限。
- Kind-specific 缺字段/非法 enum/超长字符串拒绝。
- 不使用任意类型反序列化。

### 21.2 身份和兼容

- PlayerId 首次生成后跨 controller/restart store load 稳定。
- 改头像不改 PlayerId。
- 损坏 ID 重建。
- 兼容五字段任一不同都在占席前拒绝。
- 目录输入顺序不改变规范 hash。
- AI 使用未占头像且不显示 Bot。

### 21.3 生命周期

- Lobby start 后 UDP discovery 停止。
- StartCommitted 不关闭 listener/guest TCP。
- 普通 Join 在 Match 阶段拒绝。
- 已连接 guest 不需重连即可收到 MatchInitialized。
- Match 断开不删除 Seat。
- Lobby 断开仍删除成员。
- Ended 才关闭 session。
- 当前 `CompleteGameplayTransition` 不再调用 StopServices 进入 Match。

### 21.4 命令顺序

- 多 guest 并发和 host 本地命令得到唯一递增 HostAcceptSequence。
- MatchAuthority 只在 actor thread/tick 调用。
- 同 CommandId 重发不重复变更。
- 伪造 PlayerId、旧 generation、错误 Session 拒绝。
- Ack 与 scoped snapshot revision 一致。

### 21.5 隐私

- A snapshot 有 A Owner，无 B Owner。
- B snapshot 有 B Owner，无 A Owner。
- 淘汰 spectator 只有 Public。
- host HUD scoped snapshot 不含 HostOnly。
- wire JSON 不含 Pool、ControllerKind、token、其他 Gold/Shop/Overflow。
- Reconnect token 仅在对应初始化/accepted 消息。

### 21.6 Revision

- 新 revision 原子替换。
- 相同/更旧 snapshot 丢弃且不重复通知。
- 乱序发送测试最终只保留最高 revision。
- SnapshotRequest 返回当前 scoped full。
- 不存在部分 merge/delta。

### 21.7 Token

- 32-byte CSPRNG/base64url 格式。
- 各玩家/token 唯一。
- host 只存 verifier。
- 固定时间验证路径。
- token 不在日志/异常/snapshot。
- ExplicitQuit/MatchEnded/InvalidToken/SessionEnded 清除。
- 端点不可达、EOF、timeout 不清除。
- CompatibilityMismatch 保留。

### 21.8 重连

- 普通断线进入 grace，Seat/状态保留。
- 0/500/1000/2000/5000ms 重试序列。
- 长时间不可达持续重试，不自动回主界面。
- 应用重开不需要房间号。
- 合法 token 替换旧 connection generation。
- 非法 token、陌生 player、AI seat、explicit quit 拒绝。
- 接管后重连恢复 Human，不回滚 AI 状态。
- 淘汰玩家重连只恢复 spectator。
- Preparation 重连 Ready=false。

### 21.9 Battle 恢复契约

- Battle 重连依序收到 scoped snapshot/clock/seal/playback tick/checkpoint。
- 旧 round Battle 消息丢弃。
- FirstChunkReady 慢不触发 disconnect。
- 当前播放 Tick 可高于客户端断开 Tick。
- transport 不要求从 Tick0。

### 21.10 背压和关闭

- 同 connection 绝无并发 NetworkStream write。
- unsent full snapshot 可安全合并到最新。
- Ack/End/Seal 不丢。
- 队列溢出只断开慢 guest，不阻塞 authority。
- StopAsync 幂等、所有 task 被观察、端口释放。
- cancellation 不误作 ExplicitQuit。

### 21.11 真实 loopback 冒烟

使用现有 socket integration 风格，在一个进程内：

1. Host + 1 guest Lobby join/ready/start。
2. 验证 TCP 端口和连接在 MatchInitialized 后仍存活。
3. Guest 发一个 Ready/Shop 可用的 Match 命令。
4. 收到 Ack 和只含本人 Owner 的 snapshot。
5. 强制关闭 guest socket。
6. Host Seat 保留并进入 DisconnectedGrace。
7. 用保存 token 新建连接。
8. 收到当前 revision 恢复包。
9. Host End，guest 清 token，端口释放。

不得依赖外部网络、账号或服务。

### 21.12 回归

- M1—M4 全部测试继续通过；M5 已合入时也必须保持通过。
- 现有 Lobby room join/ready/leave/start 测试更新后保持 pre-start 行为。
- 不修改视觉几何或截图基线。

## 22. 验证顺序

按仓库规范执行并记录：

1. `git diff --check`
2. 审查最终 diff、asmdef 和 `.meta`
3. Match/Lobby/Session C# 编译
4. M1—M4 既有 EditMode 测试；若基线已含 M5，则同时运行 M5
5. Protocol/Session/Reconnect 新 EditMode 测试
6. 现有和新增 loopback socket integration 测试
7. 相关 PlayMode controller 生命周期测试
8. 如环境允许，运行最小 2 进程/2 Player LAN 冒烟；否则明确未验证
9. 检查日志无 token、无新增异常
10. 独立审查：
   - Start 是否仍关闭 TCP
   - Match disconnect 是否删除 Seat
   - host local 命令是否绕过队列
   - snapshot 是否跨玩家泄密
   - raw token 是否进入日志/Host snapshot
   - 不可达是否错误清 token
   - 旧 connection 是否仍可发命令
   - StopAsync 是否遗留端口/task

测试数为 0、Unity 占用、许可证失败、超时、结果 XML 缺失或日志不完整均标为未验证。

## 23. 完成交付

最终提交前：

- 更新实际受影响的架构/测试文档，不改写无关 SPEC。
- 不提交 Library、Temp、Logs、Build、Artifacts 或无关用户修改。
- 创建一个聚焦 M6 的 commit。

交付给主 Planner：

1. 分支名和 commit SHA；
2. M1—M4 基线 SHA，以及是否包含 M5；
3. 修改文件；
4. Session lifecycle、protocol、actor、snapshot、token、reconnect 关键 API；
5. M7/M8 接入点；
6. 实际测试命令、退出码、测试数量和结果文件；
7. 未验证项和风险；
8. 明确没有实现 M7 Battle 内容或 M8 正式 HUD。

停止条件：

- M4 不是指定基线；
- 单位/能力目录无法提供实际兼容 hash；
- M1—M4 投影无法按接收者裁剪；
- 现有 Lobby connection 无法安全提升且替代方案会丢失已连接 guest；
- 需要第三方网络库、管理员权限、外部服务或 Unity/Package 升级；
- SPEC 与本文出现新的玩家可见重连冲突；
- 连续三次有实质差异的尝试仍被同一 socket/生命周期测试阻塞。

遇到停止条件时保留日志和复现证据并报告，不得关闭 TCP 后假装联网、广播 HostOnly、硬编码目录 hash 或在连接失败时删除席位。
