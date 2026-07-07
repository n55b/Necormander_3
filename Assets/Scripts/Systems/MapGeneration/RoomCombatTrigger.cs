using UnityEngine;

/// <summary>
/// 방 한가운데에 위치하여 플레이어의 최종 입장을 감지해 전투를 격발시키는 트리거 컴포넌트입니다.
/// </summary>
public class RoomCombatTrigger : MonoBehaviour
{
    private RoomInstance _room;
    private bool _triggered = false;

    public void Init(RoomInstance room)
    {
        _room = room;
        _triggered = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_triggered) return;
        
        if (other.CompareTag("Player"))
        {
            _triggered = true;
            Debug.Log($"<color=orange>[CombatTrigger]</color> Player reached room center. Starting combat in {(_room != null ? _room.gameObject.name : "Unknown Room")}");
            _room?.StartCombatEvent();
        }
    }
}
