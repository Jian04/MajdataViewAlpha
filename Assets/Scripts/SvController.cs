using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 全局 Scroll Velocity 控制器。
/// 持有谱面的 SV 表并提供累积滚动距离查询。
/// distance = 4.8 - speed * (noteScrollPos - GetCumulativeScroll(audioTime))
/// </summary>
public static class SvController
{
    // breakpoint times（升序）
    private static double[] _times      = System.Array.Empty<double>();
    // 在该断点处的累积值 ∫[0→t_i] sv(τ)dτ
    private static double[] _cumulatives = System.Array.Empty<double>();
    // 该断点开始的 sv 倍率（直到下一个断点）
    private static float[]  _multipliers = System.Array.Empty<float>();

    public static bool IsEmpty => _times.Length == 0;

    /// <summary>
    /// 用谱面的 SV 点列表初始化。points 按 time 升序排列。
    /// chartStartTime：谱面开始时间（points 之前默认 sv=1.0）。
    /// </summary>
    public static void Load(List<SvPoint> points, double chartStartTime = 0.0)
    {
        // Like <HS*>: carry forward the last SV value set before the start point.
        float effectiveSv = 1.0f;
        if (points != null)
            foreach (var p in points.OrderBy(p => p.time))
                if (p.time <= chartStartTime)
                    effectiveSv = p.multiplier;

        var sorted = new List<SvPoint> { new SvPoint { time = chartStartTime, multiplier = effectiveSv } };
        if (points != null)
            foreach (var p in points.OrderBy(p => p.time))
                if (p.time > chartStartTime)
                    sorted.Add(p);

        int n = sorted.Count;
        _times       = new double[n];
        _cumulatives = new double[n];
        _multipliers = new float[n];

        _times[0]       = sorted[0].time;
        _cumulatives[0] = 0.0;
        _multipliers[0] = sorted[0].multiplier;

        for (int i = 1; i < n; i++)
        {
            _times[i]       = sorted[i].time;
            _multipliers[i] = sorted[i].multiplier;
            // cumulative up to this breakpoint
            _cumulatives[i] = _cumulatives[i - 1]
                               + _multipliers[i - 1] * (_times[i] - _times[i - 1]);
        }
    }

    /// <summary>
    /// Note scroll position that gives uniform visual spacing within any SV zone.
    /// Before the SV event fires all notes appear with plain-time spacing (SV=1.0 rate).
    /// Equivalent to GetCumulativeScroll when the note is in an SV=1.0 zone.
    /// </summary>
    public static double GetUniformScrollPos(double time)
    {
        var zoneStart = GetSvZoneStart(time);
        return GetCumulativeScroll(zoneStart) + (time - zoneStart);
    }

    public static void Clear()
    {
        _times       = System.Array.Empty<double>();
        _cumulatives = System.Array.Empty<double>();
        _multipliers = System.Array.Empty<float>();
    }

    /// <summary>Start time of the SV zone that contains the given time.</summary>
    public static double GetSvZoneStart(double time)
    {
        if (_times.Length == 0) return 0.0;

        int lo = 0, hi = _times.Length - 1;
        if (time <= _times[0]) return _times[0];

        while (lo < hi - 1)
        {
            int mid = (lo + hi) >> 1;
            if (_times[mid] <= time) lo = mid;
            else hi = mid;
        }
        if (_times[hi] <= time) lo = hi;
        return _times[lo];
    }

    /// <summary>SV multiplier currently active at the given time.</summary>
    public static float GetCurrentSV(double time)
    {
        if (_times.Length == 0) return 1.0f;

        int lo = 0, hi = _times.Length - 1;
        if (time <= _times[0]) return _multipliers[0];

        while (lo < hi - 1)
        {
            int mid = (lo + hi) >> 1;
            if (_times[mid] <= time) lo = mid;
            else hi = mid;
        }
        if (_times[hi] <= time) lo = hi;
        return _multipliers[lo];
    }

    /// <summary>∫[0→time] sv(τ)dτ</summary>
    public static double GetCumulativeScroll(double time)
    {
        if (_times.Length == 0)
            return time; // fallback: sv=1.0, cumulative = time

        // 二分查找最后一个 <= time 的断点
        int lo = 0, hi = _times.Length - 1;
        if (time <= _times[0])
            return _cumulatives[0] + _multipliers[0] * (time - _times[0]);

        while (lo < hi - 1)
        {
            int mid = (lo + hi) >> 1;
            if (_times[mid] <= time) lo = mid;
            else hi = mid;
        }
        // The loop exits when hi == lo+1. Check if hi itself also qualifies.
        if (_times[hi] <= time) lo = hi;
        return _cumulatives[lo] + _multipliers[lo] * (time - _times[lo]);
    }
}

/// <summary>谱面 SV 变化点（序列化到 Majson JSON）</summary>
public class SvPoint
{
    public double time;
    public float multiplier;
}

