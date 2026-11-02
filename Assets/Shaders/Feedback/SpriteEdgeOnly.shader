Shader "Necromancer/Sprite Edge Only"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        [HideInInspector] [PerRendererData] _SpriteUVRect ("Sprite UV bounds", Vector) = (0,0,1,1)
        _Thickness ("윤곽 두께 (원본 도트)", Range(1,3)) = 1
        _AlphaThreshold ("윤곽 알파 기준 (반투명 그림자 제외)", Range(0.01,1)) = 0.5
        _Emission ("발광 배율", Range(1,4)) = 1.5
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float4 _SpriteUVRect;
            CBUFFER_START(UnityPerMaterial)
            float _Thickness, _AlphaThreshold, _Emission;
            CBUFFER_END
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                SetUpSpriteInstanceProperties();
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color * unity_SpriteColor;
                return o;
            }
            half Solid(float2 uv)
            {
                // 아틀라스의 패딩/옆 프레임을 비석으로 인식하지 않도록 원본 사각형 밖은 비운다.
                half inRect = step(_SpriteUVRect.x,uv.x) * step(_SpriteUVRect.y,uv.y)
                            * step(uv.x,_SpriteUVRect.z) * step(uv.y,_SpriteUVRect.w);
                return inRect * step(_AlphaThreshold, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 d = _MainTex_TexelSize.xy * round(_Thickness);
                half inside = Solid(i.uv);
                half surrounded = min(min(Solid(i.uv + float2(d.x,0)), Solid(i.uv - float2(d.x,0))),
                                      min(Solid(i.uv + float2(0,d.y)), Solid(i.uv - float2(0,d.y))));
                // 불투명 영역의 안쪽 테두리만 표시한다. 원본 그림과 그림자를 덧칠하지 않는다.
                return half4(i.color.rgb * _Emission, i.color.a * inside * (1-surrounded));
            }
            ENDHLSL
        }
    }
}
