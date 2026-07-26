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
        private static readonly int[] UpgradeCosts = { 4, 6, 8, 10, 12, 14, 16, 18 };

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
        }

        private readonly List<SlotWidgets> slotWidgets = new List<SlotWidgets>();
        private readonly ShopReadyPendingCommand pendingCommand = new ShopReadyPendingCommand();
        private LocalMatchState match;
        private ShopReadyHudState state;
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
        private Image upgradeBackground;
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
        private bool preparationPhase = true;

        public event Action<bool> FormationInteractionChanged;
        public event Action<LocalMatchOperationCode> CommandCompleted;

        public bool IsInitialized => match != null;
        public ShopReadyHudState State => state;
        public bool IsPreparationPhase => preparationPhase;

        public void Initialize(LocalMatchState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (match != null) match.Changed -= OnMatchChanged;
            match = source;
            match.Changed += OnMatchChanged;
            EnsureView();
            Refresh(match.Snapshot);
        }

        public void SetShopVisible(bool visible)
        {
            shopVisible = preparationPhase && visible;
            if (!shopVisible) pendingCommand.Clear();
            if (match != null) Refresh(match.Snapshot);
        }

        public void ToggleShopVisible() => SetShopVisible(!shopVisible);

        /// <summary>Battle hides this surface and clears transient confirmations without mutating shop state.</summary>
        public void SetPreparationPhase(bool isPreparation)
        {
            preparationPhase = isPreparation;
            if (!preparationPhase)
            {
                shopVisible = false;
                pendingCommand.Clear();
            }

            if (match != null) Refresh(match.Snapshot);
            if (root != null) root.gameObject.SetActive(preparationPhase);
        }

        public void RequestRefresh()
        {
            if (!preparationPhase || match == null || state == null || state.Gold < RefreshCost) return;
            if (!EnsureConfirmation(ShopReadyConfirmation.Refresh)) return;
            Complete(match.TryRefresh());
        }

        public void RequestUpgrade()
        {
            if (!preparationPhase || match == null || state == null) return;
            var upgradeCost = UpgradeCost(state.Level);
            if (state.Level >= MaximumLevel || state.Gold < upgradeCost) return;
            if (!EnsureConfirmation(ShopReadyConfirmation.Upgrade)) return;
            Complete(match.TryUpgrade());
        }

        public void Purchase(int shopSlotId)
        {
            if (!preparationPhase || match == null || state == null) return;
            var slot = state.Slots.FirstOrDefault(item => item.ShopSlotId == shopSlotId);
            if (slot == null || !slot.CanPurchase) return;
            if (!pendingCommand.RequestPurchase(shopSlotId))
            {
                Refresh(match.Snapshot);
                return;
            }

            Refresh(match.Snapshot);
            if (!state.Slots.Any(item => item.ShopSlotId == shopSlotId && item.CanPurchase)) return;
            Complete(match.TryPurchase(shopSlotId));
        }

        public void ToggleFrozen(int shopSlotId)
        {
            if (!preparationPhase || state == null) return;
            if (!state.Slots.Any(slot => slot.ShopSlotId == shopSlotId && slot.CanToggleFrozen)) return;
            Complete(match.TryToggleFrozen(shopSlotId));
        }

        public void ToggleReady()
        {
            if (preparationPhase && match != null) Complete(match.TryToggleReady());
        }

        private void ToggleFocusedFrozen()
        {
            if (state == null) return;
            var selected = state.Slots.FirstOrDefault(slot =>
                slot.ShopSlotId == pendingCommand.ShopSlotId && slot.CanToggleFrozen);
            var target = selected
                ?? state.Slots.FirstOrDefault(slot => slot.IsFrozen && slot.CanToggleFrozen)
                ?? state.Slots.FirstOrDefault(slot => slot.CanToggleFrozen);
            if (target != null) ToggleFrozen(target.ShopSlotId);
        }

        private void OnDestroy()
        {
            if (match != null) match.Changed -= OnMatchChanged;
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
            if (!confirmed) Refresh(match.Snapshot);
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
            EnsureView();
            state = ShopReadyHudState.Project(snapshot, shopVisible, pendingCommand.Kind);

            levelText.text = state.Level.ToString();
            readyText.text = state.IsReady ? "取消准备" : "准备就绪";
            readyIcon.sprite = FormalHudSpriteLoader.Load(
                state.IsReady ? "UI/Texture/ready/ready_icon" : "UI/Texture/ready/icon_ready");

            shopPanel.gameObject.SetActive(state.ShopVisible);
            var upgradeCost = UpgradeCost(state.Level);
            var canUpgrade = state.Level < MaximumLevel && state.Gold >= upgradeCost;
            upgradeButton.interactable = canUpgrade;
            upgradeBackground.sprite = FormalHudSpriteLoader.Load(
                canUpgrade ? "UI/Texture/shop/upgrade_max" : "UI/Texture/shop/upgrade_disable");
            upgradeLevelText.text = state.Level.ToString();
            upgradeCostText.text = state.Level >= MaximumLevel ? "MAX" : upgradeCost.ToString();
            var upgradePending = pendingCommand.Kind == ShopReadyConfirmation.Upgrade;
            upgradeFrame.gameObject.SetActive(upgradePending);
            upgradeGradient.gameObject.SetActive(upgradePending);

            refreshButton.interactable = state.Gold >= RefreshCost;
            refreshIcon.sprite = FormalHudSpriteLoader.Load(
                refreshButton.interactable ? "UI/Texture/shop/refresh_icon" : "UI/Texture/shop/refresh_icon_lock");
            refreshCostText.text = RefreshCost.ToString();

            EnsureSlots();
            for (var index = 0; index < state.Slots.Count; index++)
                BindSlot(slotWidgets[index], state.Slots[index]);

            var frozenTarget = state.Slots.FirstOrDefault(slot =>
                slot.ShopSlotId == pendingCommand.ShopSlotId && slot.CanToggleFrozen)
                ?? state.Slots.FirstOrDefault(slot => slot.IsFrozen && slot.CanToggleFrozen)
                ?? state.Slots.FirstOrDefault(slot => slot.CanToggleFrozen);
            freezeButton.interactable = frozenTarget != null;
            var unfreezing = frozenTarget != null && frozenTarget.IsFrozen;
            freezeBackground.sprite = FormalHudSpriteLoader.Load(
                unfreezing ? "UI/Texture/shop/frozen_bg_unselect" : "UI/Texture/shop/frozen_bg_normal");
            freezeIcon.sprite = FormalHudSpriteLoader.Load(
                unfreezing ? "UI/Texture/shop/frozen_icon2" : "UI/Texture/shop/frozen_icon");
            freezeText.text = unfreezing ? "解除冻结" : "冻结";

            ApplyLayout();
            FormationInteractionChanged?.Invoke(preparationPhase && state.FormationInteractionEnabled);
        }

        private void EnsureView()
        {
            if (root != null) return;
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
            levelText = Label("Level", levelButtonRoot, 44, TextAnchor.MiddleCenter, Color.white);

            shopPanel = Rect("ShopPanel", root);
            var upgradeRoot = ButtonRoot("UpgradeButton", shopPanel, RequestUpgrade, out upgradeButton);
            upgradeBackground = Image("Background", upgradeRoot, "UI/Texture/shop/upgrade_max");
            Stretch(upgradeBackground.rectTransform);
            upgradeFrame = Image("ConfirmationFrame", upgradeRoot, "UI/Texture/shop/check_frame");
            Stretch(upgradeFrame.rectTransform);
            upgradeGradient = Image("ConfirmationGradient", upgradeRoot, "UI/Texture/shop/check_grad");
            upgradeLevelText = Label("Level", upgradeRoot, 42, TextAnchor.MiddleCenter, Color.white);
            upgradeCostText = Label("Cost", upgradeRoot, 18, TextAnchor.MiddleCenter, new Color(1f, .82f, .08f));

            var freezeRoot = ButtonRoot("FreezeButton", shopPanel, ToggleFocusedFrozen, out freezeButton);
            freezeBackground = Image("Background", freezeRoot, "UI/Texture/shop/frozen_bg_normal");
            Stretch(freezeBackground.rectTransform);
            freezeIcon = Image("Icon", freezeRoot, "UI/Texture/shop/frozen_icon");
            freezeText = Label("Label", freezeRoot, 21, TextAnchor.MiddleCenter, Color.white);

            var refreshRoot = ButtonRoot("RefreshButton", shopPanel, RequestRefresh, out refreshButton);
            refreshBackground = Image("Background", refreshRoot, "UI/Texture/shop/refresh_bg_normal");
            Stretch(refreshBackground.rectTransform);
            refreshIcon = Image("Icon", refreshRoot, "UI/Texture/shop/refresh_icon");
            refreshText = Label("Label", refreshRoot, 21, TextAnchor.MiddleCenter, Color.white);
            refreshText.text = "刷新";
            refreshCostText = Label("Cost", refreshRoot, 18, TextAnchor.MiddleCenter, new Color(1f, .82f, .08f));

            readyButtonRoot = ButtonRoot("ReadyButton", root, ToggleReady, out readyButton);
            readyBackground = Image("Background", readyButtonRoot, "UI/Texture/ready/ready_bg");
            Stretch(readyBackground.rectTransform);
            readyFrame = Image("Frame", readyButtonRoot, "UI/Texture/ready/ready_frame");
            Stretch(readyFrame.rectTransform);
            readyIcon = Image("Icon", readyButtonRoot, "UI/Texture/ready/icon_ready");
            readyText = Label("Label", readyButtonRoot, 24, TextAnchor.MiddleCenter, Color.white);
        }

        private void EnsureSlots()
        {
            if (slotWidgets.Count == state.Slots.Count) return;
            foreach (var widget in slotWidgets)
            {
                if (Application.isPlaying) Destroy(widget.Root.gameObject);
                else DestroyImmediate(widget.Root.gameObject);
            }

            slotWidgets.Clear();
            foreach (var slot in state.Slots)
            {
                var rootRect = ButtonRoot("ShopSlot_" + slot.ShopSlotId, shopPanel, () => Purchase(slot.ShopSlotId), out var button);
                var background = Image("Background", rootRect, "UI/Texture/shop/bg_black");
                var portraitClip = Rect("PortraitClip", rootRect);
                portraitClip.gameObject.AddComponent<RectMask2D>();
                var portrait = Image("Portrait", portraitClip, null);
                var unaffordable = Image("UnaffordableOverlay", rootRect, "UI/Texture/shop/bg_common");
                var outline = Image("HoverOutline", rootRect, "UI/Texture/shop/frame_outline");
                var frame = Image("RarityFrame", rootRect, "UI/Texture/shop/frame_lv1");
                var confirmation = Image("PurchaseConfirmation", rootRect, "UI/Texture/shop/bg_doublecheck1");
                var name = Label("UnitName", rootRect, 16, TextAnchor.MiddleLeft, Color.white);
                var costBackground = Image("CostBackground", rootRect, "UI/Texture/shop/cost_bg_1");
                var price = Label("Price", rootRect, 20, TextAnchor.MiddleCenter, Color.white);
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
                    Price = price
                });
            }
        }

        private void BindSlot(SlotWidgets widget, ShopReadySlotViewState slot)
        {
            widget.Purchase.interactable = !slot.IsEmpty;
            widget.Background.sprite = FormalHudSpriteLoader.Load(
                slot.IsEmpty ? "UI/Texture/shop/bg_empty" : "UI/Texture/shop/bg_black");
            widget.Portrait.sprite = slot.IsEmpty ? null : FormalHudSpriteLoader.Load(slot.PortraitResourcePath);
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

            PositionBottomLeft(upgradeButton.GetComponent<RectTransform>(), 0f, 78f, 111f, 175f, scale);
            Stretch(upgradeBackground.rectTransform);
            Stretch(upgradeFrame.rectTransform);
            PositionBottomLeft(upgradeGradient.rectTransform, 0f, 18f, 111f, 136f, scale);
            PositionBottomLeft(upgradeLevelText.rectTransform, 8f, 44f, 95f, 92f, scale);
            PositionBottomLeft(upgradeCostText.rectTransform, 8f, 143f, 95f, 27f, scale);

            PositionBottomLeft(freezeButton.GetComponent<RectTransform>(), 744f, 0f, 149f, 77f, scale);
            PositionBottomLeft(freezeIcon.rectTransform, 15f, 23f, 31f, 31f, scale);
            PositionBottomLeft(freezeText.rectTransform, 45f, 18f, 96f, 40f, scale);

            PositionBottomLeft(refreshButton.GetComponent<RectTransform>(), 902f, 0f, 147f, 77f, scale);
            PositionBottomLeft(refreshIcon.rectTransform, 15f, 23f, 31f, 31f, scale);
            PositionBottomLeft(refreshText.rectTransform, 45f, 18f, 92f, 40f, scale);
            PositionBottomLeft(refreshCostText.rectTransform, 112f, 55f, 31f, 22f, scale);

            PositionBottomLeft(readyIcon.rectTransform, 18f, 15f, 30f, 30f, scale);
            PositionBottomLeft(readyText.rectTransform, 48f, 7f, 124f, 46f, scale);

            for (var index = 0; index < slotWidgets.Count; index++)
            {
                var widget = slotWidgets[index];
                PositionBottomLeft(widget.Root, 132f + index * 166f, 78f, 158f, 175f, scale);
                Stretch(widget.Background.rectTransform);
                PositionBottomLeft(widget.Portrait.transform.parent as RectTransform, 5f, 22f, 148f, 146f, scale);
                PositionBottomLeft(widget.Portrait.rectTransform, -4f, -3f, 156f, 156f, scale);
                PositionBottomLeft(widget.Unaffordable.rectTransform, 1f, 2f, 156f, 171f, scale);
                Stretch(widget.Outline.rectTransform);
                Stretch(widget.Frame.rectTransform);
                PositionBottomLeft(widget.Confirmation.rectTransform, 0f, 0f, 158f, 125f, scale);
                PositionBottomLeft(widget.Name.rectTransform, 9f, 3f, 140f, 27f, scale);
                PositionBottomLeft(widget.CostBackground.rectTransform, 57f, 162f, 44f, 35f, scale);
                PositionBottomLeft(widget.Price.rectTransform, 57f, 162f, 44f, 35f, scale);
                PositionBottomLeft(widget.Frozen.rectTransform, 0f, 0f, 157f, 66f, scale);
            }
        }

        private static int UpgradeCost(int level)
        {
            return level >= 1 && level < MaximumLevel ? UpgradeCosts[level - 1] : 0;
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

        private static Text Label(string name, Transform parent, int fontSize, TextAnchor alignment, Color color)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            value.transform.SetParent(parent, false);
            var text = value.GetComponent<Text>();
            text.font = StagingHudController.FormalUiFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = fontSize;
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
