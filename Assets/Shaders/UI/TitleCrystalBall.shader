// 타이틀 화면 '수정구슬' 효과 (UI 용)
//  ※ 값은 TitleCrystalBallSettings 에셋에서 조절한다. 이 셰이더/머티리얼은 직접 건드릴 필요 없음.
//  - 화면 전체를 덮는 RawImage 에 쓴다. 그림 한 장이 화면 전체에 깔린다.
//  - 어둠 원 2겹: 각 원의 '바깥'을 반투명하게 어둡게 덮는다 → 두 원이 겹친 안쪽이 가장 밝다.
//  - 구슬(가장 큰 원) 바깥은 더 어두워지다가 멀어질수록 배경색으로 사라진다.
//  - 그림 / 원1 / 원2 가 마우스에 따라 서로 다른 양만큼 움직여(패럴랙스) 둘러보는 느낌을 준다.
//  - 모든 계산을 '아트 픽셀 격자' 기준으로 해서 도트가 깨끗하게 유지된다.
Shader "UI/TitleCrystalBall"
{
    Properties
    {
        [PerRendererData] _MainTex ("Title Texture", 2D) = "black" {}
        _SpriteRect ("Sprite UV Rect (xMin, yMin, w, h)", Vector) = (0, 0, 1, 1)
        _ArtPx ("Art Size (px)", Vector) = (480, 270, 0, 0)
        _Cover ("Screen->Art UV Scale", Vector) = (1, 1, 0, 0)
        _Orb ("Orb (center x, y px, radius px)", Vector) = (367, 135, 165, 0)
        _Pan ("Image Pan (px)", Vector) = (0, 0, 0, 0)
        _ImageTint ("Image Tint x Brightness", Color) = (1, 1, 1, 1)
        _BgColor ("Background Color", Color) = (0, 0, 0, 1)
        _Outside ("Outside (brightness, fade px, curve)", Vector) = (0.35, 135, 1.5, 0)

        _Layer1 ("Layer1 (center x, y px, radius px, alpha)", Vector) = (342, 137, 140, 0.3)
        _Layer1Color ("Layer1 Color", Color) = (0, 0, 0, 1)
        _Layer2 ("Layer2 (center x, y px, radius px, alpha)", Vector) = (317, 143, 115, 0.3)
        _Layer2Color ("Layer2 Color", Color) = (0, 0, 0, 1)

        // UI 마스크 호환용
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            sampler2D _MainTex;
            float4 _SpriteRect, _ArtPx, _Cover, _Orb, _Pan;
            float4 _ImageTint, _BgColor, _Outside;
            float4 _Layer1, _Layer2, _Layer1Color, _Layer2Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;        // 화면 전체를 덮는 RawImage 라서 uv(0~1) = 화면 위치
                o.color = v.color;
                return o;
            }

            // 그림 밖을 비추면 가장자리를 거울처럼 이어 붙인다 (빈 검정 띠 방지)
            float MirrorPx(float p, float size)
            {
                float period = size * 2;
                p = p - period * floor(p / period);
                return p < size ? p : period - p;
            }

            float3 SampleArt(float2 artPx)
            {
                artPx = float2(MirrorPx(artPx.x, _ArtPx.x), MirrorPx(artPx.y, _ArtPx.y));
                artPx = floor(min(artPx, _ArtPx.xy - 0.5)) + 0.5;
                float2 uv = artPx / _ArtPx.xy;
                return tex2D(_MainTex, _SpriteRect.xy + uv * _SpriteRect.zw).rgb;
            }

            // 원 안이면 1 (도트 단위 판정)
            float InCircle(float2 P, float4 c) { return length(P - c.xy) <= c.z ? 1 : 0; }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 art = ((i.uv - 0.5) * _Cover.xy + 0.5) * _ArtPx.xy;
                float2 P = floor(art) + 0.5;   // 아트 픽셀 중심

                float3 col = SampleArt(P + _Pan.xy) * _ImageTint.rgb;

                // 어둠 원: 원 '바깥'을 반투명하게 덮는다 (원 안은 그대로)
                col = lerp(col, _Layer1Color.rgb, (1 - InCircle(P, _Layer1)) * _Layer1.w);
                col = lerp(col, _Layer2Color.rgb, (1 - InCircle(P, _Layer2)) * _Layer2.w);

                // 구슬 바깥: 더 어둡게, 멀어질수록 배경색으로
                float distOut = length(P - _Orb.xy) - _Orb.z;
                if (distOut > 0)
                {
                    float t = pow(saturate(distOut / max(_Outside.y, 1e-3)), max(_Outside.z, 1e-3));
                    col = lerp(col * _Outside.x, _BgColor.rgb, t);
                }

                return fixed4(col, i.color.a);
            }
            ENDCG
        }
    }
}
