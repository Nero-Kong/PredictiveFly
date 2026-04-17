using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using XRInputDevice = UnityEngine.XR.InputDevice;

[DisallowMultipleComponent]
public class VrFloatingSpectatorScreen : MonoBehaviour {
    [Header ("Camera References")]
    public Camera hmdCamera;
    public Camera spectatorCamera;
    public CameraViewModeSwitcher viewModeSwitcher;

    [Header ("Toggle")]
    public bool startEnabled = false;
    public KeyCode toggleKey = KeyCode.P;
    public bool useXrSecondaryButtonToggle = true;
    public XRNode xrToggleNode = XRNode.RightHand;

    [Header ("Screen Pose (local to HMD)")]
    public Vector3 panelLocalPosition = new Vector3 (0f, 0f, 2.2f);
    public Vector3 panelLocalEuler = Vector3.zero;
    [Min (0.2f)] public float panelHeight = 1.25f;
    [Min (0.2f)] public float panelAspect = 16f / 9f;
    [Range (0, 31)] public int floatingLayer = 5;

    [Header ("Render")]
    [Min (256)] public int textureWidth = 1920;

    [Header ("Comfort Isolation")]
    [Tooltip ("When enabled, HMD renders only the floating screen layer on a solid background.")]
    public bool isolateHmdBackground = true;
    [Tooltip ("If true, hide scene world in HMD while floating screen mode is active.")]
    public bool hideWorldInHmd = true;
    public Color hmdBackgroundColor = new Color (0.95f, 0.95f, 0.95f, 1f);

    [Header ("Debug")]
    [SerializeField] bool isActive;

    const string PanelObjectName = "__ThirdPersonFloatingScreen";

    static readonly List<XRInputDevice> xrDevices = new List<XRInputDevice> ();
    bool xrSecondaryPressedLastFrame;

    GameObject panelObject;
    MeshRenderer panelRenderer;
    Material panelMaterial;
    RenderTexture panelTexture;

    bool thirdCamCached;
    bool cachedThirdEnabled;
    StereoTargetEyeMask cachedThirdStereoTargetEye;
    RenderTexture cachedThirdTargetTexture;
    Rect cachedThirdRect;
    bool cachedThirdAllowXr;

    bool switcherCached;
    bool cachedSwitcherEnabled;

    bool hmdCamCached;
    CameraClearFlags cachedHmdClearFlags;
    Color cachedHmdBackgroundColor;
    int cachedHmdCullingMask;

    void Awake () {
        AutoAssignReferencesIfMissing ();
    }

    void Start () {
        SetFloatingScreenActive (startEnabled);
    }

    void Update () {
        if (IsTogglePressed ()) {
            SetFloatingScreenActive (!isActive);
        }

        if (isActive) {
            UpdatePanelPoseAndSize ();
        }
    }

    void OnDisable () {
        if (isActive) {
            SetFloatingScreenActive (false);
        }
    }

    void OnDestroy () {
        if (panelTexture != null) {
            panelTexture.Release ();
            Destroy (panelTexture);
            panelTexture = null;
        }

        if (panelMaterial != null) {
            Destroy (panelMaterial);
            panelMaterial = null;
        }

        if (panelObject != null) {
            Destroy (panelObject);
            panelObject = null;
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

        if (useXrSecondaryButtonToggle && IsXrSecondaryButtonPressedThisFrame ()) {
            return true;
        }

        return false;
    }

    bool IsXrSecondaryButtonPressedThisFrame () {
        InputDevices.GetDevicesAtXRNode (xrToggleNode, xrDevices);

        bool pressedNow = false;
        for (int i = 0; i < xrDevices.Count; i++) {
            if (xrDevices[i].isValid
                && xrDevices[i].TryGetFeatureValue (UnityEngine.XR.CommonUsages.secondaryButton, out bool pressed)
                && pressed) {
                pressedNow = true;
                break;
            }
        }

        bool pressedThisFrame = pressedNow && !xrSecondaryPressedLastFrame;
        xrSecondaryPressedLastFrame = pressedNow;
        return pressedThisFrame;
    }

    public void SetFloatingScreenActive (bool active) {
        AutoAssignReferencesIfMissing ();

        if (hmdCamera == null || spectatorCamera == null) {
            isActive = false;
            HidePanel ();
            return;
        }

        if (isActive == active) {
            return;
        }

        isActive = active;
        if (isActive) {
            ActivateFloatingScreen ();
        } else {
            DeactivateFloatingScreen ();
        }
    }

    void ActivateFloatingScreen () {
        CacheAndDisableViewSwitcher ();
        CacheHmdCameraStateIfNeeded ();
        CacheThirdCameraStateIfNeeded ();
        ForceFirstPersonMainView ();

        EnsurePanelTexture ();
        EnsurePanelObject ();
        ApplyPanelTexture ();
        ConfigureSpectatorCameraForPanel ();
        ConfigureHmdCameraForComfort ();
        ShowPanel ();
        UpdatePanelPoseAndSize ();
    }

    void DeactivateFloatingScreen () {
        RestoreThirdCameraState ();
        RestoreHmdCameraState ();
        RestoreViewSwitcherState ();
        HidePanel ();
    }

    void ForceFirstPersonMainView () {
        if (viewModeSwitcher != null) {
            viewModeSwitcher.SetMode (CameraViewModeSwitcher.ViewMode.FirstPerson);
        }
    }

    void CacheAndDisableViewSwitcher () {
        if (viewModeSwitcher == null) {
            return;
        }

        if (!switcherCached) {
            cachedSwitcherEnabled = viewModeSwitcher.enabled;
            switcherCached = true;
        }

        viewModeSwitcher.enabled = false;
    }

    void RestoreViewSwitcherState () {
        if (!switcherCached || viewModeSwitcher == null) {
            return;
        }

        viewModeSwitcher.enabled = cachedSwitcherEnabled;
    }

    void CacheThirdCameraStateIfNeeded () {
        if (thirdCamCached || spectatorCamera == null) {
            return;
        }

        cachedThirdEnabled = spectatorCamera.enabled;
        cachedThirdStereoTargetEye = spectatorCamera.stereoTargetEye;
        cachedThirdTargetTexture = spectatorCamera.targetTexture;
        cachedThirdRect = spectatorCamera.rect;

        UniversalAdditionalCameraData urpData = spectatorCamera.GetComponent<UniversalAdditionalCameraData> ();
        cachedThirdAllowXr = urpData != null && urpData.allowXRRendering;

        thirdCamCached = true;
    }

    void RestoreThirdCameraState () {
        if (!thirdCamCached || spectatorCamera == null) {
            return;
        }

        spectatorCamera.enabled = cachedThirdEnabled;
        spectatorCamera.stereoTargetEye = cachedThirdStereoTargetEye;
        spectatorCamera.targetTexture = cachedThirdTargetTexture;
        spectatorCamera.rect = cachedThirdRect;

        UniversalAdditionalCameraData urpData = spectatorCamera.GetComponent<UniversalAdditionalCameraData> ();
        if (urpData != null) {
            urpData.allowXRRendering = cachedThirdAllowXr;
        }
    }

    void CacheHmdCameraStateIfNeeded () {
        if (hmdCamCached || hmdCamera == null) {
            return;
        }

        cachedHmdClearFlags = hmdCamera.clearFlags;
        cachedHmdBackgroundColor = hmdCamera.backgroundColor;
        cachedHmdCullingMask = hmdCamera.cullingMask;
        hmdCamCached = true;
    }

    void ConfigureHmdCameraForComfort () {
        if (hmdCamera == null || !isolateHmdBackground) {
            return;
        }

        hmdCamera.clearFlags = CameraClearFlags.SolidColor;
        hmdCamera.backgroundColor = hmdBackgroundColor;

        if (hideWorldInHmd) {
            hmdCamera.cullingMask = 1 << Mathf.Clamp (floatingLayer, 0, 31);
        }
    }

    void RestoreHmdCameraState () {
        if (!hmdCamCached || hmdCamera == null) {
            return;
        }

        hmdCamera.clearFlags = cachedHmdClearFlags;
        hmdCamera.backgroundColor = cachedHmdBackgroundColor;
        hmdCamera.cullingMask = cachedHmdCullingMask;
    }

    void ConfigureSpectatorCameraForPanel () {
        if (spectatorCamera == null) {
            return;
        }

        spectatorCamera.enabled = true;
        spectatorCamera.stereoTargetEye = StereoTargetEyeMask.None;
        spectatorCamera.targetTexture = panelTexture;
        spectatorCamera.rect = new Rect (0f, 0f, 1f, 1f);

        UniversalAdditionalCameraData urpData = spectatorCamera.GetComponent<UniversalAdditionalCameraData> ();
        if (urpData != null) {
            urpData.allowXRRendering = false;
        }
    }

    void EnsurePanelTexture () {
        int width = Mathf.Max (256, textureWidth);
        int height = Mathf.Max (256, Mathf.RoundToInt (width / Mathf.Max (0.2f, panelAspect)));

        if (panelTexture != null && panelTexture.width == width && panelTexture.height == height) {
            return;
        }

        if (panelTexture != null) {
            panelTexture.Release ();
            Destroy (panelTexture);
            panelTexture = null;
        }

        panelTexture = new RenderTexture (width, height, 24, RenderTextureFormat.ARGB32) {
            name = "__ThirdPersonFloatingScreenRT",
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1
        };
        panelTexture.Create ();
    }

    void EnsurePanelObject () {
        if (panelObject == null) {
            panelObject = GameObject.CreatePrimitive (PrimitiveType.Quad);
            panelObject.name = PanelObjectName;

            Collider col = panelObject.GetComponent<Collider> ();
            if (col != null) {
                Destroy (col);
            }

                panelRenderer = panelObject.GetComponent<MeshRenderer> ();
        }

        if (hmdCamera != null) {
            panelObject.transform.SetParent (hmdCamera.transform, false);
        }

        panelObject.layer = Mathf.Clamp (floatingLayer, 0, 31);

        if (panelRenderer != null && panelMaterial == null) {
            Shader unlit = Shader.Find ("Universal Render Pipeline/Unlit");
            if (unlit == null) {
                unlit = Shader.Find ("Unlit/Texture");
            }

            if (unlit != null) {
                panelMaterial = new Material (unlit) {
                    name = "__ThirdPersonFloatingScreenMat",
                    hideFlags = HideFlags.DontSave
                };
                panelRenderer.sharedMaterial = panelMaterial;
            }
        }
    }

    void ApplyPanelTexture () {
        if (panelMaterial == null || panelTexture == null) {
            return;
        }

        if (panelMaterial.HasProperty ("_BaseMap")) {
            panelMaterial.SetTexture ("_BaseMap", panelTexture);
        } else {
            panelMaterial.mainTexture = panelTexture;
        }
    }

    void ShowPanel () {
        if (panelObject != null) {
            panelObject.SetActive (true);
        }
    }

    void HidePanel () {
        if (panelObject != null) {
            panelObject.SetActive (false);
        }
    }

    void UpdatePanelPoseAndSize () {
        if (panelObject == null) {
            return;
        }

        panelObject.transform.localPosition = panelLocalPosition;
        panelObject.transform.localRotation = Quaternion.Euler (panelLocalEuler);

        float height = Mathf.Max (0.2f, panelHeight);
        float width = height * Mathf.Max (0.2f, panelAspect);
        panelObject.transform.localScale = new Vector3 (width, height, 1f);
    }

    void AutoAssignReferencesIfMissing () {
        if (hmdCamera == null) {
            hmdCamera = Camera.main;
        }

        if (hmdCamera == null) {
            Camera[] cameras = FindObjectsByType<Camera> (FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++) {
                if (cameras[i].stereoTargetEye != StereoTargetEyeMask.None) {
                    hmdCamera = cameras[i];
                    break;
                }
            }
        }

        if (spectatorCamera == null) {
            SpectatorFollowCamera followCam = FindFirstObjectByType<SpectatorFollowCamera> ();
            if (followCam != null) {
                spectatorCamera = followCam.GetComponent<Camera> ();
            }
        }

        if (spectatorCamera == null) {
            Camera[] cameras = FindObjectsByType<Camera> (FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++) {
                if (cameras[i] != hmdCamera) {
                    spectatorCamera = cameras[i];
                    break;
                }
            }
        }

        if (viewModeSwitcher == null) {
            viewModeSwitcher = FindFirstObjectByType<CameraViewModeSwitcher> ();
        }
    }
}

public static class VrFloatingSpectatorScreenBootstrap {
    public static bool AutoBootstrapOnSceneLoad = false;

    [RuntimeInitializeOnLoadMethod (RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateAutoInstanceIfMissing () {
        if (!AutoBootstrapOnSceneLoad) {
            return;
        }

        if (Object.FindFirstObjectByType<VrFloatingSpectatorScreen> () != null) {
            return;
        }

        GameObject host = new GameObject ("__VrFloatingSpectatorMode");
        host.AddComponent<VrFloatingSpectatorScreen> ();
    }
}
