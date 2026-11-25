using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>Draws floor dots in tile order while sharing the HUD canvas clipping.</summary>
public sealed class MiniMapTerrainGraphic : MaskableGraphic
{
    // VertexHelper meshes are limited to fewer than 65,000 vertices.
    private const int TilesPerMesh = 8000;
    private MiniMapFloor floor;
    private int firstTile;
    private int tileEnd;
    private float cell;
    private Vector2 halfSize;
    private Sprite sprite;
    private Color32 shadow;
    private Vector2 shadowOffset;
    private Vector2Int[] tileAxes;
    private float[] columns, rows;
    private Vector2[] columnEdges, rowEdges;
    private bool edgesReady, submitted;
    private Color32 submittedColor;
    private Vector4 submittedUV;
    private readonly List<Component> modifiers = new();

    public override Texture mainTexture => sprite != null ? sprite.texture : base.mainTexture;

    internal static void Draw(RectTransform parent, MiniMapFloor floor, Vector2 uiSize, Sprite sprite, float overlap,
        Color tint, Color shadow, Vector2 shadowOffset)
    {
        if (floor.Tiles.Count == 0) return;
        float cell = uiSize.x / floor.Span.x;
        shadow.a *= tint.a;
        for (int first = 0; first < floor.Tiles.Count; first += TilesPerMesh)
        {
            var obj = new GameObject("Terrain", typeof(RectTransform), typeof(CanvasRenderer), typeof(MiniMapTerrainGraphic));
            var rt = (RectTransform)obj.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one * 0.5f;
            rt.sizeDelta = uiSize;
            rt.anchoredPosition = Vector2.zero;

            var graphic = obj.GetComponent<MiniMapTerrainGraphic>();
            graphic.floor = floor;
            graphic.firstTile = first;
            graphic.tileEnd = Mathf.Min(first + TilesPerMesh, floor.Tiles.Count);
            graphic.cell = cell;
            graphic.halfSize = Vector2.one * (cell * overlap * 0.5f);
            graphic.sprite = sprite;
            graphic.color = tint;
            graphic.shadow = shadow;
            graphic.shadowOffset = shadowOffset;
            graphic.raycastTarget = false;
            graphic.IndexTileAxes();
            graphic.SetAllDirty();
        }
    }

    private void IndexTileAxes()
    {
        var x = new Dictionary<float, int>();
        var y = new Dictionary<float, int>();
        tileAxes = new Vector2Int[tileEnd - firstTile];
        for (int i = firstTile; i < tileEnd; ++i)
        {
            Vector2 center = (floor.Tiles[i] - floor.Center) * cell;
            if (!x.TryGetValue(center.x, out int column)) x.Add(center.x, column = x.Count);
            if (!y.TryGetValue(center.y, out int row)) y.Add(center.y, row = y.Count);
            tileAxes[i - firstTile] = new Vector2Int(column, row);
        }
        columns = new float[x.Count];
        rows = new float[y.Count];
        foreach (var entry in x) columns[entry.Value] = entry.Key;
        foreach (var entry in y) rows[entry.Value] = entry.Key;
        columnEdges = new Vector2[x.Count];
        rowEdges = new Vector2[y.Count];
    }

    protected override void OnEnable()
    {
        submitted = false;
        base.OnEnable();
    }

    private bool PrepareEdges(out bool changed)
    {
        changed = false;
        var root = canvas != null ? canvas.rootCanvas : null;
        var basis = root != null ? root.transform.worldToLocalMatrix * rectTransform.localToWorldMatrix : Matrix4x4.identity;
        bool aligned = tileAxes != null && Mathf.Abs(basis.m01) < 1e-6f && Mathf.Abs(basis.m10) < 1e-6f &&
                       Mathf.Abs(basis.m20) < 1e-6f && Mathf.Abs(basis.m21) < 1e-6f;
        if (!aligned) return false;
        for (int i = 0; i < columns.Length; ++i)
        {
            var edge = new Vector2(PixelAdjustPoint(new Vector2(columns[i] - halfSize.x, 0)).x,
                                   PixelAdjustPoint(new Vector2(columns[i] + halfSize.x, 0)).x);
            changed |= !columnEdges[i].Equals(edge);
            columnEdges[i] = edge;
        }
        for (int i = 0; i < rows.Length; ++i)
        {
            var edge = new Vector2(PixelAdjustPoint(new Vector2(0, rows[i] - halfSize.y)).y,
                                   PixelAdjustPoint(new Vector2(0, rows[i] + halfSize.y)).y);
            changed |= !rowEdges[i].Equals(edge);
            rowEdges[i] = edge;
        }
        return true;
    }

    private Vector4 OuterUV => sprite != null ? DataUtility.GetOuterUV(sprite) : new Vector4(0f, 0f, 1f, 1f);

    protected override void UpdateGeometry()
    {
        edgesReady = PrepareEdges(out bool changed);
        try
        {
            var uv = OuterUV;
            Color32 tint = color;
            GetComponents(typeof(IMeshModifier), modifiers);
            bool cacheable = edgesReady && modifiers.Count == 0 && rectTransform.rect.width >= 0 && rectTransform.rect.height >= 0;
            if (cacheable && submitted && !changed && submittedUV.Equals(uv) && submittedColor.Equals(tint)) return;
            base.UpdateGeometry();
            submitted = cacheable;
            submittedUV = uv;
            submittedColor = tint;
        }
        finally { edgesReady = false; }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (floor == null) return;
        var uv = OuterUV;
        Color32 tint = color;
        bool aligned = edgesReady || PrepareEdges(out _);
        for (int index = firstTile; index < tileEnd; index++)
        {
            Vector2 min, max;
            if (aligned)
            {
                var axes = tileAxes[index - firstTile];
                var x = columnEdges[axes.x];
                var y = rowEdges[axes.y];
                min = new Vector2(x.x, y.x);
                max = new Vector2(x.y, y.y);
            }
            else
            {
                Vector2 center = (floor.Tiles[index] - floor.Center) * cell;
                min = PixelAdjustPoint(center - halfSize);
                max = PixelAdjustPoint(center + halfSize);
            }
            if (shadow.a != 0) AddQuad(vh, min + shadowOffset, max + shadowOffset, uv, shadow);
            AddQuad(vh, min, max, uv, tint);
        }
    }

    private static void AddQuad(VertexHelper vh, Vector2 min, Vector2 max, Vector4 uv, Color32 tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(min.x, min.y), tint, new Vector2(uv.x, uv.y));
        vh.AddVert(new Vector3(min.x, max.y), tint, new Vector2(uv.x, uv.w));
        vh.AddVert(new Vector3(max.x, max.y), tint, new Vector2(uv.z, uv.w));
        vh.AddVert(new Vector3(max.x, min.y), tint, new Vector2(uv.z, uv.y));
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }
}
