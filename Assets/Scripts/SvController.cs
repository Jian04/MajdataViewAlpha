using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Global Scroll Velocity controller.
/// Stores the chart's SV table and provides cumulative scroll-distance queries.
/// distance = 4.8 - speed * (noteScrollPos - GetCumulativeScroll(audioTime))
/// </summary>
public static class SvController
{
    private sealed class Curve
    {
        public double[] Times = System.Array.Empty<double>();
        public double[] Cumulatives = System.Array.Empty<double>();
        public double[] MaxCumulatives = System.Array.Empty<double>();
        public float[] Multipliers = System.Array.Empty<float>();
    }

    // Breakpoint times in ascending order
    private static double[] _times      = System.Array.Empty<double>();
    // Cumulative value at each breakpoint: integral [0 to t_i] sv(tau) dtau
    private static double[] _cumulatives = System.Array.Empty<double>();
    private static double[] _maxCumulatives = System.Array.Empty<double>();
    // SV multiplier from this breakpoint until the next
    private static float[]  _multipliers = System.Array.Empty<float>();
    private static readonly Dictionary<string, Curve> TypedCurves = new();
    private static readonly Dictionary<string, Curve> TypedOnlyCurves = new();

    public static bool IsEmpty => _times.Length == 0;
    public static bool HasTypedCurve(string noteType) =>
        !string.IsNullOrWhiteSpace(noteType) && TypedCurves.ContainsKey(noteType.ToLowerInvariant());

    /// <summary>
    /// Initializes from the chart's SV points, sorted by time.
    /// chartStartTime is the chart start; SV defaults to 1.0 before the first point.
    /// </summary>
    public static void Load(List<SvPoint> points, double chartStartTime = 0.0)
    {
        points ??= new List<SvPoint>();
        var globalPoints = points.Where(point => string.IsNullOrWhiteSpace(point.noteType)).ToList();
        // Like <HS*>: carry forward the last SV value set before the start point.
        float effectiveSv = 1.0f;
        foreach (var p in globalPoints.OrderBy(p => p.time))
            if (p.time <= chartStartTime)
                effectiveSv = p.multiplier;

        var sorted = new List<SvPoint> { new SvPoint { time = chartStartTime, multiplier = effectiveSv } };
        foreach (var p in globalPoints.OrderBy(p => p.time))
            if (p.time > chartStartTime)
                sorted.Add(p);

        int n = sorted.Count;
        _times       = new double[n];
        _cumulatives = new double[n];
        _maxCumulatives = new double[n];
        _multipliers = new float[n];

        _times[0]       = sorted[0].time;
        _cumulatives[0] = 0.0;
        _maxCumulatives[0] = 0.0;
        _multipliers[0] = sorted[0].multiplier;

        for (int i = 1; i < n; i++)
        {
            _times[i]       = sorted[i].time;
            _multipliers[i] = sorted[i].multiplier;
            // cumulative up to this breakpoint
            _cumulatives[i] = _cumulatives[i - 1]
                               + _multipliers[i - 1] * (_times[i] - _times[i - 1]);
            _maxCumulatives[i] = System.Math.Max(
                _maxCumulatives[i - 1],
                _cumulatives[i]);
        }

        TypedCurves.Clear();
        TypedOnlyCurves.Clear();
        foreach (var noteType in points
                     .Where(point => !string.IsNullOrWhiteSpace(point.noteType))
                     .Select(point => point.noteType.ToLowerInvariant())
                     .Distinct())
        {
            TypedCurves[noteType] = BuildTypedCurve(points, noteType, chartStartTime);
            TypedOnlyCurves[noteType] = BuildTypedOnlyCurve(points, noteType, chartStartTime);
        }
    }

    private static Curve BuildTypedOnlyCurve(List<SvPoint> points, string noteType, double chartStartTime)
    {
        var typedPoints = points
            .Where(point => string.Equals(point.noteType, noteType,
                System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(point => point.time)
            .ToList();
        var effective = 1f;
        foreach (var point in typedPoints)
            if (point.time <= chartStartTime)
                effective = point.reset ? 1f : point.multiplier;

        var entries = new List<(double Time, float Multiplier)> { (chartStartTime, effective) };
        foreach (var point in typedPoints.Where(point => point.time > chartStartTime))
        {
            effective = point.reset ? 1f : point.multiplier;
            if (entries.Count > 0 && System.Math.Abs(entries[^1].Time - point.time) < 0.000001d)
                entries[^1] = (point.time, effective);
            else
                entries.Add((point.time, effective));
        }
        return BuildCurve(entries);
    }

    private static Curve BuildCurve(IReadOnlyList<(double Time, float Multiplier)> entries)
    {
        var curve = new Curve
        {
            Times = entries.Select(entry => entry.Time).ToArray(),
            Multipliers = entries.Select(entry => entry.Multiplier).ToArray(),
            Cumulatives = new double[entries.Count],
            MaxCumulatives = new double[entries.Count]
        };
        for (var index = 1; index < entries.Count; index++)
        {
            curve.Cumulatives[index] = curve.Cumulatives[index - 1] +
                curve.Multipliers[index - 1] * (curve.Times[index] - curve.Times[index - 1]);
            curve.MaxCumulatives[index] = System.Math.Max(
                curve.MaxCumulatives[index - 1], curve.Cumulatives[index]);
        }
        return curve;
    }

    private static Curve BuildTypedCurve(List<SvPoint> points, string noteType, double chartStartTime)
    {
        float? typeOverride = null;
        foreach (var point in points.OrderBy(point => point.time))
        {
            if (point.time > chartStartTime ||
                !string.Equals(point.noteType, noteType, System.StringComparison.OrdinalIgnoreCase))
                continue;
            typeOverride = point.reset ? null : point.multiplier;
        }

        var effective = typeOverride ?? GetCurrentSV(chartStartTime);
        var entries = new List<(double Time, float Multiplier)> { (chartStartTime, effective) };
        foreach (var point in points
                     .Where(point => point.time > chartStartTime &&
                                     (string.IsNullOrWhiteSpace(point.noteType) ||
                                      string.Equals(point.noteType, noteType,
                                          System.StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(point => point.time))
        {
            if (string.IsNullOrWhiteSpace(point.noteType))
            {
                if (!typeOverride.HasValue)
                    effective = point.multiplier;
            }
            else
            {
                typeOverride = point.reset ? null : point.multiplier;
                effective = typeOverride ?? GetCurrentSV(point.time);
            }

            if (entries.Count > 0 && System.Math.Abs(entries[^1].Time - point.time) < 0.000001d)
                entries[^1] = (point.time, effective);
            else
                entries.Add((point.time, effective));
        }

        var curve = new Curve
        {
            Times = entries.Select(entry => entry.Time).ToArray(),
            Multipliers = entries.Select(entry => entry.Multiplier).ToArray(),
            Cumulatives = new double[entries.Count],
            MaxCumulatives = new double[entries.Count]
        };
        for (var index = 1; index < entries.Count; index++)
        {
            curve.Cumulatives[index] = curve.Cumulatives[index - 1] +
                curve.Multipliers[index - 1] * (curve.Times[index] - curve.Times[index - 1]);
            curve.MaxCumulatives[index] = System.Math.Max(
                curve.MaxCumulatives[index - 1],
                curve.Cumulatives[index]);
        }
        return curve;
    }

    /// <summary>
    /// Note scroll position that gives uniform visual spacing within any SV zone.
    /// Before the SV event fires all notes appear with plain-time spacing (SV=1.0 rate).
    /// Equivalent to GetCumulativeScroll when the note is in an SV=1.0 zone.
    /// </summary>
    public static double GetUniformScrollPos(double time, string noteType = null)
    {
        var zoneStart = GetSvZoneStart(time, noteType);
        return GetCumulativeScroll(zoneStart, noteType) + (time - zoneStart);
    }

    public static void Clear()
    {
        _times       = System.Array.Empty<double>();
        _cumulatives = System.Array.Empty<double>();
        _maxCumulatives = System.Array.Empty<double>();
        _multipliers = System.Array.Empty<float>();
        TypedCurves.Clear();
        TypedOnlyCurves.Clear();
    }

    /// <summary>Start time of the SV zone that contains the given time.</summary>
    public static double GetSvZoneStart(double time, string noteType = null)
    {
        var times = GetCurve(noteType)?.Times ?? _times;
        if (times.Length == 0) return 0.0;

        int lo = 0, hi = times.Length - 1;
        if (time <= times[0]) return times[0];

        while (lo < hi - 1)
        {
            int mid = (lo + hi) >> 1;
            if (times[mid] <= time) lo = mid;
            else hi = mid;
        }
        if (times[hi] <= time) lo = hi;
        return times[lo];
    }

    /// <summary>SV multiplier currently active at the given time.</summary>
    public static float GetCurrentSV(double time, string noteType = null)
    {
        var curve = GetCurve(noteType);
        var times = curve?.Times ?? _times;
        var multipliers = curve?.Multipliers ?? _multipliers;
        if (times.Length == 0) return 1.0f;

        int lo = 0, hi = times.Length - 1;
        if (time <= times[0]) return multipliers[0];

        while (lo < hi - 1)
        {
            int mid = (lo + hi) >> 1;
            if (times[mid] <= time) lo = mid;
            else hi = mid;
        }
        if (times[hi] <= time) lo = hi;
        return multipliers[lo];
    }

    /// <summary>∫[0→time] sv(τ)dτ</summary>
    public static double GetCumulativeScroll(double time, string noteType = null)
    {
        var curve = GetCurve(noteType);
        var times = curve?.Times ?? _times;
        var cumulatives = curve?.Cumulatives ?? _cumulatives;
        var multipliers = curve?.Multipliers ?? _multipliers;
        if (times.Length == 0)
            return time; // fallback: sv=1.0, cumulative = time

        // Binary-search the last breakpoint at or before time
        int lo = 0, hi = times.Length - 1;
        if (time <= times[0])
            return cumulatives[0] + multipliers[0] * (time - times[0]);

        while (lo < hi - 1)
        {
            int mid = (lo + hi) >> 1;
            if (times[mid] <= time) lo = mid;
            else hi = mid;
        }
        // The loop exits when hi == lo+1. Check if hi itself also qualifies.
        if (times[hi] <= time) lo = hi;
        return cumulatives[lo] + multipliers[lo] * (time - times[lo]);
    }

    public static double GetTypedOnlyCumulativeScroll(double time, string noteType)
    {
        if (string.IsNullOrWhiteSpace(noteType) ||
            !TypedOnlyCurves.TryGetValue(noteType.ToLowerInvariant(), out var curve))
            return time;
        return GetCumulativeScroll(time, curve);
    }

    public static float GetTypedOnlyProgress(
        double startTime,
        double duration,
        double currentTime,
        string noteType)
    {
        if (duration <= 0d)
            return 1f;
        var linear = UnityEngine.Mathf.Clamp01((float)((currentTime - startTime) / duration));
        if (!HasTypedCurve(noteType))
            return linear;
        var start = GetTypedOnlyCumulativeScroll(startTime, noteType);
        var end = GetTypedOnlyCumulativeScroll(startTime + duration, noteType);
        var range = end - start;
        if (System.Math.Abs(range) < 0.000001d)
            return linear;
        var current = GetTypedOnlyCumulativeScroll(
            System.Math.Clamp(currentTime, startTime, startTime + duration), noteType);
        return UnityEngine.Mathf.Clamp01((float)((current - start) / range));
    }

    private static double GetCumulativeScroll(double time, Curve curve)
    {
        if (curve.Times.Length == 0)
            return time;
        var lo = 0;
        var hi = curve.Times.Length - 1;
        if (time <= curve.Times[0])
            return curve.Cumulatives[0] + curve.Multipliers[0] * (time - curve.Times[0]);
        while (lo < hi - 1)
        {
            var mid = (lo + hi) >> 1;
            if (curve.Times[mid] <= time) lo = mid;
            else hi = mid;
        }
        if (curve.Times[hi] <= time) lo = hi;
        return curve.Cumulatives[lo] + curve.Multipliers[lo] * (time - curve.Times[lo]);
    }

    public static double GetMaxCumulativeScroll(double time, string noteType = null)
    {
        var curve = GetCurve(noteType);
        var times = curve?.Times ?? _times;
        var cumulatives = curve?.Cumulatives ?? _cumulatives;
        var maxCumulatives = curve?.MaxCumulatives ?? _maxCumulatives;
        var multipliers = curve?.Multipliers ?? _multipliers;
        if (times.Length == 0)
            return time;
        if (time <= times[0])
            return cumulatives[0] + multipliers[0] * (time - times[0]);

        var lo = 0;
        var hi = times.Length - 1;
        while (lo < hi - 1)
        {
            var mid = (lo + hi) >> 1;
            if (times[mid] <= time)
                lo = mid;
            else
                hi = mid;
        }
        if (times[hi] <= time)
            lo = hi;

        var current = cumulatives[lo] + multipliers[lo] * (time - times[lo]);
        return System.Math.Max(maxCumulatives[lo], current);
    }

    public static bool HasReachedSpawnRadius(
        double noteScrollPos,
        float speed,
        double time,
        float spawnRadius,
        string noteType = null)
    {
        if (speed <= 0.0001f)
            return false;
        var requiredScroll = noteScrollPos - (4.8f - spawnRadius) / speed;
        return GetMaxCumulativeScroll(time, noteType) >= requiredScroll - 0.000001d;
    }

    private static Curve GetCurve(string noteType)
    {
        if (string.IsNullOrWhiteSpace(noteType))
            return null;
        return TypedCurves.TryGetValue(noteType.ToLowerInvariant(), out var curve) ? curve : null;
    }
}

/// <summary>Chart SV change point serialized to Majson JSON.</summary>
public class SvPoint
{
    public double time;
    public float multiplier;
    public string noteType;
    public bool reset;
}
