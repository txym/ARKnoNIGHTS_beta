using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Match;
using NUnit.Framework;

namespace ArknoNights.MatchAI.Tests
{
    public sealed class BotDecisionMachineEditModeTests
    {
        [TestCase(MatchPhase.Battle, MatchControllerKind.NativeBot, false, false)]
        [TestCase(MatchPhase.Preparation, MatchControllerKind.Human, false, false)]
        [TestCase(MatchPhase.Preparation, MatchControllerKind.NativeBot, true, false)]
        [TestCase(MatchPhase.Preparation, MatchControllerKind.NativeBot, false, true)]
        public void Decide_IneligibleObservation_Waits(
            MatchPhase phase,
            MatchControllerKind controller,
            bool eliminated,
            bool allHumansReady)
        {
            var observation = CreateObservation(
                phase: phase,
                controller: controller,
                eliminated: eliminated,
                humans: allHumansReady
                    ? new[] { new BotHumanReadinessObservation("human", true, true, false) }
                    : Array.Empty<BotHumanReadinessObservation>());

            var intent = BotDecisionMachine.Decide(observation);

            Assert.That(intent.Kind, Is.EqualTo(BotIntentKind.Wait));
        }

        [Test]
        public void Decide_PrefersDeployableThenRightmostCandidate()
        {
            var offers = EmptyOffers();
            offers[1] = Offer(2, "unit-2", 2, 20);
            offers[3] = Offer(4, "unit-4", 2, 3);
            offers[5] = Offer(6, "unit-6", 2, 3);
            var observation = CreateObservation(gold: 3, availableCost: 5, offers: offers);

            var intent = BotDecisionMachine.Decide(observation);

            Assert.That(intent.Kind, Is.EqualTo(BotIntentKind.Buy));
            Assert.That(intent.SlotIndex, Is.EqualTo(6));
            Assert.That(intent.ExpectedUnitId, Is.EqualTo("unit-6"));
        }

        [Test]
        public void Decide_WhenNoCandidateCanDeploy_StillBuysRightmostAffordable()
        {
            var offers = EmptyOffers();
            offers[2] = Offer(3, "unit-3", 2, 20);
            offers[4] = Offer(5, "unit-5", 2, 21);
            var observation = CreateObservation(gold: 2, availableCost: 0, offers: offers);

            var intent = BotDecisionMachine.Decide(observation);

            Assert.That(intent.Kind, Is.EqualTo(BotIntentKind.Buy));
            Assert.That(intent.SlotIndex, Is.EqualTo(5));
        }

        [TestCase(7, BotIntentKind.Buy)]
        [TestCase(8, BotIntentKind.Upgrade)]
        public void Decide_UpgradeReserveThreshold_UsesExactEquality(
            int gold,
            BotIntentKind expected)
        {
            var offers = EmptyOffers();
            offers[0] = Offer(1, "unit-1", 2, 1);
            var observation = CreateObservation(
                gold: gold,
                level: 1,
                upgradePrice: 4,
                offers: offers);

            var intent = BotDecisionMachine.Decide(observation);

            Assert.That(intent.Kind, Is.EqualTo(expected));
        }

        [Test]
        public void Decide_NoCandidate_UpgradeThenRefreshThenWait()
        {
            var upgrade = BotDecisionMachine.Decide(CreateObservation(
                gold: 4,
                upgradePrice: 4,
                offers: EmptyOffers()));
            var refresh = BotDecisionMachine.Decide(CreateObservation(
                gold: 2,
                upgradePrice: 4,
                offers: EmptyOffers()));
            var wait = BotDecisionMachine.Decide(CreateObservation(
                gold: 1,
                upgradePrice: 4,
                offers: EmptyOffers()));

            Assert.That(upgrade.Kind, Is.EqualTo(BotIntentKind.Upgrade));
            Assert.That(refresh.Kind, Is.EqualTo(BotIntentKind.Refresh));
            Assert.That(wait.Kind, Is.EqualTo(BotIntentKind.Wait));
        }

        [Test]
        public void Decide_FullStagingStillAllowsUpgradeButNeverBuyOrRefresh()
        {
            var offers = EmptyOffers();
            offers[5] = Offer(6, "unit-6", 1, 1);

            var upgrade = BotDecisionMachine.Decide(CreateObservation(
                gold: 4,
                upgradePrice: 4,
                stagingUsage: 13,
                offers: offers));
            var wait = BotDecisionMachine.Decide(CreateObservation(
                gold: 3,
                upgradePrice: 4,
                stagingUsage: 13,
                offers: offers));

            Assert.That(upgrade.Kind, Is.EqualTo(BotIntentKind.Upgrade));
            Assert.That(wait.Kind, Is.EqualTo(BotIntentKind.Wait));
        }

        [Test]
        public void Decide_LevelNineHasNoUpgrade_AndIsDeterministic()
        {
            var observation = CreateObservation(
                gold: 2,
                level: 9,
                upgradePrice: null,
                offers: EmptyOffers());

            var first = BotDecisionMachine.Decide(observation);
            var second = BotDecisionMachine.Decide(observation);

            Assert.That(first.Kind, Is.EqualTo(BotIntentKind.Refresh));
            Assert.That(second.CanonicalSummary, Is.EqualTo(first.CanonicalSummary));
        }

        [Test]
        public void Decide_InvalidCatalogField_WaitsWithStableDiagnostic()
        {
            var offers = EmptyOffers();
            offers[0] = new BotShopOfferObservation(
                1,
                "unit-1",
                "type-1",
                1,
                null,
                false);
            var observation = CreateObservation(offers: offers);

            var intent = BotDecisionMachine.Decide(observation);

            Assert.That(intent.Kind, Is.EqualTo(BotIntentKind.Wait));
            Assert.That(intent.DiagnosticCode, Is.EqualTo("match.ai.observation.offer.baseCost.invalid"));
        }

        [Test]
        public void Decide_AtDeadline_Waits()
        {
            var source = CreateObservation();
            var observation = new BotObservation(
                source.SessionId,
                source.StateRevision,
                source.RoundNumber,
                source.Phase,
                source.SeatIndex,
                source.PlayerId,
                source.ControllerKind,
                source.IsEliminated,
                source.Gold,
                source.Level,
                source.CurrentUpgradePrice,
                source.TotalDeploymentCost,
                source.AvailableDeploymentCost,
                source.StagingSlotUsage,
                source.ShopOffers,
                source.OwnUnits,
                source.OwnFormation,
                source.PublicHumanReadiness,
                30000,
                30000,
                source.DecisionOrdinal);

            var intent = BotDecisionMachine.Decide(observation);

            Assert.That(intent.Kind, Is.EqualTo(BotIntentKind.Wait));
            Assert.That(intent.DiagnosticCode, Is.EqualTo("match.ai.wait.deadline"));
        }

        private static BotObservation CreateObservation(
            MatchPhase phase = MatchPhase.Preparation,
            MatchControllerKind controller = MatchControllerKind.NativeBot,
            bool eliminated = false,
            int gold = 7,
            int level = 1,
            int? upgradePrice = 4,
            int availableCost = 16,
            int stagingUsage = 0,
            IReadOnlyList<BotShopOfferObservation> offers = null,
            IReadOnlyList<BotHumanReadinessObservation> humans = null)
        {
            return new BotObservation(
                "session",
                1,
                1,
                phase,
                2,
                "bot",
                controller,
                eliminated,
                gold,
                level,
                upgradePrice,
                16,
                availableCost,
                stagingUsage,
                offers ?? EmptyOffers(),
                Array.Empty<BotUnitObservation>(),
                Array.Empty<BotFormationObservation>(),
                humans ?? Array.Empty<BotHumanReadinessObservation>(),
                0,
                30000,
                0);
        }

        private static BotShopOfferObservation Offer(
            int slot,
            string unitId,
            int price,
            int baseCost)
        {
            return new BotShopOfferObservation(
                slot,
                unitId,
                "type-" + slot,
                price,
                baseCost,
                false);
        }

        private static BotShopOfferObservation[] EmptyOffers()
        {
            return Enumerable.Range(1, 6)
                .Select(BotShopOfferObservation.Empty)
                .ToArray();
        }
    }
}
