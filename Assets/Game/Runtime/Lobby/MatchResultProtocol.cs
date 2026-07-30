using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace ArknoNights.Lobby
{
    public enum MatchSystemResultKind
    {
        AuthorityStateChanged,
        ConnectionChanged,
        PreparationAdvanced,
        BattleSeal,
        PlaybackStarted,
        RoundSettled,
        MatchEnded,
        HostAborted
    }

    [DataContract]
    public sealed class MatchStateDeltaWire
    {
        [DataMember(Name = "baseStateRevision")] public long BaseStateRevision;
        [DataMember(Name = "stateRevision")] public long StateRevision;
        [DataMember(Name = "phase")] public string Phase;
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "preparationRemainingMs")] public long PreparationRemainingMs;
        [DataMember(Name = "pairings")] public PublicMatchPairingWire[] Pairings;
        [DataMember(Name = "endReason")] public string EndReason;
        [DataMember(Name = "finalStandings")] public MatchStandingWire[] FinalStandings;
        [DataMember(Name = "changedSeats")] public PublicMatchSeatWire[] ChangedSeats;
        [DataMember(Name = "removedSeatPlayerIds")] public string[] RemovedSeatPlayerIds;
        [DataMember(Name = "hasOwnerPrivateState")] public bool HasOwnerPrivateState;
        [DataMember(Name = "ownerPrivateState")] public OwnerMatchStateWire OwnerPrivateState;
        [DataMember(Name = "localConnectionState")] public string LocalConnectionState;
    }

    [DataContract]
    public sealed class MatchOperationResultPayload
    {
        [DataMember(Name = "commandId")] public string CommandId;
        [DataMember(Name = "originPlayerId")] public string OriginPlayerId;
        [DataMember(Name = "commandKind")] public string CommandKind;
        [DataMember(Name = "primaryUnitId")] public string PrimaryUnitId;
        [DataMember(Name = "shopSlotIndex")] public int ShopSlotIndex;
        [DataMember(Name = "resultCode")] public string ResultCode;
        [DataMember(Name = "currentStateRevision")] public long CurrentStateRevision;
        [DataMember(Name = "acceptedStateRevision")] public long AcceptedStateRevision;
        [DataMember(Name = "hasAcceptedStateRevision")] public bool HasAcceptedStateRevision;
        [DataMember(Name = "didChangeState")] public bool DidChangeState;
        [DataMember(Name = "stableDetailCode")] public string StableDetailCode;
        [DataMember(Name = "hostAcceptSequence")] public long HostAcceptSequence;
        [DataMember(Name = "delta")] public MatchStateDeltaWire Delta;
    }

    [DataContract]
    public sealed class MatchSystemResultPayload
    {
        [DataMember(Name = "systemActionId")] public string SystemActionId;
        [DataMember(Name = "systemKind")] public string SystemKind;
        [DataMember(Name = "stateRevision")] public long StateRevision;
        [DataMember(Name = "hostAcceptSequence")] public long HostAcceptSequence;
        [DataMember(Name = "stableDetailCode")] public string StableDetailCode;
        [DataMember(Name = "delta")] public MatchStateDeltaWire Delta;
        [DataMember(Name = "battleSeal")] public MatchBattleSealPayload BattleSeal;
        [DataMember(Name = "playbackStart")] public MatchPlaybackStartPayload PlaybackStart;
        [DataMember(Name = "matchEnded")] public MatchEndedPayload MatchEnded;
    }

    public enum MatchDeltaApplyStatus
    {
        Applied,
        Duplicate,
        RecoveryRequired,
        Invalid
    }

    public sealed class MatchResultClientState
    {
        public event Action<ScopedSnapshotPayload> Changed;

        public ScopedSnapshotPayload Current { get; private set; }

        public bool TryApplyRecovery(ScopedSnapshotPayload recovery)
        {
            if (recovery == null
                || recovery.PublicState == null
                || recovery.StateRevision < 0
                || !string.Equals(
                    recovery.SessionId,
                    recovery.PublicState.SessionId,
                    StringComparison.Ordinal)
                || recovery.StateRevision != recovery.PublicState.StateRevision)
            {
                return false;
            }

            Current = recovery;
            Changed?.Invoke(Current);
            return true;
        }

        public MatchDeltaApplyStatus TryApply(MatchStateDeltaWire delta)
        {
            if (delta == null
                || Current == null
                || Current.PublicState == null
                || delta.StateRevision < 0
                || delta.BaseStateRevision < 0
                || delta.StateRevision < delta.BaseStateRevision)
            {
                return MatchDeltaApplyStatus.Invalid;
            }
            if (delta.StateRevision <= Current.StateRevision)
                return MatchDeltaApplyStatus.Duplicate;
            if (delta.BaseStateRevision != Current.StateRevision)
                return MatchDeltaApplyStatus.RecoveryRequired;

            var seats = Current.PublicState.Seats
                ?.Where(item => item != null)
                .ToDictionary(item => item.PlayerId, StringComparer.Ordinal)
                ?? new Dictionary<string, PublicMatchSeatWire>(StringComparer.Ordinal);
            foreach (var playerId in delta.RemovedSeatPlayerIds ?? Array.Empty<string>())
                seats.Remove(playerId);
            foreach (var seat in delta.ChangedSeats ?? Array.Empty<PublicMatchSeatWire>())
            {
                if (seat == null || string.IsNullOrWhiteSpace(seat.PlayerId))
                    return MatchDeltaApplyStatus.Invalid;
                seats[seat.PlayerId] = seat;
            }

            var owner = delta.HasOwnerPrivateState
                ? delta.OwnerPrivateState
                : Current.OwnerPrivateState;
            var publicState = new PublicMatchStateWire
            {
                SessionId = Current.SessionId,
                StateRevision = delta.StateRevision,
                Phase = delta.Phase,
                RoundNumber = delta.RoundNumber,
                PreparationRemainingMs = delta.PreparationRemainingMs,
                Pairings = delta.Pairings ?? Array.Empty<PublicMatchPairingWire>(),
                EndReason = delta.EndReason,
                FinalStandings = delta.FinalStandings ?? Array.Empty<MatchStandingWire>(),
                Seats = seats.Values
                    .OrderBy(item => item.SeatIndex)
                    .ToArray()
            };
            Current = new ScopedSnapshotPayload
            {
                SessionId = Current.SessionId,
                StateRevision = delta.StateRevision,
                PublicState = publicState,
                OwnerPrivateState = owner,
                LocalConnectionState = delta.LocalConnectionState
            };
            Changed?.Invoke(Current);
            return MatchDeltaApplyStatus.Applied;
        }
    }

    public static class MatchStateDeltaProjector
    {
        public static MatchStateDeltaWire Project(
            ScopedSnapshotPayload before,
            ScopedSnapshotPayload after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            if (before.PublicState == null || after.PublicState == null)
                throw new ArgumentException("Both projections require public state.");
            if (!string.Equals(before.SessionId, after.SessionId, StringComparison.Ordinal))
                throw new ArgumentException("Delta projections must belong to one session.");

            var beforeSeats = before.PublicState.Seats
                ?.Where(item => item != null)
                .ToDictionary(item => item.PlayerId, StringComparer.Ordinal)
                ?? new Dictionary<string, PublicMatchSeatWire>(StringComparer.Ordinal);
            var afterSeats = after.PublicState.Seats
                ?.Where(item => item != null)
                .ToDictionary(item => item.PlayerId, StringComparer.Ordinal)
                ?? new Dictionary<string, PublicMatchSeatWire>(StringComparer.Ordinal);
            var changedSeats = afterSeats.Values
                .Where(item =>
                    !beforeSeats.TryGetValue(item.PlayerId, out var previous)
                    || !WireEquals(previous, item))
                .OrderBy(item => item.SeatIndex)
                .ToArray();
            var removed = beforeSeats.Keys
                .Where(playerId => !afterSeats.ContainsKey(playerId))
                .OrderBy(playerId => playerId, StringComparer.Ordinal)
                .ToArray();
            var ownerChanged = !WireEquals(
                before.OwnerPrivateState,
                after.OwnerPrivateState);

            return new MatchStateDeltaWire
            {
                BaseStateRevision = before.StateRevision,
                StateRevision = after.StateRevision,
                Phase = after.PublicState.Phase,
                RoundNumber = after.PublicState.RoundNumber,
                PreparationRemainingMs = after.PublicState.PreparationRemainingMs,
                Pairings = after.PublicState.Pairings ?? Array.Empty<PublicMatchPairingWire>(),
                EndReason = after.PublicState.EndReason,
                FinalStandings = after.PublicState.FinalStandings
                    ?? Array.Empty<MatchStandingWire>(),
                ChangedSeats = changedSeats,
                RemovedSeatPlayerIds = removed,
                HasOwnerPrivateState = ownerChanged,
                OwnerPrivateState = ownerChanged
                    ? after.OwnerPrivateState
                    : null,
                LocalConnectionState = after.LocalConnectionState
            };
        }

        private static bool WireEquals<T>(T left, T right)
            where T : class
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            return string.Equals(
                MatchJson.Serialize(left),
                MatchJson.Serialize(right),
                StringComparison.Ordinal);
        }
    }
}
