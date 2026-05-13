using UnityEngine;

public class WalkState : PlayerAnimationState
{
    public WalkState(PlayerController _controller) : base(_controller){}

    public override void Enter()
    {
        controller.PlayAllAnim("Walk");
    }

    public override void Update()
    {
    }

    public override void Exit()
    {
        
    }
}
