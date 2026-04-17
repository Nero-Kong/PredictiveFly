using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Boid : MonoBehaviour {
    const float MinDirectionSpeed = 1e-5f;

    BoidSettings settings;

    // State
    [HideInInspector]
    public Vector3 position;
    [HideInInspector]
    public Vector3 forward;
    Vector3 velocity;

    // To update:
    Vector3 acceleration;
    [HideInInspector]
    public Vector3 avgFlockHeading;
    [HideInInspector]
    public Vector3 avgAvoidanceHeading;
    [HideInInspector]
    public Vector3 centreOfFlockmates;
    [HideInInspector]
    public int numPerceivedFlockmates;

    // Cached
    Material material;
    Transform cachedTransform;
    Transform target;
    Vector3 smoothedTargetPosition;
    bool hasSmoothedTargetPosition;
    int avoidRayStartIndex;

    void Awake () {
        material = transform.GetComponentInChildren<MeshRenderer> ().material;
        cachedTransform = transform;
    }

    public void Initialize (BoidSettings settings, Transform target) {
        this.target = target;
        this.settings = settings;

        position = cachedTransform.position;
        forward = cachedTransform.forward;
        smoothedTargetPosition = target != null ? target.position : position;
        hasSmoothedTargetPosition = target != null;
        avoidRayStartIndex = Random.Range (0, BoidHelper.directions.Length);

        float startSpeed = (settings.minSpeed + settings.maxSpeed) / 2;
        velocity = transform.forward * startSpeed;
    }

    public void SetTarget (Transform target) {
        this.target = target;
        hasSmoothedTargetPosition = false;
    }

    public void SetColour (Color col) {
        if (material != null) {
            material.color = col;
        }
    }

    public void UpdateBoid () {
        Vector3 acceleration = Vector3.zero;

        if (target != null) {
            float smoothing = Mathf.Max (0f, settings.targetPositionSmoothing);
            if (!hasSmoothedTargetPosition || smoothing <= 0f) {
                smoothedTargetPosition = target.position;
                hasSmoothedTargetPosition = true;
            } else {
                float t = 1f - Mathf.Exp (-smoothing * Time.deltaTime);
                smoothedTargetPosition = Vector3.Lerp (smoothedTargetPosition, target.position, t);
            }

            Vector3 offsetToTarget = smoothedTargetPosition - position;
            if (offsetToTarget.sqrMagnitude > Mathf.Epsilon) {
                acceleration = SteerTowards (offsetToTarget) * settings.targetWeight;
            }
        }

        if (numPerceivedFlockmates != 0) {
            centreOfFlockmates /= numPerceivedFlockmates;

            Vector3 offsetToFlockmatesCentre = (centreOfFlockmates - position);

            var alignmentForce = SteerTowards (avgFlockHeading) * settings.alignWeight;
            var cohesionForce = SteerTowards (offsetToFlockmatesCentre) * settings.cohesionWeight;
            var seperationForce = SteerTowards (avgAvoidanceHeading) * settings.seperateWeight;

            acceleration += alignmentForce;
            acceleration += cohesionForce;
            acceleration += seperationForce;
        }

        ApplyBoundsSteering (ref acceleration);

        if (IsHeadingForCollision ()) {
            Vector3 collisionAvoidDir = ObstacleRays ();
            Vector3 collisionAvoidForce = SteerTowards (collisionAvoidDir) * settings.avoidCollisionWeight;
            acceleration += collisionAvoidForce;
        }

        if (settings.maxAcceleration > 0f) {
            acceleration = Vector3.ClampMagnitude (acceleration, settings.maxAcceleration);
        }

        velocity += acceleration * Time.deltaTime;
        float speed = velocity.magnitude;
        Vector3 dir;
        if (speed > MinDirectionSpeed) {
            dir = velocity / speed;
        } else {
            dir = forward.sqrMagnitude > Mathf.Epsilon ? forward : cachedTransform.forward;
            speed = 0f;
        }

        speed = Mathf.Clamp (speed, settings.minSpeed, settings.maxSpeed);
        velocity = dir * speed;

        cachedTransform.position += velocity * Time.deltaTime;
        if (settings.constrainToBounds && settings.hardClampToBounds) {
            cachedTransform.position = ClampToBounds (cachedTransform.position);
        }
        cachedTransform.forward = dir;
        position = cachedTransform.position;
        forward = dir;
    }

    void ApplyBoundsSteering (ref Vector3 acceleration) {
        if (!settings.constrainToBounds) {
            return;
        }

        Vector3 extents = settings.boundsExtents;
        if (extents.x <= 0f || extents.y <= 0f || extents.z <= 0f) {
            return;
        }

        Vector3 localOffset = position - settings.boundsCenter;
        Vector3 push = new Vector3 (
            ComputeAxisBoundsPush (localOffset.x, extents.x),
            ComputeAxisBoundsPush (localOffset.y, extents.y),
            ComputeAxisBoundsPush (localOffset.z, extents.z)
        );

        if (push.sqrMagnitude > Mathf.Epsilon) {
            acceleration += SteerTowards (push) * settings.boundsSteerWeight;
        }
    }

    float ComputeAxisBoundsPush (float axisOffset, float extent) {
        float absOffset = Mathf.Abs (axisOffset);
        float softStart = extent * Mathf.Clamp (settings.boundsSoftZone, 0f, 0.99f);
        if (absOffset <= softStart) {
            return 0f;
        }

        float zone = Mathf.Max (extent - softStart, 1e-4f);
        float t = Mathf.Clamp01 ((absOffset - softStart) / zone);
        return -Mathf.Sign (axisOffset) * t;
    }

    Vector3 ClampToBounds (Vector3 worldPosition) {
        Vector3 c = settings.boundsCenter;
        Vector3 e = settings.boundsExtents;
        return new Vector3 (
            Mathf.Clamp (worldPosition.x, c.x - e.x, c.x + e.x),
            Mathf.Clamp (worldPosition.y, c.y - e.y, c.y + e.y),
            Mathf.Clamp (worldPosition.z, c.z - e.z, c.z + e.z)
        );
    }

    bool IsHeadingForCollision () {
        if (settings.obstacleMask.value == 0) {
            return false;
        }

        RaycastHit hit;
        if (Physics.SphereCast (position, settings.boundsRadius, forward, out hit, settings.collisionAvoidDst, settings.obstacleMask)) {
            return true;
        } else { }
        return false;
    }

    Vector3 ObstacleRays () {
        Vector3[] rayDirections = BoidHelper.directions;
        int rayCount = Mathf.Clamp (settings.maxAvoidanceRays, 1, rayDirections.Length);

        for (int i = 0; i < rayCount; i++) {
            int rayIndex = (avoidRayStartIndex + i) % rayDirections.Length;
            Vector3 dir = cachedTransform.TransformDirection (rayDirections[rayIndex]);
            Ray ray = new Ray (position, dir);
            if (!Physics.SphereCast (ray, settings.boundsRadius, settings.collisionAvoidDst, settings.obstacleMask)) {
                avoidRayStartIndex = (rayIndex + 1) % rayDirections.Length;
                return dir;
            }
        }

        avoidRayStartIndex = (avoidRayStartIndex + rayCount) % rayDirections.Length;

        return forward;
    }

    Vector3 SteerTowards (Vector3 vector) {
        Vector3 v = vector.normalized * settings.maxSpeed - velocity;
        return Vector3.ClampMagnitude (v, settings.maxSteerForce);
    }

}
