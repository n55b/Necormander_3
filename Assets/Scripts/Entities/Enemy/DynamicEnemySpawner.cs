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

    private List<MinionDataSO> _enemyDataList = new List<MinionDataSO>();
    private List<GameObject> _activeEnemies = new List<GameObject>();
    private float _spawnTimer;
    private Transform _playerTransform;
    private bool _isTriggered = false;
    private bool _rewardGiven = false;

    private void Start()
    {
        // DataManager로부터 이번 맵에서 소환할 수 있는 적군 데이터 목록을 가져옵니다.
        if (GameManager.Instance != null && GameManager.Instance.dataManager != null)
        {
            _enemyDataList = GameManager.Instance.dataManager.ENEMY_MINION_DATA;
        }

        if (_enemyDataList == null || _enemyDataList.Count == 0)
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
        if (_playerTransform == null || _enemyDataList == null || _enemyDataList.Count == 0) return;

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
        if (_enemyDataList == null || _enemyDataList.Count == 0) return null;
        return _enemyDataList[Random.Range(0, _enemyDataList.Count)];
    }

    private void TriggerEncounter()
    {
        _isTriggered = true;

        // 부대 갱신 및 스폰 로직만 수행
        if (GameManager.Instance?.squadSpawner != null)
        {
            GameManager.Instance.squadSpawner.RefreshFullSquad();
        }

        for (int i = 0; i < groupsCount; i++)
        {
            Vector2 randomPosInsideCircle = Random.insideUnitCircle * spawnDistanceFromCenter;
            Vector3 groupCenter = transform.position + new Vector3(randomPosInsideCircle.x, randomPosInsideCircle.y, 0f);

            SpawnGroup(groupCenter);
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
}
