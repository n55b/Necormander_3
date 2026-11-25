Shader "Necromancer/UI/Pixel Sparks"
{
    Properties {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Palette ("Game palette", 2D) = "white" {}
        _StencilComp ("Stencil comparison", Float) = 8
        _Stencil ("Stencil", Float) = 0
        _StencilOp ("Stencil operation", Float) = 0
        _StencilWriteMask ("Stencil write mask", Float) = 255
        _StencilReadMask ("Stencil read mask", Float) = 255
        _ColorMask ("Color mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use alpha clip", Float) = 0
    }
    SubShader {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _Palette;
            float4 _ClipRect;
            float _UIFxTime;
            struct A { float4 vertex:POSITION; float4 color:COLOR; float4 info:TEXCOORD0; float4 motion:TEXCOORD1; };
            struct V { float4 pos:SV_POSITION; float4 color:COLOR; float2 palette:TEXCOORD0; float2 local:TEXCOORD1; };
            V Vert(A a) {
                V o;
                float age=max(0,_UIFxTime-a.info.z);
                float fraction=saturate(age/max(.001,a.info.w));
                a.vertex.xy+=round(a.motion.xy*age*(2-fraction))*a.motion.z;
                o.pos=UnityObjectToClipPos(a.vertex);
                o.local=a.vertex.xy;
                o.color=a.color;
                o.color.a*=saturate((1-fraction)*3);
                o.palette=a.info.xy;
                return o;
            }
            half4 Frag(V i):SV_Target {
                half4 c=tex2D(_Palette,i.palette)*i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.local,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
