using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// InventoryManager의 슬롯 정보를 관찰하여 실제 부대(Squad)를 필드에 유지시키는 클래스입니다.
/// </summary>
public class SquadSpawner : MonoBehaviour
{
    private AllyManager _allyManager;
    private InventoryManager _inven;

    public void Initialize(InventoryManager inven, AllyManager allyManager)
    {
        _inven = inven;
        _allyManager = allyManager;
        
        Debug.Log("<color=cyan>[SquadSpawner]</color> Initialized.");
    }

    /// <summary>
    /// 현재 슬롯 데이터를 바탕으로 부대를 완전히 새로 고칩니다. (게임 시작 혹은 큰 변경 시)
    /// </summary>
    public void RefreshFullSquad()
    {
        if (_inven == null || _allyManager == null) return;

        Debug.Log($"<color=cyan>[SquadSpawner]</color> Refreshing Full Squad. Slot Count: {_inven.Slots.Count}");

        // [추가] 인벤토리 목록에 맞춰 새로 소환하기 전에 필드의 모든 유닛을 먼저 제거합니다.
        _allyManager.ClearAll();

        // 1. 소환해야 할 총 마릿수 계산 및 데이터 수집
        List<MinionDataSO> spawnList = new List<MinionDataSO>();
        for (int i = 0; i < _inven.Slots.Count; i++)
        {
            var slot = _inven.Slots[i];
            if (slot.EquippedLineage != null)
            {
                MinionDataSO currentData = slot.GetCurrentMinionData();
                if (currentData != null)
                {
                    for (int j = 0; j < slot.Quantity; j++)
                    {
                        spawnList.Add(currentData);
                    }
                }
            }
        }

        if (spawnList.Count == 0) return;

        // 2. 한 번에 모든 소환 위치 확보 (뭉침 방지)
        List<Vector2> spawnPositions = new List<Vector2>();
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.SUMCONTROLLER != null)
        {
            // 마릿수에 따라 탐색 반경을 유동적으로 조절 (최소 3m)
            float radius = Mathf.Max(3f, Mathf.Sqrt(spawnList.Count) * 1.5f);
            spawnPositions = pc.SUMCONTROLLER.GetSummonPositions2D(spawnList.Count, radius);
        }

        // 3. 확보된 위치에 순차적으로 소환
        for (int i = 0; i < spawnList.Count; i++)
        {
            Vector3 pos = (i < spawnPositions.Count) ? (Vector3)spawnPositions[i] : transform.position;
            _allyManager.SpawnAlly(spawnList[i], pos);
        }

        Debug.Log($"<color=cyan>[SquadSpawner]</color> Full Squad Spawned: {spawnList.Count} units spread across {spawnPositions.Count} positions.");
    }

    public void SpawnUnitFromSlot(MinionDataSO data)
    {
        if (data == null) return;

        // 플레이어 주변 소환 위치 확보 (낱개 소환 시에도 약간의 랜덤성 부여)
        Vector3 spawnPos = transform.position; 
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.SUMCONTROLLER != null)
        {
            var positions = pc.SUMCONTROLLER.GetSummonPositions2D(1, 3f);
            if (positions.Count > 0)
            {
                // [개선] 낱개 소환 시에도 겹침을 방지하기 위해 약간의 랜덤 오프셋 추가
                spawnPos = (Vector3)positions[0] + (Vector3)Random.insideUnitCircle * 0.5f;
            }
        }

        // AllyManager를 통해 실제 소환 및 관리 등록
        _allyManager.SpawnAlly(data, spawnPos);
    }
}
