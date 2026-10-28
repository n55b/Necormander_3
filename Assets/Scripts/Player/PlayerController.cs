using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerStates
{
    [UnityEngine.InspectorName("대기")] Idle,
    [UnityEngine.InspectorName("전투")] Battle
}

public class PlayerController : MonoBehaviour
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

    private bool _inputBlocked = false; // [추가] 맵 생성 중 입력 차단용
    public bool IsInputBlocked => _inputBlocked;

    /// <summary>
    /// 게임플레이 입력(이동·평타·대쉬·스킬·패리·상호작용)을 받으면 안 되는가.
    /// 팝업 시스템에서 시간 정지를 뺐기 때문에, timeScale==0 만 보면 보상창 버튼을 누를 때 평타가 같이 나간다.
    /// 그래서 '창이 떠 있음'도 같이 본다. 주머니/장착 정보(Overlay)는 여기 포함되지 않는다 — 전투 중에 보는
    /// 정보창은 이동을 막지 않는다. C는 패널 위 클릭만, 주머니는 공격을 차단한다.
    /// V/Tab/ESC/M 같은 창 여닫기 키에는 쓰지 않는다.
    /// </summary>
    public static bool IsGameplayPaused =>
        Time.timeScale == 0f ||
        (UIPopUpManager.Instance != null && UIPopUpManager.Instance.BlocksGameplayInput);

    /// <summary>
    /// 기절/빙결/경직으로 행동이 막혀 있는가.
    ///
    /// [26/07/17 신설] 예전엔 플레이어에게 CC 경로가 아예 없었다. MOVESPEED 가 0 이 되면서
    /// 이동만 간접적으로 멈췄고, 평타/스킬/대쉬는 그대로 나갔다(특히 대쉬는 linearVelocity 를
    /// 직접 써서 MOVESPEED 조차 우회했다). 이제 이동/평타/Q·E/R/대쉬를 전부 막는다.
    ///
    /// 지금은 실제로 걸릴 일이 없다 — 상태이상 부여 수단이 유물 전용이고 유물이 아직 없다.
    /// 경직(Hitstun)도 플레이어엔 안 붙는다(BaseEntity 가 있는 유닛에만 부여됨). 미리 뚫어둔 배선이다.
    /// </summary>
    public bool IsCCed => stat != null && stat.Status != null && stat.Status.IsActionBlocked;

    /// <summary>
    /// 애니메이션 캐싱 변수
    /// </summary>
    public IdleState idleState;
    public WalkState walkState;


    private Coroutine _animStateLockTimeoutCoroutine; // safety net so canChangeState never gets stuck false forever
    public bool canChangeState = true;

    // [추가] 외부에서 입력을 차단/해제하는 기능
    public void SetInputBlocked(bool blocked)
    {
        _inputBlocked = blocked;
        moveInput = Vector2.zero;
        if (blocked)
        {
            visualSquash?.ResetVisual();
            _isAttackDashing = false;
            _attackDashTimeLeft = 0f;
            PopupSystem.HideInteractionIcon();
            GetComponent<PlayerParryController>()?.StopGuard();
            // 차단 중에는 Update의 Idle 전환도 멈춘다. 페이드 시작 전에 걷기를 끝낸다.
            // 공격/대쉬의 애니메이션 잠금이나 사망 모션은 덮어쓰지 않는다.
            if (canChangeState && idleState != null && (stat == null || !stat.IsDead))
            {
                ResetWalkAnimSpeed();
                TransitionToState(idleState);
            }
        }
        else if (stat != null && !stat.IsDead && !IsCCed)
        {
            // 계속 누른 키는 performed가 다시 오지 않는다. 차단 전 값을 복원하지 말고
            // 현재 액션 값을 읽어 암전 중 키 해제/방향 변경까지 반영한다.
            var move = GetComponent<PlayerInput>()?.actions?.FindAction("Player/Move");
            if (move != null && move.enabled) moveInput = move.ReadValue<Vector2>();
        }

        // 이전 방의 보간값/속도가 남으면 손을 뗀 뒤에도 새 방에서 미끄러진다.
        _smoothedMoveInput = moveInput;
        _moveInputVelocity = Vector2.zero;
        MoveDirection = moveInput;
        if (_rb != null) _rb.linearVelocity = Vector2.zero;
        Debug.Log($"<color=yellow>[Player]</color> Input Blocked: {blocked}");
    }

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
    private Vector2 _smoothedMoveInput;
    private Vector2 _moveInputVelocity;

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

    [Header("평타 전진 가속 설정")]
    [SerializeField] private float attackDashSpeed = 10f; // 평타 공격 시 순간 대시 속도
    [SerializeField] private float attackDashDuration = 0.08f; // 평타 대시 지속 시간 (절도 있는 느낌용 극단적 짧은 시간)
    [Tooltip("평타 전진 그래프. X=시간 0~1, Y=전진 거리 0~1. 초반이 가파르면 타격 순간 빠르게 전진한다. " +
             "그림 재생 속도와 타격 시점은 바꾸지 않는다.")]
    [SerializeField] private AnimationCurve attackDashMovementCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    private bool _isAttackDashing = false;
    private float _attackDashTimeLeft = 0f;
    private Vector2 _attackDashDir = Vector2.zero;
    private float _attackDashSpeedVal = 10f;
    private float _attackDashProgress;

    public bool IsDashing => _isDashing;

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
    private Collider2D _dashBody;
    private Vector2 _lastSafeDashPosition;
    private bool _dashLandingTracked;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _dashBody = GetComponent<Collider2D>();

        _originalLayer = gameObject.layer;
        _dashLayer = Layers.PlayerDash;
        if (_dashLayer == -1) 
        {
            Debug.LogWarning("[PlayerController] 'Player_Dash' 레이어가 설정되어 있지 않습니다! 레이어 세팅 가이드를 확인해주세요.");
            _dashLayer = _originalLayer;
        }

        // [패리] 패리 컨트롤러 추가
        if (GetComponent<PlayerParryController>() == null)
            gameObject.AddComponent<PlayerParryController>();

        // [26/08/15] 서브 소환수 삭제로 SubSummonPassiveController 부착을 걷어냈다.

        // [수정] 스탯 초기화를 Awake로 이동하여 초기화 순서 보장
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
            // [추가] 플레이어 피격 시 들고 있는 미니언 낙하 로직 연결
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

        // 소환수 액티브는 Space(OnMinionSkill)가 담당한다.

        if (_inputBlocked) return;

        // 입력 보간만 비운다. 넉백/대시가 소유한 물리 속도를 렌더 Update에서 덮어쓰지 않는다.
        if (IsGameplayPaused || IsCCed) StopLocomotion(false);
        UpdateLocomotionAnimation();

        CheckForInteractable(); // [추가]
        TickInteractHold();

    }

    // 입력은 콜백에서 받고 보간은 물리 틱에서만 한다. 렌더 프레임률에 따라 조작감이 달라지지 않게 한다.
    private void AdvanceLocomotion(Vector2 input, float deltaTime)
    {
        Vector2 target = Vector2.ClampMagnitude(input, 1f);
        bool stopping = target.sqrMagnitude < 0.0001f;
        bool turning = !stopping && _smoothedMoveInput.sqrMagnitude > 0.0004f &&
            Vector2.Dot(_smoothedMoveInput.normalized, target.normalized) < 0.85f;
        float response = stopping ? movementSmoothTime : turning ? movementTurnTime : movementAccelerationTime;
        if (turning && Vector2.Dot(_moveInputVelocity, target - _smoothedMoveInput) < 0f)
            _moveInputVelocity = Vector2.zero;
        _smoothedMoveInput = Vector2.ClampMagnitude(Vector2.SmoothDamp(_smoothedMoveInput, target,
            ref _moveInputVelocity, Mathf.Max(0.001f, response), Mathf.Infinity, deltaTime), 1f);
        if (stopping && _smoothedMoveInput.sqrMagnitude < 0.0004f)
        {
            _smoothedMoveInput = Vector2.zero;
            _moveInputVelocity = Vector2.zero;
        }
        MoveDirection = _smoothedMoveInput;
    }

    private void StopLocomotion(bool stopVelocity = true)
    {
        _smoothedMoveInput = _moveInputVelocity = Vector2.zero;
        MoveDirection = Vector3.zero;
        if (stopVelocity && _rb != null) _rb.linearVelocity = Vector2.zero;
    }

    private void UpdateLocomotionAnimation()
    {
        if (!canChangeState) return; // 공격/대시의 모션과 재생 속도는 해당 행동이 소유한다.
        bool blocked = _inputBlocked || IsGameplayPaused || IsCCed;
        Vector2 input = blocked ? Vector2.zero : moveInput;
        bool moving = !blocked && (input.sqrMagnitude > 0.0001f || _smoothedMoveInput.sqrMagnitude >= 0.0004f);
        if (moving)
        {
            TransitionToState(walkState);
            float speed = stat != null ? stat.MOVESPEED * SpeedMultiplier : 5f;
            UpdateWalkAnimSpeed(_smoothedMoveInput.magnitude * speed);
        }
        else
        {
            ResetWalkAnimSpeed();
            TransitionToState(idleState);
        }
        // 감속 벡터가 이전 방향을 가리켜도 새 입력 방향으로 즉시 돌아본다.
        if (input.x > 0.1f) transform.localScale = new Vector3(-1f, transform.localScale.y, transform.localScale.z);
        else if (input.x < -0.1f) transform.localScale = new Vector3(1f, transform.localScale.y, transform.localScale.z);
    }

    /// <summary>대시 종료 후 현재 누른 방향으로 걷기를 이어간다. 대시 속도/옛 평타 전진은 넘기지 않는다.</summary>
    public void ResumeLocomotionAfterDash()
    {
        _isAttackDashing = false;
        _attackDashTimeLeft = 0f;
        StopLocomotion();
        if (_inputBlocked || IsGameplayPaused || IsCCed || (stat != null && stat.IsDead)) return;
        visualSquash?.PlayLand();
        _smoothedMoveInput = Vector2.ClampMagnitude(moveInput, 1f);
        MoveDirection = _smoothedMoveInput;
        if (_rb != null && stat != null) _rb.linearVelocity = _smoothedMoveInput * stat.MOVESPEED * SpeedMultiplier;
        UpdateLocomotionAnimation();
    }

    private void CheckForInteractable()
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, interactRange, interactableLayer);

        IInteractable nearest = null;
        Collider2D nearestCollider = null;
        float minDist = float.MaxValue;

        foreach (var col in colliders)
        {
            if (col.TryGetComponent<IInteractable>(out var interactable))
            {
                float dist = Vector2.SqrMagnitude(col.transform.position - transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = interactable;
                    nearestCollider = col;
                }
            }
        }

        // 포커스 변경 시 OnFocused / OnLostFocus 호출
        if (!ReferenceEquals(nearest, _closestInteractable))
        {
            _closestInteractable?.OnLostFocus(gameObject);
            nearest?.OnFocused(gameObject);
            _closestInteractable = nearest;

            // 누르고 있는 채로 대상에서 멀어졌으면 홀드는 취소된다.
            if (_holdTarget != null && !ReferenceEquals(nearest, _holdTarget)) CancelInteractHold();
        }

        // 포커스 처리(툴팁 켜기) 뒤에 배치해야 첫 프레임부터 툴팁 위에 F가 붙는다.
        // 대상에 툴팁 같은 월드 UI가 있으면 그 위에, 없으면 콜라이더 위에 뜬다(PopupSystem 참고).
        PopupSystem.ShowInteractionIcon(nearestCollider, interactIconOffset, uiIconGap);
    }

    // ── F 홀드 ────────────────────────────────────────────────────────
    /// <summary>홀드 타이머를 굴린다. 다 차면 OnHoldComplete 를 부르고 그 입력을 소모 처리한다.</summary>
    private void TickInteractHold()
    {
        if (_holdTarget == null || _holdConsumed) return;

        float need = _holdTarget.HoldSeconds;
        if (need <= 0f) { CancelInteractHold(); return; }

        _holdTimer += Time.deltaTime;
        _holdTarget.OnHoldProgress(Mathf.Clamp01(_holdTimer / need));

        if (_holdTimer >= need)
        {
            _holdConsumed = true;              // 손을 떼도 Interact 가 안 불리게
            var target = _holdTarget;
            _holdTarget = null;
            _holdTimer = 0f;
            target.OnHoldProgress(0f);
            target.OnHoldComplete(gameObject);
        }
    }

    /// <summary>진행 중인 홀드를 취소하고 게이지를 되돌린다.</summary>
    private void CancelInteractHold()
    {
        _holdTarget?.OnHoldProgress(0f);
        _holdTarget = null;
        _holdTimer = 0f;
    }

    public enum SpeedModifierSource
    {
        [UnityEngine.InspectorName("근접 공격")] MeleeAttack,
        [UnityEngine.InspectorName("패링")] Parry,
        [UnityEngine.InspectorName("스킬")] Skill,
        [UnityEngine.InspectorName("디버프")] Debuff
    }

    private Dictionary<SpeedModifierSource, float> _speedModifiers = new Dictionary<SpeedModifierSource, float>();

    public void SetSpeedModifier(SpeedModifierSource source, float multiplier)
    {
        _speedModifiers[source] = multiplier;
        if (multiplier <= 0f) StopLocomotion();
    }

    public void RemoveSpeedModifier(SpeedModifierSource source)
    {
        if (_speedModifiers.ContainsKey(source))
        {
            _speedModifiers.Remove(source);
        }
    }

    public float SpeedMultiplier
    {
        get
        {
            float total = 1.0f;
            foreach (var mult in _speedModifiers.Values)
            {
                total *= mult;
            }
            return total;
        }
    }

    private void FixedUpdate()
    {
        if (_inputBlocked || (stat != null && stat.Health.IsDead)) { StopLocomotion(); return; }

        float currentSpeed = stat.MOVESPEED * SpeedMultiplier;

        // [수정] MeleeDodgeController(2스택 구르기) 시전 중인 런타임 상태도 대시로 안전 통합 감지
        var dodgeCtrl = GetComponent<MeleeDodgeController>();
        bool dodgeControllerActive = dodgeCtrl != null && dodgeCtrl.IsDashing;

        if (_isDashing)
        {
            _dashElapsed += Time.fixedDeltaTime;
            bool finished = AdvanceDash(_dashStartPosition, _dashEndPosition, _dashElapsed, _dashTravelDuration, ref _dashProgress);
            _dashTimeLeft -= Time.fixedDeltaTime;
            if (finished || _dashTimeLeft <= 0f)
            {
                EndDash();
            }
        }
        else if (dodgeControllerActive)
        {
            // 버그 수정(v1.4): 예전엔 이 상태도 activeDash 하나로 묶여서 위 EndDash() 분기가 그대로 실행됐습니다.
            // 이때 _dashTimeLeft(PlayerController 자신의 필드)는 초기화된 적이 없어 0에서 시작하므로,
            // 매 FixedUpdate마다 조건을 만족해 EndDash()가 즉시 호출되며 MeleeDodgeController가 방금 켠
            // 무적(Invincible)과 Player_Dash 레이어를 같은 프레임에 바로 꺼버렸습니다. 그 결과 대쉬 무적시간이
            // 사실상 0에 가까워져 "회피해도 맞는" 현상의 원인이었습니다. MeleeDodgeController는 자신의
            // FixedUpdate에서 속도/무적/레이어를 전부 스스로 관리하므로, 여기서는 일반 이동 로직만 건너뜁니다.
        }
        else if (_isAttackDashing)
        {
            AdvanceAttackDash();
        }
        else
        {
            // [개선] 물리 충돌과 자연스러운 관성을 위해 transform.position 대신 linearVelocity를 사용합니다.
            // 넉백(200 이상) 중일 때는 속도를 덮어쓰지 않아 넉백 효과를 유지합니다.
            if (_rb != null && _rb.linearVelocity.sqrMagnitude < 200f)
            {
                if (IsGameplayPaused || IsCCed || currentSpeed <= 0f)
                {
                    StopLocomotion();
                    return;
                }
                AdvanceLocomotion(moveInput, Time.fixedDeltaTime);
                _rb.linearVelocity = MoveDirection * currentSpeed;
            }
        }
    }

    /// <summary>
    /// 곡선의 이번 물리 틱 이동량을 속도로 전달한다. 무적/대시 레이어 전환은 하지 않는다.
    /// </summary>
    private void AdvanceAttackDash()
    {
        // 마지막 이동 틱은 물리 시뮬레이션까지 살린 뒤 다음 틱에 정지한다.
        if (_attackDashTimeLeft <= 0f)
        {
            _isAttackDashing = false;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;
            return;
        }
        _attackDashTimeLeft = Mathf.Max(0f, _attackDashTimeLeft - Time.fixedDeltaTime);
        float progress = Mathf.Max(_attackDashProgress, SkillCombatUtil.EvaluateMotionProgress(
            attackDashMovementCurve, 1f - _attackDashTimeLeft / attackDashDuration));
        float distance = _attackDashSpeedVal * attackDashDuration * (progress - _attackDashProgress);
        _attackDashProgress = progress;
        // 가파른 그래프라도 물리 한 틱에 벽을 넘어가지 않게 같은 경로 검사를 사용한다.
        Vector2 from = _rb.position;
        Vector2 next = SkillCombatUtil.ClampToWall(from, _attackDashDir, distance, DashRadius);
        _rb.linearVelocity = (next - from) / Time.fixedDeltaTime;
    }

    public void ApplyAttackDash(Vector2 direction, float forceMultiplier = 1.0f)
    {
        if (_inputBlocked || (stat != null && stat.Health.IsDead)) return;
        if (_isDashing) return; // 구르는 중(대시)에는 평타 대시 무시
        visualSquash?.PlayAttack();

        _isAttackDashing = true;
        _attackDashDir = direction.normalized;
        _attackDashSpeedVal = attackDashSpeed * forceMultiplier;
        _attackDashTimeLeft = attackDashDuration;
        _attackDashProgress = 0f;
    }

    public void SetDashLayer(bool isDash)
    {
        if (isDash)
        {
            gameObject.layer = _dashLayer;
        }
        else
        {
            RestoreDashLanding();
            gameObject.layer = _originalLayer;
        }
    }

    private void StartDash()
    {
        // [추가] 플레이어 평타만 캔슬(미니언 마무리는 남김 — R끼리만 회수)
        var meleeCtrl = GetComponent<MeleeCombatController>();
        if (meleeCtrl != null && meleeCtrl.IsAttacking)
        {
            meleeCtrl.CancelPlayerAttack();
        }


        _isDashing = true;
        _lastDashTime = Time.time;

        SetDashLayer(true);

        // 이동 입력이 있으면 그 방향으로, 없으면 현재 바라보는 방향(또는 우측)으로 대쉬
        _dashDir = moveInput.normalized;
        if (_dashDir == Vector2.zero)
        {
            _dashDir = new Vector2(-transform.localScale.x, 0).normalized; // x scale이 -1이면 오른쪽
        }

        // [추가] Unsteppable 안전 체크 및 대시 도달 범위 축소
        float originalDist = dashSpeed * dashDuration * (stat != null ? stat.DASH_DISTANCE_MULTIPLIER : 1f);
        Vector2 start = _rb.position;
        if (MapGenerator.Instance != null && MapGenerator.Instance.HasUnsteppableBetween(start, _dashDir, originalDist))
            originalDist *= 1.5f; // 기본 회피 경로도 물을 넘을 때만 연장. Wall 제한은 그대로 유지.
        Vector2 safePos = GetSafeDashPosition(start, _dashDir, originalDist);
        _dashEndPosition = safePos;
        float actualDist = Vector2.Distance(start, safePos);
        if (actualDist > 0.0001f) _dashDir = (safePos - start).normalized;
        _dashStartPosition = _rb.position;
        _dashTravelDuration = actualDist / Mathf.Max(0.01f, dashSpeed);
        _dashElapsed = _dashProgress = 0f;
        _dashTimeLeft = _dashTravelDuration + 0.1f; // 물리 충돌로 이동 불가할 때의 안전 종료

        if (stat != null && stat.Health != null)
        {
            stat.Health.Invincible = true; // 대쉬 무적 시작
        }

        // TODO: 대쉬 애니메이션 재생 트리거
        // BodyAnimator.Play("Dash");
    }

    private void EndDash()
    {
        _isDashing = false;

        SetDashLayer(false);

        ResumeLocomotionAfterDash();

        if (stat != null && stat.Health != null)
        {
            stat.Health.Invincible = false; // 대쉬 무적 종료
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (_inputBlocked || stat.Health.IsDead || IsCCed) { moveInput = Vector2.zero; return; }

        if (context.performed || context.canceled)
        {
            moveInput = context.ReadValue<Vector2>();
        }
        return;
    }

    public void OnParry(InputAction.CallbackContext context)
    {
        var parryCtrl = GetComponent<PlayerParryController>();
        // UI/일시정지 중에 뗀 입력도 반드시 해제한다.
        if (context.canceled) { parryCtrl?.SetGuardHeld(false); return; }
        if (IsGameplayPaused) return; // 옵션 일시정지 / 창(보상·맵 등)이 떠 있으면 차단
        if (_inputBlocked || stat.Health.IsDead || PouchUI.IsOpen || (SkillExplainUI.Instance != null && SkillExplainUI.Instance.IsPointerOverPanel())) return;
        if (context.started) parryCtrl?.SetGuardHeld(true);
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (IsGameplayPaused) return; // 옵션 일시정지 / 창(보상·맵 등)이 떠 있으면 차단
        if (_inputBlocked || stat.Health.IsDead || IsCCed) return;

        if (context.performed)
        {
            // 유지형 가드는 내리고 대쉬한다.
            var parryCtrl = GetComponent<PlayerParryController>();
            if (parryCtrl != null && !parryCtrl.TryInterruptForAction()) return;

            // 근접 구르기 컨트롤러가 있다면 우선적으로 사용 (2스택 구르기 등)
            MeleeDodgeController dodgeController = GetComponent<MeleeDodgeController>();
            if (dodgeController != null)
            {
                if (dodgeController.TryDash(moveInput, transform.localScale.x)) visualSquash?.PlayDash();
                return;
            }

            // 없다면 기존 1스택 기본 구르기 사용
            if (!_isDashing && Time.time >= _lastDashTime + DashCooldown)
            {
                StartDash();
                visualSquash?.PlayDash();
            }
        }
    }

    public void OnMinionSkill(InputAction.CallbackContext context)
    {
        if (IsGameplayPaused) return; // 옵션 일시정지 / 창(보상·맵 등)이 떠 있으면 차단
        if (_inputBlocked || stat.Health.IsDead || IsCCed) return;

        if (context.performed)
        {
            // 유지형 가드는 내리고 소환수 스킬을 사용한다.
            var parryCtrl = GetComponent<PlayerParryController>();
            if (parryCtrl != null && !parryCtrl.TryInterruptForAction()) return;

            var skillCtrl = GetComponent<PlayerSkillController>();
            if (skillCtrl != null)
            {
                // Space = 장착 미니언 액티브.
                skillCtrl.ExecuteMinionSkill(transform);
            }
        }
    }

    /// <summary>
    /// F 키. 짧게 누르면 Interact, IHoldInteractable 대상을 길게 누르면 OnHoldComplete.
    ///
    /// [버그 수정] 예전엔 조건이 `_inputBlocked || context.performed && ...` 였다. || 가 &amp;&amp; 보다
    /// 늦게 묶여서, 입력이 차단된 상태(컷신/보상창 등)면 무조건 본문에 들어가 상호작용이 되고
    /// _closestInteractable 이 null 이면 NullReference 까지 났다. 의도는 `!차단 && performed` 였다.
    /// </summary>
    public void OnInteract(InputAction.CallbackContext context) // [추가]
    {
        if (_inputBlocked || IsGameplayPaused || PouchUI.IsOpen || (SkillExplainUI.Instance != null && SkillExplainUI.Instance.IsOpen)) { CancelInteractHold(); return; }

        if (context.performed)
        {
            // 누른 순간: 홀드 가능한 대상이면 타이머를 돌리기 시작한다.
            _holdConsumed = false;
            _holdTimer = 0f;
            _holdTarget = (_closestInteractable is IHoldInteractable h && h.HoldSeconds > 0f) ? h : null;

            // 홀드 대상이 아니면 예전처럼 누른 즉시 실행한다(상점 NPC·문 등 반응이 밀리면 안 되는 것들).
            if (_holdTarget == null) _closestInteractable?.Interact(gameObject);
        }
        else if (context.canceled)
        {
            // 뗀 순간: 홀드가 끝까지 안 찼으면 '짧게 누름' = 평소 Interact.
            bool wasHolding = _holdTarget != null;
            CancelInteractHold();
            if (wasHolding && !_holdConsumed) _closestInteractable?.Interact(gameObject);
            _holdConsumed = false;
        }
    }

    /// <summary>
    /// B 키. 누르고 있는 동안만 아이템 주머니가 떠 있다(시간 정지 없음).
    /// 죽었거나 입력이 차단된 상태에선 강제로 닫는다 — 누른 채로 죽으면 패널이 남는다.
    /// </summary>
    public void OnPouch(InputAction.CallbackContext context)
    {
        // B 바인딩 폐지. 이전 씬의 직렬화된 이벤트 참조만 호환한다.
    }

public void OnGemTree(InputAction.CallbackContext context)
    {
        // V키. Input Actions 의 액션 이름이 아직 "GemTree" 라서 메서드 이름도 그대로 두었다
        // (프리팹의 PlayerInput 이벤트 바인딩이 메서드 이름 문자열로 걸려 있어 바꾸면 끊긴다).
        // 젬 시스템은 삭제됐고 이 키는 SkillExplainUI 전용이다.
        if (_inputBlocked || stat.Health.IsDead) return;

        if (context.performed)
        {
            CancelInteractHold();
            PouchUI.Instance?.SetOpen(!PouchUI.IsOpen);
        }
    }

    public void OnHandSlot(InputAction.CallbackContext context)
    {
        if (_inputBlocked || stat.Health.IsDead) return;

        // [수정] 탭 키를 눌러 현재 장착된 미니언/능력을 상시 조회합니다.
        if (context.performed)
        {
            CancelInteractHold();
            SkillExplainUI.Instance?.Toggle();
        }
    }

    /// <summary>
    /// ESC = 뒤로 가기.
    ///   1) 옵션이 열려 있으면 옵션을 닫는다(게임 재개).
    ///   2) 맨 위 창이 ESC 로 닫을 수 있는 창(주머니/장착 정보/맵)이면 그 창만 닫는다.
    ///   3) 아니면 옵션을 연다(게임 정지). 보상창·대화처럼 ESC 로 안 닫히는 창은 그대로 둔 채 위에 뜬다.
    /// </summary>
    public void OnOption(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        var som = SceneOptionManager.Instance;
        if (som == null)
        {
            Debug.LogError("<color=red>[PlayerController]</color> SceneOptionManager.Instance is NULL!");
            return;
        }

        if (som.isOptionOpen) { som.CloseOptionScene(); return; }

        var mgr = UIPopUpManager.Instance;
        if (mgr != null && mgr.CloseTopByEscape()) return;

        som.OpenOptionScene();
    }

    /// <summary>
    /// 현재 전투가 진행 중인지 확인합니다. 전투 상태는 IRoomEvent 들이 플레이어 상태(P_State)로
    /// 반영하므로 그 값을 그대로 사용합니다.
    /// </summary>
    private bool IsAnyBattleActive()
    {
        return P_State == PlayerStates.Battle;
    }

    // [주석 처리] 수동 소환 입력 제거
    // public void OnNum1(InputAction.CallbackContext context) { if (stat.Health.IsDead) return; sumController.OnNumKey(1, context); }
    // public void OnNum2(InputAction.CallbackContext context) { if (stat.Health.IsDead) return; sumController.OnNumKey(2, context); }
    // public void OnNum3(InputAction.CallbackContext context) { if (stat.Health.IsDead) return; sumController.OnNumKey(3, context); }
    // public void OnNum4(InputAction.CallbackContext context) { if (stat.Health.IsDead) return; sumController.OnNumKey(4, context); }

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


    public Vector2 GetSafeDashPosition(Vector2 startPos, Vector2 direction, float maxDistance)
    {
        _lastSafeDashPosition = startPos;
        _dashLandingTracked = true;
        Vector2 offset = DashBodyOffset;
        return SkillCombatUtil.GetSafeBoxDestination(startPos + offset, direction, maxDistance, DashBodySize) - offset;
    }

    // bounds.center는 물리 위치, transform.position은 보간된 표시 위치일 수 있다.
    // 둘을 빼면 FixedUpdate/강제 이동 직후 발 오프셋이 흔들리므로 로컬 콜라이더 오프셋을 사용한다.
    private Vector2 DashBodyOffset => _dashBody != null ? (Vector2)transform.TransformVector(_dashBody.offset) : Vector2.zero;
    private Vector2 DashBodySize => _dashBody != null ? (Vector2)_dashBody.bounds.size : Vector2.one * .4f;

    private void TrackDashLanding(Vector2 position)
    {
        if (SkillCombatUtil.IsSafeBoxLanding(position + DashBodyOffset, DashBodySize))
            _lastSafeDashPosition = position;
    }

    private void RestoreDashLanding()
    {
        if (!_dashLandingTracked || _rb == null) return;
        _dashLandingTracked = false;
        if (SkillCombatUtil.IsSafeBoxLanding(_rb.position + DashBodyOffset, DashBodySize)) return;
        // 물 위에서 중단되면 이미 지나온 안전 지점으로 복귀한 뒤 일반 충돌을 복원한다.
        Vector2 safe = _lastSafeDashPosition;
        if (!SkillCombatUtil.IsSafeBoxLanding(safe + DashBodyOffset, DashBodySize))
        {
            // 출발점부터 물가에 붙어 있었다면 가까운 땅 안쪽으로 밀어낸다.
            bool found = false;
            for (float r = .025f; r <= 1f && !found; r += .025f)
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    Vector2 candidate = safe + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                    if (!SkillCombatUtil.IsSafeBoxLanding(candidate + DashBodyOffset, DashBodySize)) continue;
                    Vector2 checkedPoint = SkillCombatUtil.ClampBoxToWall(safe + DashBodyOffset, candidate - safe, DashBodySize);
                    if (Vector2.Distance(checkedPoint, candidate + DashBodyOffset) > .001f) continue;
                    safe = candidate; found = true; break;
                }
        }
        _rb.position = safe;
        transform.position = new Vector3(safe.x, safe.y, transform.position.z);
        _rb.linearVelocity = Vector2.zero;
    }

    private float DashRadius
    {
        get
        {
            Collider2D body = GetComponent<Collider2D>();
            return body != null
                ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.y) + 0.02f
                : 0.3f;
        }
    }

    /// <summary>두 대쉬 컨트롤러 공용. 물리 한 틱의 이동도 Wall/최종 착지점을 넘지 않게 제한한다.</summary>
    public bool AdvanceDash(Vector2 destination, float speed)
    {
        if (_rb == null) return true;
        Vector2 from = _rb.position;
        TrackDashLanding(from);
        Vector2 next = Vector2.MoveTowards(from, destination, Mathf.Max(0f, speed) * Time.fixedDeltaTime);
        Vector2 step = next - from;
        next = SkillCombatUtil.ClampBoxToWall(from + DashBodyOffset, step, DashBodySize) - DashBodyOffset;
        _rb.linearVelocity = (next - from) / Time.fixedDeltaTime;
        return (next - from).sqrMagnitude < 0.000001f;
    }

    /// <summary>곡선 대시. 평평한 구간은 정지/종료가 아니라 대기이며, 실제 벽에 막혔을 때만 조기 종료한다.</summary>
    public bool AdvanceDash(Vector2 start, Vector2 destination, float elapsed, float duration, ref float progress)
    {
        if (_rb == null) return true;
        progress = Mathf.Max(progress, SkillCombatUtil.EvaluateMotionProgress(
            dashMovementCurve, duration > 0f ? elapsed / duration : 1f));
        Vector2 from = _rb.position;
        TrackDashLanding(from);
        Vector2 next = Vector2.Lerp(start, destination, progress);
        Vector2 step = next - from;
        Vector2 allowed = SkillCombatUtil.ClampBoxToWall(from + DashBodyOffset, step, DashBodySize) - DashBodyOffset;
        _rb.linearVelocity = (allowed - from) / Time.fixedDeltaTime;
        bool blocked = step.sqrMagnitude > 0.000001f && (allowed - from).sqrMagnitude < 0.000001f;
        bool arrived = progress >= 1f && (destination - from).sqrMagnitude < 0.000001f;
        return blocked || arrived;
    }
}
