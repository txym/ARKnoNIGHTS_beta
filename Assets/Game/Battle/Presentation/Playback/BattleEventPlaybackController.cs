using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
using UnityEngine;

namespace ArknoNights.Battle.Presentation
{
    /// <summary>
    /// Read-only event player for a completed BattleRunResult. Rendering time controls only presentation;
    /// it never feeds positions, damage, death, or winner back into Core.
    /// </summary>
    public sealed class BattleEventPlaybackController : IDisposable
    {
        private readonly BattlefieldWorldProjection projection;
        private readonly BattleTrackPlaybackController trackPlayback;
        private readonly Dictionary<string, ViewRecord> views = new Dictionary<string, ViewRecord>(StringComparer.Ordinal);
        private readonly List<BattlePresentationDiagnostic> diagnostics = new List<BattlePresentationDiagnostic>();
        private BattleRunResult result;
        private IBattlePresentationViewFactory factory;
        private IReadOnlyList<BattleEvent> events = Array.Empty<BattleEvent>();
        private int nextEventIndex;
        private float displayTicks;
        private bool hasValidatedCompletion;
        private IReadOnlyDictionary<string, int> eliteLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        private BattlePresentationTrack compiledTrack;

        public BattleEventPlaybackController(BattlefieldWorldProjection projection = null)
        {
            this.projection = projection ?? BattlefieldWorldProjection.Default;
            trackPlayback = new BattleTrackPlaybackController(this.projection);
        }

        public BattleObserverView Observer { get; private set; } = BattleObserverView.Home;
        public float PlaybackSpeed { get; private set; } = 1f;
        public bool IsLoaded => result != null;
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsCompleted { get; private set; }
        public int ConsumedEventCount => events.Count(item => item.Tick <= Mathf.FloorToInt(displayTicks));
        public float DisplayTicks => displayTicks;
        public IReadOnlyList<BattlePresentationDiagnostic> Diagnostics => new ReadOnlyCollection<BattlePresentationDiagnostic>(diagnostics);
        public IReadOnlyList<BattlePresentationViewState> ViewStates => compiledTrack == null ? new ReadOnlyCollection<BattlePresentationViewState>(views.Values.OrderBy(item => item.UnitId, StringComparer.Ordinal).Select(item => item.ToState()).ToArray()) : new ReadOnlyCollection<BattlePresentationViewState>(trackPlayback.ViewStates.Select(WithEliteOverride).ToArray());

        public bool Load(BattleRunResult source, IBattlePresentationViewFactory viewFactory, out IReadOnlyList<BattlePresentationDiagnostic> loadDiagnostics)
        {
            return Load(source, viewFactory, null, out loadDiagnostics);
        }

        public bool Load(BattleRunResult source, IBattlePresentationViewFactory viewFactory, IReadOnlyDictionary<string, int> sourceEliteLevels, out IReadOnlyList<BattlePresentationDiagnostic> loadDiagnostics)
        {
            StopAndClear();
            result = source;
            factory = viewFactory;
            eliteLevels = sourceEliteLevels ?? new Dictionary<string, int>(StringComparer.Ordinal);
            if (source == null) AddDiagnostic("result.missing", "A completed BattleRunResult is required.");
            if (viewFactory == null) AddDiagnostic("viewFactory.missing", "A presentation view factory is required.");
            if (diagnostics.Count == 0)
            {
                events = source.Events;
                var compiler = new BattlePresentationTrackCompiler();
                if (!compiler.TryCompile(source, out compiledTrack, out var compileDiagnostics)) diagnostics.AddRange(compileDiagnostics);
                else if (!trackPlayback.Bind(compiledTrack, viewFactory, Observer, 0d, out var bindDiagnostics)) diagnostics.AddRange(bindDiagnostics);
            }

            if (diagnostics.Count != 0)
            {
                DisposeViews();
                events = Array.Empty<BattleEvent>();
                result = null;
                factory = null;
                loadDiagnostics = Diagnostics;
                return false;
            }

            loadDiagnostics = Diagnostics;
            return true;
        }

        public void Play()
        {
            if (!IsLoaded || IsCompleted) return;
            IsPlaying = true;
            IsPaused = false;
            ApplyViewPlaybackSpeed();
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            IsPaused = true;
            ApplyViewPlaybackSpeed();
        }

        public void Resume()
        {
            if (!IsLoaded || IsCompleted) return;
            IsPlaying = true;
            IsPaused = false;
            ApplyViewPlaybackSpeed();
        }

        public void SetPlaybackSpeed(float speed)
        {
            if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed), "Playback speed must be positive.");
            PlaybackSpeed = speed;
            ApplyViewPlaybackSpeed();
        }

        public void SetObserver(BattleObserverView observer)
        {
            Observer = observer;
            if (compiledTrack != null && !trackPlayback.SetObserver(observer, displayTicks, out var trackDiagnostics)) diagnostics.AddRange(trackDiagnostics);
            ApplyStatusBarObserver();
            ApplyInterpolatedPositions();
        }

        public void Advance(float unscaledDeltaSeconds)
        {
            if (!IsPlaying || IsPaused || IsCompleted || unscaledDeltaSeconds <= 0f) return;
            var previousTicks = displayTicks;
            displayTicks += unscaledDeltaSeconds * PlaybackSpeed * BattleInput.TicksPerSecond;
            if (compiledTrack != null)
            {
                foreach (var action in events.Where(item => (item.Type == BattleEventType.Spawn || item.Type == BattleEventType.Move || item.Type == BattleEventType.Attack || item.Type == BattleEventType.Skill) && item.Tick > previousTicks && item.Tick <= displayTicks))
                    if (!trackPlayback.RenderAt(action.Tick, out var actionDiagnostics)) diagnostics.AddRange(actionDiagnostics);
                if (!trackPlayback.RenderAt(displayTicks, out var trackDiagnostics)) diagnostics.AddRange(trackDiagnostics);
            }
            if (compiledTrack != null && displayTicks >= compiledTrack.EndTick)
            {
                IsPlaying = false;
                IsCompleted = true;
            }
        }

        public bool Replay(out IReadOnlyList<BattlePresentationDiagnostic> replayDiagnostics)
        {
            if (result == null || factory == null) { replayDiagnostics = Diagnostics; return false; }
            if (compiledTrack != null)
            {
                displayTicks = 0f;
                IsCompleted = false;
                IsPlaying = false;
                IsPaused = false;
                var rebound = trackPlayback.Bind(compiledTrack, factory, Observer, 0d, out replayDiagnostics);
                return rebound;
            }
            var retainedResult = result;
            var retainedFactory = factory;
            return Load(retainedResult, retainedFactory, out replayDiagnostics);
        }

        public void StopAndClear()
        {
            IsPlaying = false;
            IsPaused = false;
            IsCompleted = false;
            nextEventIndex = 0;
            displayTicks = 0f;
            hasValidatedCompletion = false;
            DisposeViews();
            diagnostics.Clear();
            events = Array.Empty<BattleEvent>();
            result = null;
            factory = null;
            compiledTrack = null;
            trackPlayback.Clear();
        }

        public void Dispose() => StopAndClear();

        private void ValidateEventOrder()
        {
            var previousTick = -1;
            var expectedSequence = 1;
            for (var index = 0; index < events.Count; index++)
            {
                var item = events[index];
                if (item == null) { AddDiagnostic("event.missing", "The event sequence contains a null item."); return; }
                if (item.Tick < previousTick || item.Tick < 0) { AddDiagnostic("event.order.invalid", "Events must be ordered by non-negative tick.", item.Tick, item.Sequence); return; }
                if (item.Tick != previousTick) { previousTick = item.Tick; expectedSequence = 1; }
                if (item.Sequence != expectedSequence) { AddDiagnostic("event.sequence.invalid", "Events must have contiguous sequence values within each tick.", item.Tick, item.Sequence); return; }
                expectedSequence++;
            }
        }

        private void ConsumeEventsThrough(int tick)
        {
            while (nextEventIndex < events.Count && events[nextEventIndex].Tick <= tick)
            {
                if (!Consume(events[nextEventIndex])) { IsPlaying = false; return; }
                nextEventIndex++;
            }
        }

        private bool Consume(BattleEvent item)
        {
            switch (item.Type)
            {
                case BattleEventType.Spawn:
                    if (string.IsNullOrWhiteSpace(item.UnitId) || string.IsNullOrWhiteSpace(item.UnitTypeId) || !item.UnitSide.HasValue || !item.ToPosition.HasValue)
                    {
                        AddDiagnostic("spawn.contract.invalid", "Spawn requires unit ID, type ID, side, and initial position.", item.Tick, item.Sequence);
                        return false;
                    }
                    if (views.ContainsKey(item.UnitId)) { AddDiagnostic("spawn.duplicate", "Duplicate Spawn for unit " + item.UnitId + ".", item.Tick, item.Sequence); return false; }
                    if (!factory.TryCreate(item.UnitId, item.UnitTypeId, out var created, out var createDiagnostic) || created == null)
                    {
                        diagnostics.Add(createDiagnostic ?? new BattlePresentationDiagnostic("view.create.failed", "No view was created for type " + item.UnitTypeId + ".", item.Tick, item.Sequence));
                        return false;
                    }
                    eliteLevels.TryGetValue(item.UnitId, out var eliteLevel);
                    var record = new ViewRecord(item.UnitId, item.UnitTypeId, item.UnitSide.Value, item.ToPosition.Value, item.HitPointsAfter, eliteLevel, created);
                    views.Add(item.UnitId, record);
                    foreach (var move in events.Where(candidate => candidate.Type == BattleEventType.Move && string.Equals(candidate.UnitId, item.UnitId, StringComparison.Ordinal) && candidate.FromPosition.HasValue && candidate.ToPosition.HasValue))
                        record.AddMove(move.Tick, move.FromPosition.Value, move.ToPosition.Value);
                    created.SetPlaybackSpeed(PlaybackSpeed);
                    created.SetStatusBarState(item.UnitId, record.Side != ToBattleSide(Observer), item.HitPointsAfter, 0);
                    created.SetWorldPosition(projection.ToWorld(record.InitialPosition, Observer));
                    break;

                case BattleEventType.Move:
                    if (!TryGetView(item.UnitId, item, out var moving)) return false;
                    if (moving.CanStartMoveAnimation(item.Tick))
                    {
                        moving.View.PlayMove();
                    }
                    moving.MarkMoveEventProcessed(item.Tick);
                    break;

                case BattleEventType.Attack:
                    if (!TryGetView(item.UnitId, item, out var attacker)) return false;
                    if (string.IsNullOrEmpty(item.RelatedUnitId))
                    {
                        AddDiagnostic("attack.contract.invalid", "Attack requires a target unit.", item.Tick, item.Sequence);
                        return false;
                    }
                    if (!TryGetView(item.RelatedUnitId, item, out var attackTarget)) return false;
                    var animationMultiplier = item.EffectiveAnimationTicks > 0 ? (float)item.OriginalAnimationTicks / item.EffectiveAnimationTicks : 1f;
                    attacker.View.PlayAttack(animationMultiplier);
                    attacker.MarkAttackAnimationStarted(item.Tick, item.EffectiveAnimationTicks);
                    attacker.MarkAttackFacing(item.Tick, attacker.PositionAt(item.Tick), attackTarget.PositionAt(item.Tick));
                    break;

                case BattleEventType.Skill:
                    if (!TryGetView(item.UnitId, item, out var caster)) return false;
                    if (string.IsNullOrWhiteSpace(item.AnimationKey)
                        || item.OriginalAnimationTicks <= 0
                        || item.EffectiveAnimationTicks != (item.OriginalAnimationTicks + 1) / 2)
                    {
                        AddDiagnostic("skill.contract.invalid", "Skill requires a key and a rounded 2x duration.", item.Tick, item.Sequence);
                        return false;
                    }
                    if (caster.View is IBattleSkillPresentationView skillView)
                        skillView.PlaySkill(item.AnimationKey, 2f);
                    caster.MarkAttackAnimationStarted(
                        item.Tick,
                        item.EffectiveAnimationTicks);
                    if (!string.IsNullOrEmpty(item.RelatedUnitId))
                    {
                        if (!TryGetView(
                                item.RelatedUnitId,
                                item,
                                out var skillTarget))
                            return false;
                        caster.MarkAttackFacing(
                            item.Tick,
                            caster.PositionAt(item.Tick),
                            skillTarget.PositionAt(item.Tick));
                    }
                    break;

                case BattleEventType.Damage:
                    if (string.IsNullOrEmpty(item.RelatedUnitId) || !TryGetView(item.RelatedUnitId, item, out var damaged)) return false;
                    damaged.HitPoints = item.HitPointsAfter;
                    damaged.View.SetStatusBarState(damaged.UnitId, damaged.Side != ToBattleSide(Observer), damaged.HitPoints, 0);
                    damaged.View.PlayHit();
                    damaged.MarkNonMoveAnimationStarted();
                    break;

                case BattleEventType.HealthChanged:
                    if (!TryGetView(item.UnitId, item, out var changed))
                        return false;
                    changed.HitPoints = item.HitPointsAfter;
                    changed.View.SetStatusBarState(
                        changed.UnitId,
                        changed.Side != ToBattleSide(Observer),
                        changed.HitPoints,
                        0);
                    break;

                case BattleEventType.Death:
                    if (!TryGetView(item.UnitId, item, out var dead)) return false;
                    dead.IsAlive = false;
                    dead.HitPoints = 0;
                    dead.View.SetStatusBarState(dead.UnitId, dead.Side != ToBattleSide(Observer), 0, 0);
                    dead.View.PlayDeath();
                    dead.MarkNonMoveAnimationStarted();
                    break;
            }
            return true;
        }

        private bool TryGetView(string unitId, BattleEvent item, out ViewRecord record)
        {
            if (!string.IsNullOrEmpty(unitId) && views.TryGetValue(unitId, out record)) return true;
            AddDiagnostic("unit.unknown", "Event references an unknown unit " + (unitId ?? "<null>") + ".", item.Tick, item.Sequence);
            record = null;
            return false;
        }

        private void ApplyInterpolatedPositions()
        {
            foreach (var record in views.Values)
            {
                var position = record.PositionAt(displayTicks);
                record.View.SetWorldPosition(projection.ToWorld(position, Observer));
                var hasMoveDirection = record.TryGetMovementDirection(displayTicks, out var moveFrom, out var moveTo, out var moveTick);
                var hasAttackDirection = record.TryGetAttackFacing(displayTicks, out var attackFrom, out var attackTo, out var attackTick);
                if (hasAttackDirection && (!hasMoveDirection || attackTick >= moveTick))
                {
                    var direction = projection.ToWorldDirection(attackFrom, attackTo, Observer);
                    if (direction != Vector3.zero) record.View.SetFacing(direction);
                }
                else if (hasMoveDirection)
                {
                    var direction = projection.ToWorldDirection(moveFrom, moveTo, Observer);
                    if (direction != Vector3.zero) record.View.SetFacing(direction);
                }
            }
        }

        private void ApplyStatusBarObserver()
        {
            var observerSide = ToBattleSide(Observer);
            foreach (var record in views.Values)
                record.View.SetStatusBarState(record.UnitId, record.Side != observerSide, record.HitPoints, 0);
        }

        private static BattleSide ToBattleSide(BattleObserverView observer) => observer == BattleObserverView.Away ? BattleSide.Away : BattleSide.Home;

        private BattlePresentationViewState WithEliteOverride(BattlePresentationViewState source)
        {
            var elite = eliteLevels.TryGetValue(source.UnitId, out var value) ? value : source.EliteLevel;
            return new BattlePresentationViewState(source.UnitId, source.TypeId, source.Side, source.Position, source.ContinuousPosition, source.MaxHitPoints, source.HitPoints, source.CurrentShield, source.HasSpawned, source.IsAlive, source.Action, elite);
        }

        private void ValidateCompletion()
        {
            if (hasValidatedCompletion) return;
            hasValidatedCompletion = true;
            if (views.Count != result.FinalUnits.Count) { AddDiagnostic("result.finalState.mismatch", "Event-derived view count does not match BattleRunResult final units."); return; }
            foreach (var finalUnit in result.FinalUnits)
            {
                if (!views.TryGetValue(finalUnit.UnitId, out var view) || view.TypeId != finalUnit.TypeId || view.Side != finalUnit.Side || !view.PositionAt(float.MaxValue).Equals(finalUnit.Position) || view.HitPoints != finalUnit.HitPoints || view.IsAlive != finalUnit.IsAlive)
                {
                    AddDiagnostic("result.finalState.mismatch", "Event-derived state does not match BattleRunResult for unit " + finalUnit.UnitId + ".");
                    return;
                }
            }

            if (!result.Winner.HasValue) return;
            var winner = result.Winner.Value;
            var winnerAlive = views.Values.Any(item => item.IsAlive && item.Side == winner);
            var losingAlive = views.Values.Any(item => item.IsAlive && item.Side != winner);
            if (!winnerAlive || losingAlive) AddDiagnostic("result.winner.mismatch", "Event-derived alive states do not match BattleRunResult winner.");
        }

        private void AddDiagnostic(string code, string message, int tick = -1, int sequence = -1) => diagnostics.Add(new BattlePresentationDiagnostic(code, message, tick, sequence));
        private void DisposeViews() { foreach (var record in views.Values) record.View.Dispose(); views.Clear(); }
        private void ApplyViewPlaybackSpeed()
        {
            var viewSpeed = IsPaused ? 0f : PlaybackSpeed;
            if (compiledTrack != null) trackPlayback.SetPlaybackSpeed(viewSpeed);
            foreach (var record in views.Values) record.View.SetPlaybackSpeed(viewSpeed);
        }

        private sealed class ViewRecord
        {
            private readonly List<MoveSegment> moves = new List<MoveSegment>();
            public ViewRecord(string unitId, string typeId, BattleSide side, FixedPosition initialPosition, int hitPoints, int eliteLevel, IBattlePresentationView view) { UnitId = unitId; TypeId = typeId; Side = side; InitialPosition = initialPosition; HitPoints = hitPoints; EliteLevel = eliteLevel; View = view; }
            public string UnitId { get; }
            public string TypeId { get; }
            public BattleSide Side { get; }
            public FixedPosition InitialPosition { get; }
            public IBattlePresentationView View { get; }
            public int EliteLevel { get; }
            public bool IsAlive { get; set; } = true;
            public int HitPoints { get; set; }
            private int lastMoveEventTick = int.MinValue;
            private int attackAnimationEndsAtTick = int.MinValue;
            private int lastAttackFacingTick = int.MinValue;
            private FixedPosition lastAttackFacingFrom;
            private FixedPosition lastAttackFacingTo;
            public void AddMove(int tick, FixedPosition from, FixedPosition to) { moves.Add(new MoveSegment(tick, from, to)); }
            public bool CanStartMoveAnimation(int tick) => tick > attackAnimationEndsAtTick && tick != lastMoveEventTick + 1;
            public void MarkMoveEventProcessed(int tick)
            {
                if (tick > attackAnimationEndsAtTick) lastMoveEventTick = tick;
            }
            public void MarkAttackAnimationStarted(int tick, int effectiveAnimationTicks)
            {
                attackAnimationEndsAtTick = Math.Max(attackAnimationEndsAtTick, tick + Math.Max(0, effectiveAnimationTicks));
                lastMoveEventTick = int.MinValue;
            }
            public void MarkAttackFacing(int tick, FixedPosition from, FixedPosition to)
            {
                lastAttackFacingTick = tick;
                lastAttackFacingFrom = from;
                lastAttackFacingTo = to;
            }
            public void MarkNonMoveAnimationStarted() => lastMoveEventTick = int.MinValue;
            public FixedPosition PositionAt(float currentTicks)
            {
                var position = InitialPosition;
                foreach (var move in moves)
                {
                    if (currentTicks < move.Tick - 1) break;
                    if (currentTicks < move.Tick)
                    {
                        var t = Mathf.Clamp01(currentTicks - (move.Tick - 1));
                        return new FixedPosition(Mathf.RoundToInt(Mathf.Lerp(move.From.XUnits, move.To.XUnits, t)), Mathf.RoundToInt(Mathf.Lerp(move.From.YUnits, move.To.YUnits, t)));
                    }
                    position = move.To;
                }
                return position;
            }
            public bool TryGetMovementDirection(float currentTicks, out FixedPosition from, out FixedPosition to, out int tick)
            {
                from = default;
                to = default;
                tick = int.MinValue;
                var hasDirection = false;
                foreach (var move in moves)
                {
                    if (currentTicks < move.Tick - 1) break;
                    from = move.From;
                    to = move.To;
                    tick = move.Tick;
                    hasDirection = true;
                    if (currentTicks < move.Tick) break;
                }
                return hasDirection;
            }
            public bool TryGetAttackFacing(float currentTicks, out FixedPosition from, out FixedPosition to, out int tick)
            {
                from = lastAttackFacingFrom;
                to = lastAttackFacingTo;
                tick = lastAttackFacingTick;
                return tick != int.MinValue && tick <= Mathf.FloorToInt(currentTicks);
            }
            public BattlePresentationViewState ToState() => new BattlePresentationViewState(UnitId, TypeId, Side, PositionAt(float.MaxValue), HitPoints, IsAlive, EliteLevel);
        }

        private readonly struct MoveSegment { public MoveSegment(int tick, FixedPosition from, FixedPosition to) { Tick = tick; From = from; To = to; } public int Tick { get; } public FixedPosition From { get; } public FixedPosition To { get; } }
    }
}
