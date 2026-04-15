using UnityEngine;

[CreateAssetMenu(menuName = "FSM/State/Follow")]
public class FollowStateSO : FSMStateSO
{
    public override void Enter(EntityFSM fsm)
    {

    }
    public override void Execute(EntityFSM fsm)
    {
        if (fsm.target == null) return;

        // CharacterState에 있는 speed 데이터를 가져와 사용
        float speed = fsm.stats.MOVESPEED;
        float dist = Vector3.Distance(fsm.transform.position, fsm.target.position);

        // 일정 거리 이상일 때만 추적
        if (dist > fsm.stats.ATKRANGE)
        {
            fsm.transform.position = Vector3.MoveTowards(
                fsm.transform.position,
                fsm.target.position,
                speed * Time.deltaTime
            );
        }
    }
    public override void Exit(EntityFSM fsm)
    {

    }
}
