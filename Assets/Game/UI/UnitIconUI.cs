using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitIconUI : MonoBehaviour
{
    public int InstantiateCount;
    public List<GameObject> InstantiateGameObjects;

    [Header("布局设置")]
    public float spacing = 100f; // 图标之间的间距
    public bool horizontalLayout = true; // true:水平排列, false:垂直排列

    private void Start()
    {
        InstantiateGameObjects = GetAllChildren(this.gameObject.transform);
        UpdateLayout(); // 初始化时更新布局
    }

    public List<GameObject> GetAllChildren(Transform parent)
    {
        List<GameObject> children = new List<GameObject>();

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            children.Add(child.gameObject);
        }

        return children;
    }

    public void InstantiateWhich(int account)
    {
        // 先隐藏所有物体
        foreach (GameObject obj in InstantiateGameObjects)
        {
            obj.SetActive(false);
        }

        // 显示指定数量的物体
        for (int i = 0; i < account && i < InstantiateGameObjects.Count; i++)
        {
            InstantiateGameObjects[i].SetActive(true);
        }

        // 更新布局
        UpdateLayout();
    }

    // 更新布局方法
    private void UpdateLayout()
    {
        if (InstantiateGameObjects == null || InstantiateGameObjects.Count == 0)
            return;

        // 获取当前激活的物体
        List<GameObject> activeObjects = new List<GameObject>();
        foreach (GameObject obj in InstantiateGameObjects)
        {
            if (obj.activeInHierarchy)
            {
                activeObjects.Add(obj);
            }
        }

        if (activeObjects.Count == 0)
            return;

        if (horizontalLayout)
        {
            // 水平对称布局
            HorizontalSymmetricLayout(activeObjects);
        }
        else
        {
            // 垂直对称布局
            VerticalSymmetricLayout(activeObjects);
        }
    }

    // 水平对称布局
    private void HorizontalSymmetricLayout(List<GameObject> objects)
    {
        int count = objects.Count;
        float totalWidth = (count - 1) * spacing;

        for (int i = 0; i < count; i++)
        {
            RectTransform rectTransform = objects[i].GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                float xPos = -totalWidth / 2f + i * spacing;
                rectTransform.anchoredPosition = new Vector2(xPos, rectTransform.anchoredPosition.y);
            }
            else
            {
                // 如果是普通的Transform
                float xPos = -totalWidth / 2f + i * spacing;
                objects[i].transform.localPosition = new Vector3(xPos,
                    objects[i].transform.localPosition.y,
                    objects[i].transform.localPosition.z);
            }
        }
    }

    // 垂直对称布局
    private void VerticalSymmetricLayout(List<GameObject> objects)
    {
        int count = objects.Count;
        float totalHeight = (count - 1) * spacing;

        for (int i = 0; i < count; i++)
        {
            RectTransform rectTransform = objects[i].GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                float yPos = totalHeight / 2f - i * spacing;
                rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, yPos);
            }
            else
            {
                // 如果是普通的Transform
                float yPos = totalHeight / 2f - i * spacing;
                objects[i].transform.localPosition = new Vector3(objects[i].transform.localPosition.x,
                    yPos,
                    objects[i].transform.localPosition.z);
            }
        }
    }

    // 手动调用更新布局（在Inspector中测试用）
    [ContextMenu("更新布局")]
    public void UpdateLayoutManual()
    {
        UpdateLayout();
    }

    // 设置间距并更新布局
    public void SetSpacing(float newSpacing)
    {
        spacing = newSpacing;
        UpdateLayout();
    }

    // 切换布局方向
    public void ToggleLayoutDirection()
    {
        horizontalLayout = !horizontalLayout;
        UpdateLayout();
    }
}