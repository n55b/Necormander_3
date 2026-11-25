Shader "Necromancer/UI/Health Liquid"
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
            sampler2D _MainTex, _Palette;
            float4 _ClipRect, _TextureSampleAdd;
            struct A { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 face:TEXCOORD1; };
            struct V { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 face:TEXCOORD1; float2 local:TEXCOORD2; };
            V Vert(A a) {
                V o; o.vertex=UnityObjectToClipPos(a.vertex); o.color=a.color; o.uv=a.uv; o.face=a.face; o.local=a.vertex.xy; return o;
            }
            fixed4 Frag(V i):SV_Target {
                float t = _Time.y * i.face.w;
                float crest = i.face.z - 1.25 + .62*sin(i.face.x*.15-t*2.2);
                float band = smoothstep(crest-.45,crest+.45,i.face.y);
                float3 base = tex2D(_Palette,float2(3.5/16,1-4.5/9)).rgb;
                float3 surface = lerp(base,tex2D(_Palette,float2(1.5/16,1-4.5/9)).rgb,.28);
                float alpha=(tex2D(_MainTex,i.uv)+_TextureSampleAdd).a * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.local,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha-.001);
                #endif
                return fixed4(lerp(base,surface,band),alpha);
            }
            ENDCG
        }
    }
}
