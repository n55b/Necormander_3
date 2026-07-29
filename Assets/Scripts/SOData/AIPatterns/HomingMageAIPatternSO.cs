using UnityEngine;

/// <summary>
/// 플레이어를 추적하는 파이어볼을 발사하고 쿨타임 중에는 도망치는 원거리 마법사 AI 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "HomingMageAIPattern", menuName = "Necromancer/AI/HomingMagePattern")]
public class HomingMageAIPatternSO : BaseAIPatternSO
{
    [Header("추적 마법사 설정")]
    [SerializeField] private GameObject projectilePrefab; // TrackingFireball 스크립트 보유 프리팹
    [SerializeField] private float projectileSpeed = 10f;
    [SerializeField] private float lifeTime = 5f;
    [SerializeField] private float launchOffset = 0.5f;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        spawnTelegraph = false; // 원거리 추적 파이어볼 마법사는 장판 생성 불필요
    }

    protected override void UpdateTargeting(BaseEntity entity)
    {
        // [최우선] 플레이어 탐색 (사거리 무한 설정을 전제로 항상 타겟팅)
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            entity.Target = player.transform;
        }
        else
        {
            // 플레이어가 없으면 가장 가까운 아군 미니언
            base.UpdateTargeting(entity);
        }
    }

    protected override void ExecuteBasicAttack(BaseEntity entity)
    {
        if (projectilePrefab == null || entity.Target == null) return;

        Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);
        GameObject projectileObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        TrackingFireball fireball = projectileObj.GetComponent<TrackingFireball>();
        if (fireball != null)
        {
            // 플레이어를 타겟으로 지정하여 유도 비행하도록 Init
            fireball.Init(entity.Target, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, lifeTime);
        }
        else
        {
            // 예외 방어: 일반 Projectile 컴포넌트인 경우
            Projectile proj = projectileObj.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.Init(entity.Target.position, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, lifeTime);
            }
        }

        Debug.Log($"<color=magenta>[HomingMage Pattern]</color> {entity.name}가 추적 파이어볼 발사!");
    }

    protected override void OnWindupStart(BaseEntity entity, float windupTime)
    {
        // 원거리 추적 파이어볼 마법사는 장판 생성 불필요
    }
}
