Shader "Necromancer/Ground Sprite Projection"
{
    Properties { _MainTex("Sprite atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Blend DstColor OneMinusSrcAlpha, Zero One
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GroundShadowLighting.hlsl"
            TEXTURE2D(_MainTex);
            float4 _MainTex_TexelSize;
            #include "Assets/Shaders/PixelArt/CanopyWind.hlsl"
            struct A { float3 world:POSITION; float2 uv:TEXCOORD0; float4 floor:TEXCOORD1; float4 wind:TEXCOORD2; float4 edges:TEXCOORD3; };
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; half3 shade:TEXCOORD1; float4 wind:TEXCOORD2; float4 edges:TEXCOORD3; };
            half Silhouette(float2 uv,float4 rect)
            {
                return all(uv>=rect.xy) && all(uv<rect.zw) ? SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_PointClamp,uv,0).a : 0;
            }
            V Vert(A a)
            {
                V o;
                float3 light = GroundShadowLighting(a.floor.xy);
                bool contact = a.floor.z < 0;
                // Keep the projection affine through transparent padding below the sole.
                float height = a.world.y - a.floor.y;
                float2 projected = contact ? a.world.xy : float2(a.world.x, a.floor.y) + light.xy * height;
                o.pos = TransformWorldToHClip(float3(projected, a.world.z));
                o.uv = a.uv;
                float penumbra = smoothstep(.10,1.9,height);
                o.shade = half3(light.z * a.floor.w * (contact ? 1 : lerp(1,.66,penumbra)), contact,penumbra);
                o.wind = a.wind;
                o.edges = a.edges;
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                float2 uv = i.uv;
                half coverage;
                if (i.shade.y > .5)
                {
                    float2 p = uv * 2 - 1;
                    float r = dot(p,p);
                    clip(1 - r);
                    coverage = 1-smoothstep(.09,1,r);
                }
                else
                {
                    if (i.wind.w > 0)
                    {
                        float h = saturate((uv.y - i.wind.x) * i.wind.y);
                        uv.x -= CanopyOffsetPixels(h,i.wind.z) * i.wind.w;
                    }
                    coverage = Silhouette(uv,i.edges);
                    if (i.shade.z > .03)
                    {
                        float2 step = _MainTex_TexelSize.xy*(1+i.shade.z);
                        half neighbours = (Silhouette(uv+float2(step.x,0),i.edges)+Silhouette(uv-float2(step.x,0),i.edges)+
                            Silhouette(uv+float2(0,step.y),i.edges)+Silhouette(uv-float2(0,step.y),i.edges))*.25;
                        coverage = lerp(coverage,neighbours,.76*i.shade.z);
                    }
                    clip(coverage - .02);
                }
                half opacity = coverage * i.shade.x;
                return half4(IndirectShadowColor()*opacity,opacity);
            }
            ENDHLSL
        }
    }
}
