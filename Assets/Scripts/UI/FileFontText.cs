using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Keep existing display updates and layout in Text; only file-font drawing uses TMP.
public sealed class FileFontText : MonoBehaviour
{
    private Text source;
    private TextMeshProUGUI label;
    public TMP_FontAsset FontAsset { get; private set; }
    public bool UsesFileFont => FontAsset != null;

    public void Use(TMP_FontAsset font)
    {
        source ??= GetComponent<Text>();
        FontAsset = font;
        if (font == null)
        {
            source.enabled = true;
            if (label != null) label.enabled = false;
            return;
        }
        if (label == null)
        {
            var child = new GameObject("File font", typeof(RectTransform));
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            label = child.AddComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        label.font = font;
        label.enabled = true;
        source.enabled = false;
        Sync();
    }

    public float Measure(string text)
    {
        Sync();
        return label.GetPreferredValues(text).x;
    }

    public void SetVisible(bool visible)
    {
        source.enabled = !UsesFileFont && visible;
        if (label != null) label.enabled = UsesFileFont && visible;
    }

    private void OnEnable() => Canvas.preWillRenderCanvases += Sync;
    private void OnDisable() => Canvas.preWillRenderCanvases -= Sync;

    private void Sync()
    {
        if (!UsesFileFont || !label.enabled) return;
        label.text = source.text;
        label.color = source.color;
        label.fontSize = source.fontSize;
        label.enableAutoSizing = source.resizeTextForBestFit;
        label.fontSizeMin = source.resizeTextMinSize;
        label.fontSizeMax = source.resizeTextMaxSize;
        label.fontStyle = source.fontStyle switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal
        };
        label.alignment = source.alignment switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            _ => TextAlignmentOptions.BottomRight
        };
        label.richText = source.supportRichText;
        label.textWrappingMode = source.horizontalOverflow == HorizontalWrapMode.Wrap
            ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        label.overflowMode = source.verticalOverflow == VerticalWrapMode.Overflow
            ? TextOverflowModes.Overflow : TextOverflowModes.Truncate;
    }
}
