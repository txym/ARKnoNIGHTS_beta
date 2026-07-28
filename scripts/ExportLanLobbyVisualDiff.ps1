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
    public int PixelCount { get; set; }
    public string FailureReason { get; set; }
}
public sealed class LanLobbyMaskComparison
{
    public long IntersectionPixels { get; set; }
    public long UnionPixels { get; set; }
    public double Jaccard { get; set; }
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
        return new LanLobbyBoundsMeasurement { Available=false, Bounds=null, PixelCount=0, FailureReason=failureReason };
    }
    static LanLobbyBoundsMeasurement AvailableBounds(int minX, int minY, int maxX, int maxY, int pixelCount=0) {
        if (maxX < minX || maxY < minY) return Unavailable("No qualifying decoded pixels found.");
        return new LanLobbyBoundsMeasurement {
            Available=true,
            Bounds=new LanLobbyVisualBounds { X=minX, Y=minY, Width=maxX-minX+1, Height=maxY-minY+1 },
            PixelCount=pixelCount,
            FailureReason=null
        };
    }
    static bool IsExcluded(Rectangle[] exclusions, int x, int y) {
        if (exclusions == null) return false;
        foreach (Rectangle exclusion in exclusions) if (Inside(exclusion,x,y)) return true;
        return false;
    }
    static bool IsCyanColor(Color pixel) {
        return pixel.A>0 && pixel.G>=80 && pixel.G>=pixel.R+20 && pixel.B>=pixel.R+10;
    }
    static bool HasCyanSupport(Bitmap bitmap, int x, int y, int radius) {
        int[] dx={-1,1,0,0,-1,-1,1,1};
        int[] dy={0,0,-1,1,-1,1,-1,1};
        int supportedDirections=0;
        for(int direction=0;direction<dx.Length;direction++) {
            for(int distance=1;distance<=radius;distance++) {
                int nx=x+dx[direction]*distance,ny=y+dy[direction]*distance;
                if(nx<0||ny<0||nx>=bitmap.Width||ny>=bitmap.Height) continue;
                if(!IsCyanColor(bitmap.GetPixel(nx,ny))) continue;
                supportedDirections++;
                break;
            }
        }
        return supportedDirections>=4;
    }
    static bool IsVisibleMask(Bitmap bitmap, int x, int y, string maskKind) {
        Color pixel=bitmap.GetPixel(x,y);
        if(pixel.A==0) return false;
        int maximum=Math.Max(pixel.R,Math.Max(pixel.G,pixel.B));
        int minimum=Math.Min(pixel.R,Math.Min(pixel.G,pixel.B));
        int spread=maximum-minimum;
        int luminance=(299*pixel.R+587*pixel.G+114*pixel.B)/1000;
        switch(maskKind) {
            case "Cyan": return IsCyanColor(pixel);
            case "Gray": return spread<=18 && luminance>=45 && luminance<=210;
            case "Dark": return luminance<70;
            case "DarkOnCyan": return luminance<70 && HasCyanSupport(bitmap,x,y,6);
            case "Light": return luminance>=170 && spread<=24;
            case "CreatorTagCyan":
                return pixel.R<=40 && pixel.G>=160 && pixel.G<=215 && pixel.B>=120 && pixel.B<=180;
            case "MutedLight": return spread<=35 && luminance>=115 && luminance<=240;
            case "Contrast": {
                int maximumDifference=0;
                int[] dx={-1,1,0,0}; int[] dy={0,0,-1,1};
                for(int i=0;i<dx.Length;i++) {
                    int nx=x+dx[i],ny=y+dy[i];
                    if(nx<0||ny<0||nx>=bitmap.Width||ny>=bitmap.Height) continue;
                    Color neighbor=bitmap.GetPixel(nx,ny);
                    maximumDifference=Math.Max(maximumDifference,
                        Math.Max(Math.Abs(pixel.R-neighbor.R),
                        Math.Max(Math.Abs(pixel.G-neighbor.G),Math.Abs(pixel.B-neighbor.B))));
                }
                return spread>=25 || maximumDifference>=25;
            }
            default: throw new ArgumentOutOfRangeException("maskKind");
        }
    }
    public static LanLobbyBoundsMeasurement FindVisibleMaskBounds(
        Bitmap bitmap, Rectangle search, string maskKind, Rectangle[] exclusions)
    {
        ValidateSearch(bitmap,search);
        int minX=search.Right,minY=search.Bottom,maxX=-1,maxY=-1,pixelCount=0;
        for(int y=search.Y;y<search.Bottom;y++) for(int x=search.X;x<search.Right;x++) {
            if(IsExcluded(exclusions,x,y) || !IsVisibleMask(bitmap,x,y,maskKind)) continue;
            pixelCount++; minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);
        }
        return pixelCount==0
            ? Unavailable("No visible color/contrast mask pixels found.")
            : AvailableBounds(minX,minY,maxX,maxY,pixelCount);
    }
    public static LanLobbyBoundsMeasurement FindLongVerticalMaskEdgeBounds(
        Bitmap bitmap, Rectangle search, string maskKind, Rectangle[] exclusions)
    {
        ValidateSearch(bitmap,search);
        const double minimumNormalizedContrast=20.0;
        int[][] rowBands;
        if(search.Height>=400) {
            rowBands=new int[][] {
                new int[] { search.Y+142, Math.Min(search.Bottom,search.Y+183) },
                new int[] { search.Y+311, Math.Min(search.Bottom,search.Y+387) }
            };
        } else {
            rowBands=new int[][] { new int[] { search.Y,search.Bottom } };
        }
        int bandRows=0;
        foreach(int[] band in rowBands) bandRows+=Math.Max(0,band[1]-band[0]);
        int minimumRun=Math.Max(3,(int)Math.Ceiling(bandRows*0.25));
        int middleX=search.X+search.Width/2;
        int leftX=-1,rightX=-1,leftCount=-1,rightCount=-1;
        double leftScore=-1.0,rightScore=-1.0;
        for(int x=search.X;x<search.Right;x++) {
            int count=0;
            double scoreSum=0.0;
            foreach(int[] band in rowBands) {
                for(int y=band[0];y<band[1];y++) {
                    double score=NormalizedHorizontalContrast(
                        bitmap,search,exclusions,x,y,x<middleX ? -1 : 1);
                    if(score<minimumNormalizedContrast) continue;
                    count++;
                    scoreSum+=score;
                }
            }
            if(count<minimumRun) continue;
            if(x<middleX && (count>leftCount ||
                (count==leftCount && (scoreSum>leftScore+0.001 ||
                (Math.Abs(scoreSum-leftScore)<=0.001 && (leftX<0 || x<leftX)))))) {
                leftX=x;
                leftCount=count;
                leftScore=scoreSum;
            }
            if(x>=middleX && (count>rightCount ||
                (count==rightCount && (scoreSum>rightScore+0.001 ||
                (Math.Abs(scoreSum-rightScore)<=0.001 && x>rightX))))) {
                rightX=x;
                rightCount=count;
                rightScore=scoreSum;
            }
        }
        if(leftX<0 || rightX<0 || rightX<=leftX)
            return Unavailable("No dominant decoded portrait-frame side-edge pair found.");
        int minY=search.Bottom,maxY=-1,pairedRows=0;
        for(int y=search.Y;y<search.Bottom;y++) {
            bool leftEdge=NormalizedHorizontalContrast(
                bitmap,search,exclusions,leftX,y,-1)>=minimumNormalizedContrast;
            bool rightEdge=NormalizedHorizontalContrast(
                bitmap,search,exclusions,rightX,y,1)>=minimumNormalizedContrast;
            if(!leftEdge || !rightEdge) continue;
            pairedRows++;
            minY=Math.Min(minY,y);
            maxY=Math.Max(maxY,y);
        }
        return pairedRows==0
            ? Unavailable("Dominant portrait-frame sides have no paired decoded vertical continuity.")
            : AvailableBounds(leftX,minY,rightX,maxY,leftCount+rightCount+pairedRows*2);
    }
    static double NormalizedHorizontalContrast(
        Bitmap bitmap, Rectangle search, Rectangle[] exclusions,
        int x, int y, int direction)
    {
        if(IsExcluded(exclusions,x,y)) return 0.0;
        Color origin=bitmap.GetPixel(x,y);
        double maximum=0.0;
        int firstDirection=direction==0 ? -1 : direction;
        int lastDirection=direction==0 ? 1 : direction;
        for(int candidateDirection=firstDirection;
            candidateDirection<=lastDirection;
            candidateDirection+=2) {
            for(int distance=1;distance<=3;distance++) {
                int neighborX=x+candidateDirection*distance;
                if(neighborX<search.X || neighborX>=search.Right ||
                    IsExcluded(exclusions,neighborX,y)) continue;
                Color neighbor=bitmap.GetPixel(neighborX,y);
                int difference=Math.Abs(origin.R-neighbor.R)+
                    Math.Abs(origin.G-neighbor.G)+Math.Abs(origin.B-neighbor.B);
                int denominator=Math.Max(origin.R,neighbor.R)+
                    Math.Max(origin.G,neighbor.G)+Math.Max(origin.B,neighbor.B);
                denominator=Math.Max(12,denominator);
                maximum=Math.Max(maximum,255.0*difference/denominator);
            }
        }
        return maximum;
    }
    public static LanLobbyBoundsMeasurement FindDominantLowerHorizontalContrastEdge(
        Bitmap bitmap, Rectangle search, Rectangle[] exclusions)
    {
        ValidateSearch(bitmap,search);
        const double minimumNormalizedContrast=20.0;
        int firstY=Math.Max(search.Y,search.Bottom-40);
        // The lower decoration is wider than the portrait frame. When a
        // same-color ReadyOverlay covers its center, only the decoded side
        // shoulders inside the unchanged frame ROI expose the top edge.
        int minimumCoverage=Math.Max(3,(int)Math.Ceiling(search.Width*0.02));
        int bestY=-1,bestCount=-1;
        double bestScore=-1.0;
        for(int y=firstY;y<search.Bottom;y++) {
            int count=0;
            double scoreSum=0.0;
            for(int x=search.X;x<search.Right;x++) {
                double score=NormalizedVerticalContrast(
                    bitmap,search,exclusions,x,y,-1);
                if(score<minimumNormalizedContrast) continue;
                count++;
                scoreSum+=score;
            }
            if(count<minimumCoverage) continue;
            if(count>bestCount ||
                (count==bestCount && (scoreSum>bestScore+0.001 ||
                (Math.Abs(scoreSum-bestScore)<=0.001 && (bestY<0 || y<bestY))))) {
                bestY=y;
                bestCount=count;
                bestScore=scoreSum;
            }
        }
        return bestY<0
            ? Unavailable("No dominant decoded lower-decoration top edge found.")
            : AvailableBounds(search.X,bestY,search.Right-1,bestY,bestCount);
    }
    static double NormalizedVerticalContrast(
        Bitmap bitmap, Rectangle search, Rectangle[] exclusions,
        int x, int y, int direction)
    {
        if(IsExcluded(exclusions,x,y)) return 0.0;
        Color origin=bitmap.GetPixel(x,y);
        double maximum=0.0;
        for(int distance=1;distance<=3;distance++) {
            int neighborY=y+direction*distance;
            if(neighborY<search.Y || neighborY>=search.Bottom ||
                IsExcluded(exclusions,x,neighborY)) continue;
            Color neighbor=bitmap.GetPixel(x,neighborY);
            int difference=Math.Abs(origin.R-neighbor.R)+
                Math.Abs(origin.G-neighbor.G)+Math.Abs(origin.B-neighbor.B);
            int denominator=Math.Max(origin.R,neighbor.R)+
                Math.Max(origin.G,neighbor.G)+Math.Max(origin.B,neighbor.B);
            denominator=Math.Max(12,denominator);
            maximum=Math.Max(maximum,255.0*difference/denominator);
        }
        return maximum;
    }
    public static LanLobbyMaskComparison CompareBounds(
        LanLobbyVisualBounds actual, LanLobbyVisualBounds reference)
    {
        if(actual==null) throw new ArgumentNullException("actual");
        if(reference==null) throw new ArgumentNullException("reference");
        int left=Math.Max(actual.X,reference.X);
        int top=Math.Max(actual.Y,reference.Y);
        int right=Math.Min(actual.X+actual.Width,reference.X+reference.Width);
        int bottom=Math.Min(actual.Y+actual.Height,reference.Y+reference.Height);
        long intersection=(long)Math.Max(0,right-left)*Math.Max(0,bottom-top);
        long actualArea=(long)actual.Width*actual.Height;
        long referenceArea=(long)reference.Width*reference.Height;
        long union=actualArea+referenceArea-intersection;
        return new LanLobbyMaskComparison {
            IntersectionPixels=intersection,
            UnionPixels=union,
            Jaccard=union==0 ? 0.0 : (double)intersection/union
        };
    }
    public static int MaximumContinuousEmptyMaskRows(
        Bitmap bitmap, Rectangle search, string maskKind)
    {
        ValidateSearch(bitmap,search);
        int longest=0,current=0;
        for(int y=search.Y;y<search.Bottom;y++) {
            bool hasQualifyingPixel=false;
            for(int x=search.X;x<search.Right;x++) {
                if(!IsVisibleMask(bitmap,x,y,maskKind)) continue;
                hasQualifyingPixel=true;
                break;
            }
            if(hasQualifyingPixel) current=0;
            else {
                current++;
                longest=Math.Max(longest,current);
            }
        }
        return longest;
    }
    public static LanLobbyMaskComparison CompareVisibleMasks(
        Bitmap actual, Bitmap reference, Rectangle search, string maskKind, Rectangle[] exclusions)
    {
        ValidateSearch(actual,search); ValidateSearch(reference,search);
        long intersection=0,union=0;
        for(int y=search.Y;y<search.Bottom;y++) for(int x=search.X;x<search.Right;x++) {
            if(IsExcluded(exclusions,x,y)) continue;
            bool a=IsVisibleMask(actual,x,y,maskKind),r=IsVisibleMask(reference,x,y,maskKind);
            if(a&&r) intersection++;
            if(a||r) union++;
        }
        return new LanLobbyMaskComparison {
            IntersectionPixels=intersection,
            UnionPixels=union,
            Jaccard=union==0 ? 0.0 : (double)intersection/union
        };
    }
    public static LanLobbyBoundsMeasurement FindUnexpectedDifferenceBounds(
        Bitmap actual, Bitmap reference, Rectangle search, Rectangle[] exclusions, int minimumChannelDifference)
    {
        ValidateSearch(actual,search); ValidateSearch(reference,search);
        if(minimumChannelDifference<1 || minimumChannelDifference>255)
            throw new ArgumentOutOfRangeException("minimumChannelDifference");
        int minX=search.Right,minY=search.Bottom,maxX=-1,maxY=-1,pixelCount=0;
        for(int y=search.Y;y<search.Bottom;y++) for(int x=search.X;x<search.Right;x++) {
            if(IsExcluded(exclusions,x,y)) continue;
            Color a=actual.GetPixel(x,y),r=reference.GetPixel(x,y);
            int difference=Math.Max(Math.Abs(a.R-r.R),
                Math.Max(Math.Abs(a.G-r.G),Math.Abs(a.B-r.B)));
            if(difference<minimumChannelDifference) continue;
            pixelCount++;
            minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);
        }
        return pixelCount==0
            ? Unavailable("No unexpected decoded actual-vs-reference RGB differences found.")
            : AvailableBounds(minX,minY,maxX,maxY,pixelCount);
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
    public static int[] CountOrangePixelsPerColumn(
        Bitmap bitmap, Rectangle search, int minimumRed, int maximumBlue, int minimumRedOverGreen)
    {
        ValidateSearch(bitmap, search);
        int[] counts=new int[search.Width];
        for(int x=search.X;x<search.Right;x++) for(int y=search.Y;y<search.Bottom;y++) {
            Color pixel=bitmap.GetPixel(x,y);
            if(pixel.A==0 || pixel.R<minimumRed || pixel.B>maximumBlue ||
                pixel.R<pixel.G+minimumRedOverGreen) continue;
            counts[x-search.X]++;
        }
        return counts;
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
    public static LanLobbyBoundsMeasurement FindLargestNeutralComponentBounds(
        Bitmap bitmap, Rectangle search, int minimumLuminanceInclusive, int maximumLuminanceInclusive, int maximumChannelSpread)
    {
        ValidateSearch(bitmap, search);
        if(minimumLuminanceInclusive < 0 || maximumLuminanceInclusive > 255 ||
            minimumLuminanceInclusive > maximumLuminanceInclusive || maximumChannelSpread < 0)
            throw new ArgumentOutOfRangeException("neutralThresholds");
        bool[,] qualifying=new bool[search.Width,search.Height];
        bool[,] visited=new bool[search.Width,search.Height];
        for(int y=0;y<search.Height;y++) for(int x=0;x<search.Width;x++) {
            Color pixel=bitmap.GetPixel(search.X+x,search.Y+y);
            int luminance=(299*pixel.R+587*pixel.G+114*pixel.B)/1000;
            int channelSpread=Math.Max(pixel.R,Math.Max(pixel.G,pixel.B))-Math.Min(pixel.R,Math.Min(pixel.G,pixel.B));
            qualifying[x,y]=pixel.A>0 && luminance>=minimumLuminanceInclusive &&
                luminance<=maximumLuminanceInclusive && channelSpread<=maximumChannelSpread;
        }
        int bestCount=0,bestMinX=search.Right,bestMinY=search.Bottom,bestMaxX=-1,bestMaxY=-1;
        int[] dx={-1,1,0,0,-1,-1,1,1};
        int[] dy={0,0,-1,1,-1,1,-1,1};
        for(int startY=0;startY<search.Height;startY++) for(int startX=0;startX<search.Width;startX++) {
            if(visited[startX,startY] || !qualifying[startX,startY]) continue;
            Queue<Point> queue=new Queue<Point>();
            queue.Enqueue(new Point(startX,startY));
            visited[startX,startY]=true;
            int count=0,minX=startX,minY=startY,maxX=startX,maxY=startY;
            while(queue.Count>0) {
                Point point=queue.Dequeue();
                count++;
                minX=Math.Min(minX,point.X); minY=Math.Min(minY,point.Y);
                maxX=Math.Max(maxX,point.X); maxY=Math.Max(maxY,point.Y);
                for(int i=0;i<dx.Length;i++) {
                    int x=point.X+dx[i],y=point.Y+dy[i];
                    if(x<0 || y<0 || x>=search.Width || y>=search.Height ||
                        visited[x,y] || !qualifying[x,y]) continue;
                    visited[x,y]=true;
                    queue.Enqueue(new Point(x,y));
                }
            }
            if(count<=bestCount) continue;
            bestCount=count; bestMinX=minX; bestMinY=minY; bestMaxX=maxX; bestMaxY=maxY;
        }
        return bestCount==0
            ? Unavailable("No qualifying decoded neutral component found.")
            : AvailableBounds(search.X+bestMinX,search.Y+bestMinY,search.X+bestMaxX,search.Y+bestMaxY);
    }
    public static LanLobbyBoundsMeasurement FindOrangeComponentUnionBounds(
        Bitmap bitmap, Rectangle search, int minimumRed, int minimumGreen, int maximumBlue, int minimumRedOverGreen,
        int minimumComponentPixelCount, int maximumComponentWidth, int maximumComponentHeight,
        Rectangle ignoredVerticalGuide, Rectangle ignoredHorizontalGuide, Rectangle[] requiredComponentAnchors)
    {
        ValidateSearch(bitmap, search);
        if(minimumComponentPixelCount<1 || maximumComponentWidth<1 || maximumComponentHeight<1)
            throw new ArgumentOutOfRangeException("orangeComponentThresholds");
        if(requiredComponentAnchors==null || requiredComponentAnchors.Length==0)
            throw new ArgumentOutOfRangeException("requiredComponentAnchors");
        foreach(Rectangle anchor in requiredComponentAnchors) {
            if(anchor.Width<=0 || anchor.Height<=0 || !Inside(search,anchor.X,anchor.Y) ||
                !Inside(search,anchor.Right-1,anchor.Bottom-1))
                throw new ArgumentOutOfRangeException("requiredComponentAnchor");
        }
        Rectangle[] ignored={ignoredVerticalGuide,ignoredHorizontalGuide};
        foreach(Rectangle region in ignored) {
            if(region.Width<=0 || region.Height<=0 || !Inside(search,region.X,region.Y) ||
                !Inside(search,region.Right-1,region.Bottom-1))
                throw new ArgumentOutOfRangeException("ignoredGuide");
        }
        bool[,] qualifying=new bool[search.Width,search.Height];
        bool[,] visited=new bool[search.Width,search.Height];
        for(int y=0;y<search.Height;y++) for(int x=0;x<search.Width;x++) {
            int bitmapX=search.X+x,bitmapY=search.Y+y;
            if(Inside(ignoredVerticalGuide,bitmapX,bitmapY) || Inside(ignoredHorizontalGuide,bitmapX,bitmapY)) continue;
            Color pixel=bitmap.GetPixel(bitmapX,bitmapY);
            qualifying[x,y]=pixel.A>0 && pixel.R>=minimumRed && pixel.G>=minimumGreen &&
                pixel.B<=maximumBlue && pixel.R>=pixel.G+minimumRedOverGreen;
        }
        int unionMinX=search.Right,unionMinY=search.Bottom,unionMaxX=-1,unionMaxY=-1;
        bool[] matchedAnchors=new bool[requiredComponentAnchors.Length];
        int[] dx={-1,1,0,0,-1,-1,1,1};
        int[] dy={0,0,-1,1,-1,1,-1,1};
        for(int startY=0;startY<search.Height;startY++) for(int startX=0;startX<search.Width;startX++) {
            if(visited[startX,startY] || !qualifying[startX,startY]) continue;
            Queue<Point> queue=new Queue<Point>();
            queue.Enqueue(new Point(startX,startY));
            visited[startX,startY]=true;
            int count=0,minX=startX,minY=startY,maxX=startX,maxY=startY;
            bool[] componentAnchorMatches=new bool[requiredComponentAnchors.Length];
            while(queue.Count>0) {
                Point point=queue.Dequeue();
                count++;
                minX=Math.Min(minX,point.X); minY=Math.Min(minY,point.Y);
                maxX=Math.Max(maxX,point.X); maxY=Math.Max(maxY,point.Y);
                int bitmapX=search.X+point.X,bitmapY=search.Y+point.Y;
                for(int anchorIndex=0;anchorIndex<requiredComponentAnchors.Length;anchorIndex++)
                    if(Inside(requiredComponentAnchors[anchorIndex],bitmapX,bitmapY))
                        componentAnchorMatches[anchorIndex]=true;
                for(int i=0;i<dx.Length;i++) {
                    int x=point.X+dx[i],y=point.Y+dy[i];
                    if(x<0 || y<0 || x>=search.Width || y>=search.Height ||
                        visited[x,y] || !qualifying[x,y]) continue;
                    visited[x,y]=true;
                    queue.Enqueue(new Point(x,y));
                }
            }
            int width=maxX-minX+1,height=maxY-minY+1;
            if(count<minimumComponentPixelCount || width>maximumComponentWidth || height>maximumComponentHeight) continue;
            bool matchesRequiredAnchor=false;
            for(int anchorIndex=0;anchorIndex<componentAnchorMatches.Length;anchorIndex++) {
                if(!componentAnchorMatches[anchorIndex]) continue;
                matchesRequiredAnchor=true;
                matchedAnchors[anchorIndex]=true;
            }
            if(!matchesRequiredAnchor) continue;
            unionMinX=Math.Min(unionMinX,minX); unionMinY=Math.Min(unionMinY,minY);
            unionMaxX=Math.Max(unionMaxX,maxX); unionMaxY=Math.Max(unionMaxY,maxY);
        }
        foreach(bool matchedAnchor in matchedAnchors)
            if(!matchedAnchor) return Unavailable("A required central-blank quadrant anchor had no qualifying decoded orange component.");
        return unionMaxX<unionMinX || unionMaxY<unionMinY
            ? Unavailable("No qualifying decoded orange components found.")
            : AvailableBounds(search.X+unionMinX,search.Y+unionMinY,search.X+unionMaxX,search.Y+unionMaxY);
    }
    public static LanLobbyBoundsMeasurement FindEdgeComponentUnionBounds(
        Bitmap bitmap, Rectangle search, int minimumChannelDifference, int minimumComponentPixelCount,
        bool excludeSearchBorderComponents, Rectangle ignoredRegion)
    {
        ValidateSearch(bitmap, search);
        if(minimumChannelDifference<1 || minimumChannelDifference>255 || minimumComponentPixelCount<1)
            throw new ArgumentOutOfRangeException("edgeComponentThresholds");
        if(ignoredRegion.Width<0 || ignoredRegion.Height<0 ||
            (ignoredRegion.Width>0 && (!Inside(search,ignoredRegion.X,ignoredRegion.Y) ||
            !Inside(search,ignoredRegion.Right-1,ignoredRegion.Bottom-1))))
            throw new ArgumentOutOfRangeException("ignoredRegion");
        bool[,] qualifying=new bool[search.Width,search.Height];
        bool[,] visited=new bool[search.Width,search.Height];
        int[] cardinalX={-1,1,0,0};
        int[] cardinalY={0,0,-1,1};
        for(int y=0;y<search.Height;y++) for(int x=0;x<search.Width;x++) {
            int bitmapX=search.X+x,bitmapY=search.Y+y;
            if(ignoredRegion.Width>0 && ignoredRegion.Height>0 && Inside(ignoredRegion,bitmapX,bitmapY)) {
                qualifying[x,y]=false;
                continue;
            }
            Color pixel=bitmap.GetPixel(bitmapX,bitmapY);
            int maximumDifference=0;
            for(int i=0;i<cardinalX.Length;i++) {
                int neighborX=bitmapX+cardinalX[i],neighborY=bitmapY+cardinalY[i];
                if(neighborX<0 || neighborY<0 || neighborX>=bitmap.Width || neighborY>=bitmap.Height) continue;
                Color neighbor=bitmap.GetPixel(neighborX,neighborY);
                maximumDifference=Math.Max(maximumDifference,
                    Math.Max(Math.Abs(pixel.R-neighbor.R),
                    Math.Max(Math.Abs(pixel.G-neighbor.G),Math.Abs(pixel.B-neighbor.B))));
            }
            qualifying[x,y]=pixel.A>0 && maximumDifference>=minimumChannelDifference;
        }
        int unionMinX=search.Right,unionMinY=search.Bottom,unionMaxX=-1,unionMaxY=-1;
        int[] dx={-1,1,0,0,-1,-1,1,1};
        int[] dy={0,0,-1,1,-1,1,-1,1};
        for(int startY=0;startY<search.Height;startY++) for(int startX=0;startX<search.Width;startX++) {
            if(visited[startX,startY] || !qualifying[startX,startY]) continue;
            Queue<Point> queue=new Queue<Point>();
            queue.Enqueue(new Point(startX,startY));
            visited[startX,startY]=true;
            int count=0,minX=startX,minY=startY,maxX=startX,maxY=startY;
            bool touchesSearchBorder=false;
            while(queue.Count>0) {
                Point point=queue.Dequeue();
                count++;
                minX=Math.Min(minX,point.X); minY=Math.Min(minY,point.Y);
                maxX=Math.Max(maxX,point.X); maxY=Math.Max(maxY,point.Y);
                touchesSearchBorder=touchesSearchBorder || point.X==0 || point.Y==0 ||
                    point.X==search.Width-1 || point.Y==search.Height-1;
                for(int i=0;i<dx.Length;i++) {
                    int x=point.X+dx[i],y=point.Y+dy[i];
                    if(x<0 || y<0 || x>=search.Width || y>=search.Height ||
                        visited[x,y] || !qualifying[x,y]) continue;
                    visited[x,y]=true;
                    queue.Enqueue(new Point(x,y));
                }
            }
            if(count<minimumComponentPixelCount || (excludeSearchBorderComponents && touchesSearchBorder)) continue;
            unionMinX=Math.Min(unionMinX,minX); unionMinY=Math.Min(unionMinY,minY);
            unionMaxX=Math.Max(unionMaxX,maxX); unionMaxY=Math.Max(unionMaxY,maxY);
        }
        return unionMaxX<unionMinX || unionMaxY<unionMinY
            ? Unavailable("No qualifying decoded edge components found.")
            : AvailableBounds(search.X+unionMinX,search.Y+unionMinY,search.X+unionMaxX,search.Y+unionMaxY);
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
            result.FailureReason="No decoded background color samples found.";
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
  @{ name='logo'; measurement='orange'; search=@{x=80;y=54;width=140;height=42}; expected=@{x=91;y=64;width=118;height=20}; tolerance=2; thresholds=@{minimumRed=80;minimumGreen=5;maximumBlue=100;minimumRedOverGreen=15} },
  @{
    name='text-01'
    measurement='orange'
    search=@{x=385;y=45;width=95;height=35}
    expected=@{x=391;y=56;width=65;height=8}
    tolerance=2
    thresholds=@{minimumRed=200;minimumGreen=100;maximumBlue=80;minimumRedOverGreen=40}
    boundsAdjustment=@{
      coordinateOrigin='crop-top-left'
      unit='px'
      x=-2
      y=-2
      width=4
      height=5
      reason='The text-01 decoded reference omits transparent/antialiased margins; this explicit calibration restores the approved visible target.'
    }
  },
  @{ name='text-02'; measurement='orange'; search=@{x=515;y=50;width=110;height=35}; expected=@{x=526;y=62;width=89;height=11}; tolerance=2; thresholds=@{minimumRed=80;minimumGreen=5;maximumBlue=100;minimumRedOverGreen=15} },
  @{
    name='triangle'
    measurement='orange'
    search=@{x=325;y=35;width=55;height=31}
    expected=@{x=338;y=47;width=30;height=17}
    tolerance=2
    thresholds=@{minimumRed=200;minimumGreen=100;maximumBlue=80;minimumRedOverGreen=40}
    boundsAdjustment=@{
      coordinateOrigin='crop-top-left'
      unit='px'
      x=-4
      y=-2
      width=6
      height=5
      reason='The triangle decoded reference omits transparent/antialiased margins; this explicit calibration restores the approved visible target.'
    }
  },
  @{
    name='central-blank'
    measurement='orange-component-union'
    search=@{x=310;y=66;width=85;height=79}
    expected=@{x=323;y=68;width=60;height=61}
    tolerance=2
    thresholds=@{
      minimumRed=200
      minimumGreen=100
      maximumBlue=80
      minimumRedOverGreen=40
      minimumComponentPixelCount=50
      maximumComponentWidth=35
      maximumComponentHeight=35
      ignoredVerticalGuideX=352
      ignoredVerticalGuideY=66
      ignoredVerticalGuideWidth=2
      ignoredVerticalGuideHeight=79
      ignoredHorizontalGuideX=310
      ignoredHorizontalGuideY=99
      ignoredHorizontalGuideWidth=85
      ignoredHorizontalGuideHeight=2
      ignoredGuideReason='Exclude the code-native vertical and horizontal guide lines before selecting blank quadrants.'
      requiredComponentAnchors=@(
        @{name='top-left';x=323;y=72;width=29;height=27}
        @{name='top-right';x=354;y=72;width=28;height=27}
        @{name='bottom-left';x=323;y=101;width=29;height=27}
        @{name='bottom-right';x=354;y=101;width=28;height=27}
      )
      componentAnchorReason='Require one size-qualified decoded orange component in each fixed-reference central Blank quadrant; exclude neighboring bank components without clipping measured bounds.'
    }
    boundsAdjustment=@{
      coordinateOrigin='crop-top-left'
      unit='px'
      x=0
      y=-4
      width=1
      height=5
      reason='The central blank decoded reference omits transparent/antialiased margins after guide-line exclusion; this explicit calibration restores the approved visible target.'
    }
  },
  @{
    name='block-bank'
    measurement='edge-component-union'
    search=@{x=35;y=97;width=660;height=110}
    expected=@{x=45;y=107;width=639;height=89}
    tolerance=4
    thresholds=@{
      minimumChannelDifference=5
      minimumComponentPixelCount=5
      excludeSearchBorderComponents=$true
      ignoredRegionX=310
      ignoredRegionY=97
      ignoredRegionWidth=85
      ignoredRegionHeight=48
      ignoredRegionReason='Exclude the overlapping central icon from the block-bank edge union.'
    }
    internalTopology=@{
      measurement='orange-column-occupancy-profile'
      search=@{x=35;y=107;width=660;height=89}
      thresholds=@{
        minimumRed=100
        minimumRedOverGreen=15
        maximumBlue=130
        minimumQualifyingPixelsPerColumn=3
        reason='Reference-calibrated decoded orange columns within the fixed block-bank content band.'
      }
      acceptance=@{
        maximumSpanEdgeDeviationPx=4
        maximumOccupiedColumnCountDelta=20
        minimumProfileJaccard=0.95
      }
    }
    boundsAdjustment=@{
      coordinateOrigin='crop-top-left'
      unit='px'
      x=-9
      y=0
      width=-1
      height=0
      reason='The block-bank asset includes a 9 px near-background/transparent left margin; this explicit calibration converts decoded edge union bounds to the approved visible target.'
    }
  },
  @{ name='input'; measurement='neutral-largest-component'; search=@{x=105;y=194;width=510;height=75}; expected=@{x=115;y=204;width=482;height=60}; tolerance=2; thresholds=@{minimumLuminanceInclusive=40;maximumLuminanceInclusive=140;maximumChannelSpread=5} }
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
$figure9FileName = ([char]0x56FE).ToString() + '9.png'
$figure11FileName = ([char]0x56FE).ToString() + '11.png'
$figure12FileName = ([char]0x56FE).ToString() + '12.png'
$figure13FileName = ([char]0x56FE).ToString() + '13.png'
$referenceByCapture = [ordered]@{
  'home'=$figure9FileName
  'discovered-prefill'=$figure9FileName
  'room-host'=$figure11FileName
  'room-full'=$figure12FileName
  'room-ready'=$figure13FileName
}
$roomSlotRoots = @(
  @{x=200;y=178;width=364;height=665;bodyX=226},
  @{x=589;y=178;width=364;height=665;bodyX=615},
  @{x=977;y=178;width=364;height=665;bodyX=1003},
  @{x=1365;y=178;width=364;height=665;bodyX=1391}
)
$roomProtectedRegions = [ordered]@{
  hostSlot=@{x=200;y=178;width=364;height=665}
  slot2=@{x=589;y=178;width=364;height=665}
  slot3=@{x=977;y=178;width=364;height=665}
  primaryAction=@{x=1487;y=943;width=432;height=95}
  leave=@{x=44;y=30;width=118;height=53}
}
$roomExclusionsByCapture = @{
  'room-host'=@(
    @{name='upper-scrolling-comments';reason='Figure 11 upper scrolling comments are outside room UI acceptance.';x=200;y=0;width=1500;height=140}
  )
  'room-full'=@(
    @{name='upper-scrolling-comments';reason='Figure 12 upper scrolling comments are outside room UI acceptance.';x=200;y=0;width=1500;height=140},
    @{name='right-side-popup';reason='Figure 12 right-side popup overlaps the obscured fourth-slot side and non-room pixels.';x=1660;y=140;width=260;height=760},
    @{name='complete-fourth-slot';reason='Figure 12 popup obscures the complete fourth-slot reference.';x=1365;y=178;width=364;height=665}
  )
  'room-ready'=@(
    @{name='upper-scrolling-comments';reason='Figure 13 upper scrolling comments are outside room UI acceptance.';x=200;y=0;width=1500;height=140},
    @{name='right-side-popup';reason='Figure 13 right-side popup overlaps the obscured fourth-slot side and non-room pixels.';x=1660;y=140;width=260;height=760},
    @{name='complete-fourth-slot';reason='Figure 13 popup obscures the complete fourth-slot reference.';x=1365;y=178;width=364;height=665}
  )
}
$roomRegions = @(
  @{ name='ignored-upper-comments'; x=0.1041666667; y=0.00; width=0.78125; height=0.1296296296; mask=$true },
  @{ name='room-background'; x=0.22; y=0.00; width=0.78; height=1.00; mask=$false },
  @{ name='player-card-layout'; x=0.11; y=0.16; width=0.80; height=0.66; mask=$false }
)

function New-LanLobbyRoomGateSpec(
    [string] $Name,
    [string] $Capture,
    $Roi,
    [ValidateSet('Cyan','Gray','Dark','DarkOnCyan','Light','Contrast','CreatorTagCyan','MutedLight')] [string] $MaskKind,
    [bool] $IsContour,
    $DiagnosticRectTransform,
    [ValidateSet('VisiblePlacement','PortraitFrame')] [string] $GateKind = 'VisiblePlacement',
    [int] $SlotIndex = -1,
    $TopBarRoi = $null)
{
    return [pscustomobject][ordered]@{
        name=$Name
        capture=$Capture
        gateKind=$GateKind
        slotIndex=$SlotIndex
        roi=[pscustomobject]$Roi
        topBarRoi=$(if ($null -eq $TopBarRoi) { $null } else { [pscustomobject]$TopBarRoi })
        exclusions=@($roomExclusionsByCapture[$Capture] | ForEach-Object { [pscustomobject]$_ })
        maskKind=$MaskKind
        isContour=$IsContour
        thresholds=$(if ($GateKind -ceq 'PortraitFrame') {
            [pscustomobject][ordered]@{
                maximumEdgeErrorPx=4
                minimumContourJaccard=0.95
                maximumCenterErrorPxPerAxis=2
            }
        } elseif ($IsContour) {
            [pscustomobject][ordered]@{ maximumEdgeErrorPx=4; minimumContourJaccard=0.95 }
        } else {
            [pscustomobject][ordered]@{ maximumCenterErrorPxPerAxis=2; maximumVisibleSizeErrorPx=3 }
        })
        diagnosticRectTransform=$DiagnosticRectTransform
    }
}

function Get-LanLobbyRoomGateSpecs($Capture)
{
    $captureName = [string]$Capture.name
    $prefix = switch ($captureName) { 'room-host' {'RoomHost'} 'room-full' {'RoomFull'} 'room-ready' {'RoomReady'} }
    $diagnosticRect = @($Capture.rects | Where-Object { $_ -and [string]$_.name -match 'RoomCard_0$' } | Select-Object -First 1)
    $diagnostic = if ($diagnosticRect.Count -eq 1) { [pscustomobject][ordered]@{ x=$diagnosticRect[0].x;y=$diagnosticRect[0].y;width=$diagnosticRect[0].width;height=$diagnosticRect[0].height;role='diagnostic-only' } } else { $null }
    $gates = @()
    if ($captureName -eq 'room-host')
    {
        $gates += New-LanLobbyRoomGateSpec 'RoomHost.Slot1.ReadyTopBar' $captureName @{x=218;y=170;width=337;height=46} 'Cyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomHost.Slot1.ReadyContour' $captureName @{x=218;y=208;width=337;height=523} 'Cyan' $true $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomHost.Slot1.ReadyCheck' $captureName @{x=305;y=655;width=55;height=45} 'DarkOnCyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomHost.Slot1.ReadyLabel' $captureName @{x=360;y=655;width=110;height=45} 'DarkOnCyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomHost.Slot1.CreatorTag' $captureName @{x=315;y=225;width=145;height=50} 'CreatorTagCyan' $false $diagnostic
        for ($slot=1;$slot -lt 4;$slot++)
        {
            $root=$roomSlotRoots[$slot]
            $gates += New-LanLobbyRoomGateSpec "RoomHost.Slot$($slot+1).EmptyComposition" $captureName @{x=$root.x;y=$root.y;width=$root.width;height=$root.height} 'Gray' $true $diagnostic
        }
    }
    elseif ($captureName -eq 'room-full')
    {
        $gates += New-LanLobbyRoomGateSpec 'RoomFull.Slot1.ReadyTopBar' $captureName @{x=218;y=170;width=337;height=46} 'Cyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomFull.Slot1.ReadyContour' $captureName @{x=218;y=208;width=337;height=523} 'Cyan' $true $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomFull.Slot1.ReadyCheck' $captureName @{x=305;y=655;width=55;height=45} 'DarkOnCyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomFull.Slot1.ReadyLabel' $captureName @{x=360;y=655;width=110;height=45} 'DarkOnCyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomFull.Slot1.CreatorTag' $captureName @{x=315;y=225;width=145;height=50} 'CreatorTagCyan' $false $diagnostic
        foreach ($slot in @(1,2))
        {
            $root=$roomSlotRoots[$slot]
            $gates += New-LanLobbyRoomGateSpec "RoomFull.Slot$($slot+1).WaitingTopBar" $captureName @{x=($root.bodyX-8);y=170;width=337;height=46} 'Gray' $false $diagnostic
            $gates += New-LanLobbyRoomGateSpec "RoomFull.Slot$($slot+1).WaitingContour" $captureName @{x=($root.bodyX-8);y=208;width=337;height=523} 'Gray' $true $diagnostic
        }
    }
    elseif ($captureName -eq 'room-ready')
    {
        $gates += New-LanLobbyRoomGateSpec 'RoomReady.Slot1.ReadyTopBar' $captureName @{x=218;y=170;width=337;height=46} 'Cyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomReady.Slot1.ReadyContour' $captureName @{x=218;y=208;width=337;height=523} 'Cyan' $true $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomReady.Slot1.ReadyCheck' $captureName @{x=305;y=655;width=55;height=45} 'DarkOnCyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomReady.Slot1.ReadyLabel' $captureName @{x=360;y=655;width=110;height=45} 'DarkOnCyan' $false $diagnostic
        $gates += New-LanLobbyRoomGateSpec 'RoomReady.Slot1.CreatorTag' $captureName @{x=315;y=225;width=145;height=50} 'CreatorTagCyan' $false $diagnostic
        foreach ($slot in @(1,2))
        {
            $root=$roomSlotRoots[$slot]
            $slotNumber=$slot+1
            $gates += New-LanLobbyRoomGateSpec "RoomReady.Slot$slotNumber.ReadyTopBar" $captureName @{x=($root.bodyX-8);y=170;width=337;height=46} 'Cyan' $false $diagnostic
            $gates += New-LanLobbyRoomGateSpec "RoomReady.Slot$slotNumber.ReadyContour" $captureName @{x=($root.bodyX-8);y=208;width=337;height=523} 'Cyan' $true $diagnostic
            $gates += New-LanLobbyRoomGateSpec "RoomReady.Slot$slotNumber.ReadyCheck" $captureName @{x=($root.bodyX+79);y=655;width=55;height=45} 'DarkOnCyan' $false $diagnostic
            $gates += New-LanLobbyRoomGateSpec "RoomReady.Slot$slotNumber.ReadyLabel" $captureName @{x=($root.bodyX+134);y=655;width=110;height=45} 'DarkOnCyan' $false $diagnostic
        }
    }
    $lastPortraitSlot = if ($captureName -eq 'room-host') { 3 } else { 2 }
    for ($slot=0; $slot -le $lastPortraitSlot; $slot++)
    {
        $root = $roomSlotRoots[$slot]
        $slotNumber = $slot + 1
        $maskKind = if ($captureName -eq 'room-ready' -or $slot -eq 0) { 'Cyan' } else { 'Gray' }
        $frameRoi = @{x=($root.bodyX-8);y=208;width=337;height=523}
        $topBarRoi = @{x=($root.bodyX-8);y=170;width=337;height=46}
        $gates += New-LanLobbyRoomGateSpec "$prefix.Slot$slotNumber.PortraitFrame" $captureName $frameRoi $maskKind $true $diagnostic 'PortraitFrame' $slot $topBarRoi
    }
    if ($captureName -in @('room-full','room-ready'))
    {
        $color = if ($captureName -eq 'room-full') { 'Gray' } else { 'Cyan' }
        $gates += New-LanLobbyRoomGateSpec "$prefix.PrimaryAction.$color" $captureName @{x=1479;y=935;width=441;height=104} $color $false $diagnostic
        if ($captureName -eq 'room-full')
        {
            $gates += New-LanLobbyRoomGateSpec "$prefix.PrimaryAction.IconCenter" $captureName @{x=1555;y=955;width=85;height=70} 'MutedLight' $false $diagnostic
            $gates += New-LanLobbyRoomGateSpec "$prefix.PrimaryAction.LabelCenter" $captureName @{x=1640;y=955;width=155;height=75} 'MutedLight' $false $diagnostic
        }
        else
        {
            $gates += New-LanLobbyRoomGateSpec "$prefix.PrimaryAction.IconCenter" $captureName @{x=1560;y=955;width=85;height=75} 'DarkOnCyan' $false $diagnostic
            $gates += New-LanLobbyRoomGateSpec "$prefix.PrimaryAction.LabelCenter" $captureName @{x=1645;y=955;width=150;height=75} 'DarkOnCyan' $false $diagnostic
        }
    }
    if ($captureName -eq 'room-ready')
    {
        $gates += New-LanLobbyRoomGateSpec 'RoomReady.Leave' $captureName @{x=45;y=25;width=75;height=65} 'Light' $false $diagnostic
    }
    return @($gates)
}

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

function ConvertTo-LanLobbyRectangle($Record)
{
    return New-Object Drawing.Rectangle ([int]$Record.x), ([int]$Record.y), ([int]$Record.width), ([int]$Record.height)
}

function Get-LanLobbyVisibleBounds
{
    param(
        [Parameter(Mandatory)] [Drawing.Bitmap] $Image,
        [Parameter(Mandatory)] $Roi,
        [Parameter(Mandatory)] [ValidateSet('Cyan','Gray','Dark','DarkOnCyan','Light','Contrast','CreatorTagCyan','MutedLight')] [string] $MaskKind,
        $Exclusions = @()
    )

    $roiRectangle = ConvertTo-LanLobbyRectangle $Roi
    [Drawing.Rectangle[]]$exclusionRectangles = @($Exclusions | ForEach-Object { ConvertTo-LanLobbyRectangle $_ })
    $measurement = [LanLobbyVisualDiff]::FindVisibleMaskBounds($Image, $roiRectangle, $MaskKind, $exclusionRectangles)
    if (-not $measurement.Available)
    {
        return [pscustomobject][ordered]@{
            available=$false
            failureReason=$measurement.FailureReason
            bounds=$null
            center=$null
            pixelCount=0
        }
    }
    $bounds = $measurement.Bounds
    return [pscustomobject][ordered]@{
        available=$true
        failureReason=$null
        bounds=[pscustomobject](ConvertTo-LanLobbyBoundsObject $bounds)
        center=[pscustomobject][ordered]@{
            x=$bounds.X + ($bounds.Width - 1) / 2.0
            y=$bounds.Y + ($bounds.Height - 1) / 2.0
        }
        pixelCount=[int]$measurement.PixelCount
    }
}

function Get-LanLobbyPortraitFrameEdgeBounds
{
    param(
        [Parameter(Mandatory)] [Drawing.Bitmap] $Image,
        [Parameter(Mandatory)] $Roi,
        [Parameter(Mandatory)] [ValidateSet('Cyan','Gray')] [string] $MaskKind,
        $Exclusions = @()
    )

    $roiRectangle = ConvertTo-LanLobbyRectangle $Roi
    [Drawing.Rectangle[]]$exclusionRectangles = @($Exclusions | ForEach-Object { ConvertTo-LanLobbyRectangle $_ })
    $measurement = [LanLobbyVisualDiff]::FindLongVerticalMaskEdgeBounds(
        $Image,
        $roiRectangle,
        $MaskKind,
        $exclusionRectangles)
    if (-not $measurement.Available)
    {
        return [pscustomobject][ordered]@{
            available=$false
            failureReason=$measurement.FailureReason
            bounds=$null
            center=$null
            pixelCount=0
        }
    }
    $bounds = $measurement.Bounds
    return [pscustomobject][ordered]@{
        available=$true
        failureReason=$null
        bounds=[pscustomobject](ConvertTo-LanLobbyBoundsObject $bounds)
        center=[pscustomobject][ordered]@{
            x=$bounds.X + ($bounds.Width - 1) / 2.0
            y=$bounds.Y + ($bounds.Height - 1) / 2.0
        }
        pixelCount=[int]$measurement.PixelCount
    }
}

function Get-LanLobbyPortraitLowerDecorationTop
{
    param(
        [Parameter(Mandatory)] [Drawing.Bitmap] $Image,
        [Parameter(Mandatory)] $Roi,
        $Exclusions = @()
    )

    [Drawing.Rectangle[]]$exclusionRectangles = @($Exclusions | ForEach-Object { ConvertTo-LanLobbyRectangle $_ })
    $measurement = [LanLobbyVisualDiff]::FindDominantLowerHorizontalContrastEdge(
        $Image,
        (ConvertTo-LanLobbyRectangle $Roi),
        $exclusionRectangles)
    return [pscustomobject][ordered]@{
        available=[bool]$measurement.Available
        failureReason=$measurement.FailureReason
        y=$(if ($measurement.Available) { [int]$measurement.Bounds.Y } else { $null })
        qualifyingPixelCount=[int]$measurement.PixelCount
    }
}

function Get-LanLobbyDeterministicMedian([double[]] $Values)
{
    if ($null -eq $Values -or $Values.Count -eq 0)
    {
        throw 'Cannot calculate a portrait-frame consensus median from zero values.'
    }
    [double[]]$sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2.0)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle-1]+[double]$sorted[$middle])/2.0
}

function Get-LanLobbyPortraitRelativeMetrics
{
    param(
        [Parameter(Mandatory)] $FramePlacement,
        [Parameter(Mandatory)] $TopBarPlacement,
        [Parameter(Mandatory)] $LowerDecorationTop
    )

    if (-not $FramePlacement.available -or -not $TopBarPlacement.available -or -not $LowerDecorationTop.available)
    {
        throw "Portrait-frame relative metrics require decoded frame, top-bar, and lower-decoration pixels. $($FramePlacement.failureReason) / $($TopBarPlacement.failureReason) / $($LowerDecorationTop.failureReason)"
    }
    $frameRight = [double]$FramePlacement.bounds.x+[double]$FramePlacement.bounds.width
    $frameBottom = [double]$FramePlacement.bounds.y+[double]$FramePlacement.bounds.height
    $topBarRight = [double]$TopBarPlacement.bounds.x+[double]$TopBarPlacement.bounds.width
    $topBarBottom = [double]$TopBarPlacement.bounds.y+[double]$TopBarPlacement.bounds.height
    return [pscustomobject][ordered]@{
        frameLeftFromTopBarLeftPx=[double]$FramePlacement.bounds.x-[double]$TopBarPlacement.bounds.x
        frameRightFromTopBarRightPx=$frameRight-$topBarRight
        frameCenterFromTopBarCenterPx=[double]$FramePlacement.center.x-[double]$TopBarPlacement.center.x
        frameWidthFromTopBarWidthPx=[double]$FramePlacement.bounds.width-[double]$TopBarPlacement.bounds.width
        frameTopFromTopBarBottomPx=[double]$FramePlacement.bounds.y-$topBarBottom
        frameBottomFromLowerDecorationTopPx=$frameBottom-[double]$LowerDecorationTop.y
    }
}

function Get-LanLobbyPortraitFrameConsensus
{
    param(
        [Parameter(Mandatory)] $RoomCaptures,
        [Parameter(Mandatory)] $ReferencePaths,
        [Parameter(Mandatory)] $ReferenceByCapture
    )

    $contributors = @()
    foreach ($capture in @($RoomCaptures | Sort-Object name))
    {
        $captureName = [string]$capture.name
        $referenceFileName = [string]$ReferenceByCapture[$captureName]
        $native = $null
        $normalized = $null
        try
        {
            $native = [Drawing.Bitmap]::FromFile([string]$ReferencePaths[$referenceFileName])
            $normalized = New-Object Drawing.Bitmap 1920,1080
            $graphics = [Drawing.Graphics]::FromImage($normalized)
            try
            {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
                $graphics.DrawImage($native,0,0,1920,1080)
            }
            finally { $graphics.Dispose() }
            foreach ($gate in @(Get-LanLobbyRoomGateSpecs $capture | Where-Object gateKind -ceq 'PortraitFrame'))
            {
                $frame = Get-LanLobbyPortraitFrameEdgeBounds -Image $normalized -Roi $gate.roi -MaskKind $gate.maskKind -Exclusions $gate.exclusions
                $topBar = Get-LanLobbyPortraitFrameEdgeBounds -Image $normalized -Roi $gate.topBarRoi -MaskKind $gate.maskKind -Exclusions $gate.exclusions
                $lowerTop = Get-LanLobbyPortraitLowerDecorationTop -Image $normalized -Roi $gate.roi -Exclusions $gate.exclusions
                try { $metrics = Get-LanLobbyPortraitRelativeMetrics $frame $topBar $lowerTop }
                catch { throw "Portrait-frame consensus contributor '$($gate.name)' failed: $($_.Exception.Message)" }
                $contributors += [pscustomobject][ordered]@{
                    gateName=[string]$gate.name
                    capture=$captureName
                    slotNumber=[int]$gate.slotIndex+1
                    referenceFigure=$referenceFileName
                    eligible=$true
                    exclusionReason=$null
                    frameVisibleBounds=$frame.bounds
                    topBarVisibleBounds=$topBar.bounds
                    lowerDecorationTopY=[int]$lowerTop.y
                    relativeMetrics=$metrics
                }
            }
        }
        finally
        {
            if ($normalized) { $normalized.Dispose() }
            if ($native) { $native.Dispose() }
        }
    }
    if ($contributors.Count -ne 10)
    {
        throw "Portrait-frame relative consensus requires exactly ten eligible unoccluded reference contributors; found $($contributors.Count)."
    }
    $topMedian = Get-LanLobbyDeterministicMedian ([double[]]@(
        $contributors | ForEach-Object { [double]$_.relativeMetrics.frameTopFromTopBarBottomPx }
    ))
    $bottomMedian = Get-LanLobbyDeterministicMedian ([double[]]@(
        $contributors | ForEach-Object { [double]$_.relativeMetrics.frameBottomFromLowerDecorationTopPx }
    ))
    return [pscustomobject][ordered]@{
        acceptanceRole='blocking'
        coordinateSpace='frame-relative-to-own-decoded-topbar-and-lower-decoration'
        calculationRule='median-of-eligible-reference-relative-offsets-even-mean-middle-two'
        contributorCount=$contributors.Count
        contributors=@($contributors)
        target=[pscustomobject][ordered]@{
            topBarHorizontalCenterDeltaPx=0.0
            topBarWidthDeltaPx=0.0
            frameTopFromTopBarBottomPx=[double]$topMedian
            frameBottomFromLowerDecorationTopPx=[double]$bottomMedian
        }
    }
}

function Measure-LanLobbyPortraitRelativePlacement
{
    param(
        [Parameter(Mandatory)] $FramePlacement,
        [Parameter(Mandatory)] $TopBarPlacement,
        [Parameter(Mandatory)] $LowerDecorationTop,
        [Parameter(Mandatory)] $Consensus,
        [Parameter(Mandatory)] $Thresholds
    )

    $actualMetrics = Get-LanLobbyPortraitRelativeMetrics $FramePlacement $TopBarPlacement $LowerDecorationTop
    $targetTop = [double]$TopBarPlacement.bounds.y+[double]$TopBarPlacement.bounds.height+
        [double]$Consensus.target.frameTopFromTopBarBottomPx
    $targetBottom = [double]$LowerDecorationTop.y+
        [double]$Consensus.target.frameBottomFromLowerDecorationTopPx
    $targetLeft = [double]$TopBarPlacement.bounds.x
    $targetRight = [double]$TopBarPlacement.bounds.x+[double]$TopBarPlacement.bounds.width
    $actualLeft = [double]$FramePlacement.bounds.x
    $actualTop = [double]$FramePlacement.bounds.y
    $actualRight = $actualLeft+[double]$FramePlacement.bounds.width
    $actualBottom = $actualTop+[double]$FramePlacement.bounds.height
    $targetWidth = $targetRight-$targetLeft
    $targetHeight = $targetBottom-$targetTop
    if ($targetWidth -le 0 -or $targetHeight -le 0)
    {
        throw "Portrait-frame relative consensus produced a non-positive target size: ${targetWidth}x${targetHeight}."
    }
    $intersectionWidth = [Math]::Max(0.0,[Math]::Min($actualRight,$targetRight)-[Math]::Max($actualLeft,$targetLeft))
    $intersectionHeight = [Math]::Max(0.0,[Math]::Min($actualBottom,$targetBottom)-[Math]::Max($actualTop,$targetTop))
    $intersection = $intersectionWidth*$intersectionHeight
    $union = ([double]$FramePlacement.bounds.width*[double]$FramePlacement.bounds.height)+
        ($targetWidth*$targetHeight)-$intersection
    $jaccard = if ($union -le 0) { 0.0 } else { $intersection/$union }
    $edgeDelta = [pscustomobject][ordered]@{
        unit='px'
        left=[double]$actualMetrics.frameLeftFromTopBarLeftPx
        top=[double]$actualMetrics.frameTopFromTopBarBottomPx-[double]$Consensus.target.frameTopFromTopBarBottomPx
        right=[double]$actualMetrics.frameRightFromTopBarRightPx
        bottom=[double]$actualMetrics.frameBottomFromLowerDecorationTopPx-[double]$Consensus.target.frameBottomFromLowerDecorationTopPx
    }
    $centerDelta = [pscustomobject][ordered]@{
        unit='px'
        deltaX=[double]$actualMetrics.frameCenterFromTopBarCenterPx
        deltaY=(($actualTop+$actualBottom)/2.0)-(($targetTop+$targetBottom)/2.0)
    }
    $sizeDelta = [pscustomobject][ordered]@{
        unit='px'
        deltaWidth=[double]$FramePlacement.bounds.width-$targetWidth
        deltaHeight=[double]$FramePlacement.bounds.height-$targetHeight
    }
    $contour = [pscustomobject][ordered]@{
        intersectionPixels=[Math]::Round($intersection,3)
        unionPixels=[Math]::Round($union,3)
        jaccard=[Math]::Round($jaccard,6)
    }
    $passed =
        [Math]::Abs([double]$edgeDelta.left) -le [double]$Thresholds.maximumEdgeErrorPx -and
        [Math]::Abs([double]$edgeDelta.top) -le [double]$Thresholds.maximumEdgeErrorPx -and
        [Math]::Abs([double]$edgeDelta.right) -le [double]$Thresholds.maximumEdgeErrorPx -and
        [Math]::Abs([double]$edgeDelta.bottom) -le [double]$Thresholds.maximumEdgeErrorPx -and
        [Math]::Abs([double]$centerDelta.deltaX) -le [double]$Thresholds.maximumCenterErrorPxPerAxis -and
        [Math]::Abs([double]$centerDelta.deltaY) -le [double]$Thresholds.maximumCenterErrorPxPerAxis -and
        [double]$contour.jaccard -ge [double]$Thresholds.minimumContourJaccard
    return [pscustomobject][ordered]@{
        acceptanceRole='blocking'
        coordinateSpace=[string]$Consensus.coordinateSpace
        actualMetrics=$actualMetrics
        targetMetrics=$Consensus.target
        lowerDecorationTop=[pscustomobject]$LowerDecorationTop
        normalizedTargetBounds=[pscustomobject][ordered]@{
            x=$targetLeft;y=$targetTop;width=$targetWidth;height=$targetHeight
        }
        edgeDeltaPx=$edgeDelta
        centerDeltaPx=$centerDelta
        sizeDeltaPx=$sizeDelta
        contour=$contour
        thresholds=$Thresholds
        passed=$passed
    }
}

function Measure-LanLobbyVisiblePlacement
{
    param(
        [Parameter(Mandatory)] [Drawing.Bitmap] $ActualImage,
        [Parameter(Mandatory)] [Drawing.Bitmap] $ReferenceImage,
        [Parameter(Mandatory)] $Gate
    )

    $isPortraitFrame = [string]$Gate.gateKind -ceq 'PortraitFrame'
    $actual = if ($isPortraitFrame) {
        Get-LanLobbyPortraitFrameEdgeBounds -Image $ActualImage -Roi $Gate.roi -MaskKind $Gate.maskKind -Exclusions $Gate.exclusions
    } else {
        Get-LanLobbyVisibleBounds -Image $ActualImage -Roi $Gate.roi -MaskKind $Gate.maskKind -Exclusions $Gate.exclusions
    }
    $reference = if ($isPortraitFrame) {
        Get-LanLobbyPortraitFrameEdgeBounds -Image $ReferenceImage -Roi $Gate.roi -MaskKind $Gate.maskKind -Exclusions $Gate.exclusions
    } else {
        Get-LanLobbyVisibleBounds -Image $ReferenceImage -Roi $Gate.roi -MaskKind $Gate.maskKind -Exclusions $Gate.exclusions
    }
    $thresholds = [pscustomobject]$Gate.thresholds
    if (-not $actual.available -or -not $reference.available)
    {
        return [pscustomobject][ordered]@{
            actual=$actual
            reference=$reference
            edgeDeltaPx=$null
            centerDeltaPx=$null
            sizeDeltaPx=$null
            contour=[pscustomobject][ordered]@{ intersectionPixels=0; unionPixels=0; jaccard=0.0 }
            thresholds=$thresholds
            status='Failed'
            reason=@($actual.failureReason,$reference.failureReason | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ' / '
            passed=$false
        }
    }

    $actualRight = $actual.bounds.x + $actual.bounds.width
    $referenceRight = $reference.bounds.x + $reference.bounds.width
    $actualBottom = $actual.bounds.y + $actual.bounds.height
    $referenceBottom = $reference.bounds.y + $reference.bounds.height
    $edgeDelta = [pscustomobject][ordered]@{
        unit='px'
        left=$actual.bounds.x - $reference.bounds.x
        top=$actual.bounds.y - $reference.bounds.y
        right=$actualRight - $referenceRight
        bottom=$actualBottom - $referenceBottom
    }
    $centerDelta = [pscustomobject][ordered]@{
        unit='px'
        deltaX=$actual.center.x - $reference.center.x
        deltaY=$actual.center.y - $reference.center.y
    }
    $sizeDelta = [pscustomobject][ordered]@{
        unit='px'
        deltaWidth=$actual.bounds.width - $reference.bounds.width
        deltaHeight=$actual.bounds.height - $reference.bounds.height
    }
    [Drawing.Rectangle[]]$exclusionRectangles = @($Gate.exclusions | ForEach-Object { ConvertTo-LanLobbyRectangle $_ })
    $comparison = if ($isPortraitFrame) {
        [LanLobbyVisualDiff]::CompareBounds(
            (New-Object LanLobbyVisualBounds -Property @{
                X=[int]$actual.bounds.x;Y=[int]$actual.bounds.y;Width=[int]$actual.bounds.width;Height=[int]$actual.bounds.height
            }),
            (New-Object LanLobbyVisualBounds -Property @{
                X=[int]$reference.bounds.x;Y=[int]$reference.bounds.y;Width=[int]$reference.bounds.width;Height=[int]$reference.bounds.height
            }))
    } else {
        [LanLobbyVisualDiff]::CompareVisibleMasks(
            $ActualImage,
            $ReferenceImage,
            (ConvertTo-LanLobbyRectangle $Gate.roi),
            [string]$Gate.maskKind,
            $exclusionRectangles)
    }
    $contour = [pscustomobject][ordered]@{
        intersectionPixels=[long]$comparison.IntersectionPixels
        unionPixels=[long]$comparison.UnionPixels
        jaccard=[Math]::Round([double]$comparison.Jaccard, 6)
    }
    $isContour = [bool]$Gate.isContour
    $passed = if ([string]$Gate.gateKind -ceq 'PortraitFrame')
    {
        [Math]::Abs($edgeDelta.left) -le [double]$thresholds.maximumEdgeErrorPx -and
        [Math]::Abs($edgeDelta.top) -le [double]$thresholds.maximumEdgeErrorPx -and
        [Math]::Abs($edgeDelta.right) -le [double]$thresholds.maximumEdgeErrorPx -and
        [Math]::Abs($edgeDelta.bottom) -le [double]$thresholds.maximumEdgeErrorPx -and
        [Math]::Abs($centerDelta.deltaX) -le [double]$thresholds.maximumCenterErrorPxPerAxis -and
        [Math]::Abs($centerDelta.deltaY) -le [double]$thresholds.maximumCenterErrorPxPerAxis -and
        $contour.jaccard -ge [double]$thresholds.minimumContourJaccard
    }
    elseif ($isContour)
    {
        [Math]::Abs($edgeDelta.left) -le [double]$thresholds.maximumEdgeErrorPx -and
        [Math]::Abs($edgeDelta.top) -le [double]$thresholds.maximumEdgeErrorPx -and
        [Math]::Abs($edgeDelta.right) -le [double]$thresholds.maximumEdgeErrorPx -and
        [Math]::Abs($edgeDelta.bottom) -le [double]$thresholds.maximumEdgeErrorPx -and
        $contour.jaccard -ge [double]$thresholds.minimumContourJaccard
    }
    else
    {
        [Math]::Abs($centerDelta.deltaX) -le [double]$thresholds.maximumCenterErrorPxPerAxis -and
        [Math]::Abs($centerDelta.deltaY) -le [double]$thresholds.maximumCenterErrorPxPerAxis -and
        [Math]::Abs($sizeDelta.deltaWidth) -le [double]$thresholds.maximumVisibleSizeErrorPx -and
        [Math]::Abs($sizeDelta.deltaHeight) -le [double]$thresholds.maximumVisibleSizeErrorPx
    }
    return [pscustomobject][ordered]@{
        actual=$actual
        reference=$reference
        edgeDeltaPx=$edgeDelta
        centerDeltaPx=$centerDelta
        sizeDeltaPx=$sizeDelta
        contour=$contour
        thresholds=$thresholds
        status=$(if ($passed) { 'Passed' } else { 'Failed' })
        reason=$(if ($passed) { 'Visible color/contrast pixels satisfy the blocking placement thresholds.' } else { 'Visible color/contrast pixels exceed a blocking placement threshold.' })
        passed=$passed
    }
}

function Get-LanLobbyCaptureKeyRect($Capture, [string] $Name)
{
    if ($null -eq $Capture.PSObject.Properties['keyRects'])
    {
        throw "Capture '$($Capture.name)' has no keyRects collection required for portrait-frame evidence."
    }
    [array]$matches = @($Capture.keyRects | Where-Object { $null -ne $_ -and [string]$_.name -ceq $Name })
    if ($matches.Count -ne 1)
    {
        throw "Capture '$($Capture.name)' portrait-frame key rect '$Name' must occur exactly once."
    }
    $rect = $matches[0]
    if ([string]$rect.coordinateOrigin -cne 'screen-bottom-left' -or [string]$rect.unit -cne 'px')
    {
        throw "Capture '$($Capture.name)' portrait-frame key rect '$Name' must declare coordinateOrigin=screen-bottom-left and unit=px."
    }
    foreach ($propertyName in @('x','y','width','height'))
    {
        if ($null -eq $rect.PSObject.Properties[$propertyName] -or
            $null -eq $rect.$propertyName -or
            -not ($rect.$propertyName -is [ValueType]))
        {
            throw "Capture '$($Capture.name)' portrait-frame key rect '$Name' must declare numeric x, y, width, and height."
        }
    }
    return $rect
}

function Get-LanLobbyPortraitFrameSharedGeometry($RoomCaptures)
{
    $records = @()
    for ($slot=0; $slot -lt 4; $slot++)
    {
        $slotRootName = "LanLobbyRoot/Room/RoomCard_$slot"
        $frameName = "$slotRootName/CardBody"
        $slotRecords = @(
            foreach ($capture in @($RoomCaptures | Sort-Object name))
            {
                $root = Get-LanLobbyCaptureKeyRect $capture $slotRootName
                $frame = Get-LanLobbyCaptureKeyRect $capture $frameName
                [pscustomobject][ordered]@{
                    capture=[string]$capture.name
                    slotNumber=$slot+1
                    widthPx=[double]$frame.width
                    heightPx=[double]$frame.height
                    localOffsetPx=[pscustomobject][ordered]@{
                        x=[double]$frame.x-[double]$root.x
                        y=[double]$frame.y-[double]$root.y
                    }
                }
            }
        )
        $baseline = @($slotRecords | Where-Object capture -ceq 'room-host')[0]
        foreach ($record in $slotRecords)
        {
            $matches = [Math]::Abs([double]$record.widthPx-[double]$baseline.widthPx) -le 0.01 -and
                [Math]::Abs([double]$record.heightPx-[double]$baseline.heightPx) -le 0.01 -and
                [Math]::Abs([double]$record.localOffsetPx.x-[double]$baseline.localOffsetPx.x) -le 0.01 -and
                [Math]::Abs([double]$record.localOffsetPx.y-[double]$baseline.localOffsetPx.y) -le 0.01
            $records += [pscustomobject][ordered]@{
                capture=$record.capture
                slotNumber=$record.slotNumber
                widthPx=$record.widthPx
                heightPx=$record.heightPx
                localOffsetPx=$record.localOffsetPx
                baselineCapture='room-host'
                matchesBaseline=$matches
                passed=$matches
            }
        }
    }
    return [pscustomobject][ordered]@{
        acceptanceRole='blocking'
        comparisonSource='manifest-keyRects'
        coordinateOrigin='screen-bottom-left'
        unit='px'
        thresholds=[pscustomobject][ordered]@{ maximumWidthHeightOrLocalOffsetDeltaPx=0.01 }
        records=@($records)
        passed=(@($records | Where-Object { -not $_.passed }).Count -eq 0)
    }
}

function Measure-LanLobbyPortraitFrameRelation
{
    param(
        [Parameter(Mandatory)] $FramePlacement,
        [Parameter(Mandatory)] $TopBarPlacement,
        [Parameter(Mandatory)] $FrameRect,
        [Parameter(Mandatory)] $LowerDecorationRect,
        [Parameter(Mandatory)] [Drawing.Bitmap] $ActualImage,
        [Parameter(Mandatory)] $SeamRoi,
        [Parameter(Mandatory)] [ValidateSet('Cyan','Gray')] [string] $MaskKind
    )

    $thresholds = [pscustomobject][ordered]@{
        maximumHorizontalCenterDeltaPx=2
        maximumVisibleWidthErrorPx=3
        minimumGeometricOverlapPx=60
        maximumContinuousBackgroundGapPx=1
    }
    $centerDelta = if ($FramePlacement.available -and $TopBarPlacement.available) {
        [double]$FramePlacement.center.x-[double]$TopBarPlacement.center.x
    } else { $null }
    $widthDelta = if ($FramePlacement.available -and $TopBarPlacement.available) {
        [double]$FramePlacement.bounds.width-[double]$TopBarPlacement.bounds.width
    } else { $null }
    $intersectionBottom = [Math]::Max([double]$FrameRect.y,[double]$LowerDecorationRect.y)
    $intersectionTop = [Math]::Min(
        [double]$FrameRect.y+[double]$FrameRect.height,
        [double]$LowerDecorationRect.y+[double]$LowerDecorationRect.height)
    $geometricOverlap = [Math]::Max(0.0,$intersectionTop-$intersectionBottom)
    $maximumGap = [LanLobbyVisualDiff]::MaximumContinuousEmptyMaskRows(
        $ActualImage,
        (ConvertTo-LanLobbyRectangle $SeamRoi),
        $MaskKind)
    $passed = $null -ne $centerDelta -and $null -ne $widthDelta -and
        [Math]::Abs($centerDelta) -le [double]$thresholds.maximumHorizontalCenterDeltaPx -and
        [Math]::Abs($widthDelta) -le [double]$thresholds.maximumVisibleWidthErrorPx -and
        $geometricOverlap -ge [double]$thresholds.minimumGeometricOverlapPx -and
        $maximumGap -le [int]$thresholds.maximumContinuousBackgroundGapPx
    return [pscustomobject][ordered]@{
        topBarHorizontalCenterDeltaPx=$centerDelta
        topBarWidthDeltaPx=$widthDelta
        geometricOverlapPx=$geometricOverlap
        maximumContinuousBackgroundGapPx=$maximumGap
        seamRoi=[pscustomobject]$SeamRoi
        maskKind=$MaskKind
        thresholds=$thresholds
        passed=$passed
    }
}

function Test-LanLobbyRectanglesOverlap($Left, $Right)
{
    return $Left.x -lt ($Right.x + $Right.width) -and
        ($Left.x + $Left.width) -gt $Right.x -and
        $Left.y -lt ($Right.y + $Right.height) -and
        ($Left.y + $Left.height) -gt $Right.y
}

function Get-LanLobbyRoomMaterialEvidence($Capture, $SpriteUsage)
{
    $failures = @()
    $bijectionPassed = $true
    $identityPassed = $true
    $pathPassed = $true
    $shaPassed = $true
    $captureListPassed = $true
    $occurrencePassed = $true
    $aggregatePassed = $true
    $auditProperty = $Capture.PSObject.Properties['sourceAudit']
    [array]$auditRows = if ($null -eq $auditProperty) { @() } else { @($auditProperty.Value | Where-Object { $_ -and [bool]$_.isBitmap }) }
    [array]$renderedRows = @($Capture.spriteSources | Where-Object { $null -ne $_ })
    if ($auditRows.Count -eq 0)
    {
        $failures += "Capture '$($Capture.name)' has no bitmap sourceAudit rows."
        $bijectionPassed = $false
        $identityPassed = $false
        $pathPassed = $false
        $shaPassed = $false
        $captureListPassed = $false
        $occurrencePassed = $false
        $aggregatePassed = $false
    }
    if ($auditRows.Count -ne $renderedRows.Count)
    {
        $failures += "Capture '$($Capture.name)' bitmap sourceAudit count $($auditRows.Count) does not equal rendered spriteSources count $($renderedRows.Count)."
        $bijectionPassed = $false
    }
    foreach ($sprite in $renderedRows)
    {
        [array]$audit = @($auditRows | Where-Object { [string]$_.node -ceq [string]$sprite.node -and [string]$_.spriteName -ceq [string]$sprite.spriteName })
        [array]$approved = @($SpriteUsage | Where-Object { $_.CaptureName -ceq [string]$Capture.name -and $_.SpriteName -ceq [string]$sprite.spriteName })
        if ($audit.Count -ne 1)
        {
            $failures += "Rendered Sprite '$($sprite.node)' does not have exactly one matching bitmap sourceAudit row."
            $bijectionPassed = $false
            $identityPassed = $false
            continue
        }
        if ($approved.Count -ne 1)
        {
            $failures += "Rendered Sprite '$($sprite.node)' does not resolve to exactly one approved material inventory row."
            $identityPassed = $false
            $pathPassed = $false
            $shaPassed = $false
            $aggregatePassed = $false
            continue
        }
        $approvedRow = $approved[0]
        $auditRow = $audit[0]
        if ([string]$auditRow.kind -cne 'bitmap-sprite' -or -not [bool]$auditRow.isBitmap -or
            [string]$auditRow.node -cne [string]$sprite.node -or [string]$auditRow.spriteName -cne [string]$sprite.spriteName)
        {
            $failures += "Rendered Sprite '$($sprite.node)' bitmap identity fields do not match the same rendered occurrence."
            $identityPassed = $false
        }
        if ([string]$sprite.sourcePath -cne [string]$approvedRow.SourcePath -or
            [string]$auditRow.resourcesPath -cne [string]$approvedRow.ResourcesPath -or
            [string]$auditRow.sourcePath -cne [string]$approvedRow.SourcePath)
        {
            $failures += "Rendered Sprite '$($sprite.node)' Resources/source paths do not match the approved imported source."
            $pathPassed = $false
        }
        if ([string]$auditRow.sha256 -cne [string]$approvedRow.ImportedSha256 -or [string]$auditRow.sha256 -cnotmatch '^[0-9A-F]{64}$')
        {
            $failures += "Rendered Sprite '$($sprite.node)' SHA-256 does not match the imported approved source."
            $shaPassed = $false
        }
        if (@($auditRow.captures).Count -ne 1 -or [string]$auditRow.captures[0] -cne [string]$Capture.name)
        {
            $failures += "Rendered Sprite '$($sprite.node)' capture list does not identify exactly this capture."
            $captureListPassed = $false
        }
        if ([int]$auditRow.occurrenceCount -ne 1)
        {
            $failures += "Rendered Sprite '$($sprite.node)' occurrence evidence does not identify exactly one occurrence in this capture."
            $occurrencePassed = $false
        }
    }
    foreach ($auditRow in $auditRows)
    {
        [array]$rendered = @($renderedRows | Where-Object { [string]$_.node -ceq [string]$auditRow.node -and [string]$_.spriteName -ceq [string]$auditRow.spriteName })
        if ($rendered.Count -ne 1)
        {
            $failures += "Bitmap sourceAudit row '$($auditRow.node)' does not map back to exactly one rendered Sprite occurrence."
            $bijectionPassed = $false
            $identityPassed = $false
        }
    }
    foreach ($group in @($renderedRows | Group-Object spriteName))
    {
        [array]$matchingAuditRows = @($auditRows | Where-Object spriteName -ceq $group.Name)
        $auditedCount = if ($matchingAuditRows.Count -eq 0) { 0 } else { [int](($matchingAuditRows | Measure-Object occurrenceCount -Sum).Sum) }
        [array]$approved = @($SpriteUsage | Where-Object { $_.CaptureName -ceq [string]$Capture.name -and $_.SpriteName -ceq [string]$group.Name })
        $approvedCount = if ($approved.Count -eq 1) { [int]$approved[0].OccurrenceCount } else { -1 }
        if ($auditedCount -ne $group.Count -or $approvedCount -ne $group.Count)
        {
            $failures += "Sprite '$($group.Name)' aggregate audit/approved occurrence inventory does not match rendered total $($group.Count)."
            $aggregatePassed = $false
        }
    }
    foreach ($auditGroup in @($auditRows | Group-Object spriteName))
    {
        $renderedCount = @($renderedRows | Where-Object spriteName -ceq $auditGroup.Name).Count
        $auditedCount = [int](($auditGroup.Group | Measure-Object occurrenceCount -Sum).Sum)
        if ($renderedCount -ne $auditGroup.Count -or $auditedCount -ne $renderedCount)
        {
            $failures += "Bitmap sourceAudit aggregate '$($auditGroup.Name)' contains extra, duplicate, or mismatched occurrences."
            $aggregatePassed = $false
        }
    }
    $normalizedRows = @(
        foreach ($auditRow in $auditRows)
        {
            [array]$rendered = @($renderedRows | Where-Object { [string]$_.node -ceq [string]$auditRow.node -and [string]$_.spriteName -ceq [string]$auditRow.spriteName })
            $visibleRoi = $null
            $captureRect = $null
            if ($rendered.Count -eq 1)
            {
                $sprite = $rendered[0]
                $captureRect = [pscustomobject][ordered]@{
                    coordinateOrigin=[string]$sprite.coordinateOrigin
                    unit=[string]$sprite.unit
                    x=[double]$sprite.x
                    y=[double]$sprite.y
                    width=[double]$sprite.width
                    height=[double]$sprite.height
                }
                $visibleRoi = [pscustomobject][ordered]@{
                    coordinateOrigin='screen-top-left'
                    unit='px'
                    x=[int][Math]::Round([double]$sprite.x * 1920.0 / [double]$Capture.width, [MidpointRounding]::AwayFromZero)
                    y=[int][Math]::Round(([double]$Capture.height - ([double]$sprite.y + [double]$sprite.height)) * 1080.0 / [double]$Capture.height, [MidpointRounding]::AwayFromZero)
                    width=[int][Math]::Round([double]$sprite.width * 1920.0 / [double]$Capture.width, [MidpointRounding]::AwayFromZero)
                    height=[int][Math]::Round([double]$sprite.height * 1080.0 / [double]$Capture.height, [MidpointRounding]::AwayFromZero)
                }
            }
            [pscustomobject][ordered]@{
                node=[string]$auditRow.node
                spriteName=[string]$auditRow.spriteName
                resourcesPath=[string]$auditRow.resourcesPath
                sourcePath=[string]$auditRow.sourcePath
                sha256=[string]$auditRow.sha256
                captures=@($auditRow.captures)
                occurrenceCount=[int]$auditRow.occurrenceCount
                captureRect=$captureRect
                visibleRoi=$visibleRoi
            }
        }
    )
    return [pscustomobject][ordered]@{
        acceptanceRole='blocking'
        bijectionPassed=$bijectionPassed
        identityPassed=$identityPassed
        pathPassed=$pathPassed
        shaPassed=$shaPassed
        captureListPassed=$captureListPassed
        occurrencePassed=$occurrencePassed
        aggregatePassed=$aggregatePassed
        passed=($bijectionPassed -and $identityPassed -and $pathPassed -and $shaPassed -and $captureListPassed -and $occurrencePassed -and $aggregatePassed)
        failures=@($failures)
        rows=$normalizedRows
    }
}

function Get-LanLobbyGateMaterialEvidence($CaptureEvidence, $Roi, [bool] $ExplicitNoBitmapAssociation)
{
    $associatedRows = @()
    if (-not $ExplicitNoBitmapAssociation)
    {
        $associatedRows = @($CaptureEvidence.rows | Where-Object {
            $null -ne $_.visibleRoi -and (Test-LanLobbyRectanglesOverlap $_.visibleRoi $Roi)
        })
    }
    return [pscustomobject][ordered]@{
        acceptanceRole='blocking'
        associationKind=$(if ($ExplicitNoBitmapAssociation) { 'explicit-no-bitmap' } else { 'roi-overlap' })
        bijectionPassed=$CaptureEvidence.bijectionPassed
        identityPassed=$CaptureEvidence.identityPassed
        pathPassed=$CaptureEvidence.pathPassed
        shaPassed=$CaptureEvidence.shaPassed
        captureListPassed=$CaptureEvidence.captureListPassed
        occurrencePassed=$CaptureEvidence.occurrencePassed
        aggregatePassed=$CaptureEvidence.aggregatePassed
        passed=$CaptureEvidence.passed
        failures=@($CaptureEvidence.failures)
        rows=@($associatedRows | Where-Object { $null -ne $_ })
    }
}

function Get-LanLobbyHostProfileStructureEvidence($Capture)
{
    $failures = @()
    $hostValues = @()
    $hostPrefix = 'LanLobbyRoot/Room/RoomCard_0'
    $allowedSpriteByNode = @{
        "$hostPrefix/CardBody" = 'card_bg'
        "$hostPrefix/TopBar" = 'bg_top_ready'
        "$hostPrefix/ReadyOverlay" = 'player_card_self_frame'
        "$hostPrefix/OccupiedContent/ReadyIcon" = 'player_card_ready'
        "$hostPrefix/LowerDecoration" = 'card_deco_self'
        "$hostPrefix/CreatorTag" = 'host_top_tag'
    }
    $readyLabelText = ([char]0x5DF2).ToString() + [char]0x5C31 + [char]0x7EEA
    $allowedTextByNode = @{
        "$hostPrefix/OccupiedContent/ReadyLabel" = $readyLabelText
    }
    $allowedRectNodes = @(
        $hostPrefix
        "$hostPrefix/CardBody"
        "$hostPrefix/TopBar"
        "$hostPrefix/ReadyOverlay"
        "$hostPrefix/OccupiedContent/ReadyIcon"
        "$hostPrefix/OccupiedContent/ReadyLabel"
        "$hostPrefix/LowerDecoration"
        "$hostPrefix/CreatorTag"
    )
    if ($null -ne $Capture.PSObject.Properties['localPlayerId'] -and -not [string]::IsNullOrWhiteSpace([string]$Capture.localPlayerId))
    {
        $hostValues += [string]$Capture.localPlayerId
    }
    if ($null -ne $Capture.PSObject.Properties['members'])
    {
        $hostMember = @($Capture.members | Where-Object { $null -ne $_ } | Select-Object -First 1)
        if ($hostMember.Count -eq 1)
        {
            foreach ($propertyName in @('playerId','displayName'))
            {
                if ($null -ne $hostMember[0].PSObject.Properties[$propertyName] -and
                    -not [string]::IsNullOrWhiteSpace([string]$hostMember[0].$propertyName))
                {
                    $hostValues += [string]$hostMember[0].$propertyName
                }
            }
        }
    }
    foreach ($textRow in @($Capture.unityText | Where-Object { $null -ne $_ }))
    {
        foreach ($hostValue in @($hostValues | Select-Object -Unique))
        {
            if (-not [string]::IsNullOrEmpty([string]$textRow.text) -and
                ([string]$textRow.text).IndexOf($hostValue, [StringComparison]::OrdinalIgnoreCase) -ge 0)
            {
                $failures += "Host identity text '$hostValue' is rendered by '$($textRow.node)'."
            }
        }
    }

    foreach ($collectionName in @('spriteSources','sourceAudit'))
    {
        foreach ($row in @($Capture.$collectionName | Where-Object { $null -ne $_ -and [string]$_.node -like "$hostPrefix/*" }))
        {
            $node = [string]$row.node
            if (-not $allowedSpriteByNode.ContainsKey($node) -or
                [string]$row.spriteName -cne [string]$allowedSpriteByNode[$node])
            {
                $failures += "Host slot $collectionName row '$node' / '$($row.spriteName)' is outside the allowed Sprite whitelist."
            }
        }
    }
    foreach ($row in @($Capture.unityText | Where-Object { $null -ne $_ -and [string]$_.node -like "$hostPrefix/*" }))
    {
        $node = [string]$row.node
        if (-not $allowedTextByNode.ContainsKey($node) -or
            [string]$row.text -cne [string]$allowedTextByNode[$node])
        {
            $failures += "Host slot Unity Text '$node' / '$($row.text)' is outside the allowed text whitelist."
        }
    }
    foreach ($collectionName in @('rects','keyRects'))
    {
        if ($null -eq $Capture.PSObject.Properties[$collectionName]) { continue }
        foreach ($row in @($Capture.$collectionName | Where-Object { $null -ne $_ -and ([string]$_.name -ceq $hostPrefix -or [string]$_.name -like "$hostPrefix/*") }))
        {
            if ([string]$row.name -notin $allowedRectNodes)
            {
                $failures += "Host slot $collectionName row '$($row.name)' is outside the allowed node whitelist."
            }
        }
    }
    foreach ($geometry in @($Capture.codeNativeGeometry | Where-Object { $null -ne $_ -and [string]$_.name -like "$hostPrefix/*" }))
    {
        $failures += "Host slot code-native geometry '$($geometry.name)' is forbidden."
    }
    return [pscustomobject][ordered]@{
        passed=($failures.Count -eq 0)
        hostIdentityValues=@($hostValues | Select-Object -Unique)
        allowedHostSprites=@($allowedSpriteByNode.Values | Sort-Object)
        allowedHostText=@($allowedTextByNode.Values | Sort-Object)
        failures=@($failures | Select-Object -Unique)
    }
}

function Add-LanLobbyBoundsAdjustment($Measurement, $Adjustment, [string] $Label)
{
    if (-not $Measurement.Available -or $null -eq $Adjustment) { return $Measurement }
    $adjustedBounds = New-Object LanLobbyVisualBounds
    $adjustedBounds.X = $Measurement.Bounds.X + [int]$Adjustment.x
    $adjustedBounds.Y = $Measurement.Bounds.Y + [int]$Adjustment.y
    $adjustedBounds.Width = $Measurement.Bounds.Width + [int]$Adjustment.width
    $adjustedBounds.Height = $Measurement.Bounds.Height + [int]$Adjustment.height
    if ($adjustedBounds.Width -le 0 -or $adjustedBounds.Height -le 0)
    {
        throw "$Label bounds adjustment produced non-positive dimensions."
    }
    $adjusted = New-Object LanLobbyBoundsMeasurement
    $adjusted.Available = $true
    $adjusted.Bounds = $adjustedBounds
    $adjusted.FailureReason = $null
    return $adjusted
}

function Measure-LanLobbyOrangeColumnTopology(
    [Drawing.Bitmap] $Bitmap,
    [Drawing.Rectangle] $Search,
    $Thresholds)
{
    $columnCounts = @(
        [LanLobbyVisualDiff]::CountOrangePixelsPerColumn(
            $Bitmap,
            $Search,
            [int]$Thresholds.minimumRed,
            [int]$Thresholds.maximumBlue,
            [int]$Thresholds.minimumRedOverGreen)
    )
    $occupiedColumns = @(
        for ($offset = 0; $offset -lt $columnCounts.Count; $offset++)
        {
            if ($columnCounts[$offset] -ge [int]$Thresholds.minimumQualifyingPixelsPerColumn)
            {
                $Search.X + $offset
            }
        }
    )
    if ($occupiedColumns.Count -eq 0)
    {
        return [pscustomobject][ordered]@{
            available=$false
            failureReason='No decoded orange columns met the minimum occupancy.'
            occupiedColumns=@()
            raw=$null
        }
    }

    $runs = @()
    $runStart = $occupiedColumns[0]
    $previous = $occupiedColumns[0]
    foreach ($column in @($occupiedColumns | Select-Object -Skip 1))
    {
        if ($column -ne $previous + 1)
        {
            $runs += [pscustomobject][ordered]@{
                coordinateOrigin='crop-top-left'
                unit='px'
                startX=$runStart
                endXInclusive=$previous
                width=($previous - $runStart + 1)
            }
            $runStart = $column
        }
        $previous = $column
    }
    $runs += [pscustomobject][ordered]@{
        coordinateOrigin='crop-top-left'
        unit='px'
        startX=$runStart
        endXInclusive=$previous
        width=($previous - $runStart + 1)
    }

    $spanStart = $occupiedColumns[0]
    $spanEnd = $occupiedColumns[-1]
    $raw = [pscustomobject][ordered]@{
        profileEncoding='occupied-column-runs'
        span=[pscustomobject][ordered]@{
            coordinateOrigin='crop-top-left'
            unit='px'
            startX=$spanStart
            endXInclusive=$spanEnd
            width=($spanEnd - $spanStart + 1)
        }
        runs=@($runs)
        occupiedColumnCount=$occupiedColumns.Count
        searchColumnCount=$Search.Width
        occupiedColumnDensity=[Math]::Round($occupiedColumns.Count / [double]$Search.Width, 6)
    }
    return [pscustomobject][ordered]@{
        available=$true
        failureReason=$null
        occupiedColumns=@($occupiedColumns)
        raw=$raw
    }
}

function Compare-LanLobbyOrangeColumnTopology($Reference, $Actual, $Acceptance)
{
    if (-not $Reference.available -or -not $Actual.available)
    {
        return [pscustomobject][ordered]@{
            available=$false
            failureReason=@($Reference.failureReason, $Actual.failureReason | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ' / '
            passed=$false
        }
    }

    $intersectionCount = @(
        $Reference.occupiedColumns |
            Where-Object { $Actual.occupiedColumns -contains $_ }
    ).Count
    $unionCount = @(
        $Reference.occupiedColumns + $Actual.occupiedColumns |
            Sort-Object -Unique
    ).Count
    $profileJaccard = if ($unionCount -eq 0) { 1.0 } else { $intersectionCount / [double]$unionCount }
    $spanStartDelta = $Actual.raw.span.startX - $Reference.raw.span.startX
    $spanEndDelta = $Actual.raw.span.endXInclusive - $Reference.raw.span.endXInclusive
    $spanWidthDelta = $Actual.raw.span.width - $Reference.raw.span.width
    $occupiedColumnCountDelta = $Actual.raw.occupiedColumnCount - $Reference.raw.occupiedColumnCount
    $passed = [Math]::Abs($spanStartDelta) -le [int]$Acceptance.maximumSpanEdgeDeviationPx -and
        [Math]::Abs($spanEndDelta) -le [int]$Acceptance.maximumSpanEdgeDeviationPx -and
        [Math]::Abs($occupiedColumnCountDelta) -le [int]$Acceptance.maximumOccupiedColumnCountDelta -and
        $profileJaccard -ge [double]$Acceptance.minimumProfileJaccard
    return [pscustomobject][ordered]@{
        available=$true
        failureReason=$null
        unit='px'
        spanStartDeltaPx=$spanStartDelta
        spanEndDeltaPx=$spanEndDelta
        spanWidthDeltaPx=$spanWidthDelta
        occupiedColumnCountDelta=$occupiedColumnCountDelta
        profileIntersectionOccupiedColumnCount=$intersectionCount
        profileUnionOccupiedColumnCount=$unionCount
        profileJaccard=[Math]::Round($profileJaccard, 6)
        passed=$passed
    }
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
            foreach ($propertyName in @('node','spriteName','resourcesPath','sourcePath','sha256'))
            {
                if ($null -eq $sprite.PSObject.Properties[$propertyName] -or
                    [string]::IsNullOrWhiteSpace([string]$sprite.$propertyName))
                {
                    throw "Capture '$($capture.name)' SpriteSource is missing required capture field '$propertyName'."
                }
            }
            if ([string]$sprite.sha256 -cnotmatch '^[A-F0-9]{64}$')
            {
                throw "Capture '$($capture.name)' SpriteSource '$($sprite.node)' must declare an uppercase 64-hex SHA-256."
            }
            $spriteCaptures = @()
            if ($null -ne $sprite.PSObject.Properties['captures'])
            {
                $spriteCaptures = @($sprite.captures | Where-Object { $null -ne $_ })
            }
            if ($null -eq $sprite.PSObject.Properties['captures'] -or
                $spriteCaptures.Count -ne 1 -or
                [string]$spriteCaptures[0] -cne [string]$capture.name)
            {
                throw "Capture '$($capture.name)' SpriteSource '$($sprite.node)' must declare its containing capture exactly once."
            }
            if ($null -eq $sprite.PSObject.Properties['occurrenceCount'] -or [int]$sprite.occurrenceCount -ne 1)
            {
                throw "Capture '$($capture.name)' SpriteSource '$($sprite.node)' must declare occurrenceCount=1."
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
            foreach ($propertyName in @('spriteName','materialName','resourcesPath','sourcePath','sha256'))
            {
                if ($null -ne $geometry.PSObject.Properties[$propertyName] -and
                    -not [string]::IsNullOrWhiteSpace([string]$geometry.$propertyName))
                {
                    throw "CodeNativeGeometry '$($geometry.name)' must not declare nonempty $propertyName."
                }
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
    foreach ($propertyName in @('spriteName','materialName','resourcesPath','sourcePath','sha256'))
    {
        if ($null -ne $Geometry.PSObject.Properties[$propertyName] -and
            -not [string]::IsNullOrWhiteSpace([string]$Geometry.$propertyName))
        {
            throw "Join geometry '$($Geometry.name)' must not declare nonempty $propertyName."
        }
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

function Resolve-LanLobbyCaptureReference([string] $ExpectedFileName, [string[]] $Directories)
{
    $matches = @(
        foreach ($directory in $Directories)
        {
            if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Reference directory not found: $directory" }
            Get-ChildItem -LiteralPath $directory -File -Filter '*.png' |
                Where-Object { $_.Name -ceq $ExpectedFileName }
        }
    )
    if ($matches.Count -ne 1)
    {
        throw "Expected exactly one reference named $ExpectedFileName; found $($matches.Count)."
    }
    return $matches[0].FullName
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
$referencePaths = @{}
$referenceProbes = @{}
foreach ($captureName in $referenceByCapture.Keys)
{
    $expectedFileName = [string]$referenceByCapture[$captureName]
    if (-not $referencePaths.ContainsKey($expectedFileName))
    {
        $referencePath = Resolve-LanLobbyCaptureReference -ExpectedFileName $expectedFileName -Directories $referenceDirectories
        $probe = Test-LanLobbyReferenceImage -ReferencePath $referencePath
        if ($expectedFileName -in @($figure11FileName,$figure12FileName,$figure13FileName) -and
            ($probe.Width -ne 2560 -or $probe.Height -ne 1440))
        {
            throw "Room reference $expectedFileName must be exactly 2560x1440 before normalization; decoded $($probe.Width)x$($probe.Height)."
        }
        $referencePaths[$expectedFileName] = $referencePath
        $referenceProbes[$expectedFileName] = $probe
    }
}
$referenceHome = $referencePaths[$figure9FileName]

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
$roomGates = @()
$roomExclusionReports = @()
$portraitFrameSharedGeometry = Get-LanLobbyPortraitFrameSharedGeometry @(
    $manifest.captures | Where-Object { [string]$_.name -in @('room-host','room-full','room-ready') }
)
$portraitFrameConsensus = Get-LanLobbyPortraitFrameConsensus `
    -RoomCaptures @($manifest.captures | Where-Object { [string]$_.name -in @('room-host','room-full','room-ready') }) `
    -ReferencePaths $referencePaths `
    -ReferenceByCapture $referenceByCapture
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
try
{
    foreach ($capture in @($manifest.captures))
    {
        $isRoom = $capture.name -like 'room-*'
        $referencePath = $referencePaths[[string]$referenceByCapture[[string]$capture.name]]
        $regionSpecs = if ($isRoom) {
            @(
                $roomRegions | Where-Object { $_.name -ne 'ignored-upper-comments' }
                foreach ($exclusion in @($roomExclusionsByCapture[[string]$capture.name]))
                {
                    @{
                        name=('ignored-' + $exclusion.name)
                        x=$exclusion.x / 1920.0
                        y=$exclusion.y / 1080.0
                        width=$exclusion.width / 1920.0
                        height=$exclusion.height / 1080.0
                        mask=$true
                    }
                }
            )
        } else { $homeRegions }
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
            try
            {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
                $graphics.DrawImage($nativeReference, 0, 0, 1920, 1080)
            }
            finally { $graphics.Dispose() }
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
            if ($isRoom)
            {
                $captureName = [string]$capture.name
                $captureExclusions = @($roomExclusionsByCapture[$captureName])
                $protected = @($roomProtectedRegions.GetEnumerator() | ForEach-Object {
                    [pscustomobject][ordered]@{ name=$_.Key; rect=[pscustomobject]$_.Value }
                })
                $exclusionRows = @(
                    foreach ($exclusion in $captureExclusions)
                    {
                        $overlaps = @($protected | Where-Object { Test-LanLobbyRectanglesOverlap $exclusion $_.rect } | ForEach-Object name)
                        [pscustomobject][ordered]@{
                            capture=$captureName
                            name=$exclusion.name
                            reason=$exclusion.reason
                            roi=[pscustomobject][ordered]@{ coordinateOrigin='screen-top-left';unit='px';x=$exclusion.x;y=$exclusion.y;width=$exclusion.width;height=$exclusion.height }
                            protectedRegionsClear=($overlaps.Count -eq 0)
                            overlappingProtectedRegions=$overlaps
                        }
                    }
                )
                if (@($exclusionRows | Where-Object { -not $_.protectedRegionsClear }).Count -gt 0)
                {
                    throw "Room reference exclusions overlap protected named gates for capture '$captureName'."
                }
                $roomExclusionReports += $exclusionRows
                $captureMaterialEvidence = Get-LanLobbyRoomMaterialEvidence $capture $spriteUsage
                $captureGateRows = @()
                foreach ($gateSpec in @(Get-LanLobbyRoomGateSpecs $capture))
                {
                    $measurement = Measure-LanLobbyVisiblePlacement $actual $normalizedReference $gateSpec
                    $gateMaterialEvidence = Get-LanLobbyGateMaterialEvidence $captureMaterialEvidence $gateSpec.roi $false
                    $portraitFrameRelation = $null
                    $relativePlacement = $null
                    $sharedGeometryPassed = $null
                    $portraitCardBodyPassed = $null
                    if ([string]$gateSpec.gateKind -ceq 'PortraitFrame')
                    {
                        $slotIndex = [int]$gateSpec.slotIndex
                        $slotRoot = "LanLobbyRoot/Room/RoomCard_$slotIndex"
                        $requiredCardBodyNode = "$slotRoot/CardBody"
                        $frameRect = Get-LanLobbyCaptureKeyRect $capture "$slotRoot/CardBody"
                        [array]$portraitCardBodyRows = @($gateMaterialEvidence.rows | Where-Object {
                            $sourceRect = $_.captureRect
                            [string]$_.node -ceq $requiredCardBodyNode -and
                            [string]$_.spriteName -ceq 'card_bg' -and
                            [string]$_.resourcesPath -ceq 'UI/Lobby/card_bg' -and
                            [string]$_.sourcePath -ceq '[uc]autochessouter/card_bg.png' -and
                            [string]$_.sha256 -ceq '050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2' -and
                            [int]$_.occurrenceCount -eq 1 -and
                            @($_.captures).Count -eq 1 -and
                            [string]$_.captures[0] -ceq $captureName -and
                            $null -ne $sourceRect -and
                            [string]$sourceRect.coordinateOrigin -ceq 'screen-bottom-left' -and
                            [string]$sourceRect.unit -ceq 'px' -and
                            [string]$frameRect.coordinateOrigin -ceq 'screen-bottom-left' -and
                            [string]$frameRect.unit -ceq 'px' -and
                            [double]$sourceRect.x -eq [double]$frameRect.x -and
                            [double]$sourceRect.y -eq [double]$frameRect.y -and
                            [double]$sourceRect.width -eq [double]$frameRect.width -and
                            [double]$sourceRect.height -eq [double]$frameRect.height
                        })
                        $portraitCardBodyPassed = $portraitCardBodyRows.Count -eq 1
                        $gateMaterialEvidence | Add-Member -NotePropertyName requiredPortraitCardBodyNode -NotePropertyValue $requiredCardBodyNode
                        $gateMaterialEvidence | Add-Member -NotePropertyName portraitCardBodyPassed -NotePropertyValue $portraitCardBodyPassed
                        $lowerRect = Get-LanLobbyCaptureKeyRect $capture "$slotRoot/LowerDecoration"
                        $lowerTopInScreenshot = [double]$capture.height-([double]$lowerRect.y+[double]$lowerRect.height)
                        $seamRoi = [pscustomobject][ordered]@{
                            coordinateOrigin='screen-top-left'
                            unit='px'
                            x=[int][Math]::Floor([double]$frameRect.x)
                            y=[int][Math]::Floor($lowerTopInScreenshot)-2
                            width=[int][Math]::Ceiling([double]$frameRect.width)
                            height=5
                        }
                        $topBarPlacement = Get-LanLobbyPortraitFrameEdgeBounds -Image $actual -Roi $gateSpec.topBarRoi -MaskKind $gateSpec.maskKind -Exclusions $gateSpec.exclusions
                        $lowerTopPlacement = Get-LanLobbyPortraitLowerDecorationTop -Image $actual -Roi $gateSpec.roi -Exclusions $gateSpec.exclusions
                        if (-not $lowerTopPlacement.available)
                        {
                            throw "Portrait-frame actual lower-decoration anchor '$($gateSpec.name)' failed: $($lowerTopPlacement.failureReason)"
                        }
                        $relativePlacement = Measure-LanLobbyPortraitRelativePlacement `
                            -FramePlacement $measurement.actual `
                            -TopBarPlacement $topBarPlacement `
                            -LowerDecorationTop $lowerTopPlacement `
                            -Consensus $portraitFrameConsensus `
                            -Thresholds $measurement.thresholds
                        $portraitFrameRelation = Measure-LanLobbyPortraitFrameRelation `
                            -FramePlacement $measurement.actual `
                            -TopBarPlacement $topBarPlacement `
                            -FrameRect $frameRect `
                            -LowerDecorationRect $lowerRect `
                            -ActualImage $actual `
                            -SeamRoi $seamRoi `
                            -MaskKind $gateSpec.maskKind
                        $sharedRecord = @($portraitFrameSharedGeometry.records | Where-Object {
                            [string]$_.capture -ceq $captureName -and [int]$_.slotNumber -eq ($slotIndex+1)
                        })
                        $sharedGeometryPassed = $sharedRecord.Count -eq 1 -and [bool]$sharedRecord[0].passed
                    }
                    $gateMaterialPassed = $gateMaterialEvidence.passed -and
                        ($null -eq $portraitCardBodyPassed -or $portraitCardBodyPassed)
                    $placementPassed = if ($null -ne $relativePlacement) { [bool]$relativePlacement.passed } else { [bool]$measurement.passed }
                    $gatePassed = $placementPassed -and $gateMaterialPassed -and
                        ($null -eq $portraitFrameRelation -or $portraitFrameRelation.passed) -and
                        ($null -eq $sharedGeometryPassed -or $sharedGeometryPassed)
                    $gateStatus = if ($gatePassed) { 'Passed' } else { 'Failed' }
                    $gateReason = if (-not $gateMaterialPassed) {
                        'Blocking bitmap manifest bijection, identity, approved path, SHA-256, capture list, occurrence, or exact portrait CardBody/card_bg association evidence failed.'
                    } elseif ($null -ne $relativePlacement -and -not $relativePlacement.passed) {
                        'Visible portrait-frame placement exceeds a blocking reconciled top-bar/lower-decoration-relative edge, center, or Jaccard threshold.'
                    } elseif ($null -ne $portraitFrameRelation -and -not $portraitFrameRelation.passed) {
                        'Visible portrait-frame relation exceeds a blocking center, width, overlap, or seam threshold.'
                    } elseif ($null -ne $sharedGeometryPassed -and -not $sharedGeometryPassed) {
                        'Manifest CardBody width, height, or local slot offset differs across room capture states.'
                    } else { $measurement.reason }
                    $gateRow = [pscustomobject][ordered]@{
                        name=$gateSpec.name
                        capture=$captureName
                        gateKind=$gateSpec.gateKind
                        referenceFigure=[IO.Path]::GetFileName($referencePath)
                        roi=[pscustomobject][ordered]@{ coordinateOrigin='screen-top-left';unit='px';x=$gateSpec.roi.x;y=$gateSpec.roi.y;width=$gateSpec.roi.width;height=$gateSpec.roi.height }
                        exclusions=@($gateSpec.exclusions | ForEach-Object {
                            [pscustomobject][ordered]@{ name=$_.name;reason=$_.reason;x=$_.x;y=$_.y;width=$_.width;height=$_.height }
                        })
                        maskKind=$gateSpec.maskKind
                        maskDescription=$(if ([string]$gateSpec.gateKind -ceq 'PortraitFrame') {
                            'Decoded screenshot silhouette measured from tint/luminance-normalized local horizontal RGB contrast.'
                        } else {
                            'Decoded opaque screenshot color/contrast mask measured directly from rendered RGB values.'
                        })
                        diagnosticRectTransform=$gateSpec.diagnosticRectTransform
                        actualVisibleBounds=$measurement.actual.bounds
                        referenceVisibleBounds=$measurement.reference.bounds
                        actualVisibleCenter=$measurement.actual.center
                        referenceVisibleCenter=$measurement.reference.center
                        actualVisiblePixelCount=$measurement.actual.pixelCount
                        referenceVisiblePixelCount=$measurement.reference.pixelCount
                        absolutePlacement=$(if ($null -ne $relativePlacement) {
                            [pscustomobject][ordered]@{
                                acceptanceRole='diagnostic-only'
                                edgeDeltaPx=$measurement.edgeDeltaPx
                                centerDeltaPx=$measurement.centerDeltaPx
                                sizeDeltaPx=$measurement.sizeDeltaPx
                                contour=$measurement.contour
                                thresholds=$measurement.thresholds
                                passed=$measurement.passed
                            }
                        } else { $null })
                        relativePlacement=$relativePlacement
                        edgeDeltaPx=$(if ($null -ne $relativePlacement) { $relativePlacement.edgeDeltaPx } else { $measurement.edgeDeltaPx })
                        centerDeltaPx=$(if ($null -ne $relativePlacement) { $relativePlacement.centerDeltaPx } else { $measurement.centerDeltaPx })
                        sizeDeltaPx=$(if ($null -ne $relativePlacement) { $relativePlacement.sizeDeltaPx } else { $measurement.sizeDeltaPx })
                        contour=$(if ($null -ne $relativePlacement) { $relativePlacement.contour } else { $measurement.contour })
                        thresholds=$measurement.thresholds
                        portraitFrameRelation=$portraitFrameRelation
                        sharedGeometryPassed=$sharedGeometryPassed
                        materialEvidence=$gateMaterialEvidence
                        status=$gateStatus
                        reason=$gateReason
                        passed=$gatePassed
                    }
                    $captureGateRows += $gateRow
                }

                $capturePrefix = switch ($captureName) { 'room-host' {'RoomHost'} 'room-full' {'RoomFull'} 'room-ready' {'RoomReady'} }
                $profileRoi = [pscustomobject][ordered]@{ coordinateOrigin='screen-top-left';unit='px';x=250;y=240;width=250;height=473 }
                $profileExclusions = @(
                    [pscustomobject][ordered]@{ name='reference-profile-art';reason='The authoritative reference character/profile artwork is explicitly outside visual acceptance and is not pixel-compared.';x=250;y=240;width=250;height=473 }
                )
                $profileStructure = Get-LanLobbyHostProfileStructureEvidence $capture
                $profileMaterialEvidence = Get-LanLobbyGateMaterialEvidence $captureMaterialEvidence $profileRoi $true
                $profileAbsent = $profileStructure.passed -and $profileMaterialEvidence.passed
                $captureGateRows += [pscustomobject][ordered]@{
                    name="$capturePrefix.Slot1.ProfileContentAbsence";capture=$captureName;referenceFigure=[IO.Path]::GetFileName($referencePath)
                    roi=$profileRoi;exclusions=$profileExclusions;maskKind='structured-host-profile-absence';maskDescription='Exact host-slot Sprite, Unity Text, Rect/keyRect, identity, and code-native-geometry whitelist; excluded reference profile artwork is not pixel-compared.'
                    diagnosticRectTransform=$null;actualVisibleBounds=$null;referenceVisibleBounds=$null;actualVisibleCenter=$null;referenceVisibleCenter=$null
                    actualVisiblePixelCount=0;referenceVisiblePixelCount=0
                    unexpectedActualPixelCount=0;structuredAbsencePassed=$profileStructure.passed;structuredAbsence=$profileStructure
                    edgeDeltaPx=$null;centerDeltaPx=$null;sizeDeltaPx=$null;contour=[pscustomobject]@{intersectionPixels=0;unionPixels=0;jaccard=$null}
                    thresholds=[pscustomobject]@{allowedHostSprites=@($profileStructure.allowedHostSprites);allowedHostText=@($profileStructure.allowedHostText);maximumUnexpectedHostRows=0};materialEvidence=$profileMaterialEvidence
                    status=$(if($profileAbsent){'Passed'}else{'Failed'});reason=$(if($profileAbsent){'Host slot matches the exact allowed Sprite/text/rect whitelist and contains no identity/profile/avatar/name/ID or code-native content.'}elseif(-not $profileStructure.passed){"Structured host profile absence failed: $($profileStructure.failures -join ' ')"}else{'Blocking material manifest evidence failed.'});passed=$profileAbsent
                }
                foreach ($forbiddenText in @('OPEN SLOT','WAITING'))
                {
                    $textOccurrenceCount = @($capture.unityText | Where-Object { [string]$_.text -ceq $forbiddenText }).Count
                    $textMaterialEvidence = Get-LanLobbyGateMaterialEvidence $captureMaterialEvidence $roomProtectedRegions.hostSlot $true
                    $textAbsent = $textOccurrenceCount -eq 0 -and $textMaterialEvidence.passed
                    $literalSuffix = if ($forbiddenText -ceq 'OPEN SLOT') { 'LegacyOpenSlotTextAbsence' } else { 'LegacyWaitingTextAbsence' }
                    $captureGateRows += [pscustomobject][ordered]@{
                        name="$capturePrefix.$literalSuffix"
                        capture=$captureName;referenceFigure=[IO.Path]::GetFileName($referencePath)
                        roi=[pscustomobject][ordered]@{coordinateOrigin='screen-top-left';unit='px';x=200;y=178;width=1529;height=665};exclusions=@($captureExclusions)
                        maskKind='manifest-text-absence';maskDescription='Structured active Unity Text evidence from the capture manifest.';diagnosticRectTransform=$null
                        actualVisibleBounds=$null;referenceVisibleBounds=$null;actualVisibleCenter=$null;referenceVisibleCenter=$null
                        actualVisiblePixelCount=$textOccurrenceCount;referenceVisiblePixelCount=0
                        edgeDeltaPx=$null;centerDeltaPx=$null;sizeDeltaPx=$null;contour=$null;thresholds=[pscustomobject]@{maximumOccurrenceCount=0}
                        materialEvidence=$textMaterialEvidence;status=$(if($textAbsent){'Passed'}else{'Failed'});reason=$(if($textAbsent){"Active Unity Text contains no '$forbiddenText'."}else{"Forbidden active Unity Text '$forbiddenText' or blocking material evidence failed."});passed=$textAbsent
                    }
                }
                [array]$spacingSpecs = if ($captureName -eq 'room-host') {
                    @(
                        [pscustomobject]@{name='RoomHost.Slot2To3.VisibleContourSpacing';left='RoomHost.Slot2.EmptyComposition';right='RoomHost.Slot3.EmptyComposition';roi=@{x=589;y=178;width=752;height=665}},
                        [pscustomobject]@{name='RoomHost.Slot3To4.VisibleContourSpacing';left='RoomHost.Slot3.EmptyComposition';right='RoomHost.Slot4.EmptyComposition';roi=@{x=977;y=178;width=752;height=665}}
                    )
                } elseif ($captureName -eq 'room-full') {
                    @([pscustomobject]@{name='RoomFull.Slot2To3.VisibleContourSpacing';left='RoomFull.Slot2.WaitingContour';right='RoomFull.Slot3.WaitingContour';roi=@{x=589;y=178;width=752;height=665}})
                } elseif ($captureName -eq 'room-ready') {
                    @([pscustomobject]@{name='RoomReady.Slot2To3.VisibleContourSpacing';left='RoomReady.Slot2.ReadyContour';right='RoomReady.Slot3.ReadyContour';roi=@{x=589;y=178;width=752;height=665}})
                } else { @() }
                foreach ($spacingSpec in $spacingSpecs)
                {
                    $leftGate=@($captureGateRows | Where-Object name -eq $spacingSpec.left)[0]
                    $rightGate=@($captureGateRows | Where-Object name -eq $spacingSpec.right)[0]
                    $spacingAvailable = $null -ne $leftGate.actualVisibleCenter -and
                        $null -ne $rightGate.actualVisibleCenter -and
                        $null -ne $leftGate.referenceVisibleCenter -and
                        $null -ne $rightGate.referenceVisibleCenter
                    $actualSpacing = if ($spacingAvailable) { $rightGate.actualVisibleCenter.x-$leftGate.actualVisibleCenter.x } else { $null }
                    $referenceSpacing = if ($spacingAvailable) { $rightGate.referenceVisibleCenter.x-$leftGate.referenceVisibleCenter.x } else { $null }
                    $spacingDelta = if ($spacingAvailable) { $actualSpacing-$referenceSpacing } else { $null }
                    $spacingRoi = [pscustomobject][ordered]@{coordinateOrigin='screen-top-left';unit='px';x=$spacingSpec.roi.x;y=$spacingSpec.roi.y;width=$spacingSpec.roi.width;height=$spacingSpec.roi.height}
                    $spacingMaterialEvidence = Get-LanLobbyGateMaterialEvidence $captureMaterialEvidence $spacingRoi $true
                    $spacingPassed=$spacingAvailable -and [Math]::Abs($spacingDelta)-le 4 -and $spacingMaterialEvidence.passed
                    $captureGateRows += [pscustomobject][ordered]@{
                        name=$spacingSpec.name
                        capture=$captureName;referenceFigure=[IO.Path]::GetFileName($referencePath)
                        roi=$spacingRoi;exclusions=@($captureExclusions)
                        maskKind='derived-visible-contour-spacing';maskDescription='Derived from the two authoritative visible contour centers.';diagnosticRectTransform=$null
                        actualVisibleBounds=@($leftGate.actualVisibleBounds,$rightGate.actualVisibleBounds);referenceVisibleBounds=@($leftGate.referenceVisibleBounds,$rightGate.referenceVisibleBounds)
                        actualVisibleCenter=@($leftGate.actualVisibleCenter,$rightGate.actualVisibleCenter);referenceVisibleCenter=@($leftGate.referenceVisibleCenter,$rightGate.referenceVisibleCenter)
                        actualVisiblePixelCount=@($leftGate.actualVisiblePixelCount,$rightGate.actualVisiblePixelCount);referenceVisiblePixelCount=@($leftGate.referenceVisiblePixelCount,$rightGate.referenceVisiblePixelCount)
                        edgeDeltaPx=$null;centerDeltaPx=[pscustomobject][ordered]@{unit='px';actualSpacing=$actualSpacing;referenceSpacing=$referenceSpacing;spacingDelta=$spacingDelta};sizeDeltaPx=$null;contour=$null
                        thresholds=[pscustomobject]@{maximumSpacingErrorPx=4};materialEvidence=$spacingMaterialEvidence;status=$(if($spacingPassed){'Passed'}else{'Failed'})
                        reason=$(if($spacingPassed){'Repeated visible contour spacing satisfies the 4 px gate.'}elseif(-not $spacingAvailable){'Repeated visible contour spacing is unavailable because a required visible contour is empty.'}else{'Repeated visible contour spacing or material evidence failed.'});passed=$spacingPassed
                    }
                }
                if ($captureName -in @('room-full','room-ready'))
                {
                    $captureGateRows += [pscustomobject][ordered]@{
                        name=$(if($captureName -eq 'room-full'){'RoomFull.Slot4.ReferencePopupExclusion'}else{'RoomReady.Slot4.ReferencePopupExclusion'})
                        capture=$captureName;referenceFigure=[IO.Path]::GetFileName($referencePath)
                        roi=[pscustomobject][ordered]@{coordinateOrigin='screen-top-left';unit='px';x=1365;y=178;width=364;height=665}
                        exclusions=@($captureExclusions);maskKind=$null;maskDescription='No visible measurement is accepted from the popup-obscured fourth-slot reference.'
                        diagnosticRectTransform=$null;actualVisibleBounds=$null;referenceVisibleBounds=$null;actualVisibleCenter=$null;referenceVisibleCenter=$null
                        actualVisiblePixelCount=$null;referenceVisiblePixelCount=$null
                        edgeDeltaPx=$null;centerDeltaPx=$null;sizeDeltaPx=$null;contour=$null
                        thresholds=[pscustomobject]@{acceptance='excluded-never-passed'};materialEvidence=$(Get-LanLobbyGateMaterialEvidence $captureMaterialEvidence ([pscustomobject]@{x=1365;y=178;width=364;height=665}) $true)
                        status='ExcludedByReferencePopup';reason="The complete fourth slot is obscured by the Figure $($(if($captureName -eq 'room-full'){12}else{13})) right-side popup; Figure 11 is the only blocking fourth-slot reference.";passed=$false
                    }
                }
                foreach ($failedGate in @($captureGateRows | Where-Object status -ceq 'Failed'))
                {
                    foreach ($diagnosticBitmap in @($overlay,$heatmap))
                    {
                        $diagnosticGraphics = [Drawing.Graphics]::FromImage($diagnosticBitmap)
                        $diagnosticPen = New-Object Drawing.Pen ([Drawing.Color]::Red), 3
                        try
                        {
                            $diagnosticGraphics.DrawRectangle($diagnosticPen, (ConvertTo-LanLobbyRectangle $failedGate.roi))
                            $failedRelationProperty = $failedGate.PSObject.Properties['portraitFrameRelation']
                            if ($null -ne $failedRelationProperty -and $null -ne $failedRelationProperty.Value)
                            {
                                $diagnosticGraphics.DrawRectangle(
                                    $diagnosticPen,
                                    (ConvertTo-LanLobbyRectangle $failedRelationProperty.Value.seamRoi))
                            }
                        }
                        finally { $diagnosticPen.Dispose(); $diagnosticGraphics.Dispose() }
                    }
                }
                $roomGates += $captureGateRows
            }
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
                    'orange-component-union' {
                        $ignoredVerticalGuide = New-Object Drawing.Rectangle $contentSpec.thresholds.ignoredVerticalGuideX, $contentSpec.thresholds.ignoredVerticalGuideY, $contentSpec.thresholds.ignoredVerticalGuideWidth, $contentSpec.thresholds.ignoredVerticalGuideHeight
                        $ignoredHorizontalGuide = New-Object Drawing.Rectangle $contentSpec.thresholds.ignoredHorizontalGuideX, $contentSpec.thresholds.ignoredHorizontalGuideY, $contentSpec.thresholds.ignoredHorizontalGuideWidth, $contentSpec.thresholds.ignoredHorizontalGuideHeight
                        [Drawing.Rectangle[]]$requiredComponentAnchors = @(
                            foreach ($anchor in $contentSpec.thresholds.requiredComponentAnchors)
                            {
                                New-Object Drawing.Rectangle $anchor.x, $anchor.y, $anchor.width, $anchor.height
                            }
                        )
                        return [LanLobbyVisualDiff]::FindOrangeComponentUnionBounds($bitmap, $search, $contentSpec.thresholds.minimumRed, $contentSpec.thresholds.minimumGreen, $contentSpec.thresholds.maximumBlue, $contentSpec.thresholds.minimumRedOverGreen, $contentSpec.thresholds.minimumComponentPixelCount, $contentSpec.thresholds.maximumComponentWidth, $contentSpec.thresholds.maximumComponentHeight, $ignoredVerticalGuide, $ignoredHorizontalGuide, $requiredComponentAnchors)
                    }
                    'luma' { return [LanLobbyVisualDiff]::FindLumaBounds($bitmap, $search, $contentSpec.thresholds.minimumLuminanceInclusive, $contentSpec.thresholds.maximumLuminanceInclusive) }
                    'neutral' { return [LanLobbyVisualDiff]::FindNeutralBounds($bitmap, $search, $contentSpec.thresholds.minimumLuminanceInclusive, $contentSpec.thresholds.maximumLuminanceInclusive, $contentSpec.thresholds.maximumChannelSpread) }
                    'neutral-largest-component' { return [LanLobbyVisualDiff]::FindLargestNeutralComponentBounds($bitmap, $search, $contentSpec.thresholds.minimumLuminanceInclusive, $contentSpec.thresholds.maximumLuminanceInclusive, $contentSpec.thresholds.maximumChannelSpread) }
                    'edge-component-union' {
                        $ignoredRegion = New-Object Drawing.Rectangle $contentSpec.thresholds.ignoredRegionX, $contentSpec.thresholds.ignoredRegionY, $contentSpec.thresholds.ignoredRegionWidth, $contentSpec.thresholds.ignoredRegionHeight
                        return [LanLobbyVisualDiff]::FindEdgeComponentUnionBounds($bitmap, $search, $contentSpec.thresholds.minimumChannelDifference, $contentSpec.thresholds.minimumComponentPixelCount, $contentSpec.thresholds.excludeSearchBorderComponents, $ignoredRegion)
                    }
                    default { throw "Unsupported Join decoration measurement: $($contentSpec.measurement)" }
                }
            }
            $referenceRawMeasurement = & $measure $locallyResizedReferenceCrop
            $actualRawMeasurement = & $measure $actualCrop
            $boundsAdjustment = $null
            $adjustmentSpec = $null
            if ($contentSpec.ContainsKey('boundsAdjustment'))
            {
                $adjustmentSpec = $contentSpec.boundsAdjustment
                $boundsAdjustment = [ordered]@{}
                foreach ($key in $adjustmentSpec.Keys) { $boundsAdjustment[$key] = $adjustmentSpec[$key] }
            }
            $referenceMeasurement = Add-LanLobbyBoundsAdjustment $referenceRawMeasurement $adjustmentSpec "$($contentSpec.name) reference"
            $actualMeasurement = Add-LanLobbyBoundsAdjustment $actualRawMeasurement $adjustmentSpec "$($contentSpec.name) actual"
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
            $boundsPassed = $passed
            $internalTopology = $null
            if ($contentSpec.ContainsKey('internalTopology'))
            {
                $topologySpec = $contentSpec.internalTopology
                $topologySearch = New-Object Drawing.Rectangle $topologySpec.search.x, $topologySpec.search.y, $topologySpec.search.width, $topologySpec.search.height
                $referenceTopology = Measure-LanLobbyOrangeColumnTopology $locallyResizedReferenceCrop $topologySearch $topologySpec.thresholds
                $actualTopology = Measure-LanLobbyOrangeColumnTopology $actualCrop $topologySearch $topologySpec.thresholds
                $referenceSelfComparison = Compare-LanLobbyOrangeColumnTopology $referenceTopology $referenceTopology $topologySpec.acceptance
                $topologyComparison = Compare-LanLobbyOrangeColumnTopology $referenceTopology $actualTopology $topologySpec.acceptance
                $topologyMeasurementAvailable = $referenceTopology.available -and $actualTopology.available
                $topologyMeasurementError = @(
                    $referenceTopology.failureReason,
                    $actualTopology.failureReason |
                        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
                ) -join ' / '
                $topologyThresholds = [ordered]@{}
                foreach ($key in $topologySpec.thresholds.Keys) { $topologyThresholds[$key] = $topologySpec.thresholds[$key] }
                $topologyAcceptance = [ordered]@{}
                foreach ($key in $topologySpec.acceptance.Keys) { $topologyAcceptance[$key] = $topologySpec.acceptance[$key] }
                $referenceSelfPassed = $referenceSelfComparison.available -and $referenceSelfComparison.passed
                $topologyPassed = $topologyMeasurementAvailable -and $referenceSelfPassed -and $topologyComparison.passed
                $internalTopology = [pscustomobject][ordered]@{
                    measurement=$topologySpec.measurement
                    acceptanceRole='blocking'
                    search=[pscustomobject][ordered]@{
                        coordinateOrigin='crop-top-left'
                        unit='px'
                        x=$topologySpec.search.x
                        y=$topologySpec.search.y
                        width=$topologySpec.search.width
                        height=$topologySpec.search.height
                    }
                    thresholds=[pscustomobject]$topologyThresholds
                    acceptance=[pscustomobject]$topologyAcceptance
                    measurementAvailable=$topologyMeasurementAvailable
                    measurementError=$(if ($topologyMeasurementAvailable) { $null } else { $topologyMeasurementError })
                    referenceRaw=$referenceTopology.raw
                    actualRaw=$actualTopology.raw
                    referenceSelfComparison=$referenceSelfComparison
                    referenceSelfPassed=$referenceSelfPassed
                    comparison=$topologyComparison
                    passed=$topologyPassed
                }
                $passed = $boundsPassed -and $topologyPassed
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
                boundsAdjustment=$(if ($null -eq $boundsAdjustment) { $null } else { [pscustomobject]$boundsAdjustment })
                referenceRawBounds=$(if ($referenceRawMeasurement.Available) { ConvertTo-LanLobbyBoundsObject $referenceRawMeasurement.Bounds } else { $null })
                actualRawBounds=$(if ($actualRawMeasurement.Available) { ConvertTo-LanLobbyBoundsObject $actualRawMeasurement.Bounds } else { $null })
                referenceBounds=$(if ($referenceMeasurement.Available) { ConvertTo-LanLobbyBoundsObject $referenceMeasurement.Bounds } else { $null })
                actualBounds=$(if ($actualMeasurement.Available) { ConvertTo-LanLobbyBoundsObject $actualMeasurement.Bounds } else { $null })
                centerDeviationPx=$centerDeviation
                sizeDeviationPx=$sizeDeviation
                boundsPassed=$boundsPassed
                internalTopology=$internalTopology
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
        roomExclusions=$roomExclusionReports
        roomGates=$roomGates
        portraitFrameSharedGeometry=$portraitFrameSharedGeometry
        portraitFrameConsensus=$portraitFrameConsensus
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
    $markdown = @('# LAN Lobby Visual Difference Report', '', "Reference figure native dimensions (decoded from this export): $($referenceDimensionLines -join '; '). Each reference is independently normalized on X and Y to 1920×1080. Full-screen capture metrics are informational and are not pixel-equality acceptance; named room gates and material provenance are blocking.", '', '## Captures', '', '| Capture | Figure | Difference ratio | Avg RGB error | Attention |', '| --- | --- | ---: | ---: | --- |')
    foreach ($item in $reportCaptures) { $markdown += "| $($item.name) | $($item.referenceFigure) | $([Math]::Round($item.pixelDifferenceRatio, 4)) | $([Math]::Round($item.averageAbsoluteRgbError, 2)) | $(if($item.attention){'ATTENTION'}else{'OK'}) |" }
    $markdown += @(
        '',
        '## Portrait-frame shared geometry',
        '',
        "portraitFrameSharedGeometry: acceptanceRole=$($portraitFrameSharedGeometry.acceptanceRole); source=$($portraitFrameSharedGeometry.comparisonSource); passed=$($portraitFrameSharedGeometry.passed).",
        '',
        '| Capture | Slot | CardBody width/height | Local offset from slot root | Matches room-host | Passed |',
        '| --- | ---: | --- | --- | --- | --- |'
    )
    foreach ($record in @($portraitFrameSharedGeometry.records))
    {
        $markdown += "| $($record.capture) | $($record.slotNumber) | $($record.widthPx)x$($record.heightPx) px | $($record.localOffsetPx.x),$($record.localOffsetPx.y) px | $($record.matchesBaseline) | $($record.passed) |"
    }
    $markdown += @(
        '',
        '## Portrait-frame reconciled relative reference consensus',
        '',
        "portraitFrameConsensus: acceptanceRole=$($portraitFrameConsensus.acceptanceRole); coordinateSpace=$($portraitFrameConsensus.coordinateSpace); calculationRule=$($portraitFrameConsensus.calculationRule); contributors=$($portraitFrameConsensus.contributorCount).",
        "Target: centerFromOwnTopBar=$($portraitFrameConsensus.target.topBarHorizontalCenterDeltaPx) px; widthFromOwnTopBar=$($portraitFrameConsensus.target.topBarWidthDeltaPx) px; topFromOwnTopBarBottom=$($portraitFrameConsensus.target.frameTopFromTopBarBottomPx) px; bottomFromOwnLowerDecorationTop=$($portraitFrameConsensus.target.frameBottomFromLowerDecorationTopPx) px.",
        '',
        '| Gate | Capture | Slot | Figure | Reference frame | Reference top bar | Lower top Y | Relative metrics |',
        '| --- | --- | ---: | --- | --- | --- | ---: | --- |'
    )
    foreach ($contributor in @($portraitFrameConsensus.contributors))
    {
        $markdown += "| $($contributor.gateName) | $($contributor.capture) | $($contributor.slotNumber) | $($contributor.referenceFigure) | $($contributor.frameVisibleBounds | ConvertTo-Json -Compress) | $($contributor.topBarVisibleBounds | ConvertTo-Json -Compress) | $($contributor.lowerDecorationTopY) | $($contributor.relativeMetrics | ConvertTo-Json -Compress) |"
    }
    $markdown += @(
        '',
        '## LAN room named visible-pixel gates',
        '',
        'Blocking placement uses decoded opaque screenshot color/contrast masks measured directly from rendered RGB values. Diagnostic RectTransform rectangles do not override visible-pixel results.',
        '',
        '| Gate | Capture / reference | ROI | Exclusions | Mask | Actual / reference visible bounds | Actual/reference centers | Actual/reference pixels | Deltas / contour | Thresholds | Associated provenance | Status / reason |',
        '| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |'
    )
    foreach ($gate in @($roomGates))
    {
        $actualBounds = if ($gate.actualVisibleBounds -is [array]) { @($gate.actualVisibleBounds | Where-Object { $null -ne $_ } | ForEach-Object { "$($_.x),$($_.y),$($_.width),$($_.height)" }) -join '; ' } elseif ($gate.actualVisibleBounds) { "$($gate.actualVisibleBounds.x),$($gate.actualVisibleBounds.y),$($gate.actualVisibleBounds.width),$($gate.actualVisibleBounds.height)" } else { 'n/a' }
        $referenceBounds = if ($gate.referenceVisibleBounds -is [array]) { @($gate.referenceVisibleBounds | Where-Object { $null -ne $_ } | ForEach-Object { "$($_.x),$($_.y),$($_.width),$($_.height)" }) -join '; ' } elseif ($gate.referenceVisibleBounds) { "$($gate.referenceVisibleBounds.x),$($gate.referenceVisibleBounds.y),$($gate.referenceVisibleBounds.width),$($gate.referenceVisibleBounds.height)" } else { 'n/a' }
        $exclusionText = if (@($gate.exclusions).Count -eq 0) { 'none' } else { @($gate.exclusions | ForEach-Object { "$($_.name)[$($_.x),$($_.y),$($_.width),$($_.height)]:$($_.reason)" }) -join '; ' }
        $centerText = "actual=$($gate.actualVisibleCenter | ConvertTo-Json -Depth 8 -Compress); reference=$($gate.referenceVisibleCenter | ConvertTo-Json -Depth 8 -Compress)"
        $pixelText = "actual=$($gate.actualVisiblePixelCount | ConvertTo-Json -Depth 8 -Compress); reference=$($gate.referenceVisiblePixelCount | ConvertTo-Json -Depth 8 -Compress)"
        $portraitFrameRelationText = if ($null -ne $gate.PSObject.Properties['portraitFrameRelation']) {
            $gate.PSObject.Properties['portraitFrameRelation'].Value | ConvertTo-Json -Depth 8 -Compress
        } else { 'null' }
        $sharedGeometryText = if ($null -ne $gate.PSObject.Properties['sharedGeometryPassed']) {
            [string]$gate.PSObject.Properties['sharedGeometryPassed'].Value
        } else { 'n/a' }
        $deltaText = "center=$($gate.centerDeltaPx | ConvertTo-Json -Depth 8 -Compress); size=$($gate.sizeDeltaPx | ConvertTo-Json -Depth 8 -Compress); edge=$($gate.edgeDeltaPx | ConvertTo-Json -Depth 8 -Compress); contour=$($gate.contour | ConvertTo-Json -Depth 8 -Compress); portraitFrameRelation=$portraitFrameRelationText; sharedGeometryPassed=$sharedGeometryText"
        $thresholdText = $gate.thresholds | ConvertTo-Json -Depth 8 -Compress
        $provenanceRows = @($gate.materialEvidence.rows | Where-Object { $null -ne $_ })
        if (@($provenanceRows | Where-Object { $null -eq $_.PSObject.Properties['node'] }).Count -gt 0)
        {
            throw "Room gate '$($gate.name)' contains a material provenance row without a node identity."
        }
        $materialRows = if ($provenanceRows.Count -eq 0) { $gate.materialEvidence.associationKind } else {
            @($provenanceRows | ForEach-Object { "$($_.node):$($_.spriteName), Resources=$($_.resourcesPath), source=$($_.sourcePath), SHA=$($_.sha256), occurrence=$($_.occurrenceCount), captures=$($_.captures -join ',')" }) -join '; '
        }
        $materialText = "association=$($gate.materialEvidence.associationKind); bijection=$($gate.materialEvidence.bijectionPassed); identity=$($gate.materialEvidence.identityPassed); paths=$($gate.materialEvidence.pathPassed); SHA=$($gate.materialEvidence.shaPassed); captureList=$($gate.materialEvidence.captureListPassed); occurrence=$($gate.materialEvidence.occurrencePassed); aggregate=$($gate.materialEvidence.aggregatePassed); rows=$materialRows"
        $markdown += "| $($gate.name) | $($gate.capture) / $($gate.referenceFigure) | $($gate.roi.x),$($gate.roi.y),$($gate.roi.width),$($gate.roi.height) | $(ConvertTo-LanLobbyMarkdownCell $exclusionText) | $($gate.maskKind) | $actualBounds / $referenceBounds | $(ConvertTo-LanLobbyMarkdownCell $centerText) | $(ConvertTo-LanLobbyMarkdownCell $pixelText) | $(ConvertTo-LanLobbyMarkdownCell $deltaText) | $(ConvertTo-LanLobbyMarkdownCell $thresholdText) | $(ConvertTo-LanLobbyMarkdownCell $materialText) | $($gate.status): $(ConvertTo-LanLobbyMarkdownCell ([string]$gate.reason)) |"
    }
    $markdown += @('', '### Lossless room gate JSONL appendix', '', '<!-- ROOM_GATE_JSONL_BEGIN -->', '```jsonl')
    foreach ($gate in @($roomGates)) { $markdown += ($gate | ConvertTo-Json -Depth 20 -Compress) }
    $markdown += @('```', '<!-- ROOM_GATE_JSONL_END -->')
    $markdown += @('', '### Room reference exclusions', '', '| Capture | Exclusion | ROI | Protected regions clear | Reason |', '| --- | --- | --- | --- | --- |')
    foreach ($exclusion in @($roomExclusionReports))
    {
        $markdown += "| $($exclusion.capture) | $($exclusion.name) | $($exclusion.roi.x),$($exclusion.roi.y),$($exclusion.roi.width),$($exclusion.roi.height) | $($exclusion.protectedRegionsClear) | $(ConvertTo-LanLobbyMarkdownCell ([string]$exclusion.reason)) |"
    }
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
    $markdown += @('', '## Home Join decoration', '', "Overall passed: $($joinDecorationReport.passed). Actual crop (top-left px): $($joinDecorationReport.actualRect.x),$($joinDecorationReport.actualRect.y),$($joinDecorationReport.actualRect.width),$($joinDecorationReport.actualRect.height).", '', '| Component | Measurement | Search | Thresholds | Tolerance (px) | Expected | Reference raw | Actual raw | Bounds adjustment | Reference adjusted | Actual adjusted | Center deviation (px) | Size deviation (px) | Measurement status | Passed |', '| --- | --- | --- | --- | ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |')
    foreach ($component in @($joinDecorationReport.components))
    {
        $referenceRaw = if ($component.referenceRawBounds) { "$($component.referenceRawBounds.x),$($component.referenceRawBounds.y),$($component.referenceRawBounds.width),$($component.referenceRawBounds.height)" } else { 'unavailable' }
        $actualRaw = if ($component.actualRawBounds) { "$($component.actualRawBounds.x),$($component.actualRawBounds.y),$($component.actualRawBounds.width),$($component.actualRawBounds.height)" } else { 'unavailable' }
        $referenceMeasured = if ($component.referenceBounds) { "$($component.referenceBounds.x),$($component.referenceBounds.y),$($component.referenceBounds.width),$($component.referenceBounds.height)" } else { 'unavailable' }
        $actualMeasured = if ($component.actualBounds) { "$($component.actualBounds.x),$($component.actualBounds.y),$($component.actualBounds.width),$($component.actualBounds.height)" } else { 'unavailable' }
        $adjustment = if ($component.boundsAdjustment) {
            ConvertTo-LanLobbyMarkdownCell "$($component.boundsAdjustment.coordinateOrigin) $($component.boundsAdjustment.unit): x=$($component.boundsAdjustment.x), y=$($component.boundsAdjustment.y), width=$($component.boundsAdjustment.width), height=$($component.boundsAdjustment.height); $($component.boundsAdjustment.reason)"
        } else { 'none' }
        $centerDeviation = if ($component.centerDeviationPx) { "dx=$($component.centerDeviationPx.deltaX), dy=$($component.centerDeviationPx.deltaY)" } else { 'n/a' }
        $sizeDeviation = if ($component.sizeDeviationPx) { "dw=$($component.sizeDeviationPx.deltaWidth), dh=$($component.sizeDeviationPx.deltaHeight)" } else { 'n/a' }
        $thresholds = @($component.thresholds.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', '
        $measurementStatus = if ($component.measurementAvailable) { 'available' } else { ConvertTo-LanLobbyMarkdownCell ('unavailable: ' + [string]$component.measurementError) }
        $markdown += "| $($component.name) | $($component.measurement) | $($component.search.x),$($component.search.y),$($component.search.width),$($component.search.height) | $thresholds | $($component.tolerancePx) | $($component.expectedBounds.x),$($component.expectedBounds.y),$($component.expectedBounds.width),$($component.expectedBounds.height) | $referenceRaw | $actualRaw | $adjustment | $referenceMeasured | $actualMeasured | $centerDeviation | $sizeDeviation | $measurementStatus | $($component.passed) |"
    }
    $blockTopologyRows = @($joinDecorationReport.components | Where-Object { $_.name -eq 'block-bank' -and $null -ne $_.internalTopology })
    if ($blockTopologyRows.Count -eq 1)
    {
        $blockTopology = $blockTopologyRows[0].internalTopology
        $referenceTopologyRuns = if ($blockTopology.referenceRaw) {
            @($blockTopology.referenceRaw.runs | ForEach-Object { "$($_.startX)..$($_.endXInclusive)" }) -join ', '
        } else { 'unavailable' }
        $actualTopologyRuns = if ($blockTopology.actualRaw) {
            @($blockTopology.actualRaw.runs | ForEach-Object { "$($_.startX)..$($_.endXInclusive)" }) -join ', '
        } else { 'unavailable' }
        $referenceTopologySpan = if ($blockTopology.referenceRaw) {
            "$($blockTopology.referenceRaw.span.startX)..$($blockTopology.referenceRaw.span.endXInclusive)"
        } else { 'unavailable' }
        $actualTopologySpan = if ($blockTopology.actualRaw) {
            "$($blockTopology.actualRaw.span.startX)..$($blockTopology.actualRaw.span.endXInclusive)"
        } else { 'unavailable' }
        $topologyMeasurementStatus = if ($blockTopology.measurementAvailable) {
            'available'
        } else {
            ConvertTo-LanLobbyMarkdownCell ('unavailable: ' + [string]$blockTopology.measurementError)
        }
        $markdown += @(
            '',
            '### Block-bank internal orange topology',
            '',
            "Blocking nested measurement: $($blockTopology.measurement). Status: $topologyMeasurementStatus. Reference self passed: $($blockTopology.referenceSelfPassed). Passed: $($blockTopology.passed).",
            "Decoded-pixel rule: R >= $($blockTopology.thresholds.minimumRed), R-G >= $($blockTopology.thresholds.minimumRedOverGreen), B <= $($blockTopology.thresholds.maximumBlue); a column is occupied at >= $($blockTopology.thresholds.minimumQualifyingPixelsPerColumn) qualifying pixels. Search (crop-top-left px): $($blockTopology.search.x),$($blockTopology.search.y),$($blockTopology.search.width),$($blockTopology.search.height).",
            "Acceptance: maximum span-edge deviation $($blockTopology.acceptance.maximumSpanEdgeDeviationPx) px; maximum occupied-column-count delta $($blockTopology.acceptance.maximumOccupiedColumnCountDelta); minimum Profile Jaccard $($blockTopology.acceptance.minimumProfileJaccard).",
            '',
            '| Raw profile | Span | Occupied columns | Runs | Density |',
            '| --- | --- | ---: | --- | ---: |',
            "| Reference raw | $referenceTopologySpan | $($blockTopology.referenceRaw.occupiedColumnCount) | $referenceTopologyRuns | $($blockTopology.referenceRaw.occupiedColumnDensity) |",
            "| Actual raw | $actualTopologySpan | $($blockTopology.actualRaw.occupiedColumnCount) | $actualTopologyRuns | $($blockTopology.actualRaw.occupiedColumnDensity) |",
            '',
            '| Comparison | Profile Jaccard | Occupied-column delta | Span start/end/width delta (px) | Passed |',
            '| --- | ---: | ---: | --- | --- |',
            "| Reference vs actual | $($blockTopology.comparison.profileJaccard) | $($blockTopology.comparison.occupiedColumnCountDelta) | $($blockTopology.comparison.spanStartDeltaPx)/$($blockTopology.comparison.spanEndDeltaPx)/$($blockTopology.comparison.spanWidthDeltaPx) | $($blockTopology.comparison.passed) |",
            "| Reference vs self | $($blockTopology.referenceSelfComparison.profileJaccard) | $($blockTopology.referenceSelfComparison.occupiedColumnCountDelta) | $($blockTopology.referenceSelfComparison.spanStartDeltaPx)/$($blockTopology.referenceSelfComparison.spanEndDeltaPx)/$($blockTopology.referenceSelfComparison.spanWidthDeltaPx) | $($blockTopology.referenceSelfPassed) |"
        )
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
