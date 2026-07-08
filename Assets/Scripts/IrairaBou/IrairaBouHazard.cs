using UnityEngine;

/// <summary>
/// Marker for anything that should fail the 3D iraira-bou run on contact.
/// </summary>
public class IrairaBouHazard : MonoBehaviour
{
    [Tooltip("Short label shown in debug/status messages.")]
    public string hazardLabel = "Electric rail";
}
