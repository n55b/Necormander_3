using UnityEngine;

[CreateAssetMenu(menuName = "FSM/State/Follow")]
public class FollowStateSO : FSMStateSO
{
    public override void Enter(EntityFSM fsm) { }

    [Header("Soft Collision Settings")]
    [SerializeField] private float pushRadius = 0.8f;   // 밀어내기 반경 (유닛 크기와 비슷하게)
    [SerializeField] private float pushStrength = 2.0f; // 밀어내는 힘 (클수록 더 빨리 제자리를 찾음)

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

        // 1. 공격 범위 안에 있으면 기본적으로 멈춤
        if (distToCenter <= atkRange)
        {
            // 공격 중이라도 다른 아군과 겹쳤다면 살짝 밀려나서 자리를 잡도록 함 (소프트 밀기)
            Vector2 softPush = ComputeSoftPush(fsm);
            if (fsm.rb != null) fsm.rb.linearVelocity = softPush * pushStrength;
            return;
        }

        // 2. 이동 방향 (적 중심 방향)
        Vector2 moveDir = ((Vector2)fsm.target.position - (Vector2)fsm.transform.position).normalized;

        // 3. 소프트 밀기 힘 계산
        Vector2 softPushForce = ComputeSoftPush(fsm);

        // 4. 최종 속도 적용 (이동 방향 + 밀어내기)
        if (fsm.rb != null)
        {
            // 최종 벡터가 너무 강해지지 않게 제한
            Vector2 finalVelocity = (moveDir + softPushForce * pushStrength).normalized * speed;
            fsm.rb.linearVelocity = finalVelocity;
        }
    }

    private Vector2 ComputeSoftPush(EntityFSM fsm)
    {
        Vector2 pushDir = Vector2.zero;
        // 주변 아군들 탐색
        Collider2D[] neighbors = Physics2D.OverlapCircleAll(fsm.transform.position, pushRadius);
        int count = 0;

        foreach (var col in neighbors)
        {
            if (col.gameObject == fsm.gameObject) continue;

            // 같은 소환수나 적군 레이어인 경우에만 소프트 밀기 적용
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

    public override void Exit(EntityFSM fsm) { }
}
