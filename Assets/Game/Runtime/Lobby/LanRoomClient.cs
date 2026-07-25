using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ArknoNights.Lobby
{
    public sealed class LanRoomClient : IDisposable
    {
        private const int HeartbeatMilliseconds = 1000;

        private readonly TcpClient tcpClient;
        private readonly NetworkStream stream;
        private readonly LobbyProfile profile;
        private readonly string requestedRoomCode;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly TaskCompletionSource<LobbyRoomSnapshot> joinCompletion = new TaskCompletionSource<LobbyRoomSnapshot>();
        private readonly TaskCompletionSource<bool> startCompletion = new TaskCompletionSource<bool>();
        private readonly Task readTask;
        private readonly Task heartbeatTask;
        private readonly ConcurrentQueue<ClientEvent> receivedEvents = new ConcurrentQueue<ClientEvent>();
        private LobbyRoomSnapshot snapshot;
        private long latencyMilliseconds = -1;
        private bool stopped;
        private volatile bool disconnected;

        private LanRoomClient(TcpClient tcpClient, LobbyProfile profile, string requestedRoomCode)
        {
            this.tcpClient = tcpClient;
            stream = tcpClient.GetStream();
            this.profile = profile;
            this.requestedRoomCode = requestedRoomCode;
            readTask = Task.Run(ReadLoopAsync);
            heartbeatTask = Task.Run(HeartbeatLoopAsync);
        }

        public LobbyRoomSnapshot Snapshot => snapshot;
        public long LatencyMilliseconds => latencyMilliseconds;
        public bool IsConnected => !stopped && !disconnected && tcpClient.Connected;

        public void Tick()
        {
            while (receivedEvents.TryDequeue(out var received))
            {
                if (received.Snapshot != null) snapshot = received.Snapshot;
                if (received.LatencyMilliseconds >= 0) latencyMilliseconds = received.LatencyMilliseconds;
            }
        }

        public static async Task<LanRoomClient> JoinAsync(IPEndPoint endpoint, string roomCode, LobbyProfile profile)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (!LobbyRoomCode.IsValid(roomCode)) throw new ArgumentException("The room code must contain exactly six digits.", nameof(roomCode));
            if (profile == null || !profile.IsValid()) throw new ArgumentException("The client profile is invalid.", nameof(profile));

            var tcpClient = new TcpClient(endpoint.AddressFamily);
            await tcpClient.ConnectAsync(endpoint.Address, endpoint.Port);
            var client = new LanRoomClient(tcpClient, profile, roomCode);
            try
            {
                await client.SendAsync(new LobbyWireMessage
                {
                    protocolVersion = LobbyProtocol.ProtocolVersion,
                    kind = LobbyMessageKind.JoinRequest.ToString(),
                    roomCode = roomCode,
                    playerId = profile.PlayerId,
                    displayName = profile.DisplayName,
                    avatarIndex = profile.AvatarIndex
                });
                var completed = await Task.WhenAny(client.joinCompletion.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                if (completed != client.joinCompletion.Task) throw new TimeoutException("The LAN room did not accept the join request.");
                await client.joinCompletion.Task;
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public static Task<LanRoomClient> JoinAsync(IPEndPoint endpoint, LobbyDiscoveryEntry entry, LobbyProfile profile)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            return JoinAsync(endpoint, entry.RoomCode, profile);
        }

        public static Task<LanRoomClient> JoinForTestsAsync(IPEndPoint endpoint, LobbyProfile profile)
        {
            return JoinAsync(endpoint, "000000", profile);
        }

        public Task SetReadyAsync(bool isReady)
        {
            return SendAsync(CreateMessage(LobbyMessageKind.SetReady, isReady));
        }

        public async Task LeaveAsync()
        {
            if (!stopped)
            {
                try { await SendAsync(CreateMessage(LobbyMessageKind.Leave, false)); }
                catch (Exception) { }
            }

            await StopAsync();
        }

        public async Task StopAsync()
        {
            if (stopped) return;
            stopped = true;
            cancellation.Cancel();
            try { tcpClient.Close(); } catch (Exception) { }
            try { await readTask.ConfigureAwait(false); } catch (Exception) { }
            try { await heartbeatTask.ConfigureAwait(false); } catch (Exception) { }
            cancellation.Dispose();
            sendGate.Dispose();
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public async Task WaitForStartAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(startCompletion.Task, Task.Delay(timeout));
            if (completed != startCompletion.Task) throw new TimeoutException("The host did not start the room before the timeout.");
            await startCompletion.Task;
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var message = await LanRoomTransport.ReadMessageAsync(stream);
                    if (message == null) break;
                    if (!Enum.TryParse(message.kind, false, out LobbyMessageKind kind)) continue;

                    if (kind == LobbyMessageKind.JoinAccepted || kind == LobbyMessageKind.RoomSnapshot || kind == LobbyMessageKind.Start)
                    {
                        if (LanRoomTransport.TryDeserializeSnapshot(message.snapshotJson, out var roomSnapshot))
                        {
                            receivedEvents.Enqueue(new ClientEvent(roomSnapshot, -1));
                            if (kind == LobbyMessageKind.JoinAccepted) joinCompletion.TrySetResult(roomSnapshot);
                            if (kind == LobbyMessageKind.Start || roomSnapshot.HasStarted) startCompletion.TrySetResult(true);
                        }
                    }
                    else if (kind == LobbyMessageKind.Ping)
                    {
                        await SendAsync(CreateMessage(LobbyMessageKind.Pong, false, message.sentUnixMilliseconds));
                    }
                    else if (kind == LobbyMessageKind.Pong)
                    {
                        receivedEvents.Enqueue(new ClientEvent(null, Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - message.sentUnixMilliseconds)));
                    }
                    else if (kind == LobbyMessageKind.Reject)
                    {
                        joinCompletion.TrySetException(new InvalidOperationException(message.rejectionCode));
                    }
                }
            }
            catch (Exception exception)
            {
                if (!stopped) joinCompletion.TrySetException(exception);
            }
            finally
            {
                disconnected = true;
            }
        }

        private async Task HeartbeatLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try { await Task.Delay(HeartbeatMilliseconds, cancellation.Token); }
                catch (TaskCanceledException) { return; }
                try { await SendAsync(CreateMessage(LobbyMessageKind.Ping, false, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())); }
                catch (Exception) { return; }
            }
        }

        private LobbyWireMessage CreateMessage(LobbyMessageKind kind, bool isReady, long sentUnixMilliseconds = 0)
        {
            var roomCode = snapshot == null ? requestedRoomCode : snapshot.RoomCode;
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

        private async Task SendAsync(LobbyWireMessage message)
        {
            await sendGate.WaitAsync();
            try { await LanRoomTransport.WriteMessageAsync(stream, message); }
            finally { sendGate.Release(); }
        }

        private sealed class ClientEvent
        {
            public ClientEvent(LobbyRoomSnapshot snapshot, long latencyMilliseconds)
            {
                Snapshot = snapshot;
                LatencyMilliseconds = latencyMilliseconds;
            }

            public LobbyRoomSnapshot Snapshot { get; }
            public long LatencyMilliseconds { get; }
        }
    }

    internal static class LanRoomTransport
    {
        public static async Task WriteMessageAsync(NetworkStream stream, LobbyWireMessage message)
        {
            var frame = LobbyProtocol.Encode(message);
            await stream.WriteAsync(frame, 0, frame.Length);
        }

        public static async Task<LobbyWireMessage> ReadMessageAsync(NetworkStream stream)
        {
            var prefix = new byte[sizeof(int)];
            if (!await ReadExactlyAsync(stream, prefix, 0, prefix.Length)) return null;
            var payloadLength = (prefix[0] << 24) | (prefix[1] << 16) | (prefix[2] << 8) | prefix[3];
            if (payloadLength < 1 || payloadLength + prefix.Length > LobbyProtocol.MaximumMessageBytes) throw new InvalidDataException("The lobby frame length is invalid.");

            var frame = new byte[prefix.Length + payloadLength];
            Buffer.BlockCopy(prefix, 0, frame, 0, prefix.Length);
            if (!await ReadExactlyAsync(stream, frame, prefix.Length, payloadLength)) return null;
            if (!LobbyProtocol.TryDecode(frame, out var message, out var error)) throw new InvalidDataException("The lobby message is invalid: " + error + ".");
            return message;
        }

        public static string SerializeSnapshot(LobbyRoomSnapshot snapshot)
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

        public static bool TryDeserializeSnapshot(string json, out LobbyRoomSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                var wire = LobbyJson.Deserialize<SnapshotWire>(json);
                if (wire == null || !LobbyRoomCode.IsValid(wire.roomCode) || wire.members == null || wire.members.Length > LobbyRoomSnapshot.MaximumMembers) return false;
                var members = new LobbyMemberSnapshot[wire.members.Length];
                for (var index = 0; index < members.Length; index++)
                {
                    var member = wire.members[index];
                    var profile = new LobbyProfile(member.playerId, member.displayName, member.avatarIndex);
                    if (!profile.IsValid()) return false;
                    members[index] = new LobbyMemberSnapshot(profile, member.isReady, member.latencyMilliseconds);
                }

                snapshot = new LobbyRoomSnapshot(wire.roomCode, wire.hostPlayerId, members, wire.hasStarted, wire.revision);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            var received = 0;
            while (received < count)
            {
                var read = await stream.ReadAsync(buffer, offset + received, count - received);
                if (read == 0) return false;
                received += read;
            }

            return true;
        }

        [DataContract]
        private sealed class SnapshotWire
        {
            [DataMember(Name = "roomCode")]
            public string roomCode;
            [DataMember(Name = "hostPlayerId")]
            public string hostPlayerId;
            [DataMember(Name = "hasStarted")]
            public bool hasStarted;
            [DataMember(Name = "revision")]
            public long revision;
            [DataMember(Name = "members")]
            public SnapshotMemberWire[] members;
        }

        [DataContract]
        private sealed class SnapshotMemberWire
        {
            [DataMember(Name = "playerId")]
            public string playerId;
            [DataMember(Name = "displayName")]
            public string displayName;
            [DataMember(Name = "avatarIndex")]
            public int avatarIndex;
            [DataMember(Name = "isReady")]
            public bool isReady;
            [DataMember(Name = "latencyMilliseconds")]
            public long latencyMilliseconds;
        }
    }
}
