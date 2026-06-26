using UnityEngine;

#nullable enable
public class DestroySelf : MonoBehaviour
{
    public bool ifDestroy;
    public bool ifStopRecording;

    private void Update()
    {
        if (ifStopRecording)
            GameObject.Find("ScreenRecorder").GetComponent<ScreenRecorder>().StopRecording();
        if (ifDestroy)
            Destroy(gameObject);
    }
}
