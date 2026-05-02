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
        if (minionData != null)
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
        return enabled;
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
        
        CharacterStat stat = target.GetComponentInChildren<CharacterStat>();
        if (stat != null)
        {
            return stat.IsDead || stat.Invincible;
        }
        return false;
    }

    protected abstract void HandleNoTarget();

    public virtual void ExecuteAttack(Transform target)
    {
        if (target != null)
        {
            CharacterStat targetStat = target.GetComponentInChildren<CharacterStat>();
            if (targetStat != null)
            {
                DamageInfo info = new DamageInfo(_stats.ATK, DamageType.Physical, this.gameObject);
                targetStat.GetDamage(info);
            }
        }
    }
}
