using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

[InitializeOnLoad]
public static class MediaRecordingVerification
{
    private const string Pending="AlphaMediaRecordingVerification";
    static MediaRecordingVerification()
    {
        EditorApplication.playModeStateChanged+=state=> {
            if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false)) return;
            SessionState.SetBool(Pending,false);
            var probe=new GameObject("Media recording verification");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            probe.AddComponent<MediaRecordingVerificationProbe>();
        };
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        SessionState.SetBool(Pending,true);
        EditorApplication.EnterPlaymode();
    }
    public static void RunCoverFormats073()
    {
        SessionState.SetBool("CoverFormats073", true);
        Run();
    }
}

public sealed class MediaRecordingVerificationProbe : MonoBehaviour
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    private IEnumerator Start()
    {
        var routine=SessionState.GetBool("CoverFormats073",false) ? VerifyCoverFormats073() : Verify();
        SessionState.SetBool("CoverFormats073",false);
        while(true)
        {
            bool moved;
            try { moved=routine.MoveNext(); }
            catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); yield break; }
            if(!moved) break;
            yield return routine.Current;
        }
        Debug.Log("RECORDING PVOVERLAY PASS: actual pptv.mp4 preparation, transition, video decoding and visible surface with recording enabled.");
        EditorApplication.Exit(0);
    }
    private IEnumerator Verify()
    {
        while(GameObject.Find("Background")==null) yield return null;
        yield return null;
        var clock=GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var controller=clock.gameObject.AddComponent<MediaTimelineController>();
        var overlay=new MediaChange {time=0,kind="pvOverlay",enabled=true,path="pptv.mp4",transition=0.5f};
        controller.Configure(new List<MediaChange>{overlay},
            @"C:\Users\sjtu_\Downloads\⚡朋友的酒⚡\⚡朋友的酒⚡",clock,true);
        var deadline=Time.realtimeSinceStartup+30;
        while(!controller.IsPrepared)
        {
            if(!string.IsNullOrEmpty(controller.PreparationError)) throw new Exception(controller.PreparationError);
            if(Time.realtimeSinceStartup>deadline) throw new Exception("Video preparation timed out");
            yield return null;
        }
        clock.SetPausedTimelineTime(0);
        controller.SetPlaybackActive(true);
        var player=(VideoPlayer)typeof(MediaTimelineController).GetField("activeVideo",Fields).GetValue(controller);
        if(player==null || player.skipOnDrop) throw new Exception("Recording must select the actual MP4 and disable dropping");
        var renderer=player.GetComponent<SpriteRenderer>();
        var background=GameObject.Find("Background").GetComponent<SpriteRenderer>();
        var start=Time.realtimeSinceStartup; long first=-1,last=-1;
        while(Time.realtimeSinceStartup-start<2)
        {
            clock.SetPausedTimelineTime(Time.realtimeSinceStartup-start);
            typeof(MediaTimelineController).GetMethod("ApplyAt",Fields).Invoke(controller,new object[]{clock.AudioTime,false});
            if(player.frame>=0) { if(first<0) first=player.frame; last=player.frame; }
            yield return null;
        }
        if(!renderer.enabled || renderer.forceRenderingOff || renderer.color.a<.99f ||
            renderer.sortingLayerID!=background.sortingLayerID || renderer.sortingOrder<=background.sortingOrder)
            throw new Exception("PV overlay surface must be fully visible above the background");
        if(last<=first || player.texture==null) throw new Exception("MP4 decoder did not advance");
        Debug.Log($"PVOVERLAY RECORDING SAMPLE: frames={first}..{last}, opacity={renderer.color.a}, texture={player.texture.width}x{player.texture.height}");
        controller.SetPlaybackActive(false);
        controller.Configure(new List<MediaChange> { new MediaChange { time=0,kind="audio",enabled=true,path="track.mp3" },
            new MediaChange { time=4,kind="audio",enabled=false } },
            Path.GetFullPath("../.codex-build/061-record-range"),clock,false);
        deadline=Time.realtimeSinceStartup+10;
        while(!controller.IsPrepared) { if(Time.realtimeSinceStartup>deadline) throw new Exception("AUDIO decode timeout"); yield return null; }
        if(controller.PreparationError!=null) throw new Exception(controller.PreparationError);
        clock.SetPausedTimelineTime(1); controller.SetPausedTimelineTime(1);
        var source=(AudioSource)typeof(MediaTimelineController).GetField("audioSource",Fields).GetValue(controller);
        if(source.clip==null || Math.Abs(source.time-1f)>.05f) throw new Exception("AUDIO scrub did not restore its source time");
        controller.SetPlaybackActive(false);
        controller.ContinuePlayback();
        if(source.clip==null || Math.Abs(source.time-1f)>.05f) throw new Exception("AUDIO lost after preview-to-play handoff");
        var samples=new float[2048]; source.clip.GetData(samples,44100);
        if(!Array.Exists(samples,value=>Math.Abs(value)>.001f)) throw new Exception("Decoded AUDIO has no samples");
        clock.SetPausedTimelineTime(4.1f);
        typeof(MediaTimelineController).GetMethod("ApplyAt",Fields).Invoke(controller,new object[]{4.1f,false});
        if(source.clip!=null) throw new Exception("AUDIO false must stop playback");
        controller.SetPlaybackActive(false);
        Debug.Log("AUDIO LIVE PASS: MP3 decode/data, scrub source offset, preview-to-play resume, False stop.");
        var covers = VerifyCoverAndBackground(clock);
        while (covers.MoveNext()) yield return covers.Current;
    }

    private IEnumerator VerifyCoverFormats073()
    {
        while (GameObject.Find("Background") == null) yield return null;
        yield return null;
        var clock = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var background = GameObject.Find("Background").GetComponent<BGManager>();
        foreach (var format in new[] { "mp4", "gif" })
        {
            clock.SetPausedTimelineTime(-5f);
            clock.isRecord = true;
            background.LoadBGFromPath(Path.GetFullPath("../.codex-build/073-cover/" + format), 1, .5f, .5f, true);
            var deadline = Time.realtimeSinceStartup + 20f;
            while (!background.IsPreparedForRecording)
            {
                if (Time.realtimeSinceStartup > deadline) throw new Exception(format + " cover preparation timeout");
                yield return null;
            }
            if (background.PreparationError != null) throw new Exception(background.PreparationError);
            var jacket = (UnityEngine.UI.RawImage)typeof(BGManager).GetField("rawImage", Fields).GetValue(background);
            if (jacket.texture == null || jacket.texture.width != 64) throw new Exception(format + " cover texture missing");
            if (format == "mp4")
            {
                var video = (VideoPlayer)typeof(BGManager).GetField("coverVideo", Fields).GetValue(background);
                if (video.skipOnDrop || !video.isLooping || video.audioOutputMode != VideoAudioOutputMode.None)
                    throw new Exception("Recording cover must loop silently without frame dropping");
                clock.SetPausedTimelineTime(-4.5f); background.SetPausedTimelineTime(-4.5f);
                deadline = Time.realtimeSinceStartup + 10f;
                while (video.frame < 4 && Time.realtimeSinceStartup < deadline) yield return null;
                if (video.frame < 4) throw new Exception("MP4 cover seek did not decode the target frame");
            }
            else
            {
                var animation = typeof(BGManager).GetField("animatedCover", Fields).GetValue(background);
                var at = animation?.GetType().GetMethod("At", Fields);
                if (animation == null || ReferenceEquals(at.Invoke(animation, new object[] { 0d }), at.Invoke(animation, new object[] { .5d })))
                    throw new Exception("GIF cover must retain multiple timed frames");
            }
            Debug.Log("COVER073 PASS: " + format + " decode, recording preparation and animation/seek.");
        }
    }

    private IEnumerator VerifyCoverAndBackground(AudioTimeProvider clock)
    {
        var directory = Path.GetFullPath("../.codex-build/063-cover-background");
        Directory.CreateDirectory(directory);
        if (File.Exists(Path.Combine(directory, "bg.apng"))) File.Delete(Path.Combine(directory, "bg.apng"));
        File.Copy(Path.GetFullPath("../.codex-build/061-cover/cover.apng"), Path.Combine(directory, "cover.apng"), true);
        var green = new Texture2D(4, 4);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++) green.SetPixel(x, y, Color.green);
        green.Apply(); File.WriteAllBytes(Path.Combine(directory, "bg.png"), green.EncodeToPNG());
        Destroy(green);
        var background = GameObject.Find("Background").GetComponent<BGManager>();
        clock.SetPausedTimelineTime(-1);
        background.LoadBGFromPath(directory, 1, .5f, .5f, false);
        var deadline = Time.realtimeSinceStartup + 15;
        while (!background.IsPreparedForRecording)
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Cover preparation timed out"); yield return null; }
        yield return null;
        var jacket = (UnityEngine.UI.RawImage)typeof(BGManager).GetField("rawImage", Fields).GetValue(background);
        var renderer = background.GetComponent<SpriteRenderer>();
        if (jacket.texture == null || renderer.sprite == null || jacket.texture == renderer.sprite.texture)
            throw new Exception("Cover and BG must use different textures");
        File.Copy(Path.Combine(directory, "cover.apng"), Path.Combine(directory, "bg.apng"), true);
        background.LoadBGFromPath(directory, 1, .5f, .5f, false);
        deadline = Time.realtimeSinceStartup + 15;
        while (!background.IsPreparedForRecording)
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Two APNGs timed out"); yield return null; }
        yield return null;
        var cover = typeof(BGManager).GetField("animatedCover", Fields).GetValue(background);
        var animation = typeof(BGManager).GetField("animatedBackground", Fields).GetValue(background);
        if (cover == null || animation == null || ReferenceEquals(cover, animation) ||
            jacket.texture == null || renderer.sprite == null)
            throw new Exception("Independent APNGs must not release each other's frames");
        Debug.Log("COVER/BACKGROUND LIVE PASS: separate cover/APNG background, recording readiness and retained textures.");
    }
}
