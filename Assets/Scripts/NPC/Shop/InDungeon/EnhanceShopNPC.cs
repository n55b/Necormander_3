using UnityEngine;

/// <summary>
/// 강화 전용 상점 NPC. 진열품(SellItem) 없이 F 한 번에 착용 장비를 +1강 한다.
///
/// 일반 상점(ShopNPC)은 방당 재고를 한 번 굴려서 그게 제동장치지만, 여기는 재고가 없다 —
/// 골드와 EquipmentSO.maxEnhanceLevel 둘만이 제동장치라서 가격이 레벨마다 오른다(CurrentCost).
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

    private GameObject _bubble;
    private Tooltip _tooltip;

    public override string InteractionPrompt => $"F : 장비 강화 ({CurrentCost()}G)";

    // ─── 강화 ─────────────────────────────────────────────────────────
    public override bool Interact(GameObject interactor)
    {
        var psi = PlayerSkillInventoryManager.Instance;
        var inv = GameManager.Instance != null ? GameManager.Instance.inventoryManager : null;
        if (psi == null || inv == null) return false;

        if (!psi.CanEnhanceEquipped())
        {
            Debug.Log("[EnhanceShop] 강화할 장비가 없거나 이미 최대 강화 레벨입니다.");
            return false;
        }

        if (!inv.SpendGold(CurrentCost()))
        {
            Debug.Log("[EnhanceShop] 골드가 부족합니다.");
            return false;
        }

        psi.EnhanceEquipped();

        // 무제한 상점이라 그 자리에서 또 누를 수 있다. 다음 강화의 가격·레벨로 즉시 갱신.
        RefreshBubble();
        return true;
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
            _tooltip.name.text = $"{eq.baseData.equipmentName} +{eq.enhanceLevel}/{eq.baseData.maxEnhanceLevel}";

        if (_tooltip.price != null)
            _tooltip.price.text = (psi != null && psi.CanEnhanceEquipped()) ? $"{CurrentCost()}G" : "최대 강화";
    }
}
