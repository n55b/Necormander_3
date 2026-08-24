using UnityEngine;

/// <summary>
/// 상점 방의 이벤트를 담당합니다.
/// 인스펙터에서 연결된 ShopNPC의 아이템을 입장 시점에 갱신합니다.
///
/// 강화 상점(Room_ShopEnhance)도 이 컴포넌트를 그대로 쓴다 — 거기서 이게 하는 일은
/// MarkCleared() 하나뿐이다(문이 안 닫히게). EnhanceShopNPC 는 재고 개념이 없어서
/// 입장 시점에 굴릴 게 없고, F 상호작용만으로 자기 완결이라 여기서 건드릴 게 없다.
/// </summary>
public class ShopRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Shop References")]
    [Tooltip("방 프리팹 내부에 있는 ShopNPC 오브젝트를 여기에 연결해주세요.\n" +
             "강화 상점 방이라면 비워두면 됩니다(EnhanceShopNPC 는 갱신할 재고가 없음).")]
    [SerializeField] private ShopNPC shopNPC;

    private void Start()
    {
        // 씬 직접 배치 또는 테스트 환경을 위해 Start 시점에도 초기화 시도
        TryInitializeShop();
    }

    public void OnPlayerEnter(RoomInstance room)
    {
        Debug.Log("<color=yellow>[ShopRoom]</color> Welcome to the Shop!");

        TryInitializeShop();

        // 상점 진입 시 클리어 처리 (문이 닫히지 않도록)
        if (room != null) room.MarkCleared();
    }

    private void TryInitializeShop()
    {
        if (shopNPC == null)
        {
            shopNPC = GetComponentInChildren<ShopNPC>(true);
            if (shopNPC == null) shopNPC = Object.FindFirstObjectByType<ShopNPC>();
        }

        if (shopNPC != null)
        {
            shopNPC.Initialize();
        }
        else if (GetComponentInChildren<EnhanceShopNPC>(true) == null && Object.FindFirstObjectByType<EnhanceShopNPC>() == null)
        {
            Debug.LogWarning($"[ShopRoomEvent] {gameObject.name}: 상점 NPC를 찾을 수 없습니다!");
        }
    }

    public void OnRoomCleared(RoomInstance room)
    {
        // 상점 이용 완료 후의 특수 로직이 필요하다면 작성
    }
}
