using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Startup100Verification
{
    public static void Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Additive);
        var background=UnityEngine.Object.FindAnyObjectByType<BGManager>();
        var type=typeof(BGManager);
        background.LoadStandbyTheme("light","#ECEEF1");
        if(type.GetField("loadedStandbySprite",flags).GetValue(background)!=null ||
           type.GetField("standbyMaterial",flags).GetValue(background)!=null)
            throw new Exception("Early settings loaded a background before dimensions were initialized");
        type.GetMethod("Start",flags).Invoke(background,null);
        var sprite=(Sprite)type.GetField("loadedStandbySprite",flags).GetValue(background);
        var renderer=background.GetComponent<SpriteRenderer>();
        if(sprite==null || !float.IsFinite(sprite.bounds.size.x) || sprite.bounds.size.x<1 ||
           renderer.sprite!=sprite || renderer.color.a<.99f)
            throw new Exception("Cold standby background missing or invalid after Start");
        var clock=UnityEngine.Object.FindAnyObjectByType<AudioTimeProvider>();
        if(clock.isStart || clock.IsPaused) throw new Exception("Startup is not stopped");
        background.LoadStandbyTheme("dark","#23262C");
        Debug.Log("100 STARTUP PASS: early settings deferred, background initialized at authored size, visible in stopped state, light/dark reload.");
    }
    public static void Build() { Run(); AlphaCommandLineBuild.BuildView(); }
}
