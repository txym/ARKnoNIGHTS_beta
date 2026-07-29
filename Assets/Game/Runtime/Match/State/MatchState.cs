using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ArknoNights.Match
{
    public struct MatchFormationPosition : IEquatable<MatchFormationPosition>
    {
        public MatchFormationPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
        public bool IsValid => X >= 1 && X <= 9 && Y >= 1 && Y <= 4 && !(X == 5 && Y == 1);

        public string CanonicalSummary
        {
            get
            {
                var writer = new CanonicalSummaryWriter(nameof(MatchFormationPosition));
                writer.Integer("x", X);
                writer.Integer("y", Y);
                return writer.ToString();
            }
        }

        public bool Equals(MatchFormationPosition other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is MatchFormationPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }
    }

    public sealed class MatchBuffState
    {
        public MatchBuffState(string buffId, string canonicalPayload)
        {
            BuffId = buffId;
            CanonicalPayload = canonicalPayload;
            var writer = new CanonicalSummaryWriter(nameof(MatchBuffState));
            writer.String("buffId", BuffId);
            writer.String("payload", CanonicalPayload);
            CanonicalSummary = writer.ToString();
        }

        public string BuffId { get; }
        public string CanonicalPayload { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchUnitState
    {
        public MatchUnitState(
            string unitId,
            string typeId,
            MatchUnitZone zone,
            int eliteLevel,
            MatchFormationPosition? formation,
            long acquisitionOrdinal,
            IEnumerable<MatchBuffState> buffs)
        {
            UnitId = unitId;
            TypeId = typeId;
            Zone = zone;
            EliteLevel = eliteLevel;
            Formation = formation;
            AcquisitionOrdinal = acquisitionOrdinal;
            Buffs = new ReadOnlyCollection<MatchBuffState>(
                (buffs ?? Enumerable.Empty<MatchBuffState>())
                    .OrderBy(buff => buff == null ? string.Empty : buff.BuffId, StringComparer.Ordinal)
                    .ThenBy(buff => buff == null ? string.Empty : buff.CanonicalPayload, StringComparer.Ordinal)
                    .ToArray());

            var writer = new CanonicalSummaryWriter(nameof(MatchUnitState));
            writer.String("unitId", UnitId);
            writer.String("typeId", TypeId);
            writer.EnumValue("zone", Zone);
            writer.Integer("eliteLevel", EliteLevel);
            writer.String("formation", Formation.HasValue ? Formation.Value.CanonicalSummary : string.Empty);
            writer.Integer("acquisitionOrdinal", AcquisitionOrdinal);
            foreach (var buff in Buffs)
            {
                writer.Summary("buff", buff == null ? string.Empty : buff.CanonicalSummary);
            }
            CanonicalSummary = writer.ToString();
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public MatchUnitZone Zone { get; }
        public int EliteLevel { get; }
        public MatchFormationPosition? Formation { get; }
        public long AcquisitionOrdinal { get; }
        public IReadOnlyList<MatchBuffState> Buffs { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchShopOfferState
    {
        public MatchShopOfferState(
            int slotIndex,
            string unitId,
            string typeId,
            bool isFrozen)
        {
            SlotIndex = slotIndex;
            UnitId = unitId;
            TypeId = typeId;
            IsFrozen = isFrozen;

            var writer = new CanonicalSummaryWriter(nameof(MatchShopOfferState));
            writer.Integer("slotIndex", SlotIndex);
            writer.String("unitId", UnitId);
            writer.String("typeId", TypeId);
            writer.Boolean("isFrozen", IsFrozen);
            CanonicalSummary = writer.ToString();
        }

        public int SlotIndex { get; }
        public string UnitId { get; }
        public string TypeId { get; }
        public bool IsFrozen { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchSeatState
    {
        internal MatchSeatState(
            int seatIndex,
            string playerId,
            string displayName,
            string avatarId,
            int life,
            int gold,
            int level,
            int totalDeploymentCost,
            int availableDeploymentCost,
            bool ready,
            bool eliminated,
            int? placement,
            MatchControllerKind controllerKind,
            MatchConnectionState connectionState,
            IEnumerable<MatchUnitState> units,
            IEnumerable<MatchShopOfferState> shopOffers)
        {
            SeatIndex = seatIndex;
            PlayerId = playerId;
            DisplayName = displayName;
            AvatarId = avatarId;
            Life = life;
            Gold = gold;
            Level = level;
            TotalDeploymentCost = totalDeploymentCost;
            AvailableDeploymentCost = availableDeploymentCost;
            Ready = ready;
            Eliminated = eliminated;
            Placement = placement;
            ControllerKind = controllerKind;
            ConnectionState = connectionState;
            Units = new ReadOnlyCollection<MatchUnitState>(
                (units ?? Enumerable.Empty<MatchUnitState>())
                    .OrderBy(unit => unit == null ? string.Empty : unit.UnitId, StringComparer.Ordinal)
                    .ToArray());
            ShopOffers = new ReadOnlyCollection<MatchShopOfferState>(
                (shopOffers ?? Enumerable.Empty<MatchShopOfferState>())
                    .OrderBy(offer => offer == null ? int.MinValue : offer.SlotIndex)
                    .ToArray());
            CanonicalSummary = BuildCanonicalSummary();
        }

        public int SeatIndex { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string AvatarId { get; }
        public int Life { get; }
        public int Gold { get; }
        public int Level { get; }
        public int TotalDeploymentCost { get; }
        public int AvailableDeploymentCost { get; }
        public bool Ready { get; }
        public bool Eliminated { get; }
        public int? Placement { get; }
        public MatchControllerKind ControllerKind { get; }
        public MatchConnectionState ConnectionState { get; }
        public IReadOnlyList<MatchUnitState> Units { get; }
        public IReadOnlyList<MatchShopOfferState> ShopOffers { get; }
        public string CanonicalSummary { get; }

        internal MatchSeatState With(
            bool? ready = null,
            bool? eliminated = null,
            int? placement = null,
            bool preservePlacement = true,
            MatchControllerKind? controllerKind = null,
            MatchConnectionState? connectionState = null)
        {
            return new MatchSeatState(
                SeatIndex,
                PlayerId,
                DisplayName,
                AvatarId,
                Life,
                Gold,
                Level,
                TotalDeploymentCost,
                AvailableDeploymentCost,
                ready ?? Ready,
                eliminated ?? Eliminated,
                preservePlacement ? Placement : placement,
                controllerKind ?? ControllerKind,
                connectionState ?? ConnectionState,
                Units,
                ShopOffers);
        }

        internal MatchSeatState WithElimination(int? placement)
        {
            return new MatchSeatState(
                SeatIndex,
                PlayerId,
                DisplayName,
                AvatarId,
                Life,
                Gold,
                Level,
                TotalDeploymentCost,
                AvailableDeploymentCost,
                false,
                true,
                placement,
                ControllerKind,
                MatchConnectionState.Eliminated,
                Units,
                ShopOffers);
        }

        private string BuildCanonicalSummary()
        {
            var writer = new CanonicalSummaryWriter(nameof(MatchSeatState));
            writer.Integer("seatIndex", SeatIndex);
            writer.String("playerId", PlayerId);
            writer.String("displayName", DisplayName);
            writer.String("avatarId", AvatarId);
            writer.Integer("life", Life);
            writer.Integer("gold", Gold);
            writer.Integer("level", Level);
            writer.Integer("totalCost", TotalDeploymentCost);
            writer.Integer("availableCost", AvailableDeploymentCost);
            writer.Boolean("ready", Ready);
            writer.Boolean("eliminated", Eliminated);
            writer.NullableInteger("placement", Placement);
            writer.EnumValue("controller", ControllerKind);
            writer.EnumValue("connection", ConnectionState);
            foreach (var unit in Units)
            {
                writer.Summary("unit", unit == null ? string.Empty : unit.CanonicalSummary);
            }
            foreach (var offer in ShopOffers)
            {
                writer.Summary("shop", offer == null ? string.Empty : offer.CanonicalSummary);
            }
            return writer.ToString();
        }
    }

    public sealed class MatchState
    {
        internal MatchState(
            string sessionId,
            string matchSeed,
            long stateRevision,
            MatchPhase phase,
            int roundNumber,
            string hostPlayerId,
            MatchCompatibilityManifest compatibilityManifest,
            IEnumerable<MatchSeatState> seats,
            string endReason)
        {
            SessionId = sessionId;
            MatchSeed = matchSeed;
            StateRevision = stateRevision;
            Phase = phase;
            RoundNumber = roundNumber;
            HostPlayerId = hostPlayerId;
            CompatibilityManifest = compatibilityManifest;
            Seats = new ReadOnlyCollection<MatchSeatState>(
                (seats ?? Enumerable.Empty<MatchSeatState>())
                    .OrderBy(seat => seat == null ? int.MinValue : seat.SeatIndex)
                    .ToArray());
            EndReason = endReason ?? string.Empty;
            CanonicalSummary = BuildCanonicalSummary();
        }

        public string SessionId { get; }
        public string MatchSeed { get; }
        public long StateRevision { get; }
        public MatchPhase Phase { get; }
        public int RoundNumber { get; }
        public string HostPlayerId { get; }
        public MatchCompatibilityManifest CompatibilityManifest { get; }
        public IReadOnlyList<MatchSeatState> Seats { get; }
        public string EndReason { get; }
        public string CanonicalSummary { get; }

        internal MatchSeatState FindSeat(string playerId)
        {
            return Seats.FirstOrDefault(
                seat => string.Equals(seat.PlayerId, playerId, StringComparison.Ordinal));
        }

        internal MatchState WithSeat(MatchSeatState changedSeat)
        {
            var seats = Seats.Select(
                seat => seat.SeatIndex == changedSeat.SeatIndex ? changedSeat : seat);
            return new MatchState(
                SessionId,
                MatchSeed,
                StateRevision + 1,
                Phase,
                RoundNumber,
                HostPlayerId,
                CompatibilityManifest,
                seats,
                EndReason);
        }

        internal MatchState WithSeatsAndPhase(
            IEnumerable<MatchSeatState> seats,
            MatchPhase phase,
            int roundNumber,
            string endReason = "")
        {
            return new MatchState(
                SessionId,
                MatchSeed,
                StateRevision + 1,
                phase,
                roundNumber,
                HostPlayerId,
                CompatibilityManifest,
                seats,
                endReason);
        }

        internal MatchState WithPhase(MatchPhase phase, string endReason = "")
        {
            return new MatchState(
                SessionId,
                MatchSeed,
                StateRevision + 1,
                phase,
                RoundNumber,
                HostPlayerId,
                CompatibilityManifest,
                Seats,
                endReason);
        }

        private string BuildCanonicalSummary()
        {
            var writer = new CanonicalSummaryWriter(nameof(MatchState));
            writer.String("sessionId", SessionId);
            writer.String("matchSeed", MatchSeed);
            writer.Integer("revision", StateRevision);
            writer.EnumValue("phase", Phase);
            writer.Integer("round", RoundNumber);
            writer.String("hostPlayerId", HostPlayerId);
            writer.Summary(
                "compatibility",
                CompatibilityManifest == null ? string.Empty : CompatibilityManifest.CanonicalSummary);
            writer.String("endReason", EndReason);
            foreach (var seat in Seats)
            {
                writer.Summary("seat", seat == null ? string.Empty : seat.CanonicalSummary);
            }
            return writer.ToString();
        }
    }

    internal static class MatchStateInvariant
    {
        internal static bool TryValidate(MatchState state, out string diagnosticCode)
        {
            if (state == null)
            {
                diagnosticCode = "match.invariant.state.null";
                return false;
            }
            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                diagnosticCode = "match.invariant.session.invalid";
                return false;
            }
            if (string.IsNullOrWhiteSpace(state.MatchSeed))
            {
                diagnosticCode = "match.invariant.seed.invalid";
                return false;
            }
            if (state.StateRevision < 0 || state.RoundNumber < 0)
            {
                diagnosticCode = "match.invariant.revisionOrRound.negative";
                return false;
            }
            if (!Enum.IsDefined(typeof(MatchPhase), state.Phase))
            {
                diagnosticCode = "match.invariant.phase";
                return false;
            }
            if (state.CompatibilityManifest == null || !state.CompatibilityManifest.IsValid)
            {
                diagnosticCode = "match.invariant.compatibility.invalid";
                return false;
            }
            if (state.Seats.Count != 4)
            {
                diagnosticCode = "match.invariant.seat.count";
                return false;
            }

            var expectedSeatIndex = 1;
            var playerIds = new HashSet<string>(StringComparer.Ordinal);
            var unitIds = new HashSet<string>(StringComparer.Ordinal);
            var shopUnitIds = new HashSet<string>(StringComparer.Ordinal);
            MatchSeatState host = null;
            foreach (var seat in state.Seats)
            {
                if (seat == null || seat.SeatIndex != expectedSeatIndex++)
                {
                    diagnosticCode = "match.invariant.seat.index";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(seat.PlayerId) || !playerIds.Add(seat.PlayerId))
                {
                    diagnosticCode = "match.invariant.playerId";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(seat.DisplayName) || string.IsNullOrWhiteSpace(seat.AvatarId))
                {
                    diagnosticCode = "match.invariant.identity";
                    return false;
                }
                if (!Enum.IsDefined(typeof(MatchControllerKind), seat.ControllerKind)
                    || !Enum.IsDefined(typeof(MatchConnectionState), seat.ConnectionState))
                {
                    diagnosticCode = "match.invariant.controllerOrConnection";
                    return false;
                }
                if (seat.Life < 0
                    || seat.Gold < 0
                    || seat.Level < 0
                    || seat.TotalDeploymentCost < 0
                    || seat.AvailableDeploymentCost < 0
                    || seat.AvailableDeploymentCost > seat.TotalDeploymentCost)
                {
                    diagnosticCode = "match.invariant.numeric";
                    return false;
                }
                if (seat.Ready
                    && (seat.ControllerKind != MatchControllerKind.Human
                        || seat.ConnectionState != MatchConnectionState.Connected
                        || seat.Eliminated))
                {
                    diagnosticCode = "match.invariant.ready";
                    return false;
                }
                if (seat.Eliminated && seat.ConnectionState != MatchConnectionState.Eliminated)
                {
                    diagnosticCode = "match.invariant.elimination.connection";
                    return false;
                }
                if (!seat.Eliminated && seat.ConnectionState == MatchConnectionState.Eliminated)
                {
                    diagnosticCode = "match.invariant.connection.elimination";
                    return false;
                }
                if (seat.Placement.HasValue
                    && (seat.Placement.Value < 1 || seat.Placement.Value > 4))
                {
                    diagnosticCode = "match.invariant.placement";
                    return false;
                }
                if (string.Equals(seat.PlayerId, state.HostPlayerId, StringComparison.Ordinal))
                {
                    host = seat;
                }

                var deployedPositions = new HashSet<MatchFormationPosition>();
                foreach (var unit in seat.Units)
                {
                    if (unit == null
                        || string.IsNullOrWhiteSpace(unit.UnitId)
                        || string.IsNullOrWhiteSpace(unit.TypeId)
                        || !Enum.IsDefined(typeof(MatchUnitZone), unit.Zone)
                        || unit.EliteLevel < 0
                        || unit.AcquisitionOrdinal < 0
                        || !unitIds.Add(unit.UnitId))
                    {
                        diagnosticCode = "match.invariant.unit";
                        return false;
                    }
                    if (unit.Zone == MatchUnitZone.Deployed)
                    {
                        if (!unit.Formation.HasValue
                            || !unit.Formation.Value.IsValid
                            || !deployedPositions.Add(unit.Formation.Value))
                        {
                            diagnosticCode = "match.invariant.deployedFormation";
                            return false;
                        }
                    }
                    else if (unit.Formation.HasValue)
                    {
                        diagnosticCode = "match.invariant.nonDeployedFormation";
                        return false;
                    }
                    if (unit.Buffs.Any(
                        buff => buff == null
                            || string.IsNullOrWhiteSpace(buff.BuffId)
                            || buff.CanonicalPayload == null))
                    {
                        diagnosticCode = "match.invariant.buff";
                        return false;
                    }
                }

                var slotIndexes = new HashSet<int>();
                foreach (var offer in seat.ShopOffers)
                {
                    var hasUnitId = offer != null && !string.IsNullOrWhiteSpace(offer.UnitId);
                    var hasTypeId = offer != null && !string.IsNullOrWhiteSpace(offer.TypeId);
                    if (offer == null
                        || offer.SlotIndex < 0
                        || !slotIndexes.Add(offer.SlotIndex)
                        || hasUnitId != hasTypeId
                        || (!hasUnitId && offer.IsFrozen)
                        || (hasUnitId && (!shopUnitIds.Add(offer.UnitId) || unitIds.Contains(offer.UnitId))))
                    {
                        diagnosticCode = "match.invariant.shop";
                        return false;
                    }
                }
            }

            if (host == null)
            {
                diagnosticCode = "match.invariant.host.missing";
                return false;
            }
            if (host.ControllerKind != MatchControllerKind.Human)
            {
                diagnosticCode = "match.invariant.host.controller";
                return false;
            }
            if (unitIds.Overlaps(shopUnitIds))
            {
                diagnosticCode = "match.invariant.shop.unitIdOwned";
                return false;
            }
            if (state.Phase == MatchPhase.Ended && string.IsNullOrWhiteSpace(state.EndReason))
            {
                diagnosticCode = "match.invariant.endReason";
                return false;
            }

            diagnosticCode = string.Empty;
            return true;
        }
    }
}
