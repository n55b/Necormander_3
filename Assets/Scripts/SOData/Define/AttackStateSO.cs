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
        if (fsm.target.gameObject.TryGetComponent<CharacterStat>(out var targetStat))
        {
            DamageInfo info = new DamageInfo(fsm.stats.ATK, DamageType.Physical, fsm.gameObject);
            targetStat.GetDamage(info);
        }
    }
}
