#nullable disable
using System.Collections.Generic;
using System.Linq;
using MajdataCore;
#if MAJDATA_EDIT
using SvPoint = MajdataEdit.SvPoint;
#endif

/// <summary>
/// Global Scroll Velocity controller.
/// Stores the chart's SV table and provides cumulative scroll-distance queries.
/// radius = destroy - direction * speed *
///          (noteScrollPos - GetCumulativeScroll(audioTime))
/// </summary>
#if MAJDATA_EDIT
internal static class SvController
#else
public static class SvController
#endif
{
    private sealed class Curve
    {
        public double[] Times = System.Array.Empty<double>();
        public double[] Cumulatives = System.Array.Empty<double>();
        public float[] Multipliers = System.Array.Empty<float>();
        public ScrollPoint[] Points = System.Array.Empty<ScrollPoint>();
        public double CachedTime = double.NaN;
        public double CachedScroll;
    }

    // Breakpoint times in ascending order
    private static double[] _times      = System.Array.Empty<double>();
    // Cumulative value at each breakpoint: integral [0 to t_i] sv(tau) dtau
    private static double[] _cumulatives = System.Array.Empty<double>();
    // SV multiplier from this breakpoint until the next
    private static float[]  _multipliers = System.Array.Empty<float>();
    private static ScrollPoint[] _points = System.Array.Empty<ScrollPoint>();
    private static double _cachedMainTime = double.NaN;
    private static double _cachedMainScroll;
    private static readonly Dictionary<string, Curve> TypedCurves = new();
    private static readonly Dictionary<string, Curve> TypedOnlyCurves = new();
    private static readonly Dictionary<string, Curve> ResolvedCurves = new();
    public static bool HasDefaultScroll =>
        _points.Length == 1 && _points[0].Multiplier == 1f && TypedCurves.Count == 0;
    private static readonly HashSet<string> TypedKeys = new();
    private static int noteCurveId;
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
        _cachedMainTime = double.NaN;
        points ??= new List<SvPoint>();
        TypedCurves.Clear();
        ResolvedCurves.Clear();
        TypedOnlyCurves.Clear();
        TypedKeys.Clear();
        noteCurveId = 0;

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
        => BuildEffectiveCurve(points, streamIndex, noteType, chartStartTime, true);

    private static Curve BuildEffectiveCurve(
        List<SvPoint> points, int streamIndex, string noteType, double chartStartTime, bool typedOnly = false)
    {
        var relevant = points
            .Where(point => point.streamIndex == streamIndex &&
                            ((!typedOnly && string.IsNullOrWhiteSpace(point.noteType)) ||
                             (!string.IsNullOrWhiteSpace(noteType) &&
                              string.Equals(point.noteType, noteType,
                                  System.StringComparison.OrdinalIgnoreCase))))
            .OrderBy(point => point.time)
            .ThenBy(point => point.sourcePosition)
            .ToList();
        var boundaries = new SortedSet<double> { chartStartTime };
        foreach (var point in relevant)
        {
            if (point.time > chartStartTime) boundaries.Add(point.time);
            if (point.transition == null || point.transition.duration <= 0f) continue;
            // Linear ramps are exact. Nonlinear curves are baked once into 128
            // linear segments; playback still uses one binary search and integral.
            var steps = point.transition.easing == "linear" ? 1 : 128;
            for (var step = 1; step <= steps; step++)
            {
                var at = point.time + point.transition.duration * (double)step / steps;
                if (at > chartStartTime) boundaries.Add(at);
            }
        }
        var times = boundaries.ToArray();
        var entries = new List<ScrollChange>();
        SvPoint global = null, typed = null;
        var cursor = 0;
        for (var i = 0; i < times.Length; i++)
        {
            var at = times[i];
            while (cursor < relevant.Count && relevant[cursor].time <= at + .0000001d)
            {
                var point = relevant[cursor++];
                if (string.IsNullOrWhiteSpace(point.noteType)) global = point.reset ? null : point;
                else typed = point.reset ? null : point;
            }
            var source = typed ?? global;
            float Value(double time) => source?.transition?.Evaluate(time - source.time) ?? source?.multiplier ?? 1f;
            var value = Value(at);
            var end = i + 1 < times.Length ? times[i + 1] : at;
            var slope = end > at ? (float)((Value(end) - value) / (end - at)) : 0f;
            entries.Add(new ScrollChange(at, value, i, slope));
            if (slope != 0f)
            {
                var zero = at - value / slope;
                if (zero > at + .000001d && zero < end - .000001d)
                    entries.Add(new ScrollChange(zero, 0f, i, slope));
            }
        }
        return CreateCurve(AlphaVisualTiming.BuildScrollCurve(entries));
    }

    private static Curve BuildCurve(IReadOnlyList<(double Time, float Multiplier)> entries)
    {
        var points = AlphaVisualTiming.BuildScrollCurve(
            entries.Select((entry, index) =>
                new ScrollChange(entry.Time, entry.Multiplier, index)));
        return CreateCurve(points);
    }

    private static Curve CreateCurve(ScrollPoint[] points)
    {
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
        _cachedMainTime = double.NaN;
        _times       = System.Array.Empty<double>();
        _cumulatives = System.Array.Empty<double>();
        _multipliers = System.Array.Empty<float>();
        _points = System.Array.Empty<ScrollPoint>();
        TypedCurves.Clear();
        ResolvedCurves.Clear();
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
        if (curve != null)
            return GetCumulativeScroll(time, curve);
        if (time == _cachedMainTime)
            return _cachedMainScroll;
        _cachedMainTime = time;
        _cachedMainScroll = AlphaVisualTiming.GetCumulativeScroll(_points, time);
        return _cachedMainScroll;
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

    internal static float GetTypedOnlyMultiplier(double time, string noteType)
    {
        return !string.IsNullOrWhiteSpace(noteType) &&
               TypedOnlyCurves.TryGetValue(noteType.ToLowerInvariant(), out var curve)
            ? AlphaVisualTiming.GetMultiplier(curve.Points, time)
            : 1f;
    }

    public static float GetTypedOnlyProgress(
        double startTime,
        double duration,
        double currentTime,
        string noteType)
    {
        if (duration <= 0d)
            return 1f;
        var linear = (float)System.Math.Clamp((currentTime - startTime) / duration, 0d, 1d);
        if (string.IsNullOrWhiteSpace(noteType) ||
            !TypedOnlyCurves.TryGetValue(noteType.ToLowerInvariant(), out var curve))
            return linear;
        return AlphaVisualTiming.GetScrollProgress(
            curve.Points, startTime, duration, currentTime);
    }

    private static double GetCumulativeScroll(double time, Curve curve)
    {
        if (time == curve.CachedTime)
            return curve.CachedScroll;
        curve.CachedTime = time;
        curve.CachedScroll = AlphaVisualTiming.GetCumulativeScroll(curve.Points, time);
        return curve.CachedScroll;
    }

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
        var current=GetCurrentSV(time,noteType);
        if(System.Math.Abs(current)>.000001f) return current;
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

    public static double GetTouchWarningLead(double judgeTime, float speed, string noteType)
    {
        if (System.Math.Abs(speed) <= 0.000001f) return 0d;
        var curve = GetCurve(noteType);
        var points = curve?.Points ?? _points;
        var scroll = GetCumulativeScroll(judgeTime, noteType);
        var duration = AlphaVisualTiming.GetTouchMotionDuration(speed);
        // Touch uses the absolute scroll distance before judgement, including
        // negative SV and repeated changes of direction.
        var lower = AlphaVisualTiming.FindLastTimeAtCumulativeScroll(points, judgeTime, scroll - duration);
        var upper = AlphaVisualTiming.FindLastTimeAtCumulativeScroll(points, judgeTime, scroll + duration);
        var appeared = double.IsNaN(lower) ? upper : double.IsNaN(upper) ? lower : System.Math.Max(lower, upper);
        return double.IsNaN(appeared) ? 0d : System.Math.Max(0d, judgeTime - appeared);
    }

    public static double GetRingAppearanceTime(double judgeTime,float speed,float spawn,float destroy,string noteType)
    {
        if (System.Math.Abs(speed) <= .000001f) return judgeTime;
        var points = GetCurve(noteType)?.Points ?? _points;
        var threshold = GetCumulativeScroll(judgeTime,noteType) -
            (System.Math.Abs(destroy-spawn) + AlphaVisualTiming.SpawnScaleDistance) / speed;
        var appeared = AlphaVisualTiming.FindLastTimeAtCumulativeScroll(points,judgeTime,threshold);
        return double.IsNaN(appeared) ? judgeTime : appeared;
    }

    public static string CreateBirthSpeedCurve(string original,double judgeTime,double distance,
        NoteValueTransition hs,double endTime,ref double appearance,bool ignoreSv=false)
    {
        var source = ignoreSv ? DefaultStreamCurve.Points : GetCurve(original)?.Points ?? _points;
        double Integral(double start)
        {
            const int steps = 64;
            var dt = (judgeTime-start)/steps;
            var sum = 0d;
            for(var i=0;i<steps;i++)
            {
                var at = start+(i+.5d)*dt;
                sum += AlphaVisualTiming.GetMultiplier(source,at)*hs.Evaluate(at-start)*dt;
            }
            return sum;
        }
        var direction = System.Math.Sign(hs.from != 0f ? hs.from : hs.to) *
            (ignoreSv ? 1 : System.Math.Sign(GetLatestNonZeroSV(judgeTime, original)));
        var target = distance*(direction == 0 ? 1 : direction);
        var lead = System.Math.Max(.01d,judgeTime-appearance);
        var low = judgeTime-lead;
        for(var i=0;direction != 0 && i<24 && Integral(low)*target < target*target;i++)
            low = judgeTime - (judgeTime-low)*2d;
        if(direction != 0 && Integral(low)*target >= target*target)
        {
            var high = judgeTime;
            for(var i=0;i<28;i++)
            {
                var middle = (low+high)*.5d;
                if(Integral(middle)*target >= target*target) low=middle; else high=middle;
            }
            appearance = (low+high)*.5d;
        }
        var appeared = appearance;
        var boundaries = new SortedSet<double> { appeared,judgeTime,endTime };
        if(appeared+hs.duration < endTime) boundaries.Add(appeared+hs.duration);
        foreach(var point in source)
            if(point.Time>appeared && point.Time<endTime) boundaries.Add(point.Time);
        for(var i=1;i<128;i++)
        {
            var at=appeared+hs.duration*i/128d;
            if(at<endTime) boundaries.Add(at);
        }
        var times = boundaries.ToArray();
        var entries = new List<ScrollChange>();
        for(var i=0;i<times.Length;i++)
        {
            var at = times[i];
            float Value(double t) => AlphaVisualTiming.GetMultiplier(source,t)*hs.Evaluate(t-appeared);
            var value = Value(at);
            var end = i+1<times.Length ? times[i+1] : at;
            // Read just before the next boundary so an SV step remains a step.
            var slope = end>at ? (float)((Value(end-.00000001d)-value)/(end-at)) : 0f;
            entries.Add(new ScrollChange(at,value,i,slope));
            if(slope != 0f)
            {
                var zero=at-value/slope;
                if(zero>at+.000001d && zero<end-.000001d) entries.Add(new ScrollChange(zero,0f,i,slope));
            }
        }
        var separator=original?.IndexOf('|') ?? -1;
        var key=(separator>0 ? original.Substring(0,separator+1) : "")+"#hs"+(++noteCurveId);
        TypedCurves[key]=CreateCurve(AlphaVisualTiming.BuildScrollCurve(entries));
        return key;
    }

    private static Curve GetCurve(string noteType)
    {
        if (string.IsNullOrWhiteSpace(noteType))
            return null;
        if (ResolvedCurves.TryGetValue(noteType, out var resolved))
            return resolved;
        var key = NormalizeCurveKey(noteType);
        if (TypedCurves.TryGetValue(key, out var curve))
            return ResolvedCurves[noteType] = curve;
        var separator = key.IndexOf('|');
        if (separator > 0 && TypedCurves.TryGetValue(key.Substring(0, separator + 1), out curve))
            return ResolvedCurves[noteType] = curve;
        return ResolvedCurves[noteType] = separator > 0 ? DefaultStreamCurve : null;
    }

    private static string NormalizeCurveKey(string key) =>
        (key ?? string.Empty).Trim().ToLowerInvariant();
#if MAJDATA_EDIT
    internal static double MonitorFirstAppearance(double judge,float speed,float spawn,float destroy,string key,bool touch,double fallback)
    {
        var points=GetCurve(key)?.Points ?? _points;
        var searchStart=System.Math.Min(0d,fallback);
        var scroll=GetCumulativeScroll(judge,key);
        var first=touch ? AlphaVisualTiming.FindFirstTouchVisibleTime(points,searchStart,judge,scroll,speed)
            : AlphaVisualTiming.FindFirstVisibleTime(points,searchStart,judge,scroll,speed,spawn,destroy);
        return double.IsNaN(first) ? fallback : first;
    }
    internal static void ReleaseMonitorBirthCurve(string key)
    {
        TypedCurves.Remove(key);
        ResolvedCurves.Remove(key);
    }
#endif
}

#if !MAJDATA_EDIT
/// <summary>Chart SV change point serialized to Majson JSON.</summary>
public class SvPoint
{
    public NoteValueTransition transition;
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public float multiplier;
    public string noteType;
    public bool reset;
}
#endif
