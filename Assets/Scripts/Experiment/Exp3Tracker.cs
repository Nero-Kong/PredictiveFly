using UnityEngine;

namespace SMHMIs.Experiment
{
    /// <summary>
    /// Helper component for Exp3 targets.
    /// When the main camera collider touches this object, it reports the capture to Exp3 and disappears.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Exp3Tracker : MonoBehaviour
    {
        [SerializeField] private Exp3 experiment;
        [SerializeField] private string trackerId;
        [SerializeField] private bool destroyOnCapture = true;

        private bool captured;

        private void Reset()
        {
            trackerId = gameObject.name;
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void Awake()
        {
            if (experiment == null)
            {
                experiment = FindFirstObjectByType<Exp3>();
            }

            if (string.IsNullOrWhiteSpace(trackerId))
            {
                trackerId = gameObject.name;
            }

            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (captured || other == null || experiment == null || !experiment.IsRecording)
            {
                return;
            }

            if (!IsMainCameraCollider(other))
            {
                return;
            }

            captured = true;
            experiment?.RegisterCapture(trackerId);

            if (destroyOnCapture)
            {
                Destroy(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private static bool IsMainCameraCollider(Collider other)
        {
            if (other.CompareTag("MainCamera"))
            {
                return true;
            }

            Transform t = other.transform;
            if (t.CompareTag("MainCamera"))
            {
                return true;
            }

            Camera cam = t.GetComponent<Camera>();
            if (cam != null && cam.CompareTag("MainCamera"))
            {
                return true;
            }

            return false;
        }
    }
}
