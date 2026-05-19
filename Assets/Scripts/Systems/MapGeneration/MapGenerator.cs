using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MapGenerator : MonoBehaviour
{
    public static MapGenerator Instance;

    [Header("Data Settings")]
    [SerializeField] private MapGenerationDataSO generationData;
    [SerializeField] private RoomPrefabDataSO prefabData;

    [Header("Global Tilemap References")]
    [SerializeField] private Tilemap globalGroundTilemap;
    [SerializeField] private Tilemap globalWallTilemap;
    [SerializeField] private Tilemap globalShadowTilemap;

    private List<RoomInstance> _rooms = new List<RoomInstance>();
    private CorridorPainter _painter;
    private bool _isGenerating = false;

    private void Awake()
    {
        Instance = this;
        _painter = gameObject.AddComponent<CorridorPainter>();
    }

    [ContextMenu("Generate Map")]
    public void GenerateMap()
    {
        if (_isGenerating) return;
        if (globalGroundTilemap == null || globalWallTilemap == null || globalShadowTilemap == null) return;
        StartCoroutine(GenerationRoutine());
    }

    private IEnumerator GenerationRoutine()
    {
        _isGenerating = true;
        SetupTilemapLayers();
        ClearExistingMap();
        SetGlobalCollidersActive(false); 

        SpawnRooms();
        
        foreach (var room in _rooms)
        {
            Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Vector2 explodeDir = ((Vector2)room.transform.position + Random.insideUnitCircle * 0.5f).normalized;
                rb.AddForce(explodeDir * (generationData.spreadingForce * 0.7f), ForceMode2D.Impulse);
            }
        }

        yield return StartCoroutine(PhysicsSpreadingRoutine());

        FinalizeRoomPositions();
        MergeAllRoomTiles();
        ConnectRooms();

        if (globalWallTilemap != null)
        {
            EnsureGlobalWallCollider();
            SetGlobalCollidersActive(true);
        }

        AssignSpecialRooms();

        _isGenerating = false;
        Debug.Log("<color=green>[MapGenerator]</color> Map Generation Completed.");
    }

    /// <summary>
    /// [추가] 각 타일맵의 Layer와 Sorting Layer를 요구사항에 맞게 강제 설정합니다.
    /// </summary>
    private void SetupTilemapLayers()
    {
        ConfigureTilemap(globalGroundTilemap, "Ground", "Ground");
        ConfigureTilemap(globalWallTilemap, "Wall", "Wall");
        ConfigureTilemap(globalShadowTilemap, "Shadow", "Shadow");
    }

    private void ConfigureTilemap(Tilemap tm, string layerName, string sortingLayerName)
    {
        if (tm == null) return;
        
        // 1. Layer 설정
        int layer = LayerMask.NameToLayer(layerName);
        if (layer != -1) tm.gameObject.layer = layer;
        else Debug.LogWarning($"[MapGenerator] '{layerName}' 레이어가 프로젝트에 존재하지 않습니다.");

        // 2. Sorting Layer 설정 (TilemapRenderer)
        var renderer = tm.GetComponent<TilemapRenderer>();
        if (renderer != null)
        {
            renderer.sortingLayerName = sortingLayerName;
        }
    }

    private void SetGlobalCollidersActive(bool active)
    {
        if (globalWallTilemap != null)
        {
            var col = globalWallTilemap.GetComponent<TilemapCollider2D>();
            if (col != null) col.enabled = active;
        }
    }

    private void EnsureGlobalWallCollider()
    {
        if (globalWallTilemap == null) return;
        GameObject obj = globalWallTilemap.gameObject;
        
        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer != -1) obj.layer = wallLayer;

        // 1. Rigidbody2D - 가장 안전한 방식으로 접근
        Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();
        if (rb == null) rb = obj.AddComponent<Rigidbody2D>();
        
        if (rb != null)
        {
            try { rb.bodyType = RigidbodyType2D.Static; }
            catch (UnityEngine.MissingComponentException) { rb = obj.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Static; }
        }

        // 2. TilemapCollider2D
        TilemapCollider2D tileCol = obj.GetComponent<TilemapCollider2D>();
        if (tileCol == null) tileCol = obj.AddComponent<TilemapCollider2D>();

        // 3. CompositeCollider2D
        CompositeCollider2D composite = obj.GetComponent<CompositeCollider2D>();
        if (composite == null) composite = obj.AddComponent<CompositeCollider2D>();
        
        if (composite != null && tileCol != null)
        {
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            tileCol.usedByComposite = true;
        }
    }

    private void ClearExistingMap()
    {
        foreach (var room in _rooms)
        {
            if (room != null)
            {
                room.name = "DELETING";
                Destroy(room.gameObject);
            }
        }
        _rooms.Clear();
        if (globalGroundTilemap != null) globalGroundTilemap.ClearAllTiles();
        if (globalWallTilemap != null) globalWallTilemap.ClearAllTiles();
        if (globalShadowTilemap != null) globalShadowTilemap.ClearAllTiles();
    }

    private void SpawnRooms()
    {
        CreateRoom(RoomType.Spawn);
        for (int i = 0; i < generationData.shopCount; i++) CreateRoom(RoomType.Shop);
        for (int i = 0; i < generationData.rewardCount; i++) CreateRoom(RoomType.Reward);
        for (int i = 0; i < generationData.eliteCount; i++) CreateRoom(RoomType.Elite);
        int currentCount = _rooms.Count;
        int remaining = Mathf.Max(generationData.minNormalRooms, generationData.totalRoomCount - currentCount);
        for (int i = 0; i < remaining; i++) CreateRoom(RoomType.Normal);
    }

    private void CreateRoom(RoomType type)
    {
        GameObject prefab = prefabData.GetRandomPrefab(type);
        if (prefab == null) return;
        
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Random.Range(generationData.minSpawnRadius, generationData.maxSpawnRadius);
        
        // [수정] 초기 소환 시 Y축에 가중치를 주어 가로 타원 현상 방지
        Vector2 spawnPos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 1.3f) * radius;

        GameObject roomObj = Instantiate(prefab, (Vector3)spawnPos, Quaternion.identity, transform);
        RoomInstance room = roomObj.GetComponent<RoomInstance>() ?? roomObj.AddComponent<RoomInstance>();
        room.Initialize(type);
        _rooms.Add(room);
    }

    private IEnumerator PhysicsSpreadingRoutine()
    {
        int iterations = 0, maxIter = 300; 
        foreach (var room in _rooms)
        {
            Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.sleepMode = RigidbodySleepMode2D.NeverSleep; rb.linearDamping = 3.5f; }
        }
        while (iterations < maxIter)
        {
            foreach (var room in _rooms)
            {
                Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
                if (rb == null) continue;
                Vector2 dir = (Vector2)room.transform.position;
                if (dir.magnitude < 0.1f) dir = Random.insideUnitCircle.normalized;
                
                // 물리력에도 Y축 보정 가미
                Vector2 combinedDir = (dir + (Vector2)Random.insideUnitCircle * 0.4f);
                combinedDir.y *= 1.5f; 
                
                rb.AddForce(combinedDir.normalized * generationData.spreadingForce, ForceMode2D.Force);
            }
            iterations++;
            yield return new WaitForFixedUpdate();
        }
        foreach (var room in _rooms)
        {
            Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
    }

    private void FinalizeRoomPositions()
    {
        foreach (var room in _rooms)
        {
            room.SnapToGrid(generationData.gridUnit);
            room.CleanupPhysics();
        }
    }

    private void MergeAllRoomTiles()
    {
        foreach (var room in _rooms) room.MergeTilesToGlobal(globalGroundTilemap, globalWallTilemap, globalShadowTilemap);
    }

    private void ConnectRooms()
    {
        if (_rooms.Count < 2) return;
        _painter.Init(globalGroundTilemap, globalWallTilemap, globalShadowTilemap, generationData.floorTile, generationData.wallTile, generationData.shadowTile);

        List<Edge> allEdges = new List<Edge>();
        for (int i = 0; i < _rooms.Count; i++)
            for (int j = i + 1; j < _rooms.Count; j++)
                allEdges.Add(new Edge(_rooms[i], _rooms[j]));

        allEdges.Sort((a, b) => a.distance.CompareTo(b.distance));

        List<Edge> mstEdges = new List<Edge>();
        HashSet<RoomInstance> reached = new HashSet<RoomInstance> { _rooms[0] };
        Dictionary<RoomInstance, List<RoomInstance>> adjacency = new Dictionary<RoomInstance, List<RoomInstance>>();
        foreach (var r in _rooms) adjacency[r] = new List<RoomInstance>();

        List<Edge> remainingPool = new List<Edge>(allEdges);
        while (reached.Count < _rooms.Count)
        {
            Edge bestEdge = null; float minDist = float.MaxValue;
            foreach (var edge in remainingPool)
            {
                if (reached.Contains(edge.a) != reached.Contains(edge.b))
                {
                    if (edge.distance < minDist) { minDist = edge.distance; bestEdge = edge; }
                }
            }
            if (bestEdge != null)
            {
                mstEdges.Add(bestEdge);
                reached.Add(bestEdge.a); reached.Add(bestEdge.b);
                adjacency[bestEdge.a].Add(bestEdge.b); adjacency[bestEdge.b].Add(bestEdge.a);
                remainingPool.Remove(bestEdge);
            }
            else break;
        }

        List<Edge> extraEdges = new List<Edge>();
        foreach (var edge in remainingPool)
        {
            if (edge.distance > 40f) continue;
            int graphDist = GetGraphDistance(edge.a, edge.b, adjacency);
            if (graphDist >= 3 && Random.value < 0.4f) 
            {
                extraEdges.Add(edge);
                adjacency[edge.a].Add(edge.b); adjacency[edge.b].Add(edge.a);
            }
        }

        foreach (var edge in mstEdges) DrawCorridorBetweenRooms(edge.a, edge.b);
        foreach (var edge in extraEdges) DrawCorridorBetweenRooms(edge.a, edge.b);

        // [추가] 모든 통로 등록 완료 후 최종 렌더링
        _painter.FinalizePainting();
    }

    private int GetGraphDistance(RoomInstance start, RoomInstance target, Dictionary<RoomInstance, List<RoomInstance>> adj)
    {
        Queue<(RoomInstance, int)> queue = new Queue<(RoomInstance, int)>();
        queue.Enqueue((start, 0));
        HashSet<RoomInstance> visited = new HashSet<RoomInstance> { start };
        while (queue.Count > 0)
        {
            var (curr, dist) = queue.Dequeue();
            if (curr == target) return dist;
            foreach (var neighbor in adj[curr]) { if (!visited.Contains(neighbor)) { visited.Add(neighbor); queue.Enqueue((neighbor, dist + 1)); } }
        }
        return 999;
    }

    private class Edge
    {
        public RoomInstance a; public RoomInstance b; public float distance;
        public Edge(RoomInstance a, RoomInstance b) { this.a = a; this.b = b; distance = Vector2.Distance(a.transform.position, b.transform.position); }
    }

    private void DrawCorridorBetweenRooms(RoomInstance a, RoomInstance b)
    {
        (Vector2Int exitA, Vector2Int dirA) = GetBestExitPoint(a, b.transform.position + (Vector3)b.centerOffset);
        (Vector2Int exitB, Vector2Int dirB) = GetBestExitPoint(b, a.transform.position + (Vector3)a.centerOffset);

        List<Vector2Int> fullPath = new List<Vector2Int>();

        Vector2Int currentA = exitA;
        fullPath.Add(exitA); 
        for (int i = 0; i < generationData.corridorStraightLength; i++) { currentA += dirA; fullPath.Add(currentA); }

        Vector2Int entrancePointB = exitB;
        for (int i = 0; i < generationData.corridorStraightLength; i++) { entrancePointB += dirB; }

        List<Vector2Int> aStarPath = _painter.FindPath(currentA, entrancePointB, generationData.corridorAvoidMargin);
        if (aStarPath != null)
        {
            fullPath.AddRange(aStarPath);
            Vector2Int finalStep = entrancePointB;
            for (int i = 0; i < generationData.corridorStraightLength; i++) { finalStep -= dirB; fullPath.Add(finalStep); }
            fullPath.Add(exitB); 

            // [수정] 즉시 그리지 않고 등록만 수행
            _painter.RegisterCorridor(fullPath);
        }
    }

    private (Vector2Int point, Vector2Int direction) GetBestExitPoint(RoomInstance room, Vector3 targetWorldPos)
    {
        Vector2Int center = Vector2Int.RoundToInt((Vector2)room.transform.position + room.centerOffset);
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        
        Vector2Int bestPoint = center;
        Vector2Int bestDir = dirs[0];
        float minScore = float.MaxValue;

        foreach (var d in dirs)
        {
            // 수직 방향 (너비 체크용)
            Vector2Int sideDir = new Vector2Int(-d.y, d.x);
            
            Vector2Int current = center;
            bool foundValidSegment = false;

            for (int i = 0; i < 50; i++)
            {
                current += d;
                Vector3Int cellPos = globalWallTilemap.WorldToCell((Vector3)(Vector2)current);
                
                if (globalWallTilemap.HasTile(cellPos)) 
                {
                    // 3칸 너비가 모두 벽인지 확인 (꼭짓점 회피 로직 포함)
                    bool l = globalWallTilemap.HasTile(cellPos + (Vector3Int)sideDir);
                    bool r = globalWallTilemap.HasTile(cellPos - (Vector3Int)sideDir);
                    
                    // 추가 마진 확인 (꼭짓점에서 최소 1칸 더 여유: 총 5칸 직선 필요)
                    bool l2 = globalWallTilemap.HasTile(cellPos + (Vector3Int)sideDir * 2);
                    bool r2 = globalWallTilemap.HasTile(cellPos - (Vector3Int)sideDir * 2);

                    if (l && r && l2 && r2)
                    {
                        foundValidSegment = true;
                        break;
                    }
                    else
                    {
                        // 벽을 만나긴 했으나 3칸 너비가 안됨 -> 이 방향은 포기 (보통 방의 끝/꼭짓점임)
                        break; 
                    }
                }
            }

            if (foundValidSegment)
            {
                float distToTarget = Vector2.Distance((Vector2)current, (Vector2)targetWorldPos);
                Vector2 toTargetDir = ((Vector2)targetWorldPos - (Vector2)center).normalized;
                float dot = Vector2.Dot(toTargetDir, (Vector2)d);
                float score = distToTarget - (dot * 10f); 
                
                if (score < minScore) { minScore = score; bestPoint = current; bestDir = d; }
            }
        }
        return (bestPoint, bestDir);
    }

    private void AssignSpecialRooms()
    {
        if (_rooms.Count == 0) return;
        RoomInstance spawnRoom = _rooms.Find(r => r.roomType == RoomType.Spawn) ?? _rooms[0];
        float maxDist = -1f; RoomInstance farthestRoom = null;
        foreach (var room in _rooms)
        {
            if (room == spawnRoom) continue;
            float d = Vector2.Distance(spawnRoom.transform.position, room.transform.position);
            if (d > maxDist) { maxDist = d; farthestRoom = room; }
        }
        if (farthestRoom != null) farthestRoom.roomType = RoomType.Boss;
    }
}
