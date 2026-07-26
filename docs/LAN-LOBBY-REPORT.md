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

## Home room-select evidence-derived capture correction (2026-07-26)

This is the current authoritative retained evidence. Current proof is limited to the focused XML under `Artifacts/LAN-LOBBY/HomeRoomSelectActionBars/Verification-EvidenceFix-20260726-165954/` and the build, Player capture, manifest, and visual report under `Artifacts/LAN-LOBBY/HomeRoomSelectActionBarsEvidenceFix/`. Action crops, Unity Text rows, and Sprite occurrence totals come from the production Player capture manifest rather than exporter constants or a manually curated list.

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
  - 128 active rendered Sprite instances aggregate to 34 audited bitmap rows with Resources path, approved source, SHA-256, captures, and occurrence count. `join_icon` correctly totals `4`: two nodes in `home` and two in `discovered-prefill`.
  - 59 active rendered `UnityEngine.UI.Text` instances aggregate by node/text/font to 37 rows. They cover Home identity/title/input/buttons/discovery/status and the Room page; each row reports runtime `fontName`, an empty unprovable `fontResourcePath`, `hasBitmapSource=false`, and an empty `bitmapSourcePath`. Dormant/non-rendered Text is explicitly excluded.
  - Code-generated non-bitmap geometry remains six grouped rows.
- The manifest contains no forbidden `$0`/`#0` Unpacked source. Manual inspection opened `CapturesFinal/home.png`, `CapturesFinal/discovered-prefill.png`, `VisualDiff/home-create-action-overlay.png`, and `VisualDiff/home-join-action-overlay.png`; the bars are unobstructed, and the local overlays retain both captured/reference contours. Metrics remain non-blocking, and other decoration placement remains outside this iteration's acceptance.

## Home action icon/text visual-center calibration (current authoritative, 2026-07-26)

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
  - `join_icon`: Resources `UI/Lobby/join_icon`; source `[uc]autochessouter/join_icon.png`; SHA-256 `6DE45E7D0AF9FFAE47D271E6A719FE008412FF62704154D277788EA094381C09`; four occurrences because each Home state also renders the Simulation Invite icon.
  - `room_select_create_btn_bg_down`: source `[uc]autochessouter/room_select_create_btn_bg_down.png`; SHA-256 `8709B2C46A88AD6CDA15F0F7E78C02AD78CDB09D3FA2D99F045BC556028CD149`.
  - `room_select_join_btn_bg_down`: source `[uc]autochessouter/room_select_join_btn_bg_down.png`; SHA-256 `71AE8387746003F1BF0DA72A3E92A7AACDB8A908B63FC6B26C553FC779D77468`.
  - “创建同盟” and “加入同盟” are Unity Text, use runtime font `Novecento wide Normal Regular.woff2`, and report `hasBitmapSource=false`; they are not represented as material-library bitmaps.
  - The complete report contains `34` bitmap rows from `128` rendered Sprite instances, `37` Unity Text rows from `59` instances, and `6` code-generated geometry rows. No source contains forbidden Unpacked `$0` or `#0`.
- Manual inspection opened `Calibration2/CapturesFinal/home.png`, `discovered-prefill.png`, `VisualDiff/home-create-action-overlay.png`, and `home-join-action-overlay.png`. The corrected icons and labels are not enlarged, both bar contours remain unobstructed, and the discovered room/prefill UI stays above the Join action rather than covering it. The other Create/Join decoration still requires later visual calibration.
- `scripts/TestLanLobbyVisualDiffSmoke.ps1`, `scripts/TestExportLanLobbyEvidenceSmoke.ps1`, and `scripts/TestLanLobbyEvidenceCommonSmoke.ps1` all print `PASS` and exit `0`.
- Repository-wide suites were rerun against the final tree and remain non-green in unchanged, out-of-scope areas. `Artifacts/LAN-LOBBY/ActionContentVisualCenters/FullSuiteFinal-20260726-184300/EditMode/EditModeResults.xml` reports `118` total and `2` failures: `BattleCoreEditModeTests.Fixture_InvalidInputMatrixReturnsStructuredErrors` plus `BattleCoreEditModeTests.RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources` (the test expects empty `displayNameZhHans`, while unchanged `gopro.json` contains `狂暴的猎狗pro`). The corresponding PlayMode XML reports `39` total and `1` failure: `PreparationBattleLoopPlayModeTests.SampleScene_AutoLoopsPreparationToBattleAndBackWithoutWritingCombatResultToPlayerState` expected `Preparation` and observed `Battle`. These existing failures block a whole-repository green claim and automatic branch integration; they do not occur in files modified by this UI calibration.

## Automated result (historical context)

All `Temp/UnityTests/...` XML paths in the following four bullets were parsed at run time, have since been cleaned, and are non-persistent historical output. They cannot be cited as current proof; only `Artifacts/LAN-LOBBY/HomeRoomSelectActionBars/Verification-EvidenceFix-20260726-165954/` and `Artifacts/LAN-LOBBY/HomeRoomSelectActionBarsEvidenceFix/` are current authoritative evidence.

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
