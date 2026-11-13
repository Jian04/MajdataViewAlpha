using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataCore;
using UnityEngine;

public static class Reported089Verification
{
    public static void BuildCheckedView()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var expression in new[] { "1>5K4[8:1]", "1^4K5[8:1]", "1P93K5-7[8:1]", "1P93K5[8:1]", "1^4-7[8:1]" })
        {
            if (!SlidePathParser.TryParsePath(expression, out var parsed) ||
                !SlideSyntaxValidator.TryValidate(parsed, out var error))
                throw new Exception("Invalid route: " + expression);
            var owner = new GameObject("Mixed path verification");
            try
            {
                var slide = owner.AddComponent<TouchSlideDrop>();
                slide.pathSegments = parsed.segments;
                if (!(bool)typeof(TouchSlideDrop).GetMethod("TryBuildExpressionPath", fields).Invoke(slide, null))
                    throw new Exception("Unable to build route: " + expression);
                var points = (List<Vector3>)typeof(TouchSlideDrop).GetField("path", fields).GetValue(slide);
                var positionMethod = typeof(TouchSlideDrop).GetMethod("AreaPosition", BindingFlags.NonPublic | BindingFlags.Static);
                Vector3 Position(SlidePositionData position) => (Vector3)positionMethod.Invoke(null, new object[] { position.area, position.position, position.isDZone });
                if (Vector3.Distance(points[0], Position(parsed.head)) > .001f ||
                    Vector3.Distance(points[^1], Position(parsed.segments[^1].end)) > .001f)
                    throw new Exception("Mixed path lost its start or end: " + expression);
                if (parsed.segments.Count > 1)
                {
                    var joint = Position(parsed.segments[0].end);
                    if (!points.Exists(point => Vector3.Distance(point, joint) < .001f))
                        throw new Exception("Mixed path lost its joint: " + expression);
                }
                Debug.Log($"089 ROUTE PASS: {expression}, {points.Count} points, start/end/joint retained.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
        AlphaCommandLineBuild.BuildView();
    }
}
