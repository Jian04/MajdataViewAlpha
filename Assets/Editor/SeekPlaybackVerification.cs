using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class SeekPlaybackVerification
{
    const string Pending = "AlphaSeekVerification";
    static SeekPlaybackVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var probe = new GameObject("Seek verification");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            probe.AddComponent<SeekPlaybackProbe>();
        };
    }
    public static void RunOct12()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        SessionState.SetString("AlphaSeekVerificationChart", ".codex-build/oct12-sustain-chart.json");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    public static void RunShake()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        SessionState.SetString("AlphaSeekVerificationChart", ".codex-build/oct12-megalovania/view.json");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    public static void RunWifi()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        SessionState.SetString("AlphaSeekVerificationChart", ".codex-build/oct12-wifi-chart.json");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
}

public sealed class SeekPlaybackProbe : MonoBehaviour
{
    private sealed class ShakeChart
    {
        public string filePath;
        public System.Collections.Generic.List<EffectChange> effectTable;
    }
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private HttpHandler handler;
    private int runtimeErrors;
    private void CountRuntimeError(string message, string stack, LogType type)
    {
        if (type is LogType.Error or LogType.Assert or LogType.Exception) runtimeErrors++;
    }
    private IEnumerator Start()
    {
        Application.logMessageReceived += CountRuntimeError;
        var routine = Verify();
        while (true)
        {
            bool moved;
            try { moved = routine.MoveNext(); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); yield break; }
            if (!moved) break;
            yield return routine.Current;
        }
        Application.logMessageReceived -= CountRuntimeError;
        if (runtimeErrors != 0)
        {
            Debug.LogError($"Seek regression emitted {runtimeErrors} runtime errors.");
            EditorApplication.Exit(1);
            yield break;
        }
        Debug.Log(SessionState.GetString("AlphaSeekVerificationChart", "").Contains("oct12-megalovania")
            ? "SHAKE LIVE PREVIEW PASS: four actual View preview reloads, shader axes correct; no runtime errors."
            : "SEEK PLAYBACK PASS: actual paused preview -> async binding -> shared Continue; no runtime errors.");
        EditorApplication.Exit(0);
    }
    private IEnumerator Send(object packet)
    {
        var json = JsonConvert.SerializeObject(packet);
        GameObject[] previousPreview = null;
        if (Application.isBatchMode && (int)Newtonsoft.Json.Linq.JObject.Parse(json)["control"] == 8)
        {
            var notes = GameObject.Find("Notes").transform;
            previousPreview = new GameObject[notes.childCount];
            for (var i = 0; i < notes.childCount; i++) previousPreview[i] = notes.GetChild(i).gameObject;
        }
        typeof(HttpHandler).GetField("request", Fields).SetValue(handler, json);
        var deadline = Time.realtimeSinceStartup + 30;
        do
        {
            yield return null;
            // Editor batch mode never resumes WaitForEndOfFrame. Render the ready
            // replacement, then pump the real retirement iterator for this boundary.
            var loader = FindAnyObjectByType<JsonDataLoader>();
            if (previousPreview != null && loader.PreviewReplacementInProgress && loader.RuntimeBindingsReady)
            {
                Camera.main.Render();
                var generation = typeof(JsonDataLoader).GetField("reloadGeneration", Fields).GetValue(loader);
                var retire = (IEnumerator)typeof(JsonDataLoader).GetMethod("RetirePreviousPreviewAtEndOfFrame", Fields)
                    .Invoke(loader, new object[] { previousPreview, generation });
                while (retire.MoveNext()) { }
                previousPreview = null;
            }
            if (Time.realtimeSinceStartup > deadline) throw new Exception("View request timed out");
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
        var counter = FindAnyObjectByType<ObjectCounter>();
        var loader = FindAnyObjectByType<JsonDataLoader>();
        var path = Path.GetFullPath(SessionState.GetString("AlphaSeekVerificationChart", ".codex-build/oct11-seek-chart.json"));
        var folder = Path.GetDirectoryName(path);
        if (path.EndsWith("oct12-megalovania" + Path.DirectorySeparatorChar + "view.json"))
        {
            var chart = JsonConvert.DeserializeObject<ShakeChart>(File.ReadAllText(path));
            foreach (var effect in chart.effectTable)
            {
                var time = (float)effect.time + .05f;
                var preview = Send(new { control = 8, jsonPath = path, chartDirectory = chart.filePath,
                    startTime = time, noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f,
                    editorPlayMethod = 1, showSongDetail = false, showAllPerfect = false });
                while (preview.MoveNext()) yield return preview.Current;
                var camera = Camera.main;
                camera.Render(); camera.Render();
                var controller = camera.GetComponent<ScreenEffectController>();
                var material = (Material)typeof(ScreenEffectController).GetField("material", Fields).GetValue(controller);
                var x = material.GetFloat("_OffsetX"); var y = material.GetFloat("_OffsetY");
                var vertical = Mathf.Abs(effect.paramB - Mathf.PI / 2) < .001f;
                if (Mathf.Abs(vertical ? x : y) > .00001f || Mathf.Abs(vertical ? y : x) < .00001f)
                    throw new Exception($"Live preview Shake direction wrong: {time}, ({x}, {y})");
                Debug.Log($"LIVE PREVIEW SHAKE PASS: t={time}, angle={effect.paramB * Mathf.Rad2Deg}, offset=({x},{y})");
            }
            yield break;
        }
        if (path.EndsWith("oct12-wifi-chart.json"))
        {
            var wifiCases = VerifyWifi(path, folder, clock, counter, loader);
            while (wifiCases.MoveNext()) yield return wifiCases.Current;
            yield break;
        }
        foreach (var offset in new[] { 0.18f, 0.5f, 0.02f })
        {
            var preview = Send(new { control = 8, jsonPath = path, chartDirectory = folder, startTime = offset,
                noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f, editorPlayMethod = 1, showSongDetail = false, showAllPerfect = false });
            while (preview.MoveNext()) yield return preview.Current;
            var prepare = Send(new { control = 4, jsonPath = path, chartDirectory = folder, startTime = offset,
                noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f, editorPlayMethod = 1,
                deferPlaybackStart = true, showSongDetail = false, showAllPerfect = false });
            while (prepare.MoveNext()) yield return prepare.Current;
            if (!loader.RuntimeBindingsReady || clock.isStart || Math.Abs(clock.AudioTime - offset) > .001)
                throw new Exception("Preview preparation advanced clock or acknowledged incomplete bindings");
            var anchor = DateTime.Now.AddSeconds(.25);
            var clockOffset = offset;
            var resume = Send(new { control = 4, startAt = anchor.Ticks, startTime = offset, audioSpeed = 1f,
                editorPlayMethod = 1, showSongDetail = false, showAllPerfect = false });
            while (resume.MoveNext()) yield return resume.Current;
            if (offset == .18f)
            {
                while (clock.AudioTime < 1.5f) yield return null;
                var pause = Send(new { control = 3 });
                while (pause.MoveNext()) yield return pause.Current;
                clockOffset = clock.AudioTime;
                anchor = DateTime.Now.AddSeconds(.25);
                var ordinaryResume = Send(new { control = 4, startAt = anchor.Ticks, startTime = clockOffset,
                    audioSpeed = 1f, editorPlayMethod = 1, showSongDetail = false, showAllPerfect = false });
                while (ordinaryResume.MoveNext()) yield return ordinaryResume.Current;
            }
            while (clock.AudioTime < 4.3f) yield return null;
            var expected = clockOffset + (DateTime.Now - anchor).TotalSeconds;
            if (Math.Abs(clock.AudioTime - expected) > .04) throw new Exception("View lost shared clock anchor");
            var missed = Convert.ToInt32(typeof(ObjectCounter).GetField("missCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(counter));
            if (path.EndsWith("oct12-sustain-chart.json"))
            {
                var good = (long)typeof(ObjectCounter).GetField("goodCount", Fields).GetValue(counter);
                var great = (long)typeof(ObjectCounter).GetField("greatCount", Fields).GetValue(counter);
                if (good != 0 || great != 0) throw new Exception($"Restored sustain lost hold time: {good} Good, {great} Great");
                Debug.Log("DJAUTO SUSTAIN PLAYBACK PASS: actual preview/prepare/start/pause/resume pipeline; no Good, Great or Miss.");
            }
            if (missed != 0 || !counter.AllFinished)
                throw new Exception($"Seek {offset}: {missed} Miss; finished={counter.AllFinished}");
            Debug.Log($"SEEK CASE PASS: t={offset}, no Miss, all subsequent notes completed, clock error {clock.AudioTime - expected:F4}s");
        }
        var label = GameObject.Find("FPS Text").GetComponent<Text>();
        if (label.font != counter.DisplayFont || !label.enabled) throw new Exception("FPS font or visibility incorrect");
        clock.isRecord = true;
        yield return null;
        if (label.enabled) throw new Exception("FPS visible while recording");
        clock.isRecord = false;
        var overlapPath = Path.GetFullPath(".codex-build/oct11-overlap-runtime.json");
        var overlapPrepare = Send(new { control = 0, jsonPath = overlapPath, chartDirectory = folder,
            startTime = 0f, noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f, editorPlayMethod = 1,
            deferPlaybackStart = true, showSongDetail = false, showAllPerfect = false });
        while (overlapPrepare.MoveNext()) yield return overlapPrepare.Current;
        var overlapResume = Send(new { control = 4, startAt = DateTime.Now.AddSeconds(.25).Ticks,
            startTime = 0f, audioSpeed = 1f, editorPlayMethod = 1, showSongDetail = false, showAllPerfect = false });
        while (overlapResume.MoveNext()) yield return overlapResume.Current;
        while (clock.AudioTime < 5.2f) yield return null;
        if (counter.tapCount != counter.tapSum || !counter.AllFinished ||
            Convert.ToInt32(typeof(ObjectCounter).GetField("missCount", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(counter)) != 0)
            throw new Exception("Supplied overlapping Wifi paths blocked subsequent keys during actual playback");
        foreach (var sensor in FindObjectsByType<Sensor>(FindObjectsSortMode.None))
            if (sensor.Status != Assets.Scripts.Types.SensorStatus.Off)
                throw new Exception("Overlapping Wifi left occupied sensor " + sensor.Type);
        Debug.Log("OVERLAP PLAYBACK PASS: both actual chart Wifi groups and following eight keys complete with no Miss; all sensors released.");
        var update = (Action)Delegate.CreateDelegate(typeof(Action), counter,
            typeof(ObjectCounter).GetMethod("Update", Fields));
        for (var i = 0; i < 10; i++) update();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) update();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (allocated != 0) throw new Exception("Unchanged score display allocated " + allocated + " bytes");
        var table = (Text)typeof(ObjectCounter).GetField("table", Fields).GetValue(counter);
        var previousText = table.text;
        counter.tapSum++;
        update();
        if (table.text == previousText) throw new Exception("Changed note totals did not refresh display");
        counter.tapSum--;
        InputManager.Mode = Assets.Scripts.Types.AutoPlayMode.Enable;
        update();
        if (!table.text.Contains("MOD: Enable")) throw new Exception("Changed DJAuto mode did not refresh display");
        Debug.Log("SCORE DISPLAY PASS: unchanged frames allocate 0 bytes; note totals and playback mode still refresh immediately.");
        var display = FindAnyObjectByType<DisplayTimelineController>();
        var outline = GameObject.Find("Outline").GetComponent<SpriteRenderer>();
        display.SetPlaybackActive(false);
        display.SetImmediateDisplay(1, true, true, true, 0, 0);
        yield return new WaitForSecondsRealtime(.3f);
        foreach (var style in new[] { 3, 2, 3, 1, 0, 3 })
        {
            var previous = outline.sprite;
            display.SetImmediateDisplay(style, true, true, true, 0, 0);
            if (style != 0 && previous == outline.sprite)
                throw new Exception("Judge-line style did not replace the image: " + style);
            yield return new WaitForSecondsRealtime(.1f);
            // An isolated note preview refreshes standby while settings are fading.
            display.SetPlaybackActive(false);
            if (style != 0 && (outline.color.a <= .05f || outline.color.a >= .95f))
                throw new Exception("Judge-line style did not fade in during standby: " + style);
            yield return new WaitForSecondsRealtime(.2f);
            if (style != 0 && Math.Abs(outline.color.a - 1f) > .001f)
                throw new Exception("Judge-line transition did not complete: " + style);
            var outgoing = (SpriteRenderer)typeof(DisplayTimelineController).GetField("fadingJudgeLine", Fields).GetValue(display);
            if (outgoing != null && !outgoing.forceRenderingOff)
                throw new Exception("Outgoing judge-line image remained visible");
        }
        display.SetImmediateDisplay(1, true, true, true, 0, 0);
        display.SetPausedTimelineTime(clock.AudioTime);
        var cancelled = (SpriteRenderer)typeof(DisplayTimelineController).GetField("fadingJudgeLine", Fields).GetValue(display);
        if (cancelled != null && !cancelled.forceRenderingOff)
            throw new Exception("Seeking left a stale judge-line transition");
        Debug.Log("JUDGE STYLE PASS: standby crossfade normal/magical/area/off in both directions; seek cancels the outgoing layer.");

    }

    private IEnumerator VerifyWifi(string path, string folder, AudioTimeProvider clock, ObjectCounter counter, JsonDataLoader loader)
    {
        var chart = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
        var duration = (float)chart["timingList"][0]["noteList"][0]["slideTime"];
        foreach (var offset in new[] { 0f, duration * .775f, duration * .525f, duration * .925f, duration * .275f })
        {
            // Start with a later preview, then drag backwards into the sustain.
            foreach (var previewTime in new[] { duration * .975f, offset })
            {
                var preview = Send(new { control = 8, jsonPath = path, chartDirectory = folder, startTime = previewTime,
                    noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f, editorPlayMethod = 1, showSongDetail = false, showAllPerfect = false });
                while (preview.MoveNext()) yield return preview.Current;
            }
            var prepare = Send(new { control = 4, jsonPath = path, chartDirectory = folder, startTime = offset,
                noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f, editorPlayMethod = 1,
                deferPlaybackStart = true, showSongDetail = false, showAllPerfect = false });
            while (prepare.MoveNext()) yield return prepare.Current;
            if (!loader.RuntimeBindingsReady || clock.isStart) throw new Exception("WiFi bindings not prepared");
            var resume = Send(new { control = 4, startAt = DateTime.Now.AddSeconds(.25).Ticks, startTime = offset,
                audioSpeed = 1f, editorPlayMethod = 1, showSongDetail = false, showAllPerfect = false });
            while (resume.MoveNext()) yield return resume.Current;
            if (offset == duration * .525f)
            {
                while (clock.AudioTime < offset + .15f) yield return null;
                var pause = Send(new { control = 3 });
                while (pause.MoveNext()) yield return pause.Current;
                var continueWifi = Send(new { control = 4, startAt = DateTime.Now.AddSeconds(.25).Ticks,
                    startTime = clock.AudioTime, audioSpeed = 1f, editorPlayMethod = 1,
                    showSongDetail = false, showAllPerfect = false });
                while (continueWifi.MoveNext()) yield return continueWifi.Current;
            }
            while (clock.AudioTime < duration + .9f) yield return null;
            if (counter.slideSum != 1 || counter.slideCount != 1 || counter.tapSum != 1 || counter.tapCount != 1)
                throw new Exception("WiFi fixture did not exercise the sustain and the following tap");
            var good = Convert.ToInt64(typeof(ObjectCounter).GetField("goodCount", Fields).GetValue(counter));
            var great = Convert.ToInt64(typeof(ObjectCounter).GetField("greatCount", Fields).GetValue(counter));
            var miss = Convert.ToInt64(typeof(ObjectCounter).GetField("missCount", Fields).GetValue(counter));
            if (good != 0 || great != 0 || miss != 0 || !counter.AllFinished)
                throw new Exception($"WiFi rewind {offset}: Good={good}, Great={great}, Miss={miss}, finished={counter.AllFinished}");
            Debug.Log($"WIFI REWIND CASE PASS: {offset}; no Good, Great or Miss, all completed.");
        }
        Debug.Log("WIFI REWIND PASS: actual DJAuto preview/prepare/resume, including final section and backwards seek.");
    }
}
