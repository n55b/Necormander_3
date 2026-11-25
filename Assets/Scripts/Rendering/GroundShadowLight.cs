using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Placed beside the world lights so streamed rooms register and release their own lights.
[RequireComponent(typeof(Light2D)), DisallowMultipleComponent]
public sealed class GroundShadowLight : MonoBehaviour
{
    internal static readonly List<Light2D> Active = new();
    internal static readonly List<GroundShadowLight> Globals = new();
    private Light2D lamp;
    private Color original;
    private float originalIntensity;
    internal void Tint(Color tint, float intensity = 1f)
    {
        var result = original * tint;
        if (lamp.color != result) lamp.color = result;
        float desired = originalIntensity * Mathf.Max(0,intensity);
        if (Mathf.Abs(lamp.intensity - desired) > .001f) lamp.intensity = desired;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLights() { Active.Clear(); Globals.Clear(); }

    private void OnEnable()
    {
        lamp = GetComponent<Light2D>();
        original = lamp.color;
        originalIntensity = lamp.intensity;
        Active.Add(lamp);
        if (lamp.lightType == Light2D.LightType.Global) Globals.Add(this);
    }

    private void OnDisable() { Active.Remove(lamp); Globals.Remove(this); if (lamp != null) Tint(Color.white); }
}
