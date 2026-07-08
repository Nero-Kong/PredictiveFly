using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// Direct head-offset locomotion with an always-visible drone body and optional
/// transparent state ghosts. The ghosts preview intent; they do not drive the XR rig.
/// </summary>
[DisallowMultipleComponent]
public class PredictiveGhostAvatarLocomotion : MonoBehaviour
{
    public enum PredictionMethod
    {
        AccelerationJerkLimited,
        KalmanVelocityEstimator
    }

    public enum GhostVisualizationMode
    {
        RealTimeDroneBody,
        DelayedDroneBody,
        DelayedWithRealTimeGhost,
        DelayedWithPredictiveGhost
    }

    public enum OrientationControlMode
    {
        Dynamic,
        Static,
        Coupled
    }

    public enum PlanarReferenceMode
    {
        InitialHeadPosition,
        BodyAnchor
    }

    [Header("Rig / HMD")]
    [Tooltip("Locomotion root moved by the direct embodied command. Defaults to this object.")]
    public Transform targetRig;
    [Tooltip("Tracked HMD/camera transform. Defaults to Camera.main.")]
    public Transform head;

    [Header("Drone Body / State Ghost")]
    [Tooltip("Opaque drone body transform. It is always shown and kept at the XR rig pose.")]
    [FormerlySerializedAs("ghostAvatar")]
    public Transform droneBodyAvatar;
    [Tooltip("If true, the transparent state ghost snaps to its target pose each frame. Disable for a calmer visual preview.")]
    public bool snapGhostToPrediction;
    [Tooltip("Transparent state ghost position response when snap is disabled. Higher = more immediate.")]
    public float ghostPositionResponse = 18f;
    [Tooltip("Transparent state ghost yaw response when snap is disabled. Higher = more immediate.")]
    public float ghostYawResponse = 18f;

    [Header("Drone State Modes")]
    [Tooltip("Runtime condition. Number keys 1-4 switch these modes in Play Mode.")]
    public GhostVisualizationMode visualizationMode = GhostVisualizationMode.DelayedWithPredictiveGhost;
    [FormerlySerializedAs("firstPersonOnlyKey")]
    public KeyCode realTimeDroneBodyKey = KeyCode.Alpha1;
    [FormerlySerializedAs("currentGhostKey")]
    public KeyCode delayedDroneBodyKey = KeyCode.Alpha2;
    [FormerlySerializedAs("predictiveGhostKey")]
    public KeyCode delayedRealTimeGhostKey = KeyCode.Alpha3;
    [FormerlySerializedAs("combinedGhostKey")]
    public KeyCode delayedPredictiveGhostKey = KeyCode.Alpha4;
    [Tooltip("Optional transparent state ghost. If empty, a runtime copy of Drone Body Avatar is created when mode 3 or 4 is used.")]
    [FormerlySerializedAs("currentGhostAvatar")]
    public Transform stateGhostAvatar;
    [Tooltip("Create a transparent state ghost copy automatically from Drone Body Avatar when needed.")]
    [FormerlySerializedAs("autoCreateCurrentGhost")]
    public bool autoCreateStateGhost = true;
    [FormerlySerializedAs("currentGhostAlpha")]
    [Range(0f, 1f)] public float droneBodyAlpha = 1f;
    [FormerlySerializedAs("predictiveGhostAlpha")]
    [Range(0f, 1f)] public float stateGhostAlpha = 0.28f;
    [FormerlySerializedAs("predictiveGhostAlphaPulse")]
    [Min(0f)] public float stateGhostAlphaPulse = 0.04f;

    [Header("Input-To-Rig Delay")]
    [Tooltip("Delay only the direct human locomotion command before it moves the XR rig. The predictive ghost stays real-time.")]
    public bool enableInputToRigDelay;
    [Tooltip("Latency in milliseconds between human body input and XR rig motion output.")]
    [Min(0)] public int inputToRigDelayMilliseconds = 500;
    [Tooltip("Drive delayed modes from the collision-constrained real-time drone pose history. This keeps the body and ghosts on one feasible trajectory after wall contact.")]
    public bool useCollisionConsistentStateDelay = true;

    [FormerlySerializedAs("enableCameraMotionDelay")]
    [SerializeField, HideInInspector] bool legacyEnableCameraMotionDelay;
    [FormerlySerializedAs("cameraMotionDelaySeconds")]
    [SerializeField, HideInInspector] float legacyCameraMotionDelaySeconds = -1f;
    [SerializeField, HideInInspector] bool legacyCameraMotionDelayMigrated;

    [Header("Rig Collision Blocking")]
    [Tooltip("Before moving the XR rig, sphere-cast a camera/probe volume and stop at scene geometry such as the transparent tube wall.")]
    public bool enableSoftCollisionBlocking = true;
    [Tooltip("Optional probe transform used for collision blocking. If empty, the HMD/head transform is used.")]
    public Transform collisionProbe;
    [Tooltip("Use the HMD/head as the default blocking probe so the camera gets stuck before entering the wall.")]
    public bool useHeadAsCollisionProbe = true;
    [Min(0.01f)] public float collisionProbeRadius = 0.22f;
    [Min(0f)] public float collisionSkinWidth = 0.03f;
    public LayerMask collisionBlockLayers = ~0;
    public QueryTriggerInteraction collisionQueryTriggerInteraction = QueryTriggerInteraction.Ignore;
    [Tooltip("Optional exact tube boundary. If empty, the script can find the generated IrairaBou tube at runtime.")]
    public IrairaBouTubeBoundary tubeBoundary;
    public bool autoFindTubeBoundary = true;
    [Tooltip("Apply the same scene/tube reachability constraint to the real-time and predictive state ghosts.")]
    public bool constrainStateGhostToReachableSpace = true;
    [Tooltip("Number of small collision-checked steps used when placing the predictive ghost into the future.")]
    [Range(1, 32)] public int predictiveGhostCollisionSteps = 10;

    [Header("Prediction")]
    [Tooltip("Prediction estimator used for the future ghost. This can be changed live in Play Mode.")]
    public PredictionMethod predictionMethod = PredictionMethod.AccelerationJerkLimited;
    [Tooltip("Press this key in Play Mode to cycle prediction methods. Set to None to disable.")]
    public KeyCode switchPredictionMethodKey = KeyCode.P;
    [Tooltip("Reset estimator history when changing prediction method to avoid stale velocity state carrying across methods.")]
    public bool resetEstimatorOnMethodSwitch = true;
    [Tooltip("Minimum forward lookahead for translation when input has just started or is unstable.")]
    public float minTranslationPredictionWindow = 0.08f;
    [Tooltip("Maximum forward lookahead for translation once input is stable.")]
    public float maxTranslationPredictionWindow = 0.45f;
    [Tooltip("Minimum forward lookahead for yaw when input has just started or is unstable.")]
    public float minYawPredictionWindow = 0.05f;
    [Tooltip("Maximum forward lookahead for yaw once input is stable.")]
    public float maxYawPredictionWindow = 0.2f;
    [Tooltip("Hard clamp on how far ahead the ghost can be in one prediction step.")]
    public float maxPredictionDistance = 3f;
    [Tooltip("Use smoothed planar velocity for ghost prediction. Disable for more immediate ghost placement.")]
    public bool useSmoothedPlanarVelocityForPrediction;
    [Tooltip("Time required for sustained input to reach full lookahead confidence.")]
    public float predictionRampTime = 0.3f;
    [Tooltip("Direction change above this angle will strongly shorten the prediction window.")]
    public float predictionDirectionStabilityAngleDeg = 55f;
    [Tooltip("Normalized speed needed to earn full translation lookahead confidence.")]
    [Range(0.05f, 1f)] public float fullPredictionAtSpeedFraction = 0.45f;
    [Tooltip("How quickly prediction windows expand or collapse toward their targets.")]
    public float predictionWindowResponse = 10f;
    [Tooltip("Command-space acceleration cap for prediction velocity filtering.")]
    public float maxPredictedLinearAcceleration = 12f;
    [Tooltip("Command-space jerk cap for prediction velocity filtering.")]
    public float maxPredictedLinearJerk = 48f;
    [Tooltip("Yaw acceleration cap for prediction filtering in deg/s^2.")]
    public float maxPredictedYawAccelerationDeg = 220f;
    [Tooltip("Yaw jerk cap for prediction filtering in deg/s^3.")]
    public float maxPredictedYawJerkDeg = 720f;

    [Header("Kalman Prediction")]
    [HideInInspector]
    [Tooltip("Legacy serialized toggle. Prefer Prediction Method.")]
    public bool useKalmanPrediction;
    [Tooltip("Process noise for linear velocity prediction. Higher values react faster but trust noisy input more.")]
    [Min(0f)] public float kalmanLinearProcessNoise = 8f;
    [Tooltip("Measurement noise for linear velocity commands. Higher values smooth more but add lag.")]
    [Min(0.0001f)] public float kalmanLinearMeasurementNoise = 0.18f;
    [Tooltip("Process noise for yaw-rate prediction. Higher values react faster but trust noisy yaw input more.")]
    [Min(0f)] public float kalmanYawProcessNoise = 600f;
    [Tooltip("Measurement noise for yaw-rate commands. Higher values smooth more but add lag.")]
    [Min(0.0001f)] public float kalmanYawMeasurementNoise = 36f;

    [Header("Planar Translation")]
    [Tooltip("Maximum planar speed in m/s.")]
    public float horizontalSpeed = 5f;
    [Tooltip("Ignore small lean around center.")]
    public float planarDeadZone = 0.02f;
    [Tooltip("Lean magnitude corresponding to max planar speed.")]
    public float planarMaxOffset = 0.14f;
    [Tooltip("1 = linear, >1 softer near center.")]
    public float planarResponseExponent = 1.2f;
    [Tooltip("0 = use lean direction only, 1 = use head forward only.")]
    [Range(0f, 1f)] public float headDirectionBlend = 0.2f;
    [Tooltip("Planar velocity smoothing (1/s).")]
    public float planarSmoothing = 10f;

    [Header("Body Reference")]
    [Tooltip("InitialHeadPosition uses the calibrated HMD position. BodyAnchor uses the current HMD-to-body-anchor offset, so stance drift and small steps do not become locomotion input.")]
    public PlanarReferenceMode planarReferenceMode = PlanarReferenceMode.BodyAnchor;
    [Tooltip("Provider for the waist/body anchor pose. Defaults to a BodyAnchorProvider on this object, adding one at runtime if needed.")]
    public BodyAnchorProvider bodyAnchorProvider;
    [Tooltip("Create a BodyAnchorProvider at runtime if body reference mode is enabled and no provider is assigned.")]
    public bool autoCreateBodyAnchorProvider = true;
    [Tooltip("Use the original initial-head-position reference while the body anchor is unavailable.")]
    public bool fallbackToInitialHeadReference;
    [Tooltip("Compute planar lean in the current body-yaw frame so whole-body turns do not become movement input.")]
    public bool normalizePlanarOffsetByBodyYaw = true;
    [Tooltip("Experimental: compute yaw control from HMD yaw relative to body yaw. Keep off until the body yaw source is stable; planar translation can still use the body anchor.")]
    public bool useBodyRelativeHeadYaw;

    [Header("Orientation & Vertical")]
    public OrientationControlMode orientationMode = OrientationControlMode.Dynamic;
    [Tooltip("Maximum vertical speed for dynamic mode (m/s).")]
    public float verticalSpeed = 10f;
    [Tooltip("Ignore tiny vertical commands near level gaze. Prevents idle head pitch noise from activating prediction.")]
    public float verticalCommandDeadZone = 0.05f;
    [Tooltip("Maximum yaw rate for dynamic mode (deg/s).")]
    public float yawSpeed = 180f;
    public float staticYawThresholdDeg = 30f;
    public float staticYawSpeedDeg = 60f;
    public float staticPitchUpThresholdDeg = 30f;
    public float staticPitchDownThresholdDeg = 30f;
    public float staticPitchUpSpeed = 5f;
    public float staticPitchDownSpeed = 5f;
    public float coupledYawMaxSpeedDeg = 200f;
    public float coupledYawGain = 4f;
    public float coupledPitchMaxSpeed = 5f;
    public float coupledPitchReferenceDeg = 30f;

    [Header("Bounds / Recenter")]
    public Vector3 positionLimits = 2000f * Vector3.one;
    public KeyCode recenterKey = KeyCode.C;
    [Tooltip("Also reset the locomotion root to its initial pose on recenter.")]
    public bool resetTargetPoseOnRecenter = true;

    [Header("Convergence Feedback")]
    [Tooltip("Distance threshold for treating rig and predictive ghost as converged. Used for debug/events only.")]
    public float convergenceDistance = 0.05f;
    [Tooltip("Yaw threshold for convergence in degrees. Used for debug/events only.")]
    public float convergenceYawThresholdDeg = 3f;
    public UnityEvent onConverged = new UnityEvent();
    public UnityEvent onSeparated = new UnityEvent();

    [Header("Debug")]
    [SerializeField] bool isConverged = true;
    [SerializeField] bool hasActiveInput;
    [SerializeField] Vector3 ghostPosition;
    [SerializeField] float ghostYawDeg;
    [SerializeField] Vector3 visualGhostPosition;
    [SerializeField] float visualGhostYawDeg;
    [SerializeField] float distanceToGhost;
    [SerializeField] float currentTranslationPredictionWindow;
    [SerializeField] float currentYawPredictionWindow;
    [SerializeField] float currentTranslationPredictionConfidence;
    [SerializeField] float currentYawPredictionConfidence;
    [SerializeField] Vector3 currentCenterOffsetLocal;
    [SerializeField] Vector3 currentPredictionVelocityLocal;
    [SerializeField] Vector3 stateGhostOffsetLocal;
    [SerializeField] float currentHeadPitchDeg;
    [SerializeField] float currentVerticalCommand;
    [SerializeField] bool debugHasBodyAnchor;
    [SerializeField] bool debugBodyAnchorUsesYaw;
    [SerializeField] bool debugUsingInitialHeadReferenceFallback;
    [SerializeField] Vector3 debugBodyLocalHeadOffset;
    [SerializeField] Vector3 debugBodyLocalMoveOffset;
    [SerializeField] Vector3 debugPlanarSignalPointLocal;
    [SerializeField] Vector3 debugPlanarSignalAnchorLocal;
    [SerializeField] float debugBodyRelativeHeadYawDeg;
    [SerializeField] float debugBodyRelativeHeadYawDeltaDeg;
    [SerializeField] Vector3 realTimeDronePosition;
    [SerializeField] float realTimeDroneYawDeg;
    [SerializeField] PredictionMethod activePredictionMethod;
    [SerializeField] GhostVisualizationMode activeVisualizationMode;
    [SerializeField] bool inputToRigDelayBufferReady;
    [SerializeField] float activeInputToRigDelayMilliseconds;
    [SerializeField] bool rigMovementBlocked;
    [SerializeField] string rigMovementBlockedBy;
    [SerializeField] bool stateGhostMovementBlocked;
    [SerializeField] string stateGhostMovementBlockedBy;

    Vector3 centerHeadLocal;
    Vector3 neutralBodyLocalHeadOffset;
    Vector3 smoothedPlanarVelocityLocal;
    Vector3 filteredPredictionVelocityWorld;
    Vector3 filteredPredictionAccelerationWorld;
    float filteredPredictionYawRateDeg;
    float filteredPredictionYawAccelerationDeg;
    Vector3 initialTargetPosition;
    Quaternion initialTargetRotation = Quaternion.identity;
    bool hasInitialTargetPose;
    bool hasCenterBodyAnchor;
    bool centerBodyAnchorUsesYaw;
    bool hasNeutralBodyRelativeHeadYaw;
    float neutralBodyRelativeHeadYawDeg;
    bool hasGhostPose;
    bool hasVisualGhostPose;
    bool hasRealTimeDronePose;
    bool warnedSetup;
    float sustainedInputTime;
    Vector3 lastRawPlanarDirectionWorld;
    bool hasLastRawPlanarDirection;
    float lastRawYawRateDeg;
    Kalman1D kalmanVelocityX;
    Kalman1D kalmanVelocityY;
    Kalman1D kalmanVelocityZ;
    Kalman1D kalmanYawRateDeg;
    PredictionMethod lastPredictionMethod;
    GhostVisualizationMode lastVisualizationMode;
    GhostAvatarAnimationDriver droneBodyDriver;
    GhostAvatarAnimationDriver stateGhostDriver;
    bool createdRuntimeStateGhost;
    readonly List<DelayedCommandSample> delayedDirectCommands = new List<DelayedCommandSample>();
    readonly List<DronePoseSample> realTimeDronePoseHistory = new List<DronePoseSample>();
    readonly RaycastHit[] rigCollisionHits = new RaycastHit[32];

    public bool IsConverged => isConverged;
    public Vector3 GhostPosition => visualGhostPosition;
    public Quaternion GhostRotation => Quaternion.Euler(0f, visualGhostYawDeg, 0f);
    public bool HasRealTimeDronePose => hasRealTimeDronePose;
    public Vector3 RealTimeDronePosition => hasRealTimeDronePose
        ? realTimeDronePosition
        : targetRig != null ? targetRig.position : transform.position;
    public Quaternion RealTimeDroneRotation => Quaternion.Euler(
        0f,
        hasRealTimeDronePose
            ? realTimeDroneYawDeg
            : targetRig != null ? targetRig.eulerAngles.y : transform.eulerAngles.y,
        0f);
    public float RealTimeDroneYawDeg => hasRealTimeDronePose
        ? realTimeDroneYawDeg
        : targetRig != null ? targetRig.eulerAngles.y : transform.eulerAngles.y;
    public Vector3 CurrentCenterOffsetLocal => currentCenterOffsetLocal;
    public Vector3 CurrentPredictionVelocityLocal => currentPredictionVelocityLocal;
    public float CurrentHeadPitchDeg => currentHeadPitchDeg;
    public float CurrentVerticalCommand => currentVerticalCommand;
    public float CurrentTranslationPredictionWindow => currentTranslationPredictionWindow;
    public float CurrentYawPredictionWindow => currentYawPredictionWindow;
    public float CurrentTranslationPredictionConfidence => currentTranslationPredictionConfidence;
    public float CurrentYawPredictionConfidence => currentYawPredictionConfidence;
    public bool IsRigMovementBlocked => rigMovementBlocked;
    public string RigMovementBlockedBy => rigMovementBlockedBy;
    public bool IsStateGhostMovementBlocked => stateGhostMovementBlocked;
    public string StateGhostMovementBlockedBy => stateGhostMovementBlockedBy;

    void Awake()
    {
        MigrateLegacyInputToRigDelay();

        if (targetRig == null)
        {
            targetRig = transform;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        ResolveBodyAnchorProvider();
    }

    void OnValidate()
    {
        MigrateLegacyInputToRigDelay();
        inputToRigDelayMilliseconds = Mathf.Max(0, inputToRigDelayMilliseconds);
        predictiveGhostCollisionSteps = Mathf.Clamp(predictiveGhostCollisionSteps, 1, 32);
        activePredictionMethod = predictionMethod;
        activeVisualizationMode = visualizationMode;
    }

    void OnEnable()
    {
        MigrateLegacyInputToRigDelay();
        MigrateLegacyPredictionToggle();
        lastPredictionMethod = predictionMethod;
        lastVisualizationMode = visualizationMode;
        SyncPredictionMethodDebugState();
        SyncVisualizationModeDebugState();
        RefreshTubeBoundaryReference();
        ResolveBodyAnchorProvider();
        WarnAboutConflicts();
        CacheInitialTargetPose();
        CaptureCenter();
        ResetMotionState();
        SnapGhostToRig();
        EnsureGhostModeReferences();
        ApplyVisualizationMode(force: true);
        UpdateConvergenceState(forceNotify: false);
    }

    void Update()
    {
        if (targetRig == null || head == null)
        {
            return;
        }

        if (Input.GetKeyDown(recenterKey))
        {
            RecenterNow();
        }

        HandleVisualizationModeSwitchInput();
        ApplyVisualizationModeIfChanged();
        HandlePredictionMethodSwitchInput();
        ApplyPredictionMethodIfChanged();

        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        LocomotionCommand command = SampleCommand(dt);
        hasActiveInput = command.hasInput;
        stateGhostMovementBlocked = false;
        stateGhostMovementBlockedBy = string.Empty;

        UpdateRealTimeDronePose(command, dt);
        RecordRealTimeDronePose();

        if (ShouldUseCollisionConsistentStateDelay())
        {
            ApplyDelayedDroneBodyPoseFromRealTimeHistory();
        }
        else
        {
            RecordDelayedDirectCommand(command);
            LocomotionCommand directCommand = ResolveDelayedDirectCommand(command);
            ApplyDirectFirstPersonLocomotion(directCommand, dt);
        }

        SyncDroneBodyTransform();

        if (ShowsPredictiveStateGhost(visualizationMode))
        {
            UpdatePredictiveStateGhostPose(command, dt);
        }
        else if (ShowsRealTimeStateGhost(visualizationMode))
        {
            UpdateRealTimeStateGhostPose();
        }
        else
        {
            SetStateGhostPoseToRig(syncTransform: true);
        }

        SyncStateGhostTransform();
        UpdateConvergenceState(forceNotify: false);
    }

    void OnDestroy()
    {
        if (createdRuntimeStateGhost && stateGhostAvatar != null)
        {
            Destroy(stateGhostAvatar.gameObject);
            stateGhostAvatar = null;
            stateGhostDriver = null;
        }
    }

    [ContextMenu("Recenter Now")]
    public void RecenterNow()
    {
        if (resetTargetPoseOnRecenter && hasInitialTargetPose && targetRig != null)
        {
            targetRig.SetPositionAndRotation(initialTargetPosition, initialTargetRotation);
        }

        ResetMotionState();
        CaptureCenter();
        SnapGhostToRig();
        ApplyVisualizationMode(force: true);
        UpdateConvergenceState(forceNotify: false);
    }

    [ContextMenu("Snap Ghost To Rig")]
    public void SnapGhostToRig()
    {
        if (targetRig == null)
        {
            return;
        }

        SetStateGhostPoseToRig(syncTransform: true);
        SetRealTimeDronePoseToRig();
        SyncDroneBodyTransform();
        SyncStateGhostTransform();
    }

    [ContextMenu("Cycle Prediction Method")]
    public void CyclePredictionMethod()
    {
        predictionMethod = predictionMethod == PredictionMethod.AccelerationJerkLimited
            ? PredictionMethod.KalmanVelocityEstimator
            : PredictionMethod.AccelerationJerkLimited;

        ApplyPredictionMethodIfChanged(force: true);
    }

    public void SetPredictionMethod(PredictionMethod method)
    {
        predictionMethod = method;
        ApplyPredictionMethodIfChanged(force: true);
    }

    public void SetUseKalmanPrediction(bool enabled)
    {
        SetPredictionMethod(enabled
            ? PredictionMethod.KalmanVelocityEstimator
            : PredictionMethod.AccelerationJerkLimited);
    }

    public void SetVisualizationMode(GhostVisualizationMode mode)
    {
        visualizationMode = mode;
        ApplyVisualizationModeIfChanged(force: true);
    }

    void HandleVisualizationModeSwitchInput()
    {
        if (IsModeKeyDown(realTimeDroneBodyKey, KeyCode.Alpha1, KeyCode.Keypad1))
        {
            SetVisualizationMode(GhostVisualizationMode.RealTimeDroneBody);
        }
        else if (IsModeKeyDown(delayedDroneBodyKey, KeyCode.Alpha2, KeyCode.Keypad2))
        {
            SetVisualizationMode(GhostVisualizationMode.DelayedDroneBody);
        }
        else if (IsModeKeyDown(delayedRealTimeGhostKey, KeyCode.Alpha3, KeyCode.Keypad3))
        {
            SetVisualizationMode(GhostVisualizationMode.DelayedWithRealTimeGhost);
        }
        else if (IsModeKeyDown(delayedPredictiveGhostKey, KeyCode.Alpha4, KeyCode.Keypad4))
        {
            SetVisualizationMode(GhostVisualizationMode.DelayedWithPredictiveGhost);
        }
    }

    static bool IsModeKeyDown(KeyCode configuredKey, KeyCode numberRowKey, KeyCode keypadKey)
    {
        if (configuredKey == KeyCode.None)
        {
            return false;
        }

        if (Input.GetKeyDown(configuredKey))
        {
            return true;
        }

        return configuredKey == numberRowKey && Input.GetKeyDown(keypadKey);
    }

    void ApplyVisualizationModeIfChanged(bool force = false)
    {
        if (!force && visualizationMode == lastVisualizationMode)
        {
            SyncVisualizationModeDebugState();
            return;
        }

        bool wasShowingPredictiveGhost = ShowsPredictiveStateGhost(lastVisualizationMode);
        bool willShowPredictiveGhost = ShowsPredictiveStateGhost(visualizationMode);
        ResetInputToRigDelayState();
        SetRealTimeDronePoseToRig();

        if (!willShowPredictiveGhost)
        {
            SetStateGhostPoseToRig(syncTransform: true);
        }
        else if (!wasShowingPredictiveGhost || force)
        {
            ResetPredictionEstimatorState();
            SetStateGhostPoseToRig(syncTransform: true);
        }

        lastVisualizationMode = visualizationMode;
        SyncVisualizationModeDebugState();
        ApplyVisualizationMode(force: true);

        Debug.Log($"[PredictiveGhostAvatarLocomotion] Ghost visualization mode switched to {visualizationMode}.", this);
    }

    void ApplyVisualizationMode(bool force = false)
    {
        EnsureGhostModeReferences();

        bool showStateGhost = ShowsStateGhost(visualizationMode);

        if (showStateGhost)
        {
            EnsureStateGhostAvatar();
        }

        SetGhostAppearance(droneBodyAvatar, droneBodyDriver, true, droneBodyAlpha, 0f);
        SetGhostAppearance(stateGhostAvatar, stateGhostDriver, showStateGhost, stateGhostAlpha, stateGhostAlphaPulse);

        if (force)
        {
            SyncDroneBodyTransform();
            SyncStateGhostTransform();
        }
    }

    void EnsureGhostModeReferences()
    {
        droneBodyDriver = droneBodyAvatar == null
            ? null
            : droneBodyAvatar.GetComponent<GhostAvatarAnimationDriver>();
        stateGhostDriver = stateGhostAvatar == null
            ? null
            : stateGhostAvatar.GetComponent<GhostAvatarAnimationDriver>();

        if (droneBodyDriver != null)
        {
            droneBodyDriver.locomotion = this;
        }

        if (stateGhostDriver != null)
        {
            stateGhostDriver.locomotion = this;
            stateGhostDriver.autoFindLocomotion = false;
        }
    }

    void EnsureStateGhostAvatar()
    {
        if (stateGhostAvatar != null || !autoCreateStateGhost || droneBodyAvatar == null)
        {
            EnsureGhostModeReferences();
            return;
        }

        GameObject clone = Instantiate(droneBodyAvatar.gameObject, droneBodyAvatar.position, droneBodyAvatar.rotation);
        clone.name = $"{droneBodyAvatar.name}_TransparentState";
        clone.transform.SetParent(droneBodyAvatar.parent, true);

        stateGhostAvatar = clone.transform;
        createdRuntimeStateGhost = true;

        EnsureGhostModeReferences();
        SetGhostAppearance(stateGhostAvatar, stateGhostDriver, true, stateGhostAlpha, stateGhostAlphaPulse);
    }

    void SetGhostAppearance(Transform ghostRoot, GhostAvatarAnimationDriver driver, bool visible, float alpha, float alphaPulse)
    {
        if (ghostRoot == null)
        {
            return;
        }

        if (driver != null)
        {
            driver.SetGhostAlpha(alpha, alphaPulse);
            driver.SetGhostVisible(visible);
        }
        else
        {
            SetRenderersVisible(ghostRoot, visible);
        }
    }

    static void SetRenderersVisible(Transform root, bool visible)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = visible;
        }
    }

    void SetStateGhostPoseToRig(bool syncTransform)
    {
        if (targetRig == null)
        {
            return;
        }

        ghostPosition = targetRig.position;
        ghostYawDeg = targetRig.eulerAngles.y;
        visualGhostPosition = ghostPosition;
        visualGhostYawDeg = ghostYawDeg;
        hasGhostPose = true;
        hasVisualGhostPose = true;
        stateGhostOffsetLocal = Vector3.zero;

        if (syncTransform)
        {
            SyncStateGhostTransform();
        }
    }

    void SetRealTimeDronePoseToRig()
    {
        if (targetRig == null)
        {
            return;
        }

        realTimeDronePosition = targetRig.position;
        realTimeDroneYawDeg = targetRig.eulerAngles.y;
        hasRealTimeDronePose = true;
    }

    void SyncDroneBodyTransform()
    {
        if (droneBodyAvatar == null || targetRig == null)
        {
            return;
        }

        droneBodyAvatar.SetPositionAndRotation(
            targetRig.position,
            Quaternion.Euler(0f, targetRig.eulerAngles.y, 0f));
    }

    void SyncStateGhostTransform()
    {
        if (stateGhostAvatar == null)
        {
            return;
        }

        if (ShowsRealTimeStateGhost(visualizationMode))
        {
            stateGhostAvatar.SetPositionAndRotation(
                realTimeDronePosition,
                Quaternion.Euler(0f, realTimeDroneYawDeg, 0f));
            return;
        }

        if (ShowsPredictiveStateGhost(visualizationMode))
        {
            stateGhostAvatar.SetPositionAndRotation(
                visualGhostPosition,
                Quaternion.Euler(0f, visualGhostYawDeg, 0f));
        }
    }

    void UpdateRealTimeDronePose(LocomotionCommand command, float dt)
    {
        if (!hasRealTimeDronePose)
        {
            SetRealTimeDronePoseToRig();
        }

        Vector3 requestedPosition = ClampPosition(realTimeDronePosition + command.directWorldVelocity * dt, positionLimits);
        realTimeDronePosition = ResolveStateGhostReachablePosition(realTimeDronePosition, requestedPosition);
        realTimeDroneYawDeg += command.directYawRateDeg * dt;
    }

    void UpdateRealTimeStateGhostPose()
    {
        ghostPosition = realTimeDronePosition;
        ghostYawDeg = realTimeDroneYawDeg;
        visualGhostPosition = ghostPosition;
        visualGhostYawDeg = ghostYawDeg;
        hasGhostPose = true;
        hasVisualGhostPose = true;
        stateGhostOffsetLocal = targetRig != null
            ? targetRig.InverseTransformDirection(ghostPosition - targetRig.position)
            : Vector3.zero;
    }

    void ApplyDirectFirstPersonLocomotion(LocomotionCommand command, float dt)
    {
        Vector3 nextPosition = targetRig.position + command.directWorldVelocity * dt;
        float nextYaw = targetRig.eulerAngles.y + command.directYawRateDeg * dt;
        Vector3 constrainedPosition = ResolveSoftBlockedRigPosition(ClampPosition(nextPosition, positionLimits));

        targetRig.SetPositionAndRotation(
            constrainedPosition,
            Quaternion.Euler(0f, nextYaw, 0f));
    }

    bool ShouldUseCollisionConsistentStateDelay()
    {
        return useCollisionConsistentStateDelay
            && GetInputToRigDelaySeconds() > 0f
            && UsesDelayedRigInput(visualizationMode);
    }

    void RecordRealTimeDronePose()
    {
        float now = Time.time;
        realTimeDronePoseHistory.Add(new DronePoseSample
        {
            time = now,
            position = realTimeDronePosition,
            yawDeg = realTimeDroneYawDeg
        });

        PruneDelayBuffer(realTimeDronePoseHistory, now, GetInputToRigDelaySeconds());
    }

    void ApplyDelayedDroneBodyPoseFromRealTimeHistory()
    {
        float delay = GetInputToRigDelaySeconds();
        activeInputToRigDelayMilliseconds = delay * 1000f;
        inputToRigDelayBufferReady = delay <= 0f || HasDelayBufferReached(realTimeDronePoseHistory, delay);
        rigMovementBlocked = false;
        rigMovementBlockedBy = string.Empty;

        if (targetRig == null)
        {
            return;
        }

        if (delay <= 0f || realTimeDronePoseHistory.Count == 0)
        {
            targetRig.SetPositionAndRotation(
                realTimeDronePosition,
                Quaternion.Euler(0f, realTimeDroneYawDeg, 0f));
            return;
        }

        DronePoseSample delayedPose = SampleDelayedDronePose(realTimeDronePoseHistory, Time.time - delay);
        targetRig.SetPositionAndRotation(
            ClampPosition(delayedPose.position, positionLimits),
            Quaternion.Euler(0f, delayedPose.yawDeg, 0f));
    }

    void RecordDelayedDirectCommand(LocomotionCommand command)
    {
        float now = Time.time;
        delayedDirectCommands.Add(new DelayedCommandSample
        {
            time = now,
            command = command
        });

        PruneDelayBuffer(delayedDirectCommands, now, GetInputToRigDelaySeconds());
    }

    LocomotionCommand ResolveDelayedDirectCommand(LocomotionCommand fallbackCommand)
    {
        float delay = GetInputToRigDelaySeconds();
        activeInputToRigDelayMilliseconds = delay * 1000f;
        inputToRigDelayBufferReady = delay <= 0f || HasDelayBufferReached(delayedDirectCommands, delay);

        if (delay <= 0f || delayedDirectCommands.Count == 0)
        {
            return fallbackCommand;
        }

        float targetTime = Time.time - delay;
        return SampleDelayedCommand(delayedDirectCommands, targetTime);
    }

    float GetInputToRigDelaySeconds()
    {
        return enableInputToRigDelay && UsesDelayedRigInput(visualizationMode)
            ? Mathf.Max(0f, inputToRigDelayMilliseconds) * 0.001f
            : 0f;
    }

    void ResetInputToRigDelayState()
    {
        delayedDirectCommands.Clear();
        realTimeDronePoseHistory.Clear();
        activeInputToRigDelayMilliseconds = GetInputToRigDelaySeconds() * 1000f;
        inputToRigDelayBufferReady = activeInputToRigDelayMilliseconds <= 0f;

        float now = Time.time;
        delayedDirectCommands.Add(new DelayedCommandSample
        {
            time = now,
            command = default(LocomotionCommand)
        });

        if (targetRig != null)
        {
            realTimeDronePoseHistory.Add(new DronePoseSample
            {
                time = now,
                position = targetRig.position,
                yawDeg = targetRig.eulerAngles.y
            });
        }
    }

    static bool HasDelayBufferReached<T>(List<T> buffer, float delay) where T : IDelayedSample
    {
        return buffer.Count > 0 && Time.time - buffer[0].SampleTime >= delay;
    }

    static void PruneDelayBuffer<T>(List<T> buffer, float now, float delay) where T : IDelayedSample
    {
        float oldestNeededTime = now - Mathf.Max(delay + 1f, 1f);
        while (buffer.Count > 2 && buffer[1].SampleTime < oldestNeededTime)
        {
            buffer.RemoveAt(0);
        }
    }

    static LocomotionCommand SampleDelayedCommand(List<DelayedCommandSample> buffer, float targetTime)
    {
        if (buffer.Count == 1 || targetTime <= buffer[0].time)
        {
            return buffer[0].command;
        }

        for (int i = 1; i < buffer.Count; i++)
        {
            DelayedCommandSample next = buffer[i];
            if (targetTime > next.time)
            {
                continue;
            }

            DelayedCommandSample previous = buffer[i - 1];
            float span = Mathf.Max(1e-4f, next.time - previous.time);
            float t = Mathf.Clamp01((targetTime - previous.time) / span);
            return LerpCommand(previous.command, next.command, t);
        }

        return buffer[buffer.Count - 1].command;
    }

    static DronePoseSample SampleDelayedDronePose(List<DronePoseSample> buffer, float targetTime)
    {
        if (buffer.Count == 1 || targetTime <= buffer[0].time)
        {
            return buffer[0];
        }

        for (int i = 1; i < buffer.Count; i++)
        {
            DronePoseSample next = buffer[i];
            if (targetTime > next.time)
            {
                continue;
            }

            DronePoseSample previous = buffer[i - 1];
            float span = Mathf.Max(1e-4f, next.time - previous.time);
            float t = Mathf.Clamp01((targetTime - previous.time) / span);
            return LerpDronePose(previous, next, t);
        }

        return buffer[buffer.Count - 1];
    }

    static DronePoseSample LerpDronePose(DronePoseSample a, DronePoseSample b, float t)
    {
        return new DronePoseSample
        {
            time = Mathf.Lerp(a.time, b.time, t),
            position = Vector3.Lerp(a.position, b.position, t),
            yawDeg = Mathf.LerpAngle(a.yawDeg, b.yawDeg, t)
        };
    }

    static LocomotionCommand LerpCommand(LocomotionCommand a, LocomotionCommand b, float t)
    {
        return new LocomotionCommand
        {
            rawWorldVelocity = Vector3.Lerp(a.rawWorldVelocity, b.rawWorldVelocity, t),
            directWorldVelocity = Vector3.Lerp(a.directWorldVelocity, b.directWorldVelocity, t),
            predictionWorldVelocity = Vector3.Lerp(a.predictionWorldVelocity, b.predictionWorldVelocity, t),
            rawYawRateDeg = Mathf.Lerp(a.rawYawRateDeg, b.rawYawRateDeg, t),
            directYawRateDeg = Mathf.Lerp(a.directYawRateDeg, b.directYawRateDeg, t),
            predictionYawRateDeg = Mathf.Lerp(a.predictionYawRateDeg, b.predictionYawRateDeg, t),
            translationPredictionWindow = Mathf.Lerp(a.translationPredictionWindow, b.translationPredictionWindow, t),
            yawPredictionWindow = Mathf.Lerp(a.yawPredictionWindow, b.yawPredictionWindow, t),
            hasInput = t < 0.5f ? a.hasInput : b.hasInput
        };
    }

    static bool UsesDelayedRigInput(GhostVisualizationMode mode)
    {
        return mode != GhostVisualizationMode.RealTimeDroneBody;
    }

    static bool ShowsStateGhost(GhostVisualizationMode mode)
    {
        return ShowsRealTimeStateGhost(mode) || ShowsPredictiveStateGhost(mode);
    }

    static bool ShowsRealTimeStateGhost(GhostVisualizationMode mode)
    {
        return mode == GhostVisualizationMode.DelayedWithRealTimeGhost;
    }

    static bool ShowsPredictiveStateGhost(GhostVisualizationMode mode)
    {
        return mode == GhostVisualizationMode.DelayedWithPredictiveGhost;
    }

    void SyncVisualizationModeDebugState()
    {
        activeVisualizationMode = visualizationMode;
    }

    void CacheInitialTargetPose()
    {
        if (targetRig == null)
        {
            hasInitialTargetPose = false;
            return;
        }

        initialTargetPosition = targetRig.position;
        initialTargetRotation = targetRig.rotation;
        hasInitialTargetPose = true;
    }

    void CaptureCenter()
    {
        if (targetRig == null || head == null)
        {
            centerHeadLocal = Vector3.zero;
            hasCenterBodyAnchor = false;
            return;
        }

        centerHeadLocal = targetRig.InverseTransformPoint(head.position);
        hasCenterBodyAnchor = false;
        hasNeutralBodyRelativeHeadYaw = false;

        if (planarReferenceMode == PlanarReferenceMode.BodyAnchor
            && TryGetBodyAnchorLocalPose(out Pose bodyAnchorLocalPose))
        {
            Vector3 bodyReferencedHeadOffset = ComputeBodyReferencedOffsetLocal(
                centerHeadLocal,
                bodyAnchorLocalPose,
                out bool usesYaw,
                out _);

            neutralBodyLocalHeadOffset = bodyReferencedHeadOffset;
            hasCenterBodyAnchor = true;
            centerBodyAnchorUsesYaw = usesYaw;
            hasNeutralBodyRelativeHeadYaw = TryComputeBodyRelativeHeadYawDeg(
                bodyAnchorLocalPose,
                targetRig.InverseTransformDirection(head.forward),
                out neutralBodyRelativeHeadYawDeg);
        }
    }

    void ResetMotionState()
    {
        smoothedPlanarVelocityLocal = Vector3.zero;
        rigMovementBlocked = false;
        rigMovementBlockedBy = string.Empty;
        stateGhostMovementBlocked = false;
        stateGhostMovementBlockedBy = string.Empty;
        hasVisualGhostPose = false;
        hasRealTimeDronePose = false;
        ResetInputToRigDelayState();
        ResetPredictionEstimatorState();
    }

    void ResetPredictionEstimatorState()
    {
        filteredPredictionVelocityWorld = Vector3.zero;
        filteredPredictionAccelerationWorld = Vector3.zero;
        filteredPredictionYawRateDeg = 0f;
        filteredPredictionYawAccelerationDeg = 0f;
        sustainedInputTime = 0f;
        hasLastRawPlanarDirection = false;
        lastRawYawRateDeg = 0f;
        kalmanVelocityX.Reset();
        kalmanVelocityY.Reset();
        kalmanVelocityZ.Reset();
        kalmanYawRateDeg.Reset();
        currentTranslationPredictionWindow = minTranslationPredictionWindow;
        currentYawPredictionWindow = minYawPredictionWindow;
        currentTranslationPredictionConfidence = 0f;
        currentYawPredictionConfidence = 0f;
    }

    void HandlePredictionMethodSwitchInput()
    {
        if (switchPredictionMethodKey == KeyCode.None)
        {
            return;
        }

        if (Input.GetKeyDown(switchPredictionMethodKey))
        {
            CyclePredictionMethod();
        }
    }

    void ApplyPredictionMethodIfChanged(bool force = false)
    {
        if (!force && predictionMethod == lastPredictionMethod)
        {
            SyncPredictionMethodDebugState();
            return;
        }

        if (resetEstimatorOnMethodSwitch)
        {
            ResetPredictionEstimatorState();
        }

        lastPredictionMethod = predictionMethod;
        SyncPredictionMethodDebugState();

        Debug.Log($"[PredictiveGhostAvatarLocomotion] Prediction method switched to {predictionMethod}.", this);
    }

    void SyncPredictionMethodDebugState()
    {
        activePredictionMethod = predictionMethod;
        useKalmanPrediction = predictionMethod == PredictionMethod.KalmanVelocityEstimator;
    }

    void MigrateLegacyPredictionToggle()
    {
        if (useKalmanPrediction && predictionMethod == PredictionMethod.AccelerationJerkLimited)
        {
            predictionMethod = PredictionMethod.KalmanVelocityEstimator;
        }
    }

    void MigrateLegacyInputToRigDelay()
    {
        if (legacyCameraMotionDelayMigrated || legacyCameraMotionDelaySeconds < 0f)
        {
            return;
        }

        enableInputToRigDelay = legacyEnableCameraMotionDelay;
        inputToRigDelayMilliseconds = Mathf.Max(0, Mathf.RoundToInt(legacyCameraMotionDelaySeconds * 1000f));
        legacyCameraMotionDelaySeconds = -1f;
        legacyCameraMotionDelayMigrated = true;
    }

    void WarnAboutConflicts()
    {
        if (warnedSetup)
        {
            return;
        }

        warnedSetup = true;

        if (droneBodyAvatar != null && targetRig != null && droneBodyAvatar.IsChildOf(targetRig))
        {
            Debug.LogWarning(
                "[PredictiveGhostAvatarLocomotion] Drone body avatar should live outside the XR rig hierarchy. The script keeps it synchronized with the rig explicitly.",
                droneBodyAvatar);
        }
    }

    Vector3 ComputeCenterOffsetLocal(Vector3 headLocalPos)
    {
        debugUsingInitialHeadReferenceFallback = planarReferenceMode == PlanarReferenceMode.InitialHeadPosition;

        if (planarReferenceMode == PlanarReferenceMode.BodyAnchor)
        {
            if (TryGetBodyAnchorLocalPose(out Pose bodyAnchorLocalPose))
            {
                debugUsingInitialHeadReferenceFallback = false;
                debugHasBodyAnchor = true;
                debugPlanarSignalPointLocal = headLocalPos;
                debugPlanarSignalAnchorLocal = bodyAnchorLocalPose.position;

                Vector3 bodyReferencedHeadOffset = ComputeBodyReferencedOffsetLocal(
                    headLocalPos,
                    bodyAnchorLocalPose,
                    out bool usesYaw,
                    out Quaternion bodyYawLocal);

                debugBodyAnchorUsesYaw = usesYaw;
                debugBodyLocalHeadOffset = bodyReferencedHeadOffset;

                if (!hasCenterBodyAnchor || centerBodyAnchorUsesYaw != usesYaw)
                {
                    neutralBodyLocalHeadOffset = bodyReferencedHeadOffset;
                    hasCenterBodyAnchor = true;
                    centerBodyAnchorUsesYaw = usesYaw;
                    hasNeutralBodyRelativeHeadYaw = TryComputeBodyRelativeHeadYawDeg(
                        bodyAnchorLocalPose,
                        targetRig.InverseTransformDirection(head.forward),
                        out neutralBodyRelativeHeadYawDeg);
                }

                Vector3 bodyReferencedDelta = bodyReferencedHeadOffset - neutralBodyLocalHeadOffset;
                bodyReferencedDelta.y = 0f;
                debugBodyLocalMoveOffset = bodyReferencedDelta;

                if (usesYaw)
                {
                    bodyReferencedDelta = bodyYawLocal * bodyReferencedDelta;
                    bodyReferencedDelta.y = 0f;
                }

                return bodyReferencedDelta;
            }

            debugHasBodyAnchor = false;
            debugBodyAnchorUsesYaw = false;
            debugBodyLocalHeadOffset = Vector3.zero;
            debugBodyLocalMoveOffset = Vector3.zero;
            debugPlanarSignalPointLocal = Vector3.zero;
            debugPlanarSignalAnchorLocal = Vector3.zero;

            if (!fallbackToInitialHeadReference)
            {
                return Vector3.zero;
            }

            debugUsingInitialHeadReferenceFallback = true;
        }

        return headLocalPos - centerHeadLocal;
    }

    bool TryGetBodyAnchorLocalPose(out Pose bodyAnchorLocalPose)
    {
        bodyAnchorLocalPose = default;

        if (planarReferenceMode != PlanarReferenceMode.BodyAnchor)
        {
            return false;
        }

        ResolveBodyAnchorProvider();
        return bodyAnchorProvider != null && bodyAnchorProvider.TryGetAnchorLocalPose(targetRig, out bodyAnchorLocalPose);
    }

    void ResolveBodyAnchorProvider()
    {
        if (bodyAnchorProvider != null || planarReferenceMode != PlanarReferenceMode.BodyAnchor)
        {
            return;
        }

        bodyAnchorProvider = GetComponent<BodyAnchorProvider>();
        if (bodyAnchorProvider == null && autoCreateBodyAnchorProvider)
        {
            bodyAnchorProvider = gameObject.AddComponent<BodyAnchorProvider>();
        }

        if (bodyAnchorProvider != null)
        {
            bodyAnchorProvider.source = BodyAnchorProvider.AnchorSource.LeftController;
            if (bodyAnchorProvider.target == null)
            {
                bodyAnchorProvider.target = targetRig != null ? targetRig : transform;
            }
        }
    }

    Vector3 ComputeBodyReferencedOffsetLocal(
        Vector3 signalLocalPos,
        Pose bodyAnchorLocalPose,
        out bool usedBodyYaw,
        out Quaternion bodyYawLocal)
    {
        Vector3 headRelativeToBodyLocal = signalLocalPos - bodyAnchorLocalPose.position;
        headRelativeToBodyLocal.y = 0f;
        usedBodyYaw = false;
        bodyYawLocal = Quaternion.identity;

        if (normalizePlanarOffsetByBodyYaw && TryGetBodyYawLocal(bodyAnchorLocalPose, out bodyYawLocal))
        {
            headRelativeToBodyLocal = Quaternion.Inverse(bodyYawLocal) * headRelativeToBodyLocal;
            usedBodyYaw = true;
        }

        headRelativeToBodyLocal.y = 0f;
        return headRelativeToBodyLocal;
    }

    Vector3 GetControlHeadForwardLocal()
    {
        debugBodyRelativeHeadYawDeg = 0f;
        debugBodyRelativeHeadYawDeltaDeg = 0f;

        Vector3 headForwardLocal = targetRig.InverseTransformDirection(head.forward);
        if (headForwardLocal.sqrMagnitude <= 1e-8f)
        {
            return Vector3.forward;
        }

        headForwardLocal.Normalize();

        if (useBodyRelativeHeadYaw &&
            TryGetBodyAnchorLocalPose(out Pose bodyAnchorLocalPose) &&
            TryComputeBodyRelativeHeadYawDeg(bodyAnchorLocalPose, headForwardLocal, out float bodyRelativeHeadYawDeg))
        {
            if (!hasNeutralBodyRelativeHeadYaw)
            {
                neutralBodyRelativeHeadYawDeg = bodyRelativeHeadYawDeg;
                hasNeutralBodyRelativeHeadYaw = true;
            }

            float yawDeltaRad = Mathf.DeltaAngle(neutralBodyRelativeHeadYawDeg, bodyRelativeHeadYawDeg) * Mathf.Deg2Rad;
            debugBodyRelativeHeadYawDeg = bodyRelativeHeadYawDeg;
            debugBodyRelativeHeadYawDeltaDeg = yawDeltaRad * Mathf.Rad2Deg;

            float pitchY = Mathf.Clamp(headForwardLocal.y, -0.9999f, 0.9999f);
            float planarMagnitude = Mathf.Sqrt(Mathf.Max(0f, 1f - pitchY * pitchY));
            headForwardLocal = new Vector3(
                Mathf.Sin(yawDeltaRad) * planarMagnitude,
                pitchY,
                Mathf.Cos(yawDeltaRad) * planarMagnitude);
        }

        return headForwardLocal.sqrMagnitude > 1e-8f ? headForwardLocal.normalized : Vector3.forward;
    }

    bool TryComputeBodyRelativeHeadYawDeg(
        Pose bodyAnchorLocalPose,
        Vector3 headForwardLocal,
        out float bodyRelativeHeadYawDeg)
    {
        bodyRelativeHeadYawDeg = 0f;

        if (!useBodyRelativeHeadYaw || !TryGetBodyYawLocal(bodyAnchorLocalPose, out Quaternion bodyYawLocal))
        {
            return false;
        }

        Vector3 bodyForwardLocal = bodyYawLocal * Vector3.forward;
        if (!TryGetPlanarYawDeg(bodyForwardLocal, out float bodyYawDeg) ||
            !TryGetPlanarYawDeg(headForwardLocal, out float headYawDeg))
        {
            return false;
        }

        bodyRelativeHeadYawDeg = Mathf.DeltaAngle(bodyYawDeg, headYawDeg);
        return true;
    }

    LocomotionCommand SampleCommand(float dt)
    {
        Vector3 headLocalPos = targetRig.InverseTransformPoint(head.position);
        Vector3 centerOffsetLocal = ComputeCenterOffsetLocal(headLocalPos);
        Vector3 planarOffsetLocal = new Vector3(centerOffsetLocal.x, 0f, centerOffsetLocal.z);
        currentCenterOffsetLocal = centerOffsetLocal;

        float planarSpeed = ComputePlanarSpeed(planarOffsetLocal.magnitude);
        Vector3 planarDirectionLocal = ComputePlanarDirectionLocal(planarOffsetLocal);
        Vector3 rawPlanarVelocityLocal = planarDirectionLocal * planarSpeed;

        float planarLerp = 1f - Mathf.Exp(-Mathf.Max(0f, planarSmoothing) * dt);
        smoothedPlanarVelocityLocal = Vector3.Lerp(smoothedPlanarVelocityLocal, rawPlanarVelocityLocal, planarLerp);

        Vector3 headForwardLocal = GetControlHeadForwardLocal();
        Vector3 planarVelocityForPrediction = useSmoothedPlanarVelocityForPrediction
            ? smoothedPlanarVelocityLocal
            : rawPlanarVelocityLocal;

        Vector2 planarCommand = horizontalSpeed > 1e-6f
            ? new Vector2(planarVelocityForPrediction.x, planarVelocityForPrediction.z) / horizontalSpeed
            : Vector2.zero;

        float yawRateRad = ComputeYawRate(headForwardLocal, planarCommand);
        float rawYawRateDeg = yawRateRad * Mathf.Rad2Deg;
        float verticalCommand = ComputeVerticalSpeed(headForwardLocal);
        if (Mathf.Abs(verticalCommand) < Mathf.Max(0f, verticalCommandDeadZone))
        {
            verticalCommand = 0f;
        }
        currentHeadPitchDeg = ComputeHeadPitchDeg(headForwardLocal);
        currentVerticalCommand = verticalCommand;

        Vector2 directPlanarCommand = horizontalSpeed > 1e-6f
            ? new Vector2(smoothedPlanarVelocityLocal.x, smoothedPlanarVelocityLocal.z) / horizontalSpeed
            : Vector2.zero;
        float directYawRateDeg = ComputeYawRate(headForwardLocal, directPlanarCommand) * Mathf.Rad2Deg;
        Vector3 directLocalVelocity = new Vector3(
            smoothedPlanarVelocityLocal.x,
            verticalCommand,
            smoothedPlanarVelocityLocal.z);
        Vector3 directWorldVelocity = targetRig.TransformDirection(directLocalVelocity);

        Vector3 predictionLocalVelocity = new Vector3(
            planarVelocityForPrediction.x,
            verticalCommand,
            planarVelocityForPrediction.z);
        currentPredictionVelocityLocal = predictionLocalVelocity;

        Vector3 rawWorldPredictionVelocity = targetRig.TransformDirection(predictionLocalVelocity);
        bool hasInput = predictionLocalVelocity.sqrMagnitude > 1e-4f || Mathf.Abs(rawYawRateDeg) > 0.5f;

        UpdateAdaptivePredictionWindows(rawWorldPredictionVelocity, rawYawRateDeg, hasInput, dt);
        Vector3 filteredWorldPredictionVelocity;
        float filteredYawPredictionRateDeg;
        if (predictionMethod == PredictionMethod.KalmanVelocityEstimator)
        {
            filteredWorldPredictionVelocity = UpdateKalmanPredictionVelocity(
                rawWorldPredictionVelocity,
                hasInput,
                currentTranslationPredictionWindow,
                dt);
            filteredYawPredictionRateDeg = UpdateKalmanPredictionYawRate(
                rawYawRateDeg,
                hasInput,
                currentYawPredictionWindow,
                dt);
        }
        else
        {
            filteredWorldPredictionVelocity = UpdateFilteredPredictionVelocity(rawWorldPredictionVelocity, hasInput, dt);
            filteredYawPredictionRateDeg = UpdateFilteredPredictionYawRate(rawYawRateDeg, hasInput, dt);
        }

        return new LocomotionCommand
        {
            rawWorldVelocity = rawWorldPredictionVelocity,
            directWorldVelocity = directWorldVelocity,
            predictionWorldVelocity = filteredWorldPredictionVelocity,
            rawYawRateDeg = rawYawRateDeg,
            directYawRateDeg = directYawRateDeg,
            predictionYawRateDeg = filteredYawPredictionRateDeg,
            translationPredictionWindow = currentTranslationPredictionWindow,
            yawPredictionWindow = currentYawPredictionWindow,
            hasInput = hasInput
        };
    }

    void UpdateAdaptivePredictionWindows(Vector3 rawWorldVelocity, float rawYawRateDeg, bool hasInput, float dt)
    {
        if (hasInput)
        {
            sustainedInputTime += dt;
        }
        else
        {
            sustainedInputTime = 0f;
        }

        Vector3 rawPlanarVelocityWorld = new Vector3(rawWorldVelocity.x, 0f, rawWorldVelocity.z);
        float planarSpeed = rawPlanarVelocityWorld.magnitude;
        float speedDenominator = Mathf.Max(0.01f, horizontalSpeed * Mathf.Max(0.05f, fullPredictionAtSpeedFraction));
        float speedConfidence = Mathf.Clamp01(planarSpeed / speedDenominator);
        float sustainConfidence = Mathf.Clamp01(sustainedInputTime / Mathf.Max(0.01f, predictionRampTime));

        float directionStability = 1f;
        if (planarSpeed > 0.01f)
        {
            Vector3 rawPlanarDirection = rawPlanarVelocityWorld / planarSpeed;
            if (hasLastRawPlanarDirection)
            {
                float angleDelta = Vector3.Angle(lastRawPlanarDirectionWorld, rawPlanarDirection);
                directionStability = 1f - Mathf.Clamp01(angleDelta / Mathf.Max(1f, predictionDirectionStabilityAngleDeg));
            }

            lastRawPlanarDirectionWorld = rawPlanarDirection;
            hasLastRawPlanarDirection = true;
        }
        else if (!hasInput)
        {
            hasLastRawPlanarDirection = false;
        }

        float yawDenominator = Mathf.Max(1f, yawSpeed * Mathf.Max(0.05f, fullPredictionAtSpeedFraction));
        float yawConfidence = Mathf.Clamp01(Mathf.Abs(rawYawRateDeg) / yawDenominator);
        float yawDeltaThreshold = Mathf.Max(20f, maxPredictedYawAccelerationDeg * 0.35f);
        float yawStability = 1f - Mathf.Clamp01(Mathf.Abs(rawYawRateDeg - lastRawYawRateDeg) / yawDeltaThreshold);
        if (Mathf.Abs(rawYawRateDeg) > 5f
            && Mathf.Abs(lastRawYawRateDeg) > 5f
            && Mathf.Sign(rawYawRateDeg) != Mathf.Sign(lastRawYawRateDeg))
        {
            yawStability *= 0.35f;
        }

        lastRawYawRateDeg = hasInput ? rawYawRateDeg : 0f;

        float translationConfidence = hasInput ? speedConfidence * sustainConfidence * directionStability : 0f;
        float rotationConfidence = hasInput ? yawConfidence * sustainConfidence * yawStability : 0f;

        currentTranslationPredictionConfidence = translationConfidence;
        currentYawPredictionConfidence = rotationConfidence;

        float targetTranslationWindow = Mathf.Lerp(
            Mathf.Max(0f, minTranslationPredictionWindow),
            Mathf.Max(minTranslationPredictionWindow, maxTranslationPredictionWindow),
            translationConfidence);

        float targetYawWindow = Mathf.Lerp(
            Mathf.Max(0f, minYawPredictionWindow),
            Mathf.Max(minYawPredictionWindow, maxYawPredictionWindow),
            rotationConfidence);

        float responseT = 1f - Mathf.Exp(-Mathf.Max(0f, predictionWindowResponse) * dt);
        currentTranslationPredictionWindow = Mathf.Lerp(currentTranslationPredictionWindow, targetTranslationWindow, responseT);
        currentYawPredictionWindow = Mathf.Lerp(currentYawPredictionWindow, targetYawWindow, responseT);
    }

    Vector3 UpdateFilteredPredictionVelocity(Vector3 rawWorldVelocity, bool hasInput, float dt)
    {
        Vector3 targetVelocity = hasInput ? rawWorldVelocity : Vector3.zero;
        Vector3 desiredAcceleration = dt > 1e-5f
            ? (targetVelocity - filteredPredictionVelocityWorld) / dt
            : Vector3.zero;

        desiredAcceleration = Vector3.ClampMagnitude(
            desiredAcceleration,
            Mathf.Max(0f, maxPredictedLinearAcceleration));

        filteredPredictionAccelerationWorld = Vector3.MoveTowards(
            filteredPredictionAccelerationWorld,
            desiredAcceleration,
            Mathf.Max(0f, maxPredictedLinearJerk) * dt);

        filteredPredictionAccelerationWorld = Vector3.ClampMagnitude(
            filteredPredictionAccelerationWorld,
            Mathf.Max(0f, maxPredictedLinearAcceleration));

        Vector3 nextVelocity = filteredPredictionVelocityWorld + filteredPredictionAccelerationWorld * dt;
        filteredPredictionVelocityWorld = ClampVectorOvershoot(filteredPredictionVelocityWorld, nextVelocity, targetVelocity);

        if (!hasInput
            && filteredPredictionVelocityWorld.sqrMagnitude < 1e-4f
            && filteredPredictionAccelerationWorld.sqrMagnitude < 1e-3f)
        {
            filteredPredictionVelocityWorld = Vector3.zero;
            filteredPredictionAccelerationWorld = Vector3.zero;
        }

        return filteredPredictionVelocityWorld;
    }

    float UpdateFilteredPredictionYawRate(float rawYawRateDeg, bool hasInput, float dt)
    {
        float targetYawRate = hasInput ? rawYawRateDeg : 0f;
        float desiredYawAcceleration = dt > 1e-5f
            ? (targetYawRate - filteredPredictionYawRateDeg) / dt
            : 0f;

        desiredYawAcceleration = Mathf.Clamp(
            desiredYawAcceleration,
            -Mathf.Max(0f, maxPredictedYawAccelerationDeg),
            Mathf.Max(0f, maxPredictedYawAccelerationDeg));

        filteredPredictionYawAccelerationDeg = Mathf.MoveTowards(
            filteredPredictionYawAccelerationDeg,
            desiredYawAcceleration,
            Mathf.Max(0f, maxPredictedYawJerkDeg) * dt);

        filteredPredictionYawAccelerationDeg = Mathf.Clamp(
            filteredPredictionYawAccelerationDeg,
            -Mathf.Max(0f, maxPredictedYawAccelerationDeg),
            Mathf.Max(0f, maxPredictedYawAccelerationDeg));

        float nextYawRate = filteredPredictionYawRateDeg + filteredPredictionYawAccelerationDeg * dt;
        filteredPredictionYawRateDeg = ClampScalarOvershoot(filteredPredictionYawRateDeg, nextYawRate, targetYawRate);

        if (!hasInput
            && Mathf.Abs(filteredPredictionYawRateDeg) < 0.05f
            && Mathf.Abs(filteredPredictionYawAccelerationDeg) < 0.5f)
        {
            filteredPredictionYawRateDeg = 0f;
            filteredPredictionYawAccelerationDeg = 0f;
        }

        return filteredPredictionYawRateDeg;
    }

    Vector3 UpdateKalmanPredictionVelocity(Vector3 rawWorldVelocity, bool hasInput, float predictionWindow, float dt)
    {
        Vector3 measurement = hasInput ? rawWorldVelocity : Vector3.zero;
        float processNoise = Mathf.Max(0f, kalmanLinearProcessNoise);
        float measurementNoise = Mathf.Max(0.0001f, kalmanLinearMeasurementNoise);

        kalmanVelocityX.Step(measurement.x, dt, processNoise, measurementNoise);
        kalmanVelocityY.Step(measurement.y, dt, processNoise, measurementNoise);
        kalmanVelocityZ.Step(measurement.z, dt, processNoise, measurementNoise);

        filteredPredictionVelocityWorld = new Vector3(
            kalmanVelocityX.Value,
            kalmanVelocityY.Value,
            kalmanVelocityZ.Value);
        filteredPredictionAccelerationWorld = new Vector3(
            kalmanVelocityX.Derivative,
            kalmanVelocityY.Derivative,
            kalmanVelocityZ.Derivative);

        float horizon = Mathf.Max(0f, predictionWindow);
        float accelerationLimit = Mathf.Max(0f, maxPredictedLinearAcceleration);
        return new Vector3(
            kalmanVelocityX.AverageValueOverHorizon(horizon, accelerationLimit),
            kalmanVelocityY.AverageValueOverHorizon(horizon, accelerationLimit),
            kalmanVelocityZ.AverageValueOverHorizon(horizon, accelerationLimit));
    }

    float UpdateKalmanPredictionYawRate(float rawYawRateDeg, bool hasInput, float predictionWindow, float dt)
    {
        float measurement = hasInput ? rawYawRateDeg : 0f;
        kalmanYawRateDeg.Step(
            measurement,
            dt,
            Mathf.Max(0f, kalmanYawProcessNoise),
            Mathf.Max(0.0001f, kalmanYawMeasurementNoise));

        filteredPredictionYawRateDeg = kalmanYawRateDeg.Value;
        filteredPredictionYawAccelerationDeg = kalmanYawRateDeg.Derivative;

        return kalmanYawRateDeg.AverageValueOverHorizon(
            Mathf.Max(0f, predictionWindow),
            Mathf.Max(0f, maxPredictedYawAccelerationDeg));
    }

    void UpdatePredictiveStateGhostPose(LocomotionCommand command, float dt)
    {
        if (!hasGhostPose)
        {
            SnapGhostToRig();
        }

        if (!command.hasInput)
        {
            ghostPosition = ResolveStateGhostReachablePosition(realTimeDronePosition, realTimeDronePosition);
            ghostYawDeg = realTimeDroneYawDeg;
            UpdateVisualGhostPose(dt);
            stateGhostOffsetLocal = targetRig != null
                ? targetRig.InverseTransformDirection(ghostPosition - targetRig.position)
                : Vector3.zero;
            SyncStateGhostTransform();
            return;
        }

        Vector3 predictedPosition = ResolvePredictiveGhostReachablePosition(
            realTimeDronePosition,
            command.predictionWorldVelocity,
            Mathf.Max(0f, command.translationPredictionWindow));
        float predictedYaw = realTimeDroneYawDeg
            + command.predictionYawRateDeg * Mathf.Max(0f, command.yawPredictionWindow);

        ghostPosition = predictedPosition;
        ghostYawDeg = predictedYaw;
        UpdateVisualGhostPose(dt);

        stateGhostOffsetLocal = targetRig != null
            ? targetRig.InverseTransformDirection(ghostPosition - targetRig.position)
            : Vector3.zero;
        SyncStateGhostTransform();
    }

    void UpdateVisualGhostPose(float dt)
    {
        if (!hasVisualGhostPose || snapGhostToPrediction || dt <= 0f)
        {
            visualGhostPosition = ghostPosition;
            visualGhostYawDeg = ghostYawDeg;
            hasVisualGhostPose = true;
            return;
        }

        float posT = 1f - Mathf.Exp(-Mathf.Max(0f, ghostPositionResponse) * dt);
        float yawT = 1f - Mathf.Exp(-Mathf.Max(0f, ghostYawResponse) * dt);
        visualGhostPosition = Vector3.Lerp(visualGhostPosition, ghostPosition, posT);
        visualGhostYawDeg = Mathf.LerpAngle(visualGhostYawDeg, ghostYawDeg, yawT);
    }

    Vector3 ResolveStateGhostReachablePosition(Vector3 currentPosition, Vector3 requestedPosition)
    {
        if (!constrainStateGhostToReachableSpace || !enableSoftCollisionBlocking || targetRig == null)
        {
            return requestedPosition;
        }

        Vector3 constrainedPosition = ResolveSoftBlockedPosition(
            currentPosition,
            requestedPosition,
            Mathf.Max(0.01f, collisionProbeRadius),
            out bool blocked,
            out string blockedBy);

        if (blocked)
        {
            stateGhostMovementBlocked = true;
            stateGhostMovementBlockedBy = blockedBy;
        }

        return constrainedPosition;
    }

    Vector3 ResolvePredictiveGhostReachablePosition(Vector3 startPosition, Vector3 worldVelocity, float predictionWindow)
    {
        float horizon = Mathf.Max(0f, predictionWindow);
        Vector3 predictionDelta = worldVelocity * horizon;
        float maxLead = Mathf.Max(0.01f, maxPredictionDistance);
        if (predictionDelta.sqrMagnitude > maxLead * maxLead)
        {
            predictionDelta = predictionDelta.normalized * maxLead;
        }

        if (predictionDelta.sqrMagnitude <= 1e-8f)
        {
            return ResolveStateGhostReachablePosition(startPosition, startPosition);
        }

        if (!constrainStateGhostToReachableSpace || !enableSoftCollisionBlocking || targetRig == null)
        {
            return ClampPosition(startPosition + predictionDelta, positionLimits);
        }

        Vector3 safeStart = ResolveStateGhostReachablePosition(startPosition, startPosition);
        Vector3 targetPosition = ClampPosition(safeStart + predictionDelta, positionLimits);
        float travelDistance = Vector3.Distance(safeStart, targetPosition);
        float stepLength = Mathf.Max(0.05f, Mathf.Max(0.01f, collisionProbeRadius) * 0.5f);
        int steps = Mathf.Clamp(
            Mathf.CeilToInt(travelDistance / stepLength),
            1,
            Mathf.Max(1, predictiveGhostCollisionSteps));

        Vector3 position = safeStart;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 requestedStepPosition = Vector3.Lerp(safeStart, targetPosition, i / (float)steps);
            Vector3 constrainedStepPosition = ResolveStateGhostReachablePosition(position, requestedStepPosition);
            Vector3 requestedStepDelta = requestedStepPosition - position;
            Vector3 actualStepDelta = constrainedStepPosition - position;

            position = constrainedStepPosition;
            if (requestedStepDelta.sqrMagnitude > 1e-7f
                && actualStepDelta.sqrMagnitude <= 1e-8f)
            {
                break;
            }
        }

        return position;
    }

    Vector3 ResolveSoftBlockedRigPosition(Vector3 requestedRigPosition)
    {
        rigMovementBlocked = false;
        rigMovementBlockedBy = string.Empty;

        if (!enableSoftCollisionBlocking || targetRig == null)
        {
            return requestedRigPosition;
        }

        Vector3 constrainedPosition = ResolveSoftBlockedPosition(
            targetRig.position,
            requestedRigPosition,
            Mathf.Max(0.01f, collisionProbeRadius),
            out bool blocked,
            out string blockedBy);

        if (blocked)
        {
            rigMovementBlocked = true;
            rigMovementBlockedBy = blockedBy;
        }

        return constrainedPosition;
    }

    Vector3 ResolveSoftBlockedPosition(Vector3 currentRigPosition, Vector3 requestedRigPosition, float radius, out bool blocked, out string blockedBy)
    {
        blocked = false;
        blockedBy = string.Empty;

        if (!enableSoftCollisionBlocking || targetRig == null)
        {
            return requestedRigPosition;
        }

        Vector3 requestedDelta = requestedRigPosition - currentRigPosition;
        float requestedDistance = requestedDelta.magnitude;

        radius = Mathf.Max(0.01f, radius);
        float skin = Mathf.Max(0f, collisionSkinWidth);

        Vector3 physicsConstrainedPosition = requestedRigPosition;
        if (requestedDistance > 1e-5f)
        {
            Vector3 direction = requestedDelta / requestedDistance;
            Vector3 probeStart = GetCollisionProbePositionForRigPosition(currentRigPosition);
            float castDistance = requestedDistance + skin;

            int hitCount = Physics.SphereCastNonAlloc(
                probeStart,
                radius,
                direction,
                rigCollisionHits,
                castDistance,
                collisionBlockLayers,
                collisionQueryTriggerInteraction);

            RaycastHit nearestHit = default;
            bool hasHit = false;
            float nearestDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = rigCollisionHits[i];
                if (ShouldIgnoreRigCollisionHit(hit))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestHit = hit;
                    hasHit = true;
                }
            }

            if (hasHit)
            {
                float allowedDistance = Mathf.Max(0f, nearestDistance - skin);
                if (allowedDistance < requestedDistance)
                {
                    blocked = true;
                    blockedBy = nearestHit.collider != null ? nearestHit.collider.name : "Unknown collider";
                    physicsConstrainedPosition = currentRigPosition + direction * allowedDistance;
                }
            }
        }

        return ResolveTubeBoundaryConstrainedRigPosition(physicsConstrainedPosition, radius, ref blocked, ref blockedBy);
    }

    Vector3 ResolveTubeBoundaryConstrainedRigPosition(Vector3 requestedRigPosition, float probeRadius, ref bool blocked, ref string blockedBy)
    {
        RefreshTubeBoundaryReference();
        if (tubeBoundary == null)
        {
            return requestedRigPosition;
        }

        Vector3 requestedProbePosition = GetCollisionProbePositionForRigPosition(requestedRigPosition);
        if (!tubeBoundary.TryConstrainInside(requestedProbePosition, probeRadius, out Vector3 constrainedProbePosition))
        {
            return requestedRigPosition;
        }

        blocked = true;
        blockedBy = tubeBoundary.hazardLabel;
        return requestedRigPosition + (constrainedProbePosition - requestedProbePosition);
    }

    void RefreshTubeBoundaryReference()
    {
        if (!autoFindTubeBoundary || tubeBoundary != null)
        {
            return;
        }

        tubeBoundary = FindFirstObjectByType<IrairaBouTubeBoundary>();
    }

    Vector3 GetCollisionProbePositionForRigPosition(Vector3 rigPosition)
    {
        Transform probe = collisionProbe != null
            ? collisionProbe
            : useHeadAsCollisionProbe ? head : null;

        if (probe == null || targetRig == null)
        {
            return rigPosition;
        }

        return probe.position + (rigPosition - targetRig.position);
    }

    bool ShouldIgnoreRigCollisionHit(RaycastHit hit)
    {
        Collider hitCollider = hit.collider;
        if (hitCollider == null)
        {
            return true;
        }

        Transform hitTransform = hitCollider.transform;
        if (targetRig != null && hitTransform.IsChildOf(targetRig))
        {
            return true;
        }

        if (droneBodyAvatar != null && hitTransform.IsChildOf(droneBodyAvatar))
        {
            return true;
        }

        if (stateGhostAvatar != null && hitTransform.IsChildOf(stateGhostAvatar))
        {
            return true;
        }

        Rigidbody attachedRigidbody = hitCollider.attachedRigidbody;
        if (attachedRigidbody != null && targetRig != null && attachedRigidbody.transform.IsChildOf(targetRig))
        {
            return true;
        }

        return false;
    }

    void UpdateConvergenceState(bool forceNotify)
    {
        if (!hasGhostPose || targetRig == null)
        {
            return;
        }

        distanceToGhost = Vector3.Distance(targetRig.position, ghostPosition);
        float yawDelta = Mathf.Abs(Mathf.DeltaAngle(targetRig.eulerAngles.y, ghostYawDeg));
        bool convergedNow = distanceToGhost <= Mathf.Max(0.001f, convergenceDistance)
            && yawDelta <= Mathf.Max(0.1f, convergenceYawThresholdDeg);

        if (!forceNotify && convergedNow == isConverged)
        {
            return;
        }

        isConverged = convergedNow;
        if (isConverged)
        {
            onConverged.Invoke();
        }
        else
        {
            onSeparated.Invoke();
        }
    }

    float ComputePlanarSpeed(float offsetMagnitude)
    {
        if (offsetMagnitude <= planarDeadZone)
        {
            return 0f;
        }

        float maxOffset = Mathf.Max(planarDeadZone + 1e-4f, planarMaxOffset);
        float normalized = Mathf.Clamp01((offsetMagnitude - planarDeadZone) / (maxOffset - planarDeadZone));
        float curved = Mathf.Pow(normalized, Mathf.Max(0.1f, planarResponseExponent));
        return curved * Mathf.Max(0f, horizontalSpeed);
    }

    Vector3 ComputePlanarDirectionLocal(Vector3 planarOffsetLocal)
    {
        Vector3 leanDirLocal = planarOffsetLocal.sqrMagnitude > 1e-8f
            ? planarOffsetLocal.normalized
            : Vector3.zero;

        Vector3 headForwardLocal = GetControlHeadForwardLocal();
        Vector3 headPlanarLocal = new Vector3(headForwardLocal.x, 0f, headForwardLocal.z);
        if (headPlanarLocal.sqrMagnitude > 1e-8f)
        {
            headPlanarLocal.Normalize();
        }

        if (leanDirLocal == Vector3.zero && headPlanarLocal == Vector3.zero)
        {
            return Vector3.zero;
        }

        if (leanDirLocal == Vector3.zero)
        {
            return headPlanarLocal;
        }

        if (headPlanarLocal == Vector3.zero)
        {
            return leanDirLocal;
        }

        Vector3 blended = Vector3.Lerp(leanDirLocal, headPlanarLocal, headDirectionBlend);
        return blended.sqrMagnitude > 1e-8f ? blended.normalized : Vector3.zero;
    }

    float ComputeYawRate(Vector3 headForwardLocal, Vector2 planarCommand)
    {
        float headYaw = Mathf.Atan2(headForwardLocal.x, headForwardLocal.z);

        switch (orientationMode)
        {
            case OrientationControlMode.Static:
            {
                float threshold = staticYawThresholdDeg * Mathf.Deg2Rad;
                float maxRate = staticYawSpeedDeg * Mathf.Deg2Rad;
                return Mathf.Abs(headYaw) > threshold ? maxRate * Mathf.Sign(headYaw) : 0f;
            }
            case OrientationControlMode.Coupled:
            {
                float maxRate = coupledYawMaxSpeedDeg * Mathf.Deg2Rad;
                return Mathf.Clamp(coupledYawGain * headYaw, -maxRate, maxRate);
            }
            case OrientationControlMode.Dynamic:
            default:
            {
                float gain = 0.5f;
                float maxRate = yawSpeed * Mathf.Deg2Rad;
                float thMin = 12f * Mathf.Deg2Rad;
                float thMax = 30f * Mathf.Deg2Rad;
                float speedThreshold = 7f;
                float speedScale = 10f;

                float planarSpeedNormalized = Mathf.Min(1f, planarCommand.magnitude);
                float vd = planarSpeedNormalized * speedScale;
                float deltaTheta = (thMin - thMax) / (1f + Mathf.Exp(speedThreshold - vd)) + thMax;
                float lambda = 1f / (1f + Mathf.Exp(-gain * (Mathf.Abs(headYaw) - deltaTheta)));
                float scaled = Mathf.Max(0f, 2f * lambda - 1f);
                float yawRate = maxRate * scaled * Mathf.Sign(headYaw);
                return Mathf.Clamp(yawRate, -maxRate, maxRate);
            }
        }
    }

    float ComputeVerticalSpeed(Vector3 headForwardLocal)
    {
        float headPitchDeg = ComputeHeadPitchDeg(headForwardLocal);

        switch (orientationMode)
        {
            case OrientationControlMode.Static:
                if (headPitchDeg >= staticPitchUpThresholdDeg)
                {
                    return staticPitchUpSpeed;
                }
                if (headPitchDeg <= -staticPitchDownThresholdDeg)
                {
                    return -staticPitchDownSpeed;
                }
                return 0f;

            case OrientationControlMode.Coupled:
            {
                float refDeg = Mathf.Max(coupledPitchReferenceDeg, 1f);
                float maxSpeed = coupledPitchMaxSpeed;
                return Mathf.Clamp((headPitchDeg / refDeg) * maxSpeed, -maxSpeed, maxSpeed);
            }

            case OrientationControlMode.Dynamic:
            default:
            {
                float lambda;
                float delta;
                float signFactor;

                if (headPitchDeg >= 0f)
                {
                    lambda = 0.3f;
                    delta = 30f;
                    signFactor = 1f;
                }
                else
                {
                    lambda = -0.4f;
                    delta = -18f;
                    signFactor = -1f;
                }

                float logistic = 1f / (1f + Mathf.Exp(-lambda * (headPitchDeg - delta)));
                logistic = Mathf.Clamp01(logistic);
                return signFactor * logistic * Mathf.Max(0f, verticalSpeed);
            }
        }
    }

    static Vector3 ClampVectorOvershoot(Vector3 current, Vector3 next, Vector3 target)
    {
        Vector3 toTargetBefore = target - current;
        if (toTargetBefore.sqrMagnitude <= 1e-8f)
        {
            return target;
        }

        Vector3 toTargetAfter = target - next;
        return Vector3.Dot(toTargetBefore, toTargetAfter) <= 0f ? target : next;
    }

    static float ClampScalarOvershoot(float current, float next, float target)
    {
        float toTargetBefore = target - current;
        if (Mathf.Abs(toTargetBefore) <= 1e-5f)
        {
            return target;
        }

        float toTargetAfter = target - next;
        return toTargetBefore * toTargetAfter <= 0f ? target : next;
    }

    static Vector3 ClampPosition(Vector3 position, Vector3 limits)
    {
        return new Vector3(
            Mathf.Clamp(position.x, -limits.x, limits.x),
            Mathf.Clamp(position.y, -limits.y, limits.y),
            Mathf.Clamp(position.z, -limits.z, limits.z));
    }

    static bool TryGetBodyYawLocal(Pose bodyAnchorLocalPose, out Quaternion bodyYawLocal)
    {
        return TryGetYawRotation(bodyAnchorLocalPose.rotation, out bodyYawLocal);
    }

    static bool TryGetYawRotation(Quaternion rotation, out Quaternion yawRotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 1e-8f)
        {
            Vector3 right = rotation * Vector3.right;
            right.y = 0f;
            if (right.sqrMagnitude <= 1e-8f)
            {
                yawRotation = Quaternion.identity;
                return false;
            }

            forward = Vector3.Cross(right.normalized, Vector3.up);
        }

        yawRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        return true;
    }

    static bool TryGetPlanarYawDeg(Vector3 forward, out float yawDeg)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude <= 1e-8f)
        {
            yawDeg = 0f;
            return false;
        }

        yawDeg = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        return true;
    }

    interface IDelayedSample
    {
        float SampleTime { get; }
    }

    struct DelayedCommandSample : IDelayedSample
    {
        public float time;
        public LocomotionCommand command;

        public float SampleTime => time;
    }

    struct DronePoseSample : IDelayedSample
    {
        public float time;
        public Vector3 position;
        public float yawDeg;

        public float SampleTime => time;
    }

    struct LocomotionCommand
    {
        public Vector3 rawWorldVelocity;
        public Vector3 directWorldVelocity;
        public Vector3 predictionWorldVelocity;
        public float rawYawRateDeg;
        public float directYawRateDeg;
        public float predictionYawRateDeg;
        public float translationPredictionWindow;
        public float yawPredictionWindow;
        public bool hasInput;
    }

    struct Kalman1D
    {
        bool initialized;
        float value;
        float derivative;
        float p00;
        float p01;
        float p10;
        float p11;

        public float Value => value;
        public float Derivative => derivative;

        public void Reset()
        {
            initialized = false;
            value = 0f;
            derivative = 0f;
            p00 = 1f;
            p01 = 0f;
            p10 = 0f;
            p11 = 1f;
        }

        public void Step(float measurement, float dt, float processNoise, float measurementNoise)
        {
            float safeDt = Mathf.Max(dt, 1e-4f);
            if (!initialized)
            {
                initialized = true;
                value = measurement;
                derivative = 0f;
                p00 = measurementNoise;
                p01 = 0f;
                p10 = 0f;
                p11 = Mathf.Max(1f, processNoise);
                return;
            }

            value += derivative * safeDt;

            float dt2 = safeDt * safeDt;
            float dt3 = dt2 * safeDt;
            float q = Mathf.Max(0f, processNoise);
            float q00 = q * dt3 / 3f;
            float q01 = q * dt2 / 2f;
            float q11 = q * safeDt;

            float predictedP00 = p00 + safeDt * (p10 + p01) + dt2 * p11 + q00;
            float predictedP01 = p01 + safeDt * p11 + q01;
            float predictedP10 = p10 + safeDt * p11 + q01;
            float predictedP11 = p11 + q11;

            float innovation = measurement - value;
            float innovationVariance = predictedP00 + Mathf.Max(0.0001f, measurementNoise);
            float k0 = predictedP00 / innovationVariance;
            float k1 = predictedP10 / innovationVariance;

            value += k0 * innovation;
            derivative += k1 * innovation;

            p00 = (1f - k0) * predictedP00;
            p01 = (1f - k0) * predictedP01;
            p10 = predictedP10 - k1 * predictedP00;
            p11 = predictedP11 - k1 * predictedP01;

            float symmetricOffDiagonal = 0.5f * (p01 + p10);
            p01 = symmetricOffDiagonal;
            p10 = symmetricOffDiagonal;
            p00 = Mathf.Max(p00, 1e-6f);
            p11 = Mathf.Max(p11, 1e-6f);
        }

        public float AverageValueOverHorizon(float horizon, float derivativeLimit)
        {
            float safeHorizon = Mathf.Max(0f, horizon);
            float safeDerivative = derivativeLimit > 0f
                ? Mathf.Clamp(derivative, -derivativeLimit, derivativeLimit)
                : derivative;
            return value + 0.5f * safeDerivative * safeHorizon;
        }
    }

    static float ComputeHeadPitchDeg(Vector3 headForwardLocal)
    {
        float planarNorm = Mathf.Sqrt(headForwardLocal.x * headForwardLocal.x + headForwardLocal.z * headForwardLocal.z);
        return Mathf.Atan2(headForwardLocal.y, planarNorm) * Mathf.Rad2Deg;
    }
}
