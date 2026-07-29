using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
using UnityEngine;

namespace ArknoNights.Battle.Infrastructure
{
    public sealed class SkillAnimationCatalogBinding
    {
        internal SkillAnimationCatalogBinding(
            string typeId,
            string abilityId,
            string animationKey,
            string animationName,
            int originalAnimationTicks,
            IEnumerable<int> segmentOriginalAnimationTicks,
            string presentationStateTag = "",
            string stateIdleAnimation = "",
            string stateMoveAnimation = "",
            string stateAttackAnimation = "",
            string stateDeathAnimation = "")
        {
            TypeId = typeId;
            AbilityId = abilityId;
            AnimationKey = animationKey ?? string.Empty;
            AnimationName = animationName ?? string.Empty;
            OriginalAnimationTicks = originalAnimationTicks;
            SegmentOriginalAnimationTicks =
                new ReadOnlyCollection<int>(
                    (segmentOriginalAnimationTicks
                     ?? (originalAnimationTicks > 0
                         ? new[] { originalAnimationTicks }
                         : Array.Empty<int>()))
                    .ToArray());
            PresentationStateTag =
                presentationStateTag ?? string.Empty;
            StateIdleAnimation =
                stateIdleAnimation ?? string.Empty;
            StateMoveAnimation =
                stateMoveAnimation ?? string.Empty;
            StateAttackAnimation =
                stateAttackAnimation ?? string.Empty;
            StateDeathAnimation =
                stateDeathAnimation ?? string.Empty;
        }

        public string TypeId { get; }
        public string AbilityId { get; }
        public string AnimationKey { get; }
        public string AnimationName { get; }
        public int OriginalAnimationTicks { get; }
        public IReadOnlyList<int> SegmentOriginalAnimationTicks
        {
            get;
        }
        public string PresentationStateTag { get; }
        public string StateIdleAnimation { get; }
        public string StateMoveAnimation { get; }
        public string StateAttackAnimation { get; }
        public string StateDeathAnimation { get; }
        public bool HasSkillAnimation =>
            !string.IsNullOrWhiteSpace(AnimationKey);
        public bool HasPresentationState =>
            !string.IsNullOrWhiteSpace(PresentationStateTag);
    }

    public sealed class SkillAnimationCatalog
    {
        private readonly Dictionary<string, SkillAnimationCatalogBinding> byAbilityId;

        internal SkillAnimationCatalog(
            string schemaVersion,
            string catalogId,
            IEnumerable<SkillAnimationCatalogBinding> bindings)
        {
            SchemaVersion = schemaVersion;
            CatalogId = catalogId;
            Bindings = new ReadOnlyCollection<SkillAnimationCatalogBinding>(
                bindings.OrderBy(item => item.AbilityId, StringComparer.Ordinal).ToArray());
            byAbilityId = Bindings.ToDictionary(
                item => item.AbilityId,
                StringComparer.Ordinal);
        }

        public string SchemaVersion { get; }
        public string CatalogId { get; }
        public IReadOnlyList<SkillAnimationCatalogBinding> Bindings { get; }
        public bool TryGetAbility(
            string abilityId,
            out SkillAnimationCatalogBinding binding) =>
            byAbilityId.TryGetValue(abilityId ?? string.Empty, out binding);
    }

    public sealed class SkillAnimationCatalogLoadResult
    {
        internal SkillAnimationCatalogLoadResult(
            SkillAnimationCatalog catalog,
            IReadOnlyList<ValidationError> errors)
        {
            Catalog = catalog;
            Errors = errors;
        }

        public bool Success => Catalog != null && Errors.Count == 0;
        public SkillAnimationCatalog Catalog { get; }
        public IReadOnlyList<ValidationError> Errors { get; }
    }

    public static class SkillAnimationCatalogLoader
    {
        public const string DefaultResourcePath =
            "BattleData/skill-animation-catalog-v1";
        public const string SchemaVersion = "skill-animation-catalog-v1";

        public static SkillAnimationCatalogLoadResult LoadFromResources(
            string resourcePath = DefaultResourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            return asset == null
                ? Failure(
                    "skillAnimation.resource.missing",
                    "skillAnimationCatalogResource=" + resourcePath)
                : LoadFromJson(asset.text);
        }

        public static SkillAnimationCatalogLoadResult LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Failure(
                    "skillAnimation.json.empty",
                    "Skill animation catalog JSON is empty.");
            SkillAnimationCatalogDto dto;
            try
            {
                dto = JsonUtility.FromJson<SkillAnimationCatalogDto>(json);
            }
            catch (Exception exception)
            {
                return Failure(
                    "skillAnimation.json.invalid",
                    exception.Message);
            }

            var errors = new List<ValidationError>();
            if (dto == null
                || !string.Equals(
                    dto.schemaVersion,
                    SchemaVersion,
                    StringComparison.Ordinal))
                errors.Add(new ValidationError(
                    "skillAnimation.schema.unsupported",
                    "Unsupported skill animation schema."));
            if (dto == null || string.IsNullOrWhiteSpace(dto.catalogId))
                errors.Add(new ValidationError(
                    "skillAnimation.catalogId.invalid",
                    "Skill animation catalog ID is required."));

            var bindings = new List<SkillAnimationCatalogBinding>();
            var abilityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in dto == null
                         ? Array.Empty<SkillAnimationBindingDto>()
                         : dto.bindings ?? Array.Empty<SkillAnimationBindingDto>())
            {
                var hasSkillAnimation = item != null
                    && !string.IsNullOrWhiteSpace(
                        item.animationKey);
                var hasPresentationState = item != null
                    && !string.IsNullOrWhiteSpace(
                        item.presentationStateTag);
                if (item == null
                    || string.IsNullOrWhiteSpace(item.typeId)
                    || string.IsNullOrWhiteSpace(item.abilityId)
                    || (!hasSkillAnimation
                        && !hasPresentationState)
                    || (hasSkillAnimation
                        && (string.IsNullOrWhiteSpace(
                                item.animationName)
                            || item.originalAnimationTicks <= 0))
                    || (!hasSkillAnimation
                        && (!string.IsNullOrEmpty(
                                item.animationName)
                            || item.originalAnimationTicks != 0))
                    || (hasPresentationState
                        && (string.IsNullOrWhiteSpace(
                                item.stateIdleAnimation)
                            || string.IsNullOrWhiteSpace(
                                item.stateMoveAnimation)
                            || string.IsNullOrWhiteSpace(
                                item.stateAttackAnimation)
                            || string.IsNullOrWhiteSpace(
                                item.stateDeathAnimation)))
                    || (!hasPresentationState
                        && (!string.IsNullOrEmpty(
                                item.stateIdleAnimation)
                            || !string.IsNullOrEmpty(
                                item.stateMoveAnimation)
                            || !string.IsNullOrEmpty(
                                item.stateAttackAnimation)
                            || !string.IsNullOrEmpty(
                                item.stateDeathAnimation))))
                {
                    errors.Add(new ValidationError(
                        "skillAnimation.binding.invalid",
                        "Skill animation binding is incomplete."));
                    continue;
                }
                var animationKeys = hasSkillAnimation
                    ? item.animationKey.Split('|')
                    : Array.Empty<string>();
                var animationNames = hasSkillAnimation
                    ? item.animationName.Split('|')
                    : Array.Empty<string>();
                var segmentTicks =
                    item.segmentOriginalAnimationTicks != null
                    && item.segmentOriginalAnimationTicks.Length > 0
                        ? item.segmentOriginalAnimationTicks
                        : hasSkillAnimation
                            ? new[] { item.originalAnimationTicks }
                            : Array.Empty<int>();
                if (animationKeys.Length != animationNames.Length
                    || animationKeys.Length != segmentTicks.Length
                    || animationKeys.Any(string.IsNullOrWhiteSpace)
                    || animationNames.Any(string.IsNullOrWhiteSpace)
                    || segmentTicks.Any(value => value <= 0))
                {
                    errors.Add(new ValidationError(
                        "skillAnimation.sequence.invalid",
                        "Skill animation sequence is incomplete: "
                        + item.abilityId));
                    continue;
                }
                if (!abilityIds.Add(item.abilityId))
                {
                    errors.Add(new ValidationError(
                        "skillAnimation.abilityId.duplicate",
                        "Duplicate skill animation ability ID: "
                        + item.abilityId));
                    continue;
                }
                bindings.Add(new SkillAnimationCatalogBinding(
                    item.typeId,
                    item.abilityId,
                    item.animationKey,
                    item.animationName,
                    item.originalAnimationTicks,
                    segmentTicks,
                    item.presentationStateTag,
                    item.stateIdleAnimation,
                    item.stateMoveAnimation,
                    item.stateAttackAnimation,
                    item.stateDeathAnimation));
            }

            if (bindings.Count == 0)
                errors.Add(new ValidationError(
                    "skillAnimation.bindings.empty",
                    "Skill animation catalog requires at least one binding."));
            var readOnlyErrors = new ReadOnlyCollection<ValidationError>(errors);
            return errors.Count == 0
                ? new SkillAnimationCatalogLoadResult(
                    new SkillAnimationCatalog(
                        dto.schemaVersion,
                        dto.catalogId,
                        bindings),
                    readOnlyErrors)
                : new SkillAnimationCatalogLoadResult(null, readOnlyErrors);
        }

        private static SkillAnimationCatalogLoadResult Failure(
            string code,
            string message) =>
            new SkillAnimationCatalogLoadResult(
                null,
                new[] { new ValidationError(code, message) });

        [Serializable]
        private sealed class SkillAnimationCatalogDto
        {
            public string schemaVersion;
            public string catalogId;
            public SkillAnimationBindingDto[] bindings;
        }

        [Serializable]
        private sealed class SkillAnimationBindingDto
        {
            public string typeId;
            public string abilityId;
            public string animationKey;
            public string animationName;
            public int originalAnimationTicks;
            public int[] segmentOriginalAnimationTicks;
            public string presentationStateTag;
            public string stateIdleAnimation;
            public string stateMoveAnimation;
            public string stateAttackAnimation;
            public string stateDeathAnimation;
        }
    }
}
