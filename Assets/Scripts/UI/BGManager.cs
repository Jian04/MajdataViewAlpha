using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class BGManager : MonoBehaviour
{
    private float playSpeed;
    private AudioTimeProvider provider;

    private RawImage rawImage;

    private GameObject SongDetail;
    private SongDetailIntroBg songDetailIntroBg;
    private SongDetailTemplateView songDetailTemplate;
    private GameObject circleRev;
    private GameObject canvasInfo;
    private readonly List<RawImage> infoBackgrounds = new();
    private SpriteRenderer spriteRender;
    private Sprite originalBackgroundSprite;
    private Sprite loadedStandbySprite;
    private Vector3 originalBackgroundScale;
    private Sprite videoSurfaceSprite;
    private int standbyLoadVersion;
    private string loadedStandbyTheme;
    private string loadingStandbyTheme;
    private static string desiredStandbyTheme = "dark";

    private VideoPlayer videoPlayer;

    public Sprite circleSprite; // reserved, unused

    // Darkens only the circular play area.
    private SpriteRenderer bgCoverRenderer;
    private Sprite originalCoverSprite;
    private Material originalCoverMaterial;
    private Transform bgCoverTransform;
    private Vector3 originalCoverScale;
    private Vector3 originalCoverPosition;

    // Uses the original reverse-circle frame for the outer area.
    private SpriteRenderer circleRevRenderer;
    private Color originalCircleRevColor = Color.white;
    private SpriteRenderer backgroundClipRenderer;
    private bool clipBackgroundToRing;

    private bool displayModeApplied;
    private bool loadingPreview;
    private bool chartBackgroundActive;
    private bool outerCoverHeldForPreview;
    private float outerCoverReleaseStart = float.NaN;
    private const float OuterCoverReleaseDuration = 0.6f;
    private const float VerticalOverscanPixels = 3f;
    private static readonly WaitForEndOfFrame WaitForFrameEnd = new();
    private float coverWorldHeight;
    private float coverWorldWidth;
    private int backgroundFitMode;
    private float pendingInnerCover;
    private float pendingOuterCover;
    private bool videoWarmupReady = true;
    private bool backgroundMediaReady;
    private bool showSongDetailIntro;
    private bool backgroundVisible;
    private float mediaOverlayBlend;
    private const float IntroGameplayRevealTime =
        MajdataCore.AlphaVisualTiming.GameplayRevealTime;

    public bool IsPreparedForRecording => videoWarmupReady;

    private void Start()
    {
        spriteRender = GetComponent<SpriteRenderer>();
        originalBackgroundSprite = spriteRender.sprite;
        originalBackgroundScale = transform.localScale;
        // The sprite rect must cover the full texture so VideoPlayer receives 0..1 UVs.
        videoSurfaceSprite = Sprite.Create(
            new Texture2D(1, 1),
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f / 10.8f);
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        videoPlayer.targetMaterialRenderer = spriteRender;
        videoPlayer.targetMaterialProperty = "_MainTex";
        videoPlayer.timeUpdateMode = VideoTimeUpdateMode.GameTime;
        rawImage = GameObject.Find("Jacket").GetComponent<RawImage>();
        provider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        SongDetail = GameObject.Find("CanvasSongDetail");

        circleRev = GameObject.Find("1080Circle_Rev");
        canvasInfo = GameObject.Find("CanvasInfo");
        if (canvasInfo != null)
        {
            RectTransform sidePanel = null;
            foreach (var image in canvasInfo.GetComponentsInChildren<RawImage>(true))
            {
                var color = image.color;
                if (color.r <= 0.01f && color.g <= 0.01f && color.b <= 0.01f)
                {
                    var rect = image.rectTransform;
                    // Older scenes already contain short top/bottom strips at x=0.
                    // The runtime covers below own those strips and extend far enough
                    // for ZOOM, so retaining both composites black twice.
                    if (Mathf.Abs(rect.anchoredPosition.x) < 1f)
                    {
                        image.gameObject.SetActive(false);
                        continue;
                    }
                    infoBackgrounds.Add(image);
                    image.gameObject.SetActive(true);
                    ExtendSidePanelToScreenEdge(rect);
                    sidePanel = rect;
                }
            }
            AddTopAndBottomCover(sidePanel);
        }
        if (circleRev != null)
        {
            circleRevRenderer = circleRev.GetComponent<SpriteRenderer>();
            if (circleRevRenderer != null)
                originalCircleRevColor = circleRevRenderer.color;
        }

        SongDetail.SetActive(false);

        songDetailIntroBg = gameObject.AddComponent<SongDetailIntroBg>();
        songDetailIntroBg.Init(SongDetail, provider);

        LoadStandbyTheme(desiredStandbyTheme);

        var bgCoverObj = GameObject.Find("BackgroundCover");
        if (bgCoverObj != null)
        {
            bgCoverRenderer = bgCoverObj.GetComponent<SpriteRenderer>();
            bgCoverTransform = bgCoverObj.transform;
            originalCoverScale = bgCoverTransform.localScale;
            originalCoverPosition = bgCoverTransform.localPosition;
            if (bgCoverRenderer != null)
            {
                originalCoverSprite = bgCoverRenderer.sprite;
                originalCoverMaterial = bgCoverRenderer.sharedMaterial;
            }
        }
    }

    /// <summary>
    /// Runtime top/bottom panels replace the short legacy scene strips. Once ZOOM
    /// shrinks the frame, they must extend far enough that the background cannot
    /// show through above and below the play area.
    ///
    /// Their inner edges sit on the authored screen edge rather than on anything
    /// measured. That is the same edge the play area reaches at its authored
    /// size, and the whole frame is carried by one ZOOM/MOVE transform, so the
    /// edges stay together without ever being recomputed. Nothing here can reach
    /// across the middle of the screen.
    /// </summary>
    private void AddTopAndBottomCover(RectTransform sample)
    {
        var parent = sample != null ? sample.parent as RectTransform : null;
        if (parent == null)
            return;

        const float zoomOutMargin = 10f;
        var height = parent.rect.height * zoomOutMargin;
        // Cover only the aperture between the side panels. Extending this across
        // the whole enlarged canvas draws the same translucent black twice at the
        // top-left and top-right (and likewise below), making those regions darker.
        // ExtendSidePanelToScreenEdge preserves this inner edge.
        var sideSign = Mathf.Sign(sample.anchoredPosition.x);
        var innerEdge = sample.anchoredPosition.x -
                        sideSign * sample.rect.width * 0.5f;
        var width = Mathf.Max(1f, Mathf.Abs(innerEdge) * 2f);

        foreach (var sign in new[] { 1f, -1f })
        {
            var clone = Instantiate(sample.gameObject, sample.parent);
            clone.name = sample.name + (sign > 0f ? "_Top" : "_Bottom");
            if (!clone.TryGetComponent<RectTransform>(out var rect))
            {
                Destroy(clone);
                continue;
            }

            // Only the panel itself is wanted; anything parented to the sample
            // belongs to the authored layout and must not be duplicated.
            for (var i = rect.childCount - 1; i >= 0; i--)
                Destroy(rect.GetChild(i).gameObject);

            // Anchors are pinned to the centre so sizeDelta really is a size.
            // On a stretched anchor it means an inset from the anchors instead,
            // and a panel this large would swallow the whole window.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(
                0f,
                sign * (parent.rect.height * 0.5f + height * 0.5f));

            if (clone.TryGetComponent<RawImage>(out var image))
                infoBackgrounds.Add(image);
            clone.SetActive(true);
        }
    }

    private static void ExtendSidePanelToScreenEdge(RectTransform rect)
    {
        if (rect == null || Mathf.Abs(rect.anchoredPosition.x) < 1f)
            return;

        // The inner edge stays exactly where it was authored: it lines up with
        // the aperture, and one ZOOM/MOVE transform carries the aperture, these
        // panels and the canvas text together, so that edge stays lined up on
        // its own. Only the outward side grows.
        //
        // It has to grow a long way. ZOOM is clamped to a tenth, so once the
        // frame shrinks with the play area a panel sized for one viewport stops
        // short of the screen edge and the background shows through beside it.
        // Ten viewports still covers the edge at the smallest ZOOM.
        //
        // Growing outward from the authored edge is also why this cannot black
        // the screen out. Deriving the rect from the measured aperture instead
        // did: that arithmetic assumed centre anchors, and on a stretched anchor
        // sizeDelta is an inset from the anchors rather than a size, so ten
        // viewports of it swallowed the whole window as it grew.
        var parent = rect.parent as RectTransform;
        if (parent == null)
            return;
        const float zoomOutMargin = 10f;
        var outward = parent.rect.width * zoomOutMargin;

        var size = rect.sizeDelta;
        size.x += outward;
        // Vertical growth is symmetric, so it stays centred and never reaches
        // across the aperture; it just keeps the column tall enough to still
        // cover its corners once the frame shrinks.
        size.y += parent.rect.height * zoomOutMargin * 2f;
        rect.sizeDelta = size;
        var position = rect.anchoredPosition;
        position.x += Mathf.Sign(position.x) * outward * 0.5f;
        rect.anchoredPosition = position;
    }


    private void Update()
    {
        if (chartBackgroundActive)
            RefreshChartBackgroundVisibility();

        if (loadingPreview && provider != null)
        {
            // Standby: keep the outer area fully closed until playback starts.
            SetOuterCoverAlpha(1f);
            outerCoverHeldForPreview = true;
            if (provider.PlaybackStarted)
            {
                loadingPreview = false;
                ApplyDisplayModeForPlayback();
            }
        }

        if (displayModeApplied && outerCoverHeldForPreview && provider != null && provider.AudioTime >= 0f)
        {
            outerCoverHeldForPreview = false;
            // The outer area is held fully closed for the whole intro. Snapping it
            // open on the first gameplay frame reads as a hard brightness step, so
            // hand it over with a short crossfade instead.
            outerCoverReleaseStart = provider.AudioTime;
        }

        if (!float.IsNaN(outerCoverReleaseStart) && provider != null)
        {
            if (provider.AudioTime < 0f)
                outerCoverReleaseStart = float.NaN;
            else
            {
                var progress = Mathf.Clamp01(
                    (provider.AudioTime - outerCoverReleaseStart) /
                    OuterCoverReleaseDuration);
                SetOuterCoverAlpha(
                    Mathf.Lerp(1f, pendingOuterCover, Mathf.SmoothStep(0f, 1f, progress)));
                if (progress >= 1f)
                    outerCoverReleaseStart = float.NaN;
            }
        }

        if (!videoPlayer.isPrepared || !videoPlayer.isPlaying)
            return;

        var delta = (float)videoPlayer.clockTime - provider.AudioTime;
        if (provider.AudioTime < 0) return;

        if (Time.captureFramerate != 0)
        {
            // GameTime advances by exactly one capture step per exported frame.
            // Chasing wall-clock speed here could clamp playback to zero after a shader hitch,
            // leaving the PV frozen for the rest of an otherwise valid recording.
            videoPlayer.timeUpdateMode = VideoTimeUpdateMode.GameTime;
            // Recording speed is already applied through Time.timeScale, so multiplying by
            // playSpeed again would make the PV run at speed squared.
            videoPlayer.playbackSpeed = 1f;
            return;
        }

        if (delta < -0.01f)
            videoPlayer.playbackSpeed = playSpeed + 0.2f;
        else if (delta > 0.01f)
            videoPlayer.playbackSpeed = playSpeed - 0.2f;
        else
            videoPlayer.playbackSpeed = playSpeed;
    }

    public void PlaySongDetail(float timelineTime, float speed)
    {
        if (SongDetail == null)
            return;
        songDetailIntroBg?.SetTimeline(timelineTime, speed);
        if (timelineTime >= 0f)
        {
            SongDetail.SetActive(false);
            return;
        }

        SongDetail.SetActive(true);
        songDetailTemplate ??= SongDetail.GetComponentInChildren<SongDetailTemplateView>(true);
        songDetailTemplate?.SampleCacheOverlayIntro(timelineTime);
            // SongDetailIntroBg owns card transitions for the arcade themes.
        if (songDetailIntroBg != null && songDetailIntroBg.TakesOverCard)
            return;
        var songDetailAnimator = SongDetail.GetComponent<Animator>();
        if (songDetailAnimator == null)
            return;

        const float entryDuration = 0.8333333f;
        const float showingDuration = 3f;
        const float exitDuration = 1f;
        var elapsed = Mathf.Clamp(5f + timelineTime, 0f,
            entryDuration + showingDuration + exitDuration);
        songDetailAnimator.speed = Time.captureFramerate != 0
            ? 1f
            : Mathf.Max(0.01f, speed);
        if (elapsed < entryDuration)
            songDetailAnimator.Play("Entry", 0, elapsed / entryDuration);
        else if (elapsed < entryDuration + showingDuration)
            songDetailAnimator.Play("Showing", 0, (elapsed - entryDuration) / showingDuration);
        else
            songDetailAnimator.Play("Exit", 0,
                (elapsed - entryDuration - showingDuration) / exitDuration);
        songDetailAnimator.Update(0f);
    }

    public void HideSongDetail()
    {
        if (SongDetail != null)
            SongDetail.SetActive(false);
    }

    // Intro theme selected by Edit: default, circleplus, or circle.
    public void SetIntroBgTheme(string themeName)
    {
        songDetailIntroBg?.SetTheme(themeName);
    }

    public void PauseVideo()
    {
        if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.isPlaying)
            videoPlayer.Pause();
        var songDetailAnimator = SongDetail != null ? SongDetail.GetComponent<Animator>() : null;
        if (songDetailAnimator != null && SongDetail.activeSelf)
            songDetailAnimator.speed = 0f;
        songDetailIntroBg?.SetSpeed(0f);
    }

    public void SetPausedTimelineTime(float time)
    {
        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            var target = Math.Max(0d, time);
            if (videoPlayer.length > 0d)
                target = Math.Min(
                    target,
                    Math.Max(0d, videoPlayer.length - 0.001d));
            videoPlayer.time = target;
            videoPlayer.Pause();
            ApplyDisplayModeForPlayback();
        }
        PauseVideo();
    }

    public void ContinueVideo(float speed)
    {
        playSpeed = speed;
        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            videoPlayer.playbackSpeed = speed;
            videoPlayer.Play();
        }
        var songDetailAnimator = SongDetail != null ? SongDetail.GetComponent<Animator>() : null;
        if (songDetailAnimator != null && SongDetail.activeSelf)
            songDetailAnimator.speed = Time.captureFramerate != 0
                ? 1f
                : Mathf.Max(0.01f, speed);
        songDetailIntroBg?.SetSpeed(speed);
    }

    // Prepare masks and visibility before loading the BGA.
    private void PrepareBGLoad(float innerCover, float outerCover, bool showSongDetail)
    {
        chartBackgroundActive = true;
        showSongDetailIntro = showSongDetail;
        backgroundMediaReady = false;
        displayModeApplied = false;
        loadingPreview = true;
        pendingInnerCover = Mathf.Clamp01(innerCover);
        pendingOuterCover = Mathf.Clamp01(outerCover);
        outerCoverHeldForPreview = true;
        SetPreviewUiVisible(true);
        SetBackgroundVisible(false);

        // Full-screen mode 2 used the circular sprite. 1080BG is a solid
        // square and makes a widescreen PV look square when tinted.
        if (bgCoverRenderer != null && bgCoverTransform != null)
        {
            bgCoverRenderer.sprite = circleSprite != null ? circleSprite : originalCoverSprite;
            bgCoverRenderer.sharedMaterial = originalCoverMaterial;
            bgCoverRenderer.color = new Color(0f, 0f, 0f, pendingInnerCover);
            bgCoverTransform.localPosition = originalCoverPosition;
            bgCoverTransform.localScale = originalCoverScale;
        }

        // Outer cover stays fully closed during standby; the configured value
        // is applied once chart playback actually starts.
        SetOuterCoverAlpha(1f);

        // Use the circle frame's rendered height as the single BGA target.
        coverWorldHeight = Mathf.Max(
            bgCoverRenderer != null ? bgCoverRenderer.bounds.size.y : 0f,
            circleRevRenderer != null ? circleRevRenderer.bounds.size.y : 0f);
        coverWorldWidth = Mathf.Max(
            bgCoverRenderer != null ? bgCoverRenderer.bounds.size.x : 0f,
            circleRevRenderer != null ? circleRevRenderer.bounds.size.x : 0f);
    }

    public void LoadBGFromPath(
        string path,
        float speed,
        float innerCover,
        float outerCover,
        bool showSongDetail,
        int fitMode = 0,
        bool loadDefaultVideo = true)
    {
        backgroundFitMode = Mathf.Clamp(fitMode, 0, 1);
        PrepareBGLoad(innerCover, outerCover, showSongDetail);

        var pictureName = new[] { "Cover", "bg" };
        var pictureExt = new[] { ".png", ".jpg", ".jpeg" };
        var videoName = new[] { "pv.mp4", "mv.mp4", "bg.mp4" };
        string videoPath = null;
        if (loadDefaultVideo)
            foreach (var name in videoName)
            {
                var candidate = Path.Combine(path, name);
                if (!File.Exists(candidate))
                    continue;
                videoPath = candidate;
                break;
            }
        videoWarmupReady = videoPath == null;

        foreach (var name in pictureName)
        {
            var finished = false;
            foreach (var ext in pictureExt)
                if (File.Exists(path + "/" + name + ext))
                {
                    StartCoroutine(loadPic(path + "/" + name + ext, videoPath == null));
                    finished = true;
                    break;
                }
            if (finished) break;
        }

        if (videoPath != null)
            loadVideo(videoPath, speed);
    }

    public void LoadStandbyTheme(string themeName)
    {
        desiredStandbyTheme = string.Equals(themeName, "light", System.StringComparison.OrdinalIgnoreCase)
            ? "light"
            : "dark";
        if (loadedStandbyTheme == desiredStandbyTheme && loadedStandbySprite != null)
        {
            if (!loadingPreview && !chartBackgroundActive &&
                (provider == null || !provider.isStart) && spriteRender != null)
            {
                spriteRender.sprite = loadedStandbySprite;
                transform.localScale = originalBackgroundScale;
                SetBackgroundVisible(true);
            }
            return;
        }
        if (loadingStandbyTheme == desiredStandbyTheme)
            return;
        loadingStandbyTheme = desiredStandbyTheme;
        var fileName = desiredStandbyTheme == "light"
            ? "Default_Background_Light.png"
            : "Default_Background_Dark.png";
        var path = Path.Combine(Application.streamingAssetsPath, "Background", fileName);
        StartCoroutine(LoadStandbySprite(path, desiredStandbyTheme, ++standbyLoadVersion));
    }

    private IEnumerator LoadStandbySprite(string path, string themeName, int version)
    {
        var sprite = SpriteLoader.LoadSpriteFromFile(path);
        if (this == null || version != standbyLoadVersion || sprite == null ||
            sprite.texture == null || sprite.texture.width <= 0)
        {
            if (version == standbyLoadVersion)
                loadingStandbyTheme = null;
            yield break;
        }

        var previous = loadedStandbySprite;
        loadedStandbySprite = sprite;
        loadedStandbyTheme = themeName;
        loadingStandbyTheme = null;
        originalBackgroundSprite = sprite;
        var isStandby = provider == null || !provider.isStart;
        if (!loadingPreview && !chartBackgroundActive && isStandby && spriteRender != null)
        {
            spriteRender.sprite = sprite;
            transform.localScale = originalBackgroundScale;
            SetBackgroundVisible(true);
        }

        if (previous != null)
        {
            if (previous.texture != null)
                Destroy(previous.texture);
            Destroy(previous);
        }
    }

    public void SetCoverAlpha(float innerCover, float outerCover)
    {
        pendingInnerCover = Mathf.Clamp01(innerCover);
        pendingOuterCover = Mathf.Clamp01(outerCover);

        if (bgCoverRenderer != null)
        {
            var color = bgCoverRenderer.color;
            color.a = pendingInnerCover;
            bgCoverRenderer.color = color;
        }

        // The outer area is only held fully closed while the intro hand-over is
        // still pending. Keying this on "any negative time" instead would replay
        // the hand-over every time playback resumes from a pause, which reads as
        // a corner flash.
        var introHold = outerCoverHeldForPreview &&
                        provider != null &&
                        provider.AudioTime < 0f;
        var outerAlpha = introHold ? 1f : pendingOuterCover;
        outerCoverHeldForPreview = introHold;
        // While the intro hand-over crossfade runs, Update owns the outer alpha;
        // writing the target here would turn the fade back into a snap.
        if (float.IsNaN(outerCoverReleaseStart))
            SetOuterCoverAlpha(outerAlpha);
    }

    private IEnumerator loadPic(string path, bool useAsBackground)
    {
        Sprite sprite;
        yield return sprite = SpriteLoader.LoadSpriteFromFile(path);
        if (this == null || rawImage == null || sprite == null)
            yield break;

        if (sprite.texture != null && sprite.texture.width > 0 && sprite.texture.height > 0)
            rawImage.texture = sprite.texture;
        if (!useAsBackground)
            yield break;

        if (spriteRender == null || sprite.bounds.size.y <= 0f)
            yield break;

        spriteRender.sprite = sprite;
        ApplyMediaScale(spriteRender, sprite.texture.width, sprite.texture.height, false);
        backgroundMediaReady = true;
        RefreshChartBackgroundVisibility();
    }

    private void loadVideo(string path, float speed)
    {
        videoWarmupReady = false;
        videoPlayer.url = "file://" + path;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.timeUpdateMode = VideoTimeUpdateMode.GameTime;
        // Offline capture must wait for every decoded PV frame. Dropping frames
        // makes the exported video stutter even though the chart clock is smooth.
        videoPlayer.skipOnDrop = provider == null || !provider.isRecord;
        videoPlayer.playbackSpeed = speed;
        playSpeed = speed;
        StartCoroutine(waitFumenStart());
    }

    private IEnumerator waitFumenStart()
    {
        videoPlayer.Prepare();
        while (videoPlayer != null && !videoPlayer.isPrepared)
            yield return WaitForFrameEnd;
        if (this == null || videoPlayer == null || spriteRender == null || provider == null)
            yield break;

        spriteRender.sprite = videoSurfaceSprite;

        ApplyMediaScale(spriteRender, videoPlayer.width, videoPlayer.height, true);

        // Decode the first frame while the timeline is still negative so
        // crossing zero does not reveal the standby background for one frame.
        videoPlayer.time = 0d;
        videoPlayer.Play();
        var decodeDeadline = Time.realtimeSinceStartup + 12f;
        do
        {
            yield return WaitForFrameEnd;
        } while (videoPlayer != null &&
                 videoPlayer.frame < 0 &&
                 (videoPlayer.texture == null || videoPlayer.texture.width <= 0) &&
                 Time.realtimeSinceStartup < decodeDeadline);
        if (this == null || videoPlayer == null)
            yield break;
        videoPlayer.Pause();
        if (videoPlayer.frame < 0 &&
            (videoPlayer.texture == null || videoPlayer.texture.width <= 0))
            yield break;
        videoWarmupReady = true;
        backgroundMediaReady = true;
        RefreshChartBackgroundVisibility();

        while (provider != null && provider.AudioTime <= 0)
            yield return WaitForFrameEnd;
        if (this == null || videoPlayer == null || provider == null)
            yield break;
        ApplyDisplayModeForPlayback();
        videoPlayer.time = provider.AudioTime;
        videoPlayer.Play();
    }

    private float GetCoverWorldHeight()
        => coverWorldHeight > 0f ? coverWorldHeight : GetCameraCoverHeight();

    private float GetCoverWorldWidth()
    {
        if (coverWorldWidth > 0f)
            return coverWorldWidth;
        var camera = Camera.main;
        return camera != null ? GetCameraCoverHeight() * camera.aspect : GetCameraCoverHeight();
    }

    /// <summary>
    /// Keeps the background picture inside the play circle.
    /// </summary>
    /// <remarks>
    /// The frame that hides the outer area is the one whose alpha the outer
    /// brightness setting drives, so turning that brightness up uncovers the
    /// four corners of the picture along with the notes out there. This is a
    /// second copy of the same frame, opaque and on the background's own
    /// sorting layer: it cuts the picture to the circle underneath everything
    /// that is played, so the corners go dark while the notes stay visible.
    /// </remarks>
    public void SetBackgroundClip(bool clip)
    {
        if (clipBackgroundToRing == clip && backgroundClipRenderer != null)
        {
            backgroundClipRenderer.enabled = clip;
            return;
        }
        clipBackgroundToRing = clip;

        if (backgroundClipRenderer == null)
        {
            if (!clip || circleRev == null || circleRevRenderer == null ||
                circleRevRenderer.sprite == null || spriteRender == null)
                return;

            var clipObject = new GameObject("BackgroundRingClip");
            // Parented to the frame it copies, so ZOOM and MOVE carry it
            // without a second transform to keep in step.
            clipObject.transform.SetParent(circleRev.transform, false);
            clipObject.transform.localPosition = Vector3.zero;
            clipObject.transform.localRotation = Quaternion.identity;
            clipObject.transform.localScale = Vector3.one;

            backgroundClipRenderer = clipObject.AddComponent<SpriteRenderer>();
            backgroundClipRenderer.sprite = circleRevRenderer.sprite;
            backgroundClipRenderer.sortingLayerID = spriteRender.sortingLayerID;
            backgroundClipRenderer.sortingOrder = Mathf.Max(
                spriteRender.sortingOrder,
                bgCoverRenderer != null ? bgCoverRenderer.sortingOrder : 0) + 1;
            var clipColor = originalCircleRevColor;
            clipColor.a = 1f;
            backgroundClipRenderer.color = clipColor;
        }

        backgroundClipRenderer.enabled = clip;
    }

    public void SetBackgroundFitMode(int fitMode)
    {
        var normalized = Mathf.Clamp(fitMode, 0, 1);
        if (backgroundFitMode == normalized)
            return;
        backgroundFitMode = normalized;

        if (!chartBackgroundActive || spriteRender == null || spriteRender.sprite == null)
            return;
        if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.width > 0 && videoPlayer.height > 0)
            ApplyMediaScale(spriteRender, videoPlayer.width, videoPlayer.height, true);
        else if (spriteRender.sprite.texture != null)
            ApplyMediaScale(
                spriteRender,
                spriteRender.sprite.texture.width,
                spriteRender.sprite.texture.height,
                false);
    }

    public void ApplyMediaScale(
        SpriteRenderer renderer,
        float sourceWidth,
        float sourceHeight,
        bool squareVideoSurface)
    {
        if (renderer == null || renderer.sprite == null || sourceWidth <= 0f || sourceHeight <= 0f)
            return;

        var bounds = renderer.sprite.bounds.size;
        if (bounds.x <= 0f || bounds.y <= 0f)
            return;

        var sourceAspect = sourceWidth / sourceHeight;
        if (squareVideoSurface)
        {
            if (backgroundFitMode == 1)
            {
                var scaleX = GetCoverWorldWidth() / bounds.x;
                renderer.transform.localScale = new Vector3(scaleX, scaleX / sourceAspect, 1f);
            }
            else
            {
                var scaleY = GetCoverWorldHeight() / bounds.y;
                renderer.transform.localScale = new Vector3(scaleY * sourceAspect, scaleY, 1f);
            }
            return;
        }

        var scale = backgroundFitMode == 1
            ? GetCoverWorldWidth() / bounds.x
            : GetCoverWorldHeight() / bounds.y;
        renderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private static float GetCameraCoverHeight()
    {
        var camera = Camera.main;
        if (camera == null)
            return 10.8f;

        var visibleHeight = camera.orthographicSize * 2f;
        var worldPerPixel = visibleHeight / Mathf.Max(1, camera.pixelHeight);
        return visibleHeight + worldPerPixel * VerticalOverscanPixels * 2f;
    }

    private void ApplyDisplayModeForPlayback()
    {
        if (displayModeApplied)
            return;

        displayModeApplied = true;
        loadingPreview = false;
        SetCoverAlpha(pendingInnerCover, pendingOuterCover);
        SetPreviewUiVisible(false);
    }

    // Drive the 1080Circle_Rev round-frame alpha, the same
    // object the early mod toggled. 1 = frame visible (outer covered), 0 = frame
    // hidden (full-screen BGA shows through the outer area).
    private void SetOuterCoverAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        foreach (var background in infoBackgrounds)
        {
            if (background == null)
                continue;
            background.gameObject.SetActive(true);
            var backgroundColor = background.color;
            backgroundColor.a = alpha;
            background.color = backgroundColor;
        }

        if (circleRevRenderer != null)
        {
            if (circleRev != null)
                circleRev.SetActive(true);
            var color = originalCircleRevColor;
            color.a = alpha;
            circleRevRenderer.color = color;
            circleRevRenderer.enabled = true;
        }
    }

    private void SetPreviewUiVisible(bool visible)
    {
        if (circleRev != null)
            circleRev.SetActive(true);
        if (canvasInfo != null)
            canvasInfo.SetActive(true);
        foreach (var background in infoBackgrounds)
            if (background != null)
                background.gameObject.SetActive(true);
    }

    private void SetBackgroundVisible(bool visible)
    {
        if (spriteRender == null)
            return;
        backgroundVisible = visible;
        ApplyBackgroundAlpha();
    }

    public void SetMediaOverlayBlend(float blend)
    {
        mediaOverlayBlend = Mathf.Clamp01(blend);
        ApplyBackgroundAlpha();
    }

    private void ApplyBackgroundAlpha()
    {
        if (spriteRender == null)
            return;
        var color = spriteRender.color;
        color.a = backgroundVisible ? 1f - mediaOverlayBlend : 0f;
        spriteRender.color = color;
    }

    private void RefreshChartBackgroundVisibility()
    {
        var introFinished = !showSongDetailIntro ||
                            provider == null ||
                            provider.AudioTime >= IntroGameplayRevealTime;
        SetBackgroundVisible(backgroundMediaReady && introFinished);
    }

    private void OnDestroy()
    {
        if (videoSurfaceSprite != null)
            Destroy(videoSurfaceSprite);
        if (loadedStandbySprite != null)
        {
            if (loadedStandbySprite.texture != null)
                Destroy(loadedStandbySprite.texture);
            Destroy(loadedStandbySprite);
        }
    }
}
