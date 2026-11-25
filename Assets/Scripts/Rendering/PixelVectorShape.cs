using UnityEngine;

/// <summary>Draws a procedural shape in the same sorting system as world sprites.</summary>
public static class PixelVectorShape
{
    public enum Kind { Ellipse, Rectangle, Ring, Cone, Locator, Arrow, Gauge, ProjectileArrow }
    public enum Stroke { None, Indicator, Boundary, Highlight }

    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int ScreenShapeId = Shader.PropertyToID("_ScreenShape");
    private static readonly int LocalSizeId = Shader.PropertyToID("_LocalSize");
    private static readonly int GaugeFillId = Shader.PropertyToID("_GaugeFill");
    private static readonly int IndicatorStrokeId = Shader.PropertyToID("_IndicatorStroke");
    private static readonly int StrokeWidthsId = Shader.PropertyToID("_StrokeWidths");
    private static readonly int ThreatCueId = Shader.PropertyToID("_ThreatCue");
    private static Sprite carrier;

    // The flash follows attack time, so pausing or cancelling a windup also pauses or cancels its cue.
    public static Vector4 ThreatCue(float progress, float duration)
    {
        progress = Mathf.Clamp01(progress);
        float window = Mathf.Min(.14f, duration * .22f);
        float remaining = (1f - progress) * duration;
        float phase = window > 0 ? Mathf.Clamp01(1f - remaining / window) : 0;
        float pulse = UISkinSettings.MotionOn ? 4f * phase * (1f - phase) : 0;
        return new Vector4(1, progress, pulse, 0);
    }

    public static void SetThreatCue(SpriteRenderer renderer, Vector4 cue)
    {
        ReadProperties(renderer);
        properties.SetVector(ThreatCueId, cue);
        renderer.SetPropertyBlock(properties);
    }

    private static Material material;
    private static MaterialPropertyBlock properties;

    public static bool IsApplied(SpriteRenderer renderer) => renderer != null && material != null && renderer.sharedMaterial == material;

    public static SpriteRenderer Create(string name, Kind kind, Color color, int order,
        Stroke stroke = Stroke.None, float parameter = 0f, int sortingLayer = 0)
    {
        var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
        renderer.color = color;
        renderer.sortingLayerID = sortingLayer;
        renderer.sortingOrder = order;
        Apply(renderer, kind, stroke, parameter);
        return renderer;
    }

    public static void Apply(SpriteRenderer renderer, Kind kind, Stroke stroke = Stroke.None, float parameter = 0f)
    {
        if (material == null)
        {
            var settings = CameraSettingsSO.Instance;
            var shader = settings != null ? settings.vectorShapeShader : null;
            if (shader == null)
            {
                Debug.LogError("CameraSettings is missing the pixel vector shape shader.", renderer);
                return;
            }

            material = new Material(shader) { name = "Pixel Vector Shape", hideFlags = HideFlags.HideAndDontSave };
            CameraSettingsSO.Changed -= ApplyStyle;
            CameraSettingsSO.Changed += ApplyStyle;
            ApplyStyle();
        }

        if (carrier == null)
        {
            // The built-in white texture supplies quad geometry. The shader never samples it.
            carrier = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            carrier.name = "Pixel Vector Quad";
            carrier.hideFlags = HideFlags.HideAndDontSave;
        }

        renderer.sharedMaterial = material;
        renderer.sprite = carrier;
        SetGeometry(renderer, kind, stroke, parameter);
    }

    public static void SetGeometry(SpriteRenderer renderer, Kind kind, Stroke stroke, float parameter)
    {
        ReadProperties(renderer);
        properties.SetVector(ShapeId, new Vector4((int)kind, parameter, (int)stroke, 0f));
        renderer.SetPropertyBlock(properties);
    }

    public static void SetScreenShape(SpriteRenderer renderer, Vector2 size, Vector2 direction, bool updateBounds = false)
    {
        ReadProperties(renderer);
        properties.SetVector(ScreenShapeId, new Vector4(size.x, size.y, direction.x, direction.y));
        renderer.SetPropertyBlock(properties);
        if (updateBounds) renderer.localBounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 1f));
    }

    public static void SetGaugeProgress(SpriteRenderer renderer, float progress, Color color, float duration)
    {
        ReadProperties(renderer);
        properties.SetVector(ShapeId, new Vector4((int)Kind.Gauge, progress, (int)Stroke.Boundary, 0f));
        properties.SetColor(GaugeFillId, color);
        properties.SetVector(ThreatCueId, ThreatCue(progress, duration));
        renderer.SetPropertyBlock(properties);
    }

    public static void SetLocalSize(SpriteRenderer renderer, Vector2 size)
    {
        ReadProperties(renderer);
        properties.SetVector(LocalSizeId, size);
        renderer.SetPropertyBlock(properties);
        renderer.localBounds = new Bounds(Vector3.zero, new Vector3(size.x, size.y, 1f));
    }

    private static void ReadProperties(SpriteRenderer renderer)
    {
        properties ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
    }

    private static void ApplyStyle()
    {
        if (material == null) return;
        var settings = CameraSettingsSO.Instance;
        material.SetColor(IndicatorStrokeId, settings.vectorIndicatorStroke);
        material.SetVector(StrokeWidthsId, new Vector4(settings.vectorIndicatorStrokePixels,
            settings.vectorTelegraphStrokePixels, 0f, 0f));
    }
}
