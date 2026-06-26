using System;
using System.Collections.Generic;
using UnityEngine;

public class DisplayTimelineController : MonoBehaviour
{
    private readonly List<DisplayChange> events = new();
    private readonly Dictionary<string, List<DisplayChange>> eventsByProperty = new();
    private readonly List<SubtitleChange> subtitles = new();
    private readonly Dictionary<string, DisplayTrack> tracks = new();
    private AudioTimeProvider timeProvider;
    private BGManager bgManager;
    private ObjectCounter objectCounter;
    private NoteEffectManager noteEffectManager;
    private SpriteRenderer[] judgeLines = Array.Empty<SpriteRenderer>();
    private bool playbackActive;

    private float initialJudgeLine;
    private float initialJudgeInfo;
    private float initialComboInfo;
    private float initialOuterBrightness;
    private float initialInnerBrightness;
    private float initialJudgeText;
    private float lastJudgeLine = float.NaN;
    private float lastJudgeInfo = float.NaN;
    private float lastComboInfo = float.NaN;
    private float lastOuterBrightness = float.NaN;
    private float lastInnerBrightness = float.NaN;
    private float lastJudgeText = float.NaN;
    private int lastComboDisplay = int.MinValue;
    private GUIStyle subtitleStyle;
    private GUIStyle subtitleShadowStyle;
    private bool hasDisplayEvents;
    private int subtitleCursor = -1;
    private float lastSubtitleTime = float.MinValue;

    internal void Configure(
        List<DisplayChange> displayEvents,
        List<SubtitleChange> subtitleEvents,
        bool showJudgeLine,
        bool showJudgeInfo,
        bool showComboInfo,
        bool showJudgeText,
        float innerBrightness,
        float outerBrightness)
    {
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        bgManager = GameObject.Find("Background").GetComponent<BGManager>();
        objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        noteEffectManager = GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>();

        // Match the original player: the real judge line is the persistent
        // "Outline" object loaded by the server scene. SampleScene also has a
        // DebugOutline with CustomSkin, so type-based lookup can bind the wrong one.
        var outlineObject = GameObject.Find("Outline");
        var outlineRenderer = outlineObject != null
            ? outlineObject.GetComponent<SpriteRenderer>()
            : null;
        if (outlineRenderer == null)
        {
            var debugOutline = GameObject.Find("DebugOutline");
            outlineRenderer = debugOutline != null
                ? debugOutline.GetComponent<SpriteRenderer>()
                : null;
        }
        judgeLines = outlineRenderer != null
            ? new[] { outlineRenderer }
            : Array.Empty<SpriteRenderer>();

        initialJudgeLine = showJudgeLine ? 1f : 0f;
        initialJudgeInfo = showJudgeInfo ? 1f : 0f;
        initialComboInfo = showComboInfo ? 1f : 0f;
        initialJudgeText = showJudgeText ? 1f : 0f;
        initialInnerBrightness = Mathf.Clamp01(innerBrightness);
        initialOuterBrightness = Mathf.Clamp01(outerBrightness);

        events.Clear();
        eventsByProperty.Clear();
        if (displayEvents != null)
        {
            events.AddRange(displayEvents);
            events.Sort((left, right) => left.time.CompareTo(right.time));
            foreach (var item in events)
            {
                if (!eventsByProperty.TryGetValue(item.property, out var propertyEvents))
                {
                    propertyEvents = new List<DisplayChange>();
                    eventsByProperty[item.property] = propertyEvents;
                }
                propertyEvents.Add(item);
            }
        }
        hasDisplayEvents = events.Count > 0;
        tracks.Clear();
        tracks["ShowJudgeLine"] = CreateTrack("ShowJudgeLine", initialJudgeLine);
        tracks["ShowJudgeInfo"] = CreateTrack("ShowJudgeInfo", initialJudgeInfo);
        tracks["ShowComboInfo"] = CreateTrack("ShowComboInfo", initialComboInfo);
        tracks["ShowJudgeText"] = CreateTrack("ShowJudgeText", initialJudgeText);
        tracks["InnerBrightness"] = CreateTrack("InnerBrightness", initialInnerBrightness);
        tracks["OuterBrightness"] = CreateTrack("OuterBrightness", initialOuterBrightness);
        subtitles.Clear();
        if (subtitleEvents != null)
        {
            subtitles.AddRange(subtitleEvents);
            subtitles.Sort((left, right) => left.time.CompareTo(right.time));
        }
        ResetSubtitleCursor();

        playbackActive = true;
        InvalidateAppliedValues();
        ApplyAt(timeProvider.AudioTime);
        enabled = true;
    }

    public void SetPlaybackActive(bool active)
    {
        playbackActive = active;
        if (active)
            ApplyAt(timeProvider != null ? timeProvider.AudioTime : 0f);
        else
            RestoreStandby();
    }

    public void SetImmediateDisplay(
        bool showJudgeLine,
        bool showJudgeInfo,
        bool showComboInfo,
        bool showJudgeText,
        float innerBrightness,
        float outerBrightness)
    {
        var now = timeProvider != null ? timeProvider.AudioTime : 0f;
        var judgeLineTarget = showJudgeLine ? 1f : 0f;
        initialJudgeLine = showJudgeLine ? 1f : 0f;
        initialJudgeInfo = showJudgeInfo ? 1f : 0f;
        initialComboInfo = showComboInfo ? 1f : 0f;
        initialJudgeText = showJudgeText ? 1f : 0f;
        initialInnerBrightness = Mathf.Clamp01(innerBrightness);
        initialOuterBrightness = Mathf.Clamp01(outerBrightness);
        if (tracks.TryGetValue("ShowJudgeLine", out var judgeLineTrack))
            judgeLineTrack.TransitionTo(now, judgeLineTarget, 0.25f);
        else
            tracks["ShowJudgeLine"] = CreateTrack("ShowJudgeLine", initialJudgeLine);
        tracks["ShowJudgeInfo"] = CreateTrack("ShowJudgeInfo", initialJudgeInfo);
        tracks["ShowComboInfo"] = CreateTrack("ShowComboInfo", initialComboInfo);
        tracks["ShowJudgeText"] = CreateTrack("ShowJudgeText", initialJudgeText);
        tracks["InnerBrightness"] = CreateTrack("InnerBrightness", initialInnerBrightness);
        tracks["OuterBrightness"] = CreateTrack("OuterBrightness", initialOuterBrightness);
        InvalidateAppliedValues();
        ApplyAt(now);
    }

    private void Update()
    {
        if (playbackActive && timeProvider != null)
            ApplyAt(timeProvider.AudioTime);
    }

    private void OnGUI()
    {
        if (!playbackActive || timeProvider == null)
            return;

        var text = GetSubtitle(timeProvider.AudioTime);
        if (string.IsNullOrEmpty(text))
            return;

        EnsureSubtitleStyles();
        var rect = new Rect(28f, 22f, Screen.width - 56f, Screen.height * 0.3f);
        var shadowRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height);
        GUI.Label(shadowRect, text, subtitleShadowStyle);
        GUI.Label(rect, text, subtitleStyle);
    }

    private string GetSubtitle(float time)
    {
        if (time < lastSubtitleTime)
            ResetSubtitleCursor();
        lastSubtitleTime = time;

        while (subtitleCursor + 1 < subtitles.Count &&
               subtitles[subtitleCursor + 1].time <= time)
            subtitleCursor++;

        var active = subtitleCursor >= 0 ? subtitles[subtitleCursor] : null;
        if (active == null)
            return null;
        if (active.duration >= 0f && time > active.time + active.duration)
            return null;
        return active.text;
    }

    private void EnsureSubtitleStyles()
    {
        if (subtitleStyle != null)
            return;

        var font = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "Microsoft JhengHei", "Arial" }, 32);
        subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = 32,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        subtitleShadowStyle = new GUIStyle(subtitleStyle);
        subtitleShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
    }

    private void ApplyAt(float time)
    {
        if (judgeLines.Length == 0 || judgeLines[0] == null)
            RefreshJudgeLineReference();
        var judgeLine = time < 0f ? 0f : tracks["ShowJudgeLine"].Evaluate(time);
        var judgeInfo = tracks["ShowJudgeInfo"].Evaluate(time);
        var comboInfo = tracks["ShowComboInfo"].Evaluate(time);
        var judgeText = tracks["ShowJudgeText"].Evaluate(time);
        var inner = tracks["InnerBrightness"].Evaluate(time);
        var outer = tracks["OuterBrightness"].Evaluate(time);
        var hasComboDisplayEvents = eventsByProperty.TryGetValue("ComboDisplay", out var comboDisplayEvents) &&
                                    comboDisplayEvents.Count > 0;
        var comboDisplay = hasComboDisplayEvents ? EvaluateComboDisplay(time, comboDisplayEvents) : lastComboDisplay;

        foreach (var renderer in judgeLines)
        {
            if (renderer == null)
                continue;
            renderer.enabled = true;
            var color = renderer.color;
            color.a = judgeLine;
            renderer.color = color;
            renderer.forceRenderingOff = judgeLine <= 0.001f;
        }
        lastJudgeLine = judgeLine;

        if (!Mathf.Approximately(judgeInfo, lastJudgeInfo) ||
            !Mathf.Approximately(comboInfo, lastComboInfo))
        {
            objectCounter?.SetSideDisplayAlpha(judgeInfo, comboInfo);
            lastJudgeInfo = judgeInfo;
            lastComboInfo = comboInfo;
        }

        if (!Mathf.Approximately(inner, lastInnerBrightness) ||
            !Mathf.Approximately(outer, lastOuterBrightness))
        {
            bgManager?.SetCoverAlpha(inner, outer);
            lastInnerBrightness = inner;
            lastOuterBrightness = outer;
        }

        if (!Mathf.Approximately(judgeText, lastJudgeText))
        {
            noteEffectManager?.ApplyJudgeTextAlpha(judgeText);
            lastJudgeText = judgeText;
        }

        if (hasComboDisplayEvents && comboDisplay != lastComboDisplay)
        {
            objectCounter?.ComboSetActive((EditorComboIndicator)comboDisplay);
            lastComboDisplay = comboDisplay;
        }

    }

    private int EvaluateComboDisplay(float time, List<DisplayChange> comboEvents)
    {
        var value = lastComboDisplay == int.MinValue ? 0 : lastComboDisplay;
        foreach (var item in comboEvents)
        {
            if (item.time > time)
                break;
            value = Mathf.RoundToInt(item.target);
        }
        return value;
    }

    private DisplayTrack CreateTrack(string property, float initial)
    {
        eventsByProperty.TryGetValue(property, out var propertyEvents);
        return new DisplayTrack(propertyEvents, initial);
    }

    private void RefreshJudgeLineReference()
    {
        var outlineObject = GameObject.Find("Outline");
        var renderer = outlineObject != null
            ? outlineObject.GetComponent<SpriteRenderer>()
            : null;
        if (renderer == null)
            return;
        if (judgeLines.Length == 1 && judgeLines[0] == renderer)
            return;

        judgeLines = new[] { renderer };
        lastJudgeLine = float.NaN;
    }

    private static float ValueAt(float time, float start, float duration, float from, float target)
    {
        if (start == float.MinValue)
            return from;
        if (duration <= 0f)
            return target;
        return Mathf.Lerp(from, target, Mathf.Clamp01((time - start) / duration));
    }

    private void RestoreStandby()
    {
        foreach (var renderer in judgeLines)
        {
            if (renderer == null)
                continue;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            var color = renderer.color;
            color.a = 1f;
            renderer.color = color;
        }

        objectCounter?.SetSideDisplayAlpha(initialJudgeInfo, initialComboInfo);
        bgManager?.SetCoverAlpha(initialInnerBrightness, initialOuterBrightness);
        noteEffectManager?.ApplyJudgeTextAlpha(initialJudgeText);
        InvalidateAppliedValues();
    }

    private void InvalidateAppliedValues()
    {
        lastJudgeLine = float.NaN;
        lastJudgeInfo = float.NaN;
        lastComboInfo = float.NaN;
        lastOuterBrightness = float.NaN;
        lastInnerBrightness = float.NaN;
        lastJudgeText = float.NaN;
        lastComboDisplay = int.MinValue;
    }

    private void ResetSubtitleCursor()
    {
        subtitleCursor = -1;
        lastSubtitleTime = float.MinValue;
    }

    private sealed class DisplayTrack
    {
        private readonly List<DisplayChange> events;
        private readonly float initial;
        private int cursor;
        private float current;
        private float transitionStart = float.MinValue;
        private float transitionDuration;
        private float transitionFrom;
        private float transitionTarget;
        private float lastTime = float.MinValue;

        public DisplayTrack(List<DisplayChange> events, float initial)
        {
            this.events = events;
            this.initial = initial;
            Reset();
        }

        public float Evaluate(float time)
        {
            if (time < lastTime)
                Reset();
            lastTime = time;

            while (events != null && cursor < events.Count && events[cursor].time <= time)
            {
                var item = events[cursor++];
                current = ValueAt((float)item.time, transitionStart, transitionDuration,
                    transitionFrom, transitionTarget);
                transitionStart = (float)item.time;
                transitionDuration = Mathf.Max(0f, item.duration);
                transitionFrom = current;
                transitionTarget = Mathf.Clamp01(item.target);
            }

            return ValueAt(time, transitionStart, transitionDuration,
                transitionFrom, transitionTarget);
        }

        public void TransitionTo(float time, float target, float duration)
        {
            var from = Evaluate(time);
            transitionStart = time;
            transitionDuration = Mathf.Max(0f, duration);
            transitionFrom = from;
            transitionTarget = Mathf.Clamp01(target);
            lastTime = time;
        }

        private void Reset()
        {
            cursor = 0;
            current = initial;
            transitionStart = float.MinValue;
            transitionDuration = 0f;
            transitionFrom = initial;
            transitionTarget = initial;
            lastTime = float.MinValue;
        }
    }
}
