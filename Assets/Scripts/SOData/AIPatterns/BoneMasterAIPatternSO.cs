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
public class BoneMasterAIPatternSO : BaseAIPatternSO
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
    public float chargeCooldown = 8f;
    public float thrustCooldown = 6f;
    public float counterCooldown = 10f;

    [Header("가중치 - 체력 100~80%")]
    public Vector3 weightsAbove80 = new Vector3(33f, 33f, 34f);
    [Header("가중치 - 체력 80~60%")]
    public Vector3 weights80To60 = new Vector3(25f, 37f, 38f);
    [Header("가중치 - 체력 60% 이하")]
    public Vector3 weightsBelow60 = new Vector3(21f, 39f, 40f);

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
    public float chargeSafetyTimeMultiplier = 1.15f;
    public float wallCheckRadius = 0.6f;
    public float chargeTelegraphWidth = 2f;

    [Header("패턴 2번: 견갑 찌르기")]
    public float thrustRange = 6.5f;
    public float thrustWidth = 1.3f;
    public float thrustTelegraphLead = 0.35f;
    public float thrustPauseBeforeFirst = 0.75f;
    public float thrustPauseAfterFirst = 0.5f;
    public float thrustPauseAfterSecond = 0.22f;
    public float thrustCounterGaugeAmount = 25f;
    public float thrustCounterGroggyDuration = 5f;

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

        IEnumerator routine;
        if (dist <= sweepRange) routine = BasicAttack_Sweep(entity);
        else if (dist <= basicThrustRange) routine = BasicAttack_Thrust(entity);
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

        GameObject telegraph = BoneMasterTelegraphUtil.SpawnCone(entity, origin, dir, sweepRadius, sweepHalfAngle, telegraphWarnColor);

        float t = 0f;
        while (t < basicAttackWindup)
        {
            Warp(entity, origin);
            t += Time.deltaTime;
            yield return null;
        }
        if (telegraph != null) Object.Destroy(telegraph);

        var info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealCone(origin, dir, sweepRadius, sweepHalfAngle, entity.opponentLayer, info);

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

        GameObject telegraph = BoneMasterTelegraphUtil.SpawnLane(
            entity, origin, dir, basicThrustLength, basicThrustWidth, telegraphWarnColor);

        float t = 0f;
        while (t < basicAttackWindup)
        {
            Warp(entity, origin);
            t += Time.deltaTime;
            yield return null;
        }
        if (telegraph != null) Object.Destroy(telegraph);

        var info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealLane(origin, dir, basicThrustLength, basicThrustWidth, entity.opponentLayer, info);

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

    private IEnumerator BasicAttack_LeapSlam(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.SetStateText("기본 공격: 도약 준비", Color.yellow);

        Vector2 landPos = entity.Target != null ? (Vector2)entity.Target.position : (Vector2)entity.transform.position;
        GameObject telegraph = BoneMasterTelegraphUtil.SpawnEllipse(entity, landPos, leapSlamRadiusX, leapSlamRadiusY, telegraphWarnColor);

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
        BossCombat.DealEllipse(landPos, leapSlamRadiusX, leapSlamRadiusY, entity.opponentLayer, info);

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
        while (t < chargeTelegraphTime)
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

                if (IsTouchingThornWall(nextPos))
                {
                    hitWall = true;
                    Debug.Log($"<color=cyan>[BoneMaster]</color> 돌진 중 뼈 투기장 접촉 감지! pos={nextPos} elapsed={elapsed:F2}/{chargeDuration:F2}");
                    break; // 벽에 닿기 "직전"에서 멈춘다(안으로 파고들지 않음).
                }
                Warp(entity, nextPos);
                yield return null;
            }

            _controller?.HardStopMovement();

            if (hijacked)
            {
                Debug.LogWarning("[BoneMaster] 돌진이 중단되어(hijacked) 벽 충돌 판정을 확인하지 못했습니다. 경직도 적용되지 않습니다.");
                yield break;
            }

            if (hitWall)
            {
                _controller?.SetStateText($"{Pattern1Label} - 가시 충돌! 경직!", Color.cyan);
                _controller?.ApplyGroggy(chargeWallStaggerDuration);
                Debug.Log($"<color=cyan>[BoneMaster]</color> 경직 적용 완료. IsGroggy={_controller?.IsGroggy}");
            }
            else
            {
                yield return new WaitForSeconds(0.5f);
            }
        }

        EndPattern(entity);
    }

    private bool IsTouchingThornWall(Vector2 pos)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, wallCheckRadius);
        foreach (var h in hits)
        {
            if (h.CompareTag(BoneMasterController.ThornWallTag)) return true;
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

        Vector2 origin = entity.transform.position;

        var gauge = _controller != null ? _controller.CounterGauge : null;
        bool broken = false;
        void OnBroken() => broken = true;
        if (gauge != null)
        {
            gauge.OnGaugeBroken += OnBroken;
            gauge.OpenWindow(thrustCounterGaugeAmount);
        }

        bool hijacked = false;

        float preFirst = 0f;
        while (preFirst < thrustPauseBeforeFirst)
        {
            if (broken) break;
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin);
            preFirst += Time.deltaTime;
            yield return null;
        }

        float[] pauseAfterStrike = { thrustPauseAfterFirst, thrustPauseAfterSecond, 0f };

        for (int i = 0; i < 3 && !broken && !hijacked; i++)
        {
            if (entity.Target != null) entity.LookAtTarget(entity.Target);
            Vector2 dir = SafeDirTo(entity, origin, entity.Target);

            GameObject strikeTelegraph = BoneMasterTelegraphUtil.SpawnLane(
                entity, origin, dir, thrustRange, thrustWidth, telegraphWarnColor);
            float leadTimer = 0f;
            while (leadTimer < thrustTelegraphLead)
            {
                if (broken) break;
                if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
                Warp(entity, origin);
                leadTimer += Time.deltaTime;
                yield return null;
            }
            if (strikeTelegraph != null) Object.Destroy(strikeTelegraph);
            if (broken || hijacked) break;

            bool applyBleed = Random.value <= 0.25f;
            var info = new DamageInfo(
                entity.Stats.ATK,
                DamageType.Physical,
                entity.gameObject,
                category: DamageCategory.EnemyBoss,
                applyStatus: applyBleed ? StatusType.Bleed : (StatusType?)null
            );
            BossCombat.DealLane(origin, dir, thrustRange, thrustWidth, entity.opponentLayer, info);

            float pause = pauseAfterStrike[i];
            float pt = 0f;
            while (pt < pause)
            {
                if (broken) break;
                if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
                Warp(entity, origin);
                pt += Time.deltaTime;
                yield return null;
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
