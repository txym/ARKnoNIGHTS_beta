using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Player
{
    internal static class ShopOfferGenerator
    {
        private static readonly int[][] RarityWeightsByLevel =
        {
            new[] { 80, 20, 0, 0, 0, 0 },
            new[] { 65, 35, 0, 0, 0, 0 },
            new[] { 50, 40, 10, 0, 0, 0 },
            new[] { 38, 40, 20, 2, 0, 0 },
            new[] { 27, 38, 27, 8, 0, 0 },
            new[] { 18, 32, 30, 18, 2, 0 },
            new[] { 11, 24, 30, 28, 6, 1 },
            new[] { 5, 14, 24, 39, 16, 2 },
            new[] { 2, 7, 11, 40, 30, 10 }
        };

        public static int[] GetRarityWeights(int playerLevel)
        {
            if (playerLevel < LocalMatchState.InitialLevel || playerLevel > LocalMatchState.MaximumLevel)
                throw new ArgumentOutOfRangeException(nameof(playerLevel));

            return RarityWeightsByLevel[playerLevel - LocalMatchState.InitialLevel].ToArray();
        }

        public static string[] Generate(
            int playerLevel,
            int offerCount,
            IEnumerable<string> availableTypeIds,
            Func<string, int> rarityByTypeId,
            Func<int, int> nextExclusive)
        {
            if (offerCount < 0) throw new ArgumentOutOfRangeException(nameof(offerCount));
            if (availableTypeIds == null) throw new ArgumentNullException(nameof(availableTypeIds));
            if (rarityByTypeId == null) throw new ArgumentNullException(nameof(rarityByTypeId));
            if (nextExclusive == null) throw new ArgumentNullException(nameof(nextExclusive));

            var candidatesByRarity = availableTypeIds
                .Where(typeId => !string.IsNullOrWhiteSpace(typeId))
                .Distinct(StringComparer.Ordinal)
                .Select(typeId => new Candidate(typeId, rarityByTypeId(typeId)))
                .GroupBy(candidate => candidate.Rarity)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(candidate => candidate.TypeId).OrderBy(typeId => typeId, StringComparer.Ordinal).ToArray());
            if (candidatesByRarity.Keys.Any(rarity => rarity < 1 || rarity > 6))
                throw new InvalidOperationException("Shop offer rarity must be in range 1..6.");

            var weights = GetRarityWeights(playerLevel);
            var availableRarities = Enumerable.Range(1, weights.Length)
                .Where(rarity => weights[rarity - 1] > 0 && candidatesByRarity.ContainsKey(rarity))
                .ToArray();
            var availableWeight = availableRarities.Sum(rarity => weights[rarity - 1]);
            if (availableWeight <= 0)
                throw new InvalidOperationException("No shop rarity with a positive level weight is available.");

            var offers = new string[offerCount];
            for (var offerIndex = 0; offerIndex < offers.Length; offerIndex++)
            {
                var rarityRoll = Next(nextExclusive, availableWeight);
                var selectedRarity = availableRarities[0];
                foreach (var rarity in availableRarities)
                {
                    if (rarityRoll < weights[rarity - 1])
                    {
                        selectedRarity = rarity;
                        break;
                    }

                    rarityRoll -= weights[rarity - 1];
                }

                var candidates = candidatesByRarity[selectedRarity];
                offers[offerIndex] = candidates[Next(nextExclusive, candidates.Length)];
            }

            return offers;
        }

        private static int Next(Func<int, int> nextExclusive, int maximumExclusive)
        {
            var value = nextExclusive(maximumExclusive);
            if (value < 0 || value >= maximumExclusive)
                throw new InvalidOperationException(
                    "Shop random source returned " + value + " outside 0.." + (maximumExclusive - 1) + ".");
            return value;
        }

        private readonly struct Candidate
        {
            public Candidate(string typeId, int rarity)
            {
                TypeId = typeId;
                Rarity = rarity;
            }

            public string TypeId { get; }
            public int Rarity { get; }
        }
    }
}
