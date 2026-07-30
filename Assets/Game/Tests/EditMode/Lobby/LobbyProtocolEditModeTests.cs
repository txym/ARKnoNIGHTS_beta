using System;
using System.Text;
using ArknoNights.Lobby;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LobbyProtocolEditModeTests
    {
        [TestCase(0, "Amiy")]
        [TestCase(1, "Clementi")]
        [TestCase(2, "Kirar")]
        [TestCase(3, "Zumam")]
        public void AvatarDisplayName_UsesTheFourApprovedCapitalizedAvatarNames(int avatarIndex, string expected)
        {
            Assert.That(LobbyProfile.DisplayNameForAvatar(avatarIndex), Is.EqualTo(expected));
        }

        [Test]
        public void Profile_NormalizesSubmittedDisplayNameToAvatarName()
        {
            var profile = new LobbyProfile("player-1", "Doctor", 2);

            Assert.That(profile.IsValid(), Is.True);
            Assert.That(profile.DisplayName, Is.EqualTo("Kirar"));
        }

        [Test]
        public void Decode_RejectsFrameAboveMaximumSize()
        {
            var oversized = new byte[LobbyProtocol.MaximumMessageBytes + 1];

            Assert.That(LobbyProtocol.TryDecode(oversized, out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.FrameTooLarge));
        }

        [TestCase("123456", true)]
        [TestCase("12345", false)]
        [TestCase("12a456", false)]
        public void RoomCode_RequiresExactlySixDigits(string value, bool expected)
        {
            Assert.That(LobbyRoomCode.IsValid(value), Is.EqualTo(expected));
        }

        [TestCase(LobbyMessageKind.JoinRequest)]
        [TestCase(LobbyMessageKind.RoomSnapshot)]
        [TestCase(LobbyMessageKind.Ping)]
        public void EncodeThenDecode_RoundTripsSupportedMessages(LobbyMessageKind kind)
        {
            var message = new LobbyWireMessage
            {
                protocolVersion = LobbyProtocol.ProtocolVersion,
                kind = kind.ToString(),
                roomCode = "123456",
                playerId = "player-1",
                displayName = kind == LobbyMessageKind.JoinRequest ? "Doctor" : string.Empty,
                avatarIndex = 2,
                sentUnixMilliseconds = 123456789,
                snapshotJson = kind == LobbyMessageKind.RoomSnapshot ? "{\"members\":[]}" : string.Empty,
                matchProtocolVersion = kind == LobbyMessageKind.JoinRequest ? "lan-match-test-1" : null,
                matchRulesVersion = kind == LobbyMessageKind.JoinRequest ? "rules-test-1" : null,
                battleCoreVersion = kind == LobbyMessageKind.JoinRequest ? "battle-test-1" : null,
                unitCatalogSha256 = kind == LobbyMessageKind.JoinRequest ? new string('a', 64) : null,
                abilityCatalogSha256 = kind == LobbyMessageKind.JoinRequest ? new string('b', 64) : null
            };

            var frame = LobbyProtocol.Encode(message);

            Assert.That(LobbyProtocol.TryDecode(frame, out var decoded, out var error), Is.True);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.None));
            Assert.That(decoded.kind, Is.EqualTo(kind.ToString()));
            Assert.That(decoded.roomCode, Is.EqualTo("123456"));
            Assert.That(decoded.playerId, Is.EqualTo("player-1"));
        }

        [Test]
        public void Decode_RejectsJoinWithoutCompleteCompatibilityManifest()
        {
            var frame = CreateFrame(
                "{\"protocolVersion\":1,\"kind\":\"JoinRequest\",\"roomCode\":\"123456\","
                + "\"playerId\":\"player-1\",\"displayName\":\"Doctor\",\"avatarIndex\":2}");

            Assert.That(
                LobbyProtocol.TryDecode(frame, out _, out var error),
                Is.False);
            Assert.That(
                error,
                Is.EqualTo(
                    LobbyProtocolError.InvalidCompatibilityManifest));
        }

        [Test]
        public void Decode_RejectsInvalidJson()
        {
            Assert.That(LobbyProtocol.TryDecode(CreateFrame("{"), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.InvalidJson));
        }

        [Test]
        public void Decode_RejectsUnsupportedProtocolVersion()
        {
            Assert.That(LobbyProtocol.TryDecode(CreateFrame("{\"protocolVersion\":2,\"kind\":\"JoinRequest\",\"roomCode\":\"123456\",\"playerId\":\"player-1\",\"displayName\":\"Doctor\",\"avatarIndex\":2}"), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.UnsupportedProtocolVersion));
        }

        [Test]
        public void Decode_RejectsNumericMessageKind()
        {
            Assert.That(LobbyProtocol.TryDecode(CreateFrame("{\"protocolVersion\":1,\"kind\":\"999\",\"roomCode\":\"123456\",\"playerId\":\"player-1\"}"), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.UnknownMessageKind));
        }

        [Test]
        public void Decode_RejectsInvalidRoomCode()
        {
            Assert.That(LobbyProtocol.TryDecode(CreateFrame("{\"protocolVersion\":1,\"kind\":\"JoinRequest\",\"roomCode\":\"12a456\",\"playerId\":\"player-1\",\"displayName\":\"Doctor\",\"avatarIndex\":2}"), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.InvalidRoomCode));
        }

        [Test]
        public void Decode_RejectsProfileWithLongDisplayName()
        {
            var json = "{\"protocolVersion\":1,\"kind\":\"JoinRequest\",\"roomCode\":\"123456\",\"playerId\":\"player-1\",\"displayName\":\"" + new string('x', LobbyProfile.MaximumDisplayNameCharacters + 1) + "\",\"avatarIndex\":2}";

            Assert.That(LobbyProtocol.TryDecode(CreateFrame(json), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.InvalidProfile));
        }

        [Test]
        public void Decode_RejectsProfileWithOutOfRangeAvatar()
        {
            var json = "{\"protocolVersion\":1,\"kind\":\"JoinRequest\",\"roomCode\":\"123456\",\"playerId\":\"player-1\",\"displayName\":\"Doctor\",\"avatarIndex\":" + (LobbyProfile.MaximumAvatarIndex + 1) + "}";

            Assert.That(LobbyProtocol.TryDecode(CreateFrame(json), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.InvalidProfile));
        }

        [Test]
        public void Decode_RejectsSnapshotJsonAboveLimit()
        {
            var json = "{\"protocolVersion\":1,\"kind\":\"RoomSnapshot\",\"roomCode\":\"123456\",\"playerId\":\"player-1\",\"snapshotJson\":\"" + new string('x', LobbyProtocol.MaximumSnapshotJsonBytes + 1) + "\"}";

            Assert.That(LobbyProtocol.TryDecode(CreateFrame(json), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyProtocolError.SnapshotTooLarge));
        }

        [Test]
        public void RoomSnapshot_RejectsMoreThanFourMembers()
        {
            var members = new LobbyMemberSnapshot[LobbyRoomSnapshot.MaximumMembers + 1];
            for (var index = 0; index < members.Length; index++)
            {
                members[index] = new LobbyMemberSnapshot(new LobbyProfile("player-" + index, "Doctor", 0), false, 0);
            }

            Assert.That(() => new LobbyRoomSnapshot("123456", "player-0", members, false, 1), Throws.ArgumentException);
        }

        private static byte[] CreateFrame(string json)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            var frame = new byte[payload.Length + sizeof(int)];
            frame[0] = (byte)(payload.Length >> 24);
            frame[1] = (byte)(payload.Length >> 16);
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, sizeof(int), payload.Length);
            return frame;
        }
    }
}
