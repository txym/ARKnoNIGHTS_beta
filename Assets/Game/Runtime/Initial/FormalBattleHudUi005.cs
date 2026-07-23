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
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// UI-005's scene-local, read-only projection. It deliberately owns no PlayerState, clock, Core runner,
/// or presentation object: the existing UI-002/003/004 components remain the command and data owners.
/// </summary>
[DisallowMultipleComponent]
public sealed class FormalBattleHudUi005 : MonoBehaviour
{
    private const string AtlasPath = "UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged";
    private const string ClockPath = "UI/Texture/BattleStatusPanelClockIcon_Transparent";
    private const string GoldIconPath = "UI/Texture/round_sources_icon";
    private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    private StagingHudController hud;
    private StateDrivenDeploymentController deployment;
    private PreparationBattleLoopController loop;
    private BattleDemoController demo;
    private UnitCatalog catalog;
    private RectTransform root;
    private RectTransform infoPanel;
    private Image portrait;
    private Text unitName;
    private Text elite;
    private Image hpFill;
    private RectTransform hpValueRoot;
    private Text hpValue;
    private Text statusLeft;
    private Image statusEnemyIcon;
    private Image statusClockIcon;
    private Text statusMiddle;
    private Text statusRight;
    private string selectedUnitId;
    private bool selectedBattleEnemy;
    private bool initialized;

    public string SelectedUnitId => selectedUnitId;
    public bool SelectedBattleEnemy => selectedBattleEnemy;

    private IEnumerator Start()
    {
        for (var frame = 0; frame < 12; frame++)
        {
            hud = GetComponent<StagingHudController>();
            deployment = GetComponent<StateDrivenDeploymentController>();
            loop = GetComponent<PreparationBattleLoopController>();
            demo = FindObjectOfType<BattleDemoController>();
            if (hud != null && hud.InitializationSucceeded && deployment != null && loop != null && demo != null) break;
            yield return null;
        }
        if (hud == null || !hud.InitializationSucceeded || deployment == null || loop == null || demo == null)
        {
            Debug.LogError("[UI-005][hud.dependencies.missing]", this);
            yield break;
        }
        var catalogLoad = UnitCatalogLoader.LoadFromResources("BattleData/unit-catalog-v1");
        if (!catalogLoad.Success) { Debug.LogError("[UI-005][catalog.load.failed]", this); yield break; }
        catalog = catalogLoad.Catalog;
        Build();
        hud.StagingSelectionChanged += SelectStagingSlot;
        deployment.DeployedSelectionChanged += SelectDeployed;
        initialized = true;
        Refresh();
    }

    private void Update()
    {
        if (!initialized) return;
        if (loop.Phase == LocalBattlePhase.Battle && Input.GetMouseButtonUp(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            var camera = Camera.main;
            if (camera != null)
            {
                foreach (var hit in Physics.RaycastAll(camera.ScreenPointToRay(Input.mousePosition), 2000f).OrderBy(item => item.distance))
                {
                    var view = hit.collider.GetComponentInParent<UnitSkelPresentationView>();
                    if (view == null || !view.name.StartsWith("BattleView_", StringComparison.Ordinal)) continue;
                    SelectBattleUnitForHud(view.name.Substring("BattleView_".Length));
                    break;
                }
            }
        }
        Refresh();
    }

    private void OnDestroy()
    {
        if (hud != null) hud.StagingSelectionChanged -= SelectStagingSlot;
        if (deployment != null) deployment.DeployedSelectionChanged -= SelectDeployed;
    }

    /// <summary>Read-only battle selection entry also used by PlayMode tests; enemy selection never routes a command.</summary>
    public void SelectBattleUnitForHud(string unitId)
    {
        if (loop == null || loop.Phase != LocalBattlePhase.Battle || string.IsNullOrEmpty(unitId)) return;
        var state = demo.Coordinator?.PresentationViewStates.FirstOrDefault(item => item.UnitId == unitId);
        if (state == null) return;
        // Staging and battlefield inspection share one selection. Clearing the former must not grant
        // the selected enemy any command authority; it only moves the read-only information projection.
        hud?.ClearStagingSelection();
        selectedUnitId = unitId;
        selectedBattleEnemy = state.Side != BattleSide.Home;
        Refresh();
    }

    private void SelectStagingSlot(string slotId)
    {
        if (string.IsNullOrEmpty(slotId) || hud?.Snapshot == null) { if (!selectedBattleEnemy) ClearSelection(); return; }
        var slot = hud.Snapshot.StagingSlots.FirstOrDefault(item => StagingHudController.BuildSlotId(item) == slotId);
        if (slot == null || slot.UnitIds.Count == 0) return;
        selectedUnitId = slot.UnitIds[0];
        selectedBattleEnemy = false;
        Refresh();
    }

    private void SelectDeployed(string unitId)
    {
        if (string.IsNullOrEmpty(unitId)) { if (!selectedBattleEnemy) ClearSelection(); return; }
        selectedUnitId = unitId;
        selectedBattleEnemy = false;
        Refresh();
    }

    private void ClearSelection()
    {
        selectedUnitId = null;
        selectedBattleEnemy = false;
        if (infoPanel) infoPanel.gameObject.SetActive(false);
    }

    private void Build()
    {
        foreach (var sprite in Resources.LoadAll<Sprite>(AtlasPath)) if (sprite != null) sprites[sprite.name] = sprite;
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { Debug.LogError("[UI-005][canvas.missing]", this); return; }
        root = Rect("FormalHudUi005", canvas.transform);
        root.SetAsFirstSibling(); // all UI-005 elements remain behind the pre-existing staging command UI.
        Stretch(root);
        BuildInformationPanel();
        BuildStatusPanel();
        BuildGoldPanel();
        BuildSettingsButton();
    }

    private void BuildGoldPanel()
    {
        var panel = Rect("GoldCurrencyPanel", root);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f); panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(0f, 300f); panel.sizeDelta = new Vector2(180f, 80f);
        var background = Image("Background", panel, Sprite("ResourcePanelBackground")); Stretch(background.rectTransform); background.preserveAspect = false;
        // Gold deliberately uses the same left-icon/right-value anchors as DeploymentCostPanel.
        // Its data is not defined yet, so only the value itself remains the explicit "--" placeholder.
        var icon = Image("Icon", panel, Resources.Load<Sprite>(GoldIconPath)); icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = Vector2.zero; icon.rectTransform.pivot = new Vector2(.5f, .5f); icon.rectTransform.anchoredPosition = new Vector2(40f, 40f); icon.rectTransform.sizeDelta = new Vector2(48f, 37f); icon.preserveAspect = true;
        var value = NumberText("Value", panel, 54, TextAnchor.MiddleCenter, new Color(1f, .82f, .15f)); value.text = "--"; value.rectTransform.anchorMin = value.rectTransform.anchorMax = Vector2.zero; value.rectTransform.pivot = new Vector2(.5f, .5f); value.rectTransform.anchoredPosition = new Vector2(116f, 35f); value.rectTransform.sizeDelta = new Vector2(90f, 54f);
    }

    private void BuildSettingsButton()
    {
        var buttonRoot = new GameObject("SettingsButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonRoot.transform.SetParent(root, false);
        // The sprite carries a shadow margin; these anchors align its visible gear body with the reference's safe inset.
        var rect = buttonRoot.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = new Vector2(24f, -13f); rect.sizeDelta = new Vector2(187f, 161f);
        var image = buttonRoot.GetComponent<Image>(); image.sprite = Sprite("SettingsButtonIcon"); image.preserveAspect = true;
        var button = buttonRoot.GetComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => Debug.Log("[UI-005][settings.notImplemented]", this));
    }

    private void BuildStatusPanel()
    {
        var panel = Rect("BattleStatusPanel", root);
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, 1f); panel.pivot = new Vector2(.5f, 1f); panel.anchoredPosition = new Vector2(-8f, -14f); panel.sizeDelta = new Vector2(830f, 54f);
        var background = Image("Background", panel, Sprite("BattleStatusPanelBackground")); Stretch(background.rectTransform); background.preserveAspect = false;
        statusEnemyIcon = Image("EnemyIcon", panel, Sprite("BattleStatusPanelEnemyCountIcon")); statusEnemyIcon.rectTransform.anchorMin = statusEnemyIcon.rectTransform.anchorMax = new Vector2(.07f, .5f); statusEnemyIcon.rectTransform.sizeDelta = new Vector2(68f, 60f); statusEnemyIcon.preserveAspect = true;
        statusLeft = Text("Left", panel, 28, TextAnchor.MiddleCenter, Color.white); Position(statusLeft.rectTransform, .22f, .5f, 160f, 50f);
        // The source slice is 22 px, but the reference status bar presents the clock at a 48 px visual size.
        statusClockIcon = Image("Clock", panel, Resources.Load<Sprite>(ClockPath)); statusClockIcon.rectTransform.anchorMin = statusClockIcon.rectTransform.anchorMax = new Vector2(.42f, .5f); statusClockIcon.rectTransform.sizeDelta = new Vector2(48f, 48f); statusClockIcon.color = new Color(.72f, .72f, .72f, 1f);
        statusMiddle = Text("Middle", panel, 28, TextAnchor.MiddleCenter, Color.white); Position(statusMiddle.rectTransform, .57f, .5f, 120f, 50f);
        var health = Image("PlayerHealthIcon", panel, Sprite("BattleStatusPanelPlayerHealthIcon")); health.rectTransform.anchorMin = health.rectTransform.anchorMax = new Vector2(.75f, .5f); health.rectTransform.sizeDelta = new Vector2(60f, 53f); health.preserveAspect = true;
        statusRight = NumberText("PlayerHealth", panel, 28, TextAnchor.MiddleCenter, new Color(1f, .47058824f, .47058824f)); Position(statusRight.rectTransform, .89f, .5f, 80f, 50f); statusRight.text = "--";
    }

    private void BuildInformationPanel()
    {
        infoPanel = Rect("UnitInformationPanel", root); infoPanel.anchorMin = new Vector2(0f, 0f); infoPanel.anchorMax = new Vector2(0f, 1f); infoPanel.pivot = new Vector2(0f, 0f); infoPanel.sizeDelta = new Vector2(720f, 0f);
        var upper = Image("UpperBackground", infoPanel, Sprite("UnitInformationPanelUpperBackground")); upper.rectTransform.anchorMin = new Vector2(0f, .475f); upper.rectTransform.anchorMax = Vector2.one; upper.rectTransform.offsetMin = upper.rectTransform.offsetMax = Vector2.zero; upper.preserveAspect = false;
        var lower = Image("LowerBackground", infoPanel, Sprite("UnitInformationPanelLowerBackground")); lower.rectTransform.anchorMin = Vector2.zero; lower.rectTransform.anchorMax = new Vector2(1f, .475f); lower.rectTransform.offsetMin = lower.rectTransform.offsetMax = Vector2.zero; lower.preserveAspect = false;
        portrait = Image("Portrait", infoPanel, null); Position(portrait.rectTransform, .2f, .79f, 180f, 180f); portrait.preserveAspect = true;
        unitName = Text("UnitName", infoPanel, 25, TextAnchor.MiddleLeft, Color.white); Position(unitName.rectTransform, .48f, .86f, 320f, 45f);
        elite = Text("Elite", infoPanel, 18, TextAnchor.MiddleLeft, new Color(.95f, .82f, .3f)); Position(elite.rectTransform, .48f, .8f, 320f, 38f);
        // Fig. 2–4 use the native 556×12 bar and place it at the lower edge of the unit overview.
        var hpBack = Image("HealthBackground", infoPanel, Sprite("UnitInformationPanelHealthBarBackground")); hpBack.rectTransform.anchorMin = hpBack.rectTransform.anchorMax = new Vector2(0f, 1f); hpBack.rectTransform.pivot = new Vector2(0f, 1f); hpBack.rectTransform.anchoredPosition = new Vector2(0f, -490f); hpBack.rectTransform.sizeDelta = new Vector2(556f, 12f); hpBack.preserveAspect = false;
        hpFill = Image("HealthFill", hpBack.rectTransform, Sprite("UnitInformationPanelHealthBarFill")); hpFill.rectTransform.anchorMin = hpFill.rectTransform.anchorMax = new Vector2(0f, .5f); hpFill.rectTransform.pivot = new Vector2(0f, .5f); hpFill.rectTransform.sizeDelta = new Vector2(556f, 12f); hpFill.preserveAspect = false;
        hpValueRoot = Rect("HealthValue", infoPanel); hpValueRoot.anchorMin = hpValueRoot.anchorMax = new Vector2(0f, 1f); hpValueRoot.pivot = new Vector2(0f, 1f); hpValueRoot.anchoredPosition = new Vector2(556f, -490f); hpValueRoot.sizeDelta = new Vector2(149f, 50f);
        var valueBack = Image("Background", hpValueRoot, Sprite("UnitInformationPanelHealthValueBackground")); Stretch(valueBack.rectTransform); valueBack.preserveAspect = false;
        hpValue = NumberText("Text", hpValueRoot, 24, TextAnchor.MiddleCenter, Color.white); Stretch(hpValue.rectTransform); hpValue.rectTransform.offsetMax = new Vector2(0f, -5f);
        foreach (var label in new[] { "技能", "阵营", "种族" }) { var tab = Text("Tab_" + label, infoPanel, 20, TextAnchor.MiddleCenter, new Color(.65f, .65f, .65f)); Position(tab.rectTransform, .18f + Array.IndexOf(new[] { "技能", "阵营", "种族" }, label) * .22f, .44f, 130f, 38f); tab.text = label + "\n未接入"; }
        infoPanel.gameObject.SetActive(false);
    }

    private void Refresh()
    {
        if (!initialized || root == null) return;
        var battle = loop.Phase == LocalBattlePhase.Battle;
        statusEnemyIcon.gameObject.SetActive(battle);
        if (battle)
        {
            var states = demo.Coordinator?.PresentationViewStates ?? Array.Empty<BattlePresentationViewState>();
            var initialEnemies = demo.Coordinator?.Input?.Players.Where(player => player.Side == BattleSide.Away).SelectMany(player => player.Units).Where(unit => unit.Zone == UnitZone.Deployed).Select(unit => unit.UnitId).ToArray() ?? Array.Empty<string>();
            var defeated = states.Count(state => initialEnemies.Contains(state.UnitId) && !state.IsAlive);
            statusLeft.font = StagingHudController.FormalNumericFont;
            statusLeft.text = defeated + "/" + initialEnemies.Length;
            statusClockIcon.gameObject.SetActive(false);
            statusMiddle.font = StagingHudController.FormalUiFont;
            statusMiddle.text = demo.State.ToString();
        }
        else
        {
            statusLeft.font = StagingHudController.FormalUiFont;
            statusLeft.text = hud.PlayerState.PlayerId == "" ? "对手" : "对手: 固定测试";
            statusClockIcon.gameObject.SetActive(true);
            statusMiddle.font = StagingHudController.FormalNumericFont;
            statusMiddle.text = Mathf.CeilToInt(loop.RemainingPreparationSeconds).ToString();
        }
        statusRight.text = "--";
        RefreshInformation();
    }

    private void RefreshInformation()
    {
        if (string.IsNullOrEmpty(selectedUnitId)) { if (infoPanel) infoPanel.gameObject.SetActive(false); return; }
        string typeId = null; int currentHp = 0; int maxHp = 0; int? eliteLevel = null;
        var player = hud?.Snapshot?.Units.FirstOrDefault(item => item.UnitId == selectedUnitId);
        if (selectedBattleEnemy)
        {
            var state = demo.Coordinator?.PresentationViewStates.FirstOrDefault(item => item.UnitId == selectedUnitId);
            if (state == null) { ClearSelection(); return; }
            typeId = state.TypeId; currentHp = state.HitPoints;
        }
        else if (player != null && player.Zone == PlayerUnitZone.Staging)
        {
            // A staging unit remains inspectable during Battle even though it has no presentation view state.
            typeId = player.TypeId;
            eliteLevel = player.EliteLevel;
        }
        else if (loop.Phase == LocalBattlePhase.Battle)
        {
            var state = demo.Coordinator?.PresentationViewStates.FirstOrDefault(item => item.UnitId == selectedUnitId);
            if (state == null) { ClearSelection(); return; }
            typeId = state.TypeId;
            currentHp = state.HitPoints;
            if (player != null) eliteLevel = player.EliteLevel;
        }
        else
        {
            if (player == null) { ClearSelection(); return; }
            typeId = player.TypeId; eliteLevel = player.EliteLevel;
        }
        if (!catalog.TryGet(typeId, out var entry)) { ClearSelection(); return; }
        maxHp = entry.Definition.MaxHitPoints; if (currentHp == 0 && loop.Phase != LocalBattlePhase.Battle) currentHp = maxHp;
        infoPanel.gameObject.SetActive(true);
        portrait.sprite = Resources.Load<Sprite>(entry.PortraitResourcePath); portrait.enabled = portrait.sprite != null;
        unitName.text = (string.IsNullOrEmpty(entry.DisplayNameZhHans) ? "未配置" : entry.DisplayNameZhHans) + " (" + typeId + ")";
        elite.text = eliteLevel.HasValue ? "精英化 " + eliteLevel.Value : "精英化: 未知";
        var ratio = maxHp <= 0 ? 0f : Mathf.Clamp01((float)currentHp / maxHp);
        hpFill.rectTransform.sizeDelta = new Vector2(556f * ratio, 12f);
        hpValueRoot.anchoredPosition = new Vector2(556f * ratio, -490f);
        var valueText = Mathf.Clamp(currentHp, 0, maxHp) + "/" + maxHp;
        hpValue.fontSize = valueText.Length > 9 ? 20 : 24;
        hpValue.text = valueText;
    }

    private Sprite Sprite(string name) { sprites.TryGetValue(name, out var sprite); return sprite; }
    private static Text NumberText(string name, Transform parent, int size, TextAnchor alignment, Color color) { var text = Text(name, parent, size, alignment, color); text.font = StagingHudController.FormalNumericFont; return text; }
    private static RectTransform Rect(string name, Transform parent) { var value = new GameObject(name, typeof(RectTransform)); value.transform.SetParent(parent, false); return value.GetComponent<RectTransform>(); }
    private static Image Image(string name, Transform parent, Sprite sprite) { var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); value.transform.SetParent(parent, false); var image = value.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = false; return image; }
    private static Text Text(string name, Transform parent, int size, TextAnchor alignment, Color color) { var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)); value.transform.SetParent(parent, false); var text = value.GetComponent<Text>(); text.font = StagingHudController.FormalUiFont; text.fontStyle = FontStyle.Normal; text.resizeTextForBestFit = false; text.fontSize = size; text.alignment = alignment; text.color = color; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow; return text; }
    private static void Stretch(RectTransform target) { target.anchorMin = Vector2.zero; target.anchorMax = Vector2.one; target.offsetMin = target.offsetMax = Vector2.zero; }
    private static void Position(RectTransform target, float x, float y, float width, float height) { target.anchorMin = target.anchorMax = new Vector2(x, y); target.pivot = new Vector2(.5f, .5f); target.sizeDelta = new Vector2(width, height); }
}

internal static class FormalBattleHudUi005Bootstrap
{
    private static bool subscribed;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Attach()
    {
        if (!subscribed) { SceneManager.sceneLoaded += (_, __) => AttachToLoadedScene(); subscribed = true; }
        AttachToLoadedScene();
    }
    private static void AttachToLoadedScene()
    {
        var hud = UnityEngine.Object.FindObjectOfType<StagingHudController>();
        if (hud != null && hud.GetComponent<FormalBattleHudUi005>() == null) hud.gameObject.AddComponent<FormalBattleHudUi005>();
    }
}
