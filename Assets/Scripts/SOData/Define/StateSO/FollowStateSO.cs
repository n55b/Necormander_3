using UnityEngine;

[CreateAssetMenu(menuName = "FSM/State/Follow")]
public class FollowStateSO : FSMStateSO
{
    public override void Enter(EntityFSM fsm) { }

    [Header("Movement Settings")]
    [SerializeField] private float separationRadius = 0.6f; // 아주 가까울 때만 밀어냄
    [SerializeField] private float separationWeight = 0.5f; // 밀어내는 힘 대폭 약화

    public override void Execute(EntityFSM fsm)
    {
        if (fsm.target == null)
        {
            if (fsm.rb != null) fsm.rb.linearVelocity = Vector2.zero;
            return;
        }

        float speed = fsm.stats.MOVESPEED;
        float distToCenter = Vector3.Distance(fsm.transform.position, fsm.target.position);
        float atkRange = fsm.stats.ATKRANGE;

        // 1. 공격 사거리 안에 들어왔다면 즉시 정지 (공격 상태 전환 유도)
        if (distToCenter <= atkRange)
        {
            if (fsm.rb != null) fsm.rb.linearVelocity = Vector2.zero;
            return;
        }

        // 2. 이동 방향 계산 (적의 중심 방향)
        Vector2 moveDir = ((Vector2)fsm.target.position - (Vector2)fsm.transform.position).normalized;

        // 3. 최소한의 분산 (겹침 방지)
        Vector2 separation = ComputeSeparation(fsm);

        // 4. 최종 속도 즉시 적용 (Lerp 없이 즉각 반응)
        if (fsm.rb != null)
        {
            Vector2 finalDir = (moveDir + separation * separationWeight).normalized;
            fsm.rb.linearVelocity = finalDir * speed;
        }
    }

    private Vector2 ComputeSeparation(EntityFSM fsm)
    {
        Vector2 v = Vector2.zero;
        Collider2D[] neighbors = Physics2D.OverlapCircleAll(fsm.transform.position, separationRadius);
        int count = 0;

        foreach (var col in neighbors)
        {
            if (col.gameObject == fsm.gameObject) continue;

            if (col.GetComponent<EntityFSM>() != null)
            {
                Vector2 diff = (Vector2)fsm.transform.position - (Vector2)col.transform.position;
                float d = diff.magnitude;
                if (d < 0.1f) d = 0.1f;
                v += diff.normalized / d;
                count++;
            }
        }
        return count > 0 ? v / count : Vector2.zero;
    }

    public override void Exit(EntityFSM fsm) { }
}
