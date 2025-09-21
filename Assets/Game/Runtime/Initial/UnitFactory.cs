using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Spine.Unity;

public static class UnitFactory
{
    private const string JsonRootRel = "GameData/Units/Json"; // λ�� Assets ��
    private const string PrefabResPath = "Prefabs/DefaultUnit"; // Resources.Load ��Ҫ�� "Resources/"

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
            Debug.LogError($"[UnitFactory] JSON Ŀ¼������: {rootAbs}");
            return result;
        }

        var prefab = Resources.Load<GameObject>(PrefabResPath);
        if (!prefab)
        {
            Debug.LogError($"[UnitFactory] �Ҳ���Ԥ����: Resources/{PrefabResPath}");
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
                Debug.LogError($"[UnitFactory] JSON ����ʧ��: {file}");
                continue;
            }
            if (j == null)
            {
                Debug.LogError($"[UnitFactory] JSON Ϊ��: {file}");
                continue;
            }

            var tpl = BuildTemplate(j);

            var go = Object.Instantiate(prefab, parent);
            if (setInactive) go.SetActive(false);
            go.name = string.IsNullOrEmpty(tpl.uintName) ? $"Unit_{tpl.id}" : $"{tpl.uintName}_{tpl.id}";

            var refCmp = go.GetComponent<UnitTemplateReference>();
            if (refCmp == null) refCmp = go.AddComponent<UnitTemplateReference>();
            refCmp.SetTemplate(tpl);
            // ---- ���ٵ��� UnitView.ApplySkeleton������ֱ�Ӹ� SkeletonAnimation ----


            var skel = go.GetComponent<SkeletonAnimation>();
            if (!skel)
            {
                Debug.LogError("[UnitFactory] DefaultUnit ��ȱ�� SkeletonAnimation ���");
            }
            else
            {
                // ֱ��ʹ�� JSON �����Դ��
                // ���磺 "Characters/gopro/enemy_1000_gopro_3_SkeletonData"
                var resPath = BuildResPath(j.uintName, j.skeletonData);
                var sda = Resources.Load<SkeletonDataAsset>(resPath);
                if (!sda)
                {
                    Debug.LogError($"[UnitFactory] SkeletonDataAsset δ�ҵ�: Resources/{resPath}");
                }
                else
                {
                    skel.skeletonDataAsset = sda;
                    skel.Initialize(true);  // �ؼ����ؽ�����ʵ��

                    // ��� JSON ����������������Ͱ���ֵ�����ã�û��ͱ��� Inspector �������
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

    // ��ϴ��ƴ�� Resources ·���� "unitName/file"
    private static string BuildResPath(string unitName, string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        string u = (unitName ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        string f = file.Trim().Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(u) 
            ? $"Characters/{f}"
            : $"Characters/{u}/{f}";
    }

    // ԭ��������������ֵ���ף�
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
