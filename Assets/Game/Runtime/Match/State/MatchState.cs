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
            : this(slotIndex, unitId, typeId, null, isFrozen)
        {
        }

        public MatchShopOfferState(
            int slotIndex,
            string unitId,
            string typeId,
            int? rarity,
            bool isFrozen)
        {
            SlotIndex = slotIndex;
            UnitId = unitId;
            TypeId = typeId;
            Rarity = rarity;
            IsFrozen = isFrozen;

            var writer = new CanonicalSummaryWriter(nameof(MatchShopOfferState));
            writer.Integer("slotIndex", SlotIndex);
            writer.String("unitId", UnitId);
            writer.String("typeId", TypeId);
            writer.NullableInteger("rarity", Rarity);
            writer.Boolean("isFrozen", IsFrozen);
            CanonicalSummary = writer.ToString();
        }

        public int SlotIndex { get; }
        public string UnitId { get; }
        public string TypeId { get; }
        public int? Rarity { get; }
        public int? Price => Rarity;
        public bool IsFrozen { get; }
        public bool IsEmpty => string.IsNullOrEmpty(UnitId);
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
            int upgradeDiscountCountAtThisLevel,
            MatchPreparationBehaviorState preparationBehavior,
            bool ready,
            bool eliminated,
            int? placement,
            MatchControllerKind controllerKind,
            MatchConnectionState connectionState,
            IEnumerable<MatchUnitState> units,
            IEnumerable<MatchShopOfferState> shopOffers)
            : this(
                seatIndex,
                playerId,
                displayName,
                avatarId,
                life,
                gold,
                level,
                totalDeploymentCost,
                availableDeploymentCost,
                upgradeDiscountCountAtThisLevel,
                preparationBehavior,
                ready,
                eliminated,
                placement,
                controllerKind,
                connectionState,
                units,
                shopOffers,
                Array.Empty<PlayerTargetedUnitBuffState>(),
                Array.Empty<PlayerGlobalBuffState>(),
                Array.Empty<PlayerSourceEffectState>())
        {
        }

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
            int upgradeDiscountCountAtThisLevel,
            MatchPreparationBehaviorState preparationBehavior,
            bool ready,
            bool eliminated,
            int? placement,
            MatchControllerKind controllerKind,
            MatchConnectionState connectionState,
            IEnumerable<MatchUnitState> units,
            IEnumerable<MatchShopOfferState> shopOffers,
            IEnumerable<PlayerTargetedUnitBuffState> targetedUnitBuffs,
            IEnumerable<PlayerGlobalBuffState> globalBuffs,
            IEnumerable<PlayerSourceEffectState> sourceEffects,
            MatchStreakKind streakKind = MatchStreakKind.None,
            int streakCount = 0)
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
            UpgradeDiscountCountAtThisLevel = upgradeDiscountCountAtThisLevel;
            PreparationBehavior = preparationBehavior ?? MatchPreparationBehaviorState.Empty;
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
            TargetedUnitBuffs = new ReadOnlyCollection<PlayerTargetedUnitBuffState>(
                (targetedUnitBuffs ?? Enumerable.Empty<PlayerTargetedUnitBuffState>())
                    .OrderBy(buff => buff == null ? string.Empty : buff.BuffInstanceId, StringComparer.Ordinal)
                    .ToArray());
            GlobalBuffs = new ReadOnlyCollection<PlayerGlobalBuffState>(
                (globalBuffs ?? Enumerable.Empty<PlayerGlobalBuffState>())
                    .OrderBy(buff => buff == null ? string.Empty : buff.BuffInstanceId, StringComparer.Ordinal)
                    .ToArray());
            SourceEffects = new ReadOnlyCollection<PlayerSourceEffectState>(
                (sourceEffects ?? Enumerable.Empty<PlayerSourceEffectState>())
                    .OrderBy(effect => effect == null ? string.Empty : effect.EffectInstanceId, StringComparer.Ordinal)
                    .ToArray());
            StreakKind = streakKind;
            StreakCount = streakCount;
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
        public int UpgradeDiscountCountAtThisLevel { get; }
        public int CurrentUpgradePrice =>
            MatchUpgradePricing.GetCurrentPrice(Level, UpgradeDiscountCountAtThisLevel);
        public MatchPreparationBehaviorState PreparationBehavior { get; }
        public bool Ready { get; }
        public bool Eliminated { get; }
        public int? Placement { get; }
        public MatchControllerKind ControllerKind { get; }
        public MatchConnectionState ConnectionState { get; }
        public IReadOnlyList<MatchUnitState> Units { get; }
        public IReadOnlyList<MatchShopOfferState> ShopOffers { get; }
        public IReadOnlyList<PlayerTargetedUnitBuffState> TargetedUnitBuffs { get; }
        public IReadOnlyList<PlayerGlobalBuffState> GlobalBuffs { get; }
        public IReadOnlyList<PlayerSourceEffectState> SourceEffects { get; }
        public MatchStreakKind StreakKind { get; }
        public int StreakCount { get; }
        public string CanonicalSummary { get; }

        internal MatchSeatState With(
            int? life = null,
            bool? ready = null,
            bool? eliminated = null,
            int? placement = null,
            bool preservePlacement = true,
            MatchControllerKind? controllerKind = null,
            MatchConnectionState? connectionState = null,
            int? gold = null,
            int? level = null,
            int? totalDeploymentCost = null,
            int? availableDeploymentCost = null,
            int? upgradeDiscountCountAtThisLevel = null,
            MatchPreparationBehaviorState preparationBehavior = null,
            IEnumerable<MatchUnitState> units = null,
            IEnumerable<MatchShopOfferState> shopOffers = null,
            IEnumerable<PlayerTargetedUnitBuffState> targetedUnitBuffs = null,
            IEnumerable<PlayerGlobalBuffState> globalBuffs = null,
            IEnumerable<PlayerSourceEffectState> sourceEffects = null,
            MatchStreakKind? streakKind = null,
            int? streakCount = null)
        {
            return new MatchSeatState(
                SeatIndex,
                PlayerId,
                DisplayName,
                AvatarId,
                life ?? Life,
                gold ?? Gold,
                level ?? Level,
                totalDeploymentCost ?? TotalDeploymentCost,
                availableDeploymentCost ?? AvailableDeploymentCost,
                upgradeDiscountCountAtThisLevel ?? UpgradeDiscountCountAtThisLevel,
                preparationBehavior ?? PreparationBehavior,
                ready ?? Ready,
                eliminated ?? Eliminated,
                preservePlacement ? Placement : placement,
                controllerKind ?? ControllerKind,
                connectionState ?? ConnectionState,
                units ?? Units,
                shopOffers ?? ShopOffers,
                targetedUnitBuffs ?? TargetedUnitBuffs,
                globalBuffs ?? GlobalBuffs,
                sourceEffects ?? SourceEffects,
                streakKind ?? StreakKind,
                streakCount ?? StreakCount);
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
                UpgradeDiscountCountAtThisLevel,
                PreparationBehavior,
                false,
                true,
                placement,
                ControllerKind,
                MatchConnectionState.Eliminated,
                Units,
                ShopOffers,
                TargetedUnitBuffs,
                GlobalBuffs,
                SourceEffects,
                StreakKind,
                StreakCount);
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
            writer.Integer("upgradeDiscount", UpgradeDiscountCountAtThisLevel);
            writer.Summary("preparationBehavior", PreparationBehavior.CanonicalSummary);
            writer.Boolean("ready", Ready);
            writer.Boolean("eliminated", Eliminated);
            writer.NullableInteger("placement", Placement);
            writer.EnumValue("controller", ControllerKind);
            writer.EnumValue("connection", ConnectionState);
            writer.EnumValue("streakKind", StreakKind);
            writer.Integer("streakCount", StreakCount);
            foreach (var unit in Units)
            {
                writer.Summary("unit", unit == null ? string.Empty : unit.CanonicalSummary);
            }
            foreach (var offer in ShopOffers)
            {
                writer.Summary("shop", offer == null ? string.Empty : offer.CanonicalSummary);
            }
            foreach (var buff in TargetedUnitBuffs)
            {
                writer.Summary("targetedUnitBuff", buff == null ? string.Empty : buff.CanonicalSummary);
            }
            foreach (var buff in GlobalBuffs)
            {
                writer.Summary("globalBuff", buff == null ? string.Empty : buff.CanonicalSummary);
            }
            foreach (var effect in SourceEffects)
            {
                writer.Summary("sourceEffect", effect == null ? string.Empty : effect.CanonicalSummary);
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
            MatchPoolState pool,
            string endReason,
            MatchFlowState flow = null)
        {
            SessionId = sessionId;
            MatchSeed = matchSeed;
            StateRevision = stateRevision;
            Phase = phase;
            RoundNumber = roundNumber;
            HostPlayerId = hostPlayerId;
            CompatibilityManifest = compatibilityManifest;
            Pool = pool;
            Seats = new ReadOnlyCollection<MatchSeatState>(
                (seats ?? Enumerable.Empty<MatchSeatState>())
                    .OrderBy(seat => seat == null ? int.MinValue : seat.SeatIndex)
                    .ToArray());
            EndReason = endReason ?? string.Empty;
            Flow = flow ?? MatchFlowState.Empty;
            CanonicalSummary = BuildCanonicalSummary();
        }

        public string SessionId { get; }
        public string MatchSeed { get; }
        public long StateRevision { get; }
        public MatchPhase Phase { get; }
        public int RoundNumber { get; }
        public string HostPlayerId { get; }
        public MatchCompatibilityManifest CompatibilityManifest { get; }
        internal MatchPoolState Pool { get; }
        public IReadOnlyList<MatchSeatState> Seats { get; }
        public string EndReason { get; }
        public MatchFlowState Flow { get; }
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
                Pool,
                EndReason,
                Flow);
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
                Pool,
                endReason,
                Flow);
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
                Pool,
                endReason,
                Flow);
        }

        internal MatchState WithEconomy(
            IEnumerable<MatchSeatState> seats,
            MatchPoolState pool,
            bool incrementRevision)
        {
            return new MatchState(
                SessionId,
                MatchSeed,
                incrementRevision ? StateRevision + 1 : StateRevision,
                Phase,
                RoundNumber,
                HostPlayerId,
                CompatibilityManifest,
                seats,
                pool,
                EndReason,
                Flow);
        }

        internal MatchState Rebuild(
            long stateRevision,
            MatchPhase phase,
            int roundNumber,
            IEnumerable<MatchSeatState> seats,
            MatchPoolState pool,
            string endReason,
            MatchFlowState flow)
        {
            return new MatchState(
                SessionId,
                MatchSeed,
                stateRevision,
                phase,
                roundNumber,
                HostPlayerId,
                CompatibilityManifest,
                seats,
                pool,
                endReason,
                flow);
        }

        internal MatchState WithFlow(MatchFlowState flow, bool incrementRevision)
        {
            return Rebuild(
                incrementRevision ? StateRevision + 1 : StateRevision,
                Phase,
                RoundNumber,
                Seats,
                Pool,
                EndReason,
                flow);
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
            writer.Summary("pool", Pool == null ? string.Empty : Pool.CanonicalSummary);
            writer.String("endReason", EndReason);
            writer.Summary("flow", Flow == null ? string.Empty : Flow.CanonicalSummary);
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
            return TryValidate(state, new StrictStagingSlotPolicy(), out diagnosticCode);
        }

        internal static bool TryValidate(
            MatchState state,
            IStagingSlotPolicy stagingSlotPolicy,
            out string diagnosticCode)
        {
            if (stagingSlotPolicy == null)
            {
                diagnosticCode = "match.invariant.stagingSlotPolicy.null";
                return false;
            }
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
            if (state.Pool == null
                || state.Pool.Catalog == null
                || !state.Pool.Catalog.TryValidate(out _, out _)
                || !state.Pool.Catalog.IsCompatibleWith(state.CompatibilityManifest)
                || state.Pool.RandomState == null
                || !state.Pool.RandomState.IsValid
                || state.Pool.NextNaturalRefreshStartSeat < 1
                || state.Pool.NextNaturalRefreshStartSeat > 4
                || state.Pool.NextAcquisitionOrdinal < 0
                || state.Pool.AppliedPostBattleRefreshRounds.Any(round => round <= 0))
            {
                diagnosticCode = "match.invariant.pool.header";
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
            var unitOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            var ownedTypeIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var shopLocations = new Dictionary<string, string>(StringComparer.Ordinal);
            var shopTypeIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var acquisitionOrdinals = new HashSet<long>();
            var retiredById = new Dictionary<string, MatchRetirementReason>(StringComparer.Ordinal);
            foreach (var retired in state.Pool.RetiredUnits)
            {
                if (retired == null
                    || string.IsNullOrWhiteSpace(retired.UnitId)
                    || !Enum.IsDefined(typeof(MatchRetirementReason), retired.Reason)
                    || retiredById.ContainsKey(retired.UnitId))
                {
                    diagnosticCode = "match.invariant.retired";
                    return false;
                }
                retiredById.Add(retired.UnitId, retired.Reason);
            }
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
                if (seat.Gold < 0
                    || seat.Level < MatchEconomyRules.MinimumLevel
                    || seat.Level > MatchEconomyRules.MaximumLevel
                    || seat.TotalDeploymentCost < 0
                    || seat.AvailableDeploymentCost < 0
                    || seat.AvailableDeploymentCost > seat.TotalDeploymentCost)
                {
                    diagnosticCode = "match.invariant.numeric";
                    return false;
                }
                if (!Enum.IsDefined(typeof(MatchStreakKind), seat.StreakKind)
                    || seat.StreakCount < 0
                    || (seat.StreakKind == MatchStreakKind.None && seat.StreakCount != 0)
                    || (seat.StreakKind != MatchStreakKind.None && seat.StreakCount == 0))
                {
                    diagnosticCode = "match.invariant.streak";
                    return false;
                }
                var maximumDiscount = seat.Level == MatchEconomyRules.MaximumLevel
                    ? 0
                    : MatchUpgradePricing.GetBasePrice(seat.Level);
                if (seat.UpgradeDiscountCountAtThisLevel < 0
                    || seat.UpgradeDiscountCountAtThisLevel > maximumDiscount
                    || seat.PreparationBehavior == null
                    || seat.PreparationBehavior.SuccessfulShopPurchaseCount < 0)
                {
                    diagnosticCode = "match.invariant.economy";
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
                var deployedCost = 0;
                foreach (var unit in seat.Units)
                {
                    if (unit == null
                        || string.IsNullOrWhiteSpace(unit.UnitId)
                        || string.IsNullOrWhiteSpace(unit.TypeId)
                        || !Enum.IsDefined(typeof(MatchUnitZone), unit.Zone)
                        || unit.EliteLevel < 0
                        || unit.EliteLevel > 3
                        || unit.AcquisitionOrdinal < 0
                        || !unitIds.Add(unit.UnitId)
                        || !acquisitionOrdinals.Add(unit.AcquisitionOrdinal)
                        || unitOwners.ContainsKey(unit.UnitId)
                        || retiredById.ContainsKey(unit.UnitId)
                        || !state.Pool.Catalog.TryGet(unit.TypeId, out var unitCatalogEntry)
                        || unit.EliteLevel > unitCatalogEntry.MaxEliteLevel)
                    {
                        diagnosticCode = "match.invariant.unit";
                        return false;
                    }
                    unitOwners.Add(unit.UnitId, seat.PlayerId);
                    ownedTypeIds.Add(unit.UnitId, unit.TypeId);
                    if (unit.Zone == MatchUnitZone.Deployed)
                    {
                        if (!unit.Formation.HasValue
                            || !unit.Formation.Value.IsValid
                            || !deployedPositions.Add(unit.Formation.Value))
                        {
                            diagnosticCode = "match.invariant.deployedFormation";
                            return false;
                        }
                        try
                        {
                            deployedCost = checked(
                                deployedCost
                                + MatchEliteRules.GetDeploymentCost(
                                    unitCatalogEntry,
                                    unit.EliteLevel));
                        }
                        catch (OverflowException)
                        {
                            diagnosticCode = "match.invariant.deployedCost.overflow";
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
                if (seat.AvailableDeploymentCost
                    != seat.TotalDeploymentCost - deployedCost)
                {
                    diagnosticCode = "match.invariant.deploymentCost.balance";
                    return false;
                }

                var ownedUnitIds = new HashSet<string>(
                    seat.Units.Select(unit => unit.UnitId),
                    StringComparer.Ordinal);
                var targetedBuffIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var buff in seat.TargetedUnitBuffs)
                {
                    if (buff == null
                        || !buff.IsValid
                        || !targetedBuffIds.Add(buff.BuffInstanceId)
                        || !ownedUnitIds.Contains(buff.TargetUnitId))
                    {
                        diagnosticCode = "match.invariant.targetedBuff";
                        return false;
                    }
                }
                var globalBuffIds = new HashSet<string>(StringComparer.Ordinal);
                if (seat.GlobalBuffs.Any(buff =>
                    buff == null
                    || !buff.IsValid
                    || !globalBuffIds.Add(buff.BuffInstanceId)))
                {
                    diagnosticCode = "match.invariant.globalBuff";
                    return false;
                }
                var sourceEffectIds = new HashSet<string>(StringComparer.Ordinal);
                if (seat.SourceEffects.Any(effect =>
                    effect == null
                    || !effect.IsValid
                    || !sourceEffectIds.Add(effect.EffectInstanceId)))
                {
                    diagnosticCode = "match.invariant.sourceEffect";
                    return false;
                }

                if (seat.ShopOffers.Count != MatchEconomyRules.ShopSlotCount)
                {
                    diagnosticCode = "match.invariant.shop.count";
                    return false;
                }
                var stagingSlotUsage = MatchStagingProjection.CountOccupiedSlots(
                    seat,
                    state.Pool.Catalog);
                if (stagingSlotUsage < 0
                    || stagingSlotUsage > MatchEconomyRules.StagingSlotCapacity)
                {
                    diagnosticCode = "match.invariant.staging.capacity";
                    return false;
                }
                var slotIndexes = new HashSet<int>();
                foreach (var offer in seat.ShopOffers)
                {
                    var hasUnitId = offer != null && !string.IsNullOrWhiteSpace(offer.UnitId);
                    var hasTypeId = offer != null && !string.IsNullOrWhiteSpace(offer.TypeId);
                    var hasRarity = offer != null && offer.Rarity.HasValue;
                    if (offer == null
                        || offer.SlotIndex < 1
                        || offer.SlotIndex > MatchEconomyRules.ShopSlotCount
                        || !slotIndexes.Add(offer.SlotIndex)
                        || hasUnitId != hasTypeId
                        || hasUnitId != hasRarity
                        || (!hasUnitId && offer.IsFrozen)
                        || (hasUnitId && (!shopUnitIds.Add(offer.UnitId)
                            || unitIds.Contains(offer.UnitId)
                            || shopLocations.ContainsKey(offer.UnitId))))
                    {
                        diagnosticCode = "match.invariant.shop";
                        return false;
                    }
                    if (hasUnitId)
                    {
                        shopLocations.Add(
                            offer.UnitId,
                            seat.PlayerId + ":" + offer.SlotIndex.ToString(CultureInfo.InvariantCulture));
                        shopTypeIds.Add(offer.UnitId, offer.TypeId);
                    }
                    if (hasUnitId
                        && (!state.Pool.Catalog.TryGet(offer.TypeId, out var catalogEntry)
                            || !catalogEntry.IsShopEligible
                            || catalogEntry.Rarity != offer.Rarity.Value))
                    {
                        diagnosticCode = "match.invariant.shop.catalog";
                        return false;
                    }
                }
                if (!slotIndexes.SetEquals(Enumerable.Range(1, MatchEconomyRules.ShopSlotCount)))
                {
                    diagnosticCode = "match.invariant.shop.slotSet";
                    return false;
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
            if (unitIds.Overlaps(retiredById.Keys)
                || shopUnitIds.Overlaps(retiredById.Keys))
            {
                diagnosticCode = "match.invariant.retired.current";
                return false;
            }
            if (acquisitionOrdinals.Count > 0
                && state.Pool.NextAcquisitionOrdinal <= acquisitionOrdinals.Max())
            {
                diagnosticCode = "match.invariant.acquisitionOrdinal.next";
                return false;
            }
            var poolIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in state.Pool.Entities)
            {
                if (entity == null
                    || !MatchPoolUnitId.IsValid(entity.UnitId)
                    || !poolIds.Add(entity.UnitId)
                    || !Enum.IsDefined(typeof(MatchPoolEntityLocation), entity.Location)
                    || !state.Pool.Catalog.TryGet(entity.TypeId, out var entry)
                    || !entry.IsShopEligible
                    || entry.Rarity != entity.Rarity
                    || entity.CopyIndex < 1
                    || entity.CopyIndex > MatchPoolCopyCounts.GetForRarity(entity.Rarity))
                {
                    diagnosticCode = "match.invariant.pool.entity";
                    return false;
                }

                switch (entity.Location)
                {
                    case MatchPoolEntityLocation.AvailablePool:
                        if (!string.IsNullOrEmpty(entity.PlayerId)
                            || entity.ShopSlotIndex.HasValue
                            || unitIds.Contains(entity.UnitId)
                            || shopUnitIds.Contains(entity.UnitId)
                            || retiredById.ContainsKey(entity.UnitId))
                        {
                            diagnosticCode = "match.invariant.pool.availableLocation";
                            return false;
                        }
                        break;
                    case MatchPoolEntityLocation.ShopOffer:
                        var expectedShopLocation = entity.PlayerId + ":"
                            + (entity.ShopSlotIndex.HasValue
                                ? entity.ShopSlotIndex.Value.ToString(CultureInfo.InvariantCulture)
                                : string.Empty);
                        if (!entity.ShopSlotIndex.HasValue
                            || !shopLocations.TryGetValue(entity.UnitId, out var actualShopLocation)
                            || !string.Equals(expectedShopLocation, actualShopLocation, StringComparison.Ordinal)
                            || !shopTypeIds.TryGetValue(entity.UnitId, out var shopTypeId)
                            || !string.Equals(entity.TypeId, shopTypeId, StringComparison.Ordinal))
                        {
                            diagnosticCode = "match.invariant.pool.shopLocation";
                            return false;
                        }
                        break;
                    case MatchPoolEntityLocation.OwnedUnit:
                        if (entity.ShopSlotIndex.HasValue
                            || !unitOwners.TryGetValue(entity.UnitId, out var ownerPlayerId)
                            || !string.Equals(entity.PlayerId, ownerPlayerId, StringComparison.Ordinal)
                            || !ownedTypeIds.TryGetValue(entity.UnitId, out var ownedTypeId)
                            || !string.Equals(entity.TypeId, ownedTypeId, StringComparison.Ordinal))
                        {
                            diagnosticCode = "match.invariant.pool.ownedLocation";
                            return false;
                        }
                        break;
                    case MatchPoolEntityLocation.ConsumedByFusion:
                        if (!string.IsNullOrEmpty(entity.PlayerId)
                            || entity.ShopSlotIndex.HasValue
                            || unitIds.Contains(entity.UnitId)
                            || shopUnitIds.Contains(entity.UnitId)
                            || !retiredById.TryGetValue(
                                entity.UnitId,
                                out var fusionRetirementReason)
                            || fusionRetirementReason != MatchRetirementReason.FusionConsumed)
                        {
                            diagnosticCode = "match.invariant.pool.fusionConsumedLocation";
                            return false;
                        }
                        break;
                    case MatchPoolEntityLocation.OverflowDiscarded:
                        if (!string.IsNullOrEmpty(entity.PlayerId)
                            || entity.ShopSlotIndex.HasValue
                            || unitIds.Contains(entity.UnitId)
                            || shopUnitIds.Contains(entity.UnitId)
                            || !retiredById.TryGetValue(
                                entity.UnitId,
                                out var overflowRetirementReason)
                            || overflowRetirementReason != MatchRetirementReason.OverflowDiscarded)
                        {
                            diagnosticCode = "match.invariant.pool.overflowDiscardedLocation";
                            return false;
                        }
                        break;
                }
            }
            if (!shopUnitIds.IsSubsetOf(poolIds)
                || unitIds.Any(unitId =>
                    MatchPoolUnitId.IsValid(unitId) && !poolIds.Contains(unitId))
                || retiredById.Keys.Any(unitId =>
                    MatchPoolUnitId.IsValid(unitId) && !poolIds.Contains(unitId)))
            {
                diagnosticCode = "match.invariant.pool.conservation";
                return false;
            }
            foreach (var entry in state.Pool.Catalog.Entries.Where(entry => entry.IsShopEligible))
            {
                var entities = state.Pool.Entities.Where(
                    entity => string.Equals(entity.TypeId, entry.TypeId, StringComparison.Ordinal)).ToArray();
                if (entities.Length != MatchPoolCopyCounts.GetForRarity(entry.Rarity)
                    || entities.Select(entity => entity.CopyIndex).Distinct().Count() != entities.Length)
                {
                    diagnosticCode = "match.invariant.pool.copyCount";
                    return false;
                }
            }
            if (state.Phase == MatchPhase.Ended && string.IsNullOrWhiteSpace(state.EndReason))
            {
                diagnosticCode = "match.invariant.endReason";
                return false;
            }
            if (state.Flow == null
                || state.Flow.PairingGeneration < 0
                || state.Flow.PairingPopulation < 0
                || state.Flow.PairingOffset < 0
                || (state.Flow.HasPreparationClock
                    && state.Flow.PreparationDeadlineHostMonotonicMs
                        < state.Flow.PreparationStartedAtHostMonotonicMs)
                || !Enum.IsDefined(typeof(MatchEndReason), state.Flow.EndReason))
            {
                diagnosticCode = "match.invariant.flow";
                return false;
            }
            if (!MatchFlowInvariant.TryValidate(state, out diagnosticCode))
            {
                return false;
            }

            diagnosticCode = string.Empty;
            return true;
        }
    }
}
