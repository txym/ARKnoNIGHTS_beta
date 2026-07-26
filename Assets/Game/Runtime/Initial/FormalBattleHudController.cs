using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using ArknoNights.Deployment;
using ArknoNights.Details;
using ArknoNights.Player;
using ArknoNights.Round;
using ArknoNights.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Scene-local, read-only formal battle HUD projection. It deliberately owns no PlayerState, clock, Core runner,
/// or presentation object: the existing UI-002/003/004 components remain the command and data owners.
/// </summary>
[DisallowMultipleComponent]
public sealed class FormalBattleHudController : MonoBehaviour
{
    private const string AtlasPath = "UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged";
    private const string ClockPath = "UI/Texture/BattleStatusPanelClockIcon_Transparent";
    private const string GoldIconPath = "UI/Texture/round_sources_icon";
    private static readonly Color InformationEntryBackgroundTint = new Color(.32f, .32f, .32f, .94f);
    private static readonly Color PlayerHealthTextColor = new Color(1f, .47058824f, .47058824f);
    private static readonly IReadOnlyDictionary<string, InformationIconSource> InformationIcons = new Dictionary<string, InformationIconSource>(StringComparer.Ordinal)
    {
        { "maxHp", new InformationIconSource("UI/Texture/UnitInformationPanelHealthIcon", "UnitInformationPanelHealthIcon") },
        { "moveSpeed", new InformationIconSource(null, "UnitInformationPanelMoveSpeedIcon") },
        { "attack", new InformationIconSource("UI/Texture/UnitInformationPanelAttackIcon", "UnitInformationPanelAttackIcon") },
        { "attackInterval", new InformationIconSource("UI/Texture/UnitInformationPanelAttackIntervalIcon", "UnitInformationPanelAttackIntervalIcon") },
        { "defense", new InformationIconSource("UI/Texture/UnitInformationPanelDefenseIcon", "UnitInformationPanelDefenseIcon") },
        { "magicResistance", new InformationIconSource("UI/Texture/UnitInformationPanelMagicResistanceIcon", "UnitInformationPanelMagicResistanceIcon") },
        { "block", new InformationIconSource("UI/Texture/UnitInformationPanelBlockCountIcon", "UnitInformationPanelBlockCountIcon") },
        { "deploymentCost", new InformationIconSource("UI/Texture/UnitInformationPanelDeploymentCostIcon", "UnitInformationPanelDeploymentCostIcon") },
        { "targetValue", new InformationIconSource(null, "UnitInformationPanelTargetValueIcon") }
    };
    private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    private readonly Dictionary<int, Sprite> raritySprites = new Dictionary<int, Sprite>();
    private readonly Dictionary<string, Sprite> informationIcons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    private StagingHudController hud;
    private StateDrivenDeploymentController deployment;
    private PreparationBattleLoopController loop;
    private BattleDemoController demo;
    private UnitCatalog catalog;
    private RectTransform root;
    private RectTransform infoPanel;
    private Image portrait;
    private Image rarityIcon;
    private Image eliteIcon;
    private Text unitName;
    private Text combatSummary;
    private Text targetValue;
    private readonly Dictionary<string, Text> statValues = new Dictionary<string, Text>(StringComparer.Ordinal);
    private readonly HashSet<string> reportedDetailDiagnostics = new HashSet<string>(StringComparer.Ordinal);
    private Image hpFill;
    private RectTransform hpValueRoot;
    private Text hpValue;
    private Text statusLeft;
    private Image statusEnemyIcon;
    private Image statusClockIcon;
    private Text statusMiddle;
    private Text statusRight;
    private Text goldValue;
    private int sessionGold;
    private int sessionLife;
    private string preparationOpponent = "固定测试";
    private string selectedUnitId;
    private string visualFixtureId;
    private bool selectedBattleEnemy;
    private bool initialized;

    public string SelectedUnitId => selectedUnitId;
    public bool SelectedBattleEnemy => selectedBattleEnemy;
    /// <summary>Raised when the single HUD selection becomes visible or is cleared; the scene coordinator uses it to hide/show the player list.</summary>
    public event Action<bool> SelectionChanged;

    /// <summary>
    /// The scene coordinator supplies the locally owned session values. This presentation component stores only
    /// their last read-only projection; it never writes economy, life, readiness, or observation.
    /// </summary>
    public void SetSessionHudValues(int gold, int life, string nextOpponent)
    {
        sessionGold = Math.Max(0, gold);
        sessionLife = Math.Max(0, life);
        preparationOpponent = string.IsNullOrWhiteSpace(nextOpponent) ? "固定测试" : nextOpponent;
        Refresh();
    }

    /// <summary>Capture/test-only layout data. It never writes PlayerState, the unit catalog, or battle input.</summary>
    public void ShowVisualFixtureForCapture(string fixtureId)
    {
        if (!string.Equals(fixtureId, "empty-name", StringComparison.Ordinal) && !string.Equals(fixtureId, "medium-name", StringComparison.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(fixtureId));
        visualFixtureId = fixtureId;
        Refresh();
    }

    /// <summary>Returns rendering to the selected production unit after an explicit capture/test fixture.</summary>
    public void ClearVisualFixtureForCapture()
    {
        visualFixtureId = null;
        Refresh();
    }

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
            Debug.LogError("[FormalBattleHud][dependencies.missing]", this);
            yield break;
        }
        var catalogLoad = UnitCatalogLoader.LoadFromResources("BattleData/unit-catalog-v1");
        if (!catalogLoad.Success) { Debug.LogError("[FormalBattleHud][catalog.load.failed]", this); yield break; }
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
        var state = CurrentBattleStates().FirstOrDefault(item => item.UnitId == unitId);
        if (state == null) return;
        // Staging and battlefield inspection share one selection. Clearing the former must not grant
        // the selected enemy any command authority; it only moves the read-only information projection.
        hud?.ClearStagingSelection();
        selectedUnitId = unitId;
        selectedBattleEnemy = state.Side != CurrentObserverSide();
        SelectionChanged?.Invoke(true);
        Refresh();
    }

    /// <summary>Read-only preparation inspection for the player currently displayed by the observation coordinator.</summary>
    public void SelectObservedPreparationUnitForHud(string unitId)
    {
        if (loop == null || loop.Phase != LocalBattlePhase.Preparation || string.IsNullOrWhiteSpace(unitId)) return;
        var displayed = hud?.DisplayedSnapshot;
        if (displayed == null || !displayed.Units.Any(unit => string.Equals(unit.UnitId, unitId, StringComparison.Ordinal))) return;
        hud?.ClearStagingSelection();
        selectedUnitId = unitId;
        selectedBattleEnemy = false;
        SelectionChanged?.Invoke(true);
        Refresh();
    }

    /// <summary>Explicit lifecycle boundary for phase and observation changes.</summary>
    public void ClearSelectionForSceneTransition() => ClearSelection();

    private void SelectStagingSlot(string slotId)
    {
        if (string.IsNullOrEmpty(slotId) || hud?.DisplayedSnapshot == null) { if (!selectedBattleEnemy) ClearSelection(); return; }
        var slot = hud.DisplayedSnapshot.StagingSlots.FirstOrDefault(item => StagingHudController.BuildSlotId(item) == slotId);
        if (slot == null || slot.UnitIds.Count == 0) return;
        selectedUnitId = slot.UnitIds[0];
        selectedBattleEnemy = false;
        SelectionChanged?.Invoke(true);
        Refresh();
    }

    private void SelectDeployed(string unitId)
    {
        if (string.IsNullOrEmpty(unitId)) { if (!selectedBattleEnemy) ClearSelection(); return; }
        selectedUnitId = unitId;
        selectedBattleEnemy = false;
        SelectionChanged?.Invoke(true);
        Refresh();
    }

    private void ClearSelection()
    {
        var hadSelection = !string.IsNullOrEmpty(selectedUnitId);
        selectedUnitId = null;
        selectedBattleEnemy = false;
        if (infoPanel) infoPanel.gameObject.SetActive(false);
        if (hadSelection) SelectionChanged?.Invoke(false);
    }

    private void Build()
    {
        foreach (var sprite in Resources.LoadAll<Sprite>(AtlasPath)) if (sprite != null) sprites[sprite.name] = sprite;
        foreach (var sprite in Resources.LoadAll<Sprite>("UI/Texture/unit_panal")) if (sprite != null) sprites[sprite.name] = sprite;
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { Debug.LogError("[FormalBattleHud][canvas.missing]", this); return; }
        root = Rect("FormalHud", canvas.transform);
        root.SetAsFirstSibling(); // Formal HUD elements remain behind the pre-existing staging command UI.
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
        // The scene coordinator supplies its read-only value from the loop-owned LocalMatchState.
        var icon = Image("Icon", panel, Resources.Load<Sprite>(GoldIconPath)); icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = Vector2.zero; icon.rectTransform.pivot = new Vector2(.5f, .5f); icon.rectTransform.anchoredPosition = new Vector2(40f, 40f); icon.rectTransform.sizeDelta = new Vector2(48f, 37f); icon.preserveAspect = true;
        goldValue = NumberText("Value", panel, 54, TextAnchor.MiddleCenter, new Color(1f, .82f, .15f)); goldValue.text = sessionGold.ToString(); goldValue.rectTransform.anchorMin = goldValue.rectTransform.anchorMax = Vector2.zero; goldValue.rectTransform.pivot = new Vector2(.5f, .5f); goldValue.rectTransform.anchoredPosition = new Vector2(116f, 35f); goldValue.rectTransform.sizeDelta = new Vector2(90f, 54f);
    }

    private void BuildSettingsButton()
    {
        var buttonRoot = new GameObject("SettingsButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonRoot.transform.SetParent(root, false);
        // The sprite carries a shadow margin; these anchors align its visible gear body with the reference's safe inset.
        var rect = buttonRoot.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = new Vector2(24f, -13f); rect.sizeDelta = new Vector2(187f, 161f);
        var image = buttonRoot.GetComponent<Image>(); image.sprite = Sprite("SettingsButtonIcon"); image.preserveAspect = true;
        var button = buttonRoot.GetComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => Debug.Log("[FormalBattleHud][settings.notImplemented]", this));
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
        statusRight = NumberText("PlayerHealth", panel, 28, TextAnchor.MiddleCenter, PlayerHealthTextColor); Position(statusRight.rectTransform, .89f, .5f, 80f, 50f); statusRight.text = "--";
    }

    private void BuildInformationPanel()
    {
        infoPanel = Rect("UnitInformationPanel", root); infoPanel.anchorMin = new Vector2(0f, 0f); infoPanel.anchorMax = new Vector2(0f, 1f); infoPanel.pivot = new Vector2(0f, 0f); infoPanel.sizeDelta = new Vector2(720f, 0f);
        var layout = UnitInformationPanelLayout.ForPanelWidth(infoPanel.sizeDelta.x);
        var upper = Image("UpperBackground", infoPanel, Sprite("UnitInformationPanelUpperBackground")); upper.rectTransform.anchorMin = new Vector2(0f, .475f); upper.rectTransform.anchorMax = Vector2.one; upper.rectTransform.offsetMin = upper.rectTransform.offsetMax = Vector2.zero; upper.preserveAspect = false;
        var lower = Image("LowerBackground", infoPanel, Sprite("UnitInformationPanelLowerBackground")); lower.rectTransform.anchorMin = Vector2.zero; lower.rectTransform.anchorMax = new Vector2(1f, .475f); lower.rectTransform.offsetMin = lower.rectTransform.offsetMax = Vector2.zero; lower.preserveAspect = false;
        portrait = Image("Portrait", infoPanel, null); PositionFromTopLeft(portrait.rectTransform, layout.Portrait); portrait.preserveAspect = true;
        rarityIcon = Image("Rarity", portrait.rectTransform, null); rarityIcon.rectTransform.anchorMin = rarityIcon.rectTransform.anchorMax = new Vector2(0f, 1f); rarityIcon.rectTransform.pivot = new Vector2(0f, 1f); rarityIcon.rectTransform.anchoredPosition = Vector2.zero; rarityIcon.rectTransform.sizeDelta = new Vector2(45f, 45f); rarityIcon.preserveAspect = true;
        eliteIcon = Image("Elite", portrait.rectTransform, null); eliteIcon.rectTransform.anchorMin = eliteIcon.rectTransform.anchorMax = new Vector2(0f, 0f); eliteIcon.rectTransform.pivot = new Vector2(0f, 0f); eliteIcon.rectTransform.anchoredPosition = Vector2.zero; eliteIcon.rectTransform.sizeDelta = new Vector2(48f, 40f); eliteIcon.preserveAspect = true;
        unitName = Text("UnitName", infoPanel, 36, TextAnchor.MiddleLeft, Color.white); PositionFromTopLeft(unitName.rectTransform, layout.UnitName);
        combatSummary = Text("CombatSummary", infoPanel, 26, TextAnchor.MiddleLeft, new Color(.8f, .8f, .8f)); PositionFromTopLeft(combatSummary.rectTransform, layout.CombatSummary);
        var target = Rect("TargetValue", infoPanel); PositionFromTopLeft(target, layout.TargetValue);
        var targetBack = Image("Background", target, Sprite("UnitInformationPanelStatEntryBackground")); Stretch(targetBack.rectTransform); targetBack.preserveAspect = false; targetBack.color = InformationEntryBackgroundTint;
        var targetIcon = Image("Icon", target, LoadInformationIcon("targetValue")); PositionFromTopLeft(targetIcon.rectTransform, layout.TargetValueIcon); targetIcon.preserveAspect = true;
        targetValue = NumberText("Value", target, 29, TextAnchor.MiddleRight, PlayerHealthTextColor); PositionFromTopLeft(targetValue.rectTransform, layout.TargetValueValue);
        AddStat("maxHp", "\u751f\u547d\u503c", layout.StatEntry(0, 0), layout);
        AddStat("moveSpeed", "\u79fb\u52a8\u901f\u5ea6", layout.StatEntry(1, 0), layout);
        AddStat("attack", "\u653b\u51fb\u529b", layout.StatEntry(0, 1), layout);
        AddStat("attackInterval", "\u653b\u51fb\u95f4\u9694", layout.StatEntry(1, 1), layout);
        AddStat("defense", "\u9632\u5fa1\u529b", layout.StatEntry(0, 2), layout);
        AddStat("magicResistance", "\u6cd5\u672f\u6297\u6027", layout.StatEntry(1, 2), layout);
        AddStat("block", "\u963b\u6321\u6570", layout.StatEntry(0, 3), layout);
        AddStat("deploymentCost", "\u90e8\u7f72\u8d39\u7528", layout.StatEntry(1, 3), layout);
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
            var states = CurrentBattleStates();
            var observerSide = CurrentObserverSide();
            var initialEnemies = CurrentBattleInput()?.Players.Where(player => player.Side != observerSide).SelectMany(player => player.Units).Where(unit => unit.Zone == UnitZone.Deployed).Select(unit => unit.UnitId).ToArray() ?? Array.Empty<string>();
            var defeated = states.Count(state => initialEnemies.Contains(state.UnitId) && !state.IsAlive);
            statusLeft.font = StagingHudController.FormalNumericFont;
            statusLeft.text = defeated + "/" + initialEnemies.Length;
            statusClockIcon.gameObject.SetActive(false);
            statusMiddle.font = StagingHudController.FormalUiFont;
            statusMiddle.text = loop.MultiBattle == null ? demo.State.ToString() : loop.MultiBattle.State.ToString();
        }
        else
        {
            statusLeft.font = StagingHudController.FormalUiFont;
            statusLeft.text = hud.PlayerState.PlayerId == "" ? "对手" : "对手: " + preparationOpponent;
            statusClockIcon.gameObject.SetActive(true);
            statusMiddle.font = StagingHudController.FormalNumericFont;
            statusMiddle.text = Mathf.CeilToInt(loop.RemainingPreparationSeconds).ToString();
        }
        statusRight.text = sessionLife.ToString();
        if (goldValue != null) goldValue.text = sessionGold.ToString();
        if (!string.IsNullOrEmpty(visualFixtureId)) { RefreshVisualFixture(); return; }
        RefreshInformation();
    }

    #if false // Replaced by the unit-detail projection below; retained only until the next source cleanup pass.
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

    #endif

    private void RefreshInformation()
    {
        if (!TryResolveSelectedDetail(out var detail)) { ClearSelection(); return; }
        infoPanel.gameObject.SetActive(true);
        foreach (var diagnostic in detail.Diagnostics)
            if (reportedDetailDiagnostics.Add(diagnostic)) Debug.LogWarning("[UI-INFO-001][" + diagnostic + "]", this);
        portrait.sprite = Resources.Load<Sprite>(detail.PortraitResourcePath); portrait.enabled = portrait.sprite != null;
        rarityIcon.sprite = LoadRaritySprite(detail.Rarity); rarityIcon.enabled = rarityIcon.sprite != null;
        eliteIcon.sprite = Sprite("StagingSlotElite" + detail.EliteLevel + "Icon"); eliteIcon.enabled = eliteIcon.sprite != null;
        unitName.text = string.IsNullOrWhiteSpace(detail.DisplayNameZhHans) ? "--" : detail.DisplayNameZhHans;
        combatSummary.text = AttackMethodText(detail.AttackMethod) + "  " + DamageTypeText(detail.DamageType);
        targetValue.text = detail.LifeDeduct.ToString();
        SetStat("maxHp", UnitDetailNumberFormatter.Value(detail.MaxHitPoints));
        SetStat("moveSpeed", DetailValue(detail.MoveSpeedCentimetresPerSecond, UnitDetailNumberFormatter.MoveSpeed));
        SetStat("attack", DetailValue(detail.Attack, UnitDetailNumberFormatter.Value));
        SetStat("attackInterval", DetailValue(detail.AttackIntervalTicks, UnitDetailNumberFormatter.AttackInterval));
        SetStat("defense", DetailValue(detail.Defense, UnitDetailNumberFormatter.Value));
        SetStat("magicResistance", DetailValue(detail.MagicResistance, UnitDetailNumberFormatter.Value));
        SetStat("block", UnitDetailNumberFormatter.Value(detail.BlockCapacity));
        SetStat("deploymentCost", UnitDetailNumberFormatter.Value(detail.DeploymentCost));
        var ratio = detail.MaxHitPoints <= 0 ? 0f : Mathf.Clamp01((float)detail.CurrentHitPoints / detail.MaxHitPoints);
        hpFill.rectTransform.sizeDelta = new Vector2(556f * ratio, 12f);
        hpValueRoot.anchoredPosition = new Vector2(556f * ratio, -490f);
        var valueText = Mathf.Clamp(detail.CurrentHitPoints, 0, detail.MaxHitPoints) + "/" + detail.MaxHitPoints;
        hpValue.fontSize = valueText.Length > 9 ? 20 : 24;
        hpValue.text = valueText;
    }

    private void RefreshVisualFixture()
    {
        if (infoPanel == null) return;
        infoPanel.gameObject.SetActive(true);
        if (catalog.TryGet("1000", out var entry))
        {
            portrait.sprite = Resources.Load<Sprite>(entry.PortraitResourcePath); portrait.enabled = portrait.sprite != null;
            rarityIcon.sprite = LoadRaritySprite(entry.Rarity); rarityIcon.enabled = rarityIcon.sprite != null;
            eliteIcon.sprite = Sprite("StagingSlotElite0Icon"); eliteIcon.enabled = eliteIcon.sprite != null;
        }
        unitName.text = string.Equals(visualFixtureId, "empty-name", StringComparison.Ordinal) ? "--" : "视觉验证单位";
        combatSummary.text = "近战  物理";
        targetValue.text = "2";
        SetStat("maxHp", "18000");
        SetStat("moveSpeed", "1.9");
        SetStat("attack", "1100");
        SetStat("attackInterval", "4");
        SetStat("defense", "0");
        SetStat("magicResistance", "20");
        SetStat("block", "1");
        SetStat("deploymentCost", "12");
        hpFill.rectTransform.sizeDelta = new Vector2(556f, 12f);
        hpValueRoot.anchoredPosition = new Vector2(556f, -490f);
        hpValue.fontSize = 20;
        hpValue.text = "18000/18000";
    }

    private bool TryResolveSelectedDetail(out UnitDetailSnapshot detail)
    {
        detail = null;
        if (string.IsNullOrEmpty(selectedUnitId)) return false;
        var displayedUnit = hud?.DisplayedSnapshot?.Units.FirstOrDefault(item => item.UnitId == selectedUnitId);
        if (loop.Phase == LocalBattlePhase.Battle && (selectedBattleEnemy || displayedUnit == null || displayedUnit.Zone != PlayerUnitZone.Staging))
        {
            var input = CurrentBattleInput();
            return input != null && UnitDetailResolver.TryResolveBattle(input, CurrentBattleStates(), catalog, selectedUnitId, out detail);
        }
        return hud?.DisplayedSnapshot != null && UnitDetailResolver.TryResolvePreparation(hud.DisplayedSnapshot, catalog, selectedUnitId, out detail);
    }

    private IReadOnlyList<BattlePresentationViewState> CurrentBattleStates()
    {
        return loop != null && loop.MultiBattle != null
            ? loop.MultiBattle.PresentationViewStates
            : demo?.Coordinator?.PresentationViewStates ?? Array.Empty<BattlePresentationViewState>();
    }

    private BattleInput CurrentBattleInput()
    {
        var multi = loop?.MultiBattle;
        if (multi != null)
        {
            var match = multi.Matches.FirstOrDefault(item => item.MatchId == multi.SelectedMatchId);
            if (match != null) return match.Input;
        }
        return demo?.Coordinator?.Input;
    }

    private BattleSide CurrentObserverSide()
    {
        return loop?.MultiBattle?.Observer == BattleObserverView.Away ? BattleSide.Away : BattleSide.Home;
    }

    private void AddStat(string key, string label, UnitInformationPanelLayout.Placement placement, UnitInformationPanelLayout layout)
    {
        var entry = Rect("Stat_" + key, infoPanel); PositionFromTopLeft(entry, placement);
        var background = Image("Background", entry, Sprite("UnitInformationPanelStatEntryBackground")); Stretch(background.rectTransform); background.preserveAspect = false; background.color = InformationEntryBackgroundTint;
        var icon = Image("Icon", entry, LoadInformationIcon(key)); PositionFromTopLeft(icon.rectTransform, layout.StatIcon); icon.preserveAspect = true;
        var labelText = Text("Label", entry, 23, TextAnchor.MiddleLeft, new Color(.74f, .74f, .74f)); PositionFromTopLeft(labelText.rectTransform, layout.StatLabel); labelText.text = label;
        var value = NumberText("Value", entry, 29, TextAnchor.MiddleRight, Color.white); PositionFromTopLeft(value.rectTransform, layout.StatValue);
        statValues.Add(key, value);
    }

    private Sprite LoadInformationIcon(string key)
    {
        if (informationIcons.TryGetValue(key, out var cached)) return cached;
        if (!InformationIcons.TryGetValue(key, out var source)) { Debug.LogError("[UI-INFO-002][icon.key.missing] key=" + key, this); return null; }
        Sprite icon;
        if (string.IsNullOrEmpty(source.TextureResourcePath)) icon = Sprite(source.SpriteName);
        else
        {
            var texture = Resources.Load<Texture2D>(source.TextureResourcePath);
            icon = texture == null ? null : UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
            if (icon != null) icon.name = source.SpriteName;
        }
        if (icon == null) Debug.LogError("[UI-INFO-002][icon.resource.missing] key=" + key + "; source=" + source.DisplayName, this);
        informationIcons[key] = icon;
        return icon;
    }

    private void SetStat(string key, string value) { if (statValues.TryGetValue(key, out var target)) target.text = value; }
    private static string DetailValue(int? value, Func<int, string> format) => value.HasValue ? format(value.Value) : "--";
    private static string AttackMethodText(AttackMethod method) => method == AttackMethod.Ranged ? "\u8fdc\u7a0b" : "\u8fd1\u6218";
    private static string DamageTypeText(DamageType type) => type == DamageType.Magic ? "\u6cd5\u672f" : type == DamageType.True ? "\u771f\u5b9e" : "\u7269\u7406";
    private Sprite LoadRaritySprite(int rarity)
    {
        if (raritySprites.TryGetValue(rarity, out var cached)) return cached;
        var texture = Resources.Load<Texture2D>("UI/Texture/UnitRarity" + rarity + "Icon");
        var sprite = texture == null ? null : UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
        raritySprites[rarity] = sprite;
        return sprite;
    }

    private Sprite Sprite(string name) { sprites.TryGetValue(name, out var sprite); return sprite; }
    private static Text NumberText(string name, Transform parent, int size, TextAnchor alignment, Color color) { var text = Text(name, parent, size, alignment, color); text.font = StagingHudController.FormalNumericFont; return text; }
    private static RectTransform Rect(string name, Transform parent) { var value = new GameObject(name, typeof(RectTransform)); value.transform.SetParent(parent, false); return value.GetComponent<RectTransform>(); }
    private static Image Image(string name, Transform parent, Sprite sprite) { var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); value.transform.SetParent(parent, false); var image = value.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = false; return image; }
    private static Text Text(string name, Transform parent, int size, TextAnchor alignment, Color color) { var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)); value.transform.SetParent(parent, false); var text = value.GetComponent<Text>(); text.font = StagingHudController.FormalUiFont; text.fontStyle = FontStyle.Normal; text.resizeTextForBestFit = false; text.fontSize = size; text.alignment = alignment; text.color = color; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow; return text; }
    private static void Stretch(RectTransform target) { target.anchorMin = Vector2.zero; target.anchorMax = Vector2.one; target.offsetMin = target.offsetMax = Vector2.zero; }
    private static void PositionFromTopLeft(RectTransform target, UnitInformationPanelLayout.Placement placement) { target.anchorMin = target.anchorMax = new Vector2(0f, 1f); target.pivot = new Vector2(0f, 1f); target.anchoredPosition = new Vector2(placement.Left, -placement.Top); target.sizeDelta = new Vector2(placement.Width, placement.Height); }
    private static void Position(RectTransform target, float x, float y, float width, float height) { target.anchorMin = target.anchorMax = new Vector2(x, y); target.pivot = new Vector2(.5f, .5f); target.sizeDelta = new Vector2(width, height); }

    private sealed class InformationIconSource
    {
        public InformationIconSource(string textureResourcePath, string spriteName)
        {
            TextureResourcePath = textureResourcePath;
            SpriteName = spriteName;
        }

        public string TextureResourcePath { get; }
        public string SpriteName { get; }
        public string DisplayName => string.IsNullOrEmpty(TextureResourcePath) ? SpriteName : TextureResourcePath;
    }
}

internal static class FormalBattleHudBootstrap
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
        if (hud != null && hud.GetComponent<FormalBattleHudController>() == null) hud.gameObject.AddComponent<FormalBattleHudController>();
    }
}
