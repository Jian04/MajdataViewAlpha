using UnityEngine;

#nullable enable
public class DestroySelf : MonoBehaviour
{
    public bool ifDestroy;
    public bool ifStopRecording;

    private void Update()
    {
        // Recording is finalized explicitly by PlayAllPerfect. Keeping the
        // serialized field preserves existing animation bindings.
        if (ifDestroy)
            Destroy(gameObject);
    }
}
