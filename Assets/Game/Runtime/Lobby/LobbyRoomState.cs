using System;
using System.Collections.Generic;

namespace ArknoNights.Lobby
{
    public sealed class LobbyRoomState
    {
        private readonly string roomCode;
        private readonly List<Member> members;
        private long revision;
        private LobbyRoomSnapshot snapshot;

        private LobbyRoomState(LobbyProfile hostProfile, string roomCode)
        {
            this.roomCode = roomCode;
            members = new List<Member> { new Member(hostProfile) };
            revision = 1;
            PublishSnapshot();
        }

        public LobbyRoomSnapshot Snapshot => snapshot;

        public static LobbyRoomState CreateHost(LobbyProfile hostProfile, string roomCode)
        {
            if (!LobbyRoomCode.IsValid(roomCode))
            {
                throw new ArgumentException("The room code must contain exactly six digits.", nameof(roomCode));
            }

            if (hostProfile == null || !hostProfile.IsValid())
            {
                throw new ArgumentException("The host profile is invalid.", nameof(hostProfile));
            }

            return new LobbyRoomState(hostProfile, roomCode);
        }

        public bool TryJoin(LobbyProfile profile, out LobbyJoinFailure failure)
        {
            if (snapshot.HasStarted)
            {
                failure = LobbyJoinFailure.RoomStarted;
                return false;
            }

            if (profile == null || !profile.IsValid())
            {
                failure = LobbyJoinFailure.InvalidProfile;
                return false;
            }

            if (FindMember(profile.PlayerId) != null)
            {
                failure = LobbyJoinFailure.DuplicatePlayer;
                return false;
            }

            if (members.Count >= LobbyRoomSnapshot.MaximumMembers)
            {
                failure = LobbyJoinFailure.RoomFull;
                return false;
            }

            members.Add(new Member(profile));
            PublishMutation();
            failure = LobbyJoinFailure.None;
            return true;
        }

        public bool TrySetReady(string playerId, bool isReady, out LobbyJoinFailure failure)
        {
            return TrySetReady(playerId, playerId, isReady, out failure);
        }

        public bool TrySetReady(string requestingPlayerId, string playerId, bool isReady, out LobbyJoinFailure failure)
        {
            if (snapshot.HasStarted)
            {
                failure = LobbyJoinFailure.RoomStarted;
                return false;
            }

            if (requestingPlayerId != playerId)
            {
                failure = LobbyJoinFailure.UnknownPlayer;
                return false;
            }

            var member = FindMember(playerId);
            if (member == null)
            {
                failure = LobbyJoinFailure.UnknownPlayer;
                return false;
            }

            member.IsReady = isReady;
            PublishMutation();
            failure = LobbyJoinFailure.None;
            return true;
        }

        public bool RemovePlayer(string playerId)
        {
            var member = FindMember(playerId);
            if (member == null)
            {
                return false;
            }

            members.Remove(member);
            PublishMutation();
            return true;
        }

        public bool TryStart(string playerId, out LobbyJoinFailure failure)
        {
            if (snapshot.HasStarted)
            {
                failure = LobbyJoinFailure.RoomStarted;
                return false;
            }

            if (snapshot.HostPlayerId != playerId)
            {
                failure = LobbyJoinFailure.NotHost;
                return false;
            }

            for (var index = 0; index < members.Count; index++)
            {
                if (!members[index].IsReady)
                {
                    failure = LobbyJoinFailure.NotReady;
                    return false;
                }
            }

            PublishMutation(true);
            failure = LobbyJoinFailure.None;
            return true;
        }

        public int PruneExpiredMembers(IEnumerable<string> expiredPlayerIds)
        {
            if (expiredPlayerIds == null)
            {
                return 0;
            }

            var expired = new HashSet<string>(expiredPlayerIds);
            var removed = members.RemoveAll(member => expired.Contains(member.Profile.PlayerId));
            if (removed > 0)
            {
                PublishMutation();
            }

            return removed;
        }

        private Member FindMember(string playerId)
        {
            for (var index = 0; index < members.Count; index++)
            {
                if (members[index].Profile.PlayerId == playerId)
                {
                    return members[index];
                }
            }

            return null;
        }

        private void PublishMutation(bool hasStarted = false)
        {
            var alreadyStarted = snapshot != null && snapshot.HasStarted;
            revision++;
            PublishSnapshot(hasStarted || alreadyStarted);
        }

        private void PublishSnapshot(bool hasStarted = false)
        {
            var snapshots = new LobbyMemberSnapshot[members.Count];
            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                snapshots[index] = new LobbyMemberSnapshot(member.Profile, member.IsReady, 0);
            }

            snapshot = new LobbyRoomSnapshot(roomCode, members.Count == 0 ? null : members[0].Profile.PlayerId, snapshots, hasStarted || (snapshot != null && snapshot.HasStarted), revision);
        }

        private sealed class Member
        {
            public Member(LobbyProfile profile)
            {
                Profile = profile;
            }

            public LobbyProfile Profile { get; }
            public bool IsReady { get; set; }
        }
    }
}
