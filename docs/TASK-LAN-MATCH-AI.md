# M5：分层自动人机 Agent 提示词

> 状态：已完成。来源提交 `d350049` 已作为 `8311a2d` 进入统一 M1—M8 集成链；本文件保留为 M5 交付契约。
> 本文件可直接作为实现 Agent 的任务提示词。本轮实现第一版简单确定性 AI，不追求策略强度。

## 1. 任务角色与目标

你负责为完整 LAN 对局实现独立、分层、可测试的自动人机：

1. 只读且权限裁剪的 `BotObservation`；
2. 无副作用的 `BotDecisionMachine`；
3. 把意图映射为真人同款权威命令的 `BotOperationAdapter`；
4. 准备阶段固定决策 Tick 的 `BotController`；
5. 原生 AI、掉线接管 AI 和主动退出接管；
6. 购买、刷新、升级；
7. 每次成功购买后的单次立即部署尝试；
8. 全真人准备后的立即停止；
9. 重连归还控制权且不回滚状态；
10. 淘汰停机和公共身份伪装边界。

AI 不需要 Ready，不在 Battle 中操作，不冻结、不撤退、不换位，也不进行准备阶段末全阵容扫描。

## 2. 分支、基线与工作方式

- 使用独立 worktree。
- 分支名使用 `codex/lan-match-ai`。
- 必须基于主 Planner 指定的 M4 commit 创建，记录 M1—M4 实际基线 SHA。
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
3. 阅读 M1—M4 实际实现、公开 API、asmdef 和全部 Match EditMode 测试。
4. 检查现有本地玩家商店/部署调用方式，但不要让 AI 直接引用 `LocalMatchState` 或 UI。
5. 执行 `git status --short`，保护已有修改。
6. 确认没有 Unity 进程占用该 worktree。
7. 运行 M1—M4 Match EditMode 测试作为基线。

## 3. 程序集和依赖方向

建议新增纯 C# AI 程序集：

```text
Assets/Game/Runtime/MatchAI/
  ARKnoNIGHTS.MatchAI.asmdef
  Observation/
  Decision/
  Operations/
  Scheduling/
  Takeover/
```

依赖方向：

```text
ARKnoNIGHTS.MatchAI
  → ARKnoNIGHTS.Match
```

不允许 Match 领域层反向依赖 MatchAI。M4 可通过接口/事件通知 AI scheduler，但不能引用具体策略类型。

要求：

- 优先 `noEngineReferences: true`。
- 不依赖 MonoBehaviour、场景、UI、Resources、Socket 或 Battle Core。
- 不读取墙上时间；使用 M4 同一房主单调时钟。
- 不使用随机数；相同 Observation 必须产生相同 Intent。
- 新增 `Assets/` 文件必须有匹配 `.meta`。

## 4. 四层结构

### 4.1 Observation

只从 Match 权威投影构造不可变 `BotObservation`。

### 4.2 Decision

纯函数：一个 Observation 产生一个主要 Intent 或 Wait，不改状态。

### 4.3 Operation

把 Intent 映射为 M2/M4 已有的 Refresh/Purchase/Upgrade/Deploy 权威命令核心。

### 4.4 Controller

负责激活、准备入口周期、固定 Tick、幂等 ActionId、提交结果和购买后的单次部署 follow-up。

不得把四层合成一个直接修改 MatchState 的 `UpdateBot()`。

## 5. BotObservation

最小字段：

```text
BotObservation
  SessionId
  StateRevision
  RoundNumber
  Phase
  SeatIndex
  PlayerId
  ControllerKind
  IsEliminated
  Gold
  Level
  CurrentUpgradePrice?
  TotalDeploymentCost
  AvailableDeploymentCost
  StagingSlotUsage
  ShopOffers[6]
  OwnUnits[]
  OwnFormation[]
  PublicHumanReadiness[]
  PreparationDeadlineHostMs
  DecisionOrdinal
```

每个 ShopOffer 至少包含：

- SlotIndex
- UnitId
- TypeId
- Rarity/Price
- 当前精英 0 部署费用
- IsFrozen（AI 不操作，但观察可保留）

权限：

- 可以读取自己的 OwnerPrivate。
- 可以读取公共席位、公开 Ready/连接/淘汰状态。
- 不读取任何对手 Gold、Cost、Level、Shop、Overflow、Buff 私有 payload 或 AI 内部状态。
- 不读取共享牌库余量、房主随机状态或 reconnect token。
- 不读取 Battle Core 结果来作弊调整当前准备决策。

Observation 构造失败必须显式停止该 AI 周期，不能退回读取 HostOnly 完整 MatchState。

## 6. BotIntent

只允许：

```text
Wait
Buy(slotIndex, expectedUnitId)
Refresh
Upgrade(expectedLevel, expectedPrice)
```

Deploy 不是决策机独立选择的主要 Intent，只是 Buy 成功后的 operation follow-up。

明确不允许：

- Freeze/Unfreeze
- Retreat
- Relocate/Swap
- Replace
- SetReady
- 手动 Fuse
- 主动退出

每个决策周期最多产生一个主要 Intent。Buy 成功后可以追加一次 Deploy follow-up；这仍属于该周期，不再重新运行决策。

## 7. 第一版确定性决策机

目标只是建立可运行接口和合理基础行为，不追求最优阵容。

### 7.1 立即 Wait 条件

按顺序检查：

1. Phase 不是 Preparation；
2. ControllerKind 不是 NativeBot/TakeoverBot；
3. 已淘汰；
4. 当前全部所需真人已经 Ready，M4 将立即封存；
5. 当前 host monotonic time 已到 Preparation Deadline；
6. StagingSlotUsage 已达 13；
7. Observation/目录字段非法。

任一成立返回 Wait。

第 6 条只阻止购买/刷新式继续消耗；若 Level 可升级，仍按 7.3 的升级判断执行。实现时不要在总入口过早返回，应将“满 13”作为购买候选为空。

### 7.2 购买候选

在六个非空商品中选择同时满足：

- Price <= Gold；
- StagingSlotUsage < 13；
- 商品具体 UnitId 与 Observation 一致。

为每个候选派生：

```text
CanLikelyDeploy = BaseElite0DeploymentCost <= AvailableDeploymentCost
```

第一版不在决策层预测合成后的最终费用，也不要求部署一定成功。候选按：

1. CanLikelyDeploy：true 优先；
2. SlotIndex 降序；
3. UnitId ordinal 升序；

取第一个，即先选择当前 Cost 看来可以立即部署的最右侧可支付商品；若都不能立即部署，仍可购买最右侧可支付商品留在 Staging。

### 7.3 Upgrade/Buy/Refresh/Wait 顺序

设：

```text
G = Gold
U = CurrentUpgradePrice（Level 9 时不存在）
P = 所选购买候选价格
```

精确状态机：

1. 若存在购买候选，且存在 Upgrade，并满足 `G >= U + P + 2`，选择 Upgrade。
2. 否则若存在购买候选，选择 Buy。
3. 否则若存在 Upgrade 且 `G >= U`，选择 Upgrade。
4. 否则若 `StagingSlotUsage < 13` 且 `G >= 2`，选择 Refresh。
5. 否则 Wait。

解释仅用于维护：

- `+2` 是升级后至少保留一次较高价购买/刷新余量的固定第一版阈值。
- AI 不把 1 Gold 全部用于最后一次刷新，因此 Refresh 要求至少 2。
- Level 9 没有 Upgrade。
- AvailableDeploymentCost 不作为购买硬前置；它参与 8.2 部署结果，AI允许先把单位留在 Staging。

不得根据对手隐藏数据、随机数、地区羁绊强度或战斗胜率增加分支。本轮不要“优化”阈值。

### 7.4 Cost 的使用

决策机必须读取 Cost，第一版将其用于购买候选优先级、可诊断结果和部署 follow-up：

- `AvailableDeploymentCost <= 0` 不阻止购买到 Staging。
- 当前精英 0 Cost 可支付的商品优先于不可支付商品。
- 部署 follow-up 由权威 Deploy 核心验证实际 Elite Cost。
- Wait/日志摘要要能说明部署失败是否因为 Cost。

这符合“用已有 Cost、Gold 和升级价格作简单判断”的接口目标，同时不假装升级会增加 Cost；实际 Cost 由回合结算增加。

## 8. 购买后立即部署

### 8.1 目标 UnitId

Buy 接受后，从 M3 Purchase 结果读取：

```text
FinalSurvivorUnitId
FinalZone
FinalEliteLevel
```

- 购买的原 UnitId 若在合成中被消费，绝不能再尝试部署它。
- FinalSurvivor 已 Deployed：不做 follow-up，视为无需部署。
- FinalSurvivor 在 Staging：执行一次 Deploy 尝试。
- FinalSurvivor 在 Overflow 或已退休：不部署。

不扫描旧 Staging 单位，也不在没有购买的周期部署。

### 8.2 固定坐标顺序

Operation 层从当前 OwnFormation 选择第一个空合法格。顺序固定为：

1. Y 行顺序：`2, 3, 4, 1`
2. 每行 X 顺序：`5, 4, 6, 3, 7, 2, 8, 1, 9`
3. 跳过门格 `(5,1)`

所以第一个目标为 `(5,2)`。总计覆盖 35 个合法格。

然后只提交一次 M4 `DeployUnit` 核心：

- 使用 FinalSurvivorUnitId；
- 使用选中的第一个空格；
- 使用购买结果 revision 对应的当前 AvailableCost 前置条件。

失败处理：

- Cost 不足、目标并发占用、Zone 已变化或没有空格时，单位保持当前状态。
- 不尝试第二格。
- 不尝试第二单位。
- 不撤退/换位现有单位。
- 不在同周期再次决策。

部署尝试本身必须走和真人相同的 MatchAuthority 语义验证，不直接改 Formation/Cost。

## 9. 决策 Tick

固定周期：

- 准备阶段入口立即执行一次初始周期，逻辑偏移 `0 ms`。
- 之后在 `1000, 2000, ... 29000 ms` 各执行一次。
- `30000 ms` 由 M4 Deadline 先封存，不执行 AI 周期。
- 每个周期每个活跃 AI 最多一个主要 Intent，外加 Buy 成功后的一个 Deploy follow-up。
- 同一 Tick 按 SeatIndex 升序处理 AI。

若 host pump 从 2500ms 跳到 5500ms：

- 依次补处理 3000、4000、5000ms 三个尚未执行周期；
- 每个周期使用上一个周期提交后的新权威状态；
- 不用一帧一次的渲染调用次数决定 AI 行为；
- 最多一轮 30 个周期，不会无限追赶。

若任一周期导致 M4 立即封存，后续周期停止。

## 10. ActionId 与幂等

每个主要动作和 follow-up 使用稳定系统动作身份：

```text
BotActionId
  SessionId
  SeatIndex
  ControllerGeneration
  RoundNumber
  DecisionOrdinal
  ActionPart = Primary | DeployFollowUp
```

要求：

- 同一调度周期重试返回原结果，不重复扣金/刷新/升级/购买/部署。
- ControllerGeneration 在 NativeBot 初始化或每次新的 TakeoverBot 激活时固定递增。
- 重连归还 Human 后，旧 generation 的延迟动作全部拒绝。
- 同一 ID 不同 intent 冲突。
- 不依赖随机 Guid、系统时间或对象 hash。

Bot 操作通过 Host internal authorized origin 调用现有命令核心；不能伪造真人网络 CommandId，也不能绕过 M1 幂等记录。

## 11. 准备入口排序

M4 发布 PreparationOpened 后：

1. 确定本轮开始时已经活跃的 NativeBot/TakeoverBot。
2. 按 SeatIndex 执行它们的 `0 ms` 初始决策周期。
3. 提交每个动作后重建该 Bot 的 Observation。
4. 初始周期全部完成后，M4 才把阶段标记为可接受普通真人命令，或完成等价的房主串行门控。
5. 若没有需要 Ready 的真人，初始 AI 周期结束后立即封存。

全部真人 Ready 后：

- M4 立即进入 Sealing。
- AI 不获得所谓“最后一次行动”。
- 已排队但尚未开始的 AI 动作检查 Phase/Revision 后拒绝。

## 12. 原生 AI

开局空席位由上层创建为 NativeBot：

- 从 Round 1 Preparation 入口开始运行。
- 显示名和 AvatarId 在 Match 初始化前由 Lobby/Session 上层从未使用身份中选择并冻结。
- Public 中表现为普通 Online 玩家。
- 不增加 Bot 标签、专用图标或 UI 字段。
- AI 层不能更换显示名/头像。
- 不拥有 Ready。
- 淘汰后停止。

## 13. 掉线接管

M5 只接收 M6 已验证的连接事件，不负责 token 身份验证。

### 13.1 普通掉线宽限

原 Human 掉线后保留席位和全部状态。接管时点：

- 在 Round 5 Preparation 掉线：
  - Round 5 剩余时间不行动；
  - Round 6 Preparation 入口切换 TakeoverBot 并执行初始周期。
- 在 Round 5 Battle 掉线：
  - Round 6 Preparation 是完整宽限，不行动；
  - Round 7 Preparation 入口切换 TakeoverBot 并执行初始周期。

一般化记录：

```text
PendingTakeover
  PlayerId
  DisconnectedRound
  DisconnectedPhase
  EffectivePreparationRound
```

规则：

- Preparation 掉线：EffectiveRound = DisconnectedRound + 1。
- Battle/Sealing/Settlement 掉线：EffectiveRound = DisconnectedRound + 2。
- 宽限中 ControllerKind 仍是 Human、Connection=DisconnectedGrace、Ready=false。
- 宽限期间 AI 不购买、刷新、升级或部署。
- M4 的统一人类封存安全规则仍可作用于该席位。
- 到 EffectiveRound 的 Preparation 入口原子切换 TakeoverBot，并取消 Ready 语义。

### 13.2 非房主主动退出

- Preparation 主动退出：立即切换 TakeoverBot；若阶段尚未封存且全部真人尚未 Ready，立即执行一次 takeover 初始周期，然后加入后续固定 Tick。
- Battle/Sealing/Settlement 主动退出：安排下一次 Preparation 入口接管。
- 主动退出者不能重连；M6 清 token。

### 13.3 房主

- 房主玩家不能切换 TakeoverBot。
- 房主退出/进程终止由 M4/M6 直接 NoContest 结束整局。
- 房主玩家被淘汰不等于退出；服务器继续，AI 不接管房主席位。

## 14. 重连与归还控制

普通掉线原玩家重连：

- 接管前：取消 PendingTakeover，恢复 Human/Connected，保留当前状态。
- 接管后：立即把 ControllerKind 从 TakeoverBot 恢复 Human/Connected。
- 不回滚 AI 已经完成的购买、刷新、升级、合成或部署。
- 不重建 Shop、Units、Gold、Cost、Level 或 Formation。
- 停止旧 ControllerGeneration；其延迟 ActionId 失效。
- Preparation 中 Ready 保持 false，玩家必须自己准备。
- Battle 中归还控制不产生任何 Battle 操作；下一 Preparation 才能操作。

原生 NativeBot 席位不可被陌生玩家重连取代。

身份和 token 校验由 M6 完成；M5 只接受 Host 内部 `RestoreHumanControl(playerId)`。

## 15. 操作结果和恢复

主要 Intent 可能因权威状态在观察后变化而被拒绝：

- OfferChanged
- InsufficientGold
- StagingFull
- UpgradePriceChanged
- PhaseRejected
- ControllerGenerationChanged
- Eliminated

规则：

- 拒绝后本周期结束。
- 不在同一周期重新观察并选择另一个动作。
- 下一固定 Tick 再决策。
- Buy 主动作拒绝则没有 Deploy follow-up。
- Buy 成功而 Deploy 失败仍保留购买结果。
- 日志/诊断使用稳定 code，不依赖本地化文本。

## 16. Public 伪装与隐私

M5 不修改 Public 模型暴露 ControllerKind：

- NativeBot/TakeoverBot 继续显示 Online。
- 不新增 Bot 标记。
- 不显示“AI 接管”提示。
- 未使用头像/普通名字由 Session 初始化负责。

BotObservation 不能通过 HostOnly 快捷访问泄露对手信息。测试应构造“改变对手私有经济但保持 Public 相同”，验证 AI Intent 不变。

## 17. CanonicalSummary 与审计

AI 内部 HostOnly 状态至少保存：

```text
BotRuntimeState
  PlayerId
  ControllerGeneration
  NextDecisionOrdinal
  NextDecisionAtOffsetMs
  PendingTakeover?
  LastIntent
  LastResultCode
```

要求：

- 稳定字段顺序、ordinal 字符串和 invariant 数字。
- 不保存对象引用 hash。
- 同状态摘要可复现。
- Public/OwnerPrivate 不包含策略内部状态。
- 可通过 ActionId 和 Match revision 审计 AI 每一步，但不要求长期保存全文日志进 MatchState。

## 18. 明确不在 M5 范围

不得实现：

- 更强阵容估值、羁绊、对手侦察、战斗胜率模型；
- 随机策略或难度档；
- Freeze/Retreat/Relocate/Replace；
- AI Ready；
- 阶段末全 Staging 扫描部署；
- 购买失败后的同周期重试；
- 部署失败后的第二坐标/第二单位重试；
- Battle 中操作；
- 牌库外赠送；
- 掉线 token、Socket 或协议；
- Battle Core、分块播放和 hash；
- UI Bot 标签或身份暴露；
- 房主迁移；
- 场景/Prefab 修改；
- Unity/Package 升级。

## 19. TDD 与测试要求

先写失败测试，再实现。至少覆盖：

### 19.1 Observation 隐私

- 只包含自己的 OwnerPrivate 和 Public。
- 改变对手 Gold/Shop/Cost 不改变 Observation/Intent。
- 不含 Pool、token、Host 随机状态。
- 非法/缺失目录字段显式 Wait+诊断。

### 19.2 决策表

- 非 Preparation、非 Bot、Eliminated、全真人 Ready 分别 Wait。
- 当前 Cost 可部署的候选优先；同组内优先最大 SlotIndex。
- 所有商品 Cost 都不可部署时仍可选择最右侧可支付商品。
- `G >= U+P+2` 时 Upgrade。
- 有候选但升级阈值不足时 Buy。
- 无候选但 Upgrade 可支付时 Upgrade。
- 无候选、不能升级、G>=2、Staging<13 时 Refresh。
- G<2 或 Staging满且不能升级时 Wait。
- Level9 无 Upgrade。
- 相同 Observation 始终同 Intent。

使用精确表驱动测试覆盖等号边界，例如 `G=U+P+1` 与 `G=U+P+2`。

### 19.3 购买和部署

- Buy 使用 expected UnitId。
- 购买合成后部署 FinalSurvivorUnitId，不部署 consumed ID。
- FinalSurvivor 已 Deployed 时无 follow-up。
- Staging 时只部署一次。
- 坐标顺序从 `(5,2)` 开始，按 Y `2,3,4,1` 和中心向外 X。
- 跳过 `(5,1)`，恰好 35 格。
- 第一个空格失败后不试第二格。
- Cost 不足购买仍保留、单位留 Staging。
- 无空格时不部署。
- 不扫描旧 Staging。

### 19.4 Tick

- 0ms 初始周期。
- 1000..29000 精确 29 个后续周期。
- 30000 不执行。
- 时间跳跃按逻辑周期补处理，结果不依赖 pump 帧数。
- SeatIndex 顺序稳定。
- 一个周期最多一个主要动作和一个 Buy follow-up。
- 阶段中途封存后停止。

### 19.5 幂等

- 相同 BotActionId 重放不重复扣金/购买/升级/部署。
- 同 ID 不同 Intent 冲突。
- generation 切换后旧动作拒绝。
- Buy 成功、follow-up 重试不重复 Buy。
- 失败动作不递增不应递增的 Match revision。

### 19.6 接管

- Prep R5 掉线：R5 不行动，R6 入口接管。
- Battle R5 掉线：R6 完整宽限，R7 入口接管。
- 宽限期不操作，M4 人类安全规则仍适用。
- Prep 主动退出立即接管。
- Battle 主动退出下一 Prep 接管。
- 房主永不接管，触发外层 NoContest。
- 淘汰 AI 停止。

### 19.7 重连

- 接管前重连取消 pending。
- 接管后重连恢复 Human 且不回滚状态。
- 旧 generation 延迟动作拒绝。
- Ready 不自动恢复。
- NativeBot 不允许被认领。
- Battle 重连不产生操作。

### 19.8 准备入口和立即封存

- AI 初始周期早于真人普通命令门开放。
- 无需 Ready 的纯 AI 存活集合在初始周期后封存。
- 最后真人 Ready 后没有 AI 最后动作。
- 全真人 Ready 与同 Tick AI 动作按房主队列顺序确定，Phase 检查阻止封存后动作。

### 19.9 回归

- M1—M4 全部既有测试继续通过。
- AI 不修改 Public ControllerKind。
- Battle 阶段 Shop/Unit 不被 AI 修改。
- 无 Freeze/Retreat/Relocate/SetReady 调用路径。

## 20. 验证顺序

按仓库规范执行并记录：

1. `git diff --check`
2. 审查最终 diff、asmdef 和 `.meta`
3. Match/MatchAI C# 编译
4. M1—M4 既有 Match EditMode 测试
5. M5 MatchAI EditMode 测试
6. 相关完整 EditMode 测试集
7. 若已有纯流程 harness，运行一局 1 Human+3 NativeBot 的虚拟时钟冒烟，不改场景
8. 检查 Unity 日志无新增错误或异常
9. 独立审查：
   - AI 是否读了对手私有状态
   - 是否用渲染帧驱动 Tick
   - 是否在 Buy 后重新部署错误 UnitId
   - 是否存在同周期重试循环
   - 全真人 Ready 后是否仍有动作
   - takeover 时点是否差一回合
   - 重连是否错误回滚
   - Public 是否泄露 Bot 身份

测试数为 0、Unity 占用、许可证失败、超时、结果 XML 缺失或日志不完整均标为未验证。

## 21. 完成交付

最终提交前：

- 更新实际受影响的架构/测试文档，不改写无关 SPEC。
- 不提交 Library、Temp、Logs、Build、Artifacts 或无关用户修改。
- 创建一个聚焦 M5 的 commit。

交付给主 Planner：

1. 分支名和 commit SHA；
2. M1—M4 基线 SHA；
3. 修改文件；
4. Observation/Decision/Operation/Controller 关键 API；
5. M6 连接事件接入点；
6. 实际测试命令、退出码、测试数量和结果文件；
7. 未验证项和风险；
8. 明确没有实现 M6—M8 或高级 AI。

停止条件：

- M4 不是指定基线；
- M4 没有可复用的 Purchase/Refresh/Upgrade/Deploy 权威命令核心；
- 无法在不访问 HostOnly 对手私有状态的情况下构造 Observation；
- M4 准备入口不能保证 AI 初始周期排序；
- SPEC 与本文出现新的玩家可见 AI 行为冲突；
- 需要改场景/Prefab、升级 Unity/Package 或安装未批准依赖；
- 连续三次有实质差异的尝试仍被同一测试阻塞。

遇到停止条件时保留证据并报告，不得让 AI 直接改 MatchState、读取对手商店或用随机操作绕过。
