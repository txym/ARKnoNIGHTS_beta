// EventTest.cs
// 挂在很多物体上：点击仅做开关，不会生成。初始化由按钮触发 DataUISwitchInitializerFromPath.InitUI()
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

// 添加 IPointerClickHandler 接口
public class EventTest : MonoBehaviour, IPointerClickHandler
{
    public UnityEvent OnOpened;
    public UnityEvent OnClosed;
    public bool toggleOnClick = true; // true: Toggle；false: 强制 Show

    private static int s_lastHandledFrame = -1; // 帧级防抖（多物体同帧点击）

    // 明确实现 IPointerClickHandler 接口的方法
    public void OnPointerClick(PointerEventData eventData)
    {
        if (s_lastHandledFrame == Time.frameCount) return;
        s_lastHandledFrame = Time.frameCount;

        if (!DataUISwitch.Instance.HasInstance)
        {
            Debug.LogWarning("[EventTest] 尚未初始化 DataUISwitch，忽略点击。请先用按钮初始化。");
            return;
        }

        bool wasVisible = DataUISwitch.Instance.Visible;

        if (toggleOnClick) DataUISwitch.Instance.Toggle();
        else DataUISwitch.Instance.Show();

        bool nowVisible = DataUISwitch.Instance.Visible;
        if (!wasVisible && nowVisible) OnOpened?.Invoke();
        if (wasVisible && !nowVisible) OnClosed?.Invoke();
    }
}