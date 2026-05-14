using TMPro;
using UnityEngine;

public class SellItem : MonoBehaviour, IInteractable
{
    [SerializeField] public PrizeDataSO item;
    [SerializeField] private GameObject Canvas;
    [SerializeField] private GameObject explainPrefab;
    private GameObject obj;

    public string InteractionPrompt => $"Buy {item.name} ({item.gold}G)";

    public bool Interact(GameObject interactor)
    {
        if (GameManager.Instance.inventoryManager.SpendGold(item.gold))
        {
            item.BuyItem();
            Destroy(this.gameObject);
            return true;
        }
        else
        {
            Debug.Log("Not enough gold!");
            // TODO: 골드 부족 피드백 UI 표시
            return false;
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Player")
        {
            Canvas.SetActive(true);
            obj = Instantiate(explainPrefab, Canvas.transform);
            Tooltip text = obj.GetComponent<Tooltip>();
            text.name.text = item.name;
            text.price.text = $"{item.gold}G";
        }
    }

    void OnTriggerStay2D(Collider2D collision)
    {
        // 기존 구매 로직은 Interact()로 이전됨
        // if(collision.tag == "Player")
        // {
        //     if(Input.GetKey(KeyCode.Q))
        //     {
        //         if(GameManager.Instance.inventoryManager.SpendGold(item.gold))
        //         {
        //             item.BuyItem();
        //             Destroy(this.gameObject);
        //         }
        //     }
        // }
    }

    void OnTriggerExit2D(Collider2D collision)
    {
        if(collision.tag == "Player")
        {
            Destroy(obj);
            Canvas.SetActive(false);
        }
    }
}
