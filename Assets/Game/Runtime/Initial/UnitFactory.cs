using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Spine.Unity;

public static class UnitFactory
{
    private const string JsonRootRel = "GameData/Units/EliteVariants/Json"; // 位于 Assets 下
    private const int LegacyMappedSkeletonType = 2;
    private const string PrefabResPath = "Prefabs/DefaultUnit"; // Resources.Load 不要带 "Resources/"

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
        out Dictionary<int, GameObject> idMap
        )
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

        List<LegacyResolvedSource> sources;
        try
        {
            sources = UnitEliteVariantResolver
                .LoadDirectory(rootAbs)
                .Values
                .OrderBy(item => item.TypeId)
                .Select(source =>
                {
                    var resolved = UnitEliteVariantResolver.Resolve(source, 0);
                    var idle = resolved.RequireAnimation("idle", source.Path);
                    var move = resolved.RequireAnimation("move", source.Path);
                    var attack = resolved.attackMethod == 0
                        ? null
                        : resolved.RequireAnimation("attack", source.Path);
                    return new LegacyResolvedSource(
                        resolved,
                        idle.name,
                        move.name,
                        attack == null ? string.Empty : attack.name);
                })
                .ToList();
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"[UnitFactory] v2 单位源加载失败: {rootAbs}: {exception.Message}");
            return result;
        }

        int mNextUnitID = -1;

        foreach (var source in sources)
        {
            var resolved = source.Resolved;
            var tpl = BuildTemplate(resolved);

            if (sUnitSOMap.ContainsKey(tpl.typeID))
            {
                Debug.LogError($"[UnitFactory] �ظ��� typeID: {tpl.typeID}");
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
                unitIdentity.unitID = mNextUnitID;
                mNextUnitID--;
            }
            var unitSkel = go.AddComponent<UnitSkelType2>();
            unitSkel.unitIdentity = unitIdentity;
            unitSkel.ConfigureLegacySourceAnimations(
                source.IdleAnimationName,
                source.MoveAnimationName,
                source.AttackAnimationName);
            var skel = go.GetComponent<SkeletonAnimation>();
            if (!skel)
            {
                Debug.LogError("[UnitFactory] DefaultUnit 上缺少 SkeletonAnimation 组件");
            }
            else
            {

                var resPath = UnitResourcePaths.BuildSkeletonDataResourcePath(
                    resolved.typeId,
                    resolved.resourceKey,
                    resolved.skeletonDataResourceName);
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

    // 原样拷贝（不做数值兜底）
    private static UnitTemplate BuildTemplate(ResolvedUnitVariant source)
    {
        var so = ScriptableObject.CreateInstance<UnitTemplate>();

        so.typeID = source.typeId;
        so.uintName = source.resourceKey;
        so.ProfilePicture = source.profilePictureResourceName;
        so.Rarity = source.rarity;
        so.cost = source.deploymentCost;

        so.attackMethod = source.attackMethod;
        so.actionMethod = source.actionMethod;
        so.unitskeltype = LegacyMappedSkeletonType;

        so.HP = source.maxHitPoints;
        so.atk = source.attack;
        so.def = source.defense;
        so.res = source.magicResistance;

        so.attackInterval = source.BaseAttackIntervalSeconds;
        so.attackRadius = source.attackRadiusMetres;
        so.BlockRadius = source.blockRadiusMetres;

        so.moveSpeed = source.moveSpeedMetresPerSecond;
        so.isBlock = source.canBlock;

        so.FixedAbility = new List<string>(source.innateAbilityIds);

        so.LifeDeduct = source.lifeDeduct;
        so.narrowTitle = source.tauntLevel;

        return so;
    }

    private sealed class LegacyResolvedSource
    {
        internal LegacyResolvedSource(
            ResolvedUnitVariant resolved,
            string idleAnimationName,
            string moveAnimationName,
            string attackAnimationName)
        {
            Resolved = resolved;
            IdleAnimationName = idleAnimationName;
            MoveAnimationName = moveAnimationName;
            AttackAnimationName = attackAnimationName;
        }

        internal ResolvedUnitVariant Resolved { get; }
        internal string IdleAnimationName { get; }
        internal string MoveAnimationName { get; }
        internal string AttackAnimationName { get; }
    }
}
