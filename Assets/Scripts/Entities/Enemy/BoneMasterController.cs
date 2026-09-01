using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 본 마스터 보스 전용 컨트롤러.
/// - 부위(투구/견갑/흉갑) 파괴에 따른 받는피해 증가 + 페이즈2 진입을 관리한다.
/// - 카운터 게이지(BossCounterGauge), 머리 위 상태 텍스트(EliteBossPatternLabel)를 배선한다.
/// - 방 경계은 보스를 따라다니지 않고, 보스가 스폰된 RoomInstance 중심에 고정되며
///   방의 실제 가로/세로 비율(roomSize)에 맞춰 타원으로 그려진다.
/// - Charger Elite와 동일하게 상시 슈퍼아머를 부여해 플레이어 평타에 경직/넉백되지 않는다.
///
/// [버그 수정] NavMeshAgent 위치는 항상 WarpTo()로 옮긴다(직접 대입 시 에이전트 내부 상태와 어긋나
/// 나중에 "튕기는" 문제가 있었다). WarpTo()는 이제 대상 지점이 NavMesh 위가 아니면(예: 장식 바위 등
/// 걷기 불가능한 지형 근처) 가장 가까운 유효한 지점으로 보정해서 워프한다 — 안 그러면 Warp가 조용히
/// 실패하면서 보스가 그 자리에 "낑겨서" 아무것도 못 하는 것처럼 멈추는 문제가 있었다.
///
/// [버그 수정 — 페이즈 전환 시 이전 패턴이 안 끊기던 문제] 흉갑 파괴 즉시 StopAllCoroutines()로
/// 강제 정리한 뒤 페이즈2 전환을 시작한다.
///
/// [개선] 페이즈2 진입 시 체력이 새 최대치까지 서서히 차오르는 연출을 추가했다.
///
/// [버그 수정 — 흰 사각형] 아레나 테스트 모드에서 실제로 이어지는 방이 없는 "문 자리" 마커가
/// 기본 흰색 Square 스프라이트로 노출되는 문제를 막는다.
/// </summary>
public class BoneMasterController : EnemyController
{
    [Header("부위 파괴 설정")]
    [SerializeField] private float[] partBreakHpRatios = { 0.8f, 0.6f, 0.4f };
    [SerializeField] private float perPartIncomingDamageBonus = 0.15f;
    [SerializeField] private float baseArmorReduction = 0.2f;

    // [0830 수정안] 부위 파괴로 보스가 얻던 이로운 효과 3종은 은퇴했다 — 남는 건 '받는 피해 증가'뿐.
    //
    // 필드와 소비 수식(rangeMul / csMul)을 지우지 않고 기본값만 0으로 죽인 이유:
    // csMul 은 예고 시간 · 애니 배속 · 인디케이터 duration · 카운터 창을 한 변수로 묶고 있어서
    // (그래야 게이지가 가득 차는 순간과 판정이 어긋나지 않는다), 걷어내면 그 배선을 전부 다시
    // 짜야 한다. 게다가 '집행'의 시전 속도 40% 가속을 물릴 자리가 정확히 이 수식들이다.
    // 0 이면 rangeMul = csMul = 1 이라 모든 수식이 항등이 된다. 되살리지 마라.
    [SerializeField] private float helmetBreakRangeBonus = 0f;
    [SerializeField] private float pauldronBreakCastSpeedBonus = 0f;
    [SerializeField] private float chestBreakMoveSpeedBonus = 0f;
    [SerializeField] private float partBreakTextDuration = 1.5f;

    [Header("슈퍼아머")]
    public float superArmorGauge = 999999f;

    [Header("뼈 갑옷 시각 표현")]
    [Tooltip("부위 파괴 수(0/1/2/3)에 따른 슈퍼아머 아웃라인 색. 슈퍼아머는 끝까지 유지되므로 " +
             "아웃라인을 끄지 않고 색만 짙어지게 한다 — 끄면 '이제 경직이 들어간다'는 거짓 신호가 된다.")]
    [SerializeField] private Color[] partBreakOutlineColors =
    {
        new Color(1f, 0.85f, 0.25f, 1f),  // 0 파괴 — 노랑(온전한 뼈 갑옷)
        new Color(1f, 0.55f, 0.15f, 1f),  // 1 파괴 — 주황(투구)
        new Color(1f, 0.30f, 0.12f, 1f),  // 2 파괴 — 빨강(견갑)
        new Color(0.75f, 0.10f, 0.10f, 1f) // 3 파괴 — 진빨강(흉갑, 페이즈2)
    };

    [Header("애니메이션")]
    [Tooltip("페이즈2 전환 연출 동안 재생할 스테이트. 전용 모션이 아직 없어서 Stun 을 홀드한다.")]
    [SerializeField] private string phase2TransitionState = "Stun";

    [Header("페이즈 전환")]
    public EnemyMinionDataSO phase2Data;
    [SerializeField] private float phase2HealFillDuration = 1f;
    [Tooltip("페이즈2 시작 시 방 중앙으로 이동 후 무적 상태로 대기하는 시간(초). 이 동안 어떤 패턴도 시전하지 않는다.")]
    [SerializeField] private float phase2InvincibleDuration = 2f;

    [Header("UI 참조")]
    [SerializeField] private EliteBossPatternLabel patternLabel;
    [SerializeField] private Vector3 patternLabelOffset = new Vector3(0f, 1.6f, 0f);

    [Header("그로기(경직) 시각 피드백")]
    [SerializeField] private Color groggyFlashColor = new Color(0.4f, 0.85f, 1f, 1f);

    [Header("카운터 아웃라인 발광")]
    [Tooltip("스프라이트 외곽선을 따라 빛나게 할 아웃라인 머티리얼. " +
             "Assets/Material/Pixel_SuperArmor_Shader.mat 과 같은 PickUpOutline 계열 셰이더(_Color 노출)를 쓴다.\n" +
             "★ 비워두면 예전처럼 몸통 전체를 단색으로 칠하는 방식으로 자동 폴백한다(연출만 투박해지고 동작은 같다).")]
    [SerializeField] private Material counterOutlineMaterial;
    [Tooltip("아웃라인 머티리얼에서 색을 받는 셰이더 프로퍼티 이름. PickUpOutline 계열은 _Color 다.")]
    [SerializeField] private string counterOutlineColorProperty = "_Color";
    [Tooltip("아웃라인 발광 세기. 1보다 크면 HDR 범위로 올라가 블룸(Bloom)이 먹는다.")]
    [SerializeField] private float counterOutlineIntensity = 2.5f;
    [Tooltip("판정이 열리기 전 '유예' 구간에서 아웃라인이 깜빡이는 속도(초당 왕복 횟수). " +
             "깜빡이는 동안은 아직 판정이 없고, 깜빡임이 멈추고 꽉 차는 순간부터 판정이 시작된다.")]
    [SerializeField] private float counterOutlinePulseSpeed = 3f;
    [Tooltip("유예 구간 깜빡임의 최저 밝기(0~1). 0이면 완전히 꺼졌다 켜지고, 높을수록 은은하게 맥동한다.")]
    [Range(0f, 1f)]
    [SerializeField] private float counterOutlinePulseFloor = 0.25f;

    [Header("방 경계 (보스 이동 제한)")]
    [Tooltip("방 크기 대비 보스가 움직일 수 있는 비율.\n\n" +
             "roomSize 는 장식 벽 띠까지 포함한 값이라 그대로 쓰면 보스가 벽 안쪽으로 들어간다. " +
             "삭제된 뼈 투기장이 쓰던 값(0.92)을 그대로 물려받았으니, 이동 가능 범위를 " +
             "바꿀 생각이 아니면 건드리지 마라.")]
    [Range(0.5f, 1f)]
    [SerializeField] private float roomBoundsMarginRatio = 0.92f;


    public int PartsDestroyed { get; private set; } = 0;
    public int CurrentPhase { get; private set; } = 1;
    public bool IsGroggy { get; private set; } = false;

    public BossCounterGauge CounterGauge { get; private set; }
    public CharacterHealth Health => Stats != null ? Stats.Health : null;

    public float AttackRangeBonus { get; private set; } = 0f;
    public float PatternCastSpeedBonus { get; private set; } = 0f;

    private float _baseMoveSpeedCached = -1f;
    private SpriteRenderer[] _bodyRenderers;
    /// <summary>지금 '집행' 은신 중인가. StopActivePattern 이 되돌려야 할지 판단하는 데 쓴다.</summary>
    private bool _isHidden;
    private Color[] _bodyOriginalColors;

    private Coroutine _stateTextClearRoutine;
    private Coroutine _groggyFlashRoutine;
    private Coroutine _groggyClearRoutine;
    private Coroutine _activePattern;
    private BossOutlineGlow _outlineGlow;
    private NavMeshAgent _navAgent;
    private RoomInstance _cachedRoom;

    // ── 특수 패턴 코루틴 수명 관리 ────────────────────────────────────
    //
    // [버그 수정 — 죽은 보스가 1초 동안 계속 때리던 문제]
    // 브레인은 특수 패턴을 entity.StartCoroutine(...) 으로 돌리면서 반환된 Coroutine 핸들을
    // 어디에도 저장하지 않았다. BaseEntity.CancelAttack() 은 ActiveAttackCoroutine(=기본 공격)만
    // StopCoroutine 하므로 특수 패턴은 어떤 수단으로도 멈출 수 없었고, MonsterDeathHandler.Die() 가
    // CancelAttack() 만 부른 뒤 fallbackDelay(1초) 후에야 오브젝트를 파괴하기 때문에
    // 그 1초 동안 시체가 WarpTo 로 움직이며 BossCombat.DealLane 으로 실제 피해를 줬다
    // (BossCombat.TryDamage 는 대상의 생사만 보고 '공격자'의 생사는 검사하지 않는다).
    //
    // 여기로 핸들을 모아서, 사망·부위파괴·페이즈 전환 어느 경로로도 확실히 끊을 수 있게 한다.

    /// <summary>특수 패턴 코루틴을 컨트롤러가 추적하며 실행한다. 브레인은 이걸 통해서만 패턴을 돌린다.</summary>
    public void RunPattern(IEnumerator routine)
    {
        if (routine == null) return;
        StopActivePattern();
        _activePattern = StartCoroutine(TrackPattern(routine));
    }

    // inner 를 StartCoroutine 이 아니라 'yield return inner' 로 중첩하는 것이 핵심이다.
    // StartCoroutine 으로 돌리면 별개의 코루틴이 되어, 바깥을 StopCoroutine 해도 안쪽은 계속 산다.
    private IEnumerator TrackPattern(IEnumerator inner)
    {
        // finally 로 감싸야 패턴이 예외로 죽어도 핸들이 남지 않는다. 죽은 핸들이 남으면
        // 다음 CancelAttack 이 hadPattern 을 true 로 잘못 계산해 CurrentState 를 건드린다.
        // (C# 이터레이터에서 try/finally 는 허용된다 — BossCounterTelegraph.Run 도 같은 방식.)
        try
        {
            yield return inner;
        }
        finally
        {
            _activePattern = null;
        }
    }

    /// <summary>진행 중인 특수 패턴을 즉시 중단하고, 그 패턴이 남긴 전조·카운터 창을 정리한다.</summary>
    public void StopActivePattern()
    {
        if (_activePattern == null) return;

        StopCoroutine(_activePattern);
        _activePattern = null;

        // StopCoroutine 으로 끊긴 루틴은 자기 정리 코드를 실행하지 못한다.
        // 열어둔 카운터 창(= 안 닫으면 다음 패턴까지 파훼 판정이 새어 나감), 월드에 남은 전조,
        // 켜 둔 카운터 아웃라인을 대신 치운다.
        CounterGauge?.CloseWindow();
        CleanupDanglingTelegraphs();
        ClearCounterOutline();

        // 집행의 은신도 여기서 되돌린다. 루틴의 finally 에만 맡기면 안 된다 — 위 주석대로
        // StopCoroutine 으로 끊긴 이터레이터는 finally 를 실행하지 않으므로, 사망/페이즈 전환이
        // 은신 도중에 오면 투명 무적 보스와 화면에 붙박인 카운터 구슬이 그대로 남는다.
        if (_isHidden) SetHidden(false);
        BossCounterPipsUI.Hide();
    }

    /// <summary>
    /// 기본 공격만 끊던 것을 특수 패턴까지 끊도록 확장한다.
    /// 보스에게 이게 실제로 불리는 경로는 사망(MonsterDeathHandler.Die)과 페이즈 전환
    /// (BreakNextPart) 둘뿐이다 — 피격 경직/넉백 경로는 CharacterHealth 와 ApplyKnockback 이
    /// 모두 슈퍼아머(보스는 게이지 999999)에서 걸러내기 때문이다.
    ///
    /// 다만 실패했을 때의 대가가 크다: 패턴 코루틴이 CurrentState = Skill 을 걸어 둔 채로 끊기면
    /// AIPatternSO.Execute() 가 매 프레임 즉시 return 해서 보스가 영구히 얼어붙는다. 그래서
    /// 슈퍼아머가 어떤 이유로든 사라져 이 경로가 열리더라도 안전하도록 상태를 되돌려 놓는다.
    /// </summary>
    public override void CancelAttack()
    {
        bool hadPattern = _activePattern != null;

        base.CancelAttack();
        StopActivePattern();

        // ★ 죽었을 때는 절대 풀지 않는다.
        // MonsterDeathHandler.Die() 도 CancelAttack() 을 부르는데, 여기서 Skill 잠금을 풀어 버리면
        // 시체가 파괴되기까지의 fallbackDelay(프리팹 값 1초) 동안 브레인이 다시 돌면서
        // '새' 패턴과 기본 공격을 시작한다. 이 프리팹은 사망 시 스크립트를 하나도 비활성화하지 못하고
        // (behavioursToDisable 의 유일한 항목이 제거된 EnemyController 를 가리켜 null 이다),
        // BaseEntity.CanExecuteAI() 도 IsDead 를 보지 않기 때문에 막아 주는 게 아무것도 없다.
        // 잠금 해제는 '살아 있는데 패턴만 끊긴' 경우(슈퍼아머가 사라져 피격 경직이 들어오는 등)에만 필요하다.
        if (hadPattern && CurrentState == AIState.Skill && (Health == null || !Health.IsDead))
        {
            // Idle 로 두면 다음 프레임에 브레인이 UpdateStateTransitions 로 정상 재판단한다.
            // (페이즈 전환 경로에서는 곧바로 Phase2TransitionRoutine 이 다시 Skill 로 잠그므로 무해하다.)
            CurrentState = AIState.Idle;
        }
    }

    /// <summary>
    /// [버그 수정 — 죽은 보스가 계속 행동하던 문제]
    /// BaseEntity.CanExecuteAI() 는 enabled / IsAttacking / 행동불가 상태만 보고 <b>IsDead 는 안 본다</b>.
    /// 보스 프리팹은 사망 시 스크립트를 비활성화하지 못하고(MonsterDeathHandler.behavioursToDisable 의
    /// 유일한 항목이 이 프리팹에서 제거된 EnemyController 라 null 이다), 콜라이더를 꺼도 보스 피해는
    /// BossCombat 의 Physics2D.Overlap* 로 나가므로 소용이 없다. 결과적으로 사망 후 시체가 파괴되기까지
    /// 1초 동안 브레인이 계속 돌며 텔레그래프를 띄우고 실제 피해까지 줬다
    /// (BossCombat.TryDamage 는 대상의 생사만 검사하고 공격자의 생사는 보지 않는다).
    /// 브레인 자체를 여기서 끊는 것이 가장 근본적인 차단이다.
    /// </summary>
    protected override bool CanExecuteAI()
    {
        if (Health != null && Health.IsDead) return false;
        return base.CanExecuteAI();
    }

    protected override void Start()
    {
        base.Start();

        _navAgent = GetComponent<NavMeshAgent>();

        CounterGauge = GetComponentInChildren<BossCounterGauge>();
        if (CounterGauge == null)
        {
            Debug.LogWarning($"[BoneMaster] {gameObject.name}: BossCounterGauge 컴포넌트가 없습니다.");
        }
        else
        {
            // [파훼 가능 신호의 단일 소유자]
            // 카운터 창은 패턴마다 따로 열고 닫는다(돌진 예고 / 3연타 마지막 / P2 광역 2종 / 패턴3).
            // 그 12개 지점에서 아웃라인을 일일이 켜고 끄면 한 곳만 빠져도 발광이 켜진 채 남는다.
            // 게이지 상태가 곧 "지금 때리면 파훼된다"이므로, 게이지에 직접 물려서 어긋날 수 없게 한다.
            CounterGauge.OnGaugeChanged += HandleCounterGaugeChanged;
        }

        if (patternLabel == null) patternLabel = GetComponentInChildren<EliteBossPatternLabel>();
        if (patternLabel == null) patternLabel = CreatePatternLabel();

        _bodyRenderers = GetComponentsInChildren<SpriteRenderer>();
        _bodyOriginalColors = new Color[_bodyRenderers.Length];
        for (int i = 0; i < _bodyRenderers.Length; i++) _bodyOriginalColors[i] = _bodyRenderers[i].color;

        // 아웃라인 발광 오버레이. 머티리얼이 비어 있으면 IsUsable 이 false 로 남고,
        // 호출측(PulseCounterOutline/ShowCounterOutline)이 예전 몸통 단색 방식으로 폴백한다.
        _outlineGlow = gameObject.AddComponent<BossOutlineGlow>();
        _outlineGlow.Init(SpriteRenderer, counterOutlineMaterial, counterOutlineColorProperty, counterOutlineIntensity);
        if (counterOutlineMaterial == null)
        {
            Debug.LogWarning($"[BoneMaster] {gameObject.name}: Counter Outline Material 이 비어 있습니다. " +
                             "카운터 연출이 예전 방식(몸통 전체 단색)으로 나갑니다 — " +
                             "프리팹에 Assets/Material/Pixel_SuperArmor_Shader.mat 같은 아웃라인 머티리얼을 꽂아 주세요.");
        }

        if (Stats != null) _baseMoveSpeedCached = Stats.BaseMoveSpeed;

        if (Stats != null && Stats.Status != null)
        {
            Stats.Status.ApplySuperArmor(superArmorGauge);
            ApplyArmorOutlineTint();
        }

        if (Health != null)
        {
            Health.UpdateHPBar += CheckPartBreak;
            Health.OnBeforeDeath += HandleBeforeDeath;
        }

        SetStateText("추격 중...");
        DamageEventBus.OnBeforeDamageCalculated += HandleIncomingDamageAmp;

    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (Health != null)
        {
            Health.UpdateHPBar -= CheckPartBreak;
            Health.OnBeforeDeath -= HandleBeforeDeath;
        }
        if (CounterGauge != null) CounterGauge.OnGaugeChanged -= HandleCounterGaugeChanged;
        DamageEventBus.OnBeforeDamageCalculated -= HandleIncomingDamageAmp;
        // 예고 중에 보스가 죽으면 패턴 코루틴이 통째로 끊겨서, 그 코루틴이 만든 텔레그래프를
        // 지우는 Destroy 가 실행되지 않는다. 텔레그래프는 보스의 자식이 아니라 월드 루트
        // 오브젝트라 보스와 같이 사라지지도 않으므로 여기서 치운다.
        CleanupDanglingTelegraphs();
    }

    /// <summary>
    /// '집행' 보상. 그로기가 걸려 있는 동안에만 받는 피해에 <b>합연산</b>으로 얹힌다.
    /// 별도 타이머가 필요 없다 — IsGroggy 가 이미 정확한 수명(그로기 해제 + 페이즈 전환 강제해제)을
    /// 갖고 있어서, 그로기가 어떤 이유로 끝나든 이 보너스도 같이 꺼진다.
    /// </summary>
    public float GroggyDamageBonus { get; private set; }

    private void HandleIncomingDamageAmp(CharacterHealth target, ref DamageInfo info)
    {
        if (target != Health) return;
        if (info.amount <= 0f) return;

        float multiplier = (1f - baseArmorReduction) + perPartIncomingDamageBonus * PartsDestroyed
                         + (IsGroggy ? GroggyDamageBonus : 0f);
        info.amount *= Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// [버그 수정 — 한 방에 임계치를 두 개 넘으면 하나가 씹히던 문제]
    /// 예전엔 if 한 번이라 호출당 최대 1개만 부서졌다. HP 1200 기준 한 타에 240(20%) 이상 깎으면
    /// 0.8과 0.6을 동시에 넘는데 투구만 부서지고 견갑은 다음 피격까지 밀렸고, 강한 빌드에서는
    /// 페이즈2 진입까지 '피격 횟수'가 부족해 계속 지연됐다. 넘긴 임계치는 전부 소진할 때까지 돈다.
    ///
    /// 흉갑(마지막)이 부서지면 BreakNextPart 안에서 StopAllCoroutines + 페이즈2 전환이 시작되므로,
    /// 루프 조건이 PartsDestroyed 를 다시 읽어 자연스럽게 끝난다(중복 진입 없음).
    /// </summary>
    private void CheckPartBreak()
    {
        if (Stats == null || Health == null) return;

        while (PartsDestroyed < partBreakHpRatios.Length)
        {
            float ratio = Health.MaxHP > 0f ? Health.CurHP / Health.MaxHP : 1f;
            if (ratio > partBreakHpRatios[PartsDestroyed]) break;
            BreakNextPart();
        }
    }

    /// <summary>
    /// [버그 수정 — 오버킬 한 방에 페이즈2가 통째로 스킵되던 문제]
    /// 부위 파괴 검사는 <c>Health.UpdateHPBar</c> 이벤트에만 실려 있는데, CharacterHealth 는
    /// 사망 처리(Die)를 그보다 <b>먼저</b> 한다(CharacterHealth.cs:293-304 — Die 가 :301, UpdateHPBar 가 :304).
    /// 그래서 HP 40% 선 위에서 0 이하로 떨어뜨리는 일격이 나오면, 흉갑이 깨지기도 전에 보스가
    /// 그냥 죽어버리고 페이즈2가 영영 일어나지 않았다.
    ///
    /// 아직 부술 부위가 남아 있으면 죽음을 취소하고 HP 1로 붙잡는다. SetHP 가 그 자리에서
    /// UpdateHPBar 를 다시 쏘므로, 위 CheckPartBreak 루프가 남은 부위를 전부 부수고
    /// 흉갑 파괴 → 페이즈2 전환까지 정상적으로 이어진다.
    /// </summary>
    private bool HandleBeforeDeath(CharacterHealth health)
    {
        if (health == null) return false;
        if (CurrentPhase >= 2) return false;                        // 페이즈2에서의 죽음은 진짜 죽음이다
        if (phase2Data == null) return false;                       // 전환할 곳이 없으면 그냥 죽는다
        if (PartsDestroyed >= partBreakHpRatios.Length) return false;

        Debug.Log($"<color=red>[BoneMaster]</color> 오버킬 감지 — 부위 파괴가 {PartsDestroyed}/{partBreakHpRatios.Length}뿐이라 " +
                  "사망을 취소하고 페이즈2로 넘긴다.");

        health.SetHP(1f); // 이 안에서 UpdateHPBar -> CheckPartBreak 가 다시 돈다
        return true;      // 사망 취소
    }

    private void BreakNextPart()
    {
        int partIndex = PartsDestroyed;
        PartsDestroyed++;
        ApplyArmorOutlineTint();

        switch (partIndex)
        {
            case 0:
                AttackRangeBonus += helmetBreakRangeBonus;
                SetStateTextTemporary("투구 파괴!", Color.red, partBreakTextDuration);
                Debug.Log("<color=orange>[BoneMaster]</color> 투구 파괴! 받는피해 +15%, 공격범위 +15%");
                break;
            case 1:
                PatternCastSpeedBonus += pauldronBreakCastSpeedBonus;
                SetStateTextTemporary("견갑 파괴!", Color.red, partBreakTextDuration);
                Debug.Log("<color=orange>[BoneMaster]</color> 견갑 파괴! 받는피해 +15%(누적 30%), 패턴 시전속도 +15%");
                break;
            case 2:
                if (Stats != null && _baseMoveSpeedCached > 0f)
                {
                    Stats.SetBaseMoveSpeed(_baseMoveSpeedCached * (1f + chestBreakMoveSpeedBonus));
                }
                SetStateTextTemporary("흉갑 파괴! 페이즈 2 돌입!", Color.red, partBreakTextDuration);
                Debug.Log("<color=red>[BoneMaster]</color> 흉갑 파괴! 받는피해 +15%(누적 45%), 이동속도 +15%. 페이즈2 진입.");

                // CancelAttack() 을 먼저 부른다. StopAllCoroutines() 만 쓰면 공격 루틴이 끝에서
                // 되돌리는 IsAttacking 이 true 로 굳고, BaseEntity.CanExecuteAI 가 그걸 보고 막아서
                // 페이즈2 브레인의 Execute() 가 영영 호출되지 않는다(= 보스가 통째로 얼어붙는다).
                // IsAttacking 을 false 로 되돌리는 곳은 공격 루틴 끝과 CancelAttack() 둘뿐인데,
                // 흉갑 파괴는 플레이어 타격 순간에 터지므로 공격 도중일 확률이 높다.
                CancelAttack();
                StopAllCoroutines();
                // [버그 수정 — 전환 순간 잔여물] StopAllCoroutines()로 죽는 코루틴은 자기가 만든
                // 오브젝트(텔레그래프)를 못 지우고, 열어둔 카운터 게이지도 못 닫는다. 여기서 대신 정리한다.
                CleanupDanglingTelegraphs();
                CounterGauge?.CloseWindow();
                StartCoroutine(Phase2TransitionRoutine());
                break;
        }
    }

private IEnumerator Phase2TransitionRoutine()
    {
        // [버그 수정 — 전환 연출 중 이전 페이즈 AI가 새 패턴을 뽑아버리는 문제] CurrentState를 Skill로
        // 강제 고정해서 AIPatternSO.Execute()가 매 프레임 즉시 return하게 만든다(패턴 코루틴이 시작될
        // 때와 동일한 방식). 안 그러면 이 코루틴이 WaitForSeconds로 대기하는 동안에도 아직 안 바뀐
        // 페이즈1 브레인이 계속 매 프레임 돌면서 돌진 등 새 패턴을 뽑아, 중앙 연출 도중 보스가 딴 데로
        // 움직여버리는 문제가 실제로 있었다. 페이즈2 브레인으로 교체되고 체력 차오르는 연출이 끝날
        // 때까지 계속 Skill 상태를 유지해서, 그 사이엔 어떤 브레인도 패턴을 시전할 수 없게 한다.
        CurrentState = AIState.Skill;

        // [버그 수정 — 전환 후에도 경직이 남아 페이즈2 브레인이 안 도는 문제]
        // IsGroggy 는 이 컨트롤러의 플래그지만, 실제 행동불가는 CharacterStatus 의 Stun 상태다.
        // 그건 코루틴이 아니라 자체 타이머로 도는 값이라 위쪽 StopAllCoroutines() 로 안 사라진다.
        // 카운터 파훼 그로기(최대 5초) 도중에 흉갑이 깨지면 페이즈2에 들어가고도
        // CanExecuteAI() 가 남은 시간만큼 false 여서 보스가 그대로 얼어 있었다. 둘 다 확실히 푼다.
        IsGroggy = false;
        if (_groggyClearRoutine != null) { StopCoroutine(_groggyClearRoutine); _groggyClearRoutine = null; }
        if (_groggyFlashRoutine != null) { StopCoroutine(_groggyFlashRoutine); _groggyFlashRoutine = null; }
        if (Stats != null && Stats.Status != null) Stats.Status.RemoveStatus(StatusType.Stun);

        ClearVisualFlash();
        HardStopMovement();
        if (Health != null) Health.Invincible = true;

        // 전환 연출 동안 세워둘 자세. 전용 모션이 없어서 Stun(1프레임)을 홀드한다 —
        // 1회 클립이 아니라 루프지만 프레임이 하나뿐이라 결과는 정지 화면과 같다.
        BossAIPatternSO.PlayState(this, phase2TransitionState);

        // [추가] 페이즈2 시작 연출: 방(맵) 정중앙으로 즉시 이동시킨다. 흉갑이 깨진 위치가 투기장 벽
        // 근처 등 애매한 곳일 수 있어서, 페이즈2는 항상 중앙에서 시작하도록 고정한다.
        RoomInstance room = FindContainingRoom();
        if (room != null)
        {
            Vector3 center = (Vector3)((Vector2)room.transform.position + room.centerOffset);
            WarpTo(center);
        }

        yield return new WaitForSeconds(phase2InvincibleDuration);

        CurrentPhase = 2;

        if (phase2Data != null)
        {
            Stats.InitializeStats(phase2Data);
            _baseMoveSpeedCached = Stats.BaseMoveSpeed;

            // [버그 수정 — 흉갑 파괴의 이동속도 +15% 가 페이즈2에서 증발하던 문제]
            // 투구/견갑 보너스는 컨트롤러 프로퍼티(AttackRangeBonus/PatternCastSpeedBonus)라 전환 후에도
            // 그대로 살아 있는데, 흉갑 보너스만 Stats.SetBaseMoveSpeed 로 스탯에 직접 써 넣는 방식이었다.
            // 바로 위 InitializeStats(phase2Data) 가 스탯을 통째로 갈아엎으므로 그 보너스만 사라졌다
            // ("부위파괴 보너스는 페이즈2에서도 누적 유지" 설계와 어긋난다). 새 기준값 위에 다시 얹는다.
            if (PartsDestroyed >= partBreakHpRatios.Length && _baseMoveSpeedCached > 0f)
            {
                Stats.SetBaseMoveSpeed(_baseMoveSpeedCached * (1f + chestBreakMoveSpeedBonus));
            }

            if (Stats.Status != null) Stats.Status.ApplySuperArmor(superArmorGauge);
            ApplyArmorOutlineTint();

            if (phase2Data.aiPattern != null)
            {
                var newAi = ScriptableObject.Instantiate(phase2Data.aiPattern);
                var oldBrain = Brain;
                newAi.Init(this); // Init()이 CurrentState를 Idle로 되돌리므로, 바로 아래에서 다시 Skill로 잠근다.
                SetRuntimeBrain(newAi);
                if (oldBrain != null) Destroy(oldBrain);
            }

            CurrentState = AIState.Skill; // 체력이 다 차오를 때까지는 새 브레인도 아직 움직이지 않게 계속 잠가둔다.

            if (Health != null)
            {
                float newMax = Health.MaxHP;
                // [버그 수정 — 전환 연출 중 1프레임 '사망' 판정] CharacterHealth.SetHP 는
                // isDead = curHP <= 0f 를 그대로 대입한다. 정확히 0을 넣으면 Die() 는 안 불리지만
                // isDead 플래그만 켜져서, 그 프레임에 BossCombat.TryDamage / IsTargetInvalid 가
                // 보스를 '죽은 것'으로 취급한다. 0 대신 아주 작은 양수에서 차오르게 한다.
                Health.SetHP(Mathf.Min(0.01f, newMax));
                yield return StartCoroutine(AnimateHealthFillUp(newMax, phase2HealFillDuration));
            }
        }
        else
        {
            Debug.LogWarning("[BoneMaster] phase2Data가 비어 있어 페이즈2 스탯/AI 전환을 건너뜁니다.");
        }

        if (Health != null) Health.Invincible = false;
        CurrentState = AIState.Idle; // 연출 종료 — 이제부터 정상적으로 AI 판단 재개
        Debug.Log("<color=red>[BoneMaster]</color> 페이즈 2 전투 시작!");
    }

    private IEnumerator AnimateHealthFillUp(float targetMax, float duration)
    {
        if (Health == null || duration <= 0f)
        {
            Health?.SetHP(targetMax);
            yield break;
        }

        // 0 에서 시작하면 첫 프레임의 Lerp 결과가 0이라 isDead 가 다시 켜진다(SetHP 주석 참조).
        float from = Mathf.Min(0.01f, targetMax);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Health.SetHP(Mathf.Lerp(from, targetMax, t / duration));
            yield return null;
        }
        Health.SetHP(targetMax);
    }

    private void SetRuntimeBrain(AIPatternSO brain)
    {
        _runtimeBrain = brain;
    }

    /// <summary>
    /// 자초한 경직(그로기)을 건다. ApplyFixedStun 은 슈퍼아머·기절 clamp·기절 내성을 전부 우회하므로
    /// 여기 넘긴 duration 이 그대로 행동불가 시간이 된다(CharacterStatus.ApplyFixedStun 주석 참조).
    /// </summary>
    /// <param name="damageBonus">
    /// 이 그로기 동안에만 받는 피해에 합연산으로 얹을 값('집행' 보상용). 안 넘기면 0 이다.
    ///
    /// [버그 수정 — 집행 보상이 영구 패시브가 되던 문제] 예전엔 호출측이 GroggyDamageBonus 에
    /// 직접 대입했는데 아무도 0 으로 되돌리지 않았다. IsGroggy 는 집행 전용이 아니라 공용 플래그라,
    /// 이후 카운터 파훼로 걸리는 0.5초 그로기마다 집행 보너스가 계속 되살아났다.
    /// 여기서 매번 정하게 만들어 이월 자체를 없앤다.
    /// </param>
    public void ApplyGroggy(float duration, float damageBonus = 0f)
    {
        if (Stats == null || Stats.Status == null) return;

        IsGroggy = true;
        GroggyDamageBonus = damageBonus;
        Stats.Status.ApplyFixedStun(duration);

        if (_groggyFlashRoutine != null) StopCoroutine(_groggyFlashRoutine);
        _groggyFlashRoutine = StartCoroutine(GroggyFlashRoutine(duration));

        // [버그 수정] 예전엔 이 핸들을 안 잡아서, 그로기가 겹치면 먼저 끝나는 쪽(짧은 것)이
        // IsGroggy = false 를 써버려 긴 그로기가 조기 해제됐다. 항상 마지막 것만 살린다.
        if (_groggyClearRoutine != null) StopCoroutine(_groggyClearRoutine);
        _groggyClearRoutine = StartCoroutine(ClearGroggyFlagAfter(duration));
    }

    private IEnumerator GroggyFlashRoutine(float duration)
    {
        SetVisualFlash(groggyFlashColor);
        yield return new WaitForSeconds(duration);
        ClearVisualFlash();
        _groggyFlashRoutine = null;
    }

    private IEnumerator ClearGroggyFlagAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        IsGroggy = false;
        _groggyClearRoutine = null;
    }

    public void SetStateText(string text, Color? color = null)
    {
        if (_stateTextClearRoutine != null)
        {
            StopCoroutine(_stateTextClearRoutine);
            _stateTextClearRoutine = null;
        }
        patternLabel?.SetText(text, color);
    }

    public void SetStateTextTemporary(string text, Color color, float duration)
    {
        patternLabel?.SetText(text, color);
        if (_stateTextClearRoutine != null) StopCoroutine(_stateTextClearRoutine);
        _stateTextClearRoutine = StartCoroutine(ClearStateTextAfter(duration));
    }

    private IEnumerator ClearStateTextAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        patternLabel?.Clear();
        _stateTextClearRoutine = null;
    }

    // ── 카운터 아웃라인 발광 ──────────────────────────────────────────
    //
    // 판정이 열리기 전까지는 "깜빡이는 아웃라인"으로 색만 알려주고(= 아직 아무 판정도 없다),
    // 판정이 열리는 순간 깜빡임을 멈추고 꽉 찬 발광으로 바꾼다. 플레이어는 색으로 진짜/가짜를,
    // 깜빡임 여부로 "지금부터 유효하다"를 읽는다.

    // 파훼 가능(카운터 창이 열림) 신호에 쓸 색. 페이즈마다 다를 수 있어 패턴 SO 가 Init 에서 밀어 넣는다
    // (색을 프리팹에도 두면 SO 의 counterRealColor 와 두 소스가 갈려 반드시 어긋난다).

    /// <summary>
    /// [0830 수정안 — 색 신호를 인디케이터 한 곳으로] 예전엔 카운터 창이 열리면 여기서 몸통
    /// 아웃라인을 자동으로 켰다. 그런데 부위 파괴 아웃라인(partBreakOutlineColors)이 이미
    /// 노랑 → 주황 → 빨강으로 전투 내내 켜져 있어서, 같은 스프라이트에 카운터 노랑/빨강을 겹치면
    /// "0파괴의 기본 노랑"과 "지금 때려도 되는 노랑"이 구분되지 않는다.
    /// (기존 counterRealColor 가 초록이었던 이유가 정확히 이 회피였다.)
    ///
    /// 이제 카운터 색은 머리 위 인디케이터(<see cref="BossAttackIndicator"/>)만 표현한다.
    /// 게이지 자체는 여전히 파훼 판정에 쓰이므로 구독은 남기되, 아웃라인은 건드리지 않는다.
    /// </summary>
    private void HandleCounterGaugeChanged()
    {
        // 의도적으로 비워 둔다 — 되살리면 부위 파괴 아웃라인과 신호가 충돌한다.
    }

    /// <summary>유예 구간용. 아웃라인을 지정 색으로 켜고 깜빡이게 한다(판정 전).</summary>
    public void PulseCounterOutline(Color color, float elapsed)
    {
        if (_outlineGlow == null || !_outlineGlow.IsUsable)
        {
            // 폴백: 머티리얼 미배선 — 예전 방식(몸통 단색)으로라도 색은 알려준다.
            SetVisualFlash(color);
            return;
        }

        if (!_outlineGlow.IsVisible) _outlineGlow.Show(color);

        float wave = Mathf.Abs(Mathf.Sin(elapsed * Mathf.PI * Mathf.Max(0.01f, counterOutlinePulseSpeed)));
        _outlineGlow.SetBrightness(Mathf.Lerp(counterOutlinePulseFloor, 1f, wave));
    }

    /// <summary>판정 개시. 깜빡임을 멈추고 아웃라인을 꽉 채운다.</summary>
    public void ShowCounterOutline(Color color)
    {
        if (_outlineGlow == null || !_outlineGlow.IsUsable)
        {
            SetVisualFlash(color);
            return;
        }

        _outlineGlow.Show(color);
        _outlineGlow.SetBrightness(1f);
    }

    public void ClearCounterOutline()
    {
        _outlineGlow?.Hide();
        ClearVisualFlash(); // 폴백 경로로 몸통을 칠했을 수도 있으니 항상 같이 되돌린다
    }

    /// <summary>
    /// 지금 부위 파괴 수에 맞는 아웃라인 색을 적용한다. 색표가 짧으면 마지막 색을 쓴다.
    /// 슈퍼아머 오버레이는 CharacterVisualFeedback 이 LateUpdate 에서 그리므로 색만 넘겨주면 된다.
    /// </summary>
    private void ApplyArmorOutlineTint()
    {
        if (partBreakOutlineColors == null || partBreakOutlineColors.Length == 0) return;

        var vf = GetComponentInChildren<CharacterVisualFeedback>(true);
        if (vf == null) return;

        int idx = Mathf.Clamp(PartsDestroyed, 0, partBreakOutlineColors.Length - 1);
        vf.SetSuperArmorTint(partBreakOutlineColors[idx]);
    }

    /// <summary>
    /// '집행'의 은신. 보스를 통째로 안 보이게 하고 무적으로 만든다.
    ///
    /// [함정] 알파를 0으로 만드는 방식은 안 된다 — 슈퍼아머 아웃라인 오버레이는 별도 SpriteRenderer 라
    /// 본체 color 의 알파를 안 따라가고 enabled 만 따라간다. 알파로 지우면 노란 뼈 윤곽만 허공에 남는다.
    ///
    /// 그림자·방향 인디케이터도 보스의 자식이라 같이 꺼야 위치가 새지 않는다.
    /// 반드시 try/finally 로 복구해라 — 코루틴이 끊긴 채로 남으면 투명 무적 보스가 된다.
    /// </summary>
    public void SetHidden(bool hidden)
    {
        _isHidden = hidden;
        if (_bodyRenderers != null)
            foreach (var sr in _bodyRenderers)
                if (sr != null) sr.enabled = !hidden;

        if (Health != null) Health.Invincible = hidden;
        if (hidden) SetStateText("");
    }

    public void SetVisualFlash(Color color)
    {
        if (_bodyRenderers == null) return;
        foreach (var sr in _bodyRenderers)
        {
            if (sr == null) continue;
            sr.color = color;
        }
    }

    public void ClearVisualFlash()
    {
        if (_bodyRenderers == null) return;
        for (int i = 0; i < _bodyRenderers.Length; i++)
        {
            if (_bodyRenderers[i] == null) continue;
            _bodyRenderers[i].color = _bodyOriginalColors[i];
        }
    }

    public void HardStopMovement()
    {
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        if (_navAgent != null && _navAgent.isOnNavMesh)
        {
            _navAgent.velocity = Vector3.zero;
        }
    }

    /// <summary>NavMeshAgent가 있으면 Warp()로, 없으면 그냥 Transform으로 위치를 옮긴다.
    /// 목표 지점이 NavMesh 위가 아니면(장식 바위 등 걷기 불가능한 지형 근처) 가장 가까운 유효한
    /// 지점으로 보정해서 워프한다 — 안 그러면 Warp가 조용히 실패하면서 보스가 그 자리에 멈춰버린다.</summary>
public void WarpTo(Vector3 pos)
    {
        // [버그 수정 — 보스가 방을 뚫는 문제] Warp()는 물리 충돌을 거치지 않는 순간이동이라, 개별
        // 패턴이 벽 체크를 깜빡하면 그대로 경계를 뚫고 나갈 수 있었다(예: 견갑 찌르기의 재조준+대시엔
        // 벽 체크가 아예 없었음). 모든 이동이 최종적으로 이 함수를 거치므로, 여기서 한 번에
        // 방 안쪽으로 clamp해서 원천 차단한다.
        //
        // 예전엔 뼈 투기장 링이 이 역할을 겸했다. 투기장을 지우면서 방 사각형으로 갈아탔는데,
        // 이게 도약 착지와 견갑 대시의 유일한 벽 가드다 — 지우면 그 둘이 다시 벽을 뚫는다.
        if (TryGetArenaRect(out Vector2 arenaCenter, out Vector2 arenaHalf))
        {
            pos.x = Mathf.Clamp(pos.x, arenaCenter.x - arenaHalf.x, arenaCenter.x + arenaHalf.x);
            pos.y = Mathf.Clamp(pos.y, arenaCenter.y - arenaHalf.y, arenaCenter.y + arenaHalf.y);
        }

        if (_navAgent != null)
        {
            Vector3 target = pos;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                target = hit.position;
            }
            else
            {
                Debug.LogWarning($"[BoneMaster] WarpTo({pos})가 NavMesh 근처(3유닛 이내)에서 유효한 지점을 못 찾았습니다. 원래 좌표로 그대로 이동합니다.");
            }
            _navAgent.Warp(target);
        }
        else
        {
            transform.position = pos;
        }
    }

    /// <summary>
    /// 돌진이 실제로 나아갈 수 있는 거리. 예고 레인의 길이이자 이동 시간 예산의 근거다.
    ///
    /// 방 경계까지의 거리와 실제 벽/장애물까지의 거리 중 가까운 쪽을 돌려준다.
    /// 이 보스는 Warp(순간이동)로 움직여서 콜라이더가 몸을 안 막아주므로, 여기서 미리 재지 않으면
    /// 예고를 벽 너머까지 그리고 그만큼의 시간 예산을 잡는다.
    /// 선 패턴처럼 "방을 가로지르는 길이"가 필요한 쪽도 이걸 쓴다.
    /// </summary>
    /// <param name="checkRadius">보스 몸 두께. 벽에서 이만큼 떨어진 지점까지를 거리로 돌려준다.</param>
    public float GetChargeDistance(Vector2 origin, Vector2 dir, float checkRadius)
    {
        // 방을 못 찾으면 얼마까지 훑을지 기준이 없다. 어떤 방보다도 긴 거리면 충분하다.
        const float NoRoomScanDistance = 60f;
        float scan = TryGetArenaRect(out Vector2 c, out Vector2 h)
                   ? RectExitDistance(origin, dir, c, h)
                   : NoRoomScanDistance;

        RaycastHit2D hit = Physics2D.CircleCast(origin, checkRadius, dir, scan,
                                                LayerMask.GetMask("Wall", "Object"));
        return hit.collider != null ? hit.distance : scan;
    }

    private EliteBossPatternLabel CreatePatternLabel()
    {
        GameObject labelObj = new GameObject("PatternLabel");
        labelObj.transform.SetParent(transform, false);
        labelObj.transform.localPosition = patternLabelOffset;

        Vector3 lossy = transform.lossyScale;
        float invX = lossy.x != 0f ? 1f / lossy.x : 1f;
        float invY = lossy.y != 0f ? 1f / lossy.y : 1f;
        labelObj.transform.localScale = new Vector3(invX, invY, 1f);

        return labelObj.AddComponent<EliteBossPatternLabel>();
    }

    /// <summary>
    /// 보스가 벗어나면 안 되는 방 사각형. 방을 못 찾으면 false — 그 경우 호출측은 clamp를 건너뛴다.
    ///
    /// [뼈 투기장 삭제] 예전엔 ThornArenaHazard 링이 경계이자 장판이었다. 링이 사라졌으므로
    /// 방 자체를 경계로 쓴다. roomSize 는 장식 벽 띠까지 포함한 값이라 그대로 쓰면 벽 안으로
    /// 들어갈 수 있어서 roomBoundsMarginRatio(링이 쓰던 값과 동일)를 곱한다.
    /// </summary>
    public bool TryGetArenaRect(out Vector2 center, out Vector2 half)
    {
        center = default;
        half = default;

        RoomInstance room = FindContainingRoom();
        if (room == null) return false;

        center = (Vector2)room.transform.position + room.centerOffset;
        half = new Vector2(room.roomSize.x, room.roomSize.y) * (0.5f * roomBoundsMarginRatio);
        return true;
    }

    /// <summary>
    /// origin 에서 dir 방향으로 갔을 때 방 사각형을 빠져나가기까지의 거리(슬래브 기법).
    /// 축마다 어느 면에 먼저 닿는지를 재서 더 가까운 쪽을 쓴다. dir 은 정규화돼 있다고 본다.
    /// </summary>
    public static float RectExitDistance(Vector2 origin, Vector2 dir, Vector2 center, Vector2 half)
    {
        float t = float.MaxValue;
        if (Mathf.Abs(dir.x) > 0.0001f)
            t = Mathf.Min(t, ((dir.x > 0f ? center.x + half.x : center.x - half.x) - origin.x) / dir.x);
        if (Mathf.Abs(dir.y) > 0.0001f)
            t = Mathf.Min(t, ((dir.y > 0f ? center.y + half.y : center.y - half.y) - origin.y) / dir.y);

        return t == float.MaxValue ? 0f : Mathf.Max(0f, t);
    }

    /// <summary>
    /// [버그 수정 — 페이즈 전환 시 텔레그래프 잔여물] StopAllCoroutines()로 패턴 코루틴을 강제 종료하면
    /// 그 코루틴이 만든 텔레그래프(BoneMasterTelegraphUtil.SpawnXXX)를 지우는 Object.Destroy() 코드가
    /// 실행되지 못하고 그대로 씬에 남는다(예: 견갑 찌르기 3타 도중 흉갑이 깨지면 노란 텔레그래프가 영원히
    /// 남음). BoneMasterTelegraphUtil이 만드는 모든 텔레그래프는 "BoneMaster_Telegraph_" 접두사를 쓰므로,
    /// 페이즈 전환 시작 시점에 이름으로 찾아 전부 정리한다.
    /// </summary>
    private int _lastTelegraphCleanupFrame = -1;

    public void CleanupDanglingTelegraphs()
    {
        // 한 프레임에 두 번 이상 부를 이유가 없다. 정리 경로가 두 갈래(브레인의 OnAttackCancelled 훅과
        // 컨트롤러의 StopActivePattern)라 CancelAttack 한 번에 씬 전수 스캔이 두 번 돌았다.
        // 이 함수는 멱등이고 씬 전체를 훑으므로, 프레임당 1회면 충분하다.
        if (_lastTelegraphCleanupFrame == Time.frameCount) return;
        _lastTelegraphCleanupFrame = Time.frameCount;

        // 예고 게이지도 같은 운명이다 — 코루틴이 강제 종료되면 Stop 을 부르는 줄까지 못 가서
        // 게이지가 반쯤 찬 채로 머리 위에 얼어붙는다. 여기가 그 유일한 회수 지점이다.
        BossAttackIndicator.Stop(this);

        var allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            if (!t.name.StartsWith("BoneMaster_Telegraph_")) continue;
            Destroy(t.gameObject);
            count++;
        }
        if (count > 0)
        {
            Debug.Log($"<color=cyan>[BoneMaster]</color> 중단된 패턴이 남긴 텔레그래프 {count}개 정리함.");
        }
    }


    private RoomInstance FindContainingRoom()
    {
        if (_cachedRoom != null) return _cachedRoom;

        foreach (var room in FindObjectsByType<RoomInstance>(FindObjectsSortMode.None))
        {
            Bounds bounds = new Bounds(
                (Vector2)room.transform.position + room.centerOffset,
                new Vector3(room.roomSize.x, room.roomSize.y, 100f));
            if (bounds.Contains(transform.position))
            {
                _cachedRoom = room;
                return room;
            }
        }
        return null;
    }
}
