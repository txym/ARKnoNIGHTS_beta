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
public sealed class LanLobbyBoundsMeasurement
{
    public bool Available { get; set; }
    public LanLobbyVisualBounds Bounds { get; set; }
    public string FailureReason { get; set; }
}
public sealed class FrameContinuity {
    public int QualifyingPixelCount { get; set; }
    public int CoveredAxisPixels { get; set; }
    public int AxisLength { get; set; }
    public double CoverageRatio { get; set; }
    public int LargestGapPixels { get; set; }
}
public sealed class FrameContrast
{
    public int FrameSampleCount { get; set; }
    public int BackgroundSampleCount { get; set; }
    public double FrameMedianLuma { get; set; }
    public double BackgroundMedianLuma { get; set; }
    public double ContrastDelta { get; set; }
    public bool Available { get; set; }
    public string FailureReason { get; set; }
}
public static class LanLobbyVisualDiff {
    sealed class CyanComponent {
        public int Count;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
    }
    static bool Inside(Rectangle r, int x, int y) { return x >= r.X && y >= r.Y && x < r.Right && y < r.Bottom; }
    static void ValidateSearch(Bitmap bitmap, Rectangle search) {
        if (bitmap == null) throw new ArgumentNullException("bitmap");
        if (search.X < 0 || search.Y < 0 || search.Right > bitmap.Width || search.Bottom > bitmap.Height ||
            search.Width <= 0 || search.Height <= 0) throw new ArgumentOutOfRangeException("search");
    }
    static bool IsCyan(Color pixel, int minimumGreen, int minimumGreenOverRed, int minimumBlueOverRed) {
        return pixel.A > 0 &&
            pixel.G >= minimumGreen &&
            pixel.G >= pixel.R + minimumGreenOverRed &&
            pixel.B >= pixel.R + minimumBlueOverRed;
    }
    static double Median(List<double> values) {
        var sorted = new List<double>(values);
        sorted.Sort();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2.0
            : sorted[middle];
    }
    static LanLobbyBoundsMeasurement Unavailable(string failureReason) {
        return new LanLobbyBoundsMeasurement { Available=false, Bounds=null, FailureReason=failureReason };
    }
    static LanLobbyBoundsMeasurement AvailableBounds(int minX, int minY, int maxX, int maxY) {
        if (maxX < minX || maxY < minY) return Unavailable("No qualifying decoded pixels found.");
        return new LanLobbyBoundsMeasurement {
            Available=true,
            Bounds=new LanLobbyVisualBounds { X=minX, Y=minY, Width=maxX-minX+1, Height=maxY-minY+1 },
            FailureReason=null
        };
    }
    public static LanLobbyBoundsMeasurement FindOrangeBounds(
        Bitmap bitmap, Rectangle search, int minimumRed, int minimumGreen, int maximumBlue, int minimumRedOverGreen)
    {
        ValidateSearch(bitmap, search);
        int minX=search.Right,minY=search.Bottom,maxX=-1,maxY=-1;
        for(int y=search.Y;y<search.Bottom;y++) for(int x=search.X;x<search.Right;x++) {
            Color pixel=bitmap.GetPixel(x,y);
            if(pixel.A == 0 || pixel.R < minimumRed || pixel.G < minimumGreen || pixel.B > maximumBlue ||
                pixel.R < pixel.G + minimumRedOverGreen) continue;
            minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
        }
        return AvailableBounds(minX,minY,maxX,maxY);
    }
    public static LanLobbyBoundsMeasurement FindLumaBounds(
        Bitmap bitmap, Rectangle search, int minimumLuminanceInclusive, int maximumLuminanceInclusive)
    {
        ValidateSearch(bitmap, search);
        if(minimumLuminanceInclusive < 0 || maximumLuminanceInclusive > 255 || minimumLuminanceInclusive > maximumLuminanceInclusive)
            throw new ArgumentOutOfRangeException("luminance");
        int minX=search.Right,minY=search.Bottom,maxX=-1,maxY=-1;
        for(int y=search.Y;y<search.Bottom;y++) for(int x=search.X;x<search.Right;x++) {
            Color pixel=bitmap.GetPixel(x,y);
            int luminance=(299*pixel.R+587*pixel.G+114*pixel.B)/1000;
            if(pixel.A == 0 || luminance < minimumLuminanceInclusive || luminance > maximumLuminanceInclusive) continue;
            minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
        }
        return AvailableBounds(minX,minY,maxX,maxY);
    }
    public static LanLobbyBoundsMeasurement FindNeutralBounds(
        Bitmap bitmap, Rectangle search, int minimumLuminanceInclusive, int maximumLuminanceInclusive, int maximumChannelSpread)
    {
        ValidateSearch(bitmap, search);
        if(minimumLuminanceInclusive < 0 || maximumLuminanceInclusive > 255 || minimumLuminanceInclusive > maximumLuminanceInclusive || maximumChannelSpread < 0)
            throw new ArgumentOutOfRangeException("neutralThresholds");
        int minX=search.Right,minY=search.Bottom,maxX=-1,maxY=-1;
        for(int y=search.Y;y<search.Bottom;y++) for(int x=search.X;x<search.Right;x++) {
            Color pixel=bitmap.GetPixel(x,y);
            int luminance=(299*pixel.R+587*pixel.G+114*pixel.B)/1000;
            int channelSpread=Math.Max(pixel.R,Math.Max(pixel.G,pixel.B))-Math.Min(pixel.R,Math.Min(pixel.G,pixel.B));
            if(pixel.A == 0 || luminance < minimumLuminanceInclusive || luminance > maximumLuminanceInclusive || channelSpread > maximumChannelSpread) continue;
            minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
        }
        return AvailableBounds(minX,minY,maxX,maxY);
    }
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
    public static LanLobbyVisualBounds FindCyanBounds(
        Bitmap bitmap,
        Rectangle search,
        int minimumGreen,
        int minimumGreenOverRed,
        int minimumBlueOverRed)
    {
        if (bitmap == null) throw new ArgumentNullException("bitmap");
        if (search.X < 0 || search.Y < 0 || search.Right > bitmap.Width || search.Bottom > bitmap.Height ||
            search.Width <= 0 || search.Height <= 0) throw new ArgumentOutOfRangeException("search");
        int minX=search.Right,minY=search.Bottom,maxX=-1,maxY=-1;
        for (int y=search.Y; y<search.Bottom; y++)
        for (int x=search.X; x<search.Right; x++)
        {
            Color pixel=bitmap.GetPixel(x,y);
            if (pixel.A == 0 || pixel.G < minimumGreen ||
                pixel.G < pixel.R + minimumGreenOverRed ||
                pixel.B < pixel.R + minimumBlueOverRed) continue;
            minX=Math.Min(minX,x); minY=Math.Min(minY,y);
            maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
        }
        if (maxX < minX || maxY < minY)
            throw new InvalidOperationException("No cyan visible pixels found in search rectangle " + search + ".");
        return new LanLobbyVisualBounds { X=minX, Y=minY, Width=maxX-minX+1, Height=maxY-minY+1 };
    }
    public static Rectangle FindLargestCyanComponentsBounds(
        Bitmap source,
        Rectangle search,
        int minimumGreen,
        int minimumGreenOverRed,
        int minimumBlueOverRed,
        int componentCount,
        int minimumComponentPixels)
    {
        ValidateSearch(source, search);
        if (componentCount <= 0) throw new ArgumentOutOfRangeException("componentCount");
        if (minimumComponentPixels <= 0) throw new ArgumentOutOfRangeException("minimumComponentPixels");
        var qualifying = new bool[search.Width, search.Height];
        for (int y=0; y<search.Height; y++)
        for (int x=0; x<search.Width; x++)
            qualifying[x,y] = IsCyan(source.GetPixel(search.X+x,search.Y+y), minimumGreen, minimumGreenOverRed, minimumBlueOverRed);

        var components = new List<CyanComponent>();
        var queue = new Queue<Point>();
        for (int seedY=0; seedY<search.Height; seedY++)
        for (int seedX=0; seedX<search.Width; seedX++)
        {
            if (!qualifying[seedX,seedY]) continue;
            qualifying[seedX,seedY]=false;
            queue.Enqueue(new Point(seedX,seedY));
            var component = new CyanComponent { MinX=seedX, MinY=seedY, MaxX=seedX, MaxY=seedY };
            while (queue.Count > 0)
            {
                Point point=queue.Dequeue();
                component.Count++;
                component.MinX=Math.Min(component.MinX,point.X);
                component.MinY=Math.Min(component.MinY,point.Y);
                component.MaxX=Math.Max(component.MaxX,point.X);
                component.MaxY=Math.Max(component.MaxY,point.Y);
                for (int dy=-1; dy<=1; dy++)
                for (int dx=-1; dx<=1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nextX=point.X+dx,nextY=point.Y+dy;
                    if (nextX < 0 || nextY < 0 || nextX >= search.Width || nextY >= search.Height ||
                        !qualifying[nextX,nextY]) continue;
                    qualifying[nextX,nextY]=false;
                    queue.Enqueue(new Point(nextX,nextY));
                }
            }
            if (component.Count >= minimumComponentPixels) components.Add(component);
        }
        components.Sort(delegate(CyanComponent left, CyanComponent right) {
            int byCount=right.Count.CompareTo(left.Count);
            if (byCount != 0) return byCount;
            int byY=left.MinY.CompareTo(right.MinY);
            return byY != 0 ? byY : left.MinX.CompareTo(right.MinX);
        });
        if (components.Count < componentCount)
            throw new InvalidOperationException(
                "Found " + components.Count + " cyan components with at least " + minimumComponentPixels +
                " pixels in search rectangle " + search + "; " + componentCount + " required.");

        int minX=search.Right,minY=search.Bottom,maxX=-1,maxY=-1;
        for (int i=0; i<componentCount; i++)
        {
            CyanComponent component=components[i];
            minX=Math.Min(minX,search.X+component.MinX);
            minY=Math.Min(minY,search.Y+component.MinY);
            maxX=Math.Max(maxX,search.X+component.MaxX);
            maxY=Math.Max(maxY,search.Y+component.MaxY);
        }
        return new Rectangle(minX,minY,maxX-minX+1,maxY-minY+1);
    }
    public static FrameContinuity MeasureCyanContinuity(
        Bitmap source,
        Rectangle search,
        bool horizontal,
        int minimumGreen,
        int minimumGreenOverRed,
        int minimumBlueOverRed)
    {
        ValidateSearch(source, search);
        int axisLength=horizontal ? search.Width : search.Height;
        var covered=new bool[axisLength];
        int qualifyingPixelCount=0;
        for (int y=search.Y; y<search.Bottom; y++)
        for (int x=search.X; x<search.Right; x++)
        {
            if (!IsCyan(source.GetPixel(x,y), minimumGreen, minimumGreenOverRed, minimumBlueOverRed)) continue;
            qualifyingPixelCount++;
            covered[horizontal ? x-search.X : y-search.Y]=true;
        }
        int coveredAxisPixels=0,largestGapPixels=0,currentGap=0;
        for (int i=0; i<axisLength; i++)
        {
            if (covered[i])
            {
                coveredAxisPixels++;
                currentGap=0;
            }
            else
            {
                currentGap++;
                largestGapPixels=Math.Max(largestGapPixels,currentGap);
            }
        }
        return new FrameContinuity {
            QualifyingPixelCount=qualifyingPixelCount,
            CoveredAxisPixels=coveredAxisPixels,
            AxisLength=axisLength,
            CoverageRatio=(double)coveredAxisPixels/axisLength,
            LargestGapPixels=largestGapPixels
        };
    }
    public static FrameContrast MeasureFrameLumaContrast(
        Bitmap source,
        Rectangle frameSearch,
        Rectangle backgroundSearch,
        int minimumGreen,
        int minimumGreenOverRed,
        int minimumBlueOverRed)
    {
        ValidateSearch(source, frameSearch);
        ValidateSearch(source, backgroundSearch);
        var frameLuma = new List<double>();
        var backgroundLuma = new List<double>();
        for (int y=frameSearch.Y; y<frameSearch.Bottom; y++)
        for (int x=frameSearch.X; x<frameSearch.Right; x++)
        {
            Color pixel=source.GetPixel(x,y);
            if (!IsCyan(pixel, minimumGreen, minimumGreenOverRed, minimumBlueOverRed)) continue;
            frameLuma.Add(0.2126*pixel.R + 0.7152*pixel.G + 0.0722*pixel.B);
        }
        for (int y=backgroundSearch.Y; y<backgroundSearch.Bottom; y++)
        for (int x=backgroundSearch.X; x<backgroundSearch.Right; x++)
        {
            Color pixel=source.GetPixel(x,y);
            if (pixel.A == 0) continue;
            backgroundLuma.Add(0.2126*pixel.R + 0.7152*pixel.G + 0.0722*pixel.B);
        }
        var result = new FrameContrast {
            FrameSampleCount=frameLuma.Count,
            BackgroundSampleCount=backgroundLuma.Count
        };
        if (frameLuma.Count == 0)
        {
            result.Available=false;
            result.FailureReason="No qualifying cyan frame samples found.";
            return result;
        }
        if (backgroundLuma.Count == 0)
        {
            result.Available=false;
            result.FailureReason="No non-transparent background samples found.";
            return result;
        }
        result.FrameMedianLuma=Median(frameLuma);
        result.BackgroundMedianLuma=Median(backgroundLuma);
        result.ContrastDelta=result.FrameMedianLuma-result.BackgroundMedianLuma;
        result.Available=true;
        return result;
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
$homeCreateDecoration = @{
  name='home-create-decoration'
  approvedTarget=@{x=1296;y=252;width=390;height=179}
  reference=@{x=1419;y=268;width=427;height=191}
}
$homeJoinDecoration = @{
  name='home-join-decoration'
  approvedTarget=@{x=1154;y=596;width=717;height=280}
  reference=@{x=1257;y=635;width=763;height=297}
}
$joinDecorationContentSpecs = @(
  @{ name='logo'; measurement='orange'; search=@{x=80;y=54;width=140;height=42}; expected=@{x=91;y=64;width=118;height=20}; tolerance=2; thresholds=@{minimumRed=200;minimumGreen=100;maximumBlue=80;minimumRedOverGreen=40} },
  @{ name='text-01'; measurement='orange'; search=@{x=385;y=45;width=95;height=35}; expected=@{x=391;y=56;width=65;height=8}; tolerance=2; thresholds=@{minimumRed=200;minimumGreen=100;maximumBlue=80;minimumRedOverGreen=40} },
  @{ name='text-02'; measurement='orange'; search=@{x=515;y=50;width=110;height=35}; expected=@{x=526;y=62;width=89;height=11}; tolerance=2; thresholds=@{minimumRed=200;minimumGreen=100;maximumBlue=80;minimumRedOverGreen=40} },
  @{ name='triangle'; measurement='orange'; search=@{x=325;y=35;width=55;height=31}; expected=@{x=338;y=47;width=30;height=17}; tolerance=2; thresholds=@{minimumRed=200;minimumGreen=100;maximumBlue=80;minimumRedOverGreen=40} },
  @{ name='central-blank'; measurement='orange'; search=@{x=310;y=66;width=85;height=79}; expected=@{x=323;y=68;width=60;height=61}; tolerance=2; thresholds=@{minimumRed=200;minimumGreen=100;maximumBlue=80;minimumRedOverGreen=40} },
  @{ name='block-bank'; measurement='luma'; search=@{x=35;y=97;width=660;height=110}; expected=@{x=45;y=107;width=639;height=89}; tolerance=4; thresholds=@{minimumLuminanceInclusive=200;maximumLuminanceInclusive=255} },
  @{ name='input'; measurement='neutral'; search=@{x=105;y=194;width=510;height=75}; expected=@{x=115;y=204;width=482;height=60}; tolerance=2; thresholds=@{minimumLuminanceInclusive=90;maximumLuminanceInclusive=140;maximumChannelSpread=0} }
)
$createDecorationContentSpecs = @(
  @{name='start-room'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=145;y=5;width=100;height=24}; expected=@{x=153;y=13;width=84;height=9}},
  @{name='dot-top-left'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=116;y=15;width=24;height=27}; expected=@{x=118;y=18;width=17;height=17}},
  @{name='dot-top-right'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=249;y=15;width=24;height=27}; expected=@{x=253;y=19;width=17;height=16}},
  @{name='middle-icon'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=150;y=34;width=92;height=100}; expected=@{x=152;y=41;width=87;height=86}},
  @{name='left-bracket'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=124;y=50;width=28;height=75}; expected=@{x=130;y=58;width=18;height=54}},
  @{name='right-bracket'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=240;y=50;width=25;height=75}; expected=@{x=243;y=58;width=18;height=54}},
  @{name='text-01'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=145;y=128;width=100;height=19}; expected=@{x=152;y=134;width=88;height=13}},
  @{name='text-02'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=160;y=147;width=75;height=10}; expected=@{x=164;y=147;width=66;height=7}},
  @{name='dot-bottom-left'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=116;y=152;width=24;height=27}; expected=@{x=118;y=155;width=16;height=17}},
  @{name='dot-bottom-right'; mode='all-cyan-pixels'; threshold=35; greenOverRed=8; blueOverRed=5; search=@{x=249;y=152;width=24;height=27}; expected=@{x=253;y=155;width=17;height=17}}
)
$homeCreateFrame = @{
  name='home-create-frame'
  approvedTarget=@{x=1154;y=224;width=717;height=374}
  reference=@{x=1257;y=239;width=763;height=397}
}
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

function Resize-LanLobbyBitmap(
    [Drawing.Bitmap] $Source,
    [int] $Width,
    [int] $Height,
    [Drawing.Drawing2D.InterpolationMode] $InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear)
{
    $result = New-Object Drawing.Bitmap $Width, $Height
    $graphics = [Drawing.Graphics]::FromImage($result)
    try { $graphics.InterpolationMode = $InterpolationMode; $graphics.DrawImage($Source, 0, 0, $Width, $Height) }
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

function Test-LanLobbyJsonNumber($Value)
{
    return $Value -is [sbyte] -or
        $Value -is [byte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64] -or
        $Value -is [single] -or
        $Value -is [double] -or
        $Value -is [decimal]
}

function Assert-LanLobbyCapturedRectangleSchema($Record, [string] $Label)
{
    foreach ($propertyName in @('coordinateOrigin','unit','x','y','width','height'))
    {
        if ($null -eq $Record.PSObject.Properties[$propertyName])
        {
            throw "$Label is missing required capture field '$propertyName'."
        }
    }
    if ([string]$Record.coordinateOrigin -cne 'screen-bottom-left' -or [string]$Record.unit -cne 'px')
    {
        throw "$Label must declare coordinateOrigin=screen-bottom-left and unit=px."
    }
    if (-not (Test-LanLobbyJsonNumber $Record.x) -or
        -not (Test-LanLobbyJsonNumber $Record.y) -or
        -not (Test-LanLobbyJsonNumber $Record.width) -or
        -not (Test-LanLobbyJsonNumber $Record.height))
    {
        throw "$Label must declare numeric x, y, width, and height."
    }
    $numbers = @([double]$Record.x, [double]$Record.y, [double]$Record.width, [double]$Record.height)
    if (@($numbers | Where-Object { [double]::IsNaN($_) -or [double]::IsInfinity($_) }).Count -gt 0 -or
        $numbers[2] -le 0 -or $numbers[3] -le 0)
    {
        throw "$Label must declare finite coordinates and positive dimensions."
    }
}

function Assert-LanLobbyCaptureEvidenceSchema($Manifest)
{
    foreach ($capture in @($Manifest.captures))
    {
        foreach ($collectionName in @('spriteSources','codeNativeGeometry'))
        {
            if ($null -eq $capture.PSObject.Properties[$collectionName] -or $null -eq $capture.$collectionName)
            {
                throw "Capture '$($capture.name)' is missing required '$collectionName' evidence."
            }
        }

        foreach ($sprite in @($capture.spriteSources))
        {
            foreach ($propertyName in @('node','spriteName','sourcePath'))
            {
                if ($null -eq $sprite.PSObject.Properties[$propertyName] -or
                    [string]::IsNullOrWhiteSpace([string]$sprite.$propertyName))
                {
                    throw "Capture '$($capture.name)' SpriteSource is missing required capture field '$propertyName'."
                }
            }
            Assert-LanLobbyCapturedRectangleSchema $sprite "SpriteSource '$($sprite.node)'"
        }

        foreach ($geometry in @($capture.codeNativeGeometry))
        {
            foreach ($propertyName in @('name','kind','isBitmap','color','raycastTarget'))
            {
                if ($null -eq $geometry.PSObject.Properties[$propertyName])
                {
                    throw "Capture '$($capture.name)' CodeNativeGeometry is missing required capture field '$propertyName'."
                }
            }
            if ($null -ne $geometry.PSObject.Properties['spriteName'])
            {
                throw "CodeNativeGeometry '$($geometry.name)' must not declare spriteName."
            }
            if ([string]::IsNullOrWhiteSpace([string]$geometry.name) -or
                [string]$geometry.kind -cne 'code-native-geometry' -or
                $geometry.isBitmap -isnot [bool] -or
                $geometry.isBitmap)
            {
                throw "CodeNativeGeometry '$($geometry.name)' must declare kind=code-native-geometry and isBitmap=false."
            }
            if ([string]::IsNullOrWhiteSpace([string]$geometry.color))
            {
                throw "CodeNativeGeometry '$($geometry.name)' must declare a nonempty color."
            }
            if ($geometry.raycastTarget -isnot [bool])
            {
                throw "CodeNativeGeometry '$($geometry.name)' must declare a Boolean raycastTarget."
            }
            if ([string]$geometry.name -like 'LanLobbyRoot/Home/RoomSelect/Join/*' -and $geometry.raycastTarget)
            {
                throw "Join geometry '$($geometry.name)' must declare raycastTarget=false."
            }
            Assert-LanLobbyCapturedRectangleSchema $geometry "CodeNativeGeometry '$($geometry.name)'"
        }
    }
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

function Convert-CapturedJoinGeometryRectangle($Capture, $Geometry)
{
    foreach ($propertyName in @('name','kind','isBitmap','color','coordinateOrigin','unit','raycastTarget','x','y','width','height'))
    {
        if ($null -eq $Geometry.PSObject.Properties[$propertyName])
        {
            throw "Join geometry is missing required capture field '$propertyName'."
        }
    }
    if ($null -ne $Geometry.PSObject.Properties['spriteName'])
    {
        throw "Join geometry '$($Geometry.name)' must not declare spriteName."
    }
    if ([string]::IsNullOrWhiteSpace([string]$Geometry.name) -or
        [string]$Geometry.kind -cne 'code-native-geometry' -or
        $Geometry.isBitmap -isnot [bool] -or
        $Geometry.isBitmap)
    {
        throw "Join geometry '$($Geometry.name)' must declare kind=code-native-geometry and isBitmap=false."
    }
    if ([string]$Geometry.coordinateOrigin -cne 'screen-bottom-left' -or [string]$Geometry.unit -cne 'px')
    {
        throw "Join geometry '$($Geometry.name)' must declare coordinateOrigin=screen-bottom-left and unit=px."
    }
    if ($Geometry.raycastTarget -isnot [bool] -or $Geometry.raycastTarget)
    {
        throw "Join geometry '$($Geometry.name)' must declare raycastTarget=false."
    }
    if ([string]::IsNullOrWhiteSpace([string]$Geometry.color))
    {
        throw "Join geometry '$($Geometry.name)' must declare a nonempty color."
    }
    try { $numbers = @([double]$Geometry.x, [double]$Geometry.y, [double]$Geometry.width, [double]$Geometry.height) }
    catch { throw "Join geometry '$($Geometry.name)' has non-numeric captured geometry." }
    if (@($numbers | Where-Object { [double]::IsNaN($_) -or [double]::IsInfinity($_) }).Count -gt 0 -or
        $numbers[0] -lt 0 -or $numbers[1] -lt 0 -or $numbers[2] -le 0 -or $numbers[3] -le 0 -or
        ($numbers[0] + $numbers[2]) -gt [double]$Capture.width -or ($numbers[1] + $numbers[3]) -gt [double]$Capture.height)
    {
        throw "Join geometry $($Geometry.name) is invalid or outside the captured screen."
    }
    return [pscustomobject][ordered]@{
        coordinateOrigin='screen-top-left'
        unit='px'
        x=[int][Math]::Round($numbers[0] * 1920.0 / [double]$Capture.width, [MidpointRounding]::AwayFromZero)
        y=[int][Math]::Round(([double]$Capture.height - ($numbers[1] + $numbers[3])) * 1080.0 / [double]$Capture.height, [MidpointRounding]::AwayFromZero)
        width=[int][Math]::Round($numbers[2] * 1920.0 / [double]$Capture.width, [MidpointRounding]::AwayFromZero)
        height=[int][Math]::Round($numbers[3] * 1080.0 / [double]$Capture.height, [MidpointRounding]::AwayFromZero)
    }
}

function Convert-CapturedJoinGraphicRectangle($Capture, $Sprite)
{
    $result = [ordered]@{ name=[string]$Sprite.node; spriteName=[string]$Sprite.spriteName; available=$false; failureReason=$null; x=$null; y=$null; width=$null; height=$null }
    try
    {
        foreach ($propertyName in @('node','spriteName','sourcePath','coordinateOrigin','unit','x','y','width','height'))
        {
            if ($null -eq $Sprite.PSObject.Properties[$propertyName]) { throw "is missing required capture field '$propertyName'" }
        }
        if ([string]::IsNullOrWhiteSpace([string]$Sprite.node) -or
            [string]::IsNullOrWhiteSpace([string]$Sprite.spriteName) -or
            [string]::IsNullOrWhiteSpace([string]$Sprite.sourcePath))
        {
            throw 'must declare nonempty node, spriteName, and sourcePath'
        }
        if ([string]$Sprite.coordinateOrigin -cne 'screen-bottom-left' -or [string]$Sprite.unit -cne 'px') { throw 'must declare coordinateOrigin=screen-bottom-left and unit=px' }
        try { $numbers = @([double]$Sprite.x, [double]$Sprite.y, [double]$Sprite.width, [double]$Sprite.height) }
        catch { throw 'has non-numeric captured geometry' }
        if (@($numbers | Where-Object { [double]::IsNaN($_) -or [double]::IsInfinity($_) }).Count -gt 0 -or
            $numbers[0] -lt 0 -or $numbers[1] -lt 0 -or $numbers[2] -le 0 -or $numbers[3] -le 0 -or
            ($numbers[0] + $numbers[2]) -gt [double]$Capture.width -or ($numbers[1] + $numbers[3]) -gt [double]$Capture.height)
        {
            throw 'has an invalid or out-of-bounds visible rectangle'
        }
        $result.available=$true
        $result.x=[int][Math]::Round($numbers[0] * 1920.0 / [double]$Capture.width, [MidpointRounding]::AwayFromZero)
        $result.y=[int][Math]::Round(([double]$Capture.height - ($numbers[1] + $numbers[3])) * 1080.0 / [double]$Capture.height, [MidpointRounding]::AwayFromZero)
        $result.width=[int][Math]::Round($numbers[2] * 1920.0 / [double]$Capture.width, [MidpointRounding]::AwayFromZero)
        $result.height=[int][Math]::Round($numbers[3] * 1080.0 / [double]$Capture.height, [MidpointRounding]::AwayFromZero)
    }
    catch { $result.failureReason=$_.Exception.Message }
    return [pscustomobject]$result
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
Assert-LanLobbyCaptureEvidenceSchema $manifest
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
$requiredJoinSpriteCounts = [ordered]@{
    join_icon = 2
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
$joinSpriteInventory = @(
    foreach ($spriteName in $requiredJoinSpriteCounts.Keys)
    {
        $matches = @($assets | Where-Object spriteName -eq $spriteName)
        $expectedSource = '[uc]autochessouter/' + $spriteName + '.png'
        $actualOccurrences = if ($matches.Count -eq 1) { [int]$matches[0].occurrenceCount } else { 0 }
        $sourcePath = if ($matches.Count -eq 1) { [string]$matches[0].sourcePath } else { $null }
        [pscustomobject][ordered]@{
            spriteName=$spriteName
            expectedOccurrenceCount=[int]$requiredJoinSpriteCounts[$spriteName]
            actualOccurrenceCount=$actualOccurrences
            expectedSourcePath=$expectedSource
            sourcePath=$sourcePath
            passed=($matches.Count -eq 1 -and $actualOccurrences -eq $requiredJoinSpriteCounts[$spriteName] -and $sourcePath -ceq $expectedSource)
        }
    }
)
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
$createDecorationReport = $null
$joinDecorationReport = $null
$createFrameReport = $null
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
                passed =
                    ($actualSpec.x -eq $spec.approvedTarget.x) -and
                    ($actualSpec.y -eq $spec.approvedTarget.y) -and
                    ($actualCrop.Width -eq $locallyResizedReferenceCrop.Width) -and
                    ($actualCrop.Height -eq $locallyResizedReferenceCrop.Height) -and
                    (@($contentVisuals | Where-Object { -not $_.passed }).Count -eq 0)
            }
            $actualCrop.Save((Join-Path $stagingDirectory ($spec.name + '-actual.png')), [Drawing.Imaging.ImageFormat]::Png)
            $locallyResizedReferenceCrop.Save((Join-Path $stagingDirectory ($spec.name + '-reference.png')), [Drawing.Imaging.ImageFormat]::Png)
            $overlay.Save((Join-Path $stagingDirectory ($spec.name + '-overlay.png')), [Drawing.Imaging.ImageFormat]::Png)
            $heatmap.Save((Join-Path $stagingDirectory ($spec.name + '-heatmap.png')), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { if ($actual) { $actual.Dispose() }; if ($nativeReference) { $nativeReference.Dispose() }; if ($actualCrop) { $actualCrop.Dispose() }; if ($nativeReferenceCrop) { $nativeReferenceCrop.Dispose() }; if ($locallyResizedReferenceCrop) { $locallyResizedReferenceCrop.Dispose() }; if ($comparisonReferenceCrop) { $comparisonReferenceCrop.Dispose() }; if ($overlay) { $overlay.Dispose() }; if ($heatmap) { $heatmap.Dispose() } }
    }
    $actual = $null
    $nativeReference = $null
    $actualCrop = $null
    $nativeReferenceCrop = $null
    $locallyResizedReferenceCrop = $null
    $overlay = $null
    $heatmap = $null
    try
    {
        $actual = [Drawing.Bitmap]::FromFile($homeCapture.path)
        $nativeReference = [Drawing.Bitmap]::FromFile($referenceHome)
        $actualSpec = $homeCreateDecoration.approvedTarget
        $actualRectangle = New-Object Drawing.Rectangle $actualSpec.x, $actualSpec.y, $actualSpec.width, $actualSpec.height
        $scaledReference = Convert-ActionReferenceRectangle $homeCreateDecoration.reference $nativeReference.Width $nativeReference.Height
        $actualCrop = New-LanLobbyBitmapCrop $actual $actualRectangle
        $nativeReferenceCrop = New-LanLobbyBitmapCrop $nativeReference $scaledReference
        $locallyResizedReferenceCrop = Resize-LanLobbyBitmap $nativeReferenceCrop $actualSpec.width $actualSpec.height ([Drawing.Drawing2D.InterpolationMode]::NearestNeighbor)
        $overlay = New-LanLobbyActionOverlay $actualCrop $locallyResizedReferenceCrop
        $heatmap = New-Object Drawing.Bitmap $actualCrop.Width, $actualCrop.Height
        $fullCrop = New-Object Drawing.Rectangle 0,0,$actualCrop.Width,$actualCrop.Height
        [long]$maskedPixels = 0
        $metric = ([LanLobbyVisualDiff]::Compare($actualCrop, $locallyResizedReferenceCrop, [Drawing.Rectangle[]]@(), [Drawing.Rectangle[]]@($fullCrop), [bool[]]@($false), $heatmap, [ref]$maskedPixels))[0]
        $components = @()
        foreach ($contentSpec in $createDecorationContentSpecs)
        {
            $search = New-Object Drawing.Rectangle $contentSpec.search.x, $contentSpec.search.y, $contentSpec.search.width, $contentSpec.search.height
            $actualBounds = $null
            $referenceBounds = $null
            $measurementAvailable = $false
            $measurementError = $null
            try
            {
                if ($contentSpec.mode -eq 'two-largest-components')
                {
                    $referenceBounds = [LanLobbyVisualDiff]::FindLargestCyanComponentsBounds(
                        $locallyResizedReferenceCrop, $search, $contentSpec.threshold, $contentSpec.greenOverRed, $contentSpec.blueOverRed,
                        $contentSpec.componentCount, $contentSpec.minimumComponentPixels)
                    $actualBounds = [LanLobbyVisualDiff]::FindLargestCyanComponentsBounds(
                        $actualCrop, $search, $contentSpec.threshold, $contentSpec.greenOverRed, $contentSpec.blueOverRed,
                        $contentSpec.componentCount, $contentSpec.minimumComponentPixels)
                }
                else
                {
                    $referenceBounds = [LanLobbyVisualDiff]::FindCyanBounds(
                        $locallyResizedReferenceCrop, $search, $contentSpec.threshold, $contentSpec.greenOverRed, $contentSpec.blueOverRed)
                    $actualBounds = [LanLobbyVisualDiff]::FindCyanBounds(
                        $actualCrop, $search, $contentSpec.threshold, $contentSpec.greenOverRed, $contentSpec.blueOverRed)
                }
                $measurementAvailable = $true
            }
            catch
            {
                $candidate = $_.Exception
                while ($candidate -and -not ($candidate -is [InvalidOperationException])) { $candidate = $candidate.InnerException }
                if (-not $candidate) { throw }
                $measurementError = $candidate.Message
            }
            $expected = $contentSpec.expected
            $centerDeviation = $null
            $sizeDeviation = $null
            $passed = $false
            if ($measurementAvailable)
            {
                $actualCenterX = $actualBounds.X + ($actualBounds.Width - 1) / 2.0
                $actualCenterY = $actualBounds.Y + ($actualBounds.Height - 1) / 2.0
                $expectedCenterX = $expected.x + ($expected.width - 1) / 2.0
                $expectedCenterY = $expected.y + ($expected.height - 1) / 2.0
                $centerDeltaX = $actualCenterX - $expectedCenterX
                $centerDeltaY = $actualCenterY - $expectedCenterY
                $widthDelta = $actualBounds.Width - $expected.width
                $heightDelta = $actualBounds.Height - $expected.height
                $centerDeviation = [ordered]@{ unit='px'; deltaX=$centerDeltaX; deltaY=$centerDeltaY }
                $sizeDeviation = [ordered]@{ unit='px'; deltaWidth=$widthDelta; deltaHeight=$heightDelta }
                $passed = [Math]::Abs($centerDeltaX) -le 1 `
                    -and [Math]::Abs($centerDeltaY) -le 1 `
                    -and [Math]::Abs($widthDelta) -le 2 `
                    -and [Math]::Abs($heightDelta) -le 2
            }
            $components += [pscustomobject][ordered]@{
                name = $contentSpec.name
                measurementMode = $contentSpec.mode
                measurementAvailable = $measurementAvailable
                measurementError = $measurementError
                thresholdMinimumGreen = $contentSpec.threshold
                minimumGreenOverRed = $contentSpec.greenOverRed
                minimumBlueOverRed = $contentSpec.blueOverRed
                componentCount = $(if ($contentSpec.mode -eq 'two-largest-components') { $contentSpec.componentCount } else { $null })
                minimumComponentPixels = $(if ($contentSpec.mode -eq 'two-largest-components') { $contentSpec.minimumComponentPixels } else { $null })
                expectedBounds = [ordered]@{ x=$expected.x; y=$expected.y; width=$expected.width; height=$expected.height }
                referenceBounds = $(if ($referenceBounds) { ConvertTo-LanLobbyBoundsObject $referenceBounds } else { $null })
                actualBounds = $(if ($actualBounds) { ConvertTo-LanLobbyBoundsObject $actualBounds } else { $null })
                centerDeviationPx = $centerDeviation
                sizeDeviationPx = $sizeDeviation
                passed = $passed
            }
        }
        $createDecorationReport = [pscustomobject][ordered]@{
            name = $homeCreateDecoration.name
            capture = 'home'
            actualRect = [ordered]@{ coordinateOrigin='screen-top-left'; unit='px'; x=$actualSpec.x; y=$actualSpec.y; width=$actualSpec.width; height=$actualSpec.height }
            referenceRect = [ordered]@{ x=$scaledReference.X; y=$scaledReference.Y; width=$scaledReference.Width; height=$scaledReference.Height }
            referenceMeasurementCanvas = [ordered]@{ width=$figure9MeasurementSize.width; height=$figure9MeasurementSize.height }
            locallyResizedReferenceSizePx = [ordered]@{ unit='px'; width=$locallyResizedReferenceCrop.Width; height=$locallyResizedReferenceCrop.Height }
            comparedPixels = $metric.ComparedPixels
            pixelDifferenceRatio = [double]$metric.DifferentPixels / $metric.ComparedPixels
            averageAbsoluteRgbError = [double]$metric.ErrorSum / ($metric.ComparedPixels * 3)
            acceptanceRole = 'informational'
            blocksCreateFrameAcceptance = $false
            components = $components
        }
        $actualCrop.Save((Join-Path $stagingDirectory ($homeCreateDecoration.name + '-actual.png')), [Drawing.Imaging.ImageFormat]::Png)
        $locallyResizedReferenceCrop.Save((Join-Path $stagingDirectory ($homeCreateDecoration.name + '-reference.png')), [Drawing.Imaging.ImageFormat]::Png)
        $overlay.Save((Join-Path $stagingDirectory ($homeCreateDecoration.name + '-overlay.png')), [Drawing.Imaging.ImageFormat]::Png)
        $heatmap.Save((Join-Path $stagingDirectory ($homeCreateDecoration.name + '-heatmap.png')), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { if ($actual) { $actual.Dispose() }; if ($nativeReference) { $nativeReference.Dispose() }; if ($actualCrop) { $actualCrop.Dispose() }; if ($nativeReferenceCrop) { $nativeReferenceCrop.Dispose() }; if ($locallyResizedReferenceCrop) { $locallyResizedReferenceCrop.Dispose() }; if ($overlay) { $overlay.Dispose() }; if ($heatmap) { $heatmap.Dispose() } }
    $actual = $null
    $nativeReference = $null
    $actualCrop = $null
    $nativeReferenceCrop = $null
    $locallyResizedReferenceCrop = $null
    $overlay = $null
    $heatmap = $null
    try
    {
        $actual = [Drawing.Bitmap]::FromFile($homeCapture.path)
        $nativeReference = [Drawing.Bitmap]::FromFile($referenceHome)
        $actualSpec = $homeJoinDecoration.approvedTarget
        $actualRectangle = New-Object Drawing.Rectangle $actualSpec.x, $actualSpec.y, $actualSpec.width, $actualSpec.height
        $scaledReference = Convert-ActionReferenceRectangle $homeJoinDecoration.reference $nativeReference.Width $nativeReference.Height
        $actualCrop = New-LanLobbyBitmapCrop $actual $actualRectangle
        $nativeReferenceCrop = New-LanLobbyBitmapCrop $nativeReference $scaledReference
        $locallyResizedReferenceCrop = Resize-LanLobbyBitmap $nativeReferenceCrop $actualSpec.width $actualSpec.height ([Drawing.Drawing2D.InterpolationMode]::NearestNeighbor)
        $overlay = New-LanLobbyActionOverlay $actualCrop $locallyResizedReferenceCrop
        $heatmap = New-Object Drawing.Bitmap $actualCrop.Width, $actualCrop.Height
        $fullCrop = New-Object Drawing.Rectangle 0,0,$actualCrop.Width,$actualCrop.Height
        [long]$maskedPixels = 0
        $metric = ([LanLobbyVisualDiff]::Compare($actualCrop, $locallyResizedReferenceCrop, [Drawing.Rectangle[]]@(), [Drawing.Rectangle[]]@($fullCrop), [bool[]]@($false), $heatmap, [ref]$maskedPixels))[0]
        $components = @()
        foreach ($contentSpec in $joinDecorationContentSpecs)
        {
            $search = New-Object Drawing.Rectangle $contentSpec.search.x, $contentSpec.search.y, $contentSpec.search.width, $contentSpec.search.height
            $thresholds = [ordered]@{}
            foreach ($key in $contentSpec.thresholds.Keys) { $thresholds[$key] = $contentSpec.thresholds[$key] }
            $measure = {
                param([Drawing.Bitmap] $bitmap)
                switch ($contentSpec.measurement)
                {
                    'orange' { return [LanLobbyVisualDiff]::FindOrangeBounds($bitmap, $search, $contentSpec.thresholds.minimumRed, $contentSpec.thresholds.minimumGreen, $contentSpec.thresholds.maximumBlue, $contentSpec.thresholds.minimumRedOverGreen) }
                    'luma' { return [LanLobbyVisualDiff]::FindLumaBounds($bitmap, $search, $contentSpec.thresholds.minimumLuminanceInclusive, $contentSpec.thresholds.maximumLuminanceInclusive) }
                    'neutral' { return [LanLobbyVisualDiff]::FindNeutralBounds($bitmap, $search, $contentSpec.thresholds.minimumLuminanceInclusive, $contentSpec.thresholds.maximumLuminanceInclusive, $contentSpec.thresholds.maximumChannelSpread) }
                    default { throw "Unsupported Join decoration measurement: $($contentSpec.measurement)" }
                }
            }
            $referenceMeasurement = & $measure $locallyResizedReferenceCrop
            $actualMeasurement = & $measure $actualCrop
            $measurementAvailable = $referenceMeasurement.Available -and $actualMeasurement.Available
            $measurementError = @($referenceMeasurement.FailureReason, $actualMeasurement.FailureReason | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ' / '
            $expected = $contentSpec.expected
            $centerDeviation = $null
            $sizeDeviation = $null
            $passed = $false
            if ($measurementAvailable)
            {
                $actualBounds = $actualMeasurement.Bounds
                $actualCenterX = $actualBounds.X + ($actualBounds.Width - 1) / 2.0
                $actualCenterY = $actualBounds.Y + ($actualBounds.Height - 1) / 2.0
                $expectedCenterX = $expected.x + ($expected.width - 1) / 2.0
                $expectedCenterY = $expected.y + ($expected.height - 1) / 2.0
                $centerDeviation = [ordered]@{ unit='px'; deltaX=($actualCenterX - $expectedCenterX); deltaY=($actualCenterY - $expectedCenterY) }
                $sizeDeviation = [ordered]@{ unit='px'; deltaWidth=($actualBounds.Width - $expected.width); deltaHeight=($actualBounds.Height - $expected.height) }
                $passed = [Math]::Abs($centerDeviation.deltaX) -le $contentSpec.tolerance -and
                    [Math]::Abs($centerDeviation.deltaY) -le $contentSpec.tolerance -and
                    [Math]::Abs($sizeDeviation.deltaWidth) -le $contentSpec.tolerance -and
                    [Math]::Abs($sizeDeviation.deltaHeight) -le $contentSpec.tolerance
            }
            $components += [pscustomobject][ordered]@{
                name=$contentSpec.name
                measurement=$contentSpec.measurement
                search=[ordered]@{ x=$contentSpec.search.x; y=$contentSpec.search.y; width=$contentSpec.search.width; height=$contentSpec.search.height }
                thresholds=[pscustomobject]$thresholds
                tolerancePx=$contentSpec.tolerance
                measurementAvailable=$measurementAvailable
                measurementError=$(if ($measurementAvailable) { $null } else { $measurementError })
                expectedBounds=[ordered]@{ x=$expected.x; y=$expected.y; width=$expected.width; height=$expected.height }
                referenceBounds=$(if ($referenceMeasurement.Available) { ConvertTo-LanLobbyBoundsObject $referenceMeasurement.Bounds } else { $null })
                actualBounds=$(if ($actualMeasurement.Available) { ConvertTo-LanLobbyBoundsObject $actualMeasurement.Bounds } else { $null })
                centerDeviationPx=$centerDeviation
                sizeDeviationPx=$sizeDeviation
                passed=$passed
            }
        }
        $joinGeometryPrefix = 'LanLobbyRoot/Home/RoomSelect/Join/'
        $joinGeometry = @($homeCapture.codeNativeGeometry | Where-Object { $_ -and [string]$_.name -like ($joinGeometryPrefix + '*') })
        $joinGeometryRects = @(
            foreach ($geometry in $joinGeometry)
            {
                $converted = Convert-CapturedJoinGeometryRectangle $homeCapture $geometry
                [pscustomobject][ordered]@{ name=[string]$geometry.name; x=$converted.x; y=$converted.y; width=$converted.width; height=$converted.height }
            }
        )
        $backingName = $joinGeometryPrefix + 'InteriorBacking'
        $backingRows = @($joinGeometryRects | Where-Object name -eq $backingName)
        if ($backingRows.Count -ne 1) { throw "Expected exactly one Join InteriorBacking geometry row; found $($backingRows.Count)." }
        $backingRect = $backingRows[0]
        $backingTargetTolerancePx = 1
        $backingTargetDeviationPx = [ordered]@{
            unit='px'
            deltaX=($backingRect.x - $homeJoinDecoration.approvedTarget.x)
            deltaY=($backingRect.y - $homeJoinDecoration.approvedTarget.y)
            deltaWidth=($backingRect.width - $homeJoinDecoration.approvedTarget.width)
            deltaHeight=($backingRect.height - $homeJoinDecoration.approvedTarget.height)
        }
        $backingTargetPassed = [Math]::Abs($backingTargetDeviationPx.deltaX) -le $backingTargetTolerancePx -and
            [Math]::Abs($backingTargetDeviationPx.deltaY) -le $backingTargetTolerancePx -and
            [Math]::Abs($backingTargetDeviationPx.deltaWidth) -le $backingTargetTolerancePx -and
            [Math]::Abs($backingTargetDeviationPx.deltaHeight) -le $backingTargetTolerancePx
        $backingBottomScreenY = $backingRect.y + $backingRect.height
        $geometryCrossesBackingBottom = @($joinGeometryRects | Where-Object { ($_.y + $_.height) -gt $backingBottomScreenY }).Count -gt 0
        $actionBoundaryScreenY = 876
        $joinGraphicRows = @(
            foreach ($sprite in @($homeCapture.spriteSources | Where-Object {
                $_ -and [string]$_.node -like ($joinGeometryPrefix + '*') -and
                [string]$_.node -cne ($joinGeometryPrefix + 'JoinAction') -and
                [string]$_.node -notlike ($joinGeometryPrefix + 'JoinAction/*')
            }))
            {
                Convert-CapturedJoinGraphicRectangle $homeCapture $sprite
            }
        )
        $graphicsOrGeometryBoundaryAvailable = @($joinGraphicRows | Where-Object { -not $_.available }).Count -eq 0
        $joinBoundaryRows = @(
            @($joinGeometryRects | ForEach-Object { [pscustomobject]@{ name=$_.name; y=$_.y; height=$_.height } }) +
            @($joinGraphicRows | Where-Object available)
        )
        $graphicsOrGeometryCrossesActionBoundary = @($joinBoundaryRows | Where-Object { ($_.y + $_.height) -gt $actionBoundaryScreenY }).Count -gt 0
        $simulationInviteAbsent = -not ((ConvertTo-Json $manifest -Depth 12) -match 'SimulationInvite')
        $outlineBottomAbsent = @($joinGeometryRects | Where-Object { $_.name -like '*OutlineBottom*' }).Count -eq 0
        $requiredSpriteInventoryPassed = @($joinSpriteInventory | Where-Object { -not $_.passed }).Count -eq 0
        $joinActionReport = @($actionBarReports | Where-Object name -eq 'home-join-action')
        if ($joinActionReport.Count -ne 1) { throw 'Expected exactly one home-join-action report for Join decoration acceptance.' }
        $joinActionPassed = [bool]$joinActionReport[0].passed
        $joinDecorationReport = [pscustomobject][ordered]@{
            name=$homeJoinDecoration.name
            capture='home'
            actualRect=[ordered]@{ coordinateOrigin='screen-top-left'; unit='px'; x=$actualSpec.x; y=$actualSpec.y; width=$actualSpec.width; height=$actualSpec.height }
            referenceRect=[ordered]@{ x=$scaledReference.X; y=$scaledReference.Y; width=$scaledReference.Width; height=$scaledReference.Height }
            referenceMeasurementCanvas=[ordered]@{ width=$figure9MeasurementSize.width; height=$figure9MeasurementSize.height }
            locallyResizedReferenceSizePx=[ordered]@{ unit='px'; width=$locallyResizedReferenceCrop.Width; height=$locallyResizedReferenceCrop.Height }
            comparedPixels=$metric.ComparedPixels
            pixelDifferenceRatio=[double]$metric.DifferentPixels / $metric.ComparedPixels
            averageAbsoluteRgbError=[double]$metric.ErrorSum / ($metric.ComparedPixels * 3)
            components=$components
            backingRect=[ordered]@{ coordinateOrigin='screen-top-left'; unit='px'; x=$backingRect.x; y=$backingRect.y; width=$backingRect.width; height=$backingRect.height }
            backingTargetRect=[ordered]@{ coordinateOrigin='screen-top-left'; unit='px'; x=$homeJoinDecoration.approvedTarget.x; y=$homeJoinDecoration.approvedTarget.y; width=$homeJoinDecoration.approvedTarget.width; height=$homeJoinDecoration.approvedTarget.height }
            backingTargetTolerancePx=$backingTargetTolerancePx
            backingTargetDeviationPx=$backingTargetDeviationPx
            backingTargetPassed=$backingTargetPassed
            backingBottomScreenY=$backingBottomScreenY
            geometryCrossesBackingBottom=$geometryCrossesBackingBottom
            actionBoundaryScreenY=$actionBoundaryScreenY
            graphicBounds=$joinGraphicRows
            graphicsOrGeometryBoundaryAvailable=$graphicsOrGeometryBoundaryAvailable
            graphicsOrGeometryCrossesActionBoundary=$graphicsOrGeometryCrossesActionBoundary
            simulationInviteAbsent=$simulationInviteAbsent
            outlineBottomAbsent=$outlineBottomAbsent
            requiredSpriteInventory=$joinSpriteInventory
            requiredSpriteInventoryPassed=$requiredSpriteInventoryPassed
            joinActionPassed=$joinActionPassed
            passed=( @($components | Where-Object { -not $_.passed }).Count -eq 0 -and $simulationInviteAbsent -and $outlineBottomAbsent -and $backingTargetPassed -and -not $geometryCrossesBackingBottom -and $graphicsOrGeometryBoundaryAvailable -and -not $graphicsOrGeometryCrossesActionBoundary -and $requiredSpriteInventoryPassed -and $joinActionPassed )
        }
        $actualCrop.Save((Join-Path $stagingDirectory ($homeJoinDecoration.name + '-actual.png')), [Drawing.Imaging.ImageFormat]::Png)
        $locallyResizedReferenceCrop.Save((Join-Path $stagingDirectory ($homeJoinDecoration.name + '-reference.png')), [Drawing.Imaging.ImageFormat]::Png)
        $overlay.Save((Join-Path $stagingDirectory ($homeJoinDecoration.name + '-overlay.png')), [Drawing.Imaging.ImageFormat]::Png)
        $heatmap.Save((Join-Path $stagingDirectory ($homeJoinDecoration.name + '-heatmap.png')), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { if ($actual) { $actual.Dispose() }; if ($nativeReference) { $nativeReference.Dispose() }; if ($actualCrop) { $actualCrop.Dispose() }; if ($nativeReferenceCrop) { $nativeReferenceCrop.Dispose() }; if ($locallyResizedReferenceCrop) { $locallyResizedReferenceCrop.Dispose() }; if ($overlay) { $overlay.Dispose() }; if ($heatmap) { $heatmap.Dispose() } }
    $createActionReport = @(
        $actionBarReports | Where-Object name -eq 'home-create-action'
    )
    if ($createActionReport.Count -ne 1) {
        throw 'Expected exactly one home-create-action report for the Create lower boundary.'
    }
    $createActionReport = $createActionReport[0]
    $createActionContentPassed = @(
        $createActionReport.contentVisuals | Where-Object { -not $_.passed }
    ).Count -eq 0
    $createActionRectPassed =
        $createActionReport.positionDeviationPx1920x1080.deltaX -eq 0 -and
        $createActionReport.positionDeviationPx1920x1080.deltaY -eq 0 -and
        $createActionReport.sizeDeviationPxAfterLocalReferenceResize.deltaWidth -eq 0 -and
        $createActionReport.sizeDeviationPxAfterLocalReferenceResize.deltaHeight -eq 0
    $bottomBoundary = [pscustomobject][ordered]@{
        kind = 'action-bar'
        action = 'home-create-action'
        visibleTopScreenY = 460
        frameLocalY = 212
        positionDeviationPx1920x1080 =
            $createActionReport.positionDeviationPx1920x1080
        sizeDeviationPxAfterLocalReferenceResize =
            $createActionReport.sizeDeviationPxAfterLocalReferenceResize
        contentPassed = $createActionContentPassed
        passed = $createActionRectPassed -and $createActionContentPassed
    }
    $actual = $null
    $nativeReference = $null
    $actualCrop = $null
    $nativeReferenceCrop = $null
    $locallyResizedReferenceCrop = $null
    $overlay = $null
    $heatmap = $null
    try
    {
        $actual = [Drawing.Bitmap]::FromFile($homeCapture.path)
        $nativeReference = [Drawing.Bitmap]::FromFile($referenceHome)
        $actualSpec = $homeCreateFrame.approvedTarget
        $actualRectangle = New-Object Drawing.Rectangle $actualSpec.x, $actualSpec.y, $actualSpec.width, $actualSpec.height
        $scaledReference = Convert-ActionReferenceRectangle $homeCreateFrame.reference $nativeReference.Width $nativeReference.Height
        $actualCrop = New-LanLobbyBitmapCrop $actual $actualRectangle
        $nativeReferenceCrop = New-LanLobbyBitmapCrop $nativeReference $scaledReference
        $locallyResizedReferenceCrop = Resize-LanLobbyBitmap $nativeReferenceCrop $actualSpec.width $actualSpec.height ([Drawing.Drawing2D.InterpolationMode]::NearestNeighbor)
        $overlay = New-LanLobbyActionOverlay $actualCrop $locallyResizedReferenceCrop
        $heatmap = New-Object Drawing.Bitmap $actualCrop.Width, $actualCrop.Height
        $fullCrop = New-Object Drawing.Rectangle 0,0,$actualCrop.Width,$actualCrop.Height
        [long]$maskedPixels = 0
        $metric = ([LanLobbyVisualDiff]::Compare($actualCrop, $locallyResizedReferenceCrop, [Drawing.Rectangle[]]@(), [Drawing.Rectangle[]]@($fullCrop), [bool[]]@($false), $heatmap, [ref]$maskedPixels))[0]
        $edges = @()
        foreach ($edgeSpec in $createFrameEdges)
        {
            $search = New-Object Drawing.Rectangle $edgeSpec.search.x, $edgeSpec.search.y, $edgeSpec.search.width, $edgeSpec.search.height
            $background = New-Object Drawing.Rectangle $edgeSpec.background.x, $edgeSpec.background.y, $edgeSpec.background.width, $edgeSpec.background.height
            $horizontal = $edgeSpec.axis -ne 'y'
            $actualContinuity = [LanLobbyVisualDiff]::MeasureCyanContinuity($actualCrop, $search, $horizontal, 12, 3, 2)
            $referenceContinuity = [LanLobbyVisualDiff]::MeasureCyanContinuity($locallyResizedReferenceCrop, $search, $horizontal, 12, 3, 2)
            $contrast = [LanLobbyVisualDiff]::MeasureFrameLumaContrast($actualCrop, $search, $background, 12, 3, 2)
            $contrastPassed = $contrast.Available -and $contrast.ContrastDelta -ge $edgeSpec.minimumContrast
            $usesPixelCount = $edgeSpec.Contains('minimumPixelCount')
            $continuityPassed = if ($usesPixelCount) {
                $actualContinuity.QualifyingPixelCount -ge $edgeSpec.minimumPixelCount
            } else {
                $actualContinuity.CoverageRatio -ge $edgeSpec.minimumCoverage -and
                    $actualContinuity.LargestGapPixels -le $edgeSpec.maximumGap
            }
            $passed = $continuityPassed -and $contrastPassed
            $edges += [pscustomobject][ordered]@{
                name = $edgeSpec.name
                axis = $edgeSpec.axis
                search = [ordered]@{ x=$edgeSpec.search.x; y=$edgeSpec.search.y; width=$edgeSpec.search.width; height=$edgeSpec.search.height }
                backgroundSearch = [ordered]@{ x=$edgeSpec.background.x; y=$edgeSpec.background.y; width=$edgeSpec.background.width; height=$edgeSpec.background.height }
                thresholdMinimumGreen = 12
                minimumGreenOverRed = 3
                minimumBlueOverRed = 2
                minimumCoverage = $(if ($usesPixelCount) { $null } else { $edgeSpec.minimumCoverage })
                maximumGap = $(if ($usesPixelCount) { $null } else { $edgeSpec.maximumGap })
                minimumPixelCount = $(if ($usesPixelCount) { $edgeSpec.minimumPixelCount } else { $null })
                qualifyingPixelCount = $actualContinuity.QualifyingPixelCount
                coveredAxisPixels = $actualContinuity.CoveredAxisPixels
                axisLength = $actualContinuity.AxisLength
                coverageRatio = $actualContinuity.CoverageRatio
                largestGapPixels = $actualContinuity.LargestGapPixels
                minimumContrast = $edgeSpec.minimumContrast
                frameSampleCount = $contrast.FrameSampleCount
                backgroundSampleCount = $contrast.BackgroundSampleCount
                frameMedianLuma = $contrast.FrameMedianLuma
                backgroundMedianLuma = $contrast.BackgroundMedianLuma
                contrastDelta = $contrast.ContrastDelta
                contrastAvailable = $contrast.Available
                contrastFailureReason = $contrast.FailureReason
                referenceMeasurement = [ordered]@{
                    qualifyingPixelCount=$referenceContinuity.QualifyingPixelCount
                    coveredAxisPixels=$referenceContinuity.CoveredAxisPixels
                    axisLength=$referenceContinuity.AxisLength
                    coverageRatio=$referenceContinuity.CoverageRatio
                    largestGapPixels=$referenceContinuity.LargestGapPixels
                }
                continuityPassed = $continuityPassed
                contrastPassed = $contrastPassed
                passed = $passed
            }
        }
        $createFrameReport = [pscustomobject][ordered]@{
            name = $homeCreateFrame.name
            capture = 'home'
            actualRect = [ordered]@{ coordinateOrigin='screen-top-left'; unit='px'; x=$actualSpec.x; y=$actualSpec.y; width=$actualSpec.width; height=$actualSpec.height }
            referenceRect = [ordered]@{ x=$scaledReference.X; y=$scaledReference.Y; width=$scaledReference.Width; height=$scaledReference.Height }
            referenceMeasurementCanvas = [ordered]@{ width=$figure9MeasurementSize.width; height=$figure9MeasurementSize.height }
            locallyResizedReferenceSizePx = [ordered]@{ unit='px'; width=$locallyResizedReferenceCrop.Width; height=$locallyResizedReferenceCrop.Height }
            comparedPixels = $metric.ComparedPixels
            pixelDifferenceRatio = [double]$metric.DifferentPixels / $metric.ComparedPixels
            averageAbsoluteRgbError = [double]$metric.ErrorSum / ($metric.ComparedPixels * 3)
            edges = $edges
            bottomBoundary = $bottomBoundary
            passed =
                @($edges | Where-Object { -not $_.passed }).Count -eq 0 -and
                $bottomBoundary.passed
        }
        $actualCrop.Save((Join-Path $stagingDirectory ($homeCreateFrame.name + '-actual.png')), [Drawing.Imaging.ImageFormat]::Png)
        $locallyResizedReferenceCrop.Save((Join-Path $stagingDirectory ($homeCreateFrame.name + '-reference.png')), [Drawing.Imaging.ImageFormat]::Png)
        $overlay.Save((Join-Path $stagingDirectory ($homeCreateFrame.name + '-overlay.png')), [Drawing.Imaging.ImageFormat]::Png)
        $heatmap.Save((Join-Path $stagingDirectory ($homeCreateFrame.name + '-heatmap.png')), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { if ($actual) { $actual.Dispose() }; if ($nativeReference) { $nativeReference.Dispose() }; if ($actualCrop) { $actualCrop.Dispose() }; if ($nativeReferenceCrop) { $nativeReferenceCrop.Dispose() }; if ($locallyResizedReferenceCrop) { $locallyResizedReferenceCrop.Dispose() }; if ($overlay) { $overlay.Dispose() }; if ($heatmap) { $heatmap.Dispose() } }
    $report = [ordered]@{
        generatedAtUtc=[DateTime]::UtcNow.ToString('o')
        referenceNormalization='independent-xy'
        captures=$reportCaptures
        actionBars=$actionBarReports
        createDecoration=$createDecorationReport
        joinDecoration=$joinDecorationReport
        createFrame=$createFrameReport
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
    $markdown += @('', '## Home Create upper decoration', '', "Actual crop (1920×1080 top-left px): $($createDecorationReport.actualRect.x),$($createDecorationReport.actualRect.y),$($createDecorationReport.actualRect.width),$($createDecorationReport.actualRect.height). Native Figure 9 crop: $($createDecorationReport.referenceRect.x),$($createDecorationReport.referenceRect.y),$($createDecorationReport.referenceRect.width),$($createDecorationReport.referenceRect.height).", '', '| Component | Measurement mode | Cyan thresholds (G/G-R/B-R) | Expected | Reference measured | Actual measured | Center deviation (px) | Size deviation (px) | Measurement status | Passed |', '| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |')
    foreach ($component in @($createDecorationReport.components))
    {
        $referenceMeasured = if ($component.referenceBounds) { "$($component.referenceBounds.x),$($component.referenceBounds.y),$($component.referenceBounds.width),$($component.referenceBounds.height)" } else { 'unavailable' }
        $actualMeasured = if ($component.actualBounds) { "$($component.actualBounds.x),$($component.actualBounds.y),$($component.actualBounds.width),$($component.actualBounds.height)" } else { 'unavailable' }
        $centerDeviation = if ($component.centerDeviationPx) { "dx=$($component.centerDeviationPx.deltaX), dy=$($component.centerDeviationPx.deltaY)" } else { 'n/a' }
        $sizeDeviation = if ($component.sizeDeviationPx) { "dw=$($component.sizeDeviationPx.deltaWidth), dh=$($component.sizeDeviationPx.deltaHeight)" } else { 'n/a' }
        $measurementStatus = if ($component.measurementAvailable) { 'available' } else { ConvertTo-LanLobbyMarkdownCell ("unavailable: " + [string]$component.measurementError) }
        $markdown += "| $($component.name) | $($component.measurementMode) | $($component.thresholdMinimumGreen)/$($component.minimumGreenOverRed)/$($component.minimumBlueOverRed) | $($component.expectedBounds.x),$($component.expectedBounds.y),$($component.expectedBounds.width),$($component.expectedBounds.height) | $referenceMeasured | $actualMeasured | $centerDeviation | $sizeDeviation | $measurementStatus | $($component.passed) |"
    }
    $markdown += @('', '## Home Create open-frame continuity', '', "Actual crop (1920×1080 top-left px): $($createFrameReport.actualRect.x),$($createFrameReport.actualRect.y),$($createFrameReport.actualRect.width),$($createFrameReport.actualRect.height). Native Figure 9 crop: $($createFrameReport.referenceRect.x),$($createFrameReport.referenceRect.y),$($createFrameReport.referenceRect.width),$($createFrameReport.referenceRect.height). Overall passed: $($createFrameReport.passed).", '', '| Edge | Search/background | Reference pixels/coverage/gap | Actual pixels/coverage/gap | Frame/background median luma | Contrast delta/minimum | Continuity passed | Contrast passed | Passed |', '| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
    foreach ($edge in @($createFrameReport.edges))
    {
        $searchAndBackground = "$($edge.search.x),$($edge.search.y),$($edge.search.width),$($edge.search.height) / $($edge.backgroundSearch.x),$($edge.backgroundSearch.y),$($edge.backgroundSearch.width),$($edge.backgroundSearch.height)"
        $markdown += "| $($edge.name) | $searchAndBackground | $($edge.referenceMeasurement.qualifyingPixelCount)/$([Math]::Round($edge.referenceMeasurement.coverageRatio, 4))/$($edge.referenceMeasurement.largestGapPixels) | $($edge.qualifyingPixelCount)/$([Math]::Round($edge.coverageRatio, 4))/$($edge.largestGapPixels) | $([Math]::Round($edge.frameMedianLuma, 2))/$([Math]::Round($edge.backgroundMedianLuma, 2)) | $([Math]::Round($edge.contrastDelta, 2))/$($edge.minimumContrast) | $($edge.continuityPassed) | $($edge.contrastPassed) | $($edge.passed) |"
    }
    $markdown += @('', '| Lower boundary kind | Action | Visible top (screen Y) | Frame local Y | Position deviation (px) | Size deviation (px) | Content passed | Passed |', '| --- | --- | ---: | ---: | --- | --- | --- | --- |', "| $($createFrameReport.bottomBoundary.kind) | $($createFrameReport.bottomBoundary.action) | $($createFrameReport.bottomBoundary.visibleTopScreenY) | $($createFrameReport.bottomBoundary.frameLocalY) | dx=$($createFrameReport.bottomBoundary.positionDeviationPx1920x1080.deltaX), dy=$($createFrameReport.bottomBoundary.positionDeviationPx1920x1080.deltaY) | dw=$($createFrameReport.bottomBoundary.sizeDeviationPxAfterLocalReferenceResize.deltaWidth), dh=$($createFrameReport.bottomBoundary.sizeDeviationPxAfterLocalReferenceResize.deltaHeight) | $($createFrameReport.bottomBoundary.contentPassed) | $($createFrameReport.bottomBoundary.passed) |")
    $markdown += @('', '## Home Join decoration', '', "Overall passed: $($joinDecorationReport.passed). Actual crop (top-left px): $($joinDecorationReport.actualRect.x),$($joinDecorationReport.actualRect.y),$($joinDecorationReport.actualRect.width),$($joinDecorationReport.actualRect.height).", '', '| Component | Measurement | Search | Thresholds | Tolerance (px) | Expected | Reference measured | Actual measured | Center deviation (px) | Size deviation (px) | Measurement status | Passed |', '| --- | --- | --- | --- | ---: | --- | --- | --- | --- | --- | --- | --- |')
    foreach ($component in @($joinDecorationReport.components))
    {
        $referenceMeasured = if ($component.referenceBounds) { "$($component.referenceBounds.x),$($component.referenceBounds.y),$($component.referenceBounds.width),$($component.referenceBounds.height)" } else { 'unavailable' }
        $actualMeasured = if ($component.actualBounds) { "$($component.actualBounds.x),$($component.actualBounds.y),$($component.actualBounds.width),$($component.actualBounds.height)" } else { 'unavailable' }
        $centerDeviation = if ($component.centerDeviationPx) { "dx=$($component.centerDeviationPx.deltaX), dy=$($component.centerDeviationPx.deltaY)" } else { 'n/a' }
        $sizeDeviation = if ($component.sizeDeviationPx) { "dw=$($component.sizeDeviationPx.deltaWidth), dh=$($component.sizeDeviationPx.deltaHeight)" } else { 'n/a' }
        $thresholds = @($component.thresholds.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', '
        $measurementStatus = if ($component.measurementAvailable) { 'available' } else { ConvertTo-LanLobbyMarkdownCell ('unavailable: ' + [string]$component.measurementError) }
        $markdown += "| $($component.name) | $($component.measurement) | $($component.search.x),$($component.search.y),$($component.search.width),$($component.search.height) | $thresholds | $($component.tolerancePx) | $($component.expectedBounds.x),$($component.expectedBounds.y),$($component.expectedBounds.width),$($component.expectedBounds.height) | $referenceMeasured | $actualMeasured | $centerDeviation | $sizeDeviation | $measurementStatus | $($component.passed) |"
    }
    $markdown += @('', "Join backing: $($joinDecorationReport.backingRect.x),$($joinDecorationReport.backingRect.y),$($joinDecorationReport.backingRect.width),$($joinDecorationReport.backingRect.height) top-left px; bottom screen Y: $($joinDecorationReport.backingBottomScreenY). SimulationInvite absent: $($joinDecorationReport.simulationInviteAbsent). OutlineBottom absent: $($joinDecorationReport.outlineBottomAbsent). Geometry crosses backing bottom: $($joinDecorationReport.geometryCrossesBackingBottom). Accepted Join action/content passed: $($joinDecorationReport.joinActionPassed).")
    $markdown += @('', "Join backing target passed: $($joinDecorationReport.backingTargetPassed); tolerance: $($joinDecorationReport.backingTargetTolerancePx) px; deviation: dx=$($joinDecorationReport.backingTargetDeviationPx.deltaX), dy=$($joinDecorationReport.backingTargetDeviationPx.deltaY), dw=$($joinDecorationReport.backingTargetDeviationPx.deltaWidth), dh=$($joinDecorationReport.backingTargetDeviationPx.deltaHeight). Fixed action boundary: y=$($joinDecorationReport.actionBoundaryScreenY); graphic/geometry boundary available: $($joinDecorationReport.graphicsOrGeometryBoundaryAvailable); graphic/geometry crosses boundary: $($joinDecorationReport.graphicsOrGeometryCrossesActionBoundary). Required Sprite inventory passed: $($joinDecorationReport.requiredSpriteInventoryPassed).", '', '| Required Join Sprite | Expected occurrences | Actual occurrences | Expected source | Actual source | Passed |', '| --- | ---: | ---: | --- | --- | --- |')
    foreach ($item in @($joinDecorationReport.requiredSpriteInventory)) { $markdown += "| $($item.spriteName) | $($item.expectedOccurrenceCount) | $($item.actualOccurrenceCount) | $($item.expectedSourcePath) | $($item.sourcePath) | $($item.passed) |" }
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
