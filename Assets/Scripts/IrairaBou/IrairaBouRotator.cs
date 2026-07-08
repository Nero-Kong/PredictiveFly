using UnityEngine;

/// <summary>
/// Simple official-primitive animation helper for rotating hazard bars.
/// </summary>
public class IrairaBouRotator : MonoBehaviour
{
    public Vector3 localAxis = Vector3.up;
    public float degreesPerSecond = 45f;

    void Update()
    {
        if (localAxis.sqrMagnitude <= 1e-6f || Mathf.Approximately(degreesPerSecond, 0f))
        {
            return;
        }

        transform.Rotate(localAxis.normalized, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
