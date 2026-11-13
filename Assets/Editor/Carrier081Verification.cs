using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataCore;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Carrier081Verification
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var loader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var root = new GameObject("081 verification");
        try
        {
            var controller = root.AddComponent<ScreenEffectController>();
            controller.Configure(new List<EffectChange> {
                new() { time=0, effect="Move", stateful=true, enabled=true, intensity=1, paramA=1, paramB=2 },
                new() { time=1, effect="Move", stateful=true, enabled=true, intensity=1, paramA=2, paramB=4, movePV=true, transition=1 },
                new() { time=3, effect="Move", stateful=true, enabled=true, intensity=1, paramA=4, paramB=8, transition=1 }
            }, root.AddComponent<AudioTimeProvider>());
            foreach (var item in new[] {(0f,Vector2.zero),(1.5f,new Vector2(1,2)),(2f,new Vector2(2,4)),(3.5f,new Vector2(1,2)),(4f,Vector2.zero),(.5f,Vector2.zero)})
                if ((controller.MovementForPV(item.Item1)-item.Item2).sqrMagnitude>.00001f)
                    throw new Exception("MOVE PV transition/rewind mismatch at " + item.Item1);
            var skin = root.AddComponent<CustomSkin>();
            skin.TouchPoint = loader.touchPrefab.GetComponent<TouchDrop>().pointNormalSprite;
            skin.TouchHold = loader.touchHoldPrefab.GetComponent<TouchHoldDrop>().TouchHoldSprite;
            foreach (var type in new[] {"tap","star","hold","touch","touchhold"})
            {
                if (!SlidePathParser.TryParsePath("1-5[1:1]", out var route)) throw new Exception("fixture path");
                var note = new GameObject(type, typeof(SpriteRenderer)); note.transform.SetParent(root.transform);
                var carrier=note.AddComponent<TrajectoryCarrierDrop>(); carrier.carrierVisualType=type;
                carrier.tapBaseScale=1.2f;
                var sprite=type=="touch" ? loader.touchPrefab.GetComponent<TouchDrop>().fanNormalSprite : loader.starPrefab.GetComponent<Assets.Scripts.Notes.TapBase>().tapSpr;
                carrier.Configure(loader,route.segments,sprite,null,Vector2.one,0,0,2,0,skin);
                if(Mathf.Abs(note.transform.localScale.x-1.2f)>.001f) throw new Exception(type+" carrier scale mismatch");
                var pose=typeof(TrajectoryCarrierDrop).GetMethod("SetPose",flags);
                var facing=note.transform.localRotation;
                pose.Invoke(carrier,new object[]{.5f});
                if(type != "hold" && Quaternion.Angle(note.transform.localRotation,facing)>.001f) throw new Exception(type+" facing follows route tangent");
                if(type is "touch" or "touchhold")
                {
                    if (note.transform.childCount != (type == "touchhold" ? 7 : 5))
                        throw new Exception(type + " needs four petals, a centre point and a ring for TouchHold");
                    pose.Invoke(carrier,new object[]{0f}); var open=note.transform.GetChild(0).localPosition.magnitude;
                    pose.Invoke(carrier,new object[]{1f}); var closed=note.transform.GetChild(0).localPosition.magnitude;
                    var startRadius = type == "touchhold" ? .226f : .626f;
                    if (Math.Abs(open - startRadius) > .001f || Math.Abs(closed - .226f) > .001f)
                        throw new Exception(type + " petal motion mismatch");
                }
            }
            VerifySpiralHold(loader, root.transform, flags);
            Debug.Log("081 CARRIER/MOVE PASS: native scale, fixed facing, four petals, PV exclusion/inclusion/transition/rewind.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void VerifySpiralHold(JsonDataLoader loader, Transform parent, BindingFlags flags)
    {
        const string source = "2h[1:1]~[1d>>>C[1:1]]";
        if (!NoteExpressionParser.TryParse(source, out var parsed, out var error) || parsed.trajectory == null)
            throw new Exception("Cannot parse the reported inherited Hold: " + error);

        var note = new GameObject("Spiral Hold verification", typeof(SpriteRenderer));
        note.transform.SetParent(parent, false);
        var carrier = note.AddComponent<TrajectoryCarrierDrop>();
        carrier.carrierVisualType = "hold";
        carrier.holdLength = 3f;
        var holdRenderer = loader.holdPrefab.GetComponent<SpriteRenderer>();
        carrier.holdDrawMode = holdRenderer.drawMode;
        carrier.Configure(loader, parsed.trajectory.segments, holdRenderer.sprite, null, Vector2.one, 0, 0, 2, 0, null);
        var tail = note.transform.Find("Hold Tail");
        if (tail == null || tail.GetComponent<SpriteRenderer>().sprite == null || tail.localPosition.y >= 0f)
            throw new Exception("Inherited Hold needs the ordinary tail marker behind its head");

        var pose = typeof(TrajectoryCarrierDrop).GetMethod("SetPose", flags);
        var reverse = typeof(TrajectoryCarrierDrop).GetField("movingBackward", flags);
        Vector3 Sample(float progress)
        {
            pose.Invoke(carrier, new object[] { progress });
            return note.transform.localPosition + note.transform.up * (carrier.holdLength * .5f);
        }

        foreach (var backwards in new[] { false, true })
        {
            reverse.SetValue(carrier, backwards);
            for (var i = 1; i < 96; i++)
            {
                var progress = i / 96f;
                var head = Sample(progress);
                var facing = note.transform.up;
                var next = Sample(progress + (backwards ? -.0001f : .0001f));
                if (Vector3.Dot(facing, (next - head).normalized) < .999f)
                    throw new Exception($"Inherited Hold head does not face its travel at {progress:F3}, reverse={backwards}");
                if (Vector3.Dot(tail.position - head, facing) >= 0f)
                    throw new Exception("Tail marker is not behind the inherited Hold");
            }
        }
        Debug.Log("107 HOLD PASS: reported spiral, 190 forward/reverse samples, head follows travel tangent, tail marker trails.");
    }

    public static void Build()
    {
        Run();
        Reported061Verification.VerifyTouchRendering();
        Reported061Verification.VerifyTapLineAlpha();
        ExtraFilterVerification.Run();
        AlphaCommandLineBuild.BuildView();
    }
}
