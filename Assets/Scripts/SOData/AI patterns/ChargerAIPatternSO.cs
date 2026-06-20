using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 플레이어를 포착하면 궁수의 조준선 프리팹을 띄워 락온을 한 뒤 고속 돌진하는 몬스터 AI 패턴입니다.
/// 벽이나 플레이어에 충돌하면 자신은 1.5초간 기절(Stun)하고, 플레이어 충돌 시 피해를 입힙니다.
/// </summary>
[CreateAssetMenu(fileName = "ChargerAIPattern", menuName = "Necromancer/AI/ChargerPattern")]
public class ChargerAIPatternSO : BaseAIPatternSO
{
    [Header("돌진 설정")]
    [SerializeField] private GameObject aimLinePrefab; // 궁수용 aimLinePrefab 재사용
    [SerializeField] private float launchOffset = 0.5f;
    [SerializeField] private float windupTime = 1.0f; // 돌진 준비 시간 (락온 유지 시간)
    [SerializeField] private float chargeSpeedMultiplier = 3.0f; // 기본 이속 대비 돌진 배수

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
            base.UpdateTargeting(entity);
        }
    }

    protected override System.Collections.IEnumerator AttackRoutine(BaseEntity entity)
    {
        entity.IsAttacking = true;
        entity.HasFiredHitEvent = false;
        entity.HasFiredAttackEndEvent = false;

        GameObject aimLine = null;
        BaseHitBox aimHitbox = null;

        // [1] 돌진 방향 조준선 생성 (락온 연출)
        if (aimLinePrefab != null && entity.Target != null)
        {
            Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);
            aimLine = Instantiate(aimLinePrefab, spawnPos, Quaternion.identity);
            aimHitbox = aimLine.GetComponent<BaseHitBox>();
            if (aimHitbox != null)
            {
                DamageInfo dummyInfo = new DamageInfo(0f, DamageType.Physical, entity.gameObject, false, 0f, false);
                aimHitbox.Init(dummyInfo, 0, 0.1f, windupTime);
                entity.SetActiveHitbox(aimHitbox);
            }
        }

        // [2] 선딜레이 대기 (플레이어를 실시간으로 조준 록온)
        float timeout = windupTime;
        Vector2 chargeDir = Vector2.right;

        while (timeout > 0f)
        {
            if (entity.Target == null) break;
            timeout -= Time.deltaTime;

            Vector2 spawnPos = (Vector2)entity.transform.position + (Vector2.up * launchOffset);
            chargeDir = ((Vector2)entity.Target.position - spawnPos).normalized;

            if (aimHitbox != null)
            {
                float angle = Mathf.Atan2(chargeDir.y, chargeDir.x) * Mathf.Rad2Deg;
                aimHitbox.transform.rotation = Quaternion.Euler(0, 0, angle);

                float currentDist = Vector2.Distance(spawnPos, entity.Target.position);
                aimHitbox.transform.localScale = new Vector3(currentDist, 1f, 1f);
            }

            yield return null;
        }

        // 조준 종료 후 조준선 정리
        if (aimLine != null) Destroy(aimLine);
        entity.SetActiveHitbox(null);

        // [3] 돌진 돌입
        var agent = entity.GetComponent<NavMeshAgent>();
        bool wasAgentEnabled = agent != null && agent.enabled;
        if (wasAgentEnabled)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;
        }

        // 물리 겹침 방지를 위해 콜라이더 트리거 설정
        var chargerCollider = entity.GetComponent<Collider2D>();
        bool originalIsTrigger = false;
        if (chargerCollider != null)
        {
            originalIsTrigger = chargerCollider.isTrigger;
            chargerCollider.isTrigger = true;
        }

        var rb = entity.GetComponent<Rigidbody2D>();
        float chargeSpeed = entity.Stats.MOVESPEED * chargeSpeedMultiplier;
        float maxChargeDuration = 3.0f; // 안전 타임아웃
        float chargeElapsed = 0f;

        LayerMask wallMask = LayerMask.GetMask("Wall", "Obstacle");
        LayerMask playerMask = LayerMask.GetMask("Player");
        LayerMask hitMask = wallMask | playerMask;

        bool hasHitObstacle = false;

        while (chargeElapsed < maxChargeDuration)
        {
            chargeElapsed += Time.deltaTime;
            if (rb != null)
            {
                rb.linearVelocity = chargeDir * chargeSpeed;
            }

            // 전방 충돌 판정 (터널링 방지를 위한 CircleCast 사용)
            float checkDistance = chargeSpeed * Time.deltaTime + 0.1f;
            RaycastHit2D hit = Physics2D.CircleCast(entity.transform.position, 0.6f, chargeDir, checkDistance, hitMask);
            if (hit.collider != null)
            {
                hasHitObstacle = true;
                
                // 플레이어에 닿은 경우에만 데미지
                if (((1 << hit.collider.gameObject.layer) & playerMask) != 0)
                {
                    var playerHealth = hit.collider.GetComponentInChildren<CharacterHealth>();
                    if (playerHealth == null) playerHealth = hit.collider.GetComponentInParent<CharacterHealth>();
                    if (playerHealth != null)
                    {
                        DamageInfo chargeDmg = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject);
                        playerHealth.GetDamage(chargeDmg);
                    }
                }
                break;
            }

            yield return null;
        }

        // 돌진 정지
        if (rb != null) rb.linearVelocity = Vector2.zero;
        if (chargerCollider != null)
        {
            chargerCollider.isTrigger = originalIsTrigger;
        }
        if (wasAgentEnabled && agent != null)
        {
            agent.enabled = true;
            agent.isStopped = false;
        }

        entity.IsAttacking = false;

        // 충돌했다면 1.5초간 기절 처리
        if (hasHitObstacle && entity.Stats != null && entity.Stats.Status != null)
        {
            entity.Stats.Status.SetDebuffBool(DebuffBoolType.Stunned, 1.5f);
        }
    }
}
