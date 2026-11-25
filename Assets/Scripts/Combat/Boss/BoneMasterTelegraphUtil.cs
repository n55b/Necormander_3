using UnityEngine;

/// <summary>Creates visual-only attack previews from the same dimensions as BossCombat.</summary>
public static class BoneMasterTelegraphUtil
{
    private const string ConeFillName = "Fill";

    public static GameObject SpawnLane(BaseEntity entity, Vector2 origin, Vector2 dir, float length, float width,
        Color color, BaseHitBox prefab, float windup, float life = 0.1f)
    {
        if (prefab == null)
        {
            Debug.LogError("[BoneMaster] Assign the lane telegraph prefab on the AI pattern.");
            return null;
        }

        Vector2 direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        var box = Object.Instantiate(prefab, origin, Rotation(direction));
        box.name = "BoneMaster_Telegraph_Lane";
        box.transform.localScale = new Vector3(length, width, 1f);
        InitAsVisualOnly(box, entity, windup, life, color);
        return box.gameObject;
    }

    public static GameObject SpawnEllipse(BaseEntity entity, Vector2 pos, float radiusX, float radiusY,
        Color color, BaseHitBox prefab, float windup, float life = 0.1f)
    {
        if (prefab == null)
        {
            Debug.LogError("[BoneMaster] Assign the ellipse telegraph prefab on the AI pattern.");
            return null;
        }

        var box = Object.Instantiate(prefab, pos, Quaternion.identity);
        box.name = "BoneMaster_Telegraph_Ellipse";
        box.transform.localScale = new Vector3(radiusX * 2f, radiusY * 2f, 1f);
        InitAsVisualOnly(box, entity, windup, life, color);
        return box.gameObject;
    }

    private static void InitAsVisualOnly(BaseHitBox box, BaseEntity entity, float windup, float life, Color color)
    {
        // A zero target mask leaves all damage to the boss attack.
        box.Init(new DamageInfo(0f, DamageType.Physical, entity != null ? entity.gameObject : null), 0, life, windup);
        foreach (var renderer in box.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.color = new Color(color.r, color.g, color.b, renderer.color.a);
    }

    public static GameObject SpawnCone(BaseEntity entity, Vector2 pos, Vector2 facingDir, float radius,
        float halfAngleDegrees, Color color, int sortingOrder = 5000, float fillAlpha = 0.35f)
    {
        int layer = WorldDepthOrder.GroundEffectsLayerId;
        float angleThreshold = Mathf.Cos(Mathf.Clamp(halfAngleDegrees, 0f, 180f) * Mathf.Deg2Rad);
        var kind = halfAngleDegrees >= 180f ? PixelVectorShape.Kind.Ellipse : PixelVectorShape.Kind.Cone;
        var boundary = PixelVectorShape.Create("BoneMaster_Telegraph_Cone", kind, color, sortingOrder,
            PixelVectorShape.Stroke.Boundary, angleThreshold, layer);
        boundary.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

        Color fillColor = color;
        fillColor.a *= fillAlpha;
        var fill = PixelVectorShape.Create(ConeFillName, kind, fillColor, sortingOrder - 1,
            PixelVectorShape.Stroke.None, angleThreshold, layer);
        fill.transform.SetParent(boundary.transform, false);
        UpdateCone(boundary.gameObject, pos, facingDir, 0f);
        return boundary.gameObject;
    }

    public static void UpdateCone(GameObject cone, Vector2 pos, Vector2 dir, float progress01 = -1f, float windup = 1f)
    {
        if (cone == null) return;

        cone.transform.position = pos;
        if (dir.sqrMagnitude > 0.0001f) cone.transform.rotation = Rotation(dir);

        if (progress01 < 0f) return;
        var fill = cone.transform.Find(ConeFillName);
        var cue = PixelVectorShape.ThreatCue(progress01, windup);
        PixelVectorShape.SetThreatCue(cone.GetComponent<SpriteRenderer>(), cue);
        if (fill != null)
        {
            PixelVectorShape.SetThreatCue(fill.GetComponent<SpriteRenderer>(), cue);
            fill.localScale = Vector3.one * Mathf.Clamp01(progress01);
        }
    }

    public static void UpdatePosition(GameObject telegraph, Vector2 pos)
    {
        if (telegraph != null) telegraph.transform.position = pos;
    }

    private static Quaternion Rotation(Vector2 direction)
        => Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
}
