using System;
using System.Collections.Generic;
using Assets.Scripts.Notes;
using MajdataCore;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A visual-only note that travels along a borrowed slide path.
/// It owns no slide bars, sensors, judgement state, or regular slide lifetime.
/// </summary>
public sealed class TrajectoryCarrierDrop : NoteDrop
{
    public string carrierVisualType = "tap";
    public bool isFakeStarRotate;
    public float tapBaseScale = 1f;
    public bool bodyBreak;
    public bool bodyMine;
    public float holdLength;
    public SpriteDrawMode holdDrawMode;
    public Sprite exSprite;
    public SpriteRenderer exTemplate;
    public Color exColor = Color.white;

    private readonly List<Vector3> path = new();
    private readonly List<float> distances = new();
    private SpriteRenderer carrier;
    private SpriteRenderer exRenderer;
    private SpriteRenderer holdTail;
    private GameObject guideLine;
    private SpriteRenderer guideLineRenderer;
    private float moveStart;
    private float duration;
    private float totalDistance;
    private Vector2 visualScale = Vector2.one;
    private float starRotation;
    private bool movingBackward;
    private readonly List<SpriteRenderer> petals = new();
    private SpriteRenderer touchPoint;
    private SpriteRenderer touchHoldRing;
    private SpriteMask touchHoldMask;

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
        int guideSortingOrder,
        CustomSkin skin)
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
        if (carrierVisualType == "hold")
        {
            carrier.drawMode = holdDrawMode;
            carrier.size = new Vector2(1.22f, 1.4f + holdLength);
            if (holdLength > 0.001f)
            {
                var holdPrefab = routeSource.holdPrefab;
                var hold = holdPrefab.GetComponent<HoldDrop>();
                // Reuse the ordinary Hold's end marker so its trailing end is distinguishable.
                holdTail = Instantiate(holdPrefab.transform.GetChild(1).GetComponent<SpriteRenderer>(), transform, false);
                holdTail.name = "Hold Tail";
                holdTail.transform.localPosition = new Vector3(0f, 0.6825f - carrier.size.y * .5f);
                if (!bodyMine)
                {
                    if (bodyBreak) holdTail.sprite = hold.holdBreakEnd;
                    else if (isEach) holdTail.sprite = hold.holdEachEnd;
                }
                holdTail.sortingLayerName = carrier.sortingLayerName;
                holdTail.sortingOrder = carrier.sortingOrder + 1;
                if (material != null) holdTail.sharedMaterial = material;
                holdTail.forceRenderingOff = true;
            }
        }
        if (exSprite != null)
        {
            exRenderer = new GameObject("EX").AddComponent<SpriteRenderer>();
            exRenderer.transform.SetParent(transform, false);
            exRenderer.transform.localPosition = exTemplate.transform.localPosition;
            exRenderer.transform.localRotation = exTemplate.transform.localRotation;
            exRenderer.transform.localScale = exTemplate.transform.localScale;
            exRenderer.sprite = exSprite;
            exRenderer.sharedMaterial = exTemplate.sharedMaterial;
            exRenderer.color = exColor;
            exRenderer.sortingLayerName = carrier.sortingLayerName;
            exRenderer.sortingOrder = carrier.sortingOrder - 1;
            exRenderer.forceRenderingOff = true;
            if (carrierVisualType == "hold") { exRenderer.drawMode = holdDrawMode; exRenderer.size = carrier.size; }
            if (material != null && material.HasProperty("_NoteAlpha"))
                SetInitialRendererAlpha(exRenderer, material.GetFloat("_NoteAlpha"));
        }
        if (carrierVisualType is "touch" or "touchhold")
        {
            var isTouchHold = carrierVisualType == "touchhold";
            var breakSkin = bodyBreak && !bodyMine;
            var touchHold = isTouchHold ? routeSource.touchHoldPrefab.GetComponent<TouchHoldDrop>() : null;
            var fans = isTouchHold ? touchHold.fans : routeSource.touchPrefab.GetComponent<TouchDrop>().fans;
            var holdSprites = breakSkin ? skin.TouchHold_Break : skin.TouchHold;
            for (var i = 0; i < 4; i++)
            {
                var petal = new GameObject("Petal" + i).AddComponent<SpriteRenderer>();
                petal.transform.SetParent(transform, false);
                petal.transform.localRotation = fans[i].transform.localRotation;
                petal.transform.localScale = fans[i].transform.localScale;
                petal.sprite = isTouchHold
                    ? ResolveCustomSkin(holdSprites[i]) ?? holdSprites[i]
                    : carrier.sprite;
                petal.sharedMaterial = carrier.sharedMaterial;
                petal.sortingLayerName = carrier.sortingLayerName;
                petal.sortingOrder = carrier.sortingOrder;
                petal.forceRenderingOff = true;
                petals.Add(petal);
            }
            touchPoint = Instantiate(fans[4].GetComponent<SpriteRenderer>(), transform, false);
            touchPoint.sprite = breakSkin ? skin.TouchPoint_Break
                : isEach && !bodyMine ? skin.TouchPoint_Each : skin.TouchPoint;
            touchPoint.sharedMaterial = carrier.sharedMaterial;
            touchPoint.sortingLayerName = carrier.sortingLayerName;
            touchPoint.sortingOrder = carrier.sortingOrder + 1;
            touchPoint.forceRenderingOff = true;
            if (isTouchHold)
            {
                SetPetalPositions(0f, Mathf.PI / 4f);
                var group = gameObject.AddComponent<SortingGroup>();
                group.sortingLayerID = carrier.sortingLayerID;
                group.sortingOrder = carrier.sortingOrder;
                touchHoldRing = Instantiate(fans[5].GetComponent<SpriteRenderer>(), transform, false);
                touchHoldRing.sprite = holdSprites[4];
                touchHoldRing.sortingLayerID = group.sortingLayerID;
                if (material != null) touchHoldRing.sharedMaterial = material;
                touchHoldRing.forceRenderingOff = true;
                touchHoldMask = Instantiate(touchHold.mask, transform, false);
                TouchHoldDrop.ConfigureProgressMask(touchHoldMask, group.sortingLayerID);
                touchHoldMask.enabled = false;
            }
        }
        CreateGuideLine(routeSource, material, guideSortingOrder);
        SetVisualScale(scale);
        SetPose(0f);
        InvalidateLiveVisual();
    }

    private void Start()
    {
        if (timeProvider == null) timeProvider = GameObject.Find("AudioTimeProvider")?
            .GetComponent<AudioTimeProvider>();
    }

    private void Update()
    {
        if (carrier == null || timeProvider == null || path.Count < 2)
            return;

        var now = timeProvider.AudioTime;
        // Running playback retires visual carriers without any judgement/count
        // callback. Preview objects remain available when scrubbing backward.
        if (!previewOnly && timeProvider.isStart && !timeProvider.IsPaused &&
            now > moveStart + duration)
        {
            if (guideLine != null) guideLine.SetActive(false);
            carrier.forceRenderingOff = true;
            if (exRenderer != null) exRenderer.forceRenderingOff = true;
            if (holdTail != null) holdTail.forceRenderingOff = true;
            Destroy(gameObject);
            return;
        }
        // A standalone caret preview starts at the carrier's reveal time. A paused
        // timeline preview must remain reversible, including hiding before reveal
        // when the user drags backward.
        var sampleTime = IsPausedTimelinePreview
            ? now
            : previewOnly ? Mathf.Max(now, time) : now;
        var visible = (timeProvider.isStart || timeProvider.IsPaused) &&
                      sampleTime >= time &&
                      sampleTime <= moveStart + duration;
        carrier.forceRenderingOff = !visible || petals.Count > 0;
        if (exRenderer != null) exRenderer.forceRenderingOff = !visible;
        if (holdTail != null) holdTail.forceRenderingOff = !visible;
        if (touchPoint != null) touchPoint.forceRenderingOff = !visible;
        foreach (var petal in petals) petal.forceRenderingOff = !visible;
        if (touchHoldMask != null)
        {
            var elapsed = Mathf.InverseLerp(moveStart, moveStart + duration, sampleTime);
            touchHoldMask.alphaCutoff = TouchHoldDrop.FullProgressCutoff * elapsed;
            touchHoldMask.enabled = visible && elapsed > 0f;
            touchHoldRing.forceRenderingOff = !touchHoldMask.enabled;
        }
        if (guideLine != null)
            guideLine.SetActive(visible);
        if (!visible)
            return;

        if (carrierVisualType == "star" && isFakeStarRotate &&
            timeProvider.isStart && !timeProvider.IsPaused)
            starRotation += 400f * Time.deltaTime;
        var motionType = SvController.ForSameStream(scrollType, "slide");
        var progress = sampleTime < moveStart
            ? 0f
            : SvController.GetTypedOnlyProgress(
                moveStart,
                duration,
                sampleTime,
                motionType);
        movingBackward = carrierVisualType == "hold" && sampleTime >= moveStart &&
                         SvController.GetTypedOnlyMultiplier(sampleTime, motionType) < 0f;
        SetPose(progress);
    }

    public override void ApplyLiveScale(Vector2? scale)
    {
        SetVisualScale(scale ?? visualScale);
    }

    private void SetVisualScale(Vector2 scale)
    {
        var prefabScale = tapBaseScale;
        transform.localScale = new Vector3(
            prefabScale * scale.x,
            prefabScale * scale.y,
            1f);
    }

    private void SetPose(float progress)
    {
        var head = Evaluate(progress, out var tangent);
        transform.localPosition = head;
        UpdateGuideLine(head);
        if (carrierVisualType == "hold")
        {
            var angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
            transform.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            // The original Hold places its judgement head half a body-length
            // ahead of the sprite centre. Keep that head exactly on the route.
            transform.localPosition = head - tangent * (holdLength * transform.localScale.y * .5f);
        }
        else if (carrierVisualType == "star")
        {
            // A borrowed route changes position, not the note's authored facing.
            var origin = path[0];
            var angle = Mathf.Atan2(origin.y, origin.x) * Mathf.Rad2Deg - 90f;
            transform.localRotation = Quaternion.Euler(0f, 0f, angle + starRotation);
        }
        else
        {
            transform.localRotation = Quaternion.identity;
        }
        if (carrierVisualType == "touch")
        {
            var distance = Mathf.Clamp(-Mathf.Exp(3.2f * (Mathf.Clamp01(progress) - 1f) - .85f) + .42f, 0f, .4f);
            SetPetalPositions(distance, 0f);
        }
    }

    private void SetPetalPositions(float distance, float angleOffset)
    {
        for (var i = 0; i < petals.Count; i++)
        {
            var angle = angleOffset + i * Mathf.PI / 2f;
            petals[i].transform.localPosition = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle)) * (.226f + distance);
        }
    }

    internal override float AngleHeadOffset => carrierVisualType == "hold" ? holdLength * .5f : 0f;

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
            InitializeTapLineAlpha(guideLineRenderer);
        }
        guideLine.SetActive(false);
    }

    private void UpdateGuideLine(Vector3 position)
    {
        if (guideLine == null)
            return;
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

    private Vector3 Evaluate(float progress, out Vector3 tangent)
    {
        tangent = Vector3.up;
        if (path.Count == 1)
            return path[0];

        var target = Mathf.Clamp01(progress) * totalDistance;
        var upper = distances.BinarySearch(target);
        // At a vertex, take the segment the head is about to travel along.
        upper = Mathf.Clamp(upper < 0 ? ~upper : upper + (movingBackward ? 0 : 1), 1, path.Count - 1);
        var lower = upper - 1;
        var span = distances[upper] - distances[lower];
        var amount = (target - distances[lower]) / span;
        tangent = (path[upper] - path[lower]) / span;
        if (movingBackward) tangent = -tangent;
        return Vector3.Lerp(path[lower], path[upper], amount);
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
            if ((start.Area != 'K' || end.Area != 'K' || start.IsDZone || end.IsDZone) &&
                (segment.shape.StartsWith("<", StringComparison.Ordinal) ||
                 segment.shape.StartsWith(">", StringComparison.Ordinal)))
            {
                AppendArc(result,
                    Position(start.Area, start.Position, start.IsDZone),
                    Position(end.Area, end.Position, end.IsDZone),
                    segment.shape, start.Position, result.Count == 0 ? 0 : 1,
                    start.Area == 'C' || end.Area == 'C');
                continue;
            }
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
                !start.IsDZone && !end.IsDZone &&
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

        if ((start.Area != 'K' || end.Area != 'K') &&
            (segment.shape.StartsWith("<", StringComparison.Ordinal) ||
             segment.shape.StartsWith(">", StringComparison.Ordinal)))
        {
            AppendArc(result, startPoint, endPoint, segment.shape, start.Position, first);
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
        int first,
        bool touchesCenter = false)
    {
        var direction = shape.StartsWith("<", StringComparison.Ordinal) ? '<' : '>';
        var loops = Math.Max(1, shape.Length);
        // TouchSlide uses a quadratic curve for a single arc into/out of C.
        if (touchesCenter && loops == 1)
        {
            var offset = end - start;
            var control = (start + end) * .5f + new Vector3(-offset.y, offset.x) * (.35f * (direction == '<' ? -1f : 1f));
            for (var i = first; i <= SamplesPerSegment; i++)
            {
                var t = i / (float)SamplesPerSegment;
                Add(result, (1f - t) * (1f - t) * start + 2f * (1f - t) * t * control + t * t * end);
            }
            return;
        }
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
        return parsed != null && (parsed.area == 'C' || parsed.position != 0)
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
