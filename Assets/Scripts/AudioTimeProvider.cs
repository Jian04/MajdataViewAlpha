using System;
using UnityEngine;

public class AudioTimeProvider : MonoBehaviour
{
    private const int PlaybackFrameRate = 120;
    public float AudioTime; //notes get this value
    public bool isStart;
    public bool isRecord;
    public float offset;
    private float speed;
    private bool previewFixedTime;

    private float startTime;
    private long ticks;
    private int previousTargetFrameRate;

    public float CurrentSpeed => isRecord ? Time.timeScale : speed;
    public bool PlaybackStarted => isStart && (isRecord
        ? Time.time >= startTime
        : Time.realtimeSinceStartup >= startTime);

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
                AudioTime = (Time.realtimeSinceStartup - startTime) * speed + offset;
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
        ticks = _ticks;
        offset = _offset;
        AudioTime = offset;
        var dateTime = new DateTime(ticks);
        var seconds = (dateTime - DateTime.Now).TotalSeconds;
        isRecord = _isRecord;
        if (_isRecord)
        {
            startTime = Time.time + 5;
            Time.timeScale = _speed;
            previousTargetFrameRate = Application.targetFrameRate;
            // captureFramerate controls exported time. Do not also throttle
            // real rendering, otherwise low resolutions cannot render faster.
            Application.targetFrameRate = -1;
            Time.captureFramerate = recordFrameRate;
        }
        else
        {
            startTime = Time.realtimeSinceStartup + (float)seconds;
            speed = _speed;
            Application.targetFrameRate = PlaybackFrameRate;
            Time.captureFramerate = 0;
        }

        isStart = true;
    }

    public void ResetStartTime()
    {
        previewFixedTime = false;
        offset = 0f;
        isStart = false;
        RestoreFrameRate();
    }

    public void SetPreviewTime(float previewTime)
    {
        RestoreFrameRate();
        previewFixedTime = true;
        isRecord = false;
        isStart = true;
        offset = previewTime;
        AudioTime = previewTime;
        speed = 1f;
        Time.captureFramerate = 0;
        Application.targetFrameRate = PlaybackFrameRate;
    }

    public void RestoreFrameRate()
    {
        if (!isRecord)
            return;
        Application.targetFrameRate = PlaybackFrameRate;
        isRecord = false;
    }
}
