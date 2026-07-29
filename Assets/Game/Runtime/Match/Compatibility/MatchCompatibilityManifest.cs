using System;

namespace ArknoNights.Match
{
    public sealed class MatchCompatibilityManifest
    {
        public MatchCompatibilityManifest(
            string protocolVersion,
            string matchRulesVersion,
            string battleCoreVersion,
            string unitCatalogSha256,
            string abilityCatalogSha256)
        {
            ProtocolVersion = protocolVersion;
            MatchRulesVersion = matchRulesVersion;
            BattleCoreVersion = battleCoreVersion;
            UnitCatalogSha256 = unitCatalogSha256;
            AbilityCatalogSha256 = abilityCatalogSha256;

            var writer = new CanonicalSummaryWriter(nameof(MatchCompatibilityManifest));
            writer.String("protocol", ProtocolVersion);
            writer.String("matchRules", MatchRulesVersion);
            writer.String("battleCore", BattleCoreVersion);
            writer.String("unitCatalogSha256", UnitCatalogSha256);
            writer.String("abilityCatalogSha256", AbilityCatalogSha256);
            CanonicalSummary = writer.ToString();
        }

        public string ProtocolVersion { get; }
        public string MatchRulesVersion { get; }
        public string BattleCoreVersion { get; }
        public string UnitCatalogSha256 { get; }
        public string AbilityCatalogSha256 { get; }
        public string CanonicalSummary { get; }

        public bool IsValid =>
            IsVersionToken(ProtocolVersion)
            && IsVersionToken(MatchRulesVersion)
            && IsVersionToken(BattleCoreVersion)
            && IsSha256(UnitCatalogSha256)
            && IsSha256(AbilityCatalogSha256);

        public bool IsCompatibleWith(MatchCompatibilityManifest other)
        {
            return other != null
                && IsValid
                && other.IsValid
                && string.Equals(ProtocolVersion, other.ProtocolVersion, StringComparison.Ordinal)
                && string.Equals(MatchRulesVersion, other.MatchRulesVersion, StringComparison.Ordinal)
                && string.Equals(BattleCoreVersion, other.BattleCoreVersion, StringComparison.Ordinal)
                && string.Equals(UnitCatalogSha256, other.UnitCatalogSha256, StringComparison.Ordinal)
                && string.Equals(AbilityCatalogSha256, other.AbilityCatalogSha256, StringComparison.Ordinal);
        }

        private static bool IsVersionToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var valid = character >= 'a' && character <= 'z'
                    || character >= 'A' && character <= 'Z'
                    || character >= '0' && character <= '9'
                    || character == '.'
                    || character == '-'
                    || character == '_';
                if (!valid) return false;
            }

            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
