using UnityEngine;

/// <summary>
/// 사망 시 서로 다른 외형(프리팹)을 가진 두 개체로 분화하는 AI 패턴입니다.
/// 예: LionMask가 사망하면 서로 다르게 생긴 LionMask_A, LionMask_B 두 마리로 갈라짐.
/// SlimeAIPatternSO와 분열 컨셉은 같지만, 같은 프리팹을 N번 복제하는 대신
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
        if (diedEntity == null) return;

        Debug.Log($"<color=orange>[DualSplit]</color> {diedEntity.gameObject.name} died. Attempting split.");

        RoomInstance room = diedEntity.GetComponentInParent<RoomInstance>();
        Debug.Log($"<color=orange>[DualSplit]</color> RoomInstance found in parent: {(room != null ? room.gameObject.name : "Null")}");

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
            Debug.Log($"<color=orange>[DualSplit]</color> Nearest RoomInstance search result: {(room != null ? room.gameObject.name : "Null")} (Distance: {minDist})");
        }

        SpawnSplit(diedEntity, room, splitPrefabA, -1f);
        SpawnSplit(diedEntity, room, splitPrefabB, 1f);
    }

    private void SpawnSplit(BaseEntity diedEntity, RoomInstance room, GameObject prefab, float sideSign)
    {
        if (prefab == null) return;

        Vector3 spawnPos = diedEntity.transform.position + new Vector3(sideSign * spawnOffset, 0f, 0f);
        GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity);

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
                    Debug.Log($"<color=orange>[DualSplit]</color> Registered {obj.name} to NormalRoomEvent: {normalEvent.gameObject.name}");
                }

                var eliteEvent = room.GetComponentInChildren<EliteRoomEvent>();
                if (eliteEvent != null)
                {
                    eliteEvent.RegisterActiveEnemy(obj);
                    Debug.Log($"<color=orange>[DualSplit]</color> Registered {obj.name} to EliteRoomEvent: {eliteEvent.gameObject.name}");
                }

                var bossEvent = room.GetComponentInChildren<BossRoomEvent>();
                if (bossEvent != null)
                {
                    bossEvent.RegisterActiveEnemy(obj);
                    Debug.Log($"<color=orange>[DualSplit]</color> Registered {obj.name} to BossRoomEvent: {bossEvent.gameObject.name}");
                }
            }
            else
            {
                Debug.LogWarning($"<color=red>[DualSplit]</color> Failed to find any RoomInstance to register {obj.name}!");
            }
        }
    }

}
