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
    [SerializeField] float throwRange;
    [HideInInspector] public float throwRangeBonus = 0f;
    public float THROWRANGE
    {
        get
        {
            float range = throwRange + throwRangeBonus;
            if (InventoryManager.Instance != null)
            {
                // [귀수의 힘] 0.5칸 증가 (스택 비례)
                range += InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.DemonHandPower) * 0.5f;
                // [다 내꺼야] 집어든 소환수 1마리당 기본 1칸 증가, 추가 노드당 0.4칸씩 추가 증가
                int allMineLevel = InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.AllMine);
                if (allMineLevel > 0)
                {
                    float multiplierPerHeld = 1.0f + (allMineLevel - 1) * 0.4f;
                    int heldCount = (throwController != null) ? throwController.HeldObjectsCount : 0;
                    range += heldCount * multiplierPerHeld;
                }
            }
            return range;
        }
    }
    [Header("아군 유닛 관련 매니저")]
    [SerializeField] AllyManager allyManager;
    [Header("던지기 컨트롤러")]
    [SerializeField] private ThrowController throwController;
    [SerializeField] private float throwChargeTime = 1.0f;
    public float ThrowChargeTime => throwChargeTime;

    [Header("액티브 스킬 매니저")]
    [SerializeField] private ActiveSkillManager activeSkillManager;
    public ActiveSkillManager ActiveSkillManager => activeSkillManager;

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

    [Header("던지기 배율 설정")]
    [SerializeField] private float minThrowChargeMultiplier = 1.0f;
    [SerializeField] private float maxThrowChargeMultiplier = 2.0f;

    [Header("투척 및 차징 모디파이어 (보석/시너지용)")]
    public float bonusThrowChargeTime = 0f;
    public float chargeEfficiencyMultiplier = 0f; // 기본 0 (보너스 퍼센트 합산, 예: +50% = 0.5f)

    [Header("Overcharge System (Closer Gem)")]
    public float overchargeTimeLimit = 0f; // 오버차지 허용 시간 (기본 0, 클로저 장착시 증가)
    public float bonusThrowEffectMultiplier = 0f; // 기본 0 (예: +25% = 0.25f)
    public float chargeMoveSpeedMultiplier = 0.5f; // 차징 중 이동속도 배율 (기본 0.5 = 50% 감소)

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
        [SerializeField] PlayerAnimationState currentAnimState;

    private bool _inputBlocked = false; // [추가] 맵 생성 중 입력 차단용

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

    public float GetThrowChargeMultiplier(float ratio)
    {
        return Mathf.Lerp(minThrowChargeMultiplier, maxThrowChargeMultiplier, ratio);
    }

    public void IncreaseMaxChargeMultiplier(float amount)
    {
        maxThrowChargeMultiplier += amount;
        Debug.Log($"<color=yellow>[Growth]</color> 최대 투척 배율 증가! 현재: {maxThrowChargeMultiplier}");
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

    public bool IsDashing => _isDashing;
    public float DashCooldown => dashCooldown;
    public float DashCooldownProgress => dashCooldown > 0f ? Mathf.Clamp01((Time.time - _lastDashTime) / dashCooldown) : 1f;

    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        
        _originalLayer = gameObject.layer;
        _dashLayer = LayerMask.NameToLayer("Player_Dash");
        if (_dashLayer == -1) 
        {
            Debug.LogWarning("[PlayerController] 'Player_Dash' 레이어가 설정되어 있지 않습니다! 레이어 세팅 가이드를 확인해주세요.");
            _dashLayer = _originalLayer;
        }

        if (throwController == null)
        {
            throwController = GetComponentInChildren<ThrowController>();
        }

        if (staminaSystem == null)
        {
            staminaSystem = GetComponent<PlayerStamina>();
            if (staminaSystem == null) staminaSystem = gameObject.AddComponent<PlayerStamina>();
        }

        // [유니크] 유니크 효과 전담 매니저 추가 (만약 인스펙터에서 안 달아뒀을 경우를 대비한 보험)
        if (GetComponent<PlayerUniqueEffectManager>() == null)
            gameObject.AddComponent<PlayerUniqueEffectManager>();

        // [패리] 패리 컨트롤러 추가
        if (GetComponent<PlayerParryController>() == null)
            gameObject.AddComponent<PlayerParryController>();

        // [액티브 스킬] 액티브 스킬 매니저 추가
        if (activeSkillManager == null)
        {
            activeSkillManager = gameObject.AddComponent<ActiveSkillManager>();
            activeSkillManager.Initialize(this);
        }

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
        // 데미지가 0보다 클 경우(실제 피해를 입었을 경우)에만 낙하
        if (damage > 0 && throwController != null)
        {
            RecordCombatAction(); // 피격 시 전투 상태 갱신

            // [시너지] 큰손 (BigHand) 3세트 이상일 경우 드롭 면역
            bool preventDrop = false;
            if (InventoryManager.Instance != null && InventoryManager.Instance.GetSynergyCount(GemSynergyGroup.BigHand) >= 3)
            {
                preventDrop = true;
            }

            if (!preventDrop)
            {
                throwController.DropAll();
            }
        }

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

        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;

            var parryCtrl = GetComponent<PlayerParryController>();
            bool isParrying = parryCtrl != null && parryCtrl.IsParrying;

            // PlayerSkillController를 통한 연계 스킬(스페이스바) 및 상시 스킬(Q, E, R) 처리
            var skillCtrl = GetComponent<PlayerSkillController>();
            if (skillCtrl != null && !_inputBlocked && !isParrying)
            {
                // 스페이스바: 큐에 대기 중인 미니언 스킬 발동
                if (kb.spaceKey.wasPressedThisFrame)
                {
                    skillCtrl.ExecuteNextMinionSkill(transform);
                }
            }
        }

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
            // 이미지 돌려주기
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
        ThrowCharge,
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

        if (throwController != null && throwController.IsCharging)
        {
            SetSpeedModifier(SpeedModifierSource.ThrowCharge, chargeMoveSpeedMultiplier);
        }
        else
        {
            RemoveSpeedModifier(SpeedModifierSource.ThrowCharge);
        }

        float currentSpeed = stat.MOVESPEED * SpeedMultiplier;

        if (_isDashing)
        {
            // 대쉬 중에는 물리 속도를 강제로 덮어써서 빠르게 이동
            _rb.linearVelocity = _dashDir * dashSpeed;
            _dashTimeLeft -= Time.fixedDeltaTime;

            if (_dashTimeLeft <= 0)
            {
                EndDash();
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
                // 이동속도 기반 애니타 속뗔 갑신이 SetAttackAnimSpeed()가 설정한 값을 덩어쓰지 않도록 건대넌다.
                // While an attack (or similar) animation lock is active, skip movement-speed-based
                // animator speed updates so they don't override SetAttackAnimSpeed().
                if (canChangeState)
                {
                    UpdateWalkAnimSpeed(currentSpeed);
                }

            }
        }
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
        if (throwController != null && throwController.IsCharging)
        {
            throwController.InputHandler.ResetCharging();
        }
        // [추가] 시전 중인 액티브 스킬 취소
        CancelActiveSkill();

        _isDashing = true;
        _dashTimeLeft = dashDuration;
        _lastDashTime = Time.time;

        SetDashLayer(true);

        // 이동 입력이 있으면 그 방향으로, 없으면 현재 바라보는 방향(또는 우측)으로 대쉬
        _dashDir = moveInput.normalized;
        if (_dashDir == Vector2.zero)
        {
            _dashDir = new Vector2(-transform.localScale.x, 0).normalized; // x scale이 -1이면 오른쪽
        }

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
        if (_inputBlocked || stat.Health.IsDead) { moveInput = Vector2.zero; return; }

        if (context.performed || context.canceled)
        {
            moveInput = context.ReadValue<Vector2>();
        }
        return;
    }

    public void OnParry(InputAction.CallbackContext context)
    {
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

    public void OnThrow(InputAction.CallbackContext context)
    {
        if (stat.Health.IsDead) return;

        // [추가] 스킬 시전 중 투척 차단
        if (IsCastingSkill) return;

        var parryCtrl = GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        if (activeSkillManager != null)
        {
            bool isAnySkillActive = (activeSkillManager.SkillSlot1 != null && activeSkillManager.SkillSlot1.IsActive) ||
                                    (activeSkillManager.SkillSlot2 != null && activeSkillManager.SkillSlot2.IsActive);

            if (isAnySkillActive)
            {
                if (context.started) activeSkillManager.HandleLeftClick();
                return; // 시즈 모드 등이 켜져 있으면 투척 이벤트를 완전히 삼킴
            }
        }

        if (_inputBlocked) return;

        if (throwController != null)
        {
            throwController.OnThrow(context);
        }
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (_inputBlocked || stat.Health.IsDead) return;

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
            if (!_isDashing && Time.time >= _lastDashTime + dashCooldown)
            {
                StartDash();
            }
        }
    }

    public void OnSkillQ(InputAction.CallbackContext context)
    {
        if (_inputBlocked || stat.Health.IsDead || IsCastingSkill) return;

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
        if (_inputBlocked || stat.Health.IsDead || IsCastingSkill) return;

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
        if (_inputBlocked || stat.Health.IsDead || IsCastingSkill) return;

        var parryCtrl = GetComponent<PlayerParryController>();
        if (parryCtrl != null && parryCtrl.IsParrying) return;

        if (context.performed)
        {
            var skillCtrl = GetComponent<PlayerSkillController>();
            if (skillCtrl != null)
            {
                skillCtrl.ExecutePlayerSkill(PlayerSkillController.SkillSlot.R, transform);
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
        if (_inputBlocked || stat.Health.IsDead) return;

        if (context.performed)
        {
            // [수정] 이미 열려 있는 상태라면 전투 중이라도 닫을 수 있게 허용
            bool isOpen = (GemTreeUI.Instance != null && GemTreeUI.Instance.IsOpen);

            if (!isOpen && IsAnyBattleActive())
            {
                Debug.Log("<color=orange>[UI]</color> 전투가 진행 중일 때는 보석 트리를 열 수 없습니다.");
                return;
            }

            if (GemTreeUI.Instance != null)
            {
                GemTreeUI.Instance.Toggle();
            }
            else
            {
                Debug.LogError("<color=red>[PlayerController]</color> GemTreeUI.Instance is NULL!");
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
    /// [추가] 현재 씬 내에서 활성화된 전투 이벤트(DynamicEnemySpawner)가 있는지 확인합니다.
    /// 사용자가 언급한 '임시 벽 생성' 로직과 동기화됩니다.
    /// </summary>
    private bool IsAnyBattleActive()
    {
        var spawners = UnityEngine.Object.FindObjectsByType<DynamicEnemySpawner>(FindObjectsSortMode.None);
        foreach (var spawner in spawners)
        {
            if (spawner.IsEventActive) return true;
        }
        return false;
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
            allyManager.SetBattleState(true);
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

    [Header("스킬 시전 시스템")]
    private Coroutine _activeSkillCoroutine;
    public bool IsCastingSkill => _activeSkillCoroutine != null;

    /// <summary>
    /// 플레이어 액티브 스킬 시전을 시작합니다.
    /// 시전 시간 동안 이속이 0.3배로 감소하며, 다른 행동(투척, 타스킬)이 차단됩니다.
    /// </summary>
    public void StartSkillCasting(System.Collections.IEnumerator skillRoutine)
    {
        CancelActiveSkill(); // 기존 시전 중인 스킬이 있다면 취소
        _activeSkillCoroutine = StartCoroutine(RunSkillRoutineWithCleanup(skillRoutine));
    }

    private System.Collections.IEnumerator RunSkillRoutineWithCleanup(System.Collections.IEnumerator skillRoutine)
    {
        SetSpeedModifier(SpeedModifierSource.Skill, 0.3f); // 스킬 시전 중 이속 감소 0.3배
        yield return StartCoroutine(skillRoutine);
        RemoveSpeedModifier(SpeedModifierSource.Skill); // 이속 복구
        _activeSkillCoroutine = null;
    }

    /// <summary>
    /// 현재 시전 중인 플레이어 액티브 스킬을 강제 취소합니다.
    /// </summary>
    public void CancelActiveSkill()
    {
        if (_activeSkillCoroutine != null)
        {
            StopCoroutine(_activeSkillCoroutine);
            _activeSkillCoroutine = null;
            RemoveSpeedModifier(SpeedModifierSource.Skill); // 이속 복구
            Debug.Log("<color=red>[Player]</color> Active skill cast canceled!");
        }
    }
}
