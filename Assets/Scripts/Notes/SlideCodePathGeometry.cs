using System;
using System.Collections.Generic;
using MajdataCore;
using UnityEngine;

internal static class SlideCodePathGeometry
{
    private const float JudgeRadius = 4.8f;
    private const float SampleSpacing = 0.055f;
    private const float Epsilon = 0.001f;
    private static readonly float Cos22_5 = Mathf.Cos(Mathf.PI / 8f);
    private static readonly float Cos67_5 = Mathf.Cos(3f * Mathf.PI / 8f);

    private readonly struct Circle
    {
        public Circle(int index, Vector2 center, float radius)
        {
            Index = index;
            Center = center;
            Radius = radius;
        }

        public int Index { get; }
        public Vector2 Center { get; }
        public float Radius { get; }
    }

    public static bool TryBuild(string expression, List<Vector3> target)
    {
        target.Clear();
        if (!SlideCodeParser.TryParse(expression, out var code, out _))
            return false;
        return TryBuild(code, target);
    }

    public static bool TryBuild(SlideCodeData code, List<Vector3> target)
    {
        target.Clear();
        if (code?.instructions == null || code.instructions.Count < 2)
            return false;

        var first = code.instructions[0];
        var currentPoint = Node(first);
        Add(target, currentPoint);
        var onOrbit = false;
        var currentCircle = default(Circle);
        var currentAngle = 0f;
        var direction = 0;

        for (var index = 1; index < code.instructions.Count; index++)
        {
            var instruction = code.instructions[index];
            if (instruction.IsNode)
            {
                var nextPoint = Node(instruction);
                if (!onOrbit)
                {
                    AppendLine(target, currentPoint, nextPoint);
                }
                else if (!ExitOrbit(
                             target, currentCircle, currentAngle,
                             direction, nextPoint, out currentAngle))
                {
                    target.Clear();
                    return false;
                }
                currentPoint = nextPoint;
                onOrbit = false;
                continue;
            }

            var nextCircle = Orbit(instruction.parameter);
            var nextDirection = instruction.command == SlideCodeCommand.P ? 1 : -1;
            if (!onOrbit)
            {
                if (!EnterOrbit(
                        target, currentPoint, nextCircle,
                        nextDirection, out currentAngle))
                {
                    target.Clear();
                    return false;
                }
                currentCircle = nextCircle;
                direction = nextDirection;
                onOrbit = true;
                continue;
            }

            if (direction != nextDirection ||
                !TransferOrbit(
                    target, currentCircle, currentAngle, nextCircle,
                    direction, out currentAngle))
            {
                target.Clear();
                return false;
            }
            currentCircle = nextCircle;
        }

        return !onOrbit && target.Count >= 2;
    }

    public static bool AppendSingleOrbit(
        List<Vector3> target,
        Vector3 start,
        Vector3 end,
        int orbitIndex,
        bool counterClockwise)
    {
        var route = new List<Vector3>();
        var circle = Orbit(orbitIndex);
        Add(route, start);
        var direction = counterClockwise ? 1 : -1;
        if (!EnterOrbit(route, start, circle, direction, out var angle) ||
            !ExitOrbit(route, circle, angle, direction, end, out _))
            return false;
        var first = target.Count == 0 ? 0 : 1;
        for (var index = first; index < route.Count; index++)
            Add(target, route[index]);
        return true;
    }

    private static bool EnterOrbit(
        List<Vector3> target,
        Vector2 point,
        Circle circle,
        int direction,
        out float angle)
    {
        var relative = point - circle.Center;
        var distance = relative.magnitude;
        if (distance < circle.Radius - Epsilon)
        {
            angle = 0f;
            return false;
        }
        var pointAngle = Mathf.Atan2(relative.y, relative.x);
        if (Mathf.Abs(distance - circle.Radius) <= Epsilon)
        {
            angle = pointAngle;
            return true;
        }

        var offset = Mathf.Acos(Mathf.Clamp(circle.Radius / distance, -1f, 1f));
        angle = pointAngle + direction * offset;
        AppendLine(target, point, Point(circle, angle));
        return true;
    }

    private static bool ExitOrbit(
        List<Vector3> target,
        Circle circle,
        float currentAngle,
        int direction,
        Vector2 point,
        out float exitAngle)
    {
        var relative = point - circle.Center;
        var distance = relative.magnitude;
        if (distance < circle.Radius - Epsilon)
        {
            exitAngle = 0f;
            return false;
        }
        var pointAngle = Mathf.Atan2(relative.y, relative.x);
        if (Mathf.Abs(distance - circle.Radius) <= Epsilon)
        {
            exitAngle = pointAngle;
        }
        else
        {
            var offset = Mathf.Acos(Mathf.Clamp(circle.Radius / distance, -1f, 1f));
            exitAngle = pointAngle - direction * offset;
        }

        var sweep = DirectedSweep(
            currentAngle, exitAngle, direction, forceFullWhenSame: false);
        AppendArc(target, circle, currentAngle, sweep, direction);
        AppendLine(target, Point(circle, exitAngle), point);
        return true;
    }

    private static bool TransferOrbit(
        List<Vector3> target,
        Circle current,
        float currentAngle,
        Circle next,
        int direction,
        out float nextAngle)
    {
        if (current.Index == next.Index)
        {
            AppendArc(
                target, current, currentAngle, currentAngle,
                direction, forceFullWhenSame: true);
            nextAngle = currentAngle;
            return true;
        }
        if (current.Index == 0 && next.Index == 9 ||
            current.Index == 9 && next.Index == 0)
        {
            nextAngle = 0f;
            return false;
        }
        if (current.Index == 9 || next.Index == 9)
            return TransferOuterOrbit(
                target, current, currentAngle, next, direction, out nextAngle);

        var delta = next.Center - current.Center;
        var distance = delta.magnitude;
        if (distance <= Epsilon)
        {
            nextAngle = 0f;
            return false;
        }
        var ratio = Mathf.Clamp(
            (current.Radius - next.Radius) / distance, -1f, 1f);
        var tangentAngle = Mathf.Atan2(delta.y, delta.x) -
                           direction * Mathf.Acos(ratio);
        AppendArc(target, current, currentAngle, tangentAngle, direction, false);
        AppendLine(
            target, Point(current, tangentAngle), Point(next, tangentAngle));
        nextAngle = tangentAngle;
        return true;
    }

    private static bool TransferOuterOrbit(
        List<Vector3> target,
        Circle current,
        float currentAngle,
        Circle next,
        int direction,
        out float nextAngle)
    {
        var inner = current.Index == 9 ? next : current;
        if (inner.Index is < 1 or > 8)
        {
            nextAngle = 0f;
            return false;
        }

        var b = Cos22_5 * 0.5f;
        var a = 1f - b;
        // MajdataPlay SlideGeo.TransferOutData: the connector is fixed by
        // orbit and direction, never selected by shortest travel from the head.
        var gamma = Mathf.PI / 4f;
        var s = (a * a + b * b - 2f * a * b * Mathf.Cos(gamma)) /
                (2f * a - 2f * b * Mathf.Cos(gamma));
        var connectorRadius = JudgeRadius * (b + s);
        var connectorCenterDistance = JudgeRadius * (a - s);
        var transferAngle = Mathf.Acos(Mathf.Clamp(
            (s * s + b * b - (a - s) * (a - s)) / (2f * b * s), -1f, 1f));
        var outwardDirection = current.Index == 9 ? -direction : direction;
        var innerAngle = OrbitAngle(inner.Index) - outwardDirection * transferAngle;
        var outerAngle = OrbitAngle(inner.Index) + outwardDirection * gamma;
        var connector = new Circle(-1, Polar(connectorCenterDistance, outerAngle), connectorRadius);

        if (current.Index != 9)
        {
            AppendArc(
                target, current, currentAngle, innerAngle,
                direction, false);
            AppendArc(
                target, connector,
                innerAngle,
                outerAngle,
                direction, false);
            nextAngle = outerAngle;
        }
        else
        {
            AppendArc(
                target, current, currentAngle, outerAngle,
                direction, true);
            AppendArc(
                target, connector,
                outerAngle,
                innerAngle,
                direction, false);
            nextAngle = innerAngle;
        }
        return true;
    }

    private static Vector2 Node(SlideCodeInstruction instruction)
    {
        if (instruction.command == SlideCodeCommand.C)
            return Vector2.zero;
        var radius = instruction.command == SlideCodeCommand.B
            ? JudgeRadius * Cos67_5 / Cos22_5
            : JudgeRadius;
        return Polar(radius, KeyAngle(instruction.parameter));
    }

    private static Circle Orbit(int index)
    {
        if (index == 0)
            return new Circle(0, Vector2.zero, JudgeRadius * Cos67_5);
        if (index == 9)
            return new Circle(9, Vector2.zero, JudgeRadius);
        var radius = JudgeRadius * Cos22_5 * 0.5f;
        return new Circle(index, Polar(radius, OrbitAngle(index)), radius);
    }

    private static float KeyAngle(int index) =>
        (112.5f - index * 45f) * Mathf.Deg2Rad;

    private static float OrbitAngle(int index) =>
        (135f - index * 45f) * Mathf.Deg2Rad;

    private static Vector2 Polar(float radius, float angle) =>
        new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

    private static Vector2 Point(Circle circle, float angle) =>
        circle.Center + Polar(circle.Radius, angle);

    private static void AppendLine(
        List<Vector3> target, Vector2 start, Vector2 end)
    {
        var count = Mathf.Max(
            1, Mathf.CeilToInt(Vector2.Distance(start, end) / SampleSpacing));
        for (var index = 1; index <= count; index++)
            Add(target, Vector2.Lerp(start, end, index / (float)count));
    }

    private static void AppendArc(
        List<Vector3> target,
        Circle circle,
        float startAngle,
        float endAngle,
        int direction,
        bool forceFullWhenSame)
    {
        var sweep = DirectedSweep(
            startAngle, endAngle, direction, forceFullWhenSame);
        AppendArc(target, circle, startAngle, sweep, direction);
    }

    private static void AppendArc(
        List<Vector3> target,
        Circle circle,
        float startAngle,
        float sweep,
        int direction)
    {
        var count = Mathf.Max(
            1, Mathf.CeilToInt(circle.Radius * sweep / SampleSpacing));
        var signedSweep = sweep * direction;
        for (var index = 1; index <= count; index++)
            Add(target, Point(
                circle, startAngle + signedSweep * (index / (float)count)));
    }

    private static float DirectedSweep(
        float startAngle,
        float endAngle,
        int direction,
        bool forceFullWhenSame)
    {
        var delta = direction > 0
            ? Mathf.Repeat(endAngle - startAngle, Mathf.PI * 2f)
            : Mathf.Repeat(startAngle - endAngle, Mathf.PI * 2f);
        if (forceFullWhenSame && delta < Epsilon)
            return Mathf.PI * 2f;
        return delta;
    }

    private static void Add(List<Vector3> target, Vector2 point)
    {
        var value = new Vector3(point.x, point.y, 0f);
        if (target.Count == 0 ||
            (target[^1] - value).sqrMagnitude > 0.000001f)
            target.Add(value);
    }
}
