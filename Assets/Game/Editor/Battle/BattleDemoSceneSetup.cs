using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One-shot, idempotent wiring for TASK-005. It only adds BattleDemoRoot and never touches legacy scene objects.</summary>
public static class BattleDemoSceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string RootName = "BattleDemoRoot";

    [MenuItem("ARKnoNIGHTS/Battle/Setup TASK-005 Demo")]
    public static void SetupSampleScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == RootName) throw new InvalidOperationException(RootName + " already exists. Refusing to alter existing Demo wiring.");

        var rootObject = new GameObject(RootName);
        var viewsObject = new GameObject("BattleDemoViews");
        viewsObject.transform.SetParent(rootObject.transform, false);

        var uiObject = new GameObject("BattleDemoUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(BattleDemoUi));
        uiObject.transform.SetParent(rootObject.transform, false);
        var canvas = uiObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = uiObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var factory = rootObject.AddComponent<MappedBattlePresentationViewFactory>();
        var controller = rootObject.AddComponent<BattleDemoController>();
        SetObjectReference(factory, "unitParent", viewsObject.transform);
        SetObjectReference(controller, "presentationFactoryComponent", factory);
        SetObjectReference(controller, "demoUi", uiObject.GetComponent<BattleDemoUi>());

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[BattleDemo][scene.wired] Added BattleDemoRoot with serialized controller, data-driven factory, view parent, and uGUI panel to " + ScenePath + ".");
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
