# LAN Lobby acceptance report

## Evidence contract

`LanLobbyCaptureSuite` is opt-in only. A Windows Player started with `-lanLobbyCaptureSuite -lanLobbyCaptureOutput <ignored-directory>` renders the production `LanLobbyView` in five deterministic states: `home`, `discovered-prefill`, `room-host`, `room-ready`, and `room-full`. It locates the actual Unity project only by an ancestor containing `Assets/`, `Packages/`, and `ProjectSettings/`; a copied/external Player without that root is rejected before writing. Relative outputs resolve from that discovered root and must be below its ignored `Temp/` or `Artifacts/` directory; `Assets/`, the project root, external locations, and path-prefix escapes are rejected before any directory is created. It writes PNGs and a `manifest.json`; each record contains image resolution, Canvas scale, room code, ready/member state, local latency, key UI rectangles, and the approved source mapping for every used Sprite.

Run `scripts/ExportLanLobbyEvidence.ps1 -CaptureDirectory <ignored-directory>` afterwards. Its output follows the same `Temp/`/`Artifacts/` restriction. The script rejects records whose Sprite source is not exactly in `docs/references/ui/lobby/ASSET_MAP.md`, requires exactly one exact reference filename for each of numeric suffixes `9` and `10`, then copies the manifest and writes one actual/reference side-by-side PNG per state. Home states require suffix `9`; room states require suffix `10`.

## Visual-difference evidence (2026-07-26)

The visual-difference exporter is non-blocking: it reports `ATTENTION` when a measured, unmasked region differs; it does not make a Player run or LAN room flow fail. It emits actual, normalized-reference, overlay, and heatmap PNGs, plus Markdown/JSON reports and a source-audited Sprite usage table. Masked regions are transparent black in heatmaps and excluded from measurements. Reference images are input evidence only and are never copied to or changed in their source directory.

For an isolated worktree which does not contain exact `图9.png` and `图10.png`, provide the primary worktree reference directory explicitly and treat it as read-only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory .\Artifacts\LAN-LOBBY\CapturesFinal `
  -OutputDirectory .\Artifacts\LAN-LOBBY\VisualDiff `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

Latest real evidence is captured from a visible `-force-d3d11` Windows Player (not a hidden window) at `1920x1080`; it is retained only in ignored `Artifacts/LAN-LOBBY/CapturesFinal/` and `Artifacts/LAN-LOBBY/VisualDiff/`. The Player exits `0`. The five capture names are `home`, `discovered-prefill`, `room-host`, `room-ready`, and `room-full`; the first two use `图9.png`, while the room states use `图10.png`. The exporter decodes and records native reference sizes for each export; current evidence is `2102x1149` (图9) and `2107x1153` (图10), each independently normalized on X/Y to the Player's `1920x1080` grid. All five captures are `ATTENTION` (`0.5445`/`0.5473` home difference ratios; `0.4654`–`0.4658` room difference ratios), which is expected evidence of remaining visual differences rather than a failed build or network test. The report includes 13 mapped Sprite rows; every row records its `UI/Lobby/...` Resources path, `[uc]autochessouter/...` source-relative path, imported SHA-256, capture names, and occurrence count.

## Home room-select evidence refresh (2026-07-26)

The prior root-level evidence paths above are retained as historical context only. The current Home evidence is `Artifacts/LAN-LOBBY/HomeRoomSelect/CapturesFinal/` and `Artifacts/LAN-LOBBY/HomeRoomSelect/VisualDiff/`.

- `Artifacts/LAN-LOBBY/HomeRoomSelect/WindowsStandaloneBuild.log` records Windows x86_64 `Succeeded`, `errors=0`, and `warnings=0`.
- A visible `-force-d3d11` Player produced five non-empty `1920x1080` PNGs plus `manifest.json`: `home`, `discovered-prefill`, `room-host`, `room-ready`, and `room-full`.
- The refreshed Home manifest includes 22 approved `[uc]autochessouter/room_select_*` sprites and a `Combined/[uc]autochesscommon/icon_amiy.png` avatar source. The focused PlayMode assertion verifies both required source classes.
- The non-blocking report records Home difference ratios `0.5852` (`home`) and `0.5850` (`discovered-prefill`), plus room ratios `0.4654`–`0.4658`. It retains decoded reference dimensions `图9.png` `2102x1149` and `图10.png` `2107x1153`, normalized independently to `1920x1080`.
- Inspect `CapturesFinal/home.png`, `VisualDiff/home-overlay.png`, and `VisualDiff/home-heatmap.png` for the primary visual review. `VisualDiff/visual-diff-report.md` and `.json` contain a 33-row audited source table; every row has its Resources path, source-relative path, SHA-256, capture names, and occurrence count.

## Automated result

- Red baseline: `Temp/UnityTests/20260725-184254/PlayModeResults.xml` recorded `LanLobbyCaptureSuitePlayModeTests` as `0/1` before the suite existed.
- Focused capture PlayMode: `Temp/UnityTests/20260725-192044/PlayModeResults.xml`, `1/1` passed. Its batchmode seam checks the production view's five fixture states and manifest; it uses a decodeable test probe because a batchmode backbuffer cannot produce a valid visual screenshot.
- Home room-select capture PlayMode: `Temp/UnityTests/20260726-042318/PlayModeResults.xml`, `3/3` passed. Its red baseline correctly failed because `icon_amiy` had no capture provenance mapping; the green run verifies approved `room_select_` and Combined-avatar sources in the Home manifest.
- Lobby EditMode: `Temp/UnityTests/20260725-185449/EditModeResults.xml`, `35/35` passed. Lobby PlayMode: `Temp/UnityTests/20260725-191428/PlayModeResults.xml`, `15/15` passed. The controller PlayMode fixture removes SampleScene's generic EventSystem during teardown so the following view fixture can actually exercise its owned EventSystem lifecycle.
- Windows build: set `$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\WindowsStandalone\ARKnoNIGHTS.exe'`, then run `D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -accept-apiupdate -projectPath G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby -executeMethod Task006StandaloneBuild.BuildWindowsX64 -logFile G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\WindowsStandaloneBuild-visual-diff-review.log`; the build summary is `Succeeded`, `StandaloneWindows64`, `errors=0`, `warnings=0`, output `Artifacts/LAN-LOBBY/WindowsStandalone/ARKnoNIGHTS.exe`.
- Player capture command: `ARKnoNIGHTS.exe -force-d3d11 -lanLobbyCaptureSuite -lanLobbyCaptureOutput Artifacts/LAN-LOBBY/CapturesFinal -screen-width 1920 -screen-height 1080`. It emits five decodeable, non-black `1920x1080` PNGs and manifest. A prior hidden-window run emitted all-black PNGs despite a D3D11 / RTX 4060 Ti device; the suite rejects that case by pixel-brightness validation, and only the non-hidden rerun is usable evidence.
- Evidence export is intentionally **unverified**: this worktree's `docs/references/ui/battle_hud/` contains only reference numeric suffixes `1` through `6`, not the user-required `9` and `10`. `ExportLanLobbyEvidence.ps1` stops before sheet creation rather than silently substituting another reference.
- Output-boundary regression: `scripts/TestExportLanLobbyEvidenceSmoke.ps1` passes. It confirms `Assets/` output is rejected before manifest/output writes, and missing or ambiguous exact reference candidates fail before the requested evidence directory is created.
- Visual inspection of all five `Artifacts/LAN-LOBBY/CapturesFinal/*.png` confirms the opaque blocker removes legacy Formal Battle HUD/scene leakage. `discovered-prefill` exposes `654321` without joining; `room-host`, `room-ready`, and `room-full` show the room number, top-left local latency, and the expected one/two/four member ready states. The isolated worktree itself still lacks numeric-suffix `9`/`10` sources, but the visual-difference run above uses the explicitly supplied primary-worktree references rather than substituting another image.
- Visual-difference regression: `scripts/TestExportLanLobbyEvidenceSmoke.ps1` and `scripts/TestLanLobbyVisualDiffSmoke.ps1` both pass. The latter asserts that a missing default worktree reference fails before output creation, while an explicitly supplied fixture directory with exact 图9/图10 succeeds without any reference-file mutation.

## Same-Wi-Fi Windows-Android acceptance (manual, not yet performed)

1. Build the Windows and Android Players from the same commit, then connect both physical devices to one non-isolated Wi-Fi AP.
2. On Windows create a room. On Android wait for discovery, tap it only to prefill the six-digit code, then press Join.
3. Confirm Android displays the host room and top-left latency, then ready both clients and start from the host.
4. Confirm both Players hide the lobby and enter the existing 30-second local preparation loop. Disconnect Android and confirm Windows returns to a recoverable room state.
5. Record AP client-isolation/firewall state, Android device/OS/build details, room code, observed latency, Player logs, and any router multicast limitation.

This report intentionally does not claim device validation while two physical Players on the same Wi-Fi are unavailable.
