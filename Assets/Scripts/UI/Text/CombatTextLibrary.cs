using System;
using UnityEngine;

[CreateAssetMenu(menuName = "UI/Combat text library")]
public sealed class CombatTextLibrary : ScriptableObject
{
    public enum Mark { None, Star, TwinSlash, Nova, Guard }
    [Serializable]
    public sealed class Style
    {
        public string id, glyphMotion;
        public Mark mark;
        [Min(8)] public int fontPixels = 10;
        [Min(.1f)] public float lifetime = 1;
        [Range(.1f, .9f)] public float fadeFrom = .68f;
        [Min(0)] public int risePixels = 18, spreadPixels = 8;
        [Range(0, 24)] public int shards = 8;
        [Range(4, 120)] public int burstRadius = 24;
        [Range(0, 32)] public int edgeRays;
        [Range(0, 4)] public int priority;
        public Vector2Int swatch;

        public Vector2 Offset(float t, int lane, bool animate)
        {
            float side = (lane & 1) == 0 ? -1 : 1;
            float fan = (lane % 6 / 2 + 1) / 3f;
            float lift = 1 - Mathf.Pow(1 - t, 3);
            return new Vector2(side * fan * (9 + (animate ? spreadPixels * lift : 0)),
                animate ? risePixels * (lift + .17f * Mathf.Sin(t * Mathf.PI * 2) * (1 - t)) : 0);
        }
    }

    public Style[] styles;
    public Style Find(string id)
    {
        if (styles != null)
            foreach (var style in styles)
                if (style != null && style.id == id) return style;
        return null;
    }
}
