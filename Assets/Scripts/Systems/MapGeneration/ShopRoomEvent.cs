using UnityEngine;

/// <summary>
/// 상점 방의 이벤트를 담당합니다.
/// 인스펙터에서 연결된 ShopNPC의 아이템을 입장 시점에 갱신합니다.
///
/// 같은 방에 서 있는 EnhanceShopNPC(장비 강화)는 여기서 건드릴 게 없다 — 재고 개념이 없어서
/// 입장 시점에 굴릴 것도 없고, F 상호작용만으로 자기 완결이다.
/// </summary>
public class ShopRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Shop References")]
    [Tooltip("방 프리팹 내부에 있는 ShopNPC 오브젝트를 여기에 연결해주세요.")]
    [SerializeField] private ShopNPC shopNPC;

    public void OnPlayerEnter(RoomInstance room)
    {
        Debug.Log("<color=yellow>[ShopRoom]</color> Welcome to the Shop!");

        // 입장 시점에만 상품 갱신 (직접 연결된 NPC 사용)
        // 연결을 깜빡했을 수도 있으니 자식에서라도 찾아보는 최소한의 안전장치.
        if (shopNPC == null) shopNPC = GetComponentInChildren<ShopNPC>();

        if (shopNPC != null) shopNPC.Initialize();
        else Debug.LogWarning($"[ShopRoomEvent] {gameObject.name}: ShopNPC가 연결되지 않았고 자식 중에도 없습니다!");

        // 상점 진입 시 클리어 처리 (문이 닫히지 않도록)
        room.MarkCleared();
    }

    public void OnRoomCleared(RoomInstance room)
    {
        // 상점 이용 완료 후의 특수 로직이 필요하다면 작성
    }
}
