using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ShopSlotPanel : MonoBehaviour
{
    public enum ScaleMode { Fit, Fill } // Fit:完整显示(可能留边)；Fill:铺满(可能超出被裁)

    [Header("等分数量")]
    [Min(1)] public int slotCount = 5;

    [Header("拖拽 Slot 预制体")]
    public GameObject slotPrefab;

    [Header("生成前清空旧子物体")]
    public bool clearBeforeBuild = true;

    [Header("启动时自动生成")]
    public bool buildOnStart = true;

    [Header("Panel尺寸变化时自动重算")]
    public bool reapplyOnResize = true;

    [Header("等比缩放设置（针对预制体内部所有子物体）")]
    public ScaleMode scaleMode = ScaleMode.Fit;

    [Tooltip("预制体在『作者态』的参考尺寸(宽,高)。留空则自动从 prefab 的 RectTransform 读取。")]
    public Vector2 referenceSize = Vector2.zero;

    RectTransform PanelRT => (RectTransform)transform;

    void Start()
    {
        if (buildOnStart) Build();
        else { ApplyAnchorsToChildren(); RescaleAllSlots(); }
    }

    void OnRectTransformDimensionsChange()
    {
        if (reapplyOnResize) { ApplyAnchorsToChildren(); RescaleAllSlots(); }
    }

    [ContextMenu("Build / Rebuild")]
    public void Build()
    {
        if (!PanelRT)
        {
            Debug.LogError("[ShopSlotPanel] 请把脚本挂在一个 UI Panel(RectTransform) 上。");
            return;
        }
        if (!slotPrefab)
        {
            Debug.LogError("[ShopSlotPanel] 请把 Slot 预制体拖到 slotPrefab。");
            return;
        }

        // 参考尺寸：优先用手填，其次从 prefab 读
        Vector2 refSize = referenceSize;
        var prefabRT = slotPrefab.GetComponent<RectTransform>();
        if ((refSize.x <= 0 || refSize.y <= 0) && prefabRT)
        {
            refSize = prefabRT.rect.size;
            if (refSize.x <= 0 || refSize.y <= 0) refSize = prefabRT.sizeDelta;
            if (refSize.x <= 0 || refSize.y <= 0) refSize = new Vector2(100, 100);
        }
        referenceSize = refSize;

        // 清空
        if (clearBeforeBuild)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                for (int i = PanelRT.childCount - 1; i >= 0; i--) DestroyImmediate(PanelRT.GetChild(i).gameObject);
            }
            else
#endif
            {
                for (int i = PanelRT.childCount - 1; i >= 0; i--) Destroy(PanelRT.GetChild(i).gameObject);
            }
        }

        // 生成
        for (int i = 0; i < slotCount; i++)
        {
            var go = Instantiate(slotPrefab, PanelRT);
            go.name = $"Slot_{i + 1}";
            var rt = go.transform as RectTransform;
            NormalizeRT(rt);

            // 把预制体原始子物体全部搬到 ContentScaler 下，统一等比缩放
            var scaler = EnsureContentScaler(rt, refSize);
            RescaleOne(rt, scaler, refSize);
        }

        // 等分 & 缩放
        ApplyAnchorsToChildren();
        RescaleAllSlots();
    }

    [ContextMenu("Apply Anchors To Children")]
    public void ApplyAnchorsToChildren()
    {
        int n = Mathf.Max(1, slotCount);
        int cc = PanelRT.childCount;
        for (int i = 0; i < cc; i++)
        {
            var rt = PanelRT.GetChild(i) as RectTransform;
            if (!rt) continue;

            float minX = (float)i / n;
            float maxX = (float)(i + 1) / n;

            rt.anchorMin = new Vector2(minX, 0f);
            rt.anchorMax = new Vector2(maxX, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            NormalizeRT(rt);

            var le = rt.GetComponent<LayoutElement>();
            if (le) le.ignoreLayout = true;
        }
    }

    // —— 等比缩放核心 —— //

    void RescaleAllSlots()
    {
        Vector2 refSize = (referenceSize.x > 0 && referenceSize.y > 0) ? referenceSize : new Vector2(100, 100);
        int cc = PanelRT.childCount;
        for (int i = 0; i < cc; i++)
        {
            var slot = PanelRT.GetChild(i) as RectTransform;
            if (!slot) continue;
            var scaler = slot.Find("__ContentScaler__") as RectTransform;
            if (!scaler) scaler = EnsureContentScaler(slot, refSize);
            RescaleOne(slot, scaler, refSize);
        }
    }

    RectTransform EnsureContentScaler(RectTransform slotRoot, Vector2 refSize)
    {
        if (!slotRoot) return null;

        // 查找或创建内容缩放容器
        var scaler = slotRoot.Find("__ContentScaler__") as RectTransform;
        if (!scaler)
        {
            var go = new GameObject("__ContentScaler__", typeof(RectTransform));
            scaler = go.GetComponent<RectTransform>();
            scaler.SetParent(slotRoot, false);

            // 将 slotRoot 原有所有子物体搬到 scaler 下（保证多个子物体都一起缩放）
            var tmp = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < slotRoot.childCount - 1; i++) // -1 因为刚加了scaler本身
                tmp.Add(slotRoot.GetChild(i));
            foreach (var t in tmp) t.SetParent(scaler, false);
        }

        // scaler 的自身尺寸设成“作者态参考尺寸”，再通过 localScale 统一放缩
        scaler.anchorMin = new Vector2(0.5f, 0.5f);
        scaler.anchorMax = new Vector2(0.5f, 0.5f);
        scaler.pivot = new Vector2(0.5f, 0.5f);
        scaler.anchoredPosition = Vector2.zero;
        scaler.sizeDelta = refSize; // 以参考尺寸作为基准盒

        return scaler;
    }

    void RescaleOne(RectTransform slotRoot, RectTransform scaler, Vector2 refSize)
    {
        if (!slotRoot || !scaler) return;

        // 当前 slot 的可用宽高（按锚点等分后）
        float w = Mathf.Max(1f, slotRoot.rect.width);
        float h = Mathf.Max(1f, slotRoot.rect.height);

        // 计算统一比例
        float sx = w / Mathf.Max(1f, refSize.x);
        float sy = h / Mathf.Max(1f, refSize.y);
        float s = (scaleMode == ScaleMode.Fit) ? Mathf.Min(sx, sy) : Mathf.Max(sx, sy);

        scaler.localScale = new Vector3(s, s, 1f);
        scaler.anchoredPosition = Vector2.zero; // 居中
    }

    // —— 工具 —— //

    void NormalizeRT(RectTransform rt)
    {
        if (!rt) return;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition3D = Vector3.zero;
    }
}
