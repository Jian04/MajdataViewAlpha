using UnityEngine;

#nullable enable
public class DestroySelf : MonoBehaviour
{
    public bool ifDestroy;
    // Kept only so legacy animation clips can deserialize their old binding.
    // Recording stop ownership belongs to PlayAllPerfect/ScreenRecorder.
    public bool ifStopRecording;

    private void Update()
    {
        if (ifDestroy)
            Destroy(gameObject);
    }
}
