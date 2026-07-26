# LAN Lobby Visual Diff Reporting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate a non-blocking visual-difference report for LAN lobby Player captures against `图9.png` and `图10.png`, including an auditable per-screen UI-sprite usage table.

**Architecture:** A PowerShell evidence helper validates the capture manifest, approved asset map, exact reference images, and safe ignored output path before it creates anything. The same helper normalizes reference pixels into the 1920×1080 Player coordinate system, applies named reference masks, calculates per-region metrics, and emits overlay/heatmap images plus Markdown and JSON. Existing capture production code remains responsible only for obtaining real screenshots and a provenance manifest.

**Tech Stack:** Unity 2022.3.62f1c1, existing `LanLobbyCaptureSuite`, Windows PowerShell, .NET `System.Drawing`, NUnit/Unity Test Framework, existing `scripts/Invoke-UnityTests.ps1`.

## Global Constraints

- Do not add packages, third-party binaries, generated art, or external services.
- Replica UI raster art remains exclusively the approved `G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess` source set; compare references are input evidence, not game assets.
- The visual report is informative: visual mismatch produces `ATTENTION`, never a nonzero exit code.
- Missing/duplicate `图9.png` or `图10.png`, invalid manifest, unmapped sprite, invalid capture dimensions, or unsafe output path must fail before the requested output directory exists.
- Outputs are allowed only under the verified Unity project root’s ignored `Temp/` or `Artifacts/` directories.
- Existing `LanLobbyCaptureSuite`, `LanLobbyView`, battle HUD, scene, Prefab, protocol, and PlayerState behavior must not change for this feature.
- Actual captures are `1920×1080`; each decoded reference's native dimensions are recorded before independent X/Y normalization for non-blocking layout/color reporting only. Current real evidence is `图9.png` `2102×1149` and `图10.png` `2107×1153`.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `scripts/LanLobbyEvidence.Common.ps1` | Shared safe-output, manifest, exact-reference and asset-map/provenance validation functions. |
| `scripts/ExportLanLobbyEvidence.ps1` | Retains existing side-by-side export and consumes shared validation rather than duplicating it. |
| `scripts/TestLanLobbyEvidenceCommonSmoke.ps1` | Creates a synthetic five-record manifest and validates common input/provenance failure behavior. |
| `scripts/ExportLanLobbyVisualDiff.ps1` | Produces normalized overlays, masked heatmaps, metrics, Markdown/JSON report and asset-usage table. |
| `scripts/TestExportLanLobbyEvidenceSmoke.ps1` | Preserves existing evidence-export safety tests after shared-helper extraction. |
| `scripts/TestLanLobbyVisualDiffSmoke.ps1` | Creates synthetic PNG/manifest fixtures and validates all visual-report success/failure paths. |
| `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs` | Adds only manifest assertions required by the visual reporter’s input contract. |
| `docs/LAN-LOBBY-REPORT.md` | Adds the visual report command, report interpretation and latest evidence result. |
| `docs/TEST_PLAN.md` | Adds exact automated visual-diff regression and Player-capture workflow. |

## Task 1: Extract validated evidence/provenance input

**Files:**

- Create: `scripts/LanLobbyEvidence.Common.ps1`
- Create: `scripts/TestLanLobbyEvidenceCommonSmoke.ps1`
- Modify: `scripts/ExportLanLobbyEvidence.ps1`
- Modify: `scripts/TestExportLanLobbyEvidenceSmoke.ps1`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs`

**Interfaces:**

- Produces `Import-LanLobbyEvidenceCommon` and the functions `Assert-LanLobbySafeOutputDirectory`, `Get-LanLobbyCaptureManifest`, `Resolve-LanLobbyReferenceImage`, and `Get-LanLobbySpriteUsage`.
- `Get-LanLobbySpriteUsage` returns objects with `CaptureName`, `SpriteName`, `ResourcesPath`, `SourcePath`, `ImportedSha256`, and `OccurrenceCount`.
- `Resolve-LanLobbyReferenceImage` accepts suffix `9` or `10` and returns one exact `图9.png`/`图10.png` path; it rejects zero or multiple matches before any output write.

- [ ] **Step 1: Write the failing smoke and PlayMode contract tests**

Add a smoke case that builds a five-record manifest with `bg_terrain`, invokes the shared validation path, and asserts its usage row contains the approved source path, `UI/Lobby/bg_terrain`, and a 64-character SHA-256. Define local `Assert-True` and `Assert-FailsWithoutOutput` functions rather than depending on Pester. Add two failure cases against the common validator:

```powershell
Assert-FailsWithoutOutput {
    Assert-LanLobbySafeOutputDirectory -ProjectRoot $projectRoot -OutputDirectory $unsafeOutput
} $unsafeOutput 'safe ignored project directory'

Assert-FailsWithoutOutput {
    Get-LanLobbySpriteUsage -ProjectRoot $projectRoot -Manifest $unmappedManifest -AssetMapPath $assetMapPath
} $unmappedOutput 'Unmapped or non-approved sprite source'
```

In `LanLobbyCaptureSuitePlayModeTests`, assert each of the five records includes nonempty `spriteName` and `sourcePath`, and the `discovered-prefill` record contains room code `654321` while its member array is empty.

- [ ] **Step 2: Run the tests to verify red**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath . -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' -NoGraphics
```

Expected: the common smoke script/functions are absent and the new manifest assertions fail until the input contract is implemented.

- [ ] **Step 3: Implement the shared input contract**

Create the helper with pre-write validation in this exact order: determine this worktree root, normalize output candidate, reject anything outside `Temp/`/`Artifacts/`, load manifest, require exactly the five approved names, verify every capture file exists, parse `ASSET_MAP.md`, validate every manifest sprite, calculate each imported PNG SHA-256, then resolve exact unique references. Keep the exporter’s current `New-Item` strictly after helper success. Do not impose the 1920×1080 visual-diff constraint here: the pre-existing side-by-side exporter’s synthetic smoke fixture intentionally uses a smaller approved PNG.

The mapping parser must expose the intended Resources path:

```powershell
[pscustomobject]@{
  SpriteName = 'bg_terrain'
  ResourcesPath = 'UI/Lobby/bg_terrain'
  SourcePath = '[uc]autochessouter/bg_terrain.png'
  ImportedSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $importedPng).Hash
}
```

Do not read the external source library during report generation; use the already audited imported PNG bytes and source-relative map entry.

- [ ] **Step 4: Run green regressions**

Run the existing exporter smoke, the common smoke, and the focused capture PlayMode test. Verify all success outputs parse and every intended failure leaves its requested output path absent.

- [ ] **Step 5: Commit**

```powershell
git add scripts/LanLobbyEvidence.Common.ps1 scripts/TestLanLobbyEvidenceCommonSmoke.ps1 scripts/ExportLanLobbyEvidence.ps1 scripts/TestExportLanLobbyEvidenceSmoke.ps1 Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs
git commit -m "test: validate LAN lobby visual report inputs"
```

## Task 2: Generate masked visual-diff and asset reports

**Files:**

- Create: `scripts/ExportLanLobbyVisualDiff.ps1`
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1`

**Interfaces:**

- Consumes `Get-LanLobbyCaptureManifest`, `Resolve-LanLobbyReferenceImage`, and `Get-LanLobbySpriteUsage` from Task 1.
- Produces `visual-diff-report.md`, `visual-diff-report.json`, copied `manifest.json`, and `<capture>-actual.png`, `<capture>-reference.png`, `<capture>-overlay.png`, `<capture>-heatmap.png` for all five captures.
- JSON records `actualWidth`, `actualHeight`, `referenceWidth`, `referenceHeight`, `referenceNormalization = "independent-xy"`, `referenceFigure`, `regions`, `maskedPixels`, `attention`, and `assets`.

- [ ] **Step 1: Extend the smoke fixture with pixel and mask assertions**

Generate five 1920×1080 actual bitmaps and exact `图9.png`/`图10.png` reference bitmaps at 2048×1118 using `System.Drawing`. Set a white rectangle only inside a masked region and a red rectangle inside the `create-room` compare region. Assert that:

```powershell
Assert-True (($report.captures | Where-Object name -eq 'home' | Select-Object -ExpandProperty maskedPixels) -gt 0) 'home must record masked pixels'
Assert-True (($home.regions | Where-Object name -eq 'ignored-radar' | Select-Object -ExpandProperty comparedPixels) -eq 0) 'masked radar pixels must not be compared'
Assert-True (($home.regions | Where-Object name -eq 'create-room' | Select-Object -ExpandProperty pixelDifferenceRatio) -gt 0) 'create-room difference must be measurable'
Assert-True (Test-Path (Join-Path $output 'home-heatmap.png')) 'home heatmap must exist'
```

Add a mismatched 1280×720 actual fixture and assert it fails before creating its requested output directory.

- [ ] **Step 2: Run the smoke script to verify red**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
```

Expected: no visual-diff exporter/report/heatmap exists.

- [ ] **Step 3: Implement normalization, regions and output**

Use `System.Drawing.Bitmap` and lock/read pixels in 1920×1080 actual space. Before any `New-Item`, reject every actual capture that is not exactly 1920×1080; this visual-diff-only rule deliberately does not alter the existing side-by-side exporter. Resample each reference to actual width/height with nearest-neighbor or high-quality bilinear interpolation, record native reference dimensions, and never overwrite the original reference.

Use these normalized region names and rectangles (`x`, `y`, `width`, `height`, each in 0..1):

```powershell
$homeRegions = @(
  @{ name='ignored-top-left'; x=0.00; y=0.00; width=0.22; height=0.15; mask=$true },
  @{ name='ignored-radar'; x=0.00; y=0.18; width=0.57; height=0.66; mask=$true },
  @{ name='ignored-bottom-left'; x=0.00; y=0.84; width=0.22; height=0.16; mask=$true },
  @{ name='create-room'; x=0.60; y=0.42; width=0.36; height=0.11; mask=$false },
  @{ name='join-room'; x=0.60; y=0.81; width=0.36; height=0.10; mask=$false },
  @{ name='main-background'; x=0.22; y=0.00; width=0.78; height=1.00; mask=$false }
)
$roomRegions = @(
  @{ name='ignored-top-left'; x=0.00; y=0.00; width=0.22; height=0.15; mask=$true },
  @{ name='ignored-player-art'; x=0.11; y=0.20; width=0.80; height=0.50; mask=$true },
  @{ name='ignored-bottom-right-mode'; x=0.55; y=0.88; width=0.25; height=0.10; mask=$true },
  @{ name='room-background'; x=0.22; y=0.00; width=0.78; height=1.00; mask=$false },
  @{ name='player-card-layout'; x=0.11; y=0.16; width=0.80; height=0.66; mask=$false }
)
```

For masked pixels write transparent black in heatmaps and exclude them from metrics. For unmasked pixels calculate absolute RGB difference, `pixelDifferenceRatio` where any channel differs by more than 24, average absolute RGB error, and a red-to-yellow heatmap intensity. Calculate `attention = $true` when the unmasked ratio exceeds `0.25` or average error exceeds `48`; always exit `0` when input validation succeeded.

For room cards, include the four actual `RoomCard_0..3` rectangles from the capture manifest beside the reference normalized card-layout region; report this as geometry data, not a false claim that unimplemented character art matched.

Write Markdown and JSON from the same ordered object graph. The Markdown asset table must group by `SpriteName` and list all capture names, Resources path, source-relative path, imported SHA-256, and total occurrence count.

- [ ] **Step 4: Run green script tests and inspect generated fixtures**

Run the visual smoke and existing exporter smoke. Use `Get-Content` plus `ConvertFrom-Json` to verify five report capture records, all twenty comparison PNGs, that every emitted asset row has a mapped source path and 64-character SHA-256, and no report output on failure fixtures.

- [ ] **Step 5: Commit**

```powershell
git add scripts/ExportLanLobbyVisualDiff.ps1 scripts/TestLanLobbyVisualDiffSmoke.ps1
git commit -m "feat: report LAN lobby visual differences"
```

## Task 3: Run real evidence and document review workflow

**Files:**

- Modify: `docs/LAN-LOBBY-REPORT.md`
- Modify: `docs/TEST_PLAN.md`

**Interfaces:**

- Consumes Task 2’s script with `-CaptureDirectory`, `-OutputDirectory`, and `-ReferenceDirectory`.
- Produces a real retained ignored evidence folder under `Artifacts/LAN-LOBBY/VisualDiff/` and documented user-facing interpretation.

- [ ] **Step 1: Add a failing command-contract smoke assertion**

Extend `TestLanLobbyVisualDiffSmoke.ps1` to assert the default worktree reference directory is used only when it contains exact figures and that an explicit external reference directory succeeds. The test’s external reference directory must contain exact `图9.png` and `图10.png` fixture files and must never be written by the exporter.

- [ ] **Step 2: Run the smoke test to verify failure**

Run the smoke script before documentation/runtime wiring changes. Expected: the command contract has no explicit-reference success assertion.

- [ ] **Step 3: Document and execute the real pipeline**

Document the command below, including the note that the primary working tree is read-only reference input when the isolated worktree does not contain figures 9/10:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory .\Artifacts\LAN-LOBBY\CapturesFinal `
  -OutputDirectory .\Artifacts\LAN-LOBBY\VisualDiff `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

Rebuild the existing Windows Player with `Task006StandaloneBuild.BuildWindowsX64`, run visible D3D11 Player capture at 1920×1080, then run the command above. Inspect the report: state names, native/normalized dimensions, masked-region note, at least one `ATTENTION` status if visual design differs, and complete asset rows. Do not revise UI styling merely to reduce a report metric in this task.

- [ ] **Step 4: Run final regressions**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath . -TestPlatform EditMode -TestFilter 'ARKnoNIGHTS.Lobby' -NoGraphics
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath . -TestPlatform PlayMode -TestFilter 'ARKnoNIGHTS.Lobby' -NoGraphics
git diff --check
```

Record exact pass counts, build result, evidence output path, and whether Android same-Wi-Fi remains unverified. Inspect final diff to confirm no Player capture PNG, output report, or temporary reference fixture was added to Git.

- [ ] **Step 5: Commit**

```powershell
git add docs/LAN-LOBBY-REPORT.md docs/TEST_PLAN.md scripts/TestLanLobbyVisualDiffSmoke.ps1
git commit -m "test: document LAN lobby visual diff evidence"
```

## Plan Self-Review

- **Spec coverage:** Task 1 implements safe inputs and detailed mapped material provenance; Task 2 implements reference normalization, named exclusions, non-blocking metrics, heatmaps and Markdown/JSON; Task 3 proves the real Player workflow and documents its limits.
- **Placeholder scan:** No task depends on an unnamed helper, unstated output behavior, or a later undefined type; all generated function names, region rectangles, commands, expected failures and commits are specified.
- **Type consistency:** All script tasks use the Task 1 helper function names. Task 2’s `visual-diff-report.json` schema is the report parsed by Task 2/3 smoke checks. Task 3 consumes Task 2’s exact command parameters.
