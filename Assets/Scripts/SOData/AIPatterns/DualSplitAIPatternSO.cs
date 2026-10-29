using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 사망 시 서로 다른 외형(프리팹)을 가진 두 개체로 분화하는 AI 패턴입니다.
/// 예: LionMask가 사망하면 서로 다르게 생긴 LionMask_A, LionMask_B 두 마리로 갈라짐.
/// 서로 다른 두 프리팹을 각각 1번씩 스폰합니다.
///
/// 주의: 이 SO 에셋은 같은 타입의 모든 적 인스턴스가 공유합니다.
/// 그래서 죽은 개체 정보를 필드에 저장해두지 않고, 사망 이벤트 콜백에 클로저로
/// 바로 넘겨서 사용합니다. (동시에 여러 마리가 존재해도 서로 안 섞이게)
/// </summary>
[CreateAssetMenu(fileName = "DualSplitAIPattern", menuName = "Necromancer/AI/DualSplitPattern")]
public class DualSplitAIPatternSO : BaseAIPatternSO
{
    [Header("분화 설정 (Dual Split)")]
    [Tooltip("분화 시 생성할 첫 번째 개체 프리팹입니다.")]
    public GameObject splitPrefabA;
    [Tooltip("분화 시 생성할 두 번째 개체 프리팹입니다.")]
    public GameObject splitPrefabB;
    [Tooltip("원본 위치 기준 좌우로 얼마나 떨어진 곳에 스폰할지 (서로 반대 방향으로 적용됩니다)")]
    public float spawnOffset = 0.5f;
    [InspectorName("분열 위치 보정 반경"), Min(0f)]
    [Tooltip("원하는 분열 위치가 벽·물에 걸리면 이 반경 안에서 안전한 자리를 찾습니다. 없으면 생성하지 않고 경고를 남깁니다.")]
    public float spawnSearchRadius = 2f;

    [Header("사자탈 전용 슬로우 설정")]
    [Range(0f, 1f), Tooltip("사자탈 및 미니 사자탈 평타 피격 시 플레이어의 이동속도 감속 비율입니다. (0.3 = 30% 감속)")]
    public float slowReduction = 0.3f;
    [Tooltip("평타 피격 시 슬로우 디버프 지속 시간(초)입니다.")]
    public float slowDuration = 3.0f;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);

        var health = entity.GetComponentInChildren<CharacterHealth>();
        if (health != null)
        {
            health.OnDeath += () => HandleDeath(entity);
        }
    }

    private void HandleDeath(BaseEntity diedEntity)
    {
        if (diedEntity == null || (splitPrefabA == null && splitPrefabB == null)) return;

        RoomInstance room = diedEntity.GetComponentInParent<RoomInstance>();

        // 부모 중 RoomInstance가 없다면 주변 반경 내 가장 가까운 RoomInstance 탐색
        if (room == null)
        {
            RoomInstance[] allRooms = FindObjectsByType<RoomInstance>(FindObjectsSortMode.None);
            float minDist = float.MaxValue;
            foreach (var r in allRooms)
            {
                float d = Vector2.Distance(diedEntity.transform.position, r.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    room = r;
                }
            }
        }

        Vector3 deathPosition = diedEntity.transform.position;
        GameObject first = SpawnSplit(diedEntity, deathPosition, room, splitPrefabA, -1f, null);
        SpawnSplit(diedEntity, deathPosition, room, splitPrefabB, 1f, first);
    }

    private GameObject SpawnSplit(BaseEntity diedEntity, Vector3 deathPosition, RoomInstance room,
                                 GameObject prefab, float sideSign, GameObject sibling)
    {
        if (prefab == null) return null;

        Vector3 desired = deathPosition + Vector3.right * (sideSign * spawnOffset);
        if (!TryFindSplitPosition(prefab, deathPosition, desired, sibling, out Vector3 spawnPos))
        {
            Debug.LogWarning($"[DualSplit] 안전한 분열 위치 없음: {diedEntity.name}#{diedEntity.gameObject.GetInstanceID()} -> {prefab.name}, 사망={deathPosition:F3}, 희망={desired:F3}", diedEntity);
            return null;
        }
        GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);
        if (Debug.isDebugBuild)
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, obj, "{0}",
                $"[DualSplit] {diedEntity.name}#{diedEntity.gameObject.GetInstanceID()} -> {obj.name}#{obj.GetInstanceID()}, 사망={deathPosition:F3}, 희망={desired:F3}, 생성={obj.transform.position:F3}");

        if (obj.TryGetComponent<BaseEntity>(out var splitEntity))
        {
            // 1. 프리팹에 등록된 minionData를 기반으로 즉시 Initialize 처리
            var data = splitEntity.MinionData;
            if (data != null)
            {
                splitEntity.Initialize(data);
            }

            // 2. 원본의 방(RoomInstance)을 찾아 동적 등록하여 방 클리어 판정에 귀속시킴
            if (room != null)
            {
                var normalEvent = room.GetComponentInChildren<NormalRoomEvent>();
                if (normalEvent != null)
                {
                    normalEvent.RegisterActiveEnemy(obj);
                }

                var eliteEvent = room.GetComponentInChildren<EliteRoomEvent>();
                if (eliteEvent != null)
                {
                    eliteEvent.RegisterActiveEnemy(obj);
                }

                var bossEvent = room.GetComponentInChildren<BossRoomEvent>();
                if (bossEvent != null)
                {
                    bossEvent.RegisterActiveEnemy(obj);
                }
            }
            else
            {
                Debug.LogWarning($"<color=red>[DualSplit]</color> Failed to find any RoomInstance to register {obj.name}!");
            }
        }
        return obj;
    }

    private bool TryFindSplitPosition(GameObject prefab, Vector3 origin, Vector3 desired,
                                      GameObject sibling, out Vector3 result)
    {
        result = origin;
        var agent = prefab.GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent != null ? agent.agentTypeID : 0,
            areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas
        };
        int waterArea = NavMesh.GetAreaFromName("UnsteppableArea");
        if (waterArea >= 0) filter.areaMask &= ~(1 << waterArea);
        if (!NavMesh.SamplePosition(origin, out NavMeshHit source, .5f, filter)) return false;

        // 분열 프리팹의 발 충돌체를 기준으로 검사한다. 비활성 프리팹의 bounds는 사용하지 않는다.
        SplitFootprint(prefab, out Vector2 offset, out float radius);
        Vector2 siblingCenter = Vector2.zero;
        float siblingRadius = 0f;
        if (sibling != null)
        {
            SplitFootprint(sibling, out Vector2 siblingOffset, out siblingRadius);
            siblingCenter = (Vector2)sibling.transform.position + siblingOffset;
        }

        const int Rings = 8, Directions = 16;
        for (int ring = 0; ring <= Rings; ring++)
        {
            float distance = Mathf.Max(0f, spawnSearchRadius) * ring / Rings;
            for (int i = 0; i < (ring == 0 ? 1 : Directions); i++)
            {
                float angle = i * Mathf.PI * 2f / Directions;
                Vector3 candidate = desired + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * distance;
                // 넓은 SamplePosition으로 벽 건너편까지 끌려가지 않도록 후보 근처만 허용한다.
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, .1f, filter)) continue;
                candidate = hit.position;
                if (Physics2D.Linecast(origin, candidate, Layers.WallMask).collider != null) continue;
                if (NavMesh.Raycast(source.position, candidate, out _, filter)) continue;
                Vector2 center = (Vector2)candidate + offset;
                if (!SkillCombatUtil.IsSafeBoxLanding(center, Vector2.one * radius * 2f)) continue;
                float separation = radius + siblingRadius + .04f;
                if (sibling != null && (center - siblingCenter).sqrMagnitude < separation * separation) continue;
                result = candidate;
                return true;
            }
        }
        return false;
    }

    private static void SplitFootprint(GameObject obj, out Vector2 offset, out float radius)
    {
        var circle = obj.GetComponent<CircleCollider2D>();
        Vector3 scale = obj.transform.lossyScale;
        offset = circle != null ? Vector2.Scale(circle.offset, new Vector2(scale.x, scale.y)) : Vector2.zero;
        radius = (circle != null ? circle.radius : .4f) * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
    }
}
