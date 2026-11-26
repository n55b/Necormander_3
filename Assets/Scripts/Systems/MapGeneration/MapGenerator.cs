using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using NavMeshPlus.Components;
using UnityEngine.Tilemaps;

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
    private Dictionary<RoomInstance, List<RoomInstance>> _masterAdjacency = new Dictionary<RoomInstance, List<RoomInstance>>();
    private bool _isGenerating = false;
    private int _placementAttempt;
    private string PrefabDiagnosticContext =>
        $"scene='{gameObject.scene.name}' floor={(GameManager.Instance != null ? GameManager.Instance.currentFloor : 1)} " +
        $"attempt={_placementAttempt} frame={Time.frameCount}";

    // ───────────────── 타일 애니메이션 스코프 제어 ─────────────────
    // 애니메이션 타일(물/횃불 등)은 화면 밖이어도 계속 갱신되므로, 플레이어가 있는 방과
    // 인접 방만 재생시키고 나머지는 정지시킨다.
    // 인접 방까지 켜는 이유: CameraManager 의 vcam 에 룸 컨파이너가 없어서 문 앞·복도에서
    // 옆 방이 화면에 같이 보인다. 현재 방만 켜면 문턱에서 옆 방 물이 '멈춰 있다 갑자기 움직이는'
    // 것이 그대로 보인다.
    private readonly HashSet<RoomInstance> _animActiveRooms = new HashSet<RoomInstance>();
    private readonly HashSet<RoomInstance> _animScopeBuffer = new HashSet<RoomInstance>();

    /// <summary>
    /// 활성 집합 = 기준 방 + GetConnectedRooms 로 얻은 인접 방. 이전 집합과의 차분만 처리한다.
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

    private void Awake()
    {
        
        // 방 진입 시점에 확실히 스코프가 갱신되도록 구독한다.
        // (SetCurrentRoom 은 미니맵 포커스 경로에서만 불려서 단독으로는 신뢰하기 어렵다)
        RoomInstance.OnPlayerEnteredRoom += SetCurrentRoom;
        Instance = this;
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
    private void PickFloorElite(System.Random random)
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
        FloorElite = pool.Count > 0 ? pool[random.Next(pool.Count)] : null;
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
        if (generationData == null)
        {
            Debug.LogError("[MapGenerator] MapGenerationData가 비어 있어 맵을 생성하지 않는다.");
            yield break;
        }
        int seed = WorkshopTrial.Active ? WorkshopTrial.Seed : requestedSeed ?? generationData.FixedLayoutSeed ?? Random.Range(0, int.MaxValue);
        LastLayoutSeed = seed;
        var random = new System.Random(seed);
        PickFloorElite(random);
        yield return StartCoroutine(IsaacStyleGenerationSequence(random));
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
        _allRooms.Clear(); _masterAdjacency.Clear();
        if (globalGroundTilemap != null) globalGroundTilemap.ClearAllTiles();
        if (globalWallTilemap != null) globalWallTilemap.ClearAllTiles();
        if (globalShadowTilemap != null) globalShadowTilemap.ClearAllTiles();
        if (globalUnsteppableTilemap != null) globalUnsteppableTilemap.ClearAllTiles();

        if (globalMiniMapTilemap != null) globalMiniMapTilemap.ClearAllTiles(); // alslaoq
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
            sb.AppendLine($"  - Type: {room.roomType}, Depth: {room.debugDepth}");
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
        //  - 각 엣지의 '사용된' 앵커 쌍을 추정해 서로 마주보는지(facing)와 앵커 간 셀 거리(gap)를 찍는다.
        //  - SUSPECT = 사용된 앵커를 못 찾음 / 서로 안 마주봄 → 팬텀 후보.
        sb.AppendLine("\n==================== CONNECTION AUDIT (엣지 <-> 실제 문 교차검증) ====================");
        sb.AppendLine("A.usedAnchor(Dir) <-> B.usedAnchor(Dir) | facing | anchorGap | VERDICT");
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
                bool suspect = anA == null || anB == null || !facing;
                if (suspect) suspectCount++;

                sb.AppendLine($"{a.name}.{dirAStr} <-> {b.name}.{dirBStr} | facing={facing} | gap={gapStr} | {(suspect ? ">>> SUSPECT" : "ok")}");
            }
        }
        sb.AppendLine($"\nSuspect edges: {suspectCount}  (facing=false / NO-USED-ANCHOR => '미니맵엔 연결·방엔 문 없음' 후보)");

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
        using (MeasureGeneration("5단계 NavMesh/미니맵 마무리", summary: true))
        {
            yield return BakeNavMesh();
            GenerationProgress = 0.9f;
            yield return null;

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
        _allRooms.Add(room);
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
}
