using UnityEngine;
using UnityEngine.UI;

/// <summary>Small self-contained uGUI panel created below BattleDemoRoot. It never searches the scene by name.</summary>
[DisallowMultipleComponent]
public sealed class BattleDemoUi : MonoBehaviour
{
    [SerializeField] private BattleDemoController controller;
    [SerializeField] private Font font;

    private Text summary;
    private Text details;
    private Text speedLabel;
    private bool initialized;

    public void Bind(BattleDemoController value)
    {
        controller = value;
        EnsureUi();
        Refresh();
    }

    public void Refresh()
    {
        if (!initialized || controller == null || controller.Coordinator == null) return;
        var demo = controller.Coordinator;
        summary.text = "Fixed Battle Demo\nState: " + demo.State + " | View: " + demo.Observer + " | Speed: " + demo.Speed + "x\n" +
                       "Battle: " + (demo.Input == null ? "<not loaded>" : demo.Input.BattleId) + " | Winner/Reason: " + (string.IsNullOrEmpty(demo.WinnerOrReason) ? "<pending>" : demo.WinnerOrReason);
        details.text = "Schemas: battle=" + (demo.Input == null ? "local-battle-v1" : demo.Input.SchemaVersion) + "; catalog=" + (demo.Catalog == null ? "unit-catalog-v1" : demo.Catalog.SchemaVersion) + "\n" +
                       "Players: " + (string.IsNullOrEmpty(demo.PlayersSummary) ? "<pending>" : demo.PlayersSummary) + "\n" +
                       "Input: " + (string.IsNullOrEmpty(demo.InputDigest) ? "<pending>" : demo.InputDigest) + " | Events: " + demo.ConsumedEventCount + "/" + demo.EventCount + " | Tick: " + demo.PresentationTick.ToString("F1") + "\n" +
                       "Events: " + (string.IsNullOrEmpty(demo.EventDigest) ? "<pending>" : demo.EventDigest) + " | Result: " + (string.IsNullOrEmpty(demo.ResultDigest) ? "<pending>" : demo.ResultDigest) + "\n" +
                       "Last error: " + (string.IsNullOrEmpty(demo.LastError) ? "<none>" : demo.LastError);
        speedLabel.text = "Speed " + demo.Speed + "x";
    }

    private void Awake() => EnsureUi();

    private void EnsureUi()
    {
        if (initialized) return;
        initialized = true;
        if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var panel = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        panel.color = new Color(0.04f, 0.06f, 0.10f, 0.88f);
        var rect = transform as RectTransform;
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-18f, -18f);
        rect.sizeDelta = new Vector2(620f, 300f);

        summary = CreateText("Summary", new Vector2(12f, -10f), new Vector2(596f, 58f), 16, TextAnchor.UpperLeft);
        details = CreateText("Details", new Vector2(12f, -72f), new Vector2(596f, 132f), 13, TextAnchor.UpperLeft);
        CreateButton("StartContinue", "Start / Continue", new Vector2(12f, 12f), () => controller?.StartOrContinue());
        CreateButton("Pause", "Pause", new Vector2(142f, 12f), () => controller?.Pause());
        CreateButton("Replay", "Replay", new Vector2(232f, 12f), () => controller?.Replay());
        CreateButton("Recalculate", "Recalculate", new Vector2(322f, 12f), () => controller?.Recalculate());
        CreateButton("Home", "Home", new Vector2(452f, 12f), () => controller?.SetHomeView());
        CreateButton("Away", "Away", new Vector2(522f, 12f), () => controller?.SetAwayView());
        speedLabel = CreateButton("Speed", "Speed 1x", new Vector2(12f, 54f), () => controller?.CycleSpeed()).GetComponentInChildren<Text>();
    }

    private Text CreateText(string name, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor alignment)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        child.transform.SetParent(transform, false);
        var rect = child.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        var text = child.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = Color.white;
        return text;
    }

    private Button CreateButton(string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        child.transform.SetParent(transform, false);
        var rect = child.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(name == "Recalculate" ? 122f : 80f, 34f);
        var image = child.GetComponent<Image>();
        image.color = new Color(0.2f, 0.32f, 0.48f, 1f);
        var button = child.GetComponent<Button>();
        button.onClick.AddListener(action);
        var text = CreateTextUnder(child.transform, "Label", label);
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private Text CreateTextUnder(Transform parent, string name, string value)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        child.transform.SetParent(parent, false);
        var rect = child.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = child.GetComponent<Text>();
        text.font = font;
        text.fontSize = 13;
        text.color = Color.white;
        text.text = value;
        return text;
    }
}
