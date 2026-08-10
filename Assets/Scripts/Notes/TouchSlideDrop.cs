using Assets.Scripts;
using Assets.Scripts.Types;
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
    public bool bodyBreak;
    public float timeStart;
    public float duration = 1f;
    public float starSpeed;
    public int sortingOrder;
    public Sprite pathSprite;
    public GameObject star;
    public GameObject barTemplate;
    public Material pathMaterial;
    public Material starMaterial;
    public Vector2 barScale = Vector2.one;
    public Vector2 starScale = Vector2.one;

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

    private void Start()
    {
        timeProvider = GameObject.Find("AudioTimeProvider")?.GetComponent<AudioTimeProvider>();
        inputManager = GameObject.Find("Input")?.GetComponent<InputManager>();
        objectCounter = GameObject.Find("ObjectCounter")?.GetComponent<ObjectCounter>();

        BuildPath();
        BuildSensorRoute();
        CreateVisuals();

        if (previewOnly || inputManager == null)
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
        if (now < timeStart)
        {
            SetBarAlpha(GetBodyAlpha(now), 0f);
            if (starRenderer != null)
                starRenderer.color = Color.clear;
            star.SetActive(false);
            return;
        }

        star.SetActive(true);
        var waitDuration = Mathf.Max(0.0001f, time - timeStart);
        if (now < time)
        {
            var appearance = Mathf.Clamp01((now - timeStart) / waitDuration);
            SetStarPose(0f);
            starRenderer.color = new Color(1f, 1f, 1f, appearance);
            star.transform.localScale = new Vector3(
                (appearance + 0.5f) * starScale.x,
                (appearance + 0.5f) * starScale.y,
                1f);
            SetBarAlpha(GetBodyAlpha(now), 0f);
            return;
        }

        if (headTriggered && routeIndex == 0 && sensorRoute.Count > 0)
            routeIndex = 1;

        var progress = SvController.GetTypedOnlyProgress(
            time, Mathf.Max(0.01f, duration), now, "slide");
        starRenderer.color = Color.white;
        star.transform.localScale = new Vector3(
            1.5f * starScale.x,
            1.5f * starScale.y,
            1f);
        SetStarPose(progress);
        SetBarAlpha(1f, progress);

        if (!previewOnly && InputManager.Mode != AutoPlayMode.Disable)
            routeIndex = Mathf.Max(
                routeIndex,
                Mathf.CeilToInt(progress * sensorRoute.Count));

        if (progress < 1f)
            return;

        judgeResult = InputManager.Mode == AutoPlayMode.Random
            ? (JudgeType)UnityEngine.Random.Range(1, 14)
            : InputManager.Mode != AutoPlayMode.Disable || routeIndex >= sensorRoute.Count
                ? JudgeType.Perfect
                : JudgeType.Miss;
        Destroy(gameObject);
    }

    private float GetBodyAlpha(float now)
    {
        var fadeLeadScale = 1f - Mathf.Clamp(starSpeed, -1f, 1f);
        var fadeStart = timeStart +
                        (-3.926913f / Mathf.Max(0.01f, speed)) * fadeLeadScale;
        var fadeEnd = Mathf.Min(fadeStart + 0.2f, timeStart);
        return fadeEnd <= fadeStart
            ? now >= timeStart ? 1f : 0f
            : Mathf.InverseLerp(fadeStart, fadeEnd, now);
    }

    private void OnSensorChanged(object sender, InputEventArgs args)
    {
        if (previewOnly || args.Status != SensorStatus.On ||
            InputManager.Mode != AutoPlayMode.Disable || sensorRoute.Count == 0)
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
                shape);

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
        if (string.IsNullOrWhiteSpace(pathExpression))
            return false;

        var durationIndex = pathExpression.IndexOf('[');
        var source = durationIndex >= 0
            ? pathExpression.Substring(0, durationIndex)
            : pathExpression;
        var match = Regex.Match(
            source,
            @"^(?<start>(?:[1-8]d?|[ABDE][1-8]|C1?))[bxfm!?]*(?<segments>(?:[-<>^](?:[1-8]d?|[ABDE][1-8]|C1?)[bxfm]*)+)$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var current = ParseAreaPosition(match.Groups["start"].Value);
        var segments = Regex.Matches(
            match.Groups["segments"].Value,
            @"(?<shape>[-<>^])(?<end>(?:[1-8]d?|[ABDE][1-8]|C1?))[bxfm]*",
            RegexOptions.CultureInvariant);
        foreach (Match segment in segments)
        {
            var next = ParseAreaPosition(segment.Groups["end"].Value);
            AppendSegment(
                current.Area,
                current.Position,
                current.IsDZone,
                next.Area,
                next.Position,
                next.IsDZone,
                segment.Groups["shape"].Value[0]);
            current = next;
        }
        return path.Count >= 2;
    }

    private static (char Area, int Position, bool IsDZone) ParseAreaPosition(string value)
    {
        return char.IsDigit(value[0])
            ? ('K', value[0] - '0', value.EndsWith("d", StringComparison.Ordinal))
            : value[0] == 'C'
            ? ('C', 8, false)
            : (value[0], value[1] - '0', false);
    }

    private void AppendSegment(
        char segmentStartArea,
        int segmentStartPosition,
        bool segmentStartIsDZone,
        char segmentEndArea,
        int segmentEndPosition,
        bool segmentEndIsDZone,
        char segmentShape)
    {
        const int segments = 256;
        var start = AreaPosition(segmentStartArea, segmentStartPosition, segmentStartIsDZone);
        var end = AreaPosition(segmentEndArea, segmentEndPosition, segmentEndIsDZone);
        var firstSample = path.Count == 0 ? 0 : 1;
        if (segmentShape == '-')
        {
            for (var i = firstSample; i <= segments; i++)
                path.Add(Vector3.Lerp(start, end, i / (float)segments));
            return;
        }

        if (segmentStartArea == 'C' || segmentEndArea == 'C')
        {
            var direction = segmentShape == '<' ? -1f : 1f;
            var delta = end - start;
            var control = (start + end) * 0.5f +
                          new Vector3(-delta.y, delta.x) * (0.35f * direction);
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
        var deltaAngle = ResolveAngleDelta(startAngle, endAngle, segmentShape);
        var startRadius = start.magnitude;
        var endRadius = end.magnitude;
        for (var i = firstSample; i <= segments; i++)
        {
            var t = i / (float)segments;
            var angle = startAngle + deltaAngle * t;
            var radiusProgress = Mathf.SmoothStep(0f, 1f, t);
            var radius = Mathf.Lerp(startRadius, endRadius, radiusProgress);
            path.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }

    private void BuildSensorRoute()
    {
        var sensorsRoot = GameObject.Find("Sensors");
        if (sensorsRoot != null)
        {
            foreach (var localPosition in path)
            {
                var worldPosition = transform.TransformPoint(localPosition);
                Sensor nearest = null;
                var nearestDistance = float.MaxValue;
                for (var i = 0; i < sensorsRoot.transform.childCount; i++)
                {
                    var child = sensorsRoot.transform.GetChild(i);
                    var candidate = child.GetComponent<Sensor>();
                    var rect = child.GetComponent<RectTransform>();
                    if (candidate == null || rect == null)
                        continue;

                    var radius = Mathf.Max(
                        rect.rect.width * rect.lossyScale.x,
                        rect.rect.height * rect.lossyScale.y) * 0.5f + 0.15f;
                    var distance = ((Vector2)worldPosition - (Vector2)rect.position).sqrMagnitude;
                    if (distance <= radius * radius && distance < nearestDistance)
                    {
                        nearest = candidate;
                        nearestDistance = distance;
                    }
                }

                if (nearest != null &&
                    (sensorRoute.Count == 0 || sensorRoute[^1] != nearest.Type))
                    sensorRoute.Add(nearest.Type);
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
        const float sample = 0.01f;
        return EvaluatePath(Mathf.Min(1f, progress + sample)) -
               EvaluatePath(Mathf.Max(0f, progress - sample));
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
        for (var i = 0; i < bars.Count; i++)
        {
            var visible = barProgress[i] > progress + 0.015f ? alpha : 0f;
            bars[i].color = new Color(1f, 1f, 1f, visible);
        }
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
        return (-3.926913f / Mathf.Max(0.01f, speed)) * fadeLeadScale;
    }

    private static float ResolveAngleDelta(float start, float end, char routeShape)
    {
        var clockwise = -Mathf.Repeat(start - end, Mathf.PI * 2f);
        var counterclockwise = Mathf.Repeat(end - start, Mathf.PI * 2f);
        return routeShape switch
        {
            '>' => clockwise,
            '<' => counterclockwise,
            _ => Mathf.Abs(clockwise) <= Mathf.Abs(counterclockwise)
                ? clockwise
                : counterclockwise
        };
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
        if (finalized || previewOnly || HttpHandler.IsReloding ||
            objectCounter == null || !gameObject.scene.isLoaded)
            return;
        finalized = true;
        objectCounter.ReportResult(this, judgeResult, bodyBreak);
    }
}
