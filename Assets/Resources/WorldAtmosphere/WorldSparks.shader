Shader "Necromancer/World Sparks"
{
    Properties { _Palette("Palette",2D)="white"{} }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass {
            Tags { "LightMode"="Universal2D" }
            Cull Off ZWrite Off Blend One OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma multi_compile_local _ FIELD_SIMULATED
            #pragma target 4.5 FIELD_SIMULATED
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Palette); float _SparkTime, _AirVisibility, _WorldDepthStrength;
            #if defined(FIELD_SIMULATED)
            struct Particle { float2 position, velocity; float birth, expires, solid, updated; };
            StructuredBuffer<Particle> _Particles;
            #endif
            struct A { float3 start:POSITION; float4 corner:TEXCOORD0; float4 velocity:TEXCOORD1; float4 contact:TEXCOORD2; };
            struct V { float4 pos:SV_POSITION; float2 local:TEXCOORD0; float4 swatch:TEXCOORD1; float2 axis:TEXCOORD2; float4 air:TEXCOORD3; };
            V Vert(A a
                #if defined(FIELD_SIMULATED)
                , uint vertex : SV_VertexID
                #endif
                ) {
                V o; float t=_SparkTime-a.corner.z, life=a.corner.w;
                float2 p=a.start.xy+a.velocity.xy*min(t,a.contact.x)+a.velocity.zw*max(0,t-a.contact.x);
                #if defined(FIELD_SIMULATED)
                p=_Particles[vertex/4].position;
                #endif
                float shape = a.start.z;
                o.air = 0;
                if (shape > .5)
                {
                    float seed = frac(dot(a.start.xy, float2(.75488,.56984))) * 6.283;
                    float phase = seed + t * (shape == 2 ? 3.0 : 1.85);
                    float envelope = smoothstep(0,.24,t) * smoothstep(0,.55,life-t) * _AirVisibility;
                    // A leaf rolls around its path; embers curl and the smaller spores drift with it.
                    p += float2(sin(phase)-sin(seed), cos(phase*.8)-cos(seed*.8)) *
                         (shape == 2 ? float2(.19,.09) : float2(.09,.055));
                    // Rising cinders start at their lamp. Falling leaves settle onto the ground plane.
                    float age = saturate(t/max(.001,life));
                    float height = shape == 4 ? age*.065 : (shape == 2 ? (1-age)*.115 : .018+.048*frac(seed));
                    p += (p-_WorldSpaceCameraPos.xy) * (height*_WorldDepthStrength);
                    o.air = float4(shape,sin(phase),cos(phase),envelope);
                }
                float radius=abs(a.contact.w)+1;
                float2 flight=t<=a.contact.x ? a.velocity.xy : a.velocity.zw;
                o.axis=flight/max(.001,length(flight));
                o.pos=TransformWorldToHClip(float3(p,0));
                float2 halfSize=float2(320,180);
                o.pos.xy=(o.pos.xy/o.pos.w*halfSize+a.corner.xy*(radius+.5))/halfSize*o.pos.w;
                o.local=a.corner.xy*(radius+.5);
                o.swatch=float4((a.contact.y+.5)/16,1-(a.contact.z+.5)/9,saturate(1-t/max(life,.001)),a.contact.w);
                if(t<0 || t>=life || life<=0) o.pos=float4(2,2,0,1);
                return o;
            }
            half4 Frag(V i):SV_Target {
                if (i.air.x > .5)
                {
                    float2 q = round(i.local);
                    float2 a = abs(q);
                    float radius = abs(i.swatch.w);
                    float body, glint, halo;
                    if (i.air.x < 1.5) // luminous spore, with intermittent cross-shaped glints
                    {
                        body = step(a.x+a.y,.5);
                        glint = step(a.x*a.y,.1)*step(a.x+a.y,radius)*smoothstep(.24,.92,i.air.y);
                        halo = saturate(1-dot(q,q)/9)*.20;
                    }
                    else if (i.air.x < 2.5) // two planes of a tumbling leaf and a narrow stem
                    {
                        float2 blade = float2(q.x + i.air.y*q.y, q.y - .4*i.air.z*q.x);
                        float width = .75 + 1.05*abs(i.air.z);
                        body = step(abs(blade.x)/radius + abs(blade.y)/width,1.12);
                        glint = body * step(0,blade.y) * (.4+.4*abs(i.air.z));
                        halo = 0;
                    }
                    else // drifting ash and warm cinders
                    {
                        float ember = step(3.5,i.air.x);
                        body = step(a.x+.65*a.y,.85);
                        glint = body*(.35+.24*i.air.z) + ember*step(a.x,.3)*step(abs(q.y+1),.3)*.45;
                        halo = saturate(1-dot(q,q)/6)*lerp(.07,.22,ember);
                    }
                    half3 pigment=SAMPLE_TEXTURE2D(_Palette,sampler_PointClamp,i.swatch.xy).rgb;
                    half opacity = i.air.w * body * (i.air.x == 2 ? .86 : .54);
                    return half4(pigment * (opacity + i.air.w*(glint*.80+halo)),opacity);
                }
                if (i.swatch.w == 0)
                {
                    clip(.5 - max(abs(i.local.x),abs(i.local.y)));
                    half3 ink = SAMPLE_TEXTURE2D(_Palette,sampler_PointClamp,i.swatch.xy).rgb;
                    return half4(ink*i.swatch.z,i.swatch.z);
                }
                float d=dot(i.local,i.local);
                float core=step(d,(i.swatch.w-.5)*(i.swatch.w-.5));
                float halo=saturate(1-d/((i.swatch.w+1)*(i.swatch.w+1)))*.24;
                if (i.swatch.w < 0) {
                    float along=abs(dot(i.local,i.axis));
                    float across=abs(dot(i.local,float2(-i.axis.y,i.axis.x)));
                    float reach=max(1,-i.swatch.w*i.swatch.z);
                    core=step(along/reach+across/1.4,1);
                    halo=.16*saturate(1-along/(reach+1))*saturate(1-across/2.4);
                }
                half3 color=SAMPLE_TEXTURE2D(_Palette,sampler_PointClamp,i.swatch.xy).rgb;
                return half4(color*(core*1.55+halo)*saturate(i.swatch.z*3),0);
            }
            ENDHLSL
        }
    }
}
