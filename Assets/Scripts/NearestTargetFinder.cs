using UnityEngine;

public class NearestTargetFinder : MonoBehaviour
{
    [Header("탐색 설정")]
    public float detectionRadius = 10f;       // 탐색 범위
    public LayerMask targetLayer;             // 타겟 레이어 (예: Enemy 또는 Ally)
    public float scanInterval = 0.2f;         // 탐색 주기 (0.2초마다 실행)

    [Header("결과")]
    public Transform nearestTarget;

    // 성능을 위해 미리 할당 (최대 20개까지 주변 유닛 감지)
    private Collider2D[] results = new Collider2D[10];
    private float lastScanTime;
    private bool canScan = false;

    void Update()
    {
        // 최적화: 매 프레임이 아니라 지정된 주기마다만 실행
        if (Time.time >= lastScanTime + scanInterval)
        {
            lastScanTime = Time.time + Random.Range(-0.02f, 0.02f); // 미세한 랜덤값으로 연산 분산
            canScan = true;
        }
    }

    public Transform FindNearest(float distance)
    {
        if(!canScan)
            return null;

        detectionRadius = distance;

        // 1. 범위 내 특정 레이어만 추출 (공간 분할 활용)
        int count = Physics2D.OverlapCircleNonAlloc(transform.position, detectionRadius, results, targetLayer);

        if (count == 0)
        {
            nearestTarget = null;
            return nearestTarget;
        }

        Transform closest = null;
        float minSqrDistance = float.MaxValue;
        Vector3 currentPos = transform.position;

        for (int i = 0; i < count; i++)
        {
            // 본인은 제외
            if (results[i].gameObject == gameObject) continue;

            // 1. 유효성 검사 (무적 상태나 죽은 대상 제외)
            if (results[i].TryGetComponent<CharacterStat>(out var stat))
            {
                if (stat.IsDead || stat.Invincible) continue;
            }

            // 2. sqrMagnitude 사용 (루트 연산을 생략해 성능 최적화)
            Vector3 diff = results[i].transform.position - currentPos;
            float sqrDistance = diff.sqrMagnitude;

            if (sqrDistance < minSqrDistance)
            {
                minSqrDistance = sqrDistance;
                closest = results[i].transform;
            }
        }

        nearestTarget = closest;
        return nearestTarget;
    }

    // 에디터에서 탐색 범위 시각화
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}