using System;
using System.Linq;
using ArknoNights.Match;
using NUnit.Framework;

namespace ArknoNights.MatchAI.Tests
{
    public sealed class BotAuthorityAndControllerEditModeTests
    {
        [Test]
        public void Observation_ChangingOpponentPrivateEconomy_DoesNotChangeIntent()
        {
            var authority = MatchAITestData.CreateAuthority();
            Assert.That(authority.TryEnterPreparation(1, 0).Accepted, Is.True);
            Assert.That(authority.TryProjectForBot(
                "player-2",
                out var beforeSource,
                out var diagnostic), Is.True, diagnostic);
            Assert.That(BotObservationFactory.TryCreate(
                beforeSource,
                0,
                0,
                out var before,
                out diagnostic), Is.True, diagnostic);

            var state = authority.ProjectForHostAuthority().State;
            var opponent = state.FindSeat("player-1");
            var alteredOffers = opponent.ShopOffers.Select(offer =>
                offer.IsEmpty
                    ? offer
                    : new MatchShopOfferState(
                        offer.SlotIndex,
                        offer.UnitId,
                        offer.TypeId,
                        offer.Rarity,
                        !offer.IsFrozen));
            var alteredOpponent = opponent.With(
                gold: 99,
                totalDeploymentCost: 99,
                availableDeploymentCost: 99,
                shopOffers: alteredOffers);
            var alteredState = state.WithEconomy(
                state.Seats.Select(seat =>
                    seat.PlayerId == alteredOpponent.PlayerId
                        ? alteredOpponent
                        : seat),
                state.Pool,
                false);
            var alteredAuthority = new MatchAuthority(
                alteredState,
                new StrictStagingSlotPolicy());
            Assert.That(alteredAuthority.TryProjectForBot(
                "player-2",
                out var afterSource,
                out diagnostic), Is.True, diagnostic);
            Assert.That(BotObservationFactory.TryCreate(
                afterSource,
                0,
                0,
                out var after,
                out diagnostic), Is.True, diagnostic);

            Assert.That(after.CanonicalSummary, Is.EqualTo(before.CanonicalSummary));
            Assert.That(
                BotDecisionMachine.Decide(after).CanonicalSummary,
                Is.EqualTo(BotDecisionMachine.Decide(before).CanonicalSummary));
            Assert.That(
                typeof(MatchBotScopedSnapshot).GetProperty("Pool"),
                Is.Null);
            Assert.That(
                typeof(MatchBotScopedSnapshot).GetProperty("MatchSeed"),
                Is.Null);
        }

        [Test]
        public void BotActionId_ReplayIsIdempotent_ConflictAndOldGenerationAreRejected()
        {
            var authority = MatchAITestData.CreateAuthority();
            authority.TryEnterPreparation(1, 0);
            Assert.That(authority.TryProjectForBot(
                "player-2",
                out var source,
                out var diagnostic), Is.True, diagnostic);
            var offer = source.ShopOffers.Last(item =>
                !item.IsEmpty && item.Price <= source.Gold);
            var actionId = new MatchBotActionId(
                source.SessionId,
                source.SeatIndex,
                source.ControllerGeneration,
                source.RoundNumber,
                0,
                MatchBotActionPart.Primary);
            var operation = new MatchBotOperationEnvelope(
                actionId,
                source.PlayerId,
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId));

            var first = authority.ExecuteBotOperation(operation);
            var revisionAfterFirst = authority.StateRevision;
            var goldAfterFirst = authority.ProjectForPlayer("player-2").Owner.Gold;
            var replay = authority.ExecuteBotOperation(operation);
            var conflict = authority.ExecuteBotOperation(
                new MatchBotOperationEnvelope(
                    actionId,
                    source.PlayerId,
                    new RefreshShopCommand()));

            Assert.That(first.Accepted, Is.True, first.DiagnosticCode);
            Assert.That(replay.CanonicalSummary, Is.EqualTo(first.CanonicalSummary));
            Assert.That(authority.StateRevision, Is.EqualTo(revisionAfterFirst));
            Assert.That(
                authority.ProjectForPlayer("player-2").Owner.Gold,
                Is.EqualTo(goldAfterFirst));
            Assert.That(conflict.Code, Is.EqualTo(MatchCommandCode.CommandIdConflict));

            var humanAuthority = MatchAITestData.CreateAuthority(
                seats: MatchAITestData.FourHumanSeats());
            humanAuthority.TryEnterPreparation(1, 0);
            Assert.That(humanAuthority.ActivateTakeoverBot("player-2").Accepted, Is.True);
            var generationOne = humanAuthority.ProjectBotControlSeats()
                .Single(item => item.PlayerId == "player-2");
            var stale = new MatchBotOperationEnvelope(
                new MatchBotActionId(
                    generationOne.SessionId,
                    generationOne.SeatIndex,
                    generationOne.ControllerGeneration,
                    generationOne.RoundNumber,
                    99,
                    MatchBotActionPart.Primary),
                generationOne.PlayerId,
                new RefreshShopCommand());
            Assert.That(humanAuthority.RestoreHumanControl("player-2").Accepted, Is.True);
            Assert.That(humanAuthority.ActivateTakeoverBot("player-2").Accepted, Is.True);

            var staleResult = humanAuthority.ExecuteBotOperation(stale);

            Assert.That(
                staleResult.Code,
                Is.EqualTo(MatchCommandCode.ControllerGenerationChanged));
            Assert.That(staleResult.ChangedState, Is.False);
        }

        [Test]
        public void DeploymentOrder_IsExactlyThirtyFiveCells_CenterOutAndSkipsGate()
        {
            var order = BotOperationAdapter.DeploymentOrder;

            Assert.That(order.Count, Is.EqualTo(35));
            Assert.That(order[0], Is.EqualTo(new MatchFormationPosition(5, 2)));
            Assert.That(order[1], Is.EqualTo(new MatchFormationPosition(4, 2)));
            Assert.That(order[8], Is.EqualTo(new MatchFormationPosition(9, 2)));
            Assert.That(order[27], Is.EqualTo(new MatchFormationPosition(4, 1)));
            Assert.That(
                order.Contains(new MatchFormationPosition(5, 1)),
                Is.False);
            Assert.That(order.Distinct().Count(), Is.EqualTo(35));
        }

        [Test]
        public void Controller_EntryTickThenCatchUp_UsesFixedOrdinalsAndBuyFollowUp()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(controller);

            Assert.That(authority.TryEnterPreparation(1, 0).Accepted, Is.True);
            Assert.That(
                controller.RuntimeStates.Select(state => state.NextDecisionOrdinal),
                Is.All.EqualTo(1));

            Assert.That(authority.AdvancePreparationClock(2500).Accepted, Is.True);
            Assert.That(
                controller.RuntimeStates.Select(state => state.NextDecisionOrdinal),
                Is.All.EqualTo(3));
            var bot = authority.ProjectForPlayer("player-2").Owner;
            var deployed = bot.Units.Single(unit =>
                unit.Zone == MatchUnitZone.Deployed);
            Assert.That(
                deployed.Formation,
                Is.EqualTo(new MatchFormationPosition(5, 2)));

            Assert.That(authority.AdvancePreparationClock(29000).Accepted, Is.True);
            Assert.That(
                controller.RuntimeStates.Select(state => state.NextDecisionOrdinal),
                Is.All.EqualTo(30));
            Assert.That(authority.AdvancePreparationClock(30000).Accepted, Is.True);
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Battle));
            Assert.That(
                controller.RuntimeStates.Select(state => state.NextDecisionOrdinal),
                Is.All.EqualTo(30));
        }

        [Test]
        public void Controller_TimeJumpAndOneSecondPumps_ProduceSameBotEconomy()
        {
            var jumpedController = new BotController();
            var jumped = MatchAITestData.CreateAuthority(jumpedController);
            jumped.TryEnterPreparation(1, 0);
            jumped.AdvancePreparationClock(29000);

            var pumpedController = new BotController();
            var pumped = MatchAITestData.CreateAuthority(pumpedController);
            pumped.TryEnterPreparation(1, 0);
            for (var now = 1000; now <= 29000; now += 1000)
            {
                Assert.That(pumped.AdvancePreparationClock(now).Accepted, Is.True);
            }

            for (var seat = 2; seat <= 4; seat++)
            {
                Assert.That(
                    jumped.ProjectForPlayer("player-" + seat).Owner.CanonicalSummary,
                    Is.EqualTo(
                        pumped.ProjectForPlayer("player-" + seat).Owner.CanonicalSummary));
            }
            Assert.That(
                jumpedController.CanonicalSummary,
                Is.EqualTo(pumpedController.CanonicalSummary));
        }

        [Test]
        public void LastHumanReady_SealsBeforeAnyQueuedBotTick()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(controller);
            authority.TryEnterPreparation(1, 0);
            var before = controller.CanonicalSummary;

            var ready = authority.Execute(
                MatchAITestData.Ready(authority, "player-1", "ready"));

            Assert.That(ready.Accepted, Is.True, ready.DiagnosticCode);
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Battle));
            Assert.That(
                authority.AdvancePreparationClock(1000).Accepted,
                Is.False);
            Assert.That(controller.CanonicalSummary, Is.EqualTo(before));
        }

        [Test]
        public void EliminatedOnlyHuman_AllowsBotsThenImmediateSeal()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(controller);
            Assert.That(authority.TryMarkEliminated("player-1", 4).Accepted, Is.True);

            var entered = authority.TryEnterPreparation(1, 0);

            Assert.That(entered.Accepted, Is.True, entered.DiagnosticCode);
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Battle));
            Assert.That(
                controller.RuntimeStates.Select(state => state.NextDecisionOrdinal),
                Is.All.EqualTo(1));
        }

        [Test]
        public void BuyWithInsufficientDeploymentCost_RemainsPurchasedInStaging()
        {
            var controller = new BotController();
            var standard = MatchAITestData.Request();
            var expensiveCatalog = new MatchShopCatalog(
                "rules-1",
                new string('a', 64),
                Enumerable.Range(1, 6).Select(index =>
                    new MatchShopCatalogEntry(
                        (1000 + index).ToString(),
                        1,
                        true,
                        3,
                        20,
                        1000 + index)));
            var request = new MatchInitializationRequest(
                standard.SessionId,
                standard.MatchSeed,
                standard.HostPlayerId,
                standard.CompatibilityManifest,
                expensiveCatalog,
                standard.Seats);
            var initialized = MatchSessionFactory.Create(
                request,
                new StrictStagingSlotPolicy(),
                controller);
            Assert.That(initialized.Success, Is.True, initialized.DiagnosticCode);
            var authority = initialized.Authority;
            authority.TryEnterPreparation(1, 0);
            authority.AdvancePreparationClock(1000);

            var owner = authority.ProjectForPlayer("player-2").Owner;

            Assert.That(owner.Units.Count, Is.EqualTo(1));
            Assert.That(owner.Units.Single().Zone, Is.EqualTo(MatchUnitZone.Staging));
            Assert.That(owner.Gold, Is.EqualTo(2));
            Assert.That(owner.AvailableDeploymentCost, Is.EqualTo(16));
        }

        [Test]
        public void EliminatedBotAndBattlePhase_DoNotExecuteAiOperations()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(controller);
            authority.TryEnterPreparation(1, 0);
            Assert.That(authority.TryMarkEliminated("player-2", 4).Accepted, Is.True);
            authority.AdvancePreparationClock(1000);

            Assert.That(
                controller.RuntimeStates.Single(state =>
                    state.PlayerId == "player-2").NextDecisionOrdinal,
                Is.EqualTo(1));
            Assert.That(
                controller.RuntimeStates.Where(state =>
                    state.PlayerId != "player-2")
                    .Select(state => state.NextDecisionOrdinal),
                Is.All.EqualTo(2));

            var state = authority.ProjectForHostAuthority().State;
            var battleState = state.Rebuild(
                state.StateRevision,
                MatchPhase.Battle,
                state.RoundNumber,
                state.Seats,
                state.Pool,
                state.EndReason,
                state.Flow);
            var battleAuthority = new MatchAuthority(
                battleState,
                new StrictStagingSlotPolicy(),
                controller);
            var bot = battleAuthority.ProjectBotControlSeats()
                .Single(seat => seat.PlayerId == "player-3");
            var goldBefore = battleAuthority.ProjectForPlayer("player-3").Owner.Gold;
            var rejected = battleAuthority.ExecuteBotOperation(
                new MatchBotOperationEnvelope(
                    new MatchBotActionId(
                        bot.SessionId,
                        bot.SeatIndex,
                        bot.ControllerGeneration,
                        bot.RoundNumber,
                        99,
                        MatchBotActionPart.Primary),
                    bot.PlayerId,
                    new RefreshShopCommand()));

            Assert.That(rejected.Code, Is.EqualTo(MatchCommandCode.PhaseRejected));
            Assert.That(rejected.ChangedState, Is.False);
            Assert.That(
                battleAuthority.ProjectForPlayer("player-3").Owner.Gold,
                Is.EqualTo(goldBefore));
        }

        [Test]
        public void PublicProjection_ContinuesToHideBotControllerIdentity()
        {
            var controller = new BotController();
            var authority = MatchAITestData.CreateAuthority(
                controller,
                MatchAITestData.TwoHumanSeats());
            authority.TryEnterPreparation(1, 0);
            controller.NotifyVoluntaryQuit(authority, "player-2");

            var publicSeat = authority.ProjectPublic().Seats.Single(seat =>
                seat.PlayerId == "player-2");

            Assert.That(publicSeat.ConnectionState, Is.EqualTo(PublicConnectionState.Online));
            Assert.That(
                typeof(PublicMatchSeatSnapshot).GetProperty("ControllerKind"),
                Is.Null);
        }
    }
}
