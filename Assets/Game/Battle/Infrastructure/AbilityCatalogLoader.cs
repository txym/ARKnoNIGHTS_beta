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
            var skillAnimations =
                SkillAnimationCatalogLoader.LoadFromResources();
            if (!skillAnimations.Success)
                errors.AddRange(skillAnimations.Errors);
            if (!string.Equals(dto.schemaVersion, SchemaVersion, StringComparison.Ordinal)) errors.Add(new ValidationError("ability.schema.unsupported", "Unsupported ability schema: " + dto.schemaVersion));
            if (string.IsNullOrWhiteSpace(dto.catalogId)) errors.Add(new ValidationError("ability.catalogId.invalid", "Ability catalog ID is required."));
            var abilities = new List<AbilityDefinition>();
            foreach (var source in dto.abilities ?? Array.Empty<AbilityDto>())
                abilities.Add(Convert(source, skillAnimations.Catalog));
            if (abilities.Count == 0) errors.Add(new ValidationError("ability.catalog.empty", "Ability catalog must contain at least one ability."));

            var definitions = unitCatalog == null ? Enumerable.Empty<UnitDefinition>() : unitCatalog.Entries.Select(entry => entry.Definition);
            var specification = new BattleInputSpecification(BattleInput.LocalBattleSchemaVersion, "ability-catalog-validation", 1, definitions, abilities, new[]
            {
                new PlayerSnapshot("home", BattleSide.Home, Array.Empty<UnitSnapshot>()),
                new PlayerSnapshot("away", BattleSide.Away, Array.Empty<UnitSnapshot>())
            });
            BattleInputFactory.TryCreate(specification, out _, out var validationErrors);
            errors.AddRange(validationErrors.Where(error => error.Code.StartsWith("ability.", StringComparison.Ordinal)));
            if (skillAnimations.Success && unitCatalog != null)
            {
                foreach (var binding in skillAnimations.Catalog.Bindings)
                {
                    if (!unitCatalog.TryGet(binding.TypeId, out var unit)
                        || !unit.Definition.InnateAbilityIds.Contains(
                            binding.AbilityId))
                        errors.Add(new ValidationError(
                            "skillAnimation.unitAbility.mismatch",
                            "Skill animation binding does not match a unit innate ability: "
                            + binding.TypeId
                            + "/"
                            + binding.AbilityId));
                }
            }
            if (abilities.Any(ability => ability != null && ability.AbilityId == "SUMMON_JELLY_MINIONS" && ability.SummonEffect != null && ability.SummonEffect.InheritPathFromCaster))
                errors.Add(new ValidationError("ability.summon.inheritPath.invalid", "SUMMON_JELLY_MINIONS cannot inherit the caster path."));
            return errors.Count == 0
                ? new AbilityCatalogLoadResult(new AbilityCatalog(dto.schemaVersion, dto.catalogId, abilities), new ReadOnlyCollection<ValidationError>(errors))
                : new AbilityCatalogLoadResult(null, new ReadOnlyCollection<ValidationError>(errors));
        }

        private static AbilityDefinition Convert(
            AbilityDto source,
            SkillAnimationCatalog skillAnimations)
        {
            if (source == null) return null;
            SkillAnimationCatalogBinding skillAnimation = null;
            var hasAnimationBinding = skillAnimations != null
                && skillAnimations.TryGetAbility(
                    source.abilityId,
                    out skillAnimation);
            var hasSkillAnimation = hasAnimationBinding
                && !string.IsNullOrWhiteSpace(
                    skillAnimation.AnimationKey)
                && skillAnimation.OriginalAnimationTicks > 0;
            var activationKind =
                ParseEnum<AbilityActivationKind>(source.activationKind);
            return new AbilityDefinition(
                abilityId: source.abilityId,
                displayNameZhHans: source.displayNameZhHans,
                descriptionZhHans: source.descriptionZhHans,
                activationKind: activationKind,
                silencePolicy:
                    ParseEnum<SilencePolicy>(source.silencePolicy),
                initialSkillPoints: source.initialSkillPoints,
                requiredSkillPoints: source.requiredSkillPoints,
                skillPointGeneration:
                    ParseEnum<SkillPointGeneration>(
                        source.skillPointGeneration),
                summonEffect: string.IsNullOrWhiteSpace(source.summonTypeId)
                    ? null
                    : new SummonEffectDefinition(source.summonTypeId, source.count, source.sideLengthCentimetres, source.inheritPathFromCaster),
                unitTraitEffect: string.IsNullOrWhiteSpace(source.unitTrait)
                    ? null
                    : new UnitTraitEffectDefinition(ParseEnum<UnitTraitEffectKind>(source.unitTrait)),
                passiveCombatModifier: source.blockCapacityAdditive == 0
                    && source.magicResistanceAdditive == 0
                    && source.attackSpeedAdditive == 0
                    && source.physicalDamageTakenPermille == 0
                    && source.magicDamageTakenPermille == 0
                        ? null
                        : new PassiveCombatModifierDefinition(
                            source.blockCapacityAdditive,
                            source.magicResistanceAdditive,
                            source.attackSpeedAdditive,
                            source.physicalDamageTakenPermille,
                            source.magicDamageTakenPermille),
                passiveLifecycleEffect:
                    source.hitPointsPerSecond == 0
                    && source.lifetimeTicks == 0
                        ? null
                        : new PassiveLifecycleEffectDefinition(
                            source.hitPointsPerSecond,
                            source.lifetimeTicks),
                onDamageReactionEffect:
                    source.onDamageReactionDamageAmount == 0
                        ? null
                        : new OnDamageReactionEffectDefinition(
                            ParseEnum<DamageType>(
                                source.onDamageReactionDamageType),
                            source.onDamageReactionDamageAmount),
                healthThresholdCombatModifier:
                    source.healthThresholdCombatHitPointsPermille == 0
                        ? null
                        : new HealthThresholdCombatModifierDefinition(
                            source
                                .healthThresholdCombatHitPointsPermille,
                            source.healthThresholdCombatInclusive,
                            source.healthThresholdCombatTriggerOnce,
                            source.healthThresholdCombatDurationTicks,
                            source
                                .healthThresholdCombatAttackMultiplierPermille,
                            source
                                .healthThresholdCombatDefenseMultiplierPermille,
                            source
                                .healthThresholdCombatBlockCapacityAdditive,
                            source
                                .healthThresholdCombatAttackSpeedAdditive,
                            source
                                .healthThresholdCombatMoveSpeedMultiplierPermille,
                            source
                                .healthThresholdCombatMakesUnblockable,
                            hasSkillAnimation
                                ? skillAnimation.AnimationKey
                                : string.Empty,
                            hasSkillAnimation
                                ? skillAnimation.OriginalAnimationTicks
                                : 0,
                            source.persistentPresentationStateTag),
                unblockedDamageTakenModifier:
                    source.unblockedPhysicalDamageTakenPermille == 0
                    && source.unblockedMagicDamageTakenPermille == 0
                        ? null
                        : new UnblockedDamageTakenModifierDefinition(
                            source
                                .unblockedPhysicalDamageTakenPermille,
                            source
                                .unblockedMagicDamageTakenPermille),
                attackSequenceModifier:
                    source.attackSequenceFirstEnhancedAttackOrdinal == 0
                        ? null
                        : new AttackSequenceModifierDefinition(
                            source
                                .attackSequenceFirstEnhancedAttackOrdinal,
                            source.attackSequenceRepeatInterval,
                            source
                                .attackSequenceAttackMultiplierPermille),
                attackCountStateModifier:
                    source.attackCountTransitionBeforeAttackOrdinal == 0
                        ? null
                        : new AttackCountStateModifierDefinition(
                            source
                                .attackCountTransitionBeforeAttackOrdinal,
                            source
                                .attackCountLockedAttackSpeedAdditive,
                            source.attackCountLockedDefenseAdditive,
                            source
                                .attackCountUnlockedAttackMultiplierPermille,
                            source
                                .attackCountUnlockedMagicResistanceAdditive,
                            source
                                .attackCountUnlockedHitPointsPerSecond,
                            source
                                .attackCountUnlockedTargetDefenseMultiplierPermille,
                            source.attackCountReleasesAlliedStates,
                            source.persistentPresentationStateTag),
                deathSpawnEffect:
                    source.deathSpawnCount == 0
                        ? null
                        : new DeathSpawnEffectDefinition(
                            (source.deathSpawnOptions
                             ?? Array.Empty<DeathSpawnOptionDto>())
                            .Where(item => item != null)
                            .Select(item =>
                                new DeathSpawnOptionDefinition(
                                    item.summonTypeId,
                                    item.weight)),
                            source.deathSpawnCount,
                            source.deathSpawnDelayTicks,
                            source.deathSpawnSideLengthCentimetres,
                            source.deathSpawnSnapToNearestPassableCell,
                            source
                                .deathSpawnSummonedMoveSpeedMultiplierPermille),
                auraCombatModifier:
                    string.IsNullOrWhiteSpace(source.auraTargetSide)
                        ? null
                        : new AuraCombatModifierDefinition(
                            ParseEnum<AuraTargetSide>(
                                source.auraTargetSide),
                            source.auraIsGlobal,
                            source.auraRadiusCentimetres,
                            source.auraExcludeSource,
                            source.auraNonStackingByAbilityId,
                            source.auraAttackMultiplierPermille,
                            source.auraDefenseAdditive,
                            source.auraMagicResistanceAdditive,
                            source.auraAttackSpeedMultiplierPermille,
                            source.auraMoveSpeedMultiplierPermille,
                            source.auraHitPointsPerSecond,
                            source.auraGrantedStatusTag),
                blockedCounterpartCombatModifier:
                    source
                        .blockedCounterpartAttackSpeedMultiplierPermille
                    == 0
                        ? null
                        : new BlockedCounterpartCombatModifierDefinition(
                            source
                                .blockedCounterpartNonStackingByAbilityId,
                            source
                                .blockedCounterpartAttackSpeedMultiplierPermille),
                nearbySameTypeSelfModifier:
                    source.nearbySameTypeRadiusCentimetres == 0
                        ? null
                        : new NearbySameTypeSelfModifierDefinition(
                            source.nearbySameTypeRadiusCentimetres,
                            source.nearbySameTypeDefenseAdditive),
                evasionModifier:
                    source.evasionPhysicalChancePermille == 0
                    && source.evasionMagicChancePermille == 0
                        ? null
                        : new EvasionModifierDefinition(
                            source.evasionPhysicalChancePermille,
                            source.evasionMagicChancePermille),
                deathAreaDamageEffect:
                    source.deathAreaRadiusCentimetres == 0
                        ? null
                        : new DeathAreaDamageEffectDefinition(
                            ParseEnum<DamageType>(
                                source.deathAreaDamageType),
                            source.deathAreaAttackMultiplierPermille,
                            source.deathAreaRadiusCentimetres,
                            source.deathAreaDelayTicks),
                attackAreaDamageModifier:
                    source.attackAreaFirstAttackOrdinal == 0
                        ? null
                        : new AttackAreaDamageModifierDefinition(
                            ParseEnum<AttackAreaShape>(
                                source.attackAreaShape),
                            source.attackAreaFirstAttackOrdinal,
                            source.attackAreaRepeatInterval,
                            ParseEnum<DamageType>(
                                source.attackAreaDamageType),
                            source.attackAreaAttackMultiplierPermille,
                            source.attackAreaRadiusCentimetres),
                onHitDamageOverTimeEffect: null,
                unblockedAttackCharge:
                    source.unblockedAttackChargeCheckIntervalTicks == 0
                        ? null
                        : new UnblockedAttackChargeDefinition(
                            source
                                .unblockedAttackChargeCheckIntervalTicks,
                            source
                                .unblockedAttackChargeAttackAdditivePerStack,
                            source.unblockedAttackChargeMaxStacks),
                triggeredSpawnEffect:
                    string.IsNullOrWhiteSpace(source.triggeredSpawnKind)
                        ? null
                        : new TriggeredSpawnEffectDefinition(
                            ParseEnum<TriggeredSpawnKind>(
                                source.triggeredSpawnKind),
                            source.triggeredSpawnFirstTriggerOrdinal,
                            source.triggeredSpawnRepeatInterval,
                            source.triggeredSpawnSummonTypeId,
                            source.triggeredSpawnSideLengthCentimetres,
                            source.triggeredSpawnMaxActiveSameType,
                            hasSkillAnimation
                                ? skillAnimation.AnimationKey
                                : string.Empty,
                            hasSkillAnimation
                                ? skillAnimation.OriginalAnimationTicks
                                : 0),
                healthThresholdAdjacentSpawnEffect:
                    source
                        .healthThresholdAdjacentSpawnHitPointsPermille
                    == 0
                        ? null
                        : new HealthThresholdAdjacentSpawnEffectDefinition(
                            source
                                .healthThresholdAdjacentSpawnHitPointsPermille,
                            source
                                .healthThresholdAdjacentSpawnInclusive,
                            source
                                .healthThresholdAdjacentSpawnTypeId),
                healthThresholdFullHealEffect:
                    source.healthThresholdFullHealHitPointsPermille == 0
                        ? null
                        : new HealthThresholdFullHealEffectDefinition(
                            source
                                .healthThresholdFullHealHitPointsPermille,
                            source.healthThresholdFullHealInclusive,
                            hasSkillAnimation
                                ? skillAnimation.AnimationKey
                                : string.Empty,
                            hasSkillAnimation
                                ? skillAnimation.OriginalAnimationTicks
                                : 0,
                            source.persistentPresentationStateTag),
                onHitDefenseDebuffEffect:
                    source.onHitDefenseReductionPerStack == 0
                    ? null
                    : new OnHitDefenseDebuffEffectDefinition(
                        source.onHitDefenseReductionPerStack),
                animationKey:
                    hasSkillAnimation
                    && activationKind == AbilityActivationKind.Timed
                        ? skillAnimation.AnimationKey
                        : string.Empty,
                skillAnimationOriginalDurationTicks:
                    hasSkillAnimation
                    && activationKind == AbilityActivationKind.Timed
                        ? skillAnimation.OriginalAnimationTicks
                        : 0,
                timedTargetAreaDamageEffect:
                    source.targetRangeCentimetres == 0
                    ? null
                    : new TimedTargetAreaDamageEffectDefinition(
                        source.targetRangeCentimetres,
                        source.areaRadiusCentimetres,
                        ParseEnum<DamageType>(source.areaDamageType),
                        source.areaAttackMultiplierPermille,
                        source.groundTargetsOnly),
                attackDashEffect:
                    source.attackDashFirstTriggerOrdinal == 0
                    ? null
                    : new AttackDashEffectDefinition(
                        source.attackDashFirstTriggerOrdinal,
                        source.attackDashRepeatInterval,
                        source.attackDashDistanceCentimetres,
                        source.attackDashUnblockableDurationTicks,
                        hasSkillAnimation
                        && skillAnimation
                            .SegmentOriginalAnimationTicks.Count > 0
                            ? (skillAnimation
                                   .SegmentOriginalAnimationTicks[0]
                               + 1)
                              / 2
                            : 0,
                        hasSkillAnimation
                            ? skillAnimation.AnimationKey
                            : string.Empty,
                        hasSkillAnimation
                            ? skillAnimation.OriginalAnimationTicks
                            : 0),
                timedBlinkEffect:
                    source.timedBlinkDistanceCentimetres == 0
                    ? null
                    : new TimedBlinkEffectDefinition(
                        source.timedBlinkDistanceCentimetres,
                        hasSkillAnimation
                        && skillAnimation
                            .SegmentOriginalAnimationTicks.Count > 0
                            ? (skillAnimation
                                   .SegmentOriginalAnimationTicks[0]
                               + 1)
                               / 2
                            : 0),
                proximityEntryDamageEffect:
                    source.proximityEntryRadiusCentimetres == 0
                    ? null
                    : new ProximityEntryDamageEffectDefinition(
                        source.proximityEntryRadiusCentimetres,
                        ParseEnum<DamageType>(
                            source.proximityEntryDamageType),
                        source
                            .proximityEntryAttackMultiplierPermille,
                        source
                            .proximityEntryGroundTargetsOnly),
                requiredStatusTagCombatModifier:
                    string.IsNullOrWhiteSpace(
                        source.requiredStatusTag)
                    ? null
                    : new RequiredStatusTagCombatModifierDefinition(
                        source.requiredStatusTag,
                        source
                            .requiredStatusTagAttackMultiplierPermille,
                        source
                            .requiredStatusTagMoveSpeedMultiplierPermille));
        }

        private static T ParseEnum<T>(string value) where T : struct => Enum.TryParse(value, true, out T parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed : (T)Enum.ToObject(typeof(T), -1);
        private static AbilityCatalogLoadResult Failure(string code, string message) => new AbilityCatalogLoadResult(null, new[] { new ValidationError(code, message) });

        [Serializable] private sealed class AbilityCatalogDto { public string schemaVersion; public string catalogId; public AbilityDto[] abilities; }
        [Serializable] private sealed class AbilityDto
        {
            public string abilityId; public string displayNameZhHans; public string descriptionZhHans; public string activationKind; public string silencePolicy; public int initialSkillPoints; public int requiredSkillPoints; public string skillPointGeneration;
            public string persistentPresentationStateTag;
            public string summonTypeId; public int count; public int sideLengthCentimetres; public bool inheritPathFromCaster;
            public string unitTrait; public int onHitDefenseReductionPerStack;
            public int blockCapacityAdditive; public int magicResistanceAdditive; public int attackSpeedAdditive; public int physicalDamageTakenPermille; public int magicDamageTakenPermille;
            public int hitPointsPerSecond; public int lifetimeTicks;
            public string onDamageReactionDamageType; public int onDamageReactionDamageAmount;
            public int unblockedPhysicalDamageTakenPermille; public int unblockedMagicDamageTakenPermille;
            public int attackSequenceFirstEnhancedAttackOrdinal; public int attackSequenceRepeatInterval; public int attackSequenceAttackMultiplierPermille;
            public int attackCountTransitionBeforeAttackOrdinal; public int attackCountLockedAttackSpeedAdditive; public int attackCountLockedDefenseAdditive; public int attackCountUnlockedAttackMultiplierPermille; public int attackCountUnlockedMagicResistanceAdditive; public int attackCountUnlockedHitPointsPerSecond; public int attackCountUnlockedTargetDefenseMultiplierPermille; public bool attackCountReleasesAlliedStates;
            public DeathSpawnOptionDto[] deathSpawnOptions; public int deathSpawnCount; public int deathSpawnDelayTicks; public int deathSpawnSideLengthCentimetres; public bool deathSpawnSnapToNearestPassableCell; public int deathSpawnSummonedMoveSpeedMultiplierPermille;
            public string auraTargetSide; public bool auraIsGlobal; public int auraRadiusCentimetres; public bool auraExcludeSource; public bool auraNonStackingByAbilityId; public int auraAttackMultiplierPermille; public int auraDefenseAdditive; public int auraMagicResistanceAdditive; public int auraAttackSpeedMultiplierPermille; public int auraMoveSpeedMultiplierPermille; public int auraHitPointsPerSecond; public string auraGrantedStatusTag;
            public bool blockedCounterpartNonStackingByAbilityId; public int blockedCounterpartAttackSpeedMultiplierPermille;
            public int nearbySameTypeRadiusCentimetres; public int nearbySameTypeDefenseAdditive;
            public int evasionPhysicalChancePermille; public int evasionMagicChancePermille;
            public string deathAreaDamageType; public int deathAreaAttackMultiplierPermille; public int deathAreaRadiusCentimetres; public int deathAreaDelayTicks;
            public string attackAreaShape; public int attackAreaFirstAttackOrdinal; public int attackAreaRepeatInterval; public string attackAreaDamageType; public int attackAreaAttackMultiplierPermille; public int attackAreaRadiusCentimetres;
            public int unblockedAttackChargeCheckIntervalTicks; public int unblockedAttackChargeAttackAdditivePerStack; public int unblockedAttackChargeMaxStacks;
            public int healthThresholdFullHealHitPointsPermille; public bool healthThresholdFullHealInclusive;
            public string requiredStatusTag; public int requiredStatusTagAttackMultiplierPermille; public int requiredStatusTagMoveSpeedMultiplierPermille;
            public int targetRangeCentimetres; public int areaRadiusCentimetres; public string areaDamageType; public int areaAttackMultiplierPermille; public bool groundTargetsOnly;
            public int attackDashFirstTriggerOrdinal; public int attackDashRepeatInterval; public int attackDashDistanceCentimetres; public int attackDashUnblockableDurationTicks;
            public int timedBlinkDistanceCentimetres;
            public int proximityEntryRadiusCentimetres; public string proximityEntryDamageType; public int proximityEntryAttackMultiplierPermille; public bool proximityEntryGroundTargetsOnly;
            public string triggeredSpawnKind; public int triggeredSpawnFirstTriggerOrdinal; public int triggeredSpawnRepeatInterval; public string triggeredSpawnSummonTypeId; public int triggeredSpawnSideLengthCentimetres; public int triggeredSpawnMaxActiveSameType;
            public int healthThresholdCombatHitPointsPermille; public bool healthThresholdCombatInclusive; public bool healthThresholdCombatTriggerOnce; public int healthThresholdCombatDurationTicks; public int healthThresholdCombatAttackMultiplierPermille; public int healthThresholdCombatDefenseMultiplierPermille; public int healthThresholdCombatBlockCapacityAdditive; public int healthThresholdCombatAttackSpeedAdditive; public int healthThresholdCombatMoveSpeedMultiplierPermille; public bool healthThresholdCombatMakesUnblockable;
            public int healthThresholdAdjacentSpawnHitPointsPermille; public bool healthThresholdAdjacentSpawnInclusive; public string healthThresholdAdjacentSpawnTypeId;
        }
        [Serializable] private sealed class DeathSpawnOptionDto { public string summonTypeId; public int weight; }
    }
}
