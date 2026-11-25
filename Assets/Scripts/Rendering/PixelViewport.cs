using UnityEngine;

/// <summary>Converts between display pixels, the native render target, the 640×360 logical frame, canvases and the gameplay plane.</summary>
public static class PixelViewport
{
    public const int Width = 640;
    public const int Height = 360;
    private const float FullFrameAspectTolerance = .01f;

    public static RectInt CalculateDisplayRect(int width, int height)
    {
        if (width <= 0 || height <= 0) return default;
        // Unity's 16:9 Game View can be a few pixels off from the nominal aspect ratio.
        float aspect = (float)Width / Height;
        if (Mathf.Abs(width / (float)height / aspect - 1f) <= FullFrameAspectTolerance)
            return new RectInt(0, 0, width, height);

        int w = Mathf.Max(1, Mathf.Min(width, Mathf.RoundToInt(height * aspect)));
        int h = Mathf.Max(1, Mathf.Min(height, Mathf.RoundToInt(width / aspect)));
        return new RectInt(Mathf.FloorToInt((width - w) * .5f), Mathf.FloorToInt((height - h) * .5f), w, h);
    }

    public static Vector2 ScreenToGamePoint(Vector2 point)
        => ScreenToGamePoint(point, PixelArtUpscaleRig.Output?.DisplayRect ?? default);

    public static Vector2 ScreenToGamePoint(Vector2 point, RectInt display)
    {
        if (display.width <= 0 || display.height <= 0) return point;
        Vector2 local = point - (Vector2)display.position;
        return new Vector2(local.x / display.width * Width, local.y / display.height * Height);
    }

    public static Vector2 GameToScreenPoint(Vector2 point, RectInt display)
    {
        if (display.width <= 0 || display.height <= 0) return point;
        return (Vector2)display.position + new Vector2(point.x / Width * display.width, point.y / Height * display.height);
    }

    public static Camera WorldCamera => PixelArtUpscaleRig.RenderCamera != null ? PixelArtUpscaleRig.RenderCamera : Camera.main;

    public static float WorldUnitsPerPixel
    {
        get
        {
            var camera = WorldCamera;
            return camera != null && camera.orthographic ? camera.orthographicSize * 2f / Height : 0f;
        }
    }

    public static float QuantizeStrokeWidth(float width, float unitsPerPixel)
        => width <= 0f || unitsPerPixel <= 0f ? width : Mathf.Max(1f, Mathf.Round(width / unitsPerPixel)) * unitsPerPixel;

    private static bool RendersToGameFrame(Camera camera)
        => camera != null && PixelArtUpscaleRig.Output is { } output && camera.targetTexture == output.Frame;

    public static Camera CanvasCamera(Canvas canvas)
    {
        var root = canvas != null ? canvas.rootCanvas : null;
        if (root == null || root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        var camera = root.worldCamera;
        if (root.renderMode == RenderMode.WorldSpace && (camera == null || !camera.isActiveAndEnabled))
            return WorldCamera;
        return camera;
    }

    internal static Vector2 DisplayToTarget(Vector2 point)
        => point - (Vector2)(PixelArtUpscaleRig.Output?.DisplayRect.position ?? Vector2Int.zero);

    public static Vector2 ProjectToLogical(Camera camera, Vector3 world)
    {
        var uv = camera.WorldToViewportPoint(world);
        return new Vector2(uv.x * Width, uv.y * Height);
    }

    public static Vector2 PointerForCanvas(Canvas canvas, Vector2 displayPoint)
        => RendersToGameFrame(CanvasCamera(canvas)) ? DisplayToTarget(displayPoint) : displayPoint;

    public static Vector2 CanvasToGamePoint(Canvas canvas, Vector3 point)
    {
        var camera = CanvasCamera(canvas);
        var screen = RectTransformUtility.WorldToScreenPoint(camera, point);
        return RendersToGameFrame(camera) ? ProjectToLogical(camera, point) : ScreenToGamePoint(screen);
    }

    public static float CanvasPixelsPerUnit(Canvas canvas, RectTransform rect)
        => (CanvasToGamePoint(canvas, rect.TransformPoint(Vector3.up)) -
            CanvasToGamePoint(canvas, rect.position)).magnitude;

    public static bool TryGamePointToCanvas(Vector2 gamePoint, Canvas canvas, RectTransform parent, out Vector2 local)
    {
        local = default;
        if (canvas == null || parent == null) return false;
        var camera = CanvasCamera(canvas);
        var point = RendersToGameFrame(camera) ? Vector2.Scale(gamePoint, new Vector2(camera.pixelWidth / (float)Width, camera.pixelHeight / (float)Height)) : GameToScreenPoint(gamePoint, PixelArtUpscaleRig.Output?.DisplayRect ?? default);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, point, camera, out local);
    }

    // Modal prefab coordinates are logical pixels, including on older 960- or 1920-unit canvases.
    internal static void FitLogicalFrame(RectTransform root)
    {
        var parent = root.parent as RectTransform;
        var canvas = root.GetComponentInParent<Canvas>(true);
        if (!TryGamePointToCanvas(Vector2.zero, canvas, parent, out var lo) ||
            !TryGamePointToCanvas(new Vector2(Width, Height), canvas, parent, out var hi)) return;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.sizeDelta = new Vector2(Width, Height);
        root.localScale = new Vector3((hi.x - lo.x) / Width, (hi.y - lo.y) / Height, 1);
        root.localPosition = (lo + hi) * .5f;
    }

    public static bool TryWorldToGamePoint(Vector3 world, out Vector2 gamePoint)
    {
        gamePoint = default;
        var camera = WorldCamera;
        if (camera == null) return false;
        var projected = camera.WorldToScreenPoint(world);
        if (projected.z <= 0) return false;
        gamePoint = RendersToGameFrame(camera) ? ProjectToLogical(camera, world) : ScreenToGamePoint(projected);
        return true;
    }

    public static bool TryWorldToCanvasPoint(Vector3 world, Canvas canvas, RectTransform parent, out Vector2 local)
    {
        local = default;
        return TryWorldToGamePoint(world, out var point) && TryGamePointToCanvas(point, canvas, parent, out local);
    }

    public static bool TryPointerToWorld(Vector2 displayPoint, out Vector2 world)
    {
        world = default;
        var camera = WorldCamera;
        if (camera == null) return false;
        var point = RendersToGameFrame(camera) ? DisplayToTarget(displayPoint) : displayPoint;
        var ray = camera.ScreenPointToRay(point);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        if (!plane.Raycast(ray, out float distance)) return false;
        world = ray.GetPoint(distance);
        return true;
    }
}
