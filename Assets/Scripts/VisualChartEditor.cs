using System;
using System.Collections;
using System.Linq;
using System.Text;
using Assets.Scripts.Types;
using UnityEngine;
using UnityEngine.Networking;

// Standby-only pointer input for adding simple notes to the Edit caret through localhost.
public class VisualChartEditor : MonoBehaviour
{
    private const float DragSeconds = 0.22f;
    private const float TapRingRadiusRatio = 1.025f;
    private const string EditEndpoint = "http://127.0.0.1:8014/";

    private AudioTimeProvider timeProvider;
    private JsonDataLoader dataLoader;
    private Transform sensorRoot;
    private Camera mainCamera;
    private SensorType? dragStart;
    private float dragStartedAt;
    private bool dragStartsAsTap;
    private readonly System.Collections.Generic.List<SensorType> dragPath = new();
    private readonly System.Collections.Generic.List<SensorType> dragJudgePath = new();
    private readonly System.Collections.Generic.List<Vector2> dragScreenPath = new();
    private readonly System.Collections.Generic.List<SensorType> dragTouches = new();
    private readonly System.Collections.Generic.List<SensorType> pendingTouches = new();
    private float pendingTouchDeadline;
    [Serializable]
    private sealed class VisualEditMessage
    {
        public string note;
        public string action;
        public int slideStart;
    }

#if MAJDATA_VE_TRACE
    private float lastTraceAt;
#endif

    private void Update()
    {
        if (timeProvider == null)
            timeProvider = FindAnyObjectByType<AudioTimeProvider>();
        if (dataLoader == null)
            dataLoader = FindAnyObjectByType<JsonDataLoader>();
        if (sensorRoot == null)
            sensorRoot = GameObject.Find("Sensors")?.transform;
        if (mainCamera == null)
            mainCamera = Camera.main;
#if MAJDATA_VE_TRACE
        if (Time.unscaledTime - lastTraceAt > 1f)
        {
            lastTraceAt = Time.unscaledTime;
            Debug.Log($"[VE] tp={timeProvider != null} dl={dataLoader != null} sr={sensorRoot != null} " +
                      $"cam={mainCamera != null} isStart={timeProvider?.isStart} prev={timeProvider?.IsPreview} " +
                      $"reload={HttpHandler.IsReloding} mouse={Input.GetMouseButton(0)}");
        }
#endif
        if (timeProvider == null || dataLoader == null || sensorRoot == null || mainCamera == null ||
            (timeProvider.isStart && !timeProvider.IsPreview) || HttpHandler.IsReloding)
            return;

        if (Input.GetMouseButtonDown(1))
        {
            SendNote(string.Empty, "undo");
            return;
        }

        if (!Input.GetMouseButton(0) && pendingTouches.Count > 0 && Time.unscaledTime >= pendingTouchDeadline)
            FlushPendingTouches();

        if (Input.GetMouseButtonDown(0))
        {
            FlushPendingTouches();
            dragStartsAsTap = IsOutsideTouchRing(Input.mousePosition);
            dragStart = dragStartsAsTap
                ? FindNearestArea(Input.mousePosition, SensorGroup.A)
                : null;
            dragPath.Clear();
            dragJudgePath.Clear();
            dragScreenPath.Clear();
            CollectDragKey(Input.mousePosition);
            dragTouches.Clear();
            CollectTouch(Input.mousePosition);
            dragStartedAt = Time.unscaledTime;
        }

        if (Input.GetMouseButton(0))
        {
            if (dragStartsAsTap)
                CollectDragKey(Input.mousePosition);
            else
                CollectTouch(Input.mousePosition);
        }

        if (!Input.GetMouseButtonUp(0))
            return;

        var held = Time.unscaledTime - dragStartedAt;
        var target = FindNearestArea(Input.mousePosition, SensorGroup.A);
        if (dragStartsAsTap && dragStart.HasValue && target.HasValue && held >= DragSeconds &&
            target.Value != dragStart.Value)
        {
            SendNote(BuildSlideToken(dragStart.Value, target.Value));
            return;
        }

        if (dragStartsAsTap && dragStart.HasValue)
        {
            SendNote(ToKeyNumber(dragStart.Value).ToString(), "slideHead");
            return;
        }

        if (dragTouches.Count == 0)
            return;

        if (held >= DragSeconds)
        {
            SendNote(string.Join("/", dragTouches.Select(BuildTouchToken)));
            return;
        }

        if (TryFindPreviewSlideAtSensor(dragTouches[0], out var slideStart))
        {
            SendNote(BuildTouchToken(dragTouches[0]), "slidePath", slideStart);
            return;
        }

        AddPendingTouch(dragTouches[0]);
    }

    private void OnDisable()
    {
        dragStart = null;
        dragPath.Clear();
        dragJudgePath.Clear();
        dragScreenPath.Clear();
        dragTouches.Clear();
        pendingTouches.Clear();
    }

    private string BuildTouchToken(SensorType type)
    {
        return ToSimaiToken(type);
    }

    private bool IsOutsideTouchRing(Vector2 screenPosition)
    {
        var center = sensorRoot.GetChild((int)SensorType.C).position;
        var centerScreen = mainCamera.WorldToScreenPoint(center);
        var touchRadius = 0f;
        var touchCount = 0;
        for (var i = 0; i < sensorRoot.childCount; i++)
        {
            var sensor = sensorRoot.GetChild(i).GetComponent<Sensor>();
            if (sensor == null || sensor.Group != SensorGroup.A)
                continue;
            touchRadius += Vector2.Distance(centerScreen, mainCamera.WorldToScreenPoint(sensor.transform.position));
            touchCount++;
        }
        return touchCount > 0 &&
               Vector2.Distance(centerScreen, screenPosition) >
               touchRadius / touchCount * TapRingRadiusRatio;
    }

    private void CollectTouch(Vector2 screenPosition)
    {
        if (IsOutsideTouchRing(screenPosition))
            return;
        var touch = FindAreaAtPoint(screenPosition);
        if (touch.HasValue && !dragTouches.Contains(touch.Value))
            dragTouches.Add(touch.Value);
    }

    private void CollectDragKey(Vector2 screenPosition)
    {
        if (dragScreenPath.Count == 0 || Vector2.Distance(dragScreenPath[^1], screenPosition) >= 2f)
            dragScreenPath.Add(screenPosition);

        var judgeArea = FindSlideJudgeAreaAtPoint(screenPosition);
        if (judgeArea.HasValue && (dragJudgePath.Count == 0 || dragJudgePath[^1] != judgeArea.Value))
            dragJudgePath.Add(judgeArea.Value);

        var key = FindNearestArea(screenPosition, SensorGroup.A);
        if (key.HasValue && (dragPath.Count == 0 || dragPath[^1] != key.Value))
            dragPath.Add(key.Value);
    }

    private string BuildSlideToken(SensorType start, SensorType end)
    {
        var startKey = ToKeyNumber(start);
        var endKey = ToKeyNumber(end);
        var observed = NormalizeSensorPath(dragJudgePath);
        string bestToken = null;
        var bestScore = float.PositiveInfinity;

        foreach (var candidate in EnumerateSlideCandidates(startKey, endKey))
        {
            if (!dataLoader.TryGetSlideRoute(candidate, out var expected, out var routePositions))
                continue;
            var normalizedExpected = NormalizeSensorPath(expected);
            var sensorScore = SensorPathDistance(observed, normalizedExpected);
            var geometryScore = RouteGeometryDistance(dragScreenPath, routePositions);
            var score = sensorScore * 10f + geometryScore;
            if (score >= bestScore)
                continue;
            bestScore = score;
            bestToken = candidate;
        }

        return bestToken ?? $"{startKey}-{endKey}";
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateSlideCandidates(int start, int end)
    {
        var operators = new[] { "-", ">", "<", "^", "v", "p", "q", "pp", "qq", "rp", "rq", "s", "z", "w" };
        foreach (var op in operators)
            yield return $"{start}{op}{end}";
        for (var middle = 1; middle <= 8; middle++)
            yield return $"{start}V{middle}{end}";
    }

    private static System.Collections.Generic.List<SensorType> NormalizeSensorPath(
        System.Collections.Generic.IEnumerable<SensorType> source)
    {
        var result = new System.Collections.Generic.List<SensorType>();
        foreach (var sensor in source)
        {
            var group = GetGroup(sensor);
            if (group is SensorGroup.D or SensorGroup.E)
                continue;
            if (result.Count == 0 || result[^1] != sensor)
                result.Add(sensor);
        }
        return result;
    }

    private static float SensorPathDistance(
        System.Collections.Generic.IReadOnlyList<SensorType> observed,
        System.Collections.Generic.IReadOnlyList<SensorType> expected)
    {
        if (observed.Count == 0 || expected.Count == 0)
            return float.PositiveInfinity;

        var previous = new int[expected.Count + 1];
        var current = new int[expected.Count + 1];
        for (var j = 0; j <= expected.Count; j++)
            previous[j] = j;
        for (var i = 1; i <= observed.Count; i++)
        {
            current[0] = i;
            for (var j = 1; j <= expected.Count; j++)
            {
                var replace = previous[j - 1] + (observed[i - 1] == expected[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), replace);
            }
            (previous, current) = (current, previous);
        }
        return previous[expected.Count] / (float)Math.Max(observed.Count, expected.Count);
    }

    private float RouteGeometryDistance(
        System.Collections.Generic.IReadOnlyList<Vector2> observed,
        System.Collections.Generic.IReadOnlyList<Vector3> expectedWorld)
    {
        if (observed.Count == 0 || expectedWorld.Count == 0)
            return float.PositiveInfinity;

        var expected = expectedWorld
            .Select(point => (Vector2)mainCamera.WorldToScreenPoint(point))
            .ToList();
        var diagonal = Mathf.Max(1f, Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height));

        static float AverageNearest(
            System.Collections.Generic.IReadOnlyList<Vector2> source,
            System.Collections.Generic.IReadOnlyList<Vector2> target)
        {
            var sum = 0f;
            foreach (var point in source)
            {
                var nearest = float.PositiveInfinity;
                foreach (var candidate in target)
                    nearest = Mathf.Min(nearest, Vector2.Distance(point, candidate));
                sum += nearest;
            }
            return sum / source.Count;
        }

        return (AverageNearest(observed, expected) + AverageNearest(expected, observed)) /
               (2f * diagonal);
    }

    private SensorType? FindAreaAtPoint(Vector2 screenPosition)
    {
        SensorType? best = null;
        var bestArea = float.PositiveInfinity;
        for (var index = 0; index < sensorRoot.childCount; index++)
        {
            var sensor = sensorRoot.GetChild(index).GetComponent<Sensor>();
            var rect = sensor?.GetComponent<RectTransform>();
            if (sensor == null || rect == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, mainCamera))
                continue;

            var area = Mathf.Abs(rect.rect.width * rect.lossyScale.x * rect.rect.height * rect.lossyScale.y);
            if (area >= bestArea)
                continue;
            bestArea = area;
            best = sensor.Type;
        }
        return best ?? FindNearestArea(screenPosition, null);
    }

    private SensorType? FindSlideJudgeAreaAtPoint(Vector2 screenPosition)
    {
        var world = mainCamera.ScreenToWorldPoint(
            new Vector3(screenPosition.x, screenPosition.y, mainCamera.nearClipPlane));
        world.z = 0f;
        for (var index = 0; index < sensorRoot.childCount; index++)
        {
            var rect = sensorRoot.GetChild(index).GetComponent<RectTransform>();
            var sensor = rect?.GetComponent<Sensor>();
            if (sensor == null || sensor.Group is SensorGroup.D or SensorGroup.E)
                continue;
            var radius = Math.Max(rect.rect.width * rect.lossyScale.x,
                                  rect.rect.height * rect.lossyScale.y) / 2f;
            if ((world - rect.position).sqrMagnitude <= radius * radius)
                return sensor.Type;
        }
        return null;
    }

    private static bool TryFindPreviewSlideAtSensor(SensorType sensor, out int slideStart)
    {
        foreach (var slide in FindObjectsByType<SlideDrop>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!slide.previewOnly || !slide.UsesSensor(sensor))
                continue;
            slideStart = slide.startPosition;
            return true;
        }

        slideStart = 0;
        return false;
    }

    private void AddPendingTouch(SensorType touch)
    {
        if (!pendingTouches.Contains(touch))
            pendingTouches.Add(touch);
        pendingTouchDeadline = Time.unscaledTime + 0.18f;
    }

    private void FlushPendingTouches()
    {
        if (pendingTouches.Count == 0)
            return;
        SendNote(string.Join("/", pendingTouches.Select(BuildTouchToken)));
        pendingTouches.Clear();
    }

    private SensorType? FindNearestArea(Vector2 screenPosition, SensorGroup? requiredGroup)
    {
        var world = mainCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, mainCamera.nearClipPlane));
        world.z = 0f;
        var nearestDistance = float.PositiveInfinity;
        SensorType? nearest = null;
        for (var i = 0; i < sensorRoot.childCount; i++)
        {
            var sensor = sensorRoot.GetChild(i).GetComponent<Sensor>();
            if (sensor == null || (requiredGroup.HasValue && sensor.Group != requiredGroup.Value))
                continue;
            var distance = (sensor.transform.position - world).sqrMagnitude;
            if (distance >= nearestDistance)
                continue;
            nearestDistance = distance;
            nearest = sensor.Type;
        }
        return nearest;
    }

    private static int ToKeyNumber(SensorType type) => (int)type - (int)SensorType.A1 + 1;

    private static string ToSimaiToken(SensorType type)
    {
        if (GetGroup(type) == SensorGroup.A)
            return "A" + ToKeyNumber(type);
        if (type == SensorType.C)
            return "C";

        var name = type.ToString();
        return name.Length >= 2 ? name : "C";
    }

    private static SensorGroup GetGroup(SensorType type)
    {
        var index = (int)type;
        if (index <= (int)SensorType.A8) return SensorGroup.A;
        if (index <= (int)SensorType.B8) return SensorGroup.B;
        if (type == SensorType.C) return SensorGroup.C;
        if (index <= (int)SensorType.D8) return SensorGroup.D;
        return SensorGroup.E;
    }

    private void SendNote(string note, string action = "note", int slideStart = 0)
    {
#if MAJDATA_VE_TRACE
        Debug.Log($"[VE] SendNote note='{note}' action={action}");
#endif
        note = note?.Trim().Trim('/');
        if (string.IsNullOrEmpty(note) && !string.Equals(action, "undo", StringComparison.Ordinal))
            return;

        var payload = JsonUtility.ToJson(new VisualEditMessage
        {
            note = note,
            action = action,
            slideStart = slideStart
        });
        // Do not use UnityWebRequest: system proxies can prevent it from reaching local Edit
        _ = LocalHttp.Client
            .PostAsync(EditEndpoint,
                new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json"))
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                    Debug.LogWarning("[VE] send failed: " +
                                     task.Exception?.GetBaseException().Message);
            }, System.Threading.Tasks.TaskScheduler.Default);
    }
}
