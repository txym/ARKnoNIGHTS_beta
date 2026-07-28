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

        [Test]
        public void FirstAttackModifier_EnhancesOnlyTheFirstStartedAttack()
        {
            Assert.That(
                RunAttackSequence(
                    firstEnhancedAttackOrdinal: 1,
                    repeatInterval: 0,
                    attackMultiplierPermille: 2000,
                    maxTicks: 7),
                Is.EqualTo(new[] { 200, 100, 100 }));
        }

        [Test]
        public void RepeatingAttackModifier_EnhancesEveryThirdStartedAttack()
        {
            Assert.That(
                RunAttackSequence(
                    firstEnhancedAttackOrdinal: 3,
                    repeatInterval: 3,
                    attackMultiplierPermille: 1300,
                    maxTicks: 13),
                Is.EqualTo(new[] { 100, 100, 130, 100, 100, 130 }));
        }

        [Test]
        public void AttackCountState_TransitionsImmediatelyBeforeFourthAttack()
        {
            var input = CreateInput(
                16,
                new[]
                {
                    Attacker(
                        "prisoner",
                        2000,
                        0,
                        "PRISONER_STATE",
                        attackIntervalTicks: 2,
                        attack: 100),
                    NonAttacker("target", 100000)
                },
                new[]
                {
                    PassiveAttackCountState(
                        lockedAttackSpeedAdditive: -50,
                        unlockedAttackMultiplierPermille: 1500)
                },
                new[] { Unit("prisoner", "prisoner", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });
            var result = new BattleRunner(input).RunToCompletion();

            Assert.That(
                result.Events
                    .Where(item =>
                        item.Type == BattleEventType.Attack
                        && item.UnitId == "prisoner")
                    .Select(item => item.Tick)
                    .ToArray(),
                Is.EqualTo(new[] { 1, 5, 9, 13, 15 }));
            Assert.That(
                result.Events
                    .Where(item =>
                        item.Type == BattleEventType.Damage
                        && item.UnitId == "prisoner")
                    .Select(item => item.DamageAmount)
                    .ToArray(),
                Is.EqualTo(new[] { 100, 100, 100, 150, 150 }));
        }

        [Test]
        public void AttackCountState_ChangesAllConfiguredSelfModifiers()
        {
            var input = CreateInput(
                12,
                new[]
                {
                    Attacker(
                        "prisoner",
                        2000,
                        0,
                        "PRISONER_STATE",
                        damageType: DamageType.Physical,
                        attackIntervalTicks: 1,
                        maxHitPoints: 5000,
                        attack: 200),
                    Attacker(
                        "enemy",
                        2000,
                        0,
                        damageType: DamageType.Magic,
                        attackIntervalTicks: 2,
                        maxHitPoints: 100000,
                        attack: 100,
                        defense: 100)
                },
                new[]
                {
                    PassiveAttackCountState(
                        lockedAttackSpeedAdditive: -50,
                        lockedDefenseAdditive: 300,
                        unlockedAttackMultiplierPermille: 1500,
                        unlockedMagicResistanceAdditive: 40,
                        unlockedHitPointsPerSecond: 300,
                        unlockedTargetDefenseMultiplierPermille: 400)
                },
                new[] { Unit("prisoner", "prisoner", 5, 4) },
                new[] { Unit("enemy", "enemy", 5, 4) });
            var runner = new BattleRunner(input);

            while (runner.CurrentTick < 6)
                runner.Step();
            var prisoner = runner.RuntimeUnits.Single(item =>
                item.UnitId == "prisoner");
            Assert.That(prisoner.StartedAttackCount, Is.EqualTo(3));
            Assert.That(prisoner.EffectiveDefense, Is.EqualTo(300));
            Assert.That(prisoner.EffectiveMagicResistance, Is.EqualTo(0));

            while (prisoner.StartedAttackCount < 4
                   && runner.Status != BattleRunnerStatus.Stopped)
                runner.Step();
            Assert.That(prisoner.StartedAttackCount, Is.EqualTo(4));
            Assert.That(prisoner.EffectiveAttack, Is.EqualTo(300));
            Assert.That(prisoner.EffectiveDefense, Is.EqualTo(0));
            Assert.That(prisoner.EffectiveMagicResistance, Is.EqualTo(40));
            Assert.That(
                prisoner.EffectiveTargetDefenseMultiplierPermille,
                Is.EqualTo(400));
            var hitPointsAfterUnlock = prisoner.CurrentHitPoints;

            runner.Step();
            var damageAfterUnlock = runner.Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.Tick == runner.CurrentTick
                    && item.RelatedUnitId == "prisoner")
                .Sum(item => item.DamageAmount);
            Assert.That(
                prisoner.CurrentHitPoints,
                Is.EqualTo(
                    hitPointsAfterUnlock
                    - damageAfterUnlock
                    + 15));
            Assert.That(
                runner.Events.Single(item =>
                    item.Type == BattleEventType.Damage
                    && item.Tick == runner.CurrentTick
                    && item.UnitId == "prisoner").DamageAmount,
                Is.EqualTo(260));
        }

        [Test]
        public void AttackCountState_FirstReleaseUnlocksAlliedCountStates()
        {
            var input = CreateInput(
                10,
                new[]
                {
                    Attacker(
                        "boss",
                        2000,
                        0,
                        "BOSS_STATE",
                        attackIntervalTicks: 2,
                        attack: 100),
                    Attacker(
                        "ally",
                        2000,
                        0,
                        "ALLY_STATE",
                        attackIntervalTicks: 100,
                        attack: 100),
                    NonAttacker("target", 100000)
                },
                new[]
                {
                    PassiveAttackCountState(
                        unlockedAttackMultiplierPermille: 1500,
                        abilityId: "BOSS_STATE",
                        releasesAlliedAttackCountStates: true),
                    PassiveAttackCountState(
                        unlockedAttackMultiplierPermille: 1500,
                        abilityId: "ALLY_STATE")
                },
                new[]
                {
                    Unit("boss", "boss", 5, 4),
                    Unit("ally", "ally", 5, 4)
                },
                new[] { Unit("target", "target", 5, 4) });
            var runner = new BattleRunner(input);

            while (runner.CurrentTick < 7)
                runner.Step();

            var ally = runner.RuntimeUnits.Single(item =>
                item.UnitId == "ally");
            Assert.That(ally.StartedAttackCount, Is.EqualTo(1));
            Assert.That(ally.EffectiveAttack, Is.EqualTo(150));
            Assert.That(
                runner.RuntimeUnits.Single(item =>
                    item.UnitId == "boss").EffectiveAttack,
                Is.EqualTo(150));
        }

        [Test]
        public void DeathSpawn_DelaysSplitAndPreventsPrematureVictory()
        {
            var input = CreateInput(
                12,
                new[]
                {
                    Attacker(
                        "killer",
                        2000,
                        0,
                        attackIntervalTicks: 100,
                        attack: 1000),
                    NonAttacker("parent", 100, "DEATH_SPLIT"),
                    NonAttacker("child", 1000)
                },
                new[]
                {
                    PassiveDeathSpawn(
                        "DEATH_SPLIT",
                        count: 2,
                        delayTicks: 4,
                        sideLengthCentimetres: 40,
                        snapToNearestPassableCell: true,
                        summonedMoveSpeedMultiplierPermille: 1000,
                        options: new[]
                        {
                            new DeathSpawnOptionDefinition("child", 1)
                        })
                },
                new[] { Unit("killer", "killer", 5, 4) },
                new[] { Unit("parent", "parent", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();
            var death = result.Events.Single(item =>
                item.Type == BattleEventType.Death
                && item.UnitId == "parent");
            var spawns = result.Events.Where(item =>
                    item.Type == BattleEventType.Spawn
                    && item.UnitTypeId == "child")
                .ToArray();

            Assert.That(spawns.Length, Is.EqualTo(2));
            Assert.That(
                spawns.Select(item => item.Tick).Distinct().Single(),
                Is.EqualTo(death.Tick + 4));
            Assert.That(
                result.Events.Any(item =>
                    item.Type == BattleEventType.BattleEnded
                    && item.Tick < death.Tick + 4),
                Is.False);
            Assert.That(
                spawns.All(item =>
                    Math.Abs(item.ToPosition.Value.XUnits - 500) <= 20
                    && Math.Abs(item.ToPosition.Value.YUnits - 500) <= 20),
                Is.True);
        }

        [Test]
        public void DeathSpawn_EmitsThreeCentredSuccessorsOnDeathTick()
        {
            var input = CreateInput(
                6,
                new[]
                {
                    Attacker(
                        "killer",
                        2000,
                        0,
                        attackIntervalTicks: 100,
                        attack: 1000),
                    NonAttacker("parent", 100, "DEATH_UNLOAD"),
                    NonAttacker("soldier", 1000)
                },
                new[]
                {
                    PassiveDeathSpawn(
                        "DEATH_UNLOAD",
                        count: 3,
                        delayTicks: 0,
                        sideLengthCentimetres: 0,
                        snapToNearestPassableCell: false,
                        summonedMoveSpeedMultiplierPermille: 1000,
                        options: new[]
                        {
                            new DeathSpawnOptionDefinition("soldier", 1)
                        })
                },
                new[] { Unit("killer", "killer", 5, 4) },
                new[] { Unit("parent", "parent", 5, 4) });
            var result = new BattleRunner(input).RunToCompletion();
            var death = result.Events.Single(item =>
                item.Type == BattleEventType.Death
                && item.UnitId == "parent");
            var spawns = result.Events.Where(item =>
                    item.Type == BattleEventType.Spawn
                    && item.UnitTypeId == "soldier")
                .ToArray();

            Assert.That(spawns.Length, Is.EqualTo(3));
            Assert.That(spawns, Has.All.Matches<BattleEvent>(item =>
                item.Tick == death.Tick
                && item.ToPosition.Value.XUnits == 500
                && item.ToPosition.Value.YUnits == 500));
        }

        [Test]
        public void DeathSpawn_WeightedChoiceAndInstanceSpeedAreDeterministic()
        {
            var input = CreateInput(
                5,
                new[]
                {
                    Attacker(
                        "killer",
                        2000,
                        0,
                        attackIntervalTicks: 100,
                        attack: 1000),
                    NonAttacker("parent", 100, "DEATH_WEIGHTED"),
                    MovingNonAttacker("option-a", 100),
                    MovingNonAttacker("option-b", 100)
                },
                new[]
                {
                    PassiveDeathSpawn(
                        "DEATH_WEIGHTED",
                        count: 1,
                        delayTicks: 0,
                        sideLengthCentimetres: 0,
                        snapToNearestPassableCell: false,
                        summonedMoveSpeedMultiplierPermille: 3000,
                        options: new[]
                        {
                            new DeathSpawnOptionDefinition("option-a", 40),
                            new DeathSpawnOptionDefinition("option-b", 60)
                        })
                },
                new[] { Unit("killer", "killer", 5, 4) },
                new[] { Unit("parent", "parent", 5, 4) });

            var first = new BattleRunner(input);
            var second = new BattleRunner(input);
            var firstResult = first.RunToCompletion();
            var secondResult = second.RunToCompletion();
            var firstSpawn = firstResult.Events.Single(item =>
                item.Type == BattleEventType.Spawn
                && item.Tick > 0);
            var secondSpawn = secondResult.Events.Single(item =>
                item.Type == BattleEventType.Spawn
                && item.Tick > 0);
            var firstUnit = first.RuntimeUnits.Single(item =>
                item.UnitId == firstSpawn.UnitId);

            Assert.That(
                firstSpawn.UnitTypeId,
                Is.EqualTo("option-a").Or.EqualTo("option-b"));
            Assert.That(
                secondSpawn.UnitTypeId,
                Is.EqualTo(firstSpawn.UnitTypeId));
            Assert.That(
                secondSpawn.ToPosition,
                Is.EqualTo(firstSpawn.ToPosition));
            Assert.That(
                firstUnit.EffectiveMoveSpeedCentimetresPerSecond,
                Is.EqualTo(300));
            Assert.That(
                firstSpawn.SpawnSnapshot
                    .MoveSpeedCentimetresPerSecond,
                Is.EqualTo(300));
        }

        [Test]
        public void DeathAreaDamage_DelaysExplosionAndHitsEnemiesInRadius()
        {
            var input = CreateInput(
                35,
                new[]
                {
                    Attacker(
                        "bomb",
                        0,
                        0,
                        "DEATH_EXPLOSION",
                        attackIntervalTicks: 100,
                        maxHitPoints: 100,
                        attack: 100),
                    Attacker(
                        "killer",
                        2000,
                        0,
                        attackIntervalTicks: 100,
                        maxHitPoints: 100000,
                        attack: 1000),
                    NonAttacker("near", 1000),
                    NonAttacker("far", 1000)
                },
                new[]
                {
                    PassiveDeathAreaDamage(
                        DamageType.Magic,
                        attackMultiplierPermille: 2000,
                        radiusCentimetres: 150,
                        delayTicks: 26)
                },
                new[] { Unit("bomb", "bomb", 5, 4) },
                new[]
                {
                    Unit("killer", "killer", 5, 4),
                    Unit("near", "near", 6, 4),
                    Unit("far", "far", 7, 4)
                });

            var result = new BattleRunner(input).RunToCompletion();
            var death = result.Events.Single(item =>
                item.Type == BattleEventType.Death
                && item.UnitId == "bomb");
            var explosionHits = result.Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.UnitId == "bomb"
                    && item.Tick == death.Tick + 26)
                .OrderBy(item => item.RelatedUnitId)
                .ToArray();

            Assert.That(
                explosionHits.Select(item => item.RelatedUnitId),
                Is.EqualTo(new[] { "killer", "near" }));
            Assert.That(
                explosionHits.Select(item => item.DamageAmount),
                Is.EqualTo(new[] { 200, 200 }));
            Assert.That(
                result.Events.Any(item =>
                    item.Type == BattleEventType.BattleEnded
                    && item.Tick < death.Tick + 26),
                Is.False);
            Assert.That(
                result.FinalUnits.Single(item =>
                    item.UnitId == "far").HitPoints,
                Is.EqualTo(1000));
            Assert.That(
                input.CanonicalSummary,
                Does.Contain("|Z:1,2000,150,26"));
            Assert.That(
                new BattlePresentationTrackCompiler().TryCompile(
                    result,
                    out _,
                    out var diagnostics),
                Is.True,
                string.Join(
                    "; ",
                    diagnostics.Select(item => item.ToString())));
        }

        [Test]
        public void RadiusAttackArea_DamagesEveryEnemyWithinTargetRadius()
        {
            var input = CreateInput(
                3,
                new[]
                {
                    Attacker(
                        "splash",
                        2000,
                        0,
                        "RADIUS_SPLASH",
                        attackIntervalTicks: 100,
                        attack: 100),
                    NonAttacker("target", 1000),
                    NonAttacker("far", 1000)
                },
                new[]
                {
                    PassiveAttackArea(
                        "RADIUS_SPLASH",
                        AttackAreaShape.Radius,
                        firstAreaAttackOrdinal: 1,
                        repeatInterval: 1,
                        DamageType.Physical,
                        attackMultiplierPermille: 1000,
                        radiusCentimetres: 150)
                },
                new[] { Unit("splash", "splash", 5, 4) },
                new[]
                {
                    Unit("centre", "target", 5, 4),
                    Unit("near", "target", 6, 4),
                    Unit("far", "far", 7, 4)
                });

            var result = new BattleRunner(input).RunToCompletion();
            var hits = result.Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.UnitId == "splash")
                .OrderBy(item => item.RelatedUnitId)
                .ToArray();

            Assert.That(
                hits.Select(item => item.RelatedUnitId),
                Is.EqualTo(new[] { "centre", "near" }));
            Assert.That(
                hits.Select(item => item.DamageAmount),
                Is.EqualTo(new[] { 100, 100 }));
            Assert.That(
                result.FinalUnits.Single(item =>
                    item.UnitId == "far").HitPoints,
                Is.EqualTo(1000));
        }

        [Test]
        public void OrthogonalAttackArea_ExpandsEveryThirdAttackToCrossCells()
        {
            var input = CreateInput(
                7,
                new[]
                {
                    Attacker(
                        "trumpeter",
                        2000,
                        0,
                        "CROSS_THIRD",
                        attackIntervalTicks: 2,
                        attack: 100),
                    NonAttacker("target", 1000)
                },
                new[]
                {
                    PassiveAttackArea(
                        "CROSS_THIRD",
                        AttackAreaShape.OrthogonalAdjacentCells,
                        firstAreaAttackOrdinal: 3,
                        repeatInterval: 3,
                        DamageType.Physical,
                        attackMultiplierPermille: 1000,
                        radiusCentimetres: 0)
                },
                new[] { Unit("trumpeter", "trumpeter", 5, 4) },
                new[]
                {
                    Unit("primary", "target", 5, 4),
                    Unit("adjacent", "target", 6, 4),
                    Unit("diagonal", "target", 4, 3)
                });

            var result = new BattleRunner(input).RunToCompletion();
            var hits = result.Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.UnitId == "trumpeter")
                .ToArray();

            Assert.That(
                hits.Where(item => item.Tick < 6)
                    .Select(item => item.RelatedUnitId),
                Is.EqualTo(new[] { "primary", "primary" }));
            Assert.That(
                hits.Where(item => item.Tick == 6)
                    .Select(item => item.RelatedUnitId)
                    .OrderBy(item => item),
                Is.EqualTo(new[] { "adjacent", "primary" }));
            Assert.That(
                result.FinalUnits.Single(item =>
                    item.UnitId == "diagonal").HitPoints,
                Is.EqualTo(1000));
        }

        [Test]
        public void OnHitDamageOverTime_RefreshesSameNameWithoutStacking()
        {
            var input = CreateInput(
                150,
                new[]
                {
                    Attacker(
                        "bleeder",
                        2000,
                        0,
                        "BLEED",
                        attackIntervalTicks: 80,
                        attack: 10),
                    NonAttacker("target", 10000)
                },
                new[]
                {
                    PassiveOnHitDamageOverTime(
                        damagePerSecond: 20,
                        durationTicks: 100)
                },
                new[]
                {
                    Unit("bleeder-a", "bleeder", 5, 4),
                    Unit("bleeder-b", "bleeder", 5, 4)
                },
                new[] { Unit("target", "target", 5, 4) });

            var result = new BattleRunner(input).RunToCompletion();
            var directDamage = result.Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.RelatedUnitId == "target")
                .Sum(item => item.DamageAmount);
            var bleedDamage = result.Events
                .Where(item =>
                    item.Type == BattleEventType.HealthChanged
                    && item.UnitId == "target"
                    && item.DamageType == DamageType.True)
                .Sum(item => item.DamageAmount);

            Assert.That(directDamage, Is.EqualTo(40));
            Assert.That(bleedDamage, Is.EqualTo(149));
            Assert.That(
                result.FinalUnits.Single(item =>
                    item.UnitId == "target").HitPoints,
                Is.EqualTo(9811));
        }

        [Test]
        public void UnblockedAttackCharge_AccumulatesAndClearsAtAttackEnd()
        {
            var input = CreateInput(
                30,
                new[]
                {
                    Attacker(
                        "musician",
                        2000,
                        0,
                        "UNBLOCKED_CHARGE",
                        attackIntervalTicks: 20,
                        attack: 100),
                    NonAttacker("target", 10000)
                },
                new[]
                {
                    PassiveUnblockedAttackCharge(
                        checkIntervalTicks: 5,
                        attackAdditivePerStack: 30,
                        maxStacks: 30)
                },
                new[] { Unit("musician", "musician", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });
            var runner = new BattleRunner(input);

            while (runner.CurrentTick < 20)
                runner.Step();
            var musician = runner.RuntimeUnits.Single(item =>
                item.UnitId == "musician");
            Assert.That(musician.EffectiveAttack, Is.EqualTo(220));

            runner.Step();
            Assert.That(musician.EffectiveAttack, Is.EqualTo(220));
            runner.Step();
            Assert.That(musician.EffectiveAttack, Is.EqualTo(100));
            Assert.That(
                runner.Events
                    .Where(item =>
                        item.Type == BattleEventType.Damage
                        && item.UnitId == "musician")
                    .Select(item => item.DamageAmount),
                Is.EqualTo(new[] { 100, 220 }));
        }

        [Test]
        public void SuccessfulAttackTriggeredSpawn_FiresOnEveryThirdHit()
        {
            var input = CreateInput(
                7,
                new[]
                {
                    Attacker(
                        "builder",
                        2000,
                        0,
                        "ATTACK_SPAWN",
                        attackIntervalTicks: 2,
                        attack: 10),
                    NonAttacker("target", 10000),
                    NonAttacker("fragment", 1000)
                },
                new[]
                {
                    PassiveTriggeredSpawn(
                        "ATTACK_SPAWN",
                        TriggeredSpawnKind.SuccessfulAttack,
                        firstTriggerOrdinal: 3,
                        repeatInterval: 3,
                        summonTypeId: "fragment",
                        sideLengthCentimetres: 40,
                        maxActiveSameType: 12)
                },
                new[] { Unit("builder", "builder", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            var runner = new BattleRunner(input);
            var result = runner.RunToCompletion();
            var spawn = result.Events.Single(item =>
                item.Type == BattleEventType.Spawn
                && item.UnitTypeId == "fragment");
            var ownerPosition = runner.RuntimeUnits.Single(item =>
                item.UnitId == "builder").Position;

            Assert.That(spawn.Tick, Is.EqualTo(6));
            Assert.That(
                Math.Abs(
                    spawn.ToPosition.Value.XUnits
                    - ownerPosition.XUnits),
                Is.LessThanOrEqualTo(20));
            Assert.That(
                Math.Abs(
                    spawn.ToPosition.Value.YUnits
                    - ownerPosition.YUnits),
                Is.LessThanOrEqualTo(20));
            Assert.That(
                spawn.SpawnSnapshot.ActivationTick,
                Is.EqualTo(7));
        }

        [Test]
        public void DamageReceivedTriggeredSpawn_RespectsFriendlyTypeCap()
        {
            Assert.That(
                RunDamageReceivedSpawn(initialFragments: 0),
                Is.EqualTo(1));
            Assert.That(
                RunDamageReceivedSpawn(initialFragments: 8),
                Is.EqualTo(0));
        }

        private static int RunDamageReceivedSpawn(int initialFragments)
        {
            var homeUnits = Enumerable.Range(1, 10)
                .Select(index =>
                    Unit(
                        "hitter-" + index,
                        "hitter",
                        5,
                        4))
                .ToArray();
            var awayUnits = new[]
                {
                    Unit("builder", "builder", 5, 4)
                }
                .Concat(Enumerable.Range(1, initialFragments)
                    .Select(index =>
                        Unit(
                            "fragment-" + index,
                            "fragment",
                            1,
                            1)))
                .ToArray();
            var input = CreateInput(
                3,
                new[]
                {
                    Attacker(
                        "hitter",
                        2000,
                        0,
                        attackIntervalTicks: 100,
                        attack: 10),
                    NonAttacker(
                        "builder",
                        10000,
                        "RECEIVED_SPAWN"),
                    NonAttacker("fragment", 1000)
                },
                new[]
                {
                    PassiveTriggeredSpawn(
                        "RECEIVED_SPAWN",
                        TriggeredSpawnKind.DamageReceived,
                        firstTriggerOrdinal: 10,
                        repeatInterval: 10,
                        summonTypeId: "fragment",
                        sideLengthCentimetres: 40,
                        maxActiveSameType: 8)
                },
                homeUnits,
                awayUnits);

            return new BattleRunner(input)
                .RunToCompletion()
                .Events
                .Count(item =>
                    item.Type == BattleEventType.Spawn
                    && item.UnitTypeId == "fragment"
                    && item.SpawnSnapshot.IsDynamicallyGenerated);
        }

        [Test]
        public void HealthThresholdAdjacentSpawn_UsesFourNonGateCellCentresOnce()
        {
            var input = CreateInput(
                5,
                new[]
                {
                    Attacker(
                        "attacker",
                        2000,
                        0,
                        attackIntervalTicks: 100,
                        attack: 600),
                    NonAttacker(
                        "golem",
                        1000,
                        "HALF_HEALTH_DROP"),
                    NonAttacker("berry", 1000)
                },
                new[]
                {
                    PassiveHealthThresholdAdjacentSpawn(
                        "HALF_HEALTH_DROP",
                        thresholdHitPointsPermille: 500,
                        inclusiveThreshold: false,
                        summonTypeId: "berry")
                },
                new[] { Unit("attacker", "attacker", 5, 4) },
                new[] { Unit("golem", "golem", 5, 4) });

            var result =
                new BattleRunner(input).RunToCompletion();
            var spawns = result.Events
                .Where(item =>
                    item.Type == BattleEventType.Spawn
                    && item.UnitTypeId == "berry")
                .ToArray();

            Assert.That(spawns.Length, Is.EqualTo(4));
            Assert.That(
                spawns.Select(item => item.Tick).Distinct(),
                Is.EqualTo(new[] { 2 }));
            Assert.That(
                spawns.Select(item =>
                        item.ToPosition.Value.ToString())
                    .OrderBy(item => item, StringComparer.Ordinal),
                Is.EqualTo(new[]
                {
                    "400,500",
                    "500,400",
                    "500,600",
                    "600,500"
                }));
            Assert.That(
                spawns.Select(item =>
                        item.SpawnSnapshot.ActivationTick)
                    .Distinct(),
                Is.EqualTo(new[] { 3 }));
            Assert.That(
                spawns.All(item =>
                    !BattlefieldRules.IsGate(
                        NearestCoordinate(
                            item.ToPosition.Value))),
                Is.True);
        }

        [Test]
        public void RadiusAura_ExcludesSourceAndDoesNotStackByAbilityId()
        {
            var input = CreateInput(
                2,
                new[]
                {
                    NonAttacker("aura-source", 1000, "ARMOR_AURA"),
                    Attacker(
                        "target",
                        0,
                        0,
                        attackIntervalTicks: 100,
                        defense: 100),
                    NonAttacker("dummy", 100000)
                },
                new[]
                {
                    PassiveAura(
                        "ARMOR_AURA",
                        AuraTargetSide.Allies,
                        isGlobal: false,
                        radiusCentimetres: 250,
                        excludeSource: true,
                        nonStackingByAbilityId: true,
                        defenseAdditive: 300,
                        magicResistanceAdditive: 30)
                },
                new[]
                {
                    Unit("source-a", "aura-source", 4, 4),
                    Unit("source-b", "aura-source", 8, 4),
                    Unit("target-close", "target", 6, 4),
                    Unit("target-far", "target", 1, 1)
                },
                new[] { Unit("dummy", "dummy", 5, 4) });
            var runner = new BattleRunner(input);

            runner.Step();

            var source = runner.RuntimeUnits.Single(item =>
                item.UnitId == "source-a");
            var close = runner.RuntimeUnits.Single(item =>
                item.UnitId == "target-close");
            var far = runner.RuntimeUnits.Single(item =>
                item.UnitId == "target-far");
            Assert.That(source.EffectiveDefense, Is.EqualTo(0));
            Assert.That(close.EffectiveDefense, Is.EqualTo(400));
            Assert.That(close.EffectiveMagicResistance, Is.EqualTo(30));
            Assert.That(far.EffectiveDefense, Is.EqualTo(100));
            Assert.That(far.EffectiveMagicResistance, Is.EqualTo(0));
        }

        [Test]
        public void GlobalFriendlyAuraAndEnemySlowModifyCurrentCombatStats()
        {
            var input = CreateInput(
                2,
                new[]
                {
                    NonAttacker("order-source", 1000, "GLOBAL_ORDER"),
                    Attacker(
                        "target",
                        0,
                        0,
                        attackIntervalTicks: 20,
                        attack: 100),
                    NonAttacker("slow-source", 1000, "ENEMY_SLOW")
                },
                new[]
                {
                    PassiveAura(
                        "GLOBAL_ORDER",
                        AuraTargetSide.Allies,
                        isGlobal: true,
                        radiusCentimetres: 0,
                        excludeSource: false,
                        nonStackingByAbilityId: true,
                        attackMultiplierPermille: 1100,
                        defenseAdditive: 100),
                    PassiveAura(
                        "ENEMY_SLOW",
                        AuraTargetSide.Enemies,
                        isGlobal: false,
                        radiusCentimetres: 250,
                        excludeSource: false,
                        nonStackingByAbilityId: true,
                        attackSpeedMultiplierPermille: 500)
                },
                new[]
                {
                    Unit("order", "order-source", 4, 4),
                    Unit("target", "target", 5, 4)
                },
                new[] { Unit("slow", "slow-source", 5, 4) });
            var runner = new BattleRunner(input);

            runner.Step();

            var target = runner.RuntimeUnits.Single(item =>
                item.UnitId == "target");
            Assert.That(target.EffectiveAttack, Is.EqualTo(110));
            Assert.That(target.EffectiveDefense, Is.EqualTo(100));
            Assert.That(
                target.EffectiveAttackIntervalTicks,
                Is.EqualTo(40));
        }

        [Test]
        public void BlockedCounterpartSlow_DoesNotStackByAbilityId()
        {
            var input = CreateInput(
                4,
                new[]
                {
                    Attacker(
                        "blocker",
                        2000,
                        2,
                        attackIntervalTicks: 10),
                    Attacker(
                        "tumour",
                        2000,
                        1,
                        "BLOCK_SLOW",
                        attackIntervalTicks: 100)
                },
                new[]
                {
                    PassiveBlockedCounterpartSlow(
                        attackSpeedMultiplierPermille: 200)
                },
                new[] { Unit("blocker", "blocker", 5, 4) },
                new[]
                {
                    Unit("tumour-a", "tumour", 5, 4),
                    Unit("tumour-b", "tumour", 4, 4)
                });
            var runner = new BattleRunner(input);

            while (runner.CurrentTick < 3)
                runner.Step();

            var blocker = runner.RuntimeUnits.Single(item =>
                item.UnitId == "blocker");
            Assert.That(blocker.BlockedUnitIds.Count, Is.EqualTo(2));
            Assert.That(
                blocker.EffectiveAttackIntervalTicks,
                Is.EqualTo(50));
        }

        [Test]
        public void NearbySameTypeDefense_StacksPerFriendlyNeighbour()
        {
            var phalanx = new UnitDefinition(
                "phalanx",
                1000,
                0,
                100,
                0,
                0,
                0,
                0,
                DamageType.None,
                AttackMethod.None,
                0,
                0,
                true,
                new[] { "MR_70", "NEARBY_DEFENSE" },
                4);
            var input = CreateInput(
                2,
                new[]
                {
                    phalanx,
                    NonAttacker("dummy", 100000)
                },
                new[]
                {
                    Passive(
                        "MR_70",
                        Modifier(magicResistanceAdditive: 70)),
                    PassiveNearbySameTypeDefense(
                        radiusCentimetres: 150,
                        defenseAdditivePerUnit: 200)
                },
                new[]
                {
                    Unit("left", "phalanx", 4, 4),
                    Unit("centre", "phalanx", 5, 4),
                    Unit("right", "phalanx", 6, 4)
                },
                new[] { Unit("dummy", "dummy", 5, 4) });
            var runner = new BattleRunner(input);

            runner.Step();

            var left = runner.RuntimeUnits.Single(item =>
                item.UnitId == "left");
            var centre = runner.RuntimeUnits.Single(item =>
                item.UnitId == "centre");
            var right = runner.RuntimeUnits.Single(item =>
                item.UnitId == "right");
            Assert.That(left.EffectiveDefense, Is.EqualTo(300));
            Assert.That(centre.EffectiveDefense, Is.EqualTo(500));
            Assert.That(right.EffectiveDefense, Is.EqualTo(300));
            Assert.That(centre.EffectiveMagicResistance, Is.EqualTo(70));
        }

        [Test]
        public void Evasion_AvoidsPhysicalAndMagicAttacksButNotTrueDamage()
        {
            Assert.That(
                RunEvasionDamage(
                    DamageType.Physical,
                    physicalChancePermille: 1000,
                    magicChancePermille: 0),
                Is.EqualTo(0));
            Assert.That(
                RunEvasionDamage(
                    DamageType.Magic,
                    physicalChancePermille: 0,
                    magicChancePermille: 1000),
                Is.EqualTo(0));
            Assert.That(
                RunEvasionDamage(
                    DamageType.True,
                    physicalChancePermille: 1000,
                    magicChancePermille: 1000),
                Is.EqualTo(100));
        }

        [Test]
        public void Evasion_EightyPercentRollIsDeterministic()
        {
            var input = CreateInput(
                41,
                new[]
                {
                    Attacker(
                        "attacker",
                        2000,
                        0,
                        damageType: DamageType.Physical,
                        attackIntervalTicks: 2,
                        attack: 100),
                    NonAttacker("target", 100000, "EVADE_80")
                },
                new[]
                {
                    PassiveEvasion(
                        physicalChancePermille: 800,
                        magicChancePermille: 800)
                },
                new[] { Unit("attacker", "attacker", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            var first = new BattleRunner(input)
                .RunToCompletion()
                .Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.UnitId == "attacker")
                .Select(item => item.DamageAmount)
                .ToArray();
            var second = new BattleRunner(input)
                .RunToCompletion()
                .Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.UnitId == "attacker")
                .Select(item => item.DamageAmount)
                .ToArray();

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Has.Some.EqualTo(0));
            Assert.That(first, Has.Some.EqualTo(100));
            Assert.That(input.CanonicalSummary, Does.Contain("|V:800,800"));
        }

        private static int RunEvasionDamage(
            DamageType damageType,
            int physicalChancePermille,
            int magicChancePermille)
        {
            var input = CreateInput(
                3,
                new[]
                {
                    Attacker(
                        "attacker",
                        2000,
                        0,
                        damageType: damageType,
                        attackIntervalTicks: 100,
                        attack: 100),
                    NonAttacker("target", 1000, "EVADE_80")
                },
                new[]
                {
                    PassiveEvasion(
                        physicalChancePermille,
                        magicChancePermille)
                },
                new[] { Unit("attacker", "attacker", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            return new BattleRunner(input)
                .RunToCompletion()
                .Events
                .Single(item =>
                    item.Type == BattleEventType.Damage
                    && item.UnitId == "attacker")
                .DamageAmount;
        }

        private static int[] RunAttackSequence(
            int firstEnhancedAttackOrdinal,
            int repeatInterval,
            int attackMultiplierPermille,
            int maxTicks)
        {
            var input = CreateInput(
                maxTicks,
                new[]
                {
                    Attacker(
                        "sequence",
                        2000,
                        0,
                        "ATTACK_SEQUENCE",
                        attackIntervalTicks: 2,
                        attack: 100),
                    NonAttacker("target", 100000)
                },
                new[]
                {
                    PassiveAttackSequence(
                        firstEnhancedAttackOrdinal,
                        repeatInterval,
                        attackMultiplierPermille)
                },
                new[] { Unit("sequence", "sequence", 5, 4) },
                new[] { Unit("target", "target", 5, 4) });

            return new BattleRunner(input)
                .RunToCompletion()
                .Events
                .Where(item =>
                    item.Type == BattleEventType.Damage
                    && item.UnitId == "sequence")
                .Select(item => item.DamageAmount)
                .ToArray();
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

        private static AbilityDefinition PassiveAttackSequence(
            int firstEnhancedAttackOrdinal,
            int repeatInterval,
            int attackMultiplierPermille)
        {
            return new AbilityDefinition(
                "ATTACK_SEQUENCE",
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
                null,
                new AttackSequenceModifierDefinition(
                    firstEnhancedAttackOrdinal,
                    repeatInterval,
                    attackMultiplierPermille),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveAttackCountState(
            int lockedAttackSpeedAdditive = 0,
            int lockedDefenseAdditive = 0,
            int unlockedAttackMultiplierPermille =
                AttackCountStateModifierDefinition
                    .NeutralMultiplierPermille,
            int unlockedMagicResistanceAdditive = 0,
            int unlockedHitPointsPerSecond = 0,
            int unlockedTargetDefenseMultiplierPermille =
                AttackCountStateModifierDefinition
                    .NeutralMultiplierPermille,
            string abilityId = "PRISONER_STATE",
            bool releasesAlliedAttackCountStates = false)
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
                null,
                null,
                null,
                new AttackCountStateModifierDefinition(
                    4,
                    lockedAttackSpeedAdditive,
                    lockedDefenseAdditive,
                    unlockedAttackMultiplierPermille,
                    unlockedMagicResistanceAdditive,
                    unlockedHitPointsPerSecond,
                    unlockedTargetDefenseMultiplierPermille,
                    releasesAlliedAttackCountStates),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveDeathSpawn(
            string abilityId,
            int count,
            int delayTicks,
            int sideLengthCentimetres,
            bool snapToNearestPassableCell,
            int summonedMoveSpeedMultiplierPermille,
            params DeathSpawnOptionDefinition[] options)
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
                null,
                null,
                null,
                null,
                new DeathSpawnEffectDefinition(
                    options,
                    count,
                    delayTicks,
                    sideLengthCentimetres,
                    snapToNearestPassableCell,
                    summonedMoveSpeedMultiplierPermille),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveDeathAreaDamage(
            DamageType damageType,
            int attackMultiplierPermille,
            int radiusCentimetres,
            int delayTicks)
        {
            return new AbilityDefinition(
                "DEATH_EXPLOSION",
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
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new DeathAreaDamageEffectDefinition(
                    damageType,
                    attackMultiplierPermille,
                    radiusCentimetres,
                    delayTicks),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveAttackArea(
            string abilityId,
            AttackAreaShape shape,
            int firstAreaAttackOrdinal,
            int repeatInterval,
            DamageType damageType,
            int attackMultiplierPermille,
            int radiusCentimetres)
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
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new AttackAreaDamageModifierDefinition(
                    shape,
                    firstAreaAttackOrdinal,
                    repeatInterval,
                    damageType,
                    attackMultiplierPermille,
                    radiusCentimetres),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveOnHitDamageOverTime(
            int damagePerSecond,
            int durationTicks)
        {
            return new AbilityDefinition(
                "BLEED",
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
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new OnHitDamageOverTimeEffectDefinition(
                    damagePerSecond,
                    durationTicks),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveUnblockedAttackCharge(
            int checkIntervalTicks,
            int attackAdditivePerStack,
            int maxStacks)
        {
            return new AbilityDefinition(
                "UNBLOCKED_CHARGE",
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
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new UnblockedAttackChargeDefinition(
                    checkIntervalTicks,
                    attackAdditivePerStack,
                    maxStacks),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveTriggeredSpawn(
            string abilityId,
            TriggeredSpawnKind triggerKind,
            int firstTriggerOrdinal,
            int repeatInterval,
            string summonTypeId,
            int sideLengthCentimetres,
            int maxActiveSameType)
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
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new TriggeredSpawnEffectDefinition(
                    triggerKind,
                    firstTriggerOrdinal,
                    repeatInterval,
                    summonTypeId,
                    sideLengthCentimetres,
                    maxActiveSameType),
                string.Empty,
                0);
        }

        private static AbilityDefinition
            PassiveHealthThresholdAdjacentSpawn(
                string abilityId,
                int thresholdHitPointsPermille,
                bool inclusiveThreshold,
                string summonTypeId)
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
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new HealthThresholdAdjacentSpawnEffectDefinition(
                    thresholdHitPointsPermille,
                    inclusiveThreshold,
                    summonTypeId),
                string.Empty,
                0);
        }

        private static BattlefieldCoordinate NearestCoordinate(
            FixedPosition position)
        {
            return new BattlefieldCoordinate(
                position.XUnits / FixedPosition.UnitsPerMetre,
                position.YUnits / FixedPosition.UnitsPerMetre);
        }

        private static AbilityDefinition PassiveAura(
            string abilityId,
            AuraTargetSide targetSide,
            bool isGlobal,
            int radiusCentimetres,
            bool excludeSource,
            bool nonStackingByAbilityId,
            int attackMultiplierPermille =
                AuraCombatModifierDefinition
                    .NeutralMultiplierPermille,
            int defenseAdditive = 0,
            int magicResistanceAdditive = 0,
            int attackSpeedMultiplierPermille =
                AuraCombatModifierDefinition
                    .NeutralMultiplierPermille,
            int moveSpeedMultiplierPermille =
                AuraCombatModifierDefinition
                    .NeutralMultiplierPermille,
            int hitPointsPerSecond = 0)
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
                null,
                null,
                null,
                null,
                null,
                new AuraCombatModifierDefinition(
                    targetSide,
                    isGlobal,
                    radiusCentimetres,
                    excludeSource,
                    nonStackingByAbilityId,
                    attackMultiplierPermille,
                    defenseAdditive,
                    magicResistanceAdditive,
                    attackSpeedMultiplierPermille,
                    moveSpeedMultiplierPermille,
                    hitPointsPerSecond),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveBlockedCounterpartSlow(
            int attackSpeedMultiplierPermille)
        {
            return new AbilityDefinition(
                "BLOCK_SLOW",
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
                null,
                null,
                null,
                null,
                null,
                new BlockedCounterpartCombatModifierDefinition(
                    true,
                    attackSpeedMultiplierPermille),
                null,
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveNearbySameTypeDefense(
            int radiusCentimetres,
            int defenseAdditivePerUnit)
        {
            return new AbilityDefinition(
                "NEARBY_DEFENSE",
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
                null,
                null,
                null,
                null,
                null,
                null,
                new NearbySameTypeSelfModifierDefinition(
                    radiusCentimetres,
                    defenseAdditivePerUnit),
                string.Empty,
                0);
        }

        private static AbilityDefinition PassiveEvasion(
            int physicalChancePermille,
            int magicChancePermille)
        {
            return new AbilityDefinition(
                "EVADE_80",
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
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new EvasionModifierDefinition(
                    physicalChancePermille,
                    magicChancePermille),
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

        private static UnitDefinition MovingNonAttacker(
            string typeId,
            int moveSpeedCentimetresPerSecond)
        {
            return new UnitDefinition(
                typeId,
                1000,
                0,
                0,
                0,
                moveSpeedCentimetresPerSecond,
                0,
                0,
                DamageType.None,
                AttackMethod.None,
                0,
                0,
                true,
                Array.Empty<string>());
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
