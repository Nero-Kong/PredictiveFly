using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu]
public class BoidSettings : ScriptableObject {
    // Settings
    public float minSpeed = 2;
    public float maxSpeed = 5;
    public float perceptionRadius = 2.5f;
    public float avoidanceRadius = 1;
    public float maxSteerForce = 3;

    public float alignWeight = 1;
    public float cohesionWeight = 1;
    public float seperateWeight = 1;

    public float targetWeight = 1;

    [Header ("Collisions")]
    public LayerMask obstacleMask;
    public float boundsRadius = .27f;
    public float avoidCollisionWeight = 10;
    public float collisionAvoidDst = 5;

    [Header ("World Bounds")]
    public bool constrainToBounds = false;
    public Vector3 boundsCenter = Vector3.zero;
    public Vector3 boundsExtents = new Vector3 (20f, 8f, 20f);
    [Range (0f, 0.99f)] public float boundsSoftZone = 0.8f;
    public float boundsSteerWeight = 8f;
    public bool hardClampToBounds = true;

    [Header ("Stability")]
    [Tooltip ("Smooths fast target jitter (for example hand-joint noise). 0 disables smoothing.")]
    public float targetPositionSmoothing = 16;
    [Tooltip ("Hard clamp on total acceleration. 0 disables clamping.")]
    public float maxAcceleration = 0;
    [Tooltip ("Max rays checked while searching a collision-free heading each frame.")]
    public int maxAvoidanceRays = 96;
    [Tooltip ("Lower bound for squared distance in separation force to avoid divide-by-zero spikes.")]
    public float separationEpsilonSqr = 1e-4f;

}
