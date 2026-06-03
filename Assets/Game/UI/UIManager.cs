// UIManager.cs —— 核心路由（无需进场景；支持新/旧输入；开局即生效）
// 修复点：遍历命中物体及父层级的所有组件，查找带 Payload 的组件，而不是只取一个任意组件。
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

public static class UIManager
{
    public static event Action<object> OnApplyData;
    private static bool s_listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitOnLoad() => EnsureListener();

    // ===== 对外 API =====
    public static void SwitchTo(object payload)
    {
        EnsureListener();
        if (!DataUISwitch.Instance.HasInstance || !DataUISwitch.Instance.Visible)
            DataUISwitch.Instance.Show();
        OnApplyData?.Invoke(payload);
    }

    public static void Close()
    {
        if (DataUISwitch.Instance.HasInstance && DataUISwitch.Instance.Visible)
            DataUISwitch.Instance.Hide();
    }

    // ===== 监听安装 =====
    private static void EnsureListener()
    {
        if (s_listening) return;
        s_listening = true;
#if ENABLE_INPUT_SYSTEM
        InputSystem.onEvent += HandleInputEvent_NewInput;
#else
        var go = new GameObject("__UIManagerRunner");
        go.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<LegacyClickRunner>();
#endif
    }

#if ENABLE_INPUT_SYSTEM
    // ===== 新输入系统：事件驱动 =====
    private static void HandleInputEvent_NewInput(InputEventPtr eventPtr, InputDevice device)
    {
        if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;
        var mouse = device as Mouse;
        if (mouse == null) return;
        if (!mouse.leftButton.wasPressedThisFrame) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            Debug.Log("[UIManager] 点击在 UI 上");
            return;
        }

        var cam = PickCamera();
        if (cam == null)
        {
            Debug.LogWarning("[UIManager] 未找到用于点击判定的相机");
            if (DataUISwitch.Instance.Visible) Close();
            return;
        }

        Vector2 pos = mouse.position.ReadValue();
        RouteByScreenPoint(cam, pos);
    }
#endif

    // ===== 公共路由逻辑 =====
    private static void RouteByScreenPoint(Camera cam, Vector2 screenPos)
    {
        bool panelVisible = DataUISwitch.Instance.Visible;

        // 3D
        var ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out var hit3D, float.MaxValue))
        {
            if (TryFindPayloadInParents(hit3D.collider.transform, out var payload3D))
            {
                Debug.Log($"[UIManager] 点击在可查看数据的物体上(3D): {hit3D.collider.name}");
                SwitchTo(payload3D);
                return;
            }
            Debug.Log("[UIManager] 点击在非数据物体上（视为空白）");
            if (panelVisible) Close();
            return;
        }

        // 2D
        var world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
        var hit2D = Physics2D.OverlapPoint((Vector2)world);
        if (hit2D != null)
        {
            if (TryFindPayloadInParents(hit2D.transform, out var payload2D))
            {
                Debug.Log($"[UIManager] 点击在可查看数据的物体上(2D): {hit2D.name}");
                SwitchTo(payload2D);
                return;
            }
            Debug.Log("[UIManager] 点击在非数据物体上（视为空白）");
            if (panelVisible) Close();
            return;
        }

        // 空白
        Debug.Log("[UIManager] 点击在空白区域");
        if (panelVisible) Close();
    }

    private static Camera PickCamera()
    {
        if (Camera.main != null) return Camera.main;
        if (Camera.current != null) return Camera.current;
        var all = Camera.allCameras;
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].enabled) return all[i];
        return null;
    }

    // ===== 修复关键：在命中物体及父层级中遍历所有组件，找到带 Payload 的那个 =====
    private static bool TryFindPayloadInParents(Transform t, out object payload)
    {
        payload = null;
        if (t == null) return false;

        // 包含自身与父层级，包含禁用组件
        var comps = t.GetComponentsInParent<Component>(true);
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;
            if (TryExtractPayload(c, out payload))
                return true;
        }
        return false;
    }

    // 从单个组件上“鸭子式”读取 Payload
    private static bool TryExtractPayload(Component comp, out object payload)
    {
        payload = null;
        var type = comp.GetType();

        var prop = type.GetProperty("Payload", BindingFlags.Instance | BindingFlags.Public);
        if (prop != null && prop.CanRead)
        {
            payload = prop.GetValue(comp);
            return true;
        }

        var field = type.GetField("Payload", BindingFlags.Instance | BindingFlags.Public);
        if (field != null)
        {
            payload = field.GetValue(comp);
            return true;
        }

        return false;
    }

    // ===== 旧输入系统降级 Runner =====
    private class LegacyClickRunner : MonoBehaviour
    {
        private void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                Debug.Log("[UIManager] 点击在 UI 上");
                return;
            }

            var cam = UIManager.PickCamera();
            if (cam == null)
            {
                Debug.LogWarning("[UIManager] 未找到用于点击判定的相机（旧输入）");
                if (DataUISwitch.Instance.Visible) UIManager.Close();
                return;
            }

            UIManager.RouteByScreenPoint(cam, Input.mousePosition);
        }

        private void OnApplicationQuit() => Destroy(gameObject);
    }
}
