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
        _pendingCandidate = candidate;
        panel.SetActive(true);

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
            if (slot.EquippedLineage != null)
                uniqueJobs.Add(slot.EquippedLineage.jobType);
        }

        // 3. 추출된 직업 개수만큼 항목 생성 (최대 6종)
        int count = 0;
        foreach (var job in uniqueJobs)
        {
            if (count >= 6) break;

            var item = Instantiate(itemPrefab, container);
            var lineage = registry.minionLineages.Find(l => l.jobType == job);
            
            item.Setup(job, lineage, this);
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
            // 실제 인벤토리에 보석 장착 (기존 보석 덮어쓰기 지원)
            bool success = InventoryManager.Instance.EquipGem(job, gem, gemSlotIndex);
            
            if (success)
            {
                Debug.Log($"<color=green>[GemUI]</color> Gem {gem.itemName} equipped to {job} slot {gemSlotIndex + 1}");
                Hide();
                
                // 보상 매니저에게 완료 알림 -> 다음 보상(보물 등)으로 진행
                RewardManager.Instance.NotifyGemSelectionComplete();
            }
        }
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
