using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using ArknoNights.Battle.Core;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattleStreamingEditModeTests
    {
        [TestCase(99, 1, 99)]
        [TestCase(100, 1, 100)]
        [TestCase(101, 2, 1)]
        [TestCase(1800, 18, 100)]
        public void Producer_PublishesExactHundredTickChunksAndImmediateTerminalTail(
            int maxTicks,
            int expectedChunkCount,
            int expectedLastChunkTicks)
        {
            var input = CreateTimeoutInput(maxTicks, 2, 3);
            var producer = new BattleSimulationProducer(
                input,
                BattleInputSha256.Compute(input));

            while (!producer.IsTerminal)
                producer.Advance(37);

            Assert.That(producer.Chunks.Count, Is.EqualTo(expectedChunkCount));
            Assert.That(producer.Chunks.Last().AuthoritativeTickCount, Is.EqualTo(expectedLastChunkTicks));
            Assert.That(producer.Chunks.Last().CompletedTick, Is.EqualTo(maxTicks));
            Assert.That(producer.Chunks.Count(item => item.IsTerminal), Is.EqualTo(1));
            Assert.That(producer.Chunks.SelectMany(item => item.Events)
                .Count(item => item.Type == BattleEventType.BattleEnded), Is.EqualTo(1));
        }

        [Test]
        public void Producer_ConcatenationAndTerminalResultEqualOneShotRun()
        {
            var input = CreateTimeoutInput(101, 2, 3);
            var oneShot = new BattleRunner(input).RunToCompletion();
            var producer = new BattleSimulationProducer(
                input,
                BattleInputSha256.Compute(input));

            while (!producer.IsTerminal)
                producer.Advance(13);

            var streamed = producer.GetTerminalResult();
            CollectionAssert.AreEqual(
                oneShot.Events.Select(EventDigest).ToArray(),
                producer.Chunks.SelectMany(item => item.Events).Select(EventDigest).ToArray());
            Assert.That(streamed.StableSummary, Is.EqualTo(oneShot.StableSummary));
            Assert.That(streamed.Outcome, Is.EqualTo(oneShot.Outcome));
            Assert.That(streamed.HomeLifeDamage, Is.EqualTo(oneShot.HomeLifeDamage));
            Assert.That(streamed.AwayLifeDamage, Is.EqualTo(oneShot.AwayLifeDamage));
            Assert.That(producer.Chunks.Last().FinalSecondSha256,
                Is.EqualTo(BattleFinalSecondHasher.Compute(oneShot)));

            var chunkCount = producer.Chunks.Count;
            producer.Advance(1000);
            Assert.That(producer.Chunks.Count, Is.EqualTo(chunkCount));
        }

        [Test]
        public void TimeoutOutcome_UsesLifeDamageAndTreatsDrawAsResolved()
        {
            var homeWin = new BattleRunner(CreateTimeoutInput(1, 3, 2)).RunToCompletion();
            var awayWin = new BattleRunner(CreateTimeoutInput(1, 1, 4)).RunToCompletion();
            var draw = new BattleRunner(CreateTimeoutInput(1, 0, 0)).RunToCompletion();

            Assert.That(homeWin.Outcome, Is.EqualTo(BattleOutcome.HomeWin));
            Assert.That(homeWin.Winner, Is.EqualTo(BattleSide.Home));
            Assert.That(homeWin.HomeLifeDamage, Is.EqualTo(2));
            Assert.That(homeWin.AwayLifeDamage, Is.EqualTo(3));
            Assert.That(awayWin.Outcome, Is.EqualTo(BattleOutcome.AwayWin));
            Assert.That(draw.Outcome, Is.EqualTo(BattleOutcome.Draw));
            Assert.That(draw.Winner, Is.Null);
            Assert.That(draw.IsTerminal, Is.True);
            Assert.That(draw.IsResolved, Is.True);
        }

        [Test]
        public void PublishedCheckpoint_IsDeeplyImmutableAcrossLaterTicks()
        {
            var input = CreateTimeoutInput(101, 1, 1);
            var producer = new BattleSimulationProducer(input, BattleInputSha256.Compute(input));
            producer.Advance(100);
            var first = producer.Chunks.Single();
            var firstDigest = string.Join("|", first.EndCheckpoint.Units.Select(CheckpointDigest));

            producer.Advance(1);

            Assert.That(string.Join("|", first.EndCheckpoint.Units.Select(CheckpointDigest)),
                Is.EqualTo(firstDigest));
            Assert.Throws<NotSupportedException>(() =>
                ((System.Collections.Generic.IList<BattleUnitCheckpoint>)first.EndCheckpoint.Units)
                    .Add(first.EndCheckpoint.Units[0]));
        }

        [Test]
        public void Producer_PreservesAttackTargetAndBlockAcrossChunkBoundary()
        {
            var input = CreateCrossBoundaryAttackInput();
            var producer = new BattleSimulationProducer(input);

            producer.Advance(100);

            var first = producer.Chunks.Single();
            var attacker = first.EndCheckpoint.Units
                .Single(item => item.UnitId == "attacker");
            Assert.That(attacker.Action,
                Is.EqualTo(BattleCheckpointAction.Attack),
                string.Join(";", first.Events.Select(EventDigest)));
            Assert.That(attacker.TargetUnitId,
                Is.EqualTo("target"));
            Assert.That(attacker.BlockedUnitIds,
                Does.Contain("target"));
            var attack = first.Events.Single(item =>
                item.Type == BattleEventType.Attack
                && item.UnitId == "attacker");
            Assert.That(attack.PlannedDamageTick,
                Is.GreaterThan(100));

            producer.Advance(input.MaxTicks - 100);

            Assert.That(producer.Chunks.Last().Events,
                Has.Some.Matches<BattleEvent>(item =>
                    item.Type == BattleEventType.Damage
                    && item.Tick == attack.PlannedDamageTick));
        }

        [Test]
        public void DynamicSpawn_AtFirstChunkTailActivatesOnNextAuthoritativeTick()
        {
            var producer = new BattleSimulationProducer(
                CreateBoundarySummonInput());

            producer.Advance(100);

            var first = producer.Chunks.Single();
            var spawnIds = first.Events
                .Where(item =>
                    item.Type == BattleEventType.Spawn
                    && item.UnitTypeId == "minion")
                .Select(item => item.UnitId)
                .ToArray();
            CollectionAssert.AreEqual(
                new[] { "-1", "-2", "-3" },
                spawnIds);
            Assert.That(first.EndCheckpoint.Units
                    .Where(item => spawnIds.Contains(item.UnitId)),
                Has.All.Matches<BattleUnitCheckpoint>(item =>
                    !item.IsActivated
                    && item.ActivationTick == 101));

            producer.Advance(1);

            Assert.That(producer.Chunks.Last()
                    .EndCheckpoint.Units
                    .Where(item => spawnIds.Contains(item.UnitId)),
                Has.All.Matches<BattleUnitCheckpoint>(item =>
                    item.IsActivated));
            Assert.That(
                producer.GetTerminalResult().AwayLifeDamage,
                Is.EqualTo(13),
                "Three minions must each contribute their own lifeDeduct=4 in addition to the caster.");
        }

        [Test]
        public void TerminalAdapters_AreStableAndCarrySealedIdentity()
        {
            var input = CreateTimeoutInput(1, 3, 2);
            var sealedHash = BattleInputSha256.Compute(input);
            var producer = new BattleSimulationProducer(input, sealedHash);
            producer.Advance(1);

            var resolution = producer.GetBattleResolution();
            var payload = producer.GetFinalSecondHashPayload();
            var recovery = producer.CreateRecoveryDescriptor();

            Assert.That(resolution.BattleId, Is.EqualTo(input.BattleId));
            Assert.That(resolution.SealedInputHash, Is.EqualTo(sealedHash));
            Assert.That(resolution.Outcome, Is.EqualTo(BattleOutcome.HomeWin));
            Assert.That(resolution.EndTick, Is.EqualTo(1));
            Assert.That(payload.FinalSecondSha256, Is.EqualTo(producer.Chunks.Single().FinalSecondSha256));
            Assert.That(recovery.NearestCheckpointTick, Is.EqualTo(1));
            Assert.That(recovery.IsTerminal, Is.True);
            Assert.That(producer.GetBattleResolution(), Is.SameAs(resolution));
        }

        [Test]
        public void TransferPayloads_DataContractJsonRoundTripWithoutCoreReferences()
        {
            var producer = new BattleSimulationProducer(
                CreateTimeoutInput(1, 3, 2));
            producer.Advance(1);
            var chunk = RoundTrip(
                new BattleChunkPayload(
                    producer.Chunks.Single()));
            var resolution = RoundTrip(
                producer.GetBattleResolution());
            var finalSecond = RoundTrip(
                producer.GetFinalSecondHashPayload());
            var recovery = RoundTrip(
                producer.CreateRecoveryDescriptor());

            Assert.That(chunk.SchemaVersion,
                Is.EqualTo("battle-chunk-payload-v1"));
            Assert.That(chunk.Events.Count,
                Is.EqualTo(
                    producer.Chunks.Single().Events.Count));
            Assert.That(chunk.EndCheckpoint.Units.Count,
                Is.EqualTo(
                    producer.Chunks.Single()
                        .EndCheckpoint.Units.Count));
            Assert.That(chunk.EndCheckpoint.Units,
                Has.All.Matches<BattleUnitCheckpointPayload>(
                    item =>
                        item.ExecutionStateFields.Count > 0));
            Assert.That(chunk.FinalSecondSha256,
                Is.EqualTo(
                    producer.Chunks.Single()
                        .FinalSecondSha256));
            Assert.That(finalSecond.FinalSecondSha256,
                Is.EqualTo(chunk.FinalSecondSha256));
            Assert.That(resolution.Outcome,
                Is.EqualTo(BattleOutcome.HomeWin));
            Assert.That(recovery.LastPublishedChunkIndex,
                Is.EqualTo(0));
            Assert.That(recovery.IsTerminal, Is.True);
        }

        [Test]
        public void FinalSecondHash_IsCultureInvariantAndUsesInclusiveLastTwentyTickWindow()
        {
            var source = new BattleRunner(
                CreateTimeoutInput(100, 3, 2))
                .RunToCompletion();
            var outsideA = CloneWithAdditionalEvent(
                source,
                EventAt(80, "outside-a"));
            var outsideB = CloneWithAdditionalEvent(
                source,
                EventAt(80, "outside-b"));
            var inside = CloneWithAdditionalEvent(
                source,
                EventAt(81, "inside"));
            var previousCulture = CultureInfo.CurrentCulture;
            string first;
            string second;
            try
            {
                CultureInfo.CurrentCulture =
                    CultureInfo.GetCultureInfo("fr-FR");
                first = BattleFinalSecondHasher.Compute(outsideA);
                CultureInfo.CurrentCulture =
                    CultureInfo.GetCultureInfo("tr-TR");
                second = BattleFinalSecondHasher.Compute(outsideB);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }

            Assert.That(first, Is.EqualTo(second),
                "Tick CompletedTicks-20 is outside the hash window.");
            Assert.That(BattleFinalSecondHasher.Compute(inside),
                Is.Not.EqualTo(first),
                "Tick CompletedTicks-19 is inside the hash window.");
            var delimitedFirst = CloneWithAdditionalEvent(
                source,
                EventAt(81, "c:,", "a|b"));
            var delimitedSecond = CloneWithAdditionalEvent(
                source,
                EventAt(81, "b|c:,", "a"));
            Assert.That(
                BattleFinalSecondHasher.Compute(delimitedFirst),
                Is.Not.EqualTo(
                    BattleFinalSecondHasher.Compute(
                        delimitedSecond)),
                "Length-prefixed fields must distinguish values that collide under delimiter concatenation.");
            Assert.That(first, Does.Match("^[0-9A-F]{64}$"));
        }

        private static BattleInput CreateTimeoutInput(
            int maxTicks,
            int homeLifeDeduct,
            int awayLifeDeduct)
        {
            var definitions = new[]
            {
                Definition("home-type", homeLifeDeduct),
                Definition("away-type", awayLifeDeduct)
            };
            var specification = new BattleInputSpecification(
                BattleInput.SupportedSchemaVersion,
                "streaming-" + maxTicks + "-" + homeLifeDeduct + "-" + awayLifeDeduct,
                maxTicks,
                definitions,
                new[]
                {
                    new PlayerSnapshot("home", BattleSide.Home, new[]
                    {
                        new UnitSnapshot("home-unit", "home-type", UnitZone.Deployed,
                            new FormationCoordinate(2, 2), Array.Empty<BuffPlaceholder>())
                    }),
                    new PlayerSnapshot("away", BattleSide.Away, new[]
                    {
                        new UnitSnapshot("away-unit", "away-type", UnitZone.Deployed,
                            new FormationCoordinate(7, 3), Array.Empty<BuffPlaceholder>())
                    })
                });
            Assert.That(BattleInputFactory.TryCreate(specification, out var input, out var errors),
                Is.True, string.Join(";", errors.Select(item => item.ToString())));
            return input;
        }

        private static UnitDefinition Definition(string typeId, int lifeDeduct)
        {
            return new UnitDefinition(
                typeId,
                1000000,
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
                Array.Empty<string>(),
                1,
                lifeDeduct);
        }

        private static BattleInput CreateCrossBoundaryAttackInput()
        {
            var attacker = new UnitDefinition(
                "attacker",
                1000000,
                10,
                0,
                0,
                10000,
                1000,
                100,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true);
            var target = new UnitDefinition(
                "target",
                1000000,
                1,
                0,
                0,
                10000,
                1000,
                1,
                DamageType.Physical,
                AttackMethod.Melee,
                1,
                0,
                true);
            return CreateInput(
                "cross-boundary-attack",
                102,
                new[] { attacker, target },
                Array.Empty<AbilityDefinition>(),
                new[]
                {
                    new UnitSnapshot(
                        "attacker",
                        "attacker",
                        UnitZone.Deployed,
                        new FormationCoordinate(4, 4),
                        Array.Empty<BuffPlaceholder>())
                },
                new[]
                {
                    new UnitSnapshot(
                        "target",
                        "target",
                        UnitZone.Deployed,
                        new FormationCoordinate(6, 4),
                        Array.Empty<BuffPlaceholder>())
                });
        }

        private static BattleInput CreateBoundarySummonInput()
        {
            var caster = new UnitDefinition(
                "caster",
                1000000,
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
                new[] { "SUMMON" },
                1);
            var minion = new UnitDefinition(
                "minion",
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
                Array.Empty<string>(),
                1,
                4);
            var enemy = new UnitDefinition(
                "enemy",
                1000000,
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
            var ability = new AbilityDefinition(
                "SUMMON",
                string.Empty,
                string.Empty,
                AbilityActivationKind.Timed,
                SilencePolicy.Unaffected,
                5,
                15,
                SkillPointGeneration.Automatic,
                new SummonEffectDefinition(
                    "minion",
                    3,
                    100,
                    false),
                null,
                "skill",
                30);
            return CreateInput(
                "boundary-summon",
                101,
                new[] { caster, minion, enemy },
                new[] { ability },
                new[]
                {
                    new UnitSnapshot(
                        "caster",
                        "caster",
                        UnitZone.Deployed,
                        new FormationCoordinate(5, 2),
                        Array.Empty<BuffPlaceholder>())
                },
                new[]
                {
                    new UnitSnapshot(
                        "enemy",
                        "enemy",
                        UnitZone.Deployed,
                        new FormationCoordinate(5, 2),
                        Array.Empty<BuffPlaceholder>())
                });
        }

        private static BattleInput CreateInput(
            string battleId,
            int maxTicks,
            UnitDefinition[] definitions,
            AbilityDefinition[] abilities,
            UnitSnapshot[] homeUnits,
            UnitSnapshot[] awayUnits)
        {
            var specification = new BattleInputSpecification(
                BattleInput.SupportedSchemaVersion,
                battleId,
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
            Assert.That(BattleInputFactory.TryCreate(
                specification,
                out var input,
                out var errors), Is.True,
                string.Join(
                    ";",
                    errors.Select(item => item.ToString())));
            return input;
        }

        private static string EventDigest(BattleEvent item)
        {
            return item.Type + "|" + item.Tick + "|" + item.Sequence + "|" + item.UnitId
                + "|" + item.RelatedUnitId + "|" + item.DamageAmount + "|" + item.Winner + "|" + item.Reason;
        }

        private static string CheckpointDigest(BattleUnitCheckpoint item)
        {
            return item.UnitId + "|" + item.Position + "|" + item.CurrentHitPoints + "|"
                + item.IsAlive + "|" + item.TargetUnitId + "|"
                + string.Join(",", item.BlockedUnitIds);
        }

        private static BattleEvent EventAt(
            int tick,
            string relatedUnitId,
            string unitId = "home-unit")
        {
            return new BattleEvent(
                BattleEventType.TargetChanged,
                tick,
                1,
                unitId,
                null,
                null,
                relatedUnitId,
                null,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                BattleStopReason.None,
                null);
        }

        private static BattleRunResult CloneWithAdditionalEvent(
            BattleRunResult source,
            BattleEvent additional)
        {
            var events = source.Events
                .Concat(new[] { additional })
                .OrderBy(item => item.Tick)
                .ThenBy(item => item.Sequence)
                .ToArray();
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
                new ReadOnlyCollection<BattleEvent>(events),
                source.FinalUnits,
                source.UnitSnapshots,
                source.StableSummary,
                source.HomeLifeDamage,
                source.AwayLifeDamage,
                source.FinalCheckpoint);
        }

        private static T RoundTrip<T>(T source)
        {
            var serializer =
                new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, source);
                stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
