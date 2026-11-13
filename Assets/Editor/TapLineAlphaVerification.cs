using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TapLineAlphaVerification
{
    public static void Build()
    {
        Run();
        AlphaCommandLineBuild.BuildView();
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var loader=GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        var build=typeof(JsonDataLoader).GetMethod("BuildAlphaTimeline",BindingFlags.Instance|BindingFlags.NonPublic);
        var resolve=typeof(JsonDataLoader).GetMethod("ResolveTapLineAlpha",BindingFlags.Instance|BindingFlags.NonPublic);
        void Load(string json) => build.Invoke(loader,new[]{JsonConvert.DeserializeObject(json,build.GetParameters()[0].ParameterType)});
        void Expect(NoteDrop note,float expected,double? time=null,bool live=false)
        {
            var actual=(float?)resolve.Invoke(loader,new object[]{note,time,live,null});
            if(!actual.HasValue || Mathf.Abs(actual.Value-expected)>.001f)
                throw new Exception($"Guide opacity {note.GetType().Name}: expected {expected}, got {actual}");
        }
        foreach(var carrier in new[]{false,true})
        {
            var go=new GameObject("Tap line alpha verification");
            NoteDrop note=carrier ? go.AddComponent<TrajectoryCarrierDrop>() : go.AddComponent<TapDrop>();
            if(note is TrajectoryCarrierDrop trajectory) trajectory.carrierVisualType="tap";
            typeof(NoteDrop).GetField("authoredVisualStateTime",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(note,2d);
            try
            {
                Load("[{'time':0,'alpha':0}]"); Expect(note,0);
                Load("[{'time':0,'noteType':'tap','alpha':0.2}]"); Expect(note,.2f);
                Load("[{'time':0,'noteType':'tap','alpha':0.2},{'time':3,'noteType':'tap','live':true,'alpha':0}]");
                Expect(note,0,4,true);
                Load("[{'time':0,'noteType':'tap','alpha':0.2},{'time':3,'noteType':'tap','live':true,'transition':{'from':0,'to':1,'duration':2}}]");
                Expect(note,.5f,4,true);
                Load("[{'time':0,'noteType':'tap','alpha':0.2},{'time':0,'noteType':'tapline','alpha':0.7},{'time':3,'noteType':'tap','live':true,'alpha':0}]");
                Expect(note,.7f); Expect(note,.7f,4,true);
                Load("[{'time':0,'noteType':'tapline','alpha':0.7},{'time':3,'noteType':'tapline','live':true,'alpha':0.4}]");
                Expect(note,.4f,4,true);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        Debug.Log("TAP LINE ALPHA PASS: Tap and trajectory carrier; ALPHA, ALPHAV, transition, explicit birth/live override.");
    }
}
