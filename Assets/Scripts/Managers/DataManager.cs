using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
/// 게임의 정적 데이터(미니언 정보, 조합 정보, 설정값)를 제공하는 순수 데이터 제공 클래스입니다.
/// </summary>
public class DataManager : MonoBehaviour
{
    [Header("성장 데이터")]
    [SerializeField] private GrowthRegistrySO growthRegistry;
    public GrowthRegistrySO GET_GROWTH_REGISTRY() => growthRegistry;
    [SerializeField] private MinionRegistrySO minionRegistry;
    [SerializeField] private AIPatternSO defaultAIPattern; 
    [SerializeField] private ShopRegistrySO shopRegistry;
    [SerializeField] private EnemyClusterRegistrySO clusterRegistry;
    [Tooltip("증강 선택 방의 페널티/보상 테이블.")]
    [SerializeField] private AugmentTableSO augmentTable;

    public AIPatternSO DEFAULT_AI_PATTERN => defaultAIPattern;

    // 데이터 게터
    public List<MinionDataSO> ALL_MINION_DATA => minionRegistry != null ? minionRegistry.allyMinionData : null;
    public List<EnemyMinionDataSO> ENEMY_MINION_DATA => minionRegistry != null ? minionRegistry.enemyMinionData : null;
    public List<EnemyMinionDataSO> ELITE_MINION_DATA => minionRegistry != null ? minionRegistry.eliteMinionData : null; // [추가] 엘리트 데이터 접근
    public List<EnemyMinionDataSO> BOSS_MINION_DATA => minionRegistry != null ? minionRegistry.bossMinionData : null; // [추가] 보스 데이터 접근
    public ShopRegistrySO SHOP_REGISTRY => shopRegistry;
    public List<EnemyClusterSO> ENEMY_CLUSTERS => clusterRegistry != null ? clusterRegistry.enemyClusters : new List<EnemyClusterSO>();
    public AugmentTableSO AUGMENT_TABLE => augmentTable;

    /// <summary>
    /// 다른 매니저들이 준비되기 전에 가장 먼저 초기화되어야 합니다.
    /// </summary>
    public void Initialize()
    {
        Debug.Log("<color=green>[DataManager]</color> Initialized.");
    }

    public MinionDataSO GetMinionData(CommandData type)
    {
        if (minionRegistry == null) return null;

        foreach (var data in minionRegistry.allyMinionData)
            if (data.minionType == type) return data;
        
        foreach (var data in minionRegistry.enemyMinionData)
            if (data.minionType == type) return data;

        if (minionRegistry.eliteMinionData != null)
        {
            foreach (var data in minionRegistry.eliteMinionData)
                if (data.minionType == type) return data;
        }

        if (minionRegistry.bossMinionData != null)
        {
            foreach (var data in minionRegistry.bossMinionData)
                if (data.minionType == type) return data;
        }
        
        return null;
    }

    [Header("몬스터·등장 및 이펙트 사전 준비")]
    [SerializeField, Min(0), InspectorName("종류별 최대 준비 수")]
    [Tooltip("맵 로딩 중 몬스터와 이펙트 프리팹 종류별로 미리 만들어 둘 최대 수. 0이면 사전 준비를 끕니다. 준비한 개체가 부족하면 기존 방식으로 생성합니다.")]
    private int prewarmPerPrefab = 12;
    [SerializeField, Min(0), InspectorName("전체 최대 준비 수")]
    [Tooltip("미리 보관할 몬스터와 이펙트 총 상한. 숫자를 높이면 메모리와 로딩 시간이 늘어납니다. 0이면 사전 준비를 끕니다.")]
    private int prewarmTotalLimit = 96;
    [SerializeField, Min(1), InspectorName("재준비 간격 (프레임)")]
    [Tooltip("사용한 대기 개체를 게임 중 보충할 때 생성 간격입니다. 한 번에 한 개씩만 준비합니다.")]
    private int prewarmRefillFrames = 4;

    private readonly Dictionary<GameObject, Queue<GameObject>> _preparedUnits = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, int> _prepareTargets = new Dictionary<GameObject, int>();
    private Transform _prepareRoot;
    private Coroutine _refillRoutine;

    // 비활성 부모 아래에서 만들기 때문에 Awake/OnEnable, AI, 물리 및 전투 등록은
    // 실제 소환 위치로 옮겨 활성화할 때 처음 실행된다.
    private void PrepareOne(GameObject prefab)
    {
        if (prefab == null) return;
        if (_prepareRoot == null)
        {
            var root = new GameObject("등장 사전 준비 (비활성)");
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            _prepareRoot = root.transform;
        }
        if (!_preparedUnits.TryGetValue(prefab, out var queue))
            _preparedUnits.Add(prefab, queue = new Queue<GameObject>());
        var unit = Instantiate(prefab, _prepareRoot);
        unit.SetActive(false);
        queue.Enqueue(unit);
    }

    private void AddPrepareTarget(EnemyMinionDataSO data, int count)
    {
        if (data == null || data.minionPrefab == null || !data.minionPrefab.activeSelf || count <= 0) return;
        AddPrepareTarget(data.minionPrefab, count);
        var catalog = PolishFeedback.Catalog;
        if (catalog != null && catalog.enabled && catalog.prewarmFreezeCount > 0)
            foreach (var visual in data.minionPrefab.GetComponentsInChildren<CharacterVisualFeedback>(true))
                AddPrepareTarget(visual.FreezingVfxPrefab, catalog.prewarmFreezeCount);
    }

    private void AddPrepareTarget(GameObject prefab, int count)
    {
        if (prefab == null || !prefab.activeSelf || count <= 0) return;
        _prepareTargets.TryGetValue(prefab, out int previous);
        _prepareTargets[prefab] = Mathf.Min(Mathf.Max(0, prewarmPerPrefab), Mathf.Max(previous, count));
    }

    /// <summary>무작위 추첨은 건드리지 않고 이 층에서 가능한 프리팹을 로딩 중 준비한다.</summary>
    public IEnumerator PrewarmEncounter(MapGenerator map, MapGenerationDataSO generation, int floor)
    {
        if (map == null || prewarmPerPrefab <= 0 || prewarmTotalLimit <= 0) yield break;
        _prepareTargets.Clear();
        var catalog = PolishFeedback.Catalog;
        var player = GameManager.Instance != null ? GameManager.Instance.PLAYERCONTROLLER : null;
        if (catalog != null && catalog.enabled && player != null)
        {
            var dodge = player.GetComponent<MeleeDodgeController>();
            var footsteps = player.GetComponentInChildren<BodyAnimationEventRelay>(true);
            if (PolishFeedback.ExistingEnabled(ExistingFeedbackCue.DashSmoke) && dodge != null)
                AddPrepareTarget(dodge.DashEffectPrefab, catalog.prewarmMovementCount);
            if (PolishFeedback.ExistingEnabled(ExistingFeedbackCue.WalkSmoke) && footsteps != null)
                AddPrepareTarget(footsteps.WalkEffectPrefab, catalog.prewarmMovementCount);
        }
        int randomRooms = 0;
        foreach (var room in map.AllRooms)
        {
            if (room == null) continue;
            var normal = room.GetComponent<NormalRoomEvent>();
            if (normal != null)
            {
                if (normal.UsesRandomEnemies) randomRooms++;
                AddPrepareTarget(normal.SpawnTelegraphPrefab, prewarmPerPrefab);
            }
            if (room.roomType == RoomType.Elite)
            {
                var elite = room.GetComponent<EliteRoomEvent>();
                if (elite != null && !elite.SkipEncounterForCurrentFloor)
                    AddPrepareTarget(elite.PrewarmEliteData, elite.PrewarmEliteCount);
            }
            if (room.roomType == RoomType.Boss)
            {
                var boss = room.GetComponent<BossRoomEvent>();
                if (boss != null) AddPrepareTarget(boss.PrewarmBossData, 1);
            }
        }
        var tuning = generation != null ? generation.GetTuningForFloor(floor) : null;
        var clusters = tuning != null && tuning.clusterPool != null && tuning.clusterPool.Exists(c => c != null)
            ? tuning.clusterPool : ENEMY_CLUSTERS;
        int waves = generation != null ? Mathf.Max(1, generation.GetWavesForFloor(floor)) : 1;
        float scale = tuning != null ? Mathf.Max(1f, tuning.enemyCountScale) : 1f;
        if (randomRooms > 0 && clusters != null)
        {
            foreach (var cluster in clusters)
            {
                if (cluster == null || cluster.enemies == null) continue;
                foreach (var entry in cluster.enemies)
                    AddPrepareTarget(entry.enemyData, Mathf.CeilToInt(Mathf.Max(0, entry.count) * scale) * randomRooms * waves);
            }
        }
        int prepared = 0;
        // 상한에 걸린 종류는 나중에 보충하지 않는다. 최초 로딩과 동일한 총량만 유지한다.
        var prefabs = new List<GameObject>(_prepareTargets.Keys);
        foreach (var prefab in prefabs)
        {
            int target = Mathf.Min(_prepareTargets[prefab], Mathf.Max(0, prewarmTotalLimit - prepared));
            _prepareTargets[prefab] = target;
            for (int i = 0; i < target; i++)
            {
                PrepareOne(prefab);
                prepared++;
                yield return null;
            }
        }
    }

    private IEnumerator RefillPreparedUnits()
    {
        while (true)
        {
            for (int i = 0; i < Mathf.Max(1, prewarmRefillFrames); i++) yield return null;
            GameObject missing = null;
            foreach (var entry in _prepareTargets)
            {
                if (entry.Key == null || entry.Value <= 0) continue;
                int count = _preparedUnits.TryGetValue(entry.Key, out var queue) ? queue.Count : 0;
                if (count < entry.Value) { missing = entry.Key; break; }
            }
            if (missing == null) break;
            PrepareOne(missing);
        }
        _refillRoutine = null;
    }

    public GameObject TakePreparedObject(GameObject prefab, Vector3 position)
    {
        GameObject unit = null;
        if (_preparedUnits.TryGetValue(prefab, out var queue))
            while (queue.Count > 0 && unit == null) unit = queue.Dequeue();
        if (unit == null) return null;
        unit.transform.SetParent(null, false);
        unit.transform.SetPositionAndRotation(position, Quaternion.identity);
        unit.SetActive(true);
        if (_refillRoutine == null && isActiveAndEnabled) _refillRoutine = StartCoroutine(RefillPreparedUnits());
        return unit;
    }

    public GameObject CreateUnit(EnemyMinionDataSO data, Vector3 position)
    {
        if (data == null || data.minionPrefab == null) return null;
        GameObject unitObj = TakePreparedObject(data.minionPrefab, position);
        if (unitObj == null) unitObj = Instantiate(data.minionPrefab, position, Quaternion.identity);
        if (unitObj.TryGetComponent<BaseEntity>(out var entity)) entity.Initialize(data);

        // [증강 페널티] '적 체력/공격력 +N%'. 적이 태어나는 길목이 여기 하나뿐이라
        // 분열로 생긴 적까지 전부 걸린다. Initialize 가 Mods.Clear() 를 하므로 반드시 그 뒤여야 한다.
        ActiveAugment.ApplyToSpawnedEnemy(unitObj);
        return unitObj;
    }
}
