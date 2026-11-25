using UnityEngine;
using UnityEngine.UI;

public sealed class ControlFocusRing : MaskableGraphic
{
    public static ControlFocusRing Create(RectTransform parent, string name)
    {
        var host = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement), typeof(ControlFocusRing));
        host.layer = parent.gameObject.layer;
        host.GetComponent<LayoutElement>().ignoreLayout = true;
        var ring = host.GetComponent<ControlFocusRing>();
        ring.color = new Color32(192,173,158,255);
        ring.raycastTarget = false;
        ring.rectTransform.SetParent(parent, false);
        ring.rectTransform.anchorMin = Vector2.zero;
        ring.rectTransform.anchorMax = Vector2.one;
        ring.rectTransform.offsetMin = ring.rectTransform.offsetMax = Vector2.zero;
        return ring;
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = rectTransform.rect;
        float pixel = 1f / Mathf.Max(.001f, PixelViewport.CanvasPixelsPerUnit(canvas, rectTransform));
        if (r.width < 4 * pixel || r.height < 4 * pixel) return;
        r = Rect.MinMaxRect(r.xMin + pixel, r.yMin + pixel, r.xMax - pixel, r.yMax - pixel);
        RoundedPlate.Draw(mesh, r, 2 * pixel, pixel, Color.clear, color);
    }
}
