using UnityEngine;

[CreateAssetMenu(fileName = "AttackState", menuName = "FSM/State/Attack")]
public class AttackStateSO : FSMStateSO
{
    private float _lastAttackTime;

    public override void Enter(EntityFSM fsm) { }

    public override void Execute(EntityFSM fsm)
    {
        if (fsm.target == null) return;

        float dist = Vector2.Distance(fsm.transform.position, fsm.target.position);

        if (dist <= fsm.stats.ATKRANGE)
        {
            if (Time.time >= _lastAttackTime + (1f / fsm.stats.ATKSPD))
            {
                Attack(fsm);
                _lastAttackTime = Time.time;
            }
        }
    }

    public override void Exit(EntityFSM fsm) { }

    private void Attack(EntityFSM fsm)
    {
        if (fsm.target.TryGetComponent<CharacterStat>(out var targetStat))
        {
            DamageInfo info = new DamageInfo(fsm.stats.ATK, DamageType.Physical, fsm.gameObject);
            targetStat.GetDamage(info);
        }
    }
}
