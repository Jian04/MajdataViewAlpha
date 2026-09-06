using System.Collections.Generic;
using System.Linq;
using MajdataCore;

/// <summary>
/// Global Scroll Velocity controller.
/// Stores the chart's SV table and provides cumulative scroll-distance queries.
/// radius = destroy - direction * speed *
///          (noteScrollPos - GetCumulativeScroll(audioTime))
/// </summary>
public static class SvController
{
    private sealed class Curve
    {
        public double[] Times = System.Array.Empty<double>();
        public double[] Cumulatives = System.Array.Empty<double>();
        public float[] Multipliers = System.Array.Empty<float>();
        public ScrollPoint[] Points = System.Array.Empty<ScrollPoint>();
    }

    // Breakpoint times in ascending order
    private static double[] _times      = System.Array.Empty<double>();
    // Cumulative value at each breakpoint: integral [0 to t_i] sv(tau) dtau
    private static double[] _cumulatives = System.Array.Empty<double>();
    // SV multiplier from this breakpoint until the next
    private static float[]  _multipliers = System.Array.Empty<float>();
    private static ScrollPoint[] _points = System.Array.Empty<ScrollPoint>();
    private static readonly Dictionary<string, Curve> TypedCurves = new();
    private static readonly Dictionary<string, Curve> TypedOnlyCurves = new();
    private static readonly HashSet<string> TypedKeys = new();
    private static Curve DefaultStreamCurve = BuildCurve(new[] { (0d, 1f) });

    public static bool IsEmpty => _times.Length == 0;
    public static bool HasTypedCurve(string noteType) =>
        !string.IsNullOrWhiteSpace(noteType) && TypedKeys.Contains(NormalizeCurveKey(noteType));

    public static string MakeCurveKey(int streamIndex, string noteType)
    {
        var normalized = (noteType ?? string.Empty).Trim().ToLowerInvariant();
        return streamIndex == 0 ? normalized : streamIndex + "|" + normalized;
    }

    public static string ForSameStream(string curveKey, string noteType)
    {
        var separator = curveKey?.IndexOf('|') ?? -1;
        return separator > 0 && int.TryParse(curveKey.Substring(0, separator), out var streamIndex)
            ? MakeCurveKey(streamIndex, noteType)
            : MakeCurveKey(0, noteType);
    }

    /// <summary>
    /// Initializes from the chart's SV points, sorted by time.
    /// chartStartTime is the chart start; SV defaults to 1.0 before the first point.
    /// </summary>
    public static void Load(List<SvPoint> points, double chartStartTime = 0.0)
    {
        points ??= new List<SvPoint>();
        TypedCurves.Clear();
        TypedOnlyCurves.Clear();
        TypedKeys.Clear();

        var mainCurve = BuildEffectiveCurve(points, 0, string.Empty, chartStartTime);
        DefaultStreamCurve = BuildCurve(new[] { (chartStartTime, 1f) });
        _times = mainCurve.Times;
        _cumulatives = mainCurve.Cumulatives;
        _multipliers = mainCurve.Multipliers;
        _points = mainCurve.Points;

        foreach (var streamIndex in points.Select(point => point.streamIndex).Where(index => index != 0).Distinct())
            TypedCurves[MakeCurveKey(streamIndex, string.Empty)] =
                BuildEffectiveCurve(points, streamIndex, string.Empty, chartStartTime);

        foreach (var group in points
                     .Where(point => !string.IsNullOrWhiteSpace(point.noteType))
                     .GroupBy(point => (point.streamIndex, Type: point.noteType.ToLowerInvariant())))
        {
            var key = MakeCurveKey(group.Key.streamIndex, group.Key.Type);
            TypedKeys.Add(key);
            TypedCurves[key] = BuildEffectiveCurve(
                points, group.Key.streamIndex, group.Key.Type, chartStartTime);
            TypedOnlyCurves[key] = BuildTypedOnlyCurve(
                points, group.Key.streamIndex, group.Key.Type, chartStartTime);
        }
    }

    private static Curve BuildTypedOnlyCurve(
        List<SvPoint> points, int streamIndex, string noteType, double chartStartTime)
    {
        var typedPoints = points
            .Where(point => point.streamIndex == streamIndex &&
                            string.Equals(point.noteType, noteType,
                System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(point => point.time)
            .ThenBy(point => point.sourcePosition)
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

    private static Curve BuildEffectiveCurve(
        List<SvPoint> points, int streamIndex, string noteType, double chartStartTime)
    {
        var relevant = points
            .Where(point => point.streamIndex == streamIndex &&
                            (string.IsNullOrWhiteSpace(point.noteType) ||
                             (!string.IsNullOrWhiteSpace(noteType) &&
                              string.Equals(point.noteType, noteType,
                                  System.StringComparison.OrdinalIgnoreCase))))
            .OrderBy(point => point.time)
            .ThenBy(point => point.sourcePosition)
            .ToList();
        var global = 1f;
        float? typed = null;
        foreach (var point in relevant.Where(point => point.time <= chartStartTime))
        {
            if (string.IsNullOrWhiteSpace(point.noteType))
                global = point.reset ? 1f : point.multiplier;
            else
                typed = point.reset ? null : point.multiplier;
        }

        var entries = new List<(double Time, float Multiplier)>
            { (chartStartTime, typed ?? global) };
        foreach (var point in relevant.Where(point => point.time > chartStartTime))
        {
            if (string.IsNullOrWhiteSpace(point.noteType))
                global = point.reset ? 1f : point.multiplier;
            else
                typed = point.reset ? null : point.multiplier;
            var effective = typed ?? global;
            if (System.Math.Abs(entries[^1].Time - point.time) < 0.000001d)
                entries[^1] = (point.time, effective);
            else
                entries.Add((point.time, effective));
        }
        return BuildCurve(entries);
    }

    private static Curve BuildCurve(IReadOnlyList<(double Time, float Multiplier)> entries)
    {
        var points = AlphaVisualTiming.BuildScrollCurve(
            entries.Select((entry, index) =>
                new ScrollChange(entry.Time, entry.Multiplier, index)));
        var curve = new Curve
        {
            Points = points,
            Times = points.Select(point => point.Time).ToArray(),
            Multipliers = points.Select(point => point.Multiplier).ToArray(),
            Cumulatives = points.Select(point => point.Cumulative).ToArray(),
        };
        return curve;
    }

    public static void Clear()
    {
        _times       = System.Array.Empty<double>();
        _cumulatives = System.Array.Empty<double>();
        _multipliers = System.Array.Empty<float>();
        _points = System.Array.Empty<ScrollPoint>();
        TypedCurves.Clear();
        TypedOnlyCurves.Clear();
        TypedKeys.Clear();
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
        return AlphaVisualTiming.GetMultiplier(curve?.Points ?? _points, time);
    }

    /// <summary>Returns the integrated SV value from the curve origin to the requested time.</summary>
    public static double GetCumulativeScroll(double time, string noteType = null)
    {
        var curve = GetCurve(noteType);
        return AlphaVisualTiming.GetCumulativeScroll(curve?.Points ?? _points, time);
    }

    /// <summary>
    /// Direction of the authored path. Positive HS travels from SPAWN to DESTROY
    /// even when DESTROY is numerically smaller than SPAWN.
    /// </summary>
    public static float GetPathDirection(float spawnRadius, float destroyRadius)
        => AlphaVisualTiming.GetPathDirection(spawnRadius, destroyRadius);

    /// <summary>
    /// Signed radial position obtained by integrating the effective SV curve
    /// backwards from the required DESTROY position at judge time.
    /// </summary>
    public static float GetVisualRadius(
        double noteScrollPos,
        float speed,
        double time,
        float spawnRadius,
        float destroyRadius,
        string noteType = null)
    {
        return AlphaVisualTiming.GetVisualRadius(
            noteScrollPos,
            GetCumulativeScroll(time, noteType),
            speed,
            spawnRadius,
            destroyRadius);
    }

    /// <summary>
    /// Position along the authored SPAWN-to-DESTROY axis. Zero is SPAWN;
    /// |DESTROY-SPAWN| is DESTROY; values beyond it are the 4.4-compatible
    /// back-scroll side of the judgement radius.
    /// </summary>
    public static float GetPathPosition(
        float radius,
        float spawnRadius,
        float destroyRadius) =>
        AlphaVisualTiming.GetPathPosition(radius, spawnRadius, destroyRadius);

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
        if (string.IsNullOrWhiteSpace(noteType) ||
            !TypedOnlyCurves.TryGetValue(noteType.ToLowerInvariant(), out var curve))
            return linear;
        return AlphaVisualTiming.GetScrollProgress(
            curve.Points, startTime, duration, currentTime);
    }

    private static double GetCumulativeScroll(double time, Curve curve)
        => AlphaVisualTiming.GetCumulativeScroll(curve.Points, time);

    /// <summary>
    /// Resolves the first BOUNCE takeoff crossing before judge time. Rewind mode
    /// can hide again after this point; Once mode remains active.
    /// </summary>
    public static double GetBounceStartTime(
        double judgeTime,
        float baseDuration,
        float hSpeedMultiplier,
        string noteType = null)
    {
        if (baseDuration <= 0f)
            return judgeTime;

        if (System.Math.Abs(hSpeedMultiplier) <= 0.000001d)
            return judgeTime;

        var direction = GetBounceDirection(
            judgeTime, hSpeedMultiplier, noteType);
        var judgeScroll = GetCumulativeScroll(judgeTime, noteType);
        var targetScroll = judgeScroll -
                           baseDuration / (hSpeedMultiplier * direction);
        var curve = GetCurve(noteType);
        var points = curve?.Points ?? _points;
        var takeoff = AlphaVisualTiming.FindFirstTimeAtCumulativeScroll(
            points,
            points.Length > 0 ? points[0].Time : 0d,
            judgeTime,
            targetScroll,
            hSpeedMultiplier * direction > 0f);
        return double.IsNaN(takeoff)
            ? judgeTime
            : takeoff;
    }

    /// <summary>
    /// Chooses the BOUNCE path orientation from the last non-zero effective speed
    /// before judgement. Zero SV does not change direction; it only pauses motion.
    /// </summary>
    public static float GetBounceDirection(
        double judgeTime,
        float hSpeedMultiplier,
        string noteType = null)
    {
        if (System.Math.Abs(hSpeedMultiplier) <= 0.000001f)
            return 1f;

        var sv = GetLatestNonZeroSV(judgeTime, noteType);
        var direction = System.Math.Sign(hSpeedMultiplier * sv);
        return direction == 0 ? 1f : direction;
    }

    /// <summary>
    /// Returns BOUNCE phase from the signed cumulative SV curve. Negative SV moves
    /// backwards along the same path; returning to positive SV moves forwards again.
    /// </summary>
    public static float GetBounceProgress(
        double judgeTime,
        float baseDuration,
        float hSpeedMultiplier,
        float direction,
        double currentTime,
        string noteType = null)
    {
        if (baseDuration <= 0f)
            return 1f;
        var current = GetCumulativeScroll(
            System.Math.Min(currentTime, judgeTime), noteType);
        var judge = GetCumulativeScroll(judgeTime, noteType);
        return 1f + direction * hSpeedMultiplier *
            (float)(current - judge) / baseDuration;
    }

    private static float GetLatestNonZeroSV(double time, string noteType)
    {
        var curve = GetCurve(noteType);
        var times = curve?.Times ?? _times;
        var multipliers = curve?.Multipliers ?? _multipliers;
        if (times.Length == 0)
            return 1f;

        var index = times.Length - 1;
        while (index > 0 && times[index] > time)
            index--;
        while (index >= 0)
        {
            if (System.Math.Abs(multipliers[index]) > 0.000001f)
                return multipliers[index];
            index--;
        }
        return 1f;
    }

    public static bool IsPastSpawnNow(
        double noteScrollPos,
        float speed,
        double time,
        float spawnRadius,
        string noteType = null,
        float destroyRadius = 4.8f) =>
        AlphaVisualTiming.IsPastSpawnNow(
            noteScrollPos,
            GetCumulativeScroll(time, noteType),
            speed,
            spawnRadius,
            destroyRadius);

    public static bool HasEverCrossedSpawn(
        double noteScrollPos,
        float speed,
        double time,
        float spawnRadius,
        string noteType = null,
        float destroyRadius = 4.8f)
    {
        var curve = GetCurve(noteType);
        var points = curve?.Points ?? _points;
        return AlphaVisualTiming.HasEverCrossedSpawn(
            points,
            points.Length > 0 ? points[0].Time : 0d,
            time,
            noteScrollPos,
            speed,
            spawnRadius,
            destroyRadius);
    }

    /// <summary>
    /// The same question asked by a note that keeps its own answer, which is what
    /// every per-frame caller wants: the search covers the chart from its start, so
    /// asking it fresh every frame costs the whole elapsed chart every frame.
    /// </summary>
    public static bool HasEverCrossedSpawn(
        ref SpawnCrossingMemo memo,
        double noteScrollPos,
        float speed,
        double time,
        float spawnRadius,
        string noteType = null,
        float destroyRadius = 4.8f)
    {
        var curve = GetCurve(noteType);
        var points = curve?.Points ?? _points;
        return memo.HasEverCrossed(
            points,
            points.Length > 0 ? points[0].Time : 0d,
            time,
            noteScrollPos,
            speed,
            spawnRadius,
            destroyRadius);
    }

    private static Curve GetCurve(string noteType)
    {
        if (string.IsNullOrWhiteSpace(noteType))
            return null;
        var key = NormalizeCurveKey(noteType);
        if (TypedCurves.TryGetValue(key, out var curve))
            return curve;
        var separator = key.IndexOf('|');
        if (separator > 0 && TypedCurves.TryGetValue(key.Substring(0, separator + 1), out curve))
            return curve;
        return separator > 0 ? DefaultStreamCurve : null;
    }

    private static string NormalizeCurveKey(string key) =>
        (key ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>Chart SV change point serialized to Majson JSON.</summary>
public class SvPoint
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public float multiplier;
    public string noteType;
    public bool reset;
}
