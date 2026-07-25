using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ArknoNights.Lobby;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanSocketIntegrationEditModeTests
    {
        [Test]
        public void HostAndClient_JoinReadyStartAndReleaseTcpPort()
        {
            var host = Run(() => LanRoomHost.StartForTestsAsync(Profile("host"), 0));
            var client = Run(() => LanRoomClient.JoinForTestsAsync(host.LoopbackEndpoint, Profile("guest")));
            try
            {
                Assert.That(client.Snapshot.Members.Select(member => member.PlayerId), Is.EquivalentTo(new[] { "host", "guest" }));

                Run(() => client.SetReadyAsync(true));
                WaitUntil(() => host.Snapshot.Members.Single(member => member.PlayerId == "guest").IsReady, TimeSpan.FromSeconds(2));
                host.SetReadyForTests("host", true);
                Assert.That(host.TryStart("host", out var failure), Is.True, failure.ToString());

                Run(() => client.WaitForStartAsync(TimeSpan.FromSeconds(2)));
                Assert.That(client.Snapshot.HasStarted, Is.True);
                WaitUntil(() => client.LatencyMilliseconds >= 0, TimeSpan.FromSeconds(2));

                Run(() => client.LeaveAsync());
                WaitUntil(() => host.Snapshot.Members.Count == 1, TimeSpan.FromSeconds(2));
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
