using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime boundary test for the hollow 3D iraira-bou tube.
/// The visible mesh is hollow; this component provides precise wall contact checks for probes.
/// </summary>
public class IrairaBouTubeBoundary : MonoBehaviour
{
    public Vector3[] centerline = new Vector3[0];
    public float wallRadius = 1f;
    public float[] radiusProfile = new float[0];
    [SerializeField] float[] cumulativeDistances = new float[0];
    [SerializeField] float totalCenterlineLength;
    public float contactTolerance = 0.015f;
    public string hazardLabel = "Transparent tube wall";

    public float TotalCenterlineLength
    {
        get
        {
            EnsureDistanceCache();
            return totalCenterlineLength;
        }
    }

    public void Configure(IReadOnlyList<Vector3> path, float radius)
    {
        float safeRadius = Mathf.Max(0.01f, radius);
        float[] uniformProfile = new float[path.Count];
        for (int i = 0; i < uniformProfile.Length; i++)
        {
            uniformProfile[i] = safeRadius;
        }

        Configure(path, uniformProfile, safeRadius);
    }

    public void Configure(IReadOnlyList<Vector3> path, IReadOnlyList<float> radii, float nominalRadius)
    {
        centerline = new Vector3[path.Count];
        radiusProfile = new float[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            centerline[i] = path[i];
            float radius = radii != null && i < radii.Count ? radii[i] : nominalRadius;
            radiusProfile[i] = Mathf.Max(0.01f, radius);
        }

        wallRadius = Mathf.Max(0.01f, nominalRadius);
        RebuildDistanceCache();
    }

    public bool IsTouchingWall(Vector3 point, float probeRadius, out Vector3 nearestPoint, out float radialDistance)
    {
        nearestPoint = point;
        radialDistance = 0f;

        if (!TryGetNearestPoint(point, out nearestPoint, out radialDistance, out float localRadius))
        {
            return false;
        }

        float allowedCenterDistance = Mathf.Max(0f, localRadius - Mathf.Max(0f, probeRadius));
        return radialDistance >= allowedCenterDistance - contactTolerance;
    }

    public bool TryGetWallClearance(
        Vector3 point,
        float probeRadius,
        out Vector3 nearestPoint,
        out float radialDistance,
        out float localRadius,
        out float clearance)
    {
        nearestPoint = point;
        radialDistance = 0f;
        localRadius = wallRadius;
        clearance = float.PositiveInfinity;

        if (!TryGetNearestPoint(point, out nearestPoint, out radialDistance, out localRadius))
        {
            return false;
        }

        float allowedCenterDistance = Mathf.Max(0f, localRadius - Mathf.Max(0f, probeRadius));
        clearance = allowedCenterDistance - radialDistance;
        return true;
    }

    public bool TryGetRouteProgress(
        Vector3 point,
        out float normalizedProgress,
        out float distanceAlongRoute,
        out float totalRouteLength,
        out Vector3 nearestPoint,
        out float radialDistance,
        out float localRadius)
    {
        normalizedProgress = 0f;
        distanceAlongRoute = 0f;
        totalRouteLength = 0f;
        nearestPoint = point;
        radialDistance = 0f;
        localRadius = wallRadius;

        if (!TryGetNearestPoint(
            point,
            out nearestPoint,
            out radialDistance,
            out localRadius,
            out int segmentIndex,
            out float segmentT))
        {
            return false;
        }

        EnsureDistanceCache();
        totalRouteLength = totalCenterlineLength;
        if (cumulativeDistances == null || cumulativeDistances.Length != centerline.Length)
        {
            return false;
        }

        int a = Mathf.Clamp(segmentIndex, 0, cumulativeDistances.Length - 1);
        int b = Mathf.Clamp(segmentIndex + 1, 0, cumulativeDistances.Length - 1);
        distanceAlongRoute = Mathf.Lerp(cumulativeDistances[a], cumulativeDistances[b], Mathf.Clamp01(segmentT));
        normalizedProgress = totalRouteLength > 1e-5f
            ? Mathf.Clamp01(distanceAlongRoute / totalRouteLength)
            : 0f;
        return true;
    }

    public bool IsNearRouteEnd(Vector3 point, float radius)
    {
        if (centerline == null || centerline.Length == 0)
        {
            return false;
        }

        return Vector3.Distance(point, centerline[centerline.Length - 1]) <= Mathf.Max(0.01f, radius);
    }

    public bool TryConstrainInside(Vector3 point, float probeRadius, out Vector3 constrainedPoint)
    {
        constrainedPoint = point;

        if (!TryGetNearestPoint(point, out Vector3 nearestPoint, out float radialDistance, out float localRadius))
        {
            return false;
        }

        float allowedCenterDistance = Mathf.Max(0f, localRadius - Mathf.Max(0f, probeRadius) - Mathf.Max(0f, contactTolerance));
        if (radialDistance <= allowedCenterDistance)
        {
            return false;
        }

        Vector3 radial = point - nearestPoint;
        constrainedPoint = radial.sqrMagnitude > 1e-8f
            ? nearestPoint + radial.normalized * allowedCenterDistance
            : nearestPoint;
        return true;
    }

    bool TryGetNearestPoint(Vector3 point, out Vector3 nearestPoint, out float radialDistance)
    {
        return TryGetNearestPoint(point, out nearestPoint, out radialDistance, out _);
    }

    bool TryGetNearestPoint(Vector3 point, out Vector3 nearestPoint, out float radialDistance, out float localRadius)
    {
        return TryGetNearestPoint(
            point,
            out nearestPoint,
            out radialDistance,
            out localRadius,
            out _,
            out _);
    }

    bool TryGetNearestPoint(
        Vector3 point,
        out Vector3 nearestPoint,
        out float radialDistance,
        out float localRadius,
        out int nearestSegment,
        out float nearestSegmentT)
    {
        nearestPoint = point;
        radialDistance = 0f;
        localRadius = wallRadius;
        nearestSegment = 0;
        nearestSegmentT = 0f;

        if (centerline == null || centerline.Length < 2)
        {
            return false;
        }

        float bestSqrDistance = float.PositiveInfinity;
        Vector3 bestPoint = centerline[0];
        int bestSegment = 0;
        float bestSegmentT = 0f;

        for (int i = 0; i < centerline.Length - 1; i++)
        {
            Vector3 candidate = ClosestPointOnSegment(centerline[i], centerline[i + 1], point, out float segmentT);
            float sqrDistance = (point - candidate).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                bestPoint = candidate;
                bestSegment = i;
                bestSegmentT = segmentT;
            }
        }

        nearestPoint = bestPoint;
        radialDistance = Mathf.Sqrt(bestSqrDistance);
        localRadius = GetRadiusAtSegment(bestSegment, bestSegmentT);
        nearestSegment = bestSegment;
        nearestSegmentT = bestSegmentT;
        return true;
    }

    void EnsureDistanceCache()
    {
        if (centerline == null || centerline.Length < 2)
        {
            cumulativeDistances = new float[0];
            totalCenterlineLength = 0f;
            return;
        }

        if (cumulativeDistances == null
            || cumulativeDistances.Length != centerline.Length
            || totalCenterlineLength <= 0f)
        {
            RebuildDistanceCache();
        }
    }

    void RebuildDistanceCache()
    {
        if (centerline == null || centerline.Length == 0)
        {
            cumulativeDistances = new float[0];
            totalCenterlineLength = 0f;
            return;
        }

        cumulativeDistances = new float[centerline.Length];
        totalCenterlineLength = 0f;
        for (int i = 1; i < centerline.Length; i++)
        {
            totalCenterlineLength += Vector3.Distance(centerline[i - 1], centerline[i]);
            cumulativeDistances[i] = totalCenterlineLength;
        }
    }

    float GetRadiusAtSegment(int segmentIndex, float t)
    {
        if (radiusProfile == null || radiusProfile.Length != centerline.Length)
        {
            return wallRadius;
        }

        int a = Mathf.Clamp(segmentIndex, 0, radiusProfile.Length - 1);
        int b = Mathf.Clamp(segmentIndex + 1, 0, radiusProfile.Length - 1);
        return Mathf.Lerp(radiusProfile[a], radiusProfile[b], Mathf.Clamp01(t));
    }

    static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point, out float t)
    {
        Vector3 ab = b - a;
        float sqrMagnitude = ab.sqrMagnitude;
        if (sqrMagnitude <= 1e-8f)
        {
            t = 0f;
            return a;
        }

        t = Vector3.Dot(point - a, ab) / sqrMagnitude;
        t = Mathf.Clamp01(t);
        return a + Mathf.Clamp01(t) * ab;
    }
}
