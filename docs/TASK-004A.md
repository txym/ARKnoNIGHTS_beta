# TASK-004A：真实单位目录、临时本地对战输入与历史回测

## 角色

你是本 Unity 项目的战斗数据接入、兼容迁移与回归验证 Agent。

你的职责是在不推翻 TASK-002～004 已有 Core、事件和表现边界的前提下，规定并实现一版“临时但使用真实单位数据”的本地单场战斗输入：对战文件只描述参战玩家、主客场和单位实例，单位数值与表现资源由现有单位 JSON 目录解析并转换。完成后，TASK-005 必须能用真实 `gopro`/`arcslma` 数据和 Spine 资源接入场景，而不是继续拿 `home-striker`/`away-guard` 合成 fixture 猜测资源映射。

本任务也是 TASK-001～004 的真实数据回归闸门。允许修复由真实数据暴露出来、且属于这些任务既有契约范围的兼容问题；不得借机新增战斗机制、重写整个 Core 或提前修改 Demo 场景。

## 开始前必须阅读

- `AGENTS.md`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`
- `docs/TASK-001.md` 至 `docs/TASK-004.md`
- `docs/decisions/`（若存在）
- TASK-001～004 的完成报告、测试 XML、日志、遗留风险和实际修改文件
- `ProjectSettings/ProjectVersion.txt`
- `ProjectSettings/EditorBuildSettings.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`（若存在）
- `Assets/GameData/Units/UnitJson.cs`
- `Assets/GameData/Units/Json/1000_gopro.json`
- `Assets/GameData/Units/Json/5503_arcslma.json`
- `Assets/GameData/Units/Json/5504_arcslmi.json`
- `Assets/Game/Runtime/Initial/UnitFactory.cs`
- `Assets/Game/Runtime/Data/Unit/UnitTemplate.cs`
- `Assets/Game/Runtime/Data/Unit/UnitIdentity.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelType1.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelType2.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs`
- `Assets/Game/Runtime/Initial/MappedBattlePresentationViewFactory.cs`
- `Assets/Game/Battle/Core/`、`Assets/Game/Battle/Infrastructure/` 和 `Assets/Game/Battle/Presentation/` 下的实际代码
- `Assets/Game/Tests/` 下 TASK-002～004 的实际测试
- `Assets/Resources/BattleFixtures/task002-minimal-v1.json`
- `Assets/Resources/BattleFixtures/task003-minimal-v1.json`
- `Assets/Resources/Prefabs/DefaultUnit.prefab`
- `Assets/Resources/Characters/gopro/` 与 `Assets/Resources/Characters/arcslma/` 下的实际 Spine 资源

开始前执行 `git status --short`，识别并保护所有用户修改和 TASK-001～004 产物。确认没有其他 Unity Editor 或 batchmode 进程打开同一项目。先建立当前基线：实际程序集、加载器、输入 schema、合成 fixture、测试数、表现工厂映射方式以及真实资源缺口；不得只按旧提示词推断。

## 任务背景

当前仓库已经存在以下事实：

- TASK-002 的 `battle-fixture-v1` 同时包含单位类型定义和 Home/Away 玩家快照；
- TASK-003 的 1v1 fixture 使用 `home-striker` 与 `away-guard`，其数值是算法测试用合成数据；
- `Assets/GameData/Units/Json/1000_gopro.json`、`5503_arcslma.json` 与 `5504_arcslmi.json` 包含当前真实单位 ID、HP、攻击、防御、法抗、移动速度、攻击间隔和 Spine 资源标识；
- 真实单位 JSON 目前缺少战斗计算需要的独立 `damageType`、`blockCapacity` 和 `attackAnimationDuration` 字段；
- 旧 `UnitFactory` 通过 `Application.dataPath/GameData/Units/Json` 枚举源文件，不是可靠的 Player 数据入口；
- `MappedBattlePresentationViewFactory` 目前只接受 Inspector 手工配置的 Core type ID 到资源绑定；合成 type ID 没有真实资源映射；
- TASK-004 已验证事件回放边界和假视图，但真实 `DefaultUnit`、`gopro`/`arcslma` SkeletonDataAsset、动画名称和旧 `UnitSkelBase` 初始化链仍未形成证据。

因此，合成 fixture 应继续承担纯算法边界测试，但不能再作为 TASK-005 的 Demo 输入。本任务需要建立一条 Player-safe、可重复、可诊断的真实数据适配链，并用它回测已有计算与表现。

## 本任务目标

1. 明确定义并文档化临时 schema `local-battle-v1`：只包含 battle ID、maxTicks、Home/Away 玩家信息及各自单位实例，不嵌入单位属性或 Spine 资源数据。
2. 明确定义并实现版本化真实单位目录 `unit-catalog-v1`：由现有 `Assets/GameData/Units/Json/*.json` 作为源数据生成或转换，提供 Core 计算字段和 Unity 表现资源字段。
3. 保持 Core `typeId` 的现有字符串契约；真实单位使用十进制 ID 字符串，例如 `"1000"`、`"5503"`，不得新增另一套无必要的 ID 类型。
4. 为现有单位 JSON 补齐当前战斗必需且已确认的字段：`damageType`、`blockCapacity`、`attackAnimationDuration`。数据值必须来自已确认规则或真实 Spine 资源，不得随意填假值。
5. 提供确定性的秒/米单位转换和结构化校验，将真实单位目录与 `local-battle-v1` 玩家快照合成为现有不可变 `BattleInput`。
6. 提供至少一份 Player-safe 的真实 1v1 本地对战输入，双方实例引用 `gopro`/`arcslma` 的真实 type ID，并能正常形成 Home/Away 战场映射。
7. 让 Unity 表现桥接能够根据真实单位目录自动解析 prefab、SkeletonDataAsset、legacy type ID、`unitskeltype` 和必要动画配置；Inspector 手工绑定只可保留为显式覆盖，不再是固定真实输入的必需步骤。
8. 使用真实输入完整运行 Battle Core，产生结构化事件、最终状态和 winner/reason；同一输入至少运行 10 次，摘要完全一致。
9. 用真实 `DefaultUnit` 和 Spine 资源执行最小 PlayMode 表现回归，确认真实 Spawn、Move、Attack 及可用的 Damage/Death 表现不会因映射或初始化失败而中断。
10. 根据真实数据暴露的问题，对 TASK-001～004 产物做最小兼容修正并重跑对应验证；保留原合成 fixture 和测试作为算法回归。
11. 更新 SPEC、ARCHITECTURE 和 TEST_PLAN 中与输入格式、真实单位目录、Player-safe 加载、表现映射及实际验证结果直接相关的内容。
12. 为 TASK-005 提供明确的输入资源路径、加载 API、真实 unit type 映射、播放入口和剩余人工检查清单。

## 临时输入格式约定

### A. `local-battle-v1`：某场对战的玩家快照

逻辑结构固定如下，具体 DTO/类名可服从现有风格：

```json
{
  "schemaVersion": "local-battle-v1",
  "battleId": "task004a-real-1v1",
  "maxTicks": 12000,
  "players": [
    {
      "playerId": "local-home",
      "side": "Home",
      "units": [
        {
          "unitId": "home-1000-001",
          "typeId": "1000",
          "zone": "Deployed",
          "formationX": 4,
          "formationY": 4,
          "buffs": []
        }
      ]
    },
    {
      "playerId": "local-away",
      "side": "Away",
      "units": [
        {
          "unitId": "away-5503-001",
          "typeId": "5503",
          "zone": "Deployed",
          "formationX": 6,
          "formationY": 4,
          "buffs": []
        }
      ]
    }
  ]
}
```

要求：

- `players` 同时承载“哪两个玩家对战”和“谁是 Home/Away”；第一阶段不另建房间、网络或四人配对对象。
- `units` 是玩家单位实例快照；`unitId` 在本场唯一，`typeId` 引用真实单位目录。
- `zone`、阵型坐标和 Buff 原始占位沿用 TASK-002 已有语义；当前真实闭环只使用可控的 Deployed 1v1 数据。
- 本文件不得重复 HP、攻击、移动速度、攻击间隔、Spine 路径等类型级字段。
- 数组顺序不得成为权威行动顺序；输入验证与 Core 仍按稳定 ID/既有规则建立确定顺序。

### B. `unit-catalog-v1`：真实单位类型目录

源事实仍是 `Assets/GameData/Units/Json/*.json`。运行时使用的 Player-safe 目录必须通过确定性转换生成或构建，不允许靠手工复制两份长期漂移的数据。

目录中每个条目至少提供：

- Core：十进制字符串 `typeId`、HP、攻击、防御、法抗、移动速度厘米/秒、攻击间隔 Tick、攻击动画时长 Tick、伤害类型、攻击方式、阻挡容量、`isSyntheticFixtureData=false`；
- Presentation：legacy 数字 ID、`uintName`、`SkeletonDataAsset` 的 Resources 路径、`unitskeltype`，以及经真实资源核对后确有必要的动画配置；
- 来源/版本：schema 版本和足够定位源 JSON 的稳定信息，不写机器绝对路径。

Player-safe 载体优先使用项目已有 `Resources.Load<TextAsset>` 能力下的合并 JSON/TextAsset。若采用 Editor 生成器：

- 生成过程必须可重复、稳定排序并显式失败；
- 生成结果应能通过测试与源 JSON 对比，防止目录漂移；
- 不得在 Player 中读取 `Application.dataPath/GameData/...` 源目录；
- 不得引入 Addressables、第三方序列化库、数据库或联网加载。

### C. 确定性字段转换

- `id` 转为十进制字符串 Core `typeId`；不得使用本地化格式。
- `moveSpeed` 的源单位是米/秒；乘以 100 转为整数厘米/秒。若不能无损转为当前定点精度，停止并报告，不得静默取整。
- `attackInterval` 的源单位是秒；按 20 TPS 转为整数 Tick。若不是整数 Tick，停止并询问，不得自行选择四舍五入、向上或向下。
- `attackAnimationDuration` 的源单位是秒；按 20 TPS 转为整数 Tick。实际值应通过对应 SkeletonDataAsset 中正式攻击动画核对后写回源 JSON。若动画不存在、命名冲突或时长不能无损转为整数 Tick，停止并询问。
- 当前 `gopro`/`arcslma` 的 `damageType` 明确写为 `Physical`；不得从 `attackMethod` 推断伤害类型。
- 当前阻挡容量明确写为 1；`attackMethod` 只转换为现有近战/远程枚举，不新增远程行为。
- 不使用浮点数推进权威战斗；浮点只允许停留在源 JSON 解析/严格转换边界和 Unity 表现层。

## 已确认规则

- 第一阶段战斗源数据由对战双方完整玩家单位快照、Home/Away 信息和单位类型定义共同构成。
- 战场使用一基 `9×8` 坐标；双方本地阵型使用一基 `9×4` 坐标；Away 阵型进入 Home 权威战场时做 180 度变换。
- `1 格 = 1 米 = 100 Unity 世界坐标单位`。
- 权威战斗为 20 TPS；不依赖 Unity Physics、`Time.deltaTime`、渲染帧率或 Spine 状态。
- `moveSpeed` 单位为米/秒；`attackInterval` 与 `attackAnimationDuration` 的源单位为秒，并转换成整数 Tick 供 Core 使用。
- 伤害在有效动画结束 Tick 到达；有效时长沿用已实现规则 `min(原始攻击动画 Tick, 攻击间隔 Tick)`。攻击者或目标在到达前死亡时删除该 pending attack。
- 当前真实单位按物理伤害处理，但伤害类型必须是独立字段；阻挡容量为 1。
- 演示层可根据 `attackAnimationDuration > attackInterval` 加速动画，但不得改变 Core 出伤 Tick、事件或结果。
- Core type ID、玩家 ID 和单位实例 ID 是不同概念，不得混用。
- 合成 fixture 可以继续用于算法边界测试，但不能作为真实 Spine Demo 的单位目录或资源映射依据。

## 不属于本任务的内容

- 不修改 `SampleScene.unity`、其他场景、Prefab、ScriptableObject、Package 或 ProjectSettings；
- 不实现 TASK-005 的按钮、Demo 状态机、暂停、重播、调速和场景 UI；
- 不新增移动、索敌、阻挡、攻击、伤害、胜负、Buff、技能、治疗、护盾或远程规则；
- 不实现准备、商店、手动部署、完整回合、玩家扣血、四人房间或联网；
- 不删除 `battle-fixture-v1`、`task002-minimal-v1.json`、`task003-minimal-v1.json` 或其现有测试；
- 不把真实单位数值直接硬编码在测试、工厂、MonoBehaviour 或 Core 类中；
- 不让 Core 引用 UnityEngine、Spine、Resources、文件路径、UnitFactory、UnitTemplate 或表现目录；
- 不通过自动读取动画状态来推进权威战斗；资源检查只用于生成/验证输入数据与表现播放；
- 不把 `BlockRadius` 0.1 当成当前全局 0.25 米战斗阻挡规则；两者语义冲突时以 SPEC 的当前战斗规则为准并在文档中说明。

## 预计影响文件或目录

候选范围：

- `Assets/GameData/Units/UnitJson.cs`
- `Assets/GameData/Units/Json/1000_gopro.json`
- `Assets/GameData/Units/Json/5503_arcslma.json`
- `Assets/GameData/Units/Json/5504_arcslmi.json`
- `Assets/Game/Battle/Infrastructure/` 下的真实单位目录、对战快照加载与适配代码
- `Assets/Game/Battle/Core/Input/`：只允许必要且兼容的验证/模型修正，不得重写已有输入
- `Assets/Game/Battle/Presentation/`：只允许真实资源元数据所需的只读契约修正
- `Assets/Game/Runtime/Initial/MappedBattlePresentationViewFactory.cs` 或经过调查后新增的等价数据驱动工厂
- `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`
- `Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs`
- `Assets/Game/Editor/Battle/`（若采用 Editor 目录生成器）
- `Assets/Resources/BattleData/` 或等价 Player-safe 目录：版本化真实单位目录和 `local-battle-v1` 输入
- `Assets/Game/Tests/EditMode/Battle/`
- `Assets/Game/Tests/PlayMode/Battle/`
- 新文件对应的 `.meta`
- `docs/SPEC.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/PHASE1_TASK_TABLE.md`（只更新实际状态与证据）

若实际结构不同，先调查并在实施计划中列出准确候选文件。不得修改场景、Prefab、Spine 资源本体或 Package。不得为了省事把源 JSON 移入 Resources，除非先证明不会破坏旧路径和引用并得到项目负责人确认。

## 实施要求

### A. 建立真实数据差距清单

1. 列出 Core `UnitDefinition`、现有 `UnitJson`、`UnitTemplate`、表现 Binding 和 SkeletonDataAsset 之间逐字段映射。
2. 明确哪些字段来自源 JSON，哪些由单位 ID/路径确定性派生，哪些当前缺失。
3. 通过 Unity API 检查 `gopro`/`arcslma` 实际 SkeletonDataAsset、攻击/移动/受击/死亡动画名称和攻击动画时长；不得从文件名或旧代码注释猜测。
4. 将已确认的 `damageType`、`blockCapacity` 和经资源核对的 `attackAnimationDuration` 写入源 JSON 与对应 DTO。若无法取得真实攻击动画时长，立即停止并报告。

### B. 实现版本化单位目录

5. 保留源 JSON 为真实单位事实来源；实现稳定排序、显式校验的目录转换。
6. 生成/加载 `unit-catalog-v1` 时，对重复 ID、缺字段、非法枚举、负数/零数值、资源路径空值和非整数 Tick 转换返回结构化错误。
7. Player-safe 目录不得依赖机器绝对路径或 `Application.dataPath/GameData/...`。
8. 转换输出必须稳定；同一源数据连续生成两次应得到字节一致或规范化摘要一致的结果。
9. 表现元数据与 Core 数值来自同一条目录记录，避免 type ID 到资源再次手工维护两张表。

### C. 实现 `local-battle-v1` 适配

10. 新 schema 只保存玩家/对战/实例字段；按 type ID 从真实目录连接单位定义，再调用现有 `BattleInput` 验证和主客场映射。
11. 不静默改变旧 `battle-fixture-v1` 语义。可以保留旧加载器并新增真实输入加载器，或在清晰版本分发层支持两种 schema；不得把旧合成文件悄悄当成真实目录。
12. 新加载结果应同时提供 Core `BattleInput` 与表现层只读单位目录/解析器，或提供语义等价的单向接口；Core 不得接收表现字段。
13. 所有解析/连接失败使用结构化诊断，至少包含 schema、battle ID、player/unit/type ID 和错误代码。
14. 新建一份可结束的真实 1v1 输入。坐标、maxTicks 和单位组合应避免当前未确认的多单位阻挡竞争、同时全灭、Buff 和异常属性。

### D. 接通真实表现资源

15. 数据驱动工厂按真实 type ID 解析 `DefaultUnit`、SkeletonDataAsset、legacy ID 和 `unitskeltype`。保留现有 Inspector Binding 时，应作为显式覆盖或测试注入，不得要求 TASK-005 手填固定单位映射。
16. 检查 `DefaultUnit` 上现有 `SkeletonAnimation`、`UnitIdentity` 和 `UnitSkelBase` 初始化顺序。真实战斗视图不应要求先运行旧 `UnitFactory.SpawnAll` 才能取得模板数据。
17. 若 `UnitSkelBase.Start/LoadSelfData` 会因旧模板缓存未初始化而失败，采用最小兼容注入/配置或明确的只读目录适配；保留旧原型路径，不把权威战斗状态交回旧组件。
18. 依据实际资源为 `UnitSkelPresentationView` 选择可用动画。缺少 Hit/Death 时可以按 TASK-004 既有降级策略记录；Move/Attack 若无可用动画或名称不确定，应停止并报告。
19. 动画播放速度可以按事件携带的持续 Tick 与真实动画时长适配；不得修改事件 Tick、Damage 到达时间或 winner。

### E. 回测并最小修正 TASK-001～004

20. TASK-001 回测：Editor 编译；确认 `UnitDeployment.ConvertCoordinate` 修复未回归。已知 `UITest.targetSprite` Standalone 债务不属于本任务，除非新增改动产生了新的非 Editor 编译问题。
21. TASK-002 回测：旧合成 fixture 的 10 项基础验证继续通过；新增真实目录、`local-battle-v1`、连接、非法字段和 Player-safe 资源加载测试。
22. TASK-003 回测：旧完整战斗测试继续通过；真实输入至少 10 次得到完全一致的 input/event/final-state 摘要和 winner/reason。
23. TASK-004 回测：既有假视图/双视角/只读回放测试继续通过；新增至少一项使用真实 `DefaultUnit` 与 `gopro`/`arcslma` SkeletonDataAsset 的 PlayMode 测试，覆盖创建、初始化、Move、Attack、结束清理和结构化诊断。
24. 只有真实数据揭示了通用契约缺陷时，才修改 TASK-002～004 代码。每处修正必须说明根因、为何不是新机制、兼容影响和新增回归测试。
25. 不修改历史完成报告来伪装旧任务当时已经验证真实数据；在当前文档中新增“TASK-004A 回测”证据。

### F. 文档与交接

26. SPEC 只更新输入来源、schema 和新补齐字段等已落实行为；ARCHITECTURE 记录真实加载/转换/表现数据流；TEST_PLAN 记录实际命令、测试数和仍未验证项。
27. 在完成报告中给出 TASK-005 必须使用的真实输入资源名、加载方法、类型目录接口、工厂接口、动画降级表和人工检查步骤。

## 兼容与迁移要求

- 保持现有 Core `BattleInput`、事件和 `BattleRunResult` 的语义；能通过适配解决时不做破坏性 schema/程序集迁移。
- 保留旧 `battle-fixture-v1` 和合成数据用于纯算法回归；不要让它们驱动真实场景 Demo。
- 保留 `UnitFactory`、`UnitTemplate`、`UnitIdentity`、`UnitSkelBase`、`UnitSkelType1/2`、`DefaultUnit` 和旧部署/UI 公共入口；不得默认删除重写。
- 真实表现桥接可以复用上述组件，但不得让它们反向写入 Core 结果。
- `MappedBattlePresentationViewFactory` 若被扩展，现有显式 Binding 的序列化兼容必须保留；若新增工厂，说明 TASK-005 应使用哪个入口以及旧工厂去留。
- 不改变 `SampleScene`、Prefab 或 Spine 资源 GUID；不删除或重新生成现有 `.meta`。
- 生成目录必须明确来源和再生方式；不得提交 Library、Temp、Logs、Build 或构建产物。
- 不为未来联网设计服务层、仓库接口、协议对象或同步抽象。

## 验证要求

### 自动验证

至少覆盖：

- 两份源单位 JSON 可解析，必需字段齐全；
- `id → typeId`、米/秒 → 厘米/秒、秒 → Tick、伤害/攻击枚举和阻挡容量转换准确；
- 非整数 Tick、重复 ID、未知类型、缺字段、非法资源路径返回结构化错误；
- 同一源目录连续生成/解析结果一致；
- `local-battle-v1` 不含单位属性，能通过目录连接生成完整 `BattleInput`；
- Home/Away 玩家快照和单位实例 ID/type ID 映射正确；
- 旧 `battle-fixture-v1` 测试全部继续通过；
- 真实 1v1 输入能够运行到 winner/reason，10 次摘要逐项一致；
- Presentation 使用真实目录解析资源，不需要 Inspector 手工绑定；
- 真实视图的 type ID、legacy ID、SkeletonDataAsset 和 `unitskeltype` 正确；
- 回放和视角投影不改变源事件、最终状态或 winner；
- 销毁后无残留视图/订阅。

结果 XML 必须可解析、测试数大于 0、失败为 0，才能写“通过”。旧测试与新增测试数量分开报告。

### Unity 编译或运行验证

- 执行 Editor 编译；
- 运行全部相关 EditMode 测试；
- 运行包含真实资源映射的 PlayMode 测试；
- 在 PlayMode 中实际加载 Player-safe 真实目录和 `local-battle-v1`；
- 实际创建至少一个 `gopro` 和一个 `arcslma` 视图，检查 Skeleton 初始化、Move、Attack 和结束清理日志；
- 检查 Console 中不存在 `resource.mapping.missing`、`resource.skeletonData.missing`、模板缓存空引用或本任务新增的未处理异常；
- Standalone 完整构建仍留给 TASK-006。若可执行非 Editor 脚本编译或资源加载测试，应记录结果；不得因已知 `UITest` 债务扩展范围。

### 人工检查

1. 检查真实目录摘要与两份源 JSON 的 ID、数值和资源路径；
2. 查看 `gopro`/`arcslma` 实际 Move/Attack 动画是否可播放；
3. 对照资源中的真实 Attack 时长与 JSON/转换 Tick；
4. 观察至少一次真实 1v1 的 Spawn、移动、攻击、受击/降级和死亡/清理；
5. 核对播放结束 winner 与 Core 结果；
6. 检查 Home/Away 视角只改变坐标和方向；
7. 重复加载/播放，检查对象和日志无累积。

本任务不修改正式场景；可使用 PlayMode 测试对象或临时运行时对象完成验证，不得保存临时场景改动。

### 无法执行时

- 动画视觉、资源持续时间或真实视图创建未实际观察时，必须写“未验证”；
- 无法可靠读取 Attack 动画真实时长时，必须停止，不得填临时猜测值；
- 测试 XML 未生成、测试数为 0、Unity 超时或日志不完整时，不得记为通过；
- 给出精确缺口、已运行命令、退出码、日志路径和下一步人工操作。

## 验收标准

- `local-battle-v1` 与 `unit-catalog-v1` 的字段、版本、职责和示例已在实现与文档中一致定义；
- 固定对战文件只描述玩家、Home/Away 和单位实例，不重复单位类型数值/资源；
- 真实目录由现有单位 JSON 确定性产生，并能在 Player-safe 路径加载；
- `gopro`/`arcslma` 的 Core 数值、攻击动画时长、伤害类型、阻挡容量和表现资源映射均有真实来源；
- 至少一份真实 1v1 输入能生成不可变 `BattleInput` 并运行到明确 winner/reason；
- 同一真实输入 10 次的 input/event/final-state 摘要和 winner/reason 完全一致；
- 旧合成 fixture 与 TASK-002～004 既有自动测试继续通过；
- 真实 `DefaultUnit`、`gopro`/`arcslma` SkeletonDataAsset 能在 PlayMode 中创建，Move/Attack 不因资源映射或初始化失败；
- 表现层仍只读，Home/Away 投影不改变权威结果；
- 没有场景、Prefab、Spine、Package 或 ProjectSettings 变更；
- SPEC、ARCHITECTURE、TEST_PLAN 反映实际实现与证据；未执行项明确写“未验证”；
- TASK-005 获得不依赖合成 type ID 或手工 Inspector 绑定的真实输入与表现入口。

## 停止并询问我的条件

- `gopro` 或 `arcslma` 找不到明确攻击动画，或实际动画时长不能无损转换为 20 TPS 的整数 Tick；
- 现有单位 JSON 中真实数值与 SPEC、Core 字段含义或 Spine 资源明显冲突；
- 需要为非整数速度/时间选择取整规则；
- `attackMethod` 的现有数值无法无歧义映射到已确认枚举；
- 为接入真实数据必须破坏性修改 Core type ID、BattleInput、事件 schema 或 TASK-003 Tick 顺序；
- 必须删除旧 fixture、重写 UnitFactory/UnitSkel、修改场景/Prefab/Spine 资源或批量迁移 GUID；
- 需要安装 Package、插件、Skill、MCP、第三方库或升级 Unity/Spine；
- 发现与本任务无关的业务/构建问题，且不处理就无法继续最小验证；
- 连续三次有实质差异的尝试仍无法推进。

## 完成报告格式

1. 修改文件；
2. `local-battle-v1` 完整字段和示例资源路径；
3. `unit-catalog-v1` 字段、源 JSON、生成/加载方式和 Player-safe 说明；
4. `gopro`/`arcslma` 的字段映射表、动画名称与真实时长证据；
5. Core 输入连接、验证和结构化错误；
6. Presentation 资源解析、旧组件兼容和动画降级策略；
7. 对 TASK-001～004 各自回测了什么、修改了什么及原因；
8. 实际运行的命令和 Unity 操作；
9. Editor 编译结果；
10. EditMode/PlayMode 测试数量、结果 XML 和日志；
11. 真实输入 10 次摘要与 winner/reason；
12. 人工动画检查结果；
13. 未验证项；
14. 所作假设；
15. 遗留风险；
16. 最终工作区状态与 diff 审查；
17. TASK-005 必须使用的输入资源、加载 API、目录/工厂接口和人工补验清单。
