using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UIElements;

public class ButtonDebug : MonoBehaviour
{
    public List<GameObject> units = new List<GameObject>();
    public Dictionary<int, GameObject> idMap = new Dictionary<int, GameObject>();
    //public GameObject test;
    public VirtualSlotPanel virtualSlotPanel;
    public void Start()
    {
        for(int i=0;i<12;i++)
        {
            //GameObject newgameobject = Instantiate(test);
            virtualSlotPanel.PlaceObjectInSlot(i);
        }
    }

    public void CreateAllUnits(List<GameObject> unitPrefabs)
    {
        if (unitPrefabs == null || unitPrefabs.Count == 0)
        {
            Debug.LogWarning("Unit prefabs list is null or empty");
            return;
        }

        int spawnedCount = 0;
        int maxUnits = unitPrefabs.Count;

        for (int x = 1; x <= 9; x++)
        {
            for (int y = 1; y <= 8; y++)
            {
                // 跳过特定位置
                if (x == 5 && (y == 1 || y == 8))
                    continue;

                // 检查是否已生成所有单位
                if (spawnedCount >= maxUnits)
                    return;

                string positionName = $"Block({x},{y})";
                GameObject positionMarker = GameObject.Find(positionName);

                if (positionMarker == null)
                {
                    Debug.LogWarning($"Position marker not found: {positionName}");
                    continue;
                }

                // 实例化单位
                Instantiate(unitPrefabs[spawnedCount], positionMarker.transform.position + new Vector3(0, 50, 0), unitPrefabs[spawnedCount].transform.rotation);

                spawnedCount++;

                Debug.Log($"生成单位 #{spawnedCount} 在坐标: ({x},{y})");
            }
        }

        if (spawnedCount < maxUnits)
        {
            Debug.LogWarning($"只有 {spawnedCount} 个单位被生成，但提供了 {maxUnits} 个预设体");
        }
    }
    public static void DeploymentUnit(GameObject unit,(int,int) position)
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
    public void Debugbutton()
    {
        units = UnitFactory.SpawnAll(
            parent: this.transform,   // 全部挂到当前物体下面
            setInactive: false,       // 若想先隐藏，改成 true，等布置好再 SetActive(true)
            out idMap
        );
        CreateAllUnits(units);
    }
   


}
