#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Layer coverage: which cells of the working layer are filled, drawn like the Unity scene view draws a selected tilemap.
// Every layer gets an orange outline along the edge of its filled area. The unsteppable layer also gets a green
// collider box on every filled cell, since it has no artwork of its own. (Walls fill whole forests; boxes there were too busy.)
internal sealed partial class WorkshopPreview
{
    internal static readonly Color32 CoverageOutlineColor = new(255, 102, 0, 255);   // scene view selection outline
    internal static readonly Color32 CoverageCellColor = new(145, 244, 139, 192);    // Physics2D collider gizmo
    private const int CoverageCellOrder = 14950, CoverageOutlineOrder = 14955;

    private readonly List<Transform> coverageEdges = new();
    private readonly List<(Vector3 from, Vector3 to, Vector3 inward)> coverageSegments = new();
    private readonly HashSet<Vector3Int> coverageCells = new();
    private Tilemap coverageMap;
    private Tile coverageTile;
    private Texture2D coverageTexture;
    private readonly List<Vector3Int> coverageChanges = new();
    private int coverageLayer = -1;
    private bool coverageDirty = true, coverageEdgesDirty, coverageHidden;
    private float coverageDensity;

    internal static bool ShowsCoverage(int layer) => layer >= 0 && layer < 4;
    internal static bool ShowsCoverageCells(int layer) => layer == 3;
    internal int CoverageLayer => coverageLayer;
    internal int CoverageCount => coverageLayer >= 0 ? coverageCells.Count : 0;
    internal int CoverageEdgeCount => coverageSegments.Count;
    internal bool CoverageVisible => !coverageHidden && coverageLayer >= 0;
    internal bool CoverageCellsVisible => CoverageVisible && coverageMap != null && coverageMap.gameObject.activeSelf && coverageMap.GetUsedTilesCount() > 0;
    private bool CoverageHasCells => coverageLayer >= 0 && ShowsCoverageCells(coverageLayer);

    // Called every frame by the window with its working layer. A new layer is read in full; a brush stroke only
    // updates the cells it touched (the wall layer of a big room has close to 20,000 cells).
    internal void UpdateCoverage(int layer)
    {
        if (disposed) return;
        int shown = ShowsCoverage(layer) && maps[layer] != null ? layer : -1;
        if (shown != coverageLayer) { coverageLayer = shown; coverageDirty = true; }
        if (coverageDirty) { RebuildCoverage(); coverageChanges.Clear(); coverageEdgesDirty = true; }
        else if (coverageChanges.Count > 0) { ApplyCoverageChanges(); coverageEdgesDirty = true; }
        if (coverageEdgesDirty) { FindCoverageEdges(); PlaceCoverageEdges(); }
        else if (!Mathf.Approximately(coverageDensity, Density)) PlaceCoverageEdges();
        coverageDirty = coverageEdgesDirty = false;
    }

    internal void ShowCoverage(bool visible)
    {
        if (coverageHidden == !visible) return;
        coverageHidden = !visible;
        if (coverageMap != null) coverageMap.gameObject.SetActive(visible && CoverageHasCells);
        coverageDensity = -1;
        if (!disposed) PlaceCoverageEdges();
    }

    private void MarkCoverageDirty(Tilemap map, Tilemap.SyncTile[] tiles)
    {
        if (coverageLayer < 0 || map != maps[coverageLayer]) return;
        if (tiles == null || coverageChanges.Count + tiles.Length > 4096) { coverageDirty = true; return; }
        foreach (var t in tiles) coverageChanges.Add(t.position);
    }

    private void ApplyCoverageChanges()
    {
        var source = maps[coverageLayer];
        var scale = source.layoutGrid.cellSize;
        var matrix = Matrix4x4.Scale(new Vector3(scale.x, scale.y, 1));
        var changes = new TileChangeData[coverageChanges.Count];
        for (int i = 0; i < coverageChanges.Count; i++)
        {
            var cell = coverageChanges[i];
            bool filled = source.HasTile(cell);
            if (filled) coverageCells.Add(cell); else coverageCells.Remove(cell);
            changes[i] = new TileChangeData(cell, filled ? coverageTile : null, Color.white, matrix);
        }
        coverageChanges.Clear();
        if (CoverageHasCells) coverageMap.SetTiles(changes, true);
    }

    private void RebuildCoverage()
    {
        coverageCells.Clear();
        if (coverageLayer < 0)
        {
            if (coverageMap != null) coverageMap.gameObject.SetActive(false);
            return;
        }
        var source = maps[coverageLayer];
        var bounds = source.cellBounds;
        foreach (var cell in bounds.allPositionsWithin)
            if (source.HasTile(cell)) coverageCells.Add(cell);

        EnsureCoverageMap(source);
        coverageMap.ClearAllTiles();
        coverageMap.gameObject.SetActive(!coverageHidden && CoverageHasCells);
        if (!CoverageHasCells) return;
        var scale = source.layoutGrid.cellSize;
        var matrix = Matrix4x4.Scale(new Vector3(scale.x, scale.y, 1));
        var changes = new TileChangeData[coverageCells.Count];
        int i = 0;
        foreach (var cell in coverageCells) changes[i++] = new TileChangeData(cell, coverageTile, Color.white, matrix);
        coverageMap.SetTiles(changes, true);
    }

    // Outer edges, merged into runs so a long wall is one line, not one per cell.
    private void FindCoverageEdges()
    {
        coverageSegments.Clear();
        if (coverageLayer < 0) return;
        var source = maps[coverageLayer];
        var horizontal = new Dictionary<(int y, int side), List<int>>();
        var vertical = new Dictionary<(int x, int side), List<int>>();
        foreach (var c in coverageCells)
        {
            if (!coverageCells.Contains(c + Vector3Int.up)) Add(horizontal, (c.y + 1, -1), c.x);
            if (!coverageCells.Contains(c + Vector3Int.down)) Add(horizontal, (c.y, 1), c.x);
            if (!coverageCells.Contains(c + Vector3Int.right)) Add(vertical, (c.x + 1, -1), c.y);
            if (!coverageCells.Contains(c + Vector3Int.left)) Add(vertical, (c.x, 1), c.y);
        }
        foreach (var pair in horizontal)
            foreach (var (from, to) in Runs(pair.Value))
                coverageSegments.Add((source.CellToWorld(new Vector3Int(from, pair.Key.y, 0)),
                    source.CellToWorld(new Vector3Int(to + 1, pair.Key.y, 0)), new Vector3(0, pair.Key.side, 0)));
        foreach (var pair in vertical)
            foreach (var (from, to) in Runs(pair.Value))
                coverageSegments.Add((source.CellToWorld(new Vector3Int(pair.Key.x, from, 0)),
                    source.CellToWorld(new Vector3Int(pair.Key.x, to + 1, 0)), new Vector3(pair.Key.side, 0, 0)));

        static void Add<TKey>(Dictionary<TKey, List<int>> lines, TKey key, int at)
        {
            if (!lines.TryGetValue(key, out var list)) lines[key] = list = new List<int>();
            list.Add(at);
        }
        static IEnumerable<(int, int)> Runs(List<int> values)
        {
            values.Sort();
            int start = values[0], end = values[0];
            for (int k = 1; k < values.Count; k++)
            {
                if (values[k] == end + 1) { end = values[k]; continue; }
                yield return (start, end);
                start = end = values[k];
            }
            yield return (start, end);
        }
    }

    // One screen pixel thick at every zoom, drawn just inside the filled area like the scene view outline.
    private void PlaceCoverageEdges()
    {
        coverageDensity = Density;
        int used = 0;
        if (!coverageHidden && coverageLayer >= 0)
        {
            float pixel = 1f / Density;
            foreach (var (from, to, inward) in coverageSegments)
            {
                if (used == coverageEdges.Count)
                {
                    var line = AddMarker(stage.transform, CoverageOutlineColor, 1);
                    line.name = "Layer coverage edge";
                    line.GetComponent<SpriteRenderer>().sortingOrder = CoverageOutlineOrder;
                    coverageEdges.Add(line);
                }
                var node = coverageEdges[used++];
                node.gameObject.SetActive(true);
                var size = to - from;
                node.position = (from + to) * .5f + inward * (pixel * .5f);
                node.localScale = new Vector3(Mathf.Max(Mathf.Abs(size.x), pixel), Mathf.Max(Mathf.Abs(size.y), pixel), 1);
            }
        }
        for (int i = used; i < coverageEdges.Count; i++) coverageEdges[i].gameObject.SetActive(false);
    }

    private void EnsureCoverageMap(Tilemap source)
    {
        if (coverageMap == null)
        {
            var node = new GameObject("Layer coverage", typeof(Tilemap), typeof(TilemapRenderer)) { layer = Layer };
            coverageMap = node.GetComponent<Tilemap>();
            var renderer = node.GetComponent<TilemapRenderer>();
            renderer.sortingLayerName = "UI"; renderer.sortingOrder = CoverageCellOrder; renderer.sharedMaterial = unlit;
            // A collider box per cell: a bright one-texel border over a faint fill, like the scene view gizmo.
            const int size = 16;
            coverageTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                var c = CoverageCellColor;
                pixels[y * size + x] = new Color32(c.r, c.g, c.b, border ? c.a : (byte)34);
            }
            coverageTexture.SetPixels32(pixels); coverageTexture.Apply(false, true);
            coverageTile = ScriptableObject.CreateInstance<Tile>();
            coverageTile.sprite = Sprite.Create(coverageTexture, new Rect(0, 0, size, size), Vector2.one * .5f, size, 0, SpriteMeshType.FullRect);
            coverageTile.flags = TileFlags.None; coverageTile.colliderType = Tile.ColliderType.None;
        }
        // Sits beside the layer it shows, so it shares that layer's grid, offset and scale.
        var t = coverageMap.transform;
        t.SetParent(source.transform.parent, false);
        t.localPosition = source.transform.localPosition;
        t.localRotation = source.transform.localRotation;
        t.localScale = source.transform.localScale;
        coverageMap.tileAnchor = source.tileAnchor;
    }

    private void DestroyCoverage()
    {
        if (coverageTile != null) Object.Destroy(coverageTile.sprite);
        Object.Destroy(coverageTile);
        Object.Destroy(coverageTexture);
    }
}
#endif
