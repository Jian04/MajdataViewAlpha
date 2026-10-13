#if UNITY_EDITOR || ALPHA_FRAME_VERIFICATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Profiling;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
public sealed class FrameStabilityProbe : MonoBehaviour
{
#if ALPHA_FRAME_VERIFICATION && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        var probe = new GameObject("Frame stability verification");
        DontDestroyOnLoad(probe);
        probe.AddComponent<FrameStabilityProbe>();
    }
#endif
    private static void Exit(int code)
    {
#if UNITY_EDITOR
        EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private HttpHandler handler;
    private IEnumerator Start()
    {
        var routine = Verify();
        while (true)
        {
            bool moved;
            try { moved = routine.MoveNext(); }
            catch (Exception exception) { Debug.LogException(exception); Exit(1); yield break; }
            if (!moved) break;
            yield return routine.Current;
        }
        Debug.Log("FRAME STABILITY MEASUREMENT COMPLETE");
        Exit(0);
    }
    private IEnumerator Send(object packet)
    {
        typeof(HttpHandler).GetField("request", Fields).SetValue(handler, JsonConvert.SerializeObject(packet));
        var deadline = Time.realtimeSinceStartup + 120;
        do
        {
            yield return null;
            if (Time.realtimeSinceStartup > deadline) throw new Exception("Stress View request timed out");
        } while ((string)typeof(HttpHandler).GetField("request", Fields).GetValue(handler) != "" ||
                 (bool)typeof(HttpHandler).GetField("deferredStartCompletion", Fields).GetValue(handler));
        if ((int)typeof(HttpHandler).GetField("responseStatusCode", Fields).GetValue(handler) != 200)
            throw new Exception((string)typeof(HttpHandler).GetField("responseBody", Fields).GetValue(handler));
    }
    private IEnumerator Verify()
    {
        while (GameObject.Find("Notes") == null) yield return null;
        yield return null;
        handler = FindAnyObjectByType<HttpHandler>();
        var clock = FindAnyObjectByType<AudioTimeProvider>();
        Application.runInBackground = true;
#if !UNITY_EDITOR
        // Hidden windows can skip swap-chain rendering. Render the gameplay camera
        // offscreen and read one pixel back so GPU work is included in this test.
        var camera = Camera.main;
        var target = new RenderTexture(980, 980, 24);
        target.Create();
        camera.targetTexture = target;
        camera.enabled = false;
        var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
#endif
        var path = Path.GetFullPath(".codex-build/oct11-stress-chart.json");
        var results = new List<object>();
        Debug.Log($"FRAME DEVICE: {SystemInfo.processorType}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}; {Screen.width}x{Screen.height}; frameTiming={FrameTimingManager.IsFeatureEnabled()}");
        foreach (var mode in new[] { 0, 1 })
        {
            var prepare = Send(new { control = 0, jsonPath = path, chartDirectory = Path.GetDirectoryName(path),
                startTime = 2f, noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f, editorPlayMethod = mode,
                deferPlaybackStart = true, showSongDetail = false, showAllPerfect = false, starDisplayMode = 1 });
            while (prepare.MoveNext()) yield return prepare.Current;
            var objects = GameObject.Find("Notes").GetComponentsInChildren<NoteDrop>(true).Length;
            var resume = Send(new { control = 4, startAt = DateTime.Now.AddSeconds(.25).Ticks,
                startTime = 2f, audioSpeed = 1f, editorPlayMethod = mode, showSongDetail = false, showAllPerfect = false });
            while (resume.MoveNext()) yield return resume.Current;
            Application.targetFrameRate = 120;
            while (clock.AudioTime < 6f) yield return null;
#if !UNITY_EDITOR
            camera.Render();
            var previousTarget = RenderTexture.active;
            RenderTexture.active = target;
            var snapshot = new Texture2D(980, 980, TextureFormat.RGBA32, false);
            snapshot.ReadPixels(new Rect(0, 0, 980, 980), 0, 0, false);
            File.WriteAllBytes(".codex-build/oct11-frame-render-" + mode + ".png", snapshot.EncodeToPNG());
            Destroy(snapshot);
            RenderTexture.active = previousTarget;
            // Exclude screenshot readback and PNG encoding from frame samples.
            for (var warmup = 0; warmup < 10; warmup++) yield return null;
#endif
            var frames = new List<double>(4096);
            var main = new List<double>(4096);
            var render = new List<double>(4096);
            var sorting = new List<double>(4096);
            var gpu = new List<double>(4096);
            var cpuRender = new List<double>(4096);
            var forcedRender = new List<double>(4096);
            var renderWatch = new System.Diagnostics.Stopwatch();
            var timing = new FrameTiming[1];
            long bytes = 0;
            using (var mainRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (var renderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1))
            using (var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            using (var sortRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Alpha.StarDisplayOrder", 1))
            {
                Debug.Log($"FRAME RECORDERS: main={mainRecorder.Valid}, render={renderRecorder.Valid}, gc={gcRecorder.Valid}, sort={sortRecorder.Valid}; objects={objects}");
                while (clock.AudioTime < 20f)
                {
                    yield return null;
#if !UNITY_EDITOR
                    renderWatch.Restart();
                    camera.Render();
                    previousTarget = RenderTexture.active;
                    RenderTexture.active = target;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    RenderTexture.active = previousTarget;
                    renderWatch.Stop();
                    forcedRender.Add(renderWatch.Elapsed.TotalMilliseconds);
#endif
                    frames.Add(Time.unscaledDeltaTime * 1000d);
                    main.Add(mainRecorder.LastValue / 1000000d);
                    render.Add(renderRecorder.LastValue / 1000000d);
                    sorting.Add(sortRecorder.LastValue / 1000000d);
                    bytes += gcRecorder.LastValue;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0)
                    {
                        gpu.Add(timing[0].gpuFrameTime);
                        cpuRender.Add(timing[0].cpuRenderThreadFrameTime);
                    }
                }
            }
            var result = new { mode, objects, frames = frames.Count, frameMs = Stats(frames), mainMs = Stats(main),
                renderMs = Stats(render), sortMs = Stats(sorting), allocatedBytesPerFrame = bytes / Math.Max(1, frames.Count),
                gpuMs = gpu.Count > 0 ? Stats(gpu) : null, cpuRenderMs = cpuRender.Count > 0 ? Stats(cpuRender) : null,
                forcedRenderReadbackMs = forcedRender.Count > 0 ? Stats(forcedRender) : null,
                over16ms = frames.Count(value => value > 16.667), over33ms = frames.Count(value => value > 33.334) };
            results.Add(result);
            Debug.Log("FRAME RESULT " + JsonConvert.SerializeObject(result));
        }
        File.WriteAllText(".codex-build/oct11-frame-latest.json", JsonConvert.SerializeObject(results, Formatting.Indented));
    }
    private static object Stats(List<double> samples)
    {
        samples.Sort();
        return new { p50 = samples[(int)(samples.Count * .5)], p95 = samples[(int)(samples.Count * .95)],
            p99 = samples[(int)(samples.Count * .99)], worst = samples[^1], mean = samples.Average() };
    }
}

#endif
