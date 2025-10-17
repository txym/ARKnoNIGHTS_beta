// DataPanelTrigger.cs —— 挂在 3D 物体上，提供 payload 给 UIManager
using UnityEngine;

public class Data3DTrigger : MonoBehaviour
{
    [Header("这个 ID 或 key 会传给数据面板")]
    public int unitId = 1000;

    // 也可以用字符串 / ScriptableObject / 自定义类等
    public string key = "";

    // UIManager 通过反射读取这个属性
    public object Payload => !string.IsNullOrEmpty(key) ? (object)key : (object)unitId;
}
