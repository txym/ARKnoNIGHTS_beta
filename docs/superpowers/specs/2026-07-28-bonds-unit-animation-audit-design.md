# BONDS 单位动画结构审计设计

## 状态

用户已确认先统计全部已导入模型的真实动画结构，再决定动画播放类型和单位 JSON
字段映射。本阶段只产生只读审计证据，不批量生成或修改单位源 JSON。

## 背景

`docs/bonds/BONDS_SPEC.md` 当前列出 93 个 TypeId。上一阶段已从
`G:\ARKnoNIGHTS_tools\spine-fetcher-output-variants-20260725\staging`
导入 172 个完整模型变体，并按项目规范放入
`Assets/Resources/Characters/<完整变体键>/`。

项目现有 `unit-source-v1` 只为三个单位配置了动画名称和攻击动画时长：

- `1000_gopro` 使用 `UnitSkelType1`；
- `5503_arcslma` 与 `5504_arcslmi` 使用 `UnitSkelType2`；
- `UnitSkelType1` 在代码中固定使用
  `Run_Begin/Run_Loop/Run_End`；
- `UnitSkelType2` 使用单个循环移动动画；
- 正式 Battle Track 的表现桥接只读取一项移动动画名称，并不使用三段移动流程。

因此，不能仅凭动画名称差异新增大量 `UnitSkelTypeN`，也不能在未统计真实资源前
假定所有模型都属于现有两类。

## 目标

1. 用 Unity/Spine API 读取 BONDS 清单中全部 172 个项目内
   `SkeletonDataAsset`。
2. 为每个变体记录全部动画的精确名称和 Spine 报告的持续秒数。
3. 统计相同动画名称集合、常见动作词、分段移动结构、多攻击动作和特殊状态动作。
4. 明确列出缺失资源、无法解析、重复动画名、非法时长及角色候选歧义。
5. 为下一阶段“动画播放类型设计与单位 JSON 导入”提供可重复生成的机器可读证据。

## 非目标

- 不修改 `Assets/GameData/Units/Json` 或
  `Assets/GameData/Units/EliteVariants/Json`。
- 不修改任何 Spine 原始文件、Unity 派生资产、头像或 `.meta`。
- 不新增 `UnitSkelType`，不改变播放逻辑。
- 不选择最终的移动、攻击、受击、死亡或状态切换动画。
- 不写入 `attackAnimationDurationSeconds`。
- 不扩充 `unit-catalog-v1`，不实现 `damageType=None`、不可阻挡或
  `actionMethod` 的运行时行为。
- 不访问网络或重新下载资源。

## 输入与身份映射

审计复用 `scripts/Import-BondsUnitResources.ps1` 的清单语义：

- BONDS 中必须有 93 个唯一 TypeId；
- 项目目标必须有 172 个唯一完整变体键；
- 普通条目的项目变体键等于 staging 文件夹名；
- staging `1322_wdgyht_2` 映射到项目默认变体
  `1322_wdgyht`；
- staging `1322_wdgyht` 映射到项目精二变体
  `1322_wdgyht_2`。

每个项目目标按以下 Resources 路径读取：

```text
Characters/<unitKey>/enemy_<unitKey>_SkeletonData
```

审计只以项目内规范目标键记录资源身份，同时保留 `sourceUnitKey` 供 1322
来源追踪。

## 审计输出

机器可读输出使用 `bonds-unit-animation-audit-v1`，保存到：

```text
Temp/bonds-unit-animation-audit-v1.json
```

该文件是可重建证据，不提交到 Git。根对象至少包含：

```text
schemaVersion
generatedAtUtc
typeIdCount
variantCount
variants
signatures
tokenSummary
diagnostics
```

每个 `variants` 条目至少包含：

```text
typeId
unitKey
sourceUnitKey
skeletonDataResourcePath
animationCount
animations[]
exactNameSignature
caseFoldedTokenSummary[]
```

每个动画记录：

```text
name
durationSeconds
```

`durationSeconds` 使用 Spine `Animation.Duration` 的原始单精度值，以
InvariantCulture 的 round-trip 格式序列化；本阶段不换算 Tick、不按六位小数
提前截断。

`signatures` 按完整、区分大小写的动画名称集合分组，记录该组包含的变体键。
`tokenSummary` 只做字面统计，例如名称中出现
`idle/run/move/walk/attack/atk/hit/die/death/skill/begin/loop/end/a/b`
等不区分大小写的词段；单字母状态段同时覆盖 `A_Attack` 和 `Attack_A`
这类前、后缀形式。它不宣称某个动画已经被选为游戏动作。

## 实现边界

新增一个 Editor-only 审计器，职责限定为：

1. 解析 BONDS 资源导入清单；
2. 使用 `Resources.Load<SkeletonDataAsset>` 读取规范路径；
3. 调用 `GetSkeletonData(true)`；
4. 枚举并稳定排序 `SkeletonData.Animations`；
5. 构造稳定的审计数据；
6. 写出 UTF-8、无 BOM、稳定字段顺序的 JSON；
7. 输出一行可定位的 Unity 日志摘要。

PowerShell 入口负责：

1. 检查同一项目没有其他 Unity Editor 或 batchmode 进程；
2. 串行调用 Unity `-executeMethod`；
3. 在 launcher 及其项目 handoff 进程存活期间捕获已经完整校验的输出；
4. 等待所有使用该项目的 Unity 进程结束；
5. 检查日志标记与完整输出 schema；
6. 拒绝缺字段、计数不一致或缺少诊断的半成品输出。

Editor 与 PowerShell 两层都必须拒绝经 junction/symlink 跳出项目 `Temp`
物理边界的输出路径；只允许普通的 `Temp` 路径段。

审计器不得引用项目外 staging 文件来读取动画；staging 仅由既有清单逻辑提供
`sourceUnitKey` 身份证据。实际动画事实必须来自项目内 Unity 已导入资源。

## 失败与诊断

以下任一情况使审计失败，不输出“成功”摘要：

- BONDS TypeId 数不是 93；
- 172 个项目变体实际覆盖的 TypeId 集合不等于 BONDS 的 93 个 TypeId 集合；
- 目标变体数不是 172；
- `unitKey` 或 Resources 路径重复；
- `SkeletonDataAsset` 缺失；
- `GetSkeletonData(true)` 返回 null 或抛出异常；
- 模型没有任何动画；
- 同一模型内出现重复动画名；
- 动画名称为空；
- 动画时长为 NaN、Infinity 或负数；
- 1322 的来源到目标映射不符合已确认规则；
- 输出 JSON 无法重新解析或计数不一致。

单个动画的时长为 0 可以作为事实记录，但必须在 `diagnostics` 中列出，供下一阶段
判断是否为占位动作。

## 测试与验证

测试驱动顺序：

1. 先添加 EditMode 测试，断言审计清单为 93 TypeId/172 变体、1322 映射正确，
   且现有三个样例资源能提供已知动画与时长。
2. 运行测试并确认因审计器尚不存在而失败。
3. 实现最小审计器并运行目标 EditMode 测试。
4. 串行执行完整审计，解析输出并核对：
   - 93 个 TypeId；
   - 172 个唯一变体；
   - 每个变体至少一个动画；
   - 所有模型均成功加载；
   - 1000 的 `Attack` 为 1 秒；
   - 5503 的 `Attack` 为约 2.666667 秒；
   - 1322 来源与项目目标键保持纠正后的对应关系。
5. 运行既有 BONDS 资源导入校验，证明审计没有改变资源内容。
6. 执行 `git diff --check` 并审查最终差异。

同一项目当前若由 Unity Editor 打开，则不得并行启动 batchmode。必须等待用户关闭
Editor 后再执行 Unity 审计；不得终止用户的 Editor 进程。

## 阶段交付

审计完成后向用户提供：

- 动画总数、唯一动画名称数和唯一名称集合数量；
- 移动候选结构分布；
- 普通攻击候选数量分布；
- 多攻击/多状态模型列表；
- 无明显移动、攻击或死亡候选的模型列表；
- 零时长或其他诊断；
- 基于统计证据的 2～3 种播放类型设计方案。

用户确认下一阶段方案前，不批量生成 93 个基础 JSON，也不写入任何变体的
`attackAnimationDurationSeconds`。
