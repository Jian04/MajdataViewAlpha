using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class RotatePvVerification
{
    public static void Run()
    {
        var root=new GameObject("View PV rotation verification");
        var background=GameObject.Find("Background").transform;
        var baseline=background.rotation;
        try
        {
            var controller=root.AddComponent<ScreenEffectController>();
            controller.Configure(new List<EffectChange> {
                new(){time=0,effect="Rotate",stateful=true,enabled=true,intensity=30},
                new(){time=1,effect="Rotate",stateful=true,enabled=true,intensity=60,transition=1,rotatePV=true},
                new(){time=3,effect="Rotate",stateful=true,enabled=true,intensity=90,transition=1,rotatePV=false}
            },root.AddComponent<AudioTimeProvider>());
            var apply=typeof(ScreenEffectController).GetMethod("ApplyBackgroundTransform",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(var sample in new[]{(0f,0f),(1.5f,30f),(2f,60f),(3.5f,30f),(4f,0f),(.5f,0f)})
            {
                var angle=controller.RotationForPV(sample.Item1);
                if(Math.Abs(angle-sample.Item2)>.001f) throw new Exception("View PV transition/rewind differs from authored state");
                apply.Invoke(controller,new object[]{angle,Vector2.zero,1f});
                if(Quaternion.Angle(background.rotation,Quaternion.Euler(0,0,angle)*baseline)>.001f)
                    throw new Exception("View PV transforms do not use their independent rotation track");
            }
            Debug.Log("VIEW ROTATE PV PASS: default exclusion, explicit inclusion, transitions, rewind and actual background transforms.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
