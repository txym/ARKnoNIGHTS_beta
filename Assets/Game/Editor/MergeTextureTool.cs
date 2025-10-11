using UnityEditor;
using UnityEngine;
using System.IO;

public class MergeTextureTool : EditorWindow
{
    // 用于存放输入的颜色图和 Alpha 图
    private Texture2D colorTexture;
    private Texture2D alphaTexture;
    private string savePath = "Assets/Resources/UI/MergedTexture.png";  // 默认保存路径

    // 显示窗口
    [MenuItem("Tools/Merge RGBA Textures")]
    public static void ShowWindow()
    {
        GetWindow<MergeTextureTool>("Merge RGBA Textures");
    }

    private void OnGUI()
    {
        GUILayout.Label("Merge Color and Alpha Textures", EditorStyles.boldLabel);

        // 选择颜色图和 Alpha 图
        colorTexture = (Texture2D)EditorGUILayout.ObjectField("Color Texture", colorTexture, typeof(Texture2D), false);
        alphaTexture = (Texture2D)EditorGUILayout.ObjectField("Alpha Texture", alphaTexture, typeof(Texture2D), false);

        // 保存路径
        savePath = EditorGUILayout.TextField("Save Path", savePath);

        if (colorTexture != null && alphaTexture != null)
        {
            if (GUILayout.Button("Merge and Save"))
            {
                MergeAndSaveTextures();
            }
        }
    }

    private void MergeAndSaveTextures()
    {
        if (colorTexture.width != alphaTexture.width || colorTexture.height != alphaTexture.height)
        {
            Debug.LogError("Color and Alpha textures must have the same dimensions.");
            return;
        }

        // 创建一个新的纹理，将两个纹理合并
        Texture2D mergedTexture = new Texture2D(colorTexture.width, colorTexture.height, TextureFormat.RGBA32, false);

        // 获取颜色图和 Alpha 图的像素数据
        Color[] colorPixels = colorTexture.GetPixels();
        Color[] alphaPixels = alphaTexture.GetPixels();

        // 合并颜色和透明度到新的纹理
        for (int i = 0; i < colorPixels.Length; i++)
        {
            Color color = colorPixels[i];
            float alpha = alphaPixels[i].r; // 使用 alpha 图的 R 通道作为透明度
            color.a = alpha;
            colorPixels[i] = color;
        }

        // 将合并后的像素数据设置到新纹理
        mergedTexture.SetPixels(colorPixels);
        mergedTexture.Apply();

        // 保存为 PNG 文件
        byte[] bytes = mergedTexture.EncodeToPNG();
        File.WriteAllBytes(savePath, bytes);

        // 刷新资源，确保新文件能在 Unity 编辑器中显示
        AssetDatabase.ImportAsset(savePath);
        Debug.Log("Merged texture saved to " + savePath);
    }
}
