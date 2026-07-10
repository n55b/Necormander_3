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

    // ==============================================================
    // 특수 패턴 공통 설정
    // ==============================================================
    [Header("특수 패턴 공통 설정 (기본 공격 8초 -> 패턴 1회 -> 기본 공격 8초 -> ...)")]
    [Tooltip("기본 공격 상태로 돌아온 뒤 이 시간(초)이 지나면, 하던 행동을 강제로 중단하고 패턴을 발동합니다.")]
    public float specialPatternInterval = 8f;
    [Tooltip("특수 패턴 장판(경고) 프리팹. 비워두면 원형 히트박스로 대체합니다.")]
    public GameObject fieldTelegraphPrefab;

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
    // 런타임 상태 (ScriptableObject 공유 인스턴스 기준 - 다른 보스 패턴들과 동일한 구조)
    // ==============================================================
    private bool _isBusy = false;
    private bool _pillarsSpawned = false;
    private bool _superArmorApplied = false;
    private List<EliteMonsterPillar> _pillars = new List<EliteMonsterPillar>();
    private List<int> _specialPool = new List<int>();
    private int _lastBasicAttack = -1;
    private EliteBossPatternLabel _label;
    private Coroutine _basicAttackCoroutine;

    // v1.2 추격 버스트 런타임 상태
    private Coroutine _pursuitBurstCoroutine;
    private bool _isBursting = false;
    private float _nextBurstTime;

    // 마지막으로 유효했던(0벡터가 아니었던) 조준 방향입니다. 목표와 완전히 겹치는 등 방향 계산이
    // 불가능한 순간에도, 임의의 고정 방향(예: 오른쪽) 대신 이 값을 사용해 "플레이어 반대 방향으로
    // 공격이 나가는" 것처럼 보이는 문제를 방지합니다.
    private Vector2 _lastAimDir = Vector2.down;

    // 기본 공격 상태로 (다시) 돌아온 절대 시각(Time.time). 이 시각으로부터 specialPatternInterval초가
    // 지나면 다음 패턴을 발동합니다. Execute()가 매 프레임 불리지 않아도(공격 중엔 아예 안 불림) 정확합니다.
    private float _basicPhaseStartTime;

    public override void Init(BaseEntity entity)
    {
        base.Init(entity);
        _isBusy = false;
        _pillarsSpawned = false;
        _superArmorApplied = false;
        _pillars.Clear();
        _specialPool.Clear();
        _lastBasicAttack = -1;
        _label = null;
        _basicAttackCoroutine = null;
        _pursuitBurstCoroutine = null;
        _isBursting = false;
        _nextBurstTime = Time.time + Random.Range(pursuitBurstMinInterval, pursuitBurstMaxInterval);
        _lastAimDir = Vector2.down;
        _basicPhaseStartTime = Time.time;
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

        // 참고: 엔진(BaseEntity.Update -> CanExecuteAI)이 IsAttacking == true인 동안에는 이 함수 자체를
        // 호출하지 않으므로, 아래 인터럽트 분기는 사실상 "공격과 공격 사이의 짧은 순간"에만 유효합니다.
        // 그래도 8초 판정 자체는 Time.time 절대시각 비교라 정확합니다.
        if (!_isBusy)
        {
            entity.LookAtTarget(entity.Target);

            if (Time.time - _basicPhaseStartTime >= specialPatternInterval)
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

                int pattern = DrawFromSpecialPool();
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

            GameObject pillarObj;
            if (pillarPrefab != null)
            {
                pillarObj = GameObject.Instantiate(pillarPrefab, targetPos, Quaternion.identity);
            }
            else
            {
                pillarObj = CreateFallbackPillar(targetPos);
            }

            EliteMonsterPillar pillar = pillarObj.GetComponent<EliteMonsterPillar>();
            if (pillar == null) pillar = pillarObj.AddComponent<EliteMonsterPillar>();
            pillar.SetMaxHP(pillarMaxHP);
            pillar.Owner = entity.gameObject;

            _pillars.Add(pillar);
        }
    }

    private GameObject CreateFallbackPillar(Vector2 pos)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        obj.name = "EliteMonster_Pillar";
        obj.transform.position = pos;
        obj.transform.localScale = new Vector3(1f, 1.4f, 1f);

        var col = obj.AddComponent<CircleCollider2D>();
        col.radius = 0.55f;

        // "Obstacle" 레이어는 이 프로젝트에 존재하지 않으므로, 실제 존재하며 Player/Enemy와
        // 충돌하도록 설정되어 있는 "Object" 레이어를 사용합니다.
        int objectLayer = LayerMask.NameToLayer("Object");
        if (objectLayer >= 0) obj.layer = objectLayer;

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(0.55f, 0.4f, 0.25f);

        return obj;
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
    private int PickBasicAttack()
    {
        int next;
        do { next = Random.Range(0, 3); } while (next == _lastBasicAttack);
        _lastBasicAttack = next;
        return next;
    }

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

        int atkIndex = PickBasicAttack(); // 0: 미니 돌진 찍기, 1: 일반 돌진, 2: 휩쓸기

        float windup = atkIndex == 0 ? stabWindup : atkIndex == 1 ? normalChargeWindup : sweepWindup;
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

        // v1.3 수정: 대시 경로가 아니라, 최종 전방 원형 판정(spawnPos, radius) 안에 있는 기둥에게만 내구도 피해를 줍니다.
        Collider2D[] pillarHits = Physics2D.OverlapCircleAll(spawnPos, radius, LayerMask.GetMask("Object"));
        foreach (var pillarHit in pillarHits)
        {
            EliteMonsterPillar hitPillar = pillarHit.GetComponentInParent<EliteMonsterPillar>();
            if (hitPillar != null && hitPillar.IsAlive)
            {
                hitPillar.DamagePattern(miniChargePillarDamage);
            }
        }

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
            if (playerHit.collider != null)
            {
                CharacterHealth pHealth = playerHit.collider.GetComponentInChildren<CharacterHealth>();
                if (pHealth == null) pHealth = playerHit.collider.GetComponentInParent<CharacterHealth>();
                if (pHealth != null && !pHealth.Invincible)
                {
                    pHealth.GetDamage(new DamageInfo(entity.Stats.ATK * normalChargeDamageMultiplier, DamageType.Physical, entity.gameObject));
                }
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
    private int DrawFromSpecialPool()
    {
        if (_specialPool.Count == 0)
        {
            _specialPool = new List<int> { 0, 1, 2 };
        }

        int idx = Random.Range(0, _specialPool.Count);
        int picked = _specialPool[idx];
        _specialPool.RemoveAt(idx);
        return picked;
    }

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
        _basicPhaseStartTime = Time.time;
    }

    // --- 패턴 1: 기둥과 돌진 (3단 돌진 체계 ③ 강한 돌진, 스펙 변경 없음) ---
    private IEnumerator Pattern1_PillarCharge(BaseEntity entity)
    {
        ShowLabel(label_Pattern1Windup);

        RoomMetrics room = GetRoomMetrics(entity);

        float t = 0f;
        Vector2 chargeDir = GetAimDir(entity);
        GameObject telegraph = null;

        // 3초 조준: 플레이어 방향을 실시간으로 주시하며, 바닥에 돌진 경로를 빨간 직사각형으로 표시합니다.
        // 전조는 항상 "플레이어 발밑"까지 확실히 이어지도록 대상과의 거리 기준으로 길이를 계산합니다.
        while (t < chargeWindup)
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
            if (playerHit.collider != null)
            {
                hitSomething = true;

                CharacterHealth pHealth = playerHit.collider.GetComponentInChildren<CharacterHealth>();
                if (pHealth == null) pHealth = playerHit.collider.GetComponentInParent<CharacterHealth>();
                if (pHealth != null && !pHealth.Invincible)
                {
                    pHealth.GetDamage(new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));
                }

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

        LayerMask targetLayer = LayerMask.GetMask("Player", "Army", "Ally");

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

            CharacterHealth pHealth = hit.GetComponentInChildren<CharacterHealth>();
            if (pHealth == null)
            {
                pHealth = hit.GetComponentInParent<CharacterHealth>();
            }
            if (pHealth == null || pHealth.IsDead || pHealth.Invincible)
            {
                continue;
            }

            pHealth.GetDamage(new DamageInfo(gravityExplosionDamage, DamageType.Physical, entity.gameObject));
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
        while (wt < slamPreCastDelay)
        {
            wt += Time.deltaTime;
            float scale = Mathf.Lerp(0.4f, slamMeleeRadius * 2f, wt / slamPreCastDelay);
            if (warmup != null)
            {
                warmup.transform.position = entity.transform.position;
                warmup.transform.localScale = Vector3.one * scale;
                var wsr = warmup.GetComponent<SpriteRenderer>();
                if (wsr != null) wsr.color = new Color(1f, 0.4f, 0f, Mathf.Lerp(0.15f, 0.4f, wt / slamPreCastDelay));
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

            pHealth.GetDamage(new DamageInfo(entity.Stats.ATK, DamageType.Physical, entity.gameObject));
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

        HashSet<GameObject> alreadyChecked = new HashSet<GameObject>();
        LayerMask targetLayer = LayerMask.GetMask("Player", "Player_Dash");

        float t = 0f;
        while (t < slamWaveExpandTime)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / slamWaveExpandTime);
            float currentRadius = Mathf.Lerp(0f, maxRadius, progress);

            float diameter = currentRadius * 2f;
            ring.transform.localScale = new Vector3(diameter, diameter, 1f);

            Collider2D[] hits = Physics2D.OverlapCircleAll(center, currentRadius + slamRingThickness, targetLayer);
            foreach (var hit in hits)
            {
                if (alreadyChecked.Contains(hit.gameObject)) continue;

                float d = Vector2.Distance(center, hit.transform.position);
                bool inRing = d >= currentRadius - slamRingThickness && d <= currentRadius + slamRingThickness;
                if (!inRing) continue;

                alreadyChecked.Add(hit.gameObject);

                CharacterHealth pHealth = hit.GetComponentInChildren<CharacterHealth>();
                if (pHealth == null) pHealth = hit.GetComponentInParent<CharacterHealth>();
                bool isDashingLayer = hit.gameObject.layer == LayerMask.NameToLayer("Player_Dash");
                if (pHealth == null || pHealth.IsDead || pHealth.Invincible || isDashingLayer) continue; // LShift 대쉬(무적/레이어 전환)로 회피 가능

                EliteMonsterPillar shelterPillar = FindShelteringPillar(hit.transform.position);
                if (shelterPillar != null)
                {
                    // 기둥 뒤에 숨었다면 플레이어는 무피해, 대신 기둥이 내구도 피해를 입음
                    shelterPillar.DamagePattern(slamPillarDamagePerWave);
                    continue;
                }

                pHealth.GetDamage(new DamageInfo(slamWaveDamage, DamageType.Physical, entity.gameObject));
            }

            yield return null;
        }

        if (ring != null) GameObject.Destroy(ring);
    }

    private EliteMonsterPillar FindShelteringPillar(Vector2 worldPos)
    {
        for (int i = 0; i < _pillars.Count; i++)
        {
            EliteMonsterPillar p = _pillars[i];
            if (p != null && p.IsAlive && p.IsSheltering(worldPos)) return p;
        }
        return null;
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

    private static Sprite _cachedCircleSprite;
    private static Sprite GetOrCreateCircleSprite()
    {
        if (_cachedCircleSprite != null) return _cachedCircleSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float r = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                tex.SetPixel(x, y, dist <= r ? Color.white : new Color(1, 1, 1, 0));
            }
        }
        tex.Apply();

        _cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedCircleSprite;
    }

    private static Sprite _cachedSquareSprite;
    private static Sprite GetOrCreateSquareSprite()
    {
        if (_cachedSquareSprite != null) return _cachedSquareSprite;

        int size = 8;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, Color.white);
            }
        }
        tex.Apply();

        _cachedSquareSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedSquareSprite;
    }

    private static Sprite _cachedRingSprite;
    private static Sprite GetOrCreateRingSprite()
    {
        if (_cachedRingSprite != null) return _cachedRingSprite;

        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerR = size / 2f;
        // 반경 대비 12%만 고리 두께로 사용 (실제 판정 두께와 시각적으로 더 잘 맞도록)
        float innerR = outerR * 0.88f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                bool inRing = dist <= outerR && dist >= innerR;
                tex.SetPixel(x, y, inRing ? Color.white : new Color(1, 1, 1, 0));
            }
        }
        tex.Apply();

        _cachedRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedRingSprite;
    }
}
