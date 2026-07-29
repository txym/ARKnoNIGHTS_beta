using System;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class BondsTargetingEditModeTests
    {
        [Test]
        public void UntargetableTrait_ExcludesCloserEnemyFromNormalAcquisition()
        {
            var input = CreateInput(
                2,
                new[]
                {
                    Attacker("home-attacker"),
                    NonAttacker("drone", 100, 0, "UNTARGETABLE"),
                    Attacker("away-normal")
                },
                new[] { UntargetableAbility() },
                new[] { Unit("home", "home-attacker", 5, 4) },
                new[]
                {
                    Unit("away-drone", "drone", 5, 4),
                    Unit("away-normal", "away-normal", 9, 4)
                });

            var result = new BattleRunner(input).RunToCompletion();

            var acquisition = result.Events.Single(item =>
                item.Type == BattleEventType.TargetChanged
                && item.UnitId == "home"
                && item.RelatedUnitId != null);
            Assert.That(acquisition.RelatedUnitId, Is.EqualTo("away-normal"));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.TargetChanged
                && item.UnitId == "home"
                && item.RelatedUnitId == "away-drone"));
        }

        [Test]
        public void DroneTrait_ExcludesMeleeAcquisitionButAllowsRangedAcquisition()
        {
            var input = CreateInput(
                2,
                new[]
                {
                    Attacker("melee"),
                    Attacker("ranged", 0, AttackMethod.Ranged),
                    NonAttacker(
                        "drone",
                        0,
                        0,
                        "UNTARGETABLE_BY_MELEE",
                        4),
                    Attacker("away-normal")
                },
                new[] { UntargetableByMeleeAbility() },
                new[]
                {
                    Unit("home-melee", "melee", 4, 4),
                    Unit("home-ranged", "ranged", 6, 4)
                },
                new[]
                {
                    Unit("away-drone", "drone", 5, 4),
                    Unit("away-normal", "away-normal", 9, 4)
                });

            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.TargetChanged
                && item.UnitId == "home-melee"
                && item.RelatedUnitId == "away-normal"));
            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.TargetChanged
                && item.UnitId == "home-ranged"
                && item.RelatedUnitId == "away-drone"));
        }

        [Test]
        public void NonAttacker_NeverAcquiresOrStartsAnAttack()
        {
            var input = CreateInput(
                10,
                new[]
                {
                    NonAttacker("ghost", 100, 0),
                    Attacker("enemy")
                },
                Array.Empty<AbilityDefinition>(),
                new[] { Unit("ghost", "ghost", 5, 4) },
                new[] { Unit("enemy", "enemy", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.UnitId == "ghost"
                && item.Type == BattleEventType.TargetChanged
                && item.RelatedUnitId != null));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.UnitId == "ghost"
                && item.Type == BattleEventType.Attack));
            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item =>
                item.UnitId == "enemy"
                && item.Type == BattleEventType.TargetChanged
                && item.RelatedUnitId == "ghost"));
        }

        [Test]
        public void ZeroBlockCapacity_PreventsSymmetricBlocking()
        {
            var input = CreateInput(
                30,
                new[]
                {
                    NonAttacker("unblockable", 0, 0, null, 4),
                    Attacker("enemy", 200)
                },
                Array.Empty<AbilityDefinition>(),
                new[] { Unit("unblockable", "unblockable", 5, 4) },
                new[] { Unit("enemy", "enemy", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.BlockStarted
                && (item.UnitId == "unblockable"
                    || item.RelatedUnitId == "unblockable")));
        }

        [Test]
        public void MobileNonAttacker_MovesDirectlyTowardsOpposingGateWithoutTargeting()
        {
            var input = CreateInput(
                2,
                new[]
                {
                    NonAttacker("runner", 200, 0),
                    NonAttacker("marker", 0, 0, null, 4)
                },
                Array.Empty<AbilityDefinition>(),
                new[] { Unit("runner", "runner", 5, 4) },
                new[] { Unit("marker", "marker", 9, 4) });
            var runner = new BattleRunner(input);

            var result = runner.RunToCompletion();

            var finalRunner = runner.RuntimeUnits.Single(item => item.UnitId == "runner");
            Assert.That(finalRunner.Position.XUnits, Is.EqualTo(500));
            Assert.That(finalRunner.Position.YUnits, Is.GreaterThan(400));
            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Move
                && item.UnitId == "runner"
                && item.RelatedUnitId == null));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.UnitId == "runner"
                && item.Type == BattleEventType.TargetChanged
                && item.RelatedUnitId != null));
        }

        [Test]
        public void StationaryActionMethod_DoesNotMove()
        {
            var input = CreateInput(
                2,
                new[]
                {
                    NonAttacker("stationary", 0, 0, null, 4),
                    NonAttacker("other", 0, 0, null, 4)
                },
                Array.Empty<AbilityDefinition>(),
                new[] { Unit("stationary", "stationary", 5, 4) },
                new[] { Unit("other", "other", 9, 4) });
            var runner = new BattleRunner(input);

            var result = runner.RunToCompletion();

            Assert.That(
                runner.RuntimeUnits.Single(item => item.UnitId == "stationary").Position,
                Is.EqualTo(FixedPosition.FromCell(new BattlefieldCoordinate(5, 4))));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Move
                && item.UnitId == "stationary"));
        }

        [Test]
        public void NoEligibleTarget_ReachesGateBoundaryExitsAndSettlesLifeLoss()
        {
            var input = CreateInput(
                100,
                new[]
                {
                    Attacker("runner", 100, AttackMethod.Melee, 3),
                    NonAttacker(
                        "drone",
                        0,
                        0,
                        "UNTARGETABLE_BY_MELEE",
                        4,
                        0)
                },
                new[] { UntargetableByMeleeAbility() },
                new[] { Unit("runner", "runner", 5, 4) },
                new[] { Unit("drone", "drone", 5, 4) });

            var runner = new BattleRunner(input);
            var result = runner.RunToCompletion();

            var reached = result.Events.Single(item =>
                item.Type == BattleEventType.GateReached
                && item.UnitId == "runner");
            Assert.That(reached.Tick, Is.EqualTo(72));
            Assert.That(reached.ToPosition.Value, Is.EqualTo(
                new FixedPosition(500, 760)));
            Assert.That(reached.DamageAmount, Is.EqualTo(3));
            Assert.That(result.HomeLifeLoss, Is.Zero);
            Assert.That(result.AwayLifeLoss, Is.EqualTo(3));
            Assert.That(result.Winner, Is.EqualTo(BattleSide.Home));
            Assert.That(
                result.FinalUnits.Single(item => item.UnitId == "runner")
                    .HasExitedBattle,
                Is.True);

            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(
                compiler.TryCompile(
                    result,
                    out var track,
                    out var diagnostics),
                Is.True,
                string.Join("; ", diagnostics));
            Assert.That(
                track.TryGetUnit("runner", out var runnerTrack),
                Is.True);
            Assert.That(runnerTrack.Sample(71).ShouldDisplay, Is.True);
            Assert.That(runnerTrack.Sample(72).HasExitedBattle, Is.True);
            Assert.That(runnerTrack.Sample(72).ShouldDisplay, Is.False);
        }

        private static UnitDefinition Attacker(
            string typeId,
            int speed = 0,
            AttackMethod attackMethod = AttackMethod.Melee,
            int lifeDeduct = 1)
        {
            return new UnitDefinition(
                typeId,
                1000,
                10,
                0,
                0,
                speed,
                20,
                1,
                DamageType.Physical,
                attackMethod,
                1,
                0,
                true,
                Array.Empty<string>(),
                1,
                lifeDeduct);
        }

        private static UnitDefinition NonAttacker(
            string typeId,
            int speed,
            int blockCapacity,
            string abilityId = null,
            int actionMethod = 1,
            int lifeDeduct = 1)
        {
            return new UnitDefinition(
                typeId,
                1000,
                0,
                0,
                0,
                speed,
                0,
                0,
                DamageType.None,
                AttackMethod.None,
                blockCapacity,
                0,
                true,
                abilityId == null ? Array.Empty<string>() : new[] { abilityId },
                actionMethod,
                lifeDeduct);
        }

        private static AbilityDefinition UntargetableAbility()
        {
            return new AbilityDefinition(
                "UNTARGETABLE",
                string.Empty,
                "不会成为敌方单位的攻击目标。",
                AbilityActivationKind.Passive,
                SilencePolicy.Unaffected,
                0,
                0,
                SkillPointGeneration.None,
                null,
                new UnitTraitEffectDefinition(UnitTraitEffectKind.Untargetable));
        }

        private static AbilityDefinition UntargetableByMeleeAbility()
        {
            return new AbilityDefinition(
                "UNTARGETABLE_BY_MELEE",
                string.Empty,
                "不会成为近战单位的攻击目标。",
                AbilityActivationKind.Passive,
                SilencePolicy.Unaffected,
                0,
                0,
                SkillPointGeneration.None,
                null,
                new UnitTraitEffectDefinition(
                    UnitTraitEffectKind.UntargetableByMelee));
        }

        private static UnitSnapshot Unit(
            string unitId,
            string typeId,
            int x,
            int y)
        {
            return new UnitSnapshot(
                unitId,
                typeId,
                UnitZone.Deployed,
                new FormationCoordinate(x, y),
                Array.Empty<BuffPlaceholder>());
        }

        private static BattleInput CreateInput(
            int maxTicks,
            UnitDefinition[] definitions,
            AbilityDefinition[] abilities,
            UnitSnapshot[] homeUnits,
            UnitSnapshot[] awayUnits)
        {
            var specification = new BattleInputSpecification(
                BattleInput.SupportedSchemaVersion,
                "bonds-targeting",
                maxTicks,
                definitions,
                abilities,
                new[]
                {
                    new PlayerSnapshot("home", BattleSide.Home, homeUnits),
                    new PlayerSnapshot("away", BattleSide.Away, awayUnits)
                });
            Assert.That(
                BattleInputFactory.TryCreate(
                    specification,
                    out var input,
                    out var errors),
                Is.True,
                string.Join("; ", errors.Select(error => error.ToString())));
            return input;
        }
    }
}
