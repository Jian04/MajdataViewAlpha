using UnityEngine;

#nullable enable
public class DestroySelf : MonoBehaviour
{
    public bool ifDestroy;
    public bool ifStopRecording;
    // After the AP presentation, keep recording three seconds of empty footage on the virtual clock
    private float stopDelay = -1f;

    private void Update()
    {
        if (ifStopRecording)
        {
            if (stopDelay < 0f)
                stopDelay = 3f;
            stopDelay -= Time.deltaTime;
            if (stopDelay <= 0f)
                GameObject.Find("ScreenRecorder").GetComponent<ScreenRecorder>().StopRecording();
        }
        if (ifDestroy)
            Destroy(gameObject);
    }
}
