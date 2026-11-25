Shader "Necromancer/Pixel Art/Billboard"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Solid billboards stream Position, Color and Center in this order.
            struct Attributes
            {
                float3 positionWS : POSITION;
                half4 color : COLOR;
                float3 centerWS : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 vertex = TransformWorldToHClip(input.positionWS);
                float4 center = TransformWorldToHClip(input.centerWS);
                float2 screenSize = GetScaledScreenParams().xy;
                float2 centerNdc = center.xy / center.w;
                float2 cornerOffset = (vertex.xy / vertex.w - centerNdc) * screenSize * 0.5;
                float radius = max(abs(cornerOffset.x), abs(cornerOffset.y));
                vertex.xy = (centerNdc + sign(cornerOffset) * (2.0 * radius / screenSize)) * vertex.w;
                output.positionCS = vertex;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }
}
