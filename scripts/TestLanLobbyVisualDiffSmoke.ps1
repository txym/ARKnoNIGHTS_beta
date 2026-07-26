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
    New-SolidPng (Join-Path $referenceDirectory $figure9) 2048 1118 ([Drawing.Color]::FromArgb(255, 40, 50, 60)) $null
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
                try
                {
                    # Masked radar: x=0.00,y=0.18,w=0.57,h=0.66.
                    $graphics.FillRectangle($maskedBrush, 100, 300, 120, 80)
                    # create-room: x=0.60,y=0.42,w=0.36,h=0.11.
                    $graphics.FillRectangle($differenceBrush, 1200, 480, 100, 60)
                }
                finally { $maskedBrush.Dispose(); $differenceBrush.Dispose() }
            }
        }
        New-SolidPng $actualPath 1920 1080 ([Drawing.Color]::FromArgb(255, 40, 50, 60)) $draw
        $records += [ordered]@{
            name = $name
            path = $actualPath
            spriteSources = @([ordered]@{ spriteName = 'bg_terrain'; sourcePath = '[uc]autochessouter/bg_terrain.png' })
            rects = @(
                [ordered]@{ name = 'LanLobbyRoot/Room/RoomCard_0'; x = 100; y = 100; width = 200; height = 300 },
                [ordered]@{ name = 'LanLobbyRoot/Room/RoomCard_1'; x = 320; y = 100; width = 200; height = 300 },
                [ordered]@{ name = 'LanLobbyRoot/Room/RoomCard_2'; x = 540; y = 100; width = 200; height = 300 },
                [ordered]@{ name = 'LanLobbyRoot/Room/RoomCard_3'; x = 760; y = 100; width = 200; height = 300 }
            )
        }
    }
    [ordered]@{ captures = $records } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $captureDirectory 'manifest.json') -Encoding UTF8

    $invalidCaptureDirectory = Join-Path $scratch 'invalid-captures'
    New-Item -ItemType Directory -Force -Path $invalidCaptureDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $captureDirectory 'manifest.json') -Destination (Join-Path $invalidCaptureDirectory 'manifest.json')
    foreach ($name in $names) { New-SolidPng (Join-Path $invalidCaptureDirectory ($name + '.png')) 1280 720 ([Drawing.Color]::Black) $null }
    $invalidManifest = Get-Content -Raw -LiteralPath (Join-Path $invalidCaptureDirectory 'manifest.json') | ConvertFrom-Json
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
    Assert-True (($createAction.actualRect.x -eq 1154) -and ($createAction.actualRect.y -eq 453) -and ($createAction.actualRect.width -eq 717) -and ($createAction.actualRect.height -eq 99)) 'Create actual crop must use the approved Rect'
    Assert-True (($joinAction.actualRect.x -eq 1154) -and ($joinAction.actualRect.y -eq 876) -and ($joinAction.actualRect.width -eq 717) -and ($joinAction.actualRect.height -eq 99)) 'Join actual crop must use the approved Rect'
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
