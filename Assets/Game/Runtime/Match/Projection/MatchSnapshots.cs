using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Match
{
    public sealed class PublicMatchUnitSnapshot
    {
        internal PublicMatchUnitSnapshot(MatchUnitState state)
        {
            UnitId = state.UnitId;
            TypeId = state.TypeId;
            Zone = state.Zone;
            EliteLevel = state.EliteLevel;
            Formation = state.Formation;
            Buffs = new ReadOnlyCollection<MatchBuffState>(
                state.Buffs.ToArray());

            var writer = new CanonicalSummaryWriter(nameof(PublicMatchUnitSnapshot));
            writer.String("unitId", UnitId);
            writer.String("typeId", TypeId);
            writer.EnumValue("zone", Zone);
            writer.Integer("eliteLevel", EliteLevel);
            writer.String("formation", Formation.HasValue ? Formation.Value.CanonicalSummary : string.Empty);
            foreach (var buff in Buffs)
            {
                writer.Summary("buff", buff.CanonicalSummary);
            }
            CanonicalSummary = writer.ToString();
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public MatchUnitZone Zone { get; }
        public int EliteLevel { get; }
        public MatchFormationPosition? Formation { get; }
        public IReadOnlyList<MatchBuffState> Buffs { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class PublicMatchSeatSnapshot
    {
        internal PublicMatchSeatSnapshot(
            MatchSeatState state,
            PublicConnectionState connectionState)
        {
            SeatIndex = state.SeatIndex;
            PlayerId = state.PlayerId;
            DisplayName = state.DisplayName;
            AvatarId = state.AvatarId;
            Life = state.Life;
            ConnectionState = connectionState;
            Ready = state.Ready;
            Eliminated = state.Eliminated;
            Placement = state.Placement;
            var units = state.Units
                    .Where(unit => unit.Zone == MatchUnitZone.Deployed || unit.Zone == MatchUnitZone.Staging)
                    .OrderBy(unit => unit.Zone)
                    .ThenBy(unit => unit.Formation.HasValue ? unit.Formation.Value.Y : int.MaxValue)
                    .ThenBy(unit => unit.Formation.HasValue ? unit.Formation.Value.X : int.MaxValue)
                    .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
                    .Select(unit => new PublicMatchUnitSnapshot(unit))
                    .ToArray();
            Units = new ReadOnlyCollection<PublicMatchUnitSnapshot>(units);
            DeployedUnits = new ReadOnlyCollection<PublicMatchUnitSnapshot>(
                units.Where(unit => unit.Zone == MatchUnitZone.Deployed).ToArray());
            StagingUnits = new ReadOnlyCollection<PublicMatchUnitSnapshot>(
                units.Where(unit => unit.Zone == MatchUnitZone.Staging).ToArray());

            var writer = new CanonicalSummaryWriter(nameof(PublicMatchSeatSnapshot));
            writer.Integer("seatIndex", SeatIndex);
            writer.String("playerId", PlayerId);
            writer.String("displayName", DisplayName);
            writer.String("avatarId", AvatarId);
            writer.Integer("life", Life);
            writer.EnumValue("connection", ConnectionState);
            writer.Boolean("ready", Ready);
            writer.Boolean("eliminated", Eliminated);
            writer.NullableInteger("placement", Placement);
            foreach (var unit in Units)
            {
                writer.Summary("unit", unit.CanonicalSummary);
            }
            CanonicalSummary = writer.ToString();
        }

        public int SeatIndex { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string AvatarId { get; }
        public int Life { get; }
        public PublicConnectionState ConnectionState { get; }
        public bool Ready { get; }
        public bool Eliminated { get; }
        public int? Placement { get; }
        public IReadOnlyList<PublicMatchUnitSnapshot> Units { get; }
        public IReadOnlyList<PublicMatchUnitSnapshot> DeployedUnits { get; }
        public IReadOnlyList<PublicMatchUnitSnapshot> StagingUnits { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class PublicMatchSnapshot
    {
        internal PublicMatchSnapshot(MatchState state)
        {
            SessionId = state.SessionId;
            StateRevision = state.StateRevision;
            Phase = state.Phase;
            RoundNumber = state.RoundNumber;
            PreparationRemainingMs = state.Phase == MatchPhase.Preparation
                && state.Flow.HasPreparationClock
                    ? Math.Max(
                        0,
                        state.Flow.PreparationDeadlineHostMonotonicMs
                            - state.Flow.LastHostMonotonicMs)
                    : 0;
            Pairings = new ReadOnlyCollection<PublicMatchPairingSnapshot>(
                (state.Flow.SealedRoundPlan == null
                    ? Enumerable.Empty<MatchSealedPairing>()
                    : state.Flow.SealedRoundPlan.Pairings)
                    .Select(pairing => new PublicMatchPairingSnapshot(pairing))
                    .ToArray());
            EndReason = state.Flow.EndReason;
            FinalStandings = new ReadOnlyCollection<MatchStanding>(
                state.Flow.FinalStandings.ToArray());
            Seats = new ReadOnlyCollection<PublicMatchSeatSnapshot>(
                state.Seats
                    .OrderBy(seat => seat.SeatIndex)
                    .Select(seat => new PublicMatchSeatSnapshot(
                        seat,
                        MatchSnapshotProjector.ToPublicConnectionState(seat)))
                    .ToArray());

            var writer = new CanonicalSummaryWriter(nameof(PublicMatchSnapshot));
            writer.String("sessionId", SessionId);
            writer.Integer("revision", StateRevision);
            writer.EnumValue("phase", Phase);
            writer.Integer("round", RoundNumber);
            writer.Integer("preparationRemainingMs", PreparationRemainingMs);
            foreach (var pairing in Pairings)
            {
                writer.Summary("pairing", pairing.CanonicalSummary);
            }
            writer.EnumValue("endReason", EndReason);
            foreach (var standing in FinalStandings)
            {
                writer.Summary("standing", standing.CanonicalSummary);
            }
            foreach (var seat in Seats)
            {
                writer.Summary("seat", seat.CanonicalSummary);
            }
            CanonicalSummary = writer.ToString();
        }

        public string SessionId { get; }
        public long StateRevision { get; }
        public MatchPhase Phase { get; }
        public int RoundNumber { get; }
        public long PreparationRemainingMs { get; }
        public IReadOnlyList<PublicMatchPairingSnapshot> Pairings { get; }
        public MatchEndReason EndReason { get; }
        public IReadOnlyList<MatchStanding> FinalStandings { get; }
        public IReadOnlyList<PublicMatchSeatSnapshot> Seats { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class PublicMatchPairingSnapshot
    {
        internal PublicMatchPairingSnapshot(MatchSealedPairing pairing)
        {
            BattleId = pairing.BattleId;
            BattleIndex = pairing.BattleIndex;
            Kind = pairing.Kind;
            HomePlayerId = pairing.HomePlayerId;
            AwayPlayerId = pairing.AwayPlayerId;
            ShadowOwnerPlayerId = pairing.ShadowOwnerPlayerId;
            var writer = new CanonicalSummaryWriter(nameof(PublicMatchPairingSnapshot));
            writer.String("battleId", BattleId);
            writer.Integer("battleIndex", BattleIndex);
            writer.EnumValue("kind", Kind);
            writer.String("homePlayerId", HomePlayerId);
            writer.String("awayPlayerId", AwayPlayerId);
            writer.String("shadowOwnerPlayerId", ShadowOwnerPlayerId);
            CanonicalSummary = writer.ToString();
        }

        public string BattleId { get; }
        public int BattleIndex { get; }
        public MatchPairingKind Kind { get; }
        public string HomePlayerId { get; }
        public string AwayPlayerId { get; }
        public string ShadowOwnerPlayerId { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class OwnerPrivateSnapshot
    {
        internal OwnerPrivateSnapshot(
            MatchSeatState seat,
            MatchShopCatalog catalog)
        {
            PlayerId = seat.PlayerId;
            Gold = seat.Gold;
            Level = seat.Level;
            UpgradeDiscountCountAtThisLevel = seat.UpgradeDiscountCountAtThisLevel;
            CurrentUpgradePrice = seat.CurrentUpgradePrice;
            PreparationBehavior = seat.PreparationBehavior;
            TotalDeploymentCost = seat.TotalDeploymentCost;
            AvailableDeploymentCost = seat.AvailableDeploymentCost;
            StreakKind = seat.StreakKind;
            StreakCount = seat.StreakCount;
            Units = new ReadOnlyCollection<MatchUnitState>(
                seat.Units
                    .OrderBy(unit => unit.UnitId, StringComparer.Ordinal)
                    .ToArray());
            ShopOffers = new ReadOnlyCollection<MatchShopOfferState>(
                seat.ShopOffers
                    .OrderBy(offer => offer.SlotIndex)
                    .ToArray());
            OverflowUnits = new ReadOnlyCollection<MatchUnitState>(
                seat.Units
                    .Where(unit => unit.Zone == MatchUnitZone.Overflow)
                    .OrderBy(unit => unit.AcquisitionOrdinal)
                    .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
                    .ToArray());
            StagingStacks = MatchStagingProjection.Project(seat, catalog);
            TargetedUnitBuffs = new ReadOnlyCollection<PlayerTargetedUnitBuffState>(
                seat.TargetedUnitBuffs.ToArray());
            GlobalBuffs = new ReadOnlyCollection<PlayerGlobalBuffState>(
                seat.GlobalBuffs.ToArray());
            SourceEffects = new ReadOnlyCollection<PlayerSourceEffectState>(
                seat.SourceEffects.ToArray());

            var writer = new CanonicalSummaryWriter(nameof(OwnerPrivateSnapshot));
            writer.String("playerId", PlayerId);
            writer.Integer("gold", Gold);
            writer.Integer("level", Level);
            writer.Integer("upgradeDiscount", UpgradeDiscountCountAtThisLevel);
            writer.Integer("currentUpgradePrice", CurrentUpgradePrice);
            writer.Summary("preparationBehavior", PreparationBehavior.CanonicalSummary);
            writer.Integer("totalCost", TotalDeploymentCost);
            writer.Integer("availableCost", AvailableDeploymentCost);
            writer.EnumValue("streakKind", StreakKind);
            writer.Integer("streakCount", StreakCount);
            foreach (var unit in Units)
            {
                writer.Summary("unit", unit.CanonicalSummary);
            }
            foreach (var offer in ShopOffers)
            {
                writer.Summary("shop", offer.CanonicalSummary);
            }
            foreach (var unit in OverflowUnits)
            {
                writer.Summary("overflowUnit", unit.CanonicalSummary);
            }
            foreach (var stack in StagingStacks)
            {
                writer.Summary("stagingStack", stack.CanonicalSummary);
            }
            foreach (var buff in TargetedUnitBuffs)
            {
                writer.Summary("targetedUnitBuff", buff.CanonicalSummary);
            }
            foreach (var buff in GlobalBuffs)
            {
                writer.Summary("globalBuff", buff.CanonicalSummary);
            }
            foreach (var effect in SourceEffects)
            {
                writer.Summary("sourceEffect", effect.CanonicalSummary);
            }
            CanonicalSummary = writer.ToString();
        }

        public string PlayerId { get; }
        public int Gold { get; }
        public int Level { get; }
        public int UpgradeDiscountCountAtThisLevel { get; }
        public int CurrentUpgradePrice { get; }
        public MatchPreparationBehaviorState PreparationBehavior { get; }
        public int TotalDeploymentCost { get; }
        public int AvailableDeploymentCost { get; }
        public MatchStreakKind StreakKind { get; }
        public int StreakCount { get; }
        public IReadOnlyList<MatchUnitState> Units { get; }
        public IReadOnlyList<MatchShopOfferState> ShopOffers { get; }
        public IReadOnlyList<MatchUnitState> OverflowUnits { get; }
        public IReadOnlyList<MatchStagingStackSnapshot> StagingStacks { get; }
        public IReadOnlyList<PlayerTargetedUnitBuffState> TargetedUnitBuffs { get; }
        public IReadOnlyList<PlayerGlobalBuffState> GlobalBuffs { get; }
        public IReadOnlyList<PlayerSourceEffectState> SourceEffects { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class PlayerMatchSnapshot
    {
        internal PlayerMatchSnapshot(PublicMatchSnapshot publicSnapshot, OwnerPrivateSnapshot owner)
        {
            Public = publicSnapshot;
            Owner = owner;
            var writer = new CanonicalSummaryWriter(nameof(PlayerMatchSnapshot));
            writer.Summary("public", Public.CanonicalSummary);
            writer.Summary("owner", Owner == null ? string.Empty : Owner.CanonicalSummary);
            CanonicalSummary = writer.ToString();
        }

        public PublicMatchSnapshot Public { get; }
        public OwnerPrivateSnapshot Owner { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchCommandRecordSnapshot
    {
        internal MatchCommandRecordSnapshot(
            string playerId,
            string commandId,
            string commandSummary,
            MatchCommandResult result)
        {
            PlayerId = playerId;
            CommandId = commandId;
            CommandSummary = commandSummary;
            Result = result;

            var writer = new CanonicalSummaryWriter(nameof(MatchCommandRecordSnapshot));
            writer.String("playerId", PlayerId);
            writer.String("commandId", CommandId);
            writer.Summary("command", CommandSummary);
            writer.Summary("result", Result == null ? string.Empty : Result.CanonicalSummary);
            CanonicalSummary = writer.ToString();
        }

        public string PlayerId { get; }
        public string CommandId { get; }
        public string CommandSummary { get; }
        public MatchCommandResult Result { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class HostMatchSnapshot
    {
        internal HostMatchSnapshot(
            MatchState state,
            IEnumerable<MatchCommandRecordSnapshot> commandRecords)
        {
            State = state;
            SessionId = state.SessionId;
            MatchSeed = state.MatchSeed;
            StateRevision = state.StateRevision;
            Phase = state.Phase;
            RoundNumber = state.RoundNumber;
            HostPlayerId = state.HostPlayerId;
            CompatibilityManifest = state.CompatibilityManifest;
            Pool = state.Pool;
            EndReason = state.EndReason;
            Flow = state.Flow;
            Seats = new ReadOnlyCollection<MatchSeatState>(
                state.Seats.OrderBy(seat => seat.SeatIndex).ToArray());
            CommandRecords = new ReadOnlyCollection<MatchCommandRecordSnapshot>(
                (commandRecords ?? Enumerable.Empty<MatchCommandRecordSnapshot>())
                    .OrderBy(record => record.PlayerId, StringComparer.Ordinal)
                    .ThenBy(record => record.CommandId, StringComparer.Ordinal)
                    .ToArray());

            var writer = new CanonicalSummaryWriter(nameof(HostMatchSnapshot));
            writer.Summary("state", state.CanonicalSummary);
            foreach (var record in CommandRecords)
            {
                writer.Summary("commandRecord", record.CanonicalSummary);
            }
            CanonicalSummary = writer.ToString();
        }

        public string SessionId { get; }
        public MatchState State { get; }
        public string MatchSeed { get; }
        public long StateRevision { get; }
        public MatchPhase Phase { get; }
        public int RoundNumber { get; }
        public string HostPlayerId { get; }
        public MatchCompatibilityManifest CompatibilityManifest { get; }
        public MatchPoolState Pool { get; }
        public string EndReason { get; }
        public MatchFlowState Flow { get; }
        public IReadOnlyList<MatchSeatState> Seats { get; }
        public IReadOnlyList<MatchCommandRecordSnapshot> CommandRecords { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchChangedEvent
    {
        internal MatchChangedEvent(PublicMatchSnapshot snapshot)
        {
            Snapshot = snapshot;
            StateRevision = snapshot.StateRevision;
        }

        public long StateRevision { get; }
        public PublicMatchSnapshot Snapshot { get; }
    }

    internal static class MatchSnapshotProjector
    {
        internal static PublicMatchSnapshot ProjectPublic(MatchState state)
        {
            return new PublicMatchSnapshot(state);
        }

        internal static PlayerMatchSnapshot ProjectForPlayer(MatchState state, MatchSeatState seat)
        {
            return new PlayerMatchSnapshot(
                ProjectPublic(state),
                seat.Eliminated
                    ? null
                    : new OwnerPrivateSnapshot(seat, state.Pool.Catalog));
        }

        internal static PublicConnectionState ToPublicConnectionState(MatchSeatState seat)
        {
            if (seat.Eliminated || seat.ConnectionState == MatchConnectionState.Eliminated)
            {
                return seat.ControllerKind == MatchControllerKind.Human
                    ? PublicConnectionState.Spectating
                    : PublicConnectionState.Eliminated;
            }
            if (seat.ControllerKind == MatchControllerKind.NativeBot
                || seat.ControllerKind == MatchControllerKind.TakeoverBot)
            {
                return PublicConnectionState.Online;
            }
            return seat.ConnectionState == MatchConnectionState.Connected
                ? PublicConnectionState.Online
                : PublicConnectionState.LostConnection;
        }
    }
}
