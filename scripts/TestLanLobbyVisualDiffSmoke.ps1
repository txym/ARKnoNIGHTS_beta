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

function Fill-JoinDecorationFixture($Graphics, $NativeCrop, $TargetSize, $Bounds, [int] $Text01OffsetX)
{
    $orangeBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::Orange)
    $blockBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 235, 235, 235))
    $inputBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 112, 112, 112))
    try
    {
        foreach ($bound in @($Bounds | Where-Object { $_.name -in @('block-bank', 'input') }))
        {
            $drawBounds = [ordered]@{ x=$bound.x; y=$bound.y; width=$bound.width; height=$bound.height }
            $brush = if ($bound.name -eq 'block-bank') { $blockBrush } elseif ($bound.name -eq 'input') { $inputBrush } else { $orangeBrush }
            Fill-ScaledFixtureRectangle $Graphics $brush $NativeCrop $TargetSize $drawBounds
        }
        foreach ($bound in @($Bounds | Where-Object { $_.name -notin @('block-bank', 'input') }))
        {
            $drawBounds = [ordered]@{ x=$bound.x; y=$bound.y; width=$bound.width; height=$bound.height }
            if ($bound.name -eq 'text-01') { $drawBounds.x += $Text01OffsetX }
            Fill-ScaledFixtureRectangle $Graphics $orangeBrush $NativeCrop $TargetSize $drawBounds
        }
    }
    finally { $orangeBrush.Dispose(); $blockBrush.Dispose(); $inputBrush.Dispose() }
}

function New-JoinDecorationSpriteSources()
{
    $prefix = 'LanLobbyRoot/Home/RoomSelect/Join'
    return @(
        0..1 | ForEach-Object { [ordered]@{ node = "$prefix/LeftBlock_$_"; spriteName = 'room_select_join_left_block'; sourcePath = '[uc]autochessouter/room_select_join_left_block.png' } }
        0..3 | ForEach-Object { [ordered]@{ node = "$prefix/MiddleBlock_$_"; spriteName = 'room_select_join_middle_block'; sourcePath = '[uc]autochessouter/room_select_join_middle_block.png' } }
        0..1 | ForEach-Object { [ordered]@{ node = "$prefix/RightBlock_$_"; spriteName = 'room_select_join_right_block'; sourcePath = '[uc]autochessouter/room_select_join_right_block.png' } }
        [ordered]@{ node = "$prefix/MiddleMask"; spriteName = 'room_select_join_middle_block_mask'; sourcePath = '[uc]autochessouter/room_select_join_middle_block_mask.png' }
        [ordered]@{ node = "$prefix/Blank"; spriteName = 'room_select_join_blank'; sourcePath = '[uc]autochessouter/room_select_join_blank.png' }
        0..3 | ForEach-Object { [ordered]@{ node = "$prefix/Ban_$_"; spriteName = 'room_select_join_ban'; sourcePath = '[uc]autochessouter/room_select_join_ban.png' } }
        [ordered]@{ node = "$prefix/Triangle"; spriteName = 'room_select_join_triangle'; sourcePath = '[uc]autochessouter/room_select_join_triangle.png' }
        [ordered]@{ node = "$prefix/Logo"; spriteName = 'room_select_join_logo'; sourcePath = '[uc]autochessouter/room_select_join_logo.png' }
        [ordered]@{ node = "$prefix/Text01"; spriteName = 'room_select_join_text_01'; sourcePath = '[uc]autochessouter/room_select_join_text_01.png' }
        [ordered]@{ node = "$prefix/Text02"; spriteName = 'room_select_join_text_02'; sourcePath = '[uc]autochessouter/room_select_join_text_02.png' }
        [ordered]@{ node = "$prefix/RoomCodeInput"; spriteName = 'room_select_join_text_bg'; sourcePath = '[uc]autochessouter/room_select_join_text_bg.png' }
        [ordered]@{ node = "$prefix/JoinAction/ActionIcon"; spriteName = 'join_icon'; sourcePath = '[uc]autochessouter/join_icon.png' }
    )
}

function New-JoinDecorationGeometry()
{
    $prefix = 'LanLobbyRoot/Home/RoomSelect/Join'
    return @(
        [ordered]@{ name = "$prefix/InteriorBacking"; coordinateOrigin='screen-bottom-left'; unit='px'; kind = 'code-native-geometry'; isBitmap = $false; color = '#000000D1'; x = 1154; y = 204; width = 717; height = 280; spriteName = $null; raycastTarget = $false }
        [ordered]@{ name = "$prefix/OutlineTop"; coordinateOrigin='screen-bottom-left'; unit='px'; kind = 'code-native-geometry'; isBitmap = $false; color = '#3030308C'; x = 1154; y = 482; width = 717; height = 2; spriteName = $null; raycastTarget = $false }
        [ordered]@{ name = "$prefix/OutlineLeft"; coordinateOrigin='screen-bottom-left'; unit='px'; kind = 'code-native-geometry'; isBitmap = $false; color = '#3030308C'; x = 1154; y = 204; width = 2; height = 280; spriteName = $null; raycastTarget = $false }
        [ordered]@{ name = "$prefix/OutlineRight"; coordinateOrigin='screen-bottom-left'; unit='px'; kind = 'code-native-geometry'; isBitmap = $false; color = '#3030308C'; x = 1869; y = 204; width = 2; height = 280; spriteName = $null; raycastTarget = $false }
        [ordered]@{ name = "$prefix/GuideHorizontal"; coordinateOrigin='screen-bottom-left'; unit='px'; kind = 'code-native-geometry'; isBitmap = $false; color = '#FFA5008C'; x = 1154; y = 383; width = 717; height = 2; spriteName = $null; raycastTarget = $false }
        [ordered]@{ name = "$prefix/GuideVertical"; coordinateOrigin='screen-bottom-left'; unit='px'; kind = 'code-native-geometry'; isBitmap = $false; color = '#FFA5008C'; x = 1506; y = 288; width = 2; height = 196; spriteName = $null; raycastTarget = $false }
    )
}

try
{
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $captureDirectory = Join-Path $scratch 'captures'
    $referenceDirectory = Join-Path $scratch 'references'
    New-Item -ItemType Directory -Force -Path $captureDirectory, $referenceDirectory | Out-Null

    $figure9 = ([char]0x56FE).ToString() + '9.png'
    $figure10 = ([char]0x56FE).ToString() + '10.png'
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
            Fill-JoinDecorationFixture $graphics $joinDecorationNativeCrop $joinDecorationTarget $joinDecorationBounds 0
            Fill-CreateOpenFrameFixture $graphics $cyanBrush $cyanBrush $createFrameNativeCrop $createFrameTarget 0
        }
        finally { $contentBrush.Dispose(); $cyanBrush.Dispose() }
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
                    Fill-JoinDecorationFixture $graphics $joinDecorationTarget $joinDecorationTarget $joinDecorationBounds 5
                    Fill-CreateOpenFrameFixture $graphics $cyanBrush $lowContrastBrush $createFrameTarget $createFrameTarget 8
                }
                finally { $maskedBrush.Dispose(); $differenceBrush.Dispose(); $contentBrush.Dispose(); $cyanBrush.Dispose(); $lowContrastBrush.Dispose() }
            }
        }
        New-SolidPng $actualPath 1920 1080 ([Drawing.Color]::FromArgb(255, 60, 60, 60)) $draw
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
            ) + $(if ($name -in @('home', 'discovered-prefill')) {
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
            } else { @() }) + $(if ($name -in @('home', 'discovered-prefill')) { New-JoinDecorationSpriteSources } else { @() })
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
            ) + $(if ($name -in @('home', 'discovered-prefill')) { New-JoinDecorationGeometry } else { @() })
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
    Assert-True (Test-Path -LiteralPath (Join-Path $output 'visual-diff-report.json')) 'visual failure must still publish JSON'
    Assert-True (Test-Path -LiteralPath (Join-Path $output 'visual-diff-report.md')) 'visual failure must still publish Markdown'
    foreach ($kind in @('actual','reference','overlay','heatmap'))
    {
        Assert-True (Test-Path -LiteralPath (Join-Path $output ('home-create-frame-' + $kind + '.png'))) "visual failure must still publish home-create-frame $kind"
    }
    $report = Get-Content -Raw -LiteralPath (Join-Path $output 'visual-diff-report.json') | ConvertFrom-Json
    $homeCapture = $report.captures | Where-Object name -eq 'home'
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
    Assert-True ($joinDecoration.backingBottomScreenY -eq 876) 'Join backing bottom must meet the accepted Join action'
    Assert-True ($joinDecoration.geometryCrossesBackingBottom -eq $false) 'Join decoration geometry must not cross the backing bottom'
    Assert-True ($joinDecoration.joinActionPassed -eq $true) 'Join decoration must retain the accepted Join action/content gate'
    Assert-True (@($joinDecoration.components).Count -eq $joinDecorationBounds.Count) 'seven Join decoration visible-bound rows'
    foreach ($expectedJoinBound in $joinDecorationBounds)
    {
        $component = @($joinDecoration.components | Where-Object name -eq $expectedJoinBound.name)
        Assert-True ($component.Count -eq 1) "Join decoration/$($expectedJoinBound.name) visible bounds must occur once"
        $component = $component[0]
        Assert-True (($component.expectedBounds.x -eq $expectedJoinBound.x) -and ($component.expectedBounds.y -eq $expectedJoinBound.y) -and ($component.expectedBounds.width -eq $expectedJoinBound.width) -and ($component.expectedBounds.height -eq $expectedJoinBound.height)) "Join decoration/$($expectedJoinBound.name) expected bounds"
        Assert-True ($component.tolerancePx -eq $expectedJoinBound.tolerance) "Join decoration/$($expectedJoinBound.name) tolerance"
        Assert-True ($component.measurementAvailable -eq $true) "Join decoration/$($expectedJoinBound.name) decoded-pixel measurement must be available"
    }
    $joinText01 = @($joinDecoration.components | Where-Object name -eq 'text-01')[0]
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
    Assert-True ($markdown.Contains("${figure10}: 2048×1118")) 'Markdown must derive figure 10 native dimensions from decoded reference pixels'
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
