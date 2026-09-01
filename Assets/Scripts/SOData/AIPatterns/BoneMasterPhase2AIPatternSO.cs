using System.Collections;
using UnityEngine;

/// <summary>
/// 본 마스터 페이즈 2 AI. 갑옷/랜스가 무너지고 양손검으로 전환된 이후의 전투.
/// 보스 패턴은 거리로 고르는 셋뿐이고, 같은 것을 두 번 연속으로는 쓰지 않는다(PickMove).
///   근거리(closeRange 이내)  → 휩쓸고 내려찍기 / 견갑 찌르기
///   중거리(midRange 이내)    → 견갑 찌르기 / 도약
///   원거리(engageRange 이내) → 도약 (연속 사용 허용)
/// 여기에 더해 페이즈2 진입 직후 '집행'이 딱 한 번 나간다(ExecutionRoutine).
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
    // ★ 거리 구간 — 페이즈1(BoneMasterAIPatternSO)과 완전히 같은 규칙이다. 그쪽 주석 참조.
    //   closeRange 이내 → 휩쓸고 내려찍기 / 견갑 찌르기
    //   midRange   이내 → 견갑 찌르기 / 도약
    //   engageRange 이내 → 도약 (연속 사용 허용)
    // 페이즈2는 몸이 가벼워진 설정이라 구간을 페이즈1보다 한 뼘씩 좁게 잡았다(더 붙어 싸운다).
    [Header("★ 거리 구간 (패턴 선택)")]
    [Tooltip("이 거리 안이면 근거리 — 휩쓸고 내려찍기 / 견갑 찌르기 중에서 고른다.")]
    public float closeRange = 3.2f;
    [Tooltip("이 거리 안이면 중거리 — 견갑 찌르기 / 도약 중에서 고른다.")]
    public float midRange = 6f;
    [Tooltip("이 거리 안이면 원거리 — 도약. 이 밖이면 추격한다(chaseTimeLimit 까지).")]
    public float engageRange = 8.5f;

    [Header("★ 패턴 간격 / 추격")]
    [Tooltip("패턴이 정상적으로 끝난 뒤 다음 패턴까지의 최소 간격(초). = 패턴 사이 추격 시간.")]
    public float attackGap = 1f;
    [Tooltip("engageRange 밖에서 이 시간(초) 넘게 쫓아다니면 거리와 무관하게 패턴을 강행한다(도약).")]
    public float chaseTimeLimit = 2f;

    [Header("보스 패턴 - 사거리")]
    public float sweepRadius = 3.64f;
    public float sweepHalfAngle = 90f;
    [Tooltip("휩쓸기 예고 중 플레이어를 따라 도는 최대 회전 속도(도/초). 0 이면 무제한(=완전 추적).")]
    public float sweepTurnSpeed = 0f;
    public float basicThrustLength = 6.75f;
    public float basicThrustWidth = 1.4f;
    public float basicAttackWindup = 0.8f;
    public float basicAttackRecovery = 0.5f;

    [Header("견갑 찌르기 (찌르기 2연타)")]
    [Tooltip("연타 횟수. 마지막 타격만 카운터가 가능하다 — 1타는 무채색으로 뜬다.")]
    public int thrustStrikeCount = 2;
    [Tooltip("조준이 끝난 뒤 창을 내밀며 미끄러지는 거리(유닛). 벽이 가까우면 그 직전까지만.")]
    public float thrustDashDistance = 2.5f;
    [Tooltip("위 거리를 미끄러지는 데 걸리는 시간(초).")]
    public float thrustDashDuration = 0.12f;
    [Tooltip("1타와 2타 사이의 간격(초). 페이즈1보다 짧게 — 몰아치는 느낌을 준다.")]
    public float thrustPauseBetween = 0.18f;
    [Tooltip("보스 몸 두께. 미끄러지는 중 벽 접촉을 이 반지름으로 검사한다.")]
    public float wallCheckRadius = 0.85f;

    [Header("도약 & 내려찍기 (카운터 없음)")]
    [Tooltip("착지 판정 타원의 가로/세로 반지름.")]
    public float leapSlamRadiusX = 2.2f;
    public float leapSlamRadiusY = 1.6f;
    [Tooltip("착지점이 플레이어를 따라다니는 시간(초). 페이즈1(0.4)보다 짧다 — 시전이 빨라진다.")]
    public float leapWindup = 0.3f;
    [Tooltip("착지점 확정 후 뛰어오르기까지 기다리는 시간(초). 실질 회피 시간 = 이 값 + leapDuration.")]
    public float leapSlamLockTime = 0.45f;
    [Tooltip("실제 체공 시간(초).")]
    public float leapDuration = 0.3f;
    [Tooltip("도약 & 내려찍기의 피해 배율(ATK 대비).")]
    public float leapSlamDamageMultiplier = 1.2f;

    [Header("패턴 간격 (숨 돌릴 틈 / 파훼 보상 딜타임)")]
    [Tooltip("패턴이 파훼(그로기)로 끝난 뒤 보스가 다음 행동을 못 하는 시간(초). " +
             "그로기 시간 위에 얹히므로 실제 딜타임 = 그로기 + 이 값 이다.")]
    public float postPatternRecovery = 1f;
    [Tooltip("[미사용] 특수 패턴과 기본 공격이 분리돼 있던 시절의 추가 딜타임. 통합 후로는 쓰지 않는다.")]
    public float postGroggyRecovery = 1.5f;

    [Header("텔레그래프(피해범위 인디케이터) 색상")]
    public Color telegraphWarnColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    [Tooltip("직선(레인) 전조 프리팹. Assets/Prefabs/Skill Visual Effects/Telegraph Line Hitbox Prefab. " +
             "시각 전용으로만 쓰며 피해는 BossCombat 이 준다(콜라이더가 없는 프리팹).")]
    public BaseHitBox laneTelegraphPrefab;
    [Tooltip("원형/타원 전조 프리팹. 도약 착지 지점 예고에 쓴다(페이즈1과 같은 프리팹).")]
    public BaseHitBox circleTelegraphPrefab;

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

    [Header("휩쓸고 내려찍기")]
    [Tooltip("휩쓸기를 시전하며 앞으로 미끄러지는 거리(유닛). " +
             "★ 판정 반원도 바닥 부채꼴도 '전진이 끝난 위치'가 중심이다. 즉 0 이 아니면 예고가 " +
             "보스보다 그만큼 앞에 그려진다 — 그림이 틀린 게 아니라 실제로 거기를 때린다. " +
             "부채꼴을 보스 한가운데에 놓으려면 0 으로 둬라(현재값).")]
    public float sweepStepDistance = 0f;
    [Tooltip("위 거리를 미끄러지는 데 걸리는 시간(초).")]
    public float sweepStepDuration = 0.12f;
    [Tooltip("내려찍기 예고 시간(초). 이 구간은 카운터가 불가능하다 — 카운터는 앞의 휩쓸기에서만.")]
    public float slamTelegraphTime = 1.2f;
    public float slamRange = 6.5f; // [수정] 원형 반경 -> 보스 기준 뻗는 직사각형의 길이로 의미가 바뀜
    public float slamWidth = 2f; // [추가] 직사각형 내려찍기의 폭
    [Tooltip("휩쓸기 판정이 나간 뒤 내려찍기 예고가 시작되기까지의 사이(초).")]
    public float spinToSlamPause = 0.35f;
    [Tooltip("내려찍기까지 정상적으로 끝냈을 때의 후딜(초).")]
    public float slamFinishRecovery = 0.8f;
    [Tooltip("내려찍기의 피해 배율(ATK 대비).")]
    public float slamDamageMultiplier = 1.15f;

    [Header("특수 패턴: 집행 (페이즈2 진입 1회)")]
    [Tooltip("이 횟수만큼 카운터에 성공해야 집행이 끝난다. 성공할 때까지 선 패턴 → 등장 → 카운터가 " +
             "무한히 반복된다(0830 확정 — 실패해도 빠져나갈 길은 없다).\n\n" +
             "체력바 아래 구슬 개수도 이 값을 따라간다. 프리팹의 구슬 수(3개)보다 크게 잡으면 " +
             "표시가 그 이상을 못 보여주므로, 늘릴 거면 Boss Counter Pips 프리팹도 같이 늘려라.")]
    public int executionRequiredHits = 3;
    [Tooltip("한 사이클에서 긋는 선의 개수. 기획 기준 4개.")]
    public int executionLinesPerCycle = 4;
    [Tooltip("첫 선의 예고 시간(초). 선이 그어지고 이만큼 뒤에 그 자리가 판정된다.")]
    public float executionLineLeadStart = 1f;
    [Tooltip("한 사이클의 마지막 선 예고 시간(초). 회차가 갈수록 이 값까지 짧아진다(= 점점 빨라진다).")]
    public float executionLineLeadEnd = 0.55f;
    [Tooltip("선 사이의 간격(초).")]
    public float executionLineGap = 0.25f;
    [Tooltip("선의 폭(유닛). 무한 반복으로 바뀌면서 얇게 내렸다 — 두꺼우면 회피 공간이 안 남는다.")]
    public float executionLineWidth = 1f;
    [Tooltip("선에 맞았을 때의 피해 배율(ATK 대비).")]
    public float executionLineDamageMultiplier = 0.8f;
    [Tooltip("마지막 선의 판정이 끝나고 보스가 플레이어 옆에 나타나기까지의 시간(초).\n\n" +
             "★ 이 값이 짧으면 '보스가 나오기도 전에 선에 맞는' 느낌이 난다 — 카운터를 준비하는 " +
             "순간과 마지막 선의 판정이 겹치기 때문이다. 실제 여유 = 이 값 + executionLineGap.")]
    public float executionAppearDelay = 0.6f;
    [Tooltip("보스가 플레이어 주변 어느 거리 안에 나타나는가(유닛). 기획 기준 1.")]
    public float executionAppearRadius = 1f;
    [Tooltip("집행 중 카운터 패턴의 시전 속도 배수. 0.75 = 25% 빨라짐. " +
             "(0.6 = 40% 였는데 카운터를 넣을 여유가 안 난다는 피드백으로 하향했다.)")]
    [Range(0.2f, 1f)] public float executionCastSpeedScale = 0.75f;
    [Tooltip("집행을 파훼했을 때(= 요구 횟수만큼 카운터에 성공했을 때) 보스가 먹는 그로기 시간(초).")]
    public float executionGroggyDuration = 4f;
    [Tooltip("위 그로기 동안 추가되는 받는 피해(합연산). 0.15 = +15%. 그로기가 끝나면 사라진다.")]
    public float executionDamageBonus = 0.15f;

    [Header("카운터 전조 (모든 패턴 공용)")]
    [Range(0f, 1f)]
    [Tooltip("이번 전조가 '페이크(빨강)'일 확률. 0830 수정안 기준 0.7 = 노랑:빨강 30:70.")]
    public float fakeCounterChance = 0.7f;
    [Tooltip("노랑 창을 파훼하는 데 필요한 총 피해량. 1이면 아무 공격이나 한 대면 성공.")]
    public float counterGaugeAmount = 1f;
    [Tooltip("노랑(진짜 카운터) 전조 색. 인디케이터가 이 색으로 찬다.")]
    public Color counterRealColor = new Color(1f, 0.9f, 0.2f);
    [Tooltip("빨강(페이크) 전조 색. 치면 보스가 예고를 건너뛰고 즉시 시전한다.")]
    public Color counterFakeColor = new Color(1f, 0.15f, 0.15f);
    [Tooltip("카운터가 불가능한 패턴(도약 & 내려찍기, 내려찍기 후속타)의 전조 색. 무채색 = '쳐도 소용없다'.")]
    public Color counterNoneColor = new Color(0.75f, 0.75f, 0.78f);
    [Tooltip("노랑 카운터에 성공했을 때 보스가 먹는 경직 시간(초). 이 동안 패턴이 취소된다.")]
    public float counterSuccessGroggyDuration = 0.5f;


    // ==============================================================
    // 애니메이션 스테이트 이름
    // ==============================================================
    // 페이즈1과 프리팹/애니메이터를 공유하므로 기본공격 스테이트 이름도 같다.
    // 패턴 두 개는 한 클립이 동작 두 박자를 담고 있어서, 앞 박자는 배속을 맞춰 늘리고
    // 뒤 박자는 '늦게 틀어서' 판정과 겹친다(배속을 안 건드리니 뒷동작이 안 느려진다).
    [Header("애니메이션 스테이트 이름 (비우면 공용 Attack 으로 폴백)")]
    [Tooltip("기본공격: 양손검 찌르기.")]
    public string animState_Thrust = "Attack_Prod";
    [Tooltip("기본공격: 양손검 휩쓸기.")]
    public string animState_Sweep = "Attack_Sweep";
    [Tooltip("패턴1. 앞 타격 = 광역 회전 베기, 뒤 타격 = 내려찍기.")]
    public string animState_SpinSlam = "Pattern_SweepChop";
    [Tooltip("도약의 준비~체공. 1회 클립이라 마지막 프레임(점프 자세)에서 저절로 홀드된다.")]
    public string animState_Jump = "Attack_Jump";
    [Tooltip("도약의 낙하~내려찍기. 타격 프레임이 앞쪽이라 착지 순간과 겹치게 늦게 튼다.")]
    public string animState_JumpFall = "Attack_Jump_Fall";
    [Tooltip("패턴3 카운터 대기 자세. 1프레임 홀드.")]
    public string animState_Counter = "Pattern_Counter";
    [Tooltip("패턴3 카운터 성공 반격.")]
    public string animState_CounterSuccess = "Pattern_Counter_Success";
    [Tooltip("뒤 박자를 다시 틀 때 클립의 어디서부터 재생할지(0~1). 앞 타격 직후를 가리켜야 " +
             "앞동작이 두 번 보이지 않는다.")]
    [Range(0f, 0.95f)] public float secondBeatClipStart = 0.45f;
    [Tooltip("클립 길이를 예비동작 시간에 맞춰 Animator.speed 를 자동 조절한다(기준점은 첫 타격 프레임).")]
    public bool matchAnimSpeedToWindup = true;

    private const string Pattern1Label = "패턴 1번: 회전 베기 & 내려찍기";
    private const string Pattern2Label = "패턴 2번: 검을 축으로 삼아";
    private const string Pattern3Label = "패턴 3번: 카운터 & 페이크 카운터";

    /// <summary>보스 패턴 3종. 같은 패턴을 두 번 연속으로 쓰지 않기 위해 직전 것을 기억한다.</summary>
    private enum Move { None, Sweep, Thrust, Leap }
    private Move _lastMove = Move.None;
    private float _chaseStartTime = -100f;

    private BoneMasterController _controller;
    private float _lastDiagLogTime = -100f;
    // 이 시각 전에는 어떤 특수 패턴도 안 뽑는다(기본 공격/추격은 계속한다). EndPattern 이 갱신.
    private float _specialLockUntil = -100f;

    /// <summary>'집행'은 페이즈2에 딱 한 번만 나온다. 브레인 인스턴스가 페이즈2 전환 때 새로
    /// 만들어지므로(Instantiate) 이 플래그는 전투 1회당 한 번 리셋되는 것과 같다.</summary>
    private bool _executionDone;
    private bool _firstTickDone = false;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _controller = entity as BoneMasterController;

        // 잠금은 여기서 걸지 않는다. Init 은 페이즈2 '전환 연출 도중'(체력이 차오르기 전)에 불리므로
        // 여기서 Time.time 기준으로 잡으면 연출 시간(약 1초)이 잠금을 갉아먹어 의도한 간격이 안 나온다.
        // 대신 브레인이 실제로 처음 판단하는 시점(= 연출이 끝나고 CurrentState 가 풀린 뒤)에 건다.
        _specialLockUntil = -100f;
        _executionDone = false;
        _firstTickDone = false;
        _lastMove = Move.None;
        _chaseStartTime = Time.time;

        // 파훼 가능 신호색을 컨트롤러에 알려준다(P1 과 동일 — 그쪽 주석 참조).
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

        // '집행'은 페이즈2 전환 연출이 끝난 직후 딱 한 번, 다른 무엇보다 먼저 나간다.
        if (!_executionDone)
        {
            _executionDone = true;
            entity.CurrentState = AIState.Skill;
            StartPattern(entity, ExecutionRoutine(entity));
            return;
        }

        // 카운터 파훼 뒤의 딜타임 + 패턴 사이 최소 간격(attackGap).
        if (Time.time < _specialLockUntil)
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중... (페이즈2)");
            // 추격 시간은 '때릴 수 있게 된 순간'부터 센다. 여기서 리셋하지 않으면 딜타임이
            // 그대로 추격 시간으로 계산돼, 잠금이 풀리는 즉시 강제 패턴이 튀어나온다.
            _chaseStartTime = Time.time;
            return;
        }

        // [버그 수정 — 패턴마다 공속 1회분(1초)의 추격이 강제로 붙던 문제]
        // AtkTimer 는 Execute() 안에서만 증가하는데 Execute 는 IsAttacking 이면 안 돈다.
        // 자세한 내용은 페이즈1(BoneMasterAIPatternSO.UpdateStateTransitions) 주석 참조.
        bool inRange = dist <= effectiveEngageRange;
        if (!inRange && Time.time - _chaseStartTime < Mathf.Max(0f, chaseTimeLimit))
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중... (페이즈2)");
            return;
        }

        entity.CurrentState = AIState.Attack;
    }

    /// <summary>이번에 쓸 패턴. 거리로 후보를 좁히고 직전과 같은 것은 피한다(원거리 도약만 예외).</summary>
    private Move PickMove(float dist, float rangeMul)
    {
        if (dist > midRange * rangeMul) return Move.Leap;

        Move a, b;
        if (dist <= closeRange * rangeMul) { a = Move.Sweep; b = Move.Thrust; }
        else { a = Move.Thrust; b = Move.Leap; }

        if (_lastMove == a) return b;
        if (_lastMove == b) return a;
        return Random.value < 0.5f ? a : b;
    }

    /// <summary>다음 패턴까지의 간격을 걸고 추격 제한시간을 리셋한다.</summary>
    private void ArmNextAttack(float extraLock = 0f)
    {
        float until = Time.time + Mathf.Max(0f, attackGap) + Mathf.Max(0f, extraLock);
        _specialLockUntil = Mathf.Max(_specialLockUntil, until);
        _chaseStartTime = Time.time;
    }

    /// <summary>특수 패턴 코루틴은 반드시 컨트롤러를 거쳐 돌린다(사망/전환 시 확실히 끊기 위해).</summary>
    private void StartPattern(BaseEntity entity, IEnumerator routine)
    {
        if (_controller != null) _controller.RunPattern(routine);
        else entity.StartCoroutine(routine);
    }

    /// <summary>앞으로 미끄러질 실제 거리. 벽이 더 가까우면 그 직전에서 끊는다.</summary>
    /// <summary>
    /// 미끄러질 때만 쓰는 벽 탐지 반지름. wallCheckRadius(0.85)를 그대로 쓰면 안 된다 —
    /// NavMesh 베이크 agentRadius 가 0.5 라 보스가 벽에 0.5 까지 붙을 수 있는데, 그러면
    /// CircleCast 시작 원이 벽과 겹쳐(Physics2D 의 QueriesStartInColliders 가 켜져 있다)
    /// 거리 0 이 나온다. 0.5 보다 작게 잡아야 '벽에 붙었다'와 '앞이 막혔다'가 구분된다.
    /// </summary>
    private const float SlideProbeRadius = 0.3f;

    private float SlideDistance(Vector2 origin, Vector2 dir, float wanted)
    {
        if (_controller == null) return wanted;

        // [버그 수정 — 벽을 뚫고 미끄러지던 문제] 예전엔 0 을 '제한 없음'으로 해석해서,
        // 벽에 붙어 있을 때 오히려 벽 체크가 통째로 꺼지고 원하는 거리를 그대로 나아갔다.
        // 0 은 '한 칸도 못 간다'가 맞다.
        float toWall = _controller.GetChargeDistance(origin, dir, SlideProbeRadius);
        return Mathf.Clamp(toWall, 0f, wanted);
    }

    /// <summary>노랑 카운터를 파훼당했다. 패턴을 취소하고 경직 + 딜타임까지 건다.</summary>
    private void CancelByCounter(BaseEntity entity)
    {
        _controller?.SetStateText("카운터 성공! 패턴 취소!", Color.cyan);
        _controller?.ApplyGroggy(counterSuccessGroggyDuration);

        // [버그 수정 — 카운터 성공이 3.5초 정지가 되던 문제] 예전엔 EndPatternAfterGroggy 를 불러서
        // postPatternRecovery(1.5) + 그로기(0.5) + postGroggyRecovery(1.5) = 3.5초를 잠갔다.
        // 특수 패턴과 기본 공격이 분리돼 있던 시절엔 그 3초에도 기본 공격이 나갔지만, 통합 이후엔
        // 기본 공격이 보스의 유일한 공격 경로라 그대로 무행동이 된다. 스펙의 보상은 '패턴 취소 +
        // 0.5초 경직'이므로 딜타임은 postPatternRecovery 하나만 얹는다(그로기 포함 총 2초).
        EndPattern(entity, counterSuccessGroggyDuration);
        FinishBasicAttack(entity);
    }

    /// <summary>이번 예고의 성질과 색을 한 번에 뽑는다.</summary>
    private BossCounterTelegraph.Kind RollTelegraph(bool counterable, out Color color)
    {
        var kind = BossCounterTelegraph.Roll(counterable, fakeCounterChance);
        color = BossCounterTelegraph.ColorOf(kind, counterRealColor, counterFakeColor, counterNoneColor);
        return kind;
    }

    // ── 보스 패턴 3종: 거리로 고른다 ────────────────────────────────
protected override void OnAttack(BaseEntity entity)
    {
        StopNavAgent(entity);
        if (entity.IsAttacking) return;

        entity.AtkTimer = 0f;
        entity.IsAttacking = true;

        float dist = entity.Target != null ? Vector2.Distance(entity.transform.position, entity.Target.position) : 0f;
        // [버그 수정 — 투구 파괴 효과 미적용] 페이즈2에서도 AttackRangeBonus가 전혀 적용되지 않고 있었다.
        // 부위파괴 보너스는 페이즈 전환 후에도 누적 유지되는 설계이므로 여기서도 반영해야 한다.
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);

        Move move = PickMove(dist, rangeMul);
        _lastMove = move;

        IEnumerator routine = move == Move.Sweep ? BasicAttack_Sweep(entity)
                            : move == Move.Thrust ? BasicAttack_Thrust(entity)
                            : BasicAttack_LeapSlam(entity);

        entity.ActiveAttackCoroutine = entity.StartCoroutine(routine);
    }

    private void FinishBasicAttack(BaseEntity entity)
    {
        // 배속은 Animator 전역 상태라 여기서 반드시 1 로 되돌린다.
        if (entity != null && entity.Animator != null) entity.Animator.speed = 1f;
        entity.IsAttacking = false;
        entity.ActiveAttackCoroutine = null;
        entity.ResetAnimationState();
        ArmNextAttack();
    }

    /// <summary>
    /// 휩쓸고 내려찍기 — 살짝 전진하며 반원으로 쓸고, 이어서 정면으로 내려찍는다.
    /// <b>카운터는 앞의 휩쓸기에서만 가능하다</b>(0830 확정). 내려찍기 예고는 무채색으로 뜬다.
    /// </summary>
    private IEnumerator BasicAttack_Sweep(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();

        if (entity.Target != null) entity.LookAtTarget(entity.Target);
        Vector2 origin = entity.transform.position;
        Vector2 dir = SafeDirTo(entity, origin, entity.Target);
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        float csMul = 1f / (1f + (_controller != null ? _controller.PatternCastSpeedBonus : 0f));
        float radius = sweepRadius * rangeMul;
        float windup = basicAttackWindup * csMul;

        Vector2 stepEnd = origin + dir * SlideDistance(origin, dir, sweepStepDistance);

        // ── 1타: 휩쓸기 (카운터 가능) ────────────────────────────────
        var kind = RollTelegraph(counterable: true, out Color col);
        _controller?.SetStateText("휩쓸고 내려찍기", col);

        GameObject cone = BoneMasterTelegraphUtil.SpawnCone(entity, stepEnd, dir, radius, sweepHalfAngle, col);
        PlayState(entity, animState_SpinSlam, windup, matchAnimSpeedToWindup);

        // 페이즈1 휩쓸기와 같은 규칙 — 예고 동안 회전 상한(sweepTurnSpeed) 안에서 계속 조준한다.
        var tele = new BossCounterTelegraph.Result();
        yield return BossCounterTelegraph.Run(entity, _controller, windup, dir, kind, col,
                                              counterGaugeAmount, tele,
                                              onTick: () =>
                                              {
                                                  Warp(entity, origin);
                                                  dir = BoneMasterAIPatternSO.AimToward(entity, dir, origin, sweepTurnSpeed);
                                                  if (entity.Target != null) entity.LookAtTarget(entity.Target);
                                                  stepEnd = origin + dir * SlideDistance(origin, dir, sweepStepDistance);
                                                  BoneMasterTelegraphUtil.UpdateCone(
                                                      cone, stepEnd, dir,
                                                      windup > 0.0001f ? tele.Elapsed / windup : 1f);
                                                  BossAttackIndicator.Aim(entity, dir);
                                              });
        if (cone != null) Object.Destroy(cone);

        if (tele.Hijacked) { FinishBasicAttack(entity); yield break; }
        if (tele.Countered) { CancelByCounter(entity); yield break; }

        // [주의] 전진 거리가 0 이면 루프를 아예 건너뛴다. 그냥 돌면 제자리에서 sweepStepDuration 만큼
        // 시간만 흘러서, 인디케이터가 가득 찬 뒤 그만큼 늦게 판정이 나간다(= 게이지가 거짓말을 한다).
        if ((stepEnd - origin).sqrMagnitude > 0.0001f)
        {
            float st = 0f;
            float stepDur = Mathf.Max(0.01f, sweepStepDuration * csMul);
            while (st < stepDur)
            {
                st += Time.deltaTime;
                Warp(entity, Vector2.Lerp(origin, stepEnd, Mathf.Clamp01(st / stepDur)));
                yield return null;
            }
            Warp(entity, stepEnd);
        }

        var sweepInfo = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealCone(stepEnd, dir, radius, sweepHalfAngle, entity.opponentLayer, sweepInfo);

        // ── 2타: 내려찍기 (카운터 없음) ──────────────────────────────
        // [버그 수정] 여기서 Idle 로 끊지 않으면 Pattern_SweepChop 클립이 계속 흘러서, 뒤 박자인
        // 내려찍기 모션이 예고가 뜨기도 전에 한 번 지나가 버린다(그러면 아래에서 다시 틀 때 두 번 보인다).
        if (spinToSlamPause > 0f)
        {
            PlayState(entity, "Idle");
            yield return new WaitForSeconds(spinToSlamPause);
        }
        if (entity.Target != null) entity.LookAtTarget(entity.Target);
        Vector2 slamDir = SafeDirTo(entity, stepEnd, entity.Target);
        float slamLead = slamTelegraphTime * csMul;

        _controller?.SetStateText("내려찍기!", counterNoneColor);
        GameObject lane = BoneMasterTelegraphUtil.SpawnLane(
            entity, stepEnd, slamDir, slamRange, slamWidth, counterNoneColor, laneTelegraphPrefab, slamLead);

        // 같은 클립의 뒤 타격(내려찍기)이 판정 순간에 겹치도록 예고가 끝나기 직전에 다시 튼다.
        float beatLead = SecondBeatLead(entity, animState_SpinSlam);
        bool beatPlayed = false;

        var slamTele = new BossCounterTelegraph.Result();
        yield return BossCounterTelegraph.Run(entity, _controller, slamLead, slamDir,
                                              BossCounterTelegraph.Kind.None, counterNoneColor,
                                              counterGaugeAmount, slamTele,
                                              onTick: () =>
                                              {
                                                  Warp(entity, stepEnd);
                                                  if (!beatPlayed && slamLead - slamTele.Elapsed <= beatLead)
                                                  {
                                                      PlayState(entity, animState_SpinSlam, startNormalized: secondBeatClipStart);
                                                      beatPlayed = true;
                                                  }
                                              });
        if (lane != null) Object.Destroy(lane);
        if (!beatPlayed && !slamTele.Hijacked)
            PlayState(entity, animState_SpinSlam, startNormalized: secondBeatClipStart);

        if (slamTele.Hijacked) { FinishBasicAttack(entity); yield break; }

        var slamInfo = new DamageInfo(entity.Stats.ATK * slamDamageMultiplier, DamageType.Physical,
                                      entity.gameObject, category: DamageCategory.EnemyBoss, causesHitstun: true);
        BossCombat.DealLane(stepEnd, slamDir, slamRange, slamWidth, entity.opponentLayer, slamInfo);

        yield return new WaitForSeconds(slamFinishRecovery);
        FinishBasicAttack(entity);
    }

    /// <summary>
    /// 견갑 찌르기 — 찌르기를 연속으로 시전한다(기본 2연타).
    /// <b>마지막 타격만 카운터가 가능하다</b>(0830 확정). 앞 타격들은 무채색으로 뜬다.
    /// </summary>
    private IEnumerator BasicAttack_Thrust(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();

        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        float csMul = 1f / (1f + (_controller != null ? _controller.PatternCastSpeedBonus : 0f));
        float length = basicThrustLength * rangeMul;
        float width = basicThrustWidth * rangeMul;
        float windup = basicAttackWindup * csMul;
        int strikes = Mathf.Max(1, thrustStrikeCount);

        for (int i = 0; i < strikes; i++)
        {
            bool isFinal = i == strikes - 1;

            // 타수마다 다시 조준한다 — 앞 타격의 돌진으로 이동한 지점이 다음 타격의 시작점이 된다.
            if (entity.Target != null) entity.LookAtTarget(entity.Target);
            Vector2 origin = entity.transform.position;
            Vector2 dir = SafeDirTo(entity, origin, entity.Target);

            var kind = RollTelegraph(counterable: isFinal, out Color col);
            _controller?.SetStateText(isFinal ? "견갑 찌르기 - 마지막!" : "견갑 찌르기", col);

            GameObject telegraph = BoneMasterTelegraphUtil.SpawnLane(
                entity, origin, dir, length, width, col, laneTelegraphPrefab, windup);
            PlayState(entity, animState_Thrust, windup, matchAnimSpeedToWindup);

            var tele = new BossCounterTelegraph.Result();
            yield return BossCounterTelegraph.Run(entity, _controller, windup, dir, kind, col,
                                                  counterGaugeAmount, tele,
                                                  onTick: () => Warp(entity, origin));
            if (telegraph != null) Object.Destroy(telegraph);

            if (tele.Hijacked) { FinishBasicAttack(entity); yield break; }
            if (tele.Countered) { CancelByCounter(entity); yield break; }

            float slide = SlideDistance(origin, dir, thrustDashDistance);
            Vector2 slideEnd = origin + dir * slide;
            float dt = 0f;
            float dur = Mathf.Max(0.01f, thrustDashDuration * csMul);
            while (dt < dur)
            {
                dt += Time.deltaTime;
                Warp(entity, Vector2.Lerp(origin, slideEnd, Mathf.Clamp01(dt / dur)));
                yield return null;
            }
            Warp(entity, slideEnd);

            var info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
            BossCombat.DealLane(origin, dir, length, width, entity.opponentLayer, info);

            if (!isFinal) yield return new WaitForSeconds(thrustPauseBetween * csMul);
        }

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

    private float SecondBeatLead(BaseEntity entity, string stateName)
    {
        var anim = entity != null ? entity.Animator : null;
        if (anim == null) return 0f;
        float len = StateClipLength(anim, stateName);
        float last = StateLastHitEventTime(anim, stateName);
        return Mathf.Max(0f, last - len * Mathf.Clamp01(secondBeatClipStart));
    }

    private void EndPattern(BaseEntity entity, float extraLock = 0f)
    {
        // 배속은 Animator 전역 상태다. 여기서 1 로 안 되돌리면 패턴이 늘려놓은 배속이
        // 다음 추격/기본공격 모션까지 그대로 따라간다.
        if (entity != null && entity.Animator != null) entity.Animator.speed = 1f;
        entity.CurrentState = AIState.Follow;
        _specialLockUntil = Time.time + Mathf.Max(0f, postPatternRecovery) + Mathf.Max(0f, extraLock);
    }

    #region 특수 패턴: 집행 (페이즈2 진입 1회)

    /// <summary>
    /// 집행 — 페이즈2 진입 직후 1회성.
    ///
    ///   (선 패턴 ×N, 갈수록 빨라짐) → 보스 은신 → 플레이어 바로 옆에 등장
    ///   → 조금 빨라진 찌르기/휩쓸기 1회(<b>무조건 노랑</b>) → 다시 은신
    /// 을 <b>카운터에 executionRequiredHits 번 성공할 때까지 무한히</b> 반복한다.
    /// 실패로 빠져나가는 길은 없다(0830 확정) — 성공해야만 끝난다.
    ///
    /// 이 패턴 동안 보스는 카운터 순간을 빼면 계속 숨어 있고 무적이다 — 딜 구간이 아니라
    /// 생존 + 카운터 구간이다. 남은 성공 횟수는 보스 체력바 아래 구슬로 보여준다.
    /// </summary>
    private IEnumerator ExecutionRoutine(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();

        int required = Mathf.Max(1, executionRequiredHits);
        int hits = 0;
        bool aborted = false;

        BossCounterPipsUI.Show(required);

        try
        {
            while (hits < required)
            {
                // ── 선 패턴: 맵을 가로지르는 직선이 플레이어 위를 지난다 ──
                _controller?.SetHidden(true);

                int lines = Mathf.Max(1, executionLinesPerCycle);
                for (int i = 0; i < lines; i++)
                {
                    if (entity == null || entity.CurrentState != AIState.Skill) { aborted = true; break; }

                    // 회차가 갈수록 예고가 짧아진다 = 점점 빨라진다. 한 바퀴 돌면 다시 처음 속도로.
                    float lead = lines <= 1
                        ? executionLineLeadStart
                        : Mathf.Lerp(executionLineLeadStart, executionLineLeadEnd, i / (lines - 1f));

                    yield return ExecutionLine(entity, lead);
                    if (executionLineGap > 0f) yield return new WaitForSeconds(executionLineGap);
                }
                if (aborted) break;

                // ── 등장: 플레이어 바로 옆 ──
                // 이 사이가 짧으면 "보스가 나오기도 전에 선에 맞는" 느낌이 난다. executionAppearDelay 주석 참조.
                yield return new WaitForSeconds(executionAppearDelay);
                if (entity == null || entity.CurrentState != AIState.Skill) { aborted = true; break; }

                WarpBesidePlayer(entity);
                _controller?.SetHidden(false);

                // ── 무조건 노랑. 시전 속도는 executionCastSpeedScale 만큼 빠르다. ──
                var res = new BossCounterTelegraph.Result();
                yield return ExecutionStrike(entity, res);

                if (res.Hijacked) { aborted = true; break; }
                if (res.Countered)
                {
                    hits++;
                    BossCounterPipsUI.SetFilled(hits);
                }
            }
        }
        finally
        {
            // 코루틴이 어디서 끊기든 투명 무적 보스나 떠 있는 구슬로 남으면 안 된다.
            _controller?.SetHidden(false);
            BossCounterPipsUI.Hide();
        }

        if (aborted || entity == null)
        {
            EndPattern(entity);
            yield break;
        }

        // ── 보상: 요구 횟수를 채워야만 여기 도달하므로 고정값이다(0830 확정) ──
        _controller?.SetStateText($"집행 파훼! 받는 피해 +{executionDamageBonus * 100f:F0}%", Color.cyan);
        _controller?.ApplyGroggy(executionGroggyDuration, executionDamageBonus);
        // 예전 EndPatternAfterGroggy 는 그로기(4초)가 끝난 뒤에도 postGroggyRecovery(1.5초)를 더
        // 얹어서 보스가 총 7초를 멍하니 서 있게 만들었다. 보상은 '그로기 + 받는 피해 증가'이지
        // 무행동이 아니므로, 카운터 파훼 때와 같은 규칙으로 딜타임은 postPatternRecovery 만 얹는다.
        EndPattern(entity, executionGroggyDuration);
    }

    /// <summary>선 하나. 플레이어 위를 지나는 무작위 각도의 직선이 방을 통째로 가로지른다.</summary>
    private IEnumerator ExecutionLine(BaseEntity entity, float lead)
    {
        Vector2 through = entity.Target != null ? (Vector2)entity.Target.position : (Vector2)entity.transform.position;

        float ang = Random.value * Mathf.PI;                     // 0~180도면 직선 전체를 다 덮는다
        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

        // 방을 완전히 가로지르도록 플레이어 기준 양쪽으로 뻗는다.
        float back = RoomReach(-dir, through);
        float fwd = RoomReach(dir, through);
        Vector2 origin = through - dir * back;
        float length = back + fwd;

        GameObject lane = BoneMasterTelegraphUtil.SpawnLane(
            entity, origin, dir, length, executionLineWidth, counterNoneColor, laneTelegraphPrefab, lead);

        float t = 0f;
        while (t < lead)
        {
            if (entity == null || entity.CurrentState != AIState.Skill) break;
            t += Time.deltaTime;
            yield return null;
        }

        if (lane != null) Object.Destroy(lane);
        if (entity == null) yield break;

        var info = new DamageInfo(entity.Stats.ATK * executionLineDamageMultiplier, DamageType.Physical,
                                  entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealLane(origin, dir, length, executionLineWidth, entity.opponentLayer, info);
    }

    /// <summary>등장 직후의 일격. 찌르기 또는 휩쓸기 중 하나를, 40% 빠르게, 무조건 노랑으로.</summary>
    private IEnumerator ExecutionStrike(BaseEntity entity, BossCounterTelegraph.Result res)
    {
        if (entity.Target != null) entity.LookAtTarget(entity.Target);
        Vector2 origin = entity.transform.position;
        Vector2 dir = SafeDirTo(entity, origin, entity.Target);

        bool useSweep = Random.value < 0.5f;
        float windup = basicAttackWindup * executionCastSpeedScale;
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);

        _controller?.SetStateText(useSweep ? "집행 - 휩쓸기!" : "집행 - 찌르기!", counterRealColor);

        GameObject telegraph = useSweep
            ? BoneMasterTelegraphUtil.SpawnCone(entity, origin, dir, sweepRadius * rangeMul, sweepHalfAngle, counterRealColor)
            : BoneMasterTelegraphUtil.SpawnLane(entity, origin, dir, basicThrustLength * rangeMul,
                                                basicThrustWidth * rangeMul, counterRealColor, laneTelegraphPrefab, windup);

        PlayState(entity, useSweep ? animState_Sweep : animState_Thrust, windup, matchAnimSpeedToWindup);

        // 집행의 카운터 구간은 추첨하지 않는다 — 반드시 노랑(Real)이다.
        //
        // [버그 수정 — "부채꼴이 예고 없이 툭 뜬다"] 레인 전조에는 차오름 게이지가 프리팹에 딸려
        // 있는데 부채꼴은 코드로 그려서 정지 그림이었다. 이제 매 프레임 진행도를 먹인다.
        yield return BossCounterTelegraph.Run(entity, _controller, windup, dir,
                                              BossCounterTelegraph.Kind.Real, counterRealColor,
                                              counterGaugeAmount, res,
                                              onTick: () =>
                                              {
                                                  Warp(entity, origin);
                                                  if (useSweep)
                                                      BoneMasterTelegraphUtil.UpdateCone(
                                                          telegraph, origin, dir,
                                                          windup > 0.0001f ? res.Elapsed / windup : 1f);
                                              });
        if (telegraph != null) Object.Destroy(telegraph);

        if (res.Hijacked || res.Countered) yield break;

        var info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        if (useSweep) BossCombat.DealCone(origin, dir, sweepRadius * rangeMul, sweepHalfAngle, entity.opponentLayer, info);
        else BossCombat.DealLane(origin, dir, basicThrustLength * rangeMul, basicThrustWidth * rangeMul, entity.opponentLayer, info);
    }

    /// <summary>
    /// 플레이어 바로 옆(executionAppearRadius 이내)으로 순간이동한다.
    ///
    /// [함정] WarpTo 는 NavMesh 밖 좌표를 최대 3유닛까지 끌어당긴다 — 그대로 넘기면 "반드시 1 이내"가
    /// 조용히 깨진다. 그래서 넘기기 전에 좁은 반경(0.5)으로 NavMesh 를 직접 확인하고, 실패하면 재추첨한다.
    /// </summary>
    private void WarpBesidePlayer(BaseEntity entity)
    {
        if (entity.Target == null) return;
        Vector2 p = entity.Target.position;

        for (int i = 0; i < 8; i++)
        {
            Vector2 cand = p + Random.insideUnitCircle.normalized * executionAppearRadius;
            if (UnityEngine.AI.NavMesh.SamplePosition(cand, out var hit, 0.5f, UnityEngine.AI.NavMesh.AllAreas))
            {
                _controller?.WarpTo(hit.position);
                return;
            }
        }
        _controller?.WarpTo(p); // 전부 실패하면 플레이어 발밑
    }

    /// <summary>
    /// 플레이어 위치에서 dir 방향으로 <b>방 경계</b>까지의 거리.
    ///
    /// GetChargeDistance 를 쓰면 안 된다 — 그건 벽 CircleCast 가 섞여 있어서 문 구멍으로 새어나가고,
    /// 방을 못 찾으면 60 을 돌려준다. 선은 '방을 가로지르는' 것이므로 사각형에서 직접 재야
    /// 문 너머나 맵 밖까지 뻗지 않는다.
    /// </summary>
    private float RoomReach(Vector2 dir, Vector2 from)
    {
        const float Fallback = 12f;
        if (_controller == null) return Fallback;
        if (!_controller.TryGetArenaRect(out Vector2 c, out Vector2 h)) return Fallback;

        float d = BoneMasterController.RectExitDistance(from, dir, c, h);
        return d > 0.1f ? d : Fallback;
    }


    #endregion

    #region 도약 & 내려찍기 (페이즈1에서 이식, 카운터 없음)
    private IEnumerator BasicAttack_LeapSlam(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.SetStateText("도약 & 내려찍기", counterNoneColor);
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

        // 준비~체공은 Attack_Jump 하나로 덮는다. 1회 클립이라 다 재생되면 마지막 프레임(점프 자세)에서
        // 저절로 멈춰 있고, 아래 도약 루프 내내 그 자세가 유지된다.
        PlayState(entity, animState_Jump, trackTime + lockTime, matchAnimSpeedToWindup);

        // life 가 도약 시간까지 덮어야 한다 — 예고가 끝나도 착지(:Destroy)까지는 장판이 떠 있어야 하니까.
        // windup 은 '실제 착지 순간'까지로 잡는다 — 프리팹의 차오름 게이지가 가득 차는 시점과
        // 피해가 들어오는 시점이 일치해야 게이지가 거짓말을 하지 않는다.
        GameObject telegraph = BoneMasterTelegraphUtil.SpawnEllipse(
            entity, landPos, radiusX, radiusY, telegraphWarnColor, circleTelegraphPrefab,
            trackTime + lockTime + leapDuration, leapDuration + 0.2f);
        // 도약은 카운터 대상이 아니다(0830 확정). 무채색 = '쳐도 소용없다'는 신호.
        BossAttackIndicator.Begin(entity, trackTime + lockTime + leapDuration, default, counterNoneColor);

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

        // 낙하 모션은 '착지에 타격 프레임이 겹치도록' 늦게 튼다. 배속으로 늘리지 않는 이유는
        // 타격 프레임이 클립 앞쪽(0.1초/0.5초)이라, 배속을 맞추면 착지 후 충격 프레임 0.4초가
        // 그만큼 느려져서 후딜(basicAttackRecovery)보다 길어지기 때문이다. 원속으로 두면 둘이 맞아떨어진다.
        float fallLead = entity.Animator != null ? StateHitEventTime(entity.Animator, animState_JumpFall) : 0f;
        bool fallPlayed = false;

        float elapsed = 0f;
        while (elapsed < leapDuration)
        {
            elapsed += Time.deltaTime;
            if (!fallPlayed && leapDuration - elapsed <= fallLead)
            {
                PlayState(entity, animState_JumpFall);
                fallPlayed = true;
            }
            Warp(entity, Vector3.Lerp(startPos, (Vector3)landPos, elapsed / leapDuration));
            yield return null;
        }
        if (!fallPlayed) PlayState(entity, animState_JumpFall);
        Warp(entity, landPos);
        _controller?.HardStopMovement();

        BossAttackIndicator.Stop(entity);
        if (telegraph != null) Object.Destroy(telegraph);

        var info = new DamageInfo(entity.Stats.ATK * leapSlamDamageMultiplier, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss, causesHitstun: true);
        BossCombat.DealEllipse(landPos, radiusX, radiusY, entity.opponentLayer, info);

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

    #endregion
}
