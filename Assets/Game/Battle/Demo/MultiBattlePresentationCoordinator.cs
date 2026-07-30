using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Presentation;

namespace ArknoNights.Battle.Demo
{
    /// <summary>One immutable Core input assigned to a stable local match identifier.</summary>
    public sealed class BattleMatchRequest
    {
        public BattleMatchRequest(string matchId, BattleInput input)
            : this(matchId, input, input == null
                ? string.Empty
                : BattleInputSha256.Compute(input))
        {
        }

        public BattleMatchRequest(
            string matchId,
            BattleInput input,
            string sealedInputHash)
        {
            MatchId = matchId ?? string.Empty;
            Input = input;
            SealedInputHash = sealedInputHash ?? string.Empty;
        }

        public string MatchId { get; }
        public BattleInput Input { get; }
        public string SealedInputHash { get; }
    }

    /// <summary>Maps one player to one supplied match and observer projection.</summary>
    public readonly struct PlayerBattleObservation
    {
        public PlayerBattleObservation(
            string playerId,
            string matchId,
            BattleObserverView observer)
        {
            PlayerId = playerId ?? string.Empty;
            MatchId = matchId ?? string.Empty;
            Observer = observer;
        }

        public string PlayerId { get; }
        public string MatchId { get; }
        public BattleObserverView Observer { get; }
    }

    /// <summary>
    /// Incremental local match state. Result and compressed Track become available
    /// only after the terminal chunk; Stream is playable after its first chunk.
    /// </summary>
    public sealed class BattleMatchPresentation
    {
        internal BattleMatchPresentation(
            BattleMatchRequest request,
            BattleSimulationProducer producer,
            BattlePresentationStreamBuffer stream)
        {
            MatchId = request.MatchId;
            Input = request.Input;
            SealedInputHash = request.SealedInputHash;
            Producer = producer;
            Stream = stream;
        }

        public string MatchId { get; }
        public BattleInput Input { get; }
        public string SealedInputHash { get; }
        public BattleRunResult Result { get; private set; }
        public BattlePresentationTrack Track { get; private set; }
        public BattlePresentationStreamBuffer Stream { get; }
        public int ProducedThroughTick =>
            Producer.ProducedThroughTick;
        public int ComputedThroughTick =>
            Producer.ComputedThroughTick;
        public bool HasFirstChunk => Stream.HasFirstChunk;
        public bool IsTerminal => Stream.IsTerminal;
        public string WinnerOrReason
        {
            get
            {
                if (Result == null)
                    return "Computing";
                switch (Result.Outcome)
                {
                    case BattleOutcome.HomeWin:
                        return BattleSide.Home.ToString();
                    case BattleOutcome.AwayWin:
                        return BattleSide.Away.ToString();
                    default:
                        return BattleOutcome.Draw.ToString();
                }
            }
        }

        public BattleResolution GetBattleResolution()
        {
            if (!IsTerminal)
                throw new InvalidOperationException(
                    "The battle has not reached a terminal state.");
            return Producer.GetBattleResolution();
        }

        public FinalSecondHashPayload GetFinalSecondHashPayload()
        {
            if (!IsTerminal)
                throw new InvalidOperationException(
                    "The battle has not reached a terminal state.");
            return Producer.GetFinalSecondHashPayload();
        }

        internal BattleSimulationProducer Producer { get; }
        internal bool ProducerHasFirstChunk =>
            Producer.HasFirstChunk;
        internal bool ProducerIsTerminal =>
            Producer.IsTerminal;
        private readonly List<BattleSimulationChunk>
            deferredInitialTerminalChunks =
                new List<BattleSimulationChunk>();

        internal void DeferInitialTerminalChunk(
            BattleSimulationChunk chunk)
        {
            deferredInitialTerminalChunks.Add(chunk);
        }

        internal bool TryAppendChunk(
            BattleSimulationChunk chunk,
            out string error)
        {
            return Stream.TryAppendChunk(chunk, out error);
        }

        internal bool TryFlushDeferredInitialTerminalChunks(
            out string error)
        {
            foreach (var chunk in deferredInitialTerminalChunks)
                if (!Stream.TryAppendChunk(chunk, out error))
                    return false;
            deferredInitialTerminalChunks.Clear();
            error = string.Empty;
            return true;
        }

        internal bool TryComplete(
            out IReadOnlyList<BattlePresentationDiagnostic>
                diagnostics)
        {
            if (Result != null)
            {
                diagnostics =
                    Array.Empty<BattlePresentationDiagnostic>();
                return true;
            }
            if (!Stream.IsTerminal)
            {
                diagnostics = new[]
                {
                    new BattlePresentationDiagnostic(
                        "multi.track.notPublished",
                        MatchId)
                };
                return false;
            }
            Result = Producer.GetTerminalResult();
            var compiler = new BattlePresentationTrackCompiler();
            if (compiler.TryCompile(
                    Result,
                    out var track,
                    out diagnostics))
            {
                Track = track;
                return true;
            }
            Result = null;
            return false;
        }
    }

    public enum MultiBattlePresentationState
    {
        Idle,
        Preparing,
        Ready,
        Playing,
        Buffering,
        Paused,
        Completed,
        Error
    }

    /// <summary>
    /// Advances all supplied battles with a stable round-robin budget and samples
    /// them on one shared presentation clock. Pairing remains owned by the caller.
    /// </summary>
    public sealed class MultiBattlePresentationCoordinator :
        IDisposable
    {
        private const int DefaultComputationBudgetPerAdvance = 200;

        private readonly BattleTrackPlaybackController playback =
            new BattleTrackPlaybackController();
        private readonly List<BattleMatchPresentation> matches =
            new List<BattleMatchPresentation>();
        private readonly ReadOnlyCollection<BattleMatchPresentation>
            matchView;
        private readonly Dictionary<string, PlayerBattleObservation>
            observations =
                new Dictionary<string, PlayerBattleObservation>(
                    StringComparer.Ordinal);
        private IBattlePresentationViewFactory factory;
        private double presentationTick;
        private float speed = 1f;
        private int roundRobinCursor;

        public MultiBattlePresentationCoordinator()
        {
            matchView =
                new ReadOnlyCollection<BattleMatchPresentation>(
                    matches);
        }

        public MultiBattlePresentationState State { get; private set; }
            = MultiBattlePresentationState.Idle;
        public double PresentationTick => presentationTick;
        public float Speed => speed;
        public string SelectedPlayerId { get; private set; }
            = string.Empty;
        public string SelectedMatchId { get; private set; }
            = string.Empty;
        public BattleObserverView Observer { get; private set; }
            = BattleObserverView.Home;
        public IReadOnlyList<BattleMatchPresentation> Matches =>
            matchView;
        public IReadOnlyList<BattlePresentationViewState>
            PresentationViewStates => playback.ViewStates;
        public string LastError { get; private set; } = string.Empty;
        public int CompletionTransitionCount { get; private set; }
        public bool AllFirstChunksReady =>
            matches.Count != 0
            && matches.All(item => item.HasFirstChunk);
        private bool AllProducerFirstChunksReady =>
            matches.Count != 0
            && matches.All(item =>
                item.ProducerHasFirstChunk);
        public bool AllBattlesTerminal =>
            matches.Count != 0
            && matches.All(item => item.IsTerminal);
        public bool AllTracksReady =>
            AllBattlesTerminal
            && matches.All(item => item.Track != null);
        public int CommonAvailableThroughTick
        {
            get
            {
                if (matches.Count == 0)
                    return 0;
                var active = matches
                    .Where(item => !item.IsTerminal)
                    .ToArray();
                return active.Length == 0
                    ? matches.Max(item =>
                        item.Stream.AvailableThroughTick)
                    : active.Min(item =>
                        item.Stream.AvailableThroughTick);
            }
        }

        public int GlobalRoundEndTick => AllBattlesTerminal
            ? MaximumEndTick()
            : matches.Count == 0
                ? 0
                : matches.Max(item => item.Input.MaxTicks);

        public string StableSummary
        {
            get
            {
                return string.Join(
                           "|",
                           matches.Select(match =>
                               match.MatchId
                               + ":"
                               + Digest(match.Input.CanonicalSummary)
                               + ":"
                               + match.ProducedThroughTick
                               + ":"
                               + (match.Track == null
                                   ? "streaming"
                                   : match.Track.SourceEventDigest
                                     + ":"
                                     + match.Track.StableSummary)
                               + ":"
                               + match.WinnerOrReason))
                    + "|selected="
                    + SelectedPlayerId
                    + ":"
                    + SelectedMatchId
                    + ":"
                    + Observer
                    + "|tick="
                    + presentationTick.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                    + "|speed="
                    + speed.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                    + "|state="
                    + State;
            }
        }

        public bool Prepare(
            IReadOnlyList<BattleMatchRequest> requests,
            IReadOnlyList<PlayerBattleObservation> mapping,
            IBattlePresentationViewFactory viewFactory,
            string initiallyObservedPlayerId)
        {
            Reset();
            if (requests == null || requests.Count == 0)
                return Fail("multi.requests.missing", string.Empty);
            if (viewFactory == null)
                return Fail(
                    "multi.playback.bind.failed",
                    "factory");

            factory = viewFactory;
            var matchIds = new HashSet<string>(
                StringComparer.Ordinal);
            var battleIds = new HashSet<string>(
                StringComparer.Ordinal);
            var inputs = new HashSet<BattleInput>();
            foreach (var request in requests)
            {
                if (request == null
                    || string.IsNullOrWhiteSpace(request.MatchId)
                    || request.Input == null)
                    return Fail(
                        "multi.matchId.invalid",
                        request == null
                            ? string.Empty
                            : request.MatchId);
                if (!matchIds.Add(request.MatchId))
                    return Fail(
                        "multi.matchId.duplicate",
                        request.MatchId);
                if (!battleIds.Add(request.Input.BattleId))
                    return Fail(
                        "multi.battleId.duplicate",
                        request.Input.BattleId);
                if (!inputs.Add(request.Input))
                    return Fail(
                        "multi.input.duplicate",
                        request.MatchId);

                BattleSimulationProducer producer;
                try
                {
                    producer = new BattleSimulationProducer(
                        request.Input,
                        request.SealedInputHash);
                }
                catch (Exception exception)
                {
                    return Fail(
                        "multi.core.failed",
                        request.MatchId
                        + "/"
                        + exception.GetType().Name);
                }
                matches.Add(
                    new BattleMatchPresentation(
                        request,
                        producer,
                        new BattlePresentationStreamBuffer(
                            request.Input.BattleId,
                            request.SealedInputHash)));
            }

            if (mapping == null || mapping.Count == 0)
                return Fail(
                    "multi.observation.count.invalid",
                    mapping == null
                        ? string.Empty
                        : "0");
            foreach (var observation in mapping)
            {
                if (string.IsNullOrWhiteSpace(
                        observation.PlayerId)
                    || !observations.TryAdd(
                        observation.PlayerId,
                        observation)
                    || !matchIds.Contains(
                        observation.MatchId))
                    return Fail(
                        "multi.observation.invalid",
                        observation.PlayerId
                        + "/"
                        + observation.MatchId);
                var match = matches.Single(item =>
                    item.MatchId == observation.MatchId);
                var expected = match.Input.Players
                    .Single(player =>
                        player.Side
                        == (observation.Observer
                            == BattleObserverView.Home
                            ? BattleSide.Home
                            : BattleSide.Away))
                    .PlayerId;
                if (!string.Equals(
                        expected,
                        observation.PlayerId,
                        StringComparison.Ordinal))
                    return Fail(
                        "multi.observation.playerMismatch",
                        observation.PlayerId
                        + "/"
                        + observation.MatchId);
            }

            if (!SelectObservedPlayer(
                    initiallyObservedPlayerId))
                return false;
            State = MultiBattlePresentationState.Preparing;
            return true;
        }

        public bool PumpComputation(int authoritativeTickBudget)
        {
            if (authoritativeTickBudget < 0)
                return Fail(
                    "multi.budget.invalid",
                    authoritativeTickBudget.ToString(
                        CultureInfo.InvariantCulture));
            if (State == MultiBattlePresentationState.Idle
                || State == MultiBattlePresentationState.Error)
                return false;
            var consumed = 0;
            while (consumed < authoritativeTickBudget
                   && matches.Any(item =>
                       !item.ProducerIsTerminal))
            {
                var match = matches[roundRobinCursor];
                roundRobinCursor =
                    (roundRobinCursor + 1) % matches.Count;
                if (match.ProducerIsTerminal)
                    continue;
                IReadOnlyList<BattleSimulationChunk> published;
                try
                {
                    published = match.Producer.Advance(1);
                }
                catch (Exception exception)
                {
                    return Fail(
                        "multi.core.failed",
                        match.MatchId
                        + "/"
                        + exception.GetType().Name);
                }
                consumed++;
                foreach (var chunk in published)
                {
                    if (chunk.IsTerminal
                        && !AllProducerFirstChunksReady)
                    {
                        match.DeferInitialTerminalChunk(chunk);
                        continue;
                    }
                    if (!match.TryAppendChunk(chunk, out var error))
                        return Fail(
                            "multi.stream.failed",
                            match.MatchId + "/" + error);
                }

                if (AllProducerFirstChunksReady)
                {
                    foreach (var deferredMatch in matches)
                        if (!deferredMatch
                                .TryFlushDeferredInitialTerminalChunks(
                                    out var error))
                            return Fail(
                                "multi.stream.failed",
                                deferredMatch.MatchId + "/" + error);
                }

                foreach (var terminalMatch in matches
                             .Where(item =>
                                 item.IsTerminal
                                 && item.Result == null))
                {
                    if (terminalMatch.TryComplete(
                            out var diagnostics))
                        continue;
                    return Fail(
                        "multi.track.failed",
                        terminalMatch.MatchId
                        + "/"
                        + string.Join(
                            ";",
                            diagnostics.Select(item =>
                                item.ToString()).ToArray()));
                }
                if (State
                        == MultiBattlePresentationState.Preparing
                    && AllFirstChunksReady)
                    break;
            }

            if (State == MultiBattlePresentationState.Preparing
                && AllFirstChunksReady)
            {
                if (!BindSelected())
                    return false;
                State = MultiBattlePresentationState.Ready;
            }
            else if (State
                         == MultiBattlePresentationState.Buffering
                     && CommonAvailableThroughTick
                         > presentationTick)
            {
                playback.SetPlaybackSpeed(speed);
                State = MultiBattlePresentationState.Playing;
            }
            return true;
        }

        public bool Play()
        {
            if (State != MultiBattlePresentationState.Ready
                && State != MultiBattlePresentationState.Paused)
                return false;
            playback.SetPlaybackSpeed(speed);
            State = MultiBattlePresentationState.Playing;
            return true;
        }

        public bool PrepareCompletedPlayback()
        {
            if (!AllTracksReady
                || string.IsNullOrEmpty(SelectedPlayerId))
                return false;
            presentationTick = 0d;
            if (!SelectObservedPlayer(
                    SelectedPlayerId,
                    true))
                return false;
            State = MultiBattlePresentationState.Ready;
            playback.SetPlaybackSpeed(0f);
            return true;
        }

        public bool Pause()
        {
            if (State != MultiBattlePresentationState.Playing
                && State != MultiBattlePresentationState.Buffering)
                return false;
            playback.SetPlaybackSpeed(0f);
            State = MultiBattlePresentationState.Paused;
            return true;
        }

        public bool Resume()
        {
            return Play();
        }

        public bool Replay()
        {
            if (!AllBattlesTerminal
                || string.IsNullOrEmpty(SelectedPlayerId))
                return false;
            presentationTick = 0d;
            if (!SelectObservedPlayer(
                    SelectedPlayerId,
                    true))
                return false;
            State = MultiBattlePresentationState.Ready;
            return Play();
        }

        public bool SetSpeed(float value)
        {
            if (value <= 0f)
                return false;
            speed = value;
            if (State == MultiBattlePresentationState.Playing)
                playback.SetPlaybackSpeed(value);
            return true;
        }

        public bool SelectObservedPlayer(string playerId)
        {
            if (!CanObservePlayer(playerId))
                return false;
            return SelectObservedPlayer(playerId, false);
        }

        public bool CanObservePlayer(string playerId)
        {
            return observations.ContainsKey(playerId ?? string.Empty);
        }

        public void Advance(float unscaledDeltaSeconds)
        {
            Advance(
                unscaledDeltaSeconds,
                DefaultComputationBudgetPerAdvance);
        }

        public void Advance(
            float unscaledDeltaSeconds,
            int computationTickBudget)
        {
            if (unscaledDeltaSeconds <= 0f
                || computationTickBudget < 0)
                return;
            if (State
                    == MultiBattlePresentationState.Preparing
                || (State
                        == MultiBattlePresentationState.Playing
                    && !AllBattlesTerminal
                    && CommonAvailableThroughTick
                        < presentationTick
                          + BattleSimulationProducer
                              .AuthoritativeTicksPerFullChunk)
                || State
                    == MultiBattlePresentationState.Buffering)
            {
                if (!PumpComputation(computationTickBudget))
                    return;
            }
            if (State != MultiBattlePresentationState.Playing
                && State
                    != MultiBattlePresentationState.Buffering)
                return;

            var maximumAvailableTick =
                CommonAvailableThroughTick;
            if (State
                    == MultiBattlePresentationState.Buffering
                && maximumAvailableTick <= presentationTick)
                return;
            if (State == MultiBattlePresentationState.Buffering)
            {
                playback.SetPlaybackSpeed(speed);
                State = MultiBattlePresentationState.Playing;
            }

            var requestedTick = presentationTick
                + unscaledDeltaSeconds
                * speed
                * BattleInput.TicksPerSecond;
            presentationTick = Math.Min(
                maximumAvailableTick,
                requestedTick);
            if (!playback.RenderAt(
                    presentationTick,
                    out var diagnostics))
            {
                Fail(
                    "multi.playback.render.failed",
                    string.Join(
                        ";",
                        diagnostics.Select(item =>
                            item.ToString()).ToArray()));
                return;
            }
            if (requestedTick > maximumAvailableTick
                && !AllBattlesTerminal)
            {
                playback.SetPlaybackSpeed(0f);
                State =
                    MultiBattlePresentationState.Buffering;
                return;
            }

            if (AllBattlesTerminal
                && presentationTick >= MaximumEndTick()
                && !playback.HasPendingTerminalPresentation)
            {
                State = MultiBattlePresentationState.Completed;
                CompletionTransitionCount++;
            }
        }

        public bool AdvanceToAuthoritativeTick(
            int authoritativeTick,
            int computationTickBudget)
        {
            if (authoritativeTick < 0 || computationTickBudget < 0)
                return false;
            if (State == MultiBattlePresentationState.Preparing
                || State == MultiBattlePresentationState.Ready
                || State == MultiBattlePresentationState.Playing
                || State == MultiBattlePresentationState.Buffering)
            {
                if (!PumpComputation(computationTickBudget))
                    return false;
            }
            if (State == MultiBattlePresentationState.Ready)
            {
                if (!Play())
                    return false;
            }
            if (State != MultiBattlePresentationState.Playing
                && State != MultiBattlePresentationState.Buffering)
                return State == MultiBattlePresentationState.Completed;

            var target = AllBattlesTerminal
                ? Math.Min(authoritativeTick, MaximumEndTick())
                : authoritativeTick;
            var available = CommonAvailableThroughTick;
            presentationTick = Math.Min(target, available);
            if (!playback.RenderAt(presentationTick, out var diagnostics))
            {
                return Fail(
                    "multi.playback.render.failed",
                    string.Join(
                        ";",
                        diagnostics.Select(item => item.ToString()).ToArray()));
            }
            if (target > available && !AllBattlesTerminal)
            {
                playback.SetPlaybackSpeed(0f);
                State = MultiBattlePresentationState.Buffering;
                return true;
            }
            playback.SetPlaybackSpeed(speed);
            State = MultiBattlePresentationState.Playing;
            if (AllBattlesTerminal
                && presentationTick >= MaximumEndTick()
                && !playback.HasPendingTerminalPresentation)
            {
                State = MultiBattlePresentationState.Completed;
                CompletionTransitionCount++;
            }
            return true;
        }

        public void Reset()
        {
            playback.Clear();
            matches.Clear();
            observations.Clear();
            factory = null;
            presentationTick = 0d;
            speed = 1f;
            roundRobinCursor = 0;
            SelectedPlayerId = string.Empty;
            SelectedMatchId = string.Empty;
            Observer = BattleObserverView.Home;
            LastError = string.Empty;
            CompletionTransitionCount = 0;
            State = MultiBattlePresentationState.Idle;
        }

        public void Dispose()
        {
            Reset();
        }

        private bool SelectObservedPlayer(
            string playerId,
            bool forceRebind)
        {
            if (!observations.TryGetValue(
                    playerId ?? string.Empty,
                    out var observation))
                return Fail(
                    "multi.observation.invalid",
                    playerId);
            var match = matches.Single(item =>
                item.MatchId == observation.MatchId);
            if (!forceRebind
                && string.Equals(
                    SelectedMatchId,
                    match.MatchId,
                    StringComparison.Ordinal)
                && Observer == observation.Observer)
            {
                SelectedPlayerId = observation.PlayerId;
                return true;
            }

            SelectedPlayerId = observation.PlayerId;
            SelectedMatchId = match.MatchId;
            Observer = observation.Observer;
            if (!match.HasFirstChunk)
                return true;
            return BindSelected();
        }

        private bool BindSelected()
        {
            if (!observations.TryGetValue(
                    SelectedPlayerId,
                    out var observation))
                return Fail(
                    "multi.observation.invalid",
                    SelectedPlayerId);
            var match = matches.Single(item =>
                item.MatchId == observation.MatchId);
            IReadOnlyList<BattlePresentationDiagnostic>
                diagnostics;
            var bound = match.Track != null
                ? playback.Bind(
                    match.Track,
                    factory,
                    observation.Observer,
                    presentationTick,
                    out diagnostics)
                : playback.Bind(
                    match.Stream,
                    factory,
                    observation.Observer,
                    presentationTick,
                    out diagnostics);
            if (!bound)
                return Fail(
                    "multi.playback.bind.failed",
                    match.MatchId
                    + "/"
                    + string.Join(
                        ";",
                        diagnostics.Select(item =>
                            item.ToString()).ToArray()));
            playback.SetPlaybackSpeed(
                State == MultiBattlePresentationState.Paused
                || State
                    == MultiBattlePresentationState.Buffering
                    ? 0f
                    : speed);
            SelectedMatchId = match.MatchId;
            Observer = observation.Observer;
            return true;
        }

        private int MaximumEndTick()
        {
            return matches.Count == 0
                ? 0
                : matches.Max(item =>
                    item.Stream.AvailableThroughTick);
        }

        private bool Fail(string code, string detail)
        {
            LastError = code + ":" + (detail ?? string.Empty);
            playback.Clear();
            matches.Clear();
            observations.Clear();
            factory = null;
            State = MultiBattlePresentationState.Error;
            return false;
        }

        private static string Digest(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var character in value ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return hash.ToString(
                    "X8",
                    CultureInfo.InvariantCulture);
            }
        }
    }
}
