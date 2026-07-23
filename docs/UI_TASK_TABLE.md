# UI 接入阶段任务总表

## 1. 文档用途

本文档规划固定单场战斗闭环之后的 UI 接入阶段。任务采用独立的 `UI-XXX` 编号，不改变 `docs/PHASE1_TASK_TABLE.md` 中的战斗 `TASK-001`～`TASK-007` 顺序。

本文档是任务规划，不代表功能已经实现或验证。游戏规则以 `docs/SPEC.md` 为准，视觉规则以 `docs/references/ui/battle_hud/UI_SPEC.md` 为准，真实架构和验证口径分别以 `docs/ARCHITECTURE.md`、`docs/TEST_PLAN.md` 为准。

## 2. 当前真实基线

- `SampleScene` 是 Build Settings 中的现有入口场景；不存在另一个已确认的 `SimpleScene`。
- 战斗 `TASK-002`～`TASK-006` 已建立真实单位目录、固定战斗计算、事件回放、Home/Away 投影、场景 Demo 和 Windows Player 验收入口。
- `BattleDemoRoot`、`BattleDemoViews` 与 `BattleDemoUI` 已存在。`BattleDemoController` 仍只会从固定 Resources 路径重新加载整份对战输入；尚无接收运行时玩家快照的公开入口。
- `BattleDemoUI` 是开发调试面板。关闭整个 `BattleDemoRoot` 会同时停用控制器并释放回放，因此正式 UI 只能默认隐藏/禁用调试面板的可视和射线部分，不能关闭整个根对象。
- `PlayerUnitCollection` 已有固定容量、Staging/Deployed/Overflow/Shop 区域桶和 `9×4` 部署网格，但当前公开坐标是零基、没有 Cost、精英化、Buff、堆叠投影、区域迁移命令或调用方。
- 旧 `InitButton → ButtonDebug.Debugbutton → UnitFactory.SpawnAll` 只为每个类型生成一个临时对象；它不是玩家单位状态。`UnitFactory` 还直接读取 `Application.dataPath/GameData/Units/Json`，不能作为正式 Player-safe 自动加载入口。
- 旧 `VirtualSlotPanel` 使用 `[ExecuteAlways]`、固定 12 槽和等宽拉伸，不符合当前最大 13 槽、头像裁切、选中等差扩展和 `180×200` 规格。
- 旧 `UnitDeployment` 从 UI 原型复制 GameObject，接受整个 `9×8` 范围，不更新玩家状态、Cost、待部署堆叠或占位；只能复用其已验证的坐标换算思路，不能继续作为权威部署逻辑。
- `ShopPanel`、`FoldButton`、`InitButton` 和 `ButtonTest` 仍在场景中。当前阶段不实现商店，应默认隐藏旧商店入口。
- `UnitJson` 与 `unit-catalog-v1` 尚无 UI 所需的初始精英化、头像资源路径和部署 Cost 的完整 Player-safe 合约。
- `TASK-007` 的表格仍标记为待执行，但工作区已经存在相关未提交脚本、Prefab 和资源修改。任何 UI 任务接触 `DefaultUnit`、状态条或世界选择表现前，必须先审计其实际完成状态，不能依据任务表状态覆盖现有改动。

## 3. 本阶段目标与边界

本阶段完成以下本地闭环：

```text
真实单位 JSON + Player-safe 类型目录 + 本地玩家测试状态
→ PlayerState / PlayerUnitCollection
→ 有序待部署堆叠槽与准备阶段 UI
→ 状态驱动的手动部署/撤退
→ 30 秒准备阶段
→ Overflow 清理与必要的自动部署
→ 运行时 BattleInput 快照
→ 已有确定性战斗计算与事件回放
→ 战斗结束后返回准备阶段
```

UI 只负责读取快照、显示状态和发起命令。玩家单位、区域、坐标、精英化、Buff 和可用部署费用由玩家状态层维护；战斗演示不得把当前 HP、死亡或胜负写回玩家状态。

本阶段不包括：

- 商店购买、刷新、出售和赤金变化；
- 玩家生命扣除和淘汰；
- 完整多回合结算、四人房间、网络同步或断线重连；
- 部署到已占用格时的完整替换预览；
- 部署区单位位置交换；
- Buff 效果、技能、治疗或护盾战斗逻辑；
- 未在 UI_SPEC 中确认的页签内容、属性图标映射或设置菜单功能；
- 美术资源整体重做、Unity/Package/渲染管线/Input System 升级。

## 4. 已确认的跨任务规则

- 本地玩家状态贯穿整局游戏，阶段切换不重建单位集合或重置 Cost。
- 初始可用部署费用为 `99`；部署扣费，撤退返还 `100%` 已占用费用。
- 单位类型 JSON 增加初始精英化等级；玩家单位实例独立维护当前精英化等级。
- 已确认 `gopro`（`1000`）和 `arcslma`（`5503`）的初始精英化等级均为 `0`；不得按 Rarity 推断。
- 待部署区严格堆叠键至少包含 `type ID + 精英化等级 + 完整 Buff`。
- 待部署槽从左到右按部署 Cost 升序，再按 `type ID` 升序；UI 直接映射玩家状态层提供的有序堆叠槽。
- 待部署区最多 `13` 个堆叠槽；数量为 1 时仍显示 `X1`。
- 本地阵型采用一基 `9×4` 坐标；蓝门 `(5,1)` 不可部署。
- 点击撤退按钮后不二次确认，但仍必须通过容量、堆叠和费用检查。
- 场景进入后自动加载；正式流程不依赖 `InitButton`。
- 准备阶段倒计时为 `30` 秒；战斗演示完成后返回准备阶段。
- 进入战斗阶段时先永久移除 Overflow 单位。
- 若部署区为空，自动选择当前 Cost 足够支付的最高部署费用单位，正常扣费并部署到 `(5,2)`。
- 若部署区仍为空且没有可自动部署单位，则不凭空生成单位，由另一方获胜。
- 单场战斗死亡和当前 HP 不写回玩家单位状态。

## 5. 任务总表

| 状态 | 编号 | 名称 | 单一目标 | 前置任务 | 预计影响区域 | 最小验证 | 机制确认 | 并行建议 | 可观察结果 |
|---|---|---|---|---|---|---|---|---|---|
| 已实现，回归待补 | UI-001 | 本地玩家状态、真实单位 UI 数据与有序待部署快照 | 把 Cost、单位实例、精英化、Buff、区域、坐标、严格堆叠和稳定排序建立为可测试的权威状态，并提供 Player-safe 自动加载测试数据 | 战斗 TASK-006；开始前审计 TASK-007 工作区状态 | `Runtime/Data/Player`、`UnitJson`、真实单位 JSON、`unit-catalog-v1` 生成/加载、Resources 玩家测试数据、相关文档 | 固定输入加载；Cost/区域/一基坐标/堆叠/排序/Overflow 原子操作断言；重复快照一致；Editor 编译 | 两个真实类型初始精英化均已确认为 0；Buff 语义及同 Cost/type 的不同堆叠最终顺序仍不固化 | 不与其他玩家状态或类型目录写入任务并行 | 自动加载后可打印玩家 `cost=99`、各区域单位和按 Cost/type ID 排序的待部署堆叠槽 |
| 已实现，视觉验收待补 | UI-002 | SampleScene 自动启动与待部署区正式 UI | 用 UI_SPEC 的槽位与资源区替换旧 12 槽调试入口，并把 UI 只读绑定到 UI-001 快照 | UI-001 | `SampleScene`、正式 HUD 根、待部署槽组件/Prefab、图集 Sprite 与头像、自动启动组件、旧调试对象显隐、轻量测试 | 布局 EditMode 9/9；真实场景 PlayMode 1/1；1920×1080 与非 1920×1080 人工视觉检查未执行 | 字体、部分边距和同 type 不同堆叠的最终顺序仍按 UI_SPEC/测试规避 | 不与任何修改 SampleScene、图集或待部署 Prefab 的任务并行 | 进入 SampleScene 自动出现真实有序待部署槽和 Cost=99；InitButton/商店默认不出现；BattleDemo 调试面板可用 Ctrl+Shift+F10 切换 |
| 已实现，人工视觉验收待补 | UI-003 | 状态驱动的部署、撤退与世界选择反馈 | 让拖拽和撤退只通过玩家状态命令修改区域、坐标和 Cost，并让场景对象成为状态投影 | UI-002；TASK-007 实际状态已审计 | `Runtime/Deployment`、准备阶段单位视图、待部署交互、世界选择效果、`SampleScene`、EditMode/PlayMode 测试 | EditMode 玩家状态 7/7；真实场景 PlayMode 3/3，覆盖部署、撤退、门格失败和交互锁 | 已占用格替换和部署区单位交换不在本任务；失败 UI 只需明确诊断 | 不与 DefaultUnit、部署场景或选择表现修改任务并行 | 从槽位拖到空格会部署一个真实单位并扣费；门格失败不改状态；撤退立即返还费用并恢复堆叠 |
| 待执行 | UI-004 | 30 秒准备—战斗循环与运行时战斗快照 | 建立最小阶段状态机，并将当前 PlayerState 与固定真实对手转换成已有 Battle Core/Presentation 可消费的运行时输入 | UI-003；战斗 TASK-005/006 接口仍可用 | 阶段控制器、玩家到 BattleInput 适配器、BattleDemoCoordinator 运行时重载、场景接线、状态/日志测试 | 不等待真实 30 秒的阶段推进测试；Overflow/自动部署/空阵型胜负/快照隔离断言；场景 PlayMode 完整一轮 | 同最高 Cost 的自动部署决胜尚未确认，固定数据避免；双方均无部署单位仍属后续规则 | 不与 BattleDemoCoordinator、BattleInput 适配或 SampleScene 写入任务并行 | 自动准备 30 秒后进入战斗，播放完成后回准备；Overflow 消失；必要时最贵可支付单位在 `(5,2)` 自动部署；战斗死亡不污染玩家状态 |
| 已实现，验收证据部分完成 | UI-005 | 战斗 HUD、单位信息面板与 UI 阶段集成验收 | 完成已确认的 HUD/信息面板表现，并对自动加载→准备→部署→战斗→返回准备的闭环做自动截图判断、最终视觉与运行验收 | UI-004；TASK-007 状态条接口可用或明确记录未验证 | 正式 HUD、状态栏、资源区、设置按钮、单位信息面板、选择路由、必要派生图标、自动截图/manifest/视觉报告入口、场景/测试/ARCHITECTURE/TEST_PLAN | HUD PlayMode 5/5、完整阶段循环 PlayMode 1/1、Windows Player 构建和 6 张截图已通过；manifest 字段不完整，逐图视觉报告及 Player 手工闭环未完成 | 赤金数值、玩家生命、页签内容/图标映射、设置菜单功能未确认；完整 manifest、逐图视觉审查、Player GUI 闭环和稳定工作树的最终 diff 审查仍待完成 | 最终串行集成任务 | 正式 HUD 与关键状态可重复运行；最终完成前须补齐 manifest/逐图报告并完成 Player 人工闭环。详见 `docs/UI-005-REPORT.md` |

## 6. 依赖关系

```text
战斗 TASK-006（已完成闭环）
          │
          ├── TASK-007 实际状态审计（只作为 DefaultUnit/世界表现冲突闸门）
          │
          ▼
UI-001 玩家状态与真实 UI 数据
          ▼
UI-002 自动启动与待部署区 UI
          ▼
UI-003 部署/撤退纵向闭环
          ▼
UI-004 准备—战斗状态机与运行时输入
          ▼
UI-005 HUD/信息面板与集成验收
```

任务原则上串行。Sprite 查找、参考图测量和只读代码调查可以提前，但不得并行修改同一场景、Prefab、图集 `.meta`、玩家状态、BattleDemo 或其他高冲突资源。

## 7. 权威边界

### 7.1 玩家状态层

负责：

- 玩家当前可用 Cost；
- 玩家拥有的单位实例及其区域、坐标、精英化和 Buff；
- 待部署严格堆叠及其稳定排序投影；
- 部署、撤退、Overflow 清理和自动部署的原子命令；
- 变化通知和只读快照。

不负责：

- RectTransform、Sprite、字体、拖拽动画或世界 GameObject；
- 战斗中的当前 HP、伤害和死亡；
- Unity 物理或渲染帧生命周期。

### 7.2 UI 与准备阶段表现层

负责：

- 映射只读玩家快照；
- 发起玩家状态命令并显示成功/失败；
- 创建和销毁待部署槽、准备阶段部署单位视图和选择效果；
- 订阅、取消订阅和重建表现。

不得直接修改玩家集合内部数组、伪造 Cost、依赖 GameObject 是否存在判断权威占位，或把拖拽临时对象当作已部署单位。

### 7.3 阶段与战斗接入层

负责：

- 准备阶段时钟；
- 阶段转换顺序；
- Overflow 清理和必要的自动部署调用；
- 从 PlayerState 封存 BattleInput；
- 驱动已有战斗计算/播放并在 Completed 后返回准备阶段。

不得把战斗死亡写回 PlayerState，也不得让 UI 或演示层修改 Battle Core 结果。

## 8. 当前未确认但不阻塞任务编写的内容

- `gopro` 和 `arcslma` 的初始精英化等级：UI-001 开始写真实 JSON 前必须询问；不得用稀有度推断。
- 同 Cost、同 `type ID`、不同精英化/Buff 的严格堆叠槽排序：固定 UI 测试数据避免该组合。
- 多个单位具有相同最高可支付 Cost 时的自动部署决胜：固定阶段测试数据避免该组合。
- 赤金初始值和变化：UI-005 只建立表现插槽和未接入状态，不增加玩家货币规则。
- 玩家生命初始值和扣除：UI-005 只建立表现插槽和未接入状态。
- 设置菜单、技能/阵营/种族页签内容、基础属性图标映射：只搭建已确认外壳，不自行照搬参考作品。
- 字体、部分边距、阴影贴图点击区域和非 16:9 安全区：以人工拟合结果记录，不伪装为规则已确认。

## 9. 通用完成要求

每个任务的实现 Agent 最后必须报告：

1. 修改文件和序列化资源；
2. 实现内容与数据流；
3. 实际执行的命令和 Unity 操作；
4. Editor 编译、EditMode、PlayMode、构建和人工检查的真实结果；
5. 测试数量、失败项、日志/XML 路径；
6. 未验证项及原因；
7. 所作假设；
8. 遗留风险；
9. 最终工作区和 diff 审查；
10. 下一任务所需输入。

任何没有实际运行的验证必须标记为“未验证”。0 个测试不得记为通过。视觉截图只能证明表现，不替代玩家状态、Cost、区域或战斗结果的结构化断言。

UI-005 的自动截图流程必须同时产出有效 PNG、布局/状态 manifest 和 Agent 实际视觉判断；只生成文件而没有打开图片审查，不算完成。若现有本地图片能力不足，Agent 可在符合 `AGENTS.md` 安全流程的前提下自行创建或安装仅用于本地截图读取/比较的 Skill；不得借此安装 Unity Package、MCP、后台服务或使用外部上传服务。

## 10. UI-004 交付状态（2026-07-22）

UI-004 已完成最小 `Preparation(30 秒) → Battle → Preparation` 循环、PlayerState→BattleInput 快照、Overflow 清理、唯一最高可支付自动部署、运行时 Demo 入口和自动化阶段验证。未扩展商店、赤金、玩家生命、完整结算或多人同步；UI-005 可读取 `PreparationBattleLoopController.Phase`、`RemainingPreparationSeconds`、`LastError`、`ActiveSeal` 和 `LastBattleSummary`，以及既有 Coordinator 的 winner/digest/事件统计。
