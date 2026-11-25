using UnityEngine;

// Play-session overrides. Room profiles and authoring assets continue to supply the defaults.
[System.Serializable]
public sealed class LightingPreview
{
    public static LightingPreview Active { get; internal set; }
    public const float DefaultHighlight = .24f, DefaultThreshold = .53f, DefaultRadius = 1.35f;
    public float azimuth, elevation, intensity = 1, shadows, highlight = DefaultHighlight;
    public Color sun = Color.white;
    public float bloom, threshold = DefaultThreshold, radius = DefaultRadius, mist, shafts = 1, wind = 1, canopy;
    public float depthSuppression;
    public float cloudSuppression; // Missing in older lighting presets, which use the full room strength.
    public float airSuppression; // Zero keeps the authored density in previously saved lighting presets.
    public float borderOpacity, borderWidth, borderSpeed;

    public static LightingPreview From(CameraSettingsSO settings) => new LightingPreview
    {
        azimuth = Mathf.Repeat(settings.groundShadowAngle + 180,360),
        elevation = Mathf.Atan2(1,settings.groundShadowLength) * Mathf.Rad2Deg,
        shadows = settings.groundShadowStrength,
        bloom = settings.bloomStrength, mist = settings.mistStrength, canopy = settings.canopyWindPixels,
        borderOpacity = settings.edgeMistOpacity, borderWidth = settings.edgeMistWidth, borderSpeed = settings.edgeMistSpeed
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Active = null;
}
