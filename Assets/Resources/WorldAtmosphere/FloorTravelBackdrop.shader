Shader "Necromancer/Floor Travel Backdrop"
{
    Properties
    {
        _TravelTime ("Floor time", Range(0,1)) = 0
        _Coverage ("Backdrop opacity", Range(0,1)) = 0
        _Night ("Night", Color) = (0.10,0.06,0.16,1)
        _Dust ("Distant dust", Color) = (0.22,0.3,0.60,1)
        _Ice ("Starlight", Color) = (0.84,0.92,0.98,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Varyings { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            float _TravelTime, _Coverage;
            half4 _Night, _Dust, _Ice;
            Varyings vert(float4 vertex:POSITION, float2 uv:TEXCOORD0)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(vertex);
                o.uv = uv;
                return o;
            }

            float hash(float2 p)
            {
                float3 h = frac(float3(p.xyx) * float3(.1031,.1030,.0973));
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }
            float noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(cell),hash(cell + float2(1,0)),f.x),
                            lerp(hash(cell + float2(0,1)),hash(cell + 1),f.x),f.y);
            }
            float stars(float2 pixel, float spacing, float time)
            {
                float2 cell = floor(pixel / spacing);
                float seed = hash(cell);
                float2 center = floor(2 + (spacing - 4) * float2(seed, hash(cell + 17.4)));
                float2 d = abs(floor(frac(pixel / spacing) * spacing) - center);
                float twinkle = .7 + .3 * sin(time * 6 + seed * 25);
                return (1 - saturate(max(d.x, d.y))) * seed * seed * twinkle;
            }
            half4 frag(Varyings i):SV_Target
            {
                float t = _TravelTime;
                float2 px = floor(i.uv * float2(640,360)) + .5;
                float mist = noise(px * .013 + float2(-t * .32, t * .12));
                float starlight = stars(px, 19, t) * .6 + stars(px + 7, 33, t) * .4;
                float skyFade = smoothstep(.15, .54, t) * (1 - smoothstep(.78, .98, t));
                half3 sky = _Night.rgb + skyFade * (_Dust.rgb * mist * .20 + _Ice.rgb * starlight * .42);
                return half4(sky * _Coverage, _Coverage);
            }
            ENDCG
        }
    }
}
