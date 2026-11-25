using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Rendering/Pixel particle library")]
public sealed class ParticleLibrary : ScriptableObject
{
    public enum Density { Low, Medium, High }
    public enum Simulation { Analytic, Compute }
    [Serializable] public class Preset
    {
        public WorldSparkField.Kind kind;
        public Vector2 speed, lifetime;
        [Range(0, 180)] public float spreadDegrees;
        [Range(0, 8)] public int paletteRow;
        [Range(0, 13)] public int paletteColumn;
        [Range(1,6)] public int radiusPixels = 1;
        public bool important, terrainCollision;
    }
    public Preset[] presets;
    public Density density = Density.Medium;
    public Simulation simulation = Simulation.Compute;
    public int FrameBudget => density == Density.Low ? 28 : density == Density.High ? 96 : 64;
    [Tooltip("Concurrent decorative quad area in logical square pixels, Low / Medium / High.")]
    public Vector3Int decorativePixelBudget = new Vector3Int(1720, 5400, 8400);
    public int DecorativePixelBudget => Mathf.Max(1, decorativePixelBudget[(int)density]);
    public float AmbientRate => density == Density.Low ? 8 : density == Density.High ? 29 : 18;
    public Preset Find(WorldSparkField.Kind kind)
    {
        if (presets != null) foreach (var preset in presets) if (preset.kind == kind) return preset;
        return null;
    }
}
