using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public class PredictiveFlyExperimentController : MonoBehaviour
{
    public enum TrialState
    {
        Idle,
        Preparing,
        Running,
        Completed,
        Aborted,
        Error
    }

    [Header("Operator Input")]
    [Tooltip("Set this once when the participant changes. The trailing positive number selects the counterbalancing schedule.")]
    public string participantId = "P001";
    [Tooltip("Set this to 1, 2, 3, or 4 before each trial. Condition and route are assigned automatically.")]
    [Range(1, 4)] public int trialIndex = 1;

    [Header("Trial Lifecycle")]
    public bool useKeyboardControls = true;
    public KeyCode prepareAndStartKey = KeyCode.Return;
    public KeyCode abortTrialKey = KeyCode.Escape;
    [Min(0f)] public float minimumPreparationSeconds = 0.75f;
    [Min(0.5f)] public float preparationTimeoutSeconds = 8f;
    public bool abortOnBodyAnchorLoss = true;
    [Min(0.1f)] public float trackingLossGraceSeconds = 5f;
    public bool recenterAfterTrial = true;
    [Tooltip("Prevent Enter from accidentally repeating a trial that already completed for the same participant.")]
    public bool preventAccidentalCompletedTrialRepeat = true;

    [Header("Output")]
    [Tooltip("Objective-data directory used by Experiment 1.")]
    public string objectiveOutputDirectory = "Data/PredictiveFlyObjective";

    [Header("References")]
    public PredictiveGhostAvatarLocomotion locomotion;
    public PredictiveFlyObjectiveLogger logger;
    public IrairaBou3DOfficialSceneBuilder sceneBuilder;
    public bool autoFindReferences = true;

    [Header("Debug")]
    [SerializeField] TrialState state = TrialState.Idle;
    [SerializeField] string scheduledAssignmentPreview;
    [SerializeField] string activeParticipantId;
    [SerializeField] int activeTrialIndex;
    [SerializeField] string activeConditionCode;
    [SerializeField] string activeConditionOrder;
    [SerializeField] string activeRouteOrder;
    [SerializeField] string activeRouteId;
    [SerializeField] string status = "Idle";
    [SerializeField] float trackingLostSince = -1f;

    Coroutine preparationRoutine;
    readonly HashSet<string> completedTrialKeys =
        new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    static readonly int[,] BalancedLatinSquare =
    {
        { 0, 1, 3, 2 },
        { 1, 2, 0, 3 },
        { 2, 3, 1, 0 },
        { 3, 0, 2, 1 }
    };

    public TrialState State => state;
    public string Status => status;
    public string ActiveConditionOrder => activeConditionOrder;
    public string ActiveRouteOrder => activeRouteOrder;
    public string ActiveRouteId => activeRouteId;

    void OnEnable()
    {
        ResolveReferences();
        ConfigureExperimentComponents();
        UpdateScheduledAssignmentPreview();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
        {
            AbortTrial("Experiment 1 controller was disabled.");
        }
    }

    void OnValidate()
    {
        trialIndex = Mathf.Clamp(trialIndex, 1, 4);
        preparationTimeoutSeconds = Mathf.Max(0.5f, preparationTimeoutSeconds);
        trackingLossGraceSeconds = Mathf.Max(0.1f, trackingLossGraceSeconds);
        UpdateScheduledAssignmentPreview();
    }

    void Update()
    {
        if (useKeyboardControls && Input.GetKeyDown(prepareAndStartKey))
        {
            PrepareAndStartTrial();
        }

        if (useKeyboardControls && Input.GetKeyDown(abortTrialKey))
        {
            AbortTrial("Experimenter abort key pressed.");
        }

        if (state != TrialState.Running)
        {
            return;
        }

        if (logger != null && !logger.IsLogging)
        {
            if (logger.FinishReached && logger.CompletedDataSaved)
            {
                CompleteTrial();
            }
            else if (logger.FinishReached)
            {
                FailCompletedTrialSave();
            }
            else
            {
                AbortTrial("Logger stopped before the finish was reached.");
            }
            return;
        }

        MonitorTracking();
    }

    [ContextMenu("Prepare And Start Trial")]
    public void PrepareAndStartTrial()
    {
        if (!Application.isPlaying)
        {
            status = "Enter Play Mode before starting a trial.";
            Debug.LogWarning($"[PredictiveFlyExperimentController] {status}", this);
            return;
        }

        if (state == TrialState.Preparing || state == TrialState.Running)
        {
            status = "A trial is already active.";
            return;
        }

        string requestedParticipantId = participantId != null ? participantId.Trim() : string.Empty;
        if (preventAccidentalCompletedTrialRepeat
            && completedTrialKeys.Contains(GetTrialKey(requestedParticipantId, trialIndex)))
        {
            status = $"Trial {trialIndex} already completed for {requestedParticipantId}. Change Trial Index before pressing Enter.";
            Debug.LogWarning($"[PredictiveFlyExperimentController] {status}", this);
            return;
        }

        ResolveReferences();
        ConfigureExperimentComponents();
        if (!TryValidateSetup(out string validationMessage))
        {
            state = TrialState.Error;
            status = validationMessage;
            Debug.LogError($"[PredictiveFlyExperimentController] {validationMessage}", this);
            return;
        }

        if (preparationRoutine != null)
        {
            StopCoroutine(preparationRoutine);
        }
        preparationRoutine = StartCoroutine(PrepareTrialRoutine());
    }

    IEnumerator PrepareTrialRoutine()
    {
        state = TrialState.Preparing;
        status = "Resolving automatic condition and route assignment.";
        trackingLostSince = -1f;

        if (!TryResolveParticipantSequenceNumber(out int sequenceNumber))
        {
            FailPreparation("Participant ID must end with a positive number, for example P001.");
            yield break;
        }

        activeParticipantId = participantId.Trim();
        activeTrialIndex = Mathf.Clamp(trialIndex, 1, 4);
        PredictiveGhostAvatarLocomotion.GhostVisualizationMode condition = ResolveScheduledCondition(sequenceNumber);
        activeConditionCode = ConditionCode(condition).ToString();
        activeConditionOrder = GetConditionOrderString(sequenceNumber);
        activeRouteOrder = GetRouteOrderString(sequenceNumber);

        IrairaBou3DOfficialSceneBuilder.CourseRouteVariant scheduledRoute = ResolveScheduledRoute(sequenceNumber);
        if (sceneBuilder.routeVariant != scheduledRoute)
        {
            status = $"Building scheduled route {RouteCode(scheduledRoute)}.";
            if (locomotion != null)
            {
                locomotion.SetLocomotionInputEnabled(false);
            }

            sceneBuilder.routeVariant = scheduledRoute;
            sceneBuilder.RebuildScene();
            if (locomotion != null)
            {
                locomotion.SetLocomotionInputEnabled(false);
            }
            yield return null;
            ResolveReferences();
            ConfigureExperimentComponents();
            if (!TryValidateSetup(out string rebuiltValidationMessage))
            {
                FailPreparation($"Scheduled route rebuild failed: {rebuiltValidationMessage}");
                yield break;
            }
        }

        sceneBuilder.NormalizeCourseVisualMaterials();

        activeRouteId = sceneBuilder.RouteId;
        status = "Preparing condition, calibration, and delay buffer.";

        locomotion.allowRuntimeVisualizationHotkeys = false;
        locomotion.allowRuntimePredictionHotkey = false;
        locomotion.SetLocomotionInputEnabled(false);
        locomotion.SetVisualizationMode(condition);
        locomotion.RecenterNow();

        float startedAt = Time.unscaledTime;
        while (Time.unscaledTime - startedAt < preparationTimeoutSeconds)
        {
            bool minimumTimeReached = Time.unscaledTime - startedAt >= minimumPreparationSeconds;
            bool delayReady = locomotion.InputToRigDelayBufferReady;
            bool trackingReady = locomotion.IsBodyAnchorAvailable;
            status = $"Preparing: delay={(delayReady ? "ready" : "filling")}, body={(trackingReady ? "tracked" : "missing")}";
            if (minimumTimeReached && delayReady && trackingReady)
            {
                break;
            }
            yield return null;
        }

        if (!locomotion.InputToRigDelayBufferReady || !locomotion.IsBodyAnchorAvailable)
        {
            locomotion.SetLocomotionInputEnabled(false);
            FailPreparation("Preparation timed out: delay buffer or body anchor was not ready.");
            yield break;
        }

        logger.conditionLabelOverride = string.Empty;
        logger.SetTrialMetadata(
            activeParticipantId,
            activeTrialIndex,
            1,
            activeRouteId,
            activeRouteOrder,
            activeConditionOrder);
        logger.StartLogging();
        if (!logger.IsLogging)
        {
            locomotion.SetLocomotionInputEnabled(false);
            FailPreparation("Logger failed to start.");
            yield break;
        }

        locomotion.SetLocomotionInputEnabled(true);
        logger.RecordSystemEvent(
            "trial_input_enabled",
            $"trial_index={activeTrialIndex};condition={condition};condition_order={activeConditionOrder};route={activeRouteId};route_order={activeRouteOrder}");
        state = TrialState.Running;
        status = $"Trial {activeTrialIndex} running: condition {activeConditionCode}, {activeRouteId}.";
        preparationRoutine = null;
    }

    [ContextMenu("Abort Trial")]
    public void AbortTrialFromInspector()
    {
        AbortTrial("Experimenter aborted the trial from the Inspector.");
    }

    public void AbortTrial(string reason)
    {
        if (state == TrialState.Preparing && preparationRoutine != null)
        {
            StopCoroutine(preparationRoutine);
            preparationRoutine = null;
        }

        if (state != TrialState.Running && state != TrialState.Preparing)
        {
            return;
        }

        if (logger != null && logger.IsLogging)
        {
            logger.RecordSystemEvent("trial_aborted", reason);
            logger.StopLogging(false);
        }

        if (locomotion != null)
        {
            locomotion.SetLocomotionInputEnabled(false);
            if (recenterAfterTrial)
            {
                locomotion.RecenterNow();
            }
        }

        state = TrialState.Aborted;
        status = reason;
    }

    void CompleteTrial()
    {
        if (state != TrialState.Running)
        {
            return;
        }

        locomotion.SetLocomotionInputEnabled(false);
        if (recenterAfterTrial)
        {
            locomotion.RecenterNow();
        }
        completedTrialKeys.Add(GetTrialKey(activeParticipantId, activeTrialIndex));
        state = TrialState.Completed;
        status = $"Trial {activeTrialIndex} completed. Set Trial Index to the next value when ready.";
    }

    void FailCompletedTrialSave()
    {
        locomotion.SetLocomotionInputEnabled(false);
        if (recenterAfterTrial)
        {
            locomotion.RecenterNow();
        }

        state = TrialState.Error;
        string detail = string.IsNullOrWhiteSpace(logger.LastSaveError)
            ? "Unknown file output error."
            : logger.LastSaveError;
        status = $"Course completed, but objective data was not saved: {detail}";
        Debug.LogError($"[PredictiveFlyExperimentController] {status}", this);
    }

    void MonitorTracking()
    {
        if (!abortOnBodyAnchorLoss || locomotion == null)
        {
            return;
        }

        if (locomotion.IsBodyAnchorAvailable)
        {
            trackingLostSince = -1f;
            return;
        }

        if (trackingLostSince < 0f)
        {
            trackingLostSince = Time.unscaledTime;
            logger?.RecordSystemEvent("tracking_loss_grace_started", "Body anchor became unavailable.");
            return;
        }

        if (Time.unscaledTime - trackingLostSince >= trackingLossGraceSeconds)
        {
            AbortTrial($"Body anchor tracking was lost for {trackingLossGraceSeconds:0.###} seconds.");
        }
    }

    public bool TryValidateSetup(out string message)
    {
        if (string.IsNullOrWhiteSpace(participantId))
        {
            message = "Participant ID is empty.";
            return false;
        }
        if (!TryResolveParticipantSequenceNumber(out _))
        {
            message = "Participant ID must end with a positive number, for example P001.";
            return false;
        }
        if (trialIndex < 1 || trialIndex > 4)
        {
            message = "Trial Index must be between 1 and 4.";
            return false;
        }
        if (sceneBuilder == null)
        {
            message = "IrairaBou3DOfficialSceneBuilder was not found.";
            return false;
        }
        if (locomotion == null)
        {
            message = "PredictiveGhostAvatarLocomotion was not found.";
            return false;
        }
        if (logger == null)
        {
            message = "PredictiveFlyObjectiveLogger was not found.";
            return false;
        }
        if (!logger.TryValidateSetup(out message))
        {
            return false;
        }

        message = "Ready";
        return true;
    }

    void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (sceneBuilder == null)
        {
            sceneBuilder = GetComponent<IrairaBou3DOfficialSceneBuilder>();
        }
        if (locomotion == null)
        {
            locomotion = FindFirstObjectByType<PredictiveGhostAvatarLocomotion>();
        }
        if (logger == null)
        {
            logger = GetComponent<PredictiveFlyObjectiveLogger>();
        }
        if (logger == null)
        {
            logger = FindFirstObjectByType<PredictiveFlyObjectiveLogger>();
        }
    }

    void ConfigureExperimentComponents()
    {
        if (logger != null)
        {
            logger.useKeyboardControls = false;
            logger.stopOnFinish = true;
            logger.stopOnModeChange = true;
            logger.saveIncompleteTrials = false;
            logger.outputDirectory = string.IsNullOrWhiteSpace(objectiveOutputDirectory)
                ? "Data/PredictiveFlyObjective"
                : objectiveOutputDirectory.Trim();
        }
        if (locomotion != null)
        {
            locomotion.allowRuntimeVisualizationHotkeys = false;
            locomotion.allowRuntimePredictionHotkey = false;
            if (state != TrialState.Running)
            {
                locomotion.SetLocomotionInputEnabled(false);
            }
        }
    }

    bool TryResolveParticipantSequenceNumber(out int sequenceNumber)
    {
        sequenceNumber = 0;

        string value = participantId != null ? participantId.Trim() : string.Empty;
        int start = value.Length;
        while (start > 0 && char.IsDigit(value[start - 1]))
        {
            start--;
        }

        string digits = value.Substring(start);
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out sequenceNumber)
            && sequenceNumber > 0;
    }

    PredictiveGhostAvatarLocomotion.GhostVisualizationMode ResolveScheduledCondition(int sequenceNumber)
    {
        int row = (Mathf.Max(1, sequenceNumber) - 1) % 4;
        int code = BalancedLatinSquare[row, Mathf.Clamp(trialIndex - 1, 0, 3)];
        return ConditionFromCode(code);
    }

    string GetConditionOrderString(int sequenceNumber)
    {
        int row = (Mathf.Max(1, sequenceNumber) - 1) % 4;
        return GetOrderString(row);
    }

    IrairaBou3DOfficialSceneBuilder.CourseRouteVariant ResolveScheduledRoute(int sequenceNumber)
    {
        // Route order rotates every four participants. Each four-person block pairs every condition with every route once.
        int routeRow = ((Mathf.Max(1, sequenceNumber) - 1) / 4) % 4;
        int routeCode = BalancedLatinSquare[routeRow, Mathf.Clamp(trialIndex - 1, 0, 3)];
        return (IrairaBou3DOfficialSceneBuilder.CourseRouteVariant)routeCode;
    }

    string GetRouteOrderString(int sequenceNumber)
    {
        int routeRow = ((Mathf.Max(1, sequenceNumber) - 1) / 4) % 4;
        return GetOrderString(routeRow);
    }

    static string GetOrderString(int row)
    {
        char[] codes = new char[4];
        for (int i = 0; i < codes.Length; i++)
        {
            codes[i] = (char)('A' + BalancedLatinSquare[Mathf.Clamp(row, 0, 3), i]);
        }
        return new string(codes);
    }

    static char RouteCode(IrairaBou3DOfficialSceneBuilder.CourseRouteVariant route)
    {
        return (char)('A' + (int)route);
    }

    static string GetTrialKey(string participant, int index)
    {
        return $"{participant?.Trim()}|{Mathf.Clamp(index, 1, 4)}";
    }

    void UpdateScheduledAssignmentPreview()
    {
        if (!TryResolveParticipantSequenceNumber(out int sequenceNumber))
        {
            scheduledAssignmentPreview = "Invalid participant ID";
            return;
        }

        PredictiveGhostAvatarLocomotion.GhostVisualizationMode condition = ResolveScheduledCondition(sequenceNumber);
        IrairaBou3DOfficialSceneBuilder.CourseRouteVariant route = ResolveScheduledRoute(sequenceNumber);
        scheduledAssignmentPreview =
            $"Trial {trialIndex}: Condition {ConditionCode(condition)} ({condition}), Route_{RouteCode(route)}";
    }

    void FailPreparation(string message)
    {
        if (locomotion != null)
        {
            locomotion.SetLocomotionInputEnabled(false);
        }
        state = TrialState.Error;
        status = message;
        preparationRoutine = null;
        Debug.LogError($"[PredictiveFlyExperimentController] {message}", this);
    }

    static char ConditionCode(PredictiveGhostAvatarLocomotion.GhostVisualizationMode condition)
    {
        switch (condition)
        {
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.RealTimeDroneBody:
                return 'A';
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedDroneBody:
                return 'B';
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithRealTimeGhost:
                return 'C';
            default:
                return 'D';
        }
    }

    static PredictiveGhostAvatarLocomotion.GhostVisualizationMode ConditionFromCode(int code)
    {
        switch (code)
        {
            case 0:
                return PredictiveGhostAvatarLocomotion.GhostVisualizationMode.RealTimeDroneBody;
            case 1:
                return PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedDroneBody;
            case 2:
                return PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithRealTimeGhost;
            default:
                return PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost;
        }
    }
}
