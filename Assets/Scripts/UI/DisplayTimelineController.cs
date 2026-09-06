using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    // The judge-area overlay follows the outline and is visible only during playback.
    private SpriteRenderer judgeArea;
    private Transform judgeLineTransform;
    private static readonly Dictionary<Transform, Vector3> originalJudgeLineScales = new();
    private bool playbackActive;
    private bool showSongDetailIntro;
    private const float IntroGameplayRevealTime =
        MajdataCore.AlphaVisualTiming.GameplayRevealTime;

    // Standby can run before the editor ever sends its display settings, and it
    // pushes these values into the scene, so they must start at the same
    // defaults the request carries (visible) instead of zero.
    private float initialJudgeLine = 1f;
    private float initialJudgeArea;
    private float initialJudgeInfo = 1f;
    private float initialComboInfo = 1f;
    private float initialOuterBrightness;
    private float initialInnerBrightness;
    private float initialJudgeText = 1f;
    private int initialComboDisplay;
    private float lastJudgeLine = float.NaN;
    private float lastJudgeArea = float.NaN;
    private float lastJudgeInfo = float.NaN;
    private float lastComboInfo = float.NaN;
    private float lastOuterBrightness = float.NaN;
    private float lastInnerBrightness = float.NaN;
    private float lastJudgeText = float.NaN;
    private int lastComboDisplay = int.MinValue;
    private GUIStyle subtitleStyle;
    private GUIStyle subtitleShadowStyle;
    private Font subtitleFont;
    private Font ownedSubtitleFont;
    private readonly Dictionary<string, Font> subtitleFontCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Font> ownedSubtitleFonts = new();
    private static readonly HashSet<string> warmedSubtitleGlyphs = new();
    private bool hasDisplayEvents;
    private int subtitleCursor = -1;
    private readonly SortedDictionary<int, SubtitleChange> activeSubtitles = new();
    private float lastSubtitleTime = float.MinValue;
    private bool subtitleGuiWarmupPending;

    private readonly List<JudgeLineColorSegment> judgeLineColors = new();
    private bool hasJudgeLineColors;
    private Color? lastJudgeLineTint;
    private bool judgeLineTintInitialized;
    // The outline survives scene reloads, so preserve each skin's original color by renderer.
    private static readonly Dictionary<SpriteRenderer, Color> originalJudgeLineRgb = new();

    internal void Configure(
        List<DisplayChange> displayEvents,
        List<SubtitleChange> subtitleEvents,
        bool showJudgeLine,
        bool showJudgeInfo,
        bool showComboInfo,
        bool showJudgeText,
        float innerBrightness,
        float outerBrightness,
        int comboDisplay,
        List<ColorChange> colorTable = null,
        bool showJudgeArea = false,
        bool showSongDetail = true)
    {
        timeProvider = GameObject.Find("AudioTimeProvider")?.GetComponent<AudioTimeProvider>();
        bgManager = GameObject.Find("Background")?.GetComponent<BGManager>();
        objectCounter = GameObject.Find("ObjectCounter")?.GetComponent<ObjectCounter>();
        noteEffectManager = GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>();

        // Bind the persistent gameplay outline instead of the scene's debug outline.
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
        judgeLineTransform = outlineRenderer != null ? outlineRenderer.transform : null;
        if (judgeLineTransform != null)
            originalJudgeLineScales[judgeLineTransform] = Vector3.one;
        SetJudgeLinePlaybackScale();
        foreach (var renderer in judgeLines)
            if (renderer != null && !originalJudgeLineRgb.ContainsKey(renderer))
                originalJudgeLineRgb[renderer] = renderer.color;

        EnsureJudgeArea(outlineObject);

        judgeLineColors.Clear();
        if (colorTable != null)
        {
            var original = GetOriginalJudgeLineColor();
            var changes = colorTable.FindAll(item =>
                string.Equals(item.noteType, "judgeline", StringComparison.OrdinalIgnoreCase));
            changes.Sort((left, right) => left.time.CompareTo(right.time));
            foreach (var ev in changes)
            {
                var target = original;
                if (!string.IsNullOrEmpty(ev.color) &&
                    !string.Equals(ev.color, "NULL", StringComparison.OrdinalIgnoreCase) &&
                    ColorUtility.TryParseHtmlString("#" + ev.color.TrimStart('#'), out var parsed))
                    target = parsed;

                var from = EvaluateJudgeLineColor((float)ev.time, original);
                judgeLineColors.Add(new JudgeLineColorSegment(
                    (float)ev.time, ev.duration, from, target));
            }
        }
        hasJudgeLineColors = judgeLineColors.Count > 0;
        if (!hasJudgeLineColors)
        {
            foreach (var renderer in judgeLines)
            {
                if (renderer == null || !originalJudgeLineRgb.TryGetValue(renderer, out var orig))
                    continue;
                var c = renderer.color;
                c.r = orig.r; c.g = orig.g; c.b = orig.b;
                renderer.color = c;
            }
        }

        initialJudgeLine = showJudgeLine ? 1f : 0f;
        initialJudgeArea = showJudgeArea ? 1f : 0f;
        initialJudgeInfo = showJudgeInfo ? 1f : 0f;
        initialComboInfo = showComboInfo ? 1f : 0f;
        initialJudgeText = showJudgeText ? 1f : 0f;
        initialInnerBrightness = Mathf.Clamp01(innerBrightness);
        initialOuterBrightness = Mathf.Clamp01(outerBrightness);
        initialComboDisplay = comboDisplay;
        showSongDetailIntro = showSongDetail;

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
        tracks["ShowJudgeArea"] = CreateTrack("ShowJudgeArea", initialJudgeArea);
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
        EnsureSubtitleStyles();
        WarmupSubtitleGlyphs();
        subtitleGuiWarmupPending = subtitles.Count > 0;
        ResetSubtitleCursor();

        playbackActive = true;
        InvalidateAppliedValues();
        ApplyAt(timeProvider != null ? timeProvider.AudioTime : 0f);
        enabled = true;
    }

    public void SetPlaybackActive(bool active)
    {
        if (active && !HasRequiredTracks())
        {
            playbackActive = false;
            enabled = false;
            return;
        }

        playbackActive = active;
        if (active)
        {
            SetJudgeLinePlaybackScale();
            enabled = true;
            ApplyAt(timeProvider != null ? timeProvider.AudioTime : 0f);
        }
        else
            RestoreStandby();
    }

    public void PausePlayback()
    {
        playbackActive = false;
        enabled = false;
    }

    public void SetPausedTimelineTime(float time)
    {
        playbackActive = false;
        SetJudgeLinePlaybackScale();
        InvalidateAppliedValues();
        ApplyAt(time);
        enabled = false;
    }

    public void SetImmediateDisplay(
        bool showJudgeLine,
        bool showJudgeInfo,
        bool showComboInfo,
        bool showJudgeText,
        float innerBrightness,
        float outerBrightness,
        bool showJudgeArea = false)
    {
        var now = timeProvider != null ? timeProvider.AudioTime : 0f;
        var judgeLineTarget = showJudgeLine ? 1f : 0f;
        var judgeAreaTarget = showJudgeArea ? 1f : 0f;
        initialJudgeLine = showJudgeLine ? 1f : 0f;
        initialJudgeArea = showJudgeArea ? 1f : 0f;
        initialJudgeInfo = showJudgeInfo ? 1f : 0f;
        initialComboInfo = showComboInfo ? 1f : 0f;
        initialJudgeText = showJudgeText ? 1f : 0f;
        initialInnerBrightness = Mathf.Clamp01(innerBrightness);
        initialOuterBrightness = Mathf.Clamp01(outerBrightness);
        if (tracks.TryGetValue("ShowJudgeLine", out var judgeLineTrack))
            judgeLineTrack.TransitionTo(now, judgeLineTarget, 0.25f);
        else
            tracks["ShowJudgeLine"] = CreateTrack("ShowJudgeLine", initialJudgeLine);
        if (tracks.TryGetValue("ShowJudgeArea", out var judgeAreaTrack))
            judgeAreaTrack.TransitionTo(now, judgeAreaTarget, 0.25f);
        else
            tracks["ShowJudgeArea"] = CreateTrack("ShowJudgeArea", initialJudgeArea);
        tracks["ShowJudgeInfo"] = CreateTrack("ShowJudgeInfo", initialJudgeInfo);
        tracks["ShowComboInfo"] = CreateTrack("ShowComboInfo", initialComboInfo);
        tracks["ShowJudgeText"] = CreateTrack("ShowJudgeText", initialJudgeText);
        tracks["InnerBrightness"] = CreateTrack("InnerBrightness", initialInnerBrightness);
        tracks["OuterBrightness"] = CreateTrack("OuterBrightness", initialOuterBrightness);
        InvalidateAppliedValues();
        if (playbackActive)
            ApplyAt(now);
        else
            RestoreStandby();
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

        EnsureSubtitleStyles();
        if (subtitleGuiWarmupPending)
        {
            WarmupSubtitleGui();
            subtitleGuiWarmupPending = false;
        }
        var time = timeProvider.AudioTime;
        AdvanceSubtitleCursor(time);
        foreach (var subtitle in activeSubtitles.Values)
        {
            if (subtitle == null || string.IsNullOrEmpty(subtitle.text) ||
                subtitle.duration >= 0f && time > subtitle.time + subtitle.duration)
                continue;
            DrawSubtitle(subtitle, time);
        }
    }

    private void DrawSubtitle(SubtitleChange subtitle, float time)
    {
        // A caption asking for nothing keeps the corner and the size captions have
        // always had; x and y move it by a fraction of the screen so the same
        // chart lands in the same place whatever the window size is.
        var size = subtitle.size > 0f ? Mathf.RoundToInt(subtitle.size) : 32;
        var font = ResolveSubtitleFont(subtitle.font);
        var fontStyle = UsesDefaultSubtitleFont(subtitle.font)
            ? FontStyle.Bold
            : FontStyle.Normal;
        subtitleStyle.font = font;
        subtitleShadowStyle.font = font;
        subtitleStyle.fontSize = size;
        subtitleShadowStyle.fontSize = size;
        subtitleStyle.fontStyle = fontStyle;
        subtitleShadowStyle.fontStyle = fontStyle;
        var elapsed = Mathf.Max(0f, time - (float)subtitle.time);
        var transition = Mathf.Max(0f, subtitle.transition);
        var typewriter = string.Equals(
            subtitle.style, "Typewriter", StringComparison.OrdinalIgnoreCase);
        var alpha = !typewriter && transition > 0f
            ? Mathf.Clamp01(elapsed / transition)
            : 1f;
        var text = typewriter
            ? GetTypewriterText(subtitle.text, elapsed, transition)
            : subtitle.text;
        if (text.Length == 0)
            return;
        subtitleStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
        subtitleShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f * alpha);
        var left = 28f + Mathf.Clamp01(subtitle.x) * Screen.width;
        var top = 22f + Mathf.Clamp01(subtitle.y) * Screen.height;
        var rect = new Rect(
            left,
            top,
            Mathf.Max(size * 2f, Screen.width - 28f - left),
            Mathf.Max(size * 2f, Screen.height - 22f - top));
        var shadowRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height);
        GUI.Label(shadowRect, text, subtitleShadowStyle);
        GUI.Label(rect, text, subtitleStyle);
    }

    private void AdvanceSubtitleCursor(float time)
    {
        if (time < lastSubtitleTime)
            ResetSubtitleCursor();
        lastSubtitleTime = time;

        while (subtitleCursor + 1 < subtitles.Count &&
               subtitles[subtitleCursor + 1].time <= time)
        {
            subtitleCursor++;
            var subtitle = subtitles[subtitleCursor];
            activeSubtitles[subtitle.index] = subtitle;
        }
    }

    private static string GetTypewriterText(
        string text, float elapsed, float transition)
    {
        if (transition <= 0f || elapsed >= transition)
            return text;
        var elements = StringInfo.ParseCombiningCharacters(text);
        if (elements.Length == 0)
            return string.Empty;
        var count = Mathf.Clamp(
            Mathf.CeilToInt(elements.Length * elapsed / transition),
            0,
            elements.Length);
        return count == 0
            ? string.Empty
            : text.Substring(
                0,
                count == elements.Length ? text.Length : elements[count]);
    }

    private void EnsureSubtitleStyles()
    {
        if (subtitleStyle != null)
            return;

        if (subtitleFont == null)
        {
            ownedSubtitleFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft JhengHei", "Arial" }, 32);
            subtitleFont = ownedSubtitleFont;
        }
        subtitleStyle = new GUIStyle
        {
            font = subtitleFont,
            fontSize = 32,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        subtitleStyle.normal.textColor = Color.white;
        subtitleShadowStyle = new GUIStyle(subtitleStyle);
        subtitleShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
    }

    private Font ResolveSubtitleFont(string requested)
    {
        if (UsesDefaultSubtitleFont(requested))
            return subtitleFont;

        if (subtitleFontCache.TryGetValue(requested, out var cached) && cached != null)
            return cached;

        var font = requested switch
        {
            "CascadiaMono" => CreateSubtitleSystemFont(
                "Cascadia Mono", "JetBrains Mono", "Cascadia Code", "Consolas"),
            "CascadiaCode" => CreateSubtitleSystemFont("Cascadia Code", "Consolas"),
            "MicrosoftYaHei" => CreateSubtitleSystemFont(
                "Microsoft YaHei UI", "Microsoft YaHei"),
            "NotoSansSC" => Resources.Load<Font>("Fonts/NotoSansSC-VF"),
            "SimSun" => CreateSubtitleSystemFont("NSimSun", "SimSun"),
            "DengXian" => CreateSubtitleSystemFont(
                "DengXian", "Microsoft YaHei UI"),
            "NotoSerifSC" => CreateSubtitleSystemFont("Noto Serif SC", "SimSun"),
            "GlobalMonospace" => CreateSubtitleSystemFont(
                "Global Monospace", "Consolas"),
            "Aileron" => Resources.Load<Font>("Fonts/Aileron-Regular"),
            "Allerta" => Resources.Load<Font>("Fonts/Allerta-Regular"),
            _ => null
        };
        font ??= subtitleFont;
        subtitleFontCache[requested] = font;
        return font;
    }

    private static bool UsesDefaultSubtitleFont(string requested) =>
        string.IsNullOrWhiteSpace(requested) ||
        string.Equals(requested, "Default", StringComparison.OrdinalIgnoreCase);

    private Font CreateSubtitleSystemFont(params string[] names)
    {
        var font = Font.CreateDynamicFontFromOSFont(names, 32);
        if (font != null)
            ownedSubtitleFonts.Add(font);
        return font;
    }

    private void OnDestroy()
    {
        if (ownedSubtitleFont != null)
            Destroy(ownedSubtitleFont);
        foreach (var font in ownedSubtitleFonts)
            if (font != null)
                Destroy(font);
        ownedSubtitleFonts.Clear();
    }

    private void WarmupSubtitleGlyphs()
    {
        EnsureSubtitleStyles();
        if (subtitleFont == null || subtitles.Count == 0)
            return;

        foreach (var group in subtitles.GroupBy(item => new
                 {
                     Font = item.font ?? string.Empty,
                     Size = item.size > 0f ? Mathf.RoundToInt(item.size) : 32,
                     Style = UsesDefaultSubtitleFont(item.font)
                         ? FontStyle.Bold
                         : FontStyle.Normal
                 }))
        {
            var font = ResolveSubtitleFont(group.Key.Font);
            if (font == null)
                continue;
            var characters = string.Concat(group.Select(item => item.text));
            var key = font.GetInstanceID() + ":" + group.Key.Size + ":" + characters;
            if (!warmedSubtitleGlyphs.Add(key))
                continue;
            font.RequestCharactersInTexture(
                characters,
                group.Key.Size,
                group.Key.Style);
        }
    }

    private void WarmupSubtitleGui()
    {
        foreach (var group in subtitles.GroupBy(item => new
                 {
                     Font = item.font ?? string.Empty,
                     Size = item.size > 0f ? Mathf.RoundToInt(item.size) : 32,
                     Style = UsesDefaultSubtitleFont(item.font)
                         ? FontStyle.Bold
                         : FontStyle.Normal
                 }))
        {
            var text = string.Concat(group.Select(item => item.text));
            if (text.Length == 0)
                continue;
            var style = new GUIStyle(subtitleStyle)
            {
                font = ResolveSubtitleFont(group.Key.Font),
                fontSize = group.Key.Size,
                fontStyle = group.Key.Style,
                wordWrap = false
            };
            style.normal.textColor = Color.clear;
            style.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(-32768f, -32768f, 1f, 1f), text, style);
        }
    }

    private void ApplyAt(float time)
    {
        if (!HasRequiredTracks())
            return;

        if (judgeLines.Length == 0 || judgeLines[0] == null)
            RefreshJudgeLineReference();
        var gameplayVisible = !showSongDetailIntro || time >= IntroGameplayRevealTime;
        var judgeLine = gameplayVisible ? tracks["ShowJudgeLine"].Evaluate(time) : 0f;
        var judgeAreaRaw = !gameplayVisible || !tracks.TryGetValue("ShowJudgeArea", out var jaTrack)
            ? 0f
            : jaTrack.Evaluate(time);
        var judgeAreaVal = Mathf.Min(judgeAreaRaw, judgeLine);
        var judgeInfo = tracks["ShowJudgeInfo"].Evaluate(time);
        var comboInfo = tracks["ShowComboInfo"].Evaluate(time);
        var judgeText = tracks["ShowJudgeText"].Evaluate(time);
        var inner = tracks["InnerBrightness"].Evaluate(time);
        var outer = tracks["OuterBrightness"].Evaluate(time);
        var hasComboDisplayEvents = eventsByProperty.TryGetValue("ComboDisplay", out var comboDisplayEvents) &&
                                    comboDisplayEvents.Count > 0;
        var comboDisplay = hasComboDisplayEvents ? EvaluateComboDisplay(time, comboDisplayEvents) : lastComboDisplay;

        var judgeLineTint = hasJudgeLineColors
            ? EvaluateJudgeLineColor(time, GetOriginalJudgeLineColor())
            : (Color?)null;
        var judgeLineChanged = !Mathf.Approximately(judgeLine, lastJudgeLine);
        var judgeLineTintChanged = hasJudgeLineColors &&
                                   (!judgeLineTintInitialized || !NullableColorEquals(judgeLineTint, lastJudgeLineTint));
        var judgeLineTintDrifted = hasJudgeLineColors && judgeLineTint.HasValue &&
                                   IsJudgeLineTintDrifted(judgeLineTint.Value);
        if (judgeLineChanged || judgeLineTintChanged || judgeLineTintDrifted)
        {
            foreach (var renderer in judgeLines)
            {
                if (renderer == null)
                    continue;
                renderer.enabled = true;
                var color = renderer.color;
                if (hasJudgeLineColors)
                {
                    var rgb = judgeLineTint ??
                              (originalJudgeLineRgb.TryGetValue(renderer, out var orig) ? orig : Color.white);
                    color.r = rgb.r;
                    color.g = rgb.g;
                    color.b = rgb.b;
                }
                color.a = judgeLine;
                renderer.color = color;
                renderer.forceRenderingOff = judgeLine <= 0.001f;
            }
            lastJudgeLineTint = judgeLineTint;
            judgeLineTintInitialized = true;
        }
        lastJudgeLine = judgeLine;

        if (judgeArea != null && !Mathf.Approximately(judgeAreaVal, lastJudgeArea))
        {
            var ac = judgeArea.color;
            ac.a = judgeAreaVal;
            judgeArea.color = ac;
            judgeArea.forceRenderingOff = judgeAreaVal <= 0.001f;
            lastJudgeArea = judgeAreaVal;
        }

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

    private Color EvaluateJudgeLineColor(float time, Color fallback)
    {
        var low = 0;
        var high = judgeLineColors.Count - 1;
        var resultIndex = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (judgeLineColors[middle].Time <= time)
            {
                resultIndex = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return resultIndex >= 0 ? judgeLineColors[resultIndex].Evaluate(time) : fallback;
    }

    private Color GetOriginalJudgeLineColor()
    {
        foreach (var renderer in judgeLines)
            if (renderer != null && originalJudgeLineRgb.TryGetValue(renderer, out var color))
                return color;
        return Color.white;
    }

    private static bool NullableColorEquals(Color? left, Color? right) =>
        left.HasValue == right.HasValue &&
        (!left.HasValue || left.Value.Equals(right.Value));

    private static bool RgbApproximately(Color left, Color right) =>
        Mathf.Approximately(left.r, right.r) &&
        Mathf.Approximately(left.g, right.g) &&
        Mathf.Approximately(left.b, right.b);

    private bool IsJudgeLineTintDrifted(Color target)
    {
        foreach (var renderer in judgeLines)
            if (renderer != null && !RgbApproximately(renderer.color, target))
                return true;
        return false;
    }

    private int EvaluateComboDisplay(float time, List<DisplayChange> comboEvents)
    {
        var value = initialComboDisplay;
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

    // Keep the overlay on the outline's parent so both sprites share the same scene transform.
    private void EnsureJudgeArea(GameObject outlineObject)
    {
        if (outlineObject == null)
            return;
        var skin = outlineObject.GetComponent<CustomSkin>();
        var sprite = skin != null ? skin.JudgeArea : null;

        if (judgeArea == null)
        {
            var existing = outlineObject.transform.Find("JudgeAreaOverlay");
            if (existing == null)
                existing = GameObject.Find("JudgeAreaOverlay")?.transform;
            if (existing != null)
                judgeArea = existing.GetComponent<SpriteRenderer>();
        }
        if (judgeArea == null)
        {
            var go = new GameObject("JudgeAreaOverlay");
            judgeArea = go.AddComponent<SpriteRenderer>();
        }
        judgeArea.transform.SetParent(outlineObject.transform.parent, false);
        judgeArea.transform.localPosition = outlineObject.transform.localPosition;
        judgeArea.transform.localRotation = outlineObject.transform.localRotation;
        judgeArea.transform.localScale = Vector3.one;

        var outlineRenderer = outlineObject.GetComponent<SpriteRenderer>();
        if (outlineRenderer != null)
        {
            judgeArea.sortingLayerID = outlineRenderer.sortingLayerID;
            judgeArea.sortingOrder = outlineRenderer.sortingOrder - 1;
        }
        judgeArea.sprite = sprite;
        var c = judgeArea.color;
        c.a = 0f;
        judgeArea.color = c;
        judgeArea.forceRenderingOff = true;
        lastJudgeArea = float.NaN;
    }

    private void SetJudgeLinePlaybackScale()
    {
        if (judgeLineTransform != null && originalJudgeLineScales.TryGetValue(judgeLineTransform, out var scale))
            judgeLineTransform.localScale = scale;
    }

    private void RefreshJudgeLineReference()
    {
        var outlineObject = GameObject.Find("Outline");
        var renderer = outlineObject != null
            ? outlineObject.GetComponent<SpriteRenderer>()
            : null;
        if (renderer == null)
            return;
        if (!originalJudgeLineRgb.ContainsKey(renderer))
            originalJudgeLineRgb[renderer] = renderer.color;
        if (judgeLines.Length == 1 && judgeLines[0] == renderer)
            return;

        judgeLines = new[] { renderer };
        judgeLineTransform = renderer.transform;
        originalJudgeLineScales[judgeLineTransform] = Vector3.one;
        lastJudgeLine = float.NaN;
        judgeLineTintInitialized = false;
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
        SetJudgeLinePlaybackScale();
        foreach (var renderer in judgeLines)
        {
            if (renderer == null)
                continue;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            var color = renderer.color;
            if (originalJudgeLineRgb.TryGetValue(renderer, out var original))
            {
                color.r = original.r;
                color.g = original.g;
                color.b = original.b;
            }
            color.a = 1f;
            renderer.color = color;
        }

        if (judgeArea != null)
        {
            var ac = judgeArea.color;
            ac.a = 0f;
            judgeArea.color = ac;
            judgeArea.forceRenderingOff = true;
        }

        objectCounter?.SetSideDisplays(false, false);
        objectCounter?.ComboSetActive((EditorComboIndicator)0);
        bgManager?.SetCoverAlpha(initialInnerBrightness, initialOuterBrightness);
        noteEffectManager?.ApplyJudgeTextAlpha(initialJudgeText);
        InvalidateAppliedValues();
    }

    private bool HasRequiredTracks()
    {
        return tracks.ContainsKey("ShowJudgeLine") &&
               tracks.ContainsKey("ShowJudgeInfo") &&
               tracks.ContainsKey("ShowComboInfo") &&
               tracks.ContainsKey("ShowJudgeText") &&
               tracks.ContainsKey("InnerBrightness") &&
               tracks.ContainsKey("OuterBrightness");
    }

    private void InvalidateAppliedValues()
    {
        lastJudgeLine = float.NaN;
        lastJudgeArea = float.NaN;
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
        activeSubtitles.Clear();
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

    private sealed class JudgeLineColorSegment
    {
        private readonly float duration;
        private readonly Color from;
        private readonly Color target;

        public JudgeLineColorSegment(float time, float duration, Color from, Color target)
        {
            Time = time;
            this.duration = Mathf.Max(0f, duration);
            this.from = from;
            this.target = target;
        }

        public float Time { get; }

        public Color Evaluate(float time)
        {
            if (duration <= 0f)
                return target;
            return Color.Lerp(from, target, Mathf.Clamp01((time - Time) / duration));
        }
    }
}
