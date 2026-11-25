using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Tilemaps;


/// <summary>
/// 일반 전투 방의 이벤트를 담당합니다.
///
/// 증강 선택 방(RoomType.Augment)도 이 컴포넌트가 굴린다 — 전투 자체는 일반 방과
/// 완전히 같고, 시작 전에 카드를 한 장 고르는 단계와 클리어 시 페널티 해제만 다르다.
/// 방 프리팹도 일반 방 것을 그대로 쓴다(RoomPrefabDataSO.GetEntry 의 폴백).
/// </summary>
public class NormalRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("기획 조절 · 동작과 타이밍")]
    [SerializeField, Min(0f), Tooltip("방에 들어간 뒤 카메라가 자리 잡도록 기다리는 시간입니다. 0이면 바로 소환 절차를 시작합니다.")]
    private float spawnStartDelay = 0.5f;
    [SerializeField, Min(0.01f), Tooltip("일반 웨이브와 튜토리얼 배치 몬스터가 등장하기 전 바닥 예고가 차오르는 시간입니다.")]
    private float spawnTelegraphDuration = 1f;
    [SerializeField, Min(0.01f), Tooltip("몬스터 등장 위치 표시의 가로·세로 크기입니다. 몬스터 크기나 피해 판정은 바꾸지 않습니다.")]
    private float spawnTelegraphScale = 2.5f;
    [InspectorName("한 프레임 등장 준비 수")]
    [SerializeField, Min(1), Tooltip("한 프레임에 등장 준비를 시작할 몬스터 수입니다. 예고와 실제 등장은 함께 나누어지며 예고 시간은 유지됩니다.")]
    private int spawnGroupPerFrame = 4;
    [SerializeField, Min(0), Tooltip("일반 랜덤 웨이브에서 슈퍼아머를 주는 몬스터 수입니다. 0이면 주지 않습니다. 튜토리얼 직접 배치에는 적용하지 않습니다.")]
    private int superArmorEnemyCount = 2;
    [SerializeField, Min(1f), Tooltip("무작위로 선정된 일반 몬스터에게 주는 슈퍼아머 양입니다. 엘리트·보스의 영구 슈퍼아머 설정과 별개입니다.")]
    private float spawnSuperArmorAmount = 100f;

    [Header("전투 설정")]
    [SerializeField] private MapGenerationDataSO mapGenerationData;
    [SerializeField, InspectorName("모든 웨이브를 한 번에 소환")]
    [Tooltip("이 방만 층 설정의 모든 웨이브를 첫 등장에 합칩니다. 웨이브별 군집 추첨·적 수 배율·증강 추가 몬스터를 그대로 합산합니다. 튜토리얼 직접 배치에는 영향이 없습니다.")]
    private bool combineAllWaves;
    [Tooltip("방 벽으로부터의 스폰 최소 거리 마진(Margin)입니다. 클수록 방 중앙 쪽에 가깝게 몹들이 스폰됩니다. 기본값 3.0f")]
    [SerializeField] private float spawnMargin = 3.0f;

    [Tooltip("스폰 후보를 몇 칸 간격으로 뽑을지. 1이면 모든 바닥 칸, 2면 4분의 1만 훑는다. " +
             "클수록 방 진입 시 스캔이 빨라지고 후보 자리가 성겨진다. 셀 1칸이 월드 0.5라 2면 후보 간격이 1.0이다.")]
    [Range(1, 4)]
    [SerializeField] private int spawnCellStride = 2;


    [Header("튜토리얼 배치 몬스터")]
    [Tooltip("튜토리얼 방에서만 사용. 이 오브젝트 아래에 몬스터 프리팹을 원하는 위치로 배치하세요. " +
             "방 진입 전에는 숨겨 두었다가 진입 시 활성화·등록하며, 전부 죽으면 기존 방 클리어 처리를 탑니다. " +
             "비워두면 기존 랜덤 웨이브를 사용합니다.")]
    [SerializeField] private Transform tutorialEnemiesRoot;

    [Header("보상 상자 설정")]
    [Tooltip("방 클리어 시 생성할 보상 상자 프리팹. 비워두면 상자 없이 보상이 즉시 지급됩니다.")]
    [SerializeField] private GameObject rewardBoxPrefab;
    [Tooltip("상자가 나올 자리. 방 프리팹 안에 빈 오브젝트를 놓고 연결하세요. 비워두면 방 정중앙.")]
    [SerializeField] private Transform rewardSpawnPoint;

    [Header("이벤트 연결")]
    public UnityEvent OnCombatStart;
    public UnityEvent OnCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private bool _isBattleActive = false;
    private bool _isSpawnPending = false; // 스폰 코루틴이 도는 동안 웨이브 진행 감지를 멈추는 락
    private RoomInstance _cachedRoom;
    private int _currentWave = 1;
    private bool _usingTutorialPlacedEnemies;
#if NECRO_RENDER_BENCH
    internal int SpawnedEnemyCount => _activeEnemies.Count;
    internal int SpawnBudget => Mathf.Max(1, spawnGroupPerFrame);
#endif
#if LEVEL_WORKSHOP_QA
    internal GameObject[] ActiveEnemiesForCheck => _activeEnemies.ToArray();
    internal int CurrentWaveForCheck => _currentWave;
#endif

    /// <summary>현재 층의 조절표. 없으면 null — 그 층은 전역 기본값으로 돈다.</summary>
    private MapGenerationDataSO.FloorTuningEntry Tuning =>
        (mapGenerationData != null && GameManager.Instance != null)
            ? mapGenerationData.GetTuningForFloor(GameManager.Instance.currentFloor)
            : null;

    [SerializeField, HideInInspector] private EnemyClusterSO[] authoredWaves = System.Array.Empty<EnemyClusterSO>();
    public IReadOnlyList<EnemyClusterSO> AuthoredWaves => authoredWaves;
    internal void ConfigureWaves(EnemyClusterSO[] waves) => authoredWaves = waves;
    private bool HasAuthoredWaves => authoredWaves.Length>0;
    private int WavesThisFloor => HasAuthoredWaves ? authoredWaves.Length : (mapGenerationData != null && GameManager.Instance != null)
        ? mapGenerationData.GetWavesForFloor(GameManager.Instance.currentFloor)
        : (mapGenerationData != null ? mapGenerationData.wavesCount : 1);
    private List<Vector3> _spawnedEnemyPositions = new List<Vector3>();
    // 스폰 후보 자리 캐시. 방마다 한 번만 바닥 타일을 훑어 '실제로 설 수 있는 칸'을 모아둔다.
    // 예전엔 roomSize 사각형에서 무작위로 던지고 10번 안에 못 맞추면 방 중앙으로 폴백했는데,
    // roomSize 는 장식용 바깥 벽 띠까지 포함한 Wall 타일맵 bounds(예: 98x91)라 기각률이 높았고
    // 그 폴백 때문에 여러 마리가 정확히 같은 좌표(방 중앙)에 겹쳐 나왔다.
    private List<Vector3> _floorSpawnCells = new List<Vector3>();
    private RoomInstance _floorCellsRoom;
    private int spawnCellRevision;

    // 웨이브마다 new 로 잡던 임시 리스트들. Clear() 는 용량을 남기므로 재사용하면 두 번째 웨이브부터
    // 할당이 0 이 된다. 스폰 루틴은 한 번에 하나만 도는 게 보장되므로(_isSpawnPending) 공유해도 안전하다.
    private readonly List<Vector3> _pendingSpawnPoints = new List<Vector3>();
    private int arrivalsPending;
    private int lastSpawnFrame = -1;
    private int spawnedThisFrame;
    private readonly List<EnemyCount> _toSpawn = new List<EnemyCount>();
    private readonly List<EnemyCount> _waveEnemies = new List<EnemyCount>();
    private readonly List<GameObject> _superArmorPool = new List<GameObject>();


    private bool _augmentChosen = false; // 증강 방에서 카드를 이미 골랐는지(방 하나당 한 번)

    /// <summary>튜토리얼에서 직접 배치한 개체는 별도의 랜덤 프리팹 준비가 필요 없다.</summary>
    [SerializeField, HideInInspector] private bool workshopEncounter;
    public bool UsesRandomEnemies => !((TutorialFlow.IsRunning || workshopEncounter) && tutorialEnemiesRoot != null);
    internal void SetWorkshopEnemies(Transform root)
    {
        workshopEncounter = true;
        if (tutorialEnemiesRoot != null && tutorialEnemiesRoot != root)
        {
            tutorialEnemiesRoot.SetParent(root,true);
            tutorialEnemiesRoot.gameObject.SetActive(true);
        }
        tutorialEnemiesRoot = root;
        root.gameObject.SetActive(false);
    }

    /// <summary>생성 완료된 방의 바닥 후보를 로딩 중 계산한다. 실제 위치 추첨은 전투 시작 때 한다.</summary>
    public void PrepareSpawnCells(RoomInstance room, MapGenerationDataSO generation)
    {
        if ((!UsesRandomEnemies && !HasAuthoredWaves) || room == null) return;
        if (mapGenerationData == null) mapGenerationData = generation;
        BuildFloorSpawnCells(room);
    }

    internal IEnumerator PrepareFloorSpawnCells(RoomInstance room, MapGenerationDataSO generation)
    {
        if ((!UsesRandomEnemies && !HasAuthoredWaves) || room == null) yield break;
        if (mapGenerationData == null) mapGenerationData = generation;
        yield return CollectFloorSpawnCells(room, true);
    }

    private void Start()
    {
        if (mapGenerationData == null && GameManager.Instance != null)
        {
            mapGenerationData = GameManager.Instance.CurrentStageMapData;
        }

        if (TutorialFlow.IsRunning && tutorialEnemiesRoot != null)
            tutorialEnemiesRoot.gameObject.SetActive(false);
    }

    private readonly RoomFinalBlow finalBlow = new();

    private void Update()
    {
        // 지연 소환 중에 적 수가 0명인 것을 감지해 즉시 다음 웨이브로 넘어가는 조기 오작동 차단
        if (!_isBattleActive || _isSpawnPending) return;

        _activeEnemies.RemoveAll(item => item == null);
        if (_activeEnemies.Count != 0 && !finalBlow.Pending) return;
        bool moreWaves = (!_usingTutorialPlacedEnemies || HasAuthoredWaves) && mapGenerationData != null && _currentWave < WavesThisFloor;
        finalBlow.Resolve(!moreWaves);

        if (_activeEnemies.Count == 0)
        {
            if (moreWaves)
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

        if (MainMinionSelectionUI.Instance != null && MainMinionSelectionUI.Instance.IsOpen) MainMinionSelectionUI.Instance.Hide();
        // 전투 시작 시 들고 있던 투척물을 떨군다.

        _usingTutorialPlacedEnemies = !UsesRandomEnemies;
        if (_usingTutorialPlacedEnemies)
        {
            // Placed enemies form the opening group, before the authored cluster sequence.
            if (HasAuthoredWaves) _currentWave = 0;
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
        if (workshopEncounter)
            enemies = System.Array.FindAll(enemies, enemy =>
            {
                for (var node=enemy.transform; node!=tutorialEnemiesRoot; node=node.parent)
                    if (!node.gameObject.activeSelf) return false;
                return true;
            });
        if (enemies.Length > 0)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, spawnStartDelay)); // 카메라가 방에 안착한 뒤 예고를 보여준다

            float telegraphDuration = Mathf.Max(0.01f, spawnTelegraphDuration);
            foreach (var enemy in enemies)
                if (enemy != null) StartCoroutine(ShowArrival(enemy.transform.position, telegraphDuration));
            yield return new WaitForSeconds(telegraphDuration);
        }

        tutorialEnemiesRoot.gameObject.SetActive(true);
        _activeEnemies.Clear();

        foreach (var enemy in enemies)
            if (enemy != null)
            {
                RegisterActiveEnemy(enemy.gameObject);
                enemy.PlayArrival(enemy.MinionData != null ? enemy.MinionData.tier : EnemyTier.Normal);
            }

        _isSpawnPending = false;
        Debug.Log($"<color=cyan>[NormalRoomEvent]</color> 튜토리얼 배치 몬스터 {_activeEnemies.Count}마리를 등록했습니다.");
    }

    private IEnumerator DelayedSpawnWaves(RoomInstance room)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, spawnStartDelay)); // 카메라 워프가 안착하는 약 0.5초 동안만 대기 후 스폰
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
        spawnCellRevision++;

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
            _isSpawnPending = false;
            return;
        }

        Debug.Log($"[NormalRoom] SpawnWaves started. Wave: {_currentWave}/{WavesThisFloor}");

        _spawnedEnemyPositions.Clear();

        var clusters = GameManager.Instance.dataManager.ENEMY_CLUSTERS;
        if (!HasAuthoredWaves && (clusters == null || clusters.Count == 0))
        {
            Debug.LogWarning("[NormalRoom] No enemy clusters found in DataManager.");
            _isSpawnPending = false;
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

        _toSpawn.Clear();
        int lastWave = !HasAuthoredWaves && combineAllWaves ? Mathf.Max(_currentWave, WavesThisFloor) : _currentWave;
        int waveCount = lastWave - _currentWave + 1;
        for (int wave = _currentWave; wave <= lastWave; wave++)
            AppendWaveEnemies(HasAuthoredWaves ? authoredWaves[wave-1] : clusters[Random.Range(0, clusters.Count)], wave, tuning != null ? tuning.enemyCountScale : 1f);
        _currentWave = lastWave;
        _isSpawnPending = true;
        StartCoroutine(SpawnClusterRoutine(room, waveCount));
    }

    private void AppendWaveEnemies(EnemyClusterSO cluster, int wave, float countScale)
    {
        if (cluster == null || cluster.enemies == null) return;
        _waveEnemies.Clear();
        foreach (var enemyCount in cluster.enemies)
        {
            if (enemyCount.enemyData == null) continue;
            for (int i = 0; i < enemyCount.count; i++) _waveEnemies.Add(enemyCount);
        }

        // 합치기 전 각 웨이브에 배율/반올림을 적용해야 기존 총 마릿수가 유지된다.
        if (!Mathf.Approximately(countScale, 1f) && _waveEnemies.Count > 0)
        {
            int target = Mathf.Max(1, Mathf.RoundToInt(_waveEnemies.Count * countScale));
            while (_waveEnemies.Count > target) _waveEnemies.RemoveAt(Random.Range(0, _waveEnemies.Count));
            int seed = _waveEnemies.Count;
            while (_waveEnemies.Count < target && seed > 0) _waveEnemies.Add(_waveEnemies[Random.Range(0, seed)]);
        }

        int extra = ActiveAugment.ExtraEnemiesForWave(wave);
        int baseCount = _waveEnemies.Count;
        for (int i = 0; i < extra && baseCount > 0; i++)
            _waveEnemies.Add(_waveEnemies[Random.Range(0, baseCount)]);
        _toSpawn.AddRange(_waveEnemies);
    }

    private IEnumerator SpawnClusterRoutine(RoomInstance room, int waveCount)
    {
        var numbers = FloatingTextManager.Instance;
        if (numbers != null) numbers.ReserveForEncounter(_activeEnemies.Count + _toSpawn.Count);
        _pendingSpawnPoints.Clear();
        arrivalsPending = 0;
        float spawnDelay = Mathf.Max(.01f, spawnTelegraphDuration);
        int perFrame = Mathf.Max(1, spawnGroupPerFrame);

        int skipped = 0;
        for (int i = 0; i < _toSpawn.Count; i++)
        {
            if (i > 0 && i % perFrame == 0) yield return null;
            // 자리를 못 찾으면 건너뛴다. 예전처럼 방 중앙으로 몰아넣지 않는다.
            if (!TryFindSpawnPoint(room, _pendingSpawnPoints, out Vector3 point))
            {
                skipped++;
                continue;
            }
            _pendingSpawnPoints.Add(point);
            arrivalsPending++;
            StartCoroutine(DelayedSpawnEnemyWithVFX(_toSpawn[i], point, spawnDelay));
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

        while (arrivalsPending > 0) yield return null;
        ApplySuperArmorToRandomEnemies(Mathf.Max(0, superArmorEnemyCount) * waveCount);

        // 4. 스폰 완료되었으므로 감지 락 해제
        _isSpawnPending = false;

        Debug.Log($"<color=cyan>[NormalRoomEvent]</color> {waveCount}개 웨이브 소환 완료. Active count: {_activeEnemies.Count}");
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
        if (_floorCellsRoom == room && _floorSpawnCells.Count > 0) return;
        var build = CollectFloorSpawnCells(room, false);
        while (build.MoveNext()) { }
    }

    private IEnumerator CollectFloorSpawnCells(RoomInstance room, bool staged)
    {
        if (room == null) yield break;
        if (_floorCellsRoom == room && _floorSpawnCells.Count > 0) yield break;

        int revision = ++spawnCellRevision;

        var tm = room.groundTilemap;
        if (tm == null) yield break; // 타일맵을 못 찾은 방은 사각형 폴백으로 흘려보낸다

        BoundsInt bounds = tm.cellBounds;
        if (bounds.size.x <= 0 || bounds.size.y <= 0) yield break;
        if (bounds.size.z != 1) yield break; // 3D 타일맵은 아래 인덱싱 전제가 깨진다 — 폴백으로 넘긴다

        int cellCount = bounds.size.x * bounds.size.y;
        var candidates = new List<Vector3>();
        var tileBlock = System.Buffers.ArrayPool<TileBase>.Shared.Rent(cellCount);
        try
        {
            tm.GetTilesBlockNonAlloc(bounds, tileBlock);
            int stride = Mathf.Max(1, spawnCellStride);
            int width = bounds.size.x;

            double deadline = Time.realtimeSinceStartupAsDouble + .002;
            for (int y = 0; y < bounds.size.y; y += stride)
            {
                if (staged && Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    yield return null;
                    if (revision != spawnCellRevision) yield break;
                    deadline = Time.realtimeSinceStartupAsDouble + .002;
                }
                int rowStart = y * width;
                for (int x = 0; x < width; x += stride)
                {
                    if (tileBlock[rowStart + x] == null) continue;

                    Vector3 world = tm.GetCellCenterWorld(new Vector3Int(bounds.xMin + x, bounds.yMin + y, bounds.zMin));
                    if (!room.IsFloorAt(world)) continue;     // 벽/물 겹친 칸 제외
                    if (IsDoorKeepOut(room, world)) continue;  // 앵커 몇 개 도는 산술, 아래보다 싸다
                    if (!HasWallClearance(room, world)) continue;

                    candidates.Add(world);
                }
            }

        }
        finally { System.Buffers.ArrayPool<TileBase>.Shared.Return(tileBlock, clearArray: true); }
        _floorCellsRoom = room;
        _floorSpawnCells = candidates;
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

    private readonly List<GameObject> arrivalRings = new();

    private void OnDisable()
    {
        foreach (var ring in arrivalRings)
            if (ring != null) ring.SetActive(false);
    }

    private IEnumerator ShowArrival(Vector3 position, float duration)
    {
        GameObject ring = null;
        foreach (var existing in arrivalRings)
        {
            if (existing != null && !existing.activeSelf) { ring = existing; break; }
        }
        if (ring == null)
        {
            ring = BossTelegraph.SpawnRing(position, 1f, new Color32(226, 112, 133, 255), name: "Enemy arrival");
            ring.transform.SetParent(transform, true);
            arrivalRings.Add(ring);
        }
        ring.transform.position = position;
        ring.SetActive(true);
        var outline = ring.GetComponent<SpriteRenderer>();
        Color ringColor = outline.color;
        float diameter = Mathf.Max(.35f, spawnTelegraphScale * .5f);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.PI / 3;
            var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            WorldSparkField.Emit(WorldSparkField.Kind.Arrival, (Vector2)position + axis * diameter * .5f, -axis, 1);
        }
        float age = 0f;
        while (age < duration)
        {
            float progress = age / duration;
            ringColor.a = Mathf.SmoothStep(.3f, 1f, progress / .12f)
                * Mathf.SmoothStep(0f, 1f, (1f - progress) / .18f);
            outline.color = ringColor;
            float size = Mathf.Lerp(diameter, diameter * .4f, Mathf.SmoothStep(0, 1, progress));
            ring.transform.localScale = new Vector3(size, size * .64f, 1f);
            yield return null;
            age += Time.deltaTime;
        }
        ring.SetActive(false);
    }

    private IEnumerator DelayedSpawnEnemyWithVFX(EnemyCount enemyCount, Vector3 spawnPos, float duration)
    {
        try
        {
            yield return ShowArrival(spawnPos, duration);
            // Telegraphs that expire together share the same limit as placement and preview.
            while (lastSpawnFrame == Time.frameCount && spawnedThisFrame >= Mathf.Max(1, spawnGroupPerFrame))
                yield return null;
            if (_cachedRoom == null || _cachedRoom.isCleared) yield break;
            if (lastSpawnFrame != Time.frameCount)
            {
                lastSpawnFrame = Time.frameCount;
                spawnedThisFrame = 0;
            }
            spawnedThisFrame++;
            var enemy = GameManager.Instance.dataManager.CreateUnit(enemyCount.enemyData, spawnPos);
            if (enemy != null)
            {
                _activeEnemies.Add(enemy);
                finalBlow.Track(enemy);
                _spawnedEnemyPositions.Add(spawnPos);
            }
        }
        finally { arrivalsPending--; }
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
                status.ApplySuperArmor(Mathf.Max(1f, spawnSuperArmorAmount));
            }
        }
    }

    public void RegisterActiveEnemy(GameObject enemy)
    {
        if (enemy != null && !_activeEnemies.Contains(enemy))
        {
            _activeEnemies.Add(enemy);
            finalBlow.Track(enemy);
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
