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

    private RoomInstance _roomA;
    private RoomInstance _roomB;

    public void SetCurrentRooms(RoomInstance a, RoomInstance b)
    {
        _roomA = a;
        _roomB = b;
    }

    // --- 5단계 드로잉 버퍼 및 중복 제거 캐시 추가 ---
    private readonly List<Vector3Int> _drawPositions = new List<Vector3Int>(1024);
    private readonly List<TileBase> _drawTiles = new List<TileBase>(1024);
    private readonly HashSet<Vector2Int> _outerWallTiles = new HashSet<Vector2Int>();

    // --- 2단계 비용 맵 캐시 추가 ---
    private float[,] _costMap;
    private int _costMapOffsetX;
    private int _costMapOffsetY;
    private int _costMapWidth;
    private int _costMapHeight;

    private static readonly float[,] _corridorWeightTable = new float[7, 7];

    // --- 3단계 가비지 재사용 변수 추가 ---
    private readonly Dictionary<Vector2Int, Vector2Int> _cameFrom = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Dictionary<Vector2Int, float> _gScore = new Dictionary<Vector2Int, float>();
    private readonly PriorityQueueCustom<Vector2Int> _openSet = new PriorityQueueCustom<Vector2Int>();

    private static readonly Vector2Int[] Directions = new Vector2Int[]
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    static CorridorPainter()
    {
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dy = -3; dy <= 3; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                // 반경 3칸 범위까지 강력한 가중치를 부여하되, 거리에 따라 감쇄
                _corridorWeightTable[dx + 3, dy + 3] = 300f / Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
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
                roomWeightTable[dx + 2, dy + 2] = 50f / Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
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
                Vector2Int gridPos = new Vector2Int(pos.x, pos.y);
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
            
            // 코너(꺾임점) 감지: 이전 노드의 방향과 현재 노드의 방향이 다를 때
            bool isCorner = false;
            if (i > 0)
            {
                Vector2Int dirPrev = GetDirection(path, i - 1);
                if (dirPrev != dir)
                {
                    isCorner = true;
                }
            }

            if (isCorner)
            {
                // 코너의 경우 모퉁이 중심(pos) 기준 3x3 사각형 영역 전체를 바닥으로 등록하여
                // 외벽 정렬 시 찌그러지는 현상을 방지합니다.
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        AddGroundWithDepth(pos + new Vector2Int(dx, dy), pathDepth);
                    }
                }
            }
            else
            {
                // 일반 직선 구간은 복도 폭 3칸(수직 방향)으로 등록
                AddGroundWithDepth(pos, pathDepth);
                AddGroundWithDepth(pos + sideDir, pathDepth);
                AddGroundWithDepth(pos - sideDir, pathDepth);
            }
        }

        if (startAnchor != null) ApplyAnchorEntrance(startAnchor, pathDepth);
        if (endAnchor != null) ApplyAnchorEntrance(endAnchor, pathDepth);
    }

    private void AddGroundWithDepth(Vector2Int pos, int depth, bool force = false)
    {
        // force가 false일 때는 방의 기존 벽 타일이나 바닥 타일 좌표인 경우 복도 생성을 스킵합니다.
        if (!force && (_roomWallTiles.Contains(pos) || _roomFloorTiles.Contains(pos)))
        {
            return;
        }

        if (_totalGroundTiles.Add(pos))
        {
            SetCostInMap(pos.x, pos.y, 999999f);
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = -3; dy <= 3; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    AddCostInMap(pos.x + dx, pos.y + dy, _corridorWeightTable[dx + 3, dy + 3]);
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
            // 방 안쪽(d = -1)과 벽 경계(d = 0)는 문 크기인 1칸 너비(s = 0)만 뚫고,
            // 방 바깥 복도 시작 지점(d = 1)에서만 복도 3칸 너비(s = -1 ~ 1) 전체를 뚫습니다.
            int startS = (d <= 0) ? 0 : -1;
            int endS = (d <= 0) ? 0 : 1;

            for (int s = startS; s <= endS; s++)
            {
                Vector2Int targetPos = pos + (dir * d) + (sideDir * s);
                if (s == 0) { _totalPathTiles.Add(targetPos); _tileDepths[targetPos] = depth; }
                
                // 문 구멍 중앙 라인(s == 0)은 force = true로 문을 뚫고, 
                // 날개 확장부(s != 0)는 force = false로 설정해 방 벽 훼손을 방지합니다.
                bool force = (s == 0);
                AddGroundWithDepth(targetPos, depth, force);
            }
        }
    }

    public HashSet<Vector2Int> GetTotalGroundTiles() => _totalGroundTiles;

    public void FinalizePainting()
    {
        // 1. Ground Tilemap 일괄 그리기
        _drawPositions.Clear();
        _drawTiles.Clear();
        foreach (var gPos in _totalGroundTiles)
        {
            if (!_roomFloorTiles.Contains(gPos))
            {
                _drawPositions.Add((Vector3Int)gPos);
                _drawTiles.Add(_floorTile);
            }
        }
        if (_drawPositions.Count > 0)
        {
            _groundTilemap.SetTiles(_drawPositions.ToArray(), _drawTiles.ToArray());
        }

        // 2. Wall Tilemap 및 Shadow Tilemap 복도 바닥 영역 정리 및 그림자 일괄 그리기
        // - 복도 바닥 영역에서는 벽을 완전히 지우고(null), 그림자 여부에 따라 지우거나 그림자 배치
        _drawPositions.Clear();
        _drawTiles.Clear();
        foreach (var gPos in _totalGroundTiles)
        {
            _drawPositions.Add((Vector3Int)gPos);
            _drawTiles.Add(null);
        }
        if (_drawPositions.Count > 0)
        {
            _wallTilemap.SetTiles(_drawPositions.ToArray(), _drawTiles.ToArray());
        }

        _drawPositions.Clear();
        _drawTiles.Clear();
        foreach (var gPos in _totalGroundTiles)
        {
            _drawPositions.Add((Vector3Int)gPos);
            if (!_totalPathTiles.Contains(gPos) && !_roomFloorTiles.Contains(gPos))
            {
                _drawTiles.Add(_shadowTile);
            }
            else
            {
                _drawTiles.Add(null);
            }
        }
        if (_drawPositions.Count > 0)
        {
            _shadowTilemap.SetTiles(_drawPositions.ToArray(), _drawTiles.ToArray());
        }

        // 3. 외벽 탐색 및 배치 (중복 타일 처리를 방지하기 위해 먼저 HashSet에 외벽 위치를 고유하게 수집)
        _outerWallTiles.Clear();
        foreach (var gPos in _totalGroundTiles)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    Vector2Int neighbor = gPos + new Vector2Int(x, y);
                    if (!_totalGroundTiles.Contains(neighbor) && !_roomFloorTiles.Contains(neighbor))
                    {
                        _outerWallTiles.Add(neighbor);
                    }
                }
            }
        }

        // 4. 수집된 외벽 위치에 벽 및 그림자 타일 일괄 그리기
        _drawPositions.Clear();
        _drawTiles.Clear();
        foreach (var wPos in _outerWallTiles)
        {
            _drawPositions.Add((Vector3Int)wPos);
            _drawTiles.Add(_wallTile);
        }
        if (_drawPositions.Count > 0)
        {
            _wallTilemap.SetTiles(_drawPositions.ToArray(), _drawTiles.ToArray());
        }

        _drawPositions.Clear();
        _drawTiles.Clear();
        foreach (var wPos in _outerWallTiles)
        {
            _drawPositions.Add((Vector3Int)wPos);
            _drawTiles.Add(_shadowTile);
        }
        if (_drawPositions.Count > 0)
        {
            _shadowTilemap.SetTiles(_drawPositions.ToArray(), _drawTiles.ToArray());
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
        _openSet.Clear();
        _openSet.Enqueue(start, 0);
        _cameFrom.Clear();
        _gScore.Clear();
        _gScore[start] = 0;

        int iterations = 0;
        while (_openSet.Count > 0 && iterations < 12000) 
        {
            iterations++;
            Vector2Int current = _openSet.Dequeue();
            if (current == end) return ReconstructPath(_cameFrom, current);

            Vector2Int lastDir = _cameFrom.ContainsKey(current) ? current - _cameFrom[current] : Vector2Int.zero;

            for (int i = 0; i < 4; i++)
            {
                Vector2Int neighbor = current + Directions[i];
                float moveCost = GetCostFromMap(neighbor.x, neighbor.y);
                if (moveCost >= 900000f) continue;

                if (IsOverlapWithRooms(neighbor, start, end)) continue;
                if (IsOverlapWithCorridors(neighbor)) continue;

                if (lastDir != Vector2Int.zero && lastDir != (neighbor - current)) moveCost += 2000f; 

                float tentativeGScore = _gScore[current] + moveCost;
                if (!_gScore.TryGetValue(neighbor, out float existingGScore) || tentativeGScore < existingGScore)
                {
                    _cameFrom[neighbor] = current; 
                    _gScore[neighbor] = tentativeGScore;
                    float h = Mathf.Abs(neighbor.x - end.x) + Mathf.Abs(neighbor.y - end.y);
                    _openSet.Enqueue(neighbor, tentativeGScore + h * 1.1f);
                }
            }
        }
        return null;
    }

    private bool IsOverlapWithCorridors(Vector2Int pos)
    {
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                Vector2Int checkPos = new Vector2Int(pos.x + dx, pos.y + dy);
                if (_totalGroundTiles.Contains(checkPos))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool IsOverlapWithRooms(Vector2Int pos, Vector2Int start, Vector2Int end)
    {
        // 복도 5칸 너비 영역(외벽 포함)이 방의 기존 바닥/벽 타일과 오버랩되는지 체크
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                Vector2Int checkPos = new Vector2Int(pos.x + dx, pos.y + dy);
                if (_roomFloorTiles.Contains(checkPos) || _roomWallTiles.Contains(checkPos))
                {
                    // 현재 연결하는 방 A와 방 B의 타일인 경우는 겹침 허용
                    if ((_roomA != null && _roomA.ContainsCell(checkPos)) || (_roomB != null && _roomB.ContainsCell(checkPos)))
                    {
                        // 단, 출발점(start) 또는 목적점(end) 근처에서만 겹침을 허용하고,
                        // 복도의 중간 부분에서는 자신이 연결하는 방이라 할지라도 벽이나 바닥을 침범하지 못하도록 제어합니다.
                        // (방 출입을 위한 앵커 부근 5칸 맨해튼 거리 이내만 겹침 허용)
                        int distToStart = Mathf.Abs(pos.x - start.x) + Mathf.Abs(pos.y - start.y);
                        int distToEnd = Mathf.Abs(pos.x - end.x) + Mathf.Abs(pos.y - end.y);
                        if (distToStart <= 3 || distToEnd <= 3)
                        {
                            continue;
                        }
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private float CalculateCost(Vector2Int pos, int margin, int currentPathDepth)
    {
        return GetCostFromMap(pos.x, pos.y);
    }

    private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current) 
    { 
        List<Vector2Int> path = new List<Vector2Int>(128) { current }; 
        while (cameFrom.ContainsKey(current)) 
        { 
            current = cameFrom[current]; 
            path.Add(current); 
        } 
        path.Reverse(); 
        return path; 
    }
}

public class PriorityQueueCustom<T>
{
    private List<KeyValuePair<T, float>> _heap = new List<KeyValuePair<T, float>>();

    public int Count => _heap.Count;

    public void Clear()
    {
        _heap.Clear();
    }

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
