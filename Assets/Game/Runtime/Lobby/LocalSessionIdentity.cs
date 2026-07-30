using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ArknoNights.Match;

namespace ArknoNights.Lobby
{
    public interface ILocalProfileIdentityStore
    {
        bool TryLoad(out string playerId);
        void Save(string playerId);
        void Clear();
    }

    public static class LocalProfileIdentity
    {
        private const string Prefix = "lan-";

        public static string GetOrCreate(ILocalProfileIdentityStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (store.TryLoad(out var existing) && IsValid(existing)) return existing;
            store.Clear();
            var bytes = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var playerId = Prefix + ToLowerHex(bytes);
            store.Save(playerId);
            return playerId;
        }

        public static bool IsValid(string playerId)
        {
            if (playerId == null || playerId.Length != Prefix.Length + 32
                || !playerId.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }
            return playerId.Skip(Prefix.Length).All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (var index = 0; index < bytes.Length; index++)
                builder.Append(bytes[index].ToString("x2"));
            return builder.ToString();
        }
    }

    public sealed class IssuedReconnectToken
    {
        internal IssuedReconnectToken(string rawToken, byte[] verifierSha256)
        {
            RawToken = rawToken;
            this.verifierSha256 = (byte[])verifierSha256.Clone();
        }

        public string RawToken { get; }
        public byte[] VerifierSha256 => (byte[])verifierSha256.Clone();

        private readonly byte[] verifierSha256;
    }

    public static class ReconnectTokenIssuer
    {
        public const int RawTokenCharacters = 43;

        public static IssuedReconnectToken Issue()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var token = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return new IssuedReconnectToken(token, Hash(token));
        }

        public static bool IsValidRawToken(string rawToken)
        {
            return rawToken != null
                && rawToken.Length == RawTokenCharacters
                && rawToken.All(character =>
                    (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '-'
                    || character == '_');
        }

        public static bool Verify(string rawToken, byte[] verifierSha256)
        {
            var candidate = string.IsNullOrEmpty(rawToken)
                ? new byte[32]
                : Hash(rawToken);
            var expected = verifierSha256 ?? Array.Empty<byte>();
            var different = candidate.Length ^ expected.Length;
            var maximum = Math.Max(candidate.Length, expected.Length);
            for (var index = 0; index < maximum; index++)
            {
                var left = index < candidate.Length ? candidate[index] : (byte)0;
                var right = index < expected.Length ? expected[index] : (byte)0;
                different |= left ^ right;
            }
            return different == 0;
        }

        private static byte[] Hash(string token)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(token));
        }
    }

    public static class ReconnectRetrySchedule
    {
        private static readonly int[] Delays = { 0, 500, 1000, 2000, 5000 };

        public static int GetDelayMilliseconds(int attempt)
        {
            if (attempt < 0) throw new ArgumentOutOfRangeException(nameof(attempt));
            return Delays[Math.Min(attempt, Delays.Length - 1)];
        }
    }

    public sealed class ReconnectCredential
    {
        public ReconnectCredential(
            string sessionId,
            string hostAddress,
            int hostPort,
            string playerId,
            string token,
            MatchCompatibilityManifest compatibilityManifest)
        {
            SessionId = sessionId;
            HostAddress = hostAddress;
            HostPort = hostPort;
            PlayerId = playerId;
            Token = token;
            CompatibilityManifest = compatibilityManifest;
        }

        public string SessionId { get; }
        public string HostAddress { get; }
        public int HostPort { get; }
        public string PlayerId { get; }
        public string Token { get; }
        public MatchCompatibilityManifest CompatibilityManifest { get; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(SessionId)
            && SessionId.Length <= 128
            && !string.IsNullOrWhiteSpace(HostAddress)
            && HostAddress.Length <= 255
            && HostPort >= 1 && HostPort <= 65535
            && LocalProfileIdentity.IsValid(PlayerId)
            && ReconnectTokenIssuer.IsValidRawToken(Token)
            && CompatibilityManifest != null
            && CompatibilityManifest.IsValid;
    }

    public interface IReconnectCredentialStore
    {
        bool TryLoad(out ReconnectCredential credential);
        void Save(ReconnectCredential credential);
        void Clear();
    }

    public static class ReconnectCredentialPolicy
    {
        public static bool ShouldClearOnAuthoritativeRejection(
            MatchReconnectRejectCode code)
        {
            return code
                != MatchReconnectRejectCode.CompatibilityMismatch;
        }
    }

    public sealed class ScopedSnapshotClientState
    {
        public event Action<ScopedSnapshotPayload> Changed;

        public ScopedSnapshotPayload Current { get; private set; }

        public bool TryApply(ScopedSnapshotPayload snapshot)
        {
            if (snapshot == null
                || snapshot.StateRevision < 0
                || (Current != null && snapshot.StateRevision <= Current.StateRevision))
            {
                return false;
            }
            Current = snapshot;
            Changed?.Invoke(snapshot);
            return true;
        }
    }
}
