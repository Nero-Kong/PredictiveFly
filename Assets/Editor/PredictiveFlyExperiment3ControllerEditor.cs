#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PredictiveFlyExperiment3Controller))]
public class PredictiveFlyExperiment3ControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PredictiveFlyExperiment3Controller controller =
            (PredictiveFlyExperiment3Controller)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Experiment 3 Runtime Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to check Experiment 2 data and start Experiment 3 trials.",
                MessageType.Info);
            return;
        }

        MessageType statusType = controller.State ==
                PredictiveFlyExperiment3Controller.Experiment3State.Error
            ? MessageType.Error
            : MessageType.Info;
        EditorGUILayout.HelpBox(
            $"State: {controller.State}\nStatus: {controller.Status}",
            statusType);

        string questionnaireCode = controller.QuestionnaireDisplayCode;
        EditorGUILayout.HelpBox(
            string.IsNullOrWhiteSpace(questionnaireCode)
                ? "Questionnaire mode code is unavailable."
                : $"QUESTIONNAIRE MODE:  {questionnaireCode}",
            string.IsNullOrWhiteSpace(questionnaireCode)
                ? MessageType.Warning
                : MessageType.Info);

        EditorGUILayout.HelpBox(
            controller.CalibrationReady
                ? $"Completed calibration loaded for {controller.CalibratedParticipantId}\n"
                    + $"Experiment 2 H(0/250/500/750/1000 ms): "
                    + $"{controller.SelectedHorizon0Seconds:0.0} / "
                    + $"{controller.SelectedHorizon250Seconds:0.0} / "
                    + $"{controller.SelectedHorizon500Seconds:0.0} / "
                    + $"{controller.SelectedHorizon750Seconds:0.0} / "
                    + $"{controller.SelectedHorizon1000Seconds:0.0} s\n"
                    + $"Profile: {controller.CalibrationProfilePath}"
                : "Experiment 3 is locked until matching completed Experiment 2 data are loaded.",
            controller.CalibrationReady ? MessageType.Info : MessageType.Warning);

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

        if (GUILayout.Button("Refresh Completed Calibration From Disk"))
        {
            controller.RefreshCalibrationFromInspector();
        }
        if (GUILayout.Button("Open Questionnaire Mode Window"))
        {
            PredictiveFlyExperiment3QuestionnaireWindow.OpenWindow();
        }
        if (GUILayout.Button("Start Experiment 3 Trial (Enter)"))
        {
            controller.PrepareAndStartTrial();
        }
        if (GUILayout.Button("Abort Active Run (Escape)"))
        {
            controller.AbortActiveRunFromInspector();
        }

        Repaint();
    }
}
#endif
