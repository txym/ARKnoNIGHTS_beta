# PREP-DEPLOY-001：已部署单位选中后拖动换位

## 角色

你是本 Unity 项目的准备阶段玩家状态与部署交互 Agent。

你的职责是在现有 `PlayerState → StateDrivenDeploymentController → PreparationUnitViewCoordinator` 边界上，实现“先选中已部署单位，再拖动并在松手时移动或交换位置”的完整准备阶段闭环。

玩家状态是权威；世界 GameObject、拖动预览、Collider 和鼠标命中只负责表现与输入。不得让 Transform、Unity Physics 碰撞或拖动中的临时对象成为阵型真相。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`，重点是 5.2.2～5.2.4
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/UI_TASK_TABLE.md`
- `docs/UI-001.md`
- `docs/UI-002.md`
- `docs/UI-003.md`
- `docs/UI-004.md`
- `docs/UI-005.md`
- `docs/UNIT-DATA-001.md` 及其完成报告
- `docs/references/ui/battle_hud/UI_SPEC.md`，重点是选择路由和部署单位世界选择效果
- `docs/decisions/`（若存在）
- `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs`
- `Assets/Game/Runtime/Deployment/StateDrivenDeploymentController.cs`
- `Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs`
- `Assets/Game/UI/FormalHud/StagingHudController.cs`
- `Assets/Game/UI/FormalHud/StagingHudLayout.cs`
- `Assets/Game/Runtime/Initial/FormalBattleHudUi005.cs`
- `Assets/Resources/Prefabs/DefaultUnit.prefab`
- `Assets/Scenes/SampleScene.unity` 中与部署网格、相机、HUD、EventSystem、准备阶段单位和选择效果有关的文本区域
- `Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs`
- `Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs`
- `Assets/Game/Tests/PlayMode/Battle/PreparationBattleLoopPlayModeTests.cs`
- 所有直接引用 `PlayerOperationCode`、`TryDeploy`、`TryRetreat`、`PreparationInteractionState`、`DeployedSelectionChanged` 或准备阶段单位视图的代码

开始前执行 `git status --short`。玩家状态、部署控制器、正式 HUD、SampleScene、DefaultUnit 和测试均可能有未提交修改；必须增量工作，不得覆盖。确认没有其他 Unity Editor 或 batchmode 进程占用项目。

## 任务背景

当前真实状态：

- `PlayerState` 已维护单位的 Staging/Deployed/Overflow/Shop 区域、一基 `9×4` 阵型坐标、部署费用和变化版本；
- `TryDeploy` 能将待部署单位放入空部署格，`TryRetreat` 能撤回单位，但没有已部署单位重定位或交换命令；
- `StateDrivenDeploymentController` 已有 Idle、Dragging、SelectedDeployed、Disabled 状态；
- 当前 Dragging 只用于从待部署槽拖出单位；已部署单位只能被点击选中并显示撤退入口；
- `PreparationUnitViewCoordinator` 根据玩家快照创建或移动场景单位，因此已有“状态驱动视图”基础；
- 准备阶段结束时 UI-004 会关闭交互并封存 BattleInput；
- SPEC 仅概括已部署单位可以交换位置，尚未记录本轮确认的完整拖动规则。

本任务需要先补充权威玩家状态的原子命令，再让交互层只在“该单位已经被选中”的前提下开始世界拖动。成功或失败后都必须保持原单位选中，且不能改变部署费用。

## 本任务目标

1. 在 `PlayerState` 增加一个原子的已部署单位重定位命令。
2. 目标为空部署格时移动选中单位。
3. 目标被另一名己方已部署单位占用时，原子交换两个单位的阵型坐标。
4. 目标是原格时返回成功的 no-op，不修改状态版本、不触发 Changed。
5. 目标越界、是门格或无法解析时失败，任何玩家状态和部署费用都不改变。
6. 第一次点击已部署单位只负责选择；只有已经处于选中状态的该单位才能在后续指针操作中开始拖动。
7. 拖动期间使用临时表现，不提前修改 `PlayerState`。
8. 松手时提交一次状态命令；成功后两个相关单位视图按快照定位，失败后原单位返回原格。
9. 成功、失败和 no-op 后都保持原始 unit ID 被选中，世界选择效果跟随它的新位置或原位置。
10. 准备阶段交互关闭、进入战斗、对象销毁或场景重载时取消未提交拖动且不残留预览。
11. 为移动、交换、无效目标、同格 no-op、费用不变、版本/通知和阶段锁增加有限但完整的验证。
12. 先更新 SPEC 的规则，再实现代码；完成后更新 ARCHITECTURE 和 TEST_PLAN 的真实状态。

## 已确认规则

- 操作只允许在准备阶段进行。
- 必须先选中部署区单位，再拖动该已选中单位。
- 拖到空的合法部署格：单位移动到目标格。
- 拖到另一名己方已部署单位所在格：两名单位交换位置。
- 拖回自己的原格：操作成功但状态不变。
- 拖到场外、无效坐标或蓝门 `(5,1)`：操作失败，阵型不变。
- 成功移动、成功交换、同格 no-op 或失败后，最初被拖动的单位都保持选中。
- 交换后选择仍绑定原始 `unitId`，不是绑定目标格或被交换的另一单位。
- 已部署单位之间移动/交换不扣除、返还或重新判断部署费用。
- 操作不涉及待部署区容量、堆叠、替换确认或撤退规则。
- 一基本地部署坐标范围是 `x=1～9, y=1～4`，蓝门 `(5,1)` 不可部署。
- 场景物体只投影玩家状态；不得用 Transform 占位作为权威判断。
- Unity Physics/Raycast 可以用于确定点击对象或鼠标投影，但碰撞结果不能直接修改阵型或绕过状态层校验。

## 不属于本任务的内容

- 不实现从待部署区拖到已占用格的替换；
- 不实现交换费用、替换费用、出售、商店购买、待部署堆叠变化或二次确认；
- 不实现战斗阶段移动、战斗重定位、远程单位或技能位移；
- 不改变自动部署、Overflow 清理、准备倒计时或战斗输入坐标转换；
- 不改变 `TryDeploy` 和 `TryRetreat` 已确认的费用规则；
- 不实现网格寻路、动画曲线、Tween、拖动粒子或美术重做；
- 不让演示层写回 Battle Core 或玩家状态；
- 不修改 DefaultUnit 的战斗动画、血条、Spine 或渲染材质；
- 不升级 Input System、Unity/Package 或引入第三方拖拽库；
- 不重写整个部署控制器、正式 HUD 或 SampleScene。

## 预计影响文件或目录

候选范围：

- `docs/SPEC.md`
- `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs`
- `Assets/Game/Runtime/Deployment/StateDrivenDeploymentController.cs`
- `Assets/Game/Runtime/Deployment/` 下必要的窄交互/预览辅助类型
- `Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs`
- `Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs` 或新增同程序集的定向部署测试
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`

只有在现有运行时挂载方式无法接入拖动输入时，才考虑最小修改 `Assets/Resources/Prefabs/DefaultUnit.prefab` 或 `Assets/Scenes/SampleScene.unity`。开始前必须先证明运行时动态挂载方案不够，并审查序列化差异。不得修改单位 JSON、Battle Core、Package 或 ProjectSettings。

## 实施要求

### A. 先写入规则和状态测试

1. 在 `docs/SPEC.md` 的 5.2.3 中写入本提示词的移动、交换、no-op、失败、选择和费用规则；不要改写其他回合机制。
2. 在修改控制器前，为 `PlayerState` 新命令建立失败优先的 EditMode 测试。
3. 命令名采用现有风格，推荐 `TryRelocateDeployed(unitId, x, y)` 或语义等价名称；不要暴露可任意修改内部 Formation 的 setter。

### B. 实现 PlayerState 原子命令

4. 按以下顺序校验：
    - unit ID 存在；
    - 单位处于 Deployed；
    - 目标坐标在 `9×4`；
    - 目标不是门格；
    - 再判断同格、空格或友方占用。
5. 同格返回 `Success`，返回当前快照，不增加 version，不触发 Changed。
6. 空格移动只修改被移动单位的 Formation。
7. 友方占用时在一次命令内交换两者 Formation；中途不得对外发出半完成快照。
8. 实际移动或交换只增加一次 version，只触发一次 Changed，并同步一次 `PlayerUnitCollection` 派生投影。
9. 失败不得修改 Formation、Cost、version、Changed 次数或其他单位。
10. 不为本任务新增敌方占格分支；准备阶段玩家状态中只有己方单位。若现有数据允许敌方混入同一 PlayerState，停止并报告架构冲突。
11. 如果现有 `PlayerOperationCode` 无法区分必要失败，可最小增加明确 code；不得用 `CoordinateOccupied` 表示成功交换。

### C. 扩展已部署单位输入手势

12. 保留第一次点击选择的现有行为。未选中的已部署单位收到拖动起始手势时，最多先完成选择，不得在同一次首次手势中直接换位。
13. 当且仅当指针按下的世界单位 `unitId == selectedUnitId` 且交互处于 Preparation/SelectedDeployed 时，允许后续移动超过正常拖动阈值后进入“已部署单位拖动”状态。
14. 释放前不提交状态。单纯点击已选中单位且未形成拖动时不移动、不取消选择。
15. 明确区分待部署拖动会话和已部署重定位会话，避免共享字段导致一个操作提交到错误命令。可以扩展状态枚举或给会话增加来源类型，但不要建立第二套全局部署系统。
16. 继续使用相机射线与部署平面把指针位置转换为候选本地坐标；坐标合法性最终由 PlayerState 再验证。
17. 指针处于屏幕 UI 上时，不应误触发世界单位拖动；已经开始的拖动在松手时仍必须有明确的取消或失败路径，不得卡住。

### D. 拖动预览和视图同步

18. 拖动预览可以移动原视图的临时表现根、创建无权威 ghost，或采用等价最小方案；无论哪种方案，`PlayerState.Snapshot` 在松手前必须不变。
19. 如果临时移动原视图，必须保存权威原位置，并在失败、取消、阶段切换和销毁时恢复。
20. 目标格有己方单位时，可以只显示目标候选，不要求提前播放交换动画；松手成功后由同一快照让两名视图到达交换后的格。
21. `PreparationUnitViewCoordinator` 继续根据 unit ID 复用视图；不能因交换而销毁再创建两个完整单位，除非现有架构确实无法保持且有证据。
22. 状态 Changed 到达后，选择效果重新绑定原始 unit ID 的视图并跟随其新位置。
23. 失败或同格 no-op 没有 Changed 事件，因此控制器必须显式结束预览、恢复权威位置并刷新/保持选择效果。
24. 结构化日志至少区分 relocate success、swap success、same-cell no-op、invalid target、gate target 和 cancel，并包含 unit ID、原坐标、目标坐标；不得每帧刷日志。

### E. 阶段与生命周期

25. `SetInteractionEnabled(false)`、`SetPreparationViewsVisible(false)`、进入 Battle、OnDisable、OnDestroy 和场景重载都必须取消未提交拖动。
26. 取消过程中不调用 PlayerState 命令，不改变 Cost/Formation/version。
27. Battle 返回 Preparation 后，视图从持久 PlayerState 的最新 Formation 重建；上一轮拖动预览不得残留。
28. 保留现有待部署拖出、撤退按钮、共享选择和信息面板选择事件。

### F. 文档

29. `docs/ARCHITECTURE.md` 只在实现完成后记录新命令、拖动会话和快照驱动同步。
30. `docs/TEST_PLAN.md` 记录实际运行的 EditMode/PlayMode/人工检查及未验证项。
31. 不把“可以拖动”写成已验证，除非实际执行对应场景检查或 PlayMode 断言。

## 兼容与迁移要求

- 保留现有 `TryDeploy`、`TryRetreat`、`RemoveOverflowUnits` 行为和调用方。
- 保留 `PlayerStateSnapshot` 的只读语义和玩家状态贯穿整局的生命周期。
- 若新增 `PlayerOperationCode`，同步所有 switch、日志、测试和 UI 错误显示；不改变旧 code 的含义。
- 不改变待部署槽拖动所使用的 unit ID 选择规则。
- 保留 `DeployedSelectionChanged` 和 staging selection 互斥行为；如必须扩展事件载荷，提供最小迁移并更新 `FormalBattleHudUi005`。
- 保留 `PreparationGridProjection` 的现有世界坐标映射。
- 不依赖 Animator/Spine 动画结束决定命令是否成功。
- 不依赖 Collider 重叠决定目标格是否被占用；占用只查 PlayerState。
- 不删除或重新生成 DefaultUnit、SampleScene 或脚本 `.meta`。
- 若必须改 Prefab/Scene，保持 GUID 和既有组件引用，并审查 YAML 差异。

## 验证要求

### 自动验证

EditMode 至少断言：

1. 已部署单位移动到空合法格成功；
2. 拖到友方占用格后两个 Formation 原子交换；
3. 同格返回成功，但 version 和 Changed 次数不变；
4. 越界和门格失败，全部状态不变；
5. 不存在单位、非 Deployed 单位失败；
6. 实际移动/交换各只增加一次 version、只通知一次；
7. 移动、交换、失败和 no-op 前后 DeploymentCost 完全相同；
8. CanonicalSummary 在实际变更时变化，在失败/no-op 时不变；
9. 重复执行同一固定操作序列得到相同快照。

PlayMode 或等价真实交互测试至少覆盖：

10. 第一次点击只选择，不移动；
11. 未选中的单位不能在首次手势中直接换位；
12. 已选中单位拖到空格后视图与 PlayerState 坐标一致；
13. 拖到占用格后两个视图交换，选择仍属于原始 unit ID；
14. 拖到门格/场外后视图回原位，选择保持；
15. 同格松手不产生额外状态通知；
16. 阶段锁或控制器禁用会取消进行中的拖动且状态不变；
17. 现有 staging deploy、retreat 和准备→战斗流程仍可运行。

不要求建立像素级拖动轨迹测试，也不要求模拟所有鼠标采样点。

### Unity 编译或运行验证

- 执行 Editor 编译；
- 运行相关 EditMode 测试并记录数量；
- 运行相关 PlayMode 测试并记录数量；
- 在 SampleScene 的 Preparation 阶段实际执行一次空格移动、一次两单位交换、一次门格失败；
- 等待 Preparation 进入 Battle，确认封存输入使用交换后的最终坐标；
- 战斗结束返回 Preparation 后确认位置仍是玩家状态中的新阵型；
- 检查 Console 无空引用、重复提交、残留 drag session 或 Missing Script；
- 若触碰 Prefab、Scene 或 Player 输入路径，执行现有 Windows Standalone 构建/冒烟；否则可明确记录未构建。

### 人工检查

1. 第一次点击单位只显示选择效果；
2. 第二次在同一已选中单位上拖动时，预览跟随自然且不会复制出永久单位；
3. 空格移动后选择菱形和撤退按钮跟随原单位；
4. 交换后两单位位置正确，选择没有跳到另一单位；
5. 失败时没有闪到错误格、扣费、取消选择或遗留 ghost；
6. 选择待部署槽与选择已部署单位仍互斥；
7. 点击撤退按钮不会被误判为拖动；
8. Preparation 倒计时结束过程中没有一次迟到的拖动提交。

### 无法执行时

- 无法可靠合成鼠标拖动时，状态层测试可以标为通过，但真实手势和视觉必须分别标为“未验证”；
- 截图不能证明原子状态交换，必须有结构化快照断言；
- PlayMode 测试无法退出或缺少 XML 时，记录实际状态，不得写通过；
- 无法构建 Player 时说明是否影响本任务输入路径，并提供人工补验清单。

## 验收标准

- SPEC 5.2.3 已准确记录本轮确认规则；
- `PlayerState` 提供一个原子重定位命令；
- 空格移动、友方交换、同格 no-op 和无效失败行为可由自动断言区分；
- 实际移动/交换只通知一次；失败/no-op 不通知；
- 所有分支都不改变 DeploymentCost；
- 第一次点击只选择，只有已选中单位的后续拖动才能提交换位；
- 松手前权威玩家状态不变；
- 成功、失败、no-op 后选择都绑定原始 unit ID；
- 阶段切换和销毁不会残留预览或迟到提交；
- 现有 staging deploy、retreat、自动部署和准备→战斗流程没有新增回归；
- Editor 无新增编译错误；
- 相关自动测试实际执行且结果可核对，未执行项明确标记；
- 最终 diff 没有单位数据、Battle Core、贴图、图集、Package、ProjectSettings 或无关资源变化。

## 停止并询问我的条件

- 现有部署坐标、门格或准备阶段规则与本提示词冲突；
- 必须决定未确认的替换、费用、动画或敌方占格行为；
- 需要让首次拖动未选中单位也直接移动，或需要更改“先选中”交互；
- 需要修改战斗阶段移动、Battle Core 或封存后的阵型；
- 需要大幅重写 `PlayerState`、部署控制器或正式 HUD；
- 需要破坏性修改 SampleScene/DefaultUnit、删除组件或重新生成 `.meta`；
- 工作区同一区域有无法安全合并的用户修改；
- 需要升级 Unity/Input System、安装第三方拖拽库或修改 Package；
- 自动测试只能通过降低原有断言或改变已确认规则。

## 完成报告格式

最后按以下结构报告：

1. 修改文件和序列化资源；
2. 新增 PlayerState 命令及每个结果分支；
3. 拖动手势状态机与“先选中”保证；
4. 预览、提交、失败恢复和选择保持方式；
5. 阶段切换与清理；
6. 实际执行的命令和 Unity 操作；
7. Editor 编译结果；
8. EditMode/PlayMode 测试数量、结果、XML/日志路径；
9. SampleScene 人工移动/交换/失败结果；
10. 构建结果或未验证说明；
11. 未验证项；
12. 所作假设；
13. 遗留风险；
14. 最终 diff 审查；
15. UI-INFO-001 可依赖的选择事件、unit ID 和最终 Formation 接口。
