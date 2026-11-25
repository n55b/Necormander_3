#ifndef NECROMANCER_GROUND_SHADOW_LIGHTING
#define NECROMANCER_GROUND_SHADOW_LIGHTING
#include "CloudLight.hlsl"
float4 _GroundKey; // XY projected distance per unit of height, Z artist strength, W ambient brightness.
float _GroundSunWeight;
int _GroundLightCount;
float4 _GroundLights[8]; // XY origin, Z inverse radius, W luminance.
float4 _GroundCones[8]; // XY forward, Z outer cosine, W inverse feather width.

float3 GroundShadowLighting(float2 contact)
{
    float sun = _GroundSunWeight * (1-.65*GroundCloud(contact));
    float2 direction = _GroundKey.xy * sun;
    float total = sun;
    for (int n = 0; n < _GroundLightCount; ++n)
    {
        float2 delta = contact - _GroundLights[n].xy;
        float d2 = dot(delta, delta);
        float2 away = delta * rsqrt(max(.0001, d2));
        float reach = saturate(1 - sqrt(d2) * _GroundLights[n].z);
        float4 cone = _GroundCones[n];
        float angular = cone.w == 0 ? 1 : saturate((dot(away, cone.xy) - cone.z) * cone.w);
        float weight = reach * reach * angular * _GroundLights[n].w * 5;
        direction += away * lerp(.30, .95, 1 - reach) * weight;
        total += weight;
    }
    return float3(direction / max(.0001,total), _GroundKey.z * saturate(total) * saturate(.44 + .31 * _GroundKey.w + .15 * (total - 1)));
}
#endif
