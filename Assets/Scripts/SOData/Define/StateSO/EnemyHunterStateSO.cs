using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 플레이어를 우선적으로 사냥하는 적군 전용 공격 상태 에셋입니다.
/// 별도의 컨트롤러 없이 이 상태 하나만으로 타겟팅, 추격, 공격을 모두 처리합니다.
/// </summary>
[CreateAssetMenu(fileName = "EnemyHunterState", menuName = "Necromancer/Attack States/EnemyHunter")]
public class EnemyHunterStateSO : AttackStateSO
{
    private NavMeshPath _testPath;

    public override void Execute(EntityFSM fsm)
    {
        if (_testPath == null) _testPath = new NavMeshPath();
        
        var entity = fsm.GetComponent<BaseEntity>();
        if (entity == null) return;

        // 1. 최적의 사냥 대상 결정 (플레이어 우선 + 도달 가능성 확인)
        FindBestHunterTarget(fsm, entity);

        // 2. 타겟이 결정되었다면 거리에 따른 행동 수행
        if (fsm.target != null)
        {
            float dist = Vector2.Distance(fsm.transform.position, fsm.target.position);

            // [공격 단계] 사거리 안이면 정지 후 공격
            if (dist <= fsm.stats.ATKRANGE)
            {
                if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
                {
                    fsm.agent.isStopped = true;
                    fsm.agent.velocity = Vector2.zero;
                }

                fsm.atkTimer += Time.deltaTime;
                if (fsm.atkTimer >= fsm.stats.ATKSPD)
                {
                    PerformAction(fsm);
                    fsm.atkTimer = 0.0f;
                }
            }
            // [추격 단계] 사거리 밖이면 NavMesh로 추격
            else
            {
                if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
                {
                    fsm.agent.isStopped = false;
                    fsm.agent.SetDestination(fsm.target.position);
                }
                fsm.atkTimer = 0.0f;
            }
        }
        else
        {
            // 타겟이 없으면 정지
            if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
            {
                fsm.agent.isStopped = true;
            }
            fsm.atkTimer = 0.0f;
        }
    }

    private void FindBestHunterTarget(EntityFSM fsm, BaseEntity entity)
    {
        // [1순위] 플레이어 탐색
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            float distToPlayer = Vector2.Distance(fsm.transform.position, player.transform.position);
            
            // 탐지 범위 내에 플레이어가 있는 경우
            if (distToPlayer <= entity.detectRange)
            {
                if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
                {
                    // NavMesh 상으로 완전히 도달 가능한지(길이 뚫려 있는지) 체크
                    fsm.agent.CalculatePath(player.transform.position, _testPath);
                    
                    // PathComplete일 때만 플레이어를 타겟으로 삼음
                    if (_testPath.status == NavMeshPathStatus.PathComplete)
                    {
                        fsm.target = player.transform;
                        return;
                    }
                }
            }
        }

        // [2순위] 플레이어가 없거나 길이 막힌 경우: 가장 가까운 아군(미니언) 탐색
        // BaseEntity의 NearestTargetFinder를 그대로 활용
        Transform nearestAlly = entity.TargetFinder.FindNearest(entity.detectRange);
        fsm.target = nearestAlly;
    }

    protected override void PerformAction(EntityFSM fsm)
    {
        // 부모(AttackStateSO)의 기본 공격 로직 실행 (상대에 따라 Damage/ExecuteAttack 분기)
        base.PerformAction(fsm);
    }

    public override void Exit(EntityFSM fsm)
    {
        base.Exit(fsm);
        if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
        {
            fsm.agent.isStopped = false; // 상태 탈출 시 에이전트 상태 초기화
        }
    }
}
