using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Tilemaps;


/// <summary>
/// 일반 전투 방의 이벤트를 담당합니다.
///
/// [26/08/01] 증강 선택 방(RoomType.Augment)도 이 컴포넌트가 굴린다 — 전투 자체는 일반 방과
/// 완전히 같고, 시작 전에 카드를 한 장 고르는 단계와 클리어 시 페널티 해제만 다르다.
/// 방 프리팹도 일반 방 것을 그대로 쓴다(RoomPrefabDataSO.GetEntry 의 폴백).
/// </summary>
public class NormalRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Combat Settings")]
    [SerializeField] private MapGenerationDataSO mapGenerationData;
    [Tooltip("몹 스폰 예고용으로 활용할 차오르는 원형 장판 프리팹을 지정하세요.")]
    [SerializeField] private GameObject spawnVfxPrefab;
    [Tooltip("방 벽으로부터의 스폰 최소 거리 마진(Margin)입니다. 클수록 방 중앙 쪽에 가깝게 몹들이 스폰됩니다. 기본값 3.0f")]
    [SerializeField] private float spawnMargin = 3.0f;

    [Tooltip("스폰 후보를 몇 칸 간격으로 뽑을지. 1이면 모든 바닥 칸, 2면 4분의 1만 훑는다. " +
             "클수록 방 진입 시 스캔이 빨라지고 후보 자리가 성겨진다. 셀 1칸이 월드 0.5라 2면 후보 간격이 1.0이다.")]
    [Range(1, 4)]
    [SerializeField] private int spawnCellStride = 2;


    [Header("Tutorial Placed Enemies")]
    [Tooltip("튜토리얼 방에서만 사용. 이 오브젝트 아래에 몬스터 프리팹을 원하는 위치로 배치하세요. " +
             "방 진입 전에는 숨겨 두었다가 진입 시 활성화·등록하며, 전부 죽으면 기존 방 클리어 처리를 탑니다. " +
             "비워두면 기존 랜덤 웨이브를 사용합니다.")]
    [SerializeField] private Transform tutorialEnemiesRoot;

    [Header("Reward Box Settings")]
    [Tooltip("방 클리어 시 생성할 보상 상자 프리팹. 비워두면 상자 없이 보상이 즉시 지급됩니다.")]
    [SerializeField] private GameObject rewardBoxPrefab;
    [Tooltip("상자가 나올 자리. 방 프리팹 안에 빈 오브젝트를 놓고 연결하세요. 비워두면 방 정중앙.")]
    [SerializeField] private Transform rewardSpawnPoint;

    [Header("Unity Events")]
    public UnityEvent OnCombatStart;
    public UnityEvent OnCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private bool _isBattleActive = false;
    private bool _isSpawnPending = false; // 스폰 코루틴이 도는 동안 웨이브 진행 감지를 멈추는 락
    private RoomInstance _cachedRoom;
    private int _currentWave = 1;
    private bool _usingTutorialPlacedEnemies;

    /// <summary>현재 층의 조절표. 없으면 null — 그 층은 전역 기본값으로 돈다.</summary>
    private MapGenerationDataSO.FloorTuningEntry Tuning =>
        (mapGenerationData != null && GameManager.Instance != null)
            ? mapGenerationData.GetTuningForFloor(GameManager.Instance.currentFloor)
            : null;

    private int WavesThisFloor => (mapGenerationData != null && GameManager.Instance != null)
        ? mapGenerationData.GetWavesForFloor(GameManager.Instance.currentFloor)
        : (mapGenerationData != null ? mapGenerationData.wavesCount : 1);
    private List<Vector3> _spawnedEnemyPositions = new List<Vector3>();
    // 스폰 후보 자리 캐시. 방마다 한 번만 바닥 타일을 훑어 '실제로 설 수 있는 칸'을 모아둔다.
    // 예전엔 roomSize 사각형에서 무작위로 던지고 10번 안에 못 맞추면 방 중앙으로 폴백했는데,
    // roomSize 는 장식용 바깥 벽 띠까지 포함한 Wall 타일맵 bounds(예: 98x91)라 기각률이 높았고
    // 그 폴백 때문에 여러 마리가 정확히 같은 좌표(방 중앙)에 겹쳐 나왔다.
    private readonly List<Vector3> _floorSpawnCells = new List<Vector3>();
    private RoomInstance _floorCellsRoom;
    // GetTilesBlockNonAlloc 용 공유 버퍼. 방마다 8,900칸짜리 배열을 새로 잡으면 진입할 때마다
    // 70KB 가 GC 로 흘러간다. 한 번에 한 방만 스캔하므로 static 하나로 돌려쓴다(필요하면 커진다).
    private static TileBase[] _tileBlockBuffer;

    // 웨이브마다 new 로 잡던 임시 리스트들. Clear() 는 용량을 남기므로 재사용하면 두 번째 웨이브부터
    // 할당이 0 이 된다. 스폰 루틴은 한 번에 하나만 도는 게 보장되므로(_isSpawnPending) 공유해도 안전하다.
    private readonly List<Vector3> _pendingSpawnPoints = new List<Vector3>();
    private readonly List<EnemyCount> _pendingEnemyCounts = new List<EnemyCount>();
    private readonly List<EnemyCount> _toSpawn = new List<EnemyCount>();
    private readonly List<GameObject> _superArmorPool = new List<GameObject>();


    private bool _augmentChosen = false; // 증강 방에서 카드를 이미 골랐는지(방 하나당 한 번)

    private void Start()
    {
        if (mapGenerationData == null && GameManager.Instance != null)
        {
            mapGenerationData = GameManager.Instance.CurrentStageMapData;
        }

        if (TutorialFlow.IsRunning && tutorialEnemiesRoot != null)
            tutorialEnemiesRoot.gameObject.SetActive(false);
    }

    private void Update()
    {
        // 지연 소환 중에 적 수가 0명인 것을 감지해 즉시 다음 웨이브로 넘어가는 조기 오작동 차단
        if (!_isBattleActive || _isSpawnPending) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int beforeCount = _activeEnemies.Count;
#endif
        _activeEnemies.RemoveAll(item => item == null);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ConvertAll + string.Join 은 호출마다 리스트와 문자열을 새로 만든다.
        // 적이 죽을 때마다 도는 진단용이라 개발 빌드에서만 남긴다.
        if (beforeCount != _activeEnemies.Count)
        {
            Debug.Log($"<color=cyan>[NormalRoomEvent]</color> Enemy removed. Count: {beforeCount} -> {_activeEnemies.Count}. " +
                      $"Remaining: {string.Join(", ", _activeEnemies.ConvertAll(e => e != null ? e.name : "null"))}");
        }
#endif

        if (_activeEnemies.Count == 0)
        {
            if (!_usingTutorialPlacedEnemies && mapGenerationData != null && _currentWave < WavesThisFloor)
            {
                _currentWave++;
                SpawnWaves(_cachedRoom);
            }
            else
            {
                _isBattleActive = false;
                _cachedRoom.MarkCleared();
                Debug.Log("<color=green>[NormalRoomEvent]</color> All enemies cleared. Marking room cleared.");
            }
        }
    }

    

    public void OnPlayerEnter(RoomInstance room)
    {
        if (_isBattleActive) return;

        // [증강 방] 전투를 시작하기 전에 페널티 카드부터 고르게 한다.
        // 고르는 동안 시간이 멈추고(AugmentSelectionUI), 고른 뒤 콜백에서 이 함수로 다시 들어온다.
        if (room.roomType == RoomType.Augment && !_augmentChosen)
        {
            _cachedRoom = room;
            if (TryOpenAugmentSelection(room)) return;
            _augmentChosen = true; // 테이블/UI 가 없으면 그냥 일반 전투로 진행
        }

        _cachedRoom = room;
        _isBattleActive = true;
        _currentWave = 1;

        if (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen) HandSlotSelectionUI.Instance.Hide();
        // 전투 시작 시 들고 있던 투척물을 떨군다.

        _usingTutorialPlacedEnemies = TutorialFlow.IsRunning && tutorialEnemiesRoot != null;
        if (_usingTutorialPlacedEnemies)
        {
            _isSpawnPending = true;
            StartCoroutine(ActivateTutorialEnemies());
        }
        else
        {
            _isSpawnPending = true; // 스폰 진행 예정 상태 설정 (스폰이 끝날 때까지 대기)
            StartCoroutine(DelayedSpawnWaves(room));
        }

        // 플레이어 상태 업데이트
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Battle);

        OnCombatStart?.Invoke();
        Debug.Log($"<color=white>[NormalRoom]</color> Battle Started in {room.gameObject.name}");
    }

    private IEnumerator ActivateTutorialEnemies()
    {
        BaseEntity[] enemies = tutorialEnemiesRoot.GetComponentsInChildren<BaseEntity>(true);
        if (enemies.Length > 0)
        {
            yield return new WaitForSeconds(0.5f); // 카메라가 방에 안착한 뒤 예고를 보여준다

            const float telegraphDuration = 1f;
            var telegraphs = new List<GameObject>();
            foreach (var enemy in enemies)
            {
                if (enemy == null || spawnVfxPrefab == null) continue;
                GameObject vfx = Instantiate(spawnVfxPrefab, enemy.transform.position, Quaternion.identity);
                BaseHitBox hitbox = vfx.GetComponent<BaseHitBox>();
                if (hitbox != null)
                {
                    var dummy = new DamageInfo(0f, DamageType.Physical, gameObject, 0f);
                    hitbox.Init(dummy, 0, 0.05f, telegraphDuration, false);
                    vfx.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
                }
                telegraphs.Add(vfx);
            }

            yield return new WaitForSeconds(telegraphDuration);
            foreach (var vfx in telegraphs)
                if (vfx != null) Destroy(vfx);
        }

        tutorialEnemiesRoot.gameObject.SetActive(true);
        _activeEnemies.Clear();

        foreach (var enemy in enemies)
            if (enemy != null) RegisterActiveEnemy(enemy.gameObject);

        _isSpawnPending = false;
        Debug.Log($"<color=cyan>[NormalRoomEvent]</color> 튜토리얼 배치 몬스터 {_activeEnemies.Count}마리를 등록했습니다.");
    }

    private IEnumerator DelayedSpawnWaves(RoomInstance room)
    {
        yield return new WaitForSeconds(0.5f); // 카메라 워프가 안착하는 약 0.5초 동안만 대기 후 스폰
        SpawnWaves(room);
    }

    /// <summary>
    /// 증강 선택 창을 띄운다. 띄웠으면 true — 호출부는 여기서 리턴하고, 선택이 끝나면
    /// 콜백이 OnPlayerEnter 를 다시 불러 전투가 시작된다.
    /// 테이블이나 UI 가 없으면 false 를 돌려 그냥 일반 전투로 흘려보낸다(방이 잠기면 안 되므로).
    /// </summary>
    private bool TryOpenAugmentSelection(RoomInstance room)
    {
        var table = GameManager.Instance != null && GameManager.Instance.dataManager != null
            ? GameManager.Instance.dataManager.AUGMENT_TABLE : null;

        if (table == null || AugmentSelectionUI.Instance == null)
        {
            Debug.LogWarning("[NormalRoomEvent] 증강 테이블 또는 AugmentSelectionUI 가 없어 카드를 못 띄운다. 일반 전투로 진행한다.");
            return false;
        }

        var offers = table.RollOffers();
        if (offers.Count == 0)
        {
            Debug.LogWarning("[NormalRoomEvent] 증강 테이블에 페널티가 하나도 없다. 일반 전투로 진행한다.");
            return false;
        }

        AugmentSelectionUI.Instance.Show(offers, table.RollNoRiskOffer(), picked =>
        {
            _augmentChosen = true;
            ActiveAugment.Apply(picked);
            OnPlayerEnter(room); // 이제 진짜 전투 시작
        });
        return true;
    }

    public void OnRoomCleared(RoomInstance room)
    {
        // [증강 방] 페널티는 이 방 전투까지만이다. 보상은 상자를 열 때 지급되므로 여기서 안 건드린다.
        if (room.roomType == RoomType.Augment) ActiveAugment.ClearPenalty();

        // 이 방은 더 이상 전투하지 않는다. 후보 캐시(방당 수천 개 Vector3 = 수십 KB)를 놓아준다.
        // 다시 필요해지면 BuildFloorSpawnCells 가 알아서 재구축한다.
        _floorSpawnCells.Clear();
        _floorSpawnCells.TrimExcess();
        _floorCellsRoom = null;

        // 인스펙터에 할당된 상자를 방 정중앙에 생성
        SpawnRoomRewardBox(room);

        // 플레이어 상태 업데이트
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Idle);

        OnCombatClear?.Invoke();
        Debug.Log($"<color=green>[NormalRoom]</color> Cleared!");
    }

    private void SpawnRoomRewardBox(RoomInstance room)
    {
        if (rewardBoxPrefab == null)
        {
            // 상자가 없으면 예외 복구 조치로 즉시 UI 개방
            if (RewardManager.Instance != null)
                RewardManager.Instance.RequestClearReward(room.roomType, room.normalRewardType);
            return;
        }

        // 자리를 안 잡아뒀으면 방 정중앙
        Vector3 spawnPos = rewardSpawnPoint != null
            ? rewardSpawnPoint.position
            : room.transform.position + (Vector3)room.centerOffset;
        GameObject boxObj = Instantiate(rewardBoxPrefab, spawnPos, Quaternion.identity);
        boxObj.name = $"RoomRewardBox_{room.roomType}_{room.name}";

        RoomRewardBox rewardBox = boxObj.GetComponent<RoomRewardBox>();
        if (rewardBox != null)
        {
            rewardBox.Initialize(room.roomType, room.normalRewardType);
            Debug.Log($"<color=magenta>[NormalRoomEvent]</color> Spawned RoomRewardBox at {spawnPos} (Type: {room.normalRewardType})");
        }
        else
        {
            Debug.LogWarning("[NormalRoomEvent] Spawned object lacks RoomRewardBox script. Triggering reward instantly.");
            if (RewardManager.Instance != null)
                RewardManager.Instance.RequestClearReward(room.roomType, room.normalRewardType);
        }
    }

    private void SpawnWaves(RoomInstance room)
    {
        if (mapGenerationData == null)
        {
            Debug.LogWarning("[NormalRoom] MapGenerationData is not assigned!");
            return;
        }

        Debug.Log($"[NormalRoom] SpawnWaves started. Wave: {_currentWave}/{WavesThisFloor}");

        _spawnedEnemyPositions.Clear();

        var clusters = GameManager.Instance.dataManager.ENEMY_CLUSTERS;
        if (clusters == null || clusters.Count == 0)
        {
            Debug.LogWarning("[NormalRoom] No enemy clusters found in DataManager.");
            return;
        }

        // 층 전용 군집 풀이 저작돼 있으면 거기서만 뽑는다. 비어 있으면 레지스트리 전체.
        // (풀에 null 이 섞여 있어도 걸러낸다 — 인스펙터에서 빈 슬롯을 남기기 쉬워서.)
        var tuning = Tuning;
        if (tuning != null && tuning.clusterPool != null && tuning.clusterPool.Count > 0)
        {
            var pool = tuning.clusterPool.FindAll(c => c != null);
            if (pool.Count > 0) clusters = pool;
        }

        // 웨이브당 1개의 무작위 군집 선택
        var selectedCluster = clusters[Random.Range(0, clusters.Count)];
        SpawnCluster(selectedCluster, room);
    }

    private void SpawnCluster(EnemyClusterSO cluster, RoomInstance room)
    {
        if (cluster == null)
        {
            Debug.LogWarning("[NormalRoom] SpawnCluster: cluster is null!");
            return;
        }
        if (cluster.enemies == null || cluster.enemies.Count == 0)
        {
            Debug.LogWarning($"[NormalRoom] SpawnCluster: cluster '{cluster.name}' has no enemies defined!");
            return;
        }

        _isSpawnPending = true; // 스폰 코루틴이 완료될 때까지 생존 감지 루프 중단 (안전장치)
        StartCoroutine(SpawnClusterRoutine(cluster, room));
    }

    private IEnumerator SpawnClusterRoutine(EnemyClusterSO cluster, RoomInstance room)
    {
        // 매 웨이브 new 하지 않고 재사용한다. 이 세 리스트는 이 루틴이 도는 동안에만 유효하다.
        _pendingSpawnPoints.Clear();
        _pendingEnemyCounts.Clear();
        _toSpawn.Clear();

        // 이번 웨이브에 실제로 뽑을 적 목록. 군집을 한 줄로 펼친 다음,
        // 증강 페널티('웨이브당 적 수 +N')가 걸려 있으면 그만큼 뒤에 더 붙인다.
        foreach (var enemyCount in cluster.enemies)
        {
            if (enemyCount.enemyData == null) continue;
            for (int i = 0; i < enemyCount.count; i++) _toSpawn.Add(enemyCount);
        }

        // 층 배율. 군집 에셋은 그대로 두고 여기서만 양을 깎는다(늘린다).
        // 어느 놈이 빠지는지는 무작위 — 조성 비율은 대체로 유지되고, 최소 1마리는 남긴다.
        var floorTuning = Tuning;
        if (floorTuning != null && !Mathf.Approximately(floorTuning.enemyCountScale, 1f) && _toSpawn.Count > 0)
        {
            int target = Mathf.Max(1, Mathf.RoundToInt(_toSpawn.Count * floorTuning.enemyCountScale));
            while (_toSpawn.Count > target) _toSpawn.RemoveAt(Random.Range(0, _toSpawn.Count));
            int seed = _toSpawn.Count;
            while (_toSpawn.Count < target && seed > 0) _toSpawn.Add(_toSpawn[Random.Range(0, seed)]);
        }

        // 증강 페널티는 플레이어가 고른 것이라 층 배율을 타지 않는다 — 배율 뒤에 붙인다.
        int extra = ActiveAugment.ExtraEnemiesForWave(_currentWave);
        int baseCount = _toSpawn.Count;
        for (int i = 0; i < extra && baseCount > 0; i++)
            _toSpawn.Add(_toSpawn[Random.Range(0, baseCount)]); // 원래 군집에 있던 적 중 하나를 복제

        int skipped = 0;
        for (int i = 0; i < _toSpawn.Count; i++)
        {
            // 자리를 못 찾으면 건너뛴다. 예전처럼 방 중앙으로 몰아넣지 않는다.
            if (!TryFindSpawnPoint(room, _pendingSpawnPoints, out Vector3 point))
            {
                skipped++;
                continue;
            }
            _pendingSpawnPoints.Add(point);
            _pendingEnemyCounts.Add(_toSpawn[i]);
        }

        if (skipped > 0)
            Debug.LogWarning($"<color=orange>[NormalRoom]</color> '{room.name}' 에서 스폰 자리를 못 찾아 {skipped}마리를 건너뛰었다. " +
                             $"spawnMargin({spawnMargin}) 또는 문 앞 금지 구역이 방에 비해 큰지 확인할 것.");

        if (_pendingSpawnPoints.Count == 0)
        {
            // 한 마리도 못 놨으면 방이 영영 안 열린다. 감지 락을 풀어 클리어 처리로 흘려보낸다.
            Debug.LogError($"<color=red>[NormalRoom]</color> '{room.name}' 에 적을 한 마리도 배치하지 못했다.");
            _isSpawnPending = false;
            yield break;
        }

        float spawnDelay = 1.0f; // 장판이 가득 차오르는 선딜 시간

        // 1. 모든 몹들의 스폰 위치에 예고 장판(VFX) 동시 소환
        for (int i = 0; i < _pendingSpawnPoints.Count; i++)
        {
            StartCoroutine(DelayedSpawnEnemyWithVFX(_pendingEnemyCounts[i], _pendingSpawnPoints[i], spawnDelay));
        }

        // 2. 장판 차오르는 시간만큼 대기 (충돌 및 생성 지연 안정성을 위해 0.1초 버퍼 추가)
        yield return new WaitForSeconds(spawnDelay + 0.1f);

        // 3. 무작위 적군 2명에게 슈퍼아머 부여
        ApplySuperArmorToRandomEnemies(2);

        // 4. 스폰 완료되었으므로 감지 락 해제
        _isSpawnPending = false;

        Debug.Log($"<color=cyan>[NormalRoomEvent]</color> Finished spawning cluster '{cluster.name}'. Active count: {_activeEnemies.Count}");
    }

    /// <summary>
    /// 방의 바닥 타일 중 '몹이 설 수 있는 칸'을 한 번만 훑어 캐시한다.
    /// GetCellCenterWorld 를 쓰므로 방 스케일/셀 크기와 무관하게 좌표가 맞는다.
    ///
    /// 비용 주의: 방이 100x90 칸쯤 되므로 칸당 검사 수가 그대로 진입 프레임 부하가 된다.
    ///  · 바닥 타일 존재 여부는 GetTilesBlock 로 네이티브 호출 '한 번'에 받아온다(칸마다 HasTile 금지).
    ///  · spawnCellStride 로 후보를 솎는다. 몹 간 최소 거리가 2 월드(=4칸)라 1~2칸 해상도면 충분하다.
    ///  · 필터는 싼 것부터: 타일 유무 → IsFloorAt → 문 앞 → 사방 여유(가장 비쌈, 앞에서 대부분 걸러진 뒤에만 돈다).
    /// </summary>
    private void BuildFloorSpawnCells(RoomInstance room)
    {
        if (room == null) return;
        if (_floorCellsRoom == room && _floorSpawnCells.Count > 0) return;

        _floorCellsRoom = room;
        _floorSpawnCells.Clear();

        var tm = room.groundTilemap;
        if (tm == null) return; // 타일맵을 못 찾은 방은 사각형 폴백으로 흘려보낸다

        BoundsInt bounds = tm.cellBounds;
        if (bounds.size.x <= 0 || bounds.size.y <= 0) return;
        if (bounds.size.z != 1) return; // 3D 타일맵은 아래 인덱싱 전제가 깨진다 — 폴백으로 넘긴다

        int cellCount = bounds.size.x * bounds.size.y;
        if (_tileBlockBuffer == null || _tileBlockBuffer.Length < cellCount)
            _tileBlockBuffer = new TileBase[cellCount];
        tm.GetTilesBlockNonAlloc(bounds, _tileBlockBuffer);

        int stride = Mathf.Max(1, spawnCellStride);
        int width = bounds.size.x;

        for (int y = 0; y < bounds.size.y; y += stride)
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x += stride)
            {
                if (_tileBlockBuffer[rowStart + x] == null) continue;

                Vector3 world = tm.GetCellCenterWorld(new Vector3Int(bounds.xMin + x, bounds.yMin + y, bounds.zMin));
                if (!room.IsFloorAt(world)) continue;     // 벽/물 겹친 칸 제외
                if (IsDoorKeepOut(room, world)) continue;  // 앵커 몇 개 도는 산술, 아래보다 싸다
                if (!HasWallClearance(room, world)) continue;

                _floorSpawnCells.Add(world);
            }
        }

        if (_floorSpawnCells.Count == 0)
            Debug.LogWarning($"<color=orange>[NormalRoom]</color> '{room.name}' 에서 유효한 스폰 칸을 못 찾았다. 사각형 무작위 방식으로 폴백한다.");
    }

    /// <summary>spawnMargin 만큼 사방이 바닥인가. 벽에 붙어서 스폰되는 것을 막는다.</summary>
    private bool HasWallClearance(RoomInstance room, Vector3 pos)
    {
        if (spawnMargin <= 0f) return true;
        return room.IsFloorAt(pos + Vector3.right * spawnMargin)
            && room.IsFloorAt(pos + Vector3.left  * spawnMargin)
            && room.IsFloorAt(pos + Vector3.up    * spawnMargin)
            && room.IsFloorAt(pos + Vector3.down  * spawnMargin);
    }

    private bool IsDoorKeepOut(RoomInstance room, Vector3 pos)
    {
        if (mapGenerationData == null) return false;
        return room.IsInDoorKeepOut(pos,
            mapGenerationData.doorKeepOutWidth,
            mapGenerationData.doorKeepOutInward,
            mapGenerationData.doorKeepOutOutward);
    }

    /// <summary>이미 잡아둔 자리들과의 최단 거리(제곱). 아무것도 없으면 MaxValue.
    /// 제곱으로 비교해 후보마다 도는 sqrt 를 없앤다 — 순서 비교라 결과는 같다.</summary>
    private float NearestSpawnSqrDistance(Vector3 pos, List<Vector3> pending)
    {
        float min = float.MaxValue;
        for (int i = 0; i < _spawnedEnemyPositions.Count; i++)
        {
            float sqr = (pos - _spawnedEnemyPositions[i]).sqrMagnitude;
            if (sqr < min) min = sqr;
        }
        for (int i = 0; i < pending.Count; i++)
        {
            float sqr = (pos - pending[i]).sqrMagnitude;
            if (sqr < min) min = sqr;
        }
        return min;
    }

    /// <summary>
    /// 이미 잡아둔 자리들과 최대한 떨어진, NavMesh 위의 스폰 좌표를 찾는다.
    /// 못 찾으면 false — 예전처럼 방 중앙으로 폴백하지 않는다(그게 한 자리 중첩의 원인이었다).
    /// </summary>
    private bool TryFindSpawnPoint(RoomInstance room, List<Vector3> pending, out Vector3 result)
    {
        result = Vector3.zero;
        if (room == null) return false;

        BuildFloorSpawnCells(room);

        int attempts = mapGenerationData != null ? mapGenerationData.maxSpawnAttempts : 10;
        attempts = Mathf.Max(attempts, 40); // 후보 하나가 싸므로 넉넉히 던진다
        float minDistance = mapGenerationData != null ? mapGenerationData.minDistanceBetweenEnemies : 2f;
        float minSqr = minDistance * minDistance;
        const float MaxNavDriftSqr = 1.2f * 1.2f;

        // 캐시가 비었을 때만 쓰는 예전 방식(사각형 무작위). 좌표 규약은 그대로 유지한다.
        bool useCells = _floorSpawnCells.Count > 0;
        float rangeX = 0f, rangeY = 0f;
        Vector3 roomCenter = Vector3.zero;
        if (!useCells)
        {
            rangeX = Mathf.Max(1f, (room.roomSize.x / 2f) - spawnMargin);
            rangeY = Mathf.Max(1f, (room.roomSize.y / 2f) - spawnMargin);
            roomCenter = room.transform.position + (Vector3)room.centerOffset;
        }

        Vector3 bestPos = Vector3.zero;
        float bestSqr = -1f;
        bool hasAny = false;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector3 candidatePos;

            if (useCells)
            {
                // 후보는 이미 바닥/여유/문앞 검사를 통과한 자리다 — 여기선 NavMesh 만 본다.
                candidatePos = _floorSpawnCells[Random.Range(0, _floorSpawnCells.Count)];
            }
            else
            {
                candidatePos = roomCenter + new Vector3(Random.Range(-rangeX, rangeX), Random.Range(-rangeY, rangeY), 0f);
                if (!room.IsFloorAt(candidatePos)) continue;
                if (IsDoorKeepOut(room, candidatePos)) continue;
            }

            // 후보 좌표가 실제로 구워진 NavMesh(이동 가능 구역) 위인지 검증한다.
            if (!NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
            // 벽 너머나 너무 먼 곳으로 NavMesh 가 당겨져 왜곡 스폰되는 것을 차단
            if ((candidatePos - hit.position).sqrMagnitude > MaxNavDriftSqr) continue;
            candidatePos = hit.position;

            float sqr = NearestSpawnSqrDistance(candidatePos, pending);
            if (sqr >= minSqr)
            {
                result = candidatePos;
                return true;
            }

            if (sqr > bestSqr)
            {
                bestSqr = sqr;
                bestPos = candidatePos;
                hasAny = true;
            }
        }

        if (!hasAny) return false; // 유효 후보가 하나도 없었다 — 이 마리는 스폰하지 않는다

        result = bestPos;
        return true;
    }

    private IEnumerator DelayedSpawnEnemyWithVFX(EnemyCount enemyCount, Vector3 spawnPos, float duration)
    {
        GameObject vfxObj = null;
        GameObject vfxPrefabToUse = spawnVfxPrefab;

#if UNITY_EDITOR
        if (vfxPrefabToUse == null)
        {
            // 인스펙터 할당 누락 시 내장 원형 예고 장판 프리팹을 자동으로 긁어와 Fallback 매핑
            vfxPrefabToUse = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Skill Visual Effects/TelegraphHitbox Prefab.prefab");
        }
#endif

        if (vfxPrefabToUse != null)
        {
            vfxObj = Instantiate(vfxPrefabToUse, spawnPos, Quaternion.identity);
            BaseHitBox hitbox = vfxObj.GetComponent<BaseHitBox>();
            if (hitbox != null)
            {
                // 데미지 0짜리 가짜 판정 전달, 0.05초 유지, duration(1초) 선딜레이 대기
                DamageInfo dummyInfo = new DamageInfo(0f, DamageType.Physical, this.gameObject, 0f);
                hitbox.Init(dummyInfo, 0, 0.05f, duration, false);

                // 프리팹 스케일 조절 (기본 원 크기가 1.0이므로 2.5f정도로 키움)
                vfxObj.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
            }
        }

        yield return new WaitForSeconds(duration);

        if (vfxObj != null) Destroy(vfxObj);

        // spawnPos 는 TryFindSpawnPoint 가 이미 NavMesh 위로 스냅해 검증한 좌표다.
        // 여기서 한 번 더 SamplePosition 을 태우면 서로 다른 두 좌표가 같은 경계점으로
        // 당겨져 최종 위치가 겹칠 수 있어서, 예고 장판이 뜬 자리에 그대로 소환한다.
        if (_cachedRoom != null && !_cachedRoom.isCleared)
        {
            GameObject enemy = GameManager.Instance.dataManager.CreateUnit(enemyCount.enemyData, spawnPos);
            if (enemy != null)
            {
                _activeEnemies.Add(enemy);
                _spawnedEnemyPositions.Add(spawnPos);
            }
        }
    }

    private void ApplySuperArmorToRandomEnemies(int count)
    {
        if (_activeEnemies.Count == 0) return;

        _superArmorPool.Clear(); // 웨이브마다 new 하지 않고 돌려쓴다
        for (int i = 0; i < _activeEnemies.Count; i++)
        {
            if (_activeEnemies[i] != null) _superArmorPool.Add(_activeEnemies[i]);
        }

        int actualCount = Mathf.Min(count, _superArmorPool.Count);
        for (int i = 0; i < actualCount; i++)
        {
            int randIndex = Random.Range(0, _superArmorPool.Count);
            GameObject enemyObj = _superArmorPool[randIndex];
            _superArmorPool.RemoveAt(randIndex);

            var status = enemyObj.GetComponentInChildren<CharacterStatus>();
            if (status == null) status = enemyObj.GetComponentInParent<CharacterStatus>();
            if (status != null)
            {
                status.ApplySuperArmor(100f);
            }
        }
    }

    public void RegisterActiveEnemy(GameObject enemy)
    {
        if (enemy != null && !_activeEnemies.Contains(enemy))
        {
            _activeEnemies.Add(enemy);
            Debug.Log($"<color=cyan>[NormalRoomEvent]</color> Added split enemy: {enemy.name}. Current Active Count: {_activeEnemies.Count}");
        }
    }

#if UNITY_EDITOR
    /// <summary>문 앞 스폰 금지 구역을 씬 뷰에 그린다. 숫자만 보고 맞출 수 있는 값이 아니라서 있어야 한다.
    /// 런타임 anchors 목록은 Initialize 전엔 비어 있으므로 자식에서 직접 긁는다.</summary>
    private void OnDrawGizmosSelected()
    {
        // 방 프리팹의 이 슬롯은 비어 있는 게 정상이다 — 런타임엔 GameManager 의 현재 층 데이터를
        // 받아와 채운다(위 Awake 참고). 에디터엔 GameManager 가 없으니 여기서만 프로젝트에서 직접 긁는다.
        // (필드에 대입하면 프리팹이 더럽혀지니 지역 변수로만 쓴다.)
        MapGenerationDataSO data = mapGenerationData;
        if (data == null)
        {
            string[] found = UnityEditor.AssetDatabase.FindAssets("t:MapGenerationDataSO");
            if (found.Length == 0) return;
            data = UnityEditor.AssetDatabase.LoadAssetAtPath<MapGenerationDataSO>(
                       UnityEditor.AssetDatabase.GUIDToAssetPath(found[0]));
        }
        if (data == null) return;

        float width   = data.doorKeepOutWidth;
        float inward  = data.doorKeepOutInward;
        float outward = data.doorKeepOutOutward;
        if (width <= 0f || (inward <= 0f && outward <= 0f)) return;

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.85f);
        foreach (var anchor in GetComponentsInChildren<RoomAnchor>())
        {
            if (anchor == null) continue;
            bool vertical = anchor.direction.y != 0;
            if ((vertical ? anchor.direction.y : anchor.direction.x) == 0) continue;

            // 안/밖 깊이가 다르니 상자 중심이 앵커에서 그만큼 밀린다.
            Vector3 dir = new Vector3(anchor.direction.x, anchor.direction.y, 0f);
            Vector3 center = anchor.transform.position + dir * ((outward - inward) * 0.5f);
            float depth = inward + outward;
            Vector3 box = vertical ? new Vector3(width, depth, 0.1f)
                                   : new Vector3(depth, width, 0.1f);
            Gizmos.DrawWireCube(center, box);
        }
    }
#endif
}
