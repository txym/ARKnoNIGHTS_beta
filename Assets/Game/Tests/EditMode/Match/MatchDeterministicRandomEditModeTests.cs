using System;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchDeterministicRandomEditModeTests
    {
        [Test]
        public void Xoshiro256StarStarV1_HasStableGoldenVector()
        {
            var random = new MatchDeterministicRandomV1(1UL, 2UL, 3UL, 4UL);

            var actual = Enumerable.Range(0, 8).Select(_ => random.NextUInt64()).ToArray();

            Assert.That(actual, Is.EqualTo(new[]
            {
                11520UL,
                0UL,
                1509978240UL,
                1215971899390074240UL,
                1216172134540287360UL,
                607988272756665600UL,
                16172922978634559625UL,
                8476171486693032832UL
            }));
        }

        [Test]
        public void MatchSeedAndPurposeDomain_HaveStableGoldenVector()
        {
            var random = MatchDeterministicRandomV1.FromSeed(
                "seed-1",
                "match-shop-draw-v1");

            var actual = Enumerable.Range(0, 8).Select(_ => random.NextUInt64()).ToArray();

            Assert.That(actual, Is.EqualTo(new[]
            {
                4875921032619917603UL,
                17698670488889233136UL,
                10152437479947839626UL,
                4447292022972425506UL,
                1357849954873659881UL,
                14663046372372255715UL,
                15943906618867707745UL,
                12939676250317390905UL
            }));
        }

        [Test]
        public void NextBelow_RejectsZeroAndStaysBoundedForLargeLimits()
        {
            var random = new MatchDeterministicRandomV1(1UL, 2UL, 3UL, 4UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextBelow(0UL));
            var values = Enumerable.Range(0, 1000)
                .Select(_ => random.NextBelow(ulong.MaxValue - 17UL))
                .ToArray();
            Assert.That(values.All(value => value < ulong.MaxValue - 17UL), Is.True);
        }

        [Test]
        public void WeightedSelector_UsesRemainingCopyCountsAndIgnoresZeroWeights()
        {
            ulong observedMaximum = 0;
            var selected = MatchWeightedSelector.SelectIndex(
                new ulong[] { 0UL, 1UL, 28UL },
                maximum =>
                {
                    observedMaximum = maximum;
                    return maximum - 1UL;
                });

            Assert.That(observedMaximum, Is.EqualTo(29UL));
            Assert.That(selected, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => MatchWeightedSelector.SelectIndex(
                new ulong[] { 0UL, 0UL },
                maximum => 0UL));
            Assert.Throws<OverflowException>(() => MatchWeightedSelector.SelectIndex(
                new[] { ulong.MaxValue, 1UL },
                maximum => 0UL));
        }

        [Test]
        public void OddsTable_HasNineRowsOfExactlyOneHundred()
        {
            for (var level = 1; level <= 9; level++)
            {
                var weights = MatchShopOdds.GetRarityWeights(level);
                Assert.That(weights.Length, Is.EqualTo(6));
                Assert.That(weights.Sum(), Is.EqualTo(100));
            }
        }
    }
}
