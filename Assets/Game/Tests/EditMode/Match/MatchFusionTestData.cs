using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    internal static class MatchFusionTestData
    {
        internal static MatchAuthority CreateAuthority(
            MatchShopCatalog catalog = null,
            int playerOneGold = 100)
        {
            var result = MatchSessionFactory.Create(MatchTestData.Request(
                MatchTestData.FourHumanSeats(),
                shopCatalog: catalog ?? MatchTestData.Catalog()));
            Assert.That(result.Success, Is.True, result.DiagnosticCode);

            var state = result.Authority.ProjectForHostAuthority().State;
            var wealthy = state.WithEconomy(
                state.Seats.Select(seat => seat.PlayerId == "player-1"
                    ? seat.With(gold: playerOneGold)
                    : seat),
                state.Pool,
                false);
            return new MatchAuthority(wealthy, new StrictStagingSlotPolicy());
        }

        internal static MatchCommandResult PurchaseNext(
            MatchAuthority authority,
            string commandId,
            string playerId = "player-1",
            string typeId = null)
        {
            var owner = authority.ProjectForPlayer(playerId).Owner;
            var offer = owner.ShopOffers
                .Where(item => !item.IsEmpty)
                .FirstOrDefault(item => typeId == null || item.TypeId == typeId);
            if (offer == null)
            {
                var refresh = Execute(
                    authority,
                    playerId,
                    commandId + "-refresh",
                    new RefreshShopCommand());
                Assert.That(refresh.Accepted, Is.True, refresh.DiagnosticCode);
                owner = authority.ProjectForPlayer(playerId).Owner;
                offer = owner.ShopOffers
                    .Where(item => !item.IsEmpty)
                    .First(item => typeId == null || item.TypeId == typeId);
            }

            return Execute(
                authority,
                playerId,
                commandId,
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId));
        }

        internal static MatchCommandResult Execute(
            MatchAuthority authority,
            string playerId,
            string commandId,
            MatchCommandPayload payload)
        {
            return authority.Execute(new MatchCommandEnvelope(
                "session-1",
                playerId,
                commandId,
                authority.StateRevision,
                payload));
        }

        internal static MatchUnitState Unit(
            string unitId,
            string typeId,
            MatchUnitZone zone,
            int eliteLevel,
            long acquisitionOrdinal,
            MatchFormationPosition? formation = null,
            params MatchBuffState[] buffs)
        {
            return new MatchUnitState(
                unitId,
                typeId,
                zone,
                eliteLevel,
                formation,
                acquisitionOrdinal,
                buffs ?? Array.Empty<MatchBuffState>());
        }

        internal static MatchAuthority WithPlayerOneState(
            MatchAuthority authority,
            IEnumerable<MatchUnitState> units,
            int? totalDeploymentCost = null,
            IEnumerable<PlayerTargetedUnitBuffState> targetedBuffs = null,
            IEnumerable<PlayerGlobalBuffState> globalBuffs = null,
            IEnumerable<PlayerSourceEffectState> sourceEffects = null)
        {
            var state = authority.ProjectForHostAuthority().State;
            var replacementUnits = (units ?? Enumerable.Empty<MatchUnitState>())
                .OrderBy(unit => unit.UnitId, StringComparer.Ordinal)
                .ToArray();
            var total = totalDeploymentCost ?? MatchInitialValues.TotalDeploymentCost;
            var occupied = replacementUnits
                .Where(unit => unit.Zone == MatchUnitZone.Deployed)
                .Sum(unit => MatchEliteRules.GetDeploymentCost(
                    state.Pool.Catalog.Entries.Single(entry => entry.TypeId == unit.TypeId),
                    unit.EliteLevel));
            var seat = state.FindSeat("player-1").With(
                totalDeploymentCost: total,
                availableDeploymentCost: checked(total - occupied),
                units: replacementUnits,
                targetedUnitBuffs: targetedBuffs,
                globalBuffs: globalBuffs,
                sourceEffects: sourceEffects);
            var nextOrdinal = replacementUnits.Length == 0
                ? state.Pool.NextAcquisitionOrdinal
                : Math.Max(
                    state.Pool.NextAcquisitionOrdinal,
                    checked(replacementUnits.Max(unit => unit.AcquisitionOrdinal) + 1));
            var pool = state.Pool.With(
                state.Pool.Entities,
                state.Pool.RandomState,
                nextAcquisitionOrdinal: nextOrdinal);
            var changed = state.WithEconomy(
                state.Seats.Select(candidate =>
                    candidate.PlayerId == seat.PlayerId ? seat : candidate),
                pool,
                false);
            Assert.That(MatchStateInvariant.TryValidate(
                changed,
                new StrictStagingSlotPolicy(),
                out var diagnostic), Is.True, diagnostic);
            return new MatchAuthority(changed, new StrictStagingSlotPolicy());
        }

        internal static MatchShopCatalog CatalogWithFillers(
            int maxEliteLevel = 3,
            int baseDeploymentCost = 2,
            int fillerCount = 13)
        {
            var entries = new List<MatchShopCatalogEntry>
            {
                MatchTestData.Entry(
                    "1001",
                    1,
                    maxEliteLevel: maxEliteLevel,
                    baseDeploymentCost: baseDeploymentCost)
            };
            for (var index = 0; index < fillerCount; index++)
            {
                var typeId = (7000 + index).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                entries.Add(MatchTestData.Entry(
                    typeId,
                    1,
                    isShopEligible: false,
                    maxEliteLevel: 0,
                    baseDeploymentCost: index + 3));
            }
            return MatchTestData.Catalog(entries.ToArray());
        }
    }
}
