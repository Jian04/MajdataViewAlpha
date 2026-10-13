using UnityEngine;
using UnityEngine.UI;

public sealed class ViewFpsDisplay : MonoBehaviour
{
    private Text label;
    private ObjectCounter counter;
    private AudioTimeProvider clock;
    private float elapsed;
    private int frames;

    private void Start()
    {
        clock = GetComponent<AudioTimeProvider>();
        counter = FindAnyObjectByType<ObjectCounter>();
        var canvasObject = new GameObject("FPS", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(980, 980);
        var textObject = new GameObject("FPS Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(canvasObject.transform, false);
        label = textObject.GetComponent<Text>();
        label.fontSize = 20;
        label.color = Color.white;
        label.alignment = TextAnchor.UpperLeft;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(12, -10);
        rect.sizeDelta = new Vector2(150, 30);
    }

    private void LateUpdate()
    {
        label.enabled = !clock.isRecord && !AlphaInstanceBadge.Recording;
        if (counter != null && label.font != counter.DisplayFont)
            label.font = counter.DisplayFont;
        frames++;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < 0.5f) return;
        label.text = $"{frames / elapsed:F0} FPS";
        frames = 0;
        elapsed = 0;
    }
}
