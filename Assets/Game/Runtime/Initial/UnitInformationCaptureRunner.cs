using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Deployment;
using ArknoNights.Details;
using ArknoNights.Round;
using ArknoNights.UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicit Player-only unit-information visual acceptance entry. It uses production layout and commands,
/// waits for end-of-frame rendering, validates each written PNG, and writes a JSON manifest beside captures.</summary>
public sealed class UnitInformationCaptureRunner : MonoBehaviour
{
    private readonly List<CaptureRecord> captures = new List<CaptureRecord>();
    private static readonly string[] InformationElementPaths =
    {
        "UnitName", "CombatSummary", "Portrait", "TargetValue", "TargetValue/Background", "TargetValue/Icon", "TargetValue/Value", "HealthBackground", "HealthValue", "Tab_技能",
        "Stat_maxHp", "Stat_maxHp/Background", "Stat_maxHp/Icon", "Stat_maxHp/Label", "Stat_maxHp/Value",
        "Stat_moveSpeed", "Stat_moveSpeed/Background", "Stat_moveSpeed/Icon", "Stat_moveSpeed/Label", "Stat_moveSpeed/Value",
        "Stat_attack", "Stat_attack/Background", "Stat_attack/Icon", "Stat_attack/Label", "Stat_attack/Value",
        "Stat_attackInterval", "Stat_attackInterval/Background", "Stat_attackInterval/Icon", "Stat_attackInterval/Label", "Stat_attackInterval/Value",
        "Stat_defense", "Stat_defense/Background", "Stat_defense/Icon", "Stat_defense/Label", "Stat_defense/Value",
        "Stat_magicResistance", "Stat_magicResistance/Background", "Stat_magicResistance/Icon", "Stat_magicResistance/Label", "Stat_magicResistance/Value",
        "Stat_block", "Stat_block/Background", "Stat_block/Icon", "Stat_block/Label", "Stat_block/Value",
        "Stat_deploymentCost", "Stat_deploymentCost/Background", "Stat_deploymentCost/Icon", "Stat_deploymentCost/Label", "Stat_deploymentCost/Value"
    };
    private string outputDirectory;
    private StagingHudController hud;
    private FormalBattleHudController formalHud;
    private StateDrivenDeploymentController deployment;
    private PreparationBattleLoopController loop;
    private bool captureVisualFixtures;
    private string activeVisualFixtureId;

    private void Awake()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "-uiCaptureSuite", StringComparison.OrdinalIgnoreCase))) { Destroy(this); return; }
        outputDirectory = CommandLineValue("-uiCaptureOutput") ?? Path.Combine(Application.dataPath, "..", "UnitInformationCaptures");
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        captureVisualFixtures = HasCommandLineFlag("-uiCaptureVisualFixture");
        StartCoroutine(Capture());
    }

    private IEnumerator Capture()
    {
        for (var frame = 0; frame < 32; frame++)
        {
            hud = FindObjectOfType<StagingHudController>();
            formalHud = FindObjectOfType<FormalBattleHudController>();
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
        var enemy = loop.MultiBattle?.PresentationViewStates.FirstOrDefault(state => state.Side == BattleSide.Away)
            ?? FindObjectOfType<BattleDemoController>()?.Coordinator?.PresentationViewStates.FirstOrDefault(state => state.Side == BattleSide.Away);
        if (enemy != null) formalHud.SelectBattleUnitForHud(enemy.UnitId);
        yield return CaptureOne("battle_enemy_selected_1920x1080", "Battle", enemy?.UnitId);
        yield return SetResolution(1600, 900);
        yield return CaptureOne("prep_or_battle_1600x900", loop.Phase.ToString(), formalHud.SelectedUnitId);
        yield return SetResolution(1280, 1024);
        yield return CaptureOne("prep_or_battle_1280x1024", loop.Phase.ToString(), formalHud.SelectedUnitId);
        if (captureVisualFixtures)
        {
            yield return SetResolution(1920, 1080);
            activeVisualFixtureId = "empty-name";
            formalHud.ShowVisualFixtureForCapture(activeVisualFixtureId);
            yield return CaptureOne("fixture_empty_name_1920x1080", "VisualFixture", null);
            activeVisualFixtureId = "medium-name";
            formalHud.ShowVisualFixtureForCapture(activeVisualFixtureId);
            yield return CaptureOne("fixture_medium_name_1920x1080", "VisualFixture", null);
            formalHud.ClearVisualFixtureForCapture();
            activeVisualFixtureId = null;
        }
        File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"), JsonUtility.ToJson(new CaptureManifest { captures = captures.ToArray() }, true));
        Debug.Log("[UnitInformationCapture][completed] count=" + captures.Count + "; output=" + outputDirectory, this);
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
        var detail = ResolveDetail(selectedUnitId);
        var panel = hud.transform.Find("FormalBattleHudCanvas/FormalHud/UnitInformationPanel") as RectTransform;
        var visualFixture = !string.IsNullOrEmpty(activeVisualFixtureId);
        captures.Add(new CaptureRecord
        {
            name = name, path = path, width = capturedWidth, height = capturedHeight, phase = phase,
            captureStage = CommandLineValue("-uiCaptureStage") ?? "unspecified",
            selectedUnitId = selectedUnitId ?? string.Empty, cost = hud.PlayerState.DeploymentCost,
            remainingPreparationSeconds = loop.RemainingPreparationSeconds,
            source = visualFixture ? "visualFixture" : (detail == null ? string.Empty : (loop.Phase == LocalBattlePhase.Battle ? "sealed-battle-input+presentation" : "player-state")),
            typeId = visualFixture ? activeVisualFixtureId : (detail?.TypeId ?? string.Empty), displayNameConfigured = visualFixture ? !string.Equals(activeVisualFixtureId, "empty-name", StringComparison.Ordinal) : detail != null && !string.IsNullOrWhiteSpace(detail.DisplayNameZhHans),
            rarity = detail?.Rarity ?? 0, eliteLevel = detail?.EliteLevel ?? -1, currentHitPoints = detail?.CurrentHitPoints ?? 0,
            maxHitPoints = detail?.MaxHitPoints ?? 0, values = detail == null ? Array.Empty<string>() : new[] { detail.MaxHitPoints.ToString(), Detail(detail.MoveSpeedCentimetresPerSecond), Detail(detail.Attack), Detail(detail.AttackIntervalTicks), Detail(detail.Defense), Detail(detail.MagicResistance), detail.BlockCapacity.ToString(), detail.DeploymentCost.ToString(), detail.LifeDeduct.ToString() },
            informationPanelWidth = panel == null ? 0f : panel.rect.width, informationPanelHeight = panel == null ? 0f : panel.rect.height,
            canvasScale = hud.GetComponentInChildren<Canvas>() == null ? 0f : hud.GetComponentInChildren<Canvas>().scaleFactor,
            panelRect = ScreenRect(panel), elementRects = CaptureRects(panel), texts = CaptureTexts(panel), icons = CaptureIcons(panel),
            visualFixture = visualFixture, visualFixtureId = activeVisualFixtureId ?? string.Empty,
            referenceRect = "1040,200,830,420", mappedTargetRect = "0,90,720,364.3"
        });
    }

    private UnitDetailSnapshot ResolveDetail(string unitId)
    {
        if (string.IsNullOrEmpty(unitId)) return null;
        var catalogLoad = UnitCatalogLoader.LoadFromResources("BattleData/unit-catalog-v1");
        if (!catalogLoad.Success) return null;
        var multi = loop == null ? null : loop.MultiBattle;
        var selectedMatch = multi == null ? null : multi.Matches.FirstOrDefault(match => match.MatchId == multi.SelectedMatchId);
        var input = selectedMatch?.Input ?? FindObjectOfType<BattleDemoController>()?.Coordinator?.Input;
        var states = multi?.PresentationViewStates ?? FindObjectOfType<BattleDemoController>()?.Coordinator?.PresentationViewStates;
        if (loop.Phase == LocalBattlePhase.Battle && input != null && UnitDetailResolver.TryResolveBattle(input, states, catalogLoad.Catalog, unitId, out var battleDetail)) return battleDetail;
        return UnitDetailResolver.TryResolvePreparation(hud.Snapshot, catalogLoad.Catalog, unitId, out var preparationDetail) ? preparationDetail : null;
    }

    private static string Detail(int? value) => value.HasValue ? value.Value.ToString() : "--";

    private static ElementRecord[] CaptureRects(RectTransform panel)
    {
        if (panel == null) return Array.Empty<ElementRecord>();
        return InformationElementPaths.Select(path => new ElementRecord { name = path, rect = ScreenRect(panel.Find(path) as RectTransform) }).ToArray();
    }

    private static TextRecord[] CaptureTexts(RectTransform panel)
    {
        if (panel == null) return Array.Empty<TextRecord>();
        return panel.GetComponentsInChildren<Text>(true).Select(text => new TextRecord
        {
            name = text.transform.GetPath(panel), value = text.text, font = text.font == null ? string.Empty : text.font.name,
            fontSize = text.fontSize, alignment = text.alignment.ToString(), rect = ScreenRect(text.rectTransform)
        }).ToArray();
    }

    private static IconRecord[] CaptureIcons(RectTransform panel)
    {
        if (panel == null) return Array.Empty<IconRecord>();
        return panel.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(image => image.transform.name == "Icon").Select(image => new IconRecord
        {
            name = image.transform.GetPath(panel), sprite = image.sprite == null ? string.Empty : image.sprite.name, rect = ScreenRect(image.rectTransform)
        }).ToArray();
    }

    private static RectRecord ScreenRect(RectTransform transform)
    {
        if (transform == null) return new RectRecord();
        var corners = new Vector3[4];
        transform.GetWorldCorners(corners);
        var bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        var topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        return new RectRecord { x = bottomLeft.x, y = Screen.height - topRight.y, width = topRight.x - bottomLeft.x, height = topRight.y - bottomLeft.y };
    }

    private static IEnumerator SetResolution(int width, int height)
    {
        Screen.SetResolution(width, height, false);
        for (var frame = 0; frame < 180 && (Screen.width != width || Screen.height != height); frame++) yield return null;
        if (Screen.width != width || Screen.height != height)
            Debug.LogWarning("[UnitInformationCapture][resolution.pending] requested=" + width + "x" + height + "; actual=" + Screen.width + "x" + Screen.height);
    }

    private void Fail(string detail) { Debug.LogError("[UnitInformationCapture][failed] " + detail, this); Application.Quit(1); }
    private static string CommandLineValue(string flag) { var args = Environment.GetCommandLineArgs(); for (var index = 0; index + 1 < args.Length; index++) if (args[index] == flag) return args[index + 1]; return null; }
    private static bool HasCommandLineFlag(string flag) => Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
    [Serializable] private sealed class CaptureManifest { public CaptureRecord[] captures; }
    [Serializable] private sealed class CaptureRecord { public string name; public string path; public int width; public int height; public string phase; public string captureStage; public string selectedUnitId; public int cost; public float remainingPreparationSeconds; public string source; public string typeId; public bool displayNameConfigured; public int rarity; public int eliteLevel; public int currentHitPoints; public int maxHitPoints; public string[] values; public float informationPanelWidth; public float informationPanelHeight; public float canvasScale; public RectRecord panelRect; public ElementRecord[] elementRects; public TextRecord[] texts; public IconRecord[] icons; public bool visualFixture; public string visualFixtureId; public string referenceRect; public string mappedTargetRect; }
    [Serializable] private sealed class RectRecord { public string name; public float x; public float y; public float width; public float height; }
    [Serializable] private sealed class ElementRecord { public string name; public RectRecord rect; }
    [Serializable] private sealed class TextRecord { public string name; public string value; public string font; public int fontSize; public string alignment; public RectRecord rect; }
    [Serializable] private sealed class IconRecord { public string name; public string sprite; public RectRecord rect; }
}

internal static class UiCaptureTransformExtensions
{
    public static string GetPath(this Transform transform, Transform ancestor)
    {
        if (transform == null || transform == ancestor) return string.Empty;
        var segments = new Stack<string>();
        for (var current = transform; current != null && current != ancestor; current = current.parent) segments.Push(current.name);
        return string.Join("/", segments);
    }
}

internal static class UnitInformationCaptureBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Attach()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "-uiCaptureSuite", StringComparison.OrdinalIgnoreCase))) return;
        var holder = new GameObject("UnitInformationCaptureRunner");
        UnityEngine.Object.DontDestroyOnLoad(holder);
        holder.AddComponent<UnitInformationCaptureRunner>();
    }
}
