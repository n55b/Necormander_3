using System.Collections.Generic;
using UnityEngine;

public class ShopNPC : MonoBehaviour
{
    [SerializeField] List<SellItem> items;

    private void Start()
    {
        // 이제 ShopRoomEvent에서 OnPlayerEnter 시점에 명시적으로 Initialize를 호출합니다.
    }

    public void Initialize()
    {
        List<PrizeDataSO> prizes = RewardProcessor.GenerateShopRoom(GameManager.Instance.dataManager);

        for(int i = 0; i < prizes.Count; i++)
        {
            items[i].item = prizes[i];
        }
    }
}
