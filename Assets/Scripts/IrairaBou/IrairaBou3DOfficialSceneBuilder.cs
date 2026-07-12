using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SpatialTracking;

/// <summary>
/// Builds a 3D iraira-bou course using only Unity primitives and built-in components.
/// Open Assets/Scenes/IrairaBou3D_Official.unity and use the context menu to rebuild after edits.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class IrairaBou3DOfficialSceneBuilder : MonoBehaviour
{
    public enum CourseRouteVariant
    {
        Base,
        MirrorHorizontal,
        MirrorVertical,
        MirrorHorizontalAndVertical
    }

    const string GeneratedRootName = "__Generated_IrairaBou3D_Official";
    const string PredictiveRigName = "Predictive Drone XR Rig";
    const string LegacyRigName = "XRRig";
    const string PredictiveCameraName = "Main Camera";
    const string DemoCameraName = "Main Camera - Keyboard Demo";
    const string TubeScaleRingRootName = "Tube Distance Scale Rings";
    const string TubeAvoidanceRootName = "Tube Avoidance Obstacles";
    const string OuterTubeMaterialName = "Official_Transparent_Outer_Glass_Tube";
    const string TubeFresnelShaderName = "PredictiveFly/Fresnel Directional Tube";
    const string DirectionalTubeMeshName = "Generated_Hollow_IrairaBou_Tube_DirectionalV2";
    const int CurrentCourseVisibilityVersion = 1;
    static readonly Color ExperimentFallbackBackgroundColor = new Color(0.008f, 0.011f, 0.016f, 1f);

    [Header("Build")]
    public bool buildOnEnable = true;
    public bool tubeOnlyScene = true;
    public int samplesPerCurveSegment = 8;
    [Min(0.1f)] public float courseLengthScale = 2f;
    [Tooltip("Number of repeated cycles used by the lateral weave, vertical weave, helix, and mirrored helix sections.")]
    [Range(1, 6)] public int repeatingSectionCycles = 3;
    [Tooltip("Fraction of each helix used to ease angular motion at both ends and prevent pinched tube joints.")]
    [Range(0.02f, 0.25f)] public float helixTransitionFraction = 0.12f;
    [Tooltip("Preview/default route. The experiment controller overrides this from Participant ID and Trial Index when Enter is pressed.")]
    public CourseRouteVariant routeVariant;

    public string RouteId => $"Route_{(char)('A' + (int)routeVariant)}";

    [Header("Course Shape")]
    public float railOffset = 0.78f;
    public float railRadius = 0.055f;
    public float centerLineWidth = 0.085f;
    [Tooltip("Fallback route-gate radius when no tube radius profile is available.")]
    public float checkpointRadius = 0.92f;
    public float checkpointTubeRadius = 0.045f;
    [Tooltip("Distance between a route-gate ring and the tube wall.")]
    [Min(0f)] public float routeGateWallInset = 0.12f;
    [Tooltip("Additional radial margin inside the drone's reachable tube area for route-gate triggers.")]
    [Min(0f)] public float routeGateEdgeMargin = 0.08f;
    [Tooltip("Thickness of the full-width checkpoint and finish trigger planes.")]
    [Min(0.02f)] public float routeGateTriggerDepth = 0.12f;
    public float probeRadius = 0.16f;
    public bool createTransparentOuterTube = true;
    public float outerTubeRadius = 3.92f;
    [Range(0.2f, 1f)] public float narrowTubeRadiusMultiplier = 0.55f;
    [Range(1f, 2f)] public float expandedTubeRadiusMultiplier = 1.25f;
    [Range(0.02f, 0.6f)] public float outerTubeAlpha = 0.14f;
    [Min(12)] public int outerTubeRadialSegments = 48;
    [Tooltip("Shader used for the Fresnel wall and embedded direction guides.")]
    public Shader outerTubeFresnelShader;
    [ColorUsage(true, true)] public Color outerTubeFresnelColor = new Color(0.5f, 0.95f, 1f, 1f);
    [Range(0f, 0.8f)] public float outerTubeFresnelAlpha = 0.28f;
    [Range(0.5f, 8f)] public float outerTubeFresnelPower = 2.2f;
    [ColorUsage(true, true)] public Color tubeTopGuideColor = new Color(0.92f, 0.98f, 1f, 1f);
    [ColorUsage(true, true)] public Color tubeSideGuideColor = new Color(0.16f, 0.82f, 1f, 1f);
    [Range(0f, 1f)] public float tubeGuideAlpha = 0.78f;
    [Range(0.001f, 0.02f)] public float tubeGuideAngularHalfWidth = 0.003f;
    [Min(0.25f)] public float tubeSideGuideDashPeriod = 4f;
    [Range(0.1f, 0.9f)] public float tubeSideGuideDashDuty = 0.55f;
    [SerializeField, HideInInspector] int courseVisibilityVersion;
    public bool createTubeScaleRings = true;
    [Min(0.5f)] public float tubeScaleRingSpacing = 6f;
    [Min(0.005f)] public float tubeScaleRingTubeRadius = 0.05f;
    [Min(0f)] public float tubeScaleRingInset = 0.08f;
    public bool createTubeAvoidanceObstacles = true;
    [Range(0.1f, 1.5f)] public float tubeAvoidanceObstacleRadius = 0.65f;
    [Range(0.05f, 0.9f)] public float tubeAvoidanceObstacleOffsetFraction = 0.55f;
    public bool showElectricRailJointSpheres;

    [Header("Demo Probe")]
    public bool createKeyboardDemoProbe = true;
    public float demoProbeMoveSpeed = 3.2f;
    public float demoProbeVerticalSpeed = 2.2f;

    [Header("Predictive Drone XR Locomotion")]
    public bool createPredictiveDroneRig = true;
    public GameObject droneVisualPrefab;
    [Min(0f)] public float expectedEyeHeight = 1.6f;
    public Vector3 predictiveRigStartOffset = Vector3.zero;
    [Min(0.01f)] public float predictiveRigCollisionRadius = 0.22f;
    [Min(0f)] public float predictiveRigCollisionSkinWidth = 0.03f;

    [Header("Planar Translation")]
    [Min(0f)] public float planarHorizontalSpeed = 5f;
    [Min(0f)] public float planarDeadZone = 0.012f;
    [Min(0.001f)] public float planarMaxOffset = 0.085f;
    [Min(0.1f)] public float planarResponseExponent = 0.9f;
    [Min(0f)] public float planarSmoothing = 18f;

    [Header("Drone Main Shell")]
    [Tooltip("Scale only the red renderer on the drone prefab root; child visuals and animation keep their original scale.")]
    [Range(0f, 1f)] public float droneMainShellScale = 0.6f;

    [ContextMenu("Rebuild Official 3D Iraira-Bou Scene")]
    public void RebuildScene()
    {
        EnsureExperimentComponents();
        ClearLegacyRootObjects();
        ClearGeneratedRoot();

        GameObject generatedRoot = CreateEmpty(GeneratedRootName, transform);
        generatedRoot.SetActive(false);
        Transform root = generatedRoot.transform;
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;

        Material outerTubeMaterial = CreateTransparentMaterial(OuterTubeMaterialName, GetOuterTubeColor(), 0.6f);
        Material tubeScaleRingMaterial = CreateMaterial("Official_Tube_Scale_Ring_Cyan", new Color(0.22f, 0.9f, 1f, 1f), 2.2f);
        Material tubeScaleMajorRingMaterial = CreateMaterial("Official_Tube_Scale_Ring_Major", new Color(1f, 0.86f, 0.38f, 1f), 2.8f);
        Material tubeObstacleMaterial = CreateMaterial("Official_Tube_Avoidance_Obstacle_Amber", new Color(1f, 0.42f, 0.1f, 1f), 2.6f);
        Material checkpointMaterial = CreateMaterial("Official_Checkpoint_Yellow", new Color(1f, 0.82f, 0.18f, 1f), 1.6f);
        Material finishMaterial = CreateMaterial("Official_Finish_Green", new Color(0.18f, 1f, 0.48f, 1f), 1.8f);

        List<Vector3> path = BuildPathSamples();
        CreateLightingAndCamera(root, !createPredictiveDroneRig);
        List<float> tubeRadiusProfile = CreateTransparentOuterTube(path, root, outerTubeMaterial);
        CreateTubeScaleRings(path, tubeRadiusProfile, root, tubeScaleRingMaterial, tubeScaleMajorRingMaterial);
        CreateTubeAvoidanceObstacles(path, tubeRadiusProfile, root, tubeObstacleMaterial);
        CreateCheckpoints(path, tubeRadiusProfile, root, checkpointMaterial, finishMaterial);

        if (!tubeOnlyScene)
        {
            Material floorMaterial = CreateMaterial("Official_Dark_Floor", new Color(0.035f, 0.045f, 0.06f, 1f), 0f);
            Material centerMaterial = CreateMaterial("Official_Cyan_Center_Path", new Color(0.05f, 0.85f, 1f, 1f), 2.5f);
            Material hazardMaterial = CreateMaterial("Official_Red_Electric_Hazard", new Color(1f, 0.08f, 0.04f, 1f), 3.2f);
            Material jointMaterial = CreateMaterial("Official_Hazard_Joints", new Color(1f, 0.25f, 0.08f, 1f), 2f);
            Material startMaterial = CreateMaterial("Official_Start_Blue", new Color(0.1f, 0.38f, 1f, 1f), 1.3f);

            CreateEnvironment(root, floorMaterial);
            CreateCenterLine(path, root, centerMaterial);
            CreateElectricRails(path, root, hazardMaterial, jointMaterial);
            CreatePrecisionGates(path, root, hazardMaterial);
            CreateStartAndFinish(path, root, startMaterial, finishMaterial);
            CreateLabels(path, root);
        }

        CreatePredictiveDroneRig(path, root);
        ConfigureExperimentComponents(root);

        if (createKeyboardDemoProbe && !tubeOnlyScene)
        {
            Material probeMaterial = CreateMaterial("Official_Probe_White", new Color(0.9f, 1f, 1f, 1f), 1.5f);
            CreateDemoProbe(path[0], root, probeMaterial);
        }

        generatedRoot.SetActive(true);
    }

    public void NormalizeCourseVisualMaterials()
    {
        Transform generatedRoot = transform.Find(GeneratedRootName);
        if (generatedRoot == null)
        {
            return;
        }

        IrairaBouTubeBoundary tubeBoundary = generatedRoot.GetComponentInChildren<IrairaBouTubeBoundary>(true);
        Renderer tubeRenderer = tubeBoundary != null ? tubeBoundary.GetComponent<Renderer>() : null;
        if (tubeRenderer == null)
        {
            return;
        }

        EnsureTubeVisualMesh(tubeBoundary);
        Material[] materials = tubeRenderer.sharedMaterials;
        for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
        {
            Material material = materials[materialIndex];
            if (material != null && material.name == OuterTubeMaterialName)
            {
                ConfigureTransparentMaterial(material, GetOuterTubeColor(), 0.6f);
            }
        }

        RefreshTubeScaleRingsIfNeeded(generatedRoot, tubeBoundary);
    }

    Color GetOuterTubeColor()
    {
        return new Color(0.55f, 0.9f, 1f, outerTubeAlpha);
    }

    void EnsureTubeVisualMesh(IrairaBouTubeBoundary tubeBoundary)
    {
        if (tubeBoundary == null || tubeBoundary.centerline == null || tubeBoundary.centerline.Length < 2)
        {
            return;
        }

        MeshFilter meshFilter = tubeBoundary.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            return;
        }

        int radialSegments = Mathf.Max(8, outerTubeRadialSegments);
        int expectedVertexCount = tubeBoundary.centerline.Length * (radialSegments + 1);
        Mesh currentMesh = meshFilter.sharedMesh;
        bool hasDirectionalUv = currentMesh != null
            && currentMesh.vertexCount == expectedVertexCount
            && currentMesh.name == DirectionalTubeMeshName;
        if (hasDirectionalUv)
        {
            return;
        }

        List<Vector3> path = new List<Vector3>(tubeBoundary.centerline);
        List<float> radiusProfile = tubeBoundary.radiusProfile != null
            ? new List<float>(tubeBoundary.radiusProfile)
            : null;
        Mesh refreshedMesh = BuildTubeWallMesh(path, radiusProfile, radialSegments);
        meshFilter.sharedMesh = refreshedMesh;

        MeshCollider meshCollider = tubeBoundary.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = refreshedMesh;
        }
    }

    void RefreshTubeScaleRingsIfNeeded(Transform generatedRoot, IrairaBouTubeBoundary tubeBoundary)
    {
        if (generatedRoot == null || tubeBoundary == null || tubeBoundary.centerline == null)
        {
            return;
        }

        List<Vector3> path = new List<Vector3>(tubeBoundary.centerline);
        List<float> radiusProfile = tubeBoundary.radiusProfile != null
            ? new List<float>(tubeBoundary.radiusProfile)
            : null;
        Transform ringRoot = generatedRoot.Find(TubeScaleRingRootName);
        if (!createTubeScaleRings)
        {
            RemoveGeneratedObject(ringRoot != null ? ringRoot.gameObject : null);
            return;
        }

        if (IsTubeScaleRingLayoutCurrent(ringRoot, path))
        {
            return;
        }

        RemoveGeneratedObject(ringRoot != null ? ringRoot.gameObject : null);
        Material ringMaterial = CreateMaterial("Official_Tube_Scale_Ring_Cyan", new Color(0.22f, 0.9f, 1f, 1f), 2.2f);
        Material majorRingMaterial = CreateMaterial("Official_Tube_Scale_Ring_Major", new Color(1f, 0.86f, 0.38f, 1f), 2.8f);
        CreateTubeScaleRings(path, radiusProfile, generatedRoot, ringMaterial, majorRingMaterial);
    }

    bool IsTubeScaleRingLayoutCurrent(Transform ringRoot, List<Vector3> path)
    {
        if (ringRoot == null || !ringRoot.gameObject.activeSelf || path == null || path.Count < 2)
        {
            return false;
        }

        float spacing = Mathf.Max(0.5f, tubeScaleRingSpacing);
        BuildCumulativeDistances(path, out float totalDistance);
        int expectedRingCount = 0;
        for (float distance = spacing; distance < totalDistance - spacing * 0.35f; distance += spacing)
        {
            expectedRingCount++;
        }

        if (ringRoot.childCount != expectedRingCount)
        {
            return false;
        }
        if (expectedRingCount == 0)
        {
            return true;
        }

        Transform firstRing = ringRoot.GetChild(0);
        string expectedName = $"Scale Ring 01 ({Mathf.RoundToInt(spacing)}m)";
        LineRenderer firstLine = firstRing.GetComponent<LineRenderer>();
        if (firstRing.name != expectedName || firstLine == null)
        {
            return false;
        }

        float expectedDiameter = Mathf.Max(0.005f, tubeScaleRingTubeRadius) * 2f;
        return Mathf.Abs(firstLine.widthMultiplier - expectedDiameter) <= 0.001f;
    }

    void RemoveGeneratedObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        target.SetActive(false);
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    void OnEnable()
    {
        UpgradeCourseVisibilitySettingsIfNeeded();
        EnsureExperimentComponents();
        if (!buildOnEnable)
        {
            return;
        }

        Transform generatedRoot = transform.Find(GeneratedRootName);
        if (generatedRoot == null || HasLegacyRootObjects() || ShouldRebuildGeneratedRoot(generatedRoot))
        {
            RebuildScene();
            return;
        }

        ConfigureExperimentComponents(generatedRoot);
        if (Application.isPlaying)
        {
            NormalizeCourseVisualMaterials();
        }
    }

    bool ShouldRebuildGeneratedRoot(Transform generatedRoot)
    {
        if (generatedRoot == null)
        {
            return true;
        }

        IrairaBouTubeBoundary tubeBoundary = generatedRoot.GetComponentInChildren<IrairaBouTubeBoundary>();
        if (createTransparentOuterTube && tubeBoundary == null)
        {
            return true;
        }

        float targetTubeRadius = Mathf.Max(outerTubeRadius, railOffset + railRadius * 2f);
        if (createTransparentOuterTube && tubeBoundary != null && !Mathf.Approximately(tubeBoundary.wallRadius, targetTubeRadius))
        {
            return true;
        }

        if (createTransparentOuterTube && tubeBoundary != null && IsGeneratedTubeShapeOutOfDate(tubeBoundary))
        {
            return true;
        }

        if (createTransparentOuterTube && createTubeScaleRings && generatedRoot.Find(TubeScaleRingRootName) == null)
        {
            return true;
        }

        if ((!createTransparentOuterTube || !createTubeScaleRings) && generatedRoot.Find(TubeScaleRingRootName) != null)
        {
            return true;
        }

        if (createTransparentOuterTube && createTubeAvoidanceObstacles && generatedRoot.Find(TubeAvoidanceRootName) == null)
        {
            return true;
        }

        if ((!createTransparentOuterTube || !createTubeAvoidanceObstacles) && generatedRoot.Find(TubeAvoidanceRootName) != null)
        {
            return true;
        }

        if (tubeOnlyScene && HasTubeOnlyExcludedContent(generatedRoot))
        {
            return true;
        }

        Transform predictiveRig = generatedRoot.Find(PredictiveRigName);
        if (createPredictiveDroneRig && predictiveRig == null)
        {
            return true;
        }

        if (!createPredictiveDroneRig && predictiveRig != null)
        {
            return true;
        }

        if (generatedRoot.Find(LegacyRigName) != null)
        {
            return true;
        }

        if (createPredictiveDroneRig)
        {
            if (generatedRoot.Find(DemoCameraName) != null)
            {
                return true;
            }

            if (CountEnabledCameras(generatedRoot) != 1)
            {
                return true;
            }

            if (CountEnabledAudioListeners(generatedRoot) != 1)
            {
                return true;
            }

            Transform cameraTransform = predictiveRig.Find("Camera Offset/" + PredictiveCameraName);
            if (cameraTransform == null || cameraTransform.GetComponent<TrackedPoseDriver>() == null)
            {
                return true;
            }
        }

        if (generatedRoot.GetComponentInChildren<IrairaBouCheckpoint>() == null
            || generatedRoot.GetComponentInChildren<IrairaBouFinish>() == null)
        {
            return true;
        }

        if (!HasFullWidthRouteGates(generatedRoot))
        {
            return true;
        }

        return false;
    }

    void OnValidate()
    {
        UpgradeCourseVisibilitySettingsIfNeeded();
        samplesPerCurveSegment = Mathf.Clamp(samplesPerCurveSegment, 3, 24);
        repeatingSectionCycles = Mathf.Clamp(repeatingSectionCycles, 1, 6);
        railOffset = Mathf.Max(0.2f, railOffset);
        railRadius = Mathf.Max(0.01f, railRadius);
        centerLineWidth = Mathf.Max(0.01f, centerLineWidth);
        checkpointRadius = Mathf.Max(0.25f, checkpointRadius);
        checkpointTubeRadius = Mathf.Max(0.01f, checkpointTubeRadius);
        routeGateWallInset = Mathf.Max(0f, routeGateWallInset);
        routeGateEdgeMargin = Mathf.Max(0f, routeGateEdgeMargin);
        routeGateTriggerDepth = Mathf.Max(0.02f, routeGateTriggerDepth);
        probeRadius = Mathf.Max(0.03f, probeRadius);
        outerTubeRadius = Mathf.Max(railOffset + railRadius * 2f, outerTubeRadius);
        narrowTubeRadiusMultiplier = Mathf.Clamp(narrowTubeRadiusMultiplier, 0.2f, 1f);
        expandedTubeRadiusMultiplier = Mathf.Clamp(expandedTubeRadiusMultiplier, 1f, 2f);
        outerTubeRadialSegments = Mathf.Max(12, outerTubeRadialSegments);
        outerTubeFresnelAlpha = Mathf.Clamp(outerTubeFresnelAlpha, 0f, 0.8f);
        outerTubeFresnelPower = Mathf.Clamp(outerTubeFresnelPower, 0.5f, 8f);
        tubeGuideAlpha = Mathf.Clamp01(tubeGuideAlpha);
        tubeGuideAngularHalfWidth = Mathf.Clamp(tubeGuideAngularHalfWidth, 0.001f, 0.02f);
        tubeSideGuideDashPeriod = Mathf.Max(0.25f, tubeSideGuideDashPeriod);
        tubeSideGuideDashDuty = Mathf.Clamp(tubeSideGuideDashDuty, 0.1f, 0.9f);
        tubeScaleRingSpacing = Mathf.Max(0.5f, tubeScaleRingSpacing);
        tubeScaleRingTubeRadius = Mathf.Max(0.005f, tubeScaleRingTubeRadius);
        tubeScaleRingInset = Mathf.Max(0f, tubeScaleRingInset);
        tubeAvoidanceObstacleRadius = Mathf.Clamp(tubeAvoidanceObstacleRadius, 0.1f, 1.5f);
        tubeAvoidanceObstacleOffsetFraction = Mathf.Clamp(tubeAvoidanceObstacleOffsetFraction, 0.05f, 0.9f);
        courseLengthScale = Mathf.Max(0.1f, courseLengthScale);
        helixTransitionFraction = Mathf.Clamp(helixTransitionFraction, 0.02f, 0.25f);
        expectedEyeHeight = Mathf.Max(0f, expectedEyeHeight);
        predictiveRigCollisionRadius = Mathf.Max(0.01f, predictiveRigCollisionRadius);
        predictiveRigCollisionSkinWidth = Mathf.Max(0f, predictiveRigCollisionSkinWidth);
        planarHorizontalSpeed = Mathf.Max(0f, planarHorizontalSpeed);
        planarDeadZone = Mathf.Max(0f, planarDeadZone);
        planarMaxOffset = Mathf.Max(planarDeadZone + 0.001f, planarMaxOffset);
        planarResponseExponent = Mathf.Max(0.1f, planarResponseExponent);
        planarSmoothing = Mathf.Max(0f, planarSmoothing);
        droneMainShellScale = Mathf.Clamp01(droneMainShellScale);
        Transform generatedRoot = transform.Find(GeneratedRootName);
        if (generatedRoot != null)
        {
            ConfigurePlanarTranslation(
                generatedRoot.GetComponentInChildren<PredictiveGhostAvatarLocomotion>(true));
            ConfigureDroneMainShellScale(
                generatedRoot.GetComponentInChildren<GhostAvatarAnimationDriver>(true));
        }
    }

    void UpgradeCourseVisibilitySettingsIfNeeded()
    {
        if (outerTubeFresnelShader == null)
        {
            outerTubeFresnelShader = Shader.Find(TubeFresnelShaderName);
        }
        if (courseVisibilityVersion >= CurrentCourseVisibilityVersion)
        {
            return;
        }

        outerTubeAlpha = 0.14f;
        outerTubeFresnelColor = new Color(0.5f, 0.95f, 1f, 1f);
        outerTubeFresnelAlpha = 0.28f;
        outerTubeFresnelPower = 2.2f;
        tubeTopGuideColor = new Color(0.92f, 0.98f, 1f, 1f);
        tubeSideGuideColor = new Color(0.16f, 0.82f, 1f, 1f);
        tubeGuideAlpha = 0.78f;
        tubeGuideAngularHalfWidth = 0.003f;
        tubeSideGuideDashPeriod = 4f;
        tubeSideGuideDashDuty = 0.55f;
        tubeScaleRingSpacing = 6f;
        tubeScaleRingTubeRadius = 0.05f;
        courseVisibilityVersion = CurrentCourseVisibilityVersion;
    }

    List<Vector3> BuildPathSamples()
    {
        Vector3 anchor = new Vector3(-8.5f, 1.15f, -5.6f);
        Vector3 position = anchor;
        Vector3 direction = Vector3.forward;
        int baseSteps = Mathf.Max(6, samplesPerCurveSegment);
        int cycles = Mathf.Clamp(repeatingSectionCycles, 1, 6);

        List<Vector3> rawSamples = new List<Vector3>();
        rawSamples.Add(position);

        AppendStraight(rawSamples, ref position, direction, 10f, 0f, baseSteps);
        AppendStraight(rawSamples, ref position, direction, 14f, 0f, baseSteps);
        AppendStraight(rawSamples, ref position, direction, 16f, 0f, baseSteps * 2);
        AppendLateralWeave(rawSamples, ref position, direction, 24f, 2.2f, cycles, baseSteps * 4 * cycles);
        AppendVerticalWeave(rawSamples, ref position, direction, 22f, 1.8f, cycles, baseSteps * 4 * cycles);
        AppendForwardHelix(rawSamples, ref position, direction, 28f, 3.6f, 0f, baseSteps * 5 * cycles, cycles);
        AppendForwardHelix(rawSamples, ref position, direction, 24f, 3.6f, 0f, baseSteps * 4 * cycles, -cycles);
        AppendStraight(rawSamples, ref position, direction, 16f, 0f, baseSteps);

        List<Vector3> scaledSamples = new List<Vector3>(rawSamples.Count);
        for (int i = 0; i < rawSamples.Count; i++)
        {
            scaledSamples.Add(ApplyRouteVariant(ScalePathPoint(rawSamples[i], anchor), anchor));
        }

        return scaledSamples;
    }

    Vector3 ApplyRouteVariant(Vector3 point, Vector3 anchor)
    {
        Vector3 delta = point - anchor;
        if (routeVariant == CourseRouteVariant.MirrorHorizontal
            || routeVariant == CourseRouteVariant.MirrorHorizontalAndVertical)
        {
            delta.x = -delta.x;
        }
        if (routeVariant == CourseRouteVariant.MirrorVertical
            || routeVariant == CourseRouteVariant.MirrorHorizontalAndVertical)
        {
            delta.y = -delta.y;
        }
        return anchor + delta;
    }

    void AppendStraight(List<Vector3> samples, ref Vector3 position, Vector3 direction, float length, float heightDelta, int steps)
    {
        Vector3 start = position;
        steps = Mathf.Max(1, steps);
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            AddPathPoint(samples, start + direction * (length * t) + Vector3.up * (heightDelta * t));
        }

        position = samples[samples.Count - 1];
    }

    void AppendLateralWeave(List<Vector3> samples, ref Vector3 position, Vector3 direction, float length, float amplitude, float cycles, int steps)
    {
        steps = Mathf.Max(8, steps);
        cycles = Mathf.Max(0.25f, cycles);
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;

        Vector3 side = Vector3.Cross(Vector3.up, direction);
        if (side.sqrMagnitude <= 1e-5f)
        {
            side = Vector3.right;
        }
        side.Normalize();

        Vector3 start = position;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float offset = -Mathf.Sin(t * Mathf.PI * 2f * cycles) * amplitude * EdgeFade(t);
            Vector3 forwardDelta = direction * (length * t);
            AddPathPoint(samples, start + forwardDelta + side * offset);
        }

        position = start + direction * length;
        AddPathPoint(samples, position);
    }

    void AppendVerticalWeave(List<Vector3> samples, ref Vector3 position, Vector3 direction, float length, float amplitude, float cycles, int steps)
    {
        steps = Mathf.Max(8, steps);
        cycles = Mathf.Max(0.25f, cycles);
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;

        Vector3 start = position;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float offset = Mathf.Sin(t * Mathf.PI * 2f * cycles) * amplitude * EdgeFade(t);
            AddPathPoint(samples, start + direction * (length * t) + Vector3.up * offset);
        }

        position = start + direction * length;
        AddPathPoint(samples, position);
    }

    static float EdgeFade(float t)
    {
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.12f));
        float fadeOut = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / 0.12f));
        return Mathf.Min(fadeIn, fadeOut);
    }

    void AppendHorizontalTurn(List<Vector3> samples, ref Vector3 position, ref Vector3 direction, float signedDegrees, float radius, float heightDelta, int steps)
    {
        steps = Mathf.Max(3, steps);
        radius = Mathf.Max(0.01f, radius);
        direction = direction.sqrMagnitude > 1e-6f ? Vector3.ProjectOnPlane(direction, Vector3.up).normalized : Vector3.forward;

        float turnSign = Mathf.Sign(signedDegrees);
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 center = position + right * (turnSign * radius);
        Vector3 radialStart = position - center;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Quaternion rotation = Quaternion.AngleAxis(signedDegrees * t, Vector3.up);
            AddPathPoint(samples, center + rotation * radialStart + Vector3.up * (heightDelta * t));
        }

        direction = (Quaternion.AngleAxis(signedDegrees, Vector3.up) * direction).normalized;
        position = samples[samples.Count - 1];
    }

    void AppendForwardHelix(List<Vector3> samples, ref Vector3 position, Vector3 direction, float length, float radius, float heightDelta, int steps, float turns = 1f)
    {
        steps = Mathf.Max(8, steps);
        float turnSign = Mathf.Abs(turns) > 1e-5f ? Mathf.Sign(turns) : 1f;
        turns = turnSign * Mathf.Max(0.25f, Mathf.Abs(turns));
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        Vector3 axisDelta = direction * length + Vector3.up * heightDelta;
        Vector3 axisDirection = axisDelta.sqrMagnitude > 1e-6f ? axisDelta.normalized : direction;

        Vector3 radialU = Vector3.Cross(Vector3.up, axisDirection);
        if (radialU.sqrMagnitude <= 1e-5f)
        {
            radialU = Vector3.Cross(Vector3.forward, axisDirection);
        }
        if (radialU.sqrMagnitude <= 1e-5f)
        {
            radialU = Vector3.right;
        }
        radialU.Normalize();

        Vector3 radialV = Vector3.Cross(axisDirection, radialU).normalized;
        Vector3 axisStart = position - radialU * radius;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float angleProgress = SmoothHelixProgress(t, helixTransitionFraction);
            float angle = angleProgress * Mathf.PI * 2f * turns;
            Vector3 axisPoint = axisStart + axisDelta * t;
            Vector3 radial = Mathf.Cos(angle) * radialU + Mathf.Sin(angle) * radialV;
            AddPathPoint(samples, axisPoint + radial * radius);
        }

        position = samples[samples.Count - 1];
    }

    static float SmoothHelixProgress(float t, float transitionFraction)
    {
        t = Mathf.Clamp01(t);
        float edge = Mathf.Clamp(transitionFraction, 0.001f, 0.49f);
        float normalization = 1f - edge;

        if (t < edge)
        {
            float u = t / edge;
            float u2 = u * u;
            float integratedSmoothStep = u2 * u - 0.5f * u2 * u2;
            return edge * integratedSmoothStep / normalization;
        }

        if (t > 1f - edge)
        {
            float u = (1f - t) / edge;
            float u2 = u * u;
            float integratedSmoothStep = u2 * u - 0.5f * u2 * u2;
            return 1f - edge * integratedSmoothStep / normalization;
        }

        return (t - edge * 0.5f) / normalization;
    }

    static void AddPathPoint(List<Vector3> samples, Vector3 point)
    {
        if (samples.Count > 0 && (samples[samples.Count - 1] - point).sqrMagnitude <= 1e-6f)
        {
            return;
        }

        samples.Add(point);
    }

    void CreateEnvironment(Transform root, Material floorMaterial)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Official Primitive Floor";
        floor.transform.SetParent(root, false);
        floor.transform.position = new Vector3(1f, -0.08f, -0.35f);
        floor.transform.localScale = new Vector3(24f, 0.12f, 14f);
        AssignMaterial(floor, floorMaterial);

        GameObject backWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backWall.name = "Low Contrast Reference Wall";
        backWall.transform.SetParent(root, false);
        backWall.transform.position = new Vector3(1f, 3.0f, 2.25f);
        backWall.transform.localScale = new Vector3(24f, 6f, 0.08f);
        AssignMaterial(backWall, floorMaterial);
    }

    void CreateLightingAndCamera(Transform root, bool createDemoCamera)
    {
        GameObject sun = new GameObject("Official Directional Light");
        sun.transform.SetParent(root, false);
        sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        Light sunLight = sun.AddComponent<Light>();
        sunLight.type = LightType.Directional;
        sunLight.intensity = 2.2f;
        sunLight.color = new Color(0.85f, 0.95f, 1f, 1f);

        GameObject accent = new GameObject("Cyan Course Accent Light");
        accent.transform.SetParent(root, false);
        accent.transform.position = new Vector3(-2f, 5f, -3f);
        Light accentLight = accent.AddComponent<Light>();
        accentLight.type = LightType.Point;
        accentLight.range = 18f;
        accentLight.intensity = 90f;
        accentLight.color = new Color(0.08f, 0.75f, 1f, 1f);

        if (!createDemoCamera)
        {
            return;
        }

        GameObject cameraObject = new GameObject(DemoCameraName);
        cameraObject.transform.SetParent(root, false);
        cameraObject.transform.position = new Vector3(0.7f, 7.3f, -13.2f);
        cameraObject.transform.rotation = Quaternion.Euler(58f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 50f;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 200f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.backgroundColor = ExperimentFallbackBackgroundColor;
        cameraObject.AddComponent<AudioListener>();
        TrySetTag(cameraObject, "MainCamera");
    }

    void CreateCenterLine(List<Vector3> path, Transform root, Material centerMaterial)
    {
        GameObject center = new GameObject("Safe Center Trajectory - LineRenderer");
        center.transform.SetParent(root, false);
        LineRenderer line = center.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = path.Count;
        line.widthMultiplier = centerLineWidth;
        line.numCapVertices = 8;
        line.numCornerVertices = 8;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = centerMaterial;
        line.SetPositions(path.ToArray());
    }

    List<float> CreateTransparentOuterTube(List<Vector3> path, Transform root, Material material)
    {
        if (!createTransparentOuterTube)
        {
            return null;
        }

        float radius = Mathf.Max(outerTubeRadius, railOffset + railRadius * 2f);
        List<float> radiusProfile = BuildTubeRadiusSamples(path);
        GameObject tube = new GameObject("Transparent Hollow Tube Wall");
        tube.transform.SetParent(root, false);

        Mesh mesh = BuildTubeWallMesh(path, radiusProfile, outerTubeRadialSegments);
        MeshFilter meshFilter = tube.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = tube.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        MeshCollider meshCollider = tube.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = false;
        meshCollider.isTrigger = false;

        IrairaBouHazard hazard = tube.AddComponent<IrairaBouHazard>();
        hazard.hazardLabel = "Transparent tube wall";

        IrairaBouTubeBoundary boundary = tube.AddComponent<IrairaBouTubeBoundary>();
        boundary.Configure(path, radiusProfile, radius);
        boundary.hazardLabel = hazard.hazardLabel;

        return radiusProfile;
    }

    void CreateTubeScaleRings(
        List<Vector3> path,
        List<float> radiusProfile,
        Transform root,
        Material ringMaterial,
        Material majorRingMaterial)
    {
        if (!createTransparentOuterTube || !createTubeScaleRings || path == null || path.Count < 2)
        {
            return;
        }

        float[] distances = BuildCumulativeDistances(path, out float totalDistance);
        float spacing = Mathf.Max(0.5f, tubeScaleRingSpacing);
        if (totalDistance <= spacing)
        {
            return;
        }

        Transform ringRoot = CreateEmpty(TubeScaleRingRootName, root).transform;
        int ringIndex = 1;
        for (float distance = spacing; distance < totalDistance - spacing * 0.35f; distance += spacing)
        {
            Vector3 center = SamplePathAtDistance(path, distances, distance, out int segmentIndex, out float segmentT);
            Vector3 tangent = SampleTangentAtSegment(path, segmentIndex);
            float wallRadius = SampleRadiusAtDistance(radiusProfile, distances, distance, segmentIndex, segmentT);
            float ringRadius = Mathf.Max(0.1f, wallRadius - Mathf.Max(0f, tubeScaleRingInset));
            bool major = ringIndex % 5 == 0;
            float tubeRadius = Mathf.Max(0.005f, tubeScaleRingTubeRadius) * (major ? 1.65f : 1f);
            Material material = major ? majorRingMaterial : ringMaterial;
            CreateScaleRingVisual($"Scale Ring {ringIndex:00} ({Mathf.RoundToInt(distance)}m)", center, tangent, ringRadius, tubeRadius, ringRoot, material);
            ringIndex++;
        }
    }

    void CreateTubeAvoidanceObstacles(List<Vector3> path, List<float> radiusProfile, Transform root, Material material)
    {
        if (!createTransparentOuterTube || !createTubeAvoidanceObstacles || path == null || path.Count < 2)
        {
            return;
        }

        float[] distances = BuildCumulativeDistances(path, out float totalDistance);
        if (totalDistance <= 6f)
        {
            return;
        }

        Transform obstacleRoot = CreateEmpty(TubeAvoidanceRootName, root).transform;
        float[] obstaclePercents = { 0.16f, 0.29f, 0.43f, 0.56f, 0.71f, 0.84f };
        Vector2[] obstacleOffsets =
        {
            new Vector2(-0.65f, 0.28f),
            new Vector2(0.58f, -0.18f),
            new Vector2(0.02f, 0.68f),
            new Vector2(-0.48f, -0.52f),
            new Vector2(0.68f, 0.36f),
            new Vector2(-0.28f, -0.66f)
        };
        float[] radiusMultipliers = { 1f, 0.85f, 1.12f, 0.9f, 1.05f, 0.95f };

        for (int i = 0; i < obstaclePercents.Length; i++)
        {
            float targetDistance = Mathf.Clamp(totalDistance * obstaclePercents[i], 2f, totalDistance - 2f);
            Vector3 pathCenter = SamplePathAtDistance(path, distances, targetDistance, out int segmentIndex, out float segmentT);
            Vector3 tangent = SampleTangentAtSegment(path, segmentIndex);
            GetTubeBasis(tangent, out Vector3 side, out Vector3 tubeUp);

            float wallRadius = radiusProfile != null && radiusProfile.Count > 0
                ? SampleRadiusAtDistance(radiusProfile, distances, targetDistance, segmentIndex, segmentT)
                : checkpointRadius + routeGateWallInset;
            float obstacleRadius = Mathf.Min(
                Mathf.Max(0.1f, tubeAvoidanceObstacleRadius * radiusMultipliers[i]),
                Mathf.Max(0.1f, wallRadius * 0.24f));
            float clearanceRadius = Mathf.Max(
                0.1f,
                wallRadius - obstacleRadius - predictiveRigCollisionRadius - predictiveRigCollisionSkinWidth - 0.35f);
            Vector2 localOffset = obstacleOffsets[i] * tubeAvoidanceObstacleOffsetFraction;
            Vector3 obstaclePosition = pathCenter
                + side * (localOffset.x * clearanceRadius)
                + tubeUp * (localOffset.y * clearanceRadius);

            GameObject obstacle = CreateSolidObstacleSphere(
                $"Avoidance Obstacle {i + 1:00}",
                obstaclePosition,
                obstacleRadius,
                obstacleRoot,
                material);
            IrairaBouHazard hazard = obstacle.AddComponent<IrairaBouHazard>();
            hazard.hazardLabel = "Tube avoidance obstacle";
        }
    }

    void CreateElectricRails(List<Vector3> path, Transform root, Material hazardMaterial, Material jointMaterial)
    {
        Transform railsRoot = CreateEmpty("Electric Collision Rails", root).transform;
        List<Vector3> sides = ComputeSides(path);

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 leftA = path[i] + sides[i] * railOffset;
            Vector3 leftB = path[i + 1] + sides[i + 1] * railOffset;
            Vector3 rightA = path[i] - sides[i] * railOffset;
            Vector3 rightB = path[i + 1] - sides[i + 1] * railOffset;
            CreateVisualCylinder($"Left Electric Rail Visual {i:00}", leftA, leftB, railRadius, railsRoot, hazardMaterial);
            CreateVisualCylinder($"Right Electric Rail Visual {i:00}", rightA, rightB, railRadius, railsRoot, hazardMaterial);
        }

        if (!showElectricRailJointSpheres)
        {
            return;
        }

        for (int i = 0; i < path.Count; i += Mathf.Max(1, samplesPerCurveSegment / 2))
        {
            CreateVisualSphere($"Left Rail Joint Visual {i:00}", path[i] + sides[i] * railOffset, railRadius * 1.7f, railsRoot, jointMaterial);
            CreateVisualSphere($"Right Rail Joint Visual {i:00}", path[i] - sides[i] * railOffset, railRadius * 1.7f, railsRoot, jointMaterial);
        }
    }

    void CreateCheckpoints(
        List<Vector3> path,
        List<float> radiusProfile,
        Transform root,
        Material checkpointMaterial,
        Material finishMaterial)
    {
        Transform checkpointRoot = CreateEmpty("Checkpoints And Finish", root).transform;
        float[] distances = BuildCumulativeDistances(path, out float totalDistance);
        float[] percents = { 0.18f, 0.36f, 0.54f, 0.72f };
        for (int i = 0; i < percents.Length; i++)
        {
            float targetDistance = totalDistance * percents[i];
            Vector3 center = SamplePathAtDistance(path, distances, targetDistance, out int segmentIndex, out float segmentT);
            Vector3 tangent = SampleTangentAtSegment(path, segmentIndex);
            float wallRadius = SampleRadiusAtDistance(radiusProfile, distances, targetDistance, segmentIndex, segmentT);
            float ringRadius = GetRouteGateRingRadius(wallRadius);
            GameObject ring = CreateRing($"Checkpoint {i + 1}", center, tangent, ringRadius, checkpointTubeRadius, checkpointRoot, checkpointMaterial);
            CreateFullWidthRouteGateTrigger(ring, tangent, wallRadius);
            IrairaBouCheckpoint checkpoint = ring.AddComponent<IrairaBouCheckpoint>();
            checkpoint.index = i + 1;
        }

        int finishIndex = path.Count - 1;
        Vector3 finishTangent = GetTangent(path, path.Count - 2);
        float finishWallRadius = radiusProfile != null && radiusProfile.Count > 0
            ? radiusProfile[Mathf.Clamp(finishIndex, 0, radiusProfile.Count - 1)]
            : outerTubeRadius;
        GameObject finish = CreateRing(
            "Finish Trigger Ring",
            path[finishIndex],
            finishTangent,
            GetRouteGateRingRadius(finishWallRadius),
            checkpointTubeRadius * 1.2f,
            checkpointRoot,
            finishMaterial);
        CreateFullWidthRouteGateTrigger(finish, finishTangent, finishWallRadius);
        finish.AddComponent<IrairaBouFinish>();
    }

    float GetRouteGateRingRadius(float wallRadius)
    {
        float inset = Mathf.Max(routeGateWallInset, checkpointTubeRadius * 1.5f);
        return Mathf.Max(0.1f, wallRadius - inset);
    }

    void CreateFullWidthRouteGateTrigger(GameObject marker, Vector3 tangent, float wallRadius)
    {
        GameObject triggerObject = CreateEmpty("Full-Width Route Gate Trigger", marker.transform);
        triggerObject.transform.localPosition = Vector3.zero;
        GetTubeBasis(tangent, out _, out Vector3 tubeUp);
        Vector3 forward = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;
        triggerObject.transform.rotation = Quaternion.LookRotation(forward, tubeUp);

        float halfExtent = Mathf.Max(
            0.1f,
            wallRadius
                - predictiveRigCollisionRadius
                - predictiveRigCollisionSkinWidth
                - routeGateEdgeMargin);
        BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(halfExtent * 2f, halfExtent * 2f, routeGateTriggerDepth);
    }

    static bool HasFullWidthRouteGates(Transform generatedRoot)
    {
        IrairaBouCheckpoint[] checkpoints = generatedRoot.GetComponentsInChildren<IrairaBouCheckpoint>(true);
        if (checkpoints.Length != 4)
        {
            return false;
        }

        for (int i = 0; i < checkpoints.Length; i++)
        {
            if (checkpoints[i].GetComponentInChildren<BoxCollider>(true) == null)
            {
                return false;
            }
        }

        IrairaBouFinish finish = generatedRoot.GetComponentInChildren<IrairaBouFinish>(true);
        return finish != null && finish.GetComponentInChildren<BoxCollider>(true) != null;
    }

    void CreatePrecisionGates(List<Vector3> path, Transform root, Material hazardMaterial)
    {
        Transform obstacleRoot = CreateEmpty("Official Primitive Bonus Hazards", root).transform;
        List<Vector3> sides = ComputeSides(path);
        int[] gateIndices =
        {
            Mathf.RoundToInt(path.Count * 0.28f),
            Mathf.RoundToInt(path.Count * 0.48f),
            Mathf.RoundToInt(path.Count * 0.66f)
        };

        for (int i = 0; i < gateIndices.Length; i++)
        {
            int index = Mathf.Clamp(gateIndices[i], 2, path.Count - 3);
            Vector3 center = path[index];
            Vector3 side = sides[index];
            Vector3 up = Vector3.up;
            CreateHazardCube($"Narrow Gate {i + 1} Left Post", center + side * (railOffset * 0.48f) + up * 0.28f, Quaternion.LookRotation(side, up), new Vector3(0.08f, 0.72f, 0.08f), obstacleRoot, hazardMaterial);
            CreateHazardCube($"Narrow Gate {i + 1} Right Post", center - side * (railOffset * 0.48f) + up * 0.28f, Quaternion.LookRotation(side, up), new Vector3(0.08f, 0.72f, 0.08f), obstacleRoot, hazardMaterial);
        }

        int rotatingIndex = Mathf.Clamp(Mathf.RoundToInt(path.Count * 0.58f), 2, path.Count - 3);
        GameObject pivot = CreateEmpty("Slow Rotating Sweep Hazard", obstacleRoot);
        pivot.transform.position = path[rotatingIndex];
        IrairaBouRotator rotator = pivot.AddComponent<IrairaBouRotator>();
        rotator.localAxis = Vector3.up;
        rotator.degreesPerSecond = 34f;

        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "Rotating Sweep Arm - Trigger";
        arm.transform.SetParent(pivot.transform, false);
        arm.transform.localPosition = Vector3.zero;
        arm.transform.localScale = new Vector3(railOffset * 1.55f, 0.08f, 0.08f);
        AssignMaterial(arm, hazardMaterial);
        Collider armCollider = arm.GetComponent<Collider>();
        if (armCollider != null) armCollider.isTrigger = true;
        IrairaBouHazard hazard = arm.AddComponent<IrairaBouHazard>();
        hazard.hazardLabel = "Rotating sweep";
    }

    void CreateStartAndFinish(List<Vector3> path, Transform root, Material startMaterial, Material finishMaterial)
    {
        Transform markerRoot = CreateEmpty("Start Finish Platforms", root).transform;
        CreatePlatform("Start Platform", path[0] + Vector3.down * 0.2f, startMaterial, markerRoot);
        CreatePlatform("Finish Platform", path[path.Count - 1] + Vector3.down * 0.2f, finishMaterial, markerRoot);
    }

    void CreateLabels(List<Vector3> path, Transform root)
    {
        Transform labelRoot = CreateEmpty("Floating Labels", root).transform;
        CreateText("Title", "3D IRAIRA-BOU\nUnity Official Primitives", new Vector3(0f, 5.4f, -5.8f), 0.22f, new Color(0.85f, 0.95f, 1f, 1f), labelRoot);
        CreateText("Start Label", "START", path[0] + new Vector3(0f, 0.8f, -0.35f), 0.16f, new Color(0.55f, 0.75f, 1f, 1f), labelRoot);
        CreateText("Finish Label", "FINISH", path[path.Count - 1] + new Vector3(0f, 0.95f, -0.35f), 0.16f, new Color(0.55f, 1f, 0.72f, 1f), labelRoot);
    }

    void CreateDemoProbe(Vector3 start, Transform root, Material probeMaterial)
    {
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        probe.name = "Keyboard Demo Player Probe";
        probe.transform.SetParent(root, false);
        probe.transform.position = start;
        probe.transform.localScale = Vector3.one * (probeRadius * 2f);
        AssignMaterial(probe, probeMaterial);

        Rigidbody body = probe.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        SphereCollider sphere = probe.GetComponent<SphereCollider>();
        if (sphere != null) sphere.radius = 0.5f;

        IrairaBouPlayerProbe playerProbe = probe.AddComponent<IrairaBouPlayerProbe>();
        playerProbe.startPosition = start;
        playerProbe.moveSpeed = demoProbeMoveSpeed;
        playerProbe.verticalSpeed = demoProbeVerticalSpeed;

        GameObject status = CreateText("Probe Status Label", "READY", start + new Vector3(0f, 1.4f, -1.1f), 0.09f, new Color(0.8f, 1f, 1f, 1f), root);
        playerProbe.statusLabel = status.GetComponent<TextMesh>();
    }

    void CreatePredictiveDroneRig(List<Vector3> path, Transform root)
    {
        if (!createPredictiveDroneRig || path == null || path.Count == 0)
        {
            return;
        }

        Vector3 headStartPosition = path[0] + predictiveRigStartOffset;
        Vector3 rigStartPosition = headStartPosition - Vector3.up * expectedEyeHeight;

        GameObject rig = new GameObject(PredictiveRigName);
        rig.transform.SetParent(root, false);
        rig.transform.position = rigStartPosition;
        rig.transform.rotation = Quaternion.identity;

        Rigidbody rigBody = rig.AddComponent<Rigidbody>();
        rigBody.useGravity = false;
        rigBody.isKinematic = true;

        GameObject cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(rig.transform, false);
        cameraOffset.transform.localPosition = Vector3.zero;
        cameraOffset.transform.localRotation = Quaternion.identity;

        GameObject cameraObject = new GameObject(PredictiveCameraName);
        cameraObject.transform.SetParent(cameraOffset.transform, false);
        cameraObject.transform.localPosition = Vector3.up * expectedEyeHeight;
        cameraObject.transform.localRotation = Quaternion.identity;
        TrySetTag(cameraObject, "MainCamera");

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 200f;
        camera.fieldOfView = 60f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.backgroundColor = ExperimentFallbackBackgroundColor;
        cameraObject.AddComponent<AudioListener>();

        TrackedPoseDriver trackedPoseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
        ConfigureTrackedPoseDriver(trackedPoseDriver);

        GameObject ghost = new GameObject("Drone Body Avatar");
        ghost.transform.SetParent(root, false);
        ghost.transform.position = rigStartPosition;
        ghost.transform.rotation = Quaternion.identity;

        GhostAvatarAnimationDriver ghostDriver = ghost.AddComponent<GhostAvatarAnimationDriver>();
        ghostDriver.visualPrefab = droneVisualPrefab;
        ghostDriver.buildProceduralVisual = droneVisualPrefab == null;
        ghostDriver.hideExistingRenderersOnStart = true;
        ghostDriver.disableExistingCollidersOnStart = true;
        ghostDriver.disablePrefabControlScripts = true;
        ghostDriver.preserveCustomPrefabBehaviours = true;
        ghostDriver.disablePrefabColliders = true;
        ghostDriver.disablePrefabCamerasAndLights = true;
        ghostDriver.applyGhostMaterialToPrefab = true;
        ghostDriver.preservePrefabMaterialTextures = true;
        ConfigureDroneMainShellScale(ghostDriver);
        ghostDriver.proceduralVisualShape = GhostAvatarAnimationDriver.ProceduralVisualShape.Drone;
        ghostDriver.enablePrefabMotionFallback = true;
        ghostDriver.ghostColor = new Color(0.18f, 0.8f, 1f, 1f);
        ghostDriver.visualLocalPosition = Vector3.up * Mathf.Max(0f, expectedEyeHeight - 0.8f);
        ghostDriver.visualLocalEulerAngles = Vector3.zero;
        ghostDriver.visualLocalScale = Vector3.one;

        PredictiveGhostAvatarLocomotion locomotion = rig.AddComponent<PredictiveGhostAvatarLocomotion>();
        locomotion.targetRig = rig.transform;
        locomotion.head = cameraObject.transform;
        locomotion.droneBodyAvatar = ghost.transform;
        locomotion.visualizationMode = PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost;
        locomotion.enableInputToRigDelay = true;
        locomotion.enableSoftCollisionBlocking = true;
        locomotion.collisionProbe = cameraObject.transform;
        locomotion.useHeadAsCollisionProbe = true;
        locomotion.collisionProbeRadius = predictiveRigCollisionRadius;
        locomotion.collisionSkinWidth = predictiveRigCollisionSkinWidth;
        locomotion.tubeBoundary = root.GetComponentInChildren<IrairaBouTubeBoundary>(true);
        locomotion.autoFindTubeBoundary = true;
        ConfigurePlanarTranslation(locomotion);

        ghostDriver.locomotion = locomotion;
    }

    void EnsureExperimentComponents()
    {
        PredictiveFlyObjectiveLogger logger = GetComponent<PredictiveFlyObjectiveLogger>();
        if (logger == null)
        {
            logger = gameObject.AddComponent<PredictiveFlyObjectiveLogger>();
        }

        PredictiveFlyExperimentController controller = GetComponent<PredictiveFlyExperimentController>();
        if (controller == null)
        {
            controller = gameObject.AddComponent<PredictiveFlyExperimentController>();
        }
        controller.enabled = true;

        PredictiveFlyRouteMarkerVisualizer legacyMarkerVisualizer = GetComponent<PredictiveFlyRouteMarkerVisualizer>();
        if (legacyMarkerVisualizer != null)
        {
            legacyMarkerVisualizer.enabled = false;
        }

        controller.sceneBuilder = this;
        controller.logger = logger;
        logger.useKeyboardControls = false;
        logger.stopOnFinish = true;
        logger.stopOnModeChange = true;
        logger.routeId = RouteId;
    }

    void ConfigureExperimentComponents(Transform generatedRoot)
    {
        EnsureExperimentComponents();

        PredictiveFlyObjectiveLogger logger = GetComponent<PredictiveFlyObjectiveLogger>();
        PredictiveFlyExperimentController controller = GetComponent<PredictiveFlyExperimentController>();
        PredictiveGhostAvatarLocomotion locomotion = generatedRoot != null
            ? generatedRoot.GetComponentInChildren<PredictiveGhostAvatarLocomotion>(true)
            : null;
        GhostAvatarAnimationDriver ghostDriver = generatedRoot != null
            ? generatedRoot.GetComponentInChildren<GhostAvatarAnimationDriver>(true)
            : null;
        IrairaBouTubeBoundary boundary = generatedRoot != null
            ? generatedRoot.GetComponentInChildren<IrairaBouTubeBoundary>(true)
            : null;

        if (logger != null)
        {
            logger.locomotion = locomotion;
            logger.rigRoot = locomotion != null ? locomotion.targetRig : null;
            logger.head = locomotion != null ? locomotion.head : null;
            logger.probe = locomotion != null && locomotion.collisionProbe != null
                ? locomotion.collisionProbe
                : logger.head;
            logger.droneBodyAvatar = locomotion != null ? locomotion.droneBodyAvatar : null;
            logger.stateGhostAvatar = locomotion != null ? locomotion.stateGhostAvatar : null;
            logger.tubeBoundary = boundary;
            logger.routeId = RouteId;
            if (locomotion != null)
            {
                logger.probeRadius = locomotion.collisionProbeRadius;
                logger.contactTolerance = Mathf.Max(
                    logger.contactTolerance,
                    locomotion.collisionSkinWidth + 0.005f);
            }
        }

        if (controller != null)
        {
            controller.sceneBuilder = this;
            controller.logger = logger;
            controller.locomotion = locomotion;
        }

        ConfigurePlanarTranslation(locomotion);
        ConfigureDroneMainShellScale(ghostDriver);

    }

    void ConfigurePlanarTranslation(PredictiveGhostAvatarLocomotion locomotion)
    {
        if (locomotion == null)
        {
            return;
        }

        locomotion.horizontalSpeed = planarHorizontalSpeed;
        locomotion.planarDeadZone = planarDeadZone;
        locomotion.planarMaxOffset = Mathf.Max(planarDeadZone + 0.001f, planarMaxOffset);
        locomotion.planarResponseExponent = planarResponseExponent;
        locomotion.planarSmoothing = planarSmoothing;
    }

    void ConfigureDroneMainShellScale(GhostAvatarAnimationDriver driver)
    {
        if (driver == null)
        {
            return;
        }

        driver.prefabMainShellScale = droneMainShellScale;
    }

    GameObject CreateRing(string name, Vector3 center, Vector3 forward, float radius, float tubeRadius, Transform parent, Material material)
    {
        GameObject ring = CreateEmpty(name, parent);
        ring.transform.position = center;
        Vector3 normal = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, normal);
        if (side.sqrMagnitude <= 1e-5f) side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(normal, side).normalized;

        const int segmentCount = 24;
        for (int i = 0; i < segmentCount; i++)
        {
            float a0 = (i / (float)segmentCount) * Mathf.PI * 2f;
            float a1 = ((i + 1) / (float)segmentCount) * Mathf.PI * 2f;
            Vector3 p0 = center + (Mathf.Cos(a0) * side + Mathf.Sin(a0) * up) * radius;
            Vector3 p1 = center + (Mathf.Cos(a1) * side + Mathf.Sin(a1) * up) * radius;
            CreateCylinderBetween($"Ring Segment {i:00}", p0, p1, tubeRadius, ring.transform, material, false, false);
        }

        return ring;
    }

    GameObject CreateScaleRingVisual(
        string name,
        Vector3 center,
        Vector3 forward,
        float radius,
        float tubeRadius,
        Transform parent,
        Material material)
    {
        GameObject ring = CreateEmpty(name, parent);
        Vector3 normal = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, normal);
        if (side.sqrMagnitude <= 1e-5f)
        {
            side = Vector3.right;
        }
        side.Normalize();
        Vector3 up = Vector3.Cross(normal, side).normalized;

        const int segmentCount = 24;
        LineRenderer line = ring.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = segmentCount;
        line.widthMultiplier = Mathf.Max(0.005f, tubeRadius) * 2f;
        line.numCornerVertices = 2;
        line.numCapVertices = 0;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
            line.SetPosition(i, center + (Mathf.Cos(angle) * side + Mathf.Sin(angle) * up) * radius);
        }

        return ring;
    }

    static void GetTubeBasis(Vector3 tangent, out Vector3 side, out Vector3 tubeUp)
    {
        Vector3 normal = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;
        side = Vector3.Cross(Vector3.up, normal);
        if (side.sqrMagnitude <= 1e-5f)
        {
            side = Vector3.Cross(Vector3.forward, normal);
        }
        if (side.sqrMagnitude <= 1e-5f)
        {
            side = Vector3.right;
        }
        side.Normalize();

        tubeUp = Vector3.Cross(normal, side);
        if (tubeUp.sqrMagnitude <= 1e-5f)
        {
            tubeUp = Vector3.up;
        }
        tubeUp.Normalize();
        if (Vector3.Dot(tubeUp, Vector3.up) < 0f)
        {
            tubeUp = -tubeUp;
        }
    }

    List<float> BuildTubeRadiusSamples(List<Vector3> path)
    {
        List<float> radii = new List<float>(path.Count);
        float uniformRadius = Mathf.Max(railOffset + railRadius * 2f, outerTubeRadius);

        for (int i = 0; i < path.Count; i++)
        {
            radii.Add(uniformRadius);
        }

        return radii;
    }

    static float[] BuildCumulativeDistances(List<Vector3> path, out float totalDistance)
    {
        float[] distances = new float[path.Count];
        totalDistance = 0f;

        for (int i = 1; i < path.Count; i++)
        {
            totalDistance += Vector3.Distance(path[i - 1], path[i]);
            distances[i] = totalDistance;
        }

        return distances;
    }

    static Vector3 SamplePathAtDistance(List<Vector3> path, float[] distances, float targetDistance, out int segmentIndex, out float segmentT)
    {
        segmentIndex = 0;
        segmentT = 0f;

        if (path == null || path.Count == 0)
        {
            return Vector3.zero;
        }

        if (path.Count == 1 || distances == null || distances.Length != path.Count || targetDistance <= 0f)
        {
            return path[0];
        }

        for (int i = 1; i < path.Count; i++)
        {
            if (targetDistance > distances[i])
            {
                continue;
            }

            segmentIndex = i - 1;
            float span = Mathf.Max(1e-5f, distances[i] - distances[i - 1]);
            segmentT = Mathf.Clamp01((targetDistance - distances[i - 1]) / span);
            return Vector3.Lerp(path[i - 1], path[i], segmentT);
        }

        segmentIndex = Mathf.Max(0, path.Count - 2);
        segmentT = 1f;
        return path[path.Count - 1];
    }

    static Vector3 SampleTangentAtSegment(List<Vector3> path, int segmentIndex)
    {
        if (path == null || path.Count < 2)
        {
            return Vector3.forward;
        }

        int a = Mathf.Clamp(segmentIndex, 0, path.Count - 2);
        Vector3 tangent = path[a + 1] - path[a];
        return tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;
    }

    float SampleRadiusAtDistance(List<float> radiusProfile, float[] distances, float targetDistance, int segmentIndex, float segmentT)
    {
        if (radiusProfile == null || radiusProfile.Count == 0)
        {
            return outerTubeRadius;
        }

        int a = Mathf.Clamp(segmentIndex, 0, radiusProfile.Count - 1);
        int b = Mathf.Clamp(segmentIndex + 1, 0, radiusProfile.Count - 1);
        return Mathf.Lerp(radiusProfile[a], radiusProfile[b], Mathf.Clamp01(segmentT));
    }

    static float BlendRadiusSection(float currentMultiplier, float normalizedDistance, float start, float end, float targetMultiplier, float fade)
    {
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, start + fade, normalizedDistance));
        float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(end - fade, end, normalizedDistance));
        float weight = Mathf.Clamp01(Mathf.Min(fadeIn, fadeOut));
        return Mathf.Lerp(currentMultiplier, targetMultiplier, weight);
    }

    Mesh BuildTubeWallMesh(List<Vector3> path, List<float> radiusProfile, int radialSegments)
    {
        radialSegments = Mathf.Max(8, radialSegments);
        int ringVertexCount = radialSegments + 1;
        List<Vector3> sides = ComputeSides(path);
        float[] pathDistances = BuildCumulativeDistances(path, out _);
        Vector3[] vertices = new Vector3[path.Count * ringVertexCount];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[(path.Count - 1) * radialSegments * 12];
        int triangleIndex = 0;

        for (int i = 0; i < path.Count; i++)
        {
            float radius = GetRadiusAt(radiusProfile, i);
            Vector3 tangent = GetTangent(path, i);
            Vector3 side = sides[i];
            Vector3 tubeUp = Vector3.Cross(tangent, side);
            if (tubeUp.sqrMagnitude <= 1e-6f)
            {
                tubeUp = Vector3.up;
            }
            tubeUp.Normalize();

            for (int j = 0; j <= radialSegments; j++)
            {
                float angle = (j / (float)radialSegments) * Mathf.PI * 2f;
                Vector3 radial = Mathf.Cos(angle) * side + Mathf.Sin(angle) * tubeUp;
                int vertexIndex = i * ringVertexCount + j;
                vertices[vertexIndex] = path[i] + radial * radius;
                normals[vertexIndex] = radial.normalized;
                uvs[vertexIndex] = new Vector2(j / (float)radialSegments, pathDistances[i]);
            }
        }

        for (int i = 0; i < path.Count - 1; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                int a = i * ringVertexCount + j;
                int b = a + 1;
                int c = (i + 1) * ringVertexCount + j;
                int d = c + 1;

                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = d;

                // Duplicate the faces reversed so physics and rendering behave consistently from inside the tube.
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = d;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = b;
            }
        }

        Mesh mesh = new Mesh
        {
            name = DirectionalTubeMeshName
        };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    float GetRadiusAt(List<float> radiusProfile, int index)
    {
        if (radiusProfile == null || radiusProfile.Count == 0)
        {
            return outerTubeRadius;
        }

        return radiusProfile[Mathf.Clamp(index, 0, radiusProfile.Count - 1)];
    }

    void CreatePlatform(string name, Vector3 position, Material material, Transform parent)
    {
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = name;
        platform.transform.SetParent(parent, false);
        platform.transform.position = position;
        platform.transform.localScale = new Vector3(1.3f, 0.08f, 1.3f);
        AssignMaterial(platform, material);
    }

    GameObject CreateText(string name, string text, Vector3 position, float characterSize, Color color, Transform parent)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.position = position;
        textObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = characterSize;
        textMesh.fontSize = 64;
        textMesh.color = color;
        return textObject;
    }

    void CreateHazardCylinder(string name, Vector3 a, Vector3 b, float radius, Transform parent, Material material)
    {
        GameObject cylinder = CreateCylinderBetween(name, a, b, radius, parent, material, true);
        IrairaBouHazard hazard = cylinder.AddComponent<IrairaBouHazard>();
        hazard.hazardLabel = "Electric rail";
    }

    void CreateVisualCylinder(string name, Vector3 a, Vector3 b, float radius, Transform parent, Material material)
    {
        CreateCylinderBetween(name, a, b, radius, parent, material, false, false);
    }

    void CreateVisualSphere(string name, Vector3 position, float radius, Transform parent, Material material)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        sphere.transform.SetParent(parent, false);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * (radius * 2f);
        AssignMaterial(sphere, material);
        RemoveCollider(sphere);
    }

    GameObject CreateSolidObstacleSphere(string name, Vector3 position, float radius, Transform parent, Material material)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        sphere.transform.SetParent(parent, false);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * (radius * 2f);
        AssignMaterial(sphere, material);
        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = false;
        }
        return sphere;
    }

    void CreateHazardSphere(string name, Vector3 position, float radius, Transform parent, Material material)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        sphere.transform.SetParent(parent, false);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * (radius * 2f);
        AssignMaterial(sphere, material);
        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null) collider.isTrigger = true;
        sphere.AddComponent<IrairaBouHazard>();
    }

    void CreateHazardCube(string name, Vector3 position, Quaternion rotation, Vector3 scale, Transform parent, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = position;
        cube.transform.rotation = rotation;
        cube.transform.localScale = scale;
        AssignMaterial(cube, material);
        Collider collider = cube.GetComponent<Collider>();
        if (collider != null) collider.isTrigger = true;
        cube.AddComponent<IrairaBouHazard>();
    }

    GameObject CreateCylinderBetween(string name, Vector3 a, Vector3 b, float radius, Transform parent, Material material, bool trigger, bool keepCollider = true)
    {
        Vector3 delta = b - a;
        float length = delta.magnitude;
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = name;
        cylinder.transform.SetParent(parent, false);
        cylinder.transform.position = (a + b) * 0.5f;
        cylinder.transform.rotation = length > 1e-6f ? Quaternion.FromToRotation(Vector3.up, delta / length) : Quaternion.identity;
        cylinder.transform.localScale = new Vector3(radius * 2f, Mathf.Max(length * 0.5f, 0.001f), radius * 2f);
        AssignMaterial(cylinder, material);
        Collider collider = cylinder.GetComponent<Collider>();
        if (collider != null)
        {
            if (keepCollider)
            {
                collider.isTrigger = trigger;
            }
            else
            {
                RemoveCollider(cylinder);
            }
        }
        return cylinder;
    }

    void RemoveCollider(GameObject go)
    {
        Collider collider = go.GetComponent<Collider>();
        if (collider == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(collider);
        }
        else
        {
            DestroyImmediate(collider);
        }
    }

    GameObject CreateEmpty(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    Material CreateMaterial(string name, Color color, float emissionMultiplier)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);
        material.name = name;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emissionMultiplier);
        }

        return material;
    }

    Material CreateTransparentMaterial(string name, Color color, float emissionMultiplier)
    {
        Shader tubeShader = ResolveTubeFresnelShader();
        Material material = tubeShader != null
            ? new Material(tubeShader) { name = name }
            : CreateMaterial(name, color, emissionMultiplier);
        ConfigureTransparentMaterial(material, color, emissionMultiplier);
        return material;
    }

    void ConfigureTransparentMaterial(Material material, Color color, float emissionMultiplier)
    {
        if (material == null)
        {
            return;
        }

        Shader tubeShader = ResolveTubeFresnelShader();
        if (tubeShader != null && material.shader != tubeShader)
        {
            material.shader = tubeShader;
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_FresnelColor")) material.SetColor("_FresnelColor", outerTubeFresnelColor);
        if (material.HasProperty("_FresnelAlpha")) material.SetFloat("_FresnelAlpha", outerTubeFresnelAlpha);
        if (material.HasProperty("_FresnelPower")) material.SetFloat("_FresnelPower", outerTubeFresnelPower);
        if (material.HasProperty("_TopGuideColor")) material.SetColor("_TopGuideColor", tubeTopGuideColor);
        if (material.HasProperty("_SideGuideColor")) material.SetColor("_SideGuideColor", tubeSideGuideColor);
        if (material.HasProperty("_GuideAlpha")) material.SetFloat("_GuideAlpha", tubeGuideAlpha);
        if (material.HasProperty("_GuideHalfWidth")) material.SetFloat("_GuideHalfWidth", tubeGuideAngularHalfWidth);
        if (material.HasProperty("_SideDashPeriod")) material.SetFloat("_SideDashPeriod", tubeSideGuideDashPeriod);
        if (material.HasProperty("_SideDashDuty")) material.SetFloat("_SideDashDuty", tubeSideGuideDashDuty);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emissionMultiplier);
        }
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_BlendModePreserveSpecular")) material.SetFloat("_BlendModePreserveSpecular", 1f);
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.86f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("MotionVectors", false);
        material.doubleSidedGI = true;
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    Shader ResolveTubeFresnelShader()
    {
        return outerTubeFresnelShader != null
            ? outerTubeFresnelShader
            : Shader.Find(TubeFresnelShaderName);
    }

    void AssignMaterial(GameObject go, Material material)
    {
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    List<Vector3> ComputeSides(List<Vector3> path)
    {
        List<Vector3> sides = new List<Vector3>(path.Count);
        Vector3 last = Vector3.right;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 tangent = GetTangent(path, i);
            Vector3 side = Vector3.Cross(Vector3.up, tangent);
            if (side.sqrMagnitude <= 1e-5f)
            {
                side = Vector3.Cross(Vector3.forward, tangent);
            }
            if (side.sqrMagnitude <= 1e-5f)
            {
                side = last;
            }
            side.Normalize();
            if (Vector3.Dot(side, last) < 0f)
            {
                side = -side;
            }
            sides.Add(side);
            last = side;
        }

        return sides;
    }

    Vector3 GetTangent(List<Vector3> path, int index)
    {
        int a = Mathf.Max(0, index - 1);
        int b = Mathf.Min(path.Count - 1, index + 1);
        Vector3 tangent = path[b] - path[a];
        return tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;
    }

    Vector3 ScalePathPoint(Vector3 point, Vector3 anchor)
    {
        return anchor + (point - anchor) * courseLengthScale;
    }

    bool IsGeneratedTubeShapeOutOfDate(IrairaBouTubeBoundary tubeBoundary)
    {
        if (tubeBoundary.centerline == null || tubeBoundary.centerline.Length == 0)
        {
            return true;
        }

        List<Vector3> expectedPath = BuildPathSamples();
        List<float> expectedRadii = BuildTubeRadiusSamples(expectedPath);
        if (tubeBoundary.centerline.Length != expectedPath.Count)
        {
            return true;
        }

        if (IsGeneratedCenterlineOutOfDate(tubeBoundary.centerline, expectedPath))
        {
            return true;
        }

        Vector3 currentEnd = tubeBoundary.centerline[tubeBoundary.centerline.Length - 1];
        Vector3 expectedEnd = expectedPath[expectedPath.Count - 1];
        if ((currentEnd - expectedEnd).sqrMagnitude > 0.001f)
        {
            return true;
        }

        if (tubeBoundary.radiusProfile == null || tubeBoundary.radiusProfile.Length != expectedRadii.Count)
        {
            return true;
        }

        float[] checkpoints = { 0.2f, 0.5f, 0.83f };
        for (int i = 0; i < checkpoints.Length; i++)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt((expectedRadii.Count - 1) * checkpoints[i]), 0, expectedRadii.Count - 1);
            float radiusError = Mathf.Abs(tubeBoundary.radiusProfile[index] - expectedRadii[index]);
            if (radiusError > 0.001f)
            {
                return true;
            }
        }

        return false;
    }

    static bool IsGeneratedCenterlineOutOfDate(Vector3[] currentPath, List<Vector3> expectedPath)
    {
        if (currentPath == null || expectedPath == null || currentPath.Length != expectedPath.Count)
        {
            return true;
        }

        const float maxAllowedSqrError = 0.0025f;
        int stride = Mathf.Max(1, expectedPath.Count / 32);
        for (int i = 0; i < expectedPath.Count; i += stride)
        {
            if ((currentPath[i] - expectedPath[i]).sqrMagnitude > maxAllowedSqrError)
            {
                return true;
            }
        }

        int last = expectedPath.Count - 1;
        return (currentPath[last] - expectedPath[last]).sqrMagnitude > maxAllowedSqrError;
    }

    void ClearGeneratedRoot()
    {
        Transform existing = transform.Find(GeneratedRootName);
        if (existing == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(existing.gameObject);
        }
        else
        {
            DestroyImmediate(existing.gameObject);
        }
    }

    bool HasLegacyRootObjects()
    {
        return transform.parent == null && GameObject.Find(LegacyRigName) != null;
    }

    void ClearLegacyRootObjects()
    {
        if (transform.parent != null)
        {
            return;
        }

        GameObject legacyRig = GameObject.Find(LegacyRigName);
        if (legacyRig == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(legacyRig);
        }
        else
        {
            DestroyImmediate(legacyRig);
        }
    }

    static int CountEnabledCameras(Transform root)
    {
        if (root == null)
        {
            return 0;
        }

        int count = 0;
        Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].enabled && cameras[i].targetTexture == null)
            {
                count++;
            }
        }

        return count;
    }

    static bool HasTubeOnlyExcludedContent(Transform root)
    {
        return root.Find("Official Primitive Floor") != null
            || root.Find("Low Contrast Reference Wall") != null
            || root.Find("Safe Center Trajectory - LineRenderer") != null
            || root.Find("Electric Collision Rails") != null
            || root.Find("Official Primitive Bonus Hazards") != null
            || root.Find("Start Finish Platforms") != null
            || root.Find("Floating Labels") != null
            || root.Find("Keyboard Demo Player Probe") != null;
    }

    static int CountEnabledAudioListeners(Transform root)
    {
        if (root == null)
        {
            return 0;
        }

        int count = 0;
        AudioListener[] listeners = root.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null && listeners[i].enabled)
            {
                count++;
            }
        }

        return count;
    }

    static void ConfigureTrackedPoseDriver(TrackedPoseDriver driver)
    {
        if (driver == null)
        {
            return;
        }

        driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.UseRelativeTransform = false;
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
        {
            return;
        }

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
        }
    }

    static void TrySetTag(GameObject go, string tag)
    {
        try
        {
            go.tag = tag;
        }
        catch
        {
            // MainCamera is a built-in tag, but keep generation resilient if tags are unavailable.
        }
    }
}
