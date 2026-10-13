Shader "Majdata/AlphaNoiseZone"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        // DisabledBlock is an additive background layer in Phira-Pro.
        Blend One One
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _ZoneTime;
            sampler2D _DisplaceTex, _SparkTex;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 flag:TEXCOORD1; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float4 screen:TEXCOORD0; float flag:TEXCOORD1; float4 color:COLOR; };
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.screen=ComputeScreenPos(o.vertex); o.flag=v.flag.x; o.color=v.color; return o; }
            float2 mirrorUV(float2 uv) { return 1-abs(frac(uv*.5)*2-1); }
            fixed4 frag(v2f i):SV_Target
            {
                if (i.flag > .5) {
                    // Phira-Pro DisabledBlock palette and two-direction spark
                    // displacement (GPL-3.0, prpr/src/core/block_shader.rs).
                    float2 uv = i.screen.xy / i.screen.w;
                    float2 direction = normalize(float2(1,1));
                    float2 perpendicular = float2(-direction.y, direction.x);
                    float shift = _ZoneTime / 20 * .3;
                    float2 displaceUV = uv * float2(.5,.2);
                    float first = tex2D(_DisplaceTex, mirrorUV(displaceUV + direction * shift)).r;
                    float second = tex2D(_DisplaceTex, mirrorUV(displaceUV + perpendicular * shift)).r;
                    float2 sparkUV = uv * float2(3,1.2) + (direction * (first-.5) + perpendicular * (second-.5)) * 2.29;
                    float spark = tex2D(_SparkTex, sparkUV).r * (first+second) * .5;
                    float3 nativeColor = float3(.497,.13766898,.13766898) * .4 +
                        float3(.31132078,.077830195,.077830195) * spark * 3.5;
                    return float4(nativeColor * i.color.rgb * i.color.a, 0);
                }
                return float4(i.color.rgb * i.color.a, 0);
            }
            ENDCG
        }
    }
}
