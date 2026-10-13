using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class JudgeLineExpandVerification
{
    public static void BuildView()
    {
        Run();
        AlphaCommandLineBuild.BuildView();
    }

    public static void Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var root = new GameObject("Expand switch verification");
        var outline = new GameObject("Expand original line verification");
        try
        {
            var original = outline.AddComponent<SpriteRenderer>();
            original.color = Color.cyan;
            var display = root.AddComponent<DisplayTimelineController>();
            var type = typeof(DisplayTimelineController);
            type.GetField("judgeLines", flags).SetValue(display, new[] { original });
            type.GetField("judgeLineTransform", flags).SetValue(display, outline.transform);
            var events = (Dictionary<string, List<DisplayChange>>)type.GetField("eventsByProperty", flags).GetValue(display);
            events["JudgeLineExpand"] = new List<DisplayChange> {
                new() { time = 1, duration = 1, target = -22.5f },
                new() { time = 3, target = 0 },
                new() { time = 5, duration = 1, target = 22.5f }
            };
            var apply = type.GetMethod("ApplyExpandedJudgeLine", flags);
            SpriteRenderer line = null;
            void At(float time, bool visible, float angle = 0)
            {
                apply.Invoke(display, new object[] { time, 1f });
                line = (SpriteRenderer)type.GetField("expandedJudgeLine", flags).GetValue(display);
                if ((line != null && !line.forceRenderingOff) != visible)
                    throw new Exception("Wrong expand visibility at " + time);
                if (visible && Mathf.Abs(Mathf.DeltaAngle(line.transform.eulerAngles.z, angle)) > .001f)
                    throw new Exception("Wrong expand angle at " + time);
                if (original.forceRenderingOff || original.color != Color.cyan || outline.transform.rotation != Quaternion.identity)
                    throw new Exception("Switch modified the original judge line");
            }
            At(0, false);
            At(1.5f, true, -11.25f);
            At(2.5f, true, -22.5f);
            At(3, false);
            At(5.5f, true, 11.25f);
            At(4, false);
            At(2, true, -22.5f);
            At(0, false);
            At(6, true, 22.5f);
            Debug.Log("JUDGELINE EXPAND SWITCH PASS: on/off/re-enable, both rotations and backward/forward seeking; original ring unchanged.");

            var trackType = type.GetNestedType("DisplayTrack", BindingFlags.NonPublic);
            foreach (var baseline in new[] { 0f, .37f, .63f, 1f })
            {
                var track = Activator.CreateInstance(trackType, new object[] { new List<DisplayChange> {
                    new() { time = 1, target = .8f, duration = 1 },
                    new() { time = 3, resetToDefault = true, duration = 1 }
                }, baseline });
                var evaluate = trackType.GetMethod("Evaluate");
                foreach (var time in new[] { 0f, 1.5f, 2f, 3f, 3.5f, 4f, 2f, 5f })
                {
                    var expected = time < 1 ? baseline : time < 2 ? Mathf.Lerp(baseline, .8f, time - 1)
                        : time < 3 ? .8f : Mathf.Lerp(.8f, baseline, Mathf.Clamp01(time - 3));
                    if (Mathf.Abs((float)evaluate.Invoke(track, new object[] { time }) - expected) > .0001f)
                        throw new Exception("Display default / transition / seek lost at " + time + " baseline " + baseline);
                }
            }
            type.GetField("initialComboDisplay", flags).SetValue(display, 7);
            var combo = new List<DisplayChange> { new() { time = 1, target = 2 }, new() { time = 3, resetToDefault = true } };
            var comboValue = type.GetMethod("EvaluateComboDisplay", flags);
            if ((int)comboValue.Invoke(display, new object[] { 4f, combo }) != 7 ||
                (int)comboValue.Invoke(display, new object[] { 2f, combo }) != 2)
                throw new Exception("Combo Default did not restore the configured mode or seek back");
            type.GetField("initialJudgeLineStyle", flags).SetValue(display, 3);
            events["JudgeLineStyle"] = new List<DisplayChange> { new() { time = 1, target = 1 }, new() { time = 3, resetToDefault = true } };
            var styleValue = type.GetMethod("EvaluateJudgeLineStyle", flags);
            if ((int)styleValue.Invoke(display, new object[] { 4f }) != 3 ||
                (int)styleValue.Invoke(display, new object[] { 2f }) != 1)
                throw new Exception("Judge line Default did not restore the configured style");
            Debug.Log("DISPLAY DEFAULT PASS: settings 0/0.37/0.63/1, fade back, seeking, configured combo mode and judge-line style.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(outline);
        }
    }
}
