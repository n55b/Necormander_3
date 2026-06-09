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
    [SerializeField] public Transform _target = null;

    // 새로운 통합 AI 브레인 (인스턴스)
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

    protected TelegraphHitbox _activeTelegraph;
    public bool IsAttacking => _activeTelegraph != null;

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
        if (_stats != null && _stats.Status != null && _stats.Status.GetDebuffBool(DebuffBoolType.Feared))
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
            if (_stats.Status.GetDebuffBool(DebuffBoolType.Frozen) ||
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
            _runtimeBrain = Instantiate(patternToUse);
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

        CharacterStat stat = target.GetComponentInParent<CharacterStat>();
        if (stat == null) stat = target.GetComponentInChildren<CharacterStat>();
        
        if (stat != null)
        {
            // [수정] 직접 컴포넌트 참조
            return stat.Health.IsDead || stat.Health.Invincible;
        }
        return false;
    }

    protected abstract void HandleNoTarget();

    // 매 프레임 혹은 상태 변경 시 호출 할 함수
    public virtual void UpdateAnimation(AIState state)
    {
        if (_animator == null) return;

        if (_lastState != state)
        {
            _lastState = state;
            // Enum 이름(Idle, Follow 등)과 애니메이터의 State 이름을 일치시켜야 함
            // 더 부드럽게 바꾸고 싶다면 Play 대신 CrossFade(state.ToString(), 0.1f) 사용
            _animator.Play(state.ToString());
        }
    }

    // 공격 실행 시 호출 (각 유닛의 특수 공격 로직은 여기서 구현)
    public virtual void ExecuteAttack(Transform target)
    {
        if (target == null) return;
        _target = target; // 공격 대상 저장

        if (_animator != null)
        {
            _animator.Play("Attack");
        }

        // [추가] TelegraphHitbox 스폰 로직 (원거리 마법사는 이를 오버라이드하거나 우회함)
        if (telegraphPrefab != null)
        {
            Vector3 dir = (target.position - transform.position).normalized;
            // 살짝 앞에 생성
            Vector3 spawnPos = transform.position + dir * 0.5f; 
            GameObject go = Instantiate(telegraphPrefab, spawnPos, Quaternion.identity);
            _activeTelegraph = go.GetComponent<TelegraphHitbox>();

            if (_activeTelegraph != null)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                go.transform.rotation = Quaternion.Euler(0, 0, angle);

                // 데미지 페이로드 생성 (경직 유발 가능, 기본 넉백 1f 추가 가능)
                DamageInfo info = new DamageInfo(_stats.ATK, DamageType.Physical, this.gameObject, false, 1f, true, "", false, false, 0f); 
                
                // Telegraph 완성 시간은 공속의 절반 정도로 임시 지정. (애니메이션 길이에 맞추려면 별도 설정 필요)
                float telegraphDuration = _stats.ATKSPD * 0.5f; 
                if (telegraphDuration < 0.2f) telegraphDuration = 0.2f;

                _activeTelegraph.Init(telegraphDuration, info, opponentLayer, new Vector2(2f, 2f));
            }
        }
        else
        {
            // 리소스가 없으면 레거시 방식으로 넘어감 (StartAttack에서 즉발 데미지)
            StartAttack();
        }
    }

    public void PlayAttackSound()
    {
        if(minionData.AttackSound != null)
        {
            SoundManager.Instance.PlaySFX(minionData.AttackSound, 0.4f);
        }
    }

    public void StartAttack()
    {
        // Telegraph가 활성화되어 데미지를 처리하는 중이라면 기존 애니메이션 이벤트(StartAttack)의 즉발 데미지 판정을 무시합니다.
        if (IsAttacking) return;

        if (_target == null)
        {
            // 애니메이션 이벤트 타이밍 때문에 BaseEntity._target이 비워진 경우,
            // AI 브레인의 현재 타겟을 사용해 공격을 복구합니다.
            if (_runtimeBrain != null && _runtimeBrain.Target != null && !IsTargetInvalid(_runtimeBrain.Target))
            {
                _target = _runtimeBrain.Target;
            }
        }

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
        
        if (_activeTelegraph != null)
        {
            Destroy(_activeTelegraph.gameObject);
            _activeTelegraph = null;
        }

        if (_animator != null)
        {
            _animator.Play("Idle"); // 애니메이션 강제 초기화
        }

        if (_runtimeBrain != null)
        {
            _runtimeBrain.ResetAttackTimer(); // 타격 시전 시간 초기화
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
