# M1：Match 房主权威领域骨架 Agent 提示词

> 状态：待在独立 worktree 中实施。
>
> 本文件可以直接作为实现 Agent 的任务提示词。本文只覆盖 M1，不得提前实现 M2—M8。

## 1. 任务角色与目标

你负责建立 ARKnoNIGHTS 局域网完整对局的纯 C# 房主权威领域骨架。

当前项目的 `LocalMatchState` 是单机 UI Demo 状态：只有本地玩家拥有真实 Gold、Level、Ready 命令所有权，商店由每机本地随机数生成。它不能扩展成联网权威状态。

本任务要新增独立 `ARKnoNIGHTS.Match` 领域程序集，提供：

1. 固定四席位 Match Session；
2. 唯一房主权威可变状态；
3. 单调 StateRevision；
4. 稳定 CommandId 幂等；
5. 原子命令接受/拒绝；
6. Public / OwnerPrivate / HostOnly 三层权限快照；
7. 基础 Phase、Connection、Controller、Elimination、Ready 状态；
8. 严格兼容清单；
9. 稳定规范摘要；
10. 给后续共享牌库、经济、合成、配对、AI、LAN 和 UI 提供清楚的扩展边界。

本任务不接 Socket、不改场景、不替换现有本地 Demo。

## 2. 工作方式

- 使用独立 worktree。
- 分支名使用 `codex/lan-match-domain`。
- 不在主工作区直接实现。
- 不创建子 Agent，除非主 Planner 明确授权。
- 不合并回主分支；完成后把 commit SHA 和验证证据交给主 Planner。

开始前：

1. 阅读仓库根目录 `AGENTS.md`。
2. 完整阅读：
   - `docs/SPEC.md`
   - `docs/ARCHITECTURE.md`
   - `docs/TEST_PLAN.md`
   - `docs/LAN-MATCH-DESIGN.md`
   - `docs/LAN-MATCH-IMPLEMENTATION-PLAN.md`
3. 检查：
   - `Assets/Game/Runtime/Data/Player/LocalMatchState.cs`
   - `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs`
   - `Assets/Game/Runtime/Round/PreparationBattlePhase.cs`
   - `Assets/Game/Runtime/Lobby/LobbyModels.cs`
   - `Assets/Game/Runtime/Lobby/LobbyRoomState.cs`
   - 对应 EditMode 测试和 asmdef
4. 执行 `git status --short`，保护 worktree 现有修改。
5. 确认没有 Unity 进程占用该 worktree。
6. 建立相关现有 EditMode 基线。

本文已确认规则若与旧 Demo 描述冲突，以本文为本任务需求；不要为了保持 `LocalMatchState` 的旧形状而破坏新领域边界。

## 3. 程序集与目录

建议新增：

```text
Assets/Game/Runtime/Match/
  ARKnoNIGHTS.Match.asmdef
  Contracts/
  State/
  Authority/
  Projection/
  Compatibility/
```

建议新增测试：

```text
Assets/Game/Tests/EditMode/Match/
  ARKnoNIGHTS.Match.EditModeTests.asmdef
  MatchSessionFactoryEditModeTests.cs
  MatchAuthorityEditModeTests.cs
  MatchSnapshotProjectionEditModeTests.cs
  MatchCompatibilityEditModeTests.cs
```

约束：

- `ARKnoNIGHTS.Match` 必须是纯 C# 领域层。
- 优先设置 `noEngineReferences: true`。
- 不依赖 `UnityEngine`、Resources、MonoBehaviour、Socket、Lobby UI、Presentation 或 Demo。
- 如果 M1 不需要引用现有程序集，就保持零引用。
- 不引用或复用 `LocalMatchState`；它的 `LocalPlayerId` 和本地私有字段模型不适合权威层。
- 不复制 `LobbyProfile` 规则。M1 接收已经解析好的四席位身份输入，不负责选择 AI 头像和名字。
- 新增 `Assets/` 文件时必须有匹配 `.meta`。

## 4. 已确认领域规则

### 4.1 四个固定席位

- Match 初始化时必须恰好得到四个席位，索引固定为 `1..4`。
- 席位索引唯一且开局后永不压缩、重排或复用。
- 每个席位有唯一非空 PlayerId。
- 必须恰好有一个 HostPlayerId，且它对应一个真人席位。
- 允许 `1—4` 个真人；其他席位在传入 M1 前已经被上层解析为原生 AI 身份。
- M1 不自行随机选择 AI 头像或名字，只验证四席位输入完整。
- DisplayName、AvatarId 和开局 SeatIndex 在局内冻结。

建议初始化输入：

```text
MatchInitializationRequest
  SessionId
  MatchSeed
  HostPlayerId
  CompatibilityManifest
  Seats[4]

MatchSeatInitialization
  SeatIndex
  PlayerId
  DisplayName
  AvatarId
  InitialControllerKind
```

`SessionId` 和 `MatchSeed` 由未来房主会话层生成并传入；M1 不读取系统时间或自行生成随机数。

### 4.2 初始玩家数值

M1 建立权威初始字段：

- Life = `400`
- Gold = `7`
- Level = `1`
- TotalDeploymentCost = `16`
- AvailableDeploymentCost = `16`
- Ready = `false`
- Eliminated = `false`
- Placement = 未定
- Units、Shop、Overflow = 空

M2 会在同一 Match 初始化事务中生成共享牌库并执行初始自然刷新。M1 不生成假商店、假单位或临时 TypeId。

### 4.3 Phase

至少定义：

```text
Initializing
Preparation
Sealing
Battle
Settlement
Ended
```

M1 只建立合法状态表示和最小内部转换入口，不实现完整 30 秒计时、配对、BattleSeal 或结算。

- 外部玩家命令不能直接设置 Phase。
- Host 内部转换必须经过 MatchAuthority。
- 非法倒退、跳跃或 Ended 后修改要结构化拒绝。
- M1 可以提供 `AllRequiredHumansReady` 只读判定，但不要在本任务里实现完整自动封存。
- M4 将在 AI 初始决策周期、倒计时和阶段封存事务全部就绪后接管 Phase 驱动。

### 4.4 Controller 和连接

HostOnly 状态至少区分：

```text
Human
NativeBot
TakeoverBot
```

以及：

```text
Connected
DisconnectedGrace
Quit
Eliminated
```

规则：

- 原生 AI 和 TakeoverBot 没有 Ready。
- 准备阶段真人掉线时，Ready 在同一个房主事务中清除。
- 掉线只改变 Connection/Ready，不删除席位或玩家状态。
- 重连恢复 Human 控制器时不回滚状态。
- Quit、AI 接管宽限和淘汰的完整时机由 M4/M5 实现；M1 只提供合法状态和房主内部变更入口。
- Host 玩家不能转为 TakeoverBot；房主退出会由未来 LAN/Flow 层结束整局。

### 4.5 公共伪装

Public Snapshot 不能暴露：

- ControllerKind；
- NativeBot / TakeoverBot 标记；
- reconnect token 或资格；
- AI 决策状态。

公共连接表现：

- 真人正常在线：Online；
- 真人仍在掉线宽限：LostConnection；
- NativeBot：Online；
- TakeoverBot：Online；
- 非房主主动退出后由 TakeoverBot 接管：Online；
- Eliminated：Eliminated/Spectating。

M1 只输出公共表现状态，不增加“机器人”字段。

## 5. 权威状态模型

### 5.1 唯一可变所有者

`MatchAuthority` 是 MatchState 的唯一可变所有者：

- 调用方不能取得可变 Seat、Player、Unit、Shop 或集合引用；
- 所有修改经过明确方法或命令；
- 每次成功且实际改变状态的事务只递增一次 StateRevision；
- 成功 no-op 不递增；
- 拒绝不递增；
- 每次 commit 最多发布一次 Changed/Snapshot 通知；
- 订阅回调不能在状态半更新时看到中间结果。

实现可以使用 copy-on-write、先验证后提交或内部 transaction draft，但必须用测试证明失败原子性。不得通过“先改、失败后尽量改回来”制造脆弱回滚。

### 5.2 基础状态

建议：

```text
MatchState
  SessionId
  MatchSeed
  StateRevision
  Phase
  RoundNumber
  HostPlayerId
  CompatibilityManifest
  Seats[4]

MatchSeatState
  SeatIndex
  PlayerId
  DisplayName
  AvatarId
  Life
  Gold
  Level
  TotalDeploymentCost
  AvailableDeploymentCost
  Ready
  Eliminated
  Placement?
  ControllerKind
  ConnectionState
  Units[]
  ShopOffers[]
```

可以调整类名，但字段语义必须完整。

### 5.3 为 M2/M3 预留的数据契约

M1 不实现经济和合成，但不要把未来关键状态塞进无类型字典。

至少定义只读/内部状态的基本形状：

```text
MatchUnitZone
  Deployed
  Staging
  Overflow

MatchUnitState
  UnitId
  TypeId
  Zone
  EliteLevel
  Formation?
  AcquisitionOrdinal
  Buffs[]

MatchShopOfferState
  SlotIndex
  UnitId?
  TypeId?
  IsFrozen

MatchBuffState
  BuffId
  CanonicalPayload
```

约束：

- M1 初始化时 Units 和 ShopOffers 可为空。
- 不生成持久 UnitId。
- 不实现牌库外赠送。
- 不实现合成、Overflow 提升或 Shop draw。
- 不用 `object`、`Dictionary<string, object>` 或 JSON 字符串代替明确类型。
- Formation 使用纯值类型，坐标合法性可先限制为 `1..9 × 1..4` 且排除 `(5,1)`；若引用 Battle Core 会破坏零依赖，可以在 Match 中定义本地纯值类型，后续由 Adapter 转换。

## 6. 命令与幂等

### 6.1 Command Envelope

建议：

```text
MatchCommandEnvelope
  SessionId
  PlayerId
  CommandId
  KnownStateRevision
  Payload
```

要求：

- SessionId、PlayerId、CommandId 非空。
- KnownStateRevision 不得大于房主当前 revision。
- KnownStateRevision 小于当前 revision不应仅因为其他玩家发生了无关事务就一律拒绝。
- 每个命令使用语义前置条件判断是否仍然有效；M2 的购买命令将携带预期 Shop UnitId，部署命令将携带具体 UnitId/坐标。
- Phase 已封存、玩家无命令权、实体前置条件变化等才返回明确 stale/invalid 结果。
- 网络层未来只负责把 Wire Payload 映射成已知领域命令；领域层不解析 JSON。

M1 至少实现一个真实命令：

```text
SetPreparationReady(bool desiredReady)
```

规则：

- 只允许 Preparation；
- 只允许 Connected、未淘汰的 Human；
- NativeBot 和 TakeoverBot 拒绝；
- desired 值已经相同是成功 no-op；
- Sealing/Battle/Settlement/Ended 拒绝；
- 准备阶段掉线由 Host 内部连接事务清 Ready，不伪装成玩家命令。

### 6.2 幂等缓存

幂等键为：

```text
(PlayerId, CommandId)
```

- 第一次处理后保留规范命令摘要和完整 CommandResult，直到 Match Session 结束。
- 同一键、相同命令内容重发：返回完全相同的结果，不重新执行、不递增 revision、不再次通知。
- 同一键、不同命令内容：返回 `CommandIdConflict`，不执行。
- 4 人 LAN 第一版可以在整局内保存全部处理记录，不要因任意 LRU 淘汰导致旧命令可能再次执行。
- 不使用对象引用或默认 `GetHashCode()` 比较命令内容。

### 6.3 结果码

至少区分：

```text
Accepted
AcceptedNoChange
SessionMismatch
UnknownPlayer
CommandIdInvalid
CommandIdConflict
FutureRevision
PhaseRejected
ControllerRejected
ConnectionRejected
Eliminated
InvalidPayload
InvalidTransition
InternalInvariantViolation
```

结果至少包含：

- CommandId；
- Code；
- CurrentStateRevision；
- AcceptedStateRevision；
- 是否改变状态；
- 稳定、非本地化的诊断 detail/code。

玩家文本由 UI 映射，领域层不保存中文提示。

## 7. 权限快照

### 7.1 Public

所有在线客户端可见：

- SessionId；
- StateRevision；
- Phase；
- RoundNumber；
- 四个固定席位及顺序；
- PlayerId、DisplayName、AvatarId；
- Life；
- PublicConnectionState；
- Ready；
- Eliminated/Placement；
- Deployed 单位；
- Staging 单位。

Public 不包含：

- Gold；
- Total/Available Deployment Cost；
- Level 和升级进度；
- Shop；
- Overflow；
- ControllerKind；
- HostOnly 重连/AI/牌库字段。

### 7.2 OwnerPrivate

每个真人只收到自己的：

- Gold；
- Level；
- Total/Available Deployment Cost；
- 自己的 Shop；
- 自己的 Overflow；
- 自己全部持久单位和 Buff；
- 后续 M2 的升级价格/折扣；
- 后续 M6 所需但可以安全下发给本人的重连会话状态。

OwnerPrivate 不能包含其他玩家的私有经济。

### 7.3 HostOnly

房主权威内部可以读取：

- 全部玩家完整状态；
- ControllerKind；
- ConnectionState 精确原因；
- AI 接管/宽限字段；
- 后续共享牌库；
- 后续 token 映射；
- 幂等命令记录。

HostOnly Snapshot 不得被房主 UI 直接使用。房主玩家的 HUD 也必须使用和普通玩家相同的 Owner-scoped Projection；否则房主被淘汰旁观时会看到不该显示的隐藏经济。

### 7.4 投影 API

建议：

```text
ProjectPublic()
ProjectForPlayer(playerId)
ProjectForHostAuthority()
```

所有输出必须：

- 不可变；
- 深复制或安全只读；
- 席位按 SeatIndex；
- 单位按稳定规则；
- 商店按 SlotIndex；
- Buff 按规范序；
- 同一 revision 重复投影产生相同 CanonicalSummary。

客户端观察哪个玩家是本地 UI 状态，不属于 MatchAuthority；不要把 `ObservedPlayerId` 放进权威 revision。

## 8. 兼容清单

定义不可变 `MatchCompatibilityManifest`：

```text
ProtocolVersion
MatchRulesVersion
BattleCoreVersion
UnitCatalogSha256
AbilityCatalogSha256
```

要求：

- 所有字段非空且格式明确；
- 两份清单严格逐字段相等才兼容；
- 单位/能力哈希为固定 64 位 SHA-256 十六进制；
- 提供稳定 CanonicalSummary；
- 不包含头像、美术、语言或表现资源；
- M1 不扫描 Resources，不计算真实目录哈希，只接收并验证上层传入的值；
- M6 加入和重连握手将复用该判定。

## 9. 稳定摘要与不变量

为 MatchState、三层 Snapshot、Command Payload/Result、CompatibilityManifest 提供稳定规范摘要。

要求：

- ordinal 字符串排序；
- invariant 数字；
- 明确枚举整数；
- 无字典插入顺序依赖；
- 字符串使用长度前缀或等价无歧义结构；
- 不使用 `GetHashCode()`；
- 不读取系统区域、时区、时间或随机数；
- 摘要用于测试和未来哈希输入，不要误称为加密签名。

每次 commit 前至少验证：

- 恰好四席；
- SeatIndex `1..4`；
- 唯一 PlayerId；
- Host 在四席且为 Human；
- 数值非负约束；
- AvailableCost 不大于 TotalCost；
- Eliminated 玩家不能 Ready；
- 非 Human 不能 Ready；
- Deployed 坐标不重复；
- UnitId 全 Match 唯一；
- Shop SlotIndex 在单玩家内唯一；
- Shop 具体 UnitId 不与持有单位重复。

M1 初始化 Units/Shop 为空，但不变量必须允许 M2 扩展。

## 10. Host 内部事务入口

M1 至少提供可测试的 Host 内部入口：

- `TryEnterPreparation(roundNumber, ...)`
- `TrySetConnectionState(playerId, ...)`
- `TrySetControllerKind(playerId, ...)`
- `TryMarkEliminated(playerId, placement?)`
- `TryEndMatch(reason)`

这些不是玩家命令，不能伪造 PlayerId 绕过权限。

规则：

- 进入新 Preparation 时，所有未淘汰 Human 的 Ready 重置为 false。
- Preparation 中把 Human 设为 DisconnectedGrace 时同事务清 Ready。
- 连接状态改变不删除席位。
- 把 NativeBot/TakeoverBot 标成 Ready 属于不变量错误。
- Ended 后只有只读投影允许，任何修改拒绝。
- M1 不计算掉线宽限回合，不自动启动 AI，不结算生命或排名。

## 11. 明确不在 M1 范围

不得实现或修改：

- UDP/TCP、Lobby Protocol、LanRoomHost、LanRoomClient；
- reconnect token 持久化；
- 共享牌库和 Pool UnitId 生成；
- 商店抽取、刷新、冻结、购买；
- 升级费用、基础收入、连续奖励；
- 合成、Overflow 事务；
- 部署/撤退/换位业务；
- 30 秒计时和提前封存；
- 配对、主客场、影子战斗；
- BattleInput、Battle Core、战斗分块和哈希；
- AI 决策；
- HUD、场景、Prefab、MonoBehaviour；
- `LocalMatchState`、`PlayerState` 或现有测试 fixture 的联网改造；
- 牌库外赠送；
- SPEC/ARCHITECTURE/TEST_PLAN 的共享段落改写。

如果一个字段只有 M2—M8 才能确定，定义最小强类型扩展位置并记录，不要用临时行为填空。

## 12. TDD 与测试要求

### 12.1 初始化

- 1 Human + 3 NativeBot 合法；
- 4 Human 合法；
- 非四席、重复 SeatIndex、越界 SeatIndex、重复 PlayerId、Host 缺失、多个 Host、Host 是 Bot 拒绝；
- 空 SessionId、非法 manifest、非法初始数值拒绝；
- 席位输入顺序打乱后，权威快照仍按 SeatIndex 且规范摘要一致。

### 12.2 Revision 和原子性

- 初始 revision 明确固定；
- 成功状态变化只 `+1`；
- 成功 no-op 不增加；
- 拒绝不增加；
- Changed 每个 commit 恰好一次；
- 订阅者只看到完整提交状态；
- 非法内部转换不留下半更新状态。

### 12.3 命令和幂等

- Connected Human 在 Preparation 可以 Ready/取消；
- Bot、TakeoverBot、Disconnected、Eliminated、错误 Phase 拒绝；
- 同 CommandId 同内容重发返回原结果且无副作用；
- 同 CommandId 不同内容返回 conflict；
- FutureRevision 拒绝；
- KnownRevision 较旧但语义仍有效时不因其他席位无关改变而机械拒绝；
- 命令摘要不受实例、字典顺序和区域设置影响。

### 12.4 连接与 Ready

- Preparation 中 Human 掉线，同事务变 LostConnection 并清 Ready；
- 重连不自动恢复 Ready；
- NativeBot/TakeoverBot 公共表现为 Online；
- Host 不能切换为 TakeoverBot；
- 连接变化不改变席位顺序、经济或单位状态。

### 12.5 权限

- Public 不含 Gold/Cost/Level/Shop/Overflow/ControllerKind；
- Player A 的 scoped snapshot 只含 A 的 OwnerPrivate；
- Player A 不能读取 B/C/D 私有状态；
- HostAuthority snapshot 有完整状态；
- 房主玩家 UI projection 与普通玩家遵守同一隐私；
- 淘汰旁观者不能得到其他玩家私有经济；
- 返回集合无法修改 Authority。

### 12.6 确定性

- 相同初始化和命令序列重复多次得到相同摘要；
- 不同输入顺序但相同席位事实得到相同摘要；
- 不同区域设置下相同；
- Snapshot 重复投影相同；
- 失败路径不改变摘要。

## 13. 验证命令

使用仓库脚本和该 worktree 绝对路径：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath '<独立-worktree-绝对路径>' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Match.Tests' `
  -OutputDirectory 'Artifacts\LanMatchDomain\Focused-EditMode' `
  -TimeoutSeconds 900 `
  -NoGraphics
```

随后串行执行：

1. 新 Match focused EditMode；
2. `ArknoNights.Battle.Tests.LocalMatchStateEditModeTests`；
3. `ArknoNights.Battle.Tests.PreparationBattlePhaseEditModeTests`；
4. Lobby 领域 EditMode；
5. 全量 EditMode；
6. 全量 PlayMode。

规则：

- 每个 XML 测试数必须大于 `0`；
- failed/skipped/inconclusive/not-run/not-runnable 必须全为 `0`；
- 工具失败、许可证、项目占用、超时、无 XML 都记为未验证；
- 扫描编译错误和未处理异常；
- Unity 测试不得并行；
- 不需要 Windows/Android 构建，因为 M1 没有平台或场景接入。

最终执行：

```powershell
git diff --check
git status --short
```

确认没有：

- Scene/Prefab/Package/ProjectSettings 变化；
- `LocalMatchState.cs` 或 Lobby Socket 修改；
- 生成目录和测试产物进入 Git；
- 无关格式化；
- 缺失 `.meta`。

## 14. 完成与交付

提交一个或少量可审查 commit。最终报告必须包含：

1. worktree 路径；
2. 分支名；
3. commit SHA；
4. 新增类型与公开 API；
5. Revision、幂等和权限模型说明；
6. 修改文件清单；
7. 每条测试命令、退出码、测试数量、XML/日志路径；
8. 未验证项；
9. M2 接入共享牌库时需要使用的扩展点；
10. 明确确认未实现 M2—M8；
11. 建议由主 Planner 更新的架构/测试文档增量。

不得仅凭代码审查声称测试通过，也不得把“领域骨架完成”描述为“局域网对局已经可玩”。
