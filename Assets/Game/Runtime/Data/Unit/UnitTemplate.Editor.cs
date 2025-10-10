#if UNITY_EDITOR
using UnityEditor;
using Bitsets;

public partial class UnitTemplate
{
    public void RebuildInnateMask(TagRegistry registry)
    {
        innateMask.Clear();
        if (registry == null || FixedAbility == null) return;
        foreach (var tag in FixedAbility)
            if (!string.IsNullOrWhiteSpace(tag))
                innateMask.SetTag(tag.Trim(), registry, true);
        EditorUtility.SetDirty(this);
    }
}
#endif
