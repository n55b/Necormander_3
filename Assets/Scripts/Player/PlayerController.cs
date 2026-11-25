using System;
using UnityEngine;

public enum PlayerStates
{
    [UnityEngine.InspectorName("대기")] Idle,
    [UnityEngine.InspectorName("전투")] Battle
}

public partial class PlayerController : MonoBehaviour
{
    [Header("기획 조절 · 동작과 타이밍")]
    [SerializeField, Min(0.01f), Tooltip("이 이동속도일 때 걷기 모션을 원래 속도로 재생합니다. 실제 이동속도는 바꾸지 않습니다.")]
    private float walkAnimationReferenceSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("툴팁 위쪽 끝에서 F 아이콘 아래쪽 끝까지의 간격입니다. 상호작용 대상 선택에는 영향이 없습니다.")]
    private float uiIconGap = 0.05f;
    [SerializeField, Tooltip("X는 최소, Y는 최대 재생 배속입니다. 이동·대시·공격 판정 시간에는 영향이 없습니다.")]
    private Vector2 walkAnimationSpeedRange = new Vector2(0.3f, 3f);

    [Header("플레이어 스탯")]
    [SerializeField] CharacterStat stat;
    public CharacterStat Stat => stat;
    [SerializeField] private int summonNum;
    [SerializeField] private float summonRange;

    [Header("무적 (i-Frame) 설정")]
    [SerializeField] private float invincibilityDuration = 1.0f;
    [SerializeField] private float invincibilityBlinkInterval = 0.1f;

    [Header("플레이어 상태")]
    [SerializeField] PlayerStates P_State = PlayerStates.Idle;

    public PlayerStates GetPlayerState() => P_State;

    // 상태에 따른 Action 이벤트
    public event Action OnEnterIdle;
    public event Action OnEnterBattle;

    // 비전투 상태 추적
    private float lastCombatTime = 0f;
    public bool IsOutOfCombat => (Time.time - lastCombatTime) > 5.0f;

    public void RecordCombatAction()
    {
        lastCombatTime = Time.time;
    }

    [Header("상호작용 설정")]
    [SerializeField] private float interactRange = 1.5f;
    [SerializeField] private LayerMask interactableLayer;
    [Tooltip("선택된 상호작용 대상의 콜라이더 위에서 F 아이콘을 띄울 간격(월드 유닛).")]
    [SerializeField] private float interactIconOffset = 0.35f;
    private IInteractable _closestInteractable;

    // [F 홀드] IHoldInteractable 을 구현한 대상만 '떼는 순간' 판정으로 바뀐다.
    // 홀드가 끝까지 찼으면 _holdConsumed 가 켜져서 손을 떼도 Interact 가 안 불린다.
    private IHoldInteractable _holdTarget;
    private float _holdTimer;
    private bool _holdConsumed;

    [Header("플레이어 애니메이터")]
    [SerializeField] Animator BodyAnimator;
    [SerializeField] private VisualSquashStretch visualSquash;
    public void PlayVisualPulse(string pulseName) => visualSquash?.Play(pulseName);
    [SerializeField] PlayerAnimationState currentAnimState;

    private bool _inputBlocked = false; // 맵 생성 중 입력 차단용
    public bool IsInputBlocked => _inputBlocked;

    /// <summary>시간 정지와 모달 창의 입력 차단 상태. 정보 오버레이는 포인터 입력에서 별도로 처리한다.</summary>
    public static bool IsGameplayPaused =>
        Time.timeScale == 0f ||
        (UIPopUpManager.Instance != null && UIPopUpManager.Instance.BlocksGameplayInput);

    /// <summary>기절·빙결·경직으로 행동이 차단된 상태.</summary>
    public bool IsCCed => stat != null && stat.Status != null && stat.Status.IsActionBlocked;

    /// <summary>
    /// 애니메이션 캐싱 변수
    /// </summary>
    public IdleState idleState;
    public WalkState walkState;


    private Coroutine _animStateLockTimeoutCoroutine; // safety net so canChangeState never gets stuck false forever
    public bool canChangeState = true;

    [Header("이동 변수")]
    [SerializeField] Vector3 MoveDirection = Vector3.zero;
    [SerializeField] Vector2 moveInput = Vector2.zero;
    public Vector2 MoveInput => moveInput;

    [Header("조작감 설정")]
    [Tooltip("출발/가속 반응 시간. 작을수록 즉각적이다. 최대 이동속도는 스탯을 그대로 사용한다.")]
    [Min(0.001f), SerializeField] private float movementAccelerationTime = 0.045f;
    [Tooltip("키를 뗐을 때 감속하는 시간. 너무 늘리면 미끄러진다. 문 이동·입력 차단에는 적용하지 않는다.")]
    [InspectorName("Movement Stop Smooth Time")]
    [Min(0.001f), SerializeField] private float movementSmoothTime = 0.065f;
    [Tooltip("급격한 방향 전환 반응 시간. 가속보다 짧게 두면 회피 조작이 둔해지지 않는다.")]
    [Min(0.001f), SerializeField] private float movementTurnTime = 0.022f;
    private PlayerLocomotion _locomotion;

    [Header("구르기(대쉬) 설정")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 1.0f;
    [Tooltip("대시 이동 그래프(두 대시 경로 공용). X=시간 0~1, Y=거리 0~1. 직선은 등속. " +
             "거리·전체 시간·벽 검사는 유지하고 가속/감속만 바꾼다. 내려가는 구간은 대기 처리하여 역주행을 막는다.")]
    [SerializeField] private AnimationCurve dashMovementCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    private bool _isDashing = false;
    private float _dashTimeLeft;
    private Vector2 _dashStartPosition;
    private float _dashTravelDuration;
    private float _dashElapsed;
    private float _dashProgress;
    private Vector2 _dashEndPosition;
    private float _lastDashTime;
    private Vector2 _dashDir;
    private int _originalLayer;
    private int _dashLayer;

    [Header("평타 준비 이동")]
    [SerializeField, Min(0f)] private float attackDashSpeed = 7.5f;
    [SerializeField, Min(0f)] private float attackDashDuration = 0.04f;
    [Tooltip("준비 이동 곡선입니다. X는 이동 시간 비율, Y는 누적 이동 거리 비율입니다.")]
    [SerializeField] private AnimationCurve attackDashMovementCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    private bool _isAttackDashing = false;
    private float _attackDashTimeLeft = 0f;
    private Vector2 _attackDashDir = Vector2.zero;
    private float _attackDashSpeedVal;
    private float _attackDashProgress;

    public bool IsDashing => _isDashing || (_dodgeController != null && _dodgeController.IsDashing);

    /// <summary>대쉬 쿨감을 먹인 최종 쿨타임(초). 대쉬 쿨감은 스킬 쿨감과 별개 스탯이다.</summary>
    public float DashCooldown => stat != null ? stat.ApplyDashCooldown(dashCooldown) : dashCooldown;
    public float DashCooldownProgress
    {
        get
        {
            float cd = DashCooldown;
            return cd > 0f ? Mathf.Clamp01((Time.time - _lastDashTime) / cd) : 1f;
        }
    }

    private Rigidbody2D _rb;
    private MeleeDodgeController _dodgeController;
    private MeleeCombatController _melee;
    private Collider2D _dashBody;
    internal Collider2D BodyCollider => _dashBody;
    private Vector2 _lastSafeDashPosition;
    private bool _dashLandingTracked;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _dashBody = GetComponent<Collider2D>();
        _dodgeController = GetComponent<MeleeDodgeController>();
        _melee = GetComponent<MeleeCombatController>();
        ProjectContentStore.TryReadPublished(ProjectPlayerTuning.Document,()=>{
            var tuning = new ProjectPlayerTuning();
            ApplyProjectTuning(tuning);
            if (_melee != null) _melee.ApplyProjectTuning(tuning);
        });

        _originalLayer = gameObject.layer;
        _dashLayer = Layers.PlayerDash;
        if (_dashLayer == -1) 
        {
            Debug.LogWarning("[PlayerController] 'Player_Dash' 레이어가 설정되어 있지 않습니다! 레이어 세팅 가이드를 확인해주세요.");
            _dashLayer = _originalLayer;
        }

        // 스탯 초기화를 Awake로 이동하여 초기화 순서 보장
        if (stat != null)
        {
            stat.Setup();
        }

        CachingAnim();
        currentAnimState = null;
        // 애니메이션 기본으로 설정
        TransitionToState(idleState);
    }



    // 애니메이션 캐싱
    private void CachingAnim()
    {
        idleState = new IdleState(this);
        walkState = new WalkState(this);

    }

    private void Start()
    {
        if (stat != null)
        {
            // 플레이어 피격 시 들고 있는 미니언 낙하 로직 연결
            if (stat.Health != null)
            {
                stat.Health.OnDamageTaken += HandleDamageTaken;
            }
        }
    }

    private void OnDestroy()
    {
        PopupSystem.ReleaseInteractionIcon();

        if (stat != null && stat.Health != null)
        {
            stat.Health.OnDamageTaken -= HandleDamageTaken;
        }
    }

    private Coroutine _invincibilityCoroutine;
    private readonly System.Collections.Generic.List<SpriteRenderer> _invincibilityRenderers = new System.Collections.Generic.List<SpriteRenderer>();

    private void HandleDamageTaken(float damage)
    {
        if (damage > 0) RecordCombatAction(); // 피격 시 전투 상태 갱신

        // 데미지를 받았을 때 1초 무적 & 깜빡임 처리
        if (damage > 0)
        {
            visualSquash?.PlayHit();
            if (_invincibilityCoroutine != null) StopCoroutine(_invincibilityCoroutine);
            _invincibilityCoroutine = StartCoroutine(InvincibilitySequence());
        }
    }

    private System.Collections.IEnumerator InvincibilitySequence()
    {
        if (stat == null || stat.Health == null) yield break;

        stat.Health.Invincible = true;

        GetComponentsInChildren(false, _invincibilityRenderers);
        var srs = _invincibilityRenderers;
        float elapsed = 0f;
        float duration = invincibilityDuration;
        float blinkInterval = invincibilityBlinkInterval;
        bool isVisible = true;
        var wait = new WaitForSeconds(blinkInterval);

        while (elapsed < duration)
        {
            elapsed += blinkInterval;
            isVisible = !isVisible;

            foreach (var sr in srs)
            {
                if (sr == null) continue;
                Color c = sr.color;
                c.a = isVisible ? 1.0f : 0.2f;
                sr.color = c;
            }
            yield return wait;
        }

        foreach (var sr in srs)
        {
            if (sr == null) continue;
            Color finalC = sr.color;
            finalC.a = 1.0f;
            sr.color = finalC;
        }

        stat.Health.Invincible = false;
        _invincibilityCoroutine = null;
    }

    private void Update()
    {
        if (stat != null && stat.Health != null && stat.Health.IsDead) { visualSquash?.ResetVisual(); StopLocomotion(); return; }

        if (_inputBlocked) return;

        // 입력 보간만 비운다. 넉백/대시가 소유한 물리 속도를 렌더 Update에서 덮어쓰지 않는다.
        if (IsGameplayPaused || IsCCed) StopLocomotion(false);
        UpdateLocomotionAnimation();

        CheckForInteractable();
        TickInteractHold();

    }

    public void ChangeState(PlayerStates _state)
    {
        if (P_State == _state) return;

        P_State = _state;

        if (P_State == PlayerStates.Battle)
        {
            OnEnterBattle?.Invoke();
        }
        else if (P_State == PlayerStates.Idle)
        {
            OnEnterIdle?.Invoke();
        }
    }


    /// <summary>
    /// 애니메이션용 스테이트 변화 및 재생 제어 함수들
    /// </summary>
    private float _lastAnimSpeed = 1f;

    /// <summary>
    /// 이동 속도에 비례해 걷기/달리기 애니메이션 재생 속도를 맞춥니다.
    /// baseMoveSpeed를 기준(1.0x)으로 재생 속도를 기본값(1.0f)으로 초기화합니다.
    /// </summary>
    private void ResetWalkAnimSpeed()
    {
        if (Mathf.Abs(1f - _lastAnimSpeed) < 0.01f) return;
        _lastAnimSpeed = 1f;

        if (BodyAnimator != null) BodyAnimator.speed = 1f;
    }

    /// <summary>
    /// 현재 이동 속도와 캐릭터의 기본 이동 속도를 비교하여 애니메이션 재생 속도를 동적으로 업데이트합니다.
    /// </summary>
    /// <param name="currentSpeed">현재 실제 캐릭터 이동 속도</param>
    public void SetAttackAnimSpeed(float speed)
    {
        _lastAnimSpeed = speed;
        if (BodyAnimator != null) BodyAnimator.speed = speed;
    }


    private void UpdateWalkAnimSpeed(float currentSpeed)
    {
        float speedRatio = Mathf.Clamp(currentSpeed / Mathf.Max(0.01f, walkAnimationReferenceSpeed), Mathf.Max(0.01f, walkAnimationSpeedRange.x), Mathf.Max(0.01f, Mathf.Max(walkAnimationSpeedRange.x, walkAnimationSpeedRange.y)));

        if (Mathf.Abs(speedRatio - _lastAnimSpeed) < 0.01f) return;
        _lastAnimSpeed = speedRatio;

        if (BodyAnimator != null) BodyAnimator.speed = speedRatio;
    }

    /// <summary>
    /// 외부(예: MeleeDodgeController 등)에서 PlayAllAnim으로 애니메이션을 직접 강제 재생했을 때 호출합니다.
    /// 현재 애니메이션 상태 캐시를 해제하여, 다음 TransitionToState 호출이 정상적으로 적용되도록 합니다.
    /// </summary>
    public void ResetAnimStateCache()
    {
        currentAnimState = null;
    }

    /// <summary>
    /// 새로운 애니메이션 상태(State)로 전환합니다. 기존 상태의 Exit()와 새 상태의 Enter()를 처리합니다.
    /// </summary>
    /// <param name="newState">전환할 새로운 애니메이션 상태</param>
    public void TransitionToState(PlayerAnimationState newState)
    {
        if (currentAnimState == newState) return;

        currentAnimState?.Exit();

        currentAnimState = newState;

        currentAnimState.Enter();
    }

    /// <summary>
    /// 애니메이션 상태 변경이 가능한 상태로 플래그를 전환합니다.
    /// </summary>
    public void CanChangeAnimState()
    {
        var melee = GetComponent<MeleeCombatController>();
        if (melee != null && !melee.TryFinishAttackAnimation()) return;
        canChangeState = true;

        if (_animStateLockTimeoutCoroutine != null)
        {
            StopCoroutine(_animStateLockTimeoutCoroutine);
            _animStateLockTimeoutCoroutine = null;
        }
    }

    /// <summary>
    /// Locks the Idle/Walk auto-transition (canChangeState = false), the same as setting
    /// canChangeState directly, but also schedules a safety-net timeout that force-unlocks
    /// it if CanChangeAnimState() is never called (missing/mistimed Animation Event,
    /// interrupted animation, etc.) so the character can never get stuck forever.
    /// </summary>
    public void LockAnimState(float maxLockDuration = 3f)
    {
        canChangeState = false;

        if (_animStateLockTimeoutCoroutine != null) StopCoroutine(_animStateLockTimeoutCoroutine);
        _animStateLockTimeoutCoroutine = maxLockDuration > 0f
            ? StartCoroutine(AnimStateLockTimeoutRoutine(maxLockDuration)) : null;
    }

    private System.Collections.IEnumerator AnimStateLockTimeoutRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);

        if (!canChangeState)
        {
            Debug.LogWarning("<color=red>[PlayerController]</color> canChangeState was force-reset by the timeout safety net. Check for a missing/mistimed Animation Event.");
            CanChangeAnimState();
        }
    }


    /// <summary>
    /// Body의 Animator에서 지정된 이름의 애니메이션 상태를 강제로 재생합니다 (손은 같은 클립 안에 함께 키프레임으로 포함됨).
    /// </summary>
    /// <param name="animName">재생할 애니메이션 상태의 이름</param>
    public void PlayAllAnim(string animName, string fallbackAnimName = null)
    {
        if (BodyAnimator == null) return;

        int hash = Animator.StringToHash(animName);
        if (BodyAnimator.HasState(0, hash))
        {
            BodyAnimator.Play(hash);
            return;
        }

        // requested state not ready yet -> play fallback state instead
        if (!string.IsNullOrEmpty(fallbackAnimName))
        {
            if (BodyAnimator.HasState(0, Animator.StringToHash(fallbackAnimName)))
            {
                BodyAnimator.Play(fallbackAnimName);
            }
        }
    }
}
