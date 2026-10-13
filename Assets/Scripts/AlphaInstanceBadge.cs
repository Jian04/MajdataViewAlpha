using MajdataCore;
using UnityEngine;

// IMGUI badge is outside the chart scene and is hidden whenever a recording starts.
public sealed class AlphaInstanceBadge : MonoBehaviour
{
    public static volatile bool Recording;
    public static long RecordingGeneration;
    public static volatile int DynamicMuriCount;
    public static volatile string[] DynamicMuriDetails = new string[0];
    private System.Threading.Timer heartbeat;
    private void Awake()
    {
        Application.runInBackground = true;
        heartbeat = new System.Threading.Timer(_ => {
            try { AlphaSession.Current?.PublishViewState(Recording, RecordingGeneration, DynamicMuriCount, DynamicMuriDetails); }
            catch (System.IO.IOException) { }
            catch (System.UnauthorizedAccessException) { }
        }, null, 0, 500);
    }
    private void OnDestroy() => heartbeat?.Dispose();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Recording = false;
        if (AlphaSession.Current == null) return;
        using (var process = System.Diagnostics.Process.GetCurrentProcess()) AlphaSession.Current.Register(true, process);
        var obj = new GameObject("Alpha instance identity");
        DontDestroyOnLoad(obj); obj.AddComponent<AlphaInstanceBadge>();
    }
    private void OnGUI()
    {
        // Window identity is painted in native chrome by Edit, never in capture.
    }
}
