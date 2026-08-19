#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PredictiveFlyExperiment2Controller))]
public class PredictiveFlyExperiment2ControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PredictiveFlyExperiment2Controller controller =
            (PredictiveFlyExperiment2Controller)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Experiment 2 Preference Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to run or verify participant calibration.",
                MessageType.Info);
            return;
        }

        MessageType statusType = controller.State ==
                PredictiveFlyExperiment2Controller.CalibrationState.Error
            ? MessageType.Error
            : controller.State ==
                PredictiveFlyExperiment2Controller.CalibrationState.Complete
                ? MessageType.Info
                : MessageType.None;
        EditorGUILayout.HelpBox(
            $"State: {controller.State}\nStatus: {controller.Status}\n"
            + $"Delay order: {controller.DelayOrderPreview}",
            statusType);

        PredictiveGhostAvatarLocomotion locomotion = controller.locomotion;
        if (locomotion == null)
        {
            EditorGUILayout.HelpBox("Locomotion reference is missing.", MessageType.Error);
        }
        else
        {
            string bodyStatus = locomotion.bodyAnchorProvider != null
                ? locomotion.bodyAnchorProvider.LastStatus
                : "BodyAnchorProvider has not been created.";
            EditorGUILayout.HelpBox(
                $"Movement enabled: {locomotion.LocomotionInputEnabled}\n"
                + $"Delay buffer ready: {locomotion.InputToRigDelayBufferReady}\n"
                + $"Body anchor ready: {locomotion.IsBodyAnchorAvailable}\n"
                + $"Body anchor: {bodyStatus}",
                locomotion.IsBodyAnchorAvailable ? MessageType.None : MessageType.Warning);
        }

        if (GUILayout.Button("Start Full Calibration (F7 / K)"))
        {
            controller.BeginCalibrationSequence();
        }
        if (GUILayout.Button("Check Saved Calibration"))
        {
            controller.CheckSavedCalibrationFromInspector();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button($"Horizon -{controller.horizonStepSeconds:0.###} s"))
            {
                controller.DecreaseCalibrationHorizonFromInspector();
            }
            if (GUILayout.Button($"Horizon +{controller.horizonStepSeconds:0.###} s"))
            {
                controller.IncreaseCalibrationHorizonFromInspector();
            }
        }

        if (GUILayout.Button("Accept Calibration Run (Space)"))
        {
            controller.AcceptCalibrationRun();
        }
        if (GUILayout.Button("Abort Calibration (Escape)"))
        {
            controller.AbortCalibrationFromInspector();
        }

        Repaint();
    }
}
#endif
