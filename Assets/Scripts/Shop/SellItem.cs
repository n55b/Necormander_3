using TMPro;
using UnityEngine;

public class SellItem : MonoBehaviour
{
    [SerializeField] public PrizeDataSO item;
    [SerializeField] private GameObject Canvas;
    [SerializeField] private GameObject explainPrefab;
    private GameObject obj;

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
        // 키 입력시 들어가 있으면 item 함수 받아와서 구매
        if(collision.tag == "Player")
        {
            if(Input.GetKey(KeyCode.Q))
            {
                if(GameManager.Instance.inventoryManager.SpendGold(item.gold))
                {
                    item.BuyItem();
                    Destroy(this.gameObject);
                }
            }
        }
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
