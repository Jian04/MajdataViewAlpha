// ALPHA: Note color tint shader
// Replaces hue of colored pixels while preserving outlines (dark, unsaturated pixels).
// _NoteColor.a == 0  → no override (pass-through)
// _NoteColor.a == 1  → full tint
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

                // Two tinting paths selected by target saturation:
                //
                // CHROMATIC path (tgtS > 0.15): hue replacement.
                //   Colored pixels (satGate): hue → targetH, S and V unchanged.
                //   White highlights (S≈0) and black outlines (V≈0): untouched.
                //
                // ACHROMATIC path (tgtS < 0.05): brightness scaling.
                //   Uses tgtV as a multiplier: V=0→black (×0), V=0.5→no change (×1), V=1→brighten (×2).
                //   Applies via darkGuard so very-dark outline pixels stay dark.
                //   Enables black (#000000), white (#FFFFFF), and gray notes.
                //
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
                // Saturation: keep the source's saturation VARIATION (material/texture
                // detail) instead of flattening every pixel to the target saturation.
                // _TintCoverage lerps source->target: 0 = pure source sat (old hue mode,
                // best material), 1 = flat target sat (washes material). Default 0.6.
                float tintedSat = saturate(lerp(origHSV.y, tgtHSV.y, _TintCoverage));
                float3 hueTinted = hsv2rgb(float3(tgtHSV.x, tintedSat, tintedValue));
                float3 chromaOut = lerp(rgb, hueTinted, darkGuard * strength * satGate);

                // Achromatic path: scale brightness (×2*tgtV, clamped)
                // V=0→×0 (black), V=0.5→×1 (unchanged), V=1→×2 (brighten)
                float3 brightened = clamp(rgb * (tgtHSV.z * 2.0), 0.0, 1.0);
                float3 grayOut    = lerp(rgb, brightened, darkGuard * strength);

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
