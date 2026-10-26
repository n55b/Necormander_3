Shader "Necromancer/Pixel Feedback Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Toggle] _Dither ("Dither alpha", Float) = 1
        _PixelsPerUnit ("Dither pixels per unit", Float) = 32
        _DitherPixelSize ("Dither dot size (sprite pixels)", Range(1,4)) = 1
        [Toggle] _SolidTint ("Solid silhouette", Float) = 0
        _Emission ("Emission multiplier", Range(1,8)) = 1
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
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
            #include "PixelDither.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Dither, _PixelsPerUnit, _DitherPixelSize, _SolidTint, _Emission;
            CBUFFER_END
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 pixel:TEXCOORD1; half4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                SetUpSpriteInstanceProperties();
                o.pixel = v.positionOS.xy * _PixelsPerUnit / max(1, _DitherPixelSize);
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color * _Color * unity_SpriteColor;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 c = half4(lerp(tex.rgb, half3(1,1,1), _SolidTint) * i.color.rgb * _Emission, tex.a * i.color.a);
                if (_Dither > .5)
                {
                    clip(c.a - PixelDitherThreshold(i.pixel));
                    c.a = 1;
                }
                return c;
            }
            ENDHLSL
        }
    }
}
