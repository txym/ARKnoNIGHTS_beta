using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Match;

namespace ArknoNights.MatchAI
{
    public static class BotOperationAdapter
    {
        private static readonly IReadOnlyList<MatchFormationPosition> deploymentOrder =
            new ReadOnlyCollection<MatchFormationPosition>(
                BuildDeploymentOrder().ToArray());

        public static IReadOnlyList<MatchFormationPosition> DeploymentOrder =>
            deploymentOrder;

        public static bool TryCreatePrimary(
            BotObservation observation,
            BotIntent intent,
            long controllerGeneration,
            out MatchBotOperationEnvelope operation,
            out string diagnosticCode)
        {
            operation = null;
            if (observation == null || intent == null || controllerGeneration < 1)
            {
                diagnosticCode = "match.ai.adapter.primary.invalid";
                return false;
            }
            MatchCommandPayload payload;
            switch (intent.Kind)
            {
                case BotIntentKind.Buy:
                    payload = new PurchaseShopOfferCommand(
                        intent.SlotIndex,
                        intent.ExpectedUnitId);
                    break;
                case BotIntentKind.Refresh:
                    payload = new RefreshShopCommand();
                    break;
                case BotIntentKind.Upgrade:
                    payload = new PurchaseLevelUpgradeCommand(
                        intent.ExpectedLevel,
                        intent.ExpectedPrice);
                    break;
                case BotIntentKind.Wait:
                    diagnosticCode = "match.ai.adapter.primary.wait";
                    return false;
                default:
                    diagnosticCode = "match.ai.adapter.primary.kind.invalid";
                    return false;
            }
            operation = new MatchBotOperationEnvelope(
                ActionId(
                    observation,
                    controllerGeneration,
                    MatchBotActionPart.Primary),
                observation.PlayerId,
                payload);
            diagnosticCode = string.Empty;
            return true;
        }

        public static bool TryCreateDeployFollowUp(
            MatchBotScopedSnapshot current,
            MatchAcquisitionResult acquisition,
            long decisionOrdinal,
            out MatchBotOperationEnvelope operation,
            out string diagnosticCode)
        {
            operation = null;
            if (current == null || acquisition == null)
            {
                diagnosticCode = "match.ai.adapter.deploy.source.invalid";
                return false;
            }
            if (acquisition.FinalZone != MatchUnitZone.Staging
                || string.IsNullOrWhiteSpace(acquisition.FinalSurvivorUnitId))
            {
                diagnosticCode = acquisition.FinalZone == MatchUnitZone.Deployed
                    ? "match.ai.adapter.deploy.alreadyDeployed"
                    : "match.ai.adapter.deploy.zone.rejected";
                return false;
            }
            var occupied = new HashSet<MatchFormationPosition>(
                current.OwnUnits
                    .Where(unit =>
                        unit.Zone == MatchUnitZone.Deployed
                        && unit.Formation.HasValue)
                    .Select(unit => unit.Formation.Value));
            var target = deploymentOrder.FirstOrDefault(position =>
                !occupied.Contains(position));
            if (!target.IsValid)
            {
                diagnosticCode = "match.ai.adapter.deploy.formation.full";
                return false;
            }
            operation = new MatchBotOperationEnvelope(
                new MatchBotActionId(
                    current.SessionId,
                    current.SeatIndex,
                    current.ControllerGeneration,
                    current.RoundNumber,
                    decisionOrdinal,
                    MatchBotActionPart.DeployFollowUp),
                current.PlayerId,
                new DeployUnitCommand(
                    acquisition.FinalSurvivorUnitId,
                    target,
                    current.AvailableDeploymentCost));
            diagnosticCode = string.Empty;
            return true;
        }

        private static MatchBotActionId ActionId(
            BotObservation observation,
            long controllerGeneration,
            MatchBotActionPart part)
        {
            return new MatchBotActionId(
                observation.SessionId,
                observation.SeatIndex,
                controllerGeneration,
                observation.RoundNumber,
                observation.DecisionOrdinal,
                part);
        }

        private static IEnumerable<MatchFormationPosition> BuildDeploymentOrder()
        {
            var rows = new[] { 2, 3, 4, 1 };
            var columns = new[] { 5, 4, 6, 3, 7, 2, 8, 1, 9 };
            foreach (var y in rows)
            {
                foreach (var x in columns)
                {
                    var position = new MatchFormationPosition(x, y);
                    if (position.IsValid)
                    {
                        yield return position;
                    }
                }
            }
        }
    }
}
