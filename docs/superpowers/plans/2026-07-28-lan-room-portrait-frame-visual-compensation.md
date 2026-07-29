# LAN Room Portrait Frame Visual Compensation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the visible portrait-frame region of every LAN room player slot share one geometry, align its visible left/right edges with the upper bar, extend it beneath the lower avatar/name bar, and prove the result against Figures 11–13 without moving the already acceptable slot components.

**Architecture:** Keep the existing public `CardBody` layout/property/node name for capture-manifest compatibility, but treat it as the conceptual `PortraitFrame`. Give it a new state-independent backing rectangle. Preserve the current invite, ready overlay, icon, label, top bar, lower decoration, creator tag, action buttons, Leave, latency, and slot-root rectangles by deriving state content from a separate fixed legacy content area rather than the enlarged frame. Render the lower decoration above the frame so it hides the extended backing seam. Extend the existing evidence pipeline with dedicated visible portrait-frame gates and structured top-bar/seam relations; backing rectangles remain diagnostic and visible pixels remain authoritative.

**Tech Stack:** Unity 2022.3.62f1, C# / Unity UI, NUnit EditMode and PlayMode tests, PowerShell evidence scripts, Windows x86_64 standalone Player, existing approved autochess PNG assets only.

## Global Constraints

- Work only in `G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby` on branch `codex/lan-lobby`.
- The approved design is `docs/superpowers/specs/2026-07-28-lan-room-portrait-frame-visual-compensation-design.md`.
- Before each task, run `git status --short`; preserve unrelated/user-owned changes and stop if a target file changed unexpectedly.
- Run Unity processes serially. Never open this project path in more than one Unity Editor, test, or build process at a time.
- Use `D:\2022.3.62f1c1\Editor\Unity.exe`. Do not upgrade Unity, Packages, the render pipeline, or the input system.
- Do not edit or commit generated directories: `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`, `Builds/`, or `Artifacts/`.
- Do not add, crop, composite, generate, or modify any bitmap. Continue using the already approved `card_bg` Sprite from `[uc]autochessouter/card_bg.png`, SHA-256 `050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2`.
- Keep the runtime property and hierarchy name `CardBody`. Renaming it to `PortraitFrame` would churn public layout consumers, capture paths, material-audit rows, and existing evidence without improving the rendered result.
- Treat `CardBody` as the conceptual portrait frame in comments, tests, and reports.
- Empty, waiting, ready, and host states must use the same normalized frame footprint.
- Host slot content remains empty: no portrait, avatar, display name, player ID, or profile card.
- Do not move the slot roots, `TopBar`, `ReadyTopBar`, `StateOverlay`, `EmptyInvite`, `ReadyIcon`, `ReadyLabel`, `LowerDecoration`, `CreatorTag`, primary action, Leave, or latency unless a new failing regression test first proves that this task changed one of them.
- Figures 11–13 are normalized from `2560x1440` to `1920x1080` by exactly `0.75`.
- Ignore the upper scrolling comments. Ignore the right popup and the complete fourth slot in Figures 12 and 13; those two fourth-slot records remain `ExcludedByReferencePopup` with `passed=false`.
- Portrait artwork, avatar/name/ID content, and reference popup pixels are outside this task’s visual gates.
- At `1920x1080`, portrait-frame visible-edge tolerances are: each edge `<= 4 px`, visible center `<= 2 px` per axis, and visible width difference `<= 3 px`.
- The maximum continuous background seam near the lower-decoration upper edge is `1 px`.
- The backing frame must geometrically overlap the lower decoration by at least `60 px`.
- Do not enlarge ROIs, add popup masks, lower thresholds, remove failing gates, or relabel failures as exclusions to obtain a pass.
- Use no more than three new visible Windows Player calibration cycles. Stop early when all dedicated non-excluded portrait-frame gates pass. After Cycle 3, stop and report exact remaining deviations.
- Every code task follows red-green-refactor: add a focused failing test, run it and retain the failure evidence, implement the minimum change, rerun to green, inspect the diff, and commit.
- A Unity test run is valid only when the process exits successfully, its XML exists and parses, test count is greater than zero, and failures/skips are both zero.
- Every task ends with `git diff --check` and a focused diff review.

## Canonical Geometry Contract

All values below are local to the `363.75 x 664.5` slot root at `1920x1080`.

| Element | Left | Bottom | Width | Height | Change policy |
|---|---:|---:|---:|---:|---|
| `CardBody` / portrait-frame backing seed | `26.0` | `38.5` | `329.0` | `626.0` | New visual-compensation seed |
| Preserved portrait content area | `18.5` | `111.25` | `337.0` | `553.25` | New private canonical only; preserves children |
| `TopBar` | `26.25` | `622.073718` | `320.25` | `42.426282` | Must not move |
| `ReadyTopBar` | `26.25` | `621.5` | `320.25` | `51.0` | Must not move |
| `StateOverlay` | `18.5` | `111.25` | `337.0` | `135.589844` | Must not move |
| `EmptyInvite` | `18.5` | `324.151364` | `337.0` | `127.447273` | Must not move |
| `ReadyIcon` | `114.5` | `144.25` | `38.0` | `38.0` | Must not move |
| `ReadyLabel` anchor | `168.5` | `148.25` | `0.0` | `0.0` | Must not move |
| `LowerDecoration` | `0.0` | `0.0` | `363.75` | `120.0` | Must not move |
| `CreatorTag` | `123.5` | `576.25` | `124.0` | `35.0` | Must not move |

The initial backing/lower overlap is:

```text
LowerDecoration.Top - CardBody.Bottom
= 120.0 - 38.5
= 81.5 px
```

These backing values are a calibration seed, not the final visual truth. A later Player cycle may change only `CardBody` left/width/height when a named visible-frame gate proves the correction necessary.

---

### Task 1: Lock the shared portrait-frame geometry without moving state content

**Files:**

- Modify: `Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs`

**Implementation boundary:**

```csharp
// Keep the public compatibility name.
public LanLobbyRect CardBody { get; }

private const float CardBodyLeft = 26f;
private const float CardBodyWidth = 329f;
private const float CardBodyHeight = 626f;

// Preserve existing state-content geometry independently from CardBody.
private static readonly LanLobbyRect CanonicalPortraitContentArea =
    FromTopLeft(18.5f, 0f, 337f, 553.25f, SlotRootHeight);
```

`CanonicalStateOverlay` and `CanonicalEmptyInvite` must derive from `CanonicalPortraitContentArea`, not from the new `CanonicalCardBody`. The public layout shape and the `Children(...)` list remain unchanged.

**Steps:**

- [ ] Run `git status --short` and inspect the two target files plus direct references to `CardBody`, `StateOverlay`, and `EmptyInvite`.

- [ ] Replace `RoomLayout_KeepsCardBodyAndLowerDecorationContainedWithSeamOverlap` with a failing contract that asserts every slot has the exact shared seed and an `81.5 px` overlap:

```csharp
[Test]
public void RoomLayout_UsesOneCompensatedPortraitFrameWithDeepLowerOverlap()
{
    var layout = global::LanLobbyRoomLayout.ForSize(1920, 1080);

    foreach (var slot in layout.Slots)
    {
        AssertRect(slot.CardBody, 26f, 38.5f, 329f, 626f);
        AssertContained(slot.CardBody, slot.Root);
        AssertContained(slot.LowerDecoration, slot.Root);
        Assert.That(
            slot.LowerDecoration.Top - slot.CardBody.Bottom,
            Is.EqualTo(81.5f).Within(0.01f));
        Assert.That(
            slot.LowerDecoration.Top - slot.CardBody.Bottom,
            Is.GreaterThanOrEqualTo(60f));
    }
}
```

- [ ] Replace the old assumption that `StateOverlay` equals the frame rectangle with a failing preservation test:

```csharp
[Test]
public void RoomLayout_EnlargesOnlyPortraitFrameAndPreservesExistingSlotChildren()
{
    var slot = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];

    AssertRect(slot.TopBar, 26.25f, 622.073718f, 320.25f, 42.426282f);
    AssertRect(slot.ReadyTopBar, 26.25f, 621.5f, 320.25f, 51f);
    AssertRect(slot.StateOverlay, 18.5f, 111.25f, 337f, 135.589844f);
    AssertRect(slot.EmptyInvite, 18.5f, 324.151364f, 337f, 127.447273f);
    AssertRect(slot.ReadyIcon, 114.5f, 144.25f, 38f, 38f);
    AssertRect(slot.ReadyLabel, 168.5f, 148.25f, 0f, 0f);
    AssertRect(slot.LowerDecoration, 0f, 0f, 363.75f, 120f);
    AssertRect(slot.CreatorTag, 123.5f, 576.25f, 124f, 35f);
}
```

- [ ] Keep the source-aspect assertions for `StateOverlay`, `EmptyInvite`, and `TopBar`. Update only the container relationship that is no longer true.

- [ ] Run the focused EditMode suite and retain RED evidence:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyRoomLayoutEditModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\Task1\Layout-red' `
  -TimeoutSeconds 900
```

Expected RED: current `CardBody` is `(18.5, 111.25, 337, 553.25)` and overlap is `8.75`, so the new exact frame and overlap assertions fail while preservation rows stay green.

- [ ] Implement only the canonical geometry change:
  - set `CardBodyLeft=26f`, `CardBodyWidth=329f`, `CardBodyHeight=626f`;
  - add `CanonicalPortraitContentArea` with the old `18.5 / 337 / 553.25` values;
  - derive `CanonicalStateOverlay` from the preserved content area;
  - center `CanonicalEmptyInvite` in the preserved content area;
  - keep every other canonical value unchanged.

- [ ] Run the focused suite again under `Artifacts\LAN-LOBBY\PortraitFrame\Task1\Layout-green`. Require nonzero tests, zero failures, and zero skips.

- [ ] Inspect scaling coverage: the existing `2560x1440` and non-16:9 tests must prove the new frame scales/letterboxes with the other local children.

- [ ] Review and commit:

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs
git add Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs
git commit -m "fix: unify LAN room portrait frame geometry"
```

---

### Task 2: Make state footprints and render order deterministic

**Files:**

- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
- Use: `Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs`

**Required direct-child render order, back to front:**

```text
CardBody
ReadyOverlay
EmptyContent
OccupiedContent
TopBar
LowerDecoration
CreatorTag
```

The active state changes, but the `CardBody` world footprint does not. `TopBar` must cover state content at the upper edge; `LowerDecoration` must cover the extended frame at the lower seam.

**Steps:**

- [ ] Run `git status --short`; inspect `BuildRoomSlot`, `BindSlot`, direct-child assertions, and existing snapshot fixtures.

- [ ] Add a helper that compares a transformed rectangle by normalized world corners so the ready-state `localScale=(1,-1,1)` is handled correctly:

```csharp
private static Rect WorldRect(RectTransform transform)
{
    var corners = new Vector3[4];
    transform.GetWorldCorners(corners);
    var minX = corners.Min(point => point.x);
    var minY = corners.Min(point => point.y);
    var maxX = corners.Max(point => point.x);
    var maxY = corners.Max(point => point.y);
    return Rect.MinMaxRect(minX, minY, maxX, maxY);
}

private static void AssertWorldRect(Rect actual, Rect expected, float tolerance = .05f)
{
    Assert.That(actual.xMin, Is.EqualTo(expected.xMin).Within(tolerance));
    Assert.That(actual.yMin, Is.EqualTo(expected.yMin).Within(tolerance));
    Assert.That(actual.xMax, Is.EqualTo(expected.xMax).Within(tolerance));
    Assert.That(actual.yMax, Is.EqualTo(expected.yMax).Within(tolerance));
}

private static Rect RelativeToRoot(Rect child, Rect root)
{
    return new Rect(
        child.xMin - root.xMin,
        child.yMin - root.yMin,
        child.width,
        child.height);
}
```

- [ ] Add a failing PlayMode test `RoomSlot_AllStatesKeepOnePortraitFrameFootprint`:
  - bind a host-only snapshot and record slot 2 `CardBody` world rectangle in `Empty`;
  - bind a snapshot with one unready guest and record slot 2 in `Waiting`;
  - bind the same guest ready and record slot 2 in `Ready`;
  - record the host slot in ready/host state;
  - call `Canvas.ForceUpdateCanvases()` after each bind;
  - assert the three slot-2 absolute world rectangles are identical across state changes;
  - normalize each slot’s frame rectangle relative to its own slot-root world rectangle and assert all four relative footprints are identical;
  - assert all four slot frames share width and height.

- [ ] Add a failing PlayMode test `RoomSlot_LayersFrameBehindContentAndBars`:

```csharp
AssertDirectChildren(slot,
    "CardBody", "ReadyOverlay", "EmptyContent", "OccupiedContent",
    "TopBar", "LowerDecoration", "CreatorTag");

Assert.That(cardBody.GetSiblingIndex(), Is.LessThan(readyOverlay.GetSiblingIndex()));
Assert.That(cardBody.GetSiblingIndex(), Is.LessThan(emptyContent.GetSiblingIndex()));
Assert.That(cardBody.GetSiblingIndex(), Is.LessThan(occupiedContent.GetSiblingIndex()));
Assert.That(topBar.GetSiblingIndex(), Is.GreaterThan(readyOverlay.GetSiblingIndex()));
Assert.That(topBar.GetSiblingIndex(), Is.GreaterThan(emptyContent.GetSiblingIndex()));
Assert.That(topBar.GetSiblingIndex(), Is.GreaterThan(occupiedContent.GetSiblingIndex()));
Assert.That(lowerDecoration.GetSiblingIndex(), Is.GreaterThan(topBar.GetSiblingIndex()));
```

- [ ] Extend the test to assert exact unchanged `RectTransform` values for `TopBar`, `ReadyTopBar`, `ReadyOverlay`, `EmptyContent`, `ReadyIcon`, `ReadyLabel`, `LowerDecoration`, and `CreatorTag` using the layout from Task 1.

- [ ] Run the View suite and retain RED evidence:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\Task2\View-red' `
  -TimeoutSeconds 900
```

Expected RED: the current direct-child order places `TopBar` before state content.

- [ ] Reorder creation in `BuildRoomSlot` only:
  1. create/position `CardBody`;
  2. create `ReadyOverlay`, `EmptyContent`, and `OccupiedContent`;
  3. create/position `TopBar`;
  4. create/position `LowerDecoration`;
  5. create/position `CreatorTag`.

- [ ] Do not change `BindSlot` state semantics:
  - Empty/Waiting remain white;
  - Ready remains cyan and vertically flips the source;
  - after ready flipping, the `anchoredPosition += height` compensation must preserve the same normalized world footprint;
  - `LowerDecoration` remains active for every state.

- [ ] Update all exact direct-child order assertions to the approved order. Do not relax resource, active-state, source-aspect, raycast, or host-content assertions.

- [ ] Run the suite again under `Artifacts\LAN-LOBBY\PortraitFrame\Task2\View-green`. Require nonzero tests, zero failures, and zero skips.

- [ ] Review and commit:

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Lobby/LanLobbyView.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs
git add Assets/Game/Runtime/Lobby/LanLobbyView.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs
git commit -m "fix: layer unified LAN room portrait frames"
```

---

### Task 3: Add capture and visible portrait-frame acceptance contracts

**Files:**

- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs`
- Modify only if a failing capture test requires more structured data: `Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs`
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1`
- Modify if the new report fields must be forwarded: `scripts/ExportLanLobbyEvidence.ps1`
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1`
- Modify only if schema/forwarding assertions require it:
  - `scripts/TestExportLanLobbyEvidenceSmoke.ps1`
  - `scripts/TestLanLobbyEvidenceCommonSmoke.ps1`

**Dedicated gate inventory:**

| Capture | Required portrait-frame gates |
|---|---|
| `room-host` / Figure 11 | Slots 1–4 |
| `room-full` / Figure 12 | Slots 1–3; slot 4 remains excluded and false |
| `room-ready` / Figure 13 | Slots 1–3; slot 4 remains excluded and false |

Gate names:

```text
RoomHost.Slot1.PortraitFrame
RoomHost.Slot2.PortraitFrame
RoomHost.Slot3.PortraitFrame
RoomHost.Slot4.PortraitFrame
RoomFull.Slot1.PortraitFrame
RoomFull.Slot2.PortraitFrame
RoomFull.Slot3.PortraitFrame
RoomReady.Slot1.PortraitFrame
RoomReady.Slot2.PortraitFrame
RoomReady.Slot3.PortraitFrame
```

Each non-excluded gate blocks on visible placement, material provenance, and a `portraitFrameRelation` object:

```powershell
[pscustomobject][ordered]@{
    topBarHorizontalCenterDeltaPx = 0.0
    topBarWidthDeltaPx = 0.0
    geometricOverlapPx = 81.5
    maximumContinuousBackgroundGapPx = 0
    thresholds = [pscustomobject]@{
        maximumHorizontalCenterDeltaPx = 2
        maximumVisibleWidthErrorPx = 3
        minimumGeometricOverlapPx = 60
        maximumContinuousBackgroundGapPx = 1
    }
    passed = $true
}
```

`topBarHorizontalCenterDeltaPx` and `topBarWidthDeltaPx` compare the decoded visible frame side edges to the corresponding decoded upper-bar visible bounds. The gate’s ordinary actual/reference placement still enforces the `2 px` center tolerance on both axes. `geometricOverlapPx` comes from manifest screen rectangles. The seam scan uses the decoded screenshot around the `LowerDecoration` top edge; it may not infer a pass from backing rectangles alone.

**Steps:**

- [ ] Run `git status --short`; inspect current capture `keyRects`, source audit, report gate generation, mask helpers, failed-ROI rendering, and smoke mutation helpers.

- [ ] Add failing Capture-suite assertions:
  - every room capture exports all four `/CardBody` and `/LowerDecoration` key rectangles;
  - every `/CardBody` rect is `329x626` at `1920x1080`;
  - every capture/state uses the same per-slot `CardBody` rectangle;
  - each `CardBody`/`LowerDecoration` pair overlaps by `81.5 px` within `1 px` capture rounding;
  - `CardBody` remains a non-raycast `card_bg` bitmap occurrence;
  - `card_bg` occurrence count remains exactly four in each room capture;
  - no new Sprite or source-audit row appears.

Capture-manifest rectangles use `coordinateOrigin="screen-bottom-left"`. Use an origin-checked overlap helper:

```csharp
private static float VerticalOverlap(CaptureRectProbe frame, CaptureRectProbe lower)
{
    Assert.That(frame.coordinateOrigin, Is.EqualTo("screen-bottom-left"));
    Assert.That(lower.coordinateOrigin, Is.EqualTo("screen-bottom-left"));
    var intersectionBottom = Mathf.Max(frame.y, lower.y);
    var intersectionTop = Mathf.Min(frame.y + frame.height, lower.y + lower.height);
    return Mathf.Max(0f, intersectionTop - intersectionBottom);
}
```

- [ ] Run the Capture suite and retain RED evidence:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\Task3\Capture-red' `
  -TimeoutSeconds 900
```

Expected RED before Task 1 implementation; if Tasks 1–2 already make the capture assertions green, retain that result as baseline and require the PowerShell smoke mutations below to supply the RED phase for evidence behavior.

- [ ] Add generated-fixture smoke assertions before changing the exporter:
  1. moving one frame’s decoded visible bounds by `5 px` fails its edge relation;
  2. widening/narrowing decoded visible frame width by `4 px` fails its width relation;
  3. erasing a `2 px` horizontal seam band fails `maximumContinuousBackgroundGapPx`;
  4. changing one capture-state manifest `CardBody` rectangle while leaving other states unchanged fails shared geometry;
  5. an unchanged fixture passes all new relations;
  6. Figure 12/13 fourth-slot exclusions remain `passed=false`.

- [ ] Run all three evidence smokes and retain at least one new RED assertion against the production exporter:

```powershell
.\scripts\TestLanLobbyVisualDiffSmoke.ps1
.\scripts\TestExportLanLobbyEvidenceSmoke.ps1
.\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

- [ ] Extend `New-LanLobbyRoomGateSpec` with an explicit portrait-frame gate kind or equivalent named metadata. Do not overload an unrelated icon gate.

- [ ] Add dedicated frame gates in `Get-LanLobbyRoomGateSpecs`:
  - use the existing slot contour ROIs as the starting search areas;
  - use `Cyan` for ready frames and `Gray` for empty/waiting frames;
  - keep `maximumEdgeErrorPx=4` and `minimumContourJaccard=0.95`;
  - add the center/width/seam/overlap relation thresholds without removing any existing contour, top-bar, button, Leave, absence, or spacing gate.

- [ ] Add a structured helper equivalent to:

```powershell
function Measure-LanLobbyPortraitFrameRelation {
    param(
        [Parameter(Mandatory)] $FramePlacement,
        [Parameter(Mandatory)] $TopBarPlacement,
        [Parameter(Mandatory)] $FrameRect,
        [Parameter(Mandatory)] $LowerDecorationRect,
        [Parameter(Mandatory)] $ActualImage,
        [Parameter(Mandatory)] $SeamRoi,
        [Parameter(Mandatory)] [ValidateSet('Cyan','Gray')] [string] $MaskKind
    )
    # Return exact measured values, thresholds, and pass/fail.
}
```

- [ ] Define the relation math explicitly:

```text
centerDeltaX = frameVisible.centerX - topBarVisible.centerX
widthDelta = frameVisible.width - topBarVisible.width
geometricOverlap = min(frameRect.y + frameRect.height,
                       lowerRect.y + lowerRect.height)
                   - max(frameRect.y, lowerRect.y)
maximumContinuousBackgroundGap = longest consecutive seam-scan row
                                  containing no qualifying frame/lower pixels
```

The first three manifest terms above are in screen-bottom-left space. Before scanning screenshot pixels, convert the lower-decoration upper edge to screenshot top-left space with
`captureHeight - (lowerRect.y + lowerRect.height)`. The seam ROI must be a narrow fixed band around that converted edge; it must not include the entire slot or skip obstructing content. Store the ROI and mask kind in the report.

- [ ] Add a cross-capture semantic gate or top-level `portraitFrameSharedGeometry` record that compares each slot’s manifest `CardBody` width/height and its local offset from the slot root across `room-host`, `room-full`, and `room-ready`. It must be blocking and must not use screenshot pixels as a substitute for state equality.

- [ ] Make each portrait-frame gate pass only when:
  - visible placement passes;
  - the relation passes;
  - material evidence passes;
  - shared geometry passes for that slot/capture;
  - the record is not an explicit reference-popup exclusion.

- [ ] Preserve material provenance:
  - `/CardBody` continues to map to `card_bg`;
  - source path and SHA remain exact;
  - rendered occurrence and audit bijection remain exact;
  - no code-native substitute or generated frame is permitted.

- [ ] Include `portraitFrameRelation` and shared-geometry status in both JSON and Markdown. Failed overlays must draw the named frame ROI and seam ROI.

- [ ] Run the Capture suite under `Artifacts\LAN-LOBBY\PortraitFrame\Task3\Capture-green`. Require nonzero tests, zero failures, and zero skips.

- [ ] Run all three smoke scripts green. Require exit code `0`, explicit nonzero fixture/assertion counts, and no threshold/mask weakening.

- [ ] Review and commit:

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs scripts/ExportLanLobbyVisualDiff.ps1 scripts/ExportLanLobbyEvidence.ps1 scripts/TestLanLobbyVisualDiffSmoke.ps1 scripts/TestExportLanLobbyEvidenceSmoke.ps1 scripts/TestLanLobbyEvidenceCommonSmoke.ps1
git add Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs scripts/ExportLanLobbyVisualDiff.ps1 scripts/ExportLanLobbyEvidence.ps1 scripts/TestLanLobbyVisualDiffSmoke.ps1 scripts/TestExportLanLobbyEvidenceSmoke.ps1 scripts/TestLanLobbyEvidenceCommonSmoke.ps1
git commit -m "test: gate visible LAN portrait frames"
```

Do not stage an unchanged optional file merely because it is listed above.

---

### Task 4: Run the pre-Player verification queue

**Files:**

- Verify only. Modify only to fix a demonstrated task-related regression using a new focused failing test.

**Steps:**

- [ ] Run `git status --short` and confirm no Unity process already owns the worktree:

```powershell
Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
  Select-Object ProcessId, CommandLine
```

If the same project path is open, stop rather than launching a competing process.

- [ ] Run the focused suites serially:

```powershell
$unity = 'D:\2022.3.62f1c1\Editor\Unity.exe'
$project = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby'

.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyRoomLayoutEditModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\PrePlayer\Layout' -TimeoutSeconds 900
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LobbyAssetMapEditModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\PrePlayer\Assets' -TimeoutSeconds 900
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\PrePlayer\View' -TimeoutSeconds 900
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\PrePlayer\Capture' -TimeoutSeconds 900
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\PrePlayer\Controller' -TimeoutSeconds 900
```

- [ ] Run evidence smokes:

```powershell
.\scripts\TestLanLobbyVisualDiffSmoke.ps1
.\scripts\TestExportLanLobbyEvidenceSmoke.ps1
.\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

- [ ] Stop before Player build if any suite has zero tests, a failure, a skip, an unreadable/missing XML, a timeout, a compile error, or an incomplete log.

- [ ] Inspect logs for `error CS`, `Scripts have compiler errors`, `Compilation failed`, unhandled exceptions, missing Sprite/resource diagnostics, and task-related assertion failures.

- [ ] Run:

```powershell
git diff --check
git status --short
```

Unity import/test output must not create tracked scene, Prefab, package, `.meta`, or generated-directory changes.

---

### Task 5: Perform at most three fresh visible Player calibration cycles

**Files:**

- Modify only when a named gate proves it necessary:
  - `Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs`
  - `Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs`
  - `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
  - `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
  - `scripts/ExportLanLobbyVisualDiff.ps1`
  - `scripts/TestLanLobbyVisualDiffSmoke.ps1`
- Generate ignored evidence only under:
  - `Artifacts/LAN-LOBBY/PortraitFrame/Cycle-1/`
  - optionally `Cycle-2/`
  - optionally `Cycle-3/`

**Five required screenshots per cycle:**

```text
home.png
discovered-prefill.png
room-host.png
room-full.png
room-ready.png
```

Home screenshots protect prior work; the three room screenshots are the portrait-frame acceptance set.

**Steps:**

- [ ] Build Cycle 1 from the exact source state that passed Task 4:

```powershell
$cycle = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\PortraitFrame\Cycle-1'
$env:ARKNIGHTS_BUILD_OUTPUT = Join-Path $cycle 'WindowsStandalone\ARKnoNIGHTS.exe'

& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -accept-apiupdate -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile (Join-Path $cycle 'WindowsStandaloneBuild.log')

if ($LASTEXITCODE -ne 0) { throw "Windows build failed: $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $env:ARKNIGHTS_BUILD_OUTPUT -PathType Leaf)) {
    throw "Windows Player was not generated."
}
```

- [ ] Inspect `WindowsStandaloneBuild.log`; require `Succeeded`, `StandaloneWindows64`, zero build errors, and no new task-related warnings.

- [ ] Run the Player visibly. Do not use `-batchmode`, `-nographics`, or a hidden window:

```powershell
& (Join-Path $cycle 'WindowsStandalone\ARKnoNIGHTS.exe') `
  -force-d3d11 `
  -lanLobbyCaptureSuite `
  -lanLobbyCaptureOutput (Join-Path $cycle 'Captures') `
  -screen-width 1920 `
  -screen-height 1080 `
  -logFile (Join-Path $cycle 'PlayerCapture.log')

if ($LASTEXITCODE -ne 0) { throw "Player capture failed: $LASTEXITCODE" }
```

- [ ] Require all five PNGs, `manifest.json`, a clean Player exit, and exact `1920x1080` decodability. Missing/black/single-color screenshots fail the cycle.

- [ ] Export side-by-side evidence and the structured visual-difference report into separate directories:

```powershell
.\scripts\ExportLanLobbyEvidence.ps1 `
  -CaptureDirectory (Join-Path $cycle 'Captures') `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud' `
  -OutputDirectory (Join-Path $cycle 'Evidence')

.\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory (Join-Path $cycle 'Captures') `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud' `
  -OutputDirectory (Join-Path $cycle 'VisualDiff')
```

- [ ] Open `room-host.png`, `room-full.png`, and `room-ready.png` at original detail. Review:
  - frame visible width and center relative to each upper bar;
  - frame continuity down to/under the lower decoration;
  - absence of a background seam;
  - equal frame size across Empty, Waiting, Ready, and host states;
  - no movement of invite, ready icon/label, top bars, lower bars, creator tag, buttons, Leave, or latency;
  - no newly stretched/giant bitmap.

- [ ] Read `visual-diff-report.json` and require every non-excluded `*.PortraitFrame` gate plus `portraitFrameSharedGeometry` to pass. Preserve existing fourth-slot exclusion semantics for Figures 12/13.

- [ ] Check regression gates. Previously passing Home action bars, room upper bars, primary action, and Leave gates may not regress because of this task.

- [ ] If Cycle 1 fails, classify the failure before editing:
  - `CardBody` visible edge/width/height failure: add or tighten a layout test, adjust only frame seed constants;
  - state footprint/layer seam failure: add a View test, adjust only frame/state/bar sibling order or frame binding;
  - demonstrable detector bug: add a smoke mutation that reproduces the false result, fix only the detector;
  - unrelated existing visual gate: record it and do not broaden this task.

- [ ] For any calibration edit:
  1. preserve the exact failing measurement and gate name;
  2. add a focused failing automated test first;
  3. implement the minimum correction;
  4. rerun its focused Unity suite and all three evidence smokes;
  5. commit source/test changes only, naming the gate in the commit message;
  6. rebuild from the new commit into `Cycle-2`.

- [ ] If Cycle 2 still fails, repeat the same bounded procedure once into `Cycle-3`.

- [ ] Stop immediately on acceptance. If Cycle 3 still fails, stop without weakening thresholds and report:
  - exact failing gate names;
  - actual/reference visible bounds;
  - per-edge, center, and width deltas;
  - overlap and seam-gap measurements;
  - the three distinct corrections attempted.

- [ ] Never treat the old `RoomSlotStates/Cycle-3` evidence as proof for this task. The portrait-frame implementation requires a new Player build and new screenshot set.

- [ ] Review the final calibration diff:

```powershell
git diff --check
git status --short
git diff -- Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs Assets/Game/Runtime/Lobby/LanLobbyView.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs scripts/ExportLanLobbyVisualDiff.ps1 scripts/TestLanLobbyVisualDiffSmoke.ps1
```

Generated Cycle evidence remains ignored and uncommitted.

---

### Task 6: Synchronize authoritative docs and run final verification

**Files:**

- Modify: `docs/SPEC.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/LAN-LOBBY-REPORT.md`
- Inspect: `docs/superpowers/specs/2026-07-28-lan-room-portrait-frame-visual-compensation-design.md`
- Verify only unless a failure proves a regression: all implementation/test/script files from Tasks 1–5

**Steps:**

- [ ] Update `docs/SPEC.md` with the accepted player-visible contract:
  - all four slots and all Empty/Waiting/Ready/host states share one portrait-frame size;
  - visible frame sides align to the upper bar within the approved tolerances;
  - the frame extends under the lower avatar/name bar;
  - lower bar covers the seam;
  - other slot components do not move;
  - host still has no portrait/profile content;
  - Figure 12/13 fourth-slot exclusion remains false, not passed.

- [ ] Update `docs/TEST_PLAN.md` with:
  - exact new test names/filters;
  - new frame gate inventory;
  - edge/center/width/overlap/seam thresholds;
  - shared-geometry semantic gate;
  - five-screen capture requirement;
  - maximum three fresh Player cycles;
  - final actual commands and result/log paths.

- [ ] Update `docs/LAN-LOBBY-REPORT.md` from the fresh final cycle only:
  - implementation summary;
  - modified source/test/script files;
  - exact final `CardBody` backing rectangle;
  - exact visible measurements and named gate results;
  - Figure 12/13 excluded fourth slots as `passed=false`;
  - `card_bg` Resources path, approved source-relative path, SHA-256, and four occurrences per room capture;
  - statement that no bitmap was added or modified;
  - test counts, failures, skips, exit codes, and log paths;
  - build result and screenshot/evidence absolute paths;
  - any unverified or failing items.

- [ ] Preserve the known unrelated blockers in the report:
  - stale-after-start snapshot can overwrite `HasStarted`;
  - accept/stop lifecycle race;
  - physical Windows/Android same-Wi-Fi interoperability remains unverified until actually run.

- [ ] Run the final focused suites serially under `Artifacts\LAN-LOBBY\PortraitFrame\Final\`:

```powershell
$unity = 'D:\2022.3.62f1c1\Editor\Unity.exe'
$project = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby'

.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyRoomLayoutEditModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\Final\Layout' -TimeoutSeconds 900
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LobbyAssetMapEditModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\Final\Assets' -TimeoutSeconds 900
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\Final\View' -TimeoutSeconds 900
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\Final\Capture' -TimeoutSeconds 900
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\PortraitFrame\Final\Controller' -TimeoutSeconds 900
```

- [ ] Run all three final evidence smokes:

```powershell
.\scripts\TestLanLobbyVisualDiffSmoke.ps1
.\scripts\TestExportLanLobbyEvidenceSmoke.ps1
.\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

- [ ] If any source, layout, capture, or detector file changed after the last successful Player cycle, perform one new final build/capture/export from that exact source state. This is verification of the last calibration result, not a fourth correction opportunity.

- [ ] Inspect final logs and evidence:
  - all XML test counts are nonzero;
  - failures/skips are zero;
  - no new C# compile errors or unhandled exceptions;
  - all five screenshots exist and decode at `1920x1080`;
  - all non-excluded portrait-frame gates pass;
  - no existing passing bar/button/Leave gate regressed;
  - material/source failures are zero.

- [ ] Run repository hygiene checks:

```powershell
git diff --check
git status --short
git ls-files 'Artifacts/*' 'Temp/*' 'Logs/*' 'Library/*' 'Build/*' 'Builds/*'
git diff --stat
git log --oneline --decorate -12
```

Require no generated artifact tracked, no new PNG, no changed reference image, no package/version change, and no unrelated Home/network refactor.

- [ ] Perform an independent final review focused on:
  - state-independent frame geometry;
  - ready-flip world footprint;
  - render ordering and seam coverage;
  - preserved component geometry;
  - fourth-slot exclusions;
  - material provenance;
  - threshold/mask integrity;
  - test gaps and stale documentation.

- [ ] Commit documentation only after it matches actual fresh results:

```powershell
git diff --check
git diff -- docs/SPEC.md docs/TEST_PLAN.md docs/LAN-LOBBY-REPORT.md
git add docs/SPEC.md docs/TEST_PLAN.md docs/LAN-LOBBY-REPORT.md
git commit -m "docs: report unified LAN portrait frames"
```

## Completion Conditions

Do not declare this plan complete unless all applicable conditions hold:

- Empty, Waiting, Ready, and host `CardBody` footprints are identical.
- The visible frame sides align with the corresponding upper bar inside the approved thresholds.
- The frame visibly reaches beneath the lower decoration with no continuous background gap over `1 px`.
- The backing overlap is at least `60 px`.
- Invite, ready overlay/icon/label, top bars, lower decorations, creator tags, buttons, Leave, latency, and slot roots have not moved.
- Figure 11 slots 1–4 and Figure 12/13 slots 1–3 pass their dedicated portrait-frame gates.
- Figure 12/13 slot 4 remains explicitly excluded with `passed=false`.
- All focused Unity tests and evidence smokes are valid and green.
- A fresh Windows Player build and actual `1920x1080` screenshots support the visual conclusion.
- `card_bg` remains the only frame bitmap, with exact approved source and SHA; no bitmap was added or modified.
- Final diff contains no generated files or unrelated changes.
- Known network blockers and physical-device verification remain reported honestly rather than being implied fixed.
