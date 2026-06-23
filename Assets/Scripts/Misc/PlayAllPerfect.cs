using Assets.Scripts.Types;
using System.Collections;
using UnityEngine;

#nullable enable
public class PlayAllPerfect : MonoBehaviour
{
    private GameObject Allperfect;
    private Animator allPerfectAnimator;
    private AudioTimeProvider timeProvider;
    private JsonDataLoader loader;
    private bool sequenceStarted;

    private void Start()
    {
        loader = FindAnyObjectByType<JsonDataLoader>();
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        Allperfect = GameObject.Find("CanvasAllPerfect");
        allPerfectAnimator = Allperfect.GetComponent<Animator>();
        Allperfect.SetActive(false);
    }

    private void Update()
    {
        if (loader == null)
            return;
        if (loader.State is not (NoteLoaderStatus.Idle or NoteLoaderStatus.Finished))
            return;
        if (!timeProvider.isStart || transform.childCount != 0 || Allperfect == null || sequenceStarted)
            return;

        sequenceStarted = true;
        GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>()?.ResetAllEffects();
        Allperfect.SetActive(true);
        StartCoroutine(FinishSequence());
    }

    private IEnumerator FinishSequence()
    {
        // Stop on the actual final animation frame rather than maintaining
        // a second hard-coded duration that can drift from the clip.
        yield return null;
        if (allPerfectAnimator != null)
        {
            while (allPerfectAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                yield return null;
        }
        Allperfect.SetActive(false);

        if (!timeProvider.isRecord)
            yield break;

        var recorder = GameObject.Find("ScreenRecorder")?.GetComponent<ScreenRecorder>();
        if (recorder != null && recorder.IsRecording)
            recorder.StopRecording();
    }
}
