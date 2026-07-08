using UnityEngine;

/// <summary>
/// Checkpoint marker for the official-primitive 3D iraira-bou scene.
/// </summary>
public class IrairaBouCheckpoint : MonoBehaviour
{
    public int index;
    public bool reached;
    public Color pendingColor = new Color(1f, 0.82f, 0.18f, 1f);
    public Color reachedColor = new Color(0.2f, 1f, 0.55f, 1f);

    public void MarkReached()
    {
        if (reached)
        {
            return;
        }

        reached = true;
        ApplyColor(reachedColor);
    }

    public void ResetCheckpoint()
    {
        reached = false;
        ApplyColor(pendingColor);
    }

    void OnEnable()
    {
        ApplyColor(reached ? reachedColor : pendingColor);
    }

    void ApplyColor(Color color)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.materials)
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
                    material.SetColor("_EmissionColor", color * 1.4f);
                }
            }
        }
    }
}
