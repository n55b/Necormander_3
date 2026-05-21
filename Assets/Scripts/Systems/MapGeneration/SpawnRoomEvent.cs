using UnityEngine;

/// <summary>
/// 시작 방(Spawn Room)의 특수 로직과 플레이어 스폰 위치를 관리합니다.
/// </summary>
public class SpawnRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Spawn Settings")]
    [SerializeField] private Transform playerSpawnPoint;

    /// <summary>
    /// 플레이어가 시작할 위치를 반환합니다. (포인트가 없으면 방 중앙 반환)
    /// </summary>
    public Vector3 GetSpawnPosition()
    {
        return playerSpawnPoint != null ? playerSpawnPoint.position : transform.position;
    }

    public void OnPlayerEnter(RoomInstance room)
    {
        // 시작 방은 진입 시 문을 닫지 않거나, 튜토리얼 메시지를 띄우는 용도로 사용 가능
        Debug.Log("<color=cyan>[SpawnRoom]</color> Welcome to the Dungeon!");
        room.isCleared = true; // 시작 방은 태생적으로 클리어 상태
    }

    public void OnRoomCleared(RoomInstance room)
    {
        // 시작 방은 특별한 클리어 조건이 없으므로 빈 칸으로 둠
    }
}
