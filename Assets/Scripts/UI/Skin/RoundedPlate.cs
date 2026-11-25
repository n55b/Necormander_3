using UnityEngine;
using UnityEngine.UI;

// Redraw the plate inside its hit region. Icons retain their original size throughout the press.
[RequireComponent(typeof(Image))]
public sealed class RoundedPlate : BaseMeshEffect
{
    private bool flat;
    public static void ApplyFlat(Image image)
    {
        if (!image || image.sprite || image.type != Image.Type.Simple) return;
        if (!image.TryGetComponent<RoundedPlate>(out var plate)) plate = image.gameObject.AddComponent<RoundedPlate>();
        plate.flat = true;
        image.SetVerticesDirty();
    }

    public Color32 rim = new(119, 132, 134, 255);
    private float focus, depth;

    public void SetInteraction(float emphasis, float inset)
    {
        if (focus == emphasis && depth == inset) return;
        focus = emphasis;
        depth = inset;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive()) return;
        mesh.Clear();
        var rect = graphic.rectTransform.rect;
        float pixel = 1f / Mathf.Max(.001f, PixelViewport.CanvasPixelsPerUnit(graphic.canvas, graphic.rectTransform));
        float inset = pixel * depth;
        var r = Rect.MinMaxRect(rect.xMin + inset,rect.yMin + inset,rect.xMax - inset,rect.yMax - inset);
        float radius = Mathf.Min(pixel * 4, Mathf.Min(r.width,r.height)*.28f);
        Color32 edge = Color32.Lerp(rim,new Color32(255,237,196,255),focus);
        Draw(mesh, r, radius, flat ? 0 : pixel * (1 + focus * .65f), graphic.color, edge);
    }

    internal static void Draw(VertexHelper mesh, Rect box, float radius, float border, Color32 face, Color32 edge)
    {
        if (box.width <= 0 || box.height <= 0) return;
        radius = Mathf.Clamp(radius, 0, Mathf.Min(box.width,box.height) * .5f);
        border = Mathf.Clamp(border, 0, Mathf.Min(box.width,box.height) * .5f);
        int outer = Contour(box, radius, border > 0 ? edge : face);
        if (border > 0)
        {
            var inner = Rect.MinMaxRect(box.xMin+border,box.yMin+border,box.xMax-border,box.yMax-border);
            int inside = Contour(inner, Mathf.Max(0, radius-border), edge);
            for (int n=0;n<16;n++)
            {
                int next = (n+1)%16;
                mesh.AddTriangle(outer+n, inside+next, outer+next);
                mesh.AddTriangle(outer+n, inside+n, inside+next);
            }
            if (face.a == 0 || inner.width <= 0 || inner.height <= 0) return;
            outer = Contour(inner, Mathf.Max(0, radius-border), face);
        }
        int center = mesh.currentVertCount;
        mesh.AddVert(box.center,face,Vector2.zero);
        for (int n=0;n<16;n++) mesh.AddTriangle(center,outer+(n+1)%16,outer+n);

        int Contour(Rect rect, float rad, Color32 color)
        {
            int start = mesh.currentVertCount;
            for (int quadrant=0;quadrant<4;quadrant++)
            {
                bool right = quadrant == 0 || quadrant == 3, upper = quadrant < 2;
                Vector2 pivot = new(right ? rect.xMax-rad : rect.xMin+rad, upper ? rect.yMax-rad : rect.yMin+rad);
                for (int step=0;step<=3;step++)
                {
                    float angle = (quadrant+step/3f)*Mathf.PI*.5f;
                    mesh.AddVert(pivot+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*rad,color,Vector2.zero);
                }
            }
            return start;
        }
    }
}
