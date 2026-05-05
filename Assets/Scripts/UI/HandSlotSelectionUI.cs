using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미니언이나 능력을 선택했을 때, 10개의 핸드 슬롯 중 어디에 넣을지 결정하는 전용 UI입니다.
/// 상단에는 선택한 보상 정보를, 하단에는 10개의 핸드 슬롯 상태를 보여줍니다.
/// </summary>
public class HandSlotSelectionUI : MonoBehaviour
{
    [Header("UI Containers")]
    [SerializeField] private GameObject panel;
    [SerializeField] private RewardCard selectedRewardCard; // 상단에 띄울 보상 정보
    [SerializeField] private Transform slotContainer; // 슬롯들이 담길 부모 (Grid Layout Group이 있는 곳)

    [Header("Prefab Settings")]
    [SerializeField] private HandSlotSelectionItem slotItemPrefab; // 슬롯 프리팹

    // 내부적으로 관리할 리스트
    private List<HandSlotSelectionItem> _spawnedItems = new List<HandSlotSelectionItem>();
    private RewardCandidate _pendingCandidate;

    private void Awake()
    {
        // [자동 생성] 10개의 슬롯을 미리 생성해둡니다.
        if (slotItemPrefab != null && slotContainer != null && _spawnedItems.Count == 0)
        {
            for (int i = 0; i < 10; i++)
            {
                var item = Instantiate(slotItemPrefab, slotContainer);
                _spawnedItems.Add(item);
            }
        }
        Hide();
    }

    /// <summary>
    /// 슬롯 선택 UI를 활성화합니다.
    /// </summary>
    public void Show(RewardCandidate candidate)
    {
        _pendingCandidate = candidate;
        panel.SetActive(true);

        // 상단에 선택한 보상 정보 표시
        if (selectedRewardCard != null)
        {
            selectedRewardCard.Setup(candidate, -1);
        }

        // 하단 슬롯 상태 갱신
        RefreshSlots();
    }

    public void RefreshSlots()
    {
        var inven = GameManager.Instance.inventoryManager;
        for (int i = 0; i < _spawnedItems.Count; i++)
        {
            if (i < inven.Slots.Count)
            {
                _spawnedItems[i].Setup(i, inven.Slots[i], this);
            }
        }
    }

    public void OnSlotSelected(int index)
    {
        // 실제 인벤토리에 적용
        var inven = GameManager.Instance.inventoryManager;
        bool success = false;

        switch (_pendingCandidate.category)
        {
            case RewardCategory.Minion:
                success = inven.EquipLineage(index, (MinionLineageSO)_pendingCandidate.rawData);
                break;
            case RewardCategory.Ability:
                success = inven.EquipThrowAbility(index, (ThrowAbilitySO)_pendingCandidate.rawData);
                break;
        }

        if (success)
        {
            Debug.Log($"<color=green>[HandSlotUI]</color> Equipped to slot {index}");
            GameManager.Instance.squadSpawner.RefreshFullSquad();
            Hide();
            
            // 보상 시퀀스 재개
            RewardManager.Instance.NotifyHandSlotSelectionComplete();
        }
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
