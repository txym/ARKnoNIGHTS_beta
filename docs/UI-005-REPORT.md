# UI-005 验收审计报告（2026-07-23）

## 结论

UI-005 的正式 HUD、选择路由、字体和本轮要求的关键布局已经实现，并有针对性的 PlayMode、Windows Player 构建与截图证据。**但不应标记为“任务完全完成”**：截图 manifest 没有记录任务要求的完整布局/状态字段，逐图视觉审查结论尚未持久化为逐状态报告，且没有本轮 Windows Player 的完整“准备→战斗→准备”人工流程证据。

当前状态：**已实现，验收证据部分完成；最终验收未完成。**

## 已验证的实现与证据

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 正式 HUD 复用现有状态 | 通过 | `FormalBattleHudUi005` 绑定既有 `PlayerState`、阶段控制器、部署控制器和 BattleDemo；未创建第二个 `PlayerState`。 |
| 资源与未定义业务字段 | 通过 | Cost 显示真实 Cost；赤金、玩家生命保持 `--` 占位，未虚构数值；设置/页签只提供已声明外壳。 |
| 选择互斥 | 通过 | 待部署槽、已部署单位和战斗敌人共用选择路由。`SampleScene_UI005MakesBattleAndStagingInformationSelectionsMutuallyExclusive` 通过。 |
| 字体与本轮数值排版 | 通过（自动化与截图观察） | 中文使用 Noto Sans SC normal；数字使用 `Novecento Wide Normal Regular`。玩家 Cost 与图标横向对齐；`HealthValue` 使用左上锚点、贴近剩余血条右上角，文本 top=5；超过 9 字符时缩至 20px。 |
| HUD 针对性 PlayMode | 5/5 通过 | [ui005-cost-health-elite-final-retry.xml](../Artifacts/UI-005/ui005-cost-health-elite-final-retry.xml)：自动加载、真实部署/撤退、交互锁、选择互斥、既有 HUD/占位字段。 |
| 阶段完整循环 PlayMode | 1/1 通过 | [ui005-phase-loop-audit.xml](../Artifacts/UI-005/ui005-phase-loop-audit.xml)：`SampleScene_AutoLoopsPreparationToBattleAndBackWithoutWritingCombatResultToPlayerState`。 |
| Windows Standalone 构建 | 通过 | [ui005-build-cost-health-elite-final.log](../Artifacts/UI-005/ui005-build-cost-health-elite-final.log) 记录 `build.succeeded`、`errors=0`；产物为 `Artifacts/UI-005/WindowsStandalone/ARKnoNIGHTS.exe`。 |
| Player 自动截图 | 通过 | [player-capture-cost-health-elite-final.log](../Artifacts/UI-005/player-capture-cost-health-elite-final.log) 记录 6 张 PNG 已完成；目录为 [CapturesCostHealthEliteFinal](../Artifacts/UI-005/CapturesCostHealthEliteFinal)。 |
| 本轮重点画面观察 | 通过（有限） | 已打开并检查 1920×1080 的准备未选中、待部署选中、已部署选中、战斗敌人选中，以及 1600×900、1280×1024 截图；重点检查状态栏时钟、槽位堆叠数/Cost、玩家 Cost、信息面板血条和血量标签。目标元素可读、无明显裁切或空白帧。 |

## 未完成或未验证项

1. `CapturesCostHealthEliteFinal/manifest.json` 仅含图片路径、分辨率、阶段、选中单位 ID、Cost 和准备倒计时；缺少 UI-005 第 39 条要求的场景、Canvas scale、fixture/schema、选中 slot、槽顺序和宽度、关键 `RectTransform` 屏幕矩形、敌人数和捕获时间点。因此 **manifest 完整性不通过**。
2. 现有截图已被打开审查，但尚未有逐张、逐状态、与图 1～图 6 对照的持久化视觉报告（包括差异、复拍或明确遗留结论）。因此不能声称参考图的字体、贴图大小、位置和整体视觉已经完全拟合。
3. 本轮完整循环由 PlayMode 自动测试验证；Windows Player 只验证了启动和截图入口，没有人工完成一次“准备→部署→战斗→返回准备”的 GUI 操作，也没有验证拖拽/点击命中区。
4. 工作树包含大量并行/既有修改和未跟踪资源；本次未将其混同为 UI-005 的最终 diff 审查结论。提交前仍需在稳定工作树中复核相关场景、Prefab、`.meta` 与无关改动范围。

## 明确保留的未定义业务

- 赤金初始值和变化规则；
- 玩家生命初始值、扣除和结算规则；
- 设置菜单、技能/阵营/种族页签的业务内容与图标映射。

这些字段保持占位或禁用，不以常量伪装为已接入功能。

## 建议的收尾顺序

1. 扩充截图 manifest，并在每张截图记录 UI-005 第 39 条全部字段。
2. 将图 1～图 6 与对应 PNG 的逐图审查、差异和最终取舍写入本报告；必要时复拍。
3. 在 Windows Player 手工走完一次准备、部署、选择、战斗、返回准备，记录截图与日志。
4. 在工作树收敛后进行最终 diff、Unity 编译和相关全量测试审查，再将 UI-005 标记为完成。
