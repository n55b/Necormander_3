using UnityEngine;

/// <summary>
/// 투사체를 발사하는 원거리 유닛용 AI 패턴입니다.
/// </summary>
[CreateAssetMenu(fileName = "RangedAIPattern", menuName = "Necromancer/AI/RangedPattern")]
public class RangedAIPatternSO : RangedBaseAIPatternSO
{
    [Header("원거리 공격 설정")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 15f;
    [SerializeField] private float lifeTime = 3f;
    [SerializeField] private float launchOffset = 0.5f;

    [Header("원거리 조준선 설정")]
    [Tooltip("화살을 쏘기 전 궤적을 보여줄 조준선 프리팹 (콜라이더가 없는 얇은 BaseHitBox를 권장합니다)")]
    [SerializeField] private GameObject aimLinePrefab;

    private void OnEnable()
    {
        // 원거리 유닛은 기본적으로 근접용 장판(Telegraph)을 자동 생성하지 않습니다.
        spawnTelegraph = false;
    }

    protected override void OnWindupStart(BaseEntity entity, float windupTime)
    {
        if (aimLinePrefab != null && entity.Target != null)
        {
            Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);
            GameObject aimLine = Instantiate(aimLinePrefab, spawnPos, Quaternion.identity);

            var hitbox = aimLine.GetComponent<BaseHitBox>();
            if (hitbox != null)
            {
                // 데미지 0짜리 가짜 페이로드 (시각적 표시 용도)
                DamageInfo dummyInfo = new DamageInfo(0f, DamageType.Physical, entity.gameObject, false, 0f, false);
                
                // windupTime 동안 차오르도록 설정
                // 발사 즉시 사라져야 하므로 duration을 매우 짧게 주거나 0으로 설정
                hitbox.Init(dummyInfo, 0, 0.1f, windupTime, entity.team == Team.Ally);
                
                // 취소 시 같이 지워질 수 있도록 엔티티에 등록
                entity.SetActiveHitbox(hitbox);
            }
        }
    }

    protected override void OnWindupUpdate(BaseEntity entity)
    {
        base.OnWindupUpdate(entity);

        if (entity.ActiveHitbox != null && entity.Target != null)
        {
            Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);
            Vector2 dir = ((Vector2)entity.Target.position - spawnPos).normalized;
            
            // 1. 회전 업데이트 (타겟이 움직이면 쫓아가며 록온)
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            entity.ActiveHitbox.transform.rotation = Quaternion.Euler(0, 0, angle);

            // 2. 박스 크기(길이) 업데이트 (현재 타겟 위치까지만 딱 맞춰서 그려짐)
            float currentDistToTarget = Vector2.Distance(spawnPos, entity.Target.position);
            entity.ActiveHitbox.transform.localScale = new Vector3(currentDistToTarget, 1f, 1f);
        }
    }

    protected override void ExecuteBasicAttack(BaseEntity entity)
    {
        if (projectilePrefab == null || entity.Target == null) return;

        Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);
        
        // 조준선(AimLine)이 활성화되어 있다면, 타겟의 새 위치가 아니라 '처음 록온했던 조준선 방향'으로 쏩니다.
        Vector3 shootTargetPos = entity.Target.position;
        if (entity.ActiveHitbox != null)
        {
            // 조준선의 방향(transform.right)을 가져와 실제 비행 거리만큼 연장
            float maxTravelDistance = projectileSpeed * lifeTime;
            shootTargetPos = spawnPos + (Vector2)(entity.ActiveHitbox.transform.right * maxTravelDistance);
        }

        GameObject projectileObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        Projectile projectile = projectileObj.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.Init(shootTargetPos, entity.Stats.ATK, entity.opponentLayer, entity.gameObject, projectileSpeed, lifeTime);
        }
    }
}
