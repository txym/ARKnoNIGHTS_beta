[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $CaptureDirectory,
    [string] $OutputDirectory,
    [string[]] $ReferenceDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'LanLobbyEvidence.Common.ps1')
Import-LanLobbyEvidenceCommon | Out-Null
Add-Type -AssemblyName System.Drawing

if (-not ('LanLobbyVisualDiff.Native' -as [type]))
{
    Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class LanLobbyVisualDiffMetric { public long ComparedPixels; public long DifferentPixels; public long ErrorSum; }
public static class LanLobbyVisualDiff {
    static bool Inside(Rectangle r, int x, int y) { return x >= r.X && y >= r.Y && x < r.Right && y < r.Bottom; }
    public static LanLobbyVisualDiffMetric[] Compare(Bitmap actual, Bitmap reference, Rectangle[] masks, Rectangle[] regions, bool[] regionMasks, Bitmap heatmap, out long maskedPixels) {
        int width = actual.Width, height = actual.Height, count = width * height;
        var masked = new bool[count]; maskedPixels = 0;
        for (int y=0; y<height; y++) for (int x=0; x<width; x++) { int i=y*width+x; foreach (var r in masks) if (Inside(r,x,y)) { masked[i]=true; maskedPixels++; break; } }
        var metrics = new LanLobbyVisualDiffMetric[regions.Length]; for(int i=0;i<metrics.Length;i++) metrics[i]=new LanLobbyVisualDiffMetric();
        var rect = new Rectangle(0,0,width,height); var af=actual.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb); var rf=reference.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb); var hf=heatmap.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try {
            var a=new byte[af.Stride*height]; var r=new byte[rf.Stride*height]; var h=new byte[hf.Stride*height]; Marshal.Copy(af.Scan0,a,0,a.Length); Marshal.Copy(rf.Scan0,r,0,r.Length);
            for(int y=0;y<height;y++) for(int x=0;x<width;x++) { int p=y*width+x, o=y*af.Stride+x*4; if(masked[p]) { h[o]=0;h[o+1]=0;h[o+2]=0;h[o+3]=0; continue; } int db=Math.Abs(a[o]-r[o]), dg=Math.Abs(a[o+1]-r[o+1]), dr=Math.Abs(a[o+2]-r[o+2]); int max=Math.Max(dr,Math.Max(dg,db)); int sum=dr+dg+db; int intensity=Math.Min(255, max*2); h[o]=0; h[o+1]=(byte)Math.Min(255,intensity*2); h[o+2]=(byte)intensity; h[o+3]=255; for(int n=0;n<regions.Length;n++) if(!regionMasks[n] && Inside(regions[n],x,y)) { metrics[n].ComparedPixels++; metrics[n].ErrorSum+=sum; if(max>24) metrics[n].DifferentPixels++; } }
            Marshal.Copy(h,0,hf.Scan0,h.Length);
        } finally { actual.UnlockBits(af); reference.UnlockBits(rf); heatmap.UnlockBits(hf); }
        return metrics;
    }
}
'@ -ReferencedAssemblies System.Drawing
}

# These normalized region and mask rules are intentionally kept verbatim from the approved visual-diff specification.
$homeRegions = @(
  @{ name='ignored-top-left'; x=0.00; y=0.00; width=0.22; height=0.15; mask=$true },
  @{ name='ignored-radar'; x=0.00; y=0.18; width=0.57; height=0.66; mask=$true },
  @{ name='ignored-bottom-left'; x=0.00; y=0.84; width=0.22; height=0.16; mask=$true },
  @{ name='create-room'; x=0.60; y=0.42; width=0.36; height=0.11; mask=$false },
  @{ name='join-room'; x=0.60; y=0.81; width=0.36; height=0.10; mask=$false },
  @{ name='main-background'; x=0.22; y=0.00; width=0.78; height=1.00; mask=$false }
)
$roomRegions = @(
  @{ name='ignored-top-left'; x=0.00; y=0.00; width=0.22; height=0.15; mask=$true },
  @{ name='ignored-player-art'; x=0.11; y=0.20; width=0.80; height=0.50; mask=$true },
  @{ name='ignored-bottom-right-mode'; x=0.55; y=0.88; width=0.25; height=0.10; mask=$true },
  @{ name='room-background'; x=0.22; y=0.00; width=0.78; height=1.00; mask=$false },
  @{ name='player-card-layout'; x=0.11; y=0.16; width=0.80; height=0.66; mask=$false }
)

function Convert-NormalizedRectangle($Region, [int] $Width, [int] $Height)
{
    $x = [Math]::Max(0, [Math]::Min($Width, [int][Math]::Floor($Region.x * $Width)))
    $y = [Math]::Max(0, [Math]::Min($Height, [int][Math]::Floor($Region.y * $Height)))
    $right = [Math]::Max($x, [Math]::Min($Width, [int][Math]::Ceiling(($Region.x + $Region.width) * $Width)))
    $bottom = [Math]::Max($y, [Math]::Min($Height, [int][Math]::Ceiling(($Region.y + $Region.height) * $Height)))
    return New-Object Drawing.Rectangle $x, $y, ($right - $x), ($bottom - $y)
}

function Get-RoomCardGeometry($Capture)
{
    $rectProperty = $Capture.PSObject.Properties['rects']
    if ($null -eq $rectProperty) { return @() }
    $cards = @($rectProperty.Value | Where-Object { $_ -and $_.name -match 'RoomCard_[0-3]$' })
    return @($cards | ForEach-Object { [pscustomobject][ordered]@{ name=[string]$_.name; x=[double]$_.x; y=[double]$_.y; width=[double]$_.width; height=[double]$_.height } })
}

function Test-LanLobbyReferenceImage([string] $ReferencePath)
{
    try
    {
        $probe = [Drawing.Bitmap]::FromFile($ReferencePath)
        try { return [pscustomobject]@{ Width = $probe.Width; Height = $probe.Height } }
        finally { $probe.Dispose() }
    }
    catch
    {
        throw "Could not decode reference image: $ReferencePath. $($_.Exception.Message)"
    }
}

$captureDirectory = [IO.Path]::GetFullPath($CaptureDirectory)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $captureDirectory 'VisualDiff' }
$outputDirectory = Assert-LanLobbySafeOutputDirectory -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory
$referenceDirectories = if ($ReferenceDirectory -and $ReferenceDirectory.Count -gt 0) { @($ReferenceDirectory | ForEach-Object { [IO.Path]::GetFullPath($_) }) } else { @(Join-Path $projectRoot 'docs/references/ui/battle_hud') }
$manifestPath = Join-Path $captureDirectory 'manifest.json'
$manifest = Get-LanLobbyCaptureManifest -ManifestPath $manifestPath
$assetMapPath = Join-Path $projectRoot 'docs/references/ui/lobby/ASSET_MAP.md'
$spriteUsage = Get-LanLobbySpriteUsage -ProjectRoot $projectRoot -Manifest $manifest -AssetMapPath $assetMapPath
$referenceHome = Resolve-LanLobbyReferenceImage -Suffix '9' -ReferenceDirectory $referenceDirectories
$referenceRoom = Resolve-LanLobbyReferenceImage -Suffix '10' -ReferenceDirectory $referenceDirectories
$referenceHomeProbe = Test-LanLobbyReferenceImage -ReferencePath $referenceHome
$referenceRoomProbe = Test-LanLobbyReferenceImage -ReferencePath $referenceRoom

# Dimension validation deliberately occurs before New-Item so failed captures cannot leave evidence output behind.
foreach ($capture in @($manifest.captures))
{
    $probe = [Drawing.Bitmap]::FromFile($capture.path)
    try { if ($probe.Width -ne 1920 -or $probe.Height -ne 1080) { throw "Visual-diff actual capture must be exactly 1920x1080: $($capture.path) is $($probe.Width)x$($probe.Height)" } }
    finally { $probe.Dispose() }
}

# The exporter never overwrites or removes caller output. A report destination must be absent;
# all generated files are staged beside it and moved in only after successful report generation.
if (Test-Path -LiteralPath $outputDirectory) { throw "Visual-diff output directory must not already exist: $outputDirectory" }
$outputParent = Split-Path -Parent $outputDirectory
New-Item -ItemType Directory -Force -Path $outputParent | Out-Null
$stagingDirectory = Join-Path $outputParent ('.lan-lobby-visual-diff-staging-' + [Guid]::NewGuid().ToString('N'))

$assets = @($spriteUsage | Group-Object SpriteName | Sort-Object Name | ForEach-Object {
    $first = $_.Group[0]
    [pscustomobject][ordered]@{ spriteName=$first.SpriteName; captures=@($_.Group.CaptureName | Sort-Object -Unique); resourcesPath=$first.ResourcesPath; sourcePath=$first.SourcePath; importedSha256=$first.ImportedSha256; occurrenceCount=[int](($_.Group | Measure-Object OccurrenceCount -Sum).Sum) }
})
$reportCaptures = @()
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
try
{
    foreach ($capture in @($manifest.captures))
    {
        $isRoom = $capture.name -like 'room-*'
        $referencePath = if ($isRoom) { $referenceRoom } else { $referenceHome }
        $regionSpecs = if ($isRoom) { $roomRegions } else { $homeRegions }
        $actual = $null
        $nativeReference = $null
        $normalizedReference = $null
        $overlay = $null
        $heatmap = $null
        try
        {
            $actual = [Drawing.Bitmap]::FromFile($capture.path)
            $nativeReference = [Drawing.Bitmap]::FromFile($referencePath)
            $normalizedReference = New-Object Drawing.Bitmap 1920, 1080
            $overlay = New-Object Drawing.Bitmap 1920, 1080
            $heatmap = New-Object Drawing.Bitmap 1920, 1080
            $graphics = [Drawing.Graphics]::FromImage($normalizedReference)
            try { $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear; $graphics.DrawImage($nativeReference, 0, 0, 1920, 1080) } finally { $graphics.Dispose() }
            $overlayGraphics = [Drawing.Graphics]::FromImage($overlay)
            $overlayAttributes = New-Object Drawing.Imaging.ImageAttributes
            try
            {
                $overlayGraphics.DrawImage($normalizedReference, 0, 0)
                $alphaMatrix = New-Object Drawing.Imaging.ColorMatrix
                $alphaMatrix.Matrix33 = 0.5
                $overlayAttributes.SetColorMatrix($alphaMatrix)
                $overlayGraphics.DrawImage($actual, (New-Object Drawing.Rectangle 0,0,1920,1080), 0,0,1920,1080, [Drawing.GraphicsUnit]::Pixel, $overlayAttributes)
            }
            finally { $overlayAttributes.Dispose(); $overlayGraphics.Dispose() }
            $rectangles = @($regionSpecs | ForEach-Object { Convert-NormalizedRectangle $_ 1920 1080 })
            $maskRectangles = @($rectangles | ForEach-Object -Begin { $i=0 } -Process { if ($regionSpecs[$i].mask) { $_ }; $i++ })
            [bool[]]$maskFlags = @($regionSpecs | ForEach-Object { [bool]$_.mask })
            [long]$maskedPixels = 0
            $metrics = [LanLobbyVisualDiff]::Compare($actual, $normalizedReference, [Drawing.Rectangle[]]$maskRectangles, [Drawing.Rectangle[]]$rectangles, $maskFlags, $heatmap, [ref]$maskedPixels)
            $regions = @()
            for ($index = 0; $index -lt $regionSpecs.Count; $index++)
            {
                $metric = $metrics[$index]
                $regions += [pscustomobject][ordered]@{ name=$regionSpecs[$index].name; x=$regionSpecs[$index].x; y=$regionSpecs[$index].y; width=$regionSpecs[$index].width; height=$regionSpecs[$index].height; mask=[bool]$regionSpecs[$index].mask; comparedPixels=[long]$metric.ComparedPixels; pixelDifferenceRatio=if ($metric.ComparedPixels -eq 0) { 0.0 } else { [double]$metric.DifferentPixels / $metric.ComparedPixels }; averageAbsoluteRgbError=if ($metric.ComparedPixels -eq 0) { 0.0 } else { [double]$metric.ErrorSum / ($metric.ComparedPixels * 3) } }
            }
            $measured = @($regions | Where-Object { -not $_.mask })
            [long]$compared = ($measured | Measure-Object comparedPixels -Sum).Sum
            [double]$weightedDifference = if ($compared -eq 0) { 0.0 } else { (($measured | ForEach-Object { $_.pixelDifferenceRatio * $_.comparedPixels } | Measure-Object -Sum).Sum / $compared) }
            [double]$weightedError = if ($compared -eq 0) { 0.0 } else { (($measured | ForEach-Object { $_.averageAbsoluteRgbError * $_.comparedPixels } | Measure-Object -Sum).Sum / $compared) }
            $reportCaptures += [pscustomobject][ordered]@{ name=[string]$capture.name; actualWidth=1920; actualHeight=1080; referenceWidth=$nativeReference.Width; referenceHeight=$nativeReference.Height; referenceNormalization='independent-xy'; referenceFigure=if ($isRoom) { '图10.png' } else { '图9.png' }; regions=$regions; maskedPixels=$maskedPixels; comparedPixels=$compared; pixelDifferenceRatio=$weightedDifference; averageAbsoluteRgbError=$weightedError; attention=($weightedDifference -gt 0.25 -or $weightedError -gt 48); roomCards=if ($isRoom) { Get-RoomCardGeometry $capture } else { @() }; referenceCardLayoutRegion=if ($isRoom) { $regions | Where-Object name -eq 'player-card-layout' } else { $null } }
            $actual.Save((Join-Path $stagingDirectory ($capture.name + '-actual.png')), [Drawing.Imaging.ImageFormat]::Png)
            $normalizedReference.Save((Join-Path $stagingDirectory ($capture.name + '-reference.png')), [Drawing.Imaging.ImageFormat]::Png)
            $overlay.Save((Join-Path $stagingDirectory ($capture.name + '-overlay.png')), [Drawing.Imaging.ImageFormat]::Png)
            $heatmap.Save((Join-Path $stagingDirectory ($capture.name + '-heatmap.png')), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { if ($actual) { $actual.Dispose() }; if ($nativeReference) { $nativeReference.Dispose() }; if ($normalizedReference) { $normalizedReference.Dispose() }; if ($overlay) { $overlay.Dispose() }; if ($heatmap) { $heatmap.Dispose() } }
    }
    $report = [ordered]@{ generatedAtUtc=[DateTime]::UtcNow.ToString('o'); referenceNormalization='independent-xy'; captures=$reportCaptures; assets=$assets }
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $stagingDirectory 'visual-diff-report.json') -Encoding UTF8
    $markdown = @('# LAN Lobby Visual Difference Report', '', 'Reference figures are native 2048×1118 evidence independently normalized on X and Y to 1920×1080. This is non-blocking layout/color reporting, not a pixel-equality claim.', '', '## Captures', '', '| Capture | Figure | Difference ratio | Avg RGB error | Attention |', '| --- | --- | ---: | ---: | --- |')
    foreach ($item in $reportCaptures) { $markdown += "| $($item.name) | $($item.referenceFigure) | $([Math]::Round($item.pixelDifferenceRatio, 4)) | $([Math]::Round($item.averageAbsoluteRgbError, 2)) | $(if($item.attention){'ATTENTION'}else{'OK'}) |" }
    $markdown += @('', 'Masked pixels are transparent black in heatmaps and excluded from metrics.', '', '## Region and mask rules', '', '| Name | x | y | width | height | Mask |', '| --- | ---: | ---: | ---: | ---: | --- |')
    foreach ($item in $reportCaptures) { foreach ($region in $item.regions) { $markdown += "| $($item.name):$($region.name) | $($region.x) | $($region.y) | $($region.width) | $($region.height) | $($region.mask) |" } }
    $markdown += @('', '## Approved sprite usage', '', '| Sprite | Captures | Resources path | Source-relative path | Imported SHA-256 | Total occurrences |', '| --- | --- | --- | --- | --- | ---: |')
    foreach ($asset in $assets) { $markdown += "| $($asset.spriteName) | $($asset.captures -join ', ') | $($asset.resourcesPath) | $($asset.sourcePath) | $($asset.importedSha256) | $($asset.occurrenceCount) |" }
    Set-Content -LiteralPath (Join-Path $stagingDirectory 'visual-diff-report.md') -Value $markdown -Encoding UTF8
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory 'manifest.json') -Force
    Move-Item -LiteralPath $stagingDirectory -Destination $outputDirectory
    $stagingDirectory = $null
}
catch { if ($stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory)) { Remove-Item -LiteralPath $stagingDirectory -Force -Recurse }; throw }

Write-Output "LAN lobby visual difference report exported: $outputDirectory"
