using UnityEngine;

namespace SMHMIs.Experiment
{
    /// <summary>
    /// Central configuration for experiment-wide settings.
    /// Other experiment scripts should reference this asset for defaults.
    /// </summary>
    public class ExpSetup : MonoBehaviour
    {
        [Header("Participant Info")]
        public string participantId = "P001";

        [Header("Logging")]
        public string baseLogDirectory = "Logs";

        [Header("Recording Durations")]
        [Tooltip("Default duration (seconds) used by experiments such as Exp2/Exp1.")]
        public float recordingDuration = 120f;

        private static ExpSetup instance;
        public static ExpSetup Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<ExpSetup>();
                    if (instance == null)
                    {
                        Debug.LogWarning("No ExpSetup found in the scene.");
                    }
                }

                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        public string ResolveLogPath(string subDirectory)
        {
            string dir = baseLogDirectory;
            if (!string.IsNullOrWhiteSpace(subDirectory))
            {
                dir = System.IO.Path.Combine(baseLogDirectory, subDirectory);
            }

            return dir;
        }
    }
}
