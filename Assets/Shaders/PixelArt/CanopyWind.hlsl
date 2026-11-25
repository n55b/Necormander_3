#ifndef NECROMANCER_CANOPY_WIND
#define NECROMANCER_CANOPY_WIND
float _AtmosphereWindTime, _CanopyWindPixels;

// Both the lit silhouette and its projected shadow bend around the same fixed trunk.
float CanopyOffsetPixels(float height, float phase)
{
    float t = _AtmosphereWindTime * 1.65 + phase;
    float gust = .65 + .35 * sin(_AtmosphereWindTime*.54 + phase*.82);
    float sway = .72*sin(t + height*1.6) + .28*sin(t*1.73 - height*2.5);
    return round(sway*gust*smoothstep(.18,.90,height)*_CanopyWindPixels);
}
#endif
