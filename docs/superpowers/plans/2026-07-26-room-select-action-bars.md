# Room Select Action Bars Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pixel-anchor the Home “创建同盟” and “加入同盟” action bars to Figure 9 before using them as anchors for later decoration work.

**Architecture:** `LanLobbyLayout` owns the two absolute 1920×1080 action-bar rectangles and scales them uniformly for other 16:9 sizes. `LanLobbyView` localizes those rectangles into the existing Create/Join containers, horizontally stretches the approved source sprites, and keeps icon, label, and hit area inside each bar. The existing visual-diff exporter gains two action-only comparisons whose reference crops use Figure 9’s native local coordinate system instead of the whole-screen independent-X/Y normalization.

**Tech Stack:** Unity 2022.3.62f1c1, C# uGUI, Unity Test Framework, PowerShell, System.Drawing.

## Global Constraints

- The approved design is `docs/superpowers/specs/2026-07-26-room-select-action-bars-design.md`.
- At 1920×1080 the Create action Rect is `(left=1154, bottom=528, width=717, height=99)` and the Join action Rect is `(left=1154, bottom=105, width=717, height=99)`; final layout tolerance is `2px`.
- Both action bars use the same X, width, and height. Their background Images intentionally do not preserve the source `537:105` aspect ratio.
- The Create and Join action icons remain approved, provenance-mapped sprites; labels remain Unity Text. Do not import or generate bitmap art.
- All decorative sprites may overlap, but this task keeps them behind the action bars and prevents them from intercepting action-bar raycasts.
- Preserve `CreateRequested`, guarded `JoinRequested`, six-digit room-code prefill, discovery behavior, Room UI, and LAN protocol behavior.
- Do not modify scenes, Prefabs, Packages, ProjectSettings, source material directories, or prior retained evidence.
- Write generated screenshots and reports only below ignored `Temp/` or `Artifacts/`; every retained run uses a new, absent output directory.

---

## File Structure

| File | Responsibility in this plan |
| --- | --- |
| `Assets/Game/Runtime/Lobby/LanLobbyLayout.cs` | Supplies uniformly scaled absolute Create/Join action-bar Rects. |
| `Assets/Game/Runtime/Lobby/LanLobbyView.cs` | Places, stretches, layers, and wires the two action bars. |
| `Assets/Game/Tests/EditMode/Lobby/LanLobbyLayoutEditModeTests.cs` | Verifies exact 1920×1080 action Rects and uniform 16:9 scaling. |
| `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs` | Verifies rendered Rects, non-preserved background aspect, internal icon/label structure, hit area, and existing events. |
| `scripts/ExportLanLobbyVisualDiff.ps1` | Exports separate Create/Join action crops and metrics. |
| `scripts/TestLanLobbyVisualDiffSmoke.ps1` | Verifies the new local comparison schema and files without external writes. |
| `docs/LAN-LOBBY-REPORT.md` | Records the retained Player evidence and exact material usage. |
| `docs/TEST_PLAN.md` | Records repeatable action-bar verification commands and results. |

---

### Task 1: Anchor and render both action bars

**Files:**
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyLayout.cs`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
- Modify: `Assets/Game/Tests/EditMode/Lobby/LanLobbyLayoutEditModeTests.cs`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`

**Interfaces:**
- Consumes: `LanLobbyLayout.ForSize(int width, int height, int playerCount)`, existing `RoomSelectCreate`/`RoomSelectJoin`, `CreateRequested`, `JoinRequested`, `room_select_create_btn_bg_down`, `room_select_join_btn_bg_down`, `create_icon`, and `join_icon`.
- Produces: `LanLobbyLayout.RoomSelectCreateAction` and `LanLobbyLayout.RoomSelectJoinAction`, both `LanLobbyRect`; rendered nodes `Home/RoomSelect/Create/CreateAction` and `Home/RoomSelect/Join/JoinAction` at the exact layout Rects.

- [ ] **Step 1: Write the failing EditMode layout test**

Add a test that specifies the exact reference rectangles and uniform scale:

```csharp
[Test]
public void RoomSelect_ActionBarsUseMeasuredFigure9Rects()
{
    var full = global::LanLobbyLayout.ForSize(1920, 1080, 4);
    AssertRect(full.RoomSelectCreateAction, 1154f, 528f, 717f, 99f, 0.01f);
    AssertRect(full.RoomSelectJoinAction, 1154f, 105f, 717f, 99f, 0.01f);

    var small = global::LanLobbyLayout.ForSize(1280, 720, 4);
    AssertRect(small.RoomSelectCreateAction, 1154f * 2f / 3f, 528f * 2f / 3f, 717f * 2f / 3f, 66f, 0.02f);
    AssertRect(small.RoomSelectJoinAction, 1154f * 2f / 3f, 70f, 717f * 2f / 3f, 66f, 0.02f);
}

private static void AssertRect(LanLobbyRect actual, float left, float bottom, float width, float height, float tolerance)
{
    Assert.That(actual.Left, Is.EqualTo(left).Within(tolerance));
    Assert.That(actual.Bottom, Is.EqualTo(bottom).Within(tolerance));
    Assert.That(actual.Width, Is.EqualTo(width).Within(tolerance));
    Assert.That(actual.Height, Is.EqualTo(height).Within(tolerance));
}
```

- [ ] **Step 2: Run the layout test and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests' -OutputDirectory 'Temp/ROOM-SELECT-ACTION-BARS/RedLayout'
```

Expected: FAIL because the two action properties do not exist.

- [ ] **Step 3: Add the measured action Rects to `LanLobbyLayout`**

Extend the constructor and public properties:

```csharp
public LanLobbyRect RoomSelectCreateAction { get; }
public LanLobbyRect RoomSelectJoinAction { get; }
```

In `ForSize`, create the absolute action Rects from the approved 1920×1080 values:

```csharp
var createAction = new LanLobbyRect(leftInset + 1154f * scale, bottomInset + 528f * scale, 717f * scale, 99f * scale);
var joinAction = new LanLobbyRect(leftInset + 1154f * scale, bottomInset + 105f * scale, 717f * scale, 99f * scale);
```

Pass both through the `LanLobbyLayout` constructor. Do not change the existing Create/Join decoration-container Rects in this task.

- [ ] **Step 4: Run the layout test and verify GREEN**

Run the Step 2 command with output `Temp/ROOM-SELECT-ACTION-BARS/GreenLayout`. Expected: non-zero test total, result `Passed`, failed `0`.

- [ ] **Step 5: Write the failing PlayMode structure and behavior test**

Add one focused test that obtains the Create/Join container and action `RectTransform`s and asserts:

```csharp
AssertActionRect(createActionRect, createRect, layout.RoomSelectCreateAction, 2f);
AssertActionRect(joinActionRect, joinRect, layout.RoomSelectJoinAction, 2f);
Assert.That(createActionImage.preserveAspect, Is.False);
Assert.That(joinActionImage.preserveAspect, Is.False);
Assert.That(createActionRect.sizeDelta, Is.EqualTo(joinActionRect.sizeDelta));
Assert.That(createAction.Find("ActionIcon").GetComponent<Image>().sprite.name, Is.EqualTo("create_icon"));
Assert.That(joinAction.Find("ActionIcon").GetComponent<Image>().sprite.name, Is.EqualTo("join_icon"));
Assert.That(createAction.Find("Label").GetComponent<Text>().text, Is.EqualTo("创建同盟"));
Assert.That(joinAction.Find("Label").GetComponent<Text>().text, Is.EqualTo("加入同盟"));
Assert.That(create.GetComponent<Button>(), Is.Null, "The whole decoration container must not replace the action-bar hit area.");
Assert.That(createActionRect.GetSiblingIndex(), Is.EqualTo(create.childCount - 1));
Assert.That(joinActionRect.GetSiblingIndex(), Is.EqualTo(join.childCount - 1));
```

Define the helper in the test file so the expected absolute bottom-left Rect is explicit:

```csharp
private static void AssertActionRect(RectTransform action, RectTransform container, LanLobbyRect expected, float tolerance)
{
    Assert.That(container.anchoredPosition.x + action.anchoredPosition.x, Is.EqualTo(expected.Left).Within(tolerance));
    Assert.That(container.anchoredPosition.y + action.anchoredPosition.y, Is.EqualTo(expected.Bottom).Within(tolerance));
    Assert.That(action.sizeDelta.x, Is.EqualTo(expected.Width).Within(tolerance));
    Assert.That(action.sizeDelta.y, Is.EqualTo(expected.Height).Within(tolerance));
}
```

Subscribe to `CreateRequested`, invoke `createAction.GetComponent<Button>().onClick`, and assert exactly one request. Retain the existing discovery-prefill assertion and controller tests for Join behavior.

- [ ] **Step 6: Run the View test and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' -OutputDirectory 'Temp/ROOM-SELECT-ACTION-BARS/RedView'
```

Expected: FAIL because the current actions are approximately `537×105`, preserve aspect, and the Create container still owns a full-area Button.

- [ ] **Step 7: Localize the absolute action Rects in `LanLobbyView`**

Pass the absolute action Rect and its container Rect into each builder. Add a pure helper:

```csharp
private static LanLobbyRect RelativeTo(LanLobbyRect child, LanLobbyRect parent)
{
    return new LanLobbyRect(child.Left - parent.Left, child.Bottom - parent.Bottom, child.Width, child.Height);
}
```

Build with:

```csharp
BuildCreateSection(create, RelativeTo(layout.RoomSelectCreateAction, layout.RoomSelectCreate));
BuildJoinSection(join, RelativeTo(layout.RoomSelectJoinAction, layout.RoomSelectJoin));
```

Remove the transparent `Image` and `Button` currently attached to the whole Create container. The existing `CreateAction` Button remains the sole clickable Create area.

- [ ] **Step 8: Stretch and layer the approved action sprites**

For each action Button, do not call `PositionSprite`. Position its `RectTransform` with `PositionBottomLeft(actionRect)`, set `Image.preserveAspect = false`, and keep it after the decorative siblings so the bar renders above temporary decoration overlap.

Use these internal values for both bars:

```csharp
PositionSprite(actionIcon, new Vector2(.08f, .5f), 48f);
label.alignment = TextAnchor.MiddleLeft;
label.rectTransform.offsetMin = new Vector2(112f, 0f);
label.rectTransform.offsetMax = new Vector2(-220f, 0f);
label.fontSize = 38;
```

Keep the existing dark Create label color and dark-orange Join label color. Do not move other decorations except when necessary to keep them behind the action Button in sibling order; decorative `Image` components remain `raycastTarget=false`.

- [ ] **Step 9: Run GREEN and regression tests**

Run the Step 6 View command into the new output directory `Temp/ROOM-SELECT-ACTION-BARS/GreenView`, then:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests' -OutputDirectory 'Temp/ROOM-SELECT-ACTION-BARS/ControllerRegression'
```

Expected: all selected tests pass with non-zero totals. Inspect XML for exact total/failed/skipped counts; a missing or zero-test XML is unverified, not passing.

- [ ] **Step 10: Commit Task 1**

```powershell
git add -- 'Assets/Game/Runtime/Lobby/LanLobbyLayout.cs' 'Assets/Game/Runtime/Lobby/LanLobbyView.cs' 'Assets/Game/Tests/EditMode/Lobby/LanLobbyLayoutEditModeTests.cs' 'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs'
git commit -m "fix: anchor room select action bars"
```

---

### Task 2: Export separate Create and Join local comparisons

**Files:**
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1`
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1`

**Interfaces:**
- Consumes: the existing five-state capture manifest, Figure 9 reference resolution, and Task 1’s two 1920×1080 action Rects.
- Produces: `report.actionBars` entries named `home-create-action` and `home-join-action`, plus `*-actual.png`, `*-reference.png`, `*-overlay.png`, and `*-heatmap.png` for each action.

- [ ] **Step 1: Add failing smoke assertions for action-only evidence**

After parsing `visual-diff-report.json`, assert:

```powershell
$actionBars = @($report.actionBars)
Assert-True ($actionBars.Count -eq 2) 'two Home action bars must be reported separately'
$createAction = $actionBars | Where-Object name -eq 'home-create-action'
$joinAction = $actionBars | Where-Object name -eq 'home-join-action'
Assert-True (($createAction.actualRect.x -eq 1154) -and ($createAction.actualRect.y -eq 453) -and ($createAction.actualRect.width -eq 717) -and ($createAction.actualRect.height -eq 99)) 'Create actual crop must use the approved Rect'
Assert-True (($joinAction.actualRect.x -eq 1154) -and ($joinAction.actualRect.y -eq 876) -and ($joinAction.actualRect.width -eq 717) -and ($joinAction.actualRect.height -eq 99)) 'Join actual crop must use the approved Rect'
foreach ($name in @('home-create-action','home-join-action')) { foreach ($kind in @('actual','reference','overlay','heatmap')) { Assert-True (Test-Path -LiteralPath (Join-Path $output ($name + '-' + $kind + '.png'))) "missing $name $kind" } }
```

- [ ] **Step 2: Run the smoke test and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
```

Expected: FAIL because `actionBars` and the eight local evidence images do not exist.

- [ ] **Step 3: Define native-reference and actual action Rect specifications**

Add immutable specifications near `$homeRegions`:

```powershell
$homeActionBars = @(
  @{ name='home-create-action'; actual=@{x=1154;y=453;width=717;height=99}; reference=@{x=1257;y=482;width=763;height=105} },
  @{ name='home-join-action'; actual=@{x=1154;y=876;width=717;height=99}; reference=@{x=1257;y=932;width=763;height=105} }
)
$figure9MeasurementSize = @{ width=2102; height=1149 }
```

Scale each reference Rect to the decoded native reference size by `nativeWidth / 2102` and `nativeHeight / 1149`. This keeps smoke fixtures valid without rewriting their native dimensions and avoids using the whole-screen independent-X/Y normalized bitmap for the action-only comparison.

- [ ] **Step 4: Implement local crop comparison and image export**

Add helpers that clamp a Rect to its source bitmap, crop an actual bitmap, crop the native reference, resize only the cropped reference to `717×99`, and compare the two equal-size bitmaps with `LanLobbyVisualDiff.Compare` over a single full-crop region and no masks.

Each report entry must contain:

```powershell
[pscustomobject][ordered]@{
    name = $spec.name
    capture = 'home'
    actualRect = [ordered]@{ x=1154; y=$spec.actual.y; width=717; height=99 }
    referenceRect = [ordered]@{ x=$scaledReference.X; y=$scaledReference.Y; width=$scaledReference.Width; height=$scaledReference.Height }
    referenceMeasurementCanvas = [ordered]@{ width=2102; height=1149 }
    comparedPixels = $metric.ComparedPixels
    pixelDifferenceRatio = [double]$metric.DifferentPixels / $metric.ComparedPixels
    averageAbsoluteRgbError = [double]$metric.ErrorSum / ($metric.ComparedPixels * 3)
}
```

Save the four files per action into the existing staging directory before the final atomic move. Do not overwrite caller output or mutate reference files.

- [ ] **Step 5: Add the action-bar section to JSON and Markdown**

Add `actionBars=$actionBarReports` to the top-level JSON report. Add a Markdown table with name, actual Rect, native reference Rect, difference ratio, and RGB error. State explicitly that the action comparison uses measured native Figure 9 crops, while the legacy full-screen report retains its existing independent-X/Y normalization.

- [ ] **Step 6: Run GREEN smoke and existing evidence smoke tests**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
```

Expected: both scripts print PASS and exit `0`. Confirm the synthetic reference hashes remain unchanged and pre-existing output sentinels remain preserved.

- [ ] **Step 7: Commit Task 2**

```powershell
git add -- 'scripts/ExportLanLobbyVisualDiff.ps1' 'scripts/TestLanLobbyVisualDiffSmoke.ps1'
git commit -m "test: compare room select action bars separately"
```

---

### Task 3: Generate and document retained Player evidence

**Files:**
- Modify: `docs/LAN-LOBBY-REPORT.md`
- Modify: `docs/TEST_PLAN.md`
- Generate ignored: `Artifacts/LAN-LOBBY/HomeRoomSelectActionBars/`

**Interfaces:**
- Consumes: Task 1 UI, Task 2 exporter, `Task006StandaloneBuild.BuildWindowsX64`, and `LanLobbyCaptureSuite`.
- Produces: a new Windows Player build log, five 1920×1080 captures, provenance manifest, full visual report, two action-only comparisons, and repeatable documentation.

- [ ] **Step 1: Run the focused suites before building**

Run Layout EditMode, View PlayMode, Capture PlayMode, and Controller PlayMode into distinct `Temp/ROOM-SELECT-ACTION-BARS/Final*` directories using `scripts/Invoke-UnityTests.ps1` and the exact filters:

```text
ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests
ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests
ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests
ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests
```

Expected: each XML is valid, has a non-zero total, result `Passed`, and `failed=0`.

- [ ] **Step 2: Build a fresh Windows x64 Player**

Use an absent output directory:

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\WindowsStandalone\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' -batchmode -accept-apiupdate -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -executeMethod Task006StandaloneBuild.BuildWindowsX64 -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\WindowsStandaloneBuild.log'
```

Expected: exit `0`, `BuildReport` result `Succeeded`, errors `0`. Record warnings exactly; do not silently classify them as new or existing without log evidence.

- [ ] **Step 3: Run visible D3D11 capture at 1920×1080**

```powershell
$captureArgs = @('-force-d3d11','-screen-width','1920','-screen-height','1080','-lanLobbyCaptureSuite','-lanLobbyCaptureOutput','G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\CapturesFinal')
$player = Start-Process -FilePath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\WindowsStandalone\ARKnoNIGHTS.exe' -ArgumentList $captureArgs -PassThru -Wait
if ($player.ExitCode -ne 0) { throw "Capture Player failed: $($player.ExitCode)" }
```

Verify five decodable 1920×1080 PNGs plus `manifest.json`. Verify Home records the two action sprites and their approved source paths; no `$0` Unpacked source may appear.

- [ ] **Step 4: Export the full and local visual reports**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 -CaptureDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\CapturesFinal' -OutputDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\VisualDiff' -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

Inspect `home-create-action-overlay.png`, `home-create-action-heatmap.png`, `home-join-action-overlay.png`, and `home-join-action-heatmap.png`. Record actual/reference Rects and both local metrics. A metric marked ATTENTION is evidence for the next iteration, not permission to hide or alter the report.

- [ ] **Step 5: Document exact verification and remaining visual scope**

Update `docs/LAN-LOBBY-REPORT.md` and `docs/TEST_PLAN.md` with:

- exact commands, result XML paths, test totals, build result/errors/warnings;
- retained screenshot, manifest, and action-only report paths;
- Create/Join actual and reference Rects and local metrics;
- source-path and occurrence summary for both action sprites/icons;
- statement that other decoration placement is intentionally not accepted in this iteration;
- next visual step: use these two calibrated bars as anchors, rotate a repeated `room_select_create_left_line` by `180°` for the Create right side, and compose Join decorations with overlap.

- [ ] **Step 6: Run final repository checks**

```powershell
git diff --check
git status --short
git ls-files 'Artifacts/*' 'Temp/*'
```

Expected: no tracked generated Player, PNG, manifest, XML, or log. Review the final diff and confirm it contains only the files listed by this plan plus the committed design/plan documents.

- [ ] **Step 7: Commit Task 3 documentation**

```powershell
git add -- 'docs/LAN-LOBBY-REPORT.md' 'docs/TEST_PLAN.md'
git commit -m "docs: record action bar visual evidence"
```

---

## Stop Conditions

- Stop if a Unity Editor or batchmode process is already using this exact worktree.
- Stop if either action sprite resolves to an unmapped or `$0` Unpacked source.
- Stop if matching Figure 9 would require changing LAN, Room, discovery, or join-gating behavior.
- Stop after three materially different visual calibration attempts if either action Rect still cannot be brought within `2px`; report the measured actual/reference Rects and the three attempted coordinates.
- Do not call the two bars visually accepted until both local overlays have been inspected, even if all automated tests pass.
