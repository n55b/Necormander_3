Shader "Necromancer/Terrain Contact Shadow"
{
    Properties { [PerRendererData] _MainTex("Tile atlas", 2D) = "white" {}
        _FieldTileAtlas("Field tile atlas gutters", Float) = 1
        [HideInInspector] _ProceduralEdges("Geometry edges", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Blend DstColor OneMinusSrcAlpha, Zero One
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GroundShadowLighting.hlsl"
            #include "Assets/Shaders/PixelArt/TilePixelGrid.hlsl"
            TEXTURE2D(_MainTex);
            TEXTURE2D(_Relief);
            float4 _ReliefSize, _ReliefBasis;
            float _ProceduralEdges, _WorldDepthStrength;
            struct A { float3 pos:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 light:TEXCOORD1; };
            V Vert(A a)
            {
                V o;
                float3 world = TransformObjectToWorld(a.pos);
                o.light = GroundShadowLighting(world.xy);
                o.light.z *= _ProceduralEdges > .5 ? 1 : a.color.a;
                o.pos = TransformWorldToHClip(world);
                o.uv = _ProceduralEdges > .5 ? a.uv : TileAtlasUV(a.uv);
                return o;
            }
            float3 Relief(float2 cell)
            {
                if (any(cell < 0) || any(cell >= _ReliefSize.xy)) return 0;
                float3 value = SAMPLE_TEXTURE2D_LOD(_Relief,sampler_PointClamp,(cell+.5)*_ReliefSize.zw,0).rga;
                return value * float3(255.0/32.0,1,1);
            }
            float Cast(float2 p, float2 light)
            {
                float2 cell = floor(p);
                float3 surface = Relief(cell);
                if (surface.x > 0 && frac(p.y) >= surface.y)
                {
                    // Red measures the distance to the lip in cells; alpha identifies authored cliff faces.
                    float belowLip = max(0,surface.x-frac(p.y));
                    float facing = saturate(.62+.38*dot(light,_ReliefBasis.zw));
                    return surface.z*_WorldDepthStrength*facing*(.14+.38*saturate(belowLip*.53));
                }
                // Trace from the receiver toward the light, in cell-height units, across whole columns.
                float2 ray = -float2(dot(light,_ReliefBasis.xy),dot(light,_ReliefBasis.zw));
                float2 stepCell = float2(ray.x >= 0 ? 1 : -1, ray.y >= 0 ? 1 : -1);
                float2 inv = stepCell / max(abs(ray),.00001);
                float2 crossed = (cell + max(stepCell,0) - p) * inv;
                float reach = min(8, 2.98 / max(.001,max(abs(ray.x),abs(ray.y))));
                float enter = 0;
                [loop] for (int n = 0; n < 8; n++)
                {
                    float leave = min(min(crossed.x,crossed.y),reach);
                    if (surface.x > 0)
                    {
                        float start = enter, end = leave;
                        float toe = (cell.y + surface.y - p.y) * inv.y;
                        if (ray.y >= 0) start = max(start,toe); else end = min(end,toe);
                        if (start <= end && start < surface.x)
                        {
                            float band = saturate(1 - start / surface.x);
                            return .72*smoothstep(0,1,band);
                        }
                    }
                    if (leave >= reach) break;
                    bool xFirst = crossed.x <= crossed.y;
                    if (xFirst) { cell.x += stepCell.x; crossed.x += abs(inv.x); }
                    else { cell.y += stepCell.y; crossed.y += abs(inv.y); }
                    enter = leave;
                    surface = Relief(cell);
                }
                return 0;
            }
            half4 Frag(V i):SV_Target
            {
                half mask = _ProceduralEdges > .5 ? Cast(i.uv, i.light.xy) :
                    SAMPLE_TEXTURE2D(_MainTex,sampler_PointClamp,i.uv).a;
                half opacity = mask * i.light.z;
                return half4(IndirectShadowColor()*opacity,opacity);
            }
            ENDHLSL
        }
    }
}
