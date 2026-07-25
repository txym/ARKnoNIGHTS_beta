# 战斗表现 Track 与双战斗观察设计

日期：2026-07-25  
状态：项目负责人已于 2026-07-25 确认

## 1. 目标

在不改变权威战斗计算的前提下，把已经完成的 `BattleRunResult` 编译为可按任意演示 Tick 采样的只读表现 Track，使四玩家本地 Demo 能够：

- 同时预先计算 `P1-P2` 与 `P3-P4` 两场独立战斗；
- 使用同一个演示时钟推进全部战斗；
- 点击玩家头像时切换到其所属战斗及 Home/Away 视角；
- 在当前演示 Tick 直接恢复单位位置、动作类型、生命、护盾和死亡状态；
- 在所有战斗 Track 都结束后返回准备阶段；
- 压缩权威结果中逐 Tick 输出的大量直线 Move 事件。

本设计只定义表现 Track、双战斗观察和与现有演示层的迁移边界，不实现新的技能、召唤、Buff、护盾或战斗数值规则。

## 2. 当前基线

当前 `BattleRunner` 每个发生移动的逻辑 Tick 都产生一条 `Move` 事件。`BattleEventPlaybackController` 按 `displayTicks` 前向消费单场事件，支持暂停、变速、重播和 Home/Away 投影，但不支持任意时间定位，也不能同时管理多场战斗。

当前表现资源已经使用单位数据中的：

- `unitSkeletonType`：选择 `UnitSkelType1` 或 `UnitSkelType2`；
- `moveAnimation`；
- `attackAnimation`；
- `hitAnimation`；
- `deathAnimation`。

新 Track 继续使用 `unitSkeletonType` 和动画名称映射，但不再播放 Hit 动作。

## 3. 架构选择

采用“预编译单位 Track”：

```text
BattleInput
    ↓ BattleRunner，只运行一次
BattleRunResult + 完整原始事件
    ↓ BattlePresentationTrackCompiler
BattlePresentationTrack
    ├── UnitPresentationTrack
    ├── UnitPresentationTrack
    └── ...
    ↓ 在 GlobalPresentationTick 采样
UnitPresentationSample
    ↓ Home/Away 投影与场景绑定
GameObject + Spine + 世界空间血条
```

不采用：

- 切换观察目标时重新运行 Core；
- 切换时反复扫描全部原始事件；
- 为每场战斗长期保留一整套隐藏 GameObject 和 Spine 播放器；
- 让表现层修改权威位置、生命、死亡或胜负。

## 4. Track 数据模型

### 4.1 BattlePresentationTrack

每场战斗产生一份只读 Track，至少包含：

- Battle ID；
- Home/Away 玩家 ID；
- 战斗结束 Tick；
- 胜方和停止原因；
- 按稳定顺序保存的单位 Track；
- 原始输入、事件和结果摘要；
- Track 摘要；
- 原始 Move 数、位置关键帧数、压缩比例和最大实测位置误差。

### 4.2 UnitPresentationTrack

每个单位 Track 至少包含：

- Unit ID；
- Type ID；
- 阵营；
- 精英化等级；
- Spawn Tick；
- 可选 Death Tick；
- 实例级最大生命；
- Position Segment；
- Facing Key；
- Action Segment；
- CurrentHP Key；
- CurrentShield Key。

Track 不保存 GameObject、MonoBehaviour、Spine 对象、Unity Physics 状态或玩家的可变 `PlayerState`。

### 4.3 UnitPresentationSample

在任意演示 Tick 采样单位 Track，得到：

- 逻辑位置；
- 朝向；
- `Idle / Move / Attack / Death` 动作类型；
- MaxHP；
- CurrentHP；
- CurrentShield；
- 是否已经 Spawn；
- 是否存活；
- 是否应显示。

Home/Away 坐标转换在逻辑 Track 采样之后执行，因此两个观察视角共用同一份 Track。

## 5. 动作规则

动作优先级固定为：

```text
Death > Attack > Move > Idle
```

- Attack 区间从 Attack 事件 Tick 开始，长度使用事件中的 `EffectiveAnimationTicks`；
- Move 由当前 Tick 是否位于有效 Position Segment 决定；
- Death 从 Death Tick 起覆盖其他动作；
- Damage 只更新 CurrentHP，不产生 Hit 动作，也不打断 Attack；
- 当前阶段 CurrentShield 固定为 `0`，但保留实例级 Track 通道；
- 切换观察目标时只恢复动作类型，Spine 动画从该动作开头播放，不恢复动画内部进度；
- 同一动作区间内反复切换可能让该动作再次从头播放，但不得改变伤害、死亡、胜负或阶段时钟。

`PlayHit()` 可以暂时保留为兼容接口，但新 Track 播放路径不得调用它。

## 6. 位置 Track 与压缩

### 6.1 权威数据边界

`BattleRunResult.Events` 保留完整的逐 Tick Move 事件，用于诊断、摘要和权威结果复核。压缩只发生在表现 Track 编译阶段，不修改 Core 事件。

### 6.2 位置线段

连续 Move 事件编译为按时间参数化的线性 Position Segment：

- 第一条 Move 的起点为 `(firstMove.Tick - 1, firstMove.FromPosition)`；
- 最后一条 Move 的终点为 `(lastMove.Tick, lastMove.ToPosition)`；
- Segment 内按演示 Tick 线性插值；
- 不得跨越没有 Move 的 Tick，以免把实际停顿表现成缓慢移动。

### 6.3 视觉无损误差

压缩允许的最大位置误差为：

```text
1 Unity 世界坐标单位 = 1 cm
```

对每个原始 Move Tick：

1. 按该 Tick 在线段中的时间比例计算压缩后位置；
2. 与原始 Move 的 `ToPosition` 比较欧氏距离；
3. 距离不得超过 `1 cm`；
4. 若超过限制，则在最大误差点加入关键帧并继续拆分。

压缩判断必须使用确定性整数或等价有界算法；不得让不同渲染帧率、平台浮点差异或观察视角改变关键帧结果。

### 6.4 精确关键帧

以下 Tick 的位置误差必须为 `0`：

- Spawn；
- Attack 开始；
- BlockStarted；
- BlockEnded；
- Death；
- BattleEnded；
- 移动开始；
- 移动停止；
- 最终单位位置。

TargetChanged 本身不强制切段；只有实际路径变化导致误差超过 `1 cm` 时才增加位置关键帧。

## 7. 动态生成单位

### 7.1 Spawn 驱动

Track 编译器不得只根据初始 `BattleInput` 创建单位 Track。每个合法 Spawn 事件都可以在任意 Tick 创建新的 `UnitPresentationTrack`：

- 初始单位通常在 Tick 0 Spawn；
- 战斗中临时生成的单位可以在任意 Tick Spawn；
- Spawn 前不可见，Spawn 后才参与采样；
- 已生成单位死亡后仍保留在 Track 和最终结果中。

### 7.2 Type ID 与实例级属性

动态生成单位必须引用单位目录中已经存在的 Type ID。Type ID 只用于资源身份和基础类型信息，包括 Prefab、SkeletonData、`unitSkeletonType` 和动画名称。

单位表现 Track 必须读取已封存的实例级属性，不由目录基础值覆盖：

- 初始单位可以从不可变 `BattleInput`、`BattleRunResult` 附带的实例快照或扩展后的 Spawn 载荷取得；
- 不在初始输入中的动态单位必须由 Spawn 载荷或与其绑定的等价不可变实例快照提供。

该实例快照至少需要表达：

- Unit ID、Type ID、阵营和逻辑位置；
- MaxHP、CurrentHP、CurrentShield；
- 实际攻击、防御、法抗和移动速度；
- 实际攻击间隔、攻击动画时长、伤害类型和阻挡容量；
- 当前 Buff 或已经计算后的其他运行时属性。

当前事件契约不足以单独承载动态单位的全部实例级属性。实现动态生成机制前，必须显式扩展 Spawn 事件或增加与 Spawn 绑定的等价不可变实例快照；不得从目录静默补齐可能被 Buff 或技能修改的数值。已有初始单位只要在封存输入中已经具备完整实例快照，不要求为了 Track 重复复制一份属性。

### 7.3 动态 Unit ID

动态生成单位使用规范的负整数字符串作为 Battle Core Unit ID：

```text
-1
-2
-3
...
```

规则为：

- 每场战斗独立从 `-1` 开始递减分配；
- 分配顺序由权威逻辑帧、事件顺序和稳定生成顺序确定，不使用随机数；
- 初始玩家单位禁止使用可解析为负整数的 Unit ID；
- Unit ID 只要求在单场战斗内唯一；
- 完整身份为 `(BattleId, UnitId)`；
- 动态单位不进入跨回合玩家状态，下一场战斗可以重新使用 `-1`；
- 表现 Track 的稳定排列使用 Spawn Tick、Spawn Sequence 和 Unit ID，不依赖负数字符串的字典序；
- 动态单位将来参与同 Tick 权威行动时的决胜顺序属于生成机制设计，不由表现 Track 擅自定义。

以下情况属于结构化错误：

- 事件在单位 Spawn 前引用该单位；
- 同一 Battle 内重复使用已经出现过的 Unit ID；
- Spawn 缺少 Unit ID、已有 Type ID、阵营或位置；
- 动态单位缺少与 Spawn 绑定的实例级属性快照，或初始单位无法从封存输入取得实例级属性；
- Type ID 缺少表现资源映射；
- Track 终态与该动态单位的 Core 终态不一致。

## 8. 双战斗观察与阶段时钟

固定配对为：

```text
MatchAB = Player1(Home) vs Player2(Away)
MatchCD = Player3(Home) vs Player4(Away)
```

观察映射为：

```text
Player1 -> MatchAB / Home
Player2 -> MatchAB / Away
Player3 -> MatchCD / Home
Player4 -> MatchCD / Away
```

两场战斗分别计算一次并编译为独立 Track。战斗阶段维护一个全局演示 Tick 和统一播放速度：

- 所有 Track 随全局演示 Tick 推进；
- 只把当前被观察战斗绑定到场景单位；
- 切换观察目标时，在当前 Tick 采样目标 Track；
- 切换不重新运行 Core，也不把 Track 从 Tick 0 重新推进；
- 已结束战斗在后续 Tick 保持最终状态；
- 当全局演示 Tick 到达所有 Battle Track 的最大结束 Tick 时，只触发一次返回准备阶段。

## 9. 现有代码迁移

- 保留 `BattleRunner`、`BattleRunResult` 和完整事件序列；
- 新增纯 C# Track 编译器、采样器和数据模型；
- 让 Track 采样逐步替代 `BattleEventPlaybackController` 的前向事件消费职责；
- 保留现有单场 `BattleDemoCoordinator` 调试入口，避免一次性删除回归路径；
- 在单场协调器上方新增或拆分双战斗回合协调能力；
- 继续复用 `MappedBattlePresentationViewFactory` 和 `UnitSkelPresentationView`；
- 同一时刻只允许被观察战斗占用场景表现根、Camera 和单位对象；
- 切换、重播、阶段退出和场景销毁必须清理单位对象与事件订阅；
- 演示 HP、死亡和胜负不得写回玩家的局内 `PlayerState`。

## 10. 错误处理

任一战斗发生以下问题时，整轮演示不开始或进入明确错误状态：

- 事件 Tick/Sequence 顺序非法；
- Move 起终点不连续；
- 事件在 Spawn 前引用单位；
- 单场 Battle 内 Unit ID 重复；
- Spawn 缺少必要身份或位置，或者无法从封存输入/动态 Spawn 快照取得实例级属性；
- 缺少已有 Type ID 对应的 Prefab、SkeletonData 或必要动画映射；
- 位置压缩实测误差超过 `1 cm`；
- Track 最终位置、HP、存活或胜方与 `BattleRunResult` 不一致。

诊断至少包含错误码、Battle ID、Unit ID、Tick 和 Sequence。不得静默跳过失败战斗后继续播放其他战斗。

## 11. 验证

### 11.1 EditMode

- 同一结果重复编译产生相同 Track 摘要；
- 每个原始 Move Tick 的压缩位置误差不超过 `1 cm`；
- 精确关键帧误差为 `0`；
- 固定长直线样本的关键帧数明显少于原始 Move 数；
- 不跨越停顿压缩；
- Damage 只更新 HP，不产生 Hit；
- 任意 Tick 的位置、动作、生命、护盾、可见和存活采样正确；
- 初始与动态 Spawn 都能建立 Track；
- 动态 ID 每场从 `-1` 确定性递减，跨 Battle 可以复用；
- Spawn 前引用、重复 ID、未知 Type ID，以及无法从封存输入/动态 Spawn 快照取得实例属性时返回稳定诊断；
- Track 终态与 Core 结果一致；
- Home/Away 只改变采样后的投影。

### 11.2 PlayMode

- 两场战斗只各运行一次 Core；
- 在两场之间反复切换后，位置、血条、敌我关系和动作类型正确；
- 切换时动画从当前动作类型开头恢复；
- Damage 不触发 Hit 动画；
- 当前场景不存在上一场遗留单位、重复 Camera、重复订阅或隐藏播放器；
- 较早结束战斗保持最终状态；
- 全部 Track 完成后只返回准备阶段一次；
- 阶段退出、重播和场景重载后对象与订阅被清理。

## 12. 不属于本设计

- 新增具体召唤、分裂或技能生成逻辑；
- 动态单位的同 Tick 权威行动决胜规则；
- Buff、治疗、护盾或技能的完整计算；
- 网络同步、断线重连或服务端战斗；
- 玩家生命扣除和淘汰；
- 精确恢复 Spine 动画内部播放进度；
- 修改单位 JSON 的已有 Type ID 资源定义。
