using UnityEngine;

/// <summary>
/// 모든 보스 AI 패턴의 기반이 되는 클래스입니다.
/// 페이즈 전환 등 보스 공통 로직을 관리합니다.
///
/// BaseAIPatternSO 를 상속한다 — AIPatternSO 의 훅(UpdateTargeting/UpdateStateTransitions/OnIdle/
/// OnFollow/OnAttack)은 전부 빈 몸통이라, 여기서 AIPatternSO 를 직접 상속하면 보스는 Execute() 를
/// 통째로 override 하고 NavMesh 추격·공격 루틴을 각자 복붙하는 것 말고는 선택지가 없다(차저/워리어/
/// 아처/서머너가 실제로 그렇다). BaseAIPatternSO 를 끼워 두면 새 보스는 필요한 훅만 골라 덮어쓸 수
/// 있고, Execute() 를 override 하는 기존 보스들은 이 훅이 아예 호출되지 않으므로 동작이 바뀌지 않는다.
/// </summary>
public abstract class BossAIPatternSO : BaseAIPatternSO
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

            float distToPlayer = Vector2.Distance(candidate, entity.Target.position);
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
