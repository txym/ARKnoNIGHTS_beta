using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ArknoNights.Lobby
{
    public static class LobbyRoomCode
    {
        public const int Length = 6;

        public static bool IsValid(string value)
        {
            if (value == null || value.Length != Length) return false;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }

            return true;
        }
    }

    public sealed class LobbyProfile
    {
        public const int MaximumDisplayNameCharacters = 20;
        public const int MinimumAvatarIndex = 0;
        public const int MaximumAvatarIndex = 3;
        private readonly string submittedDisplayName;

        public LobbyProfile(string playerId, string displayName, int avatarIndex)
        {
            PlayerId = playerId;
            submittedDisplayName = displayName;
            DisplayName = DisplayNameForAvatar(avatarIndex);
            AvatarIndex = avatarIndex;
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
        public int AvatarIndex { get; }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(PlayerId)
                && !string.IsNullOrWhiteSpace(submittedDisplayName)
                && submittedDisplayName.Length <= MaximumDisplayNameCharacters
                && AvatarIndex >= MinimumAvatarIndex
                && AvatarIndex <= MaximumAvatarIndex;
        }

        public static string DisplayNameForAvatar(int avatarIndex)
        {
            switch (avatarIndex)
            {
                case 0: return "Amiy";
                case 1: return "Clementi";
                case 2: return "Kirar";
                default: return "Zumam";
            }
        }
    }

    public sealed class LobbyMemberSnapshot
    {
        public LobbyMemberSnapshot(LobbyProfile profile, bool isReady, long latencyMilliseconds)
        {
            Profile = profile;
            IsReady = isReady;
            LatencyMilliseconds = latencyMilliseconds;
        }

        public LobbyProfile Profile { get; }
        public string PlayerId => Profile == null ? null : Profile.PlayerId;
        public bool IsReady { get; }
        public long LatencyMilliseconds { get; }
    }

    public sealed class LobbyRoomSnapshot
    {
        public const int MaximumMembers = 4;

        public LobbyRoomSnapshot(string roomCode, string hostPlayerId, IEnumerable<LobbyMemberSnapshot> members, bool hasStarted, long revision)
        {
            var memberList = new List<LobbyMemberSnapshot>(members ?? Array.Empty<LobbyMemberSnapshot>());
            if (memberList.Count > MaximumMembers)
            {
                throw new ArgumentException("A lobby room cannot contain more than four members.", nameof(members));
            }

            RoomCode = roomCode;
            HostPlayerId = hostPlayerId;
            HasStarted = hasStarted;
            Revision = revision;
            Members = new ReadOnlyCollection<LobbyMemberSnapshot>(memberList);
        }

        public string RoomCode { get; }
        public string HostPlayerId { get; }
        public IReadOnlyList<LobbyMemberSnapshot> Members { get; }
        public bool HasStarted { get; }
        public long Revision { get; }
    }

    public sealed class LobbyDiscoveryEntry
    {
        public LobbyDiscoveryEntry(string roomCode, string hostDisplayName, int memberCount, int capacity, bool isJoinable, int tcpPort, long sequence)
        {
            RoomCode = roomCode;
            HostDisplayName = hostDisplayName;
            MemberCount = memberCount;
            Capacity = capacity;
            IsJoinable = isJoinable;
            TcpPort = tcpPort;
            Sequence = sequence;
        }

        public string RoomCode { get; }
        public string HostDisplayName { get; }
        public int MemberCount { get; }
        public int Capacity { get; }
        public bool IsJoinable { get; }
        public int TcpPort { get; }
        public long Sequence { get; }
    }

    public enum LobbyJoinFailure
    {
        None,
        InvalidRoomCode,
        InvalidProfile,
        DuplicatePlayer,
        RoomFull,
        RoomStarted,
        UnknownPlayer,
        NotHost,
        NotReady
    }
}
