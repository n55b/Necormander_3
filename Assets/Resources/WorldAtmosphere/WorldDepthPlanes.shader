Shader "Necromancer/World Depth Planes"
{
    Properties
    {
        _Palette ("Artist swatches", 2D) = "white" {}
        _FloorMask ("Open floor", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Blend One OneMinusSrcAlpha, Zero One
            ZWrite Off Cull Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ DISTANT_BASIN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtmospherePlane.hlsl"
            #include "CloudLight.hlsl"
            TEXTURE2D(_FloorMask);
            float4x4 _FloorUV;
            float4 _FloorMask_TexelSize, _AirLook;
            float _Mist, _AtmosphereWindTime;

            float Open(float2 uv)
            {
                return all(uv >= 0) && all(uv < 1) ? SAMPLE_TEXTURE2D_LOD(_FloorMask,sampler_PointClamp,uv,0).r : 0;
            }

            half4 Frag(V i) : SV_Target
            {
                float2 relative = i.world - _WorldSpaceCameraPos.xy;
                float2 world = floor(i.world*32)/32;
                float time = _AtmosphereWindTime;
                float depth = saturate(_WorldDepthStrength);
                #ifdef DISTANT_BASIN
                    // Seen through transparent gaps in the map. Each shelf recedes at its own rate.
                    float2 far = floor((i.world + relative*.62)*32)/32;
                    float2 mid = floor((i.world + relative*.24)*32)/32;
                    float shelf = .70*Noise(far*.25) + .30*Noise(far*.75);
                    float ledge = .68*Noise(mid*.33+7.1) + .32*Noise(mid*.92);
                    half3 low = Swatch(13,7), ridge = lerp(Swatch(10,7),Swatch(5,0),_AirLook.x);
                    float terraces = floor(shelf*6)*.14;
                    float vapor = smoothstep(.40,.72,Noise(far*.37-float2(time*.037,0)));
                    half3 color = lerp(low,ridge,.16+terraces*.5+vapor*.18);
                    color *= 1-.30*smoothstep(.55,.61,ledge);
                    return half4(color*depth,depth);
                #else
                    float2 stepUV = abs(_FloorMask_TexelSize.xy);
                    float2 grid = mul(_FloorUV,float4(i.world,0,1)).xy * _FloorMask_TexelSize.zw;
                    float2 cell = floor(grid);
                    float2 centerUV = (cell + .5) * stepUV;
                    if (Open(centerUV) < .5) return 0;
                    // Fetch occupancy at texel centers. A boundary sample can otherwise belong
                    // to a different tile than the fractional distance used to shade it.
                    float2 f = grid - cell;
                    float2 side = float2(f.x < .5 ? -1 : 1, f.y < .5 ? -1 : 1);
                    float2 edge = min(f, 1-f);
                    float2 neighbor = side * stepUV;
                    float distance = 1;
                    if (Open(centerUV + float2(neighbor.x,0)) < .5) distance = edge.x;
                    if (Open(centerUV + float2(0,neighbor.y)) < .5) distance = min(distance, edge.y);
                    if (Open(centerUV + neighbor) < .5) distance = min(distance, length(edge));
                    float occlusion = 1-smoothstep(0,.5,distance);
                    float sky = GroundCloud(world);
                    float wash = CloudBank(world/float2(17,11)-_CloudProjection.xy+float2(.36,.27));
                    float lowFog = saturate(_Mist*(.14+.34*wash));
                    float shadow = .44*occlusion;
                    float baseAlpha = (shadow+(1-shadow)*lowFog)*depth;
                    half3 rock = lerp(Swatch(12,7),_WorldShadowTint.rgb,.66);
                    half3 light = lerp(lerp(Swatch(1,7),Swatch(9,5),_AirLook.x),Swatch(8,4),_AirLook.y);
                    half3 color = lerp(rock,light,lowFog/max(.001,shadow+lowFog)*.70);
                    float2 farPixel = floor((i.world + relative*.14*depth)*32);
                    float spores = Glimmers(farPixel,39,time,13.2)*.38*_AirLook.z*depth;
                    // Clouds remove incoming light from the existing tile colour. Mist and airborne
                    // emission are added separately so a shadow cannot brighten a dark tile.
                    float opacity = baseAlpha + (1-baseAlpha)*sky;
                    return half4(color*baseAlpha+light*spores,opacity);
                #endif
            }
            ENDHLSL
        }
    }
}
