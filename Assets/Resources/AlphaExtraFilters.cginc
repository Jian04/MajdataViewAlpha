// Display-only coordinate and color effects. No note or judgment data is read.
float _Shatter, _ShatterPhase, _RadialBlur, _Ripple, _RipplePhase, _Pixelate, _LensDistort;
float _Split, _Kaleidoscope, _Bloom, _Invert, _Posterize;
float _KaleidoscopeAngle;

#if defined(ALPHA_SHATTER)
sampler2D _AlphaGlassMap;
#endif

float2 ExtraFilterUV(float2 uv, out float shardVisible, out float shardLight, out float4 shardOptics)
{
    float aspect = abs(_MainTex_TexelSize.y / _MainTex_TexelSize.x);
    shardVisible = 1; shardLight = 0;
    shardOptics = 0;
    float2 p = uv - .5;
    p.x *= aspect;
    [branch] if (abs(_LensDistort) > .0001)
        p /= max(.35, 1.0 + _LensDistort * .9 * dot(p, p));
    [branch] if (_Ripple > .0001)
    {
        float radius = length(p);
        float extent = length(float2(aspect, 1.0)) * .5;
        float cycle = frac(_RipplePhase * .72);
        float front = lerp(-.10, extent + .10, cycle);
        float band = (radius - front) / .105;
        float envelope = saturate(1 - abs(band));
        float displacement = sin(band * 6.2831853) * envelope * envelope;
        // Gentle in-plane displacement only. The wave has completely left
        // the screen before it wraps, so a held effect has no visible jump.
        p += p / max(.0001, radius) * displacement * _Ripple *
            smoothstep(0,.13,radius) * .017;
    }
    [branch] if (_Kaleidoscope > .0001)
    {
        float angle = atan2(p.y, p.x) - _KaleidoscopeAngle;
        const float sector = 1.047197551;
        float folded = abs(frac((angle + sector * .5) / sector) - .5) * sector;
        float2 kaleido = float2(cos(folded + _KaleidoscopeAngle), sin(folded + _KaleidoscopeAngle)) * length(p);
        p = lerp(p, kaleido, _Kaleidoscope);
    }
    #if defined(ALPHA_SHATTER)
    [branch] if (_Shatter > .0001)
    {
        float4 glass = tex2D(_AlphaGlassMap, float2(p.x/aspect,p.y) + .5);
        if (glass.a > 0)
        {
            float2 surface = float2(p.x/aspect,p.y) + .5;
            float2 bend = glass.xy-surface;
            shardOptics = float4(saturate(1-glass.xy+bend*6),glass.b,length(bend));
            p = (glass.xy - .5) * float2(aspect,1);
            shardVisible = glass.a;
            shardLight = pow(glass.b,3)*.6;
        }
        else
        {
            shardVisible = 0;
        }
        shardLight += exp2(-length(p)*31 - max(0,_ShatterPhase)*57) * _Shatter*.16;
    }
    #endif
    p.x /= aspect;
    uv = p + .5;
    [branch] if (_Split > .0001)
    {
        float band = floor(uv.y * 8.0);
        uv.x += (fmod(band, 2.0) * 2.0 - 1.0) * _Split * .065;
    }
    [branch] if (_Pixelate > .0001)
    {
        float2 block = abs(_MainTex_TexelSize.xy) * (1.0 + _Pixelate * _Pixelate * 19.0);
        uv = (floor(uv / block) + .5) * block;
    }
    return uv;
}
float3 ExtraGlassSample(float2 uv, float3 color, float4 optics)
{
    #if defined(ALPHA_SHATTER)
    [branch] if (optics.z > .0001)
    {
        float3 reflection = tex2D(_MainTex,optics.xy).rgb;
        // Front/back reflections are separated by the pane's optical thickness.
        float2 separation = (optics.xy-.5)*min(.012,optics.w*.18);
        reflection = reflection*.72 + tex2D(_MainTex,saturate(optics.xy+separation)).rgb*.28;
        color = lerp(color,reflection,optics.z);
    }
    #endif
    return color;
}
float3 ExtraFilterSample(float2 uv, float3 color)
{
    [branch] if (_RadialBlur > .0001)
    {
        float3 radial = 0;
        [unroll] for (int i = 0; i < 12; i++)
            radial += tex2D(_MainTex, saturate(.5 + (uv - .5) * (1.0 - _RadialBlur * .2 * i / 11.0))).rgb;
        color = lerp(color, radial / 12.0, _RadialBlur);
    }
    [branch] if (_Bloom > .0001)
    {
        float3 glow = 0;
        [unroll] for (int i = 0; i < 16; i++)
        {
            float angle = (i % 8) * .785398163;
            float radius = i < 8 ? 4.0 : 10.0;
            float2 offset = float2(cos(angle), sin(angle)) * abs(_MainTex_TexelSize.xy) * radius;
            float3 sampleColor = tex2D(_MainTex, saturate(uv + offset)).rgb;
            glow += max(0.0, sampleColor - .55);
        }
        color += glow / 16.0 * _Bloom * 1.4;
    }
    return color;
}
float3 ExtraFilterColor(float3 color, float shardVisible, float shardLight)
{
    [branch] if (_Posterize > .0001)
    {
        float levels = max(2.0, floor(lerp(64.0, 3.0, _Posterize)));
        color = floor(saturate(color) * levels + .5) / levels;
    }
    [branch] if (_Invert > .0001) color = lerp(color, 1.0 - color, _Invert);
    return color * shardVisible + shardLight * float3(.80, .91, 1);
}
