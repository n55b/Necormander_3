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
        List<RewardCandidate> prizes = RewardProcessor.GenerateShopRoom(GameManager.Instance.dataManager);

        for(int i = 0; i < prizes.Count; i++)
        {
            if (i < items.Count && items[i] != null)
            {
                items[i].item = prizes[i];
                items[i].InitializeUI(); // UI를 업데이트하는 함수 호출 (SellItem에 추가 예정)
            }
        }
    }
}
