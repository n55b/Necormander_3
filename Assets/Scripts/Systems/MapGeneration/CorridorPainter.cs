using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Linq;

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
    private HashSet<Vector2Int> _roomTiles = new HashSet<Vector2Int>(); // 방이 점유한 공간 (바닥+벽)

    public void Init(Tilemap ground, Tilemap wall, Tilemap shadow, TileBase floor, TileBase wallT, TileBase shadowT)
    {
        _groundTilemap = ground;
        _wallTilemap = wall;
        _shadowTilemap = shadow;
        _floorTile = floor;
        _wallTile = wallT;
        _shadowTile = shadowT;

        _totalGroundTiles.Clear();
        _totalPathTiles.Clear();
        _roomTiles.Clear();

        // 현재 배치된 모든 방의 타일 정보를 스냅샷으로 저장
        SnapshotRoomTiles(_groundTilemap);
        SnapshotRoomTiles(_wallTilemap);
    }

    private void SnapshotRoomTiles(Tilemap tm)
    {
        if (tm == null) return;
        BoundsInt bounds = tm.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            if (tm.HasTile(pos)) _roomTiles.Add((Vector2Int)(Vector3Int)pos);
        }
    }

    public void RegisterCorridor(List<Vector2Int> path)
    {
        if (path == null || path.Count == 0) return;

        // 중앙 경로 등록 (Shadow와 Wall 제거용, 1칸)
        foreach (var p in path) _totalPathTiles.Add(p);

        // 전체 Ground 구역 등록 (3칸 너비)
        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int pos = path[i];
            Vector2Int dir = GetDirection(path, i);
            Vector2Int sideDir = new Vector2Int(-dir.y, dir.x);

            _totalGroundTiles.Add(pos);
            _totalGroundTiles.Add(pos + sideDir);
            _totalGroundTiles.Add(pos - sideDir);

            // 코너 보정 (3x3)
            if (i > 0 && i < path.Count - 1)
            {
                Vector2Int prevDir = path[i] - path[i - 1];
                Vector2Int nextDir = path[i + 1] - path[i];
                if (prevDir != nextDir)
                {
                    for (int x = -1; x <= 1; x++)
                        for (int y = -1; y <= 1; y++)
                            _totalGroundTiles.Add(pos + new Vector2Int(x, y));
                }
            }
        }

        // 입구 정리 (입구는 3칸 너비로 뚫음)
        RegisterEntrancePath(path[0], GetDirection(path, 0));
        RegisterEntrancePath(path[path.Count - 1], -GetDirection(path, path.Count - 1));
    }

    private void RegisterEntrancePath(Vector2Int entrancePos, Vector2Int dir)
    {
        Vector2Int sideDir = new Vector2Int(-dir.y, dir.x);
        for (int d = 0; d <= 3; d++)
        {
            // 입구는 3칸 너비로 뚫리도록 중앙과 좌우 모두 경로 등록
            _totalPathTiles.Add(entrancePos - dir * d);
            _totalPathTiles.Add(entrancePos - dir * d + sideDir);
            _totalPathTiles.Add(entrancePos - dir * d - sideDir);
        }
    }

    public void FinalizePainting()
    {
        // 1. 바닥 배치
        foreach (var gPos in _totalGroundTiles)
        {
            // 방 영역이 아닐 때만 새 Ground 타일 배치
            if (!_roomTiles.Contains(gPos)) _groundTilemap.SetTile((Vector3Int)gPos, _floorTile);
        }

        // 2. 벽 제거, Shadow 배치
        foreach (var gPos in _totalGroundTiles)
        {
            // 1칸 중앙 경로이면 모든 방해물 제거
            if (_totalPathTiles.Contains(gPos))
            {
                _wallTilemap.SetTile((Vector3Int)gPos, null);
                _shadowTilemap.SetTile((Vector3Int)gPos, null);
            }
            // 방이 아니고 중앙 경로가 아니면 (즉, 양옆 바닥이면) Shadow 배치
            else if (!_roomTiles.Contains(gPos))
            {
                _shadowTilemap.SetTile((Vector3Int)gPos, _shadowTile);
            }
        }

        // 3. 주변 벽 배치
        foreach (var gPos in _totalGroundTiles)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    Vector2Int neighbor = gPos + new Vector2Int(x, y);
                    if (!_roomTiles.Contains(neighbor) && !_totalGroundTiles.Contains(neighbor))
                    {
                        _wallTilemap.SetTile((Vector3Int)neighbor, _wallTile);
                        _shadowTilemap.SetTile((Vector3Int)neighbor, _shadowTile);
                    }
                }
            }
        }
    }

    private Vector2Int GetDirection(List<Vector2Int> path, int index)
    {
        if (path == null || path.Count < 2) return Vector2Int.up;
        if (index < path.Count - 1) return path[index + 1] - path[index];
        return path[index] - path[index - 1];
    }

    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int end, int margin)
    {
        PriorityQueueCustom<Vector2Int> openSet = new PriorityQueueCustom<Vector2Int>();
        openSet.Enqueue(start, 0);

        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float>();
        gScore[start] = 0;

        int iterations = 0;
        while (openSet.Count > 0 && iterations < 8000) 
        {
            iterations++;
            Vector2Int current = openSet.Dequeue();
            if (current == end) return ReconstructPath(cameFrom, current);

            Vector2Int lastDir = Vector2Int.zero;
            if (cameFrom.ContainsKey(current)) lastDir = current - cameFrom[current];

            foreach (Vector2Int neighbor in GetNeighbors(current))
            {
                Vector2Int nextDir = neighbor - current;
                float moveCost = CalculateCost(neighbor, margin);

                if (lastDir != Vector2Int.zero && lastDir != nextDir)
                {
                    moveCost += 25f; 
                }

                float tentativeGScore = gScore[current] + moveCost;

                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    
                    float h = Mathf.Abs(neighbor.x - end.x) + Mathf.Abs(neighbor.y - end.y);
                    float fScore = tentativeGScore + h * 1.1f; 
                    
                    openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        Debug.LogWarning($"[MapGen] A* Path failed. Using Fallback L-path from {start} to {end}");
        return CreateSimpleLPath(start, end);
    }

    private List<Vector2Int> CreateSimpleLPath(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int current = start;
        while (current.x != end.x) { path.Add(current); current.x += (int)Mathf.Sign(end.x - current.x); }
        while (current.y != end.y) { path.Add(current); current.y += (int)Mathf.Sign(end.y - current.y); }
        path.Add(end);
        return path;
    }

    private float CalculateCost(Vector2Int pos, int margin)
    {
        // [수정] 방 침범 절대 금지: 비용을 극단적으로 상향
        if (_groundTilemap.HasTile((Vector3Int)pos)) return 5000f; 
        if (_wallTilemap.HasTile((Vector3Int)pos)) return 1000f;

        for (int x = -margin; x <= margin; x++)
            for (int y = -margin; y <= margin; y++)
            {
                if (x == 0 && y == 0) continue;
                if (_groundTilemap.HasTile(new Vector3Int(pos.x + x, pos.y + y, 0))) return 20f;
            }
        return 1f;
    }

    private List<Vector2Int> GetNeighbors(Vector2Int n)
    {
        return new List<Vector2Int> {
            new Vector2Int(n.x+1, n.y), new Vector2Int(n.x-1, n.y),
            new Vector2Int(n.x, n.y+1), new Vector2Int(n.x, n.y-1)
        };
    }

    private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        List<Vector2Int> totalPath = new List<Vector2Int> { current };
        while (cameFrom.ContainsKey(current)) { current = cameFrom[current]; totalPath.Add(current); }
        totalPath.Reverse();
        return totalPath;
    }
}

public class PriorityQueueCustom<T>
{
    private List<KeyValuePair<T, float>> elements = new List<KeyValuePair<T, float>>();
    public int Count => elements.Count;
    public void Enqueue(T item, float priority) { elements.Add(new KeyValuePair<T, float>(item, priority)); }
    public T Dequeue()
    {
        int bestIndex = 0;
        for (int i = 0; i < elements.Count; i++) { if (elements[i].Value < elements[bestIndex].Value) bestIndex = i; }
        T item = elements[bestIndex].Key;
        elements.RemoveAt(bestIndex);
        return item;
    }
}
