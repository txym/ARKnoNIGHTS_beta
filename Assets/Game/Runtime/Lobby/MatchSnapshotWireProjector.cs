using System;
using System.Linq;
using ArknoNights.Match;

namespace ArknoNights.Lobby
{
    public static class MatchSnapshotWireProjector
    {
        public static ScopedSnapshotPayload Project(
            PlayerMatchSnapshot snapshot,
            MatchLocalConnectionState localConnectionState)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var publicState = snapshot.Public;
            return new ScopedSnapshotPayload
            {
                SessionId = publicState.SessionId,
                StateRevision = publicState.StateRevision,
                LocalConnectionState = localConnectionState.ToString(),
                PublicState = new PublicMatchStateWire
                {
                    SessionId = publicState.SessionId,
                    StateRevision = publicState.StateRevision,
                    Phase = publicState.Phase.ToString(),
                    RoundNumber = publicState.RoundNumber,
                    PreparationRemainingMs = publicState.PreparationRemainingMs,
                    Pairings = publicState.Pairings.Select(pairing => new PublicMatchPairingWire
                    {
                        BattleId = pairing.BattleId,
                        BattleIndex = pairing.BattleIndex,
                        Kind = pairing.Kind.ToString(),
                        HomePlayerId = pairing.HomePlayerId,
                        AwayPlayerId = pairing.AwayPlayerId,
                        ShadowOwnerPlayerId = pairing.ShadowOwnerPlayerId
                    }).ToArray(),
                    EndReason = publicState.EndReason.ToString(),
                    FinalStandings = publicState.FinalStandings.Select(ProjectStanding).ToArray(),
                    Seats = publicState.Seats.Select(ProjectPublicSeat).ToArray()
                },
                OwnerPrivateState = snapshot.Owner == null
                    ? null
                    : ProjectOwner(snapshot.Owner)
            };
        }

        public static bool ContainsOnlyRecipientPrivateState(
            ScopedSnapshotPayload snapshot,
            string recipientPlayerId)
        {
            if (snapshot == null || snapshot.PublicState == null) return false;
            return snapshot.OwnerPrivateState == null
                || string.Equals(
                    snapshot.OwnerPrivateState.PlayerId,
                    recipientPlayerId,
                    StringComparison.Ordinal);
        }

        private static PublicMatchSeatWire ProjectPublicSeat(PublicMatchSeatSnapshot seat)
        {
            return new PublicMatchSeatWire
            {
                SeatIndex = seat.SeatIndex,
                PlayerId = seat.PlayerId,
                DisplayName = seat.DisplayName,
                AvatarId = seat.AvatarId,
                Life = seat.Life,
                ConnectionState = seat.ConnectionState.ToString(),
                Ready = seat.Ready,
                Eliminated = seat.Eliminated,
                Placement = seat.Placement.GetValueOrDefault(),
                HasPlacement = seat.Placement.HasValue,
                Units = seat.Units.Select(unit => new MatchUnitWire
                {
                    UnitId = unit.UnitId,
                    TypeId = unit.TypeId,
                    Zone = unit.Zone.ToString(),
                    EliteLevel = unit.EliteLevel,
                    HasFormation = unit.Formation.HasValue,
                    FormationX = unit.Formation.HasValue ? unit.Formation.Value.X : 0,
                    FormationY = unit.Formation.HasValue ? unit.Formation.Value.Y : 0,
                    AcquisitionOrdinal = 0,
                    Buffs = unit.Buffs.Select(ProjectBuff).ToArray()
                }).ToArray(),
                TargetedUnitBuffs = seat.TargetedUnitBuffs.Select(buff =>
                    new MatchTargetedBuffWire
                    {
                        BuffInstanceId = buff.BuffInstanceId,
                        BuffTypeId = buff.BuffTypeId,
                        TargetUnitId = buff.TargetUnitId,
                        CanonicalPayload = buff.CanonicalPayload,
                        DiscardPolicy = buff.DiscardPolicy.ToString()
                    }).ToArray(),
                GlobalBuffs = seat.GlobalBuffs.Select(buff =>
                    new MatchGlobalBuffWire
                    {
                        BuffInstanceId = buff.BuffInstanceId,
                        BuffTypeId = buff.BuffTypeId,
                        CanonicalPayload = buff.CanonicalPayload
                    }).ToArray(),
                SourceEffects = seat.SourceEffects.Select(effect =>
                    new MatchSourceEffectWire
                    {
                        EffectInstanceId = effect.EffectInstanceId,
                        EffectTypeId = effect.EffectTypeId,
                        CanonicalPayload = effect.CanonicalPayload
                    }).ToArray()
            };
        }

        private static OwnerMatchStateWire ProjectOwner(OwnerPrivateSnapshot owner)
        {
            return new OwnerMatchStateWire
            {
                PlayerId = owner.PlayerId,
                Gold = owner.Gold,
                Level = owner.Level,
                UpgradeDiscountCountAtThisLevel = owner.UpgradeDiscountCountAtThisLevel,
                CurrentUpgradePrice = owner.CurrentUpgradePrice,
                SuccessfulShopPurchaseCount =
                    owner.PreparationBehavior.SuccessfulShopPurchaseCount,
                HasIssuedEffectiveFreezeThisRound =
                    owner.PreparationBehavior.HasIssuedEffectiveFreezeThisRound,
                TotalDeploymentCost = owner.TotalDeploymentCost,
                AvailableDeploymentCost = owner.AvailableDeploymentCost,
                StreakKind = owner.StreakKind.ToString(),
                StreakCount = owner.StreakCount,
                Units = owner.Units.Select(ProjectUnit).ToArray(),
                ShopOffers = owner.ShopOffers.Select(offer => new MatchShopOfferWire
                {
                    SlotIndex = offer.SlotIndex,
                    UnitId = offer.UnitId,
                    TypeId = offer.TypeId,
                    Rarity = offer.Rarity.GetValueOrDefault(),
                    HasRarity = offer.Rarity.HasValue,
                    IsFrozen = offer.IsFrozen
                }).ToArray(),
                OverflowUnits = owner.OverflowUnits.Select(ProjectUnit).ToArray(),
                StagingStacks = owner.StagingStacks.Select(stack => new MatchStagingStackWire
                {
                    TypeId = stack.TypeId,
                    NumericTypeId = stack.NumericTypeId,
                    DeploymentCost = stack.DeploymentCost,
                    EliteLevel = stack.EliteLevel,
                    BuffCanonicalSummary = stack.BuffCanonicalSummary,
                    UnitIds = stack.UnitIds.ToArray()
                }).ToArray(),
                TargetedUnitBuffs = owner.TargetedUnitBuffs.Select(buff => new MatchTargetedBuffWire
                {
                    BuffInstanceId = buff.BuffInstanceId,
                    BuffTypeId = buff.BuffTypeId,
                    TargetUnitId = buff.TargetUnitId,
                    CanonicalPayload = buff.CanonicalPayload,
                    DiscardPolicy = buff.DiscardPolicy.ToString()
                }).ToArray(),
                GlobalBuffs = owner.GlobalBuffs.Select(buff => new MatchGlobalBuffWire
                {
                    BuffInstanceId = buff.BuffInstanceId,
                    BuffTypeId = buff.BuffTypeId,
                    CanonicalPayload = buff.CanonicalPayload
                }).ToArray(),
                SourceEffects = owner.SourceEffects.Select(effect => new MatchSourceEffectWire
                {
                    EffectInstanceId = effect.EffectInstanceId,
                    EffectTypeId = effect.EffectTypeId,
                    CanonicalPayload = effect.CanonicalPayload
                }).ToArray()
            };
        }

        private static MatchUnitWire ProjectUnit(MatchUnitState unit)
        {
            return new MatchUnitWire
            {
                UnitId = unit.UnitId,
                TypeId = unit.TypeId,
                Zone = unit.Zone.ToString(),
                EliteLevel = unit.EliteLevel,
                HasFormation = unit.Formation.HasValue,
                FormationX = unit.Formation.HasValue ? unit.Formation.Value.X : 0,
                FormationY = unit.Formation.HasValue ? unit.Formation.Value.Y : 0,
                AcquisitionOrdinal = unit.AcquisitionOrdinal,
                Buffs = unit.Buffs.Select(ProjectBuff).ToArray()
            };
        }

        private static MatchBuffWire ProjectBuff(MatchBuffState buff)
        {
            return new MatchBuffWire
            {
                BuffId = buff.BuffId,
                CanonicalPayload = buff.CanonicalPayload
            };
        }

        private static MatchStandingWire ProjectStanding(MatchStanding standing)
        {
            return new MatchStandingWire
            {
                PlayerId = standing.PlayerId,
                Life = standing.Life,
                Placement = standing.Placement.GetValueOrDefault(),
                HasPlacement = standing.Placement.HasValue
            };
        }
    }
}
