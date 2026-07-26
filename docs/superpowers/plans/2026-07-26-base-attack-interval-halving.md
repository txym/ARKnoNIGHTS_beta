# 基础攻击间隔折半 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将正式单位的基础攻击间隔改为源 JSON 配置的一半，使 Battle Core、攻击动画/出伤时点和单位详情 UI 使用一致的实际间隔。

**Architecture:** 在 `unit-source-v1` 数据边界提供唯一的派生秒数，目录生成器将派生秒数按 20 TPS 向上取整并写入 Player-safe 目录；Core 继续把目录中的 `AttackIntervalTicks` 当作实际间隔，不在 Runner 内重复折半。UI 只格式化同一权威 Tick，并以最多两位小数显示。

**Tech Stack:** Unity 2022.3.62f1c1、C#、Unity Test Framework/NUnit、Unity `JsonUtility`、仓库 `scripts/Invoke-UnityTests.ps1`

## Global Constraints

- 源 JSON 中 `attackIntervalSeconds` 的数值保持不变。
- 正式单位使用 `ceil(attackIntervalSeconds × 0.5 × 20)` 生成实际 `AttackIntervalTicks`。
- `attackAnimationDurationSeconds` 不折半，继续按 20 TPS 向上取整。
- Core、表现层和 UI 不得再次折半 `AttackIntervalTicks`。
- 合成 fixture 中直接填写的 `attackIntervalTicks` 保持实际 Tick 语义。
- 攻击间隔 UI 最多显示两位小数，去除无意义的末尾零且不显示 `s`。
- 不修改场景、Prefab、ScriptableObject、Package、ProjectSettings 或 Unity 版本。

---

### Task 1: 锁定真实目录与战斗时序

**Files:**
- Modify: `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs:541-669`
- Modify: `Assets/GameData/Units/UnitJson.cs:1-42`
- Modify: `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs:44-55`
- Regenerate: `Assets/Resources/BattleData/unit-catalog-v1.json`

**Interfaces:**
- Consumes: `UnitJson.attackIntervalSeconds : float`
- Produces: `UnitJson.BaseAttackIntervalSeconds : float` and catalog `UnitDefinition.AttackIntervalTicks : int`
- Preserves: `BattleRunner.StartAttacks()` consumes `AttackIntervalTicks` as the already-derived actual interval

- [ ] **Step 1: Write failing catalog and combat assertions**

Update `RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources` with literal expected values:

```csharp
Assert.AreEqual(14, gopro.Definition.AttackIntervalTicks);
Assert.AreEqual(40, arcslma.Definition.AttackIntervalTicks);
Assert.That(arcslmi.Definition.AttackIntervalTicks, Is.EqualTo(15));
```

Rename `RealCatalog_ArcslmaDamageArrivesOnlyAfterItsFullEffectiveAnimationDuration` to `RealCatalog_ArcslmaDamageUsesHalvedBaseIntervalAsEffectiveDuration` and assert the observable combat contract:

```csharp
Assert.AreEqual(54, attack.OriginalAnimationTicks);
Assert.AreEqual(40, attack.EffectiveAnimationTicks);
Assert.AreEqual(attack.Tick + 40, attack.PlannedDamageTick);
```

The production mutation caught by these tests is either omitting the `0.5` conversion or incorrectly keeping the original 54-Tick animation as the effective damage delay.

- [ ] **Step 2: Run the targeted EditMode fixture and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattleCoreEditModeTests' `
  -OutputDirectory 'Temp/AttackInterval/Red-Core' `
  -NoGraphics
```

Expected: nonzero exit with failures showing the old catalog values `28/80/30` or arcslma effective duration `54`, while NUnit XML contains a nonzero test count.

- [ ] **Step 3: Add the single derived source value**

Add the derived property to `UnitJson` without changing serialized fields:

```csharp
public const float BaseAttackIntervalMultiplier = 0.5f;
public float BaseAttackIntervalSeconds => attackIntervalSeconds * BaseAttackIntervalMultiplier;
```

Update the catalog conversion:

```csharp
var attackIntervalTicks = ConvertSecondsToTicks(
    source.BaseAttackIntervalSeconds,
    sourcePath + ":baseAttackIntervalSeconds");
```

Do not change `ConvertSecondsToTicks`, `attackAnimationDurationSeconds`, `BattleInput`, or `BattleRunner`.

- [ ] **Step 4: Regenerate the Player-safe catalog**

First verify no Unity process is using this project, then run:

```powershell
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod UnitCatalogGenerator.Generate `
  -logFile 'G:\ARKnoNIGHTS_beta\Temp\AttackInterval\CatalogGenerate.log'
```

Expected: exit code `0`, no compilation errors, and `unit-catalog-v1.json` contains:

```json
"typeId": "1000",
"attackIntervalTicks": 14
```

```json
"typeId": "5503",
"attackIntervalTicks": 40
```

```json
"typeId": "5504",
"attackIntervalTicks": 15
```

- [ ] **Step 5: Re-run the targeted EditMode fixture and verify GREEN**

Repeat Step 2 with output directory `Temp/AttackInterval/Green-Core`.

Expected: exit code `0`, NUnit result `Passed`, failed `0`, skipped `0`, and a nonzero test count.

- [ ] **Step 6: Commit the catalog and combat behavior**

```powershell
git add -- `
  'Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs' `
  'Assets/GameData/Units/UnitJson.cs' `
  'Assets/Resources/BattleData/unit-catalog-v1.json' `
  'Assets/Game/Editor/Battle/UnitCatalogGenerator.cs'
git commit -m "feat: halve base attack intervals"
```

### Task 2: 同步 UI 精度与旧调试适配

**Files:**
- Modify: `Assets/Game/Tests/EditMode/Battle/UnitDetailProjectionEditModeTests.cs:14-23`
- Modify: `Assets/Game/Runtime/Details/UnitDetailSnapshot.cs:99-104`
- Modify: `Assets/Game/Runtime/Initial/UnitFactory.cs:145-166`

**Interfaces:**
- Consumes: `UnitDetailNumberFormatter.AttackInterval(int ticks)` and `UnitJson.BaseAttackIntervalSeconds`
- Produces: attack interval text with format `0.##`; legacy `UnitTemplate.attackInterval` using the same derived seconds
- Preserves: UI reads `UnitDetailSnapshot.AttackIntervalTicks`; it does not read or halve JSON itself

- [ ] **Step 1: Write the failing UI formatting assertions**

Replace the single old attack-interval assertion with:

```csharp
Assert.AreEqual("0.7", UnitDetailNumberFormatter.AttackInterval(14));
Assert.AreEqual("2", UnitDetailNumberFormatter.AttackInterval(40));
Assert.AreEqual("0.75", UnitDetailNumberFormatter.AttackInterval(15));
```

The production mutation caught by the third assertion is retaining the old one-decimal `0.#` formatter, which renders 15 Tick as `0.8`.

- [ ] **Step 2: Run the targeted formatter test and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitDetailProjectionEditModeTests.NumberFormatter_UsesValuesWithoutUnitSuffixes' `
  -OutputDirectory 'Temp/AttackInterval/Red-Ui' `
  -NoGraphics
```

Expected: nonzero exit with expected `0.75` but actual `0.8`.

- [ ] **Step 3: Implement the minimal formatter and legacy mapping**

Change only the attack interval formatter:

```csharp
public static string AttackInterval(int ticks) =>
    (ticks / (float)BattleInput.TicksPerSecond).ToString("0.##", CultureInfo.InvariantCulture);
```

Make the old debug/template adapter use the shared derived seconds:

```csharp
so.attackInterval = j.BaseAttackIntervalSeconds;
```

Do not add a second `0.5` constant or change move-speed formatting.

- [ ] **Step 4: Re-run the targeted formatter test and verify GREEN**

Repeat Step 2 with output directory `Temp/AttackInterval/Green-Ui`.

Expected: exit code `0`, NUnit result `Passed`, total `1`, failed `0`, skipped `0`.

- [ ] **Step 5: Commit the UI and compatibility mapping**

```powershell
git add -- `
  'Assets/Game/Tests/EditMode/Battle/UnitDetailProjectionEditModeTests.cs' `
  'Assets/Game/Runtime/Details/UnitDetailSnapshot.cs' `
  'Assets/Game/Runtime/Initial/UnitFactory.cs'
git commit -m "fix: display precise base attack intervals"
```

### Task 3: 完整回归、记录与审查

**Files:**
- Modify after evidence exists: `docs/TEST_PLAN.md`
- Review only: all files changed by Tasks 1-2

**Interfaces:**
- Consumes: generated catalog, NUnit XML, Unity logs, Git diff
- Produces: recorded verification evidence and a reviewed final patch

- [ ] **Step 1: Run the full EditMode suite**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -OutputDirectory 'Temp/AttackInterval/Full-EditMode' `
  -NoGraphics
```

Expected: exit code `0`, result `Passed`, failed `0`, skipped `0`, total greater than `0`.

- [ ] **Step 2: Run the full PlayMode suite**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -OutputDirectory 'Temp/AttackInterval/Full-PlayMode' `
  -NoGraphics
```

Expected: exit code `0`, result `Passed`, failed `0`, skipped `0`, total greater than `0`.

- [ ] **Step 3: Inspect structured results and logs**

For both suites:

```powershell
Get-Content -Raw -Encoding UTF8 'Temp/AttackInterval/Full-EditMode/summary.txt'
Get-Content -Raw -Encoding UTF8 'Temp/AttackInterval/Full-PlayMode/summary.txt'
Select-String -Path 'Temp/AttackInterval/Full-EditMode/EditMode.log','Temp/AttackInterval/Full-PlayMode/PlayMode.log' `
  -Pattern 'error CS|Compilation failed|Scripts have compiler errors|Unhandled Exception'
```

Expected: summaries report `Passed`, zero failures/skips and nonzero totals; the error scan returns no matches related to this change.

- [ ] **Step 4: Record actual evidence in TEST_PLAN**

Append a dated section that records:

- catalog generation command and values `14/40/15`;
- RED failures and their expected reason;
- EditMode and PlayMode commands, exact totals, failures and skips;
- result XML/log paths;
- unperformed manual checks or builds as “未验证”.

Do not write claimed counts before reading the XML summaries.

- [ ] **Step 5: Review the final diff and protected resources**

Run:

```powershell
git status --short
git diff --check HEAD
git diff HEAD -- `
  'Assets/GameData/Units/UnitJson.cs' `
  'Assets/Game/Editor/Battle/UnitCatalogGenerator.cs' `
  'Assets/Resources/BattleData/unit-catalog-v1.json' `
  'Assets/Game/Battle/Core/Simulation/BattleRunner.cs' `
  'Assets/Game/Runtime/Details/UnitDetailSnapshot.cs' `
  'Assets/Game/Runtime/Initial/UnitFactory.cs' `
  'Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs' `
  'Assets/Game/Tests/EditMode/Battle/UnitDetailProjectionEditModeTests.cs' `
  'docs/TEST_PLAN.md'
```

Expected: source unit JSON, `BattleRunner.cs`, scenes, Prefabs and `.meta` files are unchanged; only the approved conversion, generated values, formatter, tests and verification record differ. Existing user paths `.superpowers/` and `docs/bonds/` remain untouched.

- [ ] **Step 6: Commit verification documentation**

```powershell
git add -- 'docs/TEST_PLAN.md'
git commit -m "docs: verify halved attack intervals"
```
