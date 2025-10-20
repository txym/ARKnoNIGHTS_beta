using UnityEngine;

[DisallowMultipleComponent]
public class UnitIdentity : MonoBehaviour
{
    public int unitID;

    // ˽���ֶα��棬Inspector �ɼ����ⲿ����Ĳ���
    [SerializeField] private int unitTypeID;

    // ����ֻ������
    public int UnitTypeID => unitTypeID;

    // �Ƿ��Ѿ������������ڷ������޸ģ�
    [SerializeField, HideInInspector] private bool _typeLocked = false;

    /// <summary>����������һ������ID��</summary>
    public void SetTypeOnce(int typeId)
    {
        if (_typeLocked)
        {
            Debug.LogWarning($"[UnitIdentity] {name} ������Ϊ type {unitTypeID}�������ظ����á�");
            return; 
        }
        unitTypeID = typeId;
        _typeLocked = true;
    }

    [ContextMenu("Log UnitBasicValueSO")]
    private void LogUnitBasicValueSO()
    {
        var tpl = UnitFactory.GetUnitBasicValueSO(unitTypeID);
        Debug.Log(
            tpl
                ? $"[{name}] SO = {tpl.name} (id={tpl.typeID})"
                : $"[{name}] SO = <null>",
            tpl
        );
    }
}
