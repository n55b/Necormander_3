// 저해상도 렌더 텍스처를 화면에 확대할 때 쓰는 "sharp bilinear" 필터.
// 텍셀 내부는 가장 가까운 텍셀 중심을 그대로 샘플(=선명), 텍셀 경계에서만 화면 1픽셀 폭으로 섞는다.
// → 2.4배 같은 비정수 배율에서도 픽셀 두께가 들쭉날쭉하지 않고, 서브픽셀 이동도 부드럽게 보인다.
// PixelArtUpscaleRig 가 런타임에 머티리얼을 만들어 쓴다.
Shader "Hidden/PixelArt/SharpUpscale"
{
    Properties
    {
        _MainTex ("Low-res RT", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TexSize; // (w, h, 1/w, 1/h)
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 px   = i.uv * _TexSize.xy;
                float2 seam = floor(px + 0.5);
                float2 dpx  = max(fwidth(px), 1e-5);
                px = seam + clamp((px - seam) / dpx, -0.5, 0.5);
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, px * _TexSize.zw).rgb;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
