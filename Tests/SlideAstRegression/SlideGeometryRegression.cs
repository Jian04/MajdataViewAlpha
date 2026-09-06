using System.Numerics;
using MajdataCore;
using Vector3 = UnityEngine.Vector3;

namespace MajdataEdit;

internal static class SlideGeometryRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        for (var key = 1; key <= 8; key++)
        {
            var rightCcw = key is >= 3 and <= 6;
            Check(TouchSlideDirection.IsCounterClockwise(key, '>') == rightCcw, $"Right arc direction at {key}");
            Check(TouchSlideDirection.IsCounterClockwise(key, '<') != rightCcw, $"Left arc direction at {key}");
            foreach (var shape in new[] { '<', '>' })
            {
                var start = (135d - key * 45d) * Math.PI / 180d;
                var sweep = TouchSlideDirection.Sweep(start, start, key, shape);
                Check(Math.Abs(Math.Abs(sweep) - Math.Tau) < 1e-6, $"Full turn at E{key}{shape}E{key}");
                Check((sweep > 0) == TouchSlideDirection.IsCounterClockwise(key, shape), "Full turn direction");
                var almostSame = TouchSlideDirection.Sweep(start, start + 1e-7, key, shape);
                Check(Math.Abs(almostSame - sweep) < 1e-6, "Same ray must not collapse due to float noise");
            }
        }
        Check(TouchSlideDirection.Sweep(-Math.PI / 2, -Math.PI / 2, 5, '>') > 0, "E5>E5 must be CCW");
        Check(TouchSlideDirection.Sweep(-Math.PI / 2, -Math.PI / 2, 5, '<') < 0, "E5<D5 must be CW");

        var count = 0;
        for (var start = 1; start <= 8; start++)
            for (var end = 1; end <= 8; end++)
                foreach (var orbit in Enumerable.Range(0, 10))
                    foreach (var direction in new[] { 'P', 'Q' })
                    {
                        if (orbit == 9 && start == end) continue;
                        CheckCode($"{start}{direction}{orbit}K{end}", ref count);
                    }
        foreach (var code in new[] { "1P99K1", "2B4K6", "6B8K2", "1P333K4", "5P77CQ33K1", "5Q9A1P98CQ49K5", "1A3571P0K1" })
            CheckCode(code, ref count);
        Console.WriteLine($"PASS: touch arc directions and {count} SlideCode geometry/judgement routes");
    }

    private static void CheckCode(string expression, ref int count)
    {
        Check(SlideCodeParser.TryParse(expression, out var code, out var error), $"{expression}: {error}");
        var points = new List<Vector3>();
        Check(SlideCodePathGeometry.TryBuild(code, points), $"Failed geometry: {expression}");
        var lengths = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
            lengths[i] = lengths[i - 1] + Math.Sqrt((points[i] - points[i - 1]).sqrMagnitude);
        var total = lengths[^1];
        // A start coinciding with its exit tangent can legitimately have no orbit arc.
        if (total < 0.1) return;
        Complex PointAt(double progress)
        {
            var distance = total * progress;
            var next = Array.BinarySearch(lengths, distance);
            if (next < 0) next = ~next;
            next = Math.Clamp(next, 1, points.Count - 1);
            var t = (distance - lengths[next - 1]) / (lengths[next] - lengths[next - 1]);
            var a = points[next - 1];
            var b = points[next];
            return new Complex(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }
        var last = code.instructions[^2];
        SlideAreaRawData[] areas;
        try { areas = SlideCodeJudgment.Build(total, PointAt, !(last.IsOrbit && last.parameter == 9)); }
        catch (Exception ex) { throw new InvalidOperationException($"{expression}: {ex.Message}", ex); }
        Check(areas[0].SensorA == expression[0] - '1', $"Wrong start sensor: {expression}");
        Check(areas[^1].SensorA == expression[^1] - '1', $"Wrong end sensor: {expression}");
        Check(Math.Abs(areas[^1].LengthAfterFinish - total) < 1e-6, "End judgement distance");
        foreach (var area in areas)
            Check(area.SensorA is >= 0 and <= 16 && area.SensorB is >= -1 and <= 16, "Invalid sensor");
        count++;
    }
}
