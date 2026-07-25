using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ArknoNights.Lobby
{
    public sealed class LanRoomHost : IDisposable
    {
        private const int HeartbeatMilliseconds = 1000;
        private const int MaximumMissedPongs = 3;

        private readonly object gate = new object();
        private LobbyRoomState room;
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Dictionary<string, GuestConnection> guestsByPlayerId = new Dictionary<string, GuestConnection>();
        private readonly List<GuestConnection> connections = new List<GuestConnection>();
        private readonly bool acceptsAnyRoomCode;
        private readonly Task acceptTask;
        private readonly Task heartbeatTask;
        private bool stopped;

        private LanRoomHost(LobbyProfile hostProfile, int tcpPort, bool acceptsAnyRoomCode)
        {
            room = LobbyRoomState.CreateHost(hostProfile, CreateRoomCode());
            this.acceptsAnyRoomCode = acceptsAnyRoomCode;
            listener = new TcpListener(IPAddress.Any, tcpPort);
            listener.Start();
            acceptTask = Task.Run(AcceptLoopAsync);
            heartbeatTask = Task.Run(HeartbeatLoopAsync);
        }

        public LobbyRoomSnapshot Snapshot
        {
            get { lock (gate) return CreateSnapshotWithLatency(); }
        }

        public string RoomCode => Snapshot.RoomCode;
        public int TcpPort => ((IPEndPoint)listener.LocalEndpoint).Port;
        public IPEndPoint LoopbackEndpoint => new IPEndPoint(IPAddress.Loopback, TcpPort);

        public static Task<LanRoomHost> StartAsync(LobbyProfile hostProfile)
        {
            return StartAsync(hostProfile, CancellationToken.None);
        }

        public static Task<LanRoomHost> StartAsync(LobbyProfile hostProfile, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LanRoomHost(hostProfile, 0, false));
        }

        public static Task<LanRoomHost> StartForTestsAsync(LobbyProfile hostProfile, int tcpPort)
        {
            return Task.FromResult(new LanRoomHost(hostProfile, tcpPort, true));
        }

        public LanDiscoveryService CreateDiscoveryService()
        {
            lock (gate)
            {
                var snapshot = CreateSnapshotWithLatency();
                return new LanDiscoveryService(
                    new LobbyDiscoveryEntry(snapshot.RoomCode, snapshot.Members[0].Profile.DisplayName, snapshot.Members.Count, LobbyRoomSnapshot.MaximumMembers, !snapshot.HasStarted && snapshot.Members.Count < LobbyRoomSnapshot.MaximumMembers, TcpPort, snapshot.Revision),
                    RegenerateAuthoritativeRoomCode);
            }
        }

        public bool TryStart(string playerId, out LobbyJoinFailure failure)
        {
            bool started;
            lock (gate)
            {
                started = room.TryStart(playerId, out failure);
            }

            if (started)
            {
                _ = Task.Run(() => BroadcastSnapshotAsync(LobbyMessageKind.Start));
            }

            return started;
        }

        public async Task StopAsync()
        {
            List<GuestConnection> connections;
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                connections = new List<GuestConnection>(this.connections);
            }

            cancellation.Cancel();
            listener.Stop();
            for (var index = 0; index < connections.Count; index++) connections[index].Close();
            try { await acceptTask.ConfigureAwait(false); } catch (Exception) { }
            try { await heartbeatTask.ConfigureAwait(false); } catch (Exception) { }
            for (var index = 0; index < connections.Count; index++)
            {
                try { await connections[index].ReadTask.ConfigureAwait(false); } catch (Exception) { }
            }

            cancellation.Dispose();
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public void SetReadyForTests(string playerId, bool isReady)
        {
            bool changed;
            lock (gate)
            {
                changed = room.TrySetReady(playerId, playerId, isReady, out var failure);
                if (!changed) throw new InvalidOperationException("Could not update readiness: " + failure + ".");
            }

            if (changed) _ = Task.Run(() => BroadcastSnapshotAsync(LobbyMessageKind.RoomSnapshot));
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    var connection = new GuestConnection(client);
                    lock (gate) connections.Add(connection);
                    connection.ReadTask = Task.Run(() => ReadGuestLoopAsync(connection));
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (cancellation.IsCancellationRequested) return; }
            }
        }

        private async Task ReadGuestLoopAsync(GuestConnection connection)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var message = await LanRoomTransport.ReadMessageAsync(connection.Stream);
                    if (message == null) break;
                    await HandleGuestMessageAsync(connection, message);
                }
            }
            catch (Exception) { }
            finally
            {
                RemoveConnection(connection);
            }
        }

        private async Task HandleGuestMessageAsync(GuestConnection connection, LobbyWireMessage message)
        {
            if (!Enum.TryParse(message.kind, false, out LobbyMessageKind kind)) return;
            if (kind == LobbyMessageKind.JoinRequest)
            {
                await HandleJoinAsync(connection, message);
                return;
            }

            if (!string.Equals(connection.PlayerId, message.playerId, StringComparison.Ordinal)) return;
            if (kind == LobbyMessageKind.SetReady)
            {
                bool changed;
                lock (gate) changed = room.TrySetReady(message.playerId, message.playerId, message.isReady, out _);
                if (changed) await BroadcastSnapshotAsync(LobbyMessageKind.RoomSnapshot);
            }
            else if (kind == LobbyMessageKind.Leave)
            {
                RemoveConnection(connection);
            }
            else if (kind == LobbyMessageKind.Ping)
            {
                await connection.SendAsync(CreateMessage(LobbyMessageKind.Pong, connection.PlayerId, message.sentUnixMilliseconds));
            }
            else if (kind == LobbyMessageKind.Pong)
            {
                lock (gate)
                {
                    connection.MissedPongs = 0;
                    connection.LatencyMilliseconds = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - message.sentUnixMilliseconds);
                }
            }
        }

        private async Task HandleJoinAsync(GuestConnection connection, LobbyWireMessage message)
        {
            if ((!acceptsAnyRoomCode && !string.Equals(message.roomCode, RoomCode, StringComparison.Ordinal))
                || connection.PlayerId != null)
            {
                await connection.SendAsync(CreateMessage(LobbyMessageKind.Reject, message.playerId, 0, null, LobbyJoinFailure.InvalidRoomCode.ToString()));
                return;
            }

            var profile = new LobbyProfile(message.playerId, message.displayName, message.avatarIndex);
            LobbyJoinFailure failure;
            LobbyRoomSnapshot snapshot;
            lock (gate)
            {
                if (!room.TryJoin(profile, out failure)) snapshot = null;
                else
                {
                    connection.PlayerId = profile.PlayerId;
                    guestsByPlayerId[profile.PlayerId] = connection;
                    snapshot = CreateSnapshotWithLatency();
                }
            }

            if (snapshot == null)
            {
                await connection.SendAsync(CreateMessage(LobbyMessageKind.Reject, message.playerId, 0, null, failure.ToString()));
                return;
            }

            await connection.SendAsync(CreateMessage(LobbyMessageKind.JoinAccepted, profile.PlayerId, 0, snapshot));
            await BroadcastSnapshotAsync(LobbyMessageKind.RoomSnapshot);
        }

        private async Task HeartbeatLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try { await Task.Delay(HeartbeatMilliseconds, cancellation.Token); }
                catch (TaskCanceledException) { return; }

                List<GuestConnection> expired = null;
                List<GuestConnection> active;
                lock (gate)
                {
                    active = new List<GuestConnection>(guestsByPlayerId.Values);
                    for (var index = 0; index < active.Count; index++)
                    {
                        active[index].MissedPongs++;
                        if (active[index].MissedPongs >= MaximumMissedPongs)
                        {
                            if (expired == null) expired = new List<GuestConnection>();
                            expired.Add(active[index]);
                        }
                    }
                }

                if (expired != null)
                {
                    for (var index = 0; index < expired.Count; index++) RemoveConnection(expired[index]);
                }

                for (var index = 0; index < active.Count; index++)
                {
                    if (expired == null || !expired.Contains(active[index]))
                    {
                        try { await active[index].SendAsync(CreateMessage(LobbyMessageKind.Ping, active[index].PlayerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())); }
                        catch (Exception) { RemoveConnection(active[index]); }
                    }
                }
            }
        }

        private async Task BroadcastSnapshotAsync(LobbyMessageKind kind)
        {
            List<GuestConnection> recipients;
            LobbyRoomSnapshot snapshot;
            lock (gate)
            {
                if (stopped) return;
                recipients = new List<GuestConnection>(guestsByPlayerId.Values);
                snapshot = CreateSnapshotWithLatency();
            }

            for (var index = 0; index < recipients.Count; index++)
            {
                try { await recipients[index].SendAsync(CreateMessage(kind, recipients[index].PlayerId, 0, snapshot)); }
                catch (Exception) { RemoveConnection(recipients[index]); }
            }
        }

        private void RemoveConnection(GuestConnection connection)
        {
            bool changed = false;
            lock (gate)
            {
                connections.Remove(connection);
                if (!string.IsNullOrWhiteSpace(connection.PlayerId))
                {
                    guestsByPlayerId.Remove(connection.PlayerId);
                    changed = room.RemovePlayer(connection.PlayerId);
                    connection.PlayerId = null;
                }
            }

            connection.Close();
            if (changed && !stopped) _ = BroadcastSnapshotAsync(LobbyMessageKind.RoomSnapshot);
        }

        private LobbyRoomSnapshot CreateSnapshotWithLatency()
        {
            var source = room.Snapshot;
            var members = new LobbyMemberSnapshot[source.Members.Count];
            for (var index = 0; index < members.Length; index++)
            {
                var member = source.Members[index];
                var latency = 0L;
                if (guestsByPlayerId.TryGetValue(member.PlayerId, out var connection)) latency = connection.LatencyMilliseconds;
                members[index] = new LobbyMemberSnapshot(member.Profile, member.IsReady, latency);
            }

            return new LobbyRoomSnapshot(source.RoomCode, source.HostPlayerId, members, source.HasStarted, source.Revision);
        }

        private string RegenerateAuthoritativeRoomCode()
        {
            LobbyRoomSnapshot before;
            string replacement;
            lock (gate)
            {
                before = room.Snapshot;
                if (before.HasStarted || before.Members.Count == 0) return before.RoomCode;
                do { replacement = CreateRoomCode(); }
                while (string.Equals(replacement, before.RoomCode, StringComparison.Ordinal));
                room = RecreateRoomWithCode(before, replacement);
            }

            _ = Task.Run(() => BroadcastSnapshotAsync(LobbyMessageKind.RoomSnapshot));
            return replacement;
        }

        private static LobbyRoomState RecreateRoomWithCode(LobbyRoomSnapshot snapshot, string roomCode)
        {
            var replacement = LobbyRoomState.CreateHost(snapshot.Members[0].Profile, roomCode);
            for (var index = 1; index < snapshot.Members.Count; index++)
            {
                replacement.TryJoin(snapshot.Members[index].Profile, out _);
            }

            for (var index = 0; index < snapshot.Members.Count; index++)
            {
                var member = snapshot.Members[index];
                if (member.IsReady) replacement.TrySetReady(member.PlayerId, member.PlayerId, true, out _);
            }

            if (snapshot.HasStarted) replacement.TryStart(snapshot.HostPlayerId, out _);
            return replacement;
        }

        private LobbyWireMessage CreateMessage(LobbyMessageKind kind, string playerId, long sentUnixMilliseconds, LobbyRoomSnapshot snapshot = null, string rejection = null)
        {
            return new LobbyWireMessage
            {
                protocolVersion = LobbyProtocol.ProtocolVersion,
                kind = kind.ToString(),
                roomCode = RoomCode,
                playerId = playerId,
                sentUnixMilliseconds = sentUnixMilliseconds,
                snapshotJson = snapshot == null ? null : LanRoomTransport.SerializeSnapshot(snapshot),
                rejectionCode = rejection
            };
        }

        private static string CreateRoomCode()
        {
            return new Random().Next(0, 1000000).ToString("D6");
        }

        private sealed class GuestConnection
        {
            private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);

            public GuestConnection(TcpClient client) { Client = client; Stream = client.GetStream(); ReadTask = Task.CompletedTask; }
            public TcpClient Client { get; }
            public NetworkStream Stream { get; }
            public string PlayerId { get; set; }
            public long LatencyMilliseconds { get; set; }
            public int MissedPongs { get; set; }
            public Task ReadTask { get; set; }

            public async Task SendAsync(LobbyWireMessage message)
            {
                await sendGate.WaitAsync();
                try { await LanRoomTransport.WriteMessageAsync(Stream, message); }
                finally { sendGate.Release(); }
            }

            public void Close()
            {
                try { Client.Close(); } catch (Exception) { }
            }
        }
    }
}
