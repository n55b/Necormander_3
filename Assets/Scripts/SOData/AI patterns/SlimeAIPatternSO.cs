using UnityEngine;

/// <summary>
/// 슬라임 전용 AI 패턴입니다.
/// 기본적으로 전사(BaseAIPatternSO)와 동일하게 주변 적을 추적하고 근접 공격을 수행하며,
/// 죽었을 때 지정된 작은 슬라임으로 분열하는 로직을 포함합니다.
/// </summary>
[CreateAssetMenu(fileName = "SlimeAIPattern", menuName = "Necromancer/AI/SlimePattern")]
public class SlimeAIPatternSO : BaseAIPatternSO
{
    [Header("Slime Split Settings")]
    [Tooltip("죽었을 때 소환할 작은 슬라임 프리팹입니다. 만약 비워두면(작은 슬라임이면) 분열하지 않습니다.")]
    public GameObject smallSlimePrefab;
    public int splitCount = 2;
    public float spawnRadius = 0.5f;


    public override void Init(BaseEntity entity)
    {
        base.Init(entity);

        var health = entity.GetComponentInChildren<CharacterHealth>();
        if (health != null)
        {
            // 람다를 사용해 매개변수 공유 문제를 피함
            health.OnDeath += () => HandleDeath(entity);
        }
    }

    private void HandleDeath(BaseEntity diedEntity)
    {
        if (diedEntity == null) return;

        Debug.Log($"<color=yellow>[SlimeSplit]</color> {diedEntity.gameObject.name} died. Attempting split. Spawner: {(diedEntity.Spawner != null ? diedEntity.Spawner.gameObject.name : "Null")}");

        // 프리팹이 등록되어 있지 않거나(작은 슬라임), 엔티티가 파괴된 상태라면 실행하지 않음
        if (smallSlimePrefab != null)
        {
            RoomInstance room = diedEntity.GetComponentInParent<RoomInstance>();
            Debug.Log($"<color=yellow>[SlimeSplit]</color> RoomInstance found in parent: {(room != null ? room.gameObject.name : "Null")}");

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
                Debug.Log($"<color=yellow>[SlimeSplit]</color> Nearest RoomInstance search result: {(room != null ? room.gameObject.name : "Null")} (Distance: {minDist})");
            }

            for (int i = 0; i < splitCount; i++)
            {
                // 주변 반경 내 무작위 위치에 흩뿌려 소환
                Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * spawnRadius;
                Vector3 spawnPos = diedEntity.transform.position + (Vector3)randomOffset;
                GameObject smallSlimeObj = Instantiate(smallSlimePrefab, spawnPos, Quaternion.identity);
                
                if (smallSlimeObj != null)
                {
                    if (smallSlimeObj.TryGetComponent<BaseEntity>(out var smallEntity))
                    {
                        // 1. 프리팹에 오버라이드된 minionData를 기반으로 즉시 Initialize 처리
                        MinionDataSO data = smallEntity.MinionData;
                        if (data != null)
                        {
                            smallEntity.Initialize(data);
                        }

                        // 2. 부모 슬라임의 방(RoomInstance)을 찾아 동적 등록하여 방 클리어 판정에 귀속시킴
                        if (room != null)
                        {
                            var normalEvent = room.GetComponentInChildren<NormalRoomEvent>();
                            if (normalEvent != null)
                            {
                                normalEvent.RegisterActiveEnemy(smallSlimeObj);
                                Debug.Log($"<color=yellow>[SlimeSplit]</color> Registered {smallSlimeObj.name} to NormalRoomEvent: {normalEvent.gameObject.name}");
                            }

                            var eliteEvent = room.GetComponentInChildren<EliteRoomEvent>();
                            if (eliteEvent != null)
                            {
                                eliteEvent.RegisterActiveEnemy(smallSlimeObj);
                                Debug.Log($"<color=yellow>[SlimeSplit]</color> Registered {smallSlimeObj.name} to EliteRoomEvent: {eliteEvent.gameObject.name}");
                            }

                            var dynamicSpawner = room.GetComponentInChildren<DynamicEnemySpawner>();
                            if (dynamicSpawner != null)
                            {
                                smallEntity.Spawner = dynamicSpawner;
                                dynamicSpawner.RegisterActiveEnemy(smallSlimeObj);
                                Debug.Log($"<color=yellow>[SlimeSplit]</color> Registered {smallSlimeObj.name} to DynamicEnemySpawner: {dynamicSpawner.gameObject.name}");
                            }
                        }
                        else if (diedEntity.Spawner != null)
                        {
                            smallEntity.Spawner = diedEntity.Spawner;
                            diedEntity.Spawner.RegisterActiveEnemy(smallSlimeObj);
                            Debug.Log($"<color=yellow>[SlimeSplit]</color> Registered {smallSlimeObj.name} to Spawner fallback");
                        }
                        else
                        {
                            Debug.LogWarning($"<color=red>[SlimeSplit]</color> Failed to find any RoomInstance or Spawner to register {smallSlimeObj.name}!");
                        }
                    }
                }
            }
        }
    }

}
