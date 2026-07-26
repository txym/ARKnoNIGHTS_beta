# Room Select Create Frame Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Figure 9 Create UI by adding a dark interior backing and producing a continuous, clearly visible `doc_frame_line`-only outline without changing the frozen wings, central decoration, action bars, or LAN behavior.

**Architecture:** First make the visual exporter frame-focused and non-fatal on visual mismatches, including a quantitative luma-contrast gate. Then replace the current nine-transform frame with an eleven-segment visible-alpha-based starting layout and a textureless backing. Calibrate only the backing/frame through at most three real Windows Player cycles, then record the accepted evidence.

**Tech Stack:** Unity 2022.3.62f1c1, C# uGUI, NUnit EditMode/PlayMode tests, PowerShell 5.1, `System.Drawing`, Windows x64 Player capture.

## Global Constraints

- Work only in `G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby` on branch `codex/lan-lobby`.
- Use Unity Editor `D:\2022.3.62f1c1\Editor\Unity.exe`; do not upgrade Unity or add/change Packages.
- Every visible outline Image uses `Assets/Resources/UI/Lobby/Home/doc_frame_line.png`, sourced from the approved non-`$0`, non-`#0` autochess material and recorded in the Lobby asset map.
- The outline contains 9 to 16 real `doc_frame_line` Images; no code-drawn line, procedural mesh, `Outline`, border shader/material, or preset frame is allowed.
- `InteriorBacking` is a plain uGUI Image with `sprite == null`, no material/outline, a uniform black color, and `raycastTarget == false`.
- Keep the four current `img_pointer` instances and all central Create decoration transforms/tints byte-for-byte unchanged unless a test-only symbol reference must be renamed.
- Keep the accepted Create/Join action Rects, backgrounds, icons, labels, sibling order, and interaction unchanged.
- Keep LAN discovery, room number, host/join transport, room state, and disconnect behavior unchanged.
- `room_select_create_logo` remains absent from active runtime rendering.
- Do not modify or commit `Library/`, `Temp/`, `Logs/`, `obj/`, `Artifacts/`, `Build/`, or `Builds/`.
- Run Unity processes sequentially; never open the same project path from two Editor/batchmode processes.
- Preserve existing `.meta` files and GUIDs.
- Real calibration artifacts for this plan live only under `Artifacts\LAN-LOBBY\CreateFrameCompletion\`.
- At most three new materially different real Player capture cycles are authorized. Never start a fourth cycle.

---

### Task 1: Make frame evidence complete, contrast-aware, and frame-focused

**Files:**
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1`
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1`

**Interfaces:**
- Consumes: the existing `home.png`, capture manifest, Figure 9, action-bar evidence, 717x374 Create-frame crop, and existing cyan continuity helper.
- Produces: frame continuity plus luma contrast for five edge rows, a non-blocking ten-row central diagnostic, no wing measurement rows, and complete JSON/Markdown/images even when a visual row fails.

- [ ] **Step 1: Replace the smoke fixture's wing gate with frame-only negative cases**

In `scripts/TestLanLobbyVisualDiffSmoke.ps1`:

- remove `wing-left` and `wing-right` expected measurement rows;
- retain the ten central rows only as informational diagnostics;
- keep four synthetic `img_pointer` nodes per Home state for material evidence, but add no wing visual assertion;
- create eleven synthetic `doc_frame_line` nodes per Home state and assert aggregate occurrence `22`;
- draw a bright frame with an `8 px` internal gap in the top edge;
- draw a continuous bottom edge using the qualifying low-contrast color `ARGB(255,8,16,15)`;
- draw the remaining three frame records with bright cyan;
- assert the exporter still publishes JSON, Markdown, and all four frame PNGs.

Use these frame/background ROIs verbatim:

```powershell
$createFrameEdges = @(
  [ordered]@{
    name='top'; axis='x'
    search=@{x=0;y=0;width=690;height=18}
    background=@{x=0;y=26;width=690;height=10}
    minimumCoverage=.90; maximumGap=6; minimumContrast=18
  },
  [ordered]@{
    name='bottom'; axis='x'
    search=@{x=0;y=356;width=717;height=18}
    background=@{x=0;y=338;width=717;height=10}
    minimumCoverage=.90; maximumGap=6; minimumContrast=18
  },
  [ordered]@{
    name='left'; axis='y'
    search=@{x=0;y=0;width=18;height=374}
    background=@{x=26;y=0;width=10;height=374}
    minimumCoverage=.90; maximumGap=6; minimumContrast=18
  },
  [ordered]@{
    name='right'; axis='y'
    search=@{x=699;y=18;width=18;height=356}
    background=@{x=681;y=18;width=10;height=356}
    minimumCoverage=.90; maximumGap=6; minimumContrast=18
  },
  [ordered]@{
    name='top-right-chamfer'; axis='diagonal'
    search=@{x=680;y=0;width=37;height=37}
    background=@{x=656;y=20;width=16;height=16}
    minimumPixelCount=80; minimumContrast=18
  }
)
```

The smoke assertions must independently prove:

```text
top: continuityPassed=false, contrastPassed=true, passed=false
bottom: continuityPassed=true, contrastPassed=false, passed=false
left/right/chamfer: passed=true
createFrame.passed=false
report and all requested images exist
action Rect/content assertions retain their previous expected values
createDecoration.components has exactly ten rows and no name beginning with "wing-"
```

- [ ] **Step 2: Run the visual smoke and prove RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
```

Expected: exit non-zero because the exporter still emits wing rows, has no contrast fields, or aborts before publishing the complete frame report.

- [ ] **Step 3: Add the luma contrast measurement helper**

Inside the existing C# `LanLobbyVisualDiff` helper in
`scripts/ExportLanLobbyVisualDiff.ps1`, add:

```csharp
public sealed class FrameContrast
{
    public int FrameSampleCount { get; set; }
    public int BackgroundSampleCount { get; set; }
    public double FrameMedianLuma { get; set; }
    public double BackgroundMedianLuma { get; set; }
    public double ContrastDelta { get; set; }
    public bool Available { get; set; }
    public string FailureReason { get; set; }
}

public static FrameContrast MeasureFrameLumaContrast(
    Bitmap source,
    Rectangle frameSearch,
    Rectangle backgroundSearch,
    int minimumGreen,
    int minimumGreenOverRed,
    int minimumBlueOverRed)
```

Implementation rules:

```text
Validate both rectangles with the existing ValidateSearch.
Frame samples contain only pixels accepted by IsCyan.
Background samples contain every non-transparent pixel in backgroundSearch.
Luma = 0.2126*R + 0.7152*G + 0.0722*B.
Median sorts an independent List<double>; even counts average the two middle values.
No qualifying frame sample or no background sample returns Available=false and a stable FailureReason.
Measurement absence never throws after valid rectangle/input validation.
```

- [ ] **Step 4: Remove wing rows and make central rows informational/non-fatal**

Change `$createDecorationContentSpecs` to contain exactly the ten current
non-wing rows. Preserve their expected bounds and cyan thresholds.

Wrap each actual/reference central measurement so
`InvalidOperationException` becomes a component record with:

```powershell
measurementAvailable = $false
measurementError = $_.Exception.Message
actualBounds = $null
centerDeviationPx = $null
sizeDeviationPx = $null
passed = $false
```

Add `acceptanceRole = 'informational'` and
`blocksCreateFrameAcceptance = $false` to `createDecoration`. A central row
failure must not abort the exporter or change `createFrame.passed`.

- [ ] **Step 5: Add continuity/contrast fields to each frame row**

For each `$createFrameEdges` entry:

```powershell
$contrast = [LanLobbyVisualDiff]::MeasureFrameLumaContrast(
    $actualCrop, $background, 12, 3, 2)
$contrastPassed = $contrast.Available -and
    $contrast.ContrastDelta -ge $edgeSpec.minimumContrast
```

Straight edges use:

```powershell
$continuityPassed =
    $actualContinuity.CoverageRatio -ge $edgeSpec.minimumCoverage -and
    $actualContinuity.LargestGapPixels -le $edgeSpec.maximumGap
```

The chamfer uses:

```powershell
$continuityPassed =
    $actualContinuity.QualifyingPixelCount -ge $edgeSpec.minimumPixelCount
```

Every row reports:

```text
backgroundSearch
minimumContrast
frameSampleCount
backgroundSampleCount
frameMedianLuma
backgroundMedianLuma
contrastDelta
contrastAvailable
contrastFailureReason
continuityPassed
contrastPassed
passed = continuityPassed && contrastPassed
```

`createFrame.passed` remains true only when all five rows pass.

- [ ] **Step 6: Update Markdown without changing action-bar fields**

The frame Markdown table must include:

```text
Edge
Search/background
Reference pixels/coverage/gap
Actual pixels/coverage/gap
Frame/background median luma
Contrast delta/minimum
Continuity passed
Contrast passed
Passed
```

Do not rename, remove, or recalculate existing action-bar Rect/content fields.

- [ ] **Step 7: Run all evidence smokes**

Run sequentially:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Expected: all three scripts exit `0`. The main visual smoke contains the
deliberate top-continuity and bottom-contrast failures inside a successfully
published report.

- [ ] **Step 8: Commit the evidence slice**

```powershell
git add -- 'scripts/TestLanLobbyVisualDiffSmoke.ps1' 'scripts/ExportLanLobbyVisualDiff.ps1'
git commit -m 'test: focus create evidence on visible frame'
```

---

### Task 2: Add the dark backing and eleven-segment starting frame

**Files:**
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs`

**Interfaces:**
- Consumes: `Home/doc_frame_line`, the existing center-pivot decoration helper, fixed Create parent coordinates, and Task 1's expected manifest occurrence.
- Produces: `InteriorBacking`, eleven named `doc_frame_line` Images with measured overlaps, unchanged wings/central/action nodes, and capture evidence with eleven frame occurrences per Home state.

- [ ] **Step 1: Write the View hierarchy/geometry test first**

Extend the Create hierarchy test in
`LanLobbyViewPlayModeTests.cs` with this literal backing expectation:

```text
name: InteriorBacking
sprite: null
top-left anchor/pivot: (0,1)
top-left position: (128,-12)
size: (705,217)
color: RGBA(0,0,0,0.78)
raycastTarget: false
sibling: before CreateFrame
```

Use this exact initial frame table:

```text
Top_0             center=(252,-18) size=(260,12) rotation=0
Top_1             center=(491,-18) size=(260,12) rotation=0
Top_2             center=(730,-18) size=(260,12) rotation=0
Bottom_0          center=(252,344) size=(260,12) rotation=180
Bottom_1          center=(491,344) size=(260,12) rotation=180
Bottom_2          center=(730,344) size=(260,12) rotation=180
LeftUpper         center=(128,76)  size=(200,12) rotation=90
LeftLower         center=(128,253) size=(200,12) rotation=90
RightUpper        center=(833,76)  size=(200,12) rotation=270
RightLower        center=(833,253) size=(200,12) rotation=270
TopRightChamfer   center=(826,-10) size=(40,12)  rotation=45
```

Every frame row expects:

```text
sprite.name = doc_frame_line
preserveAspect = false
color = RGBA(0.42,0.82,0.76,0.62)
raycastTarget = false
top-left anchors and center pivot
```

Assert exactly eleven active Images under `CreateFrame`. Compute long-axis
visible span with the hand-derived constant `304f / 309f` and assert:

```text
Top/Bottom adjacent overlap: 260*304/309 - 239, within 10..24
Left/Right adjacent overlap: 200*304/309 - 177, within 10..24
cross-axis thickness: 12, within 8..14
```

- [ ] **Step 2: Freeze wings, central decoration, and action values in the same RED test**

Retain the existing literal expectations for all four wings and ten central
nodes. Add a mutation guard proving the new implementation cannot silently
move them:

```text
four img_pointer Images only
wing centers: (330,86), (330,147), (603,86), (603,147)
wing widths: 108
wing height: 108*23/324
wing rotations: 162,198,18,342
wing tint: RGBA(0.35,0.65,0.58,0.45)
```

Retain the existing Create/Join action geometry/content/click assertions
unchanged.

- [ ] **Step 3: Update Capture expectations before production code**

In `LanLobbyCaptureSuitePlayModeTests.cs`, require per Home state:

```text
doc_frame_line occurrences: 11
img_pointer occurrences: 4
room_select_create_logo occurrences: 0
codeNativeGeometry name PanelFrame: absent
InteriorBacking code-native Image: present with spriteName null, raycast false
CreateAction remains after every decoration Graphic
```

Across `home` plus `discovered-prefill`, material aggregation must report:

```text
doc_frame_line: 22
img_pointer: 8
room_select_create_logo: 0
```

- [ ] **Step 4: Run View and Capture tests and prove RED**

Run sequentially:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\CreateFrameCompletion\Red-View' `
  -TimeoutSeconds 900

powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\CreateFrameCompletion\Red-Capture' `
  -TimeoutSeconds 900
```

Expected: non-zero with complete XML. View fails because `InteriorBacking` and
the eleven-segment table do not exist; Capture fails because the current
frame occurrence is nine.

- [ ] **Step 5: Add a solid-panel helper**

Add this helper beside `CreateOrientedDecorationSprite`:

```csharp
private static Image CreateSolidDecorationPanel(
    string name,
    Transform parent,
    float left,
    float top,
    float width,
    float height,
    Color color)
{
    var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    value.transform.SetParent(parent, false);
    var image = value.GetComponent<Image>();
    image.sprite = null;
    image.material = null;
    image.color = color;
    image.raycastTarget = false;
    var rect = image.rectTransform;
    rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
    rect.pivot = new Vector2(0f, 1f);
    rect.anchoredPosition = new Vector2(left, -top);
    rect.sizeDelta = new Vector2(width, height);
    return image;
}
```

- [ ] **Step 6: Build the backing and frame before frozen content**

At the start of `BuildCreateSection`:

```csharp
CreateSolidDecorationPanel(
    "InteriorBacking", parent,
    128f, -12f, 705f, 217f,
    new Color(0f, 0f, 0f, .78f));

var createFrame = Rect("CreateFrame", parent);
Stretch(createFrame);
var frameTint = new Color(.42f, .82f, .76f, .62f);
```

Create the eleven rows from Step 1 with
`CreateOrientedDecorationSprite`. Do not modify any wing, central-decoration,
or action line below the frame.

- [ ] **Step 7: Run focused Layout/View/Capture/Controller verification**

Run sequentially into
`Artifacts\LAN-LOBBY\CreateFrameCompletion\Green-Structure\`:

```powershell
$runs = @(
  @{Leaf='Layout';Platform='EditMode';Filter='ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests'},
  @{Leaf='View';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests'},
  @{Leaf='Capture';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests'},
  @{Leaf='Controller';Platform='PlayMode';Filter='ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests'}
)
foreach ($run in $runs) {
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
    -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
    -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
    -TestPlatform $run.Platform `
    -TestFilter $run.Filter `
    -OutputDirectory (Join-Path 'Artifacts\LAN-LOBBY\CreateFrameCompletion\Green-Structure' $run.Leaf) `
    -TimeoutSeconds 900
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Require complete non-zero XML, zero failed, zero skipped, and no compilation
error.

- [ ] **Step 8: Commit the starting runtime frame**

```powershell
git add -- `
  'Assets/Game/Runtime/Lobby/LanLobbyView.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs'
git commit -m 'fix: rebuild visible create frame'
```

---

### Task 3: Calibrate the real frame in at most three Player cycles

**Files:**
- Modify only after measurement: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
- Modify with the same final literals: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- Modify only if occurrence count changes within 9..16: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs`

**Interfaces:**
- Consumes: Task 1 frame report, Task 2 starting hierarchy, `Task006StandaloneBuild.BuildWindowsX64`, and `LanLobbyCaptureSuite`.
- Produces: up to three numbered real build/capture/report cycles, a visually accepted frame or an explicit three-cycle failure, and final frozen frame literals.

- [ ] **Step 1: Run the four focused fixtures before cycle 1**

Use the same four-filter loop from Task 2 with output root:

```text
Artifacts\LAN-LOBBY\CreateFrameCompletion\Verification-1\
```

Require Layout `6/6`, View `14/14`, Capture `3/3`, and Controller `3/3` unless
the repository test inventory has intentionally increased. Every XML must be
complete and have zero failed/skipped.

- [ ] **Step 2: Build the cycle-1 Windows Player**

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT =
  'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateFrameCompletion\WindowsStandalone-1\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -accept-apiupdate `
  -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateFrameCompletion\WindowsStandaloneBuild-1.log'
```

Require waited process exit `0`, `BuildResult Succeeded`, errors `0`, and a
non-empty executable. Record warning count.

- [ ] **Step 3: Capture five cycle-1 states**

Run the Player visibly and wait for completion:

```powershell
& 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateFrameCompletion\WindowsStandalone-1\ARKnoNIGHTS.exe' `
  -force-d3d11 -screen-width 1920 -screen-height 1080 `
  -lanLobbyCaptureSuite `
  -lanLobbyCaptureOutput 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateFrameCompletion\Captures-1' `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateFrameCompletion\PlayerCapture-1.log'
```

Require exit `0`, `[LanLobby][capture.completed] count=5`, a parseable
manifest, and exactly five non-empty decodable 1920x1080 PNGs.

- [ ] **Step 4: Publish and inspect `VisualDiff-1`**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateFrameCompletion\Captures-1' `
  -OutputDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateFrameCompletion\VisualDiff-1' `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

Inspect in this order:

1. both action Rects have zero movement;
2. Create/Join icon and label rows all pass;
3. all five frame continuity rows;
4. all five contrast rows;
5. full `home.png`;
6. frame actual/reference/overlay/heatmap.

Record the ten informational central rows without using them to reject the
frame.

- [ ] **Step 5: Apply one measurement-driven correction**

Use only these correction rules:

```text
Leading/trailing gap > 6:
  shift the nearest end segment toward the gap by the measured gap minus 3 px,
  rounded to 0.5 design px.

Internal gap > 6:
  extend the two adjacent long axes equally until their visible-alpha spans
  overlap by 17 design px; visible long span = width * 304 / 309.

Overlap hotspot or shared overlap > 24:
  shorten adjacent long axes equally until visible overlap is 17 design px.

Coverage < 0.90 with every gap <= 6:
  increase the affected segment cross-axis thickness by 1 design px, capped at 14.

Contrast < 18 with continuity passing:
  increase common frame alpha by 0.05, capped at 0.75.

Frame visually bright but backing contours remain distracting:
  increase backing alpha by 0.04, capped at 0.84.

Frame visually too heavy after passing contrast:
  reduce common frame alpha by 0.03 without going below 0.50 or contrast 18.
```

Change one category per cycle: geometry/thickness, frame alpha, or backing
alpha. Copy every changed literal to the View test before production code,
run the View test to prove RED for the intended literal, then update runtime
and prove GREEN.

Do not modify wings, central decoration, action bars, or LAN code.

- [ ] **Step 6: Repeat for cycles 2 and 3 only when required**

For cycle `N` use:

```text
WindowsStandalone-N
WindowsStandaloneBuild-N.log
Captures-N
PlayerCapture-N.log
VisualDiff-N
```

Rebuild after every source change. Stop immediately when all acceptance gates
pass. If cycle 3 fails, stop and report; do not start cycle 4.

- [ ] **Step 7: Freeze and verify the accepted/final state**

Run the four focused fixtures into:

```text
Artifacts\LAN-LOBBY\CreateFrameCompletion\Verification-Final\
```

Then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

Require all focused tests and three smokes to pass. Verify the final manifest
has the frozen frame count, only `doc_frame_line` outline Sprites, four
unchanged `img_pointer` nodes, no active `room_select_create_logo`, and no
code-native outline.

- [ ] **Step 8: Commit final measured literals**

If Task 3 changed source values:

```powershell
git add -- `
  'Assets/Game/Runtime/Lobby/LanLobbyView.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs'
git commit -m 'fix: calibrate visible create frame'
```

If cycle 1 passes without source changes, do not create an empty commit; record
`commit: NONE` in the Task 3 report.

---

### Task 4: Record evidence and verify the completed frame slice

**Files:**
- Modify: `docs/SPEC.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/LAN-LOBBY-REPORT.md`

**Interfaces:**
- Consumes: accepted final JSON/Markdown/PNGs, focused XML, build/Player logs, capture manifest, asset map/hashes, and final Git diff.
- Produces: auditable frame-completion documentation and repository-wide regression evidence.

- [ ] **Step 1: Update the behavioral specification**

Add a concise Figure 9 Create-frame paragraph to `docs/SPEC.md` recording:

```text
four wings and central decoration are frozen but not pixel acceptance gates;
InteriorBacking is a textureless black non-raycast fill;
the visible outline contains the final 9..16 real doc_frame_line Images only;
all four straight edges pass coverage/gap/contrast gates;
the right-top chamfer passes pixels/contrast/manual visibility;
Create/Join action layout and LAN behavior remain immutable.
```

- [ ] **Step 2: Record exact retained evidence**

Update `docs/TEST_PLAN.md` and `docs/LAN-LOBBY-REPORT.md` with:

```text
final focused fixture totals and XML/log paths;
build exit/result/error/warning counts and executable;
five PNG names, byte sizes, decoded dimensions, and manifest;
final VisualDiff directory and report filenames;
for each frame row: search/background Rects, continuity values, both median
  lumas, contrast delta, thresholds, and pass state;
final frame segment count, names, transforms, tint, visible overlaps, source
  path, SHA-256, and occurrences;
backing Rect/color/sprite-null/raycast evidence;
zero active room_select_create_logo and zero code-native outline;
frozen action Rect/content results;
manual full Home/frame crop/overlay/heatmap findings;
explicit statement that wings/central rows are informational/frozen;
LAN regression evidence.
```

- [ ] **Step 3: Run repository-wide EditMode and PlayMode suites**

```powershell
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$root = "Artifacts\LAN-LOBBY\CreateFrameCompletion\FullSuite-$timestamp"

powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform EditMode `
  -OutputDirectory (Join-Path $root 'EditMode') `
  -TimeoutSeconds 900

powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -OutputDirectory (Join-Path $root 'PlayMode') `
  -TimeoutSeconds 900
```

Compare with the retained unrelated baseline:

```text
EditMode:
- BattleCoreEditModeTests.Fixture_InvalidInputMatrixReturnsStructuredErrors
- BattleCoreEditModeTests.RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources

PlayMode:
- PreparationBattleLoopPlayModeTests.SampleScene_AutoLoopsPreparationToBattleAndBackWithoutWritingCombatResultToPlayerState
```

Exactly those failures may be documented as unchanged and outside this UI
slice. Any new Lobby/frame/provenance/capture/evidence failure blocks
completion.

- [ ] **Step 4: Review provenance and final diff**

Run:

```powershell
git diff --check
git status --short
git diff --stat
git ls-files 'Artifacts/*' 'Temp/*' 'Library/*' 'Build/*' 'Builds/*'
git diff -- `
  'Assets/Game/Runtime/Lobby/LanLobbyView.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs' `
  'scripts/ExportLanLobbyVisualDiff.ps1' `
  'scripts/TestLanLobbyVisualDiffSmoke.ps1' `
  'docs/SPEC.md' `
  'docs/TEST_PLAN.md' `
  'docs/LAN-LOBBY-REPORT.md'
```

Require:

```text
no tracked generated directory;
no unrelated scene/Prefab/resource change;
no Create/Join action or LAN diff;
no $0/#0 source;
no missing/changed Sprite meta;
no code-native/preset outline;
only doc_frame_line outline Images;
frozen wing/central values unchanged from plan start.
```

- [ ] **Step 5: Commit documentation**

```powershell
git add -- 'docs/SPEC.md' 'docs/TEST_PLAN.md' 'docs/LAN-LOBBY-REPORT.md'
git commit -m 'docs: record visible create frame evidence'
```

- [ ] **Step 6: Completion verification**

Invoke `superpowers:verification-before-completion` before any completion
claim. Re-read the accepted JSON/Markdown and inspect the final full Home,
frame actual/reference/overlay/heatmap at native resolution. Do not call the
Create UI complete if any frame continuity, contrast, chamfer, provenance,
action, test, build, capture, or LAN gate is unverified or failed.

