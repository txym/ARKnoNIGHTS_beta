using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ArknoNights.Battle.Core
{
    public enum BattleSide { Home, Away }
    public enum UnitZone { Deployed, Staging, Shop }
    public enum DamageType { Physical = 0, Magic = 1, True = 2, None = 3 }
    public enum AttackMethod { Melee = 0, Ranged = 1, None = 2 }

    public sealed class UnitSkillAnimationDefinition
    {
        public UnitSkillAnimationDefinition(string key, int originalDurationTicks)
        {
            Key = key;
            OriginalDurationTicks = originalDurationTicks;
        }

        public string Key { get; }
        public int OriginalDurationTicks { get; }
        // Skill clips always run at 2x. Rounding upward guarantees that the
        // final authored frame is still reached on the authoritative 20 TPS grid.
        public int EffectiveDurationTicks => (OriginalDurationTicks + 1) / 2;
    }

    public sealed class ValidationError
    {
        public ValidationError(string code, string message) { Code = code; Message = message; }
        public string Code { get; }
        public string Message { get; }
        public override string ToString() => Code + ": " + Message;
    }

    public sealed class BattleInputSpecification
    {
        public BattleInputSpecification(string schemaVersion, string battleId, int maxTicks, IEnumerable<UnitDefinition> unitDefinitions, IEnumerable<PlayerSnapshot> players)
            : this(schemaVersion, battleId, maxTicks, unitDefinitions, Enumerable.Empty<AbilityDefinition>(), players)
        {
        }

        public BattleInputSpecification(string schemaVersion, string battleId, int maxTicks, IEnumerable<UnitDefinition> unitDefinitions, IEnumerable<AbilityDefinition> abilityDefinitions, IEnumerable<PlayerSnapshot> players)
        {
            SchemaVersion = schemaVersion;
            BattleId = battleId;
            MaxTicks = maxTicks;
            UnitDefinitions = (unitDefinitions ?? Enumerable.Empty<UnitDefinition>()).ToArray();
            AbilityDefinitions = (abilityDefinitions ?? Enumerable.Empty<AbilityDefinition>()).ToArray();
            Players = (players ?? Enumerable.Empty<PlayerSnapshot>()).ToArray();
        }

        public string SchemaVersion { get; }
        public string BattleId { get; }
        public int MaxTicks { get; }
        public IReadOnlyList<UnitDefinition> UnitDefinitions { get; }
        public IReadOnlyList<AbilityDefinition> AbilityDefinitions { get; }
        public IReadOnlyList<PlayerSnapshot> Players { get; }
    }

    public sealed class UnitDefinition
    {
        public UnitDefinition(string typeId, int maxHitPoints, int attack, int defense, int magicResistance, int moveSpeedCentimetresPerSecond, int attackIntervalTicks, int attackAnimationDurationTicks, DamageType damageType, AttackMethod attackMethod, int blockCapacity, bool isSyntheticFixtureData)
            : this(typeId, maxHitPoints, attack, defense, magicResistance, moveSpeedCentimetresPerSecond, attackIntervalTicks, attackAnimationDurationTicks, damageType, attackMethod, blockCapacity, 0, isSyntheticFixtureData)
        {
        }

        public UnitDefinition(string typeId, int maxHitPoints, int attack, int defense, int magicResistance, int moveSpeedCentimetresPerSecond, int attackIntervalTicks, int attackAnimationDurationTicks, DamageType damageType, AttackMethod attackMethod, int blockCapacity, int tauntLevel, bool isSyntheticFixtureData)
            : this(typeId, maxHitPoints, attack, defense, magicResistance, moveSpeedCentimetresPerSecond, attackIntervalTicks, attackAnimationDurationTicks, damageType, attackMethod, blockCapacity, tauntLevel, isSyntheticFixtureData, Enumerable.Empty<string>())
        {
        }

        public UnitDefinition(string typeId, int maxHitPoints, int attack, int defense, int magicResistance, int moveSpeedCentimetresPerSecond, int attackIntervalTicks, int attackAnimationDurationTicks, DamageType damageType, AttackMethod attackMethod, int blockCapacity, int tauntLevel, bool isSyntheticFixtureData, IEnumerable<string> innateAbilityIds)
            : this(
                typeId,
                maxHitPoints,
                attack,
                defense,
                magicResistance,
                moveSpeedCentimetresPerSecond,
                attackIntervalTicks,
                attackAnimationDurationTicks,
                damageType,
                attackMethod,
                blockCapacity,
                tauntLevel,
                isSyntheticFixtureData,
                innateAbilityIds,
                1)
        {
        }

        public UnitDefinition(string typeId, int maxHitPoints, int attack, int defense, int magicResistance, int moveSpeedCentimetresPerSecond, int attackIntervalTicks, int attackAnimationDurationTicks, DamageType damageType, AttackMethod attackMethod, int blockCapacity, int tauntLevel, bool isSyntheticFixtureData, IEnumerable<string> innateAbilityIds, int actionMethod)
            : this(
                typeId,
                maxHitPoints,
                attack,
                defense,
                magicResistance,
                moveSpeedCentimetresPerSecond,
                attackIntervalTicks,
                attackAnimationDurationTicks,
                damageType,
                attackMethod,
                blockCapacity,
                tauntLevel,
                isSyntheticFixtureData,
                innateAbilityIds,
                actionMethod,
                Enumerable.Empty<UnitSkillAnimationDefinition>())
        {
        }

        public UnitDefinition(string typeId, int maxHitPoints, int attack, int defense, int magicResistance, int moveSpeedCentimetresPerSecond, int attackIntervalTicks, int attackAnimationDurationTicks, DamageType damageType, AttackMethod attackMethod, int blockCapacity, int tauntLevel, bool isSyntheticFixtureData, IEnumerable<string> innateAbilityIds, int actionMethod, IEnumerable<UnitSkillAnimationDefinition> skillAnimations)
        {
            TypeId = typeId;
            MaxHitPoints = maxHitPoints;
            Attack = attack;
            Defense = defense;
            MagicResistance = magicResistance;
            MoveSpeedCentimetresPerSecond = moveSpeedCentimetresPerSecond;
            AttackIntervalTicks = attackIntervalTicks;
            AttackAnimationDurationTicks = attackAnimationDurationTicks;
            DamageType = damageType;
            AttackMethod = attackMethod;
            BlockCapacity = blockCapacity;
            TauntLevel = tauntLevel;
            IsSyntheticFixtureData = isSyntheticFixtureData;
            InnateAbilityIds = new ReadOnlyCollection<string>((innateAbilityIds ?? Enumerable.Empty<string>()).ToArray());
            ActionMethod = actionMethod;
            SkillAnimations = new ReadOnlyCollection<UnitSkillAnimationDefinition>((skillAnimations ?? Enumerable.Empty<UnitSkillAnimationDefinition>()).ToArray());
        }

        public string TypeId { get; }
        public int MaxHitPoints { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int MagicResistance { get; }
        public int MoveSpeedCentimetresPerSecond { get; }
        public int AttackIntervalTicks { get; }
        public int AttackAnimationDurationTicks { get; }
        public DamageType DamageType { get; }
        public AttackMethod AttackMethod { get; }
        public int BlockCapacity { get; }
        public int TauntLevel { get; }
        public bool IsSyntheticFixtureData { get; }
        public IReadOnlyList<string> InnateAbilityIds { get; }
        public int ActionMethod { get; }
        public IReadOnlyList<UnitSkillAnimationDefinition> SkillAnimations { get; }
        public bool CanAttack => AttackMethod != AttackMethod.None;
        public bool TryGetSkillAnimation(string key, out UnitSkillAnimationDefinition animation)
        {
            animation = SkillAnimations.FirstOrDefault(item =>
                item != null
                && string.Equals(item.Key, key, StringComparison.Ordinal));
            return animation != null;
        }
    }

    public readonly struct BuffPlaceholder : IEquatable<BuffPlaceholder>
    {
        public BuffPlaceholder(string id, string rawPayload) { Id = id ?? string.Empty; RawPayload = rawPayload ?? string.Empty; }
        public string Id { get; }
        public string RawPayload { get; }
        public bool Equals(BuffPlaceholder other) => Id == other.Id && RawPayload == other.RawPayload;
        public override bool Equals(object obj) => obj is BuffPlaceholder other && Equals(other);
        public override int GetHashCode() => (Id.GetHashCode() * 397) ^ RawPayload.GetHashCode();
    }

    public sealed class UnitSnapshot
    {
        public UnitSnapshot(string unitId, string typeId, UnitZone zone, FormationCoordinate? formation, IEnumerable<BuffPlaceholder> buffs)
            : this(unitId, typeId, zone, formation, buffs, 0)
        {
        }

        public UnitSnapshot(string unitId, string typeId, UnitZone zone, FormationCoordinate? formation, IEnumerable<BuffPlaceholder> buffs, int eliteLevel)
        {
            UnitId = unitId;
            TypeId = typeId;
            Zone = zone;
            Formation = formation;
            Buffs = new ReadOnlyCollection<BuffPlaceholder>((buffs ?? Enumerable.Empty<BuffPlaceholder>()).ToArray());
            EliteLevel = eliteLevel;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public UnitZone Zone { get; }
        public FormationCoordinate? Formation { get; }
        public IReadOnlyList<BuffPlaceholder> Buffs { get; }
        public int EliteLevel { get; }
    }

    public sealed class PlayerSnapshot
    {
        public PlayerSnapshot(string playerId, BattleSide side, IEnumerable<UnitSnapshot> units)
        {
            PlayerId = playerId;
            Side = side;
            Units = new ReadOnlyCollection<UnitSnapshot>((units ?? Enumerable.Empty<UnitSnapshot>()).ToArray());
        }

        public string PlayerId { get; }
        public BattleSide Side { get; }
        public IReadOnlyList<UnitSnapshot> Units { get; }
    }

    public sealed class BattleInput
    {
        internal BattleInput(BattleInputSpecification specification)
        {
            SchemaVersion = specification.SchemaVersion;
            BattleId = specification.BattleId;
            MaxTicks = specification.MaxTicks;
            UnitDefinitions = new ReadOnlyCollection<UnitDefinition>(specification.UnitDefinitions.ToArray());
            AbilityDefinitions = new ReadOnlyCollection<AbilityDefinition>(specification.AbilityDefinitions.ToArray());
            Players = new ReadOnlyCollection<PlayerSnapshot>(specification.Players.ToArray());
            CanonicalSummary = BuildCanonicalSummary();
        }

        public const string SupportedSchemaVersion = "battle-fixture-v1";
        public const string LocalBattleSchemaVersion = "local-battle-v1";
        public const int TicksPerSecond = 20;
        public const int AutomaticSkillPointsPerSecond = 2;
        public string SchemaVersion { get; }
        public string BattleId { get; }
        public int MaxTicks { get; }
        public IReadOnlyList<UnitDefinition> UnitDefinitions { get; }
        public IReadOnlyList<AbilityDefinition> AbilityDefinitions { get; }
        public IReadOnlyList<PlayerSnapshot> Players { get; }
        public string CanonicalSummary { get; }

        private string BuildCanonicalSummary()
        {
            var builder = new StringBuilder();
            builder.Append(SchemaVersion).Append('|').Append(BattleId).Append('|').Append(MaxTicks);
            foreach (var definition in UnitDefinitions.OrderBy(item => item.TypeId, StringComparer.Ordinal))
            {
                builder.Append("|T:").Append(definition.TypeId).Append(',').Append(definition.MaxHitPoints).Append(',').Append(definition.Attack).Append(',').Append(definition.Defense).Append(',').Append(definition.MagicResistance).Append(',').Append(definition.MoveSpeedCentimetresPerSecond).Append(',').Append(definition.AttackIntervalTicks).Append(',').Append(definition.AttackAnimationDurationTicks).Append(',').Append((int)definition.DamageType).Append(',').Append((int)definition.AttackMethod).Append(',').Append(definition.BlockCapacity).Append(',').Append(definition.TauntLevel).Append(',').Append(definition.IsSyntheticFixtureData ? 1 : 0);
                if (definition.ActionMethod != 1)
                    builder.Append("|M:").Append(definition.ActionMethod);
                foreach (var animation in definition.SkillAnimations.Where(item => item != null).OrderBy(item => item.Key, StringComparer.Ordinal))
                    builder.Append("|K:").Append(animation.Key).Append(',').Append(animation.OriginalDurationTicks);
                foreach (var innateId in definition.InnateAbilityIds.OrderBy(item => item, StringComparer.Ordinal)) builder.Append("|I:").Append(innateId);
            }
            foreach (var ability in AbilityDefinitions.OrderBy(item => item.AbilityId, StringComparer.Ordinal))
            {
                builder.Append("|A:").Append(ability.AbilityId).Append(',').Append((int)ability.ActivationKind).Append(',').Append((int)ability.SilencePolicy).Append(',').Append(ability.InitialSkillPoints).Append(',').Append(ability.RequiredSkillPoints).Append(',').Append((int)ability.SkillPointGeneration).Append(',').Append(ability.SummonEffect == null ? string.Empty : ability.SummonEffect.SummonTypeId).Append(',').Append(ability.SummonEffect == null ? 0 : ability.SummonEffect.Count).Append(',').Append(ability.SummonEffect == null ? 0 : ability.SummonEffect.SideLengthCentimetres).Append(',').Append(ability.SummonEffect != null && ability.SummonEffect.InheritPathFromCaster ? 1 : 0).Append(',').Append(ability.AnimationKey);
                if (ability.UnitTraitEffect != null)
                    builder.Append("|G:").Append((int)ability.UnitTraitEffect.Kind);
            }
            foreach (var player in Players.OrderBy(item => item.Side).ThenBy(item => item.PlayerId, StringComparer.Ordinal))
            {
                builder.Append("|P:").Append((int)player.Side).Append(',').Append(player.PlayerId);
                foreach (var unit in player.Units.OrderBy(item => item.UnitId, StringComparer.Ordinal))
                {
                    builder.Append("|U:").Append(unit.UnitId).Append(',').Append(unit.TypeId).Append(',').Append((int)unit.Zone).Append(',').Append(unit.EliteLevel).Append(',');
                    if (unit.Formation.HasValue) builder.Append(unit.Formation.Value.X).Append(',').Append(unit.Formation.Value.Y);
                    foreach (var buff in unit.Buffs.OrderBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.RawPayload, StringComparer.Ordinal)) builder.Append("|B:").Append(buff.Id).Append(',').Append(buff.RawPayload);
                }
            }
            return builder.ToString();
        }
    }

    public static class BattleInputFactory
    {
        public static bool TryCreate(BattleInputSpecification specification, out BattleInput input, out IReadOnlyList<ValidationError> errors)
        {
            var validationErrors = new List<ValidationError>();
            input = null;
            if (specification == null)
            {
                validationErrors.Add(new ValidationError("input.missing", "Battle input is required."));
                errors = validationErrors;
                return false;
            }

            if (specification.SchemaVersion != BattleInput.SupportedSchemaVersion && specification.SchemaVersion != BattleInput.LocalBattleSchemaVersion)
                validationErrors.Add(new ValidationError("schema.unsupported", "Unsupported schema version: " + specification.SchemaVersion));
            if (string.IsNullOrWhiteSpace(specification.BattleId)) validationErrors.Add(new ValidationError("battleId.invalid", "Battle ID is required."));
            if (specification.MaxTicks <= 0) validationErrors.Add(new ValidationError("maxTicks.invalid", "maxTicks must be positive."));

            var typeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in specification.UnitDefinitions)
            {
                if (definition == null) { validationErrors.Add(new ValidationError("type.missing", "Unit definition is missing.")); continue; }
                if (string.IsNullOrWhiteSpace(definition.TypeId)) validationErrors.Add(new ValidationError("typeId.invalid", "Type ID is required."));
                else if (!typeIds.Add(definition.TypeId)) validationErrors.Add(new ValidationError("typeId.duplicate", "Duplicate type ID: " + definition.TypeId));
                if (definition.MaxHitPoints <= 0 || definition.Attack < 0 || definition.Defense < 0 || definition.MagicResistance < 0 || definition.MagicResistance > 100 || definition.MoveSpeedCentimetresPerSecond < 0 || definition.BlockCapacity < 0 || definition.TauntLevel < 0 || definition.ActionMethod < 1 || definition.ActionMethod > 4)
                    validationErrors.Add(new ValidationError("type.values.invalid", "Unit definition has invalid numeric values: " + (definition.TypeId ?? "<missing>")));
                if (!Enum.IsDefined(typeof(DamageType), definition.DamageType) || !Enum.IsDefined(typeof(AttackMethod), definition.AttackMethod)) validationErrors.Add(new ValidationError("type.enum.invalid", "Unit definition has invalid enum values: " + (definition.TypeId ?? "<missing>")));
                else if (definition.CanAttack)
                {
                    if (definition.DamageType == DamageType.None || definition.AttackIntervalTicks <= 0 || definition.AttackAnimationDurationTicks <= 0)
                        validationErrors.Add(new ValidationError("type.attack.values.invalid", "Attacking unit requires damage and positive attack timings: " + (definition.TypeId ?? "<missing>")));
                }
                else if (definition.DamageType != DamageType.None || definition.Attack != 0 || definition.AttackIntervalTicks != 0 || definition.AttackAnimationDurationTicks != 0)
                {
                    validationErrors.Add(new ValidationError("type.nonAttacker.values.invalid", "Non-attacking unit must use None damage, zero attack and zero attack timings: " + (definition.TypeId ?? "<missing>")));
                }
                var skillAnimationKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var animation in definition.SkillAnimations)
                {
                    if (animation == null
                        || string.IsNullOrWhiteSpace(animation.Key)
                        || animation.OriginalDurationTicks <= 0)
                        validationErrors.Add(new ValidationError("type.skillAnimation.invalid", "Skill animation is invalid: " + (definition.TypeId ?? "<missing>")));
                    else if (!skillAnimationKeys.Add(animation.Key))
                        validationErrors.Add(new ValidationError("type.skillAnimation.duplicate", "Duplicate skill animation key: " + animation.Key));
                }
            }

            var abilityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ability in specification.AbilityDefinitions)
            {
                if (ability == null) { validationErrors.Add(new ValidationError("ability.missing", "Ability definition is missing.")); continue; }
                if (string.IsNullOrWhiteSpace(ability.AbilityId)) validationErrors.Add(new ValidationError("ability.id.invalid", "Ability ID is required."));
                else if (!abilityIds.Add(ability.AbilityId)) validationErrors.Add(new ValidationError("ability.id.duplicate", "Duplicate ability ID: " + ability.AbilityId));
                if (!Enum.IsDefined(typeof(AbilityActivationKind), ability.ActivationKind) || !Enum.IsDefined(typeof(SilencePolicy), ability.SilencePolicy) || !Enum.IsDefined(typeof(SkillPointGeneration), ability.SkillPointGeneration)) validationErrors.Add(new ValidationError("ability.enum.invalid", "Ability has an invalid enum value: " + ability.AbilityId));
                if (ability.ActivationKind == AbilityActivationKind.Timed)
                {
                    if (ability.RequiredSkillPoints <= 0) validationErrors.Add(new ValidationError("ability.skillPoints.required.invalid", "Required skill points must be positive: " + ability.AbilityId));
                    if (ability.InitialSkillPoints < 0) validationErrors.Add(new ValidationError("ability.skillPoints.initial.invalid", "Initial skill points cannot be negative: " + ability.AbilityId));
                    if (ability.InitialSkillPoints > ability.RequiredSkillPoints) validationErrors.Add(new ValidationError("ability.skillPoints.order.invalid", "Initial skill points cannot exceed required skill points: " + ability.AbilityId));
                    if (ability.UnitTraitEffect != null) validationErrors.Add(new ValidationError("ability.trait.unexpected", "Timed ability cannot define a unit trait: " + ability.AbilityId));
                    if (string.IsNullOrWhiteSpace(ability.AnimationKey)) validationErrors.Add(new ValidationError("ability.animationKey.invalid", "Timed ability requires an animation key: " + ability.AbilityId));
                    if (ability.SummonEffect == null) validationErrors.Add(new ValidationError("ability.summon.missing", "Summon effect is required: " + ability.AbilityId));
                    else
                    {
                        if (string.IsNullOrWhiteSpace(ability.SummonEffect.SummonTypeId) || !typeIds.Contains(ability.SummonEffect.SummonTypeId)) validationErrors.Add(new ValidationError("ability.summon.type.unknown", "Summon type is unknown: " + ability.SummonEffect.SummonTypeId));
                        if (ability.SummonEffect.Count <= 0) validationErrors.Add(new ValidationError("ability.summon.count.invalid", "Summon count must be positive: " + ability.AbilityId));
                        if (ability.SummonEffect.SideLengthCentimetres <= 0) validationErrors.Add(new ValidationError("ability.summon.sideLength.invalid", "Summon side length must be positive: " + ability.AbilityId));
                    }
                }
                else if (ability.ActivationKind == AbilityActivationKind.Passive)
                {
                    if (ability.InitialSkillPoints != 0 || ability.RequiredSkillPoints != 0)
                        validationErrors.Add(new ValidationError("ability.passive.skillPoints.invalid", "Passive ability cannot define skill points: " + ability.AbilityId));
                    if (ability.SkillPointGeneration != SkillPointGeneration.None)
                        validationErrors.Add(new ValidationError("ability.passive.generation.invalid", "Passive ability must use None skill-point generation: " + ability.AbilityId));
                    if (ability.SummonEffect != null)
                        validationErrors.Add(new ValidationError("ability.passive.summon.unexpected", "Passive ability cannot define a summon effect: " + ability.AbilityId));
                    if (ability.UnitTraitEffect == null || !Enum.IsDefined(typeof(UnitTraitEffectKind), ability.UnitTraitEffect.Kind))
                        validationErrors.Add(new ValidationError("ability.trait.invalid", "Passive ability requires a valid unit trait: " + ability.AbilityId));
                    if (!string.IsNullOrEmpty(ability.AnimationKey))
                        validationErrors.Add(new ValidationError("ability.passive.animation.unexpected", "Passive ability cannot define an animation key: " + ability.AbilityId));
                }
            }
            foreach (var definition in specification.UnitDefinitions.Where(item => item != null))
            {
                var innateIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var innateAbilityId in definition.InnateAbilityIds)
                {
                    if (string.IsNullOrWhiteSpace(innateAbilityId)) validationErrors.Add(new ValidationError("unit.innateAbility.invalid", "Unit innate ability ID is required: " + definition.TypeId));
                    else if (!innateIds.Add(innateAbilityId)) validationErrors.Add(new ValidationError("unit.innateAbility.duplicate", "Duplicate unit innate ability ID: " + innateAbilityId));
                    else if (!abilityIds.Contains(innateAbilityId)) validationErrors.Add(new ValidationError("unit.innateAbility.unknown", "Unit references an unknown innate ability ID: " + innateAbilityId));
                    else
                    {
                        var ability = specification.AbilityDefinitions.First(item =>
                            item != null
                            && string.Equals(item.AbilityId, innateAbilityId, StringComparison.Ordinal));
                        if (ability.ActivationKind == AbilityActivationKind.Timed
                            && !definition.TryGetSkillAnimation(ability.AnimationKey, out _))
                            validationErrors.Add(new ValidationError("unit.skillAnimation.missing", "Unit has no animation for timed ability " + innateAbilityId + ": " + definition.TypeId));
                    }
                }
            }

            var playersBySide = new Dictionary<BattleSide, PlayerSnapshot>();
            var playerIds = new HashSet<string>(StringComparer.Ordinal);
            var unitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var player in specification.Players)
            {
                if (player == null) { validationErrors.Add(new ValidationError("player.missing", "Player snapshot is missing.")); continue; }
                if (string.IsNullOrWhiteSpace(player.PlayerId)) validationErrors.Add(new ValidationError("playerId.invalid", "Player ID is required."));
                else if (!playerIds.Add(player.PlayerId)) validationErrors.Add(new ValidationError("playerId.duplicate", "Duplicate player ID: " + player.PlayerId));
                if (!Enum.IsDefined(typeof(BattleSide), player.Side)) validationErrors.Add(new ValidationError("player.side.invalid", "Player side is invalid."));
                else if (playersBySide.ContainsKey(player.Side)) validationErrors.Add(new ValidationError("player.side.duplicate", "Duplicate player side: " + player.Side));
                else playersBySide.Add(player.Side, player);

                foreach (var unit in player.Units)
                {
                    if (unit == null) { validationErrors.Add(new ValidationError("unit.missing", "Unit snapshot is missing.")); continue; }
                    if (string.IsNullOrWhiteSpace(unit.UnitId)) validationErrors.Add(new ValidationError("unitId.invalid", "Unit ID is required."));
                    else if (IsReservedDynamicUnitId(unit.UnitId)) validationErrors.Add(new ValidationError("unitId.reserved.dynamic", "Initial unit ID is reserved for dynamic units: " + unit.UnitId));
                    else if (!unitIds.Add(unit.UnitId)) validationErrors.Add(new ValidationError("unitId.duplicate", "Duplicate unit ID: " + unit.UnitId));
                    if (unit.EliteLevel < 0 || unit.EliteLevel > 3) validationErrors.Add(new ValidationError("unit.elite.invalid", "Unit elite level must be within 0..3: " + unit.UnitId));
                    if (string.IsNullOrWhiteSpace(unit.TypeId) || !typeIds.Contains(unit.TypeId)) validationErrors.Add(new ValidationError("unit.type.unknown", "Unit has an unknown type ID: " + (unit.TypeId ?? "<missing>")));
                    if (!Enum.IsDefined(typeof(UnitZone), unit.Zone)) validationErrors.Add(new ValidationError("unit.zone.invalid", "Unit zone is invalid: " + unit.UnitId));
                    if (unit.Zone == UnitZone.Deployed)
                    {
                        if (!unit.Formation.HasValue) validationErrors.Add(new ValidationError("unit.formation.missing", "Deployed unit requires a formation coordinate: " + unit.UnitId));
                        else if (!unit.Formation.Value.IsValid) validationErrors.Add(new ValidationError("unit.formation.invalid", "Unit has an invalid formation coordinate: " + unit.UnitId));
                        else
                        {
                            var battlefield = player.Side == BattleSide.Home ? BattlefieldRules.MapHome(unit.Formation.Value) : BattlefieldRules.MapAway(unit.Formation.Value);
                            if (!BattlefieldRules.IsDeployable(battlefield)) validationErrors.Add(new ValidationError("unit.formation.notDeployable", "Unit maps to a non-deployable battlefield coordinate: " + unit.UnitId));
                        }
                    }
                    else if (unit.Formation.HasValue) validationErrors.Add(new ValidationError("unit.formation.unexpected", "Only deployed units may have a formation coordinate: " + unit.UnitId));
                }
            }

            if (!playersBySide.ContainsKey(BattleSide.Home)) validationErrors.Add(new ValidationError("player.home.missing", "Exactly one Home player is required."));
            if (!playersBySide.ContainsKey(BattleSide.Away)) validationErrors.Add(new ValidationError("player.away.missing", "Exactly one Away player is required."));
            errors = new ReadOnlyCollection<ValidationError>(validationErrors);
            if (validationErrors.Count != 0) return false;
            input = new BattleInput(specification);
            return true;
        }

        private static bool IsReservedDynamicUnitId(string value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed < 0;
        }
    }
}
