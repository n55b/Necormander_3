#ifndef NECROMANCER_OCCLUSION_MIST
#define NECROMANCER_OCCLUSION_MIST
float _OcclusionFogProgress;
float4 _OcclusionFogShape; // World center XY, radii in logical pixels ZW.
float4 _OcclusionFogWake;  // Last part of the clearing to settle, in world coordinates.
float _OcclusionFogPPU;
TEXTURE2D(_OcclusionFogPalette);

float MistHash(float2 cell)
{
    float3 q = frac(cell.xyx * .1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float MistNoise(float2 position)
{
    float2 cell = floor(position);
    float2 f = frac(position);
    f = f*f*(3-2*f);
    return lerp(lerp(MistHash(cell), MistHash(cell+float2(1,0)), f.x),
                lerp(MistHash(cell+float2(0,1)), MistHash(cell+1), f.x), f.y);
}

half4 ApplyOcclusionMist(half4 scenery, float2 world)
{
    UNITY_BRANCH if (_OcclusionFogProgress <= 0 || scenery.a <= .008) return scenery;
    float growth = smoothstep(0, 1, _OcclusionFogProgress);
    float2 pixel = floor(world * _OcclusionFogPPU) + .5;
    float2 radius = max(1, _OcclusionFogShape.zw * lerp(.42, 1, growth));
    float2 p = (pixel - _OcclusionFogShape.xy * _OcclusionFogPPU) / radius;
    float2 wake = (_OcclusionFogWake.xy - _OcclusionFogShape.xy) * _OcclusionFogPPU / radius;
    float along = saturate(dot(p,wake) / max(.0001, dot(wake,wake)));
    float distanceToWake = length(p - along*wake) + min(.24, .4*length(wake))*along;
    UNITY_BRANCH if (distanceToWake >= 1.55) return scenery;

    // The density field drifts over the canopy even while the player stands still.
    float t = _Time.y;
    float2 sampledWorld = pixel / _OcclusionFogPPU;
    float billow = MistNoise(sampledWorld * 2.1 + float2(.28*t, -.19*t));
    float curl = MistNoise(sampledWorld * 4.6 + float2(-.21*t, billow*1.8 + .14*t));
    float density = distanceToWake + .55*(billow-.5) + .26*(curl-.5);
    float opening = growth * (1 - smoothstep(.26, 1.14, density));
    half3 haze = SAMPLE_TEXTURE2D(_OcclusionFogPalette, sampler_PointClamp, float2(1.5/16, 1-7.5/9)).rgb;
    scenery.rgb = lerp(scenery.rgb, haze, .12 * opening * (.4+.6*billow));
    scenery.a *= 1 - .94 * opening;
    return scenery;
}
#endif
