#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assets.Scripts.Types;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NoiseZoneVerification
{
    public static void Run()
    {
        foreach(var name in new[]{"BlockNoise1","PointNoise","FD_Noise_00000","TouchHover"}) {
            var importer=(TextureImporter)AssetImporter.GetAtPath($"Assets/Resources/NoiseZone/{name}.png");
            importer.textureType=TextureImporterType.Default; importer.sRGBTexture=false;
            importer.mipmapEnabled=false; importer.filterMode=FilterMode.Point;
            importer.wrapMode=name=="PointNoise"?TextureWrapMode.Repeat:TextureWrapMode.Mirror;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        VerifyNativeColorAndParticles();
        var root = new GameObject("Noise input and mesh verification");
        var clock = root.AddComponent<AudioTimeProvider>();
        var notes = new GameObject("Noise test playfield");
        var controller = root.AddComponent<NoiseZoneController>();
        var sensor = UnityEngine.Object.FindObjectsByType<Sensor>(FindObjectsSortMode.None).First(s => s.Type == SensorType.D1);
        var cameraRoot=new GameObject("Noise GPU test camera");
        var camera=cameraRoot.AddComponent<Camera>(); camera.enabled=false;
        camera.orthographic=true; camera.orthographicSize=5.2f; camera.aspect=1;
        camera.transform.position=new Vector3(0,0,-10);
        var effect=cameraRoot.AddComponent<NoiseZoneImageEffect>();
        int clicks=0;
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        void OnInput(object sender, InputEventArgs args) { if (args.IsClick) clicks++; }
        sensor.OnStatusChanged += OnInput;
        var button = new Button(KeyCode.W, SensorType.A1);
        button.OnStatusChanged += OnInput;
        var timings = new List<SimaiTimingPoint>
        {
            new() { time = 2, noteList = new List<SimaiNote>
            {
                new() { noteType=SimaiNoteType.NoiseZone, touchArea='D', startPosition=1, holdTime=2 },
                new() { noteType=SimaiNoteType.NoiseZone, touchArea='A', startPosition=1, holdTime=2 },
                new() { noteType=SimaiNoteType.NoiseZone, touchArea='C', startPosition=1, holdTime=2 },
                new() { noteType=SimaiNoteType.NoiseZone, touchArea='E', startPosition=1, holdTime=2 },
                new() { noteType=SimaiNoteType.NoiseZone, touchArea='E', startPosition=2, holdTime=2 },
                new() { noteType=SimaiNoteType.NoiseZone, touchArea='E', startPosition=3, holdTime=2 },
                new() { noteType=SimaiNoteType.NoiseZone, touchArea='E', startPosition=4, holdTime=2 }
            }}
        };
        var pulse = new InputEventArgs { Type=SensorType.A1, OldStatus=SensorStatus.Off, Status=SensorStatus.On, IsButton=true };
        try
        {
            VerifySpeedAndStyleDefaults(root, Require);
            clock.AudioTime=1.6f;
            controller.Configure(timings, notes.transform, clock, _=>.5); effect.Bind(controller);
            Require(!NoiseZoneInputGate.IsBlocked(sensor.Type), "Warning disables input too early");
            Require(sensor.PulseForAutoPlay() && clicks==1, "DJAuto warning-phase pulse lost");
            var held=Guid.NewGuid(); sensor.SetOn(held);
            clock.AudioTime=2.2f;
            Require(NoiseZoneInputGate.IsBlocked(sensor.Type) && sensor.Status==SensorStatus.Off, "Active noise did not gate held status");
            int before=clicks;
            sensor.Click(); sensor.SetOn(Guid.NewGuid());
            Require(!sensor.PulseForAutoPlay() && clicks==before, "Noise leaks tap or DJAuto input");
            button.Status=SensorStatus.On; button.PushEvent(pulse);
            Require(button.Status==SensorStatus.Off && clicks==before,"A-zone noise leaks an outer button");
            sensor.SetOff(held);
            typeof(NoiseZoneController).GetMethod("RenderAt",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{2.2d});
            foreach(var shaderName in new[]{"AlphaNoiseZone","AlphaNoiseComposite"}) {
                var shader=Resources.Load<Shader>(shaderName);
                Require(shader && !ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Noise shader failed: "+shaderName);
            }
            var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..",".codex-build","FilterPreview"));
            Directory.CreateDirectory(folder);
            var colored=Capture(notes,effect,camera,controller,Path.Combine(folder,"NOISE_UNION.png"));
            Require(colored.Count(c=>c.r>c.g+20)>1000,"Composed noise did not render red regions");
            clock.AudioTime=4f;
            Require(!NoiseZoneInputGate.IsBlocked(sensor.Type) && sensor.PulseForAutoPlay(),"Sensor remains disabled after end");
            clock.AudioTime=1.6f;
            Require(!NoiseZoneInputGate.IsBlocked(sensor.Type),"Backward seek retains a disabled sensor");
            clock.AudioTime=2.2f;
            Require(NoiseZoneInputGate.IsBlocked(sensor.Type),"Seeking into noise does not restore blocking");
            controller.Clear();
            Require(!NoiseZoneInputGate.IsBlocked(sensor.Type),"Clear keeps noise input state");
            // Staggered arrival and departure on the actual 33-sensor playfield.
            timings = new List<SimaiTimingPoint> {
                new() { time=2, noteList=new List<SimaiNote> { new() { noteType=SimaiNoteType.NoiseZone,touchArea='D',startPosition=1,holdTime=6 } } },
                new() { time=3, noteList=new List<SimaiNote> { new() { noteType=SimaiNoteType.NoiseZone,touchArea='E',startPosition=1,holdTime=3 } } },
                new() { time=4, noteList=new List<SimaiNote> { new() { noteType=SimaiNoteType.NoiseZone,touchArea='B',startPosition=1,holdTime=5 } } }
            };
            clock.AudioTime=2.2f; controller.Configure(timings,notes.transform,clock,_=>.5); effect.Bind(controller);
            var actualCenters=(System.Numerics.Vector2[])typeof(NoiseZoneController).GetField("centers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            var actualCells=(List<System.Numerics.Vector2>[])typeof(NoiseZoneController).GetField("cells",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            void Render(double time) {
                clock.AudioTime=(float)time;
                typeof(NoiseZoneController).GetMethod("RenderAt",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{time});
            }
            double whitePeak=(Math.PI*.5+Math.PI*2*10)/37.9;
            Render(whitePeak); var readyWhite=Capture(notes,effect,camera,controller,Path.Combine(folder,"NOISE_READY_WHITE.png"));
            Render(whitePeak+Math.PI/37.9); var readyDim=Capture(notes,effect,camera,controller,Path.Combine(folder,"NOISE_READY_DIM.png"));
            Require(readyWhite.Sum(c=>(long)c.g+c.b)>readyDim.Sum(c=>(long)c.g+c.b),"Soft ready flash does not brighten actual pixels");
            Require(!readyWhite.SequenceEqual(readyDim) && !NoiseZoneInputGate.IsBlocked(SensorType.D1),"Ready flash does not change or blocks warning input");
            Require(readyDim.Count(c => c.r > c.g + 20 && c.g > 10 && c.b > 10) > 100,
                "Prewarning lost Phira-Pro's warm-red fill and outline palette");
            int MaskArea(double time) {
                Render(time); var mask=new byte[128*128];
                controller.FillActiveMask(camera,controller.FieldMatrix,128,128,mask);
                return mask.Count(v=>v!=0);
            }
            int enterEarly=MaskArea(3.01),enterMiddle=MaskArea(3.12),enterFull=MaskArea(3.3);
            Require(enterEarly<enterMiddle && enterMiddle<enterFull,"Adjacent noise does not grow from its live neighbor");
            int leaveFull=MaskArea(5.7),leaveMiddle=MaskArea(5.92),leaveEnd=MaskArea(5.999);
            Require(leaveFull>leaveMiddle && leaveMiddle>=leaveEnd,"Adjacent noise does not retreat before its end");
            Require(NoiseZoneInputGate.IsBlocked(SensorType.E1),"Visual retreat prematurely re-enables judgement");
            void CheckSeam(int first,int second,bool expected) {
                var cell=actualCells[first]; bool found=false;
                var selected=(bool[])typeof(NoiseZoneController).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
                for(int v=0;v<cell.Count;v++) {
                    var a=cell[v]; var b=cell[(v+1)%cell.Count]; var middle=(a+b)*.5f;
                    if(Math.Abs(System.Numerics.Vector2.DistanceSquared(middle,actualCenters[first])-System.Numerics.Vector2.DistanceSquared(middle,actualCenters[second]))>.0001f) continue;
                    found=true;
                    bool boundary=MajdataCore.NoiseZoneGeometry.IsUnionBoundary(first,a,b,actualCenters,selected,4.8f);
                    Require(boundary==expected,$"Actual noise seam {first}/{second}: expected={expected}");
                }
                Require(found,$"Actual sensor regions {first}/{second} are not neighbors");
            }
            Render(2.8); CheckSeam(17,25,true);
            Render(3.2); CheckSeam(17,25,false);
            Render(4.2); CheckSeam(25,8,false);
            Render(6.2); CheckSeam(17,25,true); CheckSeam(8,25,true);
            var previewFolder=Path.GetFullPath(Path.Combine(Application.dataPath,"..",".codex-build","FilterPreview"));
            foreach(var time in new[]{1.58d,1.66d,2.2d,2.8d,3.01d,3.12d,3.3d,4.2d,5.92d,5.999d,6.2d,8.2d,9.2d}) {
                Render(time);
                Capture(notes,effect,camera,controller,Path.Combine(previewFolder,$"NOISE_STAGE_{time:F1}.png"));
            }
            Render(4.2); var calm=Capture(notes,effect,camera,controller,null);
            Render(4.32); var later=Capture(notes,effect,camera,controller,null);
            int changed=0;
            for(int n=0;n<calm.Length;n++) {
                if(Math.Abs(calm[n].r-later[n].r)+Math.Abs(calm[n].g-later[n].g)+Math.Abs(calm[n].b-later[n].b)>6) changed++;
            }
            
            Require(changed>20,"Noise boundary has no animated pixel emissions");
            Render(4.2); var replay=Capture(notes,effect,camera,controller,null);
            Require(calm.SequenceEqual(replay),"Noise pixels do not reproduce after seeking");
            controller.Clear();
            timings=new List<SimaiTimingPoint> {
                new() { time=0,noteList=new List<SimaiNote> { new() { noteType=SimaiNoteType.NoiseZone,touchArea='B',startPosition=1,holdTime=5 } } },
                new() { time=2,noteList=new List<SimaiNote> { new() { noteType=SimaiNoteType.NoiseZone,touchArea='C',startPosition=1,holdTime=0 } } },
                new() { time=2.5,noteList=new List<SimaiNote> { new() { noteType=SimaiNoteType.NoiseZone,touchArea='C',startPosition=1,holdTime=2f/384 } } },
                new() { time=3,noteList=new List<SimaiNote> { new() { noteType=SimaiNoteType.NoiseZone,touchArea='C',startPosition=1,holdTime=.15 } } }
            };
            clock.AudioTime=1.9f; controller.Configure(timings,notes.transform,clock,_=>.5); effect.Bind(controller);
            int baseArea=MaskArea(1.9);
            Require(((Mesh)typeof(NoiseZoneController).GetField("mesh",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller)).vertexCount>0,"Zero duration lost its warning mesh");
            Require(MaskArea(2)==baseArea && MaskArea(2.05)==baseArea,"Zero duration creates a visible active field");
            Require(!NoiseZoneInputGate.IsBlocked(SensorType.C),"Zero duration blocks input");
            int shortArea=MaskArea(2.502);
            Require(shortArea>baseArea+50 && MaskArea(2.506)==baseArea,"384th noise does not render for its exact duration");
            MaskArea(2.502);
            Capture(notes,effect,camera,controller,Path.Combine(folder,"NOISE_SHORT_GLITCH.png"));
            Require(MaskArea(2.51)==baseArea,"Short visual pulse remains past its actual duration");
            Require(MaskArea(3.001)==shortArea && MaskArea(3.13)==shortArea && MaskArea(3.16)==baseArea,"Short explicit duration is consumed by grow/shrink transitions");
            timings=new List<SimaiTimingPoint> { new() { time=2,noteList=new List<SimaiNote> {
                new() { noteType=SimaiNoteType.NoiseZone,touchArea='C',startPosition=1,holdTime=2 }
            } } };
            clock.AudioTime=3;controller.Configure(timings,notes.transform,clock,_=>.5);effect.Bind(controller);
            int isolatedFull=MaskArea(3.5),isolatedHalf=MaskArea(3.88),isolatedLast=MaskArea(3.97);
            Require(isolatedFull>isolatedHalf && isolatedHalf>isolatedLast,
                "Isolated noise does not shrink progressively into its own sensor center");
            Require(MaskArea(4.001)==0,"Isolated shrink persists after its end time");
            clock.AudioTime=3;
            controller.Configure(timings,notes.transform,clock,_=>.5,
                (_,time)=>new NoiseZoneVisual(new Color(0,.5f,1,time>=3.7?0:1), time>=3.5?Vector2.one*.5f:Vector2.one));
            effect.Bind(controller);
            int fullStyleArea=MaskArea(3.3),smallStyleArea=MaskArea(3.6);
            Require(smallStyleArea>0 && smallStyleArea<fullStyleArea*.35,
                "Noise SIZE does not scale the actual region");
            var blue=Capture(notes,effect,camera,controller,Path.Combine(folder,"NOISE_COLOR_SIZE.png"));
            Require(blue.Count(c=>c.b>c.r+15)>300,"Noise COLOR does not recolor actual GPU pixels");
            Render(3.75);
            var hidden=Capture(notes,effect,camera,controller,null);
            Require(hidden.Count(c=>c.b>c.r+15)==0 && NoiseZoneInputGate.IsBlocked(SensorType.C),
                "Noise ALPHA zero leaks visuals or incorrectly changes input blocking");
            Render(3.6);
            var filters=cameraRoot.GetComponent<ScreenEffectController>();
            filters.Configure(new List<EffectChange>{new(){time=0,effect="Saturation",intensity=1,stateful=true,enabled=true}},clock);
            var composed=RenderTexture.GetTemporary(512,512,0);
            var filtered=RenderTexture.GetTemporary(512,512,0);
            var pixels=new Texture2D(512,512,TextureFormat.RGBA32,false);
            var oldTarget=RenderTexture.active;
            try {
                RenderTexture.active=composed; GL.Clear(true,true,new Color(.12f,.12f,.12f,1));
                typeof(NoiseZoneImageEffect).GetField("fieldMatrix",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(effect,controller.FieldMatrix);
                var render=typeof(ScreenEffectController).GetMethod("OnRenderImage",BindingFlags.NonPublic|BindingFlags.Instance);
                render.Invoke(filters,new object[]{composed,filtered}); // shader warmup
                render.Invoke(filters,new object[]{composed,filtered});
                RenderTexture.active=filtered; pixels.ReadPixels(new Rect(0,0,512,512),0,0); pixels.Apply();
                var result=pixels.GetPixels32();
                Require(result.Count(c=>c.b>c.r+15)==0 && result.Count(c=>c.r>40)>300,
                    "Common filters do not affect the composed noise pixels");
            } finally {
                RenderTexture.active=oldTarget; RenderTexture.ReleaseTemporary(composed); RenderTexture.ReleaseTemporary(filtered);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
            Debug.Log("NOISE STYLE CHECK PASS: actual GPU COLOR, live SIZE/ALPHA, preserved blocking and noise-before-filter composition.");
            controller.Clear();
            Debug.Log("NOISE RUNTIME PASS: real sensor, held input, outer button, DJAuto, seek, reset, Phira-Pro composed masks, real GPU pixels, staggered merge/split and deterministic seek");
        }
        finally
        {
            sensor.OnStatusChanged-=OnInput;
            controller.Clear();
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(notes); UnityEngine.Object.DestroyImmediate(cameraRoot);
        }
    }
    private static void VerifySpeedAndStyleDefaults(GameObject root, Action<bool,string> require)
    {
        var loader=root.AddComponent<JsonDataLoader>();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object Call(string name,params object[] args) => typeof(JsonDataLoader).GetMethod(name,flags).Invoke(loader,args);
        var timing=new SimaiTimingPoint {time=4,HSpeed=1};
        float Warning(double time) => (float)Call("ResolveNoiseWarningProgress",timing,time);
        float Lead(float sv,float hs) {
            SvController.Load(new List<SvPoint>{new(){time=0,multiplier=sv}});
            Call("BuildHSpeedTimeline",new List<SpeedChange>{new(){time=0,noteType="",multiplier=hs}});
            return (float)SvController.GetTouchWarningLead(timing.time,loader.touchSpeed*hs,"blockarea");
        }
        var normalLead=Lead(1,1); var doubleSvLead=Lead(2,1);
        require(Math.Abs(doubleSvLead*2-normalLead)<.00001,"Actual noise lead is not halved by global SV times two");
        require(Warning(4-normalLead*.75)<0 && Warning(4-normalLead*.25)>0,"Global SV times two does not delay actual warning onset");
        var doubleHsLead=Lead(1,2);
        require(doubleHsLead<normalLead*.55 && Warning(4-normalLead*.75)<0,"Global HS times two does not delay actual noise warning onset");
        SvController.Load(new List<SvPoint>{new(){time=0,noteType="blockarea",multiplier=2}});
        Call("BuildHSpeedTimeline",new List<SpeedChange>{new(){time=0,noteType="blockarea",multiplier=2}});
        require(Warning(4-normalLead*.3)<0 && Warning(4-normalLead*.1)>0,"Explicit noise SV/HS override was ignored");
        SvController.Load(new List<SvPoint>());
        Call("BuildHSpeedTimeline",new List<SpeedChange>());
        var inheritedTiming=new SimaiTimingPoint {time=4,HSpeed=2};
        require((float)Call("ResolveNoiseWarningProgress",inheritedTiming,4-normalLead*.75)<0,"Legacy timing HS fallback was lost");
        Call("BuildColorTimeline",new List<ColorChange>{new(){time=0,noteType="",color="00BBFF",live=true}});
        Call("BuildSizeTimeline",new List<SizeChange>{new(){time=0,noteType="",scale=.2f,scaleX=.2f,scaleY=.2f,live=true}});
        Call("BuildAlphaTimeline",new List<AlphaChange>{new(){time=0,noteType="",alpha=.1f,live=true}});
        var style=(NoiseZoneVisual)Call("ResolveNoiseVisual",timing,3d);
        require(!style.CustomColor && style.Color.a==1 && style.Size==Vector2.one,"Global visual controls leak into noise");
        Call("BuildColorTimeline",new List<ColorChange>{new(){time=0,noteType="blockarea",color="00BBFF",live=true}});
        Call("BuildSizeTimeline",new List<SizeChange>{new(){time=0,noteType="blockarea",scale=.5f,scaleX=.5f,scaleY=.5f,live=true}});
        Call("BuildAlphaTimeline",new List<AlphaChange>{new(){time=0,noteType="blockarea",alpha=.4f,live=true}});
        style=(NoiseZoneVisual)Call("ResolveNoiseVisual",timing,3d);
        require(style.CustomColor && Math.Abs(style.Color.a-.4f)<.0001 && style.Size==Vector2.one*.5f,"Explicit noise visual controls failed");
        UnityEngine.Object.DestroyImmediate(loader);
        Debug.Log("NOISE SPEED CHECK PASS: real loader global/typed SV and HS, legacy HS, Touch warning threshold and explicit-only visual defaults.");
    }

    private static void VerifyNativeColorAndParticles()
    {
        var material = new Material(Resources.Load<Shader>("AlphaNoiseComposite"));
        Texture2D Constant(Color color) {
            var texture = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Point };
            texture.SetPixel(0,0,color); texture.Apply(); return texture;
        }
        var displacement=Constant(new Color(.5f,.5f,.5f,1));
        var masks=Constant(new Color(1,0,0,1)); var styles=Constant(Color.red);
        var sparks=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat };
        float Spark(int x,int y) => ((x/3+y/5)%4)*.125f;
        for(int y=0;y<128;y++) for(int x=0;x<128;x++) sparks.SetPixel(x,y,new Color(Spark(x,y),0,0,1));
        sparks.Apply(); material.SetTexture("_DisplaceTex",displacement); material.SetTexture("_SparkTex",sparks);
        material.SetTexture("_Masks",masks); material.SetTexture("_Styles",styles); material.SetFloat("_ZoneTime",0);
        var oldTarget=RenderTexture.active;
        try {
            foreach(var size in new[]{new Vector2Int(256,256),new Vector2Int(512,256),new Vector2Int(1024,512)}) {
                var source=new Texture2D(size.x,size.y,TextureFormat.RGBAFloat,false,true);
                var palette=new[]{new Color(1,.8f,.05f),new Color(.05f,.5f,1),new Color(.12f,.12f,.12f)};
                for(int y=0;y<size.y;y++) for(int x=0;x<size.x;x++) source.SetPixel(x,y,palette[Math.Min(2,x*3/size.x)]);
                source.Apply();
                var target=new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); target.Create();
                var pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false,true);
                try {
                    material.SetVector("_OutputSize",new Vector4(size.x,size.y,1f/size.x,1f/size.y));
                    Graphics.Blit(source,target,material,3); RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0); pixels.Apply();
                    // Independent transcription of the reference ActiveBlock
                    // HSV/overlay/premultiplied blend, not a red-pixel count.
                    for(int bar=0;bar<3;bar++) for(int step=0;step<13;step++) {
                        int x=(int)((bar+.5f)*size.x/3),y=9+step*(size.y-20)/13;
                        float u=(x+.5f)/size.x,v=(y+.5f)/size.y;
                        var sparkColor=new Color(1,.28490567f,.28490567f)*Spark((int)(Mathf.Repeat(u*3,1)*128),(int)(Mathf.Repeat(v*1.2f,1)*128))*.5f*5.69f;
                        Color.RGBToHSV(palette[bar],out var h,out var s,out var value);
                        var shifted=Color.HSVToRGB(Mathf.Repeat(h+sparkColor.r*.2f,1),s+sparkColor.g*.2f,value+sparkColor.b*.2f,true);
                        float Overlay(float a,float b) => Mathf.Clamp01(a>=.5f ? 1-2*(1-a)*(1-b) : 2*a*b);
                        var mixed=new Color(Overlay(shifted.r,sparkColor.r),Overlay(shifted.g,sparkColor.g),Overlay(shifted.b,sparkColor.b));
                        var expected=(new Color(.7132075f,.23549296f,.23549296f)-Color.white*(.5f*.411f))*(1-.667f)+mixed*.667f+palette[bar]*(1-.667f);
                        var actual=pixels.GetPixel(x,y);
                        if(Math.Abs(actual.r-Mathf.Clamp01(expected.r))>.02f || Math.Abs(actual.g-Mathf.Clamp01(expected.g))>.02f || Math.Abs(actual.b-Mathf.Clamp01(expected.b))>.02f)
                            throw new Exception($"Native noise color/particle mismatch {size} at {x},{y}: {actual} vs {expected}");
                    }
                    VerifyNativeWarningParticles(size,displacement,sparks,Spark);
                    if(size.x==512) {
                        var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..",".codex-build","FilterPreview")); Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder,"NOISE_NOTE_COLOR_REFERENCE.png"),pixels.EncodeToPNG());
                    }
                } finally { target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(pixels); }
            }
        } finally {
            RenderTexture.active=oldTarget;
            foreach(var item in new UnityEngine.Object[]{material,displacement,masks,styles,sparks}) UnityEngine.Object.DestroyImmediate(item);
        }
        Debug.Log("NOISE NATIVE COLOR PASS: yellow/blue note colors, HSV refraction, premultiplied blending and native particle UV at three resolutions/aspect ratios.");
    }

    private static void VerifyNativeWarningParticles(Vector2Int size,Texture displacement,Texture sparks,Func<int,int,float> sparkAt)
    {
        var material=new Material(Resources.Load<Shader>("AlphaNoiseZone"));
        material.SetTexture("_DisplaceTex",displacement); material.SetTexture("_SparkTex",sparks); material.SetFloat("_ZoneTime",0);
        var mesh=new Mesh { vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1),Vector3.up},
            uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},
            colors=Enumerable.Repeat(new Color(1,1,1,.75f),4).ToArray(),triangles=new[]{0,1,2,0,2,3} };
        mesh.SetUVs(1,Enumerable.Repeat(Vector2.right,4).ToList());
        var target=new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); target.Create();
        var pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false,true);
        var oldTarget=RenderTexture.active;
        try {
            RenderTexture.active=target; GL.Clear(true,true,new Color(.12f,.12f,.12f,1));
            GL.PushMatrix();
            try { GL.LoadOrtho(); material.SetPass(0); Graphics.DrawMeshNow(mesh,Matrix4x4.identity); }
            finally { GL.PopMatrix(); }
            pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0); pixels.Apply();
            for(int step=0;step<25;step++) {
                int x=7+step*(size.x-16)/25,y=17+(step*37)%(size.y-34);
                float u=(x+.5f)/size.x,v=(y+.5f)/size.y;
                float spark=sparkAt((int)(Mathf.Repeat(u*3,1)*128),(int)(Mathf.Repeat(v*1.2f,1)*128));
                var expected=new Color(.12f,.12f,.12f)+(new Color(.497f,.13766898f,.13766898f)*.4f+
                    new Color(.31132078f,.077830195f,.077830195f)*spark*.5f*3.5f)*.75f;
                var actual=pixels.GetPixel(x,y);
                if(Math.Abs(actual.r-expected.r)>.02f || Math.Abs(actual.g-expected.g)>.02f || Math.Abs(actual.b-expected.b)>.02f)
                    throw new Exception($"Native warning blend/particle size mismatch {size} at {x},{y}: {actual} vs {expected}");
            }
        } finally {
            RenderTexture.active=oldTarget; target.Release();
            foreach(var item in new UnityEngine.Object[]{material,mesh,target,pixels}) UnityEngine.Object.DestroyImmediate(item);
        }
    }
    private static Color32[] Capture(GameObject notes,NoiseZoneImageEffect effect,Camera camera,NoiseZoneController controller,string path)
    {
        var target=RenderTexture.GetTemporary(512,512,0);
        var source=RenderTexture.GetTemporary(512,512,0);
        var readback=new Texture2D(512,512,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;
        try {
            RenderTexture.active=source; GL.Clear(true,true,new Color(.12f,.12f,.12f,1));
            GL.PushMatrix(); GL.LoadProjectionMatrix(Matrix4x4.Ortho(-5.2f,5.2f,-5.2f,5.2f,-10,10)); GL.modelview=Matrix4x4.identity;
            notes.GetComponentInChildren<MeshRenderer>().sharedMaterial.SetPass(0);
            Graphics.DrawMeshNow(notes.GetComponentInChildren<MeshFilter>().sharedMesh,Matrix4x4.identity); GL.PopMatrix();
            effect.Draw(source,target,camera,controller.FieldMatrix); RenderTexture.active=target;
            readback.ReadPixels(new Rect(0,0,512,512),0,0); readback.Apply();
            if(path!=null) File.WriteAllBytes(path,readback.EncodeToPNG());
            return readback.GetPixels32();
        }
        finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target); RenderTexture.ReleaseTemporary(source); UnityEngine.Object.DestroyImmediate(readback); }
    }
}
#endif
