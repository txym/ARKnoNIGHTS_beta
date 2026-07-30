using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Match;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class MatchSessionProtocolEditModeTests
    {
        private static readonly MatchCompatibilityManifest Manifest = new MatchCompatibilityManifest(
            "lan-match-1",
            "rules-1",
            "battle-1",
            new string('a', 64),
            new string('b', 64));

        [TestCaseSource(nameof(AllKinds))]
        public void Protocol_AllKinds_RoundTripWithExplicitDirection(MatchWireKind kind)
        {
            var direction = MatchProtocol.GetAllowedDirection(kind) == MatchWireDirection.HostToClient
                ? MatchWireDirection.HostToClient
                : MatchWireDirection.ClientToHost;
            var frame = MatchProtocol.Encode(
                kind,
                "session-1",
                "message-1",
                CreatePayload(kind),
                direction);

            Assert.That((frame[0] << 24) | (frame[1] << 16) | (frame[2] << 8) | frame[3],
                Is.EqualTo(frame.Length - sizeof(int)));
            Assert.That(MatchProtocol.TryDecode(frame, direction, out var envelope, out var error),
                Is.True, error.ToString());
            Assert.That(envelope.Kind, Is.EqualTo(kind.ToString()));
        }

        [Test]
        public void Protocol_RejectsStrictUtf8UnknownSchemaDirectionAndTrailingBytes()
        {
            var frame = MatchProtocol.Encode(
                MatchWireKind.Command,
                "session-1",
                "message-1",
                CreatePayload(MatchWireKind.Command),
                MatchWireDirection.ClientToHost);
            var invalidUtf8 = (byte[])frame.Clone();
            invalidUtf8[invalidUtf8.Length - 2] = 0xc3;
            invalidUtf8[invalidUtf8.Length - 1] = 0x28;

            Assert.That(MatchProtocol.TryDecode(
                invalidUtf8,
                MatchWireDirection.ClientToHost,
                out _,
                out var utf8Error), Is.False);
            Assert.That(utf8Error, Is.EqualTo(MatchProtocolError.InvalidUtf8));
            Assert.That(MatchProtocol.TryDecode(
                frame,
                MatchWireDirection.HostToClient,
                out _,
                out var directionError), Is.False);
            Assert.That(directionError, Is.EqualTo(MatchProtocolError.DirectionNotAllowed));

            var trailing = frame.Concat(new byte[] { 0 }).ToArray();
            Assert.That(MatchProtocol.TryDecode(
                trailing,
                MatchWireDirection.ClientToHost,
                out _,
                out var trailingError), Is.False);
            Assert.That(trailingError, Is.EqualTo(MatchProtocolError.InvalidFrameLength));

            var json = MatchProtocol.StrictUtf8.GetString(frame, sizeof(int), frame.Length - sizeof(int))
                .Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
            var unsupported = MatchProtocol.FrameForTests(MatchProtocol.StrictUtf8.GetBytes(json));
            Assert.That(MatchProtocol.TryDecode(
                unsupported,
                MatchWireDirection.ClientToHost,
                out _,
                out var schemaError), Is.False);
            Assert.That(schemaError, Is.EqualTo(MatchProtocolError.UnsupportedSchemaVersion));
        }

        [Test]
        public void Protocol_RejectsInvalidLengthsAndEnforcesKindSpecificLimits()
        {
            Assert.That(MatchProtocol.TryDecode(
                new byte[] { 0, 0, 0, 0 },
                MatchWireDirection.ClientToHost,
                out _,
                out var zeroError), Is.False);
            Assert.That(zeroError, Is.EqualTo(MatchProtocolError.InvalidFrameLength));
            Assert.That(MatchProtocol.TryDecode(
                new byte[] { 255, 255, 255, 255, 0 },
                MatchWireDirection.ClientToHost,
                out _,
                out var negativeError), Is.False);
            Assert.That(negativeError, Is.EqualTo(MatchProtocolError.InvalidFrameLength));
            Assert.That(MatchProtocol.TryDecode(
                new byte[MatchProtocol.AbsoluteMaximumFrameBytes + 1],
                MatchWireDirection.ClientToHost,
                out _,
                out var maximumError), Is.False);
            Assert.That(maximumError, Is.EqualTo(MatchProtocolError.FrameTooLarge));
            var valid = MatchProtocol.Encode(
                MatchWireKind.Ping,
                "session-1",
                "message-1",
                CreatePayload(MatchWireKind.Ping),
                MatchWireDirection.ClientToHost);
            Assert.That(MatchProtocol.TryDecode(
                valid.Take(valid.Length - 1).ToArray(),
                MatchWireDirection.ClientToHost,
                out _,
                out var truncatedError), Is.False);
            Assert.That(
                truncatedError,
                Is.EqualTo(MatchProtocolError.InvalidFrameLength));

            var control = MatchProtocol.Encode(
                MatchWireKind.Ping,
                "session-1",
                "message-1",
                CreatePayload(MatchWireKind.Ping),
                MatchWireDirection.ClientToHost);
            var oversizedControl = AddOuterWhitespace(
                control,
                MatchProtocol.ControlPayloadMaximum);
            Assert.That(MatchProtocol.TryDecode(
                oversizedControl,
                MatchWireDirection.ClientToHost,
                out _,
                out var controlError), Is.False);
            Assert.That(controlError, Is.EqualTo(MatchProtocolError.PayloadTooLarge));

            var snapshot = MatchProtocol.Encode(
                MatchWireKind.ScopedSnapshot,
                "session-1",
                "message-1",
                CreatePayload(MatchWireKind.ScopedSnapshot),
                MatchWireDirection.HostToClient);
            var largerThanControl = AddOuterWhitespace(
                snapshot,
                MatchProtocol.ControlPayloadMaximum);
            Assert.That(MatchProtocol.TryDecode(
                largerThanControl,
                MatchWireDirection.HostToClient,
                out _,
                out var snapshotError), Is.True, snapshotError.ToString());

            var seal = MatchProtocol.Encode(
                MatchWireKind.BattleSeal,
                "session-1",
                "message-1",
                CreatePayload(MatchWireKind.BattleSeal),
                MatchWireDirection.HostToClient);
            var largerThanSnapshot = AddOuterWhitespace(
                seal,
                MatchProtocol.ScopedSnapshotPayloadMaximum);
            Assert.That(MatchProtocol.TryDecode(
                largerThanSnapshot,
                MatchWireDirection.HostToClient,
                out _,
                out var sealError), Is.True, sealError.ToString());
        }

        [Test]
        public void Protocol_RejectsMissingFieldsInvalidEnumsRangesAndSessionMismatch()
        {
            Assert.Throws<ArgumentException>(() =>
                MatchProtocol.Encode(
                    MatchWireKind.Command,
                    "session-1",
                    "message-1",
                    new MatchCommandWirePayload
                    {
                        PlayerId =
                            "lan-" + new string('1', 32),
                        ConnectionGeneration = 1,
                        CommandId = new string('x', 129),
                        KnownStateRevision = 0,
                        CommandKind =
                            MatchCommandKind.SetPreparationReady.ToString()
                    },
                    MatchWireDirection.ClientToHost));
            Assert.Throws<ArgumentException>(() =>
                MatchProtocol.Encode(
                    MatchWireKind.Command,
                    "session-1",
                    "message-1",
                    new MatchCommandWirePayload
                    {
                        PlayerId =
                            "lan-" + new string('1', 32),
                        ConnectionGeneration = 1,
                        CommandId = "bad-enum",
                        KnownStateRevision = 0,
                        CommandKind = "999"
                    },
                    MatchWireDirection.ClientToHost));
            Assert.Throws<ArgumentException>(() =>
                MatchProtocol.Encode(
                    MatchWireKind.Command,
                    "session-1",
                    "message-1",
                    new MatchCommandWirePayload
                    {
                        PlayerId =
                            "lan-" + new string('1', 32),
                        ConnectionGeneration = 1,
                        CommandId = "bad-formation",
                        KnownStateRevision = 0,
                        CommandKind =
                            MatchCommandKind.DeployUnit.ToString(),
                        UnitId = "unit-1",
                        TargetX = 0,
                        TargetY = 1,
                        ExpectedAvailableCost = 1
                    },
                    MatchWireDirection.ClientToHost));
            Assert.Throws<ArgumentException>(() =>
                MatchProtocol.Encode(
                    MatchWireKind.ScopedSnapshot,
                    "different-session",
                    "message-1",
                    Snapshot(1),
                    MatchWireDirection.HostToClient));
        }

        [Test]
        public void IdentityAndReconnectSecurity_AreStableAndFailClosed()
        {
            var store = new MemoryIdentityStore();
            var first = LocalProfileIdentity.GetOrCreate(store);
            var second = LocalProfileIdentity.GetOrCreate(store);

            Assert.That(first, Does.Match("^lan-[0-9a-f]{32}$"));
            Assert.That(second, Is.EqualTo(first));
            store.Value = "damaged";
            Assert.That(LocalProfileIdentity.GetOrCreate(store), Does.Match("^lan-[0-9a-f]{32}$"));
            Assert.That(store.Value, Is.Not.EqualTo("damaged"));

            var issued = ReconnectTokenIssuer.Issue();
            Assert.That(Convert.FromBase64String(
                issued.RawToken.Replace('-', '+').Replace('_', '/') + "="),
                Has.Length.EqualTo(32));
            Assert.That(ReconnectTokenIssuer.Verify(issued.RawToken, issued.VerifierSha256), Is.True);
            Assert.That(ReconnectTokenIssuer.Verify(issued.RawToken + "x", issued.VerifierSha256), Is.False);
            Assert.That(issued.VerifierSha256, Is.Not.EqualTo(issued.RawToken));
            Assert.That(Enumerable.Range(0, 7).Select(ReconnectRetrySchedule.GetDelayMilliseconds),
                Is.EqualTo(new[] { 0, 500, 1000, 2000, 5000, 5000, 5000 }));
            Assert.That(
                ReconnectCredentialPolicy
                    .ShouldClearOnAuthoritativeRejection(
                        MatchReconnectRejectCode.InvalidToken),
                Is.True);
            Assert.That(
                ReconnectCredentialPolicy
                    .ShouldClearOnAuthoritativeRejection(
                        MatchReconnectRejectCode.SessionEnded),
                Is.True);
            Assert.That(
                ReconnectCredentialPolicy
                    .ShouldClearOnAuthoritativeRejection(
                        MatchReconnectRejectCode.CompatibilityMismatch),
                Is.False);
        }

        [Test]
        public void ScopedSnapshotClient_AppliesOnlyNewerFullRevision()
        {
            var client = new ScopedSnapshotClientState();
            var notifications = 0;
            client.Changed += _ => notifications++;

            Assert.That(client.TryApply(Snapshot(2)), Is.True);
            Assert.That(client.TryApply(Snapshot(2)), Is.False);
            Assert.That(client.TryApply(Snapshot(1)), Is.False);
            Assert.That(client.TryApply(Snapshot(4)), Is.True);

            Assert.That(client.Current.StateRevision, Is.EqualTo(4));
            Assert.That(notifications, Is.EqualTo(2));
        }

        private static IEnumerable<MatchWireKind> AllKinds()
        {
            return Enum.GetValues(typeof(MatchWireKind)).Cast<MatchWireKind>();
        }

        private static object CreatePayload(MatchWireKind kind)
        {
            switch (kind)
            {
                case MatchWireKind.Handshake:
                    return new MatchHandshakePayload { PlayerId = "lan-" + new string('1', 32), Manifest = MatchCompatibilityWire.FromDomain(Manifest) };
                case MatchWireKind.HandshakeAccepted:
                    return new MatchHandshakeAcceptedPayload { ConnectionId = "connection-1", ConnectionGeneration = 1 };
                case MatchWireKind.Reject:
                    return new MatchRejectPayload { Code = "Rejected", StableDetailCode = "match.rejected" };
                case MatchWireKind.MatchInitialized:
                    return new MatchInitializedPayload { PlayerId = "lan-" + new string('1', 32), SeatIndex = 1, ConnectionGeneration = 1, HostPlayerId = "lan-" + new string('1', 32), MatchSeed = "seed-1", ReconnectToken = new string('A', ReconnectTokenIssuer.RawTokenCharacters), Manifest = MatchCompatibilityWire.FromDomain(Manifest), Snapshot = Snapshot(1), Clock = Clock() };
                case MatchWireKind.Command:
                    return new MatchCommandWirePayload { PlayerId = "lan-" + new string('1', 32), ConnectionGeneration = 1, CommandId = "command-1", KnownStateRevision = 1, CommandKind = MatchCommandKind.SetPreparationReady.ToString(), DesiredReady = true };
                case MatchWireKind.CommandAck:
                    return new MatchCommandAckPayload { CommandId = "command-1", ResultCode = MatchCommandCode.Accepted.ToString(), CurrentStateRevision = 2, AcceptedStateRevision = 2, HasAcceptedStateRevision = true, DidChangeState = true, StableDetailCode = "match.ready.accepted", HostAcceptSequence = 1 };
                case MatchWireKind.ScopedSnapshot:
                    return Snapshot(1);
                case MatchWireKind.SnapshotRequest:
                    return new MatchSnapshotRequestPayload { ClientLastAppliedRevision = 1 };
                case MatchWireKind.ClockSync:
                    return Clock();
                case MatchWireKind.BattleSeal:
                    return new MatchBattleSealPayload { RoundNumber = 1, BattleSetId = "set-1", CanonicalInputHash = new string('c', 64), SealedPayload = "{}" };
                case MatchWireKind.FirstChunkReady:
                    return new MatchFirstChunkReadyPayload { RoundNumber = 1, BattleSetId = "set-1", CanonicalInputHash = new string('c', 64), ReadyRevision = 2 };
                case MatchWireKind.PlaybackStart:
                    return new MatchPlaybackStartPayload { RoundNumber = 1, BattleSetId = "set-1", CanonicalInputHash = new string('c', 64), HostMonotonicStartMs = 100, StartTick = 0 };
                case MatchWireKind.PlaybackClock:
                    return new MatchPlaybackClockPayload { RoundNumber = 1, BattleSetId = "set-1", CanonicalInputHash = new string('c', 64), HostMonotonicNowMs = 200, CurrentTick = 10 };
                case MatchWireKind.FinalSecondHash:
                    return new MatchFinalSecondHashPayload { RoundNumber = 1, BattleId = "battle-1", CanonicalInputHash = new string('c', 64), FinalSecondSha256 = new string('d', 64) };
                case MatchWireKind.ClientBattleFailure:
                    return new MatchClientBattleFailurePayload { RoundNumber = 1, BattleId = "battle-1", CanonicalInputHash = new string('c', 64), StableDetailCode = "battle.client.failed" };
                case MatchWireKind.ReconnectRequest:
                    return new MatchReconnectRequestPayload { PlayerId = "lan-" + new string('1', 32), RawToken = new string('A', ReconnectTokenIssuer.RawTokenCharacters), ClientLastAppliedRevision = 1, Manifest = MatchCompatibilityWire.FromDomain(Manifest) };
                case MatchWireKind.ReconnectAccepted:
                    return new MatchReconnectAcceptedPayload { PlayerId = "lan-" + new string('1', 32), SeatIndex = 1, ConnectionGeneration = 2, ReconnectToken = new string('A', ReconnectTokenIssuer.RawTokenCharacters) };
                case MatchWireKind.ReconnectRejected:
                    return new MatchReconnectRejectedPayload { Code = MatchReconnectRejectCode.InvalidToken.ToString(), StableDetailCode = "match.reconnect.rejected" };
                case MatchWireKind.ExplicitQuit:
                    return new MatchExplicitQuitPayload { PlayerId = "lan-" + new string('1', 32), ConnectionGeneration = 1 };
                case MatchWireKind.MatchEnded:
                    return new MatchEndedPayload { EndReason = MatchEndReason.NoContest.ToString(), FinalRevision = 3, FinalStandings = Array.Empty<MatchStandingWire>() };
                case MatchWireKind.Ping:
                case MatchWireKind.Pong:
                    return new MatchHeartbeatPayload { ConnectionGeneration = 1, SentUnixMilliseconds = 42 };
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static MatchClockSyncPayload Clock()
        {
            return new MatchClockSyncPayload { RoundNumber = 1, Phase = MatchPhase.Preparation.ToString(), HostMonotonicNowMs = 10, PreparationDeadlineHostMonotonicMs = 30010 };
        }

        private static byte[] AddOuterWhitespace(byte[] frame, int minimumPayloadBytes)
        {
            var json = MatchProtocol.StrictUtf8.GetString(
                frame,
                sizeof(int),
                frame.Length - sizeof(int));
            var insertAt = json.Length - 1;
            var padding = new string(
                ' ',
                Math.Max(1, minimumPayloadBytes - json.Length + 1));
            return MatchProtocol.FrameForTests(
                MatchProtocol.StrictUtf8.GetBytes(
                    json.Insert(insertAt, padding)));
        }

        private static ScopedSnapshotPayload Snapshot(long revision)
        {
            return new ScopedSnapshotPayload
            {
                SessionId = "session-1",
                StateRevision = revision,
                LocalConnectionState = MatchLocalConnectionState.Connected.ToString(),
                PublicState = new PublicMatchStateWire
                {
                    SessionId = "session-1",
                    StateRevision = revision,
                    Phase = MatchPhase.Preparation.ToString(),
                    RoundNumber = 1,
                    EndReason = MatchEndReason.None.ToString(),
                    Pairings = Array.Empty<PublicMatchPairingWire>(),
                    FinalStandings = Array.Empty<MatchStandingWire>(),
                    Seats = Array.Empty<PublicMatchSeatWire>()
                }
            };
        }

        private sealed class MemoryIdentityStore : ILocalProfileIdentityStore
        {
            public string Value;
            public bool TryLoad(out string playerId) { playerId = Value; return Value != null; }
            public void Save(string playerId) { Value = playerId; }
            public void Clear() { Value = null; }
        }
    }
}
