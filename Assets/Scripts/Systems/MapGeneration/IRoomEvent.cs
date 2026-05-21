/// <summary>
/// 방 진입 및 클리어 시의 고유 로직을 정의하는 인터페이스입니다.
/// (전투, 상점, 보상 등 방 타입별로 다른 동작을 구현합니다.)
/// </summary>
public interface IRoomEvent
{
    /// <summary>
    /// 플레이어가 방에 진입했을 때 호출됩니다.
    /// </summary>
    void OnPlayerEnter(RoomInstance room);

    /// <summary>
    /// 방의 목적(전투 승리, 상점 이용 등)이 달성되었을 때 호출됩니다.
    /// </summary>
    void OnRoomCleared(RoomInstance room);
}
