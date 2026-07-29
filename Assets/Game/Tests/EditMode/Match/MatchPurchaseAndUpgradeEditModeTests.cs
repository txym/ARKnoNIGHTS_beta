using System;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchPurchaseAndUpgradeEditModeTests
    {
        [Test]
        public void Purchase_UsesRarityPriceAndMovesTheSamePoolEntityToStaging()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            Execute(authority, "player-1", "freeze", new ToggleShopFreezeCommand());
            var before = authority.ProjectForPlayer("player-1").Owner;
            var offer = before.ShopOffers.First();
            var revision = authority.StateRevision;

            var result = Execute(
                authority,
                "player-1",
                "purchase",
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId));

            var owner = authority.ProjectForPlayer("player-1").Owner;
            var host = authority.ProjectForHostAuthority();
            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.StateRevision, Is.EqualTo(revision + 1));
            Assert.That(owner.Gold, Is.EqualTo(before.Gold - offer.Rarity.Value));
            Assert.That(owner.ShopOffers.Single(item => item.SlotIndex == offer.SlotIndex).IsEmpty, Is.True);
            Assert.That(owner.ShopOffers.Single(item => item.SlotIndex == offer.SlotIndex).IsFrozen, Is.False);
            Assert.That(owner.Units.Single(unit => unit.UnitId == offer.UnitId).Zone, Is.EqualTo(MatchUnitZone.Staging));
            Assert.That(owner.Units.Single(unit => unit.UnitId == offer.UnitId).EliteLevel, Is.Zero);
            Assert.That(owner.Units.Single(unit => unit.UnitId == offer.UnitId).AcquisitionOrdinal, Is.Zero);
            Assert.That(host.Pool.Entities.Single(entity => entity.UnitId == offer.UnitId).Location,
                Is.EqualTo(MatchPoolEntityLocation.OwnedUnit));
            Assert.That(owner.PreparationBehavior.SuccessfulShopPurchaseCount, Is.EqualTo(1));
        }

        [Test]
        public void Purchase_RejectsInvalidEmptyAndChangedOffersWithoutMutatingStateOrRandom()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var offer = authority.ProjectForPlayer("player-1").Owner.ShopOffers.First();
            var before = authority.ProjectForHostAuthority();

            AssertRejectedWithoutStateChange(
                authority,
                before,
                "invalid-slot",
                new PurchaseShopOfferCommand(0, offer.UnitId),
                MatchCommandCode.ShopSlotInvalid);
            AssertRejectedWithoutStateChange(
                authority,
                before,
                "changed",
                new PurchaseShopOfferCommand(offer.SlotIndex, "different-unit"),
                MatchCommandCode.ShopOfferChanged);

            Assert.That(Execute(
                authority,
                "player-1",
                "purchase",
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId)).Code,
                Is.EqualTo(MatchCommandCode.Accepted));
            var afterPurchase = authority.ProjectForHostAuthority();
            AssertRejectedWithoutStateChange(
                authority,
                afterPurchase,
                "empty",
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId),
                MatchCommandCode.ShopOfferMissing);
        }

        [Test]
        public void Purchase_UsesReplaceablePrePurchaseStagingSlotPolicyAtTwelveAndThirteen()
        {
            var atTwelve = CreateAuthority(new FixedSlotPolicy(12));
            atTwelve.TryEnterPreparation(1);
            var twelveOffer = atTwelve.ProjectForPlayer("player-1").Owner.ShopOffers.First();
            Assert.That(Execute(
                atTwelve,
                "player-1",
                "at-12",
                new PurchaseShopOfferCommand(twelveOffer.SlotIndex, twelveOffer.UnitId)).Code,
                Is.EqualTo(MatchCommandCode.Accepted));

            var atThirteen = CreateAuthority(new FixedSlotPolicy(13));
            atThirteen.TryEnterPreparation(1);
            var thirteenOffer = atThirteen.ProjectForPlayer("player-1").Owner.ShopOffers.First();
            var before = atThirteen.ProjectForHostAuthority();
            AssertRejectedWithoutStateChange(
                atThirteen,
                before,
                "at-13",
                new PurchaseShopOfferCommand(thirteenOffer.SlotIndex, thirteenOffer.UnitId),
                MatchCommandCode.StagingFull);
        }

        [Test]
        public void Purchase_IdempotencyDoesNotChargeOrAcquireTwice()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var offer = authority.ProjectForPlayer("player-1").Owner.ShopOffers.First();
            var envelope = Envelope(
                authority,
                "player-1",
                "same-purchase",
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId));

            var first = authority.Execute(envelope);
            var repeated = authority.Execute(envelope);
            var conflict = authority.Execute(new MatchCommandEnvelope(
                "session-1",
                "player-1",
                "same-purchase",
                envelope.KnownStateRevision,
                new PurchaseShopOfferCommand(offer.SlotIndex, "different-unit")));

            Assert.That(repeated, Is.SameAs(first));
            Assert.That(conflict.Code, Is.EqualTo(MatchCommandCode.CommandIdConflict));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.Units.Count(
                unit => unit.UnitId == offer.UnitId), Is.EqualTo(1));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.PreparationBehavior
                .SuccessfulShopPurchaseCount, Is.EqualTo(1));
        }

        [Test]
        public void UpgradePricing_HasTheExactConfirmedCurve()
        {
            Assert.That(
                Enumerable.Range(1, 8).Select(MatchUpgradePricing.GetBasePrice),
                Is.EqualTo(new[] { 4, 6, 9, 13, 18, 24, 31, 39 }));
        }

        [Test]
        public void Upgrade_ValidatesSemanticLevelAndPrice_ThenClearsOldLevelDiscount()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var before = authority.ProjectForPlayer("player-1").Owner;

            Assert.That(Execute(
                authority,
                "player-1",
                "stale-level",
                new PurchaseLevelUpgradeCommand(2, before.CurrentUpgradePrice)).Code,
                Is.EqualTo(MatchCommandCode.UpgradeLevelChanged));
            Assert.That(Execute(
                authority,
                "player-1",
                "stale-price",
                new PurchaseLevelUpgradeCommand(1, before.CurrentUpgradePrice - 1)).Code,
                Is.EqualTo(MatchCommandCode.UpgradePriceChanged));

            var result = Execute(
                authority,
                "player-1",
                "upgrade",
                new PurchaseLevelUpgradeCommand(before.Level, before.CurrentUpgradePrice));
            var after = authority.ProjectForPlayer("player-1").Owner;

            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(after.Level, Is.EqualTo(2));
            Assert.That(after.Gold, Is.EqualTo(3));
            Assert.That(after.UpgradeDiscountCountAtThisLevel, Is.Zero);
            Assert.That(after.CurrentUpgradePrice, Is.EqualTo(6));
        }

        [Test]
        public void Upgrade_AfterBattleDiscountCanReachZeroAndStillChangesRevision()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            for (var round = 1; round <= 4; round++)
            {
                EnterSettlement(authority, round);
                Assert.That(authority.TryApplyPostBattleNaturalRefresh(round).Accepted, Is.True);
            }
            authority.TryEnterPreparation(5);
            var owner = authority.ProjectForPlayer("player-1").Owner;
            var revision = authority.StateRevision;

            var result = Execute(
                authority,
                "player-1",
                "free-upgrade",
                new PurchaseLevelUpgradeCommand(owner.Level, owner.CurrentUpgradePrice));

            Assert.That(owner.CurrentUpgradePrice, Is.Zero);
            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.StateRevision, Is.EqualTo(revision + 1));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.Gold, Is.EqualTo(owner.Gold));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.Level, Is.EqualTo(2));
        }

        [Test]
        public void Upgrade_InBattleAppliesFollowingNaturalRefreshDiscountToTheNewLevel()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            authority.TryAdvancePhase(MatchPhase.Sealing);
            authority.TryAdvancePhase(MatchPhase.Battle);
            Assert.That(Execute(
                authority,
                "player-1",
                "battle-upgrade",
                new PurchaseLevelUpgradeCommand(1, 4)).Code,
                Is.EqualTo(MatchCommandCode.Accepted));
            authority.TryAdvancePhase(MatchPhase.Settlement);

            authority.TryApplyPostBattleNaturalRefresh(1);

            var owner = authority.ProjectForPlayer("player-1").Owner;
            Assert.That(owner.Level, Is.EqualTo(2));
            Assert.That(owner.UpgradeDiscountCountAtThisLevel, Is.EqualTo(1));
            Assert.That(owner.CurrentUpgradePrice, Is.EqualTo(5));
        }

        [Test]
        public void EconomyCommands_RejectDisconnectedAndEliminatedHumans_ButAllowNativeBotAuthority()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            authority.TrySetConnectionState("player-1", MatchConnectionState.DisconnectedGrace);
            Assert.That(Execute(
                authority,
                "player-1",
                "disconnected",
                new RefreshShopCommand()).Code,
                Is.EqualTo(MatchCommandCode.ConnectionRejected));
            authority.TryMarkEliminated("player-2", 4);
            Assert.That(Execute(
                authority,
                "player-2",
                "eliminated",
                new PurchaseLevelUpgradeCommand(1, 4)).Code,
                Is.EqualTo(MatchCommandCode.Eliminated));

            var botAuthority = MatchTestData.CreateAuthority();
            botAuthority.TryEnterPreparation(1);
            Assert.That(Execute(
                botAuthority,
                "player-2",
                "bot-refresh",
                new RefreshShopCommand()).Accepted,
                Is.True);
        }

        [Test]
        public void PurchaseAndUpgrade_RejectInitializingAndSealingPhases()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            var initialOffer = authority.ProjectForPlayer("player-1").Owner.ShopOffers.First();
            Assert.That(Execute(
                authority,
                "player-1",
                "purchase-initializing",
                new PurchaseShopOfferCommand(initialOffer.SlotIndex, initialOffer.UnitId)).Code,
                Is.EqualTo(MatchCommandCode.PhaseRejected));
            Assert.That(Execute(
                authority,
                "player-1",
                "upgrade-initializing",
                new PurchaseLevelUpgradeCommand(1, 4)).Code,
                Is.EqualTo(MatchCommandCode.PhaseRejected));

            authority.TryEnterPreparation(1);
            authority.TryAdvancePhase(MatchPhase.Sealing);
            var sealingOffer = authority.ProjectForPlayer("player-1").Owner.ShopOffers.First();
            Assert.That(Execute(
                authority,
                "player-1",
                "purchase-sealing",
                new PurchaseShopOfferCommand(sealingOffer.SlotIndex, sealingOffer.UnitId)).Code,
                Is.EqualTo(MatchCommandCode.PhaseRejected));
            Assert.That(Execute(
                authority,
                "player-1",
                "upgrade-sealing",
                new PurchaseLevelUpgradeCommand(1, 4)).Code,
                Is.EqualTo(MatchCommandCode.PhaseRejected));
        }

        [Test]
        public void Upgrade_InsufficientGoldAndIdempotentRetryPreserveAtomicity()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var firstEnvelope = Envelope(
                authority,
                "player-1",
                "upgrade-once",
                new PurchaseLevelUpgradeCommand(1, 4));
            var first = authority.Execute(firstEnvelope);
            var repeated = authority.Execute(firstEnvelope);
            var beforeRejected = authority.ProjectForHostAuthority();

            var rejected = Execute(
                authority,
                "player-1",
                "upgrade-no-gold",
                new PurchaseLevelUpgradeCommand(2, 6));

            Assert.That(first.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(repeated, Is.SameAs(first));
            Assert.That(rejected.Code, Is.EqualTo(MatchCommandCode.InsufficientGold));
            Assert.That(authority.ProjectForHostAuthority().State.CanonicalSummary,
                Is.EqualTo(beforeRejected.State.CanonicalSummary));
        }

        [Test]
        public void Upgrade_AtLevelNine_IsRejectedWithoutStateChange()
        {
            var initial = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats())
                .ProjectForHostAuthority().State;
            var levelNineState = initial.WithEconomy(
                initial.Seats.Select(seat => seat.PlayerId == "player-1"
                    ? seat.With(level: 9, upgradeDiscountCountAtThisLevel: 0)
                    : seat),
                initial.Pool,
                false);
            var authority = new MatchAuthority(levelNineState, new StrictStagingSlotPolicy());
            authority.TryEnterPreparation(1);
            var before = authority.ProjectForHostAuthority();

            var result = Execute(
                authority,
                "player-1",
                "level-max",
                new PurchaseLevelUpgradeCommand(9, 0));

            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.LevelMax));
            Assert.That(authority.ProjectForHostAuthority().State.CanonicalSummary,
                Is.EqualTo(before.State.CanonicalSummary));
        }

        private static MatchAuthority CreateAuthority(IStagingSlotPolicy policy)
        {
            var result = MatchSessionFactory.Create(
                MatchTestData.Request(MatchTestData.FourHumanSeats()),
                policy);
            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            return result.Authority;
        }

        private static void EnterSettlement(MatchAuthority authority, int round)
        {
            Assert.That(authority.TryEnterPreparation(round).Accepted, Is.True);
            Assert.That(authority.TryAdvancePhase(MatchPhase.Sealing).Accepted, Is.True);
            Assert.That(authority.TryAdvancePhase(MatchPhase.Battle).Accepted, Is.True);
            Assert.That(authority.TryAdvancePhase(MatchPhase.Settlement).Accepted, Is.True);
        }

        private static void AssertRejectedWithoutStateChange(
            MatchAuthority authority,
            HostMatchSnapshot before,
            string commandId,
            MatchCommandPayload payload,
            MatchCommandCode expectedCode)
        {
            var result = Execute(authority, "player-1", commandId, payload);
            var after = authority.ProjectForHostAuthority();
            Assert.That(result.Code, Is.EqualTo(expectedCode));
            Assert.That(result.ChangedState, Is.False);
            Assert.That(after.State.CanonicalSummary, Is.EqualTo(before.State.CanonicalSummary));
            Assert.That(after.Pool.RandomState.CanonicalSummary, Is.EqualTo(before.Pool.RandomState.CanonicalSummary));
        }

        private static MatchCommandResult Execute(
            MatchAuthority authority,
            string playerId,
            string commandId,
            MatchCommandPayload payload)
        {
            return authority.Execute(Envelope(authority, playerId, commandId, payload));
        }

        private static MatchCommandEnvelope Envelope(
            MatchAuthority authority,
            string playerId,
            string commandId,
            MatchCommandPayload payload)
        {
            return new MatchCommandEnvelope(
                "session-1",
                playerId,
                commandId,
                authority.StateRevision,
                payload);
        }

        private sealed class FixedSlotPolicy : IStagingSlotPolicy
        {
            private readonly int occupiedSlots;

            internal FixedSlotPolicy(int occupiedSlots)
            {
                this.occupiedSlots = occupiedSlots;
            }

            public int CountOccupiedSlots(System.Collections.Generic.IReadOnlyList<MatchUnitState> units)
            {
                return occupiedSlots;
            }
        }
    }
}
