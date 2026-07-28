[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$exportScript = Join-Path $PSScriptRoot 'ExportLanLobbyVisualDiff.ps1'
$assetMapPath = Join-Path $projectRoot 'docs/references/ui/lobby/ASSET_MAP.md'
$scratch = Join-Path $projectRoot ('Temp/LAN-LOBBY-VisualDiffSmoke-' + [Guid]::NewGuid().ToString('N'))
$approvedAssets = & {
    . (Join-Path $PSScriptRoot 'LanLobbyEvidence.Common.ps1')
    Get-LanLobbyAssetMap -ProjectRoot $projectRoot -AssetMapPath $assetMapPath
}
$script:assertionCount = 0
$script:fixtureCount = 0

function Assert-True([bool] $Condition, [string] $Message)
{
    $script:assertionCount++
    if (-not $Condition) { throw "Assertion failed: $Message (fixtures=$script:fixtureCount; assertions=$script:assertionCount)" }
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
    $script:fixtureCount++
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

function New-NormalizedRoomCapture([string] $ReferencePath, [string] $Path)
{
    $script:fixtureCount++
    $reference = [Drawing.Bitmap]::FromFile($ReferencePath)
    $bitmap = New-Object Drawing.Bitmap 1920, 1080
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try
    {
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
        $graphics.DrawImage($reference, 0, 0, 1920, 1080)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $graphics.Dispose()
        $bitmap.Dispose()
        $reference.Dispose()
    }
}

function Write-LanLobbySmokeManifest([string] $Path, $Manifest)
{
    [IO.File]::WriteAllText(
        $Path,
        ($Manifest | ConvertTo-Json -Depth 20),
        (New-Object Text.UTF8Encoding($false)))
}

function Invoke-LanLobbyVisualMutation(
    [string] $SourceCaptureDirectory,
    [string] $ReferenceDirectory,
    [string] $CaseName,
    [scriptblock] $Mutate)
{
    $script:fixtureCount++
    $caseCaptureDirectory = Join-Path $scratch ($CaseName + '-captures')
    Copy-Item -LiteralPath $SourceCaptureDirectory -Destination $caseCaptureDirectory -Recurse
    $caseManifestPath = Join-Path $caseCaptureDirectory 'manifest.json'
    $caseManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $caseManifestPath | ConvertFrom-Json
    foreach ($record in $caseManifest.captures)
    {
        $record.path = Join-Path $caseCaptureDirectory ($record.name + '.png')
    }
    & $Mutate $caseManifest $caseCaptureDirectory
    Write-LanLobbySmokeManifest $caseManifestPath $caseManifest
    $caseOutput = Join-Path $scratch ($CaseName + '-output')
    & $exportScript -CaptureDirectory $caseCaptureDirectory -OutputDirectory $caseOutput -ReferenceDirectory $ReferenceDirectory | Out-Null
    return [pscustomobject]@{
        report = Get-Content -Raw -LiteralPath (Join-Path $caseOutput 'visual-diff-report.json') | ConvertFrom-Json
        output = $caseOutput
        captures = $caseCaptureDirectory
    }
}

function Add-LanLobbyFixturePixels(
    [string] $Path,
    [Drawing.Color] $Color,
    [int] $X,
    [int] $Y,
    [int] $Width,
    [int] $Height)
{
    $source = [Drawing.Bitmap]::FromFile($Path)
    $bitmap = New-Object Drawing.Bitmap $source
    $source.Dispose()
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $brush = New-Object Drawing.SolidBrush $Color
    try
    {
        $graphics.FillRectangle($brush, $X, $Y, $Width, $Height)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $brush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Shift-LanLobbyFixtureRegion(
    [string] $Path,
    [Drawing.Rectangle] $Region,
    [int] $DeltaX,
    [int] $DeltaY)
{
    $source = [Drawing.Bitmap]::FromFile($Path)
    $bitmap = New-Object Drawing.Bitmap $source
    $source.Dispose()
    $crop = $bitmap.Clone($Region, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try
    {
        $graphics.FillRectangle([Drawing.Brushes]::Black, $Region)
        $graphics.DrawImageUnscaled($crop, $Region.X + $DeltaX, $Region.Y + $DeltaY)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $graphics.Dispose()
        $crop.Dispose()
        $bitmap.Dispose()
    }
}

function Assert-LanLobbyFailedRoiDrawn(
    [string] $OutputDirectory,
    [string] $CaptureName,
    $Roi,
    [string] $Label)
{
    foreach ($kind in @('overlay','heatmap'))
    {
        $path = Join-Path $OutputDirectory "$CaptureName-$kind.png"
        $bitmap = [Drawing.Bitmap]::FromFile($path)
        try
        {
            $redBorderPixels = 0
            for ($x = [int]$Roi.x; $x -lt ([int]$Roi.x + [int]$Roi.width); $x++)
            {
                foreach ($y in @([int]$Roi.y, ([int]$Roi.y + [int]$Roi.height - 1)))
                {
                    $pixel = $bitmap.GetPixel($x, $y)
                    if ($pixel.R -ge 240 -and $pixel.G -le 24 -and $pixel.B -le 24) { $redBorderPixels++ }
                }
            }
            for ($y = [int]$Roi.y; $y -lt ([int]$Roi.y + [int]$Roi.height); $y++)
            {
                foreach ($x in @([int]$Roi.x, ([int]$Roi.x + [int]$Roi.width - 1)))
                {
                    $pixel = $bitmap.GetPixel($x, $y)
                    if ($pixel.R -ge 240 -and $pixel.G -le 24 -and $pixel.B -le 24) { $redBorderPixels++ }
                }
            }
            Assert-True ($redBorderPixels -gt 20) "$Label failed ROI must be red on $kind"
        }
        finally { $bitmap.Dispose() }
    }
}

function Fill-RoomEvidenceFixture(
    $Graphics,
    [int] $CanvasWidth,
    [int] $CanvasHeight,
    [ValidateSet('room-host','room-full','room-ready')] [string] $State)
{
    $scaleX = $CanvasWidth / 1920.0
    $scaleY = $CanvasHeight / 1080.0
    $cyan = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 0, 220, 220))
    $gray = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 105, 105, 105))
    $light = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 235, 235, 235))
    $dark = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 24, 24, 24))
    $mutedLight = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 155, 155, 155))
    $creatorCyan = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 9, 187, 151))
    function Fill-Normalized($Brush, [double] $X, [double] $Y, [double] $Width, [double] $Height)
    {
        $Graphics.FillRectangle(
            $Brush,
            [int][Math]::Round($X * $scaleX),
            [int][Math]::Round($Y * $scaleY),
            [Math]::Max(1, [int][Math]::Round($Width * $scaleX)),
            [Math]::Max(1, [int][Math]::Round($Height * $scaleY)))
    }
    function Fill-Contour($Brush, [double] $X, [double] $Y, [double] $Width, [double] $Height)
    {
        Fill-Normalized $Brush $X $Y $Width 5
        Fill-Normalized $Brush $X ($Y + $Height - 5) $Width 5
        Fill-Normalized $Brush $X $Y 5 $Height
        Fill-Normalized $Brush ($X + $Width - 5) $Y 5 $Height
    }
    try
    {
        $roots = @(199.5, 588.75, 976.5, 1365.0)
        for ($slot = 0; $slot -lt 4; $slot++)
        {
            $bodyX = $roots[$slot] + 26.25
            $bodyY = 177.75
            $isReady = $slot -eq 0 -or $State -eq 'room-ready'
            $isWaiting = $slot -gt 0 -and $State -eq 'room-full'
            $isEmpty = $slot -gt 0 -and $State -eq 'room-host'
            $stateBrush = if ($isReady) { $cyan } else { $gray }
            Fill-Normalized $stateBrush $bodyX $bodyY 320.25 30
            Fill-Contour $stateBrush $bodyX $bodyY 320.25 545.25
            if ($isReady)
            {
                Fill-Normalized $cyan $bodyX 630 320.25 85
                Fill-Contour $dark ($bodyX + 88) 660 38 37
                Fill-Contour $dark ($bodyX + 144) 664 92 29
            }
            elseif ($isWaiting)
            {
                Fill-Normalized $gray ($bodyX + 35) ($bodyY + 110) 250 320
            }
            elseif ($isEmpty)
            {
                Fill-Normalized $gray ($bodyX + 50) ($bodyY + 85) 220 360
                Fill-Normalized $light ($bodyX + 137) ($bodyY + 230) 46 46
                Fill-Normalized $light ($bodyX + 117) ($bodyY + 295) 86 20
            }
        }
        Fill-Normalized $creatorCyan 318 231 124 35
        $primaryBrush = if ($State -eq 'room-full') { $gray } else { $cyan }
        Fill-Normalized $primaryBrush 1487.25 942.75 432 94.5
        if ($State -eq 'room-full')
        {
            Fill-Normalized $mutedLight 1571 964 63 52
            Fill-Normalized $mutedLight 1650 970 153 36
        }
        else
        {
            Fill-Contour $dark 1573 964 62 52
            Fill-Contour $dark 1652 971 140 35
        }
        Fill-Normalized $light 65 39 35 24
    }
    finally
    {
        $cyan.Dispose()
        $gray.Dispose()
        $light.Dispose()
        $dark.Dispose()
        $mutedLight.Dispose()
        $creatorCyan.Dispose()
    }
}

function Fill-ScaledFixtureRectangle($Graphics, $Brush, $NativeCrop, $TargetSize, $Bounds)
{
    $x = $NativeCrop.x + [int][Math]::Round($Bounds.x * $NativeCrop.width / $TargetSize.width)
    $y = $NativeCrop.y + [int][Math]::Round($Bounds.y * $NativeCrop.height / $TargetSize.height)
    $right = $NativeCrop.x + [int][Math]::Round(($Bounds.x + $Bounds.width) * $NativeCrop.width / $TargetSize.width)
    $bottom = $NativeCrop.y + [int][Math]::Round(($Bounds.y + $Bounds.height) * $NativeCrop.height / $TargetSize.height)
    $Graphics.FillRectangle($Brush, $x, $y, [Math]::Max(1, $right-$x), [Math]::Max(1, $bottom-$y))
}

function Fill-CreateOpenFrameFixture($Graphics, $BrightBrush, $LeftBrush, $NativeCrop, $TargetSize, [int] $TopGapPixels)
{
    $topSegments = if ($TopGapPixels -gt 0) {
        @(
            [ordered]@{ x=25; y=0; width=300; height=3 },
            [ordered]@{ x=(325 + $TopGapPixels); y=0; width=(666 - 300 - $TopGapPixels); height=3 }
        )
    } else {
        @([ordered]@{ x=25; y=0; width=666; height=3 })
    }
    $brightSegments = @($topSegments) + @(
        [ordered]@{ x=688; y=0; width=3; height=236 },
        [ordered]@{ x=17; y=0; width=18; height=18 },
        [ordered]@{ x=682; y=0; width=18; height=18 }
    )
    foreach ($segment in $brightSegments)
    {
        Fill-ScaledFixtureRectangle $Graphics $BrightBrush $NativeCrop $TargetSize $segment
    }
    Fill-ScaledFixtureRectangle $Graphics $LeftBrush $NativeCrop $TargetSize ([ordered]@{ x=25; y=0; width=3; height=236 })
}

function Fill-JoinDecorationFixture($Graphics, $NativeCrop, $TargetSize, $Bounds, [int] $Text01OffsetX, [bool] $UseCycle2ActualColors)
{
    $orangeBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Orange)
    $logoBrush = New-Object Drawing.SolidBrush $(if ($UseCycle2ActualColors) {
        [Drawing.Color]::FromArgb(255, 198, 90, 60)
    } else {
        [Drawing.Color]::FromArgb(255, 198, 98, 60)
    })
    $text02Brush = New-Object Drawing.SolidBrush $(if ($UseCycle2ActualColors) {
        [Drawing.Color]::FromArgb(255, 204, 98, 60)
    } else {
        [Drawing.Color]::FromArgb(255, 171, 71, 60)
    })
    $centralBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 220, 120, 40))
    $blockTopologyBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 220, 120, 40))
    $blockBrush = New-Object Drawing.SolidBrush $(if ($UseCycle2ActualColors) {
        [Drawing.Color]::FromArgb(255, 143, 143, 143)
    } else {
        [Drawing.Color]::FromArgb(255, 161, 161, 161)
    })
    $inputBackgroundBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 20, 20, 20))
    $inputBrush = New-Object Drawing.SolidBrush $(if ($UseCycle2ActualColors) {
        [Drawing.Color]::FromArgb(255, 112, 111, 112)
    } else {
        [Drawing.Color]::FromArgb(255, 112, 112, 112)
    })
    try
    {
        Fill-ScaledFixtureRectangle $Graphics $inputBackgroundBrush $NativeCrop $TargetSize ([ordered]@{ x=105; y=200; width=510; height=69 })
        foreach ($bound in @($Bounds | Where-Object { $_.name -in @('block-bank', 'input') }))
        {
            $drawBounds = [ordered]@{ x=$bound.x; y=$bound.y; width=$bound.width; height=$bound.height }
            if ($bound.name -eq 'block-bank')
            {
                # The decoded edge body begins after the asset's near-background/transparent
                # left margin. The exporter must publish and apply the explicit adjustment.
                # Inset the solid body by one pixel so the decoded edge union itself is
                # x=54,y=107,w=640,h=89 before the asset-specific adjustment.
                $drawBounds.x += 10
                $drawBounds.y += 1
                $drawBounds.width -= 1
                $drawBounds.height -= 2
            }
            $brush = if ($bound.name -eq 'block-bank') { $blockBrush } elseif ($bound.name -eq 'input') { $inputBrush } else { $orangeBrush }
            Fill-ScaledFixtureRectangle $Graphics $brush $NativeCrop $TargetSize $drawBounds
        }
        foreach ($topologyRun in @(
            [ordered]@{ x=150; y=140; width=70; height=10 },
            [ordered]@{ x=222; y=140; width=366; height=10 }
        ))
        {
            Fill-ScaledFixtureRectangle $Graphics $blockTopologyBrush $NativeCrop $TargetSize $topologyRun
        }
        foreach ($bound in @($Bounds | Where-Object { $_.name -notin @('block-bank', 'input') }))
        {
            $drawBounds = [ordered]@{ x=$bound.x; y=$bound.y; width=$bound.width; height=$bound.height }
            if ($bound.name -eq 'text-01')
            {
                $drawBounds = if ($UseCycle2ActualColors) {
                    [ordered]@{ x=(393 + $Text01OffsetX); y=58; width=61; height=5 }
                } else {
                    [ordered]@{ x=393; y=58; width=61; height=3 }
                }
            }
            elseif ($bound.name -eq 'triangle')
            {
                $drawBounds = [ordered]@{ x=342; y=49; width=24; height=12 }
            }
            elseif ($bound.name -eq 'central-blank')
            {
                $drawBounds = if ($UseCycle2ActualColors) {
                    [ordered]@{ x=324; y=69; width=58; height=58 }
                } else {
                    [ordered]@{ x=323; y=72; width=59; height=56 }
                }
            }
            $brush = if ($bound.name -eq 'logo') {
                $logoBrush
            } elseif ($bound.name -eq 'text-02') {
                $text02Brush
            } elseif ($bound.name -eq 'central-blank') {
                $centralBrush
            } else {
                $orangeBrush
            }
            Fill-ScaledFixtureRectangle $Graphics $brush $NativeCrop $TargetSize $drawBounds
        }
    }
    finally
    {
        $orangeBrush.Dispose()
        $logoBrush.Dispose()
        $text02Brush.Dispose()
        $centralBrush.Dispose()
        $blockTopologyBrush.Dispose()
        $blockBrush.Dispose()
        $inputBackgroundBrush.Dispose()
        $inputBrush.Dispose()
    }
}

function New-JoinDecorationSpriteSources()
{
    $prefix = 'LanLobbyRoot/Home/RoomSelect/Join'
    return @(
        0..1 | ForEach-Object { [ordered]@{ node = "$prefix/LeftBlock_$_"; spriteName = 'room_select_join_left_block'; sourcePath = '[uc]autochessouter/room_select_join_left_block.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=(1199 + 84 * $_); y=288; width=125; height=89 } }
        0..3 | ForEach-Object { [ordered]@{ node = "$prefix/MiddleBlock_$_"; spriteName = 'room_select_join_middle_block'; sourcePath = '[uc]autochessouter/room_select_join_middle_block.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=(1367 + 67 * $_); y=288; width=108; height=89 } }
        0..1 | ForEach-Object { [ordered]@{ node = "$prefix/RightBlock_$_"; spriteName = 'room_select_join_right_block'; sourcePath = '[uc]autochessouter/room_select_join_right_block.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=(1635 + 80 * $_); y=288; width=121; height=89 } }
        [ordered]@{ node = "$prefix/MiddleMask"; spriteName = 'room_select_join_middle_block_mask'; sourcePath = '[uc]autochessouter/room_select_join_middle_block_mask.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1477; y=362; width=60; height=60 }
        [ordered]@{ node = "$prefix/Blank"; spriteName = 'room_select_join_blank'; sourcePath = '[uc]autochessouter/room_select_join_blank.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1477; y=355; width=60; height=61 }
        0..3 | ForEach-Object { [ordered]@{ node = "$prefix/Ban_$_"; spriteName = 'room_select_join_ban'; sourcePath = '[uc]autochessouter/room_select_join_ban.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=(1488 + 22 * ($_ % 2)); y=(388 - 19 * [int]($_ / 2)); width=13; height=13 } }
        [ordered]@{ node = "$prefix/Triangle"; spriteName = 'room_select_join_triangle'; sourcePath = '[uc]autochessouter/room_select_join_triangle.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1492; y=420; width=30; height=17 }
        [ordered]@{ node = "$prefix/Logo"; spriteName = 'room_select_join_logo'; sourcePath = '[uc]autochessouter/room_select_join_logo.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1245; y=400; width=118; height=20 }
        [ordered]@{ node = "$prefix/Text01"; spriteName = 'room_select_join_text_01'; sourcePath = '[uc]autochessouter/room_select_join_text_01.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1545; y=420; width=65; height=8 }
        [ordered]@{ node = "$prefix/Text02"; spriteName = 'room_select_join_text_02'; sourcePath = '[uc]autochessouter/room_select_join_text_02.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1680; y=411; width=89; height=11 }
        [ordered]@{ node = "$prefix/RoomCodeInput"; spriteName = 'room_select_join_text_bg'; sourcePath = '[uc]autochessouter/room_select_join_text_bg.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1269; y=220; width=482; height=60 }
        [ordered]@{ node = "$prefix/JoinAction"; spriteName = 'room_select_join_btn_bg_down'; sourcePath = '[uc]autochessouter/room_select_join_btn_bg_down.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1154; y=105; width=717; height=99 }
        [ordered]@{ node = "$prefix/JoinAction/ActionIcon"; spriteName = 'join_icon'; sourcePath = '[uc]autochessouter/join_icon.png'; coordinateOrigin='screen-bottom-left'; unit='px'; x=1201; y=134; width=44; height=50 }
    )
}

function Repair-JoinText01Fixture([string] $Path)
{
    $source = [Drawing.Bitmap]::FromFile($Path)
    $bitmap = New-Object Drawing.Bitmap $source
    $source.Dispose()
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $backgroundBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 60, 60, 60))
    $orangeBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Orange)
    try
    {
        $graphics.FillRectangle($backgroundBrush, 1545, 650, 75, 16)
        $graphics.FillRectangle($orangeBrush, 1547, 654, 61, 3)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $orangeBrush.Dispose(); $backgroundBrush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}

function Add-JoinCentralBlankNeighborIntrusionFixture([string] $Path)
{
    $source = [Drawing.Bitmap]::FromFile($Path)
    $bitmap = New-Object Drawing.Bitmap $source
    $source.Dispose()
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $blockBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 143, 143, 143))
    $orangeBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 220, 120, 40))
    try
    {
        # Crop-local x=384,y=116,w=11,h=29 reproduces the exact retained
        # neighboring MiddleBlock orange component without changing the
        # central Blank. A one-pixel decoded-gray moat keeps this component
        # separate from the fixture's synthetic solid topology strip; the
        # four remaining strip pixels still satisfy its >=3 px column rule.
        $graphics.FillRectangle($blockBrush, 1537, 711, 1, 31)
        $graphics.FillRectangle($blockBrush, 1549, 711, 1, 31)
        $graphics.FillRectangle($blockBrush, 1537, 741, 13, 1)
        $graphics.FillRectangle($orangeBrush, 1538, 712, 11, 29)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $orangeBrush.Dispose(); $blockBrush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}

function Shift-JoinCentralBlankFixture([string] $Path, [int] $DeltaX)
{
    $source = [Drawing.Bitmap]::FromFile($Path)
    $bitmap = New-Object Drawing.Bitmap $source
    $source.Dispose()
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $backgroundBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 60, 60, 60))
    $blockBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 143, 143, 143))
    $centralBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 220, 120, 40))
    try
    {
        # Restore the two surfaces behind the original central Blank before
        # redrawing the same decoded content at a genuine displaced position.
        $graphics.FillRectangle($backgroundBrush, 1478, 665, 58, 38)
        $graphics.FillRectangle($blockBrush, 1478, 703, 58, 20)
        $graphics.FillRectangle($centralBrush, 1478 + $DeltaX, 665, 58, 58)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $centralBrush.Dispose()
        $blockBrush.Dispose()
        $backgroundBrush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Shift-JoinBlockBankFixture([string] $Path, [int] $DeltaX)
{
    $source = [Drawing.Bitmap]::FromFile($Path)
    $bitmap = New-Object Drawing.Bitmap $source
    $source.Dispose()
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $backgroundBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 60, 60, 60))
    $blockBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 143, 143, 143))
    $topologyBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 220, 120, 40))
    $centralBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 220, 120, 40))
    try
    {
        $graphics.FillRectangle($backgroundBrush, 1209, 704, 638, 87)
        $graphics.FillRectangle($blockBrush, 1209 + $DeltaX, 704, 638, 87)
        $graphics.FillRectangle($topologyBrush, 1304 + $DeltaX, 736, 70, 10)
        $graphics.FillRectangle($topologyBrush, 1376 + $DeltaX, 736, 366, 10)
        # Restore the overlapping central decoration after moving the bank body.
        $graphics.FillRectangle($centralBrush, 1478, 665, 58, 58)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $centralBrush.Dispose()
        $topologyBrush.Dispose()
        $blockBrush.Dispose()
        $backgroundBrush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Scramble-JoinBlockInternalTopologyFixture([string] $Path)
{
    $source = [Drawing.Bitmap]::FromFile($Path)
    $bitmap = New-Object Drawing.Bitmap $source
    $source.Dispose()
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $blockBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 143, 143, 143))
    $topologyBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 220, 120, 40))
    try
    {
        $graphics.FillRectangle($blockBrush, 1304, 736, 70, 10)
        $graphics.FillRectangle($blockBrush, 1376, 736, 366, 10)
        # Preserve the exact outer bank body while replacing the two reference
        # occupancy runs with the retained Player's single compressed run.
        $graphics.FillRectangle($topologyBrush, 1368, 736, 304, 10)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $topologyBrush.Dispose()
        $blockBrush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Mutate-RoomVisibleThresholdFixture([string] $CaptureDirectory)
{
    $black = New-Object Drawing.SolidBrush ([Drawing.Color]::Black)
    $cyan = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 0, 220, 220))
    $dark = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 24, 24, 24))
    $gray = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 105, 105, 105))
    try
    {
        $hostPath = Join-Path $CaptureDirectory 'room-host.png'
        $hostSource = [Drawing.Bitmap]::FromFile($hostPath)
        $hostBitmap = New-Object Drawing.Bitmap $hostSource
        $hostSource.Dispose()
        $graphics = [Drawing.Graphics]::FromImage($hostBitmap)
        try
        {
            # Keep diagnostic RectTransform data untouched while moving the rendered
            # check pixels +3 px and widening the rendered ready label by +4 px.
            $graphics.FillRectangle($cyan, 314, 660, 41, 37)
            $graphics.FillRectangle($dark, 317, 660, 38, 5)
            $graphics.FillRectangle($dark, 317, 692, 38, 5)
            $graphics.FillRectangle($dark, 317, 665, 5, 27)
            $graphics.FillRectangle($dark, 350, 665, 5, 27)
            $graphics.FillRectangle($cyan, 370, 664, 96, 29)
            $graphics.FillRectangle($dark, 370, 664, 96, 5)
            $graphics.FillRectangle($dark, 370, 688, 96, 5)
            $graphics.FillRectangle($dark, 370, 669, 5, 19)
            $graphics.FillRectangle($dark, 461, 669, 5, 19)
            $hostBitmap.Save($hostPath, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $hostBitmap.Dispose() }

        $fullPath = Join-Path $CaptureDirectory 'room-full.png'
        $fullSource = [Drawing.Bitmap]::FromFile($fullPath)
        $full = New-Object Drawing.Bitmap $fullSource
        $fullSource.Dispose()
        $graphics = [Drawing.Graphics]::FromImage($full)
        try
        {
            # Move only slot 2's visible left contour edge by +5 px.
            $graphics.FillRectangle($black, 615, 208, 5, 515)
            $graphics.FillRectangle($gray, 620, 208, 5, 515)
            $full.Save($fullPath, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $full.Dispose() }

        $readyPath = Join-Path $CaptureDirectory 'room-ready.png'
        $readySource = [Drawing.Bitmap]::FromFile($readyPath)
        $ready = New-Object Drawing.Bitmap $readySource
        $readySource.Dispose()
        $graphics = [Drawing.Graphics]::FromImage($ready)
        try
        {
            # Preserve slot 3's contour extrema but remove most contour pixels so
            # edge tolerances remain satisfied while contour Jaccard drops below .95.
            $graphics.FillRectangle($black, 1008, 213, 310, 500)
            $graphics.FillRectangle($cyan, 1003, 208, 5, 515)
            $graphics.FillRectangle($cyan, 1318, 208, 5, 515)
            $graphics.FillRectangle($cyan, 1003, 208, 320, 5)
            $graphics.FillRectangle($cyan, 1003, 718, 320, 5)
            $ready.Save($readyPath, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $ready.Dispose() }
    }
    finally
    {
        $black.Dispose()
        $cyan.Dispose()
        $dark.Dispose()
        $gray.Dispose()
    }
}

function New-JoinDecorationGeometry()
{
    $prefix = 'LanLobbyRoot/Home/RoomSelect/Join'
    return @(
        [ordered]@{ name = "$prefix/InteriorBacking"; kind = 'code-native-geometry'; isBitmap = $false; spriteName=''; materialName=''; resourcesPath=''; sourcePath=''; sha256=''; color = '#000000D1'; coordinateOrigin='screen-bottom-left'; unit='px'; raycastTarget=$false; x = 1154; y = 204; width = 717; height = 280 }
        [ordered]@{ name = "$prefix/OutlineTop"; kind = 'code-native-geometry'; isBitmap = $false; spriteName=''; materialName=''; resourcesPath=''; sourcePath=''; sha256=''; color = '#3030308C'; coordinateOrigin='screen-bottom-left'; unit='px'; raycastTarget=$false; x = 1154; y = 482; width = 717; height = 2 }
        [ordered]@{ name = "$prefix/OutlineLeft"; kind = 'code-native-geometry'; isBitmap = $false; spriteName=''; materialName=''; resourcesPath=''; sourcePath=''; sha256=''; color = '#3030308C'; coordinateOrigin='screen-bottom-left'; unit='px'; raycastTarget=$false; x = 1154; y = 204; width = 2; height = 280 }
        [ordered]@{ name = "$prefix/OutlineRight"; kind = 'code-native-geometry'; isBitmap = $false; spriteName=''; materialName=''; resourcesPath=''; sourcePath=''; sha256=''; color = '#3030308C'; coordinateOrigin='screen-bottom-left'; unit='px'; raycastTarget=$false; x = 1869; y = 204; width = 2; height = 280 }
        [ordered]@{ name = "$prefix/GuideHorizontal"; kind = 'code-native-geometry'; isBitmap = $false; spriteName=''; materialName=''; resourcesPath=''; sourcePath=''; sha256=''; color = '#FFA5008C'; coordinateOrigin='screen-bottom-left'; unit='px'; raycastTarget=$false; x = 1154; y = 383; width = 717; height = 2 }
        [ordered]@{ name = "$prefix/GuideVertical"; kind = 'code-native-geometry'; isBitmap = $false; spriteName=''; materialName=''; resourcesPath=''; sourcePath=''; sha256=''; color = '#FFA5008C'; coordinateOrigin='screen-bottom-left'; unit='px'; raycastTarget=$false; x = 1506; y = 288; width = 2; height = 196 }
    )
}

try
{
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $captureDirectory = Join-Path $scratch 'captures'
    $referenceDirectory = Join-Path $scratch 'references'
    New-Item -ItemType Directory -Force -Path $captureDirectory, $referenceDirectory | Out-Null

    $figure9 = ([char]0x56FE).ToString() + '9.png'
    $figure11 = ([char]0x56FE).ToString() + '11.png'
    $figure12 = ([char]0x56FE).ToString() + '12.png'
    $figure13 = ([char]0x56FE).ToString() + '13.png'
    $createDecorationTarget = [ordered]@{ x=1296; y=252; width=390; height=179 }
    $createDecorationNativeCrop = [ordered]@{ x=1382; y=260; width=417; height=187 }
    $createFrameTarget = [ordered]@{ x=1154; y=224; width=717; height=374 }
    $createFrameNativeCrop = [ordered]@{ x=1224; y=232; width=745; height=387 }
    $joinDecorationTarget = [ordered]@{ x=1154; y=596; width=717; height=280 }
    $joinDecorationNativeCrop = [ordered]@{ x=1224; y=617; width=745; height=290 }
    $joinDecorationBounds = @(
        [ordered]@{ name='logo';          x=91;  y=64;  width=118; height=20; tolerance=2 },
        [ordered]@{ name='text-01';       x=391; y=56;  width=65;  height=8;  tolerance=2 },
        [ordered]@{ name='text-02';       x=526; y=62;  width=89;  height=11; tolerance=2 },
        [ordered]@{ name='triangle';      x=338; y=47;  width=30;  height=17; tolerance=2 },
        [ordered]@{ name='central-blank'; x=323; y=68;  width=60;  height=61; tolerance=2 },
        [ordered]@{ name='block-bank';    x=45;  y=107; width=639; height=89; tolerance=4 },
        [ordered]@{ name='input';         x=115; y=204; width=482; height=60; tolerance=2 }
    )
    $createDecorationBounds = @(
        [ordered]@{ name='start-room'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=153;y=13;width=84;height=9} },
        [ordered]@{ name='dot-top-left'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=118;y=18;width=17;height=17} },
        [ordered]@{ name='dot-top-right'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=253;y=19;width=17;height=16} },
        [ordered]@{ name='middle-icon'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=152;y=41;width=87;height=86} },
        [ordered]@{ name='left-bracket'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=130;y=58;width=18;height=54} },
        [ordered]@{ name='right-bracket'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=243;y=58;width=18;height=54} },
        [ordered]@{ name='text-01'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=152;y=134;width=88;height=13} },
        [ordered]@{ name='text-02'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=164;y=147;width=66;height=7} },
        [ordered]@{ name='dot-bottom-left'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=118;y=155;width=16;height=17} },
        [ordered]@{ name='dot-bottom-right'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; expected=@{x=253;y=155;width=17;height=17} }
    )
    $createFrameEdges = @(
        [ordered]@{
            name='top'; axis='x'
            search=@{x=25;y=0;width=666;height=18}
            background=@{x=25;y=26;width=666;height=10}
            minimumCoverage=.90; maximumGap=6; minimumContrast=18
        },
        [ordered]@{
            name='left'; axis='y'
            search=@{x=16;y=0;width=18;height=236}
            background=@{x=42;y=0;width=10;height=236}
            minimumCoverage=.90; maximumGap=6; minimumContrast=18
        },
        [ordered]@{
            name='right'; axis='y'
            search=@{x=682;y=0;width=18;height=236}
            background=@{x=664;y=0;width=10;height=236}
            minimumCoverage=.90; maximumGap=6; minimumContrast=18
        },
        [ordered]@{
            name='top-left-joint'; axis='joint'
            search=@{x=16;y=0;width=28;height=28}
            background=@{x=42;y=30;width=16;height=16}
            minimumPixelCount=80; minimumContrast=18
        },
        [ordered]@{
            name='top-right-joint'; axis='joint'
            search=@{x=673;y=0;width=28;height=28}
            background=@{x=659;y=30;width=16;height=16}
            minimumPixelCount=80; minimumContrast=18
        }
    )
    New-SolidPng (Join-Path $referenceDirectory $figure9) 2048 1118 ([Drawing.Color]::FromArgb(255, 60, 60, 60)) {
        param($graphics)
        $contentBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Black)
        $cyanBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Cyan)
        try
        {
            # Native fixture coordinates that become the approved visible bounds after each 745x104 action crop is resized to 717x99.
            $graphics.FillRectangle($contentBrush, 1224 + 49, 468 + 26, 37, 39)
            $graphics.FillRectangle($contentBrush, 1224 + 113, 468 + 29, 154, 34)
            $graphics.FillRectangle($contentBrush, 1224 + 49, 906 + 21, 46, 53)
            $graphics.FillRectangle($contentBrush, 1224 + 108, 906 + 33, 156, 36)
            foreach ($bounds in $createDecorationBounds)
            {
                Fill-ScaledFixtureRectangle $graphics $cyanBrush $createDecorationNativeCrop $createDecorationTarget $bounds.expected
            }
            Fill-JoinDecorationFixture $graphics $joinDecorationNativeCrop $joinDecorationTarget $joinDecorationBounds 0 $false
            Fill-CreateOpenFrameFixture $graphics $cyanBrush $cyanBrush $createFrameNativeCrop $createFrameTarget 0
        }
        finally { $contentBrush.Dispose(); $cyanBrush.Dispose() }
    }
    New-SolidPng (Join-Path $referenceDirectory $figure11) 2560 1440 ([Drawing.Color]::Black) {
        param($graphics)
        Fill-RoomEvidenceFixture $graphics 2560 1440 'room-host'
    }
    New-SolidPng (Join-Path $referenceDirectory $figure12) 2560 1440 ([Drawing.Color]::Black) {
        param($graphics)
        Fill-RoomEvidenceFixture $graphics 2560 1440 'room-full'
    }
    New-SolidPng (Join-Path $referenceDirectory $figure13) 2560 1440 ([Drawing.Color]::Black) {
        param($graphics)
        Fill-RoomEvidenceFixture $graphics 2560 1440 'room-ready'
    }

    $names = @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full')
    $roomSpriteSha = @{}
    foreach ($spriteName in @('bg_terrain','player_card_ready','host_top_tag','btn_match_normal','btn_match_grey','btn_topmenu_back','icon_amiy'))
    {
        $relative = if ($spriteName -eq 'icon_amiy') { "Assets/Resources/UI/Lobby/Home/$spriteName.png" } else { "Assets/Resources/UI/Lobby/$spriteName.png" }
        $roomSpriteSha[$spriteName] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $projectRoot $relative)).Hash
    }
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
                $cyanBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Cyan)
                $lowContrastBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 8, 16, 15))
                try
                {
                    # Masked radar: x=0.00,y=0.18,w=0.57,h=0.66.
                    $graphics.FillRectangle($maskedBrush, 100, 300, 120, 80)
                    # create-room: x=0.60,y=0.42,w=0.36,h=0.11.
                    $graphics.FillRectangle($differenceBrush, 1200, 480, 100, 60)
                    # Create actual crop is (1161,449); its icon is deliberately 2 px right.
                    $graphics.FillRectangle($contentBrush, 1161 + 49, 449 + 25, 36, 37)
                    $graphics.FillRectangle($contentBrush, 1161 + 109, 449 + 28, 148, 32)
                    $graphics.FillRectangle($contentBrush, 1161 + 54, 449 + 73, 20, 2)
                    # Join actual crop is (1154,876) and matches all approved visible bounds.
                    $graphics.FillRectangle($contentBrush, 1154 + 47, 876 + 20, 44, 50)
                    $graphics.FillRectangle($contentBrush, 1154 + 104, 876 + 31, 150, 34)
                    $graphics.FillRectangle($contentBrush, 1154 + 40, 876 + 72, 60, 3)
                    foreach ($bounds in $createDecorationBounds)
                    {
                        # Leave one valid-ROI diagnostic empty so the exporter must record
                        # measurement unavailability without blocking frame publication.
                        if ($bounds.name -ne 'text-02')
                        {
                            $graphics.FillRectangle(
                                $cyanBrush,
                                $createDecorationTarget.x + $bounds.expected.x,
                                $createDecorationTarget.y + $bounds.expected.y,
                                $bounds.expected.width,
                                $bounds.expected.height)
                        }
                    }
                    # text-01 deliberately exceeds its 2 px visible-bound tolerance.
                    Fill-JoinDecorationFixture $graphics $joinDecorationTarget $joinDecorationTarget $joinDecorationBounds 5 $true
                    Fill-CreateOpenFrameFixture $graphics $cyanBrush $lowContrastBrush $createFrameTarget $createFrameTarget 8
                }
                finally { $maskedBrush.Dispose(); $differenceBrush.Dispose(); $contentBrush.Dispose(); $cyanBrush.Dispose(); $lowContrastBrush.Dispose() }
            }
        }
        if ($name -like 'room-*')
        {
            $roomReferenceName = if ($name -eq 'room-host') { $figure11 } elseif ($name -eq 'room-full') { $figure12 } else { $figure13 }
            New-NormalizedRoomCapture (Join-Path $referenceDirectory $roomReferenceName) $actualPath
        }
        else
        {
            New-SolidPng $actualPath 1920 1080 ([Drawing.Color]::FromArgb(255, 60, 60, 60)) $draw
        }
        $actionRects = if ($name -eq 'home') {
            @(
                # Deliberately offset and resize Create so the report must derive non-zero deltas.
                [ordered]@{ name = 'LanLobbyRoot/Home/RoomSelect/Create/CreateAction'; coordinateOrigin = 'screen-bottom-left'; unit = 'px'; x = 1161; y = 536; width = 711; height = 95 },
                [ordered]@{ name = 'LanLobbyRoot/Home/RoomSelect/Join/JoinAction'; coordinateOrigin = 'screen-bottom-left'; unit = 'px'; x = 1154; y = 105; width = 717; height = 99 }
            )
        } else { @() }
        $roomSpriteSources = if ($name -like 'room-*') {
            $primarySprite = if ($name -eq 'room-full') { 'btn_match_grey' } else { 'btn_match_normal' }
            @(
                [ordered]@{ node='LanLobbyRoot/Terrain';spriteName='bg_terrain';sourcePath='[uc]autochessouter/bg_terrain.png';coordinateOrigin='screen-bottom-left';unit='px';x=0;y=0;width=1920;height=1080;raycastTarget=$false },
                [ordered]@{ node='LanLobbyRoot/Room/RoomCard_0/OccupiedContent/ReadyIcon';spriteName='player_card_ready';sourcePath='[uc]autochessouter/player_card_ready.png';coordinateOrigin='screen-bottom-left';unit='px';x=314;y=383;width=38;height=37;raycastTarget=$false },
                [ordered]@{ node='LanLobbyRoot/Room/RoomCard_0/CreatorTag';spriteName='host_top_tag';sourcePath='[uc]autochessouter/host_top_tag.png';coordinateOrigin='screen-bottom-left';unit='px';x=318;y=814;width=124;height=35;raycastTarget=$false },
                [ordered]@{ node='LanLobbyRoot/Room/PrimaryAction';spriteName=$primarySprite;sourcePath="[uc]autochessouter/$primarySprite.png";coordinateOrigin='screen-bottom-left';unit='px';x=1479;y=41;width=441;height=104;raycastTarget=$true },
                [ordered]@{ node='LanLobbyRoot/Room/LeaveAction';spriteName='btn_topmenu_back';sourcePath='[uc]autochessouter/btn_topmenu_back.png';coordinateOrigin='screen-bottom-left';unit='px';x=36;y=989;width=134;height=69;raycastTarget=$true }
            )
        } else { @() }
        $records += [ordered]@{
            name = $name
            path = $actualPath
            width = 1920
            height = 1080
            localPlayerId = $(if ($name -like 'room-*') { 'capture-host' } else { '' })
            members = $(if ($name -like 'room-*') { @([ordered]@{ playerId='capture-host';displayName='Doctor';isReady=$true }) } else { @() })
            spriteSources = @($(if ($name -like 'room-*') {
                @($roomSpriteSources)
            } else {
                @([ordered]@{ node = 'LanLobbyRoot/Terrain'; spriteName = 'bg_terrain'; sourcePath = '[uc]autochessouter/bg_terrain.png' })
            })) + @($(if ($name -in @('home', 'discovered-prefill')) {
                @(
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/TitleDot'; spriteName = 'room_select_dot'; sourcePath = '[uc]autochessouter/room_select_dot.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/Wings/WingLeftUpper'; spriteName = 'img_pointer'; sourcePath = '[uc]autochessouter/img_pointer.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/Wings/WingLeftLower'; spriteName = 'img_pointer'; sourcePath = '[uc]autochessouter/img_pointer.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/Wings/WingRightUpper'; spriteName = 'img_pointer'; sourcePath = '[uc]autochessouter/img_pointer.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/Wings/WingRightLower'; spriteName = 'img_pointer'; sourcePath = '[uc]autochessouter/img_pointer.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/CreateFrame/Top_0'; spriteName = 'doc_frame_line'; sourcePath = '[uc]autochessouter/doc_frame_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/CreateFrame/Top_1'; spriteName = 'doc_frame_line'; sourcePath = '[uc]autochessouter/doc_frame_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/CreateFrame/Top_2'; spriteName = 'doc_frame_line'; sourcePath = '[uc]autochessouter/doc_frame_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/CreateFrame/LeftUpper'; spriteName = 'doc_frame_line'; sourcePath = '[uc]autochessouter/doc_frame_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/CreateFrame/LeftLower'; spriteName = 'doc_frame_line'; sourcePath = '[uc]autochessouter/doc_frame_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/CreateFrame/RightUpper'; spriteName = 'doc_frame_line'; sourcePath = '[uc]autochessouter/doc_frame_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/CreateFrame/RightLower'; spriteName = 'doc_frame_line'; sourcePath = '[uc]autochessouter/doc_frame_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/DotTopLeft'; spriteName = 'room_select_dot'; sourcePath = '[uc]autochessouter/room_select_dot.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/DotTopRight'; spriteName = 'room_select_dot'; sourcePath = '[uc]autochessouter/room_select_dot.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/DotBottomLeft'; spriteName = 'room_select_dot'; sourcePath = '[uc]autochessouter/room_select_dot.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/DotBottomRight'; spriteName = 'room_select_dot'; sourcePath = '[uc]autochessouter/room_select_dot.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/LineLeft'; spriteName = 'room_select_create_left_line'; sourcePath = '[uc]autochessouter/room_select_create_left_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/LineRight'; spriteName = 'room_select_create_left_line'; sourcePath = '[uc]autochessouter/room_select_create_left_line.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/MiddleIcon'; spriteName = 'room_select_create_middleicon'; sourcePath = '[uc]autochessouter/room_select_create_middleicon.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/Text01'; spriteName = 'room_select_create_text_01'; sourcePath = '[uc]autochessouter/room_select_create_text_01.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/Text02'; spriteName = 'room_select_create_text_02'; sourcePath = '[uc]autochessouter/room_select_create_text_02.png' },
                    [ordered]@{ node = 'LanLobbyRoot/Home/RoomSelect/Create/StartRoomDecoration'; spriteName = 'room_select_img_startroom'; sourcePath = '[uc]autochessouter/room_select_img_startroom.png' }
                )
            } else { @() })) + @($(if ($name -in @('home', 'discovered-prefill')) { New-JoinDecorationSpriteSources } else { @() }))
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
                [ordered]@{ name = 'LanLobbyRoot/OpaqueBlocker'; kind = 'code-native-geometry'; isBitmap = $false; spriteName=''; materialName=''; resourcesPath=''; sourcePath=''; sha256=''; color = '#060F14FF'; coordinateOrigin='screen-bottom-left'; unit='px'; raycastTarget=$false; x = 0; y = 0; width = 1920; height = 1080 }
            ) + $(if ($name -in @('home', 'discovered-prefill')) { New-JoinDecorationGeometry } else { @() })
            sourceAudit = $(if ($name -like 'room-*') {
                @(
                    foreach ($sprite in $roomSpriteSources)
                    {
                        [ordered]@{
                            node=$sprite.node
                            kind='bitmap-sprite'
                            isBitmap=$true
                            spriteName=$sprite.spriteName
                            materialName=''
                            resourcesPath=$(if ($sprite.spriteName -eq 'icon_amiy') { 'UI/Lobby/Home/icon_amiy' } else { "UI/Lobby/$($sprite.spriteName)" })
                            sourcePath=$sprite.sourcePath
                            sha256=$roomSpriteSha[[string]$sprite.spriteName]
                            captures=@($name)
                            occurrenceCount=1
                            raycastTarget=$sprite.raycastTarget
                        }
                    }
                )
            } else { @() })
        }
    }
    foreach ($record in $records)
    {
        foreach ($spriteSource in $record.spriteSources)
        {
            $approvedAsset = $approvedAssets[[string]$spriteSource.spriteName]
            if ($null -eq $approvedAsset)
            {
                throw "Smoke fixture has no approved asset-map row for $($spriteSource.spriteName)."
            }
            $spriteSource['resourcesPath'] = $approvedAsset.ResourcesPath
            $spriteSource['sha256'] = $approvedAsset.DeclaredSha256
            $spriteSource['captures'] = @([string]$record.name)
            $spriteSource['occurrenceCount'] = 1
            if (-not $spriteSource.Contains('coordinateOrigin'))
            {
                $spriteSource['coordinateOrigin'] = 'screen-bottom-left'
                $spriteSource['unit'] = 'px'
                $spriteSource['x'] = 10
                $spriteSource['y'] = 10
                $spriteSource['width'] = 10
                $spriteSource['height'] = 10
            }
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

    $invalidJoinGeometryCases = @(
        [pscustomobject]@{
            name = 'invalid-join-geometry-bitmap'
            expectedMessage = 'must declare kind=code-native-geometry and isBitmap=false'
            mutate = { param($geometry) $geometry.isBitmap = $true }
        },
        [pscustomobject]@{
            name = 'invalid-join-geometry-raycast'
            expectedMessage = 'must declare raycastTarget=false'
            mutate = { param($geometry) $geometry.raycastTarget = $true }
        },
        [pscustomobject]@{
            name = 'invalid-join-geometry-origin'
            expectedMessage = 'must declare coordinateOrigin=screen-bottom-left and unit=px'
            mutate = { param($geometry) $geometry.coordinateOrigin = 'screen-top-left' }
        },
        [pscustomobject]@{
            name = 'invalid-join-geometry-sprite-name'
            expectedMessage = 'must not declare nonempty spriteName'
            mutate = { param($geometry) $geometry.spriteName = 'forbidden' }
        },
        [pscustomobject]@{
            name = 'invalid-join-geometry-material-name'
            expectedMessage = 'must not declare nonempty materialName'
            mutate = { param($geometry) $geometry.materialName = 'forbidden' }
        },
        [pscustomobject]@{
            name = 'invalid-join-geometry-resources-path'
            expectedMessage = 'must not declare nonempty resourcesPath'
            mutate = { param($geometry) $geometry.resourcesPath = 'UI/forbidden' }
        },
        [pscustomobject]@{
            name = 'invalid-join-geometry-source-path'
            expectedMessage = 'must not declare nonempty sourcePath'
            mutate = { param($geometry) $geometry.sourcePath = 'forbidden.png' }
        },
        [pscustomobject]@{
            name = 'invalid-join-geometry-sha256'
            expectedMessage = 'must not declare nonempty sha256'
            mutate = { param($geometry) $geometry.sha256 = ('A' * 64) }
        }
    )
    foreach ($invalidJoinGeometryCase in $invalidJoinGeometryCases)
    {
        $invalidJoinGeometryCaptureDirectory = Join-Path $scratch ($invalidJoinGeometryCase.name + '-captures')
        Copy-Item -LiteralPath $captureDirectory -Destination $invalidJoinGeometryCaptureDirectory -Recurse
        $invalidJoinGeometryManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $invalidJoinGeometryCaptureDirectory 'manifest.json') | ConvertFrom-Json
        foreach ($record in $invalidJoinGeometryManifest.captures) { $record.path = Join-Path $invalidJoinGeometryCaptureDirectory ($record.name + '.png') }
        $invalidJoinGeometryHome = @($invalidJoinGeometryManifest.captures | Where-Object name -eq 'home')[0]
        $invalidJoinGeometry = @($invalidJoinGeometryHome.codeNativeGeometry | Where-Object name -eq 'LanLobbyRoot/Home/RoomSelect/Join/OutlineTop')[0]
        $mutateJoinGeometry = $invalidJoinGeometryCase.mutate
        & $mutateJoinGeometry $invalidJoinGeometry
        $invalidJoinGeometryManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $invalidJoinGeometryCaptureDirectory 'manifest.json') -Encoding UTF8
        $invalidJoinGeometryOutput = Join-Path $scratch ($invalidJoinGeometryCase.name + '-output')
        Assert-FailsWithoutOutput {
            & $exportScript -CaptureDirectory $invalidJoinGeometryCaptureDirectory -OutputDirectory $invalidJoinGeometryOutput -ReferenceDirectory $referenceDirectory
        } $invalidJoinGeometryOutput $invalidJoinGeometryCase.expectedMessage
    }

    $invalidJoinSpriteCaptureDirectory = Join-Path $scratch 'invalid-join-sprite-origin-captures'
    Copy-Item -LiteralPath $captureDirectory -Destination $invalidJoinSpriteCaptureDirectory -Recurse
    $invalidJoinSpriteManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $invalidJoinSpriteCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $invalidJoinSpriteManifest.captures) { $record.path = Join-Path $invalidJoinSpriteCaptureDirectory ($record.name + '.png') }
    $invalidJoinSpriteHome = @($invalidJoinSpriteManifest.captures | Where-Object name -eq 'home')[0]
    (@($invalidJoinSpriteHome.spriteSources | Where-Object node -eq 'LanLobbyRoot/Home/RoomSelect/Join/Logo')[0]).coordinateOrigin = 'screen-top-left'
    $invalidJoinSpriteManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $invalidJoinSpriteCaptureDirectory 'manifest.json') -Encoding UTF8
    $invalidJoinSpriteOutput = Join-Path $scratch 'invalid-join-sprite-origin-output'
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $invalidJoinSpriteCaptureDirectory -OutputDirectory $invalidJoinSpriteOutput -ReferenceDirectory $referenceDirectory
    } $invalidJoinSpriteOutput 'must declare coordinateOrigin=screen-bottom-left and unit=px'

    $invalidSpriteNumberCaptureDirectory = Join-Path $scratch 'invalid-sprite-null-coordinate-captures'
    Copy-Item -LiteralPath $captureDirectory -Destination $invalidSpriteNumberCaptureDirectory -Recurse
    $invalidSpriteNumberManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $invalidSpriteNumberCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $invalidSpriteNumberManifest.captures) { $record.path = Join-Path $invalidSpriteNumberCaptureDirectory ($record.name + '.png') }
    $invalidSpriteNumberHome = @($invalidSpriteNumberManifest.captures | Where-Object name -eq 'home')[0]
    (@($invalidSpriteNumberHome.spriteSources | Where-Object node -eq 'LanLobbyRoot/Terrain')[0]).x = $null
    $invalidSpriteNumberManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $invalidSpriteNumberCaptureDirectory 'manifest.json') -Encoding UTF8
    $invalidSpriteNumberOutput = Join-Path $scratch 'invalid-sprite-null-coordinate-output'
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $invalidSpriteNumberCaptureDirectory -OutputDirectory $invalidSpriteNumberOutput -ReferenceDirectory $referenceDirectory
    } $invalidSpriteNumberOutput 'must declare numeric x, y, width, and height'

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
    foreach ($figure in @($figure11, $figure12, $figure13))
    {
        Copy-Item -LiteralPath (Join-Path $referenceDirectory $figure) -Destination (Join-Path $malformedReferences $figure)
    }
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
    } $defaultReferenceOutput 'Expected exactly one reference named'

    $wrongSizedReferences = Join-Path $scratch 'wrong-sized-room-references'
    Copy-Item -LiteralPath $referenceDirectory -Destination $wrongSizedReferences -Recurse
    New-SolidPng (Join-Path $wrongSizedReferences $figure12) 1920 1080 ([Drawing.Color]::Black) $null
    $wrongSizedOutput = Join-Path $scratch 'wrong-sized-room-reference-output'
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $wrongSizedOutput -ReferenceDirectory $wrongSizedReferences
    } $wrongSizedOutput 'must be exactly 2560x1440 before normalization'

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
    Assert-True (Test-Path -LiteralPath (Join-Path $output 'visual-diff-report.json')) 'visual failure must still publish JSON'
    Assert-True (Test-Path -LiteralPath (Join-Path $output 'visual-diff-report.md')) 'visual failure must still publish Markdown'
    foreach ($kind in @('actual','reference','overlay','heatmap'))
    {
        Assert-True (Test-Path -LiteralPath (Join-Path $output ('home-create-frame-' + $kind + '.png'))) "visual failure must still publish home-create-frame $kind"
    }
    $report = Get-Content -Raw -LiteralPath (Join-Path $output 'visual-diff-report.json') | ConvertFrom-Json
    $homeCapture = $report.captures | Where-Object name -eq 'home'
    $roomReferenceMap = @{
        'room-host' = $figure11
        'room-full' = $figure12
        'room-ready' = $figure13
    }
    foreach ($captureName in $roomReferenceMap.Keys)
    {
        $roomCaptureReport = @($report.captures | Where-Object name -eq $captureName)[0]
        Assert-True ($roomCaptureReport.referenceFigure -ceq $roomReferenceMap[$captureName]) "$captureName must route to its exact Figure 11-13 reference"
        Assert-True (($roomCaptureReport.referenceWidth -eq 2560) -and ($roomCaptureReport.referenceHeight -eq 1440)) "$captureName reference dimensions must be validated before normalization"
    }
    $requiredRoomGates = @(
        'RoomHost.Slot1.ReadyTopBar',
        'RoomHost.Slot1.ReadyContour',
        'RoomHost.Slot1.ReadyCheck',
        'RoomHost.Slot1.ReadyLabel',
        'RoomHost.Slot1.CreatorTag',
        'RoomHost.Slot2.EmptyComposition',
        'RoomHost.Slot3.EmptyComposition',
        'RoomHost.Slot4.EmptyComposition',
        'RoomFull.Slot1.ReadyTopBar',
        'RoomFull.Slot1.ReadyContour',
        'RoomFull.Slot1.ReadyCheck',
        'RoomFull.Slot1.ReadyLabel',
        'RoomFull.Slot1.CreatorTag',
        'RoomFull.Slot2.WaitingTopBar',
        'RoomFull.Slot2.WaitingContour',
        'RoomFull.Slot3.WaitingTopBar',
        'RoomFull.Slot3.WaitingContour',
        'RoomReady.Slot1.ReadyTopBar',
        'RoomReady.Slot1.ReadyContour',
        'RoomReady.Slot1.ReadyCheck',
        'RoomReady.Slot1.ReadyLabel',
        'RoomReady.Slot1.CreatorTag',
        'RoomReady.Slot2.ReadyTopBar',
        'RoomReady.Slot2.ReadyContour',
        'RoomReady.Slot2.ReadyCheck',
        'RoomReady.Slot2.ReadyLabel',
        'RoomReady.Slot3.ReadyTopBar',
        'RoomReady.Slot3.ReadyContour',
        'RoomReady.Slot3.ReadyCheck',
        'RoomReady.Slot3.ReadyLabel',
        'RoomFull.PrimaryAction.Gray',
        'RoomFull.PrimaryAction.IconCenter',
        'RoomFull.PrimaryAction.LabelCenter',
        'RoomReady.PrimaryAction.Cyan',
        'RoomReady.PrimaryAction.IconCenter',
        'RoomReady.PrimaryAction.LabelCenter',
        'RoomReady.Leave',
        'RoomHost.Slot1.ProfileContentAbsence',
        'RoomFull.Slot1.ProfileContentAbsence',
        'RoomReady.Slot1.ProfileContentAbsence',
        'RoomHost.LegacyOpenSlotTextAbsence',
        'RoomHost.LegacyWaitingTextAbsence',
        'RoomFull.LegacyOpenSlotTextAbsence',
        'RoomFull.LegacyWaitingTextAbsence',
        'RoomReady.LegacyOpenSlotTextAbsence',
        'RoomReady.LegacyWaitingTextAbsence',
        'RoomHost.Slot2To3.VisibleContourSpacing',
        'RoomHost.Slot3To4.VisibleContourSpacing',
        'RoomFull.Slot2To3.VisibleContourSpacing',
        'RoomReady.Slot2To3.VisibleContourSpacing'
    )
    foreach ($gateName in $requiredRoomGates)
    {
        $gate = @($report.roomGates | Where-Object name -ceq $gateName)
        Assert-True ($gate.Count -eq 1) "named room gate must occur once: $gateName"
        $gate = $gate[0]
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$gate.capture)) "$gateName capture"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$gate.referenceFigure)) "$gateName reference"
        Assert-True ($null -ne $gate.roi) "$gateName ROI"
        Assert-True ($null -ne $gate.exclusions) "$gateName exclusions"
        Assert-True ($null -ne $gate.thresholds) "$gateName thresholds"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$gate.status)) "$gateName status"
        Assert-True ($null -ne $gate.materialEvidence) "$gateName material provenance"
        Assert-True ($gate.materialEvidence.bijectionPassed) "$gateName bitmap manifest bijection"
        Assert-True ($gate.materialEvidence.pathPassed) "$gateName approved Resources/source path"
        Assert-True ($gate.materialEvidence.captureListPassed) "$gateName exact capture list"
        Assert-True ($gate.materialEvidence.aggregatePassed) "$gateName aggregate occurrence inventory"
        Assert-True ($gate.materialEvidence.associationKind -in @('roi-overlap','explicit-no-bitmap')) "$gateName per-gate material association"
    }
    $roomGateMaterialAssociations = @(
        [pscustomobject]@{ gate='RoomHost.Slot1.ReadyCheck';node='LanLobbyRoot/Room/RoomCard_0/OccupiedContent/ReadyIcon' },
        [pscustomobject]@{ gate='RoomFull.PrimaryAction.Gray';node='LanLobbyRoot/Room/PrimaryAction' },
        [pscustomobject]@{ gate='RoomReady.PrimaryAction.Cyan';node='LanLobbyRoot/Room/PrimaryAction' },
        [pscustomobject]@{ gate='RoomReady.Leave';node='LanLobbyRoot/Room/LeaveAction' }
    )
    foreach ($association in $roomGateMaterialAssociations)
    {
        $associatedGate = @($report.roomGates | Where-Object name -ceq $association.gate)[0]
        Assert-True (@($associatedGate.materialEvidence.rows | Where-Object node -ceq $association.node).Count -eq 1) "$($association.gate) must resolve ROI-associated provenance row $($association.node)"
    }
    foreach ($semanticGate in @($report.roomGates | Where-Object { $_.maskKind -in @('manifest-text-absence','structured-host-profile-absence','derived-visible-contour-spacing') }))
    {
        Assert-True ($semanticGate.materialEvidence.associationKind -eq 'explicit-no-bitmap') "$($semanticGate.name) must explicitly declare no direct bitmap association"
        Assert-True (@($semanticGate.materialEvidence.rows).Count -eq 0) "$($semanticGate.name) must not inherit an undifferentiated capture-wide row array"
    }
    foreach ($excludedName in @('RoomFull.Slot4.ReferencePopupExclusion','RoomReady.Slot4.ReferencePopupExclusion'))
    {
        $excluded = @($report.roomGates | Where-Object name -ceq $excludedName)
        Assert-True ($excluded.Count -eq 1) "$excludedName must be emitted"
        Assert-True (($excluded[0].status -ceq 'ExcludedByReferencePopup') -and ($excluded[0].passed -eq $false)) "$excludedName must be excluded, never passed"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$excluded[0].reason)) "$excludedName exclusion reason"
    }
    $diagnosticOnlyGate = @($report.roomGates | Where-Object name -eq 'RoomHost.Slot1.ReadyTopBar')[0]
    Assert-True (($diagnosticOnlyGate.diagnosticRectTransform.role -eq 'diagnostic-only') -and
        ($diagnosticOnlyGate.diagnosticRectTransform.x -eq 100) -and
        ($diagnosticOnlyGate.actualVisibleBounds.x -ne $diagnosticOnlyGate.diagnosticRectTransform.x) -and
        $diagnosticOnlyGate.passed) 'transparent-padding/diagnostic RectTransform mismatch alone must not fail matching rendered pixels'
    Assert-True (@($report.roomExclusions | Where-Object { -not $_.protectedRegionsClear }).Count -eq 0) 'barrage/popup/fourth-slot exclusions must not overlap protected gates'
    $baselineRoomFailures = @($report.roomGates | Where-Object { $_.status -eq 'Failed' } | ForEach-Object { "$($_.name):$($_.reason):j=$($_.contour.jaccard):edges=$($_.edgeDeltaPx.left)/$($_.edgeDeltaPx.top)/$($_.edgeDeltaPx.right)/$($_.edgeDeltaPx.bottom)" })
    Assert-True ($baselineRoomFailures.Count -eq 0) "baseline room visible-pixel gates must pass; failed: $($baselineRoomFailures -join ' | ')"
    foreach ($profileGate in @($report.roomGates | Where-Object name -like '*.Slot1.ProfileContentAbsence'))
    {
        Assert-True ($profileGate.maskKind -ceq 'structured-host-profile-absence') "$($profileGate.name) must not pixel-compare excluded reference profile artwork"
        Assert-True (@($profileGate.exclusions | Where-Object name -ceq 'reference-profile-art').Count -eq 1) "$($profileGate.name) must explicitly declare the reference profile-art exclusion"
        Assert-True (($profileGate.status -ceq 'Passed') -and $profileGate.structuredAbsencePassed) "$($profileGate.name) must pass from the exact host-slot whitelist"
        Assert-True ((@($profileGate.thresholds.allowedHostSprites).Count -eq 6) -and (@($profileGate.thresholds.allowedHostText).Count -eq 1)) "$($profileGate.name) must publish the exact allowed host Sprite/text whitelist"
    }
    foreach ($gate in @($report.roomGates | Where-Object { $_.name -like '*.ReadyCheck' -or $_.name -like '*.ReadyLabel' }))
    {
        Assert-True ($gate.maskKind -ceq 'DarkOnCyan') "$($gate.name) must measure dark foreground only when locally supported by the ready-cyan lower panel"
        Assert-True (([int]$gate.roi.y -ge 645) -and ([int]$gate.roi.y -lt 665)) "$($gate.name) ROI must cover the authoritative y=660..700 visible target"
    }
    foreach ($gate in @($report.roomGates | Where-Object name -like '*.CreatorTag'))
    {
        Assert-True ($gate.maskKind -ceq 'CreatorTagCyan') "$($gate.name) must measure the solid creator-tag cyan rather than unrelated light pixels"
        Assert-True (([int]$gate.roi.y -eq 225) -and ([int]$gate.roi.height -eq 50)) "$($gate.name) ROI must isolate the top creator tag"
    }
    Assert-True ((@($report.roomGates | Where-Object { $_.name -like 'RoomFull.PrimaryAction.*Center' -and $_.maskKind -ceq 'MutedLight' })).Count -eq 2) 'RoomFull primary icon and label must use the muted-light foreground detector'
    Assert-True ((@($report.roomGates | Where-Object { $_.name -like 'RoomReady.PrimaryAction.*Center' -and $_.maskKind -ceq 'DarkOnCyan' })).Count -eq 2) 'RoomReady primary icon and label must use the dark-on-cyan foreground detector'
    $baselineLeaveGate = @($report.roomGates | Where-Object name -ceq 'RoomReady.Leave')[0]
    Assert-True (($baselineLeaveGate.maskKind -ceq 'Light') -and ([int]$baselineLeaveGate.roi.width -eq 75)) 'RoomReady Leave must retain the light mask in an arrow-only ROI'
    Assert-True (@($report.roomGates | Where-Object { $_.status -eq 'Passed' -and -not $_.materialEvidence.passed }).Count -eq 0) 'a room gate may not pass material evidence with SHA/occurrence mismatch'
    foreach ($wrongFigure in @($figure12,$figure13))
    {
        $wrongRouteReferences = Join-Path $scratch ("room-host-routed-to-" + [IO.Path]::GetFileNameWithoutExtension($wrongFigure))
        Copy-Item -LiteralPath $referenceDirectory -Destination $wrongRouteReferences -Recurse
        Copy-Item -LiteralPath (Join-Path $referenceDirectory $wrongFigure) -Destination (Join-Path $wrongRouteReferences $figure11) -Force
        $script:fixtureCount++
        $wrongRouteOutput = Join-Path $scratch ("room-host-wrong-route-output-" + [IO.Path]::GetFileNameWithoutExtension($wrongFigure))
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $wrongRouteOutput -ReferenceDirectory $wrongRouteReferences | Out-Null
        $wrongRouteReport = Get-Content -Raw -LiteralPath (Join-Path $wrongRouteOutput 'visual-diff-report.json') | ConvertFrom-Json
        Assert-True (@($wrongRouteReport.roomGates | Where-Object { $_.name -like 'RoomHost.*' -and $_.status -eq 'Failed' }).Count -gt 0) "room-host must not pass when the exact Figure 11 filename contains $wrongFigure pixels"
    }
    foreach ($visibleGate in @($report.roomGates | Where-Object { $_.maskKind -in @('Cyan','Gray','Dark','DarkOnCyan','Light','Contrast','CreatorTagCyan','MutedLight') -and $null -ne $_.actualVisibleBounds }))
    {
        Assert-True ($null -ne $visibleGate.actualVisibleBounds) "$($visibleGate.name) actual visible bounds"
        Assert-True ($null -ne $visibleGate.referenceVisibleBounds) "$($visibleGate.name) reference visible bounds"
        Assert-True ($null -ne $visibleGate.actualVisibleCenter) "$($visibleGate.name) actual visible center"
        Assert-True ($null -ne $visibleGate.referenceVisibleCenter) "$($visibleGate.name) reference visible center"
        Assert-True ([string]$visibleGate.maskDescription -like '*color/contrast*') "$($visibleGate.name) must describe opaque screenshots as color/contrast masks"
    }
    $joinDecoration = $report.joinDecoration
    Assert-True ($null -ne $joinDecoration) 'Join decoration report must exist'
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
    Assert-True ($joinAction.passed -eq $true) 'accepted home-join-action report must remain passing'
    Assert-True ($joinDecoration.name -eq 'home-join-decoration') 'Join decoration report name'
    Assert-True (($joinDecoration.actualRect.x -eq 1154) -and ($joinDecoration.actualRect.y -eq 596) -and ($joinDecoration.actualRect.width -eq 717) -and ($joinDecoration.actualRect.height -eq 280)) 'Join decoration crop'
    Assert-True ($joinDecoration.simulationInviteAbsent -eq $true) 'SimulationInvite must be absent from Join evidence'
    Assert-True (($joinDecoration.backingRect.x -eq 1154) -and ($joinDecoration.backingRect.y -eq 596) -and ($joinDecoration.backingRect.width -eq 717) -and ($joinDecoration.backingRect.height -eq 280)) 'Join backing must use its exact screen-top-left geometry'
    Assert-True (($joinDecoration.backingTargetTolerancePx -eq 1) -and $joinDecoration.backingTargetPassed) 'Join backing target must be a blocking <=1 px acceptance gate'
    Assert-True ($joinDecoration.backingBottomScreenY -eq 876) 'Join backing bottom must meet the accepted Join action'
    Assert-True ($joinDecoration.geometryCrossesBackingBottom -eq $false) 'Join decoration geometry must not cross the backing bottom'
    Assert-True ($joinDecoration.graphicsOrGeometryBoundaryAvailable -and ($joinDecoration.graphicsOrGeometryCrossesActionBoundary -eq $false)) 'every Join bitmap Graphic and geometry must remain above fixed screen y=876'
    Assert-True ($joinDecoration.requiredSpriteInventoryPassed -eq $true) 'required Join Sprite inventory must be a passing acceptance gate for the valid fixture'
    Assert-True ($joinDecoration.joinActionPassed -eq $true) 'Join decoration must retain the accepted Join action/content gate'
    Assert-True (@($joinDecoration.components).Count -eq $joinDecorationBounds.Count) 'seven Join decoration visible-bound rows'
    $cycle2DetectorNames = @('logo', 'text-02', 'block-bank', 'input')
    $cycle2Unavailable = @(
        $joinDecoration.components |
            Where-Object { $_.name -in $cycle2DetectorNames -and -not $_.measurementAvailable } |
            ForEach-Object { $_.name }
    )
    Assert-True ($cycle2Unavailable.Count -eq 0) "fixed-reference/Cycle-2 detector fixtures must all be measurable; unavailable: $($cycle2Unavailable -join ', ')"
    foreach ($expectedJoinBound in $joinDecorationBounds)
    {
        $component = @($joinDecoration.components | Where-Object name -eq $expectedJoinBound.name)
        Assert-True ($component.Count -eq 1) "Join decoration/$($expectedJoinBound.name) visible bounds must occur once"
        $component = $component[0]
        Assert-True (($component.expectedBounds.x -eq $expectedJoinBound.x) -and ($component.expectedBounds.y -eq $expectedJoinBound.y) -and ($component.expectedBounds.width -eq $expectedJoinBound.width) -and ($component.expectedBounds.height -eq $expectedJoinBound.height)) "Join decoration/$($expectedJoinBound.name) expected bounds"
        Assert-True ($component.tolerancePx -eq $expectedJoinBound.tolerance) "Join decoration/$($expectedJoinBound.name) tolerance"
        Assert-True ($component.measurementAvailable -eq $true) "Join decoration/$($expectedJoinBound.name) decoded-pixel measurement must be available"
    }
    $referenceSelfConsistencyFailures = @(
        foreach ($component in $joinDecoration.components)
        {
            $reference = $component.referenceBounds
            $expected = $component.expectedBounds
            $referenceCenterX = $reference.x + ($reference.width - 1) / 2.0
            $referenceCenterY = $reference.y + ($reference.height - 1) / 2.0
            $expectedCenterX = $expected.x + ($expected.width - 1) / 2.0
            $expectedCenterY = $expected.y + ($expected.height - 1) / 2.0
            if ([Math]::Abs($referenceCenterX - $expectedCenterX) -gt $component.tolerancePx -or
                [Math]::Abs($referenceCenterY - $expectedCenterY) -gt $component.tolerancePx -or
                [Math]::Abs($reference.width - $expected.width) -gt $component.tolerancePx -or
                [Math]::Abs($reference.height - $expected.height) -gt $component.tolerancePx)
            {
                $component.name
            }
        }
    )
    Assert-True ($referenceSelfConsistencyFailures.Count -eq 0) "fixed Join reference must be self-consistent with all seven approved targets; failed: $($referenceSelfConsistencyFailures -join ', ')"
    $joinLogo = @($joinDecoration.components | Where-Object name -eq 'logo')[0]
    Assert-True (($joinLogo.thresholds.minimumRed -eq 80) -and ($joinLogo.thresholds.minimumGreen -eq 5) -and ($joinLogo.thresholds.maximumBlue -eq 100) -and ($joinLogo.thresholds.minimumRedOverGreen -eq 15)) 'Join logo detector thresholds must include fixed-reference edge pixels'
    $joinText02 = @($joinDecoration.components | Where-Object name -eq 'text-02')[0]
    Assert-True (($joinText02.thresholds.minimumRed -eq 80) -and ($joinText02.thresholds.minimumGreen -eq 5) -and ($joinText02.thresholds.maximumBlue -eq 100) -and ($joinText02.thresholds.minimumRedOverGreen -eq 15)) 'Join text-02 detector thresholds must include fixed-reference edge pixels'
    $joinText01 = @($joinDecoration.components | Where-Object name -eq 'text-01')[0]
    Assert-True (($joinText01.referenceRawBounds.x -eq 393) -and ($joinText01.referenceRawBounds.y -eq 58) -and ($joinText01.referenceRawBounds.width -eq 61) -and ($joinText01.referenceRawBounds.height -eq 3)) 'Join text-01 reference raw decoded bounds'
    Assert-True (($joinText01.boundsAdjustment.x -eq -2) -and ($joinText01.boundsAdjustment.y -eq -2) -and ($joinText01.boundsAdjustment.width -eq 4) -and ($joinText01.boundsAdjustment.height -eq 5)) 'Join text-01 explicit symmetric bounds adjustment'
    Assert-True ((($joinText01.actualRawBounds.x - $joinText01.referenceRawBounds.x) -eq 5) -and (($joinText01.actualBounds.x - $joinText01.referenceBounds.x) -eq 5)) 'Join text-01 calibration must preserve the full decoded +5 px shift'
    $joinTriangle = @($joinDecoration.components | Where-Object name -eq 'triangle')[0]
    Assert-True (($joinTriangle.referenceRawBounds.x -eq 342) -and ($joinTriangle.referenceRawBounds.y -eq 49) -and ($joinTriangle.referenceRawBounds.width -eq 24) -and ($joinTriangle.referenceRawBounds.height -eq 12)) 'Join triangle reference raw decoded bounds'
    Assert-True (($joinTriangle.boundsAdjustment.x -eq -4) -and ($joinTriangle.boundsAdjustment.y -eq -2) -and ($joinTriangle.boundsAdjustment.width -eq 6) -and ($joinTriangle.boundsAdjustment.height -eq 5)) 'Join triangle explicit symmetric bounds adjustment'
    $joinCentralBlank = @($joinDecoration.components | Where-Object name -eq 'central-blank')[0]
    Assert-True ($joinCentralBlank.measurement -eq 'orange-component-union') 'Join central blank must use decoded component union'
    Assert-True (($joinCentralBlank.thresholds.minimumComponentPixelCount -eq 50) -and ($joinCentralBlank.thresholds.maximumComponentWidth -eq 35) -and ($joinCentralBlank.thresholds.maximumComponentHeight -eq 35)) 'Join central blank component-selection thresholds'
    Assert-True (($joinCentralBlank.referenceRawBounds.x -eq 323) -and ($joinCentralBlank.referenceRawBounds.y -eq 72) -and ($joinCentralBlank.referenceRawBounds.width -eq 59) -and ($joinCentralBlank.referenceRawBounds.height -eq 56)) 'Join central blank reference raw decoded bounds'
    Assert-True (($joinCentralBlank.actualRawBounds.x -eq 324) -and ($joinCentralBlank.actualRawBounds.y -eq 69) -and ($joinCentralBlank.actualRawBounds.width -eq 58) -and ($joinCentralBlank.actualRawBounds.height -eq 58)) 'Join central blank actual guide-free decoded bounds'
    Assert-True (($joinCentralBlank.boundsAdjustment.x -eq 0) -and ($joinCentralBlank.boundsAdjustment.y -eq -4) -and ($joinCentralBlank.boundsAdjustment.width -eq 1) -and ($joinCentralBlank.boundsAdjustment.height -eq 5)) 'Join central blank explicit symmetric bounds adjustment'
    foreach ($calibratedComponent in @($joinText01, $joinTriangle, $joinCentralBlank))
    {
        Assert-True (($calibratedComponent.boundsAdjustment.coordinateOrigin -eq 'crop-top-left') -and ($calibratedComponent.boundsAdjustment.unit -eq 'px')) "Join $($calibratedComponent.name) adjustment coordinate schema"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$calibratedComponent.boundsAdjustment.reason)) "Join $($calibratedComponent.name) adjustment reason"
    }
    $joinBlockBank = @($joinDecoration.components | Where-Object name -eq 'block-bank')[0]
    Assert-True ($joinBlockBank.measurement -eq 'edge-component-union') 'Join block bank must use decoded edge-component union'
    Assert-True (($joinBlockBank.thresholds.minimumChannelDifference -eq 5) -and ($joinBlockBank.thresholds.minimumComponentPixelCount -eq 5) -and $joinBlockBank.thresholds.excludeSearchBorderComponents) 'Join block-bank edge-component thresholds'
    Assert-True (($joinBlockBank.referenceRawBounds.x -eq 54) -and ($joinBlockBank.referenceRawBounds.y -eq 107) -and ($joinBlockBank.referenceRawBounds.width -eq 640) -and ($joinBlockBank.referenceRawBounds.height -eq 89)) 'Join block-bank reference raw decoded bounds'
    Assert-True (($joinBlockBank.actualRawBounds.x -eq 54) -and ($joinBlockBank.actualRawBounds.y -eq 107) -and ($joinBlockBank.actualRawBounds.width -eq 640) -and ($joinBlockBank.actualRawBounds.height -eq 89)) 'Join block-bank actual raw decoded bounds'
    Assert-True (($joinBlockBank.boundsAdjustment.coordinateOrigin -eq 'crop-top-left') -and ($joinBlockBank.boundsAdjustment.unit -eq 'px')) 'Join block-bank adjustment coordinate schema'
    Assert-True (($joinBlockBank.boundsAdjustment.x -eq -9) -and ($joinBlockBank.boundsAdjustment.y -eq 0) -and ($joinBlockBank.boundsAdjustment.width -eq -1) -and ($joinBlockBank.boundsAdjustment.height -eq 0)) 'Join block-bank explicit asset-specific bounds adjustment'
    Assert-True ([string]$joinBlockBank.boundsAdjustment.reason -like '*near-background/transparent*') 'Join block-bank adjustment reason'
    Assert-True (($joinBlockBank.referenceBounds.x -eq 45) -and ($joinBlockBank.referenceBounds.y -eq 107) -and ($joinBlockBank.referenceBounds.width -eq 639) -and ($joinBlockBank.referenceBounds.height -eq 89)) 'Join block-bank adjusted reference bounds'
    Assert-True (($joinBlockBank.actualBounds.x -eq 45) -and ($joinBlockBank.actualBounds.y -eq 107) -and ($joinBlockBank.actualBounds.width -eq 639) -and ($joinBlockBank.actualBounds.height -eq 89)) 'Join block-bank adjusted actual bounds'
    $blockTopology = $joinBlockBank.internalTopology
    Assert-True ($null -ne $blockTopology) 'Join block-bank must publish nested internal orange topology without adding an eighth component row'
    Assert-True (($blockTopology.measurement -eq 'orange-column-occupancy-profile') -and ($blockTopology.acceptanceRole -eq 'blocking')) 'Join block-bank internal topology contract'
    Assert-True (($blockTopology.search.x -eq 35) -and ($blockTopology.search.y -eq 107) -and ($blockTopology.search.width -eq 660) -and ($blockTopology.search.height -eq 89)) 'Join block-bank internal topology search ROI'
    Assert-True (($blockTopology.thresholds.minimumRed -eq 100) -and ($blockTopology.thresholds.minimumRedOverGreen -eq 15) -and ($blockTopology.thresholds.maximumBlue -eq 130) -and ($blockTopology.thresholds.minimumQualifyingPixelsPerColumn -eq 3)) 'Join block-bank internal orange occupancy thresholds'
    Assert-True (($blockTopology.acceptance.maximumSpanEdgeDeviationPx -eq 4) -and ($blockTopology.acceptance.maximumOccupiedColumnCountDelta -eq 20) -and ($blockTopology.acceptance.minimumProfileJaccard -eq 0.95)) 'Join block-bank internal topology acceptance thresholds'
    Assert-True ($blockTopology.measurementAvailable -and $blockTopology.referenceSelfPassed -and $blockTopology.passed) 'valid Join block-bank topology must be measurable, reference-self-consistent, and passing'
    Assert-True (($blockTopology.referenceRaw.span.startX -eq 150) -and ($blockTopology.referenceRaw.span.endXInclusive -eq 587) -and ($blockTopology.referenceRaw.occupiedColumnCount -eq 436)) 'Join block-bank reference orange span/profile'
    Assert-True (($blockTopology.actualRaw.span.startX -eq 150) -and ($blockTopology.actualRaw.span.endXInclusive -eq 587) -and ($blockTopology.actualRaw.occupiedColumnCount -eq 436)) 'Join block-bank actual orange span/profile'
    Assert-True ((@($blockTopology.referenceRaw.runs).Count -eq 2) -and (@($blockTopology.actualRaw.runs).Count -eq 2)) 'valid Join block-bank topology must preserve both occupied-column runs'
    Assert-True (($blockTopology.comparison.profileJaccard -eq 1) -and ($blockTopology.comparison.occupiedColumnCountDelta -eq 0) -and ($blockTopology.comparison.spanStartDeltaPx -eq 0) -and ($blockTopology.comparison.spanEndDeltaPx -eq 0)) 'valid Join block-bank topology profile comparison'
    Assert-True ($joinBlockBank.boundsPassed -and $joinBlockBank.passed) 'valid Join block-bank outer bounds and internal topology must both pass'
    $joinInput = @($joinDecoration.components | Where-Object name -eq 'input')[0]
    Assert-True ($joinInput.measurement -eq 'neutral-largest-component') 'Join input must measure the largest decoded neutral panel'
    Assert-True (($joinInput.thresholds.minimumLuminanceInclusive -eq 40) -and ($joinInput.thresholds.maximumLuminanceInclusive -eq 140) -and ($joinInput.thresholds.maximumChannelSpread -eq 5)) 'Join input panel thresholds'
    Assert-True ($joinText01.passed -eq $false) 'deliberately displaced Join text-01 must fail its named row'
    $unexpectedJoinFailures = @($joinDecoration.components | Where-Object { $_.name -ne 'text-01' -and -not $_.passed })
    Assert-True ($unexpectedJoinFailures.Count -eq 0) "all other Join decoration rows must pass; failed: $(@($unexpectedJoinFailures | ForEach-Object { $_.name }) -join ', ')"
    Assert-True ($joinDecoration.passed -eq $false) 'Join decoration overall state must be false for the deliberate text-01 offset'
    $decoration = $report.createDecoration
    Assert-True ($decoration.name -eq 'home-create-decoration') 'Create decoration report name'
    Assert-True (($decoration.actualRect.x -eq 1296) -and ($decoration.actualRect.y -eq 252) -and ($decoration.actualRect.width -eq 390) -and ($decoration.actualRect.height -eq 179)) 'Create decoration crop'
    Assert-True ($decoration.acceptanceRole -eq 'informational') 'Create decoration diagnostics must be informational'
    Assert-True ($decoration.blocksCreateFrameAcceptance -eq $false) 'Create decoration diagnostics must not block Create frame acceptance'
    Assert-True (@($decoration.components).Count -eq 10) 'ten Create decoration diagnostics'
    Assert-True (@($decoration.components | Where-Object { $_.name -like 'wing-*' }).Count -eq 0) 'Create decoration diagnostics must contain no wing measurement rows'
    foreach ($expectedDecoration in $createDecorationBounds)
    {
        $component = @($decoration.components | Where-Object name -eq $expectedDecoration.name)
        Assert-True ($component.Count -eq 1) "Create decoration/$($expectedDecoration.name) visible bounds must occur once"
        $component = $component[0]
        $expectedBounds = $expectedDecoration.expected
        Assert-True (($component.measurementMode -eq $expectedDecoration.mode)) "Create decoration/$($expectedDecoration.name) measurement mode"
        Assert-True (($component.expectedBounds.x -eq $expectedBounds.x) -and ($component.expectedBounds.y -eq $expectedBounds.y) -and ($component.expectedBounds.width -eq $expectedBounds.width) -and ($component.expectedBounds.height -eq $expectedBounds.height)) "Create decoration/$($expectedDecoration.name) expected bounds"
        Assert-True (($component.referenceBounds.x -eq $expectedBounds.x) -and ($component.referenceBounds.y -eq $expectedBounds.y) -and ($component.referenceBounds.width -eq $expectedBounds.width) -and ($component.referenceBounds.height -eq $expectedBounds.height)) "Create decoration/$($expectedDecoration.name) reference bounds; actual=$($component.referenceBounds.x),$($component.referenceBounds.y),$($component.referenceBounds.width),$($component.referenceBounds.height)"
        Assert-True (($component.thresholdMinimumGreen -eq $expectedDecoration.threshold) -and ($component.minimumGreenOverRed -eq $expectedDecoration.greenOverRed) -and ($component.minimumBlueOverRed -eq $expectedDecoration.blueOverRed)) "Create decoration/$($expectedDecoration.name) cyan thresholds"
    }
    $missingDiagnostic = @($decoration.components | Where-Object name -eq 'text-02')[0]
    Assert-True ($missingDiagnostic.measurementAvailable -eq $false) 'missing central diagnostic must be recorded as unavailable'
    Assert-True (-not [string]::IsNullOrWhiteSpace([string]$missingDiagnostic.measurementError)) 'missing central diagnostic must record its measurement error'
    Assert-True (($null -eq $missingDiagnostic.actualBounds) -and ($null -eq $missingDiagnostic.centerDeviationPx) -and ($null -eq $missingDiagnostic.sizeDeviationPx)) 'missing central diagnostic must publish null measurements'
    Assert-True ($missingDiagnostic.passed -eq $false) 'missing central diagnostic must fail informationally'
    $unexpectedDecorationFailures = @($decoration.components | Where-Object { $_.name -ne 'text-02' -and -not $_.passed })
    Assert-True ($unexpectedDecorationFailures.Count -eq 0) "all measured Create decoration diagnostics must pass; failed: $(@($unexpectedDecorationFailures | ForEach-Object { $_.name }) -join ', ')"
    $frame = $report.createFrame
    Assert-True ($frame.name -eq 'home-create-frame') 'Create frame report name'
    Assert-True (($frame.actualRect.x -eq 1154) -and ($frame.actualRect.y -eq 224) -and ($frame.actualRect.width -eq 717) -and ($frame.actualRect.height -eq 374)) 'Create frame crop'
    Assert-True (@($frame.edges).Count -eq 5) 'five Create frame edge records'
    foreach ($expectedEdge in $createFrameEdges)
    {
        $edge = @($frame.edges | Where-Object name -eq $expectedEdge.name)
        Assert-True ($edge.Count -eq 1) "Create frame/$($expectedEdge.name) must occur once"
        $edge = $edge[0]
        Assert-True ($edge.axis -eq $expectedEdge.axis) "Create frame/$($expectedEdge.name) axis"
        Assert-True (($edge.backgroundSearch.x -eq $expectedEdge.background.x) -and ($edge.backgroundSearch.y -eq $expectedEdge.background.y) -and ($edge.backgroundSearch.width -eq $expectedEdge.background.width) -and ($edge.backgroundSearch.height -eq $expectedEdge.background.height)) "Create frame/$($expectedEdge.name) background search"
        Assert-True (($edge.thresholdMinimumGreen -eq 12) -and ($edge.minimumGreenOverRed -eq 3) -and ($edge.minimumBlueOverRed -eq 2)) "Create frame/$($expectedEdge.name) cyan thresholds"
        Assert-True ($edge.minimumContrast -eq 18) "Create frame/$($expectedEdge.name) minimum contrast"
        Assert-True (($edge.frameSampleCount -gt 0) -and ($edge.backgroundSampleCount -gt 0) -and $edge.contrastAvailable) "Create frame/$($expectedEdge.name) contrast samples"
        Assert-True ([string]::IsNullOrEmpty([string]$edge.contrastFailureReason)) "Create frame/$($expectedEdge.name) contrast failure reason must be empty when available"
        if ($expectedEdge.Contains('minimumPixelCount'))
        {
            Assert-True (($edge.minimumPixelCount -eq 80) -and ($edge.qualifyingPixelCount -ge 80)) 'Create frame joint pixel count'
        }
        else
        {
            Assert-True (($edge.minimumCoverage -eq .90) -and ($edge.maximumGap -eq 6)) "Create frame/$($expectedEdge.name) continuity limits"
        }
    }
    $topEdge = @($frame.edges | Where-Object name -eq 'top')[0]
    Assert-True (($topEdge.largestGapPixels -eq 8) -and ($topEdge.continuityPassed -eq $false) -and $topEdge.contrastPassed -and ($topEdge.passed -eq $false)) 'Create frame top fixture must fail only continuity with an 8 px gap'
    Assert-True (@($frame.edges | Where-Object name -eq 'bottom').Count -eq 0) 'Create open frame must not report a synthetic bottom edge'
    $leftEdge = @($frame.edges | Where-Object name -eq 'left')[0]
    Assert-True ($leftEdge.continuityPassed -and ($leftEdge.contrastPassed -eq $false) -and ($leftEdge.passed -eq $false)) 'Create frame left fixture must fail only brightness contrast'
    Assert-True ($leftEdge.frameMedianLuma -lt $leftEdge.backgroundMedianLuma) 'Create frame left fixture must use a dim qualifying cyan'
    $unexpectedFrameFailures = @($frame.edges | Where-Object { $_.name -notin @('top','left') -and -not $_.passed })
    Assert-True ($unexpectedFrameFailures.Count -eq 0) "right and both joints Create frame rows must pass; failed: $(@($unexpectedFrameFailures | ForEach-Object { $_.name }) -join ', ')"
    Assert-True (($frame.bottomBoundary.kind -eq 'action-bar') -and ($frame.bottomBoundary.action -eq 'home-create-action') -and ($frame.bottomBoundary.visibleTopScreenY -eq 460)) 'Create lower boundary must identify the visible Create action bar'
    Assert-True ($frame.bottomBoundary.passed -eq $false) 'deliberately displaced Create action must fail the lower-boundary contract'
    Assert-True (-not $frame.passed) 'Create frame overall state must reflect top, left, and lower-boundary failures'
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
    Assert-True (($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'join_icon').occurrenceCount -eq 2) 'Join icon occurrence count must sum one JoinAction instance in each Home capture'
    $joinSpriteCounts = @{
        room_select_join_left_block = 4
        room_select_join_middle_block = 8
        room_select_join_right_block = 4
        room_select_join_middle_block_mask = 2
        room_select_join_blank = 2
        room_select_join_ban = 8
        room_select_join_triangle = 2
        room_select_join_logo = 2
        room_select_join_text_01 = 2
        room_select_join_text_02 = 2
        room_select_join_text_bg = 2
    }
    foreach ($spriteName in $joinSpriteCounts.Keys)
    {
        $spriteUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq $spriteName)
        Assert-True ($spriteUsage.Count -eq 1) "Join Sprite $spriteName must remain separately audited"
        Assert-True ($spriteUsage[0].occurrenceCount -eq $joinSpriteCounts[$spriteName]) "Join Sprite $spriteName must preserve its repeated occurrence count"
        Assert-True ($spriteUsage[0].sourcePath -eq ('[uc]autochessouter/' + $spriteName + '.png')) "Join Sprite $spriteName must preserve its approved source path"
    }
    Assert-True (@($report.materialUsage.bitmapSprites | Where-Object { $_.sourcePath -match '\$0|#0' }).Count -eq 0) 'no Join Sprite source may use forbidden $0/#0 unpacked paths'
    Assert-True (@($report.materialUsage.codeGeneratedGeometry | Where-Object { $_.name -like '*SimulationInvite*' -or $_.name -like '*OutlineBottom*' }).Count -eq 0) 'Join geometry must contain neither SimulationInvite nor OutlineBottom'
    foreach ($geometryName in @('InteriorBacking','OutlineTop','OutlineLeft','OutlineRight','GuideHorizontal','GuideVertical'))
    {
        $geometryUsage = @($report.materialUsage.codeGeneratedGeometry | Where-Object { $_.name -eq ('LanLobbyRoot/Home/RoomSelect/Join/' + $geometryName) })
        Assert-True ($geometryUsage.Count -eq 1) "Join geometry $geometryName must be separately reported"
        Assert-True (($geometryUsage[0].isBitmap -eq $false) -and ($geometryUsage[0].occurrenceCount -eq 2)) "Join geometry $geometryName must be sprite-null in both Home captures"
    }
    $logoUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'room_select_create_logo')
    $wingUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'img_pointer')[0]
    $frameUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'doc_frame_line')[0]
    $lineUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'room_select_create_left_line')[0]
    $dotUsage = @($report.materialUsage.bitmapSprites | Where-Object spriteName -eq 'room_select_dot')[0]
    Assert-True ($logoUsage.Count -eq 0) 'room_select_create_logo material usage must be absent'
    Assert-True ($wingUsage.occurrenceCount -eq 8) 'four img_pointer Sprites in each Home state'
    Assert-True ($frameUsage.occurrenceCount -eq 14) 'seven doc_frame_line Sprites in each Home state'
    Assert-True ($lineUsage.occurrenceCount -eq 4) 'two bracket Sprites in each Home state'
    Assert-True ($dotUsage.occurrenceCount -eq 10) 'four Create dots plus title dot in each Home state'
    Assert-True (@($report.materialUsage.unityText | Where-Object { $_.node -eq 'LanLobbyRoot/Home/RoomSelect/Create/CreateAction/Label' -and $_.text -eq '创建同盟' }).Count -eq 1) 'Create action Unity Text must come from captured manifest data'
    Assert-True (@($report.materialUsage.unityText | Where-Object { $_.node -eq 'LanLobbyRoot/Home/IdentityPanel/Title' -and $_.text -eq 'LOCAL IDENTITY' }).Count -eq 1) 'Home identity text must come from captured manifest data'
    Assert-True (@($report.materialUsage.unityText | Where-Object { $_.node -eq 'LanLobbyRoot/Room/Latency' -and $_.text -eq '18 ms' }).Count -eq 1) 'Room text must come from captured manifest data'
    Assert-True (@($report.materialUsage.codeGeneratedGeometry).Count -gt 0) 'material usage must separately list code-generated geometry'
    Assert-True (@($report.materialUsage.unityText | Where-Object { $_.PSObject.Properties.Name -contains 'sourcePath' }).Count -eq 0) 'Unity Text must not be represented as a Sprite source'
    foreach ($name in @('home-create-action','home-join-action')) { foreach ($kind in @('actual','reference','overlay','heatmap')) { Assert-True (Test-Path -LiteralPath (Join-Path $output ($name + '-' + $kind + '.png'))) "missing $name $kind" } }
    foreach ($kind in @('actual','reference','overlay','heatmap')) { Assert-True (Test-Path -LiteralPath (Join-Path $output ('home-create-decoration-' + $kind + '.png'))) "missing home-create-decoration $kind" }
    foreach ($kind in @('actual','reference','overlay','heatmap')) { Assert-True (Test-Path -LiteralPath (Join-Path $output ('home-create-frame-' + $kind + '.png'))) "missing home-create-frame $kind" }
    foreach ($kind in @('actual','reference','overlay','heatmap')) { Assert-True (Test-Path -LiteralPath (Join-Path $output ('home-join-decoration-' + $kind + '.png'))) "visual failure must still publish home-join-decoration $kind" }
    Assert-True (($report.captures | Measure-Object).Count -eq 5) 'five captures must be reported'
    Assert-True (($report.referenceNormalization -eq 'independent-xy') -and ($homeCapture.actualWidth -eq 1920) -and ($homeCapture.actualHeight -eq 1080) -and ($homeCapture.referenceWidth -eq 2048) -and ($homeCapture.referenceHeight -eq 1118)) 'report must retain native dimensions and independent normalization'
    Assert-True ($homeCapture.maskedPixels -gt 0) 'home must record masked pixels'
    Assert-True (($homeCapture.regions | Where-Object name -eq 'ignored-radar' | Select-Object -ExpandProperty comparedPixels) -eq 0) 'masked radar pixels must not be compared'
    Assert-True (($homeCapture.regions | Where-Object name -eq 'create-room' | Select-Object -ExpandProperty pixelDifferenceRatio) -gt 0) 'create-room difference must be measurable'
    Assert-True (Test-Path (Join-Path $output 'home-heatmap.png')) 'home heatmap must exist'
    Assert-True ((Test-Path (Join-Path $output 'visual-diff-report.md')) -and (Test-Path (Join-Path $output 'manifest.json'))) 'report and copied manifest must exist'
    $markdown = Get-Content -Raw -LiteralPath (Join-Path $output 'visual-diff-report.md')
    Assert-True ($markdown.Contains("${figure9}: 2048×1118")) 'Markdown must derive figure 9 native dimensions from decoded reference pixels'
    foreach ($figure in @($figure11, $figure12, $figure13))
    {
        Assert-True ($markdown.Contains("${figure}: 2560×1440")) "Markdown must derive $figure native dimensions from decoded reference pixels"
    }
    Assert-True ($markdown.Contains('## LAN room named visible-pixel gates')) 'Markdown must expose named LAN room visible-pixel gates'
    Assert-True ($markdown.Contains('Full-screen capture metrics are informational')) 'Markdown must identify full-screen metrics as informational'
    Assert-True ($markdown.Contains('named room gates and material provenance are blocking')) 'Markdown must identify room/material gates as blocking'
    foreach ($column in @('Exclusions','Actual/reference centers','Actual/reference pixels','Associated provenance'))
    {
        Assert-True ($markdown.Contains($column)) "Markdown human gate table missing $column"
    }
    foreach ($gateName in $requiredRoomGates) { Assert-True ($markdown.Contains($gateName)) "Markdown missing named room gate $gateName" }
    Assert-True ($markdown.Contains('ExcludedByReferencePopup')) 'Markdown must preserve excluded fourth-slot status'
    $markdownLines = @($markdown -split "`r?`n")
    $jsonlBegin = [Array]::IndexOf($markdownLines, '<!-- ROOM_GATE_JSONL_BEGIN -->')
    $jsonlEnd = [Array]::IndexOf($markdownLines, '<!-- ROOM_GATE_JSONL_END -->')
    Assert-True ($jsonlBegin -ge 0 -and $jsonlEnd -gt $jsonlBegin) 'Markdown must contain a bounded lossless room-gate JSONL appendix'
    $jsonlGateLines = @($markdownLines[($jsonlBegin + 2)..($jsonlEnd - 2)] | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-True ($jsonlGateLines.Count -eq @($report.roomGates).Count) 'Markdown JSONL must contain exactly one row per JSON room gate'
    for ($gateIndex = 0; $gateIndex -lt @($report.roomGates).Count; $gateIndex++)
    {
        $jsonGate = $report.roomGates[$gateIndex] | ConvertTo-Json -Depth 20 -Compress
        $markdownGate = ($jsonlGateLines[$gateIndex] | ConvertFrom-Json) | ConvertTo-Json -Depth 20 -Compress
        Assert-True ($markdownGate -ceq $jsonGate) "Markdown JSONL parity for every field/provenance row: $($report.roomGates[$gateIndex].name)"
    }
    Assert-True ($markdown.Contains('Position deviation (px)')) 'Markdown action table must expose position deviation in px'
    Assert-True ($markdown.Contains('1161,449,711,95')) 'Markdown must contain the manifest-derived Create actual Rect'
    Assert-True ($markdown.Contains('dx=7, dy=-4')) 'Markdown must contain the exact manifest-derived Create position delta'
    Assert-True ($markdown.Contains('717x99')) 'Markdown must explicitly list the locally resized reference size'
    Assert-True ($markdown.Contains('711x95')) 'Markdown must explicitly list the comparison reference size'
    Assert-True ($markdown.Contains('dw=-6, dh=-4')) 'Markdown must contain the exact Create size delta'
    Assert-True ($markdown.Contains('## Home action content visible bounds')) 'Markdown must expose action-content visible bounds'
    foreach ($expected in $expectedContent) { Assert-True ($markdown.Contains("$($expected.bar)/$($expected.name)")) "Markdown missing $($expected.bar)/$($expected.name)" }
    Assert-True ($markdown.Contains('## Home Create upper decoration')) 'Markdown must expose Create decoration visible bounds'
    foreach ($expectedDecoration in $createDecorationBounds) { Assert-True ($markdown.Contains($expectedDecoration.name)) "Markdown missing Create decoration/$($expectedDecoration.name)" }
    Assert-True ($markdown.Contains('## Home Create open-frame continuity')) 'Markdown must expose Create open-frame continuity'
    Assert-True ($markdown.Contains('## Home Join decoration')) 'Markdown must expose Join decoration visible bounds'
    foreach ($expectedJoinBound in $joinDecorationBounds) { Assert-True ($markdown.Contains($expectedJoinBound.name)) "Markdown missing Join decoration/$($expectedJoinBound.name)" }
    Assert-True ($markdown.Contains('Reference raw')) 'Markdown must publish raw decoded Join bounds'
    Assert-True ($markdown.Contains('Bounds adjustment')) 'Markdown must publish the explicit Join bounds adjustment'
    Assert-True ($markdown.Contains('Reference adjusted')) 'Markdown must distinguish adjusted Join bounds'
    Assert-True ($markdown.Contains('near-background/transparent left margin')) 'Markdown must publish the block-bank adjustment reason'
    Assert-True ($markdown.Contains('Block-bank internal orange topology')) 'Markdown must publish the nested blocking block-bank topology'
    Assert-True ($markdown.Contains('Profile Jaccard')) 'Markdown must publish the stable internal column-profile metric'
    Assert-True ($markdown.Contains('150..219, 222..587')) 'Markdown must publish the fixed reference orange occupancy runs'
    Assert-True ($markdown.Contains('Search/background')) 'Markdown frame table must expose the background ROI'
    Assert-True ($markdown.Contains('Frame/background median luma')) 'Markdown frame table must expose median luma'
    Assert-True ($markdown.Contains('Contrast delta/minimum')) 'Markdown frame table must expose contrast acceptance'
    Assert-True ($markdown.Contains('Continuity passed')) 'Markdown frame table must expose continuity acceptance'
    Assert-True ($markdown.Contains('Contrast passed')) 'Markdown frame table must expose contrast acceptance result'
    foreach ($expectedEdge in $createFrameEdges) { Assert-True ($markdown.Contains($expectedEdge.name)) "Markdown missing Create frame/$($expectedEdge.name)" }
    Assert-True ($markdown.Contains('home-create-action')) 'Markdown must expose the Create lower-boundary action'
    Assert-True (-not $markdown.Contains('| bottom |')) 'Markdown must not report a synthetic bottom edge row'
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

    $thresholdCaptureDirectory = Join-Path $scratch 'room-visible-threshold-captures'
    Copy-Item -LiteralPath $captureDirectory -Destination $thresholdCaptureDirectory -Recurse
    $thresholdManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $thresholdCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $thresholdManifest.captures) { $record.path = Join-Path $thresholdCaptureDirectory ($record.name + '.png') }
    $thresholdManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $thresholdCaptureDirectory 'manifest.json') -Encoding UTF8
    Mutate-RoomVisibleThresholdFixture $thresholdCaptureDirectory
    $thresholdOutput = Join-Path $scratch 'room-visible-threshold-output'
    & $exportScript -CaptureDirectory $thresholdCaptureDirectory -OutputDirectory $thresholdOutput -ReferenceDirectory $referenceDirectory | Out-Null
    $thresholdReport = Get-Content -Raw -LiteralPath (Join-Path $thresholdOutput 'visual-diff-report.json') | ConvertFrom-Json
    $shiftedCheck = @($thresholdReport.roomGates | Where-Object name -eq 'RoomHost.Slot1.ReadyCheck')[0]
    Assert-True (($shiftedCheck.thresholds.maximumCenterErrorPxPerAxis -eq 2) -and ([Math]::Abs([double]$shiftedCheck.centerDeltaPx.deltaX) -eq 3) -and ($shiftedCheck.status -eq 'Failed')) 'a 3 px rendered check shift must fail the 2 px center gate'
    $widenedLabel = @($thresholdReport.roomGates | Where-Object name -eq 'RoomHost.Slot1.ReadyLabel')[0]
    Assert-True (($widenedLabel.thresholds.maximumVisibleSizeErrorPx -eq 3) -and ([Math]::Abs([double]$widenedLabel.sizeDeltaPx.deltaWidth) -eq 4) -and ($widenedLabel.status -eq 'Failed')) 'a 4 px rendered-label width change must fail the 3 px size gate'
    $shiftedContour = @($thresholdReport.roomGates | Where-Object name -eq 'RoomFull.Slot2.WaitingContour')[0]
    Assert-True (($shiftedContour.thresholds.maximumEdgeErrorPx -eq 4) -and ([Math]::Abs([double]$shiftedContour.edgeDeltaPx.left) -eq 5) -and ($shiftedContour.status -eq 'Failed')) 'a 5 px visible contour-edge shift must fail'
    $lowJaccard = @($thresholdReport.roomGates | Where-Object name -eq 'RoomReady.Slot3.ReadyContour')[0]
    Assert-True (($lowJaccard.thresholds.minimumContourJaccard -eq 0.95) -and ([double]$lowJaccard.contour.jaccard -lt 0.95) -and ($lowJaccard.status -eq 'Failed')) 'contour Jaccard below 0.95 must fail'
    $unchangedRectGate = @($thresholdReport.roomGates | Where-Object name -eq 'RoomHost.Slot1.ReadyTopBar')[0]
    Assert-True ($unchangedRectGate.status -eq 'Passed') 'diagnostic RectTransform data must not fail unchanged rendered visible pixels'
    Assert-True (($shiftedCheck.diagnosticRectTransform.x -eq $unchangedRectGate.diagnosticRectTransform.x) -and ($shiftedCheck.status -eq 'Failed')) 'visible-pixel movement with unchanged diagnostic RectTransform must fail'

    $detectorColorResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'room-detector-color-contracts' {
        param($caseManifest,$caseCaptureDirectory)
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-host.png') ([Drawing.Color]::FromArgb(255,0,220,220)) 300 645 65 70
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-host.png') ([Drawing.Color]::FromArgb(255,0,220,220)) 355 645 125 70
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-host.png') ([Drawing.Color]::FromArgb(255,105,105,105)) 315 225 145 50
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-full.png') ([Drawing.Color]::FromArgb(255,105,105,105)) 1555 955 85 70
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-full.png') ([Drawing.Color]::FromArgb(255,105,105,105)) 1640 955 175 75
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-ready.png') ([Drawing.Color]::FromArgb(255,0,220,220)) 1560 955 85 75
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-ready.png') ([Drawing.Color]::FromArgb(255,0,220,220)) 1645 955 150 75
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-ready.png') ([Drawing.Color]::Black) 45 25 75 65
    }
    foreach ($gateName in @(
        'RoomHost.Slot1.ReadyCheck',
        'RoomHost.Slot1.ReadyLabel',
        'RoomHost.Slot1.CreatorTag',
        'RoomFull.PrimaryAction.IconCenter',
        'RoomFull.PrimaryAction.LabelCenter',
        'RoomReady.PrimaryAction.IconCenter',
        'RoomReady.PrimaryAction.LabelCenter',
        'RoomReady.Leave'))
    {
        $detectorGate = @($detectorColorResult.report.roomGates | Where-Object name -ceq $gateName)[0]
        Assert-True (($detectorGate.status -ceq 'Failed') -and -not $detectorGate.passed) "$gateName must reject a same-geometry wrong-foreground-color mutation"
    }

    $unsupportedDarkResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'room-dark-without-cyan-support' {
        param($caseManifest,$caseCaptureDirectory)
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-host.png') ([Drawing.Color]::Black) 294 639 192 82
        Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory 'room-ready.png') ([Drawing.Color]::Black) 1554 949 247 87
    }
    foreach ($gateName in @(
        'RoomHost.Slot1.ReadyCheck',
        'RoomHost.Slot1.ReadyLabel',
        'RoomReady.PrimaryAction.IconCenter',
        'RoomReady.PrimaryAction.LabelCenter'))
    {
        $unsupportedGate = @($unsupportedDarkResult.report.roomGates | Where-Object name -ceq $gateName)[0]
        Assert-True (($unsupportedGate.status -ceq 'Failed') -and -not $unsupportedGate.passed) "$gateName must reject dark pixels without local cyan support"
    }

    $materialCaptureDirectory = Join-Path $scratch 'room-material-mismatch-captures'
    Copy-Item -LiteralPath $captureDirectory -Destination $materialCaptureDirectory -Recurse
    $materialManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $materialCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $materialManifest.captures) { $record.path = Join-Path $materialCaptureDirectory ($record.name + '.png') }
    $materialHost = @($materialManifest.captures | Where-Object name -eq 'room-host')[0]
    $materialHost.sourceAudit[0].sha256 = ('0' * 64)
    $materialHost.sourceAudit[0].occurrenceCount = 2
    $materialManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $materialCaptureDirectory 'manifest.json') -Encoding UTF8
    $materialOutput = Join-Path $scratch 'room-material-mismatch-output'
    & $exportScript -CaptureDirectory $materialCaptureDirectory -OutputDirectory $materialOutput -ReferenceDirectory $referenceDirectory | Out-Null
    $materialReport = Get-Content -Raw -LiteralPath (Join-Path $materialOutput 'visual-diff-report.json') | ConvertFrom-Json
    $materialGate = @($materialReport.roomGates | Where-Object name -eq 'RoomHost.Slot1.ReadyTopBar')[0]
    Assert-True (($materialGate.materialEvidence.shaPassed -eq $false) -and ($materialGate.materialEvidence.occurrencePassed -eq $false)) 'source SHA and rendered occurrence mismatches must both fail material evidence'
    Assert-True (($materialGate.status -eq 'Failed') -and ($materialGate.passed -eq $false)) 'material mismatch must block a visually matching named gate'

    $roomPrefixes = @{
        'room-host'='RoomHost'
        'room-full'='RoomFull'
        'room-ready'='RoomReady'
    }
    foreach ($forbiddenText in @('OPEN SLOT','WAITING'))
    {
        foreach ($captureName in @('room-host','room-full','room-ready'))
        {
            $caseName = 'legacy-' + $captureName + '-' + $forbiddenText.Replace(' ','-').ToLowerInvariant()
            $legacyResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory $caseName {
                param($caseManifest,$caseCaptureDirectory)
                $record = @($caseManifest.captures | Where-Object name -ceq $captureName)[0]
                $record.unityText = @($record.unityText) + [pscustomobject][ordered]@{
                    node="LanLobbyRoot/Room/RoomCard_2/Legacy$($forbiddenText.Replace(' ',''))"
                    text=$forbiddenText
                    fontName='Novecento wide Normal Regular'
                    fontResourcePath=''
                    hasBitmapSource=$false
                    bitmapSourcePath=''
                }
            }
            $literalSuffix = if ($forbiddenText -ceq 'OPEN SLOT') { 'LegacyOpenSlotTextAbsence' } else { 'LegacyWaitingTextAbsence' }
            $legacyGate = @($legacyResult.report.roomGates | Where-Object name -ceq "$($roomPrefixes[$captureName]).$literalSuffix")[0]
            Assert-True (($legacyGate.status -ceq 'Failed') -and ($legacyGate.passed -eq $false)) "$captureName must reject exact forbidden text $forbiddenText"
            Assert-True ([string]$legacyGate.reason -like "*$forbiddenText*") "$captureName forbidden-text failure must name $forbiddenText"
            Assert-LanLobbyFailedRoiDrawn $legacyResult.output $captureName $legacyGate.roi "$captureName $forbiddenText"
        }
    }

    $hostSpacingResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'host-slot3-to4-spacing' {
        param($caseManifest,$caseCaptureDirectory)
        Shift-LanLobbyFixtureRegion (Join-Path $caseCaptureDirectory 'room-host.png') (New-Object Drawing.Rectangle 1365,178,364,665) 5 0
    }
    $hostSpacingGate = @($hostSpacingResult.report.roomGates | Where-Object name -ceq 'RoomHost.Slot3To4.VisibleContourSpacing')[0]
    Assert-True (($hostSpacingGate.status -ceq 'Failed') -and ([Math]::Abs([double]$hostSpacingGate.centerDeltaPx.spacingDelta) -gt 4)) 'independent Figure 11 slot 4 pixel shift must block slot 3-to-4 spacing'
    Assert-LanLobbyFailedRoiDrawn $hostSpacingResult.output 'room-host' $hostSpacingGate.roi 'RoomHost slot3-to4 spacing'

    $fullSpacingResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'full-slot2-to3-spacing' {
        param($caseManifest,$caseCaptureDirectory)
        Shift-LanLobbyFixtureRegion (Join-Path $caseCaptureDirectory 'room-full.png') (New-Object Drawing.Rectangle 977,178,364,665) 5 0
    }
    $fullSpacingGate = @($fullSpacingResult.report.roomGates | Where-Object name -ceq 'RoomFull.Slot2To3.VisibleContourSpacing')[0]
    Assert-True (($fullSpacingGate.status -ceq 'Failed') -and ([Math]::Abs([double]$fullSpacingGate.centerDeltaPx.spacingDelta) -gt 4)) 'independent Figure 12 slot 3 pixel shift must block slot 2-to-3 spacing'
    Assert-LanLobbyFailedRoiDrawn $fullSpacingResult.output 'room-full' $fullSpacingGate.roi 'RoomFull slot2-to3 spacing'

    $structuredProfileCases = @(
        [pscustomobject]@{
            name='host-identity-text'
            mutate={
                param($record,$caseCaptureDirectory)
                $record.unityText = @($record.unityText) + [pscustomobject][ordered]@{
                    node='LanLobbyRoot/Room/RoomCard_0/Label';text='Doctor / capture-host';fontName='Novecento wide Normal Regular';fontResourcePath='';hasBitmapSource=$false;bitmapSourcePath=''
                }
            }
        },
        [pscustomobject]@{
            name='generic-profile-label'
            mutate={
                param($record,$caseCaptureDirectory)
                $record.unityText = @($record.unityText) + [pscustomobject][ordered]@{
                    node='LanLobbyRoot/Room/RoomCard_0/Label';text='PROFILE';fontName='Novecento wide Normal Regular';fontResourcePath='';hasBitmapSource=$false;bitmapSourcePath=''
                }
            }
        },
        [pscustomobject]@{
            name='approved-avatar-generic-icon'
            mutate={
                param($record,$caseCaptureDirectory)
                $sprite = [pscustomobject][ordered]@{
                    node='LanLobbyRoot/Room/RoomCard_0/Icon';spriteName='icon_amiy';sourcePath='Combined/[uc]autochesscommon/icon_amiy.png'
                    resourcesPath='UI/Lobby/Home/icon_amiy';sha256=$roomSpriteSha['icon_amiy']
                    captures=@([string]$record.name);occurrenceCount=1
                    coordinateOrigin='screen-bottom-left';unit='px';x=270;y=700;width=80;height=80;raycastTarget=$false
                }
                $record.spriteSources = @($record.spriteSources) + $sprite
                $record.sourceAudit = @($record.sourceAudit) + [pscustomobject][ordered]@{
                    node=$sprite.node;kind='bitmap-sprite';isBitmap=$true;spriteName=$sprite.spriteName;materialName=''
                    resourcesPath='UI/Lobby/Home/icon_amiy';sourcePath=$sprite.sourcePath;sha256=$roomSpriteSha['icon_amiy']
                    captures=@([string]$record.name);occurrenceCount=1;raycastTarget=$false
                }
                Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory "$($record.name).png") ([Drawing.Color]::FromArgb(255,70,120,210)) 270 300 80 80
            }
        },
        [pscustomobject]@{
            name='host-code-native-content'
            mutate={
                param($record,$caseCaptureDirectory)
                $record.codeNativeGeometry = @($record.codeNativeGeometry) + [pscustomobject][ordered]@{
                    name='LanLobbyRoot/Room/RoomCard_0/ProfileBacking';kind='code-native-geometry';isBitmap=$false
                    spriteName='';materialName='';resourcesPath='';sourcePath='';sha256='';color='#FFFFFFFF'
                    coordinateOrigin='screen-bottom-left';unit='px';raycastTarget=$false;x=270;y=700;width=80;height=80
                }
            }
        }
    )
    foreach ($structuredProfileCase in $structuredProfileCases)
    {
        $profileStructureResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory $structuredProfileCase.name {
            param($caseManifest,$caseCaptureDirectory)
            foreach ($record in @($caseManifest.captures | Where-Object { $_.name -like 'room-*' }))
            {
                & $structuredProfileCase.mutate $record $caseCaptureDirectory
            }
        }
        foreach ($captureName in @('room-host','room-full','room-ready'))
        {
            $profileGate = @($profileStructureResult.report.roomGates | Where-Object name -ceq "$($roomPrefixes[$captureName]).Slot1.ProfileContentAbsence")[0]
            Assert-True (($profileGate.status -ceq 'Failed') -and (-not $profileGate.structuredAbsencePassed)) "$captureName must reject $($structuredProfileCase.name)"
        }
        if ($structuredProfileCase.name -ceq 'approved-avatar-generic-icon')
        {
            $avatarGate = @($profileStructureResult.report.roomGates | Where-Object name -ceq 'RoomHost.Slot1.ProfileContentAbsence')[0]
            Assert-True $avatarGate.materialEvidence.passed 'approved extra avatar provenance must remain valid so structured/profile absence is independently blocking'
        }
    }

    $guestAvatarResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'approved-guest-avatars-remain-allowed' {
        param($caseManifest,$caseCaptureDirectory)
        $guestSpecs = @(
            [pscustomobject]@{ capture='room-full';node='LanLobbyRoot/Room/RoomCard_1/Icon';x=650;topY=300 },
            [pscustomobject]@{ capture='room-ready';node='LanLobbyRoot/Room/RoomCard_2/Profile/Icon';x=1050;topY=300 }
        )
        foreach ($guestSpec in $guestSpecs)
        {
            $record = @($caseManifest.captures | Where-Object name -ceq $guestSpec.capture)[0]
            $sprite = [pscustomobject][ordered]@{
                node=$guestSpec.node;spriteName='icon_amiy';sourcePath='Combined/[uc]autochesscommon/icon_amiy.png'
                resourcesPath='UI/Lobby/Home/icon_amiy';sha256=$roomSpriteSha['icon_amiy']
                captures=@([string]$record.name);occurrenceCount=1
                coordinateOrigin='screen-bottom-left';unit='px';x=$guestSpec.x;y=(1080-$guestSpec.topY-80);width=80;height=80;raycastTarget=$false
            }
            $record.spriteSources = @($record.spriteSources) + $sprite
            $record.sourceAudit = @($record.sourceAudit) + [pscustomobject][ordered]@{
                node=$sprite.node;kind='bitmap-sprite';isBitmap=$true;spriteName=$sprite.spriteName;materialName=''
                resourcesPath='UI/Lobby/Home/icon_amiy';sourcePath=$sprite.sourcePath;sha256=$roomSpriteSha['icon_amiy']
                captures=@([string]$record.name);occurrenceCount=1;raycastTarget=$false
            }
            Add-LanLobbyFixturePixels (Join-Path $caseCaptureDirectory "$($record.name).png") ([Drawing.Color]::FromArgb(255,220,35,170)) $guestSpec.x $guestSpec.topY 80 80
        }
    }
    foreach ($captureName in @('room-host','room-full','room-ready'))
    {
        $guestProfileGate = @($guestAvatarResult.report.roomGates | Where-Object name -ceq "$($roomPrefixes[$captureName]).Slot1.ProfileContentAbsence")[0]
        Assert-True (($guestProfileGate.status -ceq 'Passed') -and $guestProfileGate.structuredAbsencePassed) "$captureName host profile absence must ignore approved guest-slot avatar/profile Sprites"
    }

    $extraAuditResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'material-extra-unmatched-row' {
        param($caseManifest,$caseCaptureDirectory)
        $record = @($caseManifest.captures | Where-Object name -ceq 'room-host')[0]
        $record.sourceAudit = @($record.sourceAudit) + [pscustomobject][ordered]@{
            node='LanLobbyRoot/Room/UnknownExtra';kind='bitmap-sprite';isBitmap=$true;spriteName='unknown-extra';materialName=''
            resourcesPath='UI/Lobby/unknown-extra';sourcePath='[uc]autochessouter/unknown-extra.png';sha256=('A' * 64)
            captures=@('room-host');occurrenceCount=1;raycastTarget=$false
        }
    }
    $extraAuditGate = @($extraAuditResult.report.roomGates | Where-Object name -ceq 'RoomHost.Slot1.ReadyCheck')[0]
    Assert-True ((-not $extraAuditGate.materialEvidence.bijectionPassed) -and $extraAuditGate.status -ceq 'Failed') 'an extra unmatched bitmap audit row must block every room-host gate'

    $wrongPathResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'material-wrong-paths-capture' {
        param($caseManifest,$caseCaptureDirectory)
        $record = @($caseManifest.captures | Where-Object name -ceq 'room-ready')[0]
        $row = @($record.sourceAudit | Where-Object node -ceq 'LanLobbyRoot/Room/PrimaryAction')[0]
        $row.resourcesPath = 'UI/Lobby/wrong'
        $row.sourcePath = '[uc]autochessouter/wrong.png'
        $row.captures = @('room-full')
    }
    $wrongPathGate = @($wrongPathResult.report.roomGates | Where-Object name -ceq 'RoomReady.PrimaryAction.Cyan')[0]
    Assert-True ((-not $wrongPathGate.materialEvidence.pathPassed) -and (-not $wrongPathGate.materialEvidence.captureListPassed) -and $wrongPathGate.status -ceq 'Failed') 'wrong Resources/source paths and capture list must block ROI-associated material evidence'

    $missingAuditResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'material-missing-row' {
        param($caseManifest,$caseCaptureDirectory)
        $record = @($caseManifest.captures | Where-Object name -ceq 'room-ready')[0]
        $record.sourceAudit = @($record.sourceAudit | Where-Object node -cne 'LanLobbyRoot/Room/LeaveAction')
    }
    $missingAuditGate = @($missingAuditResult.report.roomGates | Where-Object name -ceq 'RoomReady.Leave')[0]
    Assert-True ((-not $missingAuditGate.materialEvidence.bijectionPassed) -and $missingAuditGate.status -ceq 'Failed') 'missing bitmap audit row must block the rendered occurrence bijection'

    $duplicateAuditResult = Invoke-LanLobbyVisualMutation $captureDirectory $referenceDirectory 'material-duplicate-row' {
        param($caseManifest,$caseCaptureDirectory)
        $record = @($caseManifest.captures | Where-Object name -ceq 'room-full')[0]
        $duplicate = @($record.sourceAudit | Where-Object node -ceq 'LanLobbyRoot/Room/PrimaryAction')[0] | Select-Object *
        $record.sourceAudit = @($record.sourceAudit) + $duplicate
    }
    $duplicateAuditGate = @($duplicateAuditResult.report.roomGates | Where-Object name -ceq 'RoomFull.PrimaryAction.Gray')[0]
    Assert-True ((-not $duplicateAuditGate.materialEvidence.bijectionPassed) -and (-not $duplicateAuditGate.materialEvidence.aggregatePassed) -and $duplicateAuditGate.status -ceq 'Failed') 'duplicate bitmap audit rows must block bijection and aggregate inventory'

    $intrudedCentralCaptureDirectory = Join-Path $scratch 'intruded-central-blank-captures'
    Copy-Item -LiteralPath $captureDirectory -Destination $intrudedCentralCaptureDirectory -Recurse
    $intrudedCentralManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $intrudedCentralCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $intrudedCentralManifest.captures) { $record.path = Join-Path $intrudedCentralCaptureDirectory ($record.name + '.png') }
    $intrudedCentralManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $intrudedCentralCaptureDirectory 'manifest.json') -Encoding UTF8
    $intrudedCentralHome = Join-Path $intrudedCentralCaptureDirectory 'home.png'
    Repair-JoinText01Fixture $intrudedCentralHome
    Add-JoinCentralBlankNeighborIntrusionFixture $intrudedCentralHome
    $intrudedCentralOutput = Join-Path $scratch 'intruded-central-blank-output'
    & $exportScript -CaptureDirectory $intrudedCentralCaptureDirectory -OutputDirectory $intrudedCentralOutput -ReferenceDirectory $referenceDirectory | Out-Null
    $intrudedCentralReport = Get-Content -Raw -LiteralPath (Join-Path $intrudedCentralOutput 'visual-diff-report.json') | ConvertFrom-Json
    $intrudedCentral = @($intrudedCentralReport.joinDecoration.components | Where-Object name -eq 'central-blank')[0]
    Assert-True ($intrudedCentral.measurementAvailable) 'unchanged central Blank must remain measurable beside neighboring orange block intrusion'
    Assert-True (($intrudedCentral.actualRawBounds.x -eq 324) -and ($intrudedCentral.actualRawBounds.y -eq 69) -and ($intrudedCentral.actualRawBounds.width -eq 58) -and ($intrudedCentral.actualRawBounds.height -eq 58)) 'neighboring block orange must not contaminate central Blank decoded bounds'
    Assert-True ($intrudedCentral.passed) 'unchanged central Blank must pass despite neighboring block orange in the broad search'
    $intrudedBlock = @($intrudedCentralReport.joinDecoration.components | Where-Object name -eq 'block-bank')[0]
    Assert-True (($intrudedBlock.actualRawBounds.x -eq $joinBlockBank.actualRawBounds.x) -and ($intrudedBlock.actualRawBounds.y -eq $joinBlockBank.actualRawBounds.y) -and ($intrudedBlock.actualRawBounds.width -eq $joinBlockBank.actualRawBounds.width) -and ($intrudedBlock.actualRawBounds.height -eq $joinBlockBank.actualRawBounds.height)) 'central intrusion fixture must preserve identical outer block-bank raw bounds'
    Assert-True (($intrudedBlock.internalTopology.comparison.profileJaccard -eq 1) -and ($intrudedBlock.internalTopology.comparison.occupiedColumnCountDelta -eq 0) -and $intrudedBlock.internalTopology.passed) 'central intrusion fixture must preserve the block-bank internal topology profile'
    Assert-True (@($intrudedCentralReport.joinDecoration.components | Where-Object { -not $_.passed }).Count -eq 0) 'central intrusion fixture must keep all seven corrected visual rows passing'
    Assert-True ($intrudedCentralReport.joinDecoration.passed) 'central intrusion fixture must keep overall Join acceptance passing'

    $shiftedCentralCaptureDirectory = Join-Path $scratch 'shifted-central-blank-captures'
    Copy-Item -LiteralPath $captureDirectory -Destination $shiftedCentralCaptureDirectory -Recurse
    $shiftedCentralManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $shiftedCentralCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $shiftedCentralManifest.captures) { $record.path = Join-Path $shiftedCentralCaptureDirectory ($record.name + '.png') }
    $shiftedCentralManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $shiftedCentralCaptureDirectory 'manifest.json') -Encoding UTF8
    $shiftedCentralHome = Join-Path $shiftedCentralCaptureDirectory 'home.png'
    Repair-JoinText01Fixture $shiftedCentralHome
    Shift-JoinCentralBlankFixture $shiftedCentralHome -6
    $shiftedCentralOutput = Join-Path $scratch 'shifted-central-blank-output'
    & $exportScript -CaptureDirectory $shiftedCentralCaptureDirectory -OutputDirectory $shiftedCentralOutput -ReferenceDirectory $referenceDirectory | Out-Null
    $shiftedCentralReport = Get-Content -Raw -LiteralPath (Join-Path $shiftedCentralOutput 'visual-diff-report.json') | ConvertFrom-Json
    $shiftedCentral = @($shiftedCentralReport.joinDecoration.components | Where-Object name -eq 'central-blank')[0]
    Assert-True ($shiftedCentral.measurementAvailable) 'genuinely shifted central Blank must remain measurable'
    Assert-True (($shiftedCentral.actualRawBounds.x -eq 318) -and ($shiftedCentral.actualRawBounds.width -eq 58)) 'genuinely shifted central Blank must preserve its decoded -6 px delta'
    Assert-True (($shiftedCentral.centerDeviationPx.deltaX -eq -5.5) -and ($shiftedCentral.passed -eq $false)) 'genuine central Blank perturbation must remain blocking'
    $shiftedCentralFailedComponents = @($shiftedCentralReport.joinDecoration.components | Where-Object { -not $_.passed } | ForEach-Object name)
    Assert-True ($shiftedCentralFailedComponents.Count -eq 1) "shifted central Blank fixture must isolate its visual-bound failure; failed: $($shiftedCentralFailedComponents -join ', ')"
    Assert-True ($shiftedCentralReport.joinDecoration.passed -eq $false) 'shifted central Blank must block overall Join acceptance'

    $shiftedBlockCaptureDirectory = Join-Path $scratch 'shifted-join-block-bank-captures'
    Copy-Item -LiteralPath $captureDirectory -Destination $shiftedBlockCaptureDirectory -Recurse
    $shiftedBlockManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $shiftedBlockCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $shiftedBlockManifest.captures) { $record.path = Join-Path $shiftedBlockCaptureDirectory ($record.name + '.png') }
    $shiftedBlockManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $shiftedBlockCaptureDirectory 'manifest.json') -Encoding UTF8
    $shiftedBlockHome = Join-Path $shiftedBlockCaptureDirectory 'home.png'
    Repair-JoinText01Fixture $shiftedBlockHome
    Shift-JoinBlockBankFixture $shiftedBlockHome -6
    $shiftedBlockOutput = Join-Path $scratch 'shifted-join-block-bank-output'
    & $exportScript -CaptureDirectory $shiftedBlockCaptureDirectory -OutputDirectory $shiftedBlockOutput -ReferenceDirectory $referenceDirectory | Out-Null
    $shiftedBlockReport = Get-Content -Raw -LiteralPath (Join-Path $shiftedBlockOutput 'visual-diff-report.json') | ConvertFrom-Json
    $shiftedBlock = @($shiftedBlockReport.joinDecoration.components | Where-Object name -eq 'block-bank')[0]
    Assert-True ($shiftedBlock.measurementAvailable -eq $true) 'shifted Join block-bank decoded measurement must remain available'
    $rawBlockShiftX = $shiftedBlock.actualRawBounds.x - $joinBlockBank.actualRawBounds.x
    $adjustedBlockShiftX = $shiftedBlock.actualBounds.x - $joinBlockBank.actualBounds.x
    Assert-True (($rawBlockShiftX -eq -6) -and ($adjustedBlockShiftX -eq -6)) 'block-bank adjustment must preserve the full decoded -6 px shift'
    Assert-True (($shiftedBlock.actualRawBounds.width -eq $joinBlockBank.actualRawBounds.width) -and ($shiftedBlock.actualBounds.width -eq $joinBlockBank.actualBounds.width)) 'block-bank adjustment must preserve an unchanged decoded width'
    Assert-True (($shiftedBlock.internalTopology.comparison.spanStartDeltaPx -eq -6) -and ($shiftedBlock.internalTopology.comparison.spanEndDeltaPx -eq -6) -and ($shiftedBlock.internalTopology.passed -eq $false)) 'uniform -6 px internal topology translation must remain independently blocking'
    Assert-True (($shiftedBlock.centerDeviationPx.deltaX -eq -6) -and ($shiftedBlock.passed -eq $false)) 'meaningful block-bank position delta must remain blocking'
    Assert-True (@($shiftedBlockReport.joinDecoration.components | Where-Object { -not $_.passed }).Count -eq 1) 'shifted block-bank fixture must isolate its visual-bound failure'
    Assert-True ($shiftedBlockReport.joinDecoration.passed -eq $false) 'shifted block-bank must block overall Join acceptance'

    $scrambledTopologyCaptureDirectory = Join-Path $scratch 'scrambled-join-block-topology-captures'
    Copy-Item -LiteralPath $captureDirectory -Destination $scrambledTopologyCaptureDirectory -Recurse
    $scrambledTopologyManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $scrambledTopologyCaptureDirectory 'manifest.json') | ConvertFrom-Json
    foreach ($record in $scrambledTopologyManifest.captures) { $record.path = Join-Path $scrambledTopologyCaptureDirectory ($record.name + '.png') }
    $scrambledTopologyManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $scrambledTopologyCaptureDirectory 'manifest.json') -Encoding UTF8
    $scrambledTopologyHome = Join-Path $scrambledTopologyCaptureDirectory 'home.png'
    Repair-JoinText01Fixture $scrambledTopologyHome
    Scramble-JoinBlockInternalTopologyFixture $scrambledTopologyHome
    $scrambledTopologyOutput = Join-Path $scratch 'scrambled-join-block-topology-output'
    & $exportScript -CaptureDirectory $scrambledTopologyCaptureDirectory -OutputDirectory $scrambledTopologyOutput -ReferenceDirectory $referenceDirectory | Out-Null
    $scrambledTopologyReport = Get-Content -Raw -LiteralPath (Join-Path $scrambledTopologyOutput 'visual-diff-report.json') | ConvertFrom-Json
    Assert-True (@($scrambledTopologyReport.joinDecoration.components).Count -eq 7) 'internal topology must not add an eighth Join component row'
    $scrambledBlock = @($scrambledTopologyReport.joinDecoration.components | Where-Object name -eq 'block-bank')[0]
    Assert-True (($scrambledBlock.actualRawBounds.x -eq $joinBlockBank.actualRawBounds.x) -and ($scrambledBlock.actualRawBounds.y -eq $joinBlockBank.actualRawBounds.y) -and ($scrambledBlock.actualRawBounds.width -eq $joinBlockBank.actualRawBounds.width) -and ($scrambledBlock.actualRawBounds.height -eq $joinBlockBank.actualRawBounds.height)) 'scrambled internal topology must preserve identical outer raw bank bounds'
    Assert-True ($scrambledBlock.boundsPassed -eq $true) 'scrambled internal topology must preserve the passing outer bounds gate'
    Assert-True ($scrambledBlock.internalTopology.measurementAvailable -and $scrambledBlock.internalTopology.referenceSelfPassed) 'scrambled internal topology must remain measurable with a self-consistent fixed reference'
    Assert-True (($scrambledBlock.internalTopology.actualRaw.span.startX -eq 214) -and ($scrambledBlock.internalTopology.actualRaw.span.endXInclusive -eq 517) -and ($scrambledBlock.internalTopology.actualRaw.occupiedColumnCount -eq 304)) 'scrambled topology must reproduce the retained Player compressed orange span'
    Assert-True ((@($scrambledBlock.internalTopology.actualRaw.runs).Count -eq 1) -and ($scrambledBlock.internalTopology.comparison.occupiedColumnCountDelta -eq -132)) 'scrambled topology must publish its single run and occupied-column deficit'
    Assert-True (([Math]::Round($scrambledBlock.internalTopology.comparison.profileJaccard, 6) -eq 0.689498) -and ($scrambledBlock.internalTopology.comparison.spanStartDeltaPx -eq 64) -and ($scrambledBlock.internalTopology.comparison.spanEndDeltaPx -eq -70) -and ($scrambledBlock.internalTopology.comparison.spanWidthDeltaPx -eq -134)) 'scrambled topology must publish the exact profile and span-size deviations'
    Assert-True (($scrambledBlock.internalTopology.passed -eq $false) -and ($scrambledBlock.passed -eq $false)) 'scrambled internal topology must block its named component despite identical outer bounds'
    Assert-True (@($scrambledTopologyReport.joinDecoration.components | Where-Object { -not $_.passed }).Count -eq 1) 'scrambled topology fixture must isolate the block-bank internal failure'
    Assert-True ($scrambledTopologyReport.joinDecoration.passed -eq $false) 'scrambled internal topology must block overall Join acceptance'

    $joinGeometryPrefix = 'LanLobbyRoot/Home/RoomSelect/Join/'
    $joinNegativeCases = @(
        [pscustomobject]@{
            name = 'displaced-join-backing'
            gate = 'backingTargetPassed'
            expected = $false
            mutate = {
                param($homeCaptureRecord)
                foreach ($geometry in @($homeCaptureRecord.codeNativeGeometry | Where-Object { $_.name -like ($joinGeometryPrefix + '*') })) { $geometry.x += 10 }
            }
        },
        [pscustomobject]@{
            name = 'join-graphic-crosses-action-boundary'
            gate = 'graphicsOrGeometryCrossesActionBoundary'
            expected = $true
            mutate = {
                param($homeCaptureRecord)
                $logo = @($homeCaptureRecord.spriteSources | Where-Object node -eq ($joinGeometryPrefix + 'Logo'))[0]
                $logo.y = 184
                $logo.height = 20
            }
        },
        [pscustomobject]@{
            name = 'missing-join-middle-block'
            gate = 'requiredSpriteInventoryPassed'
            expected = $false
            mutate = {
                param($homeCaptureRecord)
                $homeCaptureRecord.spriteSources = @($homeCaptureRecord.spriteSources | Where-Object node -ne ($joinGeometryPrefix + 'MiddleBlock_3'))
            }
        }
    )
    foreach ($negativeCase in $joinNegativeCases)
    {
        $caseCaptureDirectory = Join-Path $scratch ($negativeCase.name + '-captures')
        Copy-Item -LiteralPath $captureDirectory -Destination $caseCaptureDirectory -Recurse
        $caseManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $caseCaptureDirectory 'manifest.json') | ConvertFrom-Json
        foreach ($record in $caseManifest.captures) { $record.path = Join-Path $caseCaptureDirectory ($record.name + '.png') }
        $caseHome = @($caseManifest.captures | Where-Object name -eq 'home')[0]
        $mutate = $negativeCase.mutate
        & $mutate $caseHome
        $caseManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $caseCaptureDirectory 'manifest.json') -Encoding UTF8
        Repair-JoinText01Fixture (Join-Path $caseCaptureDirectory 'home.png')
        $caseOutput = Join-Path $scratch ($negativeCase.name + '-output')
        & $exportScript -CaptureDirectory $caseCaptureDirectory -OutputDirectory $caseOutput -ReferenceDirectory $referenceDirectory | Out-Null
        $caseReport = Get-Content -Raw -LiteralPath (Join-Path $caseOutput 'visual-diff-report.json') | ConvertFrom-Json
        Assert-True (@($caseReport.joinDecoration.components | Where-Object { -not $_.passed }).Count -eq 0) "$($negativeCase.name) must isolate the structural Join gate from visual-bound failures"
        Assert-True ($caseReport.joinDecoration.$($negativeCase.gate) -eq $negativeCase.expected) "$($negativeCase.name) must flip its Join acceptance gate"
        Assert-True ($caseReport.joinDecoration.passed -eq $false) "$($negativeCase.name) must block overall Join acceptance"
        foreach ($kind in @('actual','reference','overlay','heatmap')) { Assert-True (Test-Path -LiteralPath (Join-Path $caseOutput ('home-join-decoration-' + $kind + '.png'))) "$($negativeCase.name) must still publish home-join-decoration $kind" }
    }
    Assert-True ($script:fixtureCount -gt 0) 'zero generated fixtures is an explicit smoke failure'
    Assert-True ($script:assertionCount -gt 0) 'zero assertions is an explicit smoke failure'
    Write-Output "LAN lobby visual-diff smoke: PASS (fixtures=$script:fixtureCount; assertions=$script:assertionCount)"
}
finally
{
    if (Test-Path -LiteralPath $scratch)
    {
        try { Remove-Item -LiteralPath $scratch -Force -Recurse -ErrorAction Stop }
        catch { Write-Warning "Smoke fixture cleanup deferred: $scratch" }
    }
}
