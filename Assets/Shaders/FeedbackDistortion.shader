Shader "Necromancer/Feedback Distortion"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _Rings[8];
            int _RingCount;
            float _Aspect;
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv=input.texcoord;
                float2 offset=0;
                for(int i=0;i<_RingCount;i++)
                {
                    float2 delta=uv-_Rings[i].xy;
                    delta.x*=_Aspect;
                    float distance=length(delta);
                    float band=1-smoothstep(0,.025,abs(distance-_Rings[i].z));
                    float2 direction=delta/max(.0001,distance);
                    direction.x/=_Aspect;
                    offset+=direction*band*_Rings[i].w;
                }
                // 픽셀 경계에 맞춰 이동: 굴절 중 원본 도트를 흐리게 보간하지 않는다.
                offset=round(offset*_BlitTexture_TexelSize.zw)*_BlitTexture_TexelSize.xy;
                return SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_PointClamp,saturate(uv+offset));
            }
            ENDHLSL
        }
    }
}
