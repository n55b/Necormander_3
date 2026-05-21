using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class CorridorPainter : MonoBehaviour
{
    private Tilemap _groundTilemap;
    private Tilemap _wallTilemap;
    private Tilemap _shadowTilemap;
    private TileBase _floorTile;
    private TileBase _wallTile;
    private TileBase _shadowTile;

    private HashSet<Vector2Int> _totalGroundTiles = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _totalPathTiles = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _roomWallTiles = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _roomFloorTiles = new HashSet<Vector2Int>();
    
    private Dictionary<Vector2Int, int> _tileDepths = new Dictionary<Vector2Int, int>();

    public void Init(Tilemap ground, Tilemap wall, Tilemap shadow, TileBase floor, TileBase wallT, TileBase shadowT)
    {
        _groundTilemap = ground; _wallTilemap = wall; _shadowTilemap = shadow;
        _floorTile = floor; _wallTile = wallT; _shadowTile = shadowT;

        _totalGroundTiles.Clear(); _totalPathTiles.Clear();
        _roomWallTiles.Clear(); _roomFloorTiles.Clear(); _tileDepths.Clear();

        SnapshotRoomTiles(_groundTilemap, false);
        SnapshotRoomTiles(_wallTilemap, true);
    }

    private void SnapshotRoomTiles(Tilemap tm, bool isWall)
    {
        if (tm == null) return;
        tm.CompressBounds();
        foreach (var pos in tm.cellBounds.allPositionsWithin)
        {
            if (tm.HasTile(pos))
            {
                Vector3 worldPos = tm.CellToWorld(pos);
                Vector2Int gridPos = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
                if (isWall) _roomWallTiles.Add(gridPos); else _roomFloorTiles.Add(gridPos);
            }
        }
    }

    public void RegisterCorridorWithAnchors(List<Vector2Int> path, RoomAnchor startAnchor, RoomAnchor endAnchor, int pathDepth)
    {
        if (path == null || path.Count == 0) return;

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int pos = path[i];
            Vector2Int dir = GetDirection(path, i);
            Vector2Int sideDir = new Vector2Int(-dir.y, dir.x);

            _totalPathTiles.Add(pos);
            _tileDepths[pos] = pathDepth;
            
            AddGroundWithDepth(pos, pathDepth);
            AddGroundWithDepth(pos + sideDir, pathDepth);
            AddGroundWithDepth(pos - sideDir, pathDepth);

            if (i > 0 && i < path.Count - 1)
            {
                if ((path[i] - path[i - 1]) != (path[i + 1] - path[i]))
                {
                    for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) AddGroundWithDepth(pos + new Vector2Int(x, y), pathDepth);
                }
            }
        }

        if (startAnchor != null) ApplyAnchorEntrance(startAnchor, pathDepth);
        if (endAnchor != null) ApplyAnchorEntrance(endAnchor, pathDepth);
    }

    private void AddGroundWithDepth(Vector2Int pos, int depth)
    {
        _totalGroundTiles.Add(pos);
        if (!_tileDepths.ContainsKey(pos)) _tileDepths[pos] = depth;
    }

    private void ApplyAnchorEntrance(RoomAnchor anchor, int depth)
    {
        Vector3Int cellPos3 = _wallTilemap.WorldToCell(anchor.transform.position);
        Vector2Int pos = new Vector2Int(cellPos3.x, cellPos3.y);
        Vector2Int dir = anchor.direction;
        Vector2Int sideDir = new Vector2Int(-dir.y, dir.x);

        for (int d = -1; d <= 1; d++)
        {
            for (int s = -1; s <= 1; s++)
            {
                Vector2Int targetPos = pos + (dir * d) + (sideDir * s);
                if (s == 0) { _totalPathTiles.Add(targetPos); _tileDepths[targetPos] = depth; }
                AddGroundWithDepth(targetPos, depth);
            }
        }
    }

    public HashSet<Vector2Int> GetTotalGroundTiles() => _totalGroundTiles;

    public void FinalizePainting()
    {
        foreach (var gPos in _totalGroundTiles) if (!_roomFloorTiles.Contains(gPos)) _groundTilemap.SetTile((Vector3Int)gPos, _floorTile);
        foreach (var gPos in _totalGroundTiles)
        {
            _wallTilemap.SetTile((Vector3Int)gPos, null); _shadowTilemap.SetTile((Vector3Int)gPos, null);
            if (!_totalPathTiles.Contains(gPos) && !_roomFloorTiles.Contains(gPos)) _shadowTilemap.SetTile((Vector3Int)gPos, _shadowTile);
        }
        foreach (var gPos in _totalGroundTiles)
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
            {
                Vector2Int neighbor = gPos + new Vector2Int(x, y);
                if (!_totalGroundTiles.Contains(neighbor) && !_roomFloorTiles.Contains(neighbor))
                {
                    _wallTilemap.SetTile((Vector3Int)neighbor, _wallTile); _shadowTilemap.SetTile((Vector3Int)neighbor, _shadowTile);
                }
            }
    }

    private Vector2Int GetDirection(List<Vector2Int> path, int index)
    {
        if (path == null || path.Count < 2) return Vector2Int.up;
        if (index < path.Count - 1) return path[index + 1] - path[index];
        return path[index] - path[index - 1];
    }

    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int end, int margin, int currentPathDepth)
    {
        PriorityQueueCustom<Vector2Int> openSet = new PriorityQueueCustom<Vector2Int>();
        openSet.Enqueue(start, 0);
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float>();
        gScore[start] = 0;

        int iterations = 0;
        while (openSet.Count > 0 && iterations < 12000) 
        {
            iterations++;
            Vector2Int current = openSet.Dequeue();
            if (current == end) return ReconstructPath(cameFrom, current);

            Vector2Int lastDir = cameFrom.ContainsKey(current) ? current - cameFrom[current] : Vector2Int.zero;

            foreach (Vector2Int neighbor in GetNeighbors(current))
            {
                float moveCost = CalculateCost(neighbor, margin, currentPathDepth);
                if (moveCost >= 900000f) continue;

                if (lastDir != Vector2Int.zero && lastDir != (neighbor - current)) moveCost += 25f; 

                float tentativeGScore = gScore[current] + moveCost;
                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current; gScore[neighbor] = tentativeGScore;
                    float h = Mathf.Abs(neighbor.x - end.x) + Mathf.Abs(neighbor.y - end.y);
                    openSet.Enqueue(neighbor, tentativeGScore + h * 1.1f);
                }
            }
        }
        return null;
    }

    private float CalculateCost(Vector2Int pos, int margin, int currentPathDepth)
    {
        float cost = 1f;
        if (_roomFloorTiles.Contains(pos) || _roomWallTiles.Contains(pos)) return 999999f; 
        if (_totalGroundTiles.Contains(pos)) return 999999f; 

        int checkMargin = Mathf.Max(margin, 2);
        for (int x = -checkMargin; x <= checkMargin; x++)
            for (int y = -checkMargin; y <= checkMargin; y++)
            {
                if (x == 0 && y == 0) continue;
                Vector2Int n = pos + new Vector2Int(x, y);
                if (_roomFloorTiles.Contains(n) || _roomWallTiles.Contains(n)) cost += (50000f / Mathf.Max(Mathf.Abs(x), Mathf.Abs(y))); 
                else if (_totalGroundTiles.Contains(n)) cost += (40000f / Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)));
            }
        return cost;
    }

    private List<Vector2Int> GetNeighbors(Vector2Int n) => new List<Vector2Int> { new Vector2Int(n.x+1, n.y), new Vector2Int(n.x-1, n.y), new Vector2Int(n.x, n.y+1), new Vector2Int(n.x, n.y-1) };
    private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current) { List<Vector2Int> path = new List<Vector2Int> { current }; while (cameFrom.ContainsKey(current)) { current = cameFrom[current]; path.Add(current); } path.Reverse(); return path; }
}

public class PriorityQueueCustom<T>
{
    private List<KeyValuePair<T, float>> elements = new List<KeyValuePair<T, float>>();
    public int Count => elements.Count;
    public void Enqueue(T item, float priority) { elements.Add(new KeyValuePair<T, float>(item, priority)); }
    public T Dequeue() { int b = 0; for (int i = 0; i < elements.Count; i++) if (elements[i].Value < elements[b].Value) b = i; T item = elements[b].Key; elements.RemoveAt(b); return item; }
}
