using System.Collections;
using UnityEngine;

/// <summary>
/// 본 마스터 페이즈 2 AI. 갑옷/랜스가 무너지고 양손검으로 전환된 이후의 전투.
///   기본 공격 - 거리별 분기 2종 (도약&내려찍기는 페이즈2에서 쓰지 않는다). 텍스트: "기본 공격: OOO"
///     근접(sweepRange 이내) → 양손검 휩쓸기
///     그 외(engageRange 이내) → 양손검 찌르기
///   패턴 1번: 회전 베기 & 내려찍기
///   패턴 2번: 검을 축으로 삼아 (몸통 박치기 -> 회전 마무리 베기)
///   패턴 3번: 카운터 & 페이크 카운터 (반응 시간이 페이즈1보다 짧은 1초)
/// (텍스트: "패턴 N번: OOO" 형식으로 기본 공격과 명확히 구분한다.)
///
/// [밸런스 조정] 좁아진 페이즈2 투기장에 맞춰 광역 패턴 반경을 줄이고 안전지대를 넓혔으며,
/// 짧았던 예고 시간들을 반응 가능한 수준으로 늘렸다. 큰 피해 배율도 소폭 완화했다.
///
/// [버그 수정] NavMeshAgent 위치는 항상 WarpTo()로 옮긴다.
/// [버그 수정 — 패턴 도중 멈춤 방지] 페이즈1 패턴에만 있던 "패턴 도중 CurrentState가 외부에서
/// 바뀌면 즉시 중단" 방어 코드를 페이즈2 패턴에도 동일하게 추가했다 — 안 그러면 레이스 컨디션으로
/// 코루틴이 영원히 안 끝나는 채로 남아 보스가 "낑겨서" 아무것도 못 하는 것처럼 멈출 수 있다.
/// </summary>
[CreateAssetMenu(fileName = "BoneMasterPhase2AIPattern", menuName = "Necromancer/AI/BoneMasterPhase2Pattern")]
public class BoneMasterPhase2AIPatternSO : BaseAIPatternSO
{
    [Header("교전 거리")]
    public float engageRange = 5f;

    [Header("기본 공격 - 거리별 분기 (도약&내려찍기 없음)")]
    public float sweepRange = 2.4f;
    public float sweepRadius = 2.8f;
    public float sweepHalfAngle = 90f;
    public float basicThrustLength = 4.5f;
    public float basicThrustWidth = 1.4f;
    public float basicAttackWindup = 0.8f;
    public float basicAttackRecovery = 0.5f;

    [Header("패턴 쿨타임 (인스펙터에서 조절)")]
    public float spinSlamCooldown = 9f;
    public float pivotSpinCooldown = 9f;
    public float counterCooldown = 10f;

    [Header("가중치 (페이즈2는 체력 구간 없이 고정 33/33/34)")]
    public Vector3 weights = new Vector3(33f, 33f, 34f);

    [Header("텔레그래프(피해범위 인디케이터) 색상")]
    public Color telegraphWarnColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    [Header("패턴 1번: 회전 베기 & 내려찍기")]
    public float spinRadius = 4f;
    public float spinSafeRadius = 2.2f;
    public float spinTelegraphLead = 0.6f;
    public float slamTelegraphTime = 1.2f;
    public float slamCounterGaugeAmount = 25f;
    public float slamCounterStaggerDuration = 1.5f;
    public float slamRange = 2.5f;
    public float spinToSlamPause = 0.8f;

    [Header("패턴 2번: 검을 축으로 삼아")]
    public float pivotBodySlamRadius = 2f;
    public float pivotSafeRadius = 1.8f;
    public float pivotBodyTelegraphLead = 0.5f;
    public float pivotCounterWindow = 1f;
    public float pivotCounterGaugeAmount = 25f;
    public float pivotCounterStaggerDuration = 2.5f;
    public float pivotFinishRadius = 4f;
    public float pivotFinishSafeRadius = 2.2f;
    public float pivotFinishTelegraphLead = 0.6f;

    [Header("패턴 3번: 카운터 & 페이크 카운터")]
    public float counterReactionWindow = 1f;
    public float counterSuccessStaggerDuration = 2.5f;
    public float fakeCounterPlayerStun = 0.75f;
    public float fakeCounterPunishDamage = 4f;

    private const string Pattern1Label = "패턴 1번: 회전 베기 & 내려찍기";
    private const string Pattern2Label = "패턴 2번: 검을 축으로 삼아";
    private const string Pattern3Label = "패턴 3번: 카운터 & 페이크 카운터";

    private BoneMasterController _controller;
    private float _lastSpinTime = -100f;
    private float _lastPivotTime = -100f;
    private float _lastCounterTime = -100f;
    private float _lastDiagLogTime = -100f;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _controller = entity as BoneMasterController;
        _lastSpinTime = -100f;
        _lastPivotTime = -100f;
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

        if (Time.time - _lastDiagLogTime > 2f)
        {
            _lastDiagLogTime = Time.time;
            Debug.Log($"[BoneMaster-Diag-P2] dist={dist:F1} engageRange={engageRange:F1} CurrentState={entity.CurrentState} IsAttacking={entity.IsAttacking}");
        }

        if (dist > engageRange)
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중... (페이즈2)");
            return;
        }

        bool canSpin = Time.time - _lastSpinTime >= spinSlamCooldown;
        bool canPivot = Time.time - _lastPivotTime >= pivotSpinCooldown;
        bool canCounter = Time.time - _lastCounterTime >= counterCooldown;

        if (!canSpin && !canPivot && !canCounter)
        {
            if (entity.AtkTimer >= entity.Stats.AttackInterval)
            {
                entity.CurrentState = AIState.Attack;
            }
            else
            {
                entity.CurrentState = AIState.Follow;
                _controller?.SetStateText("추격 중... (페이즈2)");
            }
            return;
        }

        float wSpin = canSpin ? weights.x : 0f;
        float wPivot = canPivot ? weights.y : 0f;
        float wCounter = canCounter ? weights.z : 0f;
        float total = wSpin + wPivot + wCounter;
        if (total <= 0f) return;

        float roll = Random.value * total;
        entity.CurrentState = AIState.Skill;

        if (roll < wSpin)
        {
            _lastSpinTime = Time.time;
            entity.StartCoroutine(Pattern1_SpinSlamRoutine(entity));
        }
        else if (roll < wSpin + wPivot)
        {
            _lastPivotTime = Time.time;
            entity.StartCoroutine(Pattern2_PivotSpinRoutine(entity));
        }
        else
        {
            _lastCounterTime = Time.time;
            entity.StartCoroutine(Pattern3_CounterRoutine(entity));
        }
    }

    // ── 기본 공격: 거리별 분기 2종 (도약&내려찍기 없음) ────────────────
    protected override void OnAttack(BaseEntity entity)
    {
        StopNavAgent(entity);
        if (entity.IsAttacking) return;
        if (entity.AtkTimer < entity.Stats.AttackInterval) return;

        entity.AtkTimer = 0f;
        entity.IsAttacking = true;

        float dist = entity.Target != null ? Vector2.Distance(entity.transform.position, entity.Target.position) : 0f;
        IEnumerator routine = dist <= sweepRange ? BasicAttack_Sweep(entity) : BasicAttack_Thrust(entity);
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
        _controller?.SetStateText("기본 공격: 양손검 휩쓸기", Color.white);

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
        _controller?.SetStateText("기본 공격: 양손검 찌르기", Color.white);
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

    public override void OnAttackCancelled(BaseEntity entity)
    {
        base.OnAttackCancelled(entity);
    }

    private void EndPattern(BaseEntity entity)
    {
        entity.CurrentState = AIState.Follow;
    }

    #region 패턴 1번: 회전 베기 & 내려찍기
    private IEnumerator Pattern1_SpinSlamRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        _controller?.SetStateText($"{Pattern1Label} - 광역 회전 베기!", Color.yellow);

        Vector2 origin = entity.transform.position;
        bool hijacked = false;

        GameObject spinTelegraph = BoneMasterTelegraphUtil.SpawnRing(entity, origin, spinRadius, telegraphWarnColor);
        float leadT = 0f;
        while (leadT < spinTelegraphLead)
        {
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin);
            leadT += Time.deltaTime;
            yield return null;
        }
        if (spinTelegraph != null) Object.Destroy(spinTelegraph);
        if (hijacked)
        {
            Debug.LogWarning("[BoneMaster] 페이즈2 패턴 1번이 회전베기 예고 중 외부 요인으로 중단됨.");
            yield break;
        }

        var spinInfo = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealCircle(origin, spinRadius, entity.opponentLayer, spinInfo, excludeRadius: spinSafeRadius);
        yield return new WaitForSeconds(spinToSlamPause);
        Warp(entity, origin);

        _controller?.SetStateText($"{Pattern1Label} - 내려찍기 예고!", Color.yellow);
        Vector2 slamCenter = entity.Target != null ? (Vector2)entity.Target.position : origin;
        GameObject slamTelegraph = BoneMasterTelegraphUtil.SpawnCircle(entity, slamCenter, slamRange, telegraphWarnColor);

        var gauge = _controller != null ? _controller.CounterGauge : null;
        bool broken = false;
        void OnBroken() => broken = true;
        if (gauge != null)
        {
            gauge.OnGaugeBroken += OnBroken;
            gauge.OpenWindow(slamCounterGaugeAmount);
        }

        float t = 0f;
        while (t < slamTelegraphTime)
        {
            if (broken) break;
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin);
            t += Time.deltaTime;
            if (entity.Target != null && slamTelegraph != null)
            {
                slamCenter = entity.Target.position;
                slamTelegraph.transform.position = slamCenter;
            }
            yield return null;
        }
        if (gauge != null) gauge.OnGaugeBroken -= OnBroken;
        if (slamTelegraph != null) Object.Destroy(slamTelegraph);

        if (hijacked)
        {
            gauge?.CloseWindow();
            Debug.LogWarning("[BoneMaster] 페이즈2 패턴 1번이 내려찍기 예고 중 외부 요인으로 중단됨.");
            yield break;
        }

        if (broken)
        {
            gauge.CloseWindow();
            _controller?.SetStateText($"{Pattern1Label} - 내려찍기 파훼! 경직!", Color.cyan);
            _controller?.ApplyGroggy(slamCounterStaggerDuration);
        }
        else
        {
            gauge?.CloseWindow();
            _controller?.SetStateText($"{Pattern1Label} - 내려찍기!", Color.white);
            var slamInfo = new DamageInfo(entity.Stats.ATK * 1.15f, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss, causesHitstun: true);
            BossCombat.DealCircle(slamCenter, slamRange, entity.opponentLayer, slamInfo);
            yield return new WaitForSeconds(0.8f);
        }

        EndPattern(entity);
    }
    #endregion

    #region 패턴 2번: 검을 축으로 삼아
    private IEnumerator Pattern2_PivotSpinRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        _controller?.SetStateText($"{Pattern2Label} - 검을 지면에...", Color.yellow);

        Vector2 origin = entity.transform.position;
        bool hijacked = false;

        GameObject bodyTelegraph = BoneMasterTelegraphUtil.SpawnRing(entity, origin, pivotBodySlamRadius, telegraphWarnColor);
        float bt = 0f;
        while (bt < pivotBodyTelegraphLead)
        {
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin);
            bt += Time.deltaTime;
            yield return null;
        }
        if (bodyTelegraph != null) Object.Destroy(bodyTelegraph);
        if (hijacked)
        {
            Debug.LogWarning("[BoneMaster] 페이즈2 패턴 2번이 몸통박치기 예고 중 외부 요인으로 중단됨.");
            yield break;
        }

        var gauge = _controller != null ? _controller.CounterGauge : null;
        bool broken = false;
        void OnBroken() => broken = true;
        if (gauge != null)
        {
            gauge.OnGaugeBroken += OnBroken;
            gauge.OpenWindow(pivotCounterGaugeAmount);
        }

        var bodyInfo = new DamageInfo(entity.Stats.ATK * 0.7f, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealCircle(origin, pivotBodySlamRadius, entity.opponentLayer, bodyInfo, excludeRadius: pivotSafeRadius);

        float t = 0f;
        while (t < pivotCounterWindow)
        {
            if (broken) break;
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin);
            t += Time.deltaTime;
            yield return null;
        }
        if (gauge != null) gauge.OnGaugeBroken -= OnBroken;

        if (hijacked)
        {
            gauge?.CloseWindow();
            Debug.LogWarning("[BoneMaster] 페이즈2 패턴 2번이 카운터 창 중 외부 요인으로 중단됨.");
            yield break;
        }

        if (broken)
        {
            gauge.CloseWindow();
            _controller?.SetStateText($"{Pattern2Label} - 균형 붕괴! 경직!", Color.cyan);
            _controller?.ApplyGroggy(pivotCounterStaggerDuration);
        }
        else
        {
            gauge?.CloseWindow();
            _controller?.SetStateText($"{Pattern2Label} - 마무리 베기 예고!", Color.yellow);
            GameObject finishTelegraph = BoneMasterTelegraphUtil.SpawnRing(entity, origin, pivotFinishRadius, telegraphWarnColor);
            float ft = 0f;
            bool finishHijacked = false;
            while (ft < pivotFinishTelegraphLead)
            {
                if (entity.CurrentState != AIState.Skill) { finishHijacked = true; break; }
                Warp(entity, origin);
                ft += Time.deltaTime;
                yield return null;
            }
            if (finishTelegraph != null) Object.Destroy(finishTelegraph);

            if (finishHijacked)
            {
                Debug.LogWarning("[BoneMaster] 페이즈2 패턴 2번이 마무리베기 예고 중 외부 요인으로 중단됨.");
                yield break;
            }

            _controller?.SetStateText($"{Pattern2Label} - 마무리 베기!", Color.white);
            var finishInfo = new DamageInfo(entity.Stats.ATK * 1.1f, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
            BossCombat.DealCircle(origin, pivotFinishRadius, entity.opponentLayer, finishInfo, excludeRadius: pivotFinishSafeRadius);
            yield return new WaitForSeconds(0.6f);
        }

        EndPattern(entity);
    }
    #endregion

    #region 패턴 3번: 카운터 & 페이크 카운터
    private IEnumerator Pattern3_CounterRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        yield return BoneMasterCounterUtil.Run(
            entity, _controller, counterReactionWindow,
            counterSuccessStaggerDuration, fakeCounterPlayerStun, fakeCounterPunishDamage, Pattern3Label);

        EndPattern(entity);
    }
    #endregion
}
