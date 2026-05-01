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
[RequireComponent(typeof(CharacterStat), typeof(NearestTargetFinder))]
public abstract class BaseEntity : MonoBehaviour
{
    [Header("팀 설정")]
    public Team team;
    public LayerMask myTeamLayer;
    public LayerMask opponentLayer;

    [Header("탐색 설정")]
    public float detectRange = 10f;

    [Header("데이터 참조")]
    [SerializeField] protected MinionDataSO minionData;
    public MinionDataSO MinionData => minionData;

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

    protected virtual void Awake()
    {
        _stats = GetComponent<CharacterStat>();
        _nearestFinder = GetComponent<NearestTargetFinder>();
        _rb = GetComponent<Rigidbody2D>();
        _agent = GetComponent<NavMeshAgent>();
        _collider = GetComponent<Collider2D>();
        _sr = GetComponentInChildren<SpriteRenderer>();

        // [2D 환경 최적화] NavMeshAgent가 스프라이트를 3D 방향으로 돌려버리는 것을 방지
        if (_agent != null)
        {
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
        }

        // 팀에 따른 레이어 자동 설정
        SetupLayers();
    }

    protected virtual void Start()
    {
        // 데이터가 이미 할당되어 있다면 초기화
        if (minionData != null)
        {
            Initialize(minionData);
        }

        // 타겟 파인더의 대상 레이어를 상대 팀 레이어로 설정
        if (_nearestFinder != null)
        {
            _nearestFinder.targetLayer = opponentLayer;
        }
    }

    protected virtual void Update()
    {
        // 비행 중이거나 특수 상태일 때는 AI 로직 차단
        if (!CanExecuteAI()) return;

        // 통합 AI 브레인 실행 (타겟팅, 상태전환, 행동 모두 포함)
        if (_runtimeBrain != null)
        {
            _runtimeBrain.Execute(this);
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
        // 기본적으로는 항상 AI 실행 가능 (enabled 여부 체크)
        return enabled;
    }

    /// <summary>
    /// 데이터(SO)로부터 스탯과 통합 AI 패턴을 주입받아 초기화합니다.
    /// </summary>
    public virtual void Initialize(MinionDataSO data)
    {
        minionData = data;
        
        // 1. 스탯 초기화
        if (_stats != null) _stats.InitializeStats(data);
        detectRange = data.detectRange;

        // 2. 통합 AI 브레인 생성 및 초기화
        AIPatternSO patternToUse = data.aiPattern;

        // [안전장치] 만약 데이터에 패턴이 명시되어 있지 않다면 DataManager의 기본 패턴 사용
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

        // 3. 타겟 레이어 설정
        if (_nearestFinder != null) _nearestFinder.targetLayer = opponentLayer;
    }

    // 기존 HandleAIUpdate를 브레인 체제에 맞게 비워둠
    protected virtual void HandleAIUpdate() { }

    protected bool IsTargetInvalid(Transform target)
    {
        if (target == null) return true;
        if (target.TryGetComponent<CharacterStat>(out var stat))
        {
            return stat.IsDead || stat.Invincible;
        }
        return false;
    }

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
