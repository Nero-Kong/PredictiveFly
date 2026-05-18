using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoidManager : MonoBehaviour {

    const int threadGroupSize = 1024;

    public BoidSettings settings;
    public ComputeShader compute;

    [HideInInspector] public Transform target;
    [HideInInspector] public float minSpeed = 2;
    [HideInInspector] public float maxSpeed = 5;
    [HideInInspector] public float perceptionRadius = 2.5f;
    [HideInInspector] public float avoidanceRadius = 1;
    [HideInInspector] public float maxSteerForce = 3;
    [HideInInspector] public float alignWeight = 1;
    [HideInInspector] public float cohesionWeight = 1;
    [HideInInspector] public float seperateWeight = 1;
    [HideInInspector] public float targetWeight = 1;
    [HideInInspector] public float avoidCollisionWeight = 10;
    [HideInInspector] public float collisionAvoidDst = 5;

    Boid[] boids;

    void OnValidate () {
        SyncCompatibilityFieldsFromSettings ();
    }

    void Start () {
        SyncCompatibilityFieldsFromSettings ();

        boids = FindObjectsOfType<Boid> ();
        foreach (Boid b in boids) {
            b.Initialize (settings, null);
        }

    }

    void Update () {
        if (boids != null && boids.Length > 0 && settings != null && compute != null) {

            int numBoids = boids.Length;
            var boidData = new BoidData[numBoids];

            for (int i = 0; i < boids.Length; i++) {
                boidData[i].position = boids[i].position;
                boidData[i].direction = boids[i].forward;
            }

            var boidBuffer = new ComputeBuffer (numBoids, BoidData.Size);
            boidBuffer.SetData (boidData);

            compute.SetBuffer (0, "boids", boidBuffer);
            compute.SetInt ("numBoids", boids.Length);
            compute.SetFloat ("viewRadius", settings.perceptionRadius);
            compute.SetFloat ("avoidRadius", settings.avoidanceRadius);

            int threadGroups = Mathf.CeilToInt (numBoids / (float) threadGroupSize);
            compute.Dispatch (0, threadGroups, 1, 1);

            boidBuffer.GetData (boidData);

            for (int i = 0; i < boids.Length; i++) {
                boids[i].avgFlockHeading = boidData[i].flockHeading;
                boids[i].centreOfFlockmates = boidData[i].flockCentre;
                boids[i].avgAvoidanceHeading = boidData[i].avoidanceHeading;
                boids[i].numPerceivedFlockmates = boidData[i].numFlockmates;

                boids[i].UpdateBoid ();
            }

            boidBuffer.Release ();
        }
    }

    public void SetTarget (Transform newTarget) {
        target = newTarget;
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

    void SyncCompatibilityFieldsFromSettings () {
        if (settings == null) {
            return;
        }

        minSpeed = settings.minSpeed;
        maxSpeed = settings.maxSpeed;
        perceptionRadius = settings.perceptionRadius;
        avoidanceRadius = settings.avoidanceRadius;
        maxSteerForce = settings.maxSteerForce;
        alignWeight = settings.alignWeight;
        cohesionWeight = settings.cohesionWeight;
        seperateWeight = settings.seperateWeight;
        targetWeight = settings.targetWeight;
        avoidCollisionWeight = settings.avoidCollisionWeight;
        collisionAvoidDst = settings.collisionAvoidDst;
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
