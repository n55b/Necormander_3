#ifndef NECRO_PIXEL_DITHER
#define NECRO_PIXEL_DITHER
// 4x4 ordered coverage. Endpoints are exact: zero hides all, one covers all.
float PixelDitherThreshold(float2 pixel)
{
    int2 p = ((int2)floor(pixel) % 4 + 4) % 4;
    const float bayer[16] = {0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5};
    return (bayer[p.y * 4 + p.x] + .5) / 16.0;
}
#endif
