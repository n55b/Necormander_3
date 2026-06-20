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

    private BaseEntity _myEntity;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _myEntity = entity;

        var health = entity.GetComponentInChildren<CharacterHealth>();
        if (health != null)
        {
            // 중복 구독 방지를 위해 한 번 빼주고 다시 등록
            health.OnDeath -= HandleDeath;
            health.OnDeath += HandleDeath;
        }
    }

    private void HandleDeath()
    {
        // 프리팹이 등록되어 있지 않거나(작은 슬라임), 엔티티가 파괴된 상태라면 실행하지 않음
        if (smallSlimePrefab != null && _myEntity != null)
        {
            for (int i = 0; i < splitCount; i++)
            {
                // 주변 반경 내 무작위 위치에 흩뿌려 소환
                Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * spawnRadius;
                Vector3 spawnPos = _myEntity.transform.position + (Vector3)randomOffset;
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

                        // 2. 부모 슬라임의 스포너에 동적 등록하여 방 클리어 판정에 귀속시킴
                        if (_myEntity.Spawner != null)
                        {
                            smallEntity.Spawner = _myEntity.Spawner;
                            _myEntity.Spawner.RegisterActiveEnemy(smallSlimeObj);
                        }
                    }
                }
            }
        }
    }
}
