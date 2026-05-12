using UnityEngine;

public class SellItem : MonoBehaviour
{
    [SerializeField] public PrizeDataSO item;

    void OnTriggerStay2D(Collider2D collision)
    {
        // 키 입력시 들어가 있으면 item 함수 받아와서 구매
    }

    void OExit2D(Collider2D collision)
    {
        // 키 입력 받아도 구매 안 되게끔
    }
}
