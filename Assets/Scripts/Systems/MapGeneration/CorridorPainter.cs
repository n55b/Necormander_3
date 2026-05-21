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
    private HashSet<Vector2Int> _roomWallTiles = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _roomFloorTiles = new HashSet<Vector2Int>();

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
        _roomWallTiles.Clear();
        _roomFloorTiles.Clear();

        // 현재 배치된 모든 방의 타일 정보를 스냅샷으로 저장
        SnapshotRoomTiles(_groundTilemap, false);
        SnapshotRoomTiles(_wallTilemap, true);
    }

    private void SnapshotRoomTiles(Tilemap tm, bool isWall)
    {
        if (tm == null) return;
        tm.CompressBounds();
        BoundsInt bounds = tm.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            if (tm.HasTile(pos))
            {
                Vector3 worldPos = tm.CellToWorld(pos);
                Vector2Int gridPos = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
                if (isWall) _roomWallTiles.Add(gridPos);
                else _roomFloorTiles.Add(gridPos);
            }
        }
    }

    /// <summary>
    /// 앵커 정보를 기반으로 3칸 너비의 통로를 등록하고 입구를 특수하게 처리합니다.
    /// </summary>
    public void RegisterCorridorWithAnchors(List<Vector2Int> path, RoomAnchor startAnchor, RoomAnchor endAnchor)
    {
        if (path == null || path.Count == 0) return;

        // 1. 전체 경로 타일 등록 (중앙 1칸: 뚫림, 양옆 2칸: 그림자용 바닥)
        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int pos = path[i];
            Vector2Int dir = GetDirection(path, i);
            Vector2Int sideDir = new Vector2Int(-dir.y, dir.x);

            _totalPathTiles.Add(pos); // 중앙 경로 (모든 방해물 제거)
            
            // 3칸 너비 전체를 바닥 후보군으로 등록
            _totalGroundTiles.Add(pos);
            _totalGroundTiles.Add(pos + sideDir);
            _totalGroundTiles.Add(pos - sideDir);

            // 코너 보정 (3x3 영역 확보)
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

        // 2. 앵커 입구 특수 처리 (방 안쪽으로 깊게 뚫기)
        if (startAnchor != null) ApplyAnchorEntrance(startAnchor);
        if (endAnchor != null) ApplyAnchorEntrance(endAnchor);
    }

    private void ApplyAnchorEntrance(RoomAnchor anchor)
    {
        // WorldToCell을 사용하여 앵커가 위치한 타일의 정확한 정수 좌표 확보
        Vector3Int cellPos3 = _wallTilemap.WorldToCell(anchor.transform.position);
        Vector2Int pos = new Vector2Int(cellPos3.x, cellPos3.y);

        Vector2Int dir = anchor.direction; // 방에서 밖으로 나가는 방향
        Vector2Int sideDir = new Vector2Int(-dir.y, dir.x);

        // [핵심] 앵커 위치를 중심으로 안쪽 1칸, 바깥쪽 1칸 총 3칸 깊이와 3칸 너비를 모두 뚫음
        for (int d = -1; d <= 1; d++) // 깊이 (안쪽 -1, 중앙 0, 바깥쪽 1)
        {
            for (int s = -1; s <= 1; s++) // 너비 (좌 -1, 중앙 0, 우 1)
            {
                Vector2Int targetPos = pos + (dir * d) + (sideDir * s);
                
                // s == 0 이면 중앙 길이므로 완전 삭제 리스트(PathTile)에만 추가
                if (s == 0) _totalPathTiles.Add(targetPos);
                
                // 3x3 전체를 통로 영역(바닥) 리스트에 추가
                _totalGroundTiles.Add(targetPos);
            }
        }
    }

    public void RegisterCorridor(List<Vector2Int> path)
    {
        RegisterCorridorWithAnchors(path, null, null);
    }

    public void FinalizePainting()
    {
        // 1. 바닥 배치 (통로 전체)
        foreach (var gPos in _totalGroundTiles)
        {
            // 방의 기존 바닥이 아닌 곳(벽이었거나 빈 공간)에만 새 바닥 배치
            if (!_roomFloorTiles.Contains(gPos))
            {
                _groundTilemap.SetTile((Vector3Int)gPos, _floorTile);
            }
        }

        // 2. 벽 제거 및 통로용 Shadow 배치
        foreach (var gPos in _totalGroundTiles)
        {
            // [수정] 통로 영역(3칸 너비)의 모든 기존 벽과 그림자는 무조건 일단 제거
            _wallTilemap.SetTile((Vector3Int)gPos, null);
            _shadowTilemap.SetTile((Vector3Int)gPos, null);

            // 중앙 경로가 아니면서(즉, 통로 가장자리), 방 내부 바닥이 아닌 곳에만 그림자 배치
            if (!_totalPathTiles.Contains(gPos) && !_roomFloorTiles.Contains(gPos))
            {
                _shadowTilemap.SetTile((Vector3Int)gPos, _shadowTile);
            }
        }

        // 3. 주변 벽 배치 (통로를 감싸는 외벽)
        foreach (var gPos in _totalGroundTiles)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    Vector2Int neighbor = gPos + new Vector2Int(x, y);
                    // 이미 바닥이 깔린 곳이 아니고, 방의 기존 바닥도 아닌 곳에 벽 배치
                    if (!_totalGroundTiles.Contains(neighbor) && !_roomFloorTiles.Contains(neighbor))
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
        float cost = 1f;

        // 1. 방 침범 절대 금지: 비용을 극단적으로 상향
        if (_roomFloorTiles.Contains(pos) || _roomWallTiles.Contains(pos)) return 100000f;

        // 2. 통로 융합 및 평행 통로 방지
        if (_totalPathTiles.Contains(pos)) 
        {
            cost = 0.01f; // 기존 중앙로는 매우 선호 (병합 유도)
        }
        else if (_totalGroundTiles.Contains(pos)) 
        {
            cost = 9000f; // 기존 통로의 가장자리(그림자 영역)를 밟는 것은 극도로 회피 (평행 생성 방지)
        }

        // 3. 방 주변 마진 확보 (방 모서리 깎기 방지)
        // 사용자가 요청한 침범 방지를 위해 마진을 최소 2칸 이상으로 고려
        int checkMargin = Mathf.Max(margin, 2);
        for (int x = -checkMargin; x <= checkMargin; x++)
        {
            for (int y = -checkMargin; y <= checkMargin; y++)
            {
                if (x == 0 && y == 0) continue;
                Vector2Int neighbor = pos + new Vector2Int(x, y);
                if (_roomFloorTiles.Contains(neighbor) || _roomWallTiles.Contains(neighbor)) 
                {
                    // 방 근처로 갈수록 지수적으로 비용 증가
                    float dist = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
                    cost += (30000f / dist); 
                    break;
                }
            }
        }

        return cost;
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
