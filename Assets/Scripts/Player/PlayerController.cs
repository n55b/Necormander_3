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
    private IInteractable _closestInteractable;

    [Header("플레이어 애니메이터")]
    [SerializeField] Animator BodyAnimator;
    [Header("스킬 손 모션 애니메이터 (Hand 오브젝트)")]
    [SerializeField] Animator HandSkillAnimator;
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

        // [서브 소환수] 상시 패시브 적용 컨트롤러
        if (GetComponent<SubSummonPassiveController>() == null)
            gameObject.AddComponent<SubSummonPassiveController>();

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

        // 소환수 액티브는 이제 R키(OnSkillR)가 담당한다. 스페이스바 바인딩은 철거됨.

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
                }
            }
        }

        // 포커스 변경 시 OnFocused / OnLostFocus 호출
        if (!ReferenceEquals(nearest, _closestInteractable))
        {
            _closestInteractable?.OnLostFocus(gameObject);
            nearest?.OnFocused(gameObject);
            _closestInteractable = nearest;
        }
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
            // 대쉬 중에는 물리 속도를 강제로 덮어써서 빠르게 이동 (기존 1스택 구르기 전용 물리)
            _rb.linearVelocity = _dashDir * dashSpeed;
            _dashTimeLeft -= Time.fixedDeltaTime;

            if (_dashTimeLeft <= 0)
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
        // [추가] 진행 중인 근접 공격 및 투척 차징 캔슬
        var meleeCtrl = GetComponent<MeleeCombatController>();
        if (meleeCtrl != null && meleeCtrl.IsAttacking)
        {
            meleeCtrl.CancelAttack();
        }
        // [추가] 시전 중인 액티브 스킬 취소
        CancelActiveSkill();

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
        Vector2 safePos = GetSafeDashPosition(transform.position, _dashDir, originalDist);
        float actualDist = Vector2.Distance(transform.position, safePos);
        _dashTimeLeft = actualDist / dashSpeed; // 동적으로 대시 시간 조절

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

        var parryCtrl = GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        if (context.performed)
        {
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

    public void OnSkillQ(InputAction.CallbackContext context)
    {
        if (Time.timeScale == 0f) return; // [추가] 시간 일시정지 중 차단
        if (_inputBlocked || stat.Health.IsDead || IsCastingSkill || IsCCed) return;

        var parryCtrl = GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        if (context.performed)
        {
            var skillCtrl = GetComponent<PlayerSkillController>();
            if (skillCtrl != null)
            {
                skillCtrl.ExecutePlayerSkill(PlayerSkillController.SkillSlot.Q, transform);
            }
        }
    }

    public void OnSkillE(InputAction.CallbackContext context)
    {
        if (Time.timeScale == 0f) return; // [추가] 시간 일시정지 중 차단
        if (_inputBlocked || stat.Health.IsDead || IsCastingSkill || IsCCed) return;

        var parryCtrl = GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        if (context.performed)
        {
            var skillCtrl = GetComponent<PlayerSkillController>();
            if (skillCtrl != null)
            {
                skillCtrl.ExecutePlayerSkill(PlayerSkillController.SkillSlot.E, transform);
            }
        }
    }

    public void OnSkillR(InputAction.CallbackContext context)
    {
        if (Time.timeScale == 0f) return; // [추가] 시간 일시정지 중 차단
        if (_inputBlocked || stat.Health.IsDead || IsCastingSkill || IsCCed) return;

        var parryCtrl = GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        if (context.performed)
        {
            var skillCtrl = GetComponent<PlayerSkillController>();
            if (skillCtrl != null)
            {
                // R = 소환수 액티브(구 스페이스바). Q/E 는 플레이어 스킬, R 은 소환 스킬 전용.
                skillCtrl.ExecuteMinionSkill(transform);
            }
        }
    }

    public void OnInteract(InputAction.CallbackContext context) // [추가]
    {
        if (_inputBlocked || context.performed && _closestInteractable != null)
        {
            _closestInteractable.Interact(gameObject);
        }
    }

public void OnGemTree(InputAction.CallbackContext context)
    {
        // [수정] V키(Input Actions의 "GemTree" 액션)를 더 이상 GemTreeUI가 아닌
        // SkillExplainUI 토글용으로 재사용합니다. 메서드 이름은 PlayerInput 이벤트 바인딩이
        // 끊어지지 않도록 그대로 유지합니다. GemTreeUI는 별도의 키가 없는 상태로 남겨둡니다.
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


    public Vector2 CurrentSkillAimDir { get; private set; } = Vector2.right;

    private SpriteRenderer _handSpriteRenderer;
    private Sprite _defaultHandSprite;
    private bool _handSpriteCached = false;
    private Coroutine _handSkillDisableCoroutine;

    /// <summary>
    /// Hand 오브젝트의 전용 Animator(HandSkill.aseprite 기반)에서 지정된 이름의 스킬 손 모션을 재생합니다.
    /// Body의 Idle/Walk/Attack 애니메이션과는 완전히 독립적으로 동작하며, 평타와 동일한 방식으로
    /// 마우스 조준 방향 계산 + 플레이어 본체 반전 + canChangeState 잠금을 함께 처리합니다.
    /// </summary>
    public void PlayHandSkillAnim(string animName, float holdDurationOverride = -1f)
    {
        if (HandSkillAnimator == null || string.IsNullOrEmpty(animName)) return;

        // Hand의 기본(평상시) 스프라이트를 최초 1회만 캐싱해둠
        // (스킬 클립 마지막 프레임이 빈 스프라이트여도 재생 종료 후 이 값으로 복원)
        if (_handSpriteRenderer == null)
        {
            _handSpriteRenderer = HandSkillAnimator.GetComponent<SpriteRenderer>();
        }

        // [Fix] 애니메이터가 꺼져있는 상태(= 이전 스킬 모션이 끝난 뒤)일 때만 "기본 스프라이트"를 다시 캐싱합니다.
        if (!HandSkillAnimator.enabled && _handSpriteRenderer != null && _handSpriteRenderer.sprite != null)
        {
            _defaultHandSprite = _handSpriteRenderer.sprite;
        }
        // [Fix] 스킬 시작 시점에도 렌더러가 꺼져있을 수 있으므로 방어적으로 켜줍니다.
        if (_handSpriteRenderer != null) _handSpriteRenderer.enabled = true;
        _handSpriteCached = true;

        // 평타(MeleeCombatController.ExecuteMeleeAttack)와 동일한 방식으로 마우스 방향을 조준 방향으로 계산하고,
        // 같은 부호로 플레이어 본체(Body) 반전도 맞춥니다.
        if (Mouse.current != null && Camera.main != null)
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            mousePos.z = 0;
            Vector2 dir = ((Vector2)mousePos - (Vector2)transform.position).normalized;
            if (dir.sqrMagnitude > 0.0001f)
            {
                CurrentSkillAimDir = dir;

                if (dir.x > 0) transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
                else if (dir.x < 0) transform.localScale = new Vector3(1, transform.localScale.y, transform.localScale.z);
            }
        }

        int hash = Animator.StringToHash(animName);
        if (!HandSkillAnimator.HasState(0, hash))
        {
            Debug.LogWarning($"<color=orange>[PlayerController]</color> HandSkillAnimator에 '{animName}' 스테이트가 없습니다.");
            return;
        }

        float clipLength = GetHandSkillClipLength(animName);
        if (clipLength <= 0f) clipLength = 0.5f; // 클립을 못 찾았을 때의 안전 기본값

        // [Fix] 총난타(RapidPunch)처럼 실제 시전 시간이 손 애니메이션 클립 길이보다 긴 스킬은
        // 호출측에서 전체 시전 시간(holdDurationOverride)을 넘겨받아, 애니메이터가 스킬 도중에 꺼지지 않도록 합니다.
        if (holdDurationOverride > 0f) clipLength = Mathf.Max(clipLength, holdDurationOverride);

        // 평타와 동일하게, 재생 중에는 canChangeState를 잠가 이동 기반 반전이 개입하지 못하게 합니다.
        // HandSkill 클립에는 Animation Event가 없으므로, 타이머로 직접 풀어줍니다.
        LockAnimState(clipLength + 0.2f);

        HandSkillAnimator.enabled = true;
        HandSkillAnimator.Play(hash, 0, 0f);

        if (_handSkillDisableCoroutine != null) StopCoroutine(_handSkillDisableCoroutine);
        _handSkillDisableCoroutine = StartCoroutine(DisableHandSkillAnimatorAfter(clipLength));
    }

    /// <summary>
    /// HandSkillAnimator에 등록된 클립 중 이름이 일치하는 것의 길이(초)를 반환합니다. 없으면 0.
    /// 스킬 SO의 hitTimingRatio와 결합해 타격 타이밍을 계산할 때 쓰세요.
    /// </summary>
    public float GetHandSkillClipLength(string animName)
    {
        if (HandSkillAnimator == null || string.IsNullOrEmpty(animName)) return 0f;
        var controller = HandSkillAnimator.runtimeAnimatorController;
        if (controller == null) return 0f;

        foreach (var clip in controller.animationClips)
        {
            if (clip != null && clip.name == animName) return clip.length;
        }
        return 0f;
    }

    /// <summary>
    /// 스킬 손 모션 재생이 끝난 뒤, HandSkillAnimator를 다시 비활성화해 자동 반복/마지막 프레임 고정을 방지합니다.
    /// (Hand는 다시 Body 애니메이션이 제어하는 기본 손 스프라이트로 돌아감니다)
    /// </summary>
    private System.Collections.IEnumerator DisableHandSkillAnimatorAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (HandSkillAnimator != null) HandSkillAnimator.enabled = false;

        // 마지막 프레임이 빈 스프라이트였을 경우를 대비해 기본 스프라이트로 강제 복원
        // [Fix] Aseprite에서 임포트된 애니메이션 클립에는 SpriteRenderer.enabled를 직접 켜다 끔는 커브가 들어있을 수 있습니다.
        // 클립 재생이 끝나면 .sprite만 복원해서는 부족하고, 렉더러 자체를 반드시 다시 켜줘야 합니다.
        if (_handSpriteRenderer != null)
        {
            _handSpriteRenderer.enabled = true;
            if (_defaultHandSprite != null) _handSpriteRenderer.sprite = _defaultHandSprite;
        }

        CanChangeAnimState();
        _handSkillDisableCoroutine = null;
    }

    /// <summary>
    /// 현재 스킬 손 모션(HandSkillAnimator)이 재생 중인지 여부. 모든 스킬이 PlayHandSkillAnim()을 통해
    /// 공통적으로 이 값을 켜고 끄므로, IsCastingSkill(StartSkillCasting을 쓰는 스킬에만 해당)보다
    /// 더 일관적인 차단 조건입니다.
    /// </summary>
    public bool IsUsingHandSkill => HandSkillAnimator != null && HandSkillAnimator.enabled;

    [Header("스킬 시전 시스템")]
    private Coroutine _activeSkillCoroutine;
    private System.Action _activeSkillCleanup; // 종료/취소 시 반드시 1회 실행할 정리(무적 해제·입력 복구 등)
    public bool IsCastingSkill => _activeSkillCoroutine != null;

    /// <summary>
    /// 플레이어 액티브 스킬 시전을 시작합니다.
    /// 시전 시간 동안 이속이 0.3배로 감소하며, 다른 행동(투척, 타스킬)이 차단됩니다.
    /// cleanup: 정상 종료든 중간 취소(StopCoroutine)든 반드시 실행됩니다.
    /// </summary>
    public void StartSkillCasting(System.Collections.IEnumerator skillRoutine, System.Action cleanup = null)
    {
        CancelActiveSkill(); // 기존 시전 중인 스킬이 있다면 취소
        _activeSkillCleanup = cleanup;
        _activeSkillCoroutine = StartCoroutine(RunSkillRoutineWithCleanup(skillRoutine));
    }

    private System.Collections.IEnumerator RunSkillRoutineWithCleanup(System.Collections.IEnumerator skillRoutine)
    {
        SetSpeedModifier(SpeedModifierSource.Skill, 0.3f); // 스킬 시전 중 이속 감소 0.3배
        // 내부 루틴을 직접 구동한다. 별도 StartCoroutine으로 감싸면 StopCoroutine이 래퍼만 멈추고
        // 본체는 계속 도는 '고아 코루틴' 문제가 생기므로 MoveNext로 직접 돌린다.
        while (skillRoutine != null && skillRoutine.MoveNext())
            yield return skillRoutine.Current;
        FinishSkillCast();
    }

    /// <summary>
    /// 현재 시전 중인 플레이어 액티브 스킬을 강제 취소합니다.
    /// </summary>
    public void CancelActiveSkill()
    {
        if (_activeSkillCoroutine == null) return;
        StopCoroutine(_activeSkillCoroutine);
        FinishSkillCast(); // StopCoroutine은 finally를 실행하지 않으므로 여기서 명시적으로 정리
        Debug.Log("<color=red>[Player]</color> Active skill cast canceled!");
    }

    // 시전 종료(정상/취소 공통): 이속 복구 + 정리 델리게이트 1회 실행.
    private void FinishSkillCast()
    {
        RemoveSpeedModifier(SpeedModifierSource.Skill); // 이속 복구
        _activeSkillCoroutine = null;
        var cleanup = _activeSkillCleanup;
        _activeSkillCleanup = null;
        cleanup?.Invoke();
    }

    private void OnDisable()
    {
        // 비활성/파괴 직전, 시전 중이던 스킬의 정리를 보장한다(무적 해제·입력 복구 등).
        if (_activeSkillCoroutine != null) FinishSkillCast();
    }

    /// <summary>
    /// 대시 방향으로 Unsteppable(낭떠러지) 또는 벽이 있으면 걸치지 않고 안전하게 제동할 목적지 위치를 반환합니다.
    /// 실제 스캔 로직은 <see cref="SkillCombatUtil.GetSafeDestination"/> 로 일원화되어, 좌표 텔레포트로 이동하는
    /// 모든 스킬·넉백이 대시와 동일한 벽/낭떠러지 판정을 공유합니다.
    /// </summary>
    public Vector2 GetSafeDashPosition(Vector2 startPos, Vector2 direction, float maxDistance)
    {
        return SkillCombatUtil.GetSafeDestination(startPos, direction, maxDistance);
    }
}
