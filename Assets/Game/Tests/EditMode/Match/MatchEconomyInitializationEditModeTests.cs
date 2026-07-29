using System;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchEconomyInitializationEditModeTests
    {
        [Test]
        public void Create_InvalidCatalogShapes_AreRejectedWithoutPartialAuthority()
        {
            var missingCatalog = new MatchInitializationRequest(
                "session-1",
                "seed-1",
                "player-1",
                MatchTestData.Manifest(),
                (MatchShopCatalog)null,
                MatchTestData.OneHumanSeats());
            AssertRejected(MatchSessionFactory.Create(missingCatalog));
            AssertRejected(new MatchShopCatalog("rules-1", new string('a', 64), Array.Empty<MatchShopCatalogEntry>()));
            AssertRejected(MatchTestData.Catalog(
                MatchTestData.Entry("1001", 1),
                MatchTestData.Entry("1001", 1)));
            AssertRejected(MatchTestData.Catalog(MatchTestData.Entry("1001", 0)));
            AssertRejected(MatchTestData.Catalog(MatchTestData.Entry("1001", 1, maxEliteLevel: 4)));
            AssertRejected(MatchTestData.Catalog(MatchTestData.Entry("1001", 1, baseDeploymentCost: -1)));
            AssertRejected(MatchTestData.Catalog(
                MatchTestData.Entry("9001", 1, isShopEligible: false)));
        }

        [Test]
        public void Create_CatalogCompatibilityMismatch_IsRejected()
        {
            var catalog = new MatchShopCatalog(
                "rules-other",
                new string('c', 64),
                new[] { MatchTestData.Entry("1001", 1) });

            var result = MatchSessionFactory.Create(MatchTestData.Request(shopCatalog: catalog));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(MatchInitializationCode.ShopCatalogCompatibilityMismatch));
            Assert.That(result.Authority, Is.Null);
        }

        [Test]
        public void Create_OnlyEligibleTypesEnterPool_WithExactRarityCopyCounts()
        {
            var catalog = MatchTestData.Catalog(
                MatchTestData.Entry("1001", 1),
                MatchTestData.Entry("2001", 2),
                MatchTestData.Entry("3001", 3),
                MatchTestData.Entry("4001", 4),
                MatchTestData.Entry("5001", 5),
                MatchTestData.Entry("6001", 6),
                MatchTestData.Entry("9001", 1, isShopEligible: false));

            var result = MatchSessionFactory.Create(MatchTestData.Request(shopCatalog: catalog));

            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            var entities = result.Authority.ProjectForHostAuthority().Pool.Entities;
            Assert.That(entities.GroupBy(entity => entity.TypeId).ToDictionary(
                group => group.Key,
                group => group.Count()), Is.EqualTo(new System.Collections.Generic.Dictionary<string, int>
                {
                    { "1001", 28 },
                    { "2001", 24 },
                    { "3001", 14 },
                    { "4001", 10 },
                    { "5001", 8 },
                    { "6001", 6 }
                }));
            Assert.That(entities.Any(entity => entity.TypeId == "9001"), Is.False);
            Assert.That(entities.Select(entity => entity.UnitId).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(entities.Count));
            Assert.That(entities.All(entity => MatchPoolUnitId.IsValid(entity.UnitId)), Is.True);
        }

        [Test]
        public void Create_SameStableInputsReproducePoolAndInitialShops_SeedOnlyChangesDrawSequence()
        {
            var first = MatchSessionFactory.Create(MatchTestData.Request(matchSeed: "seed-A"));
            var repeated = MatchSessionFactory.Create(MatchTestData.Request(matchSeed: "seed-A"));
            var otherSeed = MatchSessionFactory.Create(MatchTestData.Request(matchSeed: "seed-B"));

            Assert.That(first.Success && repeated.Success && otherSeed.Success, Is.True);
            Assert.That(repeated.Authority.ProjectForHostAuthority().CanonicalSummary,
                Is.EqualTo(first.Authority.ProjectForHostAuthority().CanonicalSummary));
            Assert.That(otherSeed.Authority.ProjectForHostAuthority().Pool.Entities.Select(entity => entity.UnitId),
                Is.EqualTo(first.Authority.ProjectForHostAuthority().Pool.Entities.Select(entity => entity.UnitId)));
            Assert.That(ShopTypeSequence(otherSeed.Authority), Is.Not.EqualTo(ShopTypeSequence(first.Authority)));
        }

        [Test]
        public void Create_InitialNaturalRefreshIsAtomicFreeAndStartsFairCursorAtSeatTwo()
        {
            var result = MatchSessionFactory.Create(MatchTestData.Request(
                seats: MatchTestData.FourHumanSeats()));

            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            var host = result.Authority.ProjectForHostAuthority();
            Assert.That(host.StateRevision, Is.Zero);
            Assert.That(host.Seats.Select(seat => seat.Gold), Is.All.EqualTo(7));
            Assert.That(host.Seats.Select(seat => seat.UpgradeDiscountCountAtThisLevel), Is.All.Zero);
            Assert.That(host.Seats.SelectMany(seat => seat.ShopOffers).Count(), Is.EqualTo(24));
            Assert.That(host.Seats.SelectMany(seat => seat.ShopOffers).All(
                offer => !string.IsNullOrEmpty(offer.UnitId)), Is.True);
            Assert.That(host.Pool.NextNaturalRefreshStartSeat, Is.EqualTo(2));
            Assert.That(host.Pool.Entities.Count(entity => entity.Location == MatchPoolEntityLocation.ShopOffer),
                Is.EqualTo(24));
        }

        [Test]
        public void Create_InitialFairBatchInterleavesPlayersBySlot()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var result = MatchSessionFactory.Create(MatchTestData.Request(
                seats: MatchTestData.FourHumanSeats(),
                shopCatalog: catalog));

            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            var host = result.Authority.ProjectForHostAuthority();
            for (var seatIndex = 1; seatIndex <= 4; seatIndex++)
            {
                var seat = host.Seats.Single(candidate => candidate.SeatIndex == seatIndex);
                var copyIndexes = seat.ShopOffers
                    .OrderBy(offer => offer.SlotIndex)
                    .Select(offer => host.Pool.Entities.Single(entity => entity.UnitId == offer.UnitId).CopyIndex)
                    .ToArray();
                Assert.That(copyIndexes, Is.EqualTo(Enumerable.Range(0, 6)
                    .Select(slotOffset => seatIndex + (slotOffset * 4))
                    .ToArray()));
            }
        }

        [Test]
        public void Create_WhenCurrentLevelHasNoPositiveWeightPool_LeavesSlotsEmptyWithDiagnostic()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("6001", 6));

            var result = MatchSessionFactory.Create(MatchTestData.Request(shopCatalog: catalog));

            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            Assert.That(result.PoolExhaustedDiagnostic, Is.True);
            Assert.That(result.Authority.ProjectForHostAuthority().Seats.SelectMany(
                seat => seat.ShopOffers).All(offer => offer.IsEmpty), Is.True);
            Assert.That(result.Authority.ProjectForHostAuthority().Pool.Entities.All(
                entity => entity.Location == MatchPoolEntityLocation.AvailablePool), Is.True);
        }

        [Test]
        public void Create_ConditionsRarityWeightsOnAvailablePositiveWeightRarities()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("2001", 2));

            var result = MatchSessionFactory.Create(MatchTestData.Request(shopCatalog: catalog));

            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            var host = result.Authority.ProjectForHostAuthority();
            Assert.That(host.Seats.SelectMany(seat => seat.ShopOffers), Has.All.Matches<MatchShopOfferState>(
                offer => offer.Rarity == 2));
            Assert.That(host.Pool.Entities.Count(entity =>
                entity.Location == MatchPoolEntityLocation.ShopOffer), Is.EqualTo(24));
            Assert.That(host.Pool.Entities.Count(entity =>
                entity.Location == MatchPoolEntityLocation.AvailablePool), Is.Zero);
        }

        [Test]
        public void Create_ShuffledCatalogInput_ProducesIdenticalCanonicalState()
        {
            var entries = new[]
            {
                MatchTestData.Entry("1002", 1),
                MatchTestData.Entry("1001", 1),
                MatchTestData.Entry("2001", 2)
            };
            var ordered = MatchSessionFactory.Create(MatchTestData.Request(
                shopCatalog: MatchTestData.Catalog(entries)));
            var shuffled = MatchSessionFactory.Create(MatchTestData.Request(
                shopCatalog: MatchTestData.Catalog(entries.Reverse().ToArray())));

            Assert.That(ordered.Success && shuffled.Success, Is.True);
            Assert.That(shuffled.Authority.ProjectForHostAuthority().CanonicalSummary,
                Is.EqualTo(ordered.Authority.ProjectForHostAuthority().CanonicalSummary));
        }

        private static string[] ShopTypeSequence(MatchAuthority authority)
        {
            return authority.ProjectForHostAuthority().Seats
                .OrderBy(seat => seat.SeatIndex)
                .SelectMany(seat => seat.ShopOffers.OrderBy(offer => offer.SlotIndex))
                .Select(offer => offer.TypeId)
                .ToArray();
        }

        private static void AssertRejected(MatchShopCatalog catalog)
        {
            AssertRejected(MatchSessionFactory.Create(MatchTestData.Request(shopCatalog: catalog)));
        }

        private static void AssertRejected(MatchInitializationResult result)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(MatchInitializationCode.InvalidShopCatalog));
            Assert.That(result.Authority, Is.Null);
            Assert.That(result.DiagnosticCode, Is.Not.Empty);
        }
    }
}
