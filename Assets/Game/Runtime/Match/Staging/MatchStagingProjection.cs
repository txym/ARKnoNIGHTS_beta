using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Match
{
    public sealed class MatchStagingStackSnapshot
    {
        internal MatchStagingStackSnapshot(
            string typeId,
            long numericTypeId,
            int deploymentCost,
            int eliteLevel,
            string buffCanonicalSummary,
            IEnumerable<string> unitIds)
        {
            TypeId = typeId;
            NumericTypeId = numericTypeId;
            DeploymentCost = deploymentCost;
            EliteLevel = eliteLevel;
            BuffCanonicalSummary = buffCanonicalSummary;
            UnitIds = new ReadOnlyCollection<string>(
                (unitIds ?? Enumerable.Empty<string>())
                    .OrderBy(unitId => unitId, StringComparer.Ordinal)
                    .ToArray());
            var writer = new CanonicalSummaryWriter(nameof(MatchStagingStackSnapshot));
            writer.String("typeId", TypeId);
            writer.Integer("numericTypeId", NumericTypeId);
            writer.Integer("deploymentCost", DeploymentCost);
            writer.Integer("eliteLevel", EliteLevel);
            writer.Summary("buffs", BuffCanonicalSummary);
            foreach (var unitId in UnitIds) writer.String("unitId", unitId);
            CanonicalSummary = writer.ToString();
        }

        public string TypeId { get; }
        public long NumericTypeId { get; }
        public int DeploymentCost { get; }
        public int EliteLevel { get; }
        public string BuffCanonicalSummary { get; }
        public IReadOnlyList<string> UnitIds { get; }
        public string CanonicalSummary { get; }
    }

    public static class MatchStagingProjection
    {
        public static IReadOnlyList<MatchStagingStackSnapshot> Project(
            MatchSeatState seat,
            MatchShopCatalog catalog)
        {
            if (seat == null) throw new ArgumentNullException(nameof(seat));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var groups = seat.Units
                .Where(unit => unit.Zone == MatchUnitZone.Staging)
                .Select(unit => new ProjectedUnit(unit, BuildBuffSummary(seat, unit)))
                .GroupBy(
                    item => BuildStackKey(item.Unit, item.BuffCanonicalSummary),
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    if (!catalog.TryGet(first.Unit.TypeId, out var entry))
                    {
                        throw new InvalidOperationException(
                            "Staging unit type is absent from the authoritative catalog.");
                    }
                    return new MatchStagingStackSnapshot(
                        first.Unit.TypeId,
                        entry.NumericTypeId,
                        MatchEliteRules.GetDeploymentCost(entry, first.Unit.EliteLevel),
                        first.Unit.EliteLevel,
                        first.BuffCanonicalSummary,
                        group.Select(item => item.Unit.UnitId));
                })
                .OrderBy(stack => stack.DeploymentCost)
                .ThenBy(stack => stack.NumericTypeId)
                .ThenBy(stack => stack.EliteLevel)
                .ThenBy(stack => stack.BuffCanonicalSummary, StringComparer.Ordinal)
                .ThenBy(stack => stack.UnitIds[0], StringComparer.Ordinal)
                .ToArray();
            return new ReadOnlyCollection<MatchStagingStackSnapshot>(groups);
        }

        public static int CountOccupiedSlots(
            MatchSeatState seat,
            MatchShopCatalog catalog)
        {
            return Project(seat, catalog).Count;
        }

        public static bool CanPlaceInStaging(
            MatchUnitState unit,
            MatchSeatState seat,
            MatchShopCatalog catalog)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (seat == null) throw new ArgumentNullException(nameof(seat));

            var stagingUnit = new MatchUnitState(
                unit.UnitId,
                unit.TypeId,
                MatchUnitZone.Staging,
                unit.EliteLevel,
                null,
                unit.AcquisitionOrdinal,
                unit.Buffs);
            var found = false;
            var units = seat.Units.Select(existing =>
            {
                if (!string.Equals(existing.UnitId, stagingUnit.UnitId, StringComparison.Ordinal))
                {
                    return existing;
                }
                found = true;
                return stagingUnit;
            }).ToList();
            if (!found) units.Add(stagingUnit);
            var prospective = seat.With(units: units);
            return CountOccupiedSlots(prospective, catalog)
                <= MatchEconomyRules.StagingSlotCapacity;
        }

        internal static string BuildBuffSummary(MatchSeatState seat, MatchUnitState unit)
        {
            var writer = new CanonicalSummaryWriter("MatchOwnedUnitBuffProjection");
            foreach (var buff in unit.Buffs.OrderBy(
                item => item.CanonicalSummary,
                StringComparer.Ordinal))
            {
                writer.Summary("inlineBuff", buff.CanonicalSummary);
            }
            foreach (var buff in seat.TargetedUnitBuffs
                .Where(item => string.Equals(
                    item.TargetUnitId,
                    unit.UnitId,
                    StringComparison.Ordinal))
                .OrderBy(item => item.CanonicalSummary, StringComparer.Ordinal))
            {
                writer.Summary("targetedBuff", buff.CanonicalSummary);
            }
            return writer.ToString();
        }

        private static string BuildStackKey(
            MatchUnitState unit,
            string buffCanonicalSummary)
        {
            var writer = new CanonicalSummaryWriter("MatchStagingStackKey");
            writer.String("typeId", unit.TypeId);
            writer.Integer("eliteLevel", unit.EliteLevel);
            writer.Summary("buffs", buffCanonicalSummary);
            return writer.ToString();
        }

        private sealed class ProjectedUnit
        {
            internal ProjectedUnit(
                MatchUnitState unit,
                string buffCanonicalSummary)
            {
                Unit = unit;
                BuffCanonicalSummary = buffCanonicalSummary;
            }

            internal MatchUnitState Unit { get; }
            internal string BuffCanonicalSummary { get; }
        }
    }
}
