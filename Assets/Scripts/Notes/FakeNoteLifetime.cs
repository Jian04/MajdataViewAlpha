using UnityEngine;

[DisallowMultipleComponent]
public sealed class FakeNoteLifetime : MonoBehaviour
{
    private NoteDrop note;
    private AudioTimeProvider timeProvider;

    private void Awake()
    {
        note = GetComponent<NoteDrop>();
        timeProvider = FindAnyObjectByType<AudioTimeProvider>();
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
        if (timeProvider.AudioTime > note.time + note.TailDuration + NoteDrop.MissWindow)
            Destroy(gameObject);
    }
}
