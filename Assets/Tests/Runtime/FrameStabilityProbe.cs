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
        var previousStage = "";
        var stageWatch = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            var stageLoader = FindAnyObjectByType<JsonDataLoader>();
            var stageBg = FindAnyObjectByType<BGManager>();
            var stageMedia = FindAnyObjectByType<MediaTimelineController>();
            var stage = $"notes={stageLoader?.RuntimeBindingsReady}, bg={stageBg?.IsPreparedForRecording}, media={stageMedia?.IsPrepared}";
            if (stage != previousStage) { Debug.Log($"FRAME PREPARE {stageWatch.ElapsedMilliseconds}ms {stage}"); previousStage = stage; }
            yield return null;
            Camera.main?.Render();
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
        // Hidden windows can skip swap-chain rendering. Render the gameplay camera
        // offscreen and read one pixel back so GPU work is included in this test.
        var camera = Camera.main;
        var target = new RenderTexture(980, 980, 24);
        target.Create();
        camera.targetTexture = target;
        camera.enabled = false;
        var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        var arguments = Environment.GetCommandLineArgs();
        var chartArgument = Array.IndexOf(arguments, "-alphaStressChart");
        var startArgument = Array.IndexOf(arguments, "-alphaStressStart");
        var startTime = startArgument >= 0 ? float.Parse(arguments[startArgument + 1], System.Globalization.CultureInfo.InvariantCulture) : 2f;
        var durationArgument = Array.IndexOf(arguments, "-alphaStressSeconds");
        var sampleUntil = startTime + (durationArgument >= 0 ? int.Parse(arguments[durationArgument + 1]) : 18);
        var modeArgument = Array.IndexOf(arguments, "-alphaStressMode");
        var modes = modeArgument >= 0 ? new[] { int.Parse(arguments[modeArgument + 1]) } : new[] { 0, 1 };
        var path = Path.GetFullPath(chartArgument >= 0 && chartArgument + 1 < arguments.Length
            ? arguments[chartArgument + 1]
            : ".codex-build/oct11-stress-chart.json");
        var results = new List<object>();
        Debug.Log($"FRAME DEVICE: {SystemInfo.processorType}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}; {Screen.width}x{Screen.height}; frameTiming={FrameTimingManager.IsFeatureEnabled()}");
        foreach (var mode in modes)
        {
            var loadWatch = System.Diagnostics.Stopwatch.StartNew();
            var prepare = Send(new { control = 0, jsonPath = path, chartDirectory = Path.GetDirectoryName(path),
                startTime, noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f, editorPlayMethod = mode,
                deferPlaybackStart = true, showSongDetail = false, showAllPerfect = false, starDisplayMode = 1,
                pinkStar = Array.IndexOf(arguments, "-alphaVerify093") >= 0 });
            while (prepare.MoveNext()) yield return prepare.Current;
            Debug.Log("FRAME LOAD ms=" + loadWatch.ElapsedMilliseconds);
            var loader = FindAnyObjectByType<JsonDataLoader>();
            if (Array.IndexOf(arguments, "-alphaVerify093") >= 0)
            {
                VerifyInherited093();
                yield break;
            }
            if (Array.IndexOf(arguments, "-alphaVerify094") >= 0)
            {
                VerifyInherited094();
                yield break;
            }
            if (Array.IndexOf(arguments, "-alphaVerify095") >= 0)
            {
                VerifyInherited095();
                yield break;
            }
            foreach (var dropped in loader.DroppedBeats) Debug.Log("FRAME DROPPED " + JsonConvert.SerializeObject(dropped));
            var objects = GameObject.Find("Notes").GetComponentsInChildren<NoteDrop>(true).Length;
            var resume = Send(new { control = 4, startAt = DateTime.Now.AddSeconds(.25).Ticks,
                startTime, audioSpeed = 1f, editorPlayMethod = mode, showSongDetail = false, showAllPerfect = false });
            while (resume.MoveNext()) yield return resume.Current;
            if (arguments.Contains("-alphaJudgeStall"))
            {
                for (var beat = 1f; beat <= 4.5f; beat += .5f)
                {
                    while (clock.AudioTime < beat - .045f) yield return null;
                    System.Threading.Thread.Sleep(80);
                }
                while (clock.AudioTime < 4.8f) yield return null;
                var counter = FindAnyObjectByType<ObjectCounter>();
                var resultsField = typeof(ObjectCounter).GetField("judgedTapCount", Fields);
                Debug.Log("STALL TAP JUDGMENTS " + JsonConvert.SerializeObject(resultsField.GetValue(counter)));
                yield break;
            }
            Application.targetFrameRate = 120;
            while (clock.AudioTime < startTime + 4f) yield return null;
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
            var frames = new List<double>(4096);
            var main = new List<double>(4096);
            var render = new List<double>(4096);
            var sorting = new List<double>(4096);
            var gpu = new List<double>(4096);
            var cpuRender = new List<double>(4096);
            var forcedRender = new List<double>(4096);
            var spikes = new List<(float time, float milliseconds)>();
            var renderWatch = new System.Diagnostics.Stopwatch();
            var timing = new FrameTiming[1];
            long bytes = 0;
            using (var mainRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (var renderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1))
            using (var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            using (var sortRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Alpha.StarDisplayOrder", 1))
            {
                Debug.Log($"FRAME RECORDERS: main={mainRecorder.Valid}, render={renderRecorder.Valid}, gc={gcRecorder.Valid}, sort={sortRecorder.Valid}; objects={objects}");
                while (clock.AudioTime < sampleUntil)
                {
                    yield return null;
                    renderWatch.Restart();
                    camera.Render();
                    previousTarget = RenderTexture.active;
                    RenderTexture.active = target;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    RenderTexture.active = previousTarget;
                    renderWatch.Stop();
                    forcedRender.Add(renderWatch.Elapsed.TotalMilliseconds);
                    frames.Add(Time.unscaledDeltaTime * 1000d);
                    if (Time.unscaledDeltaTime > .033334f)
                        spikes.Add((clock.AudioTime, Time.unscaledDeltaTime * 1000f));
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
            foreach (var spike in spikes.OrderByDescending(frame => frame.milliseconds).Take(8))
                Debug.Log($"FRAME SPIKE at {spike.time:F3}s: {spike.milliseconds:F1}ms");
        }
        File.WriteAllText(".codex-build/oct11-frame-latest.json", JsonConvert.SerializeObject(results, Formatting.Indented));
    }
    private static object Stats(List<double> samples)
    {
        samples.Sort();
        return new { p50 = samples[(int)(samples.Count * .5)], p95 = samples[(int)(samples.Count * .95)],
            p99 = samples[(int)(samples.Count * .99)], worst = samples[^1], mean = samples.Average() };
    }

    private static void VerifyInherited093()
    {
        var carriers = FindObjectsByType<TrajectoryCarrierDrop>(FindObjectsSortMode.None);
        if (carriers.Length != 5) throw new Exception("093 expected five visual carriers, got " + carriers.Length);
        foreach (var carrier in carriers.Where(note => note.exSprite != null))
        {
            var ex = carrier.transform.Find("EX")?.GetComponent<SpriteRenderer>();
            if (ex == null || ex.sprite != carrier.exSprite) throw new Exception("093 missing EX sleeve");
            if (carrier.carrierVisualType == "star" && !carrier.isEach && !carrier.bodyBreak && ex.color != StarDrop.PinkStarExColor)
                throw new Exception("093 pink star sleeve tint mismatch");
        }
        var holds = carriers.Where(note => note.carrierVisualType == "hold").OrderBy(note => note.holdLength).ToArray();
        if (holds.Length != 2 || Mathf.Abs(holds[1].holdLength - holds[0].holdLength * 2) > .001f)
            throw new Exception("093 Hold duration/length ratio mismatch");
        var pose = typeof(TrajectoryCarrierDrop).GetMethod("SetPose", Fields);
        var evaluate = typeof(TrajectoryCarrierDrop).GetMethod("Evaluate", Fields);
        foreach (var hold in holds)
        {
            pose.Invoke(hold, new object[] { .3f });
            var head = (Vector3)evaluate.Invoke(hold, new object[] { .3f });
            var direction = hold.transform.localRotation * Vector3.up;
            var actualHead = hold.transform.localPosition + direction * (hold.holdLength * hold.transform.localScale.y * .5f);
            if ((actualHead - head).sqrMagnitude > .000001f) throw new Exception("093 Hold head left the borrowed route");
            if (Mathf.Abs(hold.GetComponent<SpriteRenderer>().size.y - 1.4f - hold.holdLength) > .001f)
                throw new Exception("093 Hold body did not retain its length");
        }
        var picture = FindObjectsByType<StarDrop>(FindObjectsSortMode.None).FirstOrDefault(note => note.customSkin == "images/head.png");
        if (picture == null || picture.GetComponent<SpriteRenderer>().sprite.texture.width != 32)
            throw new Exception("093 Slide head did not use its inherited image");
        var imageTap = FindObjectsByType<TapDrop>(FindObjectsSortMode.None).FirstOrDefault(note => note.customSkin == "images/head.png");
        if (imageTap == null || !imageTap.isEX || imageTap.transform.GetChild(0).GetComponent<SpriteRenderer>().sprite == null)
            throw new Exception("093 image-skinned Tap lost its native EX sleeve");
        Debug.Log("093 INHERIT PASS: EX sleeves, pink star tint, Hold lengths/head anchors, ordinary Slide head image.");
    }

    private static void VerifyInherited095()
    {
        var holds = FindObjectsByType<TrajectoryCarrierDrop>(FindObjectsSortMode.None).Where(note => note.carrierVisualType == "hold").OrderBy(note => note.time).ToArray();
        if (holds.Length != 5) throw new Exception("095 expected five inherited Hold fixtures, got " + holds.Length);
        var type = typeof(TrajectoryCarrierDrop);
        var pose = type.GetMethod("SetPose", Fields);
        var evaluate = type.GetMethod("Evaluate", Fields);
        foreach (var hold in holds)
        {
            var distances = (List<float>)type.GetField("distances", Fields).GetValue(hold);
            var largestJump = 0f;
            foreach (var distance in distances.Skip(1).SkipLast(1))
            {
                var at = distance / distances[^1];
                pose.Invoke(hold, new object[] { at - .000001f });
                var before = hold.transform.localRotation.eulerAngles.z;
                pose.Invoke(hold, new object[] { at + .000001f });
                largestJump = Mathf.Max(largestJump, Mathf.Abs(Mathf.DeltaAngle(before, hold.transform.localRotation.eulerAngles.z)));
            }
            var rotation = 0f; var previous = 0f;
            for (var i = 0; i <= 4096; i++)
            {
                var at = i / 4096f;
                pose.Invoke(hold, new object[] { at });
                var angle = hold.transform.localRotation.eulerAngles.z;
                if (float.IsNaN(angle)) throw new Exception("095 invalid Hold angle");
                if (i > 0) rotation += Mathf.DeltaAngle(previous, angle);
                previous = angle;
                var head = (Vector3)evaluate.Invoke(hold, new object[] { at });
                var actual = hold.transform.localPosition + hold.transform.up * (hold.holdLength * hold.transform.localScale.y * .5f);
                if ((head - actual).sqrMagnitude > .000001f) throw new Exception("095 inherited Hold head left the route");
                pose.Invoke(hold, new object[] { 1f - at });
                pose.Invoke(hold, new object[] { at });
                if (Mathf.Abs(Mathf.DeltaAngle(angle, hold.transform.localRotation.eulerAngles.z)) > .001f)
                    throw new Exception("095 Hold direction depends on seek history");
            }
            if (largestJump > .1f) throw new Exception("095 discontinuous Hold tangent: " + largestJump);
            Debug.Log($"095 HOLD PASS: nodes={distances.Count}, vertex jump={largestJump}, cumulative rotation={rotation}; deterministic reverse/fast seek, exact head anchor.");
        }
    }

    private static void VerifyInherited094()
    {
        var hold = FindObjectsByType<TrajectoryCarrierDrop>(FindObjectsSortMode.None).Single(note => note.carrierVisualType == "hold");
        var type = typeof(TrajectoryCarrierDrop);
        var distances = (List<float>)type.GetField("distances", Fields).GetValue(hold);
        var total = distances[^1];
        var pose = type.GetMethod("SetPose", Fields);
        var evaluate = type.GetMethod("Evaluate", Fields);
        var largestJump = 0f;
        foreach (var distance in distances.Skip(1).SkipLast(1))
        {
            var at = distance / total;
            pose.Invoke(hold, new object[] { at - .00001f });
            var before = hold.transform.localRotation.eulerAngles.z;
            pose.Invoke(hold, new object[] { at + .00001f });
            var after = hold.transform.localRotation.eulerAngles.z;
            largestJump = Mathf.Max(largestJump, Mathf.Abs(Mathf.DeltaAngle(before, after)));
            var head = (Vector3)evaluate.Invoke(hold, new object[] { at + .00001f });
            var actual = hold.transform.localPosition + hold.transform.up * (hold.holdLength * hold.transform.localScale.y * .5f);
            if ((head - actual).sqrMagnitude > .000001f) throw new Exception("094 Hold judgement head left the original path");
        }
        if (largestJump > .1f) throw new Exception("094 discontinuous inherited Hold angle: " + largestJump);
        Debug.Log("094 HOLD PASS: angle jump across every 1<5 polyline vertex <= " + largestJump + " degrees; original head path retained.");
    }
}

#endif
