# Room Select Create Decoration Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the misidentified Create decoration strips with a four-piece tapered wing motif, build the Create outline from nine overlapping `doc_frame_line` Sprites, and produce repeatable real-Player visual evidence without moving the approved action bars.

**Architecture:** Import the two approved non-`$0` source PNGs through the existing lobby importer and provenance map. Build every new visible element as an explicit uGUI `Image` with a center pivot, remove the code-native `PanelFrame`, and keep the action bars as the last children. Extend the visual exporter with connected-component wing measurement and a separate frame-continuity report, then calibrate from at most three new 1920×1080 Player captures.

**Tech Stack:** Unity 2022.3.62f1c1, C# uGUI, NUnit EditMode/PlayMode tests, PowerShell 5.1, `System.Drawing`, Windows x64 Player capture with Direct3D 11.

## Global Constraints

- Work only in `G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby` on `codex/lan-lobby`.
- Unity Editor path is exactly `D:\2022.3.62f1c1\Editor\Unity.exe`; do not upgrade Unity or packages.
- All added bitmap bytes must come unchanged from `G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess\[uc]autochessouter`.
- Use only `img_pointer.png` and `doc_frame_line.png`, never a `$0` variant, generated replacement, edited PNG, or unrelated Combined atlas.
- `room_select_create_logo` must have zero runtime occurrences in the Create hierarchy.
- The wing motif uses exactly four `img_pointer` instances; the Create outline uses exactly nine `doc_frame_line` instances.
- Every visible outline segment must be one of those nine Sprite instances. No code-drawn line, preset rectangle border, pure-color `Image`, vector line, or replacement frame is allowed.
- The approved Create action is `x=1154, y=453, width=717, height=99` in 1920×1080 top-left screen coordinates.
- The approved Join action is `x=1154, y=876, width=717, height=99` in 1920×1080 top-left screen coordinates.
- Create/Join action backgrounds, icons, labels, click Rects, events, Join decoration, room discovery, room-code prefill, and LAN behavior are immutable in this slice.
- All decoration Images use `raycastTarget=false`; action bars remain the last children of their sections.
- Generate real visible `-force-d3d11` Player captures at 1920×1080; screenshots, builds, logs, and reports remain under ignored `Artifacts/`.
- Run Unity processes sequentially. Do not open this worktree in more than one Unity process at a time.
- A new correction phase permits at most three materially different build/capture/report cycles. Do not relax thresholds to manufacture a pass.

---

### Task 1: Import and map the two approved Sprite sources

**Files:**
- Modify: `scripts/ImportLobbyAssets.ps1`
- Modify: `docs/references/ui/lobby/ASSET_MAP.md`
- Create from approved source: `Assets/Resources/UI/Lobby/Home/img_pointer.png`
- Create through Unity import: `Assets/Resources/UI/Lobby/Home/img_pointer.png.meta`
- Create from approved source: `Assets/Resources/UI/Lobby/Home/doc_frame_line.png`
- Create through Unity import: `Assets/Resources/UI/Lobby/Home/doc_frame_line.png.meta`

**Interfaces:**
- Consumes: `ImportLobbyAssets.ps1 -SourceRoot 'G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess'` and `Get-LanLobbyAssetMap`.
- Produces: `Resources.Load<Sprite>("UI/Lobby/Home/img_pointer")`, `Resources.Load<Sprite>("UI/Lobby/Home/doc_frame_line")`, and approved source mappings for capture evidence.

- [ ] **Step 1: Run a red whitelist/provenance precheck**

```powershell
$required = @('img_pointer', 'doc_frame_line')
$importer = Get-Content -Raw -LiteralPath .\scripts\ImportLobbyAssets.ps1
$map = Get-Content -Raw -LiteralPath .\docs\references\ui\lobby\ASSET_MAP.md
foreach ($name in $required) {
    if ($importer -notmatch ("'" + [regex]::Escape($name) + "'")) { throw "missing importer whitelist entry: $name" }
    if ($map -notmatch ([regex]::Escape($name + '.png'))) { throw "missing asset-map entry: $name" }
}
```

Expected before implementation: non-zero exit with `missing importer whitelist entry: img_pointer`.

- [ ] **Step 2: Add exact importer and asset-map entries**

Append both names to `$roomSelectAssetNames` in `scripts/ImportLobbyAssets.ps1`:

```powershell
'img_pointer', 'doc_frame_line'
```

Add these rows to `docs/references/ui/lobby/ASSET_MAP.md`:

```markdown
| img_pointer.png | [uc]autochessouter/img_pointer.png | UI/Lobby/Home/img_pointer | Create tapered wing segment | Preserve |
| doc_frame_line.png | [uc]autochessouter/doc_frame_line.png | UI/Lobby/Home/doc_frame_line | Create outline segment | Preserve |
```

- [ ] **Step 3: Run the importer against the approved source**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ImportLobbyAssets.ps1 `
  -SourceRoot 'G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess'
```

Expected: exit `0`; output reports two additional Home imports. Confirm the script never resolves a `$0` filename.

- [ ] **Step 4: Let Unity import the two PNGs**

```powershell
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -quit -accept-apiupdate `
  -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateDecorationCorrection\AssetImport.log'
```

Expected: exit `0`; both `.meta` files exist; the log has no compilation error.

- [ ] **Step 5: Prove imported bytes match the approved source**

```powershell
$pairs = @(
  @('img_pointer.png', 'Assets\Resources\UI\Lobby\Home\img_pointer.png'),
  @('doc_frame_line.png', 'Assets\Resources\UI\Lobby\Home\doc_frame_line.png')
)
$sourceRoot = 'G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess\[uc]autochessouter'
foreach ($pair in $pairs) {
    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $sourceRoot $pair[0])).Hash
    $importedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pair[1]).Hash
    if ($sourceHash -ne $importedHash) { throw "hash mismatch: $($pair[0])" }
}
```

Expected: exit `0`.

- [ ] **Step 6: Re-run the whitelist/provenance precheck**

Expected: exit `0`.

- [ ] **Step 7: Commit the asset slice**

```powershell
git add -- `
  'scripts/ImportLobbyAssets.ps1' `
  'docs/references/ui/lobby/ASSET_MAP.md' `
  'Assets/Resources/UI/Lobby/Home/img_pointer.png' `
  'Assets/Resources/UI/Lobby/Home/img_pointer.png.meta' `
  'Assets/Resources/UI/Lobby/Home/doc_frame_line.png' `
  'Assets/Resources/UI/Lobby/Home/doc_frame_line.png.meta'
git commit -m 'feat: import create decoration segments'
```

### Task 2: Replace the incorrect hierarchy with centered Sprite composition

**Files:**
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`

**Interfaces:**
- Consumes: `Home/img_pointer`, `Home/doc_frame_line`, existing `Image`, `Rect`, `PositionBottomLeft`, and frozen action layout.
- Produces: `RoomSelect/Create/CreateFrame/*`, `RoomSelect/Create/Wings/*`, corrected central decoration nodes, no `RoomSelect/PanelFrame`, and unchanged action events.

- [ ] **Step 1: Replace the old View expectations with failing structure assertions**

In `HomeRoomSelect_CreateDecorationUsesMeasuredRepeatedSpritesBehindFrozenAction`, assert that
`RoomSelect/PanelFrame`, `Create/LogoLeft`, and `Create/LogoRight` are absent.

Add oriented expectations using center coordinates measured from the Create parent top-left:

```csharp
var expected = new[]
{
    new OrientedDecorationExpectation("Wings/WingLeftUpper", "img_pointer", 325f, 62.5f, 120f, 120f * 23f / 324f, 164f, true),
    new OrientedDecorationExpectation("Wings/WingLeftLower", "img_pointer", 325f, 123.5f, 120f, 120f * 23f / 324f, 196f, true),
    new OrientedDecorationExpectation("Wings/WingRightUpper", "img_pointer", 598f, 62.5f, 115f, 115f * 23f / 324f, 16f, true),
    new OrientedDecorationExpectation("Wings/WingRightLower", "img_pointer", 598f, 123.5f, 115f, 115f * 23f / 324f, 344f, true),
    new OrientedDecorationExpectation("DotTopLeft", "room_select_dot", 390.5f, 30.5f, 17f, 17f, 0f, true),
    new OrientedDecorationExpectation("DotTopRight", "room_select_dot", 525.5f, 31.5f, 17f, 17f, 0f, true),
    new OrientedDecorationExpectation("DotBottomLeft", "room_select_dot", 390f, 167f, 16f, 16f, 0f, true),
    new OrientedDecorationExpectation("DotBottomRight", "room_select_dot", 525.5f, 167.5f, 17f, 17f, 0f, true),
    new OrientedDecorationExpectation("LineLeft", "room_select_create_left_line", 402.1f, 89f, 16.2f, 54f, 0f, true),
    new OrientedDecorationExpectation("LineRight", "room_select_create_left_line", 515.1f, 89f, 16.2f, 54f, 180f, true),
    new OrientedDecorationExpectation("MiddleIcon", "room_select_create_middleicon", 459.5f, 87.81f, 87f, 87f * 62f / 63f, 0f, true),
    new OrientedDecorationExpectation("Text01", "room_select_create_text_01", 460f, 144.29f, 88f, 88f * 9f / 63f, 0f, true),
    new OrientedDecorationExpectation("Text02", "room_select_create_text_02", 461f, 154.59f, 66f, 66f * 5f / 46f, 0f, true),
    new OrientedDecorationExpectation("StartRoomDecoration", "room_select_img_startroom", 459f, 22.25f, 84f, 84f * 8f / 64f, 0f, true)
};
```

Add nine frame expectations:

```csharp
var frameExpected = new[]
{
    new OrientedDecorationExpectation("CreateFrame/TopLeft", "doc_frame_line", 312f, -18f, 380f, 12f, 0f, false),
    new OrientedDecorationExpectation("CreateFrame/TopRight", "doc_frame_line", 635.5f, -18f, 353f, 12f, 0f, false),
    new OrientedDecorationExpectation("CreateFrame/BottomLeft", "doc_frame_line", 312f, 344f, 380f, 12f, 180f, false),
    new OrientedDecorationExpectation("CreateFrame/BottomRight", "doc_frame_line", 649f, 344f, 380f, 12f, 180f, false),
    new OrientedDecorationExpectation("CreateFrame/LeftUpper", "doc_frame_line", 128f, 76f, 200f, 12f, 90f, false),
    new OrientedDecorationExpectation("CreateFrame/LeftLower", "doc_frame_line", 128f, 250f, 200f, 12f, 90f, false),
    new OrientedDecorationExpectation("CreateFrame/RightUpper", "doc_frame_line", 833f, 103f, 200f, 12f, 270f, false),
    new OrientedDecorationExpectation("CreateFrame/RightLower", "doc_frame_line", 833f, 250f, 200f, 12f, 270f, false),
    new OrientedDecorationExpectation("CreateFrame/TopRightChamfer", "doc_frame_line", 826f, -10f, 40f, 12f, 45f, false)
};
```

The assertion helper must check top-left anchors, pivot `(0.5, 0.5)`, center-position semantics,
unrotated `sizeDelta`, `localEulerAngles.z`, Sprite name, `preserveAspect`, and
`raycastTarget=false`. Assert `CreateFrame` and `Wings` have no `Graphic`; assert
`CreateAction` is still the last child and all frozen action values remain unchanged.

Replace `DecorationExpectation` with:

```csharp
private sealed class OrientedDecorationExpectation
{
    public OrientedDecorationExpectation(
        string path,
        string spriteName,
        float centerX,
        float centerTop,
        float width,
        float height,
        float rotation,
        bool preserveAspect)
    {
        Path = path;
        SpriteName = spriteName;
        CenterX = centerX;
        CenterTop = centerTop;
        Width = width;
        Height = height;
        Rotation = rotation;
        PreserveAspect = preserveAspect;
    }

    public string Path { get; }
    public string SpriteName { get; }
    public float CenterX { get; }
    public float CenterTop { get; }
    public float Width { get; }
    public float Height { get; }
    public float Rotation { get; }
    public bool PreserveAspect { get; }
}

private static void AssertOrientedDecoration(
    RectTransform create,
    OrientedDecorationExpectation expected,
    float tolerance = .05f)
{
    var node = create.Find(expected.Path);
    Assert.That(node, Is.Not.Null, expected.Path);
    var image = node.GetComponent<UnityEngine.UI.Image>();
    var rect = node.GetComponent<RectTransform>();
    Assert.That(image.sprite.name, Is.EqualTo(expected.SpriteName));
    Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
    Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
    Assert.That(rect.pivot, Is.EqualTo(new Vector2(.5f, .5f)));
    Assert.That(rect.anchoredPosition.x, Is.EqualTo(expected.CenterX).Within(tolerance));
    Assert.That(rect.anchoredPosition.y, Is.EqualTo(-expected.CenterTop).Within(tolerance));
    Assert.That(rect.sizeDelta.x, Is.EqualTo(expected.Width).Within(tolerance));
    Assert.That(rect.sizeDelta.y, Is.EqualTo(expected.Height).Within(tolerance));
    Assert.That(Mathf.DeltaAngle(rect.localEulerAngles.z, expected.Rotation), Is.EqualTo(0f).Within(tolerance));
    Assert.That(image.preserveAspect, Is.EqualTo(expected.PreserveAspect));
    Assert.That(image.raycastTarget, Is.False);
}
```

- [ ] **Step 2: Update CaptureSuite expectations before runtime**

Replace the old occurrences and geometry expectations with:

```csharp
Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_create_logo"), Is.Zero);
Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "img_pointer"), Is.EqualTo(4));
Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "doc_frame_line"), Is.EqualTo(9));
Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_create_left_line"), Is.EqualTo(2));
Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_dot"), Is.EqualTo(5));
Assert.That(home.codeNativeGeometry.Any(item =>
    item.node.StartsWith("LanLobbyRoot/Home/RoomSelect/PanelFrame", StringComparison.Ordinal)), Is.False);
```

Also assert all `img_pointer` and `doc_frame_line` source paths start with
`[uc]autochessouter/` and contain neither `$0` nor `#0`.

- [ ] **Step 3: Run both PlayMode fixtures and verify they fail for the old hierarchy**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\CreateDecorationCorrection\Red-View' `
  -TimeoutSeconds 900
```

Repeat for `ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests`.

Expected: both runs complete with test failures naming the old `PanelFrame`/missing `Wings` or missing `CreateFrame`.

- [ ] **Step 4: Add a center-pivot composition helper**

Add this helper near the existing decoration helpers in `LanLobbyView.cs`:

```csharp
private static Image CreateOrientedDecorationSprite(
    string name,
    Transform parent,
    string spriteName,
    float centerX,
    float centerTop,
    float width,
    float height,
    float rotation,
    bool preserveAspect,
    Color color)
{
    var image = Image(name, parent, spriteName);
    var rect = image.rectTransform;
    rect.anchorMin = new Vector2(0f, 1f);
    rect.anchorMax = new Vector2(0f, 1f);
    rect.pivot = new Vector2(.5f, .5f);
    rect.anchoredPosition = new Vector2(centerX, -centerTop);
    rect.sizeDelta = new Vector2(width, height);
    rect.localEulerAngles = new Vector3(0f, 0f, rotation);
    image.preserveAspect = preserveAspect;
    image.color = color;
    image.raycastTarget = false;
    return image;
}
```

- [ ] **Step 5: Remove the old code-native frame and incorrect Logo nodes**

Delete the `BuildRoomSelectFrame(roomSelect)` call, the `BuildRoomSelectFrame` method, and the now-unused
`FrameLine` helper if no other caller remains. Delete `LogoLeft` and `LogoRight` construction from
`BuildCreateSection`.

- [ ] **Step 6: Build the initial nine-segment frame**

Create an empty `CreateFrame` Rect stretched to `Create`; add the nine `doc_frame_line` instances using
the frame expectations from Step 1 and:

```csharp
var createFrame = Rect("CreateFrame", parent);
Stretch(createFrame);
var frameTint = new Color(.35f, .60f, .55f, .28f);
CreateOrientedDecorationSprite("TopLeft", createFrame, "Home/doc_frame_line", 312f, -18f, 380f, 12f, 0f, false, frameTint);
CreateOrientedDecorationSprite("TopRight", createFrame, "Home/doc_frame_line", 635.5f, -18f, 353f, 12f, 0f, false, frameTint);
CreateOrientedDecorationSprite("BottomLeft", createFrame, "Home/doc_frame_line", 312f, 344f, 380f, 12f, 180f, false, frameTint);
CreateOrientedDecorationSprite("BottomRight", createFrame, "Home/doc_frame_line", 649f, 344f, 380f, 12f, 180f, false, frameTint);
CreateOrientedDecorationSprite("LeftUpper", createFrame, "Home/doc_frame_line", 128f, 76f, 200f, 12f, 90f, false, frameTint);
CreateOrientedDecorationSprite("LeftLower", createFrame, "Home/doc_frame_line", 128f, 250f, 200f, 12f, 90f, false, frameTint);
CreateOrientedDecorationSprite("RightUpper", createFrame, "Home/doc_frame_line", 833f, 103f, 200f, 12f, 270f, false, frameTint);
CreateOrientedDecorationSprite("RightLower", createFrame, "Home/doc_frame_line", 833f, 250f, 200f, 12f, 270f, false, frameTint);
CreateOrientedDecorationSprite("TopRightChamfer", createFrame, "Home/doc_frame_line", 826f, -10f, 40f, 12f, 45f, false, frameTint);
```

Create every segment through `CreateOrientedDecorationSprite`. Do not add any `Graphic` to the group.

- [ ] **Step 7: Build the initial four-piece wings and corrected center**

Create an empty `Wings` Rect stretched to `Create`; add the four `img_pointer` instances using the
expectations from Step 1 and:

```csharp
var wings = Rect("Wings", parent);
Stretch(wings);
var wingTint = new Color(.35f, .65f, .58f, .45f);
CreateOrientedDecorationSprite("WingLeftUpper", wings, "Home/img_pointer", 325f, 62.5f, 120f, 120f * 23f / 324f, 164f, true, wingTint);
CreateOrientedDecorationSprite("WingLeftLower", wings, "Home/img_pointer", 325f, 123.5f, 120f, 120f * 23f / 324f, 196f, true, wingTint);
CreateOrientedDecorationSprite("WingRightUpper", wings, "Home/img_pointer", 598f, 62.5f, 115f, 115f * 23f / 324f, 16f, true, wingTint);
CreateOrientedDecorationSprite("WingRightLower", wings, "Home/img_pointer", 598f, 123.5f, 115f, 115f * 23f / 324f, 344f, true, wingTint);

CreateOrientedDecorationSprite("DotTopLeft", parent, "Home/room_select_dot", 390.5f, 30.5f, 17f, 17f, 0f, true, Color.white);
CreateOrientedDecorationSprite("DotTopRight", parent, "Home/room_select_dot", 525.5f, 31.5f, 17f, 17f, 0f, true, Color.white);
CreateOrientedDecorationSprite("DotBottomLeft", parent, "Home/room_select_dot", 390f, 167f, 16f, 16f, 0f, true, Color.white);
CreateOrientedDecorationSprite("DotBottomRight", parent, "Home/room_select_dot", 525.5f, 167.5f, 17f, 17f, 0f, true, Color.white);
CreateOrientedDecorationSprite("LineLeft", parent, "Home/room_select_create_left_line", 402.1f, 89f, 16.2f, 54f, 0f, true, Color.white);
CreateOrientedDecorationSprite("LineRight", parent, "Home/room_select_create_left_line", 515.1f, 89f, 16.2f, 54f, 180f, true, Color.white);
CreateOrientedDecorationSprite("MiddleIcon", parent, "Home/room_select_create_middleicon", 459.5f, 87.81f, 87f, 87f * 62f / 63f, 0f, true, Color.white);
CreateOrientedDecorationSprite("Text01", parent, "Home/room_select_create_text_01", 460f, 144.29f, 88f, 88f * 9f / 63f, 0f, true, Color.white);
CreateOrientedDecorationSprite("Text02", parent, "Home/room_select_create_text_02", 461f, 154.59f, 66f, 66f * 5f / 46f, 0f, true, Color.white);
CreateOrientedDecorationSprite("StartRoomDecoration", parent, "Home/room_select_img_startroom", 459f, 22.25f, 84f, 84f * 8f / 64f, 0f, true, Color.white);
```

Create all central nodes through the centered helper using `Color.white`. Preserve the exact sibling
order from the design, then construct `CreateAction` last without changing any of its existing code.

- [ ] **Step 8: Run focused PlayMode tests**

Run sequentially:

```powershell
$fixtures = @(
  'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests',
  'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests',
  'ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests'
)
foreach ($fixture in $fixtures) {
  $leaf = ($fixture -split '\.')[-1]
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
    -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
    -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
    -TestPlatform PlayMode `
    -TestFilter $fixture `
    -OutputDirectory (Join-Path 'Artifacts\LAN-LOBBY\CreateDecorationCorrection\Green-Structure' $leaf) `
    -TimeoutSeconds 900
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: complete NUnit XML; all tests pass; no skipped test; no new compilation error.

- [ ] **Step 9: Commit the runtime structure**

```powershell
git add -- `
  'Assets/Game/Runtime/Lobby/LanLobbyView.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs'
git commit -m 'fix: rebuild create decoration hierarchy'
```

### Task 3: Correct the visual detector for wings and stitched frame

**Files:**
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1`
- Modify: `scripts/ExportLanLobbyVisualDiff.ps1`

**Interfaces:**
- Consumes: Home capture manifest, Figure 9, fixed action crops, the 390×179 internal-decoration crop, and a new 717×374 Create-frame crop.
- Produces: corrected 12-row `createDecoration.components`, `createFrame.edges`, four existing decoration images, four new frame images, JSON, Markdown, and material-usage evidence.

- [ ] **Step 1: Rewrite the smoke fixture first**

Replace `logo-left` and `logo-right` with:

```powershell
[ordered]@{ name='wing-left';  mode='two-largest-components'; threshold=20; greenOverRed=4; blueOverRed=3; search=@{x=0;y=35;width=116;height=105}; expected=@{x=7;y=35;width=107;height=105} },
[ordered]@{ name='wing-right'; mode='two-largest-components'; threshold=20; greenOverRed=4; blueOverRed=3; search=@{x=270;y=35;width=120;height=105}; expected=@{x=283;y=35;width=101;height=105} },
```

Keep the ten non-logo expected bounds:

```text
start-room 153,13,84,9
dot-top-left 118,18,17,17
dot-top-right 253,19,17,16
middle-icon 152,41,87,86
left-bracket 130,58,18,54
right-bracket 243,58,18,54
text-01 152,134,88,13
text-02 164,147,66,7
dot-bottom-left 118,155,16,17
dot-bottom-right 253,155,17,17
```

Add a frame fixture with target `x=1154,y=224,width=717,height=374` and Figure 9 measurement-space
reference `x=1257,y=239,width=763,height=397`. Draw qualifying pixels into these edge ROIs:

```powershell
$createFrameEdges = @(
  [ordered]@{ name='top'; axis='x'; search=@{x=0;y=0;width=690;height=18}; minimumCoverage=.90; maximumGap=6 },
  [ordered]@{ name='bottom'; axis='x'; search=@{x=0;y=356;width=717;height=18}; minimumCoverage=.90; maximumGap=6 },
  [ordered]@{ name='left'; axis='y'; search=@{x=0;y=0;width=18;height=374}; minimumCoverage=.90; maximumGap=6 },
  [ordered]@{ name='right'; axis='y'; search=@{x=699;y=18;width=18;height=356}; minimumCoverage=.90; maximumGap=6 },
  [ordered]@{ name='top-right-chamfer'; axis='diagonal'; search=@{x=680;y=0;width=37;height=37}; minimumPixelCount=80 }
)
```

Make the synthetic actual `wing-left` move right by 2 px and give the synthetic top edge an 8 px gap.
The smoke test must expect those two failures, every other internal component/edge to pass, and unchanged
Create/Join action results. Add synthetic Sprite provenance rows for four `img_pointer` and nine
`doc_frame_line` nodes; assert `room_select_create_logo` usage is absent.

- [ ] **Step 2: Run the smoke test and verify red**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
```

Expected before exporter changes: non-zero exit because `wing-left`, `createFrame`, or the new material rows are absent.

- [ ] **Step 3: Add connected-component wing measurement**

In the C# `LanLobbyVisualDiff` helper, add an 8-connected flood-fill method:

```csharp
public static Rectangle FindLargestCyanComponentsBounds(
    Bitmap source,
    Rectangle search,
    int minimumGreen,
    int minimumGreenOverRed,
    int minimumBlueOverRed,
    int componentCount,
    int minimumComponentPixels)
```

Qualifying pixels satisfy all three color thresholds. Find all 8-connected components inside `search`,
discard components smaller than `minimumComponentPixels`, sort by descending pixel count with
top-left Y then X as deterministic tie-breakers, select exactly `componentCount`, and return their union.
Throw if fewer than the requested count remain. Use `componentCount=2` and
`minimumComponentPixels=500` for both wing rows. Continue using `FindCyanBounds` for the ten
non-wing rows.

- [ ] **Step 4: Add frame-continuity measurement**

Add:

```csharp
public sealed class FrameContinuity
{
    public int QualifyingPixelCount { get; set; }
    public int CoveredAxisPixels { get; set; }
    public int AxisLength { get; set; }
    public double CoverageRatio { get; set; }
    public int LargestGapPixels { get; set; }
}

public static FrameContinuity MeasureCyanContinuity(
    Bitmap source,
    Rectangle search,
    bool horizontal,
    int minimumGreen,
    int minimumGreenOverRed,
    int minimumBlueOverRed)
```

`FrameContinuity` contains `QualifyingPixelCount`, `CoveredAxisPixels`, `AxisLength`,
`CoverageRatio`, and `LargestGapPixels`. A primary-axis coordinate is covered when any qualifying
pixel exists across the search's secondary axis. Leading, internal, and trailing uncovered runs all
contribute to `LargestGapPixels`.

Use thresholds `minimumGreen=12`, `minimumGreenOverRed=3`, and `minimumBlueOverRed=2`.
Top, bottom, left, and right pass at coverage `>=0.90` and largest gap `<=6`. The chamfer passes when
the same threshold produces at least `80` pixels in its ROI.

- [ ] **Step 5: Export the separate frame report and images**

Add `createFrame` to the root JSON with its actual/reference Rects, edge records, and overall `passed`.
Save:

```text
home-create-frame-actual.png
home-create-frame-reference.png
home-create-frame-overlay.png
home-create-frame-heatmap.png
```

Keep `createDecoration` at 12 components, now with `wing-left`/`wing-right`. Include measurement mode,
thresholds, expected/reference/actual bounds, deviations, and pass state. Add matching Markdown tables.
Do not alter action-bar calculation or output fields.

- [ ] **Step 6: Run all PowerShell evidence smokes**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

Expected: all exit `0`; only the deliberately shifted synthetic wing and deliberately broken synthetic
top edge are reported as failures inside their successful smoke fixture.

- [ ] **Step 7: Commit the detector slice**

```powershell
git add -- 'scripts/TestLanLobbyVisualDiffSmoke.ps1' 'scripts/ExportLanLobbyVisualDiff.ps1'
git commit -m 'test: measure stitched create decoration'
```

### Task 4: Capture, calibrate, and freeze the real result

**Files:**
- Modify after measurement: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
- Modify with final frozen values: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3, `Task006StandaloneBuild.BuildWindowsX64`, `LanLobbyCaptureSuite`, and the corrected visual exporter.
- Produces: focused XML, build logs, five real screenshots per cycle, corrected JSON/Markdown evidence, and final placement assertions.

- [ ] **Step 1: Run the four focused fixtures sequentially**

Create `Artifacts\LAN-LOBBY\CreateDecorationCorrection\Verification-1\{Layout,View,Capture,Controller}` and run:

```powershell
$runs = @(
  @{ Leaf='Layout'; Platform='EditMode'; Filter='ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests' },
  @{ Leaf='View'; Platform='PlayMode'; Filter='ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' },
  @{ Leaf='Capture'; Platform='PlayMode'; Filter='ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' },
  @{ Leaf='Controller'; Platform='PlayMode'; Filter='ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests' }
)
foreach ($run in $runs) {
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
    -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
    -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
    -TestPlatform $run.Platform `
    -TestFilter $run.Filter `
    -OutputDirectory (Join-Path 'Artifacts\LAN-LOBBY\CreateDecorationCorrection\Verification-1' $run.Leaf) `
    -TimeoutSeconds 900
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Require a complete NUnit XML with non-zero test count, zero failure, and zero skip.

- [ ] **Step 2: Build the visible-capture Player**

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateDecorationCorrection\WindowsStandalone\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -accept-apiupdate `
  -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateDecorationCorrection\WindowsStandaloneBuild-1.log'
```

Require exit `0`, `BuildResult Succeeded`, zero build errors, and a non-empty executable. Record warnings.

- [ ] **Step 3: Capture five real states**

Run visibly:

```powershell
& 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateDecorationCorrection\WindowsStandalone\ARKnoNIGHTS.exe' `
  -force-d3d11 -screen-width 1920 -screen-height 1080 `
  -lanLobbyCaptureSuite `
  -lanLobbyCaptureOutput 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateDecorationCorrection\Captures-1' `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateDecorationCorrection\PlayerCapture-1.log'
```

Require exit `0`, `[LanLobby][capture.completed] count=5`, five decodable non-empty 1920×1080 PNGs,
and `manifest.json`.

- [ ] **Step 4: Export the first corrected report**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateDecorationCorrection\Captures-1' `
  -OutputDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\CreateDecorationCorrection\VisualDiff-1' `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

First confirm both action Rects and all four accepted action-content rows retain their prior zero-movement/pass
state. Then inspect the 12 internal component rows, five frame-edge rows, full Home image, both actual/reference
crops, overlays, and heatmaps.

- [ ] **Step 5: Apply one measurement-driven correction**

For center components, apply:

```text
newCenterX = oldCenterX - round(deltaX * 2) / 2
newCenterTop = oldCenterTop - round(deltaY * 2) / 2
newWidth = oldWidth * min(expectedVisibleWidth / actualVisibleWidth,
                          expectedVisibleHeight / actualVisibleHeight)
newHeight = newWidth * sourceHeight / sourceWidth
```

For each wing pair, translate both instances by the pair's common center delta and apply one common uniform
scale. If width is within 4 px but height is too small, increase both absolute wing angles by `2°`; if height
is too large, decrease both by `2°`. Preserve left/right symmetry and never non-uniformly stretch a wing.

For frame edges, extend or shift the adjacent `doc_frame_line` segment so every straight edge reaches
coverage `>=0.90` with largest gap `<=6`. Keep overlap between 10 and 24 design pixels; do not add a tenth
segment or any code-native line.

Round centers and dimensions to `0.5` design pixels. Copy the exact corrected values into the View test
expectations.

- [ ] **Step 6: Repeat at most two more materially different cycles**

For cycle 2 or 3, re-run the affected View test, build, visible capture, and exporter into matching
`*-2` or `*-3` directories. Stop immediately when:

- both action bars and content retain the frozen baseline;
- all ten central rows are within center `±1 px` and size `±2 px`;
- both wing unions are within center `±2 px` and size `±4 px`;
- all five frame records pass;
- manual overlay inspection shows horizontal symmetric wings, no vertical strip blocks, continuous low-brightness
  outline, a visible right-top chamfer, and no obvious overlap hotspot.

Stop and report evidence rather than starting a fourth cycle.

- [ ] **Step 7: Run final focused verification**

Run the same four filters with a fresh output root:

```powershell
$runs = @(
  @{ Leaf='Layout'; Platform='EditMode'; Filter='ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests' },
  @{ Leaf='View'; Platform='PlayMode'; Filter='ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' },
  @{ Leaf='Capture'; Platform='PlayMode'; Filter='ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' },
  @{ Leaf='Controller'; Platform='PlayMode'; Filter='ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests' }
)
foreach ($run in $runs) {
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
    -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
    -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
    -TestPlatform $run.Platform `
    -TestFilter $run.Filter `
    -OutputDirectory (Join-Path 'Artifacts\LAN-LOBBY\CreateDecorationCorrection\Verification-Final' $run.Leaf) `
    -TimeoutSeconds 900
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyVisualDiffSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestExportLanLobbyEvidenceSmoke.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

Require all focused tests and smokes to pass.

- [ ] **Step 8: Commit the final measured placement**

```powershell
git add -- `
  'Assets/Game/Runtime/Lobby/LanLobbyView.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs'
git commit -m 'fix: calibrate stitched create decoration'
```

### Task 5: Record evidence and verify the finished slice

**Files:**
- Modify: `docs/SPEC.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/LAN-LOBBY-REPORT.md`

**Interfaces:**
- Consumes: final focused XML, Player build/capture logs, JSON/Markdown visual report, source/imported hashes, and final Git diff.
- Produces: auditable acceptance documentation and an independently reviewed branch state.

- [ ] **Step 1: Update the behavioral specification**

Add a Home Create-decoration paragraph to `docs/SPEC.md` that records:

- four non-`$0` `img_pointer` instances form the wings;
- nine non-`$0` `doc_frame_line` instances form the only visible Create outline;
- `room_select_create_logo` is intentionally omitted;
- code-native `PanelFrame` geometry is forbidden;
- both accepted action bars and LAN behavior remain immutable.

- [ ] **Step 2: Record exact retained evidence**

Append to `docs/TEST_PLAN.md` and `docs/LAN-LOBBY-REPORT.md`:

- focused fixture totals, result, XML, and log paths;
- build exit/result/error/warning counts and executable path;
- final capture and visual-report directories;
- all 12 internal expected/reference/actual bounds and deviations;
- all five frame coverage/gap/pixel-count records;
- action background/icon/label frozen-baseline results;
- Resources path, approved source-relative path, SHA-256, and occurrence count for every rendered Sprite;
- explicit zero occurrences for `room_select_create_logo`;
- manual findings for full Home, internal crop, frame crop, overlays, and heatmaps;
- explicit statement that the frame is nine real `doc_frame_line` Images, not preset or code-native geometry.

- [ ] **Step 3: Run repository-wide EditMode and PlayMode suites**

Run both suites sequentially with:

```powershell
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$fullSuiteRoot = "Artifacts\LAN-LOBBY\CreateDecorationCorrection\FullSuite-$timestamp"
```

Use `$fullSuiteRoot\EditMode` and `$fullSuiteRoot\PlayMode` as the two output directories. Do not change unrelated tests.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform EditMode `
  -OutputDirectory (Join-Path $fullSuiteRoot 'EditMode') `
  -TimeoutSeconds 900

powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -OutputDirectory (Join-Path $fullSuiteRoot 'PlayMode') `
  -TimeoutSeconds 900
```
Compare failures with the retained baseline:

```text
EditMode:
- BattleCoreEditModeTests.Fixture_InvalidInputMatrixReturnsStructuredErrors
- BattleCoreEditModeTests.RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources

PlayMode:
- PreparationBattleLoopPlayModeTests.SampleScene_AutoLoopsPreparationToBattleAndBackWithoutWritingCombatResultToPlayerState
```

If exactly those failures remain, record them as unchanged and outside this UI slice. Any new Lobby,
Create-decoration, asset-provenance, capture, or visual-report failure blocks completion.

- [ ] **Step 4: Review provenance and final diff**

```powershell
git diff --check
git status --short
git diff --stat
git ls-files 'Artifacts/*' 'Temp/*' 'Library/*' 'Build/*' 'Builds/*'
git diff -- `
  'scripts/ImportLobbyAssets.ps1' `
  'docs/references/ui/lobby/ASSET_MAP.md' `
  'Assets/Game/Runtime/Lobby/LanLobbyView.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs' `
  'Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs' `
  'scripts/ExportLanLobbyVisualDiff.ps1' `
  'scripts/TestLanLobbyVisualDiffSmoke.ps1' `
  'docs/SPEC.md' `
  'docs/TEST_PLAN.md' `
  'docs/LAN-LOBBY-REPORT.md'
```

Require no tracked generated directory, no unrelated scene/Prefab/resource change, no Join-layout or network
change, no `$0`/`#0` source, no missing `.meta`, and no code-native Create outline.

- [ ] **Step 5: Commit documentation**

```powershell
git add -- 'docs/SPEC.md' 'docs/TEST_PLAN.md' 'docs/LAN-LOBBY-REPORT.md'
git commit -m 'docs: record stitched create evidence'
```

- [ ] **Step 6: Perform completion verification**

Invoke `superpowers:verification-before-completion`. Independently review the final diff for uGUI hierarchy,
center-pivot rotation, segment count, overlaps, raycast behavior, source provenance, capture/report accuracy,
action-bar immutability, and LAN regression risk. Do not claim repository-wide green status while any retained
unrelated baseline failure remains.
