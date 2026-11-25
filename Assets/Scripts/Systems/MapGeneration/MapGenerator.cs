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
    internal bool IsBuilding(Tilemap ground) => _isGenerating && ground == globalGroundTilemap;
    public float GenerationProgress { get; private set; }
    public DungeonLayout CurrentGridLayout { get; private set; }
    public int? LastLayoutSeed { get; private set; }
    private int? _nextLayoutSeed;

    public void SetNextLayoutSeed(int seed) => _nextLayoutSeed = seed;

    [Header("데이터 연결")]
    [SerializeField] private MapGenerationDataSO generationData;
    [SerializeField] private RoomPrefabDataSO prefabData;

    [Header("디버그")]
    [Tooltip("맵 생성 뒤 Logs/MapDebugLog.txt에 전체 타일 지도를 기록합니다. 큰 방에서는 로그 한 번에 수십만 칸을 검사하므로, 연결 문제를 추적할 때만 켭니다.")]
    [SerializeField] private bool dumpMapDebugLog;

    [Tooltip("기본은 단계별 요약만 출력합니다. 켜면 방/타일/문/프레임 대기까지 상세 [MapPerf] 로그를 추가합니다. 경고와 오류는 이 설정과 무관하게 항상 출력합니다.")]
    [SerializeField] private bool logGenerationTimings = false;

    // 부모 구간은 자식 작업과 대기 시간을 포함한다. CPU 시간이나 서로 더할 수 있는 독립 구간이 아니다.
    internal static System.IDisposable MeasureGeneration(string label, bool summary = false)
    {
#if NECRO_RENDER_BENCH
        if (label.StartsWith("5.Ground", System.StringComparison.Ordinal) || label == "5.NavMesh collect sources")
            return new PixelRenderBenchmark.GenerationSample(label);
#endif
        return summary || (Instance != null && Instance.logGenerationTimings)
            ? new GenerationTiming(label, summary) : null;
    }

    private sealed class GenerationTiming : System.IDisposable
    {
        private static int nextId;
        private readonly string label;
        private readonly bool summary;
        private readonly int startFrame = Time.frameCount;
        private readonly System.Diagnostics.Stopwatch watch;

        public GenerationTiming(string name, bool summary = false)
        {
            this.summary = summary;
            label = summary ? name : $"#{++nextId} {name}";
            if (!summary) Write("BEGIN");
            watch = System.Diagnostics.Stopwatch.StartNew();
        }

        public void Dispose()
        {
            watch.Stop();
            if (summary)
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                    "[Map] {0} | {1:F2}초", label, watch.Elapsed.TotalSeconds);
            else
                Write($"END ms={watch.Elapsed.TotalMilliseconds:F2} frames={Time.frameCount - startFrame}");
        }

        private void Write(string result)
        {
            // 타이머 자체의 로그 비용을 줄이기 위해 호출 스택은 수집하지 않는다.
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                "[MapPerf] {0} | {1} | utc={2:HH:mm:ss.fff}Z frame={3} scale={4:F2} focused={5}",
                label, result, System.DateTime.UtcNow, Time.frameCount, Time.timeScale, Application.isFocused);
        }
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Map/Verify Generation Timing")]
    private static void VerifyGenerationTiming()
    {
        var messages = new List<string>();
        void Capture(string message, string stack, LogType type)
        {
            if (message.Contains("TimingSelfCheck")) messages.Add(message);
        }

        Application.logMessageReceived += Capture;
        try
        {
            try
            {
                using (new GenerationTiming("TimingSelfCheck"))
                    throw new System.InvalidOperationException("TimingSelfCheck");
            }
            catch (System.InvalidOperationException ex) when (ex.Message == "TimingSelfCheck") { }

            if (messages.Count != 2 || !messages[0].Contains(" | BEGIN |") ||
                !messages[1].Contains(" | END ms=") || !messages[1].Contains("frames=0"))
                throw new System.InvalidOperationException("Map timing BEGIN/END pairing failed.");

            messages.Clear();
            using (new GenerationTiming("TimingSelfCheck summary", summary: true)) { }
            if (messages.Count != 1 || !messages[0].StartsWith("[Map] ") || messages[0].Contains("BEGIN"))
                throw new System.InvalidOperationException("Map summary must emit exactly one line per stage.");
        }
        finally { Application.logMessageReceived -= Capture; }
        Debug.Log("[MapCheck] PASS: 단계 요약 1줄, 상세 측정 BEGIN/END 짝 및 예외 정리 검증.");
    }

    [UnityEditor.MenuItem("Tools/Map/Inspect Empty Navigation Sources")]
    private static void InspectEmptyNavigationSources()
    {
        if (!Application.isPlaying || Instance == null || !Instance.IsMapGenerationCompleted)
        {
            Debug.LogWarning("[MapCheck] 맵 생성이 끝난 플레이 모드에서 실행하세요.");
            return;
        }
        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null) return;
        // 패키지의 PhysicsColliders 수집 조건 그대로 검사한다. 생성/충돌 설정은 변경하지 않는다.
        foreach (var modifier in Object.FindObjectsByType<NavMeshModifier>(FindObjectsSortMode.None))
        {
            if (modifier.ignoreFromBuild || !modifier.AffectsAgentType(surface.agentTypeID) ||
                ((1 << modifier.gameObject.layer) & surface.layerMask.value) == 0) continue;
            var collider = modifier.GetComponent<Collider2D>();
            if (collider == null) continue;
            if (collider.compositeOperation != Collider2D.CompositeOperation.None)
                collider = collider.GetComponent<CompositeCollider2D>();
            if (collider == null) continue;
            Mesh mesh = collider.CreateMesh(false, false);
            if (mesh != null) { Object.DestroyImmediate(mesh); continue; }
            string path = collider.name;
            for (var parent = collider.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            var tiles = collider.GetComponent<Tilemap>();
            Debug.Log($"[MapCheck] mesh 없음: {path}; collider={collider.GetType().Name}, shapes={collider.shapeCount}, tileKinds={(tiles != null ? tiles.GetUsedTilesCount() : -1)}", collider);
        }
        var nav = NavMesh.CalculateTriangulation();
        Debug.Log($"[MapCheck] 생성 완료={Instance.IsMapGenerationCompleted}, NavMesh 정점={nav.vertices.Length}, 삼각형={nav.indices.Length / 3}");
    }
#endif


    [Header("통합 타일맵 연결")]
    [SerializeField] private Tilemap globalGroundTilemap;
    [SerializeField] private Tilemap globalWallTilemap;
    [SerializeField] private Tilemap globalShadowTilemap;
    [SerializeField] private Tilemap globalUnsteppableTilemap;
    public TileBase GroundTileAt(Vector3 position) => globalGroundTilemap != null
        ? globalGroundTilemap.GetTile(globalGroundTilemap.WorldToCell(position)) : null;
    public Color FootstepColor(Vector3 position)
    {
        var settings=PolishFeedback.Catalog;
        if (settings==null) return Color.white;
        if (TryGetNearbyWater(position,out _)) return settings.waterColor;
        var tile=globalGroundTilemap!=null?globalGroundTilemap.GetTile(globalGroundTilemap.WorldToCell(position)):null;
        string name=tile!=null?tile.name.ToLowerInvariant():"";
        return name.Contains("stone")||name.Contains("rock")||name.Contains("brick")?settings.stoneColor:settings.dirtColor;
    }

    public bool TryGetNearbyWater(Vector3 position, out Vector3 water)
    {
        water=position;
        if(globalUnsteppableTilemap==null) return false;
        var cell=globalUnsteppableTilemap.WorldToCell(position);
        for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
        {
            var c=cell+new Vector3Int(x,y,0);
            if(!globalUnsteppableTilemap.HasTile(c)) continue;
            var p=globalUnsteppableTilemap.GetCellCenterWorld(c);
            if(globalGroundTilemap!=null&&globalGroundTilemap.HasTile(globalGroundTilemap.WorldToCell(p))) continue;
            if(globalWallTilemap!=null&&globalWallTilemap.HasTile(globalWallTilemap.WorldToCell(p))) continue;
            water=p; return true;
        }
        return false;
    }

    // 미니맵 타일 추가
    [Header("미니맵 설정")]
    [SerializeField] private Tilemap globalMiniMapTilemap; // GlobalMiniMapTilemap 등록
    [SerializeField] private TileBase miniMapNormalTile;  // 일반 방/복도용 단색 타일

    public Tilemap GlobalMiniMapTilemap => globalMiniMapTilemap;

    /// <summary>
    /// 캐릭터가 착지할 수 있는 바닥인가. Ground가 있으면 그 아래 Unsteppable(물)은 허용하지만,
    /// Ground가 없거나 Wall이 겹친 자리는 허용하지 않는다.
    /// </summary>
    public bool TryGetGroundLandingPoint(Vector2 worldPos, float radius, out Vector2 landingPoint)
    {
        landingPoint = worldPos;
        return HasGroundSupport(worldPos, Vector2.one * (Mathf.Max(0f, radius) * 2f));
    }

    /// <summary>발 충돌체 전체(모서리 포함)가 Ground 위인지 검사한다. 물 위 Ground는 허용한다.</summary>
    public bool HasGroundSupport(Vector2 center, Vector2 size)
    {
        if (HasWallInBounds(center, size)) return false;
        if (globalGroundTilemap == null) return true;
        Vector2 half = Vector2.Max(Vector2.zero, size * .5f - Vector2.one * .0001f);
        var min = globalGroundTilemap.WorldToCell(center - half);
        var max = globalGroundTilemap.WorldToCell(center + half);
        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                if (!globalGroundTilemap.HasTile(new Vector3Int(x, y, min.z))) return false;
        return true;
    }

    public bool HasWallInBounds(Vector2 center, Vector2 size)
    {
        if (globalWallTilemap == null) return false;
        Vector2 half = Vector2.Max(Vector2.zero, size * .5f - Vector2.one * .0001f);
        var min = globalWallTilemap.WorldToCell(center - half);
        var max = globalWallTilemap.WorldToCell(center + half);
        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                if (globalWallTilemap.HasTile(new Vector3Int(x, y, min.z))) return true;
        return false;
    }

    /// <summary>진행선 중간에 Ground로 덮이지 않은 Unsteppable 타일이 있는지 확인한다.</summary>
    public bool HasUnsteppableBetween(Vector2 from, Vector2 dir, float distance)
    {
        if (globalUnsteppableTilemap == null || dir == Vector2.zero || distance <= 0f) return false;

        dir.Normalize();
        const float Step = 0.1f;
        int steps = Mathf.CeilToInt(distance / Step);
        for (int i = 1; i <= steps; i++)
        {
            Vector2 point = from + dir * Mathf.Min(i * Step, distance);
            bool unsteppable = globalUnsteppableTilemap.HasTile(globalUnsteppableTilemap.WorldToCell(point));
            bool ground = globalGroundTilemap != null &&
                          globalGroundTilemap.HasTile(globalGroundTilemap.WorldToCell(point));
            if (unsteppable && !ground)
                return true;
        }
        return false;
    }

    /// <summary>진행선에서 처음 만나는 Wall 타일 직전까지의 이동 가능 거리.</summary>
    public float GetDistanceBeforeWall(Vector2 from, Vector2 dir, float distance, float clearance = 0f)
    {
        if (globalWallTilemap == null || dir == Vector2.zero || distance <= 0f) return distance;

        dir.Normalize();
        const float Step = 0.1f;
        int steps = Mathf.CeilToInt(distance / Step);
        for (int i = 1; i <= steps; i++)
        {
            float d = Mathf.Min(i * Step, distance);
            Vector2 point = from + dir * d;
            if (!globalWallTilemap.HasTile(globalWallTilemap.WorldToCell(point))) continue;

            return Mathf.Max(0f, d - Step - Mathf.Max(0f, clearance));
        }
        return distance;
    }

    public List<RoomInstance> AllRooms => _allRooms;
    private RoomInstance _currentRoom;
    public RoomInstance CurrentRoom => _currentRoom;
    public void SetCurrentRoom(RoomInstance room)
    {
        if (_currentRoom == room) return;
        if (_currentRoom != null && _currentRoom.TryGetComponent<AmbientBirdFlock>(out var previousFlock)) previousFlock.SetPresent(false);
        _currentRoom = room;
        AmbientBirdFlock.Visit(room, LastLayoutSeed);
        UpdateTileAnimationScope(room);
    }

    public List<RoomInstance> GetConnectedRooms(RoomInstance room)
    {
        if (room != null && _masterAdjacency != null && _masterAdjacency.ContainsKey(room))
        {
            return _masterAdjacency[room];
        }
        return new List<RoomInstance>();
    }

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
    private int _placementAttempt;
    private string PrefabDiagnosticContext =>
        $"scene='{gameObject.scene.name}' floor={(GameManager.Instance != null ? GameManager.Instance.currentFloor : 1)} " +
        $"attempt={_placementAttempt} frame={Time.frameCount}";

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

    
    // ───────────────── 타일 애니메이션 스코프 제어 ─────────────────
    // 애니메이션 타일(물/횃불 등)은 화면 밖이어도 계속 갱신되므로, 플레이어가 있는 방과
    // 인접 방만 재생시키고 나머지는 정지시킨다.
    // 인접 방까지 켜는 이유: CameraManager 의 vcam 에 룸 컨파이너가 없어서 문 앞·복도에서
    // 옆 방이 화면에 같이 보인다. 현재 방만 켜면 문턱에서 옆 방 물이 '멈춰 있다 갑자기 움직이는'
    // 것이 그대로 보인다.
    private readonly HashSet<RoomInstance> _animActiveRooms = new HashSet<RoomInstance>();
    private readonly HashSet<RoomInstance> _animScopeBuffer = new HashSet<RoomInstance>();

    /// <summary>
    /// 맵 생성이 끝난 직후 1회. 각 방이 스탬프한 셀 중 실제 애니메이션 셀만 확정시키고,
    /// 전부 정지 상태로 만든 뒤 현재 스코프만 다시 켠다.
    /// </summary>
    public void FinalizeAllRoomTileAnimations()
    {
        _animActiveRooms.Clear();
        foreach (var r in _allRooms)
        {
            if (r != null) r.FinalizeAnimatedCells();
        }
        UpdateTileAnimationScope(_currentRoom);
    }

    /// <summary>
    /// 활성 집합 = 기준 방 + GetConnectedRooms 로 얻은 인접 방. 이전 집합과의 차분만 처리한다.
    /// ⚠️ 복도 타일은 CorridorPainter 가 글로벌 타일맵에 직접 칠하므로 어느 방에도 속하지 않는다.
    ///    따라서 복도의 애니메이션 타일은 항상 재생 상태로 남는다 (플레이어 이동 경로라 의도된 동작).
    /// </summary>
    public void UpdateTileAnimationScope(RoomInstance room)
    {
        if (!IsMapGenerationCompleted) return;

        _animScopeBuffer.Clear();
        if (room != null)
        {
            _animScopeBuffer.Add(room);
            var neighbors = GetConnectedRooms(room);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i] != null) _animScopeBuffer.Add(neighbors[i]);
            }
        }

        // 스코프에서 빠진 방 정지
        foreach (var r in _animActiveRooms)
        {
            if (r != null && !_animScopeBuffer.Contains(r)) r.SetTileAnimationsActive(false);
        }
        // 스코프에 들어온 방 재생 (SetTileAnimationsActive 내부에서 중복 호출은 조기 반환됨)
        foreach (var r in _animScopeBuffer)
        {
            r.SetTileAnimationsActive(true);
        }

        _animActiveRooms.Clear();
        foreach (var r in _animScopeBuffer) _animActiveRooms.Add(r);
    }
public System.Action OnMapGenerated;

    public void SetMapData(MapGenerationDataSO genData, RoomPrefabDataSO prefData)
    {
        if (genData != null) generationData = genData;
        if (prefData != null) prefabData = prefData;
    }

    /// <summary>
    /// 이번 층이 보스 층인가. 층수는 MapGenerationDataSO.floorBosses 하나만 보고 판단한다 —
    /// 여기서 "보스 방을 만들까"와 BossRoomEvent 의 "누구를 소환할까"가 같은 표를 읽어야
    /// 보스 없는 보스 방이나 보스 방 없는 보스가 생기지 않는다. (예전엔 여기만 4층으로 하드코딩)
    /// </summary>
    private bool IsBossFloor()
    {
        // BossRoomEvent.ResolveBossData 와 **글자 그대로 같은 조회**여야 한다. 여기만 true 가 되면
        // 보스가 안 나오는 보스 방이 생기고, 그 방은 적이 0마리라 진입 즉시 자동 클리어된다.
        // (디버그 시작 층은 GameManager.Awake 가 currentFloor 를 debugStartFloor 로 강제하므로
        //  여기서 따로 볼 필요가 없다 — 그 층이 표에 있으면 자동으로 보스 층이 된다.)
        var gm = GameManager.Instance;
        if (gm == null || gm.CurrentStageMapData == null) return false;
        return gm.CurrentStageMapData.GetBossForFloor(gm.currentFloor) != null;
    }

    /// <summary>이번 층의 방 개수. 층 조절표에 항목이 없으면 전역값이 그대로 나온다.</summary>
    private MapGenerationDataSO.RoomCounts FloorRoomCounts()
    {
        int floor = GameManager.Instance != null ? GameManager.Instance.currentFloor : 1;
        return generationData.GetRoomCountsForFloor(floor);
    }

    private void Awake()
    {
        
        // 방 진입 시점에 확실히 스코프가 갱신되도록 구독한다.
        // (SetCurrentRoom 은 미니맵 포커스 경로에서만 불려서 단독으로는 신뢰하기 어렵다)
        RoomInstance.OnPlayerEnteredRoom += SetCurrentRoom;
        Instance = this;
        _painter = gameObject.AddComponent<CorridorPainter>();
    }

    private void OnDestroy()
    {
        // static 이벤트라 해제하지 않으면 씬 전환 후 죽은 MapGenerator 가 계속 불린다.
        RoomInstance.OnPlayerEnteredRoom -= SetCurrentRoom;
        if (Instance == this) Instance = null;
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
        if (_isGenerating) yield break;
        yield return StartCoroutine(GenerationSequence());
    }

    // ==============================================================
    // 이 층의 엘리트 (맵을 만들기 전에 딱 한 번 정한다)
    // ==============================================================
    /// <summary>
    /// 이번 층에 나올 엘리트. 맵 생성 시작 시점에 정해지고, 두 곳이 이 값을 읽는다:
    ///   · 엘리트 방 프리팹 선택 (dedicatedRoomPrefab 이 있으면 그 방을 배치)
    ///   · EliteRoomEvent 의 실제 스폰 (같은 놈이 나와야 방과 몹이 안 어긋난다)
    /// 못 정했으면 null 이고, 그러면 양쪽 다 기존 방식(무작위 + 일반 Elite 방)으로 폴백한다.
    /// </summary>
    public EnemyMinionDataSO FloorElite { get; private set; }

    /// <summary>
    /// 이 층의 엘리트를 뽑는다. 디버그 강제값이 있으면 그걸 쓰고, 없으면 엘리트 풀에서 무작위.
    /// 풀은 EliteRoomEvent 가 쓰던 것과 같다(ELITE_MINION_DATA + ENEMY_MINION_DATA 중 IsSpawnableElite).
    /// </summary>
    private void PickFloorElite(System.Random random = null)
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.debugForcedElite != null)
        {
            FloorElite = gm.debugForcedElite;
            Debug.Log($"<color=yellow>[MapGenerator]</color> 이 층 엘리트 = '{FloorElite.minionName}' (디버그 강제 지정)");
            return;
        }

        var data = gm != null ? gm.dataManager : null;
        if (data == null) { FloorElite = null; return; }

        var pool = EligibleFloorElites(data);
        FloorElite = pool.Count > 0 ? pool[random?.Next(pool.Count) ?? Random.Range(0, pool.Count)] : null;
        Debug.Log(FloorElite != null
            ? $"<color=cyan>[MapGenerator]</color> 이 층 엘리트 = '{FloorElite.minionName}'"
            : "<color=orange>[MapGenerator]</color> 엘리트 풀이 비어 있어 이 층 엘리트를 정하지 못했다.");
    }

    public static List<EnemyMinionDataSO> EligibleFloorElites(DataManager data)
    {
        var pool = new List<EnemyMinionDataSO>();
        if (data?.ELITE_MINION_DATA != null)
            foreach (var e in data.ELITE_MINION_DATA) if (e != null && e.IsSpawnableElite) pool.Add(e);
        if (data?.ENEMY_MINION_DATA != null)
            foreach (var e in data.ENEMY_MINION_DATA) if (e != null && e.IsSpawnableElite && !pool.Contains(e)) pool.Add(e);

        return pool;
    }

    private IEnumerator GenerationSequence()
    {
        GenerationProgress = 0f;
        int? requestedSeed = _nextLayoutSeed;
        _nextLayoutSeed = null;
        LastLayoutSeed = null;
#if UNITY_EDITOR
        ProjectRooms.Reset(); // Include a collaborator's pull on the next generation.
#endif
        if (generationData != null && generationData.useIsaacStylePlacement)
        {
            int seed = WorkshopTrial.Active ? WorkshopTrial.Seed : requestedSeed ?? generationData.FixedLayoutSeed ?? Random.Range(0, int.MaxValue);
            LastLayoutSeed = seed;
            var random = new System.Random(seed);
            PickFloorElite(random);
            yield return StartCoroutine(IsaacStyleGenerationSequence(random));
            yield break;
        }

        PickFloorElite();
        IsMapGenerationCompleted = false;
        _isGenerating = true;

        int maxRegenAttempts = 10;
        int regenAttempt = 0;
        bool mapSuccess = false;
        bool isEliteArena = GameManager.Instance != null && GameManager.Instance.debugEliteArena;
        bool isBossFloor = !isEliteArena && IsBossFloor();

        while (!mapSuccess && regenAttempt < maxRegenAttempts)
        {
            regenAttempt++;
            _placementAttempt = regenAttempt;
            _currentPhaseIndex = 0;
            SetupTilemapLayers();
            ClearExistingMap();

            var rc = FloorRoomCounts();
            var specialTypes = DungeonLayoutPlanner.GetSpecialRoomTypes(rc, isBossFloor);
            int eliteCount = specialTypes.Count(type => type == RoomType.Elite);
            int normalCount = Mathf.Max(rc.minNormal, rc.total - 1 - specialTypes.Count); // -1 은 스폰 방

            int initialBranchCount = Random.Range(1, 5);
            List<RoomType> phase1 = new List<RoomType> { RoomType.Spawn };
            for (int i = 0; i < initialBranchCount; i++) phase1.Add(RoomType.Normal);
            yield return StartCoroutine(RunPhase(phase1));

            int remainingNormal = normalCount - initialBranchCount;

            _currentPhaseIndex++;
            List<RoomType> phase2 = new List<RoomType>();
            for (int i = 0; i < rc.shop; i++) phase2.Add(RoomType.Shop);
            for (int i = 0; i < rc.enhanceShop; i++) phase2.Add(RoomType.EnhanceShop);
            int eliteHalf = eliteCount / 2;
            for (int i = 0; i < eliteHalf; i++) phase2.Add(RoomType.Elite);
            int p2Normal = remainingNormal > 0 ? Random.Range(1, remainingNormal / 2 + 2) : 0;
            for (int i = 0; i < p2Normal; i++) phase2.Add(RoomType.Normal);
            remainingNormal -= p2Normal;
            if (phase2.Count > 0) yield return StartCoroutine(RunPhase(phase2));

            _currentPhaseIndex++;
            List<RoomType> phase3 = new List<RoomType>();
            int eliteRest = eliteCount - eliteHalf;
            for (int i = 0; i < eliteRest; i++) phase3.Add(RoomType.Elite);
            if (isBossFloor) phase3.Add(RoomType.Boss);
            // 증강 방은 중반에 둔다. 스폰 바로 옆이면 아직 아무것도 없는 상태로 가혹 페널티를 고르게 된다.
            for (int i = 0; i < rc.augment; i++) phase3.Add(RoomType.Augment);
            for (int i = 0; i < remainingNormal; i++) phase3.Add(RoomType.Normal);
            if (phase3.Count > 0) yield return StartCoroutine(RunPhase(phase3));

            _currentPhaseIndex++;
            List<RoomType> phase4 = new List<RoomType>();
            for (int i = 0; i < rc.reward; i++) phase4.Add(RoomType.Reward);
            if (phase4.Count > 0) yield return StartCoroutine(RunPhase(phase4));

            // 모든 방의 물리 분산 및 타일맵 병합이 완료된 후 단 한번 복도 연결 수행!
            yield return StartCoroutine(ConnectUnreachedRoomsCoroutine());

            // 최종 모든 방이 온전히 다 연결되었는지 검증
            int totalIsolated = _allRooms.Count - _reachedRooms.Count;
            bool bossConnected = !isBossFloor ||
                (_allRooms.Count(room => room.roomType == RoomType.Boss) == 1 &&
                 _reachedRooms.Any(room => room.roomType == RoomType.Boss) &&
                 RoomConnectivity.HasRequiredAccess(_allRooms, _masterAdjacency, requiresBoss: true));
            if (totalIsolated == 0 && bossConnected)
            {
                mapSuccess = true;
            }
            else
            {
                Debug.LogWarning($"<color=orange>[MapGenerator]</color> Map attempt {regenAttempt} failed: isolated={totalIsolated}, bossConnected={bossConnected}. Re-generating entire map...");
            }
        }

        if (!mapSuccess)
        {
            if (isBossFloor)
            {
                Debug.LogError($"[MapGenerator] 보스 방이 연결된 층을 {maxRegenAttempts}회 안에 생성하지 못했습니다. 방 프리팹과 문 앵커를 확인하세요.");
                _isGenerating = false;
                yield break;
            }
            Debug.LogError($"<color=red>[MapGenerator]</color> Failed to generate a fully connected map after {maxRegenAttempts} attempts! Culling remaining isolated rooms as fallback.");
            CullIsolatedRooms();
        }

        GenerationProgress = 0.6f;
        AssignSpecialRooms();
        DumpMapToLog();

        yield return SetupFinalColliders();
        GenerationProgress = 0.8f;
        yield return BakeNavMesh();

        // 물리 분산 모드 시 미니맵 위치 정밀 전사 및 다리(길) 렌더링 조건을 위해 월드 좌표 기반 가상 그리드 좌표 갱신
        float spacing = generationData.gridSpacing > 0 ? generationData.gridSpacing : 160f;
        foreach (var room in _allRooms)
        {
            room.gridPosition = new Vector2Int(
                Mathf.RoundToInt(room.transform.position.x / spacing),
                Mathf.RoundToInt(room.transform.position.y / spacing)
            );
        }

        // 일반 전투 방들의 보상 수량을 지정된 개수대로 무작위 분배 및 안배
        DistributeNormalRoomRewards();

        PlacePlayerAtSpawn();

        if (_tempObstacle != null) SafeDestroy(_tempObstacle);
        _isGenerating = false;

        // 맵 생성 완료 플래그 설정 및 이벤트 호출
        RoomShadowCasters.Bind(this);
        WallContactShadows.Rebuild(globalGroundTilemap, globalWallTilemap, globalShadowTilemap);
        IsMapGenerationCompleted = true;
        FinalizeAllRoomTileAnimations();
        OnMapGenerated?.Invoke();
        GenerationProgress = 1f;
        Debug.Log("<color=green>[MapGenerator]</color> Map Generation Completed.");
    }

    private IEnumerator SetupFinalColliders()
    {
        using var timing = MeasureGeneration("4.Wall colliders TOTAL");
        if (globalWallTilemap == null) yield break;
        GameObject wallObj = globalWallTilemap.gameObject;

        // 활성 타일맵에 타일 콜라이더부터 붙이면 합성 전 수만 개 형상/접촉을 먼저 만든다.
        // 비활성 상태에서 Manual 합성을 먼저 준비하고, 모두 연결한 뒤 한 번만 생성한다.
        bool wasActive = wallObj.activeSelf;
        TilemapCollider2D tileCol;
        CompositeCollider2D comp;
        using (MeasureGeneration("4.Wall suspend"))
            wallObj.SetActive(false);
        try
        {
            using (MeasureGeneration("4.Wall Rigidbody2D get/add + configure"))
            {
                Rigidbody2D rb = wallObj.GetComponent<Rigidbody2D>();
                if (rb == null) rb = wallObj.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Static;
            }

            using (MeasureGeneration("4.Wall CompositeCollider2D get/add + configure"))
            {
                comp = wallObj.GetComponent<CompositeCollider2D>();
                if (comp == null) comp = wallObj.AddComponent<CompositeCollider2D>();
                comp.generationType = CompositeCollider2D.GenerationType.Manual;
                comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
            }

            using (MeasureGeneration("4.Wall TilemapCollider2D get/add + configure"))
            {
                tileCol = wallObj.GetComponent<TilemapCollider2D>();
                if (tileCol == null) tileCol = wallObj.AddComponent<TilemapCollider2D>();
                tileCol.compositeOperation = Collider2D.CompositeOperation.Merge;
            }
        }
        finally
        {
            using (MeasureGeneration("4.Wall resume (merged colliders)"))
                wallObj.SetActive(wasActive);
        }

        yield return null;
        using (MeasureGeneration("4.Wall ProcessTilemapChanges"))
            tileCol.ProcessTilemapChanges();
        yield return null;
        using (MeasureGeneration("4.Wall GenerateGeometry"))
            comp.GenerateGeometry();
        using (MeasureGeneration("4.Wall SyncTransforms"))
            Physics2D.SyncTransforms();
    }

    private IEnumerator BakeNavMesh()
    {
        using var timing = MeasureGeneration("5.NavMesh TOTAL");
        var navSurface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (navSurface == null) yield break;

        // NavMeshPlus의 RenderMeshes 모드는 타일 하나를 BuildSource 하나로 만든다.
        // 큰 방에서는 수만 개가 되므로, Ground도 잠깐 합성 콜라이더로 묶어 1개 소스로 굽는다.
        Rigidbody2D groundBody = null;
        TilemapCollider2D groundTileCollider = null;
        CompositeCollider2D groundComposite = null;
        NavMeshCollectGeometry previousGeometry = navSurface.useGeometry;

        try
        {
            if (globalGroundTilemap != null)
            {
                GameObject ground = globalGroundTilemap.gameObject;
                // Configure all colliders while inactive, then restore the ground before yielding.
                bool wasActive = ground.activeSelf;
                using (MeasureGeneration("5.Ground suspend"))
                    ground.SetActive(false);
                try
                {
                    using (MeasureGeneration("5.Ground Rigidbody2D add + configure"))
                    {
                        groundBody = ground.AddComponent<Rigidbody2D>();
                        groundBody.bodyType = RigidbodyType2D.Static;
                    }

                    using (MeasureGeneration("5.Ground CompositeCollider2D add + configure"))
                    {
                        groundComposite = ground.AddComponent<CompositeCollider2D>();
                        groundComposite.generationType = CompositeCollider2D.GenerationType.Manual;
                        groundComposite.isTrigger = true;
                        groundComposite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                    }

                    using (MeasureGeneration("5.Ground TilemapCollider2D add + configure"))
                    {
                        groundTileCollider = ground.AddComponent<TilemapCollider2D>();
                        groundTileCollider.isTrigger = true;
                        groundTileCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
                    }
                }
                finally
                {
                    using (MeasureGeneration("5.Ground resume (merged colliders)"))
                        ground.SetActive(wasActive);
                }

                yield return null;
                using (MeasureGeneration("5.Ground ProcessTilemapChanges"))
                    groundTileCollider.ProcessTilemapChanges();
                yield return null;
                using (MeasureGeneration("5.Ground GenerateGeometry"))
                    groundComposite.GenerateGeometry();
                using (MeasureGeneration("5.Ground SyncTransforms"))
                    Physics2D.SyncTransforms();
                yield return null;
            }

            navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            AsyncOperation navigation;
            using (MeasureGeneration("5.NavMesh collect sources"))
                navigation = navSurface.BuildNavMeshAsync();
            if (navigation != null) yield return navigation;
            yield return null;
        }
        finally
        {
            using var cleanupTiming = MeasureGeneration("5.Ground temporary colliders schedule destruction");
            navSurface.useGeometry = previousGeometry;
            // Removing the composite first can re-create every individual ground tile's physics shape.
            if (groundTileCollider != null)
            {
                groundTileCollider.enabled = false;
                Destroy(groundTileCollider);
            }
            if (groundComposite != null)
            {
                groundComposite.enabled = false;
                Destroy(groundComposite);
            }
            if (groundBody != null) Destroy(groundBody);
        }
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
        if (WorkshopTrial.RoomTest && _allRooms.Count > 0 && GameManager.Instance?.PLAYERCONTROLLER != null)
        {
            WorkshopTrial.PositionPlayer(this,GameManager.Instance.PLAYERCONTROLLER);
            return;
        }
        // [아레나 모드] 스폰 방 대신 엘리트 방 안에 바로 떨어뜨린다. 씬을 재생하자마자 싸우기 위한 것.
        if (GameManager.Instance != null && GameManager.Instance.debugEliteArena
            && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            RoomInstance arena = _allRooms.Find(r => r.roomType == RoomType.Elite);
            if (arena != null)
            {
                // 정중앙이 아니라 아래쪽으로 비켜 놓는다. 한가운데면 착지하는 프레임에 전투 트리거가 터져
                // 카메라가 아직 못 따라온 상태로 몹이 쏟아진다.
                Vector3 center = arena.transform.position + (Vector3)arena.centerOffset;
                Vector3 pos = center + Vector3.down * Mathf.Clamp(arena.roomSize.y * 0.3f, 2f, 6f);
                if (NavMesh.SamplePosition(pos, out NavMeshHit arenaHit, 5f, NavMesh.AllAreas)) pos = arenaHit.position;

                GameManager.Instance.PLAYERCONTROLLER.transform.position = pos;
                SetCurrentRoom(arena);
                arena.ForceEnter();
                Debug.Log("<color=yellow>[MapGenerator]</color> 아레나 모드: 플레이어를 엘리트 방 안에 배치했다.");
                return;
            }
        }

        RoomInstance spawnRoom = _allRooms.Find(r => r.roomType == RoomType.Spawn);
        if (spawnRoom == null) return;
        var spawnEvent = spawnRoom.GetComponent<SpawnRoomEvent>();
        Vector3 spawnPos = spawnEvent != null ? spawnEvent.GetSpawnPosition() : spawnRoom.transform.position;
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            GameManager.Instance.PLAYERCONTROLLER.transform.position = spawnPos;

            // 시작 방 진입 로직 강제 실행 (음악 재생 등)
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
            room.MergeTilesToGlobal(globalGroundTilemap, globalWallTilemap, globalShadowTilemap, globalUnsteppableTilemap);
        }
        UpdateGlobalBoundingObstacle();
        // 각 페이즈 단위 즉시 연결을 제거하고, 모든 방의 배치가 완료된 최종 시점에 한번에 복도를 연결하도록 합니다.
        yield return null;
    }

    private RoomInstance CreateRoom(RoomType type, float angle)
    {
        GameObject prefab = prefabData.GetRandomPrefab(type, PrefabDiagnosticContext);
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
        if (!WorkshopTrial.Active) ProjectRooms.Apply(prefab, room, LastLayoutSeed ?? 0, new Vector2Int(_allRooms.Count, 0));
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

            _painter.Init(globalGroundTilemap, globalWallTilemap, globalShadowTilemap, globalUnsteppableTilemap, generationData.floorTile, generationData.wallTile, generationData.shadowTile);

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
                        if (r.roomType == RoomType.Boss) continue; // 보스 뒤로 필수 경로가 이어지면 처치 후 방문할 수 없다.
                        for (int uIndex = 0; uIndex < phaseUnreached.Count; uIndex++)
                        {
                            var u = phaseUnreached[uIndex];
                            if ((u.roomType == RoomType.Shop || u.roomType == RoomType.EnhanceShop || u.roomType == RoomType.Elite || u.roomType == RoomType.Boss) && r.roomType == RoomType.Spawn)
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
                var validReached = _reachedRooms.Where(r => r.debugDepth != -1 && r.roomType != RoomType.Boss).ToList();
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

                    // 조건 B: 상점·엘리트·보스 방은 스폰 바로 옆에 연결하지 않는다.
                    foreach (var room in _allRooms)
                    {
                        if ((room.roomType == RoomType.Shop || room.roomType == RoomType.EnhanceShop || room.roomType == RoomType.Elite || room.roomType == RoomType.Boss) && room.debugDepth < 2)
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
                if (door != null) SafeDestroy(door.gameObject);
            }
            room.doorObjects.Clear();
        }
    }

    private void CullIsolatedRooms()
    {
        List<RoomInstance> isolatedRooms = _allRooms.Where(r => !_reachedRooms.Contains(r)).ToList();
        foreach (var room in isolatedRooms)
        {
            room.EraseTilesFromGlobal(globalGroundTilemap, globalWallTilemap, globalShadowTilemap, globalUnsteppableTilemap);
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

    internal IEnumerator RetireFloorRooms()
    {
        // The save is committed and the portal fully covers the outgoing floor.
        IsMapGenerationCompleted = false;
        _currentRoom = null;
        for (int i = _allRooms.Count - 1; i >= 0; i--)
        {
            var room = _allRooms[i];
            _allRooms.RemoveAt(i);
            if (room == null) continue;
            room.gameObject.SetActive(false);
            Destroy(room.gameObject);
            yield return null;
        }
    }

    private void ClearExistingMap()
    {
        if (globalGroundTilemap != null && globalGroundTilemap.TryGetComponent<WorldFloorMask>(out var mask))
            mask.BeginFloorGeneration();
        _corridorLengths.Clear();
        _rewardConnectionDebugLogs.Clear();
        _currentRoom = null;
        _tutorialLastRoom = null;
        CurrentGridLayout = null;
        _animActiveRooms.Clear();
        _animScopeBuffer.Clear();
        if (UIBasedMiniMap.Instance != null) UIBasedMiniMap.Instance.ClearMap();
        foreach (var room in _allRooms)
        {
            if (room == null) continue;
            room.gameObject.SetActive(false);
            SafeDestroy(room.gameObject);
        }
        _allRooms.Clear(); _reachedRooms.Clear(); _masterAdjacency.Clear(); _intendedDirs.Clear();
        if (_tempObstacle != null) SafeDestroy(_tempObstacle);
        if (globalGroundTilemap != null) globalGroundTilemap.ClearAllTiles();
        if (globalWallTilemap != null) globalWallTilemap.ClearAllTiles();
        if (globalShadowTilemap != null) globalShadowTilemap.ClearAllTiles();
        if (globalUnsteppableTilemap != null) globalUnsteppableTilemap.ClearAllTiles();

        if (globalMiniMapTilemap != null) globalMiniMapTilemap.ClearAllTiles(); // alslaoq
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

                // 두 방 사이의 실제 복도 타일 수(길이)를 캐시에 저장
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
            Vector3 position = anchor.transform.position - (Vector3)(Vector2)anchor.direction * anchor.doorInset;
            GameObject doorObj = Instantiate(doorPrefab, position, Quaternion.Euler(0, 0, rotation), room.transform);
            doorObj.name = $"Door_{anchor.direction}_{room.name}";
            room.doorObjects.Add(doorObj.GetComponent<DoorUpController>());

            // 포탈은 열린 상태에서도 기둥과 빛이 보여야 하므로 충돌 장벽만 끈다.
            // 구형 문 프리팹은 기존처럼 전체를 비활성화한다.
            var controller = doorObj.GetComponent<DoorUpController>();
            if (controller != null && doorObj.GetComponentInChildren<PortalDoorVisual>(true) != null)
                controller.OpenDoor();
            else
                doorObj.SetActive(false);
        }
    }

    private void SetupTilemapLayers() { ConfigureTilemap(globalGroundTilemap, "Ground"); ConfigureTilemap(globalWallTilemap, "Wall"); ConfigureTilemap(globalShadowTilemap, "Shadow"); ConfigureTilemap(globalMiniMapTilemap, "MiniMap"); ConfigureTilemap(globalUnsteppableTilemap, "Unsteppable"); /*미니맵 추가*/ }
    private void ConfigureTilemap(Tilemap tm, string layerName)
    {
        if (tm == null) return;
        int layer = LayerMask.NameToLayer(layerName);
        if (layer != -1) tm.gameObject.layer = layer;

        // 미니맵 소팅 오더를 가장 위로 올림
        if (layerName == "MiniMap")
        {
            var tr = tm.GetComponent<TilemapRenderer>();
            if (tr != null)
            {
                tr.sortingOrder = 100;
            }
        }

        if (layerName == "Wall")
        {
            var tr = tm.GetComponent<TilemapRenderer>();
            if (tr != null)
            {
                tr.sortingLayerName = "Ground";
                tr.sortingOrder = 1;
            }
        }

        if (layerName == "Unsteppable")
        {
            var tr = tm.GetComponent<TilemapRenderer>();
            if (tr != null)
            {
                // 물/낭떠러지처럼 바닥 아래에 깔리는 통행 불가 배경층.
                tr.sortingLayerName = "Ground";
                tr.sortingOrder = -1;
            }
        }
    }
    private void AssignSpecialRooms() { }
    private void DumpMapToLog()
    {
      if (!dumpMapDebugLog) return;

      try {
        if (globalGroundTilemap == null || globalWallTilemap == null) return;
        globalGroundTilemap.CompressBounds(); globalWallTilemap.CompressBounds();
        BoundsInt bounds = globalGroundTilemap.cellBounds; BoundsInt wallBounds = globalWallTilemap.cellBounds;
        int xMin = Mathf.Min(bounds.xMin, wallBounds.xMin), xMax = Mathf.Max(bounds.xMax, wallBounds.xMax), yMin = Mathf.Min(bounds.yMin, wallBounds.yMin), yMax = Mathf.Max(bounds.yMax, wallBounds.yMax);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"Map Bounds: X({xMin} to {xMax}), Y({yMin} to {yMax})");
        sb.AppendLine("Legend: [P: Spawn], [S: Shop], [R: Reward], [E: Elite], [N: Normal]");
        bool isaacMode = generationData != null && generationData.useIsaacStylePlacement;
        sb.AppendLine($"Generation Mode: {(isaacMode ? "Isaac (grid / teleporter doors)" : "Physics (drawn corridors)")}");
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
            sb.AppendLine($"  - GridPosition (미니맵 배치/연결선 기준): {room.gridPosition}");
            sb.AppendLine($"  - Size: {room.roomSize}, CenterOffset: {room.centerOffset}");
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

        // --- [추가] 인접 그래프 덤프: 미니맵이 '연결됨'으로 읽는 바로 그 데이터(_masterAdjacency) ---
        sb.AppendLine("\n==================== ADJACENCY GRAPH (미니맵이 읽는 연결 관계) ====================");
        foreach (var room in _allRooms)
        {
            var neighbors = GetConnectedRooms(room);
            sb.Append($"{room.name} [{room.roomType}:{room.debugDepth}] grid{room.gridPosition} -> ");
            if (neighbors == null || neighbors.Count == 0) { sb.AppendLine("(none)"); continue; }
            for (int i = 0; i < neighbors.Count; i++)
            {
                var n = neighbors[i];
                if (n == null) { sb.Append("(null) "); continue; }
                sb.Append($"{n.name}[{n.roomType}:{n.debugDepth}]grid{n.gridPosition}{(i < neighbors.Count - 1 ? ", " : "")}");
            }
            sb.AppendLine();
        }

        // --- [추가] 연결 감사(CONNECTION AUDIT): 미니맵 엣지마다 실제 통행 가능한 문/통로가 있는지 교차검증 ---
        // "미니맵엔 연결됐는데 방엔 문이 없는" 팬텀 엣지를 한눈에 잡기 위한 섹션.
        //  - 각 엣지의 '사용된' 앵커 쌍을 추정해 서로 마주보는지(facing), 앵커 간 셀 거리(gap),
        //    물리 모드면 실제 통로 길이(corridorLen)를 함께 찍는다.
        //  - SUSPECT = 사용된 앵커를 못 찾음 / 서로 안 마주봄 / (물리 모드) 통로 길이 0  → 팬텀 후보.
        sb.AppendLine("\n==================== CONNECTION AUDIT (엣지 <-> 실제 문 교차검증) ====================");
        sb.AppendLine("A.usedAnchor(Dir) <-> B.usedAnchor(Dir) | facing | anchorGap | corridorLen | VERDICT");
        var auditedEdges = new HashSet<string>();
        int suspectCount = 0;
        foreach (var a in _allRooms)
        {
            foreach (var b in GetConnectedRooms(a))
            {
                if (b == null) continue;
                string key = a.GetInstanceID() < b.GetInstanceID() ? $"{a.GetInstanceID()}_{b.GetInstanceID()}" : $"{b.GetInstanceID()}_{a.GetInstanceID()}";
                if (!auditedEdges.Add(key)) continue; // 무방향 엣지 중복 제거

                RoomAnchor anA = FindUsedAnchorToward(a, b);
                RoomAnchor anB = FindUsedAnchorToward(b, a);
                int corridorLen = GetCorridorLen(a, b);

                string dirAStr = anA != null ? anA.direction.ToString() : "NO-USED-ANCHOR";
                string dirBStr = anB != null ? anB.direction.ToString() : "NO-USED-ANCHOR";
                bool facing = anA != null && anB != null && anA.direction == -anB.direction;
                string gapStr = "-";
                if (anA != null && anB != null)
                {
                    Vector3Int ca = globalGroundTilemap.WorldToCell(anA.transform.position);
                    Vector3Int cb = globalGroundTilemap.WorldToCell(anB.transform.position);
                    gapStr = (Mathf.Abs(ca.x - cb.x) + Mathf.Abs(ca.y - cb.y)).ToString();
                }
                string corridorStr = corridorLen > 0 ? corridorLen.ToString() : (isaacMode ? "n/a(teleporter)" : "MISSING");

                bool suspect = anA == null || anB == null || !facing || (!isaacMode && corridorLen <= 0);
                if (suspect) suspectCount++;

                sb.AppendLine($"{a.name}.{dirAStr} <-> {b.name}.{dirBStr} | facing={facing} | gap={gapStr} | corridor={corridorStr} | {(suspect ? ">>> SUSPECT" : "ok")}");
            }
        }
        sb.AppendLine($"\nSuspect edges: {suspectCount}  (facing=false / NO-USED-ANCHOR / (물리모드)corridor=MISSING => '미니맵엔 연결·방엔 문 없음' 후보)");

        string logDirectory = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
        System.IO.Directory.CreateDirectory(logDirectory);
        string path = System.IO.Path.Combine(logDirectory, "MapDebugLog.txt");
        System.IO.File.WriteAllText(path, sb.ToString());
      }
      catch (System.Exception _dumpEx)
      {
          // 디버그 로그는 생성 파이프라인을 절대 막으면 안 된다. 예외는 삼키고 생성은 계속.
          Debug.LogWarning($"[MapGenerator] DumpMapToLog 실패(비치명적, 생성엔 영향 없음): {_dumpEx.Message}");
      }
    }

    /// <summary>두 방 사이 통로 길이를 (순서 무관) 조회. 없으면 0.</summary>
    private int GetCorridorLen(RoomInstance a, RoomInstance b)
    {
        if (_corridorLengths == null) return 0;
        if (_corridorLengths.TryGetValue(System.Tuple.Create(a, b), out int len)) return len;
        if (_corridorLengths.TryGetValue(System.Tuple.Create(b, a), out len)) return len;
        return 0;
    }

    /// <summary>from 방의 '사용된(isUsed)' 앵커 중 to 방 중심에 가장 가까운 것(=이 엣지에 쓰였을 앵커)을 추정.</summary>
    private RoomAnchor FindUsedAnchorToward(RoomInstance from, RoomInstance to)
    {
        RoomAnchor best = null;
        float bestD = float.MaxValue;
        Vector3 toCenter = to.transform.position + (Vector3)to.centerOffset;
        foreach (var an in from.anchors)
        {
            if (an == null || !an.isUsed) continue;
            float d = Vector2.Distance(an.transform.position, toCenter);
            if (d < bestD) { bestD = d; best = an; }
        }
        return best;
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
    public void DrawRoomOnMinimap(RoomInstance room)
    {
        if (globalMiniMapTilemap == null || room == null) return;

        // 아이작 배치 모드 여부와 관계없이 미니맵 실시간 드로잉과 다리(길) 렌더링을 일괄 가동합니다.

        // 방 타입에 따른 밝고 선명한 미니맵 전용 타일 획득
        TileBase miniMapTile = GetMiniMapTileByType(room.roomType);
        if (miniMapTile == null) miniMapTile = miniMapNormalTile; // 1차 방어 코드
        if (miniMapTile == null && generationData != null) miniMapTile = generationData.floorTile; // 2차 안전 Fallback (바닥 타일 치환)
        if (miniMapTile == null) return; // 에셋이 아예 없는 경우는 스킵

        // Spacing을 12(촘촘한 논리 정렬)로 지정합니다.
        int minimapSpacing = 12;

        List<Vector3Int> miniMapPositions = new List<Vector3Int>();
        List<TileBase> miniMapTiles = new List<TileBase>();

        // 방의 실제 월드 중심 좌표
        Vector3 roomCenterWorldPos = room.transform.position + (Vector3)room.centerOffset;

        // 방 내부 타일맵들을 획득하여 분석
        Tilemap[] childTilemaps = room.GetComponentsInChildren<Tilemap>();
        foreach (var tm in childTilemaps)
        {
            // 방 모양(바닥 영역)만 선명하고 밝게 표시하기 위해 "Ground" 계열 타일맵만 복사
            // 벽(Wall)이나 그림자 등은 미니맵 가독성을 위해 제외하여 경계를 깔끔하게 만듭니다.
            if (!tm.name.Contains("Ground")) continue;

            tm.CompressBounds();
            BoundsInt bounds = tm.cellBounds;

            foreach (var pos in bounds.allPositionsWithin)
            {
                TileBase tile = tm.GetTile(pos);
                if (tile != null)
                {
                    // 투명한 벽 스프라이트의 빈 부분을 메우려고 같은 자리에 깐 Ground 는
                    // 실제 방 바닥이 아니다. 미니맵에서는 Wall 과 겹친 Ground 를 제외한다.
                    Vector3 tileCenter = tm.GetCellCenterWorld(pos);
                    if (room.wallTilemap != null &&
                        room.wallTilemap.HasTile(room.wallTilemap.WorldToCell(tileCenter)))
                        continue;

                    // 월드 좌표 기준 방 중심과의 상대 오프셋 계산
                    Vector3 tileWorldPos = tm.CellToWorld(pos);
                    int localX = Mathf.RoundToInt(tileWorldPos.x - roomCenterWorldPos.x);
                    int localY = Mathf.RoundToInt(tileWorldPos.y - roomCenterWorldPos.y);

                    // 독립 격리 공간(-1000, -1000)으로 오프셋을 이전하여 월드 안개 및 라이팅 간섭을 완벽히 격리합니다.
                    int minimapOffsetX = -1000;
                    int minimapOffsetY = -1000;
                    Vector3Int miniMapCell = new Vector3Int(
                        minimapOffsetX + room.gridPosition.x * minimapSpacing + localX,
                        minimapOffsetY + room.gridPosition.y * minimapSpacing + localY,
                        0
                    );

                    miniMapPositions.Add(miniMapCell);
                    
                    // 실제 어두컴컴한 바닥 타일 비주얼 대신 밝은 미니맵 전용 단색 타일로 교체
                    miniMapTiles.Add(miniMapTile);
                }
            }
        }

        if (miniMapPositions.Count > 0)
        {
            room.isRevealedOnMinimap = true; // 미니맵에 그려진 상태임을 기록

            globalMiniMapTilemap.SetTiles(miniMapPositions.ToArray(), miniMapTiles.ToArray());
            globalMiniMapTilemap.RefreshAllTiles();
            if (logGenerationTimings)
                Debug.Log($"<color=green>[MapGenerator]</color> [아이작 미니맵] 방 {room.name}을 미니맵에 정밀 전사했습니다. ({miniMapPositions.Count}칸)");

            // 방문 여부가 아니라, 미니맵 상에 이미 노출되어 떠 있는 방들(isRevealedOnMinimap) 간의 연결 통로를 흰색 타일막대로 드로잉
            var connected = GetConnectedRooms(room);
            foreach (var conn in connected)
            {
                if (conn != null && conn.isRevealedOnMinimap)
                {
                    DrawConnectionOnMinimap(room, conn, miniMapTile);
                }
            }
        }
    }

    /// <summary>
    /// 미니맵 상에서 두 방의 그리드 가상 격자 사이를 잇는 통로 다리를 흰색 막대 타일로 그립니다.
    /// </summary>
    private void DrawConnectionOnMinimap(RoomInstance roomA, RoomInstance roomB, TileBase tile)
    {
        if (globalMiniMapTilemap == null || tile == null) return;

        int minimapSpacing = 12;
        int minimapOffsetX = -1000;
        int minimapOffsetY = -1000;

        Vector2Int gridA = roomA.gridPosition;
        Vector2Int gridB = roomB.gridPosition;

        // 두 방의 미니맵 격자 중심 좌표
        int centerA_X = minimapOffsetX + gridA.x * minimapSpacing;
        int centerA_Y = minimapOffsetY + gridA.y * minimapSpacing;
        int centerB_X = minimapOffsetX + gridB.x * minimapSpacing;
        int centerB_Y = minimapOffsetY + gridB.y * minimapSpacing;

        List<Vector3Int> bridgePositions = new List<Vector3Int>();
        List<TileBase> bridgeTiles = new List<TileBase>();

        if (gridA.y == gridB.y)
        {
            // 가로 연결 (두 방 사이의 빈 틈 복도)
            int startX = Mathf.Min(centerA_X, centerB_X);
            int endX = Mathf.Max(centerA_X, centerB_X);
            
            for (int x = startX + 3; x <= endX - 3; x++)
            {
                // 두께 2칸짜리 가로 연결 막대
                bridgePositions.Add(new Vector3Int(x, centerA_Y, 0));
                bridgePositions.Add(new Vector3Int(x, centerA_Y - 1, 0));
                
                bridgeTiles.Add(tile);
                bridgeTiles.Add(tile);
            }
        }
        else if (gridA.x == gridB.x)
        {
            // 세로 연결 (두 방 사이의 빈 틈 복도)
            int startY = Mathf.Min(centerA_Y, centerB_Y);
            int endY = Mathf.Max(centerA_Y, centerB_Y);
            
            for (int y = startY + 3; y <= endY - 3; y++)
            {
                // 두께 2칸짜리 세로 연결 막대
                bridgePositions.Add(new Vector3Int(centerA_X, y, 0));
                bridgePositions.Add(new Vector3Int(centerA_X - 1, y, 0));
                
                bridgeTiles.Add(tile);
                bridgeTiles.Add(tile);
            }
        }

        if (bridgePositions.Count > 0)
        {
            globalMiniMapTilemap.SetTiles(bridgePositions.ToArray(), bridgeTiles.ToArray());
        }
    }

    public void UpdateMiniMapCameraFocus(RoomInstance currentRoom)
    {
        if (currentRoom == null) return;

        // 플레이어의 현재 위치 방 정보 갱신
        SetCurrentRoom(currentRoom);

        // 신형 UI 기반 미니맵 실시간 갱신 실행!
        if (UIBasedMiniMap.Instance != null)
        {
            UIBasedMiniMap.Instance.RefreshMap();
        }

        Debug.Log($"<color=cyan>[MapGenerator]</color> 플레이어가 방 {currentRoom.name}에 진입하여 UI 미니맵을 실시간 갱신했습니다.");
    }

    // =========================================================================
    // ================== 아이작 스타일 그리드 배치 & 텔레포트 이동 로직 ==================
    // =========================================================================

    private IEnumerator IsaacStyleGenerationSequence(System.Random random)
    {
        GenerationProgress = 0f;
        using var totalTiming = MeasureGeneration("전체 생성 소요(프레임 대기 포함)", summary: true);
        IsMapGenerationCompleted = false;
        _isGenerating = true;

        const int MaxAttempts = 15;
        int attempts = 0;
        DungeonLayout layout = null;
        int floor = GameManager.Instance != null ? GameManager.Instance.currentFloor : 1;
        var mode = TutorialFlow.IsRunning ? DungeonLayoutMode.Tutorial
            : GameManager.Instance != null && GameManager.Instance.debugEliteArena ? DungeonLayoutMode.EliteArena
            : IsBossFloor() ? DungeonLayoutMode.Boss : DungeonLayoutMode.Normal;
        var planner = new DungeonLayoutPlanner(generationData, prefabData);
        if (WorkshopTrial.RoomTest) layout = WorkshopTrial.Layout();
        else if (WorkshopTrial.Active) layout = WorkshopTrial.PlannedFloor;
        while (layout == null && attempts < MaxAttempts)
        {
            _placementAttempt = ++attempts;
            using var placementTiming = MeasureGeneration($"0단계 방 배치 · 시도 {attempts}", summary: true);
            if (planner.TryPlan(mode, floor, FloorElite?.dedicatedRoomPrefab, random,
                PrefabDiagnosticContext, out layout, out string error)) break;

            Debug.LogWarning($"[Map] seed={LastLayoutSeed} mode={mode} attempt={attempts}: {error}");
            if (mode == DungeonLayoutMode.Tutorial) break;
            yield return null;
        }

        if (layout == null)
        {
            Debug.LogError($"[Map] seed={LastLayoutSeed} mode={mode}: no valid layout after {attempts} attempt(s).");
            _isGenerating = false;
            yield break;
        }

        SetupTilemapLayers();
        ClearExistingMap();
        _currentPhaseIndex = 0;
        if (UnityNote.SceneLoader.FloorIsCovered) yield return null;
        using (MeasureGeneration("0.Instantiate accepted layout"))
        {
            double deadline = Time.realtimeSinceStartupAsDouble + generationData.RoomMergeBudgetSeconds;
            for (int i = 0; i < layout.Rooms.Count; i++)
            {
                yield return CreateRoomAtGrid(layout.Rooms[i]);
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    GenerationProgress = .1f * (i + 1) / layout.Rooms.Count;
                    yield return null;
                    deadline = Time.realtimeSinceStartupAsDouble + generationData.RoomMergeBudgetSeconds;
                }
            }
            if (mode == DungeonLayoutMode.Tutorial)
                _tutorialLastRoom = _allRooms[_allRooms.Count - 1];
        }

        // 배치 간격(spacing)에 맞춰 방들의 물리적 위치 재조정 및 병합 (단계별 분리)
        float spacing = generationData.gridSpacing;
        int mergedRooms = 0;
        GenerationProgress = 0.1f;
        using (MeasureGeneration($"1단계 타일 병합 · {_allRooms.Count}개 방", summary: true))
        {
            double frameDeadline = Time.realtimeSinceStartupAsDouble + generationData.RoomMergeBudgetSeconds;
            foreach (var room in _allRooms)
            {
                using (MeasureGeneration($"1.Merge room={room.name} grid={room.gridPosition}"))
                {
                    room.transform.position = new Vector3(room.gridPosition.x * spacing, room.gridPosition.y * spacing, 0);
                    room.SnapToGrid(generationData.gridUnit);
                    yield return room.MergeTilesForFloor(globalGroundTilemap, globalWallTilemap, globalShadowTilemap,
                        globalUnsteppableTilemap, generationData.RoomMergeBudgetSeconds);
                }
                GenerationProgress = 0.1f + 0.3f * ++mergedRooms / Mathf.Max(1, _allRooms.Count);

                if (mergedRooms < _allRooms.Count && Time.realtimeSinceStartupAsDouble >= frameDeadline)
                {
                    using (MeasureGeneration("1.WAIT frame budget"))
                        yield return null;
                    frameDeadline = Time.realtimeSinceStartupAsDouble + generationData.RoomMergeBudgetSeconds;
                }
            }
        }

        // 문 스폰 및 텔레포트 매핑 연동
        using (MeasureGeneration("2단계 문/텔레포터 연결", summary: true))
        {
            foreach (var room in _allRooms)
                foreach (var anchor in room.anchors) if (anchor != null) anchor.isUsed = false;
            foreach (var link in layout.Doors)
            {
                if (!ApplyGridDoorLink(link))
                {
                    ClearExistingMap();
                    _isGenerating = false;
                    yield break;
                }
                yield return null;
            }
            UpdateAllRoomDepths();
            using (MeasureGeneration("2.WAIT for door activation"))
                yield return null;
        }

        // 최종 가공
        GenerationProgress = 0.6f;
        using (MeasureGeneration("3단계 특수 방/출구", summary: true))
        {
            using (MeasureGeneration("3.AssignSpecialRooms"))
                AssignSpecialRooms();
            using (MeasureGeneration("3.SpawnTutorialExitPortal"))
                SpawnTutorialExitPortal();
            using (MeasureGeneration("3.WAIT for special room activation"))
                yield return null;
        }

        GenerationProgress = 0.7f;
        using (MeasureGeneration("4단계 벽 충돌 갱신", summary: true))
        {
            yield return SetupFinalColliders();
            using (MeasureGeneration("4.WAIT between collision and navigation"))
                yield return null;
        }

        GenerationProgress = 0.8f;
        using (MeasureGeneration("5단계 NavMesh/보상/미니맵 마무리", summary: true))
        {
            yield return BakeNavMesh();
            GenerationProgress = 0.9f;
            yield return null;

            // 일반 전투 방들의 보상 수량을 지정된 개수대로 무작위 분배 및 안배
            using (MeasureGeneration("5.DistributeNormalRoomRewards"))
                DistributeNormalRoomRewards(random);

            RoomInstance spawnRoom = _allRooms.Find(r => r.roomType == RoomType.Spawn);

            // 생성 완료 후 모든 방의 문을 기본 개방 상태로 설정하여 자유로운 이동 및 텔레포터 활성화 보장
            using (MeasureGeneration("5.Open all doors"))
            {
                foreach (var room in _allRooms)
                    room.SetDoorsOpen(true);
            }

            // 스폰 방 위치로 미니맵 카메라 정밀 포커싱 초기화
            if (spawnRoom != null)
            {
                using (MeasureGeneration("5.MiniMap focus"))
                    UpdateMiniMapCameraFocus(spawnRoom);
            }

            using (MeasureGeneration("5.PlacePlayerAtSpawn"))
                PlacePlayerAtSpawn();

            if (_tempObstacle != null) SafeDestroy(_tempObstacle);
            CurrentGridLayout = layout;
            yield return null;
            RoomShadowCasters.Bind(this);
            yield return null;
            GenerationProgress = 0.92f;
            yield return WallContactShadows.BuildForFloor(globalGroundTilemap, globalWallTilemap, globalShadowTilemap);
            GenerationProgress = 0.95f;
            _animActiveRooms.Clear();
            foreach (var room in _allRooms)
            {
                yield return room.FinalizeAnimatedCells(true);
            }
            GenerationProgress = 0.97f;
            if (globalGroundTilemap != null)
                yield return WorldFloorMask.Attach(globalGroundTilemap).PrepareGeneratedFloor();
            _isGenerating = false;
            IsMapGenerationCompleted = true;
            UpdateTileAnimationScope(_currentRoom);
            using (MeasureGeneration("5.OnMapGenerated subscribers"))
                OnMapGenerated?.Invoke();

            // [이동] 디버그 로그는 맵 생성이 '완전히' 끝난 뒤 맨 마지막에 기록한다.
            // (기존엔 문 설치 직후에 호출했는데, 혹시 로깅이 예외를 던지면 타일 리프레시/콜라이더/네브메시/적 스폰이
            //  통째로 스킵되어 맵이 텅 비어 보였다. 이제 위치도 마지막이고 내부도 try-catch라 생성엔 절대 영향 없음.)
            using (MeasureGeneration("5.DumpMapToLog (optional)"))
                DumpMapToLog();
        }

        Debug.Log($"[Map] 생성 완료 · seed={LastLayoutSeed} · {mode} · 방 {_allRooms.Count}개 · 재시도 {attempts - 1}회");
        GenerationProgress = 1f;
    }

    private IEnumerator CreateRoomAtGrid(DungeonLayout.Room planned)
    {
        float spacing = generationData.gridSpacing;
        var position = new Vector3(planned.Cell.x * spacing, planned.Cell.y * spacing);
        var pending = Object.InstantiateAsync(planned.Prefab, transform, position, Quaternion.identity);
        yield return pending;
        var obj = pending.Result[0];
        obj.name = planned.Prefab.name + "(Clone)";
        var room = obj.GetComponent<RoomInstance>() ?? obj.AddComponent<RoomInstance>();
        if (WorkshopTrial.RoomAt(planned.Cell) is WorkshopRoom edited)
            WorkshopGeometry.ApplyToGame(edited,room,WorkshopTrial.Library);
        else ProjectRooms.Apply(planned.Prefab,room,LastLayoutSeed ?? 0,planned.Cell);
        room.Initialize(planned.Type);
        room.gridPosition = planned.Cell;
        room.CleanupPhysics();
        _allRooms.Add(room);
        _reachedRooms.Add(room);
        _masterAdjacency.Add(room, new List<RoomInstance>());
    }

    private RoomInstance _tutorialLastRoom;

    private void SpawnTutorialExitPortal()
    {
        if (_tutorialLastRoom == null) return;

        if (generationData.tutorialExitPortal == null)
        {
            Debug.LogError("[MapGenerator] MapGenerationData 의 Tutorial Exit Portal 이 비어 있다 — " +
                           "튜토리얼 마지막 방에서 마을로 나갈 수가 없다.");
            return;
        }

        Vector3 pos = _tutorialLastRoom.transform.position + (Vector3)_tutorialLastRoom.centerOffset;
        Instantiate(generationData.tutorialExitPortal, pos, Quaternion.identity, _tutorialLastRoom.transform);
        Debug.Log($"<color=purple>[MapGenerator]</color> 튜토리얼 탈출 포탈을 '{_tutorialLastRoom.name}' 한가운데 세웠다.");
    }

    private bool ApplyGridDoorLink(DungeonLayout.Door link)
    {
        var from = _allRooms[link.From];
        var to = _allRooms[link.To];
        if (!RoomConnectivity.TryGetFacingAnchors(from, to, requireUnused: true, out var origin, out var destination))
        {
            Debug.LogError($"[Map] seed={LastLayoutSeed}: planned door {from.name} {from.gridPosition} → {to.name} {to.gridPosition} has no live anchors.");
            return false;
        }

        origin.isUsed = destination.isUsed = true;
        using var pairTiming = MeasureGeneration($"2.Door pair {from.name}{from.gridPosition} <-> {to.name}{to.gridPosition}");
        SpawnIsaacDoorAtAnchor(from, origin);
        SpawnIsaacDoorAtAnchor(to, destination);

        var originDoor = origin.GetComponent<DoorController>() ?? origin.gameObject.AddComponent<DoorController>();
        var destinationDoor = destination.GetComponent<DoorController>() ?? destination.gameObject.AddComponent<DoorController>();
        originDoor.SetupTeleport(destinationDoor, origin.direction, destination.ArrivalPoint(globalWallTilemap));
        destinationDoor.SetupTeleport(originDoor, destination.direction, origin.ArrivalPoint(globalWallTilemap));
        originDoor.SetTriggerEnabled(true);
        destinationDoor.SetTriggerEnabled(true);

        CarveDoorEntrance(origin);
        CarveDoorEntrance(destination);
        _masterAdjacency[from].Add(to);
        _masterAdjacency[to].Add(from);
        return true;
    }

    private GameObject SpawnIsaacDoorAtAnchor(RoomInstance room, RoomAnchor anchor)
    {
        GameObject doorPrefab = null; float rotation = 0f;
        if (anchor.direction == Vector2Int.up) doorPrefab = generationData.doorUp;
        else if (anchor.direction == Vector2Int.down) doorPrefab = generationData.doorDown;
        else if (anchor.direction == Vector2Int.left) doorPrefab = generationData.doorLeft;
        else if (anchor.direction == Vector2Int.right) doorPrefab = generationData.doorRight;
        
        if (doorPrefab == null && generationData.doorUp != null)
        {
            doorPrefab = generationData.doorUp;
            if (anchor.direction == Vector2Int.down) rotation = 180f;
            else if (anchor.direction == Vector2Int.left) rotation = 90f;
            else if (anchor.direction == Vector2Int.right) rotation = -90f;
        }

        if (doorPrefab != null)
        {
            // 문은 뚫린 벽 칸 정중앙에 세운다.
            // 앵커 좌표를 그대로 쓰면 안 된다 — 방 프리팹의 앵커는 칸 중앙에서 최대 0.13 어긋나 있어서
            // 문이 벽 선에서 삐져나온다. CarveDoorEntrance 와 똑같은 셀 계산을 써야 둘이 안 어긋난다.
            // 한 칸 더 밀지 않는 이유: 벽이 정확히 1칸 두께라 바깥은 허공이고, 거기 세우면 문이 떠 보인다.
            // 문 폭 4칸(프리팹 scale.x=4) > 구멍 폭 3칸 이라 이 자리에서도 옆으로 새는 틈은 없다.
            Vector3 doorPos = globalWallTilemap != null
                ? globalWallTilemap.GetCellCenterWorld(globalWallTilemap.WorldToCell(anchor.transform.position))
                : anchor.transform.position;
            doorPos -= (Vector3)(Vector2)anchor.direction * anchor.doorInset;
            GameObject doorObj = Instantiate(doorPrefab, doorPos, Quaternion.Euler(0, 0, rotation), room.transform);
            doorObj.name = $"Door_{anchor.direction}_{room.name}";
            room.doorObjects.Add(doorObj.GetComponent<DoorUpController>());

            // 플레이어와의 트리거 충돌 감지가 물리 엔진 매트릭스 상에서 100% 감지되도록 레이어 지정
            doorObj.gameObject.layer = Layers.Default;
            return doorObj;
        }
        return null;
    }

    private void CarveDoorEntrance(RoomAnchor anchor)
    {
        anchor.OpenPassage(globalGroundTilemap, globalWallTilemap, globalShadowTilemap, globalUnsteppableTilemap,
            generationData.floorTile);
    }

    /// <summary>
    /// 일반 전투 방(Normal Room)의 보상 유형을 SO 설정에 기재된 정확한 개수대로 분배 안배하고 아이콘을 갱신합니다.
    /// </summary>
    private void DistributeNormalRoomRewards(System.Random random = null)
    {
        if (generationData == null) return;

        // 1. 전체 방에서 일반방들만 필터링
        List<RoomInstance> normalRooms = _allRooms.FindAll(r => r.roomType == RoomType.Normal);
        if (normalRooms.Count == 0) return;

        // 2. 무작위 분배를 위해 셔플
        for (int i = 0; i < normalRooms.Count; i++)
        {
            RoomInstance temp = normalRooms[i];
            int randomIndex = random?.Next(i, normalRooms.Count) ?? Random.Range(i, normalRooms.Count);
            normalRooms[i] = normalRooms[randomIndex];
            normalRooms[randomIndex] = temp;
        }

        // 타입별 목표 수량. 카운트를 0으로 두면 그 풀은 일반 방에서 아예 안 나온다
        // (예: 플레이어 스킬을 로비 고정으로 돌리고 싶으면 playerSkillRewardRoomCount = 0).
        var quota = new List<(RoomInstance.NormalRewardType type, int count)>
        {
            (RoomInstance.NormalRewardType.PlayerSkill, generationData.playerSkillRewardRoomCount),
            (RoomInstance.NormalRewardType.MainSummon,  generationData.mainSummonRewardRoomCount),
            (RoomInstance.NormalRewardType.Item,        generationData.itemRewardRoomCount),
        };

        // 전부 0이면 설정 실수로 보고 균등 분배로 폴백한다 (보상이 하나도 없는 맵 방지).
        if (quota.TrueForAll(q => q.count <= 0))
        {
            int each = Mathf.Max(1, normalRooms.Count / quota.Count);
            for (int q = 0; q < quota.Count; q++) quota[q] = (quota[q].type, each);
            Debug.LogWarning("<color=orange>[MapGenerator]</color> 일반 방 보상 카운트가 전부 0입니다. 균등 분배로 폴백합니다.");
        }

        // Count explicit room rewards toward the floor budget before assigning the remaining rooms.
        foreach (var room in normalRooms)
        {
            if (!room.HasAuthoredReward) continue;
            int index = quota.FindIndex(entry => entry.type == room.normalRewardType);
            if (index >= 0) quota[index] = (quota[index].type, Mathf.Max(0, quota[index].count - 1));
        }
        normalRooms.RemoveAll(room => room.HasAuthoredReward);

        int cursor = 0;
        var assigned = new Dictionary<RoomInstance.NormalRewardType, int>();
        foreach (var (type, count) in quota)
        {
            int n = 0;
            for (int i = 0; i < count && cursor < normalRooms.Count; i++, cursor++, n++)
                normalRooms[cursor].SetRewardTypeAndSyncIcon(type);
            assigned[type] = n;
        }

        // 남는 방 처리. 끄면 보상 없이 둔다(= 정확히 카운트만큼만 나온다).
        int leftover = normalRooms.Count - cursor;
        if (generationData.fillRemainingRoomsRandomly)
        {
            for (; cursor < normalRooms.Count; cursor++)
            {
                var type = quota[random?.Next(quota.Count) ?? Random.Range(0, quota.Count)].type;
                normalRooms[cursor].SetRewardTypeAndSyncIcon(type);
            }
        }

        Debug.Log($"<color=green>[MapGenerator]</color> Distributed Normal Room Rewards: " +
                  string.Join(", ", assigned.Select(kv => $"{kv.Key}={kv.Value}")) +
                  $". Leftover={leftover} ({(generationData.fillRemainingRoomsRandomly ? "무작위 배정" : "보상 없음")}), Total Normal Rooms={normalRooms.Count}");
    }
}
