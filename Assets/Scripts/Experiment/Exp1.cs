using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SMHMIs.Experiment
{
    /// <summary>
    /// Generates a spiral of tiny spheres along the main camera's initial forward direction.
    /// Each sphere disappears when touched by the main camera.
    /// Spiral parameters can be adjusted in realtime; press R_refresh to rebuild.
    /// </summary>
    public class Exp1 : MonoBehaviour
    {
        [Header("Spiral Settings")]
        [SerializeField, Min(1)] private int sphereCount = 100;
        [SerializeField, Min(0.1f)] private float spacingMeters = 1f;
        [SerializeField, Min(0f)] private float radialAmplitude = 2f;
        [SerializeField, Min(0.01f)] private float pitch = 0.25f;
        [SerializeField, Min(0.1f)] private float turns = 2f;

        [Header("Visuals")]
        [SerializeField] private Transform spiralParent;
        [SerializeField, ColorUsage(true, true)] private Color sphereColor = new Color(0.2f, 0.6f, 1f, 0.4f);
        [SerializeField, Min(0.0001f)] private float sphereScale = 0.001f;

        [Header("Controls")]
        [SerializeField] private KeyCode startKey = KeyCode.S;
        [SerializeField] private KeyCode stopKey = KeyCode.Q;

        [Header("Recording")]
        [SerializeField] private ExpSetup setup;
        [SerializeField, Range(1f, 120f)] private float sampleRate = 60f;
        private float recordingDuration = 120f;
        private string participantId = "P001";
        private string logDirectory = "Logs/Exp1";

        private readonly List<GameObject> spheres = new List<GameObject>();
        private readonly List<Sample> samples = new List<Sample>();
        private Transform cameraTransform;
        private bool isRecording;
        private float recordingTimer;
        private float sampleTimer;
        private float spiralStartZ;
        private float targetEndZ;
        private bool hasCrossedStart;
        [Header("User Reset")]
        [SerializeField] private Transform rigRoot;
        [SerializeField] private Vector3 resetPosition = Vector3.zero;
        [SerializeField] private Vector3 resetEuler = Vector3.zero;

        private void Start()
        {
            cameraTransform = Camera.main != null ? Camera.main.transform : null;

            ExpSetup cfg = setup != null ? setup : ExpSetup.Instance;
            if (cfg != null)
            {
                recordingDuration = cfg.recordingDuration;
                participantId = cfg.participantId;
                logDirectory = cfg.ResolveLogPath("Exp1");
            }

            Directory.CreateDirectory(ResolveDirectory());

            if (spiralParent == null)
            {
                spiralParent = transform;
            }
        }

        private void Update()
        {
            if (!isRecording && Input.GetKeyDown(startKey))
            {
                BeginRecording();
            }

            if (isRecording && Input.GetKeyDown(stopKey))
            {
                EndRecording(saveLog: false);
            }

            if (!isRecording)
            {
                return;
            }

            if (cameraTransform == null)
            {
                return;
            }

            recordingTimer += Time.deltaTime;
            sampleTimer += Time.deltaTime;

            if (!hasCrossedStart)
            {
                if (cameraTransform.position.z >= spiralStartZ)
                {
                    hasCrossedStart = true;
                    samples.Clear();
                    recordingTimer = 0f;
                    sampleTimer = 0f;
                }
                else
                {
                    return;
                }
            }

            if (cameraTransform.position.z >= targetEndZ)
            {
                EndRecording(saveLog: true);
                return;
            }

            float interval = 1f / sampleRate;
            while (sampleTimer >= interval)
            {
                sampleTimer -= interval;
                RecordSample();
            }
        }

        private void BuildSpiral()
        {
            ClearSpiral();

            Vector3 axis = Vector3.forward;
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;
            float turnsClamped = Mathf.Max(0.01f, turns);

            spiralStartZ = 3f;

            for (int i = 0; i < sphereCount; i++)
            {
                float normalized = sphereCount == 1 ? 1f : (float)i / (sphereCount - 1);
                float angle = normalized * Mathf.PI * 2f * turnsClamped;
                float axial = spacingMeters * i + pitch * angle;
                Vector3 radialDirection = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                Vector3 radial = radialDirection * radialAmplitude;

                Vector3 position = radial + axis * (spiralStartZ + axial);

                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(spiralParent, false);
                sphere.transform.position = position;
                sphere.transform.localScale = Vector3.one * sphereScale;
                sphere.name = $"Exp1_Sphere_{i:D3}";

                if (sphere.TryGetComponent(out Collider collider))
                {
                    collider.isTrigger = true;
                }

                Renderer renderer = sphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    material.SetFloat("_Surface", 1f); // transparent
                    material.SetColor(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", sphereColor);
                    material.SetInt("_ZWrite", 0);
                    material.renderQueue = 3000;
                    renderer.material = material;
                }

                var trigger = sphere.AddComponent<SpiralSphereTrigger>();
                trigger.Initialize(cameraTransform);

                spheres.Add(sphere);
            }
            float lastAngle = Mathf.PI * 2f * turnsClamped;
            float axialLength = spacingMeters * Mathf.Max(0, sphereCount - 1) + pitch * lastAngle;
            targetEndZ = spiralStartZ + axialLength;
        }

        private void ClearSpiral()
        {
            foreach (GameObject sphere in spheres)
            {
                if (sphere != null)
                {
                    DestroyImmediate(sphere);
                }
            }

            spheres.Clear();
        }

        private void BeginRecording()
        {
            BuildSpiral();
            ResetCameraPose();
            samples.Clear();
            isRecording = true;
            recordingTimer = 0f;
            sampleTimer = 0f;
            hasCrossedStart = false;
            Debug.Log("[Exp1] Recording started.");
        }

        private void RecordSample()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Transform rig = rigRoot != null ? rigRoot : cameraTransform;

            Vector3 cameraLocalPosition = Vector3.zero;
            Quaternion cameraLocalRotation = Quaternion.identity;

            if (rigRoot != null)
            {
                cameraLocalPosition = rigRoot.InverseTransformPoint(cameraTransform.position);
                cameraLocalRotation = Quaternion.Inverse(rigRoot.rotation) * cameraTransform.rotation;
            }

            samples.Add(new Sample
            {
                time = recordingTimer,
                rigPosition = rig.position,
                rigRotation = rig.rotation,
                cameraLocalPosition = cameraLocalPosition,
                cameraLocalRotation = cameraLocalRotation
            });
        }

        private void EndRecording(bool saveLog)
        {
            if (!isRecording)
            {
                ResetCameraPose();
                return;
            }

            isRecording = false;
            Debug.Log($"[Exp1] Recording ended. Save log = {saveLog}");
            if (saveLog)
            {
                WriteLog();
            }

            ClearSpiral();
            ResetCameraPose();
        }

        private void ResetCameraPose()
        {
            Transform target = rigRoot != null ? rigRoot : cameraTransform;
            if (target == null)
            {
                return;
            }

            target.position = resetPosition;
            target.rotation = Quaternion.Euler(resetEuler);
            hasCrossedStart = false;

            if (target.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private void WriteLog()
        {
            if (samples.Count == 0)
            {
                Debug.LogWarning("[Exp1] No samples captured.");
                return;
            }

            string directory = ResolveDirectory();
            string path = Path.Combine(directory, $"{participantId}_Exp1_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");

            try
            {
                using (StreamWriter writer = new StreamWriter(path, false))
                {
                    writer.WriteLine("Time,RigPosX,RigPosY,RigPosZ,RigRotX,RigRotY,RigRotZ,RigRotW,CamLocalPosX,CamLocalPosY,CamLocalPosZ,CamLocalRotX,CamLocalRotY,CamLocalRotZ,CamLocalRotW");
                    foreach (Sample sample in samples)
                    {
                        writer.WriteLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0:F4},{1:F4},{2:F4},{3:F4},{4:F6},{5:F6},{6:F6},{7:F6},{8:F4},{9:F4},{10:F4},{11:F6},{12:F6},{13:F6},{14:F6}",
                            sample.time,
                            sample.rigPosition.x, sample.rigPosition.y, sample.rigPosition.z,
                            sample.rigRotation.x, sample.rigRotation.y, sample.rigRotation.z, sample.rigRotation.w,
                            sample.cameraLocalPosition.x, sample.cameraLocalPosition.y, sample.cameraLocalPosition.z,
                            sample.cameraLocalRotation.x, sample.cameraLocalRotation.y, sample.cameraLocalRotation.z, sample.cameraLocalRotation.w));
                    }
                }

                Debug.Log($"[Exp1] Log written to {path}");
            }
            catch (IOException ex)
            {
                Debug.LogError($"[Exp1] Failed to write log: {ex.Message}");
            }
        }

        private string ResolveDirectory()
        {
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                return Application.persistentDataPath;
            }

            return Path.IsPathRooted(logDirectory)
                ? logDirectory
                : Path.Combine(Application.persistentDataPath, logDirectory);
        }

        private sealed class SpiralSphereTrigger : MonoBehaviour
        {
            private Transform cameraTransform;
            public void Initialize(Transform camera)
            {
                cameraTransform = camera;
            }

            private void OnTriggerEnter(Collider other)
            {
                if (cameraTransform != null && other.transform == cameraTransform)
                {
                    Destroy(gameObject);
                }
                else if (other.CompareTag("MainCamera"))
                {
                    Destroy(gameObject);
                }
            }
        }

        private struct Sample
        {
            public float time;
            public Vector3 rigPosition;
            public Quaternion rigRotation;
            public Vector3 cameraLocalPosition;
            public Quaternion cameraLocalRotation;
        }
    }
}
