using System;
using System.Linq;
using ArknoNights.Match;

namespace ArknoNights.MatchAI
{
    public enum BotIntentKind
    {
        Wait = 0,
        Buy = 1,
        Refresh = 2,
        Upgrade = 3
    }

    public sealed class BotIntent
    {
        private BotIntent(
            BotIntentKind kind,
            int slotIndex,
            string expectedUnitId,
            int expectedLevel,
            int expectedPrice,
            string diagnosticCode)
        {
            Kind = kind;
            SlotIndex = slotIndex;
            ExpectedUnitId = expectedUnitId ?? string.Empty;
            ExpectedLevel = expectedLevel;
            ExpectedPrice = expectedPrice;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            CanonicalSummary = BotCanonical.Build(
                "BotIntent",
                Kind,
                SlotIndex,
                ExpectedUnitId,
                ExpectedLevel,
                ExpectedPrice,
                DiagnosticCode);
        }

        public BotIntentKind Kind { get; }
        public int SlotIndex { get; }
        public string ExpectedUnitId { get; }
        public int ExpectedLevel { get; }
        public int ExpectedPrice { get; }
        public string DiagnosticCode { get; }
        public string CanonicalSummary { get; }

        public static BotIntent Wait(string diagnosticCode)
        {
            return new BotIntent(BotIntentKind.Wait, 0, string.Empty, 0, 0, diagnosticCode);
        }

        public static BotIntent Buy(int slotIndex, string expectedUnitId)
        {
            return new BotIntent(
                BotIntentKind.Buy,
                slotIndex,
                expectedUnitId,
                0,
                0,
                "match.ai.intent.buy");
        }

        public static BotIntent Refresh()
        {
            return new BotIntent(
                BotIntentKind.Refresh,
                0,
                string.Empty,
                0,
                0,
                "match.ai.intent.refresh");
        }

        public static BotIntent Upgrade(int expectedLevel, int expectedPrice)
        {
            return new BotIntent(
                BotIntentKind.Upgrade,
                0,
                string.Empty,
                expectedLevel,
                expectedPrice,
                "match.ai.intent.upgrade");
        }
    }

    public static class BotDecisionMachine
    {
        public static BotIntent Decide(BotObservation observation)
        {
            if (observation == null)
            {
                return BotIntent.Wait("match.ai.observation.null");
            }
            if (observation.Phase != MatchPhase.Preparation)
            {
                return BotIntent.Wait("match.ai.wait.phase");
            }
            if (observation.ControllerKind != MatchControllerKind.NativeBot
                && observation.ControllerKind != MatchControllerKind.TakeoverBot)
            {
                return BotIntent.Wait("match.ai.wait.controller");
            }
            if (observation.IsEliminated)
            {
                return BotIntent.Wait("match.ai.wait.eliminated");
            }
            if (observation.AllRequiredHumansReady)
            {
                return BotIntent.Wait("match.ai.wait.humansReady");
            }
            if (observation.HostMonotonicNowMs >= observation.PreparationDeadlineHostMs)
            {
                return BotIntent.Wait("match.ai.wait.deadline");
            }
            if (!observation.TryValidate(out var validationDiagnostic))
            {
                return BotIntent.Wait(validationDiagnostic);
            }

            var candidate = observation.StagingSlotUsage < 13
                ? observation.ShopOffers
                    .Where(offer =>
                        offer != null
                        && !offer.IsEmpty
                        && offer.Price.HasValue
                        && offer.Price.Value <= observation.Gold)
                    .OrderByDescending(offer =>
                        offer.BaseElite0DeploymentCost.HasValue
                        && offer.BaseElite0DeploymentCost.Value
                            <= observation.AvailableDeploymentCost)
                    .ThenByDescending(offer => offer.SlotIndex)
                    .ThenBy(offer => offer.UnitId, StringComparer.Ordinal)
                    .FirstOrDefault()
                : null;
            var canUpgrade = observation.Level < 9
                && observation.CurrentUpgradePrice.HasValue;

            if (candidate != null
                && canUpgrade
                && observation.Gold >= checked(
                    observation.CurrentUpgradePrice.Value
                    + candidate.Price.Value
                    + 2))
            {
                return BotIntent.Upgrade(
                    observation.Level,
                    observation.CurrentUpgradePrice.Value);
            }
            if (candidate != null)
            {
                return BotIntent.Buy(candidate.SlotIndex, candidate.UnitId);
            }
            if (canUpgrade
                && observation.Gold >= observation.CurrentUpgradePrice.Value)
            {
                return BotIntent.Upgrade(
                    observation.Level,
                    observation.CurrentUpgradePrice.Value);
            }
            if (observation.StagingSlotUsage < 13 && observation.Gold >= 2)
            {
                return BotIntent.Refresh();
            }
            return BotIntent.Wait("match.ai.wait.resources");
        }
    }
}
