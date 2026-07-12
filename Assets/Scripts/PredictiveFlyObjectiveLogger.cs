using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
public class PredictiveFlyObjectiveLogger : MonoBehaviour
{
    const int TimeseriesColumnCount = 83;
    const int EventsColumnCount = 22;
    const int SummaryColumnCount = 38;
    const int EncounterColumnCount = 24;

    [Header("Participant / Trial")]
    [HideInInspector]
    public string participantId = "P001";
    [HideInInspector]
    [Min(1)] public int trialNumber = 1;
    [HideInInspector]
    [Min(1)] public int trialWithinCondition = 1;
    [HideInInspector]
    [Tooltip("Optional human-readable condition label. Leave empty to derive it from the locomotion visualization mode.")]
    public string conditionLabelOverride;
    [HideInInspector]
    [Tooltip("Route identifier frozen into every CSV row for this trial.")]
    public string routeId = "Route_A";
    [HideInInspector]
    [Tooltip("Counterbalanced route order, for example ABDC.")]
    public string routeOrder;
    [HideInInspector]
    [Tooltip("Condition-order identifier, for example ABDC.")]
    public string conditionOrder;
    [HideInInspector]
    [Tooltip("Optional build/configuration identifier. Leave empty to derive one from the active settings.")]
    public string experimentConfigId;

    [Header("Output")]
    [Tooltip("Relative paths are resolved from the Unity project root in the editor.")]
    public string outputDirectory = "Data/PredictiveFlyObjective";
    public bool startOnEnable;
    public bool stopOnDisable = true;

    [Header("Controls")]
    public bool useKeyboardControls = true;
    public KeyCode startLoggingKey = KeyCode.S;
    public KeyCode stopLoggingKey = KeyCode.Q;
    public KeyCode manualMarkerKey = KeyCode.M;
    public bool stopOnFinish;
    public bool stopOnModeChange = true;

    [Header("References")]
    public PredictiveGhostAvatarLocomotion locomotion;
    public Transform rigRoot;
    public Transform head;
    public Transform probe;
    public Transform droneBodyAvatar;
    public Transform stateGhostAvatar;
    public bool autoFindReferences = true;

    [Header("Sampling")]
    [Min(1f)] public float sampleRateHz = 30f;
    [Min(0.01f)] public float probeRadius = 0.22f;
    [Tooltip("Contact/near-wall tolerance added to the probe radius.")]
    [Min(0f)] public float contactTolerance = 0.025f;
    [Tooltip("A near-miss event starts when clearance drops below this distance without contact.")]
    [Min(0.01f)] public float nearMissDistance = 0.45f;
    [Tooltip("Obstacle encounter tracking starts below this distance.")]
    [Min(0.05f)] public float encounterStartDistance = 2.0f;
    [Tooltip("Obstacle encounter tracking ends above this distance.")]
    [Min(0.05f)] public float encounterEndDistance = 2.5f;
    [Tooltip("Input-change rate used as a proxy for avoidance correction onset.")]
    [Min(0.01f)] public float correctionInputRateThreshold = 0.8f;
    [Tooltip("Drop in closing speed used as a proxy for avoidance correction onset.")]
    [Min(0.01f)] public float correctionClosingSpeedDrop = 0.3f;
    [Tooltip("A correction signal must persist for this long before it is accepted as avoidance onset.")]
    [Min(0.03f)] public float correctionSustainSeconds = 0.15f;
    [Tooltip("Minimum interval between accepted corrections in the same obstacle encounter.")]
    [Min(0.05f)] public float correctionRefractorySeconds = 0.35f;

    [Header("Route Progress / Virtual Markers")]
    public IrairaBouTubeBoundary tubeBoundary;
    public bool useVirtualMarkersWhenSceneMarkersMissing = true;
    public float[] virtualCheckpointProgress = { 0.18f, 0.36f, 0.54f, 0.72f };
    [Range(0.8f, 1f)] public float virtualFinishProgress = 0.995f;
    [Min(0.1f)] public float virtualFinishRadius = 1.25f;

    [Header("Debug")]
    [SerializeField] bool isLogging;
    [SerializeField] string sessionId;
    [SerializeField] string activeParticipantId;
    [SerializeField] string activeMode;
    [SerializeField] string activeConditionLabel;
    [SerializeField] string activeOutputDirectory;
    [SerializeField] int sampleCount;
    [SerializeField] int collisionCount;
    [SerializeField] int nearMissCount;
    [SerializeField] int rigBlockCount;
    [SerializeField] int remoteBlockCount;
    [SerializeField] int checkpointCount;
    [SerializeField] int trackingLossCount;
    [SerializeField] bool finishReached;
    [SerializeField] bool completedDataSaved;
    [SerializeField] string lastSaveError;
    [SerializeField] float pathLength;
    [SerializeField] float minObstacleDistance = float.PositiveInfinity;
    [SerializeField] float maxRouteProgressNormalized;
    [SerializeField] float maxRouteDistance;
    [SerializeField] float routeLength;

    readonly List<ObstacleInfo> obstacles = new List<ObstacleInfo>();
    readonly List<MarkerInfo> checkpoints = new List<MarkerInfo>();
    readonly List<MarkerInfo> finishes = new List<MarkerInfo>();
    readonly Dictionary<int, EncounterState> activeEncounters = new Dictionary<int, EncounterState>();
    readonly List<int> encountersToEnd = new List<int>();
    readonly HashSet<int> reachedCheckpoints = new HashSet<int>();
    readonly HashSet<int> reachedFinishes = new HashSet<int>();
    readonly List<string> row = new List<string>(128);

    StringWriter timeseriesWriter;
    StringWriter eventsWriter;
    StringWriter summaryWriter;
    StringWriter encountersWriter;

    DateTime sessionStartDateTime;
    float trialStartTime;
    float nextSampleTime;
    Vector3 trialStartPosition;
    Vector3 lastRemotePosition;
    float lastRemoteYawDeg;
    float lastRemoteSampleTime;
    Vector3 lastBodyInput;
    float lastBodyInputTime;
    bool hasLastBodyInput;
    bool hasLastRemotePose;
    bool lastRigBlocked;
    bool lastRemoteBlocked;
    bool lastStateGhostBlocked;
    bool lastBodyAnchorAvailable;
    bool hasBodyAnchorAvailabilitySample;
    string lastModeString;
    int encounterSequence;
    float speedSum;
    float speedMax;
    float nearestDistanceSum;
    int nearestDistanceSamples;
    float correctionTtcSum;
    int correctionCount;
    Vector3 neutralHeadLocalPosition;
    float neutralPitchDeg;
    float neutralYawDeg;
    bool[] reachedVirtualCheckpoints = new bool[0];
    string activeRouteId;
    string activeRouteOrder;
    string activeConditionOrder;
    string activeConfigId;
    int activeTrialWithinCondition;

    public bool IsLogging => isLogging;
    public bool FinishReached => finishReached;
    public bool CompletedDataSaved => completedDataSaved;
    public string LastSaveError => lastSaveError;
    public int TrackingLossCount => trackingLossCount;
    public float MaxRouteProgressNormalized => maxRouteProgressNormalized;

    public void SetTrialMetadata(
        string participant,
        int trial,
        int withinCondition,
        string route,
        string scheduledRouteOrder,
        string order,
        string configId = null)
    {
        participantId = participant;
        trialNumber = Mathf.Max(1, trial);
        trialWithinCondition = Mathf.Max(1, withinCondition);
        routeId = route;
        routeOrder = scheduledRouteOrder;
        conditionOrder = order;
        experimentConfigId = configId;
    }

    public bool TryValidateSetup(out string message)
    {
        ResolveReferences();
        if (locomotion == null)
        {
            message = "PredictiveGhostAvatarLocomotion was not found.";
            return false;
        }
        if (rigRoot == null || head == null || probe == null)
        {
            message = "Rig, head, or collision probe reference is missing.";
            return false;
        }
        if (tubeBoundary == null || tubeBoundary.centerline == null || tubeBoundary.centerline.Length < 2)
        {
            message = "A configured IrairaBouTubeBoundary was not found.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(participantId))
        {
            message = "Participant ID is empty.";
            return false;
        }

        message = "Ready";
        return true;
    }

    public void RecordSystemEvent(string eventType, string note)
    {
        if (!isLogging)
        {
            return;
        }

        WriteEvent(eventType, string.Empty, string.Empty, GetRemoteProbePosition(), float.NaN, float.NaN, note);
    }

    void OnValidate()
    {
        trialNumber = Mathf.Max(1, trialNumber);
        trialWithinCondition = Mathf.Max(1, trialWithinCondition);
        encounterEndDistance = Mathf.Max(encounterStartDistance, encounterEndDistance);
        correctionSustainSeconds = Mathf.Max(0.03f, correctionSustainSeconds);
        correctionRefractorySeconds = Mathf.Max(0.05f, correctionRefractorySeconds);
        virtualFinishRadius = Mathf.Max(0.1f, virtualFinishRadius);
        virtualFinishProgress = Mathf.Clamp(virtualFinishProgress, 0.8f, 1f);
    }

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        if (startOnEnable && Application.isPlaying)
        {
            StartLogging();
        }
    }

    void Update()
    {
        if (useKeyboardControls)
        {
            if (Input.GetKeyDown(startLoggingKey))
            {
                StartLogging();
            }

            if (Input.GetKeyDown(stopLoggingKey))
            {
                StopLogging(false);
            }

            if (isLogging && Input.GetKeyDown(manualMarkerKey))
            {
                WriteEvent("manual_marker", string.Empty, string.Empty, GetRemoteProbePosition(), float.NaN, float.NaN, "Manual marker key pressed.");
            }
        }

        if (!isLogging)
        {
            return;
        }

        if (Time.time + 1e-5f >= nextSampleTime)
        {
            SampleAndWrite();
            float interval = 1f / Mathf.Max(1f, sampleRateHz);
            nextSampleTime = Mathf.Max(nextSampleTime + interval, Time.time + interval);
        }

    }

    void OnDisable()
    {
        if (stopOnDisable)
        {
            StopLogging(false);
        }
    }

    void OnApplicationQuit()
    {
        StopLogging(false);
    }

    [ContextMenu("Start Objective Logging")]
    public void StartLogging()
    {
        if (isLogging)
        {
            return;
        }

        ResolveReferences();
        if (!TryValidateSetup(out string validationMessage))
        {
            Debug.LogError($"[PredictiveFlyObjectiveLogger] Cannot start logging: {validationMessage}", this);
            return;
        }
        RefreshSceneTargets();
        CaptureNeutralHeadPose();
        ResetTrialMetrics();

        sessionStartDateTime = DateTime.Now;
        string timestamp = sessionStartDateTime.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        activeParticipantId = string.IsNullOrWhiteSpace(participantId) ? "NA" : participantId.Trim();
        activeMode = GetModeString();
        activeConditionLabel = ResolveConditionLabel(activeMode);
        activeRouteId = string.IsNullOrWhiteSpace(routeId) ? "Route_NA" : routeId.Trim();
        activeRouteOrder = string.IsNullOrWhiteSpace(routeOrder) ? "NA" : routeOrder.Trim();
        activeConditionOrder = string.IsNullOrWhiteSpace(conditionOrder) ? "NA" : conditionOrder.Trim();
        activeTrialWithinCondition = Mathf.Max(1, trialWithinCondition);
        activeConfigId = ResolveExperimentConfigId();
        sessionId = $"{Sanitize(activeParticipantId)}_{Sanitize(activeMode)}_{timestamp}_T{trialNumber:00}";
        activeOutputDirectory = ResolveOutputDirectory();

        timeseriesWriter = CreateBufferWriter(256 * 1024);
        eventsWriter = CreateBufferWriter(16 * 1024);
        summaryWriter = CreateBufferWriter(4 * 1024);
        encountersWriter = CreateBufferWriter(32 * 1024);

        WriteTimeseriesHeader();
        WriteEventsHeader();
        WriteSummaryHeader();
        WriteEncounterHeader();

        trialStartTime = Time.time;
        nextSampleTime = Time.time;
        trialStartPosition = GetRemoteProbePosition();
        lastRemotePosition = trialStartPosition;
        lastRemoteYawDeg = GetRemoteYawDeg();
        lastRemoteSampleTime = Time.time;
        lastModeString = activeMode;
        isLogging = true;

        WriteEvent("trial_start", string.Empty, string.Empty, GetRemoteProbePosition(), float.NaN, float.NaN, "Objective logging started.");
    }

    [ContextMenu("Stop Objective Logging")]
    public void StopLoggingFromInspector()
    {
        StopLogging(false);
    }

    public void StopLogging(bool completed)
    {
        if (!isLogging)
        {
            return;
        }

        bool courseCompleted = completed && finishReached;
        WriteEvent(courseCompleted ? "trial_complete" : "trial_stop", string.Empty, string.Empty, GetRemoteProbePosition(), float.NaN, float.NaN, "Objective logging stopped.");
        EndAllActiveEncounters(false);
        if (courseCompleted)
        {
            WriteTrialSummary(true);
            completedDataSaved = SaveCompletedTrialData();
        }
        else
        {
            Debug.LogWarning("[PredictiveFlyObjectiveLogger] Trial did not reach the course finish. Buffered objective data was discarded.", this);
        }
        DisposeWriters();
        isLogging = false;
    }

    void SampleAndWrite()
    {
        ResolveReferences();

        string mode = GetModeString();
        if (mode != lastModeString)
        {
            WriteEvent("mode_changed", mode, string.Empty, GetRemoteProbePosition(), float.NaN, float.NaN, $"Mode changed from {lastModeString}.");
            lastModeString = mode;
            if (stopOnModeChange)
            {
                StopLogging(false);
                return;
            }
        }

        float now = Time.time;
        float elapsed = now - trialStartTime;
        Vector3 remoteRootPosition = GetRemoteRootPosition();
        Vector3 remotePosition = GetRemoteProbePosition();
        float remoteYawDeg = GetRemoteYawDeg();
        float dt = hasLastRemotePose ? Mathf.Max(1e-4f, now - lastRemoteSampleTime) : Mathf.Max(1e-4f, Time.deltaTime);
        Vector3 remoteVelocity = hasLastRemotePose ? (remotePosition - lastRemotePosition) / dt : Vector3.zero;
        float yawRateDeg = hasLastRemotePose ? Mathf.DeltaAngle(lastRemoteYawDeg, remoteYawDeg) / dt : 0f;
        float speed = remoteVelocity.magnitude;

        if (hasLastRemotePose)
        {
            pathLength += Vector3.Distance(lastRemotePosition, remotePosition);
        }

        speedSum += speed;
        speedMax = Mathf.Max(speedMax, speed);

        Vector3 bodyInput = GetBodyInputLocal(out bool bodyAnchorAvailable, out float headPitchDeg, out float headYawDeg);
        float bodyInputMagnitude = new Vector2(bodyInput.x, bodyInput.z).magnitude;
        float inputChangeRate = ComputeInputChangeRate(bodyInput, now);
        UpdateTrackingEvents(bodyAnchorAvailable, remotePosition);
        UpdateRouteProgressAndVirtualMarkers(remotePosition);

        ObstacleSample nearest = SampleNearestObstacle(remotePosition, remoteVelocity);
        if (nearest.valid)
        {
            minObstacleDistance = Mathf.Min(minObstacleDistance, nearest.distance);
            nearestDistanceSum += nearest.distance;
            nearestDistanceSamples++;
            UpdateObstacleEncounters(remotePosition, bodyInputMagnitude, inputChangeRate);
        }
        else
        {
            EndFarEncounters(null);
        }

        UpdateBlockingEvents(remotePosition);
        UpdateMarkerEvents(remotePosition);
        WriteTimeseriesRow(
            now,
            elapsed,
            mode,
            bodyAnchorAvailable,
            bodyInput,
            bodyInputMagnitude,
            inputChangeRate,
            headPitchDeg,
            headYawDeg,
            remoteRootPosition,
            remotePosition,
            remoteYawDeg,
            remoteVelocity,
            speed,
            yawRateDeg,
            nearest);

        sampleCount++;
        hasLastRemotePose = true;
        lastRemotePosition = remotePosition;
        lastRemoteYawDeg = remoteYawDeg;
        lastRemoteSampleTime = now;

        if (finishReached && stopOnFinish)
        {
            StopLogging(true);
        }
    }

    void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (locomotion == null)
        {
            locomotion = GetComponent<PredictiveGhostAvatarLocomotion>();
        }

        if (locomotion == null)
        {
            locomotion = GetComponentInParent<PredictiveGhostAvatarLocomotion>();
        }

        if (locomotion == null)
        {
            locomotion = FindFirstObjectByType<PredictiveGhostAvatarLocomotion>();
        }

        if (rigRoot == null || (rigRoot == transform && locomotion != null && locomotion.targetRig != transform))
        {
            rigRoot = locomotion != null && locomotion.targetRig != null
                ? locomotion.targetRig
                : transform;
        }

        if (head == null)
        {
            head = locomotion != null ? locomotion.head : null;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        if (probe == null)
        {
            probe = locomotion != null && locomotion.collisionProbe != null
                ? locomotion.collisionProbe
                : head != null ? head : rigRoot;
        }

        if (droneBodyAvatar == null && locomotion != null)
        {
            droneBodyAvatar = locomotion.droneBodyAvatar;
        }

        if (stateGhostAvatar == null && locomotion != null)
        {
            stateGhostAvatar = locomotion.stateGhostAvatar;
        }


        if (tubeBoundary == null)
        {
            tubeBoundary = locomotion != null && locomotion.tubeBoundary != null
                ? locomotion.tubeBoundary
                : FindFirstObjectByType<IrairaBouTubeBoundary>();
        }
    }

    void RefreshSceneTargets()
    {
        obstacles.Clear();
        checkpoints.Clear();
        finishes.Clear();

        IrairaBouHazard[] hazardObjects = FindObjectsByType<IrairaBouHazard>(FindObjectsSortMode.None);
        for (int i = 0; i < hazardObjects.Length; i++)
        {
            if (hazardObjects[i].GetComponentInParent<IrairaBouTubeBoundary>() != null)
            {
                continue;
            }

            Collider[] colliders = hazardObjects[i].GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
            {
                continue;
            }

            obstacles.Add(new ObstacleInfo(
                hazardObjects[i].GetInstanceID(),
                SanitizeId(hazardObjects[i].name),
                string.IsNullOrEmpty(hazardObjects[i].hazardLabel) ? hazardObjects[i].name : hazardObjects[i].hazardLabel,
                colliders));
        }

        IrairaBouTubeBoundary[] tubeBoundaries = FindObjectsByType<IrairaBouTubeBoundary>(FindObjectsSortMode.None);
        for (int i = 0; i < tubeBoundaries.Length; i++)
        {
            obstacles.Add(new ObstacleInfo(
                tubeBoundaries[i].GetInstanceID(),
                SanitizeId(tubeBoundaries[i].name),
                string.IsNullOrEmpty(tubeBoundaries[i].hazardLabel) ? "Tube wall" : tubeBoundaries[i].hazardLabel,
                tubeBoundaries[i]));
        }

        IrairaBouCheckpoint[] checkpointObjects = FindObjectsByType<IrairaBouCheckpoint>(FindObjectsSortMode.None);
        for (int i = 0; i < checkpointObjects.Length; i++)
        {
            Collider[] colliders = checkpointObjects[i].GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                checkpoints.Add(new MarkerInfo(
                    checkpointObjects[i].GetInstanceID(),
                    $"checkpoint_{checkpointObjects[i].index:00}_{SanitizeId(checkpointObjects[i].name)}",
                    "Checkpoint",
                    colliders));
            }
        }

        IrairaBouFinish[] finishObjects = FindObjectsByType<IrairaBouFinish>(FindObjectsSortMode.None);
        for (int i = 0; i < finishObjects.Length; i++)
        {
            Collider[] colliders = finishObjects[i].GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                finishes.Add(new MarkerInfo(
                    finishObjects[i].GetInstanceID(),
                    SanitizeId(finishObjects[i].name),
                    "Finish",
                    colliders));
            }
        }

        routeLength = tubeBoundary != null ? tubeBoundary.TotalCenterlineLength : 0f;
        int virtualCount = virtualCheckpointProgress != null ? virtualCheckpointProgress.Length : 0;
        reachedVirtualCheckpoints = new bool[virtualCount];
    }

    void CaptureNeutralHeadPose()
    {
        Transform root = rigRoot != null ? rigRoot : transform;
        if (head == null || root == null)
        {
            neutralHeadLocalPosition = Vector3.zero;
            neutralPitchDeg = 0f;
            neutralYawDeg = 0f;
            return;
        }

        neutralHeadLocalPosition = root.InverseTransformPoint(head.position);
        Vector3 headForwardLocal = root.InverseTransformDirection(head.forward).normalized;
        ComputePitchYaw(headForwardLocal, out neutralPitchDeg, out neutralYawDeg);
    }

    void ResetTrialMetrics()
    {
        sampleCount = 0;
        collisionCount = 0;
        nearMissCount = 0;
        rigBlockCount = 0;
        remoteBlockCount = 0;
        checkpointCount = 0;
        trackingLossCount = 0;
        finishReached = false;
        completedDataSaved = false;
        lastSaveError = string.Empty;
        pathLength = 0f;
        minObstacleDistance = float.PositiveInfinity;
        maxRouteProgressNormalized = 0f;
        maxRouteDistance = 0f;
        routeLength = tubeBoundary != null ? tubeBoundary.TotalCenterlineLength : 0f;
        speedSum = 0f;
        speedMax = 0f;
        nearestDistanceSum = 0f;
        nearestDistanceSamples = 0;
        correctionTtcSum = 0f;
        correctionCount = 0;
        encounterSequence = 0;
        hasLastBodyInput = false;
        hasLastRemotePose = false;
        lastRigBlocked = false;
        lastRemoteBlocked = false;
        lastStateGhostBlocked = false;
        hasBodyAnchorAvailabilitySample = false;
        activeEncounters.Clear();
        reachedCheckpoints.Clear();
        reachedFinishes.Clear();
        if (reachedVirtualCheckpoints == null
            || reachedVirtualCheckpoints.Length != (virtualCheckpointProgress != null ? virtualCheckpointProgress.Length : 0))
        {
            reachedVirtualCheckpoints = new bool[virtualCheckpointProgress != null ? virtualCheckpointProgress.Length : 0];
        }
        else
        {
            Array.Clear(reachedVirtualCheckpoints, 0, reachedVirtualCheckpoints.Length);
        }
    }

    static StringWriter CreateBufferWriter(int initialCapacity)
    {
        return new StringWriter(new StringBuilder(initialCapacity), CultureInfo.InvariantCulture);
    }

    void WriteTimeseriesHeader()
    {
        WriteRow(timeseriesWriter,
            "session_id", "participant_id", "trial_number", "trial_within_condition", "route_id", "route_order", "condition_order", "config_id", "condition_label", "visualization_mode",
            "absolute_time", "time_s", "elapsed_s", "frame", "sample_index",
            "frame_dt_s", "instant_fps", "configured_delay_ms", "active_delay_ms", "delay_buffer_ready", "prediction_method", "locomotion_input_enabled",
            "body_anchor_available", "body_input_x", "body_input_y", "body_input_z", "body_input_magnitude", "body_input_change_rate",
            "head_px", "head_py", "head_pz", "head_qx", "head_qy", "head_qz", "head_qw", "head_pitch_deg", "head_yaw_deg",
            "remote_root_px", "remote_root_py", "remote_root_pz", "remote_root_yaw_deg",
            "remote_probe_px", "remote_probe_py", "remote_probe_pz", "remote_yaw_deg", "remote_vx", "remote_vy", "remote_vz", "remote_speed", "remote_yaw_rate_deg_s",
            "route_progress_normalized", "route_distance_m", "route_length_m",
            "delayed_body_px", "delayed_body_py", "delayed_body_pz", "delayed_body_qx", "delayed_body_qy", "delayed_body_qz", "delayed_body_qw",
            "state_ghost_role", "state_ghost_px", "state_ghost_py", "state_ghost_pz", "state_ghost_qx", "state_ghost_qy", "state_ghost_qz", "state_ghost_qw",
            "predictive_window_translation_s", "predictive_window_yaw_s", "prediction_confidence_translation", "prediction_confidence_yaw",
            "nearest_obstacle_id", "nearest_obstacle_label", "nearest_obstacle_distance_m", "nearest_obstacle_ttc_s", "nearest_obstacle_closing_speed_mps",
            "remote_blocked", "remote_blocked_by", "rig_blocked", "rig_blocked_by", "state_ghost_blocked", "state_ghost_blocked_by");
    }

    void WriteEventsHeader()
    {
        WriteRow(eventsWriter,
            "session_id", "participant_id", "trial_number", "trial_within_condition", "route_id", "route_order", "condition_order", "config_id", "condition_label", "visualization_mode",
            "absolute_time", "time_s", "elapsed_s", "event_type", "object_id", "object_label",
            "px", "py", "pz", "distance_m", "ttc_s", "note");
    }

    void WriteSummaryHeader()
    {
        WriteRow(summaryWriter,
            "session_id", "participant_id", "trial_number", "trial_within_condition", "route_id", "route_order", "condition_order", "config_id", "condition_label", "start_mode", "end_mode",
            "start_timestamp", "end_timestamp", "duration_s", "completed", "finish_reached",
            "sample_count", "collision_count", "near_miss_count", "remote_block_count", "rig_block_count", "checkpoint_count", "tracking_loss_count",
            "path_length_m", "reference_route_distance_m", "straight_distance_m", "path_efficiency", "max_route_progress_normalized", "route_length_m",
            "min_obstacle_distance_m", "mean_nearest_obstacle_distance_m",
            "mean_speed_mps", "max_speed_mps", "avoidance_correction_count", "mean_ttc_at_correction_s",
            "configured_delay_ms", "active_delay_ms", "prediction_method");
    }

    void WriteEncounterHeader()
    {
        WriteRow(encountersWriter,
            "session_id", "participant_id", "trial_number", "trial_within_condition", "route_id", "route_order", "condition_order", "config_id", "condition_label", "visualization_mode",
            "encounter_id", "object_id", "object_label", "start_time_s", "end_time_s", "duration_s",
            "min_distance_m", "min_ttc_s", "collision", "near_miss", "passed",
            "avoidance_onset_time_s", "ttc_at_correction_s", "correction_count");
    }

    void WriteTimeseriesRow(
        float now,
        float elapsed,
        string mode,
        bool bodyAnchorAvailable,
        Vector3 bodyInput,
        float bodyInputMagnitude,
        float inputChangeRate,
        float headPitchDeg,
        float headYawDeg,
        Vector3 remoteRootPosition,
        Vector3 remotePosition,
        float remoteYawDeg,
        Vector3 remoteVelocity,
        float speed,
        float yawRateDeg,
        ObstacleSample nearest)
    {
        row.Clear();
        AddCommonColumns(row, mode, now, elapsed);
        float frameDt = Mathf.Max(0f, Time.unscaledDeltaTime);
        row.AddF(frameDt);
        row.AddF(frameDt > 1e-6f ? 1f / frameDt : float.NaN);
        row.AddF(locomotion != null ? locomotion.inputToRigDelayMilliseconds : float.NaN);
        row.AddF(locomotion != null ? locomotion.ActiveInputToRigDelayMilliseconds : float.NaN);
        row.AddBool(locomotion != null && locomotion.InputToRigDelayBufferReady);
        row.Add(locomotion != null ? locomotion.ActivePredictionMethod.ToString() : string.Empty);
        row.AddBool(locomotion != null && locomotion.LocomotionInputEnabled);
        row.AddBool(bodyAnchorAvailable);
        row.AddF(bodyInput.x);
        row.AddF(bodyInput.y);
        row.AddF(bodyInput.z);
        row.AddF(bodyInputMagnitude);
        row.AddF(inputChangeRate);

        AddTransformColumns(row, head);
        row.AddF(headPitchDeg);
        row.AddF(headYawDeg);

        row.AddVector(remoteRootPosition);
        row.AddF(remoteYawDeg);
        row.AddVector(remotePosition);
        row.AddF(remoteYawDeg);
        row.AddVector(remoteVelocity);
        row.AddF(speed);
        row.AddF(yawRateDeg);
        row.AddF(maxRouteProgressNormalized);
        row.AddF(maxRouteDistance);
        row.AddF(routeLength);

        AddTransformColumns(row, droneBodyAvatar != null ? droneBodyAvatar : rigRoot);

        row.Add(GetStateGhostRole());
        AddTransformColumns(row, stateGhostAvatar);

        if (locomotion != null)
        {
            row.AddF(locomotion.CurrentTranslationPredictionWindow);
            row.AddF(locomotion.CurrentYawPredictionWindow);
            row.AddF(locomotion.CurrentTranslationPredictionConfidence);
            row.AddF(locomotion.CurrentYawPredictionConfidence);
        }
        else
        {
            row.AddEmpty(4);
        }

        if (nearest.valid)
        {
            row.Add(nearest.obstacle.objectId);
            row.Add(nearest.obstacle.label);
            row.AddF(nearest.distance);
            row.AddF(nearest.ttc);
            row.AddF(nearest.closingSpeed);
        }
        else
        {
            row.AddEmpty(5);
        }

        row.AddBool(locomotion != null && locomotion.IsRemoteMovementBlocked);
        row.Add(locomotion != null ? locomotion.RemoteMovementBlockedBy : string.Empty);
        row.AddBool(locomotion != null && locomotion.IsRigMovementBlocked);
        row.Add(locomotion != null ? locomotion.RigMovementBlockedBy : string.Empty);
        row.AddBool(locomotion != null && locomotion.IsStateGhostMovementBlocked);
        row.Add(locomotion != null ? locomotion.StateGhostMovementBlockedBy : string.Empty);

        ValidateRowCount("timeseries", row, TimeseriesColumnCount);
        WriteRow(timeseriesWriter, row);
    }

    void WriteEvent(string eventType, string objectId, string objectLabel, Vector3 position, float distance, float ttc, string note)
    {
        if (eventsWriter == null)
        {
            return;
        }

        float now = Time.time;
        float elapsed = isLogging ? now - trialStartTime : 0f;
        row.Clear();
        row.Add(sessionId);
        row.Add(activeParticipantId);
        row.Add(trialNumber.ToString(CultureInfo.InvariantCulture));
        row.Add(activeTrialWithinCondition.ToString(CultureInfo.InvariantCulture));
        row.Add(activeRouteId);
        row.Add(activeRouteOrder);
        row.Add(activeConditionOrder);
        row.Add(activeConfigId);
        row.Add(activeConditionLabel);
        row.Add(GetModeString());
        row.Add(DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
        row.AddF(now);
        row.AddF(elapsed);
        row.Add(eventType);
        row.Add(objectId);
        row.Add(objectLabel);
        row.AddVector(position);
        row.AddF(distance);
        row.AddF(ttc);
        row.Add(note);
        ValidateRowCount("events", row, EventsColumnCount);
        WriteRow(eventsWriter, row);
    }

    void WriteTrialSummary(bool completed)
    {
        float duration = Time.time - trialStartTime;
        float straightDistance = Vector3.Distance(trialStartPosition, GetRemoteProbePosition());
        float referenceRouteDistance = Mathf.Clamp(maxRouteDistance, 0f, Mathf.Max(0f, routeLength));
        float efficiency = pathLength > 1e-5f ? Mathf.Clamp01(referenceRouteDistance / pathLength) : float.NaN;
        float meanDistance = nearestDistanceSamples > 0 ? nearestDistanceSum / nearestDistanceSamples : float.NaN;
        float meanSpeed = sampleCount > 0 ? speedSum / sampleCount : float.NaN;
        float meanCorrectionTtc = correctionCount > 0 ? correctionTtcSum / correctionCount : float.NaN;

        row.Clear();
        row.Add(sessionId);
        row.Add(activeParticipantId);
        row.Add(trialNumber.ToString(CultureInfo.InvariantCulture));
        row.Add(activeTrialWithinCondition.ToString(CultureInfo.InvariantCulture));
        row.Add(activeRouteId);
        row.Add(activeRouteOrder);
        row.Add(activeConditionOrder);
        row.Add(activeConfigId);
        row.Add(activeConditionLabel);
        row.Add(activeMode);
        row.Add(GetModeString());
        row.Add(sessionStartDateTime.ToString("o", CultureInfo.InvariantCulture));
        row.Add(DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
        row.AddF(duration);
        row.AddBool(completed);
        row.AddBool(finishReached);
        row.Add(sampleCount.ToString(CultureInfo.InvariantCulture));
        row.Add(collisionCount.ToString(CultureInfo.InvariantCulture));
        row.Add(nearMissCount.ToString(CultureInfo.InvariantCulture));
        row.Add(remoteBlockCount.ToString(CultureInfo.InvariantCulture));
        row.Add(rigBlockCount.ToString(CultureInfo.InvariantCulture));
        row.Add(checkpointCount.ToString(CultureInfo.InvariantCulture));
        row.Add(trackingLossCount.ToString(CultureInfo.InvariantCulture));
        row.AddF(pathLength);
        row.AddF(referenceRouteDistance);
        row.AddF(straightDistance);
        row.AddF(efficiency);
        row.AddF(maxRouteProgressNormalized);
        row.AddF(routeLength);
        row.AddF(minObstacleDistance);
        row.AddF(meanDistance);
        row.AddF(meanSpeed);
        row.AddF(speedMax);
        row.Add(correctionCount.ToString(CultureInfo.InvariantCulture));
        row.AddF(meanCorrectionTtc);
        row.AddF(locomotion != null ? locomotion.inputToRigDelayMilliseconds : float.NaN);
        row.AddF(locomotion != null ? locomotion.ActiveInputToRigDelayMilliseconds : float.NaN);
        row.Add(locomotion != null ? locomotion.ActivePredictionMethod.ToString() : string.Empty);
        ValidateRowCount("summary", row, SummaryColumnCount);
        WriteRow(summaryWriter, row);
    }

    void UpdateBlockingEvents(Vector3 probePosition)
    {
        bool remoteBlocked = locomotion != null && locomotion.IsRemoteMovementBlocked;
        if (remoteBlocked != lastRemoteBlocked)
        {
            WriteEvent(
                remoteBlocked ? "remote_blocked_start" : "remote_blocked_end",
                string.Empty,
                locomotion != null ? locomotion.RemoteMovementBlockedBy : string.Empty,
                probePosition,
                0f,
                float.NaN,
                string.Empty);
            if (remoteBlocked)
            {
                remoteBlockCount++;
            }
            lastRemoteBlocked = remoteBlocked;
        }

        bool rigBlocked = locomotion != null && locomotion.IsRigMovementBlocked;
        if (rigBlocked != lastRigBlocked)
        {
            WriteEvent(
                rigBlocked ? "rig_blocked_start" : "rig_blocked_end",
                string.Empty,
                locomotion != null ? locomotion.RigMovementBlockedBy : string.Empty,
                probePosition,
                0f,
                float.NaN,
                string.Empty);
            if (rigBlocked)
            {
                rigBlockCount++;
            }
            lastRigBlocked = rigBlocked;
        }

        bool ghostBlocked = locomotion != null && locomotion.IsStateGhostMovementBlocked;
        if (ghostBlocked != lastStateGhostBlocked)
        {
            WriteEvent(
                ghostBlocked ? "state_ghost_blocked_start" : "state_ghost_blocked_end",
                string.Empty,
                locomotion != null ? locomotion.StateGhostMovementBlockedBy : string.Empty,
                probePosition,
                0f,
                float.NaN,
                string.Empty);
            lastStateGhostBlocked = ghostBlocked;
        }
    }

    void UpdateTrackingEvents(bool bodyAnchorAvailable, Vector3 remotePosition)
    {
        if (!hasBodyAnchorAvailabilitySample)
        {
            hasBodyAnchorAvailabilitySample = true;
            lastBodyAnchorAvailable = bodyAnchorAvailable;
            return;
        }

        if (bodyAnchorAvailable == lastBodyAnchorAvailable)
        {
            return;
        }

        WriteEvent(
            bodyAnchorAvailable ? "body_anchor_tracking_restored" : "body_anchor_tracking_lost",
            string.Empty,
            string.Empty,
            remotePosition,
            float.NaN,
            float.NaN,
            string.Empty);
        if (!bodyAnchorAvailable)
        {
            trackingLossCount++;
        }
        lastBodyAnchorAvailable = bodyAnchorAvailable;
    }

    void UpdateRouteProgressAndVirtualMarkers(Vector3 remotePosition)
    {
        if (tubeBoundary == null
            || !tubeBoundary.TryGetRouteProgress(
                remotePosition,
                out float normalizedProgress,
                out float distanceAlongRoute,
                out float totalRouteLength,
                out _,
                out _,
                out _))
        {
            return;
        }

        routeLength = totalRouteLength;
        maxRouteProgressNormalized = Mathf.Max(maxRouteProgressNormalized, normalizedProgress);
        maxRouteDistance = Mathf.Max(maxRouteDistance, distanceAlongRoute);

        if (!useVirtualMarkersWhenSceneMarkersMissing)
        {
            return;
        }

        if (checkpoints.Count == 0 && virtualCheckpointProgress != null)
        {
            for (int i = 0; i < virtualCheckpointProgress.Length; i++)
            {
                float threshold = Mathf.Clamp01(virtualCheckpointProgress[i]);
                if (reachedVirtualCheckpoints[i] || maxRouteProgressNormalized < threshold)
                {
                    continue;
                }

                reachedVirtualCheckpoints[i] = true;
                checkpointCount++;
                WriteEvent(
                    "checkpoint_reached",
                    $"virtual_checkpoint_{i + 1:00}",
                    "Virtual route checkpoint",
                    remotePosition,
                    float.NaN,
                    float.NaN,
                    $"route_progress={threshold:0.###}");
            }
        }

        if (finishes.Count == 0
            && !finishReached
            && maxRouteProgressNormalized >= virtualFinishProgress
            && tubeBoundary.IsNearRouteEnd(remotePosition, virtualFinishRadius))
        {
            finishReached = true;
            WriteEvent(
                "finish_reached",
                "virtual_finish",
                "Virtual route finish",
                remotePosition,
                0f,
                float.NaN,
                string.Empty);
        }
    }

    void UpdateMarkerEvents(Vector3 probePosition)
    {
        for (int i = 0; i < checkpoints.Count; i++)
        {
            MarkerInfo checkpoint = checkpoints[i];
            if (reachedCheckpoints.Contains(checkpoint.id))
            {
                continue;
            }

            if (IsTouchingMarker(checkpoint, probePosition))
            {
                reachedCheckpoints.Add(checkpoint.id);
                checkpointCount++;
                WriteEvent("checkpoint_reached", checkpoint.objectId, checkpoint.label, probePosition, 0f, float.NaN, string.Empty);
            }
        }

        for (int i = 0; i < finishes.Count; i++)
        {
            MarkerInfo finish = finishes[i];
            if (reachedFinishes.Contains(finish.id))
            {
                continue;
            }

            if (IsTouchingMarker(finish, probePosition))
            {
                reachedFinishes.Add(finish.id);
                finishReached = true;
                WriteEvent("finish_reached", finish.objectId, finish.label, probePosition, 0f, float.NaN, string.Empty);
            }
        }
    }

    void UpdateObstacleEncounters(Vector3 probePosition, float bodyInputMagnitude, float inputChangeRate)
    {
        HashSet<int> seenThisSample = null;
        for (int i = 0; i < obstacles.Count; i++)
        {
            ObstacleSample sample = SampleObstacle(obstacles[i], probePosition, GetCurrentRemoteVelocity());
            if (!sample.valid)
            {
                continue;
            }

            bool alreadyActive = activeEncounters.ContainsKey(obstacles[i].id);
            bool shouldTrack = sample.distance <= encounterStartDistance
                || (alreadyActive && sample.distance <= Mathf.Max(encounterEndDistance, encounterStartDistance));

            if (!shouldTrack)
            {
                continue;
            }

            if (seenThisSample == null)
            {
                seenThisSample = new HashSet<int>();
            }
            seenThisSample.Add(obstacles[i].id);
            UpdateEncounter(sample, probePosition, bodyInputMagnitude, inputChangeRate);
        }

        EndFarEncounters(seenThisSample);
    }

    void UpdateEncounter(ObstacleSample sample, Vector3 probePosition, float bodyInputMagnitude, float inputChangeRate)
    {
        if (!activeEncounters.TryGetValue(sample.obstacle.id, out EncounterState state))
        {
            state = new EncounterState
            {
                encounterId = ++encounterSequence,
                obstacle = sample.obstacle,
                startTime = Time.time,
                minDistance = float.PositiveInfinity,
                minTtc = float.PositiveInfinity,
                avoidanceOnsetTime = float.NaN,
                ttcAtCorrection = float.NaN,
                previousClosingSpeed = sample.closingSpeed,
                lastAcceptedCorrectionTime = float.NegativeInfinity,
                correctionCandidateTtc = float.NaN,
                startRouteProgress = maxRouteProgressNormalized,
                maxRouteProgress = maxRouteProgressNormalized
            };
            activeEncounters.Add(sample.obstacle.id, state);
            WriteEvent("obstacle_encounter_start", sample.obstacle.objectId, sample.obstacle.label, probePosition, sample.distance, sample.ttc, string.Empty);
        }

        state.lastSeenTime = Time.time;
        state.maxRouteProgress = Mathf.Max(state.maxRouteProgress, maxRouteProgressNormalized);
        state.minDistance = Mathf.Min(state.minDistance, sample.distance);
        if (IsFinite(sample.ttc))
        {
            state.minTtc = Mathf.Min(state.minTtc, sample.ttc);
        }

        bool contact = sample.contact || sample.distance <= GetEffectiveContactTolerance();
        if (contact && !state.activeContact)
        {
            state.activeContact = true;
            state.hadCollision = true;
            collisionCount++;
            WriteEvent("collision_start", sample.obstacle.objectId, sample.obstacle.label, probePosition, sample.distance, sample.ttc, string.Empty);
        }
        else if (!contact && state.activeContact)
        {
            state.activeContact = false;
            WriteEvent("collision_end", sample.obstacle.objectId, sample.obstacle.label, probePosition, sample.distance, sample.ttc, string.Empty);
        }

        bool nearMiss = !contact && sample.distance <= nearMissDistance;
        if (nearMiss && !state.activeNearMiss)
        {
            state.activeNearMiss = true;
            state.hadNearMiss = true;
            nearMissCount++;
            WriteEvent("near_miss_start", sample.obstacle.objectId, sample.obstacle.label, probePosition, sample.distance, sample.ttc, string.Empty);
        }
        else if (!nearMiss && state.activeNearMiss)
        {
            state.activeNearMiss = false;
            WriteEvent("near_miss_end", sample.obstacle.objectId, sample.obstacle.label, probePosition, sample.distance, sample.ttc, string.Empty);
        }

        float closingSpeedDrop = state.previousClosingSpeed - sample.closingSpeed;
        bool correctionByInput = inputChangeRate >= correctionInputRateThreshold
            && bodyInputMagnitude > 0.05f
            && closingSpeedDrop >= Mathf.Min(0.05f, correctionClosingSpeedDrop * 0.25f);
        bool correctionByClosingSpeed = state.previousClosingSpeed > 0.05f
            && closingSpeedDrop >= correctionClosingSpeedDrop;
        bool continuingCandidate = state.correctionCandidateActive
            && sample.closingSpeed <= state.correctionCandidateBaselineClosingSpeed
                - Mathf.Min(0.05f, correctionClosingSpeedDrop * 0.25f);
        bool correctionSignal = correctionByInput || correctionByClosingSpeed || continuingCandidate;

        if (correctionSignal && IsFinite(sample.ttc))
        {
            if (!state.correctionCandidateActive)
            {
                state.correctionCandidateActive = true;
                state.correctionCandidateStartTime = Time.time;
                state.correctionCandidateBaselineClosingSpeed = state.previousClosingSpeed;
                state.correctionCandidateTtc = sample.ttc;
            }

            bool sustained = Time.time - state.correctionCandidateStartTime >= correctionSustainSeconds;
            bool outsideRefractory = Time.time - state.lastAcceptedCorrectionTime >= correctionRefractorySeconds;
            if (sustained && outsideRefractory)
            {
                state.correctionCount++;
                correctionCount++;
                correctionTtcSum += state.correctionCandidateTtc;
                state.lastAcceptedCorrectionTime = Time.time;
                if (!IsFinite(state.avoidanceOnsetTime))
                {
                    state.avoidanceOnsetTime = state.correctionCandidateStartTime;
                    state.ttcAtCorrection = state.correctionCandidateTtc;
                    WriteEvent(
                        "avoidance_correction",
                        sample.obstacle.objectId,
                        sample.obstacle.label,
                        probePosition,
                        sample.distance,
                        state.correctionCandidateTtc,
                        $"sustained_for_s={Time.time - state.correctionCandidateStartTime:0.###}");
                }
                state.correctionCandidateActive = false;
            }
        }
        else
        {
            state.correctionCandidateActive = false;
        }

        state.previousClosingSpeed = sample.closingSpeed;
    }

    void EndFarEncounters(HashSet<int> seenThisSample)
    {
        encountersToEnd.Clear();
        foreach (KeyValuePair<int, EncounterState> pair in activeEncounters)
        {
            if (seenThisSample != null && seenThisSample.Contains(pair.Key))
            {
                continue;
            }

            encountersToEnd.Add(pair.Key);
        }

        for (int i = 0; i < encountersToEnd.Count; i++)
        {
            if (activeEncounters.TryGetValue(encountersToEnd[i], out EncounterState state))
            {
                bool passed = state.previousClosingSpeed <= 0f
                    && state.maxRouteProgress > state.startRouteProgress + 0.002f;
                WriteEncounterRow(state, passed);
                WriteEvent("obstacle_encounter_end", state.obstacle.objectId, state.obstacle.label, GetRemoteProbePosition(), state.minDistance, state.minTtc, string.Empty);
                activeEncounters.Remove(encountersToEnd[i]);
            }
        }
    }

    void EndAllActiveEncounters(bool passed)
    {
        encountersToEnd.Clear();
        foreach (KeyValuePair<int, EncounterState> pair in activeEncounters)
        {
            encountersToEnd.Add(pair.Key);
        }

        for (int i = 0; i < encountersToEnd.Count; i++)
        {
            if (activeEncounters.TryGetValue(encountersToEnd[i], out EncounterState state))
            {
                WriteEncounterRow(state, passed);
                activeEncounters.Remove(encountersToEnd[i]);
            }
        }
    }

    void WriteEncounterRow(EncounterState state, bool passed)
    {
        if (encountersWriter == null)
        {
            return;
        }

        float endTime = Time.time;
        row.Clear();
        row.Add(sessionId);
        row.Add(activeParticipantId);
        row.Add(trialNumber.ToString(CultureInfo.InvariantCulture));
        row.Add(activeTrialWithinCondition.ToString(CultureInfo.InvariantCulture));
        row.Add(activeRouteId);
        row.Add(activeRouteOrder);
        row.Add(activeConditionOrder);
        row.Add(activeConfigId);
        row.Add(activeConditionLabel);
        row.Add(GetModeString());
        row.Add(state.encounterId.ToString(CultureInfo.InvariantCulture));
        row.Add(state.obstacle.objectId);
        row.Add(state.obstacle.label);
        row.AddF(state.startTime - trialStartTime);
        row.AddF(endTime - trialStartTime);
        row.AddF(endTime - state.startTime);
        row.AddF(state.minDistance);
        row.AddF(state.minTtc);
        row.AddBool(state.hadCollision);
        row.AddBool(state.hadNearMiss);
        row.AddBool(passed && !state.hadCollision);
        row.AddF(IsFinite(state.avoidanceOnsetTime) ? state.avoidanceOnsetTime - trialStartTime : float.NaN);
        row.AddF(state.ttcAtCorrection);
        row.Add(state.correctionCount.ToString(CultureInfo.InvariantCulture));
        ValidateRowCount("obstacle_encounters", row, EncounterColumnCount);
        WriteRow(encountersWriter, row);
    }

    ObstacleSample SampleNearestObstacle(Vector3 probePosition, Vector3 velocity)
    {
        ObstacleSample nearest = ObstacleSample.Invalid;
        for (int i = 0; i < obstacles.Count; i++)
        {
            ObstacleSample sample = SampleObstacle(obstacles[i], probePosition, velocity);
            if (!sample.valid)
            {
                continue;
            }

            if (!nearest.valid || sample.distance < nearest.distance)
            {
                nearest = sample;
            }
        }

        return nearest;
    }

    ObstacleSample SampleObstacle(ObstacleInfo obstacle, Vector3 probePosition, Vector3 velocity)
    {
        if (obstacle.tubeBoundary != null)
        {
            if (!obstacle.tubeBoundary.TryGetWallClearance(
                probePosition,
                probeRadius,
                out Vector3 nearestPoint,
                out _,
                out _,
                out float clearance))
            {
                return ObstacleSample.Invalid;
            }

            Vector3 radialDirection = probePosition - nearestPoint;
            if (radialDirection.sqrMagnitude <= 1e-8f)
            {
                radialDirection = Vector3.up;
            }
            radialDirection.Normalize();

            float distance = Mathf.Max(0f, clearance);
            float closingSpeed = Vector3.Dot(velocity, radialDirection);
            return new ObstacleSample
            {
                valid = true,
                obstacle = obstacle,
                distance = distance,
                ttc = closingSpeed > 1e-4f ? distance / closingSpeed : float.NaN,
                closingSpeed = closingSpeed,
                contact = clearance <= GetEffectiveContactTolerance()
            };
        }

        if (obstacle.colliders == null || obstacle.colliders.Length == 0)
        {
            return ObstacleSample.Invalid;
        }

        float bestDistance = float.PositiveInfinity;
        Vector3 bestClosestPoint = probePosition;
        bool found = false;

        for (int i = 0; i < obstacle.colliders.Length; i++)
        {
            Collider col = obstacle.colliders[i];
            if (col == null || !col.enabled)
            {
                continue;
            }

            Vector3 closest = col.ClosestPoint(probePosition);
            float surfaceDistance = Mathf.Max(0f, Vector3.Distance(probePosition, closest) - probeRadius);
            if (surfaceDistance < bestDistance)
            {
                bestDistance = surfaceDistance;
                bestClosestPoint = closest;
                found = true;
            }
        }

        if (!found)
        {
            return ObstacleSample.Invalid;
        }

        Vector3 direction = bestClosestPoint - probePosition;
        if (direction.sqrMagnitude <= 1e-8f && obstacle.transform != null)
        {
            direction = obstacle.transform.position - probePosition;
        }
        if (direction.sqrMagnitude <= 1e-8f)
        {
            direction = velocity.sqrMagnitude > 1e-8f ? velocity.normalized : Vector3.forward;
        }
        direction.Normalize();

        float closingSpeedToObstacle = Vector3.Dot(velocity, direction);
        return new ObstacleSample
        {
            valid = true,
            obstacle = obstacle,
            distance = bestDistance,
            ttc = closingSpeedToObstacle > 1e-4f ? bestDistance / closingSpeedToObstacle : float.NaN,
            closingSpeed = closingSpeedToObstacle,
            contact = bestDistance <= GetEffectiveContactTolerance()
        };
    }

    bool IsTouchingMarker(MarkerInfo marker, Vector3 probePosition)
    {
        for (int i = 0; i < marker.colliders.Length; i++)
        {
            Collider col = marker.colliders[i];
            if (col == null || !col.enabled)
            {
                continue;
            }

            float distance = Mathf.Max(0f, Vector3.Distance(probePosition, col.ClosestPoint(probePosition)) - probeRadius);
            if (distance <= contactTolerance)
            {
                return true;
            }
        }

        return false;
    }

    Vector3 GetProbePosition()
    {
        if (probe != null)
        {
            return probe.position;
        }

        if (head != null)
        {
            return head.position;
        }

        return rigRoot != null ? rigRoot.position : transform.position;
    }

    Vector3 GetRemoteRootPosition()
    {
        if (locomotion != null)
        {
            return locomotion.RealTimeDronePosition;
        }

        return rigRoot != null ? rigRoot.position : transform.position;
    }

    Vector3 GetRemoteProbePosition()
    {
        Vector3 rootPosition = GetRemoteRootPosition();
        if (rigRoot == null || probe == null)
        {
            return rootPosition;
        }

        Vector3 probeOffsetLocal = rigRoot.InverseTransformPoint(probe.position);
        Quaternion remoteYaw = Quaternion.Euler(0f, GetRemoteYawDeg(), 0f);
        return rootPosition + remoteYaw * probeOffsetLocal;
    }

    float GetRemoteYawDeg()
    {
        if (locomotion != null)
        {
            return locomotion.RealTimeDroneYawDeg;
        }

        Transform root = rigRoot != null ? rigRoot : transform;
        return root.eulerAngles.y;
    }

    Vector3 GetCurrentRemoteVelocity()
    {
        if (!hasLastRemotePose)
        {
            return Vector3.zero;
        }

        float dt = Mathf.Max(1e-4f, Time.time - lastRemoteSampleTime);
        return (GetRemoteProbePosition() - lastRemotePosition) / dt;
    }

    Vector3 GetBodyInputLocal(out bool bodyAnchorAvailable, out float headPitchDeg, out float headYawDeg)
    {
        bodyAnchorAvailable = false;
        headPitchDeg = float.NaN;
        headYawDeg = float.NaN;

        if (locomotion != null)
        {
            Vector3 offset = locomotion.CurrentCenterOffsetLocal;
            headPitchDeg = locomotion.CurrentHeadPitchDeg;
            if (head != null && rigRoot != null)
            {
                Vector3 headForwardLocal = rigRoot.InverseTransformDirection(head.forward).normalized;
                ComputePitchYaw(headForwardLocal, out _, out headYawDeg);
            }
            bodyAnchorAvailable = locomotion.bodyAnchorProvider != null
                && rigRoot != null
                && locomotion.bodyAnchorProvider.TryGetAnchorLocalPose(rigRoot, out _);
            return offset;
        }

        if (head == null || rigRoot == null)
        {
            return Vector3.zero;
        }

        Vector3 headLocal = rigRoot.InverseTransformPoint(head.position);
        Vector3 offsetLocal = headLocal - neutralHeadLocalPosition;
        Vector3 forwardLocal = rigRoot.InverseTransformDirection(head.forward).normalized;
        ComputePitchYaw(forwardLocal, out float pitch, out float yaw);
        headPitchDeg = Mathf.DeltaAngle(neutralPitchDeg, pitch);
        headYawDeg = Mathf.DeltaAngle(neutralYawDeg, yaw);
        return offsetLocal;
    }

    float ComputeInputChangeRate(Vector3 bodyInput, float now)
    {
        if (!hasLastBodyInput)
        {
            lastBodyInput = bodyInput;
            lastBodyInputTime = now;
            hasLastBodyInput = true;
            return 0f;
        }

        float dt = Mathf.Max(1e-4f, now - lastBodyInputTime);
        float rate = (bodyInput - lastBodyInput).magnitude / dt;
        lastBodyInput = bodyInput;
        lastBodyInputTime = now;
        return rate;
    }

    string GetModeString()
    {
        return locomotion != null ? locomotion.visualizationMode.ToString() : "NoLocomotion";
    }

    string ResolveConditionLabel(string mode)
    {
        if (!string.IsNullOrWhiteSpace(conditionLabelOverride))
        {
            return conditionLabelOverride.Trim();
        }

        if (locomotion == null)
        {
            return mode;
        }

        switch (locomotion.visualizationMode)
        {
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.RealTimeDroneBody:
                return "NoDelay";
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedDroneBody:
                return "DelayedFeedback";
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithRealTimeGhost:
                return "Delayed_CurrentAvatar";
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost:
                return "Delayed_PredictiveAvatar";
            default:
                return mode;
        }
    }

    string GetStateGhostRole()
    {
        if (locomotion == null)
        {
            return string.Empty;
        }

        switch (locomotion.visualizationMode)
        {
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithRealTimeGhost:
                return "current_avatar";
            case PredictiveGhostAvatarLocomotion.GhostVisualizationMode.DelayedWithPredictiveGhost:
                return "predictive_avatar";
            default:
                return string.Empty;
        }
    }

    string ResolveExperimentConfigId()
    {
        if (!string.IsNullOrWhiteSpace(experimentConfigId))
        {
            return experimentConfigId.Trim();
        }

        if (locomotion == null)
        {
            return $"{activeRouteId}_NoLocomotion";
        }

        return Sanitize(
            $"{activeRouteId}_{locomotion.visualizationMode}" +
            $"_D{locomotion.inputToRigDelayMilliseconds}ms" +
            $"_{locomotion.predictionMethod}" +
            $"_H{locomotion.horizontalSpeed:0.###}" +
            $"_V{locomotion.verticalSpeed:0.###}" +
            $"_Y{locomotion.yawSpeed:0.###}");
    }

    string ResolveOutputDirectory()
    {
        string directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? "Data/PredictiveFlyObjective"
            : outputDirectory.Trim();

        if (Path.IsPathRooted(directory))
        {
            return Path.GetFullPath(directory);
        }

#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", directory));
#else
        return Path.Combine(Application.persistentDataPath, directory);
#endif
    }

    bool SaveCompletedTrialData()
    {
        string[] suffixes =
        {
            "objective_timeseries",
            "objective_events",
            "objective_trial_summary",
            "objective_obstacle_encounters"
        };
        string[] contents =
        {
            timeseriesWriter?.ToString() ?? string.Empty,
            eventsWriter?.ToString() ?? string.Empty,
            summaryWriter?.ToString() ?? string.Empty,
            encountersWriter?.ToString() ?? string.Empty
        };
        string[] finalPaths = new string[suffixes.Length];
        string[] stagingPaths = new string[suffixes.Length];
        int promotedFileCount = 0;

        try
        {
            Directory.CreateDirectory(activeOutputDirectory);
            for (int i = 0; i < suffixes.Length; i++)
            {
                finalPaths[i] = Path.Combine(activeOutputDirectory, $"{sessionId}_{suffixes[i]}.csv");
                stagingPaths[i] = finalPaths[i] + ".writing";
                if (File.Exists(finalPaths[i]))
                {
                    throw new IOException($"Output file already exists: {finalPaths[i]}");
                }
                File.WriteAllText(stagingPaths[i], contents[i], new UTF8Encoding(false));
            }

            for (int i = 0; i < suffixes.Length; i++)
            {
                File.Move(stagingPaths[i], finalPaths[i]);
                promotedFileCount++;
            }

            lastSaveError = string.Empty;
            Debug.Log($"[PredictiveFlyObjectiveLogger] Completed trial saved to {activeOutputDirectory}", this);
            return true;
        }
        catch (Exception exception)
        {
            lastSaveError = exception.Message;
            for (int i = 0; i < stagingPaths.Length; i++)
            {
                TryDeleteFile(stagingPaths[i]);
            }
            for (int i = 0; i < promotedFileCount; i++)
            {
                TryDeleteFile(finalPaths[i]);
            }
            Debug.LogError($"[PredictiveFlyObjectiveLogger] Course completed, but objective data could not be saved: {exception}", this);
            return false;
        }
    }

    static void TryDeleteFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }

    void DisposeWriters()
    {
        timeseriesWriter?.Dispose();
        eventsWriter?.Dispose();
        summaryWriter?.Dispose();
        encountersWriter?.Dispose();
        timeseriesWriter = null;
        eventsWriter = null;
        summaryWriter = null;
        encountersWriter = null;
    }

    void AddCommonColumns(List<string> values, string mode, float now, float elapsed)
    {
        values.Add(sessionId);
        values.Add(activeParticipantId);
        values.Add(trialNumber.ToString(CultureInfo.InvariantCulture));
        values.Add(activeTrialWithinCondition.ToString(CultureInfo.InvariantCulture));
        values.Add(activeRouteId);
        values.Add(activeRouteOrder);
        values.Add(activeConditionOrder);
        values.Add(activeConfigId);
        values.Add(activeConditionLabel);
        values.Add(mode);
        values.Add(DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
        values.AddF(now);
        values.AddF(elapsed);
        values.Add(Time.frameCount.ToString(CultureInfo.InvariantCulture));
        values.Add(sampleCount.ToString(CultureInfo.InvariantCulture));
    }

    static void AddTransformColumns(List<string> values, Transform target)
    {
        if (target == null)
        {
            values.AddEmpty(7);
            return;
        }

        values.AddVector(target.position);
        Quaternion q = target.rotation;
        values.AddF(q.x);
        values.AddF(q.y);
        values.AddF(q.z);
        values.AddF(q.w);
    }

    static void ComputePitchYaw(Vector3 forwardLocal, out float pitchDeg, out float yawDeg)
    {
        float planar = Mathf.Sqrt(forwardLocal.x * forwardLocal.x + forwardLocal.z * forwardLocal.z);
        pitchDeg = Mathf.Atan2(forwardLocal.y, planar) * Mathf.Rad2Deg;
        yawDeg = Mathf.Atan2(forwardLocal.x, forwardLocal.z) * Mathf.Rad2Deg;
    }

    static void WriteRow(StringWriter writer, params string[] values)
    {
        if (writer == null)
        {
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }
            writer.Write(EscapeCsv(values[i]));
        }
        writer.WriteLine();
    }

    static void WriteRow(StringWriter writer, List<string> values)
    {
        if (writer == null)
        {
            return;
        }

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }
            writer.Write(EscapeCsv(values[i]));
        }
        writer.WriteLine();
    }

    static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!mustQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NA";
        }

        char[] chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
            {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }

    static string SanitizeId(string value)
    {
        return Sanitize(string.IsNullOrEmpty(value) ? "object" : value);
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    void ValidateRowCount(string fileRole, List<string> values, int expected)
    {
        if (values.Count == expected)
        {
            return;
        }

        Debug.LogError(
            $"[PredictiveFlyObjectiveLogger] {fileRole} row has {values.Count} columns; expected {expected}. Logging was stopped to protect data integrity.",
            this);
        DisposeWriters();
        isLogging = false;
    }

    float GetEffectiveContactTolerance()
    {
        float blockingTolerance = locomotion != null && locomotion.enableSoftCollisionBlocking
            ? locomotion.collisionSkinWidth + 0.005f
            : 0f;
        return Mathf.Max(contactTolerance, blockingTolerance);
    }

    class ObstacleInfo
    {
        public readonly int id;
        public readonly string objectId;
        public readonly string label;
        public readonly Collider[] colliders;
        public readonly IrairaBouTubeBoundary tubeBoundary;
        public readonly Transform transform;

        public ObstacleInfo(int id, string objectId, string label, Collider[] colliders)
        {
            this.id = id;
            this.objectId = objectId;
            this.label = label;
            this.colliders = colliders;
            tubeBoundary = null;
            transform = colliders != null && colliders.Length > 0 && colliders[0] != null
                ? colliders[0].transform
                : null;
        }

        public ObstacleInfo(int id, string objectId, string label, IrairaBouTubeBoundary tubeBoundary)
        {
            this.id = id;
            this.objectId = objectId;
            this.label = label;
            this.tubeBoundary = tubeBoundary;
            colliders = null;
            transform = tubeBoundary != null ? tubeBoundary.transform : null;
        }
    }

    class MarkerInfo
    {
        public readonly int id;
        public readonly string objectId;
        public readonly string label;
        public readonly Collider[] colliders;

        public MarkerInfo(int id, string objectId, string label, Collider[] colliders)
        {
            this.id = id;
            this.objectId = objectId;
            this.label = label;
            this.colliders = colliders;
        }
    }

    class EncounterState
    {
        public int encounterId;
        public ObstacleInfo obstacle;
        public float startTime;
        public float lastSeenTime;
        public float minDistance;
        public float minTtc;
        public bool hadCollision;
        public bool hadNearMiss;
        public bool activeContact;
        public bool activeNearMiss;
        public float avoidanceOnsetTime;
        public float ttcAtCorrection;
        public int correctionCount;
        public float previousClosingSpeed;
        public bool correctionCandidateActive;
        public float correctionCandidateStartTime;
        public float correctionCandidateBaselineClosingSpeed;
        public float correctionCandidateTtc;
        public float lastAcceptedCorrectionTime;
        public float startRouteProgress;
        public float maxRouteProgress;
    }

    struct ObstacleSample
    {
        public bool valid;
        public ObstacleInfo obstacle;
        public float distance;
        public float ttc;
        public float closingSpeed;
        public bool contact;

        public static ObstacleSample Invalid => new ObstacleSample { valid = false };
    }
}

static class PredictiveFlyObjectiveCsvExtensions
{
    public static void AddF(this List<string> values, float value)
    {
        values.Add((float.IsNaN(value) || float.IsInfinity(value))
            ? string.Empty
            : value.ToString("0.######", CultureInfo.InvariantCulture));
    }

    public static void AddBool(this List<string> values, bool value)
    {
        values.Add(value ? "1" : "0");
    }

    public static void AddVector(this List<string> values, Vector3 value)
    {
        values.AddF(value.x);
        values.AddF(value.y);
        values.AddF(value.z);
    }

    public static void AddEmpty(this List<string> values, int count)
    {
        for (int i = 0; i < count; i++)
        {
            values.Add(string.Empty);
        }
    }
}
