// EventTest.cs —— 挂在 UI 开关上：点击时把 payload 交给 UIManager
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class EventTest : MonoBehaviour, IPointerClickHandler
{
    public UnityEvent OnOpened;
    public UnityEvent OnClosed;

    // 二选一（也可换成 ScriptableObject）——哪个有值就发哪个
    [SerializeField] private int unitId = 1000;
    [SerializeField] private string dataKey = "";

    private static int s_lastHandledFrame = -1; // 帧级防抖（多物体同帧点击）

    public void OnPointerClick(PointerEventData eventData)
    {
        if (s_lastHandledFrame == Time.frameCount) return;
        s_lastHandledFrame = Time.frameCount;

        if (!DataUISwitch.Instance.HasInstance)
        {
            Debug.LogWarning("[EventTest] 尚未初始化 DataUISwitch，忽略点击。请先用初始化按钮。");
            return;
        }

        bool wasVisible = DataUISwitch.Instance.Visible;

        // ❗关键：不在这里 Toggle/Show，而是全交给 UIManager 路由
        object payload = !string.IsNullOrEmpty(dataKey) ? (object)dataKey : (object)unitId;
        UIManager.SwitchTo(payload);

        bool nowVisible = DataUISwitch.Instance.Visible;
        if (!wasVisible && nowVisible) OnOpened?.Invoke();
        if (wasVisible && !nowVisible) OnClosed?.Invoke();
    }
}
