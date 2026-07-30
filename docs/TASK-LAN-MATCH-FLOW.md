# M4：配对、阶段、结算与淘汰 Agent 提示词

> 状态：已完成。来源提交 `5238a15` 已作为 `63ffe1f` 进入统一 M1—M8 集成链；本文件保留为 M4 交付契约。
> 本文件可直接作为实现 Agent 的任务提示词。本轮负责把 M1—M3 的领域能力编排成可运行的权威对局流程，但不接 Socket、AI 决策或 Battle Core 内部计算。

## 1. 任务角色与目标

你负责实现完整 LAN 对局的房主权威流程层：

1. 准备阶段 30 秒硬上限；
2. Ready/取消 Ready 与全体真人准备后的立即封存；
3. 权威部署、撤退、换位和替换命令；
4. 封存安全购买、自动部署、Overflow 删除；
5. 四人双循环、三人官方加影子、两人主客交替；
6. 淘汰后人数轮转切换；
7. 不可变 SealedBattleInput 计划；
8. Battle Core 结果的权威验证和映射；
9. 玩家生命伤害、胜负、连续状态和回合资源；
10. 淘汰、共享名次、胜利和旁观权限；
11. 战后自然刷新、升级减费和下一准备阶段；
12. 竞技完成或房主终止时的 Ended 状态与“直接回主界面”外部效果。

M4 不计算战斗逐 Tick 内容。Battle 层只向 M4 返回每场战斗双方承受的非负生命伤害、由该伤害导出的胜负和平局，以及输入身份；M4 决定哪些结果真正写入哪些玩家。

## 2. 分支、基线与工作方式

- 使用独立 worktree。
- 分支名使用 `codex/lan-match-flow`。
- 必须基于主 Planner 指定的 M3 commit 创建，记录 M1、M2、M3 实际基线 SHA。
- 不在主工作区直接实现。
- 不创建子 Agent，除非主 Planner明确授权。
- 不合并回主分支；完成后将 commit SHA 和验证证据交给主 Planner。

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
   - `docs/TASK-BATTLE-STREAMING.md`
   - `docs/numerical_architecture/2026-07-28_match_economy_upgrade_and_streak_design.md`
3. 阅读 M1—M3 实际实现、公开 API、asmdef 和全部 Match EditMode 测试。
4. 检查现有本地准备—战斗循环、玩家部署命令、BattleInputFactory、BattleRunResult 和测试，仅作适配参考。
5. 执行 `git status --short` 并保护已有修改。
6. 确认没有 Unity 进程占用该 worktree。
7. 运行 M1—M3 Match EditMode 测试作为基线。

本文的完整联网规则优先于本地 Demo 的固定 `1—2/3—4` 配对、99 Cost、最高费用自动部署、整场预计算后才播放和本地倒计时切换。

## 3. 架构边界

继续保持 `ARKnoNIGHTS.Match` 为纯 C# 权威领域：

- 优先 `noEngineReferences: true`。
- 不读取 `Time.time`、`DateTime.Now`、Resources、场景或 UI。
- 房主运行层传入单调时钟值和 Battle 结果。
- 所有流程改变仍通过 `MatchAuthority` transaction draft 原子提交。
- 不复制 M2 的经济、M3 的合成/Overflow 或 Battle Core 规则。
- 不从表现播放状态反向计算胜负。
- 不定义 TCP/UDP Wire DTO；M6 显式映射领域契约。
- 不在 M4 内实现 AI 购买逻辑；只提供准备入口和命令执行边界供 M5 调用。

建议增加：

```text
Assets/Game/Runtime/Match/
  Flow/
  Formation/
  Pairing/
  Sealing/
  Settlement/
  Ranking/
```

## 4. 权威阶段状态机

使用 M1 已定义的阶段：

```text
Initializing
Preparation
Sealing
Battle
Settlement
Ended
```

合法主路径：

```text
Initializing
→ Preparation(round 1)
→ Sealing
→ Battle
→ Settlement
→ Preparation(round + 1)
...
→ Settlement
→ Ended
```

另外允许房主/session 终止从任意非 Ended 阶段直接进入 Ended，但必须带 `NoContest`/`HostTerminated` 原因且不执行战斗结算。

要求：

- 玩家命令不能直接设置 Phase 或 RoundNumber。
- Sealing、Settlement 是不可被玩家命令插入的房主事务边界。
- Ended 后所有修改拒绝，只允许只读投影。
- 每次成功阶段事务最多递增一次 StateRevision、发布一次快照。
- 若 M1 已把 Ready 命令作为独立事务，实现“最后一名真人 Ready 被接受后立即封存”的组合入口，避免产生可取消的中间 Preparation revision。
- 允许 CommandAck 表示该 Ready 命令同时触发了 Battle phase；重发同 CommandId 返回完全相同结果。

## 5. 准备阶段时钟

准备阶段硬上限固定为 `30,000` 毫秒。

领域层不读取墙上时间。房主运行层传入：

```text
EnterPreparation(roundNumber, hostMonotonicNowMs)
AdvancePreparationClock(hostMonotonicNowMs)
```

或等价强类型接口。

规则：

- 每个准备阶段保存不可变 `StartedAtHostMonotonicMs` 和派生 Deadline。
- 时钟输入必须单调；倒退值拒绝且不改变状态。
- 网络延迟、客户端暂停、掉线或重连不暂停/延长 Deadline。
- `now >= deadline` 时触发封存。
- Ready 命令和超时事件均由同一个房主串行队列排序：
  - 若最后 Ready 先被接受，按提前封存；
  - 若超时事件先被接受，按超时封存；
  - 之后到达的命令按 PhaseRejected 处理。
- 客户端显示的剩余时间只是房主时钟投影，不能驱动权威转换。
- 测试使用虚拟单调时钟，不进行真实 sleep。

## 6. Ready 与准备入口

### 6.1 准备入口

进入每个新 Preparation 时：

- RoundNumber 精确加一，初始为 1。
- 所有未淘汰 Human 的 Ready 重置为 false。
- NativeBot 和 TakeoverBot 没有 Ready。
- 重置 M2 的本回合人类行为事实：
  - SuccessfulShopPurchaseCount = 0
  - HasIssuedEffectiveFreezeThisRound = false
- 启动新的 30 秒 Deadline。
- 发布一次明确的 PreparationOpened 领域事件/效果，供 M5 执行准备入口初始 AI 决策周期。

M5 接入后，准备入口初始 AI 周期必须在该阶段开始接受普通玩家命令前完成，或作为同一房主串行入口的首批内部命令完成。不要在 M4 中写假 AI；可以提供可注入、默认 no-op 的 preparation-entry participant，并通过测试锁定排序。

### 6.2 提前封存资格

`AllRequiredHumansReady` 只检查：

- 未淘汰；
- ControllerKind=Human；
- ConnectionState=Connected；
- Ready=true。

另外：

- DisconnectedGrace 的 Human 按未准备，阻止提前封存。
- DisconnectedGrace 不能阻止 30 秒超时封存。
- NativeBot/TakeoverBot 不进入 Ready 集合。
- Quit 后已转 TakeoverBot 的席位不进入。
- Eliminated 不进入。
- 至少存在一个仍受 Human 控制且 Connected 的未淘汰席位时，全部满足才提前封存。
- 若当前没有需要 Ready 的真人（例如全部剩余席位由 AI 控制），由房主流程在准备入口初始 AI 周期后立即封存，不等待 30 秒。

### 6.3 SetReady

沿用 M1 命令并补全：

- 只允许 Preparation、Connected、未淘汰 Human。
- `Ready=true` 锁定该玩家阵型操作，但商店购买、刷新、冻结、升级仍允许到真正进入 Sealing 为止。
- 尚未全体准备时可以 `Ready=false`。
- 最后一名所需真人的 `Ready=true` 一旦接受，在同一个权威串行处理内立即进入封存；不能留下可处理取消 Ready 的间隙。
- 掉线事件在 Preparation 内原子清除 Ready。
- 重连不恢复旧 Ready。
- AI 不发送 SetReady。

## 7. 权威阵型与部署命令

M1—M3 没有完整实现的部署业务由 M4 补齐，作为 Match 权威命令核心，供真人和 M5 AI 共同复用。

### 7.1 坐标

本地阵型为 `9 × 4`：

- X 只允许 `1..9`；
- Y 只允许 `1..4`；
- `(5,1)` 为门格，不可部署；
- 同一玩家同一格最多一个 Deployed 可见单位。

本任务不新增未确认的职业、地形或单位专属部署限制。若现有正式领域契约已存在额外、已由 SPEC 确认的限制，则通过注入式只读 validator 使用；不能把本地 UI 预览当权威。

### 7.2 Deploy

命令至少携带：

```text
DeployUnit
  UnitId
  TargetFormation
  ExpectedAvailableCost
```

规则：

- 普通玩家只允许 Preparation 且 Ready=false。
- 单位必须属于该玩家且在 Staging。
- 目标必须合法且为空。
- 使用当前 EliteLevel 的权威部署费用。
- AvailableCost 足够。
- 成功后移动到 Deployed、写 Formation、扣除完整费用。
- 失败原子不变。

### 7.3 Replace

当目标已有本玩家单位时使用显式原子命令：

```text
ReplaceDeployedUnit
  StagingUnitId
  ExpectedDeployedUnitId
  TargetFormation
```

验证“旧单位返回 Staging 后”的最终严格堆叠槽数不超过 13，并按：

1. 返还旧单位完整部署费用；
2. 旧单位进入 Staging；
3. 新单位离开 Staging；
4. 新单位进入目标格；
5. 扣除新单位费用；

在一个事务中提交。最终 Cost 或容量不足时全部拒绝。

### 7.4 Relocate/Swap

Preparation 且 Ready=false：

- 移到空合法格：保留 UnitId/Elite/Cost，只改 Formation。
- 移到另一已部署单位格：两者原子交换 Formation。
- 拖回原格为成功 no-op，不递增 revision。
- 非法格、门格、未知 ID 原子拒绝。
- 不扣、返或重新判断 Cost。

### 7.5 Retreat

Preparation 且 Ready=false：

- 检查单位进入 Staging 后的严格堆叠槽数不超过 13。
- 成功后清 Formation、进入 Staging、返还完整当前部署费用。
- 不二次确认。
- 容量不足原子拒绝。

### 7.6 控制器调用

- 真人命令使用自己的 PlayerId。
- M5 AI 必须调用同一语义核心，但以 Host 授权的 Bot operation origin 进入，不能伪造网络玩家。
- Ready=true 的真人阵型锁定。
- Sealing/Battle/Settlement/Ended 的普通部署类命令拒绝。
- 第 9 节的封存安全部署使用 Host internal origin 复用同一 draft 核心，允许在 Sealing 中执行，但不能绕过坐标、Cost、所有权和占位验证。

## 8. 封存触发与原子边界

封存由以下任一原因触发：

```text
AllRequiredHumansReady
PreparationDeadlineReached
NoRequiredHumansAfterBotEntryCycle
```

封存开始后：

- Phase 立即成为 transaction draft 中的 Sealing。
- 所有尚未处理的玩家命令在提交后按新 Phase 拒绝。
- 不允许新购买、刷新、升级或阵型命令插入封存中间。
- Sealing 的全部安全动作、Overflow 删除、配对和 BattleInput 构建要么完整成功并进入 Battle，要么 fail-closed；不能发布半封存快照。
- 若不可恢复的目录/输入不变量导致封存失败，使用结构化 FatalMatchError 结束 session，不用假单位、空战斗或吞错继续。

## 9. 人类封存安全规则

按 SeatIndex 升序处理未淘汰、ControllerKind=Human 的席位；DisconnectedGrace Human 仍适用这些统一安全规则。NativeBot/TakeoverBot 不执行本节的购买或全扫描自动部署。

### 9.1 第一回合安全购买

仅 Round 1，且该真人本回合：

- `SuccessfulShopPurchaseCount == 0`
- `HasIssuedEffectiveFreezeThisRound == false`

则：

1. 找到当前商店 SlotIndex 最大的非空商品；没有则跳过。
2. 复用 M2/M3 正常购买核心。
3. 正常验证 Staging 购买前 13 槽、Gold、具体 UnitId 和所有权。
4. 正常扣除商品价格并永久占用具体 Pool UnitId。
5. 在同一购买草稿中完成自动合成和 Overflow 提升。
6. 不扣除额外费用，也不免除商品价格。
7. 购买失败时保持原状态并继续封存，不向左尝试其他商品。
8. 购买成功后，从 Purchase 结果取得 `FinalSurvivorUnitId`：
   - 若它已是 Deployed，保持原位；
   - 若它在 Staging，尝试用 Host internal Deploy 核心部署到 `(5,2)`；
   - 若它在 Overflow/已退休或目标/Cost 不合法，部署失败并保持相应状态；
   - 不尝试另一个由本次购买产生的单位。

封存系统动作本身不伪造玩家 CommandId，但要有稳定 `SystemActionId(SessionId, Round, Seat, ActionKind)`，以便封存事务重试时幂等。

### 9.2 Overflow 删除

完成全部第一回合安全购买及其 M3 获得事务后：

- 调用 M3 的 `DiscardRemainingOverflowAtSeal`。
- 四席位全部剩余 Overflow 永久退休，不返池。
- 删除顺序稳定。
- 不能留下 TargetUnitId 悬空的定向 Buff。

### 9.3 任意回合空阵安全部署

Overflow 删除后，对每个未淘汰 Human：

- 若 Deployed 非空，不做任何事。
- 若 Deployed 为空且 Staging 非空：
  1. 使用 M3 权威严格堆叠排序；
  2. 取最右侧槽；
  3. 取该槽内 UnitId 最小的单位；
  4. 只尝试该单位部署到 `(5,2)`。
- Cost 不足、位置不可用或其他部署失败时保持空阵。
- 不向左尝试、不换另一个 UnitId、不购买补位。

严格槽从左到右按：

```text
DeploymentCost
TypeId numeric
EliteLevel
Buff CanonicalSummary ordinal
minimum UnitId ordinal
```

依次升序。

## 10. BattleInput 封存计划

M4 定义纯领域 `SealedRoundPlan`，不依赖当前 Battle Core DTO：

```text
SealedRoundPlan
  SessionId
  RoundNumber
  PairingGeneration
  Pairings[]
  PlayerCombatSnapshots[]
  MatchRulesVersion
  UnitCatalogSha256
  AbilityCatalogSha256
  CanonicalInputHash

SealedPairing
  BattleId
  BattleIndex
  Kind = Official | Shadow
  HomePlayerId
  AwayPlayerId
  ShadowOwnerPlayerId?
  SettlementRecipients
  BattleSeed
```

规则：

- 只封存未淘汰玩家的 Deployed 持久单位和战斗所需 Buff。
- Staging、Overflow、Shop、Gold、升级、连接和控制器状态不进入 BattleInput。
- 同一玩家同一轮可被多个 Pairing 引用，但使用同一份不可变玩家战斗快照；Home/Away adapter 只负责阵型方向映射。
- BattleId 和 BattleSeed 由 SessionId、RoundNumber、BattleIndex、PairingGeneration 和明确版本化规范稳定派生。
- 所有在线客户端收到并计算本轮全部 Official/Shadow Pairing。
- Battle 阶段后发生的商店购买/升级不能改变已封存计划或 hash。
- 跨玩家 UnitId 冲突、未知 TypeId/Ability、非法 Formation 或无法解析战斗数据时封存失败。

M4 提供 adapter interface；M7 将领域快照转换成真实 BattleInput 并流式计算。不要复制 Battle Core 类型进 Match 核心。

## 11. 四名存活玩家配对

四人完整轮转使用固定 SeatIndex `1..4`，六回合表：

| 偏移 | 对局 1 | 对局 2 |
|---:|---|---|
| 1 | 1H—2A | 3H—4A |
| 2 | 1H—3A | 2H—4A |
| 3 | 4H—1A | 3H—2A |
| 4 | 2H—1A | 4H—3A |
| 5 | 1H—4A | 2H—3A |
| 6 | 3H—1A | 4H—2A |

之后重复。

必须证明：

- 每对玩家六回合内交手两次，双方各主场一次。
- 无连续两回合同一对手。
- 任一玩家不连续超过两场相同 Home/Away 角色。
- 每轮恰好两场 Official，无 Shadow。
- 同轮每名存活玩家恰好参加一场。

只要一直保持四人存活，RoundNumber 直接映射六回合偏移。若从较少人数恢复到四人不可能发生；淘汰不可逆。

## 12. 三名存活玩家配对

每回合恰好两场：一场 Official、一场 Shadow。三名存活玩家中恰好一人的同一份阵型参加两场，该玩家是 ShadowOwner；它只接受 Official 结果。

给定稳定映射 `A/B/C`，六回合模板为：

| 偏移 | Official | Shadow | ShadowOwner |
|---:|---|---|---|
| 1 | A H—B A | C H—A A | A |
| 2 | B H—C A | A H—B A | B |
| 3 | C H—A A | B H—C A | C |
| 4 | B H—A A | A H—C A | A |
| 5 | C H—B A | B H—A A | B |
| 6 | A H—C A | C H—B A | C |

要求：

- 偏移 4—6 是前 3 回合每场主客反转。
- ShadowOwner 在同轮的两场中使用同一份封存玩家快照。
- 每名玩家每三回合恰好一次作为 ShadowOwner、一次只接受 Official、一次只接受 Shadow 结果。
- 任一玩家不得连续两轮成为“只接受 Shadow 结果”的玩家。
- 每一轮每名玩家恰好有一个 SettlementRecipient 结果。
- ShadowOwner 在 Shadow Pairing 的 SettlementRecipients 中被排除。
- Shadow 的另一侧玩家正常接受该场伤害、胜负和奖励。

## 13. 两名存活玩家配对

- 每回合一场 Official。
- 两名玩家每回合对战。
- Home/Away 严格交替。
- 无 Shadow。
- 两名玩家均接受该场结果。

三人降到两人时连续相同真实对手无法避免，已确认接受；但仍选择下一 Home/Away 方向以减少连续相同角色和累计角色失衡。

## 14. 人数轮转切换与确定性决胜

玩家在某轮 Settlement 被淘汰后，从下一轮立即切换到新人数模板，不等旧周期完成。

实现一个纯确定性候选枚举器：

- 四人表固定。
- 进入三人时枚举：
  - 存活 SeatIndex 的全部 A/B/C 映射；
  - 六个模板偏移；
  - 不重复产生等价候选。
- 进入两人时枚举下一场两个 Home/Away 方向。

按以下字典序评分选择，全部使用截至上一轮的实际 pairing 历史：

1. 最小化与上一轮重复的真实玩家对数量；Official 和 Shadow 中实际出现的玩家对都计入。
2. 硬性排除任何可避免的“同一玩家连续只接受 Shadow 结果”。
3. 最小化各玩家连续相同 Home/Away 角色的最大长度；不得选择会导致可避免的连续三场同角色候选。
4. 最小化每名玩家累计 `abs(HomeCount - AwayCount)` 的最大值，再最小化总值。
5. 三人时最小化各玩家 ShadowOwner 次数和“只接受 Shadow”次数的不平衡。
6. 最后按模板偏移、A/B/C 对应 SeatIndex 序列、BattleIndex 的规范升序决胜。

如果约束数学上不可同时满足：

- 只放宽前述明确写成“可避免”的项；
- 返回 PairingDiagnostics，指出哪个约束不可满足；
- 不使用随机数或容器顺序。

模板选定后保存 `PairingGeneration`、当前偏移和映射，后续在人数不变时顺序推进，不每轮重新优化导致摇摆。

## 15. Battle 结果契约

M4 接收每个 SealedPairing 的不可变结果：

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

规则：

- LifeDamage 为非负受检整数。
- Outcome 必须严格由伤害比较得到：
  - HomeLifeDamage < AwayLifeDamage：HomeWin；
  - HomeLifeDamage > AwayLifeDamage：AwayWin；
  - 相等：Draw。
- M4 重新计算并拒绝不一致 Outcome。
- 正常结束、双方同时全灭、超时都使用同一比较。
- 超时存活实体 lifeDeduct 的汇总由 Battle Core/M7 完成；M4 不重复枚举实体。
- 同一 BattleId 只能提交一个结果；相同内容重发幂等，不同内容冲突。
- 必须恰好收到本轮全部 Pairing 结果后才能结算；缺失或额外 BattleId 拒绝。
- SealedInputHash 必须匹配。
- M7 的末秒 SHA 校验不一致处理仍暂缓；M4 只消费房主选定的权威结果。

计算可以提前完成，但 M4 只有在房主统一播放 Tick 到达本轮全局终点并完成当前可见终局表现后，才提交 Settlement。异常落后的单个客户端不阻塞该信号；该播放策略由 M7/M6 运行层实现。

## 16. 每名玩家的唯一应用结果

构造：

```text
AppliedPlayerBattleResult
  PlayerId
  SourceBattleId
  Role
  LifeDamage
  Win | Loss | Draw
  IsFromShadow
```

四人：

- 每人接受自己 Official 战斗的一侧结果。

三人：

- ShadowOwner 只接受 Official。
- Official 的另一名玩家接受 Official。
- Shadow 的非 owner 玩家接受 Shadow。
- ShadowOwner 完全忽略 Shadow 对自己的 LifeDamage、Win/Loss/Draw、连续状态和奖励。

两人：

- 双方接受唯一 Official。

每轮结算前验证每名当轮存活玩家恰好有一个 AppliedResult，不能为 0 或 2。只有 AppliedResult 进入生命、连续状态和平局奖励。

## 17. 生命伤害与胜负

在单一 Settlement 草稿中：

1. 基于结算开始时的存活集合生成全部 AppliedResult。
2. 对每名玩家从旧 Life 同时扣除其唯一 LifeDamage。
3. 不在 0 截断；Life 允许为负。
4. 只有 AppliedResult 的 Win/Loss/Draw 更新该玩家连续状态。
5. 不能因为某玩家先降到 0 就跳过同轮其他玩家结果。

M4 不按“剩余战斗单位数量”或“谁先全灭”判胜。所有胜负只比较本场双方承受的玩家生命伤害。

## 18. 连续状态

每名玩家保存：

```text
StreakKind = None | Win | Loss
StreakCount
```

更新：

- Win：原来是 Win 则 +1，否则设 Win/1。
- Loss：原来是 Loss 则 +1，否则设 Loss/1。
- Draw：清为 None/0。

按包含本场结果的新计数发奖励：

| 连续场数 | Win 奖励 | Loss 奖励 |
|---:|---|---|
| 1 | 0 | 0 |
| 2—3 | +2 Gold | +1 Gold、+4 Cost |
| 4—5 | +4 Gold | +2 Gold、+8 Cost |
| 6+ | +6 Gold | +3 Gold、+12 Cost |

要求：

- 没有固定单场胜/负奖励。
- Loss 不扣 Gold/Cost。
- Draw 不发连续奖励。
- ShadowOwner 不因 Shadow 结果更新。
- Cost 奖励同时增加 TotalDeploymentCost 和 AvailableDeploymentCost；已部署占用不变。

## 19. 基础收入与平局彩蛋

每个完整战斗回合结算时，对扣血后仍未淘汰的玩家发放基础收入：

| 已结束回合 | 基础 Gold | 基础 Cost |
|---:|---:|---:|
| 1—4 | 9 | 20 |
| 5—8 | 11 | 24 |
| 9—12 | 13 | 28 |
| 13—16 | 15 | 32 |
| 17—20 | 17 | 36 |
| 21+ | 19 | 40 |

规则：

- 新近 Life<=0 的玩家本轮不获得基础收入、连续奖励或平局额外资源。
- 之前已淘汰玩家不结算。
- 没有利息。
- 没有高精英 Staging 单位额外 Gold。
- 基础/奖励 Cost 永久增加 Total 和 Available 相同数值。

平局彩蛋：

- Round 1—8：无额外。
- Round 9+：AppliedResult=Draw 且结算后未淘汰的玩家，额外获得当轮基础 Gold/Cost 的一半，分别向下取整：

| 回合 | 额外 Gold | 额外 Cost |
|---:|---:|---:|
| 9—12 | 6 | 14 |
| 13—16 | 7 | 16 |
| 17—20 | 8 | 18 |
| 21+ | 9 | 20 |

只按基础值，不叠加连续奖励、刷新或升级减费。三人 ShadowOwner 只看 Official AppliedResult；Shadow 非 owner 正常按 Shadow Draw 获取。

## 20. 淘汰与排名

在同一轮全部 LifeDamage 算完后：

- `Life <= 0` 且此前未淘汰的玩家在本轮淘汰。
- 玩家 Life 保留实际负数。
- 新淘汰者按结算后 Life 从高到低排名。
- 排名公式：

```text
Placement =
  SurvivorsAfterRound
  + 1
  + count(NewlyEliminated whose Life is strictly greater)
```

所以：

- 两名新淘汰者 Life 相同则共享名次。
- 更低 Life 的下一名按前面实际人数跳号。
- 若全部玩家同轮淘汰，最高 Life 者为第 1；完全相同可共享第 1。
- 若只剩一名存活者，它为第 1。
- 先前淘汰者的 Placement 永不重算。

淘汰状态：

- 淘汰真人进入 Spectating，后续不能发送任何玩家命令。
- 重连后仍是 Spectating。
- 淘汰 AI 停止，由 M5 观察 Eliminated。
- 淘汰房主玩家仍可作为服务器/旁观者继续。
- 淘汰玩家持有单位、商店商品和已消费卡不返池。
- 旁观者只接收 Public，不接收任何存活玩家 OwnerPrivate。

## 21. 结算原子顺序

本轮全部 BattleResolution 到齐且播放完成后，在一个 Host settlement transaction 中：

1. 验证 SealedRoundPlan、全部结果、hash 和重复提交。
2. 生成每名存活玩家唯一 AppliedResult。
3. 同时扣除 Life。
4. 计算 Win/Loss/Draw 连续状态。
5. 标记新淘汰并分配 Placement。
6. 只向结算后未淘汰玩家发基础收入、连续奖励和平局额外资源。
7. 调用 M2 的战后自然公平刷新和当前等级升级费用 `-1`。
8. 记录本轮 pairing/result/settlement 规范摘要。
9. 若竞技已结束，分配最终第 1 并进入 Ended。
10. 否则选择/推进下一人数 PairingGeneration，并进入下一 Preparation。

整批：

- 只递增一次 revision；
- 只发布一次权限裁剪快照；
- 不允许客户端在步骤间发命令；
- 任一步失败完整回滚；
- 同一 Round settlement 重发幂等，不重复扣血、发钱、刷新或排名。

即使本轮产生最终唯一胜者，仍把这个完整战斗回合对最终存活者的收入、连续奖励、自然刷新和升级减费作为权威结算记录后再 Ended；session 随即销毁，UI 不制作结算界面。

## 22. 对局结束

### 22.1 竞技完成

Settlement 后存活玩家数 `<=1`：

- 所有 Placement 确定。
- MatchEndReason=`CompetitiveCompleted`。
- 保存只读 FinalStandings。
- 进入 Ended。
- 发布外部 `ReturnAllToMainMenu` 效果。
- M6 收到后销毁 room/session、使房间号失效、清除 reconnect token。
- 不创建结算界面。
- 不自动创建重赛房间。

进入 Settlement 的时机已在第 15 节要求等待房主播放完成，因此最终正常战斗完整播放后才返回主界面。

### 22.2 房主/session 终止

提供 Host 内部：

```text
AbortMatchNoContest(reason)
```

用于房主主动退出、房主进程关闭/连接权威丢失等：

- 任意非 Ended 阶段立即进入 Ended。
- 不应用尚未结算的 Battle 结果。
- 不发收入、不扣生命、不定胜负。
- FinalStandings 标记 NoContest，而不是伪造赢家。
- 所有客户端直接回主界面。
- 不把房主席位转 AI。

M4 不侦测进程崩溃；M6 调用该入口。

### 22.3 非房主退出

M4 只提供控制器/连接状态和阶段钩子：

- Preparation 退出后 M5/M6 可立即设 TakeoverBot。
- Battle 退出后在下一 Preparation 接管。
- 不结束整局。
- 具体 token 和宽限轮次由 M6/M5 实现。

## 23. 快照与外部效果

Public 增加：

- RoundNumber
- Phase
- 权威准备剩余毫秒
- 当前四席位 Ready/PublicConnection
- 当前 Sealed Pairing 的公开 Home/Away/Official/Shadow 展示所需信息
- Life、Eliminated、Placement
- FinalStandings/EndReason 的公开安全部分

不得公开：

- 对手 Gold/Cost/Level/Shop
- Host pairing 评分内部历史以外的私有状态
- Pool、随机状态、AI 控制器
- reconnect token

OwnerPrivate 延续本人经济和单位。

HostOnly 增加：

- 完整 pairing scheduler 状态和历史
- Deadline 单调时钟数据
- SealedRoundPlan
- 已接收 BattleResolution
- settlement 幂等记录
- Abort reason 诊断

外部效果与领域状态分离，例如：

```text
PreparationOpened
RoundSealed
BattlePlanReady
RoundSettlementCommitted
MatchEnded
ReturnAllToMainMenu
```

效果投递失败不能导致领域事务重放。M6 后续用 StateRevision/快照恢复。

## 24. 明确不在 M4 范围

不得实现：

- AI 决策、固定 AI Tick 或购买策略；
- 掉线 token、重连握手、自动重连持久化；
- TCP/UDP、协议序列化、快照传输；
- Battle Core 的逐 Tick 计算；
- 5 秒战斗分块、播放缓冲和末秒 SHA；
- 哈希不一致纠错；
- 牌库外赠送 UnitId；
- Buff 数值效果或羁绊；
- 战斗 HP/死亡写回持久单位；
- 结算界面、重赛房间；
- 主机迁移；
- 反房主作弊；
- 场景、Prefab 或本地 Demo 的大范围改造；
- Unity/Package 升级。

## 25. TDD 与测试要求

先写失败测试，再实现。至少覆盖：

### 25.1 阶段与时钟

- 初始化进入 Round 1 Preparation，Deadline=+30000ms。
- 29999ms 不封存，30000ms 封存。
- 时钟倒退拒绝。
- 掉线/重连不改变 Deadline。
- 超时前最后 Ready 先处理则 EarlyReady；超时先处理则 Timeout。
- Sealing 中玩家命令全部拒绝。
- Ended 后全部修改拒绝。
- 同一超时事件重放不二次封存。

### 25.2 Ready

- 全部 Connected Human Ready 后立即封存。
- NativeBot/TakeoverBot 不进入 Ready 集。
- DisconnectedGrace Human 阻止提前封存但不阻止超时。
- AI-only 存活集合在准备入口 AI 初始周期后立即封存。
- 最后 Ready 和封存为同一串行结果，不能取消。
- 尚未全员 Ready 时可以取消。
- Preparation 掉线清 Ready；重连不恢复。
- Ready 锁阵型但不锁商店，Sealing 才锁商店。

### 25.3 部署业务

- 坐标边界和 `(5,1)` 门格。
- Deploy 扣当前 Elite Cost。
- Cost 不足、目标占用、错误 Zone 原子失败。
- Replace 正确返旧扣新并检查最终 Staging 槽。
- Relocate 到空格、Swap、同格 no-op。
- Retreat 返完整 Cost；Staging 超 13 拒绝。
- Ready=true 和 Battle 拒绝普通阵型命令。
- AI/internal 与真人复用同一验证核心。
- 所有成功/失败 revision 语义符合 M1。

### 25.4 封存安全规则

- R1 真人零购买零有效冻结：购买最大 SlotIndex 非空商品，正常扣金。
- Gold 不足、Staging 满、商店空只跳过，不向左重试。
- 曾购买或曾有效冻结则不自动购买。
- 冻结后解除仍视为曾冻结。
- 全空商店冻结 no-op 不算曾冻结。
- 安全购买触发 M3 合成，并用 FinalSurvivorUnitId 尝试部署。
- Overflow 在安全购买后删除。
- Deployed 空时取严格排序最右槽、槽内最小 UnitId，只试 `(5,2)`。
- 失败不尝试第二个单位。
- NativeBot/TakeoverBot 不执行人类安全购买/扫描部署。
- DisconnectedGrace Human 仍执行统一人类安全规则。
- 封存一次 revision，无中间快照。

### 25.5 四人表

- 精确 golden table 六轮。
- 每对 H/A 各一次。
- 无连续同对手。
- 不超过两场同角色。
- 六轮后重复。

### 25.6 三人表与切换

- 精确 golden table 六轮。
- 每轮一 Official、一 Shadow。
- ShadowOwner 每轮两场但只有一个 AppliedResult。
- 其他两人各一个 AppliedResult。
- 每人每三轮一次只接受 Shadow，且不连续。
- 三轮后主客反转。
- 4→3 枚举器确定性选择，优先避免上一轮重复真实对。
- 输入列表顺序打乱不改变选择。

### 25.7 两人和轮转

- 每轮唯一 Official，Home/Away 交替。
- 3→2 接受连续同对手但最小化角色重复/失衡。
- 淘汰后下一轮立即换模板。
- PairingGeneration/偏移稳定保存。

### 25.8 结果验证

- HomeDamage<AwayDamage 得 HomeWin，反之 AwayWin，相等 Draw。
- 声称 Outcome 与伤害矛盾时拒绝。
- 负伤害、溢出、错误 BattleId/hash 拒绝。
- 缺结果、额外结果、重复不同结果拒绝。
- 相同结果重发幂等。
- 正常、双方全灭、超时均只按伤害比较。

### 25.9 影子结算

- ShadowOwner 忽略 Shadow 伤害、结果、streak、奖励。
- Shadow 非 owner 正常扣血并更新。
- Official 双方正常。
- 每人恰好一个 AppliedResult。
- Shadow Draw 时只有非 owner 可能取得该场平局彩蛋。

### 25.10 收入和连续状态

- 基础 Gold/Cost 六档边界精确。
- Win/Loss 在 1、2、4、6 场档位精确。
- 结果切换重置为新类型 Count1。
- Draw 清 None/0。
- R1—8 Draw 无额外。
- R9/13/17/21 Draw 额外分别为 6/14、7/16、8/18、9/20。
- 无固定战斗奖励、无利息、无高精英奖励。
- 新淘汰者本轮不获任何资源。
- Cost 收入同时增加 Total/Available。
- 战后自然刷新与减费只执行一次并嵌入同一 revision。

### 25.11 淘汰和排名

- Life 可降为负。
- 同轮 -3 排名高于 -8。
- 相同 Life 共享名次并正确跳号。
- 全员同轮淘汰最高 Life 为 1；完全相同共享 1。
- 唯一存活者 Placement=1。
- 旧淘汰名次不重算。
- 淘汰玩家后续无命令、无收入、不返池。
- 淘汰房主仍可作为 authority 继续。

### 25.12 结束

- 竞技结束保存 FinalStandings、Ended、ReturnAllToMainMenu。
- 最终 Settlement 只在播放完成信号后提交。
- 最终存活者仍有完整回合结算记录。
- Host Abort 从 Preparation/Battle/Settlement 均 NoContest，不扣血不发钱。
- Abort 不创建 AI 房主。
- Ended 重发 effect 不重放领域结算。

### 25.13 原子性与隐私

- 在封存/结算每个步骤注入失败，完整摘要和 revision 回滚。
- Public 不泄露经济、Pool、AI、token。
- 淘汰旁观者只获得 Public。
- SealedBattleInput 不含 Shop/Gold/Cost/Overflow。
- Battle 阶段商店变化不改变 SealedRoundPlan hash。
- M1—M3 全部既有测试继续通过。

## 26. 验证顺序

按仓库规范执行并记录：

1. `git diff --check`
2. 审查最终 diff、新增 `.meta` 和 asmdef 引用
3. Match 相关 C# 编译
4. M1—M3 既有 EditMode 测试
5. M4 新增 EditMode 测试
6. 相关完整 EditMode 测试集
7. 如已有纯流程 PlayMode harness，运行最小 Round1→Battle→Settlement→Round2 冒烟；不要为本任务改场景
8. 检查 Unity 日志无新增错误/异常
9. 独立审查：
   - 最后 Ready 是否有可取消窗口
   - 封存是否发布中间状态
   - 影子 owner 是否被结算两次
   - 新淘汰者是否错误获得收入
   - 轮转是否依赖 Dictionary 顺序
   - Battle 结果是否被直接信任而未按伤害复核
   - 最终回主界面是否早于房主播放完成
   - Abort 是否错误产生胜者

测试数为 0、Unity 占用、许可证失败、超时、结果 XML 缺失或日志不完整均标为未验证。

## 27. 完成交付

最终提交前：

- 更新实际受影响的架构/测试文档，不改写无关 SPEC。
- 不提交 Library、Temp、Logs、Build、Artifacts 或无关用户修改。
- 创建一个聚焦 M4 的 commit。

交付给主 Planner：

1. 分支名和 commit SHA；
2. M1—M3 基线 SHA；
3. 修改文件；
4. Phase/Formation/Pairing/Settlement 的关键 API；
5. M5 AI、M6 LAN、M7 Battle 接入点；
6. 实际测试命令、退出码、测试数量和结果文件；
7. 未验证项和剩余风险；
8. 明确没有实现 M5—M8。

停止条件：

- M3 不是指定基线；
- 无法在一个 transaction 中组合封存或结算；
- Battle adapter 无法提供双方 LifeDamage/输入 hash；
- 现有部署规则与 SPEC 出现玩家可见冲突；
- 轮转候选无法满足规则且文中放宽原则不足以唯一决定；
- 需要修改高冲突场景/Prefab、升级 Unity/Package 或安装未批准依赖；
- 连续三次有实质差异的尝试仍被同一测试阻塞。

遇到停止条件时保留证据并报告，不得用固定 Demo 配对、假伤害、吞错、跳过影子或硬编码胜者绕过。
