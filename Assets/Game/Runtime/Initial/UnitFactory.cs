using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Spine.Unity;

public static class UnitFactory
{
    private const string JsonRootRel = "GameData/Units/Json"; // 位于 Assets 下
    private const string PrefabResPath = "Prefabs/DefaultUnit"; // Resources.Load 不要带 "Resources/"

    public static List<GameObject> SpawnAll(
        Transform parent,
        bool setInactive,
        out Dictionary<int, GameObject> idMap)
    {
        idMap = new Dictionary<int, GameObject>();
        var result = new List<GameObject>();

        string rootAbs = Path.Combine(Application.dataPath, JsonRootRel);
        if (!Directory.Exists(rootAbs))
        {
            Debug.LogError($"[UnitFactory] JSON 目录不存在: {rootAbs}");
            return result;
        }

        var prefab = Resources.Load<GameObject>(PrefabResPath);
        if (!prefab)
        {
            Debug.LogError($"[UnitFactory] 找不到预制体: Resources/{PrefabResPath}");
            return result;
        }

        var files = Directory.GetFiles(rootAbs, "*.json", SearchOption.AllDirectories);
        System.Array.Sort(files, System.StringComparer.Ordinal);

        foreach (var file in files)
        {
            UnitJson j = null;
            try
            {
                var json = File.ReadAllText(file);
                j = JsonUtility.FromJson<UnitJson>(json);
            }
            catch
            {
                Debug.LogError($"[UnitFactory] JSON 解析失败: {file}");
                continue;
            }
            if (j == null)
            {
                Debug.LogError($"[UnitFactory] JSON 为空: {file}");
                continue;
            }

            var tpl = BuildTemplate(j);

            var go = Object.Instantiate(prefab, parent);
            if (setInactive) go.SetActive(false);
            go.name = string.IsNullOrEmpty(tpl.uintName) ? $"Unit_{tpl.id}" : $"{tpl.uintName}_{tpl.id}";

            var refCmp = go.GetComponent<UnitTemplateReference>();
            if (refCmp == null) refCmp = go.AddComponent<UnitTemplateReference>();
            refCmp.SetTemplate(tpl);
            // ---- 不再调用 UnitView.ApplySkeleton；这里直接改 SkeletonAnimation ----


            var skel = go.GetComponent<SkeletonAnimation>();
            if (!skel)
            {
                Debug.LogError("[UnitFactory] DefaultUnit 上缺少 SkeletonAnimation 组件");
            }
            else
            {
                // 直接使用 JSON 里的资源名
                // 例如： "Characters/gopro/enemy_1000_gopro_3_SkeletonData"
                var resPath = BuildResPath(j.uintName, j.skeletonData);
                var sda = Resources.Load<SkeletonDataAsset>(resPath);
                if (!sda)
                {
                    Debug.LogError($"[UnitFactory] SkeletonDataAsset 未找到: Resources/{resPath}");
                }
                else
                {
                    skel.skeletonDataAsset = sda;
                    skel.Initialize(true);  // 关键：重建骨骼实例

                    // 如果 JSON 后续增加了这三项，就按有值才设置；没配就保留 Inspector 里的设置
                    // if (!string.IsNullOrEmpty(j.initialSkin)) {
                    //     skel.Skeleton.SetSkin(j.initialSkin);
                    //     skel.Skeleton.SetSlotsToSetupPose();
                    // }
                    // if (!string.IsNullOrEmpty(j.initialAnimation)) {
                    //     skel.AnimationState.SetAnimation(0, j.initialAnimation, j.loopAnimation);
                    // }
                }
            }
            // ----------------------------------------------------------------------

            result.Add(go);
            idMap[tpl.id] = go;
        }
        return result;
    }

    // 清洗并拼接 Resources 路径： "unitName/file"
    private static string BuildResPath(string unitName, string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        string u = (unitName ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        string f = file.Trim().Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(u) 
            ? $"Characters/{f}"
            : $"Characters/{u}/{f}";
    }

    // 原样拷贝（不做数值兜底）
    private static UnitTemplate BuildTemplate(UnitJson j)
    {
        var so = ScriptableObject.CreateInstance<UnitTemplate>();

        so.id = j.id;
        so.uintName = j.uintName;
        so.Rarity = j.Rarity;
        so.cost = j.cost;

        so.attackMethod = j.attackMethod;
        so.actionMethod = j.actionMethod;

        so.HP = j.HP;
        so.atk = j.atk;
        so.def = j.def;
        so.res = j.res;

        so.attackInterval = j.attackInterval;
        so.attackRadius = j.attackRadius;
        so.BlockRadius = j.BlockRadius;

        so.moveSpeed = j.moveSpeed;
        so.isBlock = j.isBlock;

        so.FixedAbility = j.FixedAbility;

        so.LifeDeduct = j.LifeDeduct;
        so.narrowTitle = j.narrowTitle;

        return so;
    }
}
