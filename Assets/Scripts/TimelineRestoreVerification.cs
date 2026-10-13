#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Types;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TimelineRestoreVerification
{
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static void Set(object value,string field,object target) => value.GetType().GetField(field,Fields).SetValue(value,target);
    static void Require(bool ok,string error) { if(!ok) throw new Exception(error); }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var root=new GameObject("Timeline state verification");
        try {
            var clock=root.AddComponent<AudioTimeProvider>();
            var manager=root.AddComponent<NoteManager>();
            manager.noteIndex[1]=0; manager.touchIndex[SensorType.C]=0;
            var counter=root.AddComponent<ObjectCounter>(); Set(counter,"notes",manager);
            var sensor=root.AddComponent<Sensor>(); sensor.Type=SensorType.C;
            foreach(var type in new[]{typeof(HoldDrop),typeof(TouchHoldDrop)}) {
                var obj=new GameObject(type.Name); obj.transform.SetParent(root.transform);
                var note=(NoteLongDrop)obj.AddComponent(type);
                note.time=0; note.LastFor=4; note.startPosition=1;
                note.BindSceneContext(clock,counter,manager); Set(note,"sensor",sensor);
                note.timelineRestorePending=true; note.RestoreTimelineState(2);
                Require((bool)typeof(NoteDrop).GetField("isJudged",Fields).GetValue(note),type.Name+" head not restored");
                Require((JudgeType)typeof(NoteDrop).GetField("judgeResult",Fields).GetValue(note)==JudgeType.Perfect,type.Name+" restored as miss");
                Require(!note.timelineRestorePending,"Restore transaction did not release input");
                note.RestoreTimelineState(2); // Commit is idempotent, not another head hit.
                Require(type==typeof(HoldDrop)?manager.noteIndex[1]==1:manager.touchIndex[SensorType.C]==1,"Restored head consumed twice");
            }
            var slideObject=new GameObject("Restored slide"); slideObject.transform.SetParent(root.transform);
            var slide=slideObject.AddComponent<SlideDrop>(); slide.time=0; slide.LastFor=4;
            Set(slide,"logicalBarCount",100);
            var queue=new List<JudgeArea>(); for(var i=1;i<=5;i++) queue.Add(new JudgeArea(new Dictionary<SensorType,bool>{{SensorType.C,i==5}},i*20));
            Set(slide,"judgeQueue",queue);
            slide.timelineRestorePending=true; slide.RestoreTimelineState(2);
            Require(queue.Count==3&&queue[0].SlideIndex==60,"Slide restored a complete, unconsumed trajectory");
            var mine=new GameObject("Untouched mine"); mine.transform.SetParent(root.transform);
            var mineSlide=mine.AddComponent<SlideDrop>(); mineSlide.time=0; mineSlide.LastFor=4; mineSlide.isMine=true;
            var mineQueue=new List<JudgeArea>{new(new Dictionary<SensorType,bool>{{SensorType.C,true}},100)};
            Set(mineSlide,"judgeQueue",mineQueue); Set(mineSlide,"logicalBarCount",100);
            mineSlide.timelineRestorePending=true; mineSlide.RestoreTimelineState(2);
            Require(mineQueue.Count==1,"Scrubbing incorrectly hit a mine");
            // Scene components receive Start in the player; batch scene loading
            // only deserializes them. Initialize the real UI before using it.
            var sceneCounter=GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
            typeof(ObjectCounter).GetMethod("Start",Fields).Invoke(sceneCounter,null);
            // The runtime skin loader creates Outline from its prefab. A batch
            // editor scene does not run that loader, so provide its renderer.
            if(GameObject.Find("Outline")==null) {
                var outline=new GameObject("Outline"); outline.transform.SetParent(root.transform);
                outline.AddComponent<SpriteRenderer>();
            }
            var display=UnityEngine.Object.FindAnyObjectByType<DisplayTimelineController>() ?? root.AddComponent<DisplayTimelineController>();
            display.SetImmediateDisplay(0,false,false,false,0,0);
            display.SetImmediateDisplay(1,false,false,false,0,0);
            var lines=(SpriteRenderer[])typeof(DisplayTimelineController).GetField("judgeLines",Fields).GetValue(display);
            Require(lines.Length>0,"Standby line was never resolved before first playback");
            Require(!lines[0].forceRenderingOff,"Standby line did not update immediately");
            Debug.Log("TIMELINE RESTORE PASS: Hold/TouchHold head state, one queue commit, consumed slide regions, untouched mine and standby judge line.");
        } finally {
            // These are test objects, not scored notes.
            foreach(var note in root.GetComponentsInChildren<NoteDrop>(true)) note.previewOnly=true;
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}

#endif
