using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using UnityEngine;

namespace ArknoNights.Battle.Infrastructure
{
    public sealed class BattleFixtureLoadResult
    {
        internal BattleFixtureLoadResult(BattleInput input, IReadOnlyList<ValidationError> errors) { Input = input; Errors = errors; }
        public bool Success => Input != null && Errors.Count == 0;
        public BattleInput Input { get; }
        public IReadOnlyList<ValidationError> Errors { get; }
    }

    /// <summary>Unity-only fixture adapter. Core receives only immutable Core values.</summary>
    public static class BattleFixtureLoader
    {
        public static BattleFixtureLoadResult LoadFromResources(string resourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null) return Failure("fixture.resource.missing", "Fixture resource was not found: " + resourcePath);
            return LoadFromJson(asset.text);
        }

        public static BattleFixtureLoadResult LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Failure("fixture.json.empty", "Fixture JSON is empty.");
            BattleFixtureDto dto;
            try { dto = JsonUtility.FromJson<BattleFixtureDto>(json); }
            catch (Exception exception) { return Failure("fixture.json.invalid", exception.Message); }
            if (dto == null) return Failure("fixture.json.invalid", "Fixture JSON could not be parsed.");

            var definitions = (dto.unitTypes ?? Array.Empty<UnitDefinitionDto>()).Select(ConvertDefinition).ToArray();
            var players = (dto.players ?? Array.Empty<PlayerDto>()).Select(ConvertPlayer).ToArray();
            var specification = new BattleInputSpecification(dto.schemaVersion, dto.battleId, dto.maxTicks, definitions, players);
            return BattleInputFactory.TryCreate(specification, out var input, out var errors) ? new BattleFixtureLoadResult(input, errors) : new BattleFixtureLoadResult(null, errors);
        }

        private static UnitDefinition ConvertDefinition(UnitDefinitionDto dto)
        {
            if (dto == null) return null;
            return new UnitDefinition(dto.typeId, dto.maxHitPoints, dto.attack, dto.defense, dto.magicResistance, dto.moveSpeedCentimetresPerSecond, dto.attackIntervalTicks, dto.attackAnimationDurationTicks, ParseEnum<DamageType>(dto.damageType), ParseEnum<AttackMethod>(dto.attackMethod), dto.blockCapacity, dto.tauntLevel, dto.isSyntheticFixtureData);
        }

        private static PlayerSnapshot ConvertPlayer(PlayerDto dto)
        {
            if (dto == null) return null;
            var units = (dto.units ?? Array.Empty<UnitDto>()).Select(ConvertUnit).ToArray();
            return new PlayerSnapshot(dto.playerId, ParseEnum<BattleSide>(dto.side), units);
        }

        private static UnitSnapshot ConvertUnit(UnitDto dto)
        {
            if (dto == null) return null;
            FormationCoordinate? formation = null;
            if (dto.formationX != 0 || dto.formationY != 0)
            {
                if (FormationCoordinate.TryCreate(dto.formationX, dto.formationY, out var parsed)) formation = parsed;
                else formation = default(FormationCoordinate);
            }
            return new UnitSnapshot(dto.unitId, dto.typeId, ParseEnum<UnitZone>(dto.zone), formation, ConvertBuffs(dto.buffs));
        }

        private static IEnumerable<BuffPlaceholder> ConvertBuffs(BuffDto[] buffs) => (buffs ?? Array.Empty<BuffDto>()).Where(item => item != null).Select(item => new BuffPlaceholder(item.id, item.rawPayload));
        private static T ParseEnum<T>(string value) where T : struct => Enum.TryParse(value, true, out T parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed : (T)Enum.ToObject(typeof(T), -1);
        private static BattleFixtureLoadResult Failure(string code, string message) => new BattleFixtureLoadResult(null, new[] { new ValidationError(code, message) });

        [Serializable] private sealed class BattleFixtureDto { public string schemaVersion; public string battleId; public int maxTicks; public UnitDefinitionDto[] unitTypes; public PlayerDto[] players; }
        [Serializable] private sealed class UnitDefinitionDto { public string typeId; public int maxHitPoints; public int attack; public int defense; public int magicResistance; public int moveSpeedCentimetresPerSecond; public int attackIntervalTicks; public int attackAnimationDurationTicks; public string damageType; public string attackMethod; public int blockCapacity; public int tauntLevel; public bool isSyntheticFixtureData; }
        [Serializable] private sealed class PlayerDto { public string playerId; public string side; public UnitDto[] units; }
        [Serializable] private sealed class UnitDto { public string unitId; public string typeId; public string zone; public int formationX; public int formationY; public BuffDto[] buffs; }
        [Serializable] private sealed class BuffDto { public string id; public string rawPayload; }
    }
}
