using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class PredictiveFlyRouteMarkerVisualizer : MonoBehaviour
{
    public IrairaBouTubeBoundary tubeBoundary;
    public float[] checkpointProgress = { 0.18f, 0.36f, 0.54f, 0.72f };
    [Range(0.8f, 1f)] public float finishProgress = 0.995f;
    [Min(0.1f)] public float ringRadius = 0.9f;
    [Min(0.005f)] public float lineWidth = 0.045f;
    [Range(12, 96)] public int ringSegments = 48;
    public Color checkpointColor = new Color(1f, 0.82f, 0.18f, 1f);
    public Color finishColor = new Color(0.18f, 1f, 0.48f, 1f);

    GameObject markerRoot;
    Material markerMaterial;

    void OnEnable()
    {
        BuildMarkers();
    }

    void OnDisable()
    {
        ClearMarkers();
    }

    void OnValidate()
    {
        ringRadius = Mathf.Max(0.1f, ringRadius);
        lineWidth = Mathf.Max(0.005f, lineWidth);
        ringSegments = Mathf.Clamp(ringSegments, 12, 96);
        finishProgress = Mathf.Clamp(finishProgress, 0.8f, 1f);
    }

    [ContextMenu("Rebuild Route Markers")]
    public void BuildMarkers()
    {
        if (tubeBoundary == null)
        {
            tubeBoundary = FindFirstObjectByType<IrairaBouTubeBoundary>();
        }
        if (tubeBoundary == null || tubeBoundary.centerline == null || tubeBoundary.centerline.Length < 2)
        {
            return;
        }

        ClearMarkers();
        markerRoot = new GameObject("__PredictiveFly_RuntimeRouteMarkers");
        markerRoot.hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSaveInEditor;
        markerRoot.transform.SetParent(transform, true);

        if (checkpointProgress != null)
        {
            for (int i = 0; i < checkpointProgress.Length; i++)
            {
                CreateRing($"Checkpoint {i + 1}", Mathf.Clamp01(checkpointProgress[i]), checkpointColor, false);
            }
        }
        CreateRing("Finish", finishProgress, finishColor, true);
    }

    void CreateRing(string label, float normalizedProgress, Color color, bool addLabel)
    {
        if (!TrySampleRoute(normalizedProgress, out Vector3 center, out Vector3 tangent))
        {
            return;
        }

        GameObject ring = new GameObject(label);
        ring.transform.SetParent(markerRoot.transform, true);
        LineRenderer line = ring.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = ringSegments;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = GetMarkerMaterial();

        Vector3 side = Vector3.Cross(Vector3.up, tangent);
        if (side.sqrMagnitude <= 1e-6f)
        {
            side = Vector3.right;
        }
        side.Normalize();
        Vector3 up = Vector3.Cross(tangent, side).normalized;
        for (int i = 0; i < ringSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / ringSegments;
            line.SetPosition(i, center + (Mathf.Cos(angle) * side + Mathf.Sin(angle) * up) * ringRadius);
        }

        if (addLabel)
        {
            GameObject textObject = new GameObject("Finish Label");
            textObject.transform.SetParent(ring.transform, true);
            textObject.transform.position = center + up * (ringRadius + 0.3f);
            textObject.transform.rotation = Quaternion.LookRotation(-tangent, up);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = "FINISH";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.16f;
            text.fontSize = 48;
            text.color = color;
        }
    }

    bool TrySampleRoute(float normalizedProgress, out Vector3 point, out Vector3 tangent)
    {
        point = Vector3.zero;
        tangent = Vector3.forward;
        Vector3[] path = tubeBoundary.centerline;
        if (path == null || path.Length < 2)
        {
            return false;
        }

        float totalLength = tubeBoundary.TotalCenterlineLength;
        float targetDistance = Mathf.Clamp01(normalizedProgress) * totalLength;
        float accumulated = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            float segmentLength = Vector3.Distance(path[i - 1], path[i]);
            if (accumulated + segmentLength >= targetDistance || i == path.Length - 1)
            {
                float t = segmentLength > 1e-6f
                    ? Mathf.Clamp01((targetDistance - accumulated) / segmentLength)
                    : 0f;
                point = Vector3.Lerp(path[i - 1], path[i], t);
                tangent = path[i] - path[i - 1];
                tangent = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;
                return true;
            }
            accumulated += segmentLength;
        }
        return false;
    }

    Material GetMarkerMaterial()
    {
        if (markerMaterial != null)
        {
            return markerMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        markerMaterial = new Material(shader)
        {
            name = "PredictiveFly Runtime Route Marker Material",
            hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSaveInEditor
        };
        return markerMaterial;
    }

    void ClearMarkers()
    {
        if (markerRoot != null)
        {
            if (Application.isPlaying)
            {
                Destroy(markerRoot);
            }
            else
            {
                DestroyImmediate(markerRoot);
            }
            markerRoot = null;
        }

        if (markerMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(markerMaterial);
            }
            else
            {
                DestroyImmediate(markerMaterial);
            }
            markerMaterial = null;
        }
    }
}
