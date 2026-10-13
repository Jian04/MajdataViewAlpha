// Display-only coordinate and color effects. No note or judgment data is read.
float _Shatter, _ShatterPhase, _RadialBlur, _Ripple, _RipplePhase, _Pixelate, _LensDistort;
float _Split, _Kaleidoscope, _Bloom, _Invert, _Posterize;
float _KaleidoscopeAngle;

float2 ShardHash(float2 cell)
{
    return frac(sin(float2(dot(cell, float2(127.1, 311.7)),
        dot(cell, float2(269.5, 183.3))) + 17.123) * 43758.5453);
}
float2 GlassVertex(float ray, int ring)
{
    if (ring == 0) return 0;
    ray = ray - floor(ray / 12.0) * 12.0;
    // Keep the original impact fracture layout, only gently disturb its depths.
    float radius = ring == 1 ? .065 : ring == 2 ? .205 : ring == 3 ? .445 : 1.12;
    float2 random = ShardHash(float2(ray, ring));
    float angle = (ray + (ShardHash(float2(ray, 19)).x - .5) * .32 +
        (random.y - .5) * .2) * .523598776;
    return float2(cos(angle), sin(angle)) * radius * (.75 + random.x * .50);
}
float GlassCross(float2 a, float2 b) { return a.x*b.y - a.y*b.x; }
void GlassTriangle(float2 screenPoint, float2 a, float2 b, float2 c, float2 id,
    float burst, inout float bestDepth, inout float2 mapped, inout float2 facetTilt,
    inout float edge, inout float visible,inout float thickness,inout float side,inout float rearRim)
{
    float2 center = (a+b+c) / 3.0;
    float2 random = ShardHash(id + 53);
    float2 radial = normalize(center + float2(.00001,.00002));
    float motion = _Shatter * burst;
    float2 shift = radial * motion * (.014 + length(center)*.07 + random.x*.006);
    float angle = (random.y-.5) * motion * .35;
    float2 local = screenPoint - center - shift;
    float2 rotation=float2(local.x*cos(angle) + local.y*sin(angle),-local.x*sin(angle) + local.y*cos(angle));
    float2 foreshortening=cos((random.yx-.5)*motion*1.15);
    float2 sourcePoint=rotation/foreshortening+center;
    float area = GlassCross(b-a,c-a);
    float u = GlassCross(b-sourcePoint,c-sourcePoint) / area;
    float v = GlassCross(c-sourcePoint,a-sourcePoint) / area;
    float w = 1.0-u-v;
    // Actual projected slab volume: separate front and back faces, with visible
    // side walls. At 980px the slab depth is several tens of pixels before tilt.
    float depthSize=.030+random.x*.018;
    float2 extrusion=(random-.5+float2(.22,-.35))*depthSize*motion*1.7;
    float2 backPoint=sourcePoint-float2(extrusion.x*cos(angle)+extrusion.y*sin(angle),
        -extrusion.x*sin(angle)+extrusion.y*cos(angle))/foreshortening;
    float bu=GlassCross(b-backPoint,c-backPoint)/area;
    float bv=GlassCross(c-backPoint,a-backPoint)/area;
    float3 frontWeights=float3(u,v,w),backWeights=float3(bu,bv,1-bu-bv);
    // Extruded triangular prism, clipped parametrically between front/back.
    float low=0,high=1;
    [unroll] for(int plane=0;plane<3;plane++) {
        float delta=backWeights[plane]-frontWeights[plane];
        if(abs(delta)<.000001) { if(frontWeights[plane]<0) high=-1; }
        else if(delta>0) low=max(low,-frontWeights[plane]/delta);
        else high=min(high,-frontWeights[plane]/delta);
    }
    bool front=min(u,min(v,w))>=0;
    float depth = random.y + length(center) * .03;
    if (low<=high && depth > bestDepth)
    {
        bestDepth = depth; mapped=lerp(sourcePoint,backPoint,saturate(low)); facetTilt = random - .5;
        edge = min(u * abs(area) / max(.00001,length(c-b)),
            min(v * abs(area) / max(.00001,length(c-a)), w * abs(area) / max(.00001,length(b-a))));
        visible=1;
        thickness=depthSize;
        side=front ? 0 : 1;
        float rearDistance=min(bu*abs(area)/max(.00001,length(c-b)),
            min(bv*abs(area)/max(.00001,length(c-a)),(1-bu-bv)*abs(area)/max(.00001,length(b-a))));
        rearRim=1-smoothstep(.0005,.0025,abs(rearDistance));
    }
}
float2 ExtraFilterUV(float2 uv, out float shardVisible, out float shardLight, out float3 shardGlass)
{
    float aspect = abs(_MainTex_TexelSize.y / _MainTex_TexelSize.x);
    shardVisible = 1; shardLight = 0; shardGlass = 0;
    float2 p = uv - .5;
    p.x *= aspect;
    [branch] if (abs(_LensDistort) > .0001)
        p /= max(.35, 1.0 + _LensDistort * .9 * dot(p, p));
    [branch] if (_Ripple > .0001)
    {
        float radius = length(p);
        float wave = radius - frac(_RipplePhase * .65) * 1.05;
        float pulse = exp(-wave * wave * 180.0) * sin(wave * 65.0);
        p += p / max(.0001, radius) * pulse * _Ripple * .025;
    }
    [branch] if (_Kaleidoscope > .0001)
    {
        float angle = atan2(p.y, p.x) - _KaleidoscopeAngle;
        const float sector = 1.047197551;
        float folded = abs(frac((angle + sector * .5) / sector) - .5) * sector;
        float2 kaleido = float2(cos(folded + _KaleidoscopeAngle), sin(folded + _KaleidoscopeAngle)) * length(p);
        p = lerp(p, kaleido, _Kaleidoscope);
    }
    [branch] if (_Shatter > .0001)
    {
        // A brief central strike, fast radial separation and small recoil.
        float phase=max(0,_ShatterPhase);
        float burst=1-exp(-phase*55)+sin(phase*50)*exp(-phase*18)*.12;
        float ray = floor(atan2(p.y,p.x) / .523598776);
        float bestDepth = -1, edge = 1,thickness=.04,side=0,rearRim=0;
        float2 mapped = p, tilt = 0;
        shardVisible = 0;
        [loop] for (int neighbor = -1; neighbor <= 1; neighbor++)
        {
            float sector = ray + neighbor;
            float stableId = sector - floor(sector/12.0)*12.0;
            [unroll] for (int ring = 0; ring < 4; ring++)
            {
                float2 a = GlassVertex(sector,ring);
                float2 b = GlassVertex(sector,ring+1);
                float2 c = GlassVertex(sector+1,ring+1);
                float2 d = GlassVertex(sector+1,ring);
                GlassTriangle(p,a,b,c,float2(stableId,ring*2),burst,bestDepth,mapped,tilt,edge,shardVisible,thickness,side,rearRim);
                if (ring > 0)
                    GlassTriangle(p,a,c,d,float2(stableId,ring*2+1),burst,bestDepth,mapped,tilt,edge,shardVisible,thickness,side,rearRim);
            }
        }
        float bevel = 1.0 - smoothstep(.0005,.003,max(0,edge));
        float facet = saturate(.4 + dot(tilt,float2(-.9,1.3)));
        // A thin brilliant cut edge, image refraction and a reflected highlight;
        // most of the original picture remains visible through the glass.
        float2 refracted = mapped + tilt * _Shatter * burst * .018;
        float2 reflectedUV = float2(.5-refracted.x/aspect,.5+refracted.y) + tilt*.11;
        float3 reflection = tex2D(_MainTex,saturate(reflectedUV)).rgb;
        float2 frontUV=float2(.5+refracted.x/aspect,.5+refracted.y);
        float2 parallax=(tilt+float2(.2,-.3))*thickness*_Shatter*burst*1.3;
        parallax.x/=aspect;
        float3 frontColor=tex2D(_MainTex,saturate(frontUV)).rgb;
        float3 backColor=tex2D(_MainTex,saturate(frontUV+parallax)).rgb*float3(.84,.97,1);
        float3 innerColor=tex2D(_MainTex,saturate(frontUV-parallax*.65)).rgb;
        float3 secondReflection=tex2D(_MainTex,saturate(reflectedUV+parallax*2)).rgb;
        float glint = pow(saturate(1.0-abs(dot(mapped,float2(.65,.76))+tilt.x*.4)*12.0),18.0);
        shardLight = shardVisible * _Shatter * (bevel*(.3+facet*.55)+rearRim*(side>.5?.42:.15));
        shardGlass = shardVisible * _Shatter * ((backColor*.27+innerColor*.14-frontColor*.41)*burst+
            reflection*facet*.10+secondReflection*(.06+bevel*.12)+glint*facet*float3(.3,.4,.48));
        // Side faces have darker absorption and separate reflections, bounded
        // by the front/rear cut highlights rather than a broad neon outline.
        float3 thickSide=backColor*float3(.22,.38,.40)+reflection*.22+secondReflection*.13;
        shardGlass+=shardVisible*side*_Shatter*(thickSide-frontColor*.92);
        // Brief punch at the center, not a slow ambient drift.
        shardGlass += exp(-dot(p,p)*220.0) * exp(-max(0.0,_ShatterPhase)*32.0) * _Shatter * .35;
        p = refracted;
    }
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
float3 ExtraFilterColor(float3 color, float shardVisible, float shardLight, float3 shardGlass)
{
    [branch] if (_Posterize > .0001)
    {
        float levels = max(2.0, floor(lerp(64.0, 3.0, _Posterize)));
        color = floor(saturate(color) * levels + .5) / levels;
    }
    [branch] if (_Invert > .0001) color = lerp(color, 1.0 - color, _Invert);
    return color * shardVisible * (1.0 - _Shatter * .08) + shardGlass + shardLight * float3(.65, .78, .92);
}
