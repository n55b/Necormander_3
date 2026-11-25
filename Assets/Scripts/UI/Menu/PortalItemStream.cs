using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// All positions are pixels in the 640 x 360 world-camera target.
public sealed class PortalItemStream : MaskableGraphic
{
    private const int Limit = 8;
    private readonly List<Image> icons = new(Limit);
    private Vector2 origin;
    private float progress;
    // Normalized floor time. Delay successive cars on one continuous spiral.
    private const float Emerge = .20f, Thrown = .282f, Pull = .752f, Swallowed = .84f, TrainDelay = .055f;
    private Color bright, dim;

    internal static PortalItemStream Create(FloorPortalGraphic portal, Vector2 from)
    {
        var sprites = new List<Sprite>(Limit);
        var pouch = ItemPouch.Instance;
        void Include(Sprite icon)
        {
            if (icon != null && sprites.Count < Limit && !sprites.Contains(icon)) sprites.Add(icon);
        }
        if (pouch != null)
            for (int i = pouch.SlotCount - 1; i >= 0 && sprites.Count < Limit; --i)
                Include(GroundItem.ItemIcon(pouch.Get(i)));
        Include(EquipmentInventoryManager.Instance?.EquippedEquipment?.baseData?.icon);
        if (InventoryManager.Instance != null && sprites.Count < Limit)
            foreach (var treasure in InventoryManager.Instance.TreasureStacks)
            {
                if (treasure.Value > 0 && treasure.Key != null) Include(treasure.Key.icon);
                if (sprites.Count == Limit) break;
            }
        if (sprites.Count == 0) return null;
        var root = new GameObject("Portal item stream", typeof(RectTransform), typeof(CanvasRenderer));
        root.transform.SetParent(portal.rectTransform, false);
        var stream = root.AddComponent<PortalItemStream>();
        stream.raycastTarget = false;
        stream.rectTransform.anchorMin = stream.rectTransform.pivot = Vector2.zero;
        stream.rectTransform.anchorMax = Vector2.one;
        stream.rectTransform.offsetMin = stream.rectTransform.offsetMax = Vector2.zero;
        stream.origin = from;
        stream.bright = FloorPortalGraphic.TravelColor(11, 7);
        stream.dim = FloorPortalGraphic.TravelColor(9, 7);
        foreach (var sprite in sprites)
        {
            var node = new GameObject(sprite.name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            node.transform.SetParent(root.transform, false);
            var image = node.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.zero;
            stream.icons.Add(image);
        }
        stream.SetProgress(0);
        return stream;
    }

    private float Slot(int index) => icons.Count == 1 ? 0 : (float)index / (icons.Count - 1);

    private static float Opacity(float time) =>
        FloorTravel.Ease(Emerge, Emerge + .027f, time) * (1 - FloorTravel.Ease(Swallowed - .042f, Swallowed, time));

    private Vector2 Path(float time, float slot)
    {
        float throwWeight = FloorTravel.Ease(Emerge, Thrown, time);
        float falling = Mathf.InverseLerp(Pull, Swallowed, time);
        float dive = falling * falling;
        float angle = 2.15f + (time - Thrown) * 12f - slot * 1.35f + dive * 5.1f;
        float radius = (61f + 3f * Mathf.Sin(angle * 2)) * (1 - dive);
        Vector2 orbit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * .52f) * radius;
        orbit.y += orbit.x * .21f;
        Vector2 source = FloorTravel.Hover(origin, 0, Emerge + TrainDelay * slot);
        return Vector2.Lerp(source, FloorTravel.Mouth + orbit, throwWeight)
            + Vector2.up * (16 * Mathf.Sin(Mathf.PI * throwWeight));
    }

    internal void SetProgress(float value)
    {
        progress = Mathf.Clamp01(value);
        for (int i = 0; i < icons.Count; i++)
        {
            float slot = Slot(i);
            float time = progress - TrainDelay * slot;
            float opacity = Opacity(time);
            var image = icons[i];
            image.color = new Color(1, 1, 1, opacity);
            image.enabled = opacity > 0;
            if (!image.enabled) continue;
            image.rectTransform.anchoredPosition = Vector2Int.RoundToInt(Path(time, slot));
            float size = Mathf.Lerp(4, 15, FloorTravel.Ease(Emerge, Thrown, time)) *
                (1 - FloorTravel.Ease(Pull, Swallowed, time));
            image.rectTransform.sizeDelta = Vector2.one * Mathf.Max(1, Mathf.RoundToInt(size));
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        for (int i = 0; i < icons.Count; i++)
        {
            float slot = Slot(i);
            float time = progress - TrainDelay * slot;
            for (int j = 1; j <= 6; j++)
            {
                float past = time - j * .005f;
                float alpha = Opacity(past) * (1 - j / 7f);
                if (alpha <= 0) continue;
                Color c = Color.Lerp(bright, dim, j / 7f);
                c.a = alpha * .76f;
                FloorPortalGraphic.Quad(mesh, Path(past, slot), Vector2.one, c);
            }
        }
    }
}
