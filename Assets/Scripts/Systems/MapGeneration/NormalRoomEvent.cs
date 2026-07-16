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
    [Tooltip("몹 스폰 예고용으로 활용할 차오르는 원형 장판 프리팹을 지정하세요.")]
    [SerializeField] private GameObject spawnVfxPrefab;
    [Tooltip("방 벽으로부터의 스폰 최소 거리 마진(Margin)입니다. 클수록 방 중앙 쪽에 가깝게 몹들이 스폰됩니다. 기본값 3.0f")]
    [SerializeField] private float spawnMargin = 3.0f;

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
        _isSpawnPending = true; // 스폰 진행 예정 상태 설정 (스폰이 끝날 때까지 대기)
        _currentWave = 1;

        if (GemTreeUI.Instance != null && GemTreeUI.Instance.IsOpen) GemTreeUI.Instance.Toggle();
        if (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen) HandSlotSelectionUI.Instance.Hide();
        // 전투 시작 시 들고 있던 투척물을 떨군다.
        FindFirstObjectByType<ThrowController>()?.ForceClear();

        // 0.5초 후 적들이 소환되도록 텀(Term) 연출 구현
        StartCoroutine(DelayedSpawnWaves(room));

        // 플레이어 상태 업데이트
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Battle);

        OnCombatStart?.Invoke();
        Debug.Log($"<color=white>[NormalRoom]</color> Battle Started in {room.gameObject.name}");
    }

    private IEnumerator DelayedSpawnWaves(RoomInstance room)
    {
        yield return new WaitForSeconds(0.5f); // 카메라 워프가 안착하는 약 0.5초 동안만 대기 후 스폰
        SpawnWaves(room);
    }

    public void OnRoomCleared(RoomInstance room)
    {
        FindFirstObjectByType<ThrowController>()?.ForceClear();

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

        float rangeX = (room.roomSize.x / 2f) - spawnMargin;
        float rangeY = (room.roomSize.y / 2f) - spawnMargin;

        // 마진이 과도하여 음수가 될 경우를 대비한 가드
        if (rangeX < 1f) rangeX = 1f;
        if (rangeY < 1f) rangeY = 1f;

        _isSpawnPending = true; // 스폰 코루틴이 완료될 때까지 생존 감지 루프 중단 (안전장치)
        StartCoroutine(SpawnClusterRoutine(cluster, room, rangeX, rangeY));
    }

    private IEnumerator SpawnClusterRoutine(EnemyClusterSO cluster, RoomInstance room, float rangeX, float rangeY)
    {
        int totalExpected = 0;
        foreach (var enemyCount in cluster.enemies)
        {
            if (enemyCount.enemyData != null) totalExpected += enemyCount.count;
        }

        List<Vector3> targetSpawnPoints = new List<Vector3>();
        List<EnemyCount> targetEnemyCounts = new List<EnemyCount>();

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

                    // [추가] 후보 좌표가 실제 구워진 NavMesh(이동 가능 구역) 위에 존재하는지 사전 엄격 검증
                    // 타일맵뿐만 아니라 씬 내 모든 벽(Wall), 기둥, 장애물 오브젝트를 100% 한 번에 완벽 우회합니다.
                    if (UnityEngine.AI.NavMesh.SamplePosition(candidatePos, out UnityEngine.AI.NavMeshHit hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        // 벽 너머나 너무 먼 곳으로 NavMesh가 당겨져 왜곡 스폰되는 것을 철저히 차단
                        if (Vector3.Distance(candidatePos, hit.position) > 1.2f)
                        {
                            continue;
                        }
                        
                        // 검증이 완료된 NavMesh 상의 실제 유효 위치로 좌표 즉각 갱신 고정
                        candidatePos = hit.position;
                    }
                    else
                    {
                        // NavMesh가 깔려 있지 않은 벽 속이거나 공백 구역이므로 탈락 처리
                        continue;
                    }

                    float minDist = float.MaxValue;
                    foreach (var center in _spawnedEnemyPositions)
                    {
                        float dist = Vector3.Distance(candidatePos, center);
                        if (dist < minDist) minDist = dist;
                    }
                    foreach (var center in targetSpawnPoints)
                    {
                        float dist = Vector3.Distance(candidatePos, center);
                        if (dist < minDist) minDist = dist;
                    }

                    if ((_spawnedEnemyPositions.Count == 0 && targetSpawnPoints.Count == 0) || minDist >= mapGenerationData.minDistanceBetweenEnemies)
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

                targetSpawnPoints.Add(bestPos);
                targetEnemyCounts.Add(enemyCount);
            }
        }

        float spawnDelay = 1.0f; // 장판이 가득 차오르는 선딜 시간

        // 1. 모든 몹들의 스폰 위치에 예고 장판(VFX) 동시 소환
        for (int i = 0; i < targetSpawnPoints.Count; i++)
        {
            StartCoroutine(DelayedSpawnEnemyWithVFX(targetEnemyCounts[i], targetSpawnPoints[i], spawnDelay));
        }

        // 2. 장판 차오르는 시간만큼 대기 (충돌 및 생성 지연 안정성을 위해 0.1초 버퍼 추가)
        yield return new WaitForSeconds(spawnDelay + 0.1f);

        // 3. 무작위 적군 2명에게 슈퍼아머 부여
        ApplySuperArmorToRandomEnemies(2);

        // 4. 스폰 완료되었으므로 감지 락 해제
        _isSpawnPending = false;
        
        Debug.Log($"<color=cyan>[NormalRoomEvent]</color> Finished spawning cluster '{cluster.name}'. Active count: {_activeEnemies.Count}");
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
                DamageInfo dummyInfo = new DamageInfo(0f, DamageType.Physical, this.gameObject, false, 0f, false);
                hitbox.Init(dummyInfo, 0, 0.05f, duration, false);
                
                // 프리팹 스케일 조절 (기본 원 크기가 1.0이므로 2.5f정도로 키움)
                vfxObj.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
            }
        }

        yield return new WaitForSeconds(duration);

        if (vfxObj != null) Destroy(vfxObj);

        // 실제 몹 소환 (샘플링 반경을 1.5f로 좁혀 복도나 벽 너머로의 몹 빨려 들어감 삐침 현상 완치)
        if (_cachedRoom != null && !_cachedRoom.isCleared && NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
        {
            GameObject enemy = GameManager.Instance.dataManager.CreateUnit(enemyCount.enemyData, hit.position);
            if (enemy != null)
            {
                _activeEnemies.Add(enemy);
                _spawnedEnemyPositions.Add(hit.position);
            }
        }
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
