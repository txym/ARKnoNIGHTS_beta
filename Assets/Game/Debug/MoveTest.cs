using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveTest : MonoBehaviour
{
  
         public static MoveTest Instance { get; private set; }
    public  List<UnitSkel> UnitSkelList;
    public  int index = -1;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    public  void Movetest()
    {
        UnitSkelList[index].ApplyHostMoveCommand(new Vector3(200,50,200),new Vector3(300,50,300),5);
    }
}
