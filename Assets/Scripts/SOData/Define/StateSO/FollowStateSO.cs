using UnityEngine;
using UnityEngine.AI;

[CreateAssetMenu(menuName = "FSM/State/Follow")]
public class FollowStateSO : FSMStateSO
{
    [Header("Soft Collision Settings")]
    [SerializeField] private float pushRadius = 0.8f;   // 밀어내기 반경 (유닛 크기와 비슷하게)
    [SerializeField] private float pushStrength = 2.0f; // 밀어내는 힘 (클수록 더 빨리 제자리를 찾음)

    public override void Enter(EntityFSM fsm) 
    {
        // NavMeshAgent 설정 동기화 및 이동 재개
        if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
        {
            fsm.agent.isStopped = false;
            fsm.agent.speed = fsm.stats.MOVESPEED;
        }
    }

    public override void Execute(EntityFSM fsm)
    {
        if (fsm.target == null)
        {
            // 타겟이 없으면 모든 이동 정지
            StopAllMovement(fsm);
            return;
        }

        float speed = fsm.stats.MOVESPEED;
        float distToTarget = Vector2.Distance(fsm.transform.position, fsm.target.position);
        float atkRange = fsm.stats.ATKRANGE;

        // 1. 공격 범위 안에 있으면 기본적으로 멈춤
        if (distToTarget <= atkRange)
        {
            // NavMesh 이동은 멈추고 제자리 유지
            if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
            {
                fsm.agent.isStopped = true;
                fsm.agent.velocity = Vector2.zero;
            }

            // 공격 중이라도 다른 아군과 겹쳤다면 살짝 밀려나서 자리를 잡도록 함 (소프트 밀기)
            Vector2 softPush = ComputeSoftPush(fsm);
            if (fsm.rb != null) fsm.rb.linearVelocity = softPush * pushStrength;
            return;
        }

        // 2. 공격 범위 밖이면 NavMesh를 이용해 영리하게 추적
        if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
        {
            fsm.agent.isStopped = false;
            fsm.agent.SetDestination(fsm.target.position);

            // NavMeshAgent가 이동을 주도할 때는 Rigidbody 속도가 방해되지 않게 초기화
            if (fsm.rb != null) fsm.rb.linearVelocity = Vector2.zero;
        }
        else
        {
            // [폴백 로직] 에이전트가 비활성화된 경우(예: 던져진 직후 등) 기존의 직선 이동 방식 사용
            Vector2 moveDir = ((Vector2)fsm.target.position - (Vector2)fsm.transform.position).normalized;
            Vector2 softPushForce = ComputeSoftPush(fsm);

            if (fsm.rb != null)
            {
                Vector2 finalVelocity = (moveDir + softPushForce * pushStrength).normalized * speed;
                fsm.rb.linearVelocity = finalVelocity;
            }
        }
    }

    private void StopAllMovement(EntityFSM fsm)
    {
        if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
        {
            fsm.agent.isStopped = true;
            fsm.agent.velocity = Vector2.zero;
        }
        if (fsm.rb != null) fsm.rb.linearVelocity = Vector2.zero;
    }

    private Vector2 ComputeSoftPush(EntityFSM fsm)
    {
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");

        // 0. 내가 비행 중이거나, 레이어를 찾을 수 없으면 밀어내기 계산 안 함
        if (fsm.gameObject.layer == flyingLayer || flyingLayer == -1) 
            return Vector2.zero;

        Vector2 pushDir = Vector2.zero;
        // 주변 아군들 탐색
        Collider2D[] neighbors = Physics2D.OverlapCircleAll(fsm.transform.position, pushRadius);
        int count = 0;

        foreach (var col in neighbors)
        {
            if (col.gameObject == fsm.gameObject) continue;

            // 1. 상대방이 비행 중이면 무시
            if (col.gameObject.layer == flyingLayer) continue;

            // 2. 같은 소환수나 적군 레이어인 경우에만 소프트 밀기 적용
            if (col.gameObject.layer == fsm.gameObject.layer)
            {
                Vector2 diff = (Vector2)fsm.transform.position - (Vector2)col.transform.position;
                float distance = diff.magnitude;

                if (distance < pushRadius)
                {
                    // 겹친 깊이에 따라 밀어내는 힘을 줍니다 (가까울수록 강하게)
                    float strength = 1.0f - (distance / pushRadius);
                    pushDir += diff.normalized * strength;
                    count++;
                }
            }
        }

        return count > 0 ? pushDir : Vector2.zero;
    }

    public override void Exit(EntityFSM fsm) 
    {
        // 상태를 탈출할 때 이동 정지
        if (fsm.agent != null && fsm.agent.isActiveAndEnabled)
        {
            fsm.agent.isStopped = true;
        }
    }
}
