using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Collections;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DjAutoOverlapVerification
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static void BuildCheckedView() { Run(); AlphaCommandLineBuild.BuildView(); }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        var loader = UnityEngine.Object.FindAnyObjectByType<JsonDataLoader>();
        typeof(JsonDataLoader).GetMethod("Start", Fields).Invoke(loader, null);
        typeof(JsonDataLoader).GetField("timeProvider", Fields).SetValue(loader,
            UnityEngine.Object.FindAnyObjectByType<AudioTimeProvider>());
        var registry = GameObject.Find("Notes").GetComponent<NoteManager>();
        registry.Clear();
        var indices = (Dictionary<int, int>)typeof(JsonDataLoader).GetField("noteIndex", Fields).GetValue(loader);
        for (int i = 1; i <= 16; i++) indices[i] = 0;
        var assembly = typeof(JsonDataLoader).Assembly;
        var timingType = assembly.GetType("SimaiTimingPoint");
        var noteType = assembly.GetType("SimaiNote");
        var timing = JsonConvert.DeserializeObject("{\"time\":1,\"HSpeed\":1}", timingType);
        var instantiate = typeof(JsonDataLoader).GetMethod("InstantiateWifi", Fields);
        var previousReload = HttpHandler.IsReloding;
        HttpHandler.IsReloding = true;
        try
        {
            // The supplied 二次元凶 chart contains 8xw4[8:1]*w4[8:1].
            // Its second branch is a path without another hittable head.
            foreach (var noHead in new[] { false, true, true })
            {
                var json = "{\"startPosition\":8,\"noteContent\":\"8w4[8:1]\",\"pathExpression\":\"8w4[8:1]\",\"slideStartTime\":1.476190476,\"slideTime\":0.238095238,\"isSlideNoHead\":" + noHead.ToString().ToLowerInvariant() + "}";
                instantiate.Invoke(loader, new[] { timing, JsonConvert.DeserializeObject(json, noteType) });
            }
            var heads = registry.GetComponentsInChildren<StarDrop>(true);
            if (heads.Length != 3 || heads.Count(h => h.isNoHead) != 2)
                throw new Exception("Overlap setup did not create one real head and two headless branches.");
            if (registry.noteOrder.Count != 1 || indices[8] != 1 ||
                heads.Where(h => h.isNoHead).Any(h => registry.noteOrder.ContainsKey(h.gameObject)))
                throw new Exception("Headless Wifi left an unfinishable entry in the A8 input queue.");
            var next = new GameObject("Tap after overlapping Wifi");
            next.transform.SetParent(registry.transform);
            registry.AddNote(next, indices[8]++);
            registry.noteIndex[8] = 1; // The only visible head has been judged.
            if (!registry.CanJudge(next, 8))
                throw new Exception("Tap after overlapping Wifi is blocked by a ghost head.");
            typeof(JsonDataLoader).GetField("restoringBeat", Fields).SetValue(loader, true);
            instantiate.Invoke(loader, new[] { timing, JsonConvert.DeserializeObject("{\"startPosition\":8,\"noteContent\":\"8w4[8:1]\",\"pathExpression\":\"8w4[8:1]\",\"slideStartTime\":1.476190476,\"slideTime\":0.238095238}", noteType) });
            if (registry.noteOrder.Count != 2 || indices[8] != 2)
                throw new Exception("Restoring a Wifi sustain must not enqueue its already passed head.");
            foreach (Transform child in registry.transform.Cast<Transform>().ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            registry.Clear();
            for (int i = 1; i <= 16; i++) indices[i] = 0;
            typeof(JsonDataLoader).GetField("restoringBeat", Fields).SetValue(loader, false);
            var chart = (IEnumerable)JsonConvert.DeserializeObject(
                File.ReadAllText(Path.Combine(Application.dataPath, "../.codex-build/oct11-overlap-chart.json")), timingType.MakeArrayType());
            int wifiCount = 0, headCount = 0;
            foreach (var beat in chart)
                foreach (var note in (IEnumerable)timingType.GetField("noteList").GetValue(beat))
                {
                    var path = (string)noteType.GetField("pathExpression").GetValue(note);
                    if (path == null || !path.Contains("w")) continue;
                    instantiate.Invoke(loader, new[] { beat, note });
                    wifiCount++;
                    if (!(bool)noteType.GetField("isSlideNoHead").GetValue(note)) headCount++;
                }
            if (wifiCount <= headCount || registry.noteOrder.Count != headCount)
                throw new Exception("Supplied chart's headless Wifi branches changed the hittable head count.");
            for (int key = 1; key <= 8; key++)
            {
                var nextTap = new GameObject("Following tap " + key);
                nextTap.transform.SetParent(registry.transform);
                registry.noteIndex[key] = indices[key];
                registry.AddNote(nextTap, indices[key]++);
                if (!registry.CanJudge(nextTap, key))
                    throw new Exception("Supplied chart blocked later taps on key " + key);
            }
            Debug.Log($"DJAUTO CHART PASS: 二次元凶, {wifiCount} Wifi paths / {headCount} hittable heads, following taps remain judgeable on all eight keys.");
            Debug.Log("DJAUTO OVERLAP PASS: supplied chart's overlapping Wifi, one judged head, no ghost entries, following A8 tap remains judgeable, restored sustain excludes its head.");
        }
        finally
        {
            foreach (Transform child in registry.transform.Cast<Transform>().ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            HttpHandler.IsReloding = previousReload;
        }
    }
}
