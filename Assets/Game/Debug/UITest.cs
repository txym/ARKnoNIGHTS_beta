using UnityEngine;
using UnityEngine.UI;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class UITest : MonoBehaviour
{
    [Header("UI 预制体路径")]
    public string prefabPath = "Assets/Resources/Prefabs/DefaultUnitSelectUI.prefab";

    [Header("精灵路径（Assets 开头）")]
    public string spritePath = "Assets/GameData/UIIconImage";

    private const string JsonRootRel = "GameData/Units/Json"; // 位于 Assets 下

    void Start()
    {
       
    }

    #region 1. 加载并实例化 UI 预制体到 Canvas
    public GameObject SpawnALLUIPrefab(string path)
    {
#if UNITY_EDITOR
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
        string resourcesPath = path.Replace("Assets/Resources/", "").Replace(".prefab", "");
        GameObject prefab = Resources.Load<GameObject>(resourcesPath);
#endif
        if (prefab == null)
        {
            Debug.LogError($"未找到预制体: {path}");
            return null;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        GameObject instance = Instantiate(prefab, canvas.transform, false);
        instance.name = prefab.name;
        return instance;
    }
    #endregion

    #region 2. 把路径转成 Sprite 并赋给实例里的 Image
    public void ReplaceImageSprite(GameObject uiInstance, string spritePath)
    {
        Image img = uiInstance.GetComponentInChildren<Image>(true);
        if (img == null)
        {
            Debug.LogError("实例里找不到 Image 组件！");
            return;
        }

#if UNITY_EDITOR
        // 编辑器模式：直接加载 .png 里的 Sprite 子资源
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(spritePath).OfType<Sprite>().ToArray();
        Sprite targetSprite = sprites.Length > 0 ? sprites[0] : null;
#else
        // 打包后模式：需要把图片放在 Resources 并改成 Sprite 导入设置
        string resourcesPath = spritePath.Replace("Assets/Resources/", "").Replace(".png", "");
        targetSprite = Resources.Load<Sprite>(resourcesPath);
#endif
        if (targetSprite == null)
        {
            Debug.LogError($"未找到 Sprite: {spritePath}");
            return;
        }

        img.sprite = targetSprite;
        //Simg.SetNativeSize();          // 按原图尺寸
        Debug.Log($"成功替换精灵: {targetSprite.name}");
    }
    #endregion
}