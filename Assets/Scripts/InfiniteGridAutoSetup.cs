using UnityEngine;
using UnityEngine.Rendering;

public static class InfiniteGridAutoSetup {
    const string GridObjectName = "__InfiniteGrid";
    const float PlaneScale = 200f; // Unity plane is 10x10, so 200 => 2000m span.
    public static bool AutoSetupOnSceneLoad = false;

    [RuntimeInitializeOnLoadMethod (RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup () {
        if (!AutoSetupOnSceneLoad) {
            return;
        }

        if (Object.FindFirstObjectByType<InfiniteGridAnchor> () != null) {
            return;
        }

        Transform rig = FindRigTransform ();
        if (rig == null) {
            return;
        }

        GameObject grid = GameObject.Find (GridObjectName);
        if (grid == null) {
            grid = GameObject.CreatePrimitive (PrimitiveType.Plane);
            grid.name = GridObjectName;

            Collider col = grid.GetComponent<Collider> ();
            if (col != null) {
                Object.Destroy (col);
            }
        }

        grid.transform.position = new Vector3 (rig.position.x, 0f, rig.position.z);
        grid.transform.rotation = Quaternion.identity;
        grid.transform.localScale = new Vector3 (PlaneScale, 1f, PlaneScale);

        var renderer = grid.GetComponent<MeshRenderer> ();
        if (renderer != null) {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material gridMaterial = BuildGridMaterial ();
            if (gridMaterial != null) {
                renderer.sharedMaterial = gridMaterial;
            }
        }

        InfiniteGridAnchor anchor = grid.GetComponent<InfiniteGridAnchor> ();
        if (anchor == null) {
            anchor = grid.AddComponent<InfiniteGridAnchor> ();
        }
        anchor.rig = rig;
        anchor.gridY = 0f;
        anchor.snapMeters = 1f;
    }

    static Transform FindRigTransform () {
        GameObject namedRig = GameObject.Find ("XRRig");
        if (namedRig != null) {
            return namedRig.transform;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null) {
            Transform t = mainCam.transform;
            while (t.parent != null) {
                if (t.name.Contains ("Rig")) {
                    return t;
                }
                t = t.parent;
            }
        }

        Camera anyCamera = Object.FindFirstObjectByType<Camera> ();
        return anyCamera != null ? anyCamera.transform : null;
    }

    static Material BuildGridMaterial () {
        Shader shader = Shader.Find ("Custom/InfiniteBWGrid");
        if (shader == null) {
            return null;
        }

        Material material = new Material (shader) {
            name = "__InfiniteGridRuntimeMat",
            hideFlags = HideFlags.DontSave
        };

        material.SetFloat ("_CellSize", 1f);
        material.SetColor ("_ColorA", Color.white);
        material.SetColor ("_ColorB", Color.black);
        material.SetFloat ("_FarFadeStart", 18f);
        material.SetFloat ("_FarFadeEnd", 55f);
        material.SetColor ("_FarColor", new Color (0.5f, 0.5f, 0.5f, 1f));
        return material;
    }
}
