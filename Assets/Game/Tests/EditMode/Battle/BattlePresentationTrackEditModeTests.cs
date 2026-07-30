using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattlePresentationTrackEditModeTests
    {
        private const string FixturePath = "BattleFixtures/task003-minimal-v1";

        [Test]
        public void Compile_ProducesStableSeekableLifecycleHpAndFinalState()
        {
            var result = RunFixture();
            var track = Compile(result);
            var spawn = result.Events.First(item => item.Type == BattleEventType.Spawn);
            var unit = track.TryGetUnit(spawn.UnitId, out var found) ? found : null;

            Assert.NotNull(unit);
            Assert.IsFalse(unit.Sample(unit.SpawnTick - 0.01).HasSpawned);
            var atSpawn = unit.Sample(unit.SpawnTick);
            Assert.IsTrue(atSpawn.HasSpawned);
            Assert.IsTrue(atSpawn.ShouldDisplay);
            Assert.AreEqual(unit.MaxHitPoints, atSpawn.MaxHitPoints);

            var final = result.FinalUnits.Single(item => item.UnitId == unit.UnitId);
            var atEnd = unit.Sample(track.EndTick + 1);
            Assert.AreEqual(final.HitPoints, atEnd.CurrentHitPoints);
            Assert.AreEqual(final.IsAlive, atEnd.IsAlive);
            Assert.AreEqual(final.Position.XUnits / 100d, atEnd.Position.XUnits, 0.00001d);
            Assert.AreEqual(final.Position.YUnits / 100d, atEnd.Position.YUnits, 0.00001d);
        }

        [Test]
        public void Compile_ActionPriorityIsDeathAttackMoveIdleAndDamageCreatesNoAction()
        {
            var result = RunFixture();
            var track = Compile(result);
            var attack = result.Events.First(item => item.Type == BattleEventType.Attack);
            var attacker = track.Units.Single(item => item.UnitId == attack.UnitId);
            var damage = result.Events.First(item => item.Type == BattleEventType.Damage);
            var damaged = track.Units.Single(item => item.UnitId == damage.RelatedUnitId);
            var death = result.Events.First(item => item.Type == BattleEventType.Death);
            var dead = track.Units.Single(item => item.UnitId == death.UnitId);
            var move = result.Events.First(item => item.Type == BattleEventType.Move);
            var mover = track.Units.Single(item => item.UnitId == move.UnitId);

            Assert.AreEqual(UnitPresentationAction.Attack, attacker.Sample(attack.Tick + 0.25).Action);
            Assert.AreEqual(damage.HitPointsAfter, damaged.Sample(damage.Tick).CurrentHitPoints);
            Assert.AreNotEqual("Hit", damaged.Sample(damage.Tick).Action.ToString());
            Assert.AreEqual(UnitPresentationAction.Death, dead.Sample(death.Tick).Action);
            Assert.IsTrue(dead.Sample(death.Tick - 0.01d).ShouldDisplay);
            Assert.IsFalse(dead.Sample(death.Tick).IsAlive);
            Assert.IsFalse(dead.Sample(death.Tick).ShouldDisplay);
            Assert.IsFalse(dead.Sample(track.EndTick).ShouldDisplay);
            Assert.AreEqual(UnitPresentationAction.Move, mover.Sample(move.Tick).Action);
            Assert.AreEqual(UnitPresentationAction.Idle, attacker.Sample(attacker.SpawnTick).Action);
        }

        [Test]
        public void Compile_AttackUsesOriginalOverEffectiveAnimationMultiplier()
        {
            var result = RunFixture();
            var track = Compile(result);
            var attack = result.Events.First(item => item.Type == BattleEventType.Attack);
            var unit = track.Units.Single(item => item.UnitId == attack.UnitId);
            var during = unit.Sample(attack.Tick + 0.25);

            Assert.AreEqual(UnitPresentationAction.Attack, during.Action);
            Assert.AreEqual((float)attack.OriginalAnimationTicks / attack.EffectiveAnimationTicks,
                during.AttackAnimationSpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Compile_HomeAndAwayShareTheSameLogicalTrack()
        {
            var track = Compile(RunFixture());
            var unit = track.Units.First();
            var sample = unit.Sample(unit.SpawnTick + 0.5);

            Assert.AreEqual(track.BattleId, "task003-minimal");
            Assert.AreEqual(1, Math.Abs(sample.HorizontalFacing));
            Assert.AreEqual(sample.Position, unit.Sample(unit.SpawnTick + 0.5).Position);
        }

        [Test]
        public void Compile_AcceptsSpawnAtANonZeroTickWhenSnapshotIsComplete()
        {
            var track = Compile(CreateDynamicSpawnResult());
            var unit = track.Units.Single();

            Assert.AreEqual("-1", unit.UnitId);
            Assert.AreEqual(5, unit.SpawnTick);
            Assert.IsFalse(unit.Sample(4.99).ShouldDisplay);
            Assert.IsTrue(unit.Sample(5).ShouldDisplay);
            Assert.AreEqual(10, unit.Sample(6).CurrentHitPoints);
        }

        [Test]
        public void Compile_LegacyInitialSpawnEventSnapshotRemainsCompatible()
        {
            var track = Compile(CreateLegacyInitialSpawnResult(0));

            Assert.That(track.Units.Single().UnitId, Is.EqualTo("home-unit"));
            Assert.That(track.Units.Single().IsDynamicallyGenerated, Is.False);
        }

        [Test]
        public void Compile_RejectsPartialSnapshotIndexAndNonzeroLegacyFallback()
        {
            var authoritative = RunFixture();
            var spawn = authoritative.Events.First(item => item.Type == BattleEventType.Spawn);
            var partialSnapshots = authoritative.UnitSnapshots
                .Where(item => item.Key != spawn.UnitId)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            AssertDiagnostic(CloneWithSnapshots(authoritative, partialSnapshots), "track.spawn.snapshot.missing");
            AssertDiagnostic(CreateLegacyInitialSpawnResult(5), "track.spawn.snapshot.missing");
        }

        [Test]
        public void Compile_HoldsTheLatestPositionAcrossAnIdleGap()
        {
            var valid = CreateDynamicSpawnResult();
            var events = valid.Events.Where(item => item.Type != BattleEventType.Move).ToArray();
            var state = new RuntimeUnitState("-1", "home", BattleSide.Home, Definition(),
                new UnitSnapshot("-1", "unit", UnitZone.Deployed, new FormationCoordinate(4, 2), Array.Empty<BuffPlaceholder>()),
                new BattlefieldCoordinate(4, 2));
            var unit = Compile(CreateResult(events, new[] { new BattleUnitFinalState(state) }, valid.Winner, valid.StopReason)).Units.Single();

            var duringIdle = unit.Sample(6);

            Assert.AreEqual(4d, duringIdle.Position.XUnits, 0.00001d);
            Assert.AreEqual(2d, duringIdle.Position.YUnits, 0.00001d);
            Assert.AreEqual(UnitPresentationAction.Idle, duringIdle.Action);
        }

        [Test]
        public void Compile_RejectsPreSpawnReferenceDuplicateSpawnAndUnknownType()
        {
            var valid = CreateDynamicSpawnResult();
            var events = valid.Events.ToList();
            events.Insert(0, Event(BattleEventType.Move, 4, 1, "-1", null, null, null,
                new FixedPosition(100, 100), new FixedPosition(101, 100)));
            AssertDiagnostic(CreateResult(events, valid.FinalUnits, valid.Winner, valid.StopReason), "track.unit.beforeSpawn");

            events = valid.Events.ToList();
            events.Insert(1, Event(BattleEventType.Spawn, 5, 2, "-1", "unit", BattleSide.Home, null, null, events[0].ToPosition, snapshot: events[0].SpawnSnapshot));
            AssertDiagnostic(CreateResult(events, valid.FinalUnits, valid.Winner, valid.StopReason), "track.spawn.duplicate");

            events = valid.Events.ToList();
            var spawn = events[0];
            events[0] = Event(BattleEventType.Spawn, spawn.Tick, spawn.Sequence, spawn.UnitId, "missing-type", BattleSide.Home,
                null, null, spawn.ToPosition, snapshot: Snapshot("-1", "missing-type", 5));
            AssertDiagnostic(CreateResult(events, valid.FinalUnits, valid.Winner, valid.StopReason), "track.unit.unknown");
        }

        [Test]
        public void Compile_RejectsSpawnSnapshotMismatchAndFinalStateMismatch()
        {
            var valid = CreateDynamicSpawnResult();
            var events = valid.Events.ToList();
            var spawn = events[0];
            events[0] = Event(BattleEventType.Spawn, spawn.Tick, spawn.Sequence, "-1", "not-the-snapshot", BattleSide.Home,
                null, null, spawn.ToPosition, snapshot: Snapshot("-1", "unit", 5));
            AssertDiagnostic(CreateResult(events, valid.FinalUnits, valid.Winner, valid.StopReason), "track.spawn.snapshot.mismatch");

            var state = new RuntimeUnitState("-1", "home", BattleSide.Home, Definition(),
                new UnitSnapshot("-1", "unit", UnitZone.Deployed, new FormationCoordinate(4, 2), Array.Empty<BuffPlaceholder>()),
                new BattlefieldCoordinate(4, 2));
            state.CurrentHitPoints = 9;
            AssertDiagnostic(CreateResult(valid.Events, new[] { new BattleUnitFinalState(state) }, valid.Winner, valid.StopReason), "track.finalState.mismatch");
        }

        [Test]
        public void Compile_RepeatedRunsProduceIdenticalTrackSummary()
        {
            var result = RunFixture();
            var summaries = Enumerable.Range(0, 10).Select(_ => Compile(result).StableSummary).Distinct().ToArray();
            Assert.AreEqual(1, summaries.Length);
        }

        [Test]
        public void Compression_DoesNotChangeSourceEventsOrFinalResult()
        {
            var result = RunFixture();
            var sourceEvents = string.Join(";", result.Events.Select(item => item.Type + ":" + item.Tick + ":" + item.Sequence + ":" + item.UnitId + ":" + item.ToPosition));

            var track = Compile(result);

            Assert.AreEqual(sourceEvents, string.Join(";", result.Events.Select(item => item.Type + ":" + item.Tick + ":" + item.Sequence + ":" + item.UnitId + ":" + item.ToPosition)));
            Assert.AreEqual(result.Winner, track.Winner);
            Assert.AreEqual(result.StopReason, track.StopReason);
            foreach (var final in result.FinalUnits)
            {
                var unit = track.Units.Single(item => item.UnitId == final.UnitId);
                var sample = unit.Sample(track.EndTick);
                Assert.AreEqual(final.IsAlive, sample.IsAlive);
                Assert.AreEqual(final.HitPoints, sample.CurrentHitPoints);
                Assert.AreEqual(final.Position.XUnits / 100d, sample.Position.XUnits, 0.00001d);
                Assert.AreEqual(final.Position.YUnits / 100d, sample.Position.YUnits, 0.00001d);
            }
        }

        [Test]
        public void Compression_ReportsBoundedMetricsAndPreservesEveryMoveSample()
        {
            var result = RunFixture();
            var track = Compile(result);

            Assert.That(track.CompressionMetrics.OriginalMoveCount, Is.EqualTo(result.Events.Count(item => item.Type == BattleEventType.Move)));
            Assert.That(track.CompressionMetrics.PositionKeyCount, Is.GreaterThan(0));
            Assert.That(track.CompressionMetrics.MaximumErrorUnits, Is.LessThanOrEqualTo(1d));
            foreach (var move in result.Events.Where(item => item.Type == BattleEventType.Move))
            {
                var sample = track.Units.Single(item => item.UnitId == move.UnitId).Sample(move.Tick);
                var dx = sample.Position.XUnits * 100d - move.ToPosition.Value.XUnits;
                var dy = sample.Position.YUnits * 100d - move.ToPosition.Value.YUnits;
                Assert.That(dx * dx + dy * dy, Is.LessThanOrEqualTo(1d), move.UnitId + " tick " + move.Tick);
            }
        }

        private static BattlePresentationTrack Compile(BattleRunResult result)
        {
            var compiler = new BattlePresentationTrackCompiler();
            Assert.IsTrue(compiler.TryCompile(result, out var track, out var diagnostics), string.Join(";", diagnostics));
            Assert.NotNull(track);
            return track;
        }

        private static void AssertDiagnostic(BattleRunResult result, string code)
        {
            var compiler = new BattlePresentationTrackCompiler();
            Assert.IsFalse(compiler.TryCompile(result, out _, out var diagnostics));
            Assert.That(diagnostics, Has.Some.Matches<BattlePresentationDiagnostic>(item => item.Code == code && item.BattleId == result.BattleId && item.Tick >= 0 && item.Sequence >= 0));
        }

        private static BattleRunResult RunFixture()
        {
            var loaded = BattleFixtureLoader.LoadFromResources(FixturePath);
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors));
            return new BattleRunner(loaded.Input).RunToCompletion();
        }

        private static BattleRunResult CreateDynamicSpawnResult()
        {
            var snapshot = Snapshot("-1", "unit", 5);
            var events = new[]
            {
                Event(BattleEventType.Spawn, 5, 1, "-1", "unit", BattleSide.Home, null, null, snapshot.Position, snapshot: snapshot),
                Event(BattleEventType.Move, 6, 1, "-1", null, null, null, snapshot.Position, new FixedPosition(401, 200)),
                Event(BattleEventType.BattleEnded, 7, 1, null, null, null, null, null, null, winner: BattleSide.Home, reason: BattleStopReason.Victory)
            };
            var state = new RuntimeUnitState("-1", "home", BattleSide.Home, Definition(),
                new UnitSnapshot("-1", "unit", UnitZone.Deployed, new FormationCoordinate(4, 2), Array.Empty<BuffPlaceholder>()),
                new BattlefieldCoordinate(4, 2));
            state.Position = new FixedPosition(401, 200);
            return CreateResult(events, new[] { new BattleUnitFinalState(state) }, BattleSide.Home, BattleStopReason.Victory);
        }

        private static BattleRunResult CreateLegacyInitialSpawnResult(int spawnTick)
        {
            var snapshot = Snapshot("home-unit", "unit", spawnTick, false);
            var events = new[]
            {
                Event(BattleEventType.Spawn, spawnTick, 1, "home-unit", "unit", BattleSide.Home, null, null, snapshot.Position, snapshot: snapshot),
                Event(BattleEventType.BattleEnded, spawnTick + 1, 1, null, null, null, null, null, null, winner: BattleSide.Home, reason: BattleStopReason.Victory)
            };
            var state = new RuntimeUnitState("home-unit", "home", BattleSide.Home, Definition(),
                new UnitSnapshot("home-unit", "unit", UnitZone.Deployed, new FormationCoordinate(4, 2), Array.Empty<BuffPlaceholder>()),
                new BattlefieldCoordinate(4, 2));
            return new BattleRunResult("legacy-initial-track", "home", "away", "input-summary",
                new ReadOnlyCollection<string>(new[] { "unit" }), spawnTick + 1, BattleStopReason.Victory, BattleSide.Home,
                new ReadOnlyCollection<BattleStepTrace>(Array.Empty<BattleStepTrace>()),
                new ReadOnlyCollection<BattleEvent>(events),
                new ReadOnlyCollection<BattleUnitFinalState>(new[] { new BattleUnitFinalState(state) }),
                "result-summary",
                0,
                1);
        }

        private static BattleRunResult CloneWithSnapshots(
            BattleRunResult source,
            IReadOnlyDictionary<string, BattleUnitInstanceSnapshot> snapshots)
        {
            return new BattleRunResult(source.BattleId, source.HomePlayerId, source.AwayPlayerId, source.InputCanonicalSummary,
                source.KnownUnitTypeIds, source.CompletedTicks, source.StopReason, source.Winner, source.Trace, source.Events,
                source.FinalUnits, new ReadOnlyDictionary<string, BattleUnitInstanceSnapshot>(
                    snapshots.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)), source.StableSummary,
                source.HomeLifeDamage, source.AwayLifeDamage, source.FinalCheckpoint);
        }

        private static BattleRunResult CreateResult(IEnumerable<BattleEvent> events, IEnumerable<BattleUnitFinalState> finalUnits, BattleSide? winner, BattleStopReason reason)
        {
            var eventArray = events.ToArray();
            var snapshots = eventArray
                .Where(item => item.Type == BattleEventType.Spawn && item.SpawnSnapshot != null)
                .Select(item => item.SpawnSnapshot)
                .GroupBy(item => item.UnitId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            return new BattleRunResult("track-test", "home", "away", "input-summary", new ReadOnlyCollection<string>(new[] { "unit" }), 7,
                reason, winner, new ReadOnlyCollection<BattleStepTrace>(Array.Empty<BattleStepTrace>()),
                new ReadOnlyCollection<BattleEvent>(eventArray), new ReadOnlyCollection<BattleUnitFinalState>(finalUnits.ToArray()),
                new ReadOnlyDictionary<string, BattleUnitInstanceSnapshot>(snapshots), "result-summary",
                winner == BattleSide.Away ? 1 : 0,
                winner == BattleSide.Home ? 1 : 0);
        }

        private static BattleUnitInstanceSnapshot Snapshot(string unitId, string typeId, int spawnTick, bool isDynamicallyGenerated = true)
            => new BattleUnitInstanceSnapshot(unitId, typeId, "home", BattleSide.Home, isDynamicallyGenerated, new FixedPosition(400, 200), 1,
                10, 10, 0, 3, 0, 0, 100, 20, 4, DamageType.Physical, AttackMethod.Melee, 1, 0, Array.Empty<BuffPlaceholder>());

        private static BattleEvent Event(BattleEventType type, int tick, int sequence, string unitId, string typeId, BattleSide? side,
            string related, FixedPosition? from, FixedPosition? to, BattleSide? winner = null, BattleStopReason reason = BattleStopReason.None,
            BattleUnitInstanceSnapshot snapshot = null)
            => new BattleEvent(type, tick, sequence, unitId, typeId, side, related, from, to, null, 0, 0, 10, 0, 0, 0, winner, reason, snapshot);

        private static UnitDefinition Definition()
            => new UnitDefinition("unit", 10, 3, 0, 0, 100, 20, 4, DamageType.Physical, AttackMethod.Melee, 1, 0, true);
    }
}
