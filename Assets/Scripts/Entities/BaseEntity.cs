using UnityEngine;
using UnityEngine.AI;

public enum Team
{
    Ally,
    Enemy
}

/// <summary>
/// 모든 아군과 적군 유닛의 공통 기반 클래스입니다.
/// 통합 AI 패턴(AIPatternSO)을 통해 유닛의 행동을 제어합니다.
/// </summary>
[RequireComponent(typeof(NearestTargetFinder))]
public abstract class BaseEntity : MonoBehaviour
{
    [Header("팀 설정")]
    public Team team;
    public LayerMask myTeamLayer;
    public LayerMask opponentLayer;

    [Header("탐색 설정")]
    public float detectRange = 10f;

    [Header("전투 프리팹")]
    [SerializeField] protected GameObject telegraphPrefab; // 공격 경고(Telegraph) 프리팹 (인스펙터 할당)

    [Header("데이터 참조 (직접 배치 시 필수)")]
    [SerializeField] protected MinionDataSO minionData;
    public MinionDataSO MinionData => minionData;
    protected Animator _animator; // 애니메이터 추가
    [SerializeField] protected AIState _lastState = (AIState)(-1); // 이전 상태 기록
    [SerializeField] protected Transform _target = null;
    [SerializeField] protected CharacterStat _targetStat = null;
    public Transform Target 
    {
        get => _target;
        set 
        {
            if (_target != value)
            {
                _target = value;
                if (_target != null)
                {
                    _targetStat = _target.GetComponentInParent<CharacterStat>();
                    if (_targetStat == null) _targetStat = _target.GetComponentInChildren<CharacterStat>();
                }
                else
                {
                    _targetStat = null;
                }
            }
        }
    }
    public CharacterStat TargetStat => _targetStat;

    [SerializeField] protected AIState _currentState = AIState.Idle;
    public AIState CurrentState
    {
        get => _currentState;
        set => _currentState = value;
    }

    public float AtkTimer { get; set; } = 0f;
    public NavMeshPath NavPath { get; set; }

    // 새로운 통합 AI 브레인 (공유 인스턴스)
    protected AIPatternSO _runtimeBrain;

    // 공통 컴포넌트 캐싱 및 노출
    protected CharacterStat _stats;
    protected NearestTargetFinder _nearestFinder;
    protected Rigidbody2D _rb;
    protected NavMeshAgent _agent;
    protected Collider2D _collider;
    protected SpriteRenderer _sr;

    public CharacterStat Stats => _stats;
    public NearestTargetFinder TargetFinder => _nearestFinder;
    public AIPatternSO Brain => _runtimeBrain;
    public SpriteRenderer SpriteRenderer => _sr;
    public Animator Animator => _animator;

    public bool HasFiredHitEvent { get; set; } = false;
    public bool HasFiredAttackEndEvent { get; set; } = false;

    // =======================================================================
    // [애니메이션 작업자 가이드라인]
    // 향후 유니티 창에서 유닛의 공격(Attack) 애니메이션을 만들 때, 아래 두 개의 이벤트를 타임라인에 꼭 추가해 주세요!
    // 1. 공격 판정(Hit)이 들어가야 하는 프레임에 Add Event -> Function Name: OnHitEvent
    // 2. 무기를 다 휘두르고 원래 자세로 돌아가는(후딜레이 끝) 프레임에 Add Event -> Function Name: OnAttackEndEvent
    // 만약 이벤트를 깜빡 잊고 안 넣으셔도 스크립트가 자동으로 감지해서 임시 타이머(0.3초/0.5초)로 Fallback 되니 에러가 나진 않습니다.
    // =======================================================================

    public virtual void OnHitEvent()
    {
        HasFiredHitEvent = true;

        if (minionData != null && minionData.AttackSound != null)
        {
            SoundManager.Instance.PlaySFX(minionData.AttackSound, 0.4f);
        }
    }

    public virtual void OnAttackEndEvent()
    {
        HasFiredAttackEndEvent = true;
    }

    /// <summary>
    /// 현재 Animator에 연결된 특정 클립이 주어진 이벤트를 가지고 있는지 검사합니다.
    /// (위에서 설명한 OnHitEvent, OnAttackEndEvent가 제대로 설정되어 있는지 검사하는 용도)
    /// </summary>
    public bool HasAnimationEvent(string clipName, string eventName)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null) return false;

        foreach (var clip in _animator.runtimeAnimatorController.animationClips)
        {
            // 클립 이름이 포함되어 있는지 확인 (Attack_Skeleton 등)
            if (clip.name.Contains(clipName))
            {
                foreach (var ev in clip.events)
                {
                    if (ev.functionName == eventName) return true;
                }
            }
        }
        return false;
    }

    protected TelegraphHitbox _activeTelegraph;
    public bool IsAttacking { get; set; } = false;

    protected virtual void Awake()
    {
        _animator = GetComponentInChildren<Animator>();

        _stats = GetComponentInChildren<CharacterStat>();
        if (_stats != null) _stats.Setup();

        _nearestFinder = GetComponent<NearestTargetFinder>();
        _rb = GetComponent<Rigidbody2D>();
        _agent = GetComponent<NavMeshAgent>();
        _collider = GetComponent<Collider2D>();
        _sr = GetComponentInChildren<SpriteRenderer>();

        if (_agent != null)
        {
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
        }

        SetupLayers();
    }

    protected virtual void Start()
    {
        // [수정] 오토배틀러 비활성화 상태에서는 적군의 모든 물리/타겟팅 판정(opponentLayer)에서 아군 미니언을 제외함
        if (GameManager.Instance != null && GameManager.Instance.testMode_DisableAutoBattle)
        {
            if (team == Team.Enemy)
            {
                opponentLayer.value &= ~(1 << LayerMask.NameToLayer("Army"));
                opponentLayer.value &= ~(1 << LayerMask.NameToLayer("Ally"));
            }
        }

        // [수정] 직접 배치된 개체라면 스스로 초기화 (스탯 및 브레인 생성)
        if (minionData != null && _runtimeBrain == null)
        {
            Initialize(minionData);
        }

        if (_nearestFinder != null)
        {
            _nearestFinder.targetLayer = opponentLayer;
        }
    }

    protected virtual void Update()
    {
        if (!CanExecuteAI()) return;

        // [유니크] 공포 상태 처리
        if (_stats != null && _stats.Status != null && _stats.Status.GetDebuffBool(DebuffBoolType.Wounded))
        {
            ExecuteFearAI();
            return;
        }

        if (_runtimeBrain != null)
        {
            _runtimeBrain.Execute(this);
        }
    }

    private void ExecuteFearAI()
    {
        // 공포 상태일 땐 플레이어로부터 멀어지는 방향으로 이동
        UpdateAnimation(AIState.Follow);
        var pc = GameManager.Instance.PLAYERCONTROLLER;
        if (pc != null && _agent != null)
        {
            if (_agent.isActiveAndEnabled)
            {
                _agent.isStopped = false;
                Vector3 dirAwayFromPlayer = (transform.position - pc.transform.position).normalized;
                Vector3 fleeTarget = transform.position + dirAwayFromPlayer * 5f;
                _agent.SetDestination(fleeTarget);
            }
        }
    }

    protected virtual void SetupLayers()
    {
        if (team == Team.Ally)
        {
            myTeamLayer = LayerMask.GetMask("Army", "Player");
            opponentLayer = LayerMask.GetMask("Enemy");
        }
        else
        {
            myTeamLayer = LayerMask.GetMask("Enemy");
            opponentLayer = LayerMask.GetMask("Army", "Player");
        }
    }

    protected virtual bool CanExecuteAI()
    {
        if (!enabled) return false;
        
        // [추가] 공격 중(Telegraph 차오르는 중)일 때는 다른 행동 불가
        if (IsAttacking) return false;

        // [추가] 동결 또는 기절 상태라면 AI 중단
        if (_stats != null && _stats.Status != null)
        {
            if (_stats.Status.GetDebuffBool(DebuffBoolType.Stunned) ||
                _stats.Status.GetDebuffBool(DebuffBoolType.Stunned))
                return false;
        }

        return true;
    }

    public virtual void Initialize(MinionDataSO data)
    {
        minionData = data;

        if (_stats != null) _stats.InitializeStats(data);
        detectRange = data.detectRange;

        AIPatternSO patternToUse = data.aiPattern;

        if (patternToUse == null && GameManager.Instance != null && GameManager.Instance.dataManager != null)
        {
            patternToUse = GameManager.Instance.dataManager.DEFAULT_AI_PATTERN;
        }

        if (patternToUse != null)
        {
            _runtimeBrain = patternToUse; // 원본 SO 공유 참조
            _runtimeBrain.Init(this);
        }
        else
        {
            Debug.LogWarning($"[BaseEntity] {gameObject.name}: 사용 가능한 AI 패턴이 없습니다!");
        }

        if (_nearestFinder != null) _nearestFinder.targetLayer = opponentLayer;
    }

    protected virtual void HandleAIUpdate() { }
    protected bool IsTargetInvalid(Transform target)
    {
        if (target == null) return true;

        CharacterStat stat;
        if (target == _target && _targetStat != null)
        {
            stat = _targetStat; // 캐싱된 스탯 사용
        }
        else
        {
            stat = target.GetComponentInParent<CharacterStat>();
            if (stat == null) stat = target.GetComponentInChildren<CharacterStat>();
        }
        
        if (stat != null)
        {
            return stat.Health.IsDead || stat.Health.Invincible;
        }
        return false;
    }

    public virtual void LookAtTarget(Transform t)
    {
        if (t == null) return;

        if (t.position.x - transform.position.x > 0.0f)
        {
            _sr.flipX = true;
        }
        else if (t.position.x - transform.position.x < 0.0f)
        {
            _sr.flipX = false;
        }
    }

    protected abstract void HandleNoTarget();

    // 매 프레임 혹은 상태 변경 시 호출 할 함수
    public virtual void UpdateAnimation(AIState state)
    {
        if (_animator == null) return;

        if (_lastState != state)
        {
            _lastState = state;

            // Attack 상태 진입 시 바로 Attack 애니메이션을 틀지 않고 대기(Idle) 모션을 취합니다.
            // 실제 Attack 애니메이션은 쿨타임이 차고 AttackRoutine이 실행될 때 한 번만 틉니다.
            if (state == AIState.Attack)
            {
                _animator.Play("Idle");
            }
            else
            {
                _animator.Play(state.ToString());
            }
        }
    }

    protected BaseHitBox _activeHitbox; // _activeTelegraph 대신 BaseHitBox를 추적합니다.
    public BaseHitBox ActiveHitbox => _activeHitbox;

    public virtual void SetActiveHitbox(BaseHitBox hitbox)
    {
        if (_activeHitbox != null && _activeHitbox != hitbox)
        {
            Destroy(_activeHitbox.gameObject);
        }
        _activeHitbox = hitbox;
    }

    public virtual void StartTelegraph(Transform target, float windupTime = 0.5f)
    {
        if (target == null) return;
        _target = target;

        if (telegraphPrefab != null)
        {
            Vector3 dir = (target.position - transform.position).normalized;
            Vector3 spawnPos = transform.position + dir * 0.5f; 
            GameObject go = Instantiate(telegraphPrefab, spawnPos, Quaternion.identity);
            
            _activeHitbox = go.GetComponent<BaseHitBox>();
            if (_activeHitbox != null)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                go.transform.rotation = Quaternion.Euler(0, 0, angle);

                DamageInfo info = new DamageInfo(_stats.ATK, DamageType.Physical, this.gameObject, false, 1f, true, "", false, false, 0f); 
                
                // [수정] 길쭉하게 늘어나지 않도록, 타격 범위(ATKRANGE)를 X, Y 균등하게 적용 (원형/정사각형 형태 유지)
                go.transform.localScale = new Vector3(_stats.ATKRANGE, _stats.ATKRANGE, 1f);

                // 전달받은 실제 애니메이션 이벤트 시간(windupTime)과 피아식별 정보(team)를 넘겨줍니다.
                _activeHitbox.Init(info, opponentLayer, 0.2f, windupTime, this.team == Team.Ally);
            }
            else
            {
                Debug.LogError($"[BaseEntity] {telegraphPrefab.name}에 BaseHitBox가 없습니다!");
            }
        }
    }

    // 공격 실행 시 호출 (애니메이션 타격 프레임에 맞춰 호출됨)
    public virtual void ExecuteAttack(Transform target)
    {
        if (target == null) return;
        _target = target;

        if (_activeHitbox != null)
        {
            // 대기 중이던 장판의 선딜레이를 즉시 끝내고 타격을 가합니다.
            _activeHitbox.ForceActivate();
            _activeHitbox = null;
        }
        else if (telegraphPrefab == null)
        {
            // 리소스가 없으면 구형 방식으로 즉발 데미지
            ExecuteLegacyDamage();
        }
    }

    // PlayAttackSound는 OnHitEvent로 통합되었으므로 기존 메서드 본문은 제거합니다.

    public void ExecuteLegacyDamage()
    {
        // 더 이상 애니메이션 이벤트로 불리지 않으므로 검사 코드 생략

        if (_target == null)
        {
            Debug.LogWarning($"{gameObject.name}: 공격 대상이 없는 상태에서 이벤트가 호출됨!");
            return;
        }

        // [수정] 플레이어가 미니언을 들고 있을 때 등을 고려하여 robust하게 Stat을 찾습니다.
        CharacterStat targetStat = _target.GetComponent<CharacterStat>();
        if (targetStat == null)
        {
            int flyingLayer = LayerMask.NameToLayer("FlyingObject");
            foreach (var s in _target.GetComponentsInChildren<CharacterStat>())
            {
                if (s.gameObject.layer != flyingLayer)
                {
                    targetStat = s;
                    break;
                }
            }
        }

        if (targetStat != null)
        {
            // [수정] 직접 Health 담당자에게 명령, isBasicAttack = true 추가
            DamageInfo info = new DamageInfo(_stats.ATK, DamageType.Physical, this.gameObject, false, 1f, true);
            targetStat.Health.GetDamage(info);

            // [이벤트 버스] 기본 공격 타격 시 넉백 등 특수 처리 연동용
            // DamageEventBus.TriggerBasicAttackImpactAfterDamage(...) 등으로 확장 가능

            // 3. 무기 속성 부여 (보석 효과) - 아군일 때만 적용
            if (this.team == Team.Ally && InventoryManager.Instance != null)
            {
                // 스택형 속성 부여
                foreach (var kvp in InventoryManager.Instance.GlobalGemStats.WeaponAttributes)
                {
                    if (kvp.Value > 0)
                    {
                        targetStat.Status.AddDebuffStack(kvp.Key, kvp.Value);
                    }
                }

                // [추가] 상태형(Bool) 속성 부여 (부식 등)
                foreach (var kvp in InventoryManager.Instance.GlobalGemStats.WeaponBoolAttributes)
                {
                    if (kvp.Value > 0)
                    {
                        targetStat.Status.SetDebuffBool(kvp.Key, kvp.Value);
                    }
                }
            }

        }

        _target = null;
        // _isAttackExecuting = false; // 공격 애니메이션 종료
    }

    // [추가] 공격 취소 (경직 시 호출)
    public virtual void CancelAttack()
    {
        _target = null;
        
        if (_activeHitbox != null)
        {
            Destroy(_activeHitbox.gameObject);
            _activeHitbox = null;
        }

        if (_animator != null)
        {
            _animator.Play("Idle"); // 애니메이션 강제 초기화
        }

        if (_runtimeBrain != null)
        {
            _runtimeBrain.ResetAttackTimer(this); // 타격 시전 시간 초기화
        }
    }

    // [추가] 넉백 적용
    public virtual void ApplyKnockback(Vector2 force)
    {
        if (_rb != null)
        {
            // NavMeshAgent가 켜져 있으면 넉백을 방해할 수 있으므로 임시 처리 고려 (현재는 AddForce)
            _rb.AddForce(force, ForceMode2D.Impulse);
        }
    }
}
