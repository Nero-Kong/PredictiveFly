using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using XRInputDevice = UnityEngine.XR.InputDevice;

[DisallowMultipleComponent]
public class CameraViewModeSwitcher : MonoBehaviour {
    public enum ViewMode {
        FirstPerson,
        ThirdPerson
    }

    [Header ("Camera References")]
    public Camera firstPersonCamera;
    public Camera thirdPersonCamera;

    [Header ("Input")]
    public KeyCode toggleKey = KeyCode.V;
    [Tooltip ("Use XR controller primary button for toggling (eg. A/X).")]
    public bool useXrPrimaryButtonToggle;
    public XRNode xrToggleNode = XRNode.RightHand;

    [Header ("Behavior")]
    [Tooltip ("If true, start in third-person mode.")]
    public bool startInThirdPerson;
    [Tooltip ("If true, also enable/disable Camera component to switch main display output.")]
    public bool switchMainDisplayCamera = true;

    [Header ("Viewport")]
    public Rect firstPersonRect = new Rect (0f, 0f, 1f, 1f);
    public Rect thirdPersonRectInFirstMode = new Rect (0.72f, 0.7f, 0.26f, 0.26f);
    public Rect thirdPersonRectInThirdMode = new Rect (0f, 0f, 1f, 1f);

    [Header ("Debug")]
    [SerializeField] ViewMode currentMode = ViewMode.FirstPerson;

    static readonly List<XRInputDevice> xrDevices = new List<XRInputDevice> ();
    bool xrPrimaryPressedLastFrame;

    void Awake () {
        AutoAssignCamerasIfMissing ();
    }

    void Start () {
        currentMode = startInThirdPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson;
        ApplyMode ();
    }

    void Update () {
        if (IsTogglePressed ()) {
            ToggleView ();
        }
    }

    bool IsTogglePressed () {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown (toggleKey)) {
            return true;
        }
#endif

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) {
            string keyName = toggleKey.ToString ();
            if (System.Enum.TryParse (keyName, true, out Key key)) {
                var keyControl = Keyboard.current[key];
                if (keyControl != null && keyControl.wasPressedThisFrame) {
                    return true;
                }
            }
        }
#endif

        if (useXrPrimaryButtonToggle && IsXrPrimaryButtonPressedThisFrame ()) {
            return true;
        }

        return false;
    }

    bool IsXrPrimaryButtonPressedThisFrame () {
        InputDevices.GetDevicesAtXRNode (xrToggleNode, xrDevices);

        bool isPressedNow = false;
        for (int i = 0; i < xrDevices.Count; i++) {
            if (xrDevices[i].isValid
                && xrDevices[i].TryGetFeatureValue (UnityEngine.XR.CommonUsages.primaryButton, out bool pressed)
                && pressed) {
                isPressedNow = true;
                break;
            }
        }

        bool pressedThisFrame = isPressedNow && !xrPrimaryPressedLastFrame;
        xrPrimaryPressedLastFrame = isPressedNow;
        return pressedThisFrame;
    }

    [ContextMenu ("Toggle View")]
    public void ToggleView () {
        SetMode (currentMode == ViewMode.FirstPerson
            ? ViewMode.ThirdPerson
            : ViewMode.FirstPerson);
    }

    [ContextMenu ("Switch To First Person")]
    public void SwitchToFirstPerson () {
        SetMode (ViewMode.FirstPerson);
    }

    [ContextMenu ("Switch To Third Person")]
    public void SwitchToThirdPerson () {
        SetMode (ViewMode.ThirdPerson);
    }

    public void SetMode (ViewMode mode) {
        currentMode = mode;
        ApplyMode ();
    }

    [ContextMenu ("Apply Current Mode")]
    public void ApplyMode () {
        if (firstPersonCamera == thirdPersonCamera && firstPersonCamera != null) {
            return;
        }

        bool firstActive = currentMode == ViewMode.FirstPerson;
        SetXrOutput (firstPersonCamera, firstActive);
        SetXrOutput (thirdPersonCamera, !firstActive);

        if (firstPersonCamera != null) {
            firstPersonCamera.rect = firstPersonRect;
        }
        if (thirdPersonCamera != null) {
            thirdPersonCamera.rect = firstActive
                ? thirdPersonRectInFirstMode
                : thirdPersonRectInThirdMode;
        }

        if (switchMainDisplayCamera) {
            if (firstPersonCamera != null) {
                firstPersonCamera.enabled = firstActive;
            }
            if (thirdPersonCamera != null) {
                thirdPersonCamera.enabled = !firstActive;
            }
        }
    }

    void AutoAssignCamerasIfMissing () {
        if (firstPersonCamera == null && Camera.main != null) {
            firstPersonCamera = Camera.main;
        }

        if (thirdPersonCamera == null) {
            Camera[] cams = FindObjectsByType<Camera> (FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++) {
                if (cams[i] != firstPersonCamera) {
                    thirdPersonCamera = cams[i];
                    break;
                }
            }
        }
    }

    static void SetXrOutput (Camera cam, bool xrEnabled) {
        if (cam == null) {
            return;
        }

        cam.stereoTargetEye = xrEnabled
            ? StereoTargetEyeMask.Both
            : StereoTargetEyeMask.None;

        UniversalAdditionalCameraData urpCameraData = cam.GetComponent<UniversalAdditionalCameraData> ();
        if (urpCameraData != null) {
            urpCameraData.allowXRRendering = xrEnabled;
        }
    }
}
