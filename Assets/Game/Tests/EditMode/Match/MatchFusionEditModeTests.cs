using System;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchFusionEditModeTests
    {
        [Test]
        public void Purchase_TwoEqualUnitsFuseInOneRevisionAndReportStableStep()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var first = MatchFusionTestData.PurchaseNext(authority, "buy-1");
            var firstUnitId = first.Acquisition.FinalSurvivorUnitId;
            var revision = authority.StateRevision;
            var changedCount = 0;
            PublicMatchSnapshot published = null;
            authority.Changed += changed =>
            {
                changedCount++;
                published = changed.Snapshot;
            };

            var second = MatchFusionTestData.PurchaseNext(authority, "buy-2");

            var owner = authority.ProjectForPlayer("player-1").Owner;
            Assert.That(second.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.StateRevision, Is.EqualTo(revision + 1));
            Assert.That(owner.Units, Has.Count.EqualTo(1));
            Assert.That(owner.Units[0].EliteLevel, Is.EqualTo(1));
            Assert.That(second.Acquisition.FusionSteps, Has.Count.EqualTo(1));
            Assert.That(second.Acquisition.FinalSurvivorUnitId,
                Is.EqualTo(string.CompareOrdinal(firstUnitId, second.Acquisition.AcquiredUnitId) <= 0
                    ? firstUnitId
                    : second.Acquisition.AcquiredUnitId));
            Assert.That(second.Acquisition.RetiredUnitIds,
                Is.EqualTo(new[] { second.Acquisition.FusionSteps[0].ConsumedUnitId }));
            Assert.That(authority.ProjectForHostAuthority().Pool.RetiredUnits.Single().Reason,
                Is.EqualTo(MatchRetirementReason.FusionConsumed));
            Assert.That(changedCount, Is.EqualTo(1));
            Assert.That(published.Seats.Single(seat => seat.PlayerId == "player-1")
                .Units.Single().EliteLevel, Is.EqualTo(1));
        }

        [Test]
        public void Purchase_ChainsFourAndEightCopiesToEliteTwoAndThree()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var four = MatchFusionTestData.CreateAuthority(catalog);
            four.TryEnterPreparation(1);
            for (var index = 0; index < 4; index++)
            {
                MatchFusionTestData.PurchaseNext(four, "four-" + index);
            }
            Assert.That(four.ProjectForPlayer("player-1").Owner.Units.Single().EliteLevel,
                Is.EqualTo(2));

            var eight = MatchFusionTestData.CreateAuthority(catalog);
            eight.TryEnterPreparation(1);
            string stableSurvivor = null;
            for (var index = 0; index < 8; index++)
            {
                var result = MatchFusionTestData.PurchaseNext(eight, "eight-" + index);
                if (stableSurvivor == null
                    || string.CompareOrdinal(result.Acquisition.FinalSurvivorUnitId, stableSurvivor) < 0)
                {
                    stableSurvivor = result.Acquisition.FinalSurvivorUnitId;
                }
            }
            var final = eight.ProjectForPlayer("player-1").Owner.Units.Single();
            Assert.That(final.EliteLevel, Is.EqualTo(3));
            Assert.That(final.UnitId, Is.EqualTo(stableSurvivor));
            Assert.That(MatchEliteRules.GetBaseCopyEquivalent(final.EliteLevel), Is.EqualTo(8));
            Assert.That(MatchEliteRules.GetBattleEntityCount(final.EliteLevel), Is.EqualTo(5));
        }

        [TestCase(0, 4, 4)]
        [TestCase(1, 4, 2)]
        [TestCase(2, 8, 2)]
        public void Purchase_RespectsCatalogMaximumElite(
            int maxEliteLevel,
            int purchaseCount,
            int expectedUnitCount)
        {
            var catalog = MatchTestData.Catalog(
                MatchTestData.Entry("1001", 1, maxEliteLevel: maxEliteLevel));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);

            for (var index = 0; index < purchaseCount; index++)
            {
                MatchFusionTestData.PurchaseNext(authority, "max-" + maxEliteLevel + "-" + index);
            }

            var units = authority.ProjectForPlayer("player-1").Owner.Units;
            Assert.That(units, Has.Count.EqualTo(expectedUnitCount));
            Assert.That(units.Max(unit => unit.EliteLevel), Is.EqualTo(maxEliteLevel));
        }

        [Test]
        public void Purchase_DifferentTypeOrDifferentEliteDoesNotFuse()
        {
            var catalog = MatchTestData.Catalog(
                MatchTestData.Entry("1001", 1),
                MatchTestData.Entry("1002", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);

            MatchFusionTestData.PurchaseNext(authority, "a-1", typeId: "1001");
            MatchFusionTestData.PurchaseNext(authority, "a-2", typeId: "1001");
            MatchFusionTestData.PurchaseNext(authority, "a-3", typeId: "1001");
            MatchFusionTestData.PurchaseNext(authority, "b-1", typeId: "1002");

            var units = authority.ProjectForPlayer("player-1").Owner.Units;
            Assert.That(units.Count(unit => unit.TypeId == "1001"), Is.EqualTo(2));
            Assert.That(units.Single(unit => unit.TypeId == "1001" && unit.EliteLevel == 1), Is.Not.Null);
            Assert.That(units.Single(unit => unit.TypeId == "1001" && unit.EliteLevel == 0), Is.Not.Null);
            Assert.That(units.Single(unit => unit.TypeId == "1002").EliteLevel, Is.Zero);
        }

        [Test]
        public void Purchase_UnreadyPreparationUsesDeployedSurvivorAndPreservesFormation()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var purchased = MatchFusionTestData.PurchaseNext(authority, "seed");
            var deployed = MatchFusionTestData.Unit(
                purchased.Acquisition.AcquiredUnitId,
                "1001",
                MatchUnitZone.Deployed,
                0,
                0,
                new MatchFormationPosition(4, 2));
            authority = MatchFusionTestData.WithPlayerOneState(authority, new[] { deployed });

            var result = MatchFusionTestData.PurchaseNext(authority, "fuse");

            var survivor = authority.ProjectForPlayer("player-1").Owner.Units.Single();
            Assert.That(survivor.UnitId, Is.EqualTo(deployed.UnitId));
            Assert.That(survivor.Zone, Is.EqualTo(MatchUnitZone.Deployed));
            Assert.That(survivor.Formation, Is.EqualTo(new MatchFormationPosition(4, 2)));
            Assert.That(survivor.EliteLevel, Is.EqualTo(1));
            Assert.That(result.Acquisition.FinalSurvivorUnitId, Is.EqualTo(deployed.UnitId));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.AvailableDeploymentCost,
                Is.EqualTo(12));
        }

        [Test]
        public void Purchase_ReadyAndBattleExcludeDeployedButStillFuseStagingWithOverflow()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var deployed = MatchFusionTestData.Unit(
                "external-deployed",
                "1001",
                MatchUnitZone.Deployed,
                0,
                0,
                new MatchFormationPosition(4, 2));
            var overflow = MatchFusionTestData.Unit(
                "external-overflow",
                "1001",
                MatchUnitZone.Overflow,
                0,
                1);
            authority = MatchFusionTestData.WithPlayerOneState(authority, new[] { deployed, overflow });
            MatchFusionTestData.Execute(
                authority,
                "player-1",
                "ready",
                new SetPreparationReadyCommand(true));

            var readyPurchase = MatchFusionTestData.PurchaseNext(authority, "ready-buy");

            var readyUnits = authority.ProjectForPlayer("player-1").Owner.Units;
            Assert.That(readyUnits.Single(unit => unit.UnitId == "external-deployed").EliteLevel,
                Is.Zero);
            Assert.That(readyUnits.Single(unit => unit.UnitId != "external-deployed").EliteLevel,
                Is.EqualTo(1));
            Assert.That(readyPurchase.Acquisition.FinalSurvivorUnitId,
                Is.Not.EqualTo("external-overflow"),
                "Staging must survive over Overflow regardless of UnitId ordering.");

            authority.TryAdvancePhase(MatchPhase.Sealing);
            authority.TryAdvancePhase(MatchPhase.Battle);
            MatchFusionTestData.PurchaseNext(authority, "battle-buy");
            var battleDeployed = authority.ProjectForPlayer("player-1").Owner.Units.Single(
                unit => unit.UnitId == "external-deployed");
            Assert.That(battleDeployed.EliteLevel, Is.Zero);
            Assert.That(battleDeployed.Formation, Is.EqualTo(new MatchFormationPosition(4, 2)));
        }

        [Test]
        public void Purchase_DoesNotScanUnrelatedHistoricalType()
        {
            var catalog = MatchTestData.Catalog(
                MatchTestData.Entry("1001", 1),
                MatchTestData.Entry("1002", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            authority = MatchFusionTestData.WithPlayerOneState(authority, new[]
            {
                MatchFusionTestData.Unit("history-b-1", "1002", MatchUnitZone.Staging, 0, 0),
                MatchFusionTestData.Unit("history-b-2", "1002", MatchUnitZone.Staging, 0, 1)
            });

            MatchFusionTestData.PurchaseNext(authority, "buy-a", typeId: "1001");

            var history = authority.ProjectForPlayer("player-1").Owner.Units
                .Where(unit => unit.TypeId == "1002")
                .ToArray();
            Assert.That(history, Has.Length.EqualTo(2));
            Assert.That(history, Has.All.Matches<MatchUnitState>(unit => unit.EliteLevel == 0));
        }

        [Test]
        public void Purchase_WhenLaterEliteCostCannotBePaidRetreatsAndReleasesActualOccupiedCost()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1, baseDeploymentCost: 2));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var deployed = MatchFusionTestData.Unit(
                "external-deployed",
                "1001",
                MatchUnitZone.Deployed,
                0,
                0,
                new MatchFormationPosition(4, 2));
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                new[] { deployed },
                totalDeploymentCost: 4);

            MatchFusionTestData.PurchaseNext(authority, "cost-1");
            MatchFusionTestData.PurchaseNext(authority, "cost-2");
            var result = MatchFusionTestData.PurchaseNext(authority, "cost-3");

            var survivor = authority.ProjectForPlayer("player-1").Owner.Units.Single();
            Assert.That(survivor.UnitId, Is.EqualTo("external-deployed"));
            Assert.That(survivor.EliteLevel, Is.EqualTo(2));
            Assert.That(survivor.Zone, Is.EqualTo(MatchUnitZone.Staging));
            Assert.That(survivor.Formation, Is.Null);
            Assert.That(authority.ProjectForPlayer("player-1").Owner.AvailableDeploymentCost,
                Is.EqualTo(4), "E1 cost 4, not E0 cost 2, must be released on the failing step.");
            Assert.That(result.Acquisition.FinalZone, Is.EqualTo(MatchUnitZone.Staging));
        }

        [Test]
        public void Purchase_IdempotentRetryReturnsSameFusionDiagnosticsWithoutDoubleRetirement()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            MatchFusionTestData.PurchaseNext(authority, "first");
            var offer = authority.ProjectForPlayer("player-1").Owner.ShopOffers.First(item => !item.IsEmpty);
            var envelope = new MatchCommandEnvelope(
                "session-1",
                "player-1",
                "idempotent-fusion",
                authority.StateRevision,
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId));

            var first = authority.Execute(envelope);
            var repeated = authority.Execute(envelope);

            Assert.That(repeated, Is.SameAs(first));
            Assert.That(repeated.Acquisition.CanonicalSummary, Is.EqualTo(first.Acquisition.CanonicalSummary));
            Assert.That(authority.ProjectForHostAuthority().Pool.RetiredUnits, Has.Count.EqualTo(1));
        }

        [Test]
        public void MaxEliteThreeUnitsRemainDistinctAndThereIsNoPlayerFuseCommand()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            authority = MatchFusionTestData.WithPlayerOneState(authority, new[]
            {
                MatchFusionTestData.Unit("elite-3-a", "1001", MatchUnitZone.Staging, 3, 0),
                MatchFusionTestData.Unit("elite-3-b", "1001", MatchUnitZone.Staging, 3, 1)
            });

            MatchFusionTestData.PurchaseNext(authority, "max-three");

            Assert.That(authority.ProjectForPlayer("player-1").Owner.Units.Count(
                unit => unit.EliteLevel == 3), Is.EqualTo(2));
            Assert.That(typeof(MatchCommandPayload).Assembly.GetTypes()
                .Where(type => typeof(MatchCommandPayload).IsAssignableFrom(type))
                .Select(type => type.Name),
                Has.None.Contains("Fuse"));
        }

        [Test]
        public void TogglingReadyAloneDoesNotScanHistoricalFusionCandidates()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            authority = MatchFusionTestData.WithPlayerOneState(authority, new[]
            {
                MatchFusionTestData.Unit(
                    "ready-deployed",
                    "1001",
                    MatchUnitZone.Deployed,
                    0,
                    0,
                    new MatchFormationPosition(4, 2)),
                MatchFusionTestData.Unit(
                    "ready-staging",
                    "1001",
                    MatchUnitZone.Staging,
                    0,
                    1)
            });
            var before = authority.ProjectForHostAuthority().State.FindSeat("player-1")
                .Units.Select(unit => unit.CanonicalSummary).ToArray();

            MatchFusionTestData.Execute(
                authority,
                "player-1",
                "ready-on",
                new SetPreparationReadyCommand(true));
            MatchFusionTestData.Execute(
                authority,
                "player-1",
                "ready-off",
                new SetPreparationReadyCommand(false));

            Assert.That(authority.ProjectForHostAuthority().State.FindSeat("player-1")
                .Units.Select(unit => unit.CanonicalSummary), Is.EqualTo(before));
        }

        [Test]
        public void ConsumedPoolUnitNeverReturnsAndCannotBeAuthorizedAgain()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            MatchFusionTestData.PurchaseNext(authority, "retire-first");
            var fusion = MatchFusionTestData.PurchaseNext(authority, "retire-second");
            var retiredId = fusion.Acquisition.RetiredUnitIds.Single();
            var host = authority.ProjectForHostAuthority();

            Assert.That(host.Pool.Entities.Single(entity => entity.UnitId == retiredId).Location,
                Is.EqualTo(MatchPoolEntityLocation.ConsumedByFusion));
            Assert.That(host.Pool.Entities.Single(entity => entity.UnitId == retiredId).PlayerId,
                Is.Empty);
            Assert.That(host.Pool.RetiredUnits.Single(retired => retired.UnitId == retiredId).Reason,
                Is.EqualTo(MatchRetirementReason.FusionConsumed));

            var draft = new MatchEconomyTransactionDraft(host.State);
            Assert.That(draft.TryAcquireAuthorizedPersistentUnit(
                "player-1",
                new MatchAuthorizedPersistentUnit(
                    retiredId,
                    "1001",
                    0,
                    Array.Empty<MatchBuffState>()),
                out _,
                out var code,
                out _), Is.False);
            Assert.That(code, Is.EqualTo(MatchCommandCode.InvalidPayload));
        }

        [Test]
        public void Purchase_WhenAcquisitionOrdinalIsExhaustedRejectsAtomically()
        {
            var authority = MatchFusionTestData.CreateAuthority();
            authority.TryEnterPreparation(1);
            var host = authority.ProjectForHostAuthority();
            var exhaustedPool = host.Pool.With(
                host.Pool.Entities,
                host.Pool.RandomState,
                nextAcquisitionOrdinal: long.MaxValue);
            var exhaustedState = host.State.WithEconomy(
                host.State.Seats,
                exhaustedPool,
                false);
            authority = new MatchAuthority(
                exhaustedState,
                new StrictStagingSlotPolicy());
            var before = authority.ProjectForHostAuthority().State.CanonicalSummary;

            var result = MatchFusionTestData.PurchaseNext(authority, "ordinal-exhausted");

            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.InternalInvariantViolation));
            Assert.That(result.ChangedState, Is.False);
            Assert.That(authority.ProjectForHostAuthority().State.CanonicalSummary,
                Is.EqualTo(before));
        }

        [Test]
        public void PoolInvariantRejectsRetiredTombstoneWhileEntityRemainsAvailable()
        {
            var authority = MatchFusionTestData.CreateAuthority();
            var host = authority.ProjectForHostAuthority();
            var available = host.Pool.Entities.First(entity =>
                entity.Location == MatchPoolEntityLocation.AvailablePool);
            var invalidPool = host.Pool.With(
                host.Pool.Entities,
                host.Pool.RandomState,
                retiredUnits: new[]
                {
                    new MatchRetiredPersistentUnitState(
                        available.UnitId,
                        MatchRetirementReason.FusionConsumed)
                });
            var invalidState = host.State.WithEconomy(
                host.State.Seats,
                invalidPool,
                false);

            Assert.That(MatchStateInvariant.TryValidate(
                invalidState,
                new StrictStagingSlotPolicy(),
                out var diagnostic), Is.False);
            Assert.That(diagnostic, Is.EqualTo("match.invariant.pool.availableLocation"));
        }
    }
}
