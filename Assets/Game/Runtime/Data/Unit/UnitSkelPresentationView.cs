using System;
using System.Collections.Generic;
using ArknoNights.Battle.Presentation;
using Spine.Unity;
using UnityEngine;

/// <summary>Assembly-CSharp bridge that lets the isolated Battle Presentation assembly drive existing Spine unit prototypes.</summary>
[DisallowMultipleComponent]
public sealed class UnitSkelPresentationView : MonoBehaviour, IBattlePresentationView, IBattleSkillPresentationView
{
    private const float DeathBlackeningSeconds = 0.5f;

    private enum DeathPresentationState
    {
        Alive,
        Animation,
        Blackening,
        Hidden
    }

    // The model's authored default is screen-right. Left is exactly a 180-degree local Y rotation.
    private static readonly Quaternion RightFacing = Quaternion.Euler(60f, 0f, 0f);
    private static readonly Quaternion LeftFacing = RightFacing * Quaternion.Euler(0f, 180f, 0f);

    [SerializeField] private UnitSkelBase unitSkel;
    [SerializeField] private SkeletonAnimation skeletonAnimation;
    [SerializeField] private string moveAnimation = "Move";
    [SerializeField] private string attackAnimation = "Attack";
    [SerializeField] private string hitAnimation = "Hit";
    [SerializeField] private string deathAnimation = "Death";
    [SerializeField] private UnitWorldStatusBar statusBar;
    private readonly Dictionary<string, string> skillAnimations =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, StateAnimations> stateAnimations =
        new Dictionary<string, StateAnimations>(StringComparer.Ordinal);
    private string presentationStateTag = string.Empty;

    private float playbackSpeed = 1f;
    private bool deathFallbackApplied;
    private DeathPresentationState deathState;
    private Spine.TrackEntry deathTrackEntry;
    private float deathBlackeningElapsed;
    private Color deathBlackeningStartColor = Color.white;

    public bool HasPendingTerminalPresentation =>
        deathState == DeathPresentationState.Animation ||
        deathState == DeathPresentationState.Blackening;

    private void Awake()
    {
        if (!unitSkel) unitSkel = GetComponent<UnitSkelBase>();
        if (!skeletonAnimation) skeletonAnimation = GetComponent<SkeletonAnimation>();
        if (!statusBar) statusBar = GetComponent<UnitWorldStatusBar>();
        transform.rotation = RightFacing;
    }

    private void Update()
    {
        if (deathState != DeathPresentationState.Blackening) return;

        deathBlackeningElapsed += Time.unscaledDeltaTime;
        var progress = Mathf.Clamp01(deathBlackeningElapsed / DeathBlackeningSeconds);
        var target = new Color(0f, 0f, 0f, deathBlackeningStartColor.a);
        if (skeletonAnimation && skeletonAnimation.Skeleton != null)
            skeletonAnimation.Skeleton.SetColor(Color.Lerp(deathBlackeningStartColor, target, progress));
        if (progress >= 1f) HideDeathView();
    }

    private void OnDestroy()
    {
        deathState = DeathPresentationState.Hidden;
        DetachDeathTrackEntry();
    }

    private void OnDisable()
    {
        if (deathState != DeathPresentationState.Animation &&
            deathState != DeathPresentationState.Blackening)
            return;

        deathState = DeathPresentationState.Hidden;
        DetachDeathTrackEntry();
    }

    /// <summary>Catalog-driven animation names for a real unit view. Empty hit animation keeps the existing warning-only fallback.</summary>
    public void ConfigureAnimations(UnitSkelBase configuredUnitSkel, string move, string attack, string hit, string death)
    {
        ConfigureAnimations(
            configuredUnitSkel,
            move,
            attack,
            hit,
            death,
            null);
    }

    public void ConfigureAnimations(
        UnitSkelBase configuredUnitSkel,
        string move,
        string attack,
        string hit,
        string death,
        IEnumerable<ArknoNights.Battle.Infrastructure.SkillAnimationCatalogBinding> skills)
    {
        unitSkel = configuredUnitSkel ? configuredUnitSkel : unitSkel;
        moveAnimation = move ?? string.Empty;
        attackAnimation = attack ?? string.Empty;
        hitAnimation = hit ?? string.Empty;
        deathAnimation = death ?? string.Empty;
        skillAnimations.Clear();
        stateAnimations.Clear();
        presentationStateTag = string.Empty;
        foreach (var skill in skills
                     ?? Array.Empty<ArknoNights.Battle.Infrastructure.SkillAnimationCatalogBinding>())
        {
            if (skill == null) continue;
            if (skill.HasSkillAnimation)
                skillAnimations[skill.AnimationKey] =
                    skill.AnimationName ?? string.Empty;
            if (skill.HasPresentationState)
                stateAnimations[skill.PresentationStateTag] =
                    new StateAnimations(
                        skill.StateIdleAnimation,
                        skill.StateMoveAnimation,
                        skill.StateAttackAnimation,
                        skill.StateDeathAnimation);
        }
    }

    public void SetWorldPosition(Vector3 position) => transform.position = position;

    public void SetFacing(Vector3 direction)
    {
        // World X is horizontal; world Z is vertical map movement and must not change left/right facing.
        if (Mathf.Approximately(direction.x, 0f)) return;
        transform.rotation = direction.x > 0f ? RightFacing : LeftFacing;
        if (!statusBar) statusBar = GetComponent<UnitWorldStatusBar>();
        if (statusBar) statusBar.RefreshForFacing();
    }

    public void SetPlaybackSpeed(float speed)
    {
        playbackSpeed = Mathf.Max(0f, speed);
        if (unitSkel) unitSkel.SetTotalSpeed(playbackSpeed);
        if (skeletonAnimation) skeletonAnimation.timeScale = playbackSpeed;
    }

    /// <summary>Configures the catalog-owned maximum HP before the playback controller sends Spawn state.</summary>
    public void ConfigureStatusBarMaximumHitPoints(int maxHitPoints)
    {
        configuredMaximumHitPoints = maxHitPoints;
        if (!statusBar) statusBar = GetComponent<UnitWorldStatusBar>();
        if (statusBar) statusBar.SetState(string.Empty, false, maxHitPoints, maxHitPoints, 0);
    }

    public void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield)
        => SetStatusBarState(unitId, isEnemy, configuredMaximumHitPoints, currentHitPoints, currentShield);

    public void SetStatusBarState(string unitId, bool isEnemy, int maxHitPoints, int currentHitPoints, int currentShield)
    {
        if (!statusBar) statusBar = GetComponent<UnitWorldStatusBar>();
        if (statusBar) statusBar.SetState(unitId, isEnemy, maxHitPoints, currentHitPoints, currentShield);
    }

    public void SetPresentationState(string stateTag)
    {
        presentationStateTag = stateTag ?? string.Empty;
    }

    public void PlayIdle()
    {
        var state = ActiveStateAnimations;
        if (state == null)
        {
            if (unitSkel)
                unitSkel.PlayDefaultPresentationAnimation();
            return;
        }
        PlayOrReport(state.Idle, true, 1f, "idle");
    }

    public void PlayMove()
    {
        var state = ActiveStateAnimations;
        PlayOrReport(
            state == null ? moveAnimation : state.Move,
            true,
            1f,
            "move");
    }

    public void PlayAttack(float animationSpeedMultiplier)
    {
        var state = ActiveStateAnimations;
        PlayOrReport(
            state == null ? attackAnimation : state.Attack,
            false,
            animationSpeedMultiplier,
            "attack");
    }

    public void PlaySkill(string animationKey, float animationSpeedMultiplier)
    {
        if (!skillAnimations.TryGetValue(animationKey ?? string.Empty, out var animationName))
        {
            Debug.LogWarning("[BattlePresentation][skill.mapping.missing] key=" + (animationKey ?? string.Empty), this);
            return;
        }
        var animationNames = animationName.Split('|');
        if (animationNames.Length == 1)
        {
            PlayOrReport(
                animationName,
                false,
                animationSpeedMultiplier,
                "skill:" + animationKey);
            return;
        }
        if (!PlayOrReport(
                animationNames[0],
                false,
                animationSpeedMultiplier,
                "skill:" + animationKey))
            return;
        for (var index = 1; index < animationNames.Length; index++)
        {
            var entry = unitSkel.QueueAnimation(
                animationNames[index],
                false);
            if (entry == null)
            {
                Debug.LogWarning(
                    "[BattlePresentation][animation.missing] action=skill:"
                    + animationKey
                    + " name="
                    + animationNames[index],
                    this);
                return;
            }
            entry.TimeScale = Mathf.Max(
                0f,
                animationSpeedMultiplier);
        }
    }

    // Real catalog entries may intentionally omit Hit. Damage remains event-authoritative and this is a no-op presentation fallback.
    public void PlayHit()
    {
        if (string.IsNullOrEmpty(hitAnimation)) return;
        PlayOrReport(hitAnimation, false, 1f, "hit");
    }

    public void PlayDeath()
    {
        if (deathState != DeathPresentationState.Alive) return;
        var state = ActiveStateAnimations;
        if (PlayOrReport(
                state == null ? deathAnimation : state.Death,
                false,
                1f,
                "death",
                out var entry))
        {
            deathState = DeathPresentationState.Animation;
            deathTrackEntry = entry;
            deathTrackEntry.Complete += HandleDeathAnimationComplete;
            deathTrackEntry.Dispose += HandleDeathTrackDisposed;
            return;
        }

        ApplyDeathFallback();
    }

    private void ApplyDeathFallback()
    {
        deathFallbackApplied = true;
        Debug.LogWarning("[BattlePresentation][death.fallback.hide] Missing death animation; hid the event-confirmed dead unit.", this);
        HideDeathView();
    }

    public void Dispose()
    {
        if (!this || !gameObject) return;
        DetachDeathTrackEntry();
        if (gameObject.activeSelf) gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private int configuredMaximumHitPoints;

    private StateAnimations ActiveStateAnimations =>
        stateAnimations.TryGetValue(
            presentationStateTag,
            out var value)
            ? value
            : null;

    private sealed class StateAnimations
    {
        internal StateAnimations(
            string idle,
            string move,
            string attack,
            string death)
        {
            Idle = idle ?? string.Empty;
            Move = move ?? string.Empty;
            Attack = attack ?? string.Empty;
            Death = death ?? string.Empty;
        }

        internal string Idle { get; }
        internal string Move { get; }
        internal string Attack { get; }
        internal string Death { get; }
    }

    private bool PlayOrReport(string animationName, bool loop, float localSpeed, string action)
        => PlayOrReport(animationName, loop, localSpeed, action, out _);

    private bool PlayOrReport(string animationName, bool loop, float localSpeed, string action, out Spine.TrackEntry entry)
    {
        entry = null;
        if (deathFallbackApplied || !unitSkel)
        {
            Debug.LogWarning("[BattlePresentation][animation.adapter.missing] Cannot play " + action + " because UnitSkelBase is unavailable.", this);
            return false;
        }

        // SkeletonAnimation.timeScale already receives playbackSpeed in SetPlaybackSpeed.
        // Applying it again here made a 0.5x demo run each clip at 0.25x while events
        // advanced at 0.5x, allowing a Damage/Death event to overtake the Attack clip.
        var success = unitSkel.TryPlayPresentationAnimation(animationName, loop, Mathf.Max(0f, localSpeed), out entry);
        if (!success) Debug.LogWarning("[BattlePresentation][animation.missing] action=" + action + "; animation=" + animationName, this);
        return success;
    }

    private void HandleDeathAnimationComplete(Spine.TrackEntry entry)
    {
        if (!ReferenceEquals(entry, deathTrackEntry) ||
            deathState != DeathPresentationState.Animation)
            return;

        DetachDeathTrackEntry();
        if (!skeletonAnimation || skeletonAnimation.Skeleton == null)
        {
            Debug.LogWarning("[BattlePresentation][death.fade.skeleton.missing] Cannot blacken a death view without an initialized Skeleton.", this);
            HideDeathView();
            return;
        }

        deathBlackeningStartColor = skeletonAnimation.Skeleton.GetColor();
        deathBlackeningElapsed = 0f;
        deathState = DeathPresentationState.Blackening;
    }

    private void HandleDeathTrackDisposed(Spine.TrackEntry entry)
    {
        if (!ReferenceEquals(entry, deathTrackEntry)) return;
        DetachDeathTrackEntry();
        if (deathState != DeathPresentationState.Animation) return;
        Debug.LogWarning("[BattlePresentation][death.animation.interrupted] Death animation ended before completion; hid the dead unit.", this);
        HideDeathView();
    }

    private void HideDeathView()
    {
        deathState = DeathPresentationState.Hidden;
        DetachDeathTrackEntry();
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void DetachDeathTrackEntry()
    {
        var entry = deathTrackEntry;
        deathTrackEntry = null;
        if (entry == null) return;
        entry.Complete -= HandleDeathAnimationComplete;
        entry.Dispose -= HandleDeathTrackDisposed;
    }
}
