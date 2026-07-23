using ArknoNights.Deployment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Idempotently adds the UI-003 input owner without changing map, camera, prefabs, or existing HUD bindings.</summary>
public static class UI003SampleSceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("ARKnoNIGHTS/UI/Setup UI-003 State-Driven Deployment")]
    public static void SetupSampleScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hudRoot = GameObject.Find("FormalBattleHudRoot");
        if (!hudRoot) throw new System.InvalidOperationException("[UI-003][scene.hudRoot.missing] FormalBattleHudRoot is required.");
        if (!hudRoot.GetComponent<StateDrivenDeploymentController>()) hudRoot.AddComponent<StateDrivenDeploymentController>();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[UI-003][scene.wired] FormalBattleHudRoot has exactly one StateDrivenDeploymentController.");
    }
}
