using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Deployment;
using ArknoNights.UI;
using UnityEngine;

/// <summary>Explicit Player-only UI-005 visual acceptance entry. It uses production layout and commands,
/// waits for end-of-frame rendering, validates each written PNG, and writes a JSON manifest beside captures.</summary>
public sealed class UI005CaptureSuite : MonoBehaviour
{
    private readonly List<CaptureRecord> captures = new List<CaptureRecord>();
    private string outputDirectory;
    private StagingHudController hud;
    private FormalBattleHudUi005 formalHud;
    private StateDrivenDeploymentController deployment;
    private PreparationBattleLoopController loop;

    private void Awake()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "-uiCaptureSuite", StringComparison.OrdinalIgnoreCase))) { Destroy(this); return; }
        outputDirectory = CommandLineValue("-uiCaptureOutput") ?? Path.Combine(Application.dataPath, "..", "UI-005-Captures");
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        StartCoroutine(Capture());
    }

    private IEnumerator Capture()
    {
        for (var frame = 0; frame < 32; frame++)
        {
            hud = FindObjectOfType<StagingHudController>();
            formalHud = FindObjectOfType<FormalBattleHudUi005>();
            deployment = hud == null ? null : hud.GetComponent<StateDrivenDeploymentController>();
            loop = hud == null ? null : hud.GetComponent<PreparationBattleLoopController>();
            if (hud != null && formalHud != null && deployment != null && loop != null && hud.InitializationSucceeded) break;
            yield return null;
        }
        if (hud == null || formalHud == null || deployment == null || loop == null) { Fail("dependencies.missing"); yield break; }

        yield return SetResolution(1920, 1080);
        yield return new WaitForEndOfFrame();
        hud.ClearStagingSelection();
        yield return CaptureOne("prep_unselected_1920x1080", "Preparation", null);
        var firstSlot = StagingHudController.BuildSlotId(hud.Snapshot.StagingSlots[0]);
        hud.ToggleSelection(firstSlot);
        yield return CaptureOne("prep_selected_staging_1920x1080", "Preparation", hud.Snapshot.StagingSlots[0].UnitIds[0]);
        deployment.BeginDragFromSlot(firstSlot);
        deployment.SetDragWorldPositionForTests(new Vector3(500f, 0f, 200f));
        deployment.CommitCurrentDragForTests();
        deployment.SelectDeployedForTests("local-1000-alpha");
        yield return CaptureOne("prep_selected_deployed_1920x1080", "Preparation", "local-1000-alpha");
        loop.AdvanceForTests(30f);
        yield return null;
        var enemy = FindObjectOfType<BattleDemoController>()?.Coordinator?.PresentationViewStates.FirstOrDefault(state => state.Side == BattleSide.Away);
        if (enemy != null) formalHud.SelectBattleUnitForHud(enemy.UnitId);
        yield return CaptureOne("battle_enemy_selected_1920x1080", "Battle", enemy?.UnitId);
        yield return SetResolution(1600, 900);
        yield return CaptureOne("prep_or_battle_1600x900", loop.Phase.ToString(), formalHud.SelectedUnitId);
        yield return SetResolution(1280, 1024);
        yield return CaptureOne("prep_or_battle_1280x1024", loop.Phase.ToString(), formalHud.SelectedUnitId);
        File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"), JsonUtility.ToJson(new CaptureManifest { captures = captures.ToArray() }, true));
        Debug.Log("[UI-005][capture.completed] count=" + captures.Count + "; output=" + outputDirectory, this);
        Application.Quit(0);
    }

    private IEnumerator CaptureOne(string name, string phase, string selectedUnitId)
    {
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        var path = Path.Combine(outputDirectory, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path, 1);
        // ScreenCapture writes asynchronously. A fixed short sleep is not reliable on slower Player startup;
        // allow ten seconds of rendered frames and fail explicitly rather than exiting with a queued write.
        byte[] bytes = null;
        for (var frame = 0; frame < 600 && bytes == null; frame++)
        {
            if (File.Exists(path))
            {
                try { bytes = File.ReadAllBytes(path); }
                catch (IOException) { } // CaptureScreenshot still owns the file; wait for its writer to close.
            }
            if (bytes == null) yield return null;
        }
        if (bytes == null) { Fail("screenshot.timeout:" + name); yield break; }
        var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var valid = ImageConversion.LoadImage(probe, bytes, false);
        var capturedWidth = probe.width;
        var capturedHeight = probe.height;
        if (valid)
        {
            var pixels = probe.GetPixels32();
            var brightPixels = pixels.Count(pixel => pixel.r > 4 || pixel.g > 4 || pixel.b > 4);
            valid = brightPixels > pixels.Length / 200; // rejects the known all-black graphics-context failure.
        }
        Destroy(probe);
        if (!valid) { Fail("screenshot.invalid:" + name); yield break; }
        captures.Add(new CaptureRecord { name = name, path = path, width = capturedWidth, height = capturedHeight, phase = phase, selectedUnitId = selectedUnitId ?? string.Empty, cost = hud.PlayerState.DeploymentCost, remainingPreparationSeconds = loop.RemainingPreparationSeconds });
    }

    private static IEnumerator SetResolution(int width, int height)
    {
        Screen.SetResolution(width, height, false);
        for (var frame = 0; frame < 180 && (Screen.width != width || Screen.height != height); frame++) yield return null;
        if (Screen.width != width || Screen.height != height)
            Debug.LogWarning("[UI-005][capture.resolution.pending] requested=" + width + "x" + height + "; actual=" + Screen.width + "x" + Screen.height);
    }

    private void Fail(string detail) { Debug.LogError("[UI-005][capture.failed] " + detail, this); Application.Quit(1); }
    private static string CommandLineValue(string flag) { var args = Environment.GetCommandLineArgs(); for (var index = 0; index + 1 < args.Length; index++) if (args[index] == flag) return args[index + 1]; return null; }
    [Serializable] private sealed class CaptureManifest { public CaptureRecord[] captures; }
    [Serializable] private sealed class CaptureRecord { public string name; public string path; public int width; public int height; public string phase; public string selectedUnitId; public int cost; public float remainingPreparationSeconds; }
}

internal static class UI005CaptureSuiteBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Attach()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "-uiCaptureSuite", StringComparison.OrdinalIgnoreCase))) return;
        var holder = new GameObject("UI005CaptureSuite");
        UnityEngine.Object.DontDestroyOnLoad(holder);
        holder.AddComponent<UI005CaptureSuite>();
    }
}
