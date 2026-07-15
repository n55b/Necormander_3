using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// 보스 전투 방의 이벤트를 담당합니다.
/// 방 진입 시 문을 닫고, 설정된 보스를 소환하며 클리어 시 포탈을 엽니다.
/// </summary>
public class BossRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Boss Settings")]
    [Tooltip("비워두면 엘리트 풀에서 랜덤으로 보스를 소환합니다.")]
    [SerializeField] private MinionDataSO specificBossData;
    [SerializeField] private GameObject portalObject;

    [Header("Reward Box Settings")]
    [Tooltip("방 클리어 시 한가운데에 생성할 보상 상자 프리팹을 연결해 주세요.")]
    [SerializeField] private GameObject rewardBoxPrefab;

    [Header("Unity Events")]
    public UnityEvent OnBossCombatStart;
    public UnityEvent OnBossCombatClear;

    private GameObject _activeBoss;
    private List<MinionDataSO> _bossEnemyPool = new List<MinionDataSO>();
    private List<GameObject> _activeEnemies = new List<GameObject>(); // 분열 등으로 추가된 적(보스 외)
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

        if (specificBossData == null && GameManager.Instance != null && GameManager.Instance.dataManager != null)
        {
            var rawList = GameManager.Instance.dataManager.BOSS_MINION_DATA;
            if (rawList != null)
            {
                foreach (var data in rawList)
                {
                    _bossEnemyPool.Add(data);
                }
            }
        }
    }

    private void Update()
    {
        // 지연 스폰 중에는 보스가 소환 안 된 상태이므로 즉시 클리어되는 현상 방지
        if (!_isBattleActive || _isSpawnPending) return;

        _activeEnemies.RemoveAll(e => e == null);
        if (_activeBoss == null && _activeEnemies.Count == 0)
        {
            _isBattleActive = false;
            _cachedRoom.MarkCleared();
        }
    }

    // 분열 적 등을 방 클리어 판정에 귀속시킨다. (SlimeAIPatternSO/DualSplitAIPatternSO 에서 호출)
    public void RegisterActiveEnemy(GameObject enemy)
    {
        if (enemy != null && !_activeEnemies.Contains(enemy)) _activeEnemies.Add(enemy);
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
        FindFirstObjectByType<ThrowController>()?.ForceClear();

        // 1초 후 보스가 소환되도록 텀(Term) 연출 구현
        StartCoroutine(DelayedSpawnBoss(room));

        // 플레이어 상태 업데이트 (전투 중)
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Battle);

        OnBossCombatStart?.Invoke();
        Debug.Log($"<color=red>[BossRoom]</color> Warning! Boss Encounter in {room.gameObject.name}");
    }

    private IEnumerator DelayedSpawnBoss(RoomInstance room)
    {
        yield return new WaitForSeconds(0.5f); // 카메라 워프가 안착하는 약 0.5초 동안만 대기 후 스폰
        _isSpawnPending = false; // 지연 해제, 이제부터 Update 감지 가능
        SpawnBoss(room);
    }

    public void OnRoomCleared(RoomInstance room)
    {
        FindFirstObjectByType<ThrowController>()?.ForceClear();

        // 인스펙터에 할당된 상자를 방 정중앙에 생성
        SpawnRoomRewardBox(room);

        // 보스 방 클리어 시 포탈 활성화 또는 생성
        if (portalObject != null)
        {
            if (portalObject.scene.IsValid())
            {
                portalObject.SetActive(true);
            }
            else
            {
                Vector3 portalPos = room.transform.position + (Vector3)room.centerOffset;
                Instantiate(portalObject, portalPos, Quaternion.identity);
            }
        }
        else
        {
            Vector3 portalPos = room.transform.position + (Vector3)room.centerOffset;
            GameObject portalObj = new GameObject("FloorProceedPortal");
            portalObj.transform.position = portalPos;
            portalObj.AddComponent<FloorProceedPortal>();
        }

        // 플레이어 상태 업데이트 (대기)
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Idle);

        OnBossCombatClear?.Invoke();
        Debug.Log($"<color=red>[BossRoom]</color> Boss Defeated!");
    }

    private void SpawnBoss(RoomInstance room)
    {
        MinionDataSO dataToSpawn = specificBossData;
        
        // 특정 보스가 할당 안 된 경우 랜덤 풀에서 가져옴
        if (dataToSpawn == null && _bossEnemyPool.Count > 0)
        {
            dataToSpawn = _bossEnemyPool[Random.Range(0, _bossEnemyPool.Count)];
        }

        if (dataToSpawn == null)
        {
            Debug.LogError("[BossRoom] Spawn failed: No Boss Data assigned or found in pool!");
            return;
        }

        Vector3 spawnPos = room.transform.position + (Vector3)room.centerOffset;
        
        if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
        {
            _activeBoss = GameManager.Instance.dataManager.CreateUnit(dataToSpawn, hit.position);
        }
        else
        {
            // 네비메쉬 위가 아니더라도 강제 소환 (중앙)
            _activeBoss = GameManager.Instance.dataManager.CreateUnit(dataToSpawn, spawnPos);
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
            Debug.Log($"<color=magenta>[BossRoomEvent]</color> Spawned RoomRewardBox at {spawnPos}");
        }
        else
        {
            Debug.LogWarning("[BossRoomEvent] Spawned object lacks RoomRewardBox script. Triggering reward instantly.");
            if (RewardManager.Instance != null)
                RewardManager.Instance.RequestClearReward(room.roomType);
        }
    }
}
