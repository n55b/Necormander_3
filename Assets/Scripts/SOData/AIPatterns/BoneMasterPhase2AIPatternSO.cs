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
public class BoneMasterPhase2AIPatternSO : BossAIPatternSO
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
    [Tooltip("쿨타임은 패턴이 '시작'하는 순간부터 잰다. 패턴과 패턴 사이에 실제로 비는 시간은 " +
             "아래 postPatternRecovery 가 따로 보장한다.")]
    public float spinSlamCooldown = 9f;
    public float pivotSpinCooldown = 9f;
    public float counterCooldown = 10f;

    [Header("패턴 간격 (숨 돌릴 틈 / 파훼 보상 딜타임)")]
    [Tooltip("특수 패턴이 끝난 뒤 다음 '특수 패턴'까지 최소로 비워 두는 시간(초). 기본 공격·추격은 계속한다.")]
    public float postPatternRecovery = 1.5f;
    [Tooltip("파훼로 그로기에 걸린 경우 '추가로' 더 비워 두는 시간(초).\n" +
             "postPatternRecovery 위에 얹히므로, 실제 딜타임 = 그로기 시간 + postPatternRecovery + 이 값 이다.")]
    public float postGroggyRecovery = 1.5f;

    [Header("가중치 (페이즈2는 체력 구간 없이 고정 33/33/34)")]
    public Vector3 weights = new Vector3(33f, 33f, 34f);

    [Header("텔레그래프(피해범위 인디케이터) 색상")]
    public Color telegraphWarnColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    [Tooltip("직선(레인) 전조 프리팹. Assets/Prefabs/Skill Visual Effects/Telegraph Line Hitbox Prefab. " +
             "시각 전용으로만 쓰며 피해는 BossCombat 이 준다(콜라이더가 없는 프리팹).")]
    public BaseHitBox laneTelegraphPrefab;

    // ── 원형(광역) 전조의 최소 예고 시간 ─────────────────────────────
    //
    // [추가 — "외곽선이 뜨자마자 터진다"는 억까 피드백]
    // 페이즈2 광역기 3종은 전부 정지 링 전조를 썼고, 에셋에 저장된 예고 시간이 0.3~0.4초였다.
    // 그 정도면 링이 그려지는 프레임과 판정이 들어오는 프레임이 사실상 붙어 있어서
    // "빨간 원이 보였다 = 이미 맞았다"가 된다. 반응할 수 있는 하한을 코드에 못박아 둔다 —
    // 에셋 값이 이보다 작아도 여기서 끌어올리므로, 잘못 튜닝해도 회피 불가 패턴이 나오지 않는다.
    // 시각적으로도 SpawnRingCountdown 으로 바꿔서 남은 시간이 띠로 차오르게 했다.
    [Header("광역 전조 공통")]
    [Tooltip("원형(광역) 전조가 뜬 뒤 발동까지 최소로 보장하는 시간(초). 각 패턴의 개별 예고 시간이 " +
             "이보다 짧으면 이 값으로 끌어올린다. 반응 불가 패턴을 구조적으로 막는 하한선.")]
    public float minRingTelegraphLead = 1f;

    [Header("패턴 1번: 회전 베기 & 내려찍기")]
    public float spinRadius = 4f;
    public float spinSafeRadius = 2.2f;
    public float spinTelegraphLead = 1f;
    public float slamTelegraphTime = 1.2f;
    public float slamCounterGaugeAmount = 25f;
    public float slamCounterStaggerDuration = 1.5f;
    public float slamRange = 6.5f; // [수정] 원형 반경 -> 보스 기준 뻗는 직사각형의 길이로 의미가 바뀜
    public float slamWidth = 2f; // [추가] 직사각형 내려찍기의 폭
    public float spinToSlamPause = 0.8f;
    [Tooltip("내려찍기까지 정상적으로 끝냈을 때의 후딜(초).")]
    public float slamFinishRecovery = 0.8f;
    [Tooltip("내려찍기의 피해 배율(ATK 대비).")]
    public float slamDamageMultiplier = 1.15f;

    [Header("패턴 2번: 검을 축으로 삼아")]
    public float pivotBodySlamRadius = 2f;
    public float pivotSafeRadius = 1.8f;
    public float pivotBodyTelegraphLead = 1f;
    public float pivotCounterWindow = 1f;
    public float pivotCounterGaugeAmount = 25f;
    public float pivotCounterStaggerDuration = 2.5f;
    public float pivotFinishRadius = 4f;
    public float pivotFinishSafeRadius = 2.2f;
    public float pivotFinishTelegraphLead = 1f;
    [Tooltip("마무리 베기까지 정상적으로 끝냈을 때의 후딜(초).")]
    public float pivotFinishRecovery = 0.6f;
    [Tooltip("몸통 박치기의 피해 배율(ATK 대비).")]
    public float pivotBodyDamageMultiplier = 0.7f;
    [Tooltip("마무리 베기의 피해 배율(ATK 대비).")]
    public float pivotFinishDamageMultiplier = 1.1f;

    [Header("패턴 3번: 카운터 & 페이크 카운터")]
    [Tooltip("아웃라인이 빛나기 시작한 뒤 '판정이 열리기까지'의 유예 시간(초).\n" +
             "이 동안은 노랑이든 빨강이든 아무 판정도 없다 — 때려도 파훼가 안 되고 역공도 안 당한다.")]
    public float counterGraceTime = 1f;
    [Tooltip("유예가 끝난 뒤 판정이 유효한 시간(초). 패턴 총 길이 = counterGraceTime + 이 값.")]
    public float counterReactionWindow = 1f;
    [Range(0f, 1f)]
    [Tooltip("이번 카운터가 '페이크(빨강, 치면 역공)'일 확률.")]
    public float fakeCounterChance = 0.5f;
    [Tooltip("진짜(노랑) 창을 파훼하는 데 필요한 총 피해량. 1이면 사실상 아무 공격이나 한 대.")]
    public float counterGaugeAmount = 1f;
    [Tooltip("'지금 때리면 파훼된다'를 뜻하는 아웃라인 색. 패턴3뿐 아니라 카운터 게이지가 열리는 모든 구간" +
             "(내려찍기 / 몸통 박치기)에 똑같이 쓰인다. 노랑은 상시 슈퍼아머 아웃라인과 겹치므로 피한다.")]
    public Color counterRealColor = new Color(0.2f, 1f, 0.3f);
    [Tooltip("페이크 카운터('치면 역공')의 아웃라인 색.")]
    public Color counterFakeColor = Color.red;
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
    // 이 시각 전에는 어떤 특수 패턴도 안 뽑는다(기본 공격/추격은 계속한다). EndPattern 이 갱신.
    private float _specialLockUntil = -100f;
    private bool _firstTickDone = false;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _controller = entity as BoneMasterController;
        _lastSpinTime = -100f;
        _lastPivotTime = -100f;
        _lastCounterTime = -100f;

        // 잠금은 여기서 걸지 않는다. Init 은 페이즈2 '전환 연출 도중'(체력이 차오르기 전)에 불리므로
        // 여기서 Time.time 기준으로 잡으면 연출 시간(약 1초)이 잠금을 갉아먹어 의도한 간격이 안 나온다.
        // 대신 브레인이 실제로 처음 판단하는 시점(= 연출이 끝나고 CurrentState 가 풀린 뒤)에 건다.
        _specialLockUntil = -100f;
        _firstTickDone = false;

        // 파훼 가능 신호색을 컨트롤러에 알려준다(P1 과 동일 — 그쪽 주석 참조).
        _controller?.SetCounterChanceColor(counterRealColor);
    }

    /// <summary>
    /// 원형(광역) 전조의 실제 예고 시간.
    /// <b>시전속도 보너스(csMul)를 먼저 곱한 뒤에 하한을 적용한다</b> — 순서가 중요하다.
    /// 반대로 하면 견갑 파괴(시전속도 +15%) 상태에서 1초 × 0.87 = 0.87초가 되어,
    /// "외곽선 뜨고 1초 뒤 발동"이라는 하한이 부위파괴만으로 뚫려 버린다.
    /// 하한은 '반응 가능성'의 보증이므로 보스 버프로 깨질 수 있으면 의미가 없다.
    /// </summary>
    private float RingLead(float configured, float csMul)
        => Mathf.Max(configured * csMul, minRingTelegraphLead);

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


        // 브레인이 실제로 처음 판단하는 프레임 = 페이즈2 전환 연출이 끝난 직후다.
        // 여기서 잠금을 걸어야 의도한 postPatternRecovery 가 연출에 갉아먹히지 않는다.
        if (!_firstTickDone)
        {
            _firstTickDone = true;
            _specialLockUntil = Time.time + Mathf.Max(0f, postPatternRecovery);
        }

        float dist = Vector2.Distance(entity.transform.position, entity.Target.position);

        // [버그 수정 — 페이즈2에 부위파괴 보너스가 반쪽만 적용되던 문제]
        // 부위파괴 보너스는 페이즈 전환 후에도 누적 유지되는 설계인데, 페이즈2의 교전 거리와
        // 패턴 쿨타임에는 전혀 반영되지 않고 있었다. 정작 같은 파일의 기본 공격 판정(OnAttack/
        // BasicAttack_*)은 rangeMul 을 쓰고 있어서 "판정은 커지는데 교전 거리는 그대로"인
        // 비일관 상태였다(= 투구를 깨면 사거리는 늘었는데 그만큼 다가가지 않아 헛방이 늘었다).
        float rangeBonus = _controller != null ? _controller.AttackRangeBonus : 0f;
        float effectiveEngageRange = engageRange * (1f + rangeBonus);

        float castSpeedBonus = _controller != null ? _controller.PatternCastSpeedBonus : 0f;
        float cdMul = 1f / (1f + castSpeedBonus);

        if (Time.time - _lastDiagLogTime > 2f)
        {
            _lastDiagLogTime = Time.time;
            float interval = entity.Stats != null ? entity.Stats.AttackInterval : -1f;
            float lockLeft = Mathf.Max(0f, _specialLockUntil - Time.time);
            Debug.Log($"[BoneMaster-Diag-P2] dist={dist:F1} engageRange={effectiveEngageRange:F1} AtkTimer={entity.AtkTimer:F2}/{interval:F2} 특수잠금={lockLeft:F2}s CurrentState={entity.CurrentState} IsAttacking={entity.IsAttacking}");
        }

        if (dist > effectiveEngageRange)
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중... (페이즈2)");
            return;
        }

        bool specialsReady = Time.time >= _specialLockUntil;
        bool canSpin = specialsReady && Time.time - _lastSpinTime >= spinSlamCooldown * cdMul;
        bool canPivot = specialsReady && Time.time - _lastPivotTime >= pivotSpinCooldown * cdMul;
        bool canCounter = specialsReady && Time.time - _lastCounterTime >= counterCooldown * cdMul;

        if (!canSpin && !canPivot && !canCounter)
        {
            FallBackToBasic(entity);
            return;
        }

        float wSpin = canSpin ? Mathf.Max(0f, weights.x) : 0f;
        float wPivot = canPivot ? Mathf.Max(0f, weights.y) : 0f;
        float wCounter = canCounter ? Mathf.Max(0f, weights.z) : 0f;
        float total = wSpin + wPivot + wCounter;

        if (total <= 0f)
        {
            FallBackToBasic(entity);
            return;
        }

        // Random.value 는 1.0 을 포함한다 — 마지막을 무조건 else 로 두면 그 순간 쿨다운 중인
        // 패턴3이 발동하고 _lastCounterTime 까지 갱신된다. (P1 과 같은 수정)
        float roll = Random.value * total;
        int pick = roll < wSpin ? 0 : (roll < wSpin + wPivot ? 1 : 2);
        if (pick == 2 && wCounter <= 0f) pick = wPivot > 0f ? 1 : 0;

        entity.CurrentState = AIState.Skill;

        switch (pick)
        {
            case 0:
                _lastSpinTime = Time.time;
                StartPattern(entity, Pattern1_SpinSlamRoutine(entity));
                break;
            case 1:
                _lastPivotTime = Time.time;
                StartPattern(entity, Pattern2_PivotSpinRoutine(entity));
                break;
            default:
                _lastCounterTime = Time.time;
                StartPattern(entity, Pattern3_CounterRoutine(entity));
                break;
        }
    }

    /// <summary>특수 패턴을 쓸 수 없는 프레임의 기본 거동. 상태를 반드시 하나로 확정한다.</summary>
    private void FallBackToBasic(BaseEntity entity)
    {
        float interval = entity.Stats != null ? entity.Stats.AttackInterval : 1f;
        if (entity.AtkTimer >= interval)
        {
            entity.CurrentState = AIState.Attack;
        }
        else
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중... (페이즈2)");
        }
    }

    /// <summary>특수 패턴 코루틴은 반드시 컨트롤러를 거쳐 돌린다(사망/전환 시 확실히 끊기 위해).</summary>
    private void StartPattern(BaseEntity entity, IEnumerator routine)
    {
        if (_controller != null) _controller.RunPattern(routine);
        else entity.StartCoroutine(routine);
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
        // [버그 수정 — 투구 파괴 효과 미적용] 페이즈2에서도 AttackRangeBonus가 전혀 적용되지 않고 있었다.
        // 부위파괴 보너스는 페이즈 전환 후에도 누적 유지되는 설계이므로 여기서도 반영해야 한다.
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        IEnumerator routine = dist <= sweepRange * rangeMul ? BasicAttack_Sweep(entity) : BasicAttack_Thrust(entity);
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
        _controller?.SetStateText("기본 공격: 양손검 찌르기", Color.white);
        if (entity.Target != null) entity.LookAtTarget(entity.Target);
        Vector2 origin = entity.transform.position;
        Vector2 dir = SafeDirTo(entity, origin, entity.Target);
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        float length = basicThrustLength * rangeMul;
        float width = basicThrustWidth * rangeMul;

        GameObject telegraph = BoneMasterTelegraphUtil.SpawnLane(
            entity, origin, dir, length, width, telegraphWarnColor, laneTelegraphPrefab, basicAttackWindup);

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

    /// <summary>취소된 기본 공격이 남긴 전조를 치운다(P1 의 같은 훅과 동일한 이유 — 그쪽 주석 참조).</summary>
    public override void OnAttackCancelled(BaseEntity entity)
    {
        base.OnAttackCancelled(entity);
        _controller?.CleanupDanglingTelegraphs();
    }

    /// <summary>특수 패턴 종료. 다음 특수 패턴까지의 최소 간격을 함께 건다.</summary>
    private void EndPattern(BaseEntity entity, float extraLock = 0f)
    {
        entity.CurrentState = AIState.Follow;
        _specialLockUntil = Time.time + Mathf.Max(0f, postPatternRecovery) + Mathf.Max(0f, extraLock);
    }

    /// <summary>파훼로 끝난 패턴의 마무리. 그로기가 끝난 뒤 postGroggyRecovery 만큼 더 쉰다.</summary>
    private void EndPatternAfterGroggy(BaseEntity entity, float groggyDuration)
        => EndPattern(entity, Mathf.Max(0f, groggyDuration) + Mathf.Max(0f, postGroggyRecovery));

    #region 패턴 1번: 회전 베기 & 내려찍기
private IEnumerator Pattern1_SpinSlamRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        _controller?.SetStateText($"{Pattern1Label} - 광역 회전 베기!", Color.yellow);

        Vector2 origin = entity.transform.position;
        bool hijacked = false;
        // [버그 수정 — 견갑 파괴 효과 미적용] 페이즈1에서 넘어온 PatternCastSpeedBonus를 예고 시간에 반영한다.
        float csMul = 1f / (1f + (_controller != null ? _controller.PatternCastSpeedBonus : 0f));

        // 정지 링 대신 '차오르는' 도넛 전조를 쓴다. 띠가 안전지대(spinSafeRadius)에서 바깥
        // (spinRadius)으로 차올라 외곽선에 닿는 순간이 발동이라, 언제/어디가 위험한지가 같이 읽힌다.
        float spinLead = RingLead(spinTelegraphLead, csMul);
        GameObject spinTelegraph = BoneMasterTelegraphUtil.SpawnRingCountdown(
            entity, origin, spinRadius, spinSafeRadius, telegraphWarnColor);

        float leadT = 0f;
        while (leadT < spinLead)
        {
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin);
            BoneMasterTelegraphUtil.UpdateRingCountdown(spinTelegraph, spinRadius, spinSafeRadius, leadT / spinLead);
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
        yield return new WaitForSeconds(spinToSlamPause * csMul);
        Warp(entity, origin);

        // [수정 — 내려찍기 회피 불가 문제] 예전엔 내려찍기가 "플레이어 위치를 텔레그래프 내내 계속
        // 추적하는 원형" 판정이라, 플레이어가 어디로 움직이든 그 자리로 계속 따라와서 사실상 피할 수
        // 없었다. 이제는 돌진/찌르기와 같은 방식으로 예고 시작 시점에 방향을 한 번만 고정하고, 보스
        // 기준 그 방향으로 뻗는 긴 직사각형(검을 내려찍는 궤적)으로 바꿔서 옆/뒤로 피할 수 있게 한다.
        _controller?.SetStateText($"{Pattern1Label} - 내려찍기 예고!", Color.yellow);
        Vector2 slamDir = SafeDirTo(entity, origin, entity.Target);
        GameObject slamTelegraph = BoneMasterTelegraphUtil.SpawnLane(
            entity, origin, slamDir, slamRange, slamWidth, telegraphWarnColor,
            laneTelegraphPrefab, slamTelegraphTime * csMul);

        var gauge = _controller != null ? _controller.CounterGauge : null;
        bool broken = false;
        void OnBroken() => broken = true;
        if (gauge != null)
        {
            gauge.OnGaugeBroken += OnBroken;
            gauge.OpenWindow(slamCounterGaugeAmount);
        }

        float t = 0f;
        while (t < slamTelegraphTime * csMul)
        {
            if (broken) break;
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin);
            t += Time.deltaTime;
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
            EndPatternAfterGroggy(entity, slamCounterStaggerDuration);
        }
        else
        {
            gauge?.CloseWindow();
            _controller?.SetStateText($"{Pattern1Label} - 내려찍기!", Color.white);
            var slamInfo = new DamageInfo(entity.Stats.ATK * slamDamageMultiplier, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss, causesHitstun: true);
            BossCombat.DealLane(origin, slamDir, slamRange, slamWidth, entity.opponentLayer, slamInfo);
            yield return new WaitForSeconds(slamFinishRecovery);
            EndPattern(entity);
        }
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
        float csMul = 1f / (1f + (_controller != null ? _controller.PatternCastSpeedBonus : 0f));

        // 예전엔 정지 링을 0.3초만 띄우고 곧바로 DealCircle 이 나갔다 — 링이 보이는 프레임과
        // 맞는 프레임이 사실상 붙어 있어서 회피가 성립하지 않았다. 차오르는 전조 + 최소 예고 하한.
        float bodyLead = RingLead(pivotBodyTelegraphLead, csMul);
        GameObject bodyTelegraph = BoneMasterTelegraphUtil.SpawnRingCountdown(
            entity, origin, pivotBodySlamRadius, pivotSafeRadius, telegraphWarnColor);

        float bt = 0f;
        while (bt < bodyLead)
        {
            if (entity.CurrentState != AIState.Skill) { hijacked = true; break; }
            Warp(entity, origin);
            BoneMasterTelegraphUtil.UpdateRingCountdown(bodyTelegraph, pivotBodySlamRadius, pivotSafeRadius, bt / bodyLead);
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

        var bodyInfo = new DamageInfo(entity.Stats.ATK * pivotBodyDamageMultiplier, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealCircle(origin, pivotBodySlamRadius, entity.opponentLayer, bodyInfo, excludeRadius: pivotSafeRadius);

        float t = 0f;
        while (t < pivotCounterWindow * csMul)
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
            EndPatternAfterGroggy(entity, pivotCounterStaggerDuration);
        }
        else
        {
            gauge?.CloseWindow();
            _controller?.SetStateText($"{Pattern2Label} - 마무리 베기 예고!", Color.yellow);

            float finishLead = RingLead(pivotFinishTelegraphLead, csMul);
            GameObject finishTelegraph = BoneMasterTelegraphUtil.SpawnRingCountdown(
                entity, origin, pivotFinishRadius, pivotFinishSafeRadius, telegraphWarnColor);

            float ft = 0f;
            bool finishHijacked = false;
            while (ft < finishLead)
            {
                if (entity.CurrentState != AIState.Skill) { finishHijacked = true; break; }
                Warp(entity, origin);
                BoneMasterTelegraphUtil.UpdateRingCountdown(finishTelegraph, pivotFinishRadius, pivotFinishSafeRadius, ft / finishLead);
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
            var finishInfo = new DamageInfo(entity.Stats.ATK * pivotFinishDamageMultiplier, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
            BossCombat.DealCircle(origin, pivotFinishRadius, entity.opponentLayer, finishInfo, excludeRadius: pivotFinishSafeRadius);
            yield return new WaitForSeconds(pivotFinishRecovery);
            EndPattern(entity);
        }
    }
    #endregion

    #region 패턴 3번: 카운터 & 페이크 카운터
    private IEnumerator Pattern3_CounterRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        var result = new BoneMasterCounterUtil.Result();
        yield return BoneMasterCounterUtil.Run(
            entity, _controller, counterReactionWindow,
            counterSuccessStaggerDuration, fakeCounterPlayerStun, fakeCounterPunishDamage, Pattern3Label, result,
            graceTime: counterGraceTime,
            fakeChance: fakeCounterChance,
            gaugeAmount: counterGaugeAmount,
            realColor: counterRealColor,
            fakeColor: counterFakeColor);

        if (result.Countered) EndPatternAfterGroggy(entity, result.GroggyDuration);
        else EndPattern(entity);
    }
    #endregion
}
