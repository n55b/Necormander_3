using UnityEngine;

public class PriestController : AllyController
{
    [Header("사제 전용 설정")]
    [SerializeField] private float healAmount = 10f;
    [SerializeField] private float healRange = 5f;
    [SerializeField] private LayerMask allyLayer;

    protected override void HandleAIUpdate()
    {
        // 1. 주변 detectRange 내의 아군들 중 체력이 깎인 대상이 있는지 확인
        Collider2D[] alliesInRange = Physics2D.OverlapCircleAll(transform.position, detectRange, opponentLayer); // NearestTargetFinder의 targetLayer와 맞춤
        
        Transform bestTarget = null;
        float lowestHPRatio = 1f;

        foreach (var col in alliesInRange)
        {
            if (col.TryGetComponent<CharacterStat>(out var stat))
            {
                if (stat.IsDead) continue;

                float hpRatio = stat.CURHP / stat.MAXHP;
                if (hpRatio < 1f && hpRatio < lowestHPRatio)
                {
                    lowestHPRatio = hpRatio;
                    bestTarget = col.transform;
                }
            }
        }

        // 2. 힐할 대상이 있다면 타겟으로 설정하고 상태 전환
        if (bestTarget != null)
        {
            _fsm.target = bestTarget;
            float dist = Vector3.Distance(transform.position, bestTarget.position);

            if (dist <= _stats.ATKRANGE)
                _fsm.ChangeState(attackState);
            else
                _fsm.ChangeState(followState);
        }
        else
        {
            // 3. 힐할 대상이 없으면 타겟을 비우고 플레이어를 따라감
            _fsm.target = null;
            HandleNoTarget();
        }
    }

    public override void ExecuteAttack(Transform target)
    {
        // ExecuteAttack 시점에서도 다시 한 번 유효한 타겟(부상당한 아군)인지 확인
        if (target == null) return;
        
        if (target.TryGetComponent<CharacterStat>(out var stat))
        {
            if (stat.CURHP < stat.MAXHP)
            {
                stat.Heal(healAmount);
                Debug.Log($"<color=green>[Priest]</color> {target.name}을(를) {healAmount}만큼 치유했습니다! (현재 HP: {stat.CURHP}/{stat.MAXHP})");
            }
        }
    }
}
