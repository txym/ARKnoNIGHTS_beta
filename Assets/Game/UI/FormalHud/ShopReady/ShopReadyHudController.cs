using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArknoNights.UI.FormalHud.ShopReady
{
    /// <summary>
    /// Scene-attachable presentation for the local player's level, shop, and ready state.
    /// The authoritative economy and commands remain in <see cref="LocalMatchState"/>.
    /// </summary>
    public sealed class ShopReadyHudController : MonoBehaviour
    {
        private const int MaximumLevel = 9;
        private const int RefreshCost = 1;
        private const float ShopVisualScale = 1.5f;
        private const float ShadowedButtonContentLift = 9f;
        private const float PriceTextVisualLift = 3f;
        private const string ShopChineseFontPath = "Fonts/FangZhengHeiTiJianTi-1";
        private const string BattleAtlasPath = "UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged";
        private const string DeploymentCostIconName = "DeploymentCostPanelIcon";
        private static readonly Color ShopNameColor = new Color(.82f, .84f, .82f, 1f);
        private static readonly Color PriceColor = new Color(1f, .94f, .72f, 1f);
        private static readonly Color ShopLevelNumberColor = new Color(40f / 255f, 221f / 255f, 169f / 255f, 1f);
        private static readonly Color UpgradeLevelNumberColor = new Color(99f / 255f, 222f / 255f, 189f / 255f, 1f);
        private static readonly Color FreezeLabelColor = new Color(.03f, .16f, .19f, 1f);
        private static readonly Color RefreshLabelColor = new Color(.22f, .13f, .01f, 1f);
        private static readonly Color ReadyLabelColor = new Color(.02f, .18f, .17f, 1f);
        private sealed class InfoRow
        {
            public RectTransform Root;
            public Image Icon;
            public Text Value;
        }

        private sealed class SlotWidgets
        {
            public int SlotId;
            public RectTransform Root;
            public Button Purchase;
            public Image Background;
            public Image Portrait;
            public Image Unaffordable;
            public Image Outline;
            public Image Frame;
            public Image Confirmation;
            public Image Frozen;
            public Image CostBackground;
            public Text Name;
            public Text Price;
            public InfoRow CostInfo;
            public InfoRow RegionInfo;
            public InfoRow OccupationInfo;
        }

        private readonly List<SlotWidgets> slotWidgets = new List<SlotWidgets>();
        private readonly ShopReadyPendingCommand pendingCommand = new ShopReadyPendingCommand();
        private readonly HashSet<string> missingAffinityTypeIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> missingAffinityIconKeys = new HashSet<string>(StringComparer.Ordinal);
        private LocalMatchState match;
        private LocalMatchState localMatchBeforeExternal;
        private ShopReadyHudState state;
        private UnitAffinityPresentationCatalog affinityCatalog;
        private RectTransform root;
        private RectTransform levelButtonRoot;
        private RectTransform shopPanel;
        private RectTransform readyButtonRoot;
        private Button levelButton;
        private Button readyButton;
        private Button refreshButton;
        private Button upgradeButton;
        private Button freezeButton;
        private Image readyBackground;
        private Image readyFrame;
        private Image readyIcon;
        private Image refreshBackground;
        private Image refreshIcon;
        private Image refreshCostBackground;
        private Image upgradeBackground;
        private Image upgradeCostBackground;
        private Image upgradeFrame;
        private Image upgradeGradient;
        private Image freezeBackground;
        private Image freezeIcon;
        private Text levelText;
        private Text readyText;
        private Text refreshText;
        private Text refreshCostText;
        private Text upgradeLevelText;
        private Text upgradeCostText;
        private Text freezeText;
        private bool shopVisible;
        private bool refreshFreePresentation;
        private bool preparationPhase = true;
        private bool externalMode;
        private Action externalRefresh;
        private Action externalUpgrade;
        private Action<int> externalPurchase;
        private Action externalToggleAllFrozen;
        private Action externalToggleReady;
        private static Font shopChineseFont;

        public event Action<bool> FormationInteractionChanged;
        public event Action<LocalMatchOperationCode> CommandCompleted;
        public event Action<bool> ShopVisibilityChanged;

        public bool IsInitialized => match != null || externalMode;
        public ShopReadyHudState State => state;
        public bool IsPreparationPhase => preparationPhase;
        public bool IsExternalMode => externalMode;
        public bool IsShopVisible => shopVisible;

        public void Initialize(LocalMatchState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (match != null) match.Changed -= OnMatchChanged;
            if (localMatchBeforeExternal != null)
                localMatchBeforeExternal.Changed -= OnMatchChanged;
            localMatchBeforeExternal = null;
            externalMode = false;
            externalRefresh = null;
            externalUpgrade = null;
            externalPurchase = null;
            externalToggleAllFrozen = null;
            externalToggleReady = null;
            pendingCommand.Clear();
            match = source;
            match.Changed += OnMatchChanged;
            EnsureView();
            Refresh(match.Snapshot);
        }

        public void InitializeExternal(
            Action refresh,
            Action upgrade,
            Action<int> purchase,
            Action toggleAllFrozen,
            Action toggleReady)
        {
            if (match != null) match.Changed -= OnMatchChanged;
            if (!externalMode) localMatchBeforeExternal = match;
            match = null;
            externalMode = true;
            externalRefresh = refresh;
            externalUpgrade = upgrade;
            externalPurchase = purchase;
            externalToggleAllFrozen = toggleAllFrozen;
            externalToggleReady = toggleReady;
            pendingCommand.Clear();
            EnsureView();
        }

        public void ApplyExternalState(
            ShopReadyHudState projection,
            bool resetPendingConfirmation)
        {
            if (!externalMode)
                throw new InvalidOperationException(
                    "External state requires external HUD mode.");
            if (projection == null)
                throw new ArgumentNullException(nameof(projection));
            pendingCommand.Reconcile(projection.Slots);
            state = projection.WithShopVisible(shopVisible);
            RenderState();
        }

        public void ResolveExternalPurchase(
            int shopSlotId,
            string shopUnitId,
            bool accepted)
        {
            if (!externalMode || state == null) return;
            if (pendingCommand.Kind
                != ShopReadyConfirmation.Purchase
                || pendingCommand.ShopSlotId != shopSlotId
                || !string.Equals(
                    pendingCommand.ShopUnitId,
                    shopUnitId ?? string.Empty,
                    StringComparison.Ordinal))
            {
                return;
            }
            pendingCommand.Resolve(accepted);
            RenderState();
        }

        public void ResolveExternalUpgrade(bool accepted)
        {
            if (externalMode
                && state != null
                && pendingCommand.Kind
                    == ShopReadyConfirmation.Upgrade)
            {
                pendingCommand.Resolve(accepted);
                RenderState();
            }
        }

        public void ClearExternalMode()
        {
            externalMode = false;
            externalRefresh = null;
            externalUpgrade = null;
            externalPurchase = null;
            externalToggleAllFrozen = null;
            externalToggleReady = null;
            pendingCommand.Clear();
            if (match == null && localMatchBeforeExternal != null)
            {
                match = localMatchBeforeExternal;
                localMatchBeforeExternal = null;
                match.Changed += OnMatchChanged;
                Refresh(match.Snapshot);
            }
        }

        public void ClearExternalPendingConfirmation()
        {
            if (!externalMode) return;
            pendingCommand.Clear();
            if (state != null) RenderState();
        }

        public void SetShopVisible(bool visible)
        {
            var changed = shopVisible != visible;
            shopVisible = visible;
            if (!shopVisible) pendingCommand.Clear();
            if (match != null) Refresh(match.Snapshot);
            else if (externalMode && state != null)
            {
                state = state.WithShopVisible(shopVisible);
                RenderState();
            }
            if (changed) ShopVisibilityChanged?.Invoke(shopVisible);
        }

        public void ToggleShopVisible() => SetShopVisible(!shopVisible);

        /// <summary>
        /// Selects the reserved free-refresh artwork without changing refresh cost or economy state.
        /// </summary>
        public void SetRefreshFreePresentation(bool isFree)
        {
            refreshFreePresentation = isFree;
            ApplyRefreshCostPresentation();
        }

        /// <summary>Phase changes close transient shop UI while keeping local economy commands available.</summary>
        public void SetPreparationPhase(bool isPreparation)
        {
            var phaseChanged = preparationPhase != isPreparation;
            preparationPhase = isPreparation;
            if (phaseChanged)
            {
                var wasVisible = shopVisible;
                shopVisible = false;
                pendingCommand.Clear();
                if (wasVisible) ShopVisibilityChanged?.Invoke(false);
            }

            if (match != null) Refresh(match.Snapshot);
            else if (externalMode && state != null) RenderState();
            if (root != null) root.gameObject.SetActive(true);
            if (readyButtonRoot != null) readyButtonRoot.gameObject.SetActive(preparationPhase);
        }

        public void RequestRefresh()
        {
            if (state == null
                || !state.ShopCommandsEnabled
                || state.Gold < RefreshCost) return;
            if (externalMode)
            {
                pendingCommand.Clear();
                externalRefresh?.Invoke();
                RenderState();
                return;
            }
            if (match == null) return;
            Complete(match.TryRefresh());
        }

        public void RequestUpgrade()
        {
            if (state == null || !state.ShopCommandsEnabled) return;
            var upgradeCost = state.UpgradeCost;
            if (state.Level >= MaximumLevel || state.Gold < upgradeCost) return;
            if (!EnsureConfirmation(ShopReadyConfirmation.Upgrade)) return;
            if (externalMode)
            {
                externalUpgrade?.Invoke();
                RenderState();
                return;
            }
            if (match == null) return;
            Complete(match.TryUpgrade());
        }

        public void Purchase(int shopSlotId)
        {
            if (state == null || !state.ShopCommandsEnabled) return;
            var slot = state.Slots.FirstOrDefault(item => item.ShopSlotId == shopSlotId);
            if (slot == null || !slot.CanPurchase) return;
            if (!pendingCommand.RequestPurchase(
                    shopSlotId,
                    slot.UnitId))
            {
                RenderCurrent();
                return;
            }

            RenderCurrent();
            if (!state.Slots.Any(item => item.ShopSlotId == shopSlotId && item.CanPurchase)) return;
            if (externalMode)
            {
                externalPurchase?.Invoke(shopSlotId);
                RenderState();
                return;
            }
            if (match == null) return;
            Complete(match.TryPurchase(shopSlotId));
        }

        public void ToggleFrozen(int shopSlotId)
        {
            if (externalMode) return;
            if (match == null || state == null || !state.ShopCommandsEnabled) return;
            if (!state.Slots.Any(slot => slot.ShopSlotId == shopSlotId && slot.CanToggleFrozen)) return;
            Complete(match.TryToggleFrozen(shopSlotId));
        }

        public void ToggleAllFrozen()
        {
            if (state == null || !state.ShopCommandsEnabled) return;
            var occupied = state.Slots.Where(slot => !slot.IsEmpty).ToArray();
            if (occupied.Length == 0) return;
            if (externalMode)
            {
                externalToggleAllFrozen?.Invoke();
                return;
            }
            if (match == null) return;
            var freeze = occupied.Any(slot => !slot.IsFrozen);
            Complete(match.TrySetOccupiedShopSlotsFrozen(freeze));
        }

        public void ToggleReady()
        {
            if (!preparationPhase
                || state == null
                || !state.ShopCommandsEnabled) return;
            if (externalMode)
            {
                externalToggleReady?.Invoke();
                return;
            }
            if (match != null) Complete(match.TryToggleReady());
        }

        private void OnDestroy()
        {
            if (match != null) match.Changed -= OnMatchChanged;
            if (localMatchBeforeExternal != null)
                localMatchBeforeExternal.Changed -= OnMatchChanged;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (root != null && state != null) ApplyLayout();
        }

        private void OnMatchChanged(LocalMatchSnapshot snapshot)
        {
            pendingCommand.Clear();
            Refresh(snapshot);
        }

        private bool EnsureConfirmation(ShopReadyConfirmation requested)
        {
            var confirmed = pendingCommand.RequestFixed(requested);
            if (!confirmed) RenderCurrent();
            return confirmed;
        }

        private void Complete(LocalMatchOperationResult result)
        {
            pendingCommand.Clear();
            CommandCompleted?.Invoke(result.Code);
            Refresh(result.Snapshot);
        }

        private void Refresh(LocalMatchSnapshot snapshot)
        {
            if (snapshot == null) return;
            state = ShopReadyHudState.Project(snapshot, shopVisible, pendingCommand.Kind);
            RenderState();
        }

        private void RenderCurrent()
        {
            if (match != null) Refresh(match.Snapshot);
            else if (state != null) RenderState();
        }

        private void RenderState()
        {
            if (state == null) return;
            EnsureView();
            levelText.text = state.Level.ToString();
            readyText.text = state.IsReady ? "取消准备" : "准备就绪";
            readyIcon.sprite = FormalHudSpriteLoader.Load(
                state.IsReady ? "UI/Texture/ready/icon_ready" : "UI/Texture/ready/ready_icon");

            shopPanel.gameObject.SetActive(state.ShopVisible);
            var upgradeCost = state.UpgradeCost;
            var canUpgrade = state.ShopCommandsEnabled
                && state.Level < MaximumLevel
                && state.Gold >= upgradeCost;
            upgradeButton.interactable = canUpgrade;
            upgradeBackground.sprite = FormalHudSpriteLoader.Load(
                canUpgrade ? "UI/Texture/shop/upgrade_max" : "UI/Texture/shop/upgrade_disable");
            upgradeLevelText.text = state.Level.ToString();
            upgradeCostText.text = state.Level >= MaximumLevel ? "MAX" : upgradeCost.ToString();
            upgradeCostBackground.sprite = FormalHudSpriteLoader.Load(
                canUpgrade ? "UI/Texture/shop/cost_bg_1" : "UI/Texture/shop/cost_bg_2");
            upgradeCostBackground.gameObject.SetActive(state.Level < MaximumLevel);
            var upgradePending = pendingCommand.Kind == ShopReadyConfirmation.Upgrade;
            upgradeFrame.gameObject.SetActive(upgradePending);
            upgradeGradient.gameObject.SetActive(upgradePending);

            refreshButton.interactable =
                state.ShopCommandsEnabled && state.Gold >= RefreshCost;
            refreshIcon.sprite = FormalHudSpriteLoader.Load(
                refreshButton.interactable ? "UI/Texture/shop/refresh_icon" : "UI/Texture/shop/refresh_icon_lock");
            refreshCostText.text = RefreshCost.ToString();
            ApplyRefreshCostPresentation();

            EnsureSlots();
            for (var index = 0; index < state.Slots.Count; index++)
                BindSlot(slotWidgets[index], state.Slots[index]);

            var occupiedSlots = state.Slots.Where(slot => !slot.IsEmpty).ToArray();
            freezeButton.interactable =
                state.ShopCommandsEnabled && occupiedSlots.Length > 0;
            var unfreezing = occupiedSlots.Length > 0 && occupiedSlots.All(slot => slot.IsFrozen);
            freezeBackground.sprite = FormalHudSpriteLoader.Load(
                unfreezing ? "UI/Texture/shop/frozen_bg_unselect" : "UI/Texture/shop/frozen_bg_normal");
            freezeIcon.sprite = FormalHudSpriteLoader.Load(
                unfreezing ? "UI/Texture/shop/frozen_icon2" : "UI/Texture/shop/frozen_icon");
            freezeText.text = unfreezing ? "解除冻结" : "冻结";
            levelButton.interactable = true;
            readyButton.interactable =
                preparationPhase && state.ShopCommandsEnabled;
            readyButtonRoot.gameObject.SetActive(preparationPhase);

            ApplyLayout();
            FormationInteractionChanged?.Invoke(preparationPhase && state.FormationInteractionEnabled);
        }

        private void ApplyRefreshCostPresentation()
        {
            if (refreshCostBackground == null || refreshCostText == null || refreshButton == null) return;
            refreshCostBackground.sprite = FormalHudSpriteLoader.Load(
                refreshFreePresentation
                    ? "UI/Texture/shop/cost_free"
                    : refreshButton.interactable
                        ? "UI/Texture/shop/cost_bg_1"
                        : "UI/Texture/shop/cost_bg_2");
            refreshCostText.gameObject.SetActive(!refreshFreePresentation);
        }

        private void EnsureView()
        {
            if (root != null) return;
            affinityCatalog = UnitAffinityPresentationCatalog.LoadFromResources();
            root = GetComponent<RectTransform>();
            if (root == null)
            {
                var view = new GameObject("ShopReadyHudView", typeof(RectTransform));
                view.transform.SetParent(transform, false);
                root = view.GetComponent<RectTransform>();
                root.sizeDelta = new Vector2(1920f, 1080f);
            }

            levelButtonRoot = ButtonRoot("ShopLevelButton", root, ToggleShopVisible, out levelButton);
            var levelBackground = Image("Background", levelButtonRoot, "UI/Texture/shop/level");
            Stretch(levelBackground.rectTransform);
            levelBackground.preserveAspect = true;
            levelText = NumberLabel("Level", levelButtonRoot, 44, TextAnchor.MiddleCenter, ShopLevelNumberColor);

            shopPanel = Rect("ShopPanel", root);
            var upgradeRoot = ButtonRoot("UpgradeButton", shopPanel, RequestUpgrade, out upgradeButton);
            upgradeBackground = Image("Background", upgradeRoot, "UI/Texture/shop/upgrade_max");
            Stretch(upgradeBackground.rectTransform);
            upgradeFrame = Image("ConfirmationFrame", upgradeRoot, "UI/Texture/shop/check_frame");
            Stretch(upgradeFrame.rectTransform);
            upgradeGradient = Image("ConfirmationGradient", upgradeRoot, "UI/Texture/shop/check_grad");
            upgradeCostBackground = Image("UpgradeCostBackground", upgradeRoot, "UI/Texture/shop/cost_bg_1");
            upgradeLevelText = NumberLabel("Level", upgradeRoot, 42, TextAnchor.MiddleCenter, UpgradeLevelNumberColor);
            upgradeCostText = NumberLabel("Cost", upgradeRoot, 18, TextAnchor.MiddleCenter, PriceColor);

            var freezeRoot = ButtonRoot("FreezeButton", shopPanel, ToggleAllFrozen, out freezeButton);
            freezeBackground = Image("Background", freezeRoot, "UI/Texture/shop/frozen_bg_normal");
            Stretch(freezeBackground.rectTransform);
            freezeIcon = Image("Icon", freezeRoot, "UI/Texture/shop/frozen_icon");
            freezeText = Label("Label", freezeRoot, 21, TextAnchor.MiddleCenter, FreezeLabelColor);

            var refreshRoot = ButtonRoot("RefreshButton", shopPanel, RequestRefresh, out refreshButton);
            refreshBackground = Image("Background", refreshRoot, "UI/Texture/shop/refresh_bg_normal");
            Stretch(refreshBackground.rectTransform);
            refreshIcon = Image("Icon", refreshRoot, "UI/Texture/shop/refresh_icon");
            refreshText = Label("Label", refreshRoot, 21, TextAnchor.MiddleCenter, RefreshLabelColor);
            refreshText.text = "刷新";
            refreshCostBackground = Image("CostBackground", refreshRoot, "UI/Texture/shop/cost_bg_1");
            refreshCostText = NumberLabel("Cost", refreshRoot, 18, TextAnchor.MiddleCenter, PriceColor);

            readyButtonRoot = ButtonRoot("ReadyButton", root, ToggleReady, out readyButton);
            readyBackground = Image("Background", readyButtonRoot, "UI/Texture/ready/ready_bg");
            readyBackground.type = UnityEngine.UI.Image.Type.Sliced;
            Stretch(readyBackground.rectTransform);
            readyFrame = Image("Frame", readyButtonRoot, "UI/Texture/ready/ready_frame");
            readyFrame.type = UnityEngine.UI.Image.Type.Sliced;
            Stretch(readyFrame.rectTransform);
            readyIcon = Image("Icon", readyButtonRoot, "UI/Texture/ready/ready_icon");
            readyText = Label("Label", readyButtonRoot, 24, TextAnchor.MiddleCenter, ReadyLabelColor);
        }

        private void EnsureSlots()
        {
            if (slotWidgets.Count == state.Slots.Count
                && slotWidgets.Select(widget => widget.SlotId)
                    .SequenceEqual(state.Slots.Select(slot => slot.ShopSlotId)))
            {
                return;
            }

            foreach (var widget in slotWidgets)
            {
                if (Application.isPlaying)
                {
                    widget.Root.gameObject.SetActive(false);
                    Destroy(widget.Root.gameObject);
                }
                else DestroyImmediate(widget.Root.gameObject);
            }

            slotWidgets.Clear();
            foreach (var slot in state.Slots)
            {
                var capturedSlotId = slot.ShopSlotId;
                var rootRect = ButtonRoot(
                    "ShopSlot_" + capturedSlotId,
                    shopPanel,
                    () => Purchase(capturedSlotId),
                    out var button);
                var background = Image("Background", rootRect, "UI/Texture/shop/bg_black");
                var portraitClip = Rect("PortraitClip", rootRect);
                portraitClip.gameObject.AddComponent<RectMask2D>();
                var portrait = Image("Portrait", portraitClip, null);
                var costInfo = CreateInfoRow("CostInfo", portraitClip, numeric: true);
                var regionInfo = CreateInfoRow("RegionInfo", portraitClip, numeric: false);
                var occupationInfo = CreateInfoRow("OccupationInfo", portraitClip, numeric: false);
                var unaffordable = Image("UnaffordableOverlay", rootRect, "UI/Texture/shop/bg_common");
                var outline = Image("HoverOutline", rootRect, "UI/Texture/shop/frame_outline");
                var frame = Image("RarityFrame", rootRect, "UI/Texture/shop/frame_lv1");
                var confirmation = Image("PurchaseConfirmation", rootRect, "UI/Texture/shop/bg_doublecheck1");
                var name = Label("UnitName", rootRect, 16, TextAnchor.MiddleLeft, ShopNameColor);
                var costBackground = Image("CostBackground", rootRect, "UI/Texture/shop/cost_bg_1");
                var price = NumberLabel("Price", rootRect, 20, TextAnchor.MiddleCenter, PriceColor);
                var frozen = Image("FrozenOverlay", rootRect, "UI/Texture/shop/ice_matte");
                var pointer = rootRect.gameObject.AddComponent<ShopSlotPointerView>();
                pointer.Initialize(outline);
                slotWidgets.Add(new SlotWidgets
                {
                    SlotId = slot.ShopSlotId,
                    Root = rootRect,
                    Purchase = button,
                    Background = background,
                    Portrait = portrait,
                    Unaffordable = unaffordable,
                    Outline = outline,
                    Frame = frame,
                    Confirmation = confirmation,
                    Frozen = frozen,
                    CostBackground = costBackground,
                    Name = name,
                    Price = price,
                    CostInfo = costInfo,
                    RegionInfo = regionInfo,
                    OccupationInfo = occupationInfo
                });
            }
        }

        private void BindSlot(SlotWidgets widget, ShopReadySlotViewState slot)
        {
            widget.Purchase.interactable =
                state.ShopCommandsEnabled && !slot.IsEmpty;
            widget.Background.sprite = FormalHudSpriteLoader.Load(
                slot.IsEmpty ? "UI/Texture/shop/bg_empty" : "UI/Texture/shop/bg_black");
            widget.Portrait.sprite = slot.IsEmpty ? null : UnitPortraitLoader.Load(slot.PortraitResourcePath);
            widget.Portrait.gameObject.SetActive(widget.Portrait.sprite != null);
            widget.Portrait.preserveAspect = true;
            widget.Unaffordable.gameObject.SetActive(!slot.IsEmpty && !slot.CanPurchase);
            widget.Frame.sprite = FormalHudSpriteLoader.Load("UI/Texture/shop/frame_lv" + FrameLevel(slot.Rarity));
            widget.Frame.gameObject.SetActive(!slot.IsEmpty);
            widget.Confirmation.gameObject.SetActive(
                !slot.IsEmpty
                && pendingCommand.Kind == ShopReadyConfirmation.Purchase
                && pendingCommand.ShopSlotId == slot.ShopSlotId);
            widget.Frozen.gameObject.SetActive(!slot.IsEmpty && slot.IsFrozen);
            widget.CostBackground.sprite = FormalHudSpriteLoader.Load(
                slot.CanPurchase ? "UI/Texture/shop/cost_bg_1" : "UI/Texture/shop/cost_bg_2");
            widget.CostBackground.gameObject.SetActive(!slot.IsEmpty);
            widget.Name.gameObject.SetActive(!slot.IsEmpty);
            widget.Name.text = string.IsNullOrWhiteSpace(slot.DisplayName) ? slot.UnitTypeId : slot.DisplayName;
            widget.Price.gameObject.SetActive(!slot.IsEmpty);
            widget.Price.text = slot.Price.ToString();
            BindInfoRows(widget, slot);
        }

        private void BindInfoRows(SlotWidgets widget, ShopReadySlotViewState slot)
        {
            ClearInfoRow(widget.CostInfo);
            ClearInfoRow(widget.RegionInfo);
            ClearInfoRow(widget.OccupationInfo);
            if (slot.IsEmpty) return;

            var costIcon = FormalHudSpriteLoader.LoadAtlasSprite(
                BattleAtlasPath,
                DeploymentCostIconName);
            BindInfoRow(widget.CostInfo, slot.DeploymentCost.ToString(), costIcon);
            if (costIcon == null && missingAffinityIconKeys.Add("cost|" + DeploymentCostIconName))
            {
                Debug.LogError(
                    "[ShopReadyHud][deployment-cost.icon.missing] atlas=" + BattleAtlasPath
                    + "; sprite=" + DeploymentCostIconName,
                    this);
            }

            if (affinityCatalog == null
                || !affinityCatalog.TryGet(slot.UnitTypeId, out var affinity))
            {
                if (missingAffinityTypeIds.Add(slot.UnitTypeId))
                    Debug.LogWarning("[ShopReadyHud][affinity.missing] type=" + slot.UnitTypeId, this);
                return;
            }

            BindAffinityInfoRow(
                widget.RegionInfo,
                slot.UnitTypeId,
                "region",
                affinity.RegionName,
                affinity.RegionIconResourcePath);
            BindAffinityInfoRow(
                widget.OccupationInfo,
                slot.UnitTypeId,
                "occupation",
                affinity.OccupationName,
                affinity.OccupationIconResourcePath);
        }

        private void BindAffinityInfoRow(
            InfoRow row,
            string typeId,
            string kind,
            string displayName,
            string iconResourcePath)
        {
            if (string.IsNullOrEmpty(displayName)) return;
            var icon = FormalHudSpriteLoader.Load(iconResourcePath);
            BindInfoRow(row, displayName, icon);
            if (!string.IsNullOrEmpty(iconResourcePath)
                && icon == null
                && missingAffinityIconKeys.Add(typeId + "|" + kind + "|" + iconResourcePath))
            {
                Debug.LogError(
                    "[ShopReadyHud][affinity.icon.missing] type=" + typeId
                    + "; kind=" + kind
                    + "; resource=" + iconResourcePath,
                    this);
            }
        }

        private static void BindInfoRow(InfoRow row, string value, Sprite icon)
        {
            row.Root.gameObject.SetActive(true);
            row.Value.text = value ?? string.Empty;
            row.Icon.sprite = icon;
            row.Icon.gameObject.SetActive(icon != null);
        }

        private static void ClearInfoRow(InfoRow row)
        {
            row.Value.text = string.Empty;
            row.Icon.sprite = null;
            row.Icon.gameObject.SetActive(false);
            row.Root.gameObject.SetActive(false);
        }

        private void ApplyLayout()
        {
            if (root == null || root.rect.width <= 0f || root.rect.height <= 0f) return;
            var layout = ShopReadyHudLayout.Calculate(root.rect.width, root.rect.height);
            var scale = layout.Scale;
            SetRect(levelButtonRoot, layout.LevelPanel);
            SetRect(shopPanel, layout.ShopPanel);
            SetRect(readyButtonRoot, layout.ReadyButton);

            Stretch(levelText.rectTransform);
            levelText.rectTransform.anchoredPosition = new Vector2(0f, -8f * scale);

            const float cardWidth = 158f * ShopVisualScale;
            const float cardGap = 8f * ShopVisualScale;
            const float upgradeWidth = 111f * ShopVisualScale;
            var slotCount = slotWidgets.Count;
            var firstCardX = ShopReadyHudLayout.ReferenceShopPanel.Width
                - (slotCount * cardWidth + Mathf.Max(0, slotCount - 1) * cardGap);
            var upgradeX = firstCardX - cardGap - upgradeWidth;
            var buttonHeight = 77f * ShopVisualScale;
            var iconSize = 31f * ShopVisualScale;
            var contentCenterY = buttonHeight * .5f + ShadowedButtonContentLift;
            var costBackgroundSize = new Vector2(44f * ShopVisualScale, 35f * ShopVisualScale);

            PositionBottomLeft(upgradeButton.GetComponent<RectTransform>(), upgradeX, 78f * ShopVisualScale, upgradeWidth, 175f * ShopVisualScale, scale);
            Stretch(upgradeBackground.rectTransform);
            Stretch(upgradeFrame.rectTransform);
            PositionBottomLeft(upgradeGradient.rectTransform, 0f, 0f, 111f * ShopVisualScale, 54f * ShopVisualScale, scale);
            PositionBottomLeft(upgradeLevelText.rectTransform, 8f * ShopVisualScale, 44f * ShopVisualScale, 95f * ShopVisualScale, 92f * ShopVisualScale, scale);
            PositionBottomLeft(upgradeCostBackground.rectTransform, 33.5f * ShopVisualScale, 157.5f * ShopVisualScale - 15f, costBackgroundSize.x, costBackgroundSize.y, scale);
            PositionBottomLeft(upgradeCostText.rectTransform, 33.5f * ShopVisualScale, 157.5f * ShopVisualScale - 15f + PriceTextVisualLift, costBackgroundSize.x, costBackgroundSize.y, scale);

            PositionBottomLeft(freezeButton.GetComponent<RectTransform>(), 744f * ShopVisualScale + 10f, -30f, 149f * ShopVisualScale, buttonHeight, scale);
            PositionBottomLeft(freezeIcon.rectTransform, 15f * ShopVisualScale, contentCenterY - iconSize * .5f, iconSize, iconSize, scale);
            PositionBottomLeft(freezeText.rectTransform, 46f * ShopVisualScale, contentCenterY - 20f * ShopVisualScale, 99f * ShopVisualScale, 40f * ShopVisualScale, scale);

            var refreshWidth = 147f * ShopVisualScale;
            PositionBottomLeft(refreshButton.GetComponent<RectTransform>(), 902f * ShopVisualScale + 15f, -30f, refreshWidth, buttonHeight, scale);
            PositionBottomLeft(refreshIcon.rectTransform, 15f * ShopVisualScale, contentCenterY - iconSize * .5f, iconSize, iconSize, scale);
            PositionBottomLeft(refreshText.rectTransform, 46f * ShopVisualScale, contentCenterY - 20f * ShopVisualScale, 94f * ShopVisualScale, 40f * ShopVisualScale, scale);
            var refreshCostLeft = (refreshWidth - costBackgroundSize.x) * .5f;
            const float refreshCostBottom = 0f;
            PositionBottomLeft(refreshCostBackground.rectTransform, refreshCostLeft, refreshCostBottom, costBackgroundSize.x, costBackgroundSize.y, scale);
            PositionBottomLeft(refreshCostText.rectTransform, refreshCostLeft, refreshCostBottom + PriceTextVisualLift, costBackgroundSize.x, costBackgroundSize.y, scale);

            PositionBottomLeft(readyIcon.rectTransform, 18f, 15f, 30f, 30f, scale);
            PositionBottomLeft(readyText.rectTransform, 48f, 7f, 124f, 46f, scale);

            for (var index = 0; index < slotWidgets.Count; index++)
            {
                var widget = slotWidgets[index];
                PositionBottomLeft(widget.Root, firstCardX + index * (cardWidth + cardGap), 78f * ShopVisualScale, cardWidth, 175f * ShopVisualScale, scale);
                Stretch(widget.Background.rectTransform);
                PositionBottomLeft(widget.Portrait.transform.parent as RectTransform, 5f * ShopVisualScale, 22f * ShopVisualScale, 148f * ShopVisualScale, 146f * ShopVisualScale, scale);
                PositionBottomLeft(widget.Portrait.rectTransform, -4f * ShopVisualScale, -3f * ShopVisualScale, 156f * ShopVisualScale, 156f * ShopVisualScale, scale);
                LayoutInfoRow(widget.CostInfo, 4f, scale);
                LayoutInfoRow(widget.RegionInfo, 26f, scale);
                LayoutInfoRow(widget.OccupationInfo, 48f, scale);
                PositionBottomLeft(widget.Unaffordable.rectTransform, 1f * ShopVisualScale, 2f * ShopVisualScale, 156f * ShopVisualScale, 171f * ShopVisualScale, scale);
                Stretch(widget.Outline.rectTransform);
                Stretch(widget.Frame.rectTransform);
                PositionBottomLeft(widget.Confirmation.rectTransform, 0f, 0f, 158f * ShopVisualScale, 125f * ShopVisualScale, scale);
                PositionBottomLeft(widget.Name.rectTransform, 9f * ShopVisualScale, 3f * ShopVisualScale, 140f * ShopVisualScale, 27f * ShopVisualScale, scale);
                PositionBottomLeft(widget.CostBackground.rectTransform, 57f * ShopVisualScale - 1f, 157.5f * ShopVisualScale - 15f, costBackgroundSize.x, costBackgroundSize.y, scale);
                PositionBottomLeft(widget.Price.rectTransform, 57f * ShopVisualScale - 1f, 157.5f * ShopVisualScale - 15f + PriceTextVisualLift, costBackgroundSize.x, costBackgroundSize.y, scale);
                PositionBottomLeft(widget.Frozen.rectTransform, 0f, 0f, 157f * ShopVisualScale, 66f * ShopVisualScale, scale);
            }

            ScaleShopText(upgradeLevelText, 42);
            ScaleShopText(upgradeCostText, 18);
            ScaleShopText(freezeText, 21);
            ScaleShopText(refreshText, 21);
            ScaleShopText(refreshCostText, 18);
            foreach (var widget in slotWidgets)
            {
                ScaleShopText(widget.Name, 16);
                ScaleShopText(widget.Price, 20);
                ScaleShopText(widget.CostInfo.Value, 13);
                ScaleShopText(widget.RegionInfo.Value, 13);
                ScaleShopText(widget.OccupationInfo.Value, 13);
            }
        }

        private static void LayoutInfoRow(InfoRow row, float bottom, float scale)
        {
            PositionBottomLeft(
                row.Root,
                6f * ShopVisualScale,
                bottom * ShopVisualScale,
                132f * ShopVisualScale,
                20f * ShopVisualScale,
                scale);
            PositionBottomLeft(
                row.Icon.rectTransform,
                0f,
                1f * ShopVisualScale,
                18f * ShopVisualScale,
                18f * ShopVisualScale,
                scale);
            PositionBottomLeft(
                row.Value.rectTransform,
                22f * ShopVisualScale,
                0f,
                110f * ShopVisualScale,
                20f * ShopVisualScale,
                scale);
        }

        private static void ScaleShopText(Text text, int referenceSize)
        {
            var scaledSize = Mathf.RoundToInt(referenceSize * ShopVisualScale);
            text.fontSize = scaledSize;
            text.resizeTextMaxSize = scaledSize;
        }

        private static int FrameLevel(int rarity)
        {
            if (rarity >= 6) return 3;
            return rarity >= 4 ? 2 : 1;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            return value.GetComponent<RectTransform>();
        }

        private static RectTransform ButtonRoot(string name, Transform parent, UnityEngine.Events.UnityAction action, out Button button)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            value.transform.SetParent(parent, false);
            var hitTarget = value.GetComponent<Image>();
            hitTarget.color = new Color(1f, 1f, 1f, .001f);
            button = value.GetComponent<Button>();
            button.targetGraphic = hitTarget;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(action);
            return value.GetComponent<RectTransform>();
        }

        private static Image Image(string name, Transform parent, string resourcePath)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            value.transform.SetParent(parent, false);
            var image = value.GetComponent<Image>();
            image.sprite = FormalHudSpriteLoader.Load(resourcePath);
            image.color = Color.white;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        private static InfoRow CreateInfoRow(string name, Transform parent, bool numeric)
        {
            var row = Rect(name, parent);
            var icon = Image("Icon", row, null);
            icon.preserveAspect = true;
            var value = numeric
                ? NumberLabel("Value", row, 13, TextAnchor.MiddleLeft, Color.white)
                : Label("Value", row, 13, TextAnchor.MiddleLeft, Color.white);
            return new InfoRow
            {
                Root = row,
                Icon = icon,
                Value = value
            };
        }

        private static Text Label(string name, Transform parent, int fontSize, TextAnchor alignment, Color color)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            value.transform.SetParent(parent, false);
            var text = value.GetComponent<Text>();
            if (shopChineseFont == null) shopChineseFont = Resources.Load<Font>(ShopChineseFontPath);
            text.font = shopChineseFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = fontSize;
            return text;
        }

        private static Text NumberLabel(string name, Transform parent, int fontSize, TextAnchor alignment, Color color)
        {
            var text = Label(name, parent, fontSize, alignment, color);
            text.font = StagingHudController.FormalNumericFont;
            return text;
        }

        private static void SetRect(RectTransform target, ShopReadyHudRect rect)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.zero;
            target.pivot = Vector2.zero;
            target.anchoredPosition = new Vector2(rect.Left, rect.Bottom);
            target.sizeDelta = new Vector2(rect.Width, rect.Height);
        }

        private static void PositionBottomLeft(RectTransform target, float left, float bottom, float width, float height, float scale)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.zero;
            target.pivot = Vector2.zero;
            target.anchoredPosition = new Vector2(left * scale, bottom * scale);
            target.sizeDelta = new Vector2(width * scale, height * scale);
        }

        private static void Stretch(RectTransform target)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.pivot = new Vector2(.5f, .5f);
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Loads both Sprite-imported and Texture-imported HUD artwork without changing source GUIDs.</summary>
    public static class FormalHudSpriteLoader
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Sprite> AtlasCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Sprite Load(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return null;
            if (Cache.TryGetValue(resourcePath, out var cached)) return cached;

            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
            {
                var texture = Resources.Load<Texture2D>(resourcePath);
                if (texture != null)
                {
                    sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(.5f, .5f),
                        100f);
                    sprite.name = texture.name;
                }
            }

            Cache[resourcePath] = sprite;
            return sprite;
        }

        public static Sprite LoadAtlasSprite(string resourcePath, string spriteName)
        {
            if (string.IsNullOrWhiteSpace(resourcePath) || string.IsNullOrWhiteSpace(spriteName))
                return null;
            var key = resourcePath + "#" + spriteName;
            if (AtlasCache.TryGetValue(key, out var cached)) return cached;

            var sprite = Resources.LoadAll<Sprite>(resourcePath)
                .FirstOrDefault(item => string.Equals(item.name, spriteName, StringComparison.Ordinal));
            AtlasCache[key] = sprite;
            return sprite;
        }
    }

    internal sealed class ShopSlotPointerView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Image outline;

        public void Initialize(Image target)
        {
            outline = target;
            if (outline != null) outline.gameObject.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (outline != null) outline.gameObject.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (outline != null) outline.gameObject.SetActive(false);
        }
    }
}
