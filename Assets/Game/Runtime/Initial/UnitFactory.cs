using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Spine.Unity;

public static class UnitFactory
{
    private const string JsonRootRel = "GameData/Units/Json"; // 位于 Assets 下
    private const string PrefabResPath = "Prefabs/DefaultUnit"; // Resources.Load 不要带 "Resources/"

    // 运行期缓存（对外不暴露）
    private static Dictionary<int, UnitTemplate> sUnitSOMap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => sUnitSOMap = new Dictionary<int, UnitTemplate>(); // 进入 Play 时清一次（避免无 Domain Reload 粘住）

    /// 按 typeId 取 unitSO（取不到返回 null ）
    public static UnitTemplate GetUnitBasicValueSO(int typeId)
    {
        if (sUnitSOMap != null && sUnitSOMap.TryGetValue(typeId, out var tpl))
            return tpl;

        Debug.LogError($"[UnitFactory] 未找到 UnitBasicValueSO，typeId={typeId}");
        return null;
    }

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

        int mNextUnitID = -1;

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

            if (sUnitSOMap.ContainsKey(tpl.typeID))
            {
                Debug.LogError($"[UnitFactory] 重复的 typeID: {tpl.typeID}，来源文件：{file}");
                continue; // 或者覆盖：soMap[tpl.typeID] = tpl;
            }
            sUnitSOMap.Add(tpl.typeID, tpl);

            var go = Object.Instantiate(prefab, parent);
            if (setInactive) go.SetActive(false);
            go.name = string.IsNullOrEmpty(tpl.uintName) ? $"Unit_{tpl.typeID}" : $"{tpl.uintName}_{tpl.typeID}";

            var unitIdentity = go.AddComponent<UnitIdentity>();
            if (unitIdentity) 
            { 
                unitIdentity.SetTypeOnce(tpl.typeID);
                unitIdentity.UnitID = mNextUnitID;
                mNextUnitID--;
            }

            // ---- 不再调用 UnitView.ApplySkeleton；这里直接改 SkeletonAnimation ----


            var skel = go.GetComponent<SkeletonAnimation>();
            if (!skel)
            {
                Debug.LogError("[UnitFactory] DefaultUnit 上缺少 SkeletonAnimation 组件");
            }
            else
            {
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
            idMap[tpl.typeID] = go;
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

        so.typeID = j.id;
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
