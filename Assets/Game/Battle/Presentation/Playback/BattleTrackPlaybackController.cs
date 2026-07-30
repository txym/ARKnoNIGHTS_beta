using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;

namespace ArknoNights.Battle.Presentation
{
    /// <summary>Renders an already-computed presentation track without changing its authoritative result.</summary>
    public sealed class BattleTrackPlaybackController : IDisposable
    {
        private readonly BattlefieldWorldProjection projection;
        private readonly Dictionary<string, ViewRecord> views = new Dictionary<string, ViewRecord>(StringComparer.Ordinal);
        private readonly List<BattlePresentationDiagnostic> diagnostics = new List<BattlePresentationDiagnostic>();
        private IBattlePresentationViewFactory factory;
        private float playbackSpeed = 1f;

        public BattleTrackPlaybackController(BattlefieldWorldProjection projection = null)
        {
            this.projection = projection ?? BattlefieldWorldProjection.Default;
        }

        public BattlePresentationTrack Track { get; private set; }
        public IBattlePresentationTimeline Timeline { get; private set; }
        public BattleObserverView Observer { get; private set; } = BattleObserverView.Home;
        public double PresentationTick { get; private set; }
        public IReadOnlyList<BattlePresentationDiagnostic> Diagnostics => new ReadOnlyCollection<BattlePresentationDiagnostic>(diagnostics);
        public IReadOnlyList<BattlePresentationViewState> ViewStates => new ReadOnlyCollection<BattlePresentationViewState>(views.Values.OrderBy(item => item.Unit.UnitId, StringComparer.Ordinal).Select(item => item.ToState(PresentationTick)).ToArray());
        public bool HasPendingTerminalPresentation => views.Values.Any(item => item.View.HasPendingTerminalPresentation);

        public bool Bind(BattlePresentationTrack track, IBattlePresentationViewFactory viewFactory, BattleObserverView observer, double presentationTick, out IReadOnlyList<BattlePresentationDiagnostic> bindDiagnostics)
        {
            return BindTimeline(
                track,
                track,
                viewFactory,
                observer,
                presentationTick,
                out bindDiagnostics);
        }

        public bool Bind(
            BattlePresentationStreamBuffer stream,
            IBattlePresentationViewFactory viewFactory,
            BattleObserverView observer,
            double presentationTick,
            out IReadOnlyList<BattlePresentationDiagnostic>
                bindDiagnostics)
        {
            return BindTimeline(
                stream,
                null,
                viewFactory,
                observer,
                presentationTick,
                out bindDiagnostics);
        }

        private bool BindTimeline(
            IBattlePresentationTimeline timeline,
            BattlePresentationTrack track,
            IBattlePresentationViewFactory viewFactory,
            BattleObserverView observer,
            double presentationTick,
            out IReadOnlyList<BattlePresentationDiagnostic>
                bindDiagnostics)
        {
            Clear();
            Track = track;
            Timeline = timeline;
            factory = viewFactory;
            Observer = observer;
            if (timeline == null) AddDiagnostic("track.missing", "A presentation timeline is required.");
            if (viewFactory == null) AddDiagnostic("viewFactory.missing", "A presentation view factory is required.");
            if (diagnostics.Count == 0) RenderAt(presentationTick, out _);
            bindDiagnostics = Diagnostics;
            if (diagnostics.Count == 0) return true;
            DisposeViews();
            Track = null;
            Timeline = null;
            factory = null;
            return false;
        }

        public bool RenderAt(double presentationTick, out IReadOnlyList<BattlePresentationDiagnostic> renderDiagnostics)
        {
            if (double.IsNaN(presentationTick) || double.IsInfinity(presentationTick) || presentationTick < 0d)
            {
                AddDiagnostic("track.playback.tick.invalid", "Presentation tick must be finite and non-negative.");
                renderDiagnostics = Diagnostics;
                return false;
            }
            if (Timeline == null || factory == null)
            {
                AddDiagnostic("track.playback.unbound", "A track and factory must be bound before rendering.");
                renderDiagnostics = Diagnostics;
                return false;
            }
            if (presentationTick < PresentationTick) DisposeViews();
            PresentationTick = Math.Min(presentationTick, Timeline.EndTick);
            foreach (var unit in Timeline.TimelineUnits)
            {
                var sample = unit.Sample(PresentationTick);
                if (!sample.HasSpawned) continue;
                if (sample.HasExitedBattle)
                {
                    if (views.TryGetValue(unit.UnitId, out var exited))
                    {
                        exited.View.Dispose();
                        views.Remove(unit.UnitId);
                    }
                    continue;
                }
                if (!views.TryGetValue(unit.UnitId, out var record))
                {
                    if (!sample.ShouldDisplay) continue;
                    IBattlePresentationView view;
                    BattlePresentationDiagnostic diagnostic;
                    var created = factory
                        is IEliteBattlePresentationViewFactory eliteFactory
                            ? eliteFactory.TryCreate(
                                unit.UnitId,
                                unit.TypeId,
                                unit.EliteLevel,
                                out view,
                                out diagnostic)
                            : factory.TryCreate(
                                unit.UnitId,
                                unit.TypeId,
                                out view,
                                out diagnostic);
                    if (!created || view == null)
                    {
                        AddDiagnostic(diagnostic ?? new BattlePresentationDiagnostic("view.create.failed", "A view could not be created.", Timeline.BattleId, unit.UnitId));
                        renderDiagnostics = Diagnostics;
                        return false;
                    }
                    record = new ViewRecord(unit, view);
                    views.Add(unit.UnitId, record);
                    view.SetPlaybackSpeed(playbackSpeed);
                }
                else
                {
                    record.Unit = unit;
                }
                Apply(record, sample);
            }
            renderDiagnostics = Diagnostics;
            return diagnostics.Count == 0;
        }

        public bool SetObserver(BattleObserverView observer, double presentationTick, out IReadOnlyList<BattlePresentationDiagnostic> observerDiagnostics)
        {
            Observer = observer;
            foreach (var record in views.Values) record.LastFacing = int.MinValue;
            var success = RenderAt(presentationTick, out observerDiagnostics);
            return success;
        }

        public void SetPlaybackSpeed(float playbackSpeed)
        {
            this.playbackSpeed = playbackSpeed;
            foreach (var record in views.Values) record.View.SetPlaybackSpeed(playbackSpeed);
        }

        public void Clear()
        {
            DisposeViews();
            diagnostics.Clear();
            Track = null;
            Timeline = null;
            factory = null;
            PresentationTick = 0d;
        }

        public void Dispose() => Clear();

        private void Apply(ViewRecord record, UnitPresentationSample sample)
        {
            var position = new FixedPosition((int)Math.Round(sample.Position.XUnits * FixedPosition.UnitsPerMetre), (int)Math.Round(sample.Position.YUnits * FixedPosition.UnitsPerMetre));
            record.View.SetWorldPosition(projection.ToWorld(sample.Position, Observer));
            if (record.Unit.TryGetMovementDirection(PresentationTick, out var from, out var to))
            {
                record.View.SetFacing(projection.ToWorldDirection(from, to, Observer));
                record.LastFacing = sample.HorizontalFacing;
            }
            record.View.SetStatusBarState(record.Unit.UnitId, record.Unit.Side != (Observer == BattleObserverView.Home ? BattleSide.Home : BattleSide.Away), sample.MaxHitPoints, sample.CurrentHitPoints, sample.CurrentShield);
            var presentationStateChanged = !string.Equals(
                record.PresentationStateTag,
                sample.PresentationStateTag,
                StringComparison.Ordinal);
            if (presentationStateChanged)
            {
                record.View.SetPresentationState(
                    sample.PresentationStateTag);
                record.PresentationStateTag =
                    sample.PresentationStateTag;
            }
            if (presentationStateChanged || record.Action != sample.Action || record.ActionStartTick != sample.ActionStartTick || record.ActionSequence != sample.ActionSequence)
            {
                if (sample.Action == UnitPresentationAction.Idle) record.View.PlayIdle();
                else if (sample.Action == UnitPresentationAction.Move) record.View.PlayMove();
                else if (sample.Action == UnitPresentationAction.Attack) record.View.PlayAttack(sample.AttackAnimationSpeedMultiplier);
                else if (sample.Action == UnitPresentationAction.Skill
                    && record.View is IBattleSkillPresentationView skillView)
                    skillView.PlaySkill(
                        sample.AnimationKey,
                        sample.AnimationSpeedMultiplier);
                else if (sample.Action == UnitPresentationAction.Death) record.View.PlayDeath();
                record.Action = sample.Action;
                record.ActionStartTick = sample.ActionStartTick;
                record.ActionSequence = sample.ActionSequence;
            }
        }

        private void AddDiagnostic(string code, string message) => diagnostics.Add(new BattlePresentationDiagnostic(code, message));
        private void AddDiagnostic(BattlePresentationDiagnostic diagnostic) => diagnostics.Add(diagnostic);
        private void DisposeViews() { foreach (var record in views.Values) record.View.Dispose(); views.Clear(); }

        private sealed class ViewRecord
        {
            internal ViewRecord(IBattleUnitPresentationTimeline unit, IBattlePresentationView view) { Unit = unit; View = view; }
            internal IBattleUnitPresentationTimeline Unit { get; set; }
            internal IBattlePresentationView View { get; }
            internal int LastFacing { get; set; } = int.MinValue;
            internal UnitPresentationAction Action { get; set; } = (UnitPresentationAction)(-1);
            internal int ActionStartTick { get; set; } = int.MinValue;
            internal int ActionSequence { get; set; } = int.MinValue;
            internal string PresentationStateTag { get; set; }
                = null;
            internal BattlePresentationViewState ToState(double presentationTick)
            {
                var sample = Unit.Sample(presentationTick);
                var position = new FixedPosition((int)Math.Round(sample.Position.XUnits * FixedPosition.UnitsPerMetre), (int)Math.Round(sample.Position.YUnits * FixedPosition.UnitsPerMetre));
                return new BattlePresentationViewState(Unit.UnitId, Unit.TypeId, Unit.Side, position, sample.Position, sample.MaxHitPoints, sample.CurrentHitPoints, sample.CurrentShield, sample.HasSpawned, sample.IsAlive, sample.Action, Unit.EliteLevel, sample.Attributes);
            }
        }
    }
}
