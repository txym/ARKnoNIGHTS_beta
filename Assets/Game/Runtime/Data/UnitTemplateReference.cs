// UnitSoRef.cs
using UnityEngine;

[DisallowMultipleComponent]
public class UnitTemplateReference : MonoBehaviour
{
    [Tooltip("本单位对应的运行时 UnitTemplate")]
    public UnitTemplate unitTemplate;

    public UnitTemplate Template => unitTemplate; // 便捷只读属性
    public void SetTemplate(UnitTemplate t) => unitTemplate = t;

    [ContextMenu("Log UnitTemplate")]
    private void LogUnitTemplate()
    {
        Debug.Log(
            unitTemplate
                ? $"[{name}] SO = {unitTemplate.name} (id={unitTemplate.id})"
                : $"[{name}] SO = <null>",
            unitTemplate
        );
    }
}
