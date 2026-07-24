# PREP-DEPLOY-001 设计：已部署单位选中后拖动换位

## 目标与边界

在准备阶段，让玩家先选中一个已部署单位，再通过后续拖动把它移动到空部署格或与另一名己方已部署单位交换位置。

玩家状态仍是阵型和费用的唯一权威。场景单位、鼠标射线、Collider 与拖动预览只负责输入和表现。本任务不改变待部署单位拖入占用格的替换、撤退、自动部署、战斗阶段移动、Battle Core、场景资源或 Package。

## 方案选择

采用对既有 `PlayerState → StateDrivenDeploymentController → PreparationUnitViewCoordinator` 链路的最小扩展：

- `PlayerState` 新增 `TryRelocateDeployed(unitId, x, y)`，集中处理校验、空格移动、友方交换与同格 no-op。
- 控制器把拖动会话标注为待部署或已部署重定位来源，避免向错误命令提交。
- 已部署重定位临时移动现有视图；直到指针松开都不写入 `PlayerState`。命令结果再由快照驱动所有视图到权威位置。

不使用独立 ghost，避免额外资源、销毁时机和双视图一致性问题；不拆分为第二套控制器，保留现有选择、撤退和阶段锁的单一所有者。

## 状态命令

`TryRelocateDeployed` 按固定顺序验证：单位存在、单位处于 `Deployed`、目标在一基 `9×4` 范围内、目标不是蓝门 `(5,1)`，随后区分目标类型。

| 目标 | 结果 | 状态变化 |
| --- | --- | --- |
| 原格 | `Success` no-op | 无：不增版本、不通知、不改费用 |
| 空合法格 | `Success` | 仅被拖动单位的 Formation 更新；增版本并通知一次 |
| 另一己方已部署单位 | `Success` | 两个单位在同一命令内交换 Formation；增版本并通知一次 |
| 越界或门格 | 现有明确失败码 | 无 |
| 不存在或非部署区单位 | 现有明确失败码 | 无 |

该命令不新增费用、容量、替换或敌方占格规则。真实移动和交换在完成两项 Formation 更新后统一调用现有变化通知，确保 `PlayerUnitCollection` 派生投影与外部观察者只看到完整快照。

## 输入与预览

首次点击部署单位仅调用现有选择路径。只有按下的单位 ID 与 `selectedUnitId` 一致、控制器处于准备阶段且移动超过既有拖动阈值时，才创建已部署重定位会话。

会话保存原始权威 Formation、单位 ID、类型 ID、候选坐标和原视图位置。拖动期间视图可自由跟随部署平面上的鼠标投影，但不修改状态；松手时将候选交给 `TryRelocateDeployed`。对屏幕 UI 的起始指针不开始世界拖动。

成功移动或交换由 `Changed` 后的快照重新定位相关 view。失败、取消和 no-op 没有可靠的变化事件可等待，控制器直接恢复保存的权威原位置并重新绑定选择效果。所有这些分支保留原始拖动 unit ID 为选中项。

## 生命周期与阶段

`SetInteractionEnabled(false)`、`SetPreparationViewsVisible(false)`、进入战斗、`OnDisable`、`OnDestroy` 与场景重载都取消活动会话：恢复预览位置、清空临时会话且不调用状态命令。恢复准备阶段时，协调器从持久 `PlayerState` 快照重建/同步视图，因此不会残留上一轮拖动表现。

选择事件继续使用 `DeployedSelectionChanged(string unitId)`；HUD 因而不需要新的选择协议，只需随现有 ID 重新解析对应快照/视图。

## 验证

- EditMode：空格移动、友方交换、同格 no-op、越界/门格、单位不存在/非部署、费用不变、版本/通知次数、摘要变化和确定性序列。
- PlayMode：先选中才允许重定位、空格移动/交换后的视图和选择、无效和同格恢复、阶段锁取消，以及现有部署、撤退和准备到战斗回归。
- 运行：执行 Unity 编译、相关 EditMode/PlayMode；若可用，再在 SampleScene 手工执行移动、交换、门格失败和完整 Preparation→Battle→Preparation 流程。

验证无法实际运行时必须明确标记为未验证，不以静态检查代替。
