[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$exportScript = Join-Path $PSScriptRoot 'ExportLanLobbyVisualDiff.ps1'
$scratch = Join-Path $projectRoot ('Temp/LAN-LOBBY-VisualDiffSmoke-' + [Guid]::NewGuid().ToString('N'))

function Assert-True([bool] $Condition, [string] $Message)
{
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Assert-FailsWithoutOutput([scriptblock] $Action, [string] $OutputPath, [string] $ExpectedMessage)
{
    $failed = $false
    try { & $Action }
    catch
    {
        $failed = $true
        if ($_.Exception.Message -notlike ('*' + $ExpectedMessage + '*'))
        {
            throw "Expected failure containing '$ExpectedMessage', actual: $($_.Exception.Message)"
        }
    }
    if (-not $failed) { throw "Expected visual-diff failure: $ExpectedMessage" }
    Assert-True (-not (Test-Path -LiteralPath $OutputPath)) "output must not exist after validation failure: $OutputPath"
}

function Assert-FailsPreservingOutput([scriptblock] $Action, [string] $OutputPath, [string] $SentinelPath, [string] $ExpectedMessage)
{
    $failed = $false
    try { & $Action }
    catch
    {
        $failed = $true
        if ($_.Exception.Message -notlike ('*' + $ExpectedMessage + '*'))
        {
            throw "Expected failure containing '$ExpectedMessage', actual: $($_.Exception.Message)"
        }
    }
    if (-not $failed) { throw "Expected visual-diff failure: $ExpectedMessage" }
    Assert-True (Test-Path -LiteralPath $OutputPath) 'caller pre-existing output directory must remain'
    Assert-True ((Get-Content -Raw -LiteralPath $SentinelPath) -eq 'preserve me') 'caller sentinel output must remain unchanged'
}

function New-SolidPng([string] $Path, [int] $Width, [int] $Height, [Drawing.Color] $Color, [scriptblock] $Draw)
{
    $bitmap = New-Object Drawing.Bitmap $Width, $Height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try
    {
        $graphics.Clear($Color)
        if ($Draw) { & $Draw $graphics }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

try
{
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $captureDirectory = Join-Path $scratch 'captures'
    $referenceDirectory = Join-Path $scratch 'references'
    New-Item -ItemType Directory -Force -Path $captureDirectory, $referenceDirectory | Out-Null

    $figure9 = ([char]0x56FE).ToString() + '9.png'
    $figure10 = ([char]0x56FE).ToString() + '10.png'
    New-SolidPng (Join-Path $referenceDirectory $figure9) 2048 1118 ([Drawing.Color]::FromArgb(255, 40, 50, 60)) {
        param($graphics)
        $contentBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Black)
        try
        {
            # Native fixture coordinates that become the approved visible bounds after each 745x104 action crop is resized to 717x99.
            $graphics.FillRectangle($contentBrush, 1224 + 49, 468 + 26, 37, 39)
            $graphics.FillRectangle($contentBrush, 1224 + 113, 468 + 29, 154, 34)
            $graphics.FillRectangle($contentBrush, 1224 + 49, 906 + 21, 46, 53)
            $graphics.FillRectangle($contentBrush, 1224 + 108, 906 + 33, 156, 36)
        }
        finally { $contentBrush.Dispose() }
    }
    New-SolidPng (Join-Path $referenceDirectory $figure10) 2048 1118 ([Drawing.Color]::FromArgb(255, 65, 75, 85)) $null

    $names = @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full')
    $records = @()
    foreach ($name in $names)
    {
        $actualPath = Join-Path $captureDirectory ($name + '.png')
        $draw = $null
        if ($name -eq 'home')
        {
            $draw = {
                param($graphics)
                $maskedBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
                $differenceBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Red)
                $contentBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Black)
                try
                {
                    # Masked radar: x=0.00,y=0.18,w=0.57,h=0.66.
                    $graphics.FillRectangle($maskedBrush, 100, 300, 120, 80)
                    # create-room: x=0.60,y=0.42,w=0.36,h=0.11.
                    $graphics.FillRectangle($differenceBrush, 1200, 480, 100, 60)
                    # Create actual crop is (1161,449); its icon is deliberately 2 px right.
                    $graphics.FillRectangle($contentBrush, 1161 + 49, 449 + 25, 36, 37)
                    $graphics.FillRectangle($contentBrush, 1161 + 109, 449 + 28, 148, 32)
                    # Join actual crop is (1154,876) and matches all approved visible bounds.
                    $graphics.FillRectangle($contentBrush, 1154 + 47, 876 + 20, 44, 50)
                    $graphics.FillRectangle($contentBrush, 1154 + 104, 876 + 31, 150, 34)
                }
                finally { $maskedBrush.Dispose(); $differenceBrush.Dispose(); $contentBrush.Dispose() }
            }
        }
        New-SolidPng $actualPath 1920 1080 ([Drawing.Color]::FromArgb(255, 40, 50, 60)) $draw
        $actionRects = if ($name -eq 'home') {
            @(
                # Deliberately offset and resize Create so the report must derive non-zero deltas.
                [ordered]@{ name = 'LanLobbyRoot/Home/RoomSelect/Create/CreateAction'; coordinateOrigin = 'screen-bottom-left'; unit = 'px'; x = 1161; y = 536; width = 711; height = 95 },
                [ordered]@{ name = 'LanLobbyRoot/Home/RoomSelect/Join/JoinAction'; coordinateOrigin = 'screen-bottom-left'; unit = 'px'; x = 1154; y = 105; width = 717; height = 99 }
            )
        } else { @() }
        $records += [ordered]@{
            name = $name
            path = $actualPath
            width = 1920
            height = 1080
            spriteSources = @(
                [ordered]@{ node = 'LanLobbyRoot/Terrain'; spriteName = 'bg_terrain'; sourcePath = '[uc]autochessouter/bg_terrain.png' }
            ) + $(if ($name -eq 'home') {
                @(
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/SimulationInvite/Icon'; spriteName = 'join_icon'; sourcePath = '[uc]autochessouter/join_icon.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Join/JoinAction/ActionIcon'; spriteName = 'join_icon'; sourcePath = '[uc]autochessouter/join_icon.png' }
                )
            } else { @() })
            rects = @(
                [ordered]@{ name = 'LanLobbyRoot/Room/RoomCard_0'; x = 100; y = 100; width = 200; height = 300 },
                [ordered]@{ name = 'LanLobbyRoot/Room/RoomCard_1'; x = 320; y = 100; width = 200; height = 300 },
                [ordered]@{ name = 'LanLobbyRoot/Room/RoomCard_2'; x = 540; y = 100; width = 200; height = 300 },
                [ordered]@{ name = 'LanLobbyRoot/Room/RoomCard_3'; x = 760; y = 100; width = 200; height = 300 }
            ) + $actionRects
            unityText = $(if ($name -eq 'home') {
                @(
                    [ordered]@{ node = 'LanLobbyRoot/Home/IdentityPanel/Title'; text = 'LOCAL IDENTITY'; fontName = 'Novecento wide Normal Regular'; fontResourcePath = ''; hasBitmapSource = $false; bitmapSourcePath = '' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/CreateAction/Label'; text = '创建同盟'; fontName = 'Novecento wide Normal Regular'; fontResourcePath = ''; hasBitmapSource = $false; bitmapSourcePath = '' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Join/JoinAction/Label'; text = '加入同盟'; fontName = 'Novecento wide Normal Regular'; fontResourcePath = ''; hasBitmapSource = $false; bitmapSourcePath = '' }
                )
            } else {
                @([ordered]@{ node = 'LanLobbyRoot/Room/Latency'; text = '18 ms'; fontName = 'Novecento wide Normal Regular'; fontResourcePath = ''; hasBitmapSource = $false; bitmapSourcePath = '' })
            })
            codeNativeGeometry = @(
                [ordered]@{ name = 'LanLobbyRoot/OpaqueBlocker'; kind = 'code-native-geometry'; isBitmap = $false; color = '#060F14FF'; x = 0; y = 0; width = 1920; height = 1080 }
            )
        }
    }
    $manifestJson = [ordered]@{ captures = $records } | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText(
        (Join-Path $captureDirectory 'manifest.json'),
        $manifestJson,
        (New-Object Text.UTF8Encoding($false)))

    $createActionRectName = 'LanLobbyRoot/Home/RoomSelect/Create/CreateAction'
    $invalidActionCases = @(
        [pscustomobject]@{
            name = 'missing-create'
            expectedMessage = 'Create action Rect must occur exactly once'
            mutate = {
                param($homeCaptureRecord)
                $homeCaptureRecord.rects = @($homeCaptureRecord.rects | Where-Object name -ne $createActionRectName)
            }
        },
        [pscustomobject]@{
            name = 'duplicate-create'
            expectedMessage = 'Create action Rect must occur exactly once'
            mutate = {
                param($homeCaptureRecord)
                $create = @($homeCaptureRecord.rects | Where-Object name -eq $createActionRectName)[0]
                $duplicate = $create | ConvertTo-Json -Depth 4 | ConvertFrom-Json
                $homeCaptureRecord.rects = @($homeCaptureRecord.rects) + $duplicate
            }
        },
        [pscustomobject]@{
            name = 'non-numeric-create-x'
            expectedMessage = 'Cannot convert value'
            mutate = {
                param($homeCaptureRecord)
                (@($homeCaptureRecord.rects | Where-Object name -eq $createActionRectName)[0]).x = 'not-a-number'
            }
        },
        [pscustomobject]@{
            name = 'wrong-create-origin'
            expectedMessage = 'coordinateOrigin=screen-bottom-left and unit=px'
            mutate = {
                param($homeCaptureRecord)
                (@($homeCaptureRecord.rects | Where-Object name -eq $createActionRectName)[0]).coordinateOrigin = 'screen-top-left'
            }
        },
        [pscustomobject]@{
            name = 'wrong-create-unit'
            expectedMessage = 'coordinateOrigin=screen-bottom-left and unit=px'
            mutate = {
                param($homeCaptureRecord)
                (@($homeCaptureRecord.rects | Where-Object name -eq $createActionRectName)[0]).unit = 'normalized'
            }
        },
        [pscustomobject]@{
            name = 'zero-create-width'
            expectedMessage = 'invalid or outside'
            mutate = {
                param($homeCaptureRecord)
                (@($homeCaptureRecord.rects | Where-Object name -eq $createActionRectName)[0]).width = 0
            }
        },
        [pscustomobject]@{
            name = 'zero-create-height'
            expectedMessage = 'invalid or outside'
            mutate = {
                param($homeCaptureRecord)
                (@($homeCaptureRecord.rects | Where-Object name -eq $createActionRectName)[0]).height = 0
            }
        },
        [pscustomobject]@{
            name = 'out-of-bounds-create'
            expectedMessage = 'invalid or outside'
            mutate = {
                param($homeCaptureRecord)
                (@($homeCaptureRecord.rects | Where-Object name -eq $createActionRectName)[0]).x = 1800
            }
        }
    )
    foreach ($invalidActionCase in $invalidActionCases)
    {
        $caseCaptureDirectory = Join-Path $scratch ($invalidActionCase.name + '-captures')
        Copy-Item -LiteralPath $captureDirectory -Destination $caseCaptureDirectory -Recurse
        $caseManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $caseCaptureDirectory 'manifest.json') | ConvertFrom-Json
        foreach ($record in $caseManifest.captures) { $record.path = Join-Path $caseCaptureDirectory ($record.name + '.png') }
        $caseHome = @($caseManifest.captures | Where-Object name -eq 'home')[0]
        $mutate = $invalidActionCase.mutate
        & $mutate $caseHome
        $caseManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $caseCaptureDirectory 'manifest.json') -Encoding UTF8

        $caseOutput = Join-Path $scratch ($invalidActionCase.name + '-output')
        Assert-FailsWithoutOutput {
            & $exportScript -CaptureDirectory $caseCaptureDirectory -OutputDirectory $caseOutput -ReferenceDirectory $referenceDirectory
        } $caseOutput $invalidActionCase.expectedMessage
    }

    $invalidCaptureDirectory = Join-Path $scratch 'invalid-captures'
    New-Item -ItemType Directory -Force -Path $invalidCaptureDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $captureDirectory 'manifest.json') -Destination (Join-Path $invalidCaptureDirectory 'manifest.json')
    foreach ($name in $names) { New-SolidPng (Join-Path $invalidCaptureDirectory ($name + '.png')) 1280 720 ([Drawing.Color]::Black) $null }
    $invalidManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $invalidCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $invalidManifest.captures) { $record.path = Join-Path $invalidCaptureDirectory ($record.name + '.png') }
    $invalidManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $invalidCaptureDirectory 'manifest.json') -Encoding UTF8
    $invalidOutput = Join-Path $scratch 'invalid-output'
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $invalidCaptureDirectory -OutputDirectory $invalidOutput -ReferenceDirectory $referenceDirectory
    } $invalidOutput '1920x1080'

    $malformedReferences = Join-Path $scratch 'malformed-references'
    New-Item -ItemType Directory -Force -Path $malformedReferences | Out-Null
    Set-Content -LiteralPath (Join-Path $malformedReferences $figure9) -Value 'not an image' -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $referenceDirectory $figure10) -Destination (Join-Path $malformedReferences $figure10)
    $preexistingOutput = Join-Path $scratch 'preexisting-output'
    New-Item -ItemType Directory -Force -Path $preexistingOutput | Out-Null
    $sentinel = Join-Path $preexistingOutput 'sentinel.txt'
    Set-Content -LiteralPath $sentinel -Value 'preserve me' -NoNewline -Encoding UTF8
    Assert-FailsPreservingOutput {
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $preexistingOutput -ReferenceDirectory $malformedReferences
    } $preexistingOutput $sentinel 'reference image'

    # The isolated worktree deliberately lacks the user-owned figures 9/10.  The default
    # may therefore only be used when those exact files exist; callers must be able to
    # supply a separate read-only directory without the exporter changing it.
    $defaultReferenceOutput = Join-Path $scratch 'default-reference-output'
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $defaultReferenceOutput
    } $defaultReferenceOutput 'Expected exactly one reference 9'

    $referenceFixtureHashes = @(
        Get-ChildItem -LiteralPath $referenceDirectory -File | Sort-Object Name | ForEach-Object {
            "$($_.Name):$((Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash)"
        }
    )
    $output = Join-Path $scratch 'output'
    & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $output -ReferenceDirectory $referenceDirectory | Out-Null
    $referenceFixtureHashesAfterExport = @(
        Get-ChildItem -LiteralPath $referenceDirectory -File | Sort-Object Name | ForEach-Object {
            "$($_.Name):$((Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash)"
        }
    )
    Assert-True (($referenceFixtureHashes -join "`n") -eq ($referenceFixtureHashesAfterExport -join "`n")) 'explicit external references must remain read-only exporter inputs'
    $report = Get-Content -Raw -LiteralPath (Join-Path $output 'visual-diff-report.json') | ConvertFrom-Json
    $homeCapture = $report.captures | Where-Object name -eq 'home'
    $actionBars = @($report.actionBars)
    Assert-True ($actionBars.Count -eq 2) 'two Home action bars must be reported separately'
    $createAction = $actionBars | Where-Object name -eq 'home-create-action'
    $joinAction = $actionBars | Where-Object name -eq 'home-join-action'
    Assert-True (($createAction.actualRect.x -eq 1161) -and ($createAction.actualRect.y -eq 449) -and ($createAction.actualRect.width -eq 711) -and ($createAction.actualRect.height -eq 95)) 'Create actual crop must be converted from the captured manifest Rect'
    Assert-True (($joinAction.actualRect.x -eq 1154) -and ($joinAction.actualRect.y -eq 876) -and ($joinAction.actualRect.width -eq 717) -and ($joinAction.actualRect.height -eq 99)) 'Join actual crop must use the approved Rect'
    Assert-True (($createAction.actualRect.coordinateOrigin -eq 'screen-top-left') -and ($createAction.actualRect.unit -eq 'px')) 'actual action Rect must name its normalized origin and unit'
    Assert-True (($createAction.approvedTargetRectPx1920x1080.coordinateOrigin -eq 'screen-top-left') -and ($createAction.approvedTargetRectPx1920x1080.unit -eq 'px')) 'approved target Rect must name its screen coordinate origin and unit'
    Assert-True (($createAction.positionDeviationPx1920x1080.deltaX -eq 7) -and ($createAction.positionDeviationPx1920x1080.deltaY -eq -4)) 'Create position delta must be actual top-left minus approved target'
    Assert-True (($createAction.locallyResizedReferenceSizePx.width -eq 717) -and ($createAction.locallyResizedReferenceSizePx.height -eq 99)) 'local reference target must remain the approved 717x99 size'
    Assert-True (($createAction.comparisonReferenceSizePx.width -eq 711) -and ($createAction.comparisonReferenceSizePx.height -eq 95)) 'metric comparison reference must explicitly report its actual-crop size'
    Assert-True (($createAction.sizeDeviationPxAfterLocalReferenceResize.deltaWidth -eq -6) -and ($createAction.sizeDeviationPxAfterLocalReferenceResize.deltaHeight -eq -4)) 'Create size delta must be actual minus locally resized target reference'
    Assert-True (($joinAction.positionDeviationPx1920x1080.deltaX -eq 0) -and ($joinAction.positionDeviationPx1920x1080.deltaY -eq 0)) 'Join position delta must remain zero'
    Assert-True (($joinAction.locallyResizedReferenceSizePx.width -eq 717) -and ($joinAction.locallyResizedReferenceSizePx.height -eq 99)) 'Join local reference target must be 717x99'
    Assert-True (($joinAction.comparisonReferenceSizePx.width -eq 717) -and ($joinAction.comparisonReferenceSizePx.height -eq 99)) 'Join metric comparison size must be explicit'
    Assert-True (($joinAction.sizeDeviationPxAfterLocalReferenceResize.deltaWidth -eq 0) -and ($joinAction.sizeDeviationPxAfterLocalReferenceResize.deltaHeight -eq 0)) 'Join size delta must remain zero'
    $expectedContent = @(
        @{ bar='home-create-action'; name='icon';  x=47;  y=25; width=36;  height=37; actualX=49; passed=$false },
        @{ bar='home-create-action'; name='label'; x=109; y=28; width=148; height=32; actualX=109; passed=$true },
        @{ bar='home-join-action';   name='icon';  x=47;  y=20; width=44;  height=50; actualX=47; passed=$true },
        @{ bar='home-join-action';   name='label'; x=104; y=31; width=150; height=34; actualX=104; passed=$true }
    )
    foreach ($expected in $expectedContent)
    {
        $bar = $actionBars | Where-Object name -eq $expected.bar
        $content = @($bar.contentVisuals | Where-Object name -eq $expected.name)
        Assert-True ($content.Count -eq 1) "$($expected.bar)/$($expected.name) visible bounds must occur once"
        $content = $content[0]
        Assert-True (($content.expectedBounds.x -eq $expected.x) -and ($content.expectedBounds.y -eq $expected.y) -and ($content.expectedBounds.width -eq $expected.width) -and ($content.expectedBounds.height -eq $expected.height)) "$($expected.bar)/$($expected.name) expected bounds"
        Assert-True (($content.actualBounds.x -eq $expected.actualX) -and ($content.actualBounds.y -eq $expected.y) -and ($content.actualBounds.width -eq $expected.width) -and ($content.actualBounds.height -eq $expected.height)) "$($expected.bar)/$($expected.name) actual bounds"
        Assert-True ($content.passed -eq $expected.passed) "$($expected.bar)/$($expected.name) pass state"
    }
    $createIconVisual = @($createAction.contentVisuals | Where-Object name -eq 'icon')[0]
    Assert-True (($createIconVisual.centerDeviationPx.deltaX -eq 2) -and ($createIconVisual.centerDeviationPx.deltaY -eq 0)) 'Create icon fixture must prove a +2 px center failure'
    Assert-True (@($report.materialUsage.bitmapSprites).Count -gt 0) 'material usage must separately list bitmap Sprites'
    Assert-True (($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'join_icon').occurrenceCount -eq 2) 'Sprite occurrence count must sum rendered instances, not capture presence'
    Assert-True (@($report.materialUsage.unityText | Where-Object { $_.node -eq 'LanLobbyRoot/Home/RoomSelect/Create/CreateAction/Label' -and $_.text -eq '创建同盟' }).Count -eq 1) 'Create action Unity Text must come from captured manifest data'
    Assert-True (@($report.materialUsage.unityText | Where-Object { $_.node -eq 'LanLobbyRoot/Home/IdentityPanel/Title' -and $_.text -eq 'LOCAL IDENTITY' }).Count -eq 1) 'Home identity text must come from captured manifest data'
    Assert-True (@($report.materialUsage.unityText | Where-Object { $_.node -eq 'LanLobbyRoot/Room/Latency' -and $_.text -eq '18 ms' }).Count -eq 1) 'Room text must come from captured manifest data'
    Assert-True (@($report.materialUsage.codeGeneratedGeometry).Count -gt 0) 'material usage must separately list code-generated geometry'
    Assert-True (@($report.materialUsage.unityText | Where-Object { $_.PSObject.Properties.Name -contains 'sourcePath' }).Count -eq 0) 'Unity Text must not be represented as a Sprite source'
    foreach ($name in @('home-create-action','home-join-action')) { foreach ($kind in @('actual','reference','overlay','heatmap')) { Assert-True (Test-Path -LiteralPath (Join-Path $output ($name + '-' + $kind + '.png'))) "missing $name $kind" } }
    Assert-True (($report.captures | Measure-Object).Count -eq 5) 'five captures must be reported'
    Assert-True (($report.referenceNormalization -eq 'independent-xy') -and ($homeCapture.actualWidth -eq 1920) -and ($homeCapture.actualHeight -eq 1080) -and ($homeCapture.referenceWidth -eq 2048) -and ($homeCapture.referenceHeight -eq 1118)) 'report must retain native dimensions and independent normalization'
    Assert-True ($homeCapture.maskedPixels -gt 0) 'home must record masked pixels'
    Assert-True (($homeCapture.regions | Where-Object name -eq 'ignored-radar' | Select-Object -ExpandProperty comparedPixels) -eq 0) 'masked radar pixels must not be compared'
    Assert-True (($homeCapture.regions | Where-Object name -eq 'create-room' | Select-Object -ExpandProperty pixelDifferenceRatio) -gt 0) 'create-room difference must be measurable'
    Assert-True (Test-Path (Join-Path $output 'home-heatmap.png')) 'home heatmap must exist'
    Assert-True ((Test-Path (Join-Path $output 'visual-diff-report.md')) -and (Test-Path (Join-Path $output 'manifest.json'))) 'report and copied manifest must exist'
    $markdown = Get-Content -Raw -LiteralPath (Join-Path $output 'visual-diff-report.md')
    Assert-True ($markdown.Contains("${figure9}: 2048×1118")) 'Markdown must derive figure 9 native dimensions from decoded reference pixels'
    Assert-True ($markdown.Contains("${figure10}: 2048×1118")) 'Markdown must derive figure 10 native dimensions from decoded reference pixels'
    Assert-True ($markdown.Contains('Position deviation (px)')) 'Markdown action table must expose position deviation in px'
    Assert-True ($markdown.Contains('1161,449,711,95')) 'Markdown must contain the manifest-derived Create actual Rect'
    Assert-True ($markdown.Contains('dx=7, dy=-4')) 'Markdown must contain the exact manifest-derived Create position delta'
    Assert-True ($markdown.Contains('717x99')) 'Markdown must explicitly list the locally resized reference size'
    Assert-True ($markdown.Contains('711x95')) 'Markdown must explicitly list the comparison reference size'
    Assert-True ($markdown.Contains('dw=-6, dh=-4')) 'Markdown must contain the exact Create size delta'
    Assert-True ($markdown.Contains('## Home action content visible bounds')) 'Markdown must expose action-content visible bounds'
    foreach ($expected in $expectedContent) { Assert-True ($markdown.Contains("$($expected.bar)/$($expected.name)")) "Markdown missing $($expected.bar)/$($expected.name)" }
    Assert-True ($markdown.Contains('## Unity Text usage')) 'Markdown must separate Unity Text usage from bitmap Sprites'
    Assert-True ($markdown.Contains('## Code-generated geometry usage')) 'Markdown must separate code-generated geometry from bitmap Sprites'
    Assert-True ((@($report.captures | Where-Object { $_.name -like 'room-*' } | ForEach-Object { @($_.roomCards).Count } | Measure-Object -Sum).Sum -eq 12)) 'room reports must retain four actual RoomCard rectangles each'
    foreach ($captureName in $names)
    {
        foreach ($kind in @('actual', 'reference', 'overlay', 'heatmap'))
        {
            Assert-True (Test-Path -LiteralPath (Join-Path $output ($captureName + '-' + $kind + '.png'))) "missing $captureName $kind image"
        }
    }
    foreach ($asset in @($report.assets))
    {
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$asset.sourcePath)) 'asset must have mapped source path'
        Assert-True ([string]$asset.importedSha256 -match '^[0-9A-F]{64}$') 'asset must have SHA-256'
    }
    Write-Output 'LAN lobby visual-diff smoke: PASS'
}
finally
{
    if (Test-Path -LiteralPath $scratch)
    {
        try { Remove-Item -LiteralPath $scratch -Force -Recurse -ErrorAction Stop }
        catch { Write-Warning "Smoke fixture cleanup deferred: $scratch" }
    }
}
