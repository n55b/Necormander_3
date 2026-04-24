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
        // [1순위] 플레이어 탐색 및 현실적인 도달 가능성 체크
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            float distToPlayer = Vector2.Distance(fsm.transform.position, player.transform.position);
            
            // 탐지 범위 내에 플레이어가 있는 경우
            if (distToPlayer <= entity.detectRange)
            {
                if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
                {
                    // 플레이어 위치로의 경로 계산
                    fsm.agent.CalculatePath(player.transform.position, _testPath);
                    
                    // A. 경로가 완벽하게 뚫려있음
                    if (_testPath.status == NavMeshPathStatus.PathComplete)
                    {
                        fsm.target = player.transform;
                        return;
                    }
                    // B. 경로가 부분적임 (유닛 등에 가로막힘)
                    else if (_testPath.status == NavMeshPathStatus.PathPartial)
                    {
                        // 갈 수 있는 마지막 지점이 플레이어와 충분히 가깝다면 (공격 사거리 내) 
                        // 플레이어를 목표로 삼고 최대한 접근합니다.
                        Vector3 lastPoint = _testPath.corners[_testPath.corners.Length - 1];
                        float distFromPathEndToPlayer = Vector2.Distance(lastPoint, player.transform.position);

                        if (distFromPathEndToPlayer <= fsm.stats.ATKRANGE)
                        {
                            fsm.target = player.transform;
                            return;
                        }
                    }
                }
            }
        }

        // [2순위] 플레이어가 없거나, 물리적으로 플레이어 사거리 내에 진입할 수 없는 경우:
        // 주변의 아군 미니언 중 가장 가까운 대상을 선택
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
