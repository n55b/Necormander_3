using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;

public class SellItem : MonoBehaviour
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

        // [강화 아이템] 착용 장비가 없거나 이미 최대 강화면 구매 불가 — 골드 안 쓰고 아이템도 유지.
        if (item.category == RewardCategory.EquipmentEnhance)
        {
            var psi = PlayerSkillInventoryManager.Instance;
            if (psi == null || !psi.CanEnhanceEquipped())
            {
                Debug.Log("[Shop] 강화할 장비가 없거나 이미 최대 강화 레벨입니다.");
                return false;
            }
        }

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

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Player" && item.displayData != null)
        {
            Canvas.SetActive(true);
            obj = Instantiate(explainPrefab, Canvas.transform);
            Tooltip text = obj.GetComponent<Tooltip>();
            
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
                text.name.text = item.displayData.itemName;
            }
            
            text.price.text = $"{item.goldAmount}G";
        }
    }

    void OnTriggerExit2D(Collider2D collision)
    {
        if(collision.tag == "Player")
        {
            if (obj != null) Destroy(obj);
            Canvas.SetActive(false);
        }
    }
}
