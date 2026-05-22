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

    // --- 2단계 비용 맵 캐시 추가 ---
    private float[,] _costMap;
    private int _costMapOffsetX;
    private int _costMapOffsetY;
    private int _costMapWidth;
    private int _costMapHeight;

    private static readonly float[,] _corridorWeightTable = new float[5, 5];

    static CorridorPainter()
    {
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                _corridorWeightTable[dx + 2, dy + 2] = 40000f / Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            }
        }
    }

    public void Init(Tilemap ground, Tilemap wall, Tilemap shadow, TileBase floor, TileBase wallT, TileBase shadowT)
    {
        _groundTilemap = ground; _wallTilemap = wall; _shadowTilemap = shadow;
        _floorTile = floor; _wallTile = wallT; _shadowTile = shadowT;

        _totalGroundTiles.Clear(); _totalPathTiles.Clear();
        _roomWallTiles.Clear(); _roomFloorTiles.Clear(); _tileDepths.Clear();

        SnapshotRoomTiles(_groundTilemap, false);
        SnapshotRoomTiles(_wallTilemap, true);

        InitializeCostMap();
    }

    private void InitializeCostMap()
    {
        if (_roomFloorTiles.Count == 0 && _roomWallTiles.Count == 0)
        {
            _costMap = null;
            return;
        }

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (var pos in _roomFloorTiles)
        {
            if (pos.x < minX) minX = pos.x;
            if (pos.y < minY) minY = pos.y;
            if (pos.x > maxX) maxX = pos.x;
            if (pos.y > maxY) maxY = pos.y;
        }
        foreach (var pos in _roomWallTiles)
        {
            if (pos.x < minX) minX = pos.x;
            if (pos.y < minY) minY = pos.y;
            if (pos.x > maxX) maxX = pos.x;
            if (pos.y > maxY) maxY = pos.y;
        }

        // 여유 마진 (복도가 외부로 돌아갈 수 있도록 넉넉하게 50칸씩 부여)
        int padding = 50;
        minX -= padding;
        minY -= padding;
        maxX += padding;
        maxY += padding;

        _costMapOffsetX = minX;
        _costMapOffsetY = minY;
        _costMapWidth = maxX - minX + 1;
        _costMapHeight = maxY - minY + 1;

        _costMap = new float[_costMapWidth, _costMapHeight];

        // 1. 기본 비용 채우기
        for (int x = 0; x < _costMapWidth; x++)
        {
            for (int y = 0; y < _costMapHeight; y++)
            {
                _costMap[x, y] = 1f;
            }
        }

        // 2. 방 장애물 위치 지정 및 패널티 누적
        foreach (var pos in _roomFloorTiles)
        {
            SetCostInMap(pos.x, pos.y, 999999f);
        }
        foreach (var pos in _roomWallTiles)
        {
            SetCostInMap(pos.x, pos.y, 999999f);
        }

        // 3. 방 장애물로부터의 거리 가중치 누적
        float[,] roomWeightTable = new float[5, 5];
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                roomWeightTable[dx + 2, dy + 2] = 50000f / Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            }
        }

        foreach (var pos in _roomFloorTiles)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    AddCostInMap(pos.x + dx, pos.y + dy, roomWeightTable[dx + 2, dy + 2]);
                }
            }
        }

        foreach (var pos in _roomWallTiles)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    AddCostInMap(pos.x + dx, pos.y + dy, roomWeightTable[dx + 2, dy + 2]);
                }
            }
        }
    }

    private float GetCostFromMap(int x, int y)
    {
        int lx = x - _costMapOffsetX;
        int ly = y - _costMapOffsetY;
        if (lx >= 0 && lx < _costMapWidth && ly >= 0 && ly < _costMapHeight)
        {
            return _costMap[lx, ly];
        }
        return 999999f;
    }

    private void SetCostInMap(int x, int y, float cost)
    {
        int lx = x - _costMapOffsetX;
        int ly = y - _costMapOffsetY;
        if (lx >= 0 && lx < _costMapWidth && ly >= 0 && ly < _costMapHeight)
        {
            _costMap[lx, ly] = cost;
        }
    }

    private void AddCostInMap(int x, int y, float delta)
    {
        int lx = x - _costMapOffsetX;
        int ly = y - _costMapOffsetY;
        if (lx >= 0 && lx < _costMapWidth && ly >= 0 && ly < _costMapHeight)
        {
            if (_costMap[lx, ly] < 900000f)
            {
                _costMap[lx, ly] += delta;
            }
        }
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
        if (_totalGroundTiles.Add(pos))
        {
            SetCostInMap(pos.x, pos.y, 999999f);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    AddCostInMap(pos.x + dx, pos.y + dy, _corridorWeightTable[dx + 2, dy + 2]);
                }
            }
        }
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
        return GetCostFromMap(pos.x, pos.y);
    }

    private List<Vector2Int> GetNeighbors(Vector2Int n) => new List<Vector2Int> { new Vector2Int(n.x+1, n.y), new Vector2Int(n.x-1, n.y), new Vector2Int(n.x, n.y+1), new Vector2Int(n.x, n.y-1) };
    private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current) { List<Vector2Int> path = new List<Vector2Int> { current }; while (cameFrom.ContainsKey(current)) { current = cameFrom[current]; path.Add(current); } path.Reverse(); return path; }
}

public class PriorityQueueCustom<T>
{
    private List<KeyValuePair<T, float>> _heap = new List<KeyValuePair<T, float>>();

    public int Count => _heap.Count;

    public void Enqueue(T item, float priority)
    {
        _heap.Add(new KeyValuePair<T, float>(item, priority));
        int childIndex = _heap.Count - 1;
        while (childIndex > 0)
        {
            int parentIndex = (childIndex - 1) / 2;
            if (_heap[childIndex].Value >= _heap[parentIndex].Value)
                break;
            
            // Swap child and parent
            var tmp = _heap[childIndex];
            _heap[childIndex] = _heap[parentIndex];
            _heap[parentIndex] = tmp;
            
            childIndex = parentIndex;
        }
    }

    public T Dequeue()
    {
        if (_heap.Count == 0) return default;

        int lastIndex = _heap.Count - 1;
        T rootItem = _heap[0].Key;
        _heap[0] = _heap[lastIndex];
        _heap.RemoveAt(lastIndex);

        int parentIndex = 0;
        while (true)
        {
            int leftChildIndex = parentIndex * 2 + 1;
            int rightChildIndex = parentIndex * 2 + 2;
            if (leftChildIndex >= _heap.Count)
                break;

            int bestChildIndex = leftChildIndex;
            if (rightChildIndex < _heap.Count && _heap[rightChildIndex].Value < _heap[leftChildIndex].Value)
            {
                bestChildIndex = rightChildIndex;
            }

            if (_heap[parentIndex].Value <= _heap[bestChildIndex].Value)
                break;

            // Swap parent and best child
            var tmp = _heap[parentIndex];
            _heap[parentIndex] = _heap[bestChildIndex];
            _heap[bestChildIndex] = tmp;

            parentIndex = bestChildIndex;
        }

        return rootItem;
    }
}
