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

            var units = builders.Values
                .OrderBy(item => item.SpawnTick)
                .ThenBy(item => item.SpawnSequence)
                .ThenBy(item => item.Snapshot.UnitId, StringComparer.Ordinal)
                .Select(item => new UnitPresentationTrack(item.Snapshot, item.SpawnTick, result.CompletedTicks, item.DeathTick, item.Positions, item.HitPoints, item.Attacks))
                .ToArray();
            track = new BattlePresentationTrack(result, units, CreateEventDigest(events));
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
