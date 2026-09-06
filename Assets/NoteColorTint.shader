// Recolors note sprites while preserving their brightness detail and dark outlines.
Shader "Sprites/NoteColorTint"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Vertex Tint", Color) = (1,1,1,1)
        _NoteColor ("Note Color Override (a=strength)", Color) = (1,1,1,0)
        _Brightness ("Brightness (for break shine)", Range(0,2)) = 1.0
        _TintCoverage ("Tint coverage (0=keep material sat, 1=flat target sat)", Range(0,1)) = 0.0
        _SrcHue ("Break detail mode (-1=off, 0=on)", Range(-1,0)) = -1.0
        _NoteAlpha ("Note Opacity", Range(0,1)) = 1.0
        _Grayscale ("Grayscale", Range(0,1)) = 0
        _DarkDetail ("Detail kept at the dark end", Range(0,1)) = 0.35
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha   // premultiplied-alpha, same as Sprites/Default

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };
            struct v2f {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _NoteColor;
            float _Brightness;
            float _TintCoverage;
            float _SrcHue;
            float _NoteAlpha;
            float _Grayscale;
            float _DarkDetail;

            // ---- HSV helpers ----
            float3 rgb2hsv(float3 c) {
                float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                return float3(abs(q.z + (q.w - q.y) / (6.0*d + 1e-9)),
                              d / (q.x + 1e-9),
                              q.x);
            }
            float3 hsv2rgb(float3 c) {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
            }

            v2f vert(appdata_t v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                o.color  = v.color * _Color;
#ifdef PIXELSNAP_ON
                o.vertex = UnityPixelSnap(o.vertex);
#endif
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                float a = c.a;

                // Guard: transparent/near-transparent pixels (alpha < 0.01).
                // Unity sprite textures are straight-alpha (not premultiplied). The RGB
                // channel of a fully transparent pixel still contains the source image's
                // color (often white), NOT zero. If we proceed, HSV math runs on those
                // garbage RGB values and outputs a faint white haze over the whole sprite
                // quad — the "full-screen overlay" bug. Zero them out here.
                if (a < 0.01) {
                    c.rgb = 0;
                    return c;
                }

                float strength = _NoteColor.a;
                if (strength < 0.001 && _Grayscale < 0.001) {
                    // Pass-through: premultiply + apply opacity
                    c.rgb *= a * _NoteAlpha * _Brightness;
                    c.a   *= _NoteAlpha;
                    return c;
                }

                // Straight-alpha input: use c.rgb directly (do NOT divide by a).
                // We will premultiply manually at the end before output.
                float3 rgb = c.rgb;

                float3 origHSV = rgb2hsv(rgb);
                float3 tgtHSV  = rgb2hsv(_NoteColor.rgb);

                // Keep the source texture's value and saturation detail. Only its hue
                // moves toward the requested note color; outlines and highlights remain intact.
                float satGate   = smoothstep(0.05, 0.25, origHSV.y);
                float darkGuard = smoothstep(0.04, 0.16, origHSV.z);
                float isGray    = 1.0 - smoothstep(0.05, 0.20, tgtHSV.y); // 1 = achromatic target

                // Break textures encode much of their pattern in red/yellow hue
                // differences. Convert those differences into value contrast so the
                // pattern remains visible without leaking the original colors.
                float preserveBreakDetail = step(0.0, _SrcHue);
                float sourceLuma = dot(rgb, float3(0.299, 0.587, 0.114));
                float detailValue = origHSV.z * lerp(0.55, 1.05, saturate(sourceLuma));
                float tintedValue = lerp(origHSV.z, saturate(detailValue), preserveBreakDetail);
                // Only the hue used to survive, so FF2200 and FF0000 came out as the
                // same red: the requested saturation and value were discarded and the
                // reachable palette was one ring of pure hues.
                //
                // Scaling the texture's own saturation and value by the target's keeps
                // every relative difference the texture carries, which is what makes
                // the detail readable. Lerping toward the target instead pulls those
                // differences to one flat number, and that is what wipes the texture
                // out.
                //
                // Neither scale is given a floor. A floor reads as safety but it makes
                // every request past it land on one shade, which is the collapse this
                // is here to undo. Neither scale exceeds one either, so a fully
                // saturated bright target lands exactly where it always did.
                float satScale = tgtHSV.y;
                float valueScale = tgtHSV.z;
                // Scaling alone cannot survive a dark request: multiplying by a value
                // near zero takes the texture's differences down with it, so 220000
                // and 000000 arrived as flat blocks. The darker the request, the more
                // of the texture's own contrast is added back on top of the scaled
                // value instead - shading a chart can still see, around a mean that
                // is still as dark as it asked for. Detail is added, never a floor
                // under the level, so every step of the dark ramp stays distinct.
                float detailGain = _DarkDetail * (1.0 - tgtHSV.z);
                float shapedValue = saturate(
                    tintedValue * valueScale + detailGain * (tintedValue - 0.5));
                float tintedSat = saturate(
                    lerp(origHSV.y * satScale, tgtHSV.y, _TintCoverage));
                float3 hueTinted = hsv2rgb(
                    float3(tgtHSV.x, tintedSat, shapedValue));
                float3 chromaOut = lerp(rgb, hueTinted, darkGuard * strength * satGate);

                // An achromatic request has no hue to move toward, so it drives the
                // grey axis instead: the texture is desaturated to its own brightness
                // detail and that is scaled to the requested level. FFFFFF therefore
                // reads as a white Note, 808080 as a plain greyscale one and 000000 as
                // black, all of them keeping their shading.
                //
                // Scaling the colour itself, as this did before, could only ever make
                // the Note a brighter version of the colour it already was: asking for
                // white turned a red Note into an overexposed red one.
                // The same reason as above: a request for black used to multiply the
                // texture away to nothing, and the note became a silhouette.
                float3 achromatic = clamp(
                    sourceLuma.xxx * (tgtHSV.z * 2.0) +
                    detailGain * (sourceLuma.xxx - 0.5),
                    0.0, 1.0);
                float3 grayOut = lerp(rgb, achromatic, darkGuard * strength);

                rgb = lerp(chromaOut, grayOut, isGray);
                float luma = dot(rgb, float3(0.299, 0.587, 0.114));
                rgb = lerp(rgb, luma.xxx, saturate(_Grayscale));

                // Premultiply + opacity before Blend One OneMinusSrcAlpha output
                c.rgb = rgb * a * _NoteAlpha * _Brightness;
                c.a   *= _NoteAlpha;
                return c;
            }
            ENDCG
        }
    }
}
