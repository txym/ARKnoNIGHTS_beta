# 自包含单位精英变体 v2 源数据设计

状态：已确认，尚未实施。

## 1. 背景

项目当前存在两套单位源 JSON：

- `Assets/GameData/Units/Json/*.json` 使用 `unit-source-v1`，保存单位级规则、精零数值和模型绑定；
- `Assets/GameData/Units/EliteVariants/Json/*.json` 使用 `unit-elite-variants-v1`，作为依赖前者的稀疏精英变体 sidecar。

现有结构存在以下问题：

1. 一个单位的权威事实被拆在两个目录中；
2. v1 精英变体文件不能脱离基础 JSON 独立解析；
3. 精英变体模型块缺少单位级规则；
4. 旧动画字段只能表达单一 Attack/Move/Death，且包含永远为空的 `hitAnimation`；
5. `unitSkeletonType` 通过数字类型隐式决定播放方式，无法清楚表达完整动画绑定；
6. 后续批量导入 93 个 TypeId、172 个资源变体时会制造重复数据和双格式维护成本。

本设计把精英变体文件升级为每个 TypeId 唯一、自包含的单位源数据。

## 2. 已确认目标

1. 唯一人工维护的单位源目录改为：

   ```text
   Assets/GameData/Units/EliteVariants/Json
   ```

2. 每个 TypeId 使用一个 `unit-elite-variants-v2` 文件。
3. v2 文件同时保存单位级共享规则和全部已导入精英变体。
4. 精零条目必须完整；高阶条目继续向最近的低阶条目继承未声明的数据块。
5. 模型块一旦声明就必须完整并原子覆盖。
6. 动画资源事实由 JSON 完整声明，但动画状态机和技能播放逻辑不写入单位源 JSON。
7. `Assets/GameData/Units/Json` 在读取方迁移和验证完成后删除。
8. 第一实施阶段只迁移当前 1000、5503、5504 三个源文件及其读取方。
9. 第一实施阶段不修改或重新生成现有 Player 运行时目录。
10. 三文件迁移验证通过后，另行执行其余单位的批量 v2 JSON 导入。

## 3. 非目标

- 不在本阶段实现精英档运行时选择。
- 不改变战斗中 `eliteLevel` 的现有行为。
- 不修改 `Assets/Resources/BattleData/unit-catalog-v1.json`。
- 不批量实现 BONDS 中描述的具体技能。
- 不在单位 JSON 中保存 `animationBehavior`、播放状态机、播放倍速或目标播放时长。
- 不实现 `UnitAnimation.md` 中记录的特殊动画播放逻辑。
- 不删除现有 Hit 播放接口；本阶段只记录其后续销毁范围。
- 不开始其余 90 个 TypeId 的源 JSON 导入。

## 4. 文件布局与命名

每个 TypeId 只有一个权威文件：

```text
Assets/GameData/Units/EliteVariants/Json/<typeId>_<baseResourceKey>.json
```

示例：

```text
Assets/GameData/Units/EliteVariants/Json/1000_gopro.json
Assets/GameData/Units/EliteVariants/Json/5503_arcslma.json
Assets/GameData/Units/EliteVariants/Json/5504_arcslmi.json
```

`sourceVariant` 是对应物理资源文件夹名，例如 `1000_gopro_2`。模型块不再重复保存 `resourceFolderName`。

1322 继续遵守已经完成的资源重映射：

- `1322_wdgyht` 是精零；
- `1322_wdgyht_2` 是精二。

## 5. v2 顶层结构

```json
{
  "schemaVersion": "unit-elite-variants-v2",
  "typeId": 1000,
  "common": {},
  "variants": []
}
```

### 5.1 `common`

`common` 保存不随精英变体变化的单位规则：

```json
{
  "rarity": 1,
  "deploymentCost": 2,
  "attackMethod": 1,
  "actionMethod": 1,
  "attackRadiusMetres": 0,
  "blockRadiusMetres": 0,
  "canBlock": true,
  "blockCapacity": 1,
  "tauntLevel": 0,
  "damageType": "Physical"
}
```

字段语义：

- `rarity`：单位稀有度，范围 `1..6`；
- `deploymentCost`：精零基础部署费用；本批单位统一为 `2`；
- `attackMethod`：`0` 表示不攻击，`1` 表示近战；
- `actionMethod`：使用已确认的 1～4 行动方式编号；
- `attackRadiusMetres`、`blockRadiusMetres`：本批单位统一为 `0`；
- `canBlock`、`blockCapacity`：基础阻挡规则；
- `tauntLevel`：基础嘲讽等级，本批单位为 `0`；
- `damageType`：`Physical`、`Magic` 或 `None`。

不攻击单位必须同时满足：

- `attackMethod = 0`；
- `damageType = "None"`；
- 变体攻击力为 `0`；
- 攻击间隔为 `0`；
- 动画表不声明任何攻击动画。

不可阻挡单位使用 `canBlock = false`、`blockCapacity = 0`。可阻挡单位使用 `canBlock = true`、`blockCapacity = 1`。

### 5.2 `variants`

每个变体条目结构如下：

```json
{
  "minEliteLevel": 0,
  "sourceVariant": "1000_gopro",
  "statsLevel": 0,
  "displayNameZhHans": "猎狗",
  "skillDescriptionZhHans": "",
  "stats": {},
  "model": {},
  "innateAbilityIds": []
}
```

字段规则：

- `minEliteLevel` 范围为 `0..3`，同一文件内不得重复；
- 第一项必须是 `minEliteLevel = 0`；
- `sourceVariant` 是原始资源变体和项目资源文件夹名；
- 当前所有导入变体使用 `statsLevel = 0`；
- `displayNameZhHans`、`skillDescriptionZhHans` 和 `innateAbilityIds` 属于变体；
- 精零必须显式声明名称、说明、完整数值、完整模型和能力数组；
- 高阶条目省略名称、说明或能力数组时，继承最近的低阶条目；
- 显式空能力数组 `[]` 表示清空能力；
- 本阶段只保留 5503 已存在的 `SUMMON_JELLY_MINIONS`；
- 其他单位本阶段使用空技能说明和空能力数组，不从 BONDS 批量生成技能。

## 6. 数值块

```json
{
  "stats": {
    "combat": {
      "maxHitPoints": 820,
      "attack": 190,
      "defense": 0,
      "magicResistance": 20
    },
    "shared": {
      "moveSpeedMetresPerSecond": 1.9,
      "attackIntervalSeconds": 1.4,
      "lifeDeduct": 1
    }
  }
}
```

继承和验证规则：

- `stats.combat` 是原子块；声明时必须提供全部四个字段；
- `stats.shared` 是原子块；声明时必须提供全部三个字段；
- 精零必须声明两个完整块；
- 高阶变体可以省略整个块并继承；
- 生命值必须大于零；
- 攻击、防御、法术抗性和目标价值不得为负；
- 法术抗性保持现有合法范围；
- 可移动单位的移动速度必须大于零；
- 不攻击单位允许攻击间隔为零；
- 攻击单位的攻击间隔必须大于零。

## 7. 模型与动画

完整模型块：

```json
{
  "model": {
    "resourceKey": "gopro",
    "skeletonDataResourceName": "enemy_1000_gopro_SkeletonData",
    "profilePictureResourceName": "UIImage_1000_gopro",
    "animations": [
      {
        "key": "idle",
        "name": "Idle"
      },
      {
        "key": "move",
        "name": "Run_Loop"
      },
      {
        "key": "attack",
        "name": "Attack",
        "durationSeconds": 1.0
      },
      {
        "key": "death",
        "name": "Die"
      }
    ]
  }
}
```

### 7.1 模型原子覆盖

精零必须声明完整模型。高阶变体可以：

- 完全省略 `model` 并继承；
- 或声明一个完整的新模型块。

模型块不允许字段级继承，避免把新 Skeleton 与旧动画、头像或资源 key 混用。

### 7.2 动画表

`animations` 是该资源变体实际会使用的 Spine 动画注册表。

规则：

- `key` 是动画层使用的稳定语义键；
- `name` 是 Skeleton 中的真实动画名；
- 同一模型内 `key` 不得重复；
- `idle`、`move`、`death` 必须存在，只保存动画名；
- 攻击单位必须声明至少一个实际使用的攻击动画；
- 不攻击单位不得声明攻击动画；
- 除 `idle`、`move`、`death` 外，所有实际使用的动画必须保存正数 `durationSeconds`；
- Skill、Start、Trans、交替攻击、状态攻击和特殊攻击等实际使用动画都进入该表；
- 未使用的动画不进入该表；
- 动画名和时长必须通过 Unity/Spine API 对真实 SkeletonDataAsset 校验。

特殊动画的语义键和后续处理方式以：

```text
docs/bonds/UnitAnimation.md
```

为人工事实来源。v2 不保存状态转换、攻击计数、技能触发、播放顺序、阻断关系或播放倍速。

### 7.3 删除的旧字段

v2 不包含：

- `unitSkeletonType`；
- `hitAnimation`；
- `attackAnimationDurationSeconds`；
- 独立的 `moveAnimation`；
- 独立的 `attackAnimation`；
- 独立的 `deathAnimation`；
- `resourceFolderName`；
- `initialEliteLevel`；
- 任何 `Default` 动画绑定。

## 8. 完整 1000 示例

```json
{
  "schemaVersion": "unit-elite-variants-v2",
  "typeId": 1000,
  "common": {
    "rarity": 1,
    "deploymentCost": 2,
    "attackMethod": 1,
    "actionMethod": 1,
    "attackRadiusMetres": 0,
    "blockRadiusMetres": 0,
    "canBlock": true,
    "blockCapacity": 1,
    "tauntLevel": 0,
    "damageType": "Physical"
  },
  "variants": [
    {
      "minEliteLevel": 0,
      "sourceVariant": "1000_gopro",
      "statsLevel": 0,
      "displayNameZhHans": "猎狗",
      "skillDescriptionZhHans": "",
      "stats": {
        "combat": {
          "maxHitPoints": 820,
          "attack": 190,
          "defense": 0,
          "magicResistance": 20
        },
        "shared": {
          "moveSpeedMetresPerSecond": 1.9,
          "attackIntervalSeconds": 1.4,
          "lifeDeduct": 1
        }
      },
      "model": {
        "resourceKey": "gopro",
        "skeletonDataResourceName": "enemy_1000_gopro_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro",
        "animations": [
          {
            "key": "idle",
            "name": "Idle"
          },
          {
            "key": "move",
            "name": "Run_Loop"
          },
          {
            "key": "attack",
            "name": "Attack",
            "durationSeconds": 1.0
          },
          {
            "key": "death",
            "name": "Die"
          }
        ]
      },
      "innateAbilityIds": []
    },
    {
      "minEliteLevel": 2,
      "sourceVariant": "1000_gopro_2",
      "statsLevel": 0,
      "displayNameZhHans": "猎狗pro",
      "stats": {
        "combat": {
          "maxHitPoints": 1700,
          "attack": 260,
          "defense": 0,
          "magicResistance": 20
        }
      },
      "model": {
        "resourceKey": "gopro_2",
        "skeletonDataResourceName": "enemy_1000_gopro_2_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro_2",
        "animations": [
          {
            "key": "idle",
            "name": "Idle"
          },
          {
            "key": "move",
            "name": "Run_Loop"
          },
          {
            "key": "attack",
            "name": "Attack",
            "durationSeconds": 1.0
          },
          {
            "key": "death",
            "name": "Die"
          }
        ]
      }
    },
    {
      "minEliteLevel": 3,
      "sourceVariant": "1000_gopro_3",
      "statsLevel": 0,
      "displayNameZhHans": "狂暴的猎狗pro",
      "stats": {
        "combat": {
          "maxHitPoints": 3000,
          "attack": 370,
          "defense": 0,
          "magicResistance": 20
        }
      },
      "model": {
        "resourceKey": "gopro_3",
        "skeletonDataResourceName": "enemy_1000_gopro_3_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro_3",
        "animations": [
          {
            "key": "idle",
            "name": "Idle"
          },
          {
            "key": "move",
            "name": "Run_Loop"
          },
          {
            "key": "attack",
            "name": "Attack",
            "durationSeconds": 1.0
          },
          {
            "key": "death",
            "name": "Die"
          }
        ]
      }
    }
  ]
}
```

## 9. 读取方迁移

### 9.1 共享解析器

现有“基础 `UnitJson` + Editor-only sidecar resolver”改为可被 Editor 工具和旧调试入口共同使用的 v2 解析器。

解析器负责：

- schema、TypeId 和文件名校验；
- `common` 完整性；
- 精英档排序、重复和范围校验；
- 精零完整性；
- 原子块继承；
- 模型与动画注册表完整性；
- 指定精英等级的确定性解析；
- 产生不依赖旧 `unit-source-v1` 的完整解析结果。

不保留 v1 或旧目录回退。v2 文件存在但无效时必须显式失败。

### 9.2 `UnitCatalogGenerator`

- 源目录改为 `Assets/GameData/Units/EliteVariants/Json`；
- 每个文件直接解析精零条目；
- 保留现有数值换算和资源验证；
- 若未来显式执行生成，可继续投影现有 `unit-catalog-v1` 结构；
- legacy Hit 字段在旧目录契约中只能投影为空字符串；
- 第一实施阶段不得执行生成，不得改写现有运行目录文件。

### 9.3 `AbilityCatalogGenerator`

- 已知单位 TypeId 改从 v2 文件顶层读取；
- 不再扫描 `Assets/GameData/Units/Json`；
- 未知召唤 TypeId 仍显式失败。

### 9.4 `UnitJsonBake`

- 改为扫描 v2 文件；
- 汇总所有显式声明的 `innateAbilityIds`；
- 不再依赖旧 `UnitJsonLite` 结构。

### 9.5 `UnitFactory`

`UnitFactory.SpawnAll` 是旧调试入口，不是正式 Player 数据链。

本阶段暂时：

- 改为扫描 v2 目录；
- 解析每个 TypeId 的精零条目；
- 继续构造 legacy `UnitTemplate`；
- 不保留旧目录兼容。

同时把“直接读取 Editor 源 JSON 的 UnitFactory 路径”记录为未来应销毁的接口。正式运行时继续只依赖生成目录。

## 10. Hit 接口边界

v2 源数据立即删除 `hitAnimation`。

现有 `unit-catalog-v1` 本阶段不改，因此其空 Hit 字段和相关运行时兼容接口暂时存在。完整待销毁范围记录在 `docs/bonds/UnitAnimation.md`，包括：

- `IBattlePresentationView.PlayHit()`；
- `UnitSkelPresentationView.PlayHit()`；
- Hit 序列化字段和配置参数；
- 旧 Damage 播放调用；
- 目录 DTO/属性；
- 测试替身。

这些接口不能永久保留为空实现，应在旧播放链完成迁移后独立删除。

## 11. 第一阶段迁移边界

第一阶段只处理：

1. 将 1000 的 `unit-source-v1` 共享字段合入现有精英变体文件并升级为 v2；
2. 将 5503、5504 旧文件迁入精英变体目录并升级为完整 v2；
3. 迁移全部直接读取方；
4. 删除旧三个 JSON、对应 `.meta` 和空旧目录；
5. 尽可能通过移动文件和 `.meta` 保留 5503、5504 的既有 GUID；
6. 更新相关测试和文档；
7. 保持生成的运行时目录不变。

迁移完成后才开始其余单位的批量 JSON 导入。

## 12. 运行时冻结边界

第一阶段不得修改：

```text
Assets/Resources/BattleData/unit-catalog-v1.json
Assets/Resources/BattleData/ability-catalog-v1.json
```

实施前记录这些文件的内容哈希，实施后重新计算并要求完全一致。

因此本阶段不会改变：

- 运行时可见单位数量；
- 当前三单位数值；
- 当前模型和动画；
- 精英等级对战斗的现有影响；
- 当前能力目录；
- 当前固定对战结果。

## 13. 错误处理

以下情况必须显式失败：

- schema 不是 `unit-elite-variants-v2`；
- 文件名、TypeId 或精零 `sourceVariant` 不一致；
- 重复 TypeId；
- 缺少 `common`；
- 缺少精零条目；
- 精英档越界或重复；
- 精零名称、数值、模型、动画或能力数组不完整；
- 原子数据块部分声明；
- 模型块部分声明；
- 重复动画 key；
- 缺少 idle、move 或 death；
- 攻击规则与攻击动画不一致；
- 需要时长的动画缺少正数时长；
- 动画名不存在或时长与 Skeleton 不一致；
- 资源文件夹、SkeletonDataAsset 或头像不存在；
- 已声明能力 ID 无法解析。

不允许：

- 静默回退旧目录；
- 以资源 key 代替缺失中文名；
- 猜测动画名；
- 从未使用的 Skeleton 动画自动生成绑定；
- 因源文件无效而继续生成部分目录。

## 14. 验证

### 14.1 纯数据与解析测试

至少覆盖：

- 完整精零解析；
- 精二/精三最近低阶继承；
- `combat`、`shared`、`model` 原子块；
- 名称、说明和能力数组继承；
- 显式空能力数组；
- 不攻击和不可阻挡单位合法值；
- 重复 TypeId/档位/动画 key；
- 缺少必要动画；
- 非循环使用动画的时长要求；
- 旧字段不会进入 v2。

### 14.2 真实三文件测试

验证：

- 1000 三个精英变体；
- 5503 精零与 `SUMMON_JELLY_MINIONS`；
- 5504 精零；
- 三个文件均不含已删除字段；
- 资源路径、头像、动画名和需保存的动画时长真实存在。

### 14.3 消费者测试

验证：

- `UnitCatalogGenerator` 能从 v2 解析精零，但测试不落盘覆盖运行目录；
- `AbilityCatalogGenerator` 从 v2 获取已知 TypeId；
- `UnitJsonBake` 能读取变体能力；
- `UnitFactory` 能从 v2 构造三个精零模板；
- 所有旧目录引用已经消失。

### 14.4 Unity 验证

- Unity 编译无新增错误；
- 相关 EditMode 测试全部通过且测试数大于零；
- 与 `UnitFactory`、真实目录和真实 Spine 相关的 PlayMode 回归通过；
- 运行目录和能力目录内容哈希保持不变；
- `git diff --check` 通过；
- 最终 diff 不包含无关资源、场景、Prefab、Package 或用户已有修改。

## 15. 文档迁移

实施时同步更新：

- `docs/SPEC.md`；
- `docs/ARCHITECTURE.md`；
- `docs/TEST_PLAN.md`；
- 与 `unit-source-v1` 和 v1 sidecar 有关的长期技术决策。

在代码迁移完成前，主 SPEC 继续描述当前已实现的数据链；本设计文档描述已确认但尚未实施的目标状态，避免文档提前宣称未交付行为。

## 16. 后续阶段

第一阶段完成后：

1. 使用同一 v2 schema 生成其余 90 个 TypeId；
2. 从真实 Skeleton 审计中写入全部实际使用动画及所需时长；
3. 按 `UnitAnimation.md` 设计和实现独立动画层；
4. 再决定运行时目录是否升级为保存全部精英变体；
5. 在旧运行目录和旧播放链淘汰后销毁 legacy UnitFactory 源直读与 Hit 接口。
