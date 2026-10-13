using System;
using UnityEngine;

// Mask composition and ActiveBlock color arithmetic adapted from
// Phira-Pro/Phira-Pro (GPL-3.0), prpr/src/core/block_{mask,shader}.rs.
// Sensor shapes and warning timing remain ViewAlpha's chart-authored regions.
[RequireComponent(typeof(Camera))]
public sealed class NoiseZoneImageEffect : MonoBehaviour
{
    private NoiseZoneController owner;
    private Camera cameraSource;
    private Matrix4x4 fieldMatrix;
    private Material material;
    private Texture2D sourceMask, sourceStyles;
    private byte[] maskBytes;
    private Color32[] stylePixels;
    private int[] styleQueue, styleDistance;
    private RenderTexture compose, ping, pong;
    private int width, height;
    private static readonly float[] GlowWeights = MakeGlowWeights();
    internal void Bind(NoiseZoneController value) {
        owner=value; enabled=value!=null;
        if(value!=null) {
            var filters=GetComponent<ScreenEffectController>() ?? gameObject.AddComponent<ScreenEffectController>();
            filters.enabled=true;
        }
    }
    internal bool IsConfigured => owner != null;
    private void OnPreRender() {
        if(owner!=null) fieldMatrix=owner.FieldMatrix;
    }
    internal bool HasActive => owner != null && owner.HasActive;
    internal void Compose(RenderTexture source, RenderTexture destination)
    {
        cameraSource ??= GetComponent<Camera>();
        Draw(source, destination, cameraSource, fieldMatrix);
    }
    internal void Draw(RenderTexture source,RenderTexture destination,Camera camera,Matrix4x4 field)
    {
        if(owner==null || !owner.HasActive) { Graphics.Blit(source,destination); return; }
        Ensure(source.width,source.height);
        owner.FillActiveMask(camera,field,width,height,maskBytes,stylePixels);
        // Extend the nearest region's styling into the glow band. Padding does
        // not change the shape mask, input blocking, or adjacent union seams.
        PadStyles();
        sourceMask.LoadRawTextureData(maskBytes); sourceMask.Apply(false);
        sourceStyles.SetPixels32(stylePixels); sourceStyles.Apply(false);
        material.SetTexture("_Styles",sourceStyles);
        material.SetFloat("_ZoneTime",(float)owner.Time);
        material.SetVector("_OutputSize",new Vector4(source.width,source.height,1f/source.width,1f/source.height));
        ping.filterMode=FilterMode.Point; pong.filterMode=FilterMode.Point;
        Graphics.Blit(sourceMask,compose,material,0);
        Graphics.Blit(compose,ping,material,1);
        for(int i=0;i<GlowWeights.Length;i++)
        {
            if(GlowWeights[i]<.01f) break;
            material.SetFloat("_FirstRing",i==0?1:0);
            material.SetFloat("_GlowWeight",GlowWeights[i]);
            Graphics.Blit(ping,pong,material,2);
            (ping,pong)=(pong,ping);
        }
        // Native Edge/Glow are linear at full resolution; composition/dilation are point sampled.
        ping.filterMode=FilterMode.Bilinear;
        material.SetTexture("_Masks",ping);
        Graphics.Blit(source,destination,material,3);
    }
    private void Ensure(int outputWidth,int outputHeight)
    {
        if(material==null)
        {
            material=new Material(Resources.Load<Shader>("AlphaNoiseComposite")) { hideFlags=HideFlags.HideAndDontSave };
            var noise=Resources.Load<Texture2D>("NoiseZone/BlockNoise1");
            var spark=Resources.Load<Texture2D>("NoiseZone/PointNoise");
            material.SetTexture("_DisplaceTex",noise); material.SetTexture("_SparkTex",spark);
        }
        int bw=Math.Max(1,outputWidth/8), bh=Math.Max(1,outputHeight/8);
        if(bw==width && bh==height && sourceMask!=null) return;
        ReleaseMasks(); width=bw; height=bh;
        maskBytes=new byte[width*height];
        stylePixels=new Color32[width*height]; styleQueue=new int[width*height]; styleDistance=new int[width*height];
        sourceMask=new Texture2D(width,height,TextureFormat.R8,false,true) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
        sourceStyles=new Texture2D(width,height,TextureFormat.RGBA32,false,true) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
        compose=Target(width,height,RenderTextureFormat.R8);
        ping=Target(width*2,height*2,RenderTextureFormat.ARGB32);
        pong=Target(width*2,height*2,RenderTextureFormat.ARGB32);
    }
    private void PadStyles()
    {
        int head=0,tail=0;
        for(int i=0;i<maskBytes.Length;i++) {
            styleDistance[i]=maskBytes[i]==0 ? -1 : 0;
            if(maskBytes[i]!=0) styleQueue[tail++]=i;
        }
        while(head<tail) {
            int source=styleQueue[head++];
            if(styleDistance[source]>=8) continue;
            int x=source%width, y=source/width;
            if(x>0) Spread(source,source-1);
            if(x+1<width) Spread(source,source+1);
            if(y>0) Spread(source,source-width);
            if(y+1<height) Spread(source,source+width);
        }
        void Spread(int source,int target) {
            if(styleDistance[target]>=0) return;
            stylePixels[target]=stylePixels[source]; styleDistance[target]=styleDistance[source]+1;
            styleQueue[tail++]=target;
        }
    }
    private static RenderTexture Target(int w,int h,RenderTextureFormat format)
    {
        var target=new RenderTexture(w,h,0,format,RenderTextureReadWrite.Linear) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave };
        target.Create(); return target;
    }
    private static float[] MakeGlowWeights()
    {
        float total=0; for(int k=1;k<=6;k++) total+=Mathf.Pow(k,2.65f);
        var weights=new float[6]; for(int i=0;i<6;i++) weights[i]=Mathf.Pow(6-i,2.65f)/total;
        return weights;
    }
    private void ReleaseMasks()
    {
        foreach(var target in new[]{compose,ping,pong}) if(target!=null) { target.Release(); Retire(target); }
        compose=null; ping=null; pong=null;
        if(sourceMask!=null) Retire(sourceMask); sourceMask=null;
        if(sourceStyles!=null) Retire(sourceStyles); sourceStyles=null;
    }
    private void OnDestroy() { ReleaseMasks(); if(material!=null) Retire(material); }
    private static void Retire(UnityEngine.Object value)
    {
        if(Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
