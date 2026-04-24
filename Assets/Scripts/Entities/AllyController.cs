using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 아군 유닛 전용 컨트롤러입니다. 
/// AIPatternSO(브레인)와 협력하여 유닛의 행동과 던지기 로직을 처리합니다.
/// </summary>
public class AllyController : BaseEntity, IThrowable
{
    [Header("Ally 전용 설정")]
    public Transform player;
    [SerializeField] bool isBattle = false;

    public MinionDataSO MinionData => minionData;
    public CommandData MinionType => minionData != null ? minionData.minionType : CommandData.SkeletonWarrior;

    [Header("Throw Physics")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float straightHeight = 0.1f;
    [SerializeField] private float minSpeed = 5f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float fullChargeSpeed = 30f;

    public float JumpHeight => jumpHeight;
    public float StraightHeight => straightHeight;
    public float MinSpeed => minSpeed;
    public float MaxSpeed => maxSpeed;
    public float FullChargeSpeed => fullChargeSpeed;

    [Header("References")]
    private ArcMovement _arcMovement;
    private BaseThrowImpactSO impactEffect; 

    private float _throwStartTime;
    private float _originalDamping;
    private float _lastChargeRatio; 
    private int _originalLayer;
    private string _originalSortingLayerName;
    private bool _hasImpacted = false;
    private bool _isDirectThrow = false;

    private ThrowCombinationSO _activeCombination;
    private bool _isCombinationLead = false;
    private List<AllyController> _combinationSupporters;

    protected override void Awake()
    {
        base.Awake();
        _arcMovement = GetComponent<ArcMovement>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (_rb != null) _originalDamping = _rb.linearDamping;
        if (_sr != null) _originalSortingLayerName = _sr.sortingLayerName;
        
        team = Team.Ally;
    }

    public override void Initialize(MinionDataSO data)
    {
        base.Initialize(data);
        if (minionData != null && minionData.throwImpact != null) 
            impactEffect = minionData.throwImpact;
    }

    protected override bool CanExecuteAI()
    {
        // 비행 중이거나 던져진 상태일 때는 AI 차단
        if ((_arcMovement != null && _arcMovement.IsFlying) || (_runtimeBrain != null && _runtimeBrain.CurrentState == AIState.Thrown)) 
            return false;
        
        return base.CanExecuteAI();
    }

    protected override void HandleNoTarget()
    {
        // 이제 브레인이 스스로 판단하므로, 브레인 외부에서의 강제 개입은 최소화합니다.
    }

    #region IThrowable 구현

    public void OnPickedUp()
    {
        // [중요] 구형 FSM 대신 브레인 상태를 Thrown으로 변경
        if (_runtimeBrain != null) _runtimeBrain.SetState(AIState.Thrown);

        if (_rb != null) _rb.simulated = false;
        if (_collider != null) _collider.enabled = false;
        if (_agent != null) _agent.enabled = false;
    }

    public void SetCombination(ThrowCombinationSO combo, bool isLead, List<AllyController> supporters)
    {
        _activeCombination = combo;
        _isCombinationLead = isLead;
        _combinationSupporters = supporters;
    }

    public void OnThrown(Vector2 targetPosition, float chargeRatio)
    {
        _throwStartTime = Time.time;
        _lastChargeRatio = chargeRatio;
        _hasImpacted = false;
        _isDirectThrow = (chargeRatio >= 1.0f); 

        _originalLayer = gameObject.layer;
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1) gameObject.layer = flyingLayer;

        if (_sr != null) _sr.sortingLayerName = "FlyingObject";

        if (_rb != null)
        {
            _rb.simulated = true;
            _rb.linearDamping = 0f;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        if (_collider != null)
        {
            _collider.enabled = true;
            _collider.isTrigger = true; 
        }

        if (_agent != null) _agent.enabled = false;

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

        if (impactEffect != null) impactEffect.OnPreThrow(p, chargeRatio);

        if (_rb != null) _rb.linearVelocity = direction * p.speed;
        if (_arcMovement != null) _arcMovement.StartArc(p.duration, p.maxHeight);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_hasImpacted) return;

        int wallMask = LayerMask.GetMask("Wall", "Obstacle");
        bool isWall = (wallMask & (1 << other.gameObject.layer)) != 0;

        if (isWall)
        {
            if (_arcMovement != null && _arcMovement.IsFlying)
            {
                TriggerImpact(other.gameObject);
                _arcMovement.StopArc();
            }
            return; 
        }

        if (_isDirectThrow)
        {
            int objectMask = LayerMask.GetMask("Object");
            bool isHitTarget = ((opponentLayer.value | objectMask) & (1 << other.gameObject.layer)) != 0;
            if (isHitTarget && _arcMovement != null && _arcMovement.IsFlying)
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

        if (_activeCombination != null)
        {
            if (_isCombinationLead && _activeCombination.combinationEffect != null)
            {
                CombinationContext context = new CombinationContext
                {
                    leadAttacker = this.gameObject,
                    impactPosition = transform.position,
                    chargeRatio = _lastChargeRatio,
                    supporters = _combinationSupporters
                };
                _activeCombination.combinationEffect.Execute(context);
            }
            return;
        }

        if (impactEffect != null)
        {
            ImpactContext context = new ImpactContext
            {
                attacker = this.gameObject,
                target = targetObj,
                impactPosition = transform.position,
                chargeRatio = _lastChargeRatio
            };
            impactEffect.Apply(context);
        }
    }

    public virtual void OnLanded()
    {
        if (!_hasImpacted) TriggerImpact(null);

        gameObject.layer = _originalLayer;
        if (_sr != null && !string.IsNullOrEmpty(_originalSortingLayerName))
            _sr.sortingLayerName = _originalSortingLayerName;

        if (_agent != null)
        {
            _agent.enabled = true;
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 1.0f, UnityEngine.AI.NavMesh.AllAreas))
                _agent.Warp(hit.position);
        }

        _activeCombination = null;
        _isCombinationLead = false;
        _combinationSupporters = null;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.linearDamping = _originalDamping;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        }
        if (_collider != null) _collider.isTrigger = false;

        // [중요] 착지 즉시 브레인 재가동
        if (_runtimeBrain != null) _runtimeBrain.Init(this);
        
        Debug.Log($"<color=green>[AllyController]</color> {gameObject.name} 착지 및 AI 재가동 완료");
    }

    #endregion

    public void SetBattleState(bool _set)
    {
        isBattle = _set;
    }
}
