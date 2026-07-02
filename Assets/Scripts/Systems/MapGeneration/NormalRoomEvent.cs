using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// 일반 전투 방의 이벤트를 담당합니다.
/// </summary>
public class NormalRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Combat Settings")]
    [SerializeField] private MapGenerationDataSO mapGenerationData;

    [Header("Reward Box Settings")]
    [Tooltip("방 클리어 시 한가운데에 생성할 보상 상자 프리팹을 연결해 주세요.")]
    [SerializeField] private GameObject rewardBoxPrefab;

    [Header("Unity Events")]
    public UnityEvent OnCombatStart;
    public UnityEvent OnCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private bool _isBattleActive = false;
    private bool _isSpawnPending = false; // 2.5초 지연 소환 대기 플래그
    private RoomInstance _cachedRoom;
    private int _currentWave = 1;
    private List<Vector3> _spawnedEnemyPositions = new List<Vector3>();

    private void Start()
    {
        if (mapGenerationData == null && GameManager.Instance != null)
        {
            mapGenerationData = GameManager.Instance.CurrentStageMapData;
        }
    }

    private void Update()
    {
        // 지연 소환 중에 적 수가 0명인 것을 감지해 즉시 다음 웨이브로 넘어가는 조기 오작동 차단
        if (!_isBattleActive || _isSpawnPending) return;

        int beforeCount = _activeEnemies.Count;
        _activeEnemies.RemoveAll(item => item == null);
        int afterCount = _activeEnemies.Count;

        if (beforeCount != afterCount)
        {
            Debug.Log($"<color=cyan>[NormalRoomEvent]</color> Enemy removed. Count: {beforeCount} -> {afterCount}. Remaining: {string.Join(", ", _activeEnemies.ConvertAll(e => e != null ? e.name : "null"))}");
        }

        if (_activeEnemies.Count == 0)
        {
            if (mapGenerationData != null && _currentWave < mapGenerationData.wavesCount)
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
        
        _cachedRoom = room;
        _isBattleActive = true;
        _isSpawnPending = true; // 스폰 진행 예정 상태 설정
        _currentWave = 1;

        room.SetDoorsOpen(false);

        if (GemTreeUI.Instance != null && GemTreeUI.Instance.IsOpen) GemTreeUI.Instance.Toggle();
        if (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen) HandSlotSelectionUI.Instance.Hide();
        if (GameManager.Instance?.squadSpawner != null) GameManager.Instance.squadSpawner.RefreshFullSquad();

        // 1초 후 적들이 소환되도록 텀(Term) 연출 구현
        StartCoroutine(DelayedSpawnWaves(room));

        // 플레이어 상태 업데이트
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Battle);

        OnCombatStart?.Invoke();
        Debug.Log($"<color=white>[NormalRoom]</color> Battle Started in {room.gameObject.name}");
    }

    private IEnumerator DelayedSpawnWaves(RoomInstance room)
    {
        yield return new WaitForSeconds(0.5f); // 카메라 워프가 안착하는 약 0.5초 동안만 대기 후 스폰
        _isSpawnPending = false; // 지연 해제, 이제부터 Update 감지 가능
        SpawnWaves(room);
    }

    public void OnRoomCleared(RoomInstance room)
    {
        if (GameManager.Instance?.PLAYERCONTROLLER != null)
        {
            var allyManager = GameManager.Instance.PLAYERCONTROLLER.GetComponent<AllyManager>();
            allyManager?.ClearAll();
        }

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

        Vector3 spawnPos = room.transform.position + (Vector3)room.centerOffset;
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

        Debug.Log($"[NormalRoom] SpawnWaves started. Wave: {_currentWave}/{mapGenerationData.wavesCount}");

        _spawnedEnemyPositions.Clear();

        var clusters = GameManager.Instance.dataManager.ENEMY_CLUSTERS;
        if (clusters == null || clusters.Count == 0)
        {
            Debug.LogWarning("[NormalRoom] No enemy clusters found in DataManager.");
            return;
        }

        // 웨이브당 1개의 무작위 군집 선택
        var selectedCluster = clusters[Random.Range(0, clusters.Count)];
        SpawnCluster(selectedCluster, room);

        // 생성된 적군 중 무작위 2명에게 슈퍼아머 부여
        ApplySuperArmorToRandomEnemies(2);
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

        float margin = 2.0f;
        float rangeX = (room.roomSize.x / 2f) - margin;
        float rangeY = (room.roomSize.y / 2f) - margin;

        int spawnedCount = 0;
        foreach (var enemyCount in cluster.enemies)
        {
            if (enemyCount.enemyData == null) continue;

            for (int i = 0; i < enemyCount.count; i++)
            {
                Vector3 bestPos = room.transform.position + (Vector3)room.centerOffset;
                float bestDist = -1f;

                for (int attempt = 0; attempt < mapGenerationData.maxSpawnAttempts; attempt++)
                {
                    Vector3 randPos = new Vector3(
                        Random.Range(-rangeX, rangeX),
                        Random.Range(-rangeY, rangeY),
                        0
                    );
                    Vector3 candidatePos = room.transform.position + (Vector3)room.centerOffset + randPos;

                    float minDist = float.MaxValue;
                    foreach (var center in _spawnedEnemyPositions)
                    {
                        float dist = Vector3.Distance(candidatePos, center);
                        if (dist < minDist) minDist = dist;
                    }

                    if (_spawnedEnemyPositions.Count == 0 || minDist >= mapGenerationData.minDistanceBetweenEnemies)
                    {
                        bestPos = candidatePos;
                        break;
                    }

                    if (minDist > bestDist)
                    {
                        bestDist = minDist;
                        bestPos = candidatePos;
                    }
                }

                if (NavMesh.SamplePosition(bestPos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                {
                    GameObject enemy = GameManager.Instance.dataManager.CreateUnit(enemyCount.enemyData, hit.position);
                    if (enemy != null)
                    {
                        _activeEnemies.Add(enemy);
                        _spawnedEnemyPositions.Add(hit.position);
                        spawnedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[NormalRoom] SpawnCluster: CreateUnit returned null for {enemyCount.enemyData.name}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[NormalRoom] NavMesh SamplePosition failed at {bestPos}");
                }
            }
        }
        
        Debug.Log($"[NormalRoom] SpawnCluster '{cluster.name}' finished. Spawned {spawnedCount} enemies. Total Active: {_activeEnemies.Count}");
    }

    private void ApplySuperArmorToRandomEnemies(int count)
    {
        if (_activeEnemies.Count == 0) return;
        List<GameObject> enemiesToBuff = new List<GameObject>();
        foreach (var obj in _activeEnemies)
        {
            if (obj != null) enemiesToBuff.Add(obj);
        }

        int actualCount = Mathf.Min(count, enemiesToBuff.Count);
        for (int i = 0; i < actualCount; i++)
        {
            int randIndex = Random.Range(0, enemiesToBuff.Count);
            GameObject enemyObj = enemiesToBuff[randIndex];
            enemiesToBuff.RemoveAt(randIndex);

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
}
