// DataUISwitchInitializerFromPath.cs
// 挂到“初始化按钮”对象上；在 Button.onClick 里调用 InitUI() 即可（无需拖预制体，只填路径）。
using UnityEngine;

public class DataUISwitchInitializerFromPath
{
    private static string resourcesPath = "UIMaterial/UIPrefabs/UnitDescriptionPrefab";
    public static bool startActive = false;

    public static void InitUI()
    {
        DataUISwitch.Instance.InitializeFromPath(resourcesPath, startActive);
    }
}
