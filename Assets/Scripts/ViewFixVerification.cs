#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Assets.Scripts.Types;
using MajdataCore;
using Newtonsoft.Json;
using UnityEngine;

public static class ViewFixVerification
{
    public static void Run()
    {
        void Require(bool value, string error) { if (!value) throw new Exception(error); }
        var speed = 7f;
        var duration = AlphaVisualTiming.GetTouchMotionDuration(speed);
        SvController.Load(new List<SvPoint>());
        Require(Math.Abs(SvController.GetTouchWarningLead(10,speed,"touch")-duration)<.00001,
            "Noise does not match Touch appearance");
        SvController.Load(new List<SvPoint>{ new() { time=0, multiplier=2 } });
        Require(Math.Abs(SvController.GetTouchWarningLead(10,speed,"touch")-duration/2)<.00001,
            "Double SV does not halve noise prewarning");
        SvController.Load(new List<SvPoint>{ new() { time=0, multiplier=2, noteType="touch", streamIndex=1 } });
        Require(Math.Abs(SvController.GetTouchWarningLead(10,speed,"1|touch")-duration/2)<.00001 &&
            Math.Abs(SvController.GetTouchWarningLead(10,speed,"touch")-duration)<.00001,
            "Typed/overlay noise warning leaks to another stream");
        var root = new GameObject("Recording duration verification");
        try {
            var handler = root.AddComponent<HttpHandler>();
            var data = new Majson { timingList=new List<SimaiTimingPoint> {
                new() { time=2, noteList=new List<SimaiNote> {
                    new(){noteType=SimaiNoteType.Hold,holdTime=3},
                    new(){noteType=SimaiNoteType.Slide,slideStartTime=3,slideTime=4},
                    new(){noteType=SimaiNoteType.NoiseZone,holdTime=8} } },
                new() {time=20,noteList=new List<SimaiNote>()} } };
            var getLength = typeof(HttpHandler).GetMethod("getChartLength",BindingFlags.NonPublic|BindingFlags.Instance);
            Require(Math.Abs((float)getLength.Invoke(handler,new object[]{JsonConvert.SerializeObject(data)})-10)<.00001,
                "Noise tail / trailing empty slots end recording incorrectly");
            var clock=root.AddComponent<AudioTimeProvider>();
            foreach(var fps in new[]{60,120}) {
                clock.SetStartTime(DateTime.Now.Ticks,0,1,true,fps);
                clock.BeginRecordingCapture(5);
                Require(Time.captureFramerate==fps && Math.Abs(clock.AudioTime+5)<.00001,
                    "Capture rate or intro start differs from request");
                clock.ResetStartTime();
                Require(Time.captureFramerate==0 && Time.timeScale==1,
                    "Recording time step leaked into normal playback");
            }
            var cacheRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"..",".codex-build","cover-path-verification"));
            var figure=Path.Combine(cacheRoot,"figure"); Directory.CreateDirectory(figure);
            var work=Path.Combine(cacheRoot,".majdata"); Directory.CreateDirectory(work);
            var texture=new Texture2D(2,2); texture.SetPixels(new[]{Color.red,Color.red,Color.red,Color.red}); texture.Apply();
            File.WriteAllBytes(Path.Combine(figure,"songdetail_master.png"),texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            var card=new GameObject("Cache verification",typeof(RectTransform),typeof(UnityEngine.UI.RawImage));
            card.transform.SetParent(root.transform);
            var template=root.AddComponent<SongDetailTemplateView>();
            var raw=card.GetComponent<UnityEngine.UI.RawImage>();
            typeof(SongDetailTemplateView).GetField("cardImage",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(template,raw);
            data.filePath=Path.Combine(work,"majdata.json"); data.diffNum=4;
            Require((bool)typeof(SongDetailTemplateView).GetMethod("TryApplyCachedCard",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(template,new object[]{data}) && raw.texture!=null,
                "Official cover misses song/figure when JSON lives in .majdata");
            Debug.Log("VIEW FIX CHECK PASS: Touch/HS/SV noise appearance, overlay isolation, final noise tail, ignored empty slots, 60/120 capture clock and intro start.");
            Debug.Log("COVER PATH CHECK PASS: .majdata/majdata.json resolves song/figure/songdetail_master.png and loads the real PNG.");
        } finally { Time.captureFramerate=0; Time.timeScale=1; UnityEngine.Object.DestroyImmediate(root); }
        NoiseZoneVerification.Run();
        TimelineRestoreVerification.Run();
    }
}
#endif
