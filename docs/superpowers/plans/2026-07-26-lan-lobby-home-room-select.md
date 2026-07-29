# LAN Lobby Home Room Select Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Rebuild the Home right-side create/join presentation with approved `room_select_` source assets while preserving the LAN-room behavior.

**Architecture:** Import only named, provenance-audited independent `room_select_` sprites and Combined avatar sprites into Resources. `LanLobbyView` composes them into a reference-proportioned Home presentation and keeps its existing public events and discovery/join validation untouched. The existing Player capture/diff pipeline supplies evidence.

**Tech Stack:** Unity 2022.3.62f1c1, uGUI, Unity Test Framework, PowerShell asset importer.

## Global Constraints

- Unpacked `room_select_` assets use only no-`$0` names; Combined assets permit `$0`/`#0`; no new art or packages.
- Preserve CreateRequested, JoinRequested, room-code prefill and all networking/room behavior.
- New image sources must map in `ASSET_MAP.md` and capture provenance; do not change scenes, Prefabs, battle HUD or PlayerState.

## Task 1: Import audited room-select and avatar sprites

**Files:**
- Modify: `scripts/ImportLobbyAssets.ps1`, `Assets/Game/Editor/UI/LobbyAssetImportSetup.cs`, `docs/references/ui/lobby/ASSET_MAP.md`
- Create: `Assets/Game/Tests/EditMode/Lobby/LobbyHomeAssetMapEditModeTests.cs`
- Create: `Assets/Resources/UI/Lobby/Home/` sprites and metas

- [ ] **Step 1: Write red asset tests**

Assert Resources loads every named `room_select_` normal sprite from the design spec and the four normal Combined avatar sprites; assert each mapped Unpacked source lacks `$0`, while Combined avatar paths start `[uc]autochesscommon/`.

- [ ] **Step 2: Run red test**

Run `LobbyHomeAssetMapEditModeTests`; expect missing resources.

- [ ] **Step 3: Implement strict import and settings**

Extend the importer whitelist with all design-listed `room_select_` normal files from `[uc]autochessouter/`, and `icon_amiy.png`, `icon_clementi.png`, `icon_kirar.png`, `icon_zumam.png` from Combined `[uc]autochesscommon/`. Refuse `$0` Unpacked paths. Import as single alpha sprites, no mipmap; map every file in `ASSET_MAP.md`.

- [ ] **Step 4: Run green audit**

Run focused EditMode tests and byte-hash every imported source against its declared root.

- [ ] **Step 5: Commit**

`git add scripts/ImportLobbyAssets.ps1 Assets/Game/Editor/UI/LobbyAssetImportSetup.cs Assets/Resources/UI/Lobby/Home Assets/Game/Tests/EditMode/Lobby/LobbyHomeAssetMapEditModeTests.cs docs/references/ui/lobby/ASSET_MAP.md; git commit -m "feat: import room select home assets"`

## Task 2: Compose the Home right-side UI

**Files:**
- Modify: `Assets/Game/Runtime/Lobby/LanLobbyView.cs`, `Assets/Game/Runtime/Lobby/LanLobbyLayout.cs`
- Modify: `Assets/Game/Tests/EditMode/Lobby/LanLobbyLayoutEditModeTests.cs`, `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`

- [ ] **Step 1: Write red layout/behavior tests**

At 1920×1080 assert named nodes `Home/RoomSelect/Create` and `Home/RoomSelect/Join` exist in the right half, all room-select Images use mapped Sprite names, avatar selector displays a Combined avatar Sprite, and `ClickDiscoveredRoomForTests("654321")` still changes only the code input with no JoinRequested.

- [ ] **Step 2: Run red tests**

Run focused layout EditMode and view PlayMode tests; expect the named composition absent.

- [ ] **Step 3: Implement minimal composition**

Replace simplified CreateRoomCard/JoinRoomCard presentation with the design-spec `room_select_` nodes in reference proportions. Keep the same button/input objects or forward listeners so CreateRequested/JoinRequested and availability rules are unchanged. Add avatar Image child selected from `UI/Lobby/Home/icon_*`; retain non-raycast decoration layers.

- [ ] **Step 4: Run green tests**

Run focused tests plus `LanLobbyControllerPlayModeTests`; verify code prefill and join gating remain unchanged.

- [ ] **Step 5: Commit**

`git add Assets/Game/Runtime/Lobby/LanLobbyView.cs Assets/Game/Runtime/Lobby/LanLobbyLayout.cs Assets/Game/Tests/EditMode/Lobby/LanLobbyLayoutEditModeTests.cs Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs; git commit -m "feat: recreate room select home interface"`

## Task 3: Capture and compare Home evidence

**Files:**
- Modify: `docs/LAN-LOBBY-REPORT.md`, `docs/TEST_PLAN.md`

- [ ] **Step 1: Add red provenance assertion**

Extend capture PlayMode tests to assert Home manifest references at least one `room_select_` source and one Combined avatar source.

- [ ] **Step 2: Run red test**

Run `LanLobbyCaptureSuitePlayModeTests`; expect old Home provenance.

- [ ] **Step 3: Generate retained evidence**

Run Lobby tests, then visible D3D11 Player capture and `ExportLanLobbyVisualDiff.ps1` into `Artifacts/LAN-LOBBY/HomeRoomSelect/`, referencing `G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud`. Inspect `home-overlay.png`, `home-heatmap.png` and the asset table; document exact path/results.

- [ ] **Step 4: Run final checks**

Run both visual/export smoke scripts, Lobby EditMode/PlayMode, `git diff --check`, and verify no generated Artifacts PNG is tracked.

- [ ] **Step 5: Commit**

`git add docs/LAN-LOBBY-REPORT.md docs/TEST_PLAN.md Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs; git commit -m "test: capture room select home evidence"`
