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

    [Header("Unity Events")]
    public UnityEvent OnEliteCombatStart;
    public UnityEvent OnEliteCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private List<MinionDataSO> _eliteEnemyPool = new List<MinionDataSO>(); 
    private bool _isBattleActive = false;
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
            var rawList = GameManager.Instance.dataManager.ENEMY_MINION_DATA;
            if (rawList != null)
            {
                foreach (var data in rawList)
                {
                    // 엘리트 풀에는 isBoss가 true인 데이터만 수집
                    if (data.isBoss) _eliteEnemyPool.Add(data);
                }
            }
            Debug.Log($"<color=red>[EliteRoom]</color> Pool Initialized. Elites: {_eliteEnemyPool.Count} in {gameObject.name}");
        }
    }

    private void Update()
    {
        if (!_isBattleActive) return;

        _activeEnemies.RemoveAll(item => item == null);
        if (_activeEnemies.Count == 0)
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

        // [수정] 부하들 없이 엘리트만 소환
        SpawnEliteOnly(room);

        OnEliteCombatStart?.Invoke();
        Debug.Log($"<color=red>[EliteRoom]</color> Warning! Elite Encounter in {room.gameObject.name}");
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
    }

    private void SpawnEliteUnit(Vector3 position)
    {
        if (_eliteEnemyPool.Count == 0) 
        {
            Debug.LogWarning("[EliteRoom] Elite Enemy Pool is empty!");
            return;
        }

        MinionDataSO data = _eliteEnemyPool[Random.Range(0, _eliteEnemyPool.Count)];
        
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
}
