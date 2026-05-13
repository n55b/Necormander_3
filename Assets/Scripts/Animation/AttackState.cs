using UnityEngine;

public class AttackState : AnimationState
{
    public AttackState(Animator animator) : base(animator){}

    public override void Enter()
    {
        animator.Play("Attack");
    }

    public override void Update()
    {
    }

    public override void Exit()
    {
        
    }
}
