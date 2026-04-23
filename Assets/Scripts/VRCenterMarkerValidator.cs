using UnityEngine;

[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
public class VRCenterMarkerValidator : MonoBehaviour
{
    [Header("Camera")]
    public Camera targetCamera;

    [Header("Marker")]
    public bool visibleOnStart = true;
    public KeyCode toggleKey = KeyCode.M;
    public Vector3 markerLocalPosition = new Vector3(0f, 0f, 2f);
    [Min(0.005f)] public float markerSize = 0.08f;
    [Min(0.001f)] public float markerThickness = 0.01f;
    public Color markerColor = new Color(0f, 1f, 0.2f, 1f);

    [Header("Debug")]
    [SerializeField] bool isVisible;

    const string MarkerRootName = "__VRCenterMarkerValidator";

    GameObject markerRoot;
    Transform horizontalLine;
    Transform verticalLine;
    Transform centerDot;
    Material markerMaterial;

    void Awake()
    {
        ResolveCamera();
        EnsureMarker();
        SetVisible(visibleOnStart);
    }

    void OnEnable()
    {
        ResolveCamera();
        EnsureMarker();
        SetVisible(visibleOnStart);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            SetVisible(!isVisible);
        }

        UpdateMarkerTransform();
    }

    void OnDisable()
    {
        SetVisible(false);
    }

    void OnDestroy()
    {
        if (markerMaterial != null)
        {
            Destroy(markerMaterial);
            markerMaterial = null;
        }

        if (markerRoot != null)
        {
            Destroy(markerRoot);
            markerRoot = null;
        }
    }

    void ResolveCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    void EnsureMarker()
    {
        if (targetCamera == null)
        {
            return;
        }

        if (markerRoot == null)
        {
            Transform existing = targetCamera.transform.Find(MarkerRootName);
            markerRoot = existing != null ? existing.gameObject : new GameObject(MarkerRootName);
        }

        markerRoot.transform.SetParent(targetCamera.transform, false);
        markerRoot.hideFlags = HideFlags.DontSave;

        EnsureMaterial();

        if (horizontalLine == null)
        {
            horizontalLine = CreateMarkerPart("HorizontalLine");
        }

        if (verticalLine == null)
        {
            verticalLine = CreateMarkerPart("VerticalLine");
        }

        if (centerDot == null)
        {
            centerDot = CreateMarkerPart("CenterDot");
        }

        ApplyMaterial(horizontalLine);
        ApplyMaterial(verticalLine);
        ApplyMaterial(centerDot);
        UpdateMarkerTransform();
    }

    Transform CreateMarkerPart(string partName)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.SetParent(markerRoot.transform, false);

        Collider col = part.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        return part.transform;
    }

    void EnsureMaterial()
    {
        if (markerMaterial != null)
        {
            ApplyMarkerColor();
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        markerMaterial = new Material(shader)
        {
            name = "__VRCenterMarkerMat",
            hideFlags = HideFlags.DontSave
        };

        ApplyMarkerColor();
    }

    void ApplyMarkerColor()
    {
        if (markerMaterial == null)
        {
            return;
        }

        if (markerMaterial.HasProperty("_BaseColor"))
        {
            markerMaterial.SetColor("_BaseColor", markerColor);
        }

        if (markerMaterial.HasProperty("_Color"))
        {
            markerMaterial.SetColor("_Color", markerColor);
        }
    }

    void ApplyMaterial(Transform target)
    {
        if (target == null || markerMaterial == null)
        {
            return;
        }

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = markerMaterial;
        }
    }

    void UpdateMarkerTransform()
    {
        if (markerRoot == null)
        {
            return;
        }

        float size = Mathf.Max(0.005f, markerSize);
        float thickness = Mathf.Min(size, Mathf.Max(0.001f, markerThickness));

        markerRoot.transform.localPosition = markerLocalPosition;
        markerRoot.transform.localRotation = Quaternion.identity;
        markerRoot.transform.localScale = Vector3.one;

        if (horizontalLine != null)
        {
            horizontalLine.localPosition = Vector3.zero;
            horizontalLine.localRotation = Quaternion.identity;
            horizontalLine.localScale = new Vector3(size, thickness, thickness);
        }

        if (verticalLine != null)
        {
            verticalLine.localPosition = Vector3.zero;
            verticalLine.localRotation = Quaternion.identity;
            verticalLine.localScale = new Vector3(thickness, size, thickness);
        }

        if (centerDot != null)
        {
            centerDot.localPosition = Vector3.zero;
            centerDot.localRotation = Quaternion.identity;
            centerDot.localScale = Vector3.one * (thickness * 1.6f);
        }

        ApplyMarkerColor();
    }

    void SetVisible(bool visible)
    {
        isVisible = visible;
        if (markerRoot != null)
        {
            markerRoot.SetActive(visible);
        }
    }
}
