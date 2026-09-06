using System;
using System.Collections.Generic;
using Assets.Scripts.Notes;
using MajdataCore;
using UnityEngine;

/// <summary>
/// A visual-only note that travels along a borrowed slide path.
/// It owns no slide bars, sensors, judgement state, or regular slide lifetime.
/// </summary>
public sealed class TrajectoryCarrierDrop : NoteDrop
{
    public string carrierVisualType = "tap";
    public bool bodyBreak;
    public bool bodyMine;

    private readonly List<Vector3> path = new();
    private readonly List<float> distances = new();
    private SpriteRenderer carrier;
    private GameObject guideLine;
    private SpriteRenderer guideLineRenderer;
    private float moveStart;
    private float duration;
    private float totalDistance;
    private Vector2 visualScale = Vector2.one;

    public override string LiveGuideStarVisualType => carrierVisualType;

    public void Configure(
        JsonDataLoader routeSource,
        IReadOnlyList<SlidePathSegmentData> segments,
        Sprite sprite,
        Material material,
        Vector2 scale,
        float revealTime,
        float movementStart,
        float movementDuration,
        int guideSortingOrder)
    {
        path.Clear();
        distances.Clear();
        if (!TrajectoryPathGeometry.TryBuild(routeSource, segments, path))
        {
            ReportUnrenderable("borrowed trajectory could not build a visual path");
            enabled = false;
            return;
        }

        totalDistance = 0f;
        distances.Add(0f);
        for (var i = 1; i < path.Count; i++)
        {
            totalDistance += Vector3.Distance(path[i - 1], path[i]);
            distances.Add(totalDistance);
        }

        time = revealTime;
        moveStart = movementStart;
        duration = Mathf.Max(0.01f, movementDuration);
        visualScale = scale;

        carrier = GetComponent<SpriteRenderer>() ??
                  gameObject.AddComponent<SpriteRenderer>();
        carrier.sprite = ResolveCustomSkin(sprite) ?? sprite;
        if (material != null)
            carrier.sharedMaterial = material;
        // The carrier is a note head, not part of the route. Keeping it on the
        // Notes layer guarantees that both the route and its moving guide star
        // remain behind it throughout the inherited trajectory.
        carrier.sortingLayerName = "Notes";
        carrier.sortingOrder = 1000;
        carrier.forceRenderingOff = true;
        CreateGuideLine(routeSource, material, guideSortingOrder);
        SetVisualScale(scale);
        SetPose(0f);
        InvalidateLiveVisual();
    }

    private void Start()
    {
        timeProvider = GameObject.Find("AudioTimeProvider")?
            .GetComponent<AudioTimeProvider>();
    }

    private void Update()
    {
        if (carrier == null || timeProvider == null || path.Count < 2)
            return;

        var now = timeProvider.AudioTime;
        // A standalone caret preview starts at the carrier's reveal time. A paused
        // timeline preview must remain reversible, including hiding before reveal
        // when the user drags backward.
        var sampleTime = IsPausedTimelinePreview
            ? now
            : previewOnly ? Mathf.Max(now, time) : now;
        var visible = (timeProvider.isStart || timeProvider.IsPaused) &&
                      sampleTime >= time &&
                      sampleTime <= moveStart + duration;
        carrier.forceRenderingOff = !visible;
        if (guideLine != null)
            guideLine.SetActive(visible);
        if (!visible)
            return;

        var progress = sampleTime < moveStart
            ? 0f
            : SvController.GetTypedOnlyProgress(
                moveStart,
                duration,
                sampleTime,
                SvController.ForSameStream(scrollType, "slide"));
        SetPose(progress);
    }

    public override void ApplyLiveScale(Vector2? scale)
    {
        SetVisualScale(scale ?? visualScale);
    }

    private void SetVisualScale(Vector2 scale)
    {
        var prefabScale = carrierVisualType == "star" ? 1.5f : 1f;
        transform.localScale = new Vector3(
            prefabScale * scale.x,
            prefabScale * scale.y,
            1f);
    }

    private void SetPose(float progress)
    {
        transform.localPosition = Evaluate(progress);
        UpdateGuideLine();
        var tangent = EvaluateTangent(progress);
        if (carrierVisualType is "hold" or "touchhold" or "star")
        {
            var angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
            transform.localRotation = Quaternion.Euler(0f, 0f, angle + 18f);
        }
        else
        {
            transform.localRotation = Quaternion.identity;
        }
    }

    private void CreateGuideLine(
        JsonDataLoader routeSource,
        Material material,
        int sortingOrder)
    {
        if (routeSource == null || routeSource.notes == null ||
            carrierVisualType is "touch" or "touchhold")
            return;

        GameObject template;
        Sprite eachSprite;
        Sprite breakSprite;
        if (carrierVisualType == "hold")
        {
            var source = routeSource.holdPrefab?.GetComponent<HoldDrop>();
            template = source?.tapLine;
            eachSprite = source?.eachLine;
            breakSprite = source?.breakLine;
        }
        else
        {
            var prefab = carrierVisualType == "star"
                ? routeSource.starPrefab
                : routeSource.tapPrefab;
            var source = prefab?.GetComponent<TapBase>();
            template = source?.tapLine;
            eachSprite = source?.eachLine;
            breakSprite = source?.breakLine;
        }

        if (template == null)
            return;
        guideLine = Instantiate(template, routeSource.notes.transform);
        guideLine.name = $"{name}_GuideLine";
        guideLineRenderer = guideLine.GetComponent<SpriteRenderer>();
        if (guideLineRenderer != null)
        {
            if (bodyBreak && breakSprite != null)
                guideLineRenderer.sprite = breakSprite;
            else if (isEach && eachSprite != null)
                guideLineRenderer.sprite = eachSprite;
            if (material != null)
                guideLineRenderer.sharedMaterial = material;
            guideLineRenderer.sortingLayerName = "Slide";
            guideLineRenderer.sortingOrder = sortingOrder;
        }
        guideLine.SetActive(false);
    }

    private void UpdateGuideLine()
    {
        if (guideLine == null)
            return;
        var position = transform.localPosition;
        var radius = position.magnitude;
        guideLine.SetActive(
            carrier != null && !carrier.forceRenderingOff && radius > 0.001f);
        guideLine.transform.localScale = Vector3.one *
            (radius / DefaultDestroyRadius);
        guideLine.transform.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(position.y, position.x) * Mathf.Rad2Deg - 90f);
    }

    private void OnDestroy()
    {
        if (guideLine != null)
            Destroy(guideLine);
    }

    private Vector3 Evaluate(float progress)
    {
        if (path.Count == 0)
            return Vector3.zero;
        if (path.Count == 1 || totalDistance <= 0.0001f)
            return path[0];

        var target = Mathf.Clamp01(progress) * totalDistance;
        var upper = distances.BinarySearch(target);
        if (upper < 0)
            upper = ~upper;
        if (upper <= 0)
            return path[0];
        if (upper >= path.Count)
            return path[^1];
        var lower = upper - 1;
        var span = distances[upper] - distances[lower];
        var amount = span <= 0.0001f
            ? 0f
            : (target - distances[lower]) / span;
        return Vector3.Lerp(path[lower], path[upper], amount);
    }

    private Vector3 EvaluateTangent(float progress)
    {
        if (path.Count < 2 || totalDistance <= 0.0001f)
            return Vector3.right;
        var target = Mathf.Clamp01(progress) * totalDistance;
        var upper = distances.BinarySearch(target);
        if (upper < 0)
            upper = ~upper;
        upper = Mathf.Clamp(upper, 1, path.Count - 1);
        var tangent = path[upper] - path[upper - 1];
        return tangent.sqrMagnitude > 0.000001f
            ? tangent
            : Vector3.right;
    }
}

internal static class TrajectoryPathGeometry
{
    private const int SamplesPerSegment = 256;

    public static bool TryBuild(
        JsonDataLoader routeSource,
        IReadOnlyList<SlidePathSegmentData> segments,
        List<Vector3> result)
    {
        result.Clear();
        if (segments == null || segments.Count == 0)
            return false;

        foreach (var segment in segments)
        {
            if (segment.shape == "SC")
            {
                var route = new List<Vector3>();
                if (!SlideCodePathGeometry.TryBuild(segment.slideCode, route))
                    return false;
                foreach (var point in route)
                    Add(result, point);
                continue;
            }
            var start = Resolve(segment.start, segment.startPosition, segment.startIsDZone);
            var end = Resolve(segment.end, segment.endPosition, segment.endIsDZone);
            if (segment.shape is "P" or "Q" && segment.hasMiddle)
            {
                var orbit = Resolve(
                    segment.middle,
                    segment.middlePosition,
                    segment.middleIsDZone);
                SelectableOrbitPathGeometry.Append(
                    routeSource,
                    result,
                    start.Area,
                    start.Position,
                    start.IsDZone,
                    orbit.Area,
                    orbit.Position,
                    orbit.IsDZone,
                    segment.middle.source?.Length == 1 &&
                    char.IsDigit(segment.middle.source[0]),
                    end.Area,
                    end.Position,
                    end.IsDZone,
                    segment.shape);
                continue;
            }
            if (start.Area == 'K' && end.Area == 'K' &&
                routeSource != null &&
                routeSource.TryGetSlideVisualRoute(
                    segment.ToExpression(includeDZone: true), out var exact) &&
                exact.Count > 0)
            {
                Add(result, Position(start.Area, start.Position, start.IsDZone));
                foreach (var point in exact)
                    Add(result, point);
                Add(result, Position(end.Area, end.Position, end.IsDZone));
                continue;
            }

            AppendFallback(routeSource, segment, start, end, result);
        }
        return result.Count >= 2;
    }

    private static void AppendFallback(
        JsonDataLoader routeSource,
        SlidePathSegmentData segment,
        (char Area, int Position, bool IsDZone) start,
        (char Area, int Position, bool IsDZone) end,
        List<Vector3> result)
    {
        var startPoint = Position(start.Area, start.Position, start.IsDZone);
        var endPoint = Position(end.Area, end.Position, end.IsDZone);
        var first = result.Count == 0 ? 0 : 1;
        if (segment.shape == "V" && segment.hasMiddle)
        {
            var middle = Resolve(
                segment.middle, segment.middlePosition, segment.middleIsDZone);
            AppendLine(result, startPoint,
                Position(middle.Area, middle.Position, middle.IsDZone), first,
                SamplesPerSegment / 2);
            AppendLine(result,
                Position(middle.Area, middle.Position, middle.IsDZone), endPoint,
                1, SamplesPerSegment / 2);
            return;
        }
        if (segment.shape == "-" || segment.shape == "v")
        {
            if (segment.shape == "v")
            {
                AppendLine(result, startPoint, Vector3.zero, first,
                    SamplesPerSegment / 2);
                AppendLine(result, Vector3.zero, endPoint, 1,
                    SamplesPerSegment / 2);
            }
            else
            {
                AppendLine(result, startPoint, endPoint, first,
                    SamplesPerSegment);
            }
            return;
        }

        if (routeSource != null &&
            routeSource.TryGetSlideVisualRoute(
                $"{start.Position}{segment.shape}{end.Position}[4:1]",
                out var sourcePath) && sourcePath.Count > 0)
        {
            var originalStart = Position('K', start.Position, start.IsDZone);
            var originalEnd = Position('K', end.Position, end.IsDZone);
            for (var i = first; i <= sourcePath.Count + 1; i++)
            {
                var source = i == 0 ? originalStart :
                    i == sourcePath.Count + 1 ? originalEnd : sourcePath[i - 1];
                var t = i / (float)(sourcePath.Count + 1);
                var offset = Vector3.Lerp(
                    startPoint - originalStart,
                    endPoint - originalEnd,
                    t);
                Add(result, source + offset);
            }
            return;
        }

        AppendArc(result, startPoint, endPoint, segment.shape, start.Position, first);
    }

    private static void AppendArc(
        List<Vector3> result,
        Vector3 start,
        Vector3 end,
        string shape,
        int startPosition,
        int first)
    {
        var direction = shape.StartsWith("<", StringComparison.Ordinal) ? '<' : '>';
        var loops = Math.Max(1, shape.Length);
        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var delta = (float)TouchSlideDirection.Sweep(startAngle, endAngle, startPosition, direction);
        var sign = Mathf.Sign(delta);
        delta += sign * Mathf.PI * 2f * (loops - 1);
        var samples = SamplesPerSegment * loops;
        for (var i = first; i <= samples; i++)
        {
            var t = i / (float)samples;
            var angle = startAngle + delta * t;
            var radius = Mathf.Lerp(start.magnitude, end.magnitude, t);
            Add(result, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }

    private static void AppendLine(
        List<Vector3> result,
        Vector3 start,
        Vector3 end,
        int first,
        int samples)
    {
        for (var i = first; i <= samples; i++)
            Add(result, Vector3.Lerp(start, end, i / (float)samples));
    }

    private static void Add(List<Vector3> result, Vector3 point)
    {
        if (result.Count == 0 ||
            (result[^1] - point).sqrMagnitude > 0.000001f)
            result.Add(point);
    }

    private static (char Area, int Position, bool IsDZone) Resolve(
        SlidePositionData parsed,
        int legacyPosition,
        bool legacyDZone)
    {
        return parsed != null && parsed.position != 0
            ? (parsed.area, parsed.position, parsed.isDZone)
            : ('K', legacyPosition, legacyDZone);
    }

    private static Vector3 Position(char area, int index, bool dZone)
    {
        if (area == 'C')
            return Vector3.zero;
        var angleOffset = area is 'A' or 'B' or 'K'
            ? Mathf.PI * 5f / 8f
            : Mathf.PI * 6f / 8f;
        if (area == 'K' && dZone)
            angleOffset += Mathf.PI / 8f;
        var radius = area switch
        {
            'K' => 4.8f,
            'A' or 'D' => 4.1f,
            'B' => 2.3f,
            'E' => 3f,
            _ => 0f
        };
        var angle = -index * Mathf.PI / 4f + angleOffset;
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
}
