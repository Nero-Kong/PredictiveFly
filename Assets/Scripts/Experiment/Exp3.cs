using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SMHMIs.Experiment
{
    /// <summary>
    /// Experiment 3:
    /// - Press S to start a 2-minute session, Q to abort (no log).
    /// - Records main camera motion at a fixed sample rate.
    /// - Tracks collisions reported via Exp2Tracker objects and notes captured targets in the log.
    /// </summary>
    public class Exp3 : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ExpSetup setup;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Transform rigRoot;
        [SerializeField] private Vector3 resetPosition = Vector3.zero;
        [SerializeField] private Vector3 resetEuler = Vector3.zero;

        [Header("Controls")]
        [SerializeField] private KeyCode startKey = KeyCode.S;
        [SerializeField] private KeyCode stopKey = KeyCode.Q;

        [Header("Sampling")]
        [SerializeField, Range(1f, 240f)] private float sampleRate = 60f;

        private const float DefaultRecordingDuration = 60f;
        private float recordingDuration = DefaultRecordingDuration;
        private string participantId = "P001";
        private string logDirectory = "Logs/Exp3";

        private readonly List<Sample> samples = new List<Sample>();
        private readonly List<string> captureBuffer = new List<string>();
        private bool isRecording;
        private float timer;
        private float sampleTimer;

        public bool IsRecording => isRecording;

        private void Start()
        {
            if (playerTransform == null && Camera.main != null)
            {
                playerTransform = Camera.main.transform;
            }

            ExpSetup cfg = setup != null ? setup : ExpSetup.Instance;
            if (cfg != null)
            {
                participantId = cfg.participantId;
                logDirectory = cfg.ResolveLogPath("Exp3");
            }
            recordingDuration = DefaultRecordingDuration;

            Directory.CreateDirectory(ResolveDirectory());
        }

        private void Update()
        {
            if (!isRecording && Input.GetKeyDown(startKey))
            {
                BeginRecording();
            }

            if (isRecording && Input.GetKeyDown(stopKey))
            {
                EndRecording("Stopped by user", saveLog: false);
            }

            if (!isRecording || playerTransform == null)
            {
                return;
            }

            timer += Time.deltaTime;
            sampleTimer += Time.deltaTime;

            float interval = 1f / sampleRate;
            while (sampleTimer >= interval)
            {
                sampleTimer -= interval;
                RecordSample();
            }

            if (timer >= recordingDuration)
            {
                EndRecording("Duration reached", saveLog: true);
            }
        }

        private void BeginRecording()
        {
            if (playerTransform == null)
            {
                Debug.LogWarning("[Exp3] No player transform assigned; cannot start.");
                return;
            }

            ResetPlayerPose();
            samples.Clear();
            captureBuffer.Clear();
            timer = 0f;
            sampleTimer = 0f;
            isRecording = true;
            Debug.Log("[Exp3] Recording started.");
        }

        private void RecordSample()
        {
            if (playerTransform == null)
            {
                return;
            }

            string capturedTargets = captureBuffer.Count > 0
                ? string.Join("|", captureBuffer)
                : string.Empty;

            Transform rig = rigRoot != null ? rigRoot : playerTransform;

            Vector3 cameraLocalPosition = Vector3.zero;
            Quaternion cameraLocalRotation = Quaternion.identity;

            if (rigRoot != null)
            {
                cameraLocalPosition = rigRoot.InverseTransformPoint(playerTransform.position);
                cameraLocalRotation = Quaternion.Inverse(rigRoot.rotation) * playerTransform.rotation;
            }

            samples.Add(new Sample
            {
                time = timer,
                rigPosition = rig.position,
                rigRotation = rig.rotation,
                cameraLocalPosition = cameraLocalPosition,
                cameraLocalRotation = cameraLocalRotation,
                capturedTarget = capturedTargets
            });

            captureBuffer.Clear();
        }

        private void EndRecording(string reason, bool saveLog)
        {
            if (!isRecording)
            {
                return;
            }

            isRecording = false;
            Debug.Log($"[Exp3] Recording ended: {reason}");
            if (saveLog)
            {
                WriteLog();
            }
            else
            {
                Debug.Log("[Exp3] Recording discarded at user request.");
            }

            ResetPlayerPose();
        }

        public void RegisterCapture(string trackerName)
        {
            if (!isRecording)
            {
                return;
            }

            string label = string.IsNullOrWhiteSpace(trackerName) ? "UnnamedTarget" : trackerName.Trim();
            captureBuffer.Add(label);
        }

        private void ResetPlayerPose()
        {
            Transform target = rigRoot != null ? rigRoot : playerTransform;
            if (target == null)
            {
                return;
            }

            target.position = resetPosition;
            target.rotation = Quaternion.Euler(resetEuler);

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
                Debug.LogWarning("[Exp3] No samples captured; skipping log.");
                return;
            }

            string directory = ResolveDirectory();
            string path = Path.Combine(directory, $"{participantId}_Exp3_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");

            try
            {
                using (StreamWriter writer = new StreamWriter(path, false))
                {
                    writer.WriteLine("Time,RigPosX,RigPosY,RigPosZ,RigRotX,RigRotY,RigRotZ,RigRotW,CamLocalPosX,CamLocalPosY,CamLocalPosZ,CamLocalRotX,CamLocalRotY,CamLocalRotZ,CamLocalRotW,CapturedTarget");
                    foreach (Sample sample in samples)
                    {
                        string capture = sample.capturedTarget ?? string.Empty;
                        capture = capture.Replace("\"", "\"\"");
                        writer.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:F4},{1:F4},{2:F4},{3:F4},{4:F6},{5:F6},{6:F6},{7:F6},{8:F4},{9:F4},{10:F4},{11:F6},{12:F6},{13:F6},{14:F6},\"{15}\"",
                            sample.time,
                            sample.rigPosition.x, sample.rigPosition.y, sample.rigPosition.z,
                            sample.rigRotation.x, sample.rigRotation.y, sample.rigRotation.z, sample.rigRotation.w,
                            sample.cameraLocalPosition.x, sample.cameraLocalPosition.y, sample.cameraLocalPosition.z,
                            sample.cameraLocalRotation.x, sample.cameraLocalRotation.y, sample.cameraLocalRotation.z, sample.cameraLocalRotation.w,
                            capture));
                    }
                }

                Debug.Log($"[Exp3] Log written to {path}");
            }
            catch (IOException ex)
            {
                Debug.LogError($"[Exp3] Failed to write log: {ex.Message}");
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

        private struct Sample
        {
            public float time;
            public Vector3 rigPosition;
            public Quaternion rigRotation;
            public Vector3 cameraLocalPosition;
            public Quaternion cameraLocalRotation;
            public string capturedTarget;
        }
    }
}
