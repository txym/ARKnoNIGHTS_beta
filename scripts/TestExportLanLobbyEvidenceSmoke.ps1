[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exportScript = Join-Path $PSScriptRoot 'ExportLanLobbyEvidence.ps1'
$scratch = Join-Path $projectRoot ('Temp/LAN-LOBBY-ExportSmoke-' + [Guid]::NewGuid().ToString('N'))
$forbiddenAssetsOutput = Join-Path $projectRoot 'Assets/LanLobbyExportSmokeForbidden'
$script:assertionCount = 0
$script:fixtureCount = 0
Add-Type -AssemblyName System.Drawing

function Assert-True([bool] $Condition, [string] $Message)
{
    $script:assertionCount++
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function New-SolidPng([string] $Path, [int] $Width, [int] $Height, [Drawing.Color] $Color)
{
    $script:fixtureCount++
    $bitmap = New-Object Drawing.Bitmap $Width, $Height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.Clear($Color); $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
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
    if (-not $failed) { throw "Expected export failure: $ExpectedMessage" }
    if (Test-Path -LiteralPath $OutputPath) { throw "Export created output before validation: $OutputPath" }
}

try
{
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory (Join-Path $scratch 'missing-captures') -OutputDirectory $forbiddenAssetsOutput
    } $forbiddenAssetsOutput 'safe ignored project directory'

    $captureDirectory = Join-Path $scratch 'captures'
    New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null
    $sourcePng = Join-Path $projectRoot 'Assets/Resources/UI/Lobby/bg_terrain.png'
    $sourceProbe = [Drawing.Bitmap]::FromFile($sourcePng)
    try { $capturePixelWidth = $sourceProbe.Width }
    finally { $sourceProbe.Dispose() }
    $records = @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full') | ForEach-Object {
        [pscustomobject]@{
            name = $_
            path = $sourcePng
            spriteSources = @([pscustomobject]@{
                node = 'LanLobbyRoot/Terrain'
                spriteName = 'bg_terrain'
                resourcesPath = 'UI/Lobby/bg_terrain'
                sourcePath = '[uc]autochessouter/bg_terrain.png'
                sha256 = 'ECE7B6159268276287C20E3B3A82A5165BCC1D344EDFA6DE3B88EE24A76F988C'
                captures = @($_)
                occurrenceCount = 1
            })
        }
    }
    [pscustomobject]@{ captures = $records } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $captureDirectory 'manifest.json') -Encoding UTF8

    $missingReferences = Join-Path $scratch 'missing-references'
    $missingOutput = Join-Path $scratch 'missing-reference-output'
    New-Item -ItemType Directory -Force -Path $missingReferences | Out-Null
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $missingOutput -ReferenceDirectory $missingReferences
    } $missingOutput 'reference named'

    $firstReferences = Join-Path $scratch 'references-a'
    $secondReferences = Join-Path $scratch 'references-b'
    $figure9 = ([char]0x56FE).ToString() + '9.png'
    $figure11 = ([char]0x56FE).ToString() + '11.png'
    $figure12 = ([char]0x56FE).ToString() + '12.png'
    $figure13 = ([char]0x56FE).ToString() + '13.png'
    New-Item -ItemType Directory -Force -Path $firstReferences, $secondReferences | Out-Null
    Copy-Item -LiteralPath $sourcePng -Destination (Join-Path $firstReferences $figure9)
    New-SolidPng (Join-Path $firstReferences $figure11) 2560 1440 ([Drawing.Color]::Red)
    New-SolidPng (Join-Path $firstReferences $figure12) 2560 1440 ([Drawing.Color]::Green)
    New-SolidPng (Join-Path $firstReferences $figure13) 2560 1440 ([Drawing.Color]::Blue)
    New-SolidPng (Join-Path $secondReferences $figure12) 2560 1440 ([Drawing.Color]::Yellow)
    $ambiguousOutput = Join-Path $scratch 'ambiguous-reference-output'
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $ambiguousOutput -ReferenceDirectory @($firstReferences, $secondReferences)
    } $ambiguousOutput 'reference named'

    $wrongSizedReferences = Join-Path $scratch 'wrong-sized-references'
    Copy-Item -LiteralPath $firstReferences -Destination $wrongSizedReferences -Recurse
    New-SolidPng (Join-Path $wrongSizedReferences $figure12) 1920 1080 ([Drawing.Color]::Green)
    $wrongSizedOutput = Join-Path $scratch 'wrong-sized-output'
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $wrongSizedOutput -ReferenceDirectory $wrongSizedReferences
    } $wrongSizedOutput 'must be exactly 2560x1440 before normalization'

    $referenceHashes = @(Get-ChildItem -LiteralPath $firstReferences -File | Sort-Object Name | ForEach-Object { "$($_.Name):$((Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash)" })
    $successfulOutput = Join-Path $scratch 'successful-output'
    & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $successfulOutput -ReferenceDirectory $firstReferences | Out-Null
    $referenceHashesAfter = @(Get-ChildItem -LiteralPath $firstReferences -File | Sort-Object Name | ForEach-Object { "$($_.Name):$((Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash)" })
    Assert-True (($referenceHashes -join "`n") -eq ($referenceHashesAfter -join "`n")) 'evidence exporter must treat exact references as read-only inputs'
    $expectedRightColors = @{
        'room-host'=[Drawing.Color]::Red
        'room-full'=[Drawing.Color]::Green
        'room-ready'=[Drawing.Color]::Blue
    }
    foreach ($captureName in @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full'))
    {
        $sheetPath = Join-Path $successfulOutput ($captureName + '-side-by-side.png')
        Assert-True (Test-Path -LiteralPath $sheetPath) "Expected side-by-side output for $captureName"
        if ($captureName -like 'room-*')
        {
            $sheet = [Drawing.Bitmap]::FromFile($sheetPath)
            try
            {
                Assert-True (($sheet.Width -eq 2560 + $capturePixelWidth) -and ($sheet.Height -eq 1440)) "$captureName side-by-side must retain its exact 2560x1440 native reference"
                $actualColor = $sheet.GetPixel($capturePixelWidth + 2000, 1000)
                $expectedColor = $expectedRightColors[$captureName]
                Assert-True (($actualColor.R -eq $expectedColor.R) -and ($actualColor.G -eq $expectedColor.G) -and ($actualColor.B -eq $expectedColor.B)) "$captureName must route to its exact Figure 11-13 color fixture"
            }
            finally { $sheet.Dispose() }
        }
    }
    Assert-True (Test-Path -LiteralPath (Join-Path $successfulOutput 'manifest.json')) 'Expected copied manifest in successful evidence export.'

    Assert-True ($script:fixtureCount -gt 0) 'zero generated fixtures is an explicit smoke failure'
    Assert-True ($script:assertionCount -gt 0) 'zero assertions is an explicit smoke failure'
    Write-Output "Export LAN lobby evidence smoke: PASS (fixtures=$script:fixtureCount; assertions=$script:assertionCount)"
}
finally
{
    if (Test-Path -LiteralPath $forbiddenAssetsOutput) { Remove-Item -LiteralPath $forbiddenAssetsOutput -Force -Recurse }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Force -Recurse }
}
