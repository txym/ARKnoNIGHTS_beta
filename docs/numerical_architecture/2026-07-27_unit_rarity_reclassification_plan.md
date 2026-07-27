# Unit Rarity Reclassification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reclassify all current shop candidates by level-0 stats and abilities, move unsuitable candidates into a documented pending-removal section, rerun the rarity fit, and identify region shortages by peer-relative rarity weighting.

**Architecture:** A repository-local PowerShell extractor converts `BONDS_SPEC.md` and the external staging packages into a reproducible UTF-8 CSV snapshot. The numerical report combines computed matchup metrics with explicit manual ability-budget judgments; only after the first-pass removal decisions does it recompute the defender population, final rarities, and region scores. `BONDS_SPEC.md` receives only the reviewed outcome, while calculation evidence remains under `docs/numerical_architecture`.

**Tech Stack:** PowerShell 7/Windows PowerShell-compatible syntax, JSON, UTF-8 CSV, Markdown, Git

## Global Constraints

- Read `docs/SPEC.md` before execution and treat it as the gameplay fact source.
- Work from the current `docs/bonds/BONDS_SPEC.md`; preserve all user-authored ability text.
- The current scope is 83 shop candidates and 4 non-shop summon/derived units unless the roster changes before extraction.
- Read every base unit from `unit-levels.json` at `level: 0`; never substitute level 1.
- Read attack damage type from the matching base `unit-source-v1.json`.
- Read `_2` and `_3` only at their own `level: 0`, and use them only for elite-growth regression.
- Missing elite variants do not prevent elite promotion and are not removal reasons.
- Physical, magic, and true damage use the formulas confirmed in `docs/SPEC.md`.
- Formal attack interval is the source `attackIntervalSeconds × 0.5`.
- Elite 0/1/2/3 use 1/2/3/5 combat entities and 1/2/3/5 deployment-Cost multipliers.
- There is no forced total roster size or forced rarity quota.
- Region high-rarity comparison uses `W(R) = Σ[r × N(R,r)]` and `A(R) = W(R) / N(R)`.
- Do not modify Unity assets, packages, scenes, prefabs, or generated directories.
- Do not stage or commit `.superpowers/` or unrelated user changes.

---

### Task 1: Produce the reproducible roster snapshot

**Files:**
- Create: `docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1`
- Create: `docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv`
- Read: `docs/bonds/BONDS_SPEC.md`
- Read: `G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging/*/unit-levels.json`
- Read: `G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging/*/unit-source-v1.json`

**Interfaces:**
- Consumes: `-BondSpecPath`, `-StagingRoot`, and `-OutputCsvPath` absolute or repository-relative paths.
- Produces: UTF-8 CSV rows with `TypeId`, `DisplayName`, `Category`, `CurrentRarity`, `IsShopCandidate`, `RosterStatus`, `Regions`, `ResourceDirectory`, `DamageType`, base `MaxHitPoints/Attack/Defense/MagicResistance/AttackIntervalSeconds/EffectiveAttackIntervalSeconds/MoveSpeedMetresPerSecond/LifeDeduct`, and `HasElite2/HasElite3`.
- Produces for each elite level `N∈{2,3}`: `EliteNResourceDirectory` plus its level-0 `EliteNMaxHitPoints/EliteNAttack/EliteNDefense/EliteNMagicResistance/EliteNAttackIntervalSeconds/EliteNMoveSpeedMetresPerSecond/EliteNLifeDeduct`. All eight fields are populated when that variant directory exists and blank when the entire variant directory is absent; this does not alter `CurrentRarity`, `Category`, or `RosterStatus`.

- [ ] **Step 1: Implement strict unit-line and section parsing**

  Parse only lines matching `^(\d+)\s+(.+?)\s+稀有([1-6])(?:\s|$)`. Track the latest `##` category, recognize `1137/1138/10002/2033` as the current non-shop set, and reject duplicate type IDs rather than silently merging them.

- [ ] **Step 2: Implement base and variant package resolution**

  Resolve a base directory as `<typeId>_*` excluding names ending `_2` or `_3`. Require exactly one base directory and a `level: 0` row. Detect `_2` and `_3` by appending the suffix to that exact base directory name. For every variant directory that exists, read its own `unit-levels.json` through the same `Get-LevelZero` rule and reject missing or duplicate level 0; never substitute the base panel. A wholly absent variant directory remains valid and exports blank variant fields.

- [ ] **Step 3: Parse region membership**

  Read headers below `# 目前考虑使用的地区`, attach numeric ID-only lines to their current region, and ignore parenthesized or undefined IDs when building final counts.

- [ ] **Step 4: Add extractor assertions**

  The script must exit nonzero unless it finds exactly 83 unique shop candidates and 4 unique non-shop units. Assert known rows:

  - `1000`: HP 820, ATK 190, DEF 0, MR 20, interval 1.4, Physical.
  - `1089`: Magic damage.
  - `2031`: HP 35000, ATK 800, DEF 800, MR 50, `lifeDeduct=5`.
  - `1169`: HP 4000, ATK 300, DEF 300, interval 2.5.

- [ ] **Step 5: Run the extractor**

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1' `
    -BondSpecPath 'docs/bonds/BONDS_SPEC.md' `
    -StagingRoot 'G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging' `
    -OutputCsvPath 'docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv'
  ```

  Expected: exit 0, 87 data rows, of which 83 have `IsShopCandidate=True`.

- [ ] **Step 6: Validate the generated CSV**

  ```powershell
  $rows = Import-Csv -Encoding UTF8 'docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv'
  if ($rows.Count -ne 87) { exit 1 }
  if (@($rows | Where-Object IsShopCandidate -eq 'True').Count -ne 83) { exit 1 }
  if (@($rows | Where-Object { [int]$_.TypeId -eq 2031 -and [int]$_.LifeDeduct -eq 5 }).Count -ne 1) { exit 1 }
  ```

- [ ] **Step 7: Commit the reproducible snapshot**

  ```powershell
  git add -- docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1 docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv
  git commit -m "docs: add reproducible unit rarity dataset"
  ```

### Task 2: Compute first-pass matchup metrics

**Files:**
- Modify: `docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1`
- Regenerate: `docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv`

**Interfaces:**
- Consumes: Task 1 rows and the confirmed damage formulas.
- Produces additional CSV fields: `MedianDamagePerHit`, `MedianDps`, `MedianTtkSeconds`, `P75TtkSeconds`, `PhysicalFloorTargetRate`, `MedianIncomingTtdSeconds`, and `BaseChassisNotes`.

- [ ] **Step 1: Add deterministic damage helpers**

  Implement integer physical damage as `Max(attack-defense, floor(attack*5/100))`, magic damage as `Max(floor(attack*(100-resistance)/100), floor(attack*5/100))`, and true damage as `attack`.

- [ ] **Step 2: Build the defender sample**

  Exclude the four non-shop units and unattackable drones `1017/1042/1355/1146`. Require 79 defenders before first-pass removals.

- [ ] **Step 3: Calculate attack metrics**

  For every attacking shop candidate, calculate per-defender damage, DPS using the halved interval, and `ceil(HP/damage) × effectiveInterval` TTK. For `DamageType=None`, leave ordinary-attack metrics empty and mark the row `ability-only`.

- [ ] **Step 4: Calculate base survival metrics**

  Replay incoming ordinary attacks from every valid attacking candidate against each defender and report median time to death. Keep raw-base and ability-adjusted survival separate; do not encode ability text as JSON stats.

- [ ] **Step 5: Add numeric regression assertions**

  Assert the first-pass defender quantiles before ability adjustment:

  - DEF P25/P50/P75/P90 = `100/300/775/1040`.
  - MR P25/P50/P75/P90 = `0/20/32.5/50`.
  - HP P25/P50/P75/P90 = `3100/6000/11500/20000`.

- [ ] **Step 6: Regenerate and validate**

  Run the Task 1 process-scoped `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ...` command again. Reject NaN, Infinity, negative TTK, a nonpositive effective interval on an attacking unit, or missing damage type.

- [ ] **Step 7: Commit matchup metrics**

  ```powershell
  git add -- docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1 docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv
  git commit -m "docs: compute unit matchup metrics"
  ```

### Task 3: Assign ability budgets and first-pass rarities

**Files:**
- Create: `docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md`
- Read: `docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv`
- Read: `docs/bonds/BONDS_SPEC.md`

**Interfaces:**
- Consumes: Computed base metrics plus the exact ability descriptions in `BONDS_SPEC.md`.
- Produces: One 83-row review table with current rarity, proposed first-pass rarity, offense evidence, durability evidence, ability tier, elite nonlinear risk, and concise rationale.

- [ ] **Step 1: Classify every ability**

  Assign each ability one explicit budget class: `none`, `minor`, `moderate`, `major`, or `rule-changing`. Record damage type, trigger, uptime, expected targets, stack rule, silence rule, and whether each elite combat entity triggers it independently.

- [ ] **Step 2: Evaluate physical and magic attackers separately**

  Use `PhysicalFloorTargetRate` to expose low-attack physical units that fail against armor. Use actual MR matchups for magic attackers. Do not compare raw ATK as if both damage types had the same mitigation curve.

- [ ] **Step 3: Evaluate summons and area damage**

  Record summons using their level-0 combat body, blocking, target value, and entity count. Evaluate AoE at 1/2/3 targets. Treat the `2031/2033` summon cap as battle-wide shared, and explicitly report the resulting maximum bodies and target value.

- [ ] **Step 4: Evaluate elite nonlinear cases**

  At minimum include:

  - `1169`: DEF 300 at one E0 entity, DEF 700 per entity for one E2, and DEF 1300 per entity for two adjacent E2 units.
  - Unstackable drone auras: no linear entity multiplier.
  - Death summons and periodic summons: independent elite-entity triggers unless an explicit shared cap applies.
  - `1121`: repeated entity unlock triggers provide no extra benefit after allies are already released.

- [ ] **Step 5: Assign first-pass rarity**

  Apply the six absolute rarity roles from `2026-07-27_unit_rarity_reclassification_method.md`. Do not target a fixed count. Use deployment Cost as a later within-rarity balancing axis, not as a reason to hide a higher-access unit in an incorrect rarity.

- [ ] **Step 6: Audit table completeness**

  Verify exactly 83 unique type IDs appear once in the first-pass table and every row has a proposed rarity and rationale.

- [ ] **Step 7: Commit first-pass analysis**

  ```powershell
  git add -- docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md
  git commit -m "docs: add first-pass unit rarity analysis"
  ```

### Task 4: Select pending-removal candidates

**Files:**
- Modify: `docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md`

**Interfaces:**
- Consumes: First-pass table and the five removal standards in the method document.
- Produces: A pending-removal table containing full evidence and one primary reason per candidate.

- [ ] **Step 1: Identify dominated candidates**

  Compare units only against candidates with overlapping combat role. A lower raw score is insufficient; the candidate must also lack a distinct ability, region role, damage-type niche, rush role, summon role, or future category-design value.

- [ ] **Step 2: Identify nonlinear and implementation-risk candidates**

  Flag abilities whose multi-entity scaling remains uncontrolled after applying an explicit shared cap. Quantify the maximum body count, defensive stacking, AoE coverage, or target-value damage that causes the risk.

- [ ] **Step 3: Record keep/remove alternatives**

  For every pending-removal unit, record the smallest rule or value change that would justify retention. This distinguishes “remove from current scope” from permanent rejection.

- [ ] **Step 4: Verify removal reasons**

  Reject any removal reason based only on current rarity, lack of `_2/_3`, or weak/strong raw data. Verify that each pending-removal row cites at least one approved standard.

- [ ] **Step 5: Commit pending-removal analysis**

  ```powershell
  git add -- docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md
  git commit -m "docs: identify pending unit removals"
  ```

### Task 5: Refit the remaining roster

**Files:**
- Modify: `docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1`
- Regenerate: `docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv`
- Modify: `docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md`

**Interfaces:**
- Consumes: The pending-removal ID set.
- Produces: Second-pass defender quantiles, matchup metrics, final proposed rarity per retained unit, and final rarity distribution.

- [ ] **Step 1: Add an explicit exclusion parameter**

  Add `-PendingRemovalIds [int[]]` to the extractor. Preserve excluded rows in the CSV with `RosterStatus=PendingRemoval`, but omit them from defender and attacker populations.

- [ ] **Step 2: Recompute matchup distributions**

  Run the extractor with the exact pending-removal IDs. Record the new DEF/MR/HP quantiles and compare them with the first-pass values.

- [ ] **Step 3: Recheck adjacent rarity outliers**

  Review any retained unit whose ability-adjusted offense or durability crosses both neighboring rarity medians, whose physical floor rate is anomalous for its proposed tier, or whose E2 nonlinear value changes by more than the normal three-entity multiplier.

- [ ] **Step 4: Freeze final proposals**

  Give every retained unit exactly one final rarity and retain every pending-removal unit’s proposed rarity solely as reference. Report the final `R1/R2/R3/R4/R5/R6` counts without forcing them toward a target.

- [ ] **Step 5: Validate second-pass coverage**

  Verify `retained count + pending-removal count = 83`, no type ID occurs twice, and all retained rows have second-pass metrics.

- [ ] **Step 6: Commit the refit**

  ```powershell
  git add -- docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1 docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md
  git commit -m "docs: refit retained unit rarities"
  ```

### Task 6: Compare region coverage

**Files:**
- Modify: `docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md`

**Interfaces:**
- Consumes: Retained IDs, final rarities, and current region membership.
- Produces: Per-region `N(R)`, rarity counts, `W(R)`, `A(R)`, peer rank, median difference, early-access count, and supplement recommendation.

- [ ] **Step 1: Calculate region metrics**

  For every header under `# 目前考虑使用的地区`, count only defined and retained shop units. Calculate:

  ```text
  N(R) = Σ N(R,r)
  W(R) = Σ [r × N(R,r)]
  A(R) = W(R) / N(R)
  ```

- [ ] **Step 2: Compare against peers**

  Report each region’s `W` and `A` rank and its signed difference from the peer median. Interpret:

  - low `W` and low `A`: high-rarity structure shortage;
  - low `W` and normal/high `A`: total roster shortage;
  - normal `N` and low `A`: too few mid/late cores.

- [ ] **Step 3: Report early access separately**

  Count retained R1–R2 units per region. Do not fold this count into the high-rarity shortage label.

- [ ] **Step 4: Exclude undefined region-only IDs**

  IDs such as current Sargon notes that have no defined candidate entry must not contribute to `N`, `W`, or `A`; list them separately as possible supplement leads.

- [ ] **Step 5: Commit region analysis**

  ```powershell
  git add -- docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md
  git commit -m "docs: compare regional unit coverage"
  ```

### Task 7: Apply the reviewed outcome to BONDS_SPEC

**Files:**
- Modify: `docs/bonds/BONDS_SPEC.md`
- Modify: `docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_method.md`

**Interfaces:**
- Consumes: Final rarity table, pending-removal table, and region report.
- Produces: Updated candidate roster with preserved ability text and a populated `## 待移除` section.

- [ ] **Step 1: Update retained rarity labels**

  Change only the `稀有1..6` token on each retained unit line. Preserve IDs, names, ability wording, headings, spacing where practical, and non-shop status.

- [ ] **Step 2: Move pending-removal entries**

  Remove each pending-removal unit’s full line from its current candidate category and place it under `## 待移除`. Preserve its original category, recommended reference rarity, full ability text, and add one concise removal reason.

- [ ] **Step 3: Update method-document roster counts**

  Replace the pre-screening scope statement with both total candidate count and final retained/pending-removal counts. Keep 83 as the audited input total.

- [ ] **Step 4: Validate BONDS_SPEC**

  Confirm every one of the 83 original shop candidate IDs occurs exactly once either in the retained sections or `待移除`, and all four non-shop IDs remain outside the shop pool.

- [ ] **Step 5: Review the final diff**

  ```powershell
  git diff --check -- docs/bonds/BONDS_SPEC.md docs/numerical_architecture
  git diff -- docs/bonds/BONDS_SPEC.md docs/numerical_architecture
  ```

  Confirm no ability description was unintentionally removed or rewritten.

- [ ] **Step 6: Commit the final roster**

  ```powershell
  git add -- docs/bonds/BONDS_SPEC.md docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_method.md
  git commit -m "docs: reclassify unit rarities and roster"
  ```

### Task 8: Final verification and handoff

**Files:**
- Verify: `docs/bonds/BONDS_SPEC.md`
- Verify: `docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_method.md`
- Verify: `docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md`
- Verify: `docs/numerical_architecture/2026-07-27_unit_rarity_dataset.csv`
- Verify: `docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1`

**Interfaces:**
- Consumes: All completed artifacts.
- Produces: Evidence-backed completion report with test commands, counts, unverified assumptions, and remaining balance risks.

- [ ] **Step 1: Run the extractor from a clean process**

  Run the final extractor from a clean Windows PowerShell process. `-ExecutionPolicy Bypass` applies only to that process; do not modify the persistent machine or user execution policy. Construct the final pending-removal ID array inside the child process so the script's `[int[]]` parameter receives two values:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& 'docs/numerical_architecture/tools/Export-UnitRarityDataset.ps1' -BondSpecPath 'docs/bonds/BONDS_SPEC.md' -StagingRoot 'G:/ARKnoNIGHTS_tools/spine-fetcher-output-variants-20260725/staging' -OutputCsvPath '.superpowers/sdd/2026-07-27_unit_rarity_reclassification_plan/task-8-clean-process-output.csv' -PendingRemovalIds @(1007,1055)"
  ```

  Require exit 0, 87 data rows, and a SHA-256 hash byte-identical to the formal CSV. Do not use `-File ... -PendingRemovalIds 1007,1055` for this final array-valued invocation, because Windows PowerShell 5.1 in the `zh-CN` environment binds that token as the single integer `10071055`.

- [ ] **Step 2: Run structural assertions**

  Verify:

  - 83 original shop candidates accounted for;
  - 4 non-shop units preserved;
  - no missing base `level: 0`;
  - no duplicate IDs;
  - every retained unit has one final rarity;
  - every pending-removal unit has one reason;
  - every current region has `N/W/A` values.

- [ ] **Step 3: Scan for ambiguity and placeholders**

  ```powershell
  $placeholderPattern = 'T' + 'BD|TO' + 'DO|待' + '定|待' + '补充|需要进一步' + '分析'
  rg -n $placeholderPattern docs/bonds/BONDS_SPEC.md docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_*
  ```

  Any intentional unresolved issue must be converted into a concrete documented risk with an owner decision, not left as a placeholder.

- [ ] **Step 4: Check Git scope**

  ```powershell
  git status --short
  git diff --check HEAD
  git log --oneline -8
  ```

  Confirm `.superpowers/` and unrelated user files remain unstaged and unmodified by this work.

- [ ] **Step 5: Record verification limits**

  State that Unity compilation and PlayMode/EditMode tests were not run because the work changes documentation and analysis artifacts only. Flag actual combat simulation, final deployment Cost, and future type-bond restructuring as not validated by this pass.
