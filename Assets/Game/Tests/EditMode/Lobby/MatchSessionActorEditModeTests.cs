using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArknoNights.Match;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class MatchSessionActorEditModeTests
    {
        private const string HostId = "lan-11111111111111111111111111111111";
        private const string GuestId = "lan-22222222222222222222222222222222";

        [Test]
        public void Create_FreezesFourSeatsAndUsesOnlyUnusedBotAvatars()
        {
            var created = CreateSession();

            Assert.That(created.Success, Is.True, created.DiagnosticCode);
            var seats = created.Actor.ProjectHostState().Seats;
            Assert.That(seats.Select(seat => seat.SeatIndex), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(seats.Take(2).All(seat =>
                seat.ControllerKind == MatchControllerKind.Human), Is.True);
            Assert.That(seats.Skip(2).All(seat =>
                seat.ControllerKind == MatchControllerKind.NativeBot), Is.True);
            Assert.That(seats.Skip(2).Select(seat => seat.AvatarId),
                Is.EquivalentTo(new[] { "avatar-2", "avatar-3" }));
            Assert.That(created.Actor.ProjectScoped(HostId).PublicState.Seats
                .Select(seat => seat.DisplayName),
                Is.EqualTo(new[] { "Amiy", "Clementi", "Kirar", "Zumam" }));
        }

        [Test]
        public void Commands_AreSerializedByHostAcceptSequenceAndSnapshotsStayScoped()
        {
            var actor = StartConnectedActor(out _, out _);
            var revision = actor.StateRevision;
            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(actor.SessionId, Command(
                    GuestId,
                    1,
                    "guest-ready",
                    revision)));
            actor.EnqueueHostCommand(Command(
                HostId,
                1,
                "host-ready",
                revision));

            var dispatches = actor.Tick(100);
            var acknowledgements = dispatches
                .Where(item => item.Kind == MatchWireKind.CommandAck)
                .Select(item => (MatchCommandAckPayload)item.Payload)
                .ToArray();

            Assert.That(acknowledgements.Select(item => item.HostAcceptSequence),
                Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(acknowledgements.All(item => item.DidChangeState), Is.True);
            var hostSnapshot = actor.ProjectScoped(HostId);
            var guestSnapshot = actor.ProjectScoped(GuestId);
            Assert.That(hostSnapshot.OwnerPrivateState.PlayerId, Is.EqualTo(HostId));
            Assert.That(guestSnapshot.OwnerPrivateState.PlayerId, Is.EqualTo(GuestId));
            Assert.That(hostSnapshot.OwnerPrivateState.PlayerId, Is.Not.EqualTo(GuestId));

            var frame = MatchProtocol.Encode(
                MatchWireKind.ScopedSnapshot,
                actor.SessionId,
                "privacy",
                guestSnapshot,
                MatchWireDirection.HostToClient);
            var json = MatchProtocol.StrictUtf8.GetString(
                frame,
                sizeof(int),
                frame.Length - sizeof(int));
            Assert.That(json, Does.Not.Contain("\"pool\""));
            Assert.That(json, Does.Not.Contain("controllerKind"));
            Assert.That(json, Does.Not.Contain("reconnectToken"));
            Assert.That(json, Does.Not.Contain(HostId + "\",\"gold\""));
        }

        [Test]
        public void DisconnectAndReconnect_PreserveSeatReplaceGenerationAndClearReady()
        {
            var actor = StartConnectedActor(
                out var guestInitialization,
                out var configuration);
            actor.EnqueueConnectionLost(
                "guest-connection",
                1,
                GuestId);
            actor.Tick(100);

            var disconnected = actor.ProjectHostState().Seats
                .Single(seat => seat.PlayerId == GuestId);
            Assert.That(disconnected.ConnectionState,
                Is.EqualTo(MatchConnectionState.DisconnectedGrace));
            Assert.That(actor.ProjectHostState().Seats,
                Has.Count.EqualTo(4));

            var reconnect = new MatchReconnectRequestPayload
            {
                PlayerId = GuestId,
                RawToken = guestInitialization.ReconnectToken,
                Manifest = MatchCompatibilityWire.FromDomain(
                    configuration.CompatibilityManifest),
                ClientLastAppliedRevision = 0
            };
            actor.EnqueueRemote(
                "replacement-connection",
                0,
                null,
                Envelope(
                    actor.SessionId,
                    reconnect,
                    MatchWireKind.ReconnectRequest));
            var dispatches = actor.Tick(200);

            var accepted = dispatches
                .Where(item => item.Kind == MatchWireKind.ReconnectAccepted)
                .Select(item => (MatchReconnectAcceptedPayload)item.Payload)
                .Single();
            Assert.That(accepted.ConnectionGeneration, Is.EqualTo(2));
            Assert.That(dispatches
                .Where(item => item.ConnectionId == "replacement-connection")
                .Select(item => item.Kind)
                .Take(3)
                .ToArray(),
                Is.EqualTo(new[]
                {
                    MatchWireKind.ReconnectAccepted,
                    MatchWireKind.ScopedSnapshot,
                    MatchWireKind.ClockSync
                }));
            var reconnected = actor.ProjectHostState().Seats
                .Single(seat => seat.PlayerId == GuestId);
            Assert.That(reconnected.ConnectionState,
                Is.EqualTo(MatchConnectionState.Connected));
            Assert.That(reconnected.Ready, Is.False);
        }

        [Test]
        public void Reconnect_CompatibilityMismatchPreservesTokenForLaterRetry()
        {
            var actor = StartConnectedActor(
                out var guestInitialization,
                out var configuration);
            var mismatch = new MatchCompatibilityManifest(
                configuration.CompatibilityManifest.ProtocolVersion,
                configuration.CompatibilityManifest.MatchRulesVersion + "-other",
                configuration.CompatibilityManifest.BattleCoreVersion,
                configuration.CompatibilityManifest.UnitCatalogSha256,
                configuration.CompatibilityManifest.AbilityCatalogSha256);
            actor.EnqueueRemote(
                "replacement",
                0,
                null,
                Envelope(
                    actor.SessionId,
                    new MatchReconnectRequestPayload
                    {
                        PlayerId = GuestId,
                        RawToken = guestInitialization.ReconnectToken,
                        Manifest = MatchCompatibilityWire.FromDomain(mismatch),
                        ClientLastAppliedRevision = 0
                    },
                    MatchWireKind.ReconnectRequest));

            var rejected = actor.Tick(100)
                .Single(item => item.Kind == MatchWireKind.ReconnectRejected);

            Assert.That(((MatchReconnectRejectedPayload)rejected.Payload).Code,
                Is.EqualTo(MatchReconnectRejectCode.CompatibilityMismatch.ToString()));
            Assert.That(guestInitialization.ReconnectToken, Is.Not.Empty);
        }

        [Test]
        public void Command_ForgedIdentityOrOldGenerationNeverReachesAuthority()
        {
            var actor = StartConnectedActor(out _, out _);
            var revision = actor.StateRevision;
            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(actor.SessionId, Command(
                    HostId,
                    1,
                    "forged-player",
                    revision)));

            var forgedAcknowledgement = actor.Tick(10)
                .Single(item => item.Kind == MatchWireKind.CommandAck);
            Assert.That(
                ((MatchCommandAckPayload)forgedAcknowledgement.Payload).ResultCode,
                Is.EqualTo(MatchCommandCode.ConnectionRejected.ToString()));
            Assert.That(
                actor.BindFrozenConnection(GuestId, "replacement-connection", 2),
                Is.True);

            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(actor.SessionId, Command(
                    GuestId,
                    1,
                    "old-generation",
                    revision)));

            var oldGenerationAcknowledgement = actor.Tick(20)
                .Single(item => item.Kind == MatchWireKind.CommandAck);

            Assert.That(
                ((MatchCommandAckPayload)oldGenerationAcknowledgement.Payload).ResultCode,
                Is.EqualTo(MatchCommandCode.ConnectionRejected.ToString()));
            Assert.That(actor.StateRevision, Is.EqualTo(revision));
        }

        [Test]
        public void CommandAck_PrecedesSnapshotAndDuplicateCommandIsIdempotent()
        {
            var actor = StartConnectedActor(out _, out _);
            var command = Command(
                GuestId,
                1,
                "idempotent-ready",
                actor.StateRevision);
            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(actor.SessionId, command));

            var first = actor.Tick(10)
                .Where(item =>
                    item.ConnectionId == "guest-connection")
                .ToArray();
            var firstAck = (MatchCommandAckPayload)first
                .First(item => item.Kind == MatchWireKind.CommandAck)
                .Payload;

            Assert.That(
                first.Select(item => item.Kind).Take(2),
                Is.EqualTo(new[]
                {
                    MatchWireKind.CommandAck,
                    MatchWireKind.ScopedSnapshot
                }));
            Assert.That(firstAck.DidChangeState, Is.True);
            var revision = actor.StateRevision;

            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(actor.SessionId, command));
            var repeatedAck = (MatchCommandAckPayload)actor.Tick(20)
                .Single(item => item.Kind == MatchWireKind.CommandAck)
                .Payload;

            Assert.That(repeatedAck.ResultCode, Is.EqualTo(firstAck.ResultCode));
            Assert.That(
                repeatedAck.AcceptedStateRevision,
                Is.EqualTo(firstAck.AcceptedStateRevision));
            Assert.That(actor.StateRevision, Is.EqualTo(revision));
        }

        [Test]
        public void SnapshotRequest_ReturnsCurrentRecipientScopedFullSnapshot()
        {
            var actor = StartConnectedActor(out _, out _);
            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(
                    actor.SessionId,
                    new MatchSnapshotRequestPayload
                    {
                        ClientLastAppliedRevision = 0
                    },
                    MatchWireKind.SnapshotRequest));

            var snapshot = (ScopedSnapshotPayload)actor.Tick(10)
                .Single(item =>
                    item.Kind == MatchWireKind.ScopedSnapshot
                    && item.ConnectionId == "guest-connection")
                .Payload;

            Assert.That(snapshot.StateRevision, Is.EqualTo(actor.StateRevision));
            Assert.That(
                snapshot.OwnerPrivateState.PlayerId,
                Is.EqualTo(GuestId));
        }

        [Test]
        public void AuthorityProjection_RejectsAccessOutsideOwningActorThread()
        {
            var actor = StartConnectedActor(out _, out _);

            Assert.Throws<InvalidOperationException>(() =>
                Task.Run(() => actor.ProjectScoped(HostId))
                    .GetAwaiter()
                    .GetResult());
        }

        [Test]
        public void Start_RevalidatesEveryCompatibilityFieldAndAvatarCapacity()
        {
            var configuration =
                LanMatchSessionConfiguration.CreateForTests();
            var expected = configuration.CompatibilityManifest;
            var mismatches = new[]
            {
                new MatchCompatibilityManifest(
                    expected.ProtocolVersion + "-x",
                    expected.MatchRulesVersion,
                    expected.BattleCoreVersion,
                    expected.UnitCatalogSha256,
                    expected.AbilityCatalogSha256),
                new MatchCompatibilityManifest(
                    expected.ProtocolVersion,
                    expected.MatchRulesVersion + "-x",
                    expected.BattleCoreVersion,
                    expected.UnitCatalogSha256,
                    expected.AbilityCatalogSha256),
                new MatchCompatibilityManifest(
                    expected.ProtocolVersion,
                    expected.MatchRulesVersion,
                    expected.BattleCoreVersion + "-x",
                    expected.UnitCatalogSha256,
                    expected.AbilityCatalogSha256),
                new MatchCompatibilityManifest(
                    expected.ProtocolVersion,
                    expected.MatchRulesVersion,
                    expected.BattleCoreVersion,
                    new string('c', 64),
                    expected.AbilityCatalogSha256),
                new MatchCompatibilityManifest(
                    expected.ProtocolVersion,
                    expected.MatchRulesVersion,
                    expected.BattleCoreVersion,
                    expected.UnitCatalogSha256,
                    new string('d', 64))
            };
            foreach (var mismatch in mismatches)
            {
                var manifests = MatchingManifests(configuration);
                manifests[GuestId] = mismatch;
                var result = MatchSessionBuilder.Create(
                    ReadyLobby(),
                    configuration,
                    manifests,
                    0);
                Assert.That(
                    result.Failure,
                    Is.EqualTo(
                        MatchSessionStartFailure.CompatibilityMismatch));
            }

            var insufficient = new LanMatchSessionConfiguration(
                expected,
                configuration.ShopCatalog,
                availableAvatarCount: 3,
                allowSyntheticPlayerIdsForTests: true);
            var capacityResult = MatchSessionBuilder.Create(
                ReadyLobby(),
                insufficient,
                MatchingManifests(insufficient),
                0);
            Assert.That(
                capacityResult.Failure,
                Is.EqualTo(
                    MatchSessionStartFailure.AvatarCapacityInsufficient));
        }

        [Test]
        public void ExplicitQuitAndHostEnd_InvalidateReconnectCredentials()
        {
            var actor = StartConnectedActor(
                out var guestInitialization,
                out var configuration);
            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(
                    actor.SessionId,
                    new MatchExplicitQuitPayload
                    {
                        PlayerId = GuestId,
                        ConnectionGeneration = 1
                    },
                    MatchWireKind.ExplicitQuit));
            actor.Tick(10);

            var guest = actor.Participants.Single(
                item => item.PlayerId == GuestId);
            Assert.That(guest.ExplicitlyQuit, Is.True);
            Assert.That(guest.ReconnectCredentialActive, Is.False);

            actor.EnqueueRemote(
                "replacement",
                0,
                null,
                Envelope(
                    actor.SessionId,
                    new MatchReconnectRequestPayload
                    {
                        PlayerId = GuestId,
                        RawToken =
                            guestInitialization.ReconnectToken,
                        Manifest = MatchCompatibilityWire.FromDomain(
                            configuration.CompatibilityManifest),
                        ClientLastAppliedRevision = 0
                    },
                    MatchWireKind.ReconnectRequest));
            var rejected = (MatchReconnectRejectedPayload)actor.Tick(20)
                .Single(item =>
                    item.Kind == MatchWireKind.ReconnectRejected)
                .Payload;
            Assert.That(
                rejected.Code,
                Is.EqualTo(
                    MatchReconnectRejectCode.ExplicitlyQuit.ToString()));

            var ended = actor.AbortByHost("match.test.abort");
            Assert.That(
                ended.Any(item =>
                    item.Kind == MatchWireKind.MatchEnded),
                Is.True);
            Assert.That(actor.Lifecycle, Is.EqualTo(MatchSessionLifecycle.Ended));
            Assert.That(
                actor.Participants.All(item =>
                    !item.ReconnectCredentialActive),
                Is.True);
        }

        [Test]
        public void BattleContracts_ValidateScopeAndRecoveryPreservesWireOrder()
        {
            var actor = StartConnectedActor(
                out var guestInitialization,
                out var configuration);
            SealCurrentRound(actor);
            var plan = actor.ProjectHostState()
                .Flow.SealedRoundPlan;
            Assert.That(plan, Is.Not.Null);
            var battleSetId =
                actor.SessionId + "-round-" + plan.RoundNumber;
            var seal = new MatchBattleSealPayload
            {
                RoundNumber = plan.RoundNumber,
                BattleSetId = battleSetId,
                CanonicalInputHash = plan.CanonicalInputHash,
                SealedPayload = "m7-contract-payload"
            };
            Assert.That(actor.PublishBattleSeal(seal), Is.Not.Empty);
            Assert.That(actor.PublishPlaybackStart(
                new MatchPlaybackStartPayload
                {
                    RoundNumber = plan.RoundNumber,
                    BattleSetId = battleSetId,
                    CanonicalInputHash = plan.CanonicalInputHash,
                    HostMonotonicStartMs = 1000,
                    StartTick = 0
                }), Is.Not.Empty);
            Assert.That(actor.PublishPlaybackClock(
                new MatchPlaybackClockPayload
                {
                    RoundNumber = plan.RoundNumber,
                    BattleSetId = battleSetId,
                    CanonicalInputHash = plan.CanonicalInputHash,
                    HostMonotonicNowMs = 7000,
                    CurrentTick = 120
                }), Is.Not.Empty);

            var accepted = new List<MatchBattleTransportEvent>();
            actor.BattleTransportAccepted += accepted.Add;
            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(
                    actor.SessionId,
                    new MatchFirstChunkReadyPayload
                    {
                        RoundNumber = plan.RoundNumber,
                        BattleSetId = battleSetId,
                        CanonicalInputHash = plan.CanonicalInputHash,
                        ReadyRevision = actor.StateRevision
                    },
                    MatchWireKind.FirstChunkReady));
            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(
                    actor.SessionId,
                    new MatchFinalSecondHashPayload
                    {
                        RoundNumber = plan.RoundNumber,
                        BattleId = "unknown-battle",
                        CanonicalInputHash = plan.CanonicalInputHash,
                        FinalSecondSha256 = new string('f', 64)
                    },
                    MatchWireKind.FinalSecondHash));
            actor.EnqueueHostBattleContract(
                MatchWireKind.FinalSecondHash,
                new MatchFinalSecondHashPayload
                {
                    RoundNumber = plan.RoundNumber,
                    BattleId = plan.Pairings[0].BattleId,
                    CanonicalInputHash = plan.CanonicalInputHash,
                    FinalSecondSha256 = new string('e', 64)
                });
            actor.Tick(100);

            Assert.That(
                accepted.Select(item => item.PlayerId),
                Is.EqualTo(new[] { GuestId, HostId }));
            Assert.That(
                accepted.Select(item => item.HostAcceptSequence),
                Is.Ordered.Ascending);
            Assert.That(
                actor.Participants.Single(item =>
                    item.PlayerId == GuestId).ActiveConnectionId,
                Is.EqualTo("guest-connection"));

            actor.EnqueueConnectionLost(
                "guest-connection",
                1,
                GuestId);
            actor.Tick(200);
            actor.EnqueueRemote(
                "replacement",
                0,
                null,
                Envelope(
                    actor.SessionId,
                    new MatchReconnectRequestPayload
                    {
                        PlayerId = GuestId,
                        RawToken =
                            guestInitialization.ReconnectToken,
                        Manifest = MatchCompatibilityWire.FromDomain(
                            configuration.CompatibilityManifest),
                        ClientLastAppliedRevision = 0
                    },
                    MatchWireKind.ReconnectRequest));
            var recovery = actor.Tick(300)
                .Where(item =>
                    item.ConnectionId == "replacement")
                .ToArray();

            Assert.That(
                recovery.Select(item => item.Kind),
                Is.EqualTo(new[]
                {
                    MatchWireKind.ReconnectAccepted,
                    MatchWireKind.ScopedSnapshot,
                    MatchWireKind.ClockSync,
                    MatchWireKind.BattleSeal,
                    MatchWireKind.PlaybackStart,
                    MatchWireKind.PlaybackClock
                }));
            Assert.That(
                ((MatchBattleSealPayload)recovery[3].Payload)
                    .SealedPayload,
                Is.EqualTo("m7-contract-payload"));
            Assert.That(
                ((MatchPlaybackClockPayload)recovery[5].Payload)
                    .CurrentTick,
                Is.EqualTo(120));
        }

        private static MatchSessionStartResult CreateSession()
        {
            var configuration = LanMatchSessionConfiguration.CreateForTests();
            var lobby = new LobbyRoomSnapshot(
                "123456",
                HostId,
                new[]
                {
                    new LobbyMemberSnapshot(
                        new LobbyProfile(HostId, "host", 0),
                        true,
                        0),
                    new LobbyMemberSnapshot(
                        new LobbyProfile(GuestId, "guest", 1),
                        true,
                        0)
                },
                false,
                3);
            return MatchSessionBuilder.Create(
                lobby,
                configuration,
                new Dictionary<string, MatchCompatibilityManifest>
                {
                    [HostId] = configuration.CompatibilityManifest,
                    [GuestId] = configuration.CompatibilityManifest
                },
                0);
        }

        private static LobbyRoomSnapshot ReadyLobby()
        {
            return new LobbyRoomSnapshot(
                "123456",
                HostId,
                new[]
                {
                    new LobbyMemberSnapshot(
                        new LobbyProfile(HostId, "host", 0),
                        true,
                        0),
                    new LobbyMemberSnapshot(
                        new LobbyProfile(GuestId, "guest", 1),
                        true,
                        0)
                },
                false,
                3);
        }

        private static Dictionary<string, MatchCompatibilityManifest>
            MatchingManifests(
                LanMatchSessionConfiguration configuration)
        {
            return new Dictionary<string, MatchCompatibilityManifest>
            {
                [HostId] = configuration.CompatibilityManifest,
                [GuestId] = configuration.CompatibilityManifest
            };
        }

        private static void SealCurrentRound(
            MatchSessionHostActor actor)
        {
            var revision = actor.StateRevision;
            actor.EnqueueRemote(
                "guest-connection",
                1,
                GuestId,
                Envelope(
                    actor.SessionId,
                    Command(
                        GuestId,
                        1,
                        "guest-seal-ready",
                        revision)));
            actor.EnqueueHostCommand(Command(
                HostId,
                1,
                "host-seal-ready",
                revision));
            actor.Tick(10);
        }

        private static MatchSessionHostActor StartConnectedActor(
            out MatchInitializedPayload guestInitialization,
            out LanMatchSessionConfiguration configuration)
        {
            configuration = LanMatchSessionConfiguration.CreateForTests();
            var lobby = new LobbyRoomSnapshot(
                "123456",
                HostId,
                new[]
                {
                    new LobbyMemberSnapshot(new LobbyProfile(HostId, "host", 0), true, 0),
                    new LobbyMemberSnapshot(new LobbyProfile(GuestId, "guest", 1), true, 0)
                },
                false,
                3);
            var created = MatchSessionBuilder.Create(
                lobby,
                configuration,
                new Dictionary<string, MatchCompatibilityManifest>
                {
                    [HostId] = configuration.CompatibilityManifest,
                    [GuestId] = configuration.CompatibilityManifest
                },
                0);
            Assert.That(created.Success, Is.True, created.DiagnosticCode);
            var actor = created.Actor;
            Assert.That(actor.BindFrozenConnection(HostId, "host-local", 1), Is.True);
            Assert.That(actor.BindFrozenConnection(GuestId, "guest-connection", 1), Is.True);
            actor.TakeInitialization(HostId);
            guestInitialization = actor.TakeInitialization(GuestId);
            actor.MarkMatchRunning();
            return actor;
        }

        private static MatchCommandWirePayload Command(
            string playerId,
            long generation,
            string commandId,
            long revision)
        {
            return new MatchCommandWirePayload
            {
                PlayerId = playerId,
                ConnectionGeneration = generation,
                CommandId = commandId,
                KnownStateRevision = revision,
                CommandKind = MatchCommandKind.SetPreparationReady.ToString(),
                DesiredReady = true
            };
        }

        private static MatchWireEnvelope Envelope(
            string sessionId,
            MatchCommandWirePayload command)
        {
            return Envelope(
                sessionId,
                command,
                MatchWireKind.Command);
        }

        private static MatchWireEnvelope Envelope(
            string sessionId,
            object payload,
            MatchWireKind kind)
        {
            var frame = MatchProtocol.Encode(
                kind,
                sessionId,
                "message-" + Guid.NewGuid().ToString("N"),
                payload,
                MatchWireDirection.ClientToHost);
            Assert.That(MatchProtocol.TryDecode(
                frame,
                MatchWireDirection.ClientToHost,
                out var envelope,
                out var error), Is.True, error.ToString());
            return envelope;
        }
    }
}
