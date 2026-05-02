using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 일반적인 미니언의 표준 AI 패턴입니다.
/// 가까운 적을 추적하고 사거리 안에서 공격을 수행합니다.
/// </summary>
[CreateAssetMenu(fileName = "BaseAIPattern", menuName = "Necromancer/AI/BasePattern")]
public class BaseAIPatternSO : AIPatternSO
{
    protected override void UpdateTargeting(BaseEntity entity)
    {
        // 1. 적군(Enemy) 탐색
        Transform nearestEnemy = entity.TargetFinder.FindNearest(entity.detectRange);
        
        if (nearestEnemy != null)
        {
            target = nearestEnemy;
        }
        else
        {
            // 2. 적이 없을 때: 아군은 플레이어를 타겟으로 삼음
            if (entity.team == Team.Ally)
            {
                var ally = entity as AllyController;
                if (ally != null && ally.player != null) target = ally.player;
                else target = null;
            }
            else
            {
                target = null;
            }
        }
    }

    protected override void UpdateStateTransitions(BaseEntity entity)
    {
        AIState nextState = AIState.Idle;

        if (target != null)
        {
            float dist = Vector2.Distance(entity.transform.position, target.position);
            
            // [수정] 아군 미니언이 플레이어를 따라갈 때만 거리 유지, 적군이 플레이어를 잡았을 때는 공격 수행
            if (entity.team == Team.Ally && target.CompareTag("Player"))
            {
                if (dist > 2.0f) nextState = AIState.Follow;
                else nextState = AIState.Idle;
            }
            else
            {
                // 적인 경우(플레이어 포함) 사거리에 따라 결정
                if (dist <= entity.Stats.ATKRANGE) nextState = AIState.Attack;
                else nextState = AIState.Follow;
            }
        }

        currentState = nextState;
    }

    protected override void OnIdle(BaseEntity entity)
    {
        StopNavAgent(entity);
        atkTimer = 0f;
    }

    protected override void OnFollow(BaseEntity entity)
    {
        var agent = entity.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.isStopped = false;
            agent.speed = entity.Stats.MOVESPEED;
            agent.SetDestination(target.position);
        }
        atkTimer = 0f;
    }

    protected override void OnAttack(BaseEntity entity)
    {
        StopNavAgent(entity);

        atkTimer += Time.deltaTime;
        if (atkTimer >= entity.Stats.ATKSPD)
        {
            // [복구] 자식 클래스에서 오버라이드할 수 있도록 전용 메서드 호출
            ExecuteBasicAttack(entity);
            atkTimer = 0f;
        }
    }

    protected virtual void ExecuteBasicAttack(BaseEntity entity)
    {
        // 기본값: 근접 공격 수행
        ExecuteAttack(entity, target);
    }
}
