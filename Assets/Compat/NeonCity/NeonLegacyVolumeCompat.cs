using UnityEngine;

[DisallowMultipleComponent]
public sealed class NeonLegacyVolumeCompat : MonoBehaviour
{
    public ScriptableObject sharedProfile;
    public bool isGlobal = true;
    public float blendDistance;
    public float weight = 1f;
    public float priority;
}
