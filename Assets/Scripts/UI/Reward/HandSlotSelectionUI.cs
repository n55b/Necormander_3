using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미니언이나 능력을 선택했을 때, 10개의 핸드 슬롯 중 어디에 넣을지 결정하는 전용 UI입니다.
/// 상단에는 선택한 보상 정보를, 하단에는 10개의 핸드 슬롯 상태를 보여줍니다.
/// </summary>
public class HandSlotSelectionUI : Singleton<HandSlotSelectionUI>
{

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

    public bool IsOpen => panel != null && panel.activeSelf;

    protected override void OnAwake()
    {
        // [추가] 툴팁 프리팹 소환
        if (tooltipPrefab != null && CommonTooltipUI.Instance == null)
        {
            Instantiate(tooltipPrefab, transform.parent); // Canvas 하위에 생성
        }

        // [자동 생성] 소환수 슬롯 수(메인 1 + 서브 1)만큼 미리 생성해둡니다.
        if (slotItemPrefab != null && slotContainer != null && _spawnedItems.Count == 0)
        {
            for (int i = 0; i < InventoryManager.SLOT_COUNT; i++)
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
            if(UIPopUpManager.Instance.IsPopUpActive || UIPopUpManager.Instance.IsOnBattle) return;

            UIPopUpManager.Instance.PopUpUI(gameObject); // 팝업 매니저에 상태 전달

            _isReadOnly = true;
            _pendingCandidate = default; // [수정] 구조체는 null 대신 default 사용
            if (selectedRewardCard != null) selectedRewardCard.gameObject.SetActive(false); // 보상 정보 숨김
            UIEventBus.NotifyOpen("HandSlot");
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
        UIEventBus.NotifyOpen("HandSlot");

        UIPopUpManager.Instance.ForcePopUpUI(gameObject); // 팝업 매니저에 상태 전달

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
                _spawnedItems[i].Setup(i, inven.Slots[i], this, _isReadOnly, SlotAccepts(i));
            }
        }
    }

    /// <summary>
    /// 지금 고른 보상이 이 슬롯에 들어갈 수 있는가.
    ///
    /// 모델(InventoryManager.EquipMinion)은 이미 역할에 맞는 슬롯으로 되돌려보내고 있었다.
    /// 문제는 UI 가 두 칸을 똑같이 눌리게 해놓고, 심지어 되돌려보내기 *전* 인덱스로 로그를 찍어서
    /// "메인을 두 칸에 낄 수 있다"는 잘못된 그림을 보여준 것이다. 실제로는 늘 같은 칸에
    /// 덮어써지고 있었다. 그래서 여기서 못 누르게 막는다 — 모델은 그대로 둔다.
    /// </summary>
    private bool SlotAccepts(int slotIndex)
    {
        if (_isReadOnly) return false;

        switch (_pendingCandidate.category)
        {
            case RewardCategory.Minion:
                // 소환수는 자기 역할 슬롯에만. 타입이 곧 역할이다.
                return InventoryManager.SlotIndexOf(_pendingCandidate.rawData as MinionDataSO) == slotIndex;
            default:
                return true;
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
                success = inven.EquipMinion(index, (MinionDataSO)_pendingCandidate.rawData);
                break;
        }

        if (success)
        {
            // 모델이 역할 슬롯으로 되돌려보낼 수 있으므로, 요청한 index 가 아니라 실제로 들어간 칸을 찍는다.
            var equipped = _pendingCandidate.rawData as MinionDataSO;
            int actual = equipped != null ? InventoryManager.SlotIndexOf(equipped) : index;
            Debug.Log($"<color=green>[HandSlotUI]</color> Equipped to slot {actual}");
            Hide();

            // 보상 시퀀스 재개 (마을 디버그 메뉴 등 RewardManager가 없는 씬에서 호출될 수 있어 null-safe)
            RewardManager.Instance?.NotifyHandSlotSelectionComplete();
        }
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        UIEventBus.NotifyClose("HandSlot");

        // OnAwake의 초기 Hide() 등, 매니저가 아직 준비되기 전(마을 씬에 픽커를 추가한 경우 등)에 불릴 수 있어 null-safe.
        if (UIPopUpManager.Instance != null) UIPopUpManager.Instance.ClosePopUpUI();
        // [추가] UI 닫을 때 툴팁 강제 제거
        if (CommonTooltipUI.Instance != null) CommonTooltipUI.Instance.Hide();
    }
}
