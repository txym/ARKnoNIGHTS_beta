// UnitSoRef.cs
using UnityEngine;

[DisallowMultipleComponent]
public class UnitTemplateReference : MonoBehaviour
{
    [Tooltip("����λ��Ӧ������ʱ UnitTemplate")]
    public UnitTemplate unitTemplate;

    public UnitTemplate Template => unitTemplate; // ���ֻ������
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
