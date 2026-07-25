using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using ArknoNights.Lobby;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanSocketIntegrationEditModeTests
    {
        private static readonly Dictionary<short, OpCode> OpCodes = CreateOpCodes();

        [Test]
        public void HostAndClient_JoinReadyStartAndReleaseTcpPort()
        {
            var host = Run(() => LanRoomHost.StartForTestsAsync(Profile("host"), 0));
            var client = Run(() => LanRoomClient.JoinForTestsAsync(host.LoopbackEndpoint, Profile("guest")));
            try
            {
                client.Tick();
                Assert.That(client.Snapshot.Members.Select(member => member.PlayerId), Is.EquivalentTo(new[] { "host", "guest" }));

                Run(() => client.SetReadyAsync(true));
                WaitUntil(() => { host.Tick(); return host.Snapshot.Members.Single(member => member.PlayerId == "guest").IsReady; }, TimeSpan.FromSeconds(2));
                host.SetReadyForTests("host", true);
                Assert.That(host.TryStart("host", out var failure), Is.True, failure.ToString());

                Run(() => client.WaitForStartAsync(TimeSpan.FromSeconds(2)));
                client.Tick();
                Assert.That(client.Snapshot.HasStarted, Is.True);
                WaitUntil(() => { client.Tick(); return client.LatencyMilliseconds >= 0; }, TimeSpan.FromSeconds(2));

                Run(() => client.LeaveAsync());
                WaitUntil(() => { host.Tick(); return host.Snapshot.Members.Count == 1; }, TimeSpan.FromSeconds(2));
            }
            finally
            {
                Run(() => client.StopAsync());
            }

            var port = host.TcpPort;
            host.Dispose();
            Assert.That(CanBindLoopback(port), Is.True);
        }

        [Test]
        public void Discovery_ExpiresStaleEntryAndRegeneratesLocalRoomCodeOnCollision()
        {
            var clock = DateTimeOffset.UtcNow;
            var local = new LobbyDiscoveryEntry("123456", "Host", 1, 4, true, 34567, 3);
            var remote = new LobbyDiscoveryEntry("654321", "Guest", 2, 4, true, 34568, 4);
            using (var discovery = new LanDiscoveryService(local, () => "654322"))
            {
                discovery.Start();
                try
                {
                    using (var sender = new UdpClient(AddressFamily.InterNetwork))
                    {
                        var announcement = LanDiscoveryService.EncodeAnnouncementForTests(remote);
                        sender.Connect(new IPEndPoint(IPAddress.Loopback, LanDiscoveryService.DiscoveryPort));
                        sender.Send(announcement, announcement.Length);
                    }

                    WaitUntil(() =>
                    {
                        discovery.Tick(clock);
                        return discovery.DiscoveredRooms.Any(entry => entry.RoomCode == "654321");
                    }, TimeSpan.FromSeconds(2));

                    discovery.Tick(clock.AddMilliseconds(LanDiscoveryService.DiscoveryExpiryMilliseconds + 1));
                    Assert.That(discovery.DiscoveredRooms, Is.Empty);

                    using (var sender = new UdpClient(AddressFamily.InterNetwork))
                    {
                        var announcement = LanDiscoveryService.EncodeAnnouncementForTests(local);
                        sender.Connect(new IPEndPoint(IPAddress.Loopback, LanDiscoveryService.DiscoveryPort));
                        sender.Send(announcement, announcement.Length);
                    }

                    WaitUntil(() =>
                    {
                        discovery.Tick(clock.AddSeconds(1));
                        return discovery.LocalRoomCode == "654322";
                    }, TimeSpan.FromSeconds(2));
                }
                finally
                {
                    discovery.Stop();
                }
            }
        }

        [Test]
        public void NetworkSerializers_DoNotReferenceUnityEngineMethodsAndWorkOnThreadPool()
        {
            AssertNoUnityEngineMethodReferences(typeof(LobbyProtocol));
            AssertNoUnityEngineMethodReferences(typeof(LanDiscoveryService));
            AssertNoUnityEngineMethodReferences(typeof(LobbyProtocol).Assembly.GetType("ArknoNights.Lobby.LanRoomTransport"));

            var message = new LobbyWireMessage
            {
                protocolVersion = LobbyProtocol.ProtocolVersion,
                kind = LobbyMessageKind.Ping.ToString(),
                roomCode = "123456",
                playerId = "worker",
                sentUnixMilliseconds = 42
            };
            var decoded = Run(() => System.Threading.Tasks.Task.Run(() =>
            {
                var frame = LobbyProtocol.Encode(message);
                Assert.That(LobbyProtocol.TryDecode(frame, out var result, out var error), Is.True, error.ToString());
                return result;
            }));

            Assert.That(decoded.playerId, Is.EqualTo("worker"));
            Assert.That(decoded.sentUnixMilliseconds, Is.EqualTo(42));
        }

        [Test]
        public void ClientSnapshot_IsPublishedOnlyWhenMainThreadTicks()
        {
            var host = Run(() => LanRoomHost.StartAsync(Profile("host")));
            var client = Run(() => LanRoomClient.JoinAsync(host.LoopbackEndpoint, host.RoomCode, Profile("guest")));
            try
            {
                Assert.That(client.Snapshot, Is.Null);

                client.Tick();

                Assert.That(client.Snapshot.Members.Select(member => member.PlayerId), Is.EquivalentTo(new[] { "host", "guest" }));
            }
            finally
            {
                Run(() => client.StopAsync());
                Run(() => host.StopAsync());
            }
        }

        [Test]
        public void HostSnapshot_IsPublishedOnlyWhenMainThreadTicks()
        {
            var host = Run(() => LanRoomHost.StartAsync(Profile("host")));
            var client = Run(() => LanRoomClient.JoinAsync(host.LoopbackEndpoint, host.RoomCode, Profile("guest")));
            try
            {
                Assert.That(host.Snapshot.Members.Select(member => member.PlayerId), Is.EquivalentTo(new[] { "host" }));

                host.Tick();

                Assert.That(host.Snapshot.Members.Select(member => member.PlayerId), Is.EquivalentTo(new[] { "host", "guest" }));
            }
            finally
            {
                Run(() => client.StopAsync());
                Run(() => host.StopAsync());
            }
        }

        [Test]
        public void HostDiscoveryCollision_RegeneratesAuthoritativeCodeAndRetainsExistingGuests()
        {
            var host = Run(() => LanRoomHost.StartAsync(Profile("host")));
            var existingGuest = Run(() => LanRoomClient.JoinAsync(host.LoopbackEndpoint, host.RoomCode, Profile("guest")));
            LanDiscoveryService discovery = null;
            LanRoomClient joiningGuest = null;
            try
            {
                Run(() => existingGuest.SetReadyAsync(true));
                WaitUntil(() => { host.Tick(); return host.Snapshot.Members.Single(member => member.PlayerId == "guest").IsReady; }, TimeSpan.FromSeconds(2));
                var originalCode = host.RoomCode;
                discovery = host.CreateDiscoveryService();
                discovery.Start();
                using (var sender = new UdpClient(AddressFamily.InterNetwork))
                {
                    var collision = new LobbyDiscoveryEntry(originalCode, "Other Host", 1, 4, true, 34569, 1);
                    var bytes = LanDiscoveryService.EncodeAnnouncementForTests(collision);
                    sender.Connect(new IPEndPoint(IPAddress.Loopback, LanDiscoveryService.DiscoveryPort));
                    sender.Send(bytes, bytes.Length);
                }

                WaitUntil(() =>
                {
                    discovery.Tick(DateTimeOffset.UtcNow);
                    host.Tick();
                    return host.RoomCode != originalCode && host.RoomCode == discovery.LocalRoomCode;
                }, TimeSpan.FromSeconds(2));

                Assert.That(host.Snapshot.Members.Single(member => member.PlayerId == "guest").IsReady, Is.True);
                joiningGuest = Run(() => LanRoomClient.JoinAsync(host.LoopbackEndpoint, host.RoomCode, Profile("new-guest")));
                joiningGuest.Tick();
                Assert.That(joiningGuest.Snapshot.Members.Select(member => member.PlayerId), Is.EquivalentTo(new[] { "host", "guest", "new-guest" }));
            }
            finally
            {
                if (joiningGuest != null) Run(() => joiningGuest.StopAsync());
                if (discovery != null) discovery.Stop();
                Run(() => existingGuest.StopAsync());
                Run(() => host.StopAsync());
            }
        }

        private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("Timed out waiting for the expected socket state.");
                }

                Thread.Sleep(20);
            }
        }

        private static void Run(Func<System.Threading.Tasks.Task> operation)
        {
            System.Threading.Tasks.Task.Run(operation).GetAwaiter().GetResult();
        }

        private static T Run<T>(Func<System.Threading.Tasks.Task<T>> operation)
        {
            return System.Threading.Tasks.Task.Run(operation).GetAwaiter().GetResult();
        }

        private static void AssertNoUnityEngineMethodReferences(Type type)
        {
            Assert.That(type, Is.Not.Null);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var body = method.GetMethodBody();
                if (body == null) continue;
                var il = body.GetILAsByteArray();
                for (var index = 0; index < il.Length;)
                {
                    var value = il[index++];
                    short code = value == 0xfe ? (short)(0xfe00 | il[index++]) : value;
                    var opcode = OpCodes[code];
                    if (opcode.OperandType == OperandType.InlineMethod || opcode.OperandType == OperandType.InlineTok)
                    {
                        var token = BitConverter.ToInt32(il, index);
                        MemberInfo member;
                        try { member = method.Module.ResolveMember(token); }
                        catch (ArgumentException) { member = null; }
                        Assert.That(member == null || member.DeclaringType == null || !member.DeclaringType.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal), Is.True, type.FullName + "." + method.Name + " references " + member);
                    }

                    index += OperandSize(opcode.OperandType, il, index);
                }
            }
        }

        private static int OperandSize(OperandType type, byte[] il, int index)
        {
            switch (type)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch: return 4 + BitConverter.ToInt32(il, index) * 4;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static Dictionary<short, OpCode> CreateOpCodes()
        {
            var values = new Dictionary<short, OpCode>();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(OpCode))
                {
                    var opcode = (OpCode)field.GetValue(null);
                    values[opcode.Value] = opcode;
                }
            }

            return values;
        }

        private static bool CanBindLoopback(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try { listener.Start(); return true; }
                finally { listener.Stop(); }
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static LobbyProfile Profile(string playerId)
        {
            return new LobbyProfile(playerId, "Doctor " + playerId, 0);
        }
    }
}
