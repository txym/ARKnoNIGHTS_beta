using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using ArknoNights.Deployment;
using ArknoNights.Player;
using ArknoNights.Round;
using ArknoNights.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Thin SampleScene bridge for UI-009. It owns phase side effects only; the preparation clock, sealing,
/// and PlayerState-to-Core conversion remain ordinary testable C# in ARKnoNIGHTS.Round.
/// </summary>
[DisallowMultipleComponent]
public sealed class PreparationBattleLoopController : MonoBehaviour
{
    private const string CatalogPath = "BattleData/unit-catalog-v1";
    private const string LocalMatchPath = "PlayerData/local-match-state-v1";
    private const int BattleMaxTicks = 1800;

    private readonly PreparationBattlePhaseMachine machine = new PreparationBattlePhaseMachine();
    private StagingHudController hud;
    private StateDrivenDeploymentController deployment;
    private BattleDemoController demo;
    private UnitCatalog catalog;
    private AbilityCatalog abilityCatalog;
    private LocalMatchState matchState;
    private MultiBattlePresentationCoordinator multiBattle;
    private int roundNumber;
    private FourPlayerBattleRoundSealResult activeSeal;
    private bool initialized;
    private bool lobbyGateActive;

    public LocalBattlePhase Phase => machine.Phase;
    public float RemainingPreparationSeconds => machine.RemainingPreparationSeconds;
    public string LastError => machine.LastError;
    public FourPlayerBattleRoundSealResult ActiveSeal => activeSeal;
    /// <summary>Shared-clock presentation session for UI-009. It is null before the first battle or after teardown.</summary>
    public MultiBattlePresentationCoordinator MultiBattle => multiBattle;
    /// <summary>Fixture player/economy source; the formal HUD scene coordinator attaches its shop and player-list surfaces.</summary>
    public LocalMatchState MatchState => matchState;
    public string LastBattleSummary { get; private set; } = string.Empty;
    public bool IsLobbyGateActive => lobbyGateActive;

    /// <summary>Reversible LAN-lobby gate. It neither reads nor mutates PlayerState.</summary>
    public void SetLobbyGate(bool active)
    {
        lobbyGateActive = active;
        if (!active && initialized && machine.Phase == LocalBattlePhase.Preparation)
            machine.EnterPreparation();
    }

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
        // Existing scene tests use this as a deterministic clock driver; the runtime Update path remains lobby-gated.
        if (initialized) Advance(unscaledSeconds, true);
    }

    private void Initialize()
    {
        if (initialized) return;
        if (hud == null || !hud.InitializationSucceeded || deployment == null || demo == null)
        {
            Fail("round.scene.dependencies.missing");
            return;
        }

        var catalogLoad = UnitCatalogLoader.LoadFromResources(CatalogPath);
        if (!catalogLoad.Success)
        {
            Fail("round.catalog.load.failed:" + string.Join(" | ", catalogLoad.Errors.Select(error => error.ToString()).ToArray()));
            return;
        }
        var abilityCatalogLoad = AbilityCatalogLoader.LoadFromResources("BattleData/ability-catalog-v1", catalogLoad.Catalog);
        if (!abilityCatalogLoad.Success)
        {
            Fail("round.abilityCatalog.load.failed:" + string.Join(" | ", abilityCatalogLoad.Errors.Select(error => error.ToString()).ToArray()));
            return;
        }
        var matchLoad = LocalMatchStateLoader.LoadFromResources(catalogLoad.Catalog, LocalMatchPath, hud.PlayerState);
        if (!matchLoad.Success)
        {
            Fail("round.localMatch.load.failed:" + string.Join(" | ", matchLoad.Errors.Select(error => error.ToString()).ToArray()));
            return;
        }

        catalog = catalogLoad.Catalog;
        abilityCatalog = abilityCatalogLoad.Catalog;
        matchState = matchLoad.State;
        matchState.Changed += HandleMatchChanged;
        multiBattle = new MultiBattlePresentationCoordinator();
        demo.SetFormalRoundMode(true);
        demo.ResetRuntimeBattle();
        deployment.SetPreparationViewsVisible(true);
        deployment.SetInteractionEnabled(true);
        machine.EnterPreparation();
        initialized = true;
        Debug.Log("[UI-009][phase.enter] phase=Preparation; remaining=" + RemainingPreparationSeconds + "; player=" + hud.PlayerState.PlayerId + "; fixturePlayers=" + string.Join(",", matchState.OrderedPlayerIds.ToArray()), this);
    }

    private void Advance(float unscaledSeconds)
    {
        Advance(unscaledSeconds, false);
    }

    private void Advance(float unscaledSeconds, bool ignoreLobbyGate)
    {
        if (lobbyGateActive && !ignoreLobbyGate && machine.Phase == LocalBattlePhase.Preparation) return;
        if (machine.Phase == LocalBattlePhase.Preparation && machine.Advance(unscaledSeconds))
        {
            BeginBattle();
            return;
        }
        if (machine.Phase != LocalBattlePhase.Battle || multiBattle == null) return;
        if (multiBattle.State == MultiBattlePresentationState.Error)
        {
            Fail("round.presentation.error:" + multiBattle.LastError);
            return;
        }
        if (multiBattle.State
            == MultiBattlePresentationState.Preparing)
        {
            var firstChunkBudget =
                BattleSimulationProducer
                    .AuthoritativeTicksPerFullChunk
                * Math.Max(1, multiBattle.Matches.Count);
            if (!multiBattle.PumpComputation(firstChunkBudget))
            {
                Fail("round.presentation.error:" + multiBattle.LastError);
                return;
            }
        }
        if (multiBattle.State == MultiBattlePresentationState.Ready
            && !multiBattle.Play())
        {
            Fail("round.presentation.play.failed:" + multiBattle.LastError);
            return;
        }
        multiBattle.Advance(unscaledSeconds);
        if (multiBattle.State == MultiBattlePresentationState.Error)
        {
            Fail("round.presentation.error:" + multiBattle.LastError);
            return;
        }
        if (multiBattle.State == MultiBattlePresentationState.Completed) ReturnToPreparation();
    }

    private void BeginBattle()
    {
        // This is deliberately first: no UI drag/selection can commit while irreversible PlayerState changes begin.
        deployment.SetInteractionEnabled(false);
        deployment.SetPreparationViewsVisible(false);
        var battleId = "ui009-round-" + (++roundNumber);
        if (!FourPlayerBattleRoundSealer.TrySealRound(matchState, catalog, abilityCatalog, BattleMaxTicks, battleId, out activeSeal, out var error))
        {
            Fail(error);
            return;
        }

        if (!demo.TryGetPresentationFactory(out var factory))
        {
            Fail("round.presentationFactory.missing");
            return;
        }
        var requests = activeSeal.Matches.Select(match => new BattleMatchRequest(match.MatchId, match.Input)).ToArray();
        var observations = BuildObservations(activeSeal).ToArray();
        if (!multiBattle.Prepare(requests, observations, factory, matchState.ObservedPlayerId))
        {
            Fail("round.multiBattle.start.failed:" + multiBattle.LastError);
            return;
        }
        Debug.Log("[UI-009][phase.sealed] round=" + battleId + "; playerSeals=" + string.Join(";", activeSeal.PlayerSeals.Select(seal => seal.After.PlayerId + "/overflow=" + string.Join(",", seal.OverflowRemovedUnitIds.ToArray()) + "/auto=" + (string.IsNullOrEmpty(seal.AutoDeployedUnitId) ? "<none>" : seal.AutoDeployedUnitId)).ToArray()) + "; summary=" + multiBattle.StableSummary, this);
    }

    private void ReturnToPreparation()
    {
        var current = matchState.Snapshot;
        var statesUnchangedDuringBattle = activeSeal != null && activeSeal.PlayerSeals.All(seal => current.Players.Any(player => player.PlayerId == seal.After.PlayerId && string.Equals(player.PlayerState.CanonicalSummary, seal.After.CanonicalSummary, StringComparison.Ordinal)));
        LastBattleSummary = "summary=" + multiBattle.StableSummary + "; playersUnchangedDuringBattle=" + statesUnchangedDuringBattle;
        Debug.Log("[UI-009][battle.completed] " + LastBattleSummary, this);

        matchState.RefreshAllShopsAfterBattle();
        multiBattle.Reset();
        demo.ResetRuntimeBattle();
        deployment.SetPreparationViewsVisible(true);
        deployment.SetInteractionEnabled(true);
        activeSeal = null;
        machine.CompleteBattle();
        Debug.Log("[UI-009][phase.enter] phase=Preparation; remaining=" + RemainingPreparationSeconds + "; playerCost=" + current.LocalPlayer.PlayerState.DeploymentCost, this);
    }

    /// <summary>Battle-phase-only observer entry for UI-008/010 player-list bindings.</summary>
    public bool TryObserveBattlePlayer(string playerId)
    {
        if (!initialized || machine.Phase != LocalBattlePhase.Battle || matchState == null || multiBattle == null) return false;
        var operation = matchState.TryObserve(playerId);
        return operation.Success && multiBattle.State != MultiBattlePresentationState.Error;
    }

    private static IEnumerable<PlayerBattleObservation> BuildObservations(FourPlayerBattleRoundSealResult seal)
    {
        foreach (var match in seal.Matches)
        {
            var home = match.Input.Players.Single(player => player.Side == ArknoNights.Battle.Core.BattleSide.Home);
            var away = match.Input.Players.Single(player => player.Side == ArknoNights.Battle.Core.BattleSide.Away);
            yield return new PlayerBattleObservation(home.PlayerId, match.MatchId, BattleObserverView.Home);
            yield return new PlayerBattleObservation(away.PlayerId, match.MatchId, BattleObserverView.Away);
        }
    }

    private void HandleMatchChanged(LocalMatchSnapshot snapshot)
    {
        if (machine.Phase != LocalBattlePhase.Battle || multiBattle == null || multiBattle.State == MultiBattlePresentationState.Error) return;
        if (!multiBattle.SelectObservedPlayer(snapshot.ObservedPlayerId)) Fail("round.observer.switch.failed:" + multiBattle.LastError);
    }

    private void Fail(string error)
    {
        machine.Fail(error);
        if (deployment != null)
        {
            deployment.SetInteractionEnabled(false);
            deployment.SetPreparationViewsVisible(false);
        }
        Debug.LogError("[UI-009][phase.error] " + error, this);
    }

    private void OnDestroy()
    {
        if (matchState != null) matchState.Changed -= HandleMatchChanged;
        multiBattle?.Dispose();
        if (demo != null) demo.SetFormalRoundMode(false);
    }
}

/// <summary>Idempotently attaches exactly one UI-009 bridge after the existing UI bootstrap has run.</summary>
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
