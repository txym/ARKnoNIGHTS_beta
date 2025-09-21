#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Bitsets;

static class BakePaths
{
    public const string UnitsJsonDir = "Assets/GameData/Units/Json";
    public const string InnateRegistryPath = "Assets/GameData/Units/Unit_Innate_Ability_Database.asset";
    public static string UnitAssetPathById(int id) => $"Assets/Game/Data/Units/Unit_{id}.asset"; // 如无 Template，可忽略
}

[System.Serializable]
class UnitJsonLite
{
    public int id;
    public List<string> FixedAbility;
}

public static class UnitJsonBake
{
    [MenuItem("Tools/Ability/Scan JSON & Bake Innate Mask")]
    public static void ScanAndBake()
    {
        var reg = AssetDatabase.LoadAssetAtPath<TagRegistry>(BakePaths.InnateRegistryPath);
        if (reg == null)
        {
            Debug.LogError($"未找到 Innate Registry: {BakePaths.InnateRegistryPath}");
            return;
        }

        var jsonPaths = Directory.GetFiles(BakePaths.UnitsJsonDir, "*.json", SearchOption.AllDirectories);
        var units = new List<(string path, UnitJsonLite data)>(jsonPaths.Length);

        // 1) 读取 JSON，汇总 tag 并只追加到 Registry
        foreach (var p in jsonPaths)
        {
            var text = File.ReadAllText(p);
            var u = JsonUtility.FromJson<UnitJsonLite>(text);
            if (u == null) continue;
            units.Add((p, u));

            if (u.FixedAbility == null) continue;
            foreach (var tag in u.FixedAbility)
                if (!string.IsNullOrWhiteSpace(tag)) reg.TryGetOrAdd(tag.Trim());
        }

        EditorUtility.SetDirty(reg);
        AssetDatabase.SaveAssets();

        // 2) 回写到 UnitTemplate（若存在）
        int baked = 0;
        foreach (var (_, u) in units)
        {
            var assetPath = BakePaths.UnitAssetPathById(u.id);
            var ut = AssetDatabase.LoadAssetAtPath<UnitTemplate>(assetPath);
            if (ut == null) continue;

            ut.RebuildInnateMask(reg);
            baked++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Bake] 扫描 {units.Count} 个 JSON；注册表大小={reg.Count}；回写 {baked} 个 UnitTemplate。");
    }
}
#endif
