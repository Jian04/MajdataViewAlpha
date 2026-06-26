using Assets.Scripts.Types;
using UnityEngine;

#nullable enable
public class PlayAllPerfect : MonoBehaviour
{
    private GameObject Allperfect;
    private AudioTimeProvider timeProvider;
    private JsonDataLoader loader;
    private bool showAllPerfect = true;

    private void Start()
    {
        loader = FindAnyObjectByType<JsonDataLoader>();
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        Allperfect = GameObject.Find("CanvasAllPerfect");
        Allperfect.SetActive(false);
    }

    private void Update()
    {
        if (!showAllPerfect || Allperfect == null || timeProvider == null)
            return;
        if (loader != null && loader.State is not (NoteLoaderStatus.Idle or NoteLoaderStatus.Finished))
            return;

        // Keep the original 4.3.1 behavior: View only reveals the AP canvas.
        // Audio timing and stop timing stay owned by MajdataEdit.
        if (timeProvider.isStart && transform.childCount == 0)
            Allperfect.SetActive(true);
    }

    public void Configure(bool visible)
    {
        showAllPerfect = visible;
        if (!showAllPerfect && Allperfect != null)
            Allperfect.SetActive(false);
    }

    public void PreviewNow()
    {
        if (showAllPerfect && Allperfect != null)
            Allperfect.SetActive(true);
    }
}
