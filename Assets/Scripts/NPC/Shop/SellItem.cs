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
        if (item.rawData == null) return false;

        if (GameManager.Instance.inventoryManager.SpendGold(item.goldAmount))
        {
            RewardManager.Instance.ApplyReward(item);
            Destroy(this.gameObject);
            return true;
        }
        else
        {
            Debug.Log("Not enough gold!");
            return false;
        }
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
