using System;
using System.Collections;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Deployment;
using ArknoNights.Round;
using ArknoNights.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Thin SampleScene bridge for UI-004. It owns phase side effects only; the preparation clock, sealing,
/// and PlayerState-to-Core conversion remain ordinary testable C# in ARKnoNIGHTS.Round.
/// </summary>
[DisallowMultipleComponent]
public sealed class PreparationBattleLoopController : MonoBehaviour
{
    private const string CatalogPath = "BattleData/unit-catalog-v1";
    private const string FixedBattlePath = "BattleData/task004a-real-1v1";

    private readonly PreparationBattlePhaseMachine machine = new PreparationBattlePhaseMachine();
    private StagingHudController hud;
    private StateDrivenDeploymentController deployment;
    private BattleDemoController demo;
    private UnitCatalog catalog;
    private PlayerSnapshot fixedAway;
    private int maxTicks;
    private int roundNumber;
    private PreparationSealResult activeSeal;
    private bool initialized;

    public LocalBattlePhase Phase => machine.Phase;
    public float RemainingPreparationSeconds => machine.RemainingPreparationSeconds;
    public string LastError => machine.LastError;
    public PreparationSealResult ActiveSeal => activeSeal;
    public string LastBattleSummary { get; private set; } = string.Empty;

    private IEnumerator Start()
    {
        // UI-002/003 install their state-driven controller during Start; wait for that established boundary.
        for (var frame = 0; frame < 8; frame++)
        {
            hud = GetComponent<StagingHudController>();
            deployment = GetComponent<StateDrivenDeploymentController>();
            demo = FindObjectOfType<BattleDemoController>();
            if (hud != null && hud.InitializationSucceeded && deployment != null && demo != null) break;
            yield return null;
        }
        Initialize();
    }

    private void Update()
    {
        if (!initialized) return;
        Advance(Time.unscaledDeltaTime);
    }

    public void AdvanceForTests(float unscaledSeconds)
    {
        if (initialized) Advance(unscaledSeconds);
    }

    private void Initialize()
    {
        if (initialized) return;
        if (hud == null || !hud.InitializationSucceeded || deployment == null || demo == null)
        {
            Fail("round.scene.dependencies.missing");
            return;
        }

        var fixedLoad = LocalBattleLoader.LoadFromResources(CatalogPath, FixedBattlePath);
        if (!fixedLoad.Success)
        {
            Fail("round.fixedAway.load.failed:" + string.Join(" | ", fixedLoad.Errors.Select(error => error.ToString()).ToArray()));
            return;
        }
        fixedAway = fixedLoad.Input.Players.SingleOrDefault(player => player.Side == BattleSide.Away);
        if (fixedAway == null || !fixedAway.Units.Any(unit => unit.Zone == UnitZone.Deployed))
        {
            Fail("round.fixedAway.empty");
            return;
        }

        catalog = fixedLoad.Catalog;
        maxTicks = fixedLoad.Input.MaxTicks;
        demo.SetFormalRoundMode(true);
        demo.ResetRuntimeBattle();
        deployment.SetPreparationViewsVisible(true);
        deployment.SetInteractionEnabled(true);
        machine.EnterPreparation();
        initialized = true;
        Debug.Log("[UI-004][phase.enter] phase=Preparation; remaining=" + RemainingPreparationSeconds + "; player=" + hud.PlayerState.PlayerId, this);
    }

    private void Advance(float unscaledSeconds)
    {
        if (machine.Phase == LocalBattlePhase.Preparation && machine.Advance(unscaledSeconds)) BeginBattle();
        if (machine.Phase != LocalBattlePhase.Battle || demo == null) return;
        if (demo.State == BattleDemoState.Error)
        {
            Fail("round.presentation.error:" + demo.Coordinator.LastError);
            return;
        }
        if (demo.State == BattleDemoState.Completed) ReturnToPreparation();
    }

    private void BeginBattle()
    {
        // This is deliberately first: no UI drag/selection can commit while irreversible PlayerState changes begin.
        deployment.SetInteractionEnabled(false);
        deployment.SetPreparationViewsVisible(false);
        var battleId = "ui004-round-" + (++roundNumber);
        if (!PreparationBattleSealer.TrySeal(hud.PlayerState, catalog, fixedAway, battleId, maxTicks, out activeSeal, out var error))
        {
            Fail(error);
            return;
        }

        Debug.Log("[UI-004][phase.sealed] battle=" + battleId + "; overflowRemoved=" + string.Join(",", activeSeal.OverflowRemovedUnitIds.ToArray()) + "; autoDeployed=" + (string.IsNullOrEmpty(activeSeal.AutoDeployedUnitId) ? "<none>" : activeSeal.AutoDeployedUnitId) + "; cost=" + activeSeal.Before.DeploymentCost + "->" + activeSeal.After.DeploymentCost + "; before=" + activeSeal.Before.CanonicalSummary + "; after=" + activeSeal.After.CanonicalSummary, this);
        if (!demo.StartRuntimeBattle(activeSeal.Input, catalog))
        {
            Fail("round.demo.start.failed:" + demo.Coordinator.LastError);
            return;
        }
        Debug.Log("[UI-004][battle.started] battle=" + battleId + "; input=" + demo.Coordinator.InputDigest + "; winner=" + demo.Coordinator.WinnerOrReason, this);
    }

    private void ReturnToPreparation()
    {
        var current = hud.PlayerState.Snapshot;
        var stateUnchangedDuringBattle = activeSeal != null && string.Equals(activeSeal.After.CanonicalSummary, current.CanonicalSummary, StringComparison.Ordinal);
        LastBattleSummary = "battle=" + demo.Coordinator.Input.BattleId + "; input=" + demo.Coordinator.InputDigest + "; events=" + demo.Coordinator.EventDigest + "; result=" + demo.Coordinator.ResultDigest + "; winner=" + demo.Coordinator.WinnerOrReason + "; playerUnchangedDuringBattle=" + stateUnchangedDuringBattle;
        Debug.Log("[UI-004][battle.completed] " + LastBattleSummary, this);

        demo.ResetRuntimeBattle();
        deployment.SetPreparationViewsVisible(true);
        deployment.SetInteractionEnabled(true);
        activeSeal = null;
        machine.CompleteBattle();
        Debug.Log("[UI-004][phase.enter] phase=Preparation; remaining=" + RemainingPreparationSeconds + "; playerCost=" + current.DeploymentCost, this);
    }

    private void Fail(string error)
    {
        machine.Fail(error);
        if (deployment != null)
        {
            deployment.SetInteractionEnabled(false);
            deployment.SetPreparationViewsVisible(false);
        }
        Debug.LogError("[UI-004][phase.error] " + error, this);
    }

    private void OnDestroy()
    {
        if (demo != null) demo.SetFormalRoundMode(false);
    }
}

/// <summary>Idempotently attaches exactly one UI-004 bridge after the existing UI bootstrap has run.</summary>
internal static class PreparationBattleLoopBootstrap
{
    private static bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        if (!subscribed)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            subscribed = true;
        }
        AttachToLoadedScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToLoadedScene();

    private static void AttachToLoadedScene()
    {
        var hud = UnityEngine.Object.FindObjectOfType<StagingHudController>();
        if (hud == null) return;
        if (hud.GetComponent<PreparationBattleLoopController>() == null)
            hud.gameObject.AddComponent<PreparationBattleLoopController>();
    }
}
