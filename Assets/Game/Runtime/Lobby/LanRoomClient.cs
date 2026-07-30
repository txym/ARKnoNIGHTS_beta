using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArknoNights.Match;

namespace ArknoNights.Lobby
{
    public sealed class MatchReconnectRejectedException : InvalidOperationException
    {
        public MatchReconnectRejectedException(
            MatchReconnectRejectCode code,
            string stableDetailCode)
            : base(stableDetailCode)
        {
            Code = code;
        }

        public MatchReconnectRejectCode Code { get; }
    }

    public sealed class LanRoomClient : IDisposable
    {
        private const int HeartbeatMilliseconds = 1000;
        private readonly TcpClient tcpClient;
        private readonly NetworkStream stream;
        private readonly LobbyProfile profile;
        private readonly string requestedRoomCode;
        private readonly MatchCompatibilityManifest expectedManifest;
        private readonly IReconnectCredentialStore credentialStore;
        private readonly IPEndPoint endpoint;
        private readonly CancellationTokenSource cancellation =
            new CancellationTokenSource();
        private readonly LanConnectionWriter writer;
        private readonly TaskCompletionSource<LobbyRoomSnapshot> joinCompletion =
            new TaskCompletionSource<LobbyRoomSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> startCompletion =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<MatchInitializedPayload> matchInitializationCompletion =
            new TaskCompletionSource<MatchInitializedPayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<MatchReconnectAcceptedPayload> reconnectCompletion =
            new TaskCompletionSource<MatchReconnectAcceptedPayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<ClientEvent> receivedEvents =
            new ConcurrentQueue<ClientEvent>();
        private readonly MatchResultClientState matchSnapshotState =
            new MatchResultClientState();
        private readonly Task readTask;
        private readonly Task heartbeatTask;
        private readonly bool reconnectingConnection;
        private ReconnectCredential reconnectCredential;
        private LobbyRoomSnapshot snapshot;
        private MatchInitializedPayload matchInitialization;
        private MatchOperationResultPayload lastOperationResult;
        private MatchSystemResultPayload lastSystemResult;
        private MatchClockSyncPayload currentClock;
        private MatchBattleSealPayload currentBattleSeal;
        private MatchPlaybackStartPayload currentPlaybackStart;
        private long latencyMilliseconds = -1;
        private long messageId;
        private int missedMatchPongs;
        private bool recoveryRequested;
        private volatile bool matchMode;
        private volatile bool disconnected;
        private volatile bool ended;
        private bool stopped;

        private LanRoomClient(
            TcpClient tcpClient,
            LobbyProfile profile,
            string requestedRoomCode,
            MatchCompatibilityManifest expectedManifest,
            IReconnectCredentialStore credentialStore,
            IPEndPoint endpoint,
            bool matchMode,
            ReconnectCredential reconnectCredential)
        {
            this.tcpClient = tcpClient;
            stream = tcpClient.GetStream();
            writer = new LanConnectionWriter(stream);
            writer.Faulted += () =>
            {
                disconnected = true;
                try { tcpClient.Close(); } catch (Exception) { }
            };
            this.profile = profile;
            this.requestedRoomCode = requestedRoomCode;
            this.expectedManifest = expectedManifest;
            this.credentialStore = credentialStore;
            this.endpoint = endpoint;
            this.matchMode = matchMode;
            this.reconnectCredential = reconnectCredential;
            reconnectingConnection = matchMode;
            readTask = Task.Run(ReadLoopAsync);
            heartbeatTask = Task.Run(HeartbeatLoopAsync);
        }

        public LobbyRoomSnapshot Snapshot => snapshot;
        public long LatencyMilliseconds => latencyMilliseconds;
        public bool IsConnected => !stopped
            && !disconnected
            && tcpClient.Connected;
        public bool IsReconnecting => matchMode && disconnected && !ended;
        public bool HasEnteredMatch => matchInitialization != null
            || reconnectCredential != null;
        public bool HasEnded => ended;
        public MatchInitializedPayload MatchInitialization => matchInitialization;
        public ScopedSnapshotPayload MatchSnapshot => matchSnapshotState.Current;
        public MatchOperationResultPayload LastOperationResult =>
            lastOperationResult;
        public MatchSystemResultPayload LastSystemResult =>
            lastSystemResult;
        public MatchClockSyncPayload CurrentClock => currentClock;
        public MatchBattleSealPayload CurrentBattleSeal => currentBattleSeal;
        public MatchPlaybackStartPayload CurrentPlaybackStart => currentPlaybackStart;
        public long ConnectionGeneration { get; private set; }
        public string SessionId { get; private set; }

        public event Action<ScopedSnapshotPayload> MatchSnapshotChanged;
        public event Action RecoveryStateApplied;
        public event Action<MatchOperationResultPayload> OperationResultReceived;
        public event Action<MatchSystemResultPayload> SystemResultReceived;
        public event Action<MatchEndedPayload> MatchEnded;
        public event Action<MatchClockSyncPayload> ClockSynchronized;
        public event Action<MatchBattleSealPayload> BattleSealReceived;
        public event Action<MatchPlaybackStartPayload> PlaybackStarted;
        public event Action Reconnecting;

        public void Tick()
        {
            while (receivedEvents.TryDequeue(out var received))
            {
                if (received.LobbySnapshot != null)
                    snapshot = received.LobbySnapshot;
                if (received.LatencyMilliseconds >= 0)
                    latencyMilliseconds = received.LatencyMilliseconds;
                if (received.Initialization != null)
                {
                    matchInitialization = received.Initialization;
                    SessionId = received.Initialization.Snapshot.SessionId;
                    ConnectionGeneration =
                        received.Initialization.ConnectionGeneration;
                    reconnectCredential = new ReconnectCredential(
                        SessionId,
                        endpoint.Address.ToString(),
                        endpoint.Port,
                        profile.PlayerId,
                        received.Initialization.ReconnectToken,
                        received.Initialization.Manifest.ToDomain());
                    credentialStore.Save(reconnectCredential);
                    ApplyRecovery(received.Initialization.Snapshot);
                }
                if (received.ReconnectAccepted != null)
                {
                    SessionId = reconnectCredential.SessionId;
                    ConnectionGeneration =
                        received.ReconnectAccepted.ConnectionGeneration;
                    credentialStore.Save(reconnectCredential);
                }
                if (received.RecoveryState != null)
                    ApplyRecovery(received.RecoveryState);
                if (received.OperationResult != null)
                {
                    ApplyDelta(received.OperationResult.Delta);
                    lastOperationResult = received.OperationResult;
                    OperationResultReceived?.Invoke(
                        received.OperationResult);
                }
                if (received.SystemResult != null)
                {
                    ApplyDelta(received.SystemResult.Delta);
                    lastSystemResult = received.SystemResult;
                    SystemResultReceived?.Invoke(received.SystemResult);
                    ApplySystemPayloads(received.SystemResult);
                }
                if (received.Clock != null)
                {
                    currentClock = received.Clock;
                    ClockSynchronized?.Invoke(received.Clock);
                }
                if (received.Disconnected && !ended)
                    Reconnecting?.Invoke();
            }
        }

        public static Task<LanRoomClient> JoinAsync(
            IPEndPoint endpoint,
            string roomCode,
            LobbyProfile profile)
        {
            return JoinAsync(
                endpoint,
                roomCode,
                profile,
                LanMatchSessionConfiguration.CreateForTests(),
                new VolatileReconnectCredentialStore());
        }

        public static async Task<LanRoomClient> JoinAsync(
            IPEndPoint endpoint,
            string roomCode,
            LobbyProfile profile,
            LanMatchSessionConfiguration matchConfiguration,
            IReconnectCredentialStore credentialStore)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (!LobbyRoomCode.IsValid(roomCode))
                throw new ArgumentException(
                    "The room code must contain exactly six digits.",
                    nameof(roomCode));
            if (profile == null || !profile.IsValid())
                throw new ArgumentException(
                    "The client profile is invalid.",
                    nameof(profile));
            if (matchConfiguration == null || !matchConfiguration.IsValid)
                throw new ArgumentException(
                    "The Match session configuration is invalid.",
                    nameof(matchConfiguration));
            if (credentialStore == null)
                throw new ArgumentNullException(nameof(credentialStore));

            var tcpClient = new TcpClient(endpoint.AddressFamily);
            await tcpClient.ConnectAsync(
                endpoint.Address,
                endpoint.Port).ConfigureAwait(false);
            var client = new LanRoomClient(
                tcpClient,
                profile,
                roomCode,
                matchConfiguration.CompatibilityManifest,
                credentialStore,
                endpoint,
                false,
                null);
            try
            {
                client.SendLobby(new LobbyWireMessage
                {
                    protocolVersion = LobbyProtocol.ProtocolVersion,
                    kind = LobbyMessageKind.JoinRequest.ToString(),
                    roomCode = roomCode,
                    playerId = profile.PlayerId,
                    displayName = profile.DisplayName,
                    avatarIndex = profile.AvatarIndex,
                    matchProtocolVersion =
                        matchConfiguration.CompatibilityManifest.ProtocolVersion,
                    matchRulesVersion =
                        matchConfiguration.CompatibilityManifest.MatchRulesVersion,
                    battleCoreVersion =
                        matchConfiguration.CompatibilityManifest.BattleCoreVersion,
                    unitCatalogSha256 =
                        matchConfiguration.CompatibilityManifest.UnitCatalogSha256,
                    abilityCatalogSha256 =
                        matchConfiguration.CompatibilityManifest.AbilityCatalogSha256
                });
                var completed = await Task.WhenAny(
                    client.joinCompletion.Task,
                    Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                if (completed != client.joinCompletion.Task)
                    throw new TimeoutException(
                        "The LAN room did not accept the join request.");
                await client.joinCompletion.Task.ConfigureAwait(false);
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public static Task<LanRoomClient> JoinAsync(
            IPEndPoint endpoint,
            LobbyDiscoveryEntry entry,
            LobbyProfile profile)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            return JoinAsync(endpoint, entry.RoomCode, profile);
        }

        public static Task<LanRoomClient> JoinForTestsAsync(
            IPEndPoint endpoint,
            LobbyProfile profile)
        {
            return JoinAsync(endpoint, "000000", profile);
        }

        public static async Task<LanRoomClient> ReconnectAsync(
            ReconnectCredential credential,
            IReconnectCredentialStore credentialStore,
            ScopedSnapshotPayload retainedSnapshot = null)
        {
            if (credential == null || !credential.IsValid)
                throw new ArgumentException(
                    "The reconnect credential is invalid.",
                    nameof(credential));
            if (credentialStore == null)
                throw new ArgumentNullException(nameof(credentialStore));
            var addresses = await Dns.GetHostAddressesAsync(
                credential.HostAddress).ConfigureAwait(false);
            if (addresses.Length == 0)
                throw new SocketException((int)SocketError.HostNotFound);
            var endpoint = new IPEndPoint(
                addresses[0],
                credential.HostPort);
            var tcpClient = new TcpClient(endpoint.AddressFamily);
            await tcpClient.ConnectAsync(
                endpoint.Address,
                endpoint.Port).ConfigureAwait(false);
            var profile = new LobbyProfile(
                credential.PlayerId,
                LobbyProfile.DisplayNameForAvatar(0),
                0);
            var client = new LanRoomClient(
                tcpClient,
                profile,
                "000000",
                credential.CompatibilityManifest,
                credentialStore,
                endpoint,
                true,
                credential);
            if (retainedSnapshot != null)
                client.matchSnapshotState.TryApplyRecovery(
                    retainedSnapshot);
            client.SessionId = credential.SessionId;
            try
            {
                client.SendMatch(
                    MatchWireKind.ReconnectRequest,
                    new MatchReconnectRequestPayload
                    {
                        PlayerId = credential.PlayerId,
                        RawToken = credential.Token,
                        Manifest = MatchCompatibilityWire.FromDomain(
                            credential.CompatibilityManifest),
                        ClientLastAppliedRevision =
                            retainedSnapshot?.StateRevision ?? 0
                    });
                var completed = await Task.WhenAny(
                    client.reconnectCompletion.Task,
                    Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                if (completed != client.reconnectCompletion.Task)
                    throw new TimeoutException(
                        "The host did not answer the reconnect request.");
                await client.reconnectCompletion.Task.ConfigureAwait(false);
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public Task SetReadyAsync(bool isReady)
        {
            if (matchMode)
                throw new InvalidOperationException(
                    "Lobby readiness is unavailable after Match start.");
            SendLobby(CreateLobbyMessage(
                LobbyMessageKind.SetReady,
                isReady));
            return Task.CompletedTask;
        }

        public Task SendCommandAsync(MatchCommandWirePayload command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!matchMode || disconnected || ended)
                throw new InvalidOperationException(
                    "The Match connection is not available for commands.");
            command.PlayerId = profile.PlayerId;
            command.ConnectionGeneration = ConnectionGeneration;
            SendMatch(MatchWireKind.OperationRequest, command);
            return Task.CompletedTask;
        }

        public Task RequestSnapshotAsync()
        {
            if (!matchMode || disconnected || ended)
                throw new InvalidOperationException(
                    "The Match connection is unavailable.");
            recoveryRequested = true;
            SendMatch(
                MatchWireKind.RecoveryStateRequest,
                new MatchSnapshotRequestPayload
            {
                ClientLastAppliedRevision =
                    matchSnapshotState.Current?.StateRevision ?? 0
            });
            return Task.CompletedTask;
        }

        public Task SendFirstChunkReadyAsync(
            MatchFirstChunkReadyPayload payload)
        {
            SendMatch(MatchWireKind.FirstChunkReady, payload);
            return Task.CompletedTask;
        }

        public Task SendFinalSecondHashAsync(
            MatchFinalSecondHashPayload payload)
        {
            SendMatch(MatchWireKind.FinalSecondHash, payload);
            return Task.CompletedTask;
        }

        public Task ReportBattleFailureAsync(
            MatchClientBattleFailurePayload payload)
        {
            SendMatch(MatchWireKind.ClientBattleFailure, payload);
            return Task.CompletedTask;
        }

        public async Task LeaveAsync()
        {
            if (!stopped)
            {
                if (matchMode && !ended)
                {
                    credentialStore.Clear();
                    if (!disconnected)
                    {
                        try
                        {
                            SendMatch(
                                MatchWireKind.ExplicitQuit,
                                new MatchExplicitQuitPayload
                                {
                                    PlayerId = profile.PlayerId,
                                    ConnectionGeneration = ConnectionGeneration
                                });
                            await writer.CompleteAsync(
                                TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                        }
                        catch (Exception) { }
                    }
                }
                else if (!matchMode)
                {
                    try
                    {
                        SendLobby(CreateLobbyMessage(
                            LobbyMessageKind.Leave,
                            false));
                    }
                    catch (Exception) { }
                }
            }
            await StopAsync().ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            if (stopped) return;
            stopped = true;
            cancellation.Cancel();
            writer.Cancel();
            try { tcpClient.Close(); } catch (Exception) { }
            try { await readTask.ConfigureAwait(false); }
            catch (Exception) { }
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (Exception) { }
            writer.Dispose();
            cancellation.Dispose();
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public async Task WaitForStartAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(
                startCompletion.Task,
                Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != startCompletion.Task)
                throw new TimeoutException(
                    "The host did not start the room before the timeout.");
            await startCompletion.Task.ConfigureAwait(false);
        }

        public async Task<MatchInitializedPayload> WaitForMatchInitializedAsync(
            TimeSpan timeout)
        {
            var completed = await Task.WhenAny(
                matchInitializationCompletion.Task,
                Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != matchInitializationCompletion.Task)
                throw new TimeoutException(
                    "The host did not initialize the Match before the timeout.");
            return await matchInitializationCompletion.Task.ConfigureAwait(false);
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var readingMatchFrame = matchMode;
                    var frame = await LanFrameTransport.ReadFrameAsync(
                        stream,
                        cancellation.Token,
                        readingMatchFrame
                            ? MatchProtocol.AbsoluteMaximumFrameBytes
                            : LobbyProtocol.MaximumMessageBytes)
                        .ConfigureAwait(false);
                    if (frame == null) break;
                    if (!matchMode)
                    {
                        if (!LobbyProtocol.TryDecode(
                            frame,
                            out var lobbyMessage,
                            out var lobbyError))
                        {
                            throw new InvalidDataException(
                                "lobby.protocol." + lobbyError);
                        }
                        await HandleLobbyMessageAsync(
                            lobbyMessage).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!MatchProtocol.TryDecode(
                            frame,
                            MatchWireDirection.HostToClient,
                            out var matchMessage,
                            out var matchError))
                        {
                            if (LobbyProtocol.TryDecode(
                                frame,
                                out var trailingLobbyMessage,
                                out _)
                                && Enum.TryParse(
                                    trailingLobbyMessage.kind,
                                    false,
                                    out LobbyMessageKind trailingKind)
                                && (trailingKind == LobbyMessageKind.Ping
                                    || trailingKind == LobbyMessageKind.Pong))
                            {
                                await HandleLobbyMessageAsync(
                                    trailingLobbyMessage).ConfigureAwait(false);
                                continue;
                            }
                            throw new InvalidDataException(
                                "match.protocol." + matchError);
                        }
                        await HandleMatchMessageAsync(
                            matchMessage).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception exception)
            {
                if (!stopped && !matchMode)
                    joinCompletion.TrySetException(exception);
                if (!stopped && reconnectingConnection)
                    reconnectCompletion.TrySetException(exception);
            }
            finally
            {
                disconnected = true;
                if (matchMode && !stopped && !ended)
                    receivedEvents.Enqueue(ClientEvent.DisconnectedEvent());
            }
        }

        private async Task HandleLobbyMessageAsync(
            LobbyWireMessage message)
        {
            if (!Enum.TryParse(
                message.kind,
                false,
                out LobbyMessageKind kind))
            {
                return;
            }
            if (kind == LobbyMessageKind.JoinAccepted
                || kind == LobbyMessageKind.RoomSnapshot
                || kind == LobbyMessageKind.Start)
            {
                if (LanRoomSnapshotWire.TryDeserialize(
                    message.snapshotJson,
                    out var roomSnapshot))
                {
                    receivedEvents.Enqueue(
                        ClientEvent.ForLobby(roomSnapshot));
                    if (kind == LobbyMessageKind.JoinAccepted)
                        joinCompletion.TrySetResult(roomSnapshot);
                    if (kind == LobbyMessageKind.Start
                        || roomSnapshot.HasStarted)
                    {
                        matchMode = true;
                        startCompletion.TrySetResult(true);
                    }
                }
            }
            else if (kind == LobbyMessageKind.Ping)
            {
                SendLobby(CreateLobbyMessage(
                    LobbyMessageKind.Pong,
                    false,
                    message.sentUnixMilliseconds));
            }
            else if (kind == LobbyMessageKind.Pong)
            {
                receivedEvents.Enqueue(ClientEvent.ForLatency(
                    Math.Max(
                        0,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            - message.sentUnixMilliseconds)));
            }
            else if (kind == LobbyMessageKind.Reject)
            {
                joinCompletion.TrySetException(
                    new InvalidOperationException(message.rejectionCode));
            }
            await Task.CompletedTask;
        }

        private async Task HandleMatchMessageAsync(
            MatchWireEnvelope envelope)
        {
            if (!Enum.TryParse(
                envelope.Kind,
                false,
                out MatchWireKind kind))
            {
                return;
            }
            if (kind == MatchWireKind.MatchInitialized
                && MatchProtocol.TryDeserializePayload(
                    envelope,
                    out MatchInitializedPayload initialization))
            {
                if (!string.Equals(
                    envelope.SessionId,
                    initialization.Snapshot.SessionId,
                    StringComparison.Ordinal)
                    || !MatchSessionBuilder.ManifestEquals(
                        expectedManifest,
                        initialization.Manifest.ToDomain()))
                {
                    throw new InvalidDataException(
                        "match.initialize.compatibility.invalid");
                }
                SessionId = envelope.SessionId;
                ConnectionGeneration = initialization.ConnectionGeneration;
                receivedEvents.Enqueue(
                    ClientEvent.ForInitialization(initialization));
                matchInitializationCompletion.TrySetResult(initialization);
            }
            else if (kind == MatchWireKind.OperationResult
                && MatchProtocol.TryDeserializePayload(
                    envelope,
                    out MatchOperationResultPayload operationResult))
            {
                receivedEvents.Enqueue(
                    ClientEvent.ForOperationResult(operationResult));
            }
            else if (kind == MatchWireKind.SystemResult
                && MatchProtocol.TryDeserializePayload(
                    envelope,
                    out MatchSystemResultPayload systemResult))
            {
                receivedEvents.Enqueue(
                    ClientEvent.ForSystemResult(systemResult));
            }
            else if (kind == MatchWireKind.RecoveryState
                && MatchProtocol.TryDeserializePayload(
                    envelope,
                    out ScopedSnapshotPayload scoped))
            {
                if (!string.Equals(
                    envelope.SessionId,
                    scoped.SessionId,
                    StringComparison.Ordinal)
                    || (!string.IsNullOrWhiteSpace(SessionId)
                        && !string.Equals(
                            envelope.SessionId,
                            SessionId,
                            StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(
                        "match.snapshot.session.invalid");
                }
                receivedEvents.Enqueue(
                    ClientEvent.ForRecoveryState(scoped));
            }
            else if (kind == MatchWireKind.ReconnectAccepted
                && MatchProtocol.TryDeserializePayload(
                    envelope,
                    out MatchReconnectAcceptedPayload accepted))
            {
                ConnectionGeneration = accepted.ConnectionGeneration;
                receivedEvents.Enqueue(
                    ClientEvent.ForReconnectAccepted(accepted));
                reconnectCompletion.TrySetResult(accepted);
            }
            else if (kind == MatchWireKind.ReconnectRejected
                && MatchProtocol.TryDeserializePayload(
                    envelope,
                    out MatchReconnectRejectedPayload rejected)
                && Enum.TryParse(
                    rejected.Code,
                    false,
                    out MatchReconnectRejectCode rejectCode))
            {
                reconnectCompletion.TrySetException(
                    new MatchReconnectRejectedException(
                        rejectCode,
                        rejected.StableDetailCode));
            }
            else if (kind == MatchWireKind.Pong
                && MatchProtocol.TryDeserializePayload(
                    envelope,
                    out MatchHeartbeatPayload pong)
                && pong.ConnectionGeneration == ConnectionGeneration)
            {
                Interlocked.Exchange(ref missedMatchPongs, 0);
                receivedEvents.Enqueue(ClientEvent.ForLatency(
                    Math.Max(
                        0,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            - pong.SentUnixMilliseconds)));
                receivedEvents.Enqueue(ClientEvent.ForClock(
                    new MatchClockSyncPayload
                    {
                        RoundNumber = pong.RoundNumber,
                        Phase = pong.Phase,
                        HostMonotonicNowMs =
                            pong.HostMonotonicNowMs,
                        PreparationDeadlineHostMonotonicMs =
                            pong.PhaseDeadlineHostMonotonicMs
                    }));
            }
            await Task.CompletedTask;
        }

        private async Task HeartbeatLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        HeartbeatMilliseconds,
                        cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                try
                {
                    if (!matchMode)
                    {
                        SendLobby(CreateLobbyMessage(
                            LobbyMessageKind.Ping,
                            false,
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                    }
                    else if (ConnectionGeneration > 0 && !ended)
                    {
                        if (Interlocked.Increment(
                                ref missedMatchPongs)
                            >= 3)
                        {
                            disconnected = true;
                            try { tcpClient.Close(); }
                            catch (Exception) { }
                            return;
                        }
                        SendMatch(
                            MatchWireKind.Ping,
                            new MatchHeartbeatPayload
                            {
                                ConnectionGeneration = ConnectionGeneration,
                                SentUnixMilliseconds =
                                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            });
                    }
                }
                catch (Exception) { return; }
            }
        }

        private void ApplyRecovery(ScopedSnapshotPayload scoped)
        {
            if (!MatchSnapshotWireProjector.ContainsOnlyRecipientPrivateState(
                scoped,
                profile.PlayerId))
            {
                throw new InvalidDataException(
                    "match.snapshot.privacy.invalid");
            }
            if (matchSnapshotState.TryApplyRecovery(scoped))
            {
                recoveryRequested = false;
                MatchSnapshotChanged?.Invoke(scoped);
                RecoveryStateApplied?.Invoke();
            }
        }

        private void ApplyDelta(MatchStateDeltaWire delta)
        {
            if (delta == null) return;
            var status = matchSnapshotState.TryApply(delta);
            if (status == MatchDeltaApplyStatus.Applied)
            {
                var current = matchSnapshotState.Current;
                if (!MatchSnapshotWireProjector
                    .ContainsOnlyRecipientPrivateState(
                        current,
                        profile.PlayerId))
                {
                    throw new InvalidDataException(
                        "match.delta.privacy.invalid");
                }
                MatchSnapshotChanged?.Invoke(current);
                return;
            }
            if ((status
                    == MatchDeltaApplyStatus.RecoveryRequired
                 || status == MatchDeltaApplyStatus.Invalid)
                && !recoveryRequested
                && matchMode
                && !disconnected
                && !ended)
            {
                _ = RequestSnapshotAsync();
            }
        }

        private void ApplySystemPayloads(
            MatchSystemResultPayload result)
        {
            if (result.BattleSeal != null)
            {
                currentBattleSeal = result.BattleSeal;
                BattleSealReceived?.Invoke(result.BattleSeal);
            }
            if (result.PlaybackStart != null)
            {
                currentPlaybackStart = result.PlaybackStart;
                PlaybackStarted?.Invoke(result.PlaybackStart);
            }
            if (result.MatchEnded != null && !ended)
            {
                ended = true;
                credentialStore.Clear();
                MatchEnded?.Invoke(result.MatchEnded);
            }
        }

        private LobbyWireMessage CreateLobbyMessage(
            LobbyMessageKind kind,
            bool isReady,
            long sentUnixMilliseconds = 0)
        {
            var roomCode = snapshot == null
                ? requestedRoomCode
                : snapshot.RoomCode;
            return new LobbyWireMessage
            {
                protocolVersion = LobbyProtocol.ProtocolVersion,
                kind = kind.ToString(),
                roomCode = roomCode,
                playerId = profile.PlayerId,
                isReady = isReady,
                sentUnixMilliseconds = sentUnixMilliseconds
            };
        }

        private void SendLobby(LobbyWireMessage message)
        {
            if (!writer.TryEnqueue(LobbyProtocol.Encode(message)))
            {
                disconnected = true;
                throw new IOException("lobby.writer.queue.full");
            }
        }

        private void SendMatch(MatchWireKind kind, object payload)
        {
            var sessionId = SessionId
                ?? reconnectCredential?.SessionId
                ?? matchInitialization?.Snapshot?.SessionId;
            var frame = MatchProtocol.Encode(
                kind,
                sessionId,
                "client-" + Interlocked.Increment(ref messageId),
                payload,
                MatchWireDirection.ClientToHost);
            if (!writer.TryEnqueue(frame))
            {
                disconnected = true;
                throw new IOException("match.writer.queue.full");
            }
        }

        private sealed class ClientEvent
        {
            private ClientEvent() { }

            public LobbyRoomSnapshot LobbySnapshot { get; private set; }
            public long LatencyMilliseconds { get; private set; } = -1;
            public MatchInitializedPayload Initialization { get; private set; }
            public MatchReconnectAcceptedPayload ReconnectAccepted { get; private set; }
            public ScopedSnapshotPayload RecoveryState { get; private set; }
            public MatchOperationResultPayload OperationResult { get; private set; }
            public MatchSystemResultPayload SystemResult { get; private set; }
            public MatchClockSyncPayload Clock { get; private set; }
            public bool Disconnected { get; private set; }

            public static ClientEvent ForLobby(LobbyRoomSnapshot value) =>
                new ClientEvent { LobbySnapshot = value };
            public static ClientEvent ForLatency(long value) =>
                new ClientEvent { LatencyMilliseconds = value };
            public static ClientEvent ForInitialization(MatchInitializedPayload value) =>
                new ClientEvent { Initialization = value };
            public static ClientEvent ForReconnectAccepted(MatchReconnectAcceptedPayload value) =>
                new ClientEvent { ReconnectAccepted = value };
            public static ClientEvent ForRecoveryState(
                ScopedSnapshotPayload value) =>
                new ClientEvent { RecoveryState = value };
            public static ClientEvent ForOperationResult(
                MatchOperationResultPayload value) =>
                new ClientEvent { OperationResult = value };
            public static ClientEvent ForSystemResult(
                MatchSystemResultPayload value) =>
                new ClientEvent { SystemResult = value };
            public static ClientEvent ForClock(MatchClockSyncPayload value) =>
                new ClientEvent { Clock = value };
            public static ClientEvent DisconnectedEvent() =>
                new ClientEvent { Disconnected = true };
        }
    }

    public static class LanMatchReconnectCoordinator
    {
        public static async Task<LanRoomClient> ReconnectUntilAcceptedAsync(
            ReconnectCredential credential,
            IReconnectCredentialStore store,
            CancellationToken cancellationToken,
            Action<int> attemptStarted = null,
            ScopedSnapshotPayload retainedSnapshot = null)
        {
            if (credential == null || !credential.IsValid)
                throw new ArgumentException(
                    "The reconnect credential is invalid.",
                    nameof(credential));
            var attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var delay = ReconnectRetrySchedule.GetDelayMilliseconds(attempt);
                attemptStarted?.Invoke(attempt);
                if (delay > 0)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                try
                {
                    return await LanRoomClient.ReconnectAsync(
                        credential,
                        store,
                        retainedSnapshot).ConfigureAwait(false);
                }
                catch (MatchReconnectRejectedException exception)
                {
                    if (exception.Code == MatchReconnectRejectCode.CompatibilityMismatch)
                        throw;
                    if (exception.Code == MatchReconnectRejectCode.InvalidToken
                        || exception.Code == MatchReconnectRejectCode.SessionEnded
                        || exception.Code == MatchReconnectRejectCode.ExplicitlyQuit
                        || exception.Code == MatchReconnectRejectCode.UnknownSession)
                    {
                        throw;
                    }
                    throw;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    attempt = checked(attempt + 1);
                }
            }
        }
    }

    internal sealed class VolatileReconnectCredentialStore :
        IReconnectCredentialStore
    {
        private ReconnectCredential credential;
        public bool TryLoad(out ReconnectCredential value)
        {
            value = credential;
            return value != null;
        }
        public void Save(ReconnectCredential value)
        {
            if (value == null || !value.IsValid)
                throw new ArgumentException(
                    "The reconnect credential is invalid.",
                    nameof(value));
            credential = value;
        }
        public void Clear() { credential = null; }
    }

    internal static class LanRoomTransport
    {
        public static async Task WriteMessageAsync(
            NetworkStream stream,
            LobbyWireMessage message)
        {
            var frame = LobbyProtocol.Encode(message);
            await stream.WriteAsync(frame, 0, frame.Length)
                .ConfigureAwait(false);
        }

        public static async Task<LobbyWireMessage> ReadMessageAsync(
            NetworkStream stream)
        {
            var frame = await LanFrameTransport.ReadFrameAsync(
                stream,
                CancellationToken.None).ConfigureAwait(false);
            if (frame == null) return null;
            if (!LobbyProtocol.TryDecode(
                frame,
                out var message,
                out var error))
            {
                throw new InvalidDataException(
                    "The lobby message is invalid: " + error + ".");
            }
            return message;
        }

        public static string SerializeSnapshot(LobbyRoomSnapshot snapshot) =>
            LanRoomSnapshotWire.Serialize(snapshot);

        public static bool TryDeserializeSnapshot(
            string json,
            out LobbyRoomSnapshot snapshot) =>
            LanRoomSnapshotWire.TryDeserialize(json, out snapshot);
    }

    internal static class LanRoomSnapshotWire
    {
        public static string Serialize(LobbyRoomSnapshot snapshot)
        {
            var members = new SnapshotMemberWire[snapshot.Members.Count];
            for (var index = 0; index < members.Length; index++)
            {
                var member = snapshot.Members[index];
                members[index] = new SnapshotMemberWire
                {
                    playerId = member.Profile.PlayerId,
                    displayName = member.Profile.DisplayName,
                    avatarIndex = member.Profile.AvatarIndex,
                    isReady = member.IsReady,
                    latencyMilliseconds = member.LatencyMilliseconds
                };
            }
            return LobbyJson.Serialize(new SnapshotWire
            {
                roomCode = snapshot.RoomCode,
                hostPlayerId = snapshot.HostPlayerId,
                hasStarted = snapshot.HasStarted,
                revision = snapshot.Revision,
                members = members
            });
        }

        public static bool TryDeserialize(
            string json,
            out LobbyRoomSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                var wire = LobbyJson.Deserialize<SnapshotWire>(json);
                if (wire == null
                    || !LobbyRoomCode.IsValid(wire.roomCode)
                    || wire.members == null
                    || wire.members.Length > LobbyRoomSnapshot.MaximumMembers)
                {
                    return false;
                }
                var members = new LobbyMemberSnapshot[wire.members.Length];
                for (var index = 0; index < members.Length; index++)
                {
                    var member = wire.members[index];
                    var profile = new LobbyProfile(
                        member.playerId,
                        member.displayName,
                        member.avatarIndex);
                    if (!profile.IsValid()) return false;
                    members[index] = new LobbyMemberSnapshot(
                        profile,
                        member.isReady,
                        member.latencyMilliseconds);
                }
                snapshot = new LobbyRoomSnapshot(
                    wire.roomCode,
                    wire.hostPlayerId,
                    members,
                    wire.hasStarted,
                    wire.revision);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DataContract]
        private sealed class SnapshotWire
        {
            [DataMember(Name = "roomCode")] public string roomCode;
            [DataMember(Name = "hostPlayerId")] public string hostPlayerId;
            [DataMember(Name = "hasStarted")] public bool hasStarted;
            [DataMember(Name = "revision")] public long revision;
            [DataMember(Name = "members")] public SnapshotMemberWire[] members;
        }

        [DataContract]
        private sealed class SnapshotMemberWire
        {
            [DataMember(Name = "playerId")] public string playerId;
            [DataMember(Name = "displayName")] public string displayName;
            [DataMember(Name = "avatarIndex")] public int avatarIndex;
            [DataMember(Name = "isReady")] public bool isReady;
            [DataMember(Name = "latencyMilliseconds")]
            public long latencyMilliseconds;
        }
    }
}
