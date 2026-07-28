# 待部署区与商店地区/种类信息 UI 设计

## 状态

- 日期：2026-07-28
- 状态：已由用户确认
- 事实来源：`docs/SPEC.md`、`docs/bonds/BONDS_SPEC.md`、`docs/references/ui/battle_hud/UI_SPEC.md`

## 目标

在不改变羁绊结算、商店经济或部署规则的前提下，为正式 HUD 补齐两处单位归属信息：

1. 待部署槽位 `HeaderLeft` 显示一个地区或种类图标；
2. 商店槽位 `PortraitClip` 左下角显示部署费用、地区和种类三行信息。

## 范围与非目标

本次修改只负责 UI 所需的只读归属数据和显示逻辑。`docs/bonds/BONDS_SPEC.md` 仍是单位地区与种类的权威来源。

本次不实现羁绊计数、羁绊 buff、商店权重变化或地区/种类筛选，也不修改单位购买价格、部署费用或战斗数值。商店槽位上方现有购买价格继续保留。

## 方案选择

采用独立的 Resources UI 归属数据表，将 BONDS 中商店单位的 `TypeId -> 地区/种类` 映射固化为 Player 可读取的 JSON。UI 程序集提供一个只读解析器，待部署区和商店共同使用。

未采用以下方案：

- 不扩展完整 `UnitCatalog` schema；地区与种类当前只影响 UI，扩大 Core、加载器和所有 fixture 的修改范围没有必要。
- 不在两个 UI 控制器中分别硬编码映射；这会产生重复数据并使 BONDS 变更难以审计。
- 不在运行时解析 Markdown；Player 构建不保证包含仓库文档。

编辑器测试会核对运行时 JSON 与当前 BONDS 的商店单位、地区和种类章节，防止提交后的两份事实静默漂移。

## 数据模型

新增版本化 Resources JSON，包含：

- 地区定义：稳定 ID、中文名称和图标 Resources 路径；
- 种类定义：稳定 ID、中文名称和可选图标 Resources 路径；
- 单位映射：`TypeId`、必需的种类 ID、可选的地区 ID。

当前 BONDS 已把每个商店单位归入至多一个地区。无地区单位的地区 ID 为空。“其他”种类保留中文名称，但图标路径为空。

解析器输出不可变的 UI 表现对象：

- `TypeId`
- `RegionName`
- `RegionIconResourcePath`
- `OccupationName`
- `OccupationIconResourcePath`
- `PreferredHeaderIconResourcePath`

`PreferredHeaderIconResourcePath` 的选择顺序固定为：

1. 存在地区图标时使用地区图标；
2. 否则存在种类图标时使用种类图标；
3. 两者都没有时为空。

未知 TypeId 不伪造归属；UI 隐藏对应归属元素，并输出一次可定位诊断。

## 资源映射

地区图标：

| 地区 | Resources 路径 |
|---|---|
| 乌萨斯 | `UI/Texture/region/logo_ursus` |
| 莱塔尼亚 | `UI/Texture/region/logo_Leithanien` |
| 维多利亚 | `UI/Texture/region/logo_victoria` |
| 哥伦比亚 | `UI/Texture/region/logo_columbia` |
| 萨尔贡 | `UI/Texture/region/logo_sargon` |
| 阿戈尔 | `UI/Texture/region/logo_egir` |
| 叙拉古 | `UI/Texture/region/logo_siracusa` |
| 整合运动 | `UI/Texture/region/logo_reunionMovement` |

种类图标：

| 种类 | Resources 路径 |
|---|---|
| 感染生物 | `UI/Texture/occupation/r_enemy_slime_repbsl_3` |
| 无人机 | `UI/Texture/occupation/r_defdrn_up_2` |
| 造物 | `UI/Texture/occupation/r_global_magic_resist_2` |
| 机械 | `UI/Texture/occupation/CHIPS` |
| 坍缩体 | `UI/Texture/occupation/logo_sami` |
| 其他 | 空 |

商店部署费用图标从 `UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged` 图集按 Sprite 名 `DeploymentCostPanelIcon` 加载，不创建替代图。

## 待部署槽位

`StagingSlotView` 在 `HeaderLeft` 内增加一个非射线目标 `Image`：

- 有地区的单位显示地区图标；
- 无地区的单位显示种类图标；
- 无可用图标时隐藏；
- `preserveAspect = true`；
- 锚点、轴心和位置均为 `HeaderLeft` 的中心；
- 参考尺寸为 `20 x 20`，与 `HeaderRight` 现有费用图标及数字视觉尺度接近；
- CanvasScaler 继续负责非 1920×1080 分辨率缩放。

绑定新槽位或槽位内容更新时必须同时刷新图标，不得保留上一个单位的 Sprite。

## 商店槽位

每个非空商店槽位在 `PortraitClip` 内新增三个固定行，从下到上依次为：

1. 部署费用；
2. 地区；
3. 种类。

三行保持固定位置；无地区时隐藏地区行，不把种类行下移。“其他”种类显示“其他”文字，图标位置留空。空商店槽位隐藏全部三行。

参考分辨率下，每行使用商店自然尺寸坐标后再应用既有 `ShopVisualScale = 1.5`：

- 行左边距：`6`
- 行宽：`132`
- 行高：`20`
- 三行底边：`4`、`26`、`48`
- 图标尺寸：`18 x 18`
- 图标与文字间距：`4`
- 文字参考字号：`13`

每行采用左侧图标、右侧文字的水平排列。图标与文字共享行中心，所有图标 `preserveAspect = true`。文字左对齐，不拉伸图标填满行高。

部署费用数字来自 `UnitCatalogEntry.DeploymentCost`，通过现有只读 `LocalMatchShopSlotSnapshot` 投影到 UI；它与由稀有度派生的商店购买价格是两个独立值。

字体规则：

- 部署费用纯数字使用 `StagingHudController.FormalNumericFont`；
- 地区和种类中文使用商店现有 `FangZhengHeiTiJianTi-1`；
- 不更换商品名、购买价格和其他现有文字的字体。

绘制顺序保持现有语义：三行位于头像之上，仍可被不可购买遮罩、二次确认效果、稀有度边框和冻结效果覆盖。

## 加载失败与回退

- 地区或种类图标资源缺失时保留对应中文文字，隐藏 Image，并输出带 TypeId、分类 ID 和路径的错误；
- “其他”的空图标路径是已确认配置，不记录资源缺失错误；
- 部署费用图集 Sprite 缺失时保留数字并记录错误；
- 未知单位仅保留既有头像、名称、购买价格等显示，不显示伪造的地区或种类；
- UI 加载失败不得阻止购买、冻结、刷新、部署或其他命令。

## 验证

自动化验证至少覆盖：

1. BONDS 中 88 个商店单位全部存在唯一运行时映射；
2. 每个映射恰有一个种类，且地区数量与 BONDS 一致；
3. 最新 BONDS 中不存在多地区商店单位；
4. 五个有图标种类和八个地区的资源能够加载；
5. “其他”的空图标路径不会被当作错误；
6. HeaderLeft 对有地区单位优先显示地区图标；
7. HeaderLeft 对无地区单位回退到种类图标；
8. 商店三行顺序、层级、字体、Sprite 名、`preserveAspect` 和中心对齐正确；
9. 商店 cost 显示部署费用而不是购买价格；
10. 无地区单位隐藏地区行，“其他”保留文字并隐藏图标；
11. 空槽位隐藏三行，不残留上一商品的数据；
12. 既有 Ready 按钮、购买、刷新、冻结、升级和待部署交互测试无回归。

完成后运行相关 EditMode、PlayMode 测试和正式 HUD 截图入口；检查 1920×1080 商店打开状态以及有地区、无地区待部署单位的实际视觉效果。

## 验收标准

- 待部署区按“地区优先、无地区回退种类”的规则显示单个居中等比图标；
- 商店头像左下角按 cost、地区、种类自下而上显示；
- cost 为部署费用，购买价格仍位于槽位上方；
- 数字和中文分别使用指定字体；
- 图标不变形，且与同一行文字垂直居中；
- 无地区、“其他”、未知单位和空槽位均按本设计稳定回退；
- 不改变任何游戏机制、经济结果或玩家命令行为。
