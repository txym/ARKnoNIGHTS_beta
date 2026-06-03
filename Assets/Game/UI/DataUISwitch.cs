// DataUISwitch.cs —— 单例：不挂场景。按路径实例化一次；之后只负责显示/隐藏与数据填充。
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DataUISwitch
{
    private static DataUISwitch _instance;
    public static DataUISwitch Instance => _instance ??= new DataUISwitch();

    private GameObject _prefab;
    private string _loadedPath = "UIMaterial/UIPrefabs/UnitDescriptionPrefab";
    private GameObject _instanceGO;

    public bool HasInstance => _instanceGO != null;
    public bool Visible => HasInstance && _instanceGO.activeSelf;
    public GameObject InstanceGO => _instanceGO;

    // ===== 初始化：从 Resources 路径实例化一次 =====
    public void InitializeFromPath(string resourcesPath, bool setActive = false)
    {
        if (_instanceGO != null) return;

        if (string.IsNullOrEmpty(resourcesPath))
        {
            Debug.LogError("[DataUISwitch] 初始化失败：resourcesPath 为空");
            return;
        }

        if (_prefab == null || _loadedPath != resourcesPath)
        {
            _prefab = Resources.Load<GameObject>(resourcesPath);
            _loadedPath = resourcesPath;
        }
        if (_prefab == null)
        {
            Debug.LogError($"[DataUISwitch] 预制体加载失败：{resourcesPath}（需放在 Resources/ 下）");
            return;
        }

        var canvas = FindCanvas() ?? CreateDefaultCanvas();
        _instanceGO = Object.Instantiate(_prefab, canvas.transform);

        // 可按需微调锚点摆放
        var rt = _instanceGO.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.6f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        _instanceGO.SetActive(setActive);

        // 订阅 UIManager 的数据切换事件
        UIManager.OnApplyData += ApplyPayload;

        Debug.Log($"[DataUISwitch] 初始化完成（{resourcesPath}），初始激活：{setActive}");
    }

    // ===== 显示/隐藏/切换 =====
    public void Show()
    {
        if (_instanceGO == null) return;
        if (!_instanceGO.activeSelf) _instanceGO.SetActive(true);
    }

    public void Hide()
    {
        if (_instanceGO == null) return;
        if (_instanceGO.activeSelf) _instanceGO.SetActive(false);
    }

    public void Toggle()
    {
        if (_instanceGO == null) return;
        _instanceGO.SetActive(!_instanceGO.activeSelf);
    }

    // ===== 把 payload 应用到 UI（按你项目随时扩展） =====
    public void ApplyPayload(object payload)
    {
        if (_instanceGO == null) return;

        // 示例1：payload 是 int（按 id 展示/查询）
        if (payload is int id)
        {
            // 这里按你的项目需求自定义：比如根据 id 查询数据
            // 下面给出一个基础演示：把 id 写到名为 "UnitName" 的 Text/TMP 上
            var nameGO = FindDeepGO(_instanceGO.transform, "UnitName");
            var tpl = UnitFactory.GetUnitBasicValueSO(id);
            SetContent(nameGO, tpl.uintName);
            return;
        }

        // 示例2：payload 是 string（直接显示 key 或用它去查表）
        if (payload is string key)
        {
            var nameGO = FindDeepGO(_instanceGO.transform, "UnitName");
            SetContent(nameGO, key);
            return;
        }

        // 可扩：ScriptableObject、自定义数据结构等
        Debug.Log($"[DataUISwitch] 未处理的 payload 类型：{payload?.GetType().Name ?? "null"}");
    }

    // ===== 工具：寻找 Canvas / 创建默认 Canvas =====
    private Canvas FindCanvas()
    {
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c.isRootCanvas) return c;
        }
        return canvases.Length > 0 ? canvases[0] : null;
    }

    private Canvas CreateDefaultCanvas()
    {
        var go = new GameObject("Canvas (Auto)");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
        Object.DontDestroyOnLoad(go);
        return canvas;
    }

    // ===== 工具：深度查找子物体 =====
    private GameObject FindDeepGO(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root.gameObject;

        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeepGO(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    // ===== 工具：给常见 UI 组件赋值 =====
    private void SetContent(GameObject go, object value)
    {
        if (go == null) return;

        switch (value)
        {
            case null:
                // 清空文本
                if (go.TryGetComponent(out TMP_Text tmp)) tmp.text = "";
                else if (go.TryGetComponent(out Text ugui)) ugui.text = "";
                break;

            case string s:
                if (go.TryGetComponent(out TMP_Text tmpText)) tmpText.text = s;
                else if (go.TryGetComponent(out Text uguiText)) uguiText.text = s;
                break;

            case Sprite sp:
                if (go.TryGetComponent(out Image img))
                {
                    img.sprite = sp;
                    img.enabled = sp != null;
                    img.preserveAspect = true;
                    if (sp != null) img.SetNativeSize();
                }
                break;

            case Texture tex:
                if (go.TryGetComponent(out RawImage raw))
                {
                    raw.texture = tex;
                    raw.enabled = tex != null;
                    if (tex != null) raw.SetNativeSize();
                    raw.rectTransform.ForceUpdateRectTransforms();
                }
                break;

            default:
                // 其它类型，按需再扩展
                if (go.TryGetComponent(out TMP_Text tmpAny)) tmpAny.text = value.ToString();
                else if (go.TryGetComponent(out Text uguiAny)) uguiAny.text = value.ToString();
                break;
        }
    }
}
