using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class UiResolutionCheck
{
    private const string Request = "Verification.UiResolution";
    private const string Preset = "UI resolution check";
    private static readonly Vector2Int[] Sizes = {
        new(640,360), new(1280,720), new(1366,768), new(1920,1080),
        new(1280,960), new(2560,1080), new(512,384)
    };
    public static bool Enabled => SessionState.GetBool(Request,false);
    public static void Run() { SessionState.SetBool(Request,true); PresentationRegressionCheck.Run(); }
    public static void RunInventory() { SessionState.SetBool(Request,true); InventoryUICheck.RunRuntimeBatch(); }
    public static void RunWorkshop() { SessionState.SetBool(Request,true); WorkshopPlayModeCheck.Run(); }

    private static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var lo = RectTransformUtility.WorldToScreenPoint(camera,corners[0]);
        var hi = RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
        return Rect.MinMaxRect(lo.x,lo.y,hi.x,hi.y);
    }

    private static IEnumerator Resize(Vector2Int size)
    {
        PlayModeWindow.SetCustomRenderingResolution((uint)size.x,(uint)size.y,Preset);
        float end = Time.realtimeSinceStartup + 5;
        while (Screen.width != size.x || Screen.height != size.y)
        {
            if (Time.realtimeSinceStartup > end) throw new InvalidOperationException($"Game View {Screen.width}×{Screen.height}, requested {size}");
            yield return null;
        }
        for (int i = 0; i < 4; i++) yield return null;
        Canvas.ForceUpdateCanvases();
    }

    // Batch mode does not display an OS window. Draw the actual presenter canvas into a physical-size target.
    private static void CaptureOutput(RawImage output, Vector2Int size, string name)
    {
        var canvas = output.canvas.rootCanvas;
        var oldCamera = canvas.worldCamera;
        float distance = canvas.planeDistance;
        var capture = RenderTexture.GetTemporary(size.x,size.y,24,RenderTextureFormat.ARGB32);
        var host = new GameObject("UI output capture");
        var camera = host.AddComponent<Camera>();
        try
        {
            camera.enabled = false;
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 2;
            camera.allowHDR = camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << output.gameObject.layer;
            camera.targetTexture = capture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var physical = PresentationRegressionCheck.ReadFrame(capture);
            Texture2D logical = null;
            try
            {
                logical = PresentationRegressionCheck.ReadFrame((RenderTexture)output.texture);
                PresentationRegressionCheck.SaveFrame(physical, name);
                PresentationRegressionCheck.SaveFrame(logical, name + "-logical");
                VerifyPixels(logical, physical, PixelViewport.CalculateDisplayRect(size.x, size.y));
            }
            finally
            {
                UnityEngine.Object.Destroy(physical);
                if (logical != null) UnityEngine.Object.Destroy(logical);
            }
        }
        finally
        {
            canvas.worldCamera = oldCamera;
            canvas.planeDistance = distance;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(host);
            RenderTexture.ReleaseTemporary(capture);
            Canvas.ForceUpdateCanvases();
        }
    }

    private static bool SameRgb(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

    private static void VerifyPixels(Texture2D logical, Texture2D physical, RectInt frame)
    {
        var source = logical.GetRawTextureData<Color32>();
        var output = physical.GetRawTextureData<Color32>();
        int width = logical.width, height = logical.height;
        int dx = 2 * frame.width, dy = 2 * frame.height;
        for (int y = 0; y < physical.height; y++)
        {
            int ny = (2 * (y - frame.y) + 1) * height;
            int sy = ny / dy;
            for (int x = 0; x < physical.width; x++)
            {
                Color32 drawn = output[y * physical.width + x];
                if (x < frame.xMin || x >= frame.xMax || y < frame.yMin || y >= frame.yMax)
                {
                    if (drawn.r == 0 && drawn.g == 0 && drawn.b == 0) continue;
                    throw new InvalidOperationException($"Nonblack pixel in letterbox at ({x},{y})");
                }
                int nx = (2 * (x - frame.x) + 1) * width;
                int sx = nx / dx;
                int index = sy * width + sx;
                if (SameRgb(drawn, source[index])) continue;
                // A physical pixel exactly between texel centers may sample either neighbor.
                bool onX = nx % dx == 0, onY = ny % dy == 0;
                if (onX && SameRgb(drawn, source[index - 1])) continue;
                if (onY && SameRgb(drawn, source[index - width])) continue;
                if (onX && onY && SameRgb(drawn, source[index - width - 1])) continue;
                throw new InvalidOperationException($"Point sample mismatch at ({x},{y}) in {physical.width}×{physical.height}, source ({sx},{sy})");
            }
        }
    }

    public static IEnumerator Verify(string screen, RectTransform panel, Selectable control = null)
    {
        if (!Enabled) yield break;
        if (panel == null) throw new ArgumentNullException(nameof(panel));
        var previous = new Vector2Int(Screen.width, Screen.height);
        var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
        var camera = PixelViewport.CanvasCamera(canvas);
        var output = UnityEngine.Object.FindObjectsByType<RawImage>(FindObjectsSortMode.None)
            .Single(i => i.texture == camera.targetTexture && i.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay);
        Rect? reference = null;
        foreach (var size in Sizes)
        {
            var resize = Resize(size);
            while (resize.MoveNext()) yield return resize.Current;
            var native = Bounds(panel,camera);
            var logicalScale = new Vector2(640f/camera.pixelWidth,360f/camera.pixelHeight);
            var bounds = new Rect(Vector2.Scale(native.position,logicalScale),Vector2.Scale(native.size,logicalScale));
            var display = PixelViewport.CalculateDisplayRect(size.x,size.y);
            var actual = Bounds(output.rectTransform,null);
            void Check(bool ok,string reason)
            {
                if (!ok) throw new InvalidOperationException($"[UI resolution] {screen} at {size}: {reason}; panel {bounds}, output {actual}");
            }
            Check(camera.targetTexture.width == display.width && camera.targetTexture.height == display.height, "native raster size");
            Check(display.xMin >= 0 && display.yMin >= 0 && display.xMax <= size.x && display.yMax <= size.y,
                "presentation exceeds physical screen");
            Check(Vector2.Distance(actual.min,display.min) < 1 && Vector2.Distance(actual.max,display.max) < 1,
                "rendered frame and pointer viewport disagree");
            Check(bounds.xMin >= -1 && bounds.yMin >= -1 && bounds.xMax <= 641 && bounds.yMax <= 361, "content outside logical frame");
            if (screen == "Options" || screen == "Shop")
            {
                bool OnPixel(Vector2 point) => Vector2.Distance(point, Vector2Int.RoundToInt(point)) < .01f;
                Check(OnPixel(bounds.min) && OnPixel(bounds.max), "window edge falls between logical pixels");
            }
            if (reference.HasValue)
                Check(Vector2.Distance(reference.Value.min,bounds.min)<2 && Vector2.Distance(reference.Value.max,bounds.max)<2,
                    $"logical layout moved from {reference.Value}");
            reference ??= bounds;
            foreach (var label in panel.GetComponentsInChildren<TextMeshProUGUI>())
                if (label.enabled && !string.IsNullOrWhiteSpace(label.text)) PresentationRegressionCheck.VerifyPixelLabel(label);
            if (control != null)
            {
                var button = (RectTransform)control.transform;
                var center = Vector2.Scale(Bounds(button,camera).center,logicalScale);
                var physical = actual.min + Vector2.Scale(center,new Vector2(actual.width / 640f,actual.height / 360f));
                var point = PixelViewport.PointerForCanvas(canvas,physical);
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
                if (hits.Count == 0 || !hits[0].gameObject.transform.IsChildOf(button))
                    Check(false, $"expected {control.name} at {physical}, got [{string.Join(", ", hits.Select(hit => hit.gameObject.name))}] on {canvas.name}, camera {camera.name}, point {point}");
            }
            if (screen == "Title" || screen == "Companions")
            {
                CaptureOutput(output,size,$"UiResolution/{screen}-{size.x}x{size.y}");
            }
            Debug.Log($"[UI resolution] PASS {screen} {size.x}x{size.y} logical={bounds}, display={display}");
        }
        var restore = Resize(previous);
        while (restore.MoveNext()) yield return restore.Current;
    }
}
