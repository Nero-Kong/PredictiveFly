using UnityEngine;
using Leap;

[DisallowMultipleComponent]
public class LeapBoidHandController : MonoBehaviour {

    [Header ("References")]
    public BoidManager boidManager;
    public LeapProvider leapProvider;
    public Transform handTarget;
    public bool autoCreateLeapProvider = true;

    [Header ("Target Follow")]
    public bool autoCreateHandTarget = true;
    public bool replaceBoidTargetOnEnable = true;
    public Vector3 targetOffset = Vector3.zero;
    [Min (0f)] public float targetPositionSmoothing = 18f;

    [Header ("Speed Mapping (hand speed -> boid speed)")]
    [Min (0f)] public float handSpeedForIdle = 0.02f;
    [Min (0f)] public float handSpeedForMax = 0.8f;
    [Min (0f)] public float minSpeedAtIdle = 2f;
    [Min (0f)] public float minSpeedAtMax = 4f;
    [Min (0f)] public float maxSpeedAtIdle = 6f;
    [Min (0f)] public float maxSpeedAtMax = 12f;
    [Min (0f)] public float boidSpeedSmoothing = 10f;

    [Header ("Grab Mapping (hand open/close -> flock compactness)")]
    [Tooltip ("True: closed fist -> tighter flock. False: open hand -> tighter flock.")]
    public bool closedMeansMoreCohesion = true;
    [Min (0f)] public float cohesionOpenMultiplier = 0.65f;
    [Min (0f)] public float cohesionClosedMultiplier = 1.8f;
    [Min (0f)] public float separationOpenMultiplier = 1.5f;
    [Min (0f)] public float separationClosedMultiplier = 0.55f;
    [Min (0f)] public float flockShapeSmoothing = 12f;

    [Header ("Fallback")]
    public bool fallbackToIdleWhenNoHands = true;

    float baseCohesionWeight;
    float baseSeparationWeight;
    bool baseWeightsCaptured;

    bool hasRawCenter;
    Vector3 lastRawCenter;

    bool hasSmoothedCenter;
    Vector3 smoothedCenter;

    float desiredMinSpeed;
    float desiredMaxSpeed;
    float desiredCohesionWeight;
    float desiredSeparationWeight;

    void Awake () {
        ResolveReferences ();
    }

    void OnEnable () {
        ResolveReferences ();
        EnsureHandTarget ();
        CaptureBaseWeightsIfNeeded ();
        ResetDesiredToCurrent ();

        if (replaceBoidTargetOnEnable && boidManager != null && handTarget != null) {
            boidManager.SetTarget (handTarget);
        }
    }

    void LateUpdate () {
        if (boidManager == null) {
            ResolveReferences ();
            if (boidManager == null) {
                return;
            }
        }

        if (handTarget == null) {
            EnsureHandTarget ();
        }

        CaptureBaseWeightsIfNeeded ();

        bool hasHands = TryGetHandMetrics (out Vector3 handCenter, out float handSpeed, out float grabStrength);
        if (hasHands) {
            UpdateHandTarget (handCenter);
            UpdateDesiredSpeed (handSpeed);
            UpdateDesiredFlockShape (grabStrength);
        } else if (fallbackToIdleWhenNoHands) {
            UpdateDesiredSpeed (0f);
            desiredCohesionWeight = baseCohesionWeight;
            desiredSeparationWeight = baseSeparationWeight;
        }

        ApplyDesiredBoidValues ();
    }

    void ResolveReferences () {
        if (boidManager == null) {
            boidManager = GetComponent<BoidManager> ();
        }

        if (boidManager == null) {
            boidManager = FindFirstObjectByType<BoidManager> ();
        }

        if (leapProvider == null) {
            leapProvider = FindFirstObjectByType<LeapProvider> ();
            if (leapProvider == null && autoCreateLeapProvider) {
                leapProvider = AutoCreateLeapProvider ();
            }
        }
    }

    void EnsureHandTarget () {
        if (handTarget != null || !autoCreateHandTarget) {
            return;
        }

        GameObject targetGo = new GameObject ("BoidHandTarget");
        if (boidManager != null && boidManager.target != null) {
            targetGo.transform.position = boidManager.target.position;
            targetGo.transform.rotation = boidManager.target.rotation;
        } else {
            targetGo.transform.position = transform.position;
            targetGo.transform.rotation = Quaternion.identity;
        }

        handTarget = targetGo.transform;
    }

    void CaptureBaseWeightsIfNeeded () {
        if (baseWeightsCaptured || boidManager == null) {
            return;
        }

        baseCohesionWeight = Mathf.Max (0f, boidManager.cohesionWeight);
        baseSeparationWeight = Mathf.Max (0f, boidManager.seperateWeight);
        baseWeightsCaptured = true;
    }

    void ResetDesiredToCurrent () {
        if (boidManager == null) {
            return;
        }

        desiredMinSpeed = boidManager.minSpeed;
        desiredMaxSpeed = boidManager.maxSpeed;
        desiredCohesionWeight = boidManager.cohesionWeight;
        desiredSeparationWeight = boidManager.seperateWeight;
    }

    bool TryGetHandMetrics (out Vector3 center, out float speed, out float grabStrength) {
        center = Vector3.zero;
        speed = 0f;
        grabStrength = 0f;

        if (leapProvider == null) {
            leapProvider = FindFirstObjectByType<LeapProvider> ();
            if (leapProvider == null && autoCreateLeapProvider) {
                leapProvider = AutoCreateLeapProvider ();
            }
            if (leapProvider == null) {
                hasRawCenter = false;
                return false;
            }
        }

        Frame frame = leapProvider.CurrentFrame;
        if (frame == null || frame.Hands == null || frame.Hands.Count == 0) {
            hasRawCenter = false;
            return false;
        }

        int trackedCount = 0;
        for (int i = 0; i < frame.Hands.Count; i++) {
            Hand hand = frame.Hands[i];
            if (hand == null) {
                continue;
            }

            center += hand.PalmPosition;
            grabStrength += Mathf.Clamp01 (hand.GrabStrength);
            trackedCount++;
        }

        if (trackedCount == 0) {
            hasRawCenter = false;
            return false;
        }

        center /= trackedCount;
        grabStrength /= trackedCount;

        float dt = Mathf.Max (Time.deltaTime, 1e-4f);
        if (hasRawCenter) {
            speed = (center - lastRawCenter).magnitude / dt;
        } else {
            speed = 0f;
        }

        lastRawCenter = center;
        hasRawCenter = true;
        return true;
    }

    void UpdateHandTarget (Vector3 handCenter) {
        if (handTarget == null) {
            return;
        }

        Vector3 desiredPosition = handCenter + targetOffset;
        if (!hasSmoothedCenter || targetPositionSmoothing <= 0f) {
            smoothedCenter = desiredPosition;
            hasSmoothedCenter = true;
        } else {
            float t = 1f - Mathf.Exp (-targetPositionSmoothing * Time.deltaTime);
            smoothedCenter = Vector3.Lerp (smoothedCenter, desiredPosition, t);
        }

        handTarget.position = smoothedCenter;
    }

    void UpdateDesiredSpeed (float handSpeed) {
        float maxRef = Mathf.Max (handSpeedForIdle + 1e-5f, handSpeedForMax);
        float speedNorm = Mathf.InverseLerp (handSpeedForIdle, maxRef, handSpeed);

        desiredMinSpeed = Mathf.Lerp (minSpeedAtIdle, minSpeedAtMax, speedNorm);
        desiredMaxSpeed = Mathf.Lerp (maxSpeedAtIdle, maxSpeedAtMax, speedNorm);

        if (desiredMaxSpeed < desiredMinSpeed) {
            desiredMaxSpeed = desiredMinSpeed;
        }
    }

    void UpdateDesiredFlockShape (float grabStrength) {
        if (!baseWeightsCaptured) {
            return;
        }

        float t = closedMeansMoreCohesion ? grabStrength : 1f - grabStrength;

        float cohesionOpen = baseCohesionWeight * cohesionOpenMultiplier;
        float cohesionClosed = baseCohesionWeight * cohesionClosedMultiplier;
        desiredCohesionWeight = Mathf.Lerp (cohesionOpen, cohesionClosed, t);

        float separationOpen = baseSeparationWeight * separationOpenMultiplier;
        float separationClosed = baseSeparationWeight * separationClosedMultiplier;
        desiredSeparationWeight = Mathf.Lerp (separationOpen, separationClosed, t);
    }

    void ApplyDesiredBoidValues () {
        if (boidManager == null) {
            return;
        }

        float speedT = SmoothingAlpha (boidSpeedSmoothing);
        boidManager.minSpeed = Mathf.Lerp (boidManager.minSpeed, desiredMinSpeed, speedT);
        boidManager.maxSpeed = Mathf.Lerp (boidManager.maxSpeed, desiredMaxSpeed, speedT);

        if (boidManager.maxSpeed < boidManager.minSpeed) {
            boidManager.maxSpeed = boidManager.minSpeed;
        }

        float shapeT = SmoothingAlpha (flockShapeSmoothing);
        boidManager.cohesionWeight = Mathf.Lerp (boidManager.cohesionWeight, desiredCohesionWeight, shapeT);
        boidManager.seperateWeight = Mathf.Lerp (boidManager.seperateWeight, desiredSeparationWeight, shapeT);
    }

    float SmoothingAlpha (float smoothing) {
        if (smoothing <= 0f) {
            return 1f;
        }

        return 1f - Mathf.Exp (-smoothing * Time.deltaTime);
    }

    LeapProvider AutoCreateLeapProvider () {
        Camera mainCam = Camera.main;
        if (mainCam == null) {
            return null;
        }

        GameObject providerGo = new GameObject ("AutoLeapXRServiceProvider");
        LeapXRServiceProvider xrProvider = providerGo.AddComponent<LeapXRServiceProvider> ();
        xrProvider.mainCamera = mainCam;
        xrProvider.deviceOffsetMode = LeapXRServiceProvider.DeviceOffsetMode.ManualHeadOffset;
        xrProvider.deviceOffsetYAxis = 0f;
        xrProvider.deviceOffsetZAxis = 0.08f;
        xrProvider.deviceTiltXAxis = 0f;

        return xrProvider;
    }
}
