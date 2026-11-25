using UnityEngine;
using UnityEngine.UI;

public abstract class RewardPixelSurface : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Draw(mesh, rectTransform.rect.width, rectTransform.rect.height);
    }

    protected abstract void Draw(VertexHelper mesh, float width, float height);

    protected void Bar(VertexHelper mesh, float x, float y, float width, float height, Color32 ink)
    {
        if (width <= 0 || height <= 0) return;
        var origin = rectTransform.rect.min;
        var a = origin + new Vector2(x, y);
        var b = a + new Vector2(width, height);
        int start = mesh.currentVertCount;
        mesh.AddVert(a, ink, Vector2.zero);
        mesh.AddVert(new Vector2(a.x, b.y), ink, Vector2.zero);
        mesh.AddVert(b, ink, Vector2.zero);
        mesh.AddVert(new Vector2(b.x, a.y), ink, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    protected void Plate(VertexHelper mesh, float x, float y, float w, float h, int bevel, Color32 ink)
    {
        Bar(mesh, x, y + bevel, w, h - bevel * 2, ink);
        for (int inset = 1; inset <= bevel; inset++)
        {
            Bar(mesh, x + inset, y + bevel - inset, w - inset * 2, 1, ink);
            Bar(mesh, x + inset, y + h - bevel + inset - 1, w - inset * 2, 1, ink);
        }
    }

    protected void Star(VertexHelper mesh, float x, float y, int radius, Color32 ink)
    {
        Bar(mesh, x - radius, y, radius * 2 + 1, 1, ink);
        Bar(mesh, x, y - radius, 1, radius * 2 + 1, ink);
    }
}
