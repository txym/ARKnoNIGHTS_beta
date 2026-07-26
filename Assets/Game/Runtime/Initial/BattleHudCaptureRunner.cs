using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArknoNights.Battle.Demo;
using ArknoNights.Player;
using ArknoNights.Round;
using ArknoNights.UI;
using ArknoNights.UI.FormalHud.ShopReady;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player-only formal battle HUD evidence entry. Every image is accompanied by the authoritative display,
/// phase, match and track data that produced it; images remain visual evidence, not battle proof.
/// </summary>
public sealed class BattleHudCaptureRunner : MonoBehaviour
{
    private readonly List<BattleHudCaptureRecord> captures = new List<BattleHudCaptureRecord>();
    private string outputDirectory;
    private StagingHudController hud;
    private PreparationBattleLoopController loop;
    private BattleHudSceneCoordinator integration;
    private ShopReadyHudController shop;
    private FormalBattleHudController formal;

    private void Awake()
    {
        if (!Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "-battleHudCapture", StringComparison.OrdinalIgnoreCase)))
        {
            Destroy(this);
            return;
        }
        outputDirectory = Path.GetFullPath(CommandLineValue("-uiCaptureOutput") ?? Path.Combine(Application.dataPath, "..", "BattleHudCaptures"));
        Directory.CreateDirectory(outputDirectory);
        StartCoroutine(Capture());
    }

    private IEnumerator Capture()
    {
        for (var frame = 0; frame < 180; frame++)
        {
            hud = FindObjectOfType<StagingHudController>();
            loop = hud == null ? null : hud.GetComponent<PreparationBattleLoopController>();
            integration = hud == null ? null : hud.GetComponent<BattleHudSceneCoordinator>();
            formal = hud == null ? null : hud.GetComponent<FormalBattleHudController>();
            shop = integration == null ? null : integration.ShopReady;
            if (hud != null && hud.InitializationSucceeded && loop != null && integration != null && integration.IsInitialized && formal != null && shop != null) break;
            yield return null;
        }
        if (hud == null || !hud.InitializationSucceeded || loop == null || integration == null || !integration.IsInitialized || formal == null || shop == null)
        {
            Fail("dependencies.missing");
            yield break;
        }

        yield return SetResolution(1920, 1080);
        shop.SetShopVisible(false);
        formal.ClearSelectionForSceneTransition();
        yield return CaptureOne("01_preparation_closed");

        shop.SetShopVisible(true);
        yield return CaptureOne("02_shop_open");

        shop.RequestUpgrade();
        yield return CaptureOne("03_shop_upgrade_confirmation");
        shop.SetShopVisible(false);
        shop.SetShopVisible(true);

        // The fixture has seven gold. Five real refresh commands leave two gold and land on the
        // mixed page, which contains an actual rarity-four (price 4) unaffordable product.
        for (var refresh = 0; refresh < 5; refresh++) loop.MatchState.TryRefresh();
        yield return CaptureOne("04_shop_unaffordable");

        shop.Purchase(0);
        yield return CaptureOne("05_shop_purchase_confirmation");
        shop.ToggleAllFrozen();
        yield return CaptureOne("06_shop_frozen");
        shop.Purchase(0);
        shop.Purchase(0);
        yield return CaptureOne("07_shop_purchase_empty_slot");
        shop.ToggleReady();
        yield return CaptureOne("08_ready_shop_still_available");

        // Observation stays a display choice.  The remote unit is selected through the existing
        // staging HUD selection path so the information panel and player-list visibility agree.
        integration.TryObservePlayer("local-ui-player-2");
        yield return CaptureOne("09_observe_remote_player");
        var remoteSlot = hud.DisplayedSnapshot.StagingSlots.FirstOrDefault();
        if (remoteSlot != null) hud.ToggleSelection(StagingHudController.BuildSlotId(remoteSlot));
        if (string.IsNullOrEmpty(formal.SelectedUnitId))
        {
            var remoteUnit = hud.DisplayedSnapshot.Units.FirstOrDefault();
            if (remoteUnit != null) formal.SelectObservedPreparationUnitForHud(remoteUnit.UnitId);
        }
        yield return CaptureOne("10_observe_selected_unit_list_hidden");

        formal.ClearSelectionForSceneTransition();
        integration.TryObservePlayer(loop.MatchState.Snapshot.LocalPlayerId);
        yield return CaptureOne("11_player_disconnect_and_exit_list");
        if (loop.MatchState.Snapshot.LocalPlayer.IsReady) shop.ToggleReady();
        loop.AdvanceForTests(PreparationBattlePhaseMachine.PreparationDurationSeconds);
        yield return null;
        shop.SetShopVisible(true);
        yield return CaptureOne("12_battle_match_ab_home");
        shop.SetShopVisible(false);
        integration.TryObservePlayer("local-ui-player-2");
        yield return CaptureOne("13_battle_match_ab_away");
        integration.TryObservePlayer("local-ui-player-3");
        yield return CaptureOne("14_battle_match_cd_home");
        integration.TryObservePlayer("local-ui-player-4");
        yield return CaptureOne("15_battle_match_cd_away");

        // Freeze the one authoritative presentation clock before comparing the two matches.
        // Screenshot file I/O spans several rendered frames, so an unpaused clock would make
        // the two manifest records describe different battle instants.
        var multiBattle = loop.MultiBattle;
        if (multiBattle == null || !multiBattle.Pause())
        {
            Fail("shared-clock.pause.failed");
            yield break;
        }
        integration.TryObservePlayer(loop.MatchState.Snapshot.LocalPlayerId);
        yield return CaptureOne("16_same_tick_match_ab");
        integration.TryObservePlayer("local-ui-player-3");
        yield return CaptureOne("17_same_tick_match_cd");

        File.WriteAllText(Path.Combine(outputDirectory, "battle-hud-manifest.json"), JsonUtility.ToJson(new BattleHudCaptureManifest { captures = captures.ToArray() }, true));
        Debug.Log("[BattleHudCapture][completed] count=" + captures.Count + "; output=" + outputDirectory, this);
        Application.Quit(0);
    }

    private IEnumerator CaptureOne(string name)
    {
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        var path = Path.Combine(outputDirectory, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path, 1);
        byte[] bytes = null;
        for (var frame = 0; frame < 600 && bytes == null; frame++)
        {
            if (File.Exists(path))
            {
                try { bytes = File.ReadAllBytes(path); }
                catch (IOException) { }
            }
            if (bytes == null) yield return null;
        }
        if (bytes == null) { Fail("screenshot.timeout:" + name); yield break; }
        var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var valid = ImageConversion.LoadImage(probe, bytes, false);
        var width = probe.width;
        var height = probe.height;
        if (valid)
        {
            var pixels = probe.GetPixels32();
            valid = pixels.Count(pixel => pixel.r > 4 || pixel.g > 4 || pixel.b > 4) > pixels.Length / 200;
        }
        Destroy(probe);
        if (!valid) { Fail("screenshot.invalid:" + name); yield break; }

        var match = loop.MatchState.Snapshot;
        var multi = loop.MultiBattle;
        captures.Add(new BattleHudCaptureRecord
        {
            name = name,
            path = path,
            width = width,
            height = height,
            phase = loop.Phase.ToString(),
            localPlayerId = match.LocalPlayerId,
            observedPlayerId = match.ObservedPlayerId,
            selectedUnitId = formal.SelectedUnitId ?? string.Empty,
            preparationRemainingSeconds = loop.RemainingPreparationSeconds,
            canvasScale = hud.GetComponentInChildren<Canvas>() == null ? 0f : hud.GetComponentInChildren<Canvas>().scaleFactor,
            shopVisible = shop.State != null && shop.State.ShopVisible,
            ready = match.LocalPlayer.IsReady,
            localGold = match.LocalPlayer.Gold,
            shopSlots = match.LocalPlayer.ShopSlots.Select(slot => slot.ShopSlotId + ":" + (slot.IsEmpty ? "empty" : slot.UnitTypeId) + ":" + slot.Price + ":frozen=" + slot.IsFrozen).ToArray(),
            selectedMatchId = multi == null ? string.Empty : multi.SelectedMatchId,
            battleObserver = multi == null ? string.Empty : multi.Observer.ToString(),
            presentationTick = multi == null ? 0d : multi.PresentationTick,
            trackSummaries = multi == null ? Array.Empty<string>() : multi.Matches.Select(item => item.MatchId + ":" + item.Track.StableSummary + ":moves=" + item.Track.CompressionMetrics.OriginalMoveCount + ":keys=" + item.Track.CompressionMetrics.PositionKeyCount + ":ratio=" + item.Track.CompressionMetrics.CompressionRatio.ToString("R")).ToArray(),
            rects = new[]
            {
                Rect("levelButton", hud.transform.Find("FormalBattleHudCanvas/ShopReadyHud/ShopLevelButton") as RectTransform),
                Rect("shopPanel", hud.transform.Find("FormalBattleHudCanvas/ShopReadyHud/ShopPanel") as RectTransform),
                Rect("readyButton", hud.transform.Find("FormalBattleHudCanvas/ShopReadyHud/ReadyButton") as RectTransform),
                Rect("playerOne", hud.transform.Find("FormalBattleHudCanvas/PlayerListPanel/Player_local-ui-player") as RectTransform),
                Rect("playerFour", hud.transform.Find("FormalBattleHudCanvas/PlayerListPanel/Player_local-ui-player-4") as RectTransform),
                Rect("information", hud.transform.Find("FormalBattleHudCanvas/FormalHud/UnitInformationPanel") as RectTransform),
                Rect("status", hud.transform.Find("FormalBattleHudCanvas/FormalHud/BattleStatusPanel") as RectTransform)
            }
        });
    }

    private static BattleHudRect Rect(string name, RectTransform transform)
    {
        if (transform == null) return new BattleHudRect { name = name };
        var corners = new Vector3[4];
        transform.GetWorldCorners(corners);
        var bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        var topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        return new BattleHudRect { name = name, x = bottomLeft.x, y = Screen.height - topRight.y, width = topRight.x - bottomLeft.x, height = topRight.y - bottomLeft.y };
    }

    private static IEnumerator SetResolution(int width, int height)
    {
        Screen.SetResolution(width, height, false);
        for (var frame = 0; frame < 180 && (Screen.width != width || Screen.height != height); frame++) yield return null;
    }

    private static string CommandLineValue(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index + 1 < args.Length; index++) if (args[index] == flag) return args[index + 1];
        return null;
    }

    private void Fail(string detail)
    {
        Debug.LogError("[BattleHudCapture][failed] " + detail, this);
        if (!string.IsNullOrEmpty(outputDirectory))
            File.WriteAllText(Path.Combine(outputDirectory, "battle-hud-capture-failed.txt"), detail + Environment.NewLine);
        Application.Quit(1);
    }

    [Serializable] private sealed class BattleHudCaptureManifest { public BattleHudCaptureRecord[] captures; }
    [Serializable] private sealed class BattleHudCaptureRecord { public string name; public string path; public int width; public int height; public string phase; public string localPlayerId; public string observedPlayerId; public string selectedUnitId; public float preparationRemainingSeconds; public float canvasScale; public bool shopVisible; public bool ready; public int localGold; public string[] shopSlots; public string selectedMatchId; public string battleObserver; public double presentationTick; public string[] trackSummaries; public BattleHudRect[] rects; }
    [Serializable] private sealed class BattleHudRect { public string name; public float x; public float y; public float width; public float height; }
}

internal static class BattleHudCaptureBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        if (!Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "-battleHudCapture", StringComparison.OrdinalIgnoreCase))) return;
        var holder = new GameObject("BattleHudCaptureRunner");
        UnityEngine.Object.DontDestroyOnLoad(holder);
        holder.AddComponent<BattleHudCaptureRunner>();
    }
}
