using System;
using System.Globalization;

namespace ArknoNights.Battle.Core
{
    public sealed class DynamicUnitIdAllocator
    {
        private long next = -1;

        public string Allocate()
        {
            if (next == long.MinValue)
                throw new InvalidOperationException("Dynamic unit ID allocation has reached the Int64 minimum value.");

            var allocated = next.ToString(CultureInfo.InvariantCulture);
            next--;
            return allocated;
        }
    }
}
