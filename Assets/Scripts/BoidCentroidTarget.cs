using UnityEngine;

[DefaultExecutionOrder (-200)]
[DisallowMultipleComponent]
public class BoidCentroidTarget : MonoBehaviour {
    public BoidManager manager;
    public bool autoFindManager = true;
    public bool assignAsBoidTarget = false;
    public Vector3 worldOffset = Vector3.zero;
    [Min (0f)] public float followSmooth = 10f;
    public bool rotateAlongMovement = true;
    [Min (0f)] public float headingSmooth = 12f;
    [Min (0f)] public float velocitySmooth = 8f;
    [Min (0f)] public float minHeadingSpeed = 0.03f;
    [Min (0f)] public float maxYawSpeedDeg = 45f;

    Vector3 lastPosition;
    bool hasLastPosition;
    Vector3 smoothedPlanarVelocity;

    void Start () {
        EnsureManager ();
        if (assignAsBoidTarget && manager != null) {
            manager.SetTarget (transform);
        }

        lastPosition = transform.position;
        hasLastPosition = true;
        smoothedPlanarVelocity = Vector3.zero;
    }

    void LateUpdate () {
        EnsureManager ();
        if (manager == null) {
            return;
        }

        if (assignAsBoidTarget && manager.target != transform) {
            manager.SetTarget (transform);
        }

        if (!manager.TryGetFlockCentroid (out Vector3 centroid)) {
            return;
        }

        Vector3 desired = centroid + worldOffset;
        if (followSmooth <= 0f) {
            transform.position = desired;
        } else {
            float t = 1f - Mathf.Exp (-followSmooth * Time.deltaTime);
            transform.position = Vector3.Lerp (transform.position, desired, t);
        }

        UpdateHeading (Time.deltaTime);
    }

    void EnsureManager () {
        if (manager == null && autoFindManager) {
            manager = FindFirstObjectByType<BoidManager> ();
        }
    }

    void UpdateHeading (float dt) {
        if (!rotateAlongMovement) {
            return;
        }

        if (!hasLastPosition) {
            lastPosition = transform.position;
            hasLastPosition = true;
            return;
        }

        float safeDt = Mathf.Max (dt, 1e-5f);
        Vector3 velocity = (transform.position - lastPosition) / safeDt;
        lastPosition = transform.position;

        // Third-person follow should remain horizon-stable.
        velocity.y = 0f;

        if (smoothedPlanarVelocity.sqrMagnitude <= 1e-6f) {
            smoothedPlanarVelocity = velocity;
        } else {
            float velT = velocitySmooth <= 0f ? 1f : 1f - Mathf.Exp (-velocitySmooth * dt);
            smoothedPlanarVelocity = Vector3.Lerp (smoothedPlanarVelocity, velocity, velT);
        }

        if (smoothedPlanarVelocity.sqrMagnitude < minHeadingSpeed * minHeadingSpeed) {
            return;
        }

        Quaternion desiredRotation = Quaternion.LookRotation (smoothedPlanarVelocity.normalized, Vector3.up);
        Quaternion nextRotation = desiredRotation;

        if (headingSmooth > 0f) {
            float t = 1f - Mathf.Exp (-headingSmooth * dt);
            nextRotation = Quaternion.Slerp (transform.rotation, desiredRotation, t);
        }

        if (maxYawSpeedDeg > 0f) {
            nextRotation = Quaternion.RotateTowards (transform.rotation, nextRotation, maxYawSpeedDeg * dt);
        }

        transform.rotation = nextRotation;
    }
}
