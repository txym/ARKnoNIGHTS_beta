# 文档导航

本目录同时保存当前事实、规则设计、实施任务和历史验收记录。它们的权威级别不同；开始工作时不要按文件更新时间猜测优先级。

## 先读这三份

1. [`SPEC.md`](SPEC.md)：已确认的游戏机制与玩家可见行为。未写明的关键规则不得自行推测。
2. [`ARCHITECTURE.md`](ARCHITECTURE.md)：当前代码实际具备的模块、入口、数据流和边界。
3. [`TEST_PLAN.md`](TEST_PLAN.md)：当前验证方法、完成标准和常用命令。

发生冲突时，先判断冲突属于“目标规则”还是“当前实现”：

- 目标行为以 `SPEC.md` 和用户当前指令为准；
- 当前实现事实以代码、资源、Project Settings 和 `ARCHITECTURE.md` 为准；
- 测试只证明对应提交和证据记录，不自动代表当前工作树；
- 仍有实质冲突时必须停止并确认，不能静默选边。

## 当前主题

### 正式 LAN 对局

- [`LAN-MATCH-DESIGN.md`](LAN-MATCH-DESIGN.md)：持续收敛中的房主权威、四席位、AI、经济、回合、同步和重连设计。
- [`LAN-MATCH-IMPLEMENTATION-PLAN.md`](LAN-MATCH-IMPLEMENTATION-PLAN.md)：M1～M8 实施顺序与依赖。
- `TASK-LAN-MATCH-*.md`、[`TASK-BATTLE-STREAMING.md`](TASK-BATTLE-STREAMING.md)：各里程碑的独立实施提示词。
- [`TASK-BATTLE-BASELINE-CLEANUP.md`](TASK-BATTLE-BASELINE-CLEANUP.md)：M1 全量回归暴露出的既有 Battle 基线漂移修复任务。

现有代码已完成 LAN Match M1—M8：房间开始后提升原 TCP 连接，进入房主权威 Match Session，并接入共享牌库/经济、合成/Overflow、回合配对/结算、自动人机、重连、分块战斗计算和正式 HUD。单进程真实 TCP 回环与 Windows x64 构建已有自动化证据；双机物理 LAN、真实网络中断恢复和多分辨率 HUD 仍待人工验收。

### 战斗、单位与 BONDS

- [`bonds/BONDS_SPEC.md`](bonds/BONDS_SPEC.md)：羁绊分类、单位清单、费用表和能力规则源；其中部署 Cost 与当前数据源的冲突见下方待确认项。
- [`bonds/BONDS_IMPLEMENTATION.md`](bonds/BONDS_IMPLEMENTATION.md)：BONDS 单位能力的生产接线台账。
- [`bonds/UnitAnimation.md`](bonds/UnitAnimation.md)：单位 Spine 动画结构与处理约定。
- [`UNIT-DATA-001.md`](UNIT-DATA-001.md)：单位源 JSON、目录生成和 Player-safe 数据契约。
- [`numerical_architecture/`](numerical_architecture/)：稀有度、单位费用、商店概率、共享卡池和经济设计及分析数据。
- [`decisions/`](decisions/)：已经确认、需要长期保留的架构决策。

### UI 与阶段任务

- [`PHASE1_TASK_TABLE.md`](PHASE1_TASK_TABLE.md) 和 `TASK-001.md`～`TASK-007.md`：第一阶段战斗闭环的任务定义。
- [`UI_TASK_TABLE.md`](UI_TASK_TABLE.md)、`UI-*.md`、`PREP-DEPLOY-001.md`：本地准备、商店、部署、HUD 和单位信息面板的任务定义。
- `*-REPORT.md`、[`LAN-LOBBY-REPORT.md`](LAN-LOBBY-REPORT.md)：对应任务当时的验收报告；它们不是当前状态摘要。

### 参考资料与过程记录

- [`references/`](references/)：UI 参考图、像素规格和批准素材映射。
- [`superpowers/specs/`](superpowers/specs/)：按日期保存的功能设计稿。
- [`superpowers/plans/`](superpowers/plans/)：按日期保存的实施计划。
- [`history/ARCHITECTURE_EVOLUTION.md`](history/ARCHITECTURE_EVOLUTION.md)：过去按任务追加的架构演进记录。
- [`history/TEST_RECORDS.md`](history/TEST_RECORDS.md)：过去的测试命令、结果、失败和证据位置。

## 文档维护约定

- 机制、数值、胜负、回合或多人同步规则变化：更新 `SPEC.md`；若仍处于独立设计阶段，同时维护对应设计文档。
- 模块边界、入口、数据流或已实现能力变化：更新 `ARCHITECTURE.md`。
- 验证命令、分层或完成标准变化：更新 `TEST_PLAN.md`。
- 一次任务的详细证据写入对应任务或报告；不要继续把流水记录追加到三份核心入口。
- 重要且长期有效的技术取舍写入 `decisions/`。
- 新计划使用可辨识的日期和主题文件名；不要重新创建含义不明的顶层 `PLAN.md`。
- 过程文档不覆盖权威规范。设计完成、实现完成和验证通过是三种不同状态，必须分别标注。

## 已知待确认冲突

以下问题会影响玩家可见行为或正式数据迁移，本次整理不替项目负责人定案：

1. 部署 Cost 有三套现状：`bonds/BONDS_SPEC.md` 保存逐单位费用表，v2 单位源的精英 0 `deploymentCost` 当前统一为 `2`，冻结的 `unit-catalog-v1` 又只暴露 `1000/5503/5504 = 2/12/4`。需要确认 BONDS 表是正式迁移目标、当前权威值还是分析结果。
2. `UI_TASK_TABLE.md` 保留早期五槽任务描述，但当前 `SPEC.md`、`LocalMatchState.ShopSlotCount` 和 HUD 均为六槽。该任务表已标为历史规划，不能再作为当前槽位数依据。
3. 正式 LAN Match M1～M8 已形成统一集成链；后续修改遇到与 `SPEC.md` 的冲突仍须先回写确认，不能另建第二套 Match 权威状态。
