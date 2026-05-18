using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(260)]
[DisallowMultipleComponent]
public class GhostAvatarAnimationDriver : MonoBehaviour
{
    [Header("References")]
    public PredictiveGhostAvatarLocomotion locomotion;
    public bool autoFindLocomotion = true;
    public Animator animator;
    public RuntimeAnimatorController animatorController;

    [Header("Visual")]
    public GameObject visualPrefab;
    public bool buildProceduralVisual = true;
    public bool hideExistingRenderersOnStart = true;
    public bool disableExistingCollidersOnStart = true;
    public bool disablePrefabControlScripts = true;
    public bool preserveCustomPrefabBehaviours = true;
    public bool disablePrefabColliders = true;
    public bool disablePrefabCamerasAndLights = true;
    public bool applyGhostMaterialToPrefab = true;
    public bool preservePrefabMaterialTextures = true;
    public bool applyRootLeanToPrefab = false;
    public bool enablePrefabMotionFallback = true;
    public Color ghostColor = new Color(0.18f, 0.8f, 1f, 0.28f);
    [Min(0.1f)] public float visualScale = 1f;
    public Vector3 visualLocalPosition = Vector3.zero;
    public Vector3 visualLocalEulerAngles = Vector3.zero;
    public Vector3 visualLocalScale = Vector3.one;
    [Min(0f)] public float visualAlphaPulse = 0.08f;
    [Min(0f)] public float visualAlphaPulseFrequency = 1.4f;

    [Header("Animation Response")]
    [Min(0.01f)] public float activeSpeedReference = 2.5f;
    [Min(0.01f)] public float verticalSpeedReference = 1.5f;
    [Min(0f)] public float parameterSmoothing = 10f;
    [Min(0f)] public float idleBobAmplitude = 0.06f;
    [Min(0f)] public float idleBobFrequency = 1.8f;
    [Min(0f)] public float flyLeanAngleDeg = 28f;
    [Min(0f)] public float prefabPitchAngleDeg = 18f;
    [Min(0f)] public float prefabRollAngleDeg = 24f;
    [Min(0f)] public float prefabClimbPitchAngleDeg = 10f;

    [Header("Animator Parameters")]
    public string floatBoolParameter = "Float";
    public string sprintBoolParameter = "Sprint";
    public string ascendBoolParameter = "Ascend";
    public string descendBoolParameter = "Descend";
    public string flyXFloatParameter = "FlyX";
    public string flyZFloatParameter = "FlyZ";
    public string speedFloatParameter = "Speed";

    [Header("Debug")]
    [SerializeField] Vector3 localVelocity;
    [SerializeField] float smoothedFlyX;
    [SerializeField] float smoothedFlyZ;
    [SerializeField] float smoothedSpeed01;
    [SerializeField] bool isAscending;
    [SerializeField] bool isDescending;
    [SerializeField] bool prefabHasCompatibleMotionBehaviours;

    const string VisualRootName = "__PredictiveGhostAvatarVisual";
    static readonly string[] UnsafePrefabBehaviourTokens =
    {
        "agent",
        "audio",
        "camera",
        "datasink",
        "datasource",
        "decision",
        "input",
        "locomotion",
        "network",
        "publisher",
        "receiver",
        "record",
        "sender",
        "socket",
        "stream",
        "telemetry"
    };

    readonly HashSet<int> animatorParameterHashes = new HashSet<int>();
    readonly List<Renderer> proceduralRenderers = new List<Renderer>();
    readonly List<Material> transparentPrefabMaterials = new List<Material>();

    Transform visualRoot;
    Transform head;
    Transform chest;
    Transform hips;
    Transform leftArm;
    Transform rightArm;
    Transform leftLeg;
    Transform rightLeg;
    Material ghostMaterial;
    Vector3 lastPosition;
    bool hasLastPosition;
    bool usingPrefabVisual;

    void Awake()
    {
        ResolveReferences();
        PrepareExistingObject();
        EnsurePrefabVisual();

        if (buildProceduralVisual && GetVisualRoot() == null)
        {
            EnsureProceduralVisual();
        }

        ResolveReferences();
        CacheAnimatorParameters();
    }

    void OnEnable()
    {
        hasLastPosition = false;
    }

    void LateUpdate()
    {
        ResolveReferences();
        UpdateMotionState();
        UpdateAnimator();
        UpdateVisualRootPose();
        UpdateGhostMaterialPulse();
        UpdateProceduralVisual();
    }

    void OnDestroy()
    {
        if (ghostMaterial != null)
        {
            Destroy(ghostMaterial);
            ghostMaterial = null;
        }

        for (int i = 0; i < transparentPrefabMaterials.Count; i++)
        {
            if (transparentPrefabMaterials[i] != null)
            {
                Destroy(transparentPrefabMaterials[i]);
            }
        }
        transparentPrefabMaterials.Clear();
    }

    void ResolveReferences()
    {
        if (locomotion == null && autoFindLocomotion)
        {
            locomotion = FindFirstObjectByType<PredictiveGhostAvatarLocomotion>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null && animatorController != null && animator.runtimeAnimatorController != animatorController)
        {
            animator.runtimeAnimatorController = animatorController;
        }
    }

    void CacheAnimatorParameters()
    {
        animatorParameterHashes.Clear();
        if (animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            animatorParameterHashes.Add(parameters[i].nameHash);
        }
    }

    void PrepareExistingObject()
    {
        if (hideExistingRenderersOnStart)
        {
            Transform currentVisualRoot = GetVisualRoot();
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null
                    && (currentVisualRoot == null || !renderers[i].transform.IsChildOf(currentVisualRoot)))
                {
                    renderers[i].enabled = false;
                }
            }
        }

        if (disableExistingCollidersOnStart)
        {
            Transform currentVisualRoot = GetVisualRoot();
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null
                    && (currentVisualRoot == null || !colliders[i].transform.IsChildOf(currentVisualRoot)))
                {
                    colliders[i].enabled = false;
                }
            }
        }
    }

    Transform GetVisualRoot()
    {
        if (visualRoot != null)
        {
            return visualRoot;
        }

        Transform existing = transform.Find(VisualRootName);
        if (existing != null)
        {
            visualRoot = existing;
        }

        return visualRoot;
    }

    void EnsurePrefabVisual()
    {
        if (visualPrefab == null || GetVisualRoot() != null)
        {
            return;
        }

        GameObject root = Instantiate(visualPrefab, transform);
        root.name = VisualRootName;
        visualRoot = root.transform;
        usingPrefabVisual = true;
        visualRoot.localPosition = visualLocalPosition;
        visualRoot.localRotation = Quaternion.Euler(visualLocalEulerAngles);
        visualRoot.localScale = visualLocalScale * visualScale;

        DisablePrefabGameplayComponents(visualRoot);

        animator = visualRoot.GetComponentInChildren<Animator>(true);
        if (animator != null && animatorController != null)
        {
            animator.runtimeAnimatorController = animatorController;
        }

        if (applyGhostMaterialToPrefab)
        {
            ApplyGhostMaterialToVisualRoot();
        }
    }

    void DisablePrefabGameplayComponents(Transform root)
    {
        if (root == null)
        {
            return;
        }

        prefabHasCompatibleMotionBehaviours = false;

        if (disablePrefabControlScripts)
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                if (ShouldDisablePrefabBehaviour(behaviour))
                {
                    behaviour.enabled = false;
                    continue;
                }

                if (CouldProvidePrefabMotion(behaviour))
                {
                    prefabHasCompatibleMotionBehaviours = true;
                }
            }
        }

        if (disablePrefabColliders)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                }
            }

            Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                if (rigidbodies[i] != null)
                {
                    rigidbodies[i].isKinematic = true;
                    rigidbodies[i].useGravity = false;
                }
            }
        }

        if (disablePrefabCamerasAndLights)
        {
            Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                {
                    cameras[i].enabled = false;
                }
            }

            AudioListener[] listeners = root.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null)
                {
                    listeners[i].enabled = false;
                }
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].enabled = false;
                }
            }
        }
    }

    bool ShouldDisablePrefabBehaviour(MonoBehaviour behaviour)
    {
        if (behaviour == null)
        {
            return false;
        }

        if (!preserveCustomPrefabBehaviours)
        {
            return true;
        }

        Type type = behaviour.GetType();
        string namespaceName = type.Namespace ?? string.Empty;
        if (namespaceName.StartsWith("Unity", StringComparison.Ordinal))
        {
            return false;
        }

        string typeName = type.Name;
        return ContainsUnsafePrefabToken(typeName)
            || ContainsUnsafePrefabToken(namespaceName);
    }

    bool CouldProvidePrefabMotion(MonoBehaviour behaviour)
    {
        if (behaviour == null)
        {
            return false;
        }

        Type type = behaviour.GetType();
        string namespaceName = type.Namespace ?? string.Empty;
        if (namespaceName.StartsWith("Unity", StringComparison.Ordinal))
        {
            return false;
        }

        string typeName = type.Name;
        return !ContainsUnsafePrefabToken(typeName)
            && !ContainsUnsafePrefabToken(namespaceName);
    }

    static bool ContainsUnsafePrefabToken(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string lowered = text.ToLowerInvariant();
        for (int i = 0; i < UnsafePrefabBehaviourTokens.Length; i++)
        {
            if (lowered.Contains(UnsafePrefabBehaviourTokens[i]))
            {
                return true;
            }
        }

        return false;
    }

    void ApplyGhostMaterialToVisualRoot()
    {
        if (GetVisualRoot() == null)
        {
            return;
        }

        EnsureGhostMaterial();

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (preservePrefabMaterialTextures)
            {
                Material[] instanceMaterials = renderer.materials;
                for (int j = 0; j < instanceMaterials.Length; j++)
                {
                    Material material = instanceMaterials[j];
                    if (material == null)
                    {
                        continue;
                    }

                    ApplyTransparentAlphaToMaterial(material, ghostColor.a);
                    transparentPrefabMaterials.Add(material);
                }
                renderer.materials = instanceMaterials;
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = ghostMaterial;
            }
            else
            {
                for (int j = 0; j < materials.Length; j++)
                {
                    materials[j] = ghostMaterial;
                }
                renderer.sharedMaterials = materials;
            }
        }
    }

    void EnsureProceduralVisual()
    {
        if (GetVisualRoot() != null)
        {
            return;
        }

        GameObject root = new GameObject(VisualRootName);
        visualRoot = root.transform;
        visualRoot.SetParent(transform, false);
        visualRoot.localPosition = visualLocalPosition;
        visualRoot.localRotation = Quaternion.Euler(visualLocalEulerAngles);
        visualRoot.localScale = visualLocalScale * visualScale;

        EnsureGhostMaterial();

        hips = CreatePart("Hips", PrimitiveType.Sphere, new Vector3(0f, 0.82f, -0.04f), new Vector3(0.36f, 0.2f, 0.24f));
        chest = CreatePart("Chest", PrimitiveType.Capsule, new Vector3(0f, 1.22f, 0f), new Vector3(0.28f, 0.42f, 0.2f));
        head = CreatePart("Head", PrimitiveType.Sphere, new Vector3(0f, 1.72f, 0.04f), new Vector3(0.28f, 0.28f, 0.28f));
        leftArm = CreatePart("LeftArm", PrimitiveType.Capsule, new Vector3(-0.34f, 1.35f, 0.22f), new Vector3(0.08f, 0.42f, 0.08f));
        rightArm = CreatePart("RightArm", PrimitiveType.Capsule, new Vector3(0.34f, 1.35f, 0.22f), new Vector3(0.08f, 0.42f, 0.08f));
        leftLeg = CreatePart("LeftLeg", PrimitiveType.Capsule, new Vector3(-0.13f, 0.42f, -0.14f), new Vector3(0.09f, 0.42f, 0.09f));
        rightLeg = CreatePart("RightLeg", PrimitiveType.Capsule, new Vector3(0.13f, 0.42f, -0.14f), new Vector3(0.09f, 0.42f, 0.09f));
    }

    Transform CreatePart(string partName, PrimitiveType primitiveType, Vector3 localPosition, Vector3 localScale)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = partName;
        part.transform.SetParent(visualRoot, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        Collider col = part.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = ghostMaterial;
            proceduralRenderers.Add(renderer);
        }

        return part.transform;
    }

    void EnsureGhostMaterial()
    {
        if (ghostMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        ghostMaterial = new Material(shader)
        {
            name = "__PredictiveGhostAvatarMat",
            hideFlags = HideFlags.DontSave
        };

        ApplyGhostMaterialColor(ghostColor);
    }

    void ApplyGhostMaterialColor(Color color)
    {
        if (ghostMaterial == null)
        {
            return;
        }

        ApplyTransparentColorToMaterial(ghostMaterial, color);
    }

    static void ApplyTransparentColorToMaterial(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        ConfigureTransparentMaterial(material);
    }

    static void ApplyTransparentAlphaToMaterial(Material material, float alpha)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            Color color = material.GetColor("_BaseColor");
            color.a = Mathf.Clamp01(alpha);
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            Color color = material.GetColor("_Color");
            color.a = Mathf.Clamp01(alpha);
            material.SetColor("_Color", color);
        }

        ConfigureTransparentMaterial(material);
    }

    static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }
        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }
        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }
        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        }
        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }
        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }
        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    void UpdateMotionState()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        if (!hasLastPosition)
        {
            lastPosition = transform.position;
            hasLastPosition = true;
            localVelocity = Vector3.zero;
            return;
        }

        Vector3 worldVelocity = (transform.position - lastPosition) / dt;
        lastPosition = transform.position;
        localVelocity = transform.InverseTransformDirection(worldVelocity);

        float targetFlyX = Mathf.Clamp(localVelocity.x / activeSpeedReference, -1f, 1f);
        float targetFlyZ = Mathf.Clamp(localVelocity.z / activeSpeedReference, -1f, 1f);
        float targetSpeed01 = Mathf.Clamp01(new Vector2(localVelocity.x, localVelocity.z).magnitude / activeSpeedReference);
        float smoothT = parameterSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-parameterSmoothing * dt);

        smoothedFlyX = Mathf.Lerp(smoothedFlyX, targetFlyX, smoothT);
        smoothedFlyZ = Mathf.Lerp(smoothedFlyZ, targetFlyZ, smoothT);
        smoothedSpeed01 = Mathf.Lerp(smoothedSpeed01, targetSpeed01, smoothT);

        isAscending = localVelocity.y > verticalSpeedReference * 0.2f;
        isDescending = localVelocity.y < -verticalSpeedReference * 0.2f;
    }

    void UpdateAnimator()
    {
        if (animator == null)
        {
            return;
        }

        SetBoolIfExists(floatBoolParameter, true);
        SetBoolIfExists(sprintBoolParameter, smoothedSpeed01 > 0.65f);
        SetBoolIfExists(ascendBoolParameter, isAscending);
        SetBoolIfExists(descendBoolParameter, isDescending);
        SetFloatIfExists(flyXFloatParameter, smoothedFlyX);
        SetFloatIfExists(flyZFloatParameter, smoothedFlyZ);
        SetFloatIfExists(speedFloatParameter, smoothedSpeed01);
    }

    void UpdateVisualRootPose()
    {
        if (GetVisualRoot() == null)
        {
            return;
        }

        float bob = Mathf.Sin(Time.time * idleBobFrequency) * idleBobAmplitude * (1f - smoothedSpeed01);
        visualRoot.localPosition = visualLocalPosition + new Vector3(0f, bob, 0f);

        Quaternion motionRotation = Quaternion.identity;
        if (usingPrefabVisual)
        {
            bool shouldUsePrefabFallback = enablePrefabMotionFallback
                && (applyRootLeanToPrefab || !prefabHasCompatibleMotionBehaviours);

            if (shouldUsePrefabFallback)
            {
                float climb01 = verticalSpeedReference <= 0f
                    ? 0f
                    : Mathf.Clamp(localVelocity.y / verticalSpeedReference, -1f, 1f);
                float pitch = (-smoothedFlyZ * prefabPitchAngleDeg) + (climb01 * prefabClimbPitchAngleDeg);
                float roll = -smoothedFlyX * prefabRollAngleDeg;
                motionRotation = Quaternion.Euler(pitch, 0f, roll);
            }
        }
        else
        {
            float lean = -flyLeanAngleDeg * smoothedSpeed01;
            motionRotation = Quaternion.Euler(lean, 0f, 0f);
        }

        visualRoot.localRotation = Quaternion.Euler(visualLocalEulerAngles) * motionRotation;
        visualRoot.localScale = visualLocalScale * visualScale;
    }

    void UpdateGhostMaterialPulse()
    {
        if (ghostMaterial == null)
        {
            return;
        }

        Color color = ghostColor;
        color.a = Mathf.Clamp01(ghostColor.a + Mathf.Sin(Time.time * visualAlphaPulseFrequency) * visualAlphaPulse);
        ApplyGhostMaterialColor(color);

        for (int i = 0; i < transparentPrefabMaterials.Count; i++)
        {
            ApplyTransparentAlphaToMaterial(transparentPrefabMaterials[i], color.a);
        }
    }

    void UpdateProceduralVisual()
    {
        if (!buildProceduralVisual || GetVisualRoot() == null)
        {
            return;
        }

        float lean = -flyLeanAngleDeg * smoothedSpeed01;

        float flap = Mathf.Sin(Time.time * 6f) * 6f * smoothedSpeed01;
        SetLocalRotation(chest, Quaternion.Euler(8f * smoothedSpeed01, 0f, 0f));
        SetLocalRotation(head, Quaternion.Euler(-lean * 0.35f, 0f, 0f));
        SetLocalRotation(hips, Quaternion.Euler(-8f * smoothedSpeed01, 0f, 0f));
        SetLocalRotation(leftArm, Quaternion.Euler(84f - 20f * smoothedSpeed01, 0f, -28f + flap));
        SetLocalRotation(rightArm, Quaternion.Euler(84f - 20f * smoothedSpeed01, 0f, 28f - flap));
        SetLocalRotation(leftLeg, Quaternion.Euler(-18f - 18f * smoothedSpeed01, 0f, -7f - flap * 0.25f));
        SetLocalRotation(rightLeg, Quaternion.Euler(-18f - 18f * smoothedSpeed01, 0f, 7f + flap * 0.25f));
    }

    void SetLocalRotation(Transform target, Quaternion rotation)
    {
        if (target != null)
        {
            target.localRotation = rotation;
        }
    }

    void SetBoolIfExists(string parameterName, bool value)
    {
        int hash = Animator.StringToHash(parameterName);
        if (animatorParameterHashes.Contains(hash))
        {
            animator.SetBool(hash, value);
        }
    }

    void SetFloatIfExists(string parameterName, float value)
    {
        int hash = Animator.StringToHash(parameterName);
        if (animatorParameterHashes.Contains(hash))
        {
            animator.SetFloat(hash, value);
        }
    }
}
