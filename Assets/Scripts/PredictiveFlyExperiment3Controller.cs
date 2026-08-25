using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(650)]
[DisallowMultipleComponent]
public class PredictiveFlyExperiment3Controller : MonoBehaviour
{
    public const string ProtocolVersion = "E3_3x2_personalized_v1";
    public const string RequiredExperiment2ProtocolVersion =
        PredictiveFlyExperiment2CalibrationStore.ProtocolVersion;
    public const int FormalTrialCount = 6;
    public const int DelayBlockCount = 3;
    public const int StrategiesPerDelay = 2;
    public const float Experiment1MinTranslationHorizonSeconds = 0.08f;
    public const float Experiment1MaxTranslationHorizonSeconds = 0.45f;
    public const float Experiment1MinYawHorizonSeconds = 0.05f;
    public const float Experiment1MaxYawHorizonSeconds = 0.2f;

    static readonly int[] DelayLevelsMilliseconds = { 0, 500, 1000 };
    static readonly int[,] DelayPermutations =
    {
        { 0, 1, 2 },
        { 0, 2, 1 },
        { 1, 0, 2 },
        { 1, 2, 0 },
        { 2, 0, 1 },
        { 2, 1, 0 }
    };

    public enum Experiment3State
    {
        Idle,
        PreparingTrial,
        RunningTrial,
        TrialCompleted,
        TrialAborted,
        SessionCompleted,
        Error
    }

    public enum ProxyStrategy
    {
        Current,
        Personalized
    }

    [Serializable]
    public struct TrialAssignment
    {
        public int participantSequence;
        public int trialIndex;
        public int delayMilliseconds;
        public ProxyStrategy strategy;
        public IrairaBou3DOfficialSceneBuilder.CourseRouteVariant route;
        public int canonicalConditionCode;
        public string conditionLabel;
    }

    [Header("Operator Input")]
    [Tooltip("Set once per participant. A matching completed local calibration profile is required.")]
    public string participantId = "P001";
    [Tooltip("Set to 1-6 before each measured trial. Delay, proxy strategy, and route are assigned automatically.")]
    [Range(1, FormalTrialCount)] public int trialIndex = 1;

    [Header("Keyboard Controls")]
    public bool useKeyboardControls = true;
    public KeyCode prepareAndStartTrialKey = KeyCode.Return;
    public KeyCode abortKey = KeyCode.Escape;

    [Header("Trial Lifecycle")]
    [Min(0f)] public float minimumPreparationSeconds = 0.75f;
    [Min(0.5f)] public float preparationTimeoutSeconds = 8f;
    public bool abortOnBodyAnchorLoss = true;
    [Min(0.1f)] public float trackingLossGraceSeconds = 0.75f;
    public bool recenterAfterRun = true;
    public bool preventAccidentalCompletedTrialRepeat = true;

    [Header("Output")]
    [Tooltip("The four objective CSV files are written below this directory.")]
    public string objectiveOutputDirectory =
        "Data/PredictiveFlyExperiment3/Objective";
    [Tooltip("Must point to the completed Experiment 2 calibration profiles.")]
    public string calibrationOutputDirectory =
        PredictiveFlyExperiment2CalibrationStore.DefaultCalibrationOutputDirectory;

    [Header("References")]
    public PredictiveGhostAvatarLocomotion locomotion;
    public PredictiveFlyObjectiveLogger logger;
    public IrairaBou3DOfficialSceneBuilder sceneBuilder;
    public PredictiveFlyExperiment2Controller experiment2Controller;
    public bool autoFindReferences = true;

    [Header("Loaded Calibration (Read Only)")]
    [SerializeField, HideInInspector] bool calibrationReady;
    [SerializeField, HideInInspector] string calibratedParticipantId;
    [SerializeField, HideInInspector] float selectedHorizon0Seconds = -1f;
    [SerializeField, HideInInspector] float selectedHorizon250Seconds = -1f;
    [SerializeField, HideInInspector] float selectedHorizon500Seconds = -1f;
    [SerializeField, HideInInspector] float selectedHorizon750Seconds = -1f;
    [SerializeField, HideInInspector] float selectedHorizon1000Seconds = -1f;
    [SerializeField, HideInInspector] string calibrationProfilePath;
    [SerializeField, HideInInspector] string calibrationCsvPath;
    [SerializeField, HideInInspector] string calibrationCompletedAt;
    [SerializeField, HideInInspector] float loadedMinimumSelectableHorizonSeconds;
    [SerializeField, HideInInspector] float loadedMaximumSelectableHorizonSeconds = 1f;
    [SerializeField, HideInInspector] float loadedHorizonStepSeconds = 0.1f;
    [SerializeField, HideInInspector] float loadedReferenceMinTranslationHorizonSeconds =
        Experiment1MinTranslationHorizonSeconds;
    [SerializeField, HideInInspector] float loadedReferenceMaxTranslationHorizonSeconds =
        Experiment1MaxTranslationHorizonSeconds;
    [SerializeField, HideInInspector] float loadedReferenceMinYawHorizonSeconds =
        Experiment1MinYawHorizonSeconds;
    [SerializeField, HideInInspector] float loadedReferenceMaxYawHorizonSeconds =
        Experiment1MaxYawHorizonSeconds;
    [SerializeField, HideInInspector] PredictiveGhostAvatarLocomotion.PredictionMethod loadedPredictionMethod =
        PredictiveGhostAvatarLocomotion.PredictionMethod.AccelerationJerkLimited;

    [Header("Debug")]
    [SerializeField, HideInInspector] Experiment3State state = Experiment3State.Idle;
    [SerializeField, HideInInspector] string scheduledAssignmentPreview;
    [SerializeField, HideInInspector] string delayOrderPreview;
    [SerializeField, HideInInspector] string status = "Idle";
    [SerializeField, HideInInspector] string activeParticipantId;
    [SerializeField, HideInInspector] int activeTrialIndex;
    [SerializeField, HideInInspector] int activeDelayMilliseconds;
    [SerializeField, HideInInspector] ProxyStrategy activeStrategy;
    [SerializeField, HideInInspector] string activeConditionOrder;
    [SerializeField, HideInInspector] string activeRouteOrder;
    [SerializeField, HideInInspector] string activeRouteId;
    [SerializeField, HideInInspector] float activeMaximumTranslationHorizonSeconds;
    [SerializeField, HideInInspector] string activeQuestionnaireCode;
    [SerializeField, HideInInspector] float trackingLostSince = -1f;

    Coroutine preparationRoutine;
    readonly HashSet<string> completedTrialKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public Experiment3State State => state;
    public string Status => status;
    public string ScheduledAssignmentPreview => scheduledAssignmentPreview;
    public string DelayOrderPreview => delayOrderPreview;
    public string ActiveConditionOrder => activeConditionOrder;
    public string ActiveRouteOrder => activeRouteOrder;
    public string ActiveRouteId => activeRouteId;
    public string ActiveParticipantId => activeParticipantId;
    public int ActiveTrialIndex => activeTrialIndex;
    public int ActiveDelayMilliseconds => activeDelayMilliseconds;
    public ProxyStrategy ActiveStrategy => activeStrategy;
    public float ActiveMaximumTranslationHorizonSeconds =>
        activeMaximumTranslationHorizonSeconds;
    public string ActiveQuestionnaireCode => activeQuestionnaireCode;
    public string ScheduledQuestionnaireCode
    {
        get
        {
            return TryResolveParticipantSequenceNumber(out int sequenceNumber)
                && TryGetTrialAssignment(sequenceNumber, trialIndex, out TrialAssignment assignment)
                    ? GetQuestionnaireModeCode(assignment)
                    : string.Empty;
        }
    }
    public string QuestionnaireDisplayCode =>
        activeTrialIndex > 0 && state != Experiment3State.Idle
            ? activeQuestionnaireCode
            : ScheduledQuestionnaireCode;
    public bool CalibrationReady => calibrationReady;
    public string CalibratedParticipantId => calibratedParticipantId;
    public float SelectedHorizon0Seconds => selectedHorizon0Seconds;
    public float SelectedHorizon250Seconds => selectedHorizon250Seconds;
    public float SelectedHorizon500Seconds => selectedHorizon500Seconds;
    public float SelectedHorizon750Seconds => selectedHorizon750Seconds;
    public float SelectedHorizon1000Seconds => selectedHorizon1000Seconds;
    public string CalibrationProfilePath => calibrationProfilePath;
    public string CalibrationCsvPath => calibrationCsvPath;
    public bool IsFormalRunActive => state == Experiment3State.PreparingTrial
        || state == Experiment3State.RunningTrial;

    void OnEnable()
    {
        ResolveReferences();
        ConfigureExperimentComponents();
        RefreshCalibrationStatusOnEnable();
        UpdateScheduledAssignmentPreview();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
        {
            AbortActiveRun("Experiment 3 controller was disabled.");
        }
    }

    void OnValidate()
    {
        trialIndex = Mathf.Clamp(trialIndex, 1, FormalTrialCount);
        preparationTimeoutSeconds = Mathf.Max(0.5f, preparationTimeoutSeconds);
        trackingLossGraceSeconds = Mathf.Max(0.1f, trackingLossGraceSeconds);
        UpdateScheduledAssignmentPreview();
    }

    void Update()
    {
        if (useKeyboardControls && Input.GetKeyDown(abortKey) && IsFormalRunActive)
        {
            AbortActiveRun("Experimenter abort key pressed.");
            return;
        }
        if (useKeyboardControls && Input.GetKeyDown(prepareAndStartTrialKey))
        {
            PrepareAndStartTrial();
        }
        if (state != Experiment3State.RunningTrial)
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
                locomotion?.SetLocomotionInputEnabled(false);
                state = Experiment3State.TrialAborted;
                status = logger.LastTrialDataSaved
                    ? "Trial ended before the finish. Incomplete objective data were saved."
                    : "Trial ended before the finish, and incomplete objective data could not be saved.";
            }
            return;
        }

        MonitorTracking();
    }

    [ContextMenu("Prepare And Start Experiment 3 Trial")]
    public void PrepareAndStartTrial()
    {
        if (!Application.isPlaying)
        {
            SetWarning("Enter Play Mode before starting a measured trial.");
            return;
        }
        if (IsFormalRunActive)
        {
            SetWarning("A measured Experiment 3 trial is already active.");
            return;
        }

        ResolveReferences();
        ConfigureExperimentComponents();

        string requestedParticipantId = participantId != null ? participantId.Trim() : string.Empty;
        if (preventAccidentalCompletedTrialRepeat
            && completedTrialKeys.Contains(GetTrialKey(requestedParticipantId, trialIndex)))
        {
            SetWarning(
                $"Trial {trialIndex} already completed for {requestedParticipantId}. "
                + "Change Trial Index before pressing Enter.");
            return;
        }

        if (!TryValidateTrialSetup(out string validationMessage))
        {
            SetError(validationMessage);
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
        state = Experiment3State.PreparingTrial;
        status = "Resolving Experiment 3 delay, proxy strategy, and route assignment.";
        trackingLostSince = -1f;

        if (!TryResolveParticipantSequenceNumber(out int sequenceNumber)
            || !TryGetTrialAssignment(sequenceNumber, trialIndex, out TrialAssignment assignment))
        {
            FailPreparation("Participant ID or Trial Index could not be resolved.");
            yield break;
        }

        activeParticipantId = participantId.Trim();
        activeTrialIndex = Mathf.Clamp(trialIndex, 1, FormalTrialCount);
        activeDelayMilliseconds = assignment.delayMilliseconds;
        activeStrategy = assignment.strategy;
        activeQuestionnaireCode = GetQuestionnaireModeCode(assignment);
        activeConditionOrder = GetConditionOrderString(sequenceNumber);
        activeRouteOrder = GetRouteOrderString(sequenceNumber);

        if (sceneBuilder.routeVariant != assignment.route)
        {
            status = $"Building scheduled route {RouteCode(assignment.route)}.";
            locomotion.SetLocomotionInputEnabled(false);
            sceneBuilder.routeVariant = assignment.route;
            sceneBuilder.RebuildScene();
            yield return null;
            ResolveReferences();
            ConfigureExperimentComponents();
            if (!TryValidateStaticConfiguration(out string rebuiltValidationMessage))
            {
                FailPreparation($"Scheduled route rebuild failed: {rebuiltValidationMessage}");
                yield break;
            }
        }

        sceneBuilder.NormalizeCourseVisualMaterials();
        activeRouteId = sceneBuilder.RouteId;
        ApplyTrialAssignment(assignment);

        status = "Preparing condition, body calibration, and delay buffer.";
        if (!IsPreparationReady())
        {
            yield return WaitForPreparationRoutine();
        }
        if (!locomotion.InputToRigDelayBufferReady || !locomotion.IsBodyAnchorAvailable)
        {
            FailPreparation("Trial preparation timed out: delay buffer or body anchor was not ready.");
            yield break;
        }

        string conditionLabel = GetConditionLabel(assignment);
        string configId = BuildExperimentConfigId(assignment);
        logger.conditionLabelOverride = conditionLabel;
        logger.SetTrialMetadata(
            activeParticipantId,
            activeTrialIndex,
            1,
            activeRouteId,
            activeRouteOrder,
            activeConditionOrder,
            configId);
        logger.StartLogging();
        if (!logger.IsLogging)
        {
            locomotion.SetLocomotionInputEnabled(false);
            FailPreparation("Objective logger failed to start.");
            yield break;
        }

        locomotion.SetLocomotionInputEnabled(true);
        logger.RecordSystemEvent(
            "experiment3_trial_input_enabled",
            BuildTrialEventNote(assignment));
        state = Experiment3State.RunningTrial;
        status = $"Trial {activeTrialIndex} running: questionnaire {activeQuestionnaireCode}, "
            + $"{conditionLabel}, {activeRouteId}.";
        preparationRoutine = null;
    }

    void ApplyTrialAssignment(TrialAssignment assignment)
    {
        ConfigureLocomotionForDelay(assignment.delayMilliseconds);
        switch (assignment.strategy)
        {
            case ProxyStrategy.Current:
                ApplyCurrentPredictionProfile();
                ApplyZeroHorizonVisualization(assignment.delayMilliseconds);
                activeMaximumTranslationHorizonSeconds = 0f;
                break;
            default:
                float selected = GetSelectedHorizon(assignment.delayMilliseconds);
                ApplyPersonalizedPredictionProfile(selected);
                if (selected <= 1e-5f)
                {
                    ApplyZeroHorizonVisualization(assignment.delayMilliseconds);
                }
                else
                {
                    locomotion.SetStateGhostAppearanceSuppressed(false);
                    locomotion.SetVisualizationMode(
                        PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost);
                }
                activeMaximumTranslationHorizonSeconds = selected;
                break;
        }

        locomotion.SetLocomotionInputEnabled(false);
        locomotion.RecenterNow();
    }

    void ConfigureLocomotionForDelay(int delayMilliseconds)
    {
        locomotion.allowRuntimeVisualizationHotkeys = false;
        locomotion.allowRuntimePredictionHotkey = false;
        locomotion.enableInputToRigDelay = true;
        locomotion.inputToRigDelayMilliseconds = Mathf.Max(0, delayMilliseconds);
        locomotion.useCollisionConsistentStateDelay = true;
        locomotion.SetPredictionMethod(loadedPredictionMethod);
    }

    void ApplyCurrentPredictionProfile()
    {
        if (locomotion == null)
        {
            return;
        }
        locomotion.minTranslationPredictionWindow = 0f;
        locomotion.maxTranslationPredictionWindow = 0f;
        locomotion.minYawPredictionWindow = 0f;
        locomotion.maxYawPredictionWindow = 0f;
    }

    void ApplyZeroHorizonVisualization(int delayMilliseconds)
    {
        locomotion.SetStateGhostAppearanceSuppressed(false);
        locomotion.SetVisualizationMode(
            delayMilliseconds == 0
                ? PredictiveGhostAvatarLocomotion.GhostVisualizationMode.RealTimeDroneBody
                : PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithRealTimeGhost);
    }

    void ApplyPersonalizedPredictionProfile(float selectedMaxTranslationHorizon)
    {
        if (locomotion == null)
        {
            return;
        }
        float selected = Mathf.Clamp(
            selectedMaxTranslationHorizon,
            loadedMinimumSelectableHorizonSeconds,
            loadedMaximumSelectableHorizonSeconds);
        float scale = loadedReferenceMaxTranslationHorizonSeconds > 1e-5f
            ? selected / loadedReferenceMaxTranslationHorizonSeconds
            : 0f;
        locomotion.minTranslationPredictionWindow =
            loadedReferenceMinTranslationHorizonSeconds * scale;
        locomotion.maxTranslationPredictionWindow = selected;
        locomotion.minYawPredictionWindow = loadedReferenceMinYawHorizonSeconds * scale;
        locomotion.maxYawPredictionWindow = loadedReferenceMaxYawHorizonSeconds * scale;
    }

    IEnumerator WaitForPreparationRoutine()
    {
        float startedAt = Time.unscaledTime;
        while (Time.unscaledTime - startedAt < preparationTimeoutSeconds)
        {
            bool minimumTimeReached = Time.unscaledTime - startedAt >= minimumPreparationSeconds;
            bool delayReady = locomotion != null && locomotion.InputToRigDelayBufferReady;
            bool trackingReady = locomotion != null && locomotion.IsBodyAnchorAvailable;
            status = $"Preparing: delay={(delayReady ? "ready" : "filling")}, "
                + $"body={(trackingReady ? "tracked" : "missing")}";
            if (minimumTimeReached && delayReady && trackingReady)
            {
                yield break;
            }
            yield return null;
        }
    }

    bool IsPreparationReady()
    {
        return minimumPreparationSeconds <= 0f
            && locomotion != null
            && locomotion.InputToRigDelayBufferReady
            && locomotion.IsBodyAnchorAvailable;
    }

    [ContextMenu("Abort Experiment 3 Trial")]
    public void AbortActiveRunFromInspector()
    {
        AbortActiveRun("Experimenter aborted the formal trial from the Inspector.");
    }

    public void AbortActiveRun(string reason)
    {
        if (!IsFormalRunActive)
        {
            return;
        }
        if (preparationRoutine != null)
        {
            StopCoroutine(preparationRoutine);
            preparationRoutine = null;
        }

        bool wasLogging = logger != null && logger.IsLogging;
        if (wasLogging)
        {
            logger.RecordSystemEvent("experiment3_trial_aborted", reason);
            logger.StopLogging(false);
        }
        locomotion?.SetLocomotionInputEnabled(false);
        ApplyCurrentPredictionProfile();
        locomotion?.SetStateGhostAppearanceSuppressed(false);
        if (recenterAfterRun)
        {
            locomotion?.RecenterNow();
        }

        state = Experiment3State.TrialAborted;
        status = wasLogging && logger.LastTrialDataSaved
            ? $"{reason} Incomplete objective data were saved."
            : reason;
    }

    void CompleteTrial()
    {
        if (state != Experiment3State.RunningTrial)
        {
            return;
        }
        locomotion.SetLocomotionInputEnabled(false);
        ApplyCurrentPredictionProfile();
        locomotion.SetStateGhostAppearanceSuppressed(false);
        if (recenterAfterRun)
        {
            locomotion.RecenterNow();
        }

        completedTrialKeys.Add(GetTrialKey(activeParticipantId, activeTrialIndex));
        int completedForParticipant = 0;
        for (int i = 1; i <= FormalTrialCount; i++)
        {
            if (completedTrialKeys.Contains(GetTrialKey(activeParticipantId, i)))
            {
                completedForParticipant++;
            }
        }
        state = completedForParticipant == FormalTrialCount
            ? Experiment3State.SessionCompleted
            : Experiment3State.TrialCompleted;
        status = state == Experiment3State.SessionCompleted
            ? $"All six measured Experiment 3 trials completed. Questionnaire code for "
                + $"Trial {activeTrialIndex}: {activeQuestionnaireCode}."
            : $"Trial {activeTrialIndex} completed. Questionnaire code: "
                + $"{activeQuestionnaireCode}. Set Trial Index to the next value when ready.";
    }

    void FailCompletedTrialSave()
    {
        locomotion.SetLocomotionInputEnabled(false);
        if (recenterAfterRun)
        {
            locomotion.RecenterNow();
        }
        state = Experiment3State.Error;
        string detail = string.IsNullOrWhiteSpace(logger.LastSaveError)
            ? "Unknown file output error."
            : logger.LastSaveError;
        status = $"Course completed, but objective data were not saved: {detail}";
        Debug.LogError($"[PredictiveFlyExperiment3Controller] {status}", this);
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
            logger?.RecordSystemEvent(
                "tracking_loss_grace_started",
                "Body anchor became unavailable.");
            return;
        }
        if (Time.unscaledTime - trackingLostSince >= trackingLossGraceSeconds)
        {
            AbortActiveRun(
                $"Body anchor tracking was lost for {trackingLossGraceSeconds:0.###} seconds.");
        }
    }

    public bool TryValidateTrialSetup(out string message)
    {
        if (!TryValidateStaticConfiguration(out message))
        {
            return false;
        }
        if (trialIndex < 1 || trialIndex > FormalTrialCount)
        {
            message = $"Trial Index must be between 1 and {FormalTrialCount}.";
            return false;
        }
        if (!TryResolveParticipantSequenceNumber(out _))
        {
            message = "Participant ID must end with a positive number, for example P001.";
            return false;
        }
        if (!TryRefreshCalibrationFromDisk(out message))
        {
            message += " Complete calibration first; the formal trial was not started.";
            return false;
        }

        logger.participantId = participantId.Trim();
        if (!logger.TryValidateSetup(out message))
        {
            return false;
        }
        message = "Ready";
        return true;
    }

    public bool TryValidateStaticConfiguration(out string message)
    {
        ResolveReferences();
        if (sceneBuilder == null || locomotion == null || logger == null)
        {
            message = "Scene builder, locomotion, or objective logger reference is missing.";
            return false;
        }
        if (experiment2Controller == null)
        {
            message = "The Experiment 2 preference controller reference is missing.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(participantId))
        {
            message = "Participant ID is empty.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(objectiveOutputDirectory)
            || string.IsNullOrWhiteSpace(calibrationOutputDirectory))
        {
            message = "Experiment 3 objective and Experiment 2 calibration directories must not be empty.";
            return false;
        }
        string formalCalibrationDirectory =
            PredictiveFlyExperiment2CalibrationStore.ResolveDirectory(calibrationOutputDirectory);
        string calibrationComponentDirectory =
            PredictiveFlyExperiment2CalibrationStore.ResolveDirectory(
                experiment2Controller.calibrationOutputDirectory);
        if (!string.Equals(
                formalCalibrationDirectory.TrimEnd(Path.DirectorySeparatorChar),
                calibrationComponentDirectory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            message = "Formal and calibration controllers point to different calibration directories.";
            return false;
        }

        message = "Ready";
        return true;
    }

    [ContextMenu("Refresh Completed Calibration From Disk")]
    public void RefreshCalibrationFromInspector()
    {
        if (TryRefreshCalibrationFromDisk(out string message))
        {
            if (state == Experiment3State.Error)
            {
                state = Experiment3State.Idle;
            }
            status = message;
            Debug.Log($"[PredictiveFlyExperiment3Controller] {message}", this);
        }
        else
        {
            SetWarning(message);
        }
    }

    public bool TryRefreshCalibrationFromDisk(out string message)
    {
        string requestedParticipant = participantId != null ? participantId.Trim() : string.Empty;
        if (!PredictiveFlyExperiment2CalibrationStore.TryLoadCompletedProfile(
                requestedParticipant,
                calibrationOutputDirectory,
                out PredictiveFlyExperiment2CalibrationStore.CompletedCalibrationProfile profile,
                out calibrationProfilePath,
                out message))
        {
            ClearLoadedCalibration();
            UpdateScheduledAssignmentPreview();
            return false;
        }
        if (!TryValidateLoadedProfile(profile, out message))
        {
            ClearLoadedCalibration();
            UpdateScheduledAssignmentPreview();
            return false;
        }

        calibrationReady = true;
        calibratedParticipantId = profile.participantId.Trim();
        selectedHorizon0Seconds = profile.selectedHorizon0Seconds;
        selectedHorizon250Seconds = profile.selectedHorizon250Seconds;
        selectedHorizon500Seconds = profile.selectedHorizon500Seconds;
        selectedHorizon750Seconds = profile.selectedHorizon750Seconds;
        selectedHorizon1000Seconds = profile.selectedHorizon1000Seconds;
        calibrationCompletedAt = profile.completedAt;
        loadedMinimumSelectableHorizonSeconds = profile.minimumSelectableHorizonSeconds;
        loadedMaximumSelectableHorizonSeconds = profile.maximumSelectableHorizonSeconds;
        loadedHorizonStepSeconds = profile.horizonStepSeconds;
        loadedReferenceMinTranslationHorizonSeconds =
            profile.referenceMinTranslationHorizonSeconds;
        loadedReferenceMaxTranslationHorizonSeconds =
            profile.referenceMaxTranslationHorizonSeconds;
        loadedReferenceMinYawHorizonSeconds = profile.referenceMinYawHorizonSeconds;
        loadedReferenceMaxYawHorizonSeconds = profile.referenceMaxYawHorizonSeconds;
        loadedPredictionMethod =
            (PredictiveGhostAvatarLocomotion.PredictionMethod)profile.predictionMethod;
        calibrationCsvPath = Path.Combine(
            PredictiveFlyExperiment2CalibrationStore.ResolveDirectory(calibrationOutputDirectory),
            profile.calibrationCsvFileName);
        UpdateScheduledAssignmentPreview();
        message = $"Calibration ready for {calibratedParticipantId}: "
            + $"0 ms={selectedHorizon0Seconds:0.0} s, "
            + $"250 ms={selectedHorizon250Seconds:0.0} s, "
            + $"500 ms={selectedHorizon500Seconds:0.0} s, "
            + $"750 ms={selectedHorizon750Seconds:0.0} s, "
            + $"1000 ms={selectedHorizon1000Seconds:0.0} s.";
        return true;
    }

    bool TryValidateLoadedProfile(
        PredictiveFlyExperiment2CalibrationStore.CompletedCalibrationProfile profile,
        out string message)
    {
        if (!Enum.IsDefined(
                typeof(PredictiveGhostAvatarLocomotion.PredictionMethod),
                profile.predictionMethod))
        {
            message = $"Calibration incomplete for {participantId}: prediction method is invalid.";
            return false;
        }
        if (!Mathf.Approximately(
                profile.referenceMinTranslationHorizonSeconds,
                Experiment1MinTranslationHorizonSeconds)
            || !Mathf.Approximately(
                profile.referenceMaxTranslationHorizonSeconds,
                Experiment1MaxTranslationHorizonSeconds)
            || !Mathf.Approximately(
                profile.referenceMinYawHorizonSeconds,
                Experiment1MinYawHorizonSeconds)
            || !Mathf.Approximately(
                profile.referenceMaxYawHorizonSeconds,
                Experiment1MaxYawHorizonSeconds)
            || (PredictiveGhostAvatarLocomotion.PredictionMethod)profile.predictionMethod
                != PredictiveGhostAvatarLocomotion.PredictionMethod.AccelerationJerkLimited)
        {
            message = $"Calibration incomplete for {participantId}: profile shape does not match "
                + "the locked Experiment 2 calibration configuration.";
            return false;
        }
        message = "Calibration profile matches the formal protocol.";
        return true;
    }

    void RefreshCalibrationStatusOnEnable()
    {
        if (string.IsNullOrWhiteSpace(participantId))
        {
            ClearLoadedCalibration();
            return;
        }
        if (TryRefreshCalibrationFromDisk(out string message))
        {
            status = message;
        }
        else if (state == Experiment3State.Idle)
        {
            status = message;
        }
    }

    void ClearLoadedCalibration()
    {
        calibrationReady = false;
        calibratedParticipantId = string.Empty;
        selectedHorizon0Seconds = -1f;
        selectedHorizon250Seconds = -1f;
        selectedHorizon500Seconds = -1f;
        selectedHorizon750Seconds = -1f;
        selectedHorizon1000Seconds = -1f;
        calibrationCsvPath = string.Empty;
        calibrationCompletedAt = string.Empty;
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
        if (sceneBuilder == null)
        {
            sceneBuilder = FindFirstObjectByType<IrairaBou3DOfficialSceneBuilder>();
        }
        if (logger == null)
        {
            logger = sceneBuilder != null
                ? sceneBuilder.GetComponent<PredictiveFlyObjectiveLogger>()
                : FindFirstObjectByType<PredictiveFlyObjectiveLogger>();
        }
        if (experiment2Controller == null)
        {
            experiment2Controller = GetComponent<PredictiveFlyExperiment2Controller>();
        }
        if (locomotion == null || !locomotion.gameObject.scene.IsValid())
        {
            locomotion = FindFirstObjectByType<PredictiveGhostAvatarLocomotion>();
        }
    }

    void ConfigureExperimentComponents()
    {
        if (logger != null)
        {
            logger.enabled = true;
            logger.useKeyboardControls = false;
            logger.stopOnFinish = true;
            logger.stopOnModeChange = true;
            logger.saveIncompleteTrials = false;
            logger.incompleteTrialSubdirectory = "Incomplete";
            logger.outputDirectory = objectiveOutputDirectory;
            logger.participantId = string.IsNullOrWhiteSpace(participantId)
                ? "P001"
                : participantId.Trim();
        }
        if (locomotion != null)
        {
            locomotion.allowRuntimeVisualizationHotkeys = false;
            locomotion.allowRuntimePredictionHotkey = false;
            if (!IsFormalRunActive)
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
        return int.TryParse(
                digits,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out sequenceNumber)
            && sequenceNumber > 0;
    }

    public static bool TryGetTrialAssignment(
        int participantSequence,
        int requestedTrialIndex,
        out TrialAssignment assignment)
    {
        assignment = default;
        if (participantSequence < 1
            || requestedTrialIndex < 1
            || requestedTrialIndex > FormalTrialCount)
        {
            return false;
        }

        int block = (requestedTrialIndex - 1) / StrategiesPerDelay;
        int positionWithinBlock = (requestedTrialIndex - 1) % StrategiesPerDelay;
        int delay = GetDelayForOrderPosition(participantSequence, block);
        int scheduleRow = (Mathf.Max(1, participantSequence) - 1) % 6;
        int scheduleCycle = (Mathf.Max(1, participantSequence) - 1) / 6;
        bool currentFirst = ((scheduleRow + scheduleCycle + block) & 1) == 0;
        ProxyStrategy firstStrategy = currentFirst
            ? ProxyStrategy.Current
            : ProxyStrategy.Personalized;
        ProxyStrategy strategy = positionWithinBlock == 0
            ? firstStrategy
            : firstStrategy == ProxyStrategy.Current
                ? ProxyStrategy.Personalized
                : ProxyStrategy.Current;
        int delayIndex = GetDelayLevelIndex(delay);
        int canonicalConditionCode = delayIndex * StrategiesPerDelay + (int)strategy;
        int routeCode = (participantSequence - 1 + canonicalConditionCode) % 4;
        assignment = new TrialAssignment
        {
            participantSequence = participantSequence,
            trialIndex = requestedTrialIndex,
            delayMilliseconds = delay,
            strategy = strategy,
            route = (IrairaBou3DOfficialSceneBuilder.CourseRouteVariant)routeCode,
            canonicalConditionCode = canonicalConditionCode,
            conditionLabel = $"D{delay}_{strategy}"
        };
        return true;
    }

    public static int GetFirstDelayMilliseconds(int participantSequence)
    {
        return GetDelayForOrderPosition(participantSequence, 0);
    }

    public static int GetDelayForOrderPosition(int participantSequence, int delayOrderIndex)
    {
        int row = (Mathf.Max(1, participantSequence) - 1) % 6;
        int position = Mathf.Clamp(delayOrderIndex, 0, DelayBlockCount - 1);
        int delayIndex = DelayPermutations[row, position];
        return DelayLevelsMilliseconds[delayIndex];
    }

    public static int GetDelayLevelMilliseconds(int delayIndex)
    {
        return DelayLevelsMilliseconds[Mathf.Clamp(delayIndex, 0, DelayBlockCount - 1)];
    }

    public static int GetDelayLevelIndex(int delayMilliseconds)
    {
        for (int i = 0; i < DelayLevelsMilliseconds.Length; i++)
        {
            if (DelayLevelsMilliseconds[i] == delayMilliseconds)
            {
                return i;
            }
        }
        return -1;
    }

    string GetConditionOrderString(int sequenceNumber)
    {
        StringBuilder builder = new StringBuilder(96);
        for (int i = 1; i <= FormalTrialCount; i++)
        {
            TryGetTrialAssignment(sequenceNumber, i, out TrialAssignment assignment);
            if (i > 1)
            {
                builder.Append('>');
            }
            builder.Append(assignment.conditionLabel);
        }
        return builder.ToString();
    }

    string GetRouteOrderString(int sequenceNumber)
    {
        char[] routes = new char[FormalTrialCount];
        for (int i = 1; i <= FormalTrialCount; i++)
        {
            TryGetTrialAssignment(sequenceNumber, i, out TrialAssignment assignment);
            routes[i - 1] = RouteCode(assignment.route);
        }
        return new string(routes);
    }

    string GetDelayOrderString(int sequenceNumber)
    {
        StringBuilder builder = new StringBuilder(20);
        for (int i = 0; i < DelayBlockCount; i++)
        {
            if (i > 0)
            {
                builder.Append('>');
            }
            builder.Append(GetDelayForOrderPosition(sequenceNumber, i));
        }
        return builder.ToString();
    }

    static char RouteCode(IrairaBou3DOfficialSceneBuilder.CourseRouteVariant route)
    {
        return (char)('A' + (int)route);
    }

    static string GetTrialKey(string participant, int index)
    {
        return $"{participant?.Trim()}|{Mathf.Clamp(index, 1, FormalTrialCount)}";
    }

    string GetConditionLabel(TrialAssignment assignment)
    {
        return assignment.strategy == ProxyStrategy.Current
            ? $"E3_D{assignment.delayMilliseconds}_Current"
            : $"E3_D{assignment.delayMilliseconds}_Personalized_H"
                + HorizonToken(GetSelectedHorizon(assignment.delayMilliseconds));
    }

    public static string GetQuestionnaireModeCode(TrialAssignment assignment)
    {
        return GetQuestionnaireModeCode(
            assignment.delayMilliseconds,
            assignment.strategy);
    }

    public static string GetQuestionnaireModeCode(
        int delayMilliseconds,
        ProxyStrategy strategy)
    {
        string delayCode;
        switch (delayMilliseconds)
        {
            case 0:
                delayCode = "A";
                break;
            case 500:
                delayCode = "B";
                break;
            case 1000:
                delayCode = "C";
                break;
            default:
                return string.Empty;
        }

        // H=0 remains the assigned Personalized policy condition and keeps the _2 code.
        return $"{delayCode}_{(strategy == ProxyStrategy.Current ? 1 : 2)}";
    }

    string BuildExperimentConfigId(TrialAssignment assignment)
    {
        float selected = assignment.strategy == ProxyStrategy.Personalized
            ? GetSelectedHorizon(assignment.delayMilliseconds)
            : 0f;
        string predictionDistanceCap = locomotion != null && locomotion.maxPredictionDistance > 0f
            ? locomotion.maxPredictionDistance.ToString("0.000", CultureInfo.InvariantCulture)
            : "none";
        return string.Join(
            "|",
            "Experiment3",
            $"protocol={ProtocolVersion}",
            $"experiment2_profile_protocol={RequiredExperiment2ProtocolVersion}",
            "design=3x2_delay_by_current_personalized",
            $"questionnaire_mode_code={GetQuestionnaireModeCode(assignment)}",
            $"delay_ms={assignment.delayMilliseconds}",
            $"strategy={assignment.strategy}",
            $"selected_max_translation_horizon_s={selected.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"prediction_distance_cap_m={predictionDistanceCap}",
            $"personalized_reference_translation_profile_s={loadedReferenceMinTranslationHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)}-{loadedReferenceMaxTranslationHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"personalized_reference_yaw_profile_s={loadedReferenceMinYawHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)}-{loadedReferenceMaxYawHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"calibrated_h0_s={selectedHorizon0Seconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"calibrated_h250_s={selectedHorizon250Seconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"calibrated_h500_s={selectedHorizon500Seconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"calibrated_h750_s={selectedHorizon750Seconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"calibrated_h1000_s={selectedHorizon1000Seconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"calibration_profile={Path.GetFileName(calibrationProfilePath)}",
            $"calibration_csv={Path.GetFileName(calibrationCsvPath)}");
    }

    string BuildTrialEventNote(TrialAssignment assignment)
    {
        return $"trial_index={activeTrialIndex};questionnaire_mode_code="
            + $"{GetQuestionnaireModeCode(assignment)};delay_ms={assignment.delayMilliseconds};"
            + $"strategy={assignment.strategy};"
            + $"max_translation_horizon_s={activeMaximumTranslationHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)};"
            + $"condition_order={activeConditionOrder};route={activeRouteId};route_order={activeRouteOrder};"
            + $"h0={selectedHorizon0Seconds.ToString("0.000", CultureInfo.InvariantCulture)};"
            + $"h250={selectedHorizon250Seconds.ToString("0.000", CultureInfo.InvariantCulture)};"
            + $"h500={selectedHorizon500Seconds.ToString("0.000", CultureInfo.InvariantCulture)};"
            + $"h750={selectedHorizon750Seconds.ToString("0.000", CultureInfo.InvariantCulture)};"
            + $"h1000={selectedHorizon1000Seconds.ToString("0.000", CultureInfo.InvariantCulture)};"
            + $"calibration_profile={Path.GetFileName(calibrationProfilePath)};"
            + $"calibration_csv={Path.GetFileName(calibrationCsvPath)}";
    }

    static string HorizonToken(float horizon)
    {
        return Mathf.Max(0f, horizon)
            .ToString("0.000", CultureInfo.InvariantCulture)
            .Replace('.', 'p');
    }

    float GetSelectedHorizon(int delayMilliseconds)
    {
        switch (delayMilliseconds)
        {
            case 0:
                return selectedHorizon0Seconds;
            case 500:
                return selectedHorizon500Seconds;
            case 1000:
                return selectedHorizon1000Seconds;
            default:
                return -1f;
        }
    }

    void UpdateScheduledAssignmentPreview()
    {
        if (!TryResolveParticipantSequenceNumber(out int sequenceNumber)
            || !TryGetTrialAssignment(sequenceNumber, trialIndex, out TrialAssignment assignment))
        {
            scheduledAssignmentPreview = "Invalid participant ID or trial index";
            delayOrderPreview = "Invalid participant ID";
            return;
        }
        float horizon = assignment.strategy == ProxyStrategy.Current
            ? 0f
            : GetSelectedHorizon(assignment.delayMilliseconds);
        string horizonText = calibrationReady
            ? $"Hmax={Mathf.Max(0f, horizon):0.0} s"
            : "completed local calibration required";
        scheduledAssignmentPreview =
            $"Trial {trialIndex} [{GetQuestionnaireModeCode(assignment)}]: "
            + $"{assignment.conditionLabel}, Route_{RouteCode(assignment.route)}, "
            + horizonText;
        delayOrderPreview = GetDelayOrderString(sequenceNumber);
    }

    void FailPreparation(string message)
    {
        locomotion?.SetLocomotionInputEnabled(false);
        state = Experiment3State.Error;
        status = message;
        preparationRoutine = null;
        Debug.LogError($"[PredictiveFlyExperiment3Controller] {message}", this);
    }

    void SetWarning(string message)
    {
        status = message;
        Debug.LogWarning($"[PredictiveFlyExperiment3Controller] {message}", this);
    }

    void SetError(string message)
    {
        locomotion?.SetLocomotionInputEnabled(false);
        state = Experiment3State.Error;
        status = message;
        Debug.LogError($"[PredictiveFlyExperiment3Controller] {message}", this);
    }
}
