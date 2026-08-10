using Assets.Scripts.Types;
using UnityEngine;

#nullable enable
public class PlayAllPerfect : MonoBehaviour
{
    private GameObject Allperfect;
    private AudioTimeProvider timeProvider;
    private JsonDataLoader loader;
    private bool showAllPerfect = true;
    private bool recordingStopScheduled;
    private const float FallbackAnimationDuration = 3.1166666f;

    private void Start()
    {
        loader = FindAnyObjectByType<JsonDataLoader>();
        timeProvider = GameObject.Find("AudioTimeProvider")?.GetComponent<AudioTimeProvider>();
        Allperfect = GameObject.Find("CanvasAllPerfect");
        Allperfect?.SetActive(false);
    }

    private void Update()
    {
        if (!showAllPerfect || Allperfect == null || timeProvider == null)
            return;
        if (loader != null && loader.State is not (NoteLoaderStatus.Idle or NoteLoaderStatus.Finished))
            return;

        if (timeProvider.isStart && transform.childCount == 0)
        {
            var firstReveal = !Allperfect.activeSelf;
            Allperfect.SetActive(true);
            if (firstReveal && timeProvider.isRecord && !recordingStopScheduled)
            {
                var recorder = FindAnyObjectByType<ScreenRecorder>();
                if (recorder != null)
                {
                    // Animator and AudioTimeProvider both advance on Unity's capture clock.
                    // Scheduling from the reveal frame therefore ends exactly three seconds
                    // after the actual AP clip, independent of chart/song length.
                    recorder.StopAfter(GetAnimationDuration() + 3f);
                    recordingStopScheduled = true;
                }
            }
        }
    }

    public void Configure(bool visible)
    {
        showAllPerfect = visible;
        recordingStopScheduled = false;
        if (Allperfect != null)
            Allperfect.SetActive(false);
    }

    public void PreviewNow()
    {
        if (showAllPerfect && Allperfect != null)
            Allperfect.SetActive(true);
    }

    private float GetAnimationDuration()
    {
        var animator = Allperfect?.GetComponent<Animator>();
        var clips = animator?.runtimeAnimatorController?.animationClips;
        if (clips == null || clips.Length == 0)
            return FallbackAnimationDuration;

        var duration = 0f;
        foreach (var clip in clips)
            if (clip != null)
                duration = Mathf.Max(duration, clip.length);
        return duration > 0f ? duration : FallbackAnimationDuration;
    }
}
