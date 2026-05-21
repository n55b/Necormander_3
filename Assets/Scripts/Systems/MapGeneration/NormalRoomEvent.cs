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
    [SerializeField] private int groupsCount = 3;
    [SerializeField] private int enemiesPerGroup = 3;
    [SerializeField] private float spawnDistanceFromCenter = 5.0f;
    [SerializeField] private float groupSpread = 1.5f;

    [Header("Unity Events")]
    public UnityEvent OnCombatStart;
    public UnityEvent OnCombatClear;

    private List<GameObject> _activeEnemies = new List<GameObject>();
    private List<MinionDataSO> _normalEnemyPool = new List<MinionDataSO>();
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
                    if (!data.isBoss && data.canSpawnRandomly) _normalEnemyPool.Add(data);
                }
            }
            Debug.Log($"<color=white>[NormalRoom]</color> Pool Initialized. Normal Enemies: {_normalEnemyPool.Count} in {gameObject.name}");
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

        room.SetDoorsOpen(false);

        if (GemTreeUI.Instance != null && GemTreeUI.Instance.IsOpen) GemTreeUI.Instance.Toggle();
        if (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen) HandSlotSelectionUI.Instance.Hide();
        if (GameManager.Instance?.squadSpawner != null) GameManager.Instance.squadSpawner.RefreshFullSquad();

        SpawnWaves(room);

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

        OnCombatClear?.Invoke();
        Debug.Log($"<color=green>[NormalRoom]</color> Cleared!");
    }

    private void SpawnWaves(RoomInstance room)
    {
        for (int i = 0; i < groupsCount; i++)
        {
            Vector2 randPos = Random.insideUnitCircle * spawnDistanceFromCenter;
            SpawnGroup(room.transform.position + (Vector3)randPos);
        }
    }

    private void SpawnGroup(Vector3 center)
    {
        for (int i = 0; i < enemiesPerGroup; i++)
        {
            Vector2 offset = Random.insideUnitCircle * groupSpread;
            Vector3 spawnPos = center + (Vector3)offset;

            // [수정] 샘플링 범위를 늘리고 실패 시 로그 출력
            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
            {
                MinionDataSO data = GetRandomEnemyData();
                if (data != null)
                {
                    GameObject enemy = GameManager.Instance.dataManager.CreateUnit(data, hit.position);
                    if (enemy != null) _activeEnemies.Add(enemy);
                }
                else
                {
                    Debug.LogWarning("[NormalRoom] RandomEnemyData is null! Check pool.");
                }
            }
            else
            {
                Debug.LogWarning($"[NormalRoom] NavMesh SamplePosition failed at {spawnPos}");
            }
        }
    }

    private MinionDataSO GetRandomEnemyData()
    {
        if (_normalEnemyPool.Count == 0) return null;
        return _normalEnemyPool[Random.Range(0, _normalEnemyPool.Count)];
    }
}
