# 基础攻击间隔折半设计

## 目标

正式单位的基础攻击间隔采用源单位 JSON 中 `attackIntervalSeconds` 的一半。战斗计算、攻击动画压速、出伤时点和单位详情 UI 必须读取同一份派生后的实际间隔；源 JSON 数值保持不变。

## 规则边界

- 正式单位先计算 `baseAttackIntervalSeconds = attackIntervalSeconds × 0.5`，再执行 `ceil(baseAttackIntervalSeconds × 20)` 得到 `AttackIntervalTicks`。
- `attackAnimationDurationSeconds` 不折半，仍按既有规则以 20 TPS 向上取整。
- Core 的 `AttackIntervalTicks` 已经是实际攻击间隔。`BattleRunner`、表现层和 UI 不得再次折半。
- 合成 `battle-fixture-v1` 直接填写实际 `attackIntervalTicks`，不使用源 JSON 折半规则。
- 攻击间隔 UI 由权威 Tick 换算为秒，最多显示两位小数并移除无意义的末尾零，不显示 `s`。

当前真实单位的预期值为：

| 单位 | JSON 秒数 | 基础攻击间隔秒数 | Core Tick | UI |
|---|---:|---:|---:|---:|
| `1000` / gopro | 1.4 | 0.7 | 14 | `0.7` |
| `5503` / arcslma | 4.0 | 2.0 | 40 | `2` |
| `5504` / arcslmi | 1.5 | 0.75 | 15 | `0.75` |

## 方案比较

### 采用：在源 JSON 到目录的转换边界派生

`UnitCatalogGenerator` 在秒到 Tick 之前应用 `0.5`。生成目录成为战斗、表现和 UI 共用的唯一实际值来源，既有 `BattleRunner` 的冷却与 `min(动画 Tick, 间隔 Tick)` 规则无需重复理解源 JSON 语义。

### 不采用：在 `BattleRunner` 中折半

该方案会把所有直接提供 `AttackIntervalTicks` 的输入都折半，包括合成 fixture 和未来 Buff 后的实际间隔，并造成目录与 UI 仍显示未折半值。

### 不采用：战斗与 UI 分别折半

该方案复制规则，容易出现取整顺序、Buff 投影和显示值漂移，也不能保证表现层与权威出伤时点一致。

## 数据流与实现范围

1. `UnitCatalogGenerator` 将源攻击间隔乘以 `0.5` 后复用既有向上取整。
2. 重新生成 `Assets/Resources/BattleData/unit-catalog-v1.json`，得到 14、40、15 Tick。
3. `BattleRunner` 继续直接使用 `AttackIntervalTicks` 设置 `NextAttackAllowedTick`，并以 `min(AttackAnimationDurationTicks, AttackIntervalTicks)` 计算有效动画和出伤 Tick。
4. `UnitDetailNumberFormatter.AttackInterval` 改为最多两位小数，以准确显示 15 Tick 对应的 `0.75` 秒。
5. 旧 `UnitFactory` 的源 JSON 到 `UnitTemplate` 适配同步使用同一折半语义，避免调试入口与正式目录出现两套基础数值；它不进入权威 Core。

不修改源 JSON、合成 fixture、Tick 频率、伤害公式、场景、Prefab、Package 或项目设置。

## 验证

- 先修改 EditMode 断言并确认在旧目录下失败：真实目录攻击间隔应为 14、40、15 Tick。
- 增加或调整真实战斗回归，确认 arcslma 原始动画仍为 54 Tick，但有效动画和计划出伤改为 40 Tick。
- 修改 UI 格式测试并确认 14、40、15 Tick 分别显示 `0.7`、`2`、`0.75`。
- 重新生成目录后运行相关 EditMode 测试、完整 EditMode 回归和相关 PlayMode 回归。
- 检查生成目录、测试结果、Unity 编译日志和最终 diff，确认没有源 JSON、场景、Prefab 或用户未提交内容的意外变化。
