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

    private sealed class SharedTextureEntry
    {
        internal string Path = "";
        internal long LastWriteTicks;
        internal long Length;
        internal Texture2D Texture;
    }

    // Stop reloads the gameplay scene. Keep the two small baked card textures alive
    // across that reload so replaying the same chart does not decode PNGs again.
    private static readonly SharedTextureEntry SharedBaseTexture = new();
    private static readonly SharedTextureEntry SharedOverlayTexture = new();

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
    private Texture2D cachedOverlayTexture;
    private RawImage cacheOverlayImage;
    private bool originalCaptured;
    private UiState cardState;
    private UiState jacketState;
    private UiState diffState;
    private UiState levelState;
    private UiState titleState;
    private UiState artistState;
    private UiState designerState;
    private string activeTemplateKey = "";
    private RectTransform titleMarqueeViewport;
    private float titleMarqueeStartX;
    private float titleMarqueeOverflow;
    private float titleMarqueeClock;

    internal bool IsMasterTemplate(Majson data)
    {
        // The new card cache baked by Edit from extracted assets covers every difficulty
        return data != null &&
               data.songDetailStyle == 1 &&
               GetCacheStem(data.diffNum) != null;
    }

    // Same name and mapping as GetSongDetailCacheStem in Edit
    private static string GetCacheStem(int difficulty) => difficulty switch
    {
        0 => "songdetail_easy",
        1 => "songdetail_basic",
        2 => "songdetail_advanced",
        3 => "songdetail_expert",
        4 => "songdetail_master",
        5 => "songdetail_remaster",
        6 => "songdetail_original",
        _ => null
    };

    internal bool ApplyMaster(
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
        LoadFonts();
        if (designerText != null && regularFont != null)
            designerText.font = regularFont;
        CaptureOriginal();

        if (TryApplyCachedCard(data))
            return true;

        // Do not use the old runtime-composited fallback: it does not match the
        // baked card visually. Missing cache should fall back to the original
        // song-detail scene, while Edit fixes/recreates the cache for next play.
        ResetOriginal();
        return false;
    }

    // Show maximum dx score (note count * 3) right of DXSCORE and above the stars.
    // This is View's fallback path. Normally Edit bakes the value into songdetail_master.png
    // with GDI+ and uses the cache. Match that layout: larger left part, white, bottom-aligned.
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
        // Place the larger "count*3/" at DXSCORE's right edge and the slightly smaller value after one space.
        // Match Edit's GDI+ coordinates: left≈120, bottom≈540, right value≈172, ending above the fifth star.
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
        text.font = regularFont; // Same font as the chart designer
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

    // Uses the same counting rules as Edit.CountTotalNotes and View.JsonDataLoader.CountNoteSum
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
                        total++; // Star head
                    total++;     // Slide body
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

        var stem = GetCacheStem(data.diffNum);
        if (stem == null)
            return false;
        var cachePath = Path.Combine(chartDir, stem + ".png");
        if (!File.Exists(cachePath))
            return false;

        cachedTexture = LoadSharedTexture(cachePath, SharedBaseTexture);
        if (cachedTexture == null)
            return false;
        foreach (var layer in templateLayers)
            layer.gameObject.SetActive(false);
        foreach (var obj in runtimeObjects)
            if (obj != null)
                obj.SetActive(false);

        cardImage.texture = cachedTexture;
        cardImage.color = Color.white;
        SetRect(cardImage.rectTransform, Vector2.zero, new Vector2(CanvasWidth, CanvasHeight));

        // The new cache does not bake the jacket, placing the live jacket between the card base
        // and LV overlay for layered intro animation. Legacy single-image caches still hide it.
        var overlayPath = Path.Combine(chartDir, stem + "_overlay.png");
        if (File.Exists(overlayPath) && TryLoadCacheOverlay(overlayPath))
        {
            ApplyJacketForCache();
            if (cacheOverlayImage != null)
                cacheOverlayImage.transform.SetAsLastSibling();
        }
        else
        {
            if (jacketImage != null)
                jacketImage.gameObject.SetActive(false);
            if (cacheOverlayImage != null)
                cacheOverlayImage.gameObject.SetActive(false);
        }
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
        // The cached base image contains the title. Do not recreate it with a live
        // Unity Text: its metrics differ from the GDI+ preview and it adds layout work
        // on the first intro frame.
        titleMarqueeViewport = null;
        titleMarqueeOverflow = 0f;
        return true;
    }

    private void Update()
    {
        UpdateCacheOverlayIntro();

        if (titleMarqueeViewport == null || titleText == null || titleMarqueeOverflow <= 0f)
            return;

        const float delay = 0.8f;
        const float endHold = 1.2f;
        const float speed = 4f;
        titleMarqueeClock += Time.unscaledDeltaTime;
        var travelTime = titleMarqueeOverflow / speed;
        var cycle = delay + travelTime + endHold;
        var phase = titleMarqueeClock % cycle;
        var offset = phase <= delay ? 0f
            : phase < delay + travelTime ? (phase - delay) * speed
            : titleMarqueeOverflow;
        var position = titleText.rectTransform.anchoredPosition;
        position.x = titleMarqueeStartX - offset;
        titleText.rectTransform.anchoredPosition = position;
    }

    private void ConfigureCachedTitle(string value)
    {
        if (titleText == null || cardImage == null)
            return;

        if (titleMarqueeViewport == null)
        {
            var viewportObject = new GameObject("TitleMarqueeViewport",
                typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(cardImage.rectTransform, false);
            titleMarqueeViewport = viewportObject.GetComponent<RectTransform>();
            runtimeObjects.Add(viewportObject);
        }

        titleMarqueeViewport.gameObject.SetActive(true);
        SetRect(titleMarqueeViewport, FromPixelCenter(170.5f, 424f), FromPixelSize(325f, 42f));
        titleText.transform.SetParent(titleMarqueeViewport, false);
        titleText.gameObject.SetActive(true);
        titleText.text = value ?? "";
        titleText.font = bodyFont;
        titleText.fontStyle = FontStyle.Bold;
        titleText.fontSize = 7;
        titleText.color = Color.white;
        titleText.alignment = TextAnchor.MiddleLeft;
        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
        titleText.verticalOverflow = VerticalWrapMode.Truncate;
        titleText.resizeTextForBestFit = false;
        titleText.raycastTarget = false;
        Canvas.ForceUpdateCanvases();

        var viewportWidth = titleMarqueeViewport.rect.width;
        var textWidth = Mathf.Max(viewportWidth, titleText.preferredWidth);
        titleMarqueeOverflow = Mathf.Max(0f, textWidth - viewportWidth);
        titleMarqueeStartX = titleMarqueeOverflow * 0.5f;
        titleMarqueeClock = 0f;
        SetRect(titleText.rectTransform, new Vector2(titleMarqueeStartX, 0f),
            new Vector2(textWidth, titleMarqueeViewport.rect.height));
        titleText.transform.SetAsLastSibling();
    }

    private bool TryLoadCacheOverlay(string overlayPath)
    {
        var texture = LoadSharedTexture(overlayPath, SharedOverlayTexture);
        if (texture == null)
            return false;

        if (cacheOverlayImage == null)
        {
            var obj = new GameObject("CacheOverlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            obj.transform.SetParent(cardImage.rectTransform, false);
            cacheOverlayImage = obj.GetComponent<RawImage>();
            cacheOverlayImage.raycastTarget = false;
            SetRect(cacheOverlayImage.rectTransform, Vector2.zero, new Vector2(CanvasWidth, CanvasHeight));
        }

        cachedOverlayTexture = texture;
        cacheOverlayImage.texture = texture;
        cacheOverlayImage.gameObject.SetActive(true);
        ApplyCacheOverlayReveal(0f);
        return true;
    }

    private void UpdateCacheOverlayIntro()
    {
        if (cacheOverlayImage == null || !cacheOverlayImage.gameObject.activeSelf)
            return;
        SyncCacheOverlayOpacity();
    }

    public void SampleCacheOverlayIntro(float timelineTime)
    {
        // Entry.anim already owns the card's original movement and fade. The cache
        // overlay is a child of that card, so it must only copy the animated base
        // opacity; scaling it here introduced an unintended horizontal fly-in.
        SyncCacheOverlayOpacity();
    }

    private void SyncCacheOverlayOpacity()
    {
        ApplyCacheOverlayReveal(cardImage != null ? cardImage.color.a : 0f);
    }

    private void ApplyCacheOverlayReveal(float progress)
    {
        if (cacheOverlayImage == null)
            return;
        progress = Mathf.Clamp01(progress);
        cacheOverlayImage.color = new Color(1f, 1f, 1f, progress);
        cacheOverlayImage.rectTransform.localScale = Vector3.one;
    }

    private static Texture2D LoadSharedTexture(string path, SharedTextureEntry entry)
    {
        try
        {
            var info = new FileInfo(path);
            if (entry.Texture != null &&
                string.Equals(entry.Path, path, System.StringComparison.OrdinalIgnoreCase) &&
                entry.LastWriteTicks == info.LastWriteTimeUtc.Ticks &&
                entry.Length == info.Length)
                return entry.Texture;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Destroy(texture);
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (entry.Texture != null)
                Destroy(entry.Texture);
            entry.Path = path;
            entry.LastWriteTicks = info.LastWriteTimeUtc.Ticks;
            entry.Length = info.Length;
            entry.Texture = texture;
            return texture;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("Unable to load baked song detail texture: " + exception.Message);
            return null;
        }
    }

    // Position the live jacket using the extracted card window, approximately (42,102)-(300,362)
    // on the 341x588 canvas. Layer order: card base < jacket < CacheOverlay with LV and level.
    private void ApplyJacketForCache()
    {
        if (jacketImage == null || cardImage == null)
            return;
        jacketImage.gameObject.SetActive(true);
        jacketImage.transform.SetParent(cardImage.rectTransform, false);
        // Fill the black window of the uncropped body image exactly.
        SetRect(jacketImage.rectTransform, FromPixelCenter(171f, 227f), FromPixelSize(260f, 262f));
        jacketImage.color = Color.white;
        if (jacketImage.texture != null)
        {
            jacketImage.texture.wrapMode = TextureWrapMode.Clamp;
            jacketImage.texture.filterMode = FilterMode.Bilinear;
        }
        jacketImage.transform.SetAsLastSibling();
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
        if (cacheOverlayImage != null)
        {
            ApplyCacheOverlayReveal(0f);
            cacheOverlayImage.gameObject.SetActive(false);
        }

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
                         Font.CreateDynamicFontFromOSFont(
                             new[] { "Segoe UI", "Microsoft YaHei UI", "Arial" }, 32) ??
                         bodyFont;
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
