using System;
using ArknoNights.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Idempotent, one-shot scene wiring for UI-002. It preserves BattleDemoRoot and only hides legacy UI objects.</summary>
public static class UI002SampleSceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string HudRootName = "FormalBattleHudRoot";

    [MenuItem("ARKnoNIGHTS/UI/Setup UI-002 Formal HUD")]
    public static void SetupSampleScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = FindRoot(scene, HudRootName);
        if (root == null) root = new GameObject(HudRootName);
        var hud = root.GetComponent<StagingHudController>() ?? root.AddComponent<StagingHudController>();
        var toggle = root.GetComponent<BattleDemoUiVisibilityToggle>() ?? root.AddComponent<BattleDemoUiVisibilityToggle>();

        var demoUi = FindRoot(scene, "BattleDemoRoot")?.transform.Find("BattleDemoUI");
        if (demoUi == null) throw new InvalidOperationException("BattleDemoRoot/BattleDemoUI is missing; refusing to guess a UI surface.");
        if (demoUi.GetComponent<CanvasGroup>() == null) demoUi.gameObject.AddComponent<CanvasGroup>();
        SetObjectReference(toggle, "battleDemoUiRoot", demoUi.gameObject);

        HideLegacy(scene, "InitButton");
        HideLegacy(scene, "ShopPanel");
        HideLegacy(scene, "FoldButton");
        HideLegacy(scene, "ButtonTest");
        foreach (var legacyPanel in UnityEngine.Object.FindObjectsOfType<VirtualSlotPanel>(true))
            if (legacyPanel != null && legacyPanel.gameObject.scene == scene) legacyPanel.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[StagingHud][scene.wired] root=" + hud.name + "; legacy UI hidden; BattleDemoRoot remains active.");
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        GameObject found = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!string.Equals(root.name, name, StringComparison.Ordinal)) continue;
            if (found != null) throw new InvalidOperationException("More than one " + name + " exists; refusing to choose one.");
            found = root;
        }
        return found;
    }

    private static void HideLegacy(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var item in all)
            {
                if (item != null && item.name == name) item.gameObject.SetActive(false);
            }
        }
    }

    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(propertyName);
        if (property == null) throw new InvalidOperationException("Serialized property was not found: " + propertyName);
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
