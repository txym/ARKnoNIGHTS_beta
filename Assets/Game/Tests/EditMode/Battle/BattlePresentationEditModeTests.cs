using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattlePresentationEditModeTests
    {
        private const string FixturePath = "BattleFixtures/task003-minimal-v1";
        private const string RealCatalogPath = "BattleData/unit-catalog-v1";
        private const string RealBattlePath = "BattleData/task004a-real-1v1";

        [Test]
        public void Projection_HomeAwayAndDoubleRotationPreserveSourceFixedPositions()
        {
            var projection = BattlefieldWorldProjection.Default;
            var source = new FixedPosition(345, 678);
            var away = projection.Project(source, BattleObserverView.Away);

            Assert.AreEqual(source, projection.Project(source, BattleObserverView.Home));
            Assert.AreEqual(new FixedPosition(655, 222), away);
            Assert.AreEqual(source, projection.Project(away, BattleObserverView.Away));
            Assert.AreEqual(new Vector3(345f, 0f, 678f), projection.ToWorld(source, BattleObserverView.Home));
            Assert.AreEqual(new Vector3(655f, 0f, 222f), projection.ToWorld(source, BattleObserverView.Away));
        }

        [Test]
        public void TrackPlayback_BindAtMiddleCreatesOnlySpawnedUnitsAndRestoresCurrentAction()
        {
            var result = RunFixture();
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(compiler.TryCompile(result, out var track, out var diagnostics), Is.True, string.Join(";", diagnostics));
            var factory = new FakeFactory();
            var attack = result.Events.First(item => item.Type == BattleEventType.Attack);

            using (var playback = new BattleTrackPlaybackController())
            {
                Assert.That(playback.Bind(track, factory, BattleObserverView.Home, attack.Tick + 0.25d, out var bindDiagnostics), Is.True, string.Join(";", bindDiagnostics));

                Assert.That(playback.ViewStates.Select(item => item.UnitId), Is.EquivalentTo(track.Units.Where(item => item.Sample(attack.Tick + 0.25d).HasSpawned).Select(item => item.UnitId)));
                Assert.That(factory.Get(attack.UnitId).Commands, Does.Contain("attack"));
                Assert.That(factory.Get(attack.UnitId).Commands, Does.Not.Contain("hit"));
            }
        }

        [Test]
        public void TrackPlayback_BindAtDeathSkipsTheDeadUnit()
        {
            var result = RunFixture();
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(compiler.TryCompile(result, out var track, out var diagnostics), Is.True, string.Join(";", diagnostics));
            var death = result.Events.First(item => item.Type == BattleEventType.Death);
            var factory = new FakeFactory();

            using (var playback = new BattleTrackPlaybackController())
            {
                Assert.That(playback.Bind(track, factory, BattleObserverView.Home, death.Tick, out var bindDiagnostics),
                    Is.True, string.Join(";", bindDiagnostics));

                Assert.That(factory.Contains(death.UnitId), Is.False);
            }
        }

        [Test]
        public void TrackPlayback_ContinuousDeathTriggersTheExistingViewOnce()
        {
            var result = RunFixture();
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(compiler.TryCompile(result, out var track, out var diagnostics), Is.True, string.Join(";", diagnostics));
            var death = result.Events.First(item => item.Type == BattleEventType.Death);
            var factory = new FakeFactory();

            using (var playback = new BattleTrackPlaybackController())
            {
                Assert.That(playback.Bind(track, factory, BattleObserverView.Home, death.Tick - 0.01d, out var bindDiagnostics),
                    Is.True, string.Join(";", bindDiagnostics));
                Assert.That(factory.Contains(death.UnitId), Is.True);

                Assert.That(playback.RenderAt(death.Tick, out var renderDiagnostics),
                    Is.True, string.Join(";", renderDiagnostics));
                Assert.That(playback.RenderAt(death.Tick + 0.25d, out renderDiagnostics),
                    Is.True, string.Join(";", renderDiagnostics));

                Assert.That(factory.Get(death.UnitId).Commands.Count(command => command == "death"), Is.EqualTo(1));
            }
        }

        [Test]
        public void TrackPlayback_RewindBeforeDeathRecreatesTheLivingUnit()
        {
            var result = RunFixture();
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(compiler.TryCompile(result, out var track, out var diagnostics), Is.True, string.Join(";", diagnostics));
            var death = result.Events.First(item => item.Type == BattleEventType.Death);
            var factory = new FakeFactory();

            using (var playback = new BattleTrackPlaybackController())
            {
                Assert.That(playback.Bind(track, factory, BattleObserverView.Home, death.Tick, out var bindDiagnostics),
                    Is.True, string.Join(";", bindDiagnostics));
                Assert.That(factory.Contains(death.UnitId), Is.False);

                Assert.That(playback.RenderAt(death.Tick - 0.01d, out var rewindDiagnostics),
                    Is.True, string.Join(";", rewindDiagnostics));

                Assert.That(factory.Contains(death.UnitId), Is.True);
                Assert.That(factory.Get(death.UnitId).Commands, Does.Not.Contain("death"));
            }
        }

        [Test]
        public void TrackPlayback_ViewStatesSampleTheCurrentPresentationTickInsteadOfTheFinalResult()
        {
            var result = RunFixture();
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(compiler.TryCompile(result, out var track, out var diagnostics), Is.True, string.Join(";", diagnostics));
            var sampleTick = 0d;

            using (var playback = new BattleTrackPlaybackController())
            {
                Assert.That(playback.Bind(track, new FakeFactory(), BattleObserverView.Home, sampleTick, out var bindDiagnostics), Is.True, string.Join(";", bindDiagnostics));
                foreach (var state in playback.ViewStates)
                {
                    var expected = track.Units.Single(unit => unit.UnitId == state.UnitId).Sample(sampleTick);
                    Assert.That(state.HitPoints, Is.EqualTo(expected.CurrentHitPoints), state.UnitId);
                    Assert.That(state.IsAlive, Is.EqualTo(expected.IsAlive), state.UnitId);
                }
            }
        }

        [Test]
        public void Playback_ConsumesSpawnTypeIdsAndReachesResultDerivedFinalState()
        {
            var result = RunFixture();
            var factory = new FakeFactory();
            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                CollectionAssert.AreEquivalent(new[] { "home-striker", "away-guard" }, factory.CreatedTypeIds);
                Assert.AreEqual(2, playback.ConsumedEventCount);
                Assert.AreEqual(new Vector3(400f, 0f, 400f), factory.Get("home-1").LastPosition);

                playback.Play();
                playback.Advance(10f);

                Assert.IsTrue(playback.IsCompleted);
                Assert.AreEqual(result.Events.Count, playback.ConsumedEventCount);
                Assert.IsEmpty(playback.Diagnostics);
                Assert.IsTrue(playback.ViewStates.Single(item => item.UnitId == "home-1").IsAlive);
                Assert.IsFalse(playback.ViewStates.Single(item => item.UnitId == "away-1").IsAlive);
                Assert.AreEqual(result.FinalUnits.Single(item => item.UnitId == "away-1").HitPoints, playback.ViewStates.Single(item => item.UnitId == "away-1").HitPoints);
                Assert.That(factory.Get("home-1").AttackMultipliers, Does.Contain(1.5f));
            }
        }

        [Test]
        public void Playback_ExposesSealedEliteMetadataWithoutChangingEventPlayback()
        {
            var result = RunFixture();
            var factory = new FakeFactory();
            var eliteLevels = new Dictionary<string, int> { { "home-1", 2 }, { "away-1", 3 } };
            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, eliteLevels, out var diagnostics), string.Join(";", diagnostics));
                Assert.AreEqual(2, playback.ViewStates.Single(item => item.UnitId == "home-1").EliteLevel);
                Assert.AreEqual(3, playback.ViewStates.Single(item => item.UnitId == "away-1").EliteLevel);
                playback.Play();
                playback.Advance(10f);
                Assert.AreEqual(2, playback.ViewStates.Single(item => item.UnitId == "home-1").EliteLevel);
                Assert.AreEqual(3, playback.ViewStates.Single(item => item.UnitId == "away-1").EliteLevel);
            }
        }

        [Test]
        public void Playback_InterpolatesMoveAndAwayObserverOnlyChangesProjection()
        {
            var factory = new FakeFactory();
            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(RunFixture(), factory, out _));
                playback.Play();
                playback.Advance(0.025f);

                var interpolated = factory.Get("home-1").LastPosition;
                Assert.Greater(interpolated.z, 400f);
                Assert.Less(interpolated.z, 405f);

                playback.SetObserver(BattleObserverView.Away);
                Assert.AreEqual(600f, factory.Get("home-1").LastPosition.x);
                Assert.Less(factory.Get("home-1").LastPosition.z, 500f);
            }
        }

        [Test]
        public void Playback_StatusBarStateTracksSpawnDamageAndObserverWithoutChangingCoreState()
        {
            var result = RunFixture();
            var factory = new FakeFactory();
            using (var playback = new BattleEventPlaybackController())
            {
                var sourceSummary = result.StableSummary;
                var homeSpawn = result.Events.Single(item => item.Type == BattleEventType.Spawn && item.UnitId == "home-1");
                var awaySpawn = result.Events.Single(item => item.Type == BattleEventType.Spawn && item.UnitId == "away-1");
                var homeFinal = result.FinalUnits.Single(item => item.UnitId == "home-1");
                Assert.IsTrue(playback.Load(result, factory, out _));
                Assert.AreEqual("home-1|False|" + homeSpawn.HitPointsAfter + "|0", factory.Get("home-1").StatusStates.Last());
                Assert.AreEqual("away-1|True|" + awaySpawn.HitPointsAfter + "|0", factory.Get("away-1").StatusStates.Last());

                playback.Play();
                playback.Advance(10f);
                Assert.AreEqual("home-1|False|" + homeFinal.HitPoints + "|0", factory.Get("home-1").StatusStates.Last());
                Assert.AreEqual("away-1|True|0|0", factory.Get("away-1").StatusStates.Last());

                playback.SetObserver(BattleObserverView.Away);
                Assert.AreEqual("home-1|True|" + homeFinal.HitPoints + "|0", factory.Get("home-1").StatusStates.Last());
                Assert.AreEqual("away-1|False|0|0", factory.Get("away-1").StatusStates.Last());
                Assert.AreEqual(sourceSummary, result.StableSummary);
            }
        }

        [Test]
        public void Playback_AtBattleStartFacesUsingTheFirstActiveMoveInsteadOfTheLastRecordedMove()
        {
            var loaded = LocalBattleLoader.LoadFromResources(RealCatalogPath, RealBattlePath);
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var factory = new FakeFactory();
            var projection = BattlefieldWorldProjection.Default;

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));

                foreach (var spawn in result.Events.Where(item => item.Type == BattleEventType.Spawn && item.Tick == 0))
                {
                    var firstActiveMove = result.Events.FirstOrDefault(item =>
                        item.Type == BattleEventType.Move &&
                        item.UnitId == spawn.UnitId &&
                        item.Tick <= 1 &&
                        item.FromPosition.HasValue &&
                        item.ToPosition.HasValue);
                    var facingDirections = factory.Get(spawn.UnitId).FacingDirections;
                    if (firstActiveMove == null)
                    {
                        Assert.IsEmpty(facingDirections, spawn.UnitId + " has no active move at tick 0.");
                        continue;
                    }

                    var expected = projection.ToWorldDirection(firstActiveMove.FromPosition.Value, firstActiveMove.ToPosition.Value, BattleObserverView.Home);
                    if (Mathf.Approximately(expected.x, 0f))
                    {
                        Assert.IsNotEmpty(facingDirections, spawn.UnitId + " must receive its opening movement direction.");
                        Assert.That(facingDirections.Last().x, Is.EqualTo(0f).Within(0.0001f), spawn.UnitId + " must retain the default horizontal facing for a Z-only opening move.");
                    }
                    else
                    {
                        Assert.IsNotEmpty(facingDirections, spawn.UnitId + " must receive its opening horizontal movement direction.");
                        Assert.AreEqual(Mathf.Sign(expected.x), Mathf.Sign(facingDirections.Last().x), spawn.UnitId + " must face its first active movement direction.");
                    }
                }
            }
        }

        [Test]
        public void Playback_AttackFacesTheCurrentAttackTargetUntilALaterMoveChangesFacing()
        {
            var result = RunRealBattle();
            var attack = result.Events.First(item =>
                item.Type == BattleEventType.Attack &&
                item.RelatedUnitId != null &&
                PositionAt(result, item.UnitId, item.Tick).XUnits != PositionAt(result, item.RelatedUnitId, item.Tick).XUnits);
            var factory = new FakeFactory();
            var projection = BattlefieldWorldProjection.Default;

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.Play();
                playback.Advance(attack.Tick / (float)BattleInput.TicksPerSecond);

                var attackerPosition = PositionAt(result, attack.UnitId, attack.Tick);
                var targetPosition = PositionAt(result, attack.RelatedUnitId, attack.Tick);
                var expectedDirection = projection.ToWorldDirection(attackerPosition, targetPosition, BattleObserverView.Home);
                var actualDirection = factory.Get(attack.UnitId).FacingDirections.Last();
                Assert.AreEqual(Mathf.Sign(expectedDirection.x), Mathf.Sign(actualDirection.x), "Attack must update left/right facing from the attack target.");

                playback.Advance(0.01f);
                Assert.AreEqual(Mathf.Sign(expectedDirection.x), Mathf.Sign(factory.Get(attack.UnitId).FacingDirections.Last().x), "Attack facing must remain active until a later movement event changes it.");
            }
        }

        [Test]
        public void Playback_PauseSpeedAndReplayDoNotMutateResultOrLeaveViews()
        {
            var result = RunFixture();
            var summary = result.StableSummary;
            var events = string.Join("|", result.Events.Select(item => item.Type + ":" + item.Tick + ":" + item.Sequence + ":" + item.UnitTypeId));
            var factory = new FakeFactory();
            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out _));
                playback.Play();
                playback.Advance(0.05f);
                var consumedBeforePause = playback.ConsumedEventCount;
                playback.Pause();
                Assert.That(factory.Views.All(view => view.PlaybackSpeeds.Last() == 0f), Is.True);
                playback.Advance(1f);
                Assert.AreEqual(consumedBeforePause, playback.ConsumedEventCount);
                playback.SetPlaybackSpeed(2f);
                Assert.That(factory.Views.All(view => view.PlaybackSpeeds.Last() == 0f), Is.True, "Changing speed while paused must not resume Spine animation.");
                playback.Resume();
                Assert.That(factory.Views.All(view => view.PlaybackSpeeds.Last() == 2f), Is.True);
                playback.Advance(10f);
                Assert.IsTrue(playback.IsCompleted);

                Assert.IsTrue(playback.Replay(out var diagnostics), string.Join(";", diagnostics));
                Assert.AreEqual(2, playback.ConsumedEventCount);
                Assert.AreEqual(4, factory.CreateCount);
                Assert.AreEqual(2, factory.DisposeCount);
                Assert.AreEqual(summary, result.StableSummary);
                Assert.AreEqual(events, string.Join("|", result.Events.Select(item => item.Type + ":" + item.Tick + ":" + item.Sequence + ":" + item.UnitTypeId)));
            }
        }

        [Test]
        public void Playback_StopsAtAttackRangeWhenAnUnblockedUnitTargetsAFullBlocker()
        {
            var result = RunFullBlockerScenario();
            var attack = result.Events.First(item => item.Type == BattleEventType.Attack && item.UnitId == "home-a");
            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item => item.Type == BattleEventType.Move && item.UnitId == "home-a" && item.Tick == attack.Tick && item.Sequence < attack.Sequence));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item => item.Type == BattleEventType.Move && item.UnitId == "home-a" && item.Tick > attack.Tick));

            var factory = new FakeFactory();
            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.Play();
                playback.Advance((attack.PlannedDamageTick + 0.1f) / BattleInput.TicksPerSecond);

                var attackerCommands = factory.Get("home-a").Commands;
                Assert.That(attackerCommands.Count(command => command == "move"), Is.EqualTo(1));
                Assert.That(attackerCommands, Does.Contain("attack"));
            }
        }

        [Test]
        public void SpawnEvents_ExposeAuthoritativeTypeAndSideForEveryCreatedUnit()
        {
            var spawnEvents = RunFixture().Events.Where(item => item.Type == BattleEventType.Spawn).ToArray();
            Assert.That(spawnEvents.Length, Is.GreaterThan(0));
            Assert.That(spawnEvents.All(item => !string.IsNullOrWhiteSpace(item.UnitId) && !string.IsNullOrWhiteSpace(item.UnitTypeId) && item.UnitSide.HasValue && item.ToPosition.HasValue), Is.True);
        }

        [Test]
        public void DynamicSpawnTrack_UsesResultSnapshotIndexAndProjectsThreeQueuedArcslmi()
        {
            var authoritative = RunSingleArcslmaSummonBattle();
            var result = WithoutDynamicEventSnapshots(authoritative);
            var compiler = new BattlePresentationTrackCompiler();

            Assert.That(compiler.TryCompile(result, out var track, out var diagnostics), Is.True, string.Join(";", diagnostics));
            var minions = track.Units.Where(unit => unit.TypeId == "5504").ToArray();
            Assert.That(minions, Has.Length.EqualTo(3));
            var summonTick = result.Events.First(item =>
                item.Type == BattleEventType.Spawn
                && item.UnitTypeId == "5504").Tick;
            Assert.That(minions.All(unit => unit.SpawnTick == summonTick), Is.True);
            Assert.That(minions.All(unit => unit.MaxHitPoints == 2500), Is.True);

            var currentHitPoints = typeof(UnitPresentationTrack).GetProperty("CurrentHitPoints");
            var currentShield = typeof(UnitPresentationTrack).GetProperty("CurrentShield");
            var initialPosition = typeof(UnitPresentationTrack).GetProperty("InitialPosition");
            Assert.That(currentHitPoints, Is.Not.Null, "The track contract must carry snapshot CurrentHitPoints.");
            Assert.That(currentShield, Is.Not.Null, "The track contract must carry snapshot CurrentShield.");
            Assert.That(initialPosition, Is.Not.Null, "The track contract must carry the actual snapshot Spawn position.");
            foreach (var minion in minions)
            {
                Assert.That(currentHitPoints.GetValue(minion), Is.EqualTo(2500), minion.UnitId);
                Assert.That(currentShield.GetValue(minion), Is.EqualTo(0), minion.UnitId);
                var spawn = result.Events.Single(item => item.Type == BattleEventType.Spawn && item.UnitId == minion.UnitId);
                Assert.That(initialPosition.GetValue(minion), Is.EqualTo(spawn.ToPosition.Value), minion.UnitId);
                var sample = minion.Sample(summonTick);
                Assert.That(sample.Position.XUnits, Is.EqualTo(spawn.ToPosition.Value.XUnits / 100d), minion.UnitId);
                Assert.That(sample.Position.YUnits, Is.EqualTo(spawn.ToPosition.Value.YUnits / 100d), minion.UnitId);
            }
        }

        [Test]
        public void DynamicSpawnPlayback_InheritsTheActivePlaybackSpeed()
        {
            var result = WithoutDynamicEventSnapshots(RunSingleArcslmaSummonBattle());
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(compiler.TryCompile(result, out var track, out var diagnostics), Is.True, string.Join(";", diagnostics));
            var factory = new FakeFactory();
            var summonTick = result.Events.First(item =>
                item.Type == BattleEventType.Spawn
                && item.UnitTypeId == "5504").Tick;

            using (var playback = new BattleTrackPlaybackController())
            {
                Assert.That(playback.Bind(track, factory, BattleObserverView.Home, 0d, out var bindDiagnostics), Is.True, string.Join(";", bindDiagnostics));
                playback.SetPlaybackSpeed(2f);
                Assert.That(playback.RenderAt(summonTick, out var renderDiagnostics), Is.True, string.Join(";", renderDiagnostics));

                foreach (var unitId in new[] { "-1", "-2", "-3" })
                    Assert.That(factory.Get(unitId).PlaybackSpeeds.Last(), Is.EqualTo(2f), unitId);
            }
        }

        [Test]
        public void DynamicSpawnTrack_RejectsMissingAndDuplicateResultSnapshotsWithIdentityDiagnostics()
        {
            var authoritative = RunSingleArcslmaSummonBattle();
            var dynamicSpawn = authoritative.Events.First(item => item.Type == BattleEventType.Spawn && item.UnitTypeId == "5504");
            var missingSnapshots = authoritative.UnitSnapshots
                .Where(item => item.Key != dynamicSpawn.UnitId)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            AssertCompileDiagnostic(
                CloneResult(authoritative, authoritative.Events, missingSnapshots),
                "track.spawn.snapshot.missing",
                dynamicSpawn.UnitId,
                dynamicSpawn.Tick,
                dynamicSpawn.Sequence);

            var duplicateSnapshots = authoritative.UnitSnapshots.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            duplicateSnapshots.Add("duplicate-index-key", CloneSnapshot(authoritative.UnitSnapshots[dynamicSpawn.UnitId]));
            AssertCompileDiagnostic(
                CloneResult(authoritative, authoritative.Events, duplicateSnapshots),
                "track.spawn.snapshot.duplicate",
                dynamicSpawn.UnitId,
                dynamicSpawn.Tick,
                dynamicSpawn.Sequence);
        }

        [TestCase("type")]
        [TestCase("side")]
        [TestCase("position")]
        public void DynamicSpawnTrack_RejectsSpawnAgainstMismatchedResultSnapshot(string mismatch)
        {
            var authoritative = RunSingleArcslmaSummonBattle();
            var dynamicSpawn = authoritative.Events.First(item => item.Type == BattleEventType.Spawn && item.UnitTypeId == "5504");
            var source = authoritative.UnitSnapshots[dynamicSpawn.UnitId];
            var mismatched = mismatch == "type"
                ? CloneSnapshot(source, typeId: "1000")
                : mismatch == "side"
                    ? CloneSnapshot(source, side: BattleSide.Away)
                    : CloneSnapshot(source, position: new FixedPosition(source.Position.XUnits + 1, source.Position.YUnits));
            var snapshots = authoritative.UnitSnapshots.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            snapshots[dynamicSpawn.UnitId] = mismatched;

            AssertCompileDiagnostic(
                CloneResult(authoritative, authoritative.Events, snapshots),
                "track.spawn.snapshot.mismatch",
                dynamicSpawn.UnitId,
                dynamicSpawn.Tick,
                dynamicSpawn.Sequence);
        }

        [Test]
        public void DynamicSpawnTrack_RejectsEventBeforeSpawnWithIdentityDiagnostic()
        {
            var authoritative = RunSingleArcslmaSummonBattle();
            var dynamicSpawn = authoritative.Events.First(item => item.Type == BattleEventType.Spawn && item.UnitTypeId == "5504");
            var probeTick = dynamicSpawn.Tick - 1;
            var events = authoritative.Events.Where(item => item.Tick < probeTick)
                .Concat(new[]
                {
                    new BattleEvent(BattleEventType.TargetChanged, probeTick, 1, dynamicSpawn.UnitId, null, null, null, null, null,
                        null, 0, 0, 0, 0, 0, 0, null, BattleStopReason.None, null)
                })
                .Concat(authoritative.Events.Where(item => item.Tick == probeTick).Select(item => CloneEvent(item, item.Sequence + 1, item.SpawnSnapshot)))
                .Concat(authoritative.Events.Where(item => item.Tick > probeTick))
                .ToArray();

            AssertCompileDiagnostic(
                CloneResult(authoritative, events, authoritative.UnitSnapshots),
                "track.unit.beforeSpawn",
                dynamicSpawn.UnitId,
                probeTick,
                1);
        }

        [Test]
        public void Playback_MissingResourceMappingFailsWithStructuredDiagnostic()
        {
            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsFalse(playback.Load(RunFixture(), new RejectingFactory(), out var diagnostics));
                Assert.That(diagnostics.Select(item => item.Code), Does.Contain("resource.mapping.missing"));
                Assert.IsFalse(playback.IsLoaded);
            }
        }

        private static BattleRunResult RunFixture()
        {
            var loaded = BattleFixtureLoader.LoadFromResources(FixturePath);
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            return new BattleRunner(loaded.Input).RunToCompletion();
        }

        private static BattleRunResult RunRealBattle()
        {
            var loaded = LocalBattleLoader.LoadFromResources(RealCatalogPath, RealBattlePath);
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            return new BattleRunner(loaded.Input).RunToCompletion();
        }

        private static BattleRunResult RunSingleArcslmaSummonBattle()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(RealCatalogPath);
            Assert.That(catalog.Success, Is.True, string.Join(";", catalog.Errors.Select(item => item.ToString())));
            var abilities = AbilityCatalogLoader.LoadFromResources("BattleData/ability-catalog-v1", catalog.Catalog);
            Assert.That(abilities.Success, Is.True, string.Join(";", abilities.Errors.Select(item => item.ToString())));
            var definitions = catalog.Catalog.Entries
                .Select(item => item.Definition.TypeId == "1000"
                    ? WithMaxHitPoints(item.Definition, 100000)
                    : item.Definition)
                .ToArray();
            var specification = new BattleInputSpecification(
                BattleInput.SupportedSchemaVersion,
                "presentation-single-caster",
                130,
                definitions,
                abilities.Catalog.Abilities,
                new[]
                {
                    new PlayerSnapshot("home", BattleSide.Home, new[]
                    {
                        new UnitSnapshot("caster", "5503", UnitZone.Deployed, new FormationCoordinate(5, 2), Array.Empty<BuffPlaceholder>())
                    }),
                    new PlayerSnapshot("away", BattleSide.Away, new[]
                    {
                        new UnitSnapshot("enemy", "1000", UnitZone.Deployed, new FormationCoordinate(5, 2), Array.Empty<BuffPlaceholder>())
                    })
                });
            Assert.That(BattleInputFactory.TryCreate(specification, out var input, out var errors), Is.True, string.Join(";", errors.Select(item => item.ToString())));
            return new BattleRunner(input).RunToCompletion();
        }

        private static UnitDefinition WithMaxHitPoints(UnitDefinition source, int maxHitPoints)
        {
            return new UnitDefinition(
                source.TypeId,
                maxHitPoints,
                source.Attack,
                source.Defense,
                source.MagicResistance,
                source.MoveSpeedCentimetresPerSecond,
                source.AttackIntervalTicks,
                source.AttackAnimationDurationTicks,
                source.DamageType,
                source.AttackMethod,
                source.BlockCapacity,
                source.TauntLevel,
                source.IsSyntheticFixtureData,
                source.InnateAbilityIds,
                source.ActionMethod,
                source.SkillAnimations);
        }

        private static BattleRunResult WithoutDynamicEventSnapshots(BattleRunResult source)
        {
            var events = source.Events.Select(item =>
                item.Type == BattleEventType.Spawn && item.UnitId.StartsWith("-", StringComparison.Ordinal)
                    ? CloneEvent(item, item.Sequence, null)
                    : item);
            return CloneResult(source, events, source.UnitSnapshots);
        }

        private static BattleRunResult CloneResult(
            BattleRunResult source,
            IEnumerable<BattleEvent> events,
            IReadOnlyDictionary<string, BattleUnitInstanceSnapshot> snapshots)
        {
            return new BattleRunResult(
                source.BattleId,
                source.HomePlayerId,
                source.AwayPlayerId,
                source.InputCanonicalSummary,
                source.KnownUnitTypeIds,
                source.CompletedTicks,
                source.StopReason,
                source.Winner,
                source.Trace,
                new ReadOnlyCollection<BattleEvent>(events.ToArray()),
                source.FinalUnits,
                new ReadOnlyDictionary<string, BattleUnitInstanceSnapshot>(
                    snapshots.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)),
                source.StableSummary);
        }

        private static BattleEvent CloneEvent(BattleEvent source, int sequence, BattleUnitInstanceSnapshot spawnSnapshot)
        {
            return new BattleEvent(
                source.Type,
                source.Tick,
                sequence,
                source.UnitId,
                source.UnitTypeId,
                source.UnitSide,
                source.RelatedUnitId,
                source.FromPosition,
                source.ToPosition,
                source.DamageType,
                source.DamageAmount,
                source.HitPointsBefore,
                source.HitPointsAfter,
                source.PlannedDamageTick,
                source.OriginalAnimationTicks,
                source.EffectiveAnimationTicks,
                source.Winner,
                source.Reason,
                spawnSnapshot,
                source.AnimationKey);
        }

        private static BattleUnitInstanceSnapshot CloneSnapshot(
            BattleUnitInstanceSnapshot source,
            string typeId = null,
            BattleSide? side = null,
            FixedPosition? position = null)
        {
            return new BattleUnitInstanceSnapshot(
                source.UnitId,
                typeId ?? source.TypeId,
                source.PlayerId,
                side ?? source.Side,
                source.IsDynamicallyGenerated,
                position ?? source.Position,
                source.EliteLevel,
                source.MaxHitPoints,
                source.CurrentHitPoints,
                source.CurrentShield,
                source.Attack,
                source.Defense,
                source.MagicResistance,
                source.MoveSpeedCentimetresPerSecond,
                source.AttackIntervalTicks,
                source.AttackAnimationDurationTicks,
                source.DamageType,
                source.AttackMethod,
                source.BlockCapacity,
                source.TauntLevel,
                source.Buffs,
                source.ActivationTick);
        }

        private static void AssertCompileDiagnostic(
            BattleRunResult result,
            string code,
            string unitId,
            int tick,
            int sequence)
        {
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(compiler.TryCompile(result, out _, out var diagnostics), Is.False);
            Assert.That(diagnostics, Has.Some.Matches<BattlePresentationDiagnostic>(item =>
                item.Code == code
                && item.BattleId == result.BattleId
                && item.UnitId == unitId
                && item.Tick == tick
                && item.Sequence == sequence));
        }

        private static FixedPosition PositionAt(BattleRunResult result, string unitId, int tick)
        {
            var spawn = result.Events.Single(item => item.Type == BattleEventType.Spawn && item.UnitId == unitId);
            var position = spawn.ToPosition.Value;
            foreach (var move in result.Events.Where(item => item.Type == BattleEventType.Move && item.UnitId == unitId && item.Tick <= tick))
                position = move.ToPosition.Value;
            return position;
        }

        private static BattleRunResult RunFullBlockerScenario()
        {
            var definitions = new[]
            {
                new UnitDefinition("a", 10000, 1, 0, 0, 200, 100, 20, DamageType.Physical, AttackMethod.Melee, 1, 0, true),
                new UnitDefinition("b", 10000, 1, 0, 0, 0, 100, 20, DamageType.Physical, AttackMethod.Melee, 1, 0, true),
                new UnitDefinition("c", 10000, 1, 0, 0, 200, 100, 20, DamageType.Physical, AttackMethod.Melee, 1, 0, true)
            };
            var specification = new BattleInputSpecification(BattleInput.SupportedSchemaVersion, "presentation-full-blocker", 100, definitions,
                new[]
                {
                    new PlayerSnapshot("home", BattleSide.Home, new[]
                    {
                        new UnitSnapshot("home-a", "a", UnitZone.Deployed, new FormationCoordinate(4, 1), Array.Empty<BuffPlaceholder>()),
                        new UnitSnapshot("home-c", "c", UnitZone.Deployed, new FormationCoordinate(5, 4), Array.Empty<BuffPlaceholder>())
                    }),
                    new PlayerSnapshot("away", BattleSide.Away, new[]
                    {
                        new UnitSnapshot("away-b", "b", UnitZone.Deployed, new FormationCoordinate(5, 4), Array.Empty<BuffPlaceholder>())
                    })
                });
            Assert.IsTrue(BattleInputFactory.TryCreate(specification, out var input, out var errors), string.Join(";", errors.Select(item => item.ToString())));
            return new BattleRunner(input).RunToCompletion();
        }

        private sealed class FakeFactory : IBattlePresentationViewFactory
        {
            private readonly Dictionary<string, FakeView> views = new Dictionary<string, FakeView>(StringComparer.Ordinal);
            public List<string> CreatedTypeIds { get; } = new List<string>();
            public IEnumerable<FakeView> Views => views.Values;
            public int CreateCount { get; private set; }
            public int DisposeCount { get; private set; }
            public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
            {
                var created = new FakeView(() => DisposeCount++);
                views[unitId] = created;
                CreatedTypeIds.Add(typeId);
                CreateCount++;
                view = created;
                diagnostic = null;
                return true;
            }
            public FakeView Get(string unitId) => views[unitId];
            public bool Contains(string unitId) => views.ContainsKey(unitId);
        }

        private sealed class FakeView : IBattlePresentationView
        {
            private readonly Action onDispose;
            public FakeView(Action onDispose) { this.onDispose = onDispose; }
            public Vector3 LastPosition { get; private set; }
            public List<float> AttackMultipliers { get; } = new List<float>();
            public List<float> PlaybackSpeeds { get; } = new List<float>();
            public List<string> Commands { get; } = new List<string>();
            public List<Vector3> FacingDirections { get; } = new List<Vector3>();
            public List<string> StatusStates { get; } = new List<string>();
            public void SetWorldPosition(Vector3 position) => LastPosition = position;
            public void SetFacing(Vector3 direction) => FacingDirections.Add(direction);
            public void SetPlaybackSpeed(float playbackSpeed) => PlaybackSpeeds.Add(playbackSpeed);
            public void PlayMove() => Commands.Add("move");
            public void PlayAttack(float animationSpeedMultiplier) { AttackMultipliers.Add(animationSpeedMultiplier); Commands.Add("attack"); }
            public void PlayHit() => Commands.Add("hit");
            public void PlayDeath() => Commands.Add("death");
            public void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield) => StatusStates.Add(unitId + "|" + isEnemy + "|" + currentHitPoints + "|" + currentShield);
            public void Dispose() => onDispose();
        }

        private sealed class RejectingFactory : IBattlePresentationViewFactory
        {
            public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
            {
                view = null;
                diagnostic = new BattlePresentationDiagnostic("resource.mapping.missing", "No mapping for " + typeId + ".");
                return false;
            }
        }
    }
}
