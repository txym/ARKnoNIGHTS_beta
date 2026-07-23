using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArknoNights.UI
{
    /// <summary>
    /// Scene-owned UI-002 entry point. It creates exactly one local PlayerState and projects its
    /// read-only snapshots into the formal HUD; it never derives slots from legacy scene objects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StagingHudController : MonoBehaviour
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string PlayerStatePath = "PlayerData/local-player-state-v1";
        private const string AtlasPath = "UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged";
        private const string FormalUiFontPath = "UI/Fonts/NotoSansSC-VF";
        private const string FormalNumericFontPath = "Fonts/Novecento wide Normal Regular.woff2";
        private static Font formalUiFont;
        private static Font formalNumericFont;

        [Header("UI-001 Player-safe input")]
        [SerializeField] private string catalogResourcePath = CatalogPath;
        [SerializeField] private string playerStateResourcePath = PlayerStatePath;
        [Header("Formal HUD")]
        [SerializeField] private int sortingOrder = 200;

        private readonly List<StagingSlotView> slotViews = new List<StagingSlotView>();
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private PlayerState playerState;
        private PlayerStateSnapshot snapshot;
        private Canvas canvas;
        private RectTransform hudRoot;
        private RectTransform stagingArea;
        private RectTransform costPanel;
        private Text costText;
        private Text statusText;
        private Font font;
        private string selectedSlotId;
        private Action<string> stagingDragStarted;
        private Action<string> stagingSelectionChanged;
        private Vector2 lastCanvasSize = new Vector2(-1f, -1f);
        private bool initialized;

        public PlayerState PlayerState => playerState;
        public PlayerStateSnapshot Snapshot => snapshot;
        public string SelectedSlotId => selectedSlotId;
        public int SlotCount => slotViews.Count;
        public bool InitializationSucceeded => initialized && playerState != null;
        /// <summary>Shared embedded UI typeface: Noto Sans SC, Normal weight, default UGUI character spacing.</summary>
        public static Font FormalUiFont
        {
            get
            {
                if (formalUiFont == null) formalUiFont = Resources.Load<Font>(FormalUiFontPath);
                return formalUiFont != null ? formalUiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }
        /// <summary>Latin digit typeface for costs, counts, timers, health values, and battle counters.</summary>
        public static Font FormalNumericFont
        {
            get
            {
                if (formalNumericFont == null) formalNumericFont = Resources.Load<Font>(FormalNumericFontPath);
                return formalNumericFont != null ? formalNumericFont : FormalUiFont;
            }
        }
        /// <summary>Read-only notification for HUD extensions. The stable stack ID remains authoritative.</summary>
        public event Action<string> StagingSelectionChanged;

        private void Awake()
        {
            CreateHud();
            LoadPlayerState();
        }

        private void LateUpdate()
        {
            if (!initialized || !hudRoot) return;
            var size = hudRoot.rect.size;
            if ((size - lastCanvasSize).sqrMagnitude > 0.01f)
            {
                lastCanvasSize = size;
                ApplyLayout();
            }
        }

        private void OnDestroy()
        {
            if (playerState != null) playerState.Changed -= HandlePlayerStateChanged;
            stagingDragStarted = null;
            stagingSelectionChanged = null;
            StagingSelectionChanged = null;
            playerState = null;
        }

        private void CreateHud()
        {
            font = FormalUiFont;
            foreach (var sprite in Resources.LoadAll<Sprite>(AtlasPath))
            {
                if (sprite != null && !string.IsNullOrEmpty(sprite.name)) sprites[sprite.name] = sprite;
            }

            var canvasObject = new GameObject("FormalBattleHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            hudRoot = canvasObject.transform as RectTransform;
            Stretch(hudRoot);
            stagingArea = CreateRect("StagingArea", hudRoot);
            stagingArea.anchorMin = Vector2.zero;
            stagingArea.anchorMax = new Vector2(1f, 0f);
            stagingArea.pivot = Vector2.zero;
            stagingArea.anchoredPosition = Vector2.zero;

            costPanel = CreateDeploymentCostPanel(hudRoot);
            statusText = CreateText("InitializationStatus", hudRoot, 13, TextAnchor.LowerLeft, new Color(1f, 0.75f, 0.35f));
            statusText.rectTransform.anchorMin = new Vector2(0f, 0f);
            statusText.rectTransform.anchorMax = new Vector2(0f, 0f);
            statusText.rectTransform.pivot = new Vector2(0f, 0f);
            statusText.rectTransform.anchoredPosition = new Vector2(12f, 12f);
            statusText.rectTransform.sizeDelta = new Vector2(900f, 40f);
            statusText.gameObject.SetActive(false);
        }

        private void LoadPlayerState()
        {
            var loaded = LocalPlayerStateLoader.LoadFromResources(catalogResourcePath, playerStateResourcePath);
            if (!loaded.Success)
            {
                var error = string.Join(";", loaded.Errors.Select(item => item.ToString()).ToArray());
                Debug.LogError("[StagingHud][playerState.load.failed] " + error, this);
                ShowFailure("Player state unavailable: " + error);
                return;
            }

            playerState = loaded.State;
            playerState.Changed += HandlePlayerStateChanged;
            initialized = true;
            HandlePlayerStateChanged(playerState.Snapshot);
            var zoneSummary = string.Join(",", Enum.GetValues(typeof(PlayerUnitZone)).Cast<PlayerUnitZone>().Select(zone => zone + "=" + playerState.GetUnits(zone).Count).ToArray());
            Debug.Log("[StagingHud][playerState.loaded] schema=" + LocalPlayerStateLoader.SchemaVersion + "; player=" + playerState.PlayerId + "; cost=" + playerState.DeploymentCost + "; zones=" + zoneSummary + "; stagingSlots=" + snapshot.StagingSlots.Count, this);
        }

        private void HandlePlayerStateChanged(PlayerStateSnapshot next)
        {
            snapshot = next;
            var availableIds = new HashSet<string>(next.StagingSlots.Select(BuildSlotId), StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(selectedSlotId) && !availableIds.Contains(selectedSlotId))
            {
                selectedSlotId = null;
                stagingSelectionChanged?.Invoke(null);
                StagingSelectionChanged?.Invoke(null);
            }
            RebuildSlots();
            if (costText) costText.text = next.DeploymentCost.ToString();
            if (statusText) statusText.gameObject.SetActive(false);
        }

        private void RebuildSlots()
        {
            foreach (var existing in slotViews) if (existing != null) Destroy(existing.gameObject);
            slotViews.Clear();
            if (snapshot == null) return;

            foreach (var stack in snapshot.StagingSlots)
            {
                var view = StagingSlotView.Create(stagingArea, font, sprites, HandleSlotClicked, HandleSlotDragStarted);
                view.Bind(stack, BuildSlotId(stack), LoadPortrait(stack));
                slotViews.Add(view);
            }
            ApplyLayout();
        }

        private Sprite LoadPortrait(StagingStackSnapshot stack)
        {
            if (string.IsNullOrWhiteSpace(stack.PortraitResourcePath))
            {
                Debug.LogError("[StagingHud][portrait.path.missing] type=" + stack.TypeId, this);
                return null;
            }
            var portrait = Resources.Load<Sprite>(stack.PortraitResourcePath);
            if (portrait == null) Debug.LogError("[StagingHud][portrait.missing] type=" + stack.TypeId + "; resource=" + stack.PortraitResourcePath, this);
            return portrait;
        }

        private void HandleSlotClicked(string slotId)
        {
            ToggleSelection(slotId);
        }

        private void HandleSlotDragStarted(string slotId)
        {
            stagingDragStarted?.Invoke(slotId);
        }

        /// <summary>UI-003 bridge. Slots report a stable stack ID while drag state remains outside the HUD.</summary>
        public void SetStagingDragStartedHandler(Action<string> handler)
        {
            stagingDragStarted = handler;
        }

        /// <summary>UI-003 bridge for the shared staging/world selection state.</summary>
        public void SetStagingSelectionChangedHandler(Action<string> handler)
        {
            stagingSelectionChanged = handler;
        }

        /// <summary>UI-003 command boundary: selection is visual-only and is cleared when its stack disappears.</summary>
        public void ToggleSelection(string slotId)
        {
            if (string.IsNullOrEmpty(slotId) || !slotViews.Any(view => string.Equals(view.SlotId, slotId, StringComparison.Ordinal))) return;
            selectedSlotId = string.Equals(selectedSlotId, slotId, StringComparison.Ordinal) ? null : slotId;
            ApplyLayout();
            stagingSelectionChanged?.Invoke(selectedSlotId);
            StagingSelectionChanged?.Invoke(selectedSlotId);
        }

        /// <summary>Clears the staging half of the shared UI-003 selection without touching PlayerState.</summary>
        public void ClearStagingSelection()
        {
            if (string.IsNullOrEmpty(selectedSlotId)) return;
            selectedSlotId = null;
            ApplyLayout();
            stagingSelectionChanged?.Invoke(null);
            StagingSelectionChanged?.Invoke(null);
        }

        private void ApplyLayout()
        {
            if (!hudRoot || !stagingArea || snapshot == null) return;
            var size = hudRoot.rect.size;
            if (size.x <= 0f || size.y <= 0f) return;
            var selectedIndex = slotViews.FindIndex(view => string.Equals(view.SlotId, selectedSlotId, StringComparison.Ordinal));
            var layout = StagingHudLayout.Calculate(size.x, size.y, slotViews.Count, selectedIndex);
            stagingArea.sizeDelta = new Vector2(0f, layout.SlotHeight);
            stagingArea.gameObject.SetActive(slotViews.Count > 0);
            for (var index = 0; index < slotViews.Count; index++)
            {
                var slot = layout.Slots[index];
                slotViews[index].ApplyLayout(slot, layout.PortraitSize, layout.SlotHeight);
            }
            ApplyCostPanelLayout(layout.PortraitSize, layout.SlotHeight);
        }

        private void ApplyCostPanelLayout(float portraitSize, float slotHeight)
        {
            if (!costPanel) return;
            var offset = portraitSize / 9f;
            var panelHeight = portraitSize * 4f / 9f;
            costPanel.anchorMin = new Vector2(1f, 0f);
            costPanel.anchorMax = new Vector2(1f, 0f);
            costPanel.pivot = new Vector2(1f, 0f);
            costPanel.sizeDelta = new Vector2(portraitSize, panelHeight);
            costPanel.anchoredPosition = new Vector2(0f, slotHeight + offset);
        }

        private void ShowFailure(string message)
        {
            initialized = false;
            if (stagingArea) stagingArea.gameObject.SetActive(false);
            if (costPanel) costPanel.gameObject.SetActive(false);
            if (statusText)
            {
                statusText.text = message;
                statusText.gameObject.SetActive(true);
            }
        }

        private RectTransform CreateDeploymentCostPanel(RectTransform parent)
        {
            var panel = CreateRect("DeploymentCostPanel", parent);
            var background = CreateImage("Background", panel, SpriteNamed("ResourcePanelBackground"));
            Stretch(background.rectTransform);
            background.preserveAspect = false;
            var icon = CreateImage("Icon", panel, SpriteNamed("DeploymentCostPanelIcon"));
            icon.rectTransform.anchorMin = Vector2.zero;
            icon.rectTransform.anchorMax = Vector2.zero;
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(40f, 40f);
            icon.rectTransform.sizeDelta = new Vector2(46f, 46f);
            icon.preserveAspect = true;
            costText = CreateNumberText("Cost", panel, 54, TextAnchor.MiddleCenter, Color.white);
            costText.rectTransform.anchorMin = Vector2.zero;
            costText.rectTransform.anchorMax = Vector2.zero;
            costText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            costText.rectTransform.anchoredPosition = new Vector2(116f, 40f);
            costText.rectTransform.sizeDelta = new Vector2(90f, 54f);
            return panel;
        }

        private Sprite SpriteNamed(string name)
        {
            if (sprites.TryGetValue(name, out var sprite)) return sprite;
            Debug.LogError("[StagingHud][sprite.missing] " + name, this);
            return null;
        }

        public static string BuildSlotId(StagingStackSnapshot slot) => slot.TypeId + "|E" + slot.EliteLevel + "|B" + string.Join(",", slot.Buffs.Select(buff => buff.Id + ":" + buff.RawPayload).ToArray()) + "|U" + string.Join(",", slot.UnitIds.ToArray());

        internal static RectTransform CreateRect(string name, Transform parent)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            return value.GetComponent<RectTransform>();
        }

        internal static Image CreateImage(string name, Transform parent, Sprite sprite)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            value.transform.SetParent(parent, false);
            var image = value.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        internal static Text CreateText(string name, Transform parent, int fontSize, TextAnchor anchor, Color color)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            value.transform.SetParent(parent, false);
            var text = value.GetComponent<Text>();
            text.font = FormalUiFont;
            text.fontStyle = FontStyle.Normal;
            text.resizeTextForBestFit = false;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        internal static Text CreateNumberText(string name, Transform parent, int fontSize, TextAnchor anchor, Color color)
        {
            var text = CreateText(name, parent, fontSize, anchor, color);
            text.font = FormalNumericFont;
            return text;
        }

        internal static void Stretch(RectTransform target)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
        }
    }

    // StandaloneInputModule only assigns pointerDrag to an IDragHandler. IBeginDragHandler alone is never invoked.
    internal sealed class StagingSlotView : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private readonly Dictionary<int, Image> eliteIcons = new Dictionary<int, Image>();
        private Image background;
        private Image portrait;
        private Image portraitOverlay;
        private Image eliteDecoration;
        private Image eliteHighlight;
        private Image selectionOverlay;
        private Text countText;
        private RectTransform root;
        private RectTransform portraitClip;
        private RectTransform header;
        private RectTransform eliteIconRoot;
        private Action<string> onClicked;
        private Action<string> onDragStarted;
        private int eliteLevel;

        public string SlotId { get; private set; }

        public static StagingSlotView Create(RectTransform parent, Font font, IReadOnlyDictionary<string, Sprite> sprites, Action<string> onClicked, Action<string> onDragStarted)
        {
            var root = new GameObject("StagingSlot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(StagingSlotView));
            root.transform.SetParent(parent, false);
            var view = root.GetComponent<StagingSlotView>();
            view.root = root.GetComponent<RectTransform>();
            view.onClicked = onClicked;
            view.onDragStarted = onDragStarted;
            var hitTarget = root.GetComponent<Image>();
            hitTarget.color = new Color(1f, 1f, 1f, 0f);
            var button = root.GetComponent<Button>();
            button.targetGraphic = hitTarget;
            button.onClick.AddListener(view.Click);
            view.BuildVisuals(sprites);
            return view;
        }

        public void Bind(StagingStackSnapshot stack, string slotId, Sprite portraitSprite)
        {
            SlotId = slotId;
            eliteLevel = stack.EliteLevel;
            portrait.sprite = portraitSprite;
            portrait.enabled = portraitSprite != null;
            countText.text = "X" + stack.Count;
            var icon = GetComponentInChildren<StagingSlotCostTextMarker>();
            if (icon != null) icon.Text.text = stack.DeploymentCost.ToString();
            foreach (var pair in eliteIcons) pair.Value.gameObject.SetActive(pair.Key == eliteLevel);
            eliteDecoration.gameObject.SetActive(eliteLevel == 1);
            eliteHighlight.gameObject.SetActive(eliteLevel == 2 || eliteLevel == 3);
        }

        public void ApplyLayout(StagingHudLayout.Slot slot, float portraitSize, float slotHeight)
        {
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.anchoredPosition = new Vector2(slot.X, slot.OffsetY);
            root.sizeDelta = new Vector2(slot.Width, slotHeight);
            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = new Vector2(1f, 0f);
            background.rectTransform.pivot = Vector2.zero;
            background.rectTransform.sizeDelta = new Vector2(0f, portraitSize * 21f / 20f);
            portraitClip.anchorMin = Vector2.zero;
            portraitClip.anchorMax = new Vector2(1f, 0f);
            portraitClip.pivot = Vector2.zero;
            portraitClip.anchoredPosition = new Vector2(0f, portraitSize / 20f);
            portraitClip.sizeDelta = new Vector2(0f, portraitSize);
            var portraitRect = portrait.rectTransform;
            portraitRect.anchorMin = new Vector2(0.5f, 0.5f);
            portraitRect.anchorMax = new Vector2(0.5f, 0.5f);
            portraitRect.pivot = new Vector2(0.5f, 0.5f);
            portraitRect.anchoredPosition = Vector2.zero;
            portraitRect.sizeDelta = new Vector2(portraitSize, portraitSize);
            portraitOverlay.rectTransform.anchorMin = Vector2.zero;
            portraitOverlay.rectTransform.anchorMax = new Vector2(1f, 0f);
            portraitOverlay.rectTransform.pivot = Vector2.zero;
            portraitOverlay.rectTransform.anchoredPosition = new Vector2(0f, portraitSize / 20f);
            portraitOverlay.rectTransform.sizeDelta = new Vector2(0f, portraitSize);
            selectionOverlay.rectTransform.anchorMin = Vector2.zero;
            selectionOverlay.rectTransform.anchorMax = new Vector2(1f, 0f);
            selectionOverlay.rectTransform.pivot = Vector2.zero;
            selectionOverlay.rectTransform.sizeDelta = new Vector2(0f, portraitSize * 21f / 20f);
            selectionOverlay.gameObject.SetActive(slot.Selected);
            eliteDecoration.rectTransform.anchorMin = Vector2.zero;
            eliteDecoration.rectTransform.anchorMax = new Vector2(1f, 0f);
            eliteDecoration.rectTransform.pivot = Vector2.zero;
            eliteDecoration.rectTransform.sizeDelta = new Vector2(0f, portraitSize / 20f);
            eliteHighlight.rectTransform.anchorMin = Vector2.zero;
            eliteHighlight.rectTransform.anchorMax = new Vector2(1f, 0f);
            eliteHighlight.rectTransform.pivot = Vector2.zero;
            eliteHighlight.rectTransform.sizeDelta = new Vector2(0f, portraitSize * 8f / 45f);
            header.anchorMin = new Vector2(0.5f, 0f);
            header.anchorMax = new Vector2(0.5f, 0f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = new Vector2(0f, portraitSize / 20f + portraitSize);
            header.sizeDelta = new Vector2(portraitSize / 2f, portraitSize * 47f / 224f);
            eliteIconRoot.anchorMin = new Vector2(0f, 0f);
            eliteIconRoot.anchorMax = new Vector2(0f, 0f);
            eliteIconRoot.pivot = Vector2.zero;
            eliteIconRoot.anchoredPosition = new Vector2(portraitSize / 30f, portraitSize / 30f);
            countText.rectTransform.anchorMin = new Vector2(1f, 0f);
            countText.rectTransform.anchorMax = new Vector2(1f, 0f);
            countText.rectTransform.pivot = new Vector2(1f, 0f);
            countText.rectTransform.anchoredPosition = new Vector2(-6f, portraitSize / 20f + 4f);
            countText.rectTransform.sizeDelta = new Vector2(74f, 38f);
        }

        private void BuildVisuals(IReadOnlyDictionary<string, Sprite> sprites)
        {
            background = StagingHudController.CreateImage("Background", transform, Sprite(sprites, "StagingSlotBackground"));
            background.preserveAspect = false;
            var clipObject = StagingHudController.CreateRect("PortraitClip", transform);
            clipObject.gameObject.AddComponent<RectMask2D>();
            portraitClip = clipObject;
            portrait = StagingHudController.CreateImage("Portrait", portraitClip, null);
            portraitOverlay = StagingHudController.CreateImage("PortraitOverlay", transform, Sprite(sprites, "StagingSlotPortraitOverlay"));
            portraitOverlay.preserveAspect = false;
            eliteDecoration = StagingHudController.CreateImage("Elite1Decoration", transform, Sprite(sprites, "StagingSlotElite1Decoration"));
            eliteDecoration.preserveAspect = false;
            eliteHighlight = StagingHudController.CreateImage("Elite2PlusHighlight", transform, Sprite(sprites, "StagingSlotElite2PlusHighlight"));
            eliteHighlight.preserveAspect = false;
            header = StagingHudController.CreateRect("Header", transform);
            var leftHeader = StagingHudController.CreateImage("HeaderLeft", header, Sprite(sprites, "StagingSlotHeaderHalfBackground"));
            leftHeader.rectTransform.anchorMin = new Vector2(0f, 0f);
            leftHeader.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            leftHeader.rectTransform.offsetMin = Vector2.zero;
            leftHeader.rectTransform.offsetMax = Vector2.zero;
            leftHeader.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            var rightHeader = StagingHudController.CreateImage("HeaderRight", header, Sprite(sprites, "StagingSlotHeaderHalfBackground"));
            rightHeader.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rightHeader.rectTransform.anchorMax = Vector2.one;
            rightHeader.rectTransform.offsetMin = Vector2.zero;
            rightHeader.rectTransform.offsetMax = Vector2.zero;
            var costIcon = StagingHudController.CreateImage("CostIcon", header, Sprite(sprites, "StagingSlotCostIcon"));
            costIcon.rectTransform.anchorMin = new Vector2(0.75f, 1f);
            costIcon.rectTransform.anchorMax = new Vector2(0.75f, 1f);
            costIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            costIcon.rectTransform.anchoredPosition = Vector2.zero;
            costIcon.rectTransform.sizeDelta = new Vector2(20f, 20f);
            var costText = StagingHudController.CreateNumberText("Cost", header, 24, TextAnchor.MiddleCenter, Color.white);
            costText.rectTransform.anchorMin = new Vector2(0.75f, 0.5f);
            costText.rectTransform.anchorMax = new Vector2(0.75f, 0.5f);
            costText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            costText.rectTransform.anchoredPosition = Vector2.zero;
            costText.rectTransform.sizeDelta = new Vector2(46f, 32f);
            var marker = costText.gameObject.AddComponent<StagingSlotCostTextMarker>();
            marker.Text = costText;
            eliteIconRoot = StagingHudController.CreateRect("EliteIcon", transform);
            for (var level = 0; level <= 3; level++)
            {
                var icon = StagingHudController.CreateImage("Elite" + level, eliteIconRoot, Sprite(sprites, "StagingSlotElite" + level + "Icon"));
                icon.rectTransform.anchorMin = Vector2.zero;
                icon.rectTransform.anchorMax = Vector2.zero;
                icon.rectTransform.pivot = Vector2.zero;
                icon.rectTransform.anchoredPosition = Vector2.zero;
                var native = icon.sprite == null ? new Vector2(40f, 32f) : new Vector2(icon.sprite.rect.width, icon.sprite.rect.height);
                icon.rectTransform.sizeDelta = native;
                eliteIcons.Add(level, icon);
            }
            selectionOverlay = StagingHudController.CreateImage("SelectionOverlay", transform, Sprite(sprites, "StagingSlotSelectionOverlay"));
            selectionOverlay.preserveAspect = false;
            countText = StagingHudController.CreateNumberText("StackCount", transform, 30, TextAnchor.LowerRight, Color.white);
        }

        private void Click() => onClicked?.Invoke(SlotId);

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            onDragStarted?.Invoke(SlotId);
        }

        // The state-driven controller owns the preview position and commit. This marker exists solely so
        // EventSystem recognises the slot as a drag source and invokes OnBeginDrag exactly once.
        public void OnDrag(PointerEventData eventData) { }

        private static Sprite Sprite(IReadOnlyDictionary<string, Sprite> sprites, string name)
        {
            if (sprites.TryGetValue(name, out var sprite)) return sprite;
            Debug.LogError("[StagingHud][sprite.missing] " + name);
            return null;
        }
    }

    internal sealed class StagingSlotCostTextMarker : MonoBehaviour
    {
        public Text Text { get; set; }
    }
}
