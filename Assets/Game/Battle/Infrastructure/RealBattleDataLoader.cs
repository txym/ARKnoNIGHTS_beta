using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ArknoNights.Battle.Core;
using UnityEngine;

namespace ArknoNights.Battle.Infrastructure
{
    public sealed class UnitCatalogEntry
    {
        internal UnitCatalogEntry(UnitDefinition definition, int legacyUnitTypeId, string resourceKey, string displayNameZhHans, string skillDescriptionZhHans, string sourceFile, int deploymentCost, string portraitResourcePath, int rarity, int initialEliteLevel, int lifeDeduct, string prefabResourcePath, string skeletonDataResourcePath, int unitSkelType, string moveAnimation, string attackAnimation, string hitAnimation, string deathAnimation)
        {
            Definition = definition;
            LegacyUnitTypeId = legacyUnitTypeId;
            ResourceKey = resourceKey;
            DisplayNameZhHans = displayNameZhHans ?? string.Empty;
            SkillDescriptionZhHans = skillDescriptionZhHans ?? string.Empty;
            SourceFile = sourceFile;
            DeploymentCost = deploymentCost;
            PortraitResourcePath = portraitResourcePath;
            Rarity = rarity;
            InitialEliteLevel = initialEliteLevel;
            LifeDeduct = lifeDeduct;
            PrefabResourcePath = prefabResourcePath;
            SkeletonDataResourcePath = skeletonDataResourcePath;
            UnitSkelType = unitSkelType;
            MoveAnimation = moveAnimation;
            AttackAnimation = attackAnimation;
            HitAnimation = hitAnimation;
            DeathAnimation = deathAnimation;
        }

        public UnitDefinition Definition { get; }
        public int LegacyUnitTypeId { get; }
        /// <summary>Stable technical Resources/object lookup key; never a player-visible name.</summary>
        public string ResourceKey { get; }
        /// <summary>Optional Simplified Chinese player-visible name. Empty means not configured.</summary>
        public string DisplayNameZhHans { get; }
        /// <summary>Optional Simplified Chinese innate-skill description. Empty is valid.</summary>
        public string SkillDescriptionZhHans { get; }
        public string SourceFile { get; }
        public int DeploymentCost { get; }
        public string PortraitResourcePath { get; }
        public int Rarity { get; }
        public int InitialEliteLevel { get; }
        public int LifeDeduct { get; }
        public string PrefabResourcePath { get; }
        public string SkeletonDataResourcePath { get; }
        public int UnitSkelType { get; }
        public string MoveAnimation { get; }
        public string AttackAnimation { get; }
        public string HitAnimation { get; }
        public string DeathAnimation { get; }
    }

    public sealed class UnitCatalog
    {
        private readonly Dictionary<string, UnitCatalogEntry> entriesByTypeId;

        internal UnitCatalog(string schemaVersion, string catalogId, IEnumerable<UnitCatalogEntry> entries)
        {
            SchemaVersion = schemaVersion;
            CatalogId = catalogId;
            Entries = new ReadOnlyCollection<UnitCatalogEntry>((entries ?? Enumerable.Empty<UnitCatalogEntry>()).OrderBy(entry => entry.Definition.TypeId, StringComparer.Ordinal).ToArray());
            entriesByTypeId = Entries.ToDictionary(entry => entry.Definition.TypeId, StringComparer.Ordinal);
            CanonicalSummary = string.Join("|", Entries.Select(entry => entry.Definition.TypeId + "," + entry.LegacyUnitTypeId + "," + entry.SkeletonDataResourcePath + "," + entry.Definition.AttackAnimationDurationTicks));
        }

        public string SchemaVersion { get; }
        public string CatalogId { get; }
        public IReadOnlyList<UnitCatalogEntry> Entries { get; }
        public string CanonicalSummary { get; }
        public bool TryGet(string typeId, out UnitCatalogEntry entry) => entriesByTypeId.TryGetValue(typeId ?? string.Empty, out entry);
    }

    public sealed class UnitCatalogLoadResult
    {
        internal UnitCatalogLoadResult(UnitCatalog catalog, IReadOnlyList<ValidationError> errors) { Catalog = catalog; Errors = errors; }
        public bool Success => Catalog != null && Errors.Count == 0;
        public UnitCatalog Catalog { get; }
        public IReadOnlyList<ValidationError> Errors { get; }
    }

    public sealed class LocalBattleLoadResult
    {
        internal LocalBattleLoadResult(BattleInput input, UnitCatalog catalog, IReadOnlyList<ValidationError> errors) { Input = input; Catalog = catalog; Errors = errors; }
        public bool Success => Input != null && Catalog != null && Errors.Count == 0;
        public BattleInput Input { get; }
        public UnitCatalog Catalog { get; }
        public IReadOnlyList<ValidationError> Errors { get; }
    }

    /// <summary>Player-safe loader for generated unit-catalog-v1 resources.</summary>
    public static class UnitCatalogLoader
    {
        public const string SchemaVersion = "unit-catalog-v1";

        public static UnitCatalogLoadResult LoadFromResources(string resourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            return asset == null
                ? Failure("catalog.resource.missing", "catalogResource=" + resourcePath)
                : LoadFromJson(asset.text);
        }

        public static UnitCatalogLoadResult LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Failure("catalog.json.empty", "Unit catalog JSON is empty.");
            UnitCatalogDto dto;
            try { dto = JsonUtility.FromJson<UnitCatalogDto>(json); }
            catch (Exception exception) { return Failure("catalog.json.invalid", exception.Message); }
            if (dto == null) return Failure("catalog.json.invalid", "Unit catalog JSON could not be parsed.");

            var errors = new List<ValidationError>();
            if (!string.Equals(dto.schemaVersion, SchemaVersion, StringComparison.Ordinal)) errors.Add(Error("catalog.schema.unsupported", dto.schemaVersion, dto.catalogId, null, null));
            if (string.IsNullOrWhiteSpace(dto.catalogId)) errors.Add(Error("catalog.id.invalid", dto.schemaVersion, dto.catalogId, null, null));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<UnitCatalogEntry>();
            foreach (var item in dto.units ?? Array.Empty<UnitCatalogEntryDto>())
            {
                var entry = ConvertEntry(item, dto.schemaVersion, dto.catalogId, errors);
                if (entry == null) continue;
                if (!seen.Add(entry.Definition.TypeId)) errors.Add(Error("catalog.typeId.duplicate", dto.schemaVersion, dto.catalogId, null, entry.Definition.TypeId));
                else entries.Add(entry);
            }

            if (entries.Count == 0) errors.Add(Error("catalog.units.empty", dto.schemaVersion, dto.catalogId, null, null));
            var readOnlyErrors = new ReadOnlyCollection<ValidationError>(errors);
            return errors.Count == 0
                ? new UnitCatalogLoadResult(new UnitCatalog(dto.schemaVersion, dto.catalogId, entries), readOnlyErrors)
                : new UnitCatalogLoadResult(null, readOnlyErrors);
        }

        private static UnitCatalogEntry ConvertEntry(UnitCatalogEntryDto dto, string schemaVersion, string catalogId, ICollection<ValidationError> errors)
        {
            if (dto == null)
            {
                errors.Add(Error("catalog.unit.missing", schemaVersion, catalogId, null, null));
                return null;
            }

            var valid = true;
            if (string.IsNullOrWhiteSpace(dto.typeId) || !dto.typeId.All(char.IsDigit)) { errors.Add(Error("catalog.typeId.invalid", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            if (!int.TryParse(dto.typeId, NumberStyles.None, CultureInfo.InvariantCulture, out var numericTypeId) || numericTypeId != dto.legacyUnitTypeId) { errors.Add(Error("catalog.legacyId.invalid", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            if (dto.maxHitPoints <= 0 || dto.attack < 0 || dto.defense < 0 || dto.magicResistance < 0 || dto.magicResistance > 100 || dto.moveSpeedCentimetresPerSecond <= 0 || dto.attackIntervalTicks <= 0 || dto.attackAnimationDurationTicks <= 0 || dto.blockCapacity <= 0 || dto.tauntLevel < 0) { errors.Add(Error("catalog.values.invalid", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            var damageType = default(DamageType);
            var attackMethod = default(AttackMethod);
            var enumsValid = TryParseEnum(dto.damageType, out damageType) & TryParseEnum(dto.attackMethod, out attackMethod);
            if (!enumsValid) { errors.Add(Error("catalog.enum.invalid", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            if (dto.isSyntheticFixtureData) { errors.Add(Error("catalog.synthetic.notAllowed", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            if (dto.deploymentCost < 0 || dto.rarity < 1 || dto.rarity > 6 || dto.initialEliteLevel < 0 || dto.initialEliteLevel > 3 || dto.lifeDeduct < 0)
            {
                errors.Add(Error("catalog.ui.values.invalid", schemaVersion, catalogId, null, dto.typeId));
                valid = false;
            }
            if (string.IsNullOrWhiteSpace(dto.portraitResourcePath))
            {
                errors.Add(Error("catalog.portrait.resource.missing", schemaVersion, catalogId, null, dto.typeId));
                valid = false;
            }
            else if (Resources.Load<Sprite>(dto.portraitResourcePath) == null)
            {
                errors.Add(Error("catalog.portrait.resource.missing", schemaVersion, catalogId, null, dto.typeId));
                valid = false;
            }
            if (string.IsNullOrWhiteSpace(dto.resourceKey) || string.IsNullOrWhiteSpace(dto.sourceFile) || string.IsNullOrWhiteSpace(dto.prefabResourcePath) || string.IsNullOrWhiteSpace(dto.skeletonDataResourcePath)) { errors.Add(Error("catalog.presentation.missing", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            else
            {
                if (Resources.Load<GameObject>(dto.prefabResourcePath) == null) { errors.Add(Error("catalog.prefab.resource.missing", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
                if (Resources.Load<UnityEngine.Object>(dto.skeletonDataResourcePath) == null) { errors.Add(Error("catalog.skeleton.resource.missing", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            }
            if (dto.unitSkelType != 1 && dto.unitSkelType != 2) { errors.Add(Error("catalog.skeletonType.invalid", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            if (string.IsNullOrWhiteSpace(dto.moveAnimation) || string.IsNullOrWhiteSpace(dto.attackAnimation) || string.IsNullOrWhiteSpace(dto.deathAnimation)) { errors.Add(Error("catalog.animation.required.missing", schemaVersion, catalogId, null, dto.typeId)); valid = false; }
            if (!valid) return null;

            return new UnitCatalogEntry(
                new UnitDefinition(dto.typeId, dto.maxHitPoints, dto.attack, dto.defense, dto.magicResistance, dto.moveSpeedCentimetresPerSecond, dto.attackIntervalTicks, dto.attackAnimationDurationTicks, damageType, attackMethod, dto.blockCapacity, dto.tauntLevel, false, dto.innateAbilityIds ?? Array.Empty<string>()),
                dto.legacyUnitTypeId, dto.resourceKey, dto.displayNameZhHans, dto.skillDescriptionZhHans, dto.sourceFile, dto.deploymentCost, dto.portraitResourcePath, dto.rarity, dto.initialEliteLevel, dto.lifeDeduct, dto.prefabResourcePath, dto.skeletonDataResourcePath, dto.unitSkelType, dto.moveAnimation, dto.attackAnimation, dto.hitAnimation ?? string.Empty, dto.deathAnimation);
        }

        private static bool TryParseEnum<T>(string value, out T parsed) where T : struct => Enum.TryParse(value, true, out parsed) && Enum.IsDefined(typeof(T), parsed);
        private static UnitCatalogLoadResult Failure(string code, string message) => new UnitCatalogLoadResult(null, new[] { new ValidationError(code, message) });
        internal static ValidationError Error(string code, string schema, string battleOrCatalogId, string playerId, string typeId) => new ValidationError(code, "schema=" + (schema ?? "<missing>") + "; battleId=" + (battleOrCatalogId ?? "<missing>") + "; playerId=" + (playerId ?? "<none>") + "; typeId=" + (typeId ?? "<none>"));

        [Serializable] private sealed class UnitCatalogDto { public string schemaVersion; public string catalogId; public UnitCatalogEntryDto[] units; }
        [Serializable] private sealed class UnitCatalogEntryDto { public string typeId; public int legacyUnitTypeId; public string resourceKey; public string displayNameZhHans; public string skillDescriptionZhHans; public string sourceFile; public int deploymentCost; public string portraitResourcePath; public int rarity; public int initialEliteLevel; public int maxHitPoints; public int attack; public int defense; public int magicResistance; public int moveSpeedCentimetresPerSecond; public int attackIntervalTicks; public int attackAnimationDurationTicks; public string damageType; public string attackMethod; public int blockCapacity; public int tauntLevel; public int lifeDeduct; public bool isSyntheticFixtureData; public string[] innateAbilityIds; public string prefabResourcePath; public string skeletonDataResourcePath; public int unitSkelType; public string moveAnimation; public string attackAnimation; public string hitAnimation; public string deathAnimation; }
    }

    /// <summary>Joins a local-battle-v1 player snapshot to a Player-safe unit catalog without exposing presentation data to Core.</summary>
    public static class LocalBattleLoader
    {
        private const string DefaultAbilityCatalogResourcePath = "BattleData/ability-catalog-v1";

        public static LocalBattleLoadResult LoadFromResources(string catalogResourcePath, string battleResourcePath)
        {
            var catalogResult = UnitCatalogLoader.LoadFromResources(catalogResourcePath);
            if (!catalogResult.Success) return new LocalBattleLoadResult(null, null, catalogResult.Errors);
            var abilityCatalogResult = AbilityCatalogLoader.LoadFromResources(DefaultAbilityCatalogResourcePath, catalogResult.Catalog);
            if (!abilityCatalogResult.Success) return new LocalBattleLoadResult(null, catalogResult.Catalog, abilityCatalogResult.Errors);
            var asset = Resources.Load<TextAsset>(battleResourcePath);
            return asset == null
                ? Failure("localBattle.resource.missing", "schema=local-battle-v1; battleResource=" + battleResourcePath)
                : LoadFromJson(catalogResult.Catalog, abilityCatalogResult.Catalog, asset.text);
        }

        public static LocalBattleLoadResult LoadFromJson(UnitCatalog catalog, string json)
        {
            var abilityCatalogResult = AbilityCatalogLoader.LoadFromResources(DefaultAbilityCatalogResourcePath, catalog);
            return abilityCatalogResult.Success
                ? LoadFromJson(catalog, abilityCatalogResult.Catalog, json)
                : new LocalBattleLoadResult(null, catalog, abilityCatalogResult.Errors);
        }

        public static LocalBattleLoadResult LoadFromJson(UnitCatalog catalog, AbilityCatalog abilityCatalog, string json)
        {
            if (catalog == null || abilityCatalog == null) return Failure("localBattle.catalog.missing", "schema=local-battle-v1");
            if (string.IsNullOrWhiteSpace(json)) return Failure("localBattle.json.empty", "schema=local-battle-v1");
            LocalBattleDto dto;
            try { dto = JsonUtility.FromJson<LocalBattleDto>(json); }
            catch (Exception exception) { return Failure("localBattle.json.invalid", exception.Message); }
            if (dto == null) return Failure("localBattle.json.invalid", "schema=local-battle-v1");

            var errors = new List<ValidationError>();
            if (!string.Equals(dto.schemaVersion, BattleInput.LocalBattleSchemaVersion, StringComparison.Ordinal)) errors.Add(UnitCatalogLoader.Error("localBattle.schema.unsupported", dto.schemaVersion, dto.battleId, null, null));
            var players = (dto.players ?? Array.Empty<PlayerDto>()).Select(player => ConvertPlayer(player, dto, catalog, errors)).ToArray();
            if (errors.Count > 0) return new LocalBattleLoadResult(null, catalog, new ReadOnlyCollection<ValidationError>(errors));

            var specification = new BattleInputSpecification(BattleInput.LocalBattleSchemaVersion, dto.battleId, dto.maxTicks, catalog.Entries.Select(entry => entry.Definition), abilityCatalog.Abilities, players);
            if (!BattleInputFactory.TryCreate(specification, out var input, out var inputErrors)) return new LocalBattleLoadResult(null, catalog, inputErrors);
            return new LocalBattleLoadResult(input, catalog, Array.Empty<ValidationError>());
        }

        private static PlayerSnapshot ConvertPlayer(PlayerDto dto, LocalBattleDto document, UnitCatalog catalog, ICollection<ValidationError> errors)
        {
            if (dto == null) { errors.Add(UnitCatalogLoader.Error("localBattle.player.missing", document.schemaVersion, document.battleId, null, null)); return null; }
            var units = (dto.units ?? Array.Empty<UnitDto>()).Select(unit => ConvertUnit(unit, dto, document, catalog, errors)).ToArray();
            return new PlayerSnapshot(dto.playerId, ParseEnum<BattleSide>(dto.side), units);
        }

        private static UnitSnapshot ConvertUnit(UnitDto dto, PlayerDto player, LocalBattleDto document, UnitCatalog catalog, ICollection<ValidationError> errors)
        {
            if (dto == null) { errors.Add(UnitCatalogLoader.Error("localBattle.unit.missing", document.schemaVersion, document.battleId, player.playerId, null)); return null; }
            if (!catalog.TryGet(dto.typeId, out _)) errors.Add(UnitCatalogLoader.Error("localBattle.unit.type.unknown", document.schemaVersion, document.battleId, player.playerId, dto.typeId));
            FormationCoordinate? formation = null;
            if (dto.formationX != 0 || dto.formationY != 0)
            {
                if (FormationCoordinate.TryCreate(dto.formationX, dto.formationY, out var parsed)) formation = parsed;
                else formation = default(FormationCoordinate);
            }
            return new UnitSnapshot(dto.unitId, dto.typeId, ParseEnum<UnitZone>(dto.zone), formation, (dto.buffs ?? Array.Empty<BuffDto>()).Where(buff => buff != null).Select(buff => new BuffPlaceholder(buff.id, buff.rawPayload)), dto.eliteLevel);
        }

        private static T ParseEnum<T>(string value) where T : struct => Enum.TryParse(value, true, out T parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed : (T)Enum.ToObject(typeof(T), -1);
        private static LocalBattleLoadResult Failure(string code, string message) => new LocalBattleLoadResult(null, null, new[] { new ValidationError(code, message) });

        [Serializable] private sealed class LocalBattleDto { public string schemaVersion; public string battleId; public int maxTicks; public PlayerDto[] players; }
        [Serializable] private sealed class PlayerDto { public string playerId; public string side; public UnitDto[] units; }
        [Serializable] private sealed class UnitDto { public string unitId; public string typeId; public string zone; public int formationX; public int formationY; public int eliteLevel; public BuffDto[] buffs; }
        [Serializable] private sealed class BuffDto { public string id; public string rawPayload; }
    }
}
