using System;
using System.IO;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattleCoreEditModeTests
    {
        private const string FixturePath = "BattleFixtures/task002-minimal-v1";
        private const string CombatFixturePath = "BattleFixtures/task003-minimal-v1";
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string RealBattlePath = "BattleData/task004a-real-1v1";

        [Test]
        public void Coordinates_UseOneBasedNineByFourAndNineByEightBounds()
        {
            Assert.IsTrue(FormationCoordinate.TryCreate(1, 1, out _));
            Assert.IsTrue(FormationCoordinate.TryCreate(9, 4, out _));
            Assert.IsFalse(FormationCoordinate.TryCreate(0, 1, out _));
            Assert.IsFalse(FormationCoordinate.TryCreate(9, 5, out _));
            Assert.IsTrue(BattlefieldCoordinate.TryCreate(1, 1, out _));
            Assert.IsTrue(BattlefieldCoordinate.TryCreate(9, 8, out _));
            Assert.IsFalse(BattlefieldCoordinate.TryCreate(10, 8, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FormationCoordinate(0, 1));
        }

        [Test]
        public void Gates_AreInBoundsButNotDeployable()
        {
            Assert.IsTrue(BattlefieldRules.BlueGate.IsValid);
            Assert.IsTrue(BattlefieldRules.RedGate.IsValid);
            Assert.IsTrue(BattlefieldRules.IsGate(BattlefieldRules.BlueGate));
            Assert.IsFalse(BattlefieldRules.IsDeployable(BattlefieldRules.BlueGate));
            Assert.IsFalse(BattlefieldRules.IsDeployable(BattlefieldRules.RedGate));
            Assert.IsTrue(BattlefieldRules.IsDeployable(new BattlefieldCoordinate(4, 1)));
        }

        [Test]
        public void Mapping_HomeIsDirect_AwayAndDoubleRotationAreStable()
        {
            var source = new FormationCoordinate(4, 3);
            var home = BattlefieldRules.MapHome(source);
            var away = BattlefieldRules.MapAway(source);
            Assert.AreEqual(new BattlefieldCoordinate(4, 3), home);
            Assert.AreEqual(new BattlefieldCoordinate(6, 6), away);
            Assert.AreEqual(home, BattlefieldRules.Rotate180(BattlefieldRules.Rotate180(home)));
            Assert.AreEqual(new FormationCoordinate(4, 3), source);
        }

        [Test]
        public void FixedPosition_RepresentsQuarterMetresExactly()
        {
            var position = FixedPosition.FromCell(new BattlefieldCoordinate(3, 7));
            Assert.AreEqual(300, position.XUnits);
            Assert.AreEqual(700, position.YUnits);
            Assert.AreEqual(25, FixedPosition.QuarterMetre);
            Assert.AreEqual(5, 100 / BattleInput.TicksPerSecond);
        }

        [Test]
        public void Fixture_LoadsImmutableCompleteSnapshotsAndOnlyDeployedUnitsRun()
        {
            var loaded = BattleFixtureLoader.LoadFromResources(FixturePath);
            Assert.IsTrue(loaded.Success, Errors(loaded));
            Assert.AreEqual(2, loaded.Input.Players.Count);
            Assert.AreEqual(4, loaded.Input.Players.Sum(player => player.Units.Count));
            var runner = new BattleRunner(loaded.Input);
            Assert.AreEqual(2, runner.RuntimeUnits.Count);
            CollectionAssert.AreEquivalent(new[] { "unit-away-1", "unit-home-1" }, runner.RuntimeUnits.Select(unit => unit.UnitId));
            Assert.AreEqual(600, runner.RuntimeUnits.Single(unit => unit.UnitId == "unit-away-1").Position.XUnits);
            Assert.AreEqual(600, runner.RuntimeUnits.Single(unit => unit.UnitId == "unit-away-1").Position.YUnits);
        }

        [Test]
        public void Fixture_RepeatedLoadsHaveTheSameCanonicalSummary()
        {
            var first = BattleFixtureLoader.LoadFromResources(FixturePath);
            var second = BattleFixtureLoader.LoadFromResources(FixturePath);
            Assert.IsTrue(first.Success, Errors(first));
            Assert.IsTrue(second.Success, Errors(second));
            Assert.AreEqual(first.Input.CanonicalSummary, second.Input.CanonicalSummary);
        }

        [Test]
        public void Fixture_InvalidInputMatrixReturnsStructuredErrors()
        {
            var valid = UnityEngine.Resources.Load<UnityEngine.TextAsset>(FixturePath).text;
            var variants = new[]
            {
                valid.Replace("battle-fixture-v1", "unknown-v99"),
                valid.Replace("unit-away-1", "unit-home-1"),
                valid.Replace("guard-beta", "guard-alpha"),
                valid.Replace("\"unitId\": \"unit-home-1\", \"typeId\": \"guard-alpha\"", "\"unitId\": \"unit-home-1\", \"typeId\": \"missing-type\""),
                valid.Replace("\"formationX\": 4, \"formationY\": 2", "\"formationX\": 5, \"formationY\": 1"),
                valid.Replace("\"zone\": \"Staging\", \"eliteLevel\": 0, \"buffs\": []", "\"zone\": \"Staging\", \"formationX\": 1, \"formationY\": 1, \"eliteLevel\": 0, \"buffs\": []"),
                valid.Replace("\"attackIntervalTicks\": 20", "\"attackIntervalTicks\": 0")
            };
            foreach (var json in variants)
            {
                var result = BattleFixtureLoader.LoadFromJson(json);
                Assert.IsFalse(result.Success);
                Assert.That(result.Errors.Count, Is.GreaterThan(0));
                Assert.That(result.Errors.All(error => !string.IsNullOrEmpty(error.Code)));
            }
        }

        [Test]
        public void Runner_IsExplicitAndTenRepeatedRunsProduceTheSameTraceAndSummary()
        {
            var input = BattleFixtureLoader.LoadFromResources(FixturePath).Input;
            string summary = null;
            string trace = null;
            for (var index = 0; index < 10; index++)
            {
                var runner = new BattleRunner(input);
                Assert.AreEqual(1, runner.Step().Tick);
                var result = runner.RunToCompletion();
                var currentTrace = string.Join("|", result.Trace.Select(item => item.ToString()));
                if (index == 0) { summary = result.StableSummary; trace = currentTrace; }
                else { Assert.AreEqual(summary, result.StableSummary); Assert.AreEqual(trace, currentTrace); }
            }
        }

        [Test]
        public void Runner_ReachingMaxTicksIsUnresolvedAndHasNoWinner()
        {
            var input = BattleFixtureLoader.LoadFromResources(FixturePath).Input;
            var result = new BattleRunner(input).RunToCompletion();
            Assert.AreEqual(input.MaxTicks, result.CompletedTicks);
            Assert.AreEqual(BattleStopReason.MaxTicksReached, result.StopReason);
            Assert.IsFalse(result.IsResolved);
            Assert.IsNull(result.Winner);
        }

        [Test]
        public void CoreAssembly_HasNoUnityEngineAssemblyReference()
        {
            Assert.IsFalse(typeof(BattleInput).Assembly.GetReferencedAssemblies().Any(assembly => assembly.Name.IndexOf("UnityEngine", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void CombatFixture_CompletesMovementBlockingAttackDamageDeathAndHomeVictory()
        {
            var loaded = BattleFixtureLoader.LoadFromResources(CombatFixturePath);
            Assert.IsTrue(loaded.Success, Errors(loaded));
            var result = new BattleRunner(loaded.Input).RunToCompletion();

            Assert.IsTrue(result.IsResolved);
            Assert.AreEqual(BattleSide.Home, result.Winner);
            Assert.AreEqual(BattleStopReason.Victory, result.StopReason);
            CollectionAssert.IsSubsetOf(new[]
            {
                BattleEventType.Spawn, BattleEventType.Move, BattleEventType.TargetChanged, BattleEventType.BlockStarted,
                BattleEventType.BlockEnded, BattleEventType.Attack, BattleEventType.Damage, BattleEventType.Death, BattleEventType.BattleEnded
            }, result.Events.Select(item => item.Type).Distinct().ToArray());
            var attack = result.Events.First(item => item.Type == BattleEventType.Attack && item.UnitId == "home-1");
            Assert.AreEqual(6, attack.OriginalAnimationTicks);
            Assert.AreEqual(4, attack.EffectiveAnimationTicks);
            Assert.AreEqual(attack.Tick + 4, attack.PlannedDamageTick);
            Assert.AreEqual(0, result.Events.Last(item => item.Type == BattleEventType.Death).HitPointsAfter);
        }

        [Test]
        public void CombatEvents_AreStrictlyOrderedAndTenRunsAreIdentical()
        {
            var input = BattleFixtureLoader.LoadFromResources(CombatFixturePath).Input;
            string expected = null;
            for (var run = 0; run < 10; run++)
            {
                var result = new BattleRunner(input).RunToCompletion();
                var actual = string.Join("|", result.Events.Select(EventSummary));
                if (run == 0) expected = actual;
                else Assert.AreEqual(expected, actual);
                foreach (var tick in result.Events.GroupBy(item => item.Tick))
                    CollectionAssert.AreEqual(Enumerable.Range(1, tick.Count()).ToArray(), tick.Select(item => item.Sequence).ToArray());
            }
        }

        [Test]
        public void Targeting_UsesDistanceThenGateDistanceThenStableUnitId()
        {
            var definition = Definition("unit", 0);
            var input = CreateInput(2, new[] { definition },
                new[] { Unit("home", "unit", 5, 4) },
                new[] { Unit("enemy-b", "unit", 6, 4), Unit("enemy-a", "unit", 4, 4) });
            var runner = new BattleRunner(input);
            runner.Step();
            Assert.AreEqual("enemy-a", runner.RuntimeUnits.Single(item => item.UnitId == "home").TargetUnitId);
        }

        [Test]
        public void Movement_PreservesLowSpeedRemainderAndDoesNotOvershoot()
        {
            var input = CreateInput(25, new[] { Definition("slow", 1), Definition("static", 0) },
                new[] { Unit("home", "slow", 4, 4) }, new[] { Unit("away", "static", 6, 4) });
            var runner = new BattleRunner(input);
            for (var index = 0; index < 20; index++) runner.Step();
            Assert.AreEqual(401, runner.RuntimeUnits.Single(item => item.UnitId == "home").Position.YUnits);
            Assert.LessOrEqual(runner.RuntimeUnits.Single(item => item.UnitId == "home").Position.YUnits, 500);
        }

        [Test]
        public void Blocking_UsesStrictQuarterMetreBoundaryThenCreatesSymmetricRelation()
        {
            var input = CreateInput(20, new[] { Definition("walker", 100), Definition("static", 0) },
                new[] { Unit("home", "walker", 4, 4) }, new[] { Unit("away", "static", 6, 4) });
            var runner = new BattleRunner(input);
            for (var index = 0; index < 15; index++) runner.Step();
            Assert.IsNull(runner.RuntimeUnits.Single(item => item.UnitId == "home").BlockedUnitId);
            runner.Step();
            Assert.AreEqual("away", runner.RuntimeUnits.Single(item => item.UnitId == "home").BlockedUnitId);
            Assert.AreEqual("home", runner.RuntimeUnits.Single(item => item.UnitId == "away").BlockedUnitId);
        }

        [Test]
        public void Blocking_EnteringRangeInOneFastMoveStopsBeforeOverlappingTheTarget()
        {
            var input = CreateInput(2, new[] { Definition("fast", 2000), Definition("static", 0) },
                new[] { Unit("home", "fast", 4, 4) }, new[] { Unit("away", "static", 6, 4) });
            var runner = new BattleRunner(input);

            runner.Step();

            var home = runner.RuntimeUnits.Single(item => item.UnitId == "home");
            var away = runner.RuntimeUnits.Single(item => item.UnitId == "away");
            var dx = home.Position.XUnits - away.Position.XUnits;
            var dy = home.Position.YUnits - away.Position.YUnits;
            var distanceSquared = dx * dx + dy * dy;
            Assert.That(distanceSquared, Is.LessThan(FixedPosition.QuarterMetre * FixedPosition.QuarterMetre));
            Assert.That(distanceSquared, Is.GreaterThan(0), "Entering the blocking radius must not move a unit onto the target centre.");
            CollectionAssert.AreEqual(new[] { "away" }, home.BlockedUnitIds);
            Assert.That(runner.Events, Has.Some.Matches<BattleEvent>(item => item.Type == BattleEventType.Attack && item.UnitId == "home" && item.RelatedUnitId == "away"));
        }

        [Test]
        public void Blocking_InterceptsAMovingUnitEvenWhenTheInterceptorIsNotItsTarget()
        {
            var input = CreateInput(2, new[] { Definition("mover", 100), Definition("target", 0), Definition("interceptor", 10000) },
                new[] { Unit("home-mover", "mover", 5, 4) },
                new[] { Unit("away-target", "target", 5, 4), Unit("away-interceptor", "interceptor", 4, 4) });
            var runner = new BattleRunner(input);

            runner.Step();

            var mover = runner.RuntimeUnits.Single(item => item.UnitId == "home-mover");
            var interceptor = runner.RuntimeUnits.Single(item => item.UnitId == "away-interceptor");
            Assert.AreEqual("away-target", mover.TargetUnitId);
            Assert.AreEqual(405, mover.Position.YUnits, "The mover advances before the post-movement blocking phase.");
            CollectionAssert.AreEqual(new[] { "away-interceptor" }, mover.BlockedUnitIds);
            CollectionAssert.AreEqual(new[] { "home-mover" }, interceptor.BlockedUnitIds);
        }

        [Test]
        public void Blocking_DoesNotReplaceAnInRangeNormalAttackTarget()
        {
            var input = CreateInput(3, new[]
                {
                    Definition("focal", 2000, attack: 1, interval: 1, animation: 1, capacity: 2),
                    Definition("primary", 2000, attack: 0, interval: 10, animation: 1),
                    Definition("interceptor", 2000, attack: 0, interval: 10, animation: 1)
                },
                new[] { Unit("home-focal", "focal", 5, 4) },
                new[] { Unit("away-primary", "primary", 5, 4), Unit("away-interceptor", "interceptor", 4, 4) });
            var runner = new BattleRunner(input);

            runner.Step();
            runner.Step();
            runner.Step();

            var focal = runner.RuntimeUnits.Single(item => item.UnitId == "home-focal");
            CollectionAssert.Contains(focal.BlockedUnitIds, "away-interceptor");
            CollectionAssert.Contains(focal.BlockedUnitIds, "away-primary");
            Assert.AreEqual("away-primary", focal.TargetUnitId);
            Assert.That(runner.Events, Has.Some.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Attack && item.Tick == 3 &&
                item.UnitId == "home-focal" &&
                item.RelatedUnitId == "away-primary"));
        }

        [Test]
        public void Blocking_FullTargetStillAllowsAnIncomingUnitToStartAttackAtRange()
        {
            var input = CreateInput(100, new[]
                {
                    Definition("a", 200, hitPoints: 10000, interval: 100, animation: 20),
                    Definition("b", 0, hitPoints: 10000, interval: 100, animation: 20),
                    Definition("c", 200, hitPoints: 10000, interval: 100, animation: 20)
                },
                new[] { Unit("home-a", "a", 4, 1), Unit("home-c", "c", 5, 4) },
                new[] { Unit("away-b", "b", 5, 4) });
            var runner = new BattleRunner(input);
            while (!runner.Events.Any(item => item.Type == BattleEventType.Attack && item.UnitId == "home-a")) runner.Step();

            var attack = runner.Events.First(item => item.Type == BattleEventType.Attack && item.UnitId == "home-a");
            Assert.That(runner.Events, Has.Some.Matches<BattleEvent>(item => item.Type == BattleEventType.Move && item.UnitId == "home-a" && item.Tick == attack.Tick && item.Sequence < attack.Sequence));
            Assert.That(runner.Events, Has.None.Matches<BattleEvent>(item => item.Type == BattleEventType.Move && item.UnitId == "home-a" && item.Tick > attack.Tick),
                "An in-range attack target keeps an unblocked unit stationary even when that target has no remaining block capacity.");
            CollectionAssert.AreEqual(new[] { "home-c" }, runner.RuntimeUnits.Single(item => item.UnitId == "away-b").BlockedUnitIds);
            Assert.IsEmpty(runner.RuntimeUnits.Single(item => item.UnitId == "home-a").BlockedUnitIds);
            Assert.That(runner.Events, Has.None.Matches<BattleEvent>(item => item.Type == BattleEventType.BlockStarted && item.UnitId == "home-a" && item.RelatedUnitId == "away-b"));
        }

        [Test]
        public void Blocking_UsesTargetThenTauntGateAndUnitIdPriorityWhileRespectingMultipleCapacity()
        {
            var capacityInput = CreateInput(2, new[]
                {
                    Definition("anchor", 0, capacity: 2), Definition("nearest-target", 0),
                    Definition("low-taunt", 10000, tauntLevel: 2), Definition("high-taunt", 10000, tauntLevel: 9)
                },
                new[] { Unit("home-anchor", "anchor", 5, 4) },
                new[] { Unit("away-nearest", "nearest-target", 5, 4), Unit("away-low", "low-taunt", 4, 4), Unit("away-high", "high-taunt", 6, 4) });
            var capacityRunner = new BattleRunner(capacityInput);
            capacityRunner.Step();

            var anchor = capacityRunner.RuntimeUnits.Single(item => item.UnitId == "home-anchor");
            CollectionAssert.AreEqual(new[] { "away-high", "away-low" }, anchor.BlockedUnitIds);
            CollectionAssert.AreEqual(new[] { "away-high", "away-low" }, capacityRunner.Events.Where(item => item.Type == BattleEventType.BlockStarted).Select(item => item.UnitId).ToArray());

            var targetPriorityInput = CreateInput(2, new[]
                {
                    Definition("anchor", 0), Definition("current-target", 10000), Definition("higher-taunt", 10000, tauntLevel: 99)
                },
                new[] { Unit("home-anchor", "anchor", 5, 4) },
                new[] { Unit("away-target", "current-target", 5, 4), Unit("away-taunting", "higher-taunt", 4, 4) });
            var targetPriorityRunner = new BattleRunner(targetPriorityInput);
            targetPriorityRunner.Step();
            CollectionAssert.AreEqual(new[] { "away-target" }, targetPriorityRunner.RuntimeUnits.Single(item => item.UnitId == "home-anchor").BlockedUnitIds);

            var gatePriorityInput = CreateInput(2, new[]
                {
                    Definition("anchor", 0), Definition("nearest-target", 0),
                    Definition("farther-from-home-gate", 2400), Definition("nearer-to-home-gate", 3800)
                },
                new[] { Unit("home-anchor", "anchor", 5, 4) },
                new[] { Unit("away-nearest", "nearest-target", 5, 4), Unit("away-farther", "farther-from-home-gate", 4, 4), Unit("away-nearer", "nearer-to-home-gate", 5, 3) });
            var gatePriorityRunner = new BattleRunner(gatePriorityInput);
            gatePriorityRunner.Step();
            CollectionAssert.AreEqual(new[] { "away-farther" }, gatePriorityRunner.RuntimeUnits.Single(item => item.UnitId == "home-anchor").BlockedUnitIds,
                "The non-overlapping entry clamp changes the candidates' post-movement distances; gate priority must use those authoritative positions.");

            var idPriorityInput = CreateInput(2, new[]
                {
                    Definition("anchor", 0), Definition("nearest-target", 0), Definition("candidate", 10000)
                },
                new[] { Unit("home-anchor", "anchor", 5, 4) },
                new[] { Unit("away-nearest", "nearest-target", 5, 4), Unit("away-b", "candidate", 4, 4), Unit("away-a", "candidate", 6, 4) });
            var idPriorityRunner = new BattleRunner(idPriorityInput);
            idPriorityRunner.Step();
            CollectionAssert.AreEqual(new[] { "away-a" }, idPriorityRunner.RuntimeUnits.Single(item => item.UnitId == "home-anchor").BlockedUnitIds);
        }

        [Test]
        public void DamageCalculator_UsesPhysicalMagicTrueAndFivePercentFloor()
        {
            Assert.AreEqual(5, DamageCalculator.Calculate(DamageType.Physical, 100, 200, 0));
            Assert.AreEqual(5, DamageCalculator.Calculate(DamageType.Magic, 100, 0, 99));
            Assert.AreEqual(33, DamageCalculator.Calculate(DamageType.Magic, 100, 0, 67));
            Assert.AreEqual(17, DamageCalculator.Calculate(DamageType.True, 17, 999, 100));
        }

        [Test]
        public void Attack_TargetDiesBeforeDamage_AnimationCompletesBeforeAttackerCanMoveAgain()
        {
            var input = CreateInput(8, new[]
                {
                    Definition("attacker", 10000, attack: 1, interval: 10, animation: 4),
                    Definition("killer", 10000, attack: 1000, interval: 10, animation: 1),
                    Definition("fragile", 0, hitPoints: 100, attack: 0, interval: 10, animation: 1),
                    Definition("survivor", 0, hitPoints: 1000, attack: 0, interval: 10, animation: 1)
                },
                new[] { Unit("home-attacker", "attacker", 5, 4), Unit("home-killer", "killer", 4, 4) },
                new[] { Unit("away-fragile", "fragile", 5, 4), Unit("away-survivor", "survivor", 5, 3) });
            var runner = new BattleRunner(input);

            runner.Step();
            var attack = runner.Events.Single(item => item.Type == BattleEventType.Attack && item.UnitId == "home-attacker");
            Assert.AreEqual(5, attack.PlannedDamageTick);

            runner.Step();
            Assert.That(runner.Events, Has.Some.Matches<BattleEvent>(item => item.Type == BattleEventType.Death && item.UnitId == "away-fragile" && item.Tick == 2));

            for (var tick = 3; tick <= attack.PlannedDamageTick; tick++)
            {
                runner.Step();
                var attacker = runner.RuntimeUnits.Single(item => item.UnitId == "home-attacker");
                Assert.AreEqual("away-survivor", attacker.TargetUnitId);
                Assert.AreEqual(new FixedPosition(500, 476), attacker.Position, "Attack animation must keep the attacker stationary through Tick " + tick + ".");
            }

            Assert.IsEmpty(runner.Events.Where(item => item.Type == BattleEventType.Damage && item.UnitId == "home-attacker" && item.RelatedUnitId == "away-fragile"));
            runner.Step();
            Assert.AreEqual(new FixedPosition(500, 576), runner.RuntimeUnits.Single(item => item.UnitId == "home-attacker").Position,
                "An in-range target remains the current attack target after the attack lock ends, regardless of its block capacity.");
        }

        [Test]
        public void SimultaneousLethalDamage_ProducesUnresolvedMutualAnnihilation()
        {
            var input = CreateInput(20, new[] { Definition("glass", 100, 100, 100, 1, 1) },
                new[] { Unit("home", "glass", 4, 4) }, new[] { Unit("away", "glass", 6, 4) });
            var result = new BattleRunner(input).RunToCompletion();
            Assert.IsFalse(result.IsResolved);
            Assert.IsNull(result.Winner);
            Assert.AreEqual(BattleStopReason.MutualAnnihilation, result.StopReason);
            Assert.AreEqual(2, result.Events.Count(item => item.Type == BattleEventType.Death));
        }

        [Test]
        public void RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources()
        {
            var first = UnitCatalogLoader.LoadFromResources(CatalogPath);
            var second = UnitCatalogLoader.LoadFromResources(CatalogPath);
            Assert.IsTrue(first.Success, Errors(first.Errors));
            Assert.IsTrue(second.Success, Errors(second.Errors));
            Assert.AreEqual(first.Catalog.CanonicalSummary, second.Catalog.CanonicalSummary);
            CollectionAssert.AreEqual(new[] { "1000", "5503" }, first.Catalog.Entries.Select(entry => entry.Definition.TypeId).ToArray());

            Assert.IsTrue(first.Catalog.TryGet("1000", out var gopro));
            Assert.AreEqual(190, gopro.Definition.MoveSpeedCentimetresPerSecond);
            Assert.AreEqual(28, gopro.Definition.AttackIntervalTicks);
            Assert.AreEqual(20, gopro.Definition.AttackAnimationDurationTicks);
            Assert.AreEqual(DamageType.Physical, gopro.Definition.DamageType);
            Assert.AreEqual(AttackMethod.Melee, gopro.Definition.AttackMethod);
            Assert.AreEqual(1, gopro.Definition.BlockCapacity);
            Assert.AreEqual(0, gopro.Definition.TauntLevel);
            Assert.AreEqual("Characters/gopro/enemy_1000_gopro_3_SkeletonData", gopro.SkeletonDataResourcePath);

            Assert.IsTrue(first.Catalog.TryGet("5503", out var arcslma));
            Assert.AreEqual(20, arcslma.Definition.MoveSpeedCentimetresPerSecond);
            Assert.AreEqual(80, arcslma.Definition.AttackIntervalTicks);
            Assert.AreEqual(54, arcslma.Definition.AttackAnimationDurationTicks);
            Assert.AreEqual("Move", arcslma.MoveAnimation);
            Assert.AreEqual("Attack", arcslma.AttackAnimation);
            Assert.AreEqual("Die", arcslma.DeathAnimation);
            Assert.IsFalse(arcslma.Definition.IsSyntheticFixtureData);

            var sourceGopro = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "GameData/Units/Json/gopro.json"));
            var sourceArcslma = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "GameData/Units/Json/arcslma.json"));
            StringAssert.Contains("\"schemaVersion\": \"unit-source-v1\"", sourceGopro);
            StringAssert.Contains("\"resourceKey\": \"gopro\"", sourceGopro);
            StringAssert.Contains("\"displayNameZhHans\": \"狂暴的猎狗pro\"", sourceGopro);
            StringAssert.Contains("\"skillDescriptionZhHans\": \"\"", sourceArcslma);
            StringAssert.Contains("\"attackAnimationDurationSeconds\": 1.0", sourceGopro);
            StringAssert.Contains("\"attackAnimationDurationSeconds\": 2.666667", sourceArcslma);
            StringAssert.Contains("\"damageType\": \"Physical\"", sourceGopro);
            StringAssert.Contains("\"blockCapacity\": 1", sourceArcslma);
            StringAssert.Contains("\"tauntLevel\": 0", sourceGopro);
            StringAssert.Contains("\"lifeDeduct\": 1", sourceArcslma);
            StringAssert.Contains("\"innateAbilityIds\": [", sourceArcslma);
            StringAssert.DoesNotContain("\"uintName\"", sourceGopro);
            StringAssert.DoesNotContain("\"HP\"", sourceGopro);
            StringAssert.DoesNotContain("\"FixedAbility\"", sourceArcslma);

        }

        [Test]
        public void LocalBattle_UsesCatalogDefinitionsAndTenRunsAreIdentical()
        {
            var loaded = LocalBattleLoader.LoadFromResources(CatalogPath, RealBattlePath);
            Assert.IsTrue(loaded.Success, Errors(loaded.Errors));
            Assert.AreEqual(BattleInput.LocalBattleSchemaVersion, loaded.Input.SchemaVersion);
            Assert.AreEqual(2, loaded.Input.Players.Count);
            Assert.AreEqual(2, loaded.Input.UnitDefinitions.Count);
            CollectionAssert.AreEquivalent(
                new[] { "home-1000-alpha", "home-5503-alpha", "home-1000-bravo", "away-5503-alpha", "away-1000-alpha", "away-5503-bravo", "away-1000-bravo" },
                loaded.Input.Players.SelectMany(player => player.Units).Select(unit => unit.UnitId).ToArray());
            CollectionAssert.AreEquivalent(new[] { "1000", "5503" }, loaded.Input.Players[0].Units.Select(unit => unit.TypeId).Distinct().ToArray());
            CollectionAssert.AreEquivalent(new[] { "1000", "5503" }, loaded.Input.Players[1].Units.Select(unit => unit.TypeId).Distinct().ToArray());
            Assert.AreEqual(new BattlefieldCoordinate(6, 7), BattlefieldRules.MapAway(new FormationCoordinate(4, 2)));

            string expected = null;
            for (var run = 0; run < 10; run++)
            {
                var result = new BattleRunner(loaded.Input).RunToCompletion();
                var summary = loaded.Input.CanonicalSummary + "|" + result.StableSummary + "|" + string.Join(";", result.Events.Select(EventSummary));
                if (expected == null) expected = summary;
                else Assert.AreEqual(expected, summary);
                Assert.IsTrue(result.IsResolved);
                Assert.AreEqual(BattleStopReason.Victory, result.StopReason);
                Assert.AreEqual(BattleSide.Away, result.Winner);
                Assert.AreEqual(7, result.FinalUnits.Count);
            }
        }

        [Test]
        public void RealCatalog_ArcslmaDamageArrivesOnlyAfterItsFullEffectiveAnimationDuration()
        {
            var loaded = LocalBattleLoader.LoadFromResources(CatalogPath, RealBattlePath);
            Assert.IsTrue(loaded.Success, Errors(loaded.Errors));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var arcslmaUnitIds = loaded.Input.Players.SelectMany(player => player.Units)
                .Where(unit => unit.TypeId == "5503")
                .Select(unit => unit.UnitId)
                .ToArray();
            var arcslmaAttacks = result.Events.Where(item => item.Type == BattleEventType.Attack && arcslmaUnitIds.Contains(item.UnitId)).ToArray();
            Assert.That(arcslmaAttacks.Length, Is.GreaterThan(0));

            foreach (var attack in arcslmaAttacks)
            {
                Assert.AreEqual(54, attack.OriginalAnimationTicks);
                Assert.AreEqual(54, attack.EffectiveAnimationTicks);
                Assert.AreEqual(attack.Tick + attack.EffectiveAnimationTicks, attack.PlannedDamageTick);
                var matchingDamage = result.Events.Where(item => item.Type == BattleEventType.Damage && item.UnitId == attack.UnitId && item.RelatedUnitId == attack.RelatedUnitId && item.Tick == attack.PlannedDamageTick).ToArray();
                Assert.That(matchingDamage.Length, Is.LessThanOrEqualTo(1));
            }

            var arcslmaDamage = result.Events.Where(item => item.Type == BattleEventType.Damage && arcslmaUnitIds.Contains(item.UnitId)).ToArray();
            Assert.That(arcslmaDamage.Length, Is.GreaterThan(0));
            foreach (var damage in arcslmaDamage)
            {
                Assert.That(arcslmaAttacks, Has.Some.Matches<BattleEvent>(attack =>
                    attack.UnitId == damage.UnitId
                    && attack.RelatedUnitId == damage.RelatedUnitId
                    && attack.PlannedDamageTick == damage.Tick));
            }
        }

        [Test]
        public void RealBattle_BlockedAwayArcslmaAttacksItsGoproBlocker()
        {
            var loaded = LocalBattleLoader.LoadFromResources(CatalogPath, RealBattlePath);
            Assert.IsTrue(loaded.Success, Errors(loaded.Errors));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            const string arcslmaUnitId = "away-5503-alpha";
            var block = result.Events.First(item =>
                item.Type == BattleEventType.BlockStarted &&
                (item.UnitId == arcslmaUnitId || item.RelatedUnitId == arcslmaUnitId));
            var goproBlockerId = block.UnitId == arcslmaUnitId ? block.RelatedUnitId : block.UnitId;

            Assert.That(loaded.Input.Players.SelectMany(player => player.Units), Has.Some.Matches<UnitSnapshot>(item => item.UnitId == goproBlockerId && item.TypeId == "1000"));
            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Attack &&
                item.UnitId == arcslmaUnitId &&
                item.RelatedUnitId == goproBlockerId &&
                item.Tick >= block.Tick));
        }

        [Test]
        public void LocalBattle_UnknownCatalogTypeReturnsStructuredContext()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var json = UnityEngine.Resources.Load<UnityEngine.TextAsset>(RealBattlePath).text.Replace("\"typeId\": \"5503\"", "\"typeId\": \"missing-type\"");
            var loaded = LocalBattleLoader.LoadFromJson(catalog, json);
            Assert.IsFalse(loaded.Success);
            var unknownTypeErrors = loaded.Errors.Where(error => error.Code == "localBattle.unit.type.unknown").ToArray();
            Assert.That(unknownTypeErrors.Length, Is.GreaterThan(0));
            foreach (var error in unknownTypeErrors)
                StringAssert.Contains("battleId=task004a-real-1v1", error.Message);
        }

        [Test]
        public void RealCatalog_MissingPresentationResourceReturnsStructuredError()
        {
            var json = UnityEngine.Resources.Load<UnityEngine.TextAsset>(CatalogPath).text.Replace("Characters/gopro/enemy_1000_gopro_3_SkeletonData", "Characters/missing/not-present");
            var loaded = UnitCatalogLoader.LoadFromJson(json);
            Assert.IsFalse(loaded.Success);
            Assert.That(loaded.Errors.Select(error => error.Code), Does.Contain("catalog.skeleton.resource.missing"));
        }

        [Test]
        public void EliteMetadata_ChangesInputDigestButNotCombatResult()
        {
            var constructor = typeof(UnitSnapshot).GetConstructor(new[]
            {
                typeof(string), typeof(string), typeof(UnitZone), typeof(FormationCoordinate?), typeof(System.Collections.Generic.IEnumerable<BuffPlaceholder>), typeof(int)
            });
            Assert.NotNull(constructor, "UnitSnapshot must preserve an explicit instance elite level.");

            var zero = (UnitSnapshot)constructor.Invoke(new object[] { "home", "unit", UnitZone.Deployed, new FormationCoordinate(4, 2), Array.Empty<BuffPlaceholder>(), 0 });
            var three = (UnitSnapshot)constructor.Invoke(new object[] { "home", "unit", UnitZone.Deployed, new FormationCoordinate(4, 2), Array.Empty<BuffPlaceholder>(), 3 });
            var away = Unit("away", "unit", 4, 3);
            var definitions = new[] { Definition("unit", 100) };
            var eliteZero = CreateInput(200, definitions, new[] { zero }, new[] { away });
            var eliteThree = CreateInput(200, definitions, new[] { three }, new[] { away });

            Assert.AreNotEqual(eliteZero.CanonicalSummary, eliteThree.CanonicalSummary);
            var zeroResult = new BattleRunner(eliteZero).RunToCompletion();
            var threeResult = new BattleRunner(eliteThree).RunToCompletion();
            Assert.AreEqual(zeroResult.CompletedTicks, threeResult.CompletedTicks);
            Assert.AreEqual(zeroResult.StopReason, threeResult.StopReason);
            Assert.AreEqual(zeroResult.Winner, threeResult.Winner);
            CollectionAssert.AreEqual(zeroResult.Events.Select(EventSummary), threeResult.Events.Select(EventSummary));
        }

        private static UnitDefinition Definition(string typeId, int speed, int hitPoints = 1000, int attack = 1, int interval = 20, int animation = 1, int capacity = 1, int tauntLevel = 0)
            => new UnitDefinition(typeId, hitPoints, attack, 0, 0, speed, interval, animation, DamageType.Physical, AttackMethod.Melee, capacity, tauntLevel, true);

        private static UnitSnapshot Unit(string id, string typeId, int x, int y)
            => new UnitSnapshot(id, typeId, UnitZone.Deployed, new FormationCoordinate(x, y), Array.Empty<BuffPlaceholder>());

        private static BattleInput CreateInput(int maxTicks, UnitDefinition[] definitions, UnitSnapshot[] homeUnits, UnitSnapshot[] awayUnits)
        {
            var specification = new BattleInputSpecification(BattleInput.SupportedSchemaVersion, "test-battle", maxTicks, definitions,
                new[] { new PlayerSnapshot("home", BattleSide.Home, homeUnits), new PlayerSnapshot("away", BattleSide.Away, awayUnits) });
            Assert.IsTrue(BattleInputFactory.TryCreate(specification, out var input, out var errors), string.Join(";", errors.Select(item => item.ToString())));
            return input;
        }

        private static string EventSummary(BattleEvent item) => item.Type + "," + item.Tick + "," + item.Sequence + "," + item.UnitId + "," + item.RelatedUnitId + "," + item.DamageAmount + "," + item.HitPointsBefore + "," + item.HitPointsAfter + "," + item.PlannedDamageTick + "," + item.OriginalAnimationTicks + "," + item.EffectiveAnimationTicks + "," + item.Winner + "," + item.Reason;

        private static string Errors(BattleFixtureLoadResult result) => string.Join("; ", result.Errors.Select(error => error.ToString()));
        private static string Errors(System.Collections.Generic.IReadOnlyList<ValidationError> errors) => string.Join("; ", errors.Select(error => error.ToString()));
    }
}
