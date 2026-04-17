using UnityEngine;

public class HeadIntentFeatureExtractor : MonoBehaviour
{
    [System.Serializable]
    public struct FeatureSample
    {
        public Vector4 core;
        public Vector4 rates;
    }

    [Header("References")]
    [SerializeField] private Transform rigRoot;
    [SerializeField] private Transform head;

    [Header("Calibration")]
    [SerializeField] private bool autoCalibrateOnEnable = true;
    [SerializeField] private KeyCode calibrateKey = KeyCode.C;

    [Header("Filter")]
    [SerializeField, Min(0f)] private float lowPassCutoffHz = 8f;
    [SerializeField, Min(0f)] private float planarNoiseFloor = 0.002f;
    [SerializeField, Min(0f)] private float angularNoiseFloorDeg = 0.2f;

    private const float MinDeltaTime = 1e-4f;

    private Vector3 anchorHeadLocalPos;
    private float anchorPitchDeg;
    private float anchorYawDeg;
    private Vector4 filteredCore;
    private Vector4 previousFilteredCore;
    private bool hasFilterState;
    private bool calibrated;
    private FeatureSample currentSample;

    public Transform RigRoot => rigRoot;
    public Transform Head => head;
    public bool IsCalibrated => calibrated;
    public FeatureSample CurrentSample => currentSample;

    public void SetReferences(Transform rig, Transform headTransform)
    {
        rigRoot = rig;
        head = headTransform;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (autoCalibrateOnEnable)
        {
            Calibrate();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(calibrateKey))
        {
            Calibrate();
        }
    }

    public void Calibrate()
    {
        ResolveReferences();
        if (!HasValidReferences())
        {
            calibrated = false;
            return;
        }

        anchorHeadLocalPos = rigRoot.InverseTransformPoint(head.position);
        Vector3 headForwardLocal = rigRoot.InverseTransformDirection(head.forward).normalized;
        ComputePitchYaw(headForwardLocal, out anchorPitchDeg, out anchorYawDeg);

        calibrated = true;
        hasFilterState = false;
        filteredCore = Vector4.zero;
        previousFilteredCore = Vector4.zero;
        currentSample = default;
    }

    public FeatureSample Sample(float deltaTime, Vector4? rawCoreOverride = null)
    {
        ResolveReferences();
        if (!calibrated)
        {
            Calibrate();
        }

        if (!HasValidReferences() || !calibrated)
        {
            currentSample = default;
            return currentSample;
        }

        float dt = Mathf.Max(deltaTime, MinDeltaTime);
        Vector4 rawCore = rawCoreOverride ?? ReadRawCore();
        rawCore = ApplyNoiseFloor(rawCore);

        if (!hasFilterState)
        {
            filteredCore = rawCore;
            previousFilteredCore = rawCore;
            hasFilterState = true;
        }
        else
        {
            float alpha = ComputeLowPassAlpha(dt);
            filteredCore = Vector4.Lerp(filteredCore, rawCore, alpha);
        }

        Vector4 rates = (filteredCore - previousFilteredCore) / dt;
        previousFilteredCore = filteredCore;

        currentSample = new FeatureSample
        {
            core = filteredCore,
            rates = rates
        };

        return currentSample;
    }

    private Vector4 ReadRawCore()
    {
        Vector3 headLocalPos = rigRoot.InverseTransformPoint(head.position);
        Vector3 offsetLocal = headLocalPos - anchorHeadLocalPos;

        Vector3 headForwardLocal = rigRoot.InverseTransformDirection(head.forward).normalized;
        ComputePitchYaw(headForwardLocal, out float currentPitchDeg, out float currentYawDeg);

        float pitchOffsetDeg = Mathf.DeltaAngle(anchorPitchDeg, currentPitchDeg);
        float yawOffsetDeg = Mathf.DeltaAngle(anchorYawDeg, currentYawDeg);

        return new Vector4(offsetLocal.x, offsetLocal.z, pitchOffsetDeg, yawOffsetDeg);
    }

    private Vector4 ApplyNoiseFloor(Vector4 raw)
    {
        if (Mathf.Abs(raw.x) < planarNoiseFloor)
        {
            raw.x = 0f;
        }

        if (Mathf.Abs(raw.y) < planarNoiseFloor)
        {
            raw.y = 0f;
        }

        if (Mathf.Abs(raw.z) < angularNoiseFloorDeg)
        {
            raw.z = 0f;
        }

        if (Mathf.Abs(raw.w) < angularNoiseFloorDeg)
        {
            raw.w = 0f;
        }

        return raw;
    }

    private float ComputeLowPassAlpha(float deltaTime)
    {
        if (lowPassCutoffHz <= 0f)
        {
            return 1f;
        }

        return 1f - Mathf.Exp(-2f * Mathf.PI * lowPassCutoffHz * deltaTime);
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
    }

    private bool HasValidReferences()
    {
        return rigRoot != null && head != null;
    }

    private static void ComputePitchYaw(Vector3 forwardLocal, out float pitchDeg, out float yawDeg)
    {
        float planar = Mathf.Sqrt(forwardLocal.x * forwardLocal.x + forwardLocal.z * forwardLocal.z);
        pitchDeg = Mathf.Atan2(forwardLocal.y, planar) * Mathf.Rad2Deg;
        yawDeg = Mathf.Atan2(forwardLocal.x, forwardLocal.z) * Mathf.Rad2Deg;
    }
}
