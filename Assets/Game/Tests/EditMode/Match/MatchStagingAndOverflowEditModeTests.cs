using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchStagingAndOverflowEditModeTests
    {
        [Test]
        public void StagingProjection_UsesStrictBuffAwareKeysAndCanonicalOrdering()
        {
            var catalog = MatchFusionTestData.CatalogWithFillers(fillerCount: 2);
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            var units = new[]
            {
                MatchFusionTestData.Unit("z-unit", "1001", MatchUnitZone.Staging, 0, 0),
                MatchFusionTestData.Unit("a-unit", "1001", MatchUnitZone.Staging, 0, 1),
                MatchFusionTestData.Unit("elite-unit", "1001", MatchUnitZone.Staging, 1, 2),
                MatchFusionTestData.Unit("filler-unit", "7000", MatchUnitZone.Staging, 0, 3)
            };
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                units,
                targetedBuffs: new[]
                {
                    new PlayerTargetedUnitBuffState(
                        "owned-buff",
                        "owned-type",
                        "z-unit",
                        "payload",
                        MatchTargetedBuffDiscardPolicy.RemoveWithTarget)
                });
            var seat = authority.ProjectForHostAuthority().Seats.Single(
                candidate => candidate.PlayerId == "player-1");

            var stacks = MatchStagingProjection.Project(seat, catalog);

            Assert.That(stacks, Has.Count.EqualTo(4));
            Assert.That(stacks.Select(stack => stack.DeploymentCost),
                Is.Ordered.Ascending);
            Assert.That(stacks.Single(stack => stack.UnitIds.Contains("a-unit")).UnitIds,
                Is.EqualTo(new[] { "a-unit" }));
            Assert.That(stacks.Single(stack => stack.UnitIds.Contains("z-unit"))
                .BuffCanonicalSummary, Does.Contain("owned-buff"));
        }

        [Test]
        public void StagingProjection_StacksIdenticalUnitsAndCanPlaceIntoAFullMatchingStack()
        {
            var catalog = MatchFusionTestData.CatalogWithFillers();
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            var full = Enumerable.Range(0, 13)
                .Select(index => MatchFusionTestData.Unit(
                    "slot-" + index,
                    (7000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MatchUnitZone.Staging,
                    0,
                    index))
                .ToList();
            full.Add(MatchFusionTestData.Unit("same-stack", "7000", MatchUnitZone.Staging, 0, 13));
            authority = MatchFusionTestData.WithPlayerOneState(authority, full);
            var seat = authority.ProjectForHostAuthority().Seats.Single(
                candidate => candidate.PlayerId == "player-1");

            Assert.That(MatchStagingProjection.CountOccupiedSlots(seat, catalog), Is.EqualTo(13));
            Assert.That(MatchStagingProjection.Project(seat, catalog)
                .Single(stack => stack.TypeId == "7000").UnitIds,
                Is.EqualTo(new[] { "same-stack", "slot-0" }));
            Assert.That(MatchStagingProjection.CanPlaceInStaging(
                MatchFusionTestData.Unit("candidate", "7000", MatchUnitZone.Staging, 0, 14),
                seat,
                catalog), Is.True);
            Assert.That(MatchStagingProjection.CanPlaceInStaging(
                MatchFusionTestData.Unit("new-stack", "1001", MatchUnitZone.Staging, 0, 15),
                seat,
                catalog), Is.False);
        }

        [Test]
        public void Purchase_PrePurchaseFullCheckUsesAuthoritativeStrictStackProjection()
        {
            var catalog = MatchFusionTestData.CatalogWithFillers();
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var full = Enumerable.Range(0, 13)
                .Select(index => MatchFusionTestData.Unit(
                    "slot-" + index,
                    (7000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MatchUnitZone.Staging,
                    0,
                    index))
                .ToArray();
            authority = MatchFusionTestData.WithPlayerOneState(authority, full);
            var before = authority.ProjectForHostAuthority().State.CanonicalSummary;

            var result = MatchFusionTestData.PurchaseNext(authority, "full-purchase", typeId: "1001");

            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.StagingFull));
            Assert.That(authority.ProjectForHostAuthority().State.CanonicalSummary, Is.EqualTo(before));
        }

        [Test]
        public void AuthorizedAcquisition_UsesOverflowWhenStagingCannotAcceptAnotherStack()
        {
            var catalog = MatchFusionTestData.CatalogWithFillers();
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var full = Enumerable.Range(0, 13)
                .Select(index => MatchFusionTestData.Unit(
                    "slot-" + index,
                    (7000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MatchUnitZone.Staging,
                    0,
                    index))
                .ToArray();
            authority = MatchFusionTestData.WithPlayerOneState(authority, full);
            var draft = new MatchEconomyTransactionDraft(
                authority.ProjectForHostAuthority().State);

            var accepted = draft.TryAcquireAuthorizedPersistentUnit(
                "player-1",
                new MatchAuthorizedPersistentUnit(
                    "authorized-new",
                    "1001",
                    0,
                    Array.Empty<MatchBuffState>()),
                out var acquisition,
                out var code,
                out var diagnostic);
            var next = draft.BuildState(true);

            Assert.That(accepted, Is.True, diagnostic);
            Assert.That(code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(acquisition.FinalZone, Is.EqualTo(MatchUnitZone.Overflow));
            Assert.That(next.FindSeat("player-1").Units.Single(
                unit => unit.UnitId == "authorized-new").Zone, Is.EqualTo(MatchUnitZone.Overflow));
            Assert.That(MatchStagingProjection.CountOccupiedSlots(
                next.FindSeat("player-1"),
                catalog), Is.EqualTo(13));
        }

        [Test]
        public void OverflowPromotionTriesEveryUnitAndDoesNotTriggerUnrelatedFusion()
        {
            var catalog = MatchFusionTestData.CatalogWithFillers();
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var units = Enumerable.Range(0, 13)
                .Select(index => MatchFusionTestData.Unit(
                    "slot-" + index,
                    (7000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MatchUnitZone.Staging,
                    0,
                    index))
                .Concat(new[]
                {
                    MatchFusionTestData.Unit(
                        "overflow-earlier-no-fit",
                        "1001",
                        MatchUnitZone.Overflow,
                        0,
                        13),
                    MatchFusionTestData.Unit(
                        "overflow-later-stackable",
                        "7000",
                        MatchUnitZone.Overflow,
                        0,
                        14)
                })
                .ToArray();
            authority = MatchFusionTestData.WithPlayerOneState(authority, units);
            var draft = new MatchEconomyTransactionDraft(
                authority.ProjectForHostAuthority().State);

            Assert.That(draft.TryAcquireAuthorizedPersistentUnit(
                "player-1",
                new MatchAuthorizedPersistentUnit(
                    "authorized-stackable",
                    "7012",
                    0,
                    Array.Empty<MatchBuffState>()),
                out _,
                out _,
                out var diagnostic), Is.True, diagnostic);
            var next = draft.BuildState(true).FindSeat("player-1");

            Assert.That(next.Units.Single(unit => unit.UnitId == "overflow-earlier-no-fit").Zone,
                Is.EqualTo(MatchUnitZone.Overflow));
            Assert.That(next.Units.Single(unit => unit.UnitId == "overflow-later-stackable").Zone,
                Is.EqualTo(MatchUnitZone.Staging));
            Assert.That(next.Units.Count(unit => unit.TypeId == "7000" && unit.EliteLevel == 0),
                Is.EqualTo(2), "Promotion itself must not start another fusion scan.");
        }

        [Test]
        public void SealDiscardRejectsMissingBuffPolicyThenRemovesTargetedBuffAndRetiresInStableOrder()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var overflow = new[]
            {
                MatchFusionTestData.Unit("overflow-b", "1001", MatchUnitZone.Overflow, 0, 2),
                MatchFusionTestData.Unit("overflow-a", "1001", MatchUnitZone.Overflow, 0, 1)
            };
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                overflow,
                targetedBuffs: new[]
                {
                    new PlayerTargetedUnitBuffState(
                        "discard-buff",
                        "discard-type",
                        "overflow-a",
                        "payload",
                        MatchTargetedBuffDiscardPolicy.Unspecified)
                });
            authority.TryAdvancePhase(MatchPhase.Sealing);
            var before = authority.ProjectForHostAuthority().State.CanonicalSummary;

            var rejected = authority.TryDiscardRemainingOverflowAtSeal();

            Assert.That(rejected.Code, Is.EqualTo(MatchCommandCode.InvalidPayload));
            Assert.That(authority.ProjectForHostAuthority().State.CanonicalSummary, Is.EqualTo(before));

            var host = authority.ProjectForHostAuthority();
            var remappedSeat = host.State.FindSeat("player-1").With(
                targetedUnitBuffs: new[]
                {
                    new PlayerTargetedUnitBuffState(
                        "discard-buff",
                        "discard-type",
                        "overflow-a",
                        "payload",
                        MatchTargetedBuffDiscardPolicy.RemoveWithTarget)
                });
            var state = host.State.WithEconomy(
                host.State.Seats.Select(seat =>
                    seat.PlayerId == remappedSeat.PlayerId ? remappedSeat : seat),
                host.State.Pool,
                false);
            authority = new MatchAuthority(state, new StrictStagingSlotPolicy());

            var accepted = authority.TryDiscardRemainingOverflowAtSeal();

            Assert.That(accepted.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(accepted.RetiredUnitIds, Is.EqualTo(new[] { "overflow-a", "overflow-b" }));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.OverflowUnits, Is.Empty);
            Assert.That(authority.ProjectForPlayer("player-1").Owner.TargetedUnitBuffs, Is.Empty);
            Assert.That(authority.ProjectForHostAuthority().Pool.RetiredUnits,
                Has.All.Matches<MatchRetiredPersistentUnitState>(
                    retired => retired.Reason == MatchRetirementReason.OverflowDiscarded));
        }

        [TestCase(false, MatchUnitZone.Overflow)]
        [TestCase(true, MatchUnitZone.Staging)]
        public void DeployedFusionRetreatUsesStrictFullStagingProjection(
            bool hasMatchingEliteStack,
            MatchUnitZone expectedZone)
        {
            var catalog = MatchFusionTestData.CatalogWithFillers(
                maxEliteLevel: 1,
                baseDeploymentCost: 2);
            var staging = Enumerable.Range(0, 13)
                .Select(index => MatchFusionTestData.Unit(
                    "slot-" + index,
                    (7000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MatchUnitZone.Staging,
                    0,
                    index + 1))
                .ToList();
            if (hasMatchingEliteStack)
            {
                staging[0] = MatchFusionTestData.Unit(
                    "matching-elite-stack",
                    "1001",
                    MatchUnitZone.Staging,
                    1,
                    1);
            }
            staging.Add(MatchFusionTestData.Unit(
                "deployed-survivor",
                "1001",
                MatchUnitZone.Deployed,
                0,
                0,
                new MatchFormationPosition(4, 2)));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                staging,
                totalDeploymentCost: 2);
            var draft = new MatchEconomyTransactionDraft(
                authority.ProjectForHostAuthority().State);

            Assert.That(draft.TryAcquireAuthorizedPersistentUnit(
                "player-1",
                new MatchAuthorizedPersistentUnit(
                    "authorized-cost-test",
                    "1001",
                    0,
                    Array.Empty<MatchBuffState>()),
                out var acquisition,
                out _,
                out var diagnostic), Is.True, diagnostic);
            var next = draft.BuildState(true).FindSeat("player-1");
            var survivor = next.Units.Single(unit => unit.UnitId == "deployed-survivor");

            Assert.That(survivor.EliteLevel, Is.EqualTo(1));
            Assert.That(survivor.Zone, Is.EqualTo(expectedZone));
            Assert.That(survivor.Formation, Is.Null);
            Assert.That(next.AvailableDeploymentCost, Is.EqualTo(2));
            Assert.That(next.TotalDeploymentCost, Is.EqualTo(2));
            Assert.That(acquisition.FinalZone, Is.EqualTo(expectedZone));
            Assert.That(MatchStagingProjection.CountOccupiedSlots(next, catalog),
                Is.EqualTo(13));
        }
    }
}
