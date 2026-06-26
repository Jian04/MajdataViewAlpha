using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public sealed class SongDetailTemplateView : MonoBehaviour
{
    private const int MasterDifficultyIndex = 4;
    private const int ReMasterDifficultyIndex = 5;
    private const float CanvasWidth = 43.5f;
    private const float CanvasHeight = 75f;

    private static readonly string[] DxLayerPaths =
    {
        "SongDetailTemplates/dx/DxBase",
        "SongDetailTemplates/dx/DxOverlay"
    };

    private static readonly string[] ReMasterDxLayerPaths =
    {
        "SongDetailTemplates/dx/DxReMasterBase",
        "SongDetailTemplates/dx/DxReMasterOverlay"
    };

    private readonly List<RawImage> templateLayers = new();
    private readonly List<GameObject> runtimeObjects = new();
    private readonly List<GameObject> legacyHiddenTexts = new();

    private RawImage cardImage;
    private RawImage jacketImage;
    private Text diffText;
    private Text levelText;
    private Text titleText;
    private Text artistText;
    private Text designerText;
    private Text bpmText;
    private Text levelPlusText;
    private Text dxScoreLeftText;
    private Text dxScoreRightText;
    private Font titleFont;
    private Font bodyFont;
    private Font levelFont;
    private Font regularFont;
    private Texture2D cachedTexture;
    private bool originalCaptured;
    private UiState cardState;
    private UiState jacketState;
    private UiState diffState;
    private UiState levelState;
    private UiState titleState;
    private UiState artistState;
    private UiState designerState;
    private string activeTemplateKey = "";

    internal bool IsMasterTemplate(Majson data)
    {
        return data != null &&
               data.songDetailStyle == 1 &&
               (data.diffNum == MasterDifficultyIndex || data.diffNum == ReMasterDifficultyIndex);
    }

    internal void ApplyMaster(
        Majson data,
        RawImage card,
        RawImage jacket,
        Text difficulty,
        Text level,
        Text title,
        Text artist,
        Text designer)
    {
        cardImage = card;
        jacketImage = jacket;
        diffText = difficulty;
        levelText = level;
        titleText = title;
        artistText = artist;
        designerText = designer;
        CaptureOriginal();
        LoadFonts();
        var isReMaster = data.diffNum == ReMasterDifficultyIndex;

        if (TryApplyCachedCard(data))
            return;

        EnsureTemplateLayers(data);

        if (cardImage != null)
        {
            cardImage.texture = null;
            cardImage.color = Color.clear;
            SetRect(cardImage.rectTransform, Vector2.zero, new Vector2(CanvasWidth, CanvasHeight));
        }

        foreach (var layer in templateLayers)
            layer.gameObject.SetActive(true);

        ApplyJacket();
        if (diffText != null)
            diffText.gameObject.SetActive(false);
        HideLegacyCardTexts();

        var levelParts = SplitLevel(CleanLevel(data.level));
        ConfigureLevelText(levelText, levelParts.number,
            FromPixelCenter(306f, 376f), FromPixelSize(86f, 56f), 50, isReMaster);
        ConfigureLevelPlus(levelParts.hasPlus, isReMaster);
        ConfigureText(titleText, data.title, bodyFont,
            FromPixelCenter(170.5f, 429.5f), FromPixelSize(320f, 37f), 5,
            TextAnchor.MiddleCenter, Color.white);
        ConfigureText(artistText, data.artist, bodyFont,
            FromPixelCenter(170.5f, 471f), FromPixelSize(320f, 31f), 4,
            TextAnchor.MiddleCenter, new Color32(235, 241, 255, 255));
        ConfigureText(designerText, data.designer, regularFont,
            FromPixelCenter(118f, 570.5f), FromPixelSize(210f, 19f), 4,
            TextAnchor.MiddleLeft, new Color32(28, 34, 62, 255));

        bpmText ??= CreateText("BPMValue");
        ConfigureText(bpmText, GetBpmText(data), regularFont,
            FromPixelCenter(284f, 570.5f), FromPixelSize(88f, 22f), 4,
            TextAnchor.MiddleRight, new Color32(28, 34, 62, 255));

        ConfigureDxScore(data);
    }

    // DXSCORE 标签右侧、星级正上方显示 dx 满分(物量*3)。这是 View 端的降级渲染路径:
    // 正常播放/录制时 Edit 端会用 GDI+ 把同样的数字烤进 songdetail_master.png 并走缓存,
    // 只有缺少缓存 PNG 时才落到这里,所以采用与 GDI+ 一致的版式(左大右小、底部对齐、白色)。
    private void ConfigureDxScore(Majson data)
    {
        dxScoreLeftText ??= CreateText("DxScoreLeft");
        dxScoreRightText ??= CreateText("DxScoreRight");

        var dxMax = CountTotalNotesForCard(data) * 3;
        if (dxMax <= 0)
        {
            dxScoreLeftText.gameObject.SetActive(false);
            dxScoreRightText.gameObject.SetActive(false);
            return;
        }

        var value = dxMax.ToString();
        // 左 "物量*3/" 靠 DXSCORE 右缘起、较大;右 "物量*3" 紧随其后留一空格、仅小一点;两段底部对齐。
        // 坐标与 Edit 端 GDI+ 版式对齐(左缘≈120、底≈540、右数字左缘≈172、末位落在第五颗星上方)。
        ConfigureDxScoreText(dxScoreLeftText, value + "/",
            FromPixelCenter(143f, 531f), FromPixelSize(48f, 18f), 5, TextAnchor.LowerLeft);
        ConfigureDxScoreText(dxScoreRightText, value,
            FromPixelCenter(191f, 531.5f), FromPixelSize(40f, 17f), 5, TextAnchor.LowerLeft);
    }

    private void ConfigureDxScoreText(Text text, string value, Vector2 position, Vector2 size,
        int maxFontSize, TextAnchor alignment)
    {
        if (text == null || cardImage == null)
            return;

        text.transform.SetParent(cardImage.rectTransform, false);
        text.gameObject.SetActive(true);
        text.text = value ?? "";
        text.font = regularFont; // 与谱师同字体
        text.fontStyle = FontStyle.Normal;
        text.color = Color.white;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(1, maxFontSize - 2);
        text.resizeTextMaxSize = maxFontSize;
        text.raycastTarget = false;
        SetRect(text.rectTransform, position, size);
        text.transform.SetAsLastSibling();
    }

    // 与 Edit 端 CountTotalNotes / View 端 JsonDataLoader.CountNoteSum 同口径。
    private static int CountTotalNotesForCard(Majson data)
    {
        if (data == null || data.timingList == null)
            return 0;
        var total = 0;
        foreach (var timing in data.timingList)
        {
            if (timing.noteList == null)
                continue;
            foreach (var note in timing.noteList)
            {
                if (note.noteType == SimaiNoteType.Slide)
                {
                    if (!note.isSlideNoHead)
                        total++; // star 头
                    total++;     // slide 体
                }
                else
                {
                    total++;
                }
            }
        }
        return total;
    }

    private bool TryApplyCachedCard(Majson data)
    {
        if (cardImage == null || data == null || string.IsNullOrWhiteSpace(data.filePath))
            return false;

        var chartDir = Path.GetDirectoryName(data.filePath);
        if (string.IsNullOrWhiteSpace(chartDir))
            return false;

        var cachePath = Path.Combine(chartDir,
            data.diffNum == ReMasterDifficultyIndex ? "songdetail_remaster.png" : "songdetail_master.png");
        if (!File.Exists(cachePath))
            return false;

        var bytes = File.ReadAllBytes(cachePath);
        cachedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!cachedTexture.LoadImage(bytes))
            return false;

        cachedTexture.wrapMode = TextureWrapMode.Clamp;
        cachedTexture.filterMode = FilterMode.Bilinear;
        foreach (var layer in templateLayers)
            layer.gameObject.SetActive(false);
        foreach (var obj in runtimeObjects)
            if (obj != null)
                obj.SetActive(false);

        cardImage.texture = cachedTexture;
        cardImage.color = Color.white;
        SetRect(cardImage.rectTransform, Vector2.zero, new Vector2(CanvasWidth, CanvasHeight));
        if (jacketImage != null)
            jacketImage.gameObject.SetActive(false);
        if (diffText != null)
            diffText.gameObject.SetActive(false);
        if (levelText != null)
            levelText.gameObject.SetActive(false);
        if (titleText != null)
            titleText.gameObject.SetActive(false);
        if (artistText != null)
            artistText.gameObject.SetActive(false);
        if (designerText != null)
            designerText.gameObject.SetActive(false);
        if (bpmText != null)
            bpmText.gameObject.SetActive(false);
        if (levelPlusText != null)
            levelPlusText.gameObject.SetActive(false);
        HideLegacyCardTexts();
        return true;
    }

    // The cached PNG already bakes every label (title / level / designer / the
    // static "NOTE DESIGN" caption…), so any leftover scene text on the card must
    // be hidden. The card's own texts are wired fields, so we walk the card root
    // directly instead of scanning the whole scene by string content. Crucially
    // this also fixes the intermittent "NOTE DESIGN" flash: the old approach used
    // FindObjectsByType, which skips inactive objects, so when the song-detail
    // panel happened to be inactive at load time the caption was never hidden and
    // re-appeared once the panel animated in. Transform traversal always includes
    // inactive children, making the hide deterministic.
    private void HideLegacyCardTexts()
    {
        legacyHiddenTexts.Clear();
        // Anchor to the designer text's *original* parent (the card's text
        // wrapper). The live parent is unreliable because ConfigureText reparents
        // the managed texts onto the card image during the non-cached layout; the
        // captured state always points at the wrapper that still holds the static
        // captions.
        var cardRoot = designerState.parent != null ? designerState.parent
                     : (designerText != null ? designerText.transform.parent : null);
        if (cardRoot == null)
            return;

        foreach (var text in cardRoot.GetComponentsInChildren<Text>(true))
        {
            if (text == null || IsManagedCardText(text))
                continue;
            // Record the full caption set (even if already hidden) so ResetOriginal
            // can always restore it when the card later falls back to the original.
            text.gameObject.SetActive(false);
            legacyHiddenTexts.Add(text.gameObject);
        }
    }

    // Texts we drive ourselves are restored through Capture/Restore (wired fields)
    // or rebuilt each load (runtime objects); only the untouched scene captions
    // need the explicit hide/restore handled by legacyHiddenTexts.
    private bool IsManagedCardText(Text text)
    {
        return text == diffText || text == levelText || text == titleText ||
               text == artistText || text == designerText || text == bpmText ||
               text == levelPlusText || runtimeObjects.Contains(text.gameObject);
    }

    internal void ResetOriginal()
    {
        if (!originalCaptured)
            return;

        foreach (var layer in templateLayers)
            layer.gameObject.SetActive(false);
        foreach (var obj in runtimeObjects)
            if (obj != null)
                obj.SetActive(false);

        // Re-show any scene captions the cached-card path hid, so the original
        // (non-master / no-cache) card looks complete again.
        foreach (var go in legacyHiddenTexts)
            if (go != null)
                go.SetActive(true);
        legacyHiddenTexts.Clear();

        Restore(cardState);
        Restore(jacketState);
        Restore(diffState);
        Restore(levelState);
        Restore(titleState);
        Restore(artistState);
        Restore(designerState);
    }

    private void CaptureOriginal()
    {
        if (originalCaptured)
            return;
        cardState = Capture(cardImage);
        jacketState = Capture(jacketImage);
        diffState = Capture(diffText);
        levelState = Capture(levelText);
        titleState = Capture(titleText);
        artistState = Capture(artistText);
        designerState = Capture(designerText);
        originalCaptured = true;
    }

    private UiState Capture(Graphic graphic)
    {
        if (graphic == null)
            return default;
        return new UiState
        {
            graphic = graphic,
            parent = graphic.transform.parent,
            siblingIndex = graphic.transform.GetSiblingIndex(),
            active = graphic.gameObject.activeSelf,
            color = graphic.color,
            anchorMin = graphic.rectTransform.anchorMin,
            anchorMax = graphic.rectTransform.anchorMax,
            pivot = graphic.rectTransform.pivot,
            anchoredPosition = graphic.rectTransform.anchoredPosition,
            sizeDelta = graphic.rectTransform.sizeDelta,
            localScale = graphic.rectTransform.localScale,
            localRotation = graphic.rectTransform.localRotation
        };
    }

    private void Restore(UiState state)
    {
        if (state.graphic == null)
            return;
        state.graphic.transform.SetParent(state.parent, false);
        state.graphic.transform.SetSiblingIndex(state.siblingIndex);
        state.graphic.gameObject.SetActive(state.active);
        state.graphic.color = state.color;
        state.graphic.rectTransform.anchorMin = state.anchorMin;
        state.graphic.rectTransform.anchorMax = state.anchorMax;
        state.graphic.rectTransform.pivot = state.pivot;
        state.graphic.rectTransform.anchoredPosition = state.anchoredPosition;
        state.graphic.rectTransform.sizeDelta = state.sizeDelta;
        state.graphic.rectTransform.localScale = state.localScale;
        state.graphic.rectTransform.localRotation = state.localRotation;
    }

    private void LoadFonts()
    {
        titleFont ??= Resources.Load<Font>("Fonts/helveticanowtext-black") ??
                      Font.CreateDynamicFontFromOSFont(new[] { "Arial" }, 32);
        bodyFont ??= Resources.Load<Font>("Fonts/MicrosoftYaHei-Bold") ??
                     Resources.Load<Font>("Fonts/NotoSansSC-VF") ??
                     Resources.Load<Font>("Fonts/GenJyuuGothic-Monospace-Heavy") ??
                     Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" }, 32);
        levelFont ??= Resources.Load<Font>("Fonts/Allerta-Regular") ??
                      Resources.Load<Font>("Fonts/helveticanowtext-black") ?? titleFont;
        regularFont ??= Resources.Load<Font>("Fonts/Aileron-Regular") ??
                         Resources.Load<Font>("Fonts/helveticanowtext-black") ??
                         Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 32);
    }

    private void EnsureTemplateLayers(Majson data)
    {
        if (cardImage == null)
            return;

        var templateKey = data != null && data.diffNum == ReMasterDifficultyIndex ? "remaster" : "master";
        if (templateLayers.Count > 0 && activeTemplateKey == templateKey)
            return;

        foreach (var layer in templateLayers)
            if (layer != null)
                Destroy(layer.gameObject);
        templateLayers.Clear();
        activeTemplateKey = templateKey;

        var parent = cardImage.rectTransform;
        var paths = templateKey == "remaster" ? ReMasterDxLayerPaths : DxLayerPaths;
        foreach (var path in paths)
        {
            var texture = Resources.Load<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning("Song detail template layer missing: " + path);
                continue;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var obj = new GameObject(path.Substring(path.LastIndexOf('/') + 1),
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            obj.transform.SetParent(parent, false);
            var image = obj.GetComponent<RawImage>();
            image.texture = texture;
            image.color = Color.white;
            image.raycastTarget = false;
            SetRect(image.rectTransform, Vector2.zero, new Vector2(CanvasWidth, CanvasHeight));
            templateLayers.Add(image);
        }
    }

    private void ApplyJacket()
    {
        if (jacketImage == null || cardImage == null)
            return;

        jacketImage.transform.SetParent(cardImage.rectTransform, false);
        SetRect(jacketImage.rectTransform, FromPixelCenter(171.5f, 219f), FromPixelSize(283f, 282f));
        jacketImage.color = Color.white;
        if (jacketImage.texture != null)
        {
            jacketImage.texture.wrapMode = TextureWrapMode.Clamp;
            jacketImage.texture.filterMode = FilterMode.Bilinear;
        }
        var overlay = templateLayers.Find(layer => layer != null && layer.name.Contains("Overlay"));
        if (overlay != null)
            jacketImage.transform.SetSiblingIndex(overlay.transform.GetSiblingIndex());
        else
            jacketImage.transform.SetAsLastSibling();
    }

    private void ConfigureText(
        Text text,
        string value,
        Font font,
        Vector2 position,
        Vector2 size,
        int maxFontSize,
        TextAnchor alignment,
        Color color)
    {
        if (text == null || cardImage == null)
            return;

        text.transform.SetParent(cardImage.rectTransform, false);
        text.gameObject.SetActive(true);
        text.text = value ?? "";
        text.font = font;
        text.fontStyle = FontStyle.Normal;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(1, maxFontSize - 3);
        text.resizeTextMaxSize = maxFontSize;
        text.lineSpacing = 0.82f;
        text.raycastTarget = false;
        SetRect(text.rectTransform, position, size);
        text.transform.SetAsLastSibling();
    }

    private static void ApplyLevelEffects(Text text, bool isReMaster)
    {
        if (text == null)
            return;

        var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
        outline.effectColor = isReMaster ? Color.white : new Color32(78, 54, 111, 255);
        outline.effectDistance = new Vector2(0.9f, -0.9f);
        outline.useGraphicAlpha = true;
        text.color = Color.white;

        var shadow = GetExactShadow(text);
        if (shadow != null)
            shadow.enabled = false;

        var gradient = text.GetComponent<GradientTextEffect>() ?? text.gameObject.AddComponent<GradientTextEffect>();
        gradient.enabled = isReMaster;
        if (isReMaster)
        {
            gradient.topColor = new Color32(214, 167, 255, 255);
            gradient.bottomColor = new Color32(92, 32, 150, 255);
        }
    }

    private void ConfigureLevelText(Text text, string value, Vector2 position, Vector2 size, int maxFontSize, bool isReMaster)
    {
        if (text == null || cardImage == null)
            return;

        text.transform.SetParent(cardImage.rectTransform, false);
        text.gameObject.SetActive(true);
        text.text = value ?? "";
        text.font = levelFont;
        text.fontStyle = FontStyle.Normal;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 30;
        text.resizeTextMaxSize = maxFontSize;
        text.raycastTarget = false;
        SetRect(text.rectTransform, position, size);
        text.transform.SetAsLastSibling();
        ApplyLevelEffects(text, isReMaster);
    }

    private void ConfigureLevelPlus(bool hasPlus, bool isReMaster)
    {
        levelPlusText ??= CreateText("LevelPlus");
        if (!hasPlus)
        {
            levelPlusText.gameObject.SetActive(false);
            return;
        }

        ConfigureLevelText(levelPlusText, "+",
            FromPixelCenter(323f, 361f), FromPixelSize(22f, 24f), 24, isReMaster);
    }

    private static Shadow GetExactShadow(Text text)
    {
        var shadows = text.GetComponents<Shadow>();
        for (var i = 0; i < shadows.Length; i++)
        {
            if (shadows[i].GetType() == typeof(Shadow))
                return shadows[i];
        }

        return null;
    }

    private Text CreateText(string name)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        runtimeObjects.Add(obj);
        obj.transform.SetParent(cardImage.rectTransform, false);
        return obj.GetComponent<Text>();
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static Vector2 FromPixelCenter(float x, float y)
    {
        var scale = CanvasHeight / 588f;
        return new Vector2((x - 170.5f) * scale, (294f - y) * scale);
    }

    private static Vector2 FromPixelSize(float width, float height)
    {
        var scale = CanvasHeight / 588f;
        return new Vector2(width * scale, height * scale);
    }

    private static string CleanLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return "";
        level = level.Trim();
        if (level.StartsWith("Lv", System.StringComparison.OrdinalIgnoreCase))
            level = level.Substring(2).Trim();
        return level;
    }

    private static (string number, bool hasPlus) SplitLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return ("", false);

        var trimmed = level.Trim();
        if (!trimmed.EndsWith("+", System.StringComparison.Ordinal))
            return (trimmed, false);

        return (trimmed.Substring(0, trimmed.Length - 1).TrimEnd(), true);
    }

    private static string GetBpmText(Majson data)
    {
        if (!string.IsNullOrWhiteSpace(data.wholeBpm))
            return data.wholeBpm.Trim();
        if (data.timingList != null)
        {
            foreach (var timing in data.timingList)
            {
                if (timing.currentBpm <= 0f)
                    continue;
                return Mathf.Abs(timing.currentBpm - Mathf.Round(timing.currentBpm)) < 0.001f
                    ? Mathf.RoundToInt(timing.currentBpm).ToString()
                    : timing.currentBpm.ToString("0.###");
            }
        }
        return "-";
    }

    private struct UiState
    {
        public Graphic graphic;
        public Transform parent;
        public int siblingIndex;
        public bool active;
        public Color color;
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;
        public Vector3 localScale;
        public Quaternion localRotation;
    }
}
