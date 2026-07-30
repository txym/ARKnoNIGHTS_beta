using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;

namespace ArknoNights.Battle.Presentation
{
    /// <summary>
    /// Causal, local-only presentation timeline. It keeps raw authoritative move
    /// points until the terminal result can be compiled by the legacy full-track
    /// compiler.
    /// </summary>
    public sealed class BattlePresentationStreamBuffer :
        IBattlePresentationTimeline
    {
        private readonly List<BattleSimulationChunk> chunks =
            new List<BattleSimulationChunk>();
        private readonly List<BattleEvent> events =
            new List<BattleEvent>();
        private IReadOnlyList<IBattleUnitPresentationTimeline> units =
            new ReadOnlyCollection<IBattleUnitPresentationTimeline>(
                Array.Empty<IBattleUnitPresentationTimeline>());

        public BattlePresentationStreamBuffer(
            string battleId,
            string sealedInputHash)
        {
            if (string.IsNullOrWhiteSpace(battleId))
                throw new ArgumentException(
                    "Battle id is required.",
                    nameof(battleId));
            if (string.IsNullOrWhiteSpace(sealedInputHash))
                throw new ArgumentException(
                    "Sealed input hash is required.",
                    nameof(sealedInputHash));
            BattleId = battleId;
            SealedInputHash = sealedInputHash;
        }

        public string BattleId { get; }
        public string SealedInputHash { get; }
        public int EndTick => AvailableThroughTick;
        public int AvailableThroughTick { get; private set; }
        public bool HasFirstChunk => chunks.Count != 0;
        public bool IsTerminal { get; private set; }
        public IReadOnlyList<BattleSimulationChunk> Chunks =>
            new ReadOnlyCollection<BattleSimulationChunk>(
                chunks.ToArray());
        public IReadOnlyList<IBattleUnitPresentationTimeline>
            TimelineUnits => units;

        public bool TryAppendChunk(
            BattleSimulationChunk chunk,
            out string error)
        {
            error = string.Empty;
            if (chunk == null)
            {
                error = "stream.chunk.missing";
                return false;
            }
            if (!string.Equals(
                    chunk.BattleId,
                    BattleId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    chunk.SealedInputHash,
                    SealedInputHash,
                    StringComparison.Ordinal))
            {
                error = "stream.chunk.identityMismatch";
                return false;
            }
            if (IsTerminal)
            {
                error = "stream.chunk.afterTerminal";
                return false;
            }
            if (chunk.ChunkIndex != chunks.Count
                || chunk.PreviousCompletedTick
                    != AvailableThroughTick
                || chunk.CompletedTick
                    <= chunk.PreviousCompletedTick)
            {
                error = "stream.chunk.range.invalid";
                return false;
            }
            if (chunk.Events.Any(item =>
                    item.Tick < 0
                    || item.Tick > chunk.CompletedTick))
            {
                error = "stream.chunk.eventRange.invalid";
                return false;
            }

            chunks.Add(chunk);
            events.AddRange(chunk.Events);
            AvailableThroughTick = chunk.CompletedTick;
            IsTerminal = chunk.IsTerminal;
            units = BuildUnits();
            return true;
        }

        private IReadOnlyList<IBattleUnitPresentationTimeline>
            BuildUnits()
        {
            var builders = new Dictionary<string, UnitBuilder>(
                StringComparer.Ordinal);
            foreach (var item in events
                         .OrderBy(value => value.Tick)
                         .ThenBy(value => value.Sequence))
            {
                if (item.Type == BattleEventType.Spawn)
                {
                    if (item.SpawnSnapshot == null)
                        throw new InvalidOperationException(
                            "Spawn snapshot is required.");
                    var builder = new UnitBuilder
                    {
                        Snapshot = item.SpawnSnapshot,
                        SpawnTick = item.Tick
                    };
                    builder.Positions.Add(
                        new UnitPresentationTrack.PositionSegment(
                            item.Tick,
                            item.Tick,
                            item.SpawnSnapshot.Position,
                            item.SpawnSnapshot.Position));
                    builder.HitPoints.Add(
                        new UnitPresentationTrack.HpKey(
                            item.Tick,
                            item.Sequence,
                            item.SpawnSnapshot.CurrentHitPoints));
                    builders.Add(item.UnitId, builder);
                    continue;
                }

                if (item.Type == BattleEventType.BattleEnded)
                    continue;
                if (!builders.TryGetValue(
                        item.UnitId ?? string.Empty,
                        out var actor))
                    continue;
                switch (item.Type)
                {
                    case BattleEventType.Move:
                        actor.Positions.Add(
                            new UnitPresentationTrack.PositionSegment(
                                item.Tick - 1,
                                item.Tick,
                                item.FromPosition.Value,
                                item.ToPosition.Value));
                        break;
                    case BattleEventType.Attack:
                        actor.Attacks.Add(
                            new UnitPresentationTrack.Attack(
                                item.Tick,
                                item.Sequence,
                                item.EffectiveAnimationTicks,
                                item.EffectiveAnimationTicks > 0
                                    ? (float)item
                                        .OriginalAnimationTicks
                                        / item.EffectiveAnimationTicks
                                    : 1f));
                        break;
                    case BattleEventType.Skill:
                        actor.Skills.Add(
                            new UnitPresentationTrack.Skill(
                                item.Tick,
                                item.Sequence,
                                item.EffectiveAnimationTicks,
                                item.AnimationKey));
                        break;
                    case BattleEventType.PresentationStateChanged:
                        actor.States.Add(
                            new UnitPresentationTrack.State(
                                item.Tick,
                                item.Sequence,
                                item.AnimationKey));
                        break;
                    case BattleEventType.Damage:
                        if (builders.TryGetValue(
                                item.RelatedUnitId
                                    ?? string.Empty,
                                out var target))
                            target.HitPoints.Add(
                                new UnitPresentationTrack.HpKey(
                                    item.Tick,
                                    item.Sequence,
                                    item.HitPointsAfter));
                        break;
                    case BattleEventType.HealthChanged:
                        actor.HitPoints.Add(
                            new UnitPresentationTrack.HpKey(
                                item.Tick,
                                item.Sequence,
                                item.HitPointsAfter));
                        break;
                    case BattleEventType.Death:
                        actor.DeathTick = item.Tick;
                        break;
                    case BattleEventType.GateReached:
                        actor.ExitTick = item.Tick;
                        break;
                }
            }

            return new ReadOnlyCollection<
                IBattleUnitPresentationTimeline>(
                builders.Values
                    .OrderBy(
                        item => item.Snapshot.UnitId,
                        StringComparer.Ordinal)
                    .Select(item =>
                        (IBattleUnitPresentationTimeline)
                        new UnitPresentationTrack(
                            item.Snapshot,
                            item.SpawnTick,
                            AvailableThroughTick,
                            item.DeathTick,
                            item.ExitTick,
                            item.Positions,
                            item.HitPoints,
                            item.Attacks,
                            item.Skills,
                            item.States))
                    .ToArray());
        }

        private sealed class UnitBuilder
        {
            internal BattleUnitInstanceSnapshot Snapshot;
            internal int SpawnTick;
            internal int? DeathTick;
            internal int? ExitTick;
            internal readonly List<
                UnitPresentationTrack.PositionSegment> Positions =
                    new List<
                        UnitPresentationTrack.PositionSegment>();
            internal readonly List<
                UnitPresentationTrack.HpKey> HitPoints =
                    new List<UnitPresentationTrack.HpKey>();
            internal readonly List<
                UnitPresentationTrack.Attack> Attacks =
                    new List<UnitPresentationTrack.Attack>();
            internal readonly List<
                UnitPresentationTrack.Skill> Skills =
                    new List<UnitPresentationTrack.Skill>();
            internal readonly List<
                UnitPresentationTrack.State> States =
                    new List<UnitPresentationTrack.State>();
        }
    }
}
