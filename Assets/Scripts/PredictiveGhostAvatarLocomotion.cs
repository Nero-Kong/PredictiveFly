using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Predictive third-person separation locomotion built on the same head-offset
/// command interpretation as HeadOffsetLocomotion.
/// Attach this to the locomotion root (for example XRRig) and disable the
/// original HeadOffsetLocomotion while testing this mode.
/// </summary>
[DisallowMultipleComponent]
public class PredictiveGhostAvatarLocomotion : MonoBehaviour
{
    [Header("Rig / HMD")]
    [Tooltip("Locomotion root that should be pulled toward the ghost. Defaults to this object.")]
    public Transform targetRig;
    [Tooltip("Tracked HMD/camera transform. Defaults to Camera.main.")]
    public Transform head;

    [Header("Ghost Avatar")]
    [Tooltip("Optional ghost avatar transform. Leave empty to run the logic without a visible mesh.")]
    public Transform ghostAvatar;
    [Tooltip("If true, the ghost snaps to the predicted pose each frame.")]
    public bool snapGhostToPrediction = true;
    [Tooltip("Ghost position response when snap is disabled. Higher = more immediate.")]
    public float ghostPositionResponse = 30f;
    [Tooltip("Ghost yaw response when snap is disabled. Higher = more immediate.")]
    public float ghostYawResponse = 30f;

    [Header("Prediction")]
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
    [Tooltip("Use a Kalman velocity/acceleration estimator instead of the legacy acceleration/jerk-limited prediction filter.")]
    public bool useKalmanPrediction;
    [Tooltip("Process noise for linear velocity prediction. Higher values react faster but trust noisy input more.")]
    [Min(0f)] public float kalmanLinearProcessNoise = 8f;
    [Tooltip("Measurement noise for linear velocity commands. Higher values smooth more but add lag.")]
    [Min(0.0001f)] public float kalmanLinearMeasurementNoise = 0.18f;
    [Tooltip("Process noise for yaw-rate prediction. Higher values react faster but trust noisy yaw input more.")]
    [Min(0f)] public float kalmanYawProcessNoise = 600f;
    [Tooltip("Measurement noise for yaw-rate commands. Higher values smooth more but add lag.")]
    [Min(0.0001f)] public float kalmanYawMeasurementNoise = 36f;

    [Header("Catch-Up")]
    [Tooltip("Smooth time for the 1PP rig to catch up to the ghost position.")]
    public float positionSmoothTime = 0.28f;
    [Tooltip("Maximum catch-up speed for the 1PP rig.")]
    public float maxCatchUpSpeed = 4f;
    [Tooltip("Smooth time for the 1PP rig yaw to catch up to the ghost yaw.")]
    public float yawSmoothTime = 0.22f;
    [Tooltip("Maximum yaw catch-up speed in degrees per second.")]
    public float maxYawCatchUpSpeedDeg = 120f;
    [Tooltip("Distance threshold for treating rig and ghost as converged.")]
    public float convergenceDistance = 0.05f;
    [Tooltip("Yaw threshold for convergence in degrees.")]
    public float convergenceYawThresholdDeg = 3f;

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

    [Header("Orientation & Vertical")]
    public HeadOffsetLocomotion.OrientationControlMode orientationMode = HeadOffsetLocomotion.OrientationControlMode.Dynamic;
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

    [Header("Feedback Hooks")]
    public UnityEvent onConverged = new UnityEvent();
    public UnityEvent onSeparated = new UnityEvent();

    [Header("Debug")]
    [SerializeField] bool isConverged = true;
    [SerializeField] bool hasActiveInput;
    [SerializeField] Vector3 ghostPosition;
    [SerializeField] float ghostYawDeg;
    [SerializeField] float distanceToGhost;
    [SerializeField] float currentTranslationPredictionWindow;
    [SerializeField] float currentYawPredictionWindow;
    [SerializeField] float currentTranslationPredictionConfidence;
    [SerializeField] float currentYawPredictionConfidence;
    [SerializeField] Vector3 currentCenterOffsetLocal;
    [SerializeField] Vector3 currentPredictionVelocityLocal;
    [SerializeField] Vector3 currentGhostOffsetLocal;
    [SerializeField] float currentHeadPitchDeg;
    [SerializeField] float currentVerticalCommand;

    Vector3 centerHeadLocal;
    Vector3 smoothedPlanarVelocityLocal;
    Vector3 rigPositionVelocity;
    float rigYawVelocity;
    Vector3 filteredPredictionVelocityWorld;
    Vector3 filteredPredictionAccelerationWorld;
    float filteredPredictionYawRateDeg;
    float filteredPredictionYawAccelerationDeg;
    Vector3 initialTargetPosition;
    Quaternion initialTargetRotation = Quaternion.identity;
    bool hasInitialTargetPose;
    bool hasGhostPose;
    bool warnedSetup;
    float sustainedInputTime;
    Vector3 lastRawPlanarDirectionWorld;
    bool hasLastRawPlanarDirection;
    float lastRawYawRateDeg;
    Kalman1D kalmanVelocityX;
    Kalman1D kalmanVelocityY;
    Kalman1D kalmanVelocityZ;
    Kalman1D kalmanYawRateDeg;

    public bool IsConverged => isConverged;
    public Vector3 GhostPosition => ghostPosition;
    public Quaternion GhostRotation => Quaternion.Euler(0f, ghostYawDeg, 0f);

    void Awake()
    {
        if (targetRig == null)
        {
            targetRig = transform;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }
    }

    void OnEnable()
    {
        WarnAboutConflicts();
        CacheInitialTargetPose();
        CaptureCenter();
        ResetMotionState();
        SnapGhostToRig();
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

        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        LocomotionCommand command = SampleCommand(dt);
        hasActiveInput = command.hasInput;

        UpdateGhostPose(command, dt);
        FollowGhost(dt);
        UpdateConvergenceState(forceNotify: false);
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
        UpdateConvergenceState(forceNotify: false);
    }

    [ContextMenu("Snap Ghost To Rig")]
    public void SnapGhostToRig()
    {
        if (targetRig == null)
        {
            return;
        }

        ghostPosition = targetRig.position;
        ghostYawDeg = targetRig.eulerAngles.y;
        hasGhostPose = true;
        currentGhostOffsetLocal = Vector3.zero;
        SyncGhostTransform();
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
            return;
        }

        centerHeadLocal = targetRig.InverseTransformPoint(head.position);
    }

    void ResetMotionState()
    {
        smoothedPlanarVelocityLocal = Vector3.zero;
        rigPositionVelocity = Vector3.zero;
        rigYawVelocity = 0f;
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

    void WarnAboutConflicts()
    {
        if (warnedSetup)
        {
            return;
        }

        warnedSetup = true;

        HeadOffsetLocomotion legacy = GetComponent<HeadOffsetLocomotion>();
        if (legacy != null && legacy.enabled)
        {
            Debug.LogWarning(
                "[PredictiveGhostAvatarLocomotion] HeadOffsetLocomotion is still enabled on the same object. Disable it to avoid two locomotion scripts fighting each other.",
                this);
        }

        if (ghostAvatar != null && targetRig != null && ghostAvatar.IsChildOf(targetRig))
        {
            Debug.LogWarning(
                "[PredictiveGhostAvatarLocomotion] Ghost avatar should live outside the XR rig hierarchy. If it is a child of the rig, the predicted separation will collapse.",
                ghostAvatar);
        }
    }

    LocomotionCommand SampleCommand(float dt)
    {
        Vector3 headLocalPos = targetRig.InverseTransformPoint(head.position);
        Vector3 centerOffsetLocal = headLocalPos - centerHeadLocal;
        Vector3 planarOffsetLocal = new Vector3(centerOffsetLocal.x, 0f, centerOffsetLocal.z);
        currentCenterOffsetLocal = centerOffsetLocal;

        float planarSpeed = ComputePlanarSpeed(planarOffsetLocal.magnitude);
        Vector3 planarDirectionLocal = ComputePlanarDirectionLocal(planarOffsetLocal);
        Vector3 rawPlanarVelocityLocal = planarDirectionLocal * planarSpeed;

        float planarLerp = 1f - Mathf.Exp(-Mathf.Max(0f, planarSmoothing) * dt);
        smoothedPlanarVelocityLocal = Vector3.Lerp(smoothedPlanarVelocityLocal, rawPlanarVelocityLocal, planarLerp);

        Vector3 headForwardLocal = targetRig.InverseTransformDirection(head.forward).normalized;
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
        if (useKalmanPrediction)
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
            predictionWorldVelocity = filteredWorldPredictionVelocity,
            rawYawRateDeg = rawYawRateDeg,
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

    void UpdateGhostPose(LocomotionCommand command, float dt)
    {
        if (!hasGhostPose)
        {
            SnapGhostToRig();
        }

        if (!command.hasInput)
        {
            currentGhostOffsetLocal = targetRig.InverseTransformDirection(ghostPosition - targetRig.position);
            SyncGhostTransform();
            return;
        }

        Vector3 predictedPosition = targetRig.position
            + command.predictionWorldVelocity * Mathf.Max(0f, command.translationPredictionWindow);
        Vector3 predictionOffset = predictedPosition - targetRig.position;

        float maxLead = Mathf.Max(0.01f, maxPredictionDistance);
        if (predictionOffset.sqrMagnitude > maxLead * maxLead)
        {
            predictedPosition = targetRig.position + predictionOffset.normalized * maxLead;
        }

        predictedPosition = ClampPosition(predictedPosition, positionLimits);
        float predictedYaw = targetRig.eulerAngles.y
            + command.predictionYawRateDeg * Mathf.Max(0f, command.yawPredictionWindow);

        if (snapGhostToPrediction)
        {
            ghostPosition = predictedPosition;
            ghostYawDeg = predictedYaw;
        }
        else
        {
            float posT = 1f - Mathf.Exp(-Mathf.Max(0f, ghostPositionResponse) * dt);
            float yawT = 1f - Mathf.Exp(-Mathf.Max(0f, ghostYawResponse) * dt);
            ghostPosition = Vector3.Lerp(ghostPosition, predictedPosition, posT);
            ghostYawDeg = Mathf.LerpAngle(ghostYawDeg, predictedYaw, yawT);
        }

        currentGhostOffsetLocal = targetRig.InverseTransformDirection(ghostPosition - targetRig.position);
        SyncGhostTransform();
    }

    void FollowGhost(float dt)
    {
        Vector3 nextPosition = Vector3.SmoothDamp(
            targetRig.position,
            ghostPosition,
            ref rigPositionVelocity,
            Mathf.Max(0.0001f, positionSmoothTime),
            ResolveSmoothDampMaxSpeed(maxCatchUpSpeed),
            dt);

        float nextYaw = Mathf.SmoothDampAngle(
            targetRig.eulerAngles.y,
            ghostYawDeg,
            ref rigYawVelocity,
            Mathf.Max(0.0001f, yawSmoothTime),
            ResolveSmoothDampMaxSpeed(maxYawCatchUpSpeedDeg),
            dt);

        targetRig.SetPositionAndRotation(
            ClampPosition(nextPosition, positionLimits),
            Quaternion.Euler(0f, nextYaw, 0f));
    }

    void SyncGhostTransform()
    {
        if (ghostAvatar == null)
        {
            return;
        }

        ghostAvatar.SetPositionAndRotation(
            ghostPosition,
            Quaternion.Euler(0f, ghostYawDeg, 0f));
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

        Vector3 headForwardLocal = targetRig.InverseTransformDirection(head.forward);
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
            case HeadOffsetLocomotion.OrientationControlMode.Static:
            {
                float threshold = staticYawThresholdDeg * Mathf.Deg2Rad;
                float maxRate = staticYawSpeedDeg * Mathf.Deg2Rad;
                return Mathf.Abs(headYaw) > threshold ? maxRate * Mathf.Sign(headYaw) : 0f;
            }
            case HeadOffsetLocomotion.OrientationControlMode.Coupled:
            {
                float maxRate = coupledYawMaxSpeedDeg * Mathf.Deg2Rad;
                return Mathf.Clamp(coupledYawGain * headYaw, -maxRate, maxRate);
            }
            case HeadOffsetLocomotion.OrientationControlMode.Dynamic:
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
            case HeadOffsetLocomotion.OrientationControlMode.Static:
                if (headPitchDeg >= staticPitchUpThresholdDeg)
                {
                    return staticPitchUpSpeed;
                }
                if (headPitchDeg <= -staticPitchDownThresholdDeg)
                {
                    return -staticPitchDownSpeed;
                }
                return 0f;

            case HeadOffsetLocomotion.OrientationControlMode.Coupled:
            {
                float refDeg = Mathf.Max(coupledPitchReferenceDeg, 1f);
                float maxSpeed = coupledPitchMaxSpeed;
                return Mathf.Clamp((headPitchDeg / refDeg) * maxSpeed, -maxSpeed, maxSpeed);
            }

            case HeadOffsetLocomotion.OrientationControlMode.Dynamic:
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

    static float ResolveSmoothDampMaxSpeed(float value)
    {
        return value <= 0f ? Mathf.Infinity : value;
    }

    struct LocomotionCommand
    {
        public Vector3 rawWorldVelocity;
        public Vector3 predictionWorldVelocity;
        public float rawYawRateDeg;
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
