using System;
using System.Reflection;
using UnityEngine;

public static class PausedJudgeRegionsVerification
{
    public static void BuildCheckedView()
    {
        Run();
        AlphaCommandLineBuild.BuildView();
    }

    public static void Run()
    {
        var root = new GameObject("Paused regions verification");
        var texture = new Texture2D(980, 980);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 980, 980), Vector2.one * .5f, 100);
        var oldFrameRate = Application.targetFrameRate;
        try
        {
            var clock = root.AddComponent<AudioTimeProvider>();
            var background = new GameObject("Fixture Background");
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = new Vector3(.2f, -.3f, 2f);
            background.transform.localScale = Vector3.one * .8f; // Current PV scale differs from standby.
            var manager = background.AddComponent<BGManager>();
            Set(manager, "provider", clock);
            Set(manager, "originalBackgroundSprite", sprite);
            Set(manager, "originalBackgroundScale", new Vector3(1.1f, 1.1f, 1f));
            Refresh(manager);
            if (Get(manager, "pausedJudgeRegions") != null) throw new Exception("Standby must not create a reference layer.");
            clock.SetPausedTimelineTime(3f);
            Refresh(manager);
            var overlay = (SpriteRenderer)Get(manager, "pausedJudgeRegions");
            if (overlay == null || !overlay.enabled) throw new Exception("Paused scrubbing reference is missing.");
            var expectedSize = sprite.bounds.size * 1.1f;
            var actualSize = Vector3.Scale(overlay.sprite.bounds.size, overlay.transform.localScale);
            if (Mathf.Abs(actualSize.x - expectedSize.x) > .001f || Mathf.Abs(actualSize.y - expectedSize.y) > .001f ||
                overlay.transform.localPosition != background.transform.localPosition)
                throw new Exception("Reference layer must match standby bounds and center, not current PV scale.");
            if (SortingLayer.GetLayerValueFromID(overlay.sortingLayerID) <= SortingLayer.GetLayerValueFromName("BG") ||
                SortingLayer.GetLayerValueFromID(overlay.sortingLayerID) >= SortingLayer.GetLayerValueFromName("Slide"))
                throw new Exception("Reference layer must be above background and below slides/notes.");
            if (overlay.sprite.texture.GetPixel(0, 0).a != 0f)
                throw new Exception("Reference image transparency was lost.");
            clock.SetPreviewTime(3f); Refresh(manager);
            if (overlay.enabled) throw new Exception("Ordinary note preview is not paused playback.");
            clock.PausePlayback(); Refresh(manager);
            if (!overlay.enabled) throw new Exception("Normal Pause must show the reference.");
            clock.SetStartTime(DateTime.Now.AddSeconds(5).Ticks, 3f, 1f, keepVisibleWhileScheduled: true);
            Refresh(manager);
            if (overlay.enabled) throw new Exception("Scheduled Continue must not flash the reference layer.");
            clock.ResetStartTime(); Refresh(manager);
            if (overlay.enabled) throw new Exception("Stop must hide the reference.");
            clock.SetPausedTimelineTime(3f); clock.isRecord = true; Refresh(manager);
            if (overlay.enabled) throw new Exception("Recording must never contain the editing reference.");
            clock.isRecord = false;
            Debug.Log("PAUSED JUDGE REGIONS PASS: original transparent image, standby bounds/center, correct layering; Pause/scrub enabled; preview, Stop, scheduled Continue and recording disabled.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
            Application.targetFrameRate = oldFrameRate;
        }
    }
    private static void Set(BGManager manager, string field, object value) =>
        typeof(BGManager).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, value);
    private static object Get(BGManager manager, string field) =>
        typeof(BGManager).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
    private static void Refresh(BGManager manager) =>
        typeof(BGManager).GetMethod("RefreshPausedJudgeRegions", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
}
