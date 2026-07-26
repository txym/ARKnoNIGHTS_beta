# Room Select Create Open-Frame Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the incorrect closed Create outline with the Figure-9
open-frame composition whose accepted `创建同盟` bar supplies the lower
boundary.

**Architecture:** Keep the existing runtime-built uGUI hierarchy and approved
asset provenance. Change the visual-evidence contract first, then use TDD to
replace the eleven frame Images with seven measured `doc_frame_line` Images
and resize the sprite-null backing. Validate with at most two real Windows
Player cycles: fixed geometry first, then one pre-authorized brightness-only
cycle if needed.

**Tech Stack:** Unity 2022.3.62f1, C#, uGUI, NUnit/Unity Test Framework,
PowerShell, System.Drawing visual evidence, Windows x64 Player.

## Global Constraints

- Work only in
  `G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby` on `codex/lan-lobby`.
- Do not open the same project path in concurrent Unity processes.
- Do not upgrade Unity, Packages, render pipeline, or Input System.
- Preserve Create action Rect `(1154,453,717,99)` and Join action Rect
  `(1154,876,717,99)` in 1920x1080 screen-top-left pixels.
- Preserve all Create/Join backgrounds, icons, labels, click wiring, sibling
  order, four wings, ten central-decoration values, and LAN behavior.
- Every visible outline Image must use the approved byte-identical
  `Assets/Resources/UI/Lobby/Home/doc_frame_line.png`.
- No `Bottom_*`, `TopRightChamfer`, code-drawn line, mesh, shader border,
  uGUI `Outline`, preset frame, or other outline Sprite is permitted.
- The corrected frame has exactly seven active `doc_frame_line` Images per
  Home state; the two Home states aggregate to fourteen occurrences.
- The dark backing stays a sprite-null, custom-material-null, non-raycast
  uGUI Image and does not cover visible Create-action pixels.
- Use TDD: change the behavior test first, run it and record the intended RED,
  then change production and prove GREEN.
- Generated builds, screenshots, reports, XML, and logs stay under ignored
  `Artifacts/` paths and must not be committed.
- Run at most two new real Player cycles. Cycle 2 may change only the common
  frame tint to the single authorized value. Never start Cycle 3.

---

### Task 1: Redefine visual evidence around the semantic open frame

**Files:**
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1`
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1`

**Interfaces:**
- Consumes: the existing five-state capture manifest, the two action-bar
  reports, and the fixed full Create crop `(1154,224,717,374)`.
- Produces: `createFrame.edges` for top/left/right and both upper joints,
  `createFrame.bottomBoundary` identifying `home-create-action`, complete
  JSON/Markdown/crops on visual failure, and fourteen-frame material usage in
  the smoke fixture.

- [ ] **Step 1: Change the smoke fixture to describe the open frame**

Replace `Fill-CreateFrameFixture` with an open-frame fixture. It must draw:

```powershell
top:
  x=25, y=0, width=666, height=3
left:
  x=25, y=0, width=3, height=236
right:
  x=688, y=0, width=3, height=236
top-left joint:
  x=17, y=0, width=18, height=18
top-right joint:
  x=682, y=0, width=18, height=18
```

The actual fixture has an `8 px` gap in the top span and uses the existing
low-contrast cyan brush for the left edge only. Do not draw any synthetic
cyan bottom line.

Replace the eleven fixture Sprite rows with these seven stable nodes in each
Home state:

```text
CreateFrame/Top_0
CreateFrame/Top_1
CreateFrame/Top_2
CreateFrame/LeftUpper
CreateFrame/LeftLower
CreateFrame/RightUpper
CreateFrame/RightLower
```

Update fixture material assertions from `22` to `14` `doc_frame_line`
occurrences.

- [ ] **Step 2: Add smoke assertions for the new report contract**

Use this literal edge specification in the smoke:

```powershell
$createFrameEdges = @(
  [ordered]@{
    name='top'; axis='x'
    search=@{x=25;y=0;width=666;height=18}
    background=@{x=25;y=26;width=666;height=10}
    minimumCoverage=.90; maximumGap=6; minimumContrast=18
  },
  [ordered]@{
    name='left'; axis='y'
    search=@{x=16;y=0;width=18;height=236}
    background=@{x=42;y=0;width=10;height=236}
    minimumCoverage=.90; maximumGap=6; minimumContrast=18
  },
  [ordered]@{
    name='right'; axis='y'
    search=@{x=682;y=0;width=18;height=236}
    background=@{x=664;y=0;width=10;height=236}
    minimumCoverage=.90; maximumGap=6; minimumContrast=18
  },
  [ordered]@{
    name='top-left-joint'; axis='joint'
    search=@{x=16;y=0;width=28;height=28}
    background=@{x=42;y=30;width=16;height=16}
    minimumPixelCount=80; minimumContrast=18
  },
  [ordered]@{
    name='top-right-joint'; axis='joint'
    search=@{x=673;y=0;width=28;height=28}
    background=@{x=659;y=30;width=16;height=16}
    minimumPixelCount=80; minimumContrast=18
  }
)
```

Assert:

- exactly five semantic records and no `bottom` record;
- top fails only the deliberate `8 px` continuity gap;
- left fails only contrast;
- right and both joints pass;
- `bottomBoundary.kind == 'action-bar'`;
- `bottomBoundary.action == 'home-create-action'`;
- `bottomBoundary.visibleTopScreenY == 460`;
- the smoke's deliberately displaced Create action makes
  `bottomBoundary.passed == false`;
- Markdown calls the section `Home Create open-frame continuity`, lists the
  bottom-boundary action, and contains no `| bottom |` row;
- complete JSON/Markdown and all four frame images are still published.

Name the production break these assertions catch: reintroducing a cyan bottom
edge or treating the transparent action Rect as the visible frame width.

- [ ] **Step 3: Run the smoke and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
```

Expected: non-zero exit because the exporter still reports the old
top/bottom/left/right/chamfer contract, lacks `bottomBoundary`, and reports
twenty-two frame occurrences from the old fixture.

Retain the console output in the Task 1 report.

- [ ] **Step 4: Implement the open-frame exporter contract**

In `ExportLanLobbyVisualDiff.ps1`, replace `$createFrameEdges` with the exact
five records from Step 2.

Treat a record as a joint when it contains `minimumPixelCount`, rather than
checking `axis == 'diagonal'`:

```powershell
$usesPixelCount = $edgeSpec.Contains('minimumPixelCount')
$continuityPassed = if ($usesPixelCount) {
    $actualContinuity.QualifyingPixelCount -ge $edgeSpec.minimumPixelCount
} else {
    $actualContinuity.CoverageRatio -ge $edgeSpec.minimumCoverage -and
        $actualContinuity.LargestGapPixels -le $edgeSpec.maximumGap
}
```

After `$actionBarReports` exists, select exactly one
`home-create-action` report and construct:

```powershell
$createActionReport = @(
    $actionBarReports | Where-Object name -eq 'home-create-action'
)
if ($createActionReport.Count -ne 1) {
    throw 'Expected exactly one home-create-action report for the Create lower boundary.'
}
$createActionReport = $createActionReport[0]
$createActionContentPassed = @(
    $createActionReport.contentVisuals | Where-Object { -not $_.passed }
).Count -eq 0
$createActionRectPassed =
    $createActionReport.positionDeviationPx1920x1080.deltaX -eq 0 -and
    $createActionReport.positionDeviationPx1920x1080.deltaY -eq 0 -and
    $createActionReport.sizeDeviationPxAfterLocalReferenceResize.deltaWidth -eq 0 -and
    $createActionReport.sizeDeviationPxAfterLocalReferenceResize.deltaHeight -eq 0
$bottomBoundary = [pscustomobject][ordered]@{
    kind = 'action-bar'
    action = 'home-create-action'
    visibleTopScreenY = 460
    frameLocalY = 212
    positionDeviationPx1920x1080 =
        $createActionReport.positionDeviationPx1920x1080
    sizeDeviationPxAfterLocalReferenceResize =
        $createActionReport.sizeDeviationPxAfterLocalReferenceResize
    contentPassed = $createActionContentPassed
    passed = $createActionRectPassed -and $createActionContentPassed
}
```

Add `bottomBoundary` to `createFrame` and require it in the overall frame
pass:

```powershell
passed =
    @($edges | Where-Object { -not $_.passed }).Count -eq 0 -and
    $bottomBoundary.passed
```

Change the Markdown heading and add a concise bottom-boundary row with action
name, visible top, deviations, content state, and pass state.

- [ ] **Step 5: Run evidence smokes and verify GREEN**

Run serially:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

Expected: all print `PASS` and exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add -- `
  'scripts/TestLanLobbyVisualDiffSmoke.ps1' `
  'scripts/ExportLanLobbyVisualDiff.ps1'
git commit -m 'fix: measure semantic create open frame'
```

---

### Task 2: Replace the closed runtime frame with seven measured Sprites

**Files:**
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`

**Interfaces:**
- Consumes: the accepted Create action, `doc_frame_line` Resources Sprite, and
  the fixed Create-local visible-boundary coordinates from the design.
- Produces: a seven-Image `CreateFrame`, corrected `InteriorBacking`, and
  fourteen frame-source occurrences across `home` and
  `discovered-prefill`.

- [ ] **Step 1: Change the View test before production**

In `LanLobbyViewPlayModeTests`, replace the eleven expectations with:

```csharp
var frameExpected = new[]
{
    new OrientedDecorationExpectation(
        "CreateFrame/Top_0", "doc_frame_line",
        263.667f, -23f, 237.171f, 12f, 0f, false),
    new OrientedDecorationExpectation(
        "CreateFrame/Top_1", "doc_frame_line",
        480f, -23f, 237.171f, 12f, 0f, false),
    new OrientedDecorationExpectation(
        "CreateFrame/Top_2", "doc_frame_line",
        696.333f, -23f, 237.171f, 12f, 0f, false),
    new OrientedDecorationExpectation(
        "CreateFrame/LeftUpper", "doc_frame_line",
        147f, 40f, 128.053f, 12f, 90f, false),
    new OrientedDecorationExpectation(
        "CreateFrame/LeftLower", "doc_frame_line",
        147f, 149f, 128.053f, 12f, 90f, false),
    new OrientedDecorationExpectation(
        "CreateFrame/RightUpper", "doc_frame_line",
        813f, 40f, 128.053f, 12f, 270f, false),
    new OrientedDecorationExpectation(
        "CreateFrame/RightLower", "doc_frame_line",
        813f, 149f, 128.053f, 12f, 270f, false)
};
```

Change the backing assertion to:

```csharp
AssertTopLeftRect(backingRect, 147f, -12f, 666f, 224f, .05f);
```

Require:

```csharp
Assert.That(frameImages, Has.Length.EqualTo(7));
Assert.That(createFrame.Find("Bottom_0"), Is.Null);
Assert.That(createFrame.Find("Bottom_1"), Is.Null);
Assert.That(createFrame.Find("Bottom_2"), Is.Null);
Assert.That(createFrame.Find("TopRightChamfer"), Is.Null);
```

Keep the first-cycle tint at:

```csharp
new Color(.42f, .82f, .76f, .72f)
```

Use the literal `304f / 309f` visible ratio to assert:

```text
top visible left endpoint:   147 ±0.1
top visible right endpoint:  813 ±0.1
top adjacent overlaps:       17 ±0.1
side visible top endpoint:   -23 ±0.1
side visible bottom endpoint:212 ±0.1
side adjacent overlaps:      17 ±0.1
```

This test catches a production change that widens the frame to transparent
action bounds or lets cyan pixels continue beside/below the visible bar.

- [ ] **Step 2: Change the Capture test before production**

Change per-Home `doc_frame_line` count from `11` to `7`, aggregate count from
`22` to `14`, and the final Home assertion from `11` to `7`.

Keep four `img_pointer`, zero `room_select_create_logo`, and all source-path
checks unchanged.

- [ ] **Step 3: Run View and Capture tests and verify RED**

Run sequentially:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Red\View' `
  -TimeoutSeconds 900

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Red\Capture' `
  -TimeoutSeconds 900
```

Expected: both have complete XML and fail only because runtime still has the
old eleven-segment geometry/count and old backing Rect. A missing/incomplete
XML is unverified, not RED.

- [ ] **Step 4: Implement the measured runtime values**

In `LanLobbyView.BuildCreateSection`:

```csharp
CreateSolidDecorationPanel(
    "InteriorBacking", parent,
    147f, -12f, 666f, 224f,
    new Color(0f, 0f, 0f, .78f));

var createFrame = Rect("CreateFrame", parent);
Stretch(createFrame);
var frameTint = new Color(.42f, .82f, .76f, .72f);
CreateOrientedDecorationSprite(
    "Top_0", createFrame, "Home/doc_frame_line",
    263.667f, -23f, 237.171f, 12f, 0f, false, frameTint);
CreateOrientedDecorationSprite(
    "Top_1", createFrame, "Home/doc_frame_line",
    480f, -23f, 237.171f, 12f, 0f, false, frameTint);
CreateOrientedDecorationSprite(
    "Top_2", createFrame, "Home/doc_frame_line",
    696.333f, -23f, 237.171f, 12f, 0f, false, frameTint);
CreateOrientedDecorationSprite(
    "LeftUpper", createFrame, "Home/doc_frame_line",
    147f, 40f, 128.053f, 12f, 90f, false, frameTint);
CreateOrientedDecorationSprite(
    "LeftLower", createFrame, "Home/doc_frame_line",
    147f, 149f, 128.053f, 12f, 90f, false, frameTint);
CreateOrientedDecorationSprite(
    "RightUpper", createFrame, "Home/doc_frame_line",
    813f, 40f, 128.053f, 12f, 270f, false, frameTint);
CreateOrientedDecorationSprite(
    "RightLower", createFrame, "Home/doc_frame_line",
    813f, 149f, 128.053f, 12f, 270f, false, frameTint);
```

Delete the three `Bottom_*` calls and `TopRightChamfer`. Do not modify any
following wing, central-decoration, or action code.

- [ ] **Step 5: Run focused fixtures and verify GREEN**

Run this loop sequentially:

```powershell
$runs = @(
  @{Leaf='Layout';Platform='EditMode';Filter='ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests'},
  @{Leaf='View';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests'},
  @{Leaf='Capture';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests'},
  @{Leaf='Controller';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests'}
)
foreach ($run in $runs) {
  powershell -NoProfile -ExecutionPolicy Bypass `
    -File .\scripts\Invoke-UnityTests.ps1 `
    -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
    -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
    -TestPlatform $run.Platform `
    -TestFilter $run.Filter `
    -OutputDirectory (
      Join-Path 'Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Green'
        $run.Leaf
    ) `
    -TimeoutSeconds 900
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Require complete XML, zero failed, and zero skipped. Expected current totals:
Layout `6`, View `14`, Capture `3`, Controller `3`.

- [ ] **Step 6: Commit**

```powershell
git add -- `
  'Assets/Game/Runtime/Lobby/LanLobbyView.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs'
git commit -m 'fix: use create action as frame boundary'
```

---

### Task 3: Calibrate with at most two real Player cycles

**Files:**
- Modify only if Cycle 1 needs the authorized tint:
  `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- Modify only if Cycle 1 needs the authorized tint:
  `Assets/Game/Runtime/Lobby/LanLobbyView.cs`

**Interfaces:**
- Consumes: Task 1 open-frame report, Task 2 seven-segment hierarchy,
  `Task006StandaloneBuild.BuildWindowsX64`, and `LanLobbyCaptureSuite`.
- Produces: one or two numbered builds/captures/reports under
  `Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\`, a final accepted result or
  an explicit two-cycle failure, and frozen frame tint.

- [ ] **Step 1: Build Cycle 1**

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT =
  'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\WindowsStandalone-1\ARKnoNIGHTS.exe'
$arguments =
  '-batchmode -accept-apiupdate ' +
  '-projectPath "G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby" ' +
  '-executeMethod Task006StandaloneBuild.BuildWindowsX64 ' +
  '-logFile "G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\WindowsStandaloneBuild-1.log"'
$process = Start-Process `
  -FilePath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ArgumentList $arguments `
  -PassThru -Wait -WindowStyle Hidden
if ($process.ExitCode -ne 0) { exit $process.ExitCode }
```

Require `BuildResult Succeeded`, errors `0`, a recorded warning count, and a
non-empty executable.

- [ ] **Step 2: Capture Cycle 1 visibly**

```powershell
$arguments =
  '-force-d3d11 -screen-width 1920 -screen-height 1080 ' +
  '-lanLobbyCaptureSuite ' +
  '-lanLobbyCaptureOutput "G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Captures-1" ' +
  '-logFile "G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\PlayerCapture-1.log"'
$process = Start-Process `
  -FilePath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\WindowsStandalone-1\ARKnoNIGHTS.exe' `
  -ArgumentList $arguments `
  -PassThru -Wait
if ($process.ExitCode -ne 0) { exit $process.ExitCode }
```

Require `[LanLobby][capture.completed] count=5`, a parseable manifest, and
five non-empty decodable `1920x1080` PNGs.

- [ ] **Step 3: Export and inspect Cycle 1**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Captures-1' `
  -OutputDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\VisualDiff-1' `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

Inspect in this exact order:

1. Create/Join position and size deviations are zero.
2. Create/Join icon and label rows pass.
3. `bottomBoundary` names `home-create-action` and passes.
4. No `bottom` edge exists.
5. Top/left/right continuity and contrast rows.
6. Both upper-joint rows.
7. Full `home.png`.
8. Frame actual/reference/overlay/heatmap.
9. Manifest has seven frame Sprites per Home, four frozen wings, zero logo,
   and only the allowed sprite-null backing as Create code-native geometry.

Record the ten central rows as informational only.

- [ ] **Step 4: Stop on acceptance or apply the only authorized tint**

If Cycle 1 passes every frame gate and manual inspection, skip to Step 7.

If geometry/semantic gates pass but contrast or manual visibility fails,
change the View test tint first to:

```csharp
new Color(.55f, .95f, .88f, 1f)
```

Run the View fixture to obtain a complete XML with exactly the intended color
assertion failure. Then change only the runtime `frameTint` to the same value
and rerun View to GREEN.

Do not change geometry, backing, action, wing, central, or LAN values in
Cycle 2.

- [ ] **Step 5: Build, capture, and export Cycle 2 when needed**

Run the second build:

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT =
  'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\WindowsStandalone-2\ARKnoNIGHTS.exe'
$arguments =
  '-batchmode -accept-apiupdate ' +
  '-projectPath "G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby" ' +
  '-executeMethod Task006StandaloneBuild.BuildWindowsX64 ' +
  '-logFile "G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\WindowsStandaloneBuild-2.log"'
$process = Start-Process `
  -FilePath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ArgumentList $arguments `
  -PassThru -Wait -WindowStyle Hidden
if ($process.ExitCode -ne 0) { exit $process.ExitCode }
```

Run the second visible capture:

```powershell
$arguments =
  '-force-d3d11 -screen-width 1920 -screen-height 1080 ' +
  '-lanLobbyCaptureSuite ' +
  '-lanLobbyCaptureOutput "G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Captures-2" ' +
  '-logFile "G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\PlayerCapture-2.log"'
$process = Start-Process `
  -FilePath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\WindowsStandalone-2\ARKnoNIGHTS.exe' `
  -ArgumentList $arguments `
  -PassThru -Wait
if ($process.ExitCode -ne 0) { exit $process.ExitCode }
```

Export the second report:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Captures-2' `
  -OutputDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\VisualDiff-2' `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

Require the same explicit build, Player, PNG, action, bottom-boundary,
open-frame, provenance, and manual-image gates listed in Steps 1–3.
Stop after Cycle 2 whether it passes or fails. Never create Cycle 3.

- [ ] **Step 6: Commit the tint only when Cycle 2 was needed**

```powershell
git add -- `
  'Assets/Game/Runtime/Lobby/LanLobbyView.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs'
git commit -m 'fix: raise open frame visibility'
```

If Cycle 1 passed, record `commit: NONE`; do not create an empty commit.

- [ ] **Step 7: Run final focused and evidence verification**

Run:

```powershell
$runs = @(
  @{Leaf='Layout';Platform='EditMode';Filter='ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests'},
  @{Leaf='View';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests'},
  @{Leaf='Capture';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests'},
  @{Leaf='Controller';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests'}
)
foreach ($run in $runs) {
  powershell -NoProfile -ExecutionPolicy Bypass `
    -File .\scripts\Invoke-UnityTests.ps1 `
    -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
    -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
    -TestPlatform $run.Platform `
    -TestFilter $run.Filter `
    -OutputDirectory (
      Join-Path 'Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Verification-Final'
        $run.Leaf
    ) `
    -TimeoutSeconds 900
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

Require complete focused XML, zero failed/skipped, and three smoke exits `0`.

---

### Task 4: Record the authoritative contract and final evidence

**Files:**
- Modify: `docs/SPEC.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/LAN-LOBBY-REPORT.md`

**Interfaces:**
- Consumes: the accepted/final Task 3 capture, build log, manifest, visual
  report, focused XML, and smoke output.
- Produces: the authoritative user-visible open-frame rule, exact retained
  evidence paths/results, asset occurrence/provenance data, and explicit
  unresolved visual risk if two cycles did not pass.

- [ ] **Step 1: Update `docs/SPEC.md`**

Add a concise LAN Home UI visual-contract subsection stating:

- `创建同盟` visible bar is the Create region's lower boundary;
- cyan `doc_frame_line` exists only on top/left/right of the upper region;
- no cyan line or dark backing may continue beside/below visible bar pixels;
- Create/Join action behavior and LAN room behavior are unchanged.

- [ ] **Step 2: Update `docs/TEST_PLAN.md`**

Add a current authoritative section with:

- seven Sprite names and fourteen Home-state occurrences;
- backing Rect/color/material/raycast contract;
- open-frame report edge and bottom-boundary fields;
- exact focused XML paths and totals;
- build result/errors/warnings;
- Player exit/completion and five PNG checks;
- final visual metrics and acceptance result;
- three evidence-smoke results;
- old closed-frame bottom gate marked superseded.

- [ ] **Step 3: Update `docs/LAN-LOBBY-REPORT.md`**

Record:

- final commit(s);
- exact final screenshot/report directories;
- Create/Join zero movement and content results;
- top/left/right/joint metrics;
- whether Cycle 1 or Cycle 2 was final;
- `doc_frame_line`, `img_pointer`, and logo counts;
- approved source path and SHA-256;
- all tests/build/capture/smoke results;
- any unverified or failed acceptance item without hiding it.

- [ ] **Step 4: Verify documentation facts against retained artifacts**

Read the final XML, build log, Player log, capture manifest, JSON report, and
Markdown report directly. Do not copy historical values from an older report.

Run:

```powershell
git diff --check
git status --short
git ls-files 'Artifacts/*' 'Temp/*'
```

Require no whitespace errors, no generated evidence tracked, and no unrelated
files.

- [ ] **Step 5: Commit**

```powershell
git add -- `
  'docs/SPEC.md' `
  'docs/TEST_PLAN.md' `
  'docs/LAN-LOBBY-REPORT.md'
git commit -m 'docs: record create open frame evidence'
```

---

### Task 5: Final scoped review and handoff

**Files:**
- Read only: all changes from `60eb9b1` through final `HEAD`
- Read only: final retained artifacts from Task 3

**Interfaces:**
- Consumes: all Task 1–4 commits and evidence.
- Produces: an independent Critical/Important/Minor review, final completion
  disposition, and precise manual screenshot handoff.

- [ ] **Step 1: Review the complete scoped diff**

Check:

- no old `Bottom_*` or `TopRightChamfer` runtime/test/fixture expectation;
- seven exact frame nodes and fourteen aggregate occurrences;
- no frame visible span below Create-local `y=212`;
- backing ends at `y=212`;
- action, wing, central, and LAN values unchanged;
- exporter no longer treats cyan bottom as acceptance;
- docs cite only retained artifacts and exact metrics.

- [ ] **Step 2: Reconcile all final evidence**

Require:

- focused tests complete with zero failed/skipped;
- successful build and Player exit;
- five valid screenshots;
- three evidence smokes pass;
- final action rows pass;
- final frame result is reported honestly;
- clean tracked worktree and no generated artifacts staged.

- [ ] **Step 3: Deliver the final screenshot and report paths**

Provide clickable absolute paths to:

```text
If Cycle 1 is final:
Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Captures-1\home.png
Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\VisualDiff-1\visual-diff-report.md
Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\VisualDiff-1\home-create-frame-actual.png
Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\VisualDiff-1\home-create-frame-overlay.png

If Cycle 2 is final:
Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\Captures-2\home.png
Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\VisualDiff-2\visual-diff-report.md
Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\VisualDiff-2\home-create-frame-actual.png
Artifacts\LAN-LOBBY\CreateOpenFrameCorrection\VisualDiff-2\home-create-frame-overlay.png
```

State explicitly whether the open frame passed or stopped after two cycles.
