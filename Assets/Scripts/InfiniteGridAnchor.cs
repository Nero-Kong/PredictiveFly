using UnityEngine;

[DisallowMultipleComponent]
public class InfiniteGridAnchor : MonoBehaviour {
    public Transform rig;
    public float gridY;
    [Min (0.01f)] public float snapMeters = 1f;

    void LateUpdate () {
        if (rig == null) {
            return;
        }

        float snap = Mathf.Max (0.01f, snapMeters);
        Vector3 rigPos = rig.position;
        transform.position = new Vector3 (
            Mathf.Floor (rigPos.x / snap) * snap,
            gridY,
            Mathf.Floor (rigPos.z / snap) * snap
        );
    }
}
