# UI-INFO-002：单位信息面板局部视觉拟合与词条图标补全

> 本文件已经是用户确认的设计与实施任务。实现 Agent 不得再次运行 brainstorming 或 writing-plans，不得另建第二份设计/计划文档，也不需要等待二次批准。完成简短的仓库状态、前置任务和冲突检查后直接实施；只有触发本文“停止并询问我的条件”时才暂停。

## 角色

你是本 Unity 项目的正式 HUD 视觉拟合与截图验收 Agent。

你的职责是在 `UI-INFO-001` 已经接通单位详情数据、选择来源和信息面板上半部结构的基础上：

1. 先给九个具体属性词条补齐已经指定的图标；
2. 再以 `docs/references/ui/battle_hud/图6.png` 的指定局部区域为视觉参考；
3. 通过多轮固定状态截图、局部裁切、测量、对比、调整和复拍，使本项目 UnitInformationPanel 上半部的组件位置、尺寸、文字大小、基线和间距尽可能接近图6的局部排版。

本任务只做信息面板的局部表现收敛，不改变单位数据、战斗规则、玩家状态、选择权限或其他 HUD 区域。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/UI_TASK_TABLE.md`
- `docs/UI-005.md`
- `docs/UI-005-REPORT.md`
- `docs/UI-INFO-001.md`
- `docs/UI-INFO-001.md` 对应的完成报告、实现计划和实际验证记录（若存在）
- `docs/superpowers/specs/2026-07-24-ui-info-001-design.md`
- `docs/superpowers/plans/2026-07-24-ui-info-001.md`
- `docs/references/ui/battle_hud/UI_SPEC.md`，重点阅读第 9、11、12、13 节
- `docs/references/ui/battle_hud/图6.png`
- `Assets/Game/Runtime/Initial/FormalBattleHudUi005.cs`
- `Assets/Game/Runtime/Initial/UI005CaptureSuite.cs`
- `Assets/Game/UI/FormalHud/StagingHudController.cs`
- `Assets/Game/UI/FormalHud/StagingHudLayout.cs`
- `Assets/Game/Runtime/Details/`，即当前定义 `UnitDetailSnapshot`、`UnitDetailResolver`、数值格式化器的目录
- `Assets/Game/Tests/EditMode/Battle/UnitDetailProjectionEditModeTests.cs`
- `Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs`
- `Assets/Resources/UI/Texture/unit_panal.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitInformationPanelHealthIcon.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitInformationPanelAttackIcon.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitInformationPanelAttackIntervalIcon.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitInformationPanelDefenseIcon.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitInformationPanelMagicResistanceIcon.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitInformationPanelBlockCountIcon.png` 及 `.meta`
- `Assets/Resources/UI/Texture/UnitInformationPanelDeploymentCostIcon.png` 及 `.meta`
- `Assets/Resources/UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged.png.meta`
- `Artifacts/UI-005/` 下最新有效截图、manifest、测试 XML、Player 日志和构建日志
- 所有直接创建或查找 `UnitInformationPanel`、`Stat_*`、`TargetValue`、`Portrait`、`HealthBackground` 和 `HealthValue` 的代码与测试

开始前执行 `git status --short`，确认 UI-INFO-001 已经完成到当前工作区。不得覆盖用户修改、重新生成图集 GUID、替换源图片或回滚已经接通的数据投影。确认没有其他 Unity Editor 或 batchmode 进程占用同一项目。

## 任务背景

当前真实状态：

- `UI-INFO-001` 已经能够解析 staging、deployed 和 battle enemy 的 `UnitDetailSnapshot`；
- UnitInformationPanel 已经显示中文名占位、攻击方式/伤害类型、头像、稀有度、精英化、目标价值、八项属性数值和 CurrentHP/MaxHP；
- 当前 `TargetValue` 已有目标价值图标；
- 当前八个右侧属性词条只创建了 Background、Label 和 Value，没有创建各自的 Icon；
- `UI_SPEC.md` 已明确九个词条的背景与图标资源映射，不需要重新命名或猜测 Sprite；
- 当前 `FormalBattleHudUi005.BuildInformationPanel` 使用大量粗略百分比锚点，例如头像 `.2/.79`、标题 `.48/.88`、词条 `.48/.765`；
- 当前右侧词条参考尺寸约为 `190×40`，与 `UI_SPEC.md` 按图6测量后建议的两列词条尺寸和密度仍有明显差异；
- 最新 1920×1080 截图已经能证明面板存在，但尚不能证明它与图6局部区域的组件位置、字号、基线和间距充分一致；
- `UI-005-REPORT.md` 已明确指出尚缺逐图、逐状态、与图1～图6对照的持久化视觉差异报告；
- 图6的整张界面不是本任务的复刻目标。只参考其中右侧单位详情局部；图6的角色、文字内容、评级字母和无关字段不得照搬。

因此，本任务不是再做一遍数据接入，而是把已经可用的上半部 UI 收敛到一套经过截图证据支持的局部排版。

## 本任务目标

1. 为生命值、移动速度、攻击力、攻击间隔、防御力、法术抗性、阻挡数和部署费用八个右侧词条分别补上正确图标。
2. 保留目标价值词条已有图标，并把它与右侧八词条纳入同一视觉规格。
3. 让所有九个词条都具有可审查的 `Background → Icon → Label → Value` 层级或语义等价结构。
4. 在修改位置和字号前生成一份 1920×1080 基线截图与局部裁切，记录当前差异。
5. 将图6的指定 `830×420` 参考区域裁切为独立对照图，不把整张图6叠到本项目画面上。
6. 测量参考区域内名称、第二行、头像、目标价值、两列四行词条的边界、基线、间距和视觉字号。
7. 将参考区域统一等比映射到本项目 UnitInformationPanel 上半部的目标内容区；不得横纵分别拉伸。
8. 通过至少“基线 → 图标完成 → 首轮布局 → 字体/间距收敛 → 最终确认”五个可辨认阶段进行截图迭代。
9. 每一轮只调整有明确证据的局部参数，并记录调整前后值、原因和截图路径。
10. 最终使主要组件位置、尺寸和文字视觉大小达到本文定义的局部容差；无法完全一致的差异必须有具体原因。
11. 保证 staging、deployed 和 battle enemy 三种来源使用同一套布局，不因数据来源切换发生跳位。
12. 保证短值、长值、空中文名占位和长 HP 文本不会覆盖图标、标签或相邻词条。
13. 扩展现有截图 manifest，使每轮截图能记录信息面板局部组件的实际屏幕矩形、字体大小和资源名。
14. 生成持久化的 `UI-INFO-002` 视觉拟合报告，列出最终参数、对照结果、已知差异和人工判断。
15. 保持 Editor 编译、相关 PlayMode 测试和 Windows Player 截图入口可用。

## 已确认规则

### 视觉参考范围

图6分辨率为 `1920×1080`。用户指定的参考区域使用图片右下角为原点：

```text
左上角（右下原点） = (880, 880)
右下角（右下原点） = (50, 460)
```

换算为常规左上原点坐标：

```text
ReferenceCropLeft   = 1920 - 880 = 1040
ReferenceCropTop    = 1080 - 880 = 200
ReferenceCropRight  = 1920 - 50  = 1870
ReferenceCropBottom = 1080 - 460 = 620
ReferenceCropSize   = 830 × 420
```

只使用该 `830×420` 区域作为上半部内部排版参考。不得把图6整张图片、整个右半屏、下方说明文字或能力区域当作本任务目标。

### 允许复刻与禁止照搬

需要尽可能复刻：

- 名称行的位置、视觉字号和基线；
- 第二行“攻击方式 + 伤害类型”的位置、视觉字号和基线；
- 头像矩形的位置和尺寸比例；
- 头像与右侧属性网格之间的间距；
- 目标价值词条相对头像的位置和尺寸；
- 右侧两列四行词条的尺寸、行距、列距；
- 每个词条内部图标、标签和值的位置、视觉大小、对齐方式；
- 各组内容的整体密度和留白。

不得照搬：

- 图6中的角色名称、编号、描述文本；
- C、D、E、B+ 等评级内容；
- 图6中本项目没有的重量、元素抗性、损伤抵抗等字段；
- 图6下方角色说明和能力区域；
- 原作职业、等级、信赖、潜能或其他未在 SPEC 中确认的内容；
- 图6的背景画面、颜色滤镜或整屏构图。

本项目仍显示自己的九项字段和真实数值，只复刻局部排版关系。

### 九词条图标映射

| 词条 | 图标资源 |
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

所有图标：

- 使用已存在的 Sprite 或 TextureImporter 生成的 Sprite；
- 保持原始宽高比；
- 不跟随词条背景做非等比拉伸；
- 不在每次刷新时重新创建 Sprite 或材质；
- 不按 type ID 或 label 文本猜图标；
- 缺失时输出包含词条 key 和资源路径/Sprite 名的结构化错误，不静默隐藏整个词条。

### 内容与数据

- 字段顺序、数值来源和格式继续以 `UI-INFO-001`、`UI_SPEC.md` 为准。
- 生命值词条显示运行时 MaxHP；详细血条显示 CurrentHP/MaxHP。
- 数值不得新增 `%`、`s`、`m/s` 等单位。
- 中文名仍为空时显示 `--`，不得为视觉拟合修改真实单位 JSON 或使用 resource key 冒充中文名。
- 截图工具可以在不进入正式玩家数据、不写回资源的隔离视觉 fixture 中使用代表性中文字符串和长短数值检查布局；必须在 manifest 中标明 `visualFixture=true`。正式流程截图仍需使用真实数据。
- rarity、elite、头像、数值和选择权限不由本任务改变。

## 不属于本任务的内容

- 不修改 UnitJson、单位目录、玩家状态、BattleInput、战斗计算或 Presentation 数据语义；
- 不实现新的单位字段、技能描述、Buff 结算、治疗、护盾或远程单位；
- 不修改 staging/deployed/enemy 的选择规则和敌方只读权限；
- 不实现信息面板下半部分的技能、阵营、种族内容；
- 不调整顶部状态栏、设置按钮、Cost/赤金区、待部署槽或世界单位血条；
- 不修改信息面板总体宽度、上下分区或渲染层级，除非现有值与 `UI_SPEC.md` 明确冲突且先报告；
- 不修改图6、源 PNG 像素、Sprite 切片 rect、spriteID、internalID 或资源 GUID；
- 不替换字体文件，不引入新的字体、Tween、UI 框架或截图上传服务；
- 不追求整张截图的像素差异最小化；
- 不通过隐藏组件、缩小文字到不可读或裁掉长值来制造视觉“接近”；
- 不重写整个 `FormalBattleHudUi005`、截图系统或 SampleScene；
- 不修改 Unity、Package、渲染管线、Input System 或 ProjectSettings。

## 预计影响文件或目录

候选范围：

- `Assets/Game/Runtime/Initial/FormalBattleHudUi005.cs`
- `Assets/Game/Runtime/Initial/UI005CaptureSuite.cs`
- `Assets/Game/UI/FormalHud/` 下现有或新增的纯布局参数/测量辅助类型
- `Assets/Game/Tests/EditMode/Battle/` 下必要的纯布局或资源映射测试
- `Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs`
- `scripts/` 下仅用于本地裁切、叠图或读取 manifest 的项目内脚本
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- 新增 `docs/UI-INFO-002-REPORT.md`
- 截图和对比产物写入 `Artifacts/UI-INFO-002/` 或其他已忽略目录

优先不修改：

- `Assets/Scenes/SampleScene.unity`
- `Assets/Resources/Prefabs/DefaultUnit.prefab`
- `Assets/Resources/UI/Texture/*.png`
- 任何图片 `.meta`
- `Assets/Game/Battle/`
- `Assets/Game/Runtime/Data/Player/`
- `Assets/GameData/`
- Package 和 ProjectSettings

若当前 UI-INFO-001 还未完成、八个属性数值尚未接通或选择来源仍不可用，停止并报告前置任务缺口，不得在本任务里重新实现数据层。

## 实施要求

### A. 建立可复现基线

1. 确认 `UI-INFO-001` 的相关 Editor/EditMode/PlayMode 结果；不能只根据任务文档推断已经通过。
2. 使用现有 `-uiCaptureSuite` 或语义等价入口，在 1920×1080 固定状态捕获至少：
    - staging unit selected；
    - deployed unit selected；
    - battle enemy selected。
3. 将本轮开始前的截图复制或重新输出到：

```text
Artifacts/UI-INFO-002/00-baseline/
```

4. 同时保存 baseline manifest，记录 Canvas scaler、信息面板尺寸、选择来源、unitId/typeId、九项显示值和所有目标组件的屏幕矩形。
5. 打开 baseline 截图，明确列出至少以下差异：
    - 缺失的词条图标；
    - 名称/第二行相对头像的位置；
    - 头像大小和位置；
    - 两列词条的宽高、行距、列距；
    - 词条内部 label/value 的字号和对齐；
    - 目标价值的位置；
    - 是否存在裁切、重叠、低对比度或组件不可见。
6. 未生成、未打开或无法解码的 baseline 不能进入后续“视觉通过”结论。

### B. 先补齐九词条图标

7. 在任何大范围布局调整之前，先扩展词条构建接口，使每个词条显式接收 icon key/资源。
8. 八个右侧词条增加 `Icon` 子对象；目标价值词条保留并统一现有 Icon 命名与布局接口。
9. 对独立 PNG 使用稳定的 Resources Sprite 加载；对 `unit_panal.png` 的两个切片使用现有多 Sprite 加载/缓存。不得每帧 `Resources.Load` 或 `Sprite.Create`。
10. 给九个词条建立集中、可审查的映射表或语义等价结构。不得在九个调用点复制字符串查找逻辑。
11. 每个 Icon：
    - `raycastTarget=false`；
    - `preserveAspect=true`；
    - 以词条左侧固定区域为锚点；
    - 不与 label 或背景边界重叠；
    - 不因数值长度变化而移动。
12. 图标缺失只影响对应词条，并输出稳定错误；不得让信息面板初始化失败。
13. 增加针对九个 key→资源/Sprite 映射的自动断言。
14. 完成图标后立即捕获：

```text
Artifacts/UI-INFO-002/01-icons/
```

15. 实际打开截图，逐项确认九个图标都出现、图标与词条语义匹配、宽高比正常。图标未完成前不得开始最终位置拟合。

### C. 制作局部参考与测量基准

16. 从图6裁切常规左上坐标 `(1040,200)` 到 `(1870,620)` 的 `830×420` 区域，输出到：

```text
Artifacts/UI-INFO-002/reference/figure6_unit_detail_830x420.png
```

17. 不修改 `docs/references/ui/battle_hud/图6.png`。
18. 在参考裁切图上测量并保存结构化矩形，至少包括：
    - Name；
    - CombatSummary；
    - Portrait；
    - TargetValue；
    - 左列四个 Stat；
    - 右列四个 Stat；
    - 每个词条内部 Icon/Label/Value 的代表矩形；
    - 主要文字基线或视觉顶部；
    - 行距、列距、头像到属性区间距。
19. 测量结果写入 JSON、CSV 或 Markdown 表格，不得只存在于 Agent 的自然语言记忆。
20. 对图6中本项目没有的字段只测量其“所在格”的几何，不记录或复制其文本语义。
21. 参考字体与项目字体不同，不能只比较 Unity `fontSize` 数字。必须同时比较截图中可见字形的像素高度、行高和基线。

### D. 建立集中布局参数

22. 将 UnitInformationPanel 上半部的可调几何集中为一个局部布局配置、常量组或纯计算结果，至少覆盖：
    - 内容区起点和统一缩放；
    - Name/CombatSummary rect 和字号；
    - Portrait rect；
    - Rarity/Elite overlay rect；
    - TargetValue rect；
    - Stat entry width/height；
    - column gap、row gap；
    - Stat Icon/Label/Value 内部 rect；
    - HP 条与上半内容的间隔。
23. 不要求引入 ScriptableObject；优先使用可测试、不会增加场景引用的普通 C# 布局描述。
24. 避免继续在 `BuildInformationPanel` 中散布无命名的 `.48f/.765f/190f/40f`。参数名称必须表达视觉用途。
25. 布局计算以 `1920×1080` 和 `PanelWidth=720` 为参考，整体按 CanvasScaler 现有规则适配其他分辨率。
26. 图6裁切区域到本项目内容区只允许一个统一缩放值。单个背景可以按自身规则拉伸，但组件关系不得分别使用不同 X/Y 缩放。
27. staging、deployed 和 enemy 共用同一布局对象；禁止按 unitId/typeId 写特殊位置。

### E. 分阶段截图调整

每轮都必须遵循：

```text
修改少量参数
→ Editor 编译
→ 固定状态截图
→ 验证 PNG/manifest
→ 裁切信息面板局部
→ 打开参考图和实拍图
→ 记录差异
→ 决定下一轮
```

28. 第一轮布局：先调整大结构，输出到 `02-structure/`：
    - 内容区整体位置；
    - Name/CombatSummary；
    - Portrait；
    - TargetValue；
    - 两列四行网格。
29. 第二轮词条内部：输出到 `03-entry-layout/`：
    - Icon 尺寸和中心；
    - Label 起点、宽度和基线；
    - Value 右边距、宽度和对齐；
    - 背景高度与行间距。
30. 第三轮字体和细间距：输出到 `04-typography/`：
    - Name、CombatSummary、Label、Value、TargetValue 的视觉字号；
    - 字重、颜色、行高；
    - 文本顶部/垂直居中；
    - 相邻组件留白。
31. 最终确认轮输出到 `05-final/`。若仍存在超过容差且可修复的差异，继续使用 `06-.../`、`07-.../`，直到收敛或触发停止条件。
32. 每轮至少保存：
    - 原始 1920×1080 截图；
    - UnitInformationPanel 上半部裁切；
    - 与参考裁切等比后的并排图；
    - 可选的 50% 透明叠图或边缘叠图；
    - manifest；
    - 本轮调整记录。
33. 因本项目文本和头像与图6不同，不使用整图像素差异百分比作为唯一判定。优先比较组件 rect、基线、视觉字高、间距和是否重叠。
34. 每轮只改有证据的参数。禁止同时调整整个 HUD、相机、背景透明度和信息面板内容来掩盖差异来源。
35. 至少完成五个阶段目录；“一次修改后截图看起来差不多”不满足本任务。
36. 如果连续两轮同一参数来回变化、总体误差不再下降或完成六轮仍无法满足容差，停止盲调，记录阻塞原因并按停止条件处理。

### F. 局部容差与验收测量

将参考裁切按统一缩放映射到本项目内容区后，使用以下首版容差：

- Name、CombatSummary、Portrait、TargetValue、八个 Stat Entry 的每条边位置误差不超过 `8 px`；
- Stat Icon 的中心位置和可见尺寸误差不超过 `4 px`；
- 两列起点、column gap、row gap 误差不超过 `6 px`；
- 文字基线或视觉顶部误差不超过 `6 px`；
- Name/CombatSummary/Label/Value 的可见字形高度与参考对应层级相差不超过 `10%`；
- 相同行左右词条的顶边和底边误差不超过 `2 px`；
- 任意 Icon、Label、Value 不得互相覆盖或越出词条背景；
- 任意真实数值和 `--` 不得被裁切；
- 三种选择来源切换后的同名组件 rect 误差不超过 `1 px`。

说明：

- 图6字体、内容和本项目不同，上述容差用于约束局部层级和空间关系，不代表像素级复刻；
- 如果某项因源 Sprite 透明边、字体字面框或不同文字长度无法满足，报告必须给出测量证据和保留理由；
- 不得通过改变数据内容来规避文字长度。

### G. 字符串和极端布局检查

37. 至少覆盖以下内容状态：
    - 真实空中文名占位 `--`；
    - 一个隔离视觉 fixture 的中等长度中文名；
    - 四位和五位 MaxHP；
    - 短数值 `0/1/2`；
    - 较长攻击间隔或移动速度显示；
    - `CurrentHP/MaxHP` 字符数不超过 9；
    - `CurrentHP/MaxHP` 字符数超过 9，触发 20px 规则。
38. 视觉 fixture 只能存在于截图/测试入口或测试代码，不能写入正式单位 JSON、PlayerState 或目录。
39. 文本溢出策略必须显式：优先保留字号层级和右对齐；若必须缩放、截断或限制字符，现有 SPEC 未确认时停止询问，不得静默选择。

### H. 截图 manifest 和报告

40. 每个 capture record 至少记录：
    - capture stage/iteration；
    - source（staging/deployed/enemy/visualFixture）；
    - resolution、Canvas scale、Panel rect；
    - unitId/typeId 或 visual fixture ID；
    - Name、CombatSummary、Portrait、TargetValue、HP bar rect；
    - 每个 Stat Entry 的 Background/Icon/Label/Value 屏幕 rect；
    - 每个 Text 的 font asset、fontSize、alignment 和实际字符串；
    - 每个 Icon 的 Sprite/资源名；
    - reference rect、mapped target rect；
    - 本轮误差摘要；
    - PNG 和局部裁切路径。
41. 新建 `docs/UI-INFO-002-REPORT.md`，至少包含：
    - baseline 问题；
    - 图标补全结果；
    - 每轮修改参数和原因；
    - 最终布局参数；
    - 每项容差结果；
    - staging/deployed/enemy 一致性；
    - 无法完全匹配的差异；
    - 实际测试、构建和截图命令；
    - 未验证项。
42. 报告只能引用实际存在且已打开审查的截图。

### I. 代码与生命周期约束

43. 图标和布局对象只在信息面板构建时创建一次；刷新数据时只更新 Sprite、文本和必要的动态宽度。
44. 不在 `Update` 中创建 GameObject、Sprite、Material 或分配图标映射。
45. Resources/Sprite 加载使用缓存，并与现有 `sprites` 字典或独立缓存职责清晰。
46. 视觉 fixture 不进入正式自动启动路径，只有明确截图/测试参数时可用。
47. 不改变 `UnitDetailResolver`、PlayerState、BattleInput 或战斗结果。
48. 不为布局验证添加 RaycastTarget、Collider 或可交互行为。

### J. 文档更新

49. `docs/ARCHITECTURE.md` 只记录实际新增的集中布局参数、图标加载和截图证据入口。
50. `docs/TEST_PLAN.md` 记录实际执行的布局测试、Player 截图、容差和人工判断。
51. `docs/references/ui/battle_hud/UI_SPEC.md` 已经是需求依据；只有当最终测量补充的是明确、稳定且不改变用户规则的数值时才可增量更新。若测量结果与现有规则冲突，停止询问，不得自行改 SPEC。

## 兼容与迁移要求

- 保留 `UI-INFO-001` 的 `UnitDetailSnapshot`、三种选择来源、数值格式和敌方只读权限。
- 保留 `FormalBattleHudUi005` 的公开/测试入口；若拆出布局辅助类型，调用方行为不变。
- 保留现有信息面板总体尺寸、上下背景、HP 条公式和下半页签占位。
- 保留所有图标资源 GUID、TextureImporter、Sprite 切片 rect、spriteID 和 internalID。
- 不把独立 PNG 重新合并进图集，也不从图集复制出重复图片。
- 不创建第二套 UnitInformationPanel；正式截图和游戏都使用同一个生产面板。
- 不按选择来源或单位类型维护多套位置常量。
- 不删除 UI-INFO-001 测试；只在需求已变更且有证据时更新几何期望。
- 截图产物不得写入 `Assets/`，避免生成 `.meta`。
- 如果新增项目内裁切/叠图脚本，必须无外部上传、无后台服务、返回可靠退出码并记录输入输出。

## 验证要求

### 自动验证

优先使用少量、稳定的结构断言，不为每个像素建立脆弱测试。

至少断言：

1. 九个词条都存在 `Background/Icon/Label/Value` 或等价层级；
2. 每个词条的 Icon 使用正确资源/Sprite；
3. 所有 Icon `preserveAspect=true`、`raycastTarget=false`；
4. 九个 Icon 在构建后非空；缺失资源路径能产生稳定诊断；
5. 八个右侧词条具有相同 width/height；
6. 同一行左右词条顶边和底边一致；
7. 四行 row gap 与两列 column gap 来自集中布局参数；
8. staging、deployed、enemy 切换不改变组件 rect；
9. 短值、长值和 `--` 不与 Icon/Label 重叠；
10. 现有 CurrentHP/MaxHP、rarity、elite 和数值格式测试继续通过；
11. visual fixture 只在截图/测试参数下启用，正常 SampleScene 启动不可见。

可以为纯布局计算增加 EditMode 测试；真实资源与 RectTransform 层级使用现有 PlayMode 测试。不要依赖屏幕像素颜色作为唯一自动断言。

### Unity 编译或运行验证

- 每个主要阶段至少执行一次 Editor 编译；最终必须再执行一次干净编译；
- 运行相关 EditMode 测试，记录测试数、失败数和 XML；
- 运行 `StagingHudScenePlayModeTests` 及新增定向 PlayMode 测试；
- 运行现有 1920×1080 Player 截图入口；
- 最终执行一次 Windows Standalone 构建和 Player 截图运行，因为本任务修改正式 Player HUD；
- 检查 Console/Player 日志无 Missing Sprite、Missing Script、重复对象、NullReference、字体缺失或截图失败；
- 验证 PNG 可解码、尺寸正确且不是空白/全黑/单色帧；
- 所有未执行验证明确标记“未验证”，不得沿用旧结果证明本轮通过。

### 人工视觉检查

Agent 必须实际打开：

- 图6原图；
- `830×420` 参考裁切；
- baseline 局部裁切；
- icons 局部裁切；
- structure、entry-layout、typography 和 final 局部裁切；
- staging、deployed、enemy 的最终 1920×1080 截图。

逐项判断：

1. 九个词条图标是否齐全且语义正确；
2. 图标是否保持宽高比、大小一致且没有被背景吞没；
3. 名称和第二行是否形成与图6相近的层级；
4. 头像、目标价值和右侧网格的位置关系是否接近；
5. 两列四行是否对齐、密度和留白是否接近；
6. Label 和 Value 的视觉字号、基线和灰白层级是否接近；
7. 长短数值是否保持右对齐且不覆盖；
8. 三种选择来源是否没有跳位；
9. HP 条、下半页签和其他 HUD 是否没有被本任务误改；
10. 整体是否仍然是本项目真实数据，而不是复制图6文本。

### 无法执行时

- 无法生成截图时，本任务不能宣称视觉完成；
- 只生成截图但未打开审查时，视觉状态记为未验证；
- 无法读取图6局部像素或测量边界时，停止并报告工具缺口，不得凭记忆调整；
- 字体差异导致无法满足视觉字高时，记录实际测量和可选方案；未经批准不得替换字体；
- Player 截图失败但 Editor 截图成功时，分别记录，不得合并为“通过”；
- 若安装或使用外部工具会触发 `AGENTS.md` 的审批条件，必须先停止询问。优先使用现有本地图片查看、Unity 截图和项目内脚本。

## 验收标准

- `UI-INFO-001` 的数据、选择和权限行为保持不变；
- 九个词条均显示正确图标；
- 八个右侧词条不再是只有 Label/Value 的无图标结构；
- 图标加载有集中映射和缓存，不在刷新循环中重复创建资源；
- 图6的 `830×420` 参考区域已裁切、测量并保存结构化结果；
- 至少存在 baseline、icons、structure、entry-layout、typography、final 六类证据目录；
- 每轮均有原始截图、局部裁切、manifest 和调整记录；
- Agent 已实际打开并审查每个关键阶段截图；
- Name、CombatSummary、Portrait、TargetValue 和八个 Stat Entry 的位置/尺寸达到本文容差，或对每个超差项给出不可消除的具体证据；
- 九个词条内部 Icon/Label/Value 无重叠、无裁切并保持正确对齐；
- staging、deployed、enemy 三种来源使用同一布局且切换不跳位；
- 空名称、代表性中文名、短值、长值和两种 HP 字符长度均经过检查；
- 没有修改真实单位 JSON 来制造视觉样本；
- 没有修改图6、源 PNG、Sprite GUID、Battle Core、PlayerState、Package 或 ProjectSettings；
- Editor 编译、相关 EditMode/PlayMode、Windows 构建与 Player 截图结果可核对；
- `docs/UI-INFO-002-REPORT.md` 完整记录最终参数、证据、差异和未验证项；
- 最终 diff 只包含本任务允许的 UI、测试、截图工具和文档修改。

## 停止并询问我的条件

- `UI-INFO-001` 的数值、图标资源或三种选择来源尚未完成，导致本任务无法只做视觉拟合；
- `UI_SPEC.md` 与图6指定区域对同一组件给出实质冲突的布局要求；
- 需要改变信息面板总体宽度、上下分区、HP 条规则或字段顺序；
- 需要决定文字截断、自动缩小、换行或横向滚动等尚未确认的玩家可见行为；
- 需要修改真实单位中文名、数值或 fixture 来适配布局；
- 需要替换字体文件、重绘图标、修改源 PNG 或 Sprite 切片；
- 连续两轮同一参数来回变化、误差不再下降，或六轮后仍有多个主要组件超过容差；
- 图6和本项目因字体/资源差异无法继续接近，且下一步需要用户选择取舍；
- 需要大幅重写 `FormalBattleHudUi005`、SampleScene 或整个截图系统；
- 需要修改 Battle Core、PlayerState、UnitDetail 数据语义或选择权限；
- 需要安装第三方 Unity Package、插件、Skill、MCP、桌面程序或上传项目截图；
- 工作区同一 UI 文件存在无法安全合并的用户修改；
- 测试只能通过降低原有数据、权限或生命周期断言。

## 完成报告格式

最后按以下结构报告：

1. 修改文件；
2. baseline 真实状态和主要差异；
3. 九词条图标映射与加载方式；
4. 集中布局参数和坐标映射；
5. 图6参考裁切与测量文件；
6. 每轮调整内容、参数变化和原因；
7. 各阶段截图、局部裁切、叠图和 manifest 路径；
8. 最终组件 rect、字号、基线、行距、列距；
9. 各项容差的实际测量结果；
10. staging/deployed/enemy 一致性；
11. 长短文本和 HP 状态检查；
12. 实际执行的命令和 Unity 操作；
13. Editor 编译、EditMode、PlayMode、构建和 Player 截图结果；
14. 测试数量、失败项、XML 和日志路径；
15. Agent 的逐图视觉判断；
16. 未验证项；
17. 无法完全匹配图6的差异及原因；
18. 所作假设；
19. 遗留风险；
20. 最终 diff 审查。
