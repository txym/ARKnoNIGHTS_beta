using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchSnapshotProjectionEditModeTests
    {
        [Test]
        public void PublicProjection_ContainsOnlyPublicFieldsAndMasksBotControllers()
        {
            var authority = MatchTestData.CreateAuthority();
            authority.TryEnterPreparation(1);

            var snapshot = authority.ProjectPublic();

            Assert.That(snapshot.Seats.Count, Is.EqualTo(4));
            Assert.That(snapshot.Seats.Single(seat => seat.PlayerId == "player-2").ConnectionState,
                Is.EqualTo(PublicConnectionState.Online));
            Assert.That(typeof(PublicMatchSeatSnapshot).GetProperty("Gold"), Is.Null);
            Assert.That(typeof(PublicMatchSeatSnapshot).GetProperty("Level"), Is.Null);
            Assert.That(typeof(PublicMatchSeatSnapshot).GetProperty("ControllerKind"), Is.Null);
            Assert.That(typeof(PublicMatchSeatSnapshot).GetProperty("ShopOffers"), Is.Null);
            Assert.That(typeof(PublicMatchSeatSnapshot).GetProperty("OverflowUnits"), Is.Null);
            Assert.That(typeof(PublicMatchSnapshot).GetProperty("Pool"), Is.Null);
            Assert.That(typeof(PublicMatchSnapshot).GetProperty("RandomState"), Is.Null);
        }

        [Test]
        public void PlayerProjection_ContainsOnlyTheRequestedOwnersPrivateState_EvenForHost()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());

            var playerTwo = authority.ProjectForPlayer("player-2");
            var hostPlayer = authority.ProjectForPlayer("player-1");

            Assert.That(playerTwo.Owner.PlayerId, Is.EqualTo("player-2"));
            Assert.That(playerTwo.Owner.Gold, Is.EqualTo(7));
            Assert.That(playerTwo.Owner.TotalDeploymentCost, Is.EqualTo(16));
            Assert.That(playerTwo.Owner.ShopOffers.Count, Is.EqualTo(MatchEconomyRules.ShopSlotCount));
            Assert.That(playerTwo.Owner.ShopOffers, Has.All.Matches<MatchShopOfferState>(
                offer => !offer.IsEmpty));
            Assert.That(hostPlayer.Owner.PlayerId, Is.EqualTo("player-1"));
            Assert.That(typeof(PlayerMatchSnapshot).GetProperty("AllPrivateSeats"), Is.Null);
            Assert.That(typeof(PlayerMatchSnapshot).GetProperty("Pool"), Is.Null);
            Assert.That(typeof(OwnerPrivateSnapshot).GetProperty("RandomState"), Is.Null);
            Assert.That(playerTwo.Public.CanonicalSummary, Is.EqualTo(hostPlayer.Public.CanonicalSummary));
        }

        [Test]
        public void HostProjection_ContainsFullStateAndIdempotencyRecords()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            authority.Execute(MatchTestData.Ready(authority, "player-2", "ready-record", true));

            var host = authority.ProjectForHostAuthority();

            Assert.That(host.Seats.Single(seat => seat.PlayerId == "player-3").Gold, Is.EqualTo(7));
            Assert.That(host.Seats.Single(seat => seat.PlayerId == "player-3").ControllerKind,
                Is.EqualTo(MatchControllerKind.Human));
            Assert.That(host.CommandRecords.Count, Is.EqualTo(1));
            Assert.That(host.CommandRecords[0].PlayerId, Is.EqualTo("player-2"));
            Assert.That(host.CommandRecords[0].CommandId, Is.EqualTo("ready-record"));
            Assert.That(host.CommandRecords[0].Result.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(host.Pool.Entities, Is.Not.Empty);
            Assert.That(host.Pool.RandomState, Is.Not.Null);
            Assert.That(host.Pool.RemainingByType, Is.Not.Empty);
        }

        [Test]
        public void ProjectionCollections_AreDefensiveAndCannotMutateAuthority()
        {
            var authority = MatchTestData.CreateAuthority();
            var publicSnapshot = authority.ProjectPublic();
            var original = authority.ProjectForHostAuthority().CanonicalSummary;
            var seats = (IList<PublicMatchSeatSnapshot>)publicSnapshot.Seats;

            Assert.Throws<NotSupportedException>(() => seats[0] = seats[1]);
            Assert.That(authority.ProjectForHostAuthority().CanonicalSummary, Is.EqualTo(original));
        }

        [Test]
        public void CanonicalSummaries_AreStableAcrossProjectionAndCulture()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
                var french = authority.ProjectForHostAuthority().CanonicalSummary;
                var frenchPublic = authority.ProjectPublic().CanonicalSummary;

                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
                Assert.That(authority.ProjectForHostAuthority().CanonicalSummary, Is.EqualTo(french));
                Assert.That(authority.ProjectPublic().CanonicalSummary, Is.EqualTo(frenchPublic));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }
    }
}
