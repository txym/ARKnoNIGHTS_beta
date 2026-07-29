using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Match
{
    public static class MatchEliteRules
    {
        private static readonly int[] BaseCopyEquivalents = { 1, 2, 4, 8 };
        private static readonly int[] BattleEntityCounts = { 1, 2, 3, 5 };
        private static readonly int[] DeploymentCostMultipliers = { 1, 2, 3, 5 };

        public static int GetBaseCopyEquivalent(int eliteLevel)
        {
            ValidateEliteLevel(eliteLevel);
            return BaseCopyEquivalents[eliteLevel];
        }

        public static int GetBattleEntityCount(int eliteLevel)
        {
            ValidateEliteLevel(eliteLevel);
            return BattleEntityCounts[eliteLevel];
        }

        public static int GetDeploymentCost(
            MatchShopCatalogEntry entry,
            int eliteLevel)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.BaseDeploymentCost < 0
                || entry.MaxEliteLevel < 0
                || entry.MaxEliteLevel > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(entry));
            }
            ValidateEliteLevel(eliteLevel);
            if (eliteLevel > entry.MaxEliteLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(eliteLevel));
            }
            return checked(entry.BaseDeploymentCost * DeploymentCostMultipliers[eliteLevel]);
        }

        internal static bool HasValidDeploymentCosts(MatchShopCatalogEntry entry)
        {
            if (entry == null || entry.BaseDeploymentCost < 0) return false;
            try
            {
                for (var eliteLevel = 0; eliteLevel <= entry.MaxEliteLevel; eliteLevel++)
                {
                    GetDeploymentCost(entry, eliteLevel);
                }
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static void ValidateEliteLevel(int eliteLevel)
        {
            if (eliteLevel < 0 || eliteLevel > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(eliteLevel));
            }
        }
    }

    public sealed class MatchFusionStep
    {
        internal MatchFusionStep(
            string typeId,
            int fromEliteLevel,
            int toEliteLevel,
            string survivorUnitId,
            string consumedUnitId,
            MatchUnitZone survivorZoneAfterStep)
        {
            TypeId = typeId;
            FromEliteLevel = fromEliteLevel;
            ToEliteLevel = toEliteLevel;
            SurvivorUnitId = survivorUnitId;
            ConsumedUnitId = consumedUnitId;
            SurvivorZoneAfterStep = survivorZoneAfterStep;
            var writer = new CanonicalSummaryWriter(nameof(MatchFusionStep));
            writer.String("typeId", TypeId);
            writer.Integer("fromEliteLevel", FromEliteLevel);
            writer.Integer("toEliteLevel", ToEliteLevel);
            writer.String("survivorUnitId", SurvivorUnitId);
            writer.String("consumedUnitId", ConsumedUnitId);
            writer.EnumValue("survivorZoneAfterStep", SurvivorZoneAfterStep);
            CanonicalSummary = writer.ToString();
        }

        public string TypeId { get; }
        public int FromEliteLevel { get; }
        public int ToEliteLevel { get; }
        public string SurvivorUnitId { get; }
        public string ConsumedUnitId { get; }
        public MatchUnitZone SurvivorZoneAfterStep { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchAcquisitionResult
    {
        internal MatchAcquisitionResult(
            string acquiredUnitId,
            string finalSurvivorUnitId,
            IEnumerable<MatchFusionStep> fusionSteps,
            IEnumerable<string> retiredUnitIds,
            MatchUnitZone finalZone,
            int finalEliteLevel,
            int goldSpent)
        {
            AcquiredUnitId = acquiredUnitId;
            FinalSurvivorUnitId = finalSurvivorUnitId;
            FusionSteps = new ReadOnlyCollection<MatchFusionStep>(
                (fusionSteps ?? Enumerable.Empty<MatchFusionStep>()).ToArray());
            RetiredUnitIds = new ReadOnlyCollection<string>(
                (retiredUnitIds ?? Enumerable.Empty<string>()).ToArray());
            FinalZone = finalZone;
            FinalEliteLevel = finalEliteLevel;
            GoldSpent = goldSpent;

            var writer = new CanonicalSummaryWriter(nameof(MatchAcquisitionResult));
            writer.String("acquiredUnitId", AcquiredUnitId);
            writer.String("finalSurvivorUnitId", FinalSurvivorUnitId);
            writer.EnumValue("finalZone", FinalZone);
            writer.Integer("finalEliteLevel", FinalEliteLevel);
            writer.Integer("goldSpent", GoldSpent);
            foreach (var step in FusionSteps) writer.Summary("fusionStep", step.CanonicalSummary);
            foreach (var retiredUnitId in RetiredUnitIds)
            {
                writer.String("retiredUnitId", retiredUnitId);
            }
            CanonicalSummary = writer.ToString();
        }

        public string AcquiredUnitId { get; }
        public string FinalSurvivorUnitId { get; }
        public IReadOnlyList<MatchFusionStep> FusionSteps { get; }
        public IReadOnlyList<string> RetiredUnitIds { get; }
        public MatchUnitZone FinalZone { get; }
        public int FinalEliteLevel { get; }
        public int GoldSpent { get; }
        public string CanonicalSummary { get; }
    }

    internal sealed class MatchAuthorizedPersistentUnit
    {
        internal MatchAuthorizedPersistentUnit(
            string unitId,
            string typeId,
            int eliteLevel,
            IEnumerable<MatchBuffState> buffs)
        {
            UnitId = unitId;
            TypeId = typeId;
            EliteLevel = eliteLevel;
            Buffs = new ReadOnlyCollection<MatchBuffState>(
                (buffs ?? Enumerable.Empty<MatchBuffState>()).ToArray());
        }

        internal string UnitId { get; }
        internal string TypeId { get; }
        internal int EliteLevel { get; }
        internal IReadOnlyList<MatchBuffState> Buffs { get; }
    }

    public enum MatchRetirementReason
    {
        FusionConsumed = 0,
        OverflowDiscarded = 1
    }

    public sealed class MatchRetiredPersistentUnitState
    {
        internal MatchRetiredPersistentUnitState(
            string unitId,
            MatchRetirementReason reason)
        {
            UnitId = unitId;
            Reason = reason;
            var writer = new CanonicalSummaryWriter(nameof(MatchRetiredPersistentUnitState));
            writer.String("unitId", UnitId);
            writer.EnumValue("reason", Reason);
            CanonicalSummary = writer.ToString();
        }

        public string UnitId { get; }
        public MatchRetirementReason Reason { get; }
        public string CanonicalSummary { get; }
    }
}
