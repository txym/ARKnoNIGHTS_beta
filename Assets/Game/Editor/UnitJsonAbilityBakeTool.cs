#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Bitsets;

static class BakePaths
{
    public const string UnitsJsonDir =
        "Assets/GameData/Units/EliteVariants/Json";
    public const string InnateRegistryPath = "Assets/GameData/Units/Unit_Innate_Ability_Database.asset";
    public const string UnitsIconImagePath = "Assets/GameData/UIIconImage";
    public static string UnitAssetPathById(int id) => $"Assets/Game/Data/Units/Unit_{id}.asset"; // 如无 Template，可忽略
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

        // 1) 读取 JSON，汇总 tag 并只追加到 Registry
        var declaredAbilityIds = CollectDeclaredAbilityIds(BakePaths.UnitsJsonDir);
        foreach (var abilityId in declaredAbilityIds)
        {
            reg.TryGetOrAdd(abilityId);
        }

        EditorUtility.SetDirty(reg);
        AssetDatabase.SaveAssets();

        // 2) 回写到 UnitTemplate（若存在）
        var unitTypeIds = UnitEliteVariantResolver
            .LoadDirectory(BakePaths.UnitsJsonDir)
            .Keys
            .OrderBy(id => id)
            .ToArray();
        int baked = 0;
        foreach (var typeId in unitTypeIds)
        {
            var assetPath = BakePaths.UnitAssetPathById(typeId);
            var ut = AssetDatabase.LoadAssetAtPath<UnitTemplate>(assetPath);
            if (ut == null) continue;

            ut.RebuildInnateMask(reg);
            baked++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Bake] 扫描 {unitTypeIds.Length} 个 JSON；注册表大小={reg.Count}；回写 {baked} 个 UnitTemplate。");
    }

    private static IReadOnlyList<string> CollectDeclaredAbilityIds(
        string unitSourceDirectory)
    {
        return UnitEliteVariantResolver
            .LoadDirectory(unitSourceDirectory)
            .Values
            .SelectMany(UnitEliteVariantResolver.GetDeclaredInnateAbilityIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
#endif
