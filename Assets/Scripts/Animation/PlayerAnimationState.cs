using UnityEngine;

public abstract class PlayerAnimationState
{
    protected PlayerController controller;
    protected Animator animator;

    public PlayerAnimationState(PlayerController _controller)
    {
        this.controller = _controller;
    }

    public abstract void Enter(); // 상태 시작 시 (애니메이션 재생)
    public abstract void Update(); // 매 프레임 로직
    public abstract void Exit(); // 상태 종료 시
}
