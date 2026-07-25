# LAN Lobby acceptance report

## Evidence contract

`LanLobbyCaptureSuite` is opt-in only. A Windows Player started with `-lanLobbyCaptureSuite -lanLobbyCaptureOutput <ignored-directory>` renders the production `LanLobbyView` in five deterministic states: `home`, `discovered-prefill`, `room-host`, `room-ready`, and `room-full`. It writes PNGs and a `manifest.json`; each record contains image resolution, Canvas scale, room code, ready/member state, local latency, key UI rectangles, and the approved source mapping for every used Sprite.

Run `scripts/ExportLanLobbyEvidence.ps1 -CaptureDirectory <ignored-directory>` afterwards. The script rejects records whose Sprite source is not exactly in `docs/references/ui/lobby/ASSET_MAP.md`, copies the manifest, and writes one actual/reference side-by-side PNG per state. Home states require reference numeric suffix `9`; room states require suffix `10`.

## Automated result

- Red baseline: `Temp/UnityTests/20260725-184254/PlayModeResults.xml` recorded `LanLobbyCaptureSuitePlayModeTests` as `0/1` before the suite existed.
- Focused capture PlayMode: `Temp/UnityTests/20260725-192044/PlayModeResults.xml`, `1/1` passed. Its batchmode seam checks the production view's five fixture states and manifest; it uses a decodeable test probe because a batchmode backbuffer cannot produce a valid visual screenshot.
- Lobby EditMode: `Temp/UnityTests/20260725-185449/EditModeResults.xml`, `35/35` passed. Lobby PlayMode: `Temp/UnityTests/20260725-191428/PlayModeResults.xml`, `15/15` passed. The controller PlayMode fixture removes SampleScene's generic EventSystem during teardown so the following view fixture can actually exercise its owned EventSystem lifecycle.
- Windows build: `D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -accept-apiupdate -projectPath G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby -executeMethod Task006StandaloneBuild.BuildWindowsX64 -logFile G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Temp\LAN-LOBBY\WindowsStandaloneBuild-final.log`; the build summary is `Succeeded`, `StandaloneWindows64`, `errors=0`, `warnings=0`, output `Temp/TASK-006/WindowsStandalone/ARKnoNIGHTS.exe`.
- Player capture command: `ARKnoNIGHTS.exe -force-d3d11 -lanLobbyCaptureSuite -lanLobbyCaptureOutput Temp/LAN-LOBBY/CapturesFinal -screen-width 1920 -screen-height 1080`. It emitted five decodeable, non-black `1920x1080` PNGs and manifest. A prior hidden-window run emitted all-black PNGs despite a D3D11 / RTX 4060 Ti device; the suite now rejects that case by pixel-brightness validation, and only the non-hidden rerun is usable evidence.
- Evidence export is intentionally **unverified**: this worktree's `docs/references/ui/battle_hud/` contains only reference numeric suffixes `1` through `6`, not the user-required `9` and `10`. `ExportLanLobbyEvidence.ps1` stops before sheet creation rather than silently substituting another reference.
- Visual inspection of all five `Temp/LAN-LOBBY/CapturesFinal/*.png` confirms the opaque blocker removes legacy Formal Battle HUD/scene leakage. `discovered-prefill` exposes `654321` without joining; `room-host`, `room-ready`, and `room-full` show the room number, top-left local latency, and the expected one/two/four member ready states. The actual/reference side-by-side comparison remains blocked only by the missing numeric-suffix `9`/`10` sources above.

## Same-Wi-Fi Windows-Android acceptance (manual, not yet performed)

1. Build the Windows and Android Players from the same commit, then connect both physical devices to one non-isolated Wi-Fi AP.
2. On Windows create a room. On Android wait for discovery, tap it only to prefill the six-digit code, then press Join.
3. Confirm Android displays the host room and top-left latency, then ready both clients and start from the host.
4. Confirm both Players hide the lobby and enter the existing 30-second local preparation loop. Disconnect Android and confirm Windows returns to a recoverable room state.
5. Record AP client-isolation/firewall state, Android device/OS/build details, room code, observed latency, Player logs, and any router multicast limitation.

This report intentionally does not claim device validation while two physical Players on the same Wi-Fi are unavailable.
