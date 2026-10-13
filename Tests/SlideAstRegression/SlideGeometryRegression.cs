using System.Numerics;
using MajdataCore;
using Vector3 = UnityEngine.Vector3;
using Assets.Scripts.Types;

namespace MajdataEdit;

internal static class SlideGeometryRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        CheckSlideCodeMirrors();
        CheckSensorQueue();
        CheckTouchSlideZones();
        foreach (var t in new[] { -.1f, .1f })
            Check(!MineJudgment.HeadCanHit(t, false), "A tap outside the mine perfect window must not detonate it");
        foreach (var t in new[] { -.05f, 0f, .05f })
            Check(MineJudgment.HeadCanHit(t, false), "Mine perfect-window contact must detonate");
        Check(!MineJudgment.HeadCleared(.05f, false) && MineJudgment.HeadCleared(.051f, false), "Mine tap avoidance deadline");
        Check(!MineJudgment.HeadCleared(0f, true) && MineJudgment.HeadCleared(.001f, true), "Mine touch avoidance deadline");
        Check(EffectEnvelope.Evaluate(3f, 1f, -1f, 0f, 0f, true) == 0f,
            "Expired MOVE overlapped by a longer effect must not reverse direction");
        foreach (var t in new[] { -1f, 0f, .5f, 1f, 2f, .25f, 4f })
        {
            var envelope = EffectEnvelope.Evaluate(t, 1f, -1f, 0f, 0f, true);
            Check(envelope >= 0f && envelope <= 1f, "Rewinding MOVE must preserve its authored sign");
        }
        Check(EffectEnvelope.Evaluate(.5f, 1f, -1f, 0f, 0f, true) == 1f, "MOVE peak");
        Check(SlideJudgeTiming.Grade(2f, 2f, 2.8f) == 7, "SlideCode final-area entry must be perfect");
        Check(SlideJudgeTiming.Grade(1.4f, 2f, 2.8f) == 13, "SlideCode early completion must lose score");
        Check(SlideJudgeTiming.Grade(2.6f, 2f, 2.8f) == 1, "SlideCode late completion must lose score");
        foreach (var key in new[] { 8, 2 })
        {
            Vector3 D(int k)
            {
                var angle = (135d - k * 45d) * Math.PI / 180d;
                return new Vector3((float)Math.Cos(angle) * 4.8f, (float)Math.Sin(angle) * 4.8f, 0);
            }
            var path = new[] { D(key), Vector3.zero, D(key % 8 + 1) };
            var distances = SlideGuideGeometry.Distances(path);
            Check(SlideGuideGeometry.Evaluate(path, distances, 0.5f).Position.sqrMagnitude < 0.000001f, "D-zone v guide must pass through C exactly");
            foreach (var progress in new[] { 0f, .8f, .2f, .5f, .99f, .01f, 1f })
            {
                var pose = SlideGuideGeometry.Evaluate(path, distances, progress);
                var expected = progress <= .5f ? path[0] * (1f - progress * 2f) : path[2] * (progress * 2f - 1f);
                Check((pose.Position - expected).sqrMagnitude < 0.000001f, "Seek order must not change the V route");
            }
        }
        foreach (var source in new[] { "8dv1d[4:1]", "2dv3d[4:1]" })
        {
            Check(SlidePathParser.TryParsePath(source, out var path), "D-zone V parse");
            Check(path.segments.Count == 1 && path.segments[0].startIsDZone && path.segments[0].endIsDZone,
                "D-zone V must retain both endpoint flags");
            Check(SlideShapeResolver.TryResolve(path.segments[0], out var prefab, out _, out _) && prefab == "v2",
                "Adjacent D-zone V must remain a V, with geometry rebuilt through center");
        }
        Check(SlidePathParser.TryParsePath("1-E5-7s2[4:1]", out var mixed) && mixed.isTouchPath &&
            mixed.segments.Count == 3 && mixed.segments[^1].shape == "s", "Mixed FES must retain s segment");
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
        foreach (var code in new[] { "8P91K1", "8P9A7P91K1", "1P99K1", "2B4K6", "6B8K2", "1P333K4", "5P77CQ33K1", "5Q9A1P98CQ49K5", "1A3571P0K1" })
            CheckCode(code, ref count);
        for (var start = 1; start <= 8; start++)
        for (var orbit = 1; orbit <= 8; orbit++)
        for (var end = 1; end <= 8; end++)
        foreach (var direction in new[] { 'P', 'Q' })
        foreach (var transfer in new[] { $"9{orbit}", $"{orbit}9" })
        {
            var source = $"{start}{direction}{transfer}K{end}";
            if (SlideCodeParser.TryParse(source, out _, out _)) CheckCode(source, ref count);
        }
        Console.WriteLine($"PASS: touch arc directions and {count} SlideCode geometry/judgement routes");
    }

    private static void CheckSensorQueue()
    {
        var areas = new[]
        {
            new JudgeArea(new() { [SensorType.A1] = false }, 0),
            new JudgeArea(new() { [SensorType.B1] = false, [SensorType.C] = false }, 1),
            new JudgeArea(new() { [SensorType.B5] = false }, 2),
            new JudgeArea(new() { [SensorType.A5] = true }, 3)
        };
        var pressed = new HashSet<SensorType>();
        SensorStatus Status(SensorType s) => pressed.Contains(s) ? SensorStatus.On : SensorStatus.Off;
        pressed.Add(SensorType.A5);
        Check(SlideJudgeQueue.Advance(areas, 0, Status) == 0, "Tapping a distant tail must not clear an untraced slide");
        pressed.Clear(); pressed.Add(SensorType.C);
        var cursor = SlideJudgeQueue.Advance(areas, 0, Status);
        Check(cursor == 1, "Either sensor in a paired area must be accepted");
        Check(SlideJudgeQueue.Advance(areas, cursor, Status) == cursor, "A held intermediate area still needs release");
        pressed.Clear();
        cursor = SlideJudgeQueue.Advance(areas, cursor, Status);
        Check(cursor == 2, "Releasing a pressed area advances the route");
        pressed.Add(SensorType.B5);
        cursor = SlideJudgeQueue.Advance(areas, cursor, Status);
        pressed.Add(SensorType.A5);
        Check(SlideJudgeQueue.Advance(areas, cursor, Status) == areas.Length,
            "Adjacent tail contact completes the route and can also trigger a mine on that sensor");

        // DJAuto's guide star can overlap several sensor regions at once. The
        // original SlideDrop checks only the current/next zone for each sample.
        var sampledAreas = new[]
        {
            new JudgeArea(new() { [SensorType.A1] = false }, 0),
            new JudgeArea(new() { [SensorType.B1] = false }, 1),
            new JudgeArea(new() { [SensorType.B5] = false }, 2),
            new JudgeArea(new() { [SensorType.A5] = true }, 3)
        };
        pressed.Clear();
        pressed.UnionWith(new[] { SensorType.A1, SensorType.B1, SensorType.B5, SensorType.A5 });
        Check(SlideJudgeQueue.AdvanceOne(sampledAreas, 0, Status) == 1,
            "One overlapping star sample must not clear the entire TouchSlide route");
        Check(SlideJudgeQueue.AdvanceOne(sampledAreas, 1, Status) == 2,
            "The next sample advances only the next touched zone");
    }

    private static void CheckTouchSlideZones()
    {
        var centers = new List<(int Sensor, System.Numerics.Vector2 Center)>();
        foreach (var area in new[] { 'A', 'B', 'C', 'D', 'E' })
        for (var i = 1; i <= (area == 'C' ? 1 : 8); i++)
        {
            var angle = (area is 'A' or 'B' ? 112.5d : 135d) - i * 45d;
            var radius = area switch { 'A' or 'D' => 4.1d, 'B' => 2.3d, 'E' => 3d, _ => 0d };
            var sensor = area switch
            {
                'A' => i - 1, 'B' => i + 7, 'C' => 16,
                'D' => i + 16, _ => i + 24
            };
            centers.Add((sensor, new((float)(radius * Math.Cos(angle * Math.PI / 180d)),
                (float)(radius * Math.Sin(angle * Math.PI / 180d)))));
        }
        var path = Enumerable.Range(0, 257).Select(sample =>
        {
            var angle = (67.5d - 135d * sample / 256d) * Math.PI / 180d;
            return new System.Numerics.Vector2((float)(4.1d * Math.Cos(angle)),
                (float)(4.1d * Math.Sin(angle)));
        }).ToArray();
        var zones = TouchSlideSensorRoute.Build(path, centers, 0.35f);
        var expected = new[] { SensorType.A1, SensorType.D2, SensorType.A2,
            SensorType.D3, SensorType.A3, SensorType.D4, SensorType.A4 };
        Check(zones.Select(zone => (SensorType)zone.Sensor).SequenceEqual(expected),
            "A1>A4 physical areas must be A1,D2,A2,D3,A3,D4,A4; actual: " +
            string.Join(",", zones.Select(zone => (SensorType)zone.Sensor)));
        Check(zones.Count == 7 && zones[0].Entry == 0f &&
            zones.Zip(zones.Skip(1)).All(pair => pair.First.Entry < pair.Second.Entry),
            "TouchSlide zones need distinct, increasing visual-clear boundaries");
    }

    private static void CheckSlideCodeMirrors()
    {
        foreach (var source in new[] { "8P91K1", "8P9A7P91K1", "1P333K4", "5Q9A1P98CQ49K5", "5P77CQ33K1", "1A3571P0K1" })
        foreach (var type in new[] { Mirror.HandleType.LRMirror, Mirror.HandleType.UDMirror })
        {
            var mirrored = Mirror.NoteMirrorHandle(source + "[8:1]", type);
            Check(mirrored.EndsWith("[8:1]"), "Mirror changed duration");
            Check(Mirror.NoteMirrorHandle(mirrored, type) == source + "[8:1]", "Mirror not reversible");
            var original = new List<Vector3>(); var flipped = new List<Vector3>();
            Check(SlideCodePathGeometry.TryBuild(source, original), "Original geometry invalid: " + source);
            Check(SlideCodePathGeometry.TryBuild(mirrored[..^5], flipped), "Mirrored geometry invalid");
            // Compare geometric reflection, not a second copy of the digit table.
            foreach (var p in original)
            {
                var expected = new Vector3(type == Mirror.HandleType.LRMirror ? -p.x : p.x,
                    type == Mirror.HandleType.UDMirror ? -p.y : p.y, p.z);
                Check(flipped.Any(q => (q - expected).sqrMagnitude < 0.01f), $"SlideCode reflection differs: {source} -> {mirrored}");
            }
        }
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
