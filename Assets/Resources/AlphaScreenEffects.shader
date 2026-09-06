Shader "Hidden/AlphaScreenEffects"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _HistoryTex ("History", 2D) = "black" {}
        _HistoryTex2 ("History 2", 2D) = "black" {}
        _HistoryTex3 ("History 3", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _HistoryTex;
            sampler2D _HistoryTex2;
            sampler2D _HistoryTex3;
            float4 _MainTex_TexelSize;
            float _Blur;
            float _Neon;
            float _Fade;
            float _Trail;
            float _Brightness;
            float _Saturation;
            float _Contrast;
            float _Rainbow;
            float _EffectTime;
            float _Flash;
            float _Vignette;
            float _Zoom;
            float _Glitch;
            float _TVNoise;
            float _Hue;        // Hue rotation in radians
            float4 _TintColor; // Color tint; alpha is blend amount
            float _OffsetX;    // Camera shift as screen-width ratio, including shake
            float _OffsetY;
            float _Rotate;     // Screen rotation in radians

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.uv;
                [branch]
                if (abs(_Zoom) > 0.0001)
                {
                    float zoomScale = max(0.1, 1.0 + _Zoom);
                    uv = (uv - 0.5) / zoomScale + 0.5;
                }

                // Rotate around screen center with aspect-ratio correction
                [branch]
                if (abs(_Rotate) > 0.0001)
                {
                    float aspect = _MainTex_TexelSize.y / _MainTex_TexelSize.x;
                    float2 p = uv - 0.5;
                    p.x *= aspect;
                    float sinR = sin(_Rotate);
                    float cosR = cos(_Rotate);
                    p = float2(p.x * cosR - p.y * sinR, p.x * sinR + p.y * cosR);
                    p.x /= aspect;
                    uv = p + 0.5;
                }

                // Camera shift/shake: dx > 0 moves screen content right
                uv -= float2(_OffsetX, _OffsetY);

                // TV-style horizontal interference: each moving row gets its
                // own horizontal displacement instead of full-screen grain.
                float rowNoise = 0.5;
                float rowGate = 0.0;
                [branch]
                if (_TVNoise > 0.0001)
                {
                    float movingRow = floor(frac(uv.y + _EffectTime * 0.32) * 120.0);
                    rowNoise = frac(sin(dot(float2(movingRow, floor(_EffectTime * 24.0)),
                        float2(12.9898, 78.233))) * 43758.5453);
                    rowGate = step(0.72, rowNoise);
                    uv.x += (rowNoise - 0.5) * rowGate * _TVNoise * 0.045;
                }

                [branch]
                if (_Glitch > 0.0001)
                {
                    float glitchBand = step(0.72,
                        frac(floor(uv.y * 42.0) * 0.173 + _EffectTime * 5.0));
                    uv.x += (sin(uv.y * 91.0 + _EffectTime * 37.0) * 0.012 +
                        sin(_EffectTime * 71.0) * 0.006) * glitchBand * _Glitch;
                }

                float insideFrame =
                    step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);
                uv = saturate(uv);
                fixed4 center = tex2D(_MainTex, uv);
                fixed4 color = center;
                [branch]
                if (_Blur > 0.0001)
                {
                    float2 stepUv = _MainTex_TexelSize.xy * min(_Blur, 8.0);
                    fixed4 blurred = center * 0.227027;
                    blurred += tex2D(_MainTex, uv + float2(stepUv.x, 0)) * 0.1945946;
                    blurred += tex2D(_MainTex, uv - float2(stepUv.x, 0)) * 0.1945946;
                    blurred += tex2D(_MainTex, uv + float2(0, stepUv.y)) * 0.1945946;
                    blurred += tex2D(_MainTex, uv - float2(0, stepUv.y)) * 0.1945946;
                    color = lerp(center, blurred, saturate(_Blur));
                }

                [branch]
                if (_Trail > 0.0001)
                {
                    fixed3 trailColor = tex2D(_HistoryTex, uv).rgb;
                    fixed3 trailColor2 = tex2D(_HistoryTex2, uv).rgb;
                    fixed3 trailColor3 = tex2D(_HistoryTex3, uv).rgb;
                    float currentLight = dot(center.rgb, float3(0.2126, 0.7152, 0.0722));
                    float historyLight = dot(trailColor, float3(0.2126, 0.7152, 0.0722));
                    float motionDifference = length(center.rgb - trailColor);
                    float historyChroma = max(trailColor.r, max(trailColor.g, trailColor.b)) -
                        min(trailColor.r, min(trailColor.g, trailColor.b));
                    float trailMask = saturate(motionDifference * 2.2 - 0.1) *
                        saturate((historyLight - 0.18) * 2.4 + historyChroma * 1.5);
                    color.rgb = lerp(color.rgb, trailColor,
                        trailMask * saturate(_Trail) * 0.98);

                    float trailDiff2 = length(center.rgb - trailColor2);
                    float trailDiff3 = length(center.rgb - trailColor3);
                    float trailLum2 = dot(trailColor2, float3(0.2126, 0.7152, 0.0722));
                    float trailLum3 = dot(trailColor3, float3(0.2126, 0.7152, 0.0722));
                    float trailMask2 = saturate((trailLum2 - currentLight) * 7.0 +
                        trailDiff2 * 1.5 - 0.14);
                    float trailMask3 = saturate((trailLum3 - currentLight) * 6.0 +
                        trailDiff3 * 1.3 - 0.16);
                    color.rgb = lerp(color.rgb, trailColor2,
                        trailMask2 * saturate(_Trail) * 0.78);
                    color.rgb = lerp(color.rgb, trailColor3,
                        trailMask3 * saturate(_Trail) * 0.55);
                }

                // RGB split: separate bright moving edges into colored ghosts.
                [branch]
                if (_Neon > 0.0001)
                {
                    float2 rgbOffset = _MainTex_TexelSize.xy *
                        (6.0 + min(_Neon, 3.0) * 10.0);
                    fixed3 leftSample = tex2D(_MainTex, uv - rgbOffset).rgb;
                    fixed3 rightSample = tex2D(_MainTex, uv + rgbOffset).rgb;
                    fixed3 downSample = tex2D(_MainTex,
                        uv + float2(rgbOffset.x * 0.25, -rgbOffset.y * 0.75)).rgb;

                    float leftLum = dot(leftSample, float3(0.2126, 0.7152, 0.0722));
                    float rightLum = dot(rightSample, float3(0.2126, 0.7152, 0.0722));
                    float downLum = dot(downSample, float3(0.2126, 0.7152, 0.0722));
                    float centerLum = dot(center.rgb, float3(0.2126, 0.7152, 0.0722));
                    float leftChroma = max(leftSample.r, max(leftSample.g, leftSample.b)) -
                        min(leftSample.r, min(leftSample.g, leftSample.b));
                    float rightChroma = max(rightSample.r, max(rightSample.g, rightSample.b)) -
                        min(rightSample.r, min(rightSample.g, rightSample.b));
                    float downChroma = max(downSample.r, max(downSample.g, downSample.b)) -
                        min(downSample.r, min(downSample.g, downSample.b));

                    float leftEdge = saturate(length(leftSample - center.rgb) * 3.5);
                    float rightEdge = saturate(length(rightSample - center.rgb) * 3.5);
                    float downEdge = saturate(length(downSample - center.rgb) * 3.5);
                    float leftMask = leftEdge * saturate((leftLum - centerLum) * 5.0) *
                        saturate((leftLum - 0.2) * 3.0 + leftChroma);
                    float rightMask = rightEdge * saturate((rightLum - centerLum) * 5.0) *
                        saturate((rightLum - 0.2) * 3.0 + rightChroma);
                    float downMask = downEdge * saturate((downLum - centerLum) * 5.0) *
                        saturate((downLum - 0.2) * 3.0 + downChroma);
                    fixed3 rgbGhost =
                        float3(leftLum, 0.0, 0.0) * leftMask +
                        float3(0.0, downLum, 0.0) * downMask +
                        float3(0.0, 0.0, rightLum) * rightMask;

                    float splitAmount = saturate(_Neon);
                    color.rgb = max(color.rgb *
                        (1.0 - max(leftMask, max(rightMask, downMask)) *
                        splitAmount * 0.25), rgbGhost * splitAmount * 1.25);
                }

                [branch]
                if (_Rainbow > 0.0001)
                {
                    float angle = atan2(uv.y - 0.5, uv.x - 0.5) / 6.2831853 + 0.5;
                    float hue = frac(angle + _EffectTime * 0.18);
                    float3 rainbow = saturate(abs(frac(hue +
                        float3(0.0, 0.6667, 0.3333)) * 6.0 - 3.0) - 1.0);
                    rainbow = rainbow * rainbow * (3.0 - 2.0 * rainbow);
                    color.rgb = lerp(color.rgb,
                        color.rgb * (rainbow * 1.35 + 0.25), saturate(_Rainbow));
                }

                float gray = dot(color.rgb, float3(0.2125, 0.7154, 0.0721));
                color.rgb = lerp(color.rgb, gray.xxx, saturate(_Saturation));
                color.rgb *= 1.0 + max(0.0, _Brightness);
                color.rgb = lerp(color.rgb, (color.rgb - 0.5) * (1.0 + max(0.0, _Contrast)) + 0.5,
                    saturate(_Contrast));
                [branch]
                if (_TVNoise > 0.0001)
                {
                    float movingY = frac(input.uv.y + _EffectTime * 0.32);
                    float horizontalLine = pow(saturate(0.5 + 0.5 *
                        sin(movingY * 760.0)), 12.0);
                    float bandFlicker = (rowNoise - 0.5) * rowGate;
                    color.rgb += bandFlicker * _TVNoise * 0.22;
                    color.rgb *= 1.0 - horizontalLine * _TVNoise * 0.38;
                }

                // Circular iris. Vignette only changes the visible radius;
                // Zoom remains the separate rectangular scaling effect.
                [branch]
                if (_Vignette > 0.0001)
                {
                    float aspect = abs(_MainTex_TexelSize.y / _MainTex_TexelSize.x);
                    float2 circleUv = input.uv - 0.5;
                    circleUv.x *= aspect;
                    float irisRadius = lerp(0.72, 0.0, saturate(_Vignette));
                    float irisMask = 1.0 - smoothstep(irisRadius - 0.025, irisRadius,
                        length(circleUv));
                    color.rgb *= irisMask;
                }
                // Rotate the overall hue
                [branch]
                if (abs(_Hue) > 0.0001)
                {
                    const float3 hueAxis = float3(0.57735, 0.57735, 0.57735);
                    float cosH = cos(_Hue);
                    float sinH = sin(_Hue);
                    color.rgb = color.rgb * cosH + cross(hueAxis, color.rgb) * sinH +
                        hueAxis * dot(hueAxis, color.rgb) * (1.0 - cosH);
                }
                color.rgb = _Flash >= 0.0
                    ? lerp(color.rgb, float3(1.0, 1.0, 1.0), saturate(_Flash))
                    : lerp(color.rgb, float3(0.0, 0.0, 0.0), saturate(-_Flash));
                // Color tint (TINT): generalized FLASH effect supporting any color
                color.rgb = lerp(color.rgb, _TintColor.rgb, saturate(_TintColor.a));
                color.rgb *= 1.0 - saturate(_Fade);
                color.rgb *= insideFrame;
                return color;
            }
            ENDCG
        }
    }
}
