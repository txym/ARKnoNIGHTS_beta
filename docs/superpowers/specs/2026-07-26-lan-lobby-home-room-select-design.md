# LAN 大厅 Home「room_select」复刻设计

**状态：用户已确认，待实施。**

## 目标

将 Home 界面右侧完整重构为参考图 9 的“创建同盟 / 加入同盟”结构，同时保留现有 LAN 房间创建、房间号预填和加入行为。视觉主素材必须是 `room_select_` 系列。

## 素材来源规则

- `G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess`：普通独立图片若文件基础名以 `0` 结尾则忽略；`room_select_` 只使用无 `$0` 的普通版本。
- `G:\素材\11.14\Combined_1763139377\Android\ui\autochess`：允许使用所有图片，包括 `#0` / `$0`。该目录的 Atlas 使用 Multiple Sprite 切分后引用；不得把整张 Atlas 直接作为 UI Image。
- 头像只来自 `Combined...\[uc]autochesscommon`：`icon_amiy`、`icon_clementi`、`icon_kirar`、`icon_zumam` 及其 `$0` 版本。最终选择项必须写入资产映射与捕获 manifest。
- 所有新贴图仍导入 `Assets/Resources/UI/Lobby`，并在 `ASSET_MAP.md` 记录 Resources 路径、精确来源相对路径、用途和拉伸方式。

## Home 结构与行为

右侧在 1920×1080 中采用图 9 的层级：

1. `room_select_right_bg` 作为右侧装饰/分区底图；
2. 标题区域使用 `room_select_title_icon`，文字为“选择同盟方式”；
3. 创建区使用 `room_select_create_left_line`、`room_select_create_middleicon`、`room_select_create_logo`、两张 `room_select_create_text_*` 与 `room_select_create_btn_bg_down`。点击创建区或创建按钮触发现有 `CreateRequested`；
4. 加入区使用 `room_select_join_left_block`、`room_select_join_middle_block`、`room_select_join_middle_block_mask`、`room_select_join_right_block`、`room_select_join_logo`、两张 `room_select_join_text_*`、`room_select_join_text_bg`、`room_select_join_triangle`、`room_select_join_blank`、`room_select_join_ban` 与 `room_select_join_btn_bg_down`；
5. 六位房间号输入框位于加入区的密钥位置；发现房间点击依然仅预填，不自动加入；合法且可加入时加入按钮才可用；
6. 现有左侧身份区保留，但头像选择显示导入的 Combined 头像，而非 `AVATAR n` 文本。

展示文字可使用现有字体；不将文字烘焙成新贴图。图 9 明确排除的左侧雷达、左上/左下区域仍不复刻。

## 验收

- 1920×1080 Player Home 截图中，右侧创建/加入 UI 的层级、色彩、按钮、输入区、装饰与参考图 9 对齐；
- 创建与加入操作保持当前 LAN 流程；发现项预填的 PlayMode 断言保持通过；
- 每个可见 `room_select_` 与头像 Sprite 都可从 manifest 追溯到批准素材路径；
- 无 `$0` 的 Unpacked 普通素材被新 Home 引用；Combined `$0` 仅可按上述规则使用；
- 自动视觉差异报告更新为真实 Home 捕获，并保留人工可见的叠图/热图。
