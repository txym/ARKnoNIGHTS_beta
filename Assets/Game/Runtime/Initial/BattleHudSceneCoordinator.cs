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
/// Scene composition root for the formal battle HUD. It joins the existing local-shop, player-observation,
/// deployment, information-panel and UI-009 presentation components without owning a second
/// PlayerState, battle runner, match state, or presentation clock.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleHudSceneCoordinator : MonoBehaviour
{
    private StagingHudController hud;
    private StateDrivenDeploymentController deployment;
    private PreparationBattleLoopController loop;
    private FormalBattleHudController formalHud;
    private ShopReadyHudController shopReady;
    private PlayerListObserverCoordinator observer;
    private PlayerListHudController playerList;
    private ObservedPreparationFormation observedFormation;
    private LocalBattlePhase observedPhase = LocalBattlePhase.Loading;
    private bool initialized;
    private bool formalSelectionBound;

    /// <summary>The existing UI-007 controller, supplied with the loop-owned LocalMatchState.</summary>
    public ShopReadyHudController ShopReady => shopReady;
    /// <summary>The existing UI-008 read-only display/command-ownership boundary.</summary>
    public PlayerListObserverCoordinator Observer => observer;
    /// <summary>Scene-owned player-list projection; it has no match data of its own.</summary>
    public PlayerListHudController PlayerList => playerList;
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
            formalHud = GetComponent<FormalBattleHudController>();
            if (hud != null && hud.InitializationSucceeded && deployment != null && loop != null && loop.MatchState != null)
                break;
            yield return null;
        }

        if (hud == null || !hud.InitializationSucceeded || deployment == null || loop == null || loop.MatchState == null)
        {
            Debug.LogError("[BattleHud][scene.dependencies.missing]", this);
            yield break;
        }

        Initialize(loop.MatchState);
    }

    private void Update()
    {
        if (!initialized) return;

        if (!formalSelectionBound)
        {
            if (formalHud == null) formalHud = GetComponent<FormalBattleHudController>();
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
            Debug.LogError("[BattleHud][canvas.missing]", this);
            observer.Dispose();
            observer = null;
            return;
        }

        var shopRoot = new GameObject("ShopReadyHud", typeof(RectTransform));
        shopRoot.transform.SetParent(canvas.transform, false);
        StretchToParent(shopRoot.GetComponent<RectTransform>());
        shopReady = shopRoot.AddComponent<ShopReadyHudController>();
        shopReady.Initialize(match);
        shopReady.FormationInteractionChanged += HandleShopFormationInteractionChanged;
        shopReady.ShopVisibilityChanged += HandleShopVisibilityChanged;

        var playerListRoot = new GameObject("PlayerListPanel", typeof(RectTransform));
        playerListRoot.transform.SetParent(canvas.transform, false);
        StretchToParent(playerListRoot.GetComponent<RectTransform>());
        playerList = playerListRoot.AddComponent<PlayerListHudController>();
        playerList.Initialize(observer, TryObservePlayer);

        observedFormation = new ObservedPreparationFormation(transform);
        observedPhase = loop.Phase;
        BindFormalSelection();
        initialized = true;
        RefreshPresentation();
        Debug.Log("[BattleHud][scene.initialized] local=" + observer.LocalCommandPlayerState.PlayerId + "; observed=" + observer.DisplayedPlayerState.PlayerId, this);
    }

    private static void StretchToParent(RectTransform target)
    {
        target.anchorMin = Vector2.zero;
        target.anchorMax = Vector2.one;
        target.pivot = new Vector2(.5f, .5f);
        target.offsetMin = Vector2.zero;
        target.offsetMax = Vector2.zero;
    }

    private void BindFormalSelection()
    {
        if (formalSelectionBound || formalHud == null || observer == null) return;
        formalHud.SelectionChanged += HandleFormalSelectionChanged;
        formalSelectionBound = true;
    }

    private void HandleFormalSelectionChanged(bool selected)
    {
        observer?.SetUnitSelected(selected);
        if (selected) shopReady?.SetShopVisible(false);
    }

    private void HandleShopVisibilityChanged(bool visible)
    {
        if (visible) formalHud?.ClearSelectionForSceneTransition();
    }

    private void HandlePhaseChanged(LocalBattlePhase previous, LocalBattlePhase next)
    {
        // This is deliberately after UI-009's ReturnToPreparation.  It changes only transient
        // ready/observer UI state; the sealed formations and completed battle outputs stay intact.
        if (previous == LocalBattlePhase.Battle && next == LocalBattlePhase.Preparation)
        {
            loop.MatchState.ResetPreparationUiState();
            formalHud?.ClearSelectionForSceneTransition();
        }
        else if (next == LocalBattlePhase.Battle)
        {
            formalHud?.ClearSelectionForSceneTransition();
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
        if (shopReady != null) shopReady.ShopVisibilityChanged -= HandleShopVisibilityChanged;
        if (formalSelectionBound && formalHud != null) formalHud.SelectionChanged -= HandleFormalSelectionChanged;
        if (observer != null) observer.Changed -= RefreshPresentation;
        observer?.Dispose();
        observedFormation?.Dispose();
    }
}

/// <summary>Presentation-only UGUI adapter for the pure player-list model.</summary>
[DisallowMultipleComponent]
public sealed class PlayerListHudController : MonoBehaviour
{
    private static readonly string[] FallbackAvatarPaths =
    {
        UnitResourcePaths.BuildProfilePictureResourcePath(1000, "gopro"),
        UnitResourcePaths.BuildProfilePictureResourcePath(5503, "arcslma"),
        UnitResourcePaths.BuildProfilePictureResourcePath(5504, "arcslmi")
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
        var width = root.rect.width > 0f ? root.rect.width : Screen.width;
        var height = root.rect.height > 0f ? root.rect.height : Screen.height;
        var layout = PlayerListLayout.Calculate(width, height, entries.Count, coordinator.IsPlayerListVisible);
        var background = Image("Background", root, FormalHudSpriteLoader.Load("UI/Texture/player_list/bg_player_list"));
        PositionFromTopLeft(background.rectTransform, layout.Background);
        background.preserveAspect = false;
        for (var index = 0; index < entries.Count; index++)
        {
            BuildRow(entries[index], layout.Rows[index]);
        }
    }

    private void BuildRow(PlayerListEntryPresentation entry, PlayerListRowLayout layout)
    {
        const float referenceRowWidth = 116f;
        var contentScale = layout.Width / referenceRowWidth;
        var row = new GameObject("Player_" + entry.PlayerId, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        row.transform.SetParent(root, false);
        var rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = new Vector2(layout.X, -layout.YMin);
        rowRect.sizeDelta = new Vector2(layout.Width, layout.Height);
        var hitTarget = row.GetComponent<Image>();
        hitTarget.color = new Color(1f, 1f, 1f, .001f);
        var button = row.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hitTarget;
        button.onClick.AddListener(() => selectPlayer(entry.PlayerId));

        var avatar = Image("Avatar", row.transform, LoadAvatar(entry));
        PositionScaled(avatar.rectTransform, 58f, 70f, 92f, 92f, contentScale);
        avatar.preserveAspect = true;
        var border = Image("AvatarBorder", row.transform, FormalHudSpriteLoader.Load("UI/Texture/player_list/avatar_border"));
        PositionScaled(border.rectTransform, 58f, 70f, 108f, 108f, contentScale);
        border.preserveAspect = true;
        if (entry.IsLocalPlayer)
        {
            var self = Image("Self", row.transform, FormalHudSpriteLoader.Load("UI/Texture/player_list/icon_self"));
            PositionScaled(self.rectTransform, 28f, 110f, 32f, 32f, contentScale);
            self.preserveAspect = true;
        }
        if (entry.ShowsLostConnection)
        {
            var lost = Image("LostConnection", row.transform, FormalHudSpriteLoader.Load("UI/Texture/player_list/icon_lost_connect"));
            PositionScaled(lost.rectTransform, 58f, 70f, 64f, 64f, contentScale);
            lost.preserveAspect = true;
        }
        if (entry.IsObservedPlayer && !entry.IsLocalPlayer)
        {
            var observing = Image("Observing", row.transform, FormalHudSpriteLoader.Load("UI/Texture/player_list/icon_observing"));
            PositionScaled(observing.rectTransform, 105f, 112f, 44f, 39f, contentScale);
            observing.preserveAspect = true;
        }

        // Disconnect is represented exclusively by LostConnection. bg_lose_hp is reserved for
        // a future, explicit post-battle life-loss presentation and must not imply that state here.
        var hp = Image("HealthBackground", row.transform, FormalHudSpriteLoader.Load("UI/Texture/player_list/bg_hp"));
        PositionScaled(hp.rectTransform, 58f, 36f, 92f, 24f, contentScale);
        hp.preserveAspect = false;
        var hpIcon = Image("HealthIcon", hp.transform, FormalHudSpriteLoader.Load("UI/Texture/player_list/icon_hp"));
        PositionScaled(hpIcon.rectTransform, 14f, 12f, 12f, 18f, contentScale);
        hpIcon.preserveAspect = true;
        var value = Text("Life", hp.transform, Mathf.RoundToInt(18f * contentScale), TextAnchor.MiddleCenter, Color.white);
        value.font = StagingHudController.FormalNumericFont;
        value.text = entry.Life.ToString();
        PositionScaled(value.rectTransform, 55f, 12f, 60f, 20f, contentScale);
        if (entry.IsLocalPlayer && coordinator.IsObservingAnotherPlayer)
        {
            var returnButton = Image("ReturnLocal", row.transform, FormalHudSpriteLoader.Load("UI/Texture/player_list/btn_return_self"));
            PositionScaled(returnButton.rectTransform, 58f, 70f, 110f, 110f, contentScale);
            returnButton.preserveAspect = true;
        }
    }

    private static void PositionScaled(RectTransform target, float centerX, float centerY, float width, float height, float scale)
    {
        Position(target, centerX * scale, centerY * scale, width * scale, height * scale);
    }

    private static Sprite LoadAvatar(PlayerListEntryPresentation entry)
    {
        var avatar = FormalHudSpriteLoader.Load(entry.AvatarResourcePath);
        if (avatar != null) return avatar;
        var fallbackIndex = StableAvatarIndex(entry.PlayerId);
        return FormalHudSpriteLoader.Load(FallbackAvatarPaths[fallbackIndex]);
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

    private static Image Image(string name, Transform parent, Sprite sprite)
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = false;
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
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
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

    private static void PositionFromTopLeft(RectTransform target, PlayerListRectLayout layout)
    {
        target.anchorMin = target.anchorMax = new Vector2(0f, 1f);
        target.pivot = new Vector2(0f, 1f);
        target.anchoredPosition = new Vector2(layout.X, -layout.YMin);
        target.sizeDelta = new Vector2(layout.Width, layout.Height);
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
                Debug.LogError("[BattleHud][observedFormation.create.failed] unit=" + unit.UnitId + "; code=" + diagnostic);
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

/// <summary>Attaches the formal HUD scene coordinator without serializing SampleScene changes.</summary>
internal static class BattleHudSceneBootstrap
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
        if (hud != null && hud.GetComponent<BattleHudSceneCoordinator>() == null)
            hud.gameObject.AddComponent<BattleHudSceneCoordinator>();
    }
}
