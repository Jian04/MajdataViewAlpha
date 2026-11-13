using MajdataCore;
using System;
using System.Collections.Generic;
using UnityEngine;

// IMGUI badge is outside the chart scene and is hidden whenever a recording starts.
public sealed class AlphaInstanceBadge : MonoBehaviour
{
    public static volatile bool Recording;
    public static long RecordingGeneration;
    private static readonly List<string> dynamicMuriDetails = new();
    private static string[] publishedDetails = Array.Empty<string>();
    private System.Threading.Timer heartbeat;

    internal static void AddDynamicMuri(string detail)
    {
        lock (dynamicMuriDetails) dynamicMuriDetails.Add(detail);
    }

    internal static void ResetDynamicMuri()
    {
        lock (dynamicMuriDetails)
        {
            dynamicMuriDetails.Clear();
            publishedDetails = Array.Empty<string>();
        }
    }

    private void Awake()
    {
        Application.runInBackground = true;
        heartbeat = new System.Threading.Timer(_ => {
            string[] details;
            lock (dynamicMuriDetails)
            {
                if (publishedDetails.Length != dynamicMuriDetails.Count)
                    publishedDetails = dynamicMuriDetails.ToArray();
                details = publishedDetails;
            }
            try { AlphaSession.Current?.PublishViewState(Recording, RecordingGeneration, details.Length, details); }
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
