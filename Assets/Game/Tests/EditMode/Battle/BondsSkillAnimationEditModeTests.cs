using System;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class BondsSkillAnimationEditModeTests
    {
        [Test]
        public void TimedSkill_UsesRoundedHalfDurationAndTrackPresentsAtDoubleSpeed()
        {
            var input = CreateInput(
                "skill-double-speed",
                117,
                Caster(1000),
                Enemy(0),
                Unit("caster", "caster", 5, 2),
                Unit("enemy", "enemy", 5, 2));

            var result = new BattleRunner(input).RunToCompletion();

            var skill = result.Events.Single(item =>
                item.Type == BattleEventType.Skill);
            Assert.That(skill.Tick, Is.EqualTo(100));
            Assert.That(skill.AnimationKey, Is.EqualTo("skill"));
            Assert.That(skill.OriginalAnimationTicks, Is.EqualTo(30));
            Assert.That(skill.EffectiveAnimationTicks, Is.EqualTo(15));

            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(
                compiler.TryCompile(result, out var track, out var diagnostics),
                Is.True,
                string.Join("; ", diagnostics.Select(item => item.ToString())));
            Assert.That(track.TryGetUnit("caster", out var casterTrack), Is.True);
            var start = casterTrack.Sample(100);
            Assert.That(start.Action, Is.EqualTo(UnitPresentationAction.Skill));
            Assert.That(start.AnimationKey, Is.EqualTo("skill"));
            Assert.That(start.AnimationSpeedMultiplier, Is.EqualTo(2f));
            Assert.That(casterTrack.Sample(115).Action, Is.EqualTo(UnitPresentationAction.Skill));
            Assert.That(casterTrack.Sample(116).Action, Is.EqualTo(UnitPresentationAction.Idle));
        }

        [Test]
        public void ReadySkill_WaitsForAlreadyStartedAttackToFinish()
        {
            var input = CreateInput(
                "skill-waits-for-attack",
                130,
                Caster(92, 100),
                Enemy(100),
                Unit("caster", "caster", 5, 4),
                Unit("enemy", "enemy", 5, 4));

            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Attack
                && item.UnitId == "caster"
                && item.Tick == 100
                && item.PlannedDamageTick == 120));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Skill
                && item.UnitId == "caster"
                && item.Tick <= 120));
            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Skill
                && item.UnitId == "caster"
                && item.Tick == 121));
        }

        [Test]
        public void OddSkillDuration_RoundsUpAfterHalving()
        {
            var animation = Ability(31);

            Assert.That(
                animation.SkillAnimationEffectiveDurationTicks,
                Is.EqualTo(16));
        }

        [Test]
        public void RepairTerminal_SummonsAtCasterCentreAndMinionTargetsNormallyNextTick()
        {
            const string abilityId = "SUMMON_REPAIR_HELPER";
            var terminal = new UnitDefinition(
                "10077",
                100000,
                1,
                0,
                0,
                0,
                1000,
                1,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true,
                new[] { abilityId },
                1);
            var helper = new UnitDefinition(
                "10073",
                1000,
                1,
                0,
                0,
                100,
                1000,
                1,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true);
            var ability = new AbilityDefinition(
                abilityId,
                string.Empty,
                string.Empty,
                AbilityActivationKind.Timed,
                SilencePolicy.Unaffected,
                3,
                5,
                SkillPointGeneration.Automatic,
                new SummonEffectDefinition("10073", 1, 0, false),
                null,
                "skill",
                50);
            var specification = new BattleInputSpecification(
                BattleInput.SupportedSchemaVersion,
                "repair-terminal-centre-summon",
                22,
                new[] { terminal, helper, Enemy(0) },
                new[] { ability },
                new[]
                {
                    new PlayerSnapshot(
                        "home",
                        BattleSide.Home,
                        new[]
                        {
                            Unit(
                                "terminal",
                                "10077",
                                5,
                                2,
                                2)
                        }),
                    new PlayerSnapshot(
                        "away",
                        BattleSide.Away,
                        new[] { Unit("enemy", "enemy", 5, 4) })
                });
            Assert.That(
                BattleInputFactory.TryCreate(
                    specification,
                    out var input,
                    out var errors),
                Is.True,
                string.Join("; ", errors.Select(item => item.ToString())));

            var result = new BattleRunner(input).RunToCompletion();

            var skill = result.Events.Single(item =>
                item.Type == BattleEventType.Skill
                && item.UnitId == "terminal");
            Assert.That(skill.Tick, Is.EqualTo(20));
            Assert.That(skill.OriginalAnimationTicks, Is.EqualTo(50));
            Assert.That(skill.EffectiveAnimationTicks, Is.EqualTo(25));
            var spawn = result.Events.Single(item =>
                item.Type == BattleEventType.Spawn
                && item.UnitTypeId == "10073");
            Assert.That(spawn.Tick, Is.EqualTo(20));
            Assert.That(
                spawn.ToPosition.Value,
                Is.EqualTo(FixedPosition.FromCell(
                    new BattlefieldCoordinate(5, 2))));
            Assert.That(spawn.SpawnSnapshot.ActivationTick, Is.EqualTo(21));
            Assert.That(spawn.SpawnSnapshot.EliteLevel, Is.EqualTo(2));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.TargetChanged
                && item.UnitId == spawn.UnitId
                && item.Tick == 20));
            Assert.That(result.Events, Has.Some.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.TargetChanged
                && item.UnitId == spawn.UnitId
                && item.RelatedUnitId == "enemy"
                && item.Tick == 21));
        }

        [Test]
        public void ChargedDrink_WaitsForFullDoubleSpeedSkillThenDamagesGroundTargetsInRadius()
        {
            const string abilityId = "CHARGED_DRINK_AREA_ATTACK";
            const string droneTraitId = "UNTARGETABLE_BY_MELEE";
            var caster = new UnitDefinition(
                "10039",
                22000,
                900,
                900,
                20,
                0,
                100,
                30,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true,
                new[] { abilityId },
                1);
            var ground = new UnitDefinition(
                "ground",
                10000,
                1,
                0,
                0,
                0,
                1000,
                1,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true);
            var drone = new UnitDefinition(
                "drone",
                10000,
                1,
                0,
                0,
                0,
                1000,
                1,
                DamageType.Physical,
                AttackMethod.Ranged,
                0,
                0,
                true,
                new[] { droneTraitId },
                1);
            var chargedArea = new AbilityDefinition(
                abilityId,
                string.Empty,
                string.Empty,
                AbilityActivationKind.Timed,
                SilencePolicy.Unaffected,
                20,
                30,
                SkillPointGeneration.Automatic,
                null,
                null,
                null,
                null,
                new TimedTargetAreaDamageEffectDefinition(
                    220,
                    150,
                    DamageType.Physical,
                    1000,
                    true),
                "skill",
                57);
            var droneTrait = new AbilityDefinition(
                droneTraitId,
                string.Empty,
                string.Empty,
                AbilityActivationKind.Passive,
                SilencePolicy.Unaffected,
                0,
                0,
                SkillPointGeneration.None,
                null,
                new UnitTraitEffectDefinition(
                    UnitTraitEffectKind.UntargetableByMelee));
            var specification = new BattleInputSpecification(
                BattleInput.SupportedSchemaVersion,
                "charged-drink-area",
                130,
                new[] { caster, ground, drone },
                new[] { chargedArea, droneTrait },
                new[]
                {
                    new PlayerSnapshot(
                        "home",
                        BattleSide.Home,
                        new[] { Unit("caster", "10039", 5, 4) }),
                    new PlayerSnapshot(
                        "away",
                        BattleSide.Away,
                        new[]
                        {
                            Unit("target", "ground", 5, 4),
                            Unit("inside", "ground", 4, 4),
                            Unit("outside", "ground", 3, 4),
                            Unit("drone", "drone", 6, 4)
                        })
                });
            Assert.That(
                BattleInputFactory.TryCreate(
                    specification,
                    out var input,
                    out var errors),
                Is.True,
                string.Join("; ", errors.Select(item => item.ToString())));

            var result = new BattleRunner(input).RunToCompletion();

            var skill = result.Events.Single(item =>
                item.Type == BattleEventType.Skill
                && item.UnitId == "caster");
            Assert.That(skill.Tick, Is.EqualTo(100));
            Assert.That(skill.RelatedUnitId, Is.EqualTo("target"));
            Assert.That(skill.AnimationKey, Is.EqualTo("skill"));
            Assert.That(skill.OriginalAnimationTicks, Is.EqualTo(57));
            Assert.That(skill.EffectiveAnimationTicks, Is.EqualTo(29));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Damage
                && item.UnitId == "caster"
                && item.Tick < 129));
            var damage = result.Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.UnitId == "caster")
                .ToArray();
            Assert.That(
                damage.Select(item => item.Tick),
                Is.EqualTo(new[] { 129, 129 }));
            Assert.That(
                damage.Select(item => item.RelatedUnitId),
                Is.EqualTo(new[] { "inside", "target" }));
            Assert.That(
                damage.Select(item => item.DamageAmount),
                Is.EqualTo(new[] { 900, 900 }));
            Assert.That(damage, Has.None.Matches<BattleEvent>(item =>
                item.RelatedUnitId == "outside"
                || item.RelatedUnitId == "drone"));
        }

        private static UnitDefinition Caster(
            int attackIntervalTicks,
            int moveSpeed = 0)
        {
            return new UnitDefinition(
                "caster",
                100000,
                1,
                0,
                0,
                moveSpeed,
                attackIntervalTicks,
                20,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true,
                new[] { "SUMMON_JELLY_MINIONS" },
                1);
        }

        private static UnitDefinition Enemy(int moveSpeed)
        {
            return new UnitDefinition(
                "enemy",
                100000,
                1,
                0,
                0,
                moveSpeed,
                1000,
                1,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true);
        }

        private static UnitDefinition Minion()
        {
            return new UnitDefinition(
                "5504",
                1000,
                1,
                0,
                0,
                0,
                1000,
                1,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true);
        }

        private static AbilityDefinition Ability(
            int skillAnimationTicks = 30)
        {
            return new AbilityDefinition(
                "SUMMON_JELLY_MINIONS",
                string.Empty,
                string.Empty,
                AbilityActivationKind.Timed,
                SilencePolicy.Unaffected,
                5,
                15,
                SkillPointGeneration.Automatic,
                new SummonEffectDefinition("5504", 3, 100, false),
                null,
                "skill",
                skillAnimationTicks);
        }

        private static UnitSnapshot Unit(
            string id,
            string typeId,
            int x,
            int y,
            int eliteLevel = 0)
        {
            return new UnitSnapshot(
                id,
                typeId,
                UnitZone.Deployed,
                new FormationCoordinate(x, y),
                Array.Empty<BuffPlaceholder>(),
                eliteLevel);
        }

        private static BattleInput CreateInput(
            string battleId,
            int maxTicks,
            UnitDefinition caster,
            UnitDefinition enemy,
            UnitSnapshot home,
            UnitSnapshot away)
        {
            var specification = new BattleInputSpecification(
                BattleInput.SupportedSchemaVersion,
                battleId,
                maxTicks,
                new[] { caster, enemy, Minion() },
                new[] { Ability() },
                new[]
                {
                    new PlayerSnapshot(
                        "home",
                        BattleSide.Home,
                        new[] { home }),
                    new PlayerSnapshot(
                        "away",
                        BattleSide.Away,
                        new[] { away })
                });
            Assert.That(
                BattleInputFactory.TryCreate(
                    specification,
                    out var input,
                    out var errors),
                Is.True,
                string.Join("; ", errors.Select(item => item.ToString())));
            return input;
        }
    }
}
