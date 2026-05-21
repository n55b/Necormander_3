using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// 엘리트 전투 방의 이벤트를 담당합니다. (강력한 엘리트 몹 1마리 + 부하들)
/// </summary>
public class EliteRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Elite Settings")]
    [SerializeField] private int eliteCount = 1;

    [Header("Minion Settings")]
    [SerializeField] private int groupsCount = 2;
    [SerializeField] private int enemiesPerGroup = 2;
    [SerializeField] private float spawnDistanceFromCenter = 6.0f;

    [Header("Unity Events")]
    public UnityEvent OnEliteCombatStart;
    public UnityEvent OnEliteCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private List<MinionDataSO> _normalEnemyPool = new List<MinionDataSO>();
    private List<MinionDataSO> _eliteEnemyPool = new List<MinionDataSO>(); // 기존 bossPool을 엘리트로 사용
    private bool _isBattleActive = false;
    private RoomInstance _cachedRoom;

    private void Start()
    {
        if (GameManager.Instance != null && GameManager.Instance.dataManager != null)
        {
            var rawList = GameManager.Instance.dataManager.ENEMY_MINION_DATA;
            if (rawList != null)
            {
                foreach (var data in rawList)
                {
                    if (data.isBoss) _eliteEnemyPool.Add(data);
                    else if (data.canSpawnRandomly) _normalEnemyPool.Add(data);
                }
            }
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

        SpawnEliteCombat(room);

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

        OnEliteCombatClear?.Invoke();
        Debug.Log($"<color=red>[EliteRoom]</color> Elite Defeated!");
    }

    private void SpawnEliteCombat(RoomInstance room)
    {
        // 1. 엘리트 소환 (방 중앙 부근)
        for (int i = 0; i < eliteCount; i++)
        {
            SpawnEliteUnit(room.transform.position + (Vector3)Random.insideUnitCircle * 2f);
        }

        // 2. 호위 병력 소환
        for (int i = 0; i < groupsCount; i++)
        {
            Vector2 randPos = Random.insideUnitCircle * spawnDistanceFromCenter;
            SpawnGroup(room.transform.position + (Vector3)randPos);
        }
    }

    private void SpawnEliteUnit(Vector3 position)
    {
        if (_eliteEnemyPool.Count == 0) return;
        MinionDataSO data = _eliteEnemyPool[Random.Range(0, _eliteEnemyPool.Count)];
        
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
        {
            GameObject eliteObj = GameManager.Instance.dataManager.CreateUnit(data, hit.position);
            if (eliteObj != null) _activeEnemies.Add(eliteObj);
        }
    }

    private void SpawnGroup(Vector3 center)
    {
        for (int i = 0; i < enemiesPerGroup; i++)
        {
            Vector2 offset = Random.insideUnitCircle * 2f;
            Vector3 spawnPos = center + (Vector3)offset;

            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                MinionDataSO data = GetRandomNormalEnemyData();
                GameObject enemy = GameManager.Instance.dataManager.CreateUnit(data, hit.position);
                if (enemy != null) _activeEnemies.Add(enemy);
            }
        }
    }

    private MinionDataSO GetRandomNormalEnemyData()
    {
        if (_normalEnemyPool.Count == 0) return null;
        return _normalEnemyPool[Random.Range(0, _normalEnemyPool.Count)];
    }
}
