# LAN 房间 UI 与局域网闭环 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 在 Windows 与 Android 的同一局域网中交付可发现、可创建、可凭六位房间号加入、可同步准备与开始的房间 UI，视觉以图 9 / 图 10 为基准。

**Architecture:** 新建 ARKnoNIGHTS.Lobby 程序集承载可测试的房间模型、消息协议和 socket 生命周期；Unity 场景接线留在新增的 Initial 层组件，现有战斗 Core 不依赖房间程序集。覆盖全屏的运行时 uGUI 根渲染大厅和房间；它通过对现有准备循环施加可逆的大厅门控，阻止房间内后台倒计时。

**Tech Stack:** Unity 2022.3.62f1c1、C#、uGUI、System.Net.Sockets（UDP 广播发现 + TCP 状态会话）、JsonUtility、NUnit / Unity Test Framework、Windows 与 Android Player。

## Global Constraints

- 不安装或升级任何 Unity Package、第三方网络库、桌面程序、后台服务或全局依赖。
- 除明确由 Codex 生成的贴图外，所有复刻 UI 的贴图必须从 G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess 导入；不得使用参考图裁切、网络图、其他本地目录或临时占位图。
- 每张大厅贴图必须在 docs/references/ui/lobby/ASSET_MAP.md 记录素材库相对路径和用途。
- 房间网络仅同步成员、昵称、头像索引、准备、延迟和开始；不联网同步 PlayerState、部署、商店、战斗或结果。
- 不改动用户当前已修改的 FormalBattleHudUi005.cs、UI005CaptureSuite.cs、现有测试、序列化资源或用户未提交资源。
- 任何网络异常都必须保留在可恢复 UI 状态，不得把本地回退伪装为已经联网。
- Android 与 Windows 真实互联必须在同一 Wi-Fi 的实机/Player 上验证；Editor 测试不能替代该验收。

---

## File Structure

| Path | Responsibility |
|---|---|
| Assets/Game/Runtime/Lobby/ARKnoNIGHTS.Lobby.asmdef | 独立 LAN 房间程序集；不引用 Battle / PlayerState。 |
| Assets/Game/Runtime/Lobby/LobbyModels.cs | 房间号、身份、成员、快照、发现项与错误码 DTO。 |
| Assets/Game/Runtime/Lobby/LobbyProtocol.cs | 有界 JSON 消息 DTO、长度前缀编码/解码。 |
| Assets/Game/Runtime/Lobby/LobbyRoomState.cs | 主机权威状态机：加入、离开、准备、开始、超时。 |
| Assets/Game/Runtime/Lobby/LanDiscoveryService.cs | UDP 广播、发现缓存、过期和碰撞检测。 |
| Assets/Game/Runtime/Lobby/LanRoomHost.cs / LanRoomClient.cs | TCP listener/client、心跳、快照及停止。 |
| Assets/Game/Runtime/Initial/LanLobbyController.cs | Unity 会话协调、身份偏好、大厅门控、战斗入口。 |
| Assets/Game/Runtime/Initial/LanLobbyView.cs / LanLobbyLayout.cs | 只渲染 uGUI 首页/房间页和响应布局。 |
| Assets/Game/Runtime/Lobby/AndroidMulticastLock.cs | Android multicast lock；其他平台无副作用；归入可测试的 Lobby 程序集。 |
| Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs | 仅追加大厅门控 API。 |
| Assets/Game/Editor/UI/LobbyAssetImportSetup.cs | 导入白名单贴图的 Sprite 设置。 |
| Assets/Plugins/Android/AndroidManifest.xml | LAN 所需 Android 权限。 |
| Assets/Resources/UI/Lobby | 素材库白名单导入 PNG。 |
| scripts/ImportLobbyAssets.ps1 | 带根目录验证的白名单复制。 |
| docs/references/ui/lobby/ASSET_MAP.md | 资产源相对路径、用途、9-slice 标记。 |
| Assets/Game/Tests/EditMode/Lobby | 协议、状态、发现与 socket 测试。 |
| Assets/Game/Tests/PlayMode/Lobby | UI、门控、生命周期测试。 |

## Task 1: 建立房间模型和受限协议

**Files:**

- Create: Assets/Game/Runtime/Lobby/ARKnoNIGHTS.Lobby.asmdef
- Create: Assets/Game/Runtime/Lobby/LobbyModels.cs
- Create: Assets/Game/Runtime/Lobby/LobbyProtocol.cs
- Create: Assets/Game/Tests/EditMode/Lobby/LobbyProtocolEditModeTests.cs
- Modify: Assets/Game/Tests/EditMode/Battle/ARKnoNIGHTS.Battle.EditModeTests.asmdef

**Interfaces:**

- Produces LobbyProfile, LobbyMemberSnapshot, LobbyRoomSnapshot, LobbyDiscoveryEntry, LobbyJoinFailure, LobbyWireMessage, LobbyProtocol.Encode and LobbyProtocol.TryDecode.
- Constraints: protocol version 1; maximum frame 4096 bytes; six-digit room code; display name maximum 20 chars; avatar index 0..7; four members maximum.

- [ ] **Step 1: Write the failing tests**

    [Test]
    public void Decode_RejectsFrameAboveMaximumSize()
    {
        var oversized = new byte[LobbyProtocol.MaximumMessageBytes + 1];
        Assert.That(LobbyProtocol.TryDecode(oversized, out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo(LobbyProtocolError.FrameTooLarge));
    }

    [TestCase("123456", true)]
    [TestCase("12345", false)]
    [TestCase("12a456", false)]
    public void RoomCode_RequiresExactlySixDigits(string value, bool expected)
    {
        Assert.That(LobbyRoomCode.IsValid(value), Is.EqualTo(expected));
    }

- [ ] **Step 2: Run tests to verify failure**

Run: Unity EditMode tests filtered to LobbyProtocolEditModeTests.

Expected: compilation fails because the Lobby assembly and protocol types do not exist.

- [ ] **Step 3: Implement the minimal protocol**

    public enum LobbyMessageKind
    {
        JoinRequest, JoinAccepted, RoomSnapshot, SetReady, Ping, Pong, Start, Leave, Reject
    }

    [Serializable]
    public sealed class LobbyWireMessage
    {
        public int protocolVersion;
        public string kind;
        public string roomCode;
        public string playerId;
        public string displayName;
        public int avatarIndex;
        public bool isReady;
        public long sentUnixMilliseconds;
        public string snapshotJson;
        public string rejectionCode;
    }

Use JsonUtility only after checking received frame length. Prepend every TCP payload with a network-byte-order 32-bit length. Reject empty fields, unknown kinds, incorrect protocol version, invalid room code, invalid profile and overlong snapshot JSON.

- [ ] **Step 4: Run tests to verify pass**

Run: Unity EditMode tests filtered to LobbyProtocolEditModeTests.

Expected: valid Join/Snapshot/Ping round-trip; invalid JSON, wrong version, oversize frames, invalid code, long names and out-of-range avatar are rejected.

- [ ] **Step 5: Commit**

    git add Assets/Game/Runtime/Lobby Assets/Game/Tests/EditMode/Lobby Assets/Game/Tests/EditMode/Battle/ARKnoNIGHTS.Battle.EditModeTests.asmdef
    git commit -m "feat: add bounded LAN lobby protocol"

## Task 2: Implement the host-authoritative room state

**Files:**

- Create: Assets/Game/Runtime/Lobby/LobbyRoomState.cs
- Create: Assets/Game/Tests/EditMode/Lobby/LobbyRoomStateEditModeTests.cs

**Interfaces:**

- Consumes Task 1 DTOs.
- Produces LobbyRoomState.CreateHost, TryJoin, TrySetReady, RemovePlayer, TryStart, PruneExpiredMembers and immutable Snapshot.

- [ ] **Step 1: Write the failing test**

    [Test]
    public void OnlyHostCanStartAndOnlyWhenEveryMemberIsReady()
    {
        var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
        state.TryJoin(Profile("guest"), out _);
        Assert.That(state.TryStart("guest", out var notHost), Is.False);
        Assert.That(notHost, Is.EqualTo(LobbyJoinFailure.NotHost));
        Assert.That(state.TryStart("host", out var notReady), Is.False);
        Assert.That(notReady, Is.EqualTo(LobbyJoinFailure.NotReady));
        state.TrySetReady("host", true, out _);
        state.TrySetReady("guest", true, out _);
        Assert.That(state.TryStart("host", out _), Is.True);
        Assert.That(state.Snapshot.HasStarted, Is.True);
    }

- [ ] **Step 2: Run test to verify failure**

Run: Unity EditMode tests filtered to LobbyRoomStateEditModeTests.

Expected: LobbyRoomState is unavailable.

- [ ] **Step 3: Implement the state transitions**

    public bool TrySetReady(string playerId, bool isReady, out LobbyJoinFailure failure)
    {
        if (snapshot.HasStarted) { failure = LobbyJoinFailure.RoomStarted; return false; }
        var member = members.FirstOrDefault(item => item.PlayerId == playerId);
        if (member == null) { failure = LobbyJoinFailure.UnknownPlayer; return false; }
        member.IsReady = isReady;
        revision++;
        failure = LobbyJoinFailure.None;
        return true;
    }

Keep host index zero. Reject duplicate player IDs, a fifth member, guest attempts to modify another member, and all joins after start. Every accepted mutation creates a defensive immutable snapshot.

- [ ] **Step 4: Run tests to verify pass**

Run: Unity EditMode tests filtered to LobbyRoomStateEditModeTests.

Expected: host rights, four-player cap, duplicate member, ready mutations, start conditions, leave and timeout all have deterministic snapshots.

- [ ] **Step 5: Commit**

    git add Assets/Game/Runtime/Lobby/LobbyRoomState.cs Assets/Game/Tests/EditMode/Lobby/LobbyRoomStateEditModeTests.cs
    git commit -m "feat: add authoritative LAN room state"

## Task 3: Add UDP discovery and TCP transport

**Files:**

- Create: Assets/Game/Runtime/Lobby/LanDiscoveryService.cs
- Create: Assets/Game/Runtime/Lobby/LanRoomHost.cs
- Create: Assets/Game/Runtime/Lobby/LanRoomClient.cs
- Create: Assets/Game/Tests/EditMode/Lobby/LanSocketIntegrationEditModeTests.cs

**Interfaces:**

- Produces LanDiscoveryService.Start, Stop, Tick and DiscoveredRooms; LanRoomHost.StartAsync and StopAsync; LanRoomClient.JoinAsync, SetReadyAsync, LeaveAsync and StopAsync.
- Uses UDP port 46871; discovery interval 750 ms; entry expiry 2500 ms; TCP listener binds an OS-selected port.

- [ ] **Step 1: Write the failing loopback integration test**

    [Test]
    public async Task HostAndClient_JoinReadyStartAndReleaseTcpPort()
    {
        using var host = await LanRoomHost.StartForTestsAsync(Profile("host"), 0);
        using var client = await LanRoomClient.JoinForTestsAsync(host.LoopbackEndpoint, Profile("guest"));
        await client.SetReadyAsync(true);
        host.SetReadyForTests("host", true);
        Assert.That(host.TryStart("host", out _), Is.True);
        await client.WaitForStartAsync(TimeSpan.FromSeconds(2));
        var port = host.TcpPort;
        host.Dispose();
        Assert.That(CanBindLoopback(port), Is.True);
    }

- [ ] **Step 2: Run test to verify failure**

Run: Unity EditMode tests filtered to LanSocketIntegrationEditModeTests.

Expected: host/client types do not exist.

- [ ] **Step 3: Implement cancellation-safe services**

    public sealed class LanDiscoveryService : IDisposable
    {
        public const int DiscoveryPort = 46871;
        public const int AnnouncementIntervalMilliseconds = 750;
        public const int DiscoveryExpiryMilliseconds = 2500;
        public IReadOnlyList<LobbyDiscoveryEntry> DiscoveredRooms { get; }
        public void Tick(DateTimeOffset now);
    }

Bind a receive UdpClient with address reuse and announce to 255.255.255.255:46871. Key discoveries by roomCode and endpoint; expire after 2500 ms. A host receiving its room code from another endpoint raises a collision event and generates a new code before the next broadcast.

Use one TcpListener(IPAddress.Any, 0) and one read loop per guest. Background work only queues immutable events; it never touches Unity APIs. Send Ping every 1000 ms, remove after three missed Pongs, measure round-trip milliseconds. Stop cancels loops, closes listener/client, awaits owned tasks and releases the port.

- [ ] **Step 4: Run tests to verify pass**

Run: Unity EditMode tests filtered to LanSocketIntegrationEditModeTests.

Expected: loopback join, full snapshot, ready, start, RTT, leave, port release, discovery expiry and collision are all verified.

- [ ] **Step 5: Commit**

    git add Assets/Game/Runtime/Lobby/LanDiscoveryService.cs Assets/Game/Runtime/Lobby/LanRoomHost.cs Assets/Game/Runtime/Lobby/LanRoomClient.cs Assets/Game/Tests/EditMode/Lobby/LanSocketIntegrationEditModeTests.cs
    git commit -m "feat: add LAN room discovery and transport"

## Task 4: Add Android multicast lifecycle and permissions

**Files:**

- Create: Assets/Game/Runtime/Lobby/AndroidMulticastLock.cs
- Create: Assets/Plugins/Android/AndroidManifest.xml
- Create: Assets/Game/Tests/PlayMode/Lobby/AndroidMulticastLockPlayModeTests.cs
- Modify: ProjectSettings/ProjectSettings.asset

**Interfaces:**

- Produces IMulticastLock.Acquire, Release, IsHeld, and AndroidMulticastLockFactory.Create.
- Controller is the only caller and holds the lock only while discovery is active.

- [ ] **Step 1: Write the failing no-op test**

    [UnityTest]
    public IEnumerator NonAndroidMulticastLock_IsNoOpAndReleasesIdempotently()
    {
        var lockHandle = AndroidMulticastLockFactory.Create();
        lockHandle.Acquire();
        lockHandle.Release();
        lockHandle.Release();
        Assert.That(lockHandle.IsHeld, Is.False);
        yield return null;
    }

- [ ] **Step 2: Run test to verify failure**

Run: Unity PlayMode tests filtered to AndroidMulticastLockPlayModeTests.

Expected: factory type is missing.

- [ ] **Step 3: Implement platform branches and manifest**

On Android, use UnityPlayer.currentActivity, Context.WIFI_SERVICE, createMulticastLock("ARKnoNIGHTS.LanLobby"), and a non-reference-counted lock. On non-Android, create no Java object.

Manifest must include INTERNET, ACCESS_WIFI_STATE and CHANGE_WIFI_MULTICAST_STATE. Set ForceInternetPermission: 1 and custom main manifest only; do not alter SDK, target platform, Unity version or packages.

- [ ] **Step 4: Run test and static manifest validation**

Run: Unity PlayMode test above; inspect manifest for the three required permissions and Unity placeholders.

Expected: Editor path executes no Android calls; manifest is valid and no unrelated permission is added.

- [ ] **Step 5: Commit**

    git add Assets/Game/Runtime/Lobby/AndroidMulticastLock.cs Assets/Plugins/Android/AndroidManifest.xml Assets/Game/Tests/PlayMode/Lobby/AndroidMulticastLockPlayModeTests.cs ProjectSettings/ProjectSettings.asset
    git commit -m "feat: enable Android LAN multicast discovery"

## Task 5: Import and audit the only allowed UI assets

**Files:**

- Create: scripts/ImportLobbyAssets.ps1
- Create: docs/references/ui/lobby/ASSET_MAP.md
- Create: Assets/Game/Editor/UI/LobbyAssetImportSetup.cs
- Create: Assets/Resources/UI/Lobby with imported PNGs and Unity-generated meta
- Create: Assets/Game/Tests/EditMode/Lobby/LobbyAssetMapEditModeTests.cs

**Interfaces:**

- Resources names: bg_terrain, shallow_main, room_create_btn_bg, room_join_btn_bg, create_icon, join_icon, img_player_bkg, img_player_confirmed, player_card_waiting, player_card_ready, player_card_self_frame, team_icon_frame, team_hp_back, btn_match_host_normal, btn_match_host_grey, btn_match_grey and btn_match_cancel.
- Every source is [uc]autochessouter/name.png beneath the approved root.

- [ ] **Step 1: Write the failing asset provenance test**

    [Test]
    public void LobbyAssetMap_MapsEveryImportedPngToApprovedSource()
    {
        var map = File.ReadAllText(ProjectPath("docs/references/ui/lobby/ASSET_MAP.md"));
        foreach (var file in Directory.GetFiles(ProjectPath("Assets/Resources/UI/Lobby"), "*.png"))
            StringAssert.Contains(Path.GetFileName(file) + " | [uc]autochessouter/", map);
    }

- [ ] **Step 2: Run test to verify failure**

Run: Unity EditMode tests filtered to LobbyAssetMapEditModeTests.

Expected: map and assets are absent.

- [ ] **Step 3: Implement white-list import**

The script accepts explicit SourceRoot, resolves it, rejects a root other than G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess, and copies only the declared 17 PNG names from [uc]autochessouter. It fails if any source is missing. It never uses reference screenshots or generated substitutes.

The asset map lists source-relative path, Resources destination, visible role and stretch mode. The editor import script sets each item to a single Sprite with alpha transparency, no mipmaps and preserved dimensions.

- [ ] **Step 4: Run import and verification**

Run:

    powershell -ExecutionPolicy Bypass -File scripts/ImportLobbyAssets.ps1 -SourceRoot 'G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess'

Then let Unity create metas and run LobbyAssetMapEditModeTests.

Expected: every imported Sprite loads, and every source maps to the approved autochessouter folder.

- [ ] **Step 5: Commit**

    git add scripts/ImportLobbyAssets.ps1 docs/references/ui/lobby/ASSET_MAP.md Assets/Game/Editor/UI/LobbyAssetImportSetup.cs Assets/Resources/UI/Lobby Assets/Game/Tests/EditMode/Lobby/LobbyAssetMapEditModeTests.cs
    git commit -m "feat: import audited LAN lobby UI assets"

## Task 6: Build the home and room views

**Files:**

- Create: Assets/Game/Runtime/Initial/LanLobbyLayout.cs
- Create: Assets/Game/Runtime/Initial/LanLobbyView.cs
- Create: Assets/Game/Tests/EditMode/Lobby/LanLobbyLayoutEditModeTests.cs
- Create: Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs
- Modify: Assets/Game/Tests/PlayMode/Battle/ARKnoNIGHTS.Battle.PlayModeTests.asmdef

**Interfaces:**

- Produces ShowHome, ShowRoom, BindDiscoveredRooms, BindRoom, SetStatus and SetLocalLatency.
- Events: CreateRequested, JoinRequested(string), ProfileSaved(LobbyProfile), ReadyRequested(bool), LeaveRequested and StartRequested.

- [ ] **Step 1: Write failing layout and prefill tests**

    [Test]
    public void RoomCards_FourPlayersStayWithin1920Width()
    {
        var layout = LanLobbyLayout.ForSize(1920, 1080, 4);
        Assert.That(layout.Cards.Count, Is.EqualTo(4));
        Assert.That(layout.Cards.All(card => card.Left >= 0 && card.Right <= 1920), Is.True);
    }

    [UnityTest]
    public IEnumerator ClickingDiscoveredRoom_PrefillsButDoesNotJoin()
    {
        view.BindDiscoveredRooms(new[] { Discovery("654321") });
        view.ClickDiscoveredRoomForTests("654321");
        Assert.That(view.RoomCodeTextForTests, Is.EqualTo("654321"));
        Assert.That(joinRequests, Is.Empty);
        yield return null;
    }

- [ ] **Step 2: Run tests to verify failure**

Run: Unity EditMode LanLobbyLayoutEditModeTests, then PlayMode LanLobbyViewPlayModeTests.

Expected: layout and view are missing.

- [ ] **Step 3: Implement the view**

Create a 1920×1080 ScreenSpaceOverlay canvas at order 1000. Use only Task 5 resources: deep grid/terrain background; large cyan create and orange join terminal cards; left identity panel; discovered-room list; six-digit input; four fixed room cards; host, ready and latency state.

Use existing formal UI/numeric fonts. Clicking a discovered item changes only the code input. Joining is disabled for unknown, full or started entries and the displayed message states the reason. Do not render player illustrations, excluded reference areas, IP address or port.

- [ ] **Step 4: Run tests to verify pass**

Run: both view test groups.

Expected: 1920×1080 and 1280×720 fit; prefill does not join; room permissions, four cards, ready color and top-left latency are correct; each Sprite name is in ASSET_MAP.

- [ ] **Step 5: Commit**

    git add Assets/Game/Runtime/Initial/LanLobbyLayout.cs Assets/Game/Runtime/Initial/LanLobbyView.cs Assets/Game/Tests/EditMode/Lobby/LanLobbyLayoutEditModeTests.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs Assets/Game/Tests/PlayMode/Battle/ARKnoNIGHTS.Battle.PlayModeTests.asmdef
    git commit -m "feat: build LAN lobby and room views"

## Task 7: Wire controller and reversible battle gate

**Files:**

- Create: Assets/Game/Runtime/Initial/LanLobbyController.cs
- Create: Assets/Game/Tests/PlayMode/Lobby/LanLobbyControllerPlayModeTests.cs
- Modify: Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs

**Interfaces:**

- Produces PreparationBattleLoopController.SetLobbyGate(bool) and IsLobbyGateActive.
- Controller owns host/client/discovery; it never writes PlayerState.

- [ ] **Step 1: Write failing gate test**

    [UnityTest]
    public IEnumerator LobbyGate_FreezesPreparationUntilHostStart()
    {
        loop.SetLobbyGate(true);
        var before = loop.RemainingPreparationSeconds;
        yield return new WaitForSecondsRealtime(1f);
        Assert.That(loop.RemainingPreparationSeconds, Is.EqualTo(before).Within(0.05f));
        controller.ReceiveStartForTests();
        yield return null;
        Assert.That(loop.IsLobbyGateActive, Is.False);
        Assert.That(loop.RemainingPreparationSeconds, Is.EqualTo(30f).Within(0.05f));
        Assert.That(view.gameObject.activeSelf, Is.False);
    }

- [ ] **Step 2: Run test to verify failure**

Run: Unity PlayMode tests filtered to LanLobbyControllerPlayModeTests.

Expected: gate and controller do not exist.

- [ ] **Step 3: Implement minimum scene integration**

    public bool IsLobbyGateActive => lobbyGateActive;

    public void SetLobbyGate(bool active)
    {
        lobbyGateActive = active;
        if (!active && initialized && machine.Phase == LocalBattlePhase.Preparation)
            machine.EnterPreparation();
    }

    private void Advance(float unscaledSeconds)
    {
        if (lobbyGateActive) return;
        // retain the existing transition logic below.
    }

The idempotent bootstrap creates exactly one LanLobbyRoot, waits for the existing loop, gates it, begins discovery and renders Home. Controller stores only LanLobby.Profile.Name and LanLobby.Profile.AvatarIndex in PlayerPrefs. On Start: stop discovery/network, release multicast, ungate/reset the preparation loop, then hide lobby canvas. On leave, rejection, disconnect and destroy: cancel services, release lock and show Home. Never modify FormalBattleHudUi005.cs, UI005CaptureSuite.cs, SampleScene.unity, PlayerState or battle results.

- [ ] **Step 4: Run tests to verify pass**

Run: LanLobbyControllerPlayModeTests and existing StagingHudScenePlayModeTests.

Expected: countdown freezes in lobby, reset is 30 seconds after Start, leave/disconnect returns Home, reload creates one root only, existing HUD tests remain green.

- [ ] **Step 5: Commit**

    git add Assets/Game/Runtime/Initial/LanLobbyController.cs Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyControllerPlayModeTests.cs
    git commit -m "feat: gate local battle behind LAN lobby"

## Task 8: Capture evidence and complete validation

**Files:**

- Create: Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs
- Create: Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs
- Create: scripts/ExportLanLobbyEvidence.ps1
- Create: docs/LAN-LOBBY-REPORT.md
- Modify: docs/ARCHITECTURE.md
- Modify: docs/TEST_PLAN.md

**Interfaces:**

- Player command: -lanLobbyCaptureSuite -lanLobbyCaptureOutput ignored-output-directory.
- Manifest records image path/resolution, canvas scale, room code, members/ready states, local latency, key screen rects, Sprite names and ASSET_MAP source paths.

- [ ] **Step 1: Write failing capture suite test**

    [UnityTest]
    public IEnumerator CaptureSuite_EmitsHomeAndRoomRecords()
    {
        yield return CaptureLobbyForTests();
        var manifest = LoadLobbyManifest();
        CollectionAssert.AreEquivalent(
            new[] { "home", "discovered-prefill", "room-host", "room-ready", "room-full" },
            manifest.captures.Select(x => x.name));
        Assert.That(manifest.captures.All(x => x.spriteSources.All(IsMappedLobbySprite)), Is.True);
    }

- [ ] **Step 2: Run test to verify failure**

Run: Unity PlayMode tests filtered to LanLobbyCaptureSuitePlayModeTests.

Expected: capture suite and manifest DTO are absent.

- [ ] **Step 3: Implement evidence outputs**

The capture suite activates only by command-line flag; it writes five named PNGs and one JSON manifest to an ignored directory. Export script reads the manifest, rejects unmapped source assets and creates reference side-by-side sheets. Update architecture with one-way dependency: Lobby -> Initial presentation -> existing local battle entrance. Update test plan with exact Unity test, Player capture and two-device manual commands. Report automated pass/fail and Windows–Android manual verification separately.

- [ ] **Step 4: Run validation queue**

Run relevant nonzero-count EditMode and PlayMode tests, build Windows Player using the discovered existing build entry, run Player capture suite, run evidence export, then inspect final diff. Record exact executables, commands, exit codes, test count/failure count and log paths in the report. Mark blocked Unity, Android or device validation as unverified.

- [ ] **Step 5: Perform same-Wi-Fi acceptance and commit**

On one non-isolated Wi-Fi: create on Windows; discover, prefill and join on Android; verify latency; ready both; start; verify both hide lobby and begin 30-second preparation; disconnect Android; verify Windows returns to a recoverable room. Record firewall/router/AP isolation and Android build details.

    git add Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs scripts/ExportLanLobbyEvidence.ps1 docs/ARCHITECTURE.md docs/TEST_PLAN.md docs/LAN-LOBBY-REPORT.md
    git commit -m "test: document LAN lobby acceptance evidence"

## Plan Self-Review

- Coverage: Tasks 1–3 implement bounded LAN discovery/TCP state; Task 4 covers Android; Task 5 enforces exclusive source assets; Task 6 creates requested UI; Task 7 prevents background battle progression and enters the existing local loop; Task 8 validates and documents.
- Consistency: later tasks use only types declared in earlier tasks. No task asks for a third-party dependency, online service, real-time battle synchronization or edits to current user-modified UI files.
- Stop conditions: stop and request approval if a package, external service, account, system privilege, Unity/package upgrade, asset outside the stated library, or a broad modification to the current dirty worktree becomes necessary.
