using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SMHMIs.Experiment
{
    /// <summary>
    /// Experiment 4:
    /// - Press S to start a fixed-duration run (default 120s from ExpSetup).
    /// - Press Q to abort without saving.
    /// - During the run GameObject1 is enabled (and GameObject2 disabled). When ending or aborting, the states swap back.
    /// - Logs XR rig world pose and camera local pose relative to the rig.
    /// - Rig is reset to the origin whenever the experiment starts or stops.
    /// </summary>
    public class Exp4 : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ExpSetup setup;
        [SerializeField] private Transform rigRoot;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private GameObject gameObjectOnRun;
        [SerializeField] private GameObject gameObjectIdle;

        [Header("Controls")]
        [SerializeField] private KeyCode startKey = KeyCode.S;
        [SerializeField] private KeyCode stopKey = KeyCode.Q;

        [Header("Sampling")]
        [SerializeField, Range(1f, 240f)] private float sampleRate = 60f;

        [Header("Reset Pose")]
        [SerializeField] private Vector3 resetPosition = Vector3.zero;
        [SerializeField] private Vector3 resetEuler = Vector3.zero;

        private readonly List<Sample> samples = new List<Sample>();
        private const float DefaultRecordingDuration = 60f;
        private float recordingDuration = DefaultRecordingDuration;
        private string participantId = "P001";
        private string logDirectory = "Logs/Exp4";

        private bool isRecording;
        private float timer;
        private float sampleTimer;

        private void Start()
        {
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            ExpSetup cfg = setup != null ? setup : ExpSetup.Instance;
            if (cfg != null)
            {
                participantId = cfg.participantId;
                logDirectory = cfg.ResolveLogPath("Exp4");
            }
            recordingDuration = DefaultRecordingDuration;

            Directory.CreateDirectory(ResolveDirectory());
            SetRunObjects(false);
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

            if (!isRecording)
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
            if (rigRoot == null || cameraTransform == null)
            {
                Debug.LogWarning("[Exp4] Missing rig or camera reference; cannot start.");
                return;
            }

            ResetRigPose();
            samples.Clear();
            timer = 0f;
            sampleTimer = 0f;
            isRecording = true;
            SetRunObjects(true);
            Debug.Log("[Exp4] Recording started.");
        }

        private void RecordSample()
        {
            if (rigRoot == null || cameraTransform == null)
            {
                return;
            }

            Vector3 camLocalPos = rigRoot.InverseTransformPoint(cameraTransform.position);
            Quaternion camLocalRot = Quaternion.Inverse(rigRoot.rotation) * cameraTransform.rotation;

            samples.Add(new Sample
            {
                time = timer,
                rigPosition = rigRoot.position,
                rigRotation = rigRoot.rotation,
                cameraLocalPosition = camLocalPos,
                cameraLocalRotation = camLocalRot
            });
        }

        private void EndRecording(string reason, bool saveLog)
        {
            if (!isRecording)
            {
                ResetRigPose();
                return;
            }

            isRecording = false;
            Debug.Log($"[Exp4] Recording ended: {reason}");
            if (saveLog)
            {
                WriteLog();
            }
            else
            {
                Debug.Log("[Exp4] Recording discarded at user request.");
            }

            SetRunObjects(false);
            ResetRigPose();
        }

        private void ResetRigPose()
        {
            if (rigRoot == null)
            {
                return;
            }

            rigRoot.position = resetPosition;
            rigRoot.rotation = Quaternion.Euler(resetEuler);

            if (rigRoot.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private void SetRunObjects(bool isRunning)
        {
            if (gameObjectOnRun != null)
            {
                gameObjectOnRun.SetActive(isRunning);
            }

            if (gameObjectIdle != null)
            {
                gameObjectIdle.SetActive(!isRunning);
            }
        }

        private void WriteLog()
        {
            if (samples.Count == 0)
            {
                Debug.LogWarning("[Exp4] No samples captured; skipping log.");
                return;
            }

            string directory = ResolveDirectory();
            string path = Path.Combine(directory, $"{participantId}_Exp4_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");

            try
            {
                using (StreamWriter writer = new StreamWriter(path, false))
                {
                    writer.WriteLine("Time,RigPosX,RigPosY,RigPosZ,RigRotX,RigRotY,RigRotZ,RigRotW,CamLocalPosX,CamLocalPosY,CamLocalPosZ,CamLocalRotX,CamLocalRotY,CamLocalRotZ,CamLocalRotW");
                    foreach (Sample sample in samples)
                    {
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "{0:F4},{1:F4},{2:F4},{3:F4},{4:F6},{5:F6},{6:F6},{7:F6},{8:F4},{9:F4},{10:F4},{11:F6},{12:F6},{13:F6},{14:F6}",
                            sample.time,
                            sample.rigPosition.x, sample.rigPosition.y, sample.rigPosition.z,
                            sample.rigRotation.x, sample.rigRotation.y, sample.rigRotation.z, sample.rigRotation.w,
                            sample.cameraLocalPosition.x, sample.cameraLocalPosition.y, sample.cameraLocalPosition.z,
                            sample.cameraLocalRotation.x, sample.cameraLocalRotation.y, sample.cameraLocalRotation.z, sample.cameraLocalRotation.w));
                    }
                }

                Debug.Log($"[Exp4] Log written to {path}");
            }
            catch (IOException ex)
            {
                Debug.LogError($"[Exp4] Failed to write log: {ex.Message}");
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
        }
    }
}
