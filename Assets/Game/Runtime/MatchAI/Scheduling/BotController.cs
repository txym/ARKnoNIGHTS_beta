using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Match;

namespace ArknoNights.MatchAI
{
    public sealed class BotRuntimeState
    {
        internal BotRuntimeState(
            string playerId,
            long controllerGeneration,
            int roundNumber,
            long nextDecisionOrdinal,
            long nextDecisionAtOffsetMs,
            PendingTakeover pendingTakeover,
            BotIntent lastIntent,
            string lastResultCode)
        {
            PlayerId = playerId ?? string.Empty;
            ControllerGeneration = controllerGeneration;
            RoundNumber = roundNumber;
            NextDecisionOrdinal = nextDecisionOrdinal;
            NextDecisionAtOffsetMs = nextDecisionAtOffsetMs;
            PendingTakeover = pendingTakeover;
            LastIntent = lastIntent;
            LastResultCode = lastResultCode ?? string.Empty;
            CanonicalSummary = BotCanonical.Build(
                "BotRuntimeState",
                PlayerId,
                ControllerGeneration,
                RoundNumber,
                NextDecisionOrdinal,
                NextDecisionAtOffsetMs,
                PendingTakeover == null ? string.Empty : PendingTakeover.CanonicalSummary,
                LastIntent == null ? string.Empty : LastIntent.CanonicalSummary,
                LastResultCode);
        }

        public string PlayerId { get; }
        public long ControllerGeneration { get; }
        public int RoundNumber { get; }
        public long NextDecisionOrdinal { get; }
        public long NextDecisionAtOffsetMs { get; }
        public PendingTakeover PendingTakeover { get; }
        public BotIntent LastIntent { get; }
        public string LastResultCode { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class BotController : IMatchPreparationEntryParticipant
    {
        public const long DecisionIntervalMs = 1000;
        public const long LastDecisionOffsetMs = 29000;

        private readonly Dictionary<string, BotRuntimeState> runtimes =
            new Dictionary<string, BotRuntimeState>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingTakeover> pendingTakeovers =
            new Dictionary<string, PendingTakeover>(StringComparer.Ordinal);
        private readonly HashSet<string> permanentlyQuitPlayers =
            new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<BotRuntimeState> RuntimeStates =>
            new ReadOnlyCollection<BotRuntimeState>(
                runtimes.Values
                    .OrderBy(state => state.PlayerId, StringComparer.Ordinal)
                    .ToArray());

        public IReadOnlyList<PendingTakeover> PendingTakeovers =>
            new ReadOnlyCollection<PendingTakeover>(
                pendingTakeovers.Values
                    .OrderBy(item => item.PlayerId, StringComparer.Ordinal)
                    .ToArray());

        public string CanonicalSummary =>
            string.Join(
                "||",
                permanentlyQuitPlayers
                    .OrderBy(playerId => playerId, StringComparer.Ordinal)
                    .Select(playerId => BotCanonical.Build(
                        "PermanentlyQuitPlayer",
                        playerId))
                    .Concat(pendingTakeovers.Values
                    .OrderBy(item => item.PlayerId, StringComparer.Ordinal)
                    .Select(item => item.CanonicalSummary))
                    .Concat(RuntimeStates.Select(item => item.CanonicalSummary)));

        public bool TryRun(IMatchBotHost host, out string diagnosticCode)
        {
            if (host == null)
            {
                diagnosticCode = "match.ai.controller.host.null";
                return false;
            }
            var seats = host.ProjectBotControlSeats();
            var phase = seats.FirstOrDefault()?.Phase ?? MatchPhase.Initializing;
            var round = seats.FirstOrDefault()?.RoundNumber ?? 0;
            if (phase != MatchPhase.Preparation || round < 1)
            {
                diagnosticCode = "match.ai.controller.entry.phase.invalid";
                return false;
            }

            foreach (var pending in pendingTakeovers.Values
                .Where(item => item.EffectivePreparationRound <= round)
                .OrderBy(item => SeatIndexOf(seats, item.PlayerId))
                .ToArray())
            {
                var pendingSeat = host.ProjectBotControlSeats().FirstOrDefault(item =>
                    string.Equals(
                        item.PlayerId,
                        pending.PlayerId,
                        StringComparison.Ordinal));
                if (pendingSeat == null
                    || pendingSeat.Eliminated
                    || pendingSeat.ControllerKind != MatchControllerKind.Human)
                {
                    pendingTakeovers.Remove(pending.PlayerId);
                    continue;
                }
                var activation = host.ActivateTakeoverBot(pending.PlayerId);
                if (!activation.Accepted)
                {
                    diagnosticCode = activation.DiagnosticCode;
                    return false;
                }
                pendingTakeovers.Remove(pending.PlayerId);
            }

            foreach (var seat in host.ProjectBotControlSeats()
                .Where(IsActiveBot)
                .OrderBy(item => item.SeatIndex))
            {
                runtimes[seat.PlayerId] = NewRuntime(
                    seat,
                    0,
                    0,
                    null,
                    "match.ai.runtime.round.opened");
                RunCycle(
                    host,
                    seat.PlayerId,
                    seat.PreparationStartedAtHostMs,
                    DecisionIntervalMs);
            }
            SyncPendingIntoRuntime();
            diagnosticCode = string.Empty;
            return true;
        }

        public bool TryAdvance(
            IMatchBotHost host,
            long hostMonotonicNowMs,
            out string diagnosticCode)
        {
            if (host == null)
            {
                diagnosticCode = "match.ai.controller.host.null";
                return false;
            }
            var seats = host.ProjectBotControlSeats();
            var first = seats.FirstOrDefault();
            if (first == null
                || first.Phase != MatchPhase.Preparation
                || hostMonotonicNowMs < first.LastHostMonotonicMs)
            {
                diagnosticCode = "match.ai.controller.advance.invalid";
                return false;
            }
            if (hostMonotonicNowMs >= first.PreparationDeadlineHostMs)
            {
                diagnosticCode = string.Empty;
                return true;
            }
            var dueThrough = Math.Min(
                LastDecisionOffsetMs,
                hostMonotonicNowMs - first.PreparationStartedAtHostMs);

            while (true)
            {
                seats = host.ProjectBotControlSeats();
                var due = seats
                    .Where(IsActiveBot)
                    .Select(seat => runtimes.TryGetValue(seat.PlayerId, out var runtime)
                        && runtime.ControllerGeneration == seat.ControllerGeneration
                        && runtime.RoundNumber == seat.RoundNumber
                            ? runtime.NextDecisionAtOffsetMs
                            : long.MaxValue)
                    .Where(offset => offset <= dueThrough)
                    .DefaultIfEmpty(long.MaxValue)
                    .Min();
                if (due == long.MaxValue)
                {
                    break;
                }
                foreach (var seat in seats
                    .Where(IsActiveBot)
                    .Where(seat =>
                        runtimes.TryGetValue(seat.PlayerId, out var runtime)
                        && runtime.ControllerGeneration == seat.ControllerGeneration
                        && runtime.RoundNumber == seat.RoundNumber
                        && runtime.NextDecisionAtOffsetMs == due)
                    .OrderBy(seat => seat.SeatIndex)
                    .ToArray())
                {
                    RunCycle(
                        host,
                        seat.PlayerId,
                        checked(seat.PreparationStartedAtHostMs + due),
                        checked(due + DecisionIntervalMs));
                }
            }
            SyncPendingIntoRuntime();
            diagnosticCode = string.Empty;
            return true;
        }

        public BotLifecycleResult NotifyDisconnected(
            IMatchBotHost host,
            string playerId)
        {
            if (!TryFindSeat(host, playerId, out var seat, out var invalid))
            {
                return invalid;
            }
            if (seat.IsHost
                || seat.ControllerKind != MatchControllerKind.Human
                || seat.Eliminated)
            {
                return Rejected("match.ai.disconnect.controller.rejected");
            }
            var transaction = host.SetHumanDisconnectedGrace(playerId);
            if (!transaction.Accepted)
            {
                return From(transaction);
            }
            var effective = seat.Phase == MatchPhase.Preparation
                ? checked(seat.RoundNumber + 1)
                : checked(seat.RoundNumber + 2);
            pendingTakeovers[playerId] = new PendingTakeover(
                playerId,
                seat.RoundNumber,
                seat.Phase,
                effective,
                true);
            SyncPendingIntoRuntime();
            return From(transaction);
        }

        public BotLifecycleResult NotifyVoluntaryQuit(
            IMatchBotHost host,
            string playerId)
        {
            if (!TryFindSeat(host, playerId, out var seat, out var invalid))
            {
                return invalid;
            }
            if (seat.IsHost
                || seat.ControllerKind != MatchControllerKind.Human
                || seat.Eliminated)
            {
                return Rejected("match.ai.quit.controller.rejected");
            }
            var quit = host.MarkHumanQuit(playerId);
            if (!quit.Accepted)
            {
                return From(quit);
            }
            permanentlyQuitPlayers.Add(playerId);
            if (seat.Phase == MatchPhase.Preparation
                && seat.LastHostMonotonicMs < seat.PreparationDeadlineHostMs)
            {
                var activation = host.ActivateTakeoverBot(playerId);
                if (!activation.Accepted)
                {
                    return From(activation);
                }
                pendingTakeovers.Remove(playerId);
                var active = host.ProjectBotControlSeats().Single(item =>
                    string.Equals(item.PlayerId, playerId, StringComparison.Ordinal));
                var elapsed = Math.Max(
                    0,
                    active.LastHostMonotonicMs - active.PreparationStartedAtHostMs);
                var nextOffset = Math.Min(
                    LastDecisionOffsetMs + DecisionIntervalMs,
                    checked((elapsed / DecisionIntervalMs + 1) * DecisionIntervalMs));
                runtimes[playerId] = NewRuntime(
                    active,
                    0,
                    elapsed,
                    null,
                    "match.ai.runtime.takeover.activated");
                RunCycle(
                    host,
                    playerId,
                    active.LastHostMonotonicMs,
                    nextOffset);
                return new BotLifecycleResult(
                    MatchCommandCode.Accepted,
                    "match.ai.quit.takeover.immediate",
                    true);
            }
            pendingTakeovers[playerId] = new PendingTakeover(
                playerId,
                seat.RoundNumber,
                seat.Phase,
                checked(seat.RoundNumber + 1),
                false);
            SyncPendingIntoRuntime();
            return From(quit);
        }

        public BotLifecycleResult RestoreHumanControl(
            IMatchBotHost host,
            string playerId)
        {
            if (!TryFindSeat(host, playerId, out var seat, out var invalid))
            {
                return invalid;
            }
            if (seat.ControllerKind == MatchControllerKind.NativeBot
                || seat.Eliminated
                || permanentlyQuitPlayers.Contains(playerId)
                || (pendingTakeovers.TryGetValue(playerId, out var pending)
                    && !pending.ReconnectAllowed))
            {
                return Rejected("match.ai.restore.controller.rejected");
            }
            var transaction = host.RestoreHumanControl(playerId);
            if (!transaction.Accepted)
            {
                return From(transaction);
            }
            pendingTakeovers.Remove(playerId);
            runtimes.Remove(playerId);
            return From(transaction);
        }

        private void RunCycle(
            IMatchBotHost host,
            string playerId,
            long logicalHostNowMs,
            long nextOffset)
        {
            if (!runtimes.TryGetValue(playerId, out var runtime))
            {
                return;
            }
            if (!host.TryProjectForBot(
                playerId,
                out var source,
                out var projectionDiagnostic))
            {
                runtimes[playerId] = AdvanceRuntime(
                    runtime,
                    nextOffset,
                    BotIntent.Wait(projectionDiagnostic),
                    projectionDiagnostic);
                return;
            }
            if (!BotObservationFactory.TryCreate(
                source,
                logicalHostNowMs,
                runtime.NextDecisionOrdinal,
                out var observation,
                out var observationDiagnostic))
            {
                runtimes[playerId] = AdvanceRuntime(
                    runtime,
                    nextOffset,
                    BotIntent.Wait(observationDiagnostic),
                    observationDiagnostic);
                return;
            }

            var intent = BotDecisionMachine.Decide(observation);
            var resultCode = intent.DiagnosticCode;
            var adapterDiagnostic = string.Empty;
            if (intent.Kind != BotIntentKind.Wait
                && BotOperationAdapter.TryCreatePrimary(
                    observation,
                    intent,
                    source.ControllerGeneration,
                    out var primary,
                    out adapterDiagnostic))
            {
                var primaryResult = host.ExecuteBotOperation(primary);
                resultCode = primaryResult.Code.ToString();
                if (intent.Kind == BotIntentKind.Buy
                    && primaryResult.Accepted
                    && primaryResult.Acquisition != null
                    && host.TryProjectForBot(
                        playerId,
                        out var afterPurchase,
                        out _)
                    && BotOperationAdapter.TryCreateDeployFollowUp(
                        afterPurchase,
                        primaryResult.Acquisition,
                        runtime.NextDecisionOrdinal,
                        out var followUp,
                        out _))
                {
                    var deployResult = host.ExecuteBotOperation(followUp);
                    resultCode = primaryResult.Code + "/" + deployResult.Code;
                }
            }
            else if (intent.Kind != BotIntentKind.Wait)
            {
                resultCode = adapterDiagnostic;
            }
            runtimes[playerId] = AdvanceRuntime(
                runtime,
                nextOffset,
                intent,
                resultCode);
        }

        private static BotRuntimeState AdvanceRuntime(
            BotRuntimeState runtime,
            long nextOffset,
            BotIntent intent,
            string resultCode)
        {
            return new BotRuntimeState(
                runtime.PlayerId,
                runtime.ControllerGeneration,
                runtime.RoundNumber,
                checked(runtime.NextDecisionOrdinal + 1),
                nextOffset,
                runtime.PendingTakeover,
                intent,
                resultCode);
        }

        private static BotRuntimeState NewRuntime(
            MatchBotSeatControlSnapshot seat,
            long nextOrdinal,
            long nextOffset,
            PendingTakeover pending,
            string resultCode)
        {
            return new BotRuntimeState(
                seat.PlayerId,
                seat.ControllerGeneration,
                seat.RoundNumber,
                nextOrdinal,
                nextOffset,
                pending,
                null,
                resultCode);
        }

        private void SyncPendingIntoRuntime()
        {
            foreach (var pair in runtimes.ToArray())
            {
                pendingTakeovers.TryGetValue(pair.Key, out var pending);
                var current = pair.Value;
                runtimes[pair.Key] = new BotRuntimeState(
                    current.PlayerId,
                    current.ControllerGeneration,
                    current.RoundNumber,
                    current.NextDecisionOrdinal,
                    current.NextDecisionAtOffsetMs,
                    pending,
                    current.LastIntent,
                    current.LastResultCode);
            }
        }

        private static bool IsActiveBot(MatchBotSeatControlSnapshot seat)
        {
            return seat != null
                && !seat.Eliminated
                && (seat.ControllerKind == MatchControllerKind.NativeBot
                    || seat.ControllerKind == MatchControllerKind.TakeoverBot);
        }

        private static int SeatIndexOf(
            IEnumerable<MatchBotSeatControlSnapshot> seats,
            string playerId)
        {
            return seats.FirstOrDefault(item =>
                string.Equals(item.PlayerId, playerId, StringComparison.Ordinal))
                ?.SeatIndex ?? int.MaxValue;
        }

        private static bool TryFindSeat(
            IMatchBotHost host,
            string playerId,
            out MatchBotSeatControlSnapshot seat,
            out BotLifecycleResult invalid)
        {
            seat = host?.ProjectBotControlSeats().FirstOrDefault(item =>
                string.Equals(item.PlayerId, playerId, StringComparison.Ordinal));
            if (seat != null)
            {
                invalid = null;
                return true;
            }
            invalid = Rejected("match.ai.control.player.unknown");
            return false;
        }

        private static BotLifecycleResult From(MatchTransactionResult transaction)
        {
            return new BotLifecycleResult(
                transaction.Code,
                transaction.DiagnosticCode,
                transaction.ChangedState);
        }

        private static BotLifecycleResult Rejected(string diagnosticCode)
        {
            return new BotLifecycleResult(
                MatchCommandCode.ControllerRejected,
                diagnosticCode,
                false);
        }
    }
}
