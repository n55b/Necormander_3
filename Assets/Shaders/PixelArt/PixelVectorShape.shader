Shader "Necromancer/Pixel Art/Vector Shape"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite carrier", 2D) = "white" {}
        [PerRendererData] _Shape ("Shape, parameter, stroke mode", Vector) = (0,0,0,0)
        [PerRendererData] _ScreenShape ("Size and direction in frame pixels", Vector) = (0,0,1,0)
        [PerRendererData] _LocalSize ("Size in local units", Vector) = (1,1,0,0)
        [PerRendererData] _PixelMotionOffset ("Actor render offset", Vector) = (0,0,0,0)
        [PerRendererData] _GaugeFill ("Gauge fill color", Color) = (1,0,0,1)
        _IndicatorStroke ("Indicator stroke color", Color) = (0.19,0.15,0.18,1)
        [PerRendererData] _ThreatCue ("Active, charge, imminent pulse", Vector) = (0,0,0,0)
        _StrokeWidths ("Indicator and telegraph widths in pixels", Vector) = (1,1,0,0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "DisableBatching"="True" }
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
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Shape;
                float4 _ScreenShape;
                float4 _LocalSize;
                float4 _PixelMotionOffset;
                half4 _GaugeFill;
                half4 _IndicatorStroke;
                float4 _StrokeWidths;
                float4 _ThreatCue;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 shapePosition : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0)) + _PixelMotionOffset.xyz;
                float4 center = TransformWorldToHClip(centerWS);
                float2 halfScreen = float2(320, 180);
                float2 coordinate = input.positionOS.xy;

                if (_ScreenShape.x > 0)
                {
                    // Only the carrier expands. Shape coordinates stay centered on the authored pivot.
                    coordinate *= 1.0 + 2.0 * (_StrokeWidths.x + 1.0) / _ScreenShape.xy;
                    float2 right = _ScreenShape.zw;
                    float2 up = float2(-right.y, right.x);
                    float2 offset = coordinate * _ScreenShape.xy;
                    float2 oriented = right * offset.x + up * offset.y;
                    oriented.y *= _ProjectionParams.x;
                    center.xy += oriented * center.w / halfScreen;
                    output.positionCS = center;
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(float3(coordinate * _LocalSize.xy, input.positionOS.z));
                }

                output.shapePosition = coordinate;
                output.color = input.color * unity_SpriteColor;
                if ((int)_Shape.x == 7) output.color.rgb *= 1.65;
                return output;
            }

            float ShapeDistance(float2 p)
            {
                float radial = length(p);
                if (_Shape.x == 0) return radial - 0.5; // Circle or ellipse.
                if (_Shape.x == 1) return max(abs(p.x), abs(p.y)) - 0.5;
                if (_Shape.x == 2) return max(radial - 0.5, 0.5 * _Shape.y - radial);
                if (_Shape.x == 3) return max(radial - 0.5, _Shape.y * radial - p.x);
                if (_Shape.x == 4)
                {
                    float band = max(radial - 0.44, 0.33 - radial);
                    band = max(band, -p.x - abs(p.y));
                    float tip = max(p.x - 0.48, max(0.15 - p.x, abs(p.y) - (0.48 - p.x) * 0.81));
                    return min(band, tip);
                }

                if (_Shape.x == 5)
                {
                    // Edge marker with a broad back and a tapered tip on +X.
                    return max(max(-p.x - 0.44, p.x - 0.46), max(abs(p.y) - 0.37, abs(p.y) - (0.46 - p.x) * 1.15));
                }

                if (_Shape.x == 6)
                {
                    // The head gauge is a tapered arch joined to a vertical diamond.
                    float x = abs(p.x);
                    float arch = max(x - 0.49, abs(p.y - (0.17 - 2.45 * x * x)) - max(0.0, 0.102 - 0.17 * x));
                    float diamond = 3.25 * x + abs(p.y) - 0.46;
                    return min(arch, diamond);
                }

                float y = abs(p.y);
                float head = max(max(-0.49 - p.x, p.x + 0.05), y - (p.x + 0.49) * 1.12);
                float shaft = max(abs(p.x) - 0.44, y - 0.16);
                float feathers = max(abs(p.x - 0.32 - y * 0.2) - 0.12, y - 0.32);
                return min(head, min(shaft, feathers));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float distance = ShapeDistance(input.shapePosition);
                if (_Shape.z == 0)
                {
                    clip(-distance);
                    return half4(input.color.rgb * (1.0 + 0.18 * _ThreatCue.y), input.color.a);
                }
                // Signed distances are measured in 640x360 design pixels at any output resolution.
                float logicalScale = GetScaledScreenParams().y / 360.0;
                float unitsPerPixel = max(length(float2(ddx(distance), ddy(distance))) * logicalScale, 0.00001);
                float signedPixels = distance / unitsPerPixel;
                if (_Shape.x == 1)
                {
                    float2 p = input.shapePosition;
                    float2 perPixel = float2(length(float2(ddx(p.x), ddy(p.x))), length(float2(ddx(p.y), ddy(p.y))));
                    float2 sides = (abs(p) - 0.5) / max(perPixel * logicalScale, 0.00001);
                    signedPixels = max(sides.x, sides.y);
                }
                if (_ThreatCue.x > 0 && _Shape.x != 6)
                {
                    clip(-signedPixels);
                    half4 ink = input.color;
                    float charge = _ThreatCue.y;
                    ink.rgb *= 1.0 + 0.18 * charge;
                    float inset = -signedPixels;
                    float core = saturate((_StrokeWidths.y - inset) * logicalScale + 0.5);
                    float halo = exp2(-2.6 * max(0.0, inset - _StrokeWidths.y));
                    float emission = (0.12 + 0.25 * charge + 0.70 * _ThreatCue.z) * halo;
                    ink.rgb = lerp(ink.rgb, half3(1.0, 0.83, 0.70), core * (0.22 + 0.36 * _ThreatCue.z));
                    ink.rgb *= 1.0 + emission;
                    float rimAlpha = core * saturate(2.25 * sqrt(saturate(input.color.a)));
                    if (_Shape.z == 2) ink.a *= halo * (0.07 + 0.10 * charge);
                    ink.a = max(ink.a, rimAlpha);
                    return ink;
                }

                if (_Shape.z == 1)
                {
                    clip(_StrokeWidths.x * unitsPerPixel - distance);
                    if (distance <= 0) return input.color;
                    return half4(_IndicatorStroke.rgb, _IndicatorStroke.a * input.color.a);
                }

                // Attack boundaries are drawn inward so the stroke stays inside the damage area.
                clip(-distance);
                if (_Shape.x == 6 || _Shape.x == 7)
                {
                    if (distance > -_StrokeWidths.x * unitsPerPixel)
                    {
                        if (_Shape.x == 6)
                            return half4(lerp(_IndicatorStroke.rgb, _GaugeFill.rgb * 1.4, 0.18 + 0.25 * _Shape.y + 0.5 * _ThreatCue.z), input.color.a);
                        return half4(_IndicatorStroke.rgb, _IndicatorStroke.a * input.color.a);
                    }
                    if (_Shape.x == 6 && abs(input.shapePosition.x) >= (1.0 - _Shape.y) * 0.5) return half4(_GaugeFill.rgb * (1 + 0.18 * _Shape.y), _GaugeFill.a);
                    return input.color;
                }
                if (_Shape.z == 2) clip(signedPixels + _StrokeWidths.y);
                if (_Shape.z == 3 && signedPixels > -_StrokeWidths.y)
                    return half4(input.color.rgb, saturate(input.color.a * 1.65));
                return input.color;
            }
            ENDHLSL
        }
    }
}
