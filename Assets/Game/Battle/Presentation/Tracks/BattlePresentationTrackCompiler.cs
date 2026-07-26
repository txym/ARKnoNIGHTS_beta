using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using ArknoNights.Battle.Core;

namespace ArknoNights.Battle.Presentation
{
    /// <summary>Builds read-only presentation data from an already-completed authoritative battle.</summary>
    public sealed class BattlePresentationTrackCompiler
    {
        private sealed class UnitBuilder
        {
            public BattleUnitInstanceSnapshot Snapshot;
            public int SpawnTick;
            public int SpawnSequence;
            public int? DeathTick;
            public readonly List<UnitPresentationTrack.PositionSegment> Positions = new List<UnitPresentationTrack.PositionSegment>();
            public readonly List<UnitPresentationTrack.HpKey> HitPoints = new List<UnitPresentationTrack.HpKey>();
            public readonly List<UnitPresentationTrack.Attack> Attacks = new List<UnitPresentationTrack.Attack>();
        }

        public bool TryCompile(BattleRunResult result, out BattlePresentationTrack track, out IReadOnlyList<BattlePresentationDiagnostic> diagnostics)
        {
            track = null;
            var errors = new List<BattlePresentationDiagnostic>();
            if (result == null)
            {
                AddError(errors, "track.result.missing", "Result is required.", string.Empty, string.Empty, -1, -1);
                diagnostics = ReadOnly(errors);
                return false;
            }

            if (string.IsNullOrWhiteSpace(result.BattleId) || string.IsNullOrWhiteSpace(result.HomePlayerId) || string.IsNullOrWhiteSpace(result.AwayPlayerId))
            {
                AddError(errors, "track.identity.invalid", "Battle identity is incomplete.", result.BattleId, string.Empty, -1, -1);
                diagnostics = ReadOnly(errors);
                return false;
            }

            var knownTypeIds = new HashSet<string>(result.KnownUnitTypeIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var builders = new Dictionary<string, UnitBuilder>(StringComparer.Ordinal);
            var events = (result.Events ?? Array.Empty<BattleEvent>()).ToArray();
            BattleEvent battleEnded = null;
            var lastTick = -1;
            var lastSequence = 0;

            foreach (var item in events)
            {
                if (!ValidateOrder(item, result.BattleId, ref lastTick, ref lastSequence, errors)) break;
                if (battleEnded != null)
                {
                    AddError(errors, "track.battleEnded.invalid", "Event exists after BattleEnded.", result.BattleId, item.UnitId, item.Tick, item.Sequence);
                    break;
                }

                if (item.Type == BattleEventType.Spawn)
                {
                    if (!TryAddSpawn(item, result.BattleId, knownTypeIds, builders, errors)) break;
                    continue;
                }

                if (item.Type == BattleEventType.BattleEnded)
                {
                    battleEnded = item;
                    continue;
                }

                if (!TryResolveUnit(item.UnitId, result.BattleId, item.Tick, item.Sequence, builders, out var actor, errors)) break;
                if (!string.IsNullOrEmpty(item.RelatedUnitId) && !TryResolveUnit(item.RelatedUnitId, result.BattleId, item.Tick, item.Sequence, builders, out _, errors)) break;

                switch (item.Type)
                {
                    case BattleEventType.Move:
                        if (!item.FromPosition.HasValue || !item.ToPosition.HasValue)
                        {
                            AddError(errors, "track.move.contract.invalid", "Move positions are required.", result.BattleId, item.UnitId, item.Tick, item.Sequence);
                            break;
                        }
                        if (!actor.Positions.Last().To.Equals(item.FromPosition.Value))
                        {
                            AddError(errors, "track.move.discontinuous", "Move start is discontinuous.", result.BattleId, item.UnitId, item.Tick, item.Sequence);
                            break;
                        }
                        actor.Positions.Add(new UnitPresentationTrack.PositionSegment(item.Tick - 1, item.Tick, item.FromPosition.Value, item.ToPosition.Value));
                        break;

                    case BattleEventType.Attack:
                        if (item.OriginalAnimationTicks <= 0 || item.EffectiveAnimationTicks <= 0)
                        {
                            AddError(errors, "track.attack.contract.invalid", "Attack duration is invalid.", result.BattleId, item.UnitId, item.Tick, item.Sequence);
                            break;
                        }
                        actor.Attacks.Add(new UnitPresentationTrack.Attack(item.Tick, item.Sequence, item.EffectiveAnimationTicks, (float)item.OriginalAnimationTicks / item.EffectiveAnimationTicks));
                        break;

                    case BattleEventType.Damage:
                        if (string.IsNullOrEmpty(item.RelatedUnitId) || !builders.TryGetValue(item.RelatedUnitId, out var target) || item.HitPointsAfter < 0 || item.HitPointsAfter > target.Snapshot.MaxHitPoints)
                        {
                            AddError(errors, "track.damage.contract.invalid", "Damage target HP is invalid.", result.BattleId, item.RelatedUnitId, item.Tick, item.Sequence);
                            break;
                        }
                        target.HitPoints.Add(new UnitPresentationTrack.HpKey(item.Tick, item.Sequence, item.HitPointsAfter));
                        break;

                    case BattleEventType.Death:
                        if (actor.DeathTick.HasValue)
                        {
                            AddError(errors, "track.death.duplicate", "Duplicate Death event.", result.BattleId, item.UnitId, item.Tick, item.Sequence);
                            break;
                        }
                        actor.DeathTick = item.Tick;
                        break;
                }

                if (errors.Count != 0) break;
            }

            if (errors.Count == 0 && (battleEnded == null || battleEnded.Tick != result.CompletedTicks || battleEnded.Winner != result.Winner || battleEnded.Reason != result.StopReason))
                AddError(errors, "track.battleEnded.invalid", "BattleEnded does not match the result.", result.BattleId, string.Empty, battleEnded == null ? -1 : battleEnded.Tick, battleEnded == null ? -1 : battleEnded.Sequence);

            if (errors.Count == 0) ValidateFinalStates(result, builders, battleEnded, errors);
            if (errors.Count != 0)
            {
                diagnostics = ReadOnly(errors);
                return false;
            }

            if (!TryCompressPositions(result, events, builders, errors, out var compressedPositions, out var metrics))
            {
                diagnostics = ReadOnly(errors);
                return false;
            }

            var units = builders.Values
                .OrderBy(item => item.SpawnTick)
                .ThenBy(item => item.SpawnSequence)
                .ThenBy(item => item.Snapshot.UnitId, StringComparer.Ordinal)
                .Select(item => new UnitPresentationTrack(item.Snapshot, item.SpawnTick, result.CompletedTicks, item.DeathTick, compressedPositions[item.Snapshot.UnitId], item.HitPoints, item.Attacks))
                .ToArray();
            track = new BattlePresentationTrack(result, units, CreateEventDigest(events), metrics);
            diagnostics = ReadOnly(errors);
            return true;
        }

        private static bool ValidateOrder(BattleEvent item, string battleId, ref int lastTick, ref int lastSequence, List<BattlePresentationDiagnostic> errors)
        {
            if (item == null)
            {
                AddError(errors, "track.event.order.invalid", "Event must not be null.", battleId, string.Empty, -1, -1);
                return false;
            }
            if (item.Tick < lastTick)
            {
                AddError(errors, "track.event.order.invalid", "Event ticks must be ordered.", battleId, item.UnitId, item.Tick, item.Sequence);
                return false;
            }
            if (item.Tick != lastTick)
            {
                if (item.Sequence != 1)
                {
                    AddError(errors, "track.event.sequence.invalid", "A tick sequence must start at one.", battleId, item.UnitId, item.Tick, item.Sequence);
                    return false;
                }
                lastTick = item.Tick;
                lastSequence = 0;
            }
            if (item.Sequence != lastSequence + 1)
            {
                AddError(errors, "track.event.sequence.invalid", "Tick sequences must be contiguous.", battleId, item.UnitId, item.Tick, item.Sequence);
                return false;
            }
            lastSequence = item.Sequence;
            return true;
        }

        private static bool TryAddSpawn(BattleEvent item, string battleId, ISet<string> knownTypeIds, IDictionary<string, UnitBuilder> builders, List<BattlePresentationDiagnostic> errors)
        {
            if (builders.ContainsKey(item.UnitId))
            {
                AddError(errors, "track.spawn.duplicate", "Duplicate Spawn event.", battleId, item.UnitId, item.Tick, item.Sequence);
                return false;
            }
            var snapshot = item.SpawnSnapshot;
            if (snapshot == null)
            {
                AddError(errors, "track.spawn.snapshot.missing", "Spawn snapshot is required.", battleId, item.UnitId, item.Tick, item.Sequence);
                return false;
            }
            if (item.UnitId != snapshot.UnitId || item.UnitTypeId != snapshot.TypeId || item.UnitSide != snapshot.Side || !item.ToPosition.HasValue || !item.ToPosition.Value.Equals(snapshot.Position))
            {
                AddError(errors, "track.spawn.snapshot.mismatch", "Spawn fields differ from its snapshot.", battleId, item.UnitId, item.Tick, item.Sequence);
                return false;
            }
            if (string.IsNullOrWhiteSpace(snapshot.UnitId) || string.IsNullOrWhiteSpace(snapshot.TypeId) || !Enum.IsDefined(typeof(BattleSide), snapshot.Side) || snapshot.MaxHitPoints <= 0 || snapshot.CurrentHitPoints < 0 || snapshot.CurrentHitPoints > snapshot.MaxHitPoints || snapshot.CurrentShield < 0)
            {
                AddError(errors, "track.spawn.contract.invalid", "Spawn snapshot is invalid.", battleId, item.UnitId, item.Tick, item.Sequence);
                return false;
            }
            if (!knownTypeIds.Contains(snapshot.TypeId))
            {
                AddError(errors, "track.unit.unknown", "Spawn Type ID is not known.", battleId, item.UnitId, item.Tick, item.Sequence);
                return false;
            }
            if (snapshot.IsDynamicallyGenerated ? !IsCanonicalNegative(snapshot.UnitId) : IsNegative(snapshot.UnitId))
            {
                AddError(errors, "track.spawn.dynamicId.invalid", "Unit ID violates dynamic identity rules.", battleId, item.UnitId, item.Tick, item.Sequence);
                return false;
            }

            var builder = new UnitBuilder { Snapshot = snapshot, SpawnTick = item.Tick, SpawnSequence = item.Sequence };
            builder.Positions.Add(new UnitPresentationTrack.PositionSegment(item.Tick, item.Tick, snapshot.Position, snapshot.Position));
            builder.HitPoints.Add(new UnitPresentationTrack.HpKey(item.Tick, item.Sequence, snapshot.CurrentHitPoints));
            builders.Add(snapshot.UnitId, builder);
            return true;
        }

        private static bool TryResolveUnit(string unitId, string battleId, int tick, int sequence, IReadOnlyDictionary<string, UnitBuilder> builders, out UnitBuilder unit, List<BattlePresentationDiagnostic> errors)
        {
            unit = null;
            if (string.IsNullOrEmpty(unitId) || !builders.TryGetValue(unitId, out unit))
            {
                AddError(errors, "track.unit.beforeSpawn", "Event references a unit before Spawn.", battleId, unitId, tick, sequence);
                return false;
            }
            return true;
        }

        private static void ValidateFinalStates(BattleRunResult result, IReadOnlyDictionary<string, UnitBuilder> builders, BattleEvent battleEnded, List<BattlePresentationDiagnostic> errors)
        {
            var finalUnits = result.FinalUnits ?? Array.Empty<BattleUnitFinalState>();
            if (finalUnits.Count != builders.Count)
            {
                AddError(errors, "track.finalState.mismatch", "Final unit count differs from spawned units.", result.BattleId, string.Empty, battleEnded.Tick, battleEnded.Sequence);
                return;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var final in finalUnits)
            {
                if (final == null || !seen.Add(final.UnitId) || !builders.TryGetValue(final.UnitId, out var builder) || builder.Snapshot.TypeId != final.TypeId || builder.Snapshot.Side != final.Side || !builder.Positions.Last().To.Equals(final.Position) || builder.HitPoints.Last().Value != final.HitPoints || (!builder.DeathTick.HasValue) != final.IsAlive)
                {
                    AddError(errors, "track.finalState.mismatch", "Track differs from a final unit state.", result.BattleId, final == null ? string.Empty : final.UnitId, battleEnded.Tick, battleEnded.Sequence);
                    return;
                }
            }
        }

        private static bool TryCompressPositions(
            BattleRunResult result,
            IReadOnlyList<BattleEvent> events,
            IReadOnlyDictionary<string, UnitBuilder> builders,
            ICollection<BattlePresentationDiagnostic> errors,
            out IReadOnlyDictionary<string, IReadOnlyList<UnitPresentationTrack.PositionSegment>> compressedPositions,
            out BattlePresentationCompressionMetrics metrics)
        {
            var output = new Dictionary<string, IReadOnlyList<UnitPresentationTrack.PositionSegment>>(StringComparer.Ordinal);
            var compressor = new PositionTrackCompressor();
            var maximumErrorUnits = 0d;
            var positionKeyCount = 0;
            var originalMoveCount = events.Count(item => item.Type == BattleEventType.Move);

            foreach (var item in builders.Values.OrderBy(value => value.Snapshot.UnitId, StringComparer.Ordinal))
            {
                if (!TryCompressUnitPositions(result, events, item, compressor, errors, out var positions, out var unitMaximumError))
                {
                    compressedPositions = new ReadOnlyDictionary<string, IReadOnlyList<UnitPresentationTrack.PositionSegment>>(output);
                    metrics = null;
                    return false;
                }

                output.Add(item.Snapshot.UnitId, positions);
                positionKeyCount += positions.SelectMany(segment => new[] { segment.Start, segment.End }).Distinct().Count();
                maximumErrorUnits = Math.Max(maximumErrorUnits, unitMaximumError);
            }

            compressedPositions = new ReadOnlyDictionary<string, IReadOnlyList<UnitPresentationTrack.PositionSegment>>(output);
            metrics = new BattlePresentationCompressionMetrics(originalMoveCount, positionKeyCount, maximumErrorUnits);
            return true;
        }

        private static bool TryCompressUnitPositions(
            BattleRunResult result,
            IReadOnlyList<BattleEvent> events,
            UnitBuilder builder,
            PositionTrackCompressor compressor,
            ICollection<BattlePresentationDiagnostic> errors,
            out IReadOnlyList<UnitPresentationTrack.PositionSegment> positions,
            out double maximumErrorUnits)
        {
            maximumErrorUnits = 0d;
            var rawMoves = builder.Positions.Where(segment => segment.End > segment.Start).ToArray();
            if (rawMoves.Length == 0)
            {
                positions = new ReadOnlyCollection<UnitPresentationTrack.PositionSegment>(builder.Positions.ToArray());
                return true;
            }

            var forcedTicks = CollectForcedTicks(events, builder.Snapshot.UnitId, builder.SpawnTick, result.CompletedTicks);
            var resultSegments = new List<UnitPresentationTrack.PositionSegment>();
            resultSegments.AddRange(builder.Positions.Where(segment => segment.End == segment.Start));
            var run = new List<RawPositionPoint>();
            UnitPresentationTrack.PositionSegment previous = default(UnitPresentationTrack.PositionSegment);
            var hasPrevious = false;

            foreach (var move in rawMoves)
            {
                if (!hasPrevious || move.Start != previous.End)
                {
                    if (run.Count != 0 && !TryCompressRun(run, forcedTicks, compressor, result.BattleId, builder.Snapshot.UnitId, errors, resultSegments, ref maximumErrorUnits))
                    {
                        positions = Array.Empty<UnitPresentationTrack.PositionSegment>();
                        return false;
                    }

                    run.Clear();
                    run.Add(new RawPositionPoint(move.Start, move.From));
                }

                run.Add(new RawPositionPoint(move.End, move.To));
                previous = move;
                hasPrevious = true;
            }

            if (!TryCompressRun(run, forcedTicks, compressor, result.BattleId, builder.Snapshot.UnitId, errors, resultSegments, ref maximumErrorUnits))
            {
                positions = Array.Empty<UnitPresentationTrack.PositionSegment>();
                return false;
            }

            if (!ValidateCompressedPositions(events, builder.Snapshot.UnitId, forcedTicks, resultSegments, result.BattleId, errors, ref maximumErrorUnits))
            {
                positions = Array.Empty<UnitPresentationTrack.PositionSegment>();
                return false;
            }

            positions = new ReadOnlyCollection<UnitPresentationTrack.PositionSegment>(resultSegments.OrderBy(segment => segment.Start).ThenBy(segment => segment.End).ToArray());
            return true;
        }

        private static bool TryCompressRun(
            IReadOnlyList<RawPositionPoint> run,
            ISet<int> forcedTicks,
            PositionTrackCompressor compressor,
            string battleId,
            string unitId,
            ICollection<BattlePresentationDiagnostic> errors,
            ICollection<UnitPresentationTrack.PositionSegment> output,
            ref double maximumErrorUnits)
        {
            if (!compressor.TryCompress(run, forcedTicks, out var compressed, out _, out var diagnostic))
            {
                errors.Add(diagnostic ?? new BattlePresentationDiagnostic("track.position.error.exceeded", "Position compression failed.", battleId, unitId));
                return false;
            }

            foreach (var segment in compressed) output.Add(segment);
            foreach (var point in run)
            {
                if (!TrySample(compressed, point.Tick, out var x, out var y))
                {
                    errors.Add(new BattlePresentationDiagnostic("track.position.error.exceeded", "A source movement tick was lost.", battleId, unitId, point.Tick));
                    return false;
                }

                var dx = x - point.Position.XUnits;
                var dy = y - point.Position.YUnits;
                maximumErrorUnits = Math.Max(maximumErrorUnits, Math.Sqrt(dx * dx + dy * dy));
                if (dx * dx + dy * dy > PositionTrackCompressor.MaximumErrorUnits * PositionTrackCompressor.MaximumErrorUnits + 0.0000001d)
                {
                    errors.Add(new BattlePresentationDiagnostic("track.position.error.exceeded", "Position compression exceeded one unit.", battleId, unitId, point.Tick));
                    return false;
                }

                if (forcedTicks.Contains(point.Tick) && (dx != 0d || dy != 0d))
                {
                    errors.Add(new BattlePresentationDiagnostic("track.position.forcedKey.inexact", "A forced position key was not exact.", battleId, unitId, point.Tick));
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateCompressedPositions(
            IReadOnlyList<BattleEvent> events,
            string unitId,
            ISet<int> forcedTicks,
            IReadOnlyList<UnitPresentationTrack.PositionSegment> segments,
            string battleId,
            ICollection<BattlePresentationDiagnostic> errors,
            ref double maximumErrorUnits)
        {
            foreach (var move in events.Where(item => item.Type == BattleEventType.Move && item.UnitId == unitId))
            {
                if (!TrySample(segments, move.Tick, out var x, out var y))
                {
                    errors.Add(new BattlePresentationDiagnostic("track.position.error.exceeded", "A source move cannot be sampled.", battleId, unitId, move.Tick, move.Sequence));
                    return false;
                }

                var dx = x - move.ToPosition.Value.XUnits;
                var dy = y - move.ToPosition.Value.YUnits;
                maximumErrorUnits = Math.Max(maximumErrorUnits, Math.Sqrt(dx * dx + dy * dy));
                if (dx * dx + dy * dy > PositionTrackCompressor.MaximumErrorUnits * PositionTrackCompressor.MaximumErrorUnits + 0.0000001d)
                {
                    errors.Add(new BattlePresentationDiagnostic("track.position.error.exceeded", "A source move exceeded the compression bound.", battleId, unitId, move.Tick, move.Sequence));
                    return false;
                }
            }

            foreach (var forcedTick in forcedTicks)
            {
                var source = events.LastOrDefault(item => item.Type == BattleEventType.Move && item.UnitId == unitId && item.Tick == forcedTick);
                if (source == null) continue;
                if (!TrySample(segments, forcedTick, out var x, out var y) || x != source.ToPosition.Value.XUnits || y != source.ToPosition.Value.YUnits)
                {
                    errors.Add(new BattlePresentationDiagnostic("track.position.forcedKey.inexact", "A forced movement tick was not exact.", battleId, unitId, forcedTick, source.Sequence));
                    return false;
                }
            }

            return true;
        }

        private static ISet<int> CollectForcedTicks(IReadOnlyList<BattleEvent> events, string unitId, int spawnTick, int endTick)
        {
            var ticks = new HashSet<int> { spawnTick, endTick };
            foreach (var item in events)
            {
                if (item.Type == BattleEventType.BattleEnded ||
                    (item.UnitId == unitId && (item.Type == BattleEventType.Spawn || item.Type == BattleEventType.Attack || item.Type == BattleEventType.BlockStarted || item.Type == BattleEventType.BlockEnded || item.Type == BattleEventType.Death)) ||
                    (item.RelatedUnitId == unitId && (item.Type == BattleEventType.BlockStarted || item.Type == BattleEventType.BlockEnded)))
                {
                    ticks.Add(item.Tick);
                }
            }

            return ticks;
        }

        private static bool TrySample(IReadOnlyList<UnitPresentationTrack.PositionSegment> segments, int tick, out double x, out double y)
        {
            var candidates = segments.Where(item => tick >= item.Start && tick <= item.End).OrderByDescending(item => item.Start).ToArray();
            if (candidates.Length == 0)
            {
                x = 0d;
                y = 0d;
                return false;
            }

            var segment = candidates[0];

            var duration = Math.Max(1, segment.End - segment.Start);
            var progress = (tick - segment.Start) / (double)duration;
            x = segment.From.XUnits + (segment.To.XUnits - segment.From.XUnits) * progress;
            y = segment.From.YUnits + (segment.To.YUnits - segment.From.YUnits) * progress;
            return true;
        }

        private static bool IsNegative(string unitId) => long.TryParse(unitId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value < 0;
        private static bool IsCanonicalNegative(string unitId) => long.TryParse(unitId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value < 0 && unitId == value.ToString(CultureInfo.InvariantCulture);
        private static IReadOnlyList<BattlePresentationDiagnostic> ReadOnly(IEnumerable<BattlePresentationDiagnostic> errors) => new ReadOnlyCollection<BattlePresentationDiagnostic>(errors.ToArray());
        private static void AddError(ICollection<BattlePresentationDiagnostic> errors, string code, string message, string battleId, string unitId, int tick, int sequence) => errors.Add(new BattlePresentationDiagnostic(code, message, battleId, unitId, tick, sequence));

        private static string CreateEventDigest(IEnumerable<BattleEvent> events)
        {
            var builder = new StringBuilder();
            foreach (var item in events)
            {
                builder.Append((int)item.Type).Append('|').Append(item.Tick).Append('|').Append(item.Sequence).Append('|').Append(item.UnitId).Append('|').Append(item.UnitTypeId).Append('|').Append(item.UnitSide).Append('|').Append(item.RelatedUnitId).Append('|').Append(item.FromPosition).Append('|').Append(item.ToPosition).Append('|').Append(item.DamageType).Append('|').Append(item.DamageAmount).Append('|').Append(item.HitPointsBefore).Append('|').Append(item.HitPointsAfter).Append('|').Append(item.PlannedDamageTick).Append('|').Append(item.OriginalAnimationTicks).Append('|').Append(item.EffectiveAnimationTicks).Append('|').Append(item.Winner).Append('|').Append(item.Reason);
                AppendSnapshot(builder, item.SpawnSnapshot);
            }
            return BattlePresentationTrack.Digest(builder.ToString());
        }

        private static void AppendSnapshot(StringBuilder builder, BattleUnitInstanceSnapshot snapshot)
        {
            if (snapshot == null) return;
            builder.Append("|spawn:").Append(snapshot.UnitId).Append('|').Append(snapshot.TypeId).Append('|').Append(snapshot.PlayerId).Append('|').Append(snapshot.Side).Append('|').Append(snapshot.IsDynamicallyGenerated).Append('|').Append(snapshot.Position).Append('|').Append(snapshot.EliteLevel).Append('|').Append(snapshot.MaxHitPoints).Append('|').Append(snapshot.CurrentHitPoints).Append('|').Append(snapshot.CurrentShield).Append('|').Append(snapshot.Attack).Append('|').Append(snapshot.Defense).Append('|').Append(snapshot.MagicResistance).Append('|').Append(snapshot.MoveSpeedCentimetresPerSecond).Append('|').Append(snapshot.AttackIntervalTicks).Append('|').Append(snapshot.AttackAnimationDurationTicks).Append('|').Append(snapshot.DamageType).Append('|').Append(snapshot.AttackMethod).Append('|').Append(snapshot.BlockCapacity).Append('|').Append(snapshot.TauntLevel);
            foreach (var buff in snapshot.Buffs) builder.Append("|buff:").Append(buff.Id).Append('|').Append(buff.RawPayload);
        }
    }
}
