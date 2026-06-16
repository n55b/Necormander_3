using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using NavMeshPlus.Components;
using UnityEngine.Tilemaps;
using System.Linq;

public class MapGenerator : MonoBehaviour
{
    public static MapGenerator Instance;

    // 맵 생성 완료 여부 플래그
    public bool IsMapGenerationCompleted { get; private set; } = false;

    [Header("Data Settings")]
    [SerializeField] private MapGenerationDataSO generationData;
    [SerializeField] private RoomPrefabDataSO prefabData;

    [Header("Global Tilemap References")]
    [SerializeField] private Tilemap globalGroundTilemap;
    [SerializeField] private Tilemap globalWallTilemap;
    [SerializeField] private Tilemap globalShadowTilemap;

    // 미니맵 타일 추가
    [Header("MiniMap Settings")]
    [SerializeField] private Tilemap globalMiniMapTilemap; // GlobalMiniMapTilemap 등록
    [SerializeField] private TileBase miniMapNormalTile;  // 일반 방/복도용 단색 타일

    [Header("안개 시스템")]
    [SerializeField] private Tilemap fogTilemap; // 방금 만든 FogTilemap 등록
    [SerializeField] private TileBase blackTile; // 검은색 타일 에셋 등록

    public Tilemap GlobalMiniMapTilemap => globalMiniMapTilemap;
    public Tilemap FogTilemap => fogTilemap;

    private List<RoomInstance> _allRooms = new List<RoomInstance>();
    private HashSet<RoomInstance> _reachedRooms = new HashSet<RoomInstance>();
    private Dictionary<RoomInstance, List<RoomInstance>> _masterAdjacency = new Dictionary<RoomInstance, List<RoomInstance>>();
    private Dictionary<RoomInstance, Vector2> _intendedDirs = new Dictionary<RoomInstance, Vector2>();
    private Dictionary<System.Tuple<RoomInstance, RoomInstance>, int> _corridorLengths = new Dictionary<System.Tuple<RoomInstance, RoomInstance>, int>();
    private List<string> _rewardConnectionDebugLogs = new List<string>();
    private CorridorPainter _painter;
    private GameObject _tempObstacle;
    private bool _isGenerating = false;
    private int _currentPhaseIndex = 0;

    // --- 4단계 가비지 최소화 연결 후보 캐싱 ---
    private struct RoomConnectionCandidate : System.IComparable<RoomConnectionCandidate>
    {
        public RoomInstance reached;
        public RoomInstance unreached;
        public float dist;

        public int CompareTo(RoomConnectionCandidate other)
        {
            return dist.CompareTo(other.dist);
        }
    }
    private readonly List<RoomConnectionCandidate> _connectionCandidates = new List<RoomConnectionCandidate>(128);

    public System.Action OnMapGenerated;

    public void SetMapData(MapGenerationDataSO genData, RoomPrefabDataSO prefData)
    {
        if (genData != null) generationData = genData;
        if (prefData != null) prefabData = prefData;
    }

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

    public IEnumerator GenerateMapCoroutine()
    {
        yield return StartCoroutine(GenerationSequence());
    }

    private IEnumerator GenerationSequence()
    {
        IsMapGenerationCompleted = false;
        _isGenerating = true;

        int maxRegenAttempts = 10;
        int regenAttempt = 0;
        bool mapSuccess = false;

        while (!mapSuccess && regenAttempt < maxRegenAttempts)
        {
            regenAttempt++;
            _currentPhaseIndex = 0;
            SetupTilemapLayers();
            ClearExistingMap();

            bool isBossFloor = GameManager.Instance != null && (GameManager.Instance.currentFloor == 4 || (GameManager.Instance.debugStartAtBoss && GameManager.Instance.currentFloor == GameManager.Instance.debugStartFloor));

            if (isBossFloor)
            {
                List<RoomType> bossPhase = new List<RoomType> { RoomType.Spawn, RoomType.Boss };
                yield return StartCoroutine(RunPhase(bossPhase));
            }
            else
            {
                int totalSpecials = generationData.shopCount + generationData.rewardCount + generationData.eliteCount;
                int normalCount = Mathf.Max(generationData.minNormalRooms, generationData.totalRoomCount - 1 - totalSpecials);

                int initialBranchCount = Random.Range(1, 5);
                List<RoomType> phase1 = new List<RoomType> { RoomType.Spawn };
                for (int i = 0; i < initialBranchCount; i++) phase1.Add(RoomType.Normal);
                yield return StartCoroutine(RunPhase(phase1));

                int remainingNormal = normalCount - initialBranchCount;

                _currentPhaseIndex++;
                List<RoomType> phase2 = new List<RoomType>();
                for (int i = 0; i < generationData.shopCount; i++) phase2.Add(RoomType.Shop);
                int eliteHalf = generationData.eliteCount / 2;
                for (int i = 0; i < eliteHalf; i++) phase2.Add(RoomType.Elite);
                int p2Normal = remainingNormal > 0 ? Random.Range(1, remainingNormal / 2 + 2) : 0;
                for (int i = 0; i < p2Normal; i++) phase2.Add(RoomType.Normal);
                remainingNormal -= p2Normal;
                if (phase2.Count > 0) yield return StartCoroutine(RunPhase(phase2));

                _currentPhaseIndex++;
                List<RoomType> phase3 = new List<RoomType>();
                int eliteRest = generationData.eliteCount - eliteHalf;
                for (int i = 0; i < eliteRest; i++) phase3.Add(RoomType.Elite);
                for (int i = 0; i < remainingNormal; i++) phase3.Add(RoomType.Normal);
                if (phase3.Count > 0) yield return StartCoroutine(RunPhase(phase3));

                _currentPhaseIndex++;
                List<RoomType> phase4 = new List<RoomType>();
                for (int i = 0; i < generationData.rewardCount; i++) phase4.Add(RoomType.Reward);
                if (phase4.Count > 0) yield return StartCoroutine(RunPhase(phase4));
            }

            // 모든 방의 물리 분산 및 타일맵 병합이 완료된 후 단 한번 복도 연결 수행!
            yield return StartCoroutine(ConnectUnreachedRoomsCoroutine());

            // 최종 모든 방이 온전히 다 연결되었는지 검증
            int totalIsolated = _allRooms.Count - _reachedRooms.Count;
            if (totalIsolated == 0)
            {
                mapSuccess = true;
            }
            else
            {
                Debug.LogWarning($"<color=orange>[MapGenerator]</color> Map attempt {regenAttempt} failed due to {totalIsolated} isolated rooms. Re-generating entire map...");
            }
        }

        if (!mapSuccess)
        {
            Debug.LogError($"<color=red>[MapGenerator]</color> Failed to generate a fully connected map after {maxRegenAttempts} attempts! Culling remaining isolated rooms as fallback.");
            CullIsolatedRooms();
        }

        AssignSpecialRooms();
        DumpMapToLog();

        SetupFinalColliders();
        BakeNavMesh();

        // 안개 타일 배치
        GenerateFogOfWar(); // 던전 전체 까맣게 칠하기

        // 스폰 방만 게임 시작할 때 안개를 즉시 걷어내 줍니다.
        RoomInstance spawnRoom = _allRooms.Find(r => r.roomType == RoomType.Spawn);
        if (spawnRoom != null)
        {
            spawnRoom.RevealRoom();
        }

        PlacePlayerAtSpawn();

        if (_tempObstacle != null) SafeDestroy(_tempObstacle);
        _isGenerating = false;

        // 맵 생성 완료 플래그 설정 및 이벤트 호출
        IsMapGenerationCompleted = true;
        OnMapGenerated?.Invoke();
        Debug.Log("<color=green>[MapGenerator]</color> Map Generation Completed.");
    }

    private void SetupFinalColliders()
    {
        if (globalWallTilemap == null) return;
        GameObject wallObj = globalWallTilemap.gameObject;

        Rigidbody2D rb = wallObj.GetComponent<Rigidbody2D>();
        if (rb == null) rb = wallObj.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;

        TilemapCollider2D tileCol = wallObj.GetComponent<TilemapCollider2D>();
        if (tileCol == null) tileCol = wallObj.AddComponent<TilemapCollider2D>();
        tileCol.compositeOperation = Collider2D.CompositeOperation.Merge;

        CompositeCollider2D comp = wallObj.GetComponent<CompositeCollider2D>();
        if (comp == null) comp = wallObj.AddComponent<CompositeCollider2D>();
        comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
        comp.generationType = CompositeCollider2D.GenerationType.Manual;

        comp.GenerateGeometry();
        Physics2D.SyncTransforms();
    }

    private void BakeNavMesh()
    {
        var navSurface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (navSurface != null) { navSurface.RemoveData(); navSurface.BuildNavMesh(); }
    }

    private void SaveCorridorLength(RoomInstance r1, RoomInstance r2, int length)
    {
        _corridorLengths[System.Tuple.Create(r1, r2)] = length;
        _corridorLengths[System.Tuple.Create(r2, r1)] = length;
    }

    private int GetCorridorLength(RoomInstance r1, RoomInstance r2)
    {
        var key = System.Tuple.Create(r1, r2);
        if (_corridorLengths.TryGetValue(key, out int length)) return length;
        return 0;
    }

    public void PlacePlayerAtSpawn()
    {
        RoomInstance spawnRoom = _allRooms.Find(r => r.roomType == RoomType.Spawn);
        if (spawnRoom == null) return;
        var spawnEvent = spawnRoom.GetComponent<SpawnRoomEvent>();
        Vector3 spawnPos = spawnEvent != null ? spawnEvent.GetSpawnPosition() : spawnRoom.transform.position;
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            GameManager.Instance.PLAYERCONTROLLER.transform.position = spawnPos;

            // [추가] 시작 방 진입 로직 강제 실행 (음악 재생 등)
            spawnRoom.ForceEnter();
        }
    }

    private IEnumerator RunPhase(List<RoomType> types)
    {
        List<RoomInstance> phaseRooms = new List<RoomInstance>();
        float startAngle = Random.Range(0f, 360f);
        float angleStep = 360f / types.Count;
        for (int i = 0; i < types.Count; i++)
        {
            float jitter = Random.Range(-25f, 25f);
            float targetAngle = (startAngle + (i * angleStep) + jitter) * Mathf.Deg2Rad;
            RoomInstance room = CreateRoom(types[i], targetAngle);
            if (room != null)
            {
                room.phaseIndex = _currentPhaseIndex; // 생성된 페이즈 인덱스 저장
                phaseRooms.Add(room);
                _allRooms.Add(room);
                if (!_masterAdjacency.ContainsKey(room)) _masterAdjacency[room] = new List<RoomInstance>();
                _intendedDirs[room] = new Vector2(Mathf.Cos(targetAngle), Mathf.Sin(targetAngle));
            }
        }
        yield return StartCoroutine(PhysicsSpreadingRoutine(phaseRooms));
        foreach (var room in phaseRooms)
        {
            room.CleanupPhysics();
        }

        // 리지드바디 컴포넌트 파괴가 다음 프레임에 완전히 처리되도록 대기하여 물리 롤백 차단
        yield return null;

        foreach (var room in phaseRooms)
        {
            room.SnapToGrid(generationData.gridUnit);
            room.MergeTilesToGlobal(globalGroundTilemap, globalWallTilemap, globalShadowTilemap);
        }
        UpdateGlobalBoundingObstacle();
        // 각 페이즈 단위 즉시 연결을 제거하고, 모든 방의 배치가 완료된 최종 시점에 한번에 복도를 연결하도록 합니다.
        yield return null;
    }

    private RoomInstance CreateRoom(RoomType type, float angle)
    {
        GameObject prefab = prefabData.GetRandomPrefab(type);
        if (prefab == null) return null;
        float spawnRadius = 1f;
        if (type != RoomType.Spawn && _allRooms.Count > 0)
        {
            float halfW = _allRooms.Max(r => Mathf.Abs(r.transform.position.x) + r.roomSize.x * 0.5f + 2f);
            float halfH = _allRooms.Max(r => Mathf.Abs(r.transform.position.y) + r.roomSize.y * 0.5f + 2f);
            float cos = Mathf.Abs(Mathf.Cos(angle)); float sin = Mathf.Abs(Mathf.Sin(angle));
            spawnRadius = (halfW * sin <= halfH * cos) ? (halfW / (cos + 0.001f)) : (halfH / (sin + 0.001f));
            // 이전 페이즈의 네모 콜라이더 박스 표면에 가깝되, 앵커가 가로막히지 않도록 반경 90% 수준으로 조율
            spawnRadius = Mathf.Max(5f, spawnRadius * 0.9f);
        }
        Vector2 spawnPos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spawnRadius;
        GameObject roomObj = Instantiate(prefab, (Vector3)spawnPos, Quaternion.identity, transform);
        RoomInstance room = roomObj.GetComponent<RoomInstance>() ?? roomObj.AddComponent<RoomInstance>();
        room.Initialize(type);
        if (type == RoomType.Spawn) { Rigidbody2D rb = room.GetComponent<Rigidbody2D>(); if (rb != null) rb.bodyType = RigidbodyType2D.Static; _reachedRooms.Add(room); if (!_masterAdjacency.ContainsKey(room)) _masterAdjacency[room] = new List<RoomInstance>(); }
        return room;
    }

    private IEnumerator PhysicsSpreadingRoutine(List<RoomInstance> activeRooms)
    {
        // 물리 엔진 시뮬레이션 간섭 방지
        foreach (var room in activeRooms)
        {
            Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
            if (rb != null) rb.simulated = false;
        }

        int maxIterations = 60;
        float stepSize = 0.5f;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            // 1. intended direction 방향으로 바깥으로 약간 퍼뜨리되, 중심부 방향의 가상 중력을 주어 팽창을 억제
            foreach (var room in activeRooms)
            {
                Rigidbody2D rb = room.GetComponent<Rigidbody2D>();
                if (rb != null && rb.bodyType == RigidbodyType2D.Static) continue;

                Vector2 intendedDir = _intendedDirs.ContainsKey(room) ? _intendedDirs[room] : ((Vector2)room.transform.position).normalized;
                // 바깥으로 미는 힘과 안쪽으로 당기는 복원력의 조합 (콤팩트 팩킹 유도)
                Vector2 centerGravity = -((Vector2)room.transform.position).normalized * 0.35f;
                Vector2 finalMove = (intendedDir * stepSize) + centerGravity;
                room.transform.position += (Vector3)finalMove;
            }

            // 2. 방들 간의 기하학적 AABB 겹침 분산 연산 (Separation)
            bool anyOverlap = false;
            for (int i = 0; i < _allRooms.Count; i++)
            {
                for (int j = i + 1; j < _allRooms.Count; j++)
                {
                    RoomInstance rA = _allRooms[i];
                    RoomInstance rB = _allRooms[j];
                    if (rA == null || rB == null) continue;

                    Rigidbody2D rbA = rA.GetComponent<Rigidbody2D>();
                    Rigidbody2D rbB = rB.GetComponent<Rigidbody2D>();
                    // activeRooms에 포함되지 않은 방은 이미 이전 페이즈에서 배치가 완료된 방이므로 무조건 고정(Static) 처리합니다.
                    bool staticA = (rbA != null && rbA.bodyType == RigidbodyType2D.Static) || !activeRooms.Contains(rA);
                    bool staticB = (rbB != null && rbB.bodyType == RigidbodyType2D.Static) || !activeRooms.Contains(rB);

                    if (staticA && staticB) continue;

                    // 방 크기 + 3.5f 콜라이더 패딩 마진 적용 (입구 막힘 방지를 위한 최적 마진)
                    float halfWA = (rA.roomSize.x + 3.5f) * 0.5f;
                    float halfHA = (rA.roomSize.y + 3.5f) * 0.5f;
                    float halfWB = (rB.roomSize.x + 3.5f) * 0.5f;
                    float halfHB = (rB.roomSize.y + 3.5f) * 0.5f;

                    Vector2 centerA = (Vector2)rA.transform.position + rA.centerOffset;
                    Vector2 centerB = (Vector2)rB.transform.position + rB.centerOffset;

                    float dx = centerB.x - centerA.x;
                    float dy = centerB.y - centerA.y;

                    float overlapX = (halfWA + halfWB) - Mathf.Abs(dx);
                    float overlapY = (halfHA + halfHB) - Mathf.Abs(dy);

                    if (overlapX > 0 && overlapY > 0)
                    {
                        anyOverlap = true;

                        // 겹치는 부피가 더 작은 축 방향으로 밀쳐내어 분리
                        if (overlapX < overlapY)
                        {
                            float pushX = overlapX;
                            float dirX = dx >= 0 ? 1f : -1f;
                            if (Mathf.Abs(dx) < 0.001f) dirX = Random.value > 0.5f ? 1f : -1f;

                            if (!staticA && !staticB)
                            {
                                rA.transform.position += Vector3.left * (dirX * pushX * 0.5f);
                                rB.transform.position += Vector3.right * (dirX * pushX * 0.5f);
                            }
                            else if (staticA)
                            {
                                rB.transform.position += Vector3.right * (dirX * pushX);
                            }
                            else if (staticB)
                            {
                                rA.transform.position += Vector3.left * (dirX * pushX);
                            }
                        }
                        else
                        {
                            float pushY = overlapY;
                            float dirY = dy >= 0 ? 1f : -1f;
                            if (Mathf.Abs(dy) < 0.001f) dirY = Random.value > 0.5f ? 1f : -1f;

                            if (!staticA && !staticB)
                            {
                                rA.transform.position += Vector3.down * (dirY * pushY * 0.5f);
                                rB.transform.position += Vector3.up * (dirY * pushY * 0.5f);
                            }
                            else if (staticA)
                            {
                                rB.transform.position += Vector3.up * (dirY * pushY);
                            }
                            else if (staticB)
                            {
                                rA.transform.position += Vector3.down * (dirY * pushY);
                            }
                        }
                    }
                }
            }

            // 모든 방의 겹침이 완전히 풀렸으면 조기 탈출
            if (!anyOverlap) break;
        }

        yield break;
    }

    private IEnumerator ConnectUnreachedRoomsCoroutine()
    {
        int maxAttempts = 5; // 통로 연결을 최대 5번만 시도하고, 실패 시 방 배치 자체를 새로 돌림
        bool routingSuccess = false;

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        int maxPhase = 0;
        foreach (var r in _allRooms)
        {
            if (r.roomType != RoomType.Reward && r.phaseIndex > maxPhase)
            {
                maxPhase = r.phaseIndex;
            }
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                ResetCorridorState();
            }

            _painter.Init(globalGroundTilemap, globalWallTilemap, globalShadowTilemap, generationData.floorTile, generationData.wallTile, generationData.shadowTile);

            // 초기 스폰 방 깊이 연산 실행
            UpdateAllRoomDepths();

            List<RoomInstance> rewardRooms = _allRooms.Where(r => r.roomType == RoomType.Reward).ToList();
            bool phaseRoutingFailed = false;

            for (int phase = 0; phase <= maxPhase; phase++)
            {
                List<RoomInstance> phaseUnreached = _allRooms.Where(r => r.phaseIndex == phase && !_reachedRooms.Contains(r) && r.roomType != RoomType.Reward).ToList();
                bool changed = true;
                int safety = 0;
                while (changed && phaseUnreached.Count > 0)
                {
                    safety++;
                    if (safety > 5000)
                    {
                        Debug.LogError("<color=red>[MapGenerator]</color> ConnectUnreachedRooms phase loop timeout! Infinite loop detected.");
                        break;
                    }
                    changed = false;
                    _connectionCandidates.Clear();

                    foreach (var r in _reachedRooms)
                    {
                        for (int uIndex = 0; uIndex < phaseUnreached.Count; uIndex++)
                        {
                            var u = phaseUnreached[uIndex];
                            if ((u.roomType == RoomType.Shop || u.roomType == RoomType.Elite) && r.roomType == RoomType.Spawn)
                                continue;

                            // 직선거리를 복도 길이 예상치로 사용
                            float approxCorridorLen = Vector2.Distance(r.transform.position, u.transform.position);
                            // 일반 방 연결에서는 순수 물리적 거리를 기준으로 하되, 맵 생성 다양성을 극대화하기 위해 랜덤 노이즈(-30f ~ +30f)를 적용합니다.
                            float estimatedCumulativeDist = approxCorridorLen + Random.Range(-30f, 30f);

                            if (attempt > 0)
                            {
                                estimatedCumulativeDist += Random.Range(-15f, 15f); // 셔플 회차 시 노이즈
                            }
                            _connectionCandidates.Add(new RoomConnectionCandidate { reached = r, unreached = u, dist = estimatedCumulativeDist });
                        }
                    }

                    _connectionCandidates.Sort();

                    for (int i = 0; i < _connectionCandidates.Count; i++)
                    {
                        if (sw.ElapsedMilliseconds > 15)
                        {
                            yield return null;
                            sw.Restart();
                        }

                        var c = _connectionCandidates[i];
                        if (!phaseUnreached.Contains(c.unreached)) continue;
                        if (DrawCorridorBetweenRooms(c.reached, c.unreached, 0, attempt > 0))
                        {
                            _reachedRooms.Add(c.unreached);
                            phaseUnreached.Remove(c.unreached);
                            _masterAdjacency[c.reached].Add(c.unreached);
                            _masterAdjacency[c.unreached].Add(c.reached);
                            changed = true;

                            // 복도가 하나 연결될 때마다 다음 프레임으로 처리를 양보하여 프리징 방지
                            yield return null;
                            sw.Restart();
                            break;
                        }
                    }
                }

                // 해당 페이즈 방들 중 한 개라도 낙오(연결 안 됨)되었다면 이 attempt는 즉시 실패 판정 후 탈출
                int phaseIsolated = _allRooms.Where(r => r.phaseIndex == phase && r.roomType != RoomType.Reward).Count(r => !_reachedRooms.Contains(r));
                if (phaseIsolated > 0)
                {
                    phaseRoutingFailed = true;
                    break;
                }
            }

            if (phaseRoutingFailed)
            {
                Debug.LogWarning($"<color=orange>[MapGenerator]</color> Phase routing failed at attempt {attempt + 1}. Retrying...");
                yield return null;
                continue;
            }

            // 일반 페이즈 방들의 연결이 100% 완료된 후에만 Reward 방 연결을 이어서 처리
            foreach (var reward in rewardRooms)
            {
                UpdateAllRoomDepths();
                var validReached = _reachedRooms.Where(r => r.debugDepth != -1).ToList();
                if (validReached.Count == 0) continue;
                int normalMaxD = validReached.Where(r => r.roomType != RoomType.Reward).Max(r => r.debugDepth);

                // 홉 수 기준 최대 깊이에서 2 이내에 있는 모든 깊은 방들을 후보군으로 선정
                float depthThreshold = Mathf.Max(1, normalMaxD - 2);
                var deepNodes = validReached.Where(r => (float)r.debugDepth >= depthThreshold).OrderBy(r => Vector2.Distance(r.transform.position, reward.transform.position)).ToList();

                // 디버그 로그 수집
                System.Text.StringBuilder logSb = new System.Text.StringBuilder();
                logSb.AppendLine($"[Reward Connection Debug] Connecting Room: {reward.name} (Attempt: {attempt + 1})");
                logSb.AppendLine($"  - Max Depth (normalMaxD): {normalMaxD}");
                logSb.AppendLine($"  - Filtering Threshold (normalMaxD - 2): {depthThreshold}");
                logSb.AppendLine("  - Candidate Reached Rooms (Sorted by Depth DESC):");
                foreach (var r in validReached.OrderByDescending(node => node.debugDepth))
                {
                    bool isSelected = (float)r.debugDepth >= depthThreshold;
                    logSb.AppendLine($"    * {r.name}: Depth={r.debugDepth}, Diameter={r.GetDiameter()}, IsCandidate={isSelected}");
                }

                if (attempt > 0)
                {
                    deepNodes = deepNodes.OrderBy(r => Vector2.Distance(r.transform.position, reward.transform.position) + Random.Range(-15f, 15f)).ToList();
                }

                RoomInstance connectedParent = null;
                foreach (var p in deepNodes)
                {
                    if (sw.ElapsedMilliseconds > 15)
                    {
                        yield return null;
                        sw.Restart();
                    }

                    if (DrawCorridorBetweenRooms(p, reward, 0, attempt > 0))
                    {
                        _reachedRooms.Add(reward);
                        _masterAdjacency[p].Add(reward);
                        _masterAdjacency[reward].Add(p);
                        connectedParent = p;

                        // 보상 방 연결 시에도 프레임 양보
                        yield return null;
                        sw.Restart();
                        break;
                    }
                }

                logSb.AppendLine($"  - Result Connection: {(connectedParent != null ? "SUCCESS to " + connectedParent.name : "FAILED")}");
                _rewardConnectionDebugLogs.Add(logSb.ToString());
            }

            int isolatedCount = _allRooms.Count - _reachedRooms.Count;
            if (isolatedCount == 0)
            {
                // 1. 모든 방이 연결된 시점에서, 숏컷 우회로(루프)를 먼저 설치해 봅니다.
                // (최종 숏컷이 포함된 동선을 기준으로 뎁스를 검증하기 위함)
                yield return StartCoroutine(CreateExtraCorridorsCoroutine());

                // 2. 최종 맵의 게임 규칙 제약 조건 유효성 검사 (Constraint Validation)
                bool isValidMap = true;

                // 마지막 시도(Fallback)인 경우는 무조건 통과시켜 맵 멈춤 방지
                if (attempt < maxAttempts - 1)
                {
                    // 현재 맵의 실제 홉 수 최대 깊이 계산 (보상방 제외)
                    int normalMaxD = _allRooms.Where(r => r.debugDepth != -1 && r.roomType != RoomType.Reward).Max(r => r.debugDepth);

                    // 조건 A: 모든 보상방(Reward)의 깊이(Depth)가 현재 맵의 최대 깊이보다 최소 1 이내여야 함.
                    // (즉, 보상방은 상대적으로 가장 깊은 최심부 영역에 매칭되도록 보장)
                    foreach (var room in _allRooms)
                    {
                        if (room.roomType == RoomType.Reward && room.debugDepth < normalMaxD - 1)
                        {
                            isValidMap = false;
                            break;
                        }
                    }

                    // 조건 B: 모든 특수방(Shop, Elite)의 깊이가 스폰 방 기준 2 이상이어야 함. (스폰방 바로 옆 직접 연결 방지)
                    foreach (var room in _allRooms)
                    {
                        if ((room.roomType == RoomType.Shop || room.roomType == RoomType.Elite) && room.debugDepth < 2)
                        {
                            isValidMap = false;
                            break;
                        }
                    }
                }

                if (isValidMap)
                {
                    routingSuccess = true;
                    break;
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[MapGenerator]</color> Generated corridor layout did not satisfy depth rules (Attempt {attempt + 1}). Resetting and retrying routing...");
                    // 제약 조건을 위반했으므로 통로 데이터를 싹 비우고 재시도
                    ResetCorridorState();
                    yield return null;
                    continue;
                }
            }

            Debug.LogWarning($"<color=orange>[MapGenerator]</color> Corridor routing attempt {attempt + 1} failed with {isolatedCount} isolated rooms. Retrying corridor routing...");
            yield return null;
        }

        if (routingSuccess)
        {
            _painter.FinalizePainting();
            UpdateAllRoomDepths();
        }
    }

    private void ResetCorridorState()
    {
        _corridorLengths.Clear();
        _rewardConnectionDebugLogs.Clear();
        _reachedRooms.Clear();
        RoomInstance spawnRoom = _allRooms.Find(r => r.roomType == RoomType.Spawn);
        if (spawnRoom != null)
        {
            _reachedRooms.Add(spawnRoom);
        }

        _masterAdjacency.Clear();
        foreach (var room in _allRooms)
        {
            _masterAdjacency[room] = new List<RoomInstance>();
        }

        foreach (var room in _allRooms)
        {
            foreach (var anchor in room.anchors)
            {
                anchor.isUsed = false;
            }

            foreach (var door in room.doorObjects)
            {
                if (door != null) SafeDestroy(door);
            }
            room.doorObjects.Clear();
        }
    }

    private void CullIsolatedRooms()
    {
        List<RoomInstance> isolatedRooms = _allRooms.Where(r => !_reachedRooms.Contains(r)).ToList();
        foreach (var room in isolatedRooms)
        {
            room.EraseTilesFromGlobal(globalGroundTilemap, globalWallTilemap, globalShadowTilemap);
            _allRooms.Remove(room);
            if (_masterAdjacency.ContainsKey(room)) _masterAdjacency.Remove(room);
            if (_intendedDirs.ContainsKey(room)) _intendedDirs.Remove(room);
            SafeDestroy(room.gameObject);
            Debug.LogWarning($"<color=red>[MapGenerator]</color> Destroyed isolated room '{room.name}' due to corridor connection failure.");
        }
    }

    private void UpdateAllRoomDepths()
    {
        RoomInstance spawn = _allRooms.Find(r => r.roomType == RoomType.Spawn);
        if (spawn == null) return;

        foreach (var r in _allRooms)
        {
            r.debugDepth = -1;
        }

        Queue<RoomInstance> queue = new Queue<RoomInstance>();
        spawn.debugDepth = 0;
        queue.Enqueue(spawn);

        int safety = 0;
        while (queue.Count > 0)
        {
            safety++;
            if (safety > 5000)
            {
                Debug.LogError("<color=red>[MapGenerator]</color> UpdateAllRoomDepths (BFS) loop timeout!");
                break;
            }

            RoomInstance curr = queue.Dequeue();
            if (!_masterAdjacency.ContainsKey(curr)) continue;

            foreach (var neighbor in _masterAdjacency[curr])
            {
                if (neighbor.debugDepth == -1) // 아직 방문하지 않은 방
                {
                    neighbor.debugDepth = curr.debugDepth + 1; // 홉 수(방 개수) 누적
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    private void UpdateGlobalBoundingObstacle()
    {
        if (_allRooms.Count == 0) return;
        if (_tempObstacle == null) { _tempObstacle = new GameObject("MapBoundsObstacle"); _tempObstacle.transform.SetParent(transform); var rb = _tempObstacle.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Static; _tempObstacle.AddComponent<BoxCollider2D>(); }
        float minX = _allRooms.Min(r => r.transform.position.x - r.roomSize.x * 0.5f); float maxX = _allRooms.Max(r => r.transform.position.x + r.roomSize.x * 0.5f); float minY = _allRooms.Min(r => r.transform.position.y - r.roomSize.y * 0.5f); float maxY = _allRooms.Max(r => r.transform.position.y + r.roomSize.y * 0.5f);
        BoxCollider2D box = _tempObstacle.GetComponent<BoxCollider2D>(); box.size = new Vector2(maxX - minX + 2f, maxY - minY + 2f); box.offset = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        Physics2D.SyncTransforms();
    }

    private void ClearExistingMap()
    {
        _corridorLengths.Clear();
        _rewardConnectionDebugLogs.Clear();
        foreach (var room in _allRooms) if (room != null) SafeDestroy(room.gameObject);
        _allRooms.Clear(); _reachedRooms.Clear(); _masterAdjacency.Clear(); _intendedDirs.Clear();
        if (_tempObstacle != null) SafeDestroy(_tempObstacle);
        if (globalGroundTilemap != null) globalGroundTilemap.ClearAllTiles();
        if (globalWallTilemap != null) globalWallTilemap.ClearAllTiles();
        if (globalShadowTilemap != null) globalShadowTilemap.ClearAllTiles();

        if (globalMiniMapTilemap != null) globalMiniMapTilemap.ClearAllTiles(); // [추가] alslaoq
    }

    private struct AnchorPair
    {
        public RoomAnchor a;
        public RoomAnchor b;
        public float dist;
    }

    private List<Vector2Int> SimplifyPath(List<Vector2Int> path)
    {
        if (path.Count <= 2) return path;

        List<Vector2Int> optimized = new List<Vector2Int>();
        int currIndex = 0;
        optimized.Add(path[0]);

        int safety = 0;
        while (currIndex < path.Count - 1)
        {
            safety++;
            if (safety > 5000)
            {
                Debug.LogError("[MapGenerator] SimplifyPath infinite loop detected!");
                break;
            }

            int nextIndex = currIndex + 1;
            for (int j = path.Count - 1; j > currIndex + 1; j--)
            {
                int dist = Mathf.Abs(path[currIndex].x - path[j].x) + Mathf.Abs(path[currIndex].y - path[j].y);
                if (dist <= 1)
                {
                    nextIndex = j;
                    break;
                }
            }
            optimized.Add(path[nextIndex]);
            currIndex = nextIndex;
        }

        return optimized;
    }

    private IEnumerator CreateExtraCorridorsCoroutine()
    {
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        List<System.Tuple<RoomInstance, RoomInstance, float>> extraCandidates = new List<System.Tuple<RoomInstance, RoomInstance, float>>();

        // 1. 서로 인접하고 추가 복도를 뚫을 수 있는 방 쌍 수집
        for (int i = 0; i < _allRooms.Count; i++)
        {
            for (int j = i + 1; j < _allRooms.Count; j++)
            {
                RoomInstance r1 = _allRooms[i];
                RoomInstance r2 = _allRooms[j];

                // 보상 방과 스폰 방은 숏컷 루프 대상에서 제외 (밸런스 보존)
                if (r1.roomType == RoomType.Reward || r2.roomType == RoomType.Reward) continue;
                if (r1.roomType == RoomType.Spawn || r2.roomType == RoomType.Spawn) continue;

                // 이미 연결되어 있는 관계는 제외
                if (_masterAdjacency[r1].Contains(r2)) continue;

                // 두 방의 깊이 차이가 최대 1 이하일 때만 우회로(숏컷) 생성을 시도합니다.
                // (지름길이 너무 얕아져서 보상방의 상대적 위계질서 깊이가 단축되는 것을 원천 방지)
                int depthDiff = Mathf.Abs(r1.debugDepth - r2.debugDepth);
                if (depthDiff > 1) continue;

                float dist = Vector2.Distance(r1.transform.position, r2.transform.position);
                if (dist < 80f) // 물리적으로 가까운 방들만 후보 선정 (80f 범위로 확장)
                {
                    extraCandidates.Add(System.Tuple.Create(r1, r2, dist));
                }
            }
        }

        // 2. 80f 이내의 후보군을 거리순이 아닌 무작위 셔플링하여 맵 전역에 고르게 배분
        extraCandidates = extraCandidates.OrderBy(x => Random.value).ToList();

        // 3. 전체 방 개수에 비례하여 최대 추가 루프 개수 결정 (최소 1개, 최대 3개)
        int maxExtraLoops = Mathf.Clamp(_allRooms.Count / 4, 1, 3);
        int successCount = 0;

        foreach (var candidate in extraCandidates)
        {
            if (sw.ElapsedMilliseconds > 15)
            {
                yield return null;
                sw.Restart();
            }

            if (successCount >= maxExtraLoops) break;

            RoomInstance r1 = candidate.Item1;
            RoomInstance r2 = candidate.Item2;

            // 특정 방에만 복도 연결이 과도하게 쏠려 스파게티가 되는 현상 차단 (최대 Degree = 3 제한)
            if (_masterAdjacency[r1].Count >= 3 || _masterAdjacency[r2].Count >= 3) continue;

            // 임의의 앵커 쌍 연결 시도
            if (DrawCorridorBetweenRooms(r1, r2, 0, shuffle: true))
            {
                _masterAdjacency[r1].Add(r2);
                _masterAdjacency[r2].Add(r1);
                successCount++;
                yield return null; // 프레임 분산
                sw.Restart();
            }
        }

        if (successCount > 0)
        {
            // 추가된 복도 타일들 최종 일괄 그리기 및 깊이 데이터 재정렬
            _painter.FinalizePainting();
            UpdateAllRoomDepths();
            Debug.Log($"<color=green>[MapGenerator]</color> Added {successCount} extra loop corridors for shortcuts.");
        }
    }

    private bool DrawCorridorBetweenRooms(RoomInstance a, RoomInstance b, int pathDepth, bool shuffle = false)
    {
        _painter.SetCurrentRooms(a, b);
        var availableA = a.anchors.Where(an => !an.isUsed).ToList();
        var availableB = b.anchors.Where(an => !an.isUsed).ToList();
        if (availableA.Count == 0 || availableB.Count == 0) return false;

        List<AnchorPair> pairs = new List<AnchorPair>();
        foreach (var anA in availableA)
        {
            foreach (var anB in availableB)
            {
                float d = Vector2.Distance(anA.transform.position, anB.transform.position);
                if (shuffle) d += Random.Range(-15f, 15f);
                pairs.Add(new AnchorPair { a = anA, b = anB, dist = d });
            }
        }

        pairs.Sort((x, y) => x.dist.CompareTo(y.dist));

        foreach (var pair in pairs)
        {
            RoomAnchor bestA = pair.a;
            RoomAnchor bestB = pair.b;

            Vector3Int cA = globalWallTilemap.WorldToCell(bestA.transform.position);
            Vector3Int cB = globalWallTilemap.WorldToCell(bestB.transform.position);
            Vector2Int exitA = new Vector2Int(cA.x, cA.y);
            Vector2Int exitB = new Vector2Int(cB.x, cB.y);

            List<Vector2Int> path = new List<Vector2Int>();
            Vector2Int curA = exitA;
            path.Add(exitA);
            for (int i = 0; i < generationData.corridorStraightLength; i++)
            {
                curA += bestA.direction;
                path.Add(curA);
            }
            Vector2Int entB = exitB;
            for (int i = 0; i < generationData.corridorStraightLength; i++)
                entB += bestB.direction;

            List<Vector2Int> astar = _painter.FindPath(curA, entB, generationData.corridorAvoidMargin, pathDepth);
            if (astar != null)
            {
                for (int i = 1; i < astar.Count; i++) path.Add(astar[i]);
                Vector2Int fin = entB;
                for (int i = 0; i < generationData.corridorStraightLength; i++)
                {
                    fin -= bestB.direction;
                    path.Add(fin);
                }
                if (path.Count > 0 && path.Last() != exitB) path.Add(exitB);
                for (int i = path.Count - 1; i > 0; i--)
                    if (path[i] == path[i - 1]) path.RemoveAt(i);

                // 경로 단순화 적용: 쓸데없이 앞으로 나갔다가 꺾여서 생기는 T자 꼬리 단락 자동 도려내기
                path = SimplifyPath(path);

                // 생성될 복도가 기존 복도들과 닿거나 겹치는지 최종 사전 체크
                if (_painter.CheckCorridorOverlapAndContact(path, bestA, bestB))
                {
                    continue; // 닿는다면 해당 경로 기각하고 다른 앵커 매칭 시도
                }

                _painter.RegisterCorridorWithAnchors(path, bestA, bestB, pathDepth);
                bestA.isUsed = true;
                bestB.isUsed = true;

                SpawnDoorAtAnchor(a, bestA);
                SpawnDoorAtAnchor(b, bestB);

                // [추가] 두 방 사이의 실제 복도 타일 수(길이)를 캐시에 저장
                SaveCorridorLength(a, b, path.Count);

                return true;
            }
        }
        return false;
    }

    private void SpawnDoorAtAnchor(RoomInstance room, RoomAnchor anchor)
    {
        GameObject doorPrefab = null; float rotation = 0f;
        if (anchor.direction == Vector2Int.up) doorPrefab = generationData.doorUp;
        else if (anchor.direction == Vector2Int.down) doorPrefab = generationData.doorDown;
        else if (anchor.direction == Vector2Int.left) doorPrefab = generationData.doorLeft;
        else if (anchor.direction == Vector2Int.right) doorPrefab = generationData.doorRight;
        if (doorPrefab == null && generationData.doorUp != null) { doorPrefab = generationData.doorUp; if (anchor.direction == Vector2Int.down) rotation = 180f; else if (anchor.direction == Vector2Int.left) rotation = 90f; else if (anchor.direction == Vector2Int.right) rotation = -90f; }

        if (doorPrefab != null)
        {
            GameObject doorObj = Instantiate(doorPrefab, anchor.transform.position, Quaternion.Euler(0, 0, rotation), room.transform);
            doorObj.name = $"Door_{anchor.direction}_{room.name}";
            room.doorObjects.Add(doorObj);

            // [수정] 생성 시에는 기본적으로 열려(비활성) 있어야 플레이어가 이동 가능함
            doorObj.SetActive(false);
        }
    }

    private void SetupTilemapLayers() { ConfigureTilemap(globalGroundTilemap, "Ground"); ConfigureTilemap(globalWallTilemap, "Wall"); ConfigureTilemap(globalShadowTilemap, "Shadow"); ConfigureTilemap(globalMiniMapTilemap, "MiniMap"); /*미니맵 추가*/ }
    private void ConfigureTilemap(Tilemap tm, string layerName)
    {
        if (tm == null) return;
        int layer = LayerMask.NameToLayer(layerName);
        if (layer != -1) tm.gameObject.layer = layer;

        if (layerName == "Wall")
        {
            var tr = tm.GetComponent<TilemapRenderer>();
            if (tr != null)
            {
                tr.sortingLayerName = "Ground";
                tr.sortingOrder = 1;
            }
        }
    }
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
        for (int y = yMax; y >= yMin; y--)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0); string roomLabel = null;
                foreach (var room in _allRooms) { Vector3Int centerCell = globalGroundTilemap.WorldToCell(room.transform.position + (Vector3)room.centerOffset); if (pos == centerCell) { string typeKey = room.roomType == RoomType.Spawn ? "P" : room.roomType.ToString().Substring(0, 1); roomLabel = $"[{typeKey}:{room.debugDepth}]"; break; } }
                if (roomLabel != null) { sb.Append(roomLabel); x += roomLabel.Length - 1; continue; }
                if (globalWallTilemap.HasTile(pos)) sb.Append("W"); else if (globalShadowTilemap.HasTile(pos)) sb.Append("S"); else if (globalGroundTilemap.HasTile(pos)) sb.Append("."); else sb.Append(" ");
            }
            sb.AppendLine();
        }

        // --- 상세 맵 생성 디버그 로그 추가 ---
        sb.AppendLine("\n==================== ROOM DETAILS ====================");
        foreach (var room in _allRooms)
        {
            Tilemap mainTM = room.wallTilemap != null ? room.wallTilemap : room.groundTilemap;
            bool mainTMFound = mainTM != null;
            Vector3 rawCellWorldZero = mainTMFound ? mainTM.CellToWorld(Vector3Int.zero) : Vector3.zero;
            Vector3Int globalCellZero = mainTMFound ? globalGroundTilemap.WorldToCell(rawCellWorldZero) : Vector3Int.zero;
            Vector3 targetCellWorldZero = mainTMFound ? globalGroundTilemap.CellToWorld(globalCellZero) : Vector3.zero;
            Vector3 alignmentError = targetCellWorldZero - rawCellWorldZero;

            // 정밀 정렬 성공 여부 판단 (에러가 아주 작은 실수 오차 범위 내인지 검사)
            bool isAligned = !mainTMFound || (Mathf.Abs(alignmentError.x) < 0.001f && Mathf.Abs(alignmentError.y) < 0.001f);

            Vector3Int cellPos = globalGroundTilemap.WorldToCell(room.transform.position);
            Transform fogMaskTrans = room.transform.Find("FogMask");
            Vector3 fogMaskPos = fogMaskTrans != null ? fogMaskTrans.position : Vector3.zero;

            sb.AppendLine($"Room: {room.name}");
            sb.AppendLine($"  - Type: {room.roomType}, Depth: {room.debugDepth}, PhaseIndex: {room.phaseIndex}");
            sb.AppendLine($"  - MainTilemap Found: {mainTMFound} (Name: {(mainTMFound ? mainTM.name : "None")})");
            sb.AppendLine($"  - Alignment Status: {(isAligned ? "SUCCESS (Aligned)" : "FAIL (Misaligned)")}");
            sb.AppendLine($"  - Local (0,0) Cell World Pos: {rawCellWorldZero}");
            sb.AppendLine($"  - Global Grid Target Cell Pos: {globalCellZero}");
            sb.AppendLine($"  - Global Grid Target World Pos: {targetCellWorldZero}");
            sb.AppendLine($"  - Applied Snapping Offset: {alignmentError}");
            sb.AppendLine($"  - Room Transform Position: {room.transform.position}");
            sb.AppendLine($"  - Room Cell Position: {cellPos}");
            sb.AppendLine($"  - Size: {room.roomSize}, CenterOffset: {room.centerOffset}");
            sb.AppendLine($"  - FogMask World Position: {(fogMaskTrans != null ? fogMaskPos.ToString() : "None")}");
            sb.AppendLine($"  - Anchors count: {room.anchors.Count}");
            for (int i = 0; i < room.anchors.Count; i++)
            {
                var anchor = room.anchors[i];
                Vector3Int anchorCell = globalGroundTilemap.WorldToCell(anchor.transform.position);
                sb.AppendLine($"    * Anchor {i}: LocalPos={anchor.transform.localPosition}, CellPos={anchorCell}, Dir={anchor.direction}, IsUsed={anchor.isUsed}");
            }
            sb.AppendLine();
        }

        // --- 보상 방 연결 과정 다익스트라 상세 로그 추가 ---
        sb.AppendLine("\n==================== REWARD ROOM CONNECTION LOGS ====================");
        if (_rewardConnectionDebugLogs.Count > 0)
        {
            foreach (var log in _rewardConnectionDebugLogs)
            {
                sb.AppendLine(log);
            }
        }
        else
        {
            sb.AppendLine("No Reward Room Connection Logs recorded.");
        }

        string path = System.IO.Path.Combine(Application.dataPath, "..", "MapDebugLog.txt");
        System.IO.File.WriteAllText(path, sb.ToString());
    }

    public static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null) return;

#if UNITY_EDITOR
        if (UnityEditor.EditorUtility.IsPersistent(obj))
        {
            Debug.LogWarning($"[SafeDestroy] Prevented destroying asset: {obj.name}");
            return;
        }
#endif

        if (obj is GameObject go)
        {
            if (!go.scene.IsValid())
            {
                Debug.LogWarning($"[SafeDestroy] Prevented destroying non-scene GameObject: {go.name}");
                return;
            }
        }
        else if (obj is Component comp)
        {
            if (comp.gameObject != null && !comp.gameObject.scene.IsValid())
            {
                Debug.LogWarning($"[SafeDestroy] Prevented destroying component on non-scene GameObject: {comp.name}");
                return;
            }
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    // 미니맵 타일맵 관련 매서드
    public TileBase GetMiniMapTileByType(RoomType type)
    {
        switch (type)
        {
            default: return miniMapNormalTile;
        }
    }
    // 카메라 자동 정렬 헬퍼 함수
    public void AutoFocusMiniMapCamera(Camera miniMapCam)
    {
        if (miniMapCam == null || globalMiniMapTilemap == null) return;

        // 유니티 내장 기능으로 미니맵 타일 영역의 타일맵 렌더러 기준 Bounds를 통째로 가져옵니다.
        var tilemapRenderer = globalMiniMapTilemap.GetComponent<TilemapRenderer>();
        if (tilemapRenderer == null) return;

        Bounds mapBounds = tilemapRenderer.bounds;

        // 1. 카메라의 중심점을 생성된 맵 전체의 정중앙 좌표로 이동시킵니다.
        Vector3 centerPos = GameManager.Instance.PLAYERCONTROLLER.transform.position; // 플레이어 위치를 중심으로 시작
        centerPos.z = miniMapCam.transform.position.z; // 기존 카메라이 Z축 깊이(-100) 유지
        miniMapCam.transform.position = centerPos;

        miniMapCam.orthographicSize = 30.0f;
    }

    // 안개 타일 & 미니맵 타일 채우기 함수
    private void GenerateFogOfWar()
    {
        if (fogTilemap == null || blackTile == null) return;

        globalGroundTilemap.CompressBounds();
        BoundsInt bounds = globalGroundTilemap.cellBounds;

        // 일괄 배치를 위한 리스트
        List<Vector3Int> fogPositions = new List<Vector3Int>();
        List<TileBase> fogTiles = new List<TileBase>();

        List<Vector3Int> miniMapPositions = new List<Vector3Int>();
        List<TileBase> miniMapTiles = new List<TileBase>();

        for (int x = bounds.xMin - 5; x <= bounds.xMax + 5; x++)
        {
            for (int y = bounds.yMin - 5; y <= bounds.yMax + 5; y++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);

                bool hasGround = globalGroundTilemap.HasTile(pos);
                bool hasWall = (globalWallTilemap != null && globalWallTilemap.HasTile(pos));

                if (hasGround || hasWall)
                {
                    // 1. 안개 영역 채우기
                    fogPositions.Add(pos);
                    fogTiles.Add(blackTile);

                    // 2. 미니맵 전용 타일맵 채우기 (새 씬이라 무조건 깨끗한 상태임)
                    if (globalMiniMapTilemap != null && miniMapNormalTile != null)
                    {
                        miniMapPositions.Add(pos);
                        miniMapTiles.Add(miniMapNormalTile);
                    }
                }
            }
        }

        // 배열로 한 번에 쏴서 렉 없이 생성
        fogTilemap.SetTiles(fogPositions.ToArray(), fogTiles.ToArray());

        if (globalMiniMapTilemap != null && miniMapPositions.Count > 0)
        {
            globalMiniMapTilemap.SetTiles(miniMapPositions.ToArray(), miniMapTiles.ToArray());
            Debug.Log($"<color=green>[MapGenerator]</color> 미니맵 타일맵에 {miniMapPositions.Count}칸의 지도를 그렸습니다.");
        }
    }
}
