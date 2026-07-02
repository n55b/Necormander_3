using UnityEngine;

/// <summary>
/// 방 클리어 후 방 한가운데에 스폰되어, 플레이어 상호작용 시 방 클리어 보상을 지급하는 상자입니다.
/// </summary>
public class RoomRewardBox : MonoBehaviour, IInteractable
{
    private RoomType _roomType = RoomType.Normal;
    private RoomInstance.NormalRewardType _normalRewardType = RoomInstance.NormalRewardType.PlayerSkill;
    private bool _isInitialized = false;

    public string InteractionPrompt => "Open Reward Box";

    public void Initialize(RoomType roomType)
    {
        _roomType = roomType;
        _normalRewardType = RoomInstance.NormalRewardType.PlayerSkill;
        _isInitialized = true;
        Debug.Log($"<color=magenta>[RoomRewardBox]</color> Initialized for RoomType: {roomType} (Fallback: PlayerSkill)");
    }

    public void Initialize(RoomType roomType, RoomInstance.NormalRewardType normalRewardType)
    {
        _roomType = roomType;
        _normalRewardType = normalRewardType;
        _isInitialized = true;
        Debug.Log($"<color=magenta>[RoomRewardBox]</color> Initialized for RoomType: {roomType}, NormalRewardType: {normalRewardType}");
    }

    public bool Interact(GameObject interactor)
    {
        if (!_isInitialized) return false;

        if (RewardManager.Instance != null)
        {
            // 기존 보상 매니저의 방 클리어 보상 요청을 트리거 (세부 보상 속성 전달)
            RewardManager.Instance.RequestClearReward(_roomType, _normalRewardType);
            Debug.Log($"<color=magenta>[RoomRewardBox]</color> Opened, requested clear reward for: {_roomType} ({_normalRewardType})");
            
            // 획득 시 자신을 파괴
            Destroy(gameObject);
            return true;
        }

        Debug.LogWarning("[RoomRewardBox] RewardManager Instance not found.");
        return false;
    }

    public void OnFocused(GameObject interactor) {}
    public void OnLostFocus(GameObject interactor) {}
}
