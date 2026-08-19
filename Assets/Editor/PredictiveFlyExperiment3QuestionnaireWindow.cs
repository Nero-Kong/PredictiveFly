#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class PredictiveFlyExperiment3QuestionnaireWindow : EditorWindow
{
    bool showOperatorDetails;

    [MenuItem("Window/PredictiveFly/Experiment 3 Questionnaire Mode")]
    public static void OpenWindow()
    {
        OpenWindow(true);
    }

    internal static void OpenWindow(bool focus)
    {
        PredictiveFlyExperiment3QuestionnaireWindow window =
            GetWindow<PredictiveFlyExperiment3QuestionnaireWindow>();
        window.titleContent = new GUIContent("E3 Questionnaire");
        window.minSize = new Vector2(390f, 285f);
        window.Show();
        if (focus)
        {
            window.Focus();
        }
    }

    void OnEnable()
    {
        EditorApplication.update += Repaint;
    }

    void OnDisable()
    {
        EditorApplication.update -= Repaint;
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(
            "Experiment 3 Questionnaire Mode",
            EditorStyles.boldLabel);

        PredictiveFlyExperiment3Controller controller = FindController();
        if (controller == null)
        {
            EditorGUILayout.HelpBox(
                "PredictiveFlyExperiment3Controller was not found in the open scene.",
                MessageType.Warning);
            return;
        }

        string code = controller.QuestionnaireDisplayCode;
        Rect codeRect = GUILayoutUtility.GetRect(
            100f,
            112f,
            GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(codeRect, new Color(0.07f, 0.09f, 0.11f, 1f));
        GUIStyle codeStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 48,
            normal = { textColor = Color.white }
        };
        GUI.Label(codeRect, string.IsNullOrWhiteSpace(code) ? "--" : code, codeStyle);

        bool completed = controller.State ==
                PredictiveFlyExperiment3Controller.Experiment3State.TrialCompleted
            || controller.State ==
                PredictiveFlyExperiment3Controller.Experiment3State.SessionCompleted;
        bool aborted = controller.State ==
            PredictiveFlyExperiment3Controller.Experiment3State.TrialAborted;
        string instruction = completed
            ? "Trial completed. Use the code above for the questionnaire."
            : aborted
                ? "Trial aborted. Do not submit it as a completed questionnaire condition."
                : controller.State ==
                    PredictiveFlyExperiment3Controller.Experiment3State.RunningTrial
                    ? "Trial is running. Confirm the code after the course is completed."
                    : "Preview of the active or scheduled questionnaire condition.";
        EditorGUILayout.HelpBox(
            instruction,
            aborted ? MessageType.Warning : MessageType.Info);

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(code)))
        {
            if (GUILayout.Button("Copy Questionnaire Code"))
            {
                EditorGUIUtility.systemCopyBuffer = code;
            }
        }

        showOperatorDetails = EditorGUILayout.Foldout(
            showOperatorDetails,
            "Operator details",
            true);
        if (showOperatorDetails)
        {
            bool hasActiveAssignment = controller.ActiveTrialIndex > 0
                && controller.State !=
                    PredictiveFlyExperiment3Controller.Experiment3State.Idle;
            EditorGUILayout.LabelField(
                "Participant",
                hasActiveAssignment
                    ? controller.ActiveParticipantId
                    : controller.participantId);
            EditorGUILayout.LabelField(
                "Trial",
                (hasActiveAssignment
                    ? controller.ActiveTrialIndex
                    : controller.trialIndex).ToString());
            EditorGUILayout.LabelField("State", controller.State.ToString());

            if (hasActiveAssignment)
            {
                EditorGUILayout.LabelField(
                    "Assigned condition",
                    $"{controller.ActiveDelayMilliseconds} ms / {controller.ActiveStrategy}");
                EditorGUILayout.LabelField("Route", controller.ActiveRouteId);
                EditorGUILayout.LabelField(
                    "Maximum horizon",
                    $"{controller.ActiveMaximumTranslationHorizonSeconds:0.0} s");
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Scheduled assignment",
                    controller.ScheduledAssignmentPreview);
            }
        }
    }

    static PredictiveFlyExperiment3Controller FindController()
    {
        return Object.FindFirstObjectByType<PredictiveFlyExperiment3Controller>(
            FindObjectsInactive.Include);
    }
}

[InitializeOnLoad]
static class PredictiveFlyExperiment3QuestionnaireWindowAutoOpen
{
    static string lastCompletedTrialKey;

    static PredictiveFlyExperiment3QuestionnaireWindowAutoOpen()
    {
        EditorApplication.update += MonitorCompletedTrial;
    }

    static void MonitorCompletedTrial()
    {
        if (!Application.isPlaying)
        {
            lastCompletedTrialKey = string.Empty;
            return;
        }

        PredictiveFlyExperiment3Controller controller =
            Object.FindFirstObjectByType<PredictiveFlyExperiment3Controller>(
                FindObjectsInactive.Include);
        if (controller == null
            || (controller.State !=
                    PredictiveFlyExperiment3Controller.Experiment3State.TrialCompleted
                && controller.State !=
                    PredictiveFlyExperiment3Controller.Experiment3State.SessionCompleted))
        {
            return;
        }

        string completedTrialKey =
            $"{controller.ActiveParticipantId}|{controller.ActiveTrialIndex}";
        if (completedTrialKey == lastCompletedTrialKey)
        {
            return;
        }

        lastCompletedTrialKey = completedTrialKey;
        PredictiveFlyExperiment3QuestionnaireWindow.OpenWindow(false);
    }
}
#endif
