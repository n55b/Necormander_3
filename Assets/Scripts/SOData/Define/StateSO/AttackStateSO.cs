using UnityEngine;

[CreateAssetMenu(fileName = "AttackState", menuName = "FSM/State/Attack")]
public class AttackStateSO : FSMStateSO
{
    public override void Enter(EntityFSM fsm)
    {
        fsm.atkTimer = 0.0f;
    }

    public override void Execute(EntityFSM fsm)
    {
        if (fsm.target == null) return;

        fsm.atkTimer += Time.deltaTime;

        if (fsm.atkTimer >= fsm.stats.ATKSPD)
        {
            Attack(fsm);
            fsm.atkTimer = 0.0f;
        }
    }

    public override void Exit(EntityFSM fsm) { }

    private void Attack(EntityFSM fsm)
    {
        // 1. AllyController가 있는지 확인 (아군인 경우)
        if (fsm.TryGetComponent<AllyController>(out var ally))
        {
            ally.ExecuteAttack(fsm.target);
        }
        // 2. 적군인 경우 (기본 공격 수행)
        else if (fsm.target.gameObject.TryGetComponent<CharacterStat>(out var targetStat))
        {
            DamageInfo info = new DamageInfo(fsm.stats.ATK, DamageType.Physical, fsm.gameObject);
            targetStat.GetDamage(info);
        }
    }
}
