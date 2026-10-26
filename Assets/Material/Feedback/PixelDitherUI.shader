Shader "Necromancer/Pixel Dither UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _PixelSize ("Dot size at 540p", Float) = 1
        _WipeSpread ("Diagonal dither sweep", Range(0,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "PixelDither.hlsl"
            struct Attributes { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _Color, _TextureSampleAdd, _ClipRect;
            float _PixelSize, _WipeSpread;
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.local = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; o.color = v.color * _Color;
                return o;
            }
            fixed4 Frag(Varyings i):SV_Target
            {
                fixed4 c = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                float pixelSize = max(1, floor(_ScreenParams.y / 540.0 + .5)) * max(1, _PixelSize);
                float sweep = dot(saturate(i.vertex.xy / _ScreenParams.xy), float2(.7,.3));
                float coverage = saturate(c.a * (1 + _WipeSpread) - sweep * _WipeSpread);
                clip(coverage - PixelDitherThreshold(i.vertex.xy / pixelSize));
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}
