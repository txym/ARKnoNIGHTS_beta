using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
using UnityEngine;

namespace ArknoNights.Battle.Infrastructure
{
    public sealed class AbilityCatalog
    {
        private readonly Dictionary<string, AbilityDefinition> abilitiesById;

        internal AbilityCatalog(string schemaVersion, string catalogId, IEnumerable<AbilityDefinition> abilities)
        {
            SchemaVersion = schemaVersion;
            CatalogId = catalogId;
            Abilities = new ReadOnlyCollection<AbilityDefinition>((abilities ?? Enumerable.Empty<AbilityDefinition>()).OrderBy(ability => ability.AbilityId, StringComparer.Ordinal).ToArray());
            abilitiesById = Abilities.ToDictionary(ability => ability.AbilityId, StringComparer.Ordinal);
        }

        public string SchemaVersion { get; }
        public string CatalogId { get; }
        public IReadOnlyList<AbilityDefinition> Abilities { get; }
        public bool TryGet(string abilityId, out AbilityDefinition ability) => abilitiesById.TryGetValue(abilityId ?? string.Empty, out ability);
    }

    public sealed class AbilityCatalogLoadResult
    {
        internal AbilityCatalogLoadResult(AbilityCatalog catalog, IReadOnlyList<ValidationError> errors) { Catalog = catalog; Errors = errors; }
        public bool Success => Catalog != null && Errors.Count == 0;
        public AbilityCatalog Catalog { get; }
        public IReadOnlyList<ValidationError> Errors { get; }
    }

    public static class AbilityCatalogLoader
    {
        public const string SchemaVersion = "ability-catalog-v1";

        public static AbilityCatalogLoadResult LoadFromResources(string resourcePath, UnitCatalog unitCatalog)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            return asset == null
                ? Failure("ability.resource.missing", "abilityCatalogResource=" + resourcePath)
                : LoadFromJson(asset.text, unitCatalog);
        }

        public static AbilityCatalogLoadResult LoadFromJson(string json, UnitCatalog unitCatalog)
        {
            if (string.IsNullOrWhiteSpace(json)) return Failure("ability.json.empty", "Ability catalog JSON is empty.");
            AbilityCatalogDto dto;
            try { dto = JsonUtility.FromJson<AbilityCatalogDto>(json); }
            catch (Exception exception) { return Failure("ability.json.invalid", exception.Message); }
            if (dto == null) return Failure("ability.json.invalid", "Ability catalog JSON could not be parsed.");

            var errors = new List<ValidationError>();
            if (!string.Equals(dto.schemaVersion, SchemaVersion, StringComparison.Ordinal)) errors.Add(new ValidationError("ability.schema.unsupported", "Unsupported ability schema: " + dto.schemaVersion));
            if (string.IsNullOrWhiteSpace(dto.catalogId)) errors.Add(new ValidationError("ability.catalogId.invalid", "Ability catalog ID is required."));
            var abilities = new List<AbilityDefinition>();
            foreach (var source in dto.abilities ?? Array.Empty<AbilityDto>()) abilities.Add(Convert(source));
            if (abilities.Count == 0) errors.Add(new ValidationError("ability.catalog.empty", "Ability catalog must contain at least one ability."));

            var definitions = unitCatalog == null ? Enumerable.Empty<UnitDefinition>() : unitCatalog.Entries.Select(entry => entry.Definition);
            var specification = new BattleInputSpecification(BattleInput.LocalBattleSchemaVersion, "ability-catalog-validation", 1, definitions, abilities, new[]
            {
                new PlayerSnapshot("home", BattleSide.Home, Array.Empty<UnitSnapshot>()),
                new PlayerSnapshot("away", BattleSide.Away, Array.Empty<UnitSnapshot>())
            });
            BattleInputFactory.TryCreate(specification, out _, out var validationErrors);
            errors.AddRange(validationErrors.Where(error => error.Code.StartsWith("ability.", StringComparison.Ordinal)));
            if (abilities.Any(ability => ability != null && ability.AbilityId == "SUMMON_JELLY_MINIONS" && ability.SummonEffect != null && ability.SummonEffect.InheritPathFromCaster))
                errors.Add(new ValidationError("ability.summon.inheritPath.invalid", "SUMMON_JELLY_MINIONS cannot inherit the caster path."));
            return errors.Count == 0
                ? new AbilityCatalogLoadResult(new AbilityCatalog(dto.schemaVersion, dto.catalogId, abilities), new ReadOnlyCollection<ValidationError>(errors))
                : new AbilityCatalogLoadResult(null, new ReadOnlyCollection<ValidationError>(errors));
        }

        private static AbilityDefinition Convert(AbilityDto source)
        {
            if (source == null) return null;
            return new AbilityDefinition(
                source.abilityId,
                source.displayNameZhHans,
                source.descriptionZhHans,
                ParseEnum<AbilityActivationKind>(source.activationKind),
                ParseEnum<SilencePolicy>(source.silencePolicy),
                source.initialSkillPoints,
                source.requiredSkillPoints,
                ParseEnum<SkillPointGeneration>(source.skillPointGeneration),
                string.IsNullOrWhiteSpace(source.summonTypeId)
                    ? null
                    : new SummonEffectDefinition(source.summonTypeId, source.count, source.sideLengthCentimetres, source.inheritPathFromCaster),
                string.IsNullOrWhiteSpace(source.unitTrait)
                    ? null
                    : new UnitTraitEffectDefinition(ParseEnum<UnitTraitEffectKind>(source.unitTrait)));
        }

        private static T ParseEnum<T>(string value) where T : struct => Enum.TryParse(value, true, out T parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed : (T)Enum.ToObject(typeof(T), -1);
        private static AbilityCatalogLoadResult Failure(string code, string message) => new AbilityCatalogLoadResult(null, new[] { new ValidationError(code, message) });

        [Serializable] private sealed class AbilityCatalogDto { public string schemaVersion; public string catalogId; public AbilityDto[] abilities; }
        [Serializable] private sealed class AbilityDto { public string abilityId; public string displayNameZhHans; public string descriptionZhHans; public string activationKind; public string silencePolicy; public int initialSkillPoints; public int requiredSkillPoints; public string skillPointGeneration; public string summonTypeId; public int count; public int sideLengthCentimetres; public bool inheritPathFromCaster; public string unitTrait; }
    }
}
