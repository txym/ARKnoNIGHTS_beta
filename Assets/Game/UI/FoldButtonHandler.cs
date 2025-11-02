using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FoldButtonHandler
{
    public static void SwitchShopFolder(GameObject button,GameObject panel)
    {
        if(panel.activeSelf)
            panel.SetActive(false);
        else
            panel.SetActive(true);
        RectTransform rectTransform=button.GetComponent<RectTransform>();
        rectTransform.Rotate(0, 0, 180);
    }
}
