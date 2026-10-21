using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 강화 전용 상점 NPC. 다음 장비를 카드로 선택하고, 상점당 한 번만 강화한다.
///
/// 상호작용은 PlayerController.CheckForInteractable 이 잡는다. 그래서 이 컴포넌트가 붙은
/// 오브젝트에 Interactable(13) 레이어의 트리거 콜라이더가 반드시 같이 있어야 한다 —
/// TryGetComponent 로 찾기 때문에 콜라이더가 자식에 있으면 안 잡힌다.
/// </summary>
public class EnhanceShopNPC : NPCBase
{
    [Header("말풍선 (SellItem 과 같은 방식)")]
    [Tooltip("평소 꺼둘 월드 스페이스 Canvas. 플레이어가 다가오면 켜진다.")]
    [SerializeField] private GameObject bubbleCanvas;
    [Tooltip("Assets/Prefabs/UI/Shop/ToolTip.prefab — Name/Price 두 줄짜리 말풍선.")]
    [SerializeField] private GameObject bubblePrefab;
    [Header("강화 선택 (기존 보상 카드 프리팹)")]
    [SerializeField] private RewardSelectionUI selectionUIPrefab;
    private RewardSelectionUI _ui;
    private bool _isOpen;
    private bool _used;

    private GameObject _bubble;
    private Tooltip _tooltip;

    private bool UnlimitedEnhancements => GameManager.Instance != null && GameManager.Instance.testMode_UnlimitedShopEnhancements;
    private bool UpgradeLimitReached => _used && !UnlimitedEnhancements;

    public override string InteractionPrompt => UpgradeLimitReached ? "이 상점에서 강화 완료" : $"F : 장비 강화 ({CurrentCost()}G)";

    // ─── 강화 ─────────────────────────────────────────────────────────
    public override bool Interact(GameObject interactor)
    {
        var psi = PlayerSkillInventoryManager.Instance;
        var inv = GameManager.Instance != null ? GameManager.Instance.inventoryManager : null;
        if (psi == null || inv == null || UpgradeLimitReached || _isOpen) return false;

        if (!psi.CanEnhanceEquipped())
        {
            Debug.Log("[EnhanceShop] 강화할 장비가 없거나 이미 최대 강화 레벨입니다.");
            return false;
        }

        var source = psi.EquippedEquipment;
        var options = source.baseData.upgradeOptions;
        var choices = new List<RewardCandidate>();
        foreach (var next in options)
        {
            if (next == null) continue;
            choices.Add(new RewardCandidate {
                category = RewardCategory.Equipment, rawData = next,
                displayData = new GrowthItemData { itemName = next.equipmentName, icon = next.icon,
                    description = next.description + $"\n\n강화 비용: {CurrentCost()}G "
                        + (UnlimitedEnhancements ? "(디버그: 횟수 제한 없음)" : "(상점당 1회)") }
            });
        }
        if (choices.Count == 0) return false;
        if (_ui == null && selectionUIPrefab != null && UIPopUpManager.Instance != null)
            _ui = Instantiate(selectionUIPrefab, UIPopUpManager.Instance.transform, false);
        if (_ui == null) return false;

        _isOpen = true;
        _ui.ShowWithCallbacks(choices, choice => {
            // 선택 중 다른 경로로 장비가 바뀌었으면 과금하지 않는다.
            if (!UpgradeLimitReached && psi.EquippedEquipment == source && choice.rawData is EquipmentSO next)
            {
                int cost = CurrentCost();
                if (inv.SpendGold(cost))
                {
                    if (psi.UpgradeTo(next)) _used = true;
                    else inv.AddGold(cost);
                }
                else
                {
                    FloatingTextManager.ShowNotice($"골드 부족 · 필요 {cost}G / 보유 {inv.GOLD}G", Color.yellow, transform);
                }
            }
            CloseSelection();
            RefreshBubble();
        }, CloseSelection);
        return true;
    }

    private void CloseSelection()
    {
        if (_isOpen && _ui != null) _ui.Hide();
        _isOpen = false;
    }

    private void OnDisable() => CloseSelection();
    private void OnDestroy()
    {
        CloseSelection();
        if (_ui != null) Destroy(_ui.gameObject);
    }

    /// <summary>
    /// 강화 비용 = enhanceCost + 현재 강화레벨 × enhanceCostPerLevel.
    /// 둘 다 ShopRegistrySO 에 있으므로 밸런싱은 에셋에서 끝난다.
    /// </summary>
    public int CurrentCost()
    {
        var dm = GameManager.Instance != null ? GameManager.Instance.dataManager : null;
        var reg = dm != null ? dm.SHOP_REGISTRY : null;
        if (reg == null) return 0;

        var eq = EquippedOrNull();
        int level = eq != null ? eq.enhanceLevel : 0;
        return reg.enhanceCost + level * reg.enhanceCostPerLevel;
    }

    private static EquipmentInstance EquippedOrNull()
        => PlayerSkillInventoryManager.Instance != null
            ? PlayerSkillInventoryManager.Instance.EquippedEquipment
            : null;

    // ─── 말풍선 ───────────────────────────────────────────────────────
    // NPCBase 의 popupSystem 아이콘 경로는 그대로 살려두고(비어 있으면 no-op) 그 위에 말풍선을 얹는다.
    public override void OnFocused(GameObject interactor)
    {
        base.OnFocused(interactor);
        ShowBubble();
    }

    public override void OnLostFocus(GameObject interactor)
    {
        base.OnLostFocus(interactor);
        HideBubble();
    }

    private void ShowBubble()
    {
        if (bubbleCanvas == null || bubblePrefab == null) return;

        bubbleCanvas.SetActive(true);
        if (_bubble == null)
        {
            _bubble = Instantiate(bubblePrefab, bubbleCanvas.transform);
            _tooltip = _bubble.GetComponent<Tooltip>();
        }
        RefreshBubble();
    }

    private void HideBubble()
    {
        if (_bubble != null) Destroy(_bubble);
        _bubble = null;
        _tooltip = null;
        if (bubbleCanvas != null) bubbleCanvas.SetActive(false);
    }

    /// <summary>말풍선 두 줄을 현재 상태로 다시 쓴다. 살 수 없는 상태면 가격 대신 이유를 보여준다.</summary>
    private void RefreshBubble()
    {
        if (_tooltip == null) return;

        var psi = PlayerSkillInventoryManager.Instance;
        var eq = EquippedOrNull();

        if (eq == null || eq.baseData == null)
        {
            if (_tooltip.name != null) _tooltip.name.text = "장비 강화";
            if (_tooltip.price != null) _tooltip.price.text = "장비 없음";
            return;
        }

        if (_tooltip.name != null)
            _tooltip.name.text = eq.baseData.equipmentName;

        if (_tooltip.price != null)
            _tooltip.price.text = UpgradeLimitReached ? "강화 완료" : (psi != null && psi.CanEnhanceEquipped()) ? $"{CurrentCost()}G" : "최종 장비";
    }
}
