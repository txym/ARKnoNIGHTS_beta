using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using ArknoNights.Player;

namespace ArknoNights.Details
{
    public sealed class UnitDetailSnapshot
    {
        internal UnitDetailSnapshot(string unitId, string typeId, BattleSide side, UnitZone zone, UnitCatalogEntry entry, int eliteLevel, int maxHitPoints, int currentHitPoints, bool dynamicAttributesAvailable, BattleUnitAttributesSnapshot attributes, IEnumerable<string> diagnostics)
        {
            UnitId = unitId;
            TypeId = typeId;
            Side = side;
            Zone = zone;
            DisplayNameZhHans = entry.DisplayNameZhHans;
            AbilityDescriptionZhHans =
                entry.SkillDescriptionZhHans;
            PortraitResourcePath = entry.PortraitResourcePath;
            Rarity = entry.Rarity;
            EliteLevel = eliteLevel;
            AttackMethod = entry.Definition.AttackMethod;
            DamageType = entry.Definition.DamageType;
            CurrentHitPoints = currentHitPoints;
            MaxHitPoints = maxHitPoints;
            DynamicAttributesAvailable = dynamicAttributesAvailable;
            MoveSpeedCentimetresPerSecond = dynamicAttributesAvailable ? attributes.MoveSpeedCentimetresPerSecond : (int?)null;
            Attack = dynamicAttributesAvailable ? attributes.Attack : (int?)null;
            AttackIntervalTicks = dynamicAttributesAvailable ? attributes.AttackIntervalTicks : (int?)null;
            Defense = dynamicAttributesAvailable ? attributes.Defense : (int?)null;
            MagicResistance = dynamicAttributesAvailable ? attributes.MagicResistance : (int?)null;
            BlockCapacity = dynamicAttributesAvailable
                ? attributes.BlockCapacity
                : entry.Definition.BlockCapacity;
            DeploymentCost = entry.DeploymentCost;
            LifeDeduct = entry.LifeDeduct;
            Diagnostics = new ReadOnlyCollection<string>((diagnostics ?? Enumerable.Empty<string>()).ToArray());
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public BattleSide Side { get; }
        public UnitZone Zone { get; }
        public string DisplayNameZhHans { get; }
        public string AbilityDescriptionZhHans { get; }
        public string PortraitResourcePath { get; }
        public int Rarity { get; }
        public int EliteLevel { get; }
        public AttackMethod AttackMethod { get; }
        public DamageType DamageType { get; }
        public int CurrentHitPoints { get; }
        public int MaxHitPoints { get; }
        public bool DynamicAttributesAvailable { get; }
        public int? MoveSpeedCentimetresPerSecond { get; }
        public int? Attack { get; }
        public int? AttackIntervalTicks { get; }
        public int? Defense { get; }
        public int? MagicResistance { get; }
        public int BlockCapacity { get; }
        public int DeploymentCost { get; }
        public int LifeDeduct { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }

    public static class UnitDetailResolver
    {
        public static bool TryResolvePreparation(PlayerStateSnapshot player, UnitCatalog catalog, string unitId, out UnitDetailSnapshot detail)
        {
            var abilityResult = catalog == null
                ? null
                : AbilityCatalogLoader.LoadFromResources(
                    "BattleData/ability-catalog-v1",
                    catalog);
            return TryResolvePreparation(
                player,
                catalog,
                abilityResult != null && abilityResult.Success
                    ? abilityResult.Catalog
                    : null,
                unitId,
                out detail);
        }

        public static bool TryResolvePreparation(
            PlayerStateSnapshot player,
            UnitCatalog catalog,
            AbilityCatalog abilityCatalog,
            string unitId,
            out UnitDetailSnapshot detail)
        {
            detail = null;
            if (player == null || catalog == null || string.IsNullOrWhiteSpace(unitId)) return false;
            var unit = player.Units.SingleOrDefault(candidate => string.Equals(candidate.UnitId, unitId, StringComparison.Ordinal));
            if (unit == null
                || !catalog.TryGet(
                    unit.TypeId,
                    unit.EliteLevel,
                    out var entry))
                return false;
            var attributes = BuildPreparationAttributes(
                entry.Definition,
                abilityCatalog);
            return TryCreate(unit.UnitId, unit.TypeId, BattleSide.Home, ToBattleZone(unit.Zone), entry, unit.EliteLevel, entry.Definition.MaxHitPoints, entry.Definition.MaxHitPoints, unit.Buffs.Count == 0 && abilityCatalog != null, attributes, out detail);
        }

        public static bool TryResolveBattle(BattleInput input, IReadOnlyList<BattlePresentationViewState> presentationStates, UnitCatalog catalog, string unitId, out UnitDetailSnapshot detail)
        {
            detail = null;
            if (input == null || presentationStates == null || catalog == null || string.IsNullOrWhiteSpace(unitId)) return false;
            var player = input.Players.SingleOrDefault(candidate => candidate.Units.Any(unit => string.Equals(unit.UnitId, unitId, StringComparison.Ordinal)));
            var unit = player == null ? null : player.Units.SingleOrDefault(candidate => string.Equals(candidate.UnitId, unitId, StringComparison.Ordinal));
            var state = presentationStates.SingleOrDefault(candidate => string.Equals(candidate.UnitId, unitId, StringComparison.Ordinal));
            if (unit == null
                || state == null
                || !catalog.TryGet(
                    unit.TypeId,
                    state.EliteLevel,
                    out var entry))
                return false;
            var attributes = new BattleUnitAttributesSnapshot(
                state.Attack,
                state.Defense,
                state.MagicResistance,
                state.MoveSpeedCentimetresPerSecond,
                state.AttackIntervalTicks,
                state.BlockCapacity);
            return TryCreate(unit.UnitId, unit.TypeId, player.Side, unit.Zone, entry, state.EliteLevel, state.MaxHitPoints, state.HitPoints, state.AttributesAvailable, attributes, out detail);
        }

        private static bool TryCreate(string unitId, string typeId, BattleSide side, UnitZone zone, UnitCatalogEntry entry, int eliteLevel, int maxHitPoints, int currentHitPoints, bool dynamicAttributesAvailable, BattleUnitAttributesSnapshot attributes, out UnitDetailSnapshot detail)
        {
            var diagnostics = new List<string>();
            if (string.IsNullOrWhiteSpace(entry.DisplayNameZhHans)) diagnostics.Add("detail.displayName.unconfigured; typeId=" + typeId);
            if (!dynamicAttributesAvailable) diagnostics.Add("detail.dynamicAttributes.unavailable.unresolvedBuff; unitId=" + unitId);
            detail = new UnitDetailSnapshot(unitId, typeId, side, zone, entry, eliteLevel, maxHitPoints, Math.Max(0, currentHitPoints), dynamicAttributesAvailable, attributes, diagnostics);
            return true;
        }

        private static BattleUnitAttributesSnapshot
            BuildPreparationAttributes(
                UnitDefinition definition,
                AbilityCatalog abilityCatalog)
        {
            var blockCapacityAdditive = 0L;
            var magicResistanceAdditive = 0L;
            var attackSpeedAdditive = 0L;
            if (abilityCatalog != null)
            {
                foreach (var abilityId in
                         definition.InnateAbilityIds)
                {
                    if (!abilityCatalog.TryGet(
                            abilityId,
                            out var ability)
                        || ability.PassiveCombatModifier == null)
                        continue;
                    blockCapacityAdditive +=
                        ability.PassiveCombatModifier
                            .BlockCapacityAdditive;
                    magicResistanceAdditive +=
                        ability.PassiveCombatModifier
                            .MagicResistanceAdditive;
                    attackSpeedAdditive +=
                        ability.PassiveCombatModifier
                            .AttackSpeedAdditive;
                }
            }

            var finalAttackSpeed = 100L
                + attackSpeedAdditive;
            var attackIntervalTicks =
                definition.AttackIntervalTicks == 0
                || finalAttackSpeed <= 0
                    ? 0
                    : (int)Math.Max(
                        1L,
                        Math.Min(
                            int.MaxValue,
                            ((long)definition
                                .AttackIntervalTicks
                             * 100L
                             + finalAttackSpeed - 1L)
                            / finalAttackSpeed));
            return new BattleUnitAttributesSnapshot(
                definition.Attack,
                definition.Defense,
                (int)Math.Max(
                    0L,
                    Math.Min(
                        100L,
                        definition.MagicResistance
                        + magicResistanceAdditive)),
                definition.MoveSpeedCentimetresPerSecond,
                attackIntervalTicks,
                (int)Math.Max(
                    0L,
                    Math.Min(
                        int.MaxValue,
                        definition.BlockCapacity
                        + blockCapacityAdditive)));
        }

        private static UnitZone ToBattleZone(PlayerUnitZone zone) => zone == PlayerUnitZone.Deployed ? UnitZone.Deployed : zone == PlayerUnitZone.Shop ? UnitZone.Shop : UnitZone.Staging;
    }

    public static class UnitDetailNumberFormatter
    {
        public static string Value(int value) => value.ToString(CultureInfo.InvariantCulture);
        public static string MoveSpeed(int centimetresPerSecond) => (centimetresPerSecond / 100f).ToString("0.#", CultureInfo.InvariantCulture);
        public static string AttackInterval(int ticks) => (ticks / (float)BattleInput.TicksPerSecond).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
