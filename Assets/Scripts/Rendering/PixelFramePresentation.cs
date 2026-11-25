using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Owns the logical frame, its display canvas and the empty camera that clears the viewport.</summary>
internal sealed class PixelFramePresentation : IDisposable
{
    private readonly RectTransform picture;
    private readonly Material material;
    private readonly FrameEdgeMist edgeMist;
    private Vector2Int screenSize;
    private RectInt displayRect;

    public RenderTexture Frame { get; }
    public Canvas Canvas { get; }
    public RectInt DisplayRect
    {
        get
        {
            Refresh();
            return displayRect;
        }
    }

    public PixelFramePresentation(Transform root, Shader upscaleShader)
    {
        edgeMist = FrameEdgeMist.Create();
        Frame = new RenderTexture(PixelViewport.Width, PixelViewport.Height, 24, RenderTextureFormat.Default)
        {
            name = "Game viewport",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1
        };
        Frame.Create();

        Canvas = root.gameObject.AddComponent<Canvas>();
        Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.sortingOrder = short.MaxValue;
        Canvas.pixelPerfect = false;

        var display = new GameObject("Presentation Camera").AddComponent<Camera>();
        display.transform.SetParent(root, false);
        display.cullingMask = 0;
        display.clearFlags = CameraClearFlags.SolidColor;
        display.backgroundColor = Color.black;
        display.depth = 100;
        display.allowHDR = false;
        display.allowMSAA = false;
        // UniversalRP's second renderer clears the backbuffer and draws overlay UI only.
        var displayData = display.GetUniversalAdditionalCameraData();
        displayData.SetRenderer(1);
        displayData.renderShadows = false;

        var image = new GameObject("Game Frame", typeof(RectTransform)).AddComponent<RawImage>();
        image.transform.SetParent(root, false);
        image.raycastTarget = false;
        image.texture = Frame;
        if (upscaleShader != null)
        {
            material = new Material(upscaleShader);
            material.SetTexture("_Palette", Resources.Load<Texture2D>("WorldAtmosphere/necrohanderPalette"));
            image.material = material;
        }
        picture = image.rectTransform;
        picture.anchorMin = picture.anchorMax = picture.pivot = Vector2.zero;
        Refresh();
    }

    public void Refresh()
    {
        var size = new Vector2Int(Screen.width, Screen.height);
        if (screenSize == size || size.x <= 0 || size.y <= 0) return;
        screenSize = size;
        displayRect = PixelViewport.CalculateDisplayRect(size.x, size.y);
        if (Frame.width != displayRect.width || Frame.height != displayRect.height)
        {
            var owner = PixelArtUpscaleRig.RenderCamera;
            bool bound = owner && owner.targetTexture == Frame;
            if (bound) owner.targetTexture = null;
            Frame.Release();
            Frame.width = displayRect.width;
            Frame.height = displayRect.height;
            Frame.Create();
            if (bound) owner.targetTexture = Frame;
        }
        picture.anchoredPosition = displayRect.position;
        picture.sizeDelta = displayRect.size;
    }

    public void Dispose()
    {
        if (edgeMist != null) Object.Destroy(edgeMist.transform.parent.gameObject);
        Frame.Release();
        Object.Destroy(Frame);
        if (material != null) Object.Destroy(material);
    }
}
