using UnityEngine;
using UnityEngine.AI; // NavMeshAgent 사용을 위해 추가
using Necromancer.Interfaces;
using Necromancer.Physics;
using System.Collections.Generic;

public class AllyController : MonoBehaviour, IThrowable
{
    private EntityFSM _fsm;
    private Rigidbody2D _rb;
    private ArcMovement _arcMovement;
    private Collider2D _collider;
    private NavMeshAgent _agent; // 네비게이션 에이전트 선언

    public Transform player;
    public LayerMask enemyLayer;

    [Header("Minion Data (Master SO)")]
    [SerializeField] protected MinionDataSO minionData;
    public MinionDataSO MinionData => minionData;

    // 시너지 판정 등에 사용되는 식별자 (마스터 데이터가 있으면 거기서 가져옴)
    public CommandData MinionType => minionData != null ? minionData.minionType : CommandData.SkeletonWarrior;

    [Header("State Assets")]
    public FSMStateSO idleState;
    public FSMStateSO followState;
    public FSMStateSO attackState;
    public FSMStateSO thrownState;

    [Header("Throw Physics Settings")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float straightHeight = 0.1f;
    [SerializeField] private float minSpeed = 5f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float fullChargeSpeed = 30f;

    [Header("References")]
    [SerializeField] private NearestTargetFinder _nearestFinder;
    [SerializeField] private BaseThrowImpactSO impactEffect; 

    private float _detectRange = 10f; // Awake에서 MinionData로부터 초기화됨
    private LayerMask _hitLayers;
    private float _throwStartTime;
    private float _originalDamping;
    private float _lastChargeRatio; 
    private int _originalLayer; // 원래 물리 레이어 저장용
    private string _originalSortingLayerName; // 원래 Sorting Layer 저장용
    private SpriteRenderer _sr; // 유닛의 비주얼 컴포넌트
    private bool _hasImpacted = false; // 효과 중복 발동 방지용
    private bool _isDirectThrow = false; // 직구 여부 플래그

    [SerializeField] bool isBattle = false;

    public bool IsBattle => isBattle;
    public EntityFSM FSM => _fsm;

    // --- 공격 관련 확장 ---
    public virtual void ExecuteAttack(Transform target)
    {
        if (target != null && target.TryGetComponent<CharacterStat>(out var targetStat))
        {
            DamageInfo info = new DamageInfo(_fsm.stats.ATK, DamageType.Physical, this.gameObject);
            targetStat.GetDamage(info);
        }
    }

    void Awake()
    {
        _fsm = GetComponent<EntityFSM>();
        _rb = GetComponent<Rigidbody2D>();
        _arcMovement = GetComponent<ArcMovement>();
        _collider = GetComponent<Collider2D>();
        _agent = GetComponent<NavMeshAgent>(); // 에이전트 캐싱
        _nearestFinder = GetComponent<NearestTargetFinder>();
        _sr = GetComponentInChildren<SpriteRenderer>(); // 비주얼 컴포넌트 캐싱

        if (_rb != null)
        {
            _rb.freezeRotation = true;
            _originalDamping = _rb.linearDamping;
        }

        _hitLayers = LayerMask.GetMask("Enemy", "Wall", "Obstacle");
    }

    /// <summary>
    /// 소환 시 호출되어 미니언 데이터를 주입하고 초기화합니다.
    /// </summary>
    public void Initialize(MinionDataSO data)
    {
        minionData = data;
        
        if (minionData != null)
        {
            if (minionData.throwImpact != null) impactEffect = minionData.throwImpact;
            _detectRange = minionData.detectRange;
        }

        // CharacterStat 초기화 강제 실행
        if (TryGetComponent<CharacterStat>(out var stat))
        {
            stat.InitializeStats(minionData);
        }
    }

    void Update()
    {
        // 비행 중이거나 상태가 thrownState일 때는 AI 로직 완전 차단
        if ((_arcMovement != null && _arcMovement.IsFlying) || _fsm.currentState == thrownState || !enabled) return;

        if (_fsm.currentState == attackState && _fsm.target != null)
        {
            if (_fsm.target.TryGetComponent<CharacterStat>(out var targetStat))
            {
                if (targetStat.IsDead || targetStat.Invincible)
                {
                    _fsm.target = null;
                    _fsm.ChangeState(followState);
                    return;
                }
            }

            float dist = Vector3.Distance(transform.position, _fsm.target.position);
            if (dist > _fsm.stats.ATKRANGE + 0.5f)
            {
                _fsm.ChangeState(followState);
                return;
            }
            return;
        }

        Transform trs = _nearestFinder.FindNearest(_detectRange);

        if (trs != null)
        {
            float dist = Vector3.Distance(transform.position, trs.position);
            if (dist <= _fsm.stats.ATKRANGE)
            {
                if (_fsm.target == null || _fsm.target != trs)
                    _fsm.target = trs;

                _fsm.ChangeState(attackState);
                return;
            }
        }

        if (isBattle)
        {
            if (trs != null)
            {
                if (_fsm.target == null || _fsm.target != trs)
                    _fsm.target = trs;

                _fsm.ChangeState(followState);
            }
            else
            {
                _fsm.target = player;
            }
        }
        else
        {
            _fsm.target = player;
        }
        _fsm.ChangeState(followState);
    }

    #region IThrowable 구현

    public void OnPickedUp()
    {
        _fsm.ChangeState(thrownState);
        if (_rb != null) _rb.simulated = false;
        if (_collider != null) _collider.enabled = false;
        
        // 집어들었을 때도 네비게이션 정지
        if (_agent != null) _agent.enabled = false;
    }

    public void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _throwStartTime = Time.time;
        _lastChargeRatio = chargeRatio;
        _hasImpacted = false;
        _isDirectThrow = (chargeRatio >= 1.0f); // 1.0 이상이면 직구

        // --- 레이어 및 Sorting Layer 저장 및 변경 ---
        _originalLayer = gameObject.layer;
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1)
        {
            gameObject.layer = flyingLayer;
        }

        if (_sr != null)
        {
            _originalSortingLayerName = _sr.sortingLayerName;
            _sr.sortingLayerName = "FlyingObject";
        }

        transform.rotation = Quaternion.identity;

        if (_rb != null)
        {
            _rb.simulated = true;
            _rb.linearDamping = 0f;
        }

        if (_collider != null)
        {
            _collider.enabled = true;
            _collider.isTrigger = true;
        }

        // --- 네비게이션 에이전트 확실히 비활성화 ---
        if (_agent != null)
        {
            _agent.enabled = false;
        }

        // --- 포물선 던지기일 때 목표 지점 분산(Spread) 적용 ---
        if (!_isDirectThrow)
        {
            targetPosition += Random.insideUnitCircle * 0.8f; 
        }

        Vector2 startPos = (Vector2)transform.position;
        Vector2 diff = targetPosition - startPos;
        float distance = diff.magnitude;
        Vector2 direction = diff.normalized;

        ThrowParams p = new ThrowParams();
        
        if (_isDirectThrow)
        {
            p.speed = fullChargeSpeed;
            p.duration = 1.5f; 
            p.maxHeight = straightHeight;
        }
        else
        {
            p.speed = Mathf.Lerp(minSpeed, maxSpeed, chargeRatio);
            p.duration = distance / p.speed;
            float targetHeight = Mathf.Lerp(jumpHeight, straightHeight, chargeRatio);
            p.maxHeight = Mathf.Min(targetHeight, distance * 0.5f);
        }

        if (impactEffect != null)
        {
            impactEffect.OnPreThrow(p, chargeRatio);
        }

        if (_rb != null) _rb.linearVelocity = direction * p.speed;
        if (_arcMovement != null) _arcMovement.StartArc(p.duration, p.maxHeight);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Time.time - _throwStartTime < 0.05f) return;
        if (_hasImpacted) return;

        if (_isDirectThrow)
        {
            if (_arcMovement != null && _arcMovement.IsFlying && (_hitLayers.value & (1 << other.gameObject.layer)) != 0)
            {
                TriggerImpact(other.gameObject);
                _arcMovement.StopArc();
            }
        }
    }

    private void TriggerImpact(GameObject targetObj)
    {
        if (_hasImpacted) return;
        _hasImpacted = true;

        if (impactEffect != null)
        {
            ImpactContext context = new ImpactContext
            {
                attacker = this.gameObject,
                target = targetObj,
                impactPosition = transform.position,
                chargeRatio = _lastChargeRatio,
                supporters = null
            };
            impactEffect.Apply(context);
        }
    }

    public virtual void OnLanded()
    {
        if (!_hasImpacted)
        {
            TriggerImpact(null);
        }

        // 레이어 및 Sorting Layer 복구
        gameObject.layer = _originalLayer;
        if (_sr != null && !string.IsNullOrEmpty(_originalSortingLayerName))
        {
            _sr.sortingLayerName = _originalSortingLayerName;
        }

        // 네비게이션 에이전트 복구
        if (_agent != null)
        {
            _agent.enabled = true;
        }

        _fsm.stats.Invincible = false;
        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.linearDamping = _originalDamping;
        }
        if (_collider != null) _collider.isTrigger = false;

        Transform trs = _nearestFinder.FindNearest(_detectRange);
        if (trs != null)
        {
            _fsm.target = trs;
            float dist = Vector3.Distance(transform.position, trs.position);
            if (dist <= _fsm.stats.ATKRANGE)
                _fsm.ChangeState(attackState);
            else
                _fsm.ChangeState(followState);
        }
        else
        {
            _fsm.target = player;
            _fsm.ChangeState(followState);
        }
    }

    #endregion

    public void SetBattleState(bool _set)
    {
        isBattle = _set;
    }
}
