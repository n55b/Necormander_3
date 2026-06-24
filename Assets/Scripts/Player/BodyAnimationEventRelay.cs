using UnityEngine;

/// <summary>
/// Body의 Animator에서 발생하는 Animation Event(예: CanChangeAnimState)를
/// 상위(부모)에 있는 PlayerController로 전달합니다.
/// Animation Event는 Animator가 붙어있는 GameObject에서만 메서드를 찾기 때문에,
/// PlayerController가 부모(루트)에 있는 구조(Player Melee 등)에서는 이 릴레이가 필요합니다.
/// </summary>
public class BodyAnimationEventRelay : MonoBehaviour
{
    private PlayerController _controller;

    private void Awake()
    {
        _controller = GetComponentInParent<PlayerController>();
        if (_controller == null)
        {
            Debug.LogWarning($"[BodyAnimationEventRelay] {gameObject.name}: 부모에서 PlayerController를 찾지 못했습니다.");
        }
    }

    /// <summary>
    /// Attack 애니메이션 클립의 Animation Event(CanChangeAnimState)에서 호출됩니다.
    /// </summary>
    public void CanChangeAnimState()
    {
        _controller?.CanChangeAnimState();
    }
}
