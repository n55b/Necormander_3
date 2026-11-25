#ifndef NECROMANCER_CLOUD_LIGHT
#define NECROMANCER_CLOUD_LIGHT
TEXTURE2D(_CloudCoverMap);
float4 _CloudProjection; // XY translation in repeating UVs, Z shadow strength, W map availability.
float4 _WorldShadowTint;

float2 SkyUV(float2 world)
{
    return world / float2(27,19) - _CloudProjection.xy;
}

float CloudBank(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_CloudCoverMap,sampler_LinearRepeat,uv,0).r * _CloudProjection.w;
}

float GroundCloud(float2 world)
{
    float2 uv = SkyUV(world);
    float bank = CloudBank(uv);
    float2 highUV = (world.yx/float2(19,27)*1.64 - _CloudProjection.yx)*float2(-1,1);
    float veil = CloudBank(highUV+float2(.41,.28));
    return _CloudProjection.z * (bank + (1-bank)*veil*.28);
}

half3 IndirectShadowColor()
{
    return half3(.12,.11,.15) + _WorldShadowTint.rgb*.46;
}
#endif
