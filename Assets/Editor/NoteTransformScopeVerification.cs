using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NoteTransformScopeVerification
{
    public static void Build()
    {
        Run();
        RotatePvVerification.Run();
        ExtraFilterVerification.Run();
        AlphaCommandLineBuild.BuildView();
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var root=new GameObject("Note transform scope verification");
        var notesObject=GameObject.Find("Notes");
        if(notesObject==null) { notesObject=new GameObject("Notes"); notesObject.transform.SetParent(root.transform,false); }
        var notes=notesObject.transform;
        var outlineObject=GameObject.Find("Outline")??GameObject.Find("DebugOutline");
        if(outlineObject==null) { outlineObject=new GameObject("Outline"); outlineObject.transform.SetParent(root.transform,false); }
        var outline=outlineObject.transform;
        var canvas=GameObject.Find("CanvasInfo").transform;
        var fixedTargets=canvas.GetComponentsInChildren<Transform>(true).Concat(new[]{outline}).ToArray();
        var positions=fixedTargets.Select(t=>t.position).ToArray();
        var rotations=fixedTargets.Select(t=>t.rotation).ToArray();
        var point=new Vector3(.6f,.9f,0);
        var baseline=notes.TransformPoint(point);
        var controller=root.AddComponent<ScreenEffectController>();
        var clock=root.AddComponent<AudioTimeProvider>();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Call(string name)=>typeof(ScreenEffectController).GetMethod(name,flags).Invoke(controller,null);
        void Check(float at,float rotation,float? flip)
        {
            clock.AudioTime=at; Call("RefreshGameplayState"); Call("OnPreCull"); Call("LateUpdate");
            var expected=Quaternion.Euler(0,0,rotation)*baseline;
            if(flip.HasValue)
            {
                var radians=flip.Value*Mathf.Deg2Rad;
                var axis=new Vector3(Mathf.Cos(radians),Mathf.Sin(radians),0);
                expected=2*Vector3.Dot(expected,axis)*axis-expected;
            }
            if(Vector3.Distance(notes.TransformPoint(point),expected)>.001f)
                throw new Exception($"Notes transform mismatch at {at}, rotation={rotation}, flip={flip}");
            for(var i=0;i<fixedTargets.Length;i++)
                if(Vector3.Distance(fixedTargets[i].position,positions[i])>.001f || Quaternion.Angle(fixedTargets[i].rotation,rotations[i])>.001f)
                    throw new Exception("Non-note target transformed: "+fixedTargets[i].name);
            Call("OnPostRender");
        }
        try
        {
            foreach(var axis in new[]{0f,45f,90f})
            {
                controller.Configure(new List<EffectChange>{
                    new(){time=0,effect="Rotate",stateful=true,enabled=true,intensity=30},
                    new(){time=2,effect="Flip",duration=1,holdTime=1,attack=0,release=0,intensity=1,paramA=axis}
                },clock);
                Check(1,30,null); Check(2.5f,30,axis); Check(3.1f,30,null); Check(2.5f,30,axis);
            }
            controller.Configure(new List<EffectChange>{
                new(){time=0,effect="Flip",stateful=true,enabled=true,intensity=1,paramA=90},
                new(){time=1,effect="Flip",stateful=true,enabled=false}
            },clock);
            Check(.5f,0,90); Check(1.5f,0,null);
            controller.Configure(new List<EffectChange>{new(){time=0,effect="Flip",stateful=true,enabled=true,intensity=1,paramA=90,flipNotesOnly=false}},clock);
            Check(.5f,0,null);
            Debug.Log("NOTE TRANSFORM SCOPE PASS: rotation, 0/45/90 note flips, composition, instant expiry, disable, rewind; outline and all HUD transforms fixed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
