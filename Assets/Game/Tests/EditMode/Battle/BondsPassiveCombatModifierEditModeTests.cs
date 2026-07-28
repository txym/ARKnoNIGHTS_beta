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

        [Test]
        public void ContinuousHealthThreshold_DoublesLaterAttacksAtHalfHealth()
        {
            var input = CreateInput(
                7,
                new[]
                {
                    Attacker(
                        "threshold",
                        0,
                        0,
                        "LOW_HP_ATTACK",
                        attackIntervalTicks: 4,
                        maxHitPoints: 1000,
                        attack: 100),
                    Attacker(
                        "enemy",
                        2000,
                        0,
                        attackIntervalTicks: 100,
                        attack: 600)
                },
                new[]
                {
                    PassiveThreshold(
                        "LOW_HP_ATTACK",
                        thresholdHitPointsPermille: 500,
                        inclusiveThreshold: true,
                        triggerOnce: false,
                        attackMultiplierPermille: 2000)
                },
                new[] { Unit("threshold", "threshold", 5, 4) },
                new[] { Unit("enemy", "enemy", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(
                result.Events
                    .Where(item =>
                        item.Type == BattleEventType.Damage
                        && item.UnitId == "threshold"
                        && item.RelatedUnitId == "enemy")
                    .Select(item => item.DamageAmount)
                    .ToArray(),
                Is.EqualTo(new[] { 100, 200 }));
        }

        [Test]
        public void ContinuousHealthThreshold_UpdatesDefenseAndBlockCapacity()
        {
            var input = CreateInput(
                3,
                new[]
                {
                    Attacker(
                        "threshold",
                        0,
                        1,
                        "LOW_HP_DEFENSE",
                        attackIntervalTicks: 100,
                        maxHitPoints: 1000,
                        defense: 100),
                    Attacker(
                        "enemy",
                        2000,
                        1,
                        attackIntervalTicks: 100,
                        attack: 1000)
                },
                new[]
                {
                    PassiveThreshold(
                        "LOW_HP_DEFENSE",
                        thresholdHitPointsPermille: 500,
                        inclusiveThreshold: false,
                        triggerOnce: false,
                        defenseMultiplierPermille: 4000,
                        blockCapacityAdditive: 1)
                },
                new[] { Unit("threshold", "threshold", 5, 4) },
                new[] { Unit("enemy", "enemy", 5, 4) });
            var runner = new BattleRunner(input);

            runner.Step();
            runner.Step();

            var threshold = runner.RuntimeUnits.Single(item =>
                item.UnitId == "threshold");
            Assert.That(threshold.CurrentHitPoints, Is.EqualTo(100));
            Assert.That(threshold.EffectiveDefense, Is.EqualTo(400));
            Assert.That(threshold.EffectiveBlockCapacity, Is.EqualTo(2));
        }

        [Test]
        public void OneShotHealthThreshold_ExpiresAfterConfiguredDuration()
        {
            var input = CreateInput(
                20,
                new[]
                {
                    Attacker(
                        "threshold",
                        100,
                        0,
                        "FIRST_DAMAGE_BOOST",
                        attackIntervalTicks: 20,
                        maxHitPoints: 100000,
                        attack: 1),
                    Attacker(
                        "enemy",
                        2000,
                        0,
                        attackIntervalTicks: 1000,
                        attack: 1)
                },
                new[]
                {
                    PassiveThreshold(
                        "FIRST_DAMAGE_BOOST",
                        thresholdHitPointsPermille: 1000,
                        inclusiveThreshold: false,
                        triggerOnce: true,
                        durationTicks: 3,
                        attackSpeedAdditive: 100,
                        moveSpeedMultiplierPermille: 2000)
                },
                new[] { Unit("threshold", "threshold", 5, 4) },
                new[] { Unit("enemy", "enemy", 5, 4) });
            var runner = new BattleRunner(input);

            runner.Step();
            runner.Step();
            var threshold = runner.RuntimeUnits.Single(item =>
                item.UnitId == "threshold");
            Assert.That(
                threshold.EffectiveAttackIntervalTicks,
                Is.EqualTo(10));
            Assert.That(
                threshold.EffectiveMoveSpeedCentimetresPerSecond,
                Is.EqualTo(200));

            runner.Step();
            runner.Step();
            runner.Step();
            runner.Step();

            Assert.That(
                threshold.EffectiveAttackIntervalTicks,
                Is.EqualTo(20));
            Assert.That(
                threshold.EffectiveMoveSpeedCentimetresPerSecond,
                Is.EqualTo(100));
        }

        [Test]
        public void ContinuousBlockThreshold_ReleasesExcessBlockWhenHealingDisablesIt()
        {
            var threshold = new UnitDefinition(
                "threshold",
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
                true,
                new[] { "LOW_HP_BLOCK", "REGEN_400" });
            var input = CreateInput(
                10,
                new[]
                {
                    threshold,
                    Attacker(
                        "enemy",
                        2000,
                        1,
                        attackIntervalTicks: 1000,
                        attack: 300)
                },
                new[]
                {
                    PassiveThreshold(
                        "LOW_HP_BLOCK",
                        thresholdHitPointsPermille: 500,
                        inclusiveThreshold: false,
                        triggerOnce: false,
                        blockCapacityAdditive: 1),
                    PassiveLifecycle(
                        "REGEN_400",
                        hitPointsPerSecond: 400)
                },
                new[] { Unit("threshold", "threshold", 5, 4) },
                new[]
                {
                    Unit("enemy-a", "enemy", 5, 4),
                    Unit("enemy-b", "enemy", 6, 4)
                });
            var runner = new BattleRunner(input);

            while (runner.CurrentTick < 4)
                runner.Step();
            var runtime = runner.RuntimeUnits.Single(item =>
                item.UnitId == "threshold");
            Assert.That(runtime.BlockedUnitIds.Count, Is.EqualTo(2));

            while (runner.CurrentTick < 7)
                runner.Step();
            Assert.That(runtime.EffectiveBlockCapacity, Is.EqualTo(1));
            Assert.That(runtime.BlockedUnitIds, Is.EqualTo(new[] { "enemy-a" }));
        }

        [Test]
        public void UnblockedDamageReduction_AppliesOnlyToPhysicalAndMagicDamage()
        {
            Assert.That(
                RunConditionalDamage(
                    DamageType.Physical,
                    targetBlockCapacity: 0),
                Is.EqualTo(500));
            Assert.That(
                RunConditionalDamage(
                    DamageType.Magic,
                    targetBlockCapacity: 0),
                Is.EqualTo(500));
            Assert.That(
                RunConditionalDamage(
                    DamageType.True,
                    targetBlockCapacity: 0),
                Is.EqualTo(1000));
            Assert.That(
                RunConditionalDamage(
                    DamageType.Physical,
                    targetBlockCapacity: 1),
                Is.EqualTo(1000));
        }

        private static int RunConditionalDamage(
            DamageType damageType,
            int targetBlockCapacity)
        {
            var target = new UnitDefinition(
                "target",
                5000,
                0,
                0,
                0,
                0,
                0,
                0,
                DamageType.None,
                AttackMethod.None,
                targetBlockCapacity,
                0,
                true,
                new[] { "UNBLOCKED_REDUCTION" },
                4);
            var input = CreateInput(
                3,
                new[]
                {
                    Attacker(
                        "attacker",
                        2000,
                        1,
                        damageType: damageType,
                        attackIntervalTicks: 100,
                        attack: 1000),
                    target
                },
                new[] { PassiveUnblockedDamageReduction() },
                new[] { Unit("attacker", "attacker", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            return new BattleRunner(input)
                .RunToCompletion()
                .Events
                .Single(item =>
                    item.Type == BattleEventType.Damage
                    && item.RelatedUnitId == "target")
                .DamageAmount;
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

        private static AbilityDefinition PassiveThreshold(
            string abilityId,
            int thresholdHitPointsPermille,
            bool inclusiveThreshold,
            bool triggerOnce,
            int durationTicks = 0,
            int attackMultiplierPermille =
                HealthThresholdCombatModifierDefinition
                    .NeutralMultiplierPermille,
            int defenseMultiplierPermille =
                HealthThresholdCombatModifierDefinition
                    .NeutralMultiplierPermille,
            int blockCapacityAdditive = 0,
            int attackSpeedAdditive = 0,
            int moveSpeedMultiplierPermille =
                HealthThresholdCombatModifierDefinition
                    .NeutralMultiplierPermille)
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
                null,
                new HealthThresholdCombatModifierDefinition(
                    thresholdHitPointsPermille,
                    inclusiveThreshold,
                    triggerOnce,
                    durationTicks,
                    attackMultiplierPermille,
                    defenseMultiplierPermille,
                    blockCapacityAdditive,
                    attackSpeedAdditive,
                    moveSpeedMultiplierPermille),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveUnblockedDamageReduction()
        {
            return new AbilityDefinition(
                "UNBLOCKED_REDUCTION",
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
                null,
                null,
                new UnblockedDamageTakenModifierDefinition(
                    500,
                    500),
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
            int magicResistance = 0,
            int maxHitPoints = 100000,
            int attack = 1000,
            int defense = 0)
        {
            return new UnitDefinition(
                typeId,
                maxHitPoints,
                attack,
                defense,
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
