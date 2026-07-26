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
        public BattleMatchRequest(string matchId, BattleInput input) { MatchId = matchId ?? string.Empty; Input = input; }
        public string MatchId { get; }
        public BattleInput Input { get; }
    }

    /// <summary>Maps one player to a completed match and the Home/Away projection used when observing it.</summary>
    public readonly struct PlayerBattleObservation
    {
        public PlayerBattleObservation(string playerId, string matchId, BattleObserverView observer)
        {
            PlayerId = playerId ?? string.Empty;
            MatchId = matchId ?? string.Empty;
            Observer = observer;
        }

        public string PlayerId { get; }
        public string MatchId { get; }
        public BattleObserverView Observer { get; }
    }

    /// <summary>Read-only Core result and compiled track for one local pairing.</summary>
    public sealed class BattleMatchPresentation
    {
        internal BattleMatchPresentation(string matchId, BattleInput input, BattleRunResult result, BattlePresentationTrack track)
        {
            MatchId = matchId;
            Input = input;
            Result = result;
            Track = track;
            WinnerOrReason = result.Winner.HasValue ? result.Winner.Value.ToString() : "Unresolved: " + result.StopReason;
        }

        public string MatchId { get; }
        public BattleInput Input { get; }
        public BattleRunResult Result { get; }
        public BattlePresentationTrack Track { get; }
        public string WinnerOrReason { get; }
    }

    public enum MultiBattlePresentationState { Idle, Ready, Playing, Paused, Completed, Error }

    /// <summary>
    /// Computes each supplied Core input once, then uses one presentation clock to sample only the selected
    /// match's immutable track. Changing observed player never owns or advances a BattleRunner.
    /// </summary>
    public sealed class MultiBattlePresentationCoordinator : IDisposable
    {
        private readonly BattleTrackPlaybackController playback = new BattleTrackPlaybackController();
        private readonly List<BattleMatchPresentation> matches = new List<BattleMatchPresentation>();
        private readonly ReadOnlyCollection<BattleMatchPresentation> matchView;
        private readonly Dictionary<string, PlayerBattleObservation> observations = new Dictionary<string, PlayerBattleObservation>(StringComparer.Ordinal);
        private IBattlePresentationViewFactory factory;
        private double presentationTick;
        private float speed = 1f;

        public MultiBattlePresentationCoordinator() { matchView = new ReadOnlyCollection<BattleMatchPresentation>(matches); }

        public MultiBattlePresentationState State { get; private set; } = MultiBattlePresentationState.Idle;
        public double PresentationTick => presentationTick;
        public float Speed => speed;
        public string SelectedPlayerId { get; private set; } = string.Empty;
        public string SelectedMatchId { get; private set; } = string.Empty;
        public BattleObserverView Observer { get; private set; } = BattleObserverView.Home;
        public IReadOnlyList<BattleMatchPresentation> Matches => matchView;
        public IReadOnlyList<BattlePresentationViewState> PresentationViewStates => playback.ViewStates;
        public string LastError { get; private set; } = string.Empty;
        public int CompletionTransitionCount { get; private set; }
        public string StableSummary => string.Join("|", matches.Select(match => match.MatchId + ":" + Digest(match.Input.CanonicalSummary) + ":" + match.Track.SourceEventDigest + ":" + match.Track.StableSummary + ":" + match.WinnerOrReason))
            + "|selected=" + SelectedPlayerId + ":" + SelectedMatchId + ":" + Observer
            + "|tick=" + presentationTick.ToString("R", CultureInfo.InvariantCulture)
            + "|speed=" + speed.ToString("R", CultureInfo.InvariantCulture)
            + "|state=" + State;

        public bool Prepare(IReadOnlyList<BattleMatchRequest> requests, IReadOnlyList<PlayerBattleObservation> mapping, IBattlePresentationViewFactory viewFactory, string initiallyObservedPlayerId)
        {
            Reset();
            if (requests == null || requests.Count == 0) return Fail("multi.requests.missing", string.Empty);
            if (viewFactory == null) return Fail("multi.playback.bind.failed", "factory");

            factory = viewFactory;
            var matchIds = new HashSet<string>(StringComparer.Ordinal);
            var battleIds = new HashSet<string>(StringComparer.Ordinal);
            var inputs = new HashSet<BattleInput>();
            foreach (var request in requests)
            {
                if (request == null || string.IsNullOrWhiteSpace(request.MatchId) || request.Input == null) return Fail("multi.matchId.invalid", request == null ? string.Empty : request.MatchId);
                if (!matchIds.Add(request.MatchId)) return Fail("multi.matchId.duplicate", request.MatchId);
                if (!battleIds.Add(request.Input.BattleId)) return Fail("multi.battleId.duplicate", request.Input.BattleId);
                if (!inputs.Add(request.Input)) return Fail("multi.input.duplicate", request.MatchId);

                BattleRunResult result;
                try { result = new BattleRunner(request.Input).RunToCompletion(); }
                catch (Exception exception) { return Fail("multi.core.failed", request.MatchId + "/" + exception.GetType().Name); }

                var compiler = new BattlePresentationTrackCompiler();
                if (!compiler.TryCompile(result, out var track, out var diagnostics))
                    return Fail("multi.track.failed", request.MatchId + "/" + request.Input.BattleId + "/" + string.Join(";", diagnostics.Select(item => item.ToString()).ToArray()));
                matches.Add(new BattleMatchPresentation(request.MatchId, request.Input, result, track));
            }

            if (mapping == null || mapping.Count != 4) return Fail("multi.observation.count.invalid", mapping == null ? string.Empty : mapping.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var observation in mapping)
            {
                if (string.IsNullOrWhiteSpace(observation.PlayerId) || !observations.TryAdd(observation.PlayerId, observation) || !matchIds.Contains(observation.MatchId))
                    return Fail("multi.observation.invalid", observation.PlayerId + "/" + observation.MatchId);
                var match = matches.Single(item => item.MatchId == observation.MatchId);
                var expected = observation.Observer == BattleObserverView.Home ? match.Result.HomePlayerId : match.Result.AwayPlayerId;
                if (!string.Equals(expected, observation.PlayerId, StringComparison.Ordinal))
                    return Fail("multi.observation.playerMismatch", observation.PlayerId + "/" + observation.MatchId);
            }

            if (!SelectObservedPlayer(initiallyObservedPlayerId)) return false;
            State = MultiBattlePresentationState.Ready;
            return true;
        }

        public bool Play()
        {
            if (State != MultiBattlePresentationState.Ready && State != MultiBattlePresentationState.Paused) return false;
            playback.SetPlaybackSpeed(speed);
            State = MultiBattlePresentationState.Playing;
            return true;
        }

        public bool Pause()
        {
            if (State != MultiBattlePresentationState.Playing) return false;
            playback.SetPlaybackSpeed(0f);
            State = MultiBattlePresentationState.Paused;
            return true;
        }

        public bool Resume() => Play();

        public bool Replay()
        {
            if (matches.Count == 0 || string.IsNullOrEmpty(SelectedPlayerId)) return false;
            presentationTick = 0d;
            if (!SelectObservedPlayer(SelectedPlayerId, true)) return false;
            State = MultiBattlePresentationState.Ready;
            return Play();
        }

        public bool SetSpeed(float value)
        {
            if (value <= 0f) return false;
            speed = value;
            if (State == MultiBattlePresentationState.Playing) playback.SetPlaybackSpeed(value);
            return true;
        }

        public bool SelectObservedPlayer(string playerId) => SelectObservedPlayer(playerId, false);

        private bool SelectObservedPlayer(string playerId, bool forceRebind)
        {
            if (!observations.TryGetValue(playerId ?? string.Empty, out var observation)) return Fail("multi.observation.invalid", playerId);
            var match = matches.Single(item => item.MatchId == observation.MatchId);
            if (!forceRebind && string.Equals(SelectedMatchId, match.MatchId, StringComparison.Ordinal) && Observer == observation.Observer)
            {
                SelectedPlayerId = observation.PlayerId;
                return true;
            }
            if (!playback.Bind(match.Track, factory, observation.Observer, presentationTick, out var diagnostics))
                return Fail("multi.playback.bind.failed", match.MatchId + "/" + string.Join(";", diagnostics.Select(item => item.ToString()).ToArray()));
            playback.SetPlaybackSpeed(State == MultiBattlePresentationState.Paused ? 0f : speed);
            SelectedPlayerId = observation.PlayerId;
            SelectedMatchId = match.MatchId;
            Observer = observation.Observer;
            return true;
        }

        public void Advance(float unscaledDeltaSeconds)
        {
            if (State != MultiBattlePresentationState.Playing || unscaledDeltaSeconds <= 0f) return;
            var maximumEndTick = MaximumEndTick();
            presentationTick = Math.Min(maximumEndTick, presentationTick + unscaledDeltaSeconds * speed * BattleInput.TicksPerSecond);
            if (!playback.RenderAt(presentationTick, out var diagnostics))
            {
                Fail("multi.playback.render.failed", string.Join(";", diagnostics.Select(item => item.ToString()).ToArray()));
                return;
            }
            if (presentationTick >= maximumEndTick && !playback.HasPendingTerminalPresentation)
            {
                State = MultiBattlePresentationState.Completed;
                CompletionTransitionCount++;
            }
        }

        public void Reset()
        {
            playback.Clear();
            matches.Clear();
            observations.Clear();
            factory = null;
            presentationTick = 0d;
            speed = 1f;
            SelectedPlayerId = string.Empty;
            SelectedMatchId = string.Empty;
            Observer = BattleObserverView.Home;
            LastError = string.Empty;
            CompletionTransitionCount = 0;
            State = MultiBattlePresentationState.Idle;
        }

        public void Dispose() => Reset();

        private int MaximumEndTick() => matches.Count == 0 ? 0 : matches.Max(match => match.Track.EndTick);

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
                foreach (var character in value ?? string.Empty) { hash ^= character; hash *= 16777619; }
                return hash.ToString("X8", CultureInfo.InvariantCulture);
            }
        }
    }
}
