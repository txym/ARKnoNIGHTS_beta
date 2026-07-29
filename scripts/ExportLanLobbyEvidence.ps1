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

Add-Type -AssemblyName System.Drawing

function Resolve-LanLobbyEvidenceReference([string] $ExpectedFileName)
{
    $matches = @(
        foreach ($directory in $referenceDirectories)
        {
            if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Reference directory not found: $directory" }
            Get-ChildItem -LiteralPath $directory -File -Filter '*.png' |
                Where-Object { $_.Name -ceq $ExpectedFileName }
        }
    )
    if ($matches.Count -ne 1) { throw "Expected exactly one reference named $ExpectedFileName; found $($matches.Count)." }
    return $matches[0].FullName
}

$figure9 = ([char]0x56FE).ToString() + '9.png'
$figure11 = ([char]0x56FE).ToString() + '11.png'
$figure12 = ([char]0x56FE).ToString() + '12.png'
$figure13 = ([char]0x56FE).ToString() + '13.png'
$referenceByCapture = [ordered]@{
    'home'=$figure9
    'discovered-prefill'=$figure9
    'room-host'=$figure11
    'room-full'=$figure12
    'room-ready'=$figure13
}
$referencePaths = @{}
foreach ($referenceName in @($referenceByCapture.Values | Select-Object -Unique))
{
    $referencePath = Resolve-LanLobbyEvidenceReference $referenceName
    try
    {
        $probe = [Drawing.Bitmap]::FromFile($referencePath)
        try
        {
            if ($referenceName -in @($figure11,$figure12,$figure13) -and
                ($probe.Width -ne 2560 -or $probe.Height -ne 1440))
            {
                throw "Room reference $referenceName must be exactly 2560x1440 before normalization; decoded $($probe.Width)x$($probe.Height)."
            }
        }
        finally { $probe.Dispose() }
    }
    catch
    {
        if ($_.Exception.Message -like '*must be exactly 2560x1440*') { throw }
        throw "Could not decode reference image: $referencePath. $($_.Exception.Message)"
    }
    $referencePaths[$referenceName] = $referencePath
}

# Reference decoding and native-size validation deliberately finish before output creation.
if (Test-Path -LiteralPath $outputDirectory) { throw "Evidence output directory must not already exist: $outputDirectory" }
$outputParent = Split-Path -Parent $outputDirectory
New-Item -ItemType Directory -Force -Path $outputParent | Out-Null
$stagingDirectory = Join-Path $outputParent ('.lan-lobby-evidence-staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
try
{
    foreach ($capture in $manifest.captures)
    {
        $reference = $referencePaths[[string]$referenceByCapture[[string]$capture.name]]
        $actual = [Drawing.Bitmap]::FromFile($capture.path)
        $expectedReference = [Drawing.Bitmap]::FromFile($reference)
        try
        {
            $width = $actual.Width + $expectedReference.Width
            $height = [Math]::Max($actual.Height, $expectedReference.Height)
            $sheet = New-Object Drawing.Bitmap $width, $height
            try
            {
                $graphics = [Drawing.Graphics]::FromImage($sheet)
                $font = New-Object Drawing.Font 'Arial', 18
                try
                {
                    $graphics.Clear([Drawing.Color]::Black)
                    $graphics.DrawImage($actual, 0, 0, $actual.Width, $actual.Height)
                    $graphics.DrawImage($expectedReference, $actual.Width, 0, $expectedReference.Width, $expectedReference.Height)
                    $graphics.DrawString('ACTUAL', $font, [Drawing.Brushes]::White, 12, 12)
                    $graphics.DrawString('REFERENCE', $font, [Drawing.Brushes]::White, ($actual.Width + 12), 12)
                    $sheet.Save((Join-Path $stagingDirectory ($capture.name + '-side-by-side.png')), [Drawing.Imaging.ImageFormat]::Png)
                }
                finally { $font.Dispose(); $graphics.Dispose() }
            }
            finally { $sheet.Dispose() }
        }
        finally
        {
            $actual.Dispose()
            $expectedReference.Dispose()
        }
    }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory 'manifest.json') -Force
    Move-Item -LiteralPath $stagingDirectory -Destination $outputDirectory
    $stagingDirectory = $null
}
catch
{
    if ($stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory))
    {
        Remove-Item -LiteralPath $stagingDirectory -Force -Recurse
    }
    throw
}
Write-Output "LAN lobby evidence exported: $outputDirectory"
