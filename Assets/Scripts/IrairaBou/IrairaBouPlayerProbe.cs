using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A lightweight probe for testing the 3D iraira-bou scene before wiring it to XR or the drone.
/// Keyboard demo controls: WASD = planar move, Q/E = vertical move, R = reset.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class IrairaBouPlayerProbe : MonoBehaviour
{
    public enum ControlMode
    {
        KeyboardDemo,
        FollowTarget
    }

    [Header("Control")]
    public ControlMode controlMode = ControlMode.KeyboardDemo;
    public Transform followTarget;
    public Vector3 followTargetLocalOffset;
    public float moveSpeed = 3f;
    public float verticalSpeed = 2f;
    public KeyCode resetKey = KeyCode.R;
    public bool blockAgainstTubeWall = true;

    [Header("Run State")]
    public Vector3 startPosition;
    public TextMesh statusLabel;
    public AudioSource audioSource;
    public IrairaBouTubeBoundary tubeBoundary;
    public Color normalColor = new Color(0.9f, 1f, 1f, 1f);
    public Color faultColor = new Color(1f, 0.22f, 0.12f, 1f);
    public Color finishColor = new Color(0.2f, 1f, 0.55f, 1f);

    Rigidbody body;
    Renderer probeRenderer;
    AudioClip zapClip;
    Vector3 desiredPosition;
    float elapsedTime;
    int faultCount;
    int checkpointCount;
    bool running;
    bool finished;
    SphereCollider probeCollider;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;

        probeCollider = GetComponent<SphereCollider>();
        probeCollider.isTrigger = false;

        probeRenderer = GetComponentInChildren<Renderer>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 0.25f;
            audioSource.maxDistance = 8f;
        }
    }

    void OnEnable()
    {
        if (startPosition == Vector3.zero)
        {
            startPosition = transform.position;
        }

        if (tubeBoundary == null)
        {
            tubeBoundary = FindFirstObjectByType<IrairaBouTubeBoundary>();
        }

        ResetRun();
    }

    void Update()
    {
        if (WasResetPressed())
        {
            ResetRun();
        }

        if (!finished)
        {
            elapsedTime += Time.deltaTime;
        }

        if (controlMode == ControlMode.FollowTarget && followTarget != null)
        {
            desiredPosition = followTarget.TransformPoint(followTargetLocalOffset);
        }
        else
        {
            desiredPosition += ReadKeyboardMovement() * Time.deltaTime;
        }

        UpdateStatusLabel();
    }

    void FixedUpdate()
    {
        ApplyTubeWallBlocking();
        body.MovePosition(desiredPosition);
    }

    void OnTriggerEnter(Collider other)
    {
        IrairaBouHazard hazard = other.GetComponentInParent<IrairaBouHazard>();
        if (hazard != null)
        {
            RegisterFault(hazard.hazardLabel);
            return;
        }

        IrairaBouCheckpoint checkpoint = other.GetComponentInParent<IrairaBouCheckpoint>();
        if (checkpoint != null && !checkpoint.reached)
        {
            checkpoint.MarkReached();
            checkpointCount++;
            PlayTone(660f, 0.08f, 0.18f);
            return;
        }

        IrairaBouFinish finish = other.GetComponentInParent<IrairaBouFinish>();
        if (finish != null && !finished)
        {
            finished = true;
            SetProbeColor(finishColor);
            PlayTone(880f, 0.18f, 0.25f);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        IrairaBouHazard hazard = collision.collider.GetComponentInParent<IrairaBouHazard>();
        if (hazard != null)
        {
            RegisterFault(hazard.hazardLabel);
            return;
        }

        IrairaBouTubeBoundary boundary = collision.collider.GetComponentInParent<IrairaBouTubeBoundary>();
        if (boundary != null)
        {
            RegisterFault(boundary.hazardLabel);
        }
    }

    public void ResetRun()
    {
        running = true;
        finished = false;
        elapsedTime = 0f;
        faultCount = 0;
        checkpointCount = 0;
        desiredPosition = startPosition;
        transform.position = startPosition;
        body.position = startPosition;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        SetProbeColor(normalColor);

        foreach (IrairaBouCheckpoint checkpoint in FindObjectsByType<IrairaBouCheckpoint>(FindObjectsSortMode.None))
        {
            checkpoint.ResetCheckpoint();
        }
    }

    void RegisterFault(string reason)
    {
        faultCount++;
        SetProbeColor(faultColor);
        PlayZap();
        desiredPosition = startPosition;
        body.position = startPosition;
        transform.position = startPosition;
    }

    void ApplyTubeWallBlocking()
    {
        if (finished || tubeBoundary == null || !blockAgainstTubeWall)
        {
            return;
        }

        if (tubeBoundary.TryConstrainInside(desiredPosition, GetWorldProbeRadius(), out Vector3 constrainedPosition))
        {
            desiredPosition = constrainedPosition;
        }
    }

    float GetWorldProbeRadius()
    {
        if (probeCollider == null)
        {
            return 0f;
        }

        Vector3 scale = transform.lossyScale;
        float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        return probeCollider.radius * maxScale;
    }

    Vector3 ReadKeyboardMovement()
    {
        Vector3 input = Vector3.zero;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) input += Vector3.forward;
            if (keyboard.sKey.isPressed) input += Vector3.back;
            if (keyboard.aKey.isPressed) input += Vector3.left;
            if (keyboard.dKey.isPressed) input += Vector3.right;
            if (keyboard.eKey.isPressed) input += Vector3.up;
            if (keyboard.qKey.isPressed) input += Vector3.down;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.W)) input += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) input += Vector3.back;
        if (Input.GetKey(KeyCode.A)) input += Vector3.left;
        if (Input.GetKey(KeyCode.D)) input += Vector3.right;
        if (Input.GetKey(KeyCode.E)) input += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) input += Vector3.down;
#endif

        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        Camera camera = Camera.main;
        if (camera != null)
        {
            forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized;
            if (forward == Vector3.zero) forward = Vector3.forward;
            if (right == Vector3.zero) right = Vector3.right;
        }

        Vector3 planar = right * input.x + forward * input.z;
        Vector3 vertical = Vector3.up * input.y;
        return planar * moveSpeed + vertical * verticalSpeed;
    }

    bool WasResetPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(resetKey))
        {
            return true;
        }
#endif

        return false;
    }

    void UpdateStatusLabel()
    {
        if (statusLabel == null)
        {
            return;
        }

        string state = finished ? "FINISH" : running ? "RUNNING" : "READY";
        statusLabel.text = $"{state}\nTime {elapsedTime:0.0}s\nFaults {faultCount}\nCheckpoints {checkpointCount}\nWASD + Q/E, R reset";
    }

    void SetProbeColor(Color color)
    {
        if (probeRenderer == null)
        {
            return;
        }

        foreach (Material material in probeRenderer.materials)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", color * 1.6f);
            }
        }
    }

    void PlayZap()
    {
        PlayTone(110f, 0.12f, 0.4f);
    }

    void PlayTone(float frequency, float duration, float volume)
    {
        if (audioSource == null)
        {
            return;
        }

        zapClip = CreateToneClip(frequency, duration);
        audioSource.PlayOneShot(zapClip, volume);
    }

    static AudioClip CreateToneClip(float frequency, float duration)
    {
        const int sampleRate = 24000;
        int sampleCount = Mathf.Max(1, Mathf.CeilToInt(sampleRate * duration));
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = 1f - i / (float)sampleCount;
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope;
        }

        AudioClip clip = AudioClip.Create("IrairaBou_Tone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
