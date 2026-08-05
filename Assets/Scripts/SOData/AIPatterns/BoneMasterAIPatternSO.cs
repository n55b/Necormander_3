using System.Collections;
using UnityEngine;

/// <summary>
/// 본 마스터 페이즈 1 AI. 기획서(0802 기준) 반영:
///   기본 공격 - 거리별 분기 3종 (텍스트: "기본 공격: OOO")
///     근접(sweepRange 이내) → 창 휩쓸기 (정면 반달 부채꼴)
///     중거리(basicThrustRange 이내) → 창 찌르기 (전방 직사각형)
///     원거리(engageRange 이내) → 도약 & 내려찍기 (착지 지점 타원)
///   패턴 1번: 박치기 돌격 (뼈 투기장 벽까지 최대한 돌진)
///   패턴 2번: 견갑 찌르기 (3연속, 위치 고정 + 매 타격 재조준, 완급이 있는 리듬)
///   패턴 3번: 카운터 & 페이크 카운터 (보스 위치 고정)
/// (텍스트: "패턴 N번: OOO" 형식으로 기본 공격과 명확히 구분한다.)
///
/// [핵심 버그 수정 1] NavMeshAgent가 붙어있는 상태에서 entity.transform.position을 직접 대입하면
/// 에이전트 내부 상태와 어긋나 나중에 "튕기는" 버그가 생긴다. WarpTo()로 통일했다.
///
/// [핵심 버그 수정 2] 돌진 이동 시간을 "벽까지 거리 × 0.95"로 계산해서 벽에 닿기 직전에 멈추도록
/// 했었는데, 이러면 애초에 "벽에 닿았다"는 판정(IsTouchingThornWall)이 걸릴 기회 자체가 거의 없다.
/// 이제 이동 "시간 예산"은 벽까지 거리보다 넉넉하게(chargeSafetyTimeMultiplier > 1) 잡고, 정지는
/// 오직 실제 벽 접촉 판정으로만 결정한다.
///
/// [핵심 버그 수정 3] 견갑 찌르기(패턴2)에만 있던 "패턴 도중 CurrentState가 외부에서 바뀌면 즉시
/// 중단" 방어 코드를 박치기 돌격(패턴1)에도 동일하게 추가했다 — 벽에 닿았는데도 경직이 안 걸리고
/// 바로 다음 행동으로 넘어가는 현상이 같은 레이스 컨디션일 가능성이 높다. 벽 충돌 감지/경직 적용
/// 시점에도 진단 로그를 남겨서, 다음에 재현되면 정확히 어디서 끊기는지 확인할 수 있게 했다.
/// </summary>
[CreateAssetMenu(fileName = "BoneMasterAIPattern", menuName = "Necromancer/AI/BoneMasterPattern")]
public class BoneMasterAIPatternSO : BossAIPatternSO
{
    [Header("교전 거리")]
    public float engageRange = 6f;

    [Header("기본 공격 - 거리별 분기")]
    public float sweepRange = 2.8f;
    public float basicThrustRange = 4.5f;
    public float sweepRadius = 3.2f;
    public float sweepHalfAngle = 90f;
    public float basicThrustLength = 4.5f;
    public float basicThrustWidth = 1.4f;
    public float leapSlamRadiusX = 2.2f;
    public float leapSlamRadiusY = 1.6f;
    public float leapWindup = 0.4f;
    public float leapDuration = 0.35f;
    public float basicAttackWindup = 0.85f;
    public float basicAttackRecovery = 0.4f;

    [Header("패턴 쿨타임 (인스펙터에서 조절)")]
    public float chargeCooldown = 12f;
    public float thrustCooldown = 6f;
    public float counterCooldown = 10f;

    [Header("가중치 - 체력 100~80%")]
    public Vector3 weightsAbove80 = new Vector3(18f, 38f, 44f);
    [Header("가중치 - 체력 80~60%")]
    public Vector3 weights80To60 = new Vector3(14f, 40f, 46f);
    [Header("가중치 - 체력 60% 이하")]
    public Vector3 weightsBelow60 = new Vector3(10f, 42f, 48f);

    [Header("텔레그래프(피해범위 인디케이터) 색상")]
    public Color telegraphWarnColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    [Header("패턴 1번: 박치기 돌격")]
    public float howlPushRadius = 2.5f;
    public float howlPushForce = 4f;
    public float chargeTelegraphTime = 1.5f;
    public float chargeCounterGaugeAmount = 30f;
    public float chargeCounterGroggyDuration = 5f;
    public float chargeWallStaggerDuration = 1.5f;
    public float chargeSpeed = 22f;
    public float chargeMaxDurationFallback = 1.2f;
    [Range(1.0f, 1.5f)]
    public float chargeSafetyTimeMultiplier = 1.3f;
    public float wallCheckRadius = 0.85f;
    public float chargeTelegraphWidth = 2f;

    [Header("패턴 2번: 견갑 찌르기")]
    public float thrustRange = 6.5f;
    public float thrustWidth = 1.3f;
    public float thrustTelegraphLead = 0.45f; // [수정] 대시가 추가되어 반응 시간을 조금 더 준다.
    [Tooltip("찌르기 시전 시 짧게 파고드는 대시 거리(1,2타). 총 사거리(thrustRange) 중 이 거리만큼은 실제 이동으로 커버한다.")]
    public float thrustDashDistance = 3f;
    public float thrustDashDuration = 0.15f;
    public float thrustPauseBeforeFirst = 0.75f;
    public float thrustPauseAfterFirst = 0.3f; // [수정] 후딜이 너무 길다는 피드백 — 구간1(1->2타) 총합 0.75초(후딜0.3+조준0.45)로 단축.
    public float thrustPauseAfterSecond = 0f; // [수정] 후딜이 너무 길다는 피드백 — 구간2(2->3타) 총합 1.0초(후딜0+조준1.0)로 단축.
    public float thrustCounterGaugeAmount = 25f;
    public float thrustCounterGroggyDuration = 4f; // [수정] 그로기가 너무 길다는 피드백으로 5->4초.
    [Header("패턴 2번: 마지막(3번째) 타격 전용 - 카운터 찬스")]
    [Tooltip("3타의 데미지 배율(ATK 대비). 기본 15뎀 기준 20뎀이 되도록 20/15로 설정.")]
    public float thrustFinalDamageMultiplier = 20f / 15f;
    public float thrustFinalRange = 7.5f;
    public float thrustFinalWidth = 1.6f;
    public float thrustFinalTelegraphLead = 1f;
    [Tooltip("3타를 실제로 찌른 후에도 이 시간(초) 동안은 카운터 판정이 계속 유효하다.")]
    public float thrustFinalCounterTail = 0.45f;
    public float thrustFinalDashDistance = 4f;
    public float thrustFinalDashDuration = 0.2f;

    [Header("패턴 3번: 카운터 & 페이크 카운터")]
    public Vector2 counterReactionWindowRange = new Vector2(1f, 1.5f);
    public float counterSuccessGroggyDuration = 2.5f;
    public float fakeCounterPlayerStun = 0.75f;
    public float fakeCounterPunishDamage = 3f;

    private const string Pattern1Label = "패턴 1번: 박치기 돌격";
    private const string Pattern2Label = "패턴 2번: 견갑 찌르기";
    private const string Pattern3Label = "패턴 3번: 카운터 & 페이크 카운터";

    private BoneMasterController _controller;
    private float _lastChargeTime = -100f;
    private float _lastThrustTime = -100f;
    private float _lastCounterTime = -100f;
    private float _lastDiagLogTime = -100f;
    [Header("스폰 연출")]
    public float startupDelay = 2f;
    private float _activationTime = -100f;


public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _controller = entity as BoneMasterController;
        if (_controller == null)
        {
            Debug.LogError($"[BoneMaster] Init: entity({entity?.gameObject?.name})를 BoneMasterController로 캐스팅하지 못했습니다!");
        }
        _lastChargeTime = -100f;
        _lastThrustTime = -100f;
        _lastCounterTime = -100f;

        // [추가] 스폰 직후 startupDelay(기본 2초) 동안은 아무 패턴도 뽑지 않고 가만히 대기한다.
        // 예전엔 스폰과 동시에 바로 돌진이 뽑힐 수 있어서, 플레이어가 상황을 인지하기도 전에
        // 돌진 -> 경직까지 순식간에 지나가버리는 문제가 있었다.
        _activationTime = Time.time + startupDelay;
        _controller?.SetStateText("...", Color.gray);
    }

    protected override void UpdateTargeting(BaseEntity entity)
    {
        if (GameManager.Instance != null && GameManager.Instance.PLAYERCONTROLLER != null)
        {
            entity.Target = GameManager.Instance.PLAYERCONTROLLER.transform;
        }
        else
        {
            entity.Target = null;
        }
    }

    private static Vector2 SafeDirTo(BaseEntity entity, Vector2 origin, Transform target)
    {
        if (target == null) return (Vector2)entity.transform.right;
        Vector2 raw = (Vector2)target.position - origin;
        if (raw.sqrMagnitude < 0.01f) return (Vector2)entity.transform.right;
        return raw.normalized;
    }

    private void Warp(BaseEntity entity, Vector3 pos)
    {
        if (_controller != null) _controller.WarpTo(pos);
        else entity.transform.position = pos;
    }

    protected override void UpdateStateTransitions(BaseEntity entity)
    {
        if (entity.Target == null)
        {
            entity.CurrentState = AIState.Idle;
            return;
        }

        // [버그 수정 — 경직 끝나기도 전에 다음 패턴이 시작되는 문제] AIPatternSO.Execute()는
        // CurrentState==Skill 일 때만 AI 판단을 멈추고, IsGroggy(경직) 여부는 보지 않는다. 패턴이
        // 끝나며 CurrentState가 Follow로 바뀌자마자 다음 프레임에 여기서 바로 새 패턴을 뽑을 수 있어서,
        // 보스가 아직 경직 중인데도(연출/스턴이 안 끝났는데도) 새 패턴이 시작돼버리는 문제가 있었다.
        if (_controller != null && _controller.IsGroggy)
        {
            entity.CurrentState = AIState.Idle;
            return;
        }


                // [추가] 스폰 직후 startupDelay 동안은 어떤 패턴도 뽑지 않고 가만히 대기한다.
        if (Time.time < _activationTime)
        {
            entity.CurrentState = AIState.Idle;
            return;
        }

float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
        float rangeBonus = _controller != null ? _controller.AttackRangeBonus : 0f;
        float effectiveEngageRange = engageRange * (1f + rangeBonus);

        if (Time.time - _lastDiagLogTime > 2f)
        {
            _lastDiagLogTime = Time.time;
            Debug.Log($"[BoneMaster-Diag] dist={dist:F1} engageRange={effectiveEngageRange:F1} AtkTimer={entity.AtkTimer:F2}/{entity.Stats.AttackInterval:F2} CurrentState={entity.CurrentState} IsAttacking={entity.IsAttacking}");
        }

        if (dist > effectiveEngageRange)
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중...");
            return;
        }

        float castSpeedBonus = _controller != null ? _controller.PatternCastSpeedBonus : 0f;
        float cdMul = 1f / (1f + castSpeedBonus);

        bool canCharge = Time.time - _lastChargeTime >= chargeCooldown * cdMul;
        bool canThrust = Time.time - _lastThrustTime >= thrustCooldown * cdMul;
        bool canCounter = Time.time - _lastCounterTime >= counterCooldown * cdMul;

        if (!canCharge && !canThrust && !canCounter)
        {
            if (entity.AtkTimer >= entity.Stats.AttackInterval)
            {
                entity.CurrentState = AIState.Attack;
            }
            else
            {
                entity.CurrentState = AIState.Follow;
                _controller?.SetStateText("추격 중...");
            }
            return;
        }

        Vector3 hpWeights = GetWeights(entity);
        float wCharge = canCharge ? hpWeights.x : 0f;
        float wThrust = canThrust ? hpWeights.y : 0f;
        float wCounter = canCounter ? hpWeights.z : 0f;
        float total = wCharge + wThrust + wCounter;
        if (total <= 0f) return;

        float roll = Random.value * total;
        entity.CurrentState = AIState.Skill;

        if (roll < wCharge)
        {
            _lastChargeTime = Time.time;
            entity.StartCoroutine(Pattern1_ChargeRoutine(entity));
        }
        else if (roll < wCharge + wThrust)
        {
            _lastThrustTime = Time.time;
            entity.StartCoroutine(Pattern2_ThrustRoutine(entity));
        }
        else
        {
            _lastCounterTime = Time.time;
            entity.StartCoroutine(Pattern3_CounterRoutine(entity));
        }
    }

    // ── 기본 공격: 거리별 분기 3종 ──────────────────────────────────
protected override void OnAttack(BaseEntity entity)
    {
        StopNavAgent(entity);
        if (entity.IsAttacking) return;
        if (entity.AtkTimer < entity.Stats.AttackInterval) return;

        entity.AtkTimer = 0f;
        entity.IsAttacking = true;

        float dist = entity.Target != null ? Vector2.Distance(entity.transform.position, entity.Target.position) : 0f;
        // [버그 수정 — 투구 파괴 효과 미적용] AttackRangeBonus가 교전거리(engageRange)에만 쓰이고
        // 정작 기본 공격 판정 자체(부채꼴/직사각형/타원 크기)에는 전혀 반영되지 않고 있었다.
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);

        IEnumerator routine;
        if (dist <= sweepRange * rangeMul) routine = BasicAttack_Sweep(entity);
        else if (dist <= basicThrustRange * rangeMul) routine = BasicAttack_Thrust(entity);
        else routine = BasicAttack_LeapSlam(entity);

        entity.ActiveAttackCoroutine = entity.StartCoroutine(routine);
    }

    private void FinishBasicAttack(BaseEntity entity)
    {
        entity.IsAttacking = false;
        entity.ActiveAttackCoroutine = null;
        entity.ResetAnimationState();
    }

private IEnumerator BasicAttack_Sweep(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        _controller?.SetStateText("기본 공격: 창 휩쓸기", Color.white);

        if (entity.Target != null) entity.LookAtTarget(entity.Target);
        Vector2 origin = entity.transform.position;
        Vector2 dir = SafeDirTo(entity, origin, entity.Target);
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        float radius = sweepRadius * rangeMul;

        GameObject telegraph = BoneMasterTelegraphUtil.SpawnCone(entity, origin, dir, radius, sweepHalfAngle, telegraphWarnColor);

        float t = 0f;
        while (t < basicAttackWindup)
        {
            Warp(entity, origin);
            t += Time.deltaTime;
            yield return null;
        }
        if (telegraph != null) Object.Destroy(telegraph);

        var info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealCone(origin, dir, radius, sweepHalfAngle, entity.opponentLayer, info);

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

private IEnumerator BasicAttack_Thrust(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        _controller?.SetStateText("기본 공격: 창 찌르기", Color.white);
        if (entity.Target != null) entity.LookAtTarget(entity.Target);
        Vector2 origin = entity.transform.position;
        Vector2 dir = SafeDirTo(entity, origin, entity.Target);
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        float length = basicThrustLength * rangeMul;
        float width = basicThrustWidth * rangeMul;

        GameObject telegraph = BoneMasterTelegraphUtil.SpawnLane(
            entity, origin, dir, length, width, telegraphWarnColor);

        float t = 0f;
        while (t < basicAttackWindup)
        {
            Warp(entity, origin);
            t += Time.deltaTime;
            yield return null;
        }
        if (telegraph != null) Object.Destroy(telegraph);

        var info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealLane(origin, dir, length, width, entity.opponentLayer, info);

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

private IEnumerator BasicAttack_LeapSlam(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.SetStateText("기본 공격: 도약 준비", Color.yellow);
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        float radiusX = leapSlamRadiusX * rangeMul;
        float radiusY = leapSlamRadiusY * rangeMul;

        Vector2 landPos = entity.Target != null ? (Vector2)entity.Target.position : (Vector2)entity.transform.position;
        GameObject telegraph = BoneMasterTelegraphUtil.SpawnEllipse(entity, landPos, radiusX, radiusY, telegraphWarnColor);

        float t = 0f;
        while (t < leapWindup + basicAttackWindup * 0.3f)
        {
            t += Time.deltaTime;
            if (entity.Target != null)
            {
                landPos = entity.Target.position;
                BoneMasterTelegraphUtil.UpdatePosition(telegraph, landPos);
            }
            yield return null;
        }

        _controller?.SetStateText("기본 공격: 도약 & 내려찍기", Color.white);
        Vector3 startPos = entity.transform.position;
        float elapsed = 0f;
        while (elapsed < leapDuration)
        {
            elapsed += Time.deltaTime;
            Warp(entity, Vector3.Lerp(startPos, (Vector3)landPos, elapsed / leapDuration));
            yield return null;
        }
        Warp(entity, landPos);
        _controller?.HardStopMovement();

        if (telegraph != null) Object.Destroy(telegraph);

        var info = new DamageInfo(entity.Stats.ATK * 1.2f, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss, causesHitstun: true);
        BossCombat.DealEllipse(landPos, radiusX, radiusY, entity.opponentLayer, info);

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

    public override void OnAttackCancelled(BaseEntity entity)
    {
        base.OnAttackCancelled(entity);
    }

    private Vector3 GetWeights(BaseEntity entity)
    {
        if (entity.Stats == null || entity.Stats.Health == null) return weightsAbove80;
        float ratio = entity.Stats.Health.CurHP / entity.Stats.Health.MaxHP;
        if (ratio > 0.8f) return weightsAbove80;
        if (ratio > 0.6f) return weights80To60;
        return weightsBelow60;
    }

    private void EndPattern(BaseEntity entity)
    {
        entity.CurrentState = AIState.Follow;
    }

    #region 패턴 1번: 박치기 돌격
private IEnumerator Pattern1_ChargeRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        _controller?.SetStateText($"{Pattern1Label} - 포효... (돌진 예고)", Color.yellow);

        if (entity.Target != null)
        {
            float d = Vector2.Distance(entity.transform.position, entity.Target.position);
            if (d <= howlPushRadius)
            {
                var status = entity.Target.GetComponentInParent<CharacterStatus>();
                if (status == null) status = entity.Target.GetComponentInChildren<CharacterStatus>();
                Vector2 dir0 = SafeDirTo(entity, entity.transform.position, entity.Target);
                status?.ApplyKnockback(dir0, howlPushForce, 0.2f);
            }
        }

        Vector2 origin = entity.transform.position;
        Vector2 lockedDir = SafeDirTo(entity, origin, entity.Target);
        // [버그 수정 — 견갑 파괴 효과 미적용] PatternCastSpeedBonus가 패턴 쿨타임 단축에만 쓰이고
        // 정작 패턴 자체의 시전(예고) 속도에는 전혀 반영되지 않고 있었다.
        float csMul = 1f / (1f + (_controller != null ? _controller.PatternCastSpeedBonus : 0f));

        float wallDist = _controller != null ? _controller.GetChargeDistance(origin, lockedDir) : -1f;
        float chargeDistance;
        float chargeDuration;
        if (wallDist > 0f)
        {
            chargeDistance = wallDist;
            chargeDuration = (wallDist * chargeSafetyTimeMultiplier) / Mathf.Max(0.01f, chargeSpeed);
        }
        else
        {
            chargeDuration = chargeMaxDurationFallback;
            chargeDistance = chargeSpeed * chargeDuration;
        }

        GameObject laneTelegraph = BoneMasterTelegraphUtil.SpawnLane(
            entity, origin, lockedDir, chargeDistance, chargeTelegraphWidth, telegraphWarnColor);

        var gauge = _controller != null ? _controller.CounterGauge : null;
        bool broken = false;
        void OnBroken() => broken = true;
        if (gauge != null)
        {
            gauge.OnGaugeBroken += OnBroken;
            gauge.OpenWindow(chargeCounterGaugeAmount);
        }

        bool hijacked = false;

        float t = 0f;
        while (t < chargeTelegraphTime * csMul)
        {
            if (broken) break;
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin); // 돌진 예고 중엔 절대 안 움직인다.
            t += Time.deltaTime;
            yield return null;
        }
        if (gauge != null) gauge.OnGaugeBroken -= OnBroken;
        if (laneTelegraph != null) Object.Destroy(laneTelegraph);

        if (hijacked)
        {
            gauge?.CloseWindow();
            Debug.LogWarning("[BoneMaster] 패턴 1번(박치기 돌격)이 예고 단계에서 외부 요인으로 중단됨(CurrentState 변경 감지).");
            yield break;
        }

        if (broken)
        {
            gauge.CloseWindow();
            _controller?.SetStateText($"{Pattern1Label} - 정면딜 파훼! 그로기!", Color.cyan);
            _controller?.ApplyGroggy(chargeCounterGroggyDuration);
        }
        else
        {
            gauge?.CloseWindow();
            _controller?.SetStateText($"{Pattern1Label} - 돌진!", Color.white);

            float elapsed = 0f;
            bool hitWall = false;
            Vector3 prevPos = entity.transform.position;
            while (elapsed < chargeDuration)
            {
                if (entity.CurrentState != AIState.Skill)
                {
                    hijacked = true;
                    Debug.LogWarning($"[BoneMaster] 패턴 1번(박치기 돌격)이 돌진 도중 외부 요인으로 중단됨(CurrentState={entity.CurrentState}, elapsed={elapsed:F2}/{chargeDuration:F2}).");
                    break;
                }

                Vector3 nextPos = entity.transform.position + (Vector3)(lockedDir * chargeSpeed * Time.deltaTime);
                elapsed += Time.deltaTime;

                if (IsTouchingThornWallSwept(prevPos, nextPos))
                {
                    hitWall = true;
                    Debug.Log($"<color=cyan>[BoneMaster]</color> 돌진 중 뼈 투기장 접촉 감지! pos={nextPos} elapsed={elapsed:F2}/{chargeDuration:F2}");
                    break; // 벽에 닿기 "직전"에서 멈춘다(안으로 파고들지 않음).
                }
                prevPos = nextPos;
                Warp(entity, nextPos);
                yield return null;
            }

            _controller?.HardStopMovement();

            if (hijacked)
            {
                Debug.LogWarning("[BoneMaster] 돌진이 중단되어(hijacked) 벽 충돌 판정을 확인하지 못했습니다. 경직도 적용되지 않습니다.");
                yield break;
            }

            // [버그 수정 4 — 가끔 경직이 안 걸리는 문제] chargeDuration은 벽까지 거리보다
            // chargeSafetyTimeMultiplier(>1)배만큼 넉넉하게 잡혀 있으므로, 시간이 다 될 때까지
            // IsTouchingThornWallSwept이 한 번도 안 걸렸어도 기하학적으로는 이미 벽을 지나쳤어야
            // 한다(프레임 드랍으로 얇은 가시 링을 건너뛴 경우 등). 이 경우도 "벽에 닿음"으로 간주해
            // 경직을 보장한다 — 안 그러면 가끔 경직이 통째로 씹히는 현상이 재발한다.
            if (!hitWall)
            {
                Debug.LogWarning($"[BoneMaster] 돌진 시간 예산 소진까지 벽 접촉이 감지되지 않음(프레임 드랍 추정) — 안전 폴백으로 경직을 적용합니다. finalPos={entity.transform.position}");
                hitWall = true;
            }

            _controller?.SetStateText($"{Pattern1Label} - 가시 충돌! 경직!", Color.cyan);
            _controller?.ApplyGroggy(chargeWallStaggerDuration);
            Debug.Log($"<color=cyan>[BoneMaster]</color> 경직 적용 완료. IsGroggy={_controller?.IsGroggy}");
        }

        EndPattern(entity);
    }

    private bool IsTouchingThornWall(Vector2 pos)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, wallCheckRadius);
        foreach (var h in hits)
        {
            // 태그가 아니라 컴포넌트로 판정한다. 예전엔 "BoneSpikeWall" 태그를 썼는데 그 태그가
            // TagManager 에 등록돼 있지 않아서, 태그를 다는 쪽은 예외로 죽고 이 검사는 영원히 false 였다.
            if (h.GetComponent<ThornArenaHazard>() != null) return true;
        }
        return false;
    }


    /// <summary>
    /// [버그 수정 4 — 가끔 경직이 안 걸리는 문제] 프레임이 잠깐 튀어(GC, 오브젝트 파괴 등) 한 프레임에
    /// 크게 이동하면 이전 위치와 다음 위치 사이의 얇은 가시 링을 통째로 건너뛰어 접촉 판정을 한 번도
    /// 못 잡는 경우가 있었다. prev→next 구간을 wallCheckRadius 간격으로 여러 지점 샘플링해서 검사한다.
    /// </summary>
    private bool IsTouchingThornWallSwept(Vector2 prevPos, Vector2 nextPos)
    {
        float dist = Vector2.Distance(prevPos, nextPos);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(0.05f, wallCheckRadius)));
        // i는 1부터 — i=0은 prevPos(= 이번 프레임 시작 위치)라 "이미 서 있던 자리"를 다시 검사한다.
        // 돌진은 항상 벽 직전에서 멈추므로 다음 돌진의 시작 위치는 벽에서 wallCheckRadius 안쪽인
        // 경우가 많고, 그러면 첫 검사에서 즉시 걸려 한 칸도 못 가고 자해 경직에 빠진다.
        for (int i = 1; i <= steps; i++)
        {
            Vector2 sample = Vector2.Lerp(prevPos, nextPos, steps == 0 ? 0f : (float)i / steps);
            if (IsTouchingThornWall(sample)) return true;
        }
        return false;
    }

    #endregion

    #region 패턴 2번: 견갑 찌르기
private IEnumerator Pattern2_ThrustRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        _controller?.SetStateText($"{Pattern2Label}", Color.yellow);

        // [버그 수정 — 견갑 파괴 효과 미적용] 견갑 파괴로 얻는 PatternCastSpeedBonus를 예고/대시/후딜
        // 전체 타이밍에 반영한다(쿨타임 단축은 기존 UpdateStateTransitions에서 이미 처리 중).
        float csMul = 1f / (1f + (_controller != null ? _controller.PatternCastSpeedBonus : 0f));

        var gauge = _controller != null ? _controller.CounterGauge : null;
        bool broken = false;
        void OnBroken() => broken = true;

        bool hijacked = false;

        Vector2 holdPos = entity.transform.position;
        float preFirst = 0f;
        while (preFirst < thrustPauseBeforeFirst * csMul)
        {
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, holdPos);
            preFirst += Time.deltaTime;
            yield return null;
        }

        float[] pauseAfterStrike = { thrustPauseAfterFirst, thrustPauseAfterSecond, 0f };

        for (int i = 0; i < 3 && !broken && !hijacked; i++)
        {
            bool isFinal = (i == 2);
            float dashDist = isFinal ? thrustFinalDashDistance : thrustDashDistance;
            // [수정] 돌진 거리만큼 사거리가 "추가로" 늘어난다(기존 정지 판정 길이 + 돌진 거리).
            float totalLen = (isFinal ? thrustFinalRange : thrustRange) + dashDist;
            float width = isFinal ? thrustFinalWidth : thrustWidth;
            float lead = isFinal ? thrustFinalTelegraphLead : thrustTelegraphLead;
            Color telegraphColor = isFinal ? Color.yellow : telegraphWarnColor;
            float dmgMul = isFinal ? thrustFinalDamageMultiplier : 1f;
            float dashDur = isFinal ? thrustFinalDashDuration : thrustDashDuration;

            // [설계 변경 - 제자리 찌르기가 심심하다는 피드백] 매 타격마다 "현재" 위치에서 다시 조준한다.
            // 이전 타격의 대시로 이동한 지점이 다음 타격의 시작점이 되므로, 3연타를 거치며 보스가
            // 실제로 플레이어 쪽으로 파고드는 느낌을 만든다.
            Vector2 origin = entity.transform.position;
            if (entity.Target != null) entity.LookAtTarget(entity.Target);
            Vector2 dir = SafeDirTo(entity, origin, entity.Target);

            GameObject strikeTelegraph = BoneMasterTelegraphUtil.SpawnLane(
                entity, origin, dir, totalLen, width, telegraphColor);

            if (isFinal && gauge != null)
            {
                _controller?.SetStateText($"{Pattern2Label} - 마지막 일격! (카운터 찬스)", Color.yellow);
                gauge.OnGaugeBroken += OnBroken;
                gauge.OpenWindow(thrustCounterGaugeAmount);
            }

            float leadTimer = 0f;
            while (leadTimer < lead * csMul)
            {
                if (broken) break;
                if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
                Warp(entity, origin);
                leadTimer += Time.deltaTime;
                yield return null;
            }
            if (strikeTelegraph != null) Object.Destroy(strikeTelegraph);
            if (broken || hijacked) break;

            // [추가] 짧게 파고드는 대시 — 제자리에서 판정만 뻗던 것을 실제 이동으로 바꿔서 위협감을 준다.
            Vector2 dashEnd = origin + dir * dashDist;
            float dashT = 0f;
            float scaledDashDur = dashDur * csMul;
            while (dashT < scaledDashDur)
            {
                dashT += Time.deltaTime;
                Warp(entity, Vector2.Lerp(origin, dashEnd, Mathf.Clamp01(dashT / scaledDashDur)));
                yield return null;
            }
            Warp(entity, dashEnd);

            bool applyBleed = Random.value <= 0.25f;
            var info = new DamageInfo(
                entity.Stats.ATK * dmgMul,
                DamageType.Physical,
                entity.gameObject,
                category: DamageCategory.EnemyBoss,
                applyStatus: applyBleed ? StatusType.Bleed : (StatusType?)null
            );
            // 판정은 텔레그래프와 동일하게 대시 "이전" origin 기준 전체 길이로 적용한다
            // (텔레그래프가 보여준 범위와 실제 피해 범위를 항상 일치시키기 위함).
            BossCombat.DealLane(origin, dir, totalLen, width, entity.opponentLayer, info);

            if (isFinal)
            {
                float tailT = 0f;
                while (tailT < thrustFinalCounterTail * csMul)
                {
                    if (broken) break;
                    if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
                    Warp(entity, dashEnd);
                    tailT += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                float pause = pauseAfterStrike[i];
                float pt = 0f;
                while (pt < pause * csMul)
                {
                    if (broken) break;
                    if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
                    Warp(entity, dashEnd);
                    pt += Time.deltaTime;
                    yield return null;
                }
            }
        }

        if (gauge != null) gauge.OnGaugeBroken -= OnBroken;

        if (hijacked)
        {
            gauge?.CloseWindow();
            Debug.LogWarning("[BoneMaster] 패턴 2번(견갑 찌르기)이 외부 요인으로 중단됨(CurrentState 변경 감지).");
            yield break;
        }

        if (broken)
        {
            gauge.CloseWindow();
            _controller?.SetStateText($"{Pattern2Label} - 카운터 성공!", Color.cyan);
            _controller?.ApplyGroggy(thrustCounterGroggyDuration);
        }
        else
        {
            gauge?.CloseWindow();
            yield return new WaitForSeconds(0.3f);
        }

        EndPattern(entity);
    }
    #endregion

    #region 패턴 3번: 카운터 & 페이크 카운터
    private IEnumerator Pattern3_CounterRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        float reactionWindow = Random.Range(counterReactionWindowRange.x, counterReactionWindowRange.y);
        yield return BoneMasterCounterUtil.Run(
            entity, _controller, reactionWindow,
            counterSuccessGroggyDuration, fakeCounterPlayerStun, fakeCounterPunishDamage, Pattern3Label);

        EndPattern(entity);
    }
    #endregion
}
