using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArknoNights.Lobby
{
    public sealed class LanDiscoveryService : IDisposable
    {
        public const int DiscoveryPort = 46871;
        public const int AnnouncementIntervalMilliseconds = 750;
        public const int DiscoveryExpiryMilliseconds = 2500;

        private readonly ConcurrentQueue<ReceivedAnnouncement> receivedAnnouncements = new ConcurrentQueue<ReceivedAnnouncement>();
        private readonly Dictionary<string, SeenAnnouncement> discoveries = new Dictionary<string, SeenAnnouncement>();
        private readonly Func<string> nextRoomCode;
        private LobbyDiscoveryEntry localAnnouncement;
        private UdpClient receiver;
        private CancellationTokenSource cancellation;
        private Task receiveTask;
        private DateTimeOffset nextAnnouncementAt;
        private IReadOnlyList<LobbyDiscoveryEntry> discoveredRooms = Array.Empty<LobbyDiscoveryEntry>();

        public LanDiscoveryService()
            : this(null, null)
        {
        }

        public LanDiscoveryService(LobbyDiscoveryEntry localAnnouncement)
            : this(localAnnouncement, null)
        {
        }

        public LanDiscoveryService(LobbyDiscoveryEntry localAnnouncement, Func<string> nextRoomCode)
        {
            if (localAnnouncement != null && !LobbyRoomCode.IsValid(localAnnouncement.RoomCode))
            {
                throw new ArgumentException("The local announcement must have a valid room code.", nameof(localAnnouncement));
            }

            this.localAnnouncement = localAnnouncement;
            this.nextRoomCode = nextRoomCode ?? CreateRoomCode;
        }

        public event Action<string> RoomCodeCollision;

        public IReadOnlyList<LobbyDiscoveryEntry> DiscoveredRooms => discoveredRooms;
        public string LocalRoomCode => localAnnouncement == null ? null : localAnnouncement.RoomCode;

        public void Start()
        {
            if (receiver != null) return;

            cancellation = new CancellationTokenSource();
            receiver = new UdpClient(AddressFamily.InterNetwork);
            receiver.ExclusiveAddressUse = false;
            receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            receiver.EnableBroadcast = true;
            receiver.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            nextAnnouncementAt = DateTimeOffset.MinValue;
            receiveTask = Task.Run(() => ReceiveLoopAsync(cancellation.Token));
        }

        public void Stop()
        {
            var source = cancellation;
            cancellation = null;
            if (source != null) source.Cancel();
            if (receiver != null) receiver.Close();
            receiver = null;
            if (receiveTask != null)
            {
                try { receiveTask.GetAwaiter().GetResult(); }
                catch (Exception) { }
            }

            receiveTask = null;
            if (source != null) source.Dispose();
        }

        public void Tick(DateTimeOffset now)
        {
            while (receivedAnnouncements.TryDequeue(out var received))
            {
                if (!TryDecodeAnnouncement(received.Bytes, out var announcement)) continue;

                if (localAnnouncement != null
                    && string.Equals(announcement.RoomCode, localAnnouncement.RoomCode, StringComparison.Ordinal)
                    && !IsOwnEndpoint(received.Endpoint))
                {
                    var replacement = nextRoomCode();
                    if (LobbyRoomCode.IsValid(replacement) && !string.Equals(replacement, localAnnouncement.RoomCode, StringComparison.Ordinal))
                    {
                        localAnnouncement = new LobbyDiscoveryEntry(replacement, localAnnouncement.HostDisplayName, localAnnouncement.MemberCount, localAnnouncement.Capacity, localAnnouncement.IsJoinable, localAnnouncement.TcpPort, localAnnouncement.Sequence + 1);
                        RoomCodeCollision?.Invoke(replacement);
                    }

                    continue;
                }

                if (localAnnouncement != null && string.Equals(announcement.RoomCode, localAnnouncement.RoomCode, StringComparison.Ordinal)) continue;
                discoveries[CreateKey(announcement.RoomCode, received.Endpoint)] = new SeenAnnouncement(announcement, received.Endpoint, now);
            }

            var expired = new List<string>();
            foreach (var pair in discoveries)
            {
                if ((now - pair.Value.LastSeen).TotalMilliseconds > DiscoveryExpiryMilliseconds) expired.Add(pair.Key);
            }

            for (var index = 0; index < expired.Count; index++) discoveries.Remove(expired[index]);
            PublishDiscoveries();

            if (receiver != null && localAnnouncement != null && now >= nextAnnouncementAt)
            {
                var bytes = EncodeAnnouncement(localAnnouncement);
                try { receiver.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)); }
                catch (SocketException) { }
                nextAnnouncementAt = now.AddMilliseconds(AnnouncementIntervalMilliseconds);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        public static byte[] EncodeAnnouncementForTests(LobbyDiscoveryEntry announcement)
        {
            return EncodeAnnouncement(announcement);
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await receiver.ReceiveAsync();
                    receivedAnnouncements.Enqueue(new ReceivedAnnouncement(result.Buffer, result.RemoteEndPoint));
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (token.IsCancellationRequested) return; }
            }
        }

        private bool IsOwnEndpoint(IPEndPoint endpoint)
        {
            if (receiver == null || endpoint == null) return false;
            var local = receiver.Client.LocalEndPoint as IPEndPoint;
            if (local == null || endpoint.Port != local.Port) return false;
            if (IPAddress.IsLoopback(endpoint.Address)) return true;
            try
            {
                var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
                for (var index = 0; index < addresses.Length; index++)
                {
                    if (addresses[index].Equals(endpoint.Address)) return true;
                }
            }
            catch (SocketException) { }

            return false;
        }

        private void PublishDiscoveries()
        {
            var values = new List<LobbyDiscoveryEntry>(discoveries.Count);
            foreach (var seen in discoveries.Values) values.Add(seen.Entry);
            values.Sort((left, right) => string.CompareOrdinal(left.RoomCode, right.RoomCode));
            discoveredRooms = new ReadOnlyCollection<LobbyDiscoveryEntry>(values);
        }

        private static byte[] EncodeAnnouncement(LobbyDiscoveryEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var wire = new DiscoveryWire
            {
                roomCode = entry.RoomCode,
                hostDisplayName = entry.HostDisplayName,
                memberCount = entry.MemberCount,
                capacity = entry.Capacity,
                isJoinable = entry.IsJoinable,
                tcpPort = entry.TcpPort,
                sequence = entry.Sequence
            };
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(wire));
        }

        private static bool TryDecodeAnnouncement(byte[] bytes, out LobbyDiscoveryEntry entry)
        {
            entry = null;
            try
            {
                var wire = JsonUtility.FromJson<DiscoveryWire>(Encoding.UTF8.GetString(bytes));
                if (wire == null || !LobbyRoomCode.IsValid(wire.roomCode) || string.IsNullOrWhiteSpace(wire.hostDisplayName)
                    || wire.memberCount < 0 || wire.capacity < 1 || wire.memberCount > wire.capacity || wire.tcpPort < 1 || wire.tcpPort > 65535)
                {
                    return false;
                }

                entry = new LobbyDiscoveryEntry(wire.roomCode, wire.hostDisplayName, wire.memberCount, wire.capacity, wire.isJoinable, wire.tcpPort, wire.sequence);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string CreateKey(string roomCode, IPEndPoint endpoint)
        {
            return roomCode + "@" + endpoint.Address + ":" + endpoint.Port;
        }

        private static string CreateRoomCode()
        {
            return new System.Random().Next(0, 1000000).ToString("D6");
        }

        [Serializable]
        private sealed class DiscoveryWire
        {
            public string roomCode;
            public string hostDisplayName;
            public int memberCount;
            public int capacity;
            public bool isJoinable;
            public int tcpPort;
            public long sequence;
        }

        private sealed class ReceivedAnnouncement
        {
            public ReceivedAnnouncement(byte[] bytes, IPEndPoint endpoint) { Bytes = bytes; Endpoint = endpoint; }
            public byte[] Bytes { get; }
            public IPEndPoint Endpoint { get; }
        }

        private sealed class SeenAnnouncement
        {
            public SeenAnnouncement(LobbyDiscoveryEntry entry, IPEndPoint endpoint, DateTimeOffset lastSeen) { Entry = entry; Endpoint = endpoint; LastSeen = lastSeen; }
            public LobbyDiscoveryEntry Entry { get; }
            public IPEndPoint Endpoint { get; }
            public DateTimeOffset LastSeen { get; }
        }
    }
}
