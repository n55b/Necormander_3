using UnityEngine;

public class IdleState : AnimationState
{
    public IdleState(Animator animator) : base(animator){}

    public override void Enter()
    {
        animator.Play("Idle");
    }

    public override void Update()
    {
    }

    public override void Exit()
    {
        
    }
}
