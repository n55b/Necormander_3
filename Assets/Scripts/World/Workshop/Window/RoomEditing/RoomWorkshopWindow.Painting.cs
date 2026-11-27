#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Brush, line, rectangle and fill painting on the working layer.
public sealed partial class RoomWorkshopWindow
{
    private IEnumerable<Vector2Int> Brush(Vector2Int center)
    {
        int low = -(brushSize - 1) / 2;
        for (int y = 0; y < brushSize; y++)
        for (int x = 0; x < brushSize; x++)
            yield return center + new Vector2Int(low + x, low + y);
    }

    private static IEnumerable<Vector2Int> Segment(Vector2Int from, Vector2Int to)
    {
        int steps = Mathf.Max(1, Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
        for (int i = 0; i <= steps; i++) yield return Vector2Int.RoundToInt(Vector2.Lerp(from, to, i / (float)steps));
    }

    private void PaintCells(Tilemap map, IEnumerable<Vector2Int> cells, bool erase)
    {
        if (!erase && palette.Length == 0) return;
        string id = erase ? "" : palette[materialIndex].id;
        var tile = erase ? null : palette[materialIndex].tile;
        foreach (var at in cells.Distinct())
        {
            if (Mathf.Abs(at.x) > 1024 || Mathf.Abs(at.y) > 1024) continue;
            var cell = (Vector3Int)at;
            if (map.GetTile(cell) == tile) continue;
            Doc.Paint(layer, at, id); map.SetTile(cell, tile);
            if (layer <= 1) WorkshopTerrainTile.RefreshNeighbors(map, cell);
        }
        if (!erase) { RememberTile(id); RememberQuick(new QuickMaterial('t', id, tool == Tool.Auto ? -1 : layer)); }
        fillRegion = null; ghostKey = null;
    }

    // Brush and eraser strokes. Holding Shift at the press draws a straight line on release.
    private void PaintStroke(Tilemap map, Vector2Int cell, bool first)
    {
        if (first) { shapeStart = cell; lineStroke = Keyboard.current?.shiftKey.isPressed == true; }
        if (lineStroke)
        {
            var a = map.CellToWorld((Vector3Int)Vector2Int.Min(shapeStart.Value, cell));
            var b = map.CellToWorld((Vector3Int)(Vector2Int.Max(shapeStart.Value, cell) + Vector2Int.one));
            preview.Area(a, b);
            return;
        }
        if (lastCell == cell) return;
        PaintCells(map, Segment(lastCell ?? cell, cell).SelectMany(Brush), tool == Tool.Erase);
        lastCell = cell;
    }

    private void RectPointer(Tilemap map, Vector2Int cell, bool first)
    {
        if (palette.Length == 0) return;
        if (first) shapeStart = cell;
        var a = map.CellToWorld((Vector3Int)Vector2Int.Min(shapeStart.Value, cell));
        var b = map.CellToWorld((Vector3Int)(Vector2Int.Max(shapeStart.Value, cell) + Vector2Int.one));
        preview.Area(a, b);
    }

    // Called on release: commits a rectangle or a Shift line.
    private void FinishShape(Vector3 world)
    {
        if (!shapeStart.HasValue) return;
        var begin = shapeStart.Value;
        shapeStart = null;
        var map = preview.Map(layer);
        if (map == null) return;
        var end = (Vector2Int)map.WorldToCell(world);
        if (tool == Tool.Rect)
        {
            preview.HideArea();
            var lo = Vector2Int.Min(begin, end); var hi = Vector2Int.Max(begin, end);
            if ((hi.x - lo.x + 1) * (hi.y - lo.y + 1) > 20000) { Say("사각형은 2만 칸까지 칠할 수 있습니다."); return; }
            var cells = new List<Vector2Int>();
            for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++) cells.Add(new Vector2Int(x, y));
            PaintCells(map, cells, false);
        }
        else if (lineStroke && tool is Tool.Paint or Tool.Auto or Tool.Erase)
        {
            preview.HideArea();
            PaintCells(map, Segment(begin, end).SelectMany(Brush), tool == Tool.Erase);
        }
        lineStroke = false;
    }

    // The connected cells sharing the clicked cell's tile on the working layer, bounded by the room.
    private bool FloodRegion(Tilemap map, Vector2Int cell, out List<Vector2Int> region, out string problem)
    {
        region = new List<Vector2Int>(); problem = null;
        if (palette.Length == 0) { problem = "칠할 재료가 없습니다."; return false; }
        var target = map.GetTile((Vector3Int)cell);
        if (target == palette[materialIndex].tile) { problem = "이미 같은 타일입니다."; return false; }
        var bounds = FillBounds(map);
        if (!bounds.Contains(cell)) { problem = "방 범위 밖입니다. 출구 안쪽을 눌러 채우세요."; return false; }
        var seen = new HashSet<Vector2Int> { cell };
        var pending = new Queue<Vector2Int>();
        pending.Enqueue(cell);
        while (pending.Count > 0)
        {
            var at = pending.Dequeue();
            region.Add(at);
            if (region.Count > 40000) { problem = "채울 범위가 너무 넓습니다."; return false; }
            foreach (var step in directions)
            {
                var next = at + step;
                if (!bounds.Contains(next) || !seen.Add(next) || map.GetTile((Vector3Int)next) != target) continue;
                pending.Enqueue(next);
            }
        }
        return true;
    }

    private void Fill(Tilemap map, Vector2Int cell)
    {
        if (!FloodRegion(map, cell, out var region, out string problem)) { Say(problem); return; }
        PaintCells(map, region, false);
        Say(region.Count.ToString("N0") + "칸을 채웠습니다.");
    }

    // Shows, before the click, exactly which cells the fill would paint.
    private void PreviewFill(Tilemap map, Vector3Int cell)
    {
        preview.Hover(null, Vector3.zero, false, Color.clear);
        var at = (Vector2Int)cell;
        string material = palette.Length > 0 ? palette[materialIndex].id : null;
        if (fillRegion != null && fillLayer == layer && fillMaterial == material && fillRegion.Contains(at)) return;
        if (!FloodRegion(map, at, out var region, out string problem))
        {
            fillRegion = null;
            preview.HideGhost();
            preview.HoverCells(map, cell, cell, EraseColor);
            Say("채우기 · " + problem);
            return;
        }
        fillRegion = new HashSet<Vector2Int>(region); fillLayer = layer; fillMaterial = material;
        var tile = palette[materialIndex].tile;
        preview.GhostTiles(map, region.Select(c => new TileChangeData((Vector3Int)c, tile, GhostTint, Matrix4x4.identity)).ToArray());
        Say("채우기 · 누르면 " + region.Count.ToString("N0") + "칸을 채웁니다.");
    }

    private RectInt FillBounds(Tilemap map)
    {
        bool any = false; int minX = 0, minY = 0, maxX = 0, maxY = 0;
        void Add(Vector2Int c)
        {
            if (!any) { minX = maxX = c.x; minY = maxY = c.y; any = true; return; }
            minX = Math.Min(minX, c.x); maxX = Math.Max(maxX, c.x); minY = Math.Min(minY, c.y); maxY = Math.Max(maxY, c.y);
        }
        for (int i = 0; i < 4; i++)
        {
            var m = preview.Map(i);
            if (m == null || m.GetUsedTilesCount() == 0) continue;
            m.CompressBounds();
            Add((Vector2Int)m.cellBounds.min); Add((Vector2Int)(m.cellBounds.max - Vector3Int.one));
        }
        foreach (var direction in directions)
        {
            var spot = preview.DoorLocal(direction);
            if (spot.HasValue) Add((Vector2Int)map.WorldToCell(preview.World(spot.Value)));
        }
        if (!any) return new RectInt(-16, -16, 33, 33);
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
#endif
