using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// 엘리트 전투 방의 이벤트를 담당합니다. (강력한 엘리트 몹만 소환)
/// </summary>
public class EliteRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Elite Settings")]
    [SerializeField] private int eliteCount = 1;
    [Tooltip("이 방에 나올 엘리트를 못박는다. 전용 방 프리팹에 미리 꽂아두는 용도.\n" +
             "비워두면 MapGenerator 가 이 층에 정한 엘리트를 쓰고, 그것도 없으면 풀에서 무작위로 뽑는다.")]
    [SerializeField] private EnemyMinionDataSO forcedElite;
    [SerializeField] private GameObject portalObject;

    [Header("Reward Box Settings")]
    [Tooltip("방 클리어 시 생성할 보상 상자 프리팹. 비워두면 상자 없이 보상이 즉시 지급됩니다.")]
    [SerializeField] private GameObject rewardBoxPrefab;
    [Tooltip("상자가 나올 자리. 방 프리팹 안에 빈 오브젝트를 놓고 연결하세요. 비워두면 방 정중앙.")]
    [SerializeField] private Transform rewardSpawnPoint;

    [Header("Unity Events")]
    public UnityEvent OnEliteCombatStart;
    public UnityEvent OnEliteCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private List<EnemyMinionDataSO> _eliteEnemyPool = new List<EnemyMinionDataSO>(); 
    private bool _isBattleActive = false;
    private bool _isSpawnPending = false; // 2.5초 지연 소환 대기 플래그
    private RoomInstance _cachedRoom;
    private EnemyMinionDataSO _hpBarTargetData; // 상단 체력바에 이름을 띄울 엘리트

    private void Start()
    {
        // 씬에 미리 배치해 둔 포탈 오브젝트가 있다면 시작 시 비활성화
        if (portalObject != null && portalObject.scene.IsValid())
        {
            portalObject.SetActive(false);
        }

        if (GameManager.Instance != null && GameManager.Instance.dataManager != null)
        {
            // 1. 일반 적군 목록에서 엘리트 데이터 수집
            var rawList = GameManager.Instance.dataManager.ENEMY_MINION_DATA;
            if (rawList != null)
            {
                foreach (var data in rawList)
                {
                    if (data.IsSpawnableElite) _eliteEnemyPool.Add(data);
                }
            }
            
            // 2. 새롭게 분리된 엘리트 전용 목록에서도 유니크하게 엘리트 수집
            var eliteList = GameManager.Instance.dataManager.ELITE_MINION_DATA;
            if (eliteList != null)
            {
                foreach (var data in eliteList)
                {
                    if (data.IsSpawnableElite && !_eliteEnemyPool.Contains(data)) _eliteEnemyPool.Add(data);
                }
            }
            Debug.Log($"<color=red>[EliteRoom]</color> Pool Initialized. Elites: {_eliteEnemyPool.Count} in {gameObject.name}");
        }
    }

    private void Update()
    {
        // 지연 스폰 중에는 엘리트 몹 수가 0명이므로 즉시 클리어되는 현상 방지
        if (!_isBattleActive || _isSpawnPending) return;

        int beforeCount = _activeEnemies.Count;
        _activeEnemies.RemoveAll(item => item == null);
        int afterCount = _activeEnemies.Count;

        if (beforeCount != afterCount)
        {
            Debug.Log($"<color=red>[EliteRoomEvent]</color> Enemy removed. Count: {beforeCount} -> {afterCount}. Remaining: {string.Join(", ", _activeEnemies.ConvertAll(e => e != null ? e.name : "null"))}");
        }

        if (_activeEnemies.Count == 0)
        {
            _isBattleActive = false;
            _cachedRoom.MarkCleared();
            Debug.Log("<color=green>[EliteRoomEvent]</color> All enemies cleared. Marking room cleared.");
        }
    }

    public void OnPlayerEnter(RoomInstance room)
    {
        if (_isBattleActive) return;
        
        _cachedRoom = room;
        _isBattleActive = true;
        _isSpawnPending = true; // 스폰 진행 예정 상태 설정

        room.SetDoorsOpen(false); // 문 폐쇄

        if (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen) HandSlotSelectionUI.Instance.Hide();
        // 전투 시작 시 들고 있던 투척물을 떨군다.

        // 1초 후 적들이 소환되도록 텀(Term) 연출 구현
        StartCoroutine(DelayedSpawnElite(room));

        // [추가] 플레이어 상태 업데이트 코드 추가
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Battle);

        OnEliteCombatStart?.Invoke();
        Debug.Log($"<color=red>[EliteRoom]</color> Warning! Elite Encounter in {room.gameObject.name}");
    }

    private IEnumerator DelayedSpawnElite(RoomInstance room)
    {
        yield return new WaitForSeconds(0.5f); // 카메라 워프가 안착하는 약 0.5초 동안만 대기 후 스폰
        _isSpawnPending = false; // 지연 해제, 이제부터 Update 감지 가능
        SpawnEliteOnly(room);
    }

    public void OnRoomCleared(RoomInstance room)
    {

        // 인스펙터에 할당된 상자를 방 정중앙에 생성
        SpawnRoomRewardBox(room);

        // [추가] 엘리트 방 클리어 시 포탈 활성화 또는 생성
        if (portalObject != null)
        {
            if (portalObject.scene.IsValid())
            {
                portalObject.SetActive(true);
                Debug.Log("<color=purple>[EliteRoom]</color> Activated Portal object in the scene.");
            }
            else
            {
                Vector3 portalPos = room.transform.position + (Vector3)room.centerOffset;
                Instantiate(portalObject, portalPos, Quaternion.identity);
                Debug.Log("<color=purple>[EliteRoom]</color> Spawned Portal from Prefab at room center.");
            }
        }
        else
        {
            Vector3 portalPos = room.transform.position + (Vector3)room.centerOffset;
            GameObject portalObj = new GameObject("FloorProceedPortal");
            portalObj.transform.position = portalPos;
            portalObj.AddComponent<FloorProceedPortal>();
            Debug.Log("<color=purple>[EliteRoom]</color> Created FloorProceedPortal dynamically at room center since portalObject is null.");
        }

        // [추가] 플레이어 상태 업데이트 코드 추가
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Idle);

        OnEliteCombatClear?.Invoke();
        Debug.Log($"<color=red>[EliteRoom]</color> Elite Defeated!");

        BossHPBarUI.Instance?.Hide();
    }

private void SpawnEliteOnly(RoomInstance room)
    {
        Vector3 roomCenter = room.transform.position + (Vector3)room.centerOffset;

        // [버그 수정] 엘리트가 1마리(보스 등)인 경우 방 정중앙에 고정 스폰한다.
        // 예전엔 항상 방 안 무작위 위치에 스폰해서, 가끔 장식 지형이나 투기장 벽 바로 옆에 스폰되는
        // 바람에 스폰 직후 돌진 패턴이 뽑히면 코앞이 바로 벽이라 순식간에 경직까지 가버리는 문제가 있었다.
        // 엘리트가 여러 마리인 방(무리형)은 기존처럼 무작위 배치를 유지한다.
        if (eliteCount == 1)
        {
            SpawnEliteUnit(roomCenter);
        }
        else
        {
            float margin = 1.5f;
            float rangeX = (room.roomSize.x / 2f) - margin;
            float rangeY = (room.roomSize.y / 2f) - margin;

            for (int i = 0; i < eliteCount; i++)
            {
                Vector3 randPos = new Vector3(
                    Random.Range(-rangeX, rangeX),
                    Random.Range(-rangeY, rangeY),
                    0
                );
                SpawnEliteUnit(roomCenter + randPos);
            }
        }

        // [추가] 생성된 엘리트 중 무작위 2명에게 슈퍼아머 부여
        ApplySuperArmorToRandomEnemies(2);

        BindBossHPBar(_hpBarTargetData);   // GetComponent 제거
    }

    private void BindBossHPBar(EnemyMinionDataSO data)
    {
        if (BossHPBarUI.Instance == null) return;
        if (_activeEnemies == null || _activeEnemies.Count == 0) return;   // ← 이게 없으면 [0] 에서 또 터짐

        GameObject target = _activeEnemies[0];
        if (target == null) return;

        var stat = target.GetComponent<CharacterStat>()
                ?? target.GetComponentInChildren<CharacterStat>(true);
        if (stat == null) return;

        if (stat.Health == null) stat.Setup();
        if (stat.Health == null) return;

        string bossName = (data != null && !string.IsNullOrEmpty(data.minionName))
            ? data.minionName : target.name;

        BossHPBarUI.Instance.Show(stat.Health, bossName);
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

    /// <summary>
    /// 이 방에 나올 엘리트를 정한다. 우선순위:
    ///   1) forcedElite — 이 방 프리팹이 특정 엘리트 전용일 때
    ///   2) MapGenerator.FloorElite — 맵 생성 때 이 층에 확정된 엘리트 (방 프리팹도 그 기준으로 골라졌다)
    ///   3) 풀에서 무작위 — 위 둘이 다 없을 때의 옛 동작
    /// </summary>
    private EnemyMinionDataSO ResolveEliteData()
    {
        if (forcedElite != null) return forcedElite;

        var floorElite = MapGenerator.Instance != null ? MapGenerator.Instance.FloorElite : null;
        if (floorElite != null) return floorElite;

        if (_eliteEnemyPool.Count == 0) return null;
        return _eliteEnemyPool[Random.Range(0, _eliteEnemyPool.Count)];
    }

    private void SpawnEliteUnit(Vector3 position)
    {
        var data = ResolveEliteData();
        if (data == null)
        {
            Debug.LogWarning("[EliteRoom] 스폰할 엘리트를 못 정했다. (forcedElite / MapGenerator.FloorElite / 엘리트 풀이 전부 비어 있음)");
            return;
        }

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
        {
            GameObject eliteObj = GameManager.Instance.dataManager.CreateUnit(data, hit.position);
            if (eliteObj != null)
            {
                // [버그 수정] 예전엔 여기서 같은 오브젝트를 두 번 Add 해서 활성 적 수가 실제의 2배로 잡혔다.
                _activeEnemies.Add(eliteObj);
                if (_hpBarTargetData == null) _hpBarTargetData = data; // 첫 엘리트 = 체력바 주인
            }
        }
        else
        {
            Debug.LogWarning($"[EliteRoom] NavMesh SamplePosition (Elite) failed at {position}");
        }
    }

    public void RegisterActiveEnemy(GameObject enemy)
    {
        if (enemy != null && !_activeEnemies.Contains(enemy))
        {
            _activeEnemies.Add(enemy);
            Debug.Log($"<color=red>[EliteRoomEvent]</color> Added split enemy: {enemy.name}. Current Active Count: {_activeEnemies.Count}");
        }
    }

    private void SpawnRoomRewardBox(RoomInstance room)
    {
        if (rewardBoxPrefab == null)
        {
            // 상자가 없으면 예외 복구 조치로 즉시 UI 개방
            if (RewardManager.Instance != null)
                RewardManager.Instance.RequestClearReward(room.roomType);
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
            rewardBox.Initialize(room.roomType);
            Debug.Log($"<color=magenta>[EliteRoomEvent]</color> Spawned RoomRewardBox at {spawnPos}");
        }
        else
        {
            Debug.LogWarning("[EliteRoomEvent] Spawned object lacks RoomRewardBox script. Triggering reward instantly.");
            if (RewardManager.Instance != null)
                RewardManager.Instance.RequestClearReward(room.roomType);
        }
    }
}
