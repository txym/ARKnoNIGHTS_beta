# 当前项目架构

> 状态：截至 2026-07-29 的当前实现摘要。
>
> 本文只描述仓库中已经存在的结构和能力，不把尚未接入的完整 LAN 对局写成已实现。游戏规则以 [`SPEC.md`](SPEC.md) 为准；历史演进见 [`history/ARCHITECTURE_EVOLUTION.md`](history/ARCHITECTURE_EVOLUTION.md)。

## 1. 项目基线

- Unity Editor：`2022.3.62f1c1`。
- 渲染：Built-in Render Pipeline。
- UI：uGUI、TextMesh Pro；角色表现使用仓库内的 Spine Unity 3.8 源码与资源。
- 输入：Project Settings 使用旧 Input Manager；第一方交互主要读取 `UnityEngine.Input`。
- 场景：Build Settings 只启用 `Assets/Scenes/SampleScene.unity`。
- 当前产品形态：单场景本地四玩家自走棋 Demo、可用的 LAN 房间流程，以及尚未接入 Lobby/场景的纯领域 Match M1 骨架；正式同步对局尚未接入。

## 2. 模块边界

| 程序集或区域 | 职责 | 关键边界 |
|---|---|---|
| `ARKnoNIGHTS.Battle.Core` | 20 TPS 确定性战斗、单位状态、索敌、移动、阻挡、攻击、能力、结果与结构化事件 | `noEngineReferences=true`；不依赖场景、物理、渲染帧率或 Unity 对象 |
| `ARKnoNIGHTS.Battle.Infrastructure` | Player-safe 目录、fixture 和本地战斗数据的加载与校验 | 引用 Core；负责 Unity Resources/JSON 到不可变输入的转换 |
| `ARKnoNIGHTS.Battle.Presentation` | 主客场投影、Track 编译、事件回放与表现接口 | 只读 Core 结果；不得反向改写战斗计算 |
| `ARKnoNIGHTS.Battle.Demo` | 计算与回放协调器、暂停、倍速、重播和观察视角 | 不拥有场景查找；场景壳在 `Assembly-CSharp` |
| `ARKnoNIGHTS.PlayerState` | 玩家单位、区域、阵型、等级、赤金、商店和本地四玩家快照 | 本地 Demo 的状态源，不是联网权威 Match 状态 |
| `ARKnoNIGHTS.Round` | 准备阶段状态机、阵型封存、四玩家配对和战斗输入生成 | 连接 PlayerState 与 Battle Core，不负责网络同步 |
| `ARKnoNIGHTS.Lobby` | UDP 房间发现、TCP 消息、房间状态、成员准备与开始 | 只同步 Lobby；不引用 PlayerState 或 Battle Core |
| `ARKnoNIGHTS.Match` | 固定四席位的房主权威领域骨架、初始化、单调 revision、命令幂等、连接状态、兼容清单与分权限快照 | `noEngineReferences=true` 且零程序集引用；M1 不含共享牌库、完整经济、回合、Battle 或 Socket |
| `ARKnoNIGHTS.UI`、`ARKnoNIGHTS.Details` | 正式 HUD、商店/准备、玩家列表与单位详情投影 | 消费快照与事件，不保存第二份权威游戏状态 |
| `Assembly-CSharp` 下的 `Runtime/Initial`、`Runtime/Deployment` | `SampleScene` 自动接线、场景生命周期和部署交互 | 是各隔离程序集与序列化场景之间的集成层 |

第一方 EditMode 和 PlayMode 测试分别位于 `Assets/Game/Tests/EditMode` 与 `Assets/Game/Tests/PlayMode`，按 Battle/Lobby/Match 分程序集；Match M1 当前只有独立 EditMode 测试程序集。

## 3. 主要数据流

### 3.1 单位与能力数据

```text
Assets/GameData/Units/EliteVariants/Json
Assets/GameData/Abilities/Json
        ↓ Editor 生成器与校验
Assets/Resources/BattleData/*-catalog-v1.json
        ↓ Player-safe loaders
UnitCatalog / AbilityCatalog / SkillAnimationCatalog
        ↓ 输入封存
BattleRunner
```

- v2 JSON 是人工维护源；`Assets/Resources/BattleData` 下的 v1 目录是运行时消费物。
- `UnitCatalogGenerator`、`AbilityCatalogGenerator` 和 `SkillAnimationCatalogGenerator` 负责生成与交叉校验。
- 运行时不读取项目外数据，也不从表现资源反向推断游戏规则。

### 3.2 本地准备与战斗

```text
local-match-state-v1 + 四份 PlayerState
        ↓ LocalMatchStateLoader
准备阶段（商店、购买、部署、观察、Ready）
        ↓ FourPlayerBattleRoundSealer
两场确定性 BattleInput
        ↓ BattleRunner
不可变 BattleRunResult
        ↓ MultiBattlePresentationCoordinator
共享时钟回放与玩家视角切换
        ↓
返回准备阶段并刷新商店
```

`PreparationBattleLoopController` 当前负责 `Preparation → Battle → Preparation` 的场景副作用。战斗中的单位受伤和死亡不直接写回持久玩家单位；完整玩家扣血、淘汰与正式经济结算仍属于后续 Match 层。

### 3.3 LAN 房间

```text
UDP 发现 → 创建/加入 TCP 房间 → 成员准备 → 房主开始
                                           ↓
                                  解除本地 Demo 门控
```

`LanLobbyController` 自动创建场景级入口。现有开始流程会关闭 Lobby 服务并让每台设备各自进入本地四玩家 Demo；它没有持续的 Match Session、权威命令排序、状态同步或重连恢复。目标方案见 [`LAN-MATCH-DESIGN.md`](LAN-MATCH-DESIGN.md)。

### 3.4 Match M1 纯领域骨架

```text
四席位初始化 + 兼容清单
        ↓ MatchSessionFactory
MatchAuthority → MatchState
        ↓ 单事务/单 revision
PublicMatchSnapshot
OwnerPrivateSnapshot
HostMatchSnapshot
```

`ARKnoNIGHTS.Match` 不读取 Unity、Resources、系统时间或 Socket。M1 只提供固定席位、初始玩家字段、强类型命令、CommandId 幂等、连接状态内部事务、稳定规范摘要和分权限投影；Lobby、PlayerState、Round、Battle 与场景尚未消费该程序集。M2 必须扩展同一 `MatchAuthority` 原子事务和状态不变量，不能另建可变权威状态。

## 4. 场景与启动入口

`SampleScene` 是唯一构建场景。场景中保留战斗 Demo、单位视图和旧调试对象；以下入口在加载后自动补齐运行时组件：

- `LanLobbyController`：LAN 主界面与房间会话；
- `StateDrivenDeploymentController`：状态驱动部署交互；
- `PreparationBattleLoopController`：本地准备/战斗循环；
- `FormalBattleHudController`、`BattleHudSceneCoordinator`：正式 HUD、商店、玩家列表和观察协调；
- `UIManager`：全局 UI 点击路由；
- 显式命令行参数触发的截图或验收 runner。

`BattleDemoController` 仍保留固定真实数据调试能力，但在正式本地回合模式下由 `PreparationBattleLoopController` 提供封存后的输入。

## 5. 当前已实现

- 纯 C#、20 TPS、确定性的 Battle Core；
- 移动、索敌、阻挡、攻击、伤害、死亡、冲门、生命损失与大量 BONDS 能力；
- 主客场坐标投影、Track、动态 Spawn、重播、暂停和倍速；
- PlayerState、准备倒计时、部署/换位/撤退、六槽商店、购买、刷新、冻结、等级与本地观察；
- 固定四玩家本地配对、两场战斗共享时钟演示；
- LAN 房间发现、创建、加入、准备、开始和房间 UI；
- 纯 C# 房主权威 Match M1 领域骨架、兼容清单、命令幂等和分权限快照；
- EditMode/PlayMode 自动测试、Windows x86_64 构建入口和多种截图证据入口。

## 6. 当前未实现或未闭环

- Match M2—M8：共享牌库、完整经济、自动合成、回合/配对/结算、AI、会话协议、重连、战斗流式计算和最终集成；
- M1 的兼容清单与快照尚未接入 Lobby 握手、TCP 协议、场景或 UI；
- 开局后保持连接、断线宽限、重连 token 恢复和 AI 接管；
- 共享卡池的联网权威扣留/返还、完整经济和跨回合结算；
- 正式玩家生命写回、淘汰、最终胜负和完整多人流程；
- 主机迁移、专用服务器与反房主作弊；
- 目标平台发布配置以及全面的分辨率、设备和人工视觉验收。

不能用本地 Demo 的四玩家 fixture 代替正式联网状态，也不能把 Lobby 的开始信号扩展为未经设计的 Match 同步。

## 7. 修改指南

- 改战斗规则：优先进入 `Battle.Core` 并用确定性 EditMode 测试覆盖；Unity 表现只消费事件。
- 改单位或能力：修改 v2 源和生成器，验证目录投影；不要直接把生成目录当人工事实源。
- 改本地回合：保持 `PlayerState → Round seal → BattleInput` 单向流。
- 改联网：遵守 `Lobby → Initial 集成 → 本地门控` 的现有依赖；M2 起按 LAN 设计扩展既有 `ARKnoNIGHTS.Match` 及其唯一权威状态，不另建第二套 Match 领域层。
- 改 UI：读取快照和事件；不要让 View 保存或推断权威规则。
- 改场景、Prefab 或序列化字段：检查 `.meta`、引用和序列化 diff，并执行相应 PlayMode/Player 验证。
