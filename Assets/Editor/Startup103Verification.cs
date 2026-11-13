using System;
using System.Reflection;
using UnityEngine;

public static class Startup103Verification
{
    private const BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Instance;
    public static void Run()
    {
        Startup100Verification.Run();
        var clock=UnityEngine.Object.FindAnyObjectByType<AudioTimeProvider>();
        var background=UnityEngine.Object.FindAnyObjectByType<BGManager>();
        var counter=GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        typeof(ObjectCounter).GetMethod("Start",Fields).Invoke(counter,null);
        var skin=GameObject.Find("Outline").GetComponent<CustomSkin>();
        skin.LoadSkin("dx","dx","dx","dx",false,"dx");
        var display=GameObject.Find("DataLoader").AddComponent<DisplayTimelineController>();
        display.SetImmediateDisplay(1,true,true,true,.6f,.8f);
        if((float)typeof(DisplayTimelineController).GetField("judgeStyleTransitionStart",Fields).GetValue(display)>=0f ||
           typeof(DisplayTimelineController).GetField("fadingJudgeLine",Fields).GetValue(display)!=null)
            throw new Exception("First display settings started a Magical crossfade");
        if(GameObject.Find("Outline").GetComponent<SpriteRenderer>().sprite!=skin.JudgeLineBase)
            throw new Exception("First display style was not applied immediately");
        var cover=(SpriteRenderer)typeof(BGManager).GetField("bgCoverRenderer",Fields).GetValue(background);
        var outer=GameObject.Find("1080Circle_Rev").GetComponent<SpriteRenderer>();
        if(cover.color.a!=0f || clock.isStart || clock.IsPaused)
            throw new Exception("Cold standby was darkened or started its playback clock");
        if(outer.color.a!=1f || !outer.enabled || !outer.gameObject.activeSelf)
            throw new Exception("Cold standby exposed the editor behind the outer mask");
        if((float)typeof(DisplayTimelineController).GetField("initialInnerBrightness",Fields).GetValue(display)!=.6f)
            throw new Exception("Standby reset changed configured gameplay brightness");
        typeof(DisplayTimelineController).GetField("timeProvider",Fields).SetValue(display,clock);
        clock.SetPausedTimelineTime(0f);
        display.SetImmediateDisplay(1,true,true,true,.6f,.8f);
        if(Mathf.Abs(cover.color.a-.6f)>.0001f || Mathf.Abs(outer.color.a-.8f)>.0001f)
            throw new Exception("Paused chart lost its configured brightness");
        clock.ResetStartTime(); display.SetPlaybackActive(false);
        if(cover.color.a!=0f || outer.color.a!=1f) throw new Exception("Stopping did not restore undimmed standby and its outer mask");
        var outline=GameObject.Find("Outline").GetComponent<SpriteRenderer>();
        foreach(var style in new[]{1,2,3})
        {
            display.SetImmediateDisplay(style,true,true,true,.6f,.8f);
            var overlay=(SpriteRenderer)typeof(DisplayTimelineController).GetField("styleOverlay",Fields).GetValue(display);
            if(outline.forceRenderingOff || !overlay.forceRenderingOff)
                throw new Exception("Standby must retain the selected judge line without the extra area overlay");
        }
        display.SetImmediateDisplay(1,true,true,true,.6f,.8f);
        clock.SetPausedTimelineTime(0f); display.SetImmediateDisplay(1,true,true,true,.6f,.8f);
        if(outline.forceRenderingOff) throw new Exception("Paused chart lost its judge line");
        display.SetImmediateDisplay(3,true,true,true,.6f,.8f);
        if((float)typeof(DisplayTimelineController).GetField("judgeStyleTransitionStart",Fields).GetValue(display)<0f)
            throw new Exception("Later user style changes lost their transition");
        VerifyFpsReload();
        Debug.Log("106 STARTUP PASS: selected judge line retained without the extra standby area; paused chart brightness and style transition retained.");
    }
    private static void VerifyFpsReload()
    {
        var loader=GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        var owner=new GameObject("FPS reload check");
        var fps=owner.AddComponent<ViewFpsDisplay>(); fps.SetMode(0,loader);
        typeof(ViewFpsDisplay).GetMethod("Start",Fields).Invoke(fps,null);
        fps.SetMode(1,loader);
        typeof(ViewFpsDisplay).GetField("frames",Fields).SetValue(fps,120);
        typeof(ViewFpsDisplay).GetField("elapsed",Fields).SetValue(fps,1f);
        typeof(ViewFpsDisplay).GetMethod("LateUpdate",Fields).Invoke(fps,null);
        var label=(UnityEngine.UI.Text)typeof(ViewFpsDisplay).GetField("label",Fields).GetValue(fps);
        var measured=label.text;
        if(string.IsNullOrEmpty(measured)) throw new Exception("FPS measurement was empty");
        UnityEngine.Object.DestroyImmediate(label.gameObject); UnityEngine.Object.DestroyImmediate(owner);
        owner=new GameObject("FPS recreated after chart load"); fps=owner.AddComponent<ViewFpsDisplay>();
        typeof(ViewFpsDisplay).GetMethod("Start",Fields).Invoke(fps,null);
        label=(UnityEngine.UI.Text)typeof(ViewFpsDisplay).GetField("label",Fields).GetValue(fps);
        if(!label.enabled || label.text!=measured) throw new Exception("Scene recreation blanked the enabled FPS label");
        fps.SetMode(1,loader);
        if(label.text!=measured) throw new Exception("Repeated settings cleared the FPS label");
        fps.SetMode(0,loader); typeof(ViewFpsDisplay).GetMethod("LateUpdate",Fields).Invoke(fps,null);
        if(label.enabled) throw new Exception("FPS Off mode was not retained");
        UnityEngine.Object.DestroyImmediate(label.gameObject); UnityEngine.Object.DestroyImmediate(owner);
        Debug.Log("105 FPS PASS: measured FPS and selected mode survive scene recreation; repeated settings retain text; Off mode still hides it.");
    }
    public static void Build() {Run(); AlphaCommandLineBuild.BuildView();}
}
