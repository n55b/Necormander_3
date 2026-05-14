using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미니언이나 능력을 선택했을 때, 10개의 핸드 슬롯 중 어디에 넣을지 결정하는 전용 UI입니다.
/// 상단에는 선택한 보상 정보를, 하단에는 10개의 핸드 슬롯 상태를 보여줍니다.
/// </summary>
public class HandSlotSelectionUI : MonoBehaviour
{
    public static HandSlotSelectionUI Instance; // [추가] 싱글톤

    [Header("UI Containers")]
    [SerializeField] private GameObject panel;
    [SerializeField] private RewardCard selectedRewardCard; // 상단에 띄울 보상 정보
    [SerializeField] private Transform slotContainer; // 슬롯들이 담길 부모 (Grid Layout Group이 있는 곳)

    [Header("Prefab Settings")]
    [SerializeField] private HandSlotSelectionItem slotItemPrefab; // 슬롯 프리팹
    [SerializeField] private GameObject tooltipPrefab; // [추가] 툴팁 프리팹

    // 내부적으로 관리할 리스트
    private List<HandSlotSelectionItem> _spawnedItems = new List<HandSlotSelectionItem>();
    private RewardCandidate _pendingCandidate;
    private bool _isReadOnly = false; // [추가] 조회 전용 모드 플래그

    private void Awake()
    {
        Instance = this; // [추가]

        // [추가] 툴팁 프리팹 소환
        if (tooltipPrefab != null && CommonTooltipUI.Instance == null)
        {
            Instantiate(tooltipPrefab, transform.parent); // Canvas 하위에 생성
        }

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
    /// [추가] 탭 키로 UI를 켜고 끕니다. (조회 전용)
    /// </summary>
    public void ToggleReadOnly()
    {
        if (panel.activeSelf && !_isReadOnly) return; // 만약 보상 획득 시점의 장착 모드라면 무시

        if (panel.activeSelf)
        {
            Hide();
        }
        else
        {
            _isReadOnly = true;
            _pendingCandidate = default; // [수정] 구조체는 null 대신 default 사용
            if (selectedRewardCard != null) selectedRewardCard.gameObject.SetActive(false); // 보상 정보 숨김
            panel.SetActive(true);
            RefreshSlots();
        }
    }

    /// <summary>
    /// 슬롯 선택 UI를 활성화합니다. (보상 획득 시 장착 모드)
    /// </summary>
    public void Show(RewardCandidate candidate)
    {
        _isReadOnly = false;
        _pendingCandidate = candidate;
        panel.SetActive(true);

        // 상단에 선택한 보상 정보 표시
        if (selectedRewardCard != null)
        {
            selectedRewardCard.gameObject.SetActive(true);
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
                _spawnedItems[i].Setup(i, inven.Slots[i], this, _isReadOnly);
            }
        }
    }

    public void OnSlotSelected(int index)
    {
        if (_isReadOnly) return; // [추가] 조회 모드에서는 작동 안함

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
            // [사용자 요청] 여기서 즉시 재소환하지 않음 (다음 전투 시작 시 소환)
            // GameManager.Instance.squadSpawner.RefreshFullSquad();
            Hide();
            
            // 보상 시퀀스 재개
            RewardManager.Instance.NotifyHandSlotSelectionComplete();
        }
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        // [추가] UI 닫을 때 툴팁 강제 제거
        if (CommonTooltipUI.Instance != null) CommonTooltipUI.Instance.Hide();
    }
}
