using System;
using System.Collections.Generic;
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

                foreach (var spawn in result.Events.Where(item => item.Type == BattleEventType.Spawn))
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
                Assert.That(attackerCommands.Last(), Is.EqualTo("attack"));
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
