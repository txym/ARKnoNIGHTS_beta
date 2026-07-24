# PREP-DEPLOY 设计：拖动期间隐藏部署选择框

## 行为

当已选中的部署单位进入重定位拖动会话时，隐藏其世界空间 `DeployedUnitSelectionIndicator`。拖动结束后，只要原始单位仍保持选中，选择框立即重新显示并重新绑定该单位的当前视图。

成功移动、交换、同格 no-op、无效目标、取消、UI 上松手和焦点丢失均恢复选择框；阶段锁、撤退、禁用和销毁继续使用现有清除选择语义，不额外显示选择框。

## 实现

控制器在 `BeginRelocateSelected` 隐藏现有 indicator GameObject；`CancelDrag` 在恢复/提交结束后按 `selectedUnitId` 和 `PreparationUnitViewCoordinator` 显示并绑定它。待部署槽拖动没有选择框，因此不改变其现有预览逻辑。

## 验证

扩展现有场景 PlayMode 测试，覆盖重定位开始时 indicator 不活跃，以及成功交换、门格失败和取消后 indicator 对原始 unit ID 恢复可见。运行定向场景 PlayMode 和 Editor 编译；真实鼠标视觉检查仍在无交互式 Editor 时标记为未验证。
