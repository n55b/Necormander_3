using UnityEngine;

/// <summary>
/// 보상 방의 이벤트를 담당합니다.
/// 입장 시 문을 닫고 상자를 활성화하며, 상자를 열면 문을 다시 엽니다.
/// </summary>
public class RewardRoomEvent : MonoBehaviour, IRoomEvent
{
    [Header("Reward References")]
    [Tooltip("방 프리팹 내부에 있는 보상 상자(EliteRewardBox)를 연결해주세요.")]
    [SerializeField] private EliteRewardBox rewardBox;

    private bool _isEventActive = false;
    private RoomInstance _cachedRoom;

    private void Awake()
    {
        // 시작 시 상자는 보이지 않게 숨겨둠
        if (rewardBox != null) rewardBox.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!_isEventActive) return;

        // [핵심] 상자가 상호작용으로 인해 파괴되었는지 체크
        if (rewardBox == null)
        {
            _isEventActive = false;
            Debug.Log("<color=magenta>[RewardRoom]</color> Box Opened! Room Unlocked.");
            _cachedRoom.MarkCleared(); // 문 열림 및 클리어 처리
        }
    }

    public void OnPlayerEnter(RoomInstance room)
    {
        if (room.isCleared || _isEventActive) return;

        _cachedRoom = room;
        _isEventActive = true;

        // 1. 문 폐쇄
        //room.SetDoorsOpen(false);

        // 2. 상자 등장
        if (rewardBox != null)
        {
            rewardBox.gameObject.SetActive(true);
            Debug.Log("<color=magenta>[RewardRoom]</color> Reward Box Appeared! Defeat the greed?");
        }
        else
        {
            Debug.LogWarning($"[RewardRoomEvent] {gameObject.name}: 보상 상자가 연결되지 않았습니다!");
            room.MarkCleared(); // 상자가 없으면 그냥 문 열어줌
        }
    }

    public void OnRoomCleared(RoomInstance room)
    {
        // 추가적인 보상 연출이나 사운드가 필요하다면 여기에 작성
    }
}
