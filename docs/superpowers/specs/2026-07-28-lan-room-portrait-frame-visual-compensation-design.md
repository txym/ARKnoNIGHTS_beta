# LAN 房间立绘区域可见边界补偿设计

## 状态

已由用户确认：

- 空槽、未准备成员、已准备成员与房主使用同一套立绘区域尺寸；
- 立绘内容本身仍不属于本轮复刻范围；
- 本轮只修正立绘区域的边框尺寸与上下层覆盖关系；
- 其他房间组件保持当前布局，除非自动证据证明本轮产生回归。

本设计补充并局部取代
`2026-07-28-lan-room-slot-states-design.md` 中关于 `CardBody` backing rect
可直接代表视觉边界的假设。可见边界继续高于贴图矩形和
`RectTransform` 边界。

## 问题证据

当前统一使用 `card_bg`：

- 贴图尺寸：`159x264`；
- 来源：`[uc]autochessouter/card_bg.png`；
- SHA-256：
  `050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2`；
- 当前 backing rect：
  - 局部左侧：`18.5 px`；
  - 宽度：`337 px`；
  - 高度：`553.25 px`。

`card_bg` 外沿包含低透明度渐变。把 backing rect 直接对齐目标并不能
保证截图中的边框也对齐。Cycle 3 中可观察到：

- 上方横条 backing rect 为局部左侧 `26.25 px`、宽度 `320.25 px`；
- 未准备槽的可见轮廓高度只有 `462 px`，参考为 `523 px`；
- 图 11 的空槽组合比参考短 `34–168 px`；
- 已准备轮廓与上方横条仍存在可见中心/宽度偏差；
- 立绘区域在下方头像名称横条之前出现明显提前结束或空隙。

这些现象与用户判断一致：问题主要来自贴图 backing 大小与实际可见
区域不一致。

## 方案选择

### 采用：统一可见边界补偿

新增一个状态无关的 `PortraitFrame` 几何。继续使用 `card_bg`，但其
backing rect 根据源贴图的实际可见区域做补偿：

- 所有槽位和状态使用相同的 backing rect；
- 可见左右边框与玩家槽位上方横条对齐；
- backing 向下延长并进入下方头像名称横条区域；
- 下方横条绘制在更高层，覆盖延长部分和接缝；
- 状态仅控制颜色、翻转、内部邀请/就绪内容和下方横条素材。

第一轮 canonical seed：

| 属性 | `1920x1080` 局部值 |
| --- | ---: |
| backing 左侧 | `26.0 px` |
| backing 顶部 | `0 px` |
| backing 宽度 | `329.0 px` |
| backing 高度 | `626.0 px` |
| 下方横条顶部 | `544.5 px` |
| backing 被横条覆盖的深度 | `81.5 px` |

这些值是从 Cycle 3 的 `337x553.25` backing 与实际可见边界比例反推的
校准起点，不是验收真值。最终值只能由新的 Player 截图中的可见边界
决定。

### 未采用：拼接独立边框

可用线段、裁片或重复贴图拼出外框，控制精确，但会增加接缝、层级和
来源审计复杂度。当前证据表明 `card_bg` 本身可用，优先修正几何。

### 未采用：按状态分别缩放

分别调整 Empty、Waiting、Ready 能更快贴近单张参考图，但会违反用户
确认的“立绘区域大小统一”，并造成后续状态切换跳动。

## 布局与层级

每个玩家槽的相关层级从下到上为：

1. `PortraitFrame`：使用 `card_bg`，统一补偿后的 backing；
2. 状态内容：
   - Empty：`card_empty`、邀请图标和文字；
   - Waiting：本轮不添加立绘；
   - Ready：现有 ready overlay、图标与文字；
3. `TopBar`：保持当前槽位上方横条布局；
4. `LowerDecoration`：头像名称横条，覆盖 PortraitFrame 底部；
5. Creator tag 等现有前景元素。

约束：

- `TopBar`、`LowerDecoration`、按钮、Leave、延迟和槽位根位置不因本轮
  移动；
- `LowerDecoration` 必须位于 PortraitFrame 之上；
- 截图中 PortraitFrame 与 LowerDecoration 之间不得露出背景缝隙；
- 房主槽继续不渲染头像、名字、ID 或立绘内容；
- Empty/Waiting/Ready 切换时 PortraitFrame 的 backing rect 不变化。

## 可见边界验收

参考图仍为：

- 图 11：房主槽与 2–4 号空槽；
- 图 12：房主槽与无遮挡的 2–3 号未准备槽；
- 图 13：房主槽与无遮挡的 2–3 号已准备槽。

图 12、图 13 的完整第四槽继续因右侧弹窗排除，且排除项不计为通过。
弹幕、弹窗像素、立绘内容和头像名称内容仍不参与本轮边框验收。

在 `1920x1080`：

- 所有状态的 PortraitFrame backing rect 必须完全相同；
- 左右可见边框相对对应上方横条的可见边界：
  - 单边误差不超过 `4 px`；
  - 可见中心单轴误差不超过 `2 px`；
  - 可见宽度误差不超过 `3 px`；
- PortraitFrame 可见纵向边框必须到达 LowerDecoration 覆盖区；
- LowerDecoration 上沿附近不得出现超过 `1 px` 的连续背景缝隙；
- backing 与 LowerDecoration 的几何重叠不得小于 `60 px`；
- Figure 11–13 中新增的边框专用阻断 gate 必须全部通过；
- 当前已经通过的 Leave、操作按钮和上方横条 gate 不得回归。

不得通过扩大 ROI、增加 popup mask、降低阈值或把失败项改成 excluded
来获得通过。

## 实现边界

预期只需要修改：

- `LanLobbyRoomLayout` 的统一 PortraitFrame canonical 几何；
- `LanLobbyView` 中 CardBody/PortraitFrame 的绑定和层级；
- 对应 Layout、View、Capture 测试；
- VisualDiff/Evidence 中边框专用 gate 与 smoke；
- 最终证据文档。

不新增位图；所有位图继续来自已批准的 autochess 素材映射。不得修改
Home UI、网络行为、玩家准备逻辑或房间生命周期。

## 验证策略

1. 先添加 Layout/View 失败测试：
   - 四个槽位共享同一 PortraitFrame；
   - Empty/Waiting/Ready 绑定后 rect 不变；
   - backing 与 LowerDecoration 至少重叠 `60 px`；
   - 现有 TopBar/LowerDecoration/按钮 rect 不变。
2. 添加 visual smoke mutation：
   - 可见左右边框偏移 `5 px` 必须失败；
   - 可见宽度偏差 `4 px` 必须失败；
   - backing 提前结束、露出 `2 px` 背景缝隙必须失败；
   - 状态间使用不同 frame rect 必须失败。
3. 修改最小实现并运行相关 EditMode、PlayMode 与三项 evidence smoke。
4. 新建独立的可见 Player 校准任务；旧 3/3 Cycle 1–3 不作为新实现的
   最终证据。
5. 新任务最多执行三次可见 Player 校准。每轮必须生成五张
   `1920x1080` 截图、manifest、Evidence 和 VisualDiff。
6. 三轮后仍失败则停止，报告精确可见边界差值，不降低门槛。

在用户明确批准本规格及其新 Player 校准任务前，不启动新的 Player。

## 非目标与剩余阻断

本设计不声称修复：

- 立绘、头像、名称或 ID 内容；
- 其他现存的 ready icon/label、按钮或非边框视觉失败；
- 已确认的 stale snapshot 覆盖 `Start` 网络缺陷；
- accept/stop 生命周期竞态；
- Windows/Android 同 Wi-Fi 实机互联。

这些项目继续独立记录，不能因本轮边框改善而标记完成。
