# LAN Room Slot States Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan.

**Goal:** Rebuild the LAN room page so Figures 11–13 are reproduced with layered empty, waiting, and ready player slots; enforce host-ready/start/leave behavior; and prove the rendered visible artwork and all bitmap provenance with repeatable tests and Player screenshots.

**Architecture:** Keep `LobbyRoomState` authoritative for membership/readiness/start eligibility and keep the existing socket/controller flow. Add a room-only measured layout model and rebuild `LanLobbyView` slots as reusable layer trees whose visibility is driven by `Empty`, `Waiting`, or `Ready`. Extend the existing capture/evidence pipeline so each room fixture maps to its own 16:9 reference and compares visible contours/centers rather than treating texture or `RectTransform` centers as visual truth.

**Tech Stack:** Unity 2022.3.62f1, C# / Unity UI, NUnit EditMode and PlayMode tests, PowerShell evidence scripts, Windows standalone Player, approved autochess PNG assets only.

## Global Constraints

- Work only in `G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby` on branch `codex/lan-lobby`.
- Before every task, run `git status --short`; preserve unrelated/user-owned changes and stop if a target file changed unexpectedly.
- Never open the same project path in two Unity Editor or batchmode processes at once. Run all Unity work serially.
- Do not upgrade Unity or Packages.
- Do not edit or commit `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`, `Builds/`, or `Artifacts/`.
- Do not import crops from Figures 11–13, generated replacement art, derived full-card composites, or any bitmap outside the approved autochess roots.
- Direct Unpacked `$0` and `#0` variants are forbidden. A Combined source is allowed only for a proven atlas entry.
- Keep the already approved Home page geometry and its room-select materials unchanged.
- Figures 11–13 are `2560x1440`; all reference coordinates normalize to `1920x1080` by exactly `0.75`.
- Ignore the upper scrolling comments in all three references.
- Ignore the right popup and the entire fourth slot in Figures 12 and 13. Figure 11 is the only blocking fourth-slot reference.
- Character illustrations and profile-card content are non-blocking. The host slot must render none of them.
- Rendered visible alpha/color bounds and visible centers are authoritative; texture rectangles and `RectTransform` centers are diagnostic only.
- Blocking icon/label tolerance at `1920x1080`: at most `2 px` visible-center error per axis and `3 px` visible-size error.
- Blocking long-contour tolerance: at most `4 px` per visible edge and visible-contour Jaccard score `>= 0.95`.
- Use no more than three fresh visible Windows Player calibration cycles. Stop early when all named gates pass. After cycle three, report remaining deviations instead of weakening gates.
- Every behavior/code task follows red-green-refactor: add a focused failing test, run it and retain the failure evidence, implement the minimum change, rerun to green, then commit.
- Every task ends with `git diff --check` and a focused diff review before commit.

## Reference-Space Geometry Contract

Use these normalized `1920x1080` measurements as the first-pass room layout. The visible-difference gates, not these backing rectangles, decide final acceptance.

| Element | X | Top Y | Width | Height | Blocking reference |
|---|---:|---:|---:|---:|---|
| Slot 1 full composition | 199.5 | 177.75 | 363.75 | 664.50 | Figures 11–13 |
| Slot 2 full composition | 588.75 | 177.75 | 363.75 | 664.50 | Figures 11–13 |
| Slot 3 full composition | 976.50 | 177.75 | 363.75 | 664.50 | Figures 11–13 |
| Slot 4 full composition | 1365.00 | 177.75 | 363.75 | 664.50 | Figure 11 only |
| Slot card body inside each root | 26.25 | 0.00 | 320.25 | 545.25 | State-specific |
| Slot lower decoration inside each root | 0.00 | 544.50 | 363.75 | 120.00 | State-specific |
| Primary action | 1487.25 | 942.75 | 432.00 | 94.50 | Figures 12–13 |
| Leave action backing rect | 43.50 | 30.00 | 118.00 | 52.50 | Figure 13 |
| Latency text safe rect | 176.00 | 30.00 | 250.00 | 52.50 | User requirement |

`Top Y` is top-left reference space. Convert it to the existing bottom-left `LanLobbyRect` convention with:

```csharp
float bottomY = canvasHeight - topY - height;
```

Do not hardcode final icon/label offsets from texture centers. Tasks 4, 6, and 8 measure and compensate their rendered visible centers.

---

### Task 1: Make room readiness and host dissolution authoritative

**Files:**

- Modify: `Assets/Game/Tests/EditMode/Lobby/LobbyRoomStateEditModeTests.cs`
- Modify: `Assets/Game/Runtime/Lobby/LobbyRoomState.cs`
- Modify: `Assets/Game/Tests/EditMode/Lobby/LanSocketIntegrationEditModeTests.cs`
- Inspect only unless a failing test proves a defect: `Assets/Game/Runtime/Lobby/LanRoomHost.cs`

**Interfaces and invariants:**

```csharp
// LobbyRoomState
public static LobbyRoomState CreateHost(LobbyProfile hostProfile, string roomCode);
public bool TryJoin(LobbyProfile profile, out LobbyJoinFailure failure);
public bool TrySetReady(string callerPlayerId, string playerId, bool isReady, out LobbyJoinFailure failure);
public bool TryStart(string playerId, out LobbyJoinFailure failure);
public bool RemovePlayer(string playerId);
public int PruneExpiredMembers(IEnumerable<string> expiredPlayerIds);
public LobbyRoomSnapshot Snapshot { get; }
```

- `CreateHost` inserts the host with `IsReady == true`.
- `TryJoin` inserts every guest with `IsReady == false`.
- `TryStart` succeeds for the host whenever every *present* member is ready, including a one-member host-only room.
- `RemovePlayer(hostId)` leaves zero members; it never promotes a guest.
- Expiry/pruning that removes the host also leaves zero members; it never promotes a guest.

**Step 1: Add failing domain tests**

Add or replace focused NUnit cases:

```csharp
[Test]
public void CreateHost_StartsReady_AndCanStartAlone()
{
    var room = LobbyRoomState.CreateHost(Profile("host"), "123456");

    Assert.That(room.Snapshot.Members.Single().IsReady, Is.True);
    Assert.That(room.TryStart("host", out var failure), Is.True, failure.ToString());
}

[Test]
public void TryJoin_AddsGuestUnready()
{
    var room = LobbyRoomState.CreateHost(Profile("host"), "123456");
    Assert.That(room.TryJoin(Profile("guest"), out var failure), Is.True, failure.ToString());
    Assert.That(room.Snapshot.Members.Single(x => x.Profile.PlayerId == "guest").IsReady, Is.False);
}

[TestCase(2)]
[TestCase(3)]
[TestCase(4)]
public void TryStart_RequiresEveryPresentGuestReady_ButNeverAFullRoom(int memberCount)
{
    // Join memberCount - 1 guests, prove start fails while one is unready,
    // mark only present guests ready, then prove start succeeds.
}

[Test]
public void RemovePlayer_WhenHostLeaves_DissolvesRoomWithoutPromotion()
{
    // Join two guests, remove host, assert Snapshot.Members is empty
    // and Snapshot.HostPlayerId is null.
}

[Test]
public void PruneExpiredMembers_WhenHostExpires_DissolvesRoomWithoutPromotion()
{
    // Pass the host ID to PruneExpiredMembers and assert the room is empty.
}
```

Update old assertions that expected the host to start unready or a remaining guest to become host. Do not merely delete those tests.

**Step 2: Run the focused domain suite and confirm red**

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LobbyRoomStateEditModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task1\Domain-red'
```

Expected red evidence must include host initially unready and/or host promotion failures. If zero tests run, treat the command as failed.

**Step 3: Implement the minimum domain change**

- Change the private member constructor or host creation call so only the initial host is ready by default:

```csharp
members = new List<Member> { new Member(hostProfile, isReady: true) };
```

- Keep `TryJoin` explicit:

```csharp
members.Add(new Member(profile, isReady: false));
```

- When `RemovePlayer` targets the current host, clear `members`, publish the empty snapshot, and return `true`.
- Apply the same dissolution rule when pruning would remove the host.
- Do not add a `Count == 4` condition to `TryStart`.

**Step 4: Run domain tests green**

Repeat Step 2 with `-OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task1\Domain-green'`. Require zero failures and zero skips.

**Step 5: Add socket-level dissolution and initial-state tests**

In `LanSocketIntegrationEditModeTests.cs`:

- update the normal start test to assert the first host snapshot is already ready rather than calling `host.TrySetReady("host", true, ...)`;
- add a host-only start broadcast test;
- add `HostStop_DisconnectsGuestsWithoutPublishingPromotedHost`, starting one host and at least one client, stopping the host, and asserting the guest reaches the existing disconnected/home-return signal without receiving a snapshot that names the guest as host.

**Step 6: Run socket tests red if needed, then green**

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanSocketIntegrationEditModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task1\Socket'
```

Only modify `LanRoomHost.cs` if this test demonstrates a transport defect. The expected current implementation is that `StopAsync` closes every connection and already supplies the dissolution transport behavior.

**Step 7: Review and commit**

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Lobby/LobbyRoomState.cs Assets/Game/Tests/EditMode/Lobby/LobbyRoomStateEditModeTests.cs Assets/Game/Tests/EditMode/Lobby/LanSocketIntegrationEditModeTests.cs
git add Assets/Game/Runtime/Lobby/LobbyRoomState.cs Assets/Game/Tests/EditMode/Lobby/LobbyRoomStateEditModeTests.cs Assets/Game/Tests/EditMode/Lobby/LanSocketIntegrationEditModeTests.cs
git commit -m "fix: make LAN host ready and dissolve on leave"
```

Do not stage `LanRoomHost.cs` unless the socket test required and verified a change.

---

### Task 2: Import and byte-audit the exact room-page materials

**Files:**

- Modify: `scripts/ImportLobbyAssets.ps1`
- Modify: `Assets/Game/Editor/UI/LobbyAssetImportSetup.cs`
- Modify: `Assets/Game/Tests/EditMode/Lobby/LobbyAssetMapEditModeTests.cs`
- Modify: `docs/references/ui/lobby/ASSET_MAP.md`
- Add through the importer, including `.meta` files:
  - `Assets/Resources/UI/Lobby/card_bg.png`
  - `Assets/Resources/UI/Lobby/bg_top_normal.png`
  - `Assets/Resources/UI/Lobby/bg_top_ready.png`
  - `Assets/Resources/UI/Lobby/card_empty.png`
  - `Assets/Resources/UI/Lobby/card_deco_self.png`
  - `Assets/Resources/UI/Lobby/bg_plus.png`
  - `Assets/Resources/UI/Lobby/btn_match_normal.png`
  - `Assets/Resources/UI/Lobby/btn_topmenu_back.png`
  - `Assets/Resources/UI/Lobby/host_top_tag.png`

**Step 1: Resolve source files without copying**

From the approved normal Unpacked tree, use `rg --files` and reject `$0`/`#0` names:

```powershell
$sourceRoot = 'G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess'
rg --files $sourceRoot | rg '(^|[\\/])(card_bg|bg_top_normal|bg_top_ready|card_empty|card_deco_self|bg_plus|btn_match_normal|btn_topmenu_back|host_top_tag)\.png$'
```

Verify the implementation machine still matches this read-only baseline:

| Asset | Exact source-relative path | SHA-256 |
|---|---|---|
| `card_bg` | `[uc]autochessouter/card_bg.png` | `050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2` |
| `bg_top_normal` | `[uc]autochessouter/bg_top_normal.png` | `5A9479B9AFDD4FC3F597CCBF4A1A0D92C1BB1B5053D716E8C20267E6B2C77C4F` |
| `bg_top_ready` | `[uc]autochessouter/bg_top_ready.png` | `EFAA99906A087AAF5AD631E4DF8CFCD7E90C4F463621779A13447675F221482D` |
| `card_empty` | `[uc]autochessouter/card_empty.png` | `4DD34E0B5BE318770082B14F245591D80F6ABFF00451744C4BEF3459798DCE31` |
| `card_deco_self` | `[uc]autochessouter/card_deco_self.png` | `A3217A0EE5C8B1D7325758162C9859BEC765C7B63331881C90CDA4804A93F661` |
| `bg_plus` | `[uc]autochessouter/bg_plus.png` | `E2CA5554B27862FE172E2D18D50092618B2E895C2AD63CDB57019BE593B7B66D` |
| `btn_match_normal` | `[uc]autochessouter/btn_match_normal.png` | `62B586274488AE3A7BF203829DDFE0C80955993AE22334F46EDE076215C3ADCD` |
| `btn_topmenu_back` | `[uc]autochessouter/btn_topmenu_back.png` | `BB78B1FCB84BA5F3A2FF8992809C8B0EFD4CAC5E1E960A8056BAA79A1A6E6303` |
| `host_top_tag` | `[uc]autochessouter/host_top_tag.png` | `861754CAFABFEF6641129CAC439501EE3E3D964E0FA3C72BC32FDAC117131009` |

If a hash differs, a name becomes ambiguous, or only a forbidden `$0`/`#0` file is available, stop Task 2 and report it; do not silently use a similarly named bitmap.

**Step 2: Make the asset tests fail first**

Extend `ExpectedAssetNames` with the nine names above. Add byte-provenance assertions patterned after `LobbyHomeAssetMapEditModeTests.AssertSourceHashMatches`:

```csharp
[TestCaseSource(nameof(ExpectedAssetNames))]
public void ApprovedLobbyAsset_BytesMatchMappedSource(string assetName)
{
    var row = ReadMapRow(assetName);
    Assert.That(row.SourceRelativePath, Does.Not.Contain("$0").And.Not.Contain("#0"));
    AssertSourceHashMatches(assetName, row.SourceRelativePath, row.Sha256);
}
```

Also assert:

- every expected bitmap appears exactly once in `ASSET_MAP.md`;
- every expected `Resources/UI/Lobby/<name>` loads as a Sprite;
- `SourceRelativePath`, SHA-256, and source kind (`Unpacked direct` or `Combined atlas sprite`) are non-empty;
- the importer root directory contains no unexpected root-level PNG.

Run:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LobbyAssetMapEditModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task2\Asset-red'
```

Expected: failures for missing imports/map rows.

**Step 3: Extend the explicit whitelists and map**

- Add the exact nine names to `$assetNames` in `ImportLobbyAssets.ps1`.
- Add matching paths to `LobbyAssetImportSetup.ApprovedAssetPaths`.
- Add one `ASSET_MAP.md` row per new bitmap with the exact relative source path and SHA-256 resolved in Step 1.
- Do not broaden the importer with wildcards.

**Step 4: Run the existing importer**

```powershell
.\scripts\ImportLobbyAssets.ps1 `
  -SourceRoot 'G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess'
```

Verify that only whitelisted PNGs were copied and that Unity generates/preserves the matching `.meta` files with Sprite import, alpha transparency, no mipmaps, and NPOT `None`.

**Step 5: Run the asset tests green**

Repeat Step 2 with `-OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task2\Asset-green'`. Require zero failures/skips and exact byte hashes.

**Step 6: Review and commit**

```powershell
git diff --check
git status --short
git diff -- scripts/ImportLobbyAssets.ps1 Assets/Game/Editor/UI/LobbyAssetImportSetup.cs Assets/Game/Tests/EditMode/Lobby/LobbyAssetMapEditModeTests.cs docs/references/ui/lobby/ASSET_MAP.md Assets/Resources/UI/Lobby
git add scripts/ImportLobbyAssets.ps1 Assets/Game/Editor/UI/LobbyAssetImportSetup.cs Assets/Game/Tests/EditMode/Lobby/LobbyAssetMapEditModeTests.cs docs/references/ui/lobby/ASSET_MAP.md Assets/Resources/UI/Lobby
git commit -m "assets: import audited LAN room slot materials"
```

---

### Task 3: Add a measured room-only layout model

**Files:**

- Add: `Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs`
- Add through Unity import: `Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs.meta`
- Add: `Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs`
- Add through Unity import: `Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs.meta`
- Inspect: `Assets/Game/Runtime/Lobby/LanLobbyLayout.cs`

**Required API:**

```csharp
public sealed class LanLobbyRoomSlotLayout
{
    public LanLobbyRect Root { get; }
    public LanLobbyRect CardBody { get; }       // local to Root
    public LanLobbyRect TopBar { get; }         // local to Root
    public LanLobbyRect StateOverlay { get; }   // local to Root
    public LanLobbyRect EmptyInvite { get; }    // local to Root
    public LanLobbyRect ReadyIcon { get; }      // local to Root
    public LanLobbyRect ReadyLabel { get; }     // local to Root
    public LanLobbyRect LowerDecoration { get; }// local to Root
    public LanLobbyRect CreatorTag { get; }     // local to Root
}

public sealed class LanLobbyRoomLayout
{
    public IReadOnlyList<LanLobbyRoomSlotLayout> Slots { get; }
    public LanLobbyRect LeaveAction { get; }
    public LanLobbyRect Latency { get; }
    public LanLobbyRect PrimaryAction { get; }
    public static LanLobbyRoomLayout ForSize(int width, int height);
}
```

`ForSize` must build from a `1920x1080` canonical layout and apply uniform scale plus centered letterbox offset for other aspect ratios. Do not alter `LanLobbyLayout` Home constants.

**Step 1: Add failing geometry tests**

Tests must prove:

- exactly four slot roots;
- canonical root values match the geometry contract above within `0.01f`;
- adjacent slot visible-root X deltas are `389.25`, `387.75`, and `388.50` within `0.01f`;
- `CardBody` and `LowerDecoration` remain inside each root and overlap by `0.75 px` so no seam appears;
- primary action and leave action match the table;
- latency never overlaps leave;
- `2560x1440` results equal canonical positions/sizes multiplied by `4/3`;
- a non-16:9 canvas letterboxes without changing internal aspect.

Run:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyRoomLayoutEditModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task3\Layout-red'
```

Expected: compile failure because the new API does not exist.

**Step 2: Implement the canonical layout**

- Define immutable canonical `LanLobbyRect` values.
- Convert top-left Y values only once in the constructor/helper.
- Keep state-specific child rects local to each root.
- Seed child rects from the source aspect ratios:
  - `card_bg` fills `CardBody`;
  - top bars span the same visible card width;
  - `player_card_self_frame` shares the ready contour area but may receive a later visible-alpha compensation;
  - `player_card_ready` keeps source aspect and is never stretched to the card;
  - lower decoration fills only `LowerDecoration`.
- Name any compensation constants after rendered content, for example `ReadyCheckVisibleCenterOffset`, not `MagicOffset`.

**Step 3: Run layout tests green**

Repeat Step 1 with `-OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task3\Layout-green'`.

**Step 4: Review and commit**

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs
git add Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs.meta Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs.meta
git commit -m "feat: add measured LAN room layout"
```

---

### Task 4: Replace stretched cards with layered slot presentation

**Files:**

- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
- Use: `Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs`

**Presentation model:**

```csharp
private enum RoomSlotPresentationState
{
    Empty,
    Waiting,
    Ready
}

private sealed class RoomSlotView
{
    public RectTransform Root;
    public Image CardBody;
    public Image TopBar;
    public Image ReadyOverlay;
    public GameObject EmptyContent;
    public Image EmptyInviteIcon;
    public Text EmptyInviteLabel;
    public Text EmptyInviteHint;
    public GameObject OccupiedContent;
    public Image ReadyIcon;
    public Text ReadyLabel;
    public Image LowerDecoration;
    public Image CreatorTag;
}
```

The concrete field names may follow the file's existing style, but the layer ownership and state transitions must remain equivalent.

**Step 1: Add failing hierarchy and state tests**

Replace tests that expect a whole-card `player_card_waiting`/`player_card_ready` image or avatar index. Add PlayMode cases that bind snapshots and inspect the named room nodes:

```csharp
[UnityTest]
public IEnumerator RoomSnapshot_EmptySlotUsesCompleteInviteComposition()
{
    // Bind host-only snapshot.
    // Slots 2–4: card_bg + bg_top_normal + card_empty + bg_plus,
    // invite label/hint present, no OPEN SLOT or WAITING text.
}

[UnityTest]
public IEnumerator RoomSnapshot_UnreadyGuestUsesNeutralFrameWithoutWaitingText()
{
    // Slot 2: bg_top_normal, ready overlay/icon/label hidden.
}

[UnityTest]
public IEnumerator RoomSnapshot_ReadyMemberUsesCyanLayersAndSourceAspectCheck()
{
    // Slot 2: bg_top_ready + player_card_self_frame +
    // player_card_ready + 已就绪.
    // Assert check Image preserves its Sprite aspect.
}

[UnityTest]
public IEnumerator RoomSnapshot_HostReadySlotHasNoProfileOrPortraitContent()
{
    // Host slot has ready treatment and creator tag;
    // portrait/avatar/name/id/profile-card nodes are absent or inactive.
}

[UnityTest]
public IEnumerator RoomSlot_DecorativeLayersDoNotReceiveRaycasts()
{
    // Every non-button Image/Text in slot roots has raycastTarget == false.
}
```

Assert exact sibling order and exact `Resources/UI/Lobby/...` Sprite names for every blocking layer. Assert there are exactly four stable slot roots and that rebinding snapshots does not create extra slot objects.

Run:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task4\View-red'
```

Expected: new layer/state tests fail against the current stretched-card implementation.

**Step 2: Build four reusable layer roots**

- In `BuildRoom`, obtain `LanLobbyRoomLayout.ForSize(...)`.
- Replace `roomCardImages`, `roomCardTexts`, and `roomCardAvatarTexts` with four `RoomSlotView` instances.
- Create all layers once. Use `SetNativeSize` only as an input to aspect calculation; place using layout rects and preserve aspect for non-stretchable icons.
- Reuse Sprites:
  - all states: `card_bg`;
  - empty: `bg_top_normal`, `card_empty`, `bg_plus`;
  - waiting: `bg_top_normal`, neutral lower decoration;
  - ready: `bg_top_ready`, `player_card_self_frame`, `player_card_ready`, `card_deco_self`;
  - host only: `host_top_tag`.
- Do not stretch `player_card_waiting` or `player_card_ready` over the whole card.
- Do not show `OPEN SLOT` or `WAITING`.
- For non-host occupied slots, keep any temporary portrait/profile placeholder behind all blocking frame layers and outside acceptance measurements.
- For the host, do not create or enable portrait, avatar, name, ID, or profile-card content.

**Step 3: Bind explicit slot states**

In `BindRoom`:

```csharp
var member = index < snapshot.Members.Count ? snapshot.Members[index] : null;
var state = member == null
    ? RoomSlotPresentationState.Empty
    : member.IsReady
        ? RoomSlotPresentationState.Ready
        : RoomSlotPresentationState.Waiting;
BindSlot(roomSlots[index], state, member, isHostSlot: index == 0);
```

Switch Sprite assignments and active flags deterministically. Rebinding `Ready -> Waiting -> Empty -> Ready` must restore every layer without stale content.

**Step 4: Run View tests green**

Repeat Step 1 with `-OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task4\View-green'`. Require zero failures/skips.

**Step 5: Review and commit**

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Lobby/LanLobbyView.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs
git add Assets/Game/Runtime/Lobby/LanLobbyView.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs
git commit -m "feat: compose LAN room player slot states"
```

---

### Task 5: Implement one role-dependent primary action and room-only Leave

**Files:**

- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
- Modify only if a lifecycle test fails: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyControllerPlayModeTests.cs`
- Inspect only unless a failing test proves a defect: `Assets/Game/Runtime/Initial/LanLobbyController.cs`

**Required behavior matrix:**

| Local role/state | Sprite | Label | Interactable | Event |
|---|---|---|---|---|
| Host, all present ready | `btn_match_normal` | `协议启动` | yes | `StartRequested` |
| Host, any guest unready | `btn_match_grey` | `协议启动` | no | none |
| Guest, local unready | `btn_match_grey` | `准备就绪` | yes | `ReadyRequested(true)` |
| Guest, local ready | `btn_match_normal` | `取消准备` | yes | `ReadyRequested(false)` |

**Step 1: Add failing role/action tests**

Add PlayMode cases for all four rows. Each case must:

- bind a snapshot and local player ID;
- assert one and only one visible primary action;
- assert exact Sprite, Chinese label, and `interactable`;
- click the button and assert the exact event count/argument;
- prove host never emits `ReadyRequested`;
- prove guest never emits `StartRequested`.

Add:

```csharp
[UnityTest]
public IEnumerator RoomLeave_IsTopLeft_UsesApprovedSprite_AndHasUnobstructedHitTarget()
{
    // btn_topmenu_back, exact layout rect, one LeaveRequested event.
    // Raycast from its visible center and require the Button target.
}

[UnityTest]
public IEnumerator RoomPrimaryAction_HitTargetWinsOverEveryDecoration()
{
    // Raycast visible icon center and label center.
    // Require the same primary Button both times.
}
```

Update `RoomPermissions_RequireLocalMemberAndNeverAllowStartedRoomActions` so a missing local member disables the primary action and a started room disables all room mutations.

Run the View suite and retain the red result:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task5\Actions-red'
```

**Step 2: Replace the three bottom buttons**

- Remove the visible bottom `readyButton`, `startButton`, and bottom Leave layout.
- Create `roomPrimaryActionButton` at `LanLobbyRoomLayout.PrimaryAction`.
- Create the room-only Leave button at `LanLobbyRoomLayout.LeaveAction`.
- Keep latency at `LanLobbyRoomLayout.Latency`.
- Keep all decorative child graphics `raycastTarget=false`; keep the Button target itself raycastable.
- In `BindRoom`, select Sprite/label/interactability from the behavior matrix.
- Register one click listener that branches from the last bound local role/readiness state and emits exactly one approved event.

**Step 3: Run View tests green**

Repeat Step 1 with `-OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task5\Actions-green'`.

**Step 4: Prove controller leave/start flow**

Run:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task5\Controller'
```

If coverage is absent, first add a failing test proving host Leave calls the existing host shutdown path and returns the local view Home, while guest Leave calls the guest leave path. Do not change `LanLobbyController` if its existing behavior passes.

**Step 5: Review and commit**

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Lobby/LanLobbyView.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs Assets/Game/Runtime/Initial/LanLobbyController.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyControllerPlayModeTests.cs
git add Assets/Game/Runtime/Lobby/LanLobbyView.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs
git add Assets/Game/Tests/PlayMode/Lobby/LanLobbyControllerPlayModeTests.cs
git add Assets/Game/Runtime/Initial/LanLobbyController.cs
git commit -m "feat: add role-aware LAN room actions"
```

Before committing, unstage unchanged optional files. The commit must contain only files actually needed by the passing tests.

---

### Task 6: Make capture fixtures and manifests represent Figures 11–13

**Files:**

- Modify: `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs`
- Modify: `Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs`

**Fixture contract:**

| Capture | Members | Readiness | Primary action | Reference |
|---|---|---|---|---|
| `room-host` | host only | host ready | cyan `协议启动` | Figure 11 |
| `room-full` | host + 3 guests | host ready, all guests unready | gray `协议启动` | Figure 12 |
| `room-ready` | host + 3 guests | all ready | cyan `协议启动` | Figure 13 |

**Step 1: Add failing fixture and manifest tests**

Tests must assert:

- the exact fixture membership/readiness table above;
- `room-host`, `room-full`, and `room-ready` all export `1920x1080`;
- all four slot roots are in `keyRects`;
- each slot exports named sub-rects for card body, top bar, overlay, invite/check, label, lower decoration, and creator tag when present;
- Leave, Latency, and PrimaryAction rects are exported;
- source audit records Resources path, approved source-relative path, SHA-256, capture names, and rendered occurrence count;
- repeated Sprites have correct occurrence counts, for example `card_bg == 4` in each room capture;
- bitmap records have `isBitmap=true`; code-native geometry has `isBitmap=false`, empty Sprite/material, and no raycast;
- the host fixture has no host portrait/avatar/name/id/profile nodes;
- no fixture contains `OPEN SLOT` or `WAITING`;
- the fourth slot remains present in all captures even though evidence later excludes it for Figures 12/13.

Run:

```powershell
.\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task6\Capture-red'
```

**Step 2: Update fixture snapshots**

- Set host ready in every room fixture.
- `room-host`: no guests.
- `room-full`: add exactly three unready guests.
- `room-ready`: add exactly three ready guests.
- Preserve capture names and Home fixtures to avoid breaking existing report links.

**Step 3: Expand structured capture data**

- Export named `RectTransform` diagnostics for every blocking node.
- Extend `TryGetApprovedSource` for every Task 2 asset.
- Record actual rendered occurrence counts per capture, not just whether a source exists.
- Keep `RectTransform` data diagnostic. Do not label it as a visible-pixel pass.
- Add stable test-only node names in `LanLobbyView` if needed; names must describe the rendered role, such as `RoomSlot2.ReadyIcon`.

**Step 4: Run capture tests green**

Repeat Step 1 with `-OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Task6\Capture-green'`.

**Step 5: Review and commit**

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs
git add Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs
git commit -m "test: capture authoritative LAN room states"
```

---

### Task 7: Route each room capture to its own reference and add visual-position gates

**Files:**

- Modify: `scripts/ExportLanLobbyVisualDiff.ps1`
- Modify: `scripts/ExportLanLobbyEvidence.ps1`
- Modify: `scripts/TestLanLobbyVisualDiffSmoke.ps1`
- Modify: `scripts/TestExportLanLobbyEvidenceSmoke.ps1`
- Modify: `scripts/TestLanLobbyEvidenceCommonSmoke.ps1`

**Reference map:**

```powershell
$referenceByCapture = @{
    'home'               = '图9.png'
    'discovered-prefill' = '图9.png'
    'room-host'          = '图11.png'
    'room-full'          = '图12.png'
    'room-ready'         = '图13.png'
}
```

Do not retain the old “every room capture uses Figure 10” fallback.

**Step 1: Add failing smoke fixtures**

Create generated test-only fixture images inside each smoke script's temporary directory; do not add fixture PNGs to the repository.

Tests must prove:

- exact `图11.png`, `图12.png`, and `图13.png` suffix resolution;
- all three references must be exactly `2560x1440`;
- a `room-host` export cannot pass against Figure 12 or 13;
- Figure 12/13 upper barrage and full fourth-slot rectangles are excluded;
- Figure 11 slot 4 remains blocking;
- shifting a rendered check icon by `3 px` fails the `2 px` center gate;
- changing an icon's visible width by `4 px` fails the `3 px` size gate;
- shifting a contour edge by `5 px` fails;
- a contour Jaccard score below `0.95` fails;
- changing only transparent padding/diagnostic `RectTransform` data does not fail if rendered visible pixels still match;
- moving visible pixels while keeping the same `RectTransform` does fail;
- popup masks cannot cover the primary button, host slot, or unobstructed slots 2–3;
- source occurrence-count or SHA mismatches fail material evidence.

Run all three scripts and retain the expected red output:

```powershell
.\scripts\TestLanLobbyVisualDiffSmoke.ps1
.\scripts\TestExportLanLobbyEvidenceSmoke.ps1
.\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
```

At least one new assertion must fail before production script changes. A test that never invokes the production exporter is not sufficient.

**Step 2: Implement capture-specific reference routing**

- Resolve each filename by exact suffix under the explicit read-only reference directory.
- Verify width and height before normalization.
- Normalize reference images to `1920x1080` with the existing deterministic image path.
- Preserve full-image metrics as informational only.

**Step 3: Add rendered visible-bound helpers**

Add production helpers with structured results:

```powershell
function Get-LanLobbyVisibleBounds {
    param(
        [Parameter(Mandatory)] $Image,
        [Parameter(Mandatory)] $Roi,
        [Parameter(Mandatory)] [ValidateSet('Cyan','Gray','Dark','Light','Contrast')] [string] $MaskKind
    )
    # Return left, top, right, bottom, width, height, centerX, centerY,
    # pixelCount, and an explicit failure when no visible pixels exist.
}

function Measure-LanLobbyVisiblePlacement {
    param($ActualImage, $ReferenceImage, $Gate)
    # Return per-edge, size, center, contour intersection/union/Jaccard,
    # thresholds, pass/fail, and both actual/reference visible bounds.
}
```

Use deterministic color/contrast masks appropriate to the opaque screenshots. Never call opaque screenshot pixels “non-transparent alpha.” Source-PNG alpha bounds may be reported separately for provenance/compensation.

**Step 4: Define named blocking gates**

At minimum:

- `RoomHost.Slot1.ReadyTopBar`
- `RoomHost.Slot1.ReadyContour`
- `RoomHost.Slot1.ReadyCheck`
- `RoomHost.Slot1.ReadyLabel`
- `RoomHost.Slot1.CreatorTag`
- `RoomHost.Slot2.EmptyComposition`
- `RoomHost.Slot3.EmptyComposition`
- `RoomHost.Slot4.EmptyComposition`
- `RoomFull.Slot2.WaitingTopBar`
- `RoomFull.Slot2.WaitingContour`
- `RoomFull.Slot3.WaitingTopBar`
- `RoomFull.Slot3.WaitingContour`
- `RoomReady.Slot2.ReadyTopBar`
- `RoomReady.Slot2.ReadyContour`
- `RoomReady.Slot2.ReadyCheck`
- `RoomReady.Slot2.ReadyLabel`
- matching unobstructed slot 3 gates;
- `RoomFull.PrimaryAction.Gray`
- `RoomReady.PrimaryAction.Cyan`
- primary embedded-icon center and label center gates;
- `RoomReady.Leave`;
- host profile-content absence;
- `OPEN SLOT`/`WAITING` absence;
- repeated slot-spacing gates between visible contours.

The report must say why slot 4 is `ExcludedByReferencePopup` for Figures 12/13 rather than marking it passed.

**Step 5: Emit machine-readable and human-readable evidence**

For each gate write:

- reference/capture name;
- ROI and exclusions;
- actual/reference visible bounds and centers;
- edge, center, size, and Jaccard measurements;
- threshold;
- pass/fail/excluded status;
- associated Sprite Resources path and source provenance when applicable.

Retain overlays and heatmaps, with named-ROI outlines for failed gates.

**Step 6: Run all smoke scripts green**

Repeat Step 1. Require process exit code 0 and explicit fixture counts. Zero fixtures is failure.

**Step 7: Review and commit**

```powershell
git diff --check
git diff -- scripts/ExportLanLobbyVisualDiff.ps1 scripts/ExportLanLobbyEvidence.ps1 scripts/TestLanLobbyVisualDiffSmoke.ps1 scripts/TestExportLanLobbyEvidenceSmoke.ps1 scripts/TestLanLobbyEvidenceCommonSmoke.ps1
git add scripts/ExportLanLobbyVisualDiff.ps1 scripts/ExportLanLobbyEvidence.ps1 scripts/TestLanLobbyVisualDiffSmoke.ps1 scripts/TestExportLanLobbyEvidenceSmoke.ps1 scripts/TestLanLobbyEvidenceCommonSmoke.ps1
git commit -m "test: compare LAN room UI with figures 11 to 13"
```

---

### Task 8: Perform bounded Player calibration using visible artwork

**Files:**

- Modify only if a gate proves it necessary:
  - `Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs`
  - `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
  - `scripts/ExportLanLobbyVisualDiff.ps1`
  - their focused tests
- Generate ignored evidence only under:
  - `Artifacts/LAN-LOBBY/RoomSlotStates/Cycle-1/`
  - optionally `Cycle-2/`
  - optionally `Cycle-3/`

**Step 1: Run all pre-Player tests serially**

Run these exact filters one at a time:

```powershell
$unity = 'D:\2022.3.62f1c1\Editor\Unity.exe'
$project = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby'

.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LobbyRoomStateEditModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\PrePlayer\Domain'
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LanSocketIntegrationEditModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\PrePlayer\Socket'
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LobbyAssetMapEditModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\PrePlayer\Assets'
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyRoomLayoutEditModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\PrePlayer\Layout'
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\PrePlayer\View'
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\PrePlayer\Capture'
.\scripts\Invoke-UnityTests.ps1 -UnityPath $unity -ProjectPath $project -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests' -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\PrePlayer\Controller'
```

Stop before building if any result has zero tests, a failure, a skip, compile errors, or an incomplete log.

**Step 2: Build the Windows Player**

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-1\WindowsStandalone\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -accept-apiupdate -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-1\Build.log'
if ($LASTEXITCODE -ne 0) { throw "Windows build failed: $LASTEXITCODE" }
```

Verify the executable exists and the build log has no new task-related errors.

**Step 3: Run visible Player capture cycle 1**

Launch the Player visibly, never with a hidden window:

```powershell
& 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-1\WindowsStandalone\ARKnoNIGHTS.exe' `
  -force-d3d11 `
  -lanLobbyCaptureSuite `
  -lanLobbyCaptureOutput 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-1\Captures' `
  -screen-width 1920 -screen-height 1080
```

Require:

- `room-host.png`
- `room-full.png`
- `room-ready.png`
- the capture manifest
- Player log
- clean process exit

Missing screenshots are a failed cycle, not a visual pass.

**Step 4: Export Figure 11–13 evidence**

```powershell
.\scripts\ExportLanLobbyEvidence.ps1 `
  -CaptureDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-1\Captures' `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud' `
  -OutputDirectory 'Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-1\Evidence'
```

Review the structured report, overlay, and heatmap for every named gate. Also visually inspect the three actual PNGs at original detail.

**Step 5: Calibrate from measured visible deltas only**

If cycle 1 fails:

1. Add or tighten a focused test that reproduces the failing named gate.
2. Change only:
   - room slot layer geometry;
   - visible-center compensation;
   - room-only primary/Leave geometry;
   - a demonstrably incorrect room-only evidence mask.
3. Do not move Home UI or lower thresholds.
4. Commit the correction with the failed gate name in the message.
5. Repeat build, visible capture, and evidence as Cycle 2.

If cycle 2 still fails, repeat once as Cycle 3. After Cycle 3, stop with exact remaining deviations.

**Step 6: Completion gate for this task**

All of the following must be true:

- every named non-excluded gate passes;
- slot 4 in Figures 12/13 is reported excluded, not passed;
- manual inspection confirms no giant/stretch-distorted textures;
- host slot has no portrait/profile content;
- Figures 11 empty slots, Figure 12 waiting slots 2–3, and Figure 13 ready slots 2–3 visibly match;
- button icon and label visible centers pass independently;
- all source hashes and occurrence counts pass.

**Step 7: Review and commit any calibration edits**

```powershell
git diff --check
git status --short
git diff -- Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs Assets/Game/Runtime/Lobby/LanLobbyView.cs scripts/ExportLanLobbyVisualDiff.ps1
```

Commit only source/test changes. Never commit generated Cycle evidence.

---

### Task 9: Synchronize specification, test plan, and final evidence report

**Files:**

- Modify: `docs/SPEC.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/LAN-LOBBY-REPORT.md`
- Inspect: `docs/superpowers/specs/2026-07-28-lan-room-slot-states-design.md`

**Step 1: Add a failing documentation contract check**

If the repository already has a docs validation script, extend it. Otherwise add a small read-only assertion block to `scripts/TestLanLobbyEvidenceCommonSmoke.ps1` that reads the three docs and fails until each contains these exact concepts:

- host starts ready;
- newly joined guest starts unready;
- start requires all present members ready but not a full room;
- host-only start is permitted;
- host Leave dissolves the room;
- no host migration/promotion;
- guest `准备就绪` / `取消准备`;
- host `协议启动`;
- Figures 12/13 fourth slot exclusion;
- visible artwork, not texture rect, is visual authority.

Run the smoke script and retain the expected red documentation assertions.

**Step 2: Update `SPEC.md`**

Make the behavior authoritative:

- host initial readiness;
- guest readiness toggle;
- all-present start eligibility and host-only start;
- guest leave vs host dissolution;
- no host promotion;
- Player-visible action labels/colors.

Remove or explicitly supersede any conflicting host-promotion or full-room-start text.

**Step 3: Update `TEST_PLAN.md`**

Document:

- exact EditMode/PlayMode filters;
- Figures 11–13 capture mapping;
- barrage/popup/fourth-slot exclusions;
- visible center/edge/Jaccard thresholds;
- three-cycle limit;
- Windows build/capture commands;
- Android + Windows physical same-Wi-Fi check as manual/unverified until actually performed.

**Step 4: Update `LAN-LOBBY-REPORT.md` from fresh artifacts only**

Record:

- final successful cycle;
- screenshot/evidence absolute paths;
- every new Sprite, source-relative path, SHA-256, capture occurrence count, and state usage;
- code-native geometry declarations;
- test counts/failures/skips and log paths;
- Windows build result;
- named visual gate summary and exclusions;
- remaining risks/manual checks.

Do not copy old pass statements if the new artifact is missing.

**Step 5: Run docs/evidence smoke green**

```powershell
.\scripts\TestLanLobbyEvidenceCommonSmoke.ps1
.\scripts\TestLanLobbyVisualDiffSmoke.ps1
.\scripts\TestExportLanLobbyEvidenceSmoke.ps1
```

**Step 6: Review and commit**

```powershell
git diff --check
git diff -- docs/SPEC.md docs/TEST_PLAN.md docs/LAN-LOBBY-REPORT.md scripts/TestLanLobbyEvidenceCommonSmoke.ps1
git add docs/SPEC.md docs/TEST_PLAN.md docs/LAN-LOBBY-REPORT.md scripts/TestLanLobbyEvidenceCommonSmoke.ps1
git commit -m "docs: specify and report LAN room slot states"
```

---

### Task 10: Run final regression, provenance, and repository hygiene checks

**Files:**

- Verify only; modify only to fix a demonstrated regression.

**Step 1: Run final focused suites**

Repeat all seven Unity filters from Task 8 and all three PowerShell smoke scripts. Save results under:

`Artifacts\LAN-LOBBY\RoomSlotStates\Final\`

Record the actual test count, failures, skips, exit code, and log path for each command.

**Step 2: Run a final Windows build and one evidence capture if calibration changed after the last successful cycle**

If no code/layout/evidence-detector file changed after the successful cycle, reuse that cycle and state why. If any did change, perform a new final build/capture/export; this final confirmation does not create a fourth calibration opportunity.

**Step 3: Inspect logs and output**

- Search Unity and Player logs for `error`, `exception`, missing Sprite, missing Resource, and failed assertion.
- Verify the Player capture dimensions are exactly `1920x1080`.
- Open `room-host.png`, `room-full.png`, and `room-ready.png` at original detail.
- Verify the structured report contains no blocking `Fail` or unexplained `Missing`.

**Step 4: Verify Git hygiene**

```powershell
git diff --check
git status --short
git ls-files 'Artifacts/*' 'Temp/*' 'Logs/*' 'Library/*' 'Build/*' 'Builds/*'
git log --oneline --decorate -12
```

Require:

- no generated directory tracked;
- no reference crop or derived composite under `Assets`;
- no unapproved new PNG;
- all new `.meta` files present;
- no unrelated Home geometry change;
- no user change lost.

**Step 5: Perform an independent final review**

Review the final diff specifically for:

- host/guest lifecycle regressions;
- stale event listeners after repeated room binding;
- host promotion paths left in domain/controller/docs;
- started-room actions remaining enabled;
- Sprite layer order and raycast interception;
- source aspect violations;
- Figure 12/13 fourth-slot false passes;
- masks accidentally hiding a named gate;
- hash records that point to a different source file;
- tests that can pass with zero assertions or zero fixtures.

Fix only demonstrated issues, rerun the affected test and the final focused suite, then commit the correction separately.

**Step 6: Final handoff contents**

Report:

1. behavior and UI completed;
2. exact modified files;
3. exact tests/build/capture run and their results;
4. final actual screenshot and evidence folder;
5. complete bitmap source/hash/usage summary;
6. anything not verified;
7. remaining physical Windows/Android same-Wi-Fi manual check.

Do not claim complete unless the design acceptance list is individually satisfied.

## Plan Self-Review Checklist

- [ ] Every approved behavior and visual requirement maps to at least one task and one verification.
- [ ] Figure 11, 12, and 13 each have a distinct capture fixture and reference route.
- [ ] Figure 12/13 popup and fourth-slot exclusions cannot mask other gates.
- [ ] Host-ready, non-full start, host-only start, and host dissolution are tested below the View layer.
- [ ] Host slot content absence is tested and visually gated.
- [ ] Empty, Waiting, and Ready are layered compositions, not stretched whole-card textures.
- [ ] Visible content bounds/centers override texture/RectTransform centers.
- [ ] Asset import is explicit, byte-audited, source-mapped, and occurrence-counted.
- [ ] Home UI is outside the modification scope.
- [ ] No task introduces an unapproved package, external executable, background service, or uploaded project data.
- [ ] All Unity operations are serial and all Player calibration is bounded to three cycles.
- [ ] No `TODO`, placeholder implementation, weakened assertion, or “zero tests means pass” path remains.
