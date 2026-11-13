using System;
using System.Collections.Generic;
using MajdataCore;
using TMPro;
using UnityEngine;

public partial class DisplayTimelineController
{
    private static TMP_FontAsset builtinNotoCaptionFont;
    private Canvas fileCaptionCanvas;
    private bool hasFileCaptions;
    private readonly Dictionary<int, TextMeshProUGUI> fileCaptionLabels = new();
    private readonly Dictionary<string, TMP_FontAsset> captionFileFonts = new(StringComparer.OrdinalIgnoreCase);

    private static string CaptionFontFile(string requested) => requested switch
    {
        "NotoSansSC" => "NotoSansSC-VF.ttf",
        "Aileron" => "Aileron-Regular.otf",
        "Allerta" => "Allerta-Regular.ttf",
        _ => FontFiles.IsFontFile(requested) ? requested : null
    };

    private TMP_FontAsset ResolveFileCaptionFont(string requested)
    {
        if (captionFileFonts.TryGetValue(requested, out var font)) return font;
        try
        {
            font = requested == "NotoSansSC"
                ? (builtinNotoCaptionFont ??= TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("Fonts/NotoSansSC-VF")))
                : RuntimeFontFiles.Load(CaptionFontFile(requested), chartDirectory);
        }
        catch (Exception error) { Debug.LogError("[TEXT 字体] " + error.Message); }
        captionFileFonts[requested] = font;
        return font;
    }

    private void OnDisable()
    {
        if (fileCaptionCanvas != null) fileCaptionCanvas.gameObject.SetActive(false);
    }

    private void UpdateFileCaptions(float time)
    {
        if (!hasFileCaptions) return;
        AdvanceSubtitleCursor(time);
        foreach (var pair in fileCaptionLabels)
        {
            var visible = activeSubtitles.TryGetValue(pair.Key, out var item) &&
                CaptionFontFile(item.font) != null &&
                (item.duration < 0f || time <= item.time + item.duration);
            pair.Value.gameObject.SetActive(visible);
        }
        foreach (var subtitle in activeSubtitles.Values)
        {
            if (CaptionFontFile(subtitle.font) == null ||
                subtitle.duration >= 0f && time > subtitle.time + subtitle.duration) continue;
            var font = ResolveFileCaptionFont(subtitle.font);
            if (font == null) continue;
            if (fileCaptionCanvas == null)
            {
                var canvasObject = new GameObject("File captions", typeof(RectTransform), typeof(Canvas));
                canvasObject.transform.SetParent(transform, false);
                fileCaptionCanvas = canvasObject.GetComponent<Canvas>();
                fileCaptionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                fileCaptionCanvas.sortingOrder = 32760;
            }
            fileCaptionCanvas.gameObject.SetActive(true);
            if (!fileCaptionLabels.TryGetValue(subtitle.index, out var label))
            {
                var child = new GameObject("Caption " + subtitle.index, typeof(RectTransform));
                child.transform.SetParent(fileCaptionCanvas.transform, false);
                label = child.AddComponent<TextMeshProUGUI>();
                label.raycastTarget = false;
                label.alignment = TextAlignmentOptions.TopLeft;
                label.textWrappingMode = TextWrappingModes.Normal;
                fileCaptionLabels[subtitle.index] = label;
            }
            label.font = font;
            label.fontSize = subtitle.size > 0f ? subtitle.size : 32f;
            label.alignment = string.Equals(subtitle.origin, "Center", StringComparison.OrdinalIgnoreCase)
                ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            var elapsed = Mathf.Max(0f, time - (float)subtitle.time);
            var typewriter = string.Equals(subtitle.style, "Typewriter", StringComparison.OrdinalIgnoreCase);
            label.text = typewriter ? GetTypewriterText(subtitle.text, elapsed, subtitle.transition) : subtitle.text;
            var alpha = !typewriter && subtitle.transition > 0f ? Mathf.Clamp01(elapsed / subtitle.transition) : 1f;
            var color=subtitleColors[subtitle]; color.a*=alpha;
            label.color = color;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            var placement = GetSubtitleRect(subtitle, label.fontSize, Screen.width, Screen.height);
            rect.anchoredPosition = new Vector2(placement.x, -placement.y);
            rect.sizeDelta = new Vector2(placement.width, placement.height);
        }
    }
}
