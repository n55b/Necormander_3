using UnityEngine;
using UnityEngine.UI;

// Each quad samples one texel of the captured pose. The GPU moves the same mesh throughout intake.
internal sealed class FloorPortalDust : RawImage
{
    private int firstRow, endRow;

    internal static void Attach(FloorPortalActor actor, RenderTexture pose)
    {
        int rows = Mathf.Max(1, 8192 / pose.width);
        for (int first = 0; first < pose.height; first += rows)
        {
            var node = new GameObject("Pose grains", typeof(RectTransform), typeof(CanvasRenderer));
            node.transform.SetParent(actor.transform.parent, false);
            var dust = node.AddComponent<FloorPortalDust>();
            dust.firstRow = first;
            dust.endRow = Mathf.Min(first + rows, pose.height);
            dust.raycastTarget = false;
            dust.texture = pose;
            dust.material = actor.material;
            dust.rectTransform.anchorMin = dust.rectTransform.pivot = Vector2.zero;
            dust.rectTransform.anchorMax = Vector2.one;
            dust.rectTransform.offsetMin = dust.rectTransform.offsetMax = Vector2.zero;
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (texture == null) return;
        float dx = 1f / texture.width, dy = 1f / texture.height;
        // Red identifies grains in the shared silhouette shader. Fractional UVs encode the corner.
        for (int y = firstRow; y < endRow; y++)
        for (int x = 0; x < texture.width; x++)
        {
            int n = mesh.currentVertCount;
            mesh.AddVert(new Vector2(x,y), Color.red, new Vector2((x+.25f)*dx,(y+.25f)*dy));
            mesh.AddVert(new Vector2(x,y+1), Color.red, new Vector2((x+.25f)*dx,(y+.75f)*dy));
            mesh.AddVert(new Vector2(x+1,y+1), Color.red, new Vector2((x+.75f)*dx,(y+.75f)*dy));
            mesh.AddVert(new Vector2(x+1,y), Color.red, new Vector2((x+.75f)*dx,(y+.25f)*dy));
            mesh.AddTriangle(n,n+1,n+2);
            mesh.AddTriangle(n,n+2,n+3);
        }
    }
}
