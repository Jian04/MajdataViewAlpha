using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed class ViewFpsDisplay : MonoBehaviour
{
    private Text label;
    private ObjectCounter counter;
    private AudioTimeProvider clock;
    private JsonDataLoader loader;
    private Canvas infoCanvas;
    private Text timeText;
    private static int mode;
    private static string lastFpsText = "";
    private float elapsed;
    private int frames;
    private BeatDisplayPoint displayedPoint;
    private FileFontText fileFontText;

    public void SetMode(int displayMode, JsonDataLoader dataLoader)
    {
        loader = dataLoader;
        displayedPoint = null;
        var nextMode = Mathf.Clamp(displayMode, 0, 4);
        if (mode == nextMode) return;
        mode = nextMode;
        frames = 0;
        elapsed = 0f;
        if (label != null) label.text = mode == 1 ? lastFpsText : "";
    }

    private void Start()
    {
        clock = GetComponent<AudioTimeProvider>();
        counter = FindAnyObjectByType<ObjectCounter>();
        var infoObject = GameObject.Find("CanvasInfo");
        infoCanvas = infoObject.GetComponent<Canvas>();
        var textObject = new GameObject("FPS Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(infoObject.transform, false);
        label = textObject.GetComponent<Text>();
        label.enabled = mode != 0;
        label.text = mode == 1 ? lastFpsText : "";
        label.fontSize = 2;
        label.color = Color.white;
        label.alignment = TextAnchor.UpperLeft;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(2, -2);
        rect.sizeDelta = new Vector2(40, 5);
        foreach (var text in infoObject.GetComponentsInChildren<Text>(true))
        {
            if (text.name == "TimeText") timeText = text;
        }
        label.font = timeText.font;
        Canvas.willRenderCanvases += AlignToReference;
    }

    private void OnDestroy() => Canvas.willRenderCanvases -= AlignToReference;

    private void AlignToReference()
    {
        if (infoCanvas == null || label == null || timeText == null) return;
        var viewport = infoCanvas.pixelRect;
        label.fontSize = timeText.fontSize;
        var parent = (RectTransform)infoCanvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,
            new Vector2(viewport.xMin + 8, viewport.yMax - 8), infoCanvas.worldCamera, out var position);
        label.rectTransform.anchoredPosition = position - new Vector2(parent.rect.xMin, parent.rect.yMax);
    }

    private void LateUpdate()
    {
        var fileFont = counter != null ? counter.DisplayFileFont : null;
        if (fileFont != null && fileFontText == null) fileFontText = label.gameObject.AddComponent<FileFontText>();
        if (fileFontText != null)
        {
            if (fileFontText.FontAsset != fileFont) fileFontText.Use(fileFont);
            fileFontText.SetVisible(mode != 0);
        }
        else label.enabled = mode != 0;
        var font = counter?.DisplayFont;
        if (font != null && label.font != font)
            label.font = font;
        if (mode == 0) return;
        if (mode != 1)
        {
            var points = loader?.BeatDisplayTable;
            var point = FindBeatPoint(points, clock.AudioTime);
            if (point == displayedPoint && !string.IsNullOrEmpty(label.text)) return;
            displayedPoint = point;
            var content = mode switch
            {
                2 => $"BPM: {point?.bpm.ToString("0.##", CultureInfo.InvariantCulture) ?? "--"}",
                3 => $"{point?.meterNumerator ?? 4}/{point?.meterDenominator ?? 4}",
                4 => $"{point?.subdivision ?? 4}",
                _ => ""
            };
            if (label.text != content) label.text = content;
            return;
        }
        frames++;
        elapsed += Time.unscaledDeltaTime;
        if (string.IsNullOrEmpty(label.text) && elapsed > 0f)
            label.text = $"{frames / elapsed:F0} FPS";
        if (elapsed < 0.5f) return;
        label.text = $"{frames / elapsed:F0} FPS";
        lastFpsText = label.text;
        frames = 0;
        elapsed = 0;
    }

    private static BeatDisplayPoint FindBeatPoint(IReadOnlyList<BeatDisplayPoint> points, double time)
    {
        if (points == null || points.Count == 0) return null;
        var left = 0;
        var right = points.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (points[middle].time <= time) left = middle + 1;
            else right = middle;
        }
        return points[Mathf.Max(0, left - 1)];
    }
}
