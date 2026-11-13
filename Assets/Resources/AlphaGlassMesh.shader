Shader "Hidden/AlphaGlassMesh"
{
    SubShader
    {
        Cull Off ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float _GlassStrength, _GlassPhase, _GlassAspect;
            struct Input
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 shard : TEXCOORD1;
                float2 edge : TEXCOORD2;
                float2 bevelNormal : TEXCOORD3;
            };
            struct Output
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float2 edge : TEXCOORD2;
                float motion : TEXCOORD3;
                float thickness : TEXCOORD4;
                float4 shard : TEXCOORD5;
                float3 bevelNormal : TEXCOORD6;
            };
            float3 Turn(float3 value, float3 axis, float s, float c)
            {
                return value*c + cross(axis,value)*s + axis*dot(axis,value)*(1-c);
            }
            Output Vert(Input input)
            {
                Output output;
                float2 center = input.shard.xy * float2(_GlassAspect,1);
                float radius = length(center);
                float2 outward = center / max(radius,.0001);
                float seed = input.shard.z;
                float scatter = saturate((_GlassStrength-.3)/.7);
                float sinceImpact = max(0,_GlassPhase - .13 - radius*.26);
                float kick = 1-exp2(-sinceImpact*44);
                float drift = saturate(sinceImpact);
                float motion = scatter * (kick*.72 + drift*.28);
                float impulse = lerp(.075,.032,saturate(input.shard.w*7)) * lerp(.76,1.27,seed);
                float travel = scatter * (impulse*kick + drift*.07) * lerp(.55,2.1,scatter);
                float lift = scatter * (kick*lerp(.035,.135,seed) + drift*.035);
                float2 direction = normalize(outward + (frac(seed * float2(7.1,19.3))-.5)*.48);
                float3 axis = float3(normalize(float2(-direction.y,direction.x) +
                    (frac(seed * float2(23.7,3.17))-.5)*.70),0);
                float s,c;
                sincos(motion * lerp(-.55,.60,frac(seed*11.7)), s,c);
                float3 local = input.position.xyz * float3(_GlassAspect,1,motion);
                local = Turn(local,axis,s,c);
                output.normal = Turn(input.normal,axis,s,c);
                output.bevelNormal = Turn(float3(input.bevelNormal,0),axis,s,c);
                local.xy += center + direction * travel;
                local.z += lift;
                // Project actual front, back and side polygons with shared depth.
                float w = 1 - local.z * .76;
                float2 clip = local.xy * float2(2/_GlassAspect,2);
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                #if UNITY_REVERSED_Z
                    float z = .5*w + local.z;
                #else
                    float z = lerp(UNITY_NEAR_CLIP_VALUE,1,.5)*w - local.z;
                #endif
                output.position = float4(clip,z,w);
                output.uv = input.uv;
                output.edge = input.edge;
                output.motion = motion;
                output.thickness = (.012 + .040*saturate(input.shard.w/.14)) * motion;
                output.shard = input.shard;
                return output;
            }

            float4 Frag(Output input) : SV_Target
            {
                float3 normal = normalize(input.normal);
                float side = input.edge.y;
                float3 incident = normalize(float3((input.uv-.5)*.35,-1));
                float3 facing = normal.z < 0 ? -normal : normal;
                float roughness = sin(dot(input.uv,float2(193,257))+input.shard.z*97)*.5 +
                    sin(dot(input.uv,float2(617,-439)))*.25;
                float edgeWidth = max(.0012, fwidth(input.edge.x)*lerp(1.2,3.2,input.shard.z)) * (1+roughness*.4);
                float bevel = 1-smoothstep(0,edgeWidth,input.edge.x);
                float radius = length((input.uv-.5)*float2(_GlassAspect,1));
                float cracked = smoothstep(radius, radius+.025, _GlassPhase*3.6) * saturate(_GlassStrength*5);
                // Fine fissures remain attached to the moving piece, rather than
                // leaving a second intact image beneath it.
                float seed = input.shard.z;
                float2 slope = float2(sin(seed*51),cos(seed*37))*lerp(.025,.16,seed)*cracked;
                // The cut's own direction controls the optical normal. Opposite
                // fracture walls refract in opposite directions, not parallel bands.
                facing = normalize(facing + float3(slope,0) +
                    input.bevelNormal*bevel*cracked*lerp(.7,2.8,seed));
                float fresnel = .04 + .96*pow(1-saturate(dot(-incident,facing)),5);
                float2 local = input.uv-.5-input.shard.xy;
                float2 branch = float2(cos(seed*32),sin(seed*32));
                float fissureDistance = abs(dot(local,branch) + sin(dot(local,branch.yx)*91+seed*17)*.0008);
                float fissure = (1-smoothstep(.00015,.0008,fissureDistance)) *
                    saturate(1-length(local)/max(.001,input.shard.w)) * step(.55,seed);
                float glint = pow(saturate(dot(reflect(incident,facing),normalize(float3(-.37,.24,.898)))),48);
                float reflection = saturate(cracked*(fresnel + bevel*.12 + fissure*.12) +
                    input.motion*(side*.38 + glint*.38));
                float transmission = 1-input.motion*.18*side - reflection*.07;
                float3 transmitted = refract(incident,facing,1/1.52);
                float thickness = max(input.thickness,(.012+.040*saturate(input.shard.w/.14))*cracked*.65);
                float2 refraction = (transmitted.xy/max(.15,abs(transmitted.z)) -
                    incident.xy/max(.15,abs(incident.z))) * thickness * float2(1/_GlassAspect,1);
                refraction += slope*bevel*thickness*1.8;
                return float4(saturate(input.uv+refraction),reflection,transmission);
            }
            ENDCG
        }
    }
}
