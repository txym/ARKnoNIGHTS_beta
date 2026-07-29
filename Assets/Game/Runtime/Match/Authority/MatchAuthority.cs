using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    public sealed class MatchAuthority
    {
        private readonly Dictionary<CommandKey, CommandRecord> commandRecords =
            new Dictionary<CommandKey, CommandRecord>();
        private MatchState state;

        internal MatchAuthority(MatchState initialState)
        {
            state = initialState ?? throw new ArgumentNullException(nameof(initialState));
        }

        public event Action<MatchChangedEvent> Changed;

        public long StateRevision => state.StateRevision;
        public bool AllRequiredHumansReady
        {
            get
            {
                var required = state.Seats
                    .Where(seat => !seat.Eliminated && seat.ControllerKind == MatchControllerKind.Human)
                    .ToArray();
                return required.Length > 0 && required.All(seat => seat.Ready);
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
            if (!(envelope.Payload is SetPreparationReadyCommand readyCommand))
            {
                result = CommandResult(
                    envelope.CommandId,
                    MatchCommandCode.InvalidPayload,
                    false,
                    null,
                    "match.command.payload.unsupported");
                return Cache(key, envelope, result);
            }

            return ExecuteReady(key, envelope, seat, readyCommand);
        }

        public MatchTransactionResult TryEnterPreparation(int roundNumber)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.phase.ended");
            }
            var expectedRound = state.RoundNumber + 1;
            if ((state.Phase != MatchPhase.Initializing && state.Phase != MatchPhase.Settlement)
                || roundNumber != expectedRound)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.phase.preparation.invalid");
            }

            var seats = state.Seats.Select(
                seat => seat.ControllerKind == MatchControllerKind.Human && !seat.Eliminated
                    ? seat.With(ready: false)
                    : seat);
            return CommitHost(
                state.WithSeatsAndPhase(seats, MatchPhase.Preparation, roundNumber),
                "match.phase.preparation.accepted");
        }

        public MatchTransactionResult TryAdvancePhase(MatchPhase targetPhase)
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
            return CommitHost(
                state.WithPhase(targetPhase),
                "match.phase.advance.accepted");
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
                controllerKind: controllerKind);
            return CommitHost(
                state.WithSeat(nextSeat),
                "match.controller.accepted");
        }

        public MatchTransactionResult TryMarkEliminated(string playerId, int? placement)
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

        public MatchTransactionResult TryEndMatch(string reason)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionRejected(MatchCommandCode.InvalidTransition, "match.phase.ended");
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                return TransactionRejected(MatchCommandCode.InvalidPayload, "match.end.reason.invalid");
            }

            return CommitHost(state.WithPhase(MatchPhase.Ended, reason), "match.end.accepted");
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

            var nextState = state.WithSeat(seat.With(ready: command.DesiredReady));
            if (!MatchStateInvariant.TryValidate(nextState, out var diagnosticCode))
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
                "match.ready.accepted");
            state = nextState;
            Cache(key, envelope, result);
            PublishChanged();
            return result;
        }

        private MatchTransactionResult CommitHost(MatchState nextState, string diagnosticCode)
        {
            if (!MatchStateInvariant.TryValidate(nextState, out var invariantDiagnostic))
            {
                return TransactionRejected(
                    MatchCommandCode.InternalInvariantViolation,
                    invariantDiagnostic);
            }

            state = nextState;
            var result = new MatchTransactionResult(
                MatchCommandCode.Accepted,
                state.StateRevision,
                state.StateRevision,
                true,
                diagnosticCode);
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
