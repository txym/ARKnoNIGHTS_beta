using System;
using System.Collections;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Presentation;
using UnityEngine;

/// <summary>Explicit command-line-only Player acceptance path for TASK-006. It never runs during normal Demo play.</summary>
public sealed class Task006PlayerAcceptance : MonoBehaviour
{
    private const string AcceptanceArgument = "-task006-acceptance";
    private const string CatalogPath = "BattleData/unit-catalog-v1";
    private const string BattlePath = "BattleData/task004a-real-1v1";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartWhenExplicitlyRequested()
    {
        if (!Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, AcceptanceArgument, StringComparison.Ordinal))) return;
        var runner = new GameObject("Task006PlayerAcceptance").AddComponent<Task006PlayerAcceptance>();
        DontDestroyOnLoad(runner.gameObject);
    }

    private IEnumerator Start()
    {
        yield return null;

        var controller = FindObjectOfType<BattleDemoController>();
        if (!controller) { Fail("controller.missing"); yield break; }

        controller.StartOrContinue();
        yield return null;
        var demo = controller.Coordinator;
        if (demo == null || demo.State != BattleDemoState.Playing) { Fail("demo.start.failed; error=" + (demo == null ? "<none>" : demo.LastError)); yield break; }
        if (!HasExpectedRealCatalog(demo)) { Fail("catalog.connection.invalid"); yield break; }

        var baseline = Task006AcceptanceSummary.Capture(demo);
        string mismatch;
        if (!Task006AcceptanceSummary.VerifyTenRuns(baseline, out mismatch)) { Fail(mismatch); yield break; }

        controller.SetHomeView();
        demo.Advance(1200f);
        if (demo.State != BattleDemoState.Completed || !Task006AcceptanceSummary.Matches(baseline, Task006AcceptanceSummary.Capture(demo))) { Fail("home.playback.invalid"); yield break; }

        controller.Replay();
        controller.SetAwayView();
        demo.Advance(1200f);
        if (demo.State != BattleDemoState.Completed || !Task006AcceptanceSummary.Matches(baseline, Task006AcceptanceSummary.Capture(demo))) { Fail("away.replay.invalid"); yield break; }

        // Replay disposal uses UnityEngine.Object.Destroy, which completes at the end of this frame.
        yield return null;
        var viewCount = FindObjectsOfType<UnitSkelPresentationView>().Length;
        var expectedViewCount = demo.Input.Players.Sum(player => player.Units.Count(unit => unit.Zone == UnitZone.Deployed));
        if (viewCount != expectedViewCount) { Fail("view.count.invalid; expected=" + expectedViewCount + "; count=" + viewCount); yield break; }

        Debug.Log("[TASK-006][player.acceptance] status=passed; iterations=10; views=" + viewCount + "; " + baseline);
        Application.Quit(0);
    }

    private static bool HasExpectedRealCatalog(BattleDemoCoordinator demo)
    {
        return demo.Catalog != null
            && demo.Catalog.Entries.Any(entry => entry.Definition.TypeId == "1000" && entry.ResourceKey == "gopro")
            && demo.Catalog.Entries.Any(entry => entry.Definition.TypeId == "5503" && entry.ResourceKey == "arcslma");
    }

    private static void Fail(string detail)
    {
        Debug.LogError("[TASK-006][player.acceptance] status=failed; detail=" + detail);
        Application.Quit(1);
    }
}

/// <summary>Stable acceptance summary built exclusively from Core and immutable Demo result data.</summary>
public static class Task006AcceptanceSummary
{
    private const string CatalogPath = "BattleData/unit-catalog-v1";
    private const string BattlePath = "BattleData/task004a-real-1v1";

    public static string Capture(BattleDemoCoordinator demo)
    {
        var finalStates = string.Join(",", demo.Result.FinalUnits.Select(unit => unit.UnitId + ":" + unit.IsAlive));
        return "inputDigest=" + demo.InputDigest + "; eventDigest=" + demo.EventDigest + "; finalStateDigest=" + demo.ResultDigest + "; winnerOrReason=" + demo.WinnerOrReason + "; finalStates=" + finalStates;
    }

    public static bool Matches(string expected, string actual) => string.Equals(expected, actual, StringComparison.Ordinal);

    public static bool TryCaptureOne(out string summary, out string failure)
    {
        using (var demo = new BattleDemoCoordinator())
        {
            if (!demo.StartOrContinue(new NoOpFactory(), CatalogPath, BattlePath))
            {
                summary = string.Empty;
                failure = "determinism.load.failed; error=" + demo.LastError;
                return false;
            }
            summary = Capture(demo);
            failure = string.Empty;
            return true;
        }
    }

    public static bool VerifyTenRuns(string expected, out string failure)
    {
        for (var index = 0; index < 10; index++)
        {
            using (var demo = new BattleDemoCoordinator())
            {
                if (!demo.StartOrContinue(new NoOpFactory(), CatalogPath, BattlePath))
                {
                    failure = "determinism.load.failed; iteration=" + (index + 1) + "; error=" + demo.LastError;
                    return false;
                }
                if (!Matches(expected, Capture(demo)))
                {
                    failure = "determinism.digest.mismatch; iteration=" + (index + 1) + "; actual=" + Capture(demo);
                    return false;
                }
            }
        }
        failure = string.Empty;
        return true;
    }

    private sealed class NoOpFactory : IBattlePresentationViewFactory
    {
        public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
        {
            view = new NoOpView();
            diagnostic = null;
            return true;
        }
    }

    private sealed class NoOpView : IBattlePresentationView
    {
        public void SetWorldPosition(Vector3 position) { }
        public void SetFacing(Vector3 direction) { }
        public void SetPlaybackSpeed(float playbackSpeed) { }
        public void PlayMove() { }
        public void PlayAttack(float animationSpeedMultiplier) { }
            public void PlayHit() { }
            public void PlayDeath() { }
            public void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield) { }
        public void Dispose() { }
    }
}
