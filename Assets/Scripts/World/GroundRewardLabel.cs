using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>바닥에서 포커스된 보상의 이름표를 640×360 화면 안에 배치한다.</summary>
[DisallowMultipleComponent]
public sealed class GroundRewardLabel : BaseMeshEffect
{
    private const int MaximumWidth = 224;
    private const int SidePadding = 4;
    private const int ScreenMargin = 2;
    private RectTransform panel;
    private TMP_Text label;
    private Vector3 localAnchor;
    private SpriteRenderer aboveSprite;
    private Vector2Int pixels;
    private float previousScale, previousFontSize;
    private string previousName;

    public void Bind(Tooltip tooltip, SpriteRenderer above = null)
    {
        panel = (RectTransform)transform;
        label = tooltip.name;
        aboveSprite = above;
        var background = (Image)graphic;
        background.sprite = null;
        background.type = Image.Type.Simple;
        background.color = new Color32(31, 27, 36, 184);
        background.raycastTarget = false;
        localAnchor = panel.localPosition;
        if (TryGetComponent<LayoutGroup>(out var layout)) layout.enabled = false;
        if (tooltip.price != null) tooltip.price.gameObject.SetActive(false);
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one * .5f;
        var textRect = label.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.Center;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Canvas.preWillRenderCanvases += Place;
    }

    protected override void OnDisable()
    {
        Canvas.preWillRenderCanvases -= Place;
        base.OnDisable();
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || pixels.x <= 0 || pixels.y <= 0) return;
        Rect rect = panel.rect;
        float dx = rect.width / pixels.x;
        float dy = rect.height / pixels.y;
        Color32 tint = graphic.color;
        mesh.Clear();
        // Each outer row stops one logical pixel before either side, even when the camera zooms.
        Strip(rect.xMin + dx, rect.xMax - dx, rect.yMin, rect.yMin + dy);
        Strip(rect.xMin, rect.xMax, rect.yMin + dy, rect.yMax - dy);
        Strip(rect.xMin + dx, rect.xMax - dx, rect.yMax - dy, rect.yMax);

        void Strip(float left, float right, float bottom, float top)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(left, bottom), tint, Vector2.zero);
            mesh.AddVert(new Vector3(left, top), tint, Vector2.zero);
            mesh.AddVert(new Vector3(right, top), tint, Vector2.zero);
            mesh.AddVert(new Vector3(right, bottom), tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start + 2, start + 3, start);
        }
    }

    private void Place()
    {
        var camera = PixelViewport.WorldCamera;
        if (panel == null || label == null || camera == null || !camera.orthographic) return;
        float scale = Mathf.Abs(panel.lossyScale.y) * PixelViewport.Height / (2f * camera.orthographicSize);
        if (scale < .0001f) return;
        if (scale != previousScale || label.text != previousName || previousFontSize != label.fontSize)
        {
            float width = label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x * scale;
            pixels.x = Mathf.Min(Mathf.Max(1, Mathf.CeilToInt(width)) + 2 * SidePadding, MaximumWidth);
            float textWidth = (pixels.x - 2 * SidePadding) / scale;
            float height = label.GetPreferredValues(label.text, textWidth, Mathf.Infinity).y * scale;
            pixels.y = Mathf.Max(12, Mathf.CeilToInt(height) + 4);
            panel.sizeDelta = (Vector2)pixels / scale;
            label.rectTransform.offsetMin = new Vector2(SidePadding / scale, 0);
            label.rectTransform.offsetMax = new Vector2(-SidePadding / scale, 0);
            previousScale = scale;
            previousName = label.text;
            previousFontSize = label.fontSize;
        }
        var point = camera.WorldToViewportPoint(panel.parent.TransformPoint(localAnchor));
        if (aboveSprite != null)
        {
            var bounds = aboveSprite.bounds;
            point = camera.WorldToViewportPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            point.y += (pixels.y * .5f + ScreenMargin) / PixelViewport.Height;
        }
        float x = point.x * PixelViewport.Width - pixels.x * .5f;
        float y = point.y * PixelViewport.Height - pixels.y * .5f;
        x = Mathf.Clamp(x, ScreenMargin, PixelViewport.Width - pixels.x - ScreenMargin);
        y = Mathf.Clamp(y, ScreenMargin, PixelViewport.Height - pixels.y - ScreenMargin);
        point.x = (x + pixels.x * .5f) / PixelViewport.Width;
        point.y = (y + pixels.y * .5f) / PixelViewport.Height;
        var position = camera.ViewportToWorldPoint(point);
        if ((position - panel.position).sqrMagnitude > 1e-12f) panel.position = position;
    }
}
