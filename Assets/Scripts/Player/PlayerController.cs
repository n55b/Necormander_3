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
    [SerializeField] Animator LHandAnimator;
    [SerializeField] Animator RHandAnimator;
    [SerializeField] PlayerAnimationState currentAnimState;

    private bool _inputBlocked = false; // [추가] 맵 생성 중 입력 차단용

    /// <summary>
    /// 애니메이션 캐싱 변수
    /// </summary>
    public IdleState idleState;
    public AttackState atkState;
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

    public bool IsDashing => _isDashing;

    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
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
        atkState = new AttackState(this);
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
    }

    private void Update()
    {
        if (stat != null && stat.Health != null && stat.Health.IsDead) return;

        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            
            // PlayerSkillController를 통한 연계 스킬(스페이스바) 및 상시 스킬(Q, E, R) 처리
            var skillCtrl = GetComponent<PlayerSkillController>();
            if (skillCtrl != null && !_inputBlocked)
            {
                // 스페이스바: 큐에 대기 중인 미니언 스킬 발동
                if (kb.spaceKey.wasPressedThisFrame)
                {
                    skillCtrl.ExecuteNextMinionSkill(transform);
                }

                // 평소: 플레이어 스킬 발동 (상시 스킬, Q, E, R)
                if (kb.qKey.wasPressedThisFrame) skillCtrl.ExecutePlayerSkill(PlayerSkillController.SkillSlot.Q, transform);
                if (kb.eKey.wasPressedThisFrame) skillCtrl.ExecutePlayerSkill(PlayerSkillController.SkillSlot.E, transform);
                if (kb.rKey.wasPressedThisFrame) skillCtrl.ExecutePlayerSkill(PlayerSkillController.SkillSlot.R, transform);
            }
        }

        if (_inputBlocked) return;

        if (canChangeState)
        {
            TransitionToState(idleState);

            // 이동 관련
            // 이미지 돌려주기
            if (MoveDirection.x > 0.0f)
                this.transform.localScale = new Vector3(-1, transform.localScale.y, transform.localScale.z);
            else if (MoveDirection.x < 0.0f)
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
                actualSmoothTime = Mathf.Max(movementSmoothTime, 0.15f);
            }

            _smoothedMoveInput = Vector2.SmoothDamp(_smoothedMoveInput, moveInput, ref _moveInputVelocity, actualSmoothTime, Mathf.Infinity, Time.deltaTime);
            MoveDirection = _smoothedMoveInput;
        }
    }

    private void CheckForInteractable() // [추가]
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, interactRange, interactableLayer);
        _closestInteractable = null;
        float closestDist = float.MaxValue;

        foreach (var col in colliders)
        {
            if (col.TryGetComponent<IInteractable>(out var interactable))
            {
                float dist = Vector2.Distance(transform.position, col.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    _closestInteractable = interactable;
                }
            }
        }

        // TODO: 여기에 가장 가까운 상호작용 오브젝트 위에 프롬프트(예: "Press Q")를 표시하는 UI 로직 추가 가능
    }

    // [추가] 외부(MeleeCombatController 등)에서 이동 속도를 비율로 줄이거나 늘리기 위한 변수
    [HideInInspector] public float SpeedMultiplier = 1.0f;

    private void FixedUpdate()
    {
        if (_inputBlocked || (stat != null && stat.Health.IsDead)) return;

        float currentSpeed = stat.MOVESPEED * SpeedMultiplier;

        if (throwController != null && throwController.IsCharging)
        {
            currentSpeed *= chargeMoveSpeedMultiplier;
        }

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
            }
        }
    }

    private void StartDash()
    {
        _isDashing = true;
        _dashTimeLeft = dashDuration;
        _lastDashTime = Time.time;
        
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

    public void OnRightClick(InputAction.CallbackContext context)
    {
        if (_inputBlocked || stat.Health.IsDead) return;

        /*
        if (sumController.IsSummoningMode)
        {
            if (context.performed)
            {
                Debug.Log($"<color=white>[PlayerController]</color> 우클릭 입력됨! 소환 수행");
                CommandData selectedType = sumController.GetCurrentSelectedType();
                MinionDataSO data = GameManager.Instance.dataManager.GetMinionData(selectedType);

                if (ReferenceEquals(data, null))
                {
                    sumController.ResetSummonMode();
                    return;
                }

                if (data.cost == 0)
                {
                    Debug.LogError($"<color=red>[PlayerController]</color> {data.minionName}의 소환 비용(Cost)이 0으로 설정되어 있습니다!");
                }

                int finalSummonCount = 1;
                Debug.Log($"<color=white>[Summon Request]</color> Type: {selectedType}, Count: {finalSummonCount} (Sync with Inventory)");

                List<Vector2> pos = sumController.GetSummonPositions2D(finalSummonCount, summonRange);

                for (int i = 0; i < finalSummonCount; i++)
                {
                    // [수정] 소환 시 인벤토리에서 해당 유닛의 수량을 늘리거나 새 슬롯을 차지합니다.
                    // 이는 나중에 "수량 늘리기 아이템"을 먹었을 때와 동일한 로직을 공유합니다.
                    bool success = GameManager.Instance.inventoryManager.AddMinionOrIncreaseQuantity(selectedType, 1);

                    if (success)
                    {
                        Vector2 spawnPos = (i < pos.Count) ? pos[i] : (Vector2)transform.position;
                        allyManager.SpawnAlly(data, spawnPos);
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[PlayerController]</color> 인벤토리 제한으로 {selectedType}을 더 이상 소환할 수 없습니다.");
                        break;
                    }
                }

                sumController.ResetSummonMode();
            }
        }
        else
        */
        {
            if (throwController != null)
            {
                if (context.started)
                {
                    throwController.OnRightClickStarted();
                }
                else if (context.canceled)
                {
                    throwController.OnRightClickCanceled();
                }
            }
        }
    }

    public void OnThrow(InputAction.CallbackContext context)
    {
        if (stat.Health.IsDead) return;

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
    /// 애니메이션용 스테이트 변화 함수들
    /// </summary>
    /// <param name="newState"></param>
    /// <param name="animName"></param>
    public void TransitionToState(PlayerAnimationState newState)
    {
        if (currentAnimState == newState) return;

        currentAnimState?.Exit();

        currentAnimState = newState;

        currentAnimState.Enter();
    }

    public void CanChangeAnimState()
    {
        canChangeState = true;
    }

    public void PlayAllAnim(string animName)
    {
        int hash = Animator.StringToHash(animName);
        if (BodyAnimator != null && BodyAnimator.HasState(0, hash)) BodyAnimator.Play(hash);
        if (LHandAnimator != null && LHandAnimator.HasState(0, hash)) LHandAnimator.Play(hash);
        if (RHandAnimator != null && RHandAnimator.HasState(0, hash)) RHandAnimator.Play(hash);
    }
}
