using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerStates
{
    Idle,
    Battle
}

public class PlayerController : MonoBehaviour
{
    [Header("플레이어 스탯")]
    [SerializeField] CharacterStat stat;
    public CharacterStat Stat => stat;
    [HideInInspector]
    [SerializeField] private PlayerStamina staminaSystem;
    public PlayerStamina STAMINA => staminaSystem;
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
    [SerializeField] PlayerAnimationState currentAnimState;

    private bool _inputBlocked = false; // [추가] 맵 생성 중 입력 차단용

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
        if (blocked) PopupSystem.HideInteractionIcon();
        if (blocked)
        {
            moveInput = Vector2.zero;
            MoveDirection = Vector3.zero;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;
        }
        Debug.Log($"<color=yellow>[Player]</color> Input Blocked: {blocked}");
    }

    [Header("이동 변수")]
    [SerializeField] Vector3 MoveDirection = Vector3.zero;
    [SerializeField] Vector2 moveInput = Vector2.zero;
    public Vector2 MoveInput => moveInput;

    [Header("조작감 설정")]
    [SerializeField] private float movementSmoothTime = 0.15f;
    private Vector2 _smoothedMoveInput;
    private Vector2 _moveInputVelocity;

    [Header("구르기(대쉬) 설정")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 1.0f;
    private bool _isDashing = false;
    private float _dashTimeLeft;
    private Vector2 _dashEndPosition;
    private float _lastDashTime;
    private Vector2 _dashDir;
    private int _originalLayer;
    private int _dashLayer;

    [Header("평타 전진 가속 설정")]
    [SerializeField] private float attackDashSpeed = 10f; // 평타 공격 시 순간 대시 속도
    [SerializeField] private float attackDashDuration = 0.08f; // 평타 대시 지속 시간 (절도 있는 느낌용 극단적 짧은 시간)
    private bool _isAttackDashing = false;
    private float _attackDashTimeLeft = 0f;
    private Vector2 _attackDashDir = Vector2.zero;
    private float _attackDashSpeedVal = 10f;

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

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();

        _originalLayer = gameObject.layer;
        _dashLayer = Layers.PlayerDash;
        if (_dashLayer == -1) 
        {
            Debug.LogWarning("[PlayerController] 'Player_Dash' 레이어가 설정되어 있지 않습니다! 레이어 세팅 가이드를 확인해주세요.");
            _dashLayer = _originalLayer;
        }

        if (staminaSystem == null)
        {
            staminaSystem = GetComponent<PlayerStamina>();
            if (staminaSystem == null) staminaSystem = gameObject.AddComponent<PlayerStamina>();
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

    private void HandleDamageTaken(float damage)
    {
        if (damage > 0) RecordCombatAction(); // 피격 시 전투 상태 갱신

        // 데미지를 받았을 때 1초 무적 & 깜빡임 처리
        if (damage > 0)
        {
            if (_invincibilityCoroutine != null) StopCoroutine(_invincibilityCoroutine);
            _invincibilityCoroutine = StartCoroutine(InvincibilitySequence());
        }
    }

    private System.Collections.IEnumerator InvincibilitySequence()
    {
        if (stat == null || stat.Health == null) yield break;

        stat.Health.Invincible = true;

        SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>();
        float elapsed = 0f;
        float duration = invincibilityDuration;
        float blinkInterval = invincibilityBlinkInterval;
        bool isVisible = true;

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
            yield return new WaitForSeconds(blinkInterval);
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
        if (stat != null && stat.Health != null && stat.Health.IsDead) return;

        // 소환수 액티브는 Space(OnMinionSkill)가 담당한다.

        if (_inputBlocked) return;

        if (canChangeState)
        {
            if (moveInput.sqrMagnitude < 0.0001f)
            {
                ResetWalkAnimSpeed();
                TransitionToState(idleState);
            }
            else
            {
                TransitionToState(walkState);
            }

            // 이동 관련
            if (MoveDirection.x > 0.1f)
                this.transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
            else if (MoveDirection.x < -0.1f)
                this.transform.localScale = new Vector3(1, transform.localScale.y, transform.localScale.z);
        }

        CheckForInteractable(); // [추가]
        TickInteractHold();

        // --- 구르기(대쉬) 입력 처리 (하드코딩 제거) ---
        // OnDash(InputAction.CallbackContext context) 콜백에서 처리합니다.

        if (!_isDashing)
        {
            float actualSmoothTime;
            // 입력이 있을 때(가속/방향전환)는 무겁지 않게 아주 빠릿하게 반응
            if (moveInput.sqrMagnitude > 0.01f)
            {
                actualSmoothTime = 0.02f;
            }
            // 키보드에서 손을 뗐을 때(감속)만 미끄러지도록 관성 적용
            else
            {
                actualSmoothTime = Mathf.Max(movementSmoothTime, 0.08f);
            }

            _smoothedMoveInput = Vector2.SmoothDamp(_smoothedMoveInput, moveInput, ref _moveInputVelocity, actualSmoothTime, Mathf.Infinity, Time.deltaTime);
            MoveDirection = _smoothedMoveInput;
        }
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

        // 진열 상품은 포커스될 때 이름/가격 툴팁이 이미 뜬다. 구매 대상 선택은 유지하되 F 아이콘만 숨긴다.
        PopupSystem.ShowInteractionIcon(nearest is SellItem ? null : nearestCollider, interactIconOffset);

        // 포커스 변경 시 OnFocused / OnLostFocus 호출
        if (!ReferenceEquals(nearest, _closestInteractable))
        {
            _closestInteractable?.OnLostFocus(gameObject);
            nearest?.OnFocused(gameObject);
            _closestInteractable = nearest;

            // 누르고 있는 채로 대상에서 멀어졌으면 홀드는 취소된다.
            if (_holdTarget != null && !ReferenceEquals(nearest, _holdTarget)) CancelInteractHold();
        }
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
        MeleeAttack,
        Parry,
        Skill,
        Debuff
    }

    private Dictionary<SpeedModifierSource, float> _speedModifiers = new Dictionary<SpeedModifierSource, float>();

    public void SetSpeedModifier(SpeedModifierSource source, float multiplier)
    {
        _speedModifiers[source] = multiplier;
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
        if (_inputBlocked || (stat != null && stat.Health.IsDead)) return;

        float currentSpeed = stat.MOVESPEED * SpeedMultiplier;

        // [수정] MeleeDodgeController(2스택 구르기) 시전 중인 런타임 상태도 대시로 안전 통합 감지
        var dodgeCtrl = GetComponent<MeleeDodgeController>();
        bool dodgeControllerActive = dodgeCtrl != null && dodgeCtrl.IsDashing;

        if (_isDashing)
        {
            bool finished = AdvanceDash(_dashEndPosition, dashSpeed);
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
            // 평타 가속 전진 중에는 해당 속도를 덮어씌워 강제 이동
            _rb.linearVelocity = _attackDashDir * _attackDashSpeedVal;
            _attackDashTimeLeft -= Time.fixedDeltaTime;
            if (_attackDashTimeLeft <= 0f)
            {
                _isAttackDashing = false;
                if (_rb != null) _rb.linearVelocity = Vector2.zero; // [추가] 평타 돌진 완료 즉시 속도 강제 제동
            }
        }
        else
        {
            // [개선] 물리 충돌과 자연스러운 관성을 위해 transform.position 대신 linearVelocity를 사용합니다.
            // 넉백(200 이상) 중일 때는 속도를 덮어쓰지 않아 넉백 효과를 유지합니다.
            if (_rb != null && _rb.linearVelocity.sqrMagnitude < 200f)
            {
                _rb.linearVelocity = MoveDirection * currentSpeed;

                // 공격(등) 애니매이션이 잠겨있는 동안(canChangeState == false)은
                // 이동속도 기반 애니메이터 속도 값이 SetAttackAnimSpeed()가 설정한 값을 덮어쓰지 않도록 건대넌다.
                // While an attack (or similar) animation lock is active, skip movement-speed-based
                // animator speed updates so they don't override SetAttackAnimSpeed().
                if (canChangeState)
                {
                    UpdateWalkAnimSpeed(currentSpeed);
                }

            }
        }
    }

    /// <summary>
    /// 평타 타격 순간에 절도 있는 짧은 전진(Lunging) 물리력을 방향키/조준선 방향으로 가합니다.
    /// 실제 대시(무적, 레이어 변경) 판정 없이 속도로만 순간 미끄러뜨립니다.
    /// </summary>
    public void ApplyAttackDash(Vector2 direction, float forceMultiplier = 1.0f)
    {
        if (_inputBlocked || (stat != null && stat.Health.IsDead)) return;
        if (_isDashing) return; // 구르는 중(대시)에는 평타 대시 무시

        _isAttackDashing = true;
        _attackDashDir = direction.normalized;
        _attackDashSpeedVal = attackDashSpeed * forceMultiplier;
        _attackDashTimeLeft = attackDashDuration;
    }

    public void SetDashLayer(bool isDash)
    {
        if (isDash)
        {
            gameObject.layer = _dashLayer;
        }
        else
        {
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
        float originalDist = dashSpeed * dashDuration;
        if (MapGenerator.Instance != null && MapGenerator.Instance.HasUnsteppableBetween(transform.position, _dashDir, originalDist))
            originalDist *= 1.5f; // 기본 회피 경로도 물을 넘을 때만 연장. Wall 제한은 그대로 유지.
        Vector2 safePos = GetSafeDashPosition(transform.position, _dashDir, originalDist);
        _dashEndPosition = safePos;
        float actualDist = Vector2.Distance(transform.position, safePos);
        if (actualDist > 0.0001f) _dashDir = (safePos - (Vector2)transform.position).normalized;
        _dashTimeLeft = actualDist / Mathf.Max(0.01f, dashSpeed) + 0.1f; // 물리 충돌로 이동 불가할 때의 안전 종료

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

        // 관성 초기화
        _smoothedMoveInput = Vector2.zero;
        _moveInputVelocity = Vector2.zero;
        if (_rb != null) _rb.linearVelocity = Vector2.zero; // 대쉬 끝나고 미끄러짐 방지

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
        if (Time.timeScale == 0f) return; // [추가] 시간 일시정지 중 차단
        if (_inputBlocked || stat.Health.IsDead) return;

        if (context.started)
        {
            var parryCtrl = GetComponent<PlayerParryController>();
            if (parryCtrl != null)
            {
                parryCtrl.TryStartParry();
            }
        }
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (Time.timeScale == 0f) return; // [추가] 시간 일시정지 중 차단
        if (_inputBlocked || stat.Health.IsDead || IsCCed) return;

        if (context.performed)
        {
            // 단발 가드의 판정/실패 후딜 중에는 대쉬로 취소하지 않는다.
            var parryCtrl = GetComponent<PlayerParryController>();
            if (parryCtrl != null && !parryCtrl.TryInterruptForAction()) return;

            // 근접 구르기 컨트롤러가 있다면 우선적으로 사용 (2스택 구르기 등)
            MeleeDodgeController dodgeController = GetComponent<MeleeDodgeController>();
            if (dodgeController != null)
            {
                dodgeController.TryDash(moveInput, transform.localScale.x);
                return;
            }

            // 없다면 기존 1스택 기본 구르기 사용
            if (!_isDashing && Time.time >= _lastDashTime + DashCooldown)
            {
                StartDash();
            }
        }
    }

    public void OnMinionSkill(InputAction.CallbackContext context)
    {
        if (Time.timeScale == 0f) return; // [추가] 시간 일시정지 중 차단
        if (_inputBlocked || stat.Health.IsDead || IsCCed) return;

        if (context.performed)
        {
            // 단발 가드의 판정/실패 후딜 중에는 소환수 스킬로 취소하지 않는다.
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
        if (_inputBlocked) { CancelInteractHold(); return; }

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
        if (PouchUI.Instance == null) return;

        bool blocked = _inputBlocked || (stat != null && stat.Health != null && stat.Health.IsDead);
        PouchUI.Instance.SetOpen(!blocked && context.performed);
    }

public void OnGemTree(InputAction.CallbackContext context)
    {
        // V키. Input Actions 의 액션 이름이 아직 "GemTree" 라서 메서드 이름도 그대로 두었다
        // (프리팹의 PlayerInput 이벤트 바인딩이 메서드 이름 문자열로 걸려 있어 바꾸면 끊긴다).
        // 젬 시스템은 삭제됐고 이 키는 SkillExplainUI 전용이다.
        if (_inputBlocked || stat.Health.IsDead) return;

        if (context.performed)
        {
            if (SkillExplainUI.Instance != null)
            {
                SkillExplainUI.Instance.Toggle();
            }
            else
            {
                Debug.LogError("<color=red>[PlayerController]</color> SkillExplainUI.Instance is NULL!");
            }
        }
    }

    public void OnHandSlot(InputAction.CallbackContext context)
    {
        if (_inputBlocked || stat.Health.IsDead) return;

        // [수정] 탭 키를 눌러 현재 장착된 미니언/능력을 상시 조회합니다.
        if (context.performed)
        {
            // [수정] 이미 열려 있는 상태라면 전투 중이라도 닫을 수 있게 허용
            bool isOpen = (HandSlotSelectionUI.Instance != null && HandSlotSelectionUI.Instance.IsOpen);

            if (!isOpen && IsAnyBattleActive())
            {
                Debug.Log("<color=orange>[UI]</color> 전투가 진행 중일 때는 인벤토리를 열 수 없습니다.");
                return;
            }

            if (HandSlotSelectionUI.Instance != null)
            {
                HandSlotSelectionUI.Instance.ToggleReadOnly();
            }
        }
    }

    public void OnOption(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            if (SceneOptionManager.Instance != null)
            {
                if (!SceneOptionManager.Instance.isOptionOpen)
                {
                    SceneOptionManager.Instance.OpenOptionScene();
                }
                else
                {
                    SceneOptionManager.Instance.CloseOptionScene();
                }
            }
            else
            {
                Debug.LogError("<color=red>[PlayerController]</color> SceneOptionManager.Instance is NULL!");
            }
        }
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
        float speedRatio = Mathf.Clamp(currentSpeed * 0.2f, 0.3f, 3f);

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
        _animStateLockTimeoutCoroutine = StartCoroutine(AnimStateLockTimeoutRoutine(maxLockDuration));
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
        return SkillCombatUtil.GetSafeDestination(startPos, direction, maxDistance, DashRadius);
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
        Vector2 next = Vector2.MoveTowards(from, destination, Mathf.Max(0f, speed) * Time.fixedDeltaTime);
        Vector2 step = next - from;
        next = SkillCombatUtil.ClampToWall(from, step, step.magnitude, DashRadius);
        _rb.linearVelocity = (next - from) / Time.fixedDeltaTime;
        return (next - from).sqrMagnitude < 0.000001f;
    }
}
