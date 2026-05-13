using UnityEngine;

public abstract class AnimationState
{
    protected Animator animator;

    public AnimationState(Animator animator)
    {
        this.animator = animator;
    }

    public abstract void Enter(); // 상태 시작 시 (애니메이션 재생)
    public abstract void Update(); // 매 프레임 로직
    public abstract void Exit(); // 상태 종료 시
}
