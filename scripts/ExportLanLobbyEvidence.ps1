[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $CaptureDirectory,
    [string] $OutputDirectory,
    [string[]] $ReferenceDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$commonScript = Join-Path $PSScriptRoot 'LanLobbyEvidence.Common.ps1'
. $commonScript
Import-LanLobbyEvidenceCommon | Out-Null
$captureDirectory = [IO.Path]::GetFullPath($CaptureDirectory)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $captureDirectory 'Evidence' }
$outputDirectory = Assert-LanLobbySafeOutputDirectory -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory

$referenceDirectories = if ($ReferenceDirectory -and $ReferenceDirectory.Count -gt 0) { @($ReferenceDirectory | ForEach-Object { [IO.Path]::GetFullPath($_) }) } else { @(Join-Path $projectRoot 'docs/references/ui/battle_hud') }
$manifestPath = Join-Path $captureDirectory 'manifest.json'
$assetMapPath = Join-Path $projectRoot 'docs/references/ui/lobby/ASSET_MAP.md'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Capture manifest not found: $manifestPath" }
$manifest = Get-LanLobbyCaptureManifest -ManifestPath $manifestPath
$spriteUsage = Get-LanLobbySpriteUsage -ProjectRoot $projectRoot -Manifest $manifest -AssetMapPath $assetMapPath
$referenceHome = Resolve-LanLobbyReferenceImage -Suffix '9' -ReferenceDirectory $referenceDirectories
$referenceRoom = Resolve-LanLobbyReferenceImage -Suffix '10' -ReferenceDirectory $referenceDirectories

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
