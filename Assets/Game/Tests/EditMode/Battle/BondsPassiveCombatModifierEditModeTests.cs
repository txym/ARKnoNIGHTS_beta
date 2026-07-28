using System;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class BondsPassiveCombatModifierEditModeTests
    {
        [Test]
        public void BlockCapacityAdditive_AllowsThreeSimultaneousBlocks()
        {
            var modifier = Modifier(blockCapacityAdditive: 2);
            var input = CreateInput(
                2,
                new[]
                {
                    Attacker(
                        "blocker",
                        0,
                        1,
                        "BLOCK_PLUS_TWO"),
                    Attacker("enemy", 2000, 1)
                },
                new[] { Passive("BLOCK_PLUS_TWO", modifier) },
                new[] { Unit("blocker", "blocker", 5, 4) },
                new[]
                {
                    Unit("enemy-a", "enemy", 5, 4),
                    Unit("enemy-b", "enemy", 6, 4),
                    Unit("enemy-c", "enemy", 4, 4)
                });

            var runner = new BattleRunner(input);
            runner.Step();
            runner.Step();

            var blocker = runner.RuntimeUnits.Single(item =>
                item.UnitId == "blocker");
            Assert.That(
                blocker.BlockedUnitIds,
                Is.EqualTo(new[]
                {
                    "enemy-a",
                    "enemy-b",
                    "enemy-c"
                }));
        }

        [Test]
        public void MagicResistanceAdditive_ReducesMagicDamage()
        {
            var damage = RunSingleDamage(
                DamageType.Magic,
                Modifier(magicResistanceAdditive: 70));

            Assert.That(damage.DamageAmount, Is.EqualTo(300));
        }

        [Test]
        public void DamageTakenModifiers_ReducePhysicalAndMagicButNotTrueDamage()
        {
            var modifier = Modifier(
                physicalDamageTakenPermille: 500,
                magicDamageTakenPermille: 500);

            Assert.That(
                RunSingleDamage(DamageType.Physical, modifier).DamageAmount,
                Is.EqualTo(500));
            Assert.That(
                RunSingleDamage(DamageType.Magic, modifier).DamageAmount,
                Is.EqualTo(500));
            Assert.That(
                RunSingleDamage(DamageType.True, modifier).DamageAmount,
                Is.EqualTo(1000));
        }

        [Test]
        public void AttackSpeedAdditive_RecomputesAttackIntervalWithCeiling()
        {
            var input = CreateInput(
                23,
                new[]
                {
                    Attacker(
                        "attacker",
                        2000,
                        0,
                        "ATTACK_SPEED_PLUS_100",
                        attackIntervalTicks: 20),
                    NonAttacker("target", 100000)
                },
                new[]
                {
                    Passive(
                        "ATTACK_SPEED_PLUS_100",
                        Modifier(attackSpeedAdditive: 100))
                },
                new[] { Unit("attacker", "attacker", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(
                result.Events
                    .Where(item =>
                        item.Type == BattleEventType.Attack
                        && item.UnitId == "attacker")
                    .Select(item => item.Tick)
                    .ToArray(),
                Is.EqualTo(new[] { 1, 11, 21 }));
        }

        [Test]
        public void PassiveSelfDamage_AccumulatesFractionalTicksExactly()
        {
            var input = CreateInput(
                20,
                new[]
                {
                    NonAttacker("decay", 1000, "DECAY_330"),
                    NonAttacker("target", 100000)
                },
                new[]
                {
                    PassiveLifecycle(
                        "DECAY_330",
                        hitPointsPerSecond: -330)
                },
                new[] { Unit("decay", "decay", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();
            var changes = result.Events
                .Where(item =>
                    item.Type == BattleEventType.HealthChanged
                    && item.UnitId == "decay")
                .ToArray();

            Assert.That(
                changes.Sum(item => item.DamageAmount),
                Is.EqualTo(330));
            Assert.That(
                changes.Last().HitPointsAfter,
                Is.EqualTo(670));
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(
                compiler.TryCompile(
                    result,
                    out var track,
                    out var diagnostics),
                Is.True,
                string.Join(
                    "; ",
                    diagnostics.Select(item => item.ToString())));
            Assert.That(
                track.TryGetUnit("decay", out var decayTrack),
                Is.True);
            Assert.That(
                decayTrack.Sample(20).CurrentHitPoints,
                Is.EqualTo(670));
        }

        [Test]
        public void PassiveRegeneration_HealsWithoutPlayingAHitEvent()
        {
            var input = CreateInput(
                20,
                new[]
                {
                    Attacker(
                        "attacker",
                        2000,
                        0,
                        attackIntervalTicks: 100),
                    NonAttacker(
                        "regenerator",
                        2000,
                        "REGEN_400")
                },
                new[]
                {
                    PassiveLifecycle(
                        "REGEN_400",
                        hitPointsPerSecond: 400)
                },
                new[] { Unit("attacker", "attacker", 5, 4) },
                new[] { Unit("regenerator", "regenerator", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(
                result.FinalUnits.Single(item =>
                    item.UnitId == "regenerator").HitPoints,
                Is.EqualTo(1380));
            Assert.That(
                result.Events,
                Has.Some.Matches<BattleEvent>(item =>
                    item.Type == BattleEventType.HealthChanged
                    && item.UnitId == "regenerator"
                    && item.HitPointsAfter > item.HitPointsBefore));
        }

        [Test]
        public void PassiveLifetime_ExpiresOnExactTick()
        {
            var input = CreateInput(
                130,
                new[]
                {
                    NonAttacker("temporary", 1000, "LIFETIME_120"),
                    NonAttacker("target", 100000)
                },
                new[]
                {
                    PassiveLifecycle(
                        "LIFETIME_120",
                        lifetimeTicks: 120)
                },
                new[] { Unit("temporary", "temporary", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(
                result.Events,
                Has.Some.Matches<BattleEvent>(item =>
                    item.Type == BattleEventType.HealthChanged
                    && item.UnitId == "temporary"
                    && item.Tick == 120
                    && item.HitPointsAfter == 0));
            Assert.That(
                result.Events,
                Has.Some.Matches<BattleEvent>(item =>
                    item.Type == BattleEventType.Death
                    && item.UnitId == "temporary"
                    && item.Tick == 120));
            Assert.That(result.Winner, Is.EqualTo(BattleSide.Away));
        }

        [Test]
        public void OnDamageReaction_DealsUnsourceMagicDamageEvenWhenOwnerDies()
        {
            var input = CreateInput(
                3,
                new[]
                {
                    Attacker(
                        "attacker",
                        2000,
                        0,
                        attackIntervalTicks: 100,
                        magicResistance: 50),
                    NonAttacker("reactor", 50, "REACTION_200_MAGIC")
                },
                new[]
                {
                    PassiveReaction(
                        "REACTION_200_MAGIC",
                        DamageType.Magic,
                        200)
                },
                new[] { Unit("attacker", "attacker", 5, 4) },
                new[] { Unit("reactor", "reactor", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();
            var reaction = result.Events.Single(item =>
                item.Type == BattleEventType.Damage
                && item.UnitId == "reactor"
                && item.RelatedUnitId == "attacker");

            Assert.That(reaction.DamageType, Is.EqualTo(DamageType.Magic));
            Assert.That(reaction.DamageAmount, Is.EqualTo(100));
            Assert.That(
                result.Events,
                Has.Some.Matches<BattleEvent>(item =>
                    item.Type == BattleEventType.Death
                    && item.UnitId == "reactor"
                    && item.Tick == reaction.Tick));
        }

        private static BattleEvent RunSingleDamage(
            DamageType damageType,
            PassiveCombatModifierDefinition modifier)
        {
            var input = CreateInput(
                3,
                new[]
                {
                    Attacker(
                        "attacker",
                        2000,
                        0,
                        null,
                        damageType,
                        100),
                    NonAttacker(
                        "target",
                        5000,
                        "DAMAGE_MODIFIER")
                },
                new[] { Passive("DAMAGE_MODIFIER", modifier) },
                new[] { Unit("attacker", "attacker", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            return new BattleRunner(input)
                .RunToCompletion()
                .Events
                .Single(item =>
                    item.Type == BattleEventType.Damage
                    && item.RelatedUnitId == "target");
        }

        private static PassiveCombatModifierDefinition Modifier(
            int blockCapacityAdditive = 0,
            int magicResistanceAdditive = 0,
            int attackSpeedAdditive = 0,
            int physicalDamageTakenPermille =
                PassiveCombatModifierDefinition
                    .NeutralDamageTakenPermille,
            int magicDamageTakenPermille =
                PassiveCombatModifierDefinition
                    .NeutralDamageTakenPermille)
        {
            return new PassiveCombatModifierDefinition(
                blockCapacityAdditive,
                magicResistanceAdditive,
                attackSpeedAdditive,
                physicalDamageTakenPermille,
                magicDamageTakenPermille);
        }

        private static AbilityDefinition Passive(
            string abilityId,
            PassiveCombatModifierDefinition modifier)
        {
            return new AbilityDefinition(
                abilityId,
                string.Empty,
                string.Empty,
                AbilityActivationKind.Passive,
                SilencePolicy.Unaffected,
                0,
                0,
                SkillPointGeneration.None,
                null,
                null,
                modifier,
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveLifecycle(
            string abilityId,
            int hitPointsPerSecond = 0,
            int lifetimeTicks = 0)
        {
            return new AbilityDefinition(
                abilityId,
                string.Empty,
                string.Empty,
                AbilityActivationKind.Passive,
                SilencePolicy.Unaffected,
                0,
                0,
                SkillPointGeneration.None,
                null,
                null,
                null,
                new PassiveLifecycleEffectDefinition(
                    hitPointsPerSecond,
                    lifetimeTicks),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveReaction(
            string abilityId,
            DamageType damageType,
            int damageAmount)
        {
            return new AbilityDefinition(
                abilityId,
                string.Empty,
                string.Empty,
                AbilityActivationKind.Passive,
                SilencePolicy.Unaffected,
                0,
                0,
                SkillPointGeneration.None,
                null,
                null,
                null,
                null,
                new OnDamageReactionEffectDefinition(
                    damageType,
                    damageAmount),
                string.Empty,
                0);
        }

        private static UnitDefinition Attacker(
            string typeId,
            int speed,
            int blockCapacity,
            string abilityId = null,
            DamageType damageType = DamageType.Physical,
            int attackIntervalTicks = 1000,
            int magicResistance = 0)
        {
            return new UnitDefinition(
                typeId,
                100000,
                1000,
                0,
                magicResistance,
                speed,
                attackIntervalTicks,
                1,
                damageType,
                AttackMethod.Melee,
                blockCapacity,
                0,
                true,
                abilityId == null
                    ? Array.Empty<string>()
                    : new[] { abilityId });
        }

        private static UnitDefinition NonAttacker(
            string typeId,
            int maxHitPoints,
            string abilityId = null)
        {
            return new UnitDefinition(
                typeId,
                maxHitPoints,
                0,
                0,
                0,
                0,
                0,
                0,
                DamageType.None,
                AttackMethod.None,
                0,
                0,
                true,
                abilityId == null
                    ? Array.Empty<string>()
                    : new[] { abilityId },
                4);
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
                "bonds-passive-combat-modifier",
                maxTicks,
                definitions,
                abilities,
                new[]
                {
                    new PlayerSnapshot(
                        "home",
                        BattleSide.Home,
                        homeUnits),
                    new PlayerSnapshot(
                        "away",
                        BattleSide.Away,
                        awayUnits)
                });
            Assert.That(
                BattleInputFactory.TryCreate(
                    specification,
                    out var input,
                    out var errors),
                Is.True,
                string.Join(
                    "; ",
                    errors.Select(item => item.ToString())));
            return input;
        }
    }
}
