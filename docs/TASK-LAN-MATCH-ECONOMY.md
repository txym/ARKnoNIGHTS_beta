# M2：共享牌库、商店、经济与升级 Agent 提示词

> 状态：下一可启动里程碑；必须基于已包含 M1 收口提交 `c615a75` 的 `txym`，在独立 worktree 中实施。
> 本文件可直接作为实现 Agent 的任务提示词。本轮只覆盖 M2；自动合成与 Overflow 由 M3 实现，阶段结算由 M4 编排。

## 1. 任务角色与目标

你负责在 M1 已建立的纯 C# `ARKnoNIGHTS.Match` 房主权威领域层中，实现完整对局所需的：

1. 开局共享实体牌库；
2. 开局统一生成的持久 `UnitId`；
3. 六槽玩家商店；
4. 初始自然刷新、战后自然刷新和主动刷新；
5. 冻结与解除冻结；
6. 商店购买；
7. 玩家等级与升级费用减免；
8. 与 M1 命令幂等、原子事务和权限投影的集成；
9. 可供 M3、M4、M5 和 M6 使用的稳定领域 API。

本任务不实现自动合成、Overflow 获取流程、部署、回合收入、连胜连败、战斗结算、AI 决策、LAN 协议或 UI。

## 2. 分支、基线与工作方式

- 使用独立 worktree。
- 分支名使用 `codex/lan-match-economy`。
- 必须基于包含 M1 收口提交 `c615a75` 的当前 `txym` 创建；开始前记录实际分支点 SHA。
- 不在主工作区直接实现。
- 不创建子 Agent，除非主 Planner 明确授权。
- 不合并回主分支；完成后把 commit SHA、最终 diff 摘要和验证证据交给主 Planner。

开始前：

1. 阅读仓库根目录 `AGENTS.md`。
2. 完整阅读：
   - `docs/SPEC.md`
   - `docs/ARCHITECTURE.md`
   - `docs/TEST_PLAN.md`
   - `docs/LAN-MATCH-DESIGN.md`
   - `docs/LAN-MATCH-IMPLEMENTATION-PLAN.md`
   - `docs/TASK-LAN-MATCH-DOMAIN.md`
   - `docs/numerical_architecture/2026-07-28_player_level_shop_odds_and_pool_refit_design.md`
   - `docs/numerical_architecture/2026-07-28_match_economy_upgrade_and_streak_design.md`
3. 阅读 M1 实际实现、公开 API、asmdef 和全部 Match EditMode 测试。
4. 检查：
   - `Assets/Game/Runtime/Data/Player/LocalMatchState.cs`
   - `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs`
   - 当前商店、单位目录和升级相关代码及测试
5. 执行 `git status --short`，保护 worktree 内已有修改。
6. 确认没有 Unity 进程占用该 worktree。
7. 运行 M1 Match EditMode 测试作为基线。

本文的完整对局规则优先于 `LocalMatchState` 的本地 Demo 临时行为。不要把 Demo 的 `200` 初始赤金、临时升级费用、无共享牌库商店或购买时生成 ID 带入 M2。

## 3. 架构边界

继续保持 `ARKnoNIGHTS.Match` 为纯 C# 领域层：

- 优先保持 `noEngineReferences: true`。
- 不依赖 `UnityEngine`、`Resources`、`MonoBehaviour`、Socket、Lobby、场景或 UI。
- 不从 Resources 或文件系统加载单位目录。
- 不引用或改造 `LocalMatchState` 作为权威状态。
- 外层负责把已经验证的商品目录投影为强类型初始化输入。
- M2 的所有可变状态仍只能由 M1 的 `MatchAuthority` 修改。
- 玩家命令必须继续经过 M1 的 `MatchCommandEnvelope`、权限检查、语义前置条件、幂等缓存和原子提交。
- Host 内部自然刷新也必须作为单一原子事务执行；不能逐个玩家发布中间状态。

可以在 M1 目录结构内增加例如：

```text
Assets/Game/Runtime/Match/
  Catalog/
  Economy/
  Pool/
  Shop/
```

测试继续放在 M1 的 Match EditMode 测试程序集内，或增加引用关系清晰的纯 C# EditMode 测试程序集。新增 `Assets/` 文件必须带匹配 `.meta`。

## 4. 商品目录输入

M2 不读取真实资源，而是接收由上层构造、经过验证的不可变目录：

```text
MatchShopCatalog
  RulesVersion
  Entries[]

MatchShopCatalogEntry
  TypeId
  Rarity
  IsShopEligible
  MaxEliteLevel
  BaseDeploymentCost
```

最低要求：

- `TypeId` 非空且全目录唯一。
- `Rarity` 只允许 `1..6`。
- `BaseDeploymentCost` 非负。
- `MaxEliteLevel` 只允许 `0..3`；M2 仅保存，M3 使用。
- 只有 `IsShopEligible=true` 的 TypeId 进入共享牌库。
- 不硬编码当前 94 个商品 TypeId、排序或总池大小。
- 不把未进入商店的能力召唤单位放入共享牌库。
- 目录输入必须能生成稳定 CanonicalSummary，并参与 M1 已有兼容性/不变量检查。
- 若上层传入目录与 `CompatibilityManifest.UnitCatalogSha256` 的对应关系需要适配，只新增清晰的验证接口；不要在 Match 层扫描或重新计算 Unity 资源目录。

M2 初始化失败必须是显式结构化结果，不能创建半成品 Match。至少拒绝空商品目录、重复 TypeId、非法稀有度、非法最大精英等级和非法部署费用。

## 5. 共享实体牌库

### 5.1 每个 TypeId 的副本数

按稀有度为每个可购买 TypeId 建立：

| 稀有度 | 每个 TypeId 的具体副本数 |
|---:|---:|
| 1 | 28 |
| 2 | 24 |
| 3 | 14 |
| 4 | 10 |
| 5 | 8 |
| 6 | 6 |

牌库数量不限制精英化。M2 不根据 `MaxEliteLevel` 改变池数量。

### 5.2 开局生成全部 UnitId

- 所有池内卡的 `UnitId` 必须在 Match 初始化事务中一次性生成。
- 商店刷新只移动或引用这些既有实体，绝不能临时生成新 `UnitId`。
- 每个 ID 在整个 Match Session 内唯一、非空、稳定。
- ID 生成只使用稳定输入，例如 SessionId、TypeId、该 TypeId 内的副本序号和明确版本化的算法。
- 规范编码必须无拼接歧义；使用长度前缀、固定字段编码或等价方案。
- 不得使用系统时间、进程随机种子、`Guid.NewGuid()`、对象地址、默认 `GetHashCode()` 或当前文化区域。
- 不要求 ID 不可预测；要求跨相同输入可复现且不碰撞。
- ID 字符串/值类型格式由实现确定，但必须有格式验证和测试。

### 5.3 牌库占用状态

每个池实体在任意时刻恰好位于一个权威位置：

```text
AvailablePool
ShopOffer(playerId, slotIndex)
OwnedUnit(playerId, unitId)
```

M2 不增加 `ConsumedByFusion` 位置；M3 会把被合成的 UnitId 从局内永久删除并扩展不变量。

规则：

- 抽入商店的卡暂时离开可用池。
- 未购买并在刷新时被替换的卡先返回可用池。
- 冻结商品继续留在原商店槽，不返池。
- 购买后永久进入玩家持有状态，不返池。
- 玩家淘汰后，其持有单位和商店商品都不返池。
- 当前没有出售单位，不增加出售或返池接口。
- 牌库外赠送持久单位明确暂缓，不实现临时 ID 规则。
- HostOnly 投影包含共享牌库实体/计数；Public 和其他玩家的 OwnerPrivate 投影不得泄露牌库余量。

## 6. 确定性随机

实现一个领域层内部、算法版本固定的确定性伪随机数发生器：

- 由 M1 的 `MatchSeed` 和明确的用途域分隔初始化。
- 算法必须在代码注释或类型名中固定版本。
- 相同目录、相同种子和相同命令/事务顺序必须得到相同结果。
- 不使用 `System.Random`、Unity Random、浮点概率或平台相关哈希。
- 抽样使用整数权重，并避免取模偏差；若使用 rejection sampling，应有边界和测试。
- 随机状态是 HostOnly 权威状态的一部分，必须随事务原子提交；失败事务不能消耗随机数。
- CanonicalSummary 至少能表示算法版本和足以复现后续抽取的随机状态，但 Owner/Public 不得获得该状态。

建议为不同用途使用稳定域标识，例如池 ID 生成、商店抽样；不要因为未来增加无关随机调用就静默改变既有商店序列。

## 7. 商店结构与排序

每名未淘汰玩家拥有固定六槽商店，槽位固定为 `1..6`：

```text
MatchShopOfferState
  SlotIndex
  UnitId?
  TypeId?
  Rarity?
  IsFrozen
```

要求：

- 空槽的 UnitId/TypeId/Rarity 为空，`IsFrozen=false`。
- 非空槽必须引用一张唯一池实体，TypeId/Rarity 与目录一致。
- 同一商店允许重复 TypeId，但每格必须是不同 UnitId。
- 一张池实体不能同时出现在两个槽、两个玩家、牌库和玩家持有单位中。
- 新抽出的商品先按“稀有度升序、十进制数值 TypeId 升序”稳定排序，再按可刷新槽的 `SlotIndex` 升序填入。
- 若 TypeId 不是直接存为十进制整数，目录适配时必须提供规范数值排序键；不要使用当前文化字符串排序。
- 冻结商品保留原物理槽位，不参与新商品排序。
- 若共享牌库不足以填满全部目标槽，剩余目标槽保持空；不得生成池外副本或重复引用同一 UnitId。

## 8. 抽取概率

### 8.1 玩家等级稀有度权重

| 等级 | R1 | R2 | R3 | R4 | R5 | R6 |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 80 | 20 | 0 | 0 | 0 | 0 |
| 2 | 65 | 35 | 0 | 0 | 0 | 0 |
| 3 | 50 | 40 | 10 | 0 | 0 | 0 |
| 4 | 38 | 40 | 20 | 2 | 0 | 0 |
| 5 | 27 | 38 | 27 | 8 | 0 | 0 |
| 6 | 18 | 32 | 30 | 18 | 2 | 0 |
| 7 | 11 | 24 | 30 | 28 | 6 | 1 |
| 8 | 5 | 14 | 24 | 39 | 16 | 2 |
| 9 | 2 | 7 | 11 | 40 | 30 | 10 |

每行必须精确合计 100。

### 8.2 两级加权抽取

每个待填槽按以下顺序独立抽取，且前一槽的占用立即影响后一槽的剩余权重：

1. 查看当前仍至少有一张可用池实体的稀有度。
2. 在这些非空稀有度之间，按当前玩家等级对应的原始稀有度权重做条件抽取。
3. 在选中的稀有度内，以每个 TypeId 当前剩余的具体副本数作为该 TypeId 的整数权重抽取 TypeId。
4. 在该 TypeId 的可用具体实体中按稳定规则取一张 UnitId；这里不再额外消耗随机数也可以，但规则必须固定并有测试。
5. 立即把该 UnitId 从可用池移入本次事务的商店草稿。

例如同一稀有度内 A 剩 1 张、B 剩 28 张时，A:B 抽取权重为 `1:28`，不是 `1:1`。

若某稀有度原始权重为 0，即使其中仍有卡，也不能被该等级抽中。若所有“原始权重大于 0”的稀有度均已耗尽，则目标槽保持空并返回可诊断但非致命的耗尽状态；不得回退抽取当前等级权重为 0 的稀有度。

不要用“先随机抽一张全池卡，再按稀有度拒绝”的近似替代以上规则。

## 9. 三类刷新

### 9.1 开局自然刷新

Match 初始化完成共享牌库后，在同一初始化事务中：

- 为四名未淘汰玩家执行一次六槽自然刷新。
- 刷新费用为 `0`，不得扣赤金。
- 单位购买价格仍正常。
- 不触发升级费用减免。
- 使用第 10 节的公平批次算法。
- 初始化完成后不能向订阅者暴露“已有牌库但商店尚未填充”的中间状态。

### 9.2 玩家主动刷新

玩家命令建议为：

```text
RefreshShop
```

规则：

- 只允许未淘汰且当前仍由该玩家合法控制的席位。
- 允许 Preparation 和 Battle；Sealing、Settlement、Initializing、Ended 拒绝。
- 固定扣除 `1` 赤金。
- 赤金不足拒绝，不返卡、不消耗随机数、不递增 revision。
- 主动刷新与自然刷新使用同一冻结语义：冻结商品保留；空槽和未冻结槽刷新。
- 先把全部未冻结旧商品返回池，再抽取新商品。
- 由房主按命令接受顺序串行处理；共享牌库竞争结果以该顺序为准。
- 单次成功刷新作为一个原子事务，只递增一次 revision，只发布一次快照。
- 若所有可刷新槽因牌库耗尽而保持空，刷新命令仍已成功并正常扣除刷新费；诊断结果需明确区分“成功但未填满”和拒绝。

不要沿用当前 Demo“主动刷新会替换冻结商品并清除冻结”的临时行为。

### 9.3 战后自然刷新

M2 提供由 M4 在回合结算事务中调用的 Host 内部能力，但不自行判断战斗何时结束：

```text
ApplyPostBattleNaturalRefresh(roundNumber)
```

或等价的 transaction component。

规则：

- 每个完整战斗回合最多调用一次；重复 roundNumber 必须结构化拒绝或幂等 no-op，不能重复刷新或重复减费。
- 淘汰玩家排除，不返还其旧商品，也不填充其空槽。
- 全部合格玩家使用第 10 节公平批次刷新。
- 刷新费用为 `0`。
- 对结算瞬间玩家当前等级的下一次升级费用应用 `-1`，最低为 `0`。
- 等级 9 不累计可转移减免。
- 该能力必须能嵌入 M4 的更大结算事务，避免先发布收入、再发布刷新等中间 revision。
- 若 M1 当前 transaction API 无法组合，先做最小扩展，使 M4 可在一个草稿中依次应用收入、连续奖励、自然刷新和升级减费；不要在 M2 中实现收入。

## 10. 多玩家自然刷新公平批次

开局自然刷新和战后自然刷新统一使用：

1. 在同一事务草稿中，先按 SeatIndex 扫描所有本批次合格玩家。
2. 对每个合格玩家，将所有空槽和未冻结槽标为目标槽；先统一把未冻结旧商品返还共享池。
3. 冻结商品保留在原玩家原槽位。
4. 确定本回合起始席位：
   - 开局批次使用 Seat 1；
   - 此后按 `1→2→3→4→1` 轮换；
   - 淘汰玩家不参与抽取，但不能改变轮换游标本身的确定性定义；
   - 若游标席位已淘汰，从该席位开始沿升序循环寻找首个合格玩家。
5. 按 `SlotIndex=1..6` 分轮；每个槽位轮次内，从起始玩家开始按席位循环，每名合格玩家最多填该槽一次。
6. 不能先填满一个玩家的六槽再填下一名玩家。
7. 每次抽取立即更新事务草稿中的剩余池，影响下一次抽取。
8. 整批完成后一次性检查不变量、提交 revision 和发布快照。

冻结导致某玩家该 SlotIndex 不需要刷新时直接跳过该目标，但不能额外获得一次抽取。若商店采用固定槽 `1..6`，交错次序必须可由测试给出精确序列。

轮换游标应是 HostOnly 状态并进入 CanonicalSummary。开局批次之后，下一次战后自然刷新从 Seat 2 开始；之后依次轮转，不因某玩家主动刷新而改变。

## 11. 冻结命令

玩家命令建议为：

```text
ToggleShopFreeze
```

按当前 UI 已确认交互：

- 只作用于自己的全部非空商店槽。
- 若至少有一个非空槽未冻结，则把全部非空槽设为冻结。
- 只有全部非空槽均已冻结时，才统一解除全部非空槽冻结。
- 空槽始终 `IsFrozen=false`。
- 商店全空时是成功 no-op，不递增 revision。
- 冻结不扣赤金。
- 冻结不禁止购买；买走冻结商品后该槽为空并清除冻结状态。
- 允许 Preparation 和 Battle；其他阶段拒绝。
- 同一次命令只递增一次 revision。

M4 会用“第一回合真人是否曾冻结过商店”判断封存安全购买。为此必须维护由权威命令产生的、每回合重置的玩家行为事实，例如：

```text
PreparationBehavior
  SuccessfulShopPurchaseCount
  HasIssuedEffectiveFreezeThisRound
```

要求：

- 只有本回合发生实际冻结状态变化时，`HasIssuedEffectiveFreezeThisRound=true`。
- 解除冻结不会把该事实改回 false。
- 全空商店的 no-op 不计为“冻结过”。
- Battle 阶段的冻结行为不应反向改变已经封存回合的安全规则事实。
- M2 只记录事实；M4 决定何时重置、何时执行第一回合安全购买。

若 M1 的回合行为状态边界需要调整，使用强类型字段，不使用字符串字典。

## 12. 购买

玩家命令必须携带语义前置条件：

```text
PurchaseShopOffer
  SlotIndex
  ExpectedUnitId
```

验证顺序至少覆盖：

1. Session、Player、CommandId、KnownRevision 和幂等规则由 M1 处理。
2. 允许 Preparation 和 Battle；其他阶段拒绝。
3. 玩家未淘汰且仍有合法命令权限。
4. SlotIndex 在 `1..6`。
5. 目标槽非空，且当前具体 `UnitId` 与 `ExpectedUnitId` 完全相同。
6. 待部署区当前已占用槽位数严格小于 `13`。
7. 玩家赤金足以支付该商品稀有度。

成功事务：

- 商品价格等于稀有度，即 R1..R6 分别花费 `1..6` 赤金。
- 扣除赤金。
- 清空目标商店槽并清除其冻结状态。
- 把同一个具体 UnitId 作为新的精英 0 持久单位放入 `Staging`。
- 保存目录 TypeId、稳定递增的 `AcquisitionOrdinal` 和 M3 所需的基础数据引用。
- 不生成新 UnitId。
- `SuccessfulShopPurchaseCount` 加一。
- 一次提交、一次 revision、一次快照。

失败事务：

- 不扣赤金。
- 不清空商店。
- 不改变共享池。
- 不新增持有单位。
- 不改变购买计数。
- 不消耗随机数。
- 不递增 revision。

特别规则：

- 待部署区已有 `13` 槽时必须先拒绝，即使 M3 将来可能通过本次获得触发合成释放槽位。
- 商店购买永远不会进入 Overflow。
- M2 不调用自动合成；M3 会把“获得单位 + 合成”包进同一原子事务。
- Battle 阶段购买只改变持久状态，不能修改已封存的本场 BattleInput。
- M4 的第一回合封存安全购买必须复用同一购买核心，传入具体槽和 UnitId，正常扣费、占池并记录购买；不能复制一套绕过验证的购买逻辑。
- 不实现购买取消；若当前 UI 有二次确认，确认前不发送领域命令，领域层只处理最终购买命令。

### 12.1 “13 槽”的边界

M2 按 M1/M3 共用的权威 `StagingSlotUsage` 计算待部署区槽数，不得简单用全体单位数量替代未来可能存在的堆叠投影。

在 M3 尚未实现时：

- 使用一个明确、可替换且有测试的 `IStagingSlotPolicy` 或纯函数。
- 默认对当前精英 0、空 Buff 的独立持有单位按已确认待部署区堆叠规则计算。
- 不在 M2 内实现精英化或连锁合成。
- M3 接入后必须能在不改购买外部契约的情况下替换为合成后状态的完整槽位投影；但购买的满 13 校验仍基于“购买前”的当前状态。

如果现有文档不足以确定某类 Buff 对堆叠槽的影响，不要猜测；测试 M2 当前可生成的精英 0、无 Buff 商品即可，并把扩展点写清楚。

## 13. 玩家升级

玩家命令建议为：

```text
PurchaseLevelUpgrade
  ExpectedCurrentLevel
  ExpectedCurrentPrice
```

基础费用：

| 当前等级 | 升至 | 基础赤金费用 |
|---:|---:|---:|
| 1 | 2 | 4 |
| 2 | 3 | 6 |
| 3 | 4 | 9 |
| 4 | 5 | 13 |
| 5 | 6 | 18 |
| 6 | 7 | 24 |
| 7 | 8 | 31 |
| 8 | 9 | 39 |

等价公式：

```text
C(L) = 3 + L * (L + 1) / 2, L in [1,8]
CurrentPrice = max(0, C(Level) - PostBattleDiscountCountAtThisLevel)
```

规则：

- 初始等级 1，最大等级 9。
- 允许 Preparation 和 Battle；其他阶段拒绝。
- 需要未淘汰且有命令权限。
- ExpectedCurrentLevel/Price 与权威状态不符时语义 stale 拒绝。
- 赤金不足拒绝。
- 成功后扣除当前实际价格，等级加一。
- 旧等级累计减免清零；新等级从完整基础价开始。
- 旧等级多余减免不转移。
- 等级 9 拒绝继续升级。
- 升级不增加部署格、部署 Cost、单位属性或任何战斗数值。
- 成功事务只递增一次 revision。
- 费用可以为 0；0 费升级仍是状态改变并递增 revision。
- 初始自然刷新不提供减免。
- 每次战后自然刷新只对结算瞬间的当前等级减免 1。

OwnerPrivate 投影必须包含当前等级、当前升级价格和本等级已累计减免，其他玩家不可见。Public 不包含等级。

## 14. 命令结果和幂等扩展

沿用 M1 的幂等键 `(PlayerId, CommandId)`：

- 相同命令内容重发返回原完整结果，不重复扣金、刷新、冻结、购买或升级。
- 同 ID 不同内容返回 `CommandIdConflict`。
- 旧 KnownRevision 不能一律拒绝；Purchase 使用 ExpectedUnitId，Upgrade 使用 ExpectedCurrentLevel/Price 进行语义校验。
- 成功 no-op 不递增 revision。
- 拒绝不递增 revision。

在 M1 结果码基础上至少可明确表达：

```text
ShopSlotInvalid
ShopOfferMissing
ShopOfferChanged
InsufficientGold
StagingFull
LevelMax
UpgradePriceChanged
PoolExhaustedDiagnostic
NaturalRefreshAlreadyApplied
```

结果码名称可以调整，但 UI/协议必须能稳定映射，不能只返回自然语言字符串。

## 15. 权限投影

### 15.1 Public

不得增加：

- Gold
- Level/升级价格/升级减免
- 任何玩家商店
- 共享牌库余量
- 随机状态

Public 继续只公开 M1 已确认的玩家公开信息和 Deployed/Staging 单位。购买进入 Staging 后，其他玩家可以通过下一份 Public Snapshot 看见该持久单位，但看不到购买价格、商店来源或剩余赤金。

### 15.2 OwnerPrivate

拥有者可见：

- 自己的 Gold
- Level
- 当前升级价格与本等级减免
- 六个商店槽的具体 UnitId、TypeId、Rarity、冻结状态
- 自己持有的完整持久单位
- 后续 M4/M5 需要的本人经济状态

不能包含其他玩家私有信息、共享池余量或 Host 随机状态。

### 15.3 HostOnly

包含：

- 全部池实体及其当前位置，或足以完整验证位置的不变状态
- 各 TypeId 剩余副本数
- 自然刷新轮换游标
- 已应用的战后自然刷新 round 记录
- 确定性随机算法版本和当前状态
- 全部玩家完整经济状态

Host 玩家 HUD 不得直接使用 HostOnly 投影，仍使用自己的 Owner-scoped 投影。

所有投影保持不可变、稳定排序并生成稳定 CanonicalSummary。

## 16. 不变量

每次事务提交前至少验证：

- M1 全部不变量继续成立。
- 每个商品 TypeId 的总实体数等于其稀有度规定的开局副本数。
- 所有池实体 UnitId 全局唯一。
- 每个池实体恰好位于 AvailablePool、某个 ShopOffer 或某个 OwnedUnit 中之一。
- AvailablePool 内实体不出现在商店或持有单位中。
- 非空商店槽引用的 UnitId/TypeId/Rarity 与目录一致。
- 每名玩家恰好六个唯一 SlotIndex `1..6`，或投影为等价固定六槽。
- 空槽不冻结。
- 同一具体 UnitId 不出现在多个玩家/槽位。
- Gold 非负。
- Level 在 `1..9`。
- 升级减免非负且不超过当前基础费用；Level 9 无可转移减免。
- `AcquisitionOrdinal` 在全 Match 内按既定作用域稳定且不重复。
- StagingSlotUsage 不超过 13；购买前满 13 不允许事务进入成功草稿。
- 淘汰玩家不参与自然刷新，但其商店与持有卡仍占用原实体。
- 失败事务后的完整 CanonicalSummary、随机状态和 revision 与失败前相同。

不要靠“发现重复后删一个”修复不变量；应拒绝导致非法状态的事务。

## 17. 为后续里程碑保留的 API

### M3 自动合成

M3 需要把成功获得单位后的：

```text
AddOwnedUnitToStaging
ResolveAcquisitionFusion
ResolveOverflow
Commit
```

组合为一个事务。M2 不要把“扣金和加入 Staging”封装为无法插入合成步骤的私有黑箱；应提供只在 Authority transaction draft 内可用的强类型步骤，同时不暴露任意外部改状态入口。

### M4 阶段结算

M4 需要在一个结算事务中组合：

```text
ApplyGoldAndCostIncome
ApplyStreakOrDrawBonus
ApplyPostBattleNaturalRefresh
ApplyUpgradeDiscount
ResetRoundBehaviorFacts
EnterNextPreparation
```

M2 自己不发收入，但自然刷新与减免必须可嵌入同一个 revision。

### M5 AI

AI 只调用与真人相同的 Refresh/Purchase/Upgrade 领域命令核心，不得直接改 Gold、Pool 或 Shop。M2 提供只读经济观察，不提供对手隐藏数据。

### M6 LAN

完整快照必须能序列化，但 M2 不定义 Wire DTO 或 JSON。保持领域投影为稳定强类型，由 M6 显式映射。

## 18. 明确不在 M2 范围

不得实现或修改：

- 自动合成、连锁合成、幸存 UnitId、Buff 重映射；
- Overflow 的获取、提升或封存删除；
- 部署、撤退、换位或自动部署；
- 第一回合自动购买的触发条件和封存流程；
- 30 秒计时、Ready 提前开战、Phase 驱动；
- 基础回合赤金/Cost 收入；
- 连胜、连败、平局彩蛋；
- 玩家生命、淘汰、排名和战斗结算；
- 配对、主客场、影子战斗；
- AI 状态机；
- LAN Socket、协议、重连和 token；
- BattleInput、Battle Core、流式分块和哈希；
- 牌库外赠送持久单位；
- 出售单位；
- 地区羁绊改变商店权重；
- `LocalMatchState`、当前本地 Demo UI、场景、Prefab 的联网改造；
- Unity Editor、Package 或资源版本升级。

若为了扩展 M1 数据结构必须修改 M1 文件，只做 M2 所需最小改动，并保持 M1 测试通过。

## 19. TDD 与测试要求

先写失败测试，再实现最小代码。至少覆盖：

### 19.1 目录和初始化

- 非法目录输入逐项拒绝。
- 只为 IsShopEligible TypeId 建池。
- R1..R6 每 TypeId 分别生成 28/24/14/10/8/6 个唯一 UnitId。
- 相同 SessionId、MatchSeed、目录得到相同 UnitId 集和初始商店。
- 改变 MatchSeed 不改变池实体集合的 TypeId/数量，但改变可观察抽取序列；若 ID 设计也包含 seed，测试明确记录该契约。
- 初始化一次提交得到四个非空六槽商店；牌库不足时按规则留空。
- 初始刷新不扣初始 7 赤金，不增加升级减免。
- 初始化订阅者看不到半初始化状态。

### 19.2 抽取

- 九级概率表每行合计 100，且只抽到权重大于 0 的稀有度。
- 某稀有度耗尽后在仍非空且权重大于 0 的稀有度中条件重抽。
- 当前等级允许的全部稀有度耗尽时目标槽保持空。
- 同稀有度 A 剩 1、B 剩 28 的确定性统计/边界测试证明权重输入为 1 和 28，不是等权。
- 每抽一张后剩余权重立即减少。
- 同 TypeId 重复出现时 UnitId 不同。
- 新抽商品按稀有度、数值 TypeId 稳定排序填入可刷新槽。
- 固定 PRNG golden vector：给定种子后的前若干整数输出必须精确一致。
- 大权重、零权重和 rejection sampling 边界无溢出、无死循环。

### 19.3 牌库位置

- 抽入商店从可用池移除。
- 刷掉未冻结商品先返池。
- 冻结商品不返池。
- 购买后转为同 ID 的 OwnedUnit。
- 玩家淘汰后商店和持有卡均不返池。
- 任何时刻全池实体守恒且位置唯一。
- Public/其他 OwnerPrivate 不包含共享池数量或随机状态。

### 19.4 主动刷新和冻结

- 主动刷新正常扣 1 赤金。
- 赤金不足完全原子失败，随机状态不前进。
- Preparation/Battle 可刷新，其余阶段拒绝。
- 主动刷新保留冻结槽，刷新未冻结/空槽。
- ToggleFreeze 的“任一未冻结则全冻、全冻才全解”规则。
- 全空冻结命令成功 no-op。
- 冻结商品仍可购买，买后空槽不冻结。
- 相同 CommandId 重发不重复扣金或刷新。
- 两名玩家按不同命令接受顺序竞争最后一张卡时结果严格服从房主顺序。

### 19.5 公平自然刷新

- 开局从 Seat 1 开始，按 SlotIndex 在玩家间交错抽取。
- 下一战后批次从 Seat 2 开始，之后轮转。
- 冻结目标被跳过但不改变其他玩家同槽的相对次序。
- 淘汰玩家排除，轮换游标落在淘汰席位时正确寻找下个合格玩家。
- 统一返卡发生在任何新抽取之前。
- 整批只递增一次 revision、只发布一次快照。
- 重复应用相同战后 round 不会二次刷新或二次减免。
- 牌库不足时确定性地填到耗尽并留下其余空槽。

### 19.6 购买

- 价格严格等于稀有度。
- ExpectedUnitId 正确时购买成功，保留同 UnitId。
- ExpectedUnitId 已改变时 stale 拒绝。
- 赤金不足、空槽、非法槽、错误阶段、淘汰、无权限分别拒绝。
- 购买冻结商品成功并清空冻结。
- Staging 为 12 槽时可买，买后 13；已为 13 时拒绝。
- 满 13 即使购入同 TypeId 将来可合成也拒绝。
- Battle 阶段购买不改变当前 SealedBattleInput。
- 失败购买不改变 Gold、Pool、Shop、Units、行为计数、随机状态或 revision。
- 相同 CommandId 重发不重复扣金或获得单位。
- 同 ID 不同 ExpectedUnitId 返回冲突。

### 19.7 升级

- 基础费用精确为 `4,6,9,13,18,24,31,39`。
- 战后刷新每次减 1，最低 0。
- 初始刷新不减费。
- 升级后旧折扣清空，新等级为全价。
- 若玩家在 Battle 中升级，随后该场战后减费作用于新等级。
- 0 费升级成功并递增 revision。
- Level 9 拒绝继续升级且不累计折扣。
- 余额不足、ExpectedLevel/Price stale、错误阶段、淘汰和权限错误分别拒绝。
- 升级不修改部署 Cost、格数或单位属性。
- 幂等重发不重复扣金或升两级。

### 19.8 投影、摘要与原子性

- Owner 只看到自己的商店与经济。
- 另一玩家、淘汰旁观者和 Public 看不到私有经济。
- HostOnly 能完整核对池实体守恒。
- 同 revision 的投影 CanonicalSummary 稳定。
- 目录输入顺序打乱不改变规范结果。
- 所有拒绝路径保持完整权威摘要和随机状态不变。
- M1 全部既有测试继续通过。

测试不能只断言“非空”或“数量大概正确”；关键顺序、具体 ID、revision、余额、池计数和结果码应做精确断言。

## 20. 验证顺序

按仓库规范执行并记录：

1. `git diff --check`
2. 审查最终 diff 和所有新增 `.meta`
3. Match 相关 C# 编译
4. M1 既有 Match EditMode 测试
5. M2 新增 EditMode 测试
6. 若项目脚本支持，运行相关完整 EditMode 测试集
7. 检查 Unity 日志，没有与本次修改相关的新错误或未处理异常
8. 独立审查：
   - 随机状态是否在失败事务中前进
   - 同一 UnitId 是否可能重复占位
   - 快照是否泄露其他玩家商店或池余量
   - 自然刷新是否真的先统一返卡再交错抽取
   - 冻结商品是否保持具体 ID 和物理槽
   - M3/M4 是否仍能组合进同一事务

若 Unity 被其他 worktree 占用、许可证失败、测试数为 0、结果 XML 缺失或超时，必须标记为未验证，不能记为通过。

## 21. 完成交付

最终提交前：

- 更新实际受影响的架构/测试文档；不要改写与 M2 无关的 SPEC。
- 不提交 Library、Temp、Logs、Build、Artifacts 或用户无关修改。
- 创建一个聚焦 M2 的 commit。

交付给主 Planner：

1. 分支名和 commit SHA；
2. 基于哪个 M1 commit；
3. 修改文件列表；
4. 关键领域 API 与 M3/M4 接入点；
5. 实际测试命令、退出码、测试数量和结果文件；
6. 未验证项；
7. 已知风险，尤其是目录适配、随机算法和事务组合；
8. 明确声明没有实现 M3—M8 的内容。

停止条件：

- M1 基线不是主 Planner 指定版本；
- M1 实现无法在不破坏公开契约的情况下支持原子组合事务；
- 商品目录的 TypeId、稀有度、ShopEligible 或排序键无法从现有权威数据可靠适配；
- SPEC 与本提示词出现新的玩家可见规则冲突；
- 需要升级 Unity/Package 或安装未批准依赖；
- 连续三次有实质差异的尝试仍无法通过同一阻塞测试。

出现停止条件时保留证据并向主 Planner报告，不得用本地 Demo 临时行为或硬编码绕过。
