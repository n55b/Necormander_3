using System;
using UnityEngine;

[CreateAssetMenu(menuName = "UI/Pixel text motion library")]
public sealed class TextMotionLibrary : ScriptableObject
{
    public enum Shape { Bounce, Shake, Wave, Palette, Jitter, Rise, DoubleBeat, Typewriter }
    public enum Unit { Label, Word, Glyph }
    [Serializable] public class Preset
    {
        public string id;
        public Shape shape;
        public Unit unit;
        [Min(.01f)] public float duration = .24f;
        [Min(0)] public float pixels = 2f;
        [Min(0)] public float cycles = 1;
        [Range(0,1)] public float stagger = .3f;
        [Min(1)] public float charactersPerSecond = 42;
        public bool loop;
        public bool useGameplayTime;
        public Color accent = Color.white;
        [Tooltip("Artist palette coordinates from the top left, in playback order.")]
        public Vector2Int[] swatches;

        public Color Accent(float age,int index,float speed)
        {
            if(swatches==null || swatches.Length==0) return accent;
            float phase=age*Mathf.Max(.01f,speed)*cycles/Mathf.Max(.01f,duration)+stagger*index;
            var point=swatches[Mathf.FloorToInt(Mathf.Repeat(phase,1)*swatches.Length)];
            return PaletteColor(point, accent);
        }

        public Vector2 Sample(float time, int index, float speed, out float tint)
        {
            float t = time * Mathf.Max(.01f, speed) / Mathf.Max(.01f, duration);
            tint = 0;
            if (!loop && t >= 1) return Vector2.zero;
            float fade = loop ? 1 : Mathf.Clamp01(1 - t);
            float phase = 2f * Mathf.PI * (cycles * t + stagger * index);
            switch (shape)
            {
                case Shape.Typewriter: return Vector2.zero;
                case Shape.Palette: tint = (.5f + .5f * Mathf.Sin(phase)) * fade; return Vector2.zero;
                case Shape.Shake:
                case Shape.Jitter:
                    float tick = Mathf.Floor(t * cycles * 7f);
                    return new Vector2(Mathf.PerlinNoise(index * .61f + 2, tick * .8f) - .5f,
                        Mathf.PerlinNoise(tick * .53f, index * .73f + 53) - .5f) * (pixels * fade * 2);
                case Shape.Rise: return Vector2.up * (pixels * fade);
                case Shape.DoubleBeat: tint = fade * Mathf.Abs(Mathf.Cos(phase)); return Vector2.up * (pixels * tint);
                case Shape.Bounce: return Vector2.up * (pixels * fade * Mathf.Cos(phase));
                default: return Vector2.up * (pixels * fade * Mathf.Sin(phase));
            }
        }
    }
    public Preset[] presets;
    [Range(0,.4f)] public float punctuationRest = .08f;
    private static Color32[] palettePixels;
    private static int paletteWidth,paletteHeight;
    internal static Color PaletteColor(Vector2Int point, Color fallback)
    {
        if (palettePixels == null)
        {
            var texture = Resources.Load<Texture2D>("WorldAtmosphere/necrohanderPalette");
            if (texture == null || !texture.isReadable) return fallback;
            paletteWidth = texture.width;
            paletteHeight = texture.height;
            palettePixels = texture.GetPixels32();
        }
        return palettePixels[(paletteHeight - 1 - Mathf.Clamp(point.y, 0, paletteHeight - 1)) * paletteWidth
            + Mathf.Clamp(point.x, 0, paletteWidth - 1)];
    }
    private static TextMotionLibrary instance;
    public static TextMotionLibrary Instance => instance != null ? instance : instance = Resources.Load<TextMotionLibrary>("UI/TextMotion");
    public Preset Find(string id)
    {
        if (presets != null) foreach (var p in presets) if (p != null && p.id == id) return p;
        return null;
    }
}
