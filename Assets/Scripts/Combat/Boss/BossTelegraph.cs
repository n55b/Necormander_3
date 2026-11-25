using UnityEngine;

/// <summary>Shared world geometry for attack previews. Dimensions are in world units.</summary>
public static class BossTelegraph
{
    private const string FillName = "Boss_Telegraph_Rect_Fill";

    public static GameObject SpawnCircle(Vector2 position, float diameter, Color color, int sortingOrder = 8,
        string name = "Boss_Telegraph_Circle")
    {
        var renderer = PixelVectorShape.Create(name, PixelVectorShape.Kind.Ellipse, color, sortingOrder,
            PixelVectorShape.Stroke.Highlight, sortingLayer: WorldDepthOrder.GroundEffectsLayerId);
        renderer.transform.position = position;
        renderer.transform.localScale = new Vector3(diameter, diameter, 1f);
        return renderer.gameObject;
    }

    public static GameObject SpawnRing(Vector2 position, float diameter, Color color, int sortingOrder = 7,
        string name = "Boss_Telegraph_Ring", bool boundaryOnly = false)
    {
        var renderer = PixelVectorShape.Create(name, boundaryOnly ? PixelVectorShape.Kind.Ellipse : PixelVectorShape.Kind.Ring,
            color, sortingOrder, boundaryOnly ? PixelVectorShape.Stroke.Boundary : PixelVectorShape.Stroke.Highlight,
            .85f, WorldDepthOrder.GroundEffectsLayerId);
        renderer.transform.position = position;
        renderer.transform.localScale = new Vector3(diameter, diameter, 1f);
        return renderer.gameObject;
    }

    public static GameObject SpawnRect(Color color, int sortingOrder = 6, string name = "Boss_Telegraph_Rect")
        => PixelVectorShape.Create(name, PixelVectorShape.Kind.Rectangle, color, sortingOrder,
            PixelVectorShape.Stroke.Highlight, sortingLayer: WorldDepthOrder.GroundEffectsLayerId).gameObject;

    public static void UpdateRect(GameObject rect, Vector2 origin, Vector2 dir, float length, float width)
    {
        if (rect == null) return;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        rect.transform.SetPositionAndRotation(origin + dir * (length * 0.5f), Quaternion.Euler(0f, 0f, angle));
        rect.transform.localScale = new Vector3(length, width, 1f);
    }

    /// <summary>Fills from the center toward both sides, regardless of lane length.</summary>
    public static void UpdateRectWithFill(GameObject rect, Vector2 origin, Vector2 dir, float length, float width,
        float progress01, Color fillColor, float windup = 1f)
    {
        if (rect == null) return;
        UpdateRect(rect, origin, dir, length, width);

        Transform fill = rect.transform.Find(FillName);
        if (fill == null)
        {
            var background = rect.GetComponent<SpriteRenderer>();
            fill = PixelVectorShape.Create(FillName, PixelVectorShape.Kind.Rectangle, fillColor,
                background.sortingOrder + 1, sortingLayer: background.sortingLayerID).transform;
            fill.SetParent(rect.transform, false);
        }

        var cue = PixelVectorShape.ThreatCue(progress01, windup);
        PixelVectorShape.SetThreatCue(rect.GetComponent<SpriteRenderer>(), cue);
        PixelVectorShape.SetThreatCue(fill.GetComponent<SpriteRenderer>(), cue);
        fill.localScale = new Vector3(1f, Mathf.Clamp01(progress01), 1f);
    }
}
