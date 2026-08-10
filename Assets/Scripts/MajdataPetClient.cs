using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// Direct HTTP client dedicated to localhost (127.0.0.1).
// System proxies such as Clash or v2rayN can intercept UnityWebRequest loopback calls.
// If the proxy does not route them back locally, they silently time out, breaking
// View note generation and desktop-pet integration with repeated Curl error 28 logs.
// Explicitly disable proxies and route all local View calls through this client.
public static class LocalHttp
{
    public static readonly System.Net.Http.HttpClient Client = Create();

    private static System.Net.Http.HttpClient Create()
    {
        // Set only UseProxy=false. Unity's MonoWebRequestHandler throws after setting Proxy
        // when UseProxy is false:
        // InvalidOperationException("Operation is not valid due to the current
        // state of the object"). This breaks LocalHttp static initialization and disables
        // MajdataPetClient.Trigger and VisualChartEditor.SendNote. The resulting
        // TypeInitializationException occurs before the caller's ContinueWith error handling.
        // .NET Core/Desktop in Edit does not have this restriction, so the implementations differ.
        var handler = new System.Net.Http.HttpClientHandler { UseProxy = false };
        return new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
    }
}

public sealed class MajdataPetClient : MonoBehaviour
{
    private const string Endpoint = "http://127.0.0.1:8015/pet";
    private static MajdataPetClient instance;
    private static bool initialNotificationSent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        EnsureInstance();
        if (initialNotificationSent)
            return;
        initialNotificationSent = true;
        Trigger("jump", "View is awake");
    }

    public static void Trigger(string action, string message = null)
    {
        Send(action, message, null);
    }

    public static void Look(float angleDegrees, string message = null)
    {
        Send("look", message, angleDegrees);
    }

    public static void ChartAgent(string message = "Writing chart ideas...")
    {
        Trigger("chart-agent", message);
    }

    public static void StarCombo(string message = "Checking star combinations...")
    {
        Trigger("star-combo", message);
    }

    public static void Waiting(string message = "Waiting for your cue...")
    {
        Trigger("waiting", message);
    }

    public static void Failed(string message = "Something needs attention.")
    {
        Trigger("failed", message);
    }

    private static MajdataPetClient EnsureInstance()
    {
        if (instance != null)
            return instance;

        var obj = new GameObject("MajdataPetClient");
        DontDestroyOnLoad(obj);
        instance = obj.AddComponent<MajdataPetClient>();
        return instance;
    }

    // Do not use UnityWebRequest: it follows system proxies and cannot reach the local pet when one is enabled
    private static void Send(string action, string message, float? angle)
    {
        var url = $"{Endpoint}?action={Uri.EscapeDataString(action ?? string.Empty)}";
        if (!string.IsNullOrWhiteSpace(message))
            url += $"&message={Uri.EscapeDataString(message)}";
        if (angle.HasValue)
            url += $"&angle={angle.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        _ = LocalHttp.Client.GetAsync(url).ContinueWith(task =>
        {
            // The desktop pet is often offline, so ignore failures
            _ = task.Exception;
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
