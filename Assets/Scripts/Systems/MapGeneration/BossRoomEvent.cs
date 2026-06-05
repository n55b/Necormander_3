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

    [Header("Unity Events")]
    public UnityEvent OnBossCombatStart;
    public UnityEvent OnBossCombatClear;

    private GameObject _activeBoss;
    private List<MinionDataSO> _bossEnemyPool = new List<MinionDataSO>(); 
    private bool _isBattleActive = false;
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
        if (!_isBattleActive) return;

        if (_activeBoss == null)
        {
            _isBattleActive = false;
            _cachedRoom.MarkCleared();
        }
    }

    public void OnPlayerEnter(RoomInstance room)
    {
        if (_isBattleActive) return;
        
        _cachedRoom = room;
        _isBattleActive = true;

        room.SetDoorsOpen(false); // 문 폐쇄

        if (GemTreeUI.Instance != null && GemTreeUI.Instance.IsOpen) GemTreeUI.Instance.Toggle();
        if (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen) HandSlotSelectionUI.Instance.Hide();
        if (GameManager.Instance?.squadSpawner != null) GameManager.Instance.squadSpawner.RefreshFullSquad();

        SpawnBoss(room);

        // 플레이어 상태 업데이트 (전투 중)
        if(GameManager.Instance?.PLAYERCONTROLLER != null) GameManager.Instance.PLAYERCONTROLLER.ChangeState(PlayerStates.Battle);

        OnBossCombatStart?.Invoke();
        Debug.Log($"<color=red>[BossRoom]</color> Warning! Boss Encounter in {room.gameObject.name}");
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
}
