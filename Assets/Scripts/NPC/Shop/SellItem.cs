using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;

/// <summary>
/// [레거시] 바닥에 진열하던 상점 아이템. 26/09/30 부터 상점은 ShopNPC 에 말을 걸어 여는 ShopPopupUI 로 바뀌었고
/// 기본 상점 프리팹에서는 빠졌다. 다른 곳에서 참조하는 경우를 위해 스크립트만 남겨둔다.
/// </summary>
public class SellItem : MonoBehaviour, IInteractable
{
    public RewardCandidate item;
    [SerializeField] private GameObject Canvas;
    [SerializeField] private GameObject explainPrefab;
    private GameObject obj;
    private SpriteRenderer _spriteRenderer;
    private bool _purchased;
    private bool _hovering;

    // ─── 공개 API ────────────────────────────────────────────────────
    public string InteractionPrompt => item.displayData != null ? $"Buy {item.displayData.itemName} ({item.goldAmount}G)" : "Buy (??)";

    /// <summary>진열 스프라이트를 상품 아이콘으로 바꾼다.</summary>
    public void InitializeUI()
    {
        if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        if (item.displayData != null && item.displayData.icon != null && _spriteRenderer != null)
            _spriteRenderer.sprite = item.displayData.icon;
    }

    /// <summary>F 입력 시 구매. 골드 차감 → 지급, 지급 실패 시 환불.</summary>
    public bool Interact(GameObject interactor)
    {
        var inventory = InventoryManager.Instance;
        if (_purchased || item.rawData == null || inventory == null || item.goldAmount < 0) return false;

        if (!inventory.SpendGold(item.goldAmount))
        {
            Debug.Log("Not enough gold!");
            return false;
        }
        if (!RewardManager.TryGivePurchase(item, transform.position))
        {
            inventory.AddGold(item.goldAmount);
            return false;
        }

        _purchased = true;
        HideHover();
        HideTooltip();
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }

    // IInteractable — PlayerController.CheckForInteractable 가 가장 가까운 대상으로 고를 때 불린다.
    public void OnFocused(GameObject interactor) => ShowTooltip();
    public void OnLostFocus(GameObject interactor) => HideTooltip();

    // ─── 내부 전용 ───────────────────────────────────────────────────
    private void Awake() => _spriteRenderer = GetComponent<SpriteRenderer>();
    private void OnDisable() => HideHover();
    private void OnMouseExit() => HideHover();

    // 마우스를 올리면 공용 툴팁(CommonTooltipUI)에 이름/설명/가격을 띄운다.
    // UI 위에 있거나 팝업이 떠 있으면 숨긴다.
    private void OnMouseOver()
    {
        bool blocked = UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        if (blocked || _purchased || (UIPopUpManager.Instance != null && UIPopUpManager.Instance.IsPopUpActive))
        { HideHover(); return; }
        if (_hovering || CommonTooltipUI.Instance == null || item.displayData == null) return;

        var data = new TooltipData(item.displayData.itemName, item.displayData.description);
        data.localizedTitle = item.displayData.localizedItemName;
        data.localizedDescription = item.displayData.localizedDescription;
        if (item.rawData is ItemSO so)
        {
            data.title = so.DisplayName;
            data.description = so.TooltipBody;
            data.localizedTitle = null;
            data.localizedDescription = null;
        }
        else if (item.rawData is MinionDataSO minion)
        {
            data.description = HandSlotSelectionUI.Describe(minion);
            data.localizedDescription = null;
        }
        data.footer = $"가격: {item.goldAmount}G · F 구매";
        CommonTooltipUI.Instance.Show(data);
        _hovering = true;
    }

    // 마우스 툴팁을 닫는다.
    private void HideHover()
    {
        if (_hovering) CommonTooltipUI.Instance?.Hide();
        _hovering = false;
    }

    // 플레이어가 트리거 범위에 들어오거나 나가면 월드 툴팁을 켜고 끈다.
    private void OnTriggerEnter2D(Collider2D collision) { if (IsPlayer(collision)) ShowTooltip(); }
    private void OnTriggerExit2D(Collider2D collision) { if (IsPlayer(collision)) HideTooltip(); }

    private static bool IsPlayer(Collider2D c) =>
        c.CompareTag("Player") || (c.transform.root != null && c.transform.root.CompareTag("Player"));

    // 아이템 위 월드 캔버스에 이름/가격 툴팁(explainPrefab)을 띄운다.
    private void ShowTooltip()
    {
        if (item.displayData == null || _purchased || Canvas == null) return;

        Canvas.SetActive(true);
        if (obj != null || explainPrefab == null) return;

        obj = Instantiate(explainPrefab, Canvas.transform);
        Tooltip text = obj.GetComponent<Tooltip>();
        if (text == null) return;

        var locEvent = text.name.GetComponent<LocalizeStringEvent>();
        if (item.displayData.localizedItemName != null && !item.displayData.localizedItemName.IsEmpty)
        {
            if (locEvent == null)
            {
                locEvent = text.name.gameObject.AddComponent<LocalizeStringEvent>();
                locEvent.OnUpdateString.AddListener((s) => text.name.text = s);
            }
            locEvent.StringReference = item.displayData.localizedItemName;
        }
        else
        {
            if (locEvent != null) locEvent.StringReference = null;
            if (text.name != null) text.name.text = item.displayData.itemName;
        }

        if (text.price != null) text.price.text = $"{item.goldAmount}G";
    }

    // 월드 툴팁을 지우고 캔버스를 끈다.
    private void HideTooltip()
    {
        if (obj != null)
        {
            Destroy(obj);
            obj = null;
        }
        if (Canvas != null) Canvas.SetActive(false);
    }
}
