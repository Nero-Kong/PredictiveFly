# VRLocomotion Scene Relationship Map

Scope: `Assets/Scenes/VRLocomotion.unity`.

This map reflects the serialized scene state, especially `m_IsActive` on GameObjects and `m_Enabled` on MonoBehaviours. The scene is the only enabled scene in `ProjectSettings/EditorBuildSettings.asset`.

## Top-Level Scene Layout

```mermaid
flowchart TD
    Scene["VRLocomotion.unity<br/>Build Settings: enabled"]

    Scene --> Light["Directional Light"]
    Scene --> BoidManagerGo["Boid Manager<br/>GameObject active"]
    Scene --> World["Scene<br/>world container"]
    Scene --> XRRig["XRRig<br/>VR locomotion root"]
    Scene --> Exp["Exp<br/>experiment host"]
    Scene --> EventSystem["EventSystem<br/>InputSystem UI module"]
    Scene --> Pine["pine<br/>root prefab instance, inactive"]

    World --> Walls["Walls<br/>boundary cubes"]
    World --> Obstacles["Obstacles<br/>collision / target geometry"]

    BoidManagerGo --> SpawnerFish["Spawner_Fish<br/>prefab instance"]
    BoidManagerGo --> SpawnerOther["Other spawners / boid prefab instances"]
    BoidManagerGo --> BoidFish["BoidFish<br/>Boid component"]
    BoidManagerGo --> BoidShark["BoidShark<br/>Boid component + missing-script refs"]
```

## Locomotion Control Stack

```mermaid
flowchart LR
    HMD["Camera<br/>MainCamera<br/>TrackedPoseDriver enabled"]
    XRRig["XRRig<br/>Transform moved by locomotion"]

    HeadOffset["HeadOffsetLocomotion<br/>disabled"]
    GhostMode["PredictiveGhostAvatarLocomotion<br/>ENABLED<br/>default active controller"]
    FeatureExtractor["HeadIntentFeatureExtractor<br/>disabled"]
    Agent["AdaptiveFlyAgent<br/>disabled"]
    Behavior["BehaviorParameters<br/>disabled<br/>BehaviorName: AdaptiveFly"]
    GhostAvatar["PredictiveGhostAvatar<br/>VRDrone_VTOL ghost visual"]

    HMD -->|"head pose / Camera.main"| HeadOffset
    HeadOffset -->|"moves target"| XRRig

    HMD -->|"head pose"| GhostMode
    GhostMode -->|"smooth-follow moves"| XRRig
    GhostMode -->|"visualizes prediction"| GhostAvatar

    HMD -->|"calibrated intent features"| FeatureExtractor
    FeatureExtractor -->|"17-observation input source"| Agent
    Behavior -->|"4 continuous actions"| Agent
    Agent -->|"moves rigRoot"| XRRig

    classDef enabled fill:#d9f7be,stroke:#389e0d,color:#111;
    classDef disabled fill:#f5f5f5,stroke:#999,color:#555;
    class HeadOffset enabled;
    class GhostMode,FeatureExtractor,Agent,Behavior disabled;
```

Important rule: only one of these should drive `XRRig` at a time. The scene currently uses `PredictiveGhostAvatarLocomotion`; keep `HeadOffsetLocomotion` disabled while testing it. `PredictiveGhostAvatarLocomotion` exposes `useKalmanPrediction`; leave it unchecked for the legacy acceleration/jerk-limited predictor, or check it to use the Kalman velocity/acceleration predictor. The predictive ghost visual is now driven by `GhostAvatarAnimationDriver`, which instantiates `Assets/Prefabs/VRDrone_VTOL.prefab` at runtime and disables the prefab's control, camera, light, and collider components.

## XRRig Hierarchy And Components

```mermaid
flowchart TD
    XRRig["XRRig<br/>active root"]

    XRRig --> Camera["Camera<br/>MainCamera"]
    Camera --> Canvas["Canvas<br/>screen-space camera UI"]
    Canvas --> TextTMP["Text (TMP)"]

    XRRig --> RigHelper["Cube<br/>child helper / collider object"]

    XRRig --> CameraOffset["CameraOffset<br/>enabled"]
    XRRig -. disabled .-> HeadOffset["HeadOffsetLocomotion"]
    XRRig --> GhostMode["PredictiveGhostAvatarLocomotion<br/>enabled"]
    XRRig --> Rigidbody["Rigidbody<br/>isKinematic, no gravity"]
    XRRig --> Trigger["SphereCollider<br/>isTrigger"]

    XRRig -. disabled .-> FeatureExtractor["HeadIntentFeatureExtractor"]
    XRRig -. disabled .-> Agent["AdaptiveFlyAgent"]
    XRRig -. disabled .-> Behavior["BehaviorParameters"]

    Camera --> TrackedPose["TrackedPoseDriver<br/>enabled"]
    Camera --> CameraComp["Camera<br/>XR stereo output"]
    Camera --> Audio["AudioListener"]
    Camera --> CameraTrigger["SphereCollider<br/>isTrigger, radius 2"]
    Camera --> CameraRb["Rigidbody"]
```

`HeadOffsetLocomotion.head` is serialized as null in the scene, so it resolves `Camera.main` in `Awake`. The RL and predictive ghost components serialize the camera transform explicitly.

## Boids And Agency Controls

```mermaid
flowchart TD
    BoidManagerGo["Boid Manager"]

    BoidManagerGo --> BoidManager["BoidManager<br/>enabled"]
    BoidManagerGo --> Agency["AgencyPresetController<br/>enabled, applyOnStart=false"]
    BoidManagerGo -. disabled .-> ViewSwitcher["CameraViewModeSwitcher"]
    BoidManagerGo -. disabled .-> LeapHand["LeapBoidHandController"]

    BoidManager --> Compute["BoidCompute.compute"]
    BoidManager --> Settings["BoidSettings asset"]
    BoidManager --> Boids["FindObjectsByType&lt;Boid&gt; at Start"]

    Spawners["Spawner prefab instances"] -->|"instantiate boids in Awake"| Boids
    Boids -->|"flock centroid"| Centroid["BoidCentroid<br/>BoidCentroidTarget enabled"]
    Agency -->|"hotkeys 1 / 2 / 3"| BoidManager
    Agency -->|"preset locomotion params"| HeadOffset["HeadOffsetLocomotion"]
    Agency -->|"preset follow params"| Centroid

    LeapHand -->|"optional hand target"| BoidManager
```

`AgencyPresetController` can change boid flocking values, `HeadOffsetLocomotion` speed/yaw values, and `BoidCentroidTarget` smoothing at runtime. Because `applyOnStart` is false, it does not overwrite the serialized scene settings on startup.

## Spectator / Third-Person View Path

```mermaid
flowchart LR
    Centroid["BoidCentroid"]
    HMD["Camera / HMD pose"]
    Spectator["SpectatorCam<br/>Camera disabled"]
    Follow["SpectatorFollowCamera<br/>component disabled"]
    Switcher["CameraViewModeSwitcher<br/>component disabled"]
    Floating["VrFloatingSpectatorScreen<br/>not present by default<br/>bootstrap disabled"]
    WhiteWorld["ThirdPersonWhiteWorldMode<br/>not present by default<br/>bootstrap disabled"]

    Centroid -->|"target"| Follow
    HMD -->|"headPoseSource"| Follow
    Follow -->|"positions"| Spectator
    Switcher -->|"toggles first/third person output"| Spectator
    Floating -->|"optional HMD floating screen"| Spectator
    WhiteWorld -->|"optional force third-person white mode"| Switcher
```

The scene contains `SpectatorCam`, but both its `Camera` component and `SpectatorFollowCamera` are disabled in the serialized state. `CameraViewModeSwitcher` is also present on `Boid Manager`, but disabled.

## Experiment Host

```mermaid
flowchart TD
    Exp["Exp<br/>GameObject active"]

    Exp -. disabled .-> Setup["ExpSetup<br/>participantId: P013_Static<br/>baseLogDirectory: C:\\Users\\54402\\OneDrive\\Desktop\\4DOF_DATA"]
    Exp -. disabled .-> Exp1["Exp1<br/>spiral target recording"]
    Exp -. disabled .-> Exp2["Exp2<br/>user + boid pose recording"]
    Exp -. disabled .-> Exp3["Exp3<br/>pose + captured target recording"]
    Exp -. disabled .-> Exp4["Exp4<br/>run/idle object switching + pose recording"]

    Exp1 -->|"rigRoot"| XRRig["XRRig"]
    Exp2 -->|"userRigRoot"| XRRig
    Exp3 -->|"rigRoot"| XRRig
    Exp4 -->|"rigRoot"| XRRig
    Exp2 -->|"userTransform"| Camera["Camera"]
    Exp3 -->|"playerTransform"| Camera
    Exp4 -->|"cameraTransform"| Camera
    Exp2 -->|"boidTransform"| BoidShark["BoidShark transform"]
```

All experiment scripts are disabled in the scene. Their common keyboard convention is `S` to start and `Q` to stop or abort.

## Runtime Mode Summary

| Subsystem | Serialized state | Main role |
| --- | --- | --- |
| `HeadOffsetLocomotion` | Disabled | Legacy rig locomotion from head offset, pitch, and yaw |
| `PredictiveGhostAvatarLocomotion` | Enabled | Current predictive separation / ghost-follow locomotion |
| `AdaptiveFlyAgent` + `HeadIntentFeatureExtractor` | Disabled | ML-Agents residual or absolute locomotion policy |
| `BoidManager` | Enabled | Compute-shader boid flock update |
| `AgencyPresetController` | Enabled | Runtime preset hotkeys for boids, locomotion, centroid smoothing |
| `SpectatorCam` camera/follow | Disabled | Optional third-person spectator view |
| `Exp1` to `Exp4` | Disabled | Optional study logging modes |

## Notes / Risks

- `BoidShark` has two MonoBehaviour entries whose script GUID was not found in the project or package cache during inspection. Unity will likely show these as missing scripts.
- `HeadOffsetLocomotion`, `PredictiveGhostAvatarLocomotion`, and `AdaptiveFlyAgent` all write to the same `XRRig` transform. Enable exactly one primary locomotion driver for a clean run.
- `HeadOffsetLocomotion` and `HeadIntentFeatureExtractor` both use `C` as a recenter/calibrate key. If both are enabled together, that key has two meanings.
- The current scene has experiment setup data serialized but disabled. Enabling an experiment will reset the rig pose on start/stop according to each experiment script.
