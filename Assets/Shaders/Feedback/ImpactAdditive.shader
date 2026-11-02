Shader "Necromancer/Impact Additive"
{
    Properties { [HDR] _Color ("Emission", Color) = (2,1.5,.8,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        Cull Off ZWrite Off
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            struct Attributes { float3 position:POSITION; half4 color:COLOR; };
            struct Varyings { float4 position:SV_POSITION; half4 color:COLOR; };
            Varyings Vert(Attributes v) { Varyings o; o.position=TransformObjectToHClip(v.position); o.color=v.color*_Color; return o; }
            half4 Frag(Varyings i):SV_Target { return i.color; }
            ENDHLSL
        }
    }
}
