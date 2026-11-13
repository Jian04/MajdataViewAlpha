using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MajdataCore;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class Reported085Verification
{
    static Reported085Verification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Preview085", false)) return;
            SessionState.SetBool("Preview085", false);
            var probe = new GameObject("085 preview verification");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            probe.AddComponent<Preview085Probe>();
        };
    }
    public static void RunPreview()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        SessionState.SetBool("Preview085", true);
        EditorApplication.EnterPlaymode();
    }
    public static void RunAndBuild()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var loader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        if (!SlidePathParser.TryParsePath("1-B2>C>7[1:1]", out var parsed)) throw new Exception("Mixed path parse");
        var inherited = new List<Vector3>();
        var geometry = typeof(TrajectoryCarrierDrop).Assembly.GetType("TrajectoryPathGeometry");
        if (!(bool)geometry.GetMethod("TryBuild", BindingFlags.Public | BindingFlags.Static).Invoke(null,
            new object[] { loader, parsed.segments, inherited })) throw new Exception("Carrier path build");
        var go = new GameObject("085 route");
        try
        {
            var touch = go.AddComponent<TouchSlideDrop>(); touch.pathSegments = parsed.segments;
            touch.slideRouteSource = loader;
            typeof(TouchSlideDrop).GetMethod("BuildPath", flags).Invoke(touch, null);
            var ordinary = (List<Vector3>)typeof(TouchSlideDrop).GetField("path", flags).GetValue(touch);
            foreach (var point in inherited)
            {
                var distance = float.MaxValue;
                foreach (var sample in ordinary) distance = Mathf.Min(distance, (sample - point).magnitude);
                if (distance > .0001f) throw new Exception("Inherited path differs from TouchSlide: " + point + " distance=" + distance);
            }
            if (!inherited.Exists(point => point.sqrMagnitude < .00001f)) throw new Exception("C was not at the origin");
            var source = System.IO.Path.Combine(Application.dataPath, "../Skin/dx/star.png");
            var a = SpriteLoader.LoadSpriteFromFile(source); var b = SpriteLoader.LoadSpriteFromFile(source);
            if (a != b) throw new Exception("Unchanged skin was decoded twice");
            var skin = UnityEngine.Object.FindAnyObjectByType<CustomSkin>(FindObjectsInactive.Include); skin.LoadSkin("dx");
            var star = go.AddComponent<StarDrop>();
            var body = go.AddComponent<SpriteRenderer>(); var ex = new GameObject("ex").AddComponent<SpriteRenderer>();
            ex.transform.SetParent(go.transform);
            typeof(Assets.Scripts.Notes.TapBase).GetField("spriteRenderer", flags).SetValue(star, body);
            typeof(Assets.Scripts.Notes.TapBase).GetField("exSpriteRender", flags).SetValue(star, ex);
            star.RefreshSkin(skin);
            if (body.sprite != skin.Star) throw new Exception("Star did not refresh immediately");
            star.isDouble = true; star.isBreak = true; star.RefreshSkin(skin);
            if (body.sprite != skin.Star_Break_Double) throw new Exception("Double break skin refresh");
            var clock = go.AddComponent<AudioTimeProvider>();
            clock.SetStartTime(DateTime.Now.AddSeconds(45).Ticks, 0, 1, true, 60);
            clock.BeginRecordingCapture(5);
            if (Mathf.Abs(clock.AudioTime + 5) > .0001f) throw new Exception("Recording intro consumed preparation time");
            clock.ResetStartTime();
            Debug.Log("085 VERIFY PASS: shared C path, decoded skin reuse, immediate star skin, offline intro clock");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
        AlphaCommandLineBuild.BuildView();
    }
}

public sealed class Preview085Probe : MonoBehaviour
{
    private IEnumerator Start()
    {
        var routine = Verify();
        while (true)
        {
            bool next;
            try { next = routine.MoveNext(); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); yield break; }
            if (!next) break;
            yield return routine.Current;
        }
        Debug.Log("085 PREVIEW PASS: 12 paused chart replacements retain visible notes; SetDisplay refresh does not clear them.");
        EditorApplication.Exit(0);
    }
    private IEnumerator Verify()
    {
        while (GameObject.Find("Notes") == null) yield return null;
        yield return null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sender = gameObject.AddComponent<SeekPlaybackProbe>(); sender.enabled = false;
        typeof(SeekPlaybackProbe).GetField("handler", flags).SetValue(sender, FindAnyObjectByType<HttpHandler>());
        var send = typeof(SeekPlaybackProbe).GetMethod("Send", flags);
        var original = File.ReadAllText(".codex-build/oct12-sustain-chart.json");
        var path = Path.GetFullPath(".codex-build/085-preview.json");
        var notes = GameObject.Find("Notes");
        bool Visible() => notes.GetComponentsInChildren<SpriteRenderer>().Any(renderer =>
            renderer.enabled && !renderer.forceRenderingOff && renderer.color.a > .05f && renderer.sprite != null);
        for (var index = 0; index < 12; index++)
        {
            var chart = Newtonsoft.Json.Linq.JObject.Parse(original);
            chart["title"] = "Edit " + index;
            File.WriteAllText(path, chart.ToString(Formatting.None));
            var request = new { control = 8, jsonPath = path, chartDirectory = Path.GetDirectoryName(path),
                startTime = .5f, noteSpeed = 7f, touchSpeed = 7.5f, audioSpeed = 1f, editorPlayMethod = 1,
                showSongDetail = false, showAllPerfect = false };
            var pending = (IEnumerator)send.Invoke(sender, new object[] { request });
            while (pending.MoveNext())
            {
                yield return pending.Current;
                if (index > 0 && !Visible()) throw new Exception("All preview notes disappeared during replacement " + index);
            }
            yield return null;
            if (!Visible()) throw new Exception("Paused Hold not visible after replacement " + index);
        }
        var display = (IEnumerator)send.Invoke(sender, new object[] { new { control = 6, skin = "dx", starSkin = "sd" } });
        while (display.MoveNext()) { yield return display.Current; if (!Visible()) throw new Exception("Display refresh hid notes"); }
    }
}
