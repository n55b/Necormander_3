using System.Collections;
using UnityEngine;

/// <summary>
/// 방 클리어 후 방 한가운데에 스폰되어, 플레이어 상호작용 시 방 클리어 보상을 지급하는 상자입니다.
/// </summary>
public class RoomRewardBox : MonoBehaviour, IInteractable
{
    private const string OpenAnimName = "Chest_Open";

    private RoomType _roomType = RoomType.Normal;
    private RoomInstance.NormalRewardType _normalRewardType = RoomInstance.NormalRewardType.PlayerSkill;
    private bool _isInitialized = false;
    // 여는 애니메이션 재생 중 중복 상호작용/파괴 방지
    private bool _isOpening = false;

    private Animator _animator;

    public string InteractionPrompt => "Open Reward Box";

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

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
        if (!_isInitialized || _isOpening) return false;

        if (RewardManager.Instance != null)
        {
            _isOpening = true;
            StartCoroutine(PlayOpenThenGrantReward());
            return true;
        }

        Debug.LogWarning("[RoomRewardBox] RewardManager Instance not found.");
        return false;
    }

    /// <summary>Chest.aseprite의 Chest_Open 애니메이션을 재생한 뒤, 재생 길이만큼 기다렸다가 보상을 지급하고 파괴합니다.</summary>
    private IEnumerator PlayOpenThenGrantReward()
    {
        float wait = 0f;
        if (_animator != null)
        {
            // 열기 전까지는 인스펙터에 세팅된 정적 스프라이트를 그대로 유지하기 위해
            // Animator는 평소 꺼둔 상태(m_Enabled: 0)다. 열 때만 켜서 재생한다.
            _animator.enabled = true;
            _animator.Play(OpenAnimName, 0, 0f);
            wait = GetClipLength(_animator, OpenAnimName);
        }

        if (wait > 0f) yield return new WaitForSeconds(wait);

        // 기존 보상 매니저의 방 클리어 보상 요청을 트리거 (세부 보상 속성 전달)
        RewardManager.Instance.RequestClearReward(_roomType, _normalRewardType);
        Debug.Log($"<color=magenta>[RoomRewardBox]</color> Opened, requested clear reward for: {_roomType} ({_normalRewardType})");

        // 획득 시 자신을 파괴
        Destroy(gameObject);
    }

    private static float GetClipLength(Animator animator, string clipName)
    {
        var controller = animator.runtimeAnimatorController;
        if (controller == null) return 0f;

        foreach (var clip in controller.animationClips)
        {
            if (clip != null && clip.name == clipName) return clip.length;
        }

        return 0f;
    }

    public void OnFocused(GameObject interactor) {}
    public void OnLostFocus(GameObject interactor) {}
}
