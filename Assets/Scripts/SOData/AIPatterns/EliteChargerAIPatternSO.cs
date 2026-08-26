using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 스테이지 1 엘리트 몬스터(차저) AI 패턴입니다. (기획서 v1.5 기준)
///
/// 전투 흐름:
/// 1) 방 입장
/// 2) 기본 공격 3종(①미니 돌진 찍기 / ②일반 돌진 / ③휩쓸기)을 8초간 반복
/// 3) 8초가 지나면 모든 행동을 강제로 인터럽트하고, 특수 패턴 2종 중 1개를 발동
/// 4) 패턴 종료 후 다시 기본 공격 8초 반복 → 다음 차례엔 "아직 안 쓴 나머지 1개"
/// 5) 둘 다 쓰면 풀을 리필하고 반복
///
/// "차저"라는 정체성은 특수 패턴 하나가 아니라 전투 내내 상시로 체감되어야 하므로,
/// 기본 공격 3종 중 2종이 돌진이고, 사거리 밖 추격 중에도 간헐적으로 짧게 가속합니다(추격 버스트).
///
/// [v1.5] 보스가 아니라 엘리트인 만큼 "짧고 명료한 전투"로 다운스케일했습니다. 삭제된 것들:
///   - 기둥(EliteMonsterPillar) 전체. 그래서 회피 수단은 대쉬와 지형뿐이고,
///     패턴 1의 회피 성공 조건도 "벽 충돌" 하나로 통일됐습니다(보스 기절 1.5초).
///   - 기둥 파편 누적 강화 스택(받는 피해 +5%/스택) — 획득 경로가 기둥뿐이었습니다.
///   - 패턴 2 '중력 도넛 폭발' — 특수 패턴 풀은 [패턴1 돌진 조준, 패턴3 바닥 충격파] 2종.
///   - 2페이즈 전환(하울링 + 상시 속도 증가). 전투 강도는 시작부터 끝까지 일정합니다.
///  ※ EliteMonsterPillar / PillarField / Charger Pillar 프리팹은 지우지 않고 남겨뒀습니다.
///    다음 보스가 기둥을 쓸 수 있어서인데, 이 차저는 더 이상 참조하지 않습니다.
///
/// [중요] Unity 엔진의 BaseEntity.Update()는 IsAttacking == true인 동안(공격 windup~후딜레이)에는
/// CanExecuteAI()가 false를 반환해 브레인의 Execute()를 아예 호출하지 않습니다. 그래서 8초 판정은
/// Time.deltaTime 누적이 아니라, "기본 공격 상태로 돌아온 절대 시각(Time.time)"을 기록해두고 그로부터
/// 8초가 지났는지를 비교하는 방식으로 계산합니다. Execute()가 얼마나 뜸하게 불리든 상관없이 정확합니다.
///
/// - 플레이어의 기본 공격에는 슈퍼아머로 경직/넉백되지 않습니다.
/// - 현재 사용 중인 공격/패턴 이름을 보스 머리 위에 한글로 표시합니다.
/// </summary>
[CreateAssetMenu(fileName = "EliteChargerAIPattern", menuName = "Necromancer/AI/EliteChargerPattern")]
public class EliteChargerAIPatternSO : BossAIPatternSO
{
    // ##############################################################################
    // # [Step 4b 계획 — 아직 실행 안 함] IBossAction 카탈로그 리팩터                  #
    // # 새 보스를 만들 때 이 파일을 참고할 텐데, 그때 "이 정리를 지금 할지" 판단용 메모.     #
    // ##############################################################################
    //
    // ▷ 지금 구조(문제):
    //   이 SO 한 파일(~1600줄)에 6개 공격 '바디'가 전부 private 코루틴으로 살고,
    //   index(매직넘버 0/1/2)로 switch 디스패치한다.
    //     - 기본 3종: BasicAttackRoutine()이 _scheduler.NextBasic() index로 windup/radius/label을
    //       삼항으로 고르고 → MiniChargeStabRoutine / NormalChargeRoutine / (sweep 인라인) 분기.
    //     - 특수 2종: RunSpecialPattern(p)의 switch(p) → Pattern1_AimedCharge / Pattern3_GroundSlam.
    //
    // ▷ 목표 구조(리팩 후):
    //   각 공격을 IBossAction 구현 1개로 분리하고, 브레인은 "고르고 실행"만 한다.
    //     _basics   = { MiniStabAction, NormalChargeAction, SweepAction };
    //     _specials = { AimedChargeAction, GroundSlamAction };
    //     Execute(): scheduler가 고른 _basics[i] / _specials[i] 의 .Run(ctx) 만 호출.
    //   => 공격 추가/삭제/교체 = 클래스 하나 + 리스트 한 줄. index 매직넘버·거대 switch 사라짐.
    //
    // ▷ 이미 만들어 둔 재사용 인프라 (Assets/Scripts/SOData/AI patterns/Boss/):
    //     IBossAction / BossContext (BossAction.cs), BossActionScheduler, BossTelegraph,
    //     BossCombat(TryDamage/DealCircle/ExpandingRing), PillarField.
    //   => 새 보스는 '엘리트를 안 뜯어도' 이 인프라 위에 IBossAction 리스트로 바로 만들 수 있다.
    //      (즉 미래 보스가 '잠겨있는' 게 아니다. 4b는 순수하게 '엘리트 자신을 그 모양에 맞추는' 일.)
    //
    // ▷ 추출 방법(한 번에 X, 액션 하나씩 → 매번 플레이테스트):
    //   1) 각 액션 클래스는 생성자로 이 SO(설정 필드 provider)를 받는다. (config는 전부 public 필드라 접근 가능)
    //   2) 액션이 쓰는 헬퍼를 공용으로 옮기거나 노출:
    //        - GetAimDir      → 이미 BossContext.AimDir() 로 대체 가능
    //        - 텔레그래프 생성   → BossTelegraph (원/사각/링). CreateFallbackRect/Circle/Ring도 여기로 흡수
    //        - 데미지/링 판정    → BossCombat.TryDamage / DealCircle / ExpandingRing (이미 링·데미지는 이걸 씀)
    //        - 기둥 질의/수명    → PillarField (이미 이관됨)
    //        - GetRoomMetrics / GetBoundsExitDistance / ResolveLandingPoint / StopNavAgent /
    //          ShowLabel/ClearLabel → 공용 유틸(예: BossContext 또는 새 BossMotion/BossRoom 유틸)로 이동하거나
    //          SO 메서드를 internal 로 노출해 액션이 호출.
    //   3) BasicAttackRoutine의 '공통 래퍼'(IsAttacking/Animator/postDelay/ClearLabel)는 브레인에 남기고,
    //      액션은 '공격 고유부'만 담당하게 쪼갠다. (특수 3종은 이미 자기완결형이라 래핑이 더 쉬움)
    //
    // ▷ 왜/언제:
    //   기능 변화 0의 순수 정리다. 즉시 이득(#6 기둥누수, 데미지 단일경로, 텔레그래프/링 중복제거, 레이어 정리)은
    //   이미 다 챙겼다. 회귀 리스크는 제일 크고 즉시 이득은 제일 작으므로 급하지 않다.
    //   ⇒ 보스 #2를 만들며 '두 보스 공통 모양'이 실제로 필요해질 때, 그 김에 여기도 함께 정리하는 걸 권장.
    //
    // ▷ 이미 완료된 관련 리팩(참고): SO 브레인 클론 방식 유지(BaseEntity가 인스턴스별 Instantiate),
    //   PillarField로 기둥 수명/정리(#6) 캡슐화, 스케줄러 일원화(_scheduler), 데미지 단일경로(BossCombat),
    //   텔레그래프 스프라이트 BossTelegraph로 통합, 링 2종 BossCombat.ExpandingRing으로 통합.
    // ##############################################################################

    // ==============================================================
    // 기본 공격 4종 (v1.3, 인스펙터에서 알아보기 쉬도록 공격별로 그룹화)
    // ==============================================================
    [Header("공통 설정 (4종 전체 공통 적용)")]
    [Tooltip("모든 기본 공격 후 공통으로 부여되는 후딜레이")]
    public float basicAttackPostDelay = 1.0f;
    [Tooltip("돌진 개시 몇 초 전에 예고 플래시(하데스식 흰색 번쩍)를 터뜨릴지. 전 몬스터 공통 반응 시간 문법. 엘리트는 2펄스")]
    public float telegraphFlashLeadTime = 0.35f;


    [Header("① 미니 돌진 찍기 (약한 돌진)")]
    [Tooltip("비워두면 기본 원형 히트박스로 대체됩니다")]
    public GameObject stabHitboxPrefab;
    [Tooltip("도약해서 착지할 때까지의 체공 시간. 이 시간 내내 착지 예고 원이 차오른다")]
    public float stabWindup = 1.0f;
    [Tooltip("착지 지점에 생기는 원형 판정 크기. 착지 예고 원도 같은 값으로 그려진다")]
    public float stabRadius = 4.4f;
    [Tooltip("도약해서 날아가는 거리. 벽에 막히면 그 앞에 착지한다")]
    public float miniChargeDistance = 3.6f;
    [Tooltip("착지 지점을 정할 때 벽 충돌 검사에 쓰는 반경")]
    public float miniChargeCheckRadius = 0.6f;

    [Header("② 일반 돌진")]
    [Tooltip("일반 차저가 가진 직선형 고속 돌진입니다. 데미지가 낮고 사거리가 짧습니다.")]
    public float normalChargeWindup = 0.8f;
    [Tooltip("돌진 속도 배율 (보스 이동속도 대비). ①번보다는 빠르지만 패턴 1의 강한 돌진보다는 느립니다")]
    public float normalChargeSpeedMultiplier = 7f;
    [Tooltip("이 공격의 '사거리'. 이 시간이 다 되면 스스로 멈춘다(속도 × 이 시간 = 실제 돌진 거리). " +
             "벽이나 플레이어에 먼저 닿으면 그 전에 멈춘다.")]
    public float normalChargeMaxDuration = 1.2f;
    [Tooltip("돌진 중 충돌 검사에 사용할 반경")]
    public float normalChargeHitRadius = 1.0f;
    [Tooltip("플레이어 직격 시 피해량 배율 (ATK 대비, 약하게)")]
    public float normalChargeDamageMultiplier = 0.5f;

    [Header("③ 휩쓸기 공격")]
    [Tooltip("비워두면 기본 원형 히트박스로 대체됩니다")]
    public GameObject sweepHitboxPrefab;
    [Tooltip("시전(웅크림+대시) 시간. 기존 1.5초 -> 1.2초(20% 단축) -> 1.02초(추가 15% 단축)")]
    public float sweepWindup = 1.02f;
    [Tooltip("휩쓸기 반경. 20% 증가 (기존 6.0)")]
    public float sweepRadius = 7.2f;

    [Header("추격 버스트 (사거리 밖에서 추격 중 간헐적 가속, 기본 공격과는 별개)")]
    [Tooltip("버스트 발동 간 최소 대기시간")]
    public float pursuitBurstMinInterval = 2.5f;
    [Tooltip("버스트 발동 간 최대 대기시간")]
    public float pursuitBurstMaxInterval = 4.5f;
    [Tooltip("버스트 중 이동속도 배율")]
    public float pursuitBurstSpeedMultiplier = 2.2f;
    [Tooltip("가속 유지 시간")]
    public float pursuitBurstDuration = 0.35f;
    [Tooltip("가속에서 평상시 속도로 되돌아오는 감속 시간")]
    public float pursuitBurstDecelDuration = 0.25f;

    // ==============================================================
    // 슈퍼아머 / 패턴 이름 표시
    // ==============================================================
    [Header("슈퍼아머")]
    [Tooltip("플레이어의 기본 공격으로는 경직/넉백되지 않도록 부여할 슈퍼아머 게이지 (사실상 무제한)")]
    public float superArmorGauge = 999999f;

    [Header("패턴 이름 표시 (보스 머리 위 한글 라벨)")]
    public bool showPatternLabel = true;
    public TMP_FontAsset patternLabelFont;
    public Vector3 patternLabelOffset = new Vector3(0f, 1.3f, 0f);
    public string label_Stab = "미니 돌진 찍기";
    public string label_NormalCharge = "돌진";
    public string label_Sweep = "휩쓸기";
    public string label_Pattern1Windup = "돌진 조준";
    public string label_Pattern1 = "돌진!";
    public string label_Pattern3Windup = "바닥 충격파 준비";
    public string label_Pattern3 = "바닥 충격파";

    // ==============================================================
    // 애니메이션 스테이트 이름
    // ==============================================================
    // [26/08/16] 해태(Enemy_10_HaeTae) 아트가 들어오면서 공격마다 전용 모션이 생겼다.
    // 예전엔 공격 종류와 무관하게 Animator.Play("Attack") 하나만 불렀는데,
    // 공용 CharacterBase_Animator 의 실사용 슬롯이 Idle/Follow/Attack/Die/Stun 5개뿐이라
    // 공격 5종을 담을 수 없었기 때문이다(AnimatorOverrideController 는 스테이트 추가가 안 된다).
    // 그래서 해태는 전용 AnimController_HaeTae 를 쓰고, 여기서 스테이트를 이름으로 지정한다.
    // 이름을 인스펙터로 뺀 이유는 다음 보스가 자기 태그 이름만 적으면 되게 하기 위해서다.
    // 비워두면 공용 "Attack" 스테이트로 폴백하므로, 아트가 없는 보스도 그대로 굴러간다.
    [Header("애니메이션 스테이트 이름 (비우면 공용 Attack 으로 폴백)")]
    [Tooltip("① 미니 돌진 찍기. 해태 기준 도약 후 착지 내려찍기 모션.")]
    public string animState_Stab = "Jump_Attack";
    [Tooltip("② 일반 돌진 / 패턴1 강한 돌진의 예비동작(웅크려 조준). 1프레임 포즈라 windup 동안 정지 홀드된다.")]
    public string animState_ChargeReady = "Dash_Ready";
    [Tooltip("② 일반 돌진 / 패턴1 강한 돌진의 질주 중. 루프 클립이라 돌진이 길어져도 계속 굴러간다.")]
    public string animState_Charge = "Dash_Attack";
    [Tooltip("③ 휩쓸기. 해태 기준 꼬리 후리기 모션.")]
    public string animState_Sweep = "Slash_Attack";
    [Tooltip("패턴3 바닥 충격파. 해태 기준 뒷발로 일어섰다 내려찍는 모션.")]
    public string animState_Slam = "ShockWave";
    [Tooltip("클립 길이를 예비동작 시간에 맞춰 Animator.speed 를 자동 조절한다. " +
             "예: Jump_Attack(0.775초) 클립을 stabWindup(1.0초)에 맞추면 speed 0.775 로 느리게 늘린다. " +
             "끄면 클립이 원래 속도로 재생되고 남는 시간 동안 마지막 프레임에서 멈춰 있는다.")]
    public bool matchAnimSpeedToWindup = true;

    // ==============================================================
    // 특수 패턴 공통 설정
    // ==============================================================
    [Header("특수 패턴 공통 설정 (기본 공격 8초 -> 패턴 1회 -> 기본 공격 8초 -> ...)")]
    [Tooltip("기본 공격 상태로 돌아온 뒤 이 시간(초)이 지나면, 하던 행동을 강제로 중단하고 패턴을 발동합니다.")]
    public float specialPatternInterval = 8f;

    // --- 패턴 1: 돌진 조준 (v1.5, 구 '기둥과 돌진') ---
    [Header("패턴 1 - 돌진 조준 (3초 조준 후 강한 돌진. 빗나가 벽에 박으면 보스가 기절한다)")]
    public float chargeWindup = 3f;
    [Tooltip("돌진 속도 배율 (보스 이동속도 대비). 값이 클수록 돌진이 훨씬 빨라집니다.")]
    public float chargeSpeedMultiplier = 9f;
    [Tooltip("돌진 판정 반경 (일반 차저보다 3배 이상 넓게)")]
    public float chargeHitRadius = 1.5f;
    [Tooltip("돌진 전조(바닥 경고 직사각형)의 기본 길이(대상이 없을 때). 평소에는 플레이어 발밑까지 이어지도록 자동 계산됩니다.")]
    public float chargeTelegraphLength = 30f;
    [Tooltip("전조 길이를 플레이어 위치보다 얼마나 더 길게 그릴지(발밑을 확실히 덮도록)")]
    public float chargeTelegraphOvershoot = 2.5f;
    public Color chargeTelegraphColor = new Color(1f, 0f, 0f, 0.35f);
    [Tooltip("돌진 예고 레인 안에서 두께 방향으로 차오르는 게이지 색. 배경 레인보다 진하게 두어야 채워지는 게 읽힌다.")]
    public Color chargeTelegraphFillColor = new Color(1f, 0.25f, 0.1f, 0.75f);

    // [v1.5] 회피 성공 조건은 '벽 충돌' 하나로 통일됐다. 기둥이 있던 v1.4 의 5초 기절 보상이
    // 사라졌으므로 그로기도 1.5초로 짧다 — 의도된 다운스케일이다.
    [Tooltip("플레이어를 못 맞히고 벽에 박았을 때 보스가 기절하는 시간")]
    public float wallChargeStunDuration = 1.5f;

    // --- 패턴 3: 바닥 충격파 ---
    [Header("패턴 3 - 바닥 충격파 (파동이 방 끝까지 퍼져나갑니다. v1.5: 회피 수단은 대쉬뿐)")]
    [Tooltip("애니메이션이 없는 것을 보완하기 위한 사전 예비동작 시간. 이 시간 동안 보스 발밑에 경고 원이 서서히 채워지며, 아직 피해는 없습니다.")]
    public float slamPreCastDelay = 0.9f;
    [Tooltip("보스 근처 확정 피해 반경 (꼼수 방지, 대쉬 무적으로만 회피됩니다)")]
    public float slamMeleeRadius = 2f;
    public int slamWaveCount = 2;
    [Tooltip("파동이 중심에서 최대 반경까지 도달하는 데 걸리는 시간 (클수록 파동이 느려져 회피하기 쉬워집니다)")]
    public float slamWaveExpandTime = 1.4f;
    [Tooltip("충격파 회차 사이의 간격")]
    public float slamWaveInterval = 0.8f;
    [Tooltip("방을 찾지 못했을 때(fallback) 사용할 파동 최대 반경")]
    public float slamWaveFallbackMaxRadius = 10f;
    [Tooltip("파동 고리의 실제 피해 판정 두께 (이 두께만큼 스쳐 지나가는 순간에만 피해 판정 - 얇을수록 회피가 쉬워집니다)")]
    public float slamRingThickness = 0.9f;
    public float slamWaveDamage = 18f;

    // ==============================================================
    // 런타임 상태 (ScriptableObject 공유 인스턴스 기준 - 다른 보스 패턴들과 동일한 구조)
    // ==============================================================
    private bool _isBusy = false;
    private bool _superArmorApplied = false;
    private BossActionScheduler _scheduler; // 기본 no-repeat + 특수 무반복풀 + 타이머 선택 로직 일원화
    private EliteBossPatternLabel _label;
    private Coroutine _basicAttackCoroutine;
    private Coroutine _specialPatternCoroutine;

    // v1.2 추격 버스트 런타임 상태
    private Coroutine _pursuitBurstCoroutine;
    private bool _isBursting = false;
    private float _nextBurstTime;

    // 마지막으로 유효했던(0벡터가 아니었던) 조준 방향입니다. 목표와 완전히 겹치는 등 방향 계산이
    // 불가능한 순간에도, 임의의 고정 방향(예: 오른쪽) 대신 이 값을 사용해 "플레이어 반대 방향으로
    // 공격이 나가는" 것처럼 보이는 문제를 방지합니다.
    private Vector2 _lastAimDir = Vector2.down;

    /// <summary>
    /// 돌진이 "실제로 전진하고 있는가"를 본다. 돌진의 진짜 종료 조건은 '벽 레이어에 닿았다'가 아니라
    /// '더 못 나아간다'이기 때문이다. 앞을 내다보는 CircleCast 는 특정 레이어만 보므로,
    /// 그 마스크 밖의 무언가에 막히면 아무것도 못 잡고 돌진이 시간 끝까지 벽에 갈린다.
    ///
    /// 첫 이동이 관측되기 전에는 판정하지 않는다 — rb.linearVelocity 는 Update 에서 넣고 실제 이동은
    /// FixedUpdate 에서 일어나서, 시작 직후 몇 프레임은 정상적으로도 위치가 그대로다.
    /// </summary>
    private struct ChargeStallDetector
    {
        private Vector2 _lastPos;
        private bool _hasMoved;
        private float _stalledTime;

        /// <summary>이만큼 계속 못 나아가면 막힌 것으로 본다. 물리 스텝 몇 번을 버티는 값.</summary>
        private const float StallGrace = 0.06f;
        /// <summary>기대 이동량의 이 비율도 못 가면 '안 움직인 것'. 벽에 비스듬히 스치는 경우까지 잡으려고 넉넉히 둔다.</summary>
        private const float MinAdvanceRatio = 0.25f;

        public ChargeStallDetector(Vector2 startPos)
        {
            _lastPos = startPos;
            _hasMoved = false;
            _stalledTime = 0f;
        }

        public bool IsStalled(Vector2 currentPos, float chargeSpeed, float deltaTime)
        {
            float advanced = Vector2.Distance(currentPos, _lastPos);
            _lastPos = currentPos;

            float expected = chargeSpeed * deltaTime;
            if (advanced >= expected * MinAdvanceRatio)
            {
                _hasMoved = true;
                _stalledTime = 0f;
                return false;
            }

            if (!_hasMoved) return false; // 아직 출발도 안 했다

            _stalledTime += deltaTime;
            return _stalledTime >= StallGrace;
        }
    }

    /// <summary>
    /// [정리] 벽과 플레이어를 모두 CircleCast로 검사해 더 가까운 쪽을 우선한다. 벽부터 무조건
    /// 검사하면 플레이어가 벽에 바짝 붙어 있을 때 항상 벽에 막힌 것으로 처리되는 사각지대가
    /// 생긴다(플레이어가 벽과 같거나 더 가까우면 맞아야 정상). 패턴 1 강한 돌진과 ②일반 돌진이
    /// 완전히 같은 로직을 썼던 걸 여기로 뽑아 중복을 없앴다.
    /// </summary>
    private readonly struct ChargeHitCheck
    {
        public readonly bool WallBlocksFirst;
        public readonly bool PlayerHittable;
        public readonly RaycastHit2D PlayerHit;

        public ChargeHitCheck(bool wallBlocksFirst, bool playerHittable, RaycastHit2D playerHit)
        {
            WallBlocksFirst = wallBlocksFirst;
            PlayerHittable = playerHittable;
            PlayerHit = playerHit;
        }
    }

    private static ChargeHitCheck CheckChargeHit(Vector2 origin, Vector2 dir, float radius, float checkDist, LayerMask wallMask, LayerMask playerMask)
    {
        RaycastHit2D obstacleHit = Physics2D.CircleCast(origin, radius, dir, checkDist, wallMask);
        RaycastHit2D playerHit = Physics2D.CircleCast(origin, radius, dir, checkDist, playerMask);
        // [최적화] LayerMask.NameToLayer는 문자열 조회다. 이미 캐싱된 Layers.PlayerDash(정수 비교)를 쓴다.
        bool playerHittable = playerHit.collider != null && playerHit.collider.gameObject.layer != Layers.PlayerDash;
        bool wallBlocksFirst = obstacleHit.collider != null && (!playerHittable || obstacleHit.distance <= playerHit.distance);
        return new ChargeHitCheck(wallBlocksFirst, playerHittable, playerHit);
    }


    private void OnDestroy()
    {
        ClearChargeTelegraph(); // 엔티티 사망/씬 언로드로 브레인 클론이 파괴될 때
    }

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _isBusy = false;
        _superArmorApplied = false;
        // 특수 패턴은 2종(패턴1 돌진 조준 / 패턴3 바닥 충격파). 무반복 풀이라 둘을 번갈아 쓴다.
        _scheduler = new BossActionScheduler(3, 2);
        _label = null;
        _basicAttackCoroutine = null;
        _specialPatternCoroutine = null;
        _pursuitBurstCoroutine = null;
        _isBursting = false;
        _nextBurstTime = Time.time + Random.Range(pursuitBurstMinInterval, pursuitBurstMaxInterval);
        _lastAimDir = Vector2.down;
        _scheduler.ResetBasicPhase(Time.time);
    }

    public override void Execute(BaseEntity entity)
    {

        if (entity.Target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) entity.Target = player.transform;
            if (entity.Target == null)
            {
                entity.UpdateAnimation(AIState.Idle);
                return;
            }
        }

        // 슈퍼아머: 플레이어의 기본 공격에 경직/넉백되지 않도록 최초 1회 부여합니다.
        if (!_superArmorApplied && entity.Stats != null && entity.Stats.Status != null)
        {
            entity.Stats.Status.ApplySuperArmor(superArmorGauge);
            _superArmorApplied = true;
        }

        if (showPatternLabel && _label == null)
        {
            _label = CreatePatternLabel(entity);
        }

        // 참고: 엔진(BaseEntity.Update -> CanExecuteAI)이 IsAttacking == true인 동안에는 이 함수 자체를
        // 호출하지 않으므로, 아래 인터럽트 분기는 사실상 "공격과 공격 사이의 짧은 순간"에만 유효합니다.
        // 그래도 8초 판정 자체는 Time.time 절대시각 비교라 정확합니다.
        if (!_isBusy)
        {
            entity.LookAtTarget(entity.Target);

            if (_scheduler.ShouldTriggerSpecial(Time.time, specialPatternInterval))
            {
                // 기본 공격 도중이라도(가능한 타이밍이라면) 강제로 중단하고 즉시 패턴을 발동합니다.
                if (entity.IsAttacking)
                {
                    if (_basicAttackCoroutine != null)
                    {
                        entity.StopCoroutine(_basicAttackCoroutine);
                        _basicAttackCoroutine = null;
                    }
                    entity.IsAttacking = false;
                    ClearLabel();
                }
                StopPursuitBurst(entity);

                int pattern = _scheduler.NextSpecial();
                _specialPatternCoroutine = entity.StartCoroutine(RunSpecialPattern(entity, pattern));
                return;
            }

            if (!entity.IsAttacking)
            {
                entity.AtkTimer += Time.deltaTime;

                float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
                var agent = entity.NavAgent;

                if (dist <= entity.Stats.ATKRANGE && entity.AtkTimer >= entity.Stats.AttackInterval)
                {
                    entity.CurrentState = AIState.Attack;
                    StopPursuitBurst(entity);
                    StopNavAgent(entity);
                    entity.AtkTimer = 0f;
                    _basicAttackCoroutine = entity.StartCoroutine(BasicAttackRoutine(entity));
                }
                else
                {
                    entity.CurrentState = AIState.Follow;
                    if (agent != null && agent.isActiveAndEnabled)
                    {
                        agent.isStopped = false;
                        if (!_isBursting) agent.speed = entity.Stats.MOVESPEED;
                        agent.SetDestination(entity.Target.position);
                    }

                    // ② 추격 버스트 (v1.2): 사거리 밖에서 추격하는 동안 간헐적으로 짧게 가속 후 감속합니다.
                    // "8초에 한 번만 돌진하는 보스"가 아니라 "항상 돌진할 수 있는 보스"로 체감시키기 위한 연출입니다.
                    if (!_isBursting && Time.time >= _nextBurstTime)
                    {
                        _pursuitBurstCoroutine = entity.StartCoroutine(PursuitBurstRoutine(entity));
                    }
                }
            }
        }

        entity.UpdateAnimation(entity.CurrentState);
    }

    /// <summary>
    /// 대상 방향을 계산합니다. 방향을 구할 수 없는 경우(대상이 없거나 완전히 겹쳐 0벡터가 되는 경우)
    /// 임의의 고정 방향 대신 마지막으로 유효했던 방향을 반환하여, 공격이 엉뚱한(반대) 방향으로
    /// 나가는 것처럼 보이는 문제를 방지합니다.
    /// </summary>
    private Vector2 GetAimDir(BaseEntity entity)
    {
        if (entity.Target != null)
        {
            Vector2 raw = (Vector2)entity.Target.position - (Vector2)entity.transform.position;
            if (raw.sqrMagnitude > 0.0001f)
            {
                Vector2 dir = raw.normalized;
                _lastAimDir = dir;
                return dir;
            }
        }
        return _lastAimDir;
    }

    // ==============================================================
    // 방 정보 헬퍼
    // ==============================================================
    private struct RoomMetrics
    {
        public bool found;
        public Vector2 center;
        public float halfX;
        public float halfY;
        public Bounds bounds;
    }

    private RoomMetrics GetRoomMetrics(BaseEntity entity)
    {
        RoomInstance room = GetCurrentRoom(entity);
        RoomMetrics m = new RoomMetrics();
        if (room == null)
        {
            m.found = false;
            return m;
        }

        m.found = true;
        m.center = (Vector2)room.transform.position + room.centerOffset;
        m.halfX = room.roomSize.x / 2f;
        m.halfY = room.roomSize.y / 2f;
        m.bounds = new Bounds(m.center, new Vector3(room.roomSize.x - 0.5f, room.roomSize.y - 0.5f, 10f));
        return m;
    }

    /// <summary>
    /// origin이 bounds 내부에 있다고 가정하고, dir 방향으로 bounds를 빠져나가는 지점까지의 거리를 계산합니다.
    /// (UnityEngine.Bounds.IntersectRay는 origin이 내부에 있으면 0을 반환하므로 직접 슬랩(slab) 방식으로 계산합니다.)
    /// </summary>
    private float GetBoundsExitDistance(Bounds bounds, Vector2 origin, Vector2 dir)
    {
        float t = float.MaxValue;

        if (Mathf.Abs(dir.x) > 0.0001f)
        {
            float tx = dir.x > 0f ? (bounds.max.x - origin.x) / dir.x : (bounds.min.x - origin.x) / dir.x;
            if (tx > 0f) t = Mathf.Min(t, tx);
        }
        if (Mathf.Abs(dir.y) > 0.0001f)
        {
            float ty = dir.y > 0f ? (bounds.max.y - origin.y) / dir.y : (bounds.min.y - origin.y) / dir.y;
            if (ty > 0f) t = Mathf.Min(t, ty);
        }

        return t == float.MaxValue ? 0f : t;
    }

    // ==============================================================
    // 패턴 이름 라벨
    // ==============================================================
    private EliteBossPatternLabel CreatePatternLabel(BaseEntity entity)
    {
        GameObject labelObj = new GameObject("PatternLabel");
        labelObj.transform.SetParent(entity.transform, false);
        labelObj.transform.localPosition = patternLabelOffset;

        Vector3 lossy = entity.transform.lossyScale;
        float invX = lossy.x != 0f ? 1f / lossy.x : 1f;
        float invY = lossy.y != 0f ? 1f / lossy.y : 1f;
        labelObj.transform.localScale = new Vector3(invX, invY, 1f);

        EliteBossPatternLabel label = labelObj.AddComponent<EliteBossPatternLabel>();
        label.SetFont(patternLabelFont);
        return label;
    }

    private void ShowLabel(string text)
    {
        if (showPatternLabel && _label != null) _label.SetText(text);
    }

    private void ClearLabel()
    {
        if (_label != null) _label.Clear();
    }

    // ==============================================================
    // 추격 버스트 (v1.2, 3단 돌진 체계 ②)
    // ==============================================================
    private IEnumerator PursuitBurstRoutine(BaseEntity entity)
    {
        _isBursting = true;
        var agent = entity.NavAgent;

        if (agent != null && agent.isActiveAndEnabled && entity.Stats != null)
        {
            float baseSpeed = entity.Stats.MOVESPEED;
            float burstSpeed = baseSpeed * pursuitBurstSpeedMultiplier;

            agent.speed = burstSpeed;
            float t = 0f;
            while (t < pursuitBurstDuration)
            {
                t += Time.deltaTime;
                if (entity.Target != null) agent.SetDestination(entity.Target.position);
                yield return null;
            }

            float dt = 0f;
            while (dt < pursuitBurstDecelDuration)
            {
                dt += Time.deltaTime;
                float f = Mathf.Clamp01(dt / pursuitBurstDecelDuration);
                agent.speed = Mathf.Lerp(burstSpeed, baseSpeed, f);
                yield return null;
            }
            agent.speed = baseSpeed;
        }

        _isBursting = false;
        _pursuitBurstCoroutine = null;
        _nextBurstTime = Time.time + Random.Range(pursuitBurstMinInterval, pursuitBurstMaxInterval);
    }

    /// <summary>
    /// 진행 중인 추격 버스트가 있다면 즉시 중단하고 이동속도를 원래대로 되돌립니다.
    /// (기본 공격 시작, 특수 패턴 강제 발동 등 다른 행동으로 전환될 때 호출합니다.)
    /// </summary>
    private void StopPursuitBurst(BaseEntity entity)
    {
        if (_pursuitBurstCoroutine != null)
        {
            entity.StopCoroutine(_pursuitBurstCoroutine);
            _pursuitBurstCoroutine = null;
        }
        if (_isBursting)
        {
            var agent = entity.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isActiveAndEnabled && entity.Stats != null) agent.speed = entity.Stats.MOVESPEED;
            _isBursting = false;
        }
    }

    // ==============================================================
    // 기본 공격 3종
    // ==============================================================
    // 기본/특수 선택 로직은 BossActionScheduler(_scheduler)로 이관됨 (기존 PickBasicAttack/DrawFromSpecialPool 대체)

    private IEnumerator BasicAttackRoutine(BaseEntity entity)
    {
        entity.IsAttacking = true;
        entity.HasFiredHitEvent = false;
        entity.HasFiredAttackEndEvent = false;

        // [26/08/16] 예전엔 여기서 곧바로 Play("Attack") 을 불렀다. 이제 공격마다 전용 모션이 있으므로
        // '어떤 공격인지 정해진 뒤'에 재생해야 한다 — 그래서 스테이트 재생을 아래 분기로 내렸다.
        int atkIndex = _scheduler.NextBasic(); // 0: 미니 돌진 찍기, 1: 일반 돌진, 2: 휩쓸기

        float windup = atkIndex == 0 ? stabWindup : atkIndex == 1 ? normalChargeWindup : sweepWindup;
        float radius = atkIndex == 0 ? stabRadius : atkIndex == 1 ? normalChargeHitRadius : sweepRadius;
        string labelText = atkIndex == 0 ? label_Stab : atkIndex == 1 ? label_NormalCharge : label_Sweep;

        ShowLabel(labelText);

        Vector2 dir = GetAimDir(entity);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (atkIndex == 0)
        {
            // ① 약한 돌진 (v1.2): 제자리 판정 대신 실제로 짧은 거리를 전진하며 찍습니다.
            // 도약(웅크림) → 착지 찍기가 windup 안에 다 들어가도록 클립을 windup 길이에 맞춰 늘립니다.
            PlayState(entity, animState_Stab, windup, matchAnimSpeedToWindup);
            yield return MiniChargeStabRoutine(entity, dir, radius, windup);
        }
        else if (atkIndex == 1)
        {
            // ② 일반 돌진: 일반 차저의 직선형 고속 돌진. 데미지가 낮고, 벽/플레이어에 닿거나
            // 사거리(normalChargeMaxDuration)가 끝나면 멈춥니다.
            yield return NormalChargeRoutine(entity, dir, windup);
        }
        else
        {
            // ③ 휩쓸기: 보스 자신을 중심으로 원형 범위
            PlayState(entity, animState_Sweep, windup, matchAnimSpeedToWindup);

            Vector2 spawnPos = entity.transform.position;

            GameObject hitboxObj = sweepHitboxPrefab != null
                ? GameObject.Instantiate(sweepHitboxPrefab, spawnPos, Quaternion.Euler(0, 0, angle))
                : CreateFallbackCircle(spawnPos, 0.5f, new Color(1f, 0f, 0f, 0.35f));

            hitboxObj.transform.localScale = Vector3.one * radius;

            BaseHitBox hb = hitboxObj.GetComponent<BaseHitBox>();
            if (hb == null) hb = hitboxObj.AddComponent<BaseHitBox>();

            DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, 1f);
            hb.Init(info, entity.opponentLayer, 0.25f, windup, entity.team == Team.Ally);

            float t = 0f;
            while (t < windup)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        entity.HasFiredHitEvent = true;

        // [수정 26/08/01] 연출 정리를 후딜레이 '앞'으로 옮겼다.
        // 예전엔 후딜레이(1초)가 끝난 뒤에야 애니메이션과 패턴 라벨을 껐다. 그래서 돌진이 벽이나
        // 플레이어에 막혀 일찍 끝나면, 실제로는 멈춰 있는데도 그 1초 동안 공격 애니메이션이 계속
        // 재생되고 머리 위 라벨도 "돌진"인 채로 남아서 "벽에 머리 박고도 계속 밀고 나간다"로 보였다.
        // 특수 패턴(RunSpecialPattern)은 후딜레이가 없어 곧바로 정리되니 멀쩡해 보였던 것뿐이다.
        // 후딜레이 자체는 '연속 즉시 시전 방지'용이므로 IsAttacking 만 붙잡고 있으면 된다.
        if (entity.Animator != null) entity.Animator.speed = 1f;
        entity.ResetAnimationState();

        // [26/08/16] ResetAnimationState 는 _lastState 만 비운다 — 실제 Play 는 UpdateAnimation 이 하는데
        // 그건 IsAttacking 동안 early-return 이고, 애초에 CanExecuteAI 가 막아서 호출조차 안 된다.
        // 즉 아래 후딜레이(1초) 내내 마지막 공격 클립이 그대로 남는다. 돌진 클립(Dash_Attack)은 '루프'라
        // 그동안 멈춰 선 채로 제자리 질주하는 그림이 된다. 그래서 여기서 직접 Idle 로 되돌린다.
        //
        // [26/08/18] 단, 루프 클립일 때만 그렇게 한다. 배속 기준이 타격 프레임으로 바뀌면서 단발
        // 클립은 판정 순간에 아직 뒷부분(마무리 동작)이 남아 있는데, 여기서 Idle 을 강제하면 때린
        // 즉시 자세가 툭 끊긴다. 그냥 두면 후딜레이 동안 마무리가 재생되고, 후딜레이가 끝나면
        // UpdateAnimation 이 (ResetAnimationState 덕에) 알아서 Idle/Follow 로 되돌린다.
        if (entity.Animator != null && entity.Animator.GetCurrentAnimatorStateInfo(0).loop)
            PlayState(entity, "Idle");

        ClearLabel();

        // 모든 기본 공격 후 후딜레이 (연속 즉시 시전 방지)
        yield return new WaitForSeconds(basicAttackPostDelay);

        entity.IsAttacking = false;
        _basicAttackCoroutine = null;
    }

    // ==============================================================
    // ① 도약 찍기 (v2.0)
    // [26/08/18] "제자리에서 웅크렸다가 짧게 돌진" 에서 "즉시 도약해서 오래 떠 있다가 착지" 로 바꿨다.
    // 예비동작 시간(stabWindup)이 통째로 체공 시간이 되고, 회피 정보는 착지 지점에 그리는 원이 준다.
    // ==============================================================
    /// <summary>
    /// 즉시 도약해서 <paramref name="airTime"/> 동안 날아간 뒤 착지하며 찍는다.
    ///
    /// 착지 예고는 두 겹이다 — <b>테두리 링</b>이 '어디에 (얼마만큼)', 그 안에서 <b>차오르는 원</b>이
    /// '언제' 를 알려준다. 둘 다 실제 히트박스와 <b>같은 스케일 값</b>을 쓴다(둘 다 지름 기준 유닛 스프라이트라,
    /// 히트박스 프리팹을 바꿔도 예고와 판정이 같이 움직인다).
    ///
    /// 몸이 쪼그라드는 연출(옛 ScaleCoroutine)은 뺐다. 해태 아트의 Jump_Attack 클립에 도약 웅크림이
    /// 이미 들어 있어 이중으로 적용됐고, 루트 스케일을 만지는 방식이라 콜라이더·그림자·HP바·인디케이터까지
    /// 같이 줄었다. 게다가 추적되지 않는 entity.StartCoroutine 이라 8초 특수 패턴 인터럽트가 끊지 못해
    /// 쪼그라든 채로 남는 경로가 있었다.
    /// </summary>
    private IEnumerator MiniChargeStabRoutine(BaseEntity entity, Vector2 dir, float radius, float airTime)
    {
        Vector2 start = entity.transform.position;
        Vector2 land = ResolveLandingPoint(entity, start, dir, miniChargeDistance);

        GameObject ringMark = CreateFallbackRing(new Color(1f, 0.55f, 0f, 0.6f));
        ringMark.transform.position = land;
        ringMark.transform.localScale = Vector3.one * radius;

        GameObject fillMark = CreateFallbackCircle(land, 0f, new Color(1f, 0.35f, 0f, 0.25f));

        // 잔상은 체공 내내. (창은 자동 만료 — 슈퍼아머라 넉백 오발동 없음)
        entity.GetComponent<DashAfterimage>()?.BeginDash(airTime + 0.3f);

        try
        {
            float t = 0f;
            while (t < airTime)
            {
                t += Time.deltaTime;
                float f = Mathf.Clamp01(t / airTime);
                entity.transform.position = Vector2.Lerp(start, land, f);
                if (fillMark != null) fillMark.transform.localScale = Vector3.one * (radius * f);
                yield return null;
            }
            entity.transform.position = land;
        }
        finally
        {
            // 8초 특수 패턴 인터럽트가 _basicAttackCoroutine 을 StopCoroutine 으로 잘라가면 아래 줄들이
            // 실행되지 않아 착지 예고 원이 바닥에 영구히 남는다. finally 는 StopCoroutine 에서도 돈다.
            if (ringMark != null) GameObject.Destroy(ringMark);
            if (fillMark != null) GameObject.Destroy(fillMark);
        }

        // 착지 타격은 예고한 그 자리 그대로다. 예전엔 진행 방향으로 0.6 밀어서 스폰했는데,
        // 그러면 "착지 지점에 원을 그린다" 는 약속이 깨진다.
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        GameObject hitboxObj = stabHitboxPrefab != null
            ? GameObject.Instantiate(stabHitboxPrefab, land, Quaternion.Euler(0f, 0f, angle))
            : CreateFallbackCircle(land, 0.5f, new Color(1f, 0f, 0f, 0.35f));
        hitboxObj.transform.localScale = Vector3.one * radius;

        BaseHitBox hb = hitboxObj.GetComponent<BaseHitBox>();
        if (hb == null) hb = hitboxObj.AddComponent<BaseHitBox>();

        DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, 1f);
        hb.Init(info, entity.opponentLayer, 0.25f, 0.05f, entity.team == Team.Ally);

        yield return new WaitForSeconds(0.05f);
    }

    /// <summary>
    /// 도약이 실제로 내려앉을 지점. 벽에 막히면 그 앞, 방 밖으로 나가면 경계 안으로 당긴다.
    /// <b>이동 전에</b> 확정해야 한다 — 착지 예고 원을 거기 그려야 하기 때문이다.
    /// </summary>
    private Vector2 ResolveLandingPoint(BaseEntity entity, Vector2 start, Vector2 dir, float distance)
    {
        LayerMask wallMask = LayerMask.GetMask("Wall", "Object");

        RaycastHit2D obstacleHit = Physics2D.CircleCast(start, miniChargeCheckRadius, dir, distance, wallMask);
        if (obstacleHit.collider != null) distance = Mathf.Max(0.1f, obstacleHit.distance - 0.1f);

        Vector2 end = start + dir * distance;

        // 방 경계 안전장치 (벽 콜라이더를 놓치고 통과해버리는 경우 대비)
        RoomMetrics room = GetRoomMetrics(entity);
        if (room.found)
        {
            end = new Vector2(
                Mathf.Clamp(end.x, room.bounds.min.x, room.bounds.max.x),
                Mathf.Clamp(end.y, room.bounds.min.y, room.bounds.max.y));
        }

        return end;
    }

    /// <summary>
    /// ② 일반 돌진 (v1.35 신규): 일반 차저가 가진 직선형 고속 돌진입니다. 미니 돌진 찍기보다
    /// 빠르고 멀리 가지만, 패턴 1의 강한 돌진보다는 약하고 데미지도 낮습니다. 기둥에 닿으면
    /// (강한 돌진처럼 완전히 무너뜨리는 게 아니라) 기둥의 내구도만 1 깎고 그 자리에서 멈춥니다.
    /// </summary>
    private IEnumerator NormalChargeRoutine(BaseEntity entity, Vector2 dir, float windup)
    {
        // 다른 기본 공격들과 통일된 예비동작: 짧은 직선 전조를 표시합니다.
        ClearChargeTelegraph(); // 이전 시도가 남긴 게 있으면 먼저 정리
        _chargeTelegraph = CreateFallbackRect(new Color(1f, 0.55f, 0f, 0.3f));

        // 예비동작 = 웅크려 조준하는 1프레임 포즈. 정지 화면이라 배속은 건드리지 않는다
        // (예비동작 정보는 바닥 전조 게이지와 방향 인디케이터가 준다).
        PlayState(entity, animState_ChargeReady);

        // [26/08/18] 웅크렸다 튀어나가는 스케일 연출(ScaleCoroutine)은 제거했다. 해태 아트에 예비동작이
        // 들어오면서 이중 적용이 됐고, 루트 스케일을 만지는 방식이라 콜라이더·그림자·HP바까지 같이 줄었다.
        float estimatedLength = entity.Stats.MOVESPEED * normalChargeSpeedMultiplier * normalChargeMaxDuration;
        var ncVfb = entity.GetComponentInChildren<CharacterVisualFeedback>();
        bool ncFlashFired = false;


        var dirIndicator = entity.GetComponentInChildren<EntityDirectionIndicator>();
        float wt = 0f;
        while (wt < windup)
        {
            if (entity == null || (entity.Stats != null && entity.Stats.Health != null && entity.Stats.Health.IsDead))
            {
                ClearChargeTelegraph();
                yield break;
            }

            wt += Time.deltaTime;
            if (entity.Target != null) dir = GetAimDir(entity);
            dirIndicator?.SetAimOverride(dir); // 충전 중 실시간 재조준을 인디케이터에도 반영 (돌진 개시 후 자동 만료 → 이동 방향 복귀)
            // 레인은 플레이어를 통과시키되, 벽/장애물에 막히는 지점까지만 그린다.
            // (아래 돌진 루프와 동일한 마스크·반지름이라 예고와 실제 정지 지점이 일치)
            float ncLength = estimatedLength;
            RaycastHit2D ncBlock = Physics2D.CircleCast(
                entity.transform.position,
                normalChargeHitRadius,
                dir,
                ncLength,
                LayerMask.GetMask("Wall", "Object"));
            if (ncBlock.collider != null)
                ncLength = ncBlock.distance;

            UpdateChargeTelegraph(_chargeTelegraph, entity.transform.position, dir, ncLength, normalChargeHitRadius * 2f, windup > 0f ? wt / windup : 1f);
            // [예고 플래시] 돌진 개시 직전 하데스식 번쩍 (엘리트 = 2펄스)
            if (!ncFlashFired && windup - wt <= telegraphFlashLeadTime)
            {
                ncFlashFired = true;
                ncVfb?.PlayTelegraphFlash(2);
            }

            yield return null;
        }
        ClearChargeTelegraph();

        // [사망 체크] 조준 중 사망 시 질주 진입 차단
        if (entity == null || (entity.Stats != null && entity.Stats.Health != null && entity.Stats.Health.IsDead))
        {
            yield break;
        }

        // 질주 개시. 루프 클립이라 돌진이 얼마나 길어지든 계속 굴러간다.
        PlayState(entity, animState_Charge);

        var agent = entity.GetComponent<NavMeshAgent>();
        bool wasAgentEnabled = agent != null && agent.enabled;
        if (wasAgentEnabled)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;
        }

        var rb = entity.GetComponent<Rigidbody2D>();
        float chargeSpeed = entity.Stats.MOVESPEED * normalChargeSpeedMultiplier;

        // [잔상] 일반 돌진 동안만 잔상 방출 윈도우 오픈
        entity.GetComponent<DashAfterimage>()?.BeginDash(normalChargeMaxDuration + 0.5f);

        float elapsed = 0f;

        LayerMask playerMask = LayerMask.GetMask("Player", "Player_Dash");
        LayerMask wallMask = LayerMask.GetMask("Wall", "Object");

        // 방 경계 안전장치 (벽 충돌 감지를 놓치고 통과해버리는 경우 대비)
        RoomMetrics room = GetRoomMetrics(entity);
        Bounds? roomBounds = room.found ? (Bounds?)room.bounds : null;

        var stall = new ChargeStallDetector(entity.transform.position);

        while (elapsed < normalChargeMaxDuration)
        {
            if (entity == null || (entity.Stats != null && entity.Stats.Health != null && entity.Stats.Health.IsDead))
            {
                if (rb != null) rb.linearVelocity = Vector2.zero;
                yield break;
            }

            elapsed += Time.deltaTime;

            if (roomBounds.HasValue && !roomBounds.Value.Contains(entity.transform.position))
            {
                if (rb != null) rb.linearVelocity = Vector2.zero;
                entity.transform.position = (Vector2)entity.transform.position - dir * 0.3f;
                break;
            }

            // [정지 감지] 아래 CircleCast 는 wallMask 에 잡히는 것만 본다. 그 밖의 무언가(구덩이 타일,
            // 다른 레이어의 장애물 등)에 물리적으로 막히면 캐스트는 아무것도 못 찾고, 돌진은 시간이
            // 다 될 때까지 벽에 갈린다. 그래서 '실제로 전진했는가'를 직접 본다 — 이게 진짜 정지 조건이다.
            if (stall.IsStalled(entity.transform.position, chargeSpeed, Time.deltaTime))
            {
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
            }

            if (rb != null) rb.linearVelocity = dir * chargeSpeed;

            float checkDist = chargeSpeed * Time.deltaTime + 0.15f;

            // [정리] 벽/플레이어 우선순위 판정은 CheckChargeHit로 공통화했다 (패턴 1과 동일 로직).
            var hitCheck = CheckChargeHit(entity.transform.position, dir, normalChargeHitRadius, checkDist, wallMask, playerMask);

            if (hitCheck.WallBlocksFirst)
            {
                // 벽에 닿으면 그냥 멈춘다. 패턴 1과 달리 기절은 없다(기본 공격이라 리스크가 없어야 함).
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
            }

            // 대쉬(Player_Dash) 중인 플레이어는 관통(phase-through): 멈추지도/데미지도 없이 그대로 돌진 지속.
            // 그 외 플레이어만 정지 + 데미지. (여전히 레이어 기반 CircleCast — 감지는 하되 대쉬는 통과시킴)
            if (hitCheck.PlayerHittable)
            {
                BossCombat.TryDamage(hitCheck.PlayerHit.collider, new DamageInfo(entity.Stats.ATK * normalChargeDamageMultiplier, DamageType.Physical, entity.gameObject));
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
            }

            yield return null;
        }

        if (rb != null) rb.linearVelocity = Vector2.zero;

        RestoreAgentAfterDash(entity, agent, wasAgentEnabled);
    }

    /// <summary>
    /// 돌진이 끝난 뒤 NavMeshAgent 를 되돌린다.
    ///
    /// [버그 수정 26/08/01] 예전엔 여기서 agent.isStopped = false 로 이동을 곧바로 재개했다.
    /// 그런데 돌진 코루틴이 끝나도 BasicAttackRoutine 은 후딜레이(basicAttackPostDelay, 1초) 동안
    /// IsAttacking = true 를 유지하고, 그동안 엔진이 브레인의 Execute() 를 아예 안 부른다.
    /// 그래서 그 1초 동안 '돌진 전에 잡아둔 낡은 목적지'로 에이전트가 계속 걸어갔고,
    /// 벽에 박고도 1초쯤 더 밀고 나가는 것처럼 보였다.
    ///
    /// 패턴 1(돌진 조준)이 멀쩡해 보였던 건 직후에 기절 1.5초가 붙어 어차피 못 움직였기 때문이다 —
    /// 같은 버그를 안고 있었을 뿐 증상이 가려져 있었다.
    ///
    /// 이동 재개는 다음 Execute() 의 Follow 분기가 알아서 한다(그때 목적지도 새로 잡는다).
    /// </summary>
    private void RestoreAgentAfterDash(BaseEntity entity, NavMeshAgent agent, bool wasAgentEnabled)
    {
        if (!wasAgentEnabled || agent == null) return;

        if (NavMesh.SamplePosition(entity.transform.position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
        {
            entity.transform.position = navHit.position;
        }

        agent.enabled = true;
        if (!agent.isOnNavMesh) return; // NavMesh 밖으로 밀려났으면 경로 조작이 예외를 던진다

        agent.ResetPath();          // 낡은 목적지를 버린다
        agent.velocity = Vector3.zero;
        agent.isStopped = true;     // 브레인이 다시 굴러갈 때까지 정지
    }

    /// <summary>
    /// 애니메이션 부재를 보완하기 위한 스쿼시(웅크림) -> 스트레치(튀어나감) 스케일 연출입니다.
    /// entity.transform.localScale을 기준 스케일의 배율로 조정하므로, 좌우 반전을 위해
    /// 음수 X 스케일을 쓰는 경우에도 부호가 그대로 유지됩니다.
    /// </summary>

    // ==============================================================
    // 특수 패턴 풀: 3개(0,1,2)를 전부 한 번씩 쓸 때까지 같은 패턴이 다시 나오지 않습니다.
    // 풀이 비면(3개 다 사용) 다시 3개로 리필합니다.
    // ==============================================================
    // (특수 패턴 무반복풀은 BossActionScheduler.NextSpecial() 로 이관)

    private IEnumerator RunSpecialPattern(BaseEntity entity, int pattern)
    {
        _isBusy = true;
        entity.IsAttacking = true;
        entity.CurrentState = AIState.Attack;

        try
        {
            switch (pattern)
            {
                case 0:
                    yield return Pattern1_AimedCharge(entity);
                    break;
                default:
                    yield return Pattern3_GroundSlam(entity);
                    break;
            }
        }
        finally
        {
            ClearLabel();
            ClearChargeTelegraph();
            // 배속은 전역 상태다. 특수 패턴에서 늘려둔 채로 나가면 이후 Idle/Move 까지 느려진다.
            if (entity != null && entity.Animator != null) entity.Animator.speed = 1f;
            if (entity != null)
            {
                entity.IsAttacking = false;
                entity.ResetAnimationState();
            }
            _specialPatternCoroutine = null;
            _isBusy = false;

            // 이제부터 다시 기본 공격 페이즈이므로, 8초 카운트를 여기서부터 새로 시작합니다.
            _scheduler.ResetBasicPhase(Time.time);
        }
    }

    // --- 패턴 1: 돌진 조준 (3초 조준 → 강한 돌진. 빗나가면 보스 기절) ---
    private IEnumerator Pattern1_AimedCharge(BaseEntity entity)
    {
        ShowLabel(label_Pattern1Windup);

        // 3초 조준 내내 웅크린 포즈로 홀드한다. 1프레임이라 정지 화면이지만,
        // 방향 인디케이터 + 바닥 전조 게이지가 "언제/어디로"를 알려주므로 정보는 충분하다.
        PlayState(entity, animState_ChargeReady);

        RoomMetrics room = GetRoomMetrics(entity);

        float t = 0f;
        Vector2 chargeDir = GetAimDir(entity);
        // 스턴 등으로 코루틴이 끊겨도 OnAttackCancelled 에서 치울 수 있도록 인스턴스 필드에 보관.
        ClearChargeTelegraph(); // 이전 시도가 남긴 게 있으면 먼저 정리
        float scaledChargeWindup = chargeWindup;
        var p1Vfb = entity.GetComponentInChildren<CharacterVisualFeedback>();
        bool p1FlashFired = false;

        var dirIndicator = entity.GetComponentInChildren<EntityDirectionIndicator>();

        // 3초 조준: 플레이어 방향을 실시간으로 주시하며, 바닥에 돌진 경로를 빨간 직사각형으로 표시합니다.
        // 전조는 항상 "플레이어 발밑"까지 확실히 이어지도록 대상과의 거리 기준으로 길이를 계산합니다.
        while (t < scaledChargeWindup)
        {
            if (entity == null || (entity.Stats != null && entity.Stats.Health != null && entity.Stats.Health.IsDead))
            {
                ClearChargeTelegraph();
                yield break;
            }

            t += Time.deltaTime;
            if (entity.Target != null)
            {
                chargeDir = GetAimDir(entity);
                entity.LookAtTarget(entity.Target);
            }

            // [수정] 레인은 플레이어에서 끊지 않고 통과시킨다. 어디까지 위험한지 다 보여야
            // 어느 쪽으로 피할지 판단이 된다. 대신 돌진 루프와 동일한 마스크·반지름으로
            // CircleCast 해서 실제로 막히는 지점까지만 그린다.
            float length = chargeTelegraphLength;
            if (entity.Target != null)
            {
                length = chargeTelegraphLength;
            }
            if (room.found)
            {
                float exitDist = GetBoundsExitDistance(room.bounds, entity.transform.position, chargeDir);
                if (exitDist > 0f) length = Mathf.Min(length, exitDist);
            }
            length = Mathf.Min(length, chargeTelegraphLength);

            RaycastHit2D laneBlock = Physics2D.CircleCast(
                entity.transform.position,
                chargeHitRadius,
                chargeDir,
                length,
                LayerMask.GetMask("Wall", "Object"));
            if (laneBlock.collider != null)
                length = laneBlock.distance;

            if (_chargeTelegraph == null)
            {
                _chargeTelegraph = CreateFallbackRect(chargeTelegraphColor);
            }
            UpdateChargeTelegraph(_chargeTelegraph, entity.transform.position, chargeDir, length, chargeHitRadius * 2f, scaledChargeWindup > 0f ? t / scaledChargeWindup : 1f);
            dirIndicator?.SetAimOverride(chargeDir); // 충전 중 실시간 재조준을 인디케이터에도 반영 (돌진 개시 후 자동 만료 → 이동 방향 복귀)

            // [예고 플래시] 강한 돌진 개시 직전 하데스식 번쩍 (엘리트 = 2펄스)
            if (!p1FlashFired && scaledChargeWindup - t <= telegraphFlashLeadTime)
            {
                p1FlashFired = true;
                p1Vfb?.PlayTelegraphFlash(2);
            }


            yield return null;
        }

        ClearChargeTelegraph();

        // [사망 체크] 조준 중 사망 시 강한 돌진 진입 차단
        if (entity == null || (entity.Stats != null && entity.Stats.Health != null && entity.Stats.Health.IsDead))
        {
            yield break;
        }

        ShowLabel(label_Pattern1);

        // 강한 돌진 개시. ②일반 돌진과 같은 루프 클립을 공유한다(둘 다 '질주 중'이라 그림이 같다).
        PlayState(entity, animState_Charge);

        var agent = entity.GetComponent<NavMeshAgent>();
        bool wasAgentEnabled = agent != null && agent.enabled;
        if (wasAgentEnabled)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;
        }

        // 방 경계를 벗어나 맵 밖으로 튀어나가는 것을 막기 위한 안전장치 (벽 콜라이더가 없거나
        // 누락된 경우에도 방 범위 안에서 강제로 돌진을 멈춥니다).
        Bounds? roomBounds = room.found ? (Bounds?)room.bounds : null;

        var rb = entity.GetComponent<Rigidbody2D>();
        float chargeSpeed = entity.Stats.MOVESPEED * chargeSpeedMultiplier;
        // 이 패턴엔 '사거리'가 따로 없다. 방이 벽으로 둘러싸여 있으니 플레이어를 못 맞히면 결국 벽에 박는다.
        // maxDuration 은 그게 어떤 이유로든(벽 콜라이더 누락 등) 안 일어났을 때 무한 돌진을 막는 안전장치일 뿐.
        float maxDuration = 3f;

        // [잔상] 패턴1 강한 돌진 동안만 잔상 방출 윈도우 오픈
        entity.GetComponent<DashAfterimage>()?.BeginDash(maxDuration + 0.5f);

        float elapsed = 0f;

        LayerMask playerMask = LayerMask.GetMask("Player", "Player_Dash");
        LayerMask wallMask = LayerMask.GetMask("Wall", "Object");

        bool hitPlayer = false;
        var stall = new ChargeStallDetector(entity.transform.position);

        while (elapsed < maxDuration)
        {
            if (entity == null || (entity.Stats != null && entity.Stats.Health != null && entity.Stats.Health.IsDead))
            {
                if (rb != null) rb.linearVelocity = Vector2.zero;
                yield break;
            }

            elapsed += Time.deltaTime;

            // [정지 감지] 앞의 CircleCast 가 못 잡는 것에 막혔을 때도 즉시 끝낸다.
            // 여기서 끝나면 hitPlayer 가 false 라 아래에서 기절이 붙는다 — 벽에 박은 것과 같은 취급.
            if (stall.IsStalled(entity.transform.position, chargeSpeed, Time.deltaTime))
            {
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
            }

            if (rb != null) rb.linearVelocity = chargeDir * chargeSpeed;

            float checkDist = chargeSpeed * Time.deltaTime + 0.2f;

            // [정리] 벽/플레이어 우선순위 판정은 CheckChargeHit로 공통화했다 (②일반 돌진과 동일 로직).
            var hitCheck = CheckChargeHit(entity.transform.position, chargeDir, chargeHitRadius, checkDist, wallMask, playerMask);
            bool outOfBounds = roomBounds.HasValue && !roomBounds.Value.Contains(entity.transform.position);

            if (hitCheck.WallBlocksFirst || outOfBounds)
            {
                // 벽에 박았다 = 플레이어가 회피에 성공했다. 아래에서 기절.
                if (rb != null) rb.linearVelocity = -chargeDir * 3f;
                entity.transform.position = (Vector2)entity.transform.position - chargeDir * 0.15f;
                break;
            }

            // 벽에 막히지 않았고, 플레이어가 (대쉬 무적이 아닌 상태로) 걸렸을 때만 직격시킵니다.
            // 대쉬(Player_Dash) 중인 플레이어는 관통(phase-through): 정지/반동/기절/데미지 전부 없이 돌진 지속.
            if (hitCheck.PlayerHittable)
            {
                hitPlayer = true;

                BossCombat.TryDamage(hitCheck.PlayerHit.collider, new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));

                if (rb != null) rb.linearVelocity = -chargeDir * 3f;
                entity.transform.position = (Vector2)entity.transform.position - chargeDir * 0.15f;
                break;
            }

            yield return null;
        }

        if (rb != null) rb.linearVelocity = Vector2.zero;

        RestoreAgentAfterDash(entity, agent, wasAgentEnabled);

        // [v1.5] 회피 성공 = 플레이어를 못 맞힌 것. 벽에 박았든(정상) 안전장치 시간이 다 됐든(비정상),
        // 빗나갔으면 무조건 기절을 준다. 방 구조나 방향 운 때문에 회피 보상을 못 받는 일이 없어야 한다.
        if (!hitPlayer && entity.Stats != null && entity.Stats.Status != null)
        {
            // 고정 스턴이어야 한다. 일반 ApplyStatus 로 걸면 자기 슈퍼아머(999999)가 씹어버려서
            // 회피 보상이 아예 발생하지 않는다 — 이 패턴의 존재 이유가 사라진다.
            entity.Stats.Status.ApplyFixedStun(wallChargeStunDuration);
        }
    }

    /// <summary>
    /// 돌진 경로를 나타내는 빨간 직사각형 전조를 생성/갱신합니다. (보스 위치 기준 전방으로 length만큼)
    /// </summary>
    private void UpdateChargeTelegraph(GameObject telegraph, Vector2 originPos, Vector2 dir, float length, float width, float progress01 = 1f)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 mid = originPos + dir * (length * 0.5f);
        telegraph.transform.position = mid;
        telegraph.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                // 배경 레인: 항상 전체 길이/폭. "어디로 올지"를 알린다.
        telegraph.transform.localScale = new Vector3(length, width, 1f);

        // [수정] 채움 게이지: "언제 올지"를 알린다.
        // 길이축으로 채우면 채울 거리가 대상과의 거리에 따라 매번 달라져 체감 속도가 들쭉날쭉해진다.
        // 폭은 항상 상수이므로 두께 방향으로 채워야 진행 속도가 일정해지고 학습이 가능해진다.
        // 자식은 부모 스케일을 물려받으므로 localScale.y = 진행도(0~1)면 실제 두께가 width * 진행도가 된다.
        // 중심 정렬이라 레인이 회전해도 좌우 대칭으로 벌어져 방향 혼동이 없다.
        Transform fill = telegraph.transform.childCount > 0 ? telegraph.transform.GetChild(0) : null;
        if (fill == null)
        {
            GameObject fillObj = CreateFallbackRect(chargeTelegraphFillColor);
            fillObj.name = "Elite_Telegraph_Rect_Fill";
            fill = fillObj.transform;
            fill.SetParent(telegraph.transform, false);

            var fillSr = fillObj.GetComponent<SpriteRenderer>();
            var baseSr = telegraph.GetComponent<SpriteRenderer>();
            if (fillSr != null && baseSr != null)
            {
                fillSr.sortingLayerID = baseSr.sortingLayerID;
                fillSr.sortingOrder = baseSr.sortingOrder + 1; // 배경 레인 바로 위
            }
        }

        fill.localPosition = Vector3.zero;
        fill.localRotation = Quaternion.identity;
        fill.localScale = new Vector3(1f, Mathf.Clamp01(progress01), 1f);
    }

private GameObject _chargeTelegraph;

    /// <summary>돌진 예고 레인 제거. 정상 종료/취소/파괴 모두에서 호출된다.</summary>
    private void ClearChargeTelegraph()
    {
        if (_chargeTelegraph != null)
        {
            GameObject.Destroy(_chargeTelegraph);
            _chargeTelegraph = null;
        }
    }

    // 피격/스턴/사망으로 공격이 끊길 때.
    public override void OnAttackCancelled(BaseEntity entity)
    {
        base.OnAttackCancelled(entity);
        ClearChargeTelegraph();
        ClearLabel();

        if (entity != null)
        {
            if (_basicAttackCoroutine != null)
            {
                entity.StopCoroutine(_basicAttackCoroutine);
                _basicAttackCoroutine = null;
            }
            if (_specialPatternCoroutine != null)
            {
                entity.StopCoroutine(_specialPatternCoroutine);
                _specialPatternCoroutine = null;
            }
            StopPursuitBurst(entity);

            var rb = entity.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }

        _isBusy = false;
    }


    // --- 패턴 3: 바닥 충격파 (보스를 중심으로 퍼져나가는 얇은 고리형 파동, 대쉬로 회피 가능) ---
    private IEnumerator Pattern3_GroundSlam(BaseEntity entity)
    {
        ShowLabel(label_Pattern3Windup);
        StopNavAgent(entity);

        // 뒷발로 일어섰다(프레임 39~40, 0.5초) 내려찍는(41~44, 0.425초) 모션을 예비동작 시간에 맞춰 늘린다.
        // 클립이 끝나는 순간 = 예비동작이 끝나고 첫 파동이 나가는 순간이라, 내려찍기와 파동이 맞물린다.
        PlayState(entity, animState_Slam, slamPreCastDelay, matchAnimSpeedToWindup);

        Vector2 preCenter = entity.transform.position;

        // 애니메이션이 없는 것을 보완하는 사전 예비동작: 발밑에 경고 원이 서서히 채워집니다. (이 동안은 무피해)
        GameObject warmup = CreateFallbackCircle(preCenter, 0.4f, new Color(1f, 0.4f, 0f, 0.15f));
        float wt = 0f;
        float scaledPreCastDelay = slamPreCastDelay;
        while (wt < scaledPreCastDelay)
        {
            wt += Time.deltaTime;
            float scale = Mathf.Lerp(0.4f, slamMeleeRadius * 2f, wt / scaledPreCastDelay);
            if (warmup != null)
            {
                warmup.transform.position = entity.transform.position;
                warmup.transform.localScale = Vector3.one * scale;
                var wsr = warmup.GetComponent<SpriteRenderer>();
                if (wsr != null) wsr.color = new Color(1f, 0.4f, 0f, Mathf.Lerp(0.15f, 0.4f, wt / scaledPreCastDelay));
            }
            yield return null;
        }
        if (warmup != null) GameObject.Destroy(warmup);

        ShowLabel(label_Pattern3);
        Vector2 center = entity.transform.position;

        RoomMetrics room = GetRoomMetrics(entity);
        float maxRadius = room.found ? Mathf.Max(room.halfX, room.halfY) * 1.15f : slamWaveFallbackMaxRadius;

        // 보스 근처 밀착 시 확정 피해 (꼼수 방지) - v1.5: 회피 수단은 대쉬 무적뿐입니다.
        LayerMask playerLayers = LayerMask.GetMask("Player", "Player_Dash");
        Collider2D[] meleeHits = Physics2D.OverlapCircleAll(center, slamMeleeRadius, playerLayers);
        foreach (var hit in meleeHits)
        {
            CharacterHealth pHealth = hit.GetComponentInChildren<CharacterHealth>();
            if (pHealth == null) pHealth = hit.GetComponentInParent<CharacterHealth>();
            bool isDashingLayer = hit.gameObject.layer == LayerMask.NameToLayer("Player_Dash");
            if (pHealth == null || pHealth.IsDead || pHealth.Invincible || isDashingLayer) continue; // LShift 대쉬(무적/레이어 전환)로 회피 가능

            // 위에서 이미 dash/무적/사망을 걸렀지만, 최종 데미지 전달은 BossCombat 단일 경로로 통일.
            BossCombat.TryDamage(hit, new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));
        }

        for (int wave = 0; wave < slamWaveCount; wave++)
        {
            yield return RunShockwaveRing(entity, center, maxRadius);

            if (wave < slamWaveCount - 1)
            {
                // [26/08/17] 파동 1회 = 내려찍기 1회. 예전엔 예비동작 때 한 번만 재생하고 나머지 파동이
                // 도는 내내(파동당 slamWaveExpandTime) 마지막 프레임에서 굳어 있었다 — 두 번째 파동은
                // 아무 동작 없이 링만 튀어나왔다. 파동 사이 간격에 맞춰 클립을 다시 늘려 재생하면
                // '다시 일어섰다 내려찍는' 순간과 다음 파동 발사가 맞물린다.
                PlayState(entity, animState_Slam, slamWaveInterval, matchAnimSpeedToWindup);
                yield return new WaitForSeconds(slamWaveInterval);
            }
        }

        // 마지막 파동이 퍼지는 동안은 내려찍은 자세로 굳어 있는 게 맞다(연출상 '여파').
        // 배속만 원복해 둔다 — RunSpecialPattern 말미에서도 하지만, 그 사이 스턴 등으로 끊길 수 있다.
        if (entity.Animator != null) entity.Animator.speed = 1f;
    }

    /// <summary>
    /// 보스 중심에서 maxRadius까지 퍼져나가는 얇은 고리형 충격파 1회를 재생합니다.
    /// 고리가 실제로 지나가는 순간에만 피해 판정을 하며, 그 순간 대쉬 무적 상태면 회피됩니다.
    /// </summary>
    private IEnumerator RunShockwaveRing(BaseEntity entity, Vector2 center, float maxRadius)
    {
        GameObject ring = CreateFallbackRing(new Color(1f, 0.35f, 0f, 0.5f));
        ring.transform.position = center;

        LayerMask targetLayer = LayerMask.GetMask("Player", "Player_Dash");

        // 공용 확장 링으로 통일. add-before-guard 순서(대쉬로 밴드를 흘려도 소비 → 재히트 없음) 보존. 웨이브마다 새 판정셋(호출 단위).
        yield return BossCombat.ExpandingRing(center, maxRadius, slamWaveExpandTime, slamRingThickness, targetLayer,
            onHit: (hit, dir) =>
            {
                CharacterHealth pHealth = hit.GetComponentInChildren<CharacterHealth>() ?? hit.GetComponentInParent<CharacterHealth>();
                bool isDashingLayer = hit.gameObject.layer == LayerMask.NameToLayer("Player_Dash");
                if (pHealth == null || pHealth.IsDead || pHealth.Invincible || isDashingLayer) return; // 대쉬 무적으로 완전 회피

                BossCombat.TryDamage(hit, new DamageInfo(slamWaveDamage, DamageType.Physical, entity.gameObject));
            },
            onExpand: (cur) =>
            {
                if (ring != null) ring.transform.localScale = new Vector3(cur * 2f, cur * 2f, 1f);
            });

        if (ring != null) GameObject.Destroy(ring);
    }

    // ==============================================================
    // 유틸리티: 장판/히트박스 생성 (프리팹이 없을 때 대체용)
    // ==============================================================
    private GameObject CreateFallbackCircle(Vector2 pos, float baseScale, Color color)
    {
        GameObject obj = new GameObject("Elite_Telegraph_Circle");
        obj.transform.position = pos;
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateCircleSprite();
        sr.color = color;
        sr.sortingOrder = 10;
        obj.transform.localScale = Vector3.one * baseScale;
        return obj;
    }

    private GameObject CreateFallbackRect(Color color)
    {
        GameObject obj = new GameObject("Elite_Telegraph_Rect");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateSquareSprite();
        sr.color = color;
        sr.sortingOrder = 10;
        return obj;
    }

    private GameObject CreateFallbackRing(Color color)
    {
        GameObject obj = new GameObject("Elite_Telegraph_Ring");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateRingSprite();
        sr.color = color;
        sr.sortingOrder = 10;
        return obj;
    }

    // 절차적 텔레그래프 스프라이트는 BossTelegraph 로 일원화(SO/기둥/BossTelegraph 3벌 중복 제거). 링 두께 정규값 0.85.
    private static Sprite GetOrCreateCircleSprite() => BossTelegraph.GetCircleSprite();

    private static Sprite GetOrCreateSquareSprite() => BossTelegraph.GetSquareSprite();

    private static Sprite GetOrCreateRingSprite() => BossTelegraph.GetRingSprite();
}
