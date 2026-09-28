using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;

public class SellItem : MonoBehaviour, IInteractable
{
    public RewardCandidate item;
    [SerializeField] private GameObject Canvas;
    [SerializeField] private GameObject explainPrefab;
    private GameObject obj;
    private SpriteRenderer _spriteRenderer;
    private bool _purchased;
    private bool _hovering;

    public string InteractionPrompt => item.displayData != null ? $"Buy {item.displayData.itemName} ({item.goldAmount}G)" : "Buy (??)";

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void InitializeUI()
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (item.displayData != null && _spriteRenderer != null)
        {
            if (item.displayData.icon != null)
            {
                _spriteRenderer.sprite = item.displayData.icon;
            }
        }
    }

    public bool Interact(GameObject interactor)
    {
        var inventory = InventoryManager.Instance;
        if (_purchased || item.rawData == null || inventory == null || item.goldAmount < 0) return false;

        if (inventory.SpendGold(item.goldAmount))
        {
            if (!RewardManager.TryGivePurchase(item, transform.position))
            {
                inventory.AddGold(item.goldAmount);
                return false;
            }
            _purchased = true;
            HideHover();
            gameObject.SetActive(false);
            Destroy(this.gameObject);
            return true;
        }
        else
        {
            Debug.Log("Not enough gold!");
            return false;
        }
    }

    // 월드 콜라이더의 마우스 이벤트만 사용한다. 별도 레이캐스트/툴팁 시스템은 만들지 않는다.
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

    private void OnMouseExit() => HideHover();
    private void OnDisable() => HideHover();
    private void HideHover()
    {
        if (_hovering) CommonTooltipUI.Instance?.Hide();
        _hovering = false;
    }

    // IInteractable — 이게 있어야 PlayerController.CheckForInteractable 가 이 상점 아이템을 감지해 F 로 Interact 를 부른다.
    public void OnFocused(GameObject interactor)
    {
        ShowTooltip();
    }

    public void OnLostFocus(GameObject interactor)
    {
        HideTooltip();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") || (collision.transform.root != null && collision.transform.root.CompareTag("Player")))
        {
            ShowTooltip();
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") || (collision.transform.root != null && collision.transform.root.CompareTag("Player")))
        {
            HideTooltip();
        }
    }

    private void ShowTooltip()
    {
        if (item.displayData == null) return;

        if (Canvas != null)
        {
            Canvas.SetActive(true);
            if (obj == null && explainPrefab != null)
            {
                obj = Instantiate(explainPrefab, Canvas.transform);
                Tooltip text = obj.GetComponent<Tooltip>();
                if (text != null)
                {
                    if (item.displayData.localizedItemName != null && !item.displayData.localizedItemName.IsEmpty)
                    {
                        var locEvent = text.name.GetComponent<LocalizeStringEvent>();
                        if (locEvent == null)
                        {
                            locEvent = text.name.gameObject.AddComponent<LocalizeStringEvent>();
                            locEvent.OnUpdateString.AddListener((s) => text.name.text = s);
                        }
                        locEvent.StringReference = item.displayData.localizedItemName;
                    }
                    else
                    {
                        var locEvent = text.name.GetComponent<LocalizeStringEvent>();
                        if (locEvent != null) locEvent.StringReference = null;
                        if (text.name != null) text.name.text = item.displayData.itemName;
                    }

                    if (text.price != null) text.price.text = $"{item.goldAmount}G";
                }
            }
        }
    }

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
