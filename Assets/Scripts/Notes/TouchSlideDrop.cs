using Assets.Scripts;
using Assets.Scripts.Types;
using MajdataCore;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public sealed class TouchSlideDrop : NoteDrop
{
    public char startArea = 'E';
    public char endArea = 'E';
    public int endPosition;
    public bool isDZoneEnd;
    public char shape = '-';
    public string pathExpression;
    public List<SlidePathSegmentData> pathSegments;
    public bool bodyBreak;
    public bool bodyMine;
    public bool suppressGuideStarFadeIn;
    public float timeStart;
    public float duration = 1f;
    public float starSpeed;
    public int sortingOrder;
    public Sprite pathSprite;
    public GameObject star;
    public GameObject barTemplate;
    public Material pathMaterial;
    public Material starMaterial;
    public GameObject judgeTemplate;
    public RuntimeAnimatorController judgeBreakShine;
    public JsonDataLoader slideRouteSource;
    public Vector2 barScale = Vector2.one;
    public Vector2 starScale = Vector2.one;
    private Vector2? liveBarScaleDefault;
    private Vector2? liveStarScaleDefault;

    private readonly List<Vector3> path = new();
    private readonly List<float> pathDistances = new();
    private readonly List<SpriteRenderer> bars = new();
    private readonly List<float> barProgress = new();
    private readonly List<SensorType> sensorRoute = new();
    private readonly HashSet<SensorType> boundSensors = new();
    private SpriteRenderer starRenderer;
    private float starRotationOffset;
    private int routeIndex;
    private bool headTriggered;
    private bool finalized;
    private float totalPathLength;
    private float trailLeadProgress;
    private bool initialized;
    private bool completed;
    private bool isSlideCode;
    private bool endsWithCircle;
    private bool endCounterClockwise;
    private GameObject judgeVisual;
    private SlideAreaRawData[] codeAreas;
    private readonly List<JudgeArea> codeJudgeAreas = new();
    private float lastBarAlpha = -1f;
    private int hiddenBarCount;
    private string motionScrollType;

    public override void ApplyLiveScale(Vector2? scale)
    {
        liveBarScaleDefault ??= barScale;
        var previous = barScale;
        var value = scale ?? liveBarScaleDefault.Value;
        barScale = value;
        if (previous.x == 0f || previous.y == 0f)
            return;

        var ratio = new Vector3(value.x / previous.x, value.y / previous.y, 1f);
        foreach (var renderer in bars)
            if (renderer != null)
                renderer.transform.localScale = Vector3.Scale(renderer.transform.localScale, ratio);
    }

    // COLOR paints a slide's route and leaves the moving guide star to the "star"
    // category. The live commands have to paint exactly the same thing, and the
    // route is not a child of the note object, so nothing generic can reach it:
    // this used to hand back only what the base class found, which is why COLORV
    // left the arc untouched while COLOR coloured it.
    protected override IEnumerable<SpriteRenderer> GetLiveVisualRenderers()
    {
        foreach (var renderer in base.GetLiveVisualRenderers())
            if (renderer != null && renderer != starRenderer)
                yield return renderer;
        foreach (var renderer in bars)
            if (renderer != null)
                yield return renderer;
    }

    public override void ApplyLiveGuideStarColor(Color? color) =>
        ApplyLiveRendererColor(starRenderer, color);

    public override void ApplyLiveGuideStarAlpha(float? alpha) =>
        ApplyLiveRendererAlpha(starRenderer, alpha);

    public override void ApplyLiveGuideStarScale(Vector2? scale)
    {
        liveStarScaleDefault ??= starScale;
        starScale = scale ?? liveStarScaleDefault.Value;
    }

    private void Start() => Initialize();

    public void Initialize()
    {
        if (initialized)
            return;
        initialized = true;
        motionScrollType = SvController.ForSameStream(scrollType, "slide");
        if (timeProvider == null) timeProvider = GameObject.Find("AudioTimeProvider")?.GetComponent<AudioTimeProvider>();
        inputManager = GameObject.Find("Input")?.GetComponent<InputManager>();
        if (objectCounter == null) objectCounter = GameObject.Find("ObjectCounter")?.GetComponent<ObjectCounter>();

        BuildPath();
        // Update bails out below a two-point path, so a touch slide that lands
        // here draws nothing, forever, without a word. The chart text was legal
        // to get this far, which is exactly why it has to be said out loud.
        if (path.Count < 2)
            ReportUnrenderable(
                $"touch slide path '{pathExpression}' resolved to " +
                $"{path.Count} point(s), nothing will be drawn");
        BuildSensorRoute();
        CreateVisuals();
        CreateJudgeVisual();
        InvalidateLiveVisual();

        if (JudgmentDisabled || inputManager == null)
            return;
        foreach (var sensorType in sensorRoute)
        {
            if (boundSensors.Add(sensorType))
                inputManager.BindSensor(OnSensorChanged, sensorType);
        }
    }

    private void Update()
    {
        if (timeProvider == null || path.Count < 2 || starRenderer == null)
            return;

        var now = timeProvider.AudioTime;
        if (now <= timeStart)
        {
            SetBarAlpha(GetBodyAlpha(now), 0f);
            if (starRenderer != null)
                starRenderer.color = Color.clear;
            star.SetActive(false);
            return;
        }
        // A paused preview never judges, so the path has to retire itself once the
        // caret is past it, exactly like Tap/Hold/Touch do.
        if (IsPausedTimelinePreview && now > time + Mathf.Max(0f, duration))
        {
            SetBarAlpha(0f, 0f);
            starRenderer.color = Color.clear;
            star.SetActive(false);
            return;
        }

        star.SetActive(true);
        if (now < time)
        {
            SetStarPose(0f);
            if (suppressGuideStarFadeIn)
            {
                starRenderer.color = Color.clear;
                star.SetActive(false);
                return;
            }
            var fadeStart = time + GetAppearanceStartOffset();
            var fadeEnd = Mathf.Min(fadeStart + 0.2f, time);
            var appearance = fadeEnd <= fadeStart
                ? now >= time ? 1f : 0f
                : Mathf.InverseLerp(fadeStart, fadeEnd, now);
            starRenderer.color = new Color(1f, 1f, 1f, appearance);
            star.transform.localScale = new Vector3(
                (appearance + 0.5f) * starScale.x,
                (appearance + 0.5f) * starScale.y,
                1f);
            SetBarAlpha(GetBodyAlpha(now), 0f);
            return;
        }

        if (!isSlideCode && headTriggered && routeIndex == 0 && sensorRoute.Count > 0)
            routeIndex = 1;

        var progress = SvController.GetTypedOnlyProgress(
            time, Mathf.Max(0.01f, duration), now,
            motionScrollType);
        starRenderer.color = Color.white;
        star.transform.localScale = new Vector3(
            1.5f * starScale.x,
            1.5f * starScale.y,
            1f);
        SetStarPose(progress);
        // Nothing judges a fake or preview path, so nothing consumes its trail
        // either: the star travels and the bars stay, the way a missed slide
        // looks. SlideDrop forces its bars back on for the same reason, and a
        // touch slide has no business ending up looking different.
        var trailProgress = progress;
        if (isSlideCode && InputManager.Mode == AutoPlayMode.Disable)
            trailProgress = routeIndex >= codeAreas.Length ? 1f
                : (float)((codeJudgeAreas[routeIndex].On
                    ? codeAreas[routeIndex].LengthAfterPush
                    : routeIndex > 0 ? codeAreas[routeIndex - 1].LengthAfterFinish : 0d) / totalPathLength);
        SetBarAlpha(1f, JudgmentDisabled ? 0f : trailProgress);

        if (!JudgmentDisabled && InputManager.Mode != AutoPlayMode.Disable)
            routeIndex = Mathf.Max(
                routeIndex,
                Mathf.CeilToInt(progress * (isSlideCode ? codeAreas.Length : sensorRoute.Count)));

        // SV controls only the visual path within the authored interval.
        // Judgement and lifetime always end at the chart's declared duration.
        if (now < time + Mathf.Max(0.01f, duration))
            return;
        if (JudgmentSuspended)
            return;
        if (JudgmentDisabled)
        {
            star.SetActive(false);
            return;
        }

        judgeResult = InputManager.Mode == AutoPlayMode.Random
            ? (JudgeType)UnityEngine.Random.Range(1, 14)
            : InputManager.Mode != AutoPlayMode.Disable || routeIndex >= (isSlideCode ? codeAreas.Length : sensorRoute.Count)
                ? JudgeType.Perfect
                : JudgeType.Miss;
        completed = true;
        Destroy(gameObject);
    }

    private void OnDisable()
    {
        // Key-head TouchSlides are disabled by their StarDrop when the paused
        // clock returns before reveal. The guide star is not a child of this
        // object, so hide it explicitly before this component stops updating.
        if (star != null)
            star.SetActive(false);
    }

    private float GetBodyAlpha(float now)
    {
        var fadeLeadScale = 1f - Mathf.Clamp(starSpeed, -1f, 1f);
        var motionSpeed = Mathf.Abs(speed);
        var fadeStart = motionSpeed <= 0.0001f
            ? timeStart
            : timeStart + (-3.926913f / motionSpeed) * fadeLeadScale;
        var fadeEnd = Mathf.Min(fadeStart + 0.2f, timeStart);
        return fadeEnd <= fadeStart
            ? now >= timeStart ? 1f : 0f
            : Mathf.InverseLerp(fadeStart, fadeEnd, now);
    }

    private void OnSensorChanged(object sender, InputEventArgs args)
    {
        if (JudgmentDisabled || JudgmentSuspended ||
            InputManager.Mode != AutoPlayMode.Disable || sensorRoute.Count == 0)
            return;

        if (isSlideCode)
        {
            if (timeProvider.AudioTime < time || routeIndex >= codeJudgeAreas.Count)
                return;
            var current = codeJudgeAreas[routeIndex];
            current.Judge(args.Type, args.Status);
            if (routeIndex + 1 < codeJudgeAreas.Count)
            {
                var next = codeJudgeAreas[routeIndex + 1];
                next.Judge(args.Type, args.Status);
                if (next.On)
                    routeIndex++;
            }
            if (routeIndex < codeJudgeAreas.Count && codeJudgeAreas[routeIndex].IsFinished)
                routeIndex++;
            return;
        }
        if (args.Status != SensorStatus.On)
            return;

        if (timeProvider != null && timeProvider.AudioTime < time)
        {
            if (args.Type == sensorRoute[0])
                headTriggered = true;
            return;
        }

        if (routeIndex < sensorRoute.Count && args.Type == sensorRoute[routeIndex])
            routeIndex++;
    }

    private void BuildPath()
    {
        path.Clear();
        pathDistances.Clear();
        totalPathLength = 0f;

        if (!TryBuildExpressionPath())
            AppendSegment(
                startArea, startPosition, isDZone,
                endArea, endPosition, isDZoneEnd,
                shape.ToString());

        if (path.Count == 0)
            return;
        pathDistances.Add(0f);
        for (var i = 1; i < path.Count; i++)
        {
            totalPathLength += Vector3.Distance(path[i - 1], path[i]);
            pathDistances.Add(totalPathLength);
        }
    }

    private bool TryBuildExpressionPath()
    {
        var segments = pathSegments;
        if (segments == null || segments.Count == 0)
        {
            if (!SlidePathParser.TryParsePath(
                    pathExpression, out var parsedPath) ||
                !SlideSyntaxValidator.TryValidate(
                    parsedPath, out _))
                return false;
            segments = parsedPath.segments;
        }
        else if (!SlideSyntaxValidator.TryValidateSegments(
                     segments, out _))
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.shape == "SC")
            {
                if (!SlideCodePathGeometry.TryBuild(segment.slideCode, path))
                    return false;
                isSlideCode = true;
                if (SlideCodeParser.TryParse(segment.slideCode, out var code, out _))
                {
                    var lastRoute = code.instructions[code.instructions.Count - 2];
                    endsWithCircle = lastRoute.IsOrbit && lastRoute.parameter == 9;
                    endCounterClockwise = lastRoute.command == SlideCodeCommand.P;
                }
                continue;
            }
            var start = ResolvePosition(
                segment.start, segment.startPosition, segment.startIsDZone);
            var end = ResolvePosition(
                segment.end, segment.endPosition, segment.endIsDZone);
            if (segment.shape == "V")
            {
                var turn = ResolvePosition(
                    segment.middle,
                    segment.middlePosition,
                    segment.middleIsDZone);
                AppendSegment(
                    start.Area, start.Position, start.IsDZone,
                    turn.Area, turn.Position, turn.IsDZone, "-");
                AppendSegment(
                    turn.Area, turn.Position, turn.IsDZone,
                    end.Area, end.Position, end.IsDZone, "-");
                continue;
            }

            if (segment.shape is "P" or "Q")
            {
                var orbit = ResolvePosition(
                    segment.middle,
                    segment.middlePosition,
                    segment.middleIsDZone);
                AppendSelectableOrbitSegment(
                    start.Area, start.Position, start.IsDZone,
                    orbit.Area, orbit.Position, orbit.IsDZone,
                    segment.middle.source?.Length == 1 &&
                    char.IsDigit(segment.middle.source[0]),
                    end.Area, end.Position, end.IsDZone,
                    segment.shape);
                continue;
            }

            AppendSegment(
                start.Area, start.Position, start.IsDZone,
                end.Area, end.Position, end.IsDZone,
                segment.shape);
        }
        return path.Count >= 2;
    }

    private void AppendSelectableOrbitSegment(
        char startArea,
        int startPosition,
        bool startIsDZone,
        char orbitArea,
        int orbitPosition,
        bool orbitIsDZone,
        bool orbitIsNumber,
        char endArea,
        int endPosition,
        bool endIsDZone,
        string shape)
    {
        SelectableOrbitPathGeometry.Append(
            slideRouteSource,
            path,
            startArea,
            startPosition,
            startIsDZone,
            orbitArea,
            orbitPosition,
            orbitIsDZone,
            orbitIsNumber,
            endArea,
            endPosition,
            endIsDZone,
            shape);
    }

    private static (char Area, int Position, bool IsDZone) ResolvePosition(
        SlidePositionData parsed,
        int legacyPosition,
        bool legacyDZone)
    {
        return parsed != null && parsed.position != 0
            ? (parsed.area, parsed.position, parsed.isDZone)
            : ('K', legacyPosition, legacyDZone);
    }

    private void AppendSegment(
        char segmentStartArea,
        int segmentStartPosition,
        bool segmentStartIsDZone,
        char segmentEndArea,
        int segmentEndPosition,
        bool segmentEndIsDZone,
        string segmentShape)
    {
        const int baseSegments = 256;
        var isMultiLoopSpiral = segmentShape.Length > 1 &&
                                (segmentShape[0] == '<' || segmentShape[0] == '>');
        var loopCount = isMultiLoopSpiral ? segmentShape.Length : 1;
        var segments = baseSegments * loopCount;
        var start = AreaPosition(segmentStartArea, segmentStartPosition, segmentStartIsDZone);
        var end = AreaPosition(segmentEndArea, segmentEndPosition, segmentEndIsDZone);
        var firstSample = path.Count == 0 ? 0 : 1;
        if (segmentShape == "-")
        {
            for (var i = firstSample; i <= segments; i++)
                path.Add(Vector3.Lerp(start, end, i / (float)segments));
            return;
        }

        if (segmentShape == "v")
        {
            AppendLine(start, Vector3.zero, firstSample, segments / 2);
            AppendLine(Vector3.zero, end, 1, segments / 2);
            return;
        }

        if (segmentShape is "p" or "q")
        {
            if (!AppendOriginalTangentRoute(
                    segmentStartPosition,
                    segmentEndPosition,
                    segmentShape,
                    start,
                    end,
                    firstSample,
                    centerAtOrigin: true))
                AppendTangentCircle(start, end, segmentShape == "p", firstSample);
            return;
        }

        // rp and rq are key-slide shapes in their own right, so their authored route
        // can be inherited exactly the way pp's and qq's is. Without them named here
        // they fell through to the branches below and were drawn as another shape
        // entirely, which a mixed path like "1rp5d[8:1]-E7[8:1]" could reach.
        //
        // The fallback reads the last character rather than the first: it is 'p' or
        // 'q' for all four shapes, so pp and qq keep the direction they always had.
        if (segmentShape is "pp" or "qq" or "rp" or "rq")
        {
            if (!AppendOriginalTangentRoute(
                    segmentStartPosition,
                    segmentEndPosition,
                    segmentShape,
                    start,
                    end,
                    firstSample,
                    centerAtOrigin: false))
                AppendTangentCircle(start, end, segmentShape[^1] == 'p', firstSample);
            return;
        }

        if (loopCount == 1 && (segmentStartArea == 'C' || segmentEndArea == 'C'))
        {
            var bezierDirection = segmentShape.StartsWith("<", StringComparison.Ordinal) ? -1f : 1f;
            var delta = end - start;
            var control = (start + end) * 0.5f +
                          new Vector3(-delta.y, delta.x) * (0.35f * bezierDirection);
            for (var i = firstSample; i <= segments; i++)
            {
                var t = i / (float)segments;
                var inverse = 1f - t;
                path.Add(inverse * inverse * start +
                         2f * inverse * t * control +
                         t * t * end);
            }
            return;
        }

        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var deltaAngle = ResolveAngleDelta(
            startAngle,
            endAngle,
            segmentShape[0],
            segmentStartPosition);
        var arcDirection = Mathf.Sign(deltaAngle);
        if (loopCount > 1)
            deltaAngle += arcDirection * Mathf.PI * 2f * (loopCount - 1);
        var startRadius = start.magnitude;
        var endRadius = end.magnitude;
        for (var i = firstSample; i <= segments; i++)
        {
            var t = i / (float)segments;
            var angle = startAngle + deltaAngle * t;
            var radius = Mathf.Lerp(startRadius, endRadius, t);
            path.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }

    private void AppendLine(Vector3 start, Vector3 end, int firstSample, int segments)
    {
        for (var i = firstSample; i <= segments; i++)
            path.Add(Vector3.Lerp(start, end, i / (float)segments));
    }

    private bool AppendOriginalTangentRoute(
        int startPosition,
        int endPosition,
        string segmentShape,
        Vector3 actualStart,
        Vector3 actualEnd,
        int firstSample,
        bool centerAtOrigin)
    {
        if (slideRouteSource == null ||
            !slideRouteSource.TryGetSlideVisualRoute(
                $"{startPosition}{segmentShape}{endPosition}[4:1]",
                out var sourcePath) ||
            sourcePath.Count < 2)
            return false;

        var originalRoute = new Vector3[sourcePath.Count + 2];
        originalRoute[0] = AreaPosition('K', startPosition, false);
        for (var i = 0; i < sourcePath.Count; i++)
            originalRoute[i + 1] = sourcePath[i];
        originalRoute[^1] = AreaPosition('K', endPosition, false);

        // Touch routes need geometry samples, not a one-to-one remap of prefab
        // bars. Preserve both tangent points explicitly so the rendered
        // polyline cannot bridge across a line/arc join.
        var route = SlideDrop.BuildAdaptiveTangentCircleRoute(
            originalRoute,
            actualStart,
            actualEnd,
            centerAtOrigin);
        for (var i = firstSample; i < route.Length; i++)
            path.Add(route[i]);
        return true;
    }

    private void AppendTangentCircle(
        Vector3 start,
        Vector3 end,
        bool clockwise,
        int firstSample)
    {
        const float circleRadius = 2.3f;
        const float maxStep = 0.08f;
        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var startTangent = TangentAngle(start.magnitude, startAngle, clockwise, true);
        var endTangent = TangentAngle(end.magnitude, endAngle, clockwise, false);
        var tangentStart = new Vector3(Mathf.Cos(startTangent), Mathf.Sin(startTangent)) * circleRadius;
        var tangentEnd = new Vector3(Mathf.Cos(endTangent), Mathf.Sin(endTangent)) * circleRadius;
        AppendAdaptiveLine(start, tangentStart, firstSample == 0, maxStep);

        var sweep = clockwise
            ? -Mathf.Repeat(startTangent - endTangent, Mathf.PI * 2f)
            : Mathf.Repeat(endTangent - startTangent, Mathf.PI * 2f);
        var arcSegments = Math.Max(
            1,
            Mathf.CeilToInt(Mathf.Abs(sweep) * circleRadius / maxStep));
        for (var i = 1; i <= arcSegments; i++)
        {
            var angle = startTangent + sweep * (i / (float)arcSegments);
            path.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * circleRadius);
        }
        AppendAdaptiveLine(
            tangentEnd, end, includeStart: false, maxStep: maxStep);
    }

    private void AppendAdaptiveLine(
        Vector3 start,
        Vector3 end,
        bool includeStart,
        float maxStep)
    {
        var segments = Math.Max(
            1,
            Mathf.CeilToInt(Vector3.Distance(start, end) / maxStep));
        var first = includeStart ? 0 : 1;
        for (var i = first; i <= segments; i++)
            path.Add(Vector3.Lerp(start, end, i / (float)segments));
    }

    private static float TangentAngle(float radius, float pointAngle, bool clockwise, bool entering)
    {
        if (radius <= 2.3001f)
            return pointAngle;
        var offset = Mathf.Acos(2.3f / radius);
        var sign = clockwise == entering ? -1f : 1f;
        return pointAngle + sign * offset;
    }

    private void BuildSensorRoute()
    {
        if (isSlideCode)
        {
            codeAreas = SlideCodeJudgment.Build(totalPathLength, progress =>
            {
                var point = EvaluatePath((float)progress);
                return new System.Numerics.Complex(point.x, point.y);
            }, !endsWithCircle);
            for (var i = 0; i < codeAreas.Length; i++)
            {
                var area = codeAreas[i];
                var last = i == codeAreas.Length - 1;
                var types = new Dictionary<SensorType, bool> { [(SensorType)area.SensorA] = last };
                if (area.SensorB >= 0)
                    types[(SensorType)area.SensorB] = last;
                codeJudgeAreas.Add(new JudgeArea(types, i));
                foreach (var sensorType in types.Keys)
                    if (!sensorRoute.Contains(sensorType))
                        sensorRoute.Add(sensorType);
            }
            return;
        }
        var sensorsRoot = GameObject.Find("Sensors");
        if (sensorsRoot != null)
        {
            var regions = new List<(SensorType Type, Vector2 Position, float RadiusSquared)>();
            for (var i = 0; i < sensorsRoot.transform.childCount; i++)
            {
                var child = sensorsRoot.transform.GetChild(i);
                if (!child.TryGetComponent<Sensor>(out var sensor) ||
                    !child.TryGetComponent<RectTransform>(out var rect))
                    continue;
                var radius = Mathf.Max(rect.rect.width * Mathf.Abs(rect.lossyScale.x),
                    rect.rect.height * Mathf.Abs(rect.lossyScale.y)) * 0.5f + 0.15f;
                regions.Add((sensor.Type, rect.position, radius * radius));
            }
            foreach (var localPosition in path)
            {
                var worldPosition = transform.TransformPoint(localPosition);
                SensorType? nearest = null;
                var nearestDistance = float.MaxValue;
                foreach (var region in regions)
                {
                    var distance = ((Vector2)worldPosition - region.Position).sqrMagnitude;
                    if (distance <= region.RadiusSquared && distance < nearestDistance)
                    {
                        nearest = region.Type;
                        nearestDistance = distance;
                    }
                }

                if (nearest != null &&
                    (sensorRoute.Count == 0 || sensorRoute[^1] != nearest.Value))
                    sensorRoute.Add(nearest.Value);
            }
        }

        var startSensor = GetSensor(startArea, startPosition, isDZone);
        var endSensor = GetSensor(endArea, endPosition, isDZoneEnd);
        if (sensorRoute.Count == 0 || sensorRoute[0] != startSensor)
            sensorRoute.Insert(0, startSensor);
        if (sensorRoute[^1] != endSensor)
            sensorRoute.Add(endSensor);
    }

    private void CreateVisuals()
    {
        starRenderer = star != null ? star.GetComponent<SpriteRenderer>() : null;
        if (starRenderer == null)
            return;
        if (starMaterial != null)
            starRenderer.sharedMaterial = starMaterial;

        var templateRenderer = barTemplate != null
            ? barTemplate.GetComponent<SpriteRenderer>()
            : null;
        var templateScale = barTemplate != null
            ? barTemplate.transform.localScale
            : Vector3.one;
        var rotationOffset = GetTemplateRotationOffset();
        starRotationOffset = rotationOffset + 18f;
        var spacing = GetTemplateSpacing();
        var totalLength = GetPathLength();
        var count = Mathf.Max(2, Mathf.RoundToInt(totalLength / spacing));
        var actualSpacing = totalLength / count;
        // Bars are positioned by normalized path progress, but their footprint is
        // measured in world units. A fixed normalized lead made the disappearing
        // edge run farther ahead of the guide star on long arc routes. Half of the
        // actual center spacing puts the trailing edge of the first visible bar at
        // the star, independent of route shape and length.
        trailLeadProgress = AlphaVisualTiming.GetTouchSlideTrailLead(
            totalLength, actualSpacing);

        for (var i = 1; i < count; i++)
        {
            var distance = i * actualSpacing;
            var progress = totalLength > 0.0001f ? distance / totalLength : 0f;
            var barObject = new GameObject("TouchSlideBar");
            barObject.transform.SetParent(transform, false);
            var renderer = barObject.AddComponent<SpriteRenderer>();
            renderer.sprite = pathSprite != null ? pathSprite : templateRenderer?.sprite;
            renderer.sortingLayerName = "Slide";
            renderer.sortingOrder = sortingOrder--;
            if (pathMaterial != null)
                renderer.sharedMaterial = pathMaterial;

            barObject.transform.localPosition = EvaluatePath(progress);
            var tangent = EvaluateTangent(progress);
            var angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
            barObject.transform.localRotation = Quaternion.Euler(0f, 0f, angle + rotationOffset);
            barObject.transform.localScale = Vector3.Scale(
                templateScale,
                new Vector3(barScale.x, barScale.y, 1f));
            bars.Add(renderer);
            barProgress.Add(progress);
        }

        SetVisualAlpha(0f, 0f);
    }

    private float GetTemplateSpacing()
    {
        if (barTemplate?.transform.parent != null &&
            barTemplate.transform.parent.childCount > 1)
        {
            var sibling = barTemplate.transform.parent.GetChild(1);
            var distance = Vector3.Distance(
                barTemplate.transform.localPosition,
                sibling.localPosition);
            if (distance > 0.05f)
                return distance;
        }
        return 0.35f;
    }

    private float GetTemplateRotationOffset()
    {
        if (barTemplate?.transform.parent == null ||
            barTemplate.transform.parent.childCount < 2)
            return 0f;
        var sibling = barTemplate.transform.parent.GetChild(1);
        var direction = sibling.localPosition - barTemplate.transform.localPosition;
        var pathAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        return Mathf.DeltaAngle(pathAngle, barTemplate.transform.localEulerAngles.z);
    }

    private float GetPathLength()
    {
        return totalPathLength;
    }

    private Vector3 EvaluatePath(float progress)
    {
        if (path.Count == 0)
            return Vector3.zero;
        if (path.Count == 1 || totalPathLength <= 0.0001f)
            return path[0];

        var target = Mathf.Clamp01(progress) * totalPathLength;
        var upper = pathDistances.BinarySearch(target);
        if (upper >= 0)
            return path[upper];
        upper = ~upper;
        if (upper <= 0)
            return path[0];
        if (upper >= path.Count)
            return path[^1];
        var lower = upper - 1;
        var span = pathDistances[upper] - pathDistances[lower];
        var amount = span <= 0.0001f
            ? 0f
            : (target - pathDistances[lower]) / span;
        return Vector3.Lerp(path[lower], path[upper], amount);
    }

    private Vector3 EvaluateTangent(float progress)
    {
        if (path.Count < 2 || totalPathLength <= 0.0001f)
            return Vector3.right;

        var target = Mathf.Clamp01(progress) * totalPathLength;
        var upper = pathDistances.BinarySearch(target);
        if (upper >= 0)
        {
            // Keep the guide star aligned with the first half while it is
            // exactly on a sharp corner (for example a D-zone v through C).
            // Averaging both sides creates a false bisector direction.
            for (var previous = upper - 1; previous >= 0; previous--)
            {
                var incoming = path[upper] - path[previous];
                if (incoming.sqrMagnitude > 0.000001f)
                    return incoming;
            }
            for (var next = upper + 1; next < path.Count; next++)
            {
                var outgoing = path[next] - path[upper];
                if (outgoing.sqrMagnitude > 0.000001f)
                    return outgoing;
            }
            return Vector3.right;
        }

        upper = ~upper;
        var lower = Mathf.Clamp(upper - 1, 0, path.Count - 2);
        upper = lower + 1;
        var tangent = path[upper] - path[lower];
        return tangent.sqrMagnitude > 0.000001f
            ? tangent
            : Vector3.right;
    }

    private void SetStarPose(float progress)
    {
        star.transform.position = transform.TransformPoint(EvaluatePath(progress));
        var tangent = EvaluateTangent(progress);
        var angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        star.transform.rotation = Quaternion.Euler(0f, 0f, angle + starRotationOffset);
    }

    private void SetBarAlpha(float alpha, float progress)
    {
        var nextHidden = barProgress.BinarySearch(progress + trailLeadProgress);
        nextHidden = nextHidden >= 0 ? nextHidden + 1 : ~nextHidden;
        var alphaChanged = lastBarAlpha != alpha;
        var first = alphaChanged ? 0 : Mathf.Min(hiddenBarCount, nextHidden);
        var end = alphaChanged ? bars.Count : Mathf.Max(hiddenBarCount, nextHidden);
        for (var i = first; i < end; i++)
        {
            var visible = i >= nextHidden ? alpha : 0f;
            bars[i].color = new Color(1f, 1f, 1f, visible);
        }
        hiddenBarCount = nextHidden;
        lastBarAlpha = alpha;
    }

    private void SetVisualAlpha(float alpha, float progress)
    {
        SetBarAlpha(alpha, progress);
        if (starRenderer != null)
            starRenderer.color = new Color(1f, 1f, 1f, alpha);
    }

    private static Vector3 AreaPosition(char area, int index, bool dZone)
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

    private static SensorType GetSensor(char area, int index, bool dZone) =>
        area == 'K'
            ? dZone ? SensorType.D1 + index - 1 : (SensorType)(index - 1)
            : TouchBase.GetSensor(area, index);

    public float GetAppearanceStartOffset()
    {
        var fadeLeadScale = 1f - Mathf.Clamp(starSpeed, -1f, 1f);
        var motionSpeed = Mathf.Abs(speed);
        return motionSpeed <= 0.0001f
            ? 0f
            : (-3.926913f / motionSpeed) * fadeLeadScale;
    }

    private static float ResolveAngleDelta(
        float start,
        float end,
        char routeShape,
        int startPosition)
    {
        return (float)TouchSlideDirection.Sweep(start, end, startPosition, routeShape);
    }

    private void CreateJudgeVisual()
    {
        if (judgeTemplate == null || JudgmentDisabled || path.Count < 2)
            return;
        judgeVisual = Instantiate(judgeTemplate, transform.parent);
        judgeVisual.SetActive(false);
        var loader = judgeVisual.GetComponent<LoadJustSprite>();
        loader._0curv1str2wifi = endsWithCircle ? 0 : 1;
        loader.judgeOffset = 0;
        var end = path[^1];
        var tangent = EvaluateTangent(1f).normalized;
        var isLeft = endsWithCircle ? endCounterClockwise : end.x < 0f;
        if (isLeft) loader.setL();
        else loader.setR();

        // MajdataPlay's end pose uses the final segment, not the head's rotation.
        float angle;
        Vector3 position;
        if (endsWithCircle)
        {
            angle = (endCounterClockwise ? 360f : 405f) - 45f * endPosition;
            var radialAngle = (endCounterClockwise ? 2f : 3f) - endPosition;
            radialAngle *= Mathf.PI / 4f;
            position = new Vector3(Mathf.Cos(radialAngle), Mathf.Sin(radialAngle)) * 4.62f;
        }
        else
        {
            position = end - tangent * 2.05f;
            angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + (isLeft ? 180f : 0f);
        }
        judgeVisual.transform.position = transform.TransformPoint(position);
        judgeVisual.transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, angle);
    }

    private void ShowJudgeVisual()
    {
        if (judgeVisual == null)
            return;
        if (bodyMine && !NoteEffectManager.ShowMineHitFeedback)
        {
            Destroy(judgeVisual);
            return;
        }
        var loader = judgeVisual.GetComponent<LoadJustSprite>();
        switch (judgeResult)
        {
            case JudgeType.Miss: loader.setMiss(); break;
            case JudgeType.FastGood: loader.setFastGd(); break;
            case JudgeType.LateGood: loader.setLateGd(); break;
            case JudgeType.FastGreat:
            case JudgeType.FastGreat1:
            case JudgeType.FastGreat2: loader.setFastGr(); break;
            case JudgeType.LateGreat:
            case JudgeType.LateGreat1:
            case JudgeType.LateGreat2: loader.setLateGr(); break;
        }
        if (bodyBreak && judgeResult == JudgeType.Perfect && judgeBreakShine != null)
            judgeVisual.GetComponent<Animator>().runtimeAnimatorController = judgeBreakShine;
        judgeVisual.SetActive(true);
    }

    private void OnDestroy()
    {
        if (inputManager != null)
        {
            foreach (var sensorType in boundSensors)
                inputManager.UnbindSensor(OnSensorChanged, sensorType);
        }
        if (star != null)
            Destroy(star);
        if (!completed || finalized || JudgmentDisabled || HttpHandler.IsReloding ||
            objectCounter == null || !gameObject.scene.isLoaded)
        {
            if (judgeVisual != null)
                Destroy(judgeVisual);
            return;
        }
        finalized = true;
        objectCounter.ReportResult(this, judgeResult, bodyBreak);
        ShowJudgeVisual();
    }
}

internal static class SelectableOrbitPathGeometry
{
    public static void Append(
        JsonDataLoader routeSource,
        List<Vector3> target,
        char startArea,
        int startPosition,
        bool startIsDZone,
        char orbitArea,
        int orbitPosition,
        bool orbitIsDZone,
        bool orbitIsNumber,
        char endArea,
        int endPosition,
        bool endIsDZone,
        string shape)
    {
        var start = Position(startArea, startPosition, startIsDZone);
        var end = Position(endArea, endPosition, endIsDZone);
        var lowerShape = shape == "P" ? "p" : "q";

        if (orbitIsNumber)
        {
            var orbitIndex = orbitArea == 'C'
                ? 0
                : orbitArea == 'O'
                    ? 9
                    : orbitPosition;
            if (!TryAppendLegacyNumericOrbit(
                    routeSource, target, start, end, orbitIndex, shape) &&
                !SlideCodePathGeometry.AppendSingleOrbit(
                    target, start, end, orbitIndex, shape == "P"))
                AppendLine(target, start, end);
            return;
        }

        if (orbitArea == 'C' || orbitPosition == 0)
        {
            if (!AppendTemplate(
                    routeSource, target,
                    startPosition, endPosition, lowerShape,
                    start, end, centerAtOrigin: true, null))
                AppendFallbackCircle(
                    target, start, end, lowerShape == "p");
            return;
        }

        var sourceShape = shape == "P" ? "pp" : "qq";
        var sourceStart = shape == "P"
            ? (orbitPosition + 5) % 8 + 1
            : orbitPosition % 8 + 1;
        var sourceEnd = (sourceStart + 3) % 8 + 1;
        var explicitCenter = orbitIsDZone
            ? Position(orbitArea, orbitPosition, true)
            : (Vector3?)null;
        if (!AppendTemplate(
                routeSource, target,
                sourceStart, sourceEnd, sourceShape,
                start, end, centerAtOrigin: false, explicitCenter))
            AppendLine(target, start, end);
    }

    private static bool TryAppendLegacyNumericOrbit(
        JsonDataLoader routeSource,
        List<Vector3> target,
        Vector3 start,
        Vector3 end,
        int orbitIndex,
        string shape)
    {
        if (orbitIndex is < 1 or > 8 || shape is not ("P" or "Q"))
            return false;

        var sourceShape = shape == "P" ? "pp" : "qq";
        var sourceStart = shape == "P"
            ? (orbitIndex + 5) % 8 + 1
            : orbitIndex % 8 + 1;
        var sourceEnd = (sourceStart + 3) % 8 + 1;
        return AppendTemplate(
            routeSource,
            target,
            sourceStart,
            sourceEnd,
            sourceShape,
            start,
            end,
            centerAtOrigin: false,
            explicitCenter: null);
    }

    private static bool AppendTemplate(
        JsonDataLoader routeSource,
        List<Vector3> target,
        int sourceStart,
        int sourceEnd,
        string sourceShape,
        Vector3 actualStart,
        Vector3 actualEnd,
        bool centerAtOrigin,
        Vector3? explicitCenter)
    {
        if (routeSource == null ||
            !routeSource.TryGetSlideVisualRoute(
                $"{sourceStart}{sourceShape}{sourceEnd}[4:1]",
                out var sourcePath) ||
            sourcePath.Count < 2)
            return false;

        var originalRoute = new Vector3[sourcePath.Count + 2];
        originalRoute[0] = Position('K', sourceStart, false);
        for (var i = 0; i < sourcePath.Count; i++)
            originalRoute[i + 1] = sourcePath[i];
        originalRoute[^1] = Position('K', sourceEnd, false);

        var route = explicitCenter.HasValue
            ? SlideDrop.BuildAdaptiveTangentCircleRouteAround(
                originalRoute, actualStart, actualEnd, explicitCenter.Value)
            : SlideDrop.BuildAdaptiveTangentCircleRoute(
                originalRoute, actualStart, actualEnd, centerAtOrigin);
        var first = target.Count == 0 ? 0 : 1;
        for (var i = first; i < route.Length; i++)
            target.Add(route[i]);
        return true;
    }

    private static void AppendFallbackCircle(
        List<Vector3> target,
        Vector3 start,
        Vector3 end,
        bool clockwise)
    {
        const float radius = 2.3f;
        const int samples = 256;
        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var startTangent = TangentAngle(
            start.magnitude, startAngle, clockwise, true);
        var endTangent = TangentAngle(
            end.magnitude, endAngle, clockwise, false);
        var tangentStart = new Vector3(
            Mathf.Cos(startTangent), Mathf.Sin(startTangent)) * radius;
        var tangentEnd = new Vector3(
            Mathf.Cos(endTangent), Mathf.Sin(endTangent)) * radius;
        AppendLine(target, start, tangentStart);
        var sweep = clockwise
            ? -Mathf.Repeat(startTangent - endTangent, Mathf.PI * 2f)
            : Mathf.Repeat(endTangent - startTangent, Mathf.PI * 2f);
        for (var i = 1; i <= samples; i++)
        {
            var angle = startTangent + sweep * (i / (float)samples);
            Add(target, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        AppendLine(target, tangentEnd, end);
    }

    private static void AppendLine(
        List<Vector3> target,
        Vector3 start,
        Vector3 end)
    {
        var samples = Math.Max(
            1, Mathf.CeilToInt(Vector3.Distance(start, end) / 0.08f));
        var first = target.Count == 0 ? 0 : 1;
        for (var i = first; i <= samples; i++)
            Add(target, Vector3.Lerp(start, end, i / (float)samples));
    }

    private static void Add(List<Vector3> target, Vector3 point)
    {
        if (target.Count == 0 ||
            (target[^1] - point).sqrMagnitude > 0.000001f)
            target.Add(point);
    }

    private static float TangentAngle(
        float radius,
        float pointAngle,
        bool clockwise,
        bool entering)
    {
        if (radius <= 2.3001f)
            return pointAngle;
        var offset = Mathf.Acos(2.3f / radius);
        return pointAngle + (clockwise == entering ? -offset : offset);
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
