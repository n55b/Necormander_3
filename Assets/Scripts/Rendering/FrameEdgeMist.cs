using UnityEngine;
using UnityEngine.UI;

// Four non-overlapping strips in the logical frame. The center incurs no fragment work.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FrameEdgeMist : Graphic
{
    private static readonly int Mist = Shader.PropertyToID("_EdgeMist");
    private Material veil;
    private int width = -1;

    internal static FrameEdgeMist Create()
    {
        var root = new GameObject("Logical frame edges", typeof(RectTransform), typeof(Canvas));
        DontDestroyOnLoad(root);
        root.layer = LayerMask.NameToLayer("UI");
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.planeDistance = .5f;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = short.MaxValue-1;
        var go = new GameObject("Edge mist",typeof(RectTransform));
        go.layer = root.layer;
        go.transform.SetParent(root.transform,false);
        var graphic = go.AddComponent<FrameEdgeMist>();
        graphic.raycastTarget = false;
        graphic.rectTransform.anchorMin = Vector2.zero;
        graphic.rectTransform.anchorMax = Vector2.one;
        graphic.rectTransform.offsetMin = graphic.rectTransform.offsetMax = Vector2.zero;
        graphic.veil = new Material(Resources.Load<Shader>("WorldAtmosphere/EdgeMist"));
        graphic.veil.SetTexture("_Palette",Resources.Load<Texture2D>("WorldAtmosphere/necrohanderPalette"));
        graphic.material = graphic.veil;
        return graphic;
    }

    private void LateUpdate()
    {
        var settings = CameraSettingsSO.Instance;
        if (settings == null || veil == null) return;
        var preview = LightingPreview.Active;
        float opacity = preview?.borderOpacity ?? settings.edgeMistOpacity;
        int nextWidth = opacity > 0 ? Mathf.RoundToInt(preview?.borderWidth ?? settings.edgeMistWidth) : 0;
        if (nextWidth != width) { width = nextWidth; SetVerticesDirty(); }
        veil.SetVector(Mist,new Vector4(opacity,width,Time.unscaledTime * (preview?.borderSpeed ?? settings.edgeMistSpeed),0));
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (width <= 0) return;
        float x = (float)width/PixelViewport.Width, y = (float)width/PixelViewport.Height;
        Add(0,0,1,y); Add(0,1-y,1,1); Add(0,y,x,1-y); Add(1-x,y,1,1-y);
        void Add(float left, float bottom, float right, float top)
        {
            int i = mesh.currentVertCount;
            Vertex(left,bottom); Vertex(left,top); Vertex(right,top); Vertex(right,bottom);
            mesh.AddTriangle(i,i+1,i+2); mesh.AddTriangle(i,i+2,i+3);
        }
        void Vertex(float u,float v)
        {
            var uv = new Vector2(u,v);
            mesh.AddVert(Rect.NormalizedToPoint(rectTransform.rect,uv),Color.white,uv);
        }
    }

    protected override void OnDestroy()
    {
        if (veil != null) Destroy(veil);
        base.OnDestroy();
    }
}
