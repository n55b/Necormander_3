using UnityEngine;

/// <summary>
/// 방 클리어 후 방 한가운데에 스폰되어, 플레이어 상호작용 시 방 클리어 보상을 지급하는 상자입니다.
/// </summary>
public class RoomRewardBox : MonoBehaviour, IInteractable
{
    private RoomType _roomType = RoomType.Normal;
    private bool _isInitialized = false;

    public string InteractionPrompt => "Open Reward Box";

    public void Initialize(RoomType roomType)
    {
        _roomType = roomType;
        _isInitialized = true;
        Debug.Log($"<color=magenta>[RoomRewardBox]</color> Initialized for RoomType: {roomType}");
    }

    public bool Interact(GameObject interactor)
    {
        if (!_isInitialized) return false;

        if (RewardManager.Instance != null)
        {
            // 기존 보상 매니저의 방 클리어 보상 요청을 트리거
            RewardManager.Instance.RequestClearReward(_roomType);
            Debug.Log($"<color=magenta>[RoomRewardBox]</color> Opened, requested clear reward for: {_roomType}");
            
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
