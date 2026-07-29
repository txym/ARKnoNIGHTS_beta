using System;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchShopRefreshEditModeTests
    {
        [Test]
        public void ActiveRefresh_InPreparation_DeductsOneGoldAndReplacesAllOffersAtomically()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var before = authority.ProjectForPlayer("player-1");
            var beforeIds = before.Owner.ShopOffers.Select(offer => offer.UnitId).ToArray();
            var beforeRevision = authority.StateRevision;

            var result = Execute(authority, "player-1", "refresh-1", new RefreshShopCommand());

            var after = authority.ProjectForPlayer("player-1");
            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(result.ChangedState, Is.True);
            Assert.That(authority.StateRevision, Is.EqualTo(beforeRevision + 1));
            Assert.That(after.Owner.Gold, Is.EqualTo(before.Owner.Gold - MatchEconomyRules.RefreshCost));
            Assert.That(after.Owner.ShopOffers.Select(offer => offer.UnitId), Is.Not.EqualTo(beforeIds));
            Assert.That(after.Owner.ShopOffers, Has.All.Matches<MatchShopOfferState>(offer => !offer.IsEmpty));
        }

        [Test]
        public void ActiveRefresh_WhenGoldIsInsufficient_LeavesFullHostStateAndRandomStateUnchanged()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            for (var index = 0; index < MatchInitialValues.Gold; index++)
            {
                Assert.That(
                    Execute(authority, "player-1", "spend-" + index, new RefreshShopCommand()).Code,
                    Is.EqualTo(MatchCommandCode.Accepted));
            }
            var before = authority.ProjectForHostAuthority();

            var result = Execute(authority, "player-1", "refresh-rejected", new RefreshShopCommand());

            var after = authority.ProjectForHostAuthority();
            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.InsufficientGold));
            Assert.That(result.ChangedState, Is.False);
            Assert.That(after.State.CanonicalSummary, Is.EqualTo(before.State.CanonicalSummary));
            Assert.That(after.Pool.RandomState.CanonicalSummary, Is.EqualTo(before.Pool.RandomState.CanonicalSummary));
        }

        [Test]
        public void ActiveRefresh_AllowsPreparationAndBattle_ButRejectsOtherPhases()
        {
            var initializing = MatchTestData.CreateAuthority();
            Assert.That(
                Execute(initializing, "player-1", "initializing", new RefreshShopCommand()).Code,
                Is.EqualTo(MatchCommandCode.PhaseRejected));

            initializing.TryEnterPreparation(1);
            Assert.That(
                Execute(initializing, "player-1", "preparation", new RefreshShopCommand()).Code,
                Is.EqualTo(MatchCommandCode.Accepted));
            initializing.TryAdvancePhase(MatchPhase.Sealing);
            Assert.That(
                Execute(initializing, "player-1", "sealing", new RefreshShopCommand()).Code,
                Is.EqualTo(MatchCommandCode.PhaseRejected));
            initializing.TryAdvancePhase(MatchPhase.Battle);
            Assert.That(
                Execute(initializing, "player-1", "battle", new RefreshShopCommand()).Code,
                Is.EqualTo(MatchCommandCode.Accepted));
        }

        [Test]
        public void ActiveRefresh_WhenNoEligibleWeightedPoolExists_IsAcceptedWithDiagnosticAndCharge()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("6001", 6));
            var authority = MatchSessionFactory.Create(MatchTestData.Request(
                shopCatalog: catalog,
                seats: MatchTestData.FourHumanSeats())).Authority;
            authority.TryEnterPreparation(1);
            var randomBefore = authority.ProjectForHostAuthority().Pool.RandomState.CanonicalSummary;

            var result = Execute(authority, "player-1", "exhausted", new RefreshShopCommand());

            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.PoolExhaustedDiagnostic));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ChangedState, Is.True);
            Assert.That(authority.ProjectForPlayer("player-1").Owner.Gold, Is.EqualTo(6));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.ShopOffers, Has.All.Matches<MatchShopOfferState>(
                offer => offer.IsEmpty));
            Assert.That(authority.ProjectForHostAuthority().Pool.RandomState.CanonicalSummary,
                Is.EqualTo(randomBefore));
        }

        [Test]
        public void ToggleFreeze_FreezesAllWhenAnyOfferIsUnfrozen_ThenUnfreezesAll()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);

            var freeze = Execute(authority, "player-1", "freeze", new ToggleShopFreezeCommand());
            var frozen = authority.ProjectForPlayer("player-1").Owner;
            var unfreeze = Execute(authority, "player-1", "unfreeze", new ToggleShopFreezeCommand());
            var unfrozen = authority.ProjectForPlayer("player-1").Owner;

            Assert.That(freeze.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(frozen.ShopOffers, Has.All.Matches<MatchShopOfferState>(offer => offer.IsFrozen));
            Assert.That(frozen.PreparationBehavior.HasIssuedEffectiveFreezeThisRound, Is.True);
            Assert.That(unfreeze.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(unfrozen.ShopOffers, Has.All.Matches<MatchShopOfferState>(offer => !offer.IsFrozen));
            Assert.That(unfrozen.PreparationBehavior.HasIssuedEffectiveFreezeThisRound, Is.True);
        }

        [Test]
        public void ToggleFreeze_WhenShopIsEmpty_IsSuccessfulNoOp()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("6001", 6));
            var authority = MatchSessionFactory.Create(
                MatchTestData.Request(
                    shopCatalog: catalog,
                    seats: MatchTestData.FourHumanSeats())).Authority;
            authority.TryEnterPreparation(1);
            var before = authority.ProjectForHostAuthority().State.CanonicalSummary;
            var revision = authority.StateRevision;

            var result = Execute(authority, "player-1", "empty-freeze", new ToggleShopFreezeCommand());

            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.AcceptedNoChange));
            Assert.That(result.ChangedState, Is.False);
            Assert.That(authority.StateRevision, Is.EqualTo(revision));
            Assert.That(authority.ProjectForHostAuthority().State.CanonicalSummary, Is.EqualTo(before));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.PreparationBehavior
                .HasIssuedEffectiveFreezeThisRound, Is.False);
        }

        [Test]
        public void ToggleFreeze_InBattle_DoesNotSetPreparationFreezeFact()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            authority.TryAdvancePhase(MatchPhase.Sealing);
            authority.TryAdvancePhase(MatchPhase.Battle);

            var result = Execute(authority, "player-1", "battle-freeze", new ToggleShopFreezeCommand());

            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.PreparationBehavior
                .HasIssuedEffectiveFreezeThisRound, Is.False);
        }

        [Test]
        public void PostBattleNaturalRefresh_IsOneAtomicRotatingIdempotentBatch()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            authority.TryAdvancePhase(MatchPhase.Sealing);
            authority.TryAdvancePhase(MatchPhase.Battle);
            authority.TryAdvancePhase(MatchPhase.Settlement);
            var before = authority.ProjectForHostAuthority();
            var changedEvents = 0;
            authority.Changed += _ => changedEvents++;

            var applied = authority.TryApplyPostBattleNaturalRefresh(1);
            var after = authority.ProjectForHostAuthority();
            var duplicate = authority.TryApplyPostBattleNaturalRefresh(1);

            Assert.That(applied.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(applied.ChangedState, Is.True);
            Assert.That(after.StateRevision, Is.EqualTo(before.StateRevision + 1));
            Assert.That(after.Pool.AppliedPostBattleRefreshRounds, Is.EqualTo(new[] { 1 }));
            Assert.That(after.Pool.NextNaturalRefreshStartSeat, Is.EqualTo(3));
            Assert.That(after.Seats, Has.All.Matches<MatchSeatState>(
                seat => seat.UpgradeDiscountCountAtThisLevel == 1));
            Assert.That(changedEvents, Is.EqualTo(1));
            Assert.That(duplicate.Code, Is.EqualTo(MatchCommandCode.NaturalRefreshAlreadyApplied));
            Assert.That(duplicate.ChangedState, Is.False);
            Assert.That(authority.StateRevision, Is.EqualTo(after.StateRevision));
        }

        [Test]
        public void ActiveRefresh_RetryWithSameCommandId_ReturnsCachedResultWithoutSecondCharge()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var envelope = Envelope(authority, "player-1", "same", new RefreshShopCommand());

            var first = authority.Execute(envelope);
            var second = authority.Execute(envelope);
            var conflict = authority.Execute(
                Envelope(authority, "player-1", "same", new ToggleShopFreezeCommand()));

            Assert.That(second, Is.SameAs(first));
            Assert.That(authority.ProjectForPlayer("player-1").Owner.Gold, Is.EqualTo(6));
            Assert.That(conflict.Code, Is.EqualTo(MatchCommandCode.CommandIdConflict));
        }

        [Test]
        public void ActiveRefresh_PreservesFrozenPhysicalSlotsAndOnlyFillsThePurchasedEmptySlot()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchSessionFactory.Create(MatchTestData.Request(
                shopCatalog: catalog,
                seats: MatchTestData.FourHumanSeats())).Authority;
            authority.TryEnterPreparation(1);
            Execute(authority, "player-1", "freeze", new ToggleShopFreezeCommand());
            var purchased = authority.ProjectForPlayer("player-1").Owner.ShopOffers.First();
            Execute(
                authority,
                "player-1",
                "purchase",
                new PurchaseShopOfferCommand(purchased.SlotIndex, purchased.UnitId));
            var retained = authority.ProjectForPlayer("player-1").Owner.ShopOffers
                .Where(offer => !offer.IsEmpty)
                .ToDictionary(offer => offer.SlotIndex, offer => offer.UnitId);

            var result = Execute(authority, "player-1", "refresh", new RefreshShopCommand());
            var after = authority.ProjectForPlayer("player-1").Owner;

            Assert.That(result.Accepted, Is.True);
            Assert.That(after.ShopOffers.Single(offer => offer.SlotIndex == purchased.SlotIndex).IsEmpty, Is.False);
            foreach (var pair in retained)
            {
                var offer = after.ShopOffers.Single(item => item.SlotIndex == pair.Key);
                Assert.That(offer.UnitId, Is.EqualTo(pair.Value));
                Assert.That(offer.IsFrozen, Is.True);
            }
        }

        [Test]
        public void PostBattleNaturalRefresh_StartsAtSeatTwoAndInterleavesExactCopiesBySlot()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchSessionFactory.Create(MatchTestData.Request(
                shopCatalog: catalog,
                seats: MatchTestData.FourHumanSeats())).Authority;
            authority.TryEnterPreparation(1);
            authority.TryAdvancePhase(MatchPhase.Sealing);
            authority.TryAdvancePhase(MatchPhase.Battle);
            authority.TryAdvancePhase(MatchPhase.Settlement);

            authority.TryApplyPostBattleNaturalRefresh(1);

            var host = authority.ProjectForHostAuthority();
            var expectedFirstCopyBySeat = new[] { 4, 1, 2, 3 };
            for (var seatIndex = 1; seatIndex <= 4; seatIndex++)
            {
                var seat = host.Seats.Single(item => item.SeatIndex == seatIndex);
                var copies = seat.ShopOffers
                    .OrderBy(offer => offer.SlotIndex)
                    .Select(offer => host.Pool.Entities.Single(
                        entity => entity.UnitId == offer.UnitId).CopyIndex)
                    .ToArray();
                Assert.That(copies, Is.EqualTo(Enumerable.Range(0, 6)
                    .Select(offset => expectedFirstCopyBySeat[seatIndex - 1] + offset * 4)
                    .ToArray()));
            }
        }

        [Test]
        public void PostBattleNaturalRefresh_ExcludesEliminatedSeatWithoutReturningItsCardsOrDiscountingIt()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var eliminatedShop = authority.ProjectForPlayer("player-2").Owner.ShopOffers
                .Select(offer => offer.CanonicalSummary)
                .ToArray();
            authority.TryMarkEliminated("player-2", 4);
            authority.TryAdvancePhase(MatchPhase.Sealing);
            authority.TryAdvancePhase(MatchPhase.Battle);
            authority.TryAdvancePhase(MatchPhase.Settlement);

            authority.TryApplyPostBattleNaturalRefresh(1);

            var eliminated = authority.ProjectForHostAuthority().Seats.Single(
                seat => seat.PlayerId == "player-2");
            Assert.That(
                eliminated.ShopOffers.Select(offer => offer.CanonicalSummary),
                Is.EqualTo(eliminatedShop));
            Assert.That(eliminated.UpgradeDiscountCountAtThisLevel, Is.Zero);
            Assert.That(authority.ProjectForHostAuthority().Pool.NextNaturalRefreshStartSeat, Is.EqualTo(3));
        }

        [Test]
        public void ActiveRefresh_ScarceLastCopiesFollowHostCommandAcceptanceOrder()
        {
            var playerOneFirst = CreateScarceCompetitionAuthority();
            Assert.That(Execute(
                playerOneFirst,
                "player-1",
                "p1-first",
                new RefreshShopCommand()).Accepted, Is.True);
            Assert.That(Execute(
                playerOneFirst,
                "player-2",
                "p2-second",
                new RefreshShopCommand()).Code, Is.EqualTo(MatchCommandCode.PoolExhaustedDiagnostic));

            var playerTwoFirst = CreateScarceCompetitionAuthority();
            Assert.That(Execute(
                playerTwoFirst,
                "player-2",
                "p2-first",
                new RefreshShopCommand()).Accepted, Is.True);
            Assert.That(Execute(
                playerTwoFirst,
                "player-1",
                "p1-second",
                new RefreshShopCommand()).Code, Is.EqualTo(MatchCommandCode.PoolExhaustedDiagnostic));

            Assert.That(playerOneFirst.ProjectForPlayer("player-1").Owner.ShopOffers.Count(
                offer => offer.IsEmpty), Is.Zero);
            Assert.That(playerOneFirst.ProjectForPlayer("player-2").Owner.ShopOffers.Count(
                offer => offer.IsEmpty), Is.EqualTo(1));
            Assert.That(playerTwoFirst.ProjectForPlayer("player-2").Owner.ShopOffers.Count(
                offer => offer.IsEmpty), Is.Zero);
            Assert.That(playerTwoFirst.ProjectForPlayer("player-1").Owner.ShopOffers.Count(
                offer => offer.IsEmpty), Is.EqualTo(1));
        }

        private static MatchAuthority CreateScarceCompetitionAuthority()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchSessionFactory.Create(MatchTestData.Request(
                shopCatalog: catalog,
                seats: MatchTestData.FourHumanSeats())).Authority;
            authority.TryEnterPreparation(1);
            Execute(authority, "player-1", "freeze-p1", new ToggleShopFreezeCommand());
            Execute(authority, "player-2", "freeze-p2", new ToggleShopFreezeCommand());
            for (var index = 0; index < 3; index++)
            {
                PurchaseFirst(authority, "player-1", "buy-p1-" + index);
            }
            for (var index = 0; index < 2; index++)
            {
                PurchaseFirst(authority, "player-2", "buy-p2-" + index);
            }
            return authority;
        }

        private static void PurchaseFirst(
            MatchAuthority authority,
            string playerId,
            string commandId)
        {
            var offer = authority.ProjectForPlayer(playerId).Owner.ShopOffers.First(
                item => !item.IsEmpty);
            var result = Execute(
                authority,
                playerId,
                commandId,
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId));
            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.Accepted));
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
    }
}
