# Room Select Action Content Visual Centers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Create and Join action-bar icons and labels match Figure 9 by visible-pixel bounds while preserving the approved background rectangles and LAN behavior.

**Architecture:** Keep the two approved `717×99` action buttons unchanged and give each bar independent, explicit local-pixel content layout. Extend the existing local-crop exporter with fixed-threshold visible-bound measurements so the real Player capture, not the texture rectangle, determines acceptance.

**Tech Stack:** Unity 2022.3.62f1, C#, Unity UI, NUnit/Unity Test Framework, Windows PowerShell 5.1, `System.Drawing`, visible D3D11 Windows Player capture.

## Global Constraints

- Work only in `G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby`; preserve the unrelated untracked `docs/unitbonds/`.
- Keep Create background `(1154,453,717,99)` and Join background `(1154,876,717,99)` in 1920×1080 top-left screen coordinates.
- Do not change Room UI, discovery, room-code prefill, Create/Join requests, or LAN protocol behavior.
- Keep `create_icon.png` and `join_icon.png` unchanged, preserve their `.meta` GUIDs, and create no derived bitmap.
- Keep other Room Select decoration layout unchanged.
- Use the same dark-pixel threshold for actual and reference crops.
- Pass when every icon/label visible center differs by at most `1 px` on each axis and visible width/height differs by at most `2 px`.
- Generated builds, captures, logs, and reports stay under ignored `Artifacts/` or `Temp/`.

## File Structure

| File | Responsibility |
| --- | --- |
| `Assets/Game/Runtime/Lobby/LanLobbyView.cs` | Owns the two independent action-content RectTransform layouts. |
| `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs` | Locks the approved background Rects and independent icon/label transforms. |
| `scripts/ExportLanLobbyVisualDiff.ps1` | Measures and reports visible-pixel bounds in the two local action crops. |
| `scripts/TestLanLobbyVisualDiffSmoke.ps1` | Proves the visible-bound report schema, calculations, thresholds, and artifact files. |
| `docs/TEST_PLAN.md` | Records the repeatable acceptance commands and exact criteria. |
| `docs/LAN-LOBBY-REPORT.md` | Records the final retained run, material provenance, and remaining visual risk. |

---

### Task 1: Independent action-content layout

**Files:**
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs:116-153`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs:323-407`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs:663-672`

**Interfaces:**
- Consumes: `BuildCreateSection(RectTransform, LanLobbyRect)`, `BuildJoinSection(RectTransform, LanLobbyRect)`, and the existing `Image`/`Text` children.
- Produces: `PositionSpriteTopLeft(Image value, float left, float top, float width)` and stable local transforms for both action bars.

- [ ] **Step 1: Write the failing PlayMode assertions**

Extend `HomeRoomSelect_ActionBarsUseMeasuredRectsStretchSpritesAndOwnCreateInput` with a helper that checks top-left anchored content:

```csharp
AssertTopLeftRect(createAction.Find("ActionIcon").GetComponent<RectTransform>(), 47f, 25f, 38f, 38f, .05f);
AssertTopLeftRect(joinAction.Find("ActionIcon").GetComponent<RectTransform>(), 45f, 19f, 47f, 47f * 41f / 36f, .05f);

var createLabel = createAction.Find("Label").GetComponent<UnityEngine.UI.Text>();
var joinLabel = joinAction.Find("Label").GetComponent<UnityEngine.UI.Text>();
Assert.That(createLabel.rectTransform.offsetMin.x, Is.EqualTo(108f).Within(.05f));
Assert.That(createLabel.rectTransform.offsetMin.y, Is.EqualTo(5f).Within(.05f));
Assert.That(createLabel.rectTransform.offsetMax.x, Is.EqualTo(-220f).Within(.05f));
Assert.That(createLabel.rectTransform.offsetMax.y, Is.EqualTo(5f).Within(.05f));
Assert.That(joinLabel.rectTransform.offsetMin.x, Is.EqualTo(103f).Within(.05f));
Assert.That(joinLabel.rectTransform.offsetMin.y, Is.EqualTo(2f).Within(.05f));
Assert.That(joinLabel.rectTransform.offsetMax.x, Is.EqualTo(-220f).Within(.05f));
Assert.That(joinLabel.rectTransform.offsetMax.y, Is.EqualTo(2f).Within(.05f));
Assert.That(createLabel.fontSize, Is.EqualTo(38));
Assert.That(joinLabel.fontSize, Is.EqualTo(38));
```

Implement `AssertTopLeftRect` in the test fixture:

```csharp
private static void AssertTopLeftRect(RectTransform rect, float left, float top, float width, float height, float tolerance)
{
    Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
    Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
    Assert.That(rect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
    Assert.That(rect.anchoredPosition.x, Is.EqualTo(left).Within(tolerance));
    Assert.That(rect.anchoredPosition.y, Is.EqualTo(-top).Within(tolerance));
    Assert.That(rect.sizeDelta.x, Is.EqualTo(width).Within(tolerance));
    Assert.That(rect.sizeDelta.y, Is.EqualTo(height).Within(tolerance));
}
```

- [ ] **Step 2: Run the focused test and verify red**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests.HomeRoomSelect_ActionBarsUseMeasuredRectsStretchSpritesAndOwnCreateInput' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\ActionContentVisualCenters\TDD-Layout-Red' `
  -TimeoutSeconds 900
```

Expected: one failed test because both icons still use the shared `.08/.5/48` geometry and both labels still use the shared offsets.

- [ ] **Step 3: Implement explicit top-left icon layout and independent label offsets**

Add this focused helper beside `PositionSprite`:

```csharp
private static void PositionSpriteTopLeft(Image value, float left, float top, float width)
{
    var rect = value.rectTransform;
    rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
    rect.pivot = new Vector2(0f, 1f);
    rect.anchoredPosition = new Vector2(left, -top);
    var spriteRect = value.sprite.rect;
    rect.sizeDelta = new Vector2(width, width * spriteRect.height / spriteRect.width);
    value.preserveAspect = true;
}
```

Replace only the two action-icon placements:

```csharp
PositionSpriteTopLeft(createIcon, 47f, 25f, 38f);
createLabel.rectTransform.offsetMin = new Vector2(108f, 5f);
createLabel.rectTransform.offsetMax = new Vector2(-220f, 5f);

PositionSpriteTopLeft(joinIcon, 45f, 19f, 47f);
joinLabel.rectTransform.offsetMin = new Vector2(103f, 2f);
joinLabel.rectTransform.offsetMax = new Vector2(-220f, 2f);
```

Keep both font sizes at `38` and do not change either action background Rect.

- [ ] **Step 4: Run the full view suite and verify green**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\ActionContentVisualCenters\TDD-Layout-Green' `
  -TimeoutSeconds 900
```

Expected: all `LanLobbyViewPlayModeTests` pass, including action hit-testing and discovery-prefill coverage.

- [ ] **Step 5: Commit the layout slice**

```powershell
git add -- 'Assets/Game/Runtime/Lobby/LanLobbyView.cs' 'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs'
git commit -m 'fix: align room action content by visual center'
```

---

### Task 2: Visible-pixel acceptance report

**Files:**
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1:270-307`
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1:18-58`
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1:375-452`

**Interfaces:**
- Consumes: existing `home-create-action-actual/reference.png` and `home-join-action-actual/reference.png`.
- Produces: `actionBars[].contentVisuals[]` in JSON and a `Home action content visible bounds` Markdown table.

- [ ] **Step 1: Add failing smoke assertions for all four visual elements**

Draw the fixture’s expected dark rectangles on the action crops and assert these entries:

```powershell
$expected = @(
    @{ bar='home-create-action'; name='icon';  x=47;  y=25; width=36;  height=37 },
    @{ bar='home-create-action'; name='label'; x=109; y=28; width=148; height=32 },
    @{ bar='home-join-action';   name='icon';  x=47;  y=20; width=44;  height=50 },
    @{ bar='home-join-action';   name='label'; x=104; y=31; width=150; height=34 }
)
```

For each row require `expectedBounds`, `referenceBounds`, `actualBounds`, `centerDeviationPx`, `sizeDeviationPx`, and `passed`. Make one fixture actual icon `2 px` right so the smoke proves a center deviation of `+2` and `passed=false`; require the remaining three rows to pass. Also assert that the Markdown table contains all four `bar/name` keys.

- [ ] **Step 2: Run the smoke test and verify red**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
```

Expected: exit `1` because `contentVisuals` and the Markdown table do not exist.

- [ ] **Step 3: Add deterministic dark-pixel bound measurement**

Use a single luminance rule for actual and reference: Alpha must be nonzero and integer luminance `(299*R + 587*G + 114*B) / 1000` must be `<45`. Search only these local rectangles to keep background ornament outside the measurement:

```powershell
$actionContentSpecs = @{
  'home-create-action' = @(
    @{ name='icon';  search=@{x=40;y=15;width=55;height=60}; expected=@{x=47;y=25;width=36;height=37} },
    @{ name='label'; search=@{x=100;y=20;width=170;height=50}; expected=@{x=109;y=28;width=148;height=32} }
  )
  'home-join-action' = @(
    @{ name='icon';  search=@{x=40;y=15;width=60;height=60}; expected=@{x=47;y=20;width=44;height=50} },
    @{ name='label'; search=@{x=95;y=20;width=170;height=50}; expected=@{x=104;y=31;width=150;height=34} }
  )
}
```

Add a `FindDarkBounds(Bitmap, Rectangle, int)` method to `LanLobbyVisualDiff`; it returns the minimal inclusive pixel bounds and throws when no qualifying pixel is found. Calculate visual center as `x + (width - 1) / 2.0` and `y + (height - 1) / 2.0`. A row passes only when both absolute center deltas are `<=1` and both absolute size deltas are `<=2`.

- [ ] **Step 4: Serialize JSON and Markdown evidence**

Append each row to its existing action-bar report:

```powershell
[pscustomobject][ordered]@{
    name = $contentSpec.name
    thresholdLumaExclusive = 45
    expectedBounds = $contentSpec.expected
    referenceBounds = ConvertTo-LanLobbyBoundsObject $referenceBounds
    actualBounds = ConvertTo-LanLobbyBoundsObject $actualBounds
    centerDeviationPx = [pscustomobject]@{ deltaX=$actualCenterX-$expectedCenterX; deltaY=$actualCenterY-$expectedCenterY }
    sizeDeviationPx = [pscustomobject]@{ deltaWidth=$actualBounds.Width-$expected.width; deltaHeight=$actualBounds.Height-$expected.height }
    passed = $passed
}
```

Add a Markdown table with Bar, Element, Expected, Reference, Actual, Center deviation, Size deviation, and Passed columns. Keep the existing whole-crop difference metrics informational; only the four `contentVisuals` rows govern this iteration’s visual-content acceptance.

- [ ] **Step 5: Run exporter smokes and verify green**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

Expected: each script prints `PASS` and exits `0`.

- [ ] **Step 6: Commit the report slice**

```powershell
git add -- 'scripts/ExportLanLobbyVisualDiff.ps1' 'scripts/TestLanLobbyVisualDiffSmoke.ps1'
git commit -m 'test: report room action visible bounds'
```

---

### Task 3: Real capture calibration and retained acceptance

**Files:**
- Modify if measurement requires correction: `Assets/Game/Runtime/Lobby/LanLobbyView.cs:323-407`
- Modify if correction changes the locked values: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs:116-153`
- Modify: `docs/TEST_PLAN.md:518-527`
- Modify: `docs/LAN-LOBBY-REPORT.md:80-115`

**Interfaces:**
- Consumes: Tasks 1–2, `Task006StandaloneBuild.BuildWindowsX64`, and `LanLobbyCaptureSuite`.
- Produces: retained focused test XML, build log, five real screenshots, visible-bound JSON/Markdown, overlays, heatmaps, and an updated provenance report.

- [ ] **Step 1: Run focused regression suites sequentially**

Run these filters into timestamped subdirectories under `Artifacts\LAN-LOBBY\ActionContentVisualCenters\Verification-<timestamp>`:

```powershell
ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests
ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests
ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests
ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests
```

Use `Invoke-UnityTests.ps1`, the fixed Unity executable, the correct EditMode/PlayMode platform for each fixture, and a 900-second timeout. Record total, failed, skipped, result, and log path from every XML/summary.

- [ ] **Step 2: Build the visible-capture Player**

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\ActionContentVisualCenters\WindowsStandalone\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -accept-apiupdate `
  -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\ActionContentVisualCenters\WindowsStandaloneBuild.log'
```

Require exit `0`, `BuildResult.Succeeded`, and zero build errors. Record every warning exactly.

- [ ] **Step 3: Capture five real 1920×1080 states**

Start the Player visibly:

```powershell
& 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\ActionContentVisualCenters\WindowsStandalone\ARKnoNIGHTS.exe' `
  -force-d3d11 -screen-width 1920 -screen-height 1080 `
  -lanLobbyCaptureSuite `
  -lanLobbyCaptureOutput 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\ActionContentVisualCenters\CapturesFinal' `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\ActionContentVisualCenters\PlayerCapture.log'
```

Require exit `0`, `[LanLobby][capture.completed] count=5`, five decodable non-black `1920×1080` PNGs, and `manifest.json`.

- [ ] **Step 4: Export the first real visible-bound report**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\ActionContentVisualCenters\CapturesFinal' `
  -OutputDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\ActionContentVisualCenters\VisualDiff' `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

Require both action background position/size deviations to remain zero. Read the four `contentVisuals` rows rather than judging the Sprite Rect centers.

- [ ] **Step 5: Apply measured corrections, with at most three capture-calibration cycles**

For each failed icon, set:

```text
newLeft = oldLeft - round(centerDeviationX)
newTop = oldTop - round(centerDeviationY)
newWidth = oldWidth * expectedVisibleWidth / actualVisibleWidth
```

Preserve Sprite aspect ratio, update the exact PlayMode assertion, rerun the view suite, rebuild, recapture into a new suffixed evidence directory, and re-export.

For each failed label, first recenter:

```text
newLeftOffset = oldLeftOffset - round(centerDeviationX)
newVerticalOffset = oldVerticalOffset + round(centerDeviationY)
```

Apply the same vertical value to `offsetMin.y` and `offsetMax.y`. If visible width or height still exceeds `2 px`, set:

```text
newFontSize = round(oldFontSize * min(expectedVisibleWidth / actualVisibleWidth, expectedVisibleHeight / actualVisibleHeight))
```

Then recenter and capture again. Stop after three materially different cycles if any row still fails; retain all measurements and report the blocker rather than lowering thresholds or moving the background.

- [ ] **Step 6: Inspect final screenshots and provenance**

Open:

```text
CapturesFinal/home.png
CapturesFinal/discovered-prefill.png
VisualDiff/home-create-action-actual.png
VisualDiff/home-create-action-reference.png
VisualDiff/home-create-action-overlay.png
VisualDiff/home-join-action-actual.png
VisualDiff/home-join-action-reference.png
VisualDiff/home-join-action-overlay.png
```

Confirm no decoration covers either action bar. Confirm `create_icon` and `join_icon` retain the approved `[uc]autochessouter` source paths, no forbidden Unpacked `$0`/`#0` source is present, and “创建同盟”/“加入同盟” remain classified as Unity Text rather than bitmap Sprite art.

- [ ] **Step 7: Update acceptance documentation**

Append the exact focused test totals, build result/warnings, capture directory, report directory, four actual/reference bounds, four center/size deltas, source paths, hashes, and manual screenshot findings to `docs/TEST_PLAN.md` and `docs/LAN-LOBBY-REPORT.md`. Explicitly retain the known unrelated full-suite Battle failures as outside this UI slice if they remain reproducible.

- [ ] **Step 8: Run final checks and commit**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
git diff --check
git status --short
git ls-files 'Artifacts/*' 'Temp/*'
```

Require all smoke scripts to exit `0`, no tracked generated evidence, no modification to `docs/unitbonds/`, and no unrelated diff. Commit only final layout corrections, their exact assertions, and documentation:

```powershell
git add -- 'Assets/Game/Runtime/Lobby/LanLobbyView.cs' 'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' 'docs/TEST_PLAN.md' 'docs/LAN-LOBBY-REPORT.md'
git commit -m 'test: verify room action visual centers'
```
