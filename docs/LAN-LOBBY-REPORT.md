# LAN Lobby acceptance report

## Evidence contract

`LanLobbyCaptureSuite` is opt-in only. A Windows Player started with `-lanLobbyCaptureSuite -lanLobbyCaptureOutput <ignored-directory>` renders the production `LanLobbyView` in five deterministic states: `home`, `discovered-prefill`, `room-host`, `room-ready`, and `room-full`. It locates the actual Unity project only by an ancestor containing `Assets/`, `Packages/`, and `ProjectSettings/`; a copied/external Player without that root is rejected before writing. Relative outputs resolve from that discovered root and must be below its ignored `Temp/` or `Artifacts/` directory; `Assets/`, the project root, external locations, and path-prefix escapes are rejected before any directory is created. It writes PNGs and a `manifest.json`; each record contains image resolution, Canvas scale, room code, ready/member state, local latency, key UI rectangles, and the approved source mapping for every used Sprite.

Run `scripts/ExportLanLobbyEvidence.ps1 -CaptureDirectory <ignored-directory>` afterwards. Its output follows the same `Temp/`/`Artifacts/` restriction. The script rejects records whose Sprite source is not exactly in `docs/references/ui/lobby/ASSET_MAP.md`, requires exactly one exact reference filename for each of numeric suffixes `9` and `10`, then copies the manifest and writes one actual/reference side-by-side PNG per state. Home states require suffix `9`; room states require suffix `10`.

## Visual-difference evidence (2026-07-26)

Full-image visual-difference metrics are non-blocking: `ATTENTION` on a broad unmasked region does not by itself make a Player run or LAN room flow fail. A task-specific report may additionally define named component, structure, action, absence, and provenance gates as blocking visual acceptance; the current Join gates are recorded below. The exporter emits actual, normalized-reference, overlay, and heatmap PNGs, plus Markdown/JSON reports and a source-audited Sprite usage table. Masked regions are transparent black in heatmaps and excluded from measurements. Reference images are input evidence only and are never copied to or changed in their source directory.

For an isolated worktree which does not contain exact `图9.png` and `图10.png`, provide the primary worktree reference directory explicitly and treat it as read-only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 `
  -CaptureDirectory .\Artifacts\LAN-LOBBY\CapturesFinal `
  -OutputDirectory .\Artifacts\LAN-LOBBY\VisualDiff `
  -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'
```

This historical root-level run was captured from a visible `-force-d3d11` Windows Player (not a hidden window) at `1920x1080`; its ignored `Artifacts/LAN-LOBBY/CapturesFinal/` and `Artifacts/LAN-LOBBY/VisualDiff/` outputs are superseded by the evidence-derived capture correction below. The Player exited `0`. The five capture names were `home`, `discovered-prefill`, `room-host`, `room-ready`, and `room-full`; the first two used `图9.png`, while the room states used `图10.png`. The exporter decoded `2102x1149` (图9) and `2107x1153` (图10), each independently normalized on X/Y to the Player's `1920x1080` grid. All five captures were `ATTENTION` (`0.5445`/`0.5473` home difference ratios; `0.4654`–`0.4658` room difference ratios), which was expected evidence of remaining visual differences rather than a failed build or network test. The report included 13 mapped Sprite rows; every row recorded its `UI/Lobby/...` Resources path, `[uc]autochessouter/...` source-relative path, imported SHA-256, capture names, and occurrence count.

## Home room-select evidence refresh (2026-07-26)

The prior root-level evidence paths above and this `Artifacts/LAN-LOBBY/HomeRoomSelect/` refresh are retained as historical context only; both are superseded by the evidence-derived capture correction below.

- `Artifacts/LAN-LOBBY/HomeRoomSelect/WindowsStandaloneBuild.log` records Windows x86_64 `Succeeded`, `errors=0`, and `warnings=0`.
- A visible `-force-d3d11` Player produced five non-empty `1920x1080` PNGs plus `manifest.json`: `home`, `discovered-prefill`, `room-host`, `room-ready`, and `room-full`.
- The refreshed Home manifest includes 22 approved `[uc]autochessouter/room_select_*` sprites and a `Combined/[uc]autochesscommon/icon_amiy.png` avatar source. The focused PlayMode assertion verifies both required source classes.
- The non-blocking report records Home difference ratios `0.5852` (`home`) and `0.5850` (`discovered-prefill`), plus room ratios `0.4654`–`0.4658`. It retains decoded reference dimensions `图9.png` `2102x1149` and `图10.png` `2107x1153`, normalized independently to `1920x1080`.
- Inspect `CapturesFinal/home.png`, `VisualDiff/home-overlay.png`, and `VisualDiff/home-heatmap.png` for the primary visual review. `VisualDiff/visual-diff-report.md` and `.json` contain a 33-row audited source table; every row has its Resources path, source-relative path, SHA-256, capture names, and occurrence count.

## Initial Home room-select action-bar evidence (2026-07-26)

This run established the Create and Join anchors. It is superseded by the later evidence below. Its focused XML was parsed from `Temp/` when run, has since been cleaned, and is non-persistent historical output rather than retained evidence; its Player/build/visual artifacts remain ignored under `Artifacts/LAN-LOBBY/HomeRoomSelectActionBars/`.

- Historical parsed-at-run results were `6/6`, `13/13`, `3/3`, and `3/3` (25 total, failed/skipped `0`) at the `Temp/ROOM-SELECT-ACTION-BARS/Final*` paths listed below. Those XML files are no longer present and must not be cited as retained proof.
- Exact focused-test commands:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform EditMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyLayoutEditModeTests' -OutputDirectory 'Temp\ROOM-SELECT-ACTION-BARS\FinalLayout' -TimeoutSeconds 900
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyViewPlayModeTests' -OutputDirectory 'Temp\ROOM-SELECT-ACTION-BARS\FinalView' -TimeoutSeconds 900
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyCaptureSuitePlayModeTests' -OutputDirectory 'Temp\ROOM-SELECT-ACTION-BARS\FinalCapture' -TimeoutSeconds 900
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' -ProjectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -TestPlatform PlayMode -TestFilter 'ArknoNights.Lobby.Tests.LanLobbyControllerPlayModeTests' -OutputDirectory 'Temp\ROOM-SELECT-ACTION-BARS\FinalController' -TimeoutSeconds 900
```
- Build command: `$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\WindowsStandalone\ARKnoNIGHTS.exe'; & 'D:\2022.3.62f1c1\Editor\Unity.exe' -batchmode -accept-apiupdate -projectPath 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby' -executeMethod Task006StandaloneBuild.BuildWindowsX64 -logFile 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\WindowsStandaloneBuild.log'`. The build reported `Succeeded`, `StandaloneWindows64`, `errors=0`, `warnings=1`; the sole warning is `Assets\Game\Runtime\Bitset\TagRegistry.cs(11,39): warning CS0414: The field 'TagRegistry.freezeAppend' is assigned but its value is never used`.
- Visible Player command: `ARKnoNIGHTS.exe -force-d3d11 -screen-width 1920 -screen-height 1080 -lanLobbyCaptureSuite -lanLobbyCaptureOutput G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\CapturesFinal`. It exited `0` and retained five decodeable `1920x1080` PNGs (`home`, `discovered-prefill`, `room-host`, `room-ready`, `room-full`) plus `CapturesFinal/manifest.json`.
- Export command: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ExportLanLobbyVisualDiff.ps1 -CaptureDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\CapturesFinal' -OutputDirectory 'G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\HomeRoomSelectActionBars\VisualDiff' -ReferenceDirectory 'G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud'`. Retained action-only review files are `VisualDiff/home-create-action-overlay.png`, `home-create-action-heatmap.png`, `home-join-action-overlay.png`, and `home-join-action-heatmap.png`; the complete report is `VisualDiff/visual-diff-report.md` and `.json`.
- Create action: actual Rect `(1154,453,717,99)`; reference Rect `(1257,482,763,105)` measured against Figure 9's `2102x1149` canvas; local `pixelDifferenceRatio=0.536508741529662`, `averageAbsoluteRgbError=27.902361598316968`, `comparedPixels=70983`.
- Join action: actual Rect `(1154,876,717,99)`; reference Rect `(1257,932,763,105)` measured against Figure 9's `2102x1149` canvas; local `pixelDifferenceRatio=0.47197216234873141`, `averageAbsoluteRgbError=29.866451591695665`, `comparedPixels=70983`.
- Manual inspection covered both action-only overlays and heatmaps. They visibly retain the captured/reference bar layers and show concentrated contour differences; the metrics are non-blocking evidence, not visual acceptance. `home` manifest records `create_icon` from `[uc]autochessouter/create_icon.png` and `join_icon` from `[uc]autochessouter/join_icon.png`; no `$0` Unpacked source occurs. The audited source table records `UI/Lobby/create_icon`, SHA-256 `AE047958EE4E7D43F368D3205110307200D30F79BCF393B36CBBDE37A8A00BA7`, 2 occurrences (`home`, `discovered-prefill`) and `UI/Lobby/join_icon`, SHA-256 `6DE45E7D0AF9FFAE47D271E6A719FE008412FF62704154D277788EA094381C09`, 2 occurrences (the same captures).
- Other decoration placement is intentionally **not accepted** in this iteration. The next visual step is to use these two calibrated bars as anchors, rotate a repeated `room_select_create_left_line` by `180°` for the Create right side, and compose Join decorations with overlap.

## Home room-select action-bar final review fix (2026-07-26)

This retained run is superseded by the evidence-derived-capture correction below. The fix moves the status line below the Join bar and the discovered-room list above it, without changing discovery, six-digit prefill, Join gating, or LAN behavior. `LanLobbyViewPlayModeTests` proves both the Home and discovered/prefilled states: the approved Join Rect remains `(1154,876,717,99)` in top-left screen coordinates, later active Home graphics do not intersect it, the discovered room Button does not intersect it, and a real `EventSystem.RaycastAll` at the bar center resolves the Join Button first and produces one `JoinRequested`.

- Persistent focused results are under `Artifacts/LAN-LOBBY/HomeRoomSelectActionBars/Verification-FinalFix-20260726-163314/`:
  - `Layout/EditModeResults.xml`: `result=Passed`, total/passed/failed/skipped `6/6/0/0`; shutdown `forced-stop-after-results` after valid non-zero XML, grace `20s`.
  - `View/PlayModeResults.xml`: `result=Passed`, `14/14/0/0`; shutdown `normal-exit-after-results`.
  - `Capture/PlayModeResults.xml`: `result=Passed`, `3/3/0/0`; shutdown `forced-stop-after-results` after valid non-zero XML, grace `20s`.
  - `Controller/PlayModeResults.xml`: `result=Passed`, `3/3/0/0`; shutdown `normal-exit-after-results`.
  - Total: `26/26` passed, failed `0`, skipped `0`. Later Unity runs did not delete these files. `Temp/` XML is not cited as retained proof.
- Each suite used `scripts/Invoke-UnityTests.ps1`, Unity `D:\2022.3.62f1c1\Editor\Unity.exe`, project `G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby`, and its exact filter (`LanLobbyLayoutEditModeTests`, `LanLobbyViewPlayModeTests`, `LanLobbyCaptureSuitePlayModeTests`, or `LanLobbyControllerPlayModeTests`), with output set to the corresponding subdirectory above.
- The fresh Windows x86_64 build is `Artifacts/LAN-LOBBY/HomeRoomSelectActionBarsFinalFix/WindowsStandalone/ARKnoNIGHTS.exe`; `WindowsStandaloneBuild.log` records `result=Succeeded`, `errors=0`, `warnings=0`, size `184751258`. Startup logged a transient licensing handshake/access-token diagnostic before entitlement resolution; it did not block compilation or the successful BuildReport, and no `error CS`, `Compilation failed`, or unhandled-exception marker occurs.
- A visible D3D11 Player ran with `-force-d3d11 -screen-width 1920 -screen-height 1080 -lanLobbyCaptureSuite -lanLobbyCaptureOutput ...\HomeRoomSelectActionBarsFinalFix\CapturesFinal -logFile ...\PlayerCapture.log`, exited `0`, and retained five decodeable non-empty `1920x1080` PNGs plus `manifest.json`. The log ends with `[LanLobby][capture.completed] count=5`; no related unhandled exception was found.
- The superseded historical report is `Artifacts/LAN-LOBBY/HomeRoomSelectActionBarsFinalFix/VisualDiff/visual-diff-report.{json,md}`. The JSON and Markdown both state the 1920×1080 top-left target, position deviation in pixels, and size deviation after the native Figure 9 crop is locally resized:
  - Create: actual/target `(1154,453,717,99)`, native reference `(1257,482,763,105)`, position `dx=0px,dy=0px`, resized-size `dw=0px,dh=0px`, ratio `0.536508741529662`, mean RGB error `27.902361598316968`.
  - Join: actual/target `(1154,876,717,99)`, native reference `(1257,932,763,105)`, position `dx=0px,dy=0px`, resized-size `dw=0px,dh=0px`, ratio `0.45764478818872123`, mean RGB error `28.189759050289037`.
- Material usage is separated into `bitmapSprites` (34 audited rows with Resources path, approved source, SHA-256, captures, and occurrences), `unityText` (four non-Sprite rows, including “创建同盟” and “加入同盟”), and `codeGeneratedGeometry` (six grouped non-bitmap rows sourced from capture manifest geometry). Unity Text has no material-library `sourcePath` and is not presented as Sprite art.
- Action bitmap audit for both `home` and `discovered-prefill` (two occurrences each):
  - `room_select_create_btn_bg_down`: `[uc]autochessouter/room_select_create_btn_bg_down.png`, SHA-256 `8709B2C46A88AD6CDA15F0F7E78C02AD78CDB09D3FA2D99F045BC556028CD149`.
  - `room_select_join_btn_bg_down`: `[uc]autochessouter/room_select_join_btn_bg_down.png`, SHA-256 `71AE8387746003F1BF0DA72A3E92A7AACDB8A908B63FC6B26C553FC779D77468`.
  - `create_icon`: `[uc]autochessouter/create_icon.png`, SHA-256 `AE047958EE4E7D43F368D3205110307200D30F79BCF393B36CBBDE37A8A00BA7`.
  - `join_icon`: `[uc]autochessouter/join_icon.png`, SHA-256 `6DE45E7D0AF9FFAE47D271E6A719FE008412FF62704154D277788EA094381C09`.
- The manifest contains no forbidden `$0`/`#0` Unpacked source. Manual inspection opened `home.png`, `discovered-prefill.png`, `home-create-action-overlay.png`, and `home-join-action-overlay.png`: neither Status nor the discovered room item covers the Join bar. The local difference metrics remain non-blocking and the remaining decorative composition is still not accepted.
- Smoke verification: `scripts/TestLanLobbyVisualDiffSmoke.ps1` and `scripts/TestExportLanLobbyEvidenceSmoke.ps1` both print `PASS` and exit `0`.

## Home room-select evidence-derived capture correction (historical; superseded for Join, 2026-07-26)

This is historical pre-reconstruction evidence. It remains useful for its action-bar baseline, but its Join hierarchy, `join_icon=4`, and `SimulationInvite` records are superseded by the 2026-07-27 Join evidence below. Proof for this historical run is limited to the focused XML under `Artifacts/LAN-LOBBY/HomeRoomSelectActionBars/Verification-EvidenceFix-20260726-165954/` and the build, Player capture, manifest, and visual report under `Artifacts/LAN-LOBBY/HomeRoomSelectActionBarsEvidenceFix/`.

- Persistent focused results are under `Artifacts/LAN-LOBBY/HomeRoomSelectActionBars/Verification-EvidenceFix-20260726-165954/`:
  - `Layout/EditModeResults.xml`: `result=Passed`, total/passed/failed/skipped `6/6/0/0`; shutdown `forced-stop-after-results` after valid XML, grace `20s`.
  - `View/PlayModeResults.xml`: `result=Passed`, `14/14/0/0`; shutdown `normal-exit-after-results`.
  - `Capture/PlayModeResults.xml`: `result=Passed`, `3/3/0/0`; shutdown `normal-exit-after-results`.
  - `Controller/PlayModeResults.xml`: `result=Passed`, `3/3/0/0`; shutdown `normal-exit-after-results`.
  - Total: `26/26` passed, failed `0`, skipped `0`.
- TDD evidence: the visual-diff smoke first failed because the old exporter accepted a manifest missing the Create action Rect; the capture suite first failed because `unityText` did not exist. A later BOM-less UTF-8 smoke failed because the common manifest reader used the Windows PowerShell legacy default encoding. After the minimal fixes, `scripts/TestLanLobbyVisualDiffSmoke.ps1`, `scripts/TestExportLanLobbyEvidenceSmoke.ps1`, and `scripts/TestLanLobbyEvidenceCommonSmoke.ps1` each print `PASS` and exit `0`.
- The fresh Windows x86_64 build is `Artifacts/LAN-LOBBY/HomeRoomSelectActionBarsEvidenceFix/WindowsStandalone/ARKnoNIGHTS.exe`. `WindowsStandaloneBuild.log` records `result=Succeeded`, `errors=0`, `warnings=1`, size `184752794`; the sole existing warning is `TagRegistry.freezeAppend` CS0414.
- A visible D3D11 Player ran with `-force-d3d11 -screen-width 1920 -screen-height 1080 -lanLobbyCaptureSuite -lanLobbyCaptureOutput ...\HomeRoomSelectActionBarsEvidenceFix\CapturesFinal -logFile ...\PlayerCapture.log`, exited `0`, and retained five decodeable non-empty `1920x1080` PNGs plus BOM-less UTF-8 `manifest.json`. The log records `[LanLobby][capture.completed] count=5`.
- Each captured action Rect declares raw `coordinateOrigin=screen-bottom-left` and `unit=px`. `Artifacts/LAN-LOBBY/HomeRoomSelectActionBarsEvidenceFix/VisualDiff/visual-diff-report.{json,md}` converts those manifest values to the normalized `1920x1080` `screen-top-left` grid before cropping:
  - Create: actual/target `(1154,453,717,99)`, native reference `(1257,482,763,105)`, locally resized reference `717x99`, comparison reference `717x99`, position `dx=0px,dy=0px`, size `dw=0px,dh=0px`, ratio `0.53635377484749869`, mean RGB error `27.899919699082879`.
  - Join: actual/target `(1154,876,717,99)`, native reference `(1257,932,763,105)`, locally resized reference `717x99`, comparison reference `717x99`, position `dx=0px,dy=0px`, size `dw=0px,dh=0px`, ratio `0.45760252454813122`, mean RGB error `28.19097060798595`.
- Material usage is manifest-derived and separated into `bitmapSprites`, `unityText`, and `codeGeneratedGeometry`:
  - This historical manifest had 128 active rendered Sprite instances aggregated to 34 audited bitmap rows with Resources path, approved source, SHA-256, captures, and occurrence count. Its `join_icon` total was `4` because `SimulationInvite` still existed; the current Join total is `2`.
  - 59 active rendered `UnityEngine.UI.Text` instances aggregate by node/text/font to 37 rows. They cover Home identity/title/input/buttons/discovery/status and the Room page; each row reports runtime `fontName`, an empty unprovable `fontResourcePath`, `hasBitmapSource=false`, and an empty `bitmapSourcePath`. Dormant/non-rendered Text is explicitly excluded.
  - Code-generated non-bitmap geometry remains six grouped rows.
- The manifest contains no forbidden `$0`/`#0` Unpacked source. Manual inspection opened `CapturesFinal/home.png`, `CapturesFinal/discovered-prefill.png`, `VisualDiff/home-create-action-overlay.png`, and `VisualDiff/home-join-action-overlay.png`; the bars are unobstructed, and the local overlays retain both captured/reference contours. Metrics remain non-blocking, and other decoration placement remains outside this iteration's acceptance.

## Home action icon/text visual-center calibration (historical action baseline; superseded for Join, 2026-07-26)

The prior action-bar evidence remains the provenance and background-Rect baseline. This calibration supersedes it for the visible placement of the two action icons and the “创建同盟” / “加入同盟” labels. It does not claim that the rest of the Home decoration is fully reproduced.

- Final focused XML is retained under `Artifacts/LAN-LOBBY/ActionContentVisualCenters/Verification-Final-20260726-183809/`: Layout `6/6`, View `14/14`, Capture `3/3`, and Controller `3/3`, for `26/26` passed with failed `0` and skipped `0`. The first earlier Controller attempt in `Verification-20260726-182355/Controller/` was marked unverified because the wrapper observed a partially written XML before `test-run` existed; its completed XML later contained `3/3` passed. A clean `Controller-Retry1` passed normally, and the final authoritative Controller run also passed normally.
- TDD evidence:
  - The first layout red run failed because the Create icon still exposed the shared anchor `(0.08,0.50)` instead of the approved top-left local placement.
  - The first visual-report red run failed because `contentVisuals` did not exist.
  - A real-capture diagnosis found that raw luminance bounds included detached thin action-background texture. A smoke fixture reproduces that contamination; the corrected icon measurement merges only compact four-neighbour components while label measurement remains unchanged.
  - The final Join-shift red run expected local X `47` but observed `45`; moving only that icon by `2 px` made the full View suite pass.
- Final retained runtime evidence is `Artifacts/LAN-LOBBY/ActionContentVisualCenters/Calibration2/`:
  - Windows x86_64 BuildReport: `Succeeded`, `errors=0`, `warnings=0`, total size `184752794`.
  - Visible D3D11 Player: exit `0`, five non-empty `1920x1080` captures, `manifest.json`, and `[LanLobby][capture.completed] count=5`.
  - Create action actual/target Rect: `(1154,453,717,99)`; position and size deviations `0 px`.
  - Join action actual/target Rect: `(1154,876,717,99)`; position and size deviations `0 px`.
- `Calibration2/VisualDiff/visual-diff-report.{json,md}` records fixed-threshold visible bounds. Coordinates are local to each `717×99` action crop:

  | Element | Expected | Figure 9 measured | Player measured | Center delta | Size delta | Result |
  | --- | --- | --- | --- | --- | --- | --- |
  | Create icon | `47,25,36,37` | `46,25,37,37` | `47,25,36,36` | `dx=0, dy=-0.5` | `dw=0, dh=-1` | PASS |
  | Create label | `109,28,148,32` | `108,27,149,34` | `109,27,149,34` | `dx=0.5, dy=0` | `dw=1, dh=2` | PASS |
  | Join icon | `47,20,44,50` | `47,20,44,50` | `47,20,44,50` | `dx=0, dy=0` | `dw=0, dh=0` | PASS |
  | Join label | `104,31,150,34` | `104,31,150,34` | `104,30,150,35` | `dx=0, dy=-0.5` | `dw=0, dh=1` | PASS |

- Measurement details: actual and reference use integer luminance `<45`. Icon rows union four-neighbour components with at least `40` pixels and component aspect ratio no greater than `4.0`; this removes detached thin bar texture without cropping the disconnected Join glyph. Label rows use all dark pixels in their dedicated search regions. Passing requires center error at most `1 px` per axis and width/height error at most `2 px`.
- Material use is unchanged and capture-derived:
  - `create_icon`: Resources `UI/Lobby/create_icon`; source `[uc]autochessouter/create_icon.png`; SHA-256 `AE047958EE4E7D43F368D3205110307200D30F79BCF393B36CBBDE37A8A00BA7`; two occurrences across `home` and `discovered-prefill`.
  - `join_icon`: Resources `UI/Lobby/join_icon`; source `[uc]autochessouter/join_icon.png`; SHA-256 `6DE45E7D0AF9FFAE47D271E6A719FE008412FF62704154D277788EA094381C09`; this historical run had four occurrences because each Home state also rendered the now-removed Simulation Invite icon.
  - `room_select_create_btn_bg_down`: source `[uc]autochessouter/room_select_create_btn_bg_down.png`; SHA-256 `8709B2C46A88AD6CDA15F0F7E78C02AD78CDB09D3FA2D99F045BC556028CD149`.
  - `room_select_join_btn_bg_down`: source `[uc]autochessouter/room_select_join_btn_bg_down.png`; SHA-256 `71AE8387746003F1BF0DA72A3E92A7AACDB8A908B63FC6B26C553FC779D77468`.
  - “创建同盟” and “加入同盟” are Unity Text, use runtime font `Novecento wide Normal Regular.woff2`, and report `hasBitmapSource=false`; they are not represented as material-library bitmaps.
  - The complete report contains `34` bitmap rows from `128` rendered Sprite instances, `37` Unity Text rows from `59` instances, and `6` code-generated geometry rows. No source contains forbidden Unpacked `$0` or `#0`.
- Manual inspection opened `Calibration2/CapturesFinal/home.png`, `discovered-prefill.png`, `VisualDiff/home-create-action-overlay.png`, and `home-join-action-overlay.png`. The corrected icons and labels are not enlarged, both bar contours remain unobstructed, and the discovered room/prefill UI stays above the Join action rather than covering it. The other Create/Join decoration still requires later visual calibration.
- `scripts/TestLanLobbyVisualDiffSmoke.ps1`, `scripts/TestExportLanLobbyEvidenceSmoke.ps1`, and `scripts/TestLanLobbyEvidenceCommonSmoke.ps1` all print `PASS` and exit `0`.
- Repository-wide suites were rerun against the final tree and remain non-green in unchanged, out-of-scope areas. `Artifacts/LAN-LOBBY/ActionContentVisualCenters/FullSuiteFinal-20260726-184300/EditMode/EditModeResults.xml` reports `118` total and `2` failures: `BattleCoreEditModeTests.Fixture_InvalidInputMatrixReturnsStructuredErrors` plus `BattleCoreEditModeTests.RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources` (the test expects empty `displayNameZhHans`, while unchanged `gopro.json` contains `狂暴的猎狗pro`). The corresponding PlayMode XML reports `39` total and `1` failure: `PreparationBattleLoopPlayModeTests.SampleScene_AutoLoopsPreparationToBattleAndBackWithoutWritingCombatResultToPlayerState` expected `Preparation` and observed `Battle`. These existing failures block a whole-repository green claim and automatic branch integration; they do not occur in files modified by this UI calibration.

## Automated result (historical context)

All `Temp/UnityTests/...` XML paths in the following four bullets were parsed at run time, have since been cleaned, and are non-persistent historical output. They cannot be cited as current proof. The two 2026-07-26 `HomeRoomSelectActionBars*` roots remain historical action-bar evidence; current Join proof is the `JoinDecoration/Verification-Final` record below.

- Historical run-time parsed red baseline (cleaned; not current proof): `Temp/UnityTests/20260725-184254/PlayModeResults.xml` recorded `LanLobbyCaptureSuitePlayModeTests` as `0/1` before the suite existed.
- Historical run-time parsed focused capture PlayMode (cleaned; not current proof): `Temp/UnityTests/20260725-192044/PlayModeResults.xml` recorded `1/1` passed. Its batchmode seam checked the production view's five fixture states and manifest; it used a decodeable test probe because a batchmode backbuffer cannot produce a valid visual screenshot.
- Historical run-time parsed Home room-select capture PlayMode (cleaned; not current proof): `Temp/UnityTests/20260726-042318/PlayModeResults.xml` recorded `3/3` passed. Its red baseline correctly failed because `icon_amiy` had no capture provenance mapping; the green run verified approved `room_select_` and Combined-avatar sources in the Home manifest.
- Historical run-time parsed lobby suites (cleaned; not current proof): `Temp/UnityTests/20260725-185449/EditModeResults.xml` recorded `35/35` EditMode passed, and `Temp/UnityTests/20260725-191428/PlayModeResults.xml` recorded `15/15` PlayMode passed. The controller PlayMode fixture removed SampleScene's generic EventSystem during teardown so the following view fixture could exercise its owned EventSystem lifecycle.
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

## Create open-frame correction — final retained evidence (2026-07-27)

Authoritative final record: commit `e3a99cbd6645ad3505bfdd42f71140dc1afb7b52` (`fix: raise open frame visibility`), following Task 2 commit `43ace20295ebcc0978c32d4e4654571c090ae6e7`. Cycle 2 is final. Focused contracts, build, Player capture, provenance, and evidence smokes passed, but real-Player Create-frame visual acceptance **FAILED**; the Create UI is not accepted or complete.

- Retained evidence: `Artifacts/LAN-LOBBY/CreateOpenFrameCorrection/WindowsStandaloneBuild-2.log`, `WindowsStandalone-2/ARKnoNIGHTS.exe`, `PlayerCapture-2.log`, `Captures-2/manifest.json`, `VisualDiff-2/visual-diff-report.{json,md}`, and `Verification-Final/{Layout,View,Capture,Controller}/`.
- Build/Player/captures passed: Windows x86_64 build `Succeeded`, `errors=0`, `warnings=0`, `totalSize=184844394`; visible D3D11 Player exit `0`, `[LanLobby][capture.completed] count=5`; five non-empty decodeable `1920x1080` PNGs (`home`, `discovered-prefill`, `room-host`, `room-ready`, `room-full`).
- Action/content contract passed: Create `(1154,453,717,99)` and Join `(1154,876,717,99)` have zero position/size deviation. Create icon/label center deltas are `0,-0.5` / `0.5,0`, size deltas `0,-1` / `1,2`; Join icon/label center deltas `0,0` / `0,-0.5`, size deltas `0,0` / `0,1`; all four rows passed.
- Semantic lower boundary passed: `bottomBoundary` is `action-bar` / `home-create-action`, visible top `460`, frame-local `212`, zero deviations, `contentPassed=true`, `passed=true`; no `bottom` edge remains. This supersedes the closed-frame bottom gate.
- Cycle 2 frame result: top `2973`, coverage `0.9625`, gap `16`, contrast `15.31/18` — **FAILED**; left `2772`, `1`, `0`, `20.24/18` — passed; right `2207`, `1`, `0`, `12.30/18` — **FAILED**; top-left joint `496`, `1`, `0`, `22.77/18` — passed; top-right joint `228`, `0.7857`, `3`, `20.51/18` — passed. `createFrame.passed=false`: top continuity/contrast and right contrast fail, while bottom boundary and actions pass.
- Provenance/backing passed: `doc_frame_line` source `[uc]autochessouter/doc_frame_line.png`, project source `Assets/Resources/UI/Lobby/Home/doc_frame_line.png`, SHA-256 `4E4D96093514340112A0799D61611A65DA41153ACBD21F271184E0C0BB311C97`; seven frame nodes per Home state (`Top_0`, `Top_1`, `Top_2`, `LeftUpper`, `LeftLower`, `RightUpper`, `RightLower`), `14` aggregate; `img_pointer=8`; active `room_select_create_logo=0`. Sole Create sprite-null backing: `InteriorBacking`, `code-native-geometry`, `isBitmap=false`, `#000000C7`, `(1179,620,666,224)`.
- Final focused XML and matching summaries passed: Layout `6/6/0/0`, View `14/14/0/0`, Capture `3/3/0/0`, Controller `3/3/0/0`, aggregate `26/26`, failed/skipped `0`. `TestLanLobbyVisualDiffSmoke.ps1`, `TestExportLanLobbyEvidenceSmoke.ps1`, and `TestLanLobbyEvidenceCommonSmoke.ps1` each printed `PASS` and exited `0`.

Manual inspection of `VisualDiff-2/home-actual.png` and the Create-frame actual/reference/overlay/heatmap agrees: the action bar visibly provides the lower boundary without a cyan bottom segment, but the top and right strokes remain faint. The permitted two cycles are exhausted; a further tint attempt or Cycle 3 requires a new architectural/acceptance decision.

## Home Join decoration — final retained evidence (current authoritative, 2026-07-27)

Authoritative Join record: commit `b645e5bed8db106f23a7a73f55b69ba75468b987` (`fix: align join block topology`). The Figure 9 Join upper decoration, room-code input background, centered `输入同盟密钥` placeholder, removed `SimulationInvite`, repeated-Sprite inventory, accepted Join action, material provenance, outer bounds, and blocking internal block topology pass. This result does not repair or supersede the separate Create open-frame visual failure above; it proves only that the Join work left Create unchanged.

### Final build and visible Player capture

- Windows x86_64 build output: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/WindowsStandalone/ARKnoNIGHTS.exe`.
- Build log: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/WindowsStandaloneBuild.log`.
- Unity process exit: `0`.
- BuildReport: `result=Succeeded`, `errors=0`, `warnings=0`, `totalSize=184845930`, `totalTime=00:00:04.3866279`.
- The Player was launched visibly with D3D11 at `1920×1080`, without `-batchmode`, a hidden-window argument, or a background service.
- Player evidence: PID `50776`, current/Explorer/Player sessions `1/1/1`, exit `0`.
- Player log: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/PlayerCapture.log`; it records `[LanLobby][capture.completed] count=5` and has zero case-insensitive `error|exception|warning` matches.
- Capture directory: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/Captures`.
- Manifest: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/Captures/manifest.json`; `119250` bytes, BOM-less UTF-8, valid JSON, exactly five capture records.

| Capture | Exact path | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `home` | `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/Captures/home.png` | 658906 | `9464B159A9C16F989C54E9105F658A91F74A0873A3E3575D15CD3573DFE3B485` |
| `discovered-prefill` | `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/Captures/discovered-prefill.png` | 665273 | `B48C3F96890B8B4ED46818EE10C4E72255ADC164456F000DFD199F7BFC8F96BD` |
| `room-host` | `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/Captures/room-host.png` | 740070 | `487E7E99397377F7C22316E4EC71CF902EE745DA7C7B22D690D35F7641DD8395` |
| `room-ready` | `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/Captures/room-ready.png` | 691294 | `73B5CD77DFD4866015ACDCFB9B2264743FE5A731B6F2F8B3F97AE43F059C033F` |
| `room-full` | `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/Captures/room-full.png` | 700509 | `BB2E6C8BF8BD549F8B149FB3582EDCA376C1C216BF0BF22815533FAB85A8DBC1` |

Every PNG is non-empty, decodes successfully, and is exactly `1920×1080`.

### Final Join VisualDiff

Exporter exit: `0`. Exact retained files:

- JSON: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/VisualDiff-CentralAnchorFix-Final/visual-diff-report.json`;
- Markdown: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/VisualDiff-CentralAnchorFix-Final/visual-diff-report.md`;
- actual crop: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/VisualDiff-CentralAnchorFix-Final/home-join-decoration-actual.png`;
- normalized Figure 9 crop: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/VisualDiff-CentralAnchorFix-Final/home-join-decoration-reference.png`;
- overlay: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/VisualDiff-CentralAnchorFix-Final/home-join-decoration-overlay.png`;
- heatmap: `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Cycle-1/VisualDiff-CentralAnchorFix-Final/home-join-decoration-heatmap.png`.

The native Figure 9 crop is `(1257,635,763,297)` on `2102×1149`. The normalized actual/target crop and Join backing are `(1154,596,717,280)` on `1920×1080`, ending at the accepted Join action top `y=876`. All seven adjusted decoded-pixel rows are measurable and pass:

| Row | Reference adjusted | Player adjusted | Center delta | Size delta | Tolerance | Result |
| --- | --- | --- | --- | --- | ---: | --- |
| `logo` | `(91,64,119,20)` | `(92,64,116,21)` | `(0,0.5)` | `(-2,+1)` | `2 px` | PASS |
| `text-01` | `(391,56,65,8)` | `(391,56,65,10)` | `(0,+1)` | `(0,+2)` | `2 px` | PASS |
| `text-02` | `(526,62,89,11)` | `(527,62,88,10)` | `(+0.5,-0.5)` | `(-1,-1)` | `2 px` | PASS |
| `triangle` | `(338,47,30,17)` | `(337,47,32,17)` | `(0,0)` | `(+2,0)` | `2 px` | PASS |
| `central-blank` | `(323,68,60,61)` | `(324,65,59,63)` | `(+0.5,-2)` | `(-1,+2)` | `2 px` | PASS |
| `block-bank` | `(45,107,639,89)` | `(45,106,639,91)` | `(0,0)` | `(0,+2)` | `4 px` | PASS |
| `input` | `(115,204,482,60)` | `(115,204,482,60)` | `(0,0)` | `(0,0)` | `2 px` | PASS |

`joinDecoration.passed=true`. Its blocking structural results are:

- backing target passed with zero deviation;
- backing bottom is `876`; no Join geometry crosses it;
- Graphic/geometry boundary data is available and no Join decoration Graphic or geometry crosses the fixed action boundary `y=876`;
- `SimulationInvite` is absent;
- `OutlineBottom` is absent;
- accepted `home-join-action` passes at `(1154,876,717,99)` with unchanged icon/label content;
- required repeated Sprite inventory and approved provenance pass.

The central-blank detector now isolates the four fixed-reference quadrants from neighboring middle-block orange. Its final raw actual bounds are `(324,69,58,58)`; the adjusted row above remains within the unchanged `2 px` tolerance.

The `block-bank` row also contains a nested blocking `internalTopology` check, which prevents the former outer-union false positive. Its ROI is `(35,107,660,89)`; a column is occupied when at least three pixels satisfy `R>=100`, `R-G>=15`, and `B<=130`. The acceptance gates are maximum span-edge deviation `4 px`, maximum occupied-column-count delta `20`, and minimum profile Jaccard `0.95`.

| Topology | Runs | Occupied columns |
| --- | --- | ---: |
| Figure 9 reference | `150..219`, `222..587` | 436 |
| Post-review Player | `150..219`, `222..483`, `485..587` | 435 |

The final comparison has Jaccard `0.997706`, count delta `-1`, and start/end/width deltas `0/0/0`; both the outer bounds and internal topology pass.

The placeholder was independently measured inside the `717×280` crop. Final actual bounds are `(273,220,165,27)` versus normalized Figure 9 `(272,221,167,25)`; both centers are exactly `(355,233)`. The text is centered and does not overlap the baked input icon.

### Complete visible-Player history and approved exception

1. `Cycle-1`: build/capture produced five valid Player screenshots, but the real manifest lacked the explicit coordinate/raycast schema assumed by the synthetic fixture. Export failed before output (`spriteName` property mismatch); `Cycle-1/VisualDiff` is absent. Cycle 1 is preserved as invalid schema evidence, not visual acceptance.
2. `Cycle-2`: numeric build exit `0`, visible Player exit `0`, and five captures succeeded. The first `Cycle-2/VisualDiff` report failed because fixed-reference detector thresholds made `logo`, `text-02`, `block-bank`, and `input` unavailable. After the detector-only correction, `Cycle-2/VisualDiff-DetectorFix-R2` made all seven rows measurable, but `triangle` and `block-bank` still failed their unchanged tolerances; manual Join acceptance remained failed.
3. `Cycle-3`: the final planned geometry correction changed only the measured triangle and outer block placement. `Cycle-3/VisualDiff` passed all seven rows and every then-existing structural gate, but manual inspection retained a placeholder horizontal-position/font-size caveat.
4. `Verification-Final`: after the focused placeholder-only correction at `24806571`, a fresh build, visible Player, five captures, manifest, exporter, manual inspection, and placeholder measurement passed the then-existing gates. This was the fourth visible Player run. The old statement that it was “not a fourth calibration cycle” was incorrect and is retained as a process deviation. Final review later showed that its outer block union hid a compressed topology: reference runs `150..219/222..587`, `436` columns; actual run `214..517`, `304` columns; Jaccard `0.689498`, count delta `-132`, and start/end deltas `+64/-70`.
5. The user then authorized exactly one additional post-review correction cycle. `PostReview-Cycle-1` was the fifth visible Player run and the only run under that exception. The retained middle Rects are `(left,top,width)` `271,118,74`, `343,118,106`, `504,118,108`, and `606,118,108`. Its first report passed the corrected topology but falsely expanded `central-blank` because neighboring `MiddleBlock_2` entered the broad detector. The evidence-only central-anchor fix replayed the same retained screenshot into `VisualDiff-CentralAnchorFix-Final`; it did not rebuild, recapture, alter runtime, or create a sixth Player run.

No second post-review Player, build, capture, or runtime correction was performed.

### Complete final bitmap material table

The final report contains `36` bitmap rows, `36` active Unity Text rows, and `8` code-native geometry rows. Every bitmap row below has a nonempty Resources path, approved source path, uppercase 64-hex imported SHA-256, capture list, and positive rendered occurrence count.

| Sprite | Resources path | Approved source | Imported SHA-256 | Captures | Occurrences |
| --- | --- | --- | --- | --- | ---: |
| `bg_terrain` | `UI/Lobby/bg_terrain` | `[uc]autochessouter/bg_terrain.png` | `ECE7B6159268276287C20E3B3A82A5165BCC1D344EDFA6DE3B88EE24A76F988C` | `discovered-prefill, home, room-full, room-host, room-ready` | 5 |
| `btn_match_cancel` | `UI/Lobby/btn_match_cancel` | `[uc]autochessouter/btn_match_cancel.png` | `6DA0D4FD99A7A3595F5FE3C79ABA99A1F41006A4114E7555D324E059419A97B3` | `room-full, room-host, room-ready` | 3 |
| `btn_match_grey` | `UI/Lobby/btn_match_grey` | `[uc]autochessouter/btn_match_grey.png` | `E774CB0533EB67BD2FE45F50339E36D0A6221BAF594E6AEE5E5C256469A4BA78` | `discovered-prefill, home, room-full, room-host, room-ready` | 7 |
| `btn_match_host_grey` | `UI/Lobby/btn_match_host_grey` | `[uc]autochessouter/btn_match_host_grey.png` | `C4CD3326EA4D04777AAA540525405DF8AA217D2E972443FDE95C202333F93614` | `room-full, room-host, room-ready` | 3 |
| `create_icon` | `UI/Lobby/create_icon` | `[uc]autochessouter/create_icon.png` | `AE047958EE4E7D43F368D3205110307200D30F79BCF393B36CBBDE37A8A00BA7` | `discovered-prefill, home` | 2 |
| `doc_frame_line` | `UI/Lobby/Home/doc_frame_line` | `[uc]autochessouter/doc_frame_line.png` | `4E4D96093514340112A0799D61611A65DA41153ACBD21F271184E0C0BB311C97` | `discovered-prefill, home` | 14 |
| `icon_amiy` | `UI/Lobby/Home/icon_amiy` | `Combined/[uc]autochesscommon/icon_amiy.png` | `14D5F8D3A8026751B511942517B9815BA3E04438857FEA649EF8A8A02B64868B` | `discovered-prefill, home` | 2 |
| `img_player_bkg` | `UI/Lobby/img_player_bkg` | `[uc]autochessouter/img_player_bkg.png` | `CB940ECA5FE84527D9AD4546C617120A90F94CD88663F98C66261EE60D45185A` | `discovered-prefill, home` | 4 |
| `img_pointer` | `UI/Lobby/Home/img_pointer` | `[uc]autochessouter/img_pointer.png` | `3CD944DC7F0F3B7DE675E8BBE23EEA9640D95DE65B4B2E91F14648385284F697` | `discovered-prefill, home` | 8 |
| `join_icon` | `UI/Lobby/join_icon` | `[uc]autochessouter/join_icon.png` | `6DE45E7D0AF9FFAE47D271E6A719FE008412FF62704154D277788EA094381C09` | `discovered-prefill, home` | 2 |
| `player_card_ready` | `UI/Lobby/player_card_ready` | `[uc]autochessouter/player_card_ready.png` | `F34786A3E832E97121EB03614B6D584C871B78E4C5CDD1FA6C5F9CB7D191A4C0` | `room-full, room-ready` | 4 |
| `player_card_waiting` | `UI/Lobby/player_card_waiting` | `[uc]autochessouter/player_card_waiting.png` | `3B87EBB1DD62B7A8BD4D525F7F2E7E4358F2F1C0B04A65997BA34E79E97F0C8C` | `discovered-prefill, room-full, room-host, room-ready` | 9 |
| `room_create_btn_bg` | `UI/Lobby/room_create_btn_bg` | `[uc]autochessouter/room_create_btn_bg.png` | `2782FDCBE671CDD760FF46FD2B3A83CEB3F6396BC44FF48673220C8B21521606` | `discovered-prefill, home` | 2 |
| `room_select_create_btn_bg_down` | `UI/Lobby/Home/room_select_create_btn_bg_down` | `[uc]autochessouter/room_select_create_btn_bg_down.png` | `8709B2C46A88AD6CDA15F0F7E78C02AD78CDB09D3FA2D99F045BC556028CD149` | `discovered-prefill, home` | 2 |
| `room_select_create_left_line` | `UI/Lobby/Home/room_select_create_left_line` | `[uc]autochessouter/room_select_create_left_line.png` | `4893EDC89BF8D9DFE3914673B0446D764D0663327579DAE19744E17C74296CA4` | `discovered-prefill, home` | 4 |
| `room_select_create_middleicon` | `UI/Lobby/Home/room_select_create_middleicon` | `[uc]autochessouter/room_select_create_middleicon.png` | `F728D411AA11A67775AA2A3CBBB1CBED665B914E1BE645DCCDB6BD34BCE288C2` | `discovered-prefill, home` | 2 |
| `room_select_create_text_01` | `UI/Lobby/Home/room_select_create_text_01` | `[uc]autochessouter/room_select_create_text_01.png` | `52DC9A7C8E48DEC53AAF69D91A6FD0E0AA60483C1EE1412E32A007B8DF2E2D72` | `discovered-prefill, home` | 2 |
| `room_select_create_text_02` | `UI/Lobby/Home/room_select_create_text_02` | `[uc]autochessouter/room_select_create_text_02.png` | `9C87A8FE6DFB72362BA8A84B089B66BC80F01822E2E3C0713396499D93F2CE33` | `discovered-prefill, home` | 2 |
| `room_select_dot` | `UI/Lobby/Home/room_select_dot` | `[uc]autochessouter/room_select_dot.png` | `056E14212EA8E02D175D03582C4726FABA09AD518E89DB318CD3EF793E996DB0` | `discovered-prefill, home` | 10 |
| `room_select_img_startroom` | `UI/Lobby/Home/room_select_img_startroom` | `[uc]autochessouter/room_select_img_startroom.png` | `495AA8F2BD5CD97EE12192DACC2CFD8A15731F0E131F9CD74F936B92A49E7E02` | `discovered-prefill, home` | 2 |
| `room_select_join_ban` | `UI/Lobby/Home/room_select_join_ban` | `[uc]autochessouter/room_select_join_ban.png` | `F1ACA192CCCD6399884810A52CDC15E733C415E83579325779E67EB700EB052E` | `discovered-prefill, home` | 8 |
| `room_select_join_blank` | `UI/Lobby/Home/room_select_join_blank` | `[uc]autochessouter/room_select_join_blank.png` | `099A060B78BCA5E39CA82E9747C94BFBC011868DE4AC18CB11C9149BC99AA2CD` | `discovered-prefill, home` | 2 |
| `room_select_join_btn_bg_down` | `UI/Lobby/Home/room_select_join_btn_bg_down` | `[uc]autochessouter/room_select_join_btn_bg_down.png` | `71AE8387746003F1BF0DA72A3E92A7AACDB8A908B63FC6B26C553FC779D77468` | `discovered-prefill, home` | 2 |
| `room_select_join_left_block` | `UI/Lobby/Home/room_select_join_left_block` | `[uc]autochessouter/room_select_join_left_block.png` | `1D10B384025DCF05972D7AEAFDF438FD88DB9B3B9B829DFD541E15103F100D10` | `discovered-prefill, home` | 4 |
| `room_select_join_logo` | `UI/Lobby/Home/room_select_join_logo` | `[uc]autochessouter/room_select_join_logo.png` | `85FB957F0BC4A5172B0F454F77F6195068484B6DEBBD6DFCEE2A2AD1D5D93B59` | `discovered-prefill, home` | 2 |
| `room_select_join_middle_block` | `UI/Lobby/Home/room_select_join_middle_block` | `[uc]autochessouter/room_select_join_middle_block.png` | `997CF5A781848535654D21D9B97D6105DA07F959AECB134DF1FB2F570E9861CA` | `discovered-prefill, home` | 8 |
| `room_select_join_middle_block_mask` | `UI/Lobby/Home/room_select_join_middle_block_mask` | `[uc]autochessouter/room_select_join_middle_block_mask.png` | `95D0FAAF36EEF6681486944D95AB3F453DB0D9B70F23CD2E0DE12F57E2609EC5` | `discovered-prefill, home` | 2 |
| `room_select_join_right_block` | `UI/Lobby/Home/room_select_join_right_block` | `[uc]autochessouter/room_select_join_right_block.png` | `11C872C6DE561E4409085E958D1CDEFA7647E9EEC5883ED91E45162B2B9FE6B8` | `discovered-prefill, home` | 4 |
| `room_select_join_text_01` | `UI/Lobby/Home/room_select_join_text_01` | `[uc]autochessouter/room_select_join_text_01.png` | `F09FD74598C6EEF1066FB53CDA294681FAEA469981B6F215B7E7DD674E63CC39` | `discovered-prefill, home` | 2 |
| `room_select_join_text_02` | `UI/Lobby/Home/room_select_join_text_02` | `[uc]autochessouter/room_select_join_text_02.png` | `F9DCC617D9BB74218E1554939A0897F39516B7965D9400EAD6CCB2DE85E19DD0` | `discovered-prefill, home` | 2 |
| `room_select_join_text_bg` | `UI/Lobby/Home/room_select_join_text_bg` | `[uc]autochessouter/room_select_join_text_bg.png` | `36260875697359E27930467D123D2B684A2DE51D2448A5B295885D06AA518472` | `discovered-prefill, home` | 2 |
| `room_select_join_triangle` | `UI/Lobby/Home/room_select_join_triangle` | `[uc]autochessouter/room_select_join_triangle.png` | `BA585545BC5EF8F6CC126BBFDE59F63A1C75D4B761646A22CCFEA195CB9B7FF7` | `discovered-prefill, home` | 2 |
| `room_select_right_bg` | `UI/Lobby/Home/room_select_right_bg` | `[uc]autochessouter/room_select_right_bg.png` | `F65BE15390749F0FA175B49C310E90E9F3A29C753F2D320068B0831A5EBFDE53` | `discovered-prefill, home` | 2 |
| `room_select_title_icon` | `UI/Lobby/Home/room_select_title_icon` | `[uc]autochessouter/room_select_title_icon.png` | `7C0E9E67D349013FC49DBAF33E1F462C0DF4171B1C6681DB50517629B4BDFF6C` | `discovered-prefill, home` | 2 |
| `shallow_main` | `UI/Lobby/shallow_main` | `[uc]autochessouter/shallow_main.png` | `054110DDEE56F1D19FAFA846D821E6CBD83D47BEB70D4C399A84DA4035A11945` | `room-full, room-host, room-ready` | 3 |
| `team_icon_frame` | `UI/Lobby/team_icon_frame` | `[uc]autochessouter/team_icon_frame.png` | `B05BFEAE52C1E54E9382936F653E291D118A976DA0B1AA6DE14720FC9CCC4380` | `discovered-prefill, home, room-full, room-host, room-ready` | 14 |

No bitmap source path contains `$0`, `#0`, `atlas`, or `derived`. All direct UI bitmaps map to approved non-`$0` `[uc]autochessouter` files; the only Combined row is the approved avatar `Combined/[uc]autochesscommon/icon_amiy.png`. No Join atlas entry, generated bitmap, derived bitmap, or precomposited Join image was introduced.

The exact aggregate Join Sprite occurrence counts are:

| Sprite | Occurrences | Captures |
| --- | ---: | --- |
| `join_icon` | 2 | `discovered-prefill, home` |
| `room_select_join_ban` | 8 | `discovered-prefill, home` |
| `room_select_join_blank` | 2 | `discovered-prefill, home` |
| `room_select_join_btn_bg_down` | 2 | `discovered-prefill, home` |
| `room_select_join_left_block` | 4 | `discovered-prefill, home` |
| `room_select_join_logo` | 2 | `discovered-prefill, home` |
| `room_select_join_middle_block` | 8 | `discovered-prefill, home` |
| `room_select_join_middle_block_mask` | 2 | `discovered-prefill, home` |
| `room_select_join_right_block` | 4 | `discovered-prefill, home` |
| `room_select_join_text_01` | 2 | `discovered-prefill, home` |
| `room_select_join_text_02` | 2 | `discovered-prefill, home` |
| `room_select_join_text_bg` | 2 | `discovered-prefill, home` |
| `room_select_join_triangle` | 2 | `discovered-prefill, home` |

Each Home state therefore has exactly `21` Join Sprite occurrences and exactly one Join action icon; the removed Simulation Invite no longer contributes a second icon.

### Code-native geometry

The final material report contains these eight sprite-null geometry groups:

| Name | Kind | `isBitmap` | Color | Captures | Occurrences |
| --- | --- | --- | --- | --- | ---: |
| `LanLobbyRoot/OpaqueBlocker` | `code-native-geometry` | `false` | `#060F14FF` | all five | 5 |
| `LanLobbyRoot/Home/RoomSelect/Create/InteriorBacking` | `code-native-geometry` | `false` | `#000000C7` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/InteriorBacking` | `code-native-geometry` | `false` | `#000000D1` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/OutlineTop` | `code-native-geometry` | `false` | `#3030308C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/OutlineLeft` | `code-native-geometry` | `false` | `#3030308C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/OutlineRight` | `code-native-geometry` | `false` | `#3030308C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/GuideHorizontal` | `code-native-geometry` | `false` | `#FFA5008C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/GuideVertical` | `code-native-geometry` | `false` | `#FFA5008C` | `discovered-prefill, home` | 2 |

For each `home` and `discovered-prefill` manifest record, the six Join rows are exactly the backing, three outlines, and two guides above. They have `kind=code-native-geometry`, `isBitmap=false`, `raycastTarget=false`, no `spriteName`, explicit `screen-bottom-left`/`px` geometry, and remain separate from bitmap provenance.

### Final regression and Create invariance

- Fresh focused XML and matching summaries under `Artifacts/LAN-LOBBY/JoinDecoration/PostReview-Verification-Final/{Layout,View,Capture,Controller}`: Layout `6/6`, View `16/16`, Capture `3/3`, Controller `3/3`; aggregate `28/28`, failed `0`, skipped `0`, inconclusive `0`. Layout used a bounded stop only after complete results; the other three suites exited normally.
- `scripts/TestLanLobbyVisualDiffSmoke.ps1`, `scripts/TestExportLanLobbyEvidenceSmoke.ps1`, and `scripts/TestLanLobbyEvidenceCommonSmoke.ps1` each printed `PASS` and exited `0`.
- Final report audit printed `FINAL MANIFEST/REPORT AUDIT: PASS`, with `captures=5`, `joinVisualRows=7/7`, `bitmapRows=36`, `joinGeometryRowsPerHome=6`, `homeGeometryRows=8`, and `discoveredGeometryRows=8`.
- Every one of the unchanged `36` bitmap material rows has a nonempty Resources/source path, uppercase 64-hex imported hash, capture list, and positive occurrence count. Aggregate Join Sprite counts remain `join_icon=2`, `ban=8`, `blank=2`, `join_btn_bg=2`, `left=4`, `logo=2`, `middle=8`, `mask=2`, `right=4`, `text01=2`, `text02=2`, `text_bg=2`, and `triangle=2`; no source contains `$0`, `#0`, `atlas`, or `derived`.
- The retained post-review and pre-correction Create crops share SHA-256 `AB0565B99828ED3BDB8E210445BFB1C9458727A03049D303D9EF46DCA8E38FD1`; the Create comparison ROI `(1154,177,717,346)` has zero differing pixels.
- This pixel identity is an invariance check only. The historical Create report still has `createFrame.passed=false` because its top continuity/contrast and right contrast failed; this Join task does not claim those Create gates were fixed.

The retained binary operationally corresponds to commit `b645e5b`: current runtime/test files equal their exact HEAD blobs; `LanLobbyView.cs` was written at `20:11:44.610`, before the retained `Assembly-CSharp.dll` at `20:13:50.883`; and the manifest middle-block screen X/width values `1303/74`, `1375/106`, `1536/108`, `1638/108`, minus Join screen X `1032`, equal the four committed local values. The former `335/108`, `402/108`, `469/108`, `536/108` implementation cannot produce this manifest. Retained hashes are:

- `Assembly-CSharp.dll`: `8489A09B12D0068C36DBBAC29C093AD900B8D733CE75475149FB76B5F3437BB0`;
- capture manifest: `A568E44B12D61130180A5E61C6AA8E280BDFDEFC34265B147BEC7348B37AC28A`;
- accepted final JSON: `B1D09C0D5599ECB4508BA5EB139B31EEC204ADFC4E6F6DC93449C6F474900F7E`.

The same-Wi-Fi Windows/Android two-device flow remains manually unverified as recorded above. The final Player evidence proves the production UI/capture/build path and preserved LAN-facing events, but it does not substitute for the physical-device discovery/join/readiness/start/disconnect procedure.

## LAN 房间槽位状态——最终有界证据（2026-07-28，当前权威）

### 结论

LAN 房间槽位实现、Windows 构建、自动测试、截图生成和素材来源核查已验证；图11–13视觉验收仍为 **FAILED**，不是“最终成功视觉周期”。

最终 Cycle 3 的 52 个命名房间门结果为：

- `Passed`: 24
- `Failed`: 26
- `ExcludedByReferencePopup`: 2，且两项均为 `passed=false`
- 素材/来源失败：0

三次可见 Windows Player 校准启动已经全部用完：Cycle 1、2、3 分别为第 1/3、2/3、3/3 次；没有发生第四次 Player 启动，也没有在最后截图之后提交未经 Player 验证的视觉改动。

### 权威房间行为

- 创建房间时，房主初始为已准备。
- 新加入的成员初始为未准备。
- 开始游戏只要求当前房间内所有成员已准备；空槽位不参与判断，也不要求满四人。
- 仅房主一人的房间可以立即开始游戏。
- 成员操作标签为“准备就绪”与“取消准备”。
- 房主操作标签为“协议启动”。
- 非房主离开只移除该成员并恢复空槽。
- 房主离开会解散房间并停止权威房间服务。
- 不支持房主迁移或将其他成员晋升为房主。

### 图11–13边界与最终证据

`room-host` 对应图11，`room-full` 对应图12，`room-ready` 对应图13。上方滚动弹幕、图12/13右侧弹窗像素、角色立绘和资料卡内容不属于视觉验收。图11是第四个空槽的唯一无遮挡参考。

图12与图13因右侧弹窗遮挡而排除完整的第四个玩家槽，且排除项不记为通过。

视觉验收以实际渲染的可见图形为准，而不是纹理矩形或RectTransform中心。

阻塞阈值未降低：`1920×1080` 下图标/标签的可见中心每轴误差上限为 `2 px`，可见宽高误差上限为 `3 px`；长轮廓/组合槽每条可见边误差上限为 `4 px`，命名 ROI 内可见轮廓 Jaccard 下限为 `0.95`。

最终证据的绝对路径：

- 截图：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-3\Captures`
- 并排证据：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-3\Evidence`
- 阻塞报告与叠图：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-3\VisualDiff`
- 构建日志：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-3\Build.log`
- Player 日志：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-3\Player.log`

26 个失败门按捕获精确分组如下；逐门可见 bounds、边差和 Jaccard 的权威明细在 `G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\RoomSlotStates\Cycle-3\VisualDiff\visual-diff-report.json`，同一张 26 行人工可读表保留在 `.superpowers/sdd/2026-07-28-lan-room-slot-states/task-8-report.md`：

- `room-host`（8）：`RoomHost.Slot1.ReadyTopBar`、`RoomHost.Slot1.ReadyContour`、`RoomHost.Slot1.ReadyCheck`、`RoomHost.Slot1.ReadyLabel`、`RoomHost.Slot1.CreatorTag`、`RoomHost.Slot2.EmptyComposition`、`RoomHost.Slot3.EmptyComposition`、`RoomHost.Slot4.EmptyComposition`。
- `room-ready`（10）：`RoomReady.Slot1.ReadyContour`、`RoomReady.Slot1.ReadyCheck`、`RoomReady.Slot1.ReadyLabel`、`RoomReady.Slot2.ReadyContour`、`RoomReady.Slot2.ReadyCheck`、`RoomReady.Slot2.ReadyLabel`、`RoomReady.Slot3.ReadyContour`、`RoomReady.Slot3.ReadyCheck`、`RoomReady.Slot3.ReadyLabel`、`RoomReady.PrimaryAction.LabelCenter`。
- `room-full`（8）：`RoomFull.Slot1.ReadyContour`、`RoomFull.Slot1.ReadyCheck`、`RoomFull.Slot1.ReadyLabel`、`RoomFull.Slot2.WaitingTopBar`、`RoomFull.Slot2.WaitingContour`、`RoomFull.Slot3.WaitingTopBar`、`RoomFull.Slot3.WaitingContour`、`RoomFull.PrimaryAction.Gray`。

两个排除门是 `RoomReady.Slot4.ReferencePopupExclusion` 和 `RoomFull.Slot4.ReferencePopupExclusion`；它们不是通过项。

### 构建、测试与日志

Cycle 3 Windows x64 构建结果为 `Succeeded`，错误 `0`，警告 `2`（均为既有 `TagRegistry.freezeAppend` CS0414），大小 `185519546` 字节，耗时 `00:00:04.2621446`。Player 以可见 D3D11 窗口、`1920×1080` 运行并正常退出；五张 PNG 均可解码且尺寸正确，UTF-8 manifest 有五条记录，Player 日志包含 `[LanLobby][capture.completed] count=5`，未发现与本任务相关的新错误。

最终串行 Unity 测试：

| 套件/过滤器 | 结果 | 日志目录 |
| --- | ---: | --- |
| `LobbyRoomStateEditModeTests` | 15/15 | `Artifacts/LAN-LOBBY/RoomSlotStates/PrePlayer/Domain` |
| `LanSocketIntegrationEditModeTests` | 8/8 | `Artifacts/LAN-LOBBY/RoomSlotStates/PrePlayer/Socket` |
| `LobbyAssetMapEditModeTests` | 30/30 | `Artifacts/LAN-LOBBY/RoomSlotStates/PrePlayer/Assets` |
| `LanLobbyRoomLayoutEditModeTests` | 12/12 | `Artifacts/LAN-LOBBY/RoomSlotStates/PrePlayer/Layout` |
| `LanLobbyViewPlayModeTests` | 27/27 | `Artifacts/LAN-LOBBY/RoomSlotStates/PrePlayer/View` |
| `LanLobbyCaptureSuitePlayModeTests` | 4/4 | `Artifacts/LAN-LOBBY/RoomSlotStates/PrePlayer/Capture` |
| `LanLobbyControllerPlayModeTests` | 4/4 | `Artifacts/LAN-LOBBY/RoomSlotStates/PrePlayer/Controller` |

总计 100/100 通过，0 失败、0 跳过。每个目录保留 XML、日志和 `summary.txt`。本节提交前的串行复跑确认 Task 9 文档/证据 smoke 为 VisualDiff `36 fixtures / 1633 assertions`、Evidence exporter `5 / 15`、Evidence common `5 / 45`。

Windows 与 Android 同一 Wi-Fi 下的两台物理设备发现、房间号预填、加入、准备切换、开始广播、离开与房主解散流程仍未人工执行，不能由上述单机证据推断为通过。

### Cycle 3 manifest 派生的完整 bitmap 使用表

以下 46 行直接来自最终 `visual-diff-report.json` 的 `materialUsage.bitmapSprites`；捕获名即该素材的实际状态使用范围。

| Sprite | Resources path | Approved source | Imported SHA-256 | Capture/state usage | Occurrences |
| --- | --- | --- | --- | --- | ---: |
| `bg_plus` | `UI/Lobby/bg_plus` | `[uc]autochessouter/bg_plus.png` | `E2CA5554B27862FE172E2D18D50092618B2E895C2AD63CDB57019BE593B7B66D` | `room-host` | 3 |
| `bg_terrain` | `UI/Lobby/bg_terrain` | `[uc]autochessouter/bg_terrain.png` | `ECE7B6159268276287C20E3B3A82A5165BCC1D344EDFA6DE3B88EE24A76F988C` | `discovered-prefill, home, room-full, room-host, room-ready` | 5 |
| `bg_top_normal` | `UI/Lobby/bg_top_normal` | `[uc]autochessouter/bg_top_normal.png` | `5A9479B9AFDD4FC3F597CCBF4A1A0D92C1BB1B5053D716E8C20267E6B2C77C4F` | `room-full, room-host` | 6 |
| `bg_top_ready` | `UI/Lobby/bg_top_ready` | `[uc]autochessouter/bg_top_ready.png` | `EFAA99906A087AAF5AD631E4DF8CFCD7E90C4F463621779A13447675F221482D` | `room-full, room-host, room-ready` | 6 |
| `btn_match_grey` | `UI/Lobby/btn_match_grey` | `[uc]autochessouter/btn_match_grey.png` | `E774CB0533EB67BD2FE45F50339E36D0A6221BAF594E6AEE5E5C256469A4BA78` | `discovered-prefill, home, room-full` | 5 |
| `btn_match_host_grey` | `UI/Lobby/btn_match_host_grey` | `[uc]autochessouter/btn_match_host_grey.png` | `C4CD3326EA4D04777AAA540525405DF8AA217D2E972443FDE95C202333F93614` | `room-full` | 1 |
| `btn_match_host_normal` | `UI/Lobby/btn_match_host_normal` | `[uc]autochessouter/btn_match_host_normal.png` | `9D36CBDA42FC64CEB7590CBEDF5E49176BFA90E63A8B263FD3C87EE08C3BF3DF` | `room-host, room-ready` | 2 |
| `btn_match_normal` | `UI/Lobby/btn_match_normal` | `[uc]autochessouter/btn_match_normal.png` | `62B586274488AE3A7BF203829DDFE0C80955993AE22334F46EDE076215C3ADCD` | `room-host, room-ready` | 2 |
| `card_bg` | `UI/Lobby/card_bg` | `[uc]autochessouter/card_bg.png` | `050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2` | `room-full, room-host, room-ready` | 12 |
| `card_deco_bg` | `UI/Lobby/card_deco_bg` | `[uc]autochessouter/card_deco_bg.png` | `C907B3527747B947ECCA08757DD6601BCA46B8EC5AF835F61F226BBBF8E1EBF1` | `room-full, room-host` | 6 |
| `card_deco_self` | `UI/Lobby/card_deco_self` | `[uc]autochessouter/card_deco_self.png` | `A3217A0EE5C8B1D7325758162C9859BEC765C7B63331881C90CDA4804A93F661` | `room-full, room-host, room-ready` | 6 |
| `card_empty` | `UI/Lobby/card_empty` | `[uc]autochessouter/card_empty.png` | `4DD34E0B5BE318770082B14F245591D80F6ABFF00451744C4BEF3459798DCE31` | `room-host` | 3 |
| `create_icon` | `UI/Lobby/create_icon` | `[uc]autochessouter/create_icon.png` | `AE047958EE4E7D43F368D3205110307200D30F79BCF393B36CBBDE37A8A00BA7` | `discovered-prefill, home` | 2 |
| `doc_frame_line` | `UI/Lobby/Home/doc_frame_line` | `[uc]autochessouter/doc_frame_line.png` | `4E4D96093514340112A0799D61611A65DA41153ACBD21F271184E0C0BB311C97` | `discovered-prefill, home` | 14 |
| `host_top_tag` | `UI/Lobby/host_top_tag` | `[uc]autochessouter/host_top_tag.png` | `861754CAFABFEF6641129CAC439501EE3E3D964E0FA3C72BC32FDAC117131009` | `room-full, room-host, room-ready` | 3 |
| `icon_amiy` | `UI/Lobby/Home/icon_amiy` | `Combined/[uc]autochesscommon/icon_amiy.png` | `14D5F8D3A8026751B511942517B9815BA3E04438857FEA649EF8A8A02B64868B` | `discovered-prefill, home` | 2 |
| `img_player_bkg` | `UI/Lobby/img_player_bkg` | `[uc]autochessouter/img_player_bkg.png` | `CB940ECA5FE84527D9AD4546C617120A90F94CD88663F98C66261EE60D45185A` | `discovered-prefill, home` | 4 |
| `img_pointer` | `UI/Lobby/Home/img_pointer` | `[uc]autochessouter/img_pointer.png` | `3CD944DC7F0F3B7DE675E8BBE23EEA9640D95DE65B4B2E91F14648385284F697` | `discovered-prefill, home` | 8 |
| `img_return` | `UI/Lobby/img_return` | `[uc]autochessouter/img_return.png` | `3F20542913541EAF1F175225268FD3E1EC0343C45A18D0BFE3F7DFBDDEFBEC09` | `room-full, room-host, room-ready` | 3 |
| `join_icon` | `UI/Lobby/join_icon` | `[uc]autochessouter/join_icon.png` | `6DE45E7D0AF9FFAE47D271E6A719FE008412FF62704154D277788EA094381C09` | `discovered-prefill, home` | 2 |
| `player_card_ready` | `UI/Lobby/player_card_ready` | `[uc]autochessouter/player_card_ready.png` | `F34786A3E832E97121EB03614B6D584C871B78E4C5CDD1FA6C5F9CB7D191A4C0` | `room-full, room-host, room-ready` | 6 |
| `player_card_self_frame` | `UI/Lobby/player_card_self_frame` | `[uc]autochessouter/player_card_self_frame.png` | `19F0D43B704F9CB92EE3BE11D9C64879D1BA9B542EDFF90E9DBDF7FCE381C3A3` | `room-full, room-host, room-ready` | 6 |
| `player_card_waiting` | `UI/Lobby/player_card_waiting` | `[uc]autochessouter/player_card_waiting.png` | `3B87EBB1DD62B7A8BD4D525F7F2E7E4358F2F1C0B04A65997BA34E79E97F0C8C` | `discovered-prefill` | 1 |
| `room_create_btn_bg` | `UI/Lobby/room_create_btn_bg` | `[uc]autochessouter/room_create_btn_bg.png` | `2782FDCBE671CDD760FF46FD2B3A83CEB3F6396BC44FF48673220C8B21521606` | `discovered-prefill, home` | 2 |
| `room_select_create_btn_bg_down` | `UI/Lobby/Home/room_select_create_btn_bg_down` | `[uc]autochessouter/room_select_create_btn_bg_down.png` | `8709B2C46A88AD6CDA15F0F7E78C02AD78CDB09D3FA2D99F045BC556028CD149` | `discovered-prefill, home` | 2 |
| `room_select_create_left_line` | `UI/Lobby/Home/room_select_create_left_line` | `[uc]autochessouter/room_select_create_left_line.png` | `4893EDC89BF8D9DFE3914673B0446D764D0663327579DAE19744E17C74296CA4` | `discovered-prefill, home` | 4 |
| `room_select_create_middleicon` | `UI/Lobby/Home/room_select_create_middleicon` | `[uc]autochessouter/room_select_create_middleicon.png` | `F728D411AA11A67775AA2A3CBBB1CBED665B914E1BE645DCCDB6BD34BCE288C2` | `discovered-prefill, home` | 2 |
| `room_select_create_text_01` | `UI/Lobby/Home/room_select_create_text_01` | `[uc]autochessouter/room_select_create_text_01.png` | `52DC9A7C8E48DEC53AAF69D91A6FD0E0AA60483C1EE1412E32A007B8DF2E2D72` | `discovered-prefill, home` | 2 |
| `room_select_create_text_02` | `UI/Lobby/Home/room_select_create_text_02` | `[uc]autochessouter/room_select_create_text_02.png` | `9C87A8FE6DFB72362BA8A84B089B66BC80F01822E2E3C0713396499D93F2CE33` | `discovered-prefill, home` | 2 |
| `room_select_dot` | `UI/Lobby/Home/room_select_dot` | `[uc]autochessouter/room_select_dot.png` | `056E14212EA8E02D175D03582C4726FABA09AD518E89DB318CD3EF793E996DB0` | `discovered-prefill, home` | 10 |
| `room_select_img_startroom` | `UI/Lobby/Home/room_select_img_startroom` | `[uc]autochessouter/room_select_img_startroom.png` | `495AA8F2BD5CD97EE12192DACC2CFD8A15731F0E131F9CD74F936B92A49E7E02` | `discovered-prefill, home` | 2 |
| `room_select_join_ban` | `UI/Lobby/Home/room_select_join_ban` | `[uc]autochessouter/room_select_join_ban.png` | `F1ACA192CCCD6399884810A52CDC15E733C415E83579325779E67EB700EB052E` | `discovered-prefill, home` | 8 |
| `room_select_join_blank` | `UI/Lobby/Home/room_select_join_blank` | `[uc]autochessouter/room_select_join_blank.png` | `099A060B78BCA5E39CA82E9747C94BFBC011868DE4AC18CB11C9149BC99AA2CD` | `discovered-prefill, home` | 2 |
| `room_select_join_btn_bg_down` | `UI/Lobby/Home/room_select_join_btn_bg_down` | `[uc]autochessouter/room_select_join_btn_bg_down.png` | `71AE8387746003F1BF0DA72A3E92A7AACDB8A908B63FC6B26C553FC779D77468` | `discovered-prefill, home` | 2 |
| `room_select_join_left_block` | `UI/Lobby/Home/room_select_join_left_block` | `[uc]autochessouter/room_select_join_left_block.png` | `1D10B384025DCF05972D7AEAFDF438FD88DB9B3B9B829DFD541E15103F100D10` | `discovered-prefill, home` | 4 |
| `room_select_join_logo` | `UI/Lobby/Home/room_select_join_logo` | `[uc]autochessouter/room_select_join_logo.png` | `85FB957F0BC4A5172B0F454F77F6195068484B6DEBBD6DFCEE2A2AD1D5D93B59` | `discovered-prefill, home` | 2 |
| `room_select_join_middle_block` | `UI/Lobby/Home/room_select_join_middle_block` | `[uc]autochessouter/room_select_join_middle_block.png` | `997CF5A781848535654D21D9B97D6105DA07F959AECB134DF1FB2F570E9861CA` | `discovered-prefill, home` | 8 |
| `room_select_join_middle_block_mask` | `UI/Lobby/Home/room_select_join_middle_block_mask` | `[uc]autochessouter/room_select_join_middle_block_mask.png` | `95D0FAAF36EEF6681486944D95AB3F453DB0D9B70F23CD2E0DE12F57E2609EC5` | `discovered-prefill, home` | 2 |
| `room_select_join_right_block` | `UI/Lobby/Home/room_select_join_right_block` | `[uc]autochessouter/room_select_join_right_block.png` | `11C872C6DE561E4409085E958D1CDEFA7647E9EEC5883ED91E45162B2B9FE6B8` | `discovered-prefill, home` | 4 |
| `room_select_join_text_01` | `UI/Lobby/Home/room_select_join_text_01` | `[uc]autochessouter/room_select_join_text_01.png` | `F09FD74598C6EEF1066FB53CDA294681FAEA469981B6F215B7E7DD674E63CC39` | `discovered-prefill, home` | 2 |
| `room_select_join_text_02` | `UI/Lobby/Home/room_select_join_text_02` | `[uc]autochessouter/room_select_join_text_02.png` | `F9DCC617D9BB74218E1554939A0897F39516B7965D9400EAD6CCB2DE85E19DD0` | `discovered-prefill, home` | 2 |
| `room_select_join_text_bg` | `UI/Lobby/Home/room_select_join_text_bg` | `[uc]autochessouter/room_select_join_text_bg.png` | `36260875697359E27930467D123D2B684A2DE51D2448A5B295885D06AA518472` | `discovered-prefill, home` | 2 |
| `room_select_join_triangle` | `UI/Lobby/Home/room_select_join_triangle` | `[uc]autochessouter/room_select_join_triangle.png` | `BA585545BC5EF8F6CC126BBFDE59F63A1C75D4B761646A22CCFEA195CB9B7FF7` | `discovered-prefill, home` | 2 |
| `room_select_right_bg` | `UI/Lobby/Home/room_select_right_bg` | `[uc]autochessouter/room_select_right_bg.png` | `F65BE15390749F0FA175B49C310E90E9F3A29C753F2D320068B0831A5EBFDE53` | `discovered-prefill, home` | 2 |
| `room_select_title_icon` | `UI/Lobby/Home/room_select_title_icon` | `[uc]autochessouter/room_select_title_icon.png` | `7C0E9E67D349013FC49DBAF33E1F462C0DF4171B1C6681DB50517629B4BDFF6C` | `discovered-prefill, home` | 2 |
| `team_icon_frame` | `UI/Lobby/team_icon_frame` | `[uc]autochessouter/team_icon_frame.png` | `B05BFEAE52C1E54E9382936F653E291D118A976DA0B1AA6DE14720FC9CCC4380` | `discovered-prefill, home` | 2 |

报告还包含 29 个动态 Unity Text 使用行。所有 46 个 bitmap 行都有非空 Resources 路径、批准来源、完整大写 SHA-256、捕获列表和正 occurrence；没有素材/来源失败。

Task 8 新增的两个直接导入需单独保留尺寸和语义状态：

| Asset | Approved source | SHA-256 | Dimensions | State usage |
| --- | --- | --- | ---: | --- |
| `img_return.png` | `[uc]autochessouter/img_return.png` | `3F20542913541EAF1F175225268FD3E1EC0343C45A18D0BFE3F7DFBDDEFBEC09` | `54x56` | room-only Leave |
| `card_deco_bg.png` | `[uc]autochessouter/card_deco_bg.png` | `C907B3527747B947ECCA08757DD6601BCA46B8EC5AF835F61F226BBBF8E1EBF1` | `322x107` | neutral Waiting/Empty lower decoration |

两者都来自批准的 `G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess` 根目录，均不是 `$0` 或 `#0` 变体。

### Code-native geometry

Cycle 3 报告中的 8 行 code-native geometry 与 bitmap 来源表分开记录：

| Name | Kind | `isBitmap` | Color | Captures | Occurrences |
| --- | --- | --- | --- | --- | ---: |
| `LanLobbyRoot/Home/RoomSelect/Create/InteriorBacking` | `code-native-geometry` | `false` | `#000000C7` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/GuideHorizontal` | `code-native-geometry` | `false` | `#FFA5008C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/GuideVertical` | `code-native-geometry` | `false` | `#FFA5008C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/InteriorBacking` | `code-native-geometry` | `false` | `#000000D1` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/OutlineLeft` | `code-native-geometry` | `false` | `#3030308C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/OutlineRight` | `code-native-geometry` | `false` | `#3030308C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/Home/RoomSelect/Join/OutlineTop` | `code-native-geometry` | `false` | `#3030308C` | `discovered-prefill, home` | 2 |
| `LanLobbyRoot/OpaqueBlocker` | `code-native-geometry` | `false` | `#060F14FF` | `discovered-prefill, home, room-full, room-host, room-ready` | 5 |

这些行的 `isBitmap=false`、无 Sprite、无自定义材质且 `raycastTarget=false`；它们不计入 bitmap 来源合规。

## LAN 房间统一 PortraitFrame——Task 6 最终核查（2026-07-28，当前权威补充）

### 结论

本次最终 focused Unity 测试、三项 evidence smoke、Cycle 3 截图/manifest、素材来源、构建与 Player 日志核查为 **verification PASS**；图 11–13 的统一 PortraitFrame 实际像素验收为 **FAIL**。统一几何是已确认的玩家可见要求，不能把“共享 backing 已实现”写成“参考图已经通过”。

final-review paired-side 纠正后的 canonical Cycle 3 导出报告共有 62 个命名房间门：

- `Passed`: 30
- `Failed`: 30
- `ExcludedByReferencePopup`: 2，且两项均为 `passed=false`
- PortraitFrame 素材/来源失败：0

本节的 62 门结果取代上一节仅包含旧槽位门的 52 门统计，作为当前 PortraitFrame 校准结论。

### 实现与最终 backing

本轮运行时/测试/证据实现涉及：

- `Assets/Game/Runtime/Lobby/LanLobbyRoomLayout.cs`
- `Assets/Game/Runtime/Lobby/LanLobbyView.cs`
- `Assets/Game/Runtime/Initial/LanLobbyCaptureSuite.cs`
- `Assets/Game/Tests/EditMode/Lobby/LanLobbyRoomLayoutEditModeTests.cs`
- `Assets/Game/Tests/PlayMode/Lobby/LanLobbyViewPlayModeTests.cs`
- `Assets/Game/Tests/PlayMode/Lobby/LanLobbyCaptureSuitePlayModeTests.cs`
- `scripts/ExportLanLobbyVisualDiff.ps1`
- `scripts/TestLanLobbyVisualDiffSmoke.ps1`

最终 `CardBody`（概念上的 PortraitFrame）在 `363.75×664.5` 槽位根内的局部 bottom-left 矩形为 `(-96.5,27.5,566,695)`。等价 top-left 参数是 `left=-96.5`、`top=-58`、`width=566`、`height=695`：raw backing 向槽位顶部上方越界 `58 px`，与 `LowerDecoration` 的 raw 几何重叠为 `120-27.5=92.5 px`。

四个槽位及 Empty/Waiting/Ready/host 状态共享这一矩形；现有 View 测试同时证明 Ready 翻转后的归一化世界 footprint 不变。`CardBody` 绘制在状态内容后面，`TopBar` 和 `LowerDecoration` 在其前面。房主槽继续不填入头像、立绘、玩家名、玩家 ID 或资料卡内容。

### Cycle 3 Player 与 detector-only 重导出

Cycle 3 runtime/layout/capture 源状态是提交 `a8314dc`。从该状态生成的 Windows x64 构建日志记录：

```text
result=Succeeded
platform=StandaloneWindows64
totalSize=185656666
totalTime=00:00:03.4280226
errors=0
warnings=0
```

Player 以可见 Direct3D 11、`1920×1080` 运行，日志含
`[LanLobby][capture.completed] count=5`，未匹配到 error、exception、
failed、fatal 或 missing Sprite。五张 canonical PNG 都能解码，且抽样
确认非黑、非单色：

| PNG | 尺寸 | SHA-256 |
| --- | ---: | --- |
| `home.png` | `1920×1080` | `93C8799DD93FD03C9D9FB42DDAE463E642565DA23DC14D37426BF9C8CE6639B7` |
| `discovered-prefill.png` | `1920×1080` | `D6DBECED5CF99CA3F749AF961F9C95D84957D8AA0498E9609498AE91D86A958B` |
| `room-host.png` | `1920×1080` | `9E1FCA1498E5AC54CF1C1A0E4621EA46AEA8A11DE38CFDC20F6DEBA89B8ECEBE` |
| `room-full.png` | `1920×1080` | `337FF209D5958F1934E11CB16FE8B46AD120D63C29A87D65829267487DA6E3F2` |
| `room-ready.png` | `1920×1080` | `AD89BD2CCBCD06FC8F124FAB1B00D558AA35B90E9ED276872D260809D41B6B25` |

Capture manifest 是严格 UTF-8 JSON，恰好含上述五条记录；Capture 与 Evidence manifest 的 SHA-256 同为 `16126A6DDCC8B1E5E1BC90AD8ED2666CD86AF45B02951FA364D5AF920A4CFD15`。

提交 `24f1280` 与本次 final-review paired-side 修复都只纠正
`scripts/ExportLanLobbyVisualDiff.ps1` 与
`scripts/TestLanLobbyVisualDiffSmoke.ps1` 的离线实际像素证据；它们
没有修改 runtime、layout、capture 或 Cycle 3 PNG。本次仍只从既有
Cycle 1/2/3 captures 重导出 canonical Evidence/VisualDiff，没有重新
构建、没有启动 Player、没有创建 Cycle 4。计划中的“detector 后 fresh
Player”条件因三次校准硬停止而未执行，不能记为通过。

最终绝对路径：

- 截图：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\PortraitFrame\Cycle-3\Captures`
- 并排证据：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\PortraitFrame\Cycle-3\Evidence`
- JSON、Markdown 与叠图：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\PortraitFrame\Cycle-3\VisualDiff`
- 构建日志：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\PortraitFrame\Cycle-3\WindowsStandaloneBuild.log`
- Player 日志：`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby\Artifacts\LAN-LOBBY\PortraitFrame\Cycle-3\PlayerCapture.log`

### 最终实际像素结果与所有新增阻塞

共识网格是 `257×513`，eligible contributor 恰好 10 条，投票规则是
`6/10`；确定性 target 含 `1020` 个实际参考轮廓像素。探测器保留每个
可见行的 `(leftX,y,rightX,y)` 实际观测对：左像素直接映射到 canonical
`x=0`，右像素直接映射到 `x=256`，Y 只按自身 `TopBar` 下沿到
`LowerDecoration` 上沿做最近整数归一化。target 的 fail-closed audit
为 left `510`、right `510`、paired rows `510`、interior `0`。TopBar
水平 center/width 仍由独立阻塞 relation 验收；没有填充 bounds、线段、
内部区域、源 aperture、manifest backing 或 ROI。

| Gate | decoded | normalized | intersection | union | Jaccard | TopBar width delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `RoomHost.Slot1.PortraitFrame` | 1034 | 1016 | 1012 | 1024 | 0.988281 | 2 |
| `RoomHost.Slot2.PortraitFrame` | 1036 | 1026 | 1020 | 1026 | 0.994152 | **4** |
| `RoomHost.Slot3.PortraitFrame` | 1016 | 1004 | 1000 | 1024 | 0.976562 | 3 |
| `RoomHost.Slot4.PortraitFrame` | 1034 | 1022 | 1016 | 1026 | 0.990253 | 1 |
| `RoomReady.Slot1.PortraitFrame` | 1034 | 1016 | 1012 | 1024 | 0.988281 | 2 |
| `RoomReady.Slot2.PortraitFrame` | 1034 | 1016 | 1012 | 1024 | 0.988281 | 2 |
| `RoomReady.Slot3.PortraitFrame` | 1034 | 1016 | 1012 | 1024 | 0.988281 | 2 |
| `RoomFull.Slot1.PortraitFrame` | 1034 | 1016 | 1012 | 1024 | 0.988281 | 2 |
| `RoomFull.Slot2.PortraitFrame` | 1036 | 1026 | 1020 | 1026 | 0.994152 | **4** |
| `RoomFull.Slot3.PortraitFrame` | 1018 | 1006 | 1002 | 1024 | 0.978516 | 3 |

结果为 `8/10`：normalized set `1004..1026`、intersection
`1000..1020`、union `1024..1026`、Jaccard `0.976562..0.994152`，
十条实际像素门都达到未修改的 `0.95`。所有十条的 shared geometry 与
material evidence 通过，raw overlap 都是 `92.5 px`、连续背景缝隙都是
`0 px`。`RoomHost.Slot2.PortraitFrame` 和
`RoomFull.Slot2.PortraitFrame` 仍因独立的自身 TopBar 可见宽度差
`4 px > 3 px` 失败，因此 PortraitFrame 与整体视觉验收都仍为
**FAILED**。

另有两个 Cycle 2 到 Cycle 3 的命名回归：

- `RoomReady.Slot2.ReadyTopBar`：actual `(615,170,323,46)`，reference `(616,170,319,46)`，宽度差 `4 px > 3 px`；
- `RoomHost.Slot2To3.VisibleContourSpacing`：actual 间距 `383.5 px`，reference `388 px`，差 `-4.5 px`，绝对值超过 `4 px`。

两个 popup 排除项仍为
`RoomReady.Slot4.ReferencePopupExclusion` 和
`RoomFull.Slot4.ReferencePopupExclusion`；二者都是
`ExcludedByReferencePopup` 且 `passed=false`。图 11 第四槽仍是唯一
无遮挡的第四槽参考。

### 素材使用与完整性

PortraitFrame 唯一 bitmap 素材：

| Sprite | Resources | 批准来源 | SHA-256 | room-host | room-ready | room-full | 总计 |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: |
| `card_bg` | `UI/Lobby/card_bg` | `[uc]autochessouter/card_bg.png` | `050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2` | 4 | 4 | 4 | 12 |

批准源文件位于
`G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess\[uc]autochessouter\card_bg.png`；
只读复核的文件长度为 `14479` 字节，SHA 与上表一致。素材根目录只读
审计为 `2310` 个文件、`40486009` 字节、0 个 reparse point。

本次 paired-side 重导出只读取
`G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud`。前后审计均为
14 个文件、0 个 reparse point；必需图 9/11/12/13 均存在且 SHA-256
分别保持 `B5EFA6DB86285C9306F27C963F1DD5CFF82135B34FD80A3937682298EA3E596E`、
`CFC2822EB87DA9D38F034DCBA5746FC9009494064A18CDDA99E3215919D7A971`、
`28103BE8A167769E01C53CB878E46BABC79B570579A0375F31D805DCFAF42219`、
`DA782E04618C673D170F15BD8D1B47E939B72F1F355EB91BC4778A367303EF00`。

从本计划基线 `7b5d092` 到 detector 提交 `24f1280`，没有新增或修改
bitmap、参考图、Package、Unity 版本、场景、Prefab、ScriptableObject
或 `.meta`；也没有在外部素材目录写入、移动或删除文件。

### 最终自动验证

以下 Unity 测试是 Task 6 已保留并经最终审查核对的证据，不是本次
paired-side 修复波的新重跑。本波没有启动 Unity、构建或 Player。保留
证据使用 `D:\2022.3.62f1c1\Editor\Unity.exe`、工作区
`G:\ARKnoNIGHTS_beta\.worktrees\lan-lobby`、`900` 秒超时串行执行：

| 过滤器 | 结果 | fail/skip/inconclusive/notRun | shutdown | 目录 |
| --- | ---: | ---: | --- | --- |
| `LanLobbyRoomLayoutEditModeTests` | 12/12 | 0/0/0/0 | `forced-stop-after-results` | `Artifacts/LAN-LOBBY/PortraitFrame/Final/Layout` |
| `LobbyAssetMapEditModeTests` | 30/30 | 0/0/0/0 | `forced-stop-after-results` | `Artifacts/LAN-LOBBY/PortraitFrame/Final/Assets` |
| `LanLobbyViewPlayModeTests` | 31/31 | 0/0/0/0 | `normal-exit-after-results` | `Artifacts/LAN-LOBBY/PortraitFrame/Final/View` |
| `LanLobbyCaptureSuitePlayModeTests` | 5/5 | 0/0/0/0 | `normal-exit-after-results` | `Artifacts/LAN-LOBBY/PortraitFrame/Final/Capture` |
| `LanLobbyControllerPlayModeTests` | 4/4 | 0/0/0/0 | `normal-exit-after-results` | `Artifacts/LAN-LOBBY/PortraitFrame/Final/Controller` |

总计 `82/82`；每个目录都有非零可读 XML、日志和 summary。所有 Final
Unity 日志均未匹配到 C# compiler error、unhandled exception、
NullReferenceException、missing Sprite 或 fatal。前两个 forced-stop 是
完整结果写出后的 runner 清理，不是 timeout。

三项 smoke：

- VisualDiff：`48 fixtures / 2043 assertions`，PASS（最终 wall-clock `303.02 s`；未预先采集子进程 CPU，故不猜测 CPU 时间）；
- Evidence exporter：`5 / 15`，PASS；
- Evidence common：`13 / 53`，PASS。

自动测试/证据工具本身通过并不覆盖上面的视觉失败。

### 未验证与已知阻断

- PortraitFrame 参考图验收未通过：actual-pixel Jaccard 已为 10/10 达标，但两条 Slot 2 宽度门仍失败，所以完整 PortraitFrame 仅 `8/10`；两条普通 gate 回归也仍保留。
- 三次可见 Player 配额已经耗尽；未运行 detector 后的新 Player，也未创建 Cycle 4。
- stale-after-start snapshot 仍可能覆盖 `HasStarted`。
- accept/stop 生命周期竞态仍未修复。
- Windows 与 Android 两台物理设备同一 Wi-Fi 下的发现、房间号预填、加入、准备切换、开始、离开和房主解散流程仍未人工验证。

> **2026-07-29 UI 合同变更提示：** 本报告中关于 `RoomSelect/RightBackground`、IdentityPanel 名字输入/Save、旧 Join `InteriorBacking` 矩形、`RoomCard/ReadyOverlay` 以及房主资料隐藏的截图与清单均为历史证据，已被 `docs/SPEC.md` 第 17 节取代。当前实现的 focused Unity 回归记录在 `docs/TEST_PLAN.md` 第 35 节；本轮没有生成新的可见 Player 截图，也不得把本报告的旧像素结果记为新布局通过。
