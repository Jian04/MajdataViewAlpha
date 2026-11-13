using UnityEngine;
using Assets.Scripts.Notes;

[DisallowMultipleComponent]
public sealed class FakeNoteLifetime : MonoBehaviour
{
    private const int TapDisposalsPerFrame = 32;
    private static int disposalFrame = -1;
    private static int disposedTaps;
    private NoteDrop note;
    private AudioTimeProvider timeProvider;

    private void Awake()
    {
        note = GetComponent<NoteDrop>();
        timeProvider = note.timeProvider;
    }

    private void Update()
    {
        if (note == null)
        {
            Destroy(this);
            return;
        }

        timeProvider ??= FindAnyObjectByType<AudioTimeProvider>();
        if (timeProvider == null || (!timeProvider.isStart && !timeProvider.IsPaused))
            return;
        // Timeline-preview notes must remain reversible in both running and
        // paused preview; the loader owns their lifetime on reload/stop.
        if (note.previewOnly)
            return;

        // Nothing judges a fake note, so this stands in for the branch that
        // would have destroyed it, and it has to use that branch's own window:
        // a made-up one longer than a miss is how a fake note ends up flying
        // well past the ring before it goes.
        if (timeProvider.AudioTime <= note.time + note.TailDuration + NoteDrop.MissWindow)
            return;

        if (note is TapBase tap && !timeProvider.isRecord)
        {
            // A visual burst can expire hundreds of note/guide pairs on one beat.
            // Hide at the original cutoff and spread native destruction over frames.
            if (disposalFrame != Time.frameCount)
            {
                disposalFrame = Time.frameCount;
                disposedTaps = 0;
            }
            if (disposedTaps >= TapDisposalsPerFrame)
            {
                if (tap.enabled) tap.HideExpiredFake();
                return;
            }
            disposedTaps++;
        }
        Destroy(gameObject);
    }
}
