using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Battle.Core
{
    public enum AbilityActivationKind { Timed, Passive }
    public enum SilencePolicy { Unaffected }
    public enum SkillPointGeneration { Automatic, None }
    public enum UnitTraitEffectKind { Untargetable }

    public sealed class UnitTraitEffectDefinition
    {
        public UnitTraitEffectDefinition(UnitTraitEffectKind kind)
        {
            Kind = kind;
        }

        public UnitTraitEffectKind Kind { get; }
    }

    public sealed class PassiveCombatModifierDefinition
    {
        public const int NeutralDamageTakenPermille = 1000;

        public PassiveCombatModifierDefinition(
            int blockCapacityAdditive,
            int magicResistanceAdditive,
            int attackSpeedAdditive,
            int physicalDamageTakenPermille,
            int magicDamageTakenPermille)
        {
            BlockCapacityAdditive = blockCapacityAdditive;
            MagicResistanceAdditive = magicResistanceAdditive;
            AttackSpeedAdditive = attackSpeedAdditive;
            PhysicalDamageTakenPermille = physicalDamageTakenPermille;
            MagicDamageTakenPermille = magicDamageTakenPermille;
        }

        public int BlockCapacityAdditive { get; }
        public int MagicResistanceAdditive { get; }
        public int AttackSpeedAdditive { get; }
        public int PhysicalDamageTakenPermille { get; }
        public int MagicDamageTakenPermille { get; }
        public bool IsNeutral =>
            BlockCapacityAdditive == 0
            && MagicResistanceAdditive == 0
            && AttackSpeedAdditive == 0
            && PhysicalDamageTakenPermille
            == NeutralDamageTakenPermille
            && MagicDamageTakenPermille
            == NeutralDamageTakenPermille;
    }

    public sealed class PassiveLifecycleEffectDefinition
    {
        public PassiveLifecycleEffectDefinition(
            int hitPointsPerSecond,
            int lifetimeTicks)
        {
            HitPointsPerSecond = hitPointsPerSecond;
            LifetimeTicks = lifetimeTicks;
        }

        public int HitPointsPerSecond { get; }
        public int LifetimeTicks { get; }
        public bool IsNeutral =>
            HitPointsPerSecond == 0
            && LifetimeTicks == 0;
    }

    public sealed class OnDamageReactionEffectDefinition
    {
        public OnDamageReactionEffectDefinition(
            DamageType damageType,
            int damageAmount)
        {
            DamageType = damageType;
            DamageAmount = damageAmount;
        }

        public DamageType DamageType { get; }
        public int DamageAmount { get; }
    }

    public sealed class UnblockedDamageTakenModifierDefinition
    {
        public const int NeutralDamageTakenPermille = 1000;

        public UnblockedDamageTakenModifierDefinition(
            int physicalDamageTakenPermille,
            int magicDamageTakenPermille)
        {
            PhysicalDamageTakenPermille =
                physicalDamageTakenPermille;
            MagicDamageTakenPermille =
                magicDamageTakenPermille;
        }

        public int PhysicalDamageTakenPermille { get; }
        public int MagicDamageTakenPermille { get; }
        public bool IsNeutral =>
            PhysicalDamageTakenPermille
            == NeutralDamageTakenPermille
            && MagicDamageTakenPermille
            == NeutralDamageTakenPermille;
    }

    public sealed class HealthThresholdCombatModifierDefinition
    {
        public const int NeutralMultiplierPermille = 1000;

        public HealthThresholdCombatModifierDefinition(
            int thresholdHitPointsPermille,
            bool inclusiveThreshold,
            bool triggerOnce,
            int durationTicks,
            int attackMultiplierPermille,
            int defenseMultiplierPermille,
            int blockCapacityAdditive,
            int attackSpeedAdditive,
            int moveSpeedMultiplierPermille)
        {
            ThresholdHitPointsPermille =
                thresholdHitPointsPermille;
            InclusiveThreshold = inclusiveThreshold;
            TriggerOnce = triggerOnce;
            DurationTicks = durationTicks;
            AttackMultiplierPermille =
                attackMultiplierPermille;
            DefenseMultiplierPermille =
                defenseMultiplierPermille;
            BlockCapacityAdditive =
                blockCapacityAdditive;
            AttackSpeedAdditive = attackSpeedAdditive;
            MoveSpeedMultiplierPermille =
                moveSpeedMultiplierPermille;
        }

        public int ThresholdHitPointsPermille { get; }
        public bool InclusiveThreshold { get; }
        public bool TriggerOnce { get; }
        public int DurationTicks { get; }
        public int AttackMultiplierPermille { get; }
        public int DefenseMultiplierPermille { get; }
        public int BlockCapacityAdditive { get; }
        public int AttackSpeedAdditive { get; }
        public int MoveSpeedMultiplierPermille { get; }
        public bool IsNeutral =>
            AttackMultiplierPermille
            == NeutralMultiplierPermille
            && DefenseMultiplierPermille
            == NeutralMultiplierPermille
            && BlockCapacityAdditive == 0
            && AttackSpeedAdditive == 0
            && MoveSpeedMultiplierPermille
            == NeutralMultiplierPermille;
    }

    public sealed class AttackSequenceModifierDefinition
    {
        public const int NeutralAttackMultiplierPermille = 1000;

        public AttackSequenceModifierDefinition(
            int firstEnhancedAttackOrdinal,
            int repeatInterval,
            int attackMultiplierPermille)
        {
            FirstEnhancedAttackOrdinal =
                firstEnhancedAttackOrdinal;
            RepeatInterval = repeatInterval;
            AttackMultiplierPermille =
                attackMultiplierPermille;
        }

        public int FirstEnhancedAttackOrdinal { get; }
        public int RepeatInterval { get; }
        public int AttackMultiplierPermille { get; }

        public bool IsEnhancedAttack(int attackOrdinal)
        {
            if (attackOrdinal < FirstEnhancedAttackOrdinal)
                return false;
            if (attackOrdinal == FirstEnhancedAttackOrdinal)
                return true;
            return RepeatInterval > 0
                && (attackOrdinal - FirstEnhancedAttackOrdinal)
                % RepeatInterval == 0;
        }
    }

    public sealed class AttackCountStateModifierDefinition
    {
        public const int NeutralMultiplierPermille = 1000;

        public AttackCountStateModifierDefinition(
            int transitionBeforeAttackOrdinal,
            int lockedAttackSpeedAdditive,
            int lockedDefenseAdditive,
            int unlockedAttackMultiplierPermille,
            int unlockedMagicResistanceAdditive,
            int unlockedHitPointsPerSecond,
            int unlockedTargetDefenseMultiplierPermille)
        {
            TransitionBeforeAttackOrdinal =
                transitionBeforeAttackOrdinal;
            LockedAttackSpeedAdditive =
                lockedAttackSpeedAdditive;
            LockedDefenseAdditive = lockedDefenseAdditive;
            UnlockedAttackMultiplierPermille =
                unlockedAttackMultiplierPermille;
            UnlockedMagicResistanceAdditive =
                unlockedMagicResistanceAdditive;
            UnlockedHitPointsPerSecond =
                unlockedHitPointsPerSecond;
            UnlockedTargetDefenseMultiplierPermille =
                unlockedTargetDefenseMultiplierPermille;
        }

        public int TransitionBeforeAttackOrdinal { get; }
        public int LockedAttackSpeedAdditive { get; }
        public int LockedDefenseAdditive { get; }
        public int UnlockedAttackMultiplierPermille { get; }
        public int UnlockedMagicResistanceAdditive { get; }
        public int UnlockedHitPointsPerSecond { get; }
        public int UnlockedTargetDefenseMultiplierPermille
        {
            get;
        }
        public bool IsNeutral =>
            LockedAttackSpeedAdditive == 0
            && LockedDefenseAdditive == 0
            && UnlockedAttackMultiplierPermille
                == NeutralMultiplierPermille
            && UnlockedMagicResistanceAdditive == 0
            && UnlockedHitPointsPerSecond == 0
            && UnlockedTargetDefenseMultiplierPermille
                == NeutralMultiplierPermille;
    }

    public sealed class DeathSpawnOptionDefinition
    {
        public DeathSpawnOptionDefinition(
            string summonTypeId,
            int weight)
        {
            SummonTypeId = summonTypeId;
            Weight = weight;
        }

        public string SummonTypeId { get; }
        public int Weight { get; }
    }

    public sealed class DeathSpawnEffectDefinition
    {
        public const int NeutralMoveSpeedMultiplierPermille = 1000;

        public DeathSpawnEffectDefinition(
            IEnumerable<DeathSpawnOptionDefinition> options,
            int count,
            int delayTicks,
            int sideLengthCentimetres,
            bool snapToNearestPassableCell,
            int summonedMoveSpeedMultiplierPermille)
        {
            Options =
                new ReadOnlyCollection<DeathSpawnOptionDefinition>(
                    (options
                     ?? Enumerable.Empty<DeathSpawnOptionDefinition>())
                    .ToArray());
            Count = count;
            DelayTicks = delayTicks;
            SideLengthCentimetres = sideLengthCentimetres;
            SnapToNearestPassableCell =
                snapToNearestPassableCell;
            SummonedMoveSpeedMultiplierPermille =
                summonedMoveSpeedMultiplierPermille;
        }

        public IReadOnlyList<DeathSpawnOptionDefinition> Options
        {
            get;
        }
        public int Count { get; }
        public int DelayTicks { get; }
        public int SideLengthCentimetres { get; }
        public bool SnapToNearestPassableCell { get; }
        public int SummonedMoveSpeedMultiplierPermille { get; }
    }

    public enum AuraTargetSide
    {
        Allies,
        Enemies
    }

    public interface IExternalCombatModifierDefinition
    {
        bool NonStackingByAbilityId { get; }
        int AttackMultiplierPermille { get; }
        int DefenseAdditive { get; }
        int MagicResistanceAdditive { get; }
        int AttackSpeedMultiplierPermille { get; }
        int MoveSpeedMultiplierPermille { get; }
        int HitPointsPerSecond { get; }
    }

    public sealed class AuraCombatModifierDefinition
        : IExternalCombatModifierDefinition
    {
        public const int NeutralMultiplierPermille = 1000;

        public AuraCombatModifierDefinition(
            AuraTargetSide targetSide,
            bool isGlobal,
            int radiusCentimetres,
            bool excludeSource,
            bool nonStackingByAbilityId,
            int attackMultiplierPermille,
            int defenseAdditive,
            int magicResistanceAdditive,
            int attackSpeedMultiplierPermille,
            int moveSpeedMultiplierPermille,
            int hitPointsPerSecond)
        {
            TargetSide = targetSide;
            IsGlobal = isGlobal;
            RadiusCentimetres = radiusCentimetres;
            ExcludeSource = excludeSource;
            NonStackingByAbilityId =
                nonStackingByAbilityId;
            AttackMultiplierPermille =
                attackMultiplierPermille;
            DefenseAdditive = defenseAdditive;
            MagicResistanceAdditive =
                magicResistanceAdditive;
            AttackSpeedMultiplierPermille =
                attackSpeedMultiplierPermille;
            MoveSpeedMultiplierPermille =
                moveSpeedMultiplierPermille;
            HitPointsPerSecond = hitPointsPerSecond;
        }

        public AuraTargetSide TargetSide { get; }
        public bool IsGlobal { get; }
        public int RadiusCentimetres { get; }
        public bool ExcludeSource { get; }
        public bool NonStackingByAbilityId { get; }
        public int AttackMultiplierPermille { get; }
        public int DefenseAdditive { get; }
        public int MagicResistanceAdditive { get; }
        public int AttackSpeedMultiplierPermille { get; }
        public int MoveSpeedMultiplierPermille { get; }
        public int HitPointsPerSecond { get; }
        public bool IsNeutral =>
            AttackMultiplierPermille
                == NeutralMultiplierPermille
            && DefenseAdditive == 0
            && MagicResistanceAdditive == 0
            && AttackSpeedMultiplierPermille
                == NeutralMultiplierPermille
            && MoveSpeedMultiplierPermille
                == NeutralMultiplierPermille
            && HitPointsPerSecond == 0;
    }

    public sealed class BlockedCounterpartCombatModifierDefinition
        : IExternalCombatModifierDefinition
    {
        public BlockedCounterpartCombatModifierDefinition(
            bool nonStackingByAbilityId,
            int attackSpeedMultiplierPermille)
        {
            NonStackingByAbilityId =
                nonStackingByAbilityId;
            AttackSpeedMultiplierPermille =
                attackSpeedMultiplierPermille;
        }

        public bool NonStackingByAbilityId { get; }
        public int AttackMultiplierPermille =>
            AuraCombatModifierDefinition
                .NeutralMultiplierPermille;
        public int DefenseAdditive => 0;
        public int MagicResistanceAdditive => 0;
        public int AttackSpeedMultiplierPermille { get; }
        public int MoveSpeedMultiplierPermille =>
            AuraCombatModifierDefinition
                .NeutralMultiplierPermille;
        public int HitPointsPerSecond => 0;
    }

    public sealed class NearbySameTypeSelfModifierDefinition
        : IExternalCombatModifierDefinition
    {
        public NearbySameTypeSelfModifierDefinition(
            int radiusCentimetres,
            int defenseAdditivePerUnit)
        {
            RadiusCentimetres = radiusCentimetres;
            DefenseAdditive = defenseAdditivePerUnit;
        }

        public int RadiusCentimetres { get; }
        public bool NonStackingByAbilityId => false;
        public int AttackMultiplierPermille =>
            AuraCombatModifierDefinition
                .NeutralMultiplierPermille;
        public int DefenseAdditive { get; }
        public int MagicResistanceAdditive => 0;
        public int AttackSpeedMultiplierPermille =>
            AuraCombatModifierDefinition
                .NeutralMultiplierPermille;
        public int MoveSpeedMultiplierPermille =>
            AuraCombatModifierDefinition
                .NeutralMultiplierPermille;
        public int HitPointsPerSecond => 0;
    }

    public sealed class EvasionModifierDefinition
    {
        public EvasionModifierDefinition(
            int physicalChancePermille,
            int magicChancePermille)
        {
            PhysicalChancePermille = physicalChancePermille;
            MagicChancePermille = magicChancePermille;
        }

        public int PhysicalChancePermille { get; }
        public int MagicChancePermille { get; }
        public bool IsNeutral =>
            PhysicalChancePermille == 0
            && MagicChancePermille == 0;
    }

    public sealed class DeathAreaDamageEffectDefinition
    {
        public DeathAreaDamageEffectDefinition(
            DamageType damageType,
            int attackMultiplierPermille,
            int radiusCentimetres,
            int delayTicks)
        {
            DamageType = damageType;
            AttackMultiplierPermille = attackMultiplierPermille;
            RadiusCentimetres = radiusCentimetres;
            DelayTicks = delayTicks;
        }

        public DamageType DamageType { get; }
        public int AttackMultiplierPermille { get; }
        public int RadiusCentimetres { get; }
        public int DelayTicks { get; }
    }

    public sealed class SummonEffectDefinition
    {
        public SummonEffectDefinition(string summonTypeId, int count, int sideLengthCentimetres, bool inheritPathFromCaster)
        {
            SummonTypeId = summonTypeId;
            Count = count;
            SideLengthCentimetres = sideLengthCentimetres;
            InheritPathFromCaster = inheritPathFromCaster;
        }

        public string SummonTypeId { get; }
        public int Count { get; }
        public int SideLengthCentimetres { get; }
        public bool InheritPathFromCaster { get; }
    }

    public sealed class AbilityDefinition
    {
        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                null,
                activationKind == AbilityActivationKind.Timed ? "skill" : string.Empty)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                activationKind == AbilityActivationKind.Timed ? "skill" : string.Empty)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, string animationKey)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                animationKey,
                0)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                healthThresholdCombatModifier,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, UnblockedDamageTakenModifierDefinition unblockedDamageTakenModifier, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                healthThresholdCombatModifier,
                unblockedDamageTakenModifier,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, UnblockedDamageTakenModifierDefinition unblockedDamageTakenModifier, AttackSequenceModifierDefinition attackSequenceModifier, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                healthThresholdCombatModifier,
                unblockedDamageTakenModifier,
                attackSequenceModifier,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, UnblockedDamageTakenModifierDefinition unblockedDamageTakenModifier, AttackSequenceModifierDefinition attackSequenceModifier, AttackCountStateModifierDefinition attackCountStateModifier, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                healthThresholdCombatModifier,
                unblockedDamageTakenModifier,
                attackSequenceModifier,
                attackCountStateModifier,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, UnblockedDamageTakenModifierDefinition unblockedDamageTakenModifier, AttackSequenceModifierDefinition attackSequenceModifier, AttackCountStateModifierDefinition attackCountStateModifier, DeathSpawnEffectDefinition deathSpawnEffect, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                healthThresholdCombatModifier,
                unblockedDamageTakenModifier,
                attackSequenceModifier,
                attackCountStateModifier,
                deathSpawnEffect,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, UnblockedDamageTakenModifierDefinition unblockedDamageTakenModifier, AttackSequenceModifierDefinition attackSequenceModifier, AttackCountStateModifierDefinition attackCountStateModifier, DeathSpawnEffectDefinition deathSpawnEffect, AuraCombatModifierDefinition auraCombatModifier, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                healthThresholdCombatModifier,
                unblockedDamageTakenModifier,
                attackSequenceModifier,
                attackCountStateModifier,
                deathSpawnEffect,
                auraCombatModifier,
                null,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, UnblockedDamageTakenModifierDefinition unblockedDamageTakenModifier, AttackSequenceModifierDefinition attackSequenceModifier, AttackCountStateModifierDefinition attackCountStateModifier, DeathSpawnEffectDefinition deathSpawnEffect, AuraCombatModifierDefinition auraCombatModifier, BlockedCounterpartCombatModifierDefinition blockedCounterpartCombatModifier, NearbySameTypeSelfModifierDefinition nearbySameTypeSelfModifier, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                healthThresholdCombatModifier,
                unblockedDamageTakenModifier,
                attackSequenceModifier,
                attackCountStateModifier,
                deathSpawnEffect,
                auraCombatModifier,
                blockedCounterpartCombatModifier,
                nearbySameTypeSelfModifier,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, UnblockedDamageTakenModifierDefinition unblockedDamageTakenModifier, AttackSequenceModifierDefinition attackSequenceModifier, AttackCountStateModifierDefinition attackCountStateModifier, DeathSpawnEffectDefinition deathSpawnEffect, AuraCombatModifierDefinition auraCombatModifier, BlockedCounterpartCombatModifierDefinition blockedCounterpartCombatModifier, NearbySameTypeSelfModifierDefinition nearbySameTypeSelfModifier, EvasionModifierDefinition evasionModifier, string animationKey, int skillAnimationOriginalDurationTicks)
            : this(
                abilityId,
                displayNameZhHans,
                descriptionZhHans,
                activationKind,
                silencePolicy,
                initialSkillPoints,
                requiredSkillPoints,
                skillPointGeneration,
                summonEffect,
                unitTraitEffect,
                passiveCombatModifier,
                passiveLifecycleEffect,
                onDamageReactionEffect,
                healthThresholdCombatModifier,
                unblockedDamageTakenModifier,
                attackSequenceModifier,
                attackCountStateModifier,
                deathSpawnEffect,
                auraCombatModifier,
                blockedCounterpartCombatModifier,
                nearbySameTypeSelfModifier,
                evasionModifier,
                null,
                animationKey,
                skillAnimationOriginalDurationTicks)
        {
        }

        public AbilityDefinition(string abilityId, string displayNameZhHans, string descriptionZhHans, AbilityActivationKind activationKind, SilencePolicy silencePolicy, int initialSkillPoints, int requiredSkillPoints, SkillPointGeneration skillPointGeneration, SummonEffectDefinition summonEffect, UnitTraitEffectDefinition unitTraitEffect, PassiveCombatModifierDefinition passiveCombatModifier, PassiveLifecycleEffectDefinition passiveLifecycleEffect, OnDamageReactionEffectDefinition onDamageReactionEffect, HealthThresholdCombatModifierDefinition healthThresholdCombatModifier, UnblockedDamageTakenModifierDefinition unblockedDamageTakenModifier, AttackSequenceModifierDefinition attackSequenceModifier, AttackCountStateModifierDefinition attackCountStateModifier, DeathSpawnEffectDefinition deathSpawnEffect, AuraCombatModifierDefinition auraCombatModifier, BlockedCounterpartCombatModifierDefinition blockedCounterpartCombatModifier, NearbySameTypeSelfModifierDefinition nearbySameTypeSelfModifier, EvasionModifierDefinition evasionModifier, DeathAreaDamageEffectDefinition deathAreaDamageEffect, string animationKey, int skillAnimationOriginalDurationTicks)
        {
            AbilityId = abilityId;
            DisplayNameZhHans = displayNameZhHans ?? string.Empty;
            DescriptionZhHans = descriptionZhHans ?? string.Empty;
            ActivationKind = activationKind;
            SilencePolicy = silencePolicy;
            InitialSkillPoints = initialSkillPoints;
            RequiredSkillPoints = requiredSkillPoints;
            SkillPointGeneration = skillPointGeneration;
            SummonEffect = summonEffect;
            UnitTraitEffect = unitTraitEffect;
            PassiveCombatModifier = passiveCombatModifier;
            PassiveLifecycleEffect = passiveLifecycleEffect;
            OnDamageReactionEffect = onDamageReactionEffect;
            HealthThresholdCombatModifier =
                healthThresholdCombatModifier;
            UnblockedDamageTakenModifier =
                unblockedDamageTakenModifier;
            AttackSequenceModifier = attackSequenceModifier;
            AttackCountStateModifier =
                attackCountStateModifier;
            DeathSpawnEffect = deathSpawnEffect;
            AuraCombatModifier = auraCombatModifier;
            BlockedCounterpartCombatModifier =
                blockedCounterpartCombatModifier;
            NearbySameTypeSelfModifier =
                nearbySameTypeSelfModifier;
            EvasionModifier = evasionModifier;
            DeathAreaDamageEffect = deathAreaDamageEffect;
            AnimationKey = animationKey ?? string.Empty;
            SkillAnimationOriginalDurationTicks = skillAnimationOriginalDurationTicks;
        }

        public string AbilityId { get; }
        public string DisplayNameZhHans { get; }
        public string DescriptionZhHans { get; }
        public AbilityActivationKind ActivationKind { get; }
        public SilencePolicy SilencePolicy { get; }
        public int InitialSkillPoints { get; }
        public int RequiredSkillPoints { get; }
        public SkillPointGeneration SkillPointGeneration { get; }
        public SummonEffectDefinition SummonEffect { get; }
        public UnitTraitEffectDefinition UnitTraitEffect { get; }
        public PassiveCombatModifierDefinition PassiveCombatModifier { get; }
        public PassiveLifecycleEffectDefinition PassiveLifecycleEffect { get; }
        public OnDamageReactionEffectDefinition OnDamageReactionEffect { get; }
        public HealthThresholdCombatModifierDefinition
            HealthThresholdCombatModifier { get; }
        public UnblockedDamageTakenModifierDefinition
            UnblockedDamageTakenModifier { get; }
        public AttackSequenceModifierDefinition
            AttackSequenceModifier { get; }
        public AttackCountStateModifierDefinition
            AttackCountStateModifier { get; }
        public DeathSpawnEffectDefinition DeathSpawnEffect { get; }
        public AuraCombatModifierDefinition AuraCombatModifier
        {
            get;
        }
        public BlockedCounterpartCombatModifierDefinition
            BlockedCounterpartCombatModifier { get; }
        public NearbySameTypeSelfModifierDefinition
            NearbySameTypeSelfModifier { get; }
        public EvasionModifierDefinition EvasionModifier { get; }
        public DeathAreaDamageEffectDefinition
            DeathAreaDamageEffect { get; }
        public string AnimationKey { get; }
        public int SkillAnimationOriginalDurationTicks { get; }
        public int SkillAnimationEffectiveDurationTicks =>
            (SkillAnimationOriginalDurationTicks + 1) / 2;
    }
}
