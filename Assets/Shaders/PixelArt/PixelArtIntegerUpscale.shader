Shader "Hidden/PixelArt/IntegerUpscale"
{
    Properties
    {
        _Palette ("Scene palette", 2D) = "white" {}
        [PerRendererData] _MainTex ("Game Frame", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); TEXTURE2D(_Palette);
            float4 _MainTex_TexelSize;
            float _ScenePaletteFade;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 size = _MainTex_TexelSize.zw;
                uint2 pixel = (uint2)min(floor(saturate(input.uv) * size), size - 1);
                float2 centerUV = (float2(pixel) + .5) / size;
                half3 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, input.uv).rgb;
                if (_ScenePaletteFade <= 0) return half4(scene, 1);
                pixel = (uint2)floor(input.uv * float2(640,360));
                uint2 lo = pixel & 1u, hi = (pixel >> 1u) & 1u;
                float rank = (4 * ((lo.x ^ lo.y) * 2 + lo.y) + (hi.x ^ hi.y) * 2 + hi.y + .5) / 16;
                half luminance = dot(scene, half3(.2126, .7152, .0722));
                float row = 1 - 7.5 / 9;
                half3 night = SAMPLE_TEXTURE2D(_Palette, sampler_PointClamp, float2(14.5/16, row)).rgb;
                half3 ink = SAMPLE_TEXTURE2D(_Palette, sampler_PointClamp, float2((luminance > .36 ? 11.5 : 13.5)/16, row)).rgb;
                half3 tone = lerp(scene, ink, smoothstep(rank * .70, rank * .70 + .30, saturate(_ScenePaletteFade * 1.8)));
                return half4(lerp(tone, night, smoothstep(rank * .70, rank * .70 + .30, saturate((_ScenePaletteFade - .35) / .65))), 1);
            }
            ENDHLSL
        }
    }
}
