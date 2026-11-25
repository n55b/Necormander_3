Shader "Necromancer/Tree Canopy Lit"
{
    Properties {
        [HideInInspector] _OcclusionFogProgress("Mist",Float) = 0
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        [PerRendererData] _SpriteRect("Sprite UV bounds", Vector) = (0,0,1,1)
        [PerRendererData] _TreePPU("Art pixels per unit", Float) = 32
        [PerRendererData] _WindPhase("Wind phase", Float) = 0
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"
            #include "OcclusionMist.hlsl"
            TEXTURE2D(_MainTex);
            float4 _MainTex_TexelSize, _SpriteRect;
            #include "CanopyWind.hlsl"
            float _WindPhase, _TreePPU, _WorldDepthStrength;
            float4 _GroundKey;
            struct A { float3 pos:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float2 light:TEXCOORD1; float2 world:TEXCOORD2; };
            V Vert(A a) {
                V o; UNITY_SETUP_INSTANCE_ID(a); SetUpSpriteInstanceProperties();
                float pad = sign(a.uv.x - (_SpriteRect.x + _SpriteRect.z)*.5) * (_CanopyWindPixels + ceil(3*_WorldDepthStrength));
                a.pos.x += pad / max(1,_TreePPU);
                a.uv.x += pad * _MainTex_TexelSize.x;
                float3 position = UnityFlipSprite(a.pos, unity_SpriteProps.xy);
                o.world = TransformObjectToWorld(position).xy;
                o.pos = TransformObjectToHClip(position);
                o.light = ComputeScreenPos(o.pos / o.pos.w).xy;
                o.uv = a.uv; o.color = a.color * unity_SpriteColor;
                return o;
            }
            half4 Frag(V i):SV_Target {
                float h = saturate((i.uv.y - _SpriteRect.y) / max(.00001,_SpriteRect.w - _SpriteRect.y));
                float crown = smoothstep(.28,.94,h);
                float lateral = clamp((i.world.x-_WorldSpaceCameraPos.x)/max(1,unity_OrthoParams.x),-1,1);
                float dx = CanopyOffsetPixels(h,_WindPhase) + round(3*lateral*crown*_WorldDepthStrength);
                float2 uv = i.uv - float2(dx * _MainTex_TexelSize.x, 0);
                clip(min(uv.x-_SpriteRect.x, _SpriteRect.z-uv.x));
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, uv) * i.color;
                float across = (uv.x-_SpriteRect.x)/max(.00001,_SpriteRect.z-_SpriteRect.x)*2-1;
                float facing = saturate(.55 + .45*across*-_GroundKey.x);
                float crownLight = lerp(.77,1.17,crown*(.70+.30*facing));
                c.rgb *= lerp(1,crownLight,_WorldDepthStrength);
                SurfaceData2D surface; InputData2D input;
                InitializeSurfaceData(c.rgb, c.a, half4(1,1,1,1), surface);
                InitializeInputData(uv, i.light, input);
                half4 lit = CombinedShapeLightShared(surface, input);
                // Global fill on the ground also reaches the canopy outside local 2D light volumes.
                half3 indirect = c.rgb * half3(.63,.72,.79) * saturate(_GroundKey.w);
                lit.rgb = max(lit.rgb, indirect);
                return ApplyOcclusionMist(lit,i.world);
            }
            ENDHLSL
        }
    }
}
