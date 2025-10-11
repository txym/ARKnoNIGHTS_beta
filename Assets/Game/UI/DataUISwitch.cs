// DataUISwitch.cs
// 单例：不挂场景。初始化时通过 Resources 路径实例化一次；之后点击只做开关。
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public sealed class DataUISwitch
{
    private static DataUISwitch _instance;
    public static DataUISwitch Instance => _instance ??= new DataUISwitch();

    private GameObject _prefab;
    private string _loadedPath= "Assets/Resources/Prefabs/DescriptionUI.prefab";
    private GameObject _instanceGO;

    private DataUISwitch() { }

    public bool HasInstance => _instanceGO != null;
    public bool Visible => _instanceGO != null && _instanceGO.activeSelf;
    public GameObject InstanceGO => _instanceGO;

    /// <summary>初始化：按 Resources 路径加载并实例化一次；之后再调无效果。</summary>
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
        var rt = _instanceGO.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.6f); // 屏幕左上 1/4 高度处
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(40f, 0f);

            rt.anchoredPosition = Vector2.zero;
            //rt.localScale = Vector3.one;
        }
        _instanceGO.SetActive(setActive);
    }

    public void Toggle(int id = 1000)
    {
        if (_instanceGO == null) { Debug.LogWarning("[DataUISwitch] 尚未初始化，忽略 Toggle"); return; }
        //_instanceGO.transform.position = gameObject.transform.position + new Vector3(0, 130, 0);
        var tpl = UnitFactory.GetUnitBasicValueSO(id);
        var descriptiontext = _instanceGO.GetComponentInChildren<Text>();
        descriptiontext.text =
        $"{tpl.uintName}\n" +
        $"生命：{tpl.HP}\n" +
        $"攻击：{tpl.atk}\n" +
        $"防御：{tpl.def}\n" +
        $"法抗：{tpl.res}\n" +
        $"攻击间隔：{tpl.attackInterval}s\n" +
        $"移动速度：{tpl.moveSpeed}";
        _instanceGO.SetActive(!_instanceGO.activeSelf);
    }

    public void Show()
    {
        if (_instanceGO == null) { Debug.LogWarning("[DataUISwitch] 尚未初始化，忽略 Show"); return; }
            _instanceGO.SetActive(true);
    }

    public void Hide()
    {
        if (_instanceGO == null) return;
        _instanceGO.SetActive(false);
    }

    public void DestroyInstance()
    {
        if (_instanceGO != null)
        {
            Object.Destroy(_instanceGO);
            _instanceGO = null;
        }
    }

    private static Canvas FindCanvas()
    {
#if UNITY_2021_3_OR_NEWER
        return Object.FindObjectOfType<Canvas>(true);
#else
        return Object.FindObjectOfType<Canvas>();
#endif
    }

    private static Canvas CreateDefaultCanvas()
    {
        var go = new GameObject("Canvas (Auto)", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        Object.DontDestroyOnLoad(go);
        return c;
    }
}
