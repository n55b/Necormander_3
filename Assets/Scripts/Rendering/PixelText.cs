using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Uses authored bitmap strikes at their native size or an integer multiple in the game frame.</summary>
[DisallowMultipleComponent]
public sealed class PixelText : MonoBehaviour
{
    private static readonly int[] Sizes = { 8, 10, 12, 15, 20, 24, 30, 36, 45, 48 };
    private static readonly HashSet<PixelText> Active = new();
    private static readonly Dictionary<string, TMP_FontAsset> Faces = new();

    [SerializeField, Min(0)] private int logicalSize;
    [SerializeField] private bool emphasis;
    [SerializeField] private bool fit;

    private TMP_Text text;
    private string previousText;
    private string checkedMarkup;
    private Vector3 previousScale;
    private TMP_FontAsset appliedFace;
    private float appliedSize;
    private float previousLens;
    private float authoredSize;
    private float preferredPixels;
    private bool pending = true;
    private System.Func<TMP_FontAsset,TMP_FontAsset> resolveFace;

    internal void UseFaceResolver(System.Func<TMP_FontAsset,TMP_FontAsset> resolver)
    {
        resolveFace=resolver;
        pending=true;
        if (text!=null && isActiveAndEnabled) PrepareForRender();
    }

    private void OnEnable()
    {
        if (text == null && TryGetComponent(out text))
            authoredSize = text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
        if (text == null) return;

        fit |= text.enableAutoSizing;
        pending = true;
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    private void OnRectTransformDimensionsChange() => pending = true;
    private void OnCanvasHierarchyChanged() => pending = true;
    private void OnTransformParentChanged() => pending = true;

    public static PixelText Configure(TMP_Text label, int pixels, bool bold = false)
    {
        if (!label.TryGetComponent<PixelText>(out var pixel)) pixel = label.gameObject.AddComponent<PixelText>();
        pixel.emphasis = bold;
        pixel.SetLogicalSize(pixels);
        if (!Application.isPlaying)
        {
            var face = ChooseFace(pixels > 0 ? Sizes[NearestSize(pixels)] : 12, bold);
            if (face != null) SetFace(label, face);
        }
        return pixel;
    }

    public void SetLogicalSize(int pixels)
    {
        if (logicalSize == pixels) return;
        logicalSize = pixels;
        pending = true;
    }

    internal static void Prepare()
    {
        foreach (var label in Active)
            label.PrepareForRender();
    }

    internal void PrepareForRender()
    {
        if (!text.isActiveAndEnabled) return;
        string raw = text.text;
        if (!ReferenceEquals(checkedMarkup, raw))
        {
            if (Application.isPlaying && text.richText && TextEffectParser.HasMarkup(raw) &&
                text.GetComponentInParent<TMP_InputField>() == null)
            {
                if (!text.TryGetComponent<TMPTextEffectPlayer>(out var effect))
                    effect = text.gameObject.AddComponent<TMPTextEffectPlayer>();
                effect.AcceptMarkup(raw);
            }
            checkedMarkup = text.text;
        }
        var scale = transform.lossyScale;
        var canvas = text.canvas;
        bool worldText = canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace;
        float lens = worldText && PixelViewport.WorldCamera != null ? PixelViewport.WorldCamera.orthographicSize : 0;
        if (!pending && lens == previousLens && scale == previousScale && previousText == text.text &&
            appliedFace != null && text.font == appliedFace && text.fontSharedMaterial == appliedFace.material &&
            Mathf.Abs(text.fontSize - appliedSize) <= .001f && !text.enableAutoSizing) return;
        if (!TryGrid(text, out var grid)) return;

        float units = grid.VerticalScale * (text.isOrthographic ? 1f : .1f);
        if (units <= .0001f) return;
        if (preferredPixels <= 0f) preferredPixels = authoredSize * units;
        int index = NearestSize(logicalSize > 0 ? logicalSize : preferredPixels);
        text.enableAutoSizing = false;
        text.fontStyle &= ~FontStyles.Bold;
        while (true)
        {
            int pixels = Sizes[index];
            var face = ChooseFace(pixels, emphasis);
            if (face == null) return;

            if (resolveFace!=null) face=resolveFace(face);
            SetFace(text, face);

            float size = pixels / (units * face.faceInfo.scale);
            if (Mathf.Abs(text.fontSize - size) > .001f) text.fontSize = size;
            appliedFace = face;
            appliedSize = size;
            if (!fit || index == 0 || FitsCurrentRect()) break;
            index--;
        }

        previousLens = lens;
        previousScale = scale;
        previousText = text.text;
        pending = false;
    }

    private static void SetFace(TMP_Text label, TMP_FontAsset face)
    {
        if (label.font != face) label.font = face;
        if (label.fontSharedMaterial != face.material) label.fontSharedMaterial = face.material;
    }

    private bool FitsCurrentRect()
    {
        Vector2 bounds = text.rectTransform.rect.size;
        if (bounds.x <= 0f || bounds.y <= 0f) return true;
        var margin = text.margin;
        var available = new Vector2(bounds.x - margin.x - margin.z, bounds.y - margin.y - margin.w);
        if (available.x <= 0f || available.y <= 0f) return true;
        var preferred = text.GetPreferredValues(text.text, available.x, available.y);
        return preferred.x <= bounds.x + .5f && preferred.y <= bounds.y + .5f;
    }

    private static int NearestSize(float request)
    {
        for (int index = 0; index < Sizes.Length - 1; index++)
            if (request <= (Sizes[index] + Sizes[index + 1]) * .5f) return index;
        return Sizes.Length - 1;
    }

    private static TMP_FontAsset ChooseFace(int pixels, bool bold)
    {
        string face = pixels switch
        {
            8 => "Micro",
            10 or 20 => "Small",
            15 or 30 or 45 => "Large",
            _ => bold ? "Emphasis" : "Body"
        };
        if (!Faces.TryGetValue(face, out var asset) || asset == null)
        {
            asset = Resources.Load<TMP_FontAsset>("PixelFonts/" + face);
            if (asset != null) Faces[face] = asset;
        }
        return asset;
    }

    internal static bool TryGrid(TMP_Text label, out GlyphGrid grid)
    {
        grid = default;
        if (PixelArtUpscaleRig.RenderCamera == null) return false;
        Transform origin = label.transform;
        if (label is TextMeshProUGUI ui)
        {
            var canvas = ui.canvas;
            if (canvas == null) return false;
            var root = canvas.rootCanvas;
            if (root.renderMode != RenderMode.WorldSpace)
            {
                // Screen labels belong to the logical canvas. The camera can move between layout and drawing.
                var rect = ((RectTransform)root.transform).rect;
                if (rect.width <= 0 || rect.height <= 0) return false;
                var local = Matrix4x4.identity;
                for (var node = origin; node != root.transform; node = node.parent)
                    local = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale) * local;
                var ratio = new Vector2(PixelViewport.Width / rect.width, PixelViewport.Height / rect.height);
                grid = new GlyphGrid(Vector2.Scale((Vector2)local.MultiplyPoint3x4(Vector3.zero) - rect.min, ratio),
                    Vector2.Scale(local.MultiplyVector(Vector3.right), ratio),
                    Vector2.Scale(local.MultiplyVector(Vector3.up), ratio));
                return grid.IsValid;
            }
            if (PixelViewport.CanvasCamera(canvas) == null) return false;
            var pixel = PixelViewport.CanvasToGamePoint(canvas, origin.position);
            var horizontal = PixelViewport.CanvasToGamePoint(canvas, origin.TransformPoint(Vector3.right));
            var vertical = PixelViewport.CanvasToGamePoint(canvas, origin.TransformPoint(Vector3.up));
            grid = new GlyphGrid(pixel, horizontal - pixel, vertical - pixel);
        }
        else
        {
            if (!PixelViewport.TryWorldToGamePoint(origin.position, out var pixel)
                || !PixelViewport.TryWorldToGamePoint(origin.TransformPoint(Vector3.right), out var horizontal)
                || !PixelViewport.TryWorldToGamePoint(origin.TransformPoint(Vector3.up), out var vertical)) return false;
            grid = new GlyphGrid(pixel, horizontal - pixel, vertical - pixel);
        }
        return grid.IsValid;
    }

    internal readonly struct GlyphGrid
    {
        private readonly Vector2 origin;
        private readonly Vector2 horizontal;
        private readonly Vector2 vertical;
        private readonly float determinant;

        internal GlyphGrid(Vector2 origin, Vector2 horizontal, Vector2 vertical)
        {
            this.origin = origin;
            this.horizontal = horizontal;
            this.vertical = vertical;
            determinant = horizontal.x * vertical.y - horizontal.y * vertical.x;
        }

        internal float VerticalScale => vertical.magnitude;
        internal bool IsValid => Mathf.Abs(determinant) > .000001f;

        internal Vector3 LocalOffset(Vector2 pixel)
            => new((vertical.y * pixel.x - vertical.x * pixel.y) / determinant,
                (horizontal.x * pixel.y - horizontal.y * pixel.x) / determinant, 0f);
    }
}
