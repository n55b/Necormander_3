#if UNITY_EDITOR || LEVEL_WORKSHOP
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

// Exit passages: the cells the dungeon opens behind each exit (RoomAnchor.PassageCells, the same call the game makes).
// Any of them without floor is filled with the generation floor tile in game, so the editor marks them while painting.
internal sealed partial class WorkshopPreview
{
    internal static readonly Color32 ExitFrameColor = new(246, 96, 84, 235);
    internal static readonly Color32 ExitGapColor = new(246, 96, 84, 110);
    internal static readonly Color32 ExitLandingColor = new(255, 212, 103, 235);
    private readonly List<Transform> exitEdges = new();
    private readonly List<Transform> exitMarks = new();
    private bool exitsHidden;

    private Tilemap PassageGrid => maps[1] != null ? maps[1] : Ground;

    private RoomAnchor ExitAnchor(Vector2Int direction, Transform node)
    {
        var anchor = node.GetComponent<RoomAnchor>() ?? node.gameObject.AddComponent<RoomAnchor>();
        anchor.direction = direction;
        return anchor;
    }

    internal readonly struct ExitPassage
    {
        public readonly Vector2Int direction;
        public readonly Vector3Int[] cells, missing;
        public readonly Vector3Int landing;
        public ExitPassage(Vector2Int direction, Vector3Int[] cells, Vector3Int[] missing, Vector3Int landing)
        { this.direction = direction; this.cells = cells; this.missing = missing; this.landing = landing; }
    }

    internal List<ExitPassage> ExitPassages()
    {
        var result = new List<ExitPassage>();
        var grid = PassageGrid;
        if (grid == null || Ground == null) return result;
        foreach (var pair in doors)
        {
            var anchor = ExitAnchor(pair.Key, pair.Value);
            var cells = anchor.PassageCells(grid, false).ToArray();
            var missing = cells.Where(cell => !Ground.HasTile(cell)).ToArray();
            result.Add(new ExitPassage(pair.Key, cells, missing, grid.WorldToCell(anchor.ArrivalPoint(grid))));
        }
        return result;
    }

    internal void ShowExitPassages(bool visible)
    {
        exitsHidden = !visible;
        UpdateExitPassages();
    }

    // Thin red frame around each passage, translucent red on cells still without floor, a small mark where players land.
    internal void UpdateExitPassages()
    {
        int edge = 0, mark = 0;
        if (!exitsHidden)
        {
            float pixel = 1f / Density;
            foreach (var exit in ExitPassages())
            {
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                foreach (var cell in exit.cells)
                {
                    var (a, b) = CellCorners(cell);
                    lo = Vector3.Min(lo, a); hi = Vector3.Max(hi, b);
                }
                float w = hi.x - lo.x, h = hi.y - lo.y;
                Edge(new Vector3((lo.x + hi.x) * .5f, hi.y - pixel * .5f), w, pixel);
                Edge(new Vector3((lo.x + hi.x) * .5f, lo.y + pixel * .5f), w, pixel);
                Edge(new Vector3(lo.x + pixel * .5f, (lo.y + hi.y) * .5f), pixel, h);
                Edge(new Vector3(hi.x - pixel * .5f, (lo.y + hi.y) * .5f), pixel, h);
                foreach (var cell in exit.missing)
                {
                    var (a, b) = CellCorners(cell);
                    Mark((a + b) * .5f, b - a, ExitGapColor, 14965);
                }
                var (la, lb) = CellCorners(exit.landing);
                Mark((la + lb) * .5f, (lb - la) * .4f, ExitLandingColor, 14986);
            }
        }
        for (int i = edge; i < exitEdges.Count; i++) exitEdges[i].gameObject.SetActive(false);
        for (int i = mark; i < exitMarks.Count; i++) exitMarks[i].gameObject.SetActive(false);

        void Edge(Vector3 center, float width, float height)
        {
            if (edge == exitEdges.Count)
            {
                var line = AddMarker(stage.transform, ExitFrameColor, 1);
                line.name = "Exit passage frame";
                line.GetComponent<SpriteRenderer>().sortingOrder = 14985;
                exitEdges.Add(line);
            }
            var node = exitEdges[edge++];
            node.gameObject.SetActive(true);
            node.position = center;
            node.localScale = new Vector3(Mathf.Abs(width), Mathf.Abs(height), 1);
        }
        void Mark(Vector3 center, Vector3 size, Color32 color, int order)
        {
            if (mark == exitMarks.Count)
            {
                var node = AddMarker(stage.transform, Color.white, 1);
                node.name = "Exit passage cell";
                exitMarks.Add(node);
            }
            var m = exitMarks[mark++];
            m.gameObject.SetActive(true);
            m.position = center;
            m.localScale = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), 1);
            var r = m.GetComponent<SpriteRenderer>();
            r.color = color; r.sortingOrder = order;
        }
    }

    private (Vector3 low, Vector3 high) CellCorners(Vector3Int cell)
    {
        var a = Ground.CellToWorld(cell);
        var b = Ground.CellToWorld(cell + new Vector3Int(1, 1, 0));
        return (Vector3.Min(a, b), Vector3.Max(a, b));
    }
}
#endif
