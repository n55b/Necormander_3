using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Linq;

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

    private List<RoomInstance> _allRooms = new List<RoomInstance>();
    private HashSet<RoomInstance> _reachedRooms = new HashSet<RoomInstance>();
    private Dictionary<RoomInstance, List<RoomInstance>> _masterAdjacency = new Dictionary<RoomInstance, List<RoomInstance>>();
    private CorridorPainter _painter;
    private GameObject _tempObstacle; // 맵 생성 중에만 사용할 임시 장애물
    private bool _isGenerating = false;
    private int _currentPhaseIndex = 0;

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
        StartCoroutine(GenerationSequence());
    }

    private IEnumerator GenerationSequence()
    {
        _isGenerating = true;
        _currentPhaseIndex = 0;
        SetupTilemapLayers();
        ClearExistingMap();

        int totalSpecials = generationData.shopCount + generationData.rewardCount + generationData.eliteCount;
        int normalCount = Mathf.Max(generationData.minNormalRooms, generationData.totalRoomCount - 1 - totalSpecials);

        // Phase 1: Core
        yield return StartCoroutine(RunPhase(new List<RoomType> { RoomType.Spawn, RoomType.Normal, RoomType.Normal }));

        // Phase 2: Expansion 1
        _currentPhaseIndex++;
        List<RoomType> phase2 = new List<RoomType>();
        for (int i = 0; i < generationData.shopCount; i++) phase2.Add(RoomType.Shop);
        int eliteHalf = generationData.eliteCount / 2;
        for (int i = 0; i < eliteHalf; i++) phase2.Add(RoomType.Elite);
        for (int i = 0; i < Mathf.RoundToInt(normalCount * 0.4f); i++) phase2.Add(RoomType.Normal);
        if (phase2.Count > 0) yield return StartCoroutine(RunPhase(phase2));

        // Phase 3: Expansion 2
        _currentPhaseIndex++;
        List<RoomType> phase3 = new List<RoomType>();
        int eliteRest = generationData.eliteCount - eliteHalf;
        for (int i = 0; i < eliteRest; i++) phase3.Add(RoomType.Elite);
        int currentNormal = _allRooms.Count(r => r.roomType == RoomType.Normal);
        for (int i = 0; i < (normalCount - currentNormal); i++) phase3.Add(RoomType.Normal);
        if (phase3.Count > 0) yield return StartCoroutine(RunPhase(phase3));

        // Phase 4: Strict (Reward)
        _currentPhaseIndex++;
        List<RoomType> phase4 = new List<RoomType>();
        for (int i = 0; i < generationData.rewardCount; i++) phase4.Add(RoomType.Reward);
        if (phase4.Count > 0) yield return StartCoroutine(RunPhase(phase4));

        AssignSpecialRooms();
        DumpMapToLog();

        // [핵심] 생성 완료 후 임시 장애물 제거
        if (_tempObstacle != null) Destroy(_tempObstacle);

        _isGenerating = false;
        Debug.Log("<color=green>[MapGenerator]</color> Phased Map Generation Completed.");
    }

    private IEnumerator RunPhase(List<RoomType> types)
    {
        List<RoomInstance> phaseRooms = new List<RoomInstance>();
        foreach (var type in types)
        {
            RoomInstance room = CreateRoom(type);
            if (room != null)
            {
                phaseRooms.Add(room);
                _allRooms.Add(room);
                if (!_masterAdjacency.ContainsKey(room)) _masterAdjacency[room] = new List<RoomInstance>();
            }
        }

        yield return StartCoroutine(PhysicsSpreadingRoutine(phaseRooms));

        foreach (var room in phaseRooms)
        {
            room.SnapToGrid(generationData.gridUnit);
            room.CleanupPhysics();
            room.MergeTilesToGlobal(globalGroundTilemap, globalWallTilemap, globalShadowTilemap);
        }

        // [수정] 맵의 바깥 테두리를 감싸는 단일 사각형 콜라이더 업데이트
        UpdateGlobalBoundingObstacle();
        
        ConnectUnreachedRooms();
        yield return null;
    }

    private RoomInstance CreateRoom(RoomType type)
    {
        GameObject prefab = prefabData.GetRandomPrefab(type);
        if (prefab == null) return null;

        // 스폰 지점은 중앙 근처로 유지 (사용자 요청: 1f 반경)
        float spawnRadius = (type == RoomType.Spawn) ? 0f : 1f;
        Vector2 spawnPos = Random.insideUnitCircle.normalized * spawnRadius;

        GameObject roomObj = Instantiate(prefab, (Vector3)spawnPos, Quaternion.identity, transform);
        RoomInstance room = roomObj.GetComponent<RoomInstance>() ?? roomObj.AddComponent<RoomInstance>();
        room.Initialize(type);
        
        if (type == RoomType.Spawn)
        {
            Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
            if (rb != null) rb.bodyType = RigidbodyType2D.Static;
            _reachedRooms.Add(room);
            if (!_masterAdjacency.ContainsKey(room)) _masterAdjacency[room] = new List<RoomInstance>();
        }
        return room;
    }

    private IEnumerator PhysicsSpreadingRoutine(List<RoomInstance> activeRooms)
    {
        int iterations = 0, maxIter = 200;
        foreach (var room in activeRooms)
        {
            Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.sleepMode = RigidbodySleepMode2D.NeverSleep; rb.linearDamping = 2f; }
        }

        while (iterations < maxIter)
        {
            foreach (var room in activeRooms)
            {
                Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
                if (rb == null || rb.bodyType == RigidbodyType2D.Static) continue;

                Vector2 pos = (Vector2)room.transform.position;
                Vector2 pushDir = pos.sqrMagnitude < 0.01f ? Random.insideUnitCircle.normalized : pos.normalized;
                
                // SO의 spreadingForce를 정직하게 사용
                rb.AddForce(pushDir * generationData.spreadingForce, ForceMode2D.Force);
            }
            iterations++;
            yield return new WaitForFixedUpdate();
        }
        foreach (var room in activeRooms) { Rigidbody2D rb = room.GetComponent<Rigidbody2D>(); if (rb != null && rb.bodyType != RigidbodyType2D.Static) rb.linearVelocity = Vector2.zero; }
    }

    private void ConnectUnreachedRooms()
    {
        _painter.Init(globalGroundTilemap, globalWallTilemap, globalShadowTilemap, generationData.floorTile, generationData.wallTile, generationData.shadowTile);
        
        List<RoomInstance> unreached = _allRooms.Where(r => !_reachedRooms.Contains(r) && r.roomType != RoomType.Reward).ToList();
        List<RoomInstance> rewardRooms = _allRooms.Where(r => r.roomType == RoomType.Reward && !_reachedRooms.Contains(r)).ToList();

        bool changed = true;
        while (changed && unreached.Count > 0)
        {
            changed = false;
            var candidates = (from r in _reachedRooms
                              from u in unreached
                              let dist = Vector2.Distance(r.transform.position, u.transform.position)
                              orderby dist
                              select new { reached = r, unreached = u }).ToList();

            foreach (var c in candidates)
            {
                if (!unreached.Contains(c.unreached)) continue;
                if ((c.unreached.roomType == RoomType.Shop || c.unreached.roomType == RoomType.Elite) && c.reached.roomType == RoomType.Spawn)
                    continue;

                if (DrawCorridorBetweenRooms(c.reached, c.unreached, 0))
                {
                    _reachedRooms.Add(c.unreached);
                    unreached.Remove(c.unreached);
                    _masterAdjacency[c.reached].Add(c.unreached);
                    _masterAdjacency[c.unreached].Add(c.reached);
                    changed = true;
                    break; 
                }
            }
        }

        foreach (var reward in rewardRooms)
        {
            UpdateAllRoomDepths();
            var validReached = _reachedRooms.Where(r => r.debugDepth != -1).ToList();
            if (validReached.Count == 0) continue;
            int maxD = validReached.Max(r => r.debugDepth);
            var deepNodes = validReached.Where(r => r.debugDepth >= maxD).OrderBy(r => Vector2.Distance(r.transform.position, reward.transform.position)).ToList();
            foreach (var p in deepNodes)
            {
                if (DrawCorridorBetweenRooms(p, reward, 0)) { _reachedRooms.Add(reward); _masterAdjacency[p].Add(reward); _masterAdjacency[reward].Add(p); break; }
            }
        }

        _painter.FinalizePainting();
        UpdateAllRoomDepths();
    }

    private void UpdateAllRoomDepths()
    {
        RoomInstance spawn = _allRooms.Find(r => r.roomType == RoomType.Spawn);
        if (spawn == null) return;
        foreach (var r in _allRooms) r.debugDepth = -1;
        Queue<RoomInstance> q = new Queue<RoomInstance>();
        q.Enqueue(spawn);
        spawn.debugDepth = 0;
        while (q.Count > 0)
        {
            RoomInstance curr = q.Dequeue();
            if (!_masterAdjacency.ContainsKey(curr)) continue;
            foreach (var neighbor in _masterAdjacency[curr])
            {
                if (neighbor.debugDepth == -1) { neighbor.debugDepth = curr.debugDepth + 1; q.Enqueue(neighbor); }
            }
        }
    }

    private void UpdateGlobalBoundingObstacle()
    {
        // [핵심 로직] 현재 생성된 모든 방을 포함하는 단일 사각형 장애물 구축
        if (_allRooms.Count == 0) return;

        if (_tempObstacle == null)
        {
            _tempObstacle = new GameObject("MapBoundsObstacle");
            _tempObstacle.transform.SetParent(transform);
            var rb = _tempObstacle.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            _tempObstacle.AddComponent<BoxCollider2D>();
        }

        // 전체 방의 범위를 계산 (Bounds)
        float minX = _allRooms.Min(r => r.transform.position.x - r.roomSize.x * 0.5f);
        float maxX = _allRooms.Max(r => r.transform.position.x + r.roomSize.x * 0.5f);
        float minY = _allRooms.Min(r => r.transform.position.y - r.roomSize.y * 0.5f);
        float maxY = _allRooms.Max(r => r.transform.position.y + r.roomSize.y * 0.5f);

        BoxCollider2D box = _tempObstacle.GetComponent<BoxCollider2D>();
        box.size = new Vector2(maxX - minX + 4f, maxY - minY + 4f); // 여유 마진 4유닛 추가
        box.offset = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        Physics2D.SyncTransforms();
    }

    private void ClearExistingMap() 
    { 
        foreach (var room in _allRooms) if (room != null) Destroy(room.gameObject); 
        _allRooms.Clear(); _reachedRooms.Clear(); _masterAdjacency.Clear();
        if (_tempObstacle != null) Destroy(_tempObstacle);
        if (globalGroundTilemap != null) globalGroundTilemap.ClearAllTiles(); 
        if (globalWallTilemap != null) globalWallTilemap.ClearAllTiles(); 
        if (globalShadowTilemap != null) globalShadowTilemap.ClearAllTiles(); 
    }

    private bool DrawCorridorBetweenRooms(RoomInstance a, RoomInstance b, int pathDepth)
    {
        var availableA = a.anchors.Where(an => !an.isUsed).ToList();
        var availableB = b.anchors.Where(an => !an.isUsed).ToList();
        if (availableA.Count == 0 || availableB.Count == 0) return false;
        RoomAnchor bestA = null, bestB = null; float minDist = float.MaxValue;
        foreach (var anA in availableA) foreach (var anB in availableB) { float d = Vector2.Distance(anA.transform.position, anB.transform.position); if (d < minDist) { minDist = d; bestA = anA; bestB = anB; } }
        if (bestA == null || bestB == null) return false;
        Vector3Int cA = globalWallTilemap.WorldToCell(bestA.transform.position), cB = globalWallTilemap.WorldToCell(bestB.transform.position);
        Vector2Int exitA = new Vector2Int(cA.x, cA.y), exitB = new Vector2Int(cB.x, cB.y);
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int curA = exitA; path.Add(exitA); for (int i = 0; i < generationData.corridorStraightLength; i++) { curA += bestA.direction; path.Add(curA); }
        Vector2Int entB = exitB; for (int i = 0; i < generationData.corridorStraightLength; i++) entB += bestB.direction;
        List<Vector2Int> astar = _painter.FindPath(curA, entB, generationData.corridorAvoidMargin, pathDepth);
        if (astar != null) { 
            for (int i = 1; i < astar.Count; i++) path.Add(astar[i]); 
            Vector2Int fin = entB; for (int i = 0; i < generationData.corridorStraightLength; i++) { fin -= bestB.direction; path.Add(fin); } 
            if (path.Count > 0 && path.Last() != exitB) path.Add(exitB); 
            for (int i = path.Count - 1; i > 0; i--) if (path[i] == path[i - 1]) path.RemoveAt(i); 
            _painter.RegisterCorridorWithAnchors(path, bestA, bestB, pathDepth); 
            bestA.isUsed = true; bestB.isUsed = true; 
            return true;
        }
        return false;
    }

    private void SetupTilemapLayers() { ConfigureTilemap(globalGroundTilemap, "Ground", "Ground"); ConfigureTilemap(globalWallTilemap, "Wall", "Wall"); ConfigureTilemap(globalShadowTilemap, "Shadow", "Shadow"); }
    private void ConfigureTilemap(Tilemap tm, string layerName, string sortingLayerName) { if (tm == null) return; int layer = LayerMask.NameToLayer(layerName); if (layer != -1) tm.gameObject.layer = layer; var renderer = tm.GetComponent<TilemapRenderer>(); if (renderer != null) renderer.sortingLayerName = sortingLayerName; }
    private void SetGlobalCollidersActive(bool active) { }
    private void AssignSpecialRooms() { }

    private void DumpMapToLog()
    {
        if (globalGroundTilemap == null || globalWallTilemap == null) return;
        globalGroundTilemap.CompressBounds(); globalWallTilemap.CompressBounds();
        BoundsInt bounds = globalGroundTilemap.cellBounds; BoundsInt wallBounds = globalWallTilemap.cellBounds;
        int xMin = Mathf.Min(bounds.xMin, wallBounds.xMin), xMax = Mathf.Max(bounds.xMax, wallBounds.xMax), yMin = Mathf.Min(bounds.yMin, wallBounds.yMin), yMax = Mathf.Max(bounds.yMax, wallBounds.yMax);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"Map Bounds: X({xMin} to {xMax}), Y({yMin} to {yMax})");
        sb.AppendLine("Legend: [P: Spawn], [S: Shop], [R: Reward], [E: Elite], [N: Normal]");
        sb.AppendLine();
        for (int y = yMax; y >= yMin; y--) {
            for (int x = xMin; x <= xMax; x++) {
                Vector3Int pos = new Vector3Int(x, y, 0); string roomLabel = null;
                foreach (var room in _allRooms) { Vector3Int centerCell = globalGroundTilemap.WorldToCell(room.transform.position + (Vector3)room.centerOffset); if (pos == centerCell) { string typeKey = room.roomType == RoomType.Spawn ? "P" : room.roomType.ToString().Substring(0, 1); roomLabel = $"[{typeKey}:{room.debugDepth}]"; break; } }
                if (roomLabel != null) { sb.Append(roomLabel); x += roomLabel.Length - 1; continue; }
                if (globalWallTilemap.HasTile(pos)) sb.Append("W"); else if (globalShadowTilemap.HasTile(pos)) sb.Append("S"); else if (globalGroundTilemap.HasTile(pos)) sb.Append("."); else sb.Append(" ");
            }
            sb.AppendLine();
        }
        string path = System.IO.Path.Combine(Application.dataPath, "..", "MapDebugLog.txt");
        System.IO.File.WriteAllText(path, sb.ToString());
    }
}
