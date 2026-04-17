using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder (320)]
[DisallowMultipleComponent]
public class ThirdPersonWhiteWorldMode : MonoBehaviour {
    [Header ("Mode")]
    public bool applyOnStart = true;
    public bool forceThirdPersonOnStart = true;
    public bool disableFloatingScreenOnStart = true;

    [Header ("White World")]
    [Range (0, 1)] public float whiteLevel = 0.9f;
    public LayerMask includeLayers = ~0;
    public bool excludeInternalObjects = true;
    public bool keepBoidsOriginal = true;
    public bool disableFog = true;
    public bool clearSkybox = true;
    public bool setCameraBackgroundWhite = true;

    [Header ("Debug")]
    [SerializeField] bool applied;

    struct RendererState {
        public Renderer renderer;
        public Material[] originalMaterials;
    }

    readonly List<RendererState> overridden = new List<RendererState> ();
    Material whiteMaterial;
    Color worldWhite;

    InfiniteGridAnchor gridAnchor;
    bool gridStateCached;
    bool gridWasActive;

    void Start () {
        if (applyOnStart) {
            ApplyMode ();
        }
    }

    void OnDisable () {
        RestoreMode ();
    }

    void OnDestroy () {
        RestoreMode ();
        if (whiteMaterial != null) {
            Destroy (whiteMaterial);
            whiteMaterial = null;
        }
    }

    [ContextMenu ("Apply White Third-Person Mode")]
    public void ApplyMode () {
        if (applied) {
            return;
        }

        worldWhite = new Color (whiteLevel, whiteLevel, whiteLevel, 1f);

        if (disableFloatingScreenOnStart) {
            VrFloatingSpectatorScreen floating = FindFirstObjectByType<VrFloatingSpectatorScreen> ();
            if (floating != null) {
                floating.SetFloatingScreenActive (false);
                floating.enabled = false;
            }
        }

        DisableInfiniteGridIfAny ();

        if (forceThirdPersonOnStart) {
            CameraViewModeSwitcher switcher = FindFirstObjectByType<CameraViewModeSwitcher> ();
            if (switcher != null) {
                switcher.SetMode (CameraViewModeSwitcher.ViewMode.ThirdPerson);
            }
        }

        if (disableFog) {
            RenderSettings.fog = false;
        }

        if (clearSkybox) {
            RenderSettings.skybox = null;
        }

        if (setCameraBackgroundWhite) {
            Camera[] cameras = FindObjectsByType<Camera> (FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++) {
                cameras[i].clearFlags = CameraClearFlags.SolidColor;
                cameras[i].backgroundColor = worldWhite;
            }
        }

        EnsureWhiteMaterial ();
        OverrideRendererMaterials ();
        applied = true;
    }

    [ContextMenu ("Restore White Third-Person Mode")]
    public void RestoreMode () {
        if (!applied) {
            return;
        }

        for (int i = 0; i < overridden.Count; i++) {
            RendererState state = overridden[i];
            if (state.renderer != null) {
                state.renderer.sharedMaterials = state.originalMaterials;
            }
        }
        overridden.Clear ();

        RestoreInfiniteGridIfAny ();
        applied = false;
    }

    void EnsureWhiteMaterial () {
        if (whiteMaterial != null) {
            SetMaterialWhiteColor ();
            return;
        }

        Shader shader = Shader.Find ("Universal Render Pipeline/Lit");
        if (shader == null) {
            shader = Shader.Find ("Universal Render Pipeline/Unlit");
        }
        if (shader == null) {
            shader = Shader.Find ("Unlit/Color");
        }
        if (shader == null) {
            shader = Shader.Find ("Standard");
        }
        if (shader == null) {
            return;
        }

        whiteMaterial = new Material (shader) {
            name = "__PureWhiteWorldMat",
            hideFlags = HideFlags.DontSave
        };
        SetMaterialWhiteColor ();
    }

    void SetMaterialWhiteColor () {
        if (whiteMaterial == null) {
            return;
        }

        if (whiteMaterial.HasProperty ("_BaseMap")) {
            whiteMaterial.SetTexture ("_BaseMap", Texture2D.whiteTexture);
        }
        if (whiteMaterial.HasProperty ("_BaseColorMap")) {
            whiteMaterial.SetTexture ("_BaseColorMap", Texture2D.whiteTexture);
        }
        if (whiteMaterial.HasProperty ("_BaseColor")) {
            whiteMaterial.SetColor ("_BaseColor", worldWhite);
        }
        if (whiteMaterial.HasProperty ("_Color")) {
            whiteMaterial.SetColor ("_Color", worldWhite);
        }
        if (whiteMaterial.HasProperty ("_Glossiness")) {
            whiteMaterial.SetFloat ("_Glossiness", 0f);
        }
        if (whiteMaterial.HasProperty ("_Metallic")) {
            whiteMaterial.SetFloat ("_Metallic", 0f);
        }
        if (whiteMaterial.HasProperty ("_Smoothness")) {
            whiteMaterial.SetFloat ("_Smoothness", 0.05f);
        }
        if (whiteMaterial.HasProperty ("_SpecColor")) {
            whiteMaterial.SetColor ("_SpecColor", Color.black);
        }
    }

    void OverrideRendererMaterials () {
        if (whiteMaterial == null) {
            return;
        }

        Renderer[] renderers = FindObjectsByType<Renderer> (FindObjectsSortMode.None);
        int includeMask = includeLayers.value;

        for (int i = 0; i < renderers.Length; i++) {
            Renderer r = renderers[i];
            if (r == null || !r.enabled) {
                continue;
            }

            int layerMask = 1 << r.gameObject.layer;
            if ((includeMask & layerMask) == 0) {
                continue;
            }

            if (excludeInternalObjects && r.gameObject.name.StartsWith ("__")) {
                continue;
            }

            if (keepBoidsOriginal && r.GetComponentInParent<Boid> () != null) {
                continue;
            }

            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) {
                continue;
            }

            Material[] originals = r.sharedMaterials;
            if (originals == null || originals.Length == 0) {
                continue;
            }

            Material[] whiteArray = new Material[originals.Length];
            for (int m = 0; m < whiteArray.Length; m++) {
                whiteArray[m] = whiteMaterial;
            }

            overridden.Add (new RendererState {
                renderer = r,
                originalMaterials = originals
            });
            r.sharedMaterials = whiteArray;
        }
    }

    void DisableInfiniteGridIfAny () {
        gridAnchor = FindFirstObjectByType<InfiniteGridAnchor> ();
        if (gridAnchor == null || gridStateCached) {
            return;
        }

        gridWasActive = gridAnchor.gameObject.activeSelf;
        gridStateCached = true;
        gridAnchor.gameObject.SetActive (false);
    }

    void RestoreInfiniteGridIfAny () {
        if (!gridStateCached || gridAnchor == null) {
            return;
        }

        gridAnchor.gameObject.SetActive (gridWasActive);
        gridStateCached = false;
    }
}

public static class ThirdPersonWhiteWorldModeBootstrap {
    public static bool AutoBootstrapOnSceneLoad = false;

    [RuntimeInitializeOnLoadMethod (RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateAutoInstanceIfMissing () {
        if (!AutoBootstrapOnSceneLoad) {
            return;
        }

        if (Object.FindFirstObjectByType<ThirdPersonWhiteWorldMode> () != null) {
            return;
        }

        GameObject host = new GameObject ("__ThirdPersonWhiteWorldMode");
        host.AddComponent<ThirdPersonWhiteWorldMode> ();
    }
}
