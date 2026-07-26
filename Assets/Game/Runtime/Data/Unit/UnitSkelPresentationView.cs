using ArknoNights.Battle.Presentation;
using Spine.Unity;
using UnityEngine;

/// <summary>Assembly-CSharp bridge that lets the isolated Battle Presentation assembly drive existing Spine unit prototypes.</summary>
[DisallowMultipleComponent]
public sealed class UnitSkelPresentationView : MonoBehaviour, IBattlePresentationView
{
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

    private float playbackSpeed = 1f;
    private bool deathFallbackApplied;

    private void Awake()
    {
        if (!unitSkel) unitSkel = GetComponent<UnitSkelBase>();
        if (!skeletonAnimation) skeletonAnimation = GetComponent<SkeletonAnimation>();
        if (!statusBar) statusBar = GetComponent<UnitWorldStatusBar>();
        transform.rotation = RightFacing;
    }

    /// <summary>Catalog-driven animation names for a real unit view. Empty hit animation keeps the existing warning-only fallback.</summary>
    public void ConfigureAnimations(UnitSkelBase configuredUnitSkel, string move, string attack, string hit, string death)
    {
        unitSkel = configuredUnitSkel ? configuredUnitSkel : unitSkel;
        moveAnimation = move ?? string.Empty;
        attackAnimation = attack ?? string.Empty;
        hitAnimation = hit ?? string.Empty;
        deathAnimation = death ?? string.Empty;
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

    public void PlayIdle()
    {
        if (unitSkel) unitSkel.PlayDefaultPresentationAnimation();
    }

    public void PlayMove() => PlayOrReport(moveAnimation, true, 1f, "move");

    public void PlayAttack(float animationSpeedMultiplier) => PlayOrReport(attackAnimation, false, animationSpeedMultiplier, "attack");

    // Real catalog entries may intentionally omit Hit. Damage remains event-authoritative and this is a no-op presentation fallback.
    public void PlayHit()
    {
        if (string.IsNullOrEmpty(hitAnimation)) return;
        PlayOrReport(hitAnimation, false, 1f, "hit");
    }

    public void PlayDeath()
    {
        if (PlayOrReport(deathAnimation, false, 1f, "death")) return;
        deathFallbackApplied = true;
        gameObject.SetActive(false);
        Debug.LogWarning("[BattlePresentation][death.fallback.hide] Missing death animation; hid the event-confirmed dead unit.", this);
    }

    public void Dispose()
    {
        if (this && gameObject) Destroy(gameObject);
    }

    private int configuredMaximumHitPoints;


    private bool PlayOrReport(string animationName, bool loop, float localSpeed, string action)
    {
        if (deathFallbackApplied || !unitSkel)
        {
            Debug.LogWarning("[BattlePresentation][animation.adapter.missing] Cannot play " + action + " because UnitSkelBase is unavailable.", this);
            return false;
        }

        // SkeletonAnimation.timeScale already receives playbackSpeed in SetPlaybackSpeed.
        // Applying it again here made a 0.5x demo run each clip at 0.25x while events
        // advanced at 0.5x, allowing a Damage/Death event to overtake the Attack clip.
        var success = unitSkel.PlayPresentationAnimation(animationName, loop, Mathf.Max(0f, localSpeed));
        if (!success) Debug.LogWarning("[BattlePresentation][animation.missing] action=" + action + "; animation=" + animationName, this);
        return success;
    }
}
