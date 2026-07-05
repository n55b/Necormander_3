using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public enum SpawnType
{
    Periodic,   // 기존의 주기적 소환
    Encounter   // 엔터 더 건전 스타일 (방 진입 시 기습)
}

/// <summary>
/// 플레이어 감지 시 적을 생성하는 스포너입니다. 
/// 주기적 소환 또는 랜덤 위치 기습(Encounter) 모드를 지원합니다.
/// </summary>
public class DynamicEnemySpawner : MonoBehaviour
{
    [Header("General Settings")]
    [SerializeField] private SpawnType spawnType = SpawnType.Encounter;
    [SerializeField] private RoomType roomType = RoomType.Normal; // [추가] 방 타입 설정
    [SerializeField] private float activationRange = 10.0f;

    [Header("Portal Settings")]
    [SerializeField] private GameObject portalPrefab;

    [Header("Encounter Settings (Gungeon Style)")]
    [SerializeField] private int groupsCount = 4;           // 생성할 부대(그룹)의 수
    [SerializeField] private int enemiesPerGroup = 3;       // 각 부대당 마릿수
    [SerializeField] private float spawnDistanceFromCenter = 7.0f; // 중심에서 최대 얼마나 떨어진 곳까지 생성할지
    [SerializeField] private float groupSpread = 1.5f;      // 부대 내 배치 간격
    [SerializeField] private bool triggerOnlyOnce = true;  // 한 번만 발동할지

    [Header("Periodic Settings")]
    [SerializeField] private float spawnInterval = 3.0f;
    [SerializeField] private float spawnRadius = 5.0f;
    [SerializeField] private int maxActiveEnemies = 10;

    [Header("Visual Debug")]
    [SerializeField] private bool showGizmos = true;

    [Header("Unity Event")]
    [SerializeField] private UnityEvent InRoomEvent;
    [SerializeField] private UnityEvent OutRoomEvent;
    [SerializeField] private UnityEvent<Vector3> EliteRoomEvent;

    private List<MinionDataSO> _normalEnemyPool = new List<MinionDataSO>();
    private List<MinionDataSO> _bossEnemyPool = new List<MinionDataSO>();
    private List<GameObject> _activeEnemies = new List<GameObject>();
    private float _spawnTimer;
    private Transform _playerTransform;
    private bool _isTriggered = false;
    private bool _rewardGiven = false;

    /// <summary>
    /// [추가] 현재 전투 이벤트(Encounter)가 활발하게 진행 중인지 여부를 반환합니다.
    /// UI 차단 판정 등에 활용됩니다.
    /// </summary>
    public bool IsEventActive => _isTriggered && !_rewardGiven;

    private void Start()
    {
        // DataManager로부터 이번 맵에서 소환할 수 있는 적군 데이터 목록을 가져옵니다.
        if (GameManager.Instance != null && GameManager.Instance.dataManager != null)
        {
            // 1. 일반 적군 목록에서 스폰 대상 분류
            var rawList = GameManager.Instance.dataManager.ENEMY_MINION_DATA;
            if (rawList != null)
            {
                foreach (var data in rawList)
                {
                    if (data.isElite) 
                    {
                        _bossEnemyPool.Add(data);
                    }
                    else if (data.canSpawnRandomly) 
                    {
                        _normalEnemyPool.Add(data);
                    }
                }
            }

            // 2. 새롭게 분리된 엘리트 전용 목록에서도 가져와 스폰 대상 분류 연동
            var eliteList = GameManager.Instance.dataManager.ELITE_MINION_DATA;
            if (eliteList != null)
            {
                foreach (var data in eliteList)
                {
                    if (data.isElite)
                    {
                        if (!_bossEnemyPool.Contains(data)) _bossEnemyPool.Add(data);
                    }
                    else if (data.canSpawnRandomly)
                    {
                        if (!_normalEnemyPool.Contains(data)) _normalEnemyPool.Add(data);
                    }
                }
            }
        }

        if (_normalEnemyPool.Count == 0 && _bossEnemyPool.Count == 0)
        {
            Debug.LogError($"<color=red>[DynamicEnemySpawner]</color> {gameObject.name}: DataManager에서 적군 미니언 데이터를 찾을 수 없습니다! Registry 설정을 확인하세요.");
        }

        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            _playerTransform = GameManager.Instance.PLAYERCONTROLLER.transform;
        }
        else
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) _playerTransform = playerObj.transform;
        }
    }

    private void Update()
    {
        if (_playerTransform == null || (_normalEnemyPool.Count == 0 && _bossEnemyPool.Count == 0)) return;

        // --- [1] 클리어 체크 및 보상 로직 ---
        if (_isTriggered && !_rewardGiven && spawnType == SpawnType.Encounter)
        {
            _activeEnemies.RemoveAll(item => item == null);
            if (_activeEnemies.Count == 0)
            {
                _rewardGiven = true; // 이제 이 방은 '클리어' 상태입니다.

                // 아군 미니언 정리 및 보상 요청
                if (GameManager.Instance?.PLAYERCONTROLLER != null)
                {
                    var allyManager = GameManager.Instance.PLAYERCONTROLLER.GetComponent<AllyManager>();
                    allyManager?.ClearAll();
                }

                if (RewardManager.Instance != null)
                    RewardManager.Instance.RequestClearReward(roomType);

                if (roomType == RoomType.Boss)
                {
                    SpawnPortal();
                }

                OutRoomEvent?.Invoke(); // 문 열림 등 퇴장 이벤트
                Debug.Log("<color=green>[Spawner]</color> Room Cleared! Events reset disabled.");
            }
        }

        // --- [2] 플레이어 감지 및 실행 로직 ---
        float distanceToPlayer = Vector2.Distance(transform.position, _playerTransform.position);

        if (distanceToPlayer <= activationRange)
        {
            // 이미 보상을 받았다면(클리어했다면) 더 이상 아무것도 하지 않음
            if (_rewardGiven) return;

            if (spawnType == SpawnType.Encounter)
            {
                // 발동되지 않은 상태에서만 트리거 실행
                if (!_isTriggered)
                {
                    // 진입 이벤트 실행 (여기서 실행하면 딱 한 번만 터집니다)
                    InRoomEvent?.Invoke();
                    TriggerEncounter();
                }
            }
            else
            {
                UpdatePeriodicSpawn();
            }
        }
        else
        {
            // 플레이어가 범위를 벗어났을 때 (아직 클리어 전이라면) 다시 리셋할지 여부
            if (spawnType == SpawnType.Encounter && _isTriggered && !triggerOnlyOnce && !_rewardGiven)
            {
                _isTriggered = false;
            }
        }
    }

    private MinionDataSO GetRandomEnemyData()
    {
        if (_normalEnemyPool.Count == 0) return null;
        return _normalEnemyPool[Random.Range(0, _normalEnemyPool.Count)];
    }

    private void TriggerEncounter()
    {
        _isTriggered = true;

        // [추가] 전투 진입 시 열려 있는 UI 자동 종료
        if (GemTreeUI.Instance != null && GemTreeUI.Instance.IsOpen) GemTreeUI.Instance.Toggle();
        if (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen) HandSlotSelectionUI.Instance.Hide();

        // 부대 갱신 및 스폰 로직만 수행
        if (GameManager.Instance?.squadSpawner != null)
        {
            GameManager.Instance.squadSpawner.RefreshFullSquad();
        }

        // [추가] 보스 방일 경우 단일 보스만 소환
        if (roomType == RoomType.Boss)
        {
            SpawnBoss();
            return;
        }

        for (int i = 0; i < groupsCount; i++)
        {
            Vector2 randomPosInsideCircle = Random.insideUnitCircle * spawnDistanceFromCenter;
            Vector3 groupCenter = transform.position + new Vector3(randomPosInsideCircle.x, randomPosInsideCircle.y, 0f);

            SpawnGroup(groupCenter);
        }
    }

    private void SpawnBoss()
    {
        if (_bossEnemyPool.Count == 0)
        {
            Debug.LogError($"<color=red>[Spawner]</color> {gameObject.name}: Boss Room이지만 보스 데이터 풀이 비어있습니다!");
            return;
        }

        // 현재 풀에서 랜덤 보스 선택 (나중에 스테이지별 필터링 가능)
        MinionDataSO data = _bossEnemyPool[Random.Range(0, _bossEnemyPool.Count)];

        Vector3 spawnPos = transform.position;
        if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
        {
            spawnPos = hit.position;
        }

        GameObject bossObj = GameManager.Instance.dataManager.CreateUnit(data, spawnPos);
        if (bossObj != null)
        {
            _activeEnemies.Add(bossObj);
            if (bossObj.TryGetComponent<BaseEntity>(out var entity))
            {
                entity.Spawner = this;
            }
            Debug.Log($"<color=red>[Spawner]</color> Boss <b>{data.minionName}</b> Spawned from Pool!");
        }
    }

    private void SpawnGroup(Vector3 center)
    {
        for (int i = 0; i < enemiesPerGroup; i++)
        {
            Vector2 offset = Random.insideUnitCircle * groupSpread;
            Vector3 spawnPos = center + new Vector3(offset.x, offset.y, 0f);

            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                spawnPos = hit.position;
                MinionDataSO data = GetRandomEnemyData();

                // [수정] 조립은 DataManager에게 맡김
                GameObject enemyObj = GameManager.Instance.dataManager.CreateUnit(data, spawnPos);
                if (enemyObj != null)
                {
                    _activeEnemies.Add(enemyObj);
                    if (enemyObj.TryGetComponent<BaseEntity>(out var entity))
                    {
                        entity.Spawner = this;
                    }
                }
            }
        }

        // 엘리트 보스 귀찮으니까 일단 여기서 소환
        if(roomType == RoomType.Elite)
        {
            EliteRoomEvent?.Invoke(center);
        }
    }

    private void UpdatePeriodicSpawn()
    {
        _activeEnemies.RemoveAll(item => item == null);
        if (_activeEnemies.Count >= maxActiveEnemies) return;

        _spawnTimer += Time.deltaTime;
        if (_spawnTimer >= spawnInterval)
        {
            Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = transform.position + new Vector3(randomCircle.x, randomCircle.y, 0f);

            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                spawnPos = hit.position;
                MinionDataSO data = GetRandomEnemyData();

                // [수정] 조립은 DataManager에게 맡김
                GameObject enemyObj = GameManager.Instance.dataManager.CreateUnit(data, spawnPos);
                if (enemyObj != null)
                {
                    _activeEnemies.Add(enemyObj);
                }
                _spawnTimer = 0f;
            }
            else
            {
                _spawnTimer = spawnInterval * 0.8f;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        // 플레이어 감지 범위 (하늘색)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, activationRange);

        if (spawnType == SpawnType.Encounter)
        {
            // 기습 가능 영역 표시 (빨간색)
            Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
            Gizmos.DrawSphere(transform.position, spawnDistanceFromCenter);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, spawnDistanceFromCenter);
        }
        else
        {
            // 주기적 소환 범위 (노란색)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
        }
    }

    private void SpawnPortal()
    {
        Vector3 spawnPos = transform.position;
        if (portalPrefab != null)
        {
            Instantiate(portalPrefab, spawnPos, Quaternion.identity);
            Debug.Log("<color=purple>[Spawner]</color> Spawned Portal from Prefab.");
        }
        else
        {
            GameObject portalObj = new GameObject("FloorProceedPortal");
            portalObj.transform.position = spawnPos;
            portalObj.AddComponent<FloorProceedPortal>();
            Debug.Log("<color=purple>[Spawner]</color> Created FloorProceedPortal dynamically since Prefab is null.");
        }
    }

    public void RegisterActiveEnemy(GameObject enemy)
    {
        if (enemy != null && !_activeEnemies.Contains(enemy))
        {
            _activeEnemies.Add(enemy);
        }
    }
}
