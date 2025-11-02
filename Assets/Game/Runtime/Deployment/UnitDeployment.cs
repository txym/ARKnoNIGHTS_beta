using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UnitDeployment : MonoBehaviour
{
    [Header("UI 识别（仅用来判断何时可以生成）")]
    public Camera uiCamera;                 // 你的 UICamera（Overlay 可留空）
    public string unitUITag = "UnitUI";     // 鼠标悬停识别的 UI 标签
    public GameObject TEST;                 // 调试：当前命中的 UI 对象

    [Space(8)]
    [Header("生成 3D 物体")]
    private GameObject dragPrefab;           // 只生成 3D 物体（已移除 UI 生成逻辑）
    public Camera worldCamera;              // 用于屏幕转世界的相机（留空用 Camera.main）
    public float fixedYValue = 50f;         // 3D 物体在 y=50 的水平面上移动

    [Space(8)]
    [Header("显示边界（仅当在范围内才显示）")]
    public float minX = 0f;
    public float maxX = 900f;
    public float minZ = 0f;
    public float maxZ = 800f;

    // 运行态
    private readonly List<RaycastResult> _results = new List<RaycastResult>(32);
    private PointerEventData _ped;
    private GameObject _dragInstance;

    void Update()
    {
        // 1) 更新 TEST（识别鼠标下的 UnitUI）
        TEST = GetUnitUIUnderMouse();

        // 2) 左键按下：当 TEST 非空时生成 3D 实例并开始拖拽
        if (Input.GetMouseButtonDown(0) && TEST != null && _dragInstance == null)
        {
            StartDrag3D();
        }

        // 3) 左键按住：更新位置 + 边界显示
        if (Input.GetMouseButton(0) && _dragInstance != null)
        {
            UpdateDrag3DPosition();
            ApplyBoundsVisibility();
        }

        // 4) 左键松开：结束拖拽（保留实例）
        if (Input.GetMouseButtonUp(0) && _dragInstance != null)
        {
            var posiyion=ConvertCoordinate(_dragInstance.transform.position);
            DeploymentUnit(_dragInstance,posiyion);
            ApplyBoundsVisibility();
            if(!_dragInstance.activeSelf)
            EndDrag3D(keepInstance: false);
            _dragInstance = null;
        }

        // 右键或 Esc 取消并删除当前实例（可选）
        if (_dragInstance != null && (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)))
        {
            EndDrag3D(keepInstance: false);
        }
    }

    // ============== UI 命中（仅用于允许生成） ==============
    public GameObject GetUnitUIUnderMouse()
    {
        if (EventSystem.current == null) return null;

        _ped ??= new PointerEventData(EventSystem.current);
        _ped.Reset();
        _ped.position = Input.mousePosition;

        _results.Clear();
        EventSystem.current.RaycastAll(_ped, _results);

        // 若指定了 UICamera：仅保留 eventCamera==uiCamera 的结果；
        // 同时保留 eventCamera==null（Overlay Canvas）的结果。
        if (uiCamera != null)
        {
            _results.RemoveAll(r =>
            {
                var gr = r.module as GraphicRaycaster;
                if (gr == null) return true;
                if (gr.eventCamera == null) return false; // Overlay：保留
                return gr.eventCamera != uiCamera;
            });
        }

        foreach (var r in _results)
        {
            var go = r.gameObject;
            if (go != null && (go.CompareTag(unitUITag) || FindTaggedAncestor(go, unitUITag) != null))
                return go.CompareTag(unitUITag) ? go : FindTaggedAncestor(go, unitUITag);
        }

        return null;
    }

    private static GameObject FindTaggedAncestor(GameObject child, string tag)
    {
        for (var t = child.transform; t != null; t = t.parent)
            if (t.gameObject.CompareTag(tag)) return t.gameObject;
        return null;
    }

    // ============== 3D 拖拽（唯一路径） ==============
    private void StartDrag3D()
    {
        List<GameObject> units = ButtonDebug.Instance.units;
        if (units.Count==0)
        {
            Debug.LogWarning("未初始化");
            return;
        }
        EventTest eventTest=TEST.GetComponent<EventTest>();
        int PrefabId = eventTest.UnitID;
        dragPrefab = ButtonDebug.Instance.idMap[PrefabId];
       _dragInstance = Instantiate(dragPrefab);
        UnitSkel unitSkel =_dragInstance.GetComponent<UnitSkel>();
        if (unitSkel != null) { unitSkel.PlayAnimation("Idle");
        MoveTest.Instance.UnitSkelList.Add(unitSkel);
            MoveTest.Instance.index++;
                }

       // 初始位置：以识别到的 UI 的屏幕点，映射到 y=fixedYValue 平面
       var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam != null)
        {
            Vector3 startScreen = GetUIScreenPosition(TEST);
            _dragInstance.transform.position = ScreenToWorldOnFixedY(cam, startScreen, fixedYValue);
        }
        else
        {
            // 没有可用相机，落在 (0,fixedY,0)
            _dragInstance.transform.position = new Vector3(0f, fixedYValue, 0f);
        }

        ApplyBoundsVisibility();
    }

    private void UpdateDrag3DPosition()
    {
        if (_dragInstance == null) return;

        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null) return;

        Vector3 pos = ScreenToWorldOnFixedY(cam, Input.mousePosition, fixedYValue);
        _dragInstance.transform.position = pos;
    }

    private void EndDrag3D(bool keepInstance)
    {
        if (!keepInstance && _dragInstance != null)
        {
            Destroy(_dragInstance);
        }
        _dragInstance = null;
    }

    // ============== 边界与可见性 ==============
    private void ApplyBoundsVisibility()
    {
        if (_dragInstance == null) return;
        var p = _dragInstance.transform.position;
        bool inside = p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ;

        if (_dragInstance.activeSelf != inside)
            _dragInstance.SetActive(inside);
    }

    // ============== 坐标换算工具 ==============
    private Vector3 GetUIScreenPosition(GameObject uiGo)
    {
        if (uiGo == null) return Input.mousePosition;

        var c = uiGo.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (c != null)
        {
            cam = (c.renderMode == RenderMode.ScreenSpaceOverlay)
                ? null
                : (c.worldCamera != null ? c.worldCamera : uiCamera);
        }
        else cam = uiCamera;

        return RectTransformUtility.WorldToScreenPoint(cam, uiGo.transform.position);
    }

    private static Vector3 ScreenToWorldOnFixedY(Camera cam, Vector3 screenPos, float fixedY)
    {
        // 与水平面 y=fixedY 相交
        var plane = new Plane(Vector3.up, new Vector3(0f, fixedY, 0f));
        var ray = cam.ScreenPointToRay(screenPos);
        if (plane.Raycast(ray, out float enter))
            return ray.GetPoint(enter);

        // 兜底：用 ScreenToWorldPoint 的深度推算，再强制 y=fixedY
        var fallback = screenPos;
        fallback.z = Mathf.Abs(cam.transform.position.y - fixedY);
        var wp = cam.ScreenToWorldPoint(fallback);
        wp.y = fixedY;
        return wp;
    }

    // ============== Gizmos（可视化边界，编辑器里看） ==============
    public static void DeploymentUnit(GameObject unit, (int, int) position)
    {
        int x = position.Item1;
        int z = position.Item2;
        if ((x == 5 && (z == 1 || z == 8)) || x < 1 || x > 9 || z < 1 || z > 8)
        {
            Destroy(unit);
            return;
        }
        string positionName = $"Block({x},{z})";
        GameObject positionMarker = GameObject.Find(positionName);
        unit.transform.position = positionMarker.transform.position + new Vector3(0, 50, 0);
    }
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // 在 y=fixedYValue 平面画一个边界矩形
        Gizmos.color = Color.yellow;
        Vector3 a = new Vector3(minX, fixedYValue, minZ);
        Vector3 b = new Vector3(maxX, fixedYValue, minZ);
        Vector3 c = new Vector3(maxX, fixedYValue, maxZ);
        Vector3 d = new Vector3(minX, fixedYValue, maxZ);
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
    }
    public static (int, int) ConvertCoordinate(Vector3 position)
    {
        int ConvertValue(float value)
        {
            return (int)Mathf.Floor((value + 50f) / 100f);
        }

        return (ConvertValue(position.x), ConvertValue(position.z));
    }//坐标转换这一块
#endif
}