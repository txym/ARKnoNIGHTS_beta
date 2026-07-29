using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Match
{
    internal static class MatchShopDrawEngine
    {
        internal static bool TryDraw(
            int level,
            MatchShopCatalog catalog,
            IDictionary<string, MatchPoolEntityState> entitiesById,
            MatchDeterministicRandomV1 random,
            string playerId,
            out MatchPoolEntityState drawnEntity)
        {
            var levelWeights = MatchShopOdds.GetRarityWeights(level);
            var rarityWeights = new ulong[6];
            for (var rarity = 1; rarity <= 6; rarity++)
            {
                var hasAvailable = entitiesById.Values.Any(entity =>
                    entity.Location == MatchPoolEntityLocation.AvailablePool
                    && entity.Rarity == rarity);
                rarityWeights[rarity - 1] = hasAvailable
                    ? (ulong)levelWeights[rarity - 1]
                    : 0UL;
            }
            if (rarityWeights.All(weight => weight == 0UL))
            {
                drawnEntity = null;
                return false;
            }

            var selectedRarity = MatchWeightedSelector.SelectIndex(
                rarityWeights,
                random.NextBelow) + 1;
            var typeCandidates = catalog.Entries
                .Where(entry => entry.IsShopEligible && entry.Rarity == selectedRarity)
                .Select(entry => new TypeCandidate(
                    entry,
                    entitiesById.Values.Count(entity =>
                        entity.Location == MatchPoolEntityLocation.AvailablePool
                        && string.Equals(entity.TypeId, entry.TypeId, StringComparison.Ordinal))))
                .Where(candidate => candidate.RemainingCount > 0)
                .ToArray();
            var selectedTypeIndex = MatchWeightedSelector.SelectIndex(
                typeCandidates.Select(candidate => (ulong)candidate.RemainingCount).ToArray(),
                random.NextBelow);
            var selectedTypeId = typeCandidates[selectedTypeIndex].Entry.TypeId;
            var entity = entitiesById.Values
                .Where(candidate =>
                    candidate.Location == MatchPoolEntityLocation.AvailablePool
                    && string.Equals(candidate.TypeId, selectedTypeId, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.CopyIndex)
                .ThenBy(candidate => candidate.UnitId, StringComparer.Ordinal)
                .First();
            drawnEntity = entity.WithLocation(
                MatchPoolEntityLocation.ShopOffer,
                playerId,
                null);
            entitiesById[drawnEntity.UnitId] = drawnEntity;
            return true;
        }

        internal static IReadOnlyList<MatchPoolEntityState> SortNewOffers(
            IEnumerable<MatchPoolEntityState> offers,
            MatchShopCatalog catalog)
        {
            return new ReadOnlyCollection<MatchPoolEntityState>(
                (offers ?? Enumerable.Empty<MatchPoolEntityState>())
                    .Select((entity, drawOrdinal) => new OrderedOffer(
                        entity,
                        catalog.Entries.Single(entry =>
                            string.Equals(entry.TypeId, entity.TypeId, StringComparison.Ordinal)),
                        drawOrdinal))
                    .OrderBy(offer => offer.Entity.Rarity)
                    .ThenBy(offer => offer.Entry.NumericTypeId)
                    .ThenBy(offer => offer.DrawOrdinal)
                    .Select(offer => offer.Entity)
                    .ToArray());
        }

        private sealed class TypeCandidate
        {
            internal TypeCandidate(MatchShopCatalogEntry entry, int remainingCount)
            {
                Entry = entry;
                RemainingCount = remainingCount;
            }

            internal MatchShopCatalogEntry Entry { get; }
            internal int RemainingCount { get; }
        }

        private sealed class OrderedOffer
        {
            internal OrderedOffer(
                MatchPoolEntityState entity,
                MatchShopCatalogEntry entry,
                int drawOrdinal)
            {
                Entity = entity;
                Entry = entry;
                DrawOrdinal = drawOrdinal;
            }

            internal MatchPoolEntityState Entity { get; }
            internal MatchShopCatalogEntry Entry { get; }
            internal int DrawOrdinal { get; }
        }
    }

    internal sealed class MatchInitialShopResult
    {
        internal MatchInitialShopResult(
            IEnumerable<MatchSeatState> seats,
            MatchPoolState pool,
            bool poolExhausted)
        {
            Seats = seats.OrderBy(seat => seat.SeatIndex).ToArray();
            Pool = pool;
            PoolExhausted = poolExhausted;
        }

        internal IReadOnlyList<MatchSeatState> Seats { get; }
        internal MatchPoolState Pool { get; }
        internal bool PoolExhausted { get; }
    }

    internal static class MatchInitialShopBuilder
    {
        internal static MatchInitialShopResult Apply(
            IEnumerable<MatchSeatState> seats,
            MatchPoolState pool)
        {
            var orderedSeats = seats.OrderBy(seat => seat.SeatIndex).ToArray();
            var entitiesById = pool.Entities.ToDictionary(
                entity => entity.UnitId,
                StringComparer.Ordinal);
            var random = MatchDeterministicRandomV1.FromState(pool.RandomState);
            var drawnByPlayer = orderedSeats.ToDictionary(
                seat => seat.PlayerId,
                seat => new List<MatchPoolEntityState>(),
                StringComparer.Ordinal);
            var poolExhausted = false;

            for (var slotIndex = 1; slotIndex <= MatchEconomyRules.ShopSlotCount; slotIndex++)
            {
                foreach (var seat in orderedSeats)
                {
                    if (MatchShopDrawEngine.TryDraw(
                        seat.Level,
                        pool.Catalog,
                        entitiesById,
                        random,
                        seat.PlayerId,
                        out var entity))
                    {
                        drawnByPlayer[seat.PlayerId].Add(entity);
                    }
                    else
                    {
                        poolExhausted = true;
                    }
                }
            }

            var changedSeats = new List<MatchSeatState>();
            foreach (var seat in orderedSeats)
            {
                var sorted = MatchShopDrawEngine.SortNewOffers(
                    drawnByPlayer[seat.PlayerId],
                    pool.Catalog);
                var offers = new List<MatchShopOfferState>();
                for (var slotIndex = 1; slotIndex <= MatchEconomyRules.ShopSlotCount; slotIndex++)
                {
                    var entity = slotIndex <= sorted.Count ? sorted[slotIndex - 1] : null;
                    if (entity == null)
                    {
                        offers.Add(new MatchShopOfferState(
                            slotIndex,
                            string.Empty,
                            string.Empty,
                            null,
                            false));
                        continue;
                    }

                    var located = entity.WithLocation(
                        MatchPoolEntityLocation.ShopOffer,
                        seat.PlayerId,
                        slotIndex);
                    entitiesById[located.UnitId] = located;
                    offers.Add(new MatchShopOfferState(
                        slotIndex,
                        located.UnitId,
                        located.TypeId,
                        located.Rarity,
                        false));
                }
                changedSeats.Add(seat.With(shopOffers: offers));
            }

            return new MatchInitialShopResult(
                changedSeats,
                pool.With(
                    entitiesById.Values,
                    random.Snapshot,
                    nextNaturalRefreshStartSeat: 2),
                poolExhausted);
        }
    }
}
