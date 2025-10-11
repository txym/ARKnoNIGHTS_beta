using UnityEditor;
using UnityEngine;
using System.IO;

public class MergeTextureTool : EditorWindow
{
    // ���ڴ���������ɫͼ�� Alpha ͼ
    private Texture2D colorTexture;
    private Texture2D alphaTexture;
    private string savePath = "Assets/Resources/UI/MergedTexture.png";  // Ĭ�ϱ���·��

    // ��ʾ����
    [MenuItem("Tools/Merge RGBA Textures")]
    public static void ShowWindow()
    {
        GetWindow<MergeTextureTool>("Merge RGBA Textures");
    }

    private void OnGUI()
    {
        GUILayout.Label("Merge Color and Alpha Textures", EditorStyles.boldLabel);

        // ѡ����ɫͼ�� Alpha ͼ
        colorTexture = (Texture2D)EditorGUILayout.ObjectField("Color Texture", colorTexture, typeof(Texture2D), false);
        alphaTexture = (Texture2D)EditorGUILayout.ObjectField("Alpha Texture", alphaTexture, typeof(Texture2D), false);

        // ����·��
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

        // ����һ���µ���������������ϲ�
        Texture2D mergedTexture = new Texture2D(colorTexture.width, colorTexture.height, TextureFormat.RGBA32, false);

        // ��ȡ��ɫͼ�� Alpha ͼ����������
        Color[] colorPixels = colorTexture.GetPixels();
        Color[] alphaPixels = alphaTexture.GetPixels();

        // �ϲ���ɫ��͸���ȵ��µ�����
        for (int i = 0; i < colorPixels.Length; i++)
        {
            Color color = colorPixels[i];
            float alpha = alphaPixels[i].r; // ʹ�� alpha ͼ�� R ͨ����Ϊ͸����
            color.a = alpha;
            colorPixels[i] = color;
        }

        // ���ϲ���������������õ�������
        mergedTexture.SetPixels(colorPixels);
        mergedTexture.Apply();

        // ����Ϊ PNG �ļ�
        byte[] bytes = mergedTexture.EncodeToPNG();
        File.WriteAllBytes(savePath, bytes);

        // ˢ����Դ��ȷ�����ļ����� Unity �༭������ʾ
        AssetDatabase.ImportAsset(savePath);
        Debug.Log("Merged texture saved to " + savePath);
    }
}
