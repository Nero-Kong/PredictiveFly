using UnityEngine;

/// <summary>
/// Single-script locomotion:
/// 1) Planar translation from HMD offset relative to an initialized center.
/// 2) Vertical and yaw commands from HMD forward vector.
/// </summary>
public class HeadOffsetLocomotion : MonoBehaviour
{
    public enum OrientationControlMode
    {
        Dynamic,
        Static,
        Coupled
    }

    [Header("Rig / HMD")]
    [Tooltip("Transform that will be moved. Defaults to this object.")]
    public Transform target;
    [Tooltip("HMD/camera transform. Defaults to Camera.main.")]
    public Transform head;

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
    [Range(0f, 1f)]
    public float headDirectionBlend = 0.2f;
    [Tooltip("Planar velocity smoothing (1/s).")]
    public float planarSmoothing = 10f;

    [Header("Orientation & Vertical")]
    public OrientationControlMode orientationMode = OrientationControlMode.Dynamic;
    [Tooltip("Maximum vertical speed for dynamic mode (m/s).")]
    public float verticalSpeed = 10f;
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
    [Tooltip("Also reset target rig pose to its initial pose when recenter key is pressed.")]
    public bool resetTargetPoseOnRecenter = true;

    private Vector3 centerHeadLocal;
    private Vector3 smoothedPlanarVelocityLocal;
    private Vector3 initialTargetPosition;
    private Quaternion initialTargetRotation = Quaternion.identity;
    private bool hasInitialTargetPose;

    private void Awake()
    {
        if (target == null)
        {
            target = transform;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }
    }

    private void OnEnable()
    {
        CacheInitialTargetPose();
        CaptureCenter();
        smoothedPlanarVelocityLocal = Vector3.zero;
    }

    private void Update()
    {
        if (target == null || head == null)
        {
            return;
        }

        if (Input.GetKeyDown(recenterKey))
        {
            RecenterNow();
        }

        Vector3 headLocalPos = target.InverseTransformPoint(head.position);
        Vector3 centerOffsetLocal = headLocalPos - centerHeadLocal;
        Vector3 planarOffsetLocal = new Vector3(centerOffsetLocal.x, 0f, centerOffsetLocal.z);

        float planarSpeed = ComputePlanarSpeed(planarOffsetLocal.magnitude);
        Vector3 planarDirectionLocal = ComputePlanarDirectionLocal(planarOffsetLocal);
        Vector3 desiredPlanarVelocityLocal = planarDirectionLocal * planarSpeed;
        float planarLerp = 1f - Mathf.Exp(-Mathf.Max(0f, planarSmoothing) * Time.deltaTime);
        smoothedPlanarVelocityLocal = Vector3.Lerp(smoothedPlanarVelocityLocal, desiredPlanarVelocityLocal, planarLerp);

        Vector3 headForwardLocal = target.InverseTransformDirection(head.forward).normalized;
        Vector2 planarCommand = horizontalSpeed > 1e-6f
            ? new Vector2(smoothedPlanarVelocityLocal.x, smoothedPlanarVelocityLocal.z) / horizontalSpeed
            : Vector2.zero;

        float yawRateRad = ComputeYawRate(headForwardLocal, planarCommand);
        float verticalCommand = ComputeVerticalSpeed(headForwardLocal);

        Vector3 localVelocity = new Vector3(smoothedPlanarVelocityLocal.x, verticalCommand, smoothedPlanarVelocityLocal.z);
        Vector3 worldVelocity = target.TransformDirection(localVelocity);
        Vector3 newPos = target.position + worldVelocity * Time.deltaTime;
        target.position = ClampPosition(newPos, positionLimits);

        float yawDeg = yawRateRad * Mathf.Rad2Deg * Time.deltaTime;
        target.Rotate(Vector3.up, yawDeg, Space.World);
    }

    private void CaptureCenter()
    {
        if (target == null || head == null)
        {
            centerHeadLocal = Vector3.zero;
            return;
        }

        centerHeadLocal = target.InverseTransformPoint(head.position);
    }

    private void CacheInitialTargetPose()
    {
        if (target == null)
        {
            hasInitialTargetPose = false;
            return;
        }

        initialTargetPosition = target.position;
        initialTargetRotation = target.rotation;
        hasInitialTargetPose = true;
    }

    private void RecenterNow()
    {
        if (resetTargetPoseOnRecenter && hasInitialTargetPose && target != null)
        {
            target.SetPositionAndRotation(initialTargetPosition, initialTargetRotation);
        }

        smoothedPlanarVelocityLocal = Vector3.zero;
        CaptureCenter();
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
        return curved * Mathf.Max(0f, horizontalSpeed);
    }

    private Vector3 ComputePlanarDirectionLocal(Vector3 planarOffsetLocal)
    {
        Vector3 leanDirLocal = planarOffsetLocal.sqrMagnitude > 1e-8f
            ? planarOffsetLocal.normalized
            : Vector3.zero;

        Vector3 headForwardLocal = target.InverseTransformDirection(head.forward);
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

    private float ComputeYawRate(Vector3 headForwardLocal, Vector2 planarCommand)
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
                // Mirrors the MATLAB dynamic yaw rule from SMHMIs.
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

    private float ComputeVerticalSpeed(Vector3 headForwardLocal)
    {
        float planarNorm = Mathf.Sqrt(headForwardLocal.x * headForwardLocal.x + headForwardLocal.z * headForwardLocal.z);
        float headPitchDeg = Mathf.Atan2(headForwardLocal.y, planarNorm) * Mathf.Rad2Deg;

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

    private static Vector3 ClampPosition(Vector3 position, Vector3 limits)
    {
        return new Vector3(
            Mathf.Clamp(position.x, -limits.x, limits.x),
            Mathf.Clamp(position.y, -limits.y, limits.y),
            Mathf.Clamp(position.z, -limits.z, limits.z));
    }
}
