using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;

public class ButtonDebug : MonoBehaviour
{
    public List<GameObject> units = new List<GameObject>();
    public Dictionary<int, GameObject> idMap = new Dictionary<int, GameObject>();
    public VirtualSlotPanel virtualSlotPanel;
    public GameObject ShopPanel;
    private string loadpath = "ProfilePicture/";
    public static ButtonDebug Instance { get; private set; }
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Start()
    {
        for(int i=0;i<12;i++)
        {
            //GameObject newgameobject = Instantiate(test);
            //virtualSlotPanel.PlaceObjectInSlot(i);
        }
    }
    public void Debugbutton()
    {
        units.Clear();
        units = UnitFactory.SpawnAll(
            parent: this.transform,   // 全部挂到当前物体下面
            setInactive: false,       // 若想先隐藏，改成 true，等布置好再 SetActive(true)
            out idMap
        );
        for(int i=0;i<units.Count;i++)
        {
            var obj = virtualSlotPanel.PlaceObjectInSlot(i);
            var image=obj.GetComponent<Image>();
            var eventText=obj.GetComponent<EventTest>();
            image.sprite= Resources.Load<Sprite>(loadpath+UnitFactory.GetUnitBasicValueSO(units[i].GetComponent<UnitIdentity>().UnitTypeID).ProfilePicture);
            eventText.ChangeUnitId(units[i].GetComponent<UnitIdentity>().UnitTypeID);
        }
        DataUISwitchInitializerFromPath.InitUI();
    }
    public void ShopPanelFolder( )
    {
        GameObject button = GameObject.Find("FoldButton");
        FoldButtonHandler.SwitchShopFolder(button, ShopPanel);
    }
   


}
