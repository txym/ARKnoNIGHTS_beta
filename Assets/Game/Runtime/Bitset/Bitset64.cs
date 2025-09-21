using System;
using System.Collections.Generic;

namespace Bitsets
{
    public static class BitSet64
    {
        public static void Set(ref ulong[] bits, int index, bool value = true)
        {
            if (index < 0) return;
            int seg = index >> 6, off = index & 63;
            EnsureCapacity(ref bits, seg + 1);
            ulong mask = 1UL << off;
            if (value) bits[seg] |= mask; else bits[seg] &= ~mask;
        }

        public static bool Get(ulong[] bits, int index)
        {
            if (bits == null || index < 0) return false;
            int seg = index >> 6; if (seg >= bits.Length) return false;
            int off = index & 63; return (bits[seg] & (1UL << off)) != 0;
        }

        public static void ClearAll(ref ulong[] bits) => bits = Array.Empty<ulong>();

        public static void EnsureCapacity(ref ulong[] bits, int segCount)
        {
            if (bits == null) { bits = new ulong[segCount]; return; }
            if (bits.Length < segCount) Array.Resize(ref bits, segCount);
        }

        public static IEnumerable<int> EnumerateSetBits(ulong[] bits)
        {
            if (bits == null) yield break;
            for (int seg = 0; seg < bits.Length; seg++)
            {
                ulong w = bits[seg];
                while (w != 0)
                {
                    int tz = TrailingZeroCountCompat(w);
                    yield return (seg << 6) + tz;
                    w &= (w - 1);
                }
            }
        }

        private static int TrailingZeroCountCompat(ulong x)
        {
            if (x == 0) return 64;
            int c = 0; while ((x & 1UL) == 0) { c++; x >>= 1; }
            return c;
        }
    }
}
