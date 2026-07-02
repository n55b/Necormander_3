using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보석을 선택했을 때 어떤 미니언의 어떤 슬롯에 장착할지 결정하는 UI입니다.
/// </summary>
public class GemSlotSelectionUI : MonoBehaviour
{
    [Header("UI Containers")]
    [SerializeField] private GameObject panel;
    [SerializeField] private RewardCard selectedGemCard;
    [SerializeField] private Transform container; // Grid Layout Group이 있는 부모

    [Header("Prefab Settings")]
    [SerializeField] private GemSlotSelectionItem itemPrefab;

    private List<GemSlotSelectionItem> _spawnedItems = new List<GemSlotSelectionItem>();
    private RewardCandidate _pendingCandidate;

    private void Awake()
    {
        Hide();
    }

    /// <summary>
    /// 보석 장착 UI를 활성화합니다.
    /// </summary>
    public void Show(RewardCandidate candidate)
    {
        if(UIPopUpManager.Instance.IsPopUpActive)  return; // 이미 팝업이 활성화되어 있으면 새로 띄우지 않음

        _pendingCandidate = candidate;
        UIEventBus.NotifyOpen("GemSlot");
        
        UIPopUpManager.Instance.PopUpUI(panel);

        // 상단에 선택한 보석 정보 표시
        if (selectedGemCard != null)
        {
            selectedGemCard.Setup(candidate, -1);
        }

        // 하단 미니언 리스트 갱신
        RefreshMinionList();
    }

    public void RefreshMinionList()
    {
        // 1. 기존 항목 제거
        foreach (var item in _spawnedItems)
        {
            if (item != null) Destroy(item.gameObject);
        }
        _spawnedItems.Clear();

        var inven = InventoryManager.Instance;
        var registry = GameManager.Instance.dataManager.GET_GROWTH_REGISTRY();
        
        // 2. 현재 부대 슬롯에 있는 유니크한 직업들 추출
        HashSet<CommandData> uniqueJobs = new HashSet<CommandData>();
        foreach (var slot in inven.Slots)
        {
            if (slot.EquippedMinion != null)
                uniqueJobs.Add(slot.EquippedMinion.minionType);
        }

        // 3. 추출된 직업 개수만큼 항목 생성 (최대 6종)
        int count = 0;
        foreach (var job in uniqueJobs)
        {
            if (count >= 6) break;

            var item = Instantiate(itemPrefab, container);
            var minion = registry.minionDatas.Find(m => m.minionType == job);
            
            item.Setup(job, minion, this);
            _spawnedItems.Add(item);
            count++;
        }

        // 레이아웃 강제 갱신 (Grid Layout Group 대응)
        LayoutRebuilder.ForceRebuildLayoutImmediate(container as RectTransform);
    }

    public void OnGemSlotSelected(CommandData job, int gemSlotIndex)
    {
        if (_pendingCandidate.rawData is GemSO gem)
        {
            // 새로운 보석 트리 시스템에서는 보석을 특정 슬롯에 직접 장착하는 것이 아니라,
            // AvailableGemInstances 목록에 추가된 후, 플레이어가 직접 트리의 빈 슬롯을 선택하여 장착해야 합니다.
            // 이 UI는 향후 트리 구조에 맞는 장착 UI로 리팩토링 되어야 합니다.
            Debug.LogWarning($"<color=orange>[GemUI]</color> Gem {gem.itemName} was selected. Socketing logic needs to be implemented for the new Gem Tree System.");
            Hide();
            // RewardManager.Instance.NotifyGemSelectionComplete(); // 삭제됨
        }
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        UIEventBus.NotifyClose("GemSlot");
        
        UIPopUpManager.Instance?.ClosePopUpUI();
    }
}
