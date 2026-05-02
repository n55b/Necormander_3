using UnityEngine;

/// <summary>
/// 투척 시의 물리적 위치 계산(벽 감지, 목표 보정 등)을 담당하는 클래스입니다.
/// </summary>
public class ThrowPhysics : MonoBehaviour
{
    private ThrowController _controller;
    public float safetyDistance = 0.75f;

    public void Init(ThrowController controller)
    {
        _controller = controller;
    }

    public Vector2 GetClampedTargetPos(Vector2 origin, Vector2 targetPos, ThrowCluster activeCluster)
    {
        int wallLayer = LayerMask.GetMask("Wall", "Obstacle");
        Vector2 direction = targetPos - origin;
        float distance = direction.magnitude;
        if (distance < 0.01f) return targetPos;

        float radius = (activeCluster != null) ? activeCluster.GetCurrentRadius() : 0.35f;
        RaycastHit2D hit = Physics2D.CircleCast(origin, radius, direction.normalized, distance, wallLayer);
        return (hit.collider != null) ? hit.centroid : targetPos;
    }

    public void UpdateHoldPosition(ThrowCluster activeCluster, Vector2 playerPos)
    {
        if (activeCluster == null || _controller.HoldPoint == null) return;

        Vector2 idealWorldPos = (Vector2)_controller.HoldPoint.position;
        int forbiddenLayers = LayerMask.GetMask("Wall", "Obstacle", "BackGround");
        
        float clusterRadius = activeCluster.GetCurrentRadius();
        float totalThreshold = clusterRadius + safetyDistance;
        Vector2 currentSafePos = idealWorldPos;

        for (int i = 0; i < 10; i++)
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(currentSafePos, totalThreshold, forbiddenLayers);
            if (hits.Length == 0) break;

            Vector2 combinedEscape = Vector2.zero;
            foreach (var hit in hits)
            {
                Vector2 closest = hit.ClosestPoint(currentSafePos);
                bool isInside = hit.OverlapPoint(currentSafePos);
                Vector2 dir = isInside ? (playerPos - currentSafePos).normalized : (currentSafePos - closest).normalized;
                if (dir.sqrMagnitude < 0.001f) dir = (playerPos - currentSafePos).normalized;
                if (dir == Vector2.zero) dir = Vector2.up;
                
                float depth = isInside ? totalThreshold : totalThreshold - Vector2.Distance(currentSafePos, closest);
                if (depth > 0) combinedEscape += dir * depth;
            }
            if (combinedEscape.sqrMagnitude > 0.0001f) currentSafePos += combinedEscape;
            else break;
        }

        activeCluster.transform.position = Vector3.Lerp(activeCluster.transform.position, (Vector3)currentSafePos, Time.deltaTime * 100f);
    }
}
