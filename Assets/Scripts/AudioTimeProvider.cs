using System;
using UnityEngine;

public class AudioTimeProvider : MonoBehaviour
{
    // Playback runs at 120 fps in builds and 60 in the Unity editor, where fixed per-frame
    // overhead makes 120 fps stutter. Judgement uses FixedUpdate; recording uses captureFramerate.
    private static int PlaybackFrameRate => Application.isEditor ? 60 : 120;
    public float AudioTime; //notes get this value
    public bool isStart;
    public bool isRecord;
    public float offset;
    private float speed;
    private bool previewFixedTime;

    private float startTime;
    private long ticks;
    private int recordingFrameRate = 60;

    public float CurrentSpeed => speed;
    public bool IsPreview => previewFixedTime;
    public bool IsPaused { get; private set; }
    public bool PlaybackStarted => isStart && (isRecord
        ? Time.time >= startTime
        : Time.realtimeSinceStartup >= startTime);

    // Continuous timeline for visuals such as the intro background: negative during the cover
    // transition and smooth across zero. AudioTime is clamped to prevent reverse playback during
    // preload, so dependent animations would jump from the frozen value when crossing zero.
    public float TimelineTime => !isStart || previewFixedTime
        ? AudioTime
        : isRecord
            ? Time.time - startTime + offset
            : (Time.realtimeSinceStartup - startTime) * speed + offset;

    private void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = PlaybackFrameRate;
    }

    // Update is called once per frame
    private void Update()
    {
        if (previewFixedTime)
            return;

        if (isStart)
        {
            if (isRecord)
                AudioTime = Time.time - startTime + offset;
            else
                // When startAt is in the future for Edit's 0.2s preload, freeze at the start
                // instead of playing backward. Preload time for requests and loading is not visible.
                AudioTime = Mathf.Max(offset, (Time.realtimeSinceStartup - startTime) * speed + offset);
        }
    }

    public float GetFrame()
    {
        var _audioTime = AudioTime * 1000;

        return _audioTime / 16.6667f;
    }
    public void SetStartTime(long _ticks, float _offset, float _speed, bool _isRecord = false, int recordFrameRate = 30)
    {
        previewFixedTime = false;
        IsPaused = false;
        ticks = _ticks;
        offset = _offset;
        AudioTime = offset;
        var dateTime = new DateTime(ticks);
        var seconds = (dateTime - DateTime.Now).TotalSeconds;
        isRecord = _isRecord;
        speed = _speed;
        if (_isRecord)
        {
            // ScreenRecorder starts the recording clock after resize, encoder setup,
            // and render warmup have completed.
            startTime = Time.time;
            recordingFrameRate = recordFrameRate >= 120 ? 120 : 60;
        }
        else
        {
            startTime = Time.realtimeSinceStartup + (float)seconds;
            Application.targetFrameRate = PlaybackFrameRate;
            Time.captureFramerate = 0;
        }

        // Recording stays frozen while the window resize, ffmpeg launch, and named-pipe
        // handshake complete. ScreenRecorder starts this clock with the first captured frame.
        isStart = !_isRecord;
    }

    public void BeginRecordingCapture(float introDuration)
    {
        if (!isRecord)
            return;

        IsPaused = false;
        introDuration = Mathf.Max(0f, introDuration);
        Time.timeScale = speed;
        // captureFramerate is the export's virtual time step and must be 60 or 120. At 30,
        // the 33ms step exceeds the 16.67ms CriticalPerfect window, making DJAuto always late.
        // Do not cap targetFrameRate here. Offline capture advances by captureFramerate,
        // while the encoder pipe naturally applies backpressure; a target frame cap would
        // unnecessarily force low-resolution exports to run in real time.
        Time.captureFramerate = recordingFrameRate;
        Application.targetFrameRate = -1;
        startTime = Time.time + introDuration;
        AudioTime = offset - introDuration;
        isStart = true;
    }

    public void ResetStartTime()
    {
        previewFixedTime = false;
        IsPaused = false;
        offset = 0f;
        isStart = false;
        RestoreFrameRate();
    }

    public void SetPreviewTime(float previewTime)
    {
        RestoreFrameRate();
        previewFixedTime = true;
        IsPaused = false;
        isRecord = false;
        isStart = true;
        offset = previewTime;
        AudioTime = previewTime;
        speed = 1f;
        Time.captureFramerate = 0;
        Application.targetFrameRate = PlaybackFrameRate;
    }

    public void PausePlayback()
    {
        isStart = false;
        IsPaused = true;
    }

    public void RestoreFrameRate()
    {
        if (!isRecord)
            return;
        Application.targetFrameRate = PlaybackFrameRate;
        isRecord = false;
    }
}
