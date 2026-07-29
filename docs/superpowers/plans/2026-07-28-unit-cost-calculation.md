# Unit Cost Calculation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 使用本轮已确认的统一压缩模型，为 `BONDS_SPEC.md` 当前 94 个商店单位计算可复核的精英 0 基础部署 Cost，并把唯一权威 Cost 表写回 BONDS。

**Architecture:** 仓库内的 PowerShell 导出器从 BONDS 解析名单、名称、稀有度和已确认能力，从外部 staging 读取 E0/精英变体面板，经过基础对局指标、显式能力场景、档位压缩和轻量 R1/R2 团战校准后生成 CSV 与分析报告。能力参数保存在纯数据 `.psd1` 中，测试脚本验证公式和全部验收条件；独立写回脚本只维护 BONDS 中有开始/结束标记的 Cost 表，不改动其他段落。

**Tech Stack:** Windows PowerShell 5.1-compatible PowerShell、JSON、PSD1、UTF-8 CSV、Markdown、Git

## Global Constraints

- 执行前重新阅读 `docs/SPEC.md`、批准的 `docs/superpowers/specs/2026-07-28-unit-cost-design.md` 和当前工作树中的 `docs/bonds/BONDS_SPEC.md`。
- 当前名单预期为 94 个唯一商店 TypeId 和 5 个非商店 TypeId；名单变化时停止，先核对需求，不能悄悄沿用旧计数。
- 仅使用 BONDS、SPEC 和本轮明确确认的能力信息；不得从原作、PRTS 或其他外部资料补入能力。
- `10031` 不计隐匿；`1238/1243` 按普通近战物理；`10039` 仅量化 90% 物理/法术减伤；`10077` 按 2 SP/s、初始 3、需求 5、20 秒约 8 次召唤量化。
- 5 个非商店单位不生成独立 Cost；作为召唤或后继体时，其战力只计入母体。
- E0/E1/E2/E3 的实体数和部署 Cost 倍率固定为 `1/2/3/5`；本轮不改倍率。
- 不修改 `Assets/`、`Packages/`、`ProjectSettings/`、Unity 代码、场景、Prefab、单位 JSON 或生成目录。
- 不修改或覆盖已有的 `Export-UnitRarityDataset.ps1` 和 2026-07-27 稀有度分析产物。
- 当前 `BONDS_SPEC.md` 已有其他未提交修改。写回后只审查并保留本任务新增的标记区段；未经用户另行授权，不把整份 BONDS 暂存或提交。
- 所有临时产物写入 `Temp/UnitCostModel/`，不提交 `Temp/`、`.superpowers/` 或其他用户改动。
- 所有四舍五入使用 `[Math]::Round(value, 0, [MidpointRounding]::AwayFromZero)`。

---

### Task 1: 建立自测入口并导出当前 94 单位基础快照

**Files:**
- Create: `docs/numerical_architecture/tools/Test-UnitCostModel.ps1`
- Create: `docs/numerical_architecture/tools/Export-UnitCostDataset.ps1`
- Read: `docs/bonds/BONDS_SPEC.md`
- Read: `G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging/*/unit-levels.json`
- Read: `G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging/*/unit-source-v1.json`

**Interfaces:**
- `Test-UnitCostModel.ps1` accepts `-BondSpecPath`, `-StagingRoot`, and optional `-TempRoot`.
- `Export-UnitCostDataset.ps1` accepts `-BondSpecPath`, `-StagingRoot`, `-AbilityInputPath`, `-OutputCsvPath`, and `-AnalysisOutputPath`.
- 导出器任何输入不完整、重复或非法时返回非零退出码，不能生成看似成功的部分结果。

- [ ] **Step 1: 先写会失败的名单与面板测试**

  在 `Test-UnitCostModel.ps1` 中创建 `Temp/UnitCostModel/`，调用尚不存在的导出器，并声明最终导出必须满足：

  ```powershell
  $rows = @(Import-Csv -LiteralPath $outputCsv -Encoding UTF8)
  Assert-Equal 94 $rows.Count 'shop row count'
  Assert-Equal 94 @($rows.TypeId | Sort-Object -Unique).Count 'unique shop TypeId count'
  Assert-Equal 0 @($rows | Where-Object { $_.TypeId -in '1137','1138','2033','5504','10002' }).Count 'non-shop Cost rows'
  Assert-Equal 1 @($rows | Where-Object TypeId -eq '1000').Count 'known TypeId 1000'
  ```

  测试脚本必须在断言失败或子进程退出码非零时显式 `exit 1`，且“0 个测试”不能算通过。

- [ ] **Step 2: 运行测试并确认预期失败**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs/numerical_architecture/tools/Test-UnitCostModel.ps1' `
    -BondSpecPath 'docs/bonds/BONDS_SPEC.md' `
    -StagingRoot 'G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging'
  ```

  Expected: 非零退出，原因是 `Export-UnitCostDataset.ps1` 尚不存在或尚未产生 94 行；不是环境或编码错误。

- [ ] **Step 3: 实现严格的 BONDS 名单解析**

  只从 `## 商店单位（94）` 后的 `text` 代码块读取商店 TypeId，只从 `## 非商店单位（5）` 后的 `text` 代码块读取非商店 TypeId。再扫描单位定义行 `^(\d+)\s+(.+?)\s+稀有([1-6])(?:\s|$|[。.])`，为每个商店 ID 解析唯一名称和稀有度。

  断言：

  - 商店集合恰好 94 个且唯一；
  - 非商店集合恰好为 `1137/1138/2033/5504/10002`；
  - 两集合不相交；
  - 每个商店 ID 的所有重复定义具有相同名称和稀有度；
  - 最终稀有度分布为 `R1=9, R2=20, R3=14, R4=24, R5=21, R6=6`。

- [ ] **Step 4: 实现 staging 包解析**

  对每个 TypeId 解析唯一基础目录，排除 `_2/_3` 变体。读取 `unit-levels.json` 唯一 `level: 0` 的：

  `maxHitPoints/attack/defense/magicResistance/attackIntervalSeconds/moveSpeedMetresPerSecond/lifeDeduct`

  从 `unit-source-v1.json` 读取 `damageType`。`1322` 显式使用 `1322_wdgyht_2` 作为 E0，并把 `1322_wdgyht` 记录为 E2 证据，不让通用后缀规则覆盖 BONDS 的反向映射。

  `1238/1243/10039` 缺失的伤害类型由本轮已确认覆盖为 `Physical`，并在 `DamageTypeSource` 写入 `ConfirmedOverride`；其他缺失伤害类型一律失败。

  测试 fixture 只复制本次实际读取的 JSON：94 个商店基础目录、5 个非商店能力引用目录及 `1322_wdgyht` E2 证据目录，共 100 个目录、200 份 JSON。主导出、负向回归和完整工作流前后都比较项目外源文件的相对路径、长度与 SHA-256；不得创建任何指向 staging 的链接或 reparse point。

- [ ] **Step 5: 导出最小基础列**

  先输出：

  `TypeId, DisplayName, Rarity, ResourceDirectory, DamageType, DamageTypeSource, MaxHitPoints, Attack, Defense, MagicResistance, AttackIntervalSeconds, EffectiveAttackIntervalSeconds, MoveSpeedMetresPerSecond, LifeDeduct`

  `EffectiveAttackIntervalSeconds = AttackIntervalSeconds × 0.5`。CSV 使用稳定的 TypeId 数值升序和 UTF-8 编码。

- [ ] **Step 6: 添加已知值回归断言并跑绿**

  至少断言：

  - `1014`: HP 2000、ATK 350、DEF 100、MR 0、原始间隔 1.2、Physical；
  - `1089`: Magic；
  - `2031`: HP 35000、ATK 800、DEF 800、MR 50、`lifeDeduct=5`；
  - `1169`: HP 4000、ATK 300、DEF 300、原始间隔 2.5；
  - `1322`: E0 资源目录是 `1322_wdgyht_2`。

  重新运行 Step 2 命令。Expected: exit 0，输出 `94` 行且全部基础字段有效。

- [ ] **Step 7: 只提交新工具骨架**

  ```powershell
  git add -- docs/numerical_architecture/tools/Test-UnitCostModel.ps1 docs/numerical_architecture/tools/Export-UnitCostDataset.ps1
  git commit -m "docs: add unit cost dataset extractor"
  ```

---

### Task 2: 实现基础输出、防守和面板战力

**Files:**
- Modify: `docs/numerical_architecture/tools/Test-UnitCostModel.ps1`
- Modify: `docs/numerical_architecture/tools/Export-UnitCostDataset.ps1`

**Interfaces:**
- 追加 CSV 列：`RawMedianDps, WinsorizedMedianDps, RawMedianTtdSeconds, WinsorizedMedianTtdSeconds, OutputReference, DefenseReference, PanelPower`。
- 无普通攻击和不可攻击单位保留空的无意义轴，并由后续能力场景单独处理，不能用 0 或无限大伪造普通面板。

- [ ] **Step 1: 先写伤害公式单元测试**

  覆盖以下固定例子：

  ```powershell
  Assert-Equal 5  (Get-PhysicalDamage -Attack 100 -Defense 500) 'physical 5% floor'
  Assert-Equal 60 (Get-PhysicalDamage -Attack 100 -Defense 40)  'physical subtraction'
  Assert-Equal 5  (Get-MagicDamage -Attack 100 -MagicResistance 100) 'magic 5% floor'
  Assert-Equal 75 (Get-MagicDamage -Attack 100 -MagicResistance 25)  'magic resistance'
  Assert-Equal 100 (Get-TrueDamage -Attack 100) 'true damage'
  ```

  再写一个小型合成样本，验证 P5/P95 Winsorize、几何平均和中位数计算。先运行并确认新断言因 helper 尚未实现而失败。

- [ ] **Step 2: 实现正式伤害 helper**

  - 物理：`max(attack - defense, floor(attack × 0.05))`
  - 法术：`max(floor(attack × (100 - MR) / 100), floor(attack × 0.05))`
  - 真实：`attack`

  对负面板、非数值、非正攻击间隔和未知伤害类型显式失败。

- [ ] **Step 3: 构造有效攻击与防守样本**

  - 防守样本排除 BONDS 已确认不可被攻击的无人机；
  - 普通攻击样本排除 BONDS 已确认“不进行普通攻击”或 `damageType=None` 的单位；
  - 每个攻击者对所有有效防守者计算实际每击伤害与 DPS；
  - 每个防守者对所有有效普通攻击者计算 `ceil(HP / damagePerHit) × effectiveInterval`。

  保留未裁剪的单位中位 DPS/TTD；对两个轴分别使用全体有效值的 P5/P95 Winsorize 结果参与评分。

- [ ] **Step 4: 计算基础面板指数**

  令有效输出和防守样本的全体中位数为 `O_ref` 与 `D_ref`，对具有普通战斗体的单位计算：

  ```text
  PanelPower = sqrt((WinsorizedMedianDps / O_ref) ×
                    (WinsorizedMedianTtdSeconds / D_ref))
  ```

  无普通攻击、不可被攻击或路径型单位只保留可解释的原始值和 `PanelModelStatus`，在 Task 3 由专用能力场景赋值。

- [ ] **Step 5: 添加不变量测试并跑绿**

  断言所有可评分普通战斗体的 `PanelPower > 0` 且有限；对完全相同单位复制 n 份时，聚合输出和总耐久均放大 n 倍，几何组合结果也应放大 n 倍而不是 n²。

  运行：

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs/numerical_architecture/tools/Test-UnitCostModel.ps1' `
    -BondSpecPath 'docs/bonds/BONDS_SPEC.md' `
    -StagingRoot 'G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging'
  ```

  Expected: exit 0，94 行；没有 NaN、Infinity、负 TTK 或非法间隔。

- [ ] **Step 6: 提交基础战力计算**

  ```powershell
  git add -- docs/numerical_architecture/tools/Test-UnitCostModel.ps1 docs/numerical_architecture/tools/Export-UnitCostDataset.ps1
  git commit -m "docs: compute base unit combat power"
  ```

---

### Task 3: 把 BONDS 已确认能力转换为可审计场景

**Files:**
- Create: `docs/numerical_architecture/tools/UnitCostAbilityInputs.psd1`
- Modify: `docs/numerical_architecture/tools/Test-UnitCostModel.ps1`
- Modify: `docs/numerical_architecture/tools/Export-UnitCostDataset.ps1`
- Read: `docs/bonds/BONDS_SPEC.md`
- Read: `docs/SPEC.md`

**Interfaces:**
- PSD1 只存纯数据，不执行代码。顶层键固定为 `DamageTypeOverrides`, `UnitScenarios`, `ExplicitRiskOnly`。
- 每个 `UnitScenarios` 条目保存 `TypeId`, `ModelKind`, 已确认数值参数, `Evidence`, `UnquantifiedRisk`。
- 导出器追加：`AbilityModelKind, OutputScenarioLow, OutputScenarioMain, OutputScenarioHigh, DefenseScenarioMain, EquivalentEntityContribution, AbilityPowerMultiplier, ContinuousPower, AbilityEvidence, RiskFlags`。

- [ ] **Step 1: 先写已确认边界测试**

  测试必须精确验证：

  - `10039` 的物理和法术承伤倍率均为 `0.10`，蓄力、范围和阻挡未知项只进入 `RiskFlags`；
  - `10077` 在 20 秒内的召唤时点为约 `1.0, 3.5, 6.0, 8.5, 11.0, 13.5, 16.0, 18.5`，共 8 次，召唤体是 `10073`；
  - `10031` 没有 Stealth/隐匿修正；
  - `1238/1243` 没有附加法伤、远程或多方向修正；
  - `1137/1138/2033/5504/10002` 不产生独立 Cost 行。

  先运行并确认因能力输入尚不存在而失败。

- [ ] **Step 2: 录入能力输入**

  按 BONDS 当前文本逐项录入所有具备可量化参数的商店单位：

  - 自身增伤、攻速、减伤、闪避、回复、自损、变身；
  - 范围伤害 1/2/3 目标场景，主评分取 2；
  - 光环覆盖 1/3/5 个有效目标，主评分取 3；
  - 20 秒内召唤时点、召唤体 TypeId 和存活贡献；
  - 不可阻挡、直冲门、位移、不攻击、不可被攻击无人机的专用场景。

  BONDS 写了能力但缺少倍率、范围、间隔或持续时间时，必须在 `ExplicitRiskOnly` 明确列出 TypeId 和未量化原因。不能既无量化条目也无风险条目。

- [ ] **Step 3: 实现能力输入完整性检查**

  从 BONDS 中扫描带“能力描述/能力详情/能力：”的商店单位，并显式覆盖未带这些标记但仍写有机制的 `1058/1078/1080/1081/1083/1095/1281/1502`；要求每个单位在 `UnitScenarios` 或 `ExplicitRiskOnly` 至少出现一次。验证 PSD1 不引用 99 个有效 TypeId 之外的单位；非商店 TypeId 只允许作为召唤、后继或能力目标引用。

- [ ] **Step 4: 实现 20 秒能力场景**

  所有能力通过输出、防守或等效实体贡献修改连续战力：

  - 主评分使用中间 AoE/光环场景，低/高场景只作敏感度；
  - 召唤战力按确认的生成时点、剩余存活时间和召唤体面板积分；
  - 路径型/不攻击单位按目标价值、存活和路径压力计算，不补造普通 DPS；
  - 无人机只使用支援、覆盖、持续时间、路线、沉默和多实体规则；
  - 未量化能力的数值贡献固定为 0，只写风险列。

  导出 `PanelPower`、各能力修正分量和最终 `ContinuousPower`，不允许只保留总分。

- [ ] **Step 5: 运行边界和审计测试**

  除 Step 1 的固定断言外，检查：

  - 所有 94 行均有正且有限的 `ContinuousPower`；
  - `AbilityPowerMultiplier` 可由已导出的分量复算；
  - 任一 `RiskFlags` 非空的单位在分析报告风险表中出现；
  - 禁止词 `隐匿/Stealth/附加法术/多方向` 不得出现在 `10031/1238/1243` 的计分证据中。

- [ ] **Step 6: 提交能力输入与场景计算**

  ```powershell
  git add -- docs/numerical_architecture/tools/UnitCostAbilityInputs.psd1 docs/numerical_architecture/tools/Test-UnitCostModel.ps1 docs/numerical_architecture/tools/Export-UnitCostDataset.ps1
  git commit -m "docs: model confirmed unit abilities for cost"
  ```

---

### Task 4: 实现 R2—R6 压缩 Cost 曲线

**Files:**
- Modify: `docs/numerical_architecture/tools/Test-UnitCostModel.ps1`
- Modify: `docs/numerical_architecture/tools/Export-UnitCostDataset.ps1`

**Interfaces:**
- 追加 CSV 列：`RarityMedianPower, IsotonicRarityMedianPower, CompressionAlpha, RarityBaseCost, WithinTierFactor, RawCostBeforeRounding, FinalBaseCost`。
- 模型参数默认值：R6 anchor `28`、同档指数 `0.6`、修正区间 `0.75..1.35`、最终范围 `5..40`。

- [ ] **Step 1: 先写模型公式测试**

  使用合成 `M1..M6` 验证：

  - PAVA 单调回归输出非递减；
  - 当 `M6/M2 > 4` 时，`alpha = ln(4)/ln(M6/M2)`；
  - 当 `M6/M2 <= 4` 时，`alpha = 1`；
  - 当 `M6 <= M2` 时显式失败；
  - `B6=28` 且 `B6/B2<=4`；
  - 同档修正是 `clamp((P/Mr)^0.6, 0.75, 1.35)`；
  - `.5` 使用 away-from-zero 四舍五入。

  先运行并确认 helper 未实现导致预期失败。

- [ ] **Step 2: 实现 PAVA 与档位中位战力**

  计算每档 `M_r=median(ContinuousPower)`，使用 pool-adjacent-violators algorithm 得到非递减 `M̃_r`。保留原始和回归后中位数供报告审计。

- [ ] **Step 3: 计算 R2—R6 基准**

  ```text
  alpha = min(1, ln(4) / ln(M̃6 / M̃2))
  Br = 28 × (M̃r / M̃6)^alpha
  ```

  仅对 `r=2..6` 使用该基准；若 `M̃6<=M̃2` 立即失败。

- [ ] **Step 4: 计算单位原始 Cost 与整数 Cost**

  ```text
  factor = clamp((Pi / M̃r)^0.6, 0.75, 1.35)
  rawCost = Br × factor
  roundedCost = round-away-from-zero(rawCost)
  FinalBaseCost = clamp(roundedCost, 5, 40)
  ```

  R1 此时只计算 `B1_initial` 和未校准整数 Cost，最终值在 Task 5 写入。

- [ ] **Step 5: 实现顶端稀疏性参数扫描**

  默认模型若产生超过 8 个 `FinalBaseCost > 30`，生成可审计的统一参数候选：

  1. `MaxWithinTierFactor` 从 `1.35` 以 `0.01` 递减到 `1.15`；
  2. 若仍不满足，再让 R6 anchor 从 `28.00` 以 `0.25` 递减到 `26.00`；
  3. 在满足其他约束的候选中，优先选择距默认参数平方偏差最小者；
  4. 若仍无法降到 8 个以内，停止并报告冲突，不逐单位手工降价。

  CSV 和分析报告记录最终实际采用的 anchor 与修正上限。

- [ ] **Step 6: 运行模型不变量测试**

  验证：

  - R2—R6 中位 Cost 非递减；
  - `medianCost(R6)/medianCost(R2) <= 4`；
  - 所有临时 R2—R6 Cost 为 `5..40` 整数；
  - 同一稀有度内，按 `ContinuousPower` 升序时 `FinalBaseCost` 不下降；
  - Cost 大于 30 的单位不超过 8 个。

- [ ] **Step 7: 提交压缩模型**

  ```powershell
  git add -- docs/numerical_architecture/tools/Test-UnitCostModel.ps1 docs/numerical_architecture/tools/Export-UnitCostDataset.ps1
  git commit -m "docs: implement compressed rarity cost curve"
  ```

---

### Task 5: 用 72 场轻量对比校准 R1

**Files:**
- Modify: `docs/numerical_architecture/tools/Test-UnitCostModel.ps1`
- Modify: `docs/numerical_architecture/tools/Export-UnitCostDataset.ps1`

**Interfaces:**
- 追加 CSV 列：`R1InitialCost, R1CalibrationKappa, R1CalibratedRawCost`。
- 分析摘要记录每个预算/阵容配对/方向的胜方、剩余实体、剩余目标价值和总胜率。

- [ ] **Step 1: 先写阵容生成和场次数测试**

  固定预算 `54/99/126/195`。每档对每个预算构造：

  - `Low`：按 `(Cost, TypeId)` 升序取最低 Cost 半区并稳定轮转；
  - `Median`：按 `(|Cost-tierMedian|, TypeId)` 升序稳定轮转；
  - `High`：按 `(Cost desc, TypeId)` 取最高 Cost 半区并稳定轮转。

  允许重复 TypeId；剩余预算小于候选集最低 Cost 时停止。断言每档每预算恰好 3 套阵容，`4×3×3×2=72` 场。

- [ ] **Step 2: 实现简化集火—死亡—移除循环**

  每个阵容展开为单位副本列表。副本保存能力场景后的 DPS、有效 HP、等效实体、阻挡容量和目标价值。双方按目标顺序同时集火：

  1. 根据攻击类型和当前目标防御重算最低伤害约束后的实际 DPS；
  2. 求双方击杀当前目标所需时间，推进到较小事件时间；
  3. 同步扣除双方目标 HP，死亡目标从列表移除；
  4. 一个方向使用 `(TargetValue desc, TypeId)`，交换方向使用逆序；
  5. 一方无存活目标时结束；同时归零时按剩余等效实体、剩余目标价值、总阻挡依次判定，仍相同则平局。

  不实现逐 Tick 寻路、动画、技能 AI 或阵型枚举。每场必须有固定事件上限，超过上限视为测试失败。

- [ ] **Step 3: 搜索统一 `kappa1`**

  对 `0.01..1.50`、步长 `0.01` 的候选统一缩放全部 R1：

  ```text
  R1FinalCost = clamp(round(kappa1 × R1InitialCost), 2, 40)
  ```

  对每个候选重新组队并跑 72 场。筛选 R1 胜率 `40%..60%` 的候选，按以下顺序选择：

  1. 胜率最接近 50%；
  2. `|kappa1-1|` 最小；
  3. `kappa1` 数值较小。

  没有候选通过时停止并输出完整候选摘要，不逐个调整 R1。

- [ ] **Step 4: 添加 R1 校准断言**

  验证最终：

  - 72 场全部有合法结果；
  - R1 胜率在 `40%..60%`；
  - 所有 R1 使用同一 `kappa1`；
  - R1 内按 `ContinuousPower` 升序时最终 Cost 不下降；
  - R1 Cost 为 `2..40` 整数，R2—R6 Cost 仍为 `5..40` 整数。

- [ ] **Step 5: 提交轻量校准**

  ```powershell
  git add -- docs/numerical_architecture/tools/Test-UnitCostModel.ps1 docs/numerical_architecture/tools/Export-UnitCostDataset.ps1
  git commit -m "docs: calibrate R1 costs against R2"
  ```

---

### Task 6: 复核精英效率与赤金—Cost 单位数

**Files:**
- Modify: `docs/numerical_architecture/tools/Test-UnitCostModel.ps1`
- Modify: `docs/numerical_architecture/tools/Export-UnitCostDataset.ps1`

**Interfaces:**
- 追加 CSV 列：`Elite2ResourceDirectory, Elite2PowerPerCostRatio, Elite3ResourceDirectory, Elite3PowerPerCostRatio, EliteEfficiencyRisk`。
- 分析报告包含赤金压力点 `34/76/126/184/250/324` 与参数化部署预算网格，不声明永久 Cost 收入规则。

- [ ] **Step 1: 先写精英倍率测试**

  对无专用变体的合成单位验证 E0/E1/E2/E3 的总战力与 Cost 同为 `1/2/3/5` 倍，单位 Cost 效率不变。对专用 E2/E3 面板验证读取的是各目录自身 `level: 0`。

- [ ] **Step 2: 读取精英变体并计算异常**

  使用真实变体时分别计算：

  ```text
  E2TotalPower / (3 × C0)
  E3TotalPower / (5 × C0)
  ```

  与 `E0Power/C0` 比较。缺目录时继承最近较低阶段；只写 `EliteEfficiencyRisk`，不改变 `1/2/3/5` 倍率。若提高 C0 会让 E0 同档明显失真，只在报告中保留风险。

- [ ] **Step 3: 实现赤金—Cost 参数检查**

  对金量 `G ∈ {34,76,126,184,250,324}`、部署预算 `K ∈ {54,99,126,195}` 和每档最终中位 Cost 计算：

  ```text
  N(G,K,r) = min(floor(G/r), floor(K/medianCost(r)))
  ```

  输出每档可购买且可部署的单位数、赤金限制/Cost 限制来源，以及 R1/R2、R2/R6 数量比。该表只作压力检查。

- [ ] **Step 4: 运行精英和经济断言**

  检查无专用变体单位的理论效率相等；所有真实变体效率有限且正；经济网格所有数量为非负整数；报告明确写出“永久 Cost 收入未确认”。

- [ ] **Step 5: 提交复核计算**

  ```powershell
  git add -- docs/numerical_architecture/tools/Test-UnitCostModel.ps1 docs/numerical_architecture/tools/Export-UnitCostDataset.ps1
  git commit -m "docs: add elite and economy cost checks"
  ```

---

### Task 7: 生成正式 CSV 与分析报告

**Files:**
- Create: `docs/numerical_architecture/2026-07-28_unit_cost_dataset.csv`
- Create: `docs/numerical_architecture/2026-07-28_unit_cost_analysis.md`
- Modify: `docs/numerical_architecture/tools/Test-UnitCostModel.ps1`
- Modify: `docs/numerical_architecture/tools/Export-UnitCostDataset.ps1`

- [ ] **Step 1: 先写正式产物完整性测试**

  测试要求 CSV 恰好 94 行、列集合完整、TypeId 与 BONDS 商店集合完全相同；报告必须包含：

  - 数据完整性；
  - R2—R6 相邻战力/Cost 增长率；
  - `R6/R2`；
  - R1/R2 的 72 场结果与 `kappa1`；
  - 每档 Cost 分布；
  - 每个 Cost 大于 30 单位的理由；
  - 精英效率风险；
  - 未量化能力；
  - 赤金—Cost 参数网格；
  - 未运行 Unity 编译、EditMode、PlayMode 和构建的说明。

- [ ] **Step 2: 生成正式产物**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs/numerical_architecture/tools/Export-UnitCostDataset.ps1' `
    -BondSpecPath 'docs/bonds/BONDS_SPEC.md' `
    -StagingRoot 'G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging' `
    -AbilityInputPath 'docs/numerical_architecture/tools/UnitCostAbilityInputs.psd1' `
    -OutputCsvPath 'docs/numerical_architecture/2026-07-28_unit_cost_dataset.csv' `
    -AnalysisOutputPath 'docs/numerical_architecture/2026-07-28_unit_cost_analysis.md'
  ```

  Expected: exit 0，正式 CSV 94 行，报告所有章节非空。

- [ ] **Step 3: 独立复算并比较**

  使用测试脚本导出到 `Temp/UnitCostModel/`，再比较正式 CSV 与临时 CSV 的规范化内容：

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs/numerical_architecture/tools/Test-UnitCostModel.ps1' `
    -BondSpecPath 'docs/bonds/BONDS_SPEC.md' `
    -StagingRoot 'G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging' `
    -ExpectedCsvPath 'docs/numerical_architecture/2026-07-28_unit_cost_dataset.csv'
  ```

  Expected: exit 0；按稳定排序和换行规范化后内容一致。

- [ ] **Step 4: 人工审查模型边界**

  逐项阅读所有 `Cost>30`、`RiskFlags`、精英效率异常和最高/最低同档离群单位。确认没有用未写入 BONDS 的能力解释 Cost；如发现模型参数需要统一调整，返回 Task 4 后重跑全部产物，不直接编辑 CSV。

- [ ] **Step 5: 提交可复算产物**

  ```powershell
  git add -- docs/numerical_architecture/tools/Test-UnitCostModel.ps1 docs/numerical_architecture/tools/Export-UnitCostDataset.ps1 docs/numerical_architecture/tools/UnitCostAbilityInputs.psd1 docs/numerical_architecture/2026-07-28_unit_cost_dataset.csv docs/numerical_architecture/2026-07-28_unit_cost_analysis.md
  git commit -m "docs: calculate unified unit deployment costs"
  ```

---

### Task 8: 将唯一 Cost 权威表写回 BONDS

**Files:**
- Create: `docs/numerical_architecture/tools/Update-BondsUnitCostTable.ps1`
- Modify: `docs/numerical_architecture/tools/Test-UnitCostModel.ps1`
- Modify: `docs/bonds/BONDS_SPEC.md`
- Read: `docs/numerical_architecture/2026-07-28_unit_cost_dataset.csv`

**Interfaces:**
- 写回脚本接受 `-BondSpecPath`, `-CostCsvPath`, optional `-OutputPath`。
- 只创建或替换：

  ```markdown
  <!-- UNIT-COST-TABLE:START -->
  ## 商店单位精英 0 基础部署 Cost
  ...
  <!-- UNIT-COST-TABLE:END -->
  ```

- [ ] **Step 1: 先写临时副本写回测试**

  把当前 BONDS 复制到 `Temp/UnitCostModel/BONDS_SPEC.md`，在副本上运行尚未实现的写回脚本。测试：

  - 首次写入恰好生成一个开始标记和一个结束标记；
  - 第二次写入字节内容不变；
  - 标记外内容在规范化换行后与写入前相同；
  - 表格恰好 94 个数据行；
  - 表格列严格为 `TypeId | 名称 | 稀有度 | 精英 0 基础 Cost C₀`；
  - TypeId 集合和 CSV 完全一致；
  - 5 个非商店 ID 不出现；
  - 每个 R1 Cost 为 `2..40` 整数，每个 R2—R6 Cost 为 `5..40` 整数。

- [ ] **Step 2: 实现局部、幂等写回**

  第一次写入时，将标记区段放在“商店单位（94）”代码块及其说明段之后、“非商店单位（5）”标题之前。已有标记时只替换标记内部。拒绝重复/缺单边标记、重复 CSV TypeId、BONDS/CSV 集合不一致或非法 Cost。

- [ ] **Step 3: 在临时副本上跑绿**

  运行完整自测，确认写回副本的标记外 SHA-256 与原文规范化内容相同，且二次执行幂等。

- [ ] **Step 4: 写入真实 BONDS**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs/numerical_architecture/tools/Update-BondsUnitCostTable.ps1' `
    -BondSpecPath 'docs/bonds/BONDS_SPEC.md' `
    -CostCsvPath 'docs/numerical_architecture/2026-07-28_unit_cost_dataset.csv'
  ```

  Expected: exit 0；真实 BONDS 中只有一个标记区段和 94 行 Cost。

- [ ] **Step 5: 审查 BONDS 差异但不暂存整份文件**

  ```powershell
  git diff -- docs/bonds/BONDS_SPEC.md
  ```

  确认本任务新增内容局限于 Cost 标记区段。由于该文件执行前已有用户修改，不运行 `git add docs/bonds/BONDS_SPEC.md`；在最终汇报中明确它保持未暂存。

- [ ] **Step 6: 提交写回工具，不提交混合所有权 BONDS**

  ```powershell
  git add -- docs/numerical_architecture/tools/Update-BondsUnitCostTable.ps1 docs/numerical_architecture/tools/Test-UnitCostModel.ps1
  git commit -m "docs: add safe BONDS cost table writer"
  ```

---

### Task 9: 最终验收、独立审查和交付

**Files:**
- Verify: `docs/bonds/BONDS_SPEC.md`
- Verify: `docs/numerical_architecture/2026-07-28_unit_cost_dataset.csv`
- Verify: `docs/numerical_architecture/2026-07-28_unit_cost_analysis.md`
- Verify: `docs/numerical_architecture/tools/*.ps1`
- Verify: `docs/numerical_architecture/tools/UnitCostAbilityInputs.psd1`

- [ ] **Step 1: 从零清理临时测试目录并重跑**

  仅删除已验证位于项目 `Temp/UnitCostModel/` 的测试目录，再运行完整测试：

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs/numerical_architecture/tools/Test-UnitCostModel.ps1' `
    -BondSpecPath 'docs/bonds/BONDS_SPEC.md' `
    -StagingRoot 'G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging' `
    -ExpectedCsvPath 'docs/numerical_architecture/2026-07-28_unit_cost_dataset.csv'
  ```

  记录退出码、断言数量、失败项和临时日志路径。Expected: exit 0，断言数大于 0。

- [ ] **Step 2: 逐项核对 13 条设计验收**

  至少执行一个只读验证命令，打印：

  - 商店 94 行且唯一、与 BONDS 集合相同；
  - 非商店 0 行；
  - R1 Cost 全为 2..40 整数，R2—R6 Cost 全为 5..40 整数；
  - R2—R6 中位 Cost 单调；
  - `R6/R2<=4`；
  - `Cost>30` 数量不超过 8；
  - 各档 Cost 对连续战力单调不反转；
  - R1 72 场胜率 40%..60%；
  - 每个高 Cost 和每个风险均在报告中有记录。

- [ ] **Step 3: 审查最终工作树**

  ```powershell
  git status --short
  git diff --check
  git diff -- docs/bonds/BONDS_SPEC.md
  git diff --stat
  ```

  将执行前已有用户修改与本任务文件分开核对。不得格式化、回滚或暂存无关修改。

- [ ] **Step 4: 做一次只读独立审查**

  重点检查：

  - 公式与批准设计一致；
  - 能力来源没有越界；
  - R1 校准没有逐单位人为修价；
  - 召唤体没有独立 Cost；
  - BONDS 是唯一权威 Cost 表，其他重复条目没有 Cost；
  - CSV 可由脚本稳定复算；
  - 未量化能力没有偷偷获得数值。

  发现问题时回到对应 Task 修复并重新跑全套测试，不能只改最终 CSV 或 BONDS 表。

- [ ] **Step 5: 明确未运行的 Unity 验证**

  本轮只修改文档、CSV 和仓库分析脚本，不运行 Unity 编译、EditMode、PlayMode 或平台构建。最终报告将这些项目标记为“未运行（本轮不涉及 Unity 运行时文件）”，不得写成通过。

- [ ] **Step 6: 交付**

  最终报告包含：

  1. 已完成的统一定价结果和关键分布；
  2. 修改/新增文件；
  3. 实际测试命令、退出码和断言数量；
  4. 未运行的 Unity 验证；
  5. 剩余能力风险与建议人工复核；
  6. `BONDS_SPEC.md` 因执行前已有用户修改而保持未暂存的说明。
