using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder (120)]
[DisallowMultipleComponent]
public class AgencyPresetController : MonoBehaviour {
    public enum PresetId {
        Arcade,
        Balance,
        Cinematic
    }

    [Serializable]
    public struct BoidPreset {
        public float minSpeed;
        public float maxSpeed;
        public float perceptionRadius;
        public float avoidanceRadius;
        public float maxSteerForce;
        public float alignWeight;
        public float cohesionWeight;
        public float seperateWeight;
        public float targetWeight;
        public float avoidCollisionWeight;
        public float collisionAvoidDst;
    }

    [Serializable]
    public struct LocomotionPreset {
        public float horizontalSpeed;
        public float planarDeadZone;
        public float planarMaxOffset;
        public float planarResponseExponent;
        public float headDirectionBlend;
        public float planarSmoothing;
        public HeadOffsetLocomotion.OrientationControlMode orientationMode;
        public float verticalSpeed;
        public float yawSpeed;
    }

    [Serializable]
    public struct CentroidPreset {
        public float followSmooth;
        public bool rotateAlongMovement;
        public float headingSmooth;
        public float velocitySmooth;
        public float minHeadingSpeed;
        public float maxYawSpeedDeg;
    }

    [Serializable]
    public class AgencyPreset {
        public string label;
        public BoidPreset boids;
        public LocomotionPreset locomotion;
        public CentroidPreset centroid;
    }

    [Header ("References")]
    public BoidManager boidManager;
    public HeadOffsetLocomotion locomotion;
    public BoidCentroidTarget centroidTarget;

    [Header ("Startup")]
    public bool applyOnStart = true;
    public PresetId startPreset = PresetId.Balance;

    [Header ("Hotkeys")]
    public KeyCode arcadeKey = KeyCode.Alpha1;
    public KeyCode balanceKey = KeyCode.Alpha2;
    public KeyCode cinematicKey = KeyCode.Alpha3;

    [Header ("Presets")]
    public AgencyPreset arcade = CreateArcadePreset ();
    public AgencyPreset balance = CreateBalancePreset ();
    public AgencyPreset cinematic = CreateCinematicPreset ();

    [Header ("Debug")]
    public bool logOnApply;
    [SerializeField] PresetId currentPreset = PresetId.Balance;

    void Reset () {
        AutoAssignReferences ();
    }

    void Awake () {
        AutoAssignReferences ();
    }

    void Start () {
        if (applyOnStart) {
            ApplyPreset (startPreset);
        }
    }

    void Update () {
        if (IsKeyPressed (arcadeKey)) {
            ApplyPreset (PresetId.Arcade);
        } else if (IsKeyPressed (balanceKey)) {
            ApplyPreset (PresetId.Balance);
        } else if (IsKeyPressed (cinematicKey)) {
            ApplyPreset (PresetId.Cinematic);
        }
    }

    [ContextMenu ("Apply Arcade")]
    public void ApplyArcade () {
        ApplyPreset (PresetId.Arcade);
    }

    [ContextMenu ("Apply Balance")]
    public void ApplyBalance () {
        ApplyPreset (PresetId.Balance);
    }

    [ContextMenu ("Apply Cinematic")]
    public void ApplyCinematic () {
        ApplyPreset (PresetId.Cinematic);
    }

    public void ApplyPreset (PresetId presetId) {
        AutoAssignReferences ();
        AgencyPreset preset = GetPresetData (presetId);
        if (preset == null) {
            return;
        }

        ApplyBoidPreset (preset.boids);
        ApplyLocomotionPreset (preset.locomotion);
        ApplyCentroidPreset (preset.centroid);

        currentPreset = presetId;
        if (logOnApply) {
            Debug.Log ($"[AgencyPreset] Applied {preset.label}", this);
        }
    }

    AgencyPreset GetPresetData (PresetId presetId) {
        switch (presetId) {
            case PresetId.Arcade:
                return arcade;
            case PresetId.Balance:
                return balance;
            case PresetId.Cinematic:
                return cinematic;
            default:
                return balance;
        }
    }

    void ApplyBoidPreset (BoidPreset p) {
        if (boidManager == null) {
            return;
        }

        boidManager.minSpeed = p.minSpeed;
        boidManager.maxSpeed = p.maxSpeed;
        boidManager.perceptionRadius = p.perceptionRadius;
        boidManager.avoidanceRadius = p.avoidanceRadius;
        boidManager.maxSteerForce = p.maxSteerForce;
        boidManager.alignWeight = p.alignWeight;
        boidManager.cohesionWeight = p.cohesionWeight;
        boidManager.seperateWeight = p.seperateWeight;
        boidManager.targetWeight = p.targetWeight;
        boidManager.avoidCollisionWeight = p.avoidCollisionWeight;
        boidManager.collisionAvoidDst = p.collisionAvoidDst;
    }

    void ApplyLocomotionPreset (LocomotionPreset p) {
        if (locomotion == null) {
            return;
        }

        locomotion.horizontalSpeed = p.horizontalSpeed;
        locomotion.planarDeadZone = p.planarDeadZone;
        locomotion.planarMaxOffset = p.planarMaxOffset;
        locomotion.planarResponseExponent = p.planarResponseExponent;
        locomotion.headDirectionBlend = p.headDirectionBlend;
        locomotion.planarSmoothing = p.planarSmoothing;
        locomotion.orientationMode = p.orientationMode;
        locomotion.verticalSpeed = p.verticalSpeed;
        locomotion.yawSpeed = p.yawSpeed;
    }

    void ApplyCentroidPreset (CentroidPreset p) {
        if (centroidTarget == null) {
            return;
        }

        centroidTarget.followSmooth = p.followSmooth;
        centroidTarget.rotateAlongMovement = p.rotateAlongMovement;
        centroidTarget.headingSmooth = p.headingSmooth;
        centroidTarget.velocitySmooth = p.velocitySmooth;
        centroidTarget.minHeadingSpeed = p.minHeadingSpeed;
        centroidTarget.maxYawSpeedDeg = p.maxYawSpeedDeg;
    }

    bool IsKeyPressed (KeyCode keyCode) {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown (keyCode)) {
            return true;
        }
#endif

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) {
            if (TryMapToInputSystemKey (keyCode, out Key key)) {
                var keyControl = Keyboard.current[key];
                if (keyControl != null && keyControl.wasPressedThisFrame) {
                    return true;
                }
            }
        }
#endif

        return false;
    }

#if ENABLE_INPUT_SYSTEM
    static bool TryMapToInputSystemKey (KeyCode keyCode, out Key key) {
        switch (keyCode) {
            case KeyCode.Alpha0:
                key = Key.Digit0;
                return true;
            case KeyCode.Alpha1:
                key = Key.Digit1;
                return true;
            case KeyCode.Alpha2:
                key = Key.Digit2;
                return true;
            case KeyCode.Alpha3:
                key = Key.Digit3;
                return true;
            case KeyCode.Alpha4:
                key = Key.Digit4;
                return true;
            case KeyCode.Alpha5:
                key = Key.Digit5;
                return true;
            case KeyCode.Alpha6:
                key = Key.Digit6;
                return true;
            case KeyCode.Alpha7:
                key = Key.Digit7;
                return true;
            case KeyCode.Alpha8:
                key = Key.Digit8;
                return true;
            case KeyCode.Alpha9:
                key = Key.Digit9;
                return true;
            default:
                return Enum.TryParse (keyCode.ToString (), true, out key);
        }
    }
#endif

    void AutoAssignReferences () {
        if (boidManager == null) {
            boidManager = GetComponent<BoidManager> ();
        }
        if (boidManager == null) {
            boidManager = FindFirstObjectByType<BoidManager> ();
        }

        if (locomotion == null) {
            locomotion = FindFirstObjectByType<HeadOffsetLocomotion> ();
        }

        if (centroidTarget == null) {
            centroidTarget = FindFirstObjectByType<BoidCentroidTarget> ();
        }
    }

    static AgencyPreset CreateArcadePreset () {
        return new AgencyPreset {
            label = "Arcade",
            boids = new BoidPreset {
                minSpeed = 1.9f,
                maxSpeed = 7f,
                perceptionRadius = 2.3f,
                avoidanceRadius = 1f,
                maxSteerForce = 6f,
                alignWeight = 0.65f,
                cohesionWeight = 0.55f,
                seperateWeight = 1.3f,
                targetWeight = 5.8f,
                avoidCollisionWeight = 8.5f,
                collisionAvoidDst = 4.2f
            },
            locomotion = new LocomotionPreset {
                horizontalSpeed = 6.8f,
                planarDeadZone = 0.02f,
                planarMaxOffset = 0.14f,
                planarResponseExponent = 1.2f,
                headDirectionBlend = 0.2f,
                planarSmoothing = 8f,
                orientationMode = HeadOffsetLocomotion.OrientationControlMode.Dynamic,
                verticalSpeed = 5.5f,
                yawSpeed = 110f
            },
            centroid = new CentroidPreset {
                followSmooth = 6f,
                rotateAlongMovement = true,
                headingSmooth = 9f,
                velocitySmooth = 10f,
                minHeadingSpeed = 0.1f,
                maxYawSpeedDeg = 35f
            }
        };
    }

    static AgencyPreset CreateBalancePreset () {
        return new AgencyPreset {
            label = "Balance",
            boids = new BoidPreset {
                minSpeed = 2f,
                maxSpeed = 6.5f,
                perceptionRadius = 2.5f,
                avoidanceRadius = 1f,
                maxSteerForce = 5f,
                alignWeight = 0.85f,
                cohesionWeight = 0.8f,
                seperateWeight = 1.25f,
                targetWeight = 4.5f,
                avoidCollisionWeight = 9f,
                collisionAvoidDst = 4.5f
            },
            locomotion = new LocomotionPreset {
                horizontalSpeed = 5.5f,
                planarDeadZone = 0.02f,
                planarMaxOffset = 0.14f,
                planarResponseExponent = 1.2f,
                headDirectionBlend = 0.2f,
                planarSmoothing = 8f,
                orientationMode = HeadOffsetLocomotion.OrientationControlMode.Dynamic,
                verticalSpeed = 5f,
                yawSpeed = 100f
            },
            centroid = new CentroidPreset {
                followSmooth = 5f,
                rotateAlongMovement = true,
                headingSmooth = 8f,
                velocitySmooth = 10f,
                minHeadingSpeed = 0.1f,
                maxYawSpeedDeg = 35f
            }
        };
    }

    static AgencyPreset CreateCinematicPreset () {
        return new AgencyPreset {
            label = "Cinematic",
            boids = new BoidPreset {
                minSpeed = 2.2f,
                maxSpeed = 5f,
                perceptionRadius = 3f,
                avoidanceRadius = 1.1f,
                maxSteerForce = 3.2f,
                alignWeight = 1.2f,
                cohesionWeight = 1.1f,
                seperateWeight = 1f,
                targetWeight = 2.2f,
                avoidCollisionWeight = 12f,
                collisionAvoidDst = 6f
            },
            locomotion = new LocomotionPreset {
                horizontalSpeed = 3.2f,
                planarDeadZone = 0.02f,
                planarMaxOffset = 0.14f,
                planarResponseExponent = 1.2f,
                headDirectionBlend = 0.2f,
                planarSmoothing = 13f,
                orientationMode = HeadOffsetLocomotion.OrientationControlMode.Dynamic,
                verticalSpeed = 3f,
                yawSpeed = 70f
            },
            centroid = new CentroidPreset {
                followSmooth = 3f,
                rotateAlongMovement = true,
                headingSmooth = 5f,
                velocitySmooth = 6f,
                minHeadingSpeed = 0.08f,
                maxYawSpeedDeg = 20f
            }
        };
    }
}
