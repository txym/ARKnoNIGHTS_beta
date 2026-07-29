using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    public static class MatchSessionFactory
    {
        public static MatchInitializationResult Create(MatchInitializationRequest request)
        {
            return Create(request, new StrictStagingSlotPolicy());
        }

        public static MatchInitializationResult Create(
            MatchInitializationRequest request,
            IStagingSlotPolicy stagingSlotPolicy)
        {
            if (request == null)
            {
                return Rejected(MatchInitializationCode.InvalidRequest, "match.initialize.request.null");
            }
            if (stagingSlotPolicy == null)
            {
                return Rejected(
                    MatchInitializationCode.InvalidRequest,
                    "match.initialize.stagingSlotPolicy.null");
            }
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                return Rejected(MatchInitializationCode.InvalidSessionId, "match.initialize.session.invalid");
            }
            if (string.IsNullOrWhiteSpace(request.MatchSeed))
            {
                return Rejected(MatchInitializationCode.InvalidMatchSeed, "match.initialize.seed.invalid");
            }
            if (request.CompatibilityManifest == null || !request.CompatibilityManifest.IsValid)
            {
                return Rejected(
                    MatchInitializationCode.InvalidCompatibilityManifest,
                    "match.initialize.compatibility.invalid");
            }
            if (request.ShopCatalog == null)
            {
                return Rejected(
                    MatchInitializationCode.InvalidShopCatalog,
                    "match.initialize.shopCatalog.invalid");
            }
            if (!request.ShopCatalog.TryValidate(out _, out var catalogDiagnostic))
            {
                return Rejected(
                    MatchInitializationCode.InvalidShopCatalog,
                    catalogDiagnostic);
            }
            if (!request.ShopCatalog.IsCompatibleWith(request.CompatibilityManifest))
            {
                return Rejected(
                    MatchInitializationCode.ShopCatalogCompatibilityMismatch,
                    "match.initialize.shopCatalog.compatibilityMismatch");
            }
            if (request.InitialPlayerValues == null
                || !request.InitialPlayerValues.IsConfirmedStandard)
            {
                return Rejected(
                    MatchInitializationCode.InvalidInitialValues,
                    "match.initialize.initialValues.invalid");
            }
            if (request.Seats == null || request.Seats.Count != 4)
            {
                return Rejected(MatchInitializationCode.InvalidSeatCount, "match.initialize.seats.count");
            }
            if (request.Seats.Any(seat => seat == null || seat.SeatIndex < 1 || seat.SeatIndex > 4))
            {
                return Rejected(MatchInitializationCode.InvalidSeatIndex, "match.initialize.seat.index");
            }
            if (request.Seats.GroupBy(seat => seat.SeatIndex).Any(group => group.Count() != 1))
            {
                return Rejected(
                    MatchInitializationCode.DuplicateSeatIndex,
                    "match.initialize.seat.index.duplicate");
            }
            if (request.Seats.Any(seat => string.IsNullOrWhiteSpace(seat.PlayerId)))
            {
                return Rejected(MatchInitializationCode.InvalidPlayerId, "match.initialize.playerId.invalid");
            }
            if (request.Seats
                .GroupBy(seat => seat.PlayerId, StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
            {
                return Rejected(
                    MatchInitializationCode.DuplicatePlayerId,
                    "match.initialize.playerId.duplicate");
            }
            if (request.Seats.Any(seat => string.IsNullOrWhiteSpace(seat.DisplayName)))
            {
                return Rejected(
                    MatchInitializationCode.InvalidDisplayName,
                    "match.initialize.displayName.invalid");
            }
            if (request.Seats.Any(seat => string.IsNullOrWhiteSpace(seat.AvatarId)))
            {
                return Rejected(MatchInitializationCode.InvalidAvatarId, "match.initialize.avatarId.invalid");
            }
            if (request.Seats.Any(
                seat => seat.InitialControllerKind != MatchControllerKind.Human
                    && seat.InitialControllerKind != MatchControllerKind.NativeBot))
            {
                return Rejected(
                    MatchInitializationCode.InvalidController,
                    "match.initialize.controller.invalid");
            }

            var host = request.Seats.SingleOrDefault(
                seat => string.Equals(seat.PlayerId, request.HostPlayerId, StringComparison.Ordinal));
            if (host == null)
            {
                return Rejected(MatchInitializationCode.HostMissing, "match.initialize.host.missing");
            }
            if (host.InitialControllerKind != MatchControllerKind.Human)
            {
                return Rejected(MatchInitializationCode.HostNotHuman, "match.initialize.host.notHuman");
            }

            var seats = request.Seats
                .OrderBy(seat => seat.SeatIndex)
                .Select(seat => CreateSeat(seat, request.InitialPlayerValues))
                .ToArray();
            var pool = MatchPoolState.CreateInitial(
                request.SessionId,
                request.MatchSeed,
                request.ShopCatalog);
            var state = new MatchState(
                request.SessionId,
                request.MatchSeed,
                0,
                MatchPhase.Initializing,
                0,
                request.HostPlayerId,
                request.CompatibilityManifest,
                seats,
                pool,
                string.Empty);
            var initializedShop = MatchInitialShopBuilder.Apply(state.Seats, state.Pool);
            state = state.WithEconomy(
                initializedShop.Seats,
                initializedShop.Pool,
                false);
            if (!MatchStateInvariant.TryValidate(state, stagingSlotPolicy, out var diagnosticCode))
            {
                return Rejected(MatchInitializationCode.InternalInvariantViolation, diagnosticCode);
            }

            return new MatchInitializationResult(
                MatchInitializationCode.Accepted,
                "match.initialize.accepted",
                new MatchAuthority(state, stagingSlotPolicy),
                initializedShop.PoolExhausted);
        }

        private static MatchSeatState CreateSeat(
            MatchSeatInitialization initialization,
            MatchInitialPlayerValues initialValues)
        {
            return new MatchSeatState(
                initialization.SeatIndex,
                initialization.PlayerId,
                initialization.DisplayName,
                initialization.AvatarId,
                initialValues.Life,
                initialValues.Gold,
                initialValues.Level,
                initialValues.TotalDeploymentCost,
                initialValues.AvailableDeploymentCost,
                0,
                MatchPreparationBehaviorState.Empty,
                false,
                false,
                null,
                initialization.InitialControllerKind,
                MatchConnectionState.Connected,
                Array.Empty<MatchUnitState>(),
                Enumerable.Range(1, MatchEconomyRules.ShopSlotCount)
                    .Select(slotIndex => new MatchShopOfferState(
                        slotIndex,
                        string.Empty,
                        string.Empty,
                        null,
                        false)));
        }

        private static MatchInitializationResult Rejected(
            MatchInitializationCode code,
            string diagnosticCode)
        {
            return new MatchInitializationResult(code, diagnosticCode, null);
        }
    }
}
