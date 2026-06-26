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

    private float smoothRDelta;
    private GameObject SongDetail;
    private GameObject circleRev;
    private GameObject canvasInfo;
    private readonly List<RawImage> infoBackgrounds = new();
    private SpriteRenderer spriteRender;
    private Sprite originalBackgroundSprite;
    private Sprite videoSurfaceSprite;

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
    private const float VerticalOverscanPixels = 3f;
    private static readonly WaitForEndOfFrame WaitForFrameEnd = new();
    private float coverWorldHeight;
    private float pendingInnerCover;
    private float pendingOuterCover;
    private void Start()
    {
        spriteRender = GetComponent<SpriteRenderer>();
        originalBackgroundSprite = spriteRender.sprite;
        // 用一张 1x1 贴图，rect 必须等于贴图尺寸，UV 才是 0~1。
        // 若用 Texture2D.whiteTexture(4x4) 配 Rect(0,0,1,1)，UV 只覆盖 0~0.25，
        // VideoPlayer 覆写 _MainTex 后只采样到视频左下角 1/16，画面会被放大4倍。
        videoSurfaceSprite = Sprite.Create(
            new Texture2D(1, 1),
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f / 10.8f);
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        videoPlayer.targetMaterialRenderer = spriteRender;
        videoPlayer.targetMaterialProperty = "_MainTex";
        smoothRDelta = Mathf.Max(Time.unscaledDeltaTime, 1f / 60f);
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

    private void Update()
    {
        if (loadingPreview && provider != null)
        {
            // Standby: keep the outer area fully closed until playback starts.
            SetOuterCoverAlpha(1f);
            if (provider.PlaybackStarted)
            {
                loadingPreview = false;
                ApplyDisplayModeForPlayback();
            }
        }

        if (!videoPlayer.isPrepared || !videoPlayer.isPlaying)
            return;

        var delta = (float)videoPlayer.clockTime - provider.AudioTime;
        smoothRDelta += (Time.unscaledDeltaTime - smoothRDelta) * 0.01f;
        if (provider.AudioTime < 0) return;
        var realSpeed = Time.deltaTime / smoothRDelta;

        if (Time.captureFramerate != 0)
        {
            videoPlayer.playbackSpeed = realSpeed - delta;
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
        if (timelineTime >= 0f)
        {
            SongDetail.SetActive(false);
            return;
        }

        SongDetail.SetActive(true);
        var songDetailAnimator = SongDetail.GetComponent<Animator>();
        if (songDetailAnimator == null)
            return;

        const float entryDuration = 0.8333333f;
        const float showingDuration = 3f;
        const float exitDuration = 1f;
        var elapsed = Mathf.Clamp(5f + timelineTime, 0f,
            entryDuration + showingDuration + exitDuration);
        songDetailAnimator.speed = Mathf.Max(0.01f, speed);
        if (elapsed < entryDuration)
            songDetailAnimator.Play("Entry", 0, elapsed / entryDuration);
        else if (elapsed < entryDuration + showingDuration)
            songDetailAnimator.Play("Showing", 0, (elapsed - entryDuration) / showingDuration);
        else
            songDetailAnimator.Play("Exit", 0,
                (elapsed - entryDuration - showingDuration) / exitDuration);
        songDetailAnimator.Update(0f);
    }

    public void PauseVideo()
    {
        if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.isPlaying)
            videoPlayer.Pause();
        var songDetailAnimator = SongDetail != null ? SongDetail.GetComponent<Animator>() : null;
        if (songDetailAnimator != null && SongDetail.activeSelf)
            songDetailAnimator.speed = 0f;
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
            songDetailAnimator.speed = Mathf.Max(0.01f, speed);
    }

    public void LoadBGFromPath(string path, float speed, float innerCover, float outerCover)
    {
        displayModeApplied = false;
        loadingPreview = true;
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

        var pictureName = new[] { "Cover", "bg" };
        var pictureExt = new[] { ".png", ".jpg", ".jpeg" };
        var videoName = new[] { "pv.mp4", "mv.mp4", "bg.mp4" };
        string videoPath = null;
        foreach (var name in videoName)
        {
            var candidate = Path.Combine(path, name);
            if (!File.Exists(candidate))
                continue;
            videoPath = candidate;
            break;
        }

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
        SetOuterCoverAlpha(outerAlpha);
    }

    private IEnumerator loadPic(string path, bool useAsBackground)
    {
        Sprite sprite;
        yield return sprite = SpriteLoader.LoadSpriteFromFile(path);
        rawImage.texture = sprite.texture;
        if (!useAsBackground)
            yield break;

        spriteRender.sprite = sprite;
        float scale = GetCoverWorldHeight() / sprite.bounds.size.y;
        gameObject.transform.localScale = new Vector3(scale, scale, scale);
        SetBackgroundVisible(true);
    }

    private void loadVideo(string path, float speed)
    {
        videoPlayer.url = "file://" + path;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.playbackSpeed = speed;
        playSpeed = speed;
        StartCoroutine(waitFumenStart());
    }

    private IEnumerator waitFumenStart()
    {
        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared) yield return WaitForFrameEnd;

        spriteRender.sprite = videoSurfaceSprite;

        float videoAspect = videoPlayer.width / (float)videoPlayer.height;
        float spriteHeight = videoSurfaceSprite.bounds.size.y;
        // 高度对齐相机可见高度，宽度按视频宽高比自动缩放（pv为16:9时铺满）。
        float screenHeight = Camera.main != null ? Camera.main.orthographicSize * 2f : spriteHeight;
        float scaleY = screenHeight / spriteHeight;
        gameObject.transform.localScale = new Vector3(scaleY * videoAspect, scaleY, 1f);

        // Decode the first frame while the timeline is still negative so
        // crossing zero does not reveal the standby background for one frame.
        videoPlayer.time = 0d;
        videoPlayer.Play();
        yield return WaitForFrameEnd;
        videoPlayer.Pause();
        SetBackgroundVisible(true);

        while (provider.AudioTime <= 0) yield return WaitForFrameEnd;
        ApplyDisplayModeForPlayback();
        videoPlayer.time = provider.AudioTime;
        videoPlayer.Play();
    }

    private float GetCoverWorldHeight()
        => coverWorldHeight > 0f ? coverWorldHeight : GetCameraCoverHeight();

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

        if (circleRevRenderer != null && circleRev.activeSelf)
        {
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
        var color = spriteRender.color;
        color.a = visible ? 1f : 0f;
        spriteRender.color = color;
    }

    private void OnDestroy()
    {
        if (videoSurfaceSprite != null)
            Destroy(videoSurfaceSprite);
    }
}
