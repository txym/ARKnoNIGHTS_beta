using UnityEngine;

[DisallowMultipleComponent]
public class UnitIdentity : MonoBehaviour
{
    public int unitID;

    [SerializeField, HideInInspector] private string playerUnitId;

    // 私有字段保存，Inspector 可见但外部代码改不了
    [SerializeField] private int unitTypeID;

    // 对外只读访问
    public int UnitTypeID => unitTypeID;
    /// <summary>Stable PlayerState instance ID for preparation views; empty for legacy/debug prototypes.</summary>
    public string PlayerUnitId => playerUnitId;
    public object Payload =>unitTypeID;

    // 是否已经锁定（运行期防二次修改）
    [SerializeField, HideInInspector] private bool _typeLocked = false;

    /// <summary>仅允许设置一次类型ID。</summary>
    public void SetTypeOnce(int typeId)
    {
        if (_typeLocked)
        {
            Debug.LogWarning($"[UnitIdentity] {name} 已锁定为 type {unitTypeID}，忽略重复设置。");
            return; // 或者抛异常
        }
        unitTypeID = typeId;
        _typeLocked = true;
    }

    public void SetPlayerUnitId(string value)
    {
        if (!string.IsNullOrEmpty(playerUnitId) && !string.Equals(playerUnitId, value, System.StringComparison.Ordinal))
        {
            Debug.LogWarning($"[UnitIdentity] {name} 已绑定 player unit {playerUnitId}，忽略重复设置。", this);
            return;
        }
        playerUnitId = value ?? string.Empty;
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
