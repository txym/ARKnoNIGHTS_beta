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
                Caster(30, 1000),
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
                Caster(30, 92, 100),
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
            var animation = new UnitSkillAnimationDefinition("skill", 31);

            Assert.That(animation.EffectiveDurationTicks, Is.EqualTo(16));
        }

        private static UnitDefinition Caster(
            int skillAnimationTicks,
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
                1,
                new[]
                {
                    new UnitSkillAnimationDefinition(
                        "skill",
                        skillAnimationTicks)
                });
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

        private static AbilityDefinition Ability()
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
                "skill");
        }

        private static UnitSnapshot Unit(
            string id,
            string typeId,
            int x,
            int y)
        {
            return new UnitSnapshot(
                id,
                typeId,
                UnitZone.Deployed,
                new FormationCoordinate(x, y),
                Array.Empty<BuffPlaceholder>());
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
