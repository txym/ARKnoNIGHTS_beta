using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    public sealed partial class MatchAuthority : IMatchBotHost
    {
        private readonly Dictionary<CommandKey, CommandRecord> commandRecords =
            new Dictionary<CommandKey, CommandRecord>();
        private readonly Dictionary<string, BotActionRecord> botActionRecords =
            new Dictionary<string, BotActionRecord>(StringComparer.Ordinal);
        private readonly IStagingSlotPolicy stagingSlotPolicy;
        private readonly IMatchPreparationEntryParticipant preparationEntryParticipant;
        private MatchState state;

        internal MatchAuthority(
            MatchState initialState,
            IStagingSlotPolicy stagingSlotPolicy)
            : this(
                initialState,
                stagingSlotPolicy,
                new NoOpMatchPreparationEntryParticipant())
        {
        }

        internal MatchAuthority(
            MatchState initialState,
            IStagingSlotPolicy stagingSlotPolicy,
            IMatchPreparationEntryParticipant preparationEntryParticipant)
        {
            state = initialState ?? throw new ArgumentNullException(nameof(initialState));
            this.stagingSlotPolicy = stagingSlotPolicy
                ?? throw new ArgumentNullException(nameof(stagingSlotPolicy));
            this.preparationEntryParticipant = preparationEntryParticipant
                ?? throw new ArgumentNullException(nameof(preparationEntryParticipant));
        }

        public event Action<MatchChangedEvent> Changed;

        public long StateRevision => state.StateRevision;
        public bool AllRequiredHumansReady
        {
            get
            {
                var required = state.Seats
                    .Where(seat =>
                        !seat.Eliminated
                        && seat.ControllerKind == MatchControllerKind.Human
                        && seat.ConnectionState == MatchConnectionState.Connected)
                    .ToArray();
                var graceBlocks = state.Seats.Any(seat =>
                    !seat.Eliminated
                    && seat.ControllerKind == MatchControllerKind.Human
                    && seat.ConnectionState == MatchConnectionState.DisconnectedGrace);
                return required.Length > 0
                    && !graceBlocks
                    && required.All(seat => seat.Ready);
            }
        }

        public PublicMatchSnapshot ProjectPublic()
        {
            return MatchSnapshotProjector.ProjectPublic(state);
        }

        public PlayerMatchSnapshot ProjectForPlayer(string playerId)
        {
            if (!TryProjectForPlayer(playerId, out var snapshot))
            {
                throw new ArgumentException("The player does not belong to this match.", nameof(playerId));
            }
            return snapshot;
        }

        public bool TryProjectForPlayer(string playerId, out PlayerMatchSnapshot snapshot)
        {
            var seat = state.FindSeat(playerId);
            if (seat == null)
            {
                snapshot = null;
                return false;
            }

            snapshot = MatchSnapshotProjector.ProjectForPlayer(state, seat);
            return true;
        }

        public HostMatchSnapshot ProjectForHostAuthority()
        {
            return new HostMatchSnapshot(
                state,
                commandRecords.Values.Select(record => new MatchCommandRecordSnapshot(
                    record.PlayerId,
                    record.CommandId,
                    record.CommandSummary,
                    record.Result)));
        }

        public MatchCommandResult Execute(MatchCommandEnvelope envelope)
        {
            if (envelope == null)
            {
                return CommandResult(
                    string.Empty,
                    MatchCommandCode.InvalidPayload,
                    false,
                    null,
                    "match.command.envelope.null");
            }
            if (string.IsNullOrWhiteSpace(envelope.CommandId))
            {
                return CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.CommandIdInvalid,
                    false,
                    null,
                    "match.command.id.invalid");
            }
            if (string.IsNullOrWhiteSpace(envelope.PlayerId))
            {
                return CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.UnknownPlayer,
                    false,
                    null,
                    "match.command.player.invalid");
            }

            var key = new CommandKey(envelope.PlayerId, envelope.CommandId);
            if (commandRecords.TryGetValue(key, out var cached))
            {
                if (string.Equals(
                    cached.CommandSummary,
                    envelope.CanonicalContentSummary,
                    StringComparison.Ordinal))
                {
                    return cached.Result;
                }

                return CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.CommandIdConflict,
                    false,
                    null,
                    "match.command.id.conflict");
            }

            MatchCommandResult result;
            if (!string.Equals(envelope.SessionId, state.SessionId, StringComparison.Ordinal))
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.SessionMismatch,
                    false,
                    null,
                    "match.command.session.mismatch");
                return Cache(key, envelope, result);
            }

            var seat = state.FindSeat(envelope.PlayerId);
            if (seat == null)
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.UnknownPlayer,
                    false,
                    null,
                    "match.command.player.unknown");
                return Cache(key, envelope, result);
            }
            if (envelope.KnownStateRevision < 0)
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.InvalidPayload,
                    false,
                    null,
                    "match.command.revision.negative");
                return Cache(key, envelope, result);
            }
            if (envelope.KnownStateRevision > state.StateRevision)
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.FutureRevision,
                    false,
                    null,
                    "match.command.revision.future");
                return Cache(key, envelope, result);
            }
            if (envelope.Payload is SetPreparationReadyCommand readyCommand)
            {
                return ExecuteReady(key, envelope, seat, readyCommand);
            }
            if (envelope.Payload is RefreshShopCommand)
            {
                return ExecuteRefreshShop(key, envelope, seat);
            }
            if (envelope.Payload is ToggleShopFreezeCommand)
            {
                return ExecuteToggleShopFreeze(key, envelope, seat);
            }
            if (envelope.Payload is PurchaseShopOfferCommand purchaseCommand)
            {
                return ExecutePurchaseShopOffer(key, envelope, seat, purchaseCommand);
            }
            if (envelope.Payload is PurchaseLevelUpgradeCommand upgradeCommand)
            {
                return ExecutePurchaseLevelUpgrade(key, envelope, seat, upgradeCommand);
            }
            if (envelope.Payload is DeployUnitCommand
                || envelope.Payload is ReplaceDeployedUnitCommand
                || envelope.Payload is RelocateOrSwapUnitCommand
                || envelope.Payload is RetreatUnitCommand)
            {
                return ExecuteFormationCommand(key, envelope);
            }
            result = CommandResult(
                envelope.CommandId,
                MatchCommandCode.InvalidPayload,
                false,
                null,
                "match.command.payload.unsupported");
            return Cache(key, envelope, result);
        }

        public MatchTransactionResult TryApplyPostBattleNaturalRefresh(int roundNumber)
        {
            if (state.Phase != MatchPhase.Settlement || roundNumber != state.RoundNumber)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.shop.naturalRefresh.phaseOrRound.invalid");
            }
            if (state.Pool.AppliedPostBattleRefreshRounds.Contains(roundNumber))
            {
                return TransactionRejected(
                    MatchCommandCode.NaturalRefreshAlreadyApplied,
                    "match.shop.naturalRefresh.alreadyApplied");
            }

            var draft = new MatchEconomyTransactionDraft(state);
            var poolExhausted = draft.ApplyPostBattleNaturalRefresh(roundNumber);
            return CommitHost(
                draft.BuildState(true),
                poolExhausted
                    ? "match.shop.naturalRefresh.poolExhausted"
                    : "match.shop.naturalRefresh.accepted",
                poolExhausted
                    ? MatchCommandCode.PoolExhaustedDiagnostic
                    : MatchCommandCode.Accepted);
        }

        public MatchTransactionResult TryEnterPreparation(int roundNumber)
        {
            return TryEnterPreparation(roundNumber, 0);
        }

        public MatchTransactionResult TryEnterPreparation(
            int roundNumber,
            long hostMonotonicNowMs)
        {
            return EnterPreparation(roundNumber, hostMonotonicNowMs);
        }

        internal MatchTransactionResult TryAdvancePhase(MatchPhase targetPhase)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.phase.ended");
            }

            MatchPhase expected;
            switch (state.Phase)
            {
                case MatchPhase.Preparation:
                    expected = MatchPhase.Sealing;
                    break;
                case MatchPhase.Sealing:
                    expected = MatchPhase.Battle;
                    break;
                case MatchPhase.Battle:
                    expected = MatchPhase.Settlement;
                    break;
                default:
                    return TransactionRejected(
                        MatchCommandCode.InvalidTransition,
                        "match.phase.advance.source.invalid");
            }

            if (targetPhase != expected)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.phase.advance.target.invalid");
            }
            var nextState = state.WithPhase(targetPhase);
            if (state.Phase == MatchPhase.Preparation)
            {
                nextState = nextState.Rebuild(
                    nextState.StateRevision,
                    nextState.Phase,
                    nextState.RoundNumber,
                    nextState.Seats,
                    nextState.Pool,
                    nextState.EndReason,
                    nextState.Flow.With(hasPreparationClock: false));
            }
            return CommitHost(
                nextState,
                "match.phase.advance.accepted");
        }

        public MatchTransactionResult TryDiscardRemainingOverflowAtSeal()
        {
            if (state.Phase != MatchPhase.Sealing)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.overflow.discard.phase.invalid");
            }

            var draft = new MatchEconomyTransactionDraft(state);
            if (!draft.TryDiscardRemainingOverflow(
                out var retiredUnitIds,
                out var code,
                out var diagnosticCode))
            {
                return TransactionRejected(code, diagnosticCode);
            }
            if (code == MatchCommandCode.AcceptedNoChange)
            {
                return TransactionNoChange(diagnosticCode);
            }
            return CommitHost(
                draft.BuildState(true),
                diagnosticCode,
                code,
                retiredUnitIds);
        }

        public MatchTransactionResult TrySetConnectionState(
            string playerId,
            MatchConnectionState connectionState)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionRejected(MatchCommandCode.InvalidTransition, "match.phase.ended");
            }
            if (!Enum.IsDefined(typeof(MatchConnectionState), connectionState))
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.connection.value.invalid");
            }
            var seat = state.FindSeat(playerId);
            if (seat == null)
            {
                return TransactionRejected(MatchCommandCode.UnknownPlayer, "match.connection.player.unknown");
            }
            if (seat.ConnectionState == connectionState)
            {
                return TransactionNoChange("match.connection.noChange");
            }
            if (seat.Eliminated || connectionState == MatchConnectionState.Eliminated)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.connection.eliminated.useMark");
            }
            if (seat.ControllerKind == MatchControllerKind.NativeBot
                && connectionState != MatchConnectionState.Connected)
            {
                return TransactionRejected(
                    MatchCommandCode.ConnectionRejected,
                    "match.connection.nativeBot.invalid");
            }
            if (string.Equals(seat.PlayerId, state.HostPlayerId, StringComparison.Ordinal)
                && connectionState == MatchConnectionState.Quit)
            {
                return TransactionRejected(
                    MatchCommandCode.ControllerRejected,
                    "match.connection.host.quitRequiresEnd");
            }

            var clearReady = seat.ControllerKind == MatchControllerKind.Human
                && connectionState != MatchConnectionState.Connected;
            var nextSeat = seat.With(
                ready: clearReady ? false : (bool?)null,
                connectionState: connectionState);
            return CommitHost(
                state.WithSeat(nextSeat),
                "match.connection.accepted");
        }

        public MatchTransactionResult TrySetControllerKind(
            string playerId,
            MatchControllerKind controllerKind)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionRejected(MatchCommandCode.InvalidTransition, "match.phase.ended");
            }
            if (!Enum.IsDefined(typeof(MatchControllerKind), controllerKind))
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.controller.value.invalid");
            }
            var seat = state.FindSeat(playerId);
            if (seat == null)
            {
                return TransactionRejected(MatchCommandCode.UnknownPlayer, "match.controller.player.unknown");
            }
            if (seat.ControllerKind == controllerKind)
            {
                return TransactionNoChange("match.controller.noChange");
            }
            if (string.Equals(seat.PlayerId, state.HostPlayerId, StringComparison.Ordinal)
                && controllerKind != MatchControllerKind.Human)
            {
                return TransactionRejected(
                    MatchCommandCode.ControllerRejected,
                    "match.controller.host.mustRemainHuman");
            }

            var nextSeat = seat.With(
                ready: controllerKind == MatchControllerKind.Human ? (bool?)null : false,
                controllerKind: controllerKind,
                controllerGeneration:
                    controllerKind == MatchControllerKind.NativeBot
                    || controllerKind == MatchControllerKind.TakeoverBot
                        ? checked(seat.ControllerGeneration + 1)
                        : seat.ControllerGeneration);
            return CommitHost(
                state.WithSeat(nextSeat),
                "match.controller.accepted");
        }

        internal MatchTransactionResult TryMarkEliminated(string playerId, int? placement)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionRejected(MatchCommandCode.InvalidTransition, "match.phase.ended");
            }
            if (placement.HasValue && (placement.Value < 1 || placement.Value > 4))
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidPayload,
                    "match.elimination.placement.invalid");
            }
            var seat = state.FindSeat(playerId);
            if (seat == null)
            {
                return TransactionRejected(MatchCommandCode.UnknownPlayer, "match.elimination.player.unknown");
            }
            if (seat.Eliminated)
            {
                return seat.Placement == placement
                    ? TransactionNoChange("match.elimination.noChange")
                    : TransactionRejected(
                        MatchCommandCode.InvalidTransition,
                        "match.elimination.placement.alreadySet");
            }

            return CommitHost(
                state.WithSeat(seat.WithElimination(placement)),
                "match.elimination.accepted");
        }

        internal MatchTransactionResult TryEndMatch(string reason)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionRejected(MatchCommandCode.InvalidTransition, "match.phase.ended");
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                return TransactionRejected(MatchCommandCode.InvalidPayload, "match.end.reason.invalid");
            }

            var nextState = state.WithPhase(MatchPhase.Ended, reason);
            nextState = nextState.Rebuild(
                nextState.StateRevision,
                nextState.Phase,
                nextState.RoundNumber,
                nextState.Seats,
                nextState.Pool,
                nextState.EndReason,
                nextState.Flow.With(hasPreparationClock: false));
            return CommitHost(nextState, "match.end.accepted");
        }

        private MatchCommandResult ExecuteReady(
            CommandKey key,
            MatchCommandEnvelope envelope,
            MatchSeatState seat,
            SetPreparationReadyCommand command)
        {
            MatchCommandResult result;
            if (state.Phase != MatchPhase.Preparation)
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.PhaseRejected,
                    false,
                    null,
                    "match.ready.phase.rejected");
                return Cache(key, envelope, result);
            }
            if (seat.ControllerKind != MatchControllerKind.Human)
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.ControllerRejected,
                    false,
                    null,
                    "match.ready.controller.rejected");
                return Cache(key, envelope, result);
            }
            if (seat.Eliminated)
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.Eliminated,
                    false,
                    null,
                    "match.ready.eliminated");
                return Cache(key, envelope, result);
            }
            if (seat.ConnectionState != MatchConnectionState.Connected)
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.ConnectionRejected,
                    false,
                    null,
                    "match.ready.connection.rejected");
                return Cache(key, envelope, result);
            }
            if (seat.Ready == command.DesiredReady)
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.AcceptedNoChange,
                    false,
                    state.StateRevision,
                    "match.ready.noChange");
                return Cache(key, envelope, result);
            }

            if (!TryBuildReadyState(
                seat,
                command.DesiredReady,
                out var nextState,
                out var readyCode,
                out var readyDiagnostic))
            {
                if (readyCode == MatchCommandCode.FatalMatchError
                    && nextState != null)
                {
                    return CommitCommand(
                        key,
                        envelope,
                        nextState,
                        readyCode,
                        readyDiagnostic);
                }
                result = CommandResult(
                    envelope.CommandId,
                    readyCode,
                    false,
                    null,
                    readyDiagnostic);
                return Cache(key, envelope, result);
            }
            if (!MatchStateInvariant.TryValidate(nextState, stagingSlotPolicy, out var diagnosticCode))
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.InternalInvariantViolation,
                    false,
                    null,
                    diagnosticCode);
                return Cache(key, envelope, result);
            }

            result = new MatchCommandResult(
                envelope.CommandId,
                MatchCommandCode.Accepted,
                nextState.StateRevision,
                nextState.StateRevision,
                true,
                readyDiagnostic);
            state = nextState;
            Cache(key, envelope, result);
            PublishChanged();
            return result;
        }

        private MatchCommandResult ExecuteRefreshShop(
            CommandKey key,
            MatchCommandEnvelope envelope,
            MatchSeatState seat)
        {
            var rejection = ValidateEconomyCommand(envelope, seat, "refresh");
            if (rejection != null)
            {
                return Cache(key, envelope, rejection);
            }
            if (seat.Gold < MatchEconomyRules.RefreshCost)
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        MatchCommandCode.InsufficientGold,
                        false,
                        null,
                        "match.shop.refresh.gold.insufficient"));
            }

            var draft = new MatchEconomyTransactionDraft(state);
            var poolExhausted = draft.RefreshPlayerShop(seat.PlayerId);
            draft.SpendGold(seat.PlayerId, MatchEconomyRules.RefreshCost);
            return CommitCommand(
                key,
                envelope,
                draft.BuildState(true),
                poolExhausted
                    ? MatchCommandCode.PoolExhaustedDiagnostic
                    : MatchCommandCode.Accepted,
                poolExhausted
                    ? "match.shop.refresh.poolExhausted"
                    : "match.shop.refresh.accepted");
        }

        private MatchCommandResult ExecuteToggleShopFreeze(
            CommandKey key,
            MatchCommandEnvelope envelope,
            MatchSeatState seat)
        {
            var rejection = ValidateEconomyCommand(envelope, seat, "freeze");
            if (rejection != null)
            {
                return Cache(key, envelope, rejection);
            }

            var draft = new MatchEconomyTransactionDraft(state);
            if (!draft.ToggleFreeze(seat.PlayerId, state.Phase))
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        MatchCommandCode.AcceptedNoChange,
                        false,
                        state.StateRevision,
                        "match.shop.freeze.noChange"));
            }
            return CommitCommand(
                key,
                envelope,
                draft.BuildState(true),
                MatchCommandCode.Accepted,
                "match.shop.freeze.accepted");
        }

        private MatchCommandResult ExecutePurchaseShopOffer(
            CommandKey key,
            MatchCommandEnvelope envelope,
            MatchSeatState seat,
            PurchaseShopOfferCommand command)
        {
            var rejection = ValidateEconomyCommand(envelope, seat, "purchase");
            if (rejection != null)
            {
                return Cache(key, envelope, rejection);
            }

            var draft = new MatchEconomyTransactionDraft(state);
            if (!draft.TryPurchaseShopOffer(
                seat.PlayerId,
                command.SlotIndex,
                command.ExpectedUnitId,
                stagingSlotPolicy,
                out var acquisition,
                out var code,
                out var diagnosticCode))
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        code,
                        false,
                        null,
                        diagnosticCode));
            }
            return CommitCommand(
                key,
                envelope,
                draft.BuildState(true),
                MatchCommandCode.Accepted,
                diagnosticCode,
                acquisition);
        }

        private MatchCommandResult ExecutePurchaseLevelUpgrade(
            CommandKey key,
            MatchCommandEnvelope envelope,
            MatchSeatState seat,
            PurchaseLevelUpgradeCommand command)
        {
            var rejection = ValidateEconomyCommand(envelope, seat, "upgrade");
            if (rejection != null)
            {
                return Cache(key, envelope, rejection);
            }
            if (seat.Level == MatchEconomyRules.MaximumLevel)
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        MatchCommandCode.LevelMax,
                        false,
                        null,
                        "match.upgrade.level.max"));
            }
            if (command.ExpectedCurrentLevel != seat.Level)
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        MatchCommandCode.UpgradeLevelChanged,
                        false,
                        null,
                        "match.upgrade.level.changed"));
            }
            if (command.ExpectedCurrentPrice != seat.CurrentUpgradePrice)
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        MatchCommandCode.UpgradePriceChanged,
                        false,
                        null,
                        "match.upgrade.price.changed"));
            }
            if (seat.Gold < seat.CurrentUpgradePrice)
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        MatchCommandCode.InsufficientGold,
                        false,
                        null,
                        "match.upgrade.gold.insufficient"));
            }

            var draft = new MatchEconomyTransactionDraft(state);
            draft.PurchaseLevelUpgrade(seat.PlayerId);
            return CommitCommand(
                key,
                envelope,
                draft.BuildState(true),
                MatchCommandCode.Accepted,
                "match.upgrade.accepted");
        }

        private MatchCommandResult ValidateEconomyCommand(
            MatchCommandEnvelope envelope,
            MatchSeatState seat,
            string operation)
        {
            if (state.Phase != MatchPhase.Preparation && state.Phase != MatchPhase.Battle)
            {
                return CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.PhaseRejected,
                    false,
                    null,
                    "match.economy." + operation + ".phase.rejected");
            }
            if (seat.Eliminated)
            {
                return CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.Eliminated,
                    false,
                    null,
                    "match.economy." + operation + ".eliminated");
            }
            if (seat.ControllerKind == MatchControllerKind.Human
                && seat.ConnectionState != MatchConnectionState.Connected)
            {
                return CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.ConnectionRejected,
                    false,
                    null,
                    "match.economy." + operation + ".connection.rejected");
            }
            return null;
        }

        private MatchCommandResult CommitCommand(
            CommandKey key,
            MatchCommandEnvelope envelope,
            MatchState nextState,
            MatchCommandCode code,
            string diagnosticCode,
            MatchAcquisitionResult acquisition = null)
        {
            if (!MatchStateInvariant.TryValidate(
                nextState,
                stagingSlotPolicy,
                out var invariantDiagnostic))
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        MatchCommandCode.InternalInvariantViolation,
                        false,
                        null,
                        invariantDiagnostic));
            }

            state = nextState;
            var result = new MatchCommandResult(
                envelope.CommandId,
                code,
                state.StateRevision,
                state.StateRevision,
                true,
                diagnosticCode,
                acquisition);
            Cache(key, envelope, result);
            PublishChanged();
            return result;
        }

        private MatchTransactionResult CommitHost(
            MatchState nextState,
            string diagnosticCode,
            MatchCommandCode code = MatchCommandCode.Accepted,
            IEnumerable<string> retiredUnitIds = null)
        {
            if (!MatchStateInvariant.TryValidate(
                nextState,
                stagingSlotPolicy,
                out var invariantDiagnostic))
            {
                return TransactionRejected(
                    MatchCommandCode.InternalInvariantViolation,
                    invariantDiagnostic);
            }

            state = nextState;
            var result = new MatchTransactionResult(
                code,
                state.StateRevision,
                state.StateRevision,
                true,
                diagnosticCode,
                retiredUnitIds);
            PublishChanged();
            return result;
        }

        private MatchCommandResult Cache(
            CommandKey key,
            MatchCommandEnvelope envelope,
            MatchCommandResult result)
        {
            commandRecords[key] = new CommandRecord(
                envelope.PlayerId,
                envelope.CommandId,
                envelope.CanonicalContentSummary,
                result);
            return result;
        }

        private MatchCommandResult CommandResult(
            string commandId,
            MatchCommandCode code,
            bool changedState,
            long? acceptedStateRevision,
            string diagnosticCode)
        {
            return new MatchCommandResult(
                commandId,
                code,
                state.StateRevision,
                acceptedStateRevision,
                changedState,
                diagnosticCode);
        }

        private MatchTransactionResult TransactionRejected(
            MatchCommandCode code,
            string diagnosticCode)
        {
            return new MatchTransactionResult(
                code,
                state.StateRevision,
                null,
                false,
                diagnosticCode);
        }

        private MatchTransactionResult TransactionNoChange(string diagnosticCode)
        {
            return new MatchTransactionResult(
                MatchCommandCode.AcceptedNoChange,
                state.StateRevision,
                state.StateRevision,
                false,
                diagnosticCode);
        }

        private void PublishChanged()
        {
            var handler = Changed;
            if (handler != null)
            {
                handler(new MatchChangedEvent(ProjectPublic()));
            }
        }

        private struct CommandKey : IEquatable<CommandKey>
        {
            internal CommandKey(string playerId, string commandId)
            {
                PlayerId = playerId;
                CommandId = commandId;
            }

            private string PlayerId { get; }
            private string CommandId { get; }

            public bool Equals(CommandKey other)
            {
                return string.Equals(PlayerId, other.PlayerId, StringComparison.Ordinal)
                    && string.Equals(CommandId, other.CommandId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is CommandKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(PlayerId) * 397)
                        ^ StringComparer.Ordinal.GetHashCode(CommandId);
                }
            }
        }

        private sealed class CommandRecord
        {
            internal CommandRecord(
                string playerId,
                string commandId,
                string commandSummary,
                MatchCommandResult result)
            {
                PlayerId = playerId;
                CommandId = commandId;
                CommandSummary = commandSummary;
                Result = result;
            }

            internal string PlayerId { get; }
            internal string CommandId { get; }
            internal string CommandSummary { get; }
            internal MatchCommandResult Result { get; }
        }
    }
}
