using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    public static class MatchEconomyRules
    {
        public const int ShopSlotCount = 6;
        public const int RefreshCost = 1;
        public const int StagingSlotCapacity = 13;
        public const int MinimumLevel = 1;
        public const int MaximumLevel = 9;
    }

    public static class MatchShopOdds
    {
        private static readonly int[][] WeightsByLevel =
        {
            new[] { 80, 20, 0, 0, 0, 0 },
            new[] { 65, 35, 0, 0, 0, 0 },
            new[] { 50, 40, 10, 0, 0, 0 },
            new[] { 38, 40, 20, 2, 0, 0 },
            new[] { 27, 38, 27, 8, 0, 0 },
            new[] { 18, 32, 30, 18, 2, 0 },
            new[] { 11, 24, 30, 28, 6, 1 },
            new[] { 5, 14, 24, 39, 16, 2 },
            new[] { 2, 7, 11, 40, 30, 10 }
        };

        public static int[] GetRarityWeights(int level)
        {
            if (level < MatchEconomyRules.MinimumLevel || level > MatchEconomyRules.MaximumLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }
            return WeightsByLevel[level - MatchEconomyRules.MinimumLevel].ToArray();
        }
    }

    public static class MatchUpgradePricing
    {
        public static int GetBasePrice(int currentLevel)
        {
            if (currentLevel < MatchEconomyRules.MinimumLevel || currentLevel >= MatchEconomyRules.MaximumLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(currentLevel));
            }
            return 3 + (currentLevel * (currentLevel + 1) / 2);
        }

        public static int GetCurrentPrice(int currentLevel, int discountCount)
        {
            if (discountCount < 0) throw new ArgumentOutOfRangeException(nameof(discountCount));
            if (currentLevel == MatchEconomyRules.MaximumLevel)
            {
                if (discountCount != 0) throw new ArgumentOutOfRangeException(nameof(discountCount));
                return 0;
            }
            return Math.Max(0, GetBasePrice(currentLevel) - discountCount);
        }
    }

    public sealed class MatchPreparationBehaviorState
    {
        public MatchPreparationBehaviorState(
            int successfulShopPurchaseCount,
            bool hasIssuedEffectiveFreezeThisRound)
        {
            SuccessfulShopPurchaseCount = successfulShopPurchaseCount;
            HasIssuedEffectiveFreezeThisRound = hasIssuedEffectiveFreezeThisRound;
            var writer = new CanonicalSummaryWriter(nameof(MatchPreparationBehaviorState));
            writer.Integer("purchaseCount", SuccessfulShopPurchaseCount);
            writer.Boolean("effectiveFreeze", HasIssuedEffectiveFreezeThisRound);
            CanonicalSummary = writer.ToString();
        }

        public static MatchPreparationBehaviorState Empty =>
            new MatchPreparationBehaviorState(0, false);

        public int SuccessfulShopPurchaseCount { get; }
        public bool HasIssuedEffectiveFreezeThisRound { get; }
        public string CanonicalSummary { get; }

        internal MatchPreparationBehaviorState WithSuccessfulPurchase()
        {
            return new MatchPreparationBehaviorState(
                checked(SuccessfulShopPurchaseCount + 1),
                HasIssuedEffectiveFreezeThisRound);
        }

        internal MatchPreparationBehaviorState WithEffectiveFreeze()
        {
            return HasIssuedEffectiveFreezeThisRound
                ? this
                : new MatchPreparationBehaviorState(SuccessfulShopPurchaseCount, true);
        }
    }

    public interface IStagingSlotPolicy
    {
        int CountOccupiedSlots(IReadOnlyList<MatchUnitState> units);
    }

    public sealed class StrictStagingSlotPolicy : IStagingSlotPolicy
    {
        public int CountOccupiedSlots(IReadOnlyList<MatchUnitState> units)
        {
            return (units ?? Array.Empty<MatchUnitState>())
                .Where(unit => unit != null && unit.Zone == MatchUnitZone.Staging)
                .GroupBy(BuildStackKey, StringComparer.Ordinal)
                .Count();
        }

        private static string BuildStackKey(MatchUnitState unit)
        {
            var writer = new CanonicalSummaryWriter("StagingStack");
            writer.String("typeId", unit.TypeId);
            writer.Integer("eliteLevel", unit.EliteLevel);
            foreach (var buff in unit.Buffs)
            {
                writer.Summary("buff", buff.CanonicalSummary);
            }
            return writer.ToString();
        }
    }
}
