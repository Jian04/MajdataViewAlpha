using System.Collections;
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
    private SpriteRenderer spriteRender;

    private VideoPlayer videoPlayer;

    private float originalScaleX;
    private int displayMode;

    public Sprite circleSprite; // reserved, unused

    private SpriteRenderer bgCoverRenderer;
    private Sprite originalCoverSprite;
    private Transform bgCoverTransform;
    private Vector3 originalCoverScale;
    private bool displayModeApplied;

    private void Start()
    {
        originalScaleX = gameObject.transform.localScale.x;
        spriteRender = GetComponent<SpriteRenderer>();
        videoPlayer = GetComponent<VideoPlayer>();
        rawImage = GameObject.Find("Jacket").GetComponent<RawImage>();
        provider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        SongDetail = GameObject.Find("CanvasSongDetail");
        SongDetail.SetActive(false);

        var bgCoverObj = GameObject.Find("BackgroundCover");
        if (bgCoverObj != null)
        {
            bgCoverRenderer = bgCoverObj.GetComponent<SpriteRenderer>();
            bgCoverTransform = bgCoverObj.transform;
            originalCoverScale = bgCoverTransform.localScale;
            if (bgCoverRenderer != null)
                originalCoverSprite = bgCoverRenderer.sprite;
        }
    }

    private void Update()
    {
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

    public void PlaySongDetail()
    {
        SongDetail.SetActive(true);
    }

    public void PauseVideo()
    {
        videoPlayer.Pause();
    }

    public void ContinueVideo(float speed)
    {
        videoPlayer.playbackSpeed = speed;
        playSpeed = speed;
        videoPlayer.Play();
    }

    public void LoadBGFromPath(string path, float speed, int bgDisplayMode = 0)
    {
        displayMode = bgDisplayMode;
        displayModeApplied = false;
        SetPreviewUiVisible(true);

        if (bgCoverRenderer != null && bgCoverTransform != null)
        {
            if (displayMode == 1)
            {
                bgCoverRenderer.sprite = originalCoverSprite;
                float screenH = Camera.main.orthographicSize * 2f;
                float screenW = screenH * Camera.main.aspect;
                float sx = screenW / (originalCoverSprite.texture.width / 100f);
                float sy = screenH / (originalCoverSprite.texture.height / 100f);
                bgCoverTransform.localScale = new Vector3(sx, sy, 1f);
            }
            else if (displayMode == 2)
            {
                bgCoverRenderer.sprite = circleSprite != null ? circleSprite : originalCoverSprite;
                bgCoverTransform.localScale = originalCoverScale;
            }
            else
            {
                bgCoverRenderer.sprite = originalCoverSprite;
                bgCoverTransform.localScale = originalCoverScale;
            }
        }

        var pictureName = new[] { "Cover", "bg" };
        var pictureExt = new[] { ".png", ".jpg", ".jpeg" };
        var videoName = new[] { "pv.mp4", "mv.mp4", "bg.mp4" };

        foreach (var name in pictureName)
        {
            var finished = false;
            foreach (var ext in pictureExt)
                if (File.Exists(path + "/" + name + ext))
                {
                    StartCoroutine(loadPic(path + "/" + name + ext));
                    finished = true;
                    break;
                }
            if (finished) break;
        }

        foreach (var name in videoName)
        {
            if (!File.Exists(path + "/" + name)) continue;
            loadVideo(path + "/" + name, speed);
            break;
        }
    }

    private IEnumerator loadPic(string path)
    {
        Sprite sprite;
        yield return sprite = SpriteLoader.LoadSpriteFromFile(path);
        rawImage.texture = sprite.texture;
        spriteRender.sprite = sprite;
        float scale;
        if (displayMode != 0)
        {
            float screenH = Camera.main.orthographicSize * 2f;
            scale = screenH / (sprite.texture.height / 100f);
        }
        else
        {
            scale = 1140f / sprite.texture.width;
        }
        gameObject.transform.localScale = new Vector3(scale, scale, scale);
    }

    private void loadVideo(string path, float speed)
    {
        videoPlayer.url = "file://" + path;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.playbackSpeed = speed;
        playSpeed = speed;
        StartCoroutine(waitFumenStart());
    }

    private IEnumerator ApplyDisplayModeWhenPlaybackStarts()
    {
        while (provider.AudioTime < 0f)
            yield return null;

        ApplyDisplayModeForPlayback();
    }

    private IEnumerator waitFumenStart()
    {
        videoPlayer.Prepare();
        while (provider.AudioTime <= 0) yield return new WaitForEndOfFrame();
        while (!videoPlayer.isPrepared) yield return new WaitForEndOfFrame();
        ApplyDisplayModeForPlayback();
        videoPlayer.Play();
        videoPlayer.time = provider.AudioTime;

        spriteRender.sprite =
            Sprite.Create(new Texture2D(1080, 1080), new Rect(0, 0, 1080, 1080), new Vector2(0.5f, 0.5f));

        if (displayMode != 0)
        {
            float screenH = Camera.main.orthographicSize * 2f;
            float scaleY = screenH / (1080f / 100f);
            float scaleX = scaleY * ((float)videoPlayer.width / videoPlayer.height);
            gameObject.transform.localScale = new Vector3(scaleX, scaleY, 1f);
        }
        else
        {
            var scale = videoPlayer.height / (float)videoPlayer.width;
            gameObject.transform.localScale = new Vector3(originalScaleX, originalScaleX * scale);
        }
    }

    private void SetPreviewUiVisible(bool visible)
    {
        var circleRev = GameObject.Find("1080Circle_Rev");
        if (circleRev != null)
            circleRev.SetActive(visible || displayMode == 0);

        var canvasInfo = GameObject.Find("CanvasInfo");
        if (canvasInfo != null)
            canvasInfo.SetActive(visible || displayMode == 0);
    }

    private void ApplyDisplayModeForPlayback()
    {
        if (displayModeApplied)
            return;

        displayModeApplied = true;
        SetPreviewUiVisible(false);
    }
}
