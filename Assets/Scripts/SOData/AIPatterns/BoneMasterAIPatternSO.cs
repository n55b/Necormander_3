using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 본 마스터 페이즈 1 AI. 0830 수정안 기준 — 기본 공격과 특수 패턴이 하나로 통합돼서
/// <b>보스 패턴은 아래 셋뿐이다.</b> 어느 것을 쓸지는 <see cref="OnAttack"/> 이 거리로 고른다.
///
///   근거리(closeRange 이내)  → 휩쓸기 / 찌르기
///   중거리(midRange 이내)    → 찌르기 / 도약
///   원거리(engageRange 이내) → 도약 &amp; 내려찍기 (착지 지점 타원, 카운터 없음)
///
/// 같은 패턴을 두 번 연속으로는 쓰지 않는다(원거리 도약만 예외). <see cref="PickMove"/> 참조.
///
/// 삭제된 것: 박치기 돌격(버그 다발), 견갑 찌르기 3연타(페이즈2의 2연타로 이동),
/// 카운터 전용 패턴(위 세 패턴의 전조에 흡수).
///
/// 카운터는 이제 별도 패턴이 아니라 <b>전조의 성질</b>이다 — 찌르기·휩쓸기의 예고 동안
/// 인디케이터가 노랑(진짜: 때리면 패턴 취소)이나 빨강(페이크: 때리면 즉시 시전)으로 찬다.
/// 확률은 <see cref="fakeCounterChance"/>, 매 전조마다 독립 추첨.
///
/// [핵심 버그 수정 1] NavMeshAgent가 붙어있는 상태에서 entity.transform.position을 직접 대입하면
/// 에이전트 내부 상태와 어긋나 나중에 "튕기는" 버그가 생긴다. WarpTo()로 통일했다.
///
/// [핵심 버그 수정 2] 패턴 도중 CurrentState가 외부에서 바뀌면 즉시 중단하는 방어 코드가
/// 모든 패턴 루프에 들어가 있다 — 없으면 경직이 씹히거나 죽은 보스가 계속 때린다.
/// </summary>
[CreateAssetMenu(fileName = "BoneMasterAIPattern", menuName = "Necromancer/AI/BoneMasterPattern")]
public class BoneMasterAIPatternSO : BossAIPatternSO
{
    // ==============================================================
    // ★ 거리 구간 — "어느 패턴이 나오는가"는 전부 이 세 값이 정한다
    // ==============================================================
    // 보스와 플레이어 사이 거리를 재서 아래 구간에 떨어뜨리고, 그 구간의 후보 중 하나를 고른다.
    // (PickMove 참조)
    //
    //   dist <= closeRange     근거리 → 휩쓸기 / 찌르기
    //   dist <= midRange       중거리 → 찌르기 / 도약
    //   dist <= engageRange    원거리 → 도약 (여기만 연속 사용 허용)
    //   dist >  engageRange    추격. 단 chaseTimeLimit 을 넘기면 그대로 도약으로 강행한다.
    //
    // 값을 바꿀 땐 사거리(sweepRadius / basicThrustLength)와 같이 봐라 — 구간이 사거리보다
    // 넓으면 그 구간에서 뽑힌 패턴이 닿지 않는 헛방이 된다.
    [Header("★ 거리 구간 (패턴 선택)")]
    [Tooltip("이 거리 안이면 근거리 — 휩쓸기 / 찌르기 중에서 고른다.")]
    public float closeRange = 3.5f;
    [Tooltip("이 거리 안이면 중거리 — 찌르기 / 도약 중에서 고른다.")]
    public float midRange = 6.5f;
    [Tooltip("이 거리 안이면 원거리 — 도약. 이 밖이면 추격한다(chaseTimeLimit 까지).")]
    public float engageRange = 9f;

    [Header("★ 패턴 간격 / 추격")]
    [Tooltip("패턴이 정상적으로 끝난 뒤 다음 패턴까지의 최소 간격(초).\n\n" +
             "★ 이 값이 곧 '패턴 사이에 보스가 쫓아오는 시간'이다. 예전엔 이 값이 아예 없어서 " +
             "공속(1/ATKSPD = 1초)이 그대로 추격 시간이 됐고, 그 1초 동안 보스가 플레이어에게 " +
             "완전히 달라붙어 항상 근거리 판정 -> 휩쓸기만 나왔다.")]
    public float attackGap = 1f;
    [Tooltip("engageRange 밖에서 이 시간(초) 넘게 쫓아다니면 거리와 무관하게 패턴을 강행한다. " +
             "그 거리에선 도약이 뽑히므로, 도망만 다니는 플레이어에게 보스가 뛰어든다.")]
    public float chaseTimeLimit = 2f;

    [Header("패턴 사거리 / 예비동작")]
    [Tooltip("휩쓸기 반원의 반지름(유닛).")]
    public float sweepRadius = 4.16f;
    [Tooltip("휩쓸기 반원의 반각(도). 90 이면 정확히 반원(180도).")]
    public float sweepHalfAngle = 90f;
    [Tooltip("휩쓸기 예고 중 플레이어를 따라 도는 최대 회전 속도(도/초). " +
             "0 이면 무제한(=완전 추적)이라 붙어서 도는 플레이어를 100% 따라가 걸어서는 못 피한다. " +
             "값을 두면 크게 돌아서 빠져나갈 여지가 남는다.")]
    public float sweepTurnSpeed = 0f;
    [Tooltip("찌르기 판정 직사각형의 길이(유닛).")]
    public float basicThrustLength = 6.75f;
    [Tooltip("찌르기 판정 직사각형의 폭(유닛).")]
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

    [Tooltip("휩쓸기를 시전하며 앞으로 미끄러지는 거리(유닛). " +
             "★ 판정 반원도 바닥 부채꼴도 '전진이 끝난 위치'가 중심이다. 즉 0 이 아니면 예고가 " +
             "보스보다 그만큼 앞에 그려진다 — 그림이 틀린 게 아니라 실제로 거기를 때린다. " +
             "부채꼴을 보스 한가운데에 놓으려면 0 으로 둬라(현재값).")]
    public float sweepStepDistance = 0f;
    [Tooltip("위 거리를 미끄러지는 데 걸리는 시간(초). 예고가 끝나는 순간부터 이만큼 나아간 뒤 판정이 난다.")]
    public float sweepStepDuration = 0.12f;

    [Header("찌르기: 미끄러지며 돌진")]
    [Tooltip("조준이 끝난 뒤 창을 내밀며 미끄러져 나아가는 거리(유닛). 항상 이 거리만큼 이동한다 " +
             "— 벽이 가까우면 벽 직전에서 멈춘다(wallCheckRadius 기준).")]
    public float thrustDashDistance = 3f;
    [Tooltip("위 거리를 이동하는 데 걸리는 시간(초). 예고가 끝나는 순간부터 이만큼 미끄러진다.")]
    public float thrustDashDuration = 0.15f;
    [Tooltip("보스 몸 두께. 미끄러지는 중 벽 접촉을 이 반지름으로 검사한다.")]
    public float wallCheckRadius = 0.85f;

    [Header("패턴 간격 (숨 돌릴 틈 / 파훼 보상 딜타임)")]
    [Tooltip("카운터 파훼로 패턴이 취소된 뒤 보스가 다음 행동을 못 하는 시간(초).\n\n" +
             "★ 패턴이 통합된 뒤로 이 잠금은 기본 공격까지 막는다 — 보스의 공격 경로가 이것 하나뿐이다. " +
             "그래서 실제 딜타임 = 그로기(counterSuccessGroggyDuration) + 이 값 이고, 그동안 보스는 추격만 한다. " +
             "늘리면 그만큼 통째로 무행동이니 신중하게 만져라.")]
    public float postPatternRecovery = 1f;
    [Tooltip("[미사용] 특수 패턴과 기본 공격이 분리돼 있던 시절의 추가 딜타임. 통합 후로는 쓰지 않는다.")]
    public float postGroggyRecovery = 1.5f;

    [Header("텔레그래프(피해범위 인디케이터) 색상")]
    public Color telegraphWarnColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    [Tooltip("직선(레인) 전조 프리팹. Assets/Prefabs/Skill Visual Effects/Telegraph Line Hitbox Prefab. " +
             "시각 전용으로만 쓰며 피해는 BossCombat 이 준다(콜라이더가 없는 프리팹).")]
    public BaseHitBox laneTelegraphPrefab;
    [Tooltip("원/타원 전조 프리팹. Assets/Prefabs/Skill Visual Effects/Center Skill Hitbox Circle Prefab.")]
    public BaseHitBox circleTelegraphPrefab;

    [Header("카운터 전조 (모든 패턴 공용)")]
    [Range(0f, 1f)]
    [Tooltip("이번 전조가 '페이크(빨강)'일 확률. 0830 수정안 기준 0.7 = 노랑:빨강 30:70.\n" +
             "노랑이면 예고 중에 때려서 패턴을 취소할 수 있고, 빨강이면 때리는 순간 보스가 즉시 시전한다.\n" +
             "매 전조마다 독립 추첨한다.")]
    public float fakeCounterChance = 0.7f;
    [Tooltip("노랑 창을 파훼하는 데 필요한 총 피해량. 1이면 사실상 아무 공격이나 한 대면 성공한다.")]
    public float counterGaugeAmount = 1f;
    [Tooltip("노랑(진짜 카운터) 전조 색. 인디케이터가 이 색으로 찬다.\n" +
             "몸통 아웃라인이 아니라 인디케이터에만 쓴다 — 아웃라인은 부위 파괴 단계 색(노랑~빨강)과 겹친다.")]
    public Color counterRealColor = new Color(1f, 0.9f, 0.2f);
    [Tooltip("빨강(페이크) 전조 색. 치면 보스가 예고를 건너뛰고 즉시 시전한다.")]
    public Color counterFakeColor = new Color(1f, 0.15f, 0.15f);
    [Tooltip("카운터를 불가능한 패턴(도약 & 내려찍기 등)의 전조 색. 무채색 = '쳐도 소용없다'는 신호.")]
    public Color counterNoneColor = new Color(0.75f, 0.75f, 0.78f);
    [Tooltip("노랑 카운터에 성공했을 때 보스가 먹는 경직 시간(초). 이 동안 패턴이 취소된다.")]
    public float counterSuccessGroggyDuration = 0.5f;


    // ==============================================================
    // 애니메이션 스테이트 이름
    // ==============================================================
    // AnimController_BoneMaster 의 스테이트 이름을 그대로 적는다(= aseprite 태그 이름).
    // 비워두면 공용 "Attack" 으로 폴백하므로, 아트가 빠져도 보스는 그대로 굴러간다.
    [Header("애니메이션 스테이트 이름 (비우면 공용 Attack 으로 폴백)")]
    [Tooltip("기본공격: 창 찌르기.")]
    public string animState_Thrust = "Attack_Prod";
    [Tooltip("기본공격: 창 휩쓸기.")]
    public string animState_Sweep = "Attack_Sweep";
    [Tooltip("기본공격 도약의 준비~체공. 1회 클립이라 마지막 프레임(점프 자세)에서 저절로 홀드된다.")]
    public string animState_Jump = "Attack_Jump";
    [Tooltip("기본공격 도약의 낙하~내려찍기. 2프레임에 타격이 박혀 있어서 착지 순간과 겹치게 늦게 튼다.")]
    public string animState_JumpFall = "Attack_Jump_Fall";
    [Tooltip("클립 길이를 예비동작 시간에 맞춰 Animator.speed 를 자동 조절한다. " +
             "기준점은 클립 끝이 아니라 타격 프레임(OnHitEvent)이라, 때리는 순간과 판정이 겹친다.")]
    public bool matchAnimSpeedToWindup = true;


    /// <summary>보스 패턴 3종. 같은 패턴을 두 번 연속으로 쓰지 않기 위해 직전 것을 기억한다.</summary>
    private enum Move { None, Sweep, Thrust, Leap }
    private Move _lastMove = Move.None;
    private float _chaseStartTime = -100f;

    private BoneMasterController _controller;
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
        _specialLockUntil = -100f;
        _lastMove = Move.None;
        _chaseStartTime = Time.time;

        // 파훼 가능 신호색을 컨트롤러에 알려준다. 컨트롤러가 카운터 게이지 상태에 물려 아웃라인을
        // 켜고 끄므로, 패턴마다 창을 여닫는 12개 지점을 일일이 손대지 않아도 신호가 일관된다.

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
            _chaseStartTime = Time.time;
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

        // 카운터 파훼 뒤의 딜타임 + 패턴 사이 최소 간격(attackGap)을 여기서 지킨다.
        if (Time.time < _specialLockUntil)
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중...");
            // 추격 시간은 '때릴 수 있게 된 순간'부터 센다. 여기서 리셋하지 않으면 딜타임이
            // 그대로 추격 시간으로 계산돼, 잠금이 풀리는 즉시 강제 패턴이 튀어나온다.
            _chaseStartTime = Time.time;
            return;
        }

        // [버그 수정 — 패턴마다 1초씩 추격이 강제로 붙던 문제]
        // 예전엔 여기서 entity.AtkTimer >= AttackInterval 을 봤는데, AtkTimer 는 AIPatternSO.Execute()
        // 안에서만 증가하고 Execute 는 IsAttacking 이면 통째로 안 돈다(BaseEntity.CanExecuteAI).
        // 즉 패턴이 도는 1.4초 동안 타이머가 얼어 있다가 패턴이 끝나서야 0 부터 다시 세기 시작했고,
        // 그 결과 패턴마다 정확히 공속 1회분(1초)의 추격이 강제로 붙었다. 그 1초면 이동속도 5 인
        // 보스가 플레이어에게 완전히 달라붙어서, 다음 판단은 항상 근거리 -> 휩쓸기만 나왔다.
        // 이제 간격은 attackGap 하나로만 저작한다.
        bool inRange = dist <= effectiveEngageRange;
        if (!inRange && Time.time - _chaseStartTime < Mathf.Max(0f, chaseTimeLimit))
        {
            entity.CurrentState = AIState.Follow;
            _controller?.SetStateText("추격 중...");
            return;
        }

        entity.CurrentState = AIState.Attack;
    }

    /// <summary>
    /// 이번에 쓸 패턴. 거리로 후보를 좁히고, 그 안에서 <b>직전과 같은 것은 피한다</b>.
    ///
    /// 원거리는 후보가 도약뿐이라 연속 사용을 허용한다(0830 확정 — 계속 멀리 도망다니는
    /// 플레이어에게 보스가 쓸 수 있는 수단이 그것 하나뿐이라서).
    /// </summary>
    private Move PickMove(float dist, float rangeMul)
    {
        if (dist > midRange * rangeMul) return Move.Leap;

        Move a, b;
        if (dist <= closeRange * rangeMul) { a = Move.Sweep; b = Move.Thrust; }
        else { a = Move.Thrust; b = Move.Leap; }

        // 후보가 둘뿐이라 '직전 것이 아닌 쪽'이 곧 답이다.
        // 직전 것이 이 구간의 후보가 아니면(구간을 갓 넘어왔다) 둘 중 아무거나.
        if (_lastMove == a) return b;
        if (_lastMove == b) return a;
        return Random.value < 0.5f ? a : b;
    }

    /// <summary>
    /// 다음 패턴까지의 간격을 건다. 추격 제한시간도 여기서 같이 리셋한다 —
    /// "패턴이 끝난 시점"이 곧 "추격이 시작된 시점"이다.
    /// </summary>
    private void ArmNextAttack(float extraLock = 0f)
    {
        float until = Time.time + Mathf.Max(0f, attackGap) + Mathf.Max(0f, extraLock);
        // 카운터 파훼 딜타임(EndPattern)이 이미 더 길게 걸려 있으면 그쪽을 존중한다.
        _specialLockUntil = Mathf.Max(_specialLockUntil, until);
        _chaseStartTime = Time.time;
    }

    /// <summary>
    /// 예고 중 조준을 플레이어 쪽으로 한 프레임만큼 돌린다.
    /// turnSpeed(도/초) 상한이 있어서 붙어서 도는 플레이어를 완전히 따라가지는 못한다 —
    /// 크게 돌면 빠져나갈 여지를 남기는 것이 목적이다(0 이면 무제한).
    /// </summary>
    public static Vector2 AimToward(BaseEntity entity, Vector2 dir, Vector2 origin, float turnSpeed)
    {
        if (entity == null || entity.Target == null) return dir;
        Vector2 want = (Vector2)entity.Target.position - origin;
        if (want.sqrMagnitude < 0.0001f) return dir;

        float tgt = Mathf.Atan2(want.y, want.x) * Mathf.Rad2Deg;
        float next = turnSpeed <= 0f
            ? tgt
            : Mathf.MoveTowardsAngle(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, tgt, turnSpeed * Time.deltaTime);

        float r = next * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
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

    /// <summary>
    /// 앞으로 미끄러질 실제 거리. 원하는 거리를 그대로 주되, 벽/장애물이 더 가까우면 그 직전에서 끊는다.
    /// 이 보스는 Warp(순간이동)로 움직여서 콜라이더가 몸을 안 막아주므로 여기서 미리 재야 한다.
    /// </summary>
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

    /// <summary>
    /// 노랑 카운터를 파훼당했다. 패턴을 취소하고 경직 + 딜타임까지 건다.
    /// (0830 수정안: 카운터 보상은 '패턴 취소 + 경직'이고, 경직 길이는 counterSuccessGroggyDuration.)
    /// </summary>
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
        // [버그 수정 — 투구 파괴 효과 미적용] AttackRangeBonus가 교전거리(engageRange)에만 쓰이고
        // 정작 기본 공격 판정 자체(부채꼴/직사각형/타원 크기)에는 전혀 반영되지 않고 있었다.
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
        // 배속은 Animator 전역 상태라 여기서 반드시 1 로 되돌린다 — 안 되돌리면 다음 모션까지 느려진다.
        if (entity != null && entity.Animator != null) entity.Animator.speed = 1f;
        entity.IsAttacking = false;
        entity.ActiveAttackCoroutine = null;
        entity.ResetAnimationState();
        ArmNextAttack();
    }

    /// <summary>
    /// 휩쓸기 — 플레이어 쪽을 조준하고(=전조 · 카운터 타이밍), 살짝 전진하며 반원으로 쓸어버린다.
    ///
    /// 판정 중심은 <b>전진이 끝난 위치</b>다. 그래서 바닥 부채꼴도 처음부터 그 자리에 그린다 —
    /// 제자리에 그렸다가 전진하면 예고가 거짓말이 된다.
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

        var kind = RollTelegraph(counterable: true, out Color col);
        _controller?.SetStateText("휩쓸기", col);

        GameObject telegraph = BoneMasterTelegraphUtil.SpawnCone(entity, stepEnd, dir, radius, sweepHalfAngle, col);
        PlayState(entity, animState_Sweep, windup, matchAnimSpeedToWindup);

        // [0830 수정안] 예고가 차는 동안 플레이어를 계속 조준한다.
        // 예전엔 예고 시작 순간의 방향으로 고정이었고, 판정 중심이 앞으로 전진한 자리(stepEnd)라
        // 보스에게 완전히 붙어 있는 플레이어는 반원의 뒤쪽 사각에 서게 됐다 — 걸어서 돌기만 해도
        // 무한히 빠졌다. 다만 회전에 상한(sweepTurnSpeed)을 둬서 크게 돌면 여전히 빠질 수 있다.
        var tele = new BossCounterTelegraph.Result();
        yield return BossCounterTelegraph.Run(entity, _controller, windup, dir, kind, col,
                                              counterGaugeAmount, tele,
                                              onTick: () =>
                                              {
                                                  Warp(entity, origin);
                                                  dir = AimToward(entity, dir, origin, sweepTurnSpeed);
                                                  if (entity.Target != null) entity.LookAtTarget(entity.Target);
                                                  stepEnd = origin + dir * SlideDistance(origin, dir, sweepStepDistance);
                                                  BoneMasterTelegraphUtil.UpdateCone(
                                                      telegraph, stepEnd, dir,
                                                      windup > 0.0001f ? tele.Elapsed / windup : 1f);
                                                  BossAttackIndicator.Aim(entity, dir);
                                              });
        if (telegraph != null) Object.Destroy(telegraph);

        if (tele.Hijacked) { FinishBasicAttack(entity); yield break; }
        if (tele.Countered) { CancelByCounter(entity); yield break; }

        // 전진 — 빨강을 맞았으면(ForcedEarly) 예고를 건너뛰고 여기부터 바로 시작된다.
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

        var info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, category: DamageCategory.EnemyBoss);
        BossCombat.DealCone(stepEnd, dir, radius, sweepHalfAngle, entity.opponentLayer, info);

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

    /// <summary>
    /// 찌르기 — 플레이어 쪽을 조준하고(=전조 · 카운터 타이밍), 창을 내밀며 미끄러져 돌진한다.
    ///
    /// 돌진 거리는 항상 <see cref="thrustDashDistance"/> 고정이다(벽이 가까우면 그 직전까지).
    /// 판정 레인은 <b>미끄러지기 전 origin</b> 기준 전체 길이 — 바닥 전조가 보여준 범위와 정확히 같다.
    /// </summary>
    private IEnumerator BasicAttack_Thrust(BaseEntity entity)
    {
        StopNavAgent(entity);
        _controller?.HardStopMovement();

        if (entity.Target != null) entity.LookAtTarget(entity.Target);
        Vector2 origin = entity.transform.position;
        Vector2 dir = SafeDirTo(entity, origin, entity.Target);
        float rangeMul = 1f + (_controller != null ? _controller.AttackRangeBonus : 0f);
        float csMul = 1f / (1f + (_controller != null ? _controller.PatternCastSpeedBonus : 0f));
        float length = basicThrustLength * rangeMul;
        float width = basicThrustWidth * rangeMul;
        float windup = basicAttackWindup * csMul;

        var kind = RollTelegraph(counterable: true, out Color col);
        _controller?.SetStateText("찌르기", col);

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

        // 미끄러지며 돌진. 판정은 도착한 뒤 한 번에 나가되 origin 기준이라 지나온 자리도 다 포함된다.
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

        yield return new WaitForSeconds(basicAttackRecovery);
        FinishBasicAttack(entity);
    }

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

    /// <summary>
    /// 특수 패턴 종료. CurrentState 를 풀어 주는 동시에 다음 특수 패턴까지의 최소 간격을 건다.
    /// </summary>
    /// <param name="extraLock">
    /// postPatternRecovery 위에 더 얹을 시간. 파훼(그로기)로 끝난 경우 "그로기 시간 + postGroggyRecovery"를
    /// 넘겨서, 그로기가 풀리자마자 다음 패턴이 시작되지 않고 실제 딜타임이 생기게 한다.
    /// </param>
    /// <summary>패턴이 끝났다. 배속은 Animator 전역 상태라 여기서 반드시 1 로 되돌린다.</summary>
    private void EndPattern(BaseEntity entity, float extraLock = 0f)
    {
        // 배속은 Animator 전역 상태다. 여기서 1 로 안 되돌리면 패턴이 늘려놓은 배속이
        // 다음 추격/기본공격 모션까지 그대로 따라간다.
        if (entity != null && entity.Animator != null) entity.Animator.speed = 1f;
        entity.CurrentState = AIState.Follow;
        _specialLockUntil = Time.time + Mathf.Max(0f, postPatternRecovery) + Mathf.Max(0f, extraLock);
    }

    #region 벽 판정 (슬라이드 돌진 / 도약 착지 공용)
    /// <summary>
    /// 벽/장애물 레이어. 다른 차저(ChargerAIPatternSO / EliteChargerAIPatternSO)와 같은 마스크를 쓴다.
    /// 필드 초기화식으로 쓰면 안 된다 — LayerMask.GetMask 는 ScriptableObject 생성자에서 호출이
    /// 금지돼 있어서 "NameToLayer is not allowed to be called from a ScriptableObject constructor"
    /// 예외가 난다. 처음 쓸 때 한 번만 조회한다.
    /// </summary>
    private static int _wallMask;
    private static int WallMask => _wallMask != 0 ? _wallMask : (_wallMask = LayerMask.GetMask("Wall", "Object"));

    /// <summary>
    /// 돌진을 멈춰야 하는가.
    ///
    /// [버그 수정 — 보스가 벽을 뚫고 계속 달리던 문제] 다른 차저들은 rb.linearVelocity 로 달려서
    /// 콜라이더가 몸을 물리적으로 막아주지만, 이 보스는 Warp(순간이동)로 달리기 때문에 막아주는
    /// 물리가 아예 없다 — 여기서 직접 보지 않으면 방 벽이든 기둥이든 그냥 통과한다.
    /// </summary>
    private bool IsTouchingWall(Vector2 pos)
    {
        return Physics2D.OverlapCircle(pos, wallCheckRadius, WallMask) != null;
    }


    /// <summary>
    /// [버그 수정 4 — 가끔 경직이 안 걸리는 문제] 프레임이 잠깐 튀어(GC, 오브젝트 파괴 등) 한 프레임에
    /// 크게 이동하면 이전 위치와 다음 위치 사이의 얇은 벽을 통째로 건너뛰어 접촉 판정을 한 번도
    /// 못 잡는 경우가 있었다. prev→next 구간을 wallCheckRadius 간격으로 여러 지점 샘플링해서 검사한다.
    /// </summary>
    private bool IsTouchingWallSwept(Vector2 prevPos, Vector2 nextPos)
    {
        float dist = Vector2.Distance(prevPos, nextPos);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(0.05f, wallCheckRadius)));
        // i는 1부터 — i=0은 prevPos(= 이번 프레임 시작 위치)라 "이미 서 있던 자리"를 다시 검사한다.
        // 돌진은 항상 벽 직전에서 멈추므로 다음 돌진의 시작 위치는 벽에서 wallCheckRadius 안쪽인
        // 경우가 많고, 그러면 첫 검사에서 즉시 걸려 한 칸도 못 가고 자해 경직에 빠진다.
        for (int i = 1; i <= steps; i++)
        {
            Vector2 sample = Vector2.Lerp(prevPos, nextPos, steps == 0 ? 0f : (float)i / steps);
            if (IsTouchingWall(sample)) return true;
        }
        return false;
    }
    #endregion
}
