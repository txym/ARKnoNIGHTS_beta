using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;

namespace ArknoNights.Battle.Demo
{
    public enum BattleDemoState { Idle, LoadingComputing, Ready, Playing, Paused, Completed, Error }

    /// <summary>
    /// Owns the fixed-demo lifecycle. It computes the Core result before constructing playback and never
    /// retains a BattleRunner after that computation. Unity scene components only provide the view factory.
    /// </summary>
    public sealed class BattleDemoCoordinator : IDisposable
    {
        private BattleEventPlaybackController playback;
        private BattleInput input;
        private BattleRunResult result;
        private UnitCatalog catalog;
        private BattleObserverView observer = BattleObserverView.Home;
        private float speed = 1f;

        public BattleDemoState State { get; private set; } = BattleDemoState.Idle;
        public string LastError { get; private set; } = string.Empty;
        public BattleInput Input => input;
        public BattleRunResult Result => result;
        public UnitCatalog Catalog => catalog;
        public BattleObserverView Observer => observer;
        public float Speed => speed;
        public float PresentationTick => playback == null ? 0f : playback.DisplayTicks;
        public int ConsumedEventCount => playback == null ? 0 : playback.ConsumedEventCount;
        public int EventCount => result == null ? 0 : result.Events.Count;
        /// <summary>Presentation-consumed unit state for read-only HUD projections. It never exposes the runner.</summary>
        public IReadOnlyList<BattlePresentationViewState> PresentationViewStates => playback == null ? Array.Empty<BattlePresentationViewState>() : playback.ViewStates;
        public string InputDigest => input == null ? string.Empty : Fingerprint(input.CanonicalSummary);
        public string EventDigest => result == null ? string.Empty : Fingerprint(BuildEventSummary(result.Events));
        public string ResultDigest => result == null ? string.Empty : Fingerprint(result.StableSummary + "|" + BuildEventSummary(result.Events));
        public string WinnerOrReason => result == null
            ? string.Empty
            : result.Outcome == BattleOutcome.Draw
                ? BattleOutcome.Draw.ToString()
                : result.Winner.Value.ToString();
        public string PlayersSummary => input == null
            ? string.Empty
            : string.Join("; ", input.Players.Select(player => player.PlayerId + "/" + player.Side + "=[" + string.Join(",", player.Units.Select(unit => unit.UnitId + ":" + unit.TypeId)) + "]"));

        public bool StartOrContinue(IBattlePresentationViewFactory factory, string catalogResourcePath, string battleResourcePath)
        {
            switch (State)
            {
                case BattleDemoState.Paused:
                    playback.Resume();
                    State = BattleDemoState.Playing;
                    return true;
                case BattleDemoState.Ready:
                    playback.Play();
                    State = BattleDemoState.Playing;
                    return true;
                case BattleDemoState.Completed:
                    return Replay();
                case BattleDemoState.Playing:
                case BattleDemoState.LoadingComputing:
                    return false;
                default:
                    return Recalculate(factory, catalogResourcePath, battleResourcePath, true);
            }
        }

        public bool Recalculate(IBattlePresentationViewFactory factory, string catalogResourcePath, string battleResourcePath, bool playAfterPrepare)
        {
            ClearPlayback();
            input = null;
            result = null;
            catalog = null;
            LastError = string.Empty;
            State = BattleDemoState.LoadingComputing;

            if (factory == null) return Fail("presentation.factory.missing", "A data-driven presentation factory is required.");

            try
            {
                var loaded = LocalBattleLoader.LoadFromResources(catalogResourcePath, battleResourcePath);
                if (!loaded.Success) return Fail("load.failed", Join(loaded.Errors));
                return Prepare(factory, loaded.Input, loaded.Catalog, playAfterPrepare);
            }
            catch (Exception exception)
            {
                return Fail("demo.unhandled", exception.GetType().Name + ": " + exception.Message);
            }
        }

        /// <summary>Runtime round entry. The caller owns building a validated immutable input from PlayerState.</summary>
        public bool StartRuntimeBattle(IBattlePresentationViewFactory factory, BattleInput runtimeInput, UnitCatalog runtimeCatalog, bool playAfterPrepare)
        {
            ClearPlayback();
            input = null;
            result = null;
            catalog = null;
            LastError = string.Empty;
            State = BattleDemoState.LoadingComputing;
            return Prepare(factory, runtimeInput, runtimeCatalog, playAfterPrepare);
        }

        public bool Pause()
        {
            if (State != BattleDemoState.Playing || playback == null) return false;
            playback.Pause();
            State = BattleDemoState.Paused;
            return true;
        }

        public bool Replay()
        {
            if (playback == null || result == null) return false;
            if (!playback.Replay(out var diagnostics)) return Fail("presentation.replay.failed", Join(diagnostics));
            playback.SetObserver(observer);
            playback.SetPlaybackSpeed(speed);
            playback.Play();
            State = BattleDemoState.Playing;
            return true;
        }

        public bool SetSpeed(float value)
        {
            if (value <= 0f) return false;
            speed = value;
            if (playback != null) playback.SetPlaybackSpeed(value);
            return true;
        }

        public void SetObserver(BattleObserverView value)
        {
            observer = value;
            if (playback != null) playback.SetObserver(value);
        }

        public void Advance(float unscaledDeltaSeconds)
        {
            if (State != BattleDemoState.Playing || playback == null) return;
            playback.Advance(unscaledDeltaSeconds);
            if (playback.Diagnostics.Count != 0)
            {
                Fail("presentation.advance.failed", Join(playback.Diagnostics));
                return;
            }
            if (playback.IsCompleted) State = BattleDemoState.Completed;
        }

        public void Dispose()
        {
            ClearPlayback();
            State = BattleDemoState.Idle;
        }

        /// <summary>Disposes current presentation views without mutating the completed Core input or result.</summary>
        public void Reset()
        {
            ClearPlayback();
            input = null;
            result = null;
            catalog = null;
            LastError = string.Empty;
            State = BattleDemoState.Idle;
        }

        private bool Prepare(IBattlePresentationViewFactory factory, BattleInput preparedInput, UnitCatalog preparedCatalog, bool playAfterPrepare)
        {
            if (factory == null) return Fail("presentation.factory.missing", "A data-driven presentation factory is required.");
            if (preparedInput == null || preparedCatalog == null) return Fail("runtime.input.missing", "A validated battle input and unit catalog are required.");
            try
            {
                input = preparedInput;
                catalog = preparedCatalog;
                // The runner is deliberately a local value: presentation receives only the completed result.
                result = new BattleRunner(input).RunToCompletion();
                playback = new BattleEventPlaybackController();
                var eliteLevels = input.Players.SelectMany(player => player.Units).ToDictionary(unit => unit.UnitId, unit => unit.EliteLevel, StringComparer.Ordinal);
                if (!playback.Load(result, factory, eliteLevels, out var diagnostics)) return Fail("presentation.load.failed", Join(diagnostics));
                playback.SetObserver(observer);
                playback.SetPlaybackSpeed(speed);
                State = BattleDemoState.Ready;
                if (playAfterPrepare)
                {
                    playback.Play();
                    State = BattleDemoState.Playing;
                }
                return true;
            }
            catch (Exception exception)
            {
                return Fail("demo.unhandled", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private bool Fail(string stage, string detail)
        {
            LastError = stage + ": " + (detail ?? string.Empty);
            ClearPlayback();
            State = BattleDemoState.Error;
            return false;
        }

        private void ClearPlayback()
        {
            if (playback == null) return;
            playback.Dispose();
            playback = null;
        }

        private static string Join<T>(IEnumerable<T> values) => string.Join(" | ", (values ?? Enumerable.Empty<T>()).Select(value => value == null ? "<null>" : value.ToString()));

        private static string BuildEventSummary(IEnumerable<BattleEvent> events)
        {
            var builder = new StringBuilder();
            foreach (var item in events ?? Enumerable.Empty<BattleEvent>())
            {
                builder.Append((int)item.Type).Append(',').Append(item.Tick).Append(',').Append(item.Sequence).Append(',').Append(item.UnitId).Append(',').Append(item.UnitTypeId).Append(',').Append(item.RelatedUnitId).Append(',').Append(item.AnimationKey);
                if (item.SpawnSnapshot != null)
                    builder.Append(",spawn=").Append(item.SpawnSnapshot.UnitId).Append(',').Append(item.SpawnSnapshot.TypeId).Append(',').Append(item.SpawnSnapshot.PlayerId).Append(',').Append(item.SpawnSnapshot.Side).Append(',').Append(item.SpawnSnapshot.MaxHitPoints).Append(',').Append(item.SpawnSnapshot.CurrentHitPoints).Append(',').Append(item.SpawnSnapshot.CurrentShield);
                builder.Append(';');
            }
            return builder.ToString();
        }

        private static string Fingerprint(string value)
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
