# UI-INFO-002 验收报告

## 完成范围

- 单位信息面板的上半部几何由 `UnitInformationPanelLayout` 集中定义：头像、标题、副标题、目标价值、两列四行属性条和 HP 区域在 720 宽面板内按同一基准缩放。
- 九个属性图标均有显式来源和可审计名称。已有图集项直接复用 Sprite；七张独立 PNG 使用 `Texture2D` 一次性创建并缓存 Sprite，不要求将资源导入为 Sprite。
- 属性条背景使用 `RGBA(0.32, 0.32, 0.32, 0.94)`；图标为 30px，标签为 23pt，数值为 29pt。`TargetValue/Value` 与 `BattleStatusPanel/PlayerHealth` 共用淡红 `RGBA(1, 0.471, 0.471, 1)`。
- 单位名称来自 Player-safe catalog 的 `displayNameZhHans`：真实单位显示“狂暴的猎狗pro”和“果冻小子”；未配置名称仍显示 `--`，不会回退成 `resourceKey`。
- 新增仅供测试/截图使用的显式视觉夹具。只有 Player 带 `-uiCaptureVisualFixture` 参数时才渲染空名或中等长度中文名；该夹具不会写入 `PlayerState`、单位目录或战斗输入。

## 视觉证据与人工审查

| 阶段 | 目录 | 结果 |
| --- | --- | --- |
| baseline 至 final | `Artifacts/UI-INFO-002/00-baseline/` 至 `05-final/` | 保留基线、图标、结构、词条布局、字号和最终状态的演进证据。 |
| 名称与对比度 | `Artifacts/UI-INFO-002/06-name-contrast/captures/` | 显示名、深色词条背景和增大后的图标/文字可见。 |
| 目标价值颜色 | `Artifacts/UI-INFO-002/07-targetvalue-color/captures/` | `TargetValue` 使用与玩家生命相同的淡红色。 |
| 夹具补充 | `Artifacts/UI-INFO-002/08-completion/captures/` | 加入空名、中等中文名和长 HP 数值夹具。 |
| 最终 Player 证据 | `Artifacts/UI-INFO-002/10-final/captures/` | 8 张原始 PNG、6 张 1920×1080 面板裁切、结构化 manifest、调整记录和图 6 并排对照。 |

`scripts/ExportUiInfoEvidence.ps1` 已对每个阶段输出 panel 裁切、manifest 更新、图 6 并排对照和 `adjustments.md`。最终 manifest 记录了 `captureStage`、Canvas scale、面板/元素 rect、文本、字体、图标、参考映射和证据导出结果。

最终 Player 的夹具记录如下：

| 夹具 | 名称 | HP | 结果 |
| --- | --- | --- | --- |
| `empty-name` | `--` | `18000/18000`，20pt | 无名称时的兼容占位正确。 |
| `medium-name` | `视觉验证单位` | `18000/18000`，20pt | 中文名称、长数字、标签、图标和值均未重叠或裁切。 |

已打开并复核最终空名、中等中文名和图 6 并排图；用户已人工确认鼠标交互测试通过。

## 自动验证

- TDD：夹具入口先按预期失败（入口不存在），实现后单项 PlayMode 转绿；最终全量 PlayMode 已覆盖其可用性和退出清理。
- 全量 EditMode：`Artifacts/UI-INFO-002/10-final/verification/EditModeResults.xml`，`77 passed / 0 failed / 0 skipped`。
- 全量 PlayMode：`Artifacts/UI-INFO-002/10-final/verification/PlayModeResults.xml`，`19 passed / 0 failed / 0 skipped`。
- Windows Player：`Artifacts/UI-INFO-002/10-final/verification/WindowsStandaloneBuild.log` 记录 `result=Succeeded`、`errors=0`、`warnings=1`；产物为 `Artifacts/UI-INFO-002/10-final/WindowsStandalone/ARKnoNIGHTS.exe`。该唯一警告是未修改的 `TagRegistry.freezeAppend`（CS0414）。
- 最终 Player 截图：`Artifacts/UI-INFO-002/10-final/verification/PlayerCapture.log` 记录 `capture.completed count=8`。`10-final/captures/manifest.json` 的证据导出结果为无 PNG 解码、裁切或对照错误，并显式记录了 TargetValue 和全部八个属性词条的 Background/Icon/Label/Value rect。

导出脚本在本机执行策略阻止时以一次性 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ...` 启动；没有修改任何持久执行策略或系统设置。一次隐藏窗口的 Player 截图因窗口未渲染而被判为无效，未计入证据；前台复跑成功后生成了上述最终 10 阶段结果。

## 剩余风险

本任务相关的自动验证、Player 截图证据和用户人工交互确认均已完成。未发现本次改动引入的编译错误、测试失败或未处理异常。
