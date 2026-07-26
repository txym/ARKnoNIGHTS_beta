# Room Select Create Upper Decoration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recreate the internal Figure 9 decoration above the approved “创建同盟” action bar, excluding the cyan outer frame and preserving the accepted action bar and LAN behavior exactly.

**Architecture:** `LanLobbyView` will place each approved Sprite in explicit top-left coordinates relative to the existing `820×270` Create container. Repeated wing, bracket, and diamond instances remain independent uGUI `Image` nodes so rotation, overlap, provenance, and visible bounds can be tested. The existing visual-diff exporter will gain a tight `home-create-decoration` crop with cyan-component ROIs, while the current action-bar report remains the frozen regression baseline.

**Tech Stack:** Unity 2022.3.62f1c1, C# uGUI, NUnit/EditMode/Unity PlayMode tests, PowerShell 5.1, `System.Drawing`, visible D3D11 Windows Player capture.

## Global Constraints

- Use Unity `D:\2022.3.62f1c1\Editor\Unity.exe`; do not upgrade Unity, packages, the render pipeline, or the Input System.
- The accepted Create action Rect remains screen top-left `1154,453,717,99` at `1920×1080`; its background, icon, label, click Rect, sibling order, and `CreateRequested` behavior must not change.
- Do not modify Join decoration placement, the common title, the left identity area, Room UI, discovery, room-code prefill, creation, joining, or LAN transport.
- The cyan outer frame is excluded. Do not add a bitmap, vector, or code-generated replacement frame.
- All new visible bitmap instances must use existing non-`$0` Sprites imported from `G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess\[uc]autochessouter`.
- Reuse `room_select_create_logo` twice, `room_select_create_left_line` twice, and `room_select_dot` four times. The left wing and right bracket use `180°` rotation as specified below; do not create derived textures.
- Only the two low-Alpha wing `room_select_create_logo` instances may use explicit non-aspect width/height. Every other decoration Sprite keeps its source aspect ratio.
- Every decoration `Image.raycastTarget` is `false` and every decoration node renders before `CreateAction`.
- Visible-bound acceptance is center deviation at most `1 px` per axis and width/height deviation at most `2 px` for every measured component.
- Preserve the unrelated untracked `docs/unitbonds/` directory. Do not add, remove, or modify it.
- Run Unity tests, builds, and captures sequentially; never open this project path in two Unity processes at once.

---

## File Structure

| File | Responsibility in this change |
| --- | --- |
| `Assets/Game/Runtime/Lobby/LanLobbyView.cs` | Builds and positions the repeated Create decoration Sprite nodes without changing the accepted action bar. |
| `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs` | Verifies exact local Sprite Rects, rotations, source names, aspect policy, raycast policy, hierarchy, and frozen action behavior. |
| `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs` | Verifies the real capture manifest enumerates every repeated Create decoration instance and approved source. |
| `scripts/ExportLanLobbyVisualDiff.ps1` | Emits the tight Create-decoration crop, visible-bound rows, overlays, heatmap, and Markdown/JSON report. |
| `scripts/TestLanLobbyVisualDiffSmoke.ps1` | Supplies deterministic synthetic cyan components and verifies exporter bounds, pass/fail states, files, and material counts. |
| `docs/SPEC.md` | Records the confirmed player-visible scope: internal Create decoration only; outer frame excluded. |
| `docs/TEST_PLAN.md` | Records commands, retained evidence, exact results, and known unrelated failures. |
| `docs/LAN-LOBBY-REPORT.md` | Records the final visual comparison and exact material usage. |

### Task 1: Build the measured Create-decoration hierarchy

**Files:**
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs:323-350`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs:642-681`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs:114-169`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs:360-410`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs:413-450`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs:80-145`

**Interfaces:**
- Consumes: `BuildCreateSection(RectTransform parent, LanLobbyRect actionRect)`, `PositionSpriteTopLeft(Image,float,float,float)`, `Image(string,Transform,string)`, and the existing `CreateAction`.
- Produces: `LogoLeft`, `LogoRight`, `DotTopLeft`, `DotTopRight`, `DotBottomLeft`, `DotBottomRight`, `LineLeft`, `LineRight`, `MiddleIcon`, `Text01`, `Text02`, and `StartRoomDecoration` nodes under `Home/RoomSelect/Create`.

- [ ] **Step 1: Write the failing hierarchy and placement tests**

Add a test named `HomeRoomSelect_CreateDecorationUsesMeasuredRepeatedSpritesBehindFrozenAction` to `LanLobbyViewPlayModeTests`. Use top-left coordinates relative to the Create container:

```csharp
var expected = new[]
{
    new DecorationExpectation("LogoLeft", "room_select_create_logo", 272f, 30f, 107f, 135f, 180f, false),
    new DecorationExpectation("LogoRight", "room_select_create_logo", 539f, 30f, 109f, 133f, 0f, false),
    new DecorationExpectation("DotTopLeft", "room_select_dot", 381f, 21f, 19f, 19f, 0f, true),
    new DecorationExpectation("DotTopRight", "room_select_dot", 516f, 21f, 19f, 19f, 0f, true),
    new DecorationExpectation("DotBottomLeft", "room_select_dot", 381f, 157f, 19f, 19f, 0f, true),
    new DecorationExpectation("DotBottomRight", "room_select_dot", 516f, 157f, 19f, 19f, 0f, true),
    new DecorationExpectation("LineLeft", "room_select_create_left_line", 393f, 54f, 20f, 20f * 40f / 12f, 0f, true),
    new DecorationExpectation("LineRight", "room_select_create_left_line", 506f, 54f, 20f, 20f * 40f / 12f, 180f, true),
    new DecorationExpectation("MiddleIcon", "room_select_create_middleicon", 415f, 44f, 89f, 89f * 62f / 63f, 0f, true),
    new DecorationExpectation("Text01", "room_select_create_text_01", 415f, 137f, 89f, 89f * 9f / 63f, 0f, true),
    new DecorationExpectation("Text02", "room_select_create_text_02", 428f, 151f, 66f, 66f * 5f / 46f, 0f, true),
    new DecorationExpectation("StartRoomDecoration", "room_select_img_startroom", 416f, 16f, 87f, 87f * 8f / 64f, 0f, true)
};
```

For every expectation assert:

```csharp
AssertTopLeftRect(image.rectTransform, item.Left, item.Top, item.Width, item.Height, .05f);
Assert.That(image.sprite.name, Is.EqualTo(item.SpriteName));
Assert.That(Mathf.DeltaAngle(image.rectTransform.localEulerAngles.z, item.Rotation), Is.EqualTo(0f).Within(.05f));
Assert.That(image.preserveAspect, Is.EqualTo(item.PreserveAspect));
Assert.That(image.raycastTarget, Is.False);
Assert.That(image.transform.GetSiblingIndex(), Is.LessThan(createAction.GetSiblingIndex()));
```

Define the test-only immutable `DecorationExpectation` with the constructor and seven read-only properties matching the arguments above. Re-run the existing action assertions in the same test:

```csharp
AssertActionRect(createAction, create, layout.RoomSelectCreateAction, .05f);
AssertTopLeftRect(createAction.Find("ActionIcon").GetComponent<RectTransform>(), 47f, 25f, 38f, 38f, .05f);
Assert.That(createAction.Find("Label").GetComponent<Text>().rectTransform.offsetMin, Is.EqualTo(new Vector2(108f, 5f)));
Assert.That(createAction.Find("Label").GetComponent<Text>().rectTransform.offsetMax, Is.EqualTo(new Vector2(-220f, 5f)));
```

Update `AssertMappedRoomSelectSprites` to replace `Line_0`, `Line_1`, and `Logo` with the new node names and to include all four Create dots. Do not change any Join mappings.

In `LanLobbyCaptureSuitePlayModeTests`, add these Home-record assertions:

```csharp
Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_create_logo"), Is.EqualTo(2));
Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_create_left_line"), Is.EqualTo(2));
Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_dot"), Is.EqualTo(5));
Assert.That(home.spriteSources
    .Where(sprite => sprite.spriteName.StartsWith("room_select_create_", StringComparison.Ordinal)
        || sprite.spriteName == "room_select_dot"
        || sprite.spriteName == "room_select_img_startroom")
    .All(sprite => sprite.sourcePath.StartsWith("[uc]autochessouter/", StringComparison.Ordinal)
        && !sprite.sourcePath.Contains("$0")), Is.True);
```

The dot count is five because the screen also contains `RoomSelect/TitleDot`.

- [ ] **Step 2: Run the new tests to verify failure**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\CreateUpperDecoration\TDD-View-Red' `
  -TimeoutSeconds 900
```

Expected: FAIL because `LogoLeft`, `LogoRight`, and the four Create dot nodes do not exist and the old node names/layout remain.

- [ ] **Step 3: Implement the minimal measured hierarchy**

Replace only the decoration portion at the top of `BuildCreateSection`. Keep the existing `CreateAction` construction and every line configuring its icon, label, and click listener unchanged.

```csharp
var logoLeft = Image("LogoLeft", parent, "Home/room_select_create_logo");
PositionTopLeft(logoLeft.rectTransform, 272f, 30f, 107f, 135f);
logoLeft.preserveAspect = false;
logoLeft.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);

var logoRight = Image("LogoRight", parent, "Home/room_select_create_logo");
PositionTopLeft(logoRight.rectTransform, 539f, 30f, 109f, 133f);
logoRight.preserveAspect = false;

CreateDecorationSprite("DotTopLeft", parent, "Home/room_select_dot", 381f, 21f, 19f, 0f);
CreateDecorationSprite("DotTopRight", parent, "Home/room_select_dot", 516f, 21f, 19f, 0f);
CreateDecorationSprite("DotBottomLeft", parent, "Home/room_select_dot", 381f, 157f, 19f, 0f);
CreateDecorationSprite("DotBottomRight", parent, "Home/room_select_dot", 516f, 157f, 19f, 0f);
CreateDecorationSprite("LineLeft", parent, "Home/room_select_create_left_line", 393f, 54f, 20f, 0f);
CreateDecorationSprite("LineRight", parent, "Home/room_select_create_left_line", 506f, 54f, 20f, 180f);
CreateDecorationSprite("MiddleIcon", parent, "Home/room_select_create_middleicon", 415f, 44f, 89f, 0f);
CreateDecorationSprite("Text01", parent, "Home/room_select_create_text_01", 415f, 137f, 89f, 0f);
CreateDecorationSprite("Text02", parent, "Home/room_select_create_text_02", 428f, 151f, 66f, 0f);
CreateDecorationSprite("StartRoomDecoration", parent, "Home/room_select_img_startroom", 416f, 16f, 87f, 0f);
```

Add these focused helpers next to `PositionSpriteTopLeft`:

```csharp
private static Image CreateDecorationSprite(
    string name,
    Transform parent,
    string resource,
    float left,
    float top,
    float width,
    float rotation)
{
    var image = Image(name, parent, resource);
    PositionSpriteTopLeft(image, left, top, width);
    image.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
    image.raycastTarget = false;
    return image;
}

private static void PositionTopLeft(RectTransform value, float left, float top, float width, float height)
{
    value.anchorMin = value.anchorMax = new Vector2(0f, 1f);
    value.pivot = new Vector2(0f, 1f);
    value.anchoredPosition = new Vector2(left, -top);
    value.sizeDelta = new Vector2(width, height);
}
```

Set both Logo `raycastTarget=false` explicitly. Do not change the generic `Image` helper because other screens may rely on its current behavior.

- [ ] **Step 4: Run the focused view and capture-manifest tests**

Run sequentially:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\CreateUpperDecoration\TDD-View-Green' -TimeoutSeconds 900
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\CreateUpperDecoration\TDD-Capture-Green' -TimeoutSeconds 900
```

Expected: both fixtures PASS with no skipped tests. Confirm the action-bar assertions still pass and no test refers to `Line_0`, `Line_1`, or `Create/Logo`.

- [ ] **Step 5: Commit the hierarchy slice**

```powershell
git add -- 'Assets/Game/Runtime/Lobby/LanLobbyView.cs' 'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' 'Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs'
git commit -m 'feat: compose create upper decoration'
```

### Task 2: Add component-level Create-decoration visual evidence

**Files:**
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1:65-165`
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1:298-355`
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1:14-112`
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1:436-554`

**Interfaces:**
- Consumes: the `home.png` capture, Figure 9, capture manifest `spriteSources`, and existing `New-LanLobbyBitmapCrop`, `Resize-LanLobbyBitmap`, `Compare`, and overlay functions.
- Produces: `report.createDecoration`, twelve visible-component rows, `home-create-decoration-{actual,reference,overlay,heatmap}.png`, and material occurrence counts for all repeated Create Sprites.

- [ ] **Step 1: Extend the smoke fixture and assertions first**

Define the approved crop in `1920×1080` top-left coordinates:

```powershell
$createDecorationTarget = [ordered]@{ x=1296; y=252; width=390; height=179 }
```

Add a helper that maps a target-local rectangle into the synthetic native Figure 9 crop:

```powershell
function Fill-ScaledFixtureRectangle($Graphics, $Brush, $NativeCrop, $TargetSize, $Bounds)
{
    $x = $NativeCrop.x + [int][Math]::Round($Bounds.x * $NativeCrop.width / $TargetSize.width)
    $y = $NativeCrop.y + [int][Math]::Round($Bounds.y * $NativeCrop.height / $TargetSize.height)
    $right = $NativeCrop.x + [int][Math]::Round(($Bounds.x + $Bounds.width) * $NativeCrop.width / $TargetSize.width)
    $bottom = $NativeCrop.y + [int][Math]::Round(($Bounds.y + $Bounds.height) * $NativeCrop.height / $TargetSize.height)
    $Graphics.FillRectangle($Brush, $x, $y, [Math]::Max(1, $right-$x), [Math]::Max(1, $bottom-$y))
}
```

Use native fixture crop `{x=1382;y=260;width=417;height=187}` for the `2048×1118` synthetic Figure 9. This is the exact floor/ceiling conversion of measurement Rect `1419,268,427,191` by `2048/2102` on X and `1118/1149` on Y. Draw the twelve expected bounds below with a cyan brush. Draw the same bounds directly into actual `home.png` at target offset `1296,252`, except move `logo-left` right by `2 px` so the smoke test proves a failed center result.

Add repeated manifest rows for both `home` and `discovered-prefill`: two unique `room_select_create_logo` nodes, two unique `room_select_create_left_line` nodes, four unique Create `room_select_dot` nodes, one title dot, and the single middle/text/start-room nodes. Every source path must be `[uc]autochessouter/<sprite>.png`.

After export, assert:

```powershell
$decoration = $report.createDecoration
Assert-True ($decoration.name -eq 'home-create-decoration') 'Create decoration report name'
Assert-True (($decoration.actualRect.x -eq 1296) -and ($decoration.actualRect.y -eq 252) -and ($decoration.actualRect.width -eq 390) -and ($decoration.actualRect.height -eq 179)) 'Create decoration crop'
Assert-True (@($decoration.components).Count -eq 12) 'twelve Create decoration components'
$logoUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'room_select_create_logo')[0]
$lineUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'room_select_create_left_line')[0]
$dotUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'room_select_dot')[0]
Assert-True ($logoUsage.occurrenceCount -eq 4) 'two wing Sprites in each Home state'
Assert-True ($lineUsage.occurrenceCount -eq 4) 'two bracket Sprites in each Home state'
Assert-True ($dotUsage.occurrenceCount -eq 10) 'four Create dots plus title dot in each Home state'
```

Also assert `logo-left.centerDeviationPx.deltaX == 2`, `logo-left.passed == false`, every other component passes, and all four new PNGs exist.

- [ ] **Step 2: Run the smoke test to verify failure**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
```

Expected: FAIL because `report.createDecoration` and its four files do not yet exist.

- [ ] **Step 3: Add cyan visible-bound detection**

Add this C# method to the existing `LanLobbyVisualDiff` helper type:

```csharp
public static LanLobbyVisualBounds FindCyanBounds(
    Bitmap bitmap,
    Rectangle search,
    int minimumGreen,
    int minimumGreenOverRed,
    int minimumBlueOverRed)
{
    if (bitmap == null) throw new ArgumentNullException("bitmap");
    if (search.X < 0 || search.Y < 0 || search.Right > bitmap.Width || search.Bottom > bitmap.Height ||
        search.Width <= 0 || search.Height <= 0) throw new ArgumentOutOfRangeException("search");
    int minX=search.Right,minY=search.Bottom,maxX=-1,maxY=-1;
    for (int y=search.Y; y<search.Bottom; y++)
    for (int x=search.X; x<search.Right; x++)
    {
        Color pixel=bitmap.GetPixel(x,y);
        if (pixel.A == 0 || pixel.G < minimumGreen ||
            pixel.G < pixel.R + minimumGreenOverRed ||
            pixel.B < pixel.R + minimumBlueOverRed) continue;
        minX=Math.Min(minX,x); minY=Math.Min(minY,y);
        maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
    }
    if (maxX < minX || maxY < minY)
        throw new InvalidOperationException("No cyan visible pixels found in search rectangle " + search + ".");
    return new LanLobbyVisualBounds { X=minX, Y=minY, Width=maxX-minX+1, Height=maxY-minY+1 };
}
```

Use `minimumGreenOverRed=8` and `minimumBlueOverRed=5` for both reference and actual images.

- [ ] **Step 4: Define the tight crop and twelve semantic ROIs**

Add:

```powershell
$homeCreateDecoration = @{
  name='home-create-decoration'
  approvedTarget=@{x=1296;y=252;width=390;height=179}
  reference=@{x=1419;y=268;width=427;height=191}
}
$createDecorationContentSpecs = @(
  @{name='logo-left'; threshold=20; search=@{x=0;y=20;width=116;height=145}; expected=@{x=8;y=26;width=106;height=126}},
  @{name='logo-right'; threshold=20; search=@{x=270;y=20;width=120;height=145}; expected=@{x=276;y=27;width=108;height=124}},
  @{name='start-room'; threshold=35; search=@{x=145;y=5;width=100;height=24}; expected=@{x=153;y=13;width=84;height=9}},
  @{name='dot-top-left'; threshold=35; search=@{x=116;y=15;width=24;height=27}; expected=@{x=118;y=18;width=17;height=17}},
  @{name='dot-top-right'; threshold=35; search=@{x=249;y=15;width=24;height=27}; expected=@{x=253;y=19;width=17;height=16}},
  @{name='middle-icon'; threshold=35; search=@{x=150;y=34;width=92;height=100}; expected=@{x=152;y=41;width=87;height=86}},
  @{name='left-bracket'; threshold=35; search=@{x=124;y=50;width=28;height=75}; expected=@{x=130;y=58;width=18;height=54}},
  @{name='right-bracket'; threshold=35; search=@{x=240;y=50;width=25;height=75}; expected=@{x=243;y=58;width=18;height=54}},
  @{name='text-01'; threshold=35; search=@{x=145;y=128;width=100;height=19}; expected=@{x=152;y=134;width=88;height=13}},
  @{name='text-02'; threshold=35; search=@{x=160;y=147;width=75;height=10}; expected=@{x=164;y=147;width=66;height=7}},
  @{name='dot-bottom-left'; threshold=35; search=@{x=116;y=152;width=24;height=27}; expected=@{x=118;y=155;width=16;height=17}},
  @{name='dot-bottom-right'; threshold=35; search=@{x=249;y=152;width=24;height=27}; expected=@{x=253;y=155;width=17;height=17}}
)
```

The twelve rows are four dots, two wings, two brackets, `middle-icon`, two lower text layers, and `start-room`. Their search Rects are intentionally non-overlapping where adjacent cyan components would otherwise merge; the user confirmed this ROI correction after the original overlapping fixture proved geometrically unsatisfiable. The explicit count prevents a dot or label from being silently omitted.

- [ ] **Step 5: Export the crop, measurements, and Markdown**

Use the same crop/resize/overlay/heatmap lifecycle as action bars:

1. Crop actual `home.png` at `1296,252,390,179`.
2. Convert and crop Figure 9 native rect `1419,268,427,191`.
3. Resize the reference crop to `390×179`.
4. For each content spec, call `FindCyanBounds` on the same ROI in both images.
5. Compute actual-minus-expected center and size deltas.
6. Set `passed` only when both absolute center deltas are `≤1` and both absolute size deltas are `≤2`.
7. Store `thresholdMinimumGreen`, the two color deltas, expected/reference/actual bounds, center/size deltas, and `passed`.
8. Save `home-create-decoration-actual.png`, `-reference.png`, `-overlay.png`, and `-heatmap.png`.

Add `createDecoration=$createDecorationReport` to the root JSON. Add a `## Home Create upper decoration` Markdown section with crop coordinates and one row per component. Keep the existing action-bar section byte-for-byte compatible.

- [ ] **Step 6: Run all evidence smoke tests**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

Expected: all three exit `0`; the synthetic `logo-left` row is the only intentional component failure, and no test writes to the real reference directory.

- [ ] **Step 7: Commit the evidence slice**

```powershell
git add -- 'scripts/ExportLanLobbyVisualDiff.ps1' 'scripts/TestLanLobbyVisualDiffSmoke.ps1'
git commit -m 'test: report create decoration visual bounds'
```

### Task 3: Capture, calibrate, and freeze the real Player result

**Files:**
- Modify after measured correction: `Assets/Game/Runtime/Lobby/LanLobbyView.cs:323-350`
- Modify with the same corrected values: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2, `Task006StandaloneBuild.BuildWindowsX64`, and `LanLobbyCaptureSuite`.
- Produces: focused XML, a Windows build log, five real screenshots, twelve component rows, four Create-decoration comparison images, and final frozen placement assertions.

- [ ] **Step 1: Run the four focused Unity fixtures sequentially**

Create `Artifacts\LAN-LOBBY\CreateUpperDecoration\Verification-<timestamp>\{Layout,View,Capture,Controller}` and invoke:

```text
EditMode  ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests
PlayMode  ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests
PlayMode  ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests
PlayMode  ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests
```

Use `scripts\Invoke-UnityTests.ps1`, the fixed Unity path, and `-TimeoutSeconds 900`. Require a complete `test-run` XML for each fixture; record total, passed, failed, skipped, result, and log path.

- [ ] **Step 2: Build the visible-capture Player**

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateUpperDecoration\WindowsStandalone\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -accept-apiupdate `
  -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateUpperDecoration\WindowsStandaloneBuild.log'
```

Require exit `0`, `BuildResult.Succeeded`, and zero build errors. Record warnings without suppressing them.

- [ ] **Step 3: Capture five real `1920×1080` states**

Run the Player visibly, not hidden:

```powershell
& 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateUpperDecoration\WindowsStandalone\ARKnoNIGHTS.exe' `
  -force-d3d11 -screen-width 1920 -screen-height 1080 `
  -lanLobbyCaptureSuite `
  -lanLobbyCaptureOutput 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateUpperDecoration\Captures-1' `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateUpperDecoration\PlayerCapture-1.log'
```

Require exit `0`, `[LanLobby][capture.completed] count=5`, five decodable non-empty `1920×1080` PNGs, and `manifest.json`.

- [ ] **Step 4: Export the first real decoration report**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateUpperDecoration\Captures-1' `
  -OutputDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateUpperDecoration\VisualDiff-1' `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

Require both action-bar reports and all four accepted icon/label rows to retain zero baseline movement and their previous pass status. Read all twelve `createDecoration.components` rows.

- [ ] **Step 5: Apply a bounded measurement-driven calibration**

For each failed aspect-preserving Sprite:

```text
newLeft = oldLeft - round(centerDeviationX)
newTop = oldTop - round(centerDeviationY)
newWidth = oldWidth * min(expectedVisibleWidth / actualVisibleWidth,
                          expectedVisibleHeight / actualVisibleHeight)
```

For a failed wing Logo, update axes independently:

```text
newLeft = oldLeft - round(centerDeviationX)
newTop = oldTop - round(centerDeviationY)
newWidth = oldWidth * expectedVisibleWidth / actualVisibleWidth
newHeight = oldHeight * expectedVisibleHeight / actualVisibleHeight
```

Round positions and sizes to the nearest `0.5 px`, copy the exact new values into the PlayMode expectations, run `LanLobbyViewPlayModeTests`, rebuild, capture into `Captures-2` or `Captures-3`, and export to the matching `VisualDiff-*` directory. Stop after at most three materially different capture cycles. Do not relax thresholds, move the Create action bar, or add the outer frame.

- [ ] **Step 6: Inspect the final visual evidence**

Open:

```text
Captures-<final>/home.png
Captures-<final>/discovered-prefill.png
VisualDiff-<final>/home-create-decoration-actual.png
VisualDiff-<final>/home-create-decoration-reference.png
VisualDiff-<final>/home-create-decoration-overlay.png
VisualDiff-<final>/home-create-decoration-heatmap.png
VisualDiff-<final>/home-create-action-overlay.png
```

Confirm all internal content is present, the two wings read as one symmetric background motif, both brackets face inward, all four dots are visible, the central layers overlap like Figure 9, no outer frame was added, and the accepted action bar is unobstructed.

- [ ] **Step 7: Commit the final measured placement**

```powershell
git add -- 'Assets/Game/Runtime/Lobby/LanLobbyView.cs' 'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs'
git commit -m 'fix: calibrate create upper decoration'
```

### Task 4: Document and verify the accepted slice

**Files:**
- Modify: `docs/SPEC.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/LAN-LOBBY-REPORT.md`

**Interfaces:**
- Consumes: final Task 3 test XML, build/capture logs, JSON/Markdown report, screenshots, and current Git diff.
- Produces: auditable acceptance documentation and a verified final branch state without integrating unrelated failures.

- [ ] **Step 1: Update the behavioral specification**

Add a Home Create-decoration acceptance paragraph to `docs/SPEC.md` stating:

- only the internal upper decoration is required in this slice;
- the cyan outer frame is excluded;
- the accepted Create bar is immutable;
- the two wings and two brackets reuse their source Sprites with the documented rotations;
- four `room_select_dot` instances are used;
- no `$0` Unpacked asset or generated replacement bitmap is allowed.

- [ ] **Step 2: Record exact retained evidence**

Append to `docs/TEST_PLAN.md` and `docs/LAN-LOBBY-REPORT.md`:

- focused fixture totals/result/log paths;
- build result, error/warning count, and build path;
- final capture and visual-report directories;
- all twelve expected/reference/actual bounds and center/size deviations;
- Create action background/icon/label frozen-baseline results;
- exact Resources path, source-relative path, SHA-256, and occurrence count for each used Sprite;
- manual findings for the actual/reference/overlay images;
- explicit statement that the outer frame was intentionally not reproduced.

- [ ] **Step 3: Run repository-wide EditMode and PlayMode suites**

Run both suites sequentially into `Artifacts\LAN-LOBBY\CreateUpperDecoration\FullSuite-<timestamp>`. Do not modify unrelated tests to make the suites green. Compare any failures with the retained baseline:

```text
EditMode:
- BattleCoreEditModeTests.Fixture_InvalidInputMatrixReturnsStructuredErrors
- BattleCoreEditModeTests.RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources

PlayMode:
- PreparationBattleLoopPlayModeTests.SampleScene_AutoLoopsPreparationToBattleAndBackWithoutWritingCombatResultToPlayerState
```

If those same failures remain, record them as unchanged and outside this UI slice. Any new Lobby or Create-decoration failure blocks completion.

- [ ] **Step 4: Run final smoke and focused verification**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

Re-run the four focused Unity fixtures into a fresh `Verification-Final-<timestamp>` directory. Require all focused tests and all twelve Create-decoration component rows to pass.

- [ ] **Step 5: Review provenance and the final diff**

```powershell
git diff --check
git status --short
git diff --stat
git ls-files 'Artifacts/*' 'Temp/*'
git diff -- 'Assets/Game/Runtime/Lobby/LanLobbyView.cs' 'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' 'Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs' 'scripts/ExportLanLobbyVisualDiff.ps1' 'scripts/TestLanLobbyVisualDiffSmoke.ps1' 'docs/SPEC.md' 'docs/TEST_PLAN.md' 'docs/LAN-LOBBY-REPORT.md'
```

Require:

- no tracked `Artifacts/`, `Temp/`, build output, or screenshots;
- no change under `docs/unitbonds/`;
- no asset bytes or `.meta` files changed;
- no Join-layout or network code change;
- no forbidden `$0`/`#0` Unpacked source in the final manifest;
- the final report lists two Logo instances per Home state, two brackets per Home state, and five total dots per Home state including the title dot.

- [ ] **Step 6: Commit documentation**

```powershell
git add -- 'docs/SPEC.md' 'docs/TEST_PLAN.md' 'docs/LAN-LOBBY-REPORT.md'
git commit -m 'docs: record create decoration evidence'
```

- [ ] **Step 7: Perform completion verification**

Invoke `superpowers:verification-before-completion` and independently review the final diff for uGUI hierarchy, rotation, raycast behavior, source provenance, capture/report accuracy, and regressions. Do not claim repository-wide green status while the three known unrelated failures remain.
