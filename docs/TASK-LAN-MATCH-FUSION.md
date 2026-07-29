# M3：自动合成与 Overflow Agent 提示词

> 状态：待在独立 worktree 中实施。
> 本文件可直接作为实现 Agent 的任务提示词。本轮只覆盖 M3，并基于 M1、M2 的权威领域层继续工作。

## 1. 任务角色与目标

你负责在纯 C# `ARKnoNIGHTS.Match` 房主权威领域层中实现：

1. 持久单位获得后的自动合成；
2. 同 TypeId、同当前精英等级的确定性连锁升阶；
3. 幸存实例和被消耗 UnitId 的永久生命周期；
4. 玩家层定向 Buff 引用重映射；
5. 已部署幸存实例升阶后的部署 Cost 调整和必要退场；
6. Staging 严格堆叠槽计算；
7. Overflow 暂存、参与合成、按获取顺序提升；
8. 准备阶段封存时永久删除剩余 Overflow；
9. 与 M2 购买事务、M4 封存事务的原子组合接口；
10. 权限投影、规范摘要和完整不变量。

本任务不实现单位出售、牌库外赠送 ID 生成、玩家主动精英化、部署操作 UI、战斗实体生成、回合流程、AI、LAN 或表现。

## 2. 分支、基线与工作方式

- 使用独立 worktree。
- 分支名使用 `codex/lan-match-fusion`。
- 必须基于主 Planner 指定的 M2 commit 创建，并记录 M1、M2 实际基线 SHA。
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
3. 阅读 M1、M2 实际实现、公开 API、asmdef 和全部 Match EditMode 测试。
4. 检查现有玩家单位、Buff、部署费用和待部署堆叠代码；只作为适配参考，不把本地 Demo 状态改成权威层。
5. 执行 `git status --short`，保护 worktree 内已有修改。
6. 确认没有 Unity 进程占用该 worktree。
7. 运行 M1、M2 Match EditMode 测试作为基线。

若实际 M2 commit 未提供可组合的 transaction draft、具体 Pool UnitId 所有权或 Staging 槽策略，先报告接口缺口；只做支持 M3 的最小扩展，不另造第二套 MatchState。

## 3. 架构边界

继续保持：

- `ARKnoNIGHTS.Match` 为纯 C# 领域层，优先 `noEngineReferences: true`。
- 不依赖 `UnityEngine`、Resources、MonoBehaviour、Socket、场景或 UI。
- 所有可变状态只有 `MatchAuthority` 可以提交。
- 玩家购买继续经过 M1/M2 的命令、幂等、权限和原子事务。
- M3 不能新增绕过 `MatchAuthority` 的 `SetEliteLevel`、`DeleteUnit`、`MoveToOverflow` 等公开可变入口。
- 自动合成是成功“获得持久单位”事务中的领域步骤，不是独立玩家命令。
- 封存删除 Overflow 是 Host 内部事务步骤，由 M4 调用。
- 失败事务不能部分删除 UnitId、改变 Buff 引用、消耗随机数、扣金或修改 Cost。

建议在现有 Match 目录增加：

```text
Assets/Game/Runtime/Match/
  Units/
    Fusion/
    Staging/
    Overflow/
    Buffs/
```

实际目录遵循 M1/M2 风格。新增 `Assets/` 文件必须带 `.meta`。

## 4. 精英等级与派生数值

持久单位当前精英等级只允许 `0..3`。

默认基础副本等价和战斗实体数量：

| 精英等级 | 等价基础副本 | 战斗实体数 | 默认部署 Cost 倍率 |
|---:|---:|---:|---:|
| 0 | 1 | 1 | 1 |
| 1 | 2 | 2 | 2 |
| 2 | 4 | 3 | 3 |
| 3 | 8 | 5 | 5 |

要求：

- 每次合成恰好把两个相同 TypeId、相同当前精英等级 `E` 的持久单位合为一个 `E+1` 单位。
- 连锁合成自然得到 `1/2/4/8` 的基础副本口径。
- 每个 TypeId 的 `MaxEliteLevel` 来自 M2 的权威商品/单位目录，允许 `0..3`。
- 当前等级已达到 `MaxEliteLevel` 时不再合成。
- `MaxEliteLevel=0` 表示该单位不能合成。
- 牌库副本数不改变 MaxEliteLevel。
- 缺少精二/精三专用模型或属性资源不禁止升阶；表现和 Battle adapter 后续按已有目录继承规则解析。
- M3 不生成战斗实体，但应提供纯只读派生函数或稳定契约，使后续 BattleInput adapter 能由 EliteLevel 得到 `1/2/3/5` 实体数。

部署费用：

- 当前基线使用精英 0 `BaseDeploymentCost` 乘以 `1/2/3/5` 得到对应等级的持久部署费用。
- 若现有权威目录已经提供明确的逐精英部署费用解析器，应通过强类型只读接口使用；不得同时保存一份可能漂移的重复费用。
- 不自行读取 v2 JSON。
- 所有乘法使用受检整数；溢出、负数或目录不一致必须拒绝初始化/事务。

## 5. 自动合成触发点

自动合成只由“获得一个新的持久单位”触发。

当前正式触发来源：

- M2 商店购买成功，并取得商店里既有的具体 Pool UnitId。

未来能力来源：

- M3 可以提供接收“上层已经合法创建并授权的具体持久单位记录”的内部 transaction component。
- 本任务不得定义牌库外 UnitId 的生成、同步、去重来源或玩家命令。
- 在未来 ID 规则完成前，不把该内部 component 暴露成可由网络或 UI 任意调用的入口。

以下行为不触发新的自动合成扫描：

- 取消准备；
- 单纯从 Overflow 提升到 Staging；
- 部署、撤退或换位；
- 冻结、刷新或升级玩家等级；
- 恢复/加载权威快照；
- 进入下一阶段；
- 封存删除 Overflow；
- 战斗结束写回；
- 仅仅修改 Buff 引用。

同一次获得事务内的合成可以并且必须连锁，直到当前候选区域不存在可继续提升的一对单位。

## 6. 获得事务

建议把 M2 的购买核心重构/扩展为可组合的内部流程：

```text
BeginAuthorityTransaction
  ValidatePurchaseAndReserveOffer
  DeductGold
  PlaceAcquiredPersistentUnit
  ResolveFusionCascadeForAcquisition
  PromoteEligibleOverflowInAcquisitionOrder
  ValidateInvariants
CommitOnce
```

外部仍只看到一个 Purchase 命令结果、一次 revision 和一次快照。

### 6.1 初始落点

- 商店购买继续遵守 M2“购买前 Staging 已满 13 槽即拒绝”，所以正常成功购买的初始落点为 Staging。
- 未来非商店能力获得时：
  - 若加入后 Staging 槽数不超过 13，则落入 Staging；
  - 否则落入 Overflow。
- 进入 Overflow 不代表获取失败，也不占 Staging 槽。
- 新持久单位取得全 Match 稳定递增的 `AcquisitionOrdinal`。
- 从商店取得的单位保留原具体 UnitId；M3 不生成替代 ID。

### 6.2 原子失败

任一验证、合成、Buff 重映射、Cost 或不变量步骤失败时，整个获得事务回滚到开始前：

- 商店商品仍在原槽；
- Gold 不变；
- Pool/Owned/Consumed 位置不变；
- 所有单位 Elite/Zone/Formation 不变；
- Available/Total Cost 不变；
- Overflow 不变；
- Buff 引用不变；
- AcquisitionOrdinal 计数不前进；
- revision、通知和 CanonicalSummary 不变。

不能使用“先提交购买，再提交合成”的两个 revision。

## 7. 候选区域

只扫描获得单位所属玩家，且只扫描与本次获得可能相关的持久单位。候选区域由事务发生时的权威 Phase/Ready 决定：

| 权威状态 | 合成候选区域 |
|---|---|
| Preparation 且玩家 `Ready=false` | Deployed + Staging + Overflow |
| Preparation 且玩家 `Ready=true` | Staging + Overflow |
| Battle | Staging + Overflow |

要求：

- 已准备玩家的 Deployed 阵型不能因战斗前商店购买而变化。
- Battle 阶段的 Deployed 单位属于已经封存阵型，不参与新购买的合成。
- Sealing、Settlement、Initializing、Ended 不接受普通获得/购买命令。
- 只比较同一玩家的单位。
- 未购买的 ShopOffer 永不参与合成。
- 已被永久消费的 UnitId 永不参与。
- 不扫描 Battle Core 内临时实体或召唤物。

## 8. 合成判定与确定性算法

两个候选可以合成，当且仅当：

- `TypeId` 完全相同；
- 当前 `EliteLevel` 完全相同；
- `EliteLevel < MaxEliteLevel`。

Buff 不需要相同，Zone 不需要相同。

为避免容器顺序影响结果，每一步：

1. 找出所有当前可合成的 `(TypeId, EliteLevel)` 组。
2. 只处理与本次获得引起的连锁闭包相关的 TypeId；一次获得只有一个 TypeId，不需要无关全玩家扫描。
3. 对候选按第 9 节幸存优先级稳定排序。
4. 取排序最前的两个单位组成一对。
5. 第一个为幸存实例，第二个被消费。
6. 幸存实例 EliteLevel 加一；保留同一 UnitId 和 AcquisitionOrdinal。
7. 原子重映射被消费 ID 的定向 Buff。
8. 更新部署 Cost/Zone。
9. 将被消费 UnitId 移入永久 tombstone。
10. 从新的当前状态继续处理相同 TypeId，直到没有可合成对。

若一次获得触发多个同等级配对，以上循环必须得到唯一确定结果。例如四个 E0 会形成两个 E1，再由两个 E1 形成一个 E2；不能依赖 Dictionary/List 当前插入顺序。

建议 CanonicalSummary 和测试记录每一步：

```text
FusionStep
  TypeId
  FromEliteLevel
  ToEliteLevel
  SurvivorUnitId
  ConsumedUnitId
  SurvivorZoneAfterStep
```

FusionStep 可以只存在于命令结果/诊断，不要求永久公开保存完整历史。

## 9. 幸存实例

排序优先级固定为：

```text
Deployed > Staging > Overflow > UnitId 升序
```

解释：

- Zone 优先于 UnitId。
- 同 Zone 时使用 UnitId 的 ordinal 规范升序。
- 不使用 AcquisitionOrdinal、当前容器顺序、坐标、费用、Buff 数量或随机数决胜。
- Deployed 只有在 Preparation 且 Ready=false 时才会进入候选集。

幸存实例保留：

- UnitId；
- TypeId；
- AcquisitionOrdinal；
- 当前实例对象语义；
- 若仍 Deployed，则保留原 Formation；
- 该实例自身明确属于“幸存实例”的持久字段；
- 第 11 节重映射后的所有玩家层定向 Buff。

连锁合成的下一层继续保留同一个幸存 UnitId。不得每升一级重新选择或生成 ID。

## 10. 被消费 UnitId

被消费的 UnitId：

- 从玩家 Units 集合永久删除；
- 不返回共享牌库；
- 不返回商店；
- 不复用；
- 不允许未来能力重新创建同 ID；
- 不出现在 Public 或 Owner 的当前单位列表；
- 其玩家层定向 Buff 必须先重映射到幸存 UnitId。

M2 的池实体位置不变量需要扩展为：

```text
AvailablePool
ShopOffer
OwnedUnit
ConsumedByFusion
```

每个开局 Pool UnitId 恰好位于其中之一。

使用 HostOnly、不可变、规范排序的 tombstone 集合，例如：

```text
RetiredPersistentUnit
  UnitId
  Reason = FusionConsumed | OverflowDiscarded
```

要求：

- tombstone 保留到 Match Session 结束。
- 相同 UnitId 只能退休一次。
- M3 不需要把被消耗实体的全部旧状态长期保存；但要保留足以证明不复用和池守恒的 ID/原因。
- tombstone 不下发给普通 Public/Owner UI。
- Overflow 封存删除也使用同一永久退休机制，但原因不同。

## 11. Buff 所有权与重映射

当前尚没有实际玩家层定向 Buff，但 M3 必须建立不会丢失未来 Buff 的强类型边界。

至少区分：

```text
PlayerTargetedUnitBuff
  BuffInstanceId
  BuffTypeId
  TargetUnitId
  CanonicalPayload

PlayerGlobalBuff
BondOrSourceEffect
BattleTemporaryBuff
```

规则：

- `PlayerTargetedUnitBuff` 属于玩家状态，只是通过 TargetUnitId 指向一个具体持久单位。
- 合成中所有指向被消费 UnitId 的定向 Buff，原子改为指向本步幸存 UnitId。
- 若多个被消费单位各有定向 Buff，全部保留并指向同一幸存单位；不静默合并、覆盖或删除不同 BuffInstanceId。
- 若同一 Buff 是否可叠加未来由 Buff 系统决定，M3 不自行去重。
- 已经指向幸存单位的定向 Buff 保持。
- PlayerGlobalBuff、羁绊和来源型效果保留在自己的玩家/来源状态中，通过后续投影重新生效，不随单位消费移动或丢失。
- BattleTemporaryBuff 只存在于 Battle Core，不写回持久 MatchState，也不参与合成。
- 合成候选不要求 Buff 相同。

如果 M1 的 `MatchUnitState.Buffs[]` 是未分类占位：

- 不要把所有 Buff 简单 union 到幸存单位。
- 用最小强类型扩展明确“实例内快照”和“玩家层定向引用”的边界。
- 幸存实例自己的持久实例字段保留。
- 被消费实例中无法证明应转移的未知 inline Buff 不得被静默处理；当前正式测试可只使用空 inline Buff 和明确的 PlayerTargetedUnitBuff。
- 发现现有实际 Buff 系统与上述所有权冲突时停止并报告，不猜测 Buff 效果。

定向 Buff 重映射后的目标必须仍属于同一玩家，不能跨玩家。

## 12. Staging 严格堆叠槽

Staging 容量是 13 个“严格堆叠槽”，不是 13 个 UnitId。

两个 Staging 持久单位可以占同一槽，当且仅当它们的：

- TypeId 相同；
- EliteLevel 相同；
- 所拥有的持久 Buff 规范快照完全一致。

每个单位仍保留独立 UnitId。Deployed、Overflow 和 Shop 不使用 Staging 堆叠。

M3 应实现唯一权威的纯函数/服务：

```text
ProjectStagingStacks(playerState)
CountStagingSlots(playerState)
CanPlaceInStaging(unit, playerState)
```

要求：

- Buff 比较使用明确 CanonicalSummary/逐字段 ordinal 比较，不使用引用相等、默认 GetHashCode 或本地化 JSON 文本。
- 当前没有实际 Buff 时，空 Buff 单位按 TypeId+EliteLevel 严格堆叠。
- 定向 Buff 是否构成“单位拥有的 Buff”必须由强类型投影明确；不能因其存储在玩家层就从堆叠键中漏掉。
- 严格堆叠槽从左到右按“部署 Cost、TypeId 数值、EliteLevel、Buff 完整规范摘要 ordinal、堆叠内最小 UnitId ordinal”依次升序。
- 槽内 UnitId 按 ordinal 升序；封存自动部署若选择该槽，使用最小 UnitId。
- 以上是权威玩家可见排序，不能再使用容器顺序或另一个 UI 排序器。
- M2 的购买前满 13 检查必须改用此唯一实现。

合成后、退场后和 Overflow 提升后都重新从权威单位集合派生槽数，不保存可漂移的第二份 SlotCount。

## 13. 已部署幸存实例与 Cost

Deployed 幸存实例每升一级时，比较升阶前后部署费用：

```text
OldCost = DeploymentCost(TypeId, oldElite)
NewCost = DeploymentCost(TypeId, newElite)
Delta = NewCost - OldCost
```

### 13.1 Cost 足够

若 `Delta <= AvailableDeploymentCost`：

- 幸存实例保持 Deployed；
- Formation 不变；
- AvailableDeploymentCost 减少 Delta；
- TotalDeploymentCost 不变。

当前默认倍率单调递增；若权威逐级费用允许 Delta 为负，必须用清晰的受检规则返还差额并保持 `0 <= Available <= Total`，不能依赖无符号下溢。

### 13.2 Cost 不足

若 `Delta > AvailableDeploymentCost`：

- 合成仍成功；
- 幸存实例保留升阶后的 EliteLevel 和同一 UnitId；
- 从 Deployed 退出；
- 清除 Formation；
- 完整释放升阶前该实例已占用的 `OldCost`，即 AvailableDeploymentCost 增加 OldCost；
- 再按升阶后实例的严格堆叠结果选择落点：
  - 放入 Staging 后不超过 13 槽：进入 Staging；
  - 否则进入 Overflow。

进入 Overflow 不占部署 Cost，不占 Staging 槽。

若一次连锁中：

- 先升阶且 Cost 足够，继续保持 Deployed；
- 后续更高一阶 Cost 不足，则在该步退场，并释放退场前当前精英等级已实际占用的完整 Cost；
- 退场后后续连锁作为 Staging/Overflow 幸存者继续，不再占用 Cost。

所有 Cost 变化与合成同一事务提交。

## 14. Overflow

Overflow 是持久玩家状态中的临时区域：

- 不参与部署；
- 不占 Staging 13 槽；
- 不占部署 Cost；
- OwnerPrivate 可见；
- Public 不可见；
- HostOnly 可完整核对；
- 无 Formation。

### 14.1 参与自动合成

Overflow 单位按第 7 节候选区参与本次获得事务引发的连锁合成。

- 它与 Staging/允许时的 Deployed 单位可以配对。
- 幸存优先级仍低于 Deployed 和 Staging。
- Overflow 被消费的 ID 进入 FusionConsumed tombstone。
- 合成触发集中在“获得单位”事务，不为 Overflow 单独启动后台扫描。

### 14.2 提升到 Staging

本次合成全部结束后，对当前玩家仍在 Overflow 的单位：

1. 按 `AcquisitionOrdinal` 升序、UnitId ordinal 升序稳定排列。
2. 逐个计算把当前单位移入 Staging 后的严格槽数。
3. 若不超过 13，移动到 Staging。
4. 若会超过 13，该单位保持 Overflow。
5. 继续检查后续单位；前一个放不下不阻塞后续能并入已有严格堆叠、因而不新增槽位的单位。
6. 提升本身不再次触发合成扫描。

这里不是严格 FIFO 阻塞队列，而是“按获取顺序逐个尝试”。必须测试较早单位留在 Overflow、较晚单位因可堆叠而成功提升。

### 14.3 封存删除

M3 提供可由 M4 组合的 Host 内部事务步骤：

```text
DiscardRemainingOverflowAtSeal()
```

规则：

- 在生成 SealedBattleInput 之前执行。
- 对四席位仍在 Overflow 的全部持久单位，按 SeatIndex、AcquisitionOrdinal、UnitId 稳定顺序永久删除。
- 进入 `OverflowDiscarded` tombstone。
- 不能返回共享牌库。
- 清除/处理指向被删除 UnitId 的定向 Buff时，不得猜测：由于没有幸存目标，这类 Buff 的失效/移除策略必须由 Buff 类型声明。
- 当前尚无这种实际 Buff 时，至少拒绝一个缺少 discard policy 的定向 Buff，而不是留下悬空引用。
- M4 需要把 Overflow 删除、封存安全购买/部署和 BattleInput 生成包在同一封存事务；M3 不自行切换 Phase。
- 一次封存删除最多产生一个 revision，并应能嵌入 M4 的更大单 revision。

为未来 Buff 提供强类型 discard policy，例如 `RemoveWithTarget`、`ReturnToOwnerPool` 等；当前只实现已确认且无外部副作用的最小类型。不要虚构玩家补偿。

## 15. 与 Ready 和 BattleInput 的关系

- Preparation、Ready=false：获得可修改 Deployed，因为阵型尚未封存。
- Preparation、Ready=true：获得只扫描 Staging+Overflow；Deployed 不变化。
- Battle：获得只扫描 Staging+Overflow；当前 SealedBattleInput 永不改变。
- 取消 Ready 只恢复未来操作权限，不触发历史单位合成。
- 在取消 Ready 后下一次新获得单位发生时，才按 Ready=false 候选区执行新的连锁；这次扫描可以使用与新获得 TypeId 相同的历史候选。
- M3 不负责 Ready 状态切换或提前开战。

Owner/Public 当前持久单位的精英等级可随购买事务更新；本场已封存战斗仍使用旧不可变快照。

## 16. 命令结果与可观察诊断

Purchase 成功结果可以增加不可变、权限安全的领域摘要：

```text
AcquiredUnitId
FinalSurvivorUnitId?
FusionSteps[]
RetiredUnitIds[]
FinalZone
FinalEliteLevel
GoldSpent
```

要求：

- 结果不泄露其他玩家私有状态或共享池余量。
- 同一 CommandId 重发返回完全相同结果。
- 若获得的 UnitId 在合成中被消费，结果要能指出最终幸存 UnitId。
- 诊断顺序稳定。
- UI 文本由外层映射，领域层用稳定结果码/值对象。

不增加玩家可直接发送的 `FuseUnits` 命令。

## 17. 权限投影

### Public

- 继续公开 Deployed 和 Staging 持久单位的 UnitId、TypeId、EliteLevel、公开 Formation/必要 Buff 投影。
- 不公开 Overflow。
- 不公开 tombstone、被消费列表、玩家定向 Buff 的私有 payload 或 Host 不变量状态。

### OwnerPrivate

- 玩家可见自己当前所有 Deployed、Staging 和 Overflow 单位。
- 可见自己有权看到的定向 Buff 和合成结果。
- 不可见其他玩家 Overflow、Buff、经济或 tombstone。

### HostOnly

- 可见全部当前单位。
- 可见全部定向 Buff 引用。
- 可见 FusionConsumed/OverflowDiscarded tombstone。
- 可核对开局 Pool UnitId 的完整守恒。

投影必须不可变、稳定排序、无悬空引用，并延续 M1/M2 CanonicalSummary 规则。

## 18. 不变量

每次提交前至少验证：

- M1/M2 全部既有不变量。
- 当前持久 UnitId 全 Match 唯一。
- tombstone UnitId 不在 AvailablePool、Shop 或任何当前 Units 中。
- 每个开局 Pool UnitId 恰好处于 AvailablePool、ShopOffer、OwnedUnit、FusionConsumed 或 OverflowDiscarded 之一。
- 同一个 UnitId 不得两次退休。
- EliteLevel 在 `0..MaxEliteLevel`。
- Deployed 单位有且只有一个合法 Formation；Staging/Overflow 无 Formation。
- Overflow 不占 Staging 槽或部署 Cost。
- Staging 严格堆叠槽数不超过 13。
- AvailableDeploymentCost 等于 TotalDeploymentCost 减去全部 Deployed 当前费用之和，且在 `0..Total`。
- 所有 PlayerTargetedUnitBuff 的 TargetUnitId 指向同一玩家当前存在的持久单位。
- 合成结束后，本次 TypeId 在当前候选区不存在仍可继续合成的一对。
- Ready/Battle 时 Deployed 的 CanonicalSummary 不因购买/合成变化。
- AcquisitionOrdinal 稳定且唯一。
- Overflow 提升顺序确定。
- 失败事务后的完整权威摘要、Pool、tombstone、Buff、Cost 和 revision 不变。

## 19. 为后续里程碑保留的 API

### M4 封存与流程

M4 必须能够在一个 authority draft 中组合：

```text
ResolveAnyCurrentAcquisitionTransaction
DiscardRemainingOverflowAtSeal
ApplyHumanSealSafeguardPurchase
ApplyHumanSealSafeguardDeploy
BuildSealedBattleInputs
EnterBattle
CommitOnce
```

注意：

- 第一回合安全购买若成功，也属于新的获得事务，必须立即合成并提升 Overflow。
- 封存顺序要由 M4 按 SPEC 固定；M3 不自行执行安全购买或自动部署。
- 如果安全购买发生在 Overflow 删除之后，新购买不应产生 Overflow，因为商店购买前仍受 13 槽硬拒绝；但合成 Cost 退场仍可能产生 Overflow，因此 M4 必须在最终 BattleInput 前确保再次满足“无剩余 Overflow”。主 Planner 的 M4 提示词会明确最终顺序。

### Battle adapter

后续由 EliteLevel 解析：

- 战斗实体数 `1/2/3/5`；
- 对应精英变体/继承后的战斗数据；
- 每个战斗实体独立 lifeDeduct。

M3 不生成 BattleEntityId，也不改 Battle Core。

### AI

AI 购买复用同一获得/合成事务。AI 的“购买成功后立即尝试部署”由 M5 在收到最终幸存 UnitId 后执行；若购入 UnitId 被消费，不能再尝试部署已删除 ID。

## 20. 明确不在 M3 范围

不得实现或修改：

- 牌库外赠送持久单位的 UnitId 生成与同步；
- 商店概率、池数量和升级数值的重新平衡；
- 出售和返池；
- 玩家主动选择合成、关闭自动合成或拆分精英单位；
- 通过普通移动/取消准备触发全量合成；
- 自动部署、手动部署、撤退、换位业务；
- 第一回合安全购买触发；
- 30 秒计时、Ready 提前开战和 Phase 驱动；
- 基础收入、连续状态、平局彩蛋和战斗结算；
- 配对、主客场和影子战斗；
- BattleInput、战斗实体 ID、Battle Core、分块和哈希；
- AI 决策；
- LAN 协议、Socket、重连和 token；
- Buff 实际数值效果、叠加公式、羁绊计算；
- 场景、Prefab、HUD 或表现；
- `LocalMatchState` 联网改造；
- Unity/Package 升级。

## 21. TDD 与测试要求

先写失败测试，再实现。至少覆盖：

### 21.1 基本合成

- 两个同 TypeId、同 E0 自动得到一个 E1。
- 两个同 TypeId 但不同 Elite 不合成。
- 不同 TypeId 不合成。
- Buff 不同仍可合成。
- MaxElite 0 完全不合成。
- MaxElite 1 停在 E1；MaxElite 2 停在 E2；默认 3 可到 E3。
- 两个 E3 不继续合成。
- 四个 E0 连锁到 E2。
- 八个 E0 连锁到 E3。
- 牌库副本数不改变上述上限。
- 没有独立玩家 `Fuse` 命令。

### 21.2 触发边界

- 商店购买成功触发合成。
- 购买失败不触发。
- Overflow 提升本身不触发第二次扫描。
- 取消 Ready 不触发。
- 部署/撤退/换位不触发。
- 新获得 TypeId A 不扫描并合成无关的历史 TypeId B。
- 快照恢复不触发。

### 21.3 候选区域

- Preparation 未准备：Deployed+Staging 合成。
- Preparation 未准备：Deployed+Overflow 合成。
- Preparation 已准备：Deployed 被排除，Staging+Overflow 可合成。
- Battle：Deployed 被排除，Staging+Overflow 可合成。
- 当前 BattleInput 在 Battle 购买后逐字段不变。
- ShopOffer 不参与。

### 21.4 幸存者

- Deployed 对 Staging，Deployed ID 幸存且坐标保留。
- Staging 对 Overflow，Staging ID 幸存。
- 同 Zone 由 UnitId ordinal 升序幸存。
- 四个/八个单位多级连锁始终保留按规则确定的同一个最终 ID。
- 输入容器顺序打乱不改变结果。
- 结果明确映射 AcquiredUnitId 到 FinalSurvivorUnitId。

### 21.5 UnitId 永久删除

- 被消费 ID 从当前 Units 删除。
- 不返池、不进商店。
- 进入 FusionConsumed tombstone。
- 不能重新插入同 ID。
- 同 ID 不能退休两次。
- M2 池守恒扩展后仍精确成立。
- Public/Owner 不泄露 tombstone，HostOnly 可核对。

### 21.6 Buff

- 指向 consumed ID 的一个定向 Buff 重映射到 survivor。
- 多个 consumed ID 的多个 Buff 全部保留。
- 已指向 survivor 的 Buff 不变。
- 相同 BuffType 但不同 BuffInstanceId 不被 M3 擅自去重。
- 全局/羁绊/来源型状态不移动、不丢失。
- BattleTemporaryBuff 不出现在持久状态。
- 不产生跨玩家引用。
- 任一重映射失败使整个购买/获得事务回滚。

### 21.7 Cost 和退场

- E0→E1 Delta 可支付时保持 Deployed、坐标不变、Available 精确扣 Delta。
- Delta 不可支付时合成仍成功、清坐标、释放 OldCost。
- Staging 可容纳时退入 Staging。
- Staging 满且不可堆叠时退入 Overflow。
- Staging 满但可并入严格堆叠时退入 Staging。
- 多级连锁前级可支付、后级不可支付时释放当时实际占用的当前精英费用。
- TotalCost 始终不变，Available 始终等于 Total 减部署占用。
- 整数溢出/非法费用原子拒绝。

### 21.8 Staging 严格堆叠

- 同 TypeId、Elite、空 Buff 的多个实例占一个槽。
- Elite 不同占不同槽。
- Buff 规范快照不同占不同槽。
- 定向 Buff 不同会反映在单位拥有 Buff 投影中并影响堆叠。
- 每个 UnitId 保持独立。
- 槽数从单位集合派生，不保存漂移计数。
- M2 购买前满 13 检查使用新权威槽函数。
- 严格堆叠槽精确按 Cost、TypeId、Elite、Buff 摘要、最小 UnitId 排序。
- “最右侧槽”和“槽内最小 UnitId”得到精确、稳定结果。

### 21.9 Overflow

- 非商店测试 fixture 获得在 Staging 满时进入 Overflow，但不由 M3 生成新 ID。
- Overflow 参与本次合成。
- 合成释放槽后按 AcquisitionOrdinal 提升。
- 同 AcquisitionOrdinal 不应发生；防御性决胜使用 UnitId。
- 较早 Overflow 单位不能放入 Staging 时保留，较晚可并入堆叠单位仍能提升。
- 提升不触发新扫描。
- 提升后 Staging 不超过 13。
- 封存按稳定顺序删除剩余 Overflow。
- 封存删除进入 OverflowDiscarded tombstone，不返池。
- 缺少 Buff discard policy 时显式拒绝，不留下悬空 TargetUnitId。
- Owner 可见自己的 Overflow，Public/他人不可见。

### 21.10 原子性与幂等

- 购买、合成、Buff 重映射、Cost、Overflow 提升只递增一次 revision。
- 同 CommandId 重发返回同一完整 FusionSteps，不重复消费单位。
- 同 ID 不同购买内容冲突。
- 在每个可能失败步骤注入失败，完整 CanonicalSummary、Gold、Shop、Pool、Units、Buff、Cost、tombstone、ordinal 和 revision 均不变。
- 订阅者看不到获得成功但尚未合成的中间状态。
- M1、M2 全部既有测试继续通过。

## 22. 验证顺序

按仓库规范执行并记录：

1. `git diff --check`
2. 审查最终 diff 和新增 `.meta`
3. Match 相关 C# 编译
4. M1/M2 既有 Match EditMode 测试
5. M3 新增 EditMode 测试
6. 若项目脚本支持，运行相关完整 EditMode 测试集
7. 检查 Unity 日志无本次新增错误或未处理异常
8. 独立审查：
   - 是否存在购买与合成两个 revision
   - 是否能重复使用 retired UnitId
   - Ready/Battle 的 Deployed 是否被误改
   - Cost 退场释放的是正确精英等级的旧占用
   - Buff 是否出现悬空/跨玩家引用
   - Overflow 提升是否错误采用首项阻塞
   - 失败事务是否修改 ordinal/tombstone
   - 是否擅自实现了牌库外 ID 或 Buff 效果

测试数为 0、Unity 占用、许可证失败、超时、结果 XML 缺失或日志不完整均标记为未验证。

## 23. 完成交付

最终提交前：

- 更新实际受影响的架构和测试文档，不改写无关 SPEC。
- 不提交 Library、Temp、Logs、Build、Artifacts 或无关用户修改。
- 创建一个聚焦 M3 的 commit。

交付给主 Planner：

1. 分支名和 commit SHA；
2. M1/M2 基线 SHA；
3. 修改文件；
4. 获得事务、合成、Buff、Cost、Overflow 的关键 API；
5. M4/M5/Battle adapter 接入点；
6. 实际测试命令、退出码、数量和结果文件；
7. 未验证项与剩余风险；
8. 明确没有实现 M4—M8、牌库外 ID 或 Buff 数值效果。

停止条件：

- M2 不是主 Planner 指定基线；
- M1/M2 无法支持单 revision 原子组合且最小扩展会破坏公开契约；
- 现有 Buff 系统与已确认所有权模型冲突；
- 无法从权威目录得到 MaxEliteLevel 或逐精英部署费用；
- SPEC 与本提示词出现新的玩家可见行为冲突；
- 需要升级 Unity/Package、安装未批准依赖或修改高冲突序列化资源；
- 连续三次有实质差异的尝试仍被同一测试阻塞。

遇到停止条件时保留证据并报告，不得通过吞错、硬编码 ID、跳过 Buff 或破坏池守恒来制造通过。
