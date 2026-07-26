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
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class LanLobbyVisualDiffMetric { public long ComparedPixels; public long DifferentPixels; public long ErrorSum; }
public class LanLobbyVisualBounds { public int X; public int Y; public int Width; public int Height; }
public static class LanLobbyVisualDiff {
    static bool Inside(Rectangle r, int x, int y) { return x >= r.X && y >= r.Y && x < r.Right && y < r.Bottom; }
    public static LanLobbyVisualBounds FindDarkBounds(Bitmap bitmap, Rectangle search, int maximumLuminanceExclusive) {
        if (bitmap == null) throw new ArgumentNullException("bitmap");
        if (search.X < 0 || search.Y < 0 || search.Right > bitmap.Width || search.Bottom > bitmap.Height || search.Width <= 0 || search.Height <= 0) throw new ArgumentOutOfRangeException("search");
        int minX=search.Right, minY=search.Bottom, maxX=-1, maxY=-1;
        for(int y=search.Y;y<search.Bottom;y++) for(int x=search.X;x<search.Right;x++) {
            Color pixel=bitmap.GetPixel(x,y);
            int luminance=(299*pixel.R+587*pixel.G+114*pixel.B)/1000;
            if(pixel.A>0 && luminance<maximumLuminanceExclusive) {
                if(x<minX)minX=x;if(y<minY)minY=y;if(x>maxX)maxX=x;if(y>maxY)maxY=y;
            }
        }
        if(maxX<minX || maxY<minY) throw new InvalidOperationException("No dark visible pixels found in search rectangle " + search + ".");
        return new LanLobbyVisualBounds { X=minX, Y=minY, Width=maxX-minX+1, Height=maxY-minY+1 };
    }
    public static LanLobbyVisualBounds FindCompactDarkBounds(Bitmap bitmap, Rectangle search, int maximumLuminanceExclusive, int minimumComponentPixels, double maximumAspectRatio) {
        if (bitmap == null) throw new ArgumentNullException("bitmap");
        if (search.X < 0 || search.Y < 0 || search.Right > bitmap.Width || search.Bottom > bitmap.Height || search.Width <= 0 || search.Height <= 0) throw new ArgumentOutOfRangeException("search");
        var dark=new bool[search.Width,search.Height];
        for(int y=0;y<search.Height;y++) for(int x=0;x<search.Width;x++) {
            Color pixel=bitmap.GetPixel(search.X+x,search.Y+y);
            int luminance=(299*pixel.R+587*pixel.G+114*pixel.B)/1000;
            dark[x,y]=pixel.A>0 && luminance<maximumLuminanceExclusive;
        }
        int unionMinX=search.Right, unionMinY=search.Bottom, unionMaxX=-1, unionMaxY=-1;
        var queue=new Queue<Point>();
        for(int seedY=0;seedY<search.Height;seedY++) for(int seedX=0;seedX<search.Width;seedX++) {
            if(!dark[seedX,seedY]) continue;
            dark[seedX,seedY]=false; queue.Enqueue(new Point(seedX,seedY));
            int count=0,minX=search.Width,minY=search.Height,maxX=-1,maxY=-1;
            while(queue.Count>0) {
                Point point=queue.Dequeue(); count++;
                minX=Math.Min(minX,point.X);minY=Math.Min(minY,point.Y);maxX=Math.Max(maxX,point.X);maxY=Math.Max(maxY,point.Y);
                int left=point.X-1,right=point.X+1,up=point.Y-1,down=point.Y+1;
                if(left>=0 && dark[left,point.Y]) { dark[left,point.Y]=false; queue.Enqueue(new Point(left,point.Y)); }
                if(right<search.Width && dark[right,point.Y]) { dark[right,point.Y]=false; queue.Enqueue(new Point(right,point.Y)); }
                if(up>=0 && dark[point.X,up]) { dark[point.X,up]=false; queue.Enqueue(new Point(point.X,up)); }
                if(down<search.Height && dark[point.X,down]) { dark[point.X,down]=false; queue.Enqueue(new Point(point.X,down)); }
            }
            int width=maxX-minX+1,height=maxY-minY+1;
            double aspect=Math.Max((double)width/height,(double)height/width);
            if(count<minimumComponentPixels || aspect>maximumAspectRatio) continue;
            unionMinX=Math.Min(unionMinX,search.X+minX);unionMinY=Math.Min(unionMinY,search.Y+minY);
            unionMaxX=Math.Max(unionMaxX,search.X+maxX);unionMaxY=Math.Max(unionMaxY,search.Y+maxY);
        }
        if(unionMaxX<unionMinX || unionMaxY<unionMinY) throw new InvalidOperationException("No compact dark components found in search rectangle " + search + ".");
        return new LanLobbyVisualBounds { X=unionMinX, Y=unionMinY, Width=unionMaxX-unionMinX+1, Height=unionMaxY-unionMinY+1 };
    }
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
$homeActionBars = @(
  @{ name='home-create-action'; label='Create'; manifestRectName='LanLobbyRoot/Home/RoomSelect/Create/CreateAction'; approvedTarget=@{x=1154;y=453;width=717;height=99}; reference=@{x=1257;y=482;width=763;height=105} },
  @{ name='home-join-action'; label='Join'; manifestRectName='LanLobbyRoot/Home/RoomSelect/Join/JoinAction'; approvedTarget=@{x=1154;y=876;width=717;height=99}; reference=@{x=1257;y=932;width=763;height=105} }
)
$actionContentSpecs = @{
  'home-create-action' = @(
    @{ name='icon'; compact=$true; search=@{x=40;y=15;width=55;height=60}; expected=@{x=47;y=25;width=36;height=37} },
    @{ name='label'; search=@{x=100;y=20;width=170;height=50}; expected=@{x=109;y=28;width=148;height=32} }
  )
  'home-join-action' = @(
    @{ name='icon'; compact=$true; search=@{x=40;y=15;width=60;height=60}; expected=@{x=47;y=20;width=44;height=50} },
    @{ name='label'; search=@{x=95;y=20;width=170;height=50}; expected=@{x=104;y=31;width=150;height=34} }
  )
}
$figure9MeasurementSize = @{ width=2102; height=1149 }
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

function Convert-ActionReferenceRectangle($Reference, [int] $NativeWidth, [int] $NativeHeight)
{
    $scaleX = [double]$NativeWidth / $figure9MeasurementSize.width
    $scaleY = [double]$NativeHeight / $figure9MeasurementSize.height
    $x = [int][Math]::Floor($Reference.x * $scaleX)
    $y = [int][Math]::Floor($Reference.y * $scaleY)
    $right = [int][Math]::Ceiling(($Reference.x + $Reference.width) * $scaleX)
    $bottom = [int][Math]::Ceiling(($Reference.y + $Reference.height) * $scaleY)
    return New-Object Drawing.Rectangle $x, $y, ($right - $x), ($bottom - $y)
}

function Get-ClampedRectangle([Drawing.Rectangle] $Rectangle, [Drawing.Bitmap] $Source)
{
    $x = [Math]::Max(0, [Math]::Min($Source.Width, $Rectangle.X))
    $y = [Math]::Max(0, [Math]::Min($Source.Height, $Rectangle.Y))
    $right = [Math]::Max($x, [Math]::Min($Source.Width, $Rectangle.Right))
    $bottom = [Math]::Max($y, [Math]::Min($Source.Height, $Rectangle.Bottom))
    if ($right -le $x -or $bottom -le $y) { throw "Action crop is outside source bitmap: $Rectangle for $($Source.Width)x$($Source.Height)" }
    return New-Object Drawing.Rectangle $x, $y, ($right - $x), ($bottom - $y)
}

function New-LanLobbyBitmapCrop([Drawing.Bitmap] $Source, [Drawing.Rectangle] $Rectangle)
{
    $crop = Get-ClampedRectangle $Rectangle $Source
    $result = New-Object Drawing.Bitmap $crop.Width, $crop.Height
    $graphics = [Drawing.Graphics]::FromImage($result)
    try { $graphics.DrawImage($Source, (New-Object Drawing.Rectangle 0,0,$crop.Width,$crop.Height), $crop.X, $crop.Y, $crop.Width, $crop.Height, [Drawing.GraphicsUnit]::Pixel) }
    finally { $graphics.Dispose() }
    return $result
}

function Resize-LanLobbyBitmap([Drawing.Bitmap] $Source, [int] $Width, [int] $Height)
{
    $result = New-Object Drawing.Bitmap $Width, $Height
    $graphics = [Drawing.Graphics]::FromImage($result)
    try { $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear; $graphics.DrawImage($Source, 0, 0, $Width, $Height) }
    finally { $graphics.Dispose() }
    return $result
}

function ConvertTo-LanLobbyBoundsObject($Bounds)
{
    return [ordered]@{ x=$Bounds.X; y=$Bounds.Y; width=$Bounds.Width; height=$Bounds.Height }
}

function New-LanLobbyActionOverlay([Drawing.Bitmap] $Actual, [Drawing.Bitmap] $Reference)
{
    $overlay = New-Object Drawing.Bitmap $Actual.Width, $Actual.Height
    $graphics = [Drawing.Graphics]::FromImage($overlay)
    $attributes = New-Object Drawing.Imaging.ImageAttributes
    try
    {
        $graphics.DrawImage($Reference, 0, 0, $Reference.Width, $Reference.Height)
        $alphaMatrix = New-Object Drawing.Imaging.ColorMatrix
        $alphaMatrix.Matrix33 = 0.5
        $attributes.SetColorMatrix($alphaMatrix)
        $graphics.DrawImage($Actual, (New-Object Drawing.Rectangle 0,0,$Actual.Width,$Actual.Height), 0,0,$Actual.Width,$Actual.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
    }
    finally { $attributes.Dispose(); $graphics.Dispose() }
    return $overlay
}

function Get-RoomCardGeometry($Capture)
{
    $rectProperty = $Capture.PSObject.Properties['rects']
    if ($null -eq $rectProperty) { return @() }
    $cards = @($rectProperty.Value | Where-Object { $_ -and $_.name -match 'RoomCard_[0-3]$' })
    return @($cards | ForEach-Object { [pscustomobject][ordered]@{ name=[string]$_.name; x=[double]$_.x; y=[double]$_.y; width=[double]$_.width; height=[double]$_.height } })
}

function Convert-CapturedActionRectangle($Capture, $Spec)
{
    $captureWidth = [double]$Capture.width
    $captureHeight = [double]$Capture.height
    if ($captureWidth -le 0 -or $captureHeight -le 0) { throw "$($Spec.label) action Rect capture dimensions are invalid." }

    $matches = @($Capture.rects | Where-Object { $_ -and [string]$_.name -ceq [string]$Spec.manifestRectName })
    if ($matches.Count -ne 1) { throw "$($Spec.label) action Rect must occur exactly once in the home capture manifest; found $($matches.Count)." }
    $raw = $matches[0]
    if ([string]$raw.coordinateOrigin -cne 'screen-bottom-left' -or [string]$raw.unit -cne 'px')
    {
        throw "$($Spec.label) action Rect must declare coordinateOrigin=screen-bottom-left and unit=px."
    }

    $numbers = @([double]$raw.x, [double]$raw.y, [double]$raw.width, [double]$raw.height)
    $invalidNumbers = @($numbers | Where-Object { [double]::IsNaN($_) -or [double]::IsInfinity($_) })
    if ($invalidNumbers.Count -gt 0 -or
        $numbers[0] -lt 0 -or $numbers[1] -lt 0 -or $numbers[2] -le 0 -or $numbers[3] -le 0 -or
        ($numbers[0] + $numbers[2]) -gt $captureWidth -or ($numbers[1] + $numbers[3]) -gt $captureHeight)
    {
        throw "$($Spec.label) action Rect is invalid or outside the captured $([int]$captureWidth)x$([int]$captureHeight) screen."
    }

    $scaleX = 1920.0 / $captureWidth
    $scaleY = 1080.0 / $captureHeight
    $converted = [pscustomobject][ordered]@{
        coordinateOrigin = 'screen-top-left'
        unit = 'px'
        x = [int][Math]::Round($numbers[0] * $scaleX, [MidpointRounding]::AwayFromZero)
        y = [int][Math]::Round(($captureHeight - ($numbers[1] + $numbers[3])) * $scaleY, [MidpointRounding]::AwayFromZero)
        width = [int][Math]::Round($numbers[2] * $scaleX, [MidpointRounding]::AwayFromZero)
        height = [int][Math]::Round($numbers[3] * $scaleY, [MidpointRounding]::AwayFromZero)
    }
    if ($converted.width -le 0 -or $converted.height -le 0 -or
        $converted.x -lt 0 -or $converted.y -lt 0 -or
        ($converted.x + $converted.width) -gt 1920 -or ($converted.y + $converted.height) -gt 1080)
    {
        throw "$($Spec.label) action Rect is outside the normalized 1920x1080 screen."
    }
    return $converted
}

function ConvertTo-LanLobbyMarkdownCell([string] $Value)
{
    if ($null -eq $Value) { return '' }
    return $Value.Replace('|', '\|').Replace("`r`n", '<br>').Replace("`n", '<br>').Replace("`r", '<br>')
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
    try
    {
        if ($probe.Width -ne 1920 -or $probe.Height -ne 1080) { throw "Visual-diff actual capture must be exactly 1920x1080: $($capture.path) is $($probe.Width)x$($probe.Height)" }
        if ([int]$capture.width -ne $probe.Width -or [int]$capture.height -ne $probe.Height) { throw "Capture manifest dimensions do not match decoded pixels: $($capture.name)" }
    }
    finally { $probe.Dispose() }
}

$homeCapture = @($manifest.captures | Where-Object { $_.name -eq 'home' })[0]
$actionActualRects = @{}
foreach ($spec in $homeActionBars)
{
    $actionActualRects[$spec.name] = Convert-CapturedActionRectangle -Capture $homeCapture -Spec $spec
}

$textOccurrences = @(
    foreach ($capture in @($manifest.captures))
    {
        $textProperty = $capture.PSObject.Properties['unityText']
        $records = @()
        if ($null -ne $textProperty) { $records = @($textProperty.Value | Where-Object { $null -ne $_ }) }
        if ($records.Count -eq 0) { throw "Capture $($capture.name) has no active Unity Text evidence." }
        foreach ($record in $records)
        {
            if ([string]::IsNullOrWhiteSpace([string]$record.node) -or
                [string]::IsNullOrEmpty([string]$record.text) -or
                [string]::IsNullOrWhiteSpace([string]$record.fontName) -or
                [bool]$record.hasBitmapSource -or
                -not [string]::IsNullOrEmpty([string]$record.bitmapSourcePath))
            {
                throw "Capture $($capture.name) has invalid Unity Text evidence at node '$($record.node)'."
            }
            [pscustomobject]@{
                Capture = [string]$capture.name
                Node = [string]$record.node
                Text = [string]$record.text
                FontName = [string]$record.fontName
                FontResourcePath = [string]$record.fontResourcePath
                HasBitmapSource = [bool]$record.hasBitmapSource
                BitmapSourcePath = [string]$record.bitmapSourcePath
            }
        }
    }
)

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
$unityTextUsage = @($textOccurrences | Group-Object Node, Text, FontName, FontResourcePath, HasBitmapSource, BitmapSourcePath | Sort-Object Name | ForEach-Object {
    $first = $_.Group[0]
    [pscustomobject][ordered]@{
        kind='unity-text'
        node=$first.Node
        text=$first.Text
        captures=@($_.Group.Capture | Sort-Object -Unique)
        fontName=$first.FontName
        fontResourcePath=$first.FontResourcePath
        hasBitmapSource=$first.HasBitmapSource
        bitmapSourcePath=$first.BitmapSourcePath
        occurrenceCount=$_.Count
    }
})
$geometryOccurrences = @(
    foreach ($capture in @($manifest.captures))
    {
        $geometryProperty = $capture.PSObject.Properties['codeNativeGeometry']
        if ($null -eq $geometryProperty) { continue }
        foreach ($geometry in @($geometryProperty.Value | Where-Object { $null -ne $_ }))
        {
            [pscustomobject]@{
                Capture = [string]$capture.name
                Name = [string]$geometry.name
                Kind = [string]$geometry.kind
                IsBitmap = [bool]$geometry.isBitmap
                Color = [string]$geometry.color
            }
        }
    }
)
$codeGeneratedGeometry = @($geometryOccurrences | Group-Object Name, Kind, IsBitmap, Color | Sort-Object Name | ForEach-Object {
    $first = $_.Group[0]
    [pscustomobject][ordered]@{
        name=$first.Name
        kind=$first.Kind
        isBitmap=$first.IsBitmap
        color=$first.Color
        captures=@($_.Group.Capture | Sort-Object -Unique)
        occurrenceCount=$_.Count
    }
})
$reportCaptures = @()
$actionBarReports = @()
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
            $reportCaptures += [pscustomobject][ordered]@{ name=[string]$capture.name; actualWidth=1920; actualHeight=1080; referenceWidth=$nativeReference.Width; referenceHeight=$nativeReference.Height; referenceNormalization='independent-xy'; referenceFigure=[IO.Path]::GetFileName($referencePath); regions=$regions; maskedPixels=$maskedPixels; comparedPixels=$compared; pixelDifferenceRatio=$weightedDifference; averageAbsoluteRgbError=$weightedError; attention=($weightedDifference -gt 0.25 -or $weightedError -gt 48); roomCards=if ($isRoom) { Get-RoomCardGeometry $capture } else { @() }; referenceCardLayoutRegion=if ($isRoom) { $regions | Where-Object name -eq 'player-card-layout' } else { $null } }
            $actual.Save((Join-Path $stagingDirectory ($capture.name + '-actual.png')), [Drawing.Imaging.ImageFormat]::Png)
            $normalizedReference.Save((Join-Path $stagingDirectory ($capture.name + '-reference.png')), [Drawing.Imaging.ImageFormat]::Png)
            $overlay.Save((Join-Path $stagingDirectory ($capture.name + '-overlay.png')), [Drawing.Imaging.ImageFormat]::Png)
            $heatmap.Save((Join-Path $stagingDirectory ($capture.name + '-heatmap.png')), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { if ($actual) { $actual.Dispose() }; if ($nativeReference) { $nativeReference.Dispose() }; if ($normalizedReference) { $normalizedReference.Dispose() }; if ($overlay) { $overlay.Dispose() }; if ($heatmap) { $heatmap.Dispose() } }
    }
    foreach ($spec in $homeActionBars)
    {
        $actualSpec = $actionActualRects[$spec.name]
        $actual = $null
        $nativeReference = $null
        $actualCrop = $null
        $nativeReferenceCrop = $null
        $locallyResizedReferenceCrop = $null
        $comparisonReferenceCrop = $null
        $overlay = $null
        $heatmap = $null
        try
        {
            $actual = [Drawing.Bitmap]::FromFile($homeCapture.path)
            $nativeReference = [Drawing.Bitmap]::FromFile($referenceHome)
            $actualRectangle = New-Object Drawing.Rectangle $actualSpec.x, $actualSpec.y, $actualSpec.width, $actualSpec.height
            $scaledReference = Convert-ActionReferenceRectangle $spec.reference $nativeReference.Width $nativeReference.Height
            $actualCrop = New-LanLobbyBitmapCrop $actual $actualRectangle
            $nativeReferenceCrop = New-LanLobbyBitmapCrop $nativeReference $scaledReference
            $locallyResizedReferenceCrop = Resize-LanLobbyBitmap $nativeReferenceCrop $spec.approvedTarget.width $spec.approvedTarget.height
            $comparisonReferenceCrop = Resize-LanLobbyBitmap $locallyResizedReferenceCrop $actualCrop.Width $actualCrop.Height
            $overlay = New-LanLobbyActionOverlay $actualCrop $comparisonReferenceCrop
            $heatmap = New-Object Drawing.Bitmap $actualCrop.Width, $actualCrop.Height
            $fullCrop = New-Object Drawing.Rectangle 0,0,$actualCrop.Width,$actualCrop.Height
            [long]$maskedPixels = 0
            $metric = ([LanLobbyVisualDiff]::Compare($actualCrop, $comparisonReferenceCrop, [Drawing.Rectangle[]]@(), [Drawing.Rectangle[]]@($fullCrop), [bool[]]@($false), $heatmap, [ref]$maskedPixels))[0]
            $contentVisuals = @()
            foreach ($contentSpec in @($actionContentSpecs[$spec.name]))
            {
                $search = New-Object Drawing.Rectangle $contentSpec.search.x, $contentSpec.search.y, $contentSpec.search.width, $contentSpec.search.height
                $usesCompactComponents = $contentSpec.ContainsKey('compact') -and [bool]$contentSpec.compact
                if ($usesCompactComponents)
                {
                    $actualBounds = [LanLobbyVisualDiff]::FindCompactDarkBounds($actualCrop, $search, 45, 40, 4.0)
                    $referenceBounds = [LanLobbyVisualDiff]::FindCompactDarkBounds($locallyResizedReferenceCrop, $search, 45, 40, 4.0)
                }
                else
                {
                    $actualBounds = [LanLobbyVisualDiff]::FindDarkBounds($actualCrop, $search, 45)
                    $referenceBounds = [LanLobbyVisualDiff]::FindDarkBounds($locallyResizedReferenceCrop, $search, 45)
                }
                $expected = $contentSpec.expected
                $actualCenterX = $actualBounds.X + ($actualBounds.Width - 1) / 2.0
                $actualCenterY = $actualBounds.Y + ($actualBounds.Height - 1) / 2.0
                $expectedCenterX = $expected.x + ($expected.width - 1) / 2.0
                $expectedCenterY = $expected.y + ($expected.height - 1) / 2.0
                $centerDeltaX = $actualCenterX - $expectedCenterX
                $centerDeltaY = $actualCenterY - $expectedCenterY
                $widthDelta = $actualBounds.Width - $expected.width
                $heightDelta = $actualBounds.Height - $expected.height
                $passed = [Math]::Abs($centerDeltaX) -le 1 `
                    -and [Math]::Abs($centerDeltaY) -le 1 `
                    -and [Math]::Abs($widthDelta) -le 2 `
                    -and [Math]::Abs($heightDelta) -le 2
                $contentVisuals += [pscustomobject][ordered]@{
                    name = $contentSpec.name
                    thresholdLumaExclusive = 45
                    measurement = if ($usesCompactComponents) { 'compact-components' } else { 'all-dark-pixels' }
                    minimumComponentPixels = if ($usesCompactComponents) { 40 } else { 0 }
                    maximumComponentAspectRatio = if ($usesCompactComponents) { 4.0 } else { 0 }
                    expectedBounds = [ordered]@{ x=$expected.x; y=$expected.y; width=$expected.width; height=$expected.height }
                    referenceBounds = ConvertTo-LanLobbyBoundsObject $referenceBounds
                    actualBounds = ConvertTo-LanLobbyBoundsObject $actualBounds
                    centerDeviationPx = [ordered]@{ unit='px'; deltaX=$centerDeltaX; deltaY=$centerDeltaY }
                    sizeDeviationPx = [ordered]@{ unit='px'; deltaWidth=$widthDelta; deltaHeight=$heightDelta }
                    passed = $passed
                }
            }
            $actionBarReports += [pscustomobject][ordered]@{
                name = $spec.name
                capture = 'home'
                actualRect = [ordered]@{ coordinateOrigin=$actualSpec.coordinateOrigin; unit=$actualSpec.unit; x=$actualSpec.x; y=$actualSpec.y; width=$actualSpec.width; height=$actualSpec.height }
                referenceRect = [ordered]@{ x=$scaledReference.X; y=$scaledReference.Y; width=$scaledReference.Width; height=$scaledReference.Height }
                referenceMeasurementCanvas = [ordered]@{ width=$figure9MeasurementSize.width; height=$figure9MeasurementSize.height }
                approvedTargetRectPx1920x1080 = [ordered]@{ coordinateOrigin='screen-top-left'; unit='px'; x=$spec.approvedTarget.x; y=$spec.approvedTarget.y; width=$spec.approvedTarget.width; height=$spec.approvedTarget.height }
                locallyResizedReferenceSizePx = [ordered]@{ unit='px'; width=$locallyResizedReferenceCrop.Width; height=$locallyResizedReferenceCrop.Height }
                comparisonReferenceSizePx = [ordered]@{ unit='px'; width=$comparisonReferenceCrop.Width; height=$comparisonReferenceCrop.Height }
                positionDeviationPx1920x1080 = [ordered]@{ unit='px'; deltaX=($actualSpec.x - $spec.approvedTarget.x); deltaY=($actualSpec.y - $spec.approvedTarget.y) }
                sizeDeviationPxAfterLocalReferenceResize = [ordered]@{ unit='px'; deltaWidth=($actualCrop.Width - $locallyResizedReferenceCrop.Width); deltaHeight=($actualCrop.Height - $locallyResizedReferenceCrop.Height) }
                comparedPixels = $metric.ComparedPixels
                pixelDifferenceRatio = [double]$metric.DifferentPixels / $metric.ComparedPixels
                averageAbsoluteRgbError = [double]$metric.ErrorSum / ($metric.ComparedPixels * 3)
                contentVisuals = $contentVisuals
            }
            $actualCrop.Save((Join-Path $stagingDirectory ($spec.name + '-actual.png')), [Drawing.Imaging.ImageFormat]::Png)
            $locallyResizedReferenceCrop.Save((Join-Path $stagingDirectory ($spec.name + '-reference.png')), [Drawing.Imaging.ImageFormat]::Png)
            $overlay.Save((Join-Path $stagingDirectory ($spec.name + '-overlay.png')), [Drawing.Imaging.ImageFormat]::Png)
            $heatmap.Save((Join-Path $stagingDirectory ($spec.name + '-heatmap.png')), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { if ($actual) { $actual.Dispose() }; if ($nativeReference) { $nativeReference.Dispose() }; if ($actualCrop) { $actualCrop.Dispose() }; if ($nativeReferenceCrop) { $nativeReferenceCrop.Dispose() }; if ($locallyResizedReferenceCrop) { $locallyResizedReferenceCrop.Dispose() }; if ($comparisonReferenceCrop) { $comparisonReferenceCrop.Dispose() }; if ($overlay) { $overlay.Dispose() }; if ($heatmap) { $heatmap.Dispose() } }
    }
    $report = [ordered]@{
        generatedAtUtc=[DateTime]::UtcNow.ToString('o')
        referenceNormalization='independent-xy'
        captures=$reportCaptures
        actionBars=$actionBarReports
        assets=$assets
        materialUsage=[ordered]@{
            bitmapSprites=$assets
            unityText=$unityTextUsage
            codeGeneratedGeometry=$codeGeneratedGeometry
        }
    }
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $stagingDirectory 'visual-diff-report.json') -Encoding UTF8
    $referenceDimensionLines = @($reportCaptures | Group-Object referenceFigure | Sort-Object Name | ForEach-Object {
        $first = $_.Group[0]
        "$($_.Name): $($first.referenceWidth)×$($first.referenceHeight)"
    })
    $markdown = @('# LAN Lobby Visual Difference Report', '', "Reference figure native dimensions (decoded from this export): $($referenceDimensionLines -join '; '). Each reference is independently normalized on X and Y to 1920×1080. This is non-blocking layout/color reporting, not a pixel-equality claim.", '', '## Captures', '', '| Capture | Figure | Difference ratio | Avg RGB error | Attention |', '| --- | --- | ---: | ---: | --- |')
    foreach ($item in $reportCaptures) { $markdown += "| $($item.name) | $($item.referenceFigure) | $([Math]::Round($item.pixelDifferenceRatio, 4)) | $([Math]::Round($item.averageAbsoluteRgbError, 2)) | $(if($item.attention){'ATTENTION'}else{'OK'}) |" }
    $markdown += @('', 'Masked pixels are transparent black in heatmaps and excluded from metrics.', '', '## Home action bars', '', 'Action comparisons use measured native Figure 9 crops. The manifest-derived actual crop and approved target use 1920×1080 screen coordinates with a top-left origin. The native reference crop is locally resized to the approved target size; a separately reported comparison copy is resized to the actual crop only for pixel metrics and overlays. The legacy full-screen report retains its existing independent-X/Y normalization.', '', '| Name | Actual Rect (px) | Approved target Rect (px) | Native reference Rect (px) | Locally resized reference | Comparison reference | Position deviation (px) | Size deviation after local resize (px) | Difference ratio | Avg RGB error |', '| --- | --- | --- | --- | --- | --- | --- | --- | ---: | ---: |')
    foreach ($item in $actionBarReports) { $markdown += "| $($item.name) | $($item.actualRect.x),$($item.actualRect.y),$($item.actualRect.width),$($item.actualRect.height) | $($item.approvedTargetRectPx1920x1080.x),$($item.approvedTargetRectPx1920x1080.y),$($item.approvedTargetRectPx1920x1080.width),$($item.approvedTargetRectPx1920x1080.height) | $($item.referenceRect.x),$($item.referenceRect.y),$($item.referenceRect.width),$($item.referenceRect.height) | $($item.locallyResizedReferenceSizePx.width)x$($item.locallyResizedReferenceSizePx.height) px | $($item.comparisonReferenceSizePx.width)x$($item.comparisonReferenceSizePx.height) px | dx=$($item.positionDeviationPx1920x1080.deltaX), dy=$($item.positionDeviationPx1920x1080.deltaY) | dw=$($item.sizeDeviationPxAfterLocalReferenceResize.deltaWidth), dh=$($item.sizeDeviationPxAfterLocalReferenceResize.deltaHeight) | $([Math]::Round($item.pixelDifferenceRatio, 4)) | $([Math]::Round($item.averageAbsoluteRgbError, 2)) |" }
    $markdown += @('', '## Home action content visible bounds', '', 'Actual and locally resized Figure 9 reference crops use the same luminance threshold. Acceptance is based on the four fixed expected visible bounds, not on full Sprite or Text Rect centers.', '', '| Element | Expected | Reference measured | Actual measured | Center deviation (px) | Size deviation (px) | Passed |', '| --- | --- | --- | --- | --- | --- | --- |')
    foreach ($item in $actionBarReports)
    {
        foreach ($content in @($item.contentVisuals))
        {
            $markdown += "| $($item.name)/$($content.name) | $($content.expectedBounds.x),$($content.expectedBounds.y),$($content.expectedBounds.width),$($content.expectedBounds.height) | $($content.referenceBounds.x),$($content.referenceBounds.y),$($content.referenceBounds.width),$($content.referenceBounds.height) | $($content.actualBounds.x),$($content.actualBounds.y),$($content.actualBounds.width),$($content.actualBounds.height) | dx=$($content.centerDeviationPx.deltaX), dy=$($content.centerDeviationPx.deltaY) | dw=$($content.sizeDeviationPx.deltaWidth), dh=$($content.sizeDeviationPx.deltaHeight) | $($content.passed) |"
        }
    }
    $markdown += @('', '## Region and mask rules', '', '| Name | x | y | width | height | Mask |', '| --- | ---: | ---: | ---: | ---: | --- |')
    foreach ($item in $reportCaptures) { foreach ($region in $item.regions) { $markdown += "| $($item.name):$($region.name) | $($region.x) | $($region.y) | $($region.width) | $($region.height) | $($region.mask) |" } }
    $markdown += @('', '## Bitmap Sprite usage', '', '| Sprite | Captures | Resources path | Source-relative path | Imported SHA-256 | Total occurrences |', '| --- | --- | --- | --- | --- | ---: |')
    foreach ($asset in $assets) { $markdown += "| $($asset.spriteName) | $($asset.captures -join ', ') | $($asset.resourcesPath) | $($asset.sourcePath) | $($asset.importedSha256) | $($asset.occurrenceCount) |" }
    $markdown += @('', '## Unity Text usage', '', 'These rows are aggregated from active, rendered UnityEngine.UI.Text instances in each captured page state. Dormant or non-rendered Text is intentionally absent. Runtime Font objects expose a font name but no provable original Resources path, so fontResourcePath and bitmapSourcePath are empty and hasBitmapSource is false.', '', '| Node | Text | Captures | Font name | Font Resources path | Has bitmap source | Bitmap source path | Total occurrences |', '| --- | --- | --- | --- | --- | --- | --- | ---: |')
    foreach ($textUsage in $unityTextUsage) { $markdown += "| $(ConvertTo-LanLobbyMarkdownCell $textUsage.node) | $(ConvertTo-LanLobbyMarkdownCell $textUsage.text) | $($textUsage.captures -join ', ') | $(ConvertTo-LanLobbyMarkdownCell $textUsage.fontName) | $(ConvertTo-LanLobbyMarkdownCell $textUsage.fontResourcePath) | $($textUsage.hasBitmapSource) | $(ConvertTo-LanLobbyMarkdownCell $textUsage.bitmapSourcePath) | $($textUsage.occurrenceCount) |" }
    $markdown += @('', '## Code-generated geometry usage', '', '| Node | Kind | Bitmap | Color | Captures | Total occurrences |', '| --- | --- | --- | --- | --- | ---: |')
    foreach ($geometry in $codeGeneratedGeometry) { $markdown += "| $($geometry.name) | $($geometry.kind) | $($geometry.isBitmap) | $($geometry.color) | $($geometry.captures -join ', ') | $($geometry.occurrenceCount) |" }
    Set-Content -LiteralPath (Join-Path $stagingDirectory 'visual-diff-report.md') -Value $markdown -Encoding UTF8
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory 'manifest.json') -Force
    Move-Item -LiteralPath $stagingDirectory -Destination $outputDirectory
    $stagingDirectory = $null
}
catch { if ($stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory)) { Remove-Item -LiteralPath $stagingDirectory -Force -Recurse }; throw }

Write-Output "LAN lobby visual difference report exported: $outputDirectory"
