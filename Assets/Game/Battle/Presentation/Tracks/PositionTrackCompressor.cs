using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;

namespace ArknoNights.Battle.Presentation
{
    internal readonly struct RawPositionPoint
    {
        internal RawPositionPoint(int tick, FixedPosition position)
        {
            Tick = tick;
            Position = position;
        }

        internal int Tick { get; }
        internal FixedPosition Position { get; }
    }

    internal sealed class PositionTrackCompressor
    {
        internal const int MaximumErrorUnits = 1;

        internal bool TryCompress(
            IReadOnlyList<RawPositionPoint> points,
            ISet<int> forcedExactTicks,
            out IReadOnlyList<UnitPresentationTrack.PositionSegment> segments,
            out long maximumSquaredErrorNumerator,
            out BattlePresentationDiagnostic diagnostic)
        {
            segments = Array.Empty<UnitPresentationTrack.PositionSegment>();
            maximumSquaredErrorNumerator = 0;
            diagnostic = null;
            if (points == null || points.Count == 0)
            {
                diagnostic = new BattlePresentationDiagnostic("track.position.empty", "A position track requires at least one point.");
                return false;
            }

            try
            {
                var output = new List<UnitPresentationTrack.PositionSegment>();
                var start = 0;
                for (var index = 1; index <= points.Count; index++)
                {
                    if (index < points.Count && points[index].Tick > points[index - 1].Tick)
                    {
                        if (points[index].Tick == points[index - 1].Tick + 1) continue;
                        CompressRun(points, start, index - 1, forcedExactTicks, output, ref maximumSquaredErrorNumerator);
                        start = index;
                        continue;
                    }

                    if (index < points.Count && points[index].Tick <= points[index - 1].Tick)
                    {
                        diagnostic = new BattlePresentationDiagnostic("track.position.order.invalid", "Position ticks must be strictly increasing.");
                        return false;
                    }

                    CompressRun(points, start, index - 1, forcedExactTicks, output, ref maximumSquaredErrorNumerator);
                }

                segments = new ReadOnlyCollection<UnitPresentationTrack.PositionSegment>(output);
                return true;
            }
            catch (OverflowException)
            {
                diagnostic = new BattlePresentationDiagnostic("track.position.overflow", "Position compression arithmetic overflowed.");
                return false;
            }
        }

        private static void CompressRun(
            IReadOnlyList<RawPositionPoint> points,
            int start,
            int end,
            ISet<int> forcedExactTicks,
            ICollection<UnitPresentationTrack.PositionSegment> output,
            ref long maximumSquaredErrorNumerator)
        {
            if (start == end)
            {
                output.Add(new UnitPresentationTrack.PositionSegment(points[start].Tick, points[start].Tick, points[start].Position, points[start].Position));
                return;
            }

            Simplify(points, start, end, forcedExactTicks, output, ref maximumSquaredErrorNumerator);
        }

        private static void Simplify(
            IReadOnlyList<RawPositionPoint> points,
            int start,
            int end,
            ISet<int> forcedExactTicks,
            ICollection<UnitPresentationTrack.PositionSegment> output,
            ref long maximumSquaredErrorNumerator)
        {
            var forcedIndex = FindForcedInteriorPoint(points, start, end, forcedExactTicks);
            if (forcedIndex >= 0)
            {
                Simplify(points, start, forcedIndex, forcedExactTicks, output, ref maximumSquaredErrorNumerator);
                Simplify(points, forcedIndex, end, forcedExactTicks, output, ref maximumSquaredErrorNumerator);
                return;
            }

            var splitIndex = -1;
            var largestError = 0L;
            var duration = checked((long)points[end].Tick - points[start].Tick);
            var allowed = checked(duration * duration * MaximumErrorUnits * MaximumErrorUnits);
            for (var index = start + 1; index < end; index++)
            {
                var error = SquaredErrorNumerator(points[start], points[end], points[index]);
                if (error > largestError)
                {
                    largestError = error;
                    splitIndex = index;
                }
            }

            maximumSquaredErrorNumerator = Math.Max(maximumSquaredErrorNumerator, largestError);
            if (splitIndex >= 0 && largestError > allowed)
            {
                Simplify(points, start, splitIndex, forcedExactTicks, output, ref maximumSquaredErrorNumerator);
                Simplify(points, splitIndex, end, forcedExactTicks, output, ref maximumSquaredErrorNumerator);
                return;
            }

            output.Add(new UnitPresentationTrack.PositionSegment(points[start].Tick, points[end].Tick, points[start].Position, points[end].Position));
        }

        private static int FindForcedInteriorPoint(IReadOnlyList<RawPositionPoint> points, int start, int end, ISet<int> forcedExactTicks)
        {
            if (forcedExactTicks == null || forcedExactTicks.Count == 0) return -1;
            for (var index = start + 1; index < end; index++)
            {
                if (forcedExactTicks.Contains(points[index].Tick)) return index;
            }

            return -1;
        }

        private static long SquaredErrorNumerator(RawPositionPoint start, RawPositionPoint end, RawPositionPoint point)
        {
            var duration = checked((long)end.Tick - start.Tick);
            var elapsed = checked((long)point.Tick - start.Tick);
            var expectedXNumerator = checked((long)start.Position.XUnits * duration + ((long)end.Position.XUnits - start.Position.XUnits) * elapsed);
            var expectedYNumerator = checked((long)start.Position.YUnits * duration + ((long)end.Position.YUnits - start.Position.YUnits) * elapsed);
            var dxNumerator = checked((long)point.Position.XUnits * duration - expectedXNumerator);
            var dyNumerator = checked((long)point.Position.YUnits * duration - expectedYNumerator);
            return checked(dxNumerator * dxNumerator + dyNumerator * dyNumerator);
        }
    }
}
