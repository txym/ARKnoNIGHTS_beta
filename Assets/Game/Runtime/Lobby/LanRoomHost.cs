using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ArknoNights.Match;

namespace ArknoNights.Lobby
{
    public sealed class LanRoomHost : IDisposable
    {
        private const int HeartbeatMilliseconds = 1000;
        private const int MaximumMissedHeartbeats = 3;
        private static readonly Stopwatch MonotonicClock = Stopwatch.StartNew();

        private readonly object gate = new object();
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Dictionary<string, GuestConnection> guestsByPlayerId =
            new Dictionary<string, GuestConnection>(StringComparer.Ordinal);
        private readonly Dictionary<string, GuestConnection> connectionsById =
            new Dictionary<string, GuestConnection>(StringComparer.Ordinal);
        private readonly List<GuestConnection> connections = new List<GuestConnection>();
        private readonly List<LanDiscoveryService> discoveryServices = new List<LanDiscoveryService>();
        private readonly Dictionary<string, MatchCompatibilityManifest> joinedManifests =
            new Dictionary<string, MatchCompatibilityManifest>(StringComparer.Ordinal);
        private readonly bool acceptsAnyRoomCode;
        private readonly LanMatchSessionConfiguration matchConfiguration;
        private readonly Task acceptTask;
        private readonly Task heartbeatTask;
        private readonly ConcurrentQueue<LobbyRoomSnapshot> pendingSnapshots =
            new ConcurrentQueue<LobbyRoomSnapshot>();
        private LobbyRoomState room;
        private LobbyRoomSnapshot publishedSnapshot;
        private volatile MatchSessionHostActor sessionActor;
        private long connectionIdCounter;
        private long messageIdCounter;
        private Task startBroadcastTask = Task.CompletedTask;
        private Task endedFlushTask = Task.CompletedTask;
        private volatile bool stopped;
        private volatile bool endedShutdownStarted;
        private volatile MatchSessionLifecycle lifecycle;

        private LanRoomHost(
            LobbyProfile hostProfile,
            int tcpPort,
            bool acceptsAnyRoomCode,
            LanMatchSessionConfiguration matchConfiguration)
        {
            if (hostProfile == null || !hostProfile.IsValid())
                throw new ArgumentException("The host profile is invalid.", nameof(hostProfile));
            this.matchConfiguration = matchConfiguration
                ?? throw new ArgumentNullException(nameof(matchConfiguration));
            if (!matchConfiguration.IsValid)
                throw new ArgumentException("The Match session configuration is invalid.", nameof(matchConfiguration));
            if (!matchConfiguration.AllowSyntheticPlayerIdsForTests
                && !LocalProfileIdentity.IsValid(hostProfile.PlayerId))
            {
                throw new ArgumentException(
                    "The host PlayerId is not an installation identity.",
                    nameof(hostProfile));
            }
            room = LobbyRoomState.CreateHost(hostProfile, CreateRoomCode());
            publishedSnapshot = room.Snapshot;
            this.acceptsAnyRoomCode = acceptsAnyRoomCode;
            joinedManifests.Add(hostProfile.PlayerId, matchConfiguration.CompatibilityManifest);
            Lifecycle = MatchSessionLifecycle.Lobby;
            listener = new TcpListener(IPAddress.Any, tcpPort);
            listener.Start();
            acceptTask = Task.Run(AcceptLoopAsync);
            heartbeatTask = Task.Run(HeartbeatLoopAsync);
        }

        public LobbyRoomSnapshot Snapshot => publishedSnapshot;
        public string RoomCode => Snapshot.RoomCode;
        public int TcpPort => ((IPEndPoint)listener.LocalEndpoint).Port;
        public IPEndPoint LoopbackEndpoint => new IPEndPoint(IPAddress.Loopback, TcpPort);
        public Task StartBroadcastTask => startBroadcastTask;
        public MatchSessionLifecycle Lifecycle
        {
            get => lifecycle;
            private set => lifecycle = value;
        }
        public MatchSessionHostActor SessionActor => sessionActor;
        public MatchInitializedPayload HostInitialization { get; private set; }
        public string EndedSessionId { get; private set; }
        public static long HostMonotonicNowMs => MonotonicClock.ElapsedMilliseconds;

        public event Action<MatchSessionDispatch> HostDispatchReceived;

        public void Tick()
        {
            while (pendingSnapshots.TryDequeue(out var snapshot))
                publishedSnapshot = snapshot;
            if (sessionActor == null
                || (Lifecycle != MatchSessionLifecycle.Match
                    && Lifecycle != MatchSessionLifecycle.StartCommitted))
            {
                return;
            }
            var dispatches = sessionActor.Tick(MonotonicClock.ElapsedMilliseconds);
            for (var index = 0; index < dispatches.Count; index++)
                ProcessDispatch(dispatches[index]);
            if (sessionActor.Lifecycle == MatchSessionLifecycle.Ended)
            {
                EndedSessionId = sessionActor.SessionId;
                Lifecycle = MatchSessionLifecycle.Ended;
                BeginEndedShutdown();
            }
        }

        public static Task<LanRoomHost> StartAsync(LobbyProfile hostProfile)
        {
            return StartAsync(
                hostProfile,
                LanMatchSessionConfiguration.CreateForTests(),
                CancellationToken.None);
        }

        public static Task<LanRoomHost> StartAsync(
            LobbyProfile hostProfile,
            CancellationToken cancellationToken)
        {
            return StartAsync(
                hostProfile,
                LanMatchSessionConfiguration.CreateForTests(),
                cancellationToken);
        }

        public static Task<LanRoomHost> StartAsync(
            LobbyProfile hostProfile,
            LanMatchSessionConfiguration matchConfiguration,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LanRoomHost(
                hostProfile,
                0,
                false,
                matchConfiguration));
        }

        public static Task<LanRoomHost> StartForTestsAsync(
            LobbyProfile hostProfile,
            int tcpPort)
        {
            return Task.FromResult(new LanRoomHost(
                hostProfile,
                tcpPort,
                true,
                LanMatchSessionConfiguration.CreateForTests()));
        }

        public LanDiscoveryService CreateDiscoveryService()
        {
            lock (gate)
            {
                if (Lifecycle != MatchSessionLifecycle.Lobby)
                    throw new InvalidOperationException("LAN discovery is unavailable after Match start.");
                var snapshot = CreateSnapshotWithLatency();
                var discovery = new LanDiscoveryService(
                    new LobbyDiscoveryEntry(
                        snapshot.RoomCode,
                        snapshot.Members[0].Profile.DisplayName,
                        snapshot.Members.Count,
                        LobbyRoomSnapshot.MaximumMembers,
                        snapshot.Members.Count < LobbyRoomSnapshot.MaximumMembers,
                        TcpPort,
                        snapshot.Revision),
                    RegenerateAuthoritativeRoomCode);
                discoveryServices.Add(discovery);
                return discovery;
            }
        }

        public bool TryStart(string playerId, out LobbyJoinFailure failure)
        {
            Dictionary<string, MatchInitializedPayload> initializations;
            LobbyRoomSnapshot startedSnapshot;
            List<LanDiscoveryService> discoveries;
            lock (gate)
            {
                if (Lifecycle != MatchSessionLifecycle.Lobby)
                {
                    failure = LobbyJoinFailure.RoomStarted;
                    return false;
                }
                var frozen = room.Snapshot;
                var built = MatchSessionBuilder.Create(
                    frozen,
                    matchConfiguration,
                    joinedManifests,
                    MonotonicClock.ElapsedMilliseconds);
                if (!built.Success)
                {
                    failure = built.Failure == MatchSessionStartFailure.AvatarCapacityInsufficient
                        ? LobbyJoinFailure.AvatarCapacityInsufficient
                        : built.Failure == MatchSessionStartFailure.CompatibilityMismatch
                            ? LobbyJoinFailure.CompatibilityMismatch
                            : LobbyJoinFailure.SessionInitializationFailed;
                    return false;
                }
                if (!room.TryStart(playerId, out failure)) return false;

                sessionActor = built.Actor;
                sessionActor.BindFrozenConnection(playerId, "host-local", 1);
                foreach (var guest in guestsByPlayerId.Values)
                    sessionActor.BindFrozenConnection(
                        guest.PlayerId,
                        guest.ConnectionId,
                        1);
                initializations = new Dictionary<string, MatchInitializedPayload>(
                    StringComparer.Ordinal);
                foreach (var participant in sessionActor.Participants.Where(value => value.IsHuman))
                    initializations[participant.PlayerId] =
                        sessionActor.TakeInitialization(participant.PlayerId);
                HostInitialization = initializations[playerId];
                startedSnapshot = CreateSnapshotWithLatency();
                PublishSnapshot(startedSnapshot);
                Lifecycle = MatchSessionLifecycle.StartCommitted;
                discoveries = discoveryServices.ToList();
                discoveryServices.Clear();
            }

            for (var index = 0; index < discoveries.Count; index++)
                discoveries[index].Stop();
            startBroadcastTask = PromoteConnectionsAsync(
                startedSnapshot,
                initializations);
            return true;
        }

        public bool TrySetReady(
            string playerId,
            bool isReady,
            out LobbyJoinFailure failure)
        {
            bool changed;
            LobbyRoomSnapshot snapshot;
            lock (gate)
            {
                if (Lifecycle != MatchSessionLifecycle.Lobby)
                {
                    failure = LobbyJoinFailure.RoomStarted;
                    return false;
                }
                changed = room.TrySetReady(
                    playerId,
                    playerId,
                    isReady,
                    out failure);
                snapshot = changed ? CreateSnapshotWithLatency() : null;
            }
            if (changed)
            {
                PublishSnapshot(snapshot);
                BroadcastLobby(LobbyMessageKind.RoomSnapshot, snapshot);
            }
            return changed;
        }

        public void EnqueueHostCommand(MatchCommandWirePayload command)
        {
            if (sessionActor == null || Lifecycle != MatchSessionLifecycle.Match)
                throw new InvalidOperationException("The Match session is not running.");
            sessionActor.EnqueueHostCommand(command);
        }

        public void EnqueueHostBattleContract(
            MatchWireKind kind,
            object payload)
        {
            if (sessionActor == null
                || Lifecycle != MatchSessionLifecycle.Match)
            {
                throw new InvalidOperationException(
                    "The Match session is not running.");
            }
            sessionActor.EnqueueHostBattleContract(kind, payload);
        }

        public bool PublishBattleSeal(MatchBattleSealPayload seal)
        {
            return ProcessActorDispatches(
                sessionActor?.PublishBattleSeal(seal));
        }

        public bool PublishPlaybackStart(MatchPlaybackStartPayload start)
        {
            return ProcessActorDispatches(
                sessionActor?.PublishPlaybackStart(start));
        }

        public bool CompleteBattleRound(
            IReadOnlyList<MatchBattleResolution> resolutions,
            out string diagnosticCode)
        {
            if (sessionActor == null || Lifecycle != MatchSessionLifecycle.Match)
            {
                diagnosticCode = "match.session.notRunning";
                return false;
            }
            return ProcessActorDispatches(
                sessionActor.CompleteBattleRound(
                    resolutions,
                    MonotonicClock.ElapsedMilliseconds,
                    out diagnosticCode));
        }

        public void AbortMatch(string stableReason = "match.host.explicitQuit")
        {
            if (sessionActor == null || Lifecycle == MatchSessionLifecycle.Ended) return;
            var dispatches = sessionActor.AbortByHost(stableReason);
            for (var index = 0; index < dispatches.Count; index++)
                ProcessDispatch(dispatches[index]);
            EndedSessionId = sessionActor.SessionId;
            Lifecycle = MatchSessionLifecycle.Ended;
            BeginEndedShutdown();
        }

        public async Task StopAsync()
        {
            List<GuestConnection> active;
            List<LanDiscoveryService> discoveries;
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                active = connections.ToList();
                discoveries = discoveryServices.ToList();
                discoveryServices.Clear();
            }
            for (var index = 0; index < discoveries.Count; index++)
                discoveries[index].Stop();
            if (endedShutdownStarted)
            {
                try { await endedFlushTask.ConfigureAwait(false); }
                catch (Exception) { }
            }
            cancellation.Cancel();
            try { listener.Stop(); } catch (Exception) { }
            for (var index = 0; index < active.Count; index++)
                active[index].Close();
            try { await acceptTask.ConfigureAwait(false); }
            catch (Exception) { }
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (Exception) { }
            for (var index = 0; index < active.Count; index++)
                await active[index].StopAsync().ConfigureAwait(false);
            cancellation.Dispose();
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public void SetReadyForTests(string playerId, bool isReady)
        {
            if (!TrySetReady(playerId, isReady, out var failure))
                throw new InvalidOperationException(
                    "Could not update readiness: " + failure + ".");
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested && !endedShutdownStarted)
            {
                try
                {
                    var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    var connection = new GuestConnection(
                        tcpClient,
                        "connection-" + Interlocked.Increment(ref connectionIdCounter));
                    connection.Writer.Faulted += () => RemoveConnection(connection);
                    lock (gate)
                    {
                        if (stopped || endedShutdownStarted)
                        {
                            connection.Close();
                            continue;
                        }
                        connections.Add(connection);
                        connectionsById[connection.ConnectionId] = connection;
                    }
                    connection.ReadTask = Task.Run(
                        () => ReadGuestLoopAsync(connection));
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException)
                {
                    if (cancellation.IsCancellationRequested || endedShutdownStarted)
                        return;
                }
            }
        }

        private async Task ReadGuestLoopAsync(GuestConnection connection)
        {
            try
            {
                while (!cancellation.IsCancellationRequested && !connection.Closed)
                {
                    var readLifecycle = Lifecycle;
                    var frame = await LanFrameTransport.ReadFrameAsync(
                        connection.Stream,
                        cancellation.Token,
                        readLifecycle == MatchSessionLifecycle.Lobby
                            ? LobbyProtocol.MaximumMessageBytes
                            : MatchProtocol.AbsoluteMaximumFrameBytes)
                        .ConfigureAwait(false);
                    if (frame == null) break;
                    var lifecycle = Lifecycle;
                    if (lifecycle == MatchSessionLifecycle.Lobby)
                    {
                        if (!LobbyProtocol.TryDecode(frame, out var lobby, out _))
                            break;
                        await HandleGuestLobbyMessageAsync(connection, lobby)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if (MatchProtocol.TryDecode(
                        frame,
                        MatchWireDirection.ClientToHost,
                        out var match,
                        out _))
                    {
                        ObserveMatchPing(connection, match);
                        sessionActor?.EnqueueRemote(
                            connection.ConnectionId,
                            connection.ConnectionGeneration,
                            connection.PlayerId,
                            match);
                        continue;
                    }
                    if (LobbyProtocol.TryDecode(frame, out var lateLobby, out _))
                    {
                        if (Enum.TryParse(
                            lateLobby.kind,
                            false,
                            out LobbyMessageKind lateKind)
                            && (lateKind == LobbyMessageKind.Ping
                                || lateKind == LobbyMessageKind.Pong)
                            && !string.IsNullOrWhiteSpace(connection.PlayerId))
                        {
                            await HandleGuestLobbyMessageAsync(connection, lateLobby)
                                .ConfigureAwait(false);
                            continue;
                        }
                        if (lateKind == LobbyMessageKind.JoinRequest)
                            await SendLateJoinRejectAsync(connection, lateLobby.playerId)
                                .ConfigureAwait(false);
                    }
                    else
                    {
                        await SendMatchProtocolRejectAsync(connection)
                            .ConfigureAwait(false);
                    }
                    break;
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception) { }
            finally
            {
                RemoveConnection(connection);
            }
        }

        private void ObserveMatchPing(
            GuestConnection connection,
            MatchWireEnvelope envelope)
        {
            if (envelope == null
                || !string.Equals(
                    envelope.Kind,
                    MatchWireKind.Ping.ToString(),
                    StringComparison.Ordinal)
                || !MatchProtocol.TryDeserializePayload(
                    envelope,
                    out MatchHeartbeatPayload ping)
                || ping.ConnectionGeneration
                    != connection.ConnectionGeneration)
            {
                return;
            }
            lock (gate)
            {
                if (connection.Removed
                    || string.IsNullOrWhiteSpace(
                        connection.PlayerId)
                    || !guestsByPlayerId.TryGetValue(
                        connection.PlayerId,
                        out var active)
                    || active != connection)
                {
                    return;
                }
                connection.MissedHeartbeats = 0;
                connection.LatencyMilliseconds = Math.Max(
                    0,
                    DateTimeOffset.UtcNow
                        .ToUnixTimeMilliseconds()
                        - ping.SentUnixMilliseconds);
            }
        }

        private async Task HandleGuestLobbyMessageAsync(
            GuestConnection connection,
            LobbyWireMessage message)
        {
            if (!Enum.TryParse(message.kind, false, out LobbyMessageKind kind))
                return;
            if (kind == LobbyMessageKind.JoinRequest)
            {
                await HandleJoinAsync(connection, message).ConfigureAwait(false);
                return;
            }
            if (!string.Equals(
                connection.PlayerId,
                message.playerId,
                StringComparison.Ordinal))
            {
                return;
            }
            if (kind == LobbyMessageKind.SetReady)
            {
                bool changed;
                LobbyRoomSnapshot snapshot;
                lock (gate)
                {
                    if (Lifecycle != MatchSessionLifecycle.Lobby) return;
                    changed = room.TrySetReady(
                        message.playerId,
                        message.playerId,
                        message.isReady,
                        out _);
                    snapshot = changed ? CreateSnapshotWithLatency() : null;
                }
                if (changed)
                {
                    PublishSnapshot(snapshot);
                    BroadcastLobby(LobbyMessageKind.RoomSnapshot, snapshot);
                }
            }
            else if (kind == LobbyMessageKind.Leave)
            {
                RemoveConnection(connection);
            }
            else if (kind == LobbyMessageKind.Ping)
            {
                SendLobby(connection, CreateLobbyMessage(
                    LobbyMessageKind.Pong,
                    connection.PlayerId,
                    message.sentUnixMilliseconds));
            }
            else if (kind == LobbyMessageKind.Pong)
            {
                lock (gate)
                {
                    connection.MissedHeartbeats = 0;
                    connection.LatencyMilliseconds = Math.Max(
                        0,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            - message.sentUnixMilliseconds);
                    if (Lifecycle == MatchSessionLifecycle.Lobby)
                        PublishSnapshot(CreateSnapshotWithLatency());
                }
            }
            await Task.CompletedTask;
        }

        private async Task HandleJoinAsync(
            GuestConnection connection,
            LobbyWireMessage message)
        {
            LobbyJoinFailure failure;
            LobbyRoomSnapshot snapshot = null;
            lock (gate)
            {
                if (Lifecycle != MatchSessionLifecycle.Lobby)
                {
                    failure = LobbyJoinFailure.RoomStarted;
                }
                else if ((!acceptsAnyRoomCode
                    && !string.Equals(
                        message.roomCode,
                        room.Snapshot.RoomCode,
                        StringComparison.Ordinal))
                    || connection.PlayerId != null)
                {
                    failure = LobbyJoinFailure.InvalidRoomCode;
                }
                else if (!message.CompatibilityManifest.IsValid
                    || !MatchSessionBuilder.ManifestEquals(
                        matchConfiguration.CompatibilityManifest,
                        message.CompatibilityManifest))
                {
                    failure = LobbyJoinFailure.CompatibilityMismatch;
                }
                else if (!matchConfiguration.AllowSyntheticPlayerIdsForTests
                    && !LocalProfileIdentity.IsValid(message.playerId))
                {
                    failure = LobbyJoinFailure.InvalidProfile;
                }
                else
                {
                    var profile = new LobbyProfile(
                        message.playerId,
                        message.displayName,
                        message.avatarIndex);
                    if (room.TryJoin(profile, out failure))
                    {
                        connection.PlayerId = profile.PlayerId;
                        connection.ConnectionGeneration = 1;
                        guestsByPlayerId[profile.PlayerId] = connection;
                        joinedManifests[profile.PlayerId] =
                            message.CompatibilityManifest;
                        snapshot = CreateSnapshotWithLatency();
                        PublishSnapshot(snapshot);
                    }
                }
            }
            if (snapshot == null)
            {
                SendLobby(connection, CreateLobbyMessage(
                    LobbyMessageKind.Reject,
                    message.playerId,
                    0,
                    null,
                    failure.ToString()));
                return;
            }
            SendLobby(connection, CreateLobbyMessage(
                LobbyMessageKind.JoinAccepted,
                connection.PlayerId,
                0,
                snapshot));
            BroadcastLobby(LobbyMessageKind.RoomSnapshot, snapshot);
            await Task.CompletedTask;
        }

        private async Task HeartbeatLoopAsync()
        {
            while (!cancellation.IsCancellationRequested
                && !endedShutdownStarted)
            {
                try
                {
                    await Task.Delay(
                        HeartbeatMilliseconds,
                        cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }

                List<GuestConnection> active;
                List<GuestConnection> expired = null;
                lock (gate)
                {
                    active = guestsByPlayerId.Values.Distinct().ToList();
                    foreach (var connection in active)
                    {
                        connection.MissedHeartbeats++;
                        if (connection.MissedHeartbeats
                            >= MaximumMissedHeartbeats)
                        {
                            if (expired == null) expired = new List<GuestConnection>();
                            expired.Add(connection);
                        }
                    }
                }
                if (expired != null)
                    foreach (var connection in expired) RemoveConnection(connection);

                foreach (var connection in active)
                {
                    if (expired != null && expired.Contains(connection)) continue;
                    if (Lifecycle == MatchSessionLifecycle.Lobby)
                    {
                        if (!SendLobby(connection, CreateLobbyMessage(
                            LobbyMessageKind.Ping,
                            connection.PlayerId,
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())))
                        {
                            RemoveConnection(connection);
                        }
                    }
                }
            }
        }

        private Task PromoteConnectionsAsync(
            LobbyRoomSnapshot startedSnapshot,
            IReadOnlyDictionary<string, MatchInitializedPayload> initializations)
        {
            List<GuestConnection> recipients;
            lock (gate) recipients = guestsByPlayerId.Values.ToList();
            foreach (var recipient in recipients)
            {
                if (!SendLobby(recipient, CreateLobbyMessage(
                    LobbyMessageKind.Start,
                    recipient.PlayerId,
                    0,
                    startedSnapshot))
                    || !initializations.TryGetValue(
                        recipient.PlayerId,
                        out var initialization)
                    || !SendMatch(
                        recipient,
                        MatchWireKind.MatchInitialized,
                        initialization))
                {
                    RemoveConnection(recipient);
                }
            }
            sessionActor.MarkMatchRunning();
            Lifecycle = MatchSessionLifecycle.Match;
            return Task.CompletedTask;
        }

        private void ProcessDispatch(MatchSessionDispatch dispatch)
        {
            if (dispatch == null) return;
            if (string.Equals(
                dispatch.ConnectionId,
                "host-local",
                StringComparison.Ordinal))
            {
                HostDispatchReceived?.Invoke(dispatch);
                return;
            }
            GuestConnection connection;
            lock (gate)
                connectionsById.TryGetValue(dispatch.ConnectionId, out connection);
            if (connection == null) return;
            if (dispatch.Kind == MatchWireKind.ReconnectAccepted
                && dispatch.Payload is MatchReconnectAcceptedPayload accepted)
            {
                GuestConnection previous = null;
                lock (gate)
                {
                    connection.PlayerId = accepted.PlayerId;
                    connection.ConnectionGeneration =
                        accepted.ConnectionGeneration;
                    if (guestsByPlayerId.TryGetValue(
                        accepted.PlayerId,
                        out var existing)
                        && existing != connection)
                    {
                        previous = existing;
                    }
                    guestsByPlayerId[accepted.PlayerId] = connection;
                }
                if (previous != null) previous.Close();
            }
            if (!string.IsNullOrWhiteSpace(dispatch.CloseConnectionId))
            {
                GuestConnection old;
                lock (gate)
                    connectionsById.TryGetValue(
                        dispatch.CloseConnectionId,
                        out old);
                old?.Close();
            }
            if (!SendMatch(
                connection,
                dispatch.Kind,
                dispatch.Payload,
                dispatch.Kind == MatchWireKind.RecoveryState
                    || dispatch.Kind == MatchWireKind.OperationResult
                    || dispatch.Kind == MatchWireKind.SystemResult))
            {
                RemoveConnection(connection);
            }
        }

        private bool ProcessActorDispatches(
            IReadOnlyList<MatchSessionDispatch> items)
        {
            if (items == null || items.Count == 0) return false;
            for (var index = 0; index < items.Count; index++)
                ProcessDispatch(items[index]);
            return true;
        }

        private void BroadcastLobby(
            LobbyMessageKind kind,
            LobbyRoomSnapshot snapshot)
        {
            List<GuestConnection> recipients;
            lock (gate) recipients = guestsByPlayerId.Values.ToList();
            foreach (var recipient in recipients)
            {
                if (!SendLobby(recipient, CreateLobbyMessage(
                    kind,
                    recipient.PlayerId,
                    0,
                    snapshot)))
                {
                    RemoveConnection(recipient);
                }
            }
        }

        private bool SendLobby(
            GuestConnection connection,
            LobbyWireMessage message)
        {
            try
            {
                return connection.Writer.TryEnqueue(
                    LobbyProtocol.Encode(message));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool SendMatch(
            GuestConnection connection,
            MatchWireKind kind,
            object payload,
            bool replacePendingSnapshot = false)
        {
            try
            {
                var sessionId = sessionActor == null
                    ? EndedSessionId
                    : sessionActor.SessionId;
                var frame = MatchProtocol.Encode(
                    kind,
                    sessionId,
                    "host-" + Interlocked.Increment(ref messageIdCounter),
                    payload,
                    MatchWireDirection.HostToClient);
                return connection.Writer.TryEnqueue(
                    frame,
                    replacePendingSnapshot);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task SendLateJoinRejectAsync(
            GuestConnection connection,
            string playerId)
        {
            SendLobby(connection, CreateLobbyMessage(
                LobbyMessageKind.Reject,
                string.IsNullOrWhiteSpace(playerId) ? "unknown" : playerId,
                0,
                null,
                LobbyJoinFailure.RoomStarted.ToString()));
            await connection.Writer.CompleteAsync(
                TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        private async Task SendMatchProtocolRejectAsync(
            GuestConnection connection)
        {
            SendMatch(connection, MatchWireKind.Reject, new MatchRejectPayload
            {
                Code = "ProtocolError",
                StableDetailCode = "match.protocol.invalid"
            });
            await connection.Writer.CompleteAsync(
                TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        private void RemoveConnection(GuestConnection connection)
        {
            bool publishLobby = false;
            LobbyRoomSnapshot lobbySnapshot = null;
            string playerId;
            long generation;
            lock (gate)
            {
                if (connection == null || connection.Removed) return;
                connection.Removed = true;
                connections.Remove(connection);
                connectionsById.Remove(connection.ConnectionId);
                playerId = connection.PlayerId;
                generation = connection.ConnectionGeneration;
                if (!string.IsNullOrWhiteSpace(playerId)
                    && guestsByPlayerId.TryGetValue(
                        playerId,
                        out var active)
                    && active == connection)
                {
                    guestsByPlayerId.Remove(playerId);
                    if (Lifecycle == MatchSessionLifecycle.Lobby)
                    {
                        joinedManifests.Remove(playerId);
                        publishLobby = room.RemovePlayer(playerId);
                        if (publishLobby)
                            lobbySnapshot = CreateSnapshotWithLatency();
                    }
                }
            }
            connection.Close();
            if (Lifecycle != MatchSessionLifecycle.Lobby
                && Lifecycle != MatchSessionLifecycle.Ended
                && !string.IsNullOrWhiteSpace(playerId))
            {
                sessionActor?.EnqueueConnectionLost(
                    connection.ConnectionId,
                    generation,
                    playerId);
            }
            if (publishLobby)
            {
                PublishSnapshot(lobbySnapshot);
                if (!stopped)
                    BroadcastLobby(
                        LobbyMessageKind.RoomSnapshot,
                        lobbySnapshot);
            }
        }

        private LobbyRoomSnapshot CreateSnapshotWithLatency()
        {
            var source = room.Snapshot;
            var members = new LobbyMemberSnapshot[source.Members.Count];
            for (var index = 0; index < members.Length; index++)
            {
                var member = source.Members[index];
                var latency = guestsByPlayerId.TryGetValue(
                    member.PlayerId,
                    out var connection)
                    ? connection.LatencyMilliseconds
                    : 0;
                members[index] = new LobbyMemberSnapshot(
                    member.Profile,
                    member.IsReady,
                    latency);
            }
            return new LobbyRoomSnapshot(
                source.RoomCode,
                source.HostPlayerId,
                members,
                source.HasStarted,
                source.Revision);
        }

        private string RegenerateAuthoritativeRoomCode()
        {
            LobbyRoomSnapshot before;
            string replacement;
            lock (gate)
            {
                before = room.Snapshot;
                if (Lifecycle != MatchSessionLifecycle.Lobby
                    || before.Members.Count == 0)
                {
                    return before.RoomCode;
                }
                do { replacement = CreateRoomCode(); }
                while (string.Equals(
                    replacement,
                    before.RoomCode,
                    StringComparison.Ordinal));
                room = RecreateRoomWithCode(before, replacement);
                PublishSnapshot(CreateSnapshotWithLatency());
            }
            BroadcastLobby(
                LobbyMessageKind.RoomSnapshot,
                CreateSnapshotWithLatency());
            return replacement;
        }

        private static LobbyRoomState RecreateRoomWithCode(
            LobbyRoomSnapshot snapshot,
            string roomCode)
        {
            var replacement = LobbyRoomState.CreateHost(
                snapshot.Members[0].Profile,
                roomCode);
            for (var index = 1; index < snapshot.Members.Count; index++)
                replacement.TryJoin(
                    snapshot.Members[index].Profile,
                    out _);
            foreach (var member in snapshot.Members)
                if (member.IsReady)
                    replacement.TrySetReady(
                        member.PlayerId,
                        member.PlayerId,
                        true,
                        out _);
            return replacement;
        }

        private LobbyWireMessage CreateLobbyMessage(
            LobbyMessageKind kind,
            string playerId,
            long sentUnixMilliseconds,
            LobbyRoomSnapshot snapshot = null,
            string rejection = null)
        {
            return new LobbyWireMessage
            {
                protocolVersion = LobbyProtocol.ProtocolVersion,
                kind = kind.ToString(),
                roomCode = room.Snapshot.RoomCode,
                playerId = playerId,
                sentUnixMilliseconds = sentUnixMilliseconds,
                snapshotJson = snapshot == null
                    ? null
                    : LanRoomSnapshotWire.Serialize(snapshot),
                rejectionCode = rejection
            };
        }

        private void PublishSnapshot(LobbyRoomSnapshot snapshot)
        {
            if (snapshot != null) pendingSnapshots.Enqueue(snapshot);
        }

        private void BeginEndedShutdown()
        {
            lock (gate)
            {
                if (endedShutdownStarted) return;
                endedShutdownStarted = true;
            }
            try { listener.Stop(); } catch (Exception) { }
            List<GuestConnection> active;
            lock (gate) active = connections.ToList();
            endedFlushTask = Task.Run(async () =>
            {
                foreach (var connection in active)
                {
                    await connection.Writer.CompleteAsync(
                        TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                    connection.Close();
                }
            });
        }

        private static string CreateRoomCode()
        {
            return new Random().Next(0, 1000000).ToString("D6");
        }

        private sealed class GuestConnection
        {
            public GuestConnection(TcpClient client, string connectionId)
            {
                Client = client;
                Stream = client.GetStream();
                ConnectionId = connectionId;
                Writer = new LanConnectionWriter(Stream);
                ReadTask = Task.CompletedTask;
            }

            public TcpClient Client { get; }
            public NetworkStream Stream { get; }
            public LanConnectionWriter Writer { get; }
            public string ConnectionId { get; }
            public string PlayerId { get; set; }
            public long ConnectionGeneration { get; set; }
            public long LatencyMilliseconds { get; set; }
            public int MissedHeartbeats { get; set; }
            public Task ReadTask { get; set; }
            public bool Removed { get; set; }
            public bool Closed { get; private set; }

            public void Close()
            {
                if (Closed) return;
                Closed = true;
                Writer.Cancel();
                try { Client.Close(); } catch (Exception) { }
            }

            public async Task StopAsync()
            {
                Close();
                try { await ReadTask.ConfigureAwait(false); }
                catch (Exception) { }
                Writer.Dispose();
            }
        }
    }
}
