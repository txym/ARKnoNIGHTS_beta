using System;
using System.Linq;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

/// <summary>One-shot TASK-004A evidence probe for the existing Spine resources.</summary>
public static class Task004aSpineProbe
{
    public static void Run()
    {
        Probe("gopro", "Characters/gopro/enemy_1000_gopro_3_SkeletonData");
        Probe("arcslma", "Characters/arcslma/enemy_5503_arcslma_SkeletonData");
    }

    private static void Probe(string unitName, string resourcePath)
    {
        var asset = Resources.Load<SkeletonDataAsset>(resourcePath);
        if (!asset)
        {
            throw new InvalidOperationException("TASK004A_SPINE_MISSING unit=" + unitName + " resource=" + resourcePath);
        }

        var skeletonData = asset.GetSkeletonData(true);
        if (skeletonData == null)
        {
            throw new InvalidOperationException("TASK004A_SPINE_INVALID unit=" + unitName + " resource=" + resourcePath);
        }

        var animations = skeletonData.Animations.Items
            .Where(animation => animation != null)
            .Select(animation => animation.Name + "=" + animation.Duration.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Debug.Log("TASK004A_SPINE unit=" + unitName + " resource=" + resourcePath + " animations=" + string.Join(",", animations));
    }
}
