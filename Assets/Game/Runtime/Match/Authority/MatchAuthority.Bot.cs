using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Match
{
    public sealed partial class MatchAuthority
    {
        public IReadOnlyList<MatchBotSeatControlSnapshot> ProjectBotControlSeats()
        {
            return ProjectBotControlSeats(state);
        }

        public bool TryProjectForBot(
            string playerId,
            out MatchBotScopedSnapshot snapshot,
            out string diagnosticCode)
        {
            return TryProjectForBot(state, playerId, out snapshot, out diagnosticCode);
        }

        public MatchBotOperationResult ExecuteBotOperation(
            MatchBotOperationEnvelope envelope)
        {
            return CreateLiveBotHost().ExecuteBotOperation(envelope);
        }

        public MatchTransactionResult SetHumanDisconnectedGrace(string playerId)
        {
            return CreateLiveBotHost().SetHumanDisconnectedGrace(playerId);
        }

        public MatchTransactionResult ActivateTakeoverBot(string playerId)
        {
            return CreateLiveBotHost().ActivateTakeoverBot(playerId);
        }

        public MatchTransactionResult MarkHumanQuit(string playerId)
        {
            return CreateLiveBotHost().MarkHumanQuit(playerId);
        }

        public MatchTransactionResult RestoreHumanControl(string playerId)
        {
            return CreateLiveBotHost().RestoreHumanControl(playerId);
        }

        private BotHostScope CreateLiveBotHost()
        {
            return new BotHostScope(this, state, true, true);
        }

        private BotHostScope CreatePreparationEntryBotHost(MatchState prepared)
        {
            return new BotHostScope(this, prepared, false, false);
        }

        private static IReadOnlyList<MatchBotSeatControlSnapshot> ProjectBotControlSeats(
            MatchState source)
        {
            return new ReadOnlyCollection<MatchBotSeatControlSnapshot>(
                source.Seats
                    .OrderBy(seat => seat.SeatIndex)
                    .Select(seat => new MatchBotSeatControlSnapshot(source, seat))
                    .ToArray());
        }

        private static bool TryProjectForBot(
            MatchState source,
            string playerId,
            out MatchBotScopedSnapshot snapshot,
            out string diagnosticCode)
        {
            var seat = source.FindSeat(playerId);
            if (seat == null)
            {
                snapshot = null;
                diagnosticCode = "match.ai.observation.player.unknown";
                return false;
            }
            if (seat.ControllerKind != MatchControllerKind.NativeBot
                && seat.ControllerKind != MatchControllerKind.TakeoverBot)
            {
                snapshot = null;
                diagnosticCode = "match.ai.observation.controller.rejected";
                return false;
            }
            snapshot = new MatchBotScopedSnapshot(source, seat);
            diagnosticCode = string.Empty;
            return true;
        }

        private MatchBotOperationResult ExecuteBotOperation(
            BotHostScope host,
            MatchBotOperationEnvelope envelope)
        {
            if (envelope == null || envelope.ActionId == null)
            {
                return BotResult(
                    null,
                    MatchCommandCode.InvalidPayload,
                    host.Current.StateRevision,
                    false,
                    null,
                    "match.ai.operation.envelope.invalid");
            }

            var actionKey = envelope.ActionId.CanonicalSummary;
            if (botActionRecords.TryGetValue(actionKey, out var cached))
            {
                return string.Equals(
                        cached.OperationSummary,
                        envelope.CanonicalSummary,
                        StringComparison.Ordinal)
                    ? cached.Result
                    : BotResult(
                        envelope.ActionId,
                        MatchCommandCode.CommandIdConflict,
                        host.Current.StateRevision,
                        false,
                        null,
                        "match.ai.actionId.conflict");
            }

            var source = host.Current;
            var actionId = envelope.ActionId;
            MatchBotOperationResult rejected = null;
            if (string.IsNullOrWhiteSpace(envelope.PlayerId)
                || string.IsNullOrWhiteSpace(actionId.SessionId)
                || actionId.SeatIndex < 1
                || actionId.SeatIndex > 4
                || actionId.ControllerGeneration < 1
                || actionId.RoundNumber < 1
                || actionId.DecisionOrdinal < 0
                || !Enum.IsDefined(typeof(MatchBotActionPart), actionId.ActionPart)
                || envelope.Payload == null)
            {
                rejected = BotResult(
                    actionId,
                    MatchCommandCode.InvalidPayload,
                    source.StateRevision,
                    false,
                    null,
                    "match.ai.operation.payload.invalid");
            }
            else if (!string.Equals(
                source.SessionId,
                actionId.SessionId,
                StringComparison.Ordinal))
            {
                rejected = BotResult(
                    actionId,
                    MatchCommandCode.SessionMismatch,
                    source.StateRevision,
                    false,
                    null,
                    "match.ai.operation.session.mismatch");
            }
            else
            {
                var seat = source.FindSeat(envelope.PlayerId);
                if (seat == null)
                {
                    rejected = BotResult(
                        actionId,
                        MatchCommandCode.UnknownPlayer,
                        source.StateRevision,
                        false,
                        null,
                        "match.ai.operation.player.unknown");
                }
                else if (actionId.SeatIndex != seat.SeatIndex
                    || actionId.RoundNumber != source.RoundNumber)
                {
                    rejected = BotResult(
                        actionId,
                        MatchCommandCode.InvalidPayload,
                        source.StateRevision,
                        false,
                        null,
                        "match.ai.actionId.scope.invalid");
                }
                else if (actionId.ControllerGeneration != seat.ControllerGeneration)
                {
                    rejected = BotResult(
                        actionId,
                        MatchCommandCode.ControllerGenerationChanged,
                        source.StateRevision,
                        false,
                        null,
                        "match.ai.controller.generation.changed");
                }
                else if (source.Phase != MatchPhase.Preparation)
                {
                    rejected = BotResult(
                        actionId,
                        MatchCommandCode.PhaseRejected,
                        source.StateRevision,
                        false,
                        null,
                        "match.ai.operation.phase.rejected");
                }
                else if (seat.Eliminated)
                {
                    rejected = BotResult(
                        actionId,
                        MatchCommandCode.Eliminated,
                        source.StateRevision,
                        false,
                        null,
                        "match.ai.operation.eliminated");
                }
                else if (seat.ControllerKind != MatchControllerKind.NativeBot
                    && seat.ControllerKind != MatchControllerKind.TakeoverBot)
                {
                    rejected = BotResult(
                        actionId,
                        MatchCommandCode.ControllerRejected,
                        source.StateRevision,
                        false,
                        null,
                        "match.ai.operation.controller.rejected");
                }
                else if (!IsAllowedBotPayload(actionId.ActionPart, envelope.Payload))
                {
                    rejected = BotResult(
                        actionId,
                        MatchCommandCode.InvalidPayload,
                        source.StateRevision,
                        false,
                        null,
                        "match.ai.operation.kind.rejected");
                }
            }

            if (rejected != null)
            {
                return CacheBotAction(actionKey, envelope, rejected);
            }

            MatchBotOperationResult result;
            if (envelope.Payload is RefreshShopCommand)
            {
                result = ExecuteBotRefresh(host, envelope);
            }
            else if (envelope.Payload is PurchaseShopOfferCommand purchase)
            {
                result = ExecuteBotPurchase(host, envelope, purchase);
            }
            else if (envelope.Payload is PurchaseLevelUpgradeCommand upgrade)
            {
                result = ExecuteBotUpgrade(host, envelope, upgrade);
            }
            else
            {
                result = ExecuteBotDeploy(host, envelope);
            }
            return CacheBotAction(actionKey, envelope, result);
        }

        private MatchBotOperationResult ExecuteBotRefresh(
            BotHostScope host,
            MatchBotOperationEnvelope envelope)
        {
            var seat = host.Current.FindSeat(envelope.PlayerId);
            if (seat.Gold < MatchEconomyRules.RefreshCost)
            {
                return BotRejected(
                    host,
                    envelope,
                    MatchCommandCode.InsufficientGold,
                    "match.ai.refresh.gold.insufficient");
            }
            var draft = new MatchEconomyTransactionDraft(host.Current);
            var poolExhausted = draft.RefreshPlayerShop(seat.PlayerId);
            draft.SpendGold(seat.PlayerId, MatchEconomyRules.RefreshCost);
            return CommitBotOperation(
                host,
                envelope,
                draft.BuildState(host.IncrementRevision),
                poolExhausted
                    ? MatchCommandCode.PoolExhaustedDiagnostic
                    : MatchCommandCode.Accepted,
                poolExhausted
                    ? "match.ai.refresh.poolExhausted"
                    : "match.ai.refresh.accepted");
        }

        private MatchBotOperationResult ExecuteBotPurchase(
            BotHostScope host,
            MatchBotOperationEnvelope envelope,
            PurchaseShopOfferCommand command)
        {
            var draft = new MatchEconomyTransactionDraft(host.Current);
            if (!draft.TryPurchaseShopOffer(
                envelope.PlayerId,
                command.SlotIndex,
                command.ExpectedUnitId,
                stagingSlotPolicy,
                out var acquisition,
                out var code,
                out var diagnosticCode))
            {
                return BotRejected(host, envelope, code, diagnosticCode);
            }
            return CommitBotOperation(
                host,
                envelope,
                draft.BuildState(host.IncrementRevision),
                MatchCommandCode.Accepted,
                diagnosticCode,
                acquisition);
        }

        private MatchBotOperationResult ExecuteBotUpgrade(
            BotHostScope host,
            MatchBotOperationEnvelope envelope,
            PurchaseLevelUpgradeCommand command)
        {
            var seat = host.Current.FindSeat(envelope.PlayerId);
            if (seat.Level == MatchEconomyRules.MaximumLevel)
            {
                return BotRejected(
                    host,
                    envelope,
                    MatchCommandCode.LevelMax,
                    "match.ai.upgrade.level.max");
            }
            if (command.ExpectedCurrentLevel != seat.Level)
            {
                return BotRejected(
                    host,
                    envelope,
                    MatchCommandCode.UpgradeLevelChanged,
                    "match.ai.upgrade.level.changed");
            }
            if (command.ExpectedCurrentPrice != seat.CurrentUpgradePrice)
            {
                return BotRejected(
                    host,
                    envelope,
                    MatchCommandCode.UpgradePriceChanged,
                    "match.ai.upgrade.price.changed");
            }
            if (seat.Gold < seat.CurrentUpgradePrice)
            {
                return BotRejected(
                    host,
                    envelope,
                    MatchCommandCode.InsufficientGold,
                    "match.ai.upgrade.gold.insufficient");
            }
            var draft = new MatchEconomyTransactionDraft(host.Current);
            draft.PurchaseLevelUpgrade(seat.PlayerId);
            return CommitBotOperation(
                host,
                envelope,
                draft.BuildState(host.IncrementRevision),
                MatchCommandCode.Accepted,
                "match.ai.upgrade.accepted");
        }

        private MatchBotOperationResult ExecuteBotDeploy(
            BotHostScope host,
            MatchBotOperationEnvelope envelope)
        {
            var draft = new MatchEconomyTransactionDraft(host.Current);
            var operation = MatchFormationService.TryApply(
                host.Current,
                draft,
                envelope.PlayerId,
                envelope.Payload,
                MatchFormationOperationOrigin.BotAuthority);
            if (!operation.Accepted)
            {
                return BotRejected(
                    host,
                    envelope,
                    operation.Code,
                    operation.DiagnosticCode);
            }
            if (!operation.Changed)
            {
                return BotResult(
                    envelope.ActionId,
                    MatchCommandCode.AcceptedNoChange,
                    host.Current.StateRevision,
                    false,
                    host.Current.StateRevision,
                    operation.DiagnosticCode);
            }
            return CommitBotOperation(
                host,
                envelope,
                draft.BuildState(host.IncrementRevision),
                operation.Code,
                operation.DiagnosticCode);
        }

        private MatchBotOperationResult CommitBotOperation(
            BotHostScope host,
            MatchBotOperationEnvelope envelope,
            MatchState next,
            MatchCommandCode code,
            string diagnosticCode,
            MatchAcquisitionResult acquisition = null)
        {
            if (!MatchStateInvariant.TryValidate(
                next,
                stagingSlotPolicy,
                out var invariantDiagnostic))
            {
                return BotRejected(
                    host,
                    envelope,
                    MatchCommandCode.InternalInvariantViolation,
                    invariantDiagnostic);
            }
            host.SetCurrent(next);
            return BotResult(
                envelope.ActionId,
                code,
                next.StateRevision,
                true,
                next.StateRevision,
                diagnosticCode,
                acquisition);
        }

        private static MatchBotOperationResult BotRejected(
            BotHostScope host,
            MatchBotOperationEnvelope envelope,
            MatchCommandCode code,
            string diagnosticCode)
        {
            return BotResult(
                envelope.ActionId,
                code,
                host.Current.StateRevision,
                false,
                null,
                diagnosticCode);
        }

        private static MatchBotOperationResult BotResult(
            MatchBotActionId actionId,
            MatchCommandCode code,
            long currentRevision,
            bool changed,
            long? acceptedRevision,
            string diagnosticCode,
            MatchAcquisitionResult acquisition = null)
        {
            return new MatchBotOperationResult(
                actionId,
                code,
                currentRevision,
                acceptedRevision,
                changed,
                diagnosticCode,
                acquisition);
        }

        private MatchBotOperationResult CacheBotAction(
            string key,
            MatchBotOperationEnvelope envelope,
            MatchBotOperationResult result)
        {
            botActionRecords[key] = new BotActionRecord(envelope.CanonicalSummary, result);
            return result;
        }

        private static bool IsAllowedBotPayload(
            MatchBotActionPart actionPart,
            MatchCommandPayload payload)
        {
            if (actionPart == MatchBotActionPart.DeployFollowUp)
            {
                return payload is DeployUnitCommand;
            }
            return payload is RefreshShopCommand
                || payload is PurchaseShopOfferCommand
                || payload is PurchaseLevelUpgradeCommand;
        }

        private MatchTransactionResult MutateSeatControl(
            BotHostScope host,
            string playerId,
            Func<MatchState, MatchSeatState, MatchSeatState> mutation,
            string diagnosticCode)
        {
            var source = host.Current;
            if (source.Phase == MatchPhase.Ended)
            {
                return HostRejected(source, MatchCommandCode.InvalidTransition, "match.phase.ended");
            }
            var seat = source.FindSeat(playerId);
            if (seat == null)
            {
                return HostRejected(
                    source,
                    MatchCommandCode.UnknownPlayer,
                    "match.ai.control.player.unknown");
            }
            var nextSeat = mutation(source, seat);
            if (nextSeat == null)
            {
                return HostRejected(
                    source,
                    MatchCommandCode.ControllerRejected,
                    diagnosticCode + ".rejected");
            }
            if (string.Equals(
                nextSeat.CanonicalSummary,
                seat.CanonicalSummary,
                StringComparison.Ordinal))
            {
                return new MatchTransactionResult(
                    MatchCommandCode.AcceptedNoChange,
                    source.StateRevision,
                    source.StateRevision,
                    false,
                    diagnosticCode + ".noChange");
            }
            var next = source.Rebuild(
                host.IncrementRevision
                    ? checked(source.StateRevision + 1)
                    : source.StateRevision,
                source.Phase,
                source.RoundNumber,
                source.Seats.Select(candidate =>
                    candidate.SeatIndex == seat.SeatIndex ? nextSeat : candidate),
                source.Pool,
                source.EndReason,
                source.Flow);
            if (!MatchStateInvariant.TryValidate(
                next,
                stagingSlotPolicy,
                out var invariantDiagnostic))
            {
                return HostRejected(
                    source,
                    MatchCommandCode.InternalInvariantViolation,
                    invariantDiagnostic);
            }
            host.SetCurrent(next);
            return new MatchTransactionResult(
                MatchCommandCode.Accepted,
                next.StateRevision,
                next.StateRevision,
                true,
                diagnosticCode + ".accepted");
        }

        private static MatchTransactionResult HostRejected(
            MatchState source,
            MatchCommandCode code,
            string diagnosticCode)
        {
            return new MatchTransactionResult(
                code,
                source.StateRevision,
                null,
                false,
                diagnosticCode);
        }

        private sealed class BotHostScope : IMatchBotHost
        {
            private readonly MatchAuthority owner;
            private readonly bool publishChanges;

            internal BotHostScope(
                MatchAuthority owner,
                MatchState current,
                bool incrementRevision,
                bool publishChanges)
            {
                this.owner = owner;
                Current = current;
                IncrementRevision = incrementRevision;
                this.publishChanges = publishChanges;
            }

            internal MatchState Current { get; private set; }
            internal bool IncrementRevision { get; }

            public IReadOnlyList<MatchBotSeatControlSnapshot> ProjectBotControlSeats()
            {
                return MatchAuthority.ProjectBotControlSeats(Current);
            }

            public bool TryProjectForBot(
                string playerId,
                out MatchBotScopedSnapshot snapshot,
                out string diagnosticCode)
            {
                return MatchAuthority.TryProjectForBot(
                    Current,
                    playerId,
                    out snapshot,
                    out diagnosticCode);
            }

            public MatchBotOperationResult ExecuteBotOperation(
                MatchBotOperationEnvelope envelope)
            {
                return owner.ExecuteBotOperation(this, envelope);
            }

            public MatchTransactionResult SetHumanDisconnectedGrace(string playerId)
            {
                return owner.MutateSeatControl(
                    this,
                    playerId,
                    (source, seat) =>
                    {
                        if (seat.ControllerKind != MatchControllerKind.Human
                            || seat.Eliminated
                            || string.Equals(
                                source.HostPlayerId,
                                seat.PlayerId,
                                StringComparison.Ordinal))
                        {
                            return null;
                        }
                        return seat.With(
                            ready: false,
                            connectionState: MatchConnectionState.DisconnectedGrace);
                    },
                    "match.ai.disconnect");
            }

            public MatchTransactionResult ActivateTakeoverBot(string playerId)
            {
                return owner.MutateSeatControl(
                    this,
                    playerId,
                    (source, seat) =>
                    {
                        if (seat.Eliminated
                            || seat.ControllerKind != MatchControllerKind.Human
                            || string.Equals(
                                source.HostPlayerId,
                                seat.PlayerId,
                                StringComparison.Ordinal))
                        {
                            return null;
                        }
                        return seat.With(
                            ready: false,
                            controllerKind: MatchControllerKind.TakeoverBot,
                            connectionState: MatchConnectionState.Connected,
                            controllerGeneration: checked(seat.ControllerGeneration + 1));
                    },
                    "match.ai.takeover");
            }

            public MatchTransactionResult MarkHumanQuit(string playerId)
            {
                return owner.MutateSeatControl(
                    this,
                    playerId,
                    (source, seat) =>
                    {
                        if (seat.Eliminated
                            || seat.ControllerKind != MatchControllerKind.Human
                            || string.Equals(
                                source.HostPlayerId,
                                seat.PlayerId,
                                StringComparison.Ordinal))
                        {
                            return null;
                        }
                        return seat.With(
                            ready: false,
                            connectionState: MatchConnectionState.Quit);
                    },
                    "match.ai.quit");
            }

            public MatchTransactionResult RestoreHumanControl(string playerId)
            {
                return owner.MutateSeatControl(
                    this,
                    playerId,
                    (source, seat) =>
                    {
                        if (seat.Eliminated
                            || seat.ControllerKind == MatchControllerKind.NativeBot)
                        {
                            return null;
                        }
                        return seat.With(
                            ready: false,
                            controllerKind: MatchControllerKind.Human,
                            connectionState: MatchConnectionState.Connected);
                    },
                    "match.ai.restore");
            }

            internal void SetCurrent(MatchState next)
            {
                Current = next;
                if (!publishChanges)
                {
                    return;
                }
                owner.state = next;
                owner.PublishChanged();
            }
        }

        private sealed class BotActionRecord
        {
            internal BotActionRecord(
                string operationSummary,
                MatchBotOperationResult result)
            {
                OperationSummary = operationSummary;
                Result = result;
            }

            internal string OperationSummary { get; }
            internal MatchBotOperationResult Result { get; }
        }
    }
}
