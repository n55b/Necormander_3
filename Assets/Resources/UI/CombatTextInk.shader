Shader "Necromancer/Combat Text Ink"
{
    Properties
    {
        _MainTex ("Bitmap atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Ink ("Shadow", Color) = (.14,.075,.18,.94)
        _ClipRect ("Clip", Vector) = (-32767,-32767,32767,32767)
        _StencilComp ("Stencil comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil operation", Float) = 0
        _StencilReadMask ("Read mask", Float) = 255
        _StencilWriteMask ("Write mask", Float) = 255
        _ColorMask ("Color mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 local : TEXCOORD1; half4 color : COLOR; };
            sampler2D _MainTex;
            half4 _Color, _Ink;
            float4 _ClipRect;
            Varyings vert(Input v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex.xy;
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 pixel = fwidth(i.uv);
                half face = tex2D(_MainTex, i.uv).a;
                half shadow = tex2D(_MainTex, i.uv + float2(-pixel.x, pixel.y)).a * _Ink.a;
                half alpha = max(face, shadow) * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(i.local, _ClipRect);
                #endif
                return half4(lerp(_Ink.rgb, i.color.rgb, face), alpha);
            }
            ENDCG
        }
    }
}
