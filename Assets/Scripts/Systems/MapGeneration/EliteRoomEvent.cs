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
    [SerializeField] private GameObject portalObject;

    [Header("Reward Box Settings")]
    [Tooltip("방 클리어 시 한가운데에 생성할 보상 상자 프리팹을 연결해 주세요.")]
    [SerializeField] private GameObject rewardBoxPrefab;

    [Header("Unity Events")]
    public UnityEvent OnEliteCombatStart;
    public UnityEvent OnEliteCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private List<EnemyMinionDataSO> _eliteEnemyPool = new List<EnemyMinionDataSO>(); 
    private bool _isBattleActive = false;
    private bool _isSpawnPending = false; // 2.5초 지연 소환 대기 플래그
    private RoomInstance _cachedRoom;

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
                    if (data.isElite) _eliteEnemyPool.Add(data);
                }
            }
            
            // 2. 새롭게 분리된 엘리트 전용 목록에서도 유니크하게 엘리트 수집
            var eliteList = GameManager.Instance.dataManager.ELITE_MINION_DATA;
            if (eliteList != null)
            {
                foreach (var data in eliteList)
                {
                    if (data.isElite && !_eliteEnemyPool.Contains(data)) _eliteEnemyPool.Add(data);
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

        if (GemTreeUI.Instance != null && GemTreeUI.Instance.IsOpen) GemTreeUI.Instance.Toggle();
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
    }

    private void SpawnEliteOnly(RoomInstance room)
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
            SpawnEliteUnit(room.transform.position + (Vector3)room.centerOffset + randPos);
        }

        // [추가] 생성된 엘리트 중 무작위 2명에게 슈퍼아머 부여
        ApplySuperArmorToRandomEnemies(2);
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

    private void SpawnEliteUnit(Vector3 position)
    {
        if (_eliteEnemyPool.Count == 0) 
        {
            Debug.LogWarning("[EliteRoom] Elite Enemy Pool is empty!");
            return;
        }

        var data = _eliteEnemyPool[Random.Range(0, _eliteEnemyPool.Count)];
        
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
        {
            GameObject eliteObj = GameManager.Instance.dataManager.CreateUnit(data, hit.position);
            if (eliteObj != null) _activeEnemies.Add(eliteObj);
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

        Vector3 spawnPos = room.transform.position + (Vector3)room.centerOffset;
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
