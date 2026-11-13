using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PseudoAlphaVerification
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Fields).Invoke(target, args);
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var loader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        var period = 240d / 205 / 8;
        var start = (double)(float)1.01 + 2 * period;
        var restoreTime = start + 32 * period;
        var points = new List<object>();
        for (var slot = 0; slot < 33; slot++)
            for (var each = 0; each < 2; each++)
                points.Add(new {
                    time = start + slot * period + each * 1.875 / 205,
                    noteList = new[] { new { noteType = "Touch", touchArea = each == 0 ? "B" : "D",
                        startPosition = each == 0 ? 1 : 2 } }
                });
        var json = JsonConvert.SerializeObject(new {
            timingList = points,
            alphaTable = new[] {
                new { time = 0d, alpha = 1f }, new { time = start, alpha = 0f },
                new { time = restoreTime, alpha = 1f }
            }
        });
        var field = typeof(JsonDataLoader).GetField("loadedData", Fields);
        var data = JsonConvert.DeserializeObject(json, field.FieldType);
        field.SetValue(loader, data);
        var table = field.FieldType.GetField("alphaTable").GetValue(data);
        var timings = (IList)field.FieldType.GetField("timingList").GetValue(data);
        Call(loader, "BuildAlphaTimeline", table);
        var count = 0;
        foreach (var point in JObject.Parse(json)["timingList"])
        {
            var time = (double)point["time"];
            var expected = time < restoreTime ? 0f : 1f;
            var initial = (float)Call(loader, "GetAlphaAt", "touch", time, 0, false, false, false);
            if (initial != expected) throw new Exception($"Initial Touch ALPHA at {time:R}: {initial} != {expected}");
            var noteObject = new GameObject("Pseudo Touch");
            var note = noteObject.AddComponent<TouchDrop>();
            var notes = (IList)timings[count].GetType().GetField("noteList").GetValue(timings[count]);
            typeof(JsonDataLoader).GetField("currentBeatTime", Fields).SetValue(loader, time);
            typeof(JsonDataLoader).GetMethod("PrepareNote", Fields).MakeGenericMethod(typeof(TouchDrop))
                .Invoke(loader, new object[] { noteObject, notes[0], null });
            note.time = (float)time;
            note.previewOnly = true;
            if (note.VisualStateTime != time) throw new Exception("Authored visual timestamp lost");
            var animated = (float?)Call(loader, "AnimatedAlpha", note, time - .1, time - 1, false);
            if (animated != expected)
                throw new Exception($"Touch ALPHA at runtime {note.time:R} (authored {time:R}): {animated} != {expected}");
            var material = (Material)Call(loader, "CreateTintMaterial", null, initial, -1f, true, false, 0f);
            note.fans = new GameObject[7];
            var fans = (SpriteRenderer[])typeof(TouchDrop).GetField("fansSprite", Fields).GetValue(note);
            for (var i = 0; i < fans.Length; i++)
            {
                note.fans[i] = new GameObject("Touch fan " + i);
                note.fans[i].transform.SetParent(noteObject.transform);
                fans[i] = note.fans[i].AddComponent<SpriteRenderer>();
                if (material != null) fans[i].sharedMaterial = material;
            }
            note.multTouchEffect2 = note.fans[5]; note.multTouchEffect3 = note.fans[6];
            note.ApplyLiveAlpha(animated);
            note.setLayer(2);
            var properties = new MaterialPropertyBlock();
            foreach (var fan in fans)
            {
                fan.GetPropertyBlock(properties);
                if (properties.GetFloat("_NoteAlpha") != expected)
                    throw new Exception("Touch petals/point/overlap frame opacity lost");
            }
            UnityEngine.Object.DestroyImmediate(noteObject);
            count++;
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Debug.Log($"PSEUDO ALPHA VERIFICATION PASSED: {count} Touch timings, 7 renderers each, authored time and alpha reset");
    }
    public static void Build()
    {
        Run();
        AlphaCommandLineBuild.BuildView();
    }
}
