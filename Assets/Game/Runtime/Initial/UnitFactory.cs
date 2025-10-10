using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Spine.Unity;

public static class UnitFactory
{
    private const string JsonRootRel = "GameData/Units/Json"; // λ�� Assets ��
    private const string PrefabResPath = "Prefabs/DefaultUnit"; // Resources.Load ��Ҫ�� "Resources/"

    // �����ڻ��棨���ⲻ��¶��
    private static Dictionary<int, UnitTemplate> sUnitSOMap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => sUnitSOMap = new Dictionary<int, UnitTemplate>(); // ���� Play ʱ��һ�Σ������� Domain Reload ճס��

    /// �� typeId ȡ unitSO��ȡ�������� null ��
    public static UnitTemplate GetUnitBasicValueSO(int typeId)
    {
        if (sUnitSOMap != null && sUnitSOMap.TryGetValue(typeId, out var tpl))
            return tpl;

        Debug.LogError($"[UnitFactory] δ�ҵ� UnitBasicValueSO��typeId={typeId}");
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
                Debug.LogError($"[UnitFactory] JSON ����ʧ��: {file}");
                continue;
            }
            if (j == null)
            {
                Debug.LogError($"[UnitFactory] JSON Ϊ��: {file}");
                continue;
            }

            var tpl = BuildTemplate(j);

            if (sUnitSOMap.ContainsKey(tpl.typeID))
            {
                Debug.LogError($"[UnitFactory] �ظ��� typeID: {tpl.typeID}����Դ�ļ���{file}");
                continue; // ���߸��ǣ�soMap[tpl.typeID] = tpl;
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

            // ---- ���ٵ��� UnitView.ApplySkeleton������ֱ�Ӹ� SkeletonAnimation ----


            var skel = go.GetComponent<SkeletonAnimation>();
            if (!skel)
            {
                Debug.LogError("[UnitFactory] DefaultUnit ��ȱ�� SkeletonAnimation ���");
            }
            else
            {
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
            idMap[tpl.typeID] = go;
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
