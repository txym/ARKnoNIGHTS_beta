using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Presentation;
using ArknoNights.Deployment;
using ArknoNights.Player;
using ArknoNights.Round;
using ArknoNights.UI;
using ArknoNights.UI.FormalHud.ShopReady;
using ArknoNights.UI.PlayerListObserver;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// UI-010's scene composition root.  It joins the existing local-shop, player-observation,
/// deployment, information-panel and UI-009 presentation components without owning a second
/// PlayerState, battle runner, match state, or presentation clock.
/// </summary>
[DisallowMultipleComponent]
public sealed class UI010SceneIntegrationController : MonoBehaviour
{
    private StagingHudController hud;
    private StateDrivenDeploymentController deployment;
    private PreparationBattleLoopController loop;
    private FormalBattleHudUi005 formalHud;
    private ShopReadyHudController shopReady;
    private PlayerListObserverCoordinator observer;
    private UI010PlayerListHud playerList;
    private ObservedPreparationFormation observedFormation;
    private LocalBattlePhase observedPhase = LocalBattlePhase.Loading;
    private bool initialized;
    private bool formalSelectionBound;

    /// <summary>The existing UI-007 controller, supplied with the loop-owned LocalMatchState.</summary>
    public ShopReadyHudController ShopReady => shopReady;
    /// <summary>The existing UI-008 read-only display/command-ownership boundary.</summary>
    public PlayerListObserverCoordinator Observer => observer;
    /// <summary>Scene-owned player-list projection; it has no match data of its own.</summary>
    public UI010PlayerListHud PlayerList => playerList;
    public bool IsInitialized => initialized;

    private IEnumerator Start()
    {
        // UI-002/003/005/009 all attach in Start.  Waiting on their published boundaries keeps
        // this component independent of Unity's Start ordering and makes scene reload idempotent.
        for (var frame = 0; frame < 120; frame++)
        {
            hud = GetComponent<StagingHudController>();
            deployment = GetComponent<StateDrivenDeploymentController>();
            loop = GetComponent<PreparationBattleLoopController>();
            formalHud = GetComponent<FormalBattleHudUi005>();
            if (hud != null && hud.InitializationSucceeded && deployment != null && loop != null && loop.MatchState != null)
                break;
            yield return null;
        }

        if (hud == null || !hud.InitializationSucceeded || deployment == null || loop == null || loop.MatchState == null)
        {
            Debug.LogError("[UI-010][scene.dependencies.missing]", this);
            yield break;
        }

        Initialize(loop.MatchState);
    }

    private void Update()
    {
        if (!initialized) return;

        if (!formalSelectionBound)
        {
            if (formalHud == null) formalHud = GetComponent<FormalBattleHudUi005>();
            BindFormalSelection();
            RefreshFormalSessionValues();
        }

        if (loop.Phase != observedPhase)
        {
            HandlePhaseChanged(observedPhase, loop.Phase);
            observedPhase = loop.Phase;
        }

        TrySelectObservedFormationUnit();
    }

    /// <summary>
    /// The only scene input boundary for changing which fixture player is rendered.  During
    /// battle, UI-009 rebinds an already-computed track; during preparation only the read-only
    /// projections change.  Neither branch creates a new battle nor a new PlayerState.
    /// </summary>
    public bool TryObservePlayer(string playerId)
    {
        if (!initialized || string.IsNullOrWhiteSpace(playerId)) return false;
        var success = loop.Phase == LocalBattlePhase.Battle
            ? loop.TryObserveBattlePlayer(playerId)
            : observer.TrySelectDisplayedPlayer(playerId).Success;
        if (success) RefreshPresentation();
        return success;
    }

    private void Initialize(LocalMatchState match)
    {
        if (initialized) return;
        observer = new PlayerListObserverCoordinator(match);
        observer.Changed += RefreshPresentation;

        var canvas = hud.GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[UI-010][canvas.missing]", this);
            observer.Dispose();
            observer = null;
            return;
        }

        var shopRoot = new GameObject("UI010ShopReady", typeof(RectTransform));
        shopRoot.transform.SetParent(canvas.transform, false);
        shopReady = shopRoot.AddComponent<ShopReadyHudController>();
        shopReady.Initialize(match);
        shopReady.FormationInteractionChanged += HandleShopFormationInteractionChanged;

        var playerListRoot = new GameObject("UI010PlayerList", typeof(RectTransform));
        playerListRoot.transform.SetParent(canvas.transform, false);
        playerList = playerListRoot.AddComponent<UI010PlayerListHud>();
        playerList.Initialize(observer, TryObservePlayer);

        observedFormation = new ObservedPreparationFormation(transform);
        observedPhase = loop.Phase;
        BindFormalSelection();
        initialized = true;
        RefreshPresentation();
        Debug.Log("[UI-010][scene.initialized] local=" + observer.LocalCommandPlayerState.PlayerId + "; observed=" + observer.DisplayedPlayerState.PlayerId, this);
    }

    private void BindFormalSelection()
    {
        if (formalSelectionBound || formalHud == null || observer == null) return;
        formalHud.SelectionChanged += observer.SetUnitSelected;
        formalSelectionBound = true;
    }

    private void HandlePhaseChanged(LocalBattlePhase previous, LocalBattlePhase next)
    {
        // This is deliberately after UI-009's ReturnToPreparation.  It changes only transient
        // ready/observer UI state; the sealed formations and completed battle outputs stay intact.
        if (previous == LocalBattlePhase.Battle && next == LocalBattlePhase.Preparation)
        {
            loop.MatchState.ResetPreparationUiState();
            formalHud?.ClearSelectionForUi010();
        }
        else if (next == LocalBattlePhase.Battle)
        {
            formalHud?.ClearSelectionForUi010();
        }

        RefreshPresentation();
    }

    private void HandleShopFormationInteractionChanged(bool ignored)
    {
        // Ready is only one term of the unified gate.  Observation and phase stay authoritative
        // here so a shop refresh cannot accidentally re-enable another player's formation.
        ApplyFormationGate();
    }

    private void RefreshPresentation()
    {
        if (!initialized || observer == null || loop == null) return;
        var preparation = loop.Phase == LocalBattlePhase.Preparation;
        shopReady?.SetPreparationPhase(preparation);

        var displayingRemote = observer.IsObservingAnotherPlayer;
        if (displayingRemote)
            hud.SetDisplayedSnapshot(observer.DisplayedPlayerState.PlayerState, true);
        else
            hud.RestoreLocalDisplay();

        // Local command views and the observed player's world views are intentionally mutually
        // exclusive.  This prevents two visible objects from claiming the same interaction role.
        deployment.SetPreparationViewsVisible(preparation && !displayingRemote);
        if (preparation && displayingRemote)
            observedFormation.Show(observer.DisplayedPlayerState.PlayerState, BattleObserverView.Away);
        else
            observedFormation.Hide();

        ApplyFormationGate();
        playerList?.Refresh();
        RefreshFormalSessionValues();
    }

    private void RefreshFormalSessionValues()
    {
        if (formalHud == null || observer == null || loop == null) return;
        var ordered = loop.MatchState.OrderedPlayerIds;
        var localIndex = -1;
        for (var index = 0; index < ordered.Count; index++)
            if (string.Equals(ordered[index], observer.LocalCommandPlayerState.PlayerId, StringComparison.Ordinal)) { localIndex = index; break; }
        var opponentId = localIndex < 0 ? string.Empty : ordered[localIndex % 2 == 0 ? localIndex + 1 : localIndex - 1];
        var opponent = observer.Snapshot.Players.FirstOrDefault(player => string.Equals(player.PlayerId, opponentId, StringComparison.Ordinal));
        formalHud.SetSessionHudValues(observer.LocalEconomyState.Gold, observer.LocalEconomyState.Life, opponent == null ? "固定测试" : opponent.DisplayName);
    }

    private void ApplyFormationGate()
    {
        if (!initialized || observer == null || deployment == null || loop == null) return;
        var canMutateFormation = loop.Phase == LocalBattlePhase.Preparation
            && !observer.IsObservingAnotherPlayer
            && !observer.LocalCommandPlayerState.IsReady;
        deployment.SetInteractionEnabled(canMutateFormation);
    }

    private void TrySelectObservedFormationUnit()
    {
        if (loop.Phase != LocalBattlePhase.Preparation || observer == null || !observer.IsObservingAnotherPlayer) return;
        if (!Input.GetMouseButtonUp(0) || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;
        var camera = Camera.main;
        if (camera == null) return;
        foreach (var hit in Physics.RaycastAll(camera.ScreenPointToRay(Input.mousePosition), 2000f).OrderBy(item => item.distance))
        {
            var view = hit.collider.GetComponentInParent<PreparationUnitView>();
            if (view == null || !observedFormation.Contains(view)) continue;
            formalHud?.SelectObservedPreparationUnitForHud(view.PlayerUnitId);
            break;
        }
    }

    private void OnDestroy()
    {
        if (shopReady != null) shopReady.FormationInteractionChanged -= HandleShopFormationInteractionChanged;
        if (formalSelectionBound && formalHud != null && observer != null) formalHud.SelectionChanged -= observer.SetUnitSelected;
        if (observer != null) observer.Changed -= RefreshPresentation;
        observer?.Dispose();
        observedFormation?.Dispose();
    }
}

/// <summary>Small presentation-only UGUI adapter for UI-008's pure player-list model.</summary>
[DisallowMultipleComponent]
public sealed class UI010PlayerListHud : MonoBehaviour
{
    private const string PlayerListPath = "UI/Texture/player_list/";
    private static readonly string[] FallbackAvatarPaths =
    {
        "ProfilePicture/UIImage_gopro",
        "ProfilePicture/UIImage_arcslma",
        "ProfilePicture/UIImage_5504_arcslmi"
    };

    private PlayerListObserverCoordinator coordinator;
    private Func<string, bool> selectPlayer;
    private RectTransform root;

    public bool IsInitialized => coordinator != null;

    public void Initialize(PlayerListObserverCoordinator source, Func<string, bool> select)
    {
        coordinator = source ?? throw new ArgumentNullException(nameof(source));
        selectPlayer = select ?? throw new ArgumentNullException(nameof(select));
        root = GetComponent<RectTransform>();
        coordinator.Changed += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        if (coordinator == null) return;
        if (root == null) root = GetComponent<RectTransform>();
        if (root == null) return;
        root.gameObject.SetActive(coordinator.IsPlayerListVisible);
        if (!root.gameObject.activeSelf) return;
        foreach (Transform child in root) Destroy(child.gameObject);

        var entries = PlayerListPresentation.Build(coordinator);
        var layout = PlayerListLayout.Calculate(Screen.width, Screen.height, entries.Count, coordinator.IsPlayerListVisible);
        for (var index = 0; index < entries.Count; index++)
        {
            BuildRow(entries[index], layout.Rows[index]);
        }
    }

    private void BuildRow(PlayerListEntryPresentation entry, PlayerListRowLayout layout)
    {
        var row = new GameObject("Player_" + entry.PlayerId, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        row.transform.SetParent(root, false);
        var rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = new Vector2(layout.X, -layout.YMin);
        rowRect.sizeDelta = new Vector2(layout.Width, layout.Height);
        row.GetComponent<Image>().color = entry.IsObservedPlayer ? new Color(.25f, .5f, .75f, .42f) : new Color(0f, 0f, 0f, .2f);
        row.GetComponent<Button>().onClick.AddListener(() => selectPlayer(entry.PlayerId));

        var avatar = Image("Avatar", row.transform, LoadAvatar(entry));
        Position(avatar.rectTransform, 42f, 46f, 78f, 78f);
        avatar.preserveAspect = true;
        var border = Image("AvatarBorder", row.transform, LoadSprite("avatar_border"));
        Position(border.rectTransform, 42f, 46f, 88f, 88f);
        border.preserveAspect = true;
        if (entry.IsLocalPlayer)
        {
            var self = Image("Self", row.transform, LoadSprite("icon_self"));
            Position(self.rectTransform, 10f, 82f, 30f, 30f);
            self.preserveAspect = true;
        }
        if (!entry.IsConnected)
        {
            var lost = Image("LostConnection", row.transform, LoadSprite("icon_lost_connect"));
            Position(lost.rectTransform, 72f, 72f, 34f, 34f);
            lost.preserveAspect = true;
        }
        if (entry.IsObservedPlayer && !entry.IsLocalPlayer)
        {
            var observing = Image("Observing", row.transform, LoadSprite("icon_observing"));
            Position(observing.rectTransform, 72f, 72f, 34f, 34f);
            observing.preserveAspect = true;
        }

        var name = Text("Name", row.transform, 18, TextAnchor.MiddleLeft, Color.white);
        name.text = entry.DisplayName;
        Position(name.rectTransform, 92f, 58f, 160f, 30f);
        var hp = Image("HealthBackground", row.transform, LoadSprite(entry.IsConnected ? "bg_hp" : "bg_lose_hp"));
        Position(hp.rectTransform, 132f, 26f, 160f, 28f);
        hp.preserveAspect = false;
        var hpIcon = Image("HealthIcon", hp.transform, LoadSprite("icon_hp"));
        Position(hpIcon.rectTransform, 15f, 14f, 22f, 22f);
        var value = Text("Life", hp.transform, 16, TextAnchor.MiddleCenter, Color.white);
        value.text = entry.Life.ToString();
        Position(value.rectTransform, 98f, 14f, 105f, 26f);
        if (entry.IsLocalPlayer && !entry.IsObservedPlayer)
        {
            var returnButton = Image("ReturnLocal", row.transform, LoadSprite("btn_return_self"));
            Position(returnButton.rectTransform, 224f, 48f, 56f, 44f);
            returnButton.preserveAspect = true;
        }
    }

    private static Sprite LoadAvatar(PlayerListEntryPresentation entry)
    {
        var avatar = Resources.Load<Sprite>(entry.AvatarResourcePath);
        if (avatar != null) return avatar;
        var fallbackIndex = StableAvatarIndex(entry.PlayerId);
        return Resources.Load<Sprite>(FallbackAvatarPaths[fallbackIndex]);
    }

    private static int StableAvatarIndex(string playerId)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in playerId ?? string.Empty) { hash ^= character; hash *= 16777619; }
            return (int)(hash % (uint)FallbackAvatarPaths.Length);
        }
    }

    private static Sprite LoadSprite(string name) => Resources.Load<Sprite>(PlayerListPath + name);

    private static Image Image(string name, Transform parent, Sprite sprite)
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        return image;
    }

    private static Text Text(string name, Transform parent, int fontSize, TextAnchor anchor, Color color)
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        value.transform.SetParent(parent, false);
        var text = value.GetComponent<Text>();
        text.font = StagingHudController.FormalUiFont;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void Position(RectTransform target, float x, float y, float width, float height)
    {
        target.anchorMin = target.anchorMax = new Vector2(0f, 0f);
        target.pivot = new Vector2(.5f, .5f);
        target.anchoredPosition = new Vector2(x, y);
        target.sizeDelta = new Vector2(width, height);
    }

    private void OnDestroy()
    {
        if (coordinator != null) coordinator.Changed -= Refresh;
    }
}

/// <summary>Read-only preparation-world projection used only while a remote fixture player is observed.</summary>
internal sealed class ObservedPreparationFormation : IDisposable
{
    private readonly Transform parent;
    private readonly Dictionary<string, PreparationUnitView> views = new Dictionary<string, PreparationUnitView>(StringComparer.Ordinal);

    public ObservedPreparationFormation(Transform parent) { this.parent = parent; }

    public bool Contains(PreparationUnitView view) => view != null && views.Values.Contains(view);

    public void Show(PlayerStateSnapshot source, BattleObserverView observer)
    {
        Hide();
        if (source == null) return;
        foreach (var unit in source.Units.Where(item => item.Zone == PlayerUnitZone.Deployed && item.Formation.HasValue))
        {
            if (!PreparationUnitViewBuilder.TryCreate(unit.UnitId, unit.TypeId, parent, false, out var instance, out var diagnostic))
            {
                Debug.LogError("[UI-010][observedFormation.create.failed] unit=" + unit.UnitId + "; code=" + diagnostic);
                continue;
            }
            var view = instance.GetComponent<PreparationUnitView>();
            view.transform.position = BattlefieldWorldProjection.Default.ToWorld(new FixedPosition(unit.Formation.Value.X * 100, unit.Formation.Value.Y * 100), observer);
            instance.name = "ObservedPreparationView_" + unit.UnitId;
            views.Add(unit.UnitId, view);
        }
    }

    public void Hide()
    {
        foreach (var view in views.Values)
            if (view != null) UnityEngine.Object.Destroy(view.gameObject);
        views.Clear();
    }

    public void Dispose() => Hide();
}

/// <summary>Attaches UI-010 to the established FormalBattleHud root without serializing SampleScene changes.</summary>
internal static class UI010SceneIntegrationBootstrap
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
        if (hud != null && hud.GetComponent<UI010SceneIntegrationController>() == null)
            hud.gameObject.AddComponent<UI010SceneIntegrationController>();
    }
}
