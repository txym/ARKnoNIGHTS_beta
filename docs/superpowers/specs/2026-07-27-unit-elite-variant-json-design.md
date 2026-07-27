# 单位精英变体 JSON 与精零兼容读取设计

## 背景

当前项目以 `unit-source-v1` 作为单位源数据，并由 `UnitCatalogGenerator`
生成扁平的 `unit-catalog-v1`。运行时目录目前每个 `typeId` 只有一套数值、
名称、头像和 Spine 模型。

部分单位在精英二、精英三时有独立的标准数值、名称、模型和能力。以
`1000_gopro` 为例：

- 精零、精一采用 `1000_gopro`；
- 精二采用 `1000_gopro_2`；
- 精三采用 `1000_gopro_3`。

各变体统一读取对应 `unit-levels.json` 中的 `level = 0`。如果来源文件没有
level 0，才回退到同目录 `unit-source-v1.json` 的标准值。

本阶段不配置或计算精英化数值系数。当前游戏只有精零单位，因此读取链路
只需把精零变体正确合并进现有扁平目录；精二、精三数据先可靠保存，暂不接入
战斗、UI 或播放层的精英档选择。

## 目标

1. 在不制造重复 `typeId` 的前提下保存同一单位的稀疏精英变体。
2. 支持高阶变体只覆盖变化的数据，未声明数据向下读取最近的低阶值。
3. 模型发生变化时，完整绑定资源、头像、骨骼、动画和攻击动画时长。
4. 能力列表随变体覆盖；未声明时继承，显式空数组时清空。
5. 保持没有精英变体文件的现有单位行为不变。
6. 当前生成结果仍为 `unit-catalog-v1`，但 `1000_gopro` 应生成精零数据。
7. 所有单位头像以原始 `Texture2D` 导入，正式 UI 统一缓存转换为 Sprite。

## 非目标

- 不实现合成或精英化命令。
- 不实现精英档数值系数。
- 不让战斗、UI 或播放层按实例 `eliteLevel` 动态选择变体。
- 不修改当前 Player 侧 `unit-catalog-v1` 的结构。
- 不从项目外的工具目录读取运行时数据。
- 不把现有 UGUI `Image` 批量替换为 `RawImage`。
- 不改变 Spine 贴图、普通 UI 贴图或图集的导入和加载方式。

## 文件布局

保留原有单位文件：

```text
Assets/GameData/Units/Json/1000_gopro.json
```

新增旁路变体文件：

```text
Assets/GameData/Units/EliteVariants/Json/1000_gopro.json
```

旁路文件使用独立 schema：

```json
{
  "schemaVersion": "unit-elite-variants-v1",
  "typeId": 1000,
  "variants": []
}
```

`unit-source-v1` 继续保存该单位通用的游戏规则和旧版回退数据。变体文件只保存
随精英档变化的名称、原始标准数值、模型展示数据和能力列表。

## JSON 结构

`variants` 是按 `minEliteLevel` 生效的稀疏覆盖列表。等级范围为 0 到 3，
不允许重复；第一项必须是精零。

```json
{
  "schemaVersion": "unit-elite-variants-v1",
  "typeId": 1000,
  "variants": [
    {
      "minEliteLevel": 0,
      "sourceVariant": "1000_gopro",
      "statsLevel": 0,
      "displayNameZhHans": "猎狗",
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
        "resourceFolderName": "1000_gopro",
        "resourceKey": "gopro",
        "skeletonDataResourceName": "enemy_1000_gopro_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro",
        "unitSkeletonType": 1,
        "attackAnimationDurationSeconds": 1.0,
        "moveAnimation": "Run_Loop",
        "attackAnimation": "Attack",
        "hitAnimation": "",
        "deathAnimation": "Die"
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
        "resourceFolderName": "1000_gopro_2",
        "resourceKey": "gopro_2",
        "skeletonDataResourceName": "enemy_1000_gopro_2_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro_2",
        "unitSkeletonType": 1,
        "attackAnimationDurationSeconds": 1.0,
        "moveAnimation": "Run_Loop",
        "attackAnimation": "Attack",
        "hitAnimation": "",
        "deathAnimation": "Die"
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
        "resourceFolderName": "1000_gopro_3",
        "resourceKey": "gopro_3",
        "skeletonDataResourceName": "enemy_1000_gopro_3_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro_3",
        "unitSkeletonType": 1,
        "attackAnimationDurationSeconds": 1.0,
        "moveAnimation": "Run_Loop",
        "attackAnimation": "Attack",
        "hitAnimation": "",
        "deathAnimation": "Die"
      }
    }
  ]
}
```

## 继承与覆盖语义

选择某个精英等级时，按 `minEliteLevel` 从低到高应用所有不高于目标等级的项。

- 精零选择 `minEliteLevel = 0`；
- 精一仍选择精零项；
- 精二应用精零项后再应用精二项；
- 精三继续应用精三项；
- 精二或精三项不存在时，自然保留上一档的结果，不向上借用更高档。

字段规则：

- `stats.combat` 和 `stats.shared` 分别作为原子数据块覆盖；数据块未声明时继承
  上一档。
- `stats.combat` 一旦声明，必须完整提供生命、攻击、防御和法术抗性。
- `stats.shared` 一旦声明，必须完整提供移动速度、攻击间隔和扣血数。
- `displayNameZhHans` 未声明时继承上一档。
- `innateAbilityIds` 未声明时继承上一档；显式 `[]` 表示清空能力。
- `model` 未声明时完整继承上一档。
- 只要声明 `model`，其中所有字段都必须完整提供，不允许字段级继承。

模型块采用原子覆盖，避免新骨骼与旧头像、旧动画名或旧攻击动画时长混用。

## 头像资源类型与 UI 适配

`Assets/Resources/ProfilePicture` 下的单位头像统一作为原始 `Texture2D`
导入，不再要求 Sprite Import Mode。JSON 与目录继续只保存不带扩展名的
Resources 路径，不增加资源类型字段。

Editor 生成器和 Player 侧目录加载器均使用 `Resources.Load<Texture2D>` 验证
头像存在，不先尝试 `Resources.Load<Sprite>`。

正式头像 UI 继续使用 UGUI `Image`。新增共享的 `UnitPortraitLoader`：

1. 按目录路径加载 `Texture2D`；
2. 用整张纹理、中心 pivot 和 `100 pixels per unit` 创建 Sprite；
3. 按资源路径缓存 Sprite；
4. 同一路径重复请求返回同一 Sprite；
5. 缺失或空路径返回 `null`。

备战槽位、商店单位头像、单位详情头像和当前正式战斗 HUD 都通过该加载器赋值
`Image.sprite`。通用 UI 美术仍可保留现有混合 Sprite/Texture 加载器；本规则
只约束单位头像。

## 当前读取链路

`UnitCatalogGenerator` 在读取每个 `unit-source-v1` 后：

1. 按 `typeId` 查找可选的 `unit-elite-variants-v1`。
2. 没有变体文件时，保持现有逻辑。
3. 有变体文件时，校验 schema、`typeId`、精英档顺序和完整的精零数据。
4. 当前固定解析目标等级 0。
5. 把精零变体的名称、数值、模型字段和能力列表覆盖到内存中的源文档。
6. 继续走原有合法性校验、Spine 动画校验、攻击间隔换算和
   `unit-catalog-v1` 生成流程。
7. 头像路径通过 `Resources.Load<Texture2D>` 校验。

运行时 `RealBattleDataLoader` 仍读取原有扁平目录，不需要理解精英变体 schema。
它同样按 `Texture2D` 校验头像，UI 再通过 `UnitPortraitLoader` 适配为 Sprite。
因此本阶段不改变当前 Player JSON 数据契约。

## 兼容性与错误处理

- 没有旁路变体文件的单位完全沿用 `unit-source-v1`。
- 旁路文件存在但损坏时生成失败，不静默回退到旧数据。
- `typeId` 不匹配、重复精英档、档位越界、缺少精零项或精零数据不完整时生成失败。
- 高阶 `stats` 可以完全省略，也可以只提供完整的 `combat` 或 `shared` 数据块。
- 高阶 `model` 可以完全省略；一旦出现则必须完整。
- 外部工具目录只作为人工导入来源，项目生成过程不访问绝对路径。
- 单位头像必须能以 `Texture2D` 从目录路径加载；Sprite 子资源不是前置条件。

## 1000_gopro 的当前验收结果

生成后的精零目录条目应至少满足：

- 名称：`猎狗`
- 最大生命值：820
- 攻击力：190
- 防御力：0
- 法术抗性：20
- 移动速度：1.9 米/秒
- 源攻击间隔：1.4 秒，继续按既有规则换算为一半基础攻击间隔
- 扣血数：1
- 骨骼：`enemy_1000_gopro_SkeletonData`
- 头像：`UIImage_1000_gopro`
- 能力：空列表

精二和精三 JSON 必须可解析和校验，但本阶段不要求它们进入 Player 侧目录或影响
战斗实例。

## 验证

1. JSON 解析测试覆盖精零解析、部分 `stats` 继承、模型原子覆盖和能力数组语义。
2. 生成器测试确认没有旁路文件的单位结果不变。
3. 生成器测试确认 `1000_gopro` 生成上述精零数据。
4. 头像加载测试确认原始 Texture2D 能转换为缓存 Sprite。
5. 运行时目录测试确认 Texture2D 头像合法。
6. 运行相关 EditMode、PlayMode 测试和 Unity 编译检查。
7. 检查生成目录差异，确认没有意外修改其他单位。

## 后续阶段

实现高阶单位时，再把解析目标从固定精零扩展为保留全部已解析精英档，并同步调整
Player 侧目录、战斗单位定义、显示名称、头像、模型工厂和能力绑定。该阶段还需要
更新当前“`eliteLevel` 不影响战斗结果”的 SPEC 约束。
