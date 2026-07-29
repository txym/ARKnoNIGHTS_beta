using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Match
{
    public enum MatchShopCatalogValidationCode
    {
        Accepted = 0,
        InvalidRulesVersion = 1,
        InvalidSourceCatalogHash = 2,
        EmptyCatalog = 3,
        InvalidEntry = 4,
        DuplicateTypeId = 5,
        NoShopEligibleEntries = 6
    }

    public sealed class MatchShopCatalogEntry
    {
        public MatchShopCatalogEntry(
            string typeId,
            int rarity,
            bool isShopEligible,
            int maxEliteLevel,
            int baseDeploymentCost,
            long numericTypeId)
        {
            TypeId = typeId;
            Rarity = rarity;
            IsShopEligible = isShopEligible;
            MaxEliteLevel = maxEliteLevel;
            BaseDeploymentCost = baseDeploymentCost;
            NumericTypeId = numericTypeId;

            var writer = new CanonicalSummaryWriter(nameof(MatchShopCatalogEntry));
            writer.String("typeId", TypeId);
            writer.Integer("numericTypeId", NumericTypeId);
            writer.Integer("rarity", Rarity);
            writer.Boolean("shopEligible", IsShopEligible);
            writer.Integer("maxEliteLevel", MaxEliteLevel);
            writer.Integer("baseDeploymentCost", BaseDeploymentCost);
            CanonicalSummary = writer.ToString();
        }

        public string TypeId { get; }
        public long NumericTypeId { get; }
        public int Rarity { get; }
        public bool IsShopEligible { get; }
        public int MaxEliteLevel { get; }
        public int BaseDeploymentCost { get; }
        public string CanonicalSummary { get; }

        internal bool IsValid =>
            !string.IsNullOrWhiteSpace(TypeId)
            && TypeId.Length <= 128
            && NumericTypeId >= 0
            && Rarity >= 1
            && Rarity <= 6
            && MaxEliteLevel >= 0
            && MaxEliteLevel <= 3
            && BaseDeploymentCost >= 0;
    }

    public sealed class MatchShopCatalog
    {
        private readonly Dictionary<string, MatchShopCatalogEntry> entriesByTypeId;

        public MatchShopCatalog(
            string rulesVersion,
            string sourceUnitCatalogSha256,
            IEnumerable<MatchShopCatalogEntry> entries)
        {
            RulesVersion = rulesVersion;
            SourceUnitCatalogSha256 = sourceUnitCatalogSha256;
            Entries = new ReadOnlyCollection<MatchShopCatalogEntry>(
                (entries ?? Enumerable.Empty<MatchShopCatalogEntry>())
                    .OrderBy(entry => entry == null ? long.MinValue : entry.NumericTypeId)
                    .ThenBy(entry => entry == null ? string.Empty : entry.TypeId, StringComparer.Ordinal)
                    .ToArray());
            entriesByTypeId = Entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.TypeId))
                .GroupBy(entry => entry.TypeId, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

            var writer = new CanonicalSummaryWriter(nameof(MatchShopCatalog));
            writer.String("rulesVersion", RulesVersion);
            writer.String("sourceUnitCatalogSha256", SourceUnitCatalogSha256);
            foreach (var entry in Entries)
            {
                writer.Summary("entry", entry == null ? string.Empty : entry.CanonicalSummary);
            }
            CanonicalSummary = writer.ToString();
        }

        public string RulesVersion { get; }
        public string SourceUnitCatalogSha256 { get; }
        public IReadOnlyList<MatchShopCatalogEntry> Entries { get; }
        public string CanonicalSummary { get; }

        public bool TryValidate(
            out MatchShopCatalogValidationCode code,
            out string diagnosticCode)
        {
            if (!IsVersionToken(RulesVersion))
            {
                code = MatchShopCatalogValidationCode.InvalidRulesVersion;
                diagnosticCode = "match.catalog.rulesVersion.invalid";
                return false;
            }
            if (!IsSha256(SourceUnitCatalogSha256))
            {
                code = MatchShopCatalogValidationCode.InvalidSourceCatalogHash;
                diagnosticCode = "match.catalog.sourceHash.invalid";
                return false;
            }
            if (Entries.Count == 0)
            {
                code = MatchShopCatalogValidationCode.EmptyCatalog;
                diagnosticCode = "match.catalog.entries.empty";
                return false;
            }
            if (Entries.Any(entry => entry == null || !entry.IsValid))
            {
                code = MatchShopCatalogValidationCode.InvalidEntry;
                diagnosticCode = "match.catalog.entry.invalid";
                return false;
            }
            if (Entries.GroupBy(entry => entry.TypeId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                code = MatchShopCatalogValidationCode.DuplicateTypeId;
                diagnosticCode = "match.catalog.typeId.duplicate";
                return false;
            }
            if (!Entries.Any(entry => entry.IsShopEligible))
            {
                code = MatchShopCatalogValidationCode.NoShopEligibleEntries;
                diagnosticCode = "match.catalog.shopEligible.empty";
                return false;
            }

            code = MatchShopCatalogValidationCode.Accepted;
            diagnosticCode = string.Empty;
            return true;
        }

        public bool IsCompatibleWith(MatchCompatibilityManifest manifest)
        {
            return manifest != null
                && manifest.IsValid
                && string.Equals(RulesVersion, manifest.MatchRulesVersion, StringComparison.Ordinal)
                && string.Equals(
                    SourceUnitCatalogSha256,
                    manifest.UnitCatalogSha256,
                    StringComparison.Ordinal);
        }

        public bool TryGet(string typeId, out MatchShopCatalogEntry entry)
        {
            return entriesByTypeId.TryGetValue(typeId ?? string.Empty, out entry);
        }

        private static bool IsVersionToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '.'
                    || character == '-'
                    || character == '_'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            return value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F'));
        }
    }
}
