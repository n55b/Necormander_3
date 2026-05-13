using UnityEngine;

public class AttackState : PlayerAnimationState
{
    public AttackState(PlayerController _controller) : base(_controller) { }
    public override void Enter()
    {
        CalculateDirection();
        controller.PlayAllAnim("Attack");
    }

    public override void Update()
    {
    }

    public override void Exit()
    {

    }

    private void CalculateDirection()
    {
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        Vector2 direction = new Vector2(mousePos.x - controller.transform.position.x, controller.transform.position.y);

        if (direction.x > 0.0f)
            controller.transform.localScale = new Vector3(-1, controller.transform.localScale.y, controller.transform.transform.localScale.z);
        else if (direction.x < 0.0f)
            controller.transform.localScale = new Vector3(1, controller.transform.transform.localScale.y, controller.transform.transform.localScale.z);
    }
}
