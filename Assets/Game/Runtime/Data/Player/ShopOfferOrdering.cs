using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ArknoNights.Player
{
    internal static class ShopOfferOrdering
    {
        public static string[] Sort(IEnumerable<string> generatedTypeIds, Func<string, int> rarityByTypeId)
        {
            if (generatedTypeIds == null) throw new ArgumentNullException(nameof(generatedTypeIds));
            if (rarityByTypeId == null) throw new ArgumentNullException(nameof(rarityByTypeId));

            return generatedTypeIds
                .Select((typeId, generatedIndex) => new OrderedOffer(
                    typeId,
                    rarityByTypeId(typeId),
                    int.Parse(typeId, NumberStyles.None, CultureInfo.InvariantCulture),
                    generatedIndex))
                .OrderBy(offer => offer.Rarity)
                .ThenBy(offer => offer.NumericTypeId)
                .ThenBy(offer => offer.GeneratedIndex)
                .Select(offer => offer.TypeId)
                .ToArray();
        }

        private readonly struct OrderedOffer
        {
            public OrderedOffer(string typeId, int rarity, int numericTypeId, int generatedIndex)
            {
                TypeId = typeId;
                Rarity = rarity;
                NumericTypeId = numericTypeId;
                GeneratedIndex = generatedIndex;
            }

            public string TypeId { get; }
            public int Rarity { get; }
            public int NumericTypeId { get; }
            public int GeneratedIndex { get; }
        }
    }
}
