#ifndef NECROMANCER_TILE_PIXEL_GRID
#define NECROMANCER_TILE_PIXEL_GRID
float4 _MainTex_TexelSize;
float _FieldTileAtlas;

// Field tiles have 16 texels of art per 17-texel stride, with the first slice at (1, 0).
// Move only atlas boundary UVs slightly inward to keep point samples out of the gutter.
float2 TileAtlasUV(float2 uv)
{
    if (_FieldTileAtlas < .5 || any(abs(_MainTex_TexelSize.zw - 272) > .5)) return uv;
    float2 corner = uv * _MainTex_TexelSize.zw - float2(1, 0);
    float2 local = corner - 17 * floor((corner + .5) / 17);
    return uv + (step(local, .01) - step(15.99, local)) * .05 * _MainTex_TexelSize.xy;
}
#endif
