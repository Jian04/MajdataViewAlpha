using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using MajdataCore;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Reported061Verification
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, All).Invoke(target, args);
    public static void VerifyVisualEditing()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var loader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        var editor = new GameObject("Visual route verification").AddComponent<VisualChartEditor>();
        var camera = Camera.main;
        typeof(VisualChartEditor).GetField("dataLoader", All).SetValue(editor, loader);
        typeof(VisualChartEditor).GetField("mainCamera", All).SetValue(editor, camera);
        typeof(VisualChartEditor).GetField("sensorRoot", All).SetValue(editor, GameObject.Find("Sensors").transform);
        var observed = (List<Assets.Scripts.Types.SensorType>)typeof(VisualChartEditor).GetField("dragJudgePath", All).GetValue(editor);
        var screenPath = (List<Vector2>)typeof(VisualChartEditor).GetField("dragScreenPath", All).GetValue(editor);
        try
        {
            foreach (var expression in new[] { "1-5", "1p5", "1V35", "1B1B2B3B4B5K5", "8P91K1", "1B1B2B3B4B5B6B7B8B1K1" })
            {
                var areas = new List<SlideAreaRawData>();
                if (!loader.TryGetSlideRoute(expression, out var expected, out var positions, areas))
                    throw new Exception("Visual route fixture failed: " + expression);
                observed.Clear(); observed.AddRange(expected);
                screenPath.Clear(); foreach (var position in positions) screenPath.Add(camera.WorldToScreenPoint(position));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var generated = (string)Call(editor, "BuildSlideToken", expected[0], expected[^1]);
                if (!SlidePathParser.TryParsePath(generated, out var parsed) ||
                    !SlideSyntaxValidator.TryValidate(parsed, out var error))
                    throw new Exception("Visual editor generated invalid syntax: " + generated);
                if (expression.Contains('K') && string.IsNullOrEmpty(parsed.segments[0].slideCode) ||
                    !expression.Contains('K') && generated != expression + "[8:1]")
                    throw new Exception("Unexpected closest route for " + expression + ": " + generated);
                if (areas.Exists(area => area.SensorB >= 0))
                {
                    var alternate = new List<Assets.Scripts.Types.SensorType>(expected);
                    for (var index = 0; index < areas.Count; index++)
                        if (areas[index].SensorB >= 0) alternate[index] = (Assets.Scripts.Types.SensorType)areas[index].SensorB;
                    var distance = (float)typeof(VisualChartEditor).GetMethod("SensorPathDistance", All).Invoke(null,
                        new object[] { alternate, expected, areas });
                    if (distance != 0) throw new Exception("SlideCode alternate sensors must belong to the same judgement area");
                }
                Debug.Log($"VISUAL ROUTE PASS: {expression} -> {generated}, match={watch.Elapsed.TotalMilliseconds:F1}ms");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(editor.gameObject); }
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var loader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        var build = typeof(TrajectoryCarrierDrop).Assembly.GetType("TrajectoryPathGeometry").GetMethod("TryBuild", All);
        foreach (var expression in new[] { "5d-1d[1:2]", "5-1[1:2]" })
        {
            if (!SlidePathParser.TryParsePath(expression, out var parsed)) throw new Exception("Parse failed");
            var points = new List<Vector3>();
            if (!(bool)build.Invoke(null, new object[] { loader, parsed.segments, points })) throw new Exception("Build failed");
            if (expression.Contains("d"))
                foreach (var point in points)
                    if (Mathf.Abs(point.x) > .001f) throw new Exception("D5-D1 borrowed line bent off axis: " + point);
        }
        Debug.Log("REPORTED061 PATH PASS: D5-D1 stays on its own axis.");
        var editor = new GameObject("Pointer regression").AddComponent<VisualChartEditor>();
        typeof(VisualChartEditor).GetField("pointerGestureActive", All).SetValue(editor, true);
        Call(editor, "OnApplicationFocus", false);
        if ((bool)typeof(VisualChartEditor).GetField("pointerGestureActive", All).GetValue(editor)) throw new Exception("Focus loss must cancel stale gestures");
        UnityEngine.Object.DestroyImmediate(editor.gameObject);
        VerifyHold(loader);
        VerifyTouchHold();
        VerifyCover();
        Debug.Log("REPORTED061 VERIFICATION PASSED");
    }
    private static void VerifyHold(JsonDataLoader loader)
    {
        var clock = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var manager = GameObject.Find("Notes").GetComponent<NoteManager>();
        var counter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        foreach (var spawn in new[] { -4.8f, 0f, 1.225f, 4.8f, 8f })
        foreach (var sv in new[] { -1f, .5f, 1f, 2f })
        foreach (var scale in new[] { .5f, 1f, 2f })
        {
            SvController.Load(new List<SvPoint> { new SvPoint { time = 0, multiplier = sv } });
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Hold_01.prefab");
            var root = UnityEngine.Object.Instantiate(prefab);
            var hold = root.GetComponent<HoldDrop>();
            hold.startPosition = 1; hold.isDZone = true; hold.time = 5; hold.LastFor = 1;
            hold.spawnRadius = spawn; hold.speed = 7; hold.previewOnly = true; hold.noteScaleY = scale;
            hold.noteScrollPos = SvController.GetCumulativeScroll(5);
            Call(hold, "BindSceneContext", clock, counter, manager);
            Call(hold, "Start");
            clock.isStart = true;
            foreach (var at in new[] { 5.01f, 5.1f, 5.5f })
            {
                clock.AudioTime = at; Call(hold, "Update");
                var renderer = root.GetComponent<SpriteRenderer>();
                if (renderer.forceRenderingOff) continue;
                var head = root.transform.TransformPoint(new Vector3(0, renderer.size.y / 2f - .7f));
                if (Mathf.Abs(head.x) > .001f || Mathf.Abs(head.y - hold.destroyRadius) > .001f)
                    throw new Exception($"HOLD HEAD POSITION spawn={spawn} sv={sv} t={at}: {head}");
            }
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
    private static void VerifyTouchHold()
    {
        var clock = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var manager = GameObject.Find("Notes").GetComponent<NoteManager>();
        var counter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        if (GameObject.Find("Outline") == null) new GameObject("Outline").AddComponent<CustomSkin>();
        foreach (var sv in new[] { -1f, .5f, 1f, 2f })
        {
            SvController.Load(new List<SvPoint> { new SvPoint { time = 0, multiplier = sv } });
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/TouchHold.prefab");
            if (prefab == null) throw new Exception("TouchHold fixture prefab not found");
            var root = UnityEngine.Object.Instantiate(prefab);
            var hold = root.GetComponent<TouchHoldDrop>();
            hold.time = 5; hold.LastFor = 1; hold.speed = 7; hold.previewOnly = true;
            hold.noteScrollPos = SvController.GetCumulativeScroll(5);
            Call(hold, "BindSceneContext", clock, counter, manager); Call(hold, "Start");
            clock.AudioTime = 5.25f; Call(hold, "Update");
            var expected = Mathf.Clamp01(sv * .25f * .91f);
            if (Mathf.Abs(hold.mask.alphaCutoff - expected) > .001f) throw new Exception("TouchHold progress does not follow SV: " + sv);
            UnityEngine.Object.DestroyImmediate(root);
        }
        Debug.Log("REPORTED061 TOUCHHOLD PASS: progress follows SV; negative scroll is zero.");
    }

    public static void VerifyTouchRendering()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var loader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        var clock = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var outline = GameObject.Find("Outline") ?? new GameObject("Outline", typeof(SpriteRenderer));
        var skin = outline.GetComponent<CustomSkin>() ?? outline.AddComponent<CustomSkin>();
        skin.LoadSkin("dx");
        VerifyTouchHold();
        SvController.Load(new List<SvPoint>());
        clock.isStart = true;
        foreach (var (token, hold, isBreak) in new[] {
            ("B1~[1-5[8:1]]", false, false), ("B1b~[1-5[8:1]]", false, true),
            ("Ch[16:1]~[1-5[8:1]]", true, false), ("Chb[16:1]~[1-5[8:1]]", true, true)
        })
        {
            if (!NoteExpressionParser.TryParse(token, out var note, out var error) ||
                note.trajectory == null || note.IsHold != hold ||
                note.modifiers.HasHead(NoteModifierFlags.Break) != isBreak)
                throw new Exception($"Touch carrier parse failed: {token} {error}");
            var root = new GameObject(token, typeof(SpriteRenderer));
            var carrier = root.AddComponent<TrajectoryCarrierDrop>();
            carrier.carrierVisualType = hold ? "touchhold" : "touch";
            carrier.bodyBreak = isBreak;
            var touchSprite = isBreak ? skin.Touch_Break : skin.Touch;
            var holdSprites = isBreak ? skin.TouchHold_Break : skin.TouchHold;
            carrier.Configure(loader, note.trajectory.segments, hold ? holdSprites[0] : touchSprite,
                null, Vector2.one, 5f, 5f, 1f, 0, skin);
            Call(carrier, "Start");
            var point = root.transform.Find("Touch_Point(Clone)").GetComponent<SpriteRenderer>();
            var expectedPoint = isBreak ? skin.TouchPoint_Break : skin.TouchPoint;
            if (point.sprite == null || point.sprite != expectedPoint || point.transform.localPosition != Vector3.zero)
                throw new Exception("Wrong carrier centre point: " + token);
            clock.AudioTime = 5.5f;
            Call(carrier, "Update");
            for (var i = 0; i < 4; i++)
            {
                var petal = root.transform.Find("Petal" + i).GetComponent<SpriteRenderer>();
                if (petal.sprite == null || petal.sprite != (hold ? holdSprites[i] : touchSprite) || petal.forceRenderingOff)
                    throw new Exception("Wrong carrier petal: " + token);
            }
            if (point.forceRenderingOff)
                throw new Exception("The centre point must travel with the visible petals: " + token);
            if (hold)
            {
                var ring = root.transform.Find("TouchHold_Border(Clone)").GetComponent<SpriteRenderer>();
                var mask = root.GetComponentInChildren<SpriteMask>();
                var petal = root.transform.Find("Petal0");
                var petalPosition = petal.localPosition;
                if (ring.sprite == null || ring.sprite != holdSprites[4] || ring.maskInteraction != SpriteMaskInteraction.VisibleOutsideMask)
                    throw new Exception("Wrong progress ring for " + token);
                foreach (var sv in new[] { 1f, -1f })
                {
                    SvController.Load(new List<SvPoint> { new SvPoint { time = 0, multiplier = sv, noteType = "slide" } });
                    foreach (var fraction in new[] { 0f, .25f, 1f, .5f, 0f })
                    {
                        clock.AudioTime = 5f + fraction;
                        Call(carrier, "Update");
                        if (Mathf.Abs(mask.alphaCutoff - .91f * fraction) > .001f ||
                            ring.forceRenderingOff != (fraction == 0f) || petal.localPosition != petalPosition)
                            throw new Exception($"TouchHold carrier progress or petals differ: {token}, SV={sv}, elapsed={fraction}");
                    }
                }
                SvController.Load(new List<SvPoint>());
            }
            clock.AudioTime = 4.5f;
            Call(carrier, "Update");
            if (!point.forceRenderingOff)
                throw new Exception("The centre point must hide before the carrier appears: " + token);
            UnityEngine.Object.DestroyImmediate(root);
        }
        Debug.Log("111 TOUCH CARRIER PASS: TouchHold and Break ring fills over the borrowed duration even with negative Slide SV; petals stay open and seek rewinds the ring.");
    }

    public static void VerifyTapLineAlpha()
    {
        var loader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        var clock = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var timelineType = typeof(JsonDataLoader).GetMethod("BuildAlphaTimeline", All).GetParameters()[0].ParameterType;
        var timeline = (System.Collections.IList)Activator.CreateInstance(timelineType);
        var itemType = timelineType.GetGenericArguments()[0];
        foreach (var (type, time, alpha, live, reset) in new[] {
            ("", 0d, 0f, false, false), ("tapline", 2d, .25f, false, false),
            ("tapline", 3d, .5f, true, false), ("tapline", 4d, 1f, true, true)
        })
        {
            var item = Activator.CreateInstance(itemType);
            itemType.GetField("time").SetValue(item, time);
            itemType.GetField("noteType").SetValue(item, type);
            itemType.GetField("alpha").SetValue(item, alpha);
            itemType.GetField("live").SetValue(item, live);
            itemType.GetField("reset").SetValue(item, reset);
            timeline.Add(item);
        }
        Call(loader, "BuildAlphaTimeline", timeline);
        var liveController = new GameObject("TapLine live state").AddComponent<LiveNoteVisualController>();
        Call(liveController, "Configure", null, null, timeline, loader, clock);
        var root = new GameObject("TapLine test", typeof(SpriteRenderer));
        var note = root.AddComponent<TapDrop>();
        note.previewOnly = true;
        note.renderReporter = loader;
        var line = new GameObject("Arc", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        line.transform.SetParent(root.transform, false);
        var tinted = new Material(Shader.Find("Sprites/NoteColorTint"));
        tinted.SetFloat("_NoteAlpha", 0f);
        line.sharedMaterial = tinted;
        var properties = new MaterialPropertyBlock();
        foreach (var (birth, at, expected) in new[] { (1f, 1f, 0f), (2f, 2f, .25f), (2f, 3f, .5f), (2f, 4f, .25f), (2f, 2f, .25f) })
        {
            note.time = birth;
            if (birth == at && at < 3f) Call(note, "InitializeTapLineAlpha", line);
            clock.AudioTime = at;
            Call(liveController, "Update");
            liveController.ApplyCurrent(note);
            note.ApplyLiveAlpha(0f);
            line.GetPropertyBlock(properties);
            if (Mathf.Abs(properties.GetFloat("_NoteAlpha") - expected) > .001f)
                throw new Exception($"tapline alpha differs at birth={birth}, time={at}");
        }
        UnityEngine.Object.DestroyImmediate(root);
        UnityEngine.Object.DestroyImmediate(liveController.gameObject);
        UnityEngine.Object.DestroyImmediate(tinted);
        Debug.Log("111 TAPLINE PASS: inherited body alpha, explicit birth/live override, reset and seek.");
    }

    private static void VerifyCover()
    {
        var folder = Path.GetFullPath("../.codex-build/061-cover");
        if (!File.Exists(Path.Combine(folder, "cover.apng"))) throw new Exception("Missing APNG fixture");
        if (ChartCover.Find(folder) != Path.Combine(folder, "cover.apng")) throw new Exception("APNG priority failed");
        var coverType = typeof(BGManager).Assembly.GetType("AnimatedChartCover");
        var open = coverType.GetMethod("Open", All);
        var cover = open.Invoke(null, new object[] { Path.Combine(folder, "cover.apng"), false });
        var prepare = (System.Collections.IEnumerator)Call(cover, "Prepare"); while (prepare.MoveNext()) { }
        foreach (var sample in new[] { (0d, 0), (.11d, 1), (.31d, 2), (.61d, 3), (1.01d, 0) })
        {
            var sprite = (Sprite)Call(cover, "At", sample.Item1);
            var target = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height);
            Graphics.Blit(sprite.texture, target);
            var previous = RenderTexture.active; RenderTexture.active = target;
            var actual = new Texture2D(sprite.texture.width, sprite.texture.height, TextureFormat.RGBA32, false);
            actual.ReadPixels(new Rect(0,0,actual.width,actual.height),0,0); actual.Apply();
            var expected = new Texture2D(2,2); expected.LoadImage(File.ReadAllBytes(Path.Combine(folder, "expected" + sample.Item2 + ".png")));
            var a = actual.GetPixels32(); var b = expected.GetPixels32();
            for (var i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r-b[i].r) > 2 || Mathf.Abs(a[i].g-b[i].g) > 2 || Mathf.Abs(a[i].b-b[i].b) > 2 || Mathf.Abs(a[i].a-b[i].a) > 2)
                    throw new Exception("APNG composite differs at frame " + sample.Item2 + " pixel " + i + " " + a[i] + " " + b[i]);
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(actual); UnityEngine.Object.DestroyImmediate(expected);
        }
        if (!ReferenceEquals(cover, open.Invoke(null, new object[] { Path.Combine(folder, "cover.apng"), false }))) throw new Exception("Replay decoded APNG again");
        Debug.Log("REPORTED061 COVER PASS: priority, frame timing, blend/dispose, seek/loop, decoded-cache reuse.");
    }
}
