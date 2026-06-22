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
    [SerializeField] private float groupSpread = 1.5f;

    [Header("Unity Events")]
    public UnityEvent OnCombatStart;
    public UnityEvent OnCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private bool _isBattleActive = false;
    private RoomInstance _cachedRoom;
    private int _currentWave = 1;
    private List<Vector3> _spawnedClusterCenters = new List<Vector3>();

    private void Start()
    {
        if (mapGenerationData == null && GameManager.Instance != null)
        {
            mapGenerationData = GameManager.Instance.CurrentStageMapData;
        }
    }

    private void Update()
    {
        if (!_isBattleActive) return;

        _activeEnemies.RemoveAll(item => item == null);
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
            }
        }
    }

    public void OnPlayerEnter(RoomInstance room)
    {
        if (_isBattleActive) return;
        
        _cachedRoom = room;
        _isBattleActive = true;
        _currentWave = 1;

        room.SetDoorsOpen(false);

        if (GemTreeUI.Instance != null && GemTreeUI.Instance.IsOpen) GemTreeUI.Instance.Toggle();
        if (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen) HandSlotSelectionUI.Instance.Hide();
        if (GameManager.Instance?.squadSpawner != null) GameManager.Instance.squadSpawner.RefreshFullSquad();

        SpawnWaves(room);

        // [추가] 플레이어 상태 업데이트 코드 추가
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Battle);

        OnCombatStart?.Invoke();
        Debug.Log($"<color=white>[NormalRoom]</color> Battle Started in {room.gameObject.name}");
    }

    public void OnRoomCleared(RoomInstance room)
    {
        if (GameManager.Instance?.PLAYERCONTROLLER != null)
        {
            var allyManager = GameManager.Instance.PLAYERCONTROLLER.GetComponent<AllyManager>();
            allyManager?.ClearAll();
        }

        if (RewardManager.Instance != null)
            RewardManager.Instance.RequestClearReward(room.roomType);

        // [추가] 플레이어 상태 업데이트 코드 추가
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Idle);

        OnCombatClear?.Invoke();
        Debug.Log($"<color=green>[NormalRoom]</color> Cleared!");
    }

    private void SpawnWaves(RoomInstance room)
    {
        if (mapGenerationData == null)
        {
            Debug.LogWarning("[NormalRoom] MapGenerationData is not assigned!");
            return;
        }

        Debug.Log($"[NormalRoom] SpawnWaves started. Wave: {_currentWave}/{mapGenerationData.wavesCount}, Clusters to spawn: {mapGenerationData.clustersPerWave}");

        if (mapGenerationData.clustersPerWave <= 0)
        {
            Debug.LogWarning("[NormalRoom] mapGenerationData.clustersPerWave is 0 or less! No enemies will be spawned.");
        }

        float margin = 2.0f;
        float rangeX = (room.roomSize.x / 2f) - margin;
        float rangeY = (room.roomSize.y / 2f) - margin;

        _spawnedClusterCenters.Clear();

        var clusters = GameManager.Instance.dataManager.ENEMY_CLUSTERS;
        if (clusters == null || clusters.Count == 0)
        {
            Debug.LogWarning("[NormalRoom] No enemy clusters found in DataManager.");
            return;
        }

        for (int i = 0; i < mapGenerationData.clustersPerWave; i++)
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
                foreach (var center in _spawnedClusterCenters)
                {
                    float dist = Vector3.Distance(candidatePos, center);
                    if (dist < minDist) minDist = dist;
                }

                if (_spawnedClusterCenters.Count == 0 || minDist >= mapGenerationData.minDistanceBetweenClusters)
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

            _spawnedClusterCenters.Add(bestPos);
            
            // Random Cluster Select
            var selectedCluster = clusters[Random.Range(0, clusters.Count)];
            SpawnCluster(selectedCluster, bestPos);
        }

        // [추가] 생성된 적군 중 무작위 2명에게 슈퍼아머 부여
        ApplySuperArmorToRandomEnemies(2);
    }

    private void SpawnCluster(EnemyClusterSO cluster, Vector3 center)
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

        int spawnedCount = 0;
        foreach (var enemyCount in cluster.enemies)
        {
            if (enemyCount.enemyData == null)
            {
                Debug.LogWarning($"[NormalRoom] SpawnCluster: cluster '{cluster.name}' has an enemy with missing Data!");
                continue;
            }
            if (enemyCount.count <= 0)
            {
                Debug.LogWarning($"[NormalRoom] SpawnCluster: cluster '{cluster.name}' enemy '{enemyCount.enemyData.name}' count is {enemyCount.count}!");
                continue;
            }

            for (int i = 0; i < enemyCount.count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * groupSpread;
                Vector3 spawnPos = center + (Vector3)offset;

                if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                {
                    GameObject enemy = GameManager.Instance.dataManager.CreateUnit(enemyCount.enemyData, hit.position);
                    if (enemy != null)
                    {
                        _activeEnemies.Add(enemy);
                        spawnedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[NormalRoom] SpawnCluster: CreateUnit returned null for {enemyCount.enemyData.name}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[NormalRoom] NavMesh SamplePosition failed at {spawnPos}");
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
}
