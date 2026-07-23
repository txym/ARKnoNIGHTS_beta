using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using UnityEngine;

/// <summary>Scene shell for the fixed real-data battle Demo. The coordinator owns no scene lookup or Core runner lifecycle.</summary>
[DisallowMultipleComponent]
public sealed class BattleDemoController : MonoBehaviour
{
    private const string DefaultCatalogPath = "BattleData/unit-catalog-v1";
    private const string DefaultBattlePath = "BattleData/task004a-real-1v1";

    [Header("TASK-004A Player-safe input")]
    [SerializeField] private string catalogResourcePath = DefaultCatalogPath;
    [SerializeField] private string battleResourcePath = DefaultBattlePath;
    [Header("Scene references")]
    [SerializeField] private MonoBehaviour presentationFactoryComponent;
    [SerializeField] private BattleDemoUi demoUi;

    private BattleDemoCoordinator coordinator;
    private BattleDemoState lastLoggedState;
    private bool formalRoundMode;

    public BattleDemoCoordinator Coordinator => coordinator;
    public BattleDemoState State => coordinator == null ? BattleDemoState.Error : coordinator.State;
    public bool FormalRoundMode => formalRoundMode;

    private void Awake()
    {
        coordinator = new BattleDemoCoordinator();
        lastLoggedState = coordinator.State;
        if (!TryGetFactory(out _))
        {
            Debug.LogError("[BattleDemo][scene.reference.missing] MappedBattlePresentationViewFactory is not assigned.", this);
            return;
        }
        if (!demoUi)
        {
            Debug.LogError("[BattleDemo][scene.reference.missing] BattleDemoUi is not assigned.", this);
            return;
        }
        demoUi.Bind(this);
        RefreshUi();
    }

    private void OnEnable()
    {
        if (coordinator != null) return;
        coordinator = new BattleDemoCoordinator();
        lastLoggedState = coordinator.State;
        if (demoUi) demoUi.Bind(this);
        RefreshUi();
    }

    private void Update()
    {
        coordinator?.Advance(Time.unscaledDeltaTime);
        LogAndRefreshOnStateChange();
        demoUi?.Refresh();
    }

    private void OnDisable() => DisposeCoordinator();
    private void OnDestroy() => DisposeCoordinator();

    public void StartOrContinue()
    {
        if (formalRoundMode) { Debug.LogWarning("[BattleDemo][start.blocked] Formal round mode owns the battle input.", this); return; }
        if (!EnsureSceneReady()) return;
        var before = coordinator.State;
        var succeeded = coordinator.StartOrContinue(GetFactory(), catalogResourcePath, battleResourcePath);
        LogAction("startOrContinue", succeeded, before);
        RefreshUi();
    }

    public void Pause()
    {
        if (!EnsureSceneReady()) return;
        var before = coordinator.State;
        LogAction("pause", coordinator.Pause(), before);
        RefreshUi();
    }

    public void Replay()
    {
        if (!EnsureSceneReady()) return;
        var before = coordinator.State;
        LogAction("replay", coordinator.Replay(), before);
        RefreshUi();
    }

    public void Recalculate()
    {
        if (formalRoundMode) { Debug.LogWarning("[BattleDemo][recalculate.blocked] Formal round mode owns the battle input.", this); return; }
        if (!EnsureSceneReady()) return;
        var before = coordinator.State;
        var succeeded = coordinator.Recalculate(GetFactory(), catalogResourcePath, battleResourcePath, true);
        LogAction("recalculate", succeeded, before);
        RefreshUi();
    }

    public void CycleSpeed()
    {
        if (!EnsureSceneReady()) return;
        var next = coordinator.Speed < 0.75f ? 1f : coordinator.Speed < 1.5f ? 2f : 0.5f;
        LogAction("speed=" + next, coordinator.SetSpeed(next), coordinator.State);
        RefreshUi();
    }

    public void SetHomeView() => SetObserver(BattleObserverView.Home);
    public void SetAwayView() => SetObserver(BattleObserverView.Away);

    /// <summary>Called only by the UI-004 phase bridge; it preserves debug pause/replay/view controls for the sealed result.</summary>
    public void SetFormalRoundMode(bool value) => formalRoundMode = value;

    public bool StartRuntimeBattle(BattleInput input, UnitCatalog catalog)
    {
        if (!EnsureSceneReady()) return false;
        var before = coordinator.State;
        var succeeded = coordinator.StartRuntimeBattle(GetFactory(), input, catalog, true);
        LogAction("runtime.start", succeeded, before);
        RefreshUi();
        return succeeded;
    }

    public void ResetRuntimeBattle()
    {
        if (coordinator == null) return;
        coordinator.Reset();
        lastLoggedState = coordinator.State;
        RefreshUi();
    }

    private void SetObserver(BattleObserverView observer)
    {
        if (!EnsureSceneReady()) return;
        coordinator.SetObserver(observer);
        Debug.Log("[BattleDemo][observer.changed] observer=" + observer + "; state=" + coordinator.State, this);
        RefreshUi();
    }

    private bool EnsureSceneReady()
    {
        if (coordinator == null || !TryGetFactory(out _))
        {
            Debug.LogError("[BattleDemo][scene.reference.invalid] Demo cannot run because required serialized references are missing.", this);
            return false;
        }
        return true;
    }

    private IBattlePresentationViewFactory GetFactory()
    {
        TryGetFactory(out var factory);
        return factory;
    }

    private bool TryGetFactory(out IBattlePresentationViewFactory factory)
    {
        factory = presentationFactoryComponent as IBattlePresentationViewFactory;
        return factory != null;
    }

    private void LogAction(string action, bool succeeded, BattleDemoState before)
    {
        var level = succeeded ? LogType.Log : LogType.Warning;
        var message = "[BattleDemo][" + action + "] success=" + succeeded + "; from=" + before + "; to=" + coordinator.State + "; battle=" + (coordinator.Input == null ? "<not-loaded>" : coordinator.Input.BattleId);
        if (level == LogType.Log) Debug.Log(message, this); else Debug.LogWarning(message + "; error=" + coordinator.LastError, this);
        lastLoggedState = coordinator.State;
    }

    private void LogAndRefreshOnStateChange()
    {
        if (coordinator == null || coordinator.State == lastLoggedState) return;
        Debug.Log("[BattleDemo][state.changed] from=" + lastLoggedState + "; to=" + coordinator.State + "; battle=" + (coordinator.Input == null ? "<not-loaded>" : coordinator.Input.BattleId) + "; error=" + coordinator.LastError, this);
        lastLoggedState = coordinator.State;
    }

    private void RefreshUi() => demoUi?.Refresh();

    private void DisposeCoordinator()
    {
        if (coordinator == null) return;
        coordinator.Dispose();
        coordinator = null;
    }
}
