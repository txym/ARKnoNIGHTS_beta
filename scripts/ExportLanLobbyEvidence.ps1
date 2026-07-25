[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $CaptureDirectory,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$captureDirectory = [IO.Path]::GetFullPath($CaptureDirectory)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $captureDirectory 'Evidence' }
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$manifestPath = Join-Path $captureDirectory 'manifest.json'
$assetMapPath = Join-Path $projectRoot 'docs/references/ui/lobby/ASSET_MAP.md'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Capture manifest not found: $manifestPath" }
if (-not (Test-Path -LiteralPath $assetMapPath)) { throw "Approved asset map not found: $assetMapPath" }

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($null -eq $manifest.captures -or @($manifest.captures).Count -ne 5) { throw 'The manifest must contain exactly five capture records.' }
$expected = @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full')
if ((@($manifest.captures.name | Sort-Object) -join '|') -ne (@($expected | Sort-Object) -join '|')) { throw 'Capture names do not match the approved lobby evidence set.' }
$assetMap = Get-Content -Raw -LiteralPath $assetMapPath
foreach ($capture in $manifest.captures) {
    if (-not (Test-Path -LiteralPath $capture.path)) { throw "Capture image missing: $($capture.path)" }
    if ($null -eq $capture.spriteSources -or @($capture.spriteSources).Count -eq 0) { throw "Capture $($capture.name) has no sprite provenance." }
    foreach ($sprite in $capture.spriteSources) {
        if ([string]::IsNullOrWhiteSpace($sprite.spriteName) -or [string]::IsNullOrWhiteSpace($sprite.sourcePath)) { throw "Capture $($capture.name) contains an incomplete sprite provenance record." }
        $expectedMapLine = '| ' + $sprite.spriteName + '.png | ' + $sprite.sourcePath + ' |'
        if (-not $assetMap.Contains($expectedMapLine)) { throw "Unmapped or non-approved sprite source: $($sprite.spriteName) -> $($sprite.sourcePath)" }
    }
}

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$referenceDirectory = Join-Path $projectRoot 'docs/references/ui/battle_hud'
# Resolve by the numeric suffix so Windows PowerShell 5 never interprets the Chinese filename
# using the active console code page. The user-provided reference contract is 图9 / 图10.
$referenceHome = @(Get-ChildItem -LiteralPath $referenceDirectory -File -Filter '*.png' | Where-Object { $_.BaseName -match '9$' }) | Select-Object -First 1 -ExpandProperty FullName
$referenceRoom = @(Get-ChildItem -LiteralPath $referenceDirectory -File -Filter '*.png' | Where-Object { $_.BaseName -match '10$' }) | Select-Object -First 1 -ExpandProperty FullName
foreach ($capture in $manifest.captures) {
    $reference = if ($capture.name -like 'room-*') { $referenceRoom } else { $referenceHome }
    if ([string]::IsNullOrWhiteSpace($reference) -or -not (Test-Path -LiteralPath $reference)) { throw "Reference image missing for capture '$($capture.name)': expected numeric reference suffix 9 or 10 in $referenceDirectory" }
    $actual = [Drawing.Bitmap]::FromFile($capture.path)
    $expectedReference = [Drawing.Bitmap]::FromFile($reference)
    try {
        $width = $actual.Width + $expectedReference.Width
        $height = [Math]::Max($actual.Height, $expectedReference.Height)
        $sheet = New-Object Drawing.Bitmap $width, $height
        try {
            $graphics = [Drawing.Graphics]::FromImage($sheet)
            try {
                $graphics.Clear([Drawing.Color]::Black)
                $graphics.DrawImage($actual, 0, 0, $actual.Width, $actual.Height)
                $graphics.DrawImage($expectedReference, $actual.Width, 0, $expectedReference.Width, $expectedReference.Height)
                $graphics.DrawString('ACTUAL', (New-Object Drawing.Font 'Arial', 18), [Drawing.Brushes]::White, 12, 12)
                $graphics.DrawString('REFERENCE', (New-Object Drawing.Font 'Arial', 18), [Drawing.Brushes]::White, ($actual.Width + 12), 12)
                $sheet.Save((Join-Path $outputDirectory ($capture.name + '-side-by-side.png')), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose() }
        } finally { $sheet.Dispose() }
    } finally {
        $actual.Dispose()
        $expectedReference.Dispose()
    }
}

Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $outputDirectory 'manifest.json') -Force
Write-Output "LAN lobby evidence exported: $outputDirectory"
