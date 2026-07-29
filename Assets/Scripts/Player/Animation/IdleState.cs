using UnityEngine;

public class IdleState : PlayerAnimationState
{
    public IdleState(PlayerController _controller) : base(_controller){}

    public override void Enter()
    {
        controller.PlayAllAnim("Idle");
    }

    public override void Update()
    {
    }

    public override void Exit()
    {
        
    }
}
