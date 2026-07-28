using System;

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
        public string AnimationKey { get; }
        public int SkillAnimationOriginalDurationTicks { get; }
        public int SkillAnimationEffectiveDurationTicks =>
            (SkillAnimationOriginalDurationTicks + 1) / 2;
    }
}
