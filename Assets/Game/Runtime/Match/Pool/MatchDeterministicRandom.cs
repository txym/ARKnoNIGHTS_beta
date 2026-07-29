using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ArknoNights.Match
{
    public sealed class MatchRandomStateSnapshot
    {
        public MatchRandomStateSnapshot(ulong state0, ulong state1, ulong state2, ulong state3)
        {
            State0 = state0;
            State1 = state1;
            State2 = state2;
            State3 = state3;
            var writer = new CanonicalSummaryWriter(nameof(MatchRandomStateSnapshot));
            writer.UnsignedInteger("state0", State0);
            writer.UnsignedInteger("state1", State1);
            writer.UnsignedInteger("state2", State2);
            writer.UnsignedInteger("state3", State3);
            CanonicalSummary = writer.ToString();
        }

        public ulong State0 { get; }
        public ulong State1 { get; }
        public ulong State2 { get; }
        public ulong State3 { get; }
        public string CanonicalSummary { get; }
        public bool IsValid => (State0 | State1 | State2 | State3) != 0UL;
    }

    /// <summary>
    /// Fixed xoshiro256** 1.0 implementation. State encoding and output are intentionally versioned;
    /// changing this algorithm requires a Match rules version change.
    /// </summary>
    internal sealed class MatchDeterministicRandomV1
    {
        internal const string AlgorithmVersion = "xoshiro256starstar-v1";
        private ulong state0;
        private ulong state1;
        private ulong state2;
        private ulong state3;

        internal MatchDeterministicRandomV1(
            ulong state0,
            ulong state1,
            ulong state2,
            ulong state3)
        {
            if ((state0 | state1 | state2 | state3) == 0UL)
            {
                throw new ArgumentException("The deterministic random state cannot be all zero.");
            }
            this.state0 = state0;
            this.state1 = state1;
            this.state2 = state2;
            this.state3 = state3;
        }

        internal static MatchDeterministicRandomV1 FromSeed(string matchSeed, string purposeDomain)
        {
            var writer = new CanonicalSummaryWriter("MatchRandomSeedV1");
            writer.String("matchSeed", matchSeed);
            writer.String("purposeDomain", purposeDomain);
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(writer.ToString()));
            }

            var result = new MatchDeterministicRandomV1(
                ReadUInt64LittleEndian(hash, 0),
                ReadUInt64LittleEndian(hash, 8),
                ReadUInt64LittleEndian(hash, 16),
                ReadUInt64LittleEndian(hash, 24));
            return result;
        }

        internal static MatchDeterministicRandomV1 FromState(MatchRandomStateSnapshot state)
        {
            if (state == null || !state.IsValid) throw new ArgumentException("Random state is invalid.", nameof(state));
            return new MatchDeterministicRandomV1(
                state.State0,
                state.State1,
                state.State2,
                state.State3);
        }

        internal MatchRandomStateSnapshot Snapshot =>
            new MatchRandomStateSnapshot(state0, state1, state2, state3);

        internal ulong NextUInt64()
        {
            var result = RotateLeft(unchecked(state1 * 5UL), 7);
            result = unchecked(result * 9UL);
            var temporary = unchecked(state1 << 17);

            state2 ^= state0;
            state3 ^= state1;
            state1 ^= state2;
            state0 ^= state3;
            state2 ^= temporary;
            state3 = RotateLeft(state3, 45);
            return result;
        }

        internal ulong NextBelow(ulong maximumExclusive)
        {
            if (maximumExclusive == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
            }

            var rejectionThreshold = unchecked(0UL - maximumExclusive) % maximumExclusive;
            while (true)
            {
                var value = NextUInt64();
                if (value >= rejectionThreshold)
                {
                    return value % maximumExclusive;
                }
            }
        }

        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }

        private static ulong ReadUInt64LittleEndian(byte[] bytes, int offset)
        {
            ulong value = 0UL;
            for (var index = 0; index < 8; index++)
            {
                value |= ((ulong)bytes[offset + index]) << (index * 8);
            }
            return value;
        }
    }

    internal static class MatchWeightedSelector
    {
        internal static int SelectIndex(
            IReadOnlyList<ulong> weights,
            Func<ulong, ulong> nextBelow)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (nextBelow == null) throw new ArgumentNullException(nameof(nextBelow));
            ulong total = 0UL;
            foreach (var weight in weights)
            {
                checked
                {
                    total += weight;
                }
            }
            if (total == 0UL) throw new ArgumentException("At least one weight must be positive.", nameof(weights));

            var roll = nextBelow(total);
            if (roll >= total)
            {
                throw new InvalidOperationException("The random source returned a value outside the requested bound.");
            }
            for (var index = 0; index < weights.Count; index++)
            {
                if (roll < weights[index]) return index;
                roll -= weights[index];
            }
            throw new InvalidOperationException("Weighted selection failed to resolve a positive bucket.");
        }
    }

    public static class MatchPoolUnitId
    {
        private const string Prefix = "pool1-";

        public static string Create(string sessionId, string typeId, int copyIndex)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session ID is required.", nameof(sessionId));
            if (string.IsNullOrWhiteSpace(typeId)) throw new ArgumentException("Type ID is required.", nameof(typeId));
            if (copyIndex <= 0) throw new ArgumentOutOfRangeException(nameof(copyIndex));

            var writer = new CanonicalSummaryWriter("MatchPoolUnitIdV1");
            writer.String("sessionId", sessionId);
            writer.String("typeId", typeId);
            writer.Integer("copyIndex", copyIndex);
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(writer.ToString()));
            }
            var hex = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                hex.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return Prefix + hex;
        }

        public static bool IsValid(string unitId)
        {
            if (unitId == null || unitId.Length != Prefix.Length + 64
                || !unitId.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }
            return unitId.Skip(Prefix.Length).All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));
        }
    }
}
