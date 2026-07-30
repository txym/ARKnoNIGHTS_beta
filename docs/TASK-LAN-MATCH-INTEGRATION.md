# M8：正式 LAN 对局运行时、HUD 与端到端集成 Agent 提示词

> 状态：已完成。M8 实现提交为 `b3bfb44`；Battle 基线修复在其上移植为 `9807472`，本文件保留为最终集成交付契约。
> 本文件可直接作为集成 Agent 的任务提示词。本轮目标是得到真正从 LAN 房间进入并持续同步对局的可玩纵切面。

## 1. 任务角色与最终目标

你负责集成，不重新设计或重写各模块。

完成后应能：

1. 1—4 名真人通过现有 LAN 房间号创建/加入房间；
2. 空席位由伪装身份的 NativeBot 填充；
3. 房间开始后保持原 TCP 连接并进入 Match Session；
4. Host 和 guest 都从权限裁剪快照驱动 HUD；
5. 真人执行商店、升级、部署、撤退、换位、Ready；
6. AI 在准备阶段按 M5 运行；
7. 全真人 Ready 或 30 秒到时封存；
8. 所有在线机器独立计算本轮全部 Official/Shadow 战斗；
9. 第一块就绪后按房主统一时钟播放，计算继续提前一块；
10. 房主播放完成后提交权威结算；
11. 所有客户端看到相同 Life、排名、下一回合和商店状态；
12. 普通断线/应用重开可用 token 恢复当前阶段；
13. 掉线接管和重连归还不回滚；
14. 对局结束直接返回主界面；
15. 不制作结算界面，不增加 Bot 标记，不实现重赛房间。

## 2. 集成分支和提交顺序

- 使用唯一独立 worktree。
- 分支名使用 `codex/lan-match-integration`。
- 不在主工作区直接集成。
- 不创建子 Agent，除非主 Planner明确授权。
- Unity Editor/测试在本 worktree 独占并串行。

开始前从主 Planner取得并记录：

```text
M1 domain commit
M2 economy commit
M3 fusion commit
M4 flow commit
M5 AI commit
M6 session commit
M7 battle-streaming commit
unit/ability content commits（若已有）
```

建议依赖顺序：

```text
M1 → M2 → M3 → M4 → M5 → M6
                         ↘
                           M7
→ M8 integration
```

要求：

- 基于主 Planner 指定的共同基线创建集成分支。
- 按顺序 cherry-pick/merge 指定 commit，每一步检查冲突和测试。
- M7 若从较早基线并行开发，只适配公开契约，不覆盖 M1—M6 的 Match 文件。
- 单位/能力分支若已经完成，以其实际目录和版本 adapter 为准。
- 不从未提交的其他 worktree 复制散落文件。
- 遇到同一场景/Prefab 或高冲突生成目录修改，停止并向主 Planner确认正确拥有者。

## 3. 开始前阅读和基线

完整阅读：

- 根目录 `AGENTS.md`
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
- `docs/TASK-LAN-MATCH-SESSION.md`
- `docs/TASK-BATTLE-STREAMING.md`

检查实际代码：

- `Assets/Game/Runtime/Initial/LanLobbyController.cs`
- Lobby/Session host/client/protocol
- Match/MatchAI 程序集
- `PreparationBattleLoopController`
- `PreparationBattlePhase`
- `LocalMatchState` 和正式替代投影
- 商店 HUD/controller
- 部署/撤退/换位 controller
- Ready/倒计时 UI
- Player list/observer
- `MultiBattlePresentationCoordinator`
- 流式 Battle producer/playback
- 场景 bootstrap 和 asmdef

执行：

- `git status --short`
- 每个已集成里程碑的 focused 测试
- 当前场景最小 PlayMode 基线

保护用户修改和 GUID。不提交生成目录。

## 4. 正式运行时组合根

建立单一运行时组合根，例如：

```text
LanMatchRuntimeController
  SessionRole = Host | Guest
  SessionHost/SessionClient
  ScopedClientProjection
  MatchAuthority?          // Host only
  BotCoordinator?          // Host only
  RoundBattleCoordinator   // Every online machine
  MatchHudBinder
  ReconnectCoordinator
```

原则：

- Host 持有唯一 MatchAuthority。
- Host 自己也维护一个按 host 玩家身份裁剪的 `ScopedClientProjection`，HUD 不读取 HostOnly。
- Guest 从不创建可写 MatchAuthority。
- 每台在线机器都创建本地 M7 Battle Producer 集合。
- UI 只读取本机 scoped projection 和本机 Battle presentation。
- Host 本地 UI 命令也进入 M6 的统一 HostAcceptSequence 队列。
- Runtime controller 负责生命周期组合，不复制领域规则。

不要继续让 `PreparationBattleLoopController` 的 `LocalMatchState` 充当 LAN 权威。旧 Demo 可保留在明确的 offline/demo mode，正式 LAN 路径必须显式选择新 runtime。

## 5. Lobby handoff

修改当前开始流程：

- Host `TryStart` 成功后进入 M6 StartCommitted。
- Guest 收到 MatchInitialized 后进入 Match。
- 停止 discovery，但保留 TCP listener/connection。
- `LanLobbyController.CompleteGameplayTransition()` 不得调用关闭 Match 所需连接的 `StopServices(false)`。
- Lobby view 隐藏。
- 正式 Match HUD 激活。
- 初始化失败时显示结构化错误并安全回主界面；不能静默放行本地 Demo。
- 重复 Start/MatchInitialized 幂等，不创建两个 runtime controller。

场景切换若会销毁 session 对象：

- 使用明确的 `DontDestroyOnLoad` 组合根或在切换前完成安全所有权转交；
- 不创建重复 bootstrap；
- OnDestroy 不能把正常 Match handoff 当成退出并关闭连接。

## 6. 目录、能力和兼容 hash

- 从实际运行时单位目录生成 MatchShopCatalog 和 Battle unit definitions。
- 从实际能力目录生成 AbilityCatalog hash 和 Battle definitions。
- 使用 M6 CompatibilityManifest 统一版本。
- 不硬编码当前 94 个商品、100 个源文档或能力列表顺序。
- 商店只纳入 ShopEligible TypeId。
- BattleSeal 中每个已部署单位/能力必须解析。
- 精英 `0/1/2/3` 按 M3/M7 契约展开 `1/2/3/5` 个战斗实体。
- 持久 UnitId 与 Battle EntityId 明确适配，不混用。
- 战斗召唤负数局部 ID 不回写 Match。

若单位能力 Agent 的实现与 M7 输入字段存在接口差异，新增单向 adapter；不要在集成层复制能力规则或回写源 JSON。

## 7. Scoped projection 驱动 HUD

### 7.1 本地拥有者数据

以下始终来自本地玩家 OwnerPrivate：

- Gold
- Level/升级价格
- Total/Available Cost
- Shop 六槽/冻结
- 自己的 Overflow
- 自己的命令可用性

切换观察玩家不能改变这些来源。

### 7.2 观察数据

场地、Staging 和单位详情来自当前观察玩家的 Public：

- Deployed
- Staging 严格堆叠槽
- Formation
- Life/淘汰/连接/Ready
- 公开持久 Buff 投影

不能因为 UI 已有本地对象就读取其他玩家 OwnerPrivate。

### 7.3 淘汰旁观

- 本地玩家淘汰后丢弃/停止接收 OwnerPrivate。
- 仍可选择观察公开阵型和战斗。
- 商店/升级/部署/Ready 命令全部禁用。
- 房主玩家淘汰后 HUD 同样只用 Public，但后台 authority 继续。

### 7.4 AI 伪装

- NativeBot/TakeoverBot 显示正常头像、名字和 Online。
- 不增加标签、图标、颜色或提示。
- UI 不接收 ControllerKind。

## 8. 命令绑定

UI 动作显式映射：

```text
Shop purchase        → PurchaseShopOffer(slot, expectedUnitId)
Refresh              → RefreshShop
Freeze toggle        → ToggleShopFreeze
Upgrade confirm      → PurchaseLevelUpgrade(expectedLevel, expectedPrice)
Deploy               → DeployUnit(unitId, target, expectedCost)
Replace confirm      → ReplaceDeployedUnit(...)
Retreat              → RetreatUnit(unitId)
Relocate/swap        → RelocateOrSwap(...)
Ready/cancel         → SetPreparationReady(desired)
Explicit quit        → ExplicitQuit
```

要求：

- 每个用户提交创建稳定 CommandId。
- Guest 经 M6 发送；Host local 经同一 authority queue。
- 不在 Ack 前修改本地权威 Gold、Shop、Unit、Formation 或 Ready。
- 可保留纯视觉拖动/确认预览，但提交失败必须回到 snapshot 状态。
- Ack 和更新 snapshot 乱序时由 revision 规则收敛。
- Pending 命令不能阻塞无关 UI 超过必要范围。
- Phase/Ready/Eliminated 改变后立即取消无效拖动、替换预览和选择。
- 观察玩家切换不改变命令 PlayerId。

## 9. 准备阶段 UI

- 倒计时显示基于 M6 ClockSync 后的房主 Deadline。
- 不用本地 `Time.time` 自己触发阶段。
- 30 秒到时只等待房主 snapshot/phase。
- Human 显示 Ready/取消 Ready。
- Ready=true 锁定阵型操作，商店仍可用。
- NativeBot/TakeoverBot 不显示 Ready 状态差异。
- 最后 Human Ready 被接受后立即隐藏/禁用 Ready 并进入 Sealing/Battle。
- 掉线清 Ready 后 snapshot 立即反映。
- Reconnecting 本机停止命令但保留最后只读画面。

M5 initial bot cycle 和无真人立即封存都由 Host runtime 调度，不由 UI 模拟。

## 10. Round seal 与本地 Battle Producer

收到同一个 BattleSeal 后，每台在线机器：

1. 严格验证 Session/Round/manifest/input hash。
2. 为全部 SealedPairing 创建 M7 Producer：
   - 四人：两场 Official；
   - 三人：一场 Official + 一场 Shadow；
   - 两人：一场 Official。
3. 使用稳定轮转预算推进，不能先算完一场。
4. 全部本地战斗都产出第一块后：
   - Guest 发 FirstChunkReady；
   - Host local coordinator 直接登记 local ready。
5. 继续计算，至少维持一个 100 Tick 块 ahead。

任何客户端都不能用房主的 BattleResolution/Chunk 替代本地 Core 计算。正常网络不传输 5 秒 chunk。

Host 自身任一战斗首块失败：

- 调用 M4 AbortMatchNoContest/FatalMatchError；
- 所有人直接回主界面；
- 不发送空 ready 或伪造结果。

Guest 本地计算失败：

- 上报结构化 ClientBattleFailure 诊断；
- mismatch/failure 的最终处置目前未确认，不能擅自踢人或替换房主结果；
- 本地进入同步错误状态，host 正常权威流程不由未确认策略改写。

## 11. 首块等待和统一播放时钟

Host 在 BattleSeal 发布时冻结“当前在线连接集合”并启动 `10,000 ms` 首块等待：

- Host local 全部第一块必须成功。
- 在线 Guest 报 FirstChunkReady。
- Guest 期间断线则从等待集合移除，进入普通掉线流程。
- 单纯未 ready 的慢 Guest 不算断线、不触发 AI。
- 全部等待对象 ready 后可提前开始。
- 10 秒到达后不再等待慢 Guest。

Host 广播一个未来统一 `PlaybackStartHostMonotonicMs`。第一版技术常量：

```text
PlaybackStartLeadMs = 1000
```

用于让 LAN 消息到达并按 ClockSync 对齐；它不改变 20 TPS 战斗时间。

Client：

- 根据 M6 时钟偏移映射到本机单调时钟。
- 不因渲染帧累计产生权威 Tick。
- 统一表现 Tick 为：

```text
floor((EstimatedHostNowMs - PlaybackStartHostMs) * 20 / 1000)
```

- clamp 到 `0..GlobalRoundEndTick`。
- 使用整数/明确舍入。

ClockSync 应使用现有 Ping/Pong RTT 样本的稳定估计；至少过滤明显过期样本并测试 LAN 延迟下不会倒退表现 Tick。不要引入外部时间服务。

## 12. 播放、缓冲和观察

### 12.1 全局 Tick

- 同轮所有战斗共享同一表现 Tick。
- 提前结束的战斗保持终态。
- GlobalRoundEndTick 为本轮所有 Battle EndTick 的最大值。
- Host 不因某个 guest 本地画面暂停而暂停全局。

### 12.2 本地 underflow

如果本机 Producer 未算到下一个表现 Tick：

- 只暂停本机画面在最后可用 Tick；
- 显示“同步中”；
- 房主全局 Tick 继续；
- Core 按预算继续计算；
- 恢复后从本机最近 checkpoint 加速重建到当前全局 Tick；
- 跳过错过动画，但不跳过 Core Tick。

正常路径应通过首块和 one-chunk-ahead 避免 underflow；不要把“播放必然落后”写成常规流程。

### 12.3 观察映射

选择某玩家时，显示“本轮实际应用于该玩家结算”的 Pairing：

- 四人/两人：唯一 Official。
- 三人：
  - ShadowOwner 显示自己的 Official；
  - Official 另一方显示 Official；
  - 只接受 Shadow 结果的玩家显示 Shadow。

按该 Pairing 中玩家的 Home/Away role 使用相应观察视角。

切换观察：

- 不重算 Core；
- 在当前全局 Tick 从本机 checkpoint/track 重绑；
- 已死亡/已冲家实体不创建；
- 当前动作可从动作开头播放；
- 不改变本地命令拥有者或商店。

## 13. 终局结果和 hash

每台机器在每个本地 Battle 终止后：

- 生成 M7 `BattleResolution`。
- 生成最后最多 20 Tick 的 FinalSecondHash。

Guest：

- 上报每个 BattleId/InputHash/FinalSecondHash。
- 不上报或覆盖 Match settlement。

Host：

- 使用自己的 BattleResolution 作为 M4 权威输入。
- 验证 Outcome 与双方 LifeDamage。
- 收集其他客户端 hash 仅作诊断比较。
- hash 不一致记录 Session/Round/Battle/客户端/双方 hash，但不记录私有输入。
- 第一版不纠错、不踢人、不重算、不终止。

## 14. 回合播放完成与结算

Host 只有在：

- 所有本地 Battle Producer 终止；
- 全局表现 Tick 到达 GlobalRoundEndTick；
- 当前可见终局死亡表现按既有规则完成；

后，才向 M4 提交 `PlaybackCompleted + BattleResolution set`。

M4 原子结算后：

- 收到新 Preparation snapshot：清理旧战斗 view/producer，显示新回合 HUD。
- 收到 Ended：清理并回主界面。

Guest 不自行推断阶段结束；即使本地 Track 已播完，也等待房主 snapshot。

终局隐藏 Battle 的死亡动画不要求全部播放；只等待 host 当前可见终局表现，遵循现有 Presentation 完成契约。

## 15. 重连集成

### 15.1 Preparation 重连

- 应用 M6 scoped snapshot。
- 使用 host Deadline 恢复倒计时。
- Ready=false。
- 恢复本人当前 OwnerPrivate；AI 已改状态保留。

### 15.2 Battle 重连

收到当前 snapshot、BattleSeal 和 PlaybackClock 后：

1. 验证输入/hash。
2. 创建本轮全部本地 Producer。
3. 以无逐 Tick表现的加速预算从 Tick 0 计算到当前 host Tick。
4. 不跳过任何 Core Tick。
5. 使用本机 checkpoint 直接绑定当前 Tick。
6. 已死亡/已冲家实体不创建，不补死亡动画。
7. 当前动作允许从开头播放。
8. 随后转入 one-chunk-ahead 正常计算/播放。

如果仍未追上，显示本地同步中；不阻塞 host。

### 15.3 Ended/拒绝

- MatchEnded/SessionEnded/InvalidToken：清 credential、销毁 match runtime、回主界面。
- Host endpoint 不可达：保留 runtime 最后只读状态并持续重连，直到玩家主动返回。

## 16. 直接回主界面

没有结算界面。

收到 MatchEnded 或 host local Ended effect：

1. 停止/释放 Battle producer 和 view。
2. 取消准备/拖动/商店 pending UI。
3. 停止 MatchAI（host）。
4. 关闭 M6 session。
5. 清 reconnect credential。
6. 恢复主界面和 LAN discovery。
7. 不自动建立重赛房间。

Host NoContest 也走同一清理，但不显示赢家。

## 17. 旧 Demo 隔离

- 保留需要继续回归的本地 Demo 测试入口。
- 正式 LAN path 不调用 `LocalMatchState.CreateDefault()`。
- 正式 LAN path 不使用固定 `MatchAB/MatchCD` 伪造配对。
- 正式 LAN path 不在开始时关闭 TCP。
- 正式 LAN path 不在播放前 `RunToCompletion()` 全部战斗。
- 离线 Demo 的 200 Gold、99 Cost、临时升级费不泄漏到 Match。
- 使用明确 mode/fixture factory，不能靠场景中是否恰好找到某 MonoBehaviour 猜模式。

## 18. 场景和序列化资源

只有集成确实需要时才修改场景/Prefab：

- 先检查现有 runtime bootstrap 能否通过代码接线。
- 必须修改时，只由本 M8 worktree 执行。
- 保持 `.meta`/GUID。
- 检查 YAML diff、引用和对象生命周期。
- 不移动无关 UI，不更换美术，不修改已冻结视觉几何。
- 不与其他 Unity Editor 同时打开同一路径。

## 19. 明确不在 M8 范围

不得实现：

- 新游戏机制或数值再平衡；
- 牌库外赠送 UnitId；
- hash mismatch 处理；
- host migration；
- 专用服务器/Internet relay；
- 反作弊；
- 正式暂停、倍速、Replay；
- 结算界面；
- 重赛房间；
- Bot 标记；
- 新美术或无关 UI 重构；
- Unity/Package 升级；
- 第三方网络/序列化依赖。

## 20. 自动化测试

### 20.1 组合根

- Host 和 Guest 构造不同依赖集合。
- Guest 无 MatchAuthority。
- Host HUD 不读 HostOnly。
- 重复 start 不创建双实例。
- scene reload/handoff 不关闭有效 session。

### 20.2 HUD 权限

- Shop/Gold/Cost/Level 始终本地本人。
- 观察切换只改变 Public formation/staging。
- 伪造其他 Owner 数据不会进入 view。
- 淘汰 spectator 不显示 OwnerPrivate。
- AI 无标记。

### 20.3 命令

- Host/Guest 相同 UI 操作走相同 authority queue。
- Ack 前不提交本地权威状态。
- 拒绝恢复 snapshot。
- 观察切换不改变 command PlayerId。
- Ready 锁阵型但商店可用。

### 20.4 Battle 集成

- 4-player fixture 创建两场 Official。
- 3-player fixture 创建 Official+Shadow，观察映射正确。
- 2-player fixture 创建一场且 H/A 交替来自 M4。
- 所有本地 Producer 首块后才 ready。
- 10 秒慢 guest 不阻塞。
- 慢 guest 不变 AI。
- Host 首块失败 NoContest。
- 正常不传 host BattleChunk。
- one-chunk-ahead 和 underflow 本地暂停。
- M4 收到 host BattleResolution 后结果一致。

### 20.5 Hash

- 相同 seal 多客户端 hash 相同。
- 人工不同 hash 只记录诊断，不改变 settlement。
- 旧轮/hash/input 不匹配消息拒绝。

### 20.6 重连

- Preparation 恢复 Owner/Deadline/Ready=false。
- Battle 从 Tick0 本地加速计算到 host 当前 Tick，画面不从头播放。
- 死亡单位不创建。
- 接管后恢复不回滚。
- endpoint 不可达保持重连。
- Ended 清理。

### 20.7 回合循环

用虚拟时钟/确定 Battle fixture 完成：

```text
Lobby Start
→ Round1 Preparation
→ Ready/Seal
→ Battle first chunk/start/play
→ Settlement
→ Round2 Preparation
```

精确断言两台客户端：

- 同 StateRevision；
- 同 Public；
- 各自只有自己的 OwnerPrivate；
- 同 Round/Phase/Life；
- 商店具体 UnitId 与房主 snapshot 一致；
- BattleInput/hash 一致。

### 20.8 结束

- 唯一/共享第一名 Ended 后直接主界面。
- NoContest 不伪造赢家。
- 无结算界面。
- session/listener/task 正确释放。
- room code/token 失效。
- discovery 恢复。

## 21. 端到端验证矩阵

按风险至少验证：

1. 1 Human host + 3 NativeBot；
2. 2 Human（host+guest）+ 2 NativeBot；
3. 4 Human loopback clients；
4. 4→3 淘汰后 Official+Shadow；
5. 3→2 后主客交替；
6. Preparation 掉线、下一 Prep 接管；
7. Battle 掉线、隔一完整 Prep 接管；
8. AI 接管后重连；
9. Battle 中途重连；
10. Guest explicit quit；
11. Host explicit quit/NoContest；
12. Competitive Ended。

优先使用：

- 纯领域 EditMode；
- loopback socket integration；
- PlayMode runtime/HUD；
- 最后再做两个独立 Player 进程的真实 LAN 冒烟。

若不能自动化多进程，提供可重复的手工步骤和结构化日志，但不能把未执行写成通过。

## 22. 性能与稳定性验收

- 20 TPS Core 不受渲染帧影响。
- 每场最多 1800 Tick。
- 多 Battle 稳定轮转，无一场独占。
- 正常至少一块 ahead。
- 权威 actor 不被 socket write 或慢 client 阻塞。
- Full scoped snapshot 在 M6 大小上限内；记录最大实测 bytes。
- 不出现重复 UnitId、重复结算、重复 revision 或双 runtime。
- 日志无 reconnect token。
- 结束后无 listener/task/producer/view 泄漏。

本轮先验证正确性和稳定性，不做无证据的微优化。

## 23. 验证顺序

1. 每合入一个 M commit 后 `git diff --check` 和 focused tests。
2. Match/MatchAI/Lobby/Battle 全部 C# 编译。
3. M1—M7 focused EditMode。
4. Lobby/Session loopback integration。
5. Battle streaming EditMode。
6. Match round integration EditMode。
7. HUD/scene PlayMode。
8. 全量 EditMode。
9. 全量 PlayMode。
10. 目标平台 Development Build。
11. 至少 2 个独立 Player 进程 LAN 冒烟，若环境支持。
12. 最终 diff/日志/序列化资源独立审查。

每次记录：

- 完整命令；
- 退出码；
- 测试数量；
- 失败/跳过/inconclusive；
- XML 和日志路径；
- Build 路径；
- 冒烟步骤和结果。

测试数 0、许可证失败、项目占用、超时、结果缺失均为未验证。

## 24. 完成交付

只有以下全部满足才能声明完整：

- SPEC 相关验收逐项核对；
- Unity 无新增编译错误；
- M1—M7 回归通过；
- M8 集成测试通过；
- 目标平台构建成功，或明确标记未验证；
- 至少一条真实 host+guest LAN 流程成功，或明确标记未验证；
- 日志无新增相关异常/敏感 token；
- 最终 diff 无无关修改；
- 必要文档同步。

最终提交一个或多个按集成阶段聚焦、可审查的 commit，并交付：

1. 分支和所有 commit SHA；
2. 集成的 M1—M7/单位能力基线 SHA；
3. 修改文件；
4. 最终运行时架构；
5. 测试/构建/冒烟证据；
6. 未验证项；
7. 剩余风险；
8. 人工复核步骤。

停止条件：

- 缺少任一主 Planner 指定 M1—M7 基线；
- 单位/能力目录无法产生兼容 hash 或 BattleInput；
- 合并冲突会覆盖用户修改或另一个 Agent 拥有的场景/Prefab；
- 出现新的玩家可见规则冲突；
- 需要升级 Unity/Package、安装未批准依赖或获取额外权限；
- 连续三次有实质差异的尝试仍无法通过同一阻塞测试。

遇到停止条件时保留证据并报告，不得退回本地 Demo、关闭 TCP后伪装联网、广播完整 HostState、信任房主 BattleChunk 或降低断言。
