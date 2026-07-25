[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CaptureDirectory,

    [Parameter(Mandatory = $true)]
    [string]$AdjustmentRecord
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Artifacts/UI-INFO-002'))
$captureRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $CaptureDirectory))
if (-not $captureRoot.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "CaptureDirectory must be under $artifactsRoot"
}

$manifestPath = Join-Path $captureRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Manifest is missing: $manifestPath" }
$manifest = Get-Content -Raw -Encoding utf8 -LiteralPath $manifestPath | ConvertFrom-Json
if ($null -eq $manifest.captures -or $manifest.captures.Count -eq 0) { throw "Manifest has no captures: $manifestPath" }

function Set-ManifestValue {
    param([object]$Record, [string]$Name, [string]$Value)
    if ($Record.PSObject.Properties.Name -contains $Name) { $Record.$Name = $Value }
    else { $Record | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
}

function Write-PanelCrop {
    param([string]$SourcePath, [string]$PanelPath)
    $source = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        if ($source.Width -lt 720 -or $source.Height -lt 567) { throw "Capture is too small for the 720x567 panel crop: $SourcePath" }
        $panel = New-Object System.Drawing.Bitmap 720, 567
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($panel)
            try {
                $graphics.Clear([System.Drawing.Color]::Black)
                $graphics.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, 720, 567), (New-Object System.Drawing.Rectangle 0, 0, 720, 567), [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally { $graphics.Dispose() }
            $panel.Save($PanelPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $panel.Dispose() }
    }
    finally { $source.Dispose() }
}

$panelCount = 0
foreach ($capture in $manifest.captures) {
    if ($capture.width -ne 1920 -or $capture.height -ne 1080) { continue }
    $sourcePath = $capture.path
    if (-not (Test-Path -LiteralPath $sourcePath)) { $sourcePath = Join-Path $captureRoot ([System.IO.Path]::GetFileName($capture.path)) }
    if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Capture PNG is missing: $($capture.path)" }
    $panelPath = Join-Path $captureRoot ($capture.name + '_panel.png')
    Write-PanelCrop -SourcePath $sourcePath -PanelPath $panelPath
    Set-ManifestValue -Record $capture -Name 'panelCapturePath' -Value $panelPath
    $panelCount++
}

$referencePath = Join-Path $artifactsRoot 'reference/figure6_unit_detail_830x420.png'
$baselineReferencePath = Join-Path $artifactsRoot '00-baseline/figure6_unit_detail_830x420.png'
if (-not (Test-Path -LiteralPath $referencePath) -and (Test-Path -LiteralPath $baselineReferencePath)) {
    Copy-Item -LiteralPath $baselineReferencePath -Destination $referencePath
}
$comparisonCapture = $manifest.captures | Where-Object { $_.name -eq 'prep_selected_staging_1920x1080' } | Select-Object -First 1
if ($null -eq $comparisonCapture) { $comparisonCapture = $manifest.captures | Where-Object { $_.name -match 'fixture_medium_name_1920x1080' } | Select-Object -First 1 }
if ($null -eq $comparisonCapture) { throw 'No 1920x1080 staging or medium-name fixture capture is available for comparison.' }
if (-not (Test-Path -LiteralPath $referencePath)) { throw "Reference crop is missing: $referencePath" }

$panelPath = $comparisonCapture.panelCapturePath
if (-not (Test-Path -LiteralPath $panelPath)) { throw "Panel crop is missing: $panelPath" }
$comparisonPath = Join-Path $captureRoot ($comparisonCapture.name + '_comparison.png')
$reference = [System.Drawing.Image]::FromFile($referencePath)
$panel = [System.Drawing.Image]::FromFile($panelPath)
try {
    $comparison = New-Object System.Drawing.Bitmap 1440, 364
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($comparison)
        try {
            $graphics.Clear([System.Drawing.Color]::Black)
            $graphics.DrawImage($reference, (New-Object System.Drawing.Rectangle 0, 0, 720, 364), (New-Object System.Drawing.Rectangle 0, 0, $reference.Width, $reference.Height), [System.Drawing.GraphicsUnit]::Pixel)
            $graphics.DrawImage($panel, (New-Object System.Drawing.Rectangle 720, 0, 720, 364), (New-Object System.Drawing.Rectangle 0, 90, 720, 364), [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally { $graphics.Dispose() }
        $comparison.Save($comparisonPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $comparison.Dispose() }
}
finally {
    $reference.Dispose()
    $panel.Dispose()
}

Set-ManifestValue -Record $comparisonCapture -Name 'comparisonPath' -Value $comparisonPath
$recordPath = Join-Path $captureRoot 'adjustments.md'
$record = "# UI-INFO-002 adjustment record`r`n`r`n- Capture directory: ``$captureRoot`` `r`n- Adjustment: $AdjustmentRecord`r`n- Panel crop: ``720x567`` from the top-left of each 1920x1080 capture.`r`n- Comparison: Figure 6 reference scaled to ``720x364`` beside the mapped information-content area (``x=0, y=90, w=720, h=364``).`r`n"
[System.IO.File]::WriteAllText($recordPath, $record, (New-Object System.Text.UTF8Encoding($false)))
Set-ManifestValue -Record $manifest -Name 'adjustmentRecordPath' -Value $recordPath
Set-ManifestValue -Record $manifest -Name 'errorSummary' -Value 'No PNG decode, crop, or comparison errors occurred during evidence export.'

$json = $manifest | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
if (-not (Test-Path -LiteralPath $comparisonPath) -or $panelCount -eq 0) { throw 'Evidence export did not produce all required files.' }
Write-Output "UI-INFO-002 evidence exported: captures=$panelCount; comparison=$comparisonPath; record=$recordPath"
