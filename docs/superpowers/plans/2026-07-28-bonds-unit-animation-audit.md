# BONDS Unit Animation Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a repeatable, read-only Unity/Spine inventory of every animation and duration in the 172 imported BONDS model variants, then report the observed structures before designing animation playback types.

**Architecture:** Add one Editor-only audit builder that discovers canonical project variants from the 93 BONDS TypeIds, loads each `SkeletonDataAsset` through Unity/Spine, and writes a versioned JSON report under `Temp`. Add a PowerShell runner that enforces single-Unity access, invokes the Editor method, waits for completion, and validates the output. The audit records facts and lexical summaries only; it does not select gameplay animations or edit unit JSON.

**Tech Stack:** Unity 2022.3.62f1c1, C#, Spine-Unity, Unity Test Framework/NUnit, Windows PowerShell 5.1, existing project scripts.

## Global Constraints

- Work directly in `G:\ARKnoNIGHTS_beta`; do not create a worktree and do not dispatch subagents.
- Preserve all existing user and prior-task changes. Stage or commit only files named by the current task.
- Do not modify `Assets/GameData/Units/Json`, `Assets/GameData/Units/EliteVariants/Json`, `Assets/Resources/Characters`, `Assets/Resources/ProfilePicture`, or `Assets/Resources/BattleData`.
- Do not launch Unity while another Unity process is using `G:\ARKnoNIGHTS_beta`.
- Use only project-local scripts and the installed Unity/Spine packages; do not install dependencies or access the network.
- The audit must discover exactly 93 BONDS TypeIds and exactly 172 canonical project variants.
- Preserve the confirmed remap: source `1322_wdgyht_2` becomes project `1322_wdgyht`, and source `1322_wdgyht` becomes project `1322_wdgyht_2`.
- Record Spine animation names and `Animation.Duration` values without choosing move, attack, hit, death, idle, or state-transition roles.
- Write generated evidence only beneath `Temp/`; never commit generated audit JSON or Unity logs.
- Stop after reporting the animation statistics and ambiguous structures. Do not proceed to unit JSON generation in this plan.

## File Structure

- Create `Assets/Game/Editor/Battle/BondsUnitAnimationAudit.cs`: Editor-only identity discovery, Spine enumeration, validation, stable DTO construction, JSON writing, and `-executeMethod` entry point.
- Create `Assets/Game/Tests/EditMode/Battle/BondsUnitAnimationAuditEditModeTests.cs`: reflection-based tests for the Editor assembly, all 172 variants, 1322 remapping, known sample durations, JSON round-trip, and stable ordering.
- Create `scripts/Invoke-BondsUnitAnimationAudit.ps1`: single-Unity process guard, batch invocation, timeout handling, log/output validation, and concise summary.
- Create `scripts/tests/Test-BondsUnitAnimationAuditRunner.ps1`: isolated PowerShell validation tests using temporary audit fixtures.
- Modify `scripts/README.md`: document the animation-audit command, outputs, and single-Unity prerequisite.
- Generate, but do not commit, `Temp/bonds-unit-animation-audit-v1.json`.
- Generate, but do not commit, `Temp/bonds-unit-animation-audit.log`.
- Include Unity-generated `.meta` files for the three new repository assets; never hand-copy a GUID.

---

### Task 1: Build the Unity/Spine animation inventory

**Files:**
- Create: `Assets/Game/Tests/EditMode/Battle/BondsUnitAnimationAuditEditModeTests.cs`
- Create: `Assets/Game/Editor/Battle/BondsUnitAnimationAudit.cs`
- Create through Unity import: corresponding `.meta` files

**Interfaces:**
- Consumes: `docs/bonds/BONDS_SPEC.md`
- Consumes: `Assets/Resources/Characters/<unitKey>/enemy_<unitKey>_SkeletonData.asset`
- Produces: `BondsUnitAnimationAudit.BuildDocument() : BondsUnitAnimationAuditDocument`
- Produces: `BondsUnitAnimationAudit.Serialize(BondsUnitAnimationAuditDocument) : string`
- Produces: `BondsUnitAnimationAudit.Run() : void`
- Produces DTO fields: `schemaVersion`, `generatedAtUtc`, `typeIdCount`, `variantCount`, `variants`, `signatures`, `tokenSummary`, `diagnostics`

- [ ] **Step 1: Write the failing EditMode inventory test**

Create `BondsUnitAnimationAuditEditModeTests.cs` in the existing
`ArknoNights.Battle.Tests` namespace. Use reflection because the production
type belongs to `Assembly-CSharp-Editor`:

```csharp
private static object BuildDocument()
{
    var auditType = Type.GetType("BondsUnitAnimationAudit, Assembly-CSharp-Editor");
    Assert.That(auditType, Is.Not.Null, "Editor animation audit type must exist.");
    var method = auditType.GetMethod(
        "BuildDocument",
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert.That(method, Is.Not.Null, "BuildDocument must exist.");
    return method.Invoke(null, null);
}
```

Add these three tests before production code exists:

1. `BuildDocument_EnumeratesAllCanonicalBondsVariantsWithKnownSampleDurations`
   reflects the public DTO fields and asserts:

```text
schemaVersion == "bonds-unit-animation-audit-v1"
typeIdCount == 93
variantCount == 172
variants have 172 unique unitKey values in ordinal order
1322_wdgyht has sourceUnitKey 1322_wdgyht_2
1322_wdgyht_2 has sourceUnitKey 1322_wdgyht
1000_gopro contains Attack with duration 1.0f
5503_arcslma contains Attack with duration within 0.000001f of 2.666667f
every variant has at least one animation
every resource path is Characters/<key>/enemy_<key>_SkeletonData
```

2. `Serialize_RoundTripsAllDurationsAndStableOrdering` calls
   `BuildDocument`, calls `Serialize` twice after setting
   `generatedAtUtc` to a fixed value, and asserts byte equality plus exact
   float bit equality after `JsonUtility.FromJson`.
3. `BuildDocument_DoesNotModifyImportedAssets` snapshots the
   `AssetDatabase.GetAssetDependencyHash` values for all discovered
   `SkeletonDataAsset` paths, calls `BuildDocument`, and asserts every hash is
   unchanged.

The tests must inspect real project resources rather than mock Spine objects.

- [ ] **Step 2: Run the test and verify the RED state**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BondsUnitAnimationAuditEditModeTests' `
  -OutputDirectory 'G:\ARKnoNIGHTS_beta\Temp\bonds-unit-animation-audit-red' `
  -NoGraphics
```

Expected: three discovered tests fail because
`BondsUnitAnimationAudit, Assembly-CSharp-Editor` does not exist. A compiler
error or zero-test result is not the expected RED state and must be fixed
before continuing.

- [ ] **Step 3: Implement canonical identity discovery and Spine enumeration**

Create `BondsUnitAnimationAudit.cs` with Editor-only imports:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
```

Use these exact constants:

```csharp
internal static class BondsUnitAnimationAudit
{
    internal const string SchemaVersion = "bonds-unit-animation-audit-v1";
    private const int ExpectedTypeIdCount = 93;
    private const int ExpectedVariantCount = 172;
    private const string BondSpecRelativePath = "docs/bonds/BONDS_SPEC.md";
    private const string CharacterAssetRoot = "Assets/Resources/Characters";
    private const string CharacterResourceRoot = "Characters";
    private const string DefaultOutputRelativePath =
        "Temp/bonds-unit-animation-audit-v1.json";
}
```

Add these exact methods to that class:

```text
internal static BondsUnitAnimationAuditDocument BuildDocument()
internal static string Serialize(BondsUnitAnimationAuditDocument document)
public static void Run()
```

`BuildDocument` must:

1. Resolve the repository root from `Application.dataPath`.
2. Read BONDS as strict UTF-8 and parse the two fenced TypeId sections with
   the same regular-expression contract as the existing resource test.
3. Reject any count other than 93 and any duplicate TypeId.
4. Enumerate only top-level directories under `CharacterAssetRoot` whose
   leading decimal ID belongs to the BONDS set.
5. Sort `unitKey` values with `StringComparer.Ordinal`, reject duplicates,
   require exactly 172, and require their actual TypeId set to equal all 93
   BONDS TypeIds.
6. Derive `sourceUnitKey` using:

```csharp
private static string SourceUnitKey(string unitKey)
{
    return unitKey == "1322_wdgyht"
        ? "1322_wdgyht_2"
        : unitKey == "1322_wdgyht_2"
            ? "1322_wdgyht"
            : unitKey;
}
```

7. Load
   `Resources.Load<SkeletonDataAsset>("Characters/" + unitKey + "/enemy_" + unitKey + "_SkeletonData")`.
8. Call `GetSkeletonData(true)`, enumerate non-null animations, and sort them
   by exact name using `StringComparer.Ordinal`.
9. Reject empty names, duplicate names, missing/empty animation sets,
   negative/non-finite durations, missing resources, and Spine exceptions.
10. Add a diagnostic for each zero-duration animation without failing.
11. Set `exactNameSignature` to the ordered animation names joined with the
    unit-separator character `\u001f`.
12. Tokenize names with
    `Regex.Matches(name, @"[A-Z]+(?=[A-Z][a-z]|\d|$)|[A-Z]?[a-z]+|[A-Z]+|\d+")`,
    lower-case with `InvariantCulture`, de-duplicate per variant, and sort.
13. Group exact signatures in ordinal order and aggregate token counts in
    ordinal order.

Define `[Serializable]` field-only DTOs so `JsonUtility` can serialize them:

```csharp
internal sealed class BondsUnitAnimationAuditDocument
{
    public string schemaVersion;
    public string generatedAtUtc;
    public int typeIdCount;
    public int variantCount;
    public BondsUnitAnimationAuditVariant[] variants;
    public BondsUnitAnimationAuditSignature[] signatures;
    public BondsUnitAnimationAuditToken[] tokenSummary;
    public string[] diagnostics;
}

internal sealed class BondsUnitAnimationAuditVariant
{
    public int typeId;
    public string unitKey;
    public string sourceUnitKey;
    public string skeletonDataResourcePath;
    public int animationCount;
    public BondsUnitAnimationAuditAnimation[] animations;
    public string exactNameSignature;
    public string[] caseFoldedTokenSummary;
}

internal sealed class BondsUnitAnimationAuditAnimation
{
    public string name;
    public float durationSeconds;
}

internal sealed class BondsUnitAnimationAuditSignature
{
    public string exactNameSignature;
    public string[] unitKeys;
}

internal sealed class BondsUnitAnimationAuditToken
{
    public string token;
    public int variantCount;
    public int animationCount;
}
```

Set `generatedAtUtc` with
`DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)`. Keep DTO field
declaration order stable.

- [ ] **Step 4: Implement stable serialization and output validation**

`Serialize` must call `JsonUtility.ToJson(document, true) + "\n"`, then parse
the result back with `JsonUtility.FromJson<BondsUnitAnimationAuditDocument>`.
Reject schema/count drift, missing arrays, or any animation duration whose
round-tripped IEEE-754 value differs:

```csharp
if (BitConverter.SingleToInt32Bits(expected.durationSeconds)
    != BitConverter.SingleToInt32Bits(actual.durationSeconds))
{
    throw new InvalidOperationException(
        "BONDS_ANIMATION_AUDIT_DURATION_ROUNDTRIP_FAILED unitKey="
        + variant.unitKey + " animation=" + expected.name);
}
```

`Run` must:

1. Read optional `-bondsAnimationAuditOutput <absolute path>` from
   `Environment.GetCommandLineArgs()`.
2. Default to `Temp/bonds-unit-animation-audit-v1.json` under the repository.
3. Require the resolved output path to stay under the repository `Temp`
   directory.
4. Create only the exact output directory.
5. Write UTF-8 without BOM using `File.WriteAllText`.
6. Re-read and verify byte text equality with `Serialize(document)`.
7. log:

```text
BONDS_ANIMATION_AUDIT_COMPLETE typeIds=93 variants=172 signatures=<n> diagnostics=<n> output=<path>
```

Do not call `AssetDatabase.Refresh` and do not modify any Unity asset.

- [ ] **Step 5: Run the target EditMode test and verify GREEN**

Run the Step 2 command with output directory:

```text
G:\ARKnoNIGHTS_beta\Temp\bonds-unit-animation-audit-green
```

Expected: one or more tests discovered, zero failures, zero skipped. Inspect
the XML and log; compilation warnings/errors related to the audit are not
acceptable.

- [ ] **Step 6: Commit the Unity audit builder**

After the target EditMode suite passes, stage only:

```text
Assets/Game/Editor/Battle/BondsUnitAnimationAudit.cs
Assets/Game/Editor/Battle/BondsUnitAnimationAudit.cs.meta
Assets/Game/Tests/EditMode/Battle/BondsUnitAnimationAuditEditModeTests.cs
Assets/Game/Tests/EditMode/Battle/BondsUnitAnimationAuditEditModeTests.cs.meta
```

Commit:

```powershell
git commit -m "test: add bonds spine animation audit"
```

Do not stage imported resources, HUD changes, BONDS edits, or other existing
worktree changes.

---

### Task 2: Add a safe repeatable batch audit runner

**Files:**
- Create: `scripts/tests/Test-BondsUnitAnimationAuditRunner.ps1`
- Create: `scripts/Invoke-BondsUnitAnimationAudit.ps1`
- Modify: `scripts/README.md`

**Interfaces:**
- Consumes: installed `Unity.exe`, project path, and
  `BondsUnitAnimationAudit.Run`
- Produces: `Test-BondsUnitAnimationAuditOutput -Path <string>`
- Produces: process exit code `0` only for a valid 93/172 audit
- Produces: `Temp/bonds-unit-animation-audit-v1.json`
- Produces: `Temp/bonds-unit-animation-audit.log`

- [ ] **Step 1: Write the failing PowerShell validator test**

Create `scripts/tests/Test-BondsUnitAnimationAuditRunner.ps1`. It must dot
source the runner, construct a temporary valid fixture with:

```text
schemaVersion = bonds-unit-animation-audit-v1
typeIdCount = 93
variantCount = 172
172 unique variants
non-empty animation arrays
```

Assert that `Test-BondsUnitAnimationAuditOutput` accepts the valid fixture.
Then create three invalid copies and assert rejection for:

```text
schemaVersion = unknown
variantCount = 171
duplicate unitKey
```

Use an exact temporary directory under
`[System.IO.Path]::GetTempPath()` and remove only that verified directory in
`finally`.

- [ ] **Step 2: Run the PowerShell test and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\tests\Test-BondsUnitAnimationAuditRunner.ps1
```

Expected: FAIL because
`scripts/Invoke-BondsUnitAnimationAudit.ps1` or
`Test-BondsUnitAnimationAuditOutput` does not exist.

- [ ] **Step 3: Implement the validator and batch runner**

Create `scripts/Invoke-BondsUnitAnimationAudit.ps1` with parameters:

```powershell
[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$ProjectPath,
    [string]$OutputPath,
    [string]$LogPath,
    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 900,
    [switch]$NoGraphics
)
```

Define:

```powershell
function Test-BondsUnitAnimationAuditOutput {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
}
```

Implement the function by reading strict UTF-8 JSON, requiring schema
`bonds-unit-animation-audit-v1`, every required root/variant/animation field,
counts 93/172, actual coverage of 93 TypeIds, exactly 172 unique non-empty
`unitKey` values, matching `animationCount`, non-empty signature/token arrays,
and at least one animation with a non-empty name and non-negative finite
duration for every variant. Throw a message prefixed
`BONDS_ANIMATION_AUDIT_OUTPUT_INVALID` for every invalid fixture.

When the script is dot-sourced, define functions without invoking Unity.
When executed normally:

1. Require a non-empty `UnityPath`, then default `ProjectPath` to the
   repository root.
2. Default output/log to the exact two `Temp` paths named above.
3. Resolve all paths, require output/log to remain beneath project `Temp`,
   and reject reparse points from `Temp` through either target.
4. Check `Unity.exe`, `Assets`, `Packages`, and `ProjectSettings`.
5. Query `Win32_Process` and reject an existing Unity command line that
   contains the exact project path.
6. Delete only stale output/log files at the two resolved exact paths.
7. start Unity with:

```text
-batchmode
-projectPath <project>
-executeMethod BondsUnitAnimationAudit.Run
-bondsAnimationAuditOutput <output>
-logFile <log>
-quit
```

Append `-nographics` when requested.

8. Use `Start-Process -PassThru`, poll for a fully valid snapshot while the
   launcher or any project handoff `Unity.exe` remains alive, enforce the
   timeout, and terminate only the process started by this script if it times
   out.
9. Require normal observed exit codes, the
   `BONDS_ANIMATION_AUDIT_COMPLETE` log marker, and a valid output.
10. Print one concise summary and return exit code 0.

- [ ] **Step 4: Run the PowerShell validator test and verify GREEN**

Run the Step 2 command.

Expected:

```text
BONDS_ANIMATION_AUDIT_RUNNER_VALID
```

with exit code 0.

- [ ] **Step 5: Document the runner usage**

Add an example to `scripts/README.md`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-BondsUnitAnimationAudit.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -NoGraphics
```

State that the script fails if this project is open in Unity and writes only
`Temp/bonds-unit-animation-audit-v1.json` plus
`Temp/bonds-unit-animation-audit.log`.

- [ ] **Step 6: Commit the runner**

Stage only:

```text
scripts/Invoke-BondsUnitAnimationAudit.ps1
scripts/tests/Test-BondsUnitAnimationAuditRunner.ps1
scripts/README.md
```

Commit:

```powershell
git commit -m "build: add bonds animation audit runner"
```

---

### Task 3: Execute the audit and report observed structures

**Files:**
- Generate only: `Temp/bonds-unit-animation-audit-v1.json`
- Generate only: `Temp/bonds-unit-animation-audit.log`
- No committed source changes

**Interfaces:**
- Consumes: Tasks 1 and 2
- Produces: verified statistics and ambiguity lists for the next design phase

- [ ] **Step 1: Confirm the Unity queue is free**

Run:

```powershell
Get-CimInstance Win32_Process |
  Where-Object { $_.Name -eq 'Unity.exe' } |
  Select-Object ProcessId, CommandLine
```

Expected: no Unity process using `G:\ARKnoNIGHTS_beta` and no other project
test/build process that would violate the project-wide single Unity queue.
Do not terminate a user-owned Editor.

- [ ] **Step 2: Run the complete animation audit**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-BondsUnitAnimationAudit.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -OutputPath 'G:\ARKnoNIGHTS_beta\Temp\bonds-unit-animation-audit-v1.json' `
  -LogPath 'G:\ARKnoNIGHTS_beta\Temp\bonds-unit-animation-audit.log' `
  -NoGraphics
```

Expected: exit 0 and a summary reporting 93 TypeIds and 172 variants.

- [ ] **Step 3: Validate the generated evidence independently**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command `
  ". .\scripts\Invoke-BondsUnitAnimationAudit.ps1; Test-BondsUnitAnimationAuditOutput -Path .\Temp\bonds-unit-animation-audit-v1.json"
```

Then parse the JSON and calculate:

```text
total animation records
unique exact animation names
unique exact-name signatures
variant counts with 0, 1, or multiple attack/atk tokens
variant counts with single move/run/walk tokens
variant counts with begin+loop+end token structures
variant counts with 0, 1, or multiple die/death tokens
unit keys containing skill, transform, change, a, or b state tokens
zero-duration diagnostics
```

Candidate statistics must be labeled lexical observations, not final gameplay
role selections.

- [ ] **Step 4: Re-run existing resource-integrity checks**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\tests\Test-BondsUnitResource1322Remap.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\tests\Test-BondsUnitResourceImport.ps1
```

Expected:

```text
BONDS_1322_REMAP_MANIFEST_VALID
BONDS_RESOURCE_IMPORT_MANIFEST_VALID variants=172 typeIds=93
```

Then run the existing Unity resource test:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BondsUnitResourceImportEditModeTests' `
  -OutputDirectory 'G:\ARKnoNIGHTS_beta\Temp\bonds-unit-resource-post-audit' `
  -NoGraphics
```

Require a non-zero test count and zero failures/skips.

- [ ] **Step 5: Perform final static verification**

Run:

```powershell
git diff --check
git status --short
git log -3 --oneline
```

Inspect the audit log for:

```text
error CS
UnhandledException
BONDS_ANIMATION_AUDIT_
Spine
```

Confirm no resource, unit JSON, generated catalog, scene, prefab, package, or
project setting was changed by this phase.

- [ ] **Step 6: Stop at the review checkpoint**

Report:

- exact commands and result counts;
- output/log paths;
- animation/signature/token statistics;
- ambiguous or exceptional model lists;
- zero-duration or resource diagnostics;
- existing unrelated worktree changes preserved;
- full suite/build not run, because this phase is an Editor-only read-only
  audit.

Present 2–3 evidence-based playback-structure options, but do not modify unit
JSON or animation runtime code until the user approves the next design.
