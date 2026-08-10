using Assets.Scripts.Interfaces;
using Assets.Scripts.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#nullable enable
public class SlideDrop : NoteLongDrop, IFlasher
{
    // Start is called before the first frame update
    public GameObject star_slide;

    public Sprite spriteNormal;
    public Sprite spriteEach;
    public Sprite spriteBreak;
    public RuntimeAnimatorController slideShine;
    public RuntimeAnimatorController judgeBreakShine;
    public GameObject parent;

    public bool isMirror;
    public bool isDZoneEnd;
    public bool isReverse;
    public int rotationPosition = -1;
    public bool isJustR;
    public bool isSpecialFlip; // fixes known star problem
    public bool isBreak;

    public float timeStart;

    public int sortIndex;

    public float fadeInTime;

    public float fullFadeInTime;
    public float starSpeed;

    public float slideConst;
    float arriveTime = -1;

    public List<int> areaStep = new List<int>();
    public bool smoothSlideAnime = false;

    public Material breakMaterial;
    // ALPHA: set by JsonDataLoader to override fill color
    public Material colorOverrideMaterial;
    public string slideType;
    public float noteScaleX = 1f;
    public float noteScaleY = 1f;

    List<SensorType> boundSensors = new();
    List<Sensor> judgeSensors = new();
    List<Sensor> triggerSensors = new(); // AutoPlay; sensors already triggered
    List<JudgeArea> judgeQueue = new(); // Judgement queue
    List<JudgeArea> _judgeQueue = new(); // Judgement queue

    public ConnSlideInfo ConnectInfo { get; set; } = new();
    public bool isFinished { get => judgeQueue.Count == 0; }
    public bool isPendingFinish { get => judgeQueue.Count == 1; }

    public bool UsesSensor(SensorType target)
    {
        var route = _judgeQueue.Count > 0 ? _judgeQueue : judgeQueue;
        return route.Any(area => area.GetSensorTypes().Contains(target));
    }

    public float GetAppearanceStartOffset()
    {
        var fadeLeadScale = 1f - Mathf.Clamp(starSpeed, -1f, 1f);
        return (-3.926913f / speed) * fadeLeadScale;
    }
    

    Animator fadeInAnimator;


    private readonly List<GameObject> slideBars = new();
    private readonly List<SpriteRenderer> slideBarRenderers = new();
    private readonly List<Vector3> routeBarPositions = new();
    private int logicalBarCount;

    private readonly List<Vector3> slidePositions = new();
    private readonly List<Quaternion> slideRotations = new();
    private GameObject slideOK;

    private SpriteRenderer spriteRenderer_star;
    private float currentSlideBarAlpha = -1f;

    public int endPosition;

    List<GameObject> sensors = new();
    SensorManager sManager;
    
    List<Sensor> registerSensors = new();

    bool canShine = false;
    bool canCheck = false;
    bool isJudgeInputBound = false;
    bool isChecking = false;
    float judgeTiming; // Correct judgement frame
    bool isInitialized = false; // Prevent duplicate initialization
    bool isDestroying = false; // Prevent duplicate destruction

    /// <summary>
    /// Initializes the Slide
    /// </summary>
    public void Initialize()
    {
        if (isInitialized)
            return;
        isInitialized = true;
        slideOK = transform.GetChild(transform.childCount - 1).gameObject; //slideok is the last one        
        objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();

        int rotPos = rotationPosition > 0 ? rotationPosition : startPosition;
        if (isMirror)
        {
            transform.localScale = new Vector3(-1f, 1f, 1f);
            transform.rotation = Quaternion.Euler(0f, 0f, -45f * rotPos);
            slideOK.transform.localScale = new Vector3(-1f, 1f, 1f);
        }
        else
        {
            transform.rotation = Quaternion.Euler(0f, 0f, -45f * (rotPos - 1));
        }

        if (isJustR)
        {
            if (slideOK.GetComponent<LoadJustSprite>().setR() == 1 && isMirror)
            {
                slideOK.transform.Rotate(new Vector3(0f, 0f, 180f));
                var angel = slideOK.transform.rotation.eulerAngles.z * Mathf.Deg2Rad;
                slideOK.transform.position += new Vector3(Mathf.Sin(angel) * 0.27f, Mathf.Cos(angel) * -0.27f);
            }
        }
        else
        {
            if (slideOK.GetComponent<LoadJustSprite>().setL() == 1 && !isMirror)
            {
                slideOK.transform.Rotate(new Vector3(0f, 0f, 180f));
                var angel = slideOK.transform.rotation.eulerAngles.z * Mathf.Deg2Rad;
                slideOK.transform.position += new Vector3(Mathf.Sin(angel) * 0.27f, Mathf.Cos(angel) * -0.27f);
            }
        }
        AdjustReverseArcJudgeEffectRadius();
        spriteRenderer_star = star_slide.GetComponent<SpriteRenderer>();

        if (isBreak)
        {
            spriteRenderer_star.sharedMaterial = breakMaterial;
            var controller = star_slide.AddComponent<BreakShineController>();
            controller.enabled = true;
            controller.parent = this;
        }

        for (var i = 0; i < transform.childCount - 1; i++)
            slideBars.Add(transform.GetChild(i).gameObject);

        slideOK.SetActive(false);
        slideOK.transform.SetParent(transform.parent);
        BuildSlidePath();
        foreach (var gm in slideBars)
        {
            gm.transform.localScale = Vector3.Scale(
                gm.transform.localScale, new Vector3(noteScaleX, noteScaleY, 1f));
            var sr = gm.GetComponent<SpriteRenderer>();
            slideBarRenderers.Add(sr);
            sr.color = new Color(1f, 1f, 1f, 0f);
            sr.sortingOrder = sortIndex--;
            sr.sortingLayerName = "Slide";
            if (isBreak)
            {
                sr.sprite = spriteBreak;
                sr.sharedMaterial = breakMaterial;
                var controller = gm.AddComponent<BreakShineController>();
                controller.parent = this;
                controller.enabled = true;
            }
            else if (isEach)
            {
                sr.sprite = spriteEach;
            }
            else
            {
                sr.sprite = spriteNormal;
            }
            if (isReverse)
                sr.flipX = true;
        }

        // ALPHA: apply color override to star and all slide bars
        if (colorOverrideMaterial != null)
        {
            spriteRenderer_star.sharedMaterial = colorOverrideMaterial;
            foreach (var renderer in slideBarRenderers)
                renderer.sharedMaterial = colorOverrideMaterial;
        }

        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        // Calculate when the Slide begins fading in
        // At speed 8.0, show the Slide 300ms early
        fadeInTime = GetAppearanceStartOffset();
        // Time when the Slide is fully visible
        // Normally negative; very high speeds skip the fade
        fullFadeInTime = Math.Min(fadeInTime + 0.2f, 0);
        var interval = fullFadeInTime - fadeInTime;
        fadeInAnimator = this.GetComponent<Animator>();
        if (interval > 0.0001f)
        {
            fadeInAnimator.speed = 0.2f / interval;
            fadeInAnimator.SetTrigger("slide");
        }
        else
        {
            fadeInAnimator.enabled = false;
            setSlideBarAlpha(0f);
        }

        var sManagerObj = GameObject.Find("Sensors");
        var count = sManagerObj.transform.childCount;
        for (int i = 0; i < count; i++)
            sensors.Add(sManagerObj.transform.GetChild(i).gameObject);
        sManager = sManagerObj.GetComponent<SensorManager>();

        GetSensors(sensors.Select(x => x.GetComponent<RectTransform>())
                                        .ToArray());

        if (isReverse)
        {
            judgeSensors.Reverse();
            slidePositions.Reverse();
            slideRotations.Reverse();
            slideBars.Reverse();
            var targetPos = slidePositions.Last();
            float slideOKRadius = slideOK.transform.position.magnitude;
            slideOK.transform.position = targetPos.normalized * slideOKRadius;
            float extraRot = endPosition <= 4 ? 90f : -90f;
            slideOK.transform.rotation = Quaternion.Euler(0f, 0f,
                -(2 * endPosition - 1) * 22.5f + extraRot + (isDZoneEnd ? 22.5f : 0f));
        }

        var _slideIndex = areaStep.Skip(1).ToArray();
        if (_slideIndex.Length != judgeSensors.Count)
            _slideIndex = null;
        for (int i = 0; i < judgeSensors.Count; i++)
        {
            var sensor = judgeSensors[i];
            int index = 0;
            if (_slideIndex is null)
                index = (logicalBarCount / judgeSensors.Count) * (i + 1);
            else
                index = _slideIndex[i];
            judgeQueue.Add(new JudgeArea(
                new Dictionary<SensorType, bool>
                {
                    {sensor.Type, i == judgeSensors.Count - 1 }
                }, index));
        }
        // A_k uses B_k (enum value +8) as its partner area. D-zone Slides use E-zone
        // intermediate sensors, which have no equivalent +8 partner, so accept a narrower area.
        void AddPartnerArea(int queueIndex)
        {
            if (judgeSensors[queueIndex].Group != SensorGroup.A)
                return;
            judgeQueue[queueIndex].AddArea(judgeSensors[queueIndex].Type + 8);
            registerSensors.Add(sManager.GetSensor(judgeSensors[queueIndex].Type + 8));
        }
        if (slideType is "line3" or "line7")// 1-3
        {
            judgeQueue[1].CanSkip = ConnectInfo.IsConnSlide;
            AddPartnerArea(1);
        }
        else if (slideType == "circle3")// 1^3
            judgeQueue[1].CanSkip = ConnectInfo.IsConnSlide;
        else if (slideType[0] == 'L')// 1V3
        {
            judgeQueue[1].CanSkip = ConnectInfo.IsConnSlide;
            AddPartnerArea(1);
            if (slideType == "L5")// 1V35
            {
                judgeQueue[3].CanSkip = ConnectInfo.IsConnSlide;
                AddPartnerArea(3);
            }
        }
        if (ConnectInfo.IsConnSlide && ConnectInfo.IsGroupPartEnd)
            judgeQueue.LastOrDefault().SetIsLast();
        else if (ConnectInfo.IsConnSlide)
            judgeQueue.LastOrDefault().SetNonLast();
        registerSensors.AddRange(judgeSensors);
        _judgeQueue = new(judgeQueue);

        parent = ConnectInfo.Parent;
        if( (ConnectInfo.IsConnSlide && ConnectInfo.IsGroupPartEnd) || 
            !ConnectInfo.IsConnSlide)
        {
            judgeTiming = time + LastFor * CalJudgeTiming();
        }

    }

    private void BuildSlidePath()
    {
        var physicalStart = isReverse ? endPosition : startPosition;
        var physicalEnd = isReverse ? startPosition : endPosition;
        var startIsDZone = isReverse ? isDZoneEnd : isDZone;
        var endIsDZone = isReverse ? isDZone : isDZoneEnd;

        if (!startIsDZone && !endIsDZone)
        {
            slidePositions.Add(getPositionFromDistance(4.8f, physicalStart));
            routeBarPositions.AddRange(slideBars.Select(bar => bar.transform.position));
            foreach (var bar in slideBars)
            {
                slidePositions.Add(bar.transform.position);
                slideRotations.Add(Quaternion.Euler(
                    bar.transform.rotation.eulerAngles + new Vector3(0f, 0f, 18f)));
            }

            logicalBarCount = routeBarPositions.Count;
            AppendRouteEnd(getPositionFromDistance(4.8f, physicalEnd));
            return;
        }

        BuildDZoneSlidePath(physicalStart, physicalEnd, startIsDZone, endIsDZone);
    }

    private void BuildDZoneSlidePath(
        int physicalStart,
        int physicalEnd,
        bool startIsDZone,
        bool endIsDZone)
    {
        var originalBars = slideBars.ToArray();
        var originalRotations = originalBars.Select(bar => bar.transform.rotation).ToArray();
        var originalScales = originalBars.Select(bar => bar.transform.localScale).ToArray();
        var startOffset = startIsDZone ? -0.5f : 0f;
        var endOffset = endIsDZone ? -0.5f : 0f;
        var start = getPositionFromDistance(4.8f, physicalStart + startOffset);
        var end = getPositionFromDistance(4.8f, physicalEnd + endOffset);

        var prefabStart = getPositionFromDistance(4.8f, physicalStart);
        var prefabEnd = getPositionFromDistance(4.8f, physicalEnd);
        var originalRoute = new Vector3[originalBars.Length + 2];
        originalRoute[0] = prefabStart;
        for (var i = 0; i < originalBars.Length; i++)
            originalRoute[i + 1] = originalBars[i].transform.position;
        originalRoute[^1] = prefabEnd;

        var originalDistances = BuildCumulativeDistances(originalRoute);
        var originalLength = originalDistances[^1];
        var startCorrection = start - prefabStart;
        var endCorrection = end - prefabEnd;
        var pathKind = GetDZonePathKind();
        var deformedRoute = pathKind switch
        {
            DZonePathKind.Line => BuildStraightRoute(originalRoute.Length, start, end),
            DZonePathKind.OuterCircle => BuildOuterCircleRoute(originalRoute, start, end),
            DZonePathKind.AnchoredMiddle => BuildAnchoredMiddleRoute(originalRoute, start, end),
            DZonePathKind.InnerCircle => BuildTangentCircleRoute(
                originalRoute, start, end, slideType.StartsWith("pq", StringComparison.Ordinal)),
            _ => new Vector3[originalRoute.Length]
        };
        if (pathKind == DZonePathKind.DeformedPrefab)
        {
            for (var i = 0; i < originalRoute.Length; i++)
            {
                var progress = originalLength > 0.0001f
                    ? originalDistances[i] / originalLength
                    : 0f;
                var blend = progress * progress * (3f - 2f * progress);
                deformedRoute[i] = originalRoute[i] +
                                   Vector3.Lerp(startCorrection, endCorrection, blend);
            }
        }
        deformedRoute[0] = start;
        deformedRoute[^1] = end;

        var deformedDistances = BuildCumulativeDistances(deformedRoute);
        var deformedLength = deformedDistances[^1];
        AlignDZoneJudgeEffect(
            originalRoute,
            originalDistances,
            originalLength,
            deformedRoute,
            deformedDistances,
            deformedLength,
            prefabEnd,
            end);
        logicalBarCount = originalBars.Length;
        slidePositions.Add(start);
        for (var i = 0; i < logicalBarCount; i++)
        {
            var progress = (i + 1f) / (logicalBarCount + 1f);
            var position = SamplePolyline(deformedRoute, deformedDistances, progress * deformedLength);
            routeBarPositions.Add(position);
            slidePositions.Add(position);

            var oldTangent = SamplePolylineTangent(
                originalRoute, originalDistances, progress * originalLength);
            var newTangent = SamplePolylineTangent(
                deformedRoute, deformedDistances, progress * deformedLength);
            var tangentDelta = Vector2.SignedAngle(oldTangent, newTangent);
            var rotation = Quaternion.AngleAxis(tangentDelta, Vector3.forward) * originalRotations[i];
            slideRotations.Add(Quaternion.Euler(
                rotation.eulerAngles + new Vector3(0f, 0f, 18f)));
        }
        AppendRouteEnd(end);

        var originalSpacing = originalLength > 0.0001f
            ? originalLength / (originalBars.Length + 1f)
            : 0f;
        var visualCount = originalSpacing > 0.0001f
            ? Mathf.Clamp(Mathf.RoundToInt(deformedLength / originalSpacing) - 1, 1, 96)
            : originalBars.Length;
        var visualBars = new List<GameObject>(visualCount);
        for (var i = 0; i < visualCount; i++)
        {
            var progress = (i + 1f) / (visualCount + 1f);
            var sourceIndex = Mathf.Clamp(
                Mathf.RoundToInt(progress * (originalBars.Length + 1f)) - 1,
                0,
                originalBars.Length - 1);
            var bar = i < originalBars.Length
                ? originalBars[i]
                : Instantiate(originalBars[sourceIndex], transform);
            bar.SetActive(true);
            bar.transform.position = SamplePolyline(
                deformedRoute, deformedDistances, progress * deformedLength);
            var oldTangent = SamplePolylineTangent(
                originalRoute, originalDistances, progress * originalLength);
            var newTangent = SamplePolylineTangent(
                deformedRoute, deformedDistances, progress * deformedLength);
            var tangentDelta = Vector2.SignedAngle(oldTangent, newTangent);
            bar.transform.rotation = Quaternion.AngleAxis(tangentDelta, Vector3.forward) *
                                     originalRotations[sourceIndex];
            bar.transform.localScale = originalScales[sourceIndex];
            visualBars.Add(bar);
        }

        for (var i = visualCount; i < originalBars.Length; i++)
            originalBars[i].SetActive(false);
        slideBars.Clear();
        slideBars.AddRange(visualBars);
    }

    private enum DZonePathKind
    {
        DeformedPrefab,
        Line,
        OuterCircle,
        AnchoredMiddle,
        InnerCircle
    }

    private DZonePathKind GetDZonePathKind()
    {
        if (string.IsNullOrEmpty(slideType))
            return DZonePathKind.DeformedPrefab;
        if (slideType.StartsWith("line", StringComparison.Ordinal))
            return DZonePathKind.Line;
        if (slideType.StartsWith("circle", StringComparison.Ordinal))
            return DZonePathKind.OuterCircle;
        if (slideType == "s")
            return DZonePathKind.AnchoredMiddle;
        if (slideType.IndexOf("pq", StringComparison.Ordinal) >= 0)
            return DZonePathKind.InnerCircle;
        return DZonePathKind.DeformedPrefab;
    }

    private static Vector3[] BuildStraightRoute(int pointCount, Vector3 start, Vector3 end)
    {
        var route = new Vector3[pointCount];
        for (var i = 0; i < route.Length; i++)
            route[i] = Vector3.Lerp(start, end, i / Math.Max(1f, route.Length - 1f));
        return route;
    }

    private static Vector3[] BuildAnchoredMiddleRoute(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end)
    {
        var route = originalRoute.ToArray();
        var firstAnchor = -1;
        var lastAnchor = -1;
        for (var i = 1; i < originalRoute.Count - 1; i++)
        {
            var incoming = originalRoute[i] - originalRoute[i - 1];
            var outgoing = originalRoute[i + 1] - originalRoute[i];
            if (incoming.sqrMagnitude <= 0.0001f || outgoing.sqrMagnitude <= 0.0001f)
                continue;
            if (Mathf.Abs(Vector2.SignedAngle(incoming, outgoing)) < 30f)
                continue;
            if (firstAnchor < 0)
                firstAnchor = i;
            lastAnchor = i;
        }

        if (firstAnchor < 1 || lastAnchor <= firstAnchor || lastAnchor >= route.Length - 1)
            return BuildStraightRoute(route.Length, start, end);

        for (var i = 0; i <= firstAnchor; i++)
            route[i] = Vector3.Lerp(start, originalRoute[firstAnchor], i / (float)firstAnchor);
        var tailLength = route.Length - 1 - lastAnchor;
        for (var i = lastAnchor; i < route.Length; i++)
            route[i] = Vector3.Lerp(
                originalRoute[lastAnchor], end, (i - lastAnchor) / (float)tailLength);
        route[0] = start;
        route[^1] = end;
        return route;
    }

    private static Vector3[] BuildOuterCircleRoute(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end)
    {
        var route = new Vector3[originalRoute.Count];
        var originalSweep = 0f;
        for (var i = 1; i < originalRoute.Count; i++)
        {
            var previousAngle = Mathf.Atan2(originalRoute[i - 1].y, originalRoute[i - 1].x);
            var currentAngle = Mathf.Atan2(originalRoute[i].y, originalRoute[i].x);
            originalSweep += Mathf.DeltaAngle(
                previousAngle * Mathf.Rad2Deg,
                currentAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        }

        var startAngle = Mathf.Atan2(start.y, start.x);
        var endAngle = Mathf.Atan2(end.y, end.x);
        var sweep = endAngle - startAngle;
        sweep += Mathf.Round((originalSweep - sweep) / (Mathf.PI * 2f)) * Mathf.PI * 2f;
        if (originalSweep > 0f && sweep <= 0f)
            sweep += Mathf.PI * 2f;
        else if (originalSweep < 0f && sweep >= 0f)
            sweep -= Mathf.PI * 2f;

        var radius = start.magnitude;
        for (var i = 0; i < route.Length; i++)
        {
            var progress = i / Math.Max(1f, route.Length - 1f);
            var angle = startAngle + sweep * progress;
            route[i] = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                Mathf.Lerp(start.z, end.z, progress));
        }
        route[0] = start;
        route[^1] = end;
        return route;
    }

    private static Vector3[] BuildTangentCircleRoute(
        IReadOnlyList<Vector3> originalRoute,
        Vector3 start,
        Vector3 end,
        bool centerAtOrigin)
    {
        var route = new Vector3[originalRoute.Count];
        if (!TryFindCircleSection(
                originalRoute,
                out var circleStart,
                out var circleEnd,
                out var center,
                out var radius,
                out var direction,
                centerAtOrigin) ||
            !TryGetTangentPoint(center, radius, start, originalRoute[circleStart], out var entry) ||
            !TryGetTangentPoint(center, radius, end, originalRoute[circleEnd], out var exit))
        {
            for (var i = 0; i < route.Length; i++)
                route[i] = Vector3.Lerp(start, end, i / (float)Math.Max(1, route.Length - 1));
            return route;
        }

        var startAngle = Mathf.Atan2(entry.y - center.y, entry.x - center.x);
        var endAngle = Mathf.Atan2(exit.y - center.y, exit.x - center.x);
        var sweep = GetDirectedSweep(startAngle, endAngle, direction);
        var entryLength = Vector3.Distance(start, entry);
        var arcLength = Mathf.Abs(sweep) * radius;
        var exitLength = Vector3.Distance(exit, end);
        var totalLength = entryLength + arcLength + exitLength;
        if (totalLength <= 0.0001f)
            return route;

        for (var i = 0; i < route.Length; i++)
        {
            var distance = totalLength * i / Math.Max(1f, route.Length - 1f);
            if (distance <= entryLength)
            {
                route[i] = Vector3.Lerp(
                    start,
                    entry,
                    entryLength > 0.0001f ? distance / entryLength : 1f);
            }
            else if (distance < entryLength + arcLength)
            {
                var arcProgress = arcLength > 0.0001f
                    ? (distance - entryLength) / arcLength
                    : 1f;
                var angle = startAngle + sweep * arcProgress;
                route[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius,
                    Mathf.Lerp(start.z, end.z, distance / totalLength));
            }
            else
            {
                route[i] = Vector3.Lerp(
                    exit,
                    end,
                    exitLength > 0.0001f
                        ? (distance - entryLength - arcLength) / exitLength
                        : 1f);
            }
        }

        route[0] = start;
        route[^1] = end;
        return route;
    }

    private static bool TryFindCircleSection(
        IReadOnlyList<Vector3> route,
        out int sectionStart,
        out int sectionEnd,
        out Vector3 center,
        out float radius,
        out float direction,
        bool centerAtOrigin)
    {
        sectionStart = 0;
        sectionEnd = 0;
        center = Vector3.zero;
        radius = 0f;
        direction = 1f;
        if (route.Count < 6)
            return false;

        var bestStart = -1;
        var bestEnd = -1;
        var bestDirection = 0f;
        var currentStart = -1;
        var currentDirection = 0f;
        for (var i = 1; i < route.Count - 1; i++)
        {
            var incoming = route[i] - route[i - 1];
            var outgoing = route[i + 1] - route[i];
            if (incoming.sqrMagnitude <= 0.0001f || outgoing.sqrMagnitude <= 0.0001f)
                continue;

            var turn = Vector2.SignedAngle(incoming, outgoing);
            var turnDirection = Mathf.Sign(turn);
            var isCircleTurn = Mathf.Abs(turn) >= 4f && Mathf.Abs(turn) <= 30f;
            if (isCircleTurn && (currentStart < 0 || turnDirection == currentDirection))
            {
                if (currentStart < 0)
                {
                    currentStart = i;
                    currentDirection = turnDirection;
                }
                continue;
            }

            if (currentStart >= 0 &&
                (bestStart < 0 || i - 1 - currentStart > bestEnd - bestStart))
            {
                bestStart = currentStart;
                bestEnd = i - 1;
                bestDirection = currentDirection;
            }
            currentStart = isCircleTurn ? i : -1;
            currentDirection = isCircleTurn ? turnDirection : 0f;
        }

        if (currentStart >= 0 &&
            (bestStart < 0 || route.Count - 2 - currentStart > bestEnd - bestStart))
        {
            bestStart = currentStart;
            bestEnd = route.Count - 2;
            bestDirection = currentDirection;
        }

        if (bestStart < 0 || bestEnd - bestStart < 2)
            return false;

        sectionStart = bestStart;
        sectionEnd = bestEnd;
        if (centerAtOrigin)
        {
            center = new Vector3(0f, 0f, route[sectionStart].z);
            radius = 0f;
            for (var i = sectionStart; i <= sectionEnd; i++)
                radius += Vector2.Distance(Vector2.zero, route[i]);
            radius /= sectionEnd - sectionStart + 1f;
        }
        else if (!TryFitCircle(route, sectionStart, sectionEnd, out center, out radius))
        {
            return false;
        }

        direction = bestDirection;
        return true;
    }

    private static bool TryFitCircle(
        IReadOnlyList<Vector3> points,
        int start,
        int end,
        out Vector3 center,
        out float radius)
    {
        center = Vector3.zero;
        radius = 0f;
        double xx = 0d, xy = 0d, x = 0d;
        double yy = 0d, y = 0d, count = 0d;
        double xb = 0d, yb = 0d, b = 0d;
        for (var i = start; i <= end; i++)
        {
            var px = (double)points[i].x;
            var py = (double)points[i].y;
            var value = px * px + py * py;
            xx += 4d * px * px;
            xy += 4d * px * py;
            x += 2d * px;
            yy += 4d * py * py;
            y += 2d * py;
            count += 1d;
            xb += 2d * px * value;
            yb += 2d * py * value;
            b += value;
        }

        var determinant = Determinant3(xx, xy, x, xy, yy, y, x, y, count);
        if (Math.Abs(determinant) <= 0.000001d)
            return false;

        var cx = Determinant3(xb, xy, x, yb, yy, y, b, y, count) / determinant;
        var cy = Determinant3(xx, xb, x, xy, yb, y, x, b, count) / determinant;
        var constant = Determinant3(xx, xy, xb, xy, yy, yb, x, y, b) / determinant;
        var radiusSquared = cx * cx + cy * cy + constant;
        if (radiusSquared <= 0.000001d)
            return false;

        center = new Vector3((float)cx, (float)cy, points[start].z);
        radius = Mathf.Sqrt((float)radiusSquared);
        return true;
    }

    private static double Determinant3(
        double a, double b, double c,
        double d, double e, double f,
        double g, double h, double i)
        => a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);

    private static bool TryGetTangentPoint(
        Vector3 center,
        float radius,
        Vector3 point,
        Vector3 preferred,
        out Vector3 tangent)
    {
        tangent = preferred;
        var delta = point - center;
        var distanceSquared = delta.x * delta.x + delta.y * delta.y;
        var radiusSquared = radius * radius;
        if (distanceSquared <= radiusSquared + 0.0001f)
            return false;

        var baseScale = radiusSquared / distanceSquared;
        var offsetScale = radius * Mathf.Sqrt(distanceSquared - radiusSquared) / distanceSquared;
        var basePoint = center + delta * baseScale;
        var perpendicular = new Vector3(-delta.y, delta.x, 0f);
        var first = basePoint + perpendicular * offsetScale;
        var second = basePoint - perpendicular * offsetScale;
        tangent = (first - preferred).sqrMagnitude <= (second - preferred).sqrMagnitude
            ? first
            : second;
        tangent.z = preferred.z;
        return true;
    }

    private static float GetDirectedSweep(float start, float end, float direction)
    {
        var sweep = end - start;
        if (direction >= 0f)
        {
            while (sweep < 0f)
                sweep += Mathf.PI * 2f;
        }
        else
        {
            while (sweep > 0f)
                sweep -= Mathf.PI * 2f;
        }
        return sweep;
    }

    private void AlignDZoneJudgeEffect(
        IReadOnlyList<Vector3> originalRoute,
        IReadOnlyList<float> originalDistances,
        float originalLength,
        IReadOnlyList<Vector3> deformedRoute,
        IReadOnlyList<float> deformedDistances,
        float deformedLength,
        Vector3 originalEnd,
        Vector3 deformedEnd)
    {
        if (isReverse)
            return;

        var originalTangent = SamplePolylineTangent(
            originalRoute, originalDistances, originalLength);
        var deformedTangent = SamplePolylineTangent(
            deformedRoute, deformedDistances, deformedLength);
        if (originalTangent.sqrMagnitude <= 0.0001f ||
            deformedTangent.sqrMagnitude <= 0.0001f)
            return;

        var rotationDelta = Quaternion.AngleAxis(
            Vector2.SignedAngle(originalTangent, deformedTangent),
            Vector3.forward);
        var offsetFromEndpoint = slideOK.transform.position - originalEnd;
        slideOK.transform.position = deformedEnd + rotationDelta * offsetFromEndpoint;
        slideOK.transform.rotation = rotationDelta * slideOK.transform.rotation;
    }

    private void AppendRouteEnd(Vector3 endPosition)
    {
        var previous = slidePositions.LastOrDefault();
        var previousRotation = slideRotations.LastOrDefault();
        var denominator = previous.magnitude * endPosition.magnitude;
        var cosine = denominator > 0.0001f
            ? Mathf.Clamp(Vector3.Dot(previous, endPosition) / denominator, -1f, 1f)
            : 1f;
        var angle = Mathf.Acos(cosine) * Mathf.Rad2Deg;
        if (slideRotations.Count >= 2)
        {
            var rotationDelta = Mathf.DeltaAngle(
                slideRotations[^2].eulerAngles.z,
                slideRotations[^1].eulerAngles.z);
            if (rotationDelta < 0f)
                angle = -angle;
        }

        slidePositions.Add(endPosition);
        slideRotations.Add(previousRotation * Quaternion.Euler(0f, 0f, angle));
    }

    private static float[] BuildCumulativeDistances(IReadOnlyList<Vector3> points)
    {
        var distances = new float[points.Count];
        for (var i = 1; i < points.Count; i++)
            distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        return distances;
    }

    private static Vector3 SamplePolyline(
        IReadOnlyList<Vector3> points,
        IReadOnlyList<float> distances,
        float targetDistance)
    {
        if (points.Count == 0)
            return Vector3.zero;
        if (points.Count == 1 || distances[^1] <= 0.0001f)
            return points[0];

        targetDistance = Mathf.Clamp(targetDistance, 0f, distances[^1]);
        var upper = 1;
        while (upper < distances.Count - 1 && distances[upper] < targetDistance)
            upper++;
        var lower = upper - 1;
        var segmentLength = distances[upper] - distances[lower];
        var amount = segmentLength > 0.0001f
            ? (targetDistance - distances[lower]) / segmentLength
            : 0f;
        return Vector3.Lerp(points[lower], points[upper], amount);
    }

    private static Vector3 SamplePolylineTangent(
        IReadOnlyList<Vector3> points,
        IReadOnlyList<float> distances,
        float targetDistance)
    {
        if (points.Count < 2)
            return Vector3.right;
        var sampleDistance = Mathf.Max(0.01f, distances[^1] * 0.005f);
        return SamplePolyline(points, distances, targetDistance + sampleDistance) -
               SamplePolyline(points, distances, targetDistance - sampleDistance);
    }

    private void AdjustReverseArcJudgeEffectRadius()
    {
        if (!isReverse || string.IsNullOrEmpty(slideType) || !slideType.StartsWith("ppqq"))
            return;

        int keyDistance = Math.Abs(startPosition - endPosition);
        keyDistance = Math.Min(keyDistance, 8 - keyDistance);
        keyDistance = Mathf.Clamp(keyDistance, 1, 4);

        // Adjacent rp/rq endpoints overshoot the ring the most. Pull close
        // endpoints further inward while leaving long arcs almost unchanged.
        float radiusScale = Mathf.Lerp(0.76f, 0.94f, (keyDistance - 1) / 3f);
        var position = slideOK.transform.position;
        slideOK.transform.position = new Vector3(
            position.x * radiusScale,
            position.y * radiusScale,
            position.z);
    }
    /// <summary>
    /// Connection Slide
    /// <para>Forces this Slide to finish</para>
    /// </summary>
    public void ForceFinish()
    {
        if (!ConnectInfo.IsConnSlide || ConnectInfo.IsGroupPartEnd)
            return;
        judgeQueue.Clear();
    }
    private void Start()
    {
        Initialize();
        if(ConnectInfo.IsConnSlide)
        {
            LastFor = (ConnectInfo.TotalLength / ConnectInfo.TotalSlideLen) * GetSlideLength();
            if(!ConnectInfo.IsGroupPartHead)
            {
                var parent = ConnectInfo.Parent!.GetComponent<SlideDrop>();
                time = parent.time + parent.LastFor;
                judgeTiming = time + LastFor * CalJudgeTiming();
            }
        }
        var allSensors = judgeQueue.SelectMany(x => x.GetSensorTypes())
                                   .GroupBy(x => x)
                                   .Select(x => x.Key);
        inputManager = GameObject.Find("Input").GetComponent<InputManager>();
        boundSensors.AddRange(allSensors);
    }
    private void BindJudgeInputWhenReady()
    {
        if (previewOnly || isJudgeInputBound || !canCheck)
            return;

        foreach (var sensor in boundSensors)
            inputManager.BindSensor(Check, sensor);
        isJudgeInputBound = true;
    }
    private bool IgnoreSensorGroup(SensorGroup group)
    {
        // Mixed A/D slides cross both sensor rings, so no ring is excluded.
        if (isDZone != isDZoneEnd)
            return false;

        return isDZone
            ? group == SensorGroup.A || group == SensorGroup.B
            : group == SensorGroup.E || group == SensorGroup.D;
    }

    void GetSensors(RectTransform[] sensors)
    {
        Sensor lastSensor = null;
        foreach (var pos in routeBarPositions)
        {
            foreach (var s in sensors)
            {
                var sensor = s.GetComponent<Sensor>();
                if (IgnoreSensorGroup(sensor.Group))
                    continue;

                var rCenter = s.position;
                var rWidth = s.rect.width * s.lossyScale.x;
                var rHeight = s.rect.height * s.lossyScale.y;

                var radius = Math.Max(rWidth, rHeight) / 2;

                if ((pos - rCenter).sqrMagnitude <= radius * radius)
                {
                    if(lastSensor is null || sensor != lastSensor)
                    {
                        judgeSensors.Add(sensor);
                        lastSensor = sensor;
                        break;
                    }
                }
            }
        }
        
    }
    private void FixedUpdate()
    {
        if (previewOnly)
            return;
        if (InputManager.Mode is AutoPlayMode.Enable or AutoPlayMode.Random)
            return;

        /// time      is when the Slide starts
        /// timeStart is when the Slide is fully visible but not yet moving
        /// LastFor   is the Slide duration
        var timing = timeProvider.AudioTime - time;
        var startTiming = timeProvider.AudioTime - timeStart;
        var forceJudgeTiming = time + LastFor + 0.6;

        if (ConnectInfo.IsGroupPart)
        {
            if (ConnectInfo.IsGroupPartHead && startTiming >= -0.05f)
                canCheck = true;
            else if (!ConnectInfo.IsGroupPartHead)
                canCheck = ConnectInfo.ParentFinished || ConnectInfo.ParentPendingFinish;
        }
        else if (startTiming >= -0.05f)
            canCheck = true;

        BindJudgeInputWhenReady();

        if (timing > 0)
            Running();

        if (ConnectInfo.IsConnSlide)
        {
            if(ConnectInfo.IsGroupPartEnd && isFinished)
            {
                HideBar(areaStep.LastOrDefault());
                Judge();
            }
            else if (ConnectInfo.IsGroupPartEnd && timeProvider.AudioTime - forceJudgeTiming >= 0)
                TooLateJudge();
            else if(isFinished)
                HideBar(areaStep.LastOrDefault());
        }
        else if (isFinished)
        {
            HideBar(areaStep.LastOrDefault());
            Judge();
        }
        else if (timeProvider.AudioTime - forceJudgeTiming >= 0)
            TooLateJudge();
    }
    // Update is called once per frame
    private void Update()
    {
        if (star_slide == null)
        {
            if (isFinished)
                DestroySelf();
            return;
        }
        // During Slide fade-in, opacity rises from 0 to 0.55 over 200ms
        var startiming = timeProvider.AudioTime - timeStart;
        if (startiming <= 0f)
        {
            if (fadeInTime >= -0.0001f)
            {
                fadeInAnimator.enabled = false;
                setSlideBarAlpha(startiming >= 0f ? 1f : 0f);
                return;
            }
            if (startiming >= -0.05f)
            {
                fadeInAnimator.enabled = false;
                setSlideBarAlpha(1f);
            }
            else if (!fadeInAnimator.enabled && startiming >= fadeInTime)
                fadeInAnimator.enabled = true;
            return;
            
        }
        fadeInAnimator.enabled = false;
        setSlideBarAlpha(1f);

        star_slide.SetActive(true);
        var timing = timeProvider.AudioTime - time;
        if (timing <= 0f)
        {
            canShine = true;
            float alpha;
            if (ConnectInfo.IsConnSlide && !ConnectInfo.IsGroupPartHead)
                alpha = 0;
            else
            {
                // Only a starting Slide, not a child segment in a Slide Group, fades in its initial star
                alpha = 1f - -timing / (time - timeStart);
                alpha = alpha > 1f ? 1f : alpha;
                alpha = alpha < 0f ? 0f : alpha;                
            }

            spriteRenderer_star.color = new Color(1, 1, 1, alpha);
            star_slide.transform.localScale = new Vector3(alpha + 0.5f, alpha + 0.5f, alpha + 0.5f);
            star_slide.transform.position = slidePositions[0];
            applyStarRotation(slideRotations[0]);
        }
        else
        {
            UpdateStar();
            Running();
        }
        Check();
    }
    public float GetSlideLength()
    {
        float len = 0;
        for (int i = 0; i < slidePositions.Count - 2; i++)
        {
            var a = slidePositions[i];
            var b = slidePositions[i + 1];
            len += (b - a).magnitude; 
        }
        return len;
    }
    public void Check(object sender, InputEventArgs arg) => Check();
    /// <summary>
    /// Checks the judgement queue
    /// </summary>
    public void Check()
    {
        if (previewOnly)
            return;
        if (isFinished || !canCheck)
            return;
        else if (isChecking)
            return;
        else if (InputManager.Mode is AutoPlayMode.Enable or AutoPlayMode.Random)
            return;
        isChecking = true;
        if (ConnectInfo.Parent != null && judgeQueue.Count < _judgeQueue.Count)
        {
            if(!ConnectInfo.ParentFinished)
                ConnectInfo.Parent.GetComponent<SlideDrop>().ForceFinish();
        }

        var first = judgeQueue.First();
        JudgeArea? second = null;

        if (judgeQueue.Count >= 2)
            second = judgeQueue[1];
            var fType = first.GetSensorTypes();
            foreach (var t in fType)
            {
                var sensor = sManager.GetSensor(t);
                first.Judge(t, sensor.Status);
            }

        if (second is not null && (first.CanSkip || first.On))
        {
                var sType = second.GetSensorTypes();
                foreach (var t in sType)
                {
                    var sensor = sManager.GetSensor(t);
                    second.Judge(t, sensor.Status);
                }

            if (second.IsFinished)
            {
                HideBar(first.SlideIndex);
                RemoveJudgeAreas(2);
                isChecking = false;
                return;
            }
            else if (second.On)
            {
                HideBar(first.SlideIndex);
                RemoveJudgeAreas(1);
                isChecking = false;
                return;
            }
        }

        if (first.IsFinished)
        {
            HideBar(first.SlideIndex);
            RemoveJudgeAreas(1);
            isChecking = false;
            return;
        }
        isChecking = false;
    }
    void HideBar(int endIndex)
    {
        var logicalHideCount = Mathf.Clamp(endIndex, 0, logicalBarCount);
        var visualHideCount = logicalBarCount > 0
            ? Mathf.CeilToInt(logicalHideCount * slideBars.Count / (float)logicalBarCount)
            : 0;
        for (var i = 0; i < visualHideCount && i < slideBars.Count; i++)
            slideBars[i].SetActive(false);
    }
    /// <summary>
    /// AutoPlay
    /// <para>
    /// Triggers Sensors
    /// </para>
    /// </summary>
    void Running()
    {
        if (star_slide == null)
            return;
        else if (InputManager.Mode is AutoPlayMode.Enable or AutoPlayMode.Random or AutoPlayMode.Disable)
            return;

        var starRadius = 0.763736616f;
        var starPos = star_slide.transform.position;
        var oldList = new List<Sensor>(triggerSensors);
        triggerSensors.Clear();
        foreach (var s in sensors.Select(x => x.GetComponent<RectTransform>()))
        {
            var sensor = s.GetComponent<Sensor>();
            if (IgnoreSensorGroup(sensor.Group))
                continue;

            var rCenter = s.position;
            var rWidth = s.rect.width * s.lossyScale.x;
            var rHeight = s.rect.height * s.lossyScale.y;

            var radius = Math.Max(rWidth, rHeight) / 2;

            if ((starPos - rCenter).sqrMagnitude <= (radius * radius + starRadius * starRadius))
                triggerSensors.Add(sensor);
        }
        var untriggerSensors = oldList.Where(x => !triggerSensors.Contains(x));

        foreach (var s in untriggerSensors)
            sManager.SetSensorOff(s.Type, guid);
        foreach (var s in triggerSensors)
            sManager.SetSensorOn(s.Type, guid);
    }
    /// <summary>
    /// Judges the Slide
    /// </summary>
    void Judge()
    {
        if (!ConnectInfo.IsGroupPartEnd && ConnectInfo.IsConnSlide)
            return;
        var starTiming = timeStart + (time - timeStart) * 0.75;
        var stayTime = (time + LastFor) - judgeTiming; // Dwell time
        if (!isJudged)
        {
            arriveTime = timeProvider.AudioTime;
            var triggerTime = timeProvider.AudioTime;           

            const float totalInterval = 1.2f; // Seconds
            const float nPInterval = 0.4666667f; // Base Perfect interval

            float extInterval = MathF.Min(stayTime / 4, 0.733333f);           // Extra Perfect interval
            float pInterval = MathF.Min(nPInterval + extInterval, totalInterval);// Total Perfect interval
            var ext = MathF.Max(extInterval - 0.4f,0);
            float grInterval = MathF.Max(0.4f - extInterval, 0);        // Total Great interval
            float gdInterval = MathF.Max(0.3333334f - ext, 0); // Total Good interval

            var diff = judgeTiming - triggerTime; // Positive is Fast; negative is Late
            bool isFast = false;
            JudgeType? judge = null;

            if (diff > 0)
                isFast = true;

            var p = pInterval / 2;
            var gr = grInterval / 2;
            var gd = gdInterval / 2;
            diff = MathF.Abs(diff);

            if( gr == 0 )
            {
                if(diff >= p)
                    judge = isFast ? JudgeType.FastGood : JudgeType.LateGood;
                else
                    judge = JudgeType.Perfect;
            }
            else
            {
                if (diff >= gr + p || diff >= totalInterval / 2)
                    judge = isFast ? JudgeType.FastGood : JudgeType.LateGood;
                else if (diff >= p)
                    judge = isFast ? JudgeType.FastGreat : JudgeType.LateGreat;
                else
                    judge = JudgeType.Perfect;
            }            
            judgeResult = judge ?? JudgeType.Miss;
            SetJust();
            isJudged = true;
        }
        else if (arriveTime < starTiming && timeProvider.AudioTime >= starTiming + stayTime * 0.8)
            DestroySelf();
        else if (arriveTime >= starTiming && timeProvider.AudioTime >= arriveTime + stayTime * 0.8)
            DestroySelf();
    }
    void SetJust()
    {
        switch (judgeResult)
        {
            case JudgeType.FastGreat2:
            case JudgeType.FastGreat1:
            case JudgeType.FastGreat:
                slideOK.GetComponent<LoadJustSprite>().setFastGr();
                break;
            case JudgeType.FastGood:
                slideOK.GetComponent<LoadJustSprite>().setFastGd();
                break;
            case JudgeType.LateGood:
                slideOK.GetComponent<LoadJustSprite>().setLateGd();
                break;
            case JudgeType.LateGreat1:
            case JudgeType.LateGreat2:
            case JudgeType.LateGreat:
                slideOK.GetComponent<LoadJustSprite>().setLateGr();
                break;

        }
    }
    /// <summary>
    /// Calculates when the guide Star enters the final judgement area
    /// </summary>
    /// <returns>Correct judgement frame in seconds</returns>
    float CalJudgeTiming()
    {
        var s = judgeSensors.LastOrDefault().gameObject.transform.GetComponent<RectTransform>();
        var starRadius = 0.763736616f;
        var rCenter = s.position;
        var rWidth = s.rect.width * s.lossyScale.x;
        var rHeight = s.rect.height * s.lossyScale.y;

        var radius = Math.Max(rWidth, rHeight) / 2;
        for (float process = 0.85f; process < 1;process += 0.01f)
        {
            var indexProcess = (slidePositions.Count - 1) * process;
            var index = (int)indexProcess;
            var pos = indexProcess - index;

            var a = slidePositions[index + 1];
            var b = slidePositions[index];
            var ba = a - b;
            var newPos = ba * pos + b;

            if ((newPos - rCenter).sqrMagnitude <= (radius * radius + starRadius * starRadius))
                return GetTimeProgressForPathProgress(process);
        }
        return 0.9f;
    }
    /// <summary>
    /// Forces a TooLate judgement and destroys the Slide
    /// </summary>
    void TooLateJudge()
    {
        if (judgeQueue.Count == 1)
            slideOK.GetComponent<LoadJustSprite>().setLateGd();
        else
            slideOK.GetComponent<LoadJustSprite>().setMiss();
        isJudged = true;
        DestroySelf();
    }
    /// <summary>
    /// Destroys the current Slide
    /// <para>When <paramref name="onlyStar"/> is true, destroys only the guide Star</para>
    /// </summary>
    /// <param name="onlyStar"></param>
    void DestroySelf(bool onlyStar = false)
    {
        
        if (onlyStar)
        { 
            Destroy(star_slide);
            star_slide = null;
            ClearTriggeredSensor();
        }
        else
        {
            if(ConnectInfo.Parent != null)
                Destroy(ConnectInfo.Parent);

            foreach (GameObject obj in slideBars)
                obj.SetActive(false);

            if (star_slide != null)
                Destroy(star_slide);
            Destroy(gameObject);
        }
    }
    /// <summary>
    /// Clears all triggered Sensors
    /// </summary>
    void ClearTriggeredSensor()
    {
        var sensors = _judgeQueue.SelectMany(x => x.GetAreas())
                           .Select(x => x.Type);
        foreach (var t in sensors)
            sManager.SetSensorOff(t, guid);
        // Also release every sensor the auto-play star physically overlapped. Running() turns
        // these On by geometry (star radius), which can include sensors OUTSIDE the judge path,
        // so releasing only the judge areas leaves those stuck On. A stuck-On sensor makes
        // Sensor.Click() a no-op, so DJAuto's ClickSensor stops registering and every later note
        // on it misses. This is worst after a pause: Running() keeps re-asserting SetSensorOn
        // on the frozen star position each FixedUpdate, and those are never cleared on destroy.
        foreach (var s in triggerSensors)
            if (s != null)
                sManager.SetSensorOff(s.Type, guid);
        triggerSensors.Clear();
    }
    void OnDestroy()
    {
        if (isDestroying)
            return;
        isDestroying = true;
        if (isJudgeInputBound)
            foreach (var sensor in boundSensors)
                inputManager?.UnbindSensor(Check, sensor);
        if (previewOnly || HttpHandler.IsReloding)
            return;
        ClearTriggeredSensor();
        if (ConnectInfo.Parent != null)
            Destroy(ConnectInfo.Parent);
        if(star_slide != null)
            Destroy(star_slide);
        if (ConnectInfo.IsGroupPartEnd || !ConnectInfo.IsConnSlide)
        {
            switch(InputManager.Mode)
            {
                case AutoPlayMode.Enable:
                    judgeResult = JudgeType.Perfect;
                    SetJust();
                    break;
                case AutoPlayMode.Random:
                    judgeResult = (JudgeType)UnityEngine.Random.Range(1, 14);
                    SetJust();
                    break;
            }
            // Only completion of the last Slide in a group shows the judgement bar and increments the total
            objectCounter.ReportResult(this, judgeResult, isBreak);
            if (isBreak && judgeResult == JudgeType.Perfect)
                slideOK.GetComponent<Animator>().runtimeAnimatorController = judgeBreakShine;
            slideOK.SetActive(true);
        }
        else
        {
            // Remove the judgement bar when this is not the last Slide in the group
            Destroy(slideOK);
        }
    }
    /// <summary>
    /// Updates the guide Star state
    /// <para>Includes position and angle</para>
    /// </summary>
    void UpdateStar()
    {
        spriteRenderer_star.color = Color.white;
        star_slide.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);

        var process = GetSlidePathProgress(timeProvider.AudioTime);
        var indexProcess = (slidePositions.Count - 1) * process;
        var index = (int)indexProcess;
        var pos = indexProcess - index;

        if(process == 1)
        {
            switch (InputManager.Mode)
            {
                case AutoPlayMode.Enable:
                case AutoPlayMode.Random:
                    var barIndex = areaStep[(int)(process * (areaStep.Count - 1))];
                    HideBar(barIndex);
                    DestroySelf();
                    judgeQueue.Clear();
                    return;
            }
            star_slide.transform.position = slidePositions.LastOrDefault();
            applyStarRotation(slideRotations.LastOrDefault());
            if (ConnectInfo.IsConnSlide && !ConnectInfo.IsGroupPartEnd)
                DestroySelf(true);
            else if (isFinished && isJudged)
                DestroySelf();
        }
        else
        {
            var a = slidePositions[index + 1];
            var b = slidePositions[index];
            var ba = a - b;
            var newPos = ba * pos + b;

            star_slide.transform.position = newPos;
            if (index < slideRotations.Count - 1)
            {
                var _a = slideRotations[index + 1].eulerAngles.z;
                var _b = slideRotations[index].eulerAngles.z;
                var dAngle = Mathf.DeltaAngle(_b, _a) * pos;
                dAngle = Mathf.Abs(dAngle);
                var newRotation = Quaternion.Euler(0f, 0f,
                                Mathf.MoveTowardsAngle(_b, _a, dAngle));
                applyStarRotation(newRotation);
            }
        } 
        switch(InputManager.Mode)
        {
            case AutoPlayMode.Enable:
            case AutoPlayMode.Random:
                var barIndex = areaStep[(int)(process * (areaStep.Count - 1))];
                RemoveJudgeAreas((int)(process * (judgeQueue.Count - 1)));
                HideBar(barIndex);
                break;
        }
    }

    private float GetSlidePathProgress(float audioTime)
    {
        return SvController.GetTypedOnlyProgress(time, LastFor, audioTime, "slide");
    }

    private float GetTimeProgressForPathProgress(float pathProgress)
    {
        if (!SvController.HasTypedCurve("slide"))
            return pathProgress;

        var bestTimeProgress = pathProgress;
        var bestError = float.MaxValue;
        const int samples = 128;
        for (var sample = 0; sample <= samples; sample++)
        {
            var timeProgress = sample / (float)samples;
            var currentPathProgress = GetSlidePathProgress(time + LastFor * timeProgress);
            var error = Mathf.Abs(currentPathProgress - pathProgress);
            if (error >= bestError)
                continue;
            bestError = error;
            bestTimeProgress = timeProgress;
        }
        return bestTimeProgress;
    }
   
    private void setSlideBarAlpha(float alpha)
    {
        if (Mathf.Approximately(currentSlideBarAlpha, alpha))
            return;

        currentSlideBarAlpha = alpha;
        var color = new Color(1f, 1f, 1f, alpha);
        foreach (var renderer in slideBarRenderers)
            renderer.color = color;
    }

    private void RemoveJudgeAreas(int count)
    {
        count = Math.Min(count, judgeQueue.Count);
        if (count > 0)
            judgeQueue.RemoveRange(0, count);
    }
    private void applyStarRotation(Quaternion newRotation)
    {
        var halfFlip = newRotation.eulerAngles;
        halfFlip.z += 180f;
        if (isSpecialFlip)
            star_slide.transform.rotation = Quaternion.Euler(halfFlip);
        else
            star_slide.transform.rotation = newRotation;
    }
    public GameObject[] GetSlideBars() => slideBars.ToArray();
    public bool CanShine() => canShine;
}
