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
    [SerializeField] float throwRange;
    public float THROWRANGE { get { return throwRange; } }
    [Header("아군 유닛 관련 매니저")]
    [SerializeField] AllyManager allyManager;
    [Header("소환 컨트롤러")]
    [SerializeField] SummonController sumController;
    public SummonController SUMCONTROLLER { get { return sumController; } }
    [Header("던지기 컨트롤러")]
    [SerializeField] private ThrowController throwController;
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

    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        if (throwController == null)
        {
            throwController = GetComponentInChildren<ThrowController>();
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
            throwController.DropAll();
        }
    }

    private void Update()
    {
        if (_inputBlocked || (stat != null && stat.Health != null && stat.Health.IsDead)) return;

        MoveDirection = moveInput;

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

        // [기존 임시 디버깅 삭제]
        // if (Input.GetKeyDown(KeyCode.E))
        // {
        //     Debug.Log("<color=cyan>[DirectInput]</color> Keyboard E Pressed!");
        // }
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

    private void FixedUpdate()
    {
        if (_inputBlocked) return; // [추가] 입력 차단 시 로직 스킵

        // 사망 시 조종 불가
        if (stat.Health.IsDead) return;

        // [복구] 기존 이동 로직으로 원복하되, 넉백 중일 때는 물리 속도를 덮어쓰지 않도록 개선 가능
        // 만약 리지드바디의 속도가 아주 높다면 이동 처리를 스킵하거나 합산
        if (_rb != null && _rb.linearVelocity.sqrMagnitude < 200f) // 대략적인 임계값
        {
            transform.position += MoveDirection * stat.MOVESPEED * Time.deltaTime;
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
        if (_inputBlocked || stat.Health.IsDead) return;

        if (throwController != null)
        {
            throwController.OnThrow(context);
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

    public void OnNum1(InputAction.CallbackContext context) { if (stat.Health.IsDead) return; sumController.OnNumKey(1, context); }
    public void OnNum2(InputAction.CallbackContext context) { if (stat.Health.IsDead) return; sumController.OnNumKey(2, context); }
    public void OnNum3(InputAction.CallbackContext context) { if (stat.Health.IsDead) return; sumController.OnNumKey(3, context); }
    public void OnNum4(InputAction.CallbackContext context) { if (stat.Health.IsDead) return; sumController.OnNumKey(4, context); }

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
        BodyAnimator.Play(animName);
        LHandAnimator.Play(animName);
        RHandAnimator.Play(animName);
    }
}
