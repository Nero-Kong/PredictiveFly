using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(640)]
[DisallowMultipleComponent]
public class PredictiveFlyExperiment2Controller : MonoBehaviour
{
    public const string ProtocolVersion = PredictiveFlyExperiment2CalibrationStore.ProtocolVersion;
    public const int CalibrationDelayCount = 5;
    public const float Experiment1MinTranslationHorizonSeconds = 0.08f;
    public const float Experiment1MaxTranslationHorizonSeconds = 0.45f;
    public const float Experiment1MinYawHorizonSeconds = 0.05f;
    public const float Experiment1MaxYawHorizonSeconds = 0.2f;

    static readonly int[] CalibrationDelayLevelsMilliseconds = { 0, 250, 500, 750, 1000 };
    static readonly int[,] CalibrationDelayOrders =
    {
        { 0, 1, 4, 2, 3 },
        { 1, 2, 0, 3, 4 },
        { 2, 3, 1, 4, 0 },
        { 3, 4, 2, 0, 1 },
        { 4, 0, 3, 1, 2 },
        { 3, 2, 4, 1, 0 },
        { 4, 3, 0, 2, 1 },
        { 0, 4, 1, 3, 2 },
        { 1, 0, 2, 4, 3 },
        { 2, 1, 3, 0, 4 }
    };

    public enum CalibrationState
    {
        Idle,
        Preparing,
        Calibrating,
        Complete,
        Aborted,
        Error
    }

    [Header("Operator Input")]
    [Tooltip("Set this to the participant ID before starting calibration.")]
    public string participantId = "P001";

    [Header("Keyboard Controls")]
    public bool useKeyboardControls = true;
    public KeyCode beginCalibrationKey = KeyCode.F7;
    [Tooltip("Ordinary-key fallback in case the keyboard or Unity intercepts the function key.")]
    public KeyCode alternateBeginCalibrationKey = KeyCode.K;
    public KeyCode decreaseHorizonKey = KeyCode.LeftArrow;
    public KeyCode increaseHorizonKey = KeyCode.RightArrow;
    public KeyCode acceptCalibrationKey = KeyCode.Space;
    public KeyCode abortKey = KeyCode.Escape;

    [Header("Calibration")]
    [Tooltip("Maximum translation look-ahead available during participant adjustment. Yaw windows scale proportionally.")]
    [Min(0f)] public float minimumSelectableHorizonSeconds;
    [Min(0.05f)] public float maximumSelectableHorizonSeconds = 1f;
    [Min(0.01f)] public float horizonStepSeconds = 0.1f;
    [Min(0f)] public float minimumCalibrationRunSeconds = 20f;
    [Tooltip("Add a third mid-anchor run when the low- and high-anchor choices differ by more than this amount.")]
    [Min(0f)] public float thirdRunDifferenceThresholdSeconds = 0.2f;
    public IrairaBou3DOfficialSceneBuilder.CourseRouteVariant calibrationRoute =
        IrairaBou3DOfficialSceneBuilder.CourseRouteVariant.Base;

    [Header("Personalized Profile Shape")]
    [Min(0f)] public float referenceMinTranslationHorizonSeconds =
        Experiment1MinTranslationHorizonSeconds;
    [Min(0f)] public float referenceMaxTranslationHorizonSeconds =
        Experiment1MaxTranslationHorizonSeconds;
    [Min(0f)] public float referenceMinYawHorizonSeconds =
        Experiment1MinYawHorizonSeconds;
    [Min(0f)] public float referenceMaxYawHorizonSeconds =
        Experiment1MaxYawHorizonSeconds;
    public PredictiveGhostAvatarLocomotion.PredictionMethod predictionMethod =
        PredictiveGhostAvatarLocomotion.PredictionMethod.AccelerationJerkLimited;

    [Header("Run Lifecycle")]
    [Min(0f)] public float minimumPreparationSeconds = 0.75f;
    [Min(0.5f)] public float preparationTimeoutSeconds = 8f;
    public bool recenterAfterRun = true;

    [Header("Output")]
    [Tooltip("Calibration CSV files and completed participant profiles are stored here.")]
    public string calibrationOutputDirectory =
        PredictiveFlyExperiment2CalibrationStore.DefaultCalibrationOutputDirectory;

    [Header("References")]
    public PredictiveGhostAvatarLocomotion locomotion;
    public IrairaBou3DOfficialSceneBuilder sceneBuilder;
    public PredictiveFlyExperiment3Controller experiment3Controller;
    public bool autoFindReferences = true;

    [Header("Debug")]
    [SerializeField, HideInInspector] CalibrationState state = CalibrationState.Idle;
    [SerializeField, HideInInspector] string status = "Idle";
    [SerializeField, HideInInspector] string delayOrderPreview;
    [SerializeField, HideInInspector] string calibratedParticipantId;
    [SerializeField, HideInInspector] float selectedHorizon0Seconds = -1f;
    [SerializeField, HideInInspector] float selectedHorizon250Seconds = -1f;
    [SerializeField, HideInInspector] float selectedHorizon500Seconds = -1f;
    [SerializeField, HideInInspector] float selectedHorizon750Seconds = -1f;
    [SerializeField, HideInInspector] float selectedHorizon1000Seconds = -1f;
    [SerializeField, HideInInspector] int activeDelayMilliseconds;
    [SerializeField, HideInInspector] int activeRunNumber;
    [SerializeField, HideInInspector] string activeAnchor;
    [SerializeField, HideInInspector] float currentHorizonSeconds;
    [SerializeField, HideInInspector] string calibrationCsvPath;
    [SerializeField, HideInInspector] string completedProfilePath;
    [SerializeField, HideInInspector] string lastSaveError;

    Coroutine preparationRoutine;
    float calibrationRunStartedAt;
    int delayOrderIndex;
    int runIndexForDelay;
    int participantSequence;
    string calibrationTimestamp;

    readonly List<float> selections0 = new List<float>(3);
    readonly List<float> selections250 = new List<float>(3);
    readonly List<float> selections500 = new List<float>(3);
    readonly List<float> selections750 = new List<float>(3);
    readonly List<float> selections1000 = new List<float>(3);
    readonly List<CalibrationEventRecord> calibrationEvents =
        new List<CalibrationEventRecord>(64);

    float metricsLastSampleAt;
    Vector3 lastRealTimePosition;
    bool hasLastRealTimePosition;
    float metricDuration;
    float latestSpeed;
    float speedIntegral;
    float maxSpeed;
    float delayedToRealTimeIntegral;
    float maxDelayedToRealTime;
    float realTimeToProxyIntegral;
    float maxRealTimeToProxy;
    float delayedToProxyIntegral;
    float maxDelayedToProxy;
    float latestDelayedToRealTime;
    float latestRealTimeToProxy;
    float latestDelayedToProxy;

    public CalibrationState State => state;
    public string Status => status;
    public string DelayOrderPreview => delayOrderPreview;
    public string CalibratedParticipantId => calibratedParticipantId;
    public float SelectedHorizon0Seconds => selectedHorizon0Seconds;
    public float SelectedHorizon250Seconds => selectedHorizon250Seconds;
    public float SelectedHorizon500Seconds => selectedHorizon500Seconds;
    public float SelectedHorizon750Seconds => selectedHorizon750Seconds;
    public float SelectedHorizon1000Seconds => selectedHorizon1000Seconds;
    public int ActiveDelayMilliseconds => activeDelayMilliseconds;
    public int ActiveRunNumber => activeRunNumber;
    public string ActiveAnchor => activeAnchor;
    public float CurrentHorizonSeconds => currentHorizonSeconds;
    public string CalibrationCsvPath => calibrationCsvPath;
    public string CompletedProfilePath => completedProfilePath;
    public bool IsCalibrationActive => state == CalibrationState.Preparing
        || state == CalibrationState.Calibrating;

    void Awake()
    {
        ResolveReferences();
        ConfigureExperimentComponents();
        UpdateDelayOrderPreview();
    }

    void OnEnable()
    {
        ResolveReferences();
        ConfigureExperimentComponents();
        UpdateDelayOrderPreview();
    }

    void OnValidate()
    {
        maximumSelectableHorizonSeconds = Mathf.Max(
            minimumSelectableHorizonSeconds + 0.05f,
            maximumSelectableHorizonSeconds);
        horizonStepSeconds = Mathf.Max(0.01f, horizonStepSeconds);
        preparationTimeoutSeconds = Mathf.Max(0.5f, preparationTimeoutSeconds);
        referenceMaxTranslationHorizonSeconds = Mathf.Max(
            referenceMinTranslationHorizonSeconds,
            referenceMaxTranslationHorizonSeconds);
        referenceMaxYawHorizonSeconds = Mathf.Max(
            referenceMinYawHorizonSeconds,
            referenceMaxYawHorizonSeconds);
        UpdateDelayOrderPreview();
    }

    void Update()
    {
        EnsureExperiment1ControllerDisabled();

        if (useKeyboardControls && Input.GetKeyDown(abortKey) && IsCalibrationActive)
        {
            AbortCalibration("Experimenter abort key pressed.");
            return;
        }

        if (state == CalibrationState.Calibrating)
        {
            UpdateRunMetrics();
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

        if (useKeyboardControls
            && (IsKeyDown(beginCalibrationKey) || IsKeyDown(alternateBeginCalibrationKey)))
        {
            BeginCalibrationSequence();
        }
    }

    [ContextMenu("Begin Experiment 2 Calibration")]
    public void BeginCalibrationSequence()
    {
        if (!Application.isPlaying)
        {
            SetWarning("Enter Play Mode before starting calibration.");
            return;
        }
        if (IsCalibrationActive)
        {
            SetWarning("A calibration sequence is already active.");
            return;
        }

        ResolveReferences();
        ConfigureExperimentComponents();
        if (experiment3Controller != null && experiment3Controller.IsFormalRunActive)
        {
            SetWarning("A measured Experiment 3 trial is active. Finish or abort it before Experiment 2.");
            return;
        }
        if (!TryValidateStaticConfiguration(out string validationMessage))
        {
            SetError(validationMessage);
            return;
        }
        if (!TryResolveParticipantSequenceNumber(out participantSequence))
        {
            SetError("Participant ID must end with a positive number, for example P001.");
            return;
        }
        if (!PredictiveFlyExperiment2CalibrationStore.TryInvalidateCompletedProfile(
                participantId,
                calibrationOutputDirectory,
                out string invalidateMessage))
        {
            SetError(invalidateMessage);
            return;
        }

        selectedHorizon0Seconds = -1f;
        selectedHorizon250Seconds = -1f;
        selectedHorizon500Seconds = -1f;
        selectedHorizon750Seconds = -1f;
        selectedHorizon1000Seconds = -1f;
        calibratedParticipantId = participantId.Trim();
        selections0.Clear();
        selections250.Clear();
        selections500.Clear();
        selections750.Clear();
        selections1000.Clear();
        calibrationEvents.Clear();
        delayOrderIndex = 0;
        runIndexForDelay = 0;
        calibrationTimestamp = DateTime.Now.ToString(
            "yyyyMMdd_HHmmss_fff",
            CultureInfo.InvariantCulture);
        string directory = PredictiveFlyExperiment2CalibrationStore.ResolveDirectory(
            calibrationOutputDirectory);
        calibrationCsvPath = Path.Combine(
            directory,
            $"{PredictiveFlyExperiment2CalibrationStore.Sanitize(calibratedParticipantId)}_"
            + $"{calibrationTimestamp}_experiment2_calibration.csv");
        completedProfilePath = PredictiveFlyExperiment2CalibrationStore.GetProfilePath(
            calibratedParticipantId,
            calibrationOutputDirectory);
        lastSaveError = string.Empty;

        AddCalibrationEvent(
            "sequence_start",
            -1,
            0,
            string.Empty,
            0f,
            float.NaN,
            $"delay_order={GetDelayOrderString(participantSequence)};{invalidateMessage}");
        if (!string.IsNullOrEmpty(lastSaveError))
        {
            SetError($"Calibration could not start because its CSV was not saved: {lastSaveError}");
            return;
        }

        Debug.Log(
            $"[PredictiveFlyExperiment2Controller] Experiment 2 sequence accepted for "
            + $"{calibratedParticipantId}; preparing the first run.",
            this);
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
        if (state != CalibrationState.Calibrating || stepDirection == 0)
        {
            return;
        }

        float next = RoundHorizon(
            currentHorizonSeconds + Mathf.Sign(stepDirection) * horizonStepSeconds,
            minimumSelectableHorizonSeconds,
            maximumSelectableHorizonSeconds,
            horizonStepSeconds);
        if (Mathf.Approximately(next, currentHorizonSeconds))
        {
            return;
        }

        currentHorizonSeconds = next;
        ApplyPersonalizedPredictionProfile(currentHorizonSeconds);
        UpdateCalibrationProxyAppearance();
        AddCalibrationEvent(
            "adjustment",
            activeDelayMilliseconds,
            activeRunNumber,
            activeAnchor,
            Time.unscaledTime - calibrationRunStartedAt,
            currentHorizonSeconds,
            string.Empty);
        status = BuildCalibrationStatus("Adjusting");
    }

    [ContextMenu("Accept Calibration Run")]
    public void AcceptCalibrationRun()
    {
        if (state != CalibrationState.Calibrating)
        {
            return;
        }

        float elapsed = Time.unscaledTime - calibrationRunStartedAt;
        if (elapsed + 1e-4f < minimumCalibrationRunSeconds)
        {
            SetWarning(
                $"Continue calibration for at least {minimumCalibrationRunSeconds:0.#} seconds "
                + $"({elapsed:0.#} elapsed).");
            return;
        }

        currentHorizonSeconds = RoundHorizon(
            currentHorizonSeconds,
            minimumSelectableHorizonSeconds,
            maximumSelectableHorizonSeconds,
            horizonStepSeconds);
        List<float> selections = GetSelections(activeDelayMilliseconds);
        selections.Add(currentHorizonSeconds);
        AddCalibrationEvent(
            "run_accept",
            activeDelayMilliseconds,
            activeRunNumber,
            activeAnchor,
            elapsed,
            currentHorizonSeconds,
            string.Empty);
        if (!string.IsNullOrEmpty(lastSaveError))
        {
            SetError($"Calibration run could not be accepted because its CSV was not saved: {lastSaveError}");
            return;
        }

        locomotion.SetLocomotionInputEnabled(false);
        if (recenterAfterRun)
        {
            locomotion.RecenterNow();
        }

        runIndexForDelay++;
        bool needsAnotherAnchoredRun = selections.Count < 2;
        bool needsThirdRun = selections.Count == 2
            && Mathf.Abs(selections[0] - selections[1])
                > thirdRunDifferenceThresholdSeconds + 1e-5f;
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
        SetSelectedHorizon(activeDelayMilliseconds, lockedHorizon);
        AddCalibrationEvent(
            "selection_locked",
            activeDelayMilliseconds,
            activeRunNumber,
            activeAnchor,
            elapsed,
            currentHorizonSeconds,
            $"selected_horizon_s={lockedHorizon.ToString("0.000", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrEmpty(lastSaveError))
        {
            SetError($"Locked calibration value could not be saved: {lastSaveError}");
            return;
        }

        delayOrderIndex++;
        runIndexForDelay = 0;
        if (delayOrderIndex < CalibrationDelayCount)
        {
            StartCalibrationRun();
            return;
        }

        CompleteCalibrationSequence();
    }

    void CompleteCalibrationSequence()
    {
        ApplyCurrentPredictionProfile();
        locomotion.SetStateGhostAppearanceSuppressed(false);
        locomotion.RecenterNow();
        AddCalibrationEvent(
            "sequence_complete",
            -1,
            0,
            string.Empty,
            0f,
            float.NaN,
            $"protocol={ProtocolVersion};h0={selectedHorizon0Seconds:0.000};"
            + $"h250={selectedHorizon250Seconds:0.000};"
            + $"h500={selectedHorizon500Seconds:0.000};"
            + $"h750={selectedHorizon750Seconds:0.000};"
            + $"h1000={selectedHorizon1000Seconds:0.000}");
        if (!string.IsNullOrEmpty(lastSaveError) || !File.Exists(calibrationCsvPath))
        {
            SetError(
                "Calibration values were selected, but the completion CSV could not be saved. "
                + $"Formal trials remain locked. {lastSaveError}");
            return;
        }

        PredictiveFlyExperiment2CalibrationStore.CompletedCalibrationProfile profile =
            new PredictiveFlyExperiment2CalibrationStore.CompletedCalibrationProfile
            {
                protocolVersion = ProtocolVersion,
                completed = true,
                participantId = calibratedParticipantId,
                completedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                calibrationCsvFileName = Path.GetFileName(calibrationCsvPath),
                selectedHorizon0Seconds = selectedHorizon0Seconds,
                selectedHorizon250Seconds = selectedHorizon250Seconds,
                selectedHorizon500Seconds = selectedHorizon500Seconds,
                selectedHorizon750Seconds = selectedHorizon750Seconds,
                selectedHorizon1000Seconds = selectedHorizon1000Seconds,
                minimumSelectableHorizonSeconds = minimumSelectableHorizonSeconds,
                maximumSelectableHorizonSeconds = maximumSelectableHorizonSeconds,
                horizonStepSeconds = horizonStepSeconds,
                referenceMinTranslationHorizonSeconds = referenceMinTranslationHorizonSeconds,
                referenceMaxTranslationHorizonSeconds = referenceMaxTranslationHorizonSeconds,
                referenceMinYawHorizonSeconds = referenceMinYawHorizonSeconds,
                referenceMaxYawHorizonSeconds = referenceMaxYawHorizonSeconds,
                predictionMethod = (int)predictionMethod
            };
        if (!PredictiveFlyExperiment2CalibrationStore.TrySaveCompletedProfile(
                profile,
                calibrationOutputDirectory,
                out completedProfilePath,
                out string saveMessage))
        {
            SetError(
                $"Calibration completion profile was not saved. Formal trials remain locked. {saveMessage}");
            return;
        }

        state = CalibrationState.Complete;
        status = $"Calibration complete and saved locally for {calibratedParticipantId}: "
            + $"0 ms={selectedHorizon0Seconds:0.0} s, "
            + $"250 ms={selectedHorizon250Seconds:0.0} s, "
            + $"500 ms={selectedHorizon500Seconds:0.0} s, "
            + $"750 ms={selectedHorizon750Seconds:0.0} s, "
            + $"1000 ms={selectedHorizon1000Seconds:0.0} s. Experiment 3 is unlocked.";
        experiment3Controller?.TryRefreshCalibrationFromDisk(out _);
        Debug.Log($"[PredictiveFlyExperiment2Controller] {status}", this);
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
        state = CalibrationState.Preparing;
        activeDelayMilliseconds = GetCalibrationDelayForOrderPosition(
            participantSequence,
            delayOrderIndex);
        activeRunNumber = runIndexForDelay + 1;
        activeAnchor = GetCalibrationAnchor(
            participantSequence,
            delayOrderIndex,
            runIndexForDelay);
        currentHorizonSeconds = GetAnchorHorizon(activeAnchor);
        status = $"Preparing {activeDelayMilliseconds} ms calibration run "
            + $"{activeRunNumber} ({activeAnchor}).";

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
        ConfigureLocomotionForDelay(activeDelayMilliseconds);
        ApplyPersonalizedPredictionProfile(currentHorizonSeconds);
        locomotion.SetVisualizationMode(
            PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost);
        UpdateCalibrationProxyAppearance();
        locomotion.SetLocomotionInputEnabled(false);
        locomotion.RecenterNow();

        if (!TryValidateStaticConfiguration(out string validationMessage))
        {
            FailPreparation(validationMessage);
            yield break;
        }

        if (!IsPreparationReady())
        {
            yield return WaitForPreparationRoutine();
        }
        if (!locomotion.InputToRigDelayBufferReady || !locomotion.IsBodyAnchorAvailable)
        {
            FailPreparation(
                "Calibration preparation timed out: delay buffer or body anchor was not ready.");
            yield break;
        }

        locomotion.SetLocomotionInputEnabled(true);
        calibrationRunStartedAt = Time.unscaledTime;
        ResetRunMetrics();
        state = CalibrationState.Calibrating;
        AddCalibrationEvent(
            "run_start",
            activeDelayMilliseconds,
            activeRunNumber,
            activeAnchor,
            0f,
            currentHorizonSeconds,
            string.Empty);
        if (!string.IsNullOrEmpty(lastSaveError))
        {
            locomotion.SetLocomotionInputEnabled(false);
            SetError($"Calibration run CSV could not be saved: {lastSaveError}");
            yield break;
        }
        status = BuildCalibrationStatus("Running");
        Debug.Log(
            $"[PredictiveFlyExperiment2Controller] {status} Locomotion input is enabled.",
            this);
        preparationRoutine = null;
    }

    IEnumerator WaitForPreparationRoutine()
    {
        float startedAt = Time.unscaledTime;
        while (Time.unscaledTime - startedAt < preparationTimeoutSeconds)
        {
            bool minimumTimeReached = Time.unscaledTime - startedAt >= minimumPreparationSeconds;
            bool delayReady = locomotion != null && locomotion.InputToRigDelayBufferReady;
            bool trackingReady = locomotion != null && locomotion.IsBodyAnchorAvailable;
            status = $"Preparing calibration: delay={(delayReady ? "ready" : "filling")}, "
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

    [ContextMenu("Abort Experiment 2 Calibration")]
    public void AbortCalibrationFromInspector()
    {
        AbortCalibration("Experimenter aborted calibration from the Inspector.");
    }

    public void AbortCalibration(string reason)
    {
        if (!IsCalibrationActive)
        {
            return;
        }
        if (preparationRoutine != null)
        {
            StopCoroutine(preparationRoutine);
            preparationRoutine = null;
        }

        AddCalibrationEvent(
            "sequence_abort",
            activeDelayMilliseconds,
            activeRunNumber,
            activeAnchor,
            state == CalibrationState.Calibrating
                ? Time.unscaledTime - calibrationRunStartedAt
                : 0f,
            currentHorizonSeconds,
            reason);
        locomotion?.SetLocomotionInputEnabled(false);
        ApplyCurrentPredictionProfile();
        locomotion?.SetStateGhostAppearanceSuppressed(false);
        if (recenterAfterRun)
        {
            locomotion?.RecenterNow();
        }
        state = CalibrationState.Aborted;
        status = $"{reason} No completed profile was created; Experiment 3 remains locked.";
    }

    [ContextMenu("Check Saved Calibration")]
    public void CheckSavedCalibrationFromInspector()
    {
        if (TryCheckSavedCalibration(out string message))
        {
            status = message;
            Debug.Log($"[PredictiveFlyExperiment2Controller] {message}", this);
        }
        else
        {
            SetWarning(message);
        }
    }

    public bool TryCheckSavedCalibration(out string message)
    {
        if (!PredictiveFlyExperiment2CalibrationStore.TryLoadCompletedProfile(
                participantId,
                calibrationOutputDirectory,
                out PredictiveFlyExperiment2CalibrationStore.CompletedCalibrationProfile profile,
                out completedProfilePath,
                out message))
        {
            return false;
        }

        calibratedParticipantId = profile.participantId;
        selectedHorizon0Seconds = profile.selectedHorizon0Seconds;
        selectedHorizon250Seconds = profile.selectedHorizon250Seconds;
        selectedHorizon500Seconds = profile.selectedHorizon500Seconds;
        selectedHorizon750Seconds = profile.selectedHorizon750Seconds;
        selectedHorizon1000Seconds = profile.selectedHorizon1000Seconds;
        calibrationCsvPath = Path.Combine(
            PredictiveFlyExperiment2CalibrationStore.ResolveDirectory(calibrationOutputDirectory),
            profile.calibrationCsvFileName);
        message = $"Saved calibration is complete for {profile.participantId}: "
            + $"0 ms={profile.selectedHorizon0Seconds:0.0} s, "
            + $"250 ms={profile.selectedHorizon250Seconds:0.0} s, "
            + $"500 ms={profile.selectedHorizon500Seconds:0.0} s, "
            + $"750 ms={profile.selectedHorizon750Seconds:0.0} s, "
            + $"1000 ms={profile.selectedHorizon1000Seconds:0.0} s.";
        return true;
    }

    public bool TryValidateStaticConfiguration(out string message)
    {
        ResolveReferences();
        if (sceneBuilder == null || locomotion == null)
        {
            message = "Scene builder or locomotion reference is missing.";
            return false;
        }
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
        if (maximumSelectableHorizonSeconds <= minimumSelectableHorizonSeconds)
        {
            message = "Maximum selectable horizon must exceed the minimum.";
            return false;
        }
        if (referenceMaxTranslationHorizonSeconds < referenceMinTranslationHorizonSeconds
            || referenceMaxYawHorizonSeconds < referenceMinYawHorizonSeconds)
        {
            message = "Personalized profile reference maxima must be at least their minima.";
            return false;
        }
        if (!Mathf.Approximately(
                referenceMinTranslationHorizonSeconds,
                Experiment1MinTranslationHorizonSeconds)
            || !Mathf.Approximately(
                referenceMaxTranslationHorizonSeconds,
                Experiment1MaxTranslationHorizonSeconds)
            || !Mathf.Approximately(
                referenceMinYawHorizonSeconds,
                Experiment1MinYawHorizonSeconds)
            || !Mathf.Approximately(
                referenceMaxYawHorizonSeconds,
                Experiment1MaxYawHorizonSeconds)
            || predictionMethod
                != PredictiveGhostAvatarLocomotion.PredictionMethod.AccelerationJerkLimited)
        {
            message = "Experiment 2 calibration must use the Experiment 1 profile shape: "
                + "translation 0.08-0.45 s, yaw 0.05-0.20 s, AccelerationJerkLimited.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(calibrationOutputDirectory))
        {
            message = "Calibration output directory is empty.";
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
        if (experiment3Controller == null)
        {
            experiment3Controller = GetComponent<PredictiveFlyExperiment3Controller>();
        }
        if (locomotion == null || !locomotion.gameObject.scene.IsValid())
        {
            locomotion = FindFirstObjectByType<PredictiveGhostAvatarLocomotion>();
        }
    }

    void ConfigureExperimentComponents()
    {
        EnsureExperiment1ControllerDisabled();
        if (locomotion == null)
        {
            return;
        }
        locomotion.allowRuntimeVisualizationHotkeys = false;
        locomotion.allowRuntimePredictionHotkey = false;
        if (!IsCalibrationActive)
        {
            locomotion.SetLocomotionInputEnabled(false);
        }
    }

    void EnsureExperiment1ControllerDisabled()
    {
        PredictiveFlyExperimentController experiment1 =
            GetComponent<PredictiveFlyExperimentController>();
        if (experiment1 != null && experiment1.enabled)
        {
            experiment1.enabled = false;
        }
    }

    void ConfigureLocomotionForDelay(int delayMilliseconds)
    {
        locomotion.allowRuntimeVisualizationHotkeys = false;
        locomotion.allowRuntimePredictionHotkey = false;
        locomotion.enableInputToRigDelay = true;
        locomotion.inputToRigDelayMilliseconds = Mathf.Max(0, delayMilliseconds);
        locomotion.useCollisionConsistentStateDelay = true;
        locomotion.SetPredictionMethod(predictionMethod);
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
        float scale = referenceMaxTranslationHorizonSeconds > 1e-5f
            ? selected / referenceMaxTranslationHorizonSeconds
            : 0f;
        locomotion.minTranslationPredictionWindow =
            referenceMinTranslationHorizonSeconds * scale;
        locomotion.maxTranslationPredictionWindow = selected;
        locomotion.minYawPredictionWindow = referenceMinYawHorizonSeconds * scale;
        locomotion.maxYawPredictionWindow = referenceMaxYawHorizonSeconds * scale;
    }

    void UpdateCalibrationProxyAppearance()
    {
        if (locomotion == null)
        {
            return;
        }
        bool hideCoincidentProxy = activeDelayMilliseconds == 0
            && currentHorizonSeconds <= 1e-5f;
        locomotion.SetStateGhostAppearanceSuppressed(hideCoincidentProxy);
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
        int delayMilliseconds = GetCalibrationDelayForOrderPosition(
            participantSequence,
            delayOrderIndex);
        int delayOffset = GetCalibrationDelayLevelIndex(delayMilliseconds);
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

    static float RoundHorizon(float value, float minimum, float maximum, float step)
    {
        float safeMin = Mathf.Min(minimum, maximum);
        float safeMax = Mathf.Max(minimum, maximum);
        float safeStep = Mathf.Max(0.0001f, step);
        float clamped = Mathf.Clamp(value, safeMin, safeMax);
        float steps = Mathf.Round((clamped - safeMin) / safeStep);
        return Mathf.Clamp(safeMin + steps * safeStep, safeMin, safeMax);
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

    List<float> GetSelections(int delayMilliseconds)
    {
        switch (delayMilliseconds)
        {
            case 0:
                return selections0;
            case 250:
                return selections250;
            case 500:
                return selections500;
            case 750:
                return selections750;
            case 1000:
                return selections1000;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(delayMilliseconds),
                    delayMilliseconds,
                    "Unsupported Experiment 2 delay.");
        }
    }

    void SetSelectedHorizon(int delayMilliseconds, float value)
    {
        switch (delayMilliseconds)
        {
            case 0:
                selectedHorizon0Seconds = value;
                break;
            case 250:
                selectedHorizon250Seconds = value;
                break;
            case 500:
                selectedHorizon500Seconds = value;
                break;
            case 750:
                selectedHorizon750Seconds = value;
                break;
            case 1000:
                selectedHorizon1000Seconds = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(delayMilliseconds),
                    delayMilliseconds,
                    "Unsupported Experiment 2 delay.");
        }
    }

    public static int GetCalibrationDelayForOrderPosition(
        int participantSequence,
        int delayOrderIndex)
    {
        int rowCount = CalibrationDelayOrders.GetLength(0);
        int row = (Mathf.Max(1, participantSequence) - 1) % rowCount;
        int position = Mathf.Clamp(delayOrderIndex, 0, CalibrationDelayCount - 1);
        return CalibrationDelayLevelsMilliseconds[CalibrationDelayOrders[row, position]];
    }

    public static int GetCalibrationDelayLevelMilliseconds(int delayIndex)
    {
        return CalibrationDelayLevelsMilliseconds[
            Mathf.Clamp(delayIndex, 0, CalibrationDelayCount - 1)];
    }

    public static int GetCalibrationDelayLevelIndex(int delayMilliseconds)
    {
        for (int i = 0; i < CalibrationDelayLevelsMilliseconds.Length; i++)
        {
            if (CalibrationDelayLevelsMilliseconds[i] == delayMilliseconds)
            {
                return i;
            }
        }
        return -1;
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

    string GetDelayOrderString(int sequenceNumber)
    {
        StringBuilder builder = new StringBuilder(20);
        for (int i = 0; i < CalibrationDelayCount; i++)
        {
            if (i > 0)
            {
                builder.Append('>');
            }
            builder.Append(GetCalibrationDelayForOrderPosition(
                sequenceNumber,
                i));
        }
        return builder.ToString();
    }

    void UpdateDelayOrderPreview()
    {
        delayOrderPreview = TryResolveParticipantSequenceNumber(out int sequenceNumber)
            ? GetDelayOrderString(sequenceNumber)
            : "Invalid participant ID";
    }

    string BuildCalibrationStatus(string prefix)
    {
        return $"{prefix} {activeDelayMilliseconds} ms calibration run {activeRunNumber} "
            + $"({activeAnchor}), Hmax={currentHorizonSeconds:0.0} s. "
            + $"Use [{decreaseHorizonKey}] / [{increaseHorizonKey}], then [{acceptCalibrationKey}].";
    }

    void ResetRunMetrics()
    {
        metricsLastSampleAt = Time.unscaledTime;
        metricDuration = 0f;
        latestSpeed = 0f;
        speedIntegral = 0f;
        maxSpeed = 0f;
        delayedToRealTimeIntegral = 0f;
        maxDelayedToRealTime = 0f;
        realTimeToProxyIntegral = 0f;
        maxRealTimeToProxy = 0f;
        delayedToProxyIntegral = 0f;
        maxDelayedToProxy = 0f;
        latestDelayedToRealTime = float.NaN;
        latestRealTimeToProxy = float.NaN;
        latestDelayedToProxy = float.NaN;
        hasLastRealTimePosition = locomotion != null;
        lastRealTimePosition = locomotion != null
            ? locomotion.RealTimeDronePosition
            : Vector3.zero;
        UpdateSeparationSnapshot();
    }

    void UpdateRunMetrics()
    {
        if (locomotion == null)
        {
            return;
        }
        float now = Time.unscaledTime;
        float dt = Mathf.Max(0f, now - metricsLastSampleAt);
        Vector3 currentPosition = locomotion.RealTimeDronePosition;
        float speed = hasLastRealTimePosition && dt > 1e-5f
            ? Vector3.Distance(lastRealTimePosition, currentPosition) / dt
            : 0f;
        UpdateSeparationSnapshot();
        if (dt > 0f)
        {
            metricDuration += dt;
            speedIntegral += speed * dt;
            delayedToRealTimeIntegral += latestDelayedToRealTime * dt;
            realTimeToProxyIntegral += latestRealTimeToProxy * dt;
            delayedToProxyIntegral += latestDelayedToProxy * dt;
        }
        latestSpeed = speed;
        maxSpeed = Mathf.Max(maxSpeed, speed);
        maxDelayedToRealTime = Mathf.Max(maxDelayedToRealTime, latestDelayedToRealTime);
        maxRealTimeToProxy = Mathf.Max(maxRealTimeToProxy, latestRealTimeToProxy);
        maxDelayedToProxy = Mathf.Max(maxDelayedToProxy, latestDelayedToProxy);
        metricsLastSampleAt = now;
        lastRealTimePosition = currentPosition;
        hasLastRealTimePosition = true;
    }

    void UpdateSeparationSnapshot()
    {
        if (locomotion == null)
        {
            return;
        }
        Vector3 realTime = locomotion.RealTimeDronePosition;
        Vector3 delayed = locomotion.droneBodyAvatar != null
            ? locomotion.droneBodyAvatar.position
            : realTime;
        Vector3 proxy = locomotion.stateGhostAvatar != null
            ? locomotion.stateGhostAvatar.position
            : locomotion.GhostPosition;
        latestDelayedToRealTime = Vector3.Distance(delayed, realTime);
        latestRealTimeToProxy = Vector3.Distance(realTime, proxy);
        latestDelayedToProxy = Vector3.Distance(delayed, proxy);
    }

    float RunMean(float integral)
    {
        return metricDuration > 1e-5f ? integral / metricDuration : float.NaN;
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
        UpdateSeparationSnapshot();
        calibrationEvents.Add(new CalibrationEventRecord
        {
            protocolVersion = ProtocolVersion,
            timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            eventType = eventType,
            participantId = string.IsNullOrWhiteSpace(calibratedParticipantId)
                ? participantId?.Trim()
                : calibratedParticipantId.Trim(),
            participantSequence = participantSequence,
            delayMilliseconds = delayMilliseconds,
            runNumber = runNumber,
            anchor = anchor,
            elapsedSeconds = elapsedSeconds,
            horizonSeconds = horizonSeconds,
            effectiveTranslationHorizonSeconds = locomotion != null
                ? locomotion.CurrentTranslationPredictionWindow
                : float.NaN,
            predictionConfidence = locomotion != null
                ? locomotion.CurrentTranslationPredictionConfidence
                : float.NaN,
            instantaneousSpeedMps = latestSpeed,
            delayedToRealTimeDistanceM = latestDelayedToRealTime,
            realTimeToProxyDistanceM = latestRealTimeToProxy,
            delayedToProxyDistanceM = latestDelayedToProxy,
            runMeanSpeedMps = RunMean(speedIntegral),
            runMaxSpeedMps = maxSpeed,
            runMeanDelayedToRealTimeDistanceM = RunMean(delayedToRealTimeIntegral),
            runMaxDelayedToRealTimeDistanceM = maxDelayedToRealTime,
            runMeanRealTimeToProxyDistanceM = RunMean(realTimeToProxyIntegral),
            runMaxRealTimeToProxyDistanceM = maxRealTimeToProxy,
            runMeanDelayedToProxyDistanceM = RunMean(delayedToProxyIntegral),
            runMaxDelayedToProxyDistanceM = maxDelayedToProxy,
            selectedHorizon0Seconds = selectedHorizon0Seconds,
            selectedHorizon250Seconds = selectedHorizon250Seconds,
            selectedHorizon500Seconds = selectedHorizon500Seconds,
            selectedHorizon750Seconds = selectedHorizon750Seconds,
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
        csv.AppendLine(
            "protocol_version,timestamp,event_type,participant_id,participant_sequence,delay_ms,"
            + "run_number,start_anchor,elapsed_s,horizon_s,effective_translation_horizon_s,"
            + "prediction_confidence,instantaneous_speed_mps,delayed_to_realtime_distance_m,"
            + "realtime_to_proxy_distance_m,delayed_to_proxy_distance_m,run_mean_speed_mps,"
            + "run_max_speed_mps,run_mean_delayed_to_realtime_distance_m,"
            + "run_max_delayed_to_realtime_distance_m,run_mean_realtime_to_proxy_distance_m,"
            + "run_max_realtime_to_proxy_distance_m,run_mean_delayed_to_proxy_distance_m,"
            + "run_max_delayed_to_proxy_distance_m,selected_horizon_0_s,"
            + "selected_horizon_250_s,selected_horizon_500_s,selected_horizon_750_s,"
            + "selected_horizon_1000_s,note");
        for (int i = 0; i < calibrationEvents.Count; i++)
        {
            CalibrationEventRecord item = calibrationEvents[i];
            AppendCsv(csv, item.protocolVersion);
            AppendCsv(csv, item.timestamp);
            AppendCsv(csv, item.eventType);
            AppendCsv(csv, item.participantId);
            AppendCsv(csv, item.participantSequence.ToString(CultureInfo.InvariantCulture));
            AppendCsv(csv, item.delayMilliseconds >= 0
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
            AppendCsvFloat(csv, item.effectiveTranslationHorizonSeconds);
            AppendCsvFloat(csv, item.predictionConfidence);
            AppendCsvFloat(csv, item.instantaneousSpeedMps);
            AppendCsvFloat(csv, item.delayedToRealTimeDistanceM);
            AppendCsvFloat(csv, item.realTimeToProxyDistanceM);
            AppendCsvFloat(csv, item.delayedToProxyDistanceM);
            AppendCsvFloat(csv, item.runMeanSpeedMps);
            AppendCsvFloat(csv, item.runMaxSpeedMps);
            AppendCsvFloat(csv, item.runMeanDelayedToRealTimeDistanceM);
            AppendCsvFloat(csv, item.runMaxDelayedToRealTimeDistanceM);
            AppendCsvFloat(csv, item.runMeanRealTimeToProxyDistanceM);
            AppendCsvFloat(csv, item.runMaxRealTimeToProxyDistanceM);
            AppendCsvFloat(csv, item.runMeanDelayedToProxyDistanceM);
            AppendCsvFloat(csv, item.runMaxDelayedToProxyDistanceM);
            AppendCsvOptionalHorizon(csv, item.selectedHorizon0Seconds);
            AppendCsvOptionalHorizon(csv, item.selectedHorizon250Seconds);
            AppendCsvOptionalHorizon(csv, item.selectedHorizon500Seconds);
            AppendCsvOptionalHorizon(csv, item.selectedHorizon750Seconds);
            AppendCsvOptionalHorizon(csv, item.selectedHorizon1000Seconds);
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
            lastSaveError = string.Empty;
        }
        catch (Exception exception)
        {
            lastSaveError = exception.Message;
            TryDeleteFile(stagingPath);
            Debug.LogError(
                $"[PredictiveFlyExperiment2Controller] Experiment 2 CSV could not be saved: {exception}",
                this);
        }
    }

    static void AppendCsvOptionalHorizon(StringBuilder builder, float value)
    {
        AppendCsv(builder, value < 0f
            ? string.Empty
            : value.ToString("0.000", CultureInfo.InvariantCulture));
    }

    static void AppendCsvFloat(StringBuilder builder, float value)
    {
        AppendCsv(
            builder,
            float.IsNaN(value) || float.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.000", CultureInfo.InvariantCulture));
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

    static bool IsKeyDown(KeyCode key)
    {
        return key != KeyCode.None && Input.GetKeyDown(key);
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
        state = CalibrationState.Error;
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
        locomotion?.SetLocomotionInputEnabled(false);
        state = CalibrationState.Error;
        status = message;
        Debug.LogError($"[PredictiveFlyExperiment2Controller] {message}", this);
    }

    struct CalibrationEventRecord
    {
        public string protocolVersion;
        public string timestamp;
        public string eventType;
        public string participantId;
        public int participantSequence;
        public int delayMilliseconds;
        public int runNumber;
        public string anchor;
        public float elapsedSeconds;
        public float horizonSeconds;
        public float effectiveTranslationHorizonSeconds;
        public float predictionConfidence;
        public float instantaneousSpeedMps;
        public float delayedToRealTimeDistanceM;
        public float realTimeToProxyDistanceM;
        public float delayedToProxyDistanceM;
        public float runMeanSpeedMps;
        public float runMaxSpeedMps;
        public float runMeanDelayedToRealTimeDistanceM;
        public float runMaxDelayedToRealTimeDistanceM;
        public float runMeanRealTimeToProxyDistanceM;
        public float runMaxRealTimeToProxyDistanceM;
        public float runMeanDelayedToProxyDistanceM;
        public float runMaxDelayedToProxyDistanceM;
        public float selectedHorizon0Seconds;
        public float selectedHorizon250Seconds;
        public float selectedHorizon500Seconds;
        public float selectedHorizon750Seconds;
        public float selectedHorizon1000Seconds;
        public string note;
    }
}
