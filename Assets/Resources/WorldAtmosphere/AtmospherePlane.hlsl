#ifndef NECROMANCER_ATMOSPHERE_PLANE
#define NECROMANCER_ATMOSPHERE_PLANE
TEXTURE2D(_Palette);
float _WorldDepthStrength;
struct A { float3 pos:POSITION; };
struct V { float4 pos:SV_POSITION; float2 world:TEXCOORD0; };
V Vert(A a) {
    V o;
    float2 clipXY = a.pos.xy * 2;
    float3 world = mul(UNITY_MATRIX_I_V, float4(clipXY * unity_OrthoParams.xy, -3, 1)).xyz;
    o.pos = TransformWorldToHClip(world);
    o.world = world.xy;
    return o;
}
float Hash(float2 p) {
    float3 q = frac(p.xyx * .1031);
    q += dot(q, q.yzx + 33.33); return frac((q.x+q.y)*q.z);
}
float Noise(float2 p) {
    float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),
                lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
}
half3 Swatch(float col,float row) {
    return SAMPLE_TEXTURE2D(_Palette,sampler_PointClamp,float2((col+.5)/16,1-(row+.5)/9)).rgb;
}
// One glimmer per world-space cell. The same positions survive scrolling, zoom and room blends.
float Glimmers(float2 pixel, float cellWidth, float t, float salt)
{
    float2 grid = pixel / cellWidth;
    float2 cell = floor(grid);
    float seed = Hash(cell + salt);
    float phase = seed * 6.283 + t * (.32 + .48*seed);
    float2 center = .5 + .13*float2(sin(phase),cos(phase*.83 + seed))
                       + .55*(float2(seed,Hash(cell.yx-salt))-.5);
    float2 offset = round((frac(grid)-center)*cellWidth);
    float2 d = abs(offset);
    float pulse = smoothstep(-.35,.95,sin(phase + 3*seed));
    float star = step(d.x+d.y,.1) + step(.72,seed)*step(d.x*d.y,.1)*step(d.x+d.y,1.1)*pulse*.68;
    float2 companion = offset - float2(3,2);
    float dust = step(.66,seed)*step(dot(companion,companion),.1)*.35;
    return pulse * (star + dust + .16*saturate(1-dot(d,d)/5));
}
#endif
