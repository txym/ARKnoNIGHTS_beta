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
        private readonly Dictionary<string, MatchRetiredPersistentUnitState> retiredById;
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
            retiredById = source.Pool.RetiredUnits.ToDictionary(
                retired => retired.UnitId,
                StringComparer.Ordinal);
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
                .Where(seat => !GetSeat(seat.PlayerId).Eliminated)
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
            out MatchAcquisitionResult acquisition,
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            acquisition = null;
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
            var stagingSlotUsage = MatchStagingProjection.CountOccupiedSlots(
                seat,
                source.Pool.Catalog);
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
            if (nextAcquisitionOrdinal == long.MaxValue)
            {
                code = MatchCommandCode.InternalInvariantViolation;
                diagnosticCode = "match.acquisition.ordinal.exhausted";
                return false;
            }

            SpendGold(playerId, price);
            var acquired = AddOwnedUnitToStaging(playerId, shopSlotIndex);
            if (!TryResolveAcquisition(
                playerId,
                acquired.UnitId,
                acquired.TypeId,
                price,
                out acquisition,
                out code,
                out diagnosticCode))
            {
                return false;
            }
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

        internal bool TryAcquireAuthorizedPersistentUnit(
            string playerId,
            MatchAuthorizedPersistentUnit acquired,
            out MatchAcquisitionResult acquisition,
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            acquisition = null;
            if (source.Phase != MatchPhase.Preparation
                && source.Phase != MatchPhase.Battle)
            {
                code = MatchCommandCode.PhaseRejected;
                diagnosticCode = "match.acquisition.phase.rejected";
                return false;
            }
            if (acquired == null
                || string.IsNullOrWhiteSpace(acquired.UnitId)
                || string.IsNullOrWhiteSpace(acquired.TypeId)
                || acquired.Buffs.Any(buff =>
                    buff == null
                    || string.IsNullOrWhiteSpace(buff.BuffId)
                    || buff.CanonicalPayload == null))
            {
                code = MatchCommandCode.InvalidPayload;
                diagnosticCode = "match.acquisition.unit.invalid";
                return false;
            }
            if (!seatsByPlayerId.TryGetValue(playerId ?? string.Empty, out var seat))
            {
                code = MatchCommandCode.UnknownPlayer;
                diagnosticCode = "match.acquisition.player.unknown";
                return false;
            }
            if (seat.Eliminated)
            {
                code = MatchCommandCode.Eliminated;
                diagnosticCode = "match.acquisition.player.eliminated";
                return false;
            }
            if (!source.Pool.Catalog.TryGet(acquired.TypeId, out var entry)
                || acquired.EliteLevel < 0
                || acquired.EliteLevel > entry.MaxEliteLevel)
            {
                code = MatchCommandCode.InvalidPayload;
                diagnosticCode = "match.acquisition.catalog.invalid";
                return false;
            }
            if (nextAcquisitionOrdinal == long.MaxValue)
            {
                code = MatchCommandCode.InternalInvariantViolation;
                diagnosticCode = "match.acquisition.ordinal.exhausted";
                return false;
            }
            if (seatsByPlayerId.Values.SelectMany(item => item.Units).Any(
                    unit => string.Equals(unit.UnitId, acquired.UnitId, StringComparison.Ordinal))
                || entitiesById.ContainsKey(acquired.UnitId)
                || retiredById.ContainsKey(acquired.UnitId))
            {
                code = MatchCommandCode.InvalidPayload;
                diagnosticCode = "match.acquisition.unitId.duplicate";
                return false;
            }

            var ordinal = nextAcquisitionOrdinal;
            var unit = new MatchUnitState(
                acquired.UnitId,
                acquired.TypeId,
                MatchUnitZone.Staging,
                acquired.EliteLevel,
                null,
                ordinal,
                acquired.Buffs);
            var stagingSeat = seat.With(units: seat.Units.Concat(new[] { unit }));
            if (MatchStagingProjection.CountOccupiedSlots(
                stagingSeat,
                source.Pool.Catalog) > MatchEconomyRules.StagingSlotCapacity)
            {
                unit = new MatchUnitState(
                    unit.UnitId,
                    unit.TypeId,
                    MatchUnitZone.Overflow,
                    unit.EliteLevel,
                    null,
                    unit.AcquisitionOrdinal,
                    unit.Buffs);
            }
            nextAcquisitionOrdinal = checked(nextAcquisitionOrdinal + 1);
            ReplaceSeat(seat.With(units: seat.Units.Concat(new[] { unit })));
            return TryResolveAcquisition(
                playerId,
                unit.UnitId,
                unit.TypeId,
                0,
                out acquisition,
                out code,
                out diagnosticCode);
        }

        internal bool TryDiscardRemainingOverflow(
            out IReadOnlyList<string> retiredUnitIds,
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            var ordered = seatsByPlayerId.Values
                .OrderBy(seat => seat.SeatIndex)
                .SelectMany(seat => seat.Units
                    .Where(unit => unit.Zone == MatchUnitZone.Overflow)
                    .OrderBy(unit => unit.AcquisitionOrdinal)
                    .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
                    .Select(unit => new OverflowDiscardCandidate(seat.PlayerId, unit)))
                .ToArray();
            if (ordered.Length == 0)
            {
                retiredUnitIds = Array.Empty<string>();
                code = MatchCommandCode.AcceptedNoChange;
                diagnosticCode = "match.overflow.discard.noChange";
                return true;
            }

            foreach (var candidate in ordered)
            {
                var seat = GetSeat(candidate.PlayerId);
                var targeted = seat.TargetedUnitBuffs.Where(buff =>
                    string.Equals(
                        buff.TargetUnitId,
                        candidate.Unit.UnitId,
                        StringComparison.Ordinal));
                if (targeted.Any(buff =>
                    buff.DiscardPolicy != MatchTargetedBuffDiscardPolicy.RemoveWithTarget))
                {
                    retiredUnitIds = Array.Empty<string>();
                    code = MatchCommandCode.InvalidPayload;
                    diagnosticCode = "match.overflow.discard.buffPolicy.missing";
                    return false;
                }
            }

            foreach (var group in ordered.GroupBy(
                candidate => candidate.PlayerId,
                StringComparer.Ordinal))
            {
                var seat = GetSeat(group.Key);
                var discardedIds = new HashSet<string>(
                    group.Select(candidate => candidate.Unit.UnitId),
                    StringComparer.Ordinal);
                ReplaceSeat(seat.With(
                    units: seat.Units.Where(unit => !discardedIds.Contains(unit.UnitId)),
                    targetedUnitBuffs: seat.TargetedUnitBuffs.Where(
                        buff => !discardedIds.Contains(buff.TargetUnitId))));
            }
            foreach (var candidate in ordered)
            {
                if (!TryRetire(
                    candidate.PlayerId,
                    candidate.Unit.UnitId,
                    MatchRetirementReason.OverflowDiscarded,
                    out diagnosticCode))
                {
                    retiredUnitIds = Array.Empty<string>();
                    code = MatchCommandCode.InternalInvariantViolation;
                    return false;
                }
            }

            retiredUnitIds = ordered.Select(candidate => candidate.Unit.UnitId).ToArray();
            code = MatchCommandCode.Accepted;
            diagnosticCode = "match.overflow.discard.accepted";
            return true;
        }

        internal bool TryResolvePreparationEntryFusions(
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            var changed = false;
            foreach (var originalSeat in seatsByPlayerId.Values
                         .Where(seat => !seat.Eliminated)
                         .OrderBy(seat => seat.SeatIndex))
            {
                var typeIds = originalSeat.Units
                    .Select(unit => unit.TypeId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(typeId => typeId, StringComparer.Ordinal)
                    .ToArray();
                foreach (var typeId in typeIds)
                {
                    var anchor = GetSeat(originalSeat.PlayerId).Units
                        .Where(unit => string.Equals(
                            unit.TypeId,
                            typeId,
                            StringComparison.Ordinal))
                        .OrderBy(unit => ZonePriority(unit.Zone))
                        .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (anchor == null)
                    {
                        continue;
                    }
                    if (!TryResolveAcquisition(
                        originalSeat.PlayerId,
                        anchor.UnitId,
                        typeId,
                        0,
                        out var acquisition,
                        out code,
                        out diagnosticCode))
                    {
                        return false;
                    }
                    changed |= acquisition.FusionSteps.Count != 0;
                }
            }

            code = changed
                ? MatchCommandCode.Accepted
                : MatchCommandCode.AcceptedNoChange;
            diagnosticCode = changed
                ? "match.fusion.preparationEntry.accepted"
                : "match.fusion.preparationEntry.noChange";
            return true;
        }

        private bool TryResolveAcquisition(
            string playerId,
            string acquiredUnitId,
            string acquiredTypeId,
            int goldSpent,
            out MatchAcquisitionResult acquisition,
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            acquisition = null;
            var steps = new List<MatchFusionStep>();
            var retiredUnitIds = new List<string>();
            var survivorByConsumedId = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!source.Pool.Catalog.TryGet(acquiredTypeId, out var catalogEntry))
            {
                code = MatchCommandCode.InternalInvariantViolation;
                diagnosticCode = "match.fusion.catalog.missing";
                return false;
            }

            while (true)
            {
                var seat = GetSeat(playerId);
                var pair = seat.Units
                    .Where(unit =>
                        string.Equals(unit.TypeId, acquiredTypeId, StringComparison.Ordinal)
                        && unit.EliteLevel < catalogEntry.MaxEliteLevel
                        && IsFusionCandidateZone(seat, unit.Zone))
                    .GroupBy(unit => unit.EliteLevel)
                    .Where(group => group.Count() >= 2)
                    .OrderBy(group => group.Key)
                    .Select(group => group
                        .OrderBy(unit => ZonePriority(unit.Zone))
                        .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
                        .Take(2)
                        .ToArray())
                    .FirstOrDefault();
                if (pair == null)
                {
                    break;
                }

                var survivor = pair[0];
                var consumed = pair[1];
                if (consumed.Buffs.Count != 0)
                {
                    code = MatchCommandCode.InternalInvariantViolation;
                    diagnosticCode = "match.fusion.consumed.inlineBuff.unsupported";
                    return false;
                }

                var remappedTargetedBuffs = seat.TargetedUnitBuffs.Select(buff =>
                    string.Equals(
                        buff.TargetUnitId,
                        consumed.UnitId,
                        StringComparison.Ordinal)
                        ? buff.WithTarget(survivor.UnitId)
                        : buff).ToArray();
                int availableCost;
                MatchUnitState upgraded;
                try
                {
                    availableCost = seat.AvailableDeploymentCost;
                    if (consumed.Zone == MatchUnitZone.Deployed)
                    {
                        availableCost = checked(
                            availableCost
                            + MatchEliteRules.GetDeploymentCost(
                                catalogEntry,
                                consumed.EliteLevel));
                    }

                    upgraded = new MatchUnitState(
                        survivor.UnitId,
                        survivor.TypeId,
                        survivor.Zone,
                        checked(survivor.EliteLevel + 1),
                        survivor.Formation,
                        survivor.AcquisitionOrdinal,
                        survivor.Buffs);
                    if (survivor.Zone == MatchUnitZone.Deployed)
                    {
                        var oldCost = MatchEliteRules.GetDeploymentCost(
                            catalogEntry,
                            survivor.EliteLevel);
                        var newCost = MatchEliteRules.GetDeploymentCost(
                            catalogEntry,
                            upgraded.EliteLevel);
                        var delta = checked(newCost - oldCost);
                        if (delta <= availableCost)
                        {
                            availableCost = checked(availableCost - delta);
                        }
                        else
                        {
                            availableCost = checked(availableCost + oldCost);
                            var staging = new MatchUnitState(
                                upgraded.UnitId,
                                upgraded.TypeId,
                                MatchUnitZone.Staging,
                                upgraded.EliteLevel,
                                null,
                                upgraded.AcquisitionOrdinal,
                                upgraded.Buffs);
                            var stagingUnits = seat.Units
                                .Where(unit =>
                                    !string.Equals(unit.UnitId, survivor.UnitId, StringComparison.Ordinal)
                                    && !string.Equals(unit.UnitId, consumed.UnitId, StringComparison.Ordinal))
                                .Concat(new[] { staging })
                                .ToArray();
                            var stagingSeat = seat.With(
                                availableDeploymentCost: availableCost,
                                units: stagingUnits,
                                targetedUnitBuffs: remappedTargetedBuffs);
                            upgraded = MatchStagingProjection.CountOccupiedSlots(
                                stagingSeat,
                                source.Pool.Catalog)
                                <= MatchEconomyRules.StagingSlotCapacity
                                ? staging
                                : new MatchUnitState(
                                    staging.UnitId,
                                    staging.TypeId,
                                    MatchUnitZone.Overflow,
                                    staging.EliteLevel,
                                    null,
                                    staging.AcquisitionOrdinal,
                                    staging.Buffs);
                        }
                    }
                }
                catch (OverflowException)
                {
                    code = MatchCommandCode.InternalInvariantViolation;
                    diagnosticCode = "match.fusion.cost.overflow";
                    return false;
                }

                var changedUnits = seat.Units
                    .Where(unit =>
                        !string.Equals(unit.UnitId, survivor.UnitId, StringComparison.Ordinal)
                        && !string.Equals(unit.UnitId, consumed.UnitId, StringComparison.Ordinal))
                    .Concat(new[] { upgraded })
                    .ToArray();
                ReplaceSeat(seat.With(
                    availableDeploymentCost: availableCost,
                    units: changedUnits,
                    targetedUnitBuffs: remappedTargetedBuffs));
                if (!TryRetire(
                    playerId,
                    consumed.UnitId,
                    MatchRetirementReason.FusionConsumed,
                    out diagnosticCode))
                {
                    code = MatchCommandCode.InternalInvariantViolation;
                    return false;
                }

                survivorByConsumedId[consumed.UnitId] = survivor.UnitId;
                retiredUnitIds.Add(consumed.UnitId);
                steps.Add(new MatchFusionStep(
                    acquiredTypeId,
                    survivor.EliteLevel,
                    upgraded.EliteLevel,
                    upgraded.UnitId,
                    consumed.UnitId,
                    upgraded.Zone));
            }

            PromoteEligibleOverflow(playerId);
            var finalSurvivorUnitId = acquiredUnitId;
            while (survivorByConsumedId.TryGetValue(
                finalSurvivorUnitId,
                out var mappedSurvivorUnitId))
            {
                finalSurvivorUnitId = mappedSurvivorUnitId;
            }
            var finalUnit = GetSeat(playerId).Units.SingleOrDefault(unit =>
                string.Equals(
                    unit.UnitId,
                    finalSurvivorUnitId,
                    StringComparison.Ordinal));
            if (finalUnit == null)
            {
                code = MatchCommandCode.InternalInvariantViolation;
                diagnosticCode = "match.fusion.finalSurvivor.missing";
                return false;
            }

            acquisition = new MatchAcquisitionResult(
                acquiredUnitId,
                finalSurvivorUnitId,
                steps,
                retiredUnitIds,
                finalUnit.Zone,
                finalUnit.EliteLevel,
                goldSpent);
            code = MatchCommandCode.Accepted;
            diagnosticCode = "match.acquisition.accepted";
            return true;
        }

        private void PromoteEligibleOverflow(string playerId)
        {
            var orderedIds = GetSeat(playerId).Units
                .Where(unit => unit.Zone == MatchUnitZone.Overflow)
                .OrderBy(unit => unit.AcquisitionOrdinal)
                .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
                .Select(unit => unit.UnitId)
                .ToArray();
            foreach (var unitId in orderedIds)
            {
                var seat = GetSeat(playerId);
                var unit = seat.Units.Single(candidate =>
                    string.Equals(candidate.UnitId, unitId, StringComparison.Ordinal));
                var staging = new MatchUnitState(
                    unit.UnitId,
                    unit.TypeId,
                    MatchUnitZone.Staging,
                    unit.EliteLevel,
                    null,
                    unit.AcquisitionOrdinal,
                    unit.Buffs);
                var units = seat.Units.Select(candidate =>
                    string.Equals(candidate.UnitId, unitId, StringComparison.Ordinal)
                        ? staging
                        : candidate).ToArray();
                var prospective = seat.With(units: units);
                if (MatchStagingProjection.CountOccupiedSlots(
                    prospective,
                    source.Pool.Catalog) <= MatchEconomyRules.StagingSlotCapacity)
                {
                    ReplaceSeat(prospective);
                }
            }
        }

        private bool TryRetire(
            string playerId,
            string unitId,
            MatchRetirementReason reason,
            out string diagnosticCode)
        {
            if (retiredById.ContainsKey(unitId))
            {
                diagnosticCode = "match.retirement.duplicate";
                return false;
            }
            if (entitiesById.TryGetValue(unitId, out var entity))
            {
                if (entity.Location != MatchPoolEntityLocation.OwnedUnit
                    || !string.Equals(entity.PlayerId, playerId, StringComparison.Ordinal))
                {
                    diagnosticCode = "match.retirement.poolLocation.invalid";
                    return false;
                }
                entitiesById[unitId] = entity.WithLocation(
                    reason == MatchRetirementReason.FusionConsumed
                        ? MatchPoolEntityLocation.ConsumedByFusion
                        : MatchPoolEntityLocation.OverflowDiscarded);
            }
            retiredById.Add(
                unitId,
                new MatchRetiredPersistentUnitState(unitId, reason));
            diagnosticCode = string.Empty;
            return true;
        }

        private bool IsFusionCandidateZone(MatchSeatState seat, MatchUnitZone zone)
        {
            if (zone == MatchUnitZone.Staging || zone == MatchUnitZone.Overflow)
            {
                return true;
            }
            return zone == MatchUnitZone.Deployed
                && source.Phase == MatchPhase.Preparation
                && !seat.Ready;
        }

        private static int ZonePriority(MatchUnitZone zone)
        {
            switch (zone)
            {
                case MatchUnitZone.Deployed: return 0;
                case MatchUnitZone.Staging: return 1;
                case MatchUnitZone.Overflow: return 2;
                default: return int.MaxValue;
            }
        }

        private sealed class OverflowDiscardCandidate
        {
            internal OverflowDiscardCandidate(string playerId, MatchUnitState unit)
            {
                PlayerId = playerId;
                Unit = unit;
            }

            internal string PlayerId { get; }
            internal MatchUnitState Unit { get; }
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
                nextAcquisitionOrdinal,
                retiredUnits: retiredById.Values);
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
