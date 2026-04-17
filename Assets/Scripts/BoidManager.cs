using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoidManager : MonoBehaviour {

    const int threadGroupSize = 1024;

    [Header ("Settings Asset (defaults source)")]
    public BoidSettings settings;
    public Transform target;
    public ComputeShader compute;

    [Header ("Live Boid Settings (editable at runtime)")]
    public float minSpeed = 2;
    public float maxSpeed = 5;
    public float perceptionRadius = 2.5f;
    public float avoidanceRadius = 1;
    public float maxSteerForce = 3;

    public float alignWeight = 1;
    public float cohesionWeight = 1;
    public float seperateWeight = 1;
    public float targetWeight = 1;

    public LayerMask obstacleMask;
    public float boundsRadius = .27f;
    public float avoidCollisionWeight = 10;
    public float collisionAvoidDst = 5;
    public bool constrainToBounds = false;
    public bool useManagerPositionAsBoundsCenter = true;
    public Vector3 boundsCenter = Vector3.zero;
    public Vector3 boundsExtents = new Vector3 (20f, 8f, 20f);
    [Range (0f, 0.99f)] public float boundsSoftZone = 0.8f;
    public float boundsSteerWeight = 8f;
    public bool hardClampToBounds = true;
    public float targetPositionSmoothing = 16;
    public float maxAcceleration = 0;
    public int maxAvoidanceRays = 96;
    public float separationEpsilonSqr = 1e-4f;

    Boid[] boids;
    Transform currentAppliedTarget;
    BoidSettings runtimeSettings;
    BoidData[] boidDataCache = Array.Empty<BoidData> ();
    ComputeBuffer boidBuffer;
    int boidBufferCount;

    void Awake () {
        EnsureRuntimeSettings ();
        SyncManagerFieldsFromSettings (runtimeSettings);
    }

    void Start () {
        EnsureRuntimeSettings ();
        ApplyManagerFieldsToRuntimeSettings ();

        boids = FindObjectsByType<Boid>(FindObjectsSortMode.None);
        foreach (Boid b in boids) {
            b.Initialize (runtimeSettings, target);
        }
        currentAppliedTarget = target;

    }

    void Update () {
        if (boids != null) {
            ApplyManagerFieldsToRuntimeSettings ();

            RemoveNullBoids();
            if (boids == null || boids.Length == 0) {
                ReleaseComputeBuffer ();
                return;
            }

            if (target != currentAppliedTarget) {
                ApplyTargetToBoids (target);
            }

            int numBoids = boids.Length;
            EnsureBoidDataCapacity (numBoids);
            EnsureComputeBuffer (numBoids);

            for (int i = 0; i < numBoids; i++) {
                boidDataCache[i].position = boids[i].position;
                boidDataCache[i].direction = boids[i].forward;
            }

            boidBuffer.SetData (boidDataCache, 0, 0, numBoids);

            compute.SetBuffer (0, "boids", boidBuffer);
            compute.SetInt ("numBoids", numBoids);
            compute.SetFloat ("viewRadius", runtimeSettings.perceptionRadius);
            compute.SetFloat ("avoidRadius", runtimeSettings.avoidanceRadius);
            compute.SetFloat ("separationEpsilonSqr", runtimeSettings.separationEpsilonSqr);

            int threadGroups = Mathf.CeilToInt (numBoids / (float) threadGroupSize);
            compute.Dispatch (0, threadGroups, 1, 1);

            boidBuffer.GetData (boidDataCache, 0, 0, numBoids);

            for (int i = 0; i < numBoids; i++) {
                boids[i].avgFlockHeading = boidDataCache[i].flockHeading;
                boids[i].centreOfFlockmates = boidDataCache[i].flockCentre;
                boids[i].avgAvoidanceHeading = boidDataCache[i].avoidanceHeading;
                boids[i].numPerceivedFlockmates = boidDataCache[i].numFlockmates;

                boids[i].UpdateBoid ();
            }
        }
    }

    void OnDisable () {
        ReleaseComputeBuffer ();
    }

    void OnDestroy () {
        ReleaseComputeBuffer ();
    }

    [ContextMenu ("Load Live Fields From Settings Asset")]
    public void LoadLiveFieldsFromSettingsAsset () {
        if (settings != null) {
            SyncManagerFieldsFromSettings (settings);
        }
    }

    [ContextMenu ("Save Live Fields To Settings Asset")]
    public void SaveLiveFieldsToSettingsAsset () {
        if (settings != null) {
            CopyFieldsToSettings (settings);
        }
    }

    void EnsureRuntimeSettings () {
        if (runtimeSettings != null) {
            return;
        }

        runtimeSettings = settings != null
            ? Instantiate (settings)
            : ScriptableObject.CreateInstance<BoidSettings> ();
    }

    void SyncManagerFieldsFromSettings (BoidSettings src) {
        if (src == null) {
            return;
        }

        minSpeed = src.minSpeed;
        maxSpeed = src.maxSpeed;
        perceptionRadius = src.perceptionRadius;
        avoidanceRadius = src.avoidanceRadius;
        maxSteerForce = src.maxSteerForce;
        alignWeight = src.alignWeight;
        cohesionWeight = src.cohesionWeight;
        seperateWeight = src.seperateWeight;
        targetWeight = src.targetWeight;
        obstacleMask = src.obstacleMask;
        boundsRadius = src.boundsRadius;
        avoidCollisionWeight = src.avoidCollisionWeight;
        collisionAvoidDst = src.collisionAvoidDst;
        constrainToBounds = src.constrainToBounds;
        boundsCenter = src.boundsCenter;
        boundsExtents = src.boundsExtents;
        boundsSoftZone = src.boundsSoftZone;
        boundsSteerWeight = src.boundsSteerWeight;
        hardClampToBounds = src.hardClampToBounds;
        targetPositionSmoothing = src.targetPositionSmoothing;
        maxAcceleration = src.maxAcceleration;
        maxAvoidanceRays = src.maxAvoidanceRays;
        separationEpsilonSqr = src.separationEpsilonSqr;
    }

    void CopyFieldsToSettings (BoidSettings dst) {
        float safeMinSpeed = Mathf.Max (0f, minSpeed);
        float safeMaxSpeed = Mathf.Max (safeMinSpeed, maxSpeed);

        dst.minSpeed = safeMinSpeed;
        dst.maxSpeed = safeMaxSpeed;
        dst.perceptionRadius = Mathf.Max (0f, perceptionRadius);
        dst.avoidanceRadius = Mathf.Max (0f, avoidanceRadius);
        dst.maxSteerForce = Mathf.Max (0f, maxSteerForce);
        dst.alignWeight = alignWeight;
        dst.cohesionWeight = cohesionWeight;
        dst.seperateWeight = seperateWeight;
        dst.targetWeight = targetWeight;
        dst.obstacleMask = obstacleMask;
        dst.boundsRadius = Mathf.Max (0f, boundsRadius);
        dst.avoidCollisionWeight = Mathf.Max (0f, avoidCollisionWeight);
        dst.collisionAvoidDst = Mathf.Max (0f, collisionAvoidDst);
        dst.constrainToBounds = constrainToBounds;
        dst.boundsCenter = boundsCenter;
        dst.boundsExtents = new Vector3 (
            Mathf.Max (0.01f, boundsExtents.x),
            Mathf.Max (0.01f, boundsExtents.y),
            Mathf.Max (0.01f, boundsExtents.z)
        );
        dst.boundsSoftZone = Mathf.Clamp (boundsSoftZone, 0f, 0.99f);
        dst.boundsSteerWeight = Mathf.Max (0f, boundsSteerWeight);
        dst.hardClampToBounds = hardClampToBounds;
        dst.targetPositionSmoothing = Mathf.Max (0f, targetPositionSmoothing);
        dst.maxAcceleration = Mathf.Max (0f, maxAcceleration);
        dst.maxAvoidanceRays = Mathf.Max (1, maxAvoidanceRays);
        dst.separationEpsilonSqr = Mathf.Max (1e-8f, separationEpsilonSqr);
    }

    void ApplyManagerFieldsToRuntimeSettings () {
        EnsureRuntimeSettings ();

        if (useManagerPositionAsBoundsCenter) {
            boundsCenter = transform.position;
        }

        CopyFieldsToSettings (runtimeSettings);
    }

    void ApplyTargetToBoids (Transform newTarget) {
        currentAppliedTarget = newTarget;
        for (int i = 0; i < boids.Length; i++) {
            if (boids[i] != null) {
                boids[i].SetTarget (newTarget);
            }
        }
    }

    public void SetTarget (Transform newTarget) {
        target = newTarget;
        if (boids != null && boids.Length > 0) {
            ApplyTargetToBoids (newTarget);
        }
    }

    public bool TryGetFlockCentroid (out Vector3 centroid) {
        centroid = Vector3.zero;
        if (boids == null || boids.Length == 0) {
            return false;
        }

        int count = 0;
        for (int i = 0; i < boids.Length; i++) {
            Boid b = boids[i];
            if (b != null && b.isActiveAndEnabled) {
                centroid += b.position;
                count++;
            }
        }

        if (count == 0) {
            centroid = Vector3.zero;
            return false;
        }

        centroid /= count;
        return true;
    }

    void RemoveNullBoids () {
        if (boids == null) {
            return;
        }

        int count = 0;
        for (int i = 0; i < boids.Length; i++) {
            Boid b = boids[i];
            if (b != null) {
                boids[count++] = b;
            }
        }

        if (count == 0) {
            boids = null;
        } else if (count != boids.Length) {
            Boid[] trimmed = new Boid[count];
            Array.Copy(boids, trimmed, count);
            boids = trimmed;
        }
    }

    void EnsureBoidDataCapacity (int count) {
        if (boidDataCache == null || boidDataCache.Length != count) {
            boidDataCache = new BoidData[count];
        }
    }

    void EnsureComputeBuffer (int count) {
        if (boidBuffer != null && boidBufferCount == count) {
            return;
        }

        ReleaseComputeBuffer ();
        boidBuffer = new ComputeBuffer (count, BoidData.Size);
        boidBufferCount = count;
    }

    void ReleaseComputeBuffer () {
        if (boidBuffer != null) {
            boidBuffer.Release ();
            boidBuffer = null;
            boidBufferCount = 0;
        }
    }

    public struct BoidData {
        public Vector3 position;
        public Vector3 direction;

        public Vector3 flockHeading;
        public Vector3 flockCentre;
        public Vector3 avoidanceHeading;
        public int numFlockmates;

        public static int Size {
            get {
                return sizeof (float) * 3 * 5 + sizeof (int);
            }
        }
    }
}

