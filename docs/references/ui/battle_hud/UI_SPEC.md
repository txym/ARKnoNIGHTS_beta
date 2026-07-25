# 战斗界面 UI 规格

## 1. 适用范围

本文描述战斗界面的待部署区（`StagingArea`）、单位槽位（`StagingSlot`）、资源显示区、商店、准备按钮、左侧玩家列表和其他 HUD 元素的基础尺寸、排列与视觉状态。

除单位头像和动态文字外，本节涉及的大部分 UI 贴图来自：

`Assets/Resources/UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged.png`

参考图：

- [图1](./图1.png)：槽位总自然宽度超过屏幕宽度，未选中；
- [图2](./图2.png)：槽位总自然宽度超过屏幕宽度，其中一个槽位被选中；
- [图3](./图3.png)：部署操作中的界面状态，本节不定义其中的灰显、已部署标记、部署范围、技能和拖拽提示；
- [图4](./图4.png)：槽位总自然宽度未超过屏幕宽度，其中一个槽位被选中；
- [图7](./图7.png)：商店槽位、升级按钮、冻结/刷新按钮和左侧玩家列表的主要视觉参考，原图尺寸为 `2106 × 1151`；
- [图8](./图8.png)：玩家等级按钮中等级数字的字号和视觉比例参考，原图尺寸为 `2101 × 1148`。

当前参考分辨率为 `1920 × 1080`。下文先以该分辨率说明，再给出相对尺寸公式。

## 2. 术语与尺寸

### 2.1 参考分辨率与比例换算

本文使用 `1920 × 1080` 作为参考设计分辨率。除明确标注为 Sprite 原始像素或 Unity 世界坐标的数值外，屏幕空间位置和尺寸应按以下方式换算：

```text
NormalizedX = ReferenceX ÷ 1920
NormalizedY = ReferenceY ÷ 1080
ScaledX = NormalizedX × ScreenWidth
ScaledY = NormalizedY × ScreenHeight
ReferenceUiScale = ScreenHeight ÷ 1080
ScaledNativeSize = SourcePixelSize × ReferenceUiScale
```

矩形区域使用相同规则分别换算左、上、宽和高。对于明确以界面高度或父区域尺寸定义的元素，优先使用文中给出的直接比例，不从取整后的参考像素反推。

当前布局以 `16:9` 为主要目标比例。若实际屏幕不是 `16:9`：

- 待部署槽位、右侧资源区和单位信息面板优先保持相对 `ScreenHeight` 的尺寸关系；
- 水平锚定元素继续贴对应屏幕边缘；
- 不通过非等比拉伸头像和图标填补额外空间；
- 超宽或窄屏的最终安全区和遮挡处理仍需单独验证。

### 2.2 待部署区基础尺寸

设：

- `ScreenWidth`：界面可用宽度；参考值为 `1920`；
- `ScreenHeight`：界面可用高度；参考值为 `1080`；
- `StagingAreaHeight`：待部署区高度；
- `PortraitSize`：单位头像的自然边长；
- `PortraitBottomOffset`：头像区域底部相对槽位底边的偏移；
- `CostIconHalfHeight`：费用标记伸出头像区域顶部的高度；
- `SlotContentHeight`：槽位内容实际占用的高度；
- `SlotNaturalWidth`：槽位未压缩时的自然宽度；
- `SlotHeight`：完整槽位高度；
- `SlotCount`：当前显示的槽位数量。

尺寸规则：

```text
PortraitSize = ScreenHeight ÷ 6
SlotNaturalWidth = PortraitSize
PortraitBottomOffset = PortraitSize ÷ 20
CostIconHalfHeight = PortraitSize ÷ 18
SlotContentHeight = PortraitBottomOffset + PortraitSize + CostIconHalfHeight
SlotHeight = PortraitSize × 10 ÷ 9
StagingAreaHeight = SlotHeight
```

在 `16:9` 下，还可以写为：

```text
PortraitSize = 3 × ScreenWidth ÷ 32
SlotNaturalWidth = 3 × ScreenWidth ÷ 32
PortraitBottomOffset = ScreenHeight ÷ 120
CostIconHalfHeight = ScreenHeight ÷ 108
SlotHeight = 5 × ScreenHeight ÷ 27
```

在 `1920 × 1080` 下：

```text
PortraitSize = 180
SlotNaturalWidth = 180
PortraitBottomOffset = 9
CostIconHalfHeight = 10
SlotContentHeight = 9 + 180 + 10 = 199
SlotHeight = 200
StagingAreaHeight = 200
```

此前提到的“StagingSlot 默认长宽为 180”实际指单位头像区域。头像目标尺寸为 `180 × 180`，完整槽位自然尺寸为 `180 × 200`，待部署区高度也为 `200`。槽位内容实际占用 `199` 高度，槽位高度统一取整为 `200`。

`SlotCount` 的取值范围为 `0～13`。待部署区没有独立背景；当 `SlotCount=0` 时，不显示任何待部署槽位或待部署区背景。

`SlotCount` 表示严格堆叠后的槽位数量，不等于单位实例总数。槽位顺序由玩家状态层提供，UI 不自行维护另一份排序：从左到右先按部署费用升序，再按 `type ID` 升序。严格堆叠要求 `type ID`、精英化等级和完整 Buff 状态一致；同费用、同 `type ID` 但不能堆叠的多个槽位之间的最终决胜顺序仍待确认。

## 3. 基础横向布局

先计算全部槽位的自然总宽度：

```text
NaturalTotalWidth = SlotCount × SlotNaturalWidth
```

根据该值是否超过 `ScreenWidth`，使用两种布局模式。

### 3.1 非压缩模式：自然总宽度不超过屏幕

触发条件：

```text
NaturalTotalWidth <= ScreenWidth
```

规则：

- 每个槽位保持自然宽度 `SlotNaturalWidth`；
- 单位头像保持正方形，不横向拉伸；
- 全部槽位整体靠屏幕右侧排列；
- 槽位顺序保持稳定，不因靠右排列而反转。

整体起始位置：

```text
StartX = ScreenWidth - NaturalTotalWidth
```

例如，在 `1920` 宽度下显示 10 个槽位：

```text
NaturalTotalWidth = 10 × 180 = 1800
StartX = 1920 - 1800 = 120
NormalizedStartX = 120 ÷ 1920 = 1 ÷ 16
```

即左侧保留 `120` 的空白，10 个正方形头像靠右排列。参考图4。

### 3.2 压缩模式：自然总宽度超过屏幕

触发条件：

```text
NaturalTotalWidth > ScreenWidth
```

规则：

- 待部署区横向空间由全部槽位共同使用；
- 每个槽位首先获得相同的基础分配宽度；
- 全部槽位从左至右铺满待部署区，不保留额外左右空白；
- 槽位顺序保持稳定。

基础分配宽度：

```text
CompressedBaseWidth = ScreenWidth ÷ SlotCount
```

例如，在 `1920` 宽度下显示 12 个槽位：

```text
CompressedBaseWidth = 1920 ÷ 12 = 160
CompressedBaseWidth = 4 × ScreenHeight ÷ 27（16:9 参考比例）
```

因此每个槽位获得 `160` 的横向空间。参考图1。

压缩槽位允许小于头像的自然宽度，但不得小于：

```text
MinCompressedWidth = PortraitSize × 7 ÷ 9
```

在 `PortraitSize = 180` 时：

```text
MinCompressedWidth = 140
MinCompressedWidth = 7 × ScreenHeight ÷ 54
```

压缩只改变槽位获得的横向显示空间，不得对头像贴图进行非等比缩放。头像应保持原始宽高比和自然尺寸，通过裁切或可显示区域遮罩适配槽位宽度。具体采用裁切还是遮罩属于实现细节，但结果不得使头像变形。

用于表现槽位边框的部分贴图允许随槽位宽度进行横向变形；头像及其他要求保持比例的内容不受该规则影响。

## 4. 选中状态

### 4.1 通用选择规则

- 同一时间最多选中一个 `StagingSlot`；
- 点击未选中的槽位后，该槽位进入 `Selected` 状态；
- 点击另一个槽位时，原槽位取消选中，新槽位进入 `Selected`；
- 再次点击当前已选中的槽位时，取消选中并恢复普通布局；
- 选择操作不得改变槽位的逻辑顺序。

### 4.2 选中后的垂直位移

选中槽位向屏幕上方移动：

```text
SelectedOffsetY = PortraitSize ÷ 9
```

在 `PortraitSize = 180` 时：

```text
SelectedOffsetY = 20
SelectedOffsetY = ScreenHeight ÷ 54
```

该位移只改变视觉位置，不改变待部署区的数据顺序。参考图2和图4。

### 4.3 非压缩模式中的选中槽位

在非压缩模式中：

- 选中槽位继续保持 `SlotNaturalWidth × PortraitSize` 的正方形头像；
- 其他槽位的位置和宽度不变；
- 仅对选中槽位应用向上位移。

参考图4。

### 4.4 压缩模式中的选中槽位

在压缩模式中，选中槽位需要从 `CompressedBaseWidth` 扩展至自然宽度 `SlotNaturalWidth`，使选中头像恢复为正方形。

```text
SelectedWidth = SlotNaturalWidth
RequiredExtraWidth = SelectedWidth - CompressedBaseWidth
```

扩展选中槽位时：

- 待部署区总宽度仍不得超过 `ScreenWidth`；
- 从选中槽位开始向两侧形成槽位宽度的等差过渡；
- 选中槽位最宽，向外侧逐步过渡到当前布局中的最窄槽位；
- 靠近选中槽位的若干槽位允许比未选中时的 `CompressedBaseWidth` 更宽；
- 为维持总宽度，距离更远的槽位相应变窄；
- 任一非选中槽位的最终宽度不得小于 `MinCompressedWidth`；
- 选中第一项或最后一项时，只在实际存在相邻槽位的一侧形成上述过渡，槽位组仍保持在屏幕范围内；
- 槽位顺序不得改变；
- 取消选择后，所有槽位恢复相同的 `CompressedBaseWidth`。

等差数列的项数和公差应根据槽位数量、选中索引和可用总宽度求得，并同时满足选中槽位宽度、总宽度及最小槽位宽度约束。规格只约束最终布局关系，不限定具体求解方法。

在当前最大槽位数为 `13` 的条件下，即使一个选中槽位保持 `180` 宽，其余 12 个槽位全部取最小宽度 `140`，总宽度也只有 `1860`，不会出现无法容纳选中槽位的情况。

## 5. 槽位组成与视觉层级

### 5.1 尺寸基准

槽位和头像区域是两个不同概念：

```text
StagingArea              高度 = 200
└── StagingSlot          自然尺寸 = 180 × 200
    └── PortraitArea     目标尺寸 = 180 × 180
```

对应比例为：

```text
StagingAreaHeight = 10 × PortraitSize ÷ 9
SlotNaturalWidth = PortraitSize
SlotHeight = 10 × PortraitSize ÷ 9
PortraitArea = PortraitSize × PortraitSize
```

单位头像来源于 `Assets/Resources/ProfilePicture`。头像是正方形，目标显示尺寸为 `180 × 180`，同时也是其他槽位元素进行布局时使用的头像区域尺寸。进入压缩布局后，头像仍保持正方形和原始宽高比，通过槽位的可显示区域裁切，不进行横向变形。

`StagingSlotBackground` 的原图尺寸为 `180 × 189`，即自然宽度等于 `PortraitSize`，高度等于 `21 × PortraitSize ÷ 20`。在自然宽度下按原尺寸显示并贴紧槽位底边；在压缩状态下只跟随槽位宽度进行横向缩放，高度仍保持该比例，不纵向填满完整槽位。

`StagingSlotBackground` 底部有一段不属于头像区域的空间：

```text
BackgroundBottomExtraHeight = PortraitSize ÷ 20
```

在 `PortraitSize=180` 时，该空间高度为 `9`。

头像区域位于这段底部空间之上，因此在自然槽位中的纵向范围为：

```text
底部位置 = 9
顶部位置 = 9 + 180 = 189
```

高度为 `20` 的费用标记以头像区域顶部为中心线，向上占用其半高 `10`。槽位内容由此占用 `199` 高度，并包含在高度为 `200` 的槽位中。

### 5.2 层级编号约定

为避免“顶层”和 Unity Hierarchy 顺序产生歧义，本文统一按最终视觉遮挡关系编号：`0` 层最靠近观察者，编号越大越靠后。这里的编号仅用于描述视觉叠放顺序，不强制对应 Unity 的具体 Hierarchy、Canvas 或组件实现。

从前到后的视觉层级如下：

| 视觉层 | 元素 | 显示条件 | 尺寸与位置规则 |
|---:|---|---|---|
| 0 | `StagingSlotStackCountText` | 槽位存在时 | 位于头像区域右下角，样式参考图5；不是图集 Sprite；数量为 1 时也显示 `X1` |
| 1 | `StagingSlotSelectionOverlay` | 槽位被选中时 | 显示尺寸与 `StagingSlotBackground` 相同；选中槽位会恢复自然宽度，因此该贴图不在压缩宽度下显示 |
| 2 | 精英化底部装饰或高亮 | 见 5.3 节 | 贴紧槽位底边，跟随槽位宽度横向缩放 |
| 3 | `StagingSlotCostIcon` 与费用数字 | 始终保留费用显示区域 | 固定尺寸，不随槽位宽度缩放；具体位置见 5.4 节 |
| 4 | 精英化等级图标、单位稀有度图标与顶部信息区背景 | 始终保留顶部信息区；精英化图标四选一；稀有度图标六选一 | 固定尺寸，不随槽位宽度缩放；具体位置见 5.3、5.4 节 |
| 5 | `StagingSlotPortraitOverlay` | 显示头像时 | 玩家可见的头像覆盖贴图，跟随槽位宽度横向缩放 |
| 6 | 单位头像 | 显示单位时 | 来源于 `Assets/Resources/ProfilePicture`，目标尺寸为 `180 × 180`，保持原始宽高比，不随槽位宽度产生非等比变形 |
| 7 | `StagingSlotBackground` | 槽位存在时 | 最底层；自然尺寸 `180 × 189`，贴槽位底边，只跟随槽位宽度横向缩放 |

### 5.3 精英化与稀有度显示

#### 5.3.1 精英化状态

精英化状态决定第 2 层和第 4 层的显示内容：

类型 JSON 保存创建单位时使用的初始精英化等级；单位进入玩家状态后，每个实例独立维护当前精英化等级。槽位 UI 必须读取玩家状态提供的当前值，不得用稀有度推断，也不得在运行时每次回读类型 JSON 覆盖实例状态。

| 精英化状态 | 第 2 层底部效果 | 第 4 层等级图标 |
|---|---|---|
| 精英化 0 | 不显示精英化底部效果 | `StagingSlotElite0Icon` |
| 精英化 1 | `StagingSlotElite1Decoration` | `StagingSlotElite1Icon` |
| 精英化 2 | `StagingSlotElite2PlusHighlight` | `StagingSlotElite2Icon` |
| 精英化 3 | `StagingSlotElite2PlusHighlight` | `StagingSlotElite3Icon` |

`StagingSlotElite1Decoration` 的图集切片原始尺寸为 `550 × 40`，显示时压缩为：

```text
宽度 = 当前槽位宽度
高度 = PortraitSize ÷ 20 = 9
```

该装饰贴紧槽位底边。`StagingSlotElite2PlusHighlight` 在自然宽度为 `180` 的槽位中，目标显示尺寸为 `180 × 32`，即宽度等于当前槽位宽度、参考高度等于 `8 × PortraitSize ÷ 45`；进入压缩布局后只跟随槽位宽度横向变化，高度保持该参考比例。

四张精英化等级图标位于头像左下角，同一时间只显示一张。它们使用共同的底部基线和水平中轴，不随槽位压缩改变自身尺寸。

#### 5.3.2 单位稀有度

单位稀有度读取单位类型 JSON 及单位目录中的 `Rarity`，取值范围为 `1～6`。稀有度是单位类型数据，与单位实例的当前精英化等级相互独立；UI 不得根据精英化等级推断稀有度，也不得根据稀有度推断精英化等级。

稀有度图标映射如下：

| `Rarity` | 图标资源 |
|---:|---|
| 1 | `Assets/Resources/UI/Texture/UnitRarity1Icon.png` |
| 2 | `Assets/Resources/UI/Texture/UnitRarity2Icon.png` |
| 3 | `Assets/Resources/UI/Texture/UnitRarity3Icon.png` |
| 4 | `Assets/Resources/UI/Texture/UnitRarity4Icon.png` |
| 5 | `Assets/Resources/UI/Texture/UnitRarity5Icon.png` |
| 6 | `Assets/Resources/UI/Texture/UnitRarity6Icon.png` |

六张贴图的原始尺寸均为 `45 × 45`。在 `PortraitSize=180` 时按原始尺寸显示：

```text
RarityIconSize = PortraitSize ÷ 4 = 45
RarityIconRight = 0
RarityIconTop = 0
```

待部署槽位中的稀有度图标位于第 4 视觉层，锚定在当前可见头像区域的右上角。图标保持 `45 × 45` 和原始宽高比，不随槽位宽度横向压缩：

- 自然宽度和选中状态下，图标贴齐 `180 × 180` 头像区域的右上角；
- 压缩状态下，图标跟随槽位的可见头像裁剪区域右边缘移动，不继续锚定在被裁掉的原始头像右边缘；
- 图标必须完整保持在槽位可见范围内，不得被头像裁剪遮罩裁掉；
- 图标与头像顶部中央的费用区域可以同时显示；二者不得因共用锚点而互相覆盖。

当 `Rarity` 缺失或不在 `1～6` 范围内时，不显示错误等级的替代图标，并输出可定位单位 `typeId` 的结构化错误；不得截断到最近等级或默认显示 1 级图标。

### 5.4 顶部信息区与费用

头像顶部中央有一个总宽度为 `90` 的信息区，左右两半各宽 `45`：

```text
HeaderWidth = PortraitSize ÷ 2
HeaderHalfWidth = PortraitSize ÷ 4
```

- 左半区预留给单位阵营，本阶段暂不显示具体内容；
- 右半区显示部署费用；
- 两半都使用 `StagingSlotHeaderHalfBackground`；
- 图集中只提供右半区贴图，左半区使用同一贴图水平翻转得到；
- 两半贴图组合后整体水平居中，并贴着头像顶部；
- 顶部信息区不随槽位压缩改变自身尺寸。

第 3 层包含：

- `StagingSlotCostIcon`：费用标记贴图；
- `StagingSlotCostText`：具体费用数字，不是图集 Sprite。

`StagingSlotCostIcon` 的中心与右半区 `StagingSlotHeaderHalfBackground` 的顶部中点对齐。在自然宽度为 `180` 的槽位中，图标尺寸为 `20 × 20`，即边长为 `PortraitSize ÷ 9`。费用数字显示在右半区内；左半区当前只显示翻转后的背景，不显示阵营图标或文字。

精英化图标锚定在头像区域左下角，并保留 `PortraitSize ÷ 30` 的左、下边距；在 `1920 × 1080` 下即为 `(6, 6)`。部署费用面板的数字与 `DeploymentCostPanelIcon` 使用相同的垂直中心 `y=40`。

### 5.5 堆叠数量

堆叠数量位于第 0 层，即所有槽位视觉元素之前，锚定在头像区域右下角。显示形式参考图5中的 `X5`，数量为 `1` 时也显示 `X1`。

堆叠数量是动态文字，不是固定 Sprite。字体、字号和边距仍待后续补充。

### 5.6 待部署槽位 Sprite 命名

为统一大小写、去除下划线混用，并让名称表达用途，建议使用以下名称：

| 原名称 | 当前名称 | 用途 |
|---|---|---|
| `StagingSlotBackground` | 保持不变 | 槽位底层背景 |
| `StagingSlot_Elitism_I_decorate` | `StagingSlotElite1Decoration` | 精英化 1 底部装饰 |
| `StagingSlotHighlight` | `StagingSlotElite2PlusHighlight` | 精英化 2、3 共用高亮 |
| `StagingSlotSelectionOverlay` | 保持不变 | 选中效果 |
| `Elitism_0_icon` | `StagingSlotElite0Icon` | 精英化 0 图标 |
| `Elitism_I_icon` | `StagingSlotElite1Icon` | 精英化 1 图标 |
| `Elitism_II_icon` | `StagingSlotElite2Icon` | 精英化 2 图标 |
| `Elitism_III_icon` | `StagingSlotElite3Icon` | 精英化 3 图标 |
| `StagingSlot_MidBackground` | `StagingSlotHeaderHalfBackground` | 顶部信息区半幅背景 |
| `StagingSlotDeploymentCost` | `StagingSlotCostIcon` | 费用标记图标 |
| `StagingSlotMask` | `StagingSlotPortraitOverlay` | 玩家可见的头像覆盖贴图 |

以上映射已经应用到图集的 `.png.meta`。重命名只改变 Sprite Editor 中显示的切片名称，并同步更新 `nameFileIdTable`；各切片的矩形、`spriteID` 和 `internalID` 保持不变。

## 6. 右侧资源显示区

### 6.1 坐标系和公共尺寸

本节使用界面右下角作为原点：

- `x=0` 位于界面最右侧，`x` 向左增大；
- `y=0` 位于界面最底部，`y` 向上增大；
- 以下数值均以 `1920 × 1080` 参考分辨率下的 UI 设计坐标表示。

两个资源显示区均与自然状态下的待部署槽位等宽，长宽比为 `9:4`：

```text
ResourcePanelWidth = SlotNaturalWidth = 180
ResourcePanelHeight = ResourcePanelWidth × 4 ÷ 9 = 80
ResourcePanelWidth = ScreenHeight ÷ 6
ResourcePanelHeight = 2 × ScreenHeight ÷ 27
```

参考图1～图4可以用于比对字体大小、数字位置和整体视觉比例；本节明确写出的尺寸和坐标是布局实现的权威数据。

### 6.2 部署费用区

部署费用区（`DeploymentCostPanel`）位于界面最右侧、待部署区上方。下方不再放置“剩余可放置角色数”UI，只保留选中槽位向上移动所需的空间。

```text
DeploymentCostPanelBottom = StagingAreaHeight + SelectedOffsetY
                          = 200 + 20
                          = 220
                          = 11 × ScreenHeight ÷ 54
```

区域范围为：

```text
横向：x = 0～180
纵向：y = 220～300
右下角：(0, 220)
左上角：(180, 300)
```

相对屏幕高度的比例为：

```text
PanelRight = 0
PanelWidth = ScreenHeight ÷ 6
PanelBottom = 11 × ScreenHeight ÷ 54
PanelTop = 5 × ScreenHeight ÷ 18
```

显示规则：

- 背景使用图集 Sprite `ResourcePanelBackground`，拉伸至 `180 × 80` 并填满区域；
- 部署费用图标使用图集 Sprite `DeploymentCostPanelIcon`；
- `DeploymentCostPanelIcon` 的原始尺寸为 `46 × 46`，在 `1920 × 1080`、宽度为 `180` 的资源区中按原始尺寸显示；其他分辨率使用 `46 × 46 × ReferenceUiScale`；
- `DeploymentCostPanelIcon` 的中心位于部署费用区的垂直中线；在参考分辨率下全局 UI 坐标为 `(140,260)`。横向位置等于从右侧起算的面板宽度 `7/9`，纵向位置等于部署费用区上下边界的中点；
- 部署费用数字显示当前玩家可用的部署费用。

当前本地 UI Demo 的玩家初始可用部署费用为 `99`。部署成功扣除费用，撤退成功返还 `100%` 已占用费用；准备/战斗阶段切换不把该数值重置为 `99`。该面板必须绑定玩家状态，不自行增减显示文本。

### 6.3 赤金区

赤金区（`GoldCurrencyPanel`）与部署费用区尺寸相同，并紧贴在其正上方：

```text
横向：x = 0～180
纵向：y = 300～380
右下角：(0, 300)
左上角：(180, 380)
```

相对屏幕高度的比例为：

```text
PanelRight = 0
PanelWidth = ScreenHeight ÷ 6
PanelBottom = 5 × ScreenHeight ÷ 18
PanelTop = 19 × ScreenHeight ÷ 54
```

显示规则：

- 背景同样使用 `ResourcePanelBackground`，拉伸至 `180 × 80`；
- 赤金图标使用 `Assets/Resources/UI/Texture/round_sources_icon.png`；
- 赤金图标原始尺寸为 `48 × 37`，在参考分辨率下按原始尺寸显示；其他分辨率使用 `48 × 37 × ReferenceUiScale`；
- 赤金图标沿用部署费用图标的相对位置，中心位于全局 UI 坐标 `(140,380)`；横向位置同样等于从右侧起算的面板宽度 `7/9`，纵向位置等于 `19 × ScreenHeight ÷ 54`；
- 赤金数字沿用部署费用数字的位置、字体和字号，仅将文字颜色改为黄色；
- 该区域显示玩家当前持有的赤金数量。

## 7. 左上角设置按钮

整个界面左上角显示设置按钮（`SettingsButton`），按钮贴图使用图集 Sprite `SettingsButtonIcon`。

`SettingsButtonIcon` 当前图集切片的原始尺寸为 `187 × 161`。贴图自身包含阴影和透明留白，因此切片矩形边界不能直接代表玩家看到的按钮主体边界，也不能仅凭原始宽高准确确定最终位置。

当前只确认以下规则：

- 设置按钮锚定在整个界面的左上角，而不是战场、待部署区或其他局部面板；
- 使用现有 `SettingsButtonIcon`，暂不裁切、重绘或删除阴影；
- 精确显示尺寸和位置以 `1920 × 1080` 下的图1～图4为视觉参考，由实现 Agent 在场景中逐步比对；
- 比对时应以不含阴影的按钮主体位置为主要依据，同时检查阴影没有被屏幕边界裁掉；
- 在完成实际场景比对前，不得把暂定坐标描述为已经确认。

实现 Agent 确定最终布局后，应在本文补充：

- `SettingsButton` 的锚点、轴心和 `anchoredPosition`；
- `SettingsButtonIcon` 的最终显示尺寸；
- 点击区域是使用完整贴图矩形，还是只覆盖可见按钮主体；
- `1920 × 1080` 下与参考图的人工比对结果。

## 8. 顶部游戏状态栏

### 8.1 区域和背景

游戏状态栏（`BattleStatusPanel`）位于整个界面顶部中央，参考图1～图4。在 `1920 × 1080` 参考图中，其大致像素范围为：

```text
左上角：(537, 14)
右下角：(1367, 68)
参考宽度：830
参考高度：54
```

对应的屏幕比例为：

```text
PanelLeft = 537 ÷ 1920 × ScreenWidth = 179 ÷ 640 × ScreenWidth
PanelTop = 14 ÷ 1080 × ScreenHeight = 7 ÷ 540 × ScreenHeight
PanelWidth = 830 ÷ 1920 × ScreenWidth = 83 ÷ 192 × ScreenWidth
PanelHeight = 54 ÷ 1080 × ScreenHeight = ScreenHeight ÷ 20
PanelCenterX = 952 ÷ 1920 × ScreenWidth = 119 ÷ 240 × ScreenWidth
PanelCenterOffsetFromScreenCenter = -8 ÷ 1920 × ScreenWidth
                                  = -ScreenWidth ÷ 240
```

因此实现时优先采用顶部居中锚点，并以 `PanelCenterX`、`PanelTop`、`PanelWidth` 和 `PanelHeight` 作为初始拟合值。

上述坐标来自参考图的视觉测量，用于后续拟合，并非已经在 Unity 场景中验证的最终 `RectTransform` 数值。

该组参考图坐标使用图片左上角作为原点：`x` 向右增大，`y` 向下增大。此坐标系只用于屏幕空间 HUD 的参考图测量，不与第 6 节从右下角起算的局部资源区坐标混用。

状态栏背景使用图集 Sprite `BattleStatusPanelBackground`。当前切片原始尺寸为 `831 × 60`，贴图自身已经包含三个区域及其分隔线；实现时应使用贴图已有的分隔，不重复绘制新的分隔线。

三个区域从左到右依次显示：

1. 初始敌人击败进度；
2. 倒计时；
3. 玩家生命。

各区域内部图标和文字的精确坐标、字号与间距需要参照图1～图4进行视觉拟合。

### 8.2 初始敌人击败进度

状态栏左侧根据当前阶段切换显示内容。在战斗阶段显示：

```text
已击败的初始敌人数 / 本场总初始敌人数
```

图标使用图集 Sprite `BattleStatusPanelEnemyCountIcon`，当前切片原始尺寸为 `68 × 60`。

“初始敌人”指战斗开始时已经包含在敌方战斗输入中的单位。分母在本场战斗开始时确定；分子随这些初始敌人死亡而增加。战斗过程中后续生成的其他单位是否计入该数值，需由对应生成机制另行定义，当前不自行纳入。

进入非战斗阶段后，左侧不再显示 `BattleStatusPanelEnemyCountIcon` 和敌人击败进度，改为显示下一战斗阶段的对战对手。当前计划使用纯文字显示，不额外要求对手图标。对手文字的具体字段和格式后续补充。

### 8.3 倒计时

状态栏中间显示倒计时及其图标。

倒计时图标使用以下图集中的 `BattleStatusPanelClockIcon` Sprite：

`Assets/Resources/UI/Texture/SpriteAtlasTexture-CHARACTER_SORT_TYPE_ICON_0-128x128-fmt34_alpha.png`

`BattleStatusPanelClockIcon` 当前切片原始尺寸为 `22 × 22`。源图当前是完全不透明的黑底白色图标，不能直接作为透明背景图标使用。目标效果是保留时钟图形、移除黑色背景，并在状态栏中显示为灰色。

资源处理要求：

- 不直接覆盖或破坏现有源图；
- 使用源图亮度生成 Alpha，使黑色背景透明并保留图标轮廓；
- 通过 UI 颜色或派生资源把保留下来的图标染成目标灰色，不进行普通 RGB 反色；
- 先只对 `BattleStatusPanelClockIcon` 切片制作处理小样，验证透明度、灰色效果和边缘抗锯齿结果；
- 如果确认同一源图内所有图标都需要相同处理，再考虑生成整张图的派生版本；
- 不得在未检查其他切片用途的情况下批量替换原资源；
- 处理后的图标需要在状态栏背景上进行实际可读性检查。

当前本地 UI Demo 的准备阶段倒计时固定为 `30` 秒。场景自动加载成功后进入准备阶段；倒计时归零后进入战斗阶段；战斗演示完成并返回准备阶段时重新从 `30` 秒开始。战斗阶段及后续其他阶段是否具有独立倒计时、其初始值和显示格式仍待确认，不得把准备阶段的 `30` 秒硬编码成所有阶段的时长。

### 8.4 玩家生命

状态栏右侧显示玩家生命及其图标。图标使用图集 Sprite `BattleStatusPanelPlayerHealthIcon`，当前切片原始尺寸为 `60 × 53`。

当前本地 UI Demo 的玩家初始生命为 `400`。当前阶段仍不包含完整的玩家生命扣除和淘汰规则，因此本节只确定初始显示、布局用途和贴图来源，不把玩家生命变化描述为已经实现。扣除条件和显示格式仍需在相应游戏机制确认后补充。未接入占位文本使用参考图采样的浅红色 `#FF7878`。

## 9. 选中单位信息面板

### 9.1 显示条件、范围和层级

玩家选中可查看的单位时，界面左侧显示单位信息面板（`UnitInformationPanel`），参考图2～图4。未选中单位时隐藏该面板。

以下单位被选中时都显示该面板：

- 待部署区中的己方单位；
- 部署区中的己方单位；
- 战场上的敌方单位。

查看敌方单位信息只切换信息面板内容，不赋予玩家对敌方单位执行部署、撤退或其他己方单位操作的权限。

面板从界面左边缘开始，并铺满整个界面高度。在 `1920 × 1080` 参考分辨率下：

```text
PanelHeight = ScreenHeight = 1080
PanelWidth ≈ ScreenHeight × 2 ÷ 3 ≈ 720
PanelWidth = 3 × ScreenWidth ÷ 8（16:9）
UpperSectionHeight = 21 × ScreenHeight ÷ 40 = 567
LowerSectionHeight = 19 × ScreenHeight ÷ 40 = 513
UpperSectionHeight + LowerSectionHeight = 1080
```

单位信息面板的渲染层级确认位于设置按钮、待部署区和顶部状态栏之下。面板仍然铺满左侧屏幕高度；设置按钮、顶部状态栏和待部署槽位显示在它前面，不因面板出现而隐藏。

面板宽度 `720` 是根据“约为界面高度的三分之二”和参考图得到的初始值，最终宽度及内部横向排版需要在场景中视觉拟合。

### 9.2 上下背景区域

单位信息面板由上下两部分组成：

| 区域 | 参考纵向范围 | 目标高度 | 背景 Sprite | 原始切片尺寸 |
|---|---:|---:|---|---:|
| 上半部分 | `y=0～21/40 × ScreenHeight` | `21/40 × ScreenHeight` | `UnitInformationPanelUpperBackground` | `385 × 542` |
| 下半部分 | `y=21/40～1 × ScreenHeight` | `19/40 × ScreenHeight` | `UnitInformationPanelLowerBackground` | `563 × 290` |

两个背景贴图都需要适配约 `720` 的目标面板宽度和各自的目标高度。具体使用直接拉伸、九宫格还是其他保持纹理细节的方式，需要在实际显示中检查后决定。

相关 Sprite 已统一使用正确拼写 `Information`，不再沿用旧名称中的 `Imformation`。

### 9.3 上半部分内容

参考图2～图4中的原始单位立绘和上半部分信息排版不作为本项目目标。本项目改为严格参考图6右侧单位详情区域的布局密度、字体层级、词条间距和左右两列组织方式。图6只提供排版参考；其中与本项目无关的编号、评级字母、原作属性和说明文字不得照搬。

#### 9.3.1 信息顺序

上半部分从上到下按以下顺序显示：

1. 第一行只显示单位名称；
2. 第二行显示攻击方式和伤害类型，当前格式为 `近战 伤害类型`，其中“伤害类型”替换为该单位实际的 `物理`、`法术` 或 `真实`；
3. 第三行左侧显示单位头像，右侧显示两列属性词条；
4. 单位头像右下角叠加精英化等级图标；
5. 单位头像右上角叠加单位稀有度图标；
6. 单位头像下方显示目标价值词条。

单位头像沿用 `Assets/Resources/ProfilePicture` 中的头像资源。精英化等级和单位稀有度沿用本文件第 5.3 节定义的状态、数据来源和图标映射。精英化图标与稀有度图标都属于头像覆盖层，不额外占用属性词条的宽度；信息面板内的稀有度图标固定在头像右上角，精英化图标固定在头像右下角。

当前阶段只实现近战单位，因此第二行的攻击方式显示为 `近战`。后续接入远程单位时必须读取单位实际 `AttackMethod`，不得继续硬编码为近战。伤害类型始终读取单位实际 `DamageType`。

#### 9.3.2 图6排版比例

图6只关注用户指定的右侧单位详情矩形。图6尺寸为 `1920 × 1080`，该矩形使用图片右下角为原点：`x` 向左增加，`y` 向上增加；两个参考角约为：

```text
左上角（右下原点坐标） = (880, 880)
右下角（右下原点坐标） = (50, 460)
ReferenceRegionWidth = 880 - 50 = 830
ReferenceRegionHeight = 880 - 460 = 420
```

换算为常见的图片左上角原点坐标后，该区域约为：

```text
左上角 = (1920 - 880, 1080 - 880) = (1040, 200)
右下角 = (1920 - 50, 1080 - 460) = (1870, 620)
```

不得把图6整个右半屏、整张图片或信息面板上半部分都当成需要放大的参考内容。只对上述 `830 × 420` 区域做统一等比缩放；横纵方向必须使用同一个缩放值，不得为了填满 `567` 高的上半部分而单独放大纵向尺寸。

该区域内头像约为 `211 × 211`，两列属性区域中单个词条约为 `291 × 60`，列间距和行间距约为 `12`。映射到本项目 `720` 宽的信息面板时，统一使用：

```text
Figure6LayoutScale = PanelWidth ÷ ReferenceRegionWidth
                   = 720 ÷ 830
                   ≈ 0.8675
TargetReferenceRegionHeight = 420 × Figure6LayoutScale
                            ≈ 364.3
```

在 `1920 × 1080` 下，该等比缩放区域的首版目标纵向范围约为 `y=90～454`，不会铺满上半部分。首版数值如下；其他分辨率继续乘以 `ReferenceUiScale`：

```text
ContentLeft = ContentRight = PanelWidth ÷ 60 = 12

UnitNameLeft = 215
UnitNameTop = 90
UnitNameHeight = 43
UnitNameFontSize ≈ 36

CombatTypeLeft = UnitNameLeft
CombatTypeTop = 148
CombatTypeHeight = 31
CombatTypeFontSize ≈ 26

MainContentTop ≈ 200
PortraitLeft = 12
PortraitSize = PanelWidth ÷ 4 = 180
PortraitRarityIconSize = PortraitSize ÷ 4 = 45
PortraitRarityIconRight = 0
PortraitRarityIconTop = 0
PortraitToStatsGap ≈ 23

StatColumnGap ≈ 10
StatEntryWidth = (PanelWidth - 2 × ContentLeft - PortraitSize
                  - PortraitToStatsGap - StatColumnGap) ÷ 2
               = 241.5
StatEntryHeight ≈ 52
StatRowGap ≈ 10
```

第一、第二行从属性区的左边缘开始，而不是压在左上角设置按钮下面。第三行头像从内容区左边缘开始。上述布局使四行属性词条在 `y≈200～438` 内完成，头像和目标价值词条在 `y≈200～445` 内完成，并在 `y=490` 的信息面板血条之前保留约 `45` 像素的间隔。

两列属性词条按行对齐，每个词条内部参考图6使用相同结构：

```text
图标中心距词条左边 ≈ 30
属性名称左边距 ≈ 50
数值右边距 ≈ 20
属性名称字号 ≈ 22
属性数值字号 ≈ 28
```

词条背景允许按各自目标矩形拉伸。图标保持宽高比，不随背景做非等比变形。字体、字重、行高和实际视觉边界必须通过 `1920 × 1080` 截图与图6指定的 `830 × 420` 区域逐项比对；不得通过放大整组 UI 来弥补字体或间距不准确。上述数值是由参考图测量得到的首版拟合值，不得描述为已经完成 Unity 运行验证的最终值。

#### 9.3.3 属性顺序和显示来源

右侧两列按“从左到右、再从上到下”的顺序排列：

| 行 | 左列 | 右列 |
|---:|---|---|
| 1 | 生命值 | 移动速度 |
| 2 | 攻击力 | 攻击间隔 |
| 3 | 防御力 | 法术抗性 |
| 4 | 阻挡数 | 部署费用 |

头像下方显示第九个词条“目标价值”。部署费用词条显示当前单位自身的部署费用，不显示玩家当前剩余部署费用。

生命值、移动速度、攻击力、攻击间隔、防御力和法术抗性必须显示该单位在当前单局及当前战斗时点的实际属性，而不是 UI 直接重新读取原始 JSON 后自行推算：

- 生命值属性词条显示经过当前单局效果和 Buff 修正后的 `MaxHP`；剩余 `CurrentHP` 继续由第 9.4 节的血条和 `CurrentHP / MaxHP` 数字显示；
- 移动速度显示当前实际移动速度，权威单位为米/秒；
- 攻击力显示当前实际攻击力；
- 攻击间隔显示当前实际的两次攻击动画开始时间间隔，权威单位为秒；
- 防御力显示当前实际防御力；
- 法术抗性显示当前实际法术抗性。

上述六项在没有 Buff 时通常与单位 JSON 的对应基础配置相同，但 UI 必须读取只读的单局/战斗运行时属性快照，以便 Buff 生效后立即显示修改后的值。UI 不得自行解释 Buff，也不得反向修改战斗状态。如果当前状态层尚未提供某项运行时属性，先显示明确占位值 `--`，不得在战斗中静默回退到可能已经过期的 JSON 基础值。

阻挡数、部署费用和目标价值按单位数据中对应的配置值显示，不参与上述六项运行时属性投影。目标价值读取单位 JSON 的 `LifeDeduct`。

数值格式遵循以下最小规则：

- 整数属性不添加评级字母；
- 移动速度和攻击间隔保留表达当前数据所需的小数，不为了排版改变权威数值；
- 法术抗性只显示数值，不附加 `%`；
- 攻击间隔只显示数值，不附加 `s`；
- 数值过长时优先缩小数值字号或使用经确认的紧凑格式，不压缩图标和属性名称。

#### 9.3.4 词条背景和图标

生命值、移动速度、攻击力、攻击间隔、防御力、法术抗性、阻挡数、部署费用和目标价值九个词条共用 `Assets/Resources/UI/Texture/unit_panal.png` 中的 Sprite `UnitInformationPanelStatEntryBackground`。右侧八个词条使用相同目标尺寸；目标价值词条与头像等宽，仍使用同一背景 Sprite。

图标映射如下：

| 词条 | 资源或 Sprite |
|---|---|
| 生命值 | `Assets/Resources/UI/Texture/UnitInformationPanelHealthIcon.png` |
| 移动速度 | `unit_panal.png` / `UnitInformationPanelMoveSpeedIcon` |
| 攻击力 | `Assets/Resources/UI/Texture/UnitInformationPanelAttackIcon.png` |
| 攻击间隔 | `Assets/Resources/UI/Texture/UnitInformationPanelAttackIntervalIcon.png` |
| 防御力 | `Assets/Resources/UI/Texture/UnitInformationPanelDefenseIcon.png` |
| 法术抗性 | `Assets/Resources/UI/Texture/UnitInformationPanelMagicResistanceIcon.png` |
| 阻挡数 | `Assets/Resources/UI/Texture/UnitInformationPanelBlockCountIcon.png` |
| 部署费用 | `Assets/Resources/UI/Texture/UnitInformationPanelDeploymentCostIcon.png` |
| 目标价值 | `unit_panal.png` / `UnitInformationPanelTargetValueIcon` |

七张独立属性图标来自 `SpriteAtlasTexture-CHARACTER_SORT_TYPE_ICON_0-128x128-fmt34_alpha.png` 的已处理切片。原始图集中的其他未识别切片名称保持不变；信息面板只依赖上表中的明确资源，不按原始图集的自动编号在运行时猜测图标。

### 9.4 上半部分血条

经图2～图4的截图复核，血条在距离界面顶部 `490` 的位置显示；该坐标对应血条上边缘。血条贴着界面左边缘：

```text
HealthBarLeft = 0
HealthBarTop = 49 × ScreenHeight ÷ 108 = 490
HealthBarWidth = 556
HealthBarHeight = 12
```

血条规则：

- 背景使用 `UnitInformationPanelHealthBarBackground`，原始切片尺寸为 `556 × 12`；
- 当前生命填充使用 `UnitInformationPanelHealthBarFill`，原始切片尺寸为 `556 × 12`；
- 背景和填充保持原始长度 `556`、厚度 `12`；
- 填充从左向右显示；
- `FillWidth = 556 × CurrentHP ÷ MaxHP`；
- 本信息面板血条不显示护盾；
- 生命填充只表现生命值，不把护盾计入长度或颜色。

血条右端显示详细生命数字，格式为：

```text
CurrentHP / MaxHP
```

数字背景使用 `UnitInformationPanelHealthValueBackground`，原始切片尺寸为 `149 × 50`。该生命数字区域使用左上轴心，贴齐生命填充右上角；其左上角以填充右端作为锚定位置：

```text
HpNumberPanelLeft = HealthBarLeft + FillWidth
```

若生命数字背景保持原始 `149 × 50` 的参考显示尺寸，则其缩放尺寸为：

```text
HpNumberPanelWidth = 149 × ReferenceUiScale
HpNumberPanelHeight = 50 × ReferenceUiScale
```

在 1920×1080 参考布局中，`HealthValue` 的锚点、轴心和位置分别为左上、`(0, 1)`、`(FillWidth, -490)`。生命值接近 `0` 或满值时是否需要限制移动范围，仍需视觉拟合。

### 9.5 下半部分页签

下半部分顶部显示一行三页切换栏。本项目使用以下三个页面：

1. 技能；
2. 阵营；
3. 种族。

同一时间只激活一个页面。点击其他页签后切换下半部分显示内容，不改变当前选中的单位。

页签第一版使用纯文字和颜色区分选中状态，不依赖 `UnitInformationPanelTabInactiveDecoration` 或 `UnitInformationPanelTabActiveDecoration`：

- `UnitInformationPanelTabInactiveDecoration` 当前是 `270 × 17` 的细长切片，不作为单个页签图标；
- `UnitInformationPanelTabActiveDecoration` 只有名称映射而没有有效 Sprite 切片，不作为当前实现依赖；
- 三个页签使用相同的文字布局，通过文字颜色或简单背景颜色区分当前激活页面；
- 切换页签不得改变当前选中的单位。

三个页面的具体内容、默认激活页面、选中和未选中的颜色，以及是否需要切换动画后续补充。

## 10. 部署单位选择反馈

### 10.1 表现形式和空间归属

玩家选中部署区中的单位时，在该单位附近显示部署单位选择效果（`DeployedUnitSelectionIndicator`），参考图3。

该效果不属于屏幕空间 Canvas UI，而是与选中单位绑定的世界空间表现对象：

- 选择效果跟随当前选中的部署单位；
- 单位取消选中或改为选中其他单位时，原选择效果应消失或转移；
- 该对象只负责表现和交互入口，不应改变单位的权威部署数据；
- 初始位置估计为选中单位上方约 `200` Unity 世界坐标单位，即当前 `100` 世界单位格长的 `2` 倍；
- `200` 是后续场景拟合的起始值，不是已经完成视觉验证的最终坐标。

实现时应先确认单位表现所使用的世界坐标轴，再把“上方”换算为明确的局部偏移量，不得仅因变量名方便而默认使用某一世界轴。

### 10.2 选择效果贴图

选择效果使用图集 Sprite `DeployedUnitSelectionOverlay`。当前切片原始尺寸为 `924 × 924`。

`DeployedUnitSelectionOverlay` 的原始切片内容已经是菱形，不需要在运行时再次旋转。表现对象应直接保持贴图当前方向；额外旋转 `45°` 会造成错误方向。

该贴图的最终世界尺寸、与单位的精确偏移和渲染排序尚未数值化，应在场景中参照图3逐步拟合。拟合时以可见菱形边线相对单位的位置为准，不以包含透明像素的完整切片矩形为准。

### 10.3 撤退入口

菱形左上边的中点位置显示可交互的撤退按钮，使用图集 Sprite `ReturnToStagingButtonIcon`。当前切片原始尺寸为 `204 × 210`。

定位规则：

- 撤退图标以菱形左上边的几何中点作为初始锚定位置；
- 图标应与选择效果作为同一组表现，跟随当前选中单位；
- 图标需要提供可点击区域，并能够发起当前选中单位的撤退操作；
- `ReturnToStagingButtonIcon` 自身带有阴影和透明留白，最终对齐应以玩家看到的图标主体为准；
- 图标相对菱形边线的精确偏移需要在场景中参照图3进行视觉拟合；
- 在完成拟合前，不把切片矩形中心与可见主体中心视为必然一致。

若菱形可见边界的宽和高均为 `DiamondSize`，且局部原点位于菱形中心，则左上边几何中点的初始局部位置为：

```text
RetreatAnchorX = -DiamondSize ÷ 4
RetreatAnchorY = +DiamondSize ÷ 4
```

该公式只确定几何锚点；带阴影图标的最终视觉修正量仍需通过图3拟合。

点击撤退按钮后，将当前选中单位从部署区撤回待部署区。具体容量检查、堆叠处理、部署费用返还和失败行为以 `docs/SPEC.md` 的“将部署区单位撤回待部署区”规则为准。世界空间按钮只负责发起操作，不直接绕过业务规则修改单位数据。

当前确认点击撤退按钮后不进行二次确认。按钮发起一次状态层撤退命令；容量或堆叠检查失败时保持单位、阵型和费用不变。

## 11. 其他 Sprite 命名

本轮新增 UI 使用以下统一命名。规则与待部署槽位一致：使用 PascalCase，不混用下划线，以所属组件开头并以具体用途结尾。

| 原名称 | 当前名称 | 用途 |
|---|---|---|
| `SettingIcon` | `SettingsButtonIcon` | 左上角设置按钮 |
| `CostBackground` | `ResourcePanelBackground` | 部署费用和赤金区共用背景 |
| `CostIcon` | `DeploymentCostPanelIcon` | 部署费用区图标 |
| `BattleStatusPanel_Background` | `BattleStatusPanelBackground` | 顶部状态栏背景 |
| `BattleStatusPanel_EnemyCountII` | `BattleStatusPanelEnemyCountIcon` | 初始敌人击败进度图标 |
| `BattleStatusPanel_PlayerHP` | `BattleStatusPanelPlayerHealthIcon` | 玩家生命图标 |
| `Clock` | `BattleStatusPanelClockIcon` | 状态栏倒计时图标 |
| `UnitImformationPanel_BackgroundUp` | `UnitInformationPanelUpperBackground` | 单位信息面板上半背景 |
| `UnitImformationPanel_BackgroundDown` | `UnitInformationPanelLowerBackground` | 单位信息面板下半背景 |
| `UnitImformationPanel_hp_background` | `UnitInformationPanelHealthBarBackground` | 单位信息血条背景 |
| `UnitImformationPanel_hp_fill` | `UnitInformationPanelHealthBarFill` | 单位信息当前生命填充 |
| `UnitImformationPanel_hp_num` | `UnitInformationPanelHealthValueBackground` | 详细生命数字背景 |
| `UnitImformationPanel_UnSwitchIcon` | `UnitInformationPanelTabInactiveDecoration` | 页签未激活装饰候选切片 |
| `UnitImformationPanel_SwitchIcon` | `UnitInformationPanelTabActiveDecoration` | 缺失切片的残留名称映射 |
| `unit_panal_back` | `UnitInformationPanelStatEntryBackground` | 九个属性词条共用背景 |
| `unit_panal_targetValue` | `UnitInformationPanelTargetValueIcon` | 目标价值图标 |
| `movespeed` | `UnitInformationPanelMoveSpeedIcon` | 移动速度图标 |
| `icon_sort_hp.png` | `UnitInformationPanelHealthIcon.png` | 生命值图标 |
| `icon_sort_atk.png` | `UnitInformationPanelAttackIcon.png` | 攻击力图标 |
| `icon_sort_atkspeed.png` | `UnitInformationPanelAttackIntervalIcon.png` | 攻击间隔图标 |
| `icon_sort_def.png` | `UnitInformationPanelDefenseIcon.png` | 防御力图标 |
| `icon_sort_res_def.png` | `UnitInformationPanelMagicResistanceIcon.png` | 法术抗性图标 |
| `icon_sort_block.png` | `UnitInformationPanelBlockCountIcon.png` | 阻挡数图标 |
| `icon_sort_cost.png` | `UnitInformationPanelDeploymentCostIcon.png` | 单位部署费用图标 |
| `img_chess_level_1.png` | `UnitRarity1Icon.png` | 稀有度 1 共享图标 |
| `img_chess_level_2.png` | `UnitRarity2Icon.png` | 稀有度 2 共享图标 |
| `img_chess_level_3.png` | `UnitRarity3Icon.png` | 稀有度 3 共享图标 |
| `img_chess_level_4.png` | `UnitRarity4Icon.png` | 稀有度 4 共享图标 |
| `img_chess_level_5.png` | `UnitRarity5Icon.png` | 稀有度 5 共享图标 |
| `img_chess_level_6.png` | `UnitRarity6Icon.png` | 稀有度 6 共享图标 |
| `DeploymentAreaSelectionOverlay` | `DeployedUnitSelectionOverlay` | 已部署单位选择菱形 |
| `BackToStagingArea` | `ReturnToStagingButtonIcon` | 撤回待部署区按钮 |

除 `UnitInformationPanelTabActiveDecoration` 仍只有 `nameFileIdTable` 映射、没有 Sprite 切片本体外，其余名称都已经应用到相应图集 `.meta` 或独立贴图资源。重命名保留了贴图 GUID；所有现有切片的矩形、`spriteID` 和 `internalID` 保持不变。

## 12. 示例汇总

在 `1920 × 1080` 下：

| 项目 | 公式 | 结果 |
|---|---:|---:|
| 头像自然边长 | `ScreenHeight ÷ 6` | 180 |
| 头像区域底部偏移 | `ScreenHeight ÷ 120` | 9 |
| 费用标记向上占用高度 | `ScreenHeight ÷ 108` | 10 |
| 槽位内容高度 | `9 + 180 + 10` | 199 |
| 槽位总高度 | `5 × ScreenHeight ÷ 27` | 200 |
| 待部署区高度 | `SlotHeight` | 200 |
| 选中上移距离 | `ScreenHeight ÷ 54` | 20 |
| 最小压缩宽度 | `7 × ScreenHeight ÷ 54` | 140 |
| 12 槽基础压缩宽度 | `1920 ÷ 12` | 160 |
| 10 槽自然总宽度 | `10 × 180` | 1800 |
| 10 槽靠右后的左侧空白 | `1920 - 1800` | 120 |
| 资源显示区尺寸 | `ScreenHeight/6 × 2×ScreenHeight/27` | `180 × 80` |
| 部署费用区纵向范围 | `11×ScreenHeight/54` 至 `5×ScreenHeight/18` | `y=220～300` |
| 赤金区纵向范围 | 紧接部署费用区上方 | `y=300～380` |
| 顶部状态栏尺寸 | `83×ScreenWidth/192 × ScreenHeight/20` | `830 × 54` |
| 单位信息面板尺寸 | `3×ScreenWidth/8 × ScreenHeight` | `720 × 1080` |
| 信息面板上下分区 | `21/40 × 19/40` | `567 + 513` |
| 信息面板血条位置 | `(0, 49×ScreenHeight/108)` | `(0,490)` |
| 信息面板血条尺寸 | 原始切片尺寸 | `556 × 12` |
| 信息面板头像 | `PanelWidth/4` | `180 × 180` |
| 单位稀有度图标 | `PortraitSize/4` | `45 × 45` |
| 信息面板属性词条 | `(720-2×12-180-23-10)/2 × 52` | `241.5 × 52` |
| 信息面板属性行间距 / 列间距 | 图6指定区域测量值按 `720/830` 等比缩放 | `10 / 10` |
| 部署选择初始高度偏移 | `2 × GridCellWorldSize` | 200 世界单位 |

## 13. 玩家等级按钮、商店与准备按钮

### 13.1 玩家等级按钮与商店整体布局

玩家等级按钮（`ShopLevelButton`）固定在界面右上角：

- 背景使用 `Assets/Resources/UI/Texture/shop/level.png`；
- 按钮中显示本地玩家当前等级；
- 等级数字的字号、字重和视觉比例参考图8右下角的等级数字；
- 按钮的视觉中心应尽量与左上角设置按钮形成镜像对称，但两张贴图都可能带有透明留白或阴影，最终位置以可见主体的视觉中心为准；
- 点击按钮只负责打开或关闭商店界面。

商店面板（`ShopPanel`）在准备阶段打开时显示在玩家等级按钮下方，位于界面上半区域。商店不得覆盖顶部中央的阶段信息栏。商店内部从左到右排列：

1. 一个升级按钮；
2. 五个商品槽位。

商品槽位整体靠商店区域右侧排列，升级按钮位于五个槽位左侧。商店打开时，在商店面板下方显示冻结按钮和刷新按钮，视觉顺序参考图7：冻结在左，刷新在右。商店关闭时，商店面板、冻结按钮和刷新按钮一并隐藏。

玩家等级按钮始终是商店的唯一打开/关闭入口；`Assets/Resources/UI/Texture/shop/btn_open.png` 不再使用。商店、五个槽位、升级按钮、冻结按钮和刷新按钮的最终参考坐标与间距尚需在 `1920 × 1080` 下通过截图拟合。拟合时应保持图7中的整体相对比例，不得为了填满上半区域而非等比拉伸商品头像或图标。

### 13.2 商品槽位

商品槽位以三个稀有度边框的共同原始尺寸 `158 × 175` 作为自然尺寸基准。槽位有商品时：

- 最底层背景使用 `bg_black.png`；
- 单位头像显示在槽位主体区域，视觉上靠右上放置；头像的最终尺寸、裁剪范围和精确偏移仍需参照图7拟合；
- 边框根据单位稀有度选择；
- 三个边框底部自带横向信息区域，单位名称显示在该区域；
- 名称区域之外的左下区域显示单位阵营和种类；
- 商品价格区域位于槽位上方中央。

稀有度边框映射为：

| 单位稀有度 | 边框资源 |
|---:|---|
| 1～3 | `frame_lv1.png` |
| 4～5 | `frame_lv2.png` |
| 6 | `frame_lv3.png` |

`bg_black.png` 的原始尺寸为 `156 × 172`，应放在边框、头像、名称和其他信息之后。它在几何上接近覆盖完整槽位，但不得遮挡前景名称文字。

商品被购买后，槽位进入空状态：

- 清除单位头像、名称、阵营、种类、价格、冻结特效和购买确认状态；
- 使用 `bg_empty.png` 显示空槽位；
- 空槽位不再响应商品购买。

价格区域规则：

- 商品价格直接等于单位稀有度；单位 JSON 和 Player-safe catalog 不增加独立价格字段；
- UI 可以读取领域层提供的派生 `Price`，但该值必须由 `Rarity` 计算；
- 当前 `typeId=1000` 的稀有度和价格均为 `1`，`typeId=5503` 的稀有度和价格均为 `4`；
- 玩家付得起时使用 `cost_bg_1.png`；
- 玩家付不起时使用 `cost_bg_2.png`；
- 玩家付不起时，在槽位主体上覆盖 `bg_common.png`。

首次点击一个允许发起购买的商品时，显示 `bg_doublecheck1.png` 作为二次确认效果。该贴图在商品槽位内部处于接近最前的视觉层，除槽位冻结特效 `ice_matte.png` 外，不应被其他槽位内容覆盖。是否需要在二次确认状态中额外显示文字或价格变化，仍待后续补充。

当前本地 UI Demo 不实现免费购买，不创建免费商品状态，也不触发 `cost_free.png`。该贴图保留在资源目录中供后续规则使用，不属于本轮验收矩阵。

### 13.3 冻结、刷新与悬停效果

冻结作用于当前有商品的槽位，不作用于已经购买后的空槽位：

- 冻结槽位在底部显示 `ice_matte.png`；
- `ice_matte.png` 原始尺寸为 `157 × 66`，宽度应与槽位宽度近似一致，底边与槽位底边对齐；
- `ice_matte.png` 是槽位视觉最前层，可以覆盖名称区域和 `bg_doublecheck1.png`；
- 冻结不阻止点击或购买商品；
- 冻结商品购买成功并变为空槽位后，移除冻结特效。

冻结按钮的视觉状态为：

| 状态或操作 | 背景 | 图标 |
|---|---|---|
| 当前未冻结，可执行冻结 | `frozen_bg_normal.png` | `frozen_icon.png` |
| 当前已冻结，可解除冻结 | `frozen_bg_unselect.png` | `frozen_icon2.png` |

`frozen_icon_lock.png` 不再使用。

刷新按钮正常状态使用 `refresh_bg_normal.png` 和 `refresh_icon.png`；刷新不可用时可以使用 `refresh_icon_lock.png` 替换正常图标。刷新不可用的具体条件和是否还需改变背景颜色属于商店机制，尚待确认。

当前本地 UI Demo 刷新消耗 `1` 赤金。五个初始商品全部为 `typeId=1000`，允许重复类型；刷新使用 `docs/SPEC.md` 中确认的三页固定循环。被冻结且仍有商品的槽位保留原商品，其他槽位使用下一页对应索引的商品。

`frame_outline.png` 暂定作为商品槽位鼠标悬停效果，放在 `frame_lv1/2/3` 下方。其实际可见程度必须通过截图验证；若完全被稀有度边框遮挡，应先报告视觉结果，再决定是否轻微放大或弃用，不得直接改变槽位自然尺寸。

### 13.4 升级按钮

升级按钮与商品槽位等高，使用以下状态：

- 可以升级时，背景使用 `upgrade_max.png`，并在按钮上显示玩家当前等级；
- 不能升级时，背景使用 `upgrade_disable.png`；
- 首次点击可以升级的按钮后，使用 `check_frame.png` 与 `check_grad.png` 组合显示升级二次确认效果。

当前本地 UI Demo 的初始等级为 `1`，最高等级为 `9`。升级只扣除赤金并把等级增加 `1`；临时费用依次为 `4、6、8、10、12、14、16、18`，分别对应 `1→2` 至 `8→9`。等级 `9` 或赤金不足时显示不可升级状态。

`check_frame.png` 的原始尺寸为 `111 × 175`，与升级按钮一致。`check_grad.png` 的原始尺寸为 `16 × 136`，其具体拉伸、平铺或重复方式需要通过截图拟合，不得仅按原始窄条尺寸直接居中显示。

`acbattle_img_bg_bond_desc.png` 不再使用。

### 13.5 准备按钮

准备按钮（`ReadyButton`）固定放在右侧赤金区和部署费用区上方，不随商店打开或关闭改变位置。右侧资源与准备按钮从上到下依次为：

1. 准备按钮；
2. 赤金区；
3. 部署费用区。

按钮背景由 `Assets/Resources/UI/Texture/ready/ready_bg.png` 和 `ready_frame.png` 组合构成。两张贴图都需要拉伸或填充到按钮目标矩形；实现时应根据边缘效果判断使用普通拉伸还是九宫格，不得把原始小尺寸直接作为最终按钮尺寸。

准备按钮包含两个玩家状态：

| 玩家状态 | 图标 | 按钮文字 | 点击结果 |
|---|---|---|---|
| 未准备 | `icon_ready.png` | `准备就绪` | 进入已准备状态 |
| 已准备 | `ready_icon.png` | `取消准备` | 取消准备 |

已准备状态只锁定会改变当前部署阵型的单位操作：

- 禁止从待部署区部署单位；
- 禁止在部署区移动或交换单位；
- 禁止将部署区单位撤回待部署区；
- 其他会改变部署阵型的入口必须使用同一个准备状态进行拦截，不能只禁用某一种拖动路径。

已准备状态不销毁、不重建也不修改当前阵型。取消准备后恢复上述单位操作。已准备期间仍然允许：

- 打开或关闭商店；
- 购买单位；
- 升级玩家等级；
- 刷新商店；
- 冻结或解除冻结商品。

购买获得的单位可以正常进入本地玩家的待部署区，但在取消准备前不能继续部署、移动或撤退单位。

点击准备不会提前结束准备阶段，原有 `30` 秒倒计时继续推进。进入战斗阶段时，等级按钮、商店和准备按钮全部隐藏，并强制关闭已打开的商店。战斗结束返回准备阶段时，准备状态重置为未准备。

## 14. 左侧玩家列表

### 14.1 位置和头像槽位

玩家列表（`PlayerListPanel`）固定在界面左侧，当前本地 Demo 固定显示 `4` 名测试玩家，整体排列和尺寸比例参考图7。每名玩家的初始生命为 `400`。单个玩家头像槽位使用：

- `avatar_border.png`：头像框；
- 玩家头像：显示在头像框内部；
- `bg_hp.png`：位于头像下方中央的常规玩家生命背景；
- `icon_hp.png`：显示在头像下方的生命区域内，位置参考图7；
- 玩家当前生命数字：显示在 `bg_hp.png` 上。

本地玩家头像左上角显示 `icon_self.png`。该图标可以略微超出头像框，最终偏移以图7中的可见主体为准。

玩家掉线后，在其头像上显示 `icon_lost_connect.png`。掉线图标与头像、生命和其他状态图标之间的遮挡顺序及掉线后是否允许继续观察，仍属于后续多人规则。

`band_info_board.png` 和 `band_info_board 1.png` 当前不使用。

### 14.2 战斗结束扣除生命的表现

常规状态使用 `bg_hp.png`。战斗阶段结束并表现玩家生命扣除时，临时使用 `bg_lose_hp.png` 作为掉血状态背景。掉血表现结束后恢复常规生命背景。

本节只规定 UI 状态，不定义玩家生命初始值、实际扣除数值、扣血动画时长和淘汰规则。

### 14.3 观察其他玩家

点击其他玩家头像后，当前界面切换为观察该玩家：

- 场地和已部署单位显示被观察玩家的数据；
- 待部署区显示被观察玩家的数据；
- 观察视图使用现有 Away/观察者显示变换，只改变表现坐标，不修改被观察玩家保存的阵型坐标；
- 被观察玩家的单位和阵型只读，观察者不能部署、移动、交换或撤退对方单位；
- 商店始终显示并操作本地玩家自己的商店，不能查看被观察玩家的商店；
- 玩家等级、部署费用、赤金和准备状态继续显示本地玩家自己的数据。

准备阶段和战斗阶段都允许切换观察目标。当前四玩家本地 Demo 在战斗阶段额外计算第二场独立战斗，固定映射为：

| 被观察玩家 | 战斗 | 观察视角 |
|---|---|---|
| 玩家 1 | `MatchAB`：玩家 1 对玩家 2 | Home |
| 玩家 2 | `MatchAB`：玩家 1 对玩家 2 | Away |
| 玩家 3 | `MatchCD`：玩家 3 对玩家 4 | Home |
| 玩家 4 | `MatchCD`：玩家 3 对玩家 4 | Away |

两场战斗分别使用各自玩家的封存快照额外计算，产生独立结果和只读表现 Track；切换玩家只选择对应战斗和观察视角，不得重新运行战斗 Core，也不得复制本地玩家当前战斗的结果来伪装另一组玩家的战斗。

两场 Track 共用一个演示时钟和播放速度，未显示战斗也随该时钟推进。切换玩家时按当前演示 Tick 采样目标战斗，精确恢复位置、生命、护盾、死亡和动作类型；Spine 动画从当前动作类型开头播放，不恢复动画内部进度。较早结束的一场保持最终状态；全部战斗 Track 都结束后返回准备阶段。

进入观察状态后：

- 当前被观察玩家头像右上角显示 `icon_observing.png`；
- 本地玩家自己的头像显示 `btn_return_self.png`；
- 点击本地玩家头像上的 `btn_return_self.png` 返回自己的场地和待部署区；
- 返回自己后清除被观察玩家头像上的观察标记。

观察状态只改变当前显示目标，不改变玩家数据、商店归属、准备状态或战斗结果。

### 14.4 与单位信息面板的互斥

未选中单位时，左侧玩家列表正常显示。选中任意单位后：

- 隐藏整个 `PlayerListPanel`；
- 在左侧显示第9节定义的单位信息面板；
- 玩家列表的观察目标、掉线状态和其他数据继续保留，不因隐藏而重置；
- 取消单位选择或关闭单位信息面板后，重新显示玩家列表。

该规则同样适用于观察其他玩家时选中其单位。此时单位信息面板可以显示被观察单位的只读信息，但不得因此开放对方单位的部署、移动、交换或撤退操作。

## 15. 商店与玩家列表资源导入

本节新增贴图主要来自：

- `Assets/Resources/UI/Texture/shop/`
- `Assets/Resources/UI/Texture/ready/`
- `Assets/Resources/UI/Texture/player_list/`

这些贴图当前具有独立 `.meta` 文件。实施任务必须先检查实际 Unity 导入结果；需要用于 `UnityEngine.UI.Image` 的贴图应导入为可用的 Sprite。需要拉伸的背景和边框应根据可见边缘决定普通拉伸或九宫格边界，头像和图标不得为了填满目标区域而非等比变形。

明确不使用的贴图为：

- `shop/frozen_icon_lock.png`
- `shop/btn_open.png`
- `shop/acbattle_img_bg_bond_desc.png`
- `player_list/band_info_board.png`
- `player_list/band_info_board 1.png`

未使用贴图不需要在本轮删除；保持资源和 `.meta` 文件不变，避免无必要的 GUID 与引用迁移。

## 16. 待后续补充

以下细节不影响继续完善需求，但在制作槽位 Prefab 前需要确认：

1. 不同槽位框贴图中，哪些部分适合直接横向缩放，哪些部分需要通过九宫格切片保持边角比例。
2. 中文使用 Noto Sans SC（思源黑体）Normal，字间距 `0`。费用、堆叠、倒计时、战斗计数、玩家生命和单位生命数值使用项目内的 Novecento Wide Normal Regular（Unity 字体名 `Novecento wide`）；堆叠数量为 `30px`，相对头像右下角的具体边距仍以截图为准。
3. 部署费用为 `54px`，待部署槽费用为 `24px` 且锚点 Y 为 `0`；赤金沿用部署费用的字体与字号，当前以图1～图4作为视觉匹配参考。
4. 设置按钮的精确显示尺寸、位置和点击区域；当前需要通过参考图进行视觉拟合。
5. `DeployedUnitSelectionOverlay` 的最终世界尺寸、坐标偏移和渲染排序。
6. `ReturnToStagingButtonIcon` 是否保持屏幕方向竖直、点击区域如何确定，以及它相对菱形左上边中点的最终视觉偏移。
7. 状态栏最终 `RectTransform` 数值和三个区域内部的精确排版。
8. 战斗阶段及后续其他阶段的倒计时初始值和显示格式；准备阶段已经确认使用 30 秒并在每次返回准备阶段时重置。
9. 非战斗阶段对战对手文字使用的具体字段、格式、字体和字号。
10. `BattleStatusPanelClockIcon` 最终处理为单独切片派生资源，还是整张源图的派生版本。
11. 玩家生命扣除规则、掉血持续时间和显示格式；初始值已经确认为 `400`。
12. 单位信息面板的最终宽度、背景缩放方式和内部横向排版。
13. 单位信息面板所需六项动态属性的只读运行时快照接口，以及 Buff 生效后的刷新时机。
14. 单位信息面板生命数字区域以 `149 × 50` 原始背景贴图显示，左上角随填充右上端移动；数值为 `24px` Novecento Wide Normal Regular，文本顶部内边距为 `5px`。当 `CurrentHP/MaxHP` 超过 9 个字符时缩小为 `20px`。
15. 三个页签的默认页面、选中/未选中颜色，以及是否需要切换动画。
16. 灰显、已部署标记和拖拽中的视觉状态后续单独定义；这些状态不属于待部署区基础布局规则。
17. 玩家等级按钮、商店面板、升级按钮、五个商品槽位、冻结按钮和刷新按钮在 `1920 × 1080` 下的最终 `RectTransform` 数值。
18. 商品头像在 `158 × 175` 槽位内的尺寸、裁剪方式和右上角视觉偏移。
19. 商品槽位名称、阵营、种类、价格、二次确认和冻结特效的最终字体、字号、间距与层级截图。
20. `frame_outline.png` 放在稀有度边框下方时是否具有足够清晰的悬停效果。
21. 商店刷新不可用的条件，以及 `refresh_icon_lock.png` 是否需要配套的禁用背景。
22. 准备按钮的最终尺寸、与赤金区的间距，以及背景和边框采用普通拉伸还是九宫格。
23. 点击准备后是否影响准备阶段剩余时间、何时提前进入战斗，以及多人情况下全部玩家准备后的阶段转换规则。
24. 左侧玩家列表的头像尺寸、槽位间距、生命文字格式，以及列表人数超过可用高度时的处理方式。
25. 玩家掉线后是否仍允许观察、掉血背景持续时间、掉血数字或动画，以及玩家淘汰后的头像状态。
26. 两场战斗之间是否需要额外的视觉切换过渡；Track、共享时钟、切换采样和阶段结束语义已经确认。
