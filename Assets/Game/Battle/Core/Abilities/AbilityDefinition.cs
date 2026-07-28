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
            AnimationKey = animationKey ?? string.Empty;
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
        public string AnimationKey { get; }
    }
}
