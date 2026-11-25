Shader "Necromancer/UI/Edge Mist"
{
    Properties { _Palette ("Artist palette",2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, Zero One
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Palette);
            float4 _EdgeMist;

            float EdgeHash(float2 p)
            {
                float3 q = frac(p.xyx * .1031);
                q += dot(q,q.yzx+33.33);
                return frac((q.x+q.y)*q.z);
            }
            float Billow(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(EdgeHash(cell),EdgeHash(cell+float2(1,0)),f.x),
                            lerp(EdgeHash(cell+float2(0,1)),EdgeHash(cell+1),f.x),f.y);
            }
            half EdgeVeil(float2 pixel)
            {
                float2 sides = min(pixel, float2(640,360)-1-pixel);
                float edge = min(sides.x, sides.y);
                UNITY_BRANCH if (_EdgeMist.x <= 0 || edge >= _EdgeMist.y) return 0;
                float t = _EdgeMist.z;
                float swirl = Billow(pixel/24 + float2(.17*t, -.11*t));
                float fine = Billow(pixel/6 + float2(-.16*t, swirl*1.6));
                float width = max(1, .55 * _EdgeMist.y * (.28+.52*swirl+.20*fine));
                float density = saturate(1-edge/width) * (.60+.4*swirl);
                float alpha = floor(density*8 + EdgeHash(pixel))*(_EdgeMist.x/8);
                // A broader shadow rolls inward from both adjoining sides at each corner.
                float2 roll = 1 - smoothstep(0, _EdgeMist.y, sides);
                float shadow = (roll.x + roll.y - .65*roll.x*roll.y) * (.28+.10*swirl) * _EdgeMist.x;
                return alpha + (1-alpha)*shadow;
            }
            struct V { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(float4 positionOS:POSITION, float2 uv:TEXCOORD0)
            {
                V o; o.position = TransformObjectToHClip(positionOS.xyz); o.uv = uv; return o;
            }
            half4 Frag(V i):SV_Target
            {
                float alpha = EdgeVeil(floor(i.uv * float2(640,360)));
                half3 tint = SAMPLE_TEXTURE2D(_Palette,sampler_PointClamp,float2(13.5/16,1-7.5/9)).rgb;
                return half4(tint,alpha);
            }
            ENDHLSL
        }
    }
}
