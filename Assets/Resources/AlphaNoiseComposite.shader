// ActiveBlock/BlockCompose/EdgeMask/GlowMask arithmetic adapted from
// https://github.com/Phira-Pro/Phira-Pro/tree/main/prpr/src/core (GPL-3.0).
Shader "Hidden/AlphaNoiseComposite"
{
    Properties { _MainTex("Scene",2D)="white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex,_DisplaceTex,_SparkTex,_Masks,_Styles;
        float4 _MainTex_TexelSize,_OutputSize;
        float _ZoneTime,_FirstRing,_GlowWeight;
        struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
        v2f vert(appdata_img v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord; return o; }
        float2 mirrorUV(float2 uv) { return 1-abs(frac(uv*.5)*2-1); }
        float noise(float2 uv) { return tex2D(_DisplaceTex,mirrorUV(uv)).r; }
        float2 displaced(float2 uv,float speed)
        {
            const float2 d=float2(.70703125,.70703125);
            float t=_ZoneTime/20*speed;
            float a=noise(uv+d*t)-.5;
            float b=noise(uv+float2(-d.y,d.x)*t)-.5;
            return d*a+float2(-d.y,d.x)*b;
        }
        float4 compose(v2f i):SV_Target
        {
            float2 warped=i.uv+displaced(i.uv*float2(2.13,1.02),2.59)*.1;
            return tex2D(_MainTex,warped).rrrr;
        }
        float4 seed(v2f i):SV_Target
        {
            float mask=tex2D(_MainTex,i.uv).r;
            return float4(mask,0,0,mask);
        }
        float4 expand(v2f i):SV_Target
        {
            float4 previous=tex2D(_MainTex,i.uv);
            float maximum=previous.r;
            [unroll] for(int y=-1;y<=1;y++)
            [unroll] for(int x=-1;x<=1;x++)
                maximum=max(maximum,tex2D(_MainTex,i.uv+float2(x,y)*_MainTex_TexelSize.xy).r);
            float delta=maximum-previous.r;
            return float4(maximum,max(previous.g,delta*_FirstRing),previous.b+delta*_GlowWeight,previous.a);
        }
        float3 rgb2hsv(float3 c)
        {
            float4 K=float4(0,-1.0/3,2.0/3,-1);
            float4 p=lerp(float4(c.bg,K.wz),float4(c.gb,K.xy),step(c.b,c.g));
            float4 q=lerp(float4(p.xyw,c.r),float4(c.r,p.yzx),step(p.x,c.r));
            float d=q.x-min(q.w,q.y),e=1e-10;
            return float3(abs(q.z+(q.w-q.y)/(6*d+e)),d/(q.x+e),q.x);
        }
        float3 hsv2rgb(float3 c)
        {
            float3 p=abs(frac(c.xxx+float3(1,2.0/3,1.0/3))*6-3);
            return c.z*lerp(float3(1,1,1),saturate(p-1),c.y);
        }
        float3 overlay(float3 a,float3 b) { return lerp(2*a*b,1-2*(1-a)*(1-b),step(.5,a)); }
        float4 active(v2f i):SV_Target
        {
            float4 scene=tex2D(_MainTex,i.uv);
            float4 masks=tex2D(_Masks,i.uv);
            // Native Compose and Edge use point-sampled 1/8 and 1/4 camera
            // pixels respectively; only Glow interpolates at full resolution.
            float2 baseSize=max(1,floor(_OutputSize.xy/8));
            float2 effectSize=baseSize*2;
            float fill=tex2D(_Masks,(floor(i.uv*baseSize)+.5)/baseSize).a;
            float edge=tex2D(_Masks,(floor(i.uv*effectSize)+.5)/effectSize).g;
            float glow=masks.b;
            if(fill+edge+glow<.0001) return scene;
            float2 styleUV=i.uv+displaced(i.uv*float2(2.13,1.02),2.59)*.1;
            float4 style=tex2D(_Styles,styleUV);
            float2 d=float2(.70703125,.70703125);
            float t=_ZoneTime/20*1.5;
            float2 ua=i.uv*float2(.8,.3)+d*t;
            float2 ub=i.uv*float2(.8,.3)+float2(-d.y,d.x)*t;
            // Native scene/displacement pixel scale: six full-resolution pixels.
            ua=(floor(ua*_OutputSize.xy/6)+.5)*6/_OutputSize.xy;
            ub=(floor(ub*_OutputSize.xy/6)+.5)*6/_OutputSize.xy;
            float a=noise(ua),b=noise(ub),average=(a+b)*.5;
            float2 offset=d*(a-.5)+float2(-d.y,d.x)*(b-.5);
            float2 sceneUV=(floor(i.uv*_OutputSize.xy/6)+.5)*6/_OutputSize.xy+offset*.15;
            float2 sceneSize=max(1,floor(_OutputSize.xy/3));
            sceneUV=(floor(saturate(sceneUV)*sceneSize)+.5)/sceneSize;
            float3 background=tex2D(_MainTex,sceneUV).rgb;
            float spark=tex2D(_SparkTex,frac(i.uv*float2(3,1.2)+offset*2.39)).r;
            // Native SparkTint is red with a small animated hue component.
            float3 sparkColor=spark*lerp(style.rgb,1,.28490567)*average*5.69;
            float3 hsv=rgb2hsv(background)+sparkColor*.2;
            float3 mixed=saturate(overlay(hsv2rgb(hsv),sparkColor));
            float3 tint=lerp(.23549296,.7132075,style.rgb)-average*.411;
            float3 region=lerp(tint,mixed,.667)*fill;
            float effectiveGlow=(1-fill-edge)*glow;
            region+=lerp(style.rgb,1,.33018857)*edge*.8;
            region+=lerp(style.rgb,1,.17924517)*effectiveGlow*.8;
            float alpha=fill*.667+edge*.8+effectiveGlow*.8;
            return float4(region*style.a+scene.rgb*(1-alpha*style.a),scene.a);
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment compose
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment seed
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment expand
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment active
            #pragma target 3.0
        ENDCG }
    }
}
