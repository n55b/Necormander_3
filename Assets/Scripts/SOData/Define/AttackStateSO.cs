using UnityEngine;

[CreateAssetMenu(fileName = "AttackState", menuName = "FSM/State/Attack")]
public class AttackStateSO : FSMStateSO
{
    private float _lastAttackTime;
    private CharacterStat enemy;

    public override void Enter(EntityFSM fsm)
    {

    }

    public override void Execute(EntityFSM fsm)
    {
        if (fsm.target == null) return;

        // 타겟과의 거리 체크
        float dist = Vector3.Distance(fsm.transform.position, fsm.target.position);

        if (dist <= fsm.stats.ATKRANGE)
        {
            // 공격 쿨타임 체크
            if (Time.time >= _lastAttackTime + fsm.stats.ATKSPD)
            {
                Attack(fsm);
                _lastAttackTime = Time.time;
            }
        }
    }

    public override void Exit(EntityFSM fsm)
    {

    }

    private void Attack(EntityFSM fsm)
    {
        enemy.GetDamage(fsm.stats.ATK);
        Debug.Log($"{fsm.name}이(가) {fsm.target.name}을 공격합니다! 데미지: {fsm.stats.ATK}");
        // 여기에 실제 데미지를 입히는 로직이나 애니메이션 실행을 넣습니다.
    }
}