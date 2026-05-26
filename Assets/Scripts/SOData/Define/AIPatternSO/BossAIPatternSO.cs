using UnityEngine;

/// <summary>
/// 모든 보스 AI 패턴의 기반이 되는 클래스입니다.
/// 페이즈 전환 등 보스 공통 로직을 관리합니다.
/// </summary>
public abstract class BossAIPatternSO : AIPatternSO
{
    [Header("Boss Phase Settings")]
    public float phase2Threshold = 0.5f; // 페이즈 2 전환 체력 비율
    protected int currentPhase = 1;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        currentPhase = 1;
    }

    protected void UpdatePhase(BaseEntity entity)
    {
        if (entity.Stats == null || entity.Stats.Health == null) return;

        float hpRatio = entity.Stats.Health.CurHP / entity.Stats.Health.MaxHP;
        if (currentPhase == 1 && hpRatio <= phase2Threshold)
        {
            currentPhase = 2;
            OnPhaseChanged(entity, 2);
        }
    }

    protected virtual void OnPhaseChanged(BaseEntity entity, int newPhase)
    {
        Debug.Log($"<color=red>[Boss]</color> Phase Changed to <b>{newPhase}</b>!");
    }

    protected RoomInstance GetCurrentRoom(BaseEntity entity)
    {
        foreach (var room in FindObjectsByType<RoomInstance>(FindObjectsSortMode.None))
        {
            Bounds bounds = new Bounds((Vector2)room.transform.position + room.centerOffset, new Vector3(room.roomSize.x, room.roomSize.y, 100f));
            if (bounds.Contains(entity.transform.position))
            {
                return room;
            }
        }
        return null;
    }

    protected Vector2 GetTacticalPosition(BaseEntity entity, Transform target)
    {
        RoomInstance room = GetCurrentRoom(entity);
        if (room == null) return entity.transform.position;

        Vector2 roomCenter = (Vector2)room.transform.position + room.centerOffset;
        Vector2 extents = new Vector2(Mathf.Max(0, room.roomSize.x / 2f - 2f), Mathf.Max(0, room.roomSize.y / 2f - 2f)); // 벽에서 2만큼 띄움

        Vector2 bestPos = entity.transform.position;
        float maxDist = -1f;

        // 방 안의 랜덤한 위치 5개를 뽑아서 그 중 플레이어와 가장 먼 위치를 선택
        for (int i = 0; i < 5; i++)
        {
            float randX = Random.Range(-extents.x, extents.x);
            float randY = Random.Range(-extents.y, extents.y);
            Vector2 candidate = roomCenter + new Vector2(randX, randY);

            float distToPlayer = Vector2.Distance(candidate, target.position);
            if (distToPlayer > maxDist)
            {
                maxDist = distToPlayer;
                bestPos = candidate;
            }
        }
        
        // 갈 수 있는 경로인지 확인(NavMesh)
        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(bestPos, out hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
        {
            return hit.position;
        }

        return bestPos;
    }
}
