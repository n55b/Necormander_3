using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Editable UI silhouettes compiled to colored triangles for uGUI.</summary>
public sealed class VectorUiCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Layer
    {
        public Color32 color;
        public Vector2[] vertices;
        public int[] triangles;
        [NonSerialized] private bool checkedBounds;
        [NonSerialized] private bool withinSprite;

        internal bool FitsSprite
        {
            get
            {
                if (checkedBounds) return withinSprite;
                withinSprite = Array.TrueForAll(vertices, p => p.x >= 0 && p.y >= 0 && p.x <= 1 && p.y <= 1);
                checkedBounds = true;
                return withinSprite;
            }
        }
    }

    [Serializable]
    public sealed class Entry
    {
        public Sprite sprite;
        public Vector2 artSize;
        public float slice;
        public Layer[] layers;
    }

    [SerializeField] private Entry[] entries;
    private static Dictionary<Sprite, Entry> bySprite;

    public static bool TryGet(Sprite sprite, out Entry entry)
    {
        entry = null;
        if (sprite == null) return false;
        if (bySprite == null)
        {
            var catalog = Resources.Load<VectorUiCatalog>("UI/VectorUiCatalog");
            if (catalog == null || catalog.entries == null) return false;
            bySprite = new Dictionary<Sprite, Entry>();
            foreach (var shape in catalog.entries)
                if (shape.sprite != null) bySprite[shape.sprite] = shape;
        }

        return bySprite.TryGetValue(sprite, out entry);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => bySprite = null;

#if UNITY_EDITOR
    private void OnValidate() => bySprite = null;
#endif
}
