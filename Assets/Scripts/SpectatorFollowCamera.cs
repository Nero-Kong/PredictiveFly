using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder (200)]
[DisallowMultipleComponent]
public class SpectatorFollowCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    [Tooltip("Optional HMD/camera transform used to inject head-local motion for free-look in VR.")]
    public Transform headPoseSource;

    [Header("Offsets")]
    public Vector3 localOffset = new Vector3(0f, 2f, -6f);
    public Vector3 lookAtOffset = new Vector3(0f, 1f, 0f);
    public bool useTargetRotation = true;
    [Tooltip("Keep target at screen center in follow mode by ignoring head-pose offsets.")]
    public bool keepTargetCentered = true;
    public bool applyHeadRotation = true;
    public bool applyHeadPosition = true;
    public float headPositionScale = 1f;

    [Header("Origin Lock")]
    public KeyCode lockAtOriginKey = KeyCode.O;
    public bool lockAtOrigin;
    public Vector3 lockedWorldPosition = Vector3.zero;

    [Header("Smoothing")]
    [Min(0f)] public float positionSmooth = 8f;
    [Min(0f)] public float rotationSmooth = 10f;

    Camera cachedCamera;
    Vector3 smoothedBasePosition;
    Quaternion smoothedBaseRotation = Quaternion.identity;
    bool hasFollowState;

    void Awake()
    {
        cachedCamera = GetComponent<Camera>();
    }

    void OnEnable()
    {
        Application.onBeforeRender += OnBeforeRender;
        hasFollowState = false;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= OnBeforeRender;
        hasFollowState = false;
    }

    void LateUpdate()
    {
        if (IsLockTogglePressed())
        {
            ToggleLockAtOrigin();
        }

        UpdateBaseFollow(Time.deltaTime);
        ApplyFinalPose();
    }

    void OnBeforeRender()
    {
        if (!IsXrCameraActive())
        {
            return;
        }

        // Re-apply current head pose right before rendering to minimize XR pose latency.
        ApplyFinalPose();
    }

    bool IsXrCameraActive()
    {
        if (cachedCamera == null)
        {
            cachedCamera = GetComponent<Camera>();
        }

        return cachedCamera != null
            && cachedCamera.enabled
            && cachedCamera.stereoTargetEye != StereoTargetEyeMask.None;
    }

    void UpdateBaseFollow(float dt)
    {
        if (lockAtOrigin)
        {
            if (!hasFollowState)
            {
                smoothedBaseRotation = transform.rotation;
                hasFollowState = true;
            }

            smoothedBasePosition = lockedWorldPosition;
            return;
        }

        if (target == null)
        {
            return;
        }

        Vector3 basePosition = useTargetRotation
            ? target.TransformPoint(localOffset)
            : target.position + localOffset;

        Vector3 lookPoint = target.position + lookAtOffset;
        Vector3 lookDir = lookPoint - basePosition;
        if (lookDir.sqrMagnitude < 0.0001f)
        {
            lookDir = target.forward;
        }
        Quaternion baseRotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

        if (!hasFollowState)
        {
            smoothedBasePosition = basePosition;
            smoothedBaseRotation = baseRotation;
            hasFollowState = true;
            return;
        }

        float posT = positionSmooth <= 0f ? 1f : 1f - Mathf.Exp(-positionSmooth * dt);
        smoothedBasePosition = Vector3.Lerp(smoothedBasePosition, basePosition, posT);

        float rotT = rotationSmooth <= 0f ? 1f : 1f - Mathf.Exp(-rotationSmooth * dt);
        smoothedBaseRotation = Quaternion.Slerp(smoothedBaseRotation, baseRotation, rotT);
    }

    bool IsLockTogglePressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(lockAtOriginKey))
        {
            return true;
        }
#endif

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            string keyName = lockAtOriginKey.ToString();
            if (System.Enum.TryParse(keyName, true, out Key key))
            {
                var keyControl = Keyboard.current[key];
                if (keyControl != null && keyControl.wasPressedThisFrame)
                {
                    return true;
                }
            }
        }
#endif

        return false;
    }

    public void ToggleLockAtOrigin()
    {
        lockAtOrigin = !lockAtOrigin;

        if (lockAtOrigin)
        {
            if (!hasFollowState)
            {
                smoothedBaseRotation = transform.rotation;
                hasFollowState = true;
            }

            smoothedBasePosition = lockedWorldPosition;
        }
    }

    void ApplyFinalPose()
    {
        if (!hasFollowState)
        {
            return;
        }

        Vector3 finalPosition = smoothedBasePosition;
        Quaternion finalRotation = smoothedBaseRotation;

        bool isXr = IsXrCameraActive();
        // In XR, head pose must always drive the final view for comfort.
        bool allowHeadPoseOffset = isXr || !keepTargetCentered || lockAtOrigin;
        if (allowHeadPoseOffset && headPoseSource != null)
        {
            if (applyHeadRotation)
            {
                finalRotation = smoothedBaseRotation * headPoseSource.localRotation;
            }

            if (applyHeadPosition)
            {
                finalPosition += smoothedBaseRotation * (headPoseSource.localPosition * headPositionScale);
            }
        }

        transform.SetPositionAndRotation(finalPosition, finalRotation);
    }
}
