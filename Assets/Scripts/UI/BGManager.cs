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

    private bool displayModeApplied;
    private bool loadingPreview;
    private bool chartBackgroundActive;
    private bool outerCoverHeldForPreview;
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
    private const float IntroGameplayRevealTime = -2f;

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
            foreach (var image in canvasInfo.GetComponentsInChildren<RawImage>(true))
            {
                var color = image.color;
                if (color.r <= 0.01f && color.g <= 0.01f && color.b <= 0.01f)
                {
                    infoBackgrounds.Add(image);
                    image.gameObject.SetActive(true);
                    ExtendSidePanelToScreenEdge(image.rectTransform);
                }
            }
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

    private static void ExtendSidePanelToScreenEdge(RectTransform rect)
    {
        if (rect == null || Mathf.Abs(rect.anchoredPosition.x) < 1f)
            return;

        // Preserve the inner edge and add outward overscan for fractional Canvas scaling.
        const float overscan = 4f;
        var size = rect.sizeDelta;
        size.x += overscan;
        rect.sizeDelta = size;
        var position = rect.anchoredPosition;
        position.x += Mathf.Sign(position.x) * overscan * 0.5f;
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
            SetOuterCoverAlpha(pendingOuterCover);
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
        outerCoverHeldForPreview = true;
        pendingInnerCover = Mathf.Clamp01(innerCover);
        pendingOuterCover = Mathf.Clamp01(outerCover);
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

        var outerAlpha = provider != null && provider.AudioTime < 0f
            ? 1f
            : pendingOuterCover;
        outerCoverHeldForPreview = outerAlpha >= 0.999f && provider != null && provider.AudioTime < 0f;
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
