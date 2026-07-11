using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 스테이지 1 엘리트 몬스터(차저) AI 패턴입니다.
///
/// 전투 흐름:
/// 1) 방 입장
/// 2) 기본 공격을 8초간 반복
/// 3) 8초가 지나면 모든 행동을 강제로 인터럽트하고, 패턴 1/2/3 중 1개를 무작위로 발동
/// 4) 패턴 종료 후 다시 기본 공격을 8초간 반복
/// 5) 다음 차례에는 "아직 쓰지 않은 나머지 2개" 중 1개를 무작위로 발동
/// 6) 기본 공격 8초 반복 -> 7) 마지막 남은 1개 패턴을 발동 -> 8) 기본 공격 8초 반복
/// 9) 이후 다시 3개 중 1개를 무작위로 뽑는 사이클로 복귀 (풀 리필)
///
/// [v1.2] "차저"라는 정체성이 8초에 한 번뿐인 특수 패턴 1개에만 있어 정적으로 느껴진다는
/// 피드백을 반영해, 기본 공격을 "3단 돌진 체계"로 재편했습니다.
///   ① 약한 돌진: 기본 공격 3종 중 "미니 돌진 찍기" (실제 짧은 전진 + 스쿼시/스트레치 연출)
///   ② 추격 버스트: 사거리 밖에서 추격(Follow)하는 동안 간헐적으로 짧게 가속 후 감속
///   ③ 강한 돌진: 기존 패턴 1 (기둥과 돌진) - 스펙 변경 없이 그대로 유지
///
/// [v1.3] 패턴 2를 "안 팎 도넛"에서 "중력 도넛 폭발"로 완전히 교체했습니다.
///   - 엘리트 몹이 6초간 자신을 중심으로 주변 대상을 끌어당깁니다.
///   - 매 프레임 연속으로 끄는 대신, gravityPullTickInterval초마다 한 번씩 gravityPullTickDistance만큼
///     "틱" 형태로 짧게 끌어당깁니다. 틱과 틱 사이에는 플레이어가 완전히 자유롭게 움직일 수 있어,
///     기획 의도대로 "6초 동안 기둥 뒤로 도망갈 시간"이 실제로 주어집니다.
///   - 살아있는 기둥 뒤에 서면 그 틱에서 끌려가지 않고(유일한 회피 수단), 그 기둥은 대신 내구도 2를 잃습니다.
///   - 6초가 끝나면 "폭발"하여, 기둥 뒤에 숨지 못한 대상에게 직접 피해를 줍니다.
///   - 이 패턴은 보스에게 그로기(기절)를 전혀 부여하지 않습니다.
///
/// [중요] Unity 엔진의 BaseEntity.Update()는 IsAttacking == true인 동안(공격 windup~후딜레이)에는
/// CanExecuteAI()가 false를 반환해 브레인의 Execute()를 아예 호출하지 않습니다. 그래서 8초 판정은
/// Time.deltaTime 누적이 아니라, "기본 공격 상태로 돌아온 절대 시각(Time.time)"을 기록해두고 그로부터
/// 8초가 지났는지를 비교하는 방식으로 계산합니다. Execute()가 얼마나 뜸하게 불리든 상관없이 정확합니다.
///
/// - 플레이어의 기본 공격에는 슈퍼아머로 경직/넉백되지 않습니다.
/// - 현재 사용 중인 공격/패턴 이름을 보스 머리 위에 한글로 표시합니다.
/// (기획서: 스테이지 1 엘리트 몬스터 기획, EliteMob1.2 최신 수정안 기준)
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
    //     - 특수 3종: RunSpecialPattern(p)의 switch(p) → Pattern1_PillarCharge / Pattern2_GravityDonut /
    //       Pattern3_GroundSlam.
    //
    // ▷ 목표 구조(리팩 후):
    //   각 공격을 IBossAction 구현 1개로 분리하고, 브레인은 "고르고 실행"만 한다.
    //     _basics   = { MiniStabAction, NormalChargeAction, SweepAction };
    //     _specials = { PillarChargeAction, GravityDonutAction, GroundSlamAction };
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
    //        - GetRoomMetrics / GetBoundsExitDistance / ScaleCoroutine / MiniChargeDash / StopNavAgent /
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

    [Header("① 미니 돌진 찍기 (약한 돌진)")]
    [Tooltip("비워두면 기본 원형 히트박스로 대체됩니다")]
    public GameObject stabHitboxPrefab;
    [Tooltip("시전(웅크림+대시) 시간")]
    public float stabWindup = 1.0f;
    [Tooltip("대시 끝에 생기는 전방 원형 판정 반경")]
    public float stabRadius = 4.4f;
    [Tooltip("전방으로 실제로 전진하는 거리")]
    public float miniChargeDistance = 3.6f;
    [Tooltip("전진(대시)에 걸리는 시간. 나머지 windup 시간은 웅크림(스쿼시) 연출에 사용됩니다")]
    public float miniChargeDashDuration = 0.15f;
    [Tooltip("대시 도중 벽/기둥 충돌 검사에 사용할 반경")]
    public float miniChargeCheckRadius = 0.6f;
    [Tooltip("웅크릴 때의 스케일 배율 (1보다 작을수록 더 낮고 넓게 웅크립니다)")]
    public float miniChargeSquashScale = 0.82f;
    [Tooltip("튀어나갈 때의 스케일 배율 (1보다 클수록 더 크게 튀어나가 보입니다)")]
    public float miniChargeStretchScale = 1.2f;
    [Tooltip("전방 원형 판정 범위 안에 살아있는 기둥이 있을 때, 그 기둥이 잃는 내구도")]
    public int miniChargePillarDamage = 1;

    [Header("② 일반 돌진 (v1.35 신규)")]
    [Tooltip("일반 차저가 가진 직선형 고속 돌진입니다. 데미지는 낮고, 기둥에 닿으면 내구도만 1 깎입니다 (파훼 없이 그 자리에서 멈춤)")]
    public float normalChargeWindup = 0.8f;
    [Tooltip("돌진 속도 배율 (보스 이동속도 대비). ①번보다는 빠르지만 패턴 1의 강한 돌진보다는 느립니다")]
    public float normalChargeSpeedMultiplier = 7f;
    [Tooltip("돌진 지속(최대) 시간. 벽/기둥/플레이어에 맞으면 그 전에 멈추어요")]
    public float normalChargeMaxDuration = 1.2f;
    [Tooltip("돌진 중 충돌 검사에 사용할 반경")]
    public float normalChargeHitRadius = 1.0f;
    [Tooltip("플레이어 직격 시 피해량 배율 (ATK 대비, 약하게)")]
    public float normalChargeDamageMultiplier = 0.5f;
    [Tooltip("기둥에 닿았을 때 그 기둥이 잃는 내구도 (파훼되지는 않고 돌진만 멈춤)")]
    public int normalChargePillarDamage = 1;
    [Tooltip("예비동작(윈드업) 동안 웅크린 정도")]
    public float normalChargeSquashScale = 0.82f;
    [Tooltip("돌진 시작 순간 튀어나가는 스트레치 정도 (미니 돌진보다 더 과장해서 확실한 느낌을 줍니다)")]
    public float normalChargeStretchScale = 1.4f;

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
    // 기둥 설정
    // ==============================================================
    [Header("기둥 설정")]
    [Tooltip("비워두면 코드에서 임시 원기둥 형태로 생성합니다.")]
    public GameObject pillarPrefab;
    public int pillarCount = 4;
    [Tooltip("방을 찾지 못했을 때(fallback) 보스 기준 기둥 배치 거리")]
    public float pillarSpawnDistance = 5.5f;
    public int pillarMaxHP = 4;
    [Tooltip("방 벽에서 기둥까지 남겨둘 최소 여백(절대값). 값이 클수록 기둥이 벽에서 멀어집니다.")]
    public float pillarWallMargin = 3f;
    [Range(0.1f, 1f)]
    [Tooltip("벽 여백을 제외한 남은 절반 크기 중 기둥이 실제로 밀려나는 비율. 1에 가까울수록 벽에 붙고, 작을수록 중앙 쪽으로 모입니다.")]
    public float pillarInwardFactor = 0.65f;

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
    public string label_Pattern1Windup = "기둥과 돌진 준비";
    public string label_Pattern1 = "돌진!";
    public string label_Pattern2 = "중력 도넛 폭발";
    public string label_Pattern3Windup = "바닥 충격파 준비";
    public string label_Pattern3 = "바닥 충격파";

    [Header("v1.4 B2 - 기둥 스택 표시 (체력바 근처, 몇 번 맞았는지 pip으로 표시)")]
    [Tooltip("보스 기준 스택 인디케이터 위치. 월드 HP 바 높이에 맞춰 조정하세요.")]
    public Vector3 stackIndicatorOffset = new Vector3(0f, 1.0f, 0f);
    public float stackIndicatorPipSize = 0.16f;
    public float stackIndicatorPipSpacing = 0.2f;
    public Color stackIndicatorFilledColor = new Color(1f, 0.35f, 0.1f);
    public Color stackIndicatorEmptyColor = new Color(0.15f, 0.15f, 0.15f, 0.7f);

    // ==============================================================
    // 특수 패턴 공통 설정
    // ==============================================================
    [Header("특수 패턴 공통 설정 (기본 공격 8초 -> 패턴 1회 -> 기본 공격 8초 -> ...)")]
    [Tooltip("기본 공격 상태로 돌아온 뒤 이 시간(초)이 지나면, 하던 행동을 강제로 중단하고 패턴을 발동합니다.")]
    public float specialPatternInterval = 8f;

    // --- 패턴 1: 기둥과 돌진 (3단 돌진 체계 ③ 강한 돌진) ---
    [Header("패턴 1 - 기둥과 돌진 (v1.2: 3단 돌진 체계의 ③ 강한 돌진 단계. 스펙 변경 없음)")]
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
    [Tooltip("기둥에 박았을 때 보스 기절 시간")]
    public float pillarChargeStunDuration = 5f;
    [Tooltip("기둥이 없거나 벽에 유도되었을 때 보스 기절 시간")]
    public float wallChargeStunDuration = 1.5f;

    // --- 패턴 2: 중력 도넛 폭발 (v1.3, 기존 안/팎 도넛 완전 대체) ---
    [Header("패턴 2 - 중력 도넛 폭발 (엘리트 몹 위치 중심, 방 크기에 비례해서 자동 조정됩니다)")]
    [Tooltip("플레이어를 끌어당기는 총 지속시간")]
    public float gravityPullDuration = 6f;
    [Tooltip("몇 초마다 한 번씩 끌어당길지. 이 간격 사이에는 플레이어가 완전히 자유롭게 움직일 수 있습니다.")]
    public float gravityPullTickInterval = 1f;
    [Tooltip("한 번(1틱)에 순간적으로 끌려가는 거리")]
    public float gravityPullTickDistance = 1.0f;
    [Tooltip("판정 범위 반경의 비율 (방의 대각선 기준). 맵 전체를 덮도록 크게 확장됨 (방 반경이 아니라 대각선 기준이라 모서리까지 확실히 덮습니다)")]
    public float gravityFieldRadiusRatio = 1.3f;
    [Tooltip("방을 찾지 못했을 때(fallback) 사용할 판정 범위 절대 반경")]
    public float gravityFieldFallbackRadius = 20f;
    [Tooltip("6초 종료 시 '폭발' 피해량 (기둥 뒤에 숨지 못한 대상에게 적용)")]
    public float gravityExplosionDamage = 22f;
    [Tooltip("폭발 시점에 기둥 뒤에 숨어 회피한 경우, 그 기둥이 대신 입는 내구도 피해")]
    public int gravityPillarDamage = 2;
    [Tooltip("중력장 시각화 색상")]
    public Color gravityFieldColor = new Color(0.5f, 0.22f, 0.85f, 0.32f);

    // --- 패턴 3: 바닥 충격파 ---
    [Header("패턴 3 - 바닥 충격파 (파동이 방 끝까지 퍼져나갑니다, 대쉬로 회피 가능)")]
    [Tooltip("애니메이션이 없는 것을 보완하기 위한 사전 예비동작 시간. 이 시간 동안 보스 발밑에 경고 원이 서서히 채워지며, 아직 피해는 없습니다.")]
    public float slamPreCastDelay = 0.9f;
    [Tooltip("보스 근처 확정 피해 반경 (꼼수 방지, 대쉬 무적/기둥 뒤 숨기로 회피됩니다)")]
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
    [Tooltip("기둥 뒤에 숨어서 충격파를 회피했을 때, 그 기둥이 대신 입는 내구도 피해 (1회당)")]
    public int slamPillarDamagePerWave = 2;

    // ==============================================================
    // v1.4 D - 2페이즈 전환 (하울링, HP 40% 최초 1회만 발동)
    // ==============================================================
    [Header("D0 - 2페이즈 전환 하울링 (HP 임계치에서 최초 1회만 발동)")]
    [Tooltip("0 = 2페이즈 발동함(기본값), 1 = 2페이즈 발동 안함 (디버그/밸런스 테스트용 스위치)")]
    public int disablePhase2 = 0;
    [Range(0.1f, 0.6f)]
    [Tooltip("보스 체력 비율이 이 값 아래로 내려가는 순간(최초 1회) 하울링 전환이 발동합니다.")]
    public float phase2HpThreshold = 0.4f;
    public string label_Howl = "하울링!";
    [Tooltip("하울링 시전 중 보스를 무적으로 만들지 여부 (연출 도중 부당하게 맞아 타이밍이 꼬이는 것 방지)")]
    public bool howlBossInvincible = true;
    [Tooltip("하울링 링 색상")]
    public Color howlRingColor = new Color(0.85f, 0.8f, 1f, 0.55f);
    [Tooltip("하울링 링이 중심에서 방 크기만큼 퍼지는 데 걸리는 시간")]
    public float howlRingExpandTime = 1.2f;
    [Tooltip("하울링 링의 실제 판정 두께 (이 두께만큼 스쳐 지나가는 순간에만 판정 - 대쉬 무적으로 완전 회피 가능)")]
    public float howlRingThickness = 1.2f;
    [Tooltip("하울링에 맞았을 때 밀려나는 힘 (데미지는 없음)")]
    public float howlKnockbackForce = 5f;
    [Tooltip("밀려나는 데 걸리는 시간")]
    public float howlKnockbackDuration = 0.25f;
    [Tooltip("밀려난 뒤 부여되는 경직(행동 불가) 시간")]
    public float howlHitstunDuration = 0.4f;
    [Tooltip("하울링 발동 시 플레이어 카메라 흔들림 강도")]
    public float howlCameraShakeForce = 1.8f;

    [Header("D1 - 2페이즈 이후 상시 속도 증가 (전투 끝까지 유지, 이동속도/시전 윈드업/8초 주기에 동일 비율 적용)")]
    [Range(0.1f, 0.15f)]
    [Tooltip("0.125 = 12.5%. 이동속도는 곱으로 증가하고, 시전(윈드업)과 8초 특수 패턴 주기는 같은 비율로 단축됩니다.")]
    public float phase2SpeedIncreasePercent = 0.125f;

    // ==============================================================
    // 런타임 상태 (ScriptableObject 공유 인스턴스 기준 - 다른 보스 패턴들과 동일한 구조)
    // ==============================================================
    private bool _isBusy = false;
    private bool _pillarsSpawned = false;
    private bool _superArmorApplied = false;
    private PillarField _pillarField; // 기둥 무리의 수명/질의/정리를 캡슐화 (기존 List<EliteMonsterPillar> 대체)
    private BossActionScheduler _scheduler; // 기본 no-repeat + 특수 무반복풀 + 타이머 선택 로직 일원화
    private EliteBossPatternLabel _label;
    private EliteChargerStackIndicator _stackIndicator; // v1.4 B2
    private Coroutine _basicAttackCoroutine;

    // v1.2 추격 버스트 런타임 상태
    private Coroutine _pursuitBurstCoroutine;
    private bool _isBursting = false;
    private float _nextBurstTime;

    // 마지막으로 유효했던(0벡터가 아니었던) 조준 방향입니다. 목표와 완전히 겹치는 등 방향 계산이
    // 불가능한 순간에도, 임의의 고정 방향(예: 오른쪽) 대신 이 값을 사용해 "플레이어 반대 방향으로
    // 공격이 나가는" 것처럼 보이는 문제를 방지합니다.
    private Vector2 _lastAimDir = Vector2.down;


    // ==============================================================
    // v1.4 B2: 기둥 파편 누적 강화 스택 (전투 끝까지 영구 유지, 시간 경과로 안 풀림)
    // ==============================================================
    private int _pillarDamageStackCount = 0;
    private CharacterHealth _selfHealthRef;
    private const float PillarStackBonusPerStack = 0.05f;
    private const int PillarStackMaxCount = 4;

    // v1.4 D0/D3: 2페이즈 전환은 전투당 최초 1회만 발동합니다.
    private bool _phase2Triggered = false;

    /// <summary>
    /// v1.4 D1: 2페이즈 전환 이후, "시전(윈드업)" 성격의 지속시간을 이동속도 증가와 같은 비율로 단축합니다.
    /// (예: phase2SpeedIncreasePercent=0.15면 15% 빨라짐 -> 지속시간은 1/1.15배로 단축)
    /// </summary>
    private float ScaleDuration(float baseDuration)
    {
        return _phase2Triggered ? baseDuration / (1f + phase2SpeedIncreasePercent) : baseDuration;
    }

    // #6 정리: 브레인 클론이 파괴될 때(엔티티 사망/씬 언로드/재초기화 — BaseEntity.OnDestroy:325) 남은 기둥을 전부 제거.
    // 기둥은 보스의 자식이 아니라 월드에 스폰되므로, 이 훅이 없으면 클리어한 방에 유령 기둥이 남는다.
    private void OnDestroy()
    {
        _pillarField?.DestroyAll();
    }

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _isBusy = false;
        _pillarsSpawned = false;
        _superArmorApplied = false;
        _pillarField?.DestroyAll();          // 재초기화 대비: 이전 기둥 정리 후
        _pillarField = new PillarField();     // 새 필드로 교체
        _scheduler = new BossActionScheduler(3, 3);
        _label = null;
        _stackIndicator = null; // v1.4 B2
        _basicAttackCoroutine = null;
        _pursuitBurstCoroutine = null;
        _isBursting = false;
        _nextBurstTime = Time.time + Random.Range(pursuitBurstMinInterval, pursuitBurstMaxInterval);
        _lastAimDir = Vector2.down;
        _scheduler.ResetBasicPhase(Time.time);

        // v1.4 D0/D3: 2페이즈 전환 플래그도 전투마다 초기화
        _phase2Triggered = false;

        // v1.4 B2: 스택 초기화 및 데미지 증폭 훅 구독 (전투마다 새로 시작, 죽으면 구독 해제)
        _pillarDamageStackCount = 0;
        DamageEventBus.OnBeforeDamageCalculated -= HandlePillarStackDamageAmp; // 중복 구독 방지 안전장치
        DamageEventBus.OnBeforeDamageCalculated += HandlePillarStackDamageAmp;
        _selfHealthRef = (entity.Stats != null) ? entity.Stats.Health : null;
        if (_selfHealthRef != null)
        {
            _selfHealthRef.OnDeath -= UnsubscribePillarStackHandler;
            _selfHealthRef.OnDeath += UnsubscribePillarStackHandler;
        }
    }

    private void UnsubscribePillarStackHandler()
    {
        DamageEventBus.OnBeforeDamageCalculated -= HandlePillarStackDamageAmp;
    }

    private void HandlePillarStackDamageAmp(CharacterHealth target, ref DamageInfo info)
    {
        if (_pillarDamageStackCount <= 0) return;
        if (target == null || target != _selfHealthRef) return;
        info.amount *= (1f + PillarStackBonusPerStack * _pillarDamageStackCount);
    }

    /// <summary>
    /// v1.4 B2: 기둥 파편에 보스가 맞을 때(균열 카운터 성공 / 재생성 유도 성공)마다 호출됩니다.
    /// 받는 피해 +5%, 최대 20%(4스택)까지 전투 끝까지 영구적으로 누적됩니다 (시간 경과로 안 풀림).
    /// </summary>
    public void AddPillarDamageStack()
    {
        if (_pillarDamageStackCount >= PillarStackMaxCount) return;
        _pillarDamageStackCount++;
        Debug.Log($"<color=orange>[EliteCharger]</color> 기둥 누적 강화 스택 획득! {_pillarDamageStackCount}/{PillarStackMaxCount} (받는 피해 +{_pillarDamageStackCount * 5}%)");
        _stackIndicator?.UpdateStack(_pillarDamageStackCount, PillarStackBonusPerStack);
    }

    public override void Execute(BaseEntity entity)
    {
        if (entity.CurrentState == AIState.Thrown || entity.CurrentState == AIState.Caught) return;

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

        if (!_pillarsSpawned)
        {
            SpawnPillars(entity);
            _pillarsSpawned = true;
        }

        if (showPatternLabel && _label == null)
        {
            _label = CreatePatternLabel(entity);
        }

        if (_stackIndicator == null)
        {
            _stackIndicator = CreateStackIndicator(entity);
        }

        // v1.4 D0/D3: 2페이즈 전환(하울링) - 체력 임계치를 넘는 순간 최초 1회, 다른 모든 행동보다 우선 발동
        if (disablePhase2 == 0 && !_phase2Triggered && !_isBusy && entity.Stats != null && entity.Stats.MAXHP > 0f)
        {
            float hpRatio = entity.Stats.CURHP / entity.Stats.MAXHP;
            if (hpRatio < phase2HpThreshold)
            {
                _phase2Triggered = true;

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

                entity.StartCoroutine(RunPhase2Transition(entity));
                return;
            }
        }

        // 참고: 엔진(BaseEntity.Update -> CanExecuteAI)이 IsAttacking == true인 동안에는 이 함수 자체를
        // 호출하지 않으므로, 아래 인터럽트 분기는 사실상 "공격과 공격 사이의 짧은 순간"에만 유효합니다.
        // 그래도 8초 판정 자체는 Time.time 절대시각 비교라 정확합니다.
        if (!_isBusy)
        {
            entity.LookAtTarget(entity.Target);

            if (_scheduler.ShouldTriggerSpecial(Time.time, ScaleDuration(specialPatternInterval)))
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
                entity.StartCoroutine(RunSpecialPattern(entity, pattern));
                return;
            }

            if (!entity.IsAttacking)
            {
                entity.AtkTimer += Time.deltaTime;

                float dist = Vector2.Distance(entity.transform.position, entity.Target.position);
                var agent = entity.GetComponent<NavMeshAgent>();

                if (dist <= entity.Stats.ATKRANGE && entity.AtkTimer >= entity.Stats.ATKSPD)
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
    // 기둥 소환
    // ==============================================================
    private void SpawnPillars(BaseEntity entity)
    {
        RoomMetrics room = GetRoomMetrics(entity);
        Vector2 origin = entity.transform.position;
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        List<Vector2> positions = new List<Vector2>();
        for (int i = 0; i < pillarCount && i < dirs.Length; i++)
        {
            Vector2 targetPos;
            if (room.found)
            {
                float halfX = Mathf.Max(1f, room.halfX - pillarWallMargin);
                float halfY = Mathf.Max(1f, room.halfY - pillarWallMargin);
                float marginX = halfX * pillarInwardFactor;
                float marginY = halfY * pillarInwardFactor;

                targetPos = room.center + new Vector2(dirs[i].x * marginX, dirs[i].y * marginY);
            }
            else
            {
                targetPos = origin + dirs[i] * pillarSpawnDistance;
            }

            if (NavMesh.SamplePosition(targetPos, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
            {
                targetPos = navHit.position;
            }

            positions.Add(targetPos);
        }

        // 실제 스폰/수명/정리는 PillarField 가 담당 (프리팹 없으면 내부 fallback). 배치 계산만 여기서.
        _pillarField.Spawn(entity.gameObject, pillarPrefab, positions, pillarMaxHP);
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

    /// <summary>
    /// v1.4 B2: 기둥 파편에 맞은 횟수(누적 강화 스택)를 보스 체력바 근처에 pip으로 표시합니다.
    /// </summary>
    private EliteChargerStackIndicator CreateStackIndicator(BaseEntity entity)
    {
        GameObject obj = new GameObject("PillarStackIndicator");
        obj.transform.SetParent(entity.transform, false);
        obj.transform.localPosition = stackIndicatorOffset;

        Vector3 lossy = entity.transform.lossyScale;
        float invX = lossy.x != 0f ? 1f / lossy.x : 1f;
        float invY = lossy.y != 0f ? 1f / lossy.y : 1f;
        obj.transform.localScale = new Vector3(invX, invY, 1f);

        EliteChargerStackIndicator indicator = obj.AddComponent<EliteChargerStackIndicator>();
        indicator.Build(PillarStackMaxCount, stackIndicatorPipSize, stackIndicatorPipSpacing, stackIndicatorFilledColor, stackIndicatorEmptyColor, patternLabelFont);
        indicator.UpdateStack(_pillarDamageStackCount, PillarStackBonusPerStack);
        return indicator;
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
        var agent = entity.GetComponent<NavMeshAgent>();

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

        if (entity.Animator != null && entity.Animator.runtimeAnimatorController != null)
        {
            entity.Animator.speed = 1f;
            entity.Animator.Play("Attack", -1, 0f);
        }

        int atkIndex = _scheduler.NextBasic(); // 0: 미니 돌진 찍기, 1: 일반 돌진, 2: 휩쓸기

        float windup = atkIndex == 0 ? ScaleDuration(stabWindup) : atkIndex == 1 ? ScaleDuration(normalChargeWindup) : ScaleDuration(sweepWindup);
        float radius = atkIndex == 0 ? stabRadius : atkIndex == 1 ? normalChargeHitRadius : sweepRadius;
        string labelText = atkIndex == 0 ? label_Stab : atkIndex == 1 ? label_NormalCharge : label_Sweep;

        ShowLabel(labelText);

        Vector2 dir = GetAimDir(entity);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (atkIndex == 0)
        {
            // ① 약한 돌진 (v1.2): 제자리 판정 대신 실제로 짧은 거리를 전진하며 찍습니다.
            yield return MiniChargeStabRoutine(entity, dir, radius, windup);
        }
        else if (atkIndex == 1)
        {
            // ② 일반 돌진 (v1.35 신규): 일반 차저의 직선형 고속 돌진입니다. 데미지는 낮고, 기둥에
            // 닿으면 기둥의 내구도를 1 깎으며 돌진이 멈춥니다 (기둥이 완전히 무너지진 않습니다).
            yield return NormalChargeRoutine(entity, dir, windup);
        }
        else
        {
            // ③ 휩쓸기: 보스 자신을 중심으로 원형 범위
            Vector2 spawnPos = entity.transform.position;

            GameObject hitboxObj = sweepHitboxPrefab != null
                ? GameObject.Instantiate(sweepHitboxPrefab, spawnPos, Quaternion.Euler(0, 0, angle))
                : CreateFallbackCircle(spawnPos, 0.5f, new Color(1f, 0f, 0f, 0.35f));

            hitboxObj.transform.localScale = Vector3.one * radius;

            BaseHitBox hb = hitboxObj.GetComponent<BaseHitBox>();
            if (hb == null) hb = hitboxObj.AddComponent<BaseHitBox>();

            DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, false, 1f, true);
            hb.Init(info, entity.opponentLayer, 0.25f, windup, entity.team == Team.Ally);

            float t = 0f;
            while (t < windup)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        entity.HasFiredHitEvent = true;

        // 모든 기본 공격 후 1초 후딜레이 (연속 즉시 시전 방지)
        yield return new WaitForSeconds(basicAttackPostDelay);

        if (entity.Animator != null) entity.Animator.speed = 1f;
        entity.IsAttacking = false;
        _basicAttackCoroutine = null;
        entity.ResetAnimationState();
        ClearLabel();
    }

    // ==============================================================
    // ① 약한 돌진: 미니 돌진 찍기 (v1.2)
    // 전용 애니메이션이 없어서, 웅크렸다가(스쿼시) 튀어나가는(스트레치) 스케일 연출로
    // 그 부재를 보완합니다. 실제 이동은 벽/기둥에 막히면 그 앞에서 멈춥니다.
    // ==============================================================
    private IEnumerator MiniChargeStabRoutine(BaseEntity entity, Vector2 dir, float radius, float windup)
    {
        float dashDuration = Mathf.Min(miniChargeDashDuration, Mathf.Max(0.05f, windup - 0.1f));
        float squashDuration = Mathf.Max(0.05f, windup - dashDuration);

        entity.StartCoroutine(ScaleCoroutine(entity, squashDuration, miniChargeSquashScale, dashDuration, miniChargeStretchScale));

        yield return new WaitForSeconds(squashDuration);

        yield return MiniChargeDash(entity, dir, miniChargeDistance, dashDuration);

        Vector2 spawnPos = (Vector2)entity.transform.position + dir * 0.6f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        GameObject hitboxObj = stabHitboxPrefab != null
            ? GameObject.Instantiate(stabHitboxPrefab, spawnPos, Quaternion.Euler(0f, 0f, angle))
            : CreateFallbackCircle(spawnPos, 0.5f, new Color(1f, 0f, 0f, 0.35f));
        hitboxObj.transform.localScale = Vector3.one * radius;

        BaseHitBox hb = hitboxObj.GetComponent<BaseHitBox>();
        if (hb == null) hb = hitboxObj.AddComponent<BaseHitBox>();

        DamageInfo info = new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject, false, 1f, true);
        hb.Init(info, entity.opponentLayer, 0.25f, 0.05f, entity.team == Team.Ally);

        // v1.3: 최종 전방 원형 판정(spawnPos, radius) 안에 있는 살아있는 기둥에게만 내구도 피해.
        _pillarField.DamageInRadius(spawnPos, radius, miniChargePillarDamage);

        yield return new WaitForSeconds(0.05f);
    }

    /// <summary>
    /// 짧은 거리를 실제로 전진합니다. 대시 경로에 벽/기둥이 있으면 그 앞에서 멈춥니다.
    /// </summary>
    private IEnumerator MiniChargeDash(BaseEntity entity, Vector2 dir, float distance, float duration)
    {
        LayerMask wallMask = LayerMask.GetMask("Wall", "Object");
        float clampedDistance = distance;

        RaycastHit2D obstacleHit = Physics2D.CircleCast(entity.transform.position, miniChargeCheckRadius, dir, distance, wallMask);
        if (obstacleHit.collider != null)
        {
            clampedDistance = Mathf.Max(0.1f, obstacleHit.distance - 0.1f);
        }

        Vector2 start = entity.transform.position;
        Vector2 end = start + dir * clampedDistance;

        // 방 경계를 벗어나는 것을 막는 안전장치 (벽 콜라이더를 놓치고 통과해버리는 경우 대비)
        RoomMetrics room = GetRoomMetrics(entity);
        if (room.found)
        {
            end = new Vector2(
                Mathf.Clamp(end.x, room.bounds.min.x, room.bounds.max.x),
                Mathf.Clamp(end.y, room.bounds.min.y, room.bounds.max.y));
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);
            entity.transform.position = Vector2.Lerp(start, end, f);
            yield return null;
        }
        entity.transform.position = end;
    }

    /// <summary>
    /// ② 일반 돌진 (v1.35 신규): 일반 차저가 가진 직선형 고속 돌진입니다. 미니 돌진 찍기보다
    /// 빠르고 멀리 가지만, 패턴 1의 강한 돌진보다는 약하고 데미지도 낮습니다. 기둥에 닿으면
    /// (강한 돌진처럼 완전히 무너뜨리는 게 아니라) 기둥의 내구도만 1 깎고 그 자리에서 멈춥니다.
    /// </summary>
    private IEnumerator NormalChargeRoutine(BaseEntity entity, Vector2 dir, float windup)
    {
        // 다른 기본 공격들과 통일된 예비동작: 짧은 직선 전조를 표시합니다.
        GameObject telegraph = CreateFallbackRect(new Color(1f, 0.55f, 0f, 0.3f));

        // 확실한 돌진 느낌을 위해, 윈드업 동안 웅크렸다가 돌진 시작 순간 크게 튀어나가는 스쿼시/스트레치 연출입니다.
        entity.StartCoroutine(ScaleCoroutine(entity, windup, normalChargeSquashScale, 0.2f, normalChargeStretchScale));
        float estimatedLength = entity.Stats.MOVESPEED * normalChargeSpeedMultiplier * normalChargeMaxDuration;

        float wt = 0f;
        while (wt < windup)
        {
            wt += Time.deltaTime;
            if (entity.Target != null) dir = GetAimDir(entity);
            UpdateChargeTelegraph(telegraph, entity.transform.position, dir, estimatedLength, normalChargeHitRadius * 2f);
            yield return null;
        }
        if (telegraph != null) GameObject.Destroy(telegraph);

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
        float elapsed = 0f;

        LayerMask playerMask = LayerMask.GetMask("Player", "Player_Dash");
        LayerMask wallMask = LayerMask.GetMask("Wall", "Object");

        // 방 경계 안전장치 (벽/기둥 충돌 감지를 놓치고 통과해버리는 경우 대비)
        RoomMetrics room = GetRoomMetrics(entity);
        Bounds? roomBounds = room.found ? (Bounds?)room.bounds : null;

        while (elapsed < normalChargeMaxDuration)
        {
            elapsed += Time.deltaTime;

            if (roomBounds.HasValue && !roomBounds.Value.Contains(entity.transform.position))
            {
                if (rb != null) rb.linearVelocity = Vector2.zero;
                entity.transform.position = (Vector2)entity.transform.position - dir * 0.3f;
                break;
            }

            if (rb != null) rb.linearVelocity = dir * chargeSpeed;

            float checkDist = chargeSpeed * Time.deltaTime + 0.15f;

            RaycastHit2D obstacleHit = Physics2D.CircleCast(entity.transform.position, normalChargeHitRadius, dir, checkDist, wallMask);
            if (obstacleHit.collider != null)
            {
                // 강한 돌진(패턴 1)과 달리, 기둥을 완전히 무너뜨리지 않고 내구도만 1 깎습니다.
                EliteMonsterPillar hitPillar = obstacleHit.collider.GetComponentInParent<EliteMonsterPillar>();
                if (hitPillar != null && hitPillar.IsAlive)
                {
                    hitPillar.DamagePattern(normalChargePillarDamage);
                }
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
            }

            RaycastHit2D playerHit = Physics2D.CircleCast(entity.transform.position, normalChargeHitRadius, dir, checkDist, playerMask);
            // 대쉬(Player_Dash) 중인 플레이어는 관통(phase-through): 멈추지도/데미지도 없이 그대로 돌진 지속.
            // 그 외 플레이어만 정지 + 데미지. (여전히 레이어 기반 CircleCast — 감지는 하되 대쉬는 통과시킴)
            if (playerHit.collider != null && playerHit.collider.gameObject.layer != LayerMask.NameToLayer("Player_Dash"))
            {
                BossCombat.TryDamage(playerHit.collider, new DamageInfo(entity.Stats.ATK * normalChargeDamageMultiplier, DamageType.Physical, entity.gameObject));
                if (rb != null) rb.linearVelocity = Vector2.zero;
                break;
            }

            yield return null;
        }

        if (rb != null) rb.linearVelocity = Vector2.zero;

        if (wasAgentEnabled && agent != null)
        {
            if (NavMesh.SamplePosition(entity.transform.position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                entity.transform.position = navHit.position;
            }
            agent.enabled = true;
            agent.isStopped = false;
        }
    }

    /// <summary>
    /// 애니메이션 부재를 보완하기 위한 스쿼시(웅크림) -> 스트레치(튀어나감) 스케일 연출입니다.
    /// entity.transform.localScale을 기준 스케일의 배율로 조정하므로, 좌우 반전을 위해
    /// 음수 X 스케일을 쓰는 경우에도 부호가 그대로 유지됩니다.
    /// </summary>
    private IEnumerator ScaleCoroutine(BaseEntity entity, float squashDuration, float squashScale, float stretchDuration, float stretchScale)
    {
        Vector3 baseScale = entity.transform.localScale;

        float t = 0f;
        while (t < squashDuration)
        {
            t += Time.deltaTime;
            float s = Mathf.Lerp(1f, squashScale, Mathf.Clamp01(t / squashDuration));
            entity.transform.localScale = baseScale * s;
            yield return null;
        }

        entity.transform.localScale = baseScale * stretchScale;

        t = 0f;
        while (t < stretchDuration)
        {
            t += Time.deltaTime;
            float s = Mathf.Lerp(stretchScale, 1f, Mathf.Clamp01(t / stretchDuration));
            entity.transform.localScale = baseScale * s;
            yield return null;
        }

        entity.transform.localScale = baseScale;
    }

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

        switch (pattern)
        {
            case 0:
                yield return Pattern1_PillarCharge(entity);
                break;
            case 1:
                yield return Pattern2_GravityDonut(entity);
                break;
            default:
                yield return Pattern3_GroundSlam(entity);
                break;
        }

        ClearLabel();
        entity.IsAttacking = false;
        entity.ResetAnimationState();
        _isBusy = false;

        // 이제부터 다시 기본 공격 페이즈이므로, 8초 카운트를 여기서부터 새로 시작합니다.
        _scheduler.ResetBasicPhase(Time.time);
    }

    // ==============================================================
    // v1.4 D0 - 2페이즈 전환 하울링 (HP 임계치 최초 1회, 패턴3 충격파 링 로직 재사용)
    // ==============================================================
    private IEnumerator RunPhase2Transition(BaseEntity entity)
    {
        _isBusy = true;
        entity.IsAttacking = true;
        entity.CurrentState = AIState.Attack;

        StopNavAgent(entity);

        bool wasInvincible = false;
        if (howlBossInvincible && entity.Stats != null && entity.Stats.Health != null)
        {
            wasInvincible = entity.Stats.Health.Invincible;
            entity.Stats.Health.Invincible = true;
        }

        ShowLabel(label_Howl);

        // "지금부터 다르다"를 확실히 알리는 충격 연출
        if (CameraManager.Instance != null) CameraManager.Instance.HitShakeCamera(3f);
        if (HitStopManager.Instance != null) HitStopManager.Instance.DoHitStop(0.12f);

        // D2 흡수: 살아있는 기둥 전부 즉시 균열 상태로 전환 (기존 CollapseInstantly 재사용)
        foreach (var p in _pillarField.All)
        {
            if (p != null && p.IsAlive) p.CollapseInstantly();
        }

        Vector2 center = entity.transform.position;
        RoomMetrics room = GetRoomMetrics(entity);
        float maxRadius = room.found ? Mathf.Max(room.halfX, room.halfY) * 1.15f : slamWaveFallbackMaxRadius;

        yield return RunHowlRing(entity, center, maxRadius);

        if (howlBossInvincible && entity.Stats != null && entity.Stats.Health != null)
        {
            entity.Stats.Health.Invincible = wasInvincible;
        }

        // D1: 전투 끝까지 유지되는 속도 증가 적용 (이동속도는 CharacterStatus의 지속 버프 시스템 재사용,
        // 시전/8초 주기는 ScaleDuration()이 이후 _phase2Triggered를 보고 자동으로 단축시킵니다)
        if (entity.Stats != null && entity.Stats.Status != null)
        {
            entity.Stats.Status.ApplySpeedBuff("Phase2Howl", phase2SpeedIncreasePercent, 99999f);
        }

        ClearLabel();
        entity.IsAttacking = false;
        entity.ResetAnimationState();
        _isBusy = false;

        _scheduler.ResetBasicPhase(Time.time);
    }

    /// <summary>
    /// 패턴3의 RunShockwaveRing()과 동일한 확장 링 로직을 재사용하되, 데미지 대신 방사형 넉백 + 경직 +
    /// (플레이어라면) 카메라 흔들림을 부여합니다. 대쉬 무적 중이면 다른 패턴들과 동일하게 완전히 회피됩니다.
    /// </summary>
    private IEnumerator RunHowlRing(BaseEntity entity, Vector2 center, float maxRadius)
    {
        GameObject ring = CreateFallbackRing(howlRingColor);
        ring.transform.position = center;

        LayerMask targetLayer = LayerMask.GetMask("Player", "Player_Dash", "Army"); // "Ally"는 프로젝트에 없는 레이어라 제거(런타임 동일)

        // 공용 확장 링 판정으로 통일. add-before-guard 순서(밴드 진입 순간 소비 → 대쉬로 흘리면 재히트 없음)는 ExpandingRing이 보존.
        yield return BossCombat.ExpandingRing(center, maxRadius, howlRingExpandTime, howlRingThickness, targetLayer,
            onHit: (hit, pushDir) =>
            {
                CharacterHealth hHealth = hit.GetComponentInChildren<CharacterHealth>() ?? hit.GetComponentInParent<CharacterHealth>();
                bool isDashingLayer = hit.gameObject.layer == LayerMask.NameToLayer("Player_Dash");
                if (hHealth == null || hHealth.IsDead || hHealth.Invincible || isDashingLayer) return; // 대쉬 무적으로 완전 회피

                CharacterStatus status = hit.GetComponentInChildren<CharacterStatus>() ?? hit.GetComponentInParent<CharacterStatus>();
                if (status != null)
                {
                    status.ApplyKnockback(pushDir, howlKnockbackForce, howlKnockbackDuration);
                    status.SetDebuffBool(DebuffBoolType.Hitstunned, howlHitstunDuration);
                }

                if (hit.gameObject.layer == LayerMask.NameToLayer("Player") && CameraManager.Instance != null)
                    CameraManager.Instance.HitShakeCamera(howlCameraShakeForce);
            },
            onExpand: (cur) =>
            {
                if (ring != null) ring.transform.localScale = new Vector3(cur * 2f, cur * 2f, 1f);
            });

        if (ring != null) GameObject.Destroy(ring);
    }

    // --- 패턴 1: 기둥과 돌진 (3단 돌진 체계 ③ 강한 돌진, 스펙 변경 없음) ---
    private IEnumerator Pattern1_PillarCharge(BaseEntity entity)
    {
        ShowLabel(label_Pattern1Windup);

        RoomMetrics room = GetRoomMetrics(entity);

        float t = 0f;
        Vector2 chargeDir = GetAimDir(entity);
        GameObject telegraph = null;
        float scaledChargeWindup = ScaleDuration(chargeWindup); // v1.4 D1

        // 3초 조준: 플레이어 방향을 실시간으로 주시하며, 바닥에 돌진 경로를 빨간 직사각형으로 표시합니다.
        // 전조는 항상 "플레이어 발밑"까지 확실히 이어지도록 대상과의 거리 기준으로 길이를 계산합니다.
        while (t < scaledChargeWindup)
        {
            t += Time.deltaTime;
            if (entity.Target != null)
            {
                chargeDir = GetAimDir(entity);
                entity.LookAtTarget(entity.Target);
            }

            float length = chargeTelegraphLength;
            if (entity.Target != null)
            {
                length = Vector2.Distance(entity.transform.position, entity.Target.position) + chargeTelegraphOvershoot;
            }
            if (room.found)
            {
                float exitDist = GetBoundsExitDistance(room.bounds, entity.transform.position, chargeDir);
                if (exitDist > 0f) length = Mathf.Min(length, exitDist);
            }
            length = Mathf.Min(length, chargeTelegraphLength);

            if (telegraph == null)
            {
                telegraph = CreateFallbackRect(chargeTelegraphColor);
            }
            UpdateChargeTelegraph(telegraph, entity.transform.position, chargeDir, length, chargeHitRadius * 2f);

            yield return null;
        }

        if (telegraph != null) GameObject.Destroy(telegraph);

        ShowLabel(label_Pattern1);

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
        float maxDuration = 3f;
        float elapsed = 0f;

        LayerMask playerMask = LayerMask.GetMask("Player", "Player_Dash");
        LayerMask wallMask = LayerMask.GetMask("Wall", "Object");
        LayerMask hitMask = playerMask | wallMask;

        bool hitSomething = false;
        EliteMonsterPillar hitPillar = null;

        while (elapsed < maxDuration)
        {
            elapsed += Time.deltaTime;
            if (rb != null) rb.linearVelocity = chargeDir * chargeSpeed;

            float checkDist = chargeSpeed * Time.deltaTime + 0.2f;

            // 기둥/벽이 플레이어보다 먼저 걸리도록, "Object"(기둥) + "Wall" 레이어를 우선 검사합니다.
            // 기둥 뒤에 숨은 플레이어가 기둥보다 먼저 맞는 일이 없도록 플레이어 판정은 별도로 나중에 검사합니다.
            RaycastHit2D obstacleHit = Physics2D.CircleCast(entity.transform.position, chargeHitRadius, chargeDir, checkDist, wallMask);

            bool outOfBounds = roomBounds.HasValue && !roomBounds.Value.Contains(entity.transform.position);

            if (obstacleHit.collider != null || outOfBounds)
            {
                hitSomething = true;
                if (obstacleHit.collider != null)
                {
                    hitPillar = obstacleHit.collider.GetComponentInParent<EliteMonsterPillar>();
                }

                if (rb != null) rb.linearVelocity = -chargeDir * 3f;
                entity.transform.position = (Vector2)entity.transform.position - chargeDir * 0.15f;
                break;
            }

            // 기둥/벽에 막히지 않았을 때만 플레이어 직격 여부를 검사합니다.
            RaycastHit2D playerHit = Physics2D.CircleCast(entity.transform.position, chargeHitRadius, chargeDir, checkDist, playerMask);
            // 대쉬(Player_Dash) 중인 플레이어는 관통(phase-through): 정지/반동/기절/데미지 전부 없이 돌진 지속.
            if (playerHit.collider != null && playerHit.collider.gameObject.layer != LayerMask.NameToLayer("Player_Dash"))
            {
                hitSomething = true;

                BossCombat.TryDamage(playerHit.collider, new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));

                if (rb != null) rb.linearVelocity = -chargeDir * 3f;
                entity.transform.position = (Vector2)entity.transform.position - chargeDir * 0.15f;
                break;
            }

            yield return null;
        }

        if (rb != null) rb.linearVelocity = Vector2.zero;

        if (wasAgentEnabled && agent != null)
        {
            if (NavMesh.SamplePosition(entity.transform.position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                entity.transform.position = navHit.position;
            }
            agent.enabled = true;
            agent.isStopped = false;
        }

        if (hitPillar != null && hitPillar.IsAlive)
        {
            // 기둥에 명중: 체력과 무관하게 즉시 붕괴 (기획서대로 이 순간에는 무피해) + 보스 5초 기절.
            // 안전지대 폭발 피해는 2초 뒤(EliteMonsterPillar.CollapseRoutine)에만 발생합니다.
            hitPillar.CollapseInstantly();
            if (entity.Stats != null && entity.Stats.Status != null)
            {
                entity.Stats.Status.SetDebuffBool(DebuffBoolType.Stunned, pillarChargeStunDuration);
            }
        }
        else if (hitSomething)
        {
            // 벽 혹은 기둥이 없는 상태에서의 충돌: 짧은 기절만 부여
            if (entity.Stats != null && entity.Stats.Status != null)
            {
                entity.Stats.Status.SetDebuffBool(DebuffBoolType.Stunned, wallChargeStunDuration);
            }
        }
    }

    /// <summary>
    /// 돌진 경로를 나타내는 빨간 직사각형 전조를 생성/갱신합니다. (보스 위치 기준 전방으로 length만큼)
    /// </summary>
    private void UpdateChargeTelegraph(GameObject telegraph, Vector2 originPos, Vector2 dir, float length, float width)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 mid = originPos + dir * (length * 0.5f);
        telegraph.transform.position = mid;
        telegraph.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        telegraph.transform.localScale = new Vector3(length, width, 1f);
    }

    // --- 패턴 2: 중력 도넛 폭발 (v1.3, 기존 안/팎 도넛 완전 대체) ---
    private IEnumerator Pattern2_GravityDonut(BaseEntity entity)
    {
        ShowLabel(label_Pattern2);

        StopNavAgent(entity);

        Vector2 center = entity.transform.position;

        RoomMetrics room = GetRoomMetrics(entity);
        float fieldRadius = room.found
            ? Mathf.Sqrt(room.halfX * room.halfX + room.halfY * room.halfY) * gravityFieldRadiusRatio
            : gravityFieldFallbackRadius;

        GameObject telegraph = SpawnGravityTelegraph(center, fieldRadius);

        LayerMask targetLayer = LayerMask.GetMask("Player", "Army"); // "Ally"는 프로젝트에 없는 레이어라 제거(런타임 동일)

        // v1.3: 매 프레임 연속으로 끄는 대신, tickInterval초마다 한 번씩 tickDistance만큼만 순간적으로
        // 끌어당깁니다. 틱과 틱 사이에는 플레이어가 완전히 자유롭게 움직일 수 있어, 기획 의도대로
        // "6초 동안 기둥 뒤로 도망갈 시간"이 실제로 주어집니다.
        float t = 0f;
        float nextTickTime = gravityPullTickInterval;
        while (t < gravityPullDuration)
        {
            t += Time.deltaTime;

            if (telegraph != null)
            {
                var sr = telegraph.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    // 펄스 연출: 시간에 따라 투명도가 진동하며 끌어당기는 중임을 표현합니다.
                    float pulse = gravityFieldColor.a + 0.15f * Mathf.Sin(t * 6f);
                    Color c = gravityFieldColor;
                    c.a = Mathf.Clamp01(pulse);
                    sr.color = c;
                }
            }

            if (t >= nextTickTime)
            {
                nextTickTime += gravityPullTickInterval;

                Collider2D[] hits = Physics2D.OverlapCircleAll(center, fieldRadius, targetLayer);
                foreach (var hit in hits)
                {
                    // 살아있는 기둥 뒤에 숨은 대상은 이 틱에서 끌려가지 않습니다. (유일한 회피 수단)
                    if (FindShelteringPillar(hit.transform.position) != null)
                    {
                        continue;
                    }

                    Vector2 currentPos = hit.transform.position;
                    Vector2 pulled = Vector2.MoveTowards(currentPos, center, gravityPullTickDistance);

                    Rigidbody2D rb2 = hit.attachedRigidbody;
                    if (rb2 != null)
                    {
                        rb2.MovePosition(pulled);
                    }
                    else
                    {
                        hit.transform.position = pulled;
                    }
                }
            }

            yield return null;
        }

        if (telegraph != null)
        {
            GameObject.Destroy(telegraph);
        }

        // 폭발: 기둥 뒤에 숨어있지 않은 대상은 직접 피해를, 숨어있던 대상은 무피해 대신
        // 그 기둥이 내구도 피해를 입습니다. 이 패턴은 그로기를 전혀 부여하지 않습니다.
        Collider2D[] finalHits = Physics2D.OverlapCircleAll(center, fieldRadius, targetLayer);
        foreach (var hit in finalHits)
        {
            EliteMonsterPillar shelterPillar = FindShelteringPillar(hit.transform.position);
            if (shelterPillar != null)
            {
                shelterPillar.DamagePattern(gravityPillarDamage);
                continue;
            }

            // 단일 데미지 경로: 무적/사망/대쉬(Player_Dash) 회피 판정을 BossCombat 한 곳에서 처리.
            // (중력장 마스크엔 Player_Dash가 없어 대쉬는 애초에 안 잡히지만, TryDamage가 방어적으로도 걸러냄.)
            BossCombat.TryDamage(hit, new DamageInfo(gravityExplosionDamage, DamageType.Physical, entity.gameObject));
        }
    }

    /// <summary>
    /// 중력장 범위를 표시하는 원형 전조입니다. 시전 중 계속 유지되며, 펄스 연출로 투명도가 진동합니다.
    /// </summary>
    private GameObject SpawnGravityTelegraph(Vector2 center, float radius)
    {
        GameObject obj = new GameObject("Elite_Telegraph_Gravity");
        obj.transform.position = center;
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateCircleSprite();
        sr.color = gravityFieldColor;
        sr.sortingOrder = 9;
        obj.transform.localScale = Vector3.one * (radius * 2f);
        return obj;
    }

    // --- 패턴 3: 바닥 충격파 (보스를 중심으로 퍼져나가는 얇은 고리형 파동, 대쉬로 회피 가능) ---
    private IEnumerator Pattern3_GroundSlam(BaseEntity entity)
    {
        ShowLabel(label_Pattern3Windup);
        StopNavAgent(entity);

        Vector2 preCenter = entity.transform.position;

        // 애니메이션이 없는 것을 보완하는 사전 예비동작: 발밑에 경고 원이 서서히 채워집니다. (이 동안은 무피해)
        GameObject warmup = CreateFallbackCircle(preCenter, 0.4f, new Color(1f, 0.4f, 0f, 0.15f));
        float wt = 0f;
        float scaledPreCastDelay = ScaleDuration(slamPreCastDelay); // v1.4 D1
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

        // 보스 근처 밀착 시 확정 피해 (꼼수 방지) - 대쉬 무적, 혹은 기둥 뒤에 숨었으면 회피됩니다.
        LayerMask playerLayers = LayerMask.GetMask("Player", "Player_Dash");
        Collider2D[] meleeHits = Physics2D.OverlapCircleAll(center, slamMeleeRadius, playerLayers);
        foreach (var hit in meleeHits)
        {
            CharacterHealth pHealth = hit.GetComponentInChildren<CharacterHealth>();
            if (pHealth == null) pHealth = hit.GetComponentInParent<CharacterHealth>();
            bool isDashingLayer = hit.gameObject.layer == LayerMask.NameToLayer("Player_Dash");
            if (pHealth == null || pHealth.IsDead || pHealth.Invincible || isDashingLayer) continue; // LShift 대쉬(무적/레이어 전환)로 회피 가능

            EliteMonsterPillar shelterPillar = FindShelteringPillar(hit.transform.position);
            if (shelterPillar != null)
            {
                shelterPillar.DamagePattern(slamPillarDamagePerWave);
                continue;
            }

            // 위에서 이미 dash/무적/사망을 걸렀지만, 최종 데미지 전달은 BossCombat 단일 경로로 통일.
            BossCombat.TryDamage(hit, new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));
        }

        for (int wave = 0; wave < slamWaveCount; wave++)
        {
            yield return RunShockwaveRing(entity, center, maxRadius);

            if (wave < slamWaveCount - 1)
            {
                yield return new WaitForSeconds(slamWaveInterval);
            }
        }
    }

    /// <summary>
    /// 보스 중심에서 maxRadius까지 퍼져나가는 얇은 고리형 충격파 1회를 재생합니다.
    /// 고리가 실제로 지나가는 순간에만 피해 판정을 하며, 그 순간 대쉬 무적 상태면 회피됩니다.
    /// 기둥 뒤에 숨어서 회피한 경우, 플레이어는 무피해 대신 그 기둥이 내구도 피해를 입습니다.
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

                EliteMonsterPillar shelterPillar = FindShelteringPillar(hit.transform.position);
                if (shelterPillar != null)
                {
                    shelterPillar.DamagePattern(slamPillarDamagePerWave); // 기둥 뒤 = 무피해, 기둥 내구도 소모
                    return;
                }

                BossCombat.TryDamage(hit, new DamageInfo(slamWaveDamage, DamageType.Physical, entity.gameObject));
            },
            onExpand: (cur) =>
            {
                if (ring != null) ring.transform.localScale = new Vector3(cur * 2f, cur * 2f, 1f);
            });

        if (ring != null) GameObject.Destroy(ring);
    }

    // 숨을 수 있는(Active|Cracking) 기둥 뒤에 있으면 그 기둥을 반환. 순회/판정은 PillarField 로 위임.
    // (IsSheltering 내부에서 ProvidesShelter 를 검사하므로 IsAlive 게이트는 걸지 않는다 — 균열 상태 뒤도 회피 인정)
    private EliteMonsterPillar FindShelteringPillar(Vector2 worldPos)
    {
        return _pillarField.FindSheltering(worldPos);
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
