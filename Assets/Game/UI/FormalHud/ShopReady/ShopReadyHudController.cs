using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Player;
using UnityEngine;
using UnityEngine.UI;

namespace ArknoNights.UI.FormalHud.ShopReady
{
    /// <summary>Scene-attachable right HUD. UI-010 owns attaching it and supplies the already-loaded LocalMatchState.</summary>
    public sealed class ShopReadyHudController : MonoBehaviour
    {
        private sealed class SlotWidgets
        {
            public int SlotId;
            public GameObject Root;
            public Text Label;
            public Button Purchase;
            public Button Freeze;
        }

        private readonly List<SlotWidgets> slotWidgets = new List<SlotWidgets>();
        private LocalMatchState match;
        private ShopReadyHudState state;
        private RectTransform root;
        private RectTransform levelPanel;
        private RectTransform shopPanel;
        private Button shopToggle;
        private Button readyButton;
        private Button refreshButton;
        private Button upgradeButton;
        private Text levelText;
        private Text goldText;
        private Text confirmationText;
        private bool shopVisible = true;
        private readonly ShopReadyPendingCommand pendingCommand = new ShopReadyPendingCommand();

        public event Action<bool> FormationInteractionChanged;
        public event Action<LocalMatchOperationCode> CommandCompleted;

        public bool IsInitialized => match != null;
        public ShopReadyHudState State => state;

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
            shopVisible = visible;
            if (!visible) pendingCommand.Clear();
            if (match != null) Refresh(match.Snapshot);
        }

        public void ToggleShopVisible() => SetShopVisible(!shopVisible);

        public void RequestRefresh()
        {
            if (!EnsureConfirmation(ShopReadyConfirmation.Refresh)) return;
            Complete(match.TryRefresh());
        }

        public void RequestUpgrade()
        {
            if (!EnsureConfirmation(ShopReadyConfirmation.Upgrade)) return;
            Complete(match.TryUpgrade());
        }

        public void Purchase(int shopSlotId)
        {
            if (match == null || state == null || !state.Slots.Any(slot => slot.ShopSlotId == shopSlotId && slot.CanPurchase)) return;
            if (!pendingCommand.RequestPurchase(shopSlotId))
            {
                Refresh(match.Snapshot);
                return;
            }
            Refresh(match.Snapshot);
            if (!state.Slots.Any(slot => slot.ShopSlotId == shopSlotId && slot.CanPurchase)) return;
            Complete(match.TryPurchase(shopSlotId));
        }

        public void ToggleFrozen(int shopSlotId)
        {
            if (state == null || !state.Slots.Any(slot => slot.ShopSlotId == shopSlotId && slot.CanToggleFrozen)) return;
            Complete(match.TryToggleFrozen(shopSlotId));
        }

        public void ToggleReady()
        {
            if (match != null) Complete(match.TryToggleReady());
        }

        private void OnDestroy()
        {
            if (match != null) match.Changed -= OnMatchChanged;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (root != null && state != null) ApplyLayout();
        }

        private void OnMatchChanged(LocalMatchSnapshot snapshot) { pendingCommand.Clear(); Refresh(snapshot); }

        private bool EnsureConfirmation(ShopReadyConfirmation requested)
        {
            if (match == null) return false;
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
            levelText.text = "LV " + state.Level;
            goldText.text = "G " + state.Gold;
            readyButton.GetComponentInChildren<Text>().text = state.IsReady ? "\u53d6\u6d88\u51c6\u5907" : "\u51c6\u5907\u5c31\u7eea";
            shopPanel.gameObject.SetActive(state.ShopVisible);
            confirmationText.gameObject.SetActive(state.PendingConfirmation != ShopReadyConfirmation.None);
            confirmationText.text = state.PendingConfirmation == ShopReadyConfirmation.Purchase
                ? "\u518d\u6b21\u70b9\u51fb\u8d2d\u4e70\u4ee5\u786e\u8ba4"
                : state.PendingConfirmation == ShopReadyConfirmation.Refresh
                    ? "\u518d\u6b21\u70b9\u51fb\u5237\u65b0\u4ee5\u786e\u8ba4"
                    : "\u518d\u6b21\u70b9\u51fb\u5347\u7ea7\u4ee5\u786e\u8ba4";
            RebuildSlots();
            ApplyLayout();
            FormationInteractionChanged?.Invoke(state.FormationInteractionEnabled);
        }

        private void EnsureView()
        {
            if (root != null) return;
            root = GetComponent<RectTransform>();
            if (root == null) root = gameObject.AddComponent<RectTransform>();
            levelPanel = Panel("LevelPanel", root, new Color(.08f, .12f, .18f, .94f));
            SetSprite(levelPanel.GetComponent<Image>(), "UI/Texture/shop/level");
            levelText = Label("Level", levelPanel, 24, TextAnchor.MiddleCenter);
            Stretch(levelText.rectTransform);
            shopPanel = Panel("ShopPanel", root, new Color(.08f, .12f, .18f, .94f));
            shopToggle = Button("ShopToggle", root, "SHOP", ToggleShopVisible);
            readyButton = Button("Ready", root, "READY", ToggleReady);
            refreshButton = Button("Refresh", shopPanel, "REFRESH", RequestRefresh);
            upgradeButton = Button("Upgrade", shopPanel, "UPGRADE", RequestUpgrade);
            goldText = Label("Gold", shopPanel, 20, TextAnchor.MiddleLeft);
            confirmationText = Label("Confirmation", shopPanel, 18, TextAnchor.MiddleCenter);
        }

        private void RebuildSlots()
        {
            foreach (var widget in slotWidgets)
            {
                if (Application.isPlaying) Destroy(widget.Root);
                else DestroyImmediate(widget.Root);
            }
            slotWidgets.Clear();
            foreach (var slot in state.Slots)
            {
                var widget = new SlotWidgets { SlotId = slot.ShopSlotId, Root = new GameObject("ShopSlot_" + slot.ShopSlotId, typeof(RectTransform), typeof(Image)) };
                widget.Root.transform.SetParent(shopPanel, false);
                SetSprite(widget.Root.GetComponent<Image>(), slot.IsEmpty ? "UI/Texture/shop/bg_empty" : "UI/Texture/shop/bg_black");
                widget.Label = Label("Label", widget.Root.transform, 16, TextAnchor.MiddleLeft);
                widget.Label.text = slot.IsEmpty ? "EMPTY" : slot.UnitTypeId + "  " + slot.Price;
                widget.Purchase = Button("Purchase", widget.Root.transform, "BUY", () => Purchase(slot.ShopSlotId));
                widget.Purchase.interactable = slot.CanPurchase;
                widget.Freeze = Button("Freeze", widget.Root.transform, slot.IsFrozen ? "UNFREEZE" : "FREEZE", () => ToggleFrozen(slot.ShopSlotId));
                widget.Freeze.interactable = slot.CanToggleFrozen;
                slotWidgets.Add(widget);
                SetSprite(widget.Purchase.GetComponent<Image>(), slot.CanPurchase ? "UI/Texture/shop/cost_bg_1" : "UI/Texture/shop/cost_bg_2");
                SetSprite(widget.Freeze.GetComponent<Image>(), slot.IsFrozen ? "UI/Texture/shop/frozen_bg_unselect" : "UI/Texture/shop/frozen_bg_normal");
            }
        }

        private void ApplyLayout()
        {
            if (root.rect.width <= 0f || root.rect.height <= 0f) return;
            var layout = ShopReadyHudLayout.Calculate(root.rect.width, root.rect.height);
            SetRect(levelPanel, layout.LevelPanel);
            SetRect(shopPanel, layout.ShopPanel);
            SetRect(shopToggle.GetComponent<RectTransform>(), layout.ShopToggle);
            SetRect(readyButton.GetComponent<RectTransform>(), layout.ReadyButton);
            Position(refreshButton.GetComponent<RectTransform>(), 110f, 30f, 92f, 36f);
            Position(upgradeButton.GetComponent<RectTransform>(), 210f, 30f, 92f, 36f);
            Position(goldText.rectTransform, 30f, 30f, 110f, 36f);
            Position(confirmationText.rectTransform, 255f, 275f, 230f, 30f);
            for (var index = 0; index < slotWidgets.Count; index++)
            {
                var widget = slotWidgets[index];
                var slotRoot = widget.Root.GetComponent<RectTransform>();
                Position(slotRoot, 255f, 236f - index * 42f, 480f, 36f);
                Position(widget.Label.rectTransform, 135f, 18f, 250f, 32f);
                Position(widget.Purchase.GetComponent<RectTransform>(), 350f, 18f, 80f, 30f);
                Position(widget.Freeze.GetComponent<RectTransform>(), 435f, 18f, 80f, 30f);
            }
        }

        private static RectTransform Panel(string name, Transform parent, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return panel.GetComponent<RectTransform>();
        }

        private static void SetSprite(Image image, string resourcePath)
        {
            if (image == null) return;
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null) return;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }

        private static Text Label(string name, Transform parent, int fontSize, TextAnchor alignment)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            value.transform.SetParent(parent, false);
            var text = value.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button Button(string name, Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            value.transform.SetParent(parent, false);
            value.GetComponent<Image>().color = new Color(.23f, .42f, .62f, 1f);
            var button = value.GetComponent<Button>();
            button.onClick.AddListener(action);
            var text = Label("Text", value.transform, 16, TextAnchor.MiddleCenter);
            text.text = label;
            Stretch(text.rectTransform);
            return button;
        }

        private static void SetRect(RectTransform target, ShopReadyHudRect rect)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.zero;
            target.pivot = Vector2.zero;
            target.anchoredPosition = new Vector2(rect.Left, rect.Bottom);
            target.sizeDelta = new Vector2(rect.Width, rect.Height);
        }

        private static void Position(RectTransform target, float x, float y, float width, float height)
        {
            target.anchorMin = target.anchorMax = new Vector2(.5f, .5f);
            target.pivot = new Vector2(.5f, .5f);
            target.anchoredPosition = new Vector2(x, y);
            target.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform target)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.offsetMin = target.offsetMax = Vector2.zero;
        }
    }
}
