Shader "Hidden/Necromancer/Portal Pose Capture"
{
    Properties { _MainTex ("Sprite atlas", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            sampler2D _MainTex;
            fixed4 _Color;
            Varyings vert(float4 position : POSITION, float2 uv : TEXCOORD0)
            {
                Varyings o;
                o.pos = float4(position.xy, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.pos.y = -o.pos.y;
                #endif
                o.uv = uv;
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
