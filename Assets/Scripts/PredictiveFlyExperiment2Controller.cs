using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(650)]
[DisallowMultipleComponent]
public class PredictiveFlyExperiment2Controller : MonoBehaviour
{
    public const float Experiment1MinTranslationHorizonSeconds = 0.08f;
    public const float Experiment1MaxTranslationHorizonSeconds = 0.45f;
    public const float Experiment1MinYawHorizonSeconds = 0.05f;
    public const float Experiment1MaxYawHorizonSeconds = 0.2f;

    public enum Experiment2State
    {
        Idle,
        PreparingCalibration,
        Calibrating,
        CalibrationComplete,
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
        Fixed,
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
    [Tooltip("Set once per participant. The trailing positive number selects all counterbalancing schedules.")]
    public string participantId = "E2P001";
    [Tooltip("Set to 1-6 before each measured trial. Delay, proxy strategy, and route are assigned automatically.")]
    [Range(1, 6)] public int trialIndex = 1;
    [Tooltip("Participant ID for which the two visible personalized horizons were calibrated. Set this with the horizons only when recovering a previous session.")]
    public string calibratedParticipantId;

    [Header("Keyboard Controls")]
    public bool useKeyboardControls = true;
    public KeyCode beginCalibrationKey = KeyCode.F7;
    public KeyCode decreaseHorizonKey = KeyCode.LeftBracket;
    public KeyCode increaseHorizonKey = KeyCode.RightBracket;
    public KeyCode acceptCalibrationKey = KeyCode.Space;
    public KeyCode prepareAndStartTrialKey = KeyCode.Return;
    public KeyCode abortKey = KeyCode.Escape;

    [Header("Calibration")]
    [Tooltip("Maximum translation look-ahead available during participant adjustment. Yaw windows scale proportionally.")]
    [Min(0f)] public float minimumSelectableHorizonSeconds;
    [Min(0.05f)] public float maximumSelectableHorizonSeconds = 1f;
    [Min(0.01f)] public float horizonStepSeconds = 0.1f;
    [Min(0f)] public float minimumCalibrationRunSeconds = 20f;
    [Tooltip("After low- and high-anchor runs, add a third mid-anchor run when their accepted values differ by more than this amount.")]
    [Min(0f)] public float thirdRunDifferenceThresholdSeconds = 0.2f;
    public IrairaBou3DOfficialSceneBuilder.CourseRouteVariant calibrationRoute =
        IrairaBou3DOfficialSceneBuilder.CourseRouteVariant.Base;
    [Tooltip("Both delays must be calibrated before any measured trial can begin.")]
    public bool requireBothCalibrationsBeforeTrials = true;
    [Tooltip("Locked participant choice for 500 ms. -1 means not calibrated.")]
    public float selectedHorizon500Seconds = -1f;
    [Tooltip("Locked participant choice for 1000 ms. -1 means not calibrated.")]
    public float selectedHorizon1000Seconds = -1f;

    [Header("Fixed Predictor Profile")]
    [Tooltip("Experiment 1 fixed profile. Both delay levels reuse these exact values; the prediction horizon is additional look-ahead beyond the Current proxy.")]
    [Min(0f)] public float fixedMinTranslationHorizonSeconds = Experiment1MinTranslationHorizonSeconds;
    [Min(0f)] public float fixedMaxTranslationHorizonSeconds = Experiment1MaxTranslationHorizonSeconds;
    [Min(0f)] public float fixedMinYawHorizonSeconds = Experiment1MinYawHorizonSeconds;
    [Min(0f)] public float fixedMaxYawHorizonSeconds = Experiment1MaxYawHorizonSeconds;
    public PredictiveGhostAvatarLocomotion.PredictionMethod fixedPredictionMethod =
        PredictiveGhostAvatarLocomotion.PredictionMethod.AccelerationJerkLimited;

    [Header("Trial Lifecycle")]
    [Min(0f)] public float minimumPreparationSeconds = 0.75f;
    [Min(0.5f)] public float preparationTimeoutSeconds = 8f;
    public bool abortOnBodyAnchorLoss = true;
    [Min(0.1f)] public float trackingLossGraceSeconds = 0.75f;
    public bool recenterAfterRun = true;
    public bool preventAccidentalCompletedTrialRepeat = true;

    [Header("Output")]
    [Tooltip("The existing four objective CSV files are written below this directory.")]
    public string objectiveOutputDirectory = "Data/PredictiveFlyExperiment2/Objective";
    [Tooltip("Participant horizon-adjustment events are written here as a separate calibration CSV.")]
    public string calibrationOutputDirectory = "Data/PredictiveFlyExperiment2/Calibration";

    [Header("References")]
    public PredictiveGhostAvatarLocomotion locomotion;
    public PredictiveFlyObjectiveLogger logger;
    public IrairaBou3DOfficialSceneBuilder sceneBuilder;
    public bool autoFindReferences = true;

    [Header("Debug")]
    [SerializeField] Experiment2State state = Experiment2State.Idle;
    [SerializeField] string scheduledAssignmentPreview;
    [SerializeField] string delayOrderPreview;
    [SerializeField] string status = "Idle";
    [SerializeField] string activeParticipantId;
    [SerializeField] int activeTrialIndex;
    [SerializeField] int activeDelayMilliseconds;
    [SerializeField] ProxyStrategy activeStrategy;
    [SerializeField] string activeConditionOrder;
    [SerializeField] string activeRouteOrder;
    [SerializeField] string activeRouteId;
    [SerializeField] float activeMaximumTranslationHorizonSeconds;
    [SerializeField] int activeCalibrationDelayMilliseconds;
    [SerializeField] int activeCalibrationRunNumber;
    [SerializeField] string activeCalibrationAnchor;
    [SerializeField] float currentCalibrationHorizonSeconds;
    [SerializeField] string calibrationCsvPath;
    [SerializeField] string lastCalibrationSaveError;
    [SerializeField] float trackingLostSince = -1f;

    Coroutine preparationRoutine;
    float calibrationRunStartedAt;
    int calibrationDelayOrderIndex;
    int calibrationRunIndexForDelay;
    int calibrationParticipantSequence;
    string calibrationTimestamp;

    readonly List<float> calibrationSelections500 = new List<float>(3);
    readonly List<float> calibrationSelections1000 = new List<float>(3);
    readonly List<CalibrationEventRecord> calibrationEvents = new List<CalibrationEventRecord>(64);
    readonly HashSet<string> completedTrialKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static readonly int[,] StrategyPermutations =
    {
        { 0, 1, 2 },
        { 0, 2, 1 },
        { 1, 0, 2 },
        { 1, 2, 0 },
        { 2, 0, 1 },
        { 2, 1, 0 }
    };

    public Experiment2State State => state;
    public string Status => status;
    public string ScheduledAssignmentPreview => scheduledAssignmentPreview;
    public string ActiveConditionOrder => activeConditionOrder;
    public string ActiveRouteOrder => activeRouteOrder;
    public string ActiveRouteId => activeRouteId;
    public float CurrentCalibrationHorizonSeconds => currentCalibrationHorizonSeconds;

    void Awake()
    {
        ResolveReferences();
        ConfigureExperimentComponents();
        UpdateScheduledAssignmentPreview();
    }

    void OnEnable()
    {
        ResolveReferences();
        ConfigureExperimentComponents();
        UpdateScheduledAssignmentPreview();
    }

    void OnValidate()
    {
        trialIndex = Mathf.Clamp(trialIndex, 1, 6);
        maximumSelectableHorizonSeconds = Mathf.Max(
            minimumSelectableHorizonSeconds + 0.05f,
            maximumSelectableHorizonSeconds);
        horizonStepSeconds = Mathf.Max(0.01f, horizonStepSeconds);
        preparationTimeoutSeconds = Mathf.Max(0.5f, preparationTimeoutSeconds);
        trackingLossGraceSeconds = Mathf.Max(0.1f, trackingLossGraceSeconds);
        fixedMaxTranslationHorizonSeconds = Mathf.Max(
            fixedMinTranslationHorizonSeconds,
            fixedMaxTranslationHorizonSeconds);
        fixedMaxYawHorizonSeconds = Mathf.Max(fixedMinYawHorizonSeconds, fixedMaxYawHorizonSeconds);
        selectedHorizon500Seconds = ClampOptionalHorizon(selectedHorizon500Seconds);
        selectedHorizon1000Seconds = ClampOptionalHorizon(selectedHorizon1000Seconds);
        UpdateScheduledAssignmentPreview();
    }

    void Update()
    {
        EnsureExperiment1ControllerDisabled();

        if (useKeyboardControls && Input.GetKeyDown(abortKey))
        {
            AbortActiveRun("Experimenter abort key pressed.");
            return;
        }

        if (state == Experiment2State.Calibrating)
        {
            if (useKeyboardControls && Input.GetKeyDown(decreaseHorizonKey))
            {
                AdjustCalibrationHorizon(-1);
            }
            if (useKeyboardControls && Input.GetKeyDown(increaseHorizonKey))
            {
                AdjustCalibrationHorizon(1);
            }
            if (useKeyboardControls && Input.GetKeyDown(acceptCalibrationKey))
            {
                AcceptCalibrationRun();
            }
            return;
        }

        if (useKeyboardControls && Input.GetKeyDown(beginCalibrationKey))
        {
            BeginCalibrationSequence();
        }

        if (useKeyboardControls && Input.GetKeyDown(prepareAndStartTrialKey))
        {
            PrepareAndStartTrial();
        }

        if (state != Experiment2State.RunningTrial)
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
                state = Experiment2State.TrialAborted;
                status = logger.LastTrialDataSaved
                    ? "Trial ended before the finish. Incomplete objective data were saved."
                    : "Trial ended before the finish, and incomplete objective data could not be saved.";
            }
            return;
        }

        MonitorTracking();
    }

    [ContextMenu("Begin Experiment 2 Calibration")]
    public void BeginCalibrationSequence()
    {
        if (!Application.isPlaying)
        {
            SetWarning("Enter Play Mode before starting calibration.");
            return;
        }

        if (IsRunActive())
        {
            SetWarning("A calibration or measured trial is already active.");
            return;
        }

        ResolveReferences();
        ConfigureExperimentComponents();
        if (!TryValidateCommonSetup(false, out string validationMessage))
        {
            SetError(validationMessage);
            return;
        }

        if (!TryResolveParticipantSequenceNumber(out calibrationParticipantSequence))
        {
            SetError("Participant ID must end with a positive number, for example E2P001.");
            return;
        }

        selectedHorizon500Seconds = -1f;
        selectedHorizon1000Seconds = -1f;
        calibratedParticipantId = participantId.Trim();
        calibrationSelections500.Clear();
        calibrationSelections1000.Clear();
        calibrationEvents.Clear();
        calibrationDelayOrderIndex = 0;
        calibrationRunIndexForDelay = 0;
        calibrationTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        calibrationCsvPath = Path.Combine(
            ResolveDirectory(calibrationOutputDirectory, "Data/PredictiveFlyExperiment2/Calibration"),
            $"{Sanitize(calibratedParticipantId)}_{calibrationTimestamp}_experiment2_calibration.csv");
        lastCalibrationSaveError = string.Empty;

        AddCalibrationEvent("sequence_start", 0, 0, string.Empty, 0f, float.NaN,
            $"delay_order={GetDelayOrderString(calibrationParticipantSequence)}");
        StartCalibrationRun();
    }

    [ContextMenu("Decrease Calibration Horizon")]
    public void DecreaseCalibrationHorizonFromInspector()
    {
        AdjustCalibrationHorizon(-1);
    }

    [ContextMenu("Increase Calibration Horizon")]
    public void IncreaseCalibrationHorizonFromInspector()
    {
        AdjustCalibrationHorizon(1);
    }

    public void AdjustCalibrationHorizon(int stepDirection)
    {
        if (state != Experiment2State.Calibrating || stepDirection == 0)
        {
            return;
        }

        float next = RoundHorizon(
            currentCalibrationHorizonSeconds + Mathf.Sign(stepDirection) * horizonStepSeconds,
            minimumSelectableHorizonSeconds,
            maximumSelectableHorizonSeconds,
            horizonStepSeconds);
        if (Mathf.Approximately(next, currentCalibrationHorizonSeconds))
        {
            return;
        }

        currentCalibrationHorizonSeconds = next;
        ApplyPersonalizedPredictionProfile(currentCalibrationHorizonSeconds);
        AddCalibrationEvent(
            "adjustment",
            activeCalibrationDelayMilliseconds,
            activeCalibrationRunNumber,
            activeCalibrationAnchor,
            Time.unscaledTime - calibrationRunStartedAt,
            currentCalibrationHorizonSeconds,
            string.Empty);
        status = BuildCalibrationStatus("Adjusting");
    }

    [ContextMenu("Accept Calibration Run")]
    public void AcceptCalibrationRun()
    {
        if (state != Experiment2State.Calibrating)
        {
            return;
        }

        float elapsed = Time.unscaledTime - calibrationRunStartedAt;
        if (elapsed + 1e-4f < minimumCalibrationRunSeconds)
        {
            SetWarning($"Continue calibration for at least {minimumCalibrationRunSeconds:0.#} seconds ({elapsed:0.#} elapsed)." );
            return;
        }

        currentCalibrationHorizonSeconds = RoundHorizon(
            currentCalibrationHorizonSeconds,
            minimumSelectableHorizonSeconds,
            maximumSelectableHorizonSeconds,
            horizonStepSeconds);
        List<float> selections = GetCalibrationSelections(activeCalibrationDelayMilliseconds);
        selections.Add(currentCalibrationHorizonSeconds);
        AddCalibrationEvent(
            "run_accept",
            activeCalibrationDelayMilliseconds,
            activeCalibrationRunNumber,
            activeCalibrationAnchor,
            elapsed,
            currentCalibrationHorizonSeconds,
            string.Empty);

        locomotion.SetLocomotionInputEnabled(false);
        if (recenterAfterRun)
        {
            locomotion.RecenterNow();
        }

        calibrationRunIndexForDelay++;
        bool needsAnotherAnchoredRun = selections.Count < 2;
        bool needsThirdRun = selections.Count == 2
            && Mathf.Abs(selections[0] - selections[1]) > thirdRunDifferenceThresholdSeconds + 1e-5f;
        if (needsAnotherAnchoredRun || needsThirdRun)
        {
            StartCalibrationRun();
            return;
        }

        float lockedHorizon = ComputeLockedHorizon(
            selections,
            minimumSelectableHorizonSeconds,
            maximumSelectableHorizonSeconds,
            horizonStepSeconds);
        SetSelectedHorizon(activeCalibrationDelayMilliseconds, lockedHorizon);
        AddCalibrationEvent(
            "selection_locked",
            activeCalibrationDelayMilliseconds,
            activeCalibrationRunNumber,
            activeCalibrationAnchor,
            elapsed,
            currentCalibrationHorizonSeconds,
            $"selected_horizon_s={lockedHorizon.ToString("0.000", CultureInfo.InvariantCulture)}");

        calibrationDelayOrderIndex++;
        calibrationRunIndexForDelay = 0;
        if (calibrationDelayOrderIndex < 2)
        {
            StartCalibrationRun();
            return;
        }

        ApplyFixedPredictionProfile();
        locomotion.RecenterNow();
        state = Experiment2State.CalibrationComplete;
        status = $"Calibration complete: 500 ms={selectedHorizon500Seconds:0.0} s, 1000 ms={selectedHorizon1000Seconds:0.0} s. Set Trial Index and press Enter.";
        AddCalibrationEvent(
            "sequence_complete",
            0,
            0,
            string.Empty,
            0f,
            float.NaN,
            $"h500={selectedHorizon500Seconds:0.000};h1000={selectedHorizon1000Seconds:0.000}");
        UpdateScheduledAssignmentPreview();
    }

    void StartCalibrationRun()
    {
        if (preparationRoutine != null)
        {
            StopCoroutine(preparationRoutine);
        }
        preparationRoutine = StartCoroutine(PrepareCalibrationRunRoutine());
    }

    IEnumerator PrepareCalibrationRunRoutine()
    {
        state = Experiment2State.PreparingCalibration;
        trackingLostSince = -1f;
        activeCalibrationDelayMilliseconds = GetDelayForOrderPosition(
            calibrationParticipantSequence,
            calibrationDelayOrderIndex);
        activeCalibrationRunNumber = calibrationRunIndexForDelay + 1;
        activeCalibrationAnchor = GetCalibrationAnchor(
            calibrationParticipantSequence,
            calibrationDelayOrderIndex,
            calibrationRunIndexForDelay);
        currentCalibrationHorizonSeconds = GetAnchorHorizon(activeCalibrationAnchor);
        status = $"Preparing {activeCalibrationDelayMilliseconds} ms calibration run {activeCalibrationRunNumber} ({activeCalibrationAnchor}).";

        if (sceneBuilder.routeVariant != calibrationRoute)
        {
            locomotion.SetLocomotionInputEnabled(false);
            sceneBuilder.routeVariant = calibrationRoute;
            sceneBuilder.RebuildScene();
            yield return null;
            ResolveReferences();
            ConfigureExperimentComponents();
        }

        sceneBuilder.NormalizeCourseVisualMaterials();
        ConfigureLocomotionForDelay(activeCalibrationDelayMilliseconds);
        ApplyPersonalizedPredictionProfile(currentCalibrationHorizonSeconds);
        locomotion.SetVisualizationMode(
            PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost);
        locomotion.SetLocomotionInputEnabled(false);
        locomotion.RecenterNow();

        if (!TryValidateCommonSetup(false, out string validationMessage))
        {
            FailPreparation(validationMessage);
            yield break;
        }

        if (!TryWaitForPreparation())
        {
            yield return WaitForPreparationRoutine();
        }

        if (!locomotion.InputToRigDelayBufferReady || !locomotion.IsBodyAnchorAvailable)
        {
            FailPreparation("Calibration preparation timed out: delay buffer or body anchor was not ready.");
            yield break;
        }

        locomotion.SetLocomotionInputEnabled(true);
        calibrationRunStartedAt = Time.unscaledTime;
        state = Experiment2State.Calibrating;
        AddCalibrationEvent(
            "run_start",
            activeCalibrationDelayMilliseconds,
            activeCalibrationRunNumber,
            activeCalibrationAnchor,
            0f,
            currentCalibrationHorizonSeconds,
            string.Empty);
        status = BuildCalibrationStatus("Running");
        preparationRoutine = null;
    }

    [ContextMenu("Prepare And Start Experiment 2 Trial")]
    public void PrepareAndStartTrial()
    {
        if (!Application.isPlaying)
        {
            SetWarning("Enter Play Mode before starting a measured trial.");
            return;
        }

        if (IsRunActive())
        {
            SetWarning("A calibration or measured trial is already active.");
            return;
        }

        string requestedParticipantId = participantId != null ? participantId.Trim() : string.Empty;
        if (preventAccidentalCompletedTrialRepeat
            && completedTrialKeys.Contains(GetTrialKey(requestedParticipantId, trialIndex)))
        {
            SetWarning($"Trial {trialIndex} already completed for {requestedParticipantId}. Change Trial Index before pressing Enter.");
            return;
        }

        ResolveReferences();
        ConfigureExperimentComponents();
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
        state = Experiment2State.PreparingTrial;
        status = "Resolving Experiment 2 delay, proxy strategy, and route assignment.";
        trackingLostSince = -1f;

        if (!TryResolveParticipantSequenceNumber(out int sequenceNumber)
            || !TryGetTrialAssignment(sequenceNumber, trialIndex, out TrialAssignment assignment))
        {
            FailPreparation("Participant ID or Trial Index could not be resolved.");
            yield break;
        }

        activeParticipantId = participantId.Trim();
        activeTrialIndex = Mathf.Clamp(trialIndex, 1, 6);
        activeDelayMilliseconds = assignment.delayMilliseconds;
        activeStrategy = assignment.strategy;
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
            if (!TryValidateCommonSetup(true, out string rebuiltValidationMessage))
            {
                FailPreparation($"Scheduled route rebuild failed: {rebuiltValidationMessage}");
                yield break;
            }
        }

        sceneBuilder.NormalizeCourseVisualMaterials();
        activeRouteId = sceneBuilder.RouteId;
        ApplyTrialAssignment(assignment);

        status = "Preparing condition, calibration, and delay buffer.";
        if (!TryWaitForPreparation())
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
            "experiment2_trial_input_enabled",
            BuildTrialEventNote(assignment));
        state = Experiment2State.RunningTrial;
        status = $"Trial {activeTrialIndex} running: {conditionLabel}, {activeRouteId}.";
        preparationRoutine = null;
    }

    void ApplyTrialAssignment(TrialAssignment assignment)
    {
        ConfigureLocomotionForDelay(assignment.delayMilliseconds);
        switch (assignment.strategy)
        {
            case ProxyStrategy.Current:
                ApplyCurrentPredictionProfile();
                locomotion.SetVisualizationMode(
                    PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithRealTimeGhost);
                activeMaximumTranslationHorizonSeconds = 0f;
                break;
            case ProxyStrategy.Fixed:
                ApplyFixedPredictionProfile();
                locomotion.SetVisualizationMode(
                    PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost);
                activeMaximumTranslationHorizonSeconds = fixedMaxTranslationHorizonSeconds;
                break;
            default:
                float selected = GetSelectedHorizon(assignment.delayMilliseconds);
                ApplyPersonalizedPredictionProfile(selected);
                locomotion.SetVisualizationMode(
                    PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost);
                activeMaximumTranslationHorizonSeconds = selected;
                break;
        }

        locomotion.SetLocomotionInputEnabled(false);
        locomotion.RecenterNow();
    }

    void ApplyCurrentPredictionProfile()
    {
        if (locomotion == null)
        {
            return;
        }

        // Current displays the real-time remote estimate with no additional future extrapolation.
        locomotion.minTranslationPredictionWindow = 0f;
        locomotion.maxTranslationPredictionWindow = 0f;
        locomotion.minYawPredictionWindow = 0f;
        locomotion.maxYawPredictionWindow = 0f;
    }

    void ConfigureLocomotionForDelay(int delayMilliseconds)
    {
        locomotion.allowRuntimeVisualizationHotkeys = false;
        locomotion.allowRuntimePredictionHotkey = false;
        locomotion.enableInputToRigDelay = true;
        locomotion.inputToRigDelayMilliseconds = Mathf.Max(0, delayMilliseconds);
        locomotion.useCollisionConsistentStateDelay = true;
        locomotion.SetPredictionMethod(fixedPredictionMethod);
    }

    void ApplyFixedPredictionProfile()
    {
        if (locomotion == null)
        {
            return;
        }

        locomotion.minTranslationPredictionWindow = fixedMinTranslationHorizonSeconds;
        locomotion.maxTranslationPredictionWindow = fixedMaxTranslationHorizonSeconds;
        locomotion.minYawPredictionWindow = fixedMinYawHorizonSeconds;
        locomotion.maxYawPredictionWindow = fixedMaxYawHorizonSeconds;
    }

    void ApplyPersonalizedPredictionProfile(float selectedMaxTranslationHorizon)
    {
        if (locomotion == null)
        {
            return;
        }

        float selected = Mathf.Clamp(
            selectedMaxTranslationHorizon,
            minimumSelectableHorizonSeconds,
            maximumSelectableHorizonSeconds);
        float scale = fixedMaxTranslationHorizonSeconds > 1e-5f
            ? selected / fixedMaxTranslationHorizonSeconds
            : 0f;
        locomotion.minTranslationPredictionWindow = fixedMinTranslationHorizonSeconds * scale;
        locomotion.maxTranslationPredictionWindow = selected;
        locomotion.minYawPredictionWindow = fixedMinYawHorizonSeconds * scale;
        locomotion.maxYawPredictionWindow = fixedMaxYawHorizonSeconds * scale;
    }

    IEnumerator WaitForPreparationRoutine()
    {
        float startedAt = Time.unscaledTime;
        while (Time.unscaledTime - startedAt < preparationTimeoutSeconds)
        {
            bool minimumTimeReached = Time.unscaledTime - startedAt >= minimumPreparationSeconds;
            bool delayReady = locomotion != null && locomotion.InputToRigDelayBufferReady;
            bool trackingReady = locomotion != null && locomotion.IsBodyAnchorAvailable;
            status = $"Preparing: delay={(delayReady ? "ready" : "filling")}, body={(trackingReady ? "tracked" : "missing")}";
            if (minimumTimeReached && delayReady && trackingReady)
            {
                yield break;
            }
            yield return null;
        }
    }

    bool TryWaitForPreparation()
    {
        return minimumPreparationSeconds <= 0f
            && locomotion != null
            && locomotion.InputToRigDelayBufferReady
            && locomotion.IsBodyAnchorAvailable;
    }

    [ContextMenu("Abort Experiment 2 Run")]
    public void AbortActiveRunFromInspector()
    {
        AbortActiveRun("Experimenter aborted the run from the Inspector.");
    }

    public void AbortActiveRun(string reason)
    {
        if (preparationRoutine != null)
        {
            StopCoroutine(preparationRoutine);
            preparationRoutine = null;
        }

        bool calibrationActive = state == Experiment2State.PreparingCalibration
            || state == Experiment2State.Calibrating;
        bool trialActive = state == Experiment2State.PreparingTrial
            || state == Experiment2State.RunningTrial;
        if (!calibrationActive && !trialActive)
        {
            return;
        }

        if (calibrationActive)
        {
            AddCalibrationEvent(
                "sequence_abort",
                activeCalibrationDelayMilliseconds,
                activeCalibrationRunNumber,
                activeCalibrationAnchor,
                state == Experiment2State.Calibrating
                    ? Time.unscaledTime - calibrationRunStartedAt
                    : 0f,
                currentCalibrationHorizonSeconds,
                reason);
        }

        if (logger != null && logger.IsLogging)
        {
            logger.RecordSystemEvent("experiment2_trial_aborted", reason);
            logger.StopLogging(false);
        }

        if (locomotion != null)
        {
            locomotion.SetLocomotionInputEnabled(false);
            ApplyFixedPredictionProfile();
            if (recenterAfterRun)
            {
                locomotion.RecenterNow();
            }
        }

        state = Experiment2State.TrialAborted;
        status = logger != null && logger.LastTrialDataSaved
            ? $"{reason} Incomplete objective data were saved."
            : reason;
    }

    void CompleteTrial()
    {
        if (state != Experiment2State.RunningTrial)
        {
            return;
        }

        locomotion.SetLocomotionInputEnabled(false);
        ApplyFixedPredictionProfile();
        if (recenterAfterRun)
        {
            locomotion.RecenterNow();
        }

        completedTrialKeys.Add(GetTrialKey(activeParticipantId, activeTrialIndex));
        int completedForParticipant = 0;
        for (int i = 1; i <= 6; i++)
        {
            if (completedTrialKeys.Contains(GetTrialKey(activeParticipantId, i)))
            {
                completedForParticipant++;
            }
        }

        state = completedForParticipant == 6
            ? Experiment2State.SessionCompleted
            : Experiment2State.TrialCompleted;
        status = state == Experiment2State.SessionCompleted
            ? "All six measured Experiment 2 trials completed."
            : $"Trial {activeTrialIndex} completed. Set Trial Index to the next value when ready.";
    }

    void FailCompletedTrialSave()
    {
        locomotion.SetLocomotionInputEnabled(false);
        if (recenterAfterRun)
        {
            locomotion.RecenterNow();
        }

        state = Experiment2State.Error;
        string detail = string.IsNullOrWhiteSpace(logger.LastSaveError)
            ? "Unknown file output error."
            : logger.LastSaveError;
        status = $"Course completed, but objective data were not saved: {detail}";
        Debug.LogError($"[PredictiveFlyExperiment2Controller] {status}", this);
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
            AbortActiveRun($"Body anchor tracking was lost for {trackingLossGraceSeconds:0.###} seconds.");
        }
    }

    public bool TryValidateTrialSetup(out string message)
    {
        if (!TryValidateCommonSetup(true, out message))
        {
            return false;
        }

        if (trialIndex < 1 || trialIndex > 6)
        {
            message = "Trial Index must be between 1 and 6.";
            return false;
        }

        if (requireBothCalibrationsBeforeTrials)
        {
            if (!string.Equals(
                    calibratedParticipantId?.Trim(),
                    participantId?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                message = "The visible personalized horizons belong to another participant. Run calibration for this Participant ID.";
                return false;
            }
            if (selectedHorizon500Seconds < 0f || selectedHorizon1000Seconds < 0f)
            {
                message = "Both 500 ms and 1000 ms personalized horizons must be calibrated before measured trials.";
                return false;
            }
        }

        if (TryResolveParticipantSequenceNumber(out int sequenceNumber)
            && TryGetTrialAssignment(sequenceNumber, trialIndex, out TrialAssignment assignment)
            && assignment.strategy == ProxyStrategy.Personalized
            && GetSelectedHorizon(assignment.delayMilliseconds) < 0f)
        {
            message = $"The {assignment.delayMilliseconds} ms personalized horizon has not been calibrated.";
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
        if (fixedMaxTranslationHorizonSeconds < fixedMinTranslationHorizonSeconds
            || fixedMaxYawHorizonSeconds < fixedMinYawHorizonSeconds)
        {
            message = "Fixed prediction-window maxima must be at least their minima.";
            return false;
        }
        if (!Mathf.Approximately(
                fixedMinTranslationHorizonSeconds,
                Experiment1MinTranslationHorizonSeconds)
            || !Mathf.Approximately(
                fixedMaxTranslationHorizonSeconds,
                Experiment1MaxTranslationHorizonSeconds)
            || !Mathf.Approximately(fixedMinYawHorizonSeconds, Experiment1MinYawHorizonSeconds)
            || !Mathf.Approximately(fixedMaxYawHorizonSeconds, Experiment1MaxYawHorizonSeconds)
            || fixedPredictionMethod
                != PredictiveGhostAvatarLocomotion.PredictionMethod.AccelerationJerkLimited)
        {
            message = "Experiment 2 Fixed must reproduce the Experiment 1 profile: "
                + "translation 0.08-0.45 s, yaw 0.05-0.20 s, AccelerationJerkLimited.";
            return false;
        }
        if (maximumSelectableHorizonSeconds <= minimumSelectableHorizonSeconds)
        {
            message = "Maximum selectable horizon must exceed the minimum.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(objectiveOutputDirectory)
            || string.IsNullOrWhiteSpace(calibrationOutputDirectory))
        {
            message = "Experiment 2 output directories must not be empty.";
            return false;
        }

        message = "Ready";
        return true;
    }

    bool TryValidateCommonSetup(bool requireLoggerSetup, out string message)
    {
        if (string.IsNullOrWhiteSpace(participantId))
        {
            message = "Participant ID is empty.";
            return false;
        }
        if (!TryResolveParticipantSequenceNumber(out _))
        {
            message = "Participant ID must end with a positive number, for example E2P001.";
            return false;
        }
        if (!TryValidateStaticConfiguration(out message))
        {
            return false;
        }

        logger.participantId = participantId.Trim();
        if (requireLoggerSetup && !logger.TryValidateSetup(out message))
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
        if (locomotion == null || !locomotion.gameObject.scene.IsValid())
        {
            locomotion = FindFirstObjectByType<PredictiveGhostAvatarLocomotion>();
        }
    }

    void ConfigureExperimentComponents()
    {
        EnsureExperiment1ControllerDisabled();
        if (logger != null)
        {
            logger.enabled = true;
            logger.useKeyboardControls = false;
            logger.stopOnFinish = true;
            logger.stopOnModeChange = true;
            logger.saveIncompleteTrials = true;
            logger.incompleteTrialSubdirectory = "Incomplete";
            logger.outputDirectory = objectiveOutputDirectory;
            logger.participantId = string.IsNullOrWhiteSpace(participantId) ? "E2P001" : participantId.Trim();
        }
        if (locomotion != null)
        {
            locomotion.allowRuntimeVisualizationHotkeys = false;
            locomotion.allowRuntimePredictionHotkey = false;
            if (state != Experiment2State.Calibrating && state != Experiment2State.RunningTrial)
            {
                locomotion.SetLocomotionInputEnabled(false);
            }
        }
    }

    void EnsureExperiment1ControllerDisabled()
    {
        PredictiveFlyExperimentController experiment1 = GetComponent<PredictiveFlyExperimentController>();
        if (experiment1 != null && experiment1.enabled)
        {
            experiment1.enabled = false;
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

    public static bool TryGetTrialAssignment(
        int participantSequence,
        int requestedTrialIndex,
        out TrialAssignment assignment)
    {
        assignment = default;
        if (participantSequence < 1 || requestedTrialIndex < 1 || requestedTrialIndex > 6)
        {
            return false;
        }

        int block = (requestedTrialIndex - 1) / 3;
        int positionWithinBlock = (requestedTrialIndex - 1) % 3;
        int firstDelay = GetFirstDelayMilliseconds(participantSequence);
        int delay = block == 0 ? firstDelay : OtherDelay(firstDelay);
        int permutationRow = (participantSequence - 1) % 6;
        if (delay == 1000)
        {
            permutationRow = (permutationRow + 2) % 6;
        }

        ProxyStrategy strategy = (ProxyStrategy)StrategyPermutations[permutationRow, positionWithinBlock];
        int canonicalConditionCode = (delay == 500 ? 0 : 3) + (int)strategy;
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
        return (Mathf.Max(1, participantSequence) - 1) % 2 == 0 ? 500 : 1000;
    }

    public static int GetDelayForOrderPosition(int participantSequence, int delayOrderIndex)
    {
        int first = GetFirstDelayMilliseconds(participantSequence);
        return Mathf.Clamp(delayOrderIndex, 0, 1) == 0 ? first : OtherDelay(first);
    }

    public static string GetCalibrationAnchor(
        int participantSequence,
        int delayOrderIndex,
        int runIndexForDelay)
    {
        if (runIndexForDelay >= 2)
        {
            return "Mid";
        }

        int delayMilliseconds = GetDelayForOrderPosition(participantSequence, delayOrderIndex);
        int delayOffset = delayMilliseconds == 1000 ? 1 : 0;
        bool lowFirst = ((Mathf.Max(1, participantSequence) - 1 + delayOffset) % 2) == 0;
        if (runIndexForDelay == 0)
        {
            return lowFirst ? "Low" : "High";
        }
        return lowFirst ? "High" : "Low";
    }

    public static float ComputeLockedHorizon(
        IList<float> acceptedHorizons,
        float minimum,
        float maximum,
        float step)
    {
        if (acceptedHorizons == null || acceptedHorizons.Count == 0)
        {
            return -1f;
        }

        float selected;
        if (acceptedHorizons.Count == 1)
        {
            selected = acceptedHorizons[0];
        }
        else if (acceptedHorizons.Count == 2)
        {
            selected = (acceptedHorizons[0] + acceptedHorizons[1]) * 0.5f;
        }
        else
        {
            List<float> sorted = new List<float>(acceptedHorizons);
            sorted.Sort();
            selected = sorted[sorted.Count / 2];
        }

        return RoundHorizon(selected, minimum, maximum, step);
    }

    static int OtherDelay(int delayMilliseconds)
    {
        return delayMilliseconds == 500 ? 1000 : 500;
    }

    string GetConditionOrderString(int sequenceNumber)
    {
        StringBuilder builder = new StringBuilder(96);
        for (int i = 1; i <= 6; i++)
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
        char[] routes = new char[6];
        for (int i = 1; i <= 6; i++)
        {
            TryGetTrialAssignment(sequenceNumber, i, out TrialAssignment assignment);
            routes[i - 1] = RouteCode(assignment.route);
        }
        return new string(routes);
    }

    static char RouteCode(IrairaBou3DOfficialSceneBuilder.CourseRouteVariant route)
    {
        return (char)('A' + (int)route);
    }

    static string GetTrialKey(string participant, int index)
    {
        return $"{participant?.Trim()}|{Mathf.Clamp(index, 1, 6)}";
    }

    string GetConditionLabel(TrialAssignment assignment)
    {
        switch (assignment.strategy)
        {
            case ProxyStrategy.Current:
                return $"E2_D{assignment.delayMilliseconds}_Current";
            case ProxyStrategy.Fixed:
                return $"E2_D{assignment.delayMilliseconds}_Fixed";
            default:
                return $"E2_D{assignment.delayMilliseconds}_Personalized_H{HorizonToken(GetSelectedHorizon(assignment.delayMilliseconds))}";
        }
    }

    string BuildExperimentConfigId(TrialAssignment assignment)
    {
        float selected = assignment.strategy == ProxyStrategy.Personalized
            ? GetSelectedHorizon(assignment.delayMilliseconds)
            : assignment.strategy == ProxyStrategy.Fixed
                ? fixedMaxTranslationHorizonSeconds
                : 0f;
        return string.Join(
            "|",
            "Study2",
            $"delay_ms={assignment.delayMilliseconds}",
            $"strategy={assignment.strategy}",
            $"selected_max_translation_horizon_s={selected.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"fixed_translation_profile_s={fixedMinTranslationHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)}-{fixedMaxTranslationHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"fixed_yaw_profile_s={fixedMinYawHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)}-{fixedMaxYawHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"calibrated_h500_s={selectedHorizon500Seconds.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"calibrated_h1000_s={selectedHorizon1000Seconds.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    string BuildTrialEventNote(TrialAssignment assignment)
    {
        return $"trial_index={activeTrialIndex};delay_ms={assignment.delayMilliseconds};strategy={assignment.strategy};" +
            $"max_translation_horizon_s={activeMaximumTranslationHorizonSeconds.ToString("0.000", CultureInfo.InvariantCulture)};" +
            $"condition_order={activeConditionOrder};route={activeRouteId};route_order={activeRouteOrder};" +
            $"h500={selectedHorizon500Seconds.ToString("0.000", CultureInfo.InvariantCulture)};" +
            $"h1000={selectedHorizon1000Seconds.ToString("0.000", CultureInfo.InvariantCulture)}";
    }

    static string HorizonToken(float horizon)
    {
        return Mathf.Max(0f, horizon)
            .ToString("0.000", CultureInfo.InvariantCulture)
            .Replace('.', 'p');
    }

    float GetSelectedHorizon(int delayMilliseconds)
    {
        return delayMilliseconds == 500
            ? selectedHorizon500Seconds
            : selectedHorizon1000Seconds;
    }

    void SetSelectedHorizon(int delayMilliseconds, float value)
    {
        if (delayMilliseconds == 500)
        {
            selectedHorizon500Seconds = value;
        }
        else
        {
            selectedHorizon1000Seconds = value;
        }
    }

    List<float> GetCalibrationSelections(int delayMilliseconds)
    {
        return delayMilliseconds == 500
            ? calibrationSelections500
            : calibrationSelections1000;
    }

    float GetAnchorHorizon(string anchor)
    {
        if (anchor == "Low")
        {
            return minimumSelectableHorizonSeconds;
        }
        if (anchor == "High")
        {
            return maximumSelectableHorizonSeconds;
        }
        return RoundHorizon(
            (minimumSelectableHorizonSeconds + maximumSelectableHorizonSeconds) * 0.5f,
            minimumSelectableHorizonSeconds,
            maximumSelectableHorizonSeconds,
            horizonStepSeconds);
    }

    static float RoundHorizon(float value, float minimum, float maximum, float step)
    {
        float safeMin = Mathf.Min(minimum, maximum);
        float safeMax = Mathf.Max(minimum, maximum);
        float safeStep = Mathf.Max(0.0001f, step);
        float clamped = Mathf.Clamp(value, safeMin, safeMax);
        float steps = Mathf.Round((clamped - safeMin) / safeStep);
        return Mathf.Clamp(safeMin + steps * safeStep, safeMin, safeMax);
    }

    float ClampOptionalHorizon(float value)
    {
        return value < 0f
            ? -1f
            : RoundHorizon(
                value,
                minimumSelectableHorizonSeconds,
                maximumSelectableHorizonSeconds,
                horizonStepSeconds);
    }

    string GetDelayOrderString(int sequenceNumber)
    {
        int first = GetFirstDelayMilliseconds(sequenceNumber);
        return $"{first}>{OtherDelay(first)}";
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
            : assignment.strategy == ProxyStrategy.Fixed
                ? fixedMaxTranslationHorizonSeconds
                : GetSelectedHorizon(assignment.delayMilliseconds);
        string horizonText = horizon < 0f ? "calibration required" : $"Hmax={horizon:0.0} s";
        scheduledAssignmentPreview =
            $"Trial {trialIndex}: {assignment.conditionLabel}, Route_{RouteCode(assignment.route)}, {horizonText}";
        delayOrderPreview = GetDelayOrderString(sequenceNumber);
    }

    bool IsRunActive()
    {
        return state == Experiment2State.PreparingCalibration
            || state == Experiment2State.Calibrating
            || state == Experiment2State.PreparingTrial
            || state == Experiment2State.RunningTrial;
    }

    string BuildCalibrationStatus(string prefix)
    {
        return $"{prefix} {activeCalibrationDelayMilliseconds} ms calibration run {activeCalibrationRunNumber} " +
            $"({activeCalibrationAnchor}); horizon={currentCalibrationHorizonSeconds:0.0} s. Use [ / ] and Space.";
    }

    void AddCalibrationEvent(
        string eventType,
        int delayMilliseconds,
        int runNumber,
        string anchor,
        float elapsedSeconds,
        float horizonSeconds,
        string note)
    {
        calibrationEvents.Add(new CalibrationEventRecord
        {
            timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            eventType = eventType,
            participantId = string.IsNullOrWhiteSpace(calibratedParticipantId)
                ? participantId?.Trim()
                : calibratedParticipantId.Trim(),
            participantSequence = calibrationParticipantSequence,
            delayMilliseconds = delayMilliseconds,
            runNumber = runNumber,
            anchor = anchor,
            elapsedSeconds = elapsedSeconds,
            horizonSeconds = horizonSeconds,
            selectedHorizon500Seconds = selectedHorizon500Seconds,
            selectedHorizon1000Seconds = selectedHorizon1000Seconds,
            note = note
        });
        SaveCalibrationData();
    }

    void SaveCalibrationData()
    {
        if (string.IsNullOrWhiteSpace(calibrationCsvPath))
        {
            return;
        }

        StringBuilder csv = new StringBuilder(4096);
        csv.AppendLine("timestamp,event_type,participant_id,participant_sequence,delay_ms,run_number,start_anchor,elapsed_s,horizon_s,selected_horizon_500_s,selected_horizon_1000_s,note");
        for (int i = 0; i < calibrationEvents.Count; i++)
        {
            CalibrationEventRecord item = calibrationEvents[i];
            AppendCsv(csv, item.timestamp);
            AppendCsv(csv, item.eventType);
            AppendCsv(csv, item.participantId);
            AppendCsv(csv, item.participantSequence.ToString(CultureInfo.InvariantCulture));
            AppendCsv(csv, item.delayMilliseconds > 0
                ? item.delayMilliseconds.ToString(CultureInfo.InvariantCulture)
                : string.Empty);
            AppendCsv(csv, item.runNumber > 0
                ? item.runNumber.ToString(CultureInfo.InvariantCulture)
                : string.Empty);
            AppendCsv(csv, item.anchor);
            AppendCsv(csv, item.elapsedSeconds.ToString("0.000", CultureInfo.InvariantCulture));
            AppendCsv(csv, float.IsNaN(item.horizonSeconds)
                ? string.Empty
                : item.horizonSeconds.ToString("0.000", CultureInfo.InvariantCulture));
            AppendCsv(csv, item.selectedHorizon500Seconds < 0f
                ? string.Empty
                : item.selectedHorizon500Seconds.ToString("0.000", CultureInfo.InvariantCulture));
            AppendCsv(csv, item.selectedHorizon1000Seconds < 0f
                ? string.Empty
                : item.selectedHorizon1000Seconds.ToString("0.000", CultureInfo.InvariantCulture));
            AppendCsv(csv, item.note, true);
        }

        string stagingPath = calibrationCsvPath + ".writing";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(calibrationCsvPath));
            File.WriteAllText(stagingPath, csv.ToString(), new UTF8Encoding(false));
            if (File.Exists(calibrationCsvPath))
            {
                File.Replace(stagingPath, calibrationCsvPath, null);
            }
            else
            {
                File.Move(stagingPath, calibrationCsvPath);
            }
            lastCalibrationSaveError = string.Empty;
        }
        catch (Exception exception)
        {
            lastCalibrationSaveError = exception.Message;
            TryDeleteFile(stagingPath);
            Debug.LogError($"[PredictiveFlyExperiment2Controller] Calibration CSV could not be saved: {exception}", this);
        }
    }

    static void AppendCsv(StringBuilder builder, string value, bool endRow = false)
    {
        string safe = value ?? string.Empty;
        bool requiresQuotes = safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (requiresQuotes)
        {
            builder.Append('"');
            builder.Append(safe.Replace("\"", "\"\""));
            builder.Append('"');
        }
        else
        {
            builder.Append(safe);
        }
        builder.Append(endRow ? '\n' : ',');
    }

    string ResolveDirectory(string configured, string fallback)
    {
        string directory = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        if (Path.IsPathRooted(directory))
        {
            return Path.GetFullPath(directory);
        }

#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", directory));
#else
        return Path.Combine(Application.persistentDataPath, directory);
#endif
    }

    static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NA";
        }

        StringBuilder builder = new StringBuilder(value.Length);
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(Array.IndexOf(invalid, c) >= 0 || char.IsWhiteSpace(c) ? '_' : c);
        }
        return builder.ToString();
    }

    static void TryDeleteFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }

    void FailPreparation(string message)
    {
        locomotion?.SetLocomotionInputEnabled(false);
        state = Experiment2State.Error;
        status = message;
        preparationRoutine = null;
        Debug.LogError($"[PredictiveFlyExperiment2Controller] {message}", this);
    }

    void SetWarning(string message)
    {
        status = message;
        Debug.LogWarning($"[PredictiveFlyExperiment2Controller] {message}", this);
    }

    void SetError(string message)
    {
        state = Experiment2State.Error;
        status = message;
        Debug.LogError($"[PredictiveFlyExperiment2Controller] {message}", this);
    }

    struct CalibrationEventRecord
    {
        public string timestamp;
        public string eventType;
        public string participantId;
        public int participantSequence;
        public int delayMilliseconds;
        public int runNumber;
        public string anchor;
        public float elapsedSeconds;
        public float horizonSeconds;
        public float selectedHorizon500Seconds;
        public float selectedHorizon1000Seconds;
        public string note;
    }
}
