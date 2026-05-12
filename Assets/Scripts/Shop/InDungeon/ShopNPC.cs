using System.Collections.Generic;
using UnityEngine;

public class ShopNPC : MonoBehaviour
{
    [SerializeField] List<SellItem> items;

    private void Start()
    {
        Initialize();
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
