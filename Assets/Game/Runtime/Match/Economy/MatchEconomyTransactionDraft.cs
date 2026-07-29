using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    internal sealed class MatchEconomyTransactionDraft
    {
        private readonly MatchState source;
        private readonly Dictionary<string, MatchSeatState> seatsByPlayerId;
        private readonly Dictionary<string, MatchPoolEntityState> entitiesById;
        private readonly MatchDeterministicRandomV1 random;
        private int nextNaturalRefreshStartSeat;
        private readonly HashSet<int> appliedPostBattleRefreshRounds;
        private long nextAcquisitionOrdinal;

        internal MatchEconomyTransactionDraft(MatchState source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            seatsByPlayerId = source.Seats.ToDictionary(
                seat => seat.PlayerId,
                StringComparer.Ordinal);
            entitiesById = source.Pool.Entities.ToDictionary(
                entity => entity.UnitId,
                StringComparer.Ordinal);
            random = MatchDeterministicRandomV1.FromState(source.Pool.RandomState);
            nextNaturalRefreshStartSeat = source.Pool.NextNaturalRefreshStartSeat;
            appliedPostBattleRefreshRounds =
                new HashSet<int>(source.Pool.AppliedPostBattleRefreshRounds);
            nextAcquisitionOrdinal = source.Pool.NextAcquisitionOrdinal;
        }

        internal MatchSeatState GetSeat(string playerId)
        {
            return seatsByPlayerId[playerId];
        }

        internal void ReplaceSeat(MatchSeatState seat)
        {
            seatsByPlayerId[seat.PlayerId] = seat;
        }

        internal bool RefreshPlayerShop(string playerId)
        {
            var seat = GetSeat(playerId);
            var targetSlots = seat.ShopOffers
                .Where(offer => !offer.IsFrozen)
                .Select(offer => offer.SlotIndex)
                .OrderBy(slot => slot)
                .ToArray();
            ReturnOffersToPool(seat, targetSlots);
            return DrawAndAssign(seat, targetSlots);
        }

        internal bool ToggleFreeze(string playerId, MatchPhase phase)
        {
            var seat = GetSeat(playerId);
            var occupied = seat.ShopOffers.Where(offer => !offer.IsEmpty).ToArray();
            if (occupied.Length == 0)
            {
                return false;
            }

            var shouldFreeze = occupied.Any(offer => !offer.IsFrozen);
            var offers = seat.ShopOffers.Select(offer =>
                offer.IsEmpty
                    ? offer
                    : new MatchShopOfferState(
                        offer.SlotIndex,
                        offer.UnitId,
                        offer.TypeId,
                        offer.Rarity,
                        shouldFreeze));
            var behavior = phase == MatchPhase.Preparation && shouldFreeze
                ? seat.PreparationBehavior.WithEffectiveFreeze()
                : seat.PreparationBehavior;
            ReplaceSeat(seat.With(preparationBehavior: behavior, shopOffers: offers));
            return true;
        }

        internal bool ApplyPostBattleNaturalRefresh(int roundNumber)
        {
            if (!appliedPostBattleRefreshRounds.Add(roundNumber))
            {
                return false;
            }

            var eligibleSeats = RotatedSeats(nextNaturalRefreshStartSeat)
                .Where(seat => !seat.Eliminated)
                .ToArray();
            var targetSlotsByPlayer = eligibleSeats.ToDictionary(
                seat => seat.PlayerId,
                seat => seat.ShopOffers
                    .Where(offer => !offer.IsFrozen)
                    .Select(offer => offer.SlotIndex)
                    .OrderBy(slot => slot)
                    .ToArray(),
                StringComparer.Ordinal);

            foreach (var seat in eligibleSeats)
            {
                ReturnOffersToPool(seat, targetSlotsByPlayer[seat.PlayerId]);
            }

            var drawnByPlayer = eligibleSeats.ToDictionary(
                seat => seat.PlayerId,
                seat => new List<MatchPoolEntityState>(),
                StringComparer.Ordinal);
            var poolExhausted = false;
            for (var slotIndex = 1; slotIndex <= MatchEconomyRules.ShopSlotCount; slotIndex++)
            {
                foreach (var originalSeat in eligibleSeats)
                {
                    if (!targetSlotsByPlayer[originalSeat.PlayerId].Contains(slotIndex))
                    {
                        continue;
                    }
                    var seat = GetSeat(originalSeat.PlayerId);
                    if (MatchShopDrawEngine.TryDraw(
                        seat.Level,
                        source.Pool.Catalog,
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

            foreach (var originalSeat in eligibleSeats)
            {
                var seat = GetSeat(originalSeat.PlayerId);
                AssignOffers(
                    seat,
                    targetSlotsByPlayer[seat.PlayerId],
                    drawnByPlayer[seat.PlayerId]);
                seat = GetSeat(seat.PlayerId);
                var discount = seat.Level == MatchEconomyRules.MaximumLevel
                    ? 0
                    : Math.Min(
                        MatchUpgradePricing.GetBasePrice(seat.Level),
                        checked(seat.UpgradeDiscountCountAtThisLevel + 1));
                ReplaceSeat(seat.With(upgradeDiscountCountAtThisLevel: discount));
            }

            nextNaturalRefreshStartSeat =
                nextNaturalRefreshStartSeat % source.Seats.Count + 1;
            return poolExhausted;
        }

        internal void SpendGold(string playerId, int amount)
        {
            var seat = GetSeat(playerId);
            ReplaceSeat(seat.With(gold: checked(seat.Gold - amount)));
        }

        internal bool TryPurchaseShopOffer(
            string playerId,
            int shopSlotIndex,
            string expectedUnitId,
            IStagingSlotPolicy stagingSlotPolicy,
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            var seat = GetSeat(playerId);
            if (stagingSlotPolicy == null)
            {
                code = MatchCommandCode.InternalInvariantViolation;
                diagnosticCode = "match.shop.purchase.staging.policy.null";
                return false;
            }
            if (shopSlotIndex < 1 || shopSlotIndex > MatchEconomyRules.ShopSlotCount)
            {
                code = MatchCommandCode.ShopSlotInvalid;
                diagnosticCode = "match.shop.purchase.slot.invalid";
                return false;
            }
            var offer = seat.ShopOffers.Single(item => item.SlotIndex == shopSlotIndex);
            if (offer.IsEmpty)
            {
                code = MatchCommandCode.ShopOfferMissing;
                diagnosticCode = "match.shop.purchase.offer.missing";
                return false;
            }
            if (!string.Equals(offer.UnitId, expectedUnitId, StringComparison.Ordinal))
            {
                code = MatchCommandCode.ShopOfferChanged;
                diagnosticCode = "match.shop.purchase.offer.changed";
                return false;
            }
            var stagingSlotUsage = stagingSlotPolicy.CountOccupiedSlots(seat.Units);
            if (stagingSlotUsage < 0)
            {
                code = MatchCommandCode.InternalInvariantViolation;
                diagnosticCode = "match.shop.purchase.staging.usage.invalid";
                return false;
            }
            if (stagingSlotUsage >= MatchEconomyRules.StagingSlotCapacity)
            {
                code = MatchCommandCode.StagingFull;
                diagnosticCode = "match.shop.purchase.staging.full";
                return false;
            }
            var price = offer.Rarity.Value;
            if (seat.Gold < price)
            {
                code = MatchCommandCode.InsufficientGold;
                diagnosticCode = "match.shop.purchase.gold.insufficient";
                return false;
            }

            SpendGold(playerId, price);
            AddOwnedUnitToStaging(playerId, shopSlotIndex);
            code = MatchCommandCode.Accepted;
            diagnosticCode = "match.shop.purchase.accepted";
            return true;
        }

        internal MatchUnitState AddOwnedUnitToStaging(string playerId, int shopSlotIndex)
        {
            var seat = GetSeat(playerId);
            var offer = seat.ShopOffers.Single(item => item.SlotIndex == shopSlotIndex);
            var ordinal = nextAcquisitionOrdinal;
            nextAcquisitionOrdinal = checked(nextAcquisitionOrdinal + 1);
            var unit = new MatchUnitState(
                offer.UnitId,
                offer.TypeId,
                MatchUnitZone.Staging,
                0,
                null,
                ordinal,
                Array.Empty<MatchBuffState>());
            entitiesById[offer.UnitId] = entitiesById[offer.UnitId].WithLocation(
                MatchPoolEntityLocation.OwnedUnit,
                playerId);
            var offers = seat.ShopOffers.Select(item =>
                item.SlotIndex == shopSlotIndex
                    ? EmptyOffer(shopSlotIndex)
                    : item);
            ReplaceSeat(seat.With(
                preparationBehavior: seat.PreparationBehavior.WithSuccessfulPurchase(),
                units: seat.Units.Concat(new[] { unit }),
                shopOffers: offers));
            return unit;
        }

        internal void PurchaseLevelUpgrade(string playerId)
        {
            var seat = GetSeat(playerId);
            ReplaceSeat(seat.With(
                gold: checked(seat.Gold - seat.CurrentUpgradePrice),
                level: checked(seat.Level + 1),
                upgradeDiscountCountAtThisLevel: 0));
        }

        internal void ResetRoundBehaviorFacts()
        {
            foreach (var seat in source.Seats)
            {
                var current = GetSeat(seat.PlayerId);
                ReplaceSeat(current.With(
                    preparationBehavior: MatchPreparationBehaviorState.Empty));
            }
        }

        internal MatchState BuildState(bool incrementRevision)
        {
            var pool = source.Pool.With(
                entitiesById.Values,
                random.Snapshot,
                nextNaturalRefreshStartSeat,
                appliedPostBattleRefreshRounds,
                nextAcquisitionOrdinal);
            return source.WithEconomy(
                source.Seats.Select(seat => seatsByPlayerId[seat.PlayerId]),
                pool,
                incrementRevision);
        }

        private IEnumerable<MatchSeatState> RotatedSeats(int startSeatIndex)
        {
            for (var offset = 0; offset < source.Seats.Count; offset++)
            {
                var seatIndex = (startSeatIndex - 1 + offset) % source.Seats.Count + 1;
                yield return source.Seats.Single(seat => seat.SeatIndex == seatIndex);
            }
        }

        private void ReturnOffersToPool(MatchSeatState seat, IReadOnlyCollection<int> targetSlots)
        {
            var targetSet = new HashSet<int>(targetSlots);
            var offers = seat.ShopOffers.Select(offer =>
            {
                if (!targetSet.Contains(offer.SlotIndex))
                {
                    return offer;
                }
                if (!offer.IsEmpty)
                {
                    entitiesById[offer.UnitId] = entitiesById[offer.UnitId].WithLocation(
                        MatchPoolEntityLocation.AvailablePool);
                }
                return EmptyOffer(offer.SlotIndex);
            }).ToArray();
            ReplaceSeat(seat.With(shopOffers: offers));
        }

        private bool DrawAndAssign(MatchSeatState originalSeat, IReadOnlyList<int> targetSlots)
        {
            var drawn = new List<MatchPoolEntityState>();
            var poolExhausted = false;
            foreach (var ignored in targetSlots)
            {
                if (MatchShopDrawEngine.TryDraw(
                    originalSeat.Level,
                    source.Pool.Catalog,
                    entitiesById,
                    random,
                    originalSeat.PlayerId,
                    out var entity))
                {
                    drawn.Add(entity);
                }
                else
                {
                    poolExhausted = true;
                }
            }
            AssignOffers(GetSeat(originalSeat.PlayerId), targetSlots, drawn);
            return poolExhausted;
        }

        private void AssignOffers(
            MatchSeatState seat,
            IReadOnlyList<int> targetSlots,
            IEnumerable<MatchPoolEntityState> drawn)
        {
            var sorted = MatchShopDrawEngine.SortNewOffers(drawn, source.Pool.Catalog);
            var sortedSlots = targetSlots.OrderBy(slot => slot).ToArray();
            var replacements = new Dictionary<int, MatchShopOfferState>();
            for (var index = 0; index < sortedSlots.Length; index++)
            {
                var slotIndex = sortedSlots[index];
                if (index >= sorted.Count)
                {
                    replacements[slotIndex] = EmptyOffer(slotIndex);
                    continue;
                }

                var entity = sorted[index].WithLocation(
                    MatchPoolEntityLocation.ShopOffer,
                    seat.PlayerId,
                    slotIndex);
                entitiesById[entity.UnitId] = entity;
                replacements[slotIndex] = new MatchShopOfferState(
                    slotIndex,
                    entity.UnitId,
                    entity.TypeId,
                    entity.Rarity,
                    false);
            }
            ReplaceSeat(seat.With(
                shopOffers: seat.ShopOffers.Select(offer =>
                    replacements.TryGetValue(offer.SlotIndex, out var replacement)
                        ? replacement
                        : offer)));
        }

        private static MatchShopOfferState EmptyOffer(int slotIndex)
        {
            return new MatchShopOfferState(
                slotIndex,
                string.Empty,
                string.Empty,
                null,
                false);
        }
    }
}
