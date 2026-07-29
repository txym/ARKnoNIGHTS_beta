using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchSessionFactoryEditModeTests
    {
        [Test]
        public void Create_OneHumanAndThreeBots_UsesFixedInitialStateAndSeatOrder()
        {
            var result = MatchSessionFactory.Create(MatchTestData.Request());

            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            var snapshot = result.Authority.ProjectForHostAuthority();
            Assert.That(snapshot.SessionId, Is.EqualTo("session-1"));
            Assert.That(snapshot.MatchSeed, Is.EqualTo("seed-1"));
            Assert.That(snapshot.HostPlayerId, Is.EqualTo("player-1"));
            Assert.That(snapshot.StateRevision, Is.EqualTo(0));
            Assert.That(snapshot.Phase, Is.EqualTo(MatchPhase.Initializing));
            Assert.That(snapshot.RoundNumber, Is.EqualTo(0));
            Assert.That(snapshot.Seats.Select(seat => seat.SeatIndex), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(snapshot.Seats.Count(seat => seat.ControllerKind == MatchControllerKind.Human), Is.EqualTo(1));

            foreach (var seat in snapshot.Seats)
            {
                Assert.That(seat.Life, Is.EqualTo(400));
                Assert.That(seat.Gold, Is.EqualTo(7));
                Assert.That(seat.Level, Is.EqualTo(1));
                Assert.That(seat.TotalDeploymentCost, Is.EqualTo(16));
                Assert.That(seat.AvailableDeploymentCost, Is.EqualTo(16));
                Assert.That(seat.Ready, Is.False);
                Assert.That(seat.Eliminated, Is.False);
                Assert.That(seat.Placement, Is.Null);
                Assert.That(seat.ConnectionState, Is.EqualTo(MatchConnectionState.Connected));
                Assert.That(seat.Units, Is.Empty);
                Assert.That(seat.ShopOffers, Is.Empty);
            }
        }

        [Test]
        public void Create_FourHumans_IsValid()
        {
            var result = MatchSessionFactory.Create(MatchTestData.Request(MatchTestData.FourHumanSeats()));

            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            Assert.That(result.Authority.ProjectForHostAuthority().Seats.All(
                seat => seat.ControllerKind == MatchControllerKind.Human), Is.True);
        }

        [Test]
        public void Create_ShuffledSeatInput_ProducesIdenticalCanonicalState()
        {
            var ordered = MatchSessionFactory.Create(MatchTestData.Request(MatchTestData.FourHumanSeats()));
            var shuffledSeats = MatchTestData.FourHumanSeats();
            shuffledSeats = new[] { shuffledSeats[2], shuffledSeats[0], shuffledSeats[3], shuffledSeats[1] };
            var shuffled = MatchSessionFactory.Create(MatchTestData.Request(shuffledSeats));

            Assert.That(ordered.Success, Is.True, ordered.DiagnosticCode);
            Assert.That(shuffled.Success, Is.True, shuffled.DiagnosticCode);
            Assert.That(shuffled.Authority.ProjectForHostAuthority().CanonicalSummary,
                Is.EqualTo(ordered.Authority.ProjectForHostAuthority().CanonicalSummary));
        }

        [Test]
        public void Create_InvalidSeatShapesAndHost_AreRejectedWithoutAuthority()
        {
            AssertRejected(
                MatchTestData.Request(MatchTestData.OneHumanSeats().Take(3).ToArray()),
                MatchInitializationCode.InvalidSeatCount);

            var duplicateIndex = MatchTestData.OneHumanSeats();
            duplicateIndex[3] = MatchTestData.Seat(3, MatchControllerKind.NativeBot, "player-4");
            AssertRejected(MatchTestData.Request(duplicateIndex), MatchInitializationCode.DuplicateSeatIndex);

            var outOfRange = MatchTestData.OneHumanSeats();
            outOfRange[3] = MatchTestData.Seat(5, MatchControllerKind.NativeBot, "player-4");
            AssertRejected(MatchTestData.Request(outOfRange), MatchInitializationCode.InvalidSeatIndex);

            var duplicatePlayer = MatchTestData.OneHumanSeats();
            duplicatePlayer[3] = MatchTestData.Seat(4, MatchControllerKind.NativeBot, "player-2");
            AssertRejected(MatchTestData.Request(duplicatePlayer), MatchInitializationCode.DuplicatePlayerId);

            AssertRejected(
                MatchTestData.Request(hostPlayerId: "missing-host"),
                MatchInitializationCode.HostMissing);

            var botHost = MatchTestData.OneHumanSeats();
            botHost[0] = MatchTestData.Seat(1, MatchControllerKind.NativeBot);
            AssertRejected(MatchTestData.Request(botHost), MatchInitializationCode.HostNotHuman);
        }

        [Test]
        public void Create_InvalidIdentitySessionSeedAndManifest_AreRejected()
        {
            AssertRejected(MatchTestData.Request(sessionId: " "), MatchInitializationCode.InvalidSessionId);
            AssertRejected(MatchTestData.Request(matchSeed: ""), MatchInitializationCode.InvalidMatchSeed);

            var invalidIdentity = MatchTestData.OneHumanSeats();
            invalidIdentity[2] = MatchTestData.Seat(3, MatchControllerKind.NativeBot, displayName: " ");
            AssertRejected(MatchTestData.Request(invalidIdentity), MatchInitializationCode.InvalidDisplayName);

            var invalidManifest = new MatchCompatibilityManifest(
                "protocol-1",
                "rules-1",
                "battle-1",
                "not-a-sha256",
                new string('b', 64));
            AssertRejected(
                MatchTestData.Request(manifest: invalidManifest),
                MatchInitializationCode.InvalidCompatibilityManifest);

            var invalidInitialValues = new MatchInitializationRequest(
                "session-1",
                "seed-1",
                "player-1",
                MatchTestData.Manifest(),
                new MatchInitialPlayerValues(-1, 7, 1, 16, 16),
                MatchTestData.OneHumanSeats());
            AssertRejected(invalidInitialValues, MatchInitializationCode.InvalidInitialValues);
        }

        private static void AssertRejected(MatchInitializationRequest request, MatchInitializationCode code)
        {
            var result = MatchSessionFactory.Create(request);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(code));
            Assert.That(result.Authority, Is.Null);
            Assert.That(result.DiagnosticCode, Is.Not.Empty);
        }
    }
}
