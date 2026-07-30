using System;
using System.Linq;
using ArknoNights.Match;

namespace ArknoNights.MatchAI
{
    public static class BotObservationFactory
    {
        public static bool TryCreate(
            MatchBotScopedSnapshot source,
            long hostMonotonicNowMs,
            long decisionOrdinal,
            out BotObservation observation,
            out string diagnosticCode)
        {
            if (source == null)
            {
                observation = null;
                diagnosticCode = "match.ai.observation.source.null";
                return false;
            }

            observation = new BotObservation(
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
                source.ShopOffers.Select(offer => new BotShopOfferObservation(
                    offer.SlotIndex,
                    offer.UnitId,
                    offer.TypeId,
                    offer.Price,
                    offer.BaseElite0DeploymentCost,
                    offer.IsFrozen)),
                source.OwnUnits.Select(unit => new BotUnitObservation(
                    unit.UnitId,
                    unit.TypeId,
                    unit.Zone,
                    unit.EliteLevel)),
                source.OwnUnits
                    .Where(unit =>
                        unit.Zone == MatchUnitZone.Deployed
                        && unit.Formation.HasValue)
                    .Select(unit => new BotFormationObservation(
                        unit.UnitId,
                        unit.Formation.Value.X,
                        unit.Formation.Value.Y)),
                source.PublicHumanReadiness.Select(human =>
                    new BotHumanReadinessObservation(
                        human.PlayerId,
                        human.Ready,
                        human.Connected,
                        human.Eliminated)),
                hostMonotonicNowMs,
                source.PreparationDeadlineHostMs,
                decisionOrdinal);
            if (!observation.TryValidate(out diagnosticCode))
            {
                observation = null;
                return false;
            }
            diagnosticCode = string.Empty;
            return true;
        }
    }
}
