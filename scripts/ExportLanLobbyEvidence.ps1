[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $CaptureDirectory,
    [string] $OutputDirectory,
    [string[]] $ReferenceDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$captureDirectory = [IO.Path]::GetFullPath($CaptureDirectory)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $captureDirectory 'Evidence' }
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Temp'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Artifacts'))

function Test-IsWithinDirectory([string] $Candidate, [string] $Root)
{
    $candidatePath = [IO.Path]::GetFullPath($Candidate).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $candidatePath -ceq $rootPath -or $candidatePath.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-ExactReference([string] $Suffix, [string[]] $Directories)
{
    $expectedName = ([char]0x56FE).ToString() + $Suffix + '.png'
    $matches = @(
        foreach ($directory in $Directories)
        {
            if (-not (Test-Path -LiteralPath $directory)) { throw "Reference directory not found: $directory" }
            Get-ChildItem -LiteralPath $directory -File -Filter '*.png' | Where-Object { $_.Name -ceq $expectedName }
        }
    )
    if ($matches.Count -ne 1)
    {
        throw "Expected exactly one reference $Suffix named $expectedName; found $($matches.Count)."
    }
    return $matches[0].FullName
}

if (-not (Test-IsWithinDirectory $outputDirectory $temporaryRoot) -and -not (Test-IsWithinDirectory $outputDirectory $artifactsRoot))
{
    throw "Evidence output must be inside the safe ignored project directory Temp/ or Artifacts/: $outputDirectory"
}

$referenceDirectories = if ($ReferenceDirectory -and $ReferenceDirectory.Count -gt 0) { @($ReferenceDirectory | ForEach-Object { [IO.Path]::GetFullPath($_) }) } else { @(Join-Path $projectRoot 'docs/references/ui/battle_hud') }
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

$referenceHome = Resolve-ExactReference '9' $referenceDirectories
$referenceRoom = Resolve-ExactReference '10' $referenceDirectories

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
# References were already resolved by exact Unicode filename and cardinality before any output write.
foreach ($capture in $manifest.captures) {
    $reference = if ($capture.name -like 'room-*') { $referenceRoom } else { $referenceHome }
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
