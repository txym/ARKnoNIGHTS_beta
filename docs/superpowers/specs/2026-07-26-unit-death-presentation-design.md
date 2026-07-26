# 单位死亡表现与 Track 切入设计

## 目标

单位在连续战斗演示中完整播放死亡动画。动画完成后保留 `0.5` 秒，Spine 身体在该阶段逐渐变为纯黑，随后从画面隐藏。任意 Track 节点切入时不补播已经死亡单位的死亡表现。

本功能只改变显示层，不改变 Death 事件、Core 最终状态、胜负、Track 的权威 Tick 或玩家持久状态。

## 玩家可见规则

- 连续播放跨过 Death 事件时，现存单位开始播放目录映射的死亡动画。
- 死亡动画完成后保持最后一帧 `0.5` 秒；这段现实时间内，Spine 身体颜色从当时颜色线性插值到纯黑，Alpha 保持不变。
- `0.5` 秒结束后隐藏整个单位 GameObject。状态条不参与着色，但随单位一起隐藏。
- 死亡动画缺失或播放失败时，保留现有结构化警告并立即隐藏，不推测动画时长。
- 播放器从任意 `PresentationTick` 初次绑定、重新绑定或回退重建视图时，满足 `DeathTick <= PresentationTick` 的单位不创建、不显示，也不补播死亡动画。
- 从死亡前节点切入时单位按该节点的存活状态正常显示；从 Tick `0` Replay 时，后续连续播放仍会正常触发完整死亡表现。
- 正式回合到达最大 Track EndTick 后，等待当前可见的终局死亡表现完成再清理视图和返回准备阶段；终局 Tick 切入没有创建死亡视图时立即完成。

## 方案比较

### 采用：连续死亡由视图完成，切入时过滤已死亡单位

`BattleTrackPlaybackController` 只为切入 Tick 仍存活且应该显示的单位创建视图。已存在的视图在连续播放跨过 Death 时仍收到一次 `PlayDeath`。`UnitSkelPresentationView` 监听实际 Spine `TrackEntry.Complete`，完成后自行执行 `0.5` 秒变黑并隐藏。视图在死亡动画和变黑阶段报告待完成终局表现，播放控制器聚合该状态；正式多战斗协调器到达最大 EndTick 后据此延后 Completed，而不是猜测统一动画时长或增加固定等待。

该方案直接服从真实动画完成点，同时避免为任意 Track seek 重建动画帧和变黑进度，范围最小。

### 不采用：切入后从头补播所有死亡单位

该方案不会泄漏旧视图状态，但会让很早死亡的单位在任意观察切换后重新出现并播放死亡动画，与切入节点的画面语义冲突。

### 不采用：按 Track Tick 精确重建死亡阶段

该方案需要把 Spine 动画时长或死亡表现结束 Tick 引入 Track/接口，并在 seek 时定位动画帧和颜色。当前需求明确不要求死亡动画进度准确性，因此不扩大 Track 数据契约。

## 生命周期与状态

单个真实单位视图的死亡表现为：

1. `Alive`：正常显示。
2. `DeathAnimation`：非循环播放死亡动画，并监听本次 `TrackEntry` 的完成回调。
3. `Blackening`：保持死亡动画最后一帧，经过 `0.5` 秒把 Skeleton RGB 插值到 `0`。
4. `Hidden`：立即停用 GameObject，但不自行销毁。
5. `Disposed`：播放控制器在 Replay、重新绑定或清理时统一销毁视图。

`PlayDeath` 必须幂等，旧 TrackEntry 的回调在中断、停用或销毁时不得继续改变新状态。`Dispose` 应先立即隐藏再请求 Unity 销毁，避免延迟到帧末的旧视图与新绑定视图短暂重叠。若播放器之外的生命周期直接停用或销毁正在死亡的视图，该视图必须进入 `Hidden`、退订回调并停止报告待完成终局表现。

正式回合的终局清理只等待当前已经存在的视图处于 `DeathAnimation` 或 `Blackening`。进入 `Hidden`、缺动画 fallback 或中断隐藏后立即视为完成。重新绑定会先 Dispose 旧视图，再按切入 Tick 过滤死亡单位，因此不会为了终局等待重新创建或补播死亡单位。

## 着色方式

现有 `Spine/Skeleton` Shader 将贴图颜色与顶点色相乘，Spine runtime 也提供 Skeleton 级 RGBA。实现直接修改该单位 Skeleton 的 RGB，不实例化或替换共享材质，不修改 Prefab、Shader 或其他单位。

## Track 切入语义

`UnitPresentationSample.ShouldDisplay` 表示从该 Tick 构建画面时是否应该创建单位。对已 Spawn 但已死亡的单位，点采样应返回 `HasSpawned = true`、`IsAlive = false`、`ShouldDisplay = false`。

播放控制器只在“尚无视图记录”时使用 `ShouldDisplay` 过滤创建；已有视图连续跨过 Death 时不能因点采样变为 false 而立即消失，必须走完死亡动画、变黑和隐藏。回退使控制器清理并重建视图，因此自然应用切入过滤。

## 实现范围

- `UnitPresentationTrack.Sample`：为死亡后的点采样返回 `ShouldDisplay = false`。
- `BattleTrackPlaybackController`：创建新视图时遵守 `ShouldDisplay`，保留连续死亡事件对既有视图的触发。
- `UnitSkelBase` / `UnitSkelPresentationView`：取得本次死亡 `TrackEntry`、监听完成、执行着色和隐藏，并安全清理回调。
- `IBattlePresentationView` / `MultiBattlePresentationCoordinator`：以默认兼容的终局表现状态阻止正式回合在派发终局 Death 的同一帧 Reset。
- 相关 EditMode/PlayMode 测试、`docs/SPEC.md`、`docs/TEST_PLAN.md` 和必要的架构说明。

不修改 Core、Death 事件格式、目录 JSON、场景、Prefab、Shader、Package 或 Unity 版本。

## 验证

- EditMode 先以失败测试证明死亡后的 `UnitPresentationSample` 不再允许切入创建。
- EditMode 验证：绑定死亡后的 Tick 不创建死亡单位；连续播放跨过 Death 仍只调用一次 `PlayDeath`；回退到死亡前 Tick 会重建存活单位。
- PlayMode 使用真实目录和 Spine 资源验证：死亡动画完成前单位保持可见；完成后的 `0.5` 秒内 Skeleton RGB 单调趋近黑色；结束后 GameObject 停用。
- PlayMode 验证 Replay/重新绑定会立即隐藏并清理旧的变黑视图，且切入死亡后 Tick 不出现死亡单位。
- PlayMode 正式回合集成验证：跳到最大 EndTick 后仍保持 Battle/Playing，终局死亡表现完成后才返回 Preparation。
- 运行相关测试后执行全量 EditMode、全量 PlayMode、Unity 编译检查和最终 diff 审查。
- 人工检查真实死亡动画最后一帧、变黑观感、状态条残留和多单位同时死亡时的画面；自动颜色断言不能完全替代视觉验收。
