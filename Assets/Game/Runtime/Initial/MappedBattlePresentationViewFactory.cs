using System;
using System.Collections.Generic;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using Spine.Unity;
using UnityEngine;

/// <summary>
/// Scene-configurable, explicit Core type to existing Unity resource bridge. TASK-005 can attach it to a
/// controller; TASK-004 intentionally creates no mapping asset or scene wiring.
/// </summary>
public sealed class MappedBattlePresentationViewFactory : MonoBehaviour, IBattlePresentationViewFactory
{
    private const string DefaultUnitPrefabResourcePath = "Prefabs/DefaultUnit";
    private const string DefaultCatalogResourcePath = "BattleData/unit-catalog-v1";

    [Serializable]
    private sealed class Binding
    {
        public string coreTypeId;
        public int legacyUnitTypeId;
        public int unitSkelType = 1;
        public GameObject prefabOverride;
        public SkeletonDataAsset skeletonData;
    }

    [SerializeField] private Transform unitParent;
    [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
    [SerializeField] private TextAsset unitCatalogAsset;

    /// <summary>
    /// Optional Player-safe catalog asset override. When unset, the generated Resources catalog is loaded.
    /// This keeps resource selection data-driven while allowing a scene or test harness to provide another
    /// validated catalog without type-specific factory branches.
    /// </summary>
    public TextAsset UnitCatalogAsset
    {
        get => unitCatalogAsset;
        set => unitCatalogAsset = value;
    }

    public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
    {
        view = null;
        diagnostic = null;
        var binding = Array.Find(bindings, item => item != null && string.Equals(item.coreTypeId, typeId, StringComparison.Ordinal));
        UnitCatalogEntry catalogEntry = null;
        var catalogResult = unitCatalogAsset
            ? UnitCatalogLoader.LoadFromJson(unitCatalogAsset.text)
            : UnitCatalogLoader.LoadFromResources(DefaultCatalogResourcePath);
        if (catalogResult.Success) catalogResult.Catalog.TryGet(typeId, out catalogEntry);
        if (binding == null && catalogEntry == null)
        {
            diagnostic = new BattlePresentationDiagnostic("resource.mapping.missing", "No catalog or explicit Unity resource binding exists for Core type " + typeId + ".");
            return false;
        }

        var prefabResourcePath = catalogEntry != null ? catalogEntry.PrefabResourcePath : DefaultUnitPrefabResourcePath;
        var prefab = binding != null && binding.prefabOverride ? binding.prefabOverride : Resources.Load<GameObject>(prefabResourcePath);
        if (!prefab)
        {
            diagnostic = new BattlePresentationDiagnostic("resource.prefab.missing", "DefaultUnit prefab is unavailable for Core type " + typeId + ".");
            return false;
        }

        var instance = Instantiate(prefab, unitParent ? unitParent : transform);
        var skeleton = instance.GetComponent<SkeletonAnimation>();
        if (!skeleton)
        {
            Destroy(instance);
            diagnostic = new BattlePresentationDiagnostic("resource.skeleton.missing", "Mapped prefab has no SkeletonAnimation for Core type " + typeId + ".");
            return false;
        }

        var skeletonData = binding != null && binding.skeletonData ? binding.skeletonData : catalogEntry != null ? Resources.Load<SkeletonDataAsset>(catalogEntry.SkeletonDataResourcePath) : skeleton.skeletonDataAsset;
        if (!skeletonData)
        {
            Destroy(instance);
            diagnostic = new BattlePresentationDiagnostic("resource.skeletonData.missing", "No SkeletonDataAsset is configured for Core type " + typeId + ".");
            return false;
        }

        try
        {
            skeleton.skeletonDataAsset = skeletonData;
            skeleton.Initialize(true);
        }
        catch (Exception exception)
        {
            Destroy(instance);
            diagnostic = new BattlePresentationDiagnostic("resource.skeleton.initialize.failed", "Failed to initialize SkeletonDataAsset for Core type " + typeId + ": " + exception.Message);
            return false;
        }

        var identity = instance.GetComponent<UnitIdentity>() ?? instance.AddComponent<UnitIdentity>();
        var legacyUnitTypeId = binding != null ? binding.legacyUnitTypeId : catalogEntry.LegacyUnitTypeId;
        var unitSkelType = binding != null ? binding.unitSkelType : catalogEntry.UnitSkelType;
        identity.SetTypeOnce(legacyUnitTypeId);
        var unitSkel = instance.GetComponent<UnitSkelBase>();
        if (!unitSkel)
        {
            switch (unitSkelType)
            {
                case 1:
                    unitSkel = instance.AddComponent<UnitSkelType1>();
                    break;
                case 2:
                    unitSkel = instance.AddComponent<UnitSkelType2>();
                    break;
                default:
                    Destroy(instance);
                    diagnostic = new BattlePresentationDiagnostic("resource.skeletonType.invalid", "Unsupported unit skeleton type for Core type " + typeId + ".");
                    return false;
            }
        }

        if (catalogEntry != null)
            unitSkel.ConfigureCatalogPresentationData(identity, catalogEntry.Definition.MoveSpeedCentimetresPerSecond / 100f, catalogEntry.Definition.AttackIntervalTicks / (float)ArknoNights.Battle.Core.BattleInput.TicksPerSecond);
        else
            unitSkel.unitIdentity = identity;

        var presentationView = instance.GetComponent<UnitSkelPresentationView>() ?? instance.AddComponent<UnitSkelPresentationView>();
        if (catalogEntry != null) presentationView.ConfigureStatusBarMaximumHitPoints(catalogEntry.Definition.MaxHitPoints);
        if (catalogEntry != null)
            presentationView.ConfigureAnimations(unitSkel, catalogEntry.MoveAnimation, catalogEntry.AttackAnimation, catalogEntry.HitAnimation, catalogEntry.DeathAnimation, catalogEntry.SkillAnimations);
        instance.name = "BattleView_" + unitId;
        view = presentationView;
        return true;
    }
}
