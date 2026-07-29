using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ArknoNights.Match
{
    public enum MatchPoolEntityLocation
    {
        AvailablePool = 0,
        ShopOffer = 1,
        OwnedUnit = 2
    }

    public sealed class MatchPoolEntityState
    {
        internal MatchPoolEntityState(
            string unitId,
            string typeId,
            int rarity,
            int copyIndex,
            MatchPoolEntityLocation location,
            string playerId,
            int? shopSlotIndex)
        {
            UnitId = unitId;
            TypeId = typeId;
            Rarity = rarity;
            CopyIndex = copyIndex;
            Location = location;
            PlayerId = playerId ?? string.Empty;
            ShopSlotIndex = shopSlotIndex;
            var writer = new CanonicalSummaryWriter(nameof(MatchPoolEntityState));
            writer.String("unitId", UnitId);
            writer.String("typeId", TypeId);
            writer.Integer("rarity", Rarity);
            writer.Integer("copyIndex", CopyIndex);
            writer.EnumValue("location", Location);
            writer.String("playerId", PlayerId);
            writer.NullableInteger("shopSlotIndex", ShopSlotIndex);
            CanonicalSummary = writer.ToString();
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public int Rarity { get; }
        public int CopyIndex { get; }
        public MatchPoolEntityLocation Location { get; }
        public string PlayerId { get; }
        public int? ShopSlotIndex { get; }
        public string CanonicalSummary { get; }

        internal MatchPoolEntityState WithLocation(
            MatchPoolEntityLocation location,
            string playerId = "",
            int? shopSlotIndex = null)
        {
            return new MatchPoolEntityState(
                UnitId,
                TypeId,
                Rarity,
                CopyIndex,
                location,
                playerId,
                shopSlotIndex);
        }
    }

    public sealed class MatchPoolRemainingCount
    {
        internal MatchPoolRemainingCount(string typeId, int count)
        {
            TypeId = typeId;
            Count = count;
            var writer = new CanonicalSummaryWriter(nameof(MatchPoolRemainingCount));
            writer.String("typeId", TypeId);
            writer.Integer("count", Count);
            CanonicalSummary = writer.ToString();
        }

        public string TypeId { get; }
        public int Count { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchPoolState
    {
        internal MatchPoolState(
            MatchShopCatalog catalog,
            IEnumerable<MatchPoolEntityState> entities,
            MatchRandomStateSnapshot randomState,
            int nextNaturalRefreshStartSeat,
            IEnumerable<int> appliedPostBattleRefreshRounds,
            long nextAcquisitionOrdinal)
        {
            Catalog = catalog;
            Entities = new ReadOnlyCollection<MatchPoolEntityState>(
                (entities ?? Enumerable.Empty<MatchPoolEntityState>())
                    .OrderBy(entity => entity.TypeId, StringComparer.Ordinal)
                    .ThenBy(entity => entity.CopyIndex)
                    .ToArray());
            RandomState = randomState;
            NextNaturalRefreshStartSeat = nextNaturalRefreshStartSeat;
            AppliedPostBattleRefreshRounds = new ReadOnlyCollection<int>(
                (appliedPostBattleRefreshRounds ?? Enumerable.Empty<int>())
                    .Distinct()
                    .OrderBy(round => round)
                    .ToArray());
            NextAcquisitionOrdinal = nextAcquisitionOrdinal;
            RemainingByType = new ReadOnlyCollection<MatchPoolRemainingCount>(
                Catalog.Entries
                    .Where(entry => entry.IsShopEligible)
                    .Select(entry => new MatchPoolRemainingCount(
                        entry.TypeId,
                        Entities.Count(entity =>
                            entity.Location == MatchPoolEntityLocation.AvailablePool
                            && string.Equals(entity.TypeId, entry.TypeId, StringComparison.Ordinal))))
                    .ToArray());

            var writer = new CanonicalSummaryWriter(nameof(MatchPoolState));
            writer.Summary("catalog", Catalog == null ? string.Empty : Catalog.CanonicalSummary);
            writer.String("randomAlgorithm", MatchDeterministicRandomV1.AlgorithmVersion);
            writer.Summary("randomState", RandomState == null ? string.Empty : RandomState.CanonicalSummary);
            writer.Integer("nextNaturalRefreshStartSeat", NextNaturalRefreshStartSeat);
            writer.Integer("nextAcquisitionOrdinal", NextAcquisitionOrdinal);
            foreach (var round in AppliedPostBattleRefreshRounds) writer.Integer("appliedRound", round);
            foreach (var entity in Entities) writer.Summary("entity", entity.CanonicalSummary);
            CanonicalSummary = writer.ToString();
        }

        public MatchShopCatalog Catalog { get; }
        public IReadOnlyList<MatchPoolEntityState> Entities { get; }
        public IReadOnlyList<MatchPoolRemainingCount> RemainingByType { get; }
        public string RandomAlgorithmVersion => MatchDeterministicRandomV1.AlgorithmVersion;
        public MatchRandomStateSnapshot RandomState { get; }
        public int NextNaturalRefreshStartSeat { get; }
        public IReadOnlyList<int> AppliedPostBattleRefreshRounds { get; }
        public long NextAcquisitionOrdinal { get; }
        public string CanonicalSummary { get; }

        internal static MatchPoolState CreateInitial(
            string sessionId,
            string matchSeed,
            MatchShopCatalog catalog)
        {
            var entities = new List<MatchPoolEntityState>();
            foreach (var entry in catalog.Entries.Where(entry => entry.IsShopEligible))
            {
                var copyCount = MatchPoolCopyCounts.GetForRarity(entry.Rarity);
                for (var copyIndex = 1; copyIndex <= copyCount; copyIndex++)
                {
                    entities.Add(new MatchPoolEntityState(
                        MatchPoolUnitId.Create(sessionId, entry.TypeId, copyIndex),
                        entry.TypeId,
                        entry.Rarity,
                        copyIndex,
                        MatchPoolEntityLocation.AvailablePool,
                        string.Empty,
                        null));
                }
            }
            return new MatchPoolState(
                catalog,
                entities,
                MatchDeterministicRandomV1.FromSeed(matchSeed, "match-shop-draw-v1").Snapshot,
                1,
                Array.Empty<int>(),
                0);
        }

        internal MatchPoolState With(
            IEnumerable<MatchPoolEntityState> entities,
            MatchRandomStateSnapshot randomState,
            int? nextNaturalRefreshStartSeat = null,
            IEnumerable<int> appliedPostBattleRefreshRounds = null,
            long? nextAcquisitionOrdinal = null)
        {
            return new MatchPoolState(
                Catalog,
                entities,
                randomState,
                nextNaturalRefreshStartSeat ?? NextNaturalRefreshStartSeat,
                appliedPostBattleRefreshRounds ?? AppliedPostBattleRefreshRounds,
                nextAcquisitionOrdinal ?? NextAcquisitionOrdinal);
        }
    }

    public static class MatchPoolCopyCounts
    {
        public static int GetForRarity(int rarity)
        {
            switch (rarity)
            {
                case 1: return 28;
                case 2: return 24;
                case 3: return 14;
                case 4: return 10;
                case 5: return 8;
                case 6: return 6;
                default: throw new ArgumentOutOfRangeException(nameof(rarity));
            }
        }
    }
}
