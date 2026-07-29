using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace ArknoNights.UI
{
    /// <summary>UI-only unit region/category presentation resolved from a versioned Resources document.</summary>
    public sealed class UnitAffinityPresentation
    {
        internal UnitAffinityPresentation(
            string typeId,
            string regionName,
            string regionIconResourcePath,
            string occupationName,
            string occupationIconResourcePath)
        {
            TypeId = typeId;
            RegionName = regionName ?? string.Empty;
            RegionIconResourcePath = regionIconResourcePath ?? string.Empty;
            OccupationName = occupationName ?? string.Empty;
            OccupationIconResourcePath = occupationIconResourcePath ?? string.Empty;
        }

        public string TypeId { get; }
        public string RegionName { get; }
        public string RegionIconResourcePath { get; }
        public string OccupationName { get; }
        public string OccupationIconResourcePath { get; }
        public string PreferredHeaderIconResourcePath =>
            !string.IsNullOrEmpty(RegionIconResourcePath)
                ? RegionIconResourcePath
                : OccupationIconResourcePath;
    }

    /// <summary>
    /// Loads the Player-safe presentation mirror of BONDS data. It never calculates affinity state or buffs.
    /// </summary>
    public sealed class UnitAffinityPresentationCatalog
    {
        public const string SchemaVersion = "unit-affinity-presentation-v1";
        public const string ResourcePath = "UI/Data/unit-affinity-presentation-v1";

        private readonly IReadOnlyDictionary<string, UnitAffinityPresentation> entriesByTypeId;

        private UnitAffinityPresentationCatalog(IEnumerable<UnitAffinityPresentation> entries)
        {
            var ordered = new List<UnitAffinityPresentation>(entries ?? Array.Empty<UnitAffinityPresentation>());
            Entries = new ReadOnlyCollection<UnitAffinityPresentation>(ordered);
            var lookup = new Dictionary<string, UnitAffinityPresentation>(StringComparer.Ordinal);
            foreach (var entry in ordered) lookup.Add(entry.TypeId, entry);
            entriesByTypeId = new ReadOnlyDictionary<string, UnitAffinityPresentation>(lookup);
        }

        public IReadOnlyList<UnitAffinityPresentation> Entries { get; }

        public bool TryGet(string typeId, out UnitAffinityPresentation value)
        {
            if (string.IsNullOrWhiteSpace(typeId))
            {
                value = null;
                return false;
            }

            return entriesByTypeId.TryGetValue(typeId, out value);
        }

        public static UnitAffinityPresentationCatalog LoadFromResources()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError("[AffinityUI][catalog.resource.missing] path=" + ResourcePath);
                return Empty();
            }

            try
            {
                return Parse(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[AffinityUI][catalog.resource.invalid] path=" + ResourcePath
                    + "; type=" + exception.GetType().Name
                    + "; message=" + exception.Message);
                return Empty();
            }
        }

        public static UnitAffinityPresentationCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("Affinity presentation JSON is empty.");

            AffinityPresentationDocument document;
            try
            {
                document = JsonUtility.FromJson<AffinityPresentationDocument>(json);
            }
            catch (Exception exception)
            {
                throw new FormatException("Affinity presentation JSON is malformed.", exception);
            }

            if (document == null)
                throw new FormatException("Affinity presentation document is missing.");
            if (!string.Equals(document.schemaVersion, SchemaVersion, StringComparison.Ordinal))
                throw new FormatException("Unsupported affinity presentation schema: " + document.schemaVersion);

            var regions = BuildDefinitions(document.regions, "region", allowBlankIcon: false);
            var occupations = BuildDefinitions(document.occupations, "occupation", allowBlankIcon: true);
            var unitDtos = document.units ?? Array.Empty<UnitAffinityPresentationDto>();
            var typeIds = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<UnitAffinityPresentation>(unitDtos.Length);

            foreach (var unit in unitDtos)
            {
                if (unit == null || string.IsNullOrWhiteSpace(unit.typeId))
                    throw new FormatException("Affinity unit TypeId is blank.");
                if (!typeIds.Add(unit.typeId))
                    throw new FormatException("Duplicate affinity unit TypeId: " + unit.typeId);
                AffinityDefinitionDto occupation = null;
                if (!string.IsNullOrWhiteSpace(unit.occupationId)
                    && !occupations.TryGetValue(unit.occupationId, out occupation))
                {
                    throw new FormatException(
                        "Unknown affinity occupation for TypeId " + unit.typeId + ": " + unit.occupationId);
                }

                AffinityDefinitionDto region = null;
                if (!string.IsNullOrWhiteSpace(unit.regionId)
                    && !regions.TryGetValue(unit.regionId, out region))
                {
                    throw new FormatException(
                        "Unknown affinity region for TypeId " + unit.typeId + ": " + unit.regionId);
                }
                if (region == null && occupation == null)
                {
                    throw new FormatException(
                        "Affinity unit has neither region nor occupation: " + unit.typeId);
                }

                entries.Add(new UnitAffinityPresentation(
                    unit.typeId,
                    region == null ? string.Empty : region.displayName,
                    region == null ? string.Empty : region.iconResourcePath,
                    occupation == null ? string.Empty : occupation.displayName,
                    occupation == null ? string.Empty : occupation.iconResourcePath));
            }

            return new UnitAffinityPresentationCatalog(entries);
        }

        private static Dictionary<string, AffinityDefinitionDto> BuildDefinitions(
            IEnumerable<AffinityDefinitionDto> source,
            string kind,
            bool allowBlankIcon)
        {
            var result = new Dictionary<string, AffinityDefinitionDto>(StringComparer.Ordinal);
            foreach (var definition in source ?? Array.Empty<AffinityDefinitionDto>())
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                    throw new FormatException("Affinity " + kind + " ID is blank.");
                if (string.IsNullOrWhiteSpace(definition.displayName))
                    throw new FormatException("Affinity " + kind + " name is blank: " + definition.id);
                if (!allowBlankIcon && string.IsNullOrWhiteSpace(definition.iconResourcePath))
                    throw new FormatException("Affinity " + kind + " icon is blank: " + definition.id);
                if (!result.TryAdd(definition.id, definition))
                    throw new FormatException("Duplicate affinity " + kind + " ID: " + definition.id);
            }

            return result;
        }

        private static UnitAffinityPresentationCatalog Empty() =>
            new UnitAffinityPresentationCatalog(Array.Empty<UnitAffinityPresentation>());

        [Serializable]
        private sealed class AffinityPresentationDocument
        {
            public string schemaVersion;
            public AffinityDefinitionDto[] regions;
            public AffinityDefinitionDto[] occupations;
            public UnitAffinityPresentationDto[] units;
        }

        [Serializable]
        private sealed class AffinityDefinitionDto
        {
            public string id;
            public string displayName;
            public string iconResourcePath;
        }

        [Serializable]
        private sealed class UnitAffinityPresentationDto
        {
            public string typeId;
            public string regionId;
            public string occupationId;
        }
    }
}
