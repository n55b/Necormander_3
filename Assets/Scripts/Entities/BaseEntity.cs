using UnityEngine;
using UnityEngine.AI;

public enum Team
{
    Ally,
    Enemy
}

/// <summary>
/// 모든 아군과 적군 유닛의 공통 기반 클래스입니다.
/// FSM, 스탯, 타겟 탐색 등 공통 로직을 관리합니다.
/// </summary>
[RequireComponent(typeof(EntityFSM), typeof(CharacterStat), typeof(NearestTargetFinder))]
public abstract class BaseEntity : MonoBehaviour
{
    [Header("팀 설정")]
    public Team team;
    public LayerMask myTeamLayer;
    public LayerMask opponentLayer;

    [Header("상태 에셋")]
    public FSMStateSO idleState;
    public FSMStateSO followState;
    public FSMStateSO attackState;

    [Header("탐색 설정")]
    public float detectRange = 10f;

    [Header("데이터 참조")]
    [SerializeField] protected MinionDataSO minionData;

    // 공통 컴포넌트 캐싱 및 노출
    protected EntityFSM _fsm;
    protected CharacterStat _stats;
    protected NearestTargetFinder _nearestFinder;
    protected Rigidbody2D _rb;
    protected NavMeshAgent _agent;
    protected Collider2D _collider;
    protected SpriteRenderer _sr;

    public EntityFSM FSM => _fsm;
    public CharacterStat Stats => _stats;
    public NearestTargetFinder TargetFinder => _nearestFinder;

    protected virtual void Awake()
    {
        _fsm = GetComponent<EntityFSM>();
        _stats = GetComponent<CharacterStat>();
        _nearestFinder = GetComponent<NearestTargetFinder>();
        _rb = GetComponent<Rigidbody2D>();
        _agent = GetComponent<NavMeshAgent>();
        _collider = GetComponent<Collider2D>();
        _sr = GetComponentInChildren<SpriteRenderer>();

        // 팀에 따른 레이어 자동 설정 (인스펙터 설정을 잊었을 때를 대비한 안전장치)
        SetupLayers();
    }

    protected virtual void Start()
    {
        // 타겟 파인더의 대상 레이어를 상대 팀 레이어로 설정
        if (_nearestFinder != null)
        {
            _nearestFinder.targetLayer = opponentLayer;
        }

        if (idleState != null)
        {
            _fsm.ChangeState(idleState);
        }
    }

    protected virtual void Update()
    {
        // 비행 중이거나 특수 상태일 때는 AI 로직 차단 (자식에서 확장 가능)
        if (!CanExecuteAI()) return;

        HandleAIUpdate();
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
        // 기본적으로는 항상 AI 실행 가능
        return enabled;
    }

    /// <summary>
    /// 데이터(SO)로부터 스탯과 특수 공격 상태를 주입받아 초기화합니다.
    /// </summary>
    public virtual void Initialize(MinionDataSO data)
    {
        minionData = data;
        
        // 1. 스탯 초기화
        if (_stats != null) _stats.InitializeStats(data);
        detectRange = data.detectRange;

        // 2. 고유 공격 패턴 주입 (데이터에 설정되어 있다면 프리팹 설정을 무시하고 덮어씀)
        if (data.attackState != null)
        {
            attackState = data.attackState;
        }

        // 3. 타겟 레이어 설정
        if (_nearestFinder != null) _nearestFinder.targetLayer = opponentLayer;
    }

    /// <summary>
    /// 공통 AI 탐색 및 상태 전환 로직
    /// </summary>
    protected virtual void HandleAIUpdate()
    {
        // 공격 중일 때 타겟 유효성 체크
        if (_fsm.currentState == attackState && _fsm.target != null)
        {
            if (IsTargetInvalid(_fsm.target))
            {
                _fsm.target = null;
                _fsm.ChangeState(followState);
                return;
            }

            float dist = Vector3.Distance(transform.position, _fsm.target.position);
            if (dist > _stats.ATKRANGE + 0.5f)
            {
                _fsm.ChangeState(followState);
            }
            return;
        }

        // 주변 타겟 탐색
        Transform nearest = _nearestFinder.FindNearest(detectRange);

        if (nearest != null)
        {
            _fsm.target = nearest;
            float dist = Vector3.Distance(transform.position, nearest.position);

            if (dist <= _stats.ATKRANGE)
                _fsm.ChangeState(attackState);
            else
                _fsm.ChangeState(followState);
        }
        else
        {
            HandleNoTarget();
        }
    }

    protected bool IsTargetInvalid(Transform target)
    {
        if (target == null) return true;
        if (target.TryGetComponent<CharacterStat>(out var targetStat))
        {
            return targetStat.IsDead || targetStat.Invincible;
        }
        return false;
    }

    /// <summary>
    /// 타겟이 없을 때의 행동 (아군은 플레이어 추적, 적군은 제자리 대기 등)
    /// </summary>
    protected abstract void HandleNoTarget();

    // 공격 실행 시 호출 (각 유닛의 특수 공격 로직은 여기서 구현)
    public virtual void ExecuteAttack(Transform target)
    {
        if (target != null && target.TryGetComponent<CharacterStat>(out var targetStat))
        {
            DamageInfo info = new DamageInfo(_stats.ATK, DamageType.Physical, this.gameObject);
            targetStat.GetDamage(info);
        }
    }
}
