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
    public static string UnitAssetPathById(int id) => $"Assets/Game/Data/Units/Unit_{id}.asset"; // ���� Template���ɺ���
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
            Debug.LogError($"δ�ҵ� Innate Registry: {BakePaths.InnateRegistryPath}");
            return;
        }

        var jsonPaths = Directory.GetFiles(BakePaths.UnitsJsonDir, "*.json", SearchOption.AllDirectories);
        var units = new List<(string path, UnitJsonLite data)>(jsonPaths.Length);

        // 1) ��ȡ JSON������ tag ��ֻ׷�ӵ� Registry
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

        // 2) ��д�� UnitTemplate�������ڣ�
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
        Debug.Log($"[Bake] ɨ�� {units.Count} �� JSON��ע����С={reg.Count}����д {baked} �� UnitTemplate��");
    }
}
#endif
