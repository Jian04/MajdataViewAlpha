using System;
using System.Collections;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

// Only Launcher-originated windows take part in the paired entrance animation.
public static class StartupWindowPresentation
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    internal static IntPtr WindowHandle;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Prepare()
    {
        var pid = Process.GetCurrentProcess().Id;
        var area = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != pid || !GetClientRect(window, out var bounds)) return true;
            var size = (bounds.Right - bounds.Left) * (bounds.Bottom - bounds.Top);
            if (size > area) { area = size; WindowHandle = window; }
            return true;
        }, IntPtr.Zero);
        var session = MajdataCore.AlphaSession.Current;
        if (WindowHandle == IntPtr.Zero || session == null || session.IsBackgroundWorker || !HasEntranceAnimation) return;
        // Cloaking keeps native chrome hidden even when Unity shows/repositions
        // the player again during initialization. Rendering continues normally.
        var hidden = 1;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(WindowHandle, 13, ref hidden, sizeof(int)));
    }
    internal static bool HasEntranceAnimation => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MAJDATA_STARTUP_ORIGIN"));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void HideBeforeSplash() => Prepare();
    internal static void Reveal()
    {
        ShowWindow(WindowHandle, 4);
        var hidden = 0;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(WindowHandle, 13, ref hidden, sizeof(int)));
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void WaitForFirstFrame()
    {
        if (MajdataCore.AlphaSession.Current == null) return;
        Prepare();
        var go = new GameObject("Startup frame acknowledgement");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<StartupFrameAcknowledgement>();
    }
    private delegate bool WindowVisitor(IntPtr window, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visit, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Bounds bounds);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
#endif
}

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
public sealed class StartupFrameAcknowledgement : MonoBehaviour
{
    private bool revealing;
    private void Update()
    {
        if (revealing || StartupWindowPresentation.WindowHandle == IntPtr.Zero) return;
        var path = Path.Combine(MajdataCore.AlphaSession.Current.DirectoryPath, "startup-show");
        if (!File.Exists(path)) return;
        revealing = true;
        StartCoroutine(RevealAndAcknowledge(path));
    }
    private IEnumerator RevealAndAcknowledge(string path)
    {
        // Acknowledge the visible frame, so Edit keeps its overlay until View
        // has rendered after uncloaking. DWM only permits uncloaking our own HWND.
        StartupWindowPresentation.Reveal();
        yield return new WaitForEndOfFrame();
        File.Delete(path); Destroy(gameObject);
    }
    private IEnumerator Start()
    {
        var session = MajdataCore.AlphaSession.Current;
        while (StartupWindowPresentation.WindowHandle == IntPtr.Zero) { StartupWindowPresentation.Prepare(); yield return null; }
        File.WriteAllText(Path.Combine(session.DirectoryPath, "view-window"), StartupWindowPresentation.WindowHandle.ToInt64().ToString());
        while (Camera.main == null || GameObject.Find("Background") == null ||
            UnityEngine.Object.FindAnyObjectByType<HttpHandler>()?.ReadyForEditor != true) yield return null;
        File.WriteAllText(Path.Combine(session.DirectoryPath, "view-ready"), Process.GetCurrentProcess().Id.ToString());
        if (session.IsBackgroundWorker || !StartupWindowPresentation.HasEntranceAnimation)
        {
            Destroy(gameObject); yield break;
        }
        var request = Path.Combine(session.DirectoryPath, "startup-capture");
        while (!revealing && (!File.Exists(request) ||
               UnityEngine.Object.FindAnyObjectByType<HttpHandler>()?.HasEditorDisplaySettings != true)) yield return null;
        if (revealing) yield break;
        yield return new WaitForEndOfFrame();
        if (revealing) yield break;
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            var target = Path.Combine(session.DirectoryPath, "startup-frame.png");
            File.WriteAllBytes(target + ".tmp", image.EncodeToPNG());
            File.Move(target + ".tmp", target);
        }
        finally { Destroy(image); }
    }
}
#endif
