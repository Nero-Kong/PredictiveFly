#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PredictiveFlyExperimentSceneUpgrade
{
    const string OfficialScenePath = "Assets/Scenes/IrairaBou3D_Official.unity";

    static PredictiveFlyExperimentSceneUpgrade()
    {
        EditorApplication.delayCall += UpgradeOpenOfficialSceneIfNeeded;
    }

    [MenuItem("PredictiveFly/Rebuild And Save Official Experiment Scene _F9")]
    public static void RebuildAndSaveOfficialScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[PredictiveFly] Stop Play Mode before rebuilding the experiment scene.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != OfficialScenePath)
        {
            scene = EditorSceneManager.OpenScene(OfficialScenePath, OpenSceneMode.Single);
        }

        IrairaBou3DOfficialSceneBuilder builder = Object.FindFirstObjectByType<IrairaBou3DOfficialSceneBuilder>();
        if (builder == null)
        {
            Debug.LogError("[PredictiveFly] Official scene builder was not found.");
            return;
        }

        builder.RebuildScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PredictiveFly] Rebuilt and saved the official experiment scene.");
    }

    static void UpgradeOpenOfficialSceneIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != OfficialScenePath)
        {
            return;
        }

        IrairaBou3DOfficialSceneBuilder builder = Object.FindFirstObjectByType<IrairaBou3DOfficialSceneBuilder>();
        if (builder == null)
        {
            return;
        }

        bool needsUpgrade = builder.GetComponent<PredictiveFlyObjectiveLogger>() == null
            || Object.FindFirstObjectByType<IrairaBouCheckpoint>() == null
            || Object.FindFirstObjectByType<IrairaBouFinish>() == null;
        if (!needsUpgrade)
        {
            return;
        }

        builder.RebuildScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PredictiveFly] Upgraded and saved the open official experiment scene.");
    }
}
#endif
