# 玩家商店刷新排序设计

日期：2026-07-28

## 1. 目标

商店刷新后，商品从左到右按以下复合键升序排列：

1. 单位稀有度；
2. 单位数值 `typeId`。

因此稀有度更高的商品位于更右侧；稀有度相同时，数值
`typeId` 更大的商品位于更右侧。

本设计同时区分玩家主动刷新与整轮战斗结束后的被动刷新，并为四名
玩家维护彼此独立的商店状态。

## 2. 已确认行为

### 2.1 初始商店

- 初始加载不额外排序，只在发生主动或被动刷新时排序。
- 当前固定 Demo 允许四名玩家从相同的初始测试商品开始。
- 固定商品页面只是当前测试商品来源，不属于长期商店机制。未来按玩家
  等级和概率随机生成商品时，刷新排序规则保持不变。

### 2.2 主动刷新

- 主动刷新只作用于发起命令的本地玩家。
- 主动刷新继续消耗 `1` 赤金；赤金不足时保持现有失败语义，商店和赤金
  都不改变。
- 主动刷新会替换全部六个商品，包括已经冻结的商品。
- 主动刷新后全部冻结状态清除。
- 六个新商品先按稀有度、数值 `typeId` 升序排序，再依次写入
  `ShopSlotId=0..5`。

### 2.3 战斗结束被动刷新

- 两场战斗都完成、整轮从 Battle 返回 Preparation 时触发一次。
- 被动刷新不消耗任何玩家的赤金。
- 四名玩家在同一次整轮转换中全部刷新。
- 每名玩家的冻结商品及冻结状态保持在原 `ShopSlotId`。
- 空槽和未冻结槽均属于可刷新槽。
- 新生成的商品先按稀有度、数值 `typeId` 升序排序，再依次填入该玩家
  从左到右的可刷新槽；冻结槽不参与排序，也不移动。

### 2.4 排序决胜

- 稀有度从 Player-safe `UnitCatalogEntry.Rarity` 读取，不保存第二份价格
  或稀有度配置。
- `typeId` 按十进制数值比较，不按字符串字典序比较。例如 `9` 位于
  `10` 左侧。
- 稀有度和数值 `typeId` 都相同的重复商品保持生成顺序。

### 2.5 UI 与权限

- 观察其他玩家时，商店面板仍显示本地玩家商店。
- 远端三名玩家的商店只作为独立权威状态维护，本任务不新增远端商店
  展示或远端主动商店命令。
- `ShopSlotId` 继续表示稳定的物理槽位。排序改变槽位中的商品，不改变
  六个槽位本身的 ID、点击命令或布局。

## 3. 架构

排序必须在 `LocalMatchState` 领域层完成，不能只在 UI 投影中完成。
`ShopReadyHudState` 和 `ShopReadyHudController` 继续按权威
`ShopSlotId` 显示本地玩家快照，不维护第二份排序。

每个 `LocalMatchPlayerData` 拥有独立的六槽商店状态。现有仅属于本地
玩家的购买、冻结和主动刷新命令改为读取本地玩家的商店；快照则为四名
玩家分别投影各自的商店。

排序逻辑收敛为一个无 Unity 场景依赖的领域辅助单元：

```text
generated offers
    -> resolve rarity from UnitCatalog
    -> stable order by rarity ascending
    -> then numeric typeId ascending
    -> assign to target ShopSlotIds ascending
```

当前固定页面的推进方式保留在测试商品来源边界内。排序器只消费一次刷新
产生的商品集合，不读取或暴露页面游标。

## 4. 数据流

### 4.1 主动刷新

```text
LocalMatchState.TryRefresh
    -> validate local gold
    -> request six offers
    -> sort offers
    -> replace local slots 0..5
    -> clear all frozen flags
    -> deduct one gold
    -> publish one changed snapshot
```

### 4.2 被动刷新

```text
both battle presentations completed
    -> PreparationBattleLoopController.ReturnToPreparation
    -> LocalMatchState refresh-all-after-battle operation
    -> for each player:
         preserve frozen slots
         request offers for refreshable slots
         sort offers
         fill refreshable slots left-to-right
    -> publish one changed snapshot
    -> continue existing return-to-preparation transition
```

被动刷新只改变四名玩家的商店内容与相应冻结状态，不修改赤金、单位池、
阵型、部署费用、生命、观察目标、准备状态、已封存输入或战斗结果。

## 5. 错误与一致性

- 商店商品类型仍必须在加载时存在于单位目录。
- 刷新排序只接受能够按十进制数值解释的目录 `typeId`；当前真实目录及
  固定测试目录均满足该契约。
- 主动刷新校验失败时不得推进测试商品来源、扣费、清除冻结或发出状态
  变化通知。
- 被动刷新由一次合法的 Battle → Preparation 转换触发，不因 UI
  重绘、观察切换或重复读取快照而重复执行。
- 四名玩家刷新完成后作为一次领域变更发布，避免 UI 观察到部分玩家已
  刷新、部分玩家尚未刷新的中间状态。

## 6. 测试

### 6.1 EditMode

- 主动刷新按稀有度升序排列。
- 同稀有度按数值 `typeId` 升序排列，并以 `9`、`10` 覆盖字符串排序
  回归。
- 主动刷新替换冻结商品并清除全部冻结状态。
- 主动刷新仍只扣除本地玩家 `1` 赤金，失败保持原状态。
- 四名玩家分别拥有六个商店槽位。
- 被动刷新不扣除赤金，并在一次操作中刷新四名玩家。
- 被动刷新保留冻结商品的商品、槽位和冻结状态。
- 被动刷新的新商品只在非冻结槽之间排序。
- 快照和 canonical summary 保持确定性。

### 6.2 PlayMode

- 两场战斗全部完成并返回准备阶段时，被动刷新恰好触发一次。
- 四名玩家商店均发生预期变化，本地玩家赤金不变。
- 正式商店 UI 继续显示本地玩家的排序后商店；观察远端玩家不切换商店。

## 7. 文档

实现完成后同步：

- `docs/SPEC.md`：主动刷新、被动刷新、冻结与排序规则；
- `docs/ARCHITECTURE.md`：每玩家商店状态和领域排序边界；
- `docs/TEST_PLAN.md`：Red/Green、全量测试和构建证据。

## 8. 非目标

- 不实现按等级和概率随机抽取商品。
- 不把固定页面游标写入长期公共接口。
- 不显示远端玩家商店。
- 不新增远端主动购买、冻结或刷新命令。
- 不修改商店卡片布局、点击区域、价格规则或刷新费用 UI。
- 不修改当前工作区中与本任务无关的准备按钮切图、羁绊资源或批量单位
  资源导入改动。
