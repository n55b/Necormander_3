using UnityEngine;

/// <summary>
/// 공격 상태의 기반 클래스이자, '근접 공격'을 수행하는 기본 클래스입니다.
/// 특수 공격이 없는 미니언들은 이 에셋을 그대로 사용하면 근접 공격을 수행합니다.
/// </summary>
[CreateAssetMenu(fileName = "MeleeAttackState", menuName = "Necromancer/Attack States/MeleeAttack")]
public class AttackStateSO : FSMStateSO
{
    public override void Enter(EntityFSM fsm)
    {
        fsm.atkTimer = 0.0f;
    }

    public override void Execute(EntityFSM fsm)
    {
        fsm.atkTimer += Time.deltaTime;

        if (fsm.atkTimer >= fsm.stats.ATKSPD)
        {
            if (CanPerformAction(fsm))
            {
                PerformAction(fsm);
                fsm.atkTimer = 0.0f;
            }
        }
    }

    /// <summary>
    /// 기본적으로 타겟이 있어야 공격이 가능합니다.
    /// </summary>
    protected virtual bool CanPerformAction(EntityFSM fsm) => fsm.target != null;

    /// <summary>
    /// [기본 행동: 근접 공격]
    /// 자식 클래스(원거리, 힐러)에서 이 메서드를 오버라이드하여 행동을 변경합니다.
    /// </summary>
    protected virtual void PerformAction(EntityFSM fsm)
    {
        // 아군인 경우
        if (fsm.TryGetComponent<AllyController>(out var ally))
        {
            ally.ExecuteAttack(fsm.target);
        }
        // 적군인 경우
        else if (fsm.target.gameObject.TryGetComponent<CharacterStat>(out var targetStat))
        {
            DamageInfo info = new DamageInfo(fsm.stats.ATK, DamageType.Physical, fsm.gameObject);
            targetStat.GetDamage(info);
        }
    }

    public override void Exit(EntityFSM fsm) { }
}
