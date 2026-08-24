using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("도약 착지점이 플레이어를 따라다니는 시간(초). 이 시간이 지나면 착지점이 확정된다.")]
    public float leapWindup = 0.4f;
    [Tooltip("착지점이 확정된 뒤 실제로 뛰어오르기까지 기다리는 시간(초). " +
             "플레이어가 빠져나갈 수 있는 실질 회피 시간 = 이 값 + leapDuration. " +
             "0으로 두면 예전처럼 '따라오다 바로 착지'가 되어 사실상 회피가 불가능해진다.")]
    public float leapSlamLockTime = 0.65f;
    public float leapDuration = 0.35f;
    public float basicAttackWindup = 0.85f;
    public float basicAttackRecovery = 0.4f;
    [Tooltip("도약 & 내려찍기의 피해 배율(ATK 대비).")]
    public float leapSlamDamageMultiplier = 1.2f;

    [Header("패턴 쿨타임 (인스펙터에서 조절)")]
    [Tooltip("쿨타임은 패턴이 '시작'하는 순간부터 잰다(패턴 수행 시간이 쿨에 포함된다). " +
             "패턴과 패턴 사이에 실제로 비는 시간은 아래 postPatternRecovery 가 따로 보장한다.")]
    public float chargeCooldown = 12f;
    public float thrustCooldown = 6f;
    public float counterCooldown = 10f;

    // ── 패턴 간격 ────────────────────────────────────────────────────
    //
    // [추가 — "패턴 사이에 시간이 없다"는 피드백]
    // 예전엔 패턴이 끝나면서 CurrentState 가 Follow 로 돌아간 '바로 다음 프레임'에 새 패턴을
    // 뽑을 수 있었다. 쿨타임은 패턴 시작 시점부터 재므로(예: 찌르기 쿨 6초 - 패턴 길이 약 3초)
    // 실제로 숨 돌릴 틈이 3초도 안 됐고, 카운터를 성공시켜도 그로기가 풀리는 즉시 다음 패턴이
    // 시작돼서 '파훼 보상'이 체감되지 않았다.
    // 두 값을 분리해 둔 이유: 평소 호흡(postPatternRecovery)과 파훼 보상 딜타임(postGroggyRecovery)은
    // 튜닝 의도가 서로 다르다 — 전자는 난이도, 후자는 플레이어 보상이다.
    [Header("패턴 간격 (숨 돌릴 틈 / 파훼 보상 딜타임)")]
    [Tooltip("특수 패턴이 끝난 뒤 다음 '특수 패턴'까지 최소로 비워 두는 시간(초). " +
             "이 동안에도 기본 공격과 추격은 정상적으로 한다 — 보스가 멈춰 있는 게 아니다.")]
    public float postPatternRecovery = 1.5f;
    [Tooltip("카운터 파훼(또는 돌진 벽충돌)로 그로기에 걸린 경우 '추가로' 더 비워 두는 시간(초).\n" +
             "postPatternRecovery 위에 얹히므로, 실제 딜타임 = 그로기 시간 + postPatternRecovery + 이 값 이다.\n" +
             "예) 그로기 5 + 1.5 + 1.5 = 총 8초 동안 특수 패턴이 안 나온다.\n" +
             "  이 중 앞 5초는 그로기라 보스가 완전히 멈춰 있고(브레인 자체가 안 돈다), " +
             "남은 3초 동안만 기본 공격·추격을 한다.")]
    public float postGroggyRecovery = 1.5f;

    [Header("가중치 - 체력 100~80%")]
    public Vector3 weightsAbove80 = new Vector3(18f, 38f, 44f);
    [Header("가중치 - 체력 80~60%")]
    public Vector3 weights80To60 = new Vector3(14f, 40f, 46f);
    [Header("가중치 - 체력 60% 이하")]
    public Vector3 weightsBelow60 = new Vector3(10f, 42f, 48f);
    [Header("가중치 구간 경계 (체력 비율)")]
    [Range(0f, 1f)]
    [Tooltip("이 비율보다 체력이 높으면 weightsAbove80 을 쓴다. 필드 이름의 '80'은 이 기본값에서 온 것이라, " +
             "값을 바꾸면 이름과 실제가 달라지는 점만 유의.")]
    public float weightThresholdHigh = 0.8f;
    [Range(0f, 1f)]
    [Tooltip("이 비율보다 체력이 높으면 weights80To60, 이하면 weightsBelow60 을 쓴다.")]
    public float weightThresholdMid = 0.6f;

    [Header("텔레그래프(피해범위 인디케이터) 색상")]
    public Color telegraphWarnColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    [Tooltip("직선(레인) 전조 프리팹. Assets/Prefabs/Skill Visual Effects/Telegraph Line Hitbox Prefab. " +
             "시각 전용으로만 쓰며 피해는 BossCombat 이 준다(콜라이더가 없는 프리팹).")]
    public BaseHitBox laneTelegraphPrefab;
    [Tooltip("원/타원 전조 프리팹. Assets/Prefabs/Skill Visual Effects/Center Skill Hitbox Circle Prefab.")]
    public BaseHitBox circleTelegraphPrefab;

    [Header("패턴 1번: 박치기 돌격")]
    public float howlPushRadius = 2.5f;
    public float howlPushForce = 4f;
    [Tooltip("[시간] 포효 넉백이 지속되는 시간(초). 밀려나는 거리와 연출 길이를 함께 좌우한다.")]
    public float howlPushDuration = 0.2f;
    public float chargeTelegraphTime = 1.5f;
    public float chargeCounterGaugeAmount = 30f;
    public float chargeCounterGroggyDuration = 5f;
    public float chargeWallStaggerDuration = 1.5f;
    [Tooltip("돌진에 들이받혔을 때의 피해 배율(ATK 대비). 예고한 레인과 정확히 같은 폭으로 판정한다.")]
    public float chargeDamageMultiplier = 1.4f;
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
    [Tooltip("연타 횟수. 마지막 한 대만 '카운터 찬스'(노란 예고 + 파훼 게이지)가 열린다. " +
             "3보다 크게 잡으면 3타 이후의 타격 간 후딜은 thrustPauseAfterExtra 를 쓴다.")]
    public int thrustStrikeCount = 3;
    public float thrustPauseBeforeFirst = 0.75f;
    public float thrustPauseAfterFirst = 0.3f; // [수정] 후딜이 너무 길다는 피드백 — 구간1(1->2타) 총합 0.75초(후딜0.3+조준0.45)로 단축.
    public float thrustPauseAfterSecond = 0f; // [수정] 후딜이 너무 길다는 피드백 — 구간2(2->3타) 총합 1.0초(후딜0+조준1.0)로 단축.
    [Tooltip("[시간] 연타를 4회 이상으로 늘렸을 때, 3타 이후 타격들 사이의 공통 후딜(초).")]
    public float thrustPauseAfterExtra = 0.3f;
    [Tooltip("마지막 타격을 제외한 타격들의 피해 배율(ATK 대비). 마지막 타는 thrustFinalDamageMultiplier 를 쓴다.")]
    public float thrustDamageMultiplier = 1f;
    [Tooltip("마지막(카운터 찬스) 타격의 예고 색. 일반 타격과 확실히 구분되는 색이어야 한다.")]
    public Color thrustFinalTelegraphColor = Color.yellow;
    public float thrustCounterGaugeAmount = 25f;
    public float thrustCounterGroggyDuration = 4f; // [수정] 그로기가 너무 길다는 피드백으로 5->4초.
    [Range(0f, 1f)]
    [Tooltip("찌르기 각 타격이 출혈을 얹을 확률.")]
    public float thrustBleedChance = 0.25f;
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
    [Tooltip("3연타를 끝까지 마친 뒤의 후딜(초). 파훼당했을 땐 그로기가 대신 들어가므로 적용되지 않는다.")]
    public float thrustFinishRecovery = 0.3f;

    [Header("패턴 3번: 카운터 & 페이크 카운터")]
    [Tooltip("아웃라인이 빛나기 시작한 뒤 '판정이 열리기까지'의 유예 시간(초).\n" +
             "이 동안은 노랑이든 빨강이든 아무 판정도 없다 — 때려도 파훼가 안 되고 역공도 안 당한다.\n" +
             "색을 보고 손을 뗄 시간을 주는 값이라, 0으로 두면 '빨개진 순간 이미 휘두르던 공격'이 그대로 처벌된다.")]
    public float counterGraceTime = 1f;
    [Tooltip("유예가 끝난 뒤 판정이 유효한 시간(초). x~y 사이에서 매번 무작위로 뽑는다.\n" +
             "패턴 총 길이 = counterGraceTime + 이 값.")]
    public Vector2 counterReactionWindowRange = new Vector2(1f, 1.5f);
    [Range(0f, 1f)]
    [Tooltip("이번 카운터가 '페이크(빨강, 치면 역공)'일 확률. 0이면 항상 진짜, 1이면 항상 페이크.")]
    public float fakeCounterChance = 0.5f;
    [Tooltip("진짜(노랑) 창을 파훼하는 데 필요한 총 피해량. 1이면 사실상 아무 공격이나 한 대면 성공.")]
    public float counterGaugeAmount = 1f;
    [Tooltip("'지금 때리면 파훼된다'를 뜻하는 아웃라인 색. 패턴3뿐 아니라 카운터 게이지가 열리는 모든 구간" +
             "(돌진 정면딜 / 3연타 마지막 / P2 광역기)에 똑같이 쓰인다.\n" +
             "★ 노랑은 피하는 게 좋다 — 보스는 슈퍼아머가 상시라 노란 아웃라인이 이미 항상 켜져 있어서 구분이 안 된다.")]
    public Color counterRealColor = new Color(0.2f, 1f, 0.3f);
    [Tooltip("페이크 카운터('치면 역공')의 아웃라인 색. 위 색과 확실히 구분되는 색이어야 한다.")]
    public Color counterFakeColor = Color.red;
    public float counterSuccessGroggyDuration = 2.5f;
    [Tooltip("[시간] 페이크에 낚였을 때 플레이어가 묶이는 경직 시간(초).")]
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
    // 이 시각 전에는 어떤 특수 패턴도 안 뽑는다(기본 공격/추격은 계속한다). EndPattern 이 갱신.
    private float _specialLockUntil = -100f;
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
        _specialLockUntil = -100f;

        // 파훼 가능 신호색을 컨트롤러에 알려준다. 컨트롤러가 카운터 게이지 상태에 물려 아웃라인을
        // 켜고 끄므로, 패턴마다 창을 여닫는 12개 지점을 일일이 손대지 않아도 신호가 일관된다.
        _controller?.SetCounterChanceColor(counterRealColor);

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
            float interval = entity.Stats != null ? entity.Stats.AttackInterval : -1f;
            float lockLeft = Mathf.Max(0f, _specialLockUntil - Time.time);
            Debug.Log($"[BoneMaster-Diag] dist={dist:F1} engageRange={effectiveEngageRange:F1} AtkTimer={entity.AtkTimer:F2}/{interval:F2} 특수잠금={lockLeft:F2}s CurrentState={entity.CurrentState} IsAttacking={entity.IsAttacking}");
        }

        if (dist > effectiveEngageRange)
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중...");
            return;
        }

        float castSpeedBonus = _controller != null ? _controller.PatternCastSpeedBonus : 0f;
        float cdMul = 1f / (1f + castSpeedBonus);

        // [추가] 직전 패턴이 끝난 뒤의 '숨 돌릴 틈'. 이 동안에도 아래 기본 공격 분기는 정상적으로 탄다.
        bool specialsReady = Time.time >= _specialLockUntil;

        bool canCharge = specialsReady && Time.time - _lastChargeTime >= chargeCooldown * cdMul;
        bool canThrust = specialsReady && Time.time - _lastThrustTime >= thrustCooldown * cdMul;
        bool canCounter = specialsReady && Time.time - _lastCounterTime >= counterCooldown * cdMul;

        if (!canCharge && !canThrust && !canCounter)
        {
            FallBackToBasic(entity);
            return;
        }

        Vector3 hpWeights = GetWeights(entity);
        float wCharge = canCharge ? Mathf.Max(0f, hpWeights.x) : 0f;
        float wThrust = canThrust ? Mathf.Max(0f, hpWeights.y) : 0f;
        float wCounter = canCounter ? Mathf.Max(0f, hpWeights.z) : 0f;
        float total = wCharge + wThrust + wCounter;

        if (total <= 0f)
        {
            // 쿨은 돌았는데 가중치가 전부 0인 경우(특정 패턴만 테스트하려고 나머지를 0으로 둘 때 흔하다).
            // 예전엔 그냥 return 이라 CurrentState 를 아무도 안 바꿔서 보스가 직전 상태로 굳었다.
            FallBackToBasic(entity);
            return;
        }

        // [버그 수정 — 룰렛 경계에서 쿨다운 중인 패턴이 발동] Random.value 는 1.0 을 포함하므로
        // roll 이 정확히 total 이 될 수 있는데, 마지막 분기가 무조건 else 였다. 그 순간 카운터가
        // 쿨다운 중(가중치 0)이어도 패턴3이 발동하고 _lastCounterTime 까지 갱신됐다.
        float roll = Random.value * total;
        int pick = roll < wCharge ? 0 : (roll < wCharge + wThrust ? 1 : 2);
        if (pick == 2 && wCounter <= 0f) pick = wThrust > 0f ? 1 : 0;

        entity.CurrentState = AIState.Skill;

        switch (pick)
        {
            case 0:
                _lastChargeTime = Time.time;
                StartPattern(entity, Pattern1_ChargeRoutine(entity));
                break;
            case 1:
                _lastThrustTime = Time.time;
                StartPattern(entity, Pattern2_ThrustRoutine(entity));
                break;
            default:
                _lastCounterTime = Time.time;
                StartPattern(entity, Pattern3_CounterRoutine(entity));
                break;
        }
    }

    /// <summary>
    /// i번째 타격 직후의 후딜(초). 1·2타는 각자 전용 값을 쓰고, 연타를 4회 이상으로 늘렸을 때의
    /// 나머지 구간은 공통값을 쓴다. (기존 에셋에 저장된 thrustPauseAfterFirst/Second 를 그대로
    /// 살리기 위해 배열이 아니라 이 방식으로 둔다 — 배열로 바꾸면 저장된 튜닝값이 날아간다.)
    /// 마지막 타격의 후딜은 여기가 아니라 thrustFinishRecovery 가 담당한다.
    /// </summary>
    private float PauseAfterStrike(int index)
    {
        if (index == 0) return thrustPauseAfterFirst;
        if (index == 1) return thrustPauseAfterSecond;
        return thrustPauseAfterExtra;
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
            _controller?.SetStateText("추격 중...");
        }
    }

    /// <summary>
    /// 특수 패턴 코루틴은 반드시 컨트롤러를 거쳐 돌린다 — 컨트롤러가 핸들을 잡고 있어야
    /// 사망/부위파괴/페이즈 전환 때 확실히 끊을 수 있다(BoneMasterController.RunPattern 주석 참조).
    /// </summary>
    private void StartPattern(BaseEntity entity, IEnumerator routine)
    {
        if (_controller != null) _controller.RunPattern(routine);
        else entity.StartCoroutine(routine);
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

private IEnumerator BasicAttack_LeapSlam(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.SetStateText("기본 공격: 도약 준비", Color.yellow);
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        float radiusX = leapSlamRadiusX * rangeMul;
        float radiusY = leapSlamRadiusY * rangeMul;

        Vector2 landPos = entity.Target != null ? (Vector2)entity.Target.position : (Vector2)entity.transform.position;

        // [버그 수정 — 도약이 사실상 회피 불가였던 문제]
        // 예전엔 예고 시간 전체(leapWindup + basicAttackWindup*0.3 = 0.655초) 동안 착지점이 매 프레임
        // 플레이어를 따라다녔다. 즉 "어디로 떨어지는지"가 예고가 끝나는 순간에야 확정되고, 그 뒤
        // 도약 시간(0.35초)만이 실제 회피 시간이었다. 이동속도 5 기준 0.35초에 1.75유닛인데,
        // 투구를 깨면 타원 최소축이 1.6 × 1.15 = 1.84유닛이 되어 **어느 방향으로도 걸어서 못 나갔다.**
        // 앞 구간에서만 추적하고 뒤 구간은 위치를 굳혀서, 확정 후 회피 시간을 확보한다.
        float trackTime = leapWindup;                           // 이 동안만 착지점이 따라온다
        float lockTime = Mathf.Max(0f, leapSlamLockTime);        // 위치를 굳히고 기다리는 시간

        // life 가 도약 시간까지 덮어야 한다 — 예고가 끝나도 착지(:Destroy)까지는 장판이 떠 있어야 하니까.
        // windup 은 '실제 착지 순간'까지로 잡는다 — 프리팹의 차오름 게이지가 가득 차는 시점과
        // 피해가 들어오는 시점이 일치해야 게이지가 거짓말을 하지 않는다.
        GameObject telegraph = BoneMasterTelegraphUtil.SpawnEllipse(
            entity, landPos, radiusX, radiusY, telegraphWarnColor, circleTelegraphPrefab,
            trackTime + lockTime + leapDuration, leapDuration + 0.2f);

        float t = 0f;
        while (t < trackTime)
        {
            t += Time.deltaTime;
            if (entity.Target != null)
            {
                landPos = entity.Target.position;
                BoneMasterTelegraphUtil.UpdatePosition(telegraph, landPos);
            }
            yield return null;
        }

        // 착지점 확정 — 여기서부터는 절대 안 따라간다. 플레이어가 빠져나갈 수 있는 구간.
        _controller?.SetStateText("기본 공격: 도약 지점 확정!", Color.yellow);
        BoneMasterTelegraphUtil.UpdatePosition(telegraph, landPos);

        float lockT = 0f;
        while (lockT < lockTime)
        {
            lockT += Time.deltaTime;
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

        var info = new DamageInfo(entity.Stats.ATK * leapSlamDamageMultiplier, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss, causesHitstun: true);
        BossCombat.DealEllipse(landPos, radiusX, radiusY, entity.opponentLayer, info);

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

    /// <summary>
    /// [버그 수정 — 취소된 기본 공격의 전조가 씬에 남던 문제]
    /// AIPatternSO.OnAttackCancelled 의 계약은 "루틴이 만든 월드 오브젝트를 여기서 치운다"인데
    /// 몸통이 비어 있었다. 기본 공격이 windup 도중 CancelAttack 으로 끊기면 루틴 안의
    /// Object.Destroy(telegraph) 가 실행되지 못하고, 전조는 보스의 자식이 아니라 월드 루트
    /// 오브젝트라 보스와 함께 사라지지도 않는다.
    /// </summary>
    public override void OnAttackCancelled(BaseEntity entity)
    {
        base.OnAttackCancelled(entity);
        _controller?.CleanupDanglingTelegraphs();
    }

    private Vector3 GetWeights(BaseEntity entity)
    {
        if (entity.Stats == null || entity.Stats.Health == null) return weightsAbove80;
        float ratio = entity.Stats.Health.CurHP / entity.Stats.Health.MaxHP;
        if (ratio > weightThresholdHigh) return weightsAbove80;
        if (ratio > weightThresholdMid) return weights80To60;
        return weightsBelow60;
    }

    /// <summary>
    /// 특수 패턴 종료. CurrentState 를 풀어 주는 동시에 다음 특수 패턴까지의 최소 간격을 건다.
    /// </summary>
    /// <param name="extraLock">
    /// postPatternRecovery 위에 더 얹을 시간. 파훼(그로기)로 끝난 경우 "그로기 시간 + postGroggyRecovery"를
    /// 넘겨서, 그로기가 풀리자마자 다음 패턴이 시작되지 않고 실제 딜타임이 생기게 한다.
    /// </param>
    private void EndPattern(BaseEntity entity, float extraLock = 0f)
    {
        entity.CurrentState = AIState.Follow;
        _specialLockUntil = Time.time + Mathf.Max(0f, postPatternRecovery) + Mathf.Max(0f, extraLock);
    }

    /// <summary>파훼로 끝난 패턴의 마무리. 그로기가 끝난 뒤 postGroggyRecovery 만큼 더 쉰다.</summary>
    private void EndPatternAfterGroggy(BaseEntity entity, float groggyDuration)
        => EndPattern(entity, Mathf.Max(0f, groggyDuration) + Mathf.Max(0f, postGroggyRecovery));

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
                status?.ApplyKnockback(dir0, howlPushForce, howlPushDuration);
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
            entity, origin, lockedDir, chargeDistance, chargeTelegraphWidth, telegraphWarnColor,
            laneTelegraphPrefab, chargeTelegraphTime * csMul);

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
            EndPatternAfterGroggy(entity, chargeCounterGroggyDuration);
            yield break;
        }
        else
        {
            gauge?.CloseWindow();
            _controller?.SetStateText($"{Pattern1Label} - 돌진!", Color.white);

            // [버그 수정 — 돌진에 피해 판정이 아예 없던 문제]
            // 이 패턴은 폭 chargeTelegraphWidth 짜리 빨간 레인을 1.5초나 예고하고 22u/s 로 달려오는데,
            // 루틴 전체에 BossCombat 호출이 단 한 줄도 없어서 정통으로 들이받혀도 데미지가 0이었다.
            // (시작 시 howlPushRadius 안쪽을 밀어내는 넉백만 있었고 그것도 피해는 없다.)
            //
            // 돌진은 '지나간 자리'가 판정이라, 정지 패턴처럼 끝에서 한 번 때리면 몸을 관통당해도
            // 안 맞는다. 매 프레임 prev->next 구간을 훑되(DealLaneOnce), 이미 맞은 대상은
            // chargeHits 로 걸러 한 번만 맞게 한다. 폭은 예고한 레인과 정확히 같은 값을 쓴다 —
            // 그림과 판정이 같은 변수에서 나와야 어긋날 수 없다.
            var chargeHits = new HashSet<GameObject>();
            var chargeInfo = new DamageInfo(
                entity.Stats.ATK * chargeDamageMultiplier,
                DamageType.Physical,
                entity.gameObject,
                category: DamageCategory.EnemyBoss,
                causesHitstun: true);

            float elapsed = 0f;
            bool hitWall = false;
            Vector3 prevPos = entity.transform.position;
            while (elapsed < chargeDuration)
            {
                if (entity == null || (entity.Stats != null && entity.Stats.Health != null && entity.Stats.Health.IsDead))
                {
                    _controller?.HardStopMovement();
                    yield break;
                }

                if (entity.CurrentState != AIState.Skill)
                {
                    hijacked = true;
                    Debug.LogWarning($"[BoneMaster] 패턴 1번(박치기 돌격)이 돌진 도중 외부 요인으로 중단됨(CurrentState={entity.CurrentState}, elapsed={elapsed:F2}/{chargeDuration:F2}).");
                    break;
                }

                Vector3 nextPos = entity.transform.position + (Vector3)(lockedDir * chargeSpeed * Time.deltaTime);
                elapsed += Time.deltaTime;

                // 이동 판정보다 먼저 훑는다 — 벽에 닿아 멈추는 프레임의 구간도 피해가 들어가야 한다.
                float segLen = Vector2.Distance(prevPos, nextPos);
                if (segLen > 0.0001f)
                {
                    BossCombat.DealLaneOnce(prevPos, lockedDir, segLen, chargeTelegraphWidth,
                                            entity.opponentLayer, chargeInfo, chargeHits);
                }

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

            // 벽에 처박은 것도 '플레이어가 회피에 성공해서 얻어낸' 딜타임이므로 파훼와 같이 취급한다.
            EndPatternAfterGroggy(entity, chargeWallStaggerDuration);
            yield break;
        }
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

        int strikeCount = Mathf.Max(1, thrustStrikeCount);

        for (int i = 0; i < strikeCount && !broken && !hijacked; i++)
        {
            bool isFinal = (i == strikeCount - 1); // 마지막 타격만 카운터 찬스가 열린다
            float dashDist = isFinal ? thrustFinalDashDistance : thrustDashDistance;
            // [수정] 돌진 거리만큼 사거리가 "추가로" 늘어난다(기존 정지 판정 길이 + 돌진 거리).
            float totalLen = (isFinal ? thrustFinalRange : thrustRange) + dashDist;
            float width = isFinal ? thrustFinalWidth : thrustWidth;
            float lead = isFinal ? thrustFinalTelegraphLead : thrustTelegraphLead;
            Color telegraphColor = isFinal ? thrustFinalTelegraphColor : telegraphWarnColor;
            float dmgMul = isFinal ? thrustFinalDamageMultiplier : thrustDamageMultiplier;
            float dashDur = isFinal ? thrustFinalDashDuration : thrustDashDuration;

            // [설계 변경 - 제자리 찌르기가 심심하다는 피드백] 매 타격마다 "현재" 위치에서 다시 조준한다.
            // 이전 타격의 대시로 이동한 지점이 다음 타격의 시작점이 되므로, 3연타를 거치며 보스가
            // 실제로 플레이어 쪽으로 파고드는 느낌을 만든다.
            Vector2 origin = entity.transform.position;
            if (entity.Target != null) entity.LookAtTarget(entity.Target);
            Vector2 dir = SafeDirTo(entity, origin, entity.Target);

            GameObject strikeTelegraph = BoneMasterTelegraphUtil.SpawnLane(
                entity, origin, dir, totalLen, width, telegraphColor, laneTelegraphPrefab, lead * csMul);

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

            bool applyBleed = Random.value <= thrustBleedChance;
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
                float pause = PauseAfterStrike(i);
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
            EndPatternAfterGroggy(entity, thrustCounterGroggyDuration);
        }
        else
        {
            gauge?.CloseWindow();
            yield return new WaitForSeconds(thrustFinishRecovery);
            EndPattern(entity);
        }
    }
    #endregion

    #region 패턴 3번: 카운터 & 페이크 카운터
    private IEnumerator Pattern3_CounterRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();
        float reactionWindow = Random.Range(counterReactionWindowRange.x, counterReactionWindowRange.y);

        var result = new BoneMasterCounterUtil.Result();
        yield return BoneMasterCounterUtil.Run(
            entity, _controller, reactionWindow,
            counterSuccessGroggyDuration, fakeCounterPlayerStun, fakeCounterPunishDamage, Pattern3Label, result,
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
