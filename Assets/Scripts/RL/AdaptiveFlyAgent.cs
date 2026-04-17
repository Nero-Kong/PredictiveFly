using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class AdaptiveFlyAgent : Agent
{
    public enum PolicyOutputMode
    {
        AbsoluteAction,
        ResidualOnDynamicRule
    }

    private struct IntentTargets
    {
        public float desiredVx;
        public float desiredVz;
        public float desiredVy;
        public float desiredYawRateDeg;
        public float confidence;
    }

    [Header("References")]
    [SerializeField] private Transform rigRoot;
    [SerializeField] private Transform head;
    [SerializeField] private HeadIntentFeatureExtractor featureExtractor;

    [Header("Observation Normalization")]
    [SerializeField, Min(0.01f)] private float planarOffsetObservationRange = 0.35f;
    [SerializeField, Min(1f)] private float pitchObservationRangeDeg = 45f;
    [SerializeField, Min(1f)] private float yawObservationRangeDeg = 90f;
    [SerializeField, Min(0.01f)] private float planarRateObservationRange = 2f;
    [SerializeField, Min(1f)] private float angularRateObservationRangeDeg = 240f;

    [Header("Action Limits")]
    [SerializeField, Min(0f)] private float maxPlanarSpeed = 6f;
    [SerializeField, Min(0f)] private float maxVerticalSpeed = 4f;
    [SerializeField, Min(0f)] private float maxYawRateDeg = 120f;

    [Header("Policy Output")]
    [SerializeField] private PolicyOutputMode policyOutputMode = PolicyOutputMode.ResidualOnDynamicRule;
    [SerializeField, Range(0f, 1f)] private float residualActionScale = 0.35f;

    [Header("Dynamic Rule Baseline")]
    [SerializeField, Min(0f)] private float planarDeadZone = 0.04f;
    [SerializeField, Min(0.001f)] private float planarMaxOffset = 0.25f;
    [SerializeField, Min(0.1f)] private float planarResponseExponent = 1.8f;
    [SerializeField, Range(0f, 1f)] private float headDirectionBlend = 0.55f;
    [SerializeField, Min(0f)] private float planarSmoothing = 10f;
    [SerializeField, Min(0f)] private float dynamicYawGain = 0.5f;
    [SerializeField, Min(0f)] private float dynamicYawThresholdMinDeg = 12f;
    [SerializeField, Min(0f)] private float dynamicYawThresholdMaxDeg = 30f;
    [SerializeField, Min(0f)] private float dynamicYawSpeedThreshold = 7f;
    [SerializeField, Min(0f)] private float dynamicYawSpeedScale = 10f;
    [SerializeField] private float dynamicPitchUpLambda = 0.3f;
    [SerializeField] private float dynamicPitchUpDeltaDeg = 30f;
    [SerializeField] private float dynamicPitchDownLambda = -0.4f;
    [SerializeField] private float dynamicPitchDownDeltaDeg = -18f;

    [Header("Safety Layer")]
    [SerializeField, Min(0f)] private float maxPlanarAccel = 8f;
    [SerializeField, Min(0f)] private float maxVerticalAccel = 6f;
    [SerializeField, Min(0f)] private float maxYawAccelDeg = 240f;
    [SerializeField, Min(0f)] private float maxPlanarJerk = 35f;
    [SerializeField, Min(0f)] private float maxVerticalJerk = 20f;
    [SerializeField, Min(0f)] private float maxYawJerkDeg = 900f;
    [SerializeField] private Vector3 positionLimits = 2000f * Vector3.one;
    [SerializeField] private bool endEpisodeOnBoundsHit;

    [Header("Episode")]
    [SerializeField] private bool resetRigPoseOnEpisodeBegin = true;

    [Header("Decision Fallback")]
    [SerializeField] private bool autoRequestDecisionWithoutRequester = true;
    [SerializeField, Min(1)] private int fallbackDecisionPeriod = 1;

    [Header("Reward")]
    [SerializeField] private bool enableTrainingReward = true;
    [SerializeField, Min(0f)] private float intentAlignPlanarScale = 1.2f;
    [SerializeField, Min(0f)] private float intentAlignVerticalScale = 0.5f;
    [SerializeField, Min(0f)] private float intentAlignYawScale = 0.7f;
    [SerializeField, Min(0f)] private float actionPenaltyScale = 0.001f;
    [SerializeField, Min(0f)] private float accelerationPenaltyScale = 0.003f;
    [SerializeField, Min(0f)] private float jerkPenaltyScale = 0.002f;
    [SerializeField, Min(0f)] private float driftPenaltyScale = 0.015f;
    [SerializeField, Min(0f)] private float residualPenaltyScale = 0.001f;
    [SerializeField, Min(0f)] private float alivePenalty = 0.0005f;
    [SerializeField] private float outOfBoundsPenalty = -1f;

    [Header("Intent Proxy")]
    [SerializeField, Min(0.01f)] private float planarFullCommandOffset = 0.25f;
    [SerializeField, Min(1f)] private float pitchFullCommandDeg = 25f;
    [SerializeField, Min(1f)] private float yawFullCommandDeg = 45f;
    [SerializeField, Min(0.001f)] private float planarConfidenceStart = 0.03f;
    [SerializeField, Min(0.1f)] private float pitchConfidenceStartDeg = 4f;
    [SerializeField, Min(0.1f)] private float yawConfidenceStartDeg = 6f;

    [Header("Drift Termination")]
    [SerializeField] private bool endEpisodeOnPersistentDrift = true;
    [SerializeField, Range(0f, 1f)] private float driftConfidenceThreshold = 0.1f;
    [SerializeField, Min(0f)] private float driftSpeedThreshold = 0.3f;
    [SerializeField, Min(0.1f)] private float driftDurationSeconds = 1f;
    [SerializeField] private float driftTerminationPenalty = -0.5f;

    [Header("Inference Stabilizer")]
    [SerializeField] private bool enableInferenceStabilizer = true;
    [SerializeField] private bool inferenceOnlyStabilizer = true;
    [SerializeField, Range(0f, 1f)] private float holdIntentEnter = 0.12f;
    [SerializeField, Range(0f, 1f)] private float holdIntentExit = 0.18f;
    [SerializeField, Min(0f)] private float holdActionMagnitude = 0.08f;
    [SerializeField, Min(1f)] private float verticalInferenceBoost = 1.8f;
    [SerializeField, Min(1f)] private float yawInferenceBoost = 1.15f;
    [SerializeField, Range(0f, 1f)] private float verticalIntentBlend = 0.45f;
    [SerializeField, Range(0f, 1f)] private float yawIntentBlend = 0.25f;

    private HeadIntentFeatureExtractor.FeatureSample latestFeatureSample;
    private float latestIntentConfidence;
    private Vector2 smoothedRulePlanarVelocityLocal;

    private Vector3 initialRigPosition;
    private Quaternion initialRigRotation;

    private Vector3 localVelocity;
    private Vector3 localAcceleration;
    private float yawRateDeg;
    private float yawAccelerationDeg;

    private Vector4 lastAppliedAction;
    private int fallbackDecisionCounter;
    private float lowIntentMovingTime;
    private bool inferenceHold;
    private BehaviorParameters behaviorParameters;

    public override void Initialize()
    {
        ResolveReferences();
        behaviorParameters = GetComponent<BehaviorParameters>();

        initialRigPosition = rigRoot.position;
        initialRigRotation = rigRoot.rotation;

        latestFeatureSample = default;
        latestIntentConfidence = 0f;
        localVelocity = Vector3.zero;
        localAcceleration = Vector3.zero;
        yawRateDeg = 0f;
        yawAccelerationDeg = 0f;
        lastAppliedAction = Vector4.zero;
        fallbackDecisionCounter = 0;
        lowIntentMovingTime = 0f;
        inferenceHold = false;
        smoothedRulePlanarVelocityLocal = Vector2.zero;
    }

    public override void OnEpisodeBegin()
    {
        ResolveReferences();

        if (resetRigPoseOnEpisodeBegin)
        {
            rigRoot.SetPositionAndRotation(initialRigPosition, initialRigRotation);
        }

        localVelocity = Vector3.zero;
        localAcceleration = Vector3.zero;
        yawRateDeg = 0f;
        yawAccelerationDeg = 0f;
        lastAppliedAction = Vector4.zero;
        latestIntentConfidence = 0f;
        fallbackDecisionCounter = 0;
        lowIntentMovingTime = 0f;
        inferenceHold = false;
        smoothedRulePlanarVelocityLocal = Vector2.zero;

        if (featureExtractor != null)
        {
            featureExtractor.SetReferences(rigRoot, head);
            featureExtractor.Calibrate();
        }
    }

    private void FixedUpdate()
    {
        if (!autoRequestDecisionWithoutRequester)
        {
            return;
        }

        if (HasDecisionRequester())
        {
            return;
        }

        fallbackDecisionCounter++;
        if (fallbackDecisionCounter >= Mathf.Max(1, fallbackDecisionPeriod))
        {
            fallbackDecisionCounter = 0;
            RequestDecision();
        }
        else
        {
            RequestAction();
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        latestFeatureSample = SampleFeatureSource(Time.fixedDeltaTime);
        latestIntentConfidence = ComputeIntentConfidence(latestFeatureSample.core);

        sensor.AddObservation(SafeNormalizeSigned(latestFeatureSample.core.x, planarOffsetObservationRange));
        sensor.AddObservation(SafeNormalizeSigned(latestFeatureSample.core.y, planarOffsetObservationRange));
        sensor.AddObservation(SafeNormalizeSigned(latestFeatureSample.core.z, pitchObservationRangeDeg));
        sensor.AddObservation(SafeNormalizeSigned(latestFeatureSample.core.w, yawObservationRangeDeg));

        sensor.AddObservation(SafeNormalizeSigned(latestFeatureSample.rates.x, planarRateObservationRange));
        sensor.AddObservation(SafeNormalizeSigned(latestFeatureSample.rates.y, planarRateObservationRange));
        sensor.AddObservation(SafeNormalizeSigned(latestFeatureSample.rates.z, angularRateObservationRangeDeg));
        sensor.AddObservation(SafeNormalizeSigned(latestFeatureSample.rates.w, angularRateObservationRangeDeg));

        sensor.AddObservation(lastAppliedAction.x);
        sensor.AddObservation(lastAppliedAction.y);
        sensor.AddObservation(lastAppliedAction.z);
        sensor.AddObservation(lastAppliedAction.w);

        sensor.AddObservation(SafeNormalizeSigned(localVelocity.x, maxPlanarSpeed));
        sensor.AddObservation(SafeNormalizeSigned(localVelocity.y, maxVerticalSpeed));
        sensor.AddObservation(SafeNormalizeSigned(localVelocity.z, maxPlanarSpeed));
        sensor.AddObservation(SafeNormalizeSigned(yawRateDeg, maxYawRateDeg));
        sensor.AddObservation(latestIntentConfidence);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (rigRoot == null)
        {
            return;
        }

        float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);
        Vector4 policyAction = ReadAction(actions);
        IntentTargets intent = BuildIntentTargets(latestFeatureSample.core);
        Vector4 ruleAction = BuildDynamicRuleAction(dt);
        Vector4 action = CombinePolicyAndRule(policyAction, ruleAction);
        action = ApplyInferenceStabilizer(action, intent);

        Vector3 oldAcceleration = localAcceleration;
        float oldYawAcceleration = yawAccelerationDeg;
        bool hitBounds = ApplyMotion(action, dt);

        float actionCost = action.x * action.x + action.y * action.y + action.z * action.z + action.w * action.w;
        float policyActionCost = policyAction.x * policyAction.x + policyAction.y * policyAction.y + policyAction.z * policyAction.z + policyAction.w * policyAction.w;
        Vector3 jerk = (localAcceleration - oldAcceleration) / dt;
        float yawJerkRad = ((yawAccelerationDeg - oldYawAcceleration) / dt) * Mathf.Deg2Rad;
        float jerkCost = jerk.sqrMagnitude + yawJerkRad * yawJerkRad;
        float accelerationCost = localAcceleration.sqrMagnitude + Mathf.Pow(yawAccelerationDeg * Mathf.Deg2Rad, 2f);
        float motionCost = localVelocity.sqrMagnitude + Mathf.Pow(yawRateDeg * Mathf.Deg2Rad, 2f);

        if (!enableTrainingReward)
        {
            return;
        }

        float planarError = Vector2.Distance(
            new Vector2(localVelocity.x, localVelocity.z),
            new Vector2(intent.desiredVx, intent.desiredVz));
        float verticalError = Mathf.Abs(localVelocity.y - intent.desiredVy);
        float yawErrorDeg = Mathf.Abs(yawRateDeg - intent.desiredYawRateDeg);

        float planarAlign = Mathf.Clamp(1f - planarError / Mathf.Max(maxPlanarSpeed, 1e-4f), -1f, 1f);
        float verticalAlign = Mathf.Clamp(1f - verticalError / Mathf.Max(maxVerticalSpeed, 1e-4f), -1f, 1f);
        float yawAlign = Mathf.Clamp(1f - yawErrorDeg / Mathf.Max(maxYawRateDeg, 1e-4f), -1f, 1f);

        AddReward(intentAlignPlanarScale * intent.confidence * planarAlign);
        AddReward(intentAlignVerticalScale * intent.confidence * verticalAlign);
        AddReward(intentAlignYawScale * intent.confidence * yawAlign);

        AddReward(-alivePenalty);
        AddReward(-actionPenaltyScale * actionCost);
        AddReward(-accelerationPenaltyScale * accelerationCost);
        AddReward(-jerkPenaltyScale * jerkCost);
        AddReward(-driftPenaltyScale * (1f - intent.confidence) * motionCost);

        if (policyOutputMode == PolicyOutputMode.ResidualOnDynamicRule)
        {
            AddReward(-residualPenaltyScale * policyActionCost);
        }

        if (hitBounds)
        {
            AddReward(outOfBoundsPenalty);
            if (endEpisodeOnBoundsHit)
            {
                EndEpisode();
                return;
            }
        }

        if (!endEpisodeOnPersistentDrift)
        {
            return;
        }

        bool lowConfidence = intent.confidence < driftConfidenceThreshold;
        bool movingTooMuch = localVelocity.magnitude > driftSpeedThreshold;
        lowIntentMovingTime = (lowConfidence && movingTooMuch) ? lowIntentMovingTime + dt : 0f;

        if (lowIntentMovingTime >= driftDurationSeconds)
        {
            AddReward(driftTerminationPenalty);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        latestFeatureSample = SampleFeatureSource(Time.fixedDeltaTime);
        latestIntentConfidence = ComputeIntentConfidence(latestFeatureSample.core);

        Vector4 ruleAction = BuildDynamicRuleAction(Mathf.Max(Time.fixedDeltaTime, 1e-4f));

        ActionSegment<float> continuous = actionsOut.ContinuousActions;
        if (continuous.Length < 4)
        {
            return;
        }

        if (policyOutputMode == PolicyOutputMode.ResidualOnDynamicRule)
        {
            continuous[0] = 0f;
            continuous[1] = 0f;
            continuous[2] = 0f;
            continuous[3] = 0f;
            return;
        }

        continuous[0] = ruleAction.x;
        continuous[1] = ruleAction.y;
        continuous[2] = ruleAction.z;
        continuous[3] = ruleAction.w;
    }

    private bool ApplyMotion(Vector4 action, float dt)
    {
        float desiredVx = action.x * maxPlanarSpeed;
        float desiredVz = action.y * maxPlanarSpeed;
        float desiredVy = action.z * maxVerticalSpeed;
        float desiredYawRate = action.w * maxYawRateDeg;

        float accelX = localAcceleration.x;
        float accelZ = localAcceleration.z;
        float accelY = localAcceleration.y;

        localVelocity.x = StepWithJerkLimit(
            localVelocity.x,
            desiredVx,
            ref accelX,
            maxPlanarAccel,
            maxPlanarJerk,
            dt);

        localVelocity.z = StepWithJerkLimit(
            localVelocity.z,
            desiredVz,
            ref accelZ,
            maxPlanarAccel,
            maxPlanarJerk,
            dt);

        localVelocity.y = StepWithJerkLimit(
            localVelocity.y,
            desiredVy,
            ref accelY,
            maxVerticalAccel,
            maxVerticalJerk,
            dt);

        localAcceleration = new Vector3(accelX, accelY, accelZ);

        yawRateDeg = StepWithJerkLimit(
            yawRateDeg,
            desiredYawRate,
            ref yawAccelerationDeg,
            maxYawAccelDeg,
            maxYawJerkDeg,
            dt);

        Vector3 worldVelocity = rigRoot.TransformDirection(localVelocity);
        Vector3 unclamped = rigRoot.position + worldVelocity * dt;
        Vector3 clamped = ClampPosition(unclamped, positionLimits);
        bool hitBounds = (clamped - unclamped).sqrMagnitude > 1e-10f;

        rigRoot.position = clamped;
        rigRoot.Rotate(Vector3.up, yawRateDeg * dt, Space.World);

        lastAppliedAction = new Vector4(
            SafeNormalizeSigned(localVelocity.x, maxPlanarSpeed),
            SafeNormalizeSigned(localVelocity.z, maxPlanarSpeed),
            SafeNormalizeSigned(localVelocity.y, maxVerticalSpeed),
            SafeNormalizeSigned(yawRateDeg, maxYawRateDeg));

        return hitBounds;
    }

    private IntentTargets BuildIntentTargets(Vector4 core)
    {
        Vector2 planar = new Vector2(core.x, core.y);
        float planarMagnitude = planar.magnitude;
        Vector2 planarDirection = planarMagnitude > 1e-5f ? planar / planarMagnitude : Vector2.zero;
        float planarCommand = Mathf.Clamp01(planarMagnitude / Mathf.Max(planarFullCommandOffset, 1e-4f));
        float planarSpeed = planarCommand * maxPlanarSpeed;

        return new IntentTargets
        {
            desiredVx = planarDirection.x * planarSpeed,
            desiredVz = planarDirection.y * planarSpeed,
            desiredVy = Mathf.Clamp(core.z / Mathf.Max(pitchFullCommandDeg, 1f), -1f, 1f) * maxVerticalSpeed,
            desiredYawRateDeg = Mathf.Clamp(core.w / Mathf.Max(yawFullCommandDeg, 1f), -1f, 1f) * maxYawRateDeg,
            confidence = ComputeIntentConfidence(core)
        };
    }

    private Vector4 ApplyInferenceStabilizer(Vector4 action, IntentTargets intent)
    {
        if (!enableInferenceStabilizer)
        {
            return action;
        }

        if (inferenceOnlyStabilizer && !IsInferenceOnlyMode())
        {
            return action;
        }

        if (inferenceHold)
        {
            inferenceHold = intent.confidence <= holdIntentExit;
        }
        else if (intent.confidence < holdIntentEnter)
        {
            inferenceHold = true;
        }

        float actionMagnitude = Mathf.Max(
            Mathf.Abs(action.x),
            Mathf.Max(Mathf.Abs(action.y), Mathf.Max(Mathf.Abs(action.z), Mathf.Abs(action.w))));

        if (inferenceHold || (intent.confidence < holdIntentExit && actionMagnitude < holdActionMagnitude))
        {
            return Vector4.zero;
        }

        float verticalNormTarget = SafeNormalizeSigned(intent.desiredVy, maxVerticalSpeed);
        float yawNormTarget = SafeNormalizeSigned(intent.desiredYawRateDeg, maxYawRateDeg);

        action.z = Mathf.Clamp(action.z * verticalInferenceBoost, -1f, 1f);
        action.w = Mathf.Clamp(action.w * yawInferenceBoost, -1f, 1f);

        action.z = Mathf.Lerp(action.z, verticalNormTarget, Mathf.Clamp01(verticalIntentBlend));
        action.w = Mathf.Lerp(action.w, yawNormTarget, Mathf.Clamp01(yawIntentBlend));

        return action;
    }

    private bool IsInferenceOnlyMode()
    {
        return behaviorParameters != null && behaviorParameters.BehaviorType == BehaviorType.InferenceOnly;
    }

    private Vector4 CombinePolicyAndRule(Vector4 policyAction, Vector4 ruleAction)
    {
        if (policyOutputMode != PolicyOutputMode.ResidualOnDynamicRule)
        {
            return policyAction;
        }

        return new Vector4(
            Mathf.Clamp(ruleAction.x + policyAction.x * residualActionScale, -1f, 1f),
            Mathf.Clamp(ruleAction.y + policyAction.y * residualActionScale, -1f, 1f),
            Mathf.Clamp(ruleAction.z + policyAction.z * residualActionScale, -1f, 1f),
            Mathf.Clamp(ruleAction.w + policyAction.w * residualActionScale, -1f, 1f));
    }

    private Vector4 BuildDynamicRuleAction(float dt)
    {
        if (rigRoot == null || head == null)
        {
            return Vector4.zero;
        }

        Vector3 planarOffsetLocal = new Vector3(latestFeatureSample.core.x, 0f, latestFeatureSample.core.y);
        float planarSpeed = ComputePlanarSpeed(planarOffsetLocal.magnitude);
        Vector3 planarDirectionLocal = ComputePlanarDirectionLocal(planarOffsetLocal);
        Vector3 desiredPlanarVelocityLocal = planarDirectionLocal * planarSpeed;

        float planarLerp = 1f - Mathf.Exp(-Mathf.Max(0f, planarSmoothing) * Mathf.Max(dt, 1e-4f));
        smoothedRulePlanarVelocityLocal = Vector2.Lerp(
            smoothedRulePlanarVelocityLocal,
            new Vector2(desiredPlanarVelocityLocal.x, desiredPlanarVelocityLocal.z),
            planarLerp);

        Vector3 headForwardLocal = rigRoot.InverseTransformDirection(head.forward).normalized;
        Vector2 planarCommand = maxPlanarSpeed > 1e-6f
            ? smoothedRulePlanarVelocityLocal / maxPlanarSpeed
            : Vector2.zero;

        float yawRateRad = ComputeDynamicYawRate(headForwardLocal, planarCommand);
        float verticalSpeed = ComputeDynamicVerticalSpeed(headForwardLocal);

        return new Vector4(
            SafeNormalizeSigned(smoothedRulePlanarVelocityLocal.x, maxPlanarSpeed),
            SafeNormalizeSigned(smoothedRulePlanarVelocityLocal.y, maxPlanarSpeed),
            SafeNormalizeSigned(verticalSpeed, maxVerticalSpeed),
            SafeNormalizeSigned(yawRateRad * Mathf.Rad2Deg, maxYawRateDeg));
    }

    private float ComputePlanarSpeed(float offsetMagnitude)
    {
        if (offsetMagnitude <= planarDeadZone)
        {
            return 0f;
        }

        float maxOffset = Mathf.Max(planarDeadZone + 1e-4f, planarMaxOffset);
        float normalized = Mathf.Clamp01((offsetMagnitude - planarDeadZone) / (maxOffset - planarDeadZone));
        float curved = Mathf.Pow(normalized, Mathf.Max(0.1f, planarResponseExponent));
        return curved * Mathf.Max(0f, maxPlanarSpeed);
    }

    private Vector3 ComputePlanarDirectionLocal(Vector3 planarOffsetLocal)
    {
        Vector3 leanDirLocal = planarOffsetLocal.sqrMagnitude > 1e-8f
            ? planarOffsetLocal.normalized
            : Vector3.zero;

        Vector3 headForwardLocal = rigRoot.InverseTransformDirection(head.forward);
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

    private float ComputeDynamicYawRate(Vector3 headForwardLocal, Vector2 planarCommand)
    {
        float headYaw = Mathf.Atan2(headForwardLocal.x, headForwardLocal.z);
        float maxRate = maxYawRateDeg * Mathf.Deg2Rad;
        float thMin = dynamicYawThresholdMinDeg * Mathf.Deg2Rad;
        float thMax = dynamicYawThresholdMaxDeg * Mathf.Deg2Rad;

        float planarSpeedNormalized = Mathf.Min(1f, planarCommand.magnitude);
        float vd = planarSpeedNormalized * dynamicYawSpeedScale;
        float deltaTheta = (thMin - thMax) / (1f + Mathf.Exp(dynamicYawSpeedThreshold - vd)) + thMax;
        float lambda = 1f / (1f + Mathf.Exp(-dynamicYawGain * (Mathf.Abs(headYaw) - deltaTheta)));
        float scaled = Mathf.Max(0f, 2f * lambda - 1f);
        float yawRate = maxRate * scaled * Mathf.Sign(headYaw);
        return Mathf.Clamp(yawRate, -maxRate, maxRate);
    }

    private float ComputeDynamicVerticalSpeed(Vector3 headForwardLocal)
    {
        float planarNorm = Mathf.Sqrt(headForwardLocal.x * headForwardLocal.x + headForwardLocal.z * headForwardLocal.z);
        float headPitchDeg = Mathf.Atan2(headForwardLocal.y, planarNorm) * Mathf.Rad2Deg;

        float lambda;
        float delta;
        float signFactor;

        if (headPitchDeg >= 0f)
        {
            lambda = dynamicPitchUpLambda;
            delta = dynamicPitchUpDeltaDeg;
            signFactor = 1f;
        }
        else
        {
            lambda = dynamicPitchDownLambda;
            delta = dynamicPitchDownDeltaDeg;
            signFactor = -1f;
        }

        float logistic = 1f / (1f + Mathf.Exp(-lambda * (headPitchDeg - delta)));
        logistic = Mathf.Clamp01(logistic);
        return signFactor * logistic * Mathf.Max(0f, maxVerticalSpeed);
    }

    private float ComputeIntentConfidence(Vector4 core)
    {
        float planarConfidence = Mathf.Clamp01(
            new Vector2(core.x, core.y).magnitude / Mathf.Max(planarConfidenceStart, 1e-4f));
        float pitchConfidence = Mathf.Clamp01(Mathf.Abs(core.z) / Mathf.Max(pitchConfidenceStartDeg, 0.1f));
        float yawConfidence = Mathf.Clamp01(Mathf.Abs(core.w) / Mathf.Max(yawConfidenceStartDeg, 0.1f));
        return Mathf.Max(planarConfidence, Mathf.Max(pitchConfidence, yawConfidence));
    }

    private HeadIntentFeatureExtractor.FeatureSample SampleFeatureSource(float dt)
    {
        if (featureExtractor == null)
        {
            return default;
        }

        return featureExtractor.Sample(dt);
    }

    private Vector4 ReadAction(ActionBuffers actions)
    {
        ActionSegment<float> continuous = actions.ContinuousActions;
        float vx = continuous.Length > 0 ? Mathf.Clamp(continuous[0], -1f, 1f) : 0f;
        float vz = continuous.Length > 1 ? Mathf.Clamp(continuous[1], -1f, 1f) : 0f;
        float vy = continuous.Length > 2 ? Mathf.Clamp(continuous[2], -1f, 1f) : 0f;
        float yaw = continuous.Length > 3 ? Mathf.Clamp(continuous[3], -1f, 1f) : 0f;
        return new Vector4(vx, vz, vy, yaw);
    }

    private void ResolveReferences()
    {
        if (rigRoot == null)
        {
            rigRoot = transform;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        if (featureExtractor == null)
        {
            featureExtractor = GetComponent<HeadIntentFeatureExtractor>();
        }

        if (featureExtractor == null)
        {
            featureExtractor = gameObject.AddComponent<HeadIntentFeatureExtractor>();
        }

        featureExtractor.SetReferences(rigRoot, head);
    }

    private bool HasDecisionRequester()
    {
        Component[] components = GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
            {
                continue;
            }

            if (component.GetType().Name == "DecisionRequester")
            {
                return true;
            }
        }

        return false;
    }

    private static float StepWithJerkLimit(
        float currentValue,
        float targetValue,
        ref float currentAcceleration,
        float maxAcceleration,
        float maxJerk,
        float dt)
    {
        if (dt <= 0f)
        {
            return currentValue;
        }

        if (maxAcceleration <= 0f)
        {
            currentAcceleration = 0f;
            return targetValue;
        }

        float desiredAcceleration = (targetValue - currentValue) / dt;
        desiredAcceleration = Mathf.Clamp(desiredAcceleration, -maxAcceleration, maxAcceleration);

        if (maxJerk > 0f)
        {
            float maxAccelerationDelta = maxJerk * dt;
            float accelerationDelta = desiredAcceleration - currentAcceleration;
            currentAcceleration += Mathf.Clamp(accelerationDelta, -maxAccelerationDelta, maxAccelerationDelta);
        }
        else
        {
            currentAcceleration = desiredAcceleration;
        }

        currentAcceleration = Mathf.Clamp(currentAcceleration, -maxAcceleration, maxAcceleration);
        return currentValue + currentAcceleration * dt;
    }

    private static Vector3 ClampPosition(Vector3 position, Vector3 limits)
    {
        return new Vector3(
            Mathf.Clamp(position.x, -limits.x, limits.x),
            Mathf.Clamp(position.y, -limits.y, limits.y),
            Mathf.Clamp(position.z, -limits.z, limits.z));
    }

    private static float SafeNormalizeSigned(float value, float maxAbs)
    {
        if (maxAbs <= 1e-6f)
        {
            return 0f;
        }

        return Mathf.Clamp(value / maxAbs, -1f, 1f);
    }
}

