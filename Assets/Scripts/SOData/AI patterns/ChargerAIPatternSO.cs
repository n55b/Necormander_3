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
    [Tooltip("기존 조준선(락온 연출). 예고 레인이 같은 역할을 더 잘 하므로 기본 꺼짐. 0 데미지 더미 히트박스라 꺼도 판정에는 영향 없다.")]
    [SerializeField] private bool showAimLine = false;

    [SerializeField] private float launchOffset = 0.5f;
    [SerializeField] private float windupTime = 1.0f; // 돌진 준비 시간 (락온 유지 시간)
    [SerializeField] private float telegraphFlashLeadTime = 0.35f;
    [Header("돌진 예고 레인")]
    [Tooltip("돌진 경로를 바닥에 깔고, 두께가 차오르며 개시 타이밍을 알린다. 끄면 기존처럼 플래시만 사용.")]
    [SerializeField] private bool showChargeTelegraph = true;
    [Tooltip("배경 레인 색. '어디로 올지'를 알린다.")]
    [SerializeField] private Color chargeTelegraphColor = new Color(1f, 0f, 0f, 0.22f);
    [Tooltip("두께로 차오르는 게이지 색. '언제 올지'를 알린다. 배경보다 진해야 읽힌다.")]
    [SerializeField] private Color chargeTelegraphFillColor = new Color(1f, 0.3f, 0.1f, 0.7f);
    [Tooltip("레인 두께(월드 단위). 돌진 히트 판정 폭에 맞추면 예고가 정직해진다.")]
    [SerializeField] private float chargeTelegraphWidth = 0.9f;
    [Tooltip("돌진 안전 타임아웃. 최대 돌진 거리(= 돌진속도 x 이 시간) 계산에도 쓰인다.")]
    [SerializeField] private float maxChargeDuration = 3.0f;
    private GameObject _chargeTelegraph;

    /// <summary>돌진 예고 레인 제거. 정상 종료/취소 양쪽에서 호출된다.</summary>
    private void ClearChargeTelegraph()
    {
        if (_chargeTelegraph != null)
        {
            Destroy(_chargeTelegraph);
            _chargeTelegraph = null;
        }
    }

    // 피격/스턴으로 공격이 끊길 때. StopCoroutine 은 루틴 뒷정리를 실행하지 않는다.
    public override void OnAttackCancelled(BaseEntity entity)
    {
        base.OnAttackCancelled(entity);
        ClearChargeTelegraph();
    }

    // 엔티티 사망/씬 언로드로 브레인 클론이 파괴될 때.
    private void OnDisable()
    {
        ClearChargeTelegraph();
    }

 // [예고 문법] 돌진 개시 몇 초 전에 예고 플래시를 터뜨릴지 (전 몬스터 공통 반응 시간)

    [SerializeField] private float chargeSpeedMultiplier = 3.0f; // 기본 이속 대비 돌진 배수
    [SerializeField, Range(0.1f, 1.5f)] private float wallStopRadiusRatio = 0.55f; // [추가] 돌진 중 벽/장애물 감지용 CircleCast 반지름. 값을 낮추면 벽에 더 가까이 붙은 뒤에 멈춤
    [SerializeField, Range(0f, 0.3f)] private float wallCheckDistanceBuffer = 0.15f; // [추가] 벽 감지 거리에 매 프레임 고정으로 더해지는 여유값. 낮추면 벽에 더 붙어서 멈춤

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

        // [수정] Attack 애니메이션 재생 (기존엔 누락되어 있어 돌진 중 Attack 애니메이션이 재생되지 않았음)
        if (entity.Animator != null && entity.Animator.runtimeAnimatorController != null)
        {
            entity.Animator.speed = 1f;
            entity.Animator.Play("Attack", -1, 0f);
        }

        GameObject aimLine = null;
        BaseHitBox aimHitbox = null;

        // [1] 돌진 방향 조준선 생성 (락온 연출)
        if (showAimLine && aimLinePrefab != null && entity.Target != null)
        {
            // [수정] 월드가 아닌 시전자(entity) 자식으로 매달아 몬스터가 이동/넉백되어도 오프셋 지점에 조준선이 따라붙도록 처리
            aimLine = Instantiate(aimLinePrefab, entity.transform.position, Quaternion.identity, entity.transform);
            aimLine.transform.localPosition = Vector3.up * launchOffset;
            
            aimHitbox = aimLine.GetComponent<BaseHitBox>();
            if (aimHitbox != null)
            {
                DamageInfo dummyInfo = new DamageInfo(0f, DamageType.Physical, entity.gameObject, 0f);
                aimHitbox.Init(dummyInfo, 0, 0.1f, windupTime);
                entity.SetActiveHitbox(aimHitbox);
            }
        }

        // [2] 선딜레이 대기 (플레이어를 실시간으로 조준 록온)
        float timeout = windupTime;
        Vector2 chargeDir = Vector2.right;
        var telegraphVfb = entity.GetComponentInChildren<CharacterVisualFeedback>();
        bool telegraphFlashFired = false;
        // 스턴 등으로 코루틴이 끊겨도 OnAttackCancelled 에서 치울 수 있도록 인스턴스 필드에 보관.
        // (엔티티별 클론이라 몹끼리 섞이지 않는다 — BaseEntity._runtimeBrain)
        ClearChargeTelegraph(); // 이전 시도가 남긴 게 있으면 먼저 정리


        // 방향 인디케이터: 돌진 충전 중엔 멈춰 있어도(이동 velocity≈0) 실시간 재조준 방향을 가리키게 오버라이드.
        var dirIndicator = entity.GetComponentInChildren<EntityDirectionIndicator>();

        while (timeout > 0f)
        {
            if (entity.Target == null) break;
            timeout -= Time.deltaTime;

            // [예고 플래시] 돌진 개시 leadTime초 전, 하데스식 흰색 번쩍으로 타이밍 신호 (일반 몬스터 = 1펄스)
            if (!telegraphFlashFired && timeout <= telegraphFlashLeadTime)
            {
                telegraphFlashFired = true;
                telegraphVfb?.PlayTelegraphFlash(1);
            }


            Vector2 spawnPos = aimHitbox != null ? (Vector2)aimHitbox.transform.position : (Vector2)entity.transform.position + (Vector2.up * launchOffset);
            chargeDir = ((Vector2)entity.Target.position - spawnPos).normalized;

            // [수정] 차지 준비 중 실시간 플립: 본래의 LookAtTarget 공통 메서드를 사용하여 SpriteRenderer.flipX를 실시간 제어
            entity.LookAtTarget(entity.Target);
            dirIndicator?.SetAimOverride(chargeDir); // 인디케이터도 실시간 조준 방향으로 (돌진 개시 후엔 자동 만료 → 이동 방향 복귀)

            // [추가] 돌진 예고 레인: 경로는 배경으로 계속 보여주고, 개시 타이밍은 두께 게이지로 알린다.
            // 조준선(aimHitbox)은 길이가 대상과의 거리라 타이밍을 읽을 수 없었다.
            // 폭은 상수라 게이지가 항상 같은 속도로 차오르므로 회피 타이밍이 학습된다.
            if (showChargeTelegraph)
            {
                if (_chargeTelegraph == null)
                    _chargeTelegraph = BossTelegraph.SpawnRect(chargeTelegraphColor);

                // [수정] 레인은 플레이어에서 끊지 않고 통과시킨다.
                // 어디까지 위험한지 다 보여야 '어느 쪽으로 피할지'를 판단할 수 있다.
                // 벽/장애물에 막히는 지점까지, 막는 게 없으면 최대 돌진 거리까지.
                // (돌진 루프와 동일한 마스크·반지름이라 예고와 실제 정지 지점이 항상 일치한다)
                float laneLength = entity.Stats.MOVESPEED * chargeSpeedMultiplier * maxChargeDuration;
                RaycastHit2D laneBlock = Physics2D.CircleCast(
                    entity.transform.position,
                    wallStopRadiusRatio,
                    chargeDir,
                    laneLength,
                    LayerMask.GetMask("Wall", "Object"));
                if (laneBlock.collider != null)
                    laneLength = laneBlock.distance;
                float chargeProgress = windupTime > 0f ? 1f - Mathf.Clamp01(timeout / windupTime) : 1f;
                BossTelegraph.UpdateRectWithFill(_chargeTelegraph, spawnPos, chargeDir, laneLength, chargeTelegraphWidth, chargeProgress, chargeTelegraphFillColor);
            }

            if (aimHitbox != null)
            {
                // 1. 회전 업데이트 (타겟 추적 회전)
                float angle = Mathf.Atan2(chargeDir.y, chargeDir.x) * Mathf.Rad2Deg;
                aimHitbox.transform.localRotation = Quaternion.Euler(0, 0, angle);

                // 2. 박스 크기(길이) 업데이트 (현재 타겟 위치까지 조준선 신장)
                float currentDist = Vector2.Distance(spawnPos, entity.Target.position);
                aimHitbox.transform.localScale = new Vector3(currentDist, 1f, 1f);
            }

            yield return null;
        }

        // 조준 종료 후 조준선 정리
        if (aimLine != null) Destroy(aimLine);
        ClearChargeTelegraph();
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

        // 물리 겹침 방지를 위해 콜라이더 트리거 설정 (제거: 벽 뚫기를 물리적으로 막기 위해 isTrigger=false 고수)
        var chargerCollider = entity.GetComponent<Collider2D>();
        var rb = entity.GetComponent<Rigidbody2D>();
        float chargeSpeed = entity.Stats.MOVESPEED * chargeSpeedMultiplier;
        // maxChargeDuration 은 예고 레인 길이 계산과 공유하기 위해 필드로 올렸다.

        // [잔상] 돌진 구간에서만 잔상 방출 윈도우를 엽니다 (평타 넉백 등에는 잔상이 안 나오도록 명시적 제어).
        // 코루틴이 스턴/사망으로 끊겨도 윈도우가 자동 만료되므로 잔상이 켜진 채 남지 않습니다.
        var afterimage = entity.GetComponent<DashAfterimage>();
        afterimage?.BeginDash(maxChargeDuration + 0.5f);

        float chargeElapsed = 0f;

        // [수정] 물리 scale.x를 뒤흔들지 않고, 스프라이트 flipX만 돌진 물리 진행 방향에 정확히 동기화
        var entitySR = entity.GetComponentInChildren<SpriteRenderer>();
        if (entitySR != null)
        {
            if (chargeDir.x > 0.01f) entitySR.flipX = true;
            else if (chargeDir.x < -0.01f) entitySR.flipX = false;
        }

        LayerMask wallMask = LayerMask.GetMask("Wall", "Object"); // [수정] 장애물(기둥 등)은 "Object" 레이어 — 엘리트 차저와 동일하게 벽+장애물 모두 감지
        LayerMask playerMask = Layers.PlayerMask;
        LayerMask hitMask = wallMask | playerMask;

        bool hasHitObstacle = false;

        while (chargeElapsed < maxChargeDuration)
        {
            chargeElapsed += Time.deltaTime;
            if (rb != null)
            {
                rb.linearVelocity = chargeDir * chargeSpeed;
            }

            // 돌진 시작 0.15초 이후부터 벽/플레이어 충돌 감지 ( Stun 판정 목적 )
            if (chargeElapsed > 0.15f)
            {
                float checkDistance = chargeSpeed * Time.deltaTime + wallCheckDistanceBuffer;
                RaycastHit2D hit = Physics2D.CircleCast(entity.transform.position, wallStopRadiusRatio, chargeDir, checkDistance, hitMask);
                if (hit.collider != null)
                {
                    hasHitObstacle = true;
                    
                    // 플레이어에 닿은 경우 데미지
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
                    else
                    {
                        // 벽/정적 장애물에 충돌한 경우, Stuck 방지를 위해 물리 반동 및 강제 좌표 미세 오프셋 확보
                        if (rb != null)
                        {
                            rb.linearVelocity = -chargeDir * 3f;
                        }
                        entity.transform.position = (Vector2)entity.transform.position - chargeDir * 0.15f;
                    }
                    break;
                }
            }

            yield return null;
        }

        // 돌진 정지
        afterimage?.EndDash(); // [잔상] 돌진 종료와 함께 잔상 방출 윈도우 닫기

        if (rb != null) rb.linearVelocity = Vector2.zero;
        
        if (wasAgentEnabled && agent != null)
        {
            // NavMesh 영역 바깥 탈출 Stuck 방지를 위해 재배치 보증
            Vector3 finalPos = entity.transform.position;
            if (NavMesh.SamplePosition(finalPos, out NavMeshHit navHit, 2.0f, NavMesh.AllAreas))
            {
                entity.transform.position = navHit.position;
            }
            agent.enabled = true;
            agent.isStopped = false;
        }

        entity.IsAttacking = false;

        // 충돌했다면 1.5초간 기절 처리
        if (hasHitObstacle && entity.Stats != null && entity.Stats.Status != null)
        {
            entity.Stats.Status.ApplyStatus(StatusType.Stun, 1.5f);
        }
    }
}
