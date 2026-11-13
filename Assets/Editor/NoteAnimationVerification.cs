using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using MajdataCore;

public static class NoteAnimationVerification
{
    private const BindingFlags Fields = BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private static object Call(object target,string method,params object[] args) =>
        target.GetType().GetMethod(method,Fields).Invoke(target,args);
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var clock=GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var loader=GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        VerifyPlaybackDirectory(loader,clock);
        var skinObject = new GameObject("Slide skin verification", typeof(SpriteRenderer));
        var skin = skinObject.AddComponent<CustomSkin>();
        foreach (var variant in new[] { "sd", "dx", "sd" })
        {
            skin.LoadSkin("dx", "dx", "dx", "dx", false, variant);
            foreach (var pair in new[] { (skin.Slide, "slide.png"), (skin.Slide_Break, "slide_break.png"), (skin.Slide_Each, "slide_each.png") })
            {
                var expectedSkin = SpriteLoader.LoadSpriteFromFile(System.IO.Path.GetFullPath("Skin/" + variant + "/" + pair.Item2));
                Check(pair.Item1.texture.width == expectedSkin.texture.width && pair.Item1.texture.height == expectedSkin.texture.height &&
                    System.Linq.Enumerable.SequenceEqual(pair.Item1.texture.GetPixels32(), expectedSkin.texture.GetPixels32()), "Slide skin does not match " + variant + "/" + pair.Item2);
            }
        }
        skin.LoadSkin("dx");
        UnityEngine.Object.DestroyImmediate(skinObject);
        Debug.Log("SLIDE SKIN PASS: SD/DX/SD normal, break and each pixels match selected skin.");

        var display=clock.gameObject.AddComponent<ViewFpsDisplay>();
        Call(display,"Start");
        var label=GameObject.Find("FPS Text").GetComponent<Text>();
        Call(display,"LateUpdate");
        Check(!label.enabled,"Top-left information must start disabled");
        display.SetMode(2,loader); Call(display,"LateUpdate");
        Check(label.enabled && label.text.StartsWith("BPM:"),"English BPM display");
        display.SetMode(3,loader); Call(display,"LateUpdate");
        Check(label.text=="4/4","Time signature should contain only the fraction");
        display.SetMode(4,loader); Call(display,"LateUpdate");
        Check(label.text=="4","Subdivision should contain only the number");
        display.SetMode(1,loader);
        label.text="120 FPS";
        typeof(ViewFpsDisplay).GetField("frames",Fields).SetValue(display,36);
        typeof(ViewFpsDisplay).GetField("elapsed",Fields).SetValue(display,.3f);
        for(var i=0;i<3;i++) display.SetMode(1,loader);
        Check(label.text=="120 FPS","Chart/settings refresh must preserve the FPS label");
        Check((int)typeof(ViewFpsDisplay).GetField("frames",Fields).GetValue(display)==36 &&
            (float)typeof(ViewFpsDisplay).GetField("elapsed",Fields).GetValue(display)==.3f,
            "Chart/settings refresh must preserve the FPS sampling interval");
        display.SetMode(2,loader); Call(display,"LateUpdate");
        Check(label.text.StartsWith("BPM:"),"Changing the display mode must still update the label");
        var canvas=GameObject.Find("CanvasInfo").GetComponent<Canvas>();
        var camera=canvas.worldCamera;
        var corners=new Vector3[4];
        foreach(var size in new[]{new Vector2Int(980,980),new Vector2Int(1600,900)})
        {
            var texture=new RenderTexture(size.x,size.y,24); texture.Create(); camera.targetTexture=texture;
            Canvas.ForceUpdateCanvases(); Call(display,"AlignToReference");
            var expected=GameObject.Find("TimeText").GetComponent<Text>();
            label.rectTransform.GetWorldCorners(corners);
            var topLeft=camera.WorldToScreenPoint(corners[1]);
            Check(Mathf.Abs(topLeft.x-8)<.1f && Mathf.Abs(topLeft.y-(size.y-8))<.1f,
                $"HUD top-left inset {size}: {topLeft}");
            Check(label.fontSize==expected.fontSize,"HUD reference font size");
            camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(texture);
        }
        display.SetMode(0,loader); Call(display,"LateUpdate");
        Check(!label.enabled,"Disabled mode must remain hidden");

        var touchObject=new GameObject("Touch alpha regression");
        var touch=touchObject.AddComponent<TouchDrop>();
        touch.previewOnly=true;
        touch.fans=new GameObject[7];
        var renderers=(SpriteRenderer[])typeof(TouchDrop).GetField("fansSprite",Fields).GetValue(touch);
        var material=new Material(Shader.Find("Sprites/NoteColorTint")); material.SetFloat("_NoteAlpha",.3f);
        for(var i=0;i<7;i++)
        {
            touch.fans[i]=new GameObject("fan "+i); touch.fans[i].transform.SetParent(touchObject.transform);
            renderers[i]=touch.fans[i].AddComponent<SpriteRenderer>(); renderers[i].sharedMaterial=material;
        }
        touch.multTouchEffect2=touch.fans[5]; touch.multTouchEffect3=touch.fans[6];
        Check(Mathf.Abs(renderers[5].sharedMaterial.GetFloat("_NoteAlpha")-.3f)<.001f &&
            Mathf.Abs(renderers[6].sharedMaterial.GetFloat("_NoteAlpha")-.3f)<.001f,
            "Touch white frames must share initial ALPHA opacity");
        touch.setLayer(0); touch.ApplyLiveAlpha(.2f); touch.setLayer(2);
        var properties=new MaterialPropertyBlock();
        foreach(var renderer in renderers)
        {
            Check(Mathf.Abs(renderer.sharedMaterial.GetFloat("_NoteAlpha")-1f)<.001f,"Live material should keep opacity in its property block");
            renderer.GetPropertyBlock(properties);
            Check(Mathf.Abs(properties.GetFloat("_NoteAlpha")-.2f)<.001f,"Touch white frames must share ALPHAV opacity");
        }
        touch.ApplyLiveScale(Vector2.zero); touch.ApplyLiveScale(Vector2.one);
        Check(touch.transform.localScale==Vector3.one,"Touch SIZE must recover from zero scale");
        UnityEngine.Object.DestroyImmediate(touchObject);
        UnityEngine.Object.DestroyImmediate(material);
        VerifyTouchJust(loader,clock);
        VerifyNoteAnimations(loader,clock);
        VerifyExOpacity(loader,clock);
        VerifyFakeMine(clock);
        VerifyTextColor(clock);
        Call(display,"OnDestroy");
        UnityEngine.Object.DestroyImmediate(label.gameObject);
        UnityEngine.Object.DestroyImmediate(display);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Debug.Log("NOTE ANIMATION VERIFICATION PASSED: fresh/replayed chart directory, HUD square/wide/off/refresh, Touch opacity, ANGLE centers/defaults/guide/reset, SIZE/ALPHA transitions");
    }

    private static void VerifyTouchJust(JsonDataLoader loader, AudioTimeProvider clock)
    {
        SvController.Load(new List<SvPoint>());
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Touch.prefab");
        var root = UnityEngine.Object.Instantiate(prefab);
        var touch = root.GetComponent<TouchDrop>();
        var border = touch.justEffect.GetComponent<SpriteRenderer>();
        var material = new Material(Shader.Find("Sprites/NoteColorTint"));
        var liveRoot = new GameObject("Touch white box alpha");
        var live = liveRoot.AddComponent<LiveNoteVisualController>();
        var skinRoot = new GameObject("Outline", typeof(SpriteRenderer), typeof(CustomSkin));
        try
        {
            Call(GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>(),"Start");
            touch.previewOnly = true;
            touch.areaPosition = 'C';
            touch.time = 2f;
            touch.noteScrollPos = SvController.GetCumulativeScroll(2d);
            touch.justSprite = border.sprite;
            material.SetFloat("_NoteAlpha", .35f);
            touch.colorOverrideMaterial = material;
            Call(touch,"Awake");
            Check(!touch.justEffect.activeSelf, "Uninitialized Touch must not show its white box");
            Call(touch,"Start");
            var alphaTableType = typeof(JsonDataLoader).GetMethod("BuildAlphaTimeline", Fields).GetParameters()[0].ParameterType;
            var changes = JsonConvert.DeserializeObject(@"[
                {'time':2.03,'noteType':'touch','alpha':0,'live':true},
                {'time':2.06,'noteType':'touch','reset':true,'live':true}
            ]", alphaTableType);
            Call(loader,"BuildAlphaTimeline",changes);
            Call(live,"Configure",null,null,changes,loader,clock);
            void Sample(float time, float alpha)
            {
                clock.AudioTime = time;
                Call(live,"Update");
                Call(touch,"Update");
                live.ApplyCurrent(touch);
                Check(touch.justEffect.activeSelf && Mathf.Abs(border.color.a - alpha) < .001f,
                    $"Touch white box at {time}: expected {alpha}, got {border.color.a}");
            }
            Sample(2f, .35f);
            Sample(2.04f, 0f);
            Sample(2.08f, .35f);
            Debug.Log("TOUCH WHITE BOX ALPHAV PASS: real Touch prefab, initial ALPHA, live zero and NULL restore.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(border.gameObject);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(liveRoot);
            UnityEngine.Object.DestroyImmediate(skinRoot);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void VerifyFakeMine(AudioTimeProvider clock)
    {
        SvController.Load(new List<SvPoint>());
        var previousMode = InputManager.Mode;
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Tap_01.prefab");
        var manager = GameObject.Find("Notes").GetComponent<NoteManager>();
        var counter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        var sprite = SpriteLoader.LoadSpriteFromFile(System.IO.Path.GetFullPath("Skin/dx/tap.png"));
        try
        {
            foreach (var mode in new[] { Assets.Scripts.Types.AutoPlayMode.Enable, Assets.Scripts.Types.AutoPlayMode.DJAuto })
            {
                InputManager.Mode = mode;
                var root = UnityEngine.Object.Instantiate(prefab, manager.transform);
                var tap = root.GetComponent<TapDrop>();
                tap.time = 5; tap.speed = 7; tap.startPosition = 1; tap.isMine = true; tap.isFake = true;
                tap.tapSpr = sprite;
                tap.noteScrollPos = SvController.GetCumulativeScroll(5);
                typeof(NoteDrop).GetMethod("BindSceneContext", Fields).Invoke(tap, new object[] { clock, counter, manager });
                Call(tap, "Start");
                clock.isStart = true; clock.AudioTime = 5.06f;
                Call(tap, "FixedUpdate"); Call(tap, "Update");
                var renderer = root.GetComponent<SpriteRenderer>();
                Check(!renderer.forceRenderingOff && root.transform.position.magnitude > tap.destroyRadius,
                    "Fake mine must remain visible past the judgement ring: " + mode);
                Check(!(bool)typeof(NoteDrop).GetField("isJudged", Fields).GetValue(tap),
                    "Fake mine must not judge: " + mode);
                tap.isFake = false; tap.previewOnly = true;
                Call(tap, "Update");
                Check(renderer.forceRenderingOff && root.transform.position.magnitude <= tap.destroyRadius + .001f,
                    "Real mine must retain its existing visual retirement: " + mode);
                UnityEngine.Object.DestroyImmediate(tap.tapLine);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        finally { InputManager.Mode = previousMode; clock.isStart = false; }
        Debug.Log("FAKE MINE PASS: Auto and DJAuto cross the ring without judging; real-mine visuals unchanged.");
    }

    private static void VerifyExOpacity(JsonDataLoader loader, AudioTimeProvider clock)
    {
        SvController.Load(new List<SvPoint>());
        var material=new Material(Shader.Find("Sprites/NoteColorTint"));
        material.SetFloat("_NoteAlpha",0f);
        var model=SpriteLoader.LoadSpriteFromFile(System.IO.Path.GetFullPath("Skin/dx/tap.png"));
        var exModel=SpriteLoader.LoadSpriteFromFile(System.IO.Path.GetFullPath("Skin/dx/tap_ex.png"));
        var manager=GameObject.Find("Notes").GetComponent<NoteManager>();
        var counter=GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        foreach(var name in new[]{"Tap_01","Star_01","Hold_01"})
        {
            var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/"+name+".prefab");
            var root=UnityEngine.Object.Instantiate(prefab);
            var note=root.GetComponent<NoteDrop>();
            note.previewOnly=true; note.time=5; note.speed=7;
            note.bounceDuration=2; note.ConfigureBounce(1);
            typeof(NoteDrop).GetMethod("BindSceneContext",Fields).Invoke(note,new object[]{clock,counter,manager});
            if(note is Assets.Scripts.Notes.TapBase tap)
            {
                tap.isEX=true; tap.tapSpr=model; tap.exSpr=exModel;
                tap.colorOverrideMaterial=material;
                if(tap is StarDrop star) star.isFakeStar=true;
            }
            else if(note is HoldDrop hold)
            {
                hold.isEX=true; hold.LastFor=.125f; hold.tapSpr=model; hold.exSpr=exModel;
                hold.colorOverrideMaterial=material;
            }
            Call(note,"Start");
            clock.isStart=true; clock.AudioTime=4;
            Call(note,"Update");
            var ex=root.transform.GetChild(0).GetComponent<SpriteRenderer>();
            Check(!ex.forceRenderingOff && ex.sprite!=null,"BOUNCE EX must be drawable: "+name+"; off="+ex.forceRenderingOff+"; sprite="+ex.sprite+"; start="+note.bounceStartTime);
            Check(Mathf.Abs(ex.color.a-1f)<.001f,"EX vertex alpha must not latch the initial zero: "+name);
            note.ApplyLiveAlpha(1f);
            var properties=new MaterialPropertyBlock(); ex.GetPropertyBlock(properties);
            Check(Mathf.Abs(properties.GetFloat("_NoteAlpha")-1f)<.001f,"ALPHAV must reveal EX: "+name);
            note.ApplyLiveAlpha(null); ex.GetPropertyBlock(properties);
            Check(properties.GetFloat("_NoteAlpha")==0f,"EX reset must restore authored initial opacity: "+name);
            note.ApplyLiveAlpha(.5f); ex.GetPropertyBlock(properties);
            Check(Mathf.Abs(ex.color.a*properties.GetFloat("_NoteAlpha")-.5f)<.001f,"EX opacity must be applied once: "+name);
            UnityEngine.Object.DestroyImmediate(root);
        }
        UnityEngine.Object.DestroyImmediate(material);
        clock.isStart=false;
        Debug.Log("BOUNCE EX PASS: Tap/Hold/Star; initial ALPHA zero; ALPHAV reveal, half opacity and reset.");
    }

    private static void VerifyTextColor(AudioTimeProvider clock)
    {
        var display=clock.GetComponent<DisplayTimelineController>();
        var caption=new SubtitleChange {time=0,text="RGB",font="Allerta-Regular.ttf",color="00BBFF",transition=2};
        Call(display,"Configure",new List<DisplayChange>(),new List<SubtitleChange>{caption},1,true,true,true,
            0f,0f,0,null,true,System.IO.Path.GetFullPath("."));
        Call(display,"UpdateFileCaptions",1f);
        var labels=(Dictionary<int,TMPro.TextMeshProUGUI>)typeof(DisplayTimelineController)
            .GetField("fileCaptionLabels",Fields).GetValue(display);
        var color=labels[0].color;
        Check(color.r==0 && Mathf.Abs(color.g-187f/255)<.001f && color.b==1 && Mathf.Abs(color.a-.5f)<.001f,
            "TEXT RGB must apply to the actual TMP caption and retain fade opacity");
        var label = labels[0];
        Check(label.rectTransform.anchoredPosition == Vector2.zero && label.alignment == TMPro.TextAlignmentOptions.TopLeft,
            "TEXT defaults to the screen top-left without extra margins");
        caption.origin = "Center"; caption.x = -.25f; caption.y = .1f;
        Call(display,"UpdateFileCaptions",1f);
        Check(label.alignment == TMPro.TextAlignmentOptions.Center &&
            (label.rectTransform.anchoredPosition - new Vector2(-.25f * Screen.width, -.1f * Screen.height)).sqrMagnitude < .001f &&
            label.rectTransform.sizeDelta == new Vector2(Screen.width, Screen.height),
            "TEXT Center must center the text and preserve signed screen-fraction offsets");
        var placement = (Rect)typeof(DisplayTimelineController).GetMethod("GetSubtitleRect", BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null, new object[] { caption, 32f, (float)Screen.width, (float)Screen.height });
        Check(placement.position == new Vector2(-.25f * Screen.width, .1f * Screen.height) &&
            placement.size == label.rectTransform.sizeDelta, "GUI and TMP captions must share coordinate placement");
        Debug.Log("TEXT COLOR PASS: file-font caption RGB and fade opacity.");
        Debug.Log("TEXT ORIGIN PASS: top-left default, Center alignment, signed offsets and shared GUI/TMP layout.");
    }

    private static void VerifyPlaybackDirectory(JsonDataLoader loader, AudioTimeProvider clock)
    {
        var controller=clock.GetComponent<DisplayTimelineController>() ?? clock.gameObject.AddComponent<DisplayTimelineController>();
        var configure=typeof(HttpHandler).GetMethod("ConfigureDisplayTimeline",BindingFlags.NonPublic|BindingFlags.Static);
        var requestType=configure.GetParameters()[5].ParameterType;
        var dataField=typeof(JsonDataLoader).GetField("loadedData",Fields);
        var directoryField=typeof(DisplayTimelineController).GetField("chartDirectory",Fields);
        void Configure(string requestJson,string expected)
        {
            var request=JsonConvert.DeserializeObject(requestJson,requestType);
            configure.Invoke(null,new object[]{controller,null,null,clock,"{}",request,null});
            Check((string)directoryField.GetValue(controller)==expected,"Display timeline must use the current playback directory");
        }
        dataField.SetValue(loader,null);
        Configure("{'chartDirectory':'D:/Charts/first','jsonPath':'D:/Preview/track.json'}","D:/Charts/first");
        // Replay can start before LoadJson replaces the previous chart (whose filePath is empty).
        dataField.SetValue(loader,JsonConvert.DeserializeObject("{}",dataField.FieldType));
        Configure("{'chartDirectory':'D:/Charts/second','jsonPath':'D:/Preview/track.json'}","D:/Charts/second");
        var previewPath=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"track.json");
        Configure(JsonConvert.SerializeObject(new {jsonPath=previewPath}),System.IO.Path.GetDirectoryName(previewPath));
        dataField.SetValue(loader,null);
    }

    private static void VerifyNoteAnimations(JsonDataLoader loader,AudioTimeProvider clock)
    {
        SvController.Load(new List<SvPoint>());
        var dataField=typeof(JsonDataLoader).GetField("loadedData",Fields);
        var data=JsonConvert.DeserializeObject(@"{
            'angleTable':[{'time':0,'angle':45},
                {'time':3,'noteType':'tap','live':true,'transition':{'from':0,'to':90,'duration':2}}],
            'sizeTable':[{'time':0,'transition':{'from':0,'to':2,'fromY':0,'toY':1,'duration':1}}],
            'alphaTable':[{'time':3,'live':true,'transition':{'from':0,'to':1,'duration':2}}],
            'colorTable':[{'time':0,'color':'0000FF','transition':{'from':1,'to':0,'fromY':0,'toY':0,'fromZ':0,'toZ':1,'duration':2}},
                {'time':3,'noteType':'tap','color':'FFFF00','live':true,'transition':{'from':0,'to':1,'fromY':1,'toY':1,'fromZ':0,'toZ':0,'duration':2}}],
            'spawnTable':[{'time':0,'radius':2},{'time':0,'noteType':'tap','reset':true}]
        }",dataField.FieldType);
        object Table(string name) => data.GetType().GetField(name,Fields).GetValue(data);
        dataField.SetValue(loader,data);
        Call(loader,"BuildSizeTimeline",Table("sizeTable"));
        Call(loader,"BuildAlphaTimeline",Table("alphaTable"));
        Call(loader,"BuildColorTimeline",Table("colorTable"));
        Call(loader,"BuildSpawnTimeline",Table("spawnTable"));
        Call(loader,"BuildNoteAnimations");
        var tapObject=new GameObject("ANGLE regression tap");
        var tap=tapObject.AddComponent<TapDrop>(); tap.previewOnly=true; tap.time=2;
        tap.transform.position=new Vector3(2,3,0); tap.transform.localRotation=Quaternion.Euler(0,0,10);
        var animation=tapObject.AddComponent<NotePropertyAnimation>();
        Call(animation,"Configure",tap,loader,clock); Call(animation,"Start");
        clock.AudioTime=2; Call(animation,"Update"); Call(animation,"LateUpdate");
        Check(Quaternion.Angle(tap.transform.localRotation,Quaternion.Euler(0,0,55))<.01f,"ANGLE rotation about artwork center");
        Check(tap.transform.position==new Vector3(2,3,0),"ANGLE must not move the note");
        Call(animation,"Update"); Call(animation,"LateUpdate");
        Check(Quaternion.Angle(tap.transform.localRotation,Quaternion.Euler(0,0,55))<.01f,"ANGLE must not accumulate each frame");
        clock.AudioTime=4; Call(animation,"Update"); Call(animation,"LateUpdate");
        Check(Quaternion.Angle(tap.transform.localRotation,Quaternion.Euler(0,0,55))<.01f,"ANGLEV interpolates from command time");
        Check(Mathf.Abs((float)Call(loader,"AnimatedAlpha",tap,4d,1d,false)-.5f)<.001f,"ALPHAV interpolates loaded-note opacity");
        var color=(Color)Call(loader,"AnimatedColor",tap,2d,1d,false);
        Check(Mathf.Abs(color.r-.5f)<.001f && color.g==0 && Mathf.Abs(color.b-.5f)<.001f,
            "COLOR interpolates RGB from each note's appearance");
        color=(Color)Call(loader,"AnimatedColor",tap,4d,1d,false);
        Check(Mathf.Abs(color.r-.5f)<.001f && color.g==1 && color.b==0,
            "COLORV interpolates from the authored command time");
        tap.isMine=true;
        Check(Call(loader,"AnimatedColor",tap,4d,1d,false)==null,"Global and tap RGB curves must not recolor mines");
        tap.isMine=false;
        var scale=(Vector2)Call(loader,"AnimatedSize",tap,1.5d,1d,false);
        Check((scale-new Vector2(1,.5f)).sqrMagnitude<.00001f,"SIZE interpolates both axes from note appearance");
        var angleQuery=(Func<NoteDrop,double,double,bool,float>)Delegate.CreateDelegate(
            typeof(Func<NoteDrop,double,double,bool,float>),loader,typeof(JsonDataLoader).GetMethod("AnimatedAngle",Fields));
        angleQuery(tap,4,1,false);
        var allocated=GC.GetAllocatedBytesForCurrentThread();
        var timer=System.Diagnostics.Stopwatch.StartNew();
        for(var i=0;i<10000;i++) angleQuery(tap,4,1,false);
        timer.Stop();
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(allocated<1024,$"Angle queries must not allocate each frame: {allocated} bytes");
        Debug.Log($"ANGLE HOT PATH: 10000 queries, {timer.Elapsed.TotalMilliseconds:F2}ms, {allocated} bytes");
        var radius=(float)typeof(JsonDataLoader).GetMethod("GetSpawnRadiusAt",Fields).Invoke(loader,
            new object[]{"tap",2d,false,false,0,false});
        Check(Mathf.Abs(radius-2)<.001f,"Reset typed SPAWN must inherit the global radius");

        var slideObject=new GameObject("ANGLE regression slide");
        var slide=slideObject.AddComponent<SlideDrop>(); slide.previewOnly=true; slide.timeStart=2;
        float Angle(bool guide) => (float)Call(loader,"AnimatedAngle",slide,2d,1d,guide);
        Check(Angle(false)==0 && Angle(true)==0,
            "Global ANGLE must leave paths and guide stars unchanged");
        var angles=(IList)Table("angleTable");
        var angleType=angles.GetType().GetGenericArguments()[0];
        angles.Add(JsonConvert.DeserializeObject("{'time':0,'noteType':'slidestar','angle':-30}",angleType));
        Call(loader,"BuildNoteAnimations");
        Check(Angle(false)==0 && Angle(true)==-30,
            "Explicit slidestar target must not rotate the path");
        angles.Add(JsonConvert.DeserializeObject("{'time':1,'noteType':'slidestar','reset':true}",angleType));
        Call(loader,"BuildNoteAnimations");
        Check(Angle(true)==0,"ANGLE NULL restores original guide orientation");
        var speeds=(IList)Table("hsTable");
        speeds.Add(JsonConvert.DeserializeObject(
            "{'time':0,'noteType':'slide','transition':{'from':1,'to':2,'duration':1}}",
            speeds.GetType().GetGenericArguments()[0]));
        Call(loader,"BuildHSpeedTimeline",speeds); Call(loader,"BuildNoteAnimations");
        slide.scrollType="slide"; slide.time=3; slide.speed=loader.noteSpeed;
        var slideAnimation=slideObject.AddComponent<NotePropertyAnimation>();
        Call(slideAnimation,"Configure",slide,loader,clock); Call(slideAnimation,"Start");
        var appeared=(double)typeof(NotePropertyAnimation).GetField("appearance",Fields).GetValue(slideAnimation);
        var middle=(float)Call(slideAnimation,"SlideFadeProgress",(appeared+slide.timeStart)*.5d);
        Check(middle>0 && middle<1 && (float)Call(slideAnimation,"SlideFadeProgress",(double)slide.timeStart)==1,
            "HS*slide must drive actual appearance progress");
        Check(slide.time==3 && slide.scrollType=="slide","HS*slide must retain its movement time and SV route");
        UnityEngine.Object.DestroyImmediate(tapObject);
        UnityEngine.Object.DestroyImmediate(slideObject);
        SvController.Clear();
    }
}
