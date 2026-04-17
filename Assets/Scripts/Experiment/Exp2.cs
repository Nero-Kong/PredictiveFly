using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace SMHMIs.Experiment
{
    /// <summary>
    /// Independent experiment that records the motion of the player (camera) and a boid target.
    /// Press S to start logging, Q to stop. After stopping (or reaching the duration limit) a CSV is written.
    /// </summary>
    public class Exp2 : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ExpSetup setup;
        [SerializeField] private Transform userTransform;
        [SerializeField] private Transform boidTransform;
        [SerializeField] private Transform centerMarker;
        [SerializeField, Min(0f)] private float markerDistance = 0.5f;
        [SerializeField] private Canvas userCanvas;
        [SerializeField] private Color crosshairColor = Color.white;
        [SerializeField, Range(12, 200)] private int crosshairFontSize = 48;
        [SerializeField] private GameObject objectToToggle;

        [Header("Runtime")]
        private const float DefaultRecordingDuration = 60f;
        private float recordingDuration = DefaultRecordingDuration;
        [SerializeField] private KeyCode startKey = KeyCode.S;
        [SerializeField] private KeyCode stopKey = KeyCode.Q;

        [Header("Logging")]
        private string participantId = "P001";
        private string logDirectory = "Logs/Exp2";

        [Header("User Reset")]
        [SerializeField] private Transform userRigRoot;
        [SerializeField] private Vector3 resetPosition = Vector3.zero;
        [SerializeField] private Vector3 resetEuler = Vector3.zero;

        private readonly List<Sample> samples = new List<Sample>();
        private bool isRecording;
        private float timer;
        private GameObject crosshairRoot;

        private void Start()
        {
            if (userTransform == null && Camera.main != null)
            {
                userTransform = Camera.main.transform;
            }

            ExpSetup cfg = setup != null ? setup : ExpSetup.Instance;
            if (cfg != null)
            {
                participantId = cfg.participantId;
                logDirectory = cfg.ResolveLogPath("Exp2");
            }
            recordingDuration = DefaultRecordingDuration;

            Directory.CreateDirectory(ResolveDirectory());
            UpdateMarker(true);
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
            RecordSample();

            if (timer >= recordingDuration)
            {
                EndRecording("Duration reached", saveLog: true);
            }
        }

        private void BeginRecording()
        {
            if (boidTransform == null || userTransform == null)
            {
                Debug.LogWarning("[Exp2] Missing transforms; cannot start recording.");
                return;
            }

            ResetUserRigPose();
            samples.Clear();
            timer = 0f;
            isRecording = true;
            UpdateMarker(false);
            SetHiddenObject(true);
            Debug.Log("[Exp2] Recording started.");
        }

        private void RecordSample()
        {
            if (boidTransform == null || userTransform == null)
            {
                return;
            }

            Transform rig = userRigRoot != null ? userRigRoot : userTransform;

            Vector3 cameraLocalPosition = Vector3.zero;
            Quaternion cameraLocalRotation = Quaternion.identity;

            if (userRigRoot != null)
            {
                cameraLocalPosition = userRigRoot.InverseTransformPoint(userTransform.position);
                cameraLocalRotation = Quaternion.Inverse(userRigRoot.rotation) * userTransform.rotation;
            }

            samples.Add(new Sample
            {
                time = timer,
                rigPosition = rig.position,
                rigRotation = rig.rotation,
                cameraLocalPosition = cameraLocalPosition,
                cameraLocalRotation = cameraLocalRotation,
                boidPosition = boidTransform.position,
                boidRotation = boidTransform.rotation
            });
        }

        private void EndRecording(string reason, bool saveLog)
        {
            if (!isRecording)
            {
                return;
            }

            isRecording = false;
            Debug.Log($"[Exp2] Recording ended: {reason}");
            UpdateMarker(true);
            SetHiddenObject(false);
            if (saveLog)
            {
                WriteLog();
            }
            else
            {
                Debug.Log("[Exp2] Recording discarded (user exit).");
            }

            ResetUserRigPose();
        }

        private void WriteLog()
        {
            if (samples.Count == 0)
            {
                Debug.LogWarning("[Exp2] No samples captured; skipping log.");
                return;
            }

            string directory = ResolveDirectory();
            string fileName = $"{participantId}_Exp2_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = Path.Combine(directory, fileName);

            try
            {
                using (StreamWriter writer = new StreamWriter(path, false))
                {
                    writer.WriteLine("Time,RigPosX,RigPosY,RigPosZ,RigRotX,RigRotY,RigRotZ,RigRotW,CamLocalPosX,CamLocalPosY,CamLocalPosZ,CamLocalRotX,CamLocalRotY,CamLocalRotZ,CamLocalRotW,BoidPosX,BoidPosY,BoidPosZ,BoidRotX,BoidRotY,BoidRotZ,BoidRotW");
                    foreach (Sample sample in samples)
                    {
                        writer.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:F4},{1:F4},{2:F4},{3:F4},{4:F6},{5:F6},{6:F6},{7:F6},{8:F4},{9:F4},{10:F4},{11:F6},{12:F6},{13:F6},{14:F6},{15:F4},{16:F4},{17:F4},{18:F6},{19:F6},{20:F6},{21:F6}",
                            sample.time,
                            sample.rigPosition.x, sample.rigPosition.y, sample.rigPosition.z,
                            sample.rigRotation.x, sample.rigRotation.y, sample.rigRotation.z, sample.rigRotation.w,
                            sample.cameraLocalPosition.x, sample.cameraLocalPosition.y, sample.cameraLocalPosition.z,
                            sample.cameraLocalRotation.x, sample.cameraLocalRotation.y, sample.cameraLocalRotation.z, sample.cameraLocalRotation.w,
                            sample.boidPosition.x, sample.boidPosition.y, sample.boidPosition.z,
                            sample.boidRotation.x, sample.boidRotation.y, sample.boidRotation.z, sample.boidRotation.w));
                    }
                }

                Debug.Log($"[Exp2] Log written to {path}");
            }
            catch (IOException ex)
            {
                Debug.LogError($"[Exp2] Failed to write log: {ex.Message}");
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

        private void ResetUserRigPose()
        {
            Transform target = userRigRoot != null ? userRigRoot : userTransform;
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

        private void UpdateMarker(bool hidden)
        {
            if (userCanvas != null)
            {
                EnsureCrosshair();
                if (crosshairRoot != null)
                {
                    crosshairRoot.SetActive(!hidden);
                }
                return;
            }

            if (centerMarker == null || userTransform == null)
            {
                return;
            }

            if (hidden)
            {
                centerMarker.gameObject.SetActive(false);
                return;
            }

            centerMarker.gameObject.SetActive(true);
            centerMarker.SetParent(userTransform, false);
            centerMarker.localPosition = new Vector3(0f, 0f, Mathf.Max(0f, markerDistance));
            centerMarker.localRotation = Quaternion.identity;
        }

        private void EnsureCrosshair()
        {
            if (userCanvas == null)
            {
                return;
            }

            if (crosshairRoot != null)
            {
                ApplyGraphicStyle();
                return;
            }

            GameObject go = new GameObject("Exp2Crosshair", typeof(RectTransform));
            go.transform.SetParent(userCanvas.transform, false);
            crosshairRoot = go;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            GameObject horizontal = new GameObject("Horizontal", typeof(RectTransform));
            GameObject vertical = new GameObject("Vertical", typeof(RectTransform));
            horizontal.transform.SetParent(go.transform, false);
            vertical.transform.SetParent(go.transform, false);

            Image hImage = horizontal.AddComponent<Image>();
            Image vImage = vertical.AddComponent<Image>();
            hImage.raycastTarget = false;
            vImage.raycastTarget = false;

            go.AddComponent<CrosshairLines>().Initialize(hImage, vImage);

            ApplyGraphicStyle();
        }

        private void ApplyGraphicStyle()
        {
            CrosshairLines lines = crosshairRoot != null ? crosshairRoot.GetComponent<CrosshairLines>() : null;
            if (lines == null)
            {
                return;
            }

            lines.ApplyStyle(crosshairColor, crosshairFontSize);
        }

        private sealed class CrosshairLines : MonoBehaviour
        {
            private Image horizontal;
            private Image vertical;

            public void Initialize(Image horizontalImage, Image verticalImage)
            {
                horizontal = horizontalImage;
                vertical = verticalImage;
            }

            public void ApplyStyle(Color color, int size)
            {
                if (horizontal == null || vertical == null)
                {
                    return;
                }

                float lineThickness = Mathf.Max(1f, size * 0.1f);
                float lineLength = Mathf.Max(10f, size);

                horizontal.color = color;
                vertical.color = color;

                RectTransform hRect = horizontal.rectTransform;
                RectTransform vRect = vertical.rectTransform;

                hRect.anchorMin = hRect.anchorMax = hRect.pivot = new Vector2(0.5f, 0.5f);
                vRect.anchorMin = vRect.anchorMax = vRect.pivot = new Vector2(0.5f, 0.5f);

                hRect.sizeDelta = new Vector2(lineLength, lineThickness);
                vRect.sizeDelta = new Vector2(lineThickness, lineLength);

                horizontal.gameObject.SetActive(true);
                vertical.gameObject.SetActive(true);
            }
        }

        private void SetHiddenObject(bool hidden)
        {
            if (objectToToggle == null)
            {
                return;
            }

            objectToToggle.SetActive(!hidden);
        }

        private struct Sample
        {
            public float time;
            public Vector3 rigPosition;
            public Quaternion rigRotation;
            public Vector3 cameraLocalPosition;
            public Quaternion cameraLocalRotation;
            public Vector3 boidPosition;
            public Quaternion boidRotation;
        }
    }
}
