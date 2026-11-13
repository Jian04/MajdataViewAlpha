using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class WikiGifRecorder
{
    private const string Pending = "AlphaWikiGifRecorder";
    static WikiGifRecorder()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var probe = new GameObject("Wiki GIF recorder");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            probe.AddComponent<WikiGifRecorderProbe>();
        };
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
}

public sealed class WikiGifRecorderProbe : MonoBehaviour
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private Process encoder;
    private sealed class Example
    {
        public string id, chart, output;
        public float start, duration;
    }

    private IEnumerator Start()
    {
        var routine = Record();
        while (true)
        {
            bool moved;
            try { moved = routine.MoveNext(); }
            catch (Exception error)
            {
                if (encoder != null && !encoder.HasExited) encoder.Kill();
                Debug.LogException(error); EditorApplication.Exit(1); yield break;
            }
            if (!moved) break;
            yield return routine.Current;
        }
        Debug.Log("096 WIKI GIF RECORDING COMPLETE");
        EditorApplication.Exit(0);
    }

    private IEnumerator Record()
    {
        const int size = 512, frameRate = 24;
        var args = Environment.GetCommandLineArgs();
        var manifestAt = Array.IndexOf(args, "-alphaWikiManifest");
        var examples = JsonConvert.DeserializeObject<Example[]>(File.ReadAllText(args[manifestAt + 1]));
        var onlyAt = Array.IndexOf(args, "-alphaWikiOnly");
        var resume = Array.IndexOf(args, "-alphaWikiResume") >= 0;
        while (GameObject.Find("Notes") == null) yield return null;
        yield return null;
        var handler = FindAnyObjectByType<HttpHandler>();
        GameObject.Find("CanvasButtons")?.SetActive(false);
        var camera = Camera.main;
        var target = new RenderTexture(size, size, 24); target.Create();
        camera.targetTexture = target; camera.enabled = false; camera.aspect = 1;
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        var pixels = new byte[size * size * 3];
        var encoderType = typeof(ScreenRecorder).Assembly.GetType("RecordingEncoder");
        var executable = (string)encoderType.GetMethod("ResolveExecutable", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        var buildArguments = encoderType.GetMethod("BuildGifArguments", BindingFlags.Static | BindingFlags.NonPublic);
        Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
        foreach (var example in examples)
        {
            if (onlyAt >= 0 && example.id != args[onlyAt + 1]) continue;
            if (resume && File.Exists(example.output) && new FileInfo(example.output).Length > 0) continue;
            var folder = Path.GetDirectoryName(example.chart);
            Directory.CreateDirectory(Path.GetDirectoryName(example.output));
            // HttpHandler obtains the post-effect controller through Camera.main.
            camera.enabled = true;
            typeof(HttpHandler).GetField("request", Hidden).SetValue(handler, JsonConvert.SerializeObject(new
            {
                control = 0, jsonPath = example.chart, chartDirectory = folder, startTime = example.start,
                deferPlaybackStart = true,
                audioSpeed = 1f, noteSpeed = 7f, touchSpeed = 7.5f, starSpeed = 1f,
                judgeLineStyle = 2, showSongDetail = false, showAllPerfect = false,
                showJudgeInfo = true, showComboInfo = true, showJudgeText = true,
                standbyTheme = "dark", standbyBackgroundColor = "#000000", introBgTheme = "default",
                showPausedJudgeAreas = true, smoothSlideAnime = true, starDisplayMode = 1
            }));
            var deadline = Time.realtimeSinceStartup + 60;
            do
            {
                yield return null;
                camera.Render();
                if (Time.realtimeSinceStartup > deadline) throw new Exception("Wiki preview preparation timed out: " + example.id);
            } while ((string)typeof(HttpHandler).GetField("request", Hidden).GetValue(handler) != "" ||
                     (bool)typeof(HttpHandler).GetField("deferredStartCompletion", Hidden).GetValue(handler));
            if ((int)typeof(HttpHandler).GetField("responseStatusCode", Hidden).GetValue(handler) != 200)
                throw new Exception((string)typeof(HttpHandler).GetField("responseBody", Hidden).GetValue(handler));
            camera.enabled = false;
            var loader = FindAnyObjectByType<JsonDataLoader>();
            if (loader.DroppedBeats.Count > 0) throw new Exception("Wiki example has dropped notes: " + example.id);
            foreach (var note in GameObject.Find("Notes").GetComponentsInChildren<NoteDrop>(true)) note.previewOnly = true;
            foreach (var line in GameObject.Find("Notes").GetComponentsInChildren<EachLineDrop>(true)) line.previewOnly = true;
            var clock = FindAnyObjectByType<AudioTimeProvider>();
            var display = FindAnyObjectByType<DisplayTimelineController>();
            var media = FindAnyObjectByType<MediaTimelineController>();
            var background = FindAnyObjectByType<BGManager>();
            background.LoadBGFromPath(folder, 1f, 0f, 0f, false, 0, true);
            var arguments = (string)buildArguments.Invoke(null, new object[] { size, size, frameRate, example.output });
            encoder = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true
            });
            var diagnostics = encoder.StandardError.ReadToEndAsync();
            var frameCount = (int)Math.Ceiling(example.duration * frameRate);
            using (var input = encoder.StandardInput.BaseStream)
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var time = example.start + (float)frame / frameRate;
                    clock.SetPausedTimelineTime(time);
                    display.SetPausedTimelineTime(time); media.SetPausedTimelineTime(time); background.SetPausedTimelineTime(time);
                    yield return null;
                    camera.Render();
                    var previous = RenderTexture.active; RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                    RenderTexture.active = previous;
                    texture.GetRawTextureData<byte>().CopyTo(pixels);
                    input.Write(pixels, 0, pixels.Length);
                    if (frame == frameRate * 2)
                        File.WriteAllBytes(Path.Combine(folder, example.id + ".png"), texture.EncodeToPNG());
                }
            deadline = Time.realtimeSinceStartup + 30;
            while (!encoder.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
            if (!encoder.HasExited) { encoder.Kill(); throw new Exception("Wiki encoder timed out: " + example.id); }
            if (encoder.ExitCode != 0) throw new Exception("Wiki encoder failed: " + example.id + "\n" + diagnostics.Result);
            Debug.Log($"096 WIKI GIF: {example.id}, {frameCount} frames, {new FileInfo(example.output).Length} bytes");
            encoder.Dispose(); encoder = null;
        }
        camera.targetTexture = null; target.Release(); Destroy(target); Destroy(texture);
    }
}
