Shader "Majdata/StandbyBackground"
{
    Properties
    {
        [PerRendererData] _MainTex ("Foreground", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ThemeColor ("Theme background", Color) = (.137,.149,.173,1)
        _LightTheme ("Light theme", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "UnitySprites.cginc"
            fixed4 _ThemeColor;
            float _LightTheme;
            fixed4 Fragment(v2f input) : SV_Target
            {
                fixed4 source = SampleSpriteTexture(input.texcoord);
                // Only the original dark texture is keyed out; all lines stay at their authored UVs.
                half brightness = max(source.r, max(source.g, source.b));
                #ifdef UNITY_COLORSPACE_GAMMA
                half key = smoothstep(.21, .42, brightness);
                half3 foreground = lerp(source.rgb, .38 + .32 * (1 - source.rgb), _LightTheme);
                #else
                half key = smoothstep(.036, .147, brightness);
                half3 foreground = _LightTheme > .5 ? GammaToLinearSpace(.38 + .32 * (1 - LinearToGammaSpace(source.rgb))) : source.rgb;
                #endif
                return fixed4(lerp(_ThemeColor.rgb, foreground, key) * input.color.rgb, source.a * input.color.a);
            }
            ENDCG
        }
    }
}
