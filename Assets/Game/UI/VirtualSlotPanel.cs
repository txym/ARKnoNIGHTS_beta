using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class VirtualSlotPanel : MonoBehaviour
{
    [Header("Panel引用")]
    public RectTransform panel;

    [Header("预制体引用")]
    public GameObject itemPrefab;

    [Header("虚拟子块设置")]
    [Range(1, 24)] public int slotCount = 12;
    public bool debugMode = true;
    public float spacing = 0f;  // 间距
    public float padding = 0f;  // 填充

    private List<SlotInfo> virtualSlots = new List<SlotInfo>();
    private float slotSize;

    [System.Serializable]
    public class SlotInfo
    {
        public int index;
        public Vector2 anchoredPosition;
        public bool isOccupied;
        public GameObject occupiedObject;
    }

    void Start()
    {
        InitializePanel();
        CalculateVirtualSlots();
    }

    void OnEnable()
    {
        InitializePanel();
        CalculateVirtualSlots();
    }

    void Update()
    {
        if (Application.isPlaying && Screen.width != slotSize * 12f)
        {
            CalculateVirtualSlots();
        }
    }

    void OnRectTransformDimensionsChange()
    {
        CalculateVirtualSlots();
    }

    /// <summary>
    /// 初始化Panel设置 - 修正为左下角
    /// </summary>
    void InitializePanel()
    {
        if (!panel) return;
        panel.anchorMin = new Vector2(0, 0);
        panel.anchorMax = new Vector2(0, 0);
        panel.pivot = new Vector2(0, 0);
        panel.anchoredPosition = Vector2.zero;

        if (debugMode) Debug.Log("Panel初始化完成 - 左下角锚点");
    }

    /// <summary>
    /// 计算虚拟子块布局
    /// </summary>
    public void CalculateVirtualSlots()
    {
        if (!panel) return;

        virtualSlots.Clear();

        // 计算每个子块的宽度（屏幕宽度的1/12）
        slotSize = (Screen.width - padding * 2 - spacing * (slotCount - 1)) / slotCount;

        if (debugMode)
        {
            Debug.Log($"屏幕宽度: {Screen.width}px, 子块大小: {slotSize}px");
        }

        // 调整Panel大小
        AdjustPanelSize();

        // 创建虚拟子块
        for (int i = 0; i < slotCount; i++)
        {
            CreateVirtualSlot(i);
        }

        // 更新已存在的物体
        UpdateAllOccupiedObjects();

        if (debugMode) Debug.Log($"虚拟布局计算完成，创建了 {slotCount} 个子块");
    }

    /// <summary>
    /// 调整Panel大小以容纳所有子块
    /// </summary>
    void AdjustPanelSize()
    {
        if (!panel) return;

        // 计算Panel的总宽度
        float totalWidth = slotSize * slotCount + spacing * (slotCount - 1) + padding * 2;
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, totalWidth);

        // 设置Panel的高度，保持槽位的高度
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, slotSize + padding * 2);

        if (debugMode) Debug.Log($"Panel大小调整为: {totalWidth}x{slotSize + padding * 2}px");
    }

    /// <summary>
    /// 创建虚拟子块
    /// </summary>
    void CreateVirtualSlot(int index)
    {
        var slotInfo = new SlotInfo();
        slotInfo.index = index;

        // 计算每个子块的位置
        float x = padding + index * (slotSize + spacing) + slotSize / 2f;
        slotInfo.anchoredPosition = new Vector2(x, slotSize / 2f);
        slotInfo.isOccupied = false;
        slotInfo.occupiedObject = null;

        virtualSlots.Add(slotInfo);

        if (debugMode && index < 3)
            Debug.Log($"插槽 {index} 位置: {slotInfo.anchoredPosition}, 大小: {slotSize}px");
    }

    /// <summary>
    /// 在指定插槽放置预制体
    /// </summary>
    public GameObject PlaceObjectInSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= virtualSlots.Count || !itemPrefab)
        {
            Debug.LogWarning($"无效的插槽索引或预制体: {slotIndex}");
            return null;
        }

        var slotInfo = virtualSlots[slotIndex];

        if (slotInfo.isOccupied)
        {
            Debug.LogWarning($"插槽 {slotIndex} 已被占用");
            return null;
        }

        // 实例化预制体
        GameObject newInstance = Instantiate(itemPrefab, panel);
        newInstance.name = $"{itemPrefab.name}_{slotIndex:00}";

        // 定位并调整大小
        PositionAndResizeObject(newInstance, slotInfo);

        // 更新插槽状态
        slotInfo.isOccupied = true;
        slotInfo.occupiedObject = newInstance;

        return newInstance;
    }

    /// <summary>
    /// 定位并调整物体大小
    /// </summary>
    void PositionAndResizeObject(GameObject obj, SlotInfo slot)
    {
        RectTransform rectTransform = obj.GetComponent<RectTransform>();
        if (!rectTransform)
        {
            rectTransform = obj.AddComponent<RectTransform>();
        }

        // 使用拉伸锚点确保完全填满子块空间
        rectTransform.anchorMin =Vector2.zero;  // 左下角锚点
        rectTransform.anchorMax = Vector2.one;   // 右上角锚点
        rectTransform.pivot = new Vector2(0.5f, 0.5f);  // 物体的旋转中心设为中点

        // 计算每个物体的位置和大小，确保占据正确的空间
        rectTransform.offsetMin = new Vector2(slot.anchoredPosition.x - slotSize / 2f, 0);  // 设置左下角的偏移
        rectTransform.offsetMax = new Vector2(slot.anchoredPosition.x + slotSize / 2f, slotSize);  // 设置右上角的偏移

        // 激活物体
        obj.SetActive(true);

        // 如果你发现物体的宽度不对，可以使用 sizeDelta 来直接控制尺寸
        rectTransform.sizeDelta = new Vector2(slotSize, slotSize);  // 设置物体宽高为 160px
    }

    /// <summary>
    /// 更新所有已放置物体的位置和大小
    /// </summary>
    void UpdateAllOccupiedObjects()
    {
        foreach (var slot in virtualSlots)
        {
            if (slot.isOccupied && slot.occupiedObject)
            {
                PositionAndResizeObject(slot.occupiedObject, slot);
            }
        }
    }
}
