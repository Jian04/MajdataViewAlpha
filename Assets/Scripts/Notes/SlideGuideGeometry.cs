using System;
using System.Collections.Generic;
using UnityEngine;

// Sample the authored polyline directly, retaining every corner. Sampling the
// sparse bar positions a second time cuts across v/s/z corners.
internal static class SlideGuideGeometry
{
    public static float[] Distances(IReadOnlyList<Vector3> points)
    {
        var result = new float[points.Count];
        for (int i = 1; i < points.Count; i++) result[i] = result[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        return result;
    }

    public static (Vector3 Position, Vector3 Tangent) Evaluate(Vector3[] points, float[] distances, float progress)
    {
        if (points.Length < 2) return (points.Length == 0 ? Vector3.zero : points[0], Vector3.right);
        float target = Mathf.Clamp01(progress) * distances[distances.Length - 1];
        int upper = Array.BinarySearch(distances, target);
        if (upper < 0) upper = ~upper;
        upper = Math.Max(1, Math.Min(points.Length - 1, upper));
        int lower = upper - 1;
        float span = distances[upper] - distances[lower];
        var tangent = points[upper] - points[lower];
        return (Vector3.Lerp(points[lower], points[upper], span > 0.000001f ? (target - distances[lower]) / span : 0f), tangent);
    }
}
