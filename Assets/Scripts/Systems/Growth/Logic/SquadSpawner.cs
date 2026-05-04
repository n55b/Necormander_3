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

        // 10개 슬롯을 순회하며 장착된 유닛 소환
        for (int i = 0; i < _inven.Slots.Count; i++)
        {
            var slot = _inven.Slots[i];
            
            // [수정] 능력이 아닌 미니언이 장착된 슬롯만 처리
            if (slot.EquippedLineage != null)
            {
                MinionDataSO currentData = slot.GetCurrentMinionData();
                if (currentData != null)
                {
                    Debug.Log($"<color=cyan>[SquadSpawner]</color> Slot {i}: Found {currentData.minionName} (Quantity: {slot.Quantity})");
                    for (int j = 0; j < slot.Quantity; j++)
                    {
                        SpawnUnitFromSlot(currentData);
                    }
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[SquadSpawner]</color> Slot {i} has unit info but data is null!");
                }
            }
        }
    }

    public void SpawnUnitFromSlot(MinionDataSO data)
    {
        if (data == null) return;

        // 플레이어 주변 소환 위치 확보
        Vector3 spawnPos = transform.position; 
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && pc.SUMCONTROLLER != null)
        {
            var positions = pc.SUMCONTROLLER.GetSummonPositions2D(1, 3f);
            if (positions.Count > 0) spawnPos = positions[0];
        }

        // AllyManager를 통해 실제 소환 및 관리 등록
        _allyManager.SpawnAlly(data, spawnPos);
        Debug.Log($"<color=green>[SquadSpawner]</color> 자동 소환: {data.minionName}");
    }
}
