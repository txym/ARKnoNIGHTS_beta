using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class PositionTrackCompressorEditModeTests
    {
        [Test]
        public void StraightRun_CollapsesToOneSegmentAndKeepsEveryOriginalTickWithinOneUnit()
        {
            var points = Enumerable.Range(0, 31)
                .Select(tick => new RawPositionPoint(tick, new FixedPosition(tick * 10, tick * 3)))
                .ToArray();

            var segments = Compress(points);

            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(segments[0].Start, Is.EqualTo(0));
            Assert.That(segments[0].End, Is.EqualTo(30));
            AssertEveryPointIsWithinOneUnit(points, segments);
        }

        [Test]
        public void OneUnitDeviationMayCollapseButTwoUnitDeviationCreatesAKey()
        {
            var oneUnit = new[]
            {
                new RawPositionPoint(0, new FixedPosition(0, 0)),
                new RawPositionPoint(1, new FixedPosition(10, 1)),
                new RawPositionPoint(2, new FixedPosition(20, 0))
            };
            var twoUnits = new[]
            {
                new RawPositionPoint(0, new FixedPosition(0, 0)),
                new RawPositionPoint(1, new FixedPosition(10, 2)),
                new RawPositionPoint(2, new FixedPosition(20, 0))
            };

            Assert.That(Compress(oneUnit), Has.Length.EqualTo(1));
            Assert.That(Compress(twoUnits).Length, Is.GreaterThan(1));
        }

        [Test]
        public void ForcedTicksRemainExactEvenWhenTheyAreCollinear()
        {
            var points = Enumerable.Range(0, 5)
                .Select(tick => new RawPositionPoint(tick, new FixedPosition(tick * 10, 0)))
                .ToArray();

            var segments = Compress(points, new HashSet<int> { 2 });

            Assert.That(segments, Has.Length.EqualTo(2));
            Assert.That(segments.SelectMany(segment => new[] { segment.Start, segment.End }), Does.Contain(2));
            Assert.That(Sample(segments, 2), Is.EqualTo(new FixedPosition(20, 0)));
        }

        [Test]
        public void StationaryGapAlwaysSplitsMovementRuns()
        {
            var points = new[]
            {
                new RawPositionPoint(0, new FixedPosition(0, 0)),
                new RawPositionPoint(1, new FixedPosition(10, 0)),
                new RawPositionPoint(4, new FixedPosition(10, 0)),
                new RawPositionPoint(5, new FixedPosition(20, 0))
            };

            var segments = Compress(points);

            Assert.That(segments, Has.Length.EqualTo(2));
            Assert.That(segments[0].End, Is.EqualTo(1));
            Assert.That(segments[1].Start, Is.EqualTo(4));
        }

        [Test]
        public void AttackBlockDeathBattleEndAndFinalPositionAreForcedExact()
        {
            var points = Enumerable.Range(0, 7)
                .Select(tick => new RawPositionPoint(tick, new FixedPosition(tick * 10, 0)))
                .ToArray();
            var forced = new HashSet<int> { 1, 2, 3, 5, 6 };

            var segments = Compress(points, forced);

            foreach (var tick in forced)
            {
                Assert.That(segments.SelectMany(segment => new[] { segment.Start, segment.End }), Does.Contain(tick));
                Assert.That(Sample(segments, tick), Is.EqualTo(points.Single(point => point.Tick == tick).Position));
            }
        }

        [Test]
        public void Compression_IsDeterministicAcrossTenCompilations()
        {
            var points = new[]
            {
                new RawPositionPoint(0, new FixedPosition(0, 0)),
                new RawPositionPoint(1, new FixedPosition(10, 2)),
                new RawPositionPoint(2, new FixedPosition(20, 0)),
                new RawPositionPoint(3, new FixedPosition(30, 0))
            };

            var summaries = Enumerable.Range(0, 10)
                .Select(_ => string.Join(";", Compress(points).Select(segment => segment.Start + ":" + segment.End + ":" + segment.From + ":" + segment.To)))
                .Distinct()
                .ToArray();

            Assert.That(summaries, Has.Length.EqualTo(1));
        }

        private static UnitPresentationTrack.PositionSegment[] Compress(IReadOnlyList<RawPositionPoint> points, ISet<int> forcedTicks = null)
        {
            var compressor = new PositionTrackCompressor();
            Assert.That(compressor.TryCompress(points, forcedTicks ?? new HashSet<int>(), out var segments, out _, out var diagnostic), Is.True, diagnostic == null ? string.Empty : diagnostic.Message);
            return segments.ToArray();
        }

        private static void AssertEveryPointIsWithinOneUnit(IEnumerable<RawPositionPoint> points, IReadOnlyList<UnitPresentationTrack.PositionSegment> segments)
        {
            foreach (var point in points)
            {
                var sampled = Sample(segments, point.Tick);
                var dx = sampled.XUnits - point.Position.XUnits;
                var dy = sampled.YUnits - point.Position.YUnits;
                Assert.That(dx * dx + dy * dy, Is.LessThanOrEqualTo(1), "tick " + point.Tick);
            }
        }

        private static FixedPosition Sample(IReadOnlyList<UnitPresentationTrack.PositionSegment> segments, int tick)
        {
            var segment = segments.Where(item => tick >= item.Start && tick <= item.End).OrderByDescending(item => item.Start).First();
            var duration = Math.Max(1, segment.End - segment.Start);
            var elapsed = tick - segment.Start;
            return new FixedPosition(
                (int)Math.Round(segment.From.XUnits + (segment.To.XUnits - segment.From.XUnits) * (double)elapsed / duration, MidpointRounding.AwayFromZero),
                (int)Math.Round(segment.From.YUnits + (segment.To.YUnits - segment.From.YUnits) * (double)elapsed / duration, MidpointRounding.AwayFromZero));
        }
    }
}
