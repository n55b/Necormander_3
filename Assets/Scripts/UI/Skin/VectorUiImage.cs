using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>Projects vector art through the existing uGUI fill and nine-slice layout.</summary>
[AddComponentMenu("UI/Vector Image")]
public sealed class VectorUiImage : Image
{
    private const float Epsilon = 0.000001f;
    private List<UIVertex> layout;
    private static readonly VertexHelper InputMesh = new();
    private static readonly List<UIVertex> InputTriangles = new();
    private static readonly List<Component> MeshModifiers = new();
    // Identical world health bars often change to the same fill in a single attack.
    // uGUI copies SetMesh into each CanvasRenderer, just as it does with Graphic.workerMesh.
    private sealed class GaugeMesh
    {
        internal VectorUiCatalog.Entry shape;
        internal Sprite sprite;
        internal readonly List<UIVertex> corners = new(6);
        internal readonly Mesh mesh = new() { name = "World gauge geometry", hideFlags = HideFlags.HideAndDontSave };
    }
    private static readonly GaugeMesh[] gauges = new GaugeMesh[16];
    private static int nextGauge;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGauges()
    {
        for (int i = 0; i < gauges.Length; ++i)
        {
            if (gauges[i] != null) Destroy(gauges[i].mesh);
            gauges[i] = null;
        }
        nextGauge = 0;
    }
    private VectorUiCatalog.Entry drawnShape;
    private Sprite drawnSprite;
    private bool reuseInput;
    private bool hasDimensions;
    private Rect localRect, adjustedRect;
    private float imagePixelsPerUnit;
    private Vector2 lastPivot;
    private readonly Vector2[] region = new Vector2[4];
    private readonly Vector2[] clipA = new Vector2[8];
    private readonly Vector2[] clipB = new Vector2[8];

    private float focus;
    internal bool HasFrame => VectorUiCatalog.TryGet(overrideSprite, out var entry) && entry.slice > 0;
    internal void SetFocus(float amount)
    {
        if (amount == focus) return;
        focus = amount;
        drawnShape = null;
        SetVerticesDirty();
    }

    public bool UsesVector => VectorUiCatalog.TryGet(overrideSprite, out _);
    public override Texture mainTexture => UsesVector ? Texture2D.whiteTexture : base.mainTexture;

    protected override void OnEnable()
    {
        drawnShape = null;
        hasDimensions = false;
        base.OnEnable();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        // Screen-space camera canvases notify descendants when the lens moves. Image
        // layout depends on its local and pixel-adjusted rect, not the camera position.
        if (hasDimensions && isActiveAndEnabled && rectTransform.rect == localRect &&
            rectTransform.pivot == lastPivot && pixelsPerUnit == imagePixelsPerUnit &&
            GetPixelAdjustedRect() == adjustedRect) return;
        base.OnRectTransformDimensionsChange();
    }

    protected override void UpdateGeometry()
    {
        var source = overrideSprite;
        if (!Application.isPlaying || rectTransform.rect.width < 0 || rectTransform.rect.height < 0)
        {
            hasDimensions = false;
            drawnShape = null;
            base.UpdateGeometry();
            return;
        }

        // A moving camera can dirty a whole screen canvas while its rounded image corners,
        // tint and UVs stay identical. Mesh modifiers can depend on additional state.
        GetComponents(typeof(IMeshModifier), MeshModifiers);
        bool modified = MeshModifiers.Count != 0;
        MeshModifiers.Clear();
        hasDimensions = !modified;
        if (hasDimensions)
        {
            localRect = rectTransform.rect;
            adjustedRect = GetPixelAdjustedRect();
            lastPivot = rectTransform.pivot;
            imagePixelsPerUnit = pixelsPerUnit;
        }
        if (modified || !VectorUiCatalog.TryGet(source, out var shape))
        {
            drawnShape = null;
            base.UpdateGeometry();
            return;
        }

        BuildLayout(InputMesh, source, shape);
        InputTriangles.Clear();
        InputMesh.GetUIVertexStream(InputTriangles);
        if (drawnShape == shape && drawnSprite == source && SameLayout(layout)) return;
        layout ??= new List<UIVertex>();
        layout.Clear();
        layout.AddRange(InputTriangles);
        reuseInput = true;
        try
        {
            if (type == Type.Filled && fillMethod == FillMethod.Horizontal && InputTriangles.Count == 6 &&
                canvas != null && canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                SubmitGauge(shape, source);
            else base.UpdateGeometry();
        }
        finally { reuseInput = false; }
        drawnShape = shape;
        drawnSprite = source;
    }

    private void SubmitGauge(VectorUiCatalog.Entry shape, Sprite source)
    {
        foreach (var entry in gauges)
            if (entry != null && entry.shape == shape && entry.sprite == source && SameLayout(entry.corners))
            {
                canvasRenderer.SetMesh(entry.mesh);
                return;
            }
        var slot = gauges[nextGauge] ??= new GaugeMesh();
        nextGauge = (nextGauge + 1) % gauges.Length;
        OnPopulateMesh(InputMesh);
        InputMesh.FillMesh(slot.mesh);
        slot.corners.Clear();
        slot.corners.AddRange(InputTriangles);
        slot.shape = shape;
        slot.sprite = source;
        canvasRenderer.SetMesh(slot.mesh);
    }

    private static bool SameLayout(List<UIVertex> layout)
    {
        if (layout == null || layout.Count != InputTriangles.Count) return false;
        for (int i = 0; i < layout.Count; i++)
            if (!layout[i].position.Equals(InputTriangles[i].position) || !layout[i].uv0.Equals(InputTriangles[i].uv0) ||
                !layout[i].color.Equals(InputTriangles[i].color)) return false;
        return true;
    }

    public override void SetAllDirty()
    {
        base.SetAllDirty();
        // Sprites from one sheet can use different texture routes when swapped by a Selectable.
        SetMaterialDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        var source = overrideSprite;
        if (!VectorUiCatalog.TryGet(source, out var shape)) { base.OnPopulateMesh(mesh); return; }
        if (!reuseInput) BuildLayout(mesh, source, shape);
        if (!reuseInput)
        {
            layout ??= new List<UIVertex>();
            layout.Clear();
            mesh.GetUIVertexStream(layout);
        }
        mesh.Clear();
        if (layout.Count == 0) return;

        Vector4 uv = DataUtility.GetOuterUV(source);
        var uvMin = new Vector2(uv.x, uv.y);
        var uvSize = new Vector2(uv.z - uv.x, uv.w - uv.y);
        if (uvSize.x <= 0f || uvSize.y <= 0f) return;

        for (int i = 0; i < layout.Count;)
        {
            bool quad = i + 5 < layout.Count && SameVertex(layout[i], layout[i + 5]) &&
                SameVertex(layout[i + 2], layout[i + 3]);
            int count = quad ? 4 : 3;
            region[0] = Normalize(layout[i].uv0, uvMin, uvSize);
            region[1] = Normalize(layout[i + 1].uv0, uvMin, uvSize);
            region[2] = Normalize(layout[i + 2].uv0, uvMin, uvSize);
            if (quad) region[3] = Normalize(layout[i + 4].uv0, uvMin, uvSize);

            if (quad && Mathf.Abs(Cross(region[1] - region[0], region[2] - region[0])) < Epsilon)
                DrawRegion(mesh, shape, count, 2, 3, layout[i].position, layout[i + 2].position,
                    layout[i + 4].position, layout[i].color);
            else
                DrawRegion(mesh, shape, count, 1, 2, layout[i].position, layout[i + 1].position,
                    layout[i + 2].position, layout[i].color);
            i += quad ? 6 : 3;
        }
    }

    private void BuildLayout(VertexHelper mesh, Sprite source, VectorUiCatalog.Entry shape)
    {
        if (shape.slice <= 0 || (type != Type.Simple && type != Type.Sliced) || canvas == null ||
            canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
        {
            base.OnPopulateMesh(mesh);
            return;
        }
        var rect = rectTransform.rect;
        var uv = DataUtility.GetOuterUV(source);
        float scale = PixelViewport.CanvasPixelsPerUnit(canvas, rectTransform);
        float corner = Mathf.Min(shape.slice / Mathf.Max(.001f,scale), Mathf.Min(rect.width,rect.height)*.5f);
        Vector4 x = new(rect.xMin,rect.xMin+corner,rect.xMax-corner,rect.xMax);
        Vector4 y = new(rect.yMin,rect.yMin+corner,rect.yMax-corner,rect.yMax);
        float du = (uv.z-uv.x)*shape.slice/shape.artSize.x, dv = (uv.w-uv.y)*shape.slice/shape.artSize.y;
        Vector4 u = new(uv.x,uv.x+du,uv.z-du,uv.z), v = new(uv.y,uv.y+dv,uv.w-dv,uv.w);
        mesh.Clear();
        for(int j=0;j<3;j++) for(int i=0;i<3;i++)
        {
            if ((!fillCenter && i==1 && j==1) || x[i+1]<=x[i] || y[j+1]<=y[j]) continue;
            int k=mesh.currentVertCount;
            mesh.AddVert(new Vector2(x[i],y[j]),color,new Vector2(u[i],v[j]));
            mesh.AddVert(new Vector2(x[i],y[j+1]),color,new Vector2(u[i],v[j+1]));
            mesh.AddVert(new Vector2(x[i+1],y[j+1]),color,new Vector2(u[i+1],v[j+1]));
            mesh.AddVert(new Vector2(x[i+1],y[j]),color,new Vector2(u[i+1],v[j]));
            mesh.AddTriangle(k,k+1,k+2); mesh.AddTriangle(k+2,k+3,k);
        }
    }

    private void DrawRegion(VertexHelper mesh, VectorUiCatalog.Entry shape, int regionCount, int second, int third,
        Vector3 origin, Vector3 positionU, Vector3 positionV, Color32 color)
    {
        Vector2 basisU = region[second] - region[0];
        Vector2 basisV = region[third] - region[0];
        float determinant = Cross(basisU, basisV);
        if (Mathf.Abs(determinant) < Epsilon) return;

        var min = region[0];
        var max = min;
        for (int i = 1; i < regionCount; i++)
        {
            min = Vector2.Min(min, region[i]);
            max = Vector2.Max(max, region[i]);
        }

        float winding = Mathf.Sign(determinant);
        positionU -= origin;
        positionV -= origin;
        bool fullQuad = regionCount == 4 && region[0] == Vector2.zero && region[1] == Vector2.up &&
                        region[2] == Vector2.one && region[3] == Vector2.right;
        bool rectangular = regionCount == 4 && region[0].x == region[1].x && region[1].y == region[2].y &&
                           region[2].x == region[3].x && region[3].y == region[0].y;
        foreach (var layer in shape.layers)
        {
            var ink = layer == shape.layers[0] && focus > 0 ? Color32.Lerp(layer.color,new Color32(255,237,196,layer.color.a),focus) : layer.color;
            var tint = Multiply(ink, color);
            var points = layer.vertices;
            var triangles = layer.triangles;
            if (fullQuad && layer.FitsSprite)
            {
                int start = mesh.currentVertCount;
                var right = positionV - positionU;
                for (int i = 0; i < points.Length; ++i)
                    mesh.AddVert(origin + right * points[i].x + positionU * points[i].y, tint, Vector2.zero);
                for (int i = 0; i < triangles.Length; i += 3)
                    mesh.AddTriangle(start + triangles[i], start + triangles[i + 1], start + triangles[i + 2]);
                continue;
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 a = points[triangles[i]];
                Vector2 b = points[triangles[i + 1]];
                Vector2 c = points[triangles[i + 2]];
                if (Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > max.x || Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < min.x ||
                    Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > max.y || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < min.y)
                    continue;

                clipA[0] = a;
                clipA[1] = b;
                clipA[2] = c;
                var input = clipA;
                var output = clipB;
                int count = 3;
                // Most triangles are wholly inside the current nine-slice or HP fill.
                bool inside = rectangular && InRect(a, min, max) && InRect(b, min, max) && InRect(c, min, max);
                for (int edge = 0; !inside && edge < regionCount && count >= 3; edge++)
                {
                    count = Clip(input, output, count, region[edge], region[(edge + 1) % regionCount], winding);
                    (input, output) = (output, input);
                }

                if (count < 3) continue;
                int start = mesh.currentVertCount;
                for (int j = 0; j < count; j++)
                {
                    Vector2 delta = input[j] - region[0];
                    Vector3 position = origin + positionU * (Cross(delta, basisV) / determinant)
                        + positionV * (Cross(basisU, delta) / determinant);
                    mesh.AddVert(position, tint, Vector2.zero);
                }

                for (int j = 1; j < count - 1; j++) mesh.AddTriangle(start, start + j, start + j + 1);
            }
        }
    }

    private static bool InRect(Vector2 point, Vector2 min, Vector2 max)
        => point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y;

    private static int Clip(Vector2[] input, Vector2[] output, int count, Vector2 start, Vector2 end, float winding)
    {
        int written = 0;
        Vector2 direction = end - start;
        Vector2 previous = input[count - 1];
        float previousSide = winding * Cross(direction, previous - start);
        for (int i = 0; i < count; i++)
        {
            Vector2 point = input[i];
            float side = winding * Cross(direction, point - start);
            if ((side >= 0f) != (previousSide >= 0f))
                output[written++] = Vector2.LerpUnclamped(previous, point, previousSide / (previousSide - side));
            if (side >= 0f) output[written++] = point;
            previous = point;
            previousSide = side;
        }

        return written;
    }

    private static bool SameVertex(UIVertex a, UIVertex b) => a.position == b.position && a.uv0 == b.uv0;

    private static Vector2 Normalize(Vector4 uv, Vector2 min, Vector2 size)
        => new Vector2((uv.x - min.x) / size.x, (uv.y - min.y) / size.y);

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static Color32 Multiply(Color32 a, Color32 b) => new Color32(
        (byte)(a.r * b.r / 255), (byte)(a.g * b.g / 255), (byte)(a.b * b.b / 255), (byte)(a.a * b.a / 255));
}
